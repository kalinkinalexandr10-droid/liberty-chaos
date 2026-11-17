// NpcSpawner — меню призыва любых NPC GTA IV с выбором количества.
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Клавиши (по умолчанию; F7, B, G, N, C, Delete меняются в Mod menu settings — F9):
//   F7                 — открыть/закрыть меню
//   Left / Right       — категория (сюжетные, катсцены, мужчины, женщины...)
//   Up / Down          — выбор (PageUp/PageDown — по 10)
//   + / -              — количество (1..50), и на цифровом блоке, и в основном ряду
//   Enter              — призвать выбранного NPC в выбранном количестве
//   Delete             — убрать всех призванных этим меню
//   B                  — ДРАКА: все призванные дерутся друг с другом
//   G                  — ОХРАНА: призванные получают случайное оружие, ходят за тобой,
//                        садятся к тебе в машину; кому не хватило места — угоняют машину
//                        и едут следом; бьют всех, кто напал на тебя или на них, и копов,
//                        пока у тебя есть розыск
//   N                  — успокоить всех (снять режимы)
//   C                  — убрать ВСЕХ NPC в мире (кроме нужных текущей миссии)
//   Backspace          — закрыть меню
// Превью: выбранный NPC появляется перед игроком и медленно крутится (вместо фото).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;

public class NpcSpawner : Script
{
    // ===== ОБЩИЙ БЛОК: настройки (scripts\ModMenu.ini), язык, шрифт, уведомления =====
    class Cfg
    {
        public readonly Dictionary<string, string> V = new Dictionary<string, string>();
        readonly string path;
        DateTime stamp = DateTime.MinValue;
        int nextCheck = 0;
        public Cfg() { path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\ModMenu.ini"); Reload(true); }
        public bool Reload(bool force)
        {
            int now = Environment.TickCount;
            if (!force && now < nextCheck) return false;
            nextCheck = now + 1000;
            try
            {
                if (!File.Exists(path)) { if (V.Count > 0) { V.Clear(); return true; } return force; }
                DateTime t = File.GetLastWriteTimeUtc(path);
                if (!force && t == stamp) return false;
                stamp = t;
                V.Clear();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#") || l.StartsWith(";") || l.StartsWith("[")) continue;
                    int i = l.IndexOf('=');
                    if (i > 0) V[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                }
                return true;
            }
            catch (Exception) { return false; }
        }
        public string Get(string k, string def) { string v; return V.TryGetValue(k, out v) && v.Length > 0 ? v : def; }
        public int Int(string k, int def, int min, int max) { int v; if (!int.TryParse(Get(k, ""), out v)) v = def; return Math.Max(min, Math.Min(max, v)); }
        public Keys Key(string k, Keys def) { try { return (Keys)Enum.Parse(typeof(Keys), Get(k, def.ToString()), true); } catch (Exception) { return def; } }
        public bool Ru { get { return Get("Language", "EN").ToUpper() == "RU"; } }
    }
    readonly Cfg cfg = new Cfg();
    string T(string en, string ru) { return cfg.Ru ? ru : en; }

    GTA.Font fTitle, fRow, fSmall;
    void InitFonts()
    {
        fTitle = new GTA.Font("Arial", 0.030f, FontScaling.ScreenUnits, true, false);
        fRow = new GTA.Font("Arial", 0.024f, FontScaling.ScreenUnits, false, false);
        fSmall = new GTA.Font("Arial", 0.019f, FontScaling.ScreenUnits, false, false);
        // без тёмного контура: на выделенной (оранжевой) строке он сливался с чёрными буквами
        fTitle.Effect = FontEffect.None; fRow.Effect = FontEffect.None; fSmall.Effect = FontEffect.None;
    }

    string toast = null; int toastUntil = 0;
    const float ToastY = 0.91f;
    void Notify(string text, int ms) { toast = text; toastUntil = Environment.TickCount + ms; }
    void DrawToast(GTA.Graphics g)
    {
        if (toast == null || Environment.TickCount > toastUntil) return;
        g.Scaling = FontScaling.ScreenUnits;
        RectangleF r = new RectangleF(0.30f, ToastY, 0.40f, 0.042f);
        g.DrawRectangle(r, Color.FromArgb(200, 0, 0, 0));
        g.DrawText(toast, r, TextAlignment.Center | TextAlignment.VerticalCenter, Color.White, fRow);
    }
    // ===== конец общего блока =====

    const int VisibleRows = 14;
    const int MaxCountCap = 50;
    Keys keyMenu = Keys.F7, keyFight = Keys.B, keyGuard = Keys.G, keyHunt = Keys.H, keyCalm = Keys.N, keyClearAll = Keys.C, keyRemoveMine = Keys.Delete;
    int npcLimit = 150; // сколько призванных NPC держится в мире; лишние (самые старые) удаляются
    int MaxCount { get { return Math.Min(MaxCountCap, npcLimit); } }

    void ApplyConfig()
    {
        keyMenu = cfg.Key("Key_NpcMenu", Keys.F7);
        keyFight = cfg.Key("Key_NpcFight", Keys.B);
        keyGuard = cfg.Key("Key_NpcGuard", Keys.G);
        keyHunt = cfg.Key("Key_NpcHunt", Keys.H);
        keyCalm = cfg.Key("Key_NpcCalm", Keys.N);
        keyClearAll = cfg.Key("Key_NpcClearAll", Keys.C);
        keyRemoveMine = cfg.Key("Key_NpcRemoveMine", Keys.Delete);
        npcLimit = cfg.Int("NpcLimit", 150, 5, 300);
        if (count > MaxCount) count = MaxCount;
    }

    string CatName(string c)
    {
        if (!cfg.Ru) return c;
        switch (c)
        {
            case "Story characters": return "Сюжетные персонажи";
            case "Cutscene characters": return "Персонажи катсцен";
            case "Men (young)": return "Мужчины (молодые)";
            case "Men (middle-aged)": return "Мужчины (средних лет)";
            case "Men (old)": return "Мужчины (пожилые)";
            case "Women (young)": return "Женщины (молодые)";
            case "Women (middle-aged)": return "Женщины (средних лет)";
            case "Women (old)": return "Женщины (пожилые)";
        }
        return c;
    }

    class Entry { public string Model; public string Name; }
    class Category { public string Name; public List<Entry> Items = new List<Entry>(); }

    readonly List<Category> categories = new List<Category>();
    readonly List<Ped> spawned = new List<Ped>();
    readonly string modDir;
    readonly Random rnd = new Random();

    bool menuOpen = false;
    int catIndex = 0;
    int selected = 0;
    int scroll = 0;
    int count = 1;

    // ---------- живое превью ----------
    // Фото для 400+ обычных NPC нигде нет, поэтому показываем саму модель:
    // выбранный NPC стоит перед игроком и медленно крутится, как на витрине.
    const int PreviewDelayMs = 300;   // не грузим модель на каждое нажатие при быстрой прокрутке
    const float PreviewTurnSpeed = 45f;
    Ped preview = null;
    string previewModel = null;
    int lastSelChange = 0;
    int lastTick = 0;

    public NpcSpawner()
    {
        modDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\NpcSpawner");
        InitFonts();
        ApplyConfig();
        Load();
        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
        Interval = 0;
        lastTick = Environment.TickCount;
        this.Tick += new EventHandler(OnTick);
    }

    // ================= РЕЖИМЫ: ДРАКА И ОХРАНА =================
    class Guard
    {
        public Ped P; public int NextOrder; public string State = ""; public Ped Target; public Vehicle Car; public int OrderSince;
        public VehicleSeat Seat = VehicleSeat.None;
        // для определения «застрял»
        public Vector3 LastPos; public int LastMoveTime; public Vehicle BadCar;
    }

    // режим нападения: те же NPC, те же механики машин, но цель — Нико
    bool huntMode = false;
    static readonly RelationshipGroup HunterGroup = RelationshipGroup.Mission_8; // своя группа для охотников
    const int StuckMs = 7000;          // не сдвинулся дальше StuckMove за это время — застрял
    const float StuckMove = 2.5f;
    const float AttackOnFootRange = 120f; // ближе — нападают пешком, дальше — ищут машину
    const float HunterTeleportRange = 400f; // пеший охотник дальше этого — переносим ближе к Нико

    const float CatchUpDistance = 250f; // водитель-охранник отстал дальше — переносим его машину на дорогу за игроком

    // кэш всех NPC вокруг, обновляется раз за проход охраны (World.GetPeds(радиус) в GTA IV
    // возвращает лишь несколько ближайших — из-за этого охрана «не видела» копов дальше пары метров)
    Ped[] pedCache = new Ped[0];
    Vehicle lastPlayerCar = null;
    bool lastPlayerInCar = false;

    void RefreshPedCache()
    {
        try { pedCache = World.GetAllPeds(); } catch (Exception) { pedCache = new Ped[0]; }
    }

    const float CopSearchRadius = 200f;   // как далеко охрана ищет копов при розыске (было 80)
    const float EnemyScanRadius = 150f;   // в каком радиусе запоминаем обидчиков (было 50)
    const float EnemyForgetRadius = 300f; // дальше — враг забывается (было 120)

    // модели полиции GTA IV (на случай, если RelationshipGroup у NPC не Cop)
    static readonly string[] CopModels = { "M_Y_COP", "M_Y_COP_TRAFFIC", "M_M_FATCOP_01", "M_Y_STROOPER", "M_Y_SWAT", "M_M_FBI", "M_Y_NHELIPILOT" };
    Model[] copModelCache = null;

    bool IsCop(Ped p)
    {
        try
        {
            if (p.RelationshipGroup == RelationshipGroup.Cop) return true;
            if (copModelCache == null)
            {
                copModelCache = new Model[CopModels.Length];
                for (int i = 0; i < CopModels.Length; i++) copModelCache[i] = new Model(CopModels[i]);
            }
            Model m = p.Model;
            foreach (Model c in copModelCache) if (m == c) return true;
        }
        catch (Exception) { }
        return false;
    }

    // ближайший живой коп к точке (только когда у игрока розыск)
    Ped NearestCop(Vector3 from, float radius)
    {
        Ped best = null; float bd = radius;
        Ped me = Player.Character;
        foreach (Ped p in pedCache)
        {
            try
            {
                if (!Alive(p) || p == me) continue;
                float d = (p.Position - from).Length();
                if (d >= bd || !IsCop(p) || IsOurs(p)) continue;
                bd = d; best = p;
            }
            catch (Exception) { }
        }
        return best;
    }
    class Fighter { public Ped P; public Ped Target; public int NextCheck; }

    readonly List<Guard> guards = new List<Guard>();
    readonly List<Fighter> fighters = new List<Fighter>();
    readonly List<Ped> enemies = new List<Ped>();
    int nextBehaviorTick = 0;
    bool copsHated = false;

    static readonly Weapon[] GuardWeapons = {
        Weapon.Handgun_Glock, Weapon.Handgun_DesertEagle, Weapon.Shotgun_Basic, Weapon.Shotgun_Baretta,
        Weapon.SMG_Uzi, Weapon.SMG_MP5, Weapon.Rifle_AK47, Weapon.Rifle_M4 };

    static bool Alive(Ped p)
    {
        try { return p != null && p.Exists() && p.isAlive; } catch (Exception) { return false; }
    }

    // в SHDN оператор == сравнивает игровой handle, т.е. это тот же самый NPC, даже если обёртки разные
    static bool Same(Ped a, Ped b)
    {
        if ((object)a == null || (object)b == null) return false;
        return a == b;
    }

    bool IsOurs(Ped p)
    {
        foreach (Ped s in spawned) if (Same(s, p)) return true;
        return Same(p, preview);
    }

    void ClearModes(bool calm)
    {
        foreach (Guard g in guards)
        {
            try
            {
                if (!Alive(g.P)) continue;
                g.P.ChangeRelationship(RelationshipGroup.Cop, Relationship.Neutral);
                if (huntMode) g.P.ChangeRelationship(RelationshipGroup.Player, Relationship.Neutral);
                if (calm) g.P.Task.ClearAll();
            }
            catch (Exception) { }
        }
        if (huntMode)
        {
            // группа охотников снова нейтральна к игроку
            try
            {
                Function.Call("SET_RELATIONSHIP", (int)Relationship.Neutral, (int)HunterGroup, (int)RelationshipGroup.Player);
                Function.Call("SET_RELATIONSHIP", (int)Relationship.Neutral, (int)RelationshipGroup.Player, (int)HunterGroup);
            }
            catch (Exception) { }
        }
        huntMode = false;
        foreach (Fighter f in fighters) { try { if (calm && Alive(f.P)) f.P.Task.ClearAll(); } catch (Exception) { } }
        guards.Clear();
        fighters.Clear();
        enemies.Clear();
        copsHated = false;
    }

    // --- ДРАКА ---
    void StartFight()
    {
        ClearModes(false);
        foreach (Ped p in spawned)
        {
            if (!Alive(p)) continue;
            Fighter f = new Fighter(); f.P = p;
            try { p.BlockPermanentEvents = false; } catch (Exception) { }
            fighters.Add(f);
        }
        if (fighters.Count < 2) { fighters.Clear(); Notify(T("Need at least 2 spawned NPC to fight", "Для драки нужно хотя бы 2 призванных NPC"), 2000); return; }
        foreach (Fighter f in fighters) AssignFightTarget(f);
        Notify(T("FIGHT! ", "ДРАКА! ") + fighters.Count + " NPC", 2000);
    }

    void AssignFightTarget(Fighter f)
    {
        List<Ped> pool = new List<Ped>();
        foreach (Fighter o in fighters) if (o != f && Alive(o.P)) pool.Add(o.P);
        if (pool.Count == 0) { f.Target = null; return; }
        f.Target = pool[rnd.Next(pool.Count)];
        try { f.P.Task.FightAgainst(f.Target, -1); } catch (Exception) { }
    }

    void UpdateFighters(int now)
    {
        int alive = 0;
        foreach (Fighter f in fighters) if (Alive(f.P)) alive++;
        if (alive < 2) { if (fighters.Count > 0) Notify(T("Fight is over", "Драка окончена"), 2000); fighters.Clear(); return; }
        foreach (Fighter f in fighters)
        {
            if (!Alive(f.P) || now < f.NextCheck) continue;
            f.NextCheck = now + 1000;
            if (!Alive(f.Target)) AssignFightTarget(f); // соперник выбыл — ищем нового
        }
    }

    // --- ОХРАНА ---
    void StartGuards()
    {
        ClearModes(false);
        foreach (Ped p in spawned)
        {
            if (!Alive(p)) continue;
            Guard g = new Guard(); g.P = p;
            try
            {
                Weapon w = GuardWeapons[rnd.Next(GuardWeapons.Length)];
                Function.Call("GIVE_WEAPON_TO_CHAR", p, (int)w, 9999, false);
                p.Weapons.FromType(w).Select();
                p.ChangeRelationship(RelationshipGroup.Player, Relationship.Companion);
                p.CantBeDamagedByRelationshipGroup(RelationshipGroup.Player, true); // ты их случайно не убьёшь
                p.BlockPermanentEvents = true;  // не разбегаются от выстрелов, слушают только нас
                p.Accuracy = 70;
                p.WillDoDrivebys = true;
                p.CanBeDraggedOutOfVehicle = false;
            }
            catch (Exception ex) { Game.Console.Print("NpcSpawner guard setup: " + ex.Message); }
            guards.Add(g);
        }
        if (guards.Count == 0) { Notify(T("Spawn some NPC first", "Сначала призови NPC"), 2000); return; }
        Notify(T("Bodyguards: ", "Охрана: ") + guards.Count, 2000);
    }

    // --- НАПАДЕНИЕ НА НИКО ---
    void StartHunt()
    {
        ClearModes(false);
        foreach (Ped p in spawned)
        {
            if (!Alive(p)) continue;
            Guard g = new Guard(); g.P = p;
            try
            {
                Weapon w = GuardWeapons[rnd.Next(GuardWeapons.Length)];
                Function.Call("GIVE_WEAPON_TO_CHAR", p, (int)w, 9999, false);
                p.Weapons.FromType(w).Select();
                // их можно убить: снимаем всё, что могло остаться от режима охраны или превью
                p.Invincible = false;
                p.CantBeDamagedByRelationshipGroup(RelationshipGroup.Player, false);
                p.MakeProofTo(false, false, false, false, false); // урон от пуль, огня, взрывов, ударов машиной и рукопашки
                p.AlwaysDiesOnLowHealth = true;
                p.Health = 200;
                // отдельная «вражеская» группа: игрок и охотники взаимно враги —
                // прицел Нико захватывает их как врагов (красный маркер), и они атакуют Нико
                p.RelationshipGroup = HunterGroup;
                p.ChangeRelationship(RelationshipGroup.Player, Relationship.Hate);
                try
                {
                    Function.Call("SET_RELATIONSHIP", (int)Relationship.Hate, (int)HunterGroup, (int)RelationshipGroup.Player);
                    Function.Call("SET_RELATIONSHIP", (int)Relationship.Hate, (int)RelationshipGroup.Player, (int)HunterGroup);
                }
                catch (Exception) { }
                p.BlockPermanentEvents = true;  // не убегают, слушают только нас
                p.Accuracy = 45;
                p.WillDoDrivebys = true;        // стреляют из машины
                p.CanBeDraggedOutOfVehicle = true;
            }
            catch (Exception ex) { Game.Console.Print("NpcSpawner hunter setup: " + ex.Message); }
            guards.Add(g);
        }
        if (guards.Count == 0) { Notify(T("Spawn some NPC first", "Сначала призови NPC"), 2000); return; }
        huntMode = true;
        lastPlayerInCar = false; lastPlayerCar = null;
        Notify(T("They are coming for you: ", "Охота на тебя: ") + guards.Count + T(" attackers", " нападающих"), 2500);
    }

    // застрял: больше StuckMs не сдвинулся — бросает машину (и запоминает её как «плохую») и ищет другую
    void CheckStuck(Guard g, Ped me, int now)
    {
        try
        {
            Vector3 pos = g.P.Position;
            if (g.LastMoveTime == 0 || (pos - g.LastPos).Length() > StuckMove) { g.LastPos = pos; g.LastMoveTime = now; return; }
            if (now - g.LastMoveTime < StuckMs) return;
            g.LastPos = pos; g.LastMoveTime = now;

            // стоять на месте нормально: рядом с Нико, едет в его машине или стоит возле него
            if ((pos - me.Position).Length() < 25f) return;
            if (g.State == "riding" || g.State == "idle") return;

            Vehicle pv = g.P.isInVehicle() ? g.P.CurrentVehicle : null;
            if (pv != null) { g.BadCar = pv; g.P.Task.LeaveVehicleImmediately(pv); }
            else g.P.Task.ClearAll();
            g.State = "stuck"; g.Car = null; g.Seat = VehicleSeat.None;
            g.NextOrder = now + 600; // дать выйти и сразу перепланировать
        }
        catch (Exception) { }
    }

    // пеший охотник очень далеко — переносим на тротуар/дорогу в ~60 м позади Нико
    void BringCloser(Guard g, Ped me)
    {
        try
        {
            Vector3 behind = me.GetOffsetPosition(new Vector3(0f, -60f, 0f));
            Vector3 p = World.GetNextPositionOnStreet(behind);
            if ((p - behind).Length() < 80f) { g.P.Position = p; g.LastMoveTime = 0; }
        }
        catch (Exception) { }
    }

    void UpdateHunters(int now)
    {
        guards.RemoveAll(delegate(Guard g) { return !Alive(g.P); });
        if (guards.Count == 0) { huntMode = false; Notify(T("All attackers are down", "Все нападающие выбыли"), 2500); return; }
        Ped me = Player.Character;
        if (!Alive(me)) return;

        Vehicle myCar = null;
        try { if (me.isInVehicle()) myCar = me.CurrentVehicle; } catch (Exception) { }
        bool inCar = myCar != null;

        // Нико сел/вышел/пересел — все сразу перепланируют погоню
        if (inCar != lastPlayerInCar || (inCar && myCar != lastPlayerCar))
        {
            foreach (Guard g in guards) if (g.State != "steal") g.NextOrder = 0;
            lastPlayerInCar = inCar;
            lastPlayerCar = myCar;
        }

        List<Vehicle> claimed = new List<Vehicle>();
        foreach (Guard g in guards) if (g.State == "steal" && g.Car != null) claimed.Add(g.Car);
        foreach (Guard g in guards) CheckBoarding(g, now);

        foreach (Guard g in guards)
        {
            CheckStuck(g, me, now);
            if (now < g.NextOrder) continue;
            Ped p = g.P;
            try
            {
                float dist = (p.Position - me.Position).Length();
                Vehicle pv = p.isInVehicle() ? p.CurrentVehicle : null;

                // всегда знают, где Нико: далеко отставший пешком — переносим ближе
                if (pv == null && dist > HunterTeleportRange) { BringCloser(g, me); g.NextOrder = now + 500; continue; }

                if (pv != null)
                {
                    g.Car = null; g.Seat = VehicleSeat.None;
                    // Нико пешком и близко — выходим и нападаем
                    if (!inCar && dist < 35f)
                    {
                        if (g.State != "exit") { p.Task.LeaveVehicle(); g.State = "exit"; }
                        g.NextOrder = now + 800;
                        continue;
                    }
                    // за рулём — гонимся за Нико (пассажиры в это время стреляют из окон)
                    if (IsDriver(pv, p)) { FollowPlayer(g, pv, me, dist, now); continue; }
                    if (pv.GetPedOnSeat(VehicleSeat.Driver) == null)
                    {
                        try { Function.Call("TASK_SHUFFLE_TO_NEXT_CAR_SEAT", p, pv); } catch (Exception) { }
                        g.State = "to-driver"; g.NextOrder = now + 2000;
                    }
                    else { g.State = "passenger"; g.NextOrder = now + 1000; }
                    continue;
                }

                // пешком: Нико рядом — нападаем (по машине тоже стреляют)
                float attackRange = inCar ? 30f : AttackOnFootRange;
                if (dist < attackRange)
                {
                    if (g.State != "attack" || now - g.OrderSince > 4000)
                    {
                        p.Task.FightAgainst(me, -1);
                        g.State = "attack"; g.Car = null; g.Seat = VehicleSeat.None; g.OrderSince = now;
                    }
                    g.NextOrder = now + 700;
                    continue;
                }

                // далеко — нужна машина
                if ((g.State == "enter-friend" || g.State == "steal") && g.Car != null) { g.NextOrder = now + 400; continue; }

                // место в машине другого охотника
                Vehicle friend = null; float fd = 60f; VehicleSeat seat = VehicleSeat.None;
                foreach (Guard o in guards)
                {
                    if (o == g) continue;
                    Vehicle ov = null;
                    try { ov = o.P.isInVehicle() ? o.P.CurrentVehicle : (o.State == "steal" ? o.Car : null); } catch (Exception) { }
                    if (ov == null || !ov.Exists() || ov == myCar || ov == g.BadCar) continue;
                    float d = (ov.Position - p.Position).Length();
                    if (d >= fd) continue;
                    VehicleSeat s = ReserveSeat(ov, g);
                    if (s == VehicleSeat.None) continue;
                    friend = ov; fd = d; seat = s;
                }
                if (friend != null) { p.Task.EnterVehicle(friend, seat); Order(g, "enter-friend", friend, seat, now); continue; }

                Vehicle steal = FindCarToSteal(p.Position, myCar, claimed, g.BadCar);
                if (steal != null)
                {
                    claimed.Add(steal);
                    p.Task.EnterVehicle(steal, VehicleSeat.Driver);
                    Order(g, "steal", steal, VehicleSeat.Driver, now);
                    continue;
                }
                p.Task.RunTo(me.Position);
                g.State = "run";
                g.NextOrder = now + 1500;
            }
            catch (Exception ex) { Game.Console.Print("NpcSpawner hunter: " + ex.Message); g.NextOrder = now + 1500; }
        }
    }

    // кто напал на игрока или на охрану — враг
    void CollectEnemies(Ped me)
    {
        Vector3 mePos = me.Position;
        enemies.RemoveAll(delegate(Ped p) { return !Alive(p) || (p.Position - mePos).Length() > EnemyForgetRadius; });
        foreach (Ped p in pedCache)
        {
            try { if (!Alive(p) || (p.Position - mePos).Length() > EnemyScanRadius) continue; } catch (Exception) { continue; }
            if (Same(p, me) || IsOurs(p)) continue;
            bool hostile = false;
            try
            {
                if (me.HasBeenDamagedBy(p)) hostile = true;
                foreach (Guard g in guards) if (Alive(g.P) && g.P.HasBeenDamagedBy(p)) { hostile = true; break; }
                if (copsHated && IsCop(p)) hostile = true;
            }
            catch (Exception) { }
            if (!hostile) continue;
            bool known = false;
            foreach (Ped e in enemies) if (Same(e, p)) { known = true; break; }
            if (!known) enemies.Add(p);
        }
        // сбрасываем «кто ударил», чтобы не путать старые удары с новыми
        try { Function.Call("CLEAR_CHAR_LAST_DAMAGE_ENTITY", me); } catch (Exception) { }
        foreach (Guard g in guards) { try { if (Alive(g.P)) Function.Call("CLEAR_CHAR_LAST_DAMAGE_ENTITY", g.P); } catch (Exception) { } }
    }

    Ped NearestEnemy(Vector3 from)
    {
        Ped best = null; float bd = float.MaxValue;
        foreach (Ped e in enemies)
        {
            if (!Alive(e)) continue;
            float d = (e.Position - from).Length();
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    void UpdateGuards(int now)
    {
        guards.RemoveAll(delegate(Guard g) { return !Alive(g.P); });
        if (guards.Count == 0) return;
        Ped me = Player.Character;
        if (!Alive(me)) return;

        // копы — враги только пока у тебя есть розыск
        bool wanted = false;
        try { wanted = Player.WantedLevel > 0; } catch (Exception) { }
        if (wanted != copsHated)
        {
            copsHated = wanted;
            foreach (Guard g in guards)
                try { g.P.ChangeRelationship(RelationshipGroup.Cop, wanted ? Relationship.Hate : Relationship.Neutral); } catch (Exception) { }
        }

        RefreshPedCache();
        CollectEnemies(me);

        Vehicle myCar = null;
        try { if (me.isInVehicle()) myCar = me.CurrentVehicle; } catch (Exception) { }
        bool inCar = myCar != null;

        // игрок сел в машину / вышел / пересел — вся охрана перепланирует СРАЗУ, а не через пару секунд
        if (inCar != lastPlayerInCar || (inCar && myCar != lastPlayerCar))
        {
            foreach (Guard g in guards)
            {
                g.NextOrder = 0;
                if (g.State != "riding") { g.State = ""; g.Car = null; }
            }
            lastPlayerInCar = inCar;
            lastPlayerCar = myCar;
        }

        // машины, которые уже угоняет кто-то из охраны (чтобы двое не бежали к одной)
        List<Vehicle> claimed = new List<Vehicle>();
        foreach (Guard g in guards) if (g.State == "steal" && g.Car != null) claimed.Add(g.Car);

        // охранник бежит к машине, а его место заняли / машина пропала — сразу ищет другую
        foreach (Guard g in guards) CheckBoarding(g, now);

        foreach (Guard g in guards)
        {
            CheckStuck(g, me, now);
            if (now < g.NextOrder) continue;
            Ped p = g.P;
            try
            {
                float dist = (p.Position - me.Position).Length();
                Vehicle pv = p.isInVehicle() ? p.CurrentVehicle : null;

                if (!inCar)
                {
                    // ===== игрок пешком: сначала бой, потом следование =====
                    // при розыске — ближайший коп в радиусе, иначе ближайший обидчик
                    Ped enemy = null;
                    if (copsHated) enemy = NearestCop(p.Position, CopSearchRadius);
                    if (enemy == null) enemy = NearestEnemy(p.Position);
                    if (enemy != null && (enemy.Position - p.Position).Length() < CopSearchRadius)
                    {
                        if (g.State != "fight" || !Alive(g.Target) || !Same(g.Target, enemy))
                        {
                            if (pv != null) p.Task.LeaveVehicle();
                            p.Task.FightAgainst(enemy, -1);
                            g.Target = enemy;
                            g.State = "fight";
                        }
                        g.NextOrder = now + 800;
                        continue;
                    }
                    if (g.State == "fight") { g.State = ""; g.Target = null; }

                    if (pv != null)
                    {
                        if (dist < 25f) { if (g.State != "exit") { p.Task.LeaveVehicle(); g.State = "exit"; } }
                        else if (IsDriver(pv, p)) { p.Task.DriveTo(pv, me, 25f, false); g.State = "drive"; }
                    }
                    else if (dist > 15f) { p.Task.RunTo(me.GetOffsetPosition(new Vector3(0f, -2f, 0f))); g.State = "run"; }
                    else if (dist > 3.5f) { p.Task.GoTo(me, 0f, -2f); g.State = "walk"; }
                    else if (g.State != "idle") { p.Task.ClearAll(); g.State = "idle"; }
                    g.Car = null;
                    g.NextOrder = now + 1000;
                    continue;
                }

                // ===== игрок в машине: срочно в машину и за ним (стреляют уже из окон) =====
                // занял место — сидит, пока игрок не выйдет (выходят все вместе, ветка «пешком» выше)
                if (pv != null && pv == myCar) { g.State = "riding"; g.Car = null; g.Seat = VehicleSeat.None; g.NextOrder = now + 1500; continue; }
                if (pv != null)
                {
                    g.Car = null; g.Seat = VehicleSeat.None;
                    if (IsDriver(pv, p)) { FollowPlayer(g, pv, me, dist, now); continue; }
                    // пассажир: если водителя нет (убили/выпал) — пересаживается за руль
                    if (pv.GetPedOnSeat(VehicleSeat.Driver) == null)
                    {
                        try { Function.Call("TASK_SHUFFLE_TO_NEXT_CAR_SEAT", p, pv); } catch (Exception) { }
                        g.State = "to-driver";
                        g.NextOrder = now + 2000;
                    }
                    else { g.State = "passenger"; g.NextOrder = now + 1500; }
                    continue;
                }

                // уже бежит к своему забронированному месту — не перебиваем (проверку «место заняли» делает CheckBoarding)
                if ((g.State == "enter-player" || g.State == "enter-friend" || g.State == "steal") && g.Car != null)
                { g.NextOrder = now + 400; continue; }

                // 1) своё (забронированное) место у игрока
                VehicleSeat seat = VehicleSeat.None;
                if (dist < 50f) seat = ReserveSeat(myCar, g);
                if (seat != VehicleSeat.None)
                {
                    p.Task.EnterVehicle(myCar, seat);
                    Order(g, "enter-player", myCar, seat, now);
                    continue;
                }
                // 2) место в машине, которую ведёт или угоняет кто-то из охраны (ближайшая со свободным креслом)
                Vehicle friend = null; float fd = 60f;
                foreach (Guard o in guards)
                {
                    if (o == g) continue;
                    Vehicle ov = null;
                    try { ov = o.P.isInVehicle() ? o.P.CurrentVehicle : (o.State == "steal" ? o.Car : null); } catch (Exception) { }
                    if (ov == null || !ov.Exists() || ov == myCar) continue;
                    float d = (ov.Position - p.Position).Length();
                    if (d >= fd) continue;
                    VehicleSeat s = ReserveSeat(ov, g);
                    if (s == VehicleSeat.None) continue;
                    friend = ov; fd = d; seat = s;
                }
                if (friend != null)
                {
                    p.Task.EnterVehicle(friend, seat);
                    Order(g, "enter-friend", friend, seat, now);
                    continue;
                }
                // 3) угоняем ближайшую свободную от охраны машину (водителя выкидывает)
                Vehicle steal = FindCarToSteal(p.Position, myCar, claimed, g.BadCar);
                if (steal != null)
                {
                    claimed.Add(steal);
                    p.Task.EnterVehicle(steal, VehicleSeat.Driver);
                    Order(g, "steal", steal, VehicleSeat.Driver, now);
                    continue;
                }
                p.Task.RunTo(me.Position);
                g.State = "run";
                g.NextOrder = now + 1000;
            }
            catch (Exception ex) { Game.Console.Print("NpcSpawner guard: " + ex.Message); g.NextOrder = now + 1500; }
        }
    }

    void Order(Guard g, string state, Vehicle car, VehicleSeat seat, int now)
    {
        g.State = state; g.Car = car; g.Seat = seat; g.OrderSince = now; g.NextOrder = now + 400;
    }

    static readonly VehicleSeat[] PassengerSeatOrder = { VehicleSeat.RightFront, VehicleSeat.LeftRear, VehicleSeat.RightRear };

    // свободное и никем из охраны не забронированное пассажирское место
    VehicleSeat ReserveSeat(Vehicle v, Guard me)
    {
        try
        {
            if (v == null || !v.Exists() || !v.isAlive) return VehicleSeat.None;
            int count = Math.Min(PassengerSeatOrder.Length, v.PassengerSeats);
            for (int i = 0; i < count; i++)
            {
                VehicleSeat s = PassengerSeatOrder[i];
                if (!v.isSeatFree(s)) continue;
                bool reserved = false;
                foreach (Guard o in guards)
                    if (o != me && o.Car != null && o.Car == v && o.Seat == s) { reserved = true; break; }
                if (!reserved) return s;
            }
        }
        catch (Exception) { }
        return VehicleSeat.None;
    }

    // пока охранник бежит к машине: если его место заняли, машина пропала/разбита или он завис — отменяем и ищем заново
    void CheckBoarding(Guard g, int now)
    {
        if (g.Car == null || !(g.State == "enter-player" || g.State == "enter-friend" || g.State == "steal")) return;
        try
        {
            if (g.P.isInVehicle()) return; // уже сел — дальше разберётся основной цикл
            bool cancel = false;
            if (!g.Car.Exists() || !g.Car.isAlive || !g.Car.isDriveable) cancel = true;
            else if (now - g.OrderSince > 10000) cancel = true;
            else if (g.Seat == VehicleSeat.Driver)
            {
                Ped d = g.Car.GetPedOnSeat(VehicleSeat.Driver);
                if (d != null && IsOurs(d)) cancel = true; // за руль уже сел другой охранник
            }
            else if (g.Seat != VehicleSeat.None && !g.Car.isSeatFree(g.Seat)) cancel = true; // кресло заняли
            if (cancel)
            {
                g.P.Task.ClearAll();
                g.State = ""; g.Car = null; g.Seat = VehicleSeat.None;
                g.NextOrder = 0; // перепланировать в этом же проходе
            }
        }
        catch (Exception) { g.State = ""; g.Car = null; g.Seat = VehicleSeat.None; g.NextOrder = 0; }
    }

    // водитель-охранник всегда едет к Нико: маршрут обновляется каждую секунду, сильно отставших переносим к игроку
    void FollowPlayer(Guard g, Vehicle pv, Ped me, float dist, int now)
    {
        try
        {
            if (dist > CatchUpDistance)
            {
                Vector3 behind = me.GetOffsetPosition(new Vector3(0f, -25f, 0f));
                Vector3 road = World.GetNextPositionOnStreet(behind);
                if ((road - behind).Length() < 60f)
                {
                    pv.Position = road;
                    pv.Heading = me.Heading;
                    pv.PlaceOnGroundProperly();
                }
            }
            float speed = dist > 80f ? 60f : dist > 25f ? 40f : 20f;
            g.P.Task.DriveTo(pv, me, speed, false);
            g.State = "follow-car";
        }
        catch (Exception) { }
        g.NextOrder = now + 1000;
    }

    static bool IsDriver(Vehicle v, Ped p)
    {
        try { Ped d = v.GetPedOnSeat(VehicleSeat.Driver); return d != null && Same(d, p); } catch (Exception) { return false; }
    }

    // сколько пассажирских мест свободно
    static int FreeSeats(Vehicle v)
    {
        int n = 0;
        try
        {
            VehicleSeat[] seats = { VehicleSeat.RightFront, VehicleSeat.LeftRear, VehicleSeat.RightRear };
            int count = Math.Min(seats.Length, v.PassengerSeats);
            for (int i = 0; i < count; i++) if (v.isSeatFree(seats[i])) n++;
        }
        catch (Exception) { }
        return n;
    }

    // сколько охранников уже идут садиться в эту машину
    int CountHeading(Vehicle v, string state)
    {
        int n = 0;
        foreach (Guard g in guards) if (g.State == state && g.Car != null && g.Car == v) n++;
        return n;
    }

    Vehicle FindCarToSteal(Vector3 from, Vehicle exclude, List<Vehicle> claimed, Vehicle badCar)
    {
        Vehicle[] cars;
        try { cars = World.GetVehicles(from, 80f); } catch (Exception) { return null; }
        Vehicle best = null; float bd = float.MaxValue;
        foreach (Vehicle v in cars)
        {
            try
            {
                if (v == null || !v.Exists() || !v.isDriveable || v == exclude) continue;
                if (badCar != null && v == badCar) continue; // на ней уже застревали
                try { if (v.Model.isHelicopter || v.Model.isBoat || v.Model.isTrain) continue; } catch (Exception) { }
                if (exclude != null && (v.Position - exclude.Position).Length() < 0.5f) continue;
                bool taken = false;
                foreach (Vehicle c in claimed) if (c == v || (c.Position - v.Position).Length() < 0.5f) { taken = true; break; }
                if (taken) continue;
                // не берём машины, где уже сидит кто-то из охраны
                Ped drv = v.GetPedOnSeat(VehicleSeat.Driver);
                if (drv != null && IsOurs(drv)) continue;
                float d = (v.Position - from).Length();
                if (d < bd) { bd = d; best = v; }
            }
            catch (Exception) { }
        }
        return best;
    }

    void UpdateBehaviors(int now)
    {
        if (now < nextBehaviorTick) return;
        nextBehaviorTick = now + 250; // 4 раза в секунду хватает и не грузит игру
        if (fighters.Count > 0) UpdateFighters(now);
        if (guards.Count > 0) { if (huntMode) UpdateHunters(now); else UpdateGuards(now); }
    }

    void OnTick(object sender, EventArgs e)
    {
        int now = Environment.TickCount;
        float dt = Math.Min(0.1f, (now - lastTick) / 1000f);
        lastTick = now;

        if (cfg.Reload(false)) { ApplyConfig(); EnforceLimit(); }
        UpdateBehaviors(now);

        if (!menuOpen || categories.Count == 0) { RemovePreview(); return; }

        List<Entry> items = categories[catIndex].Items;
        if (selected >= items.Count) return;
        string want = items[selected].Model;

        if (want != previewModel && now - lastSelChange >= PreviewDelayMs)
        {
            RemovePreview();
            CreatePreview(want);
        }

        try
        {
            if (preview != null && preview.Exists()) preview.Heading = preview.Heading + PreviewTurnSpeed * dt;
        }
        catch (Exception) { }
    }

    void CreatePreview(string model)
    {
        previewModel = model; // даже при неудаче — чтобы не пытаться каждый кадр
        try
        {
            Ped me = Player.Character;
            if (me == null || !me.Exists()) return;
            float dist = me.isInVehicle() ? 6f : 3f;
            Vector3 p = me.GetOffsetPosition(new Vector3(0f, dist, 0f));
            float gz = World.GetGroundZ(new Vector3(p.X, p.Y, p.Z + 1.5f));
            if (gz != 0f && Math.Abs(gz - p.Z) < 4f) p = new Vector3(p.X, p.Y, gz);
            preview = World.CreatePed(new Model(model), p);
            if (preview == null || !preview.Exists()) { preview = null; return; }
            preview.Heading = me.Heading + 180f;     // лицом к игроку
            preview.Invincible = true;
            preview.BlockPermanentEvents = true;     // не убегает и не реагирует
            preview.Task.StandStill(-1);
        }
        catch (Exception ex) { preview = null; Game.Console.Print("NpcSpawner preview: " + ex.Message); }
    }

    void RemovePreview()
    {
        try { if (preview != null && preview.Exists()) preview.Delete(); } catch (Exception) { }
        preview = null;
        previewModel = null;
    }

    void Load()
    {
        string file = Path.Combine(modDir, "npcs.txt");
        try
        {
            if (File.Exists(file))
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    Entry e = new Entry();
                    e.Model = p[0].Trim().ToUpper();
                    e.Name = p.Length > 1 ? p[1].Trim() : e.Model;
                    GetCategory(p.Length > 2 ? p[2].Trim() : "NPC").Items.Add(e);
                }
            }
        }
        catch (Exception ex) { Game.Console.Print("NpcSpawner: npcs.txt error: " + ex.Message); }

        if (categories.Count == 0)
        {
            foreach (string m in new string[] { "IG_ROMAN", "IG_BRUCIE", "IG_LITTLEJACOB", "M_Y_COP" })
            { Entry e = new Entry(); e.Model = m; e.Name = m; GetCategory("NPC").Items.Add(e); }
        }
    }

    Category GetCategory(string name)
    {
        foreach (Category c in categories) if (c.Name == name) return c;
        Category n = new Category(); n.Name = name; categories.Add(n); return n;
    }

    // ---------- Призыв ----------
    void Spawn(Entry e, int n)
    {
        Ped me = Player.Character;
        if (me == null || !me.Exists()) return;
        int ok = 0;
        try
        {
            Model model = new Model(e.Model);
            for (int i = 0; i < n; i++)
            {
                // по кругу вокруг игрока, 3..6 м, чтобы не появлялись друг в друге
                double a = 2 * Math.PI * i / Math.Max(1, n) + rnd.NextDouble() * 0.3;
                float r = 3f + (float)rnd.NextDouble() * 3f + (n > 12 ? i % 3 : 0);
                Vector3 pos = me.GetOffsetPosition(new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0f));
                Ped p = World.CreatePed(model, pos);
                if (p == null || !p.Exists()) continue;
                p.Heading = (float)rnd.NextDouble() * 360f;
                spawned.Add(p);
                ok++;
            }
        }
        catch (Exception ex) { Notify(T("Spawn error: ", "Ошибка спавна: ") + ex.Message, 3000); }

        EnforceLimit();

        if (ok == 0) Notify(T("Failed to spawn ", "Не удалось призвать ") + e.Model, 2000);
        else Notify(e.Name + " x" + ok + T(" spawned", " — призваны"), 1500);
    }

    // держим не больше npcLimit призванных; самых старых удаляем
    void EnforceLimit()
    {
        spawned.RemoveAll(delegate(Ped p) { try { return p == null || !p.Exists(); } catch (Exception) { return true; } });
        while (spawned.Count > npcLimit)
        {
            Ped old = spawned[0];
            spawned.RemoveAt(0);
            guards.RemoveAll(delegate(Guard g) { return Same(g.P, old); });
            fighters.RemoveAll(delegate(Fighter f) { return Same(f.P, old); });
            try { if (old != null && old.Exists()) old.Delete(); } catch (Exception) { }
        }
    }

    void DeleteSpawned()
    {
        ClearModes(false);
        int n = 0;
        foreach (Ped p in spawned)
        {
            try { if (p != null && p.Exists()) { p.Delete(); n++; } } catch (Exception) { }
        }
        spawned.Clear();
        Notify(T("Removed ", "Убрано ") + n + " NPC", 1500);
    }

    // Убрать ВСЕХ NPC в мире: прохожих, водителей, копов и призванных.
    // Персонажей, нужных текущей миссии, не трогаем, чтобы не сломать сюжет.
    void ClearAllWorldPeds()
    {
        ClearModes(false);
        RemovePreview();
        Ped me = Player.Character;
        int n = 0;
        Ped[] all;
        try { all = World.GetAllPeds(); } catch (Exception) { all = new Ped[0]; }
        foreach (Ped p in all)
        {
            try
            {
                if (p == null || !p.Exists() || p == me) continue;
                if (p.isRequiredForMission && !IsOurs(p)) continue;
                p.Delete();
                n++;
            }
            catch (Exception) { }
        }
        spawned.Clear();
        Notify(T("Cleared ", "Убрано из мира: ") + n + " NPC", 2000);
    }

    // ---------- Ввод ----------
    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (e.Key == keyMenu) { menuOpen = !menuOpen; return; }
        if (!menuOpen || categories.Count == 0) return;

        List<Entry> items = categories[catIndex].Items;
        lastSelChange = Environment.TickCount;

        // настраиваемые клавиши
        if (e.Key == keyRemoveMine) { DeleteSpawned(); return; }
        if (e.Key == keyFight) { StartFight(); return; }
        if (e.Key == keyGuard) { StartGuards(); return; }
        if (e.Key == keyHunt) { StartHunt(); return; }
        if (e.Key == keyCalm) { ClearModes(true); Notify(T("NPC calmed down", "NPC успокоились"), 1500); return; }
        if (e.Key == keyClearAll) { ClearAllWorldPeds(); return; }

        switch (e.Key)
        {
            case Keys.Left: catIndex = (catIndex - 1 + categories.Count) % categories.Count; selected = 0; scroll = 0; return;
            case Keys.Right: catIndex = (catIndex + 1) % categories.Count; selected = 0; scroll = 0; return;
            case Keys.Up: selected = (selected - 1 + items.Count) % items.Count; break;
            case Keys.Down: selected = (selected + 1) % items.Count; break;
            case Keys.PageUp: selected = Math.Max(0, selected - 10); break;
            case Keys.PageDown: selected = Math.Min(items.Count - 1, selected + 10); break;
            case Keys.Add:
            case Keys.Oemplus: count = Math.Min(MaxCount, count + 1); break;
            case Keys.Subtract:
            case Keys.OemMinus: count = Math.Max(1, count - 1); break;
            case Keys.Enter: Spawn(items[selected], count); break;
            case Keys.Back: menuOpen = false; break;
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
    }

    // ---------- Отрисовка (по центру, чтобы не перекрывать меню машин и оружия) ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        DrawToast(g);
        if (!menuOpen || categories.Count == 0) return;
        g.Scaling = FontScaling.ScreenUnits;

        Category cat = categories[catIndex];
        List<Entry> items = cat.Items;
        if (selected >= items.Count) selected = items.Count - 1;

        float w = 0.30f, x = 0.5f - w / 2f, y = 0.21f, rowH = 0.032f;

        g.DrawRectangle(new RectangleF(x, y - 0.12f, w, 0.12f + rowH * VisibleRows + 0.115f), Color.FromArgb(190, 0, 0, 0));
        g.DrawText(T("NPC SPAWNER", "ПРИЗЫВ NPC"), x + 0.01f, y - 0.117f, Color.Orange, fTitle);
        g.DrawText("<  " + CatName(cat.Name).ToUpper() + "  (" + (catIndex + 1) + "/" + categories.Count + ")  >", x + 0.01f, y - 0.077f, Color.White, fRow);

        // количество
        g.DrawRectangle(new RectangleF(x + 0.01f, y - 0.042f, w - 0.02f, 0.032f), Color.FromArgb(120, 255, 140, 0));
        g.DrawText(T("Count:  -  ", "Количество:  -  ") + count + "  +   (max " + MaxCount + ")", x + 0.02f, y - 0.038f, Color.White, fRow);

        for (int i = 0; i < VisibleRows && scroll + i < items.Count; i++)
        {
            int idx = scroll + i;
            float ry = y + i * rowH;
            if (idx == selected) g.DrawRectangle(new RectangleF(x, ry, w, rowH), Color.FromArgb(200, 255, 140, 0));
            g.DrawText(items[idx].Name, x + 0.01f, ry + 0.004f, idx == selected ? Color.Black : Color.White, fRow);
        }

        float by = y + rowH * VisibleRows + 0.006f;
        g.DrawText((selected + 1) + "/" + items.Count + "   " + items[selected].Model + T("   alive: ", "   в мире: ") + spawned.Count + "/" + npcLimit, x + 0.01f, by, Color.LightGray, fSmall);
        g.DrawText(T("Enter spawn   +/- count   ", "Enter — призвать   +/- — кол-во   ") + keyRemoveMine + T(" remove mine", " — убрать своих"), x + 0.01f, by + 0.027f, Color.LightGray, fSmall);
        g.DrawText(keyFight + T(" fight  ", " — драка  ") + keyGuard + T(" guards  ", " — охрана  ") + keyHunt + T(" hunt me  ", " — охота на меня  ") + keyCalm + T(" calm  ", " — успокоить  ") + keyClearAll + T(" clear ALL", " — убрать ВСЕХ"),
            x + 0.01f, by + 0.054f, Color.Orange, fSmall);
        string mode = fighters.Count > 0 ? T("FIGHT (", "ДРАКА (") + fighters.Count + ")"
                    : guards.Count > 0 ? (huntMode ? T("HUNTING YOU (", "ОХОТА НА ТЕБЯ (") : T("GUARDS (", "ОХРАНА (")) + guards.Count + ")"
                    : T("no mode", "режим не включён");
        g.DrawText(T("Mode: ", "Режим: ") + mode, x + 0.01f, by + 0.081f, Color.White, fSmall);
    }
}

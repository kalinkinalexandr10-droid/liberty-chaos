// FlyMode — полёт для GTA IV и сброс всякого с высоты.
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Управление:
//   F8              — взлететь / приземлиться (только пешком)
//   W / S           — вперёд / назад по направлению камеры (по горизонтали)
//   A / D           — влево / вправо
//   Space / Ctrl    — вверх / вниз
//   Shift           — ускорение
//   Q               — выбрать, что сбрасывать: бомба / огненная бомба / машина / NPC
//   X               — сбросить
// Во время полёта и до первого касания земли после него ты бессмертен — разбиться нельзя.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;

public class FlyMode : Script
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
        // основной цвет меню (MenuColor, меняется в F9). Тот же список — в ModSettings.
        static readonly string[] ColorIds = { "Orange", "Red", "Pink", "Purple", "Blue", "Cyan", "Green", "Yellow", "White" };
        static readonly Color[] ColorVals = {
            Color.FromArgb(255, 140, 0), Color.FromArgb(220, 40, 40), Color.FromArgb(255, 80, 160), Color.FromArgb(150, 80, 230),
            Color.FromArgb(40, 120, 255), Color.FromArgb(0, 200, 220), Color.FromArgb(60, 200, 80), Color.FromArgb(255, 210, 0), Color.FromArgb(230, 230, 230) };
        public Color Accent { get { string id = Get("MenuColor", "Orange"); for (int i = 0; i < ColorIds.Length; i++) if (string.Compare(ColorIds[i], id, true) == 0) return ColorVals[i]; return ColorVals[0]; } }
        // текст на выделенной строке: чёрный на светлом цвете, белый на тёмном
        public Color SelText { get { Color c = Accent; return c.R * 0.299f + c.G * 0.587f + c.B * 0.114f > 140f ? Color.Black : Color.White; } }
    }
    readonly Cfg cfg = new Cfg();
    string T(string en, string ru) { return cfg.Ru ? ru : en; }

    GTA.Font fRow;
    string toast = null; int toastUntil = 0;
    const float ToastY = 0.76f;
    void Notify(string text, int ms) { toast = text; toastUntil = Environment.TickCount + ms; }
    void DrawToast(GTA.Graphics g)
    {
        if (toast == null || Environment.TickCount > toastUntil) return;
        g.Scaling = FontScaling.ScreenUnits;
        RectangleF r = new RectangleF(0.25f, ToastY, 0.50f, 0.042f);
        g.DrawRectangle(r, Color.FromArgb(200, 0, 0, 0));
        g.DrawText(toast, r, TextAlignment.Center | TextAlignment.VerticalCenter, Color.White, fRow);
    }
    // ===== конец общего блока =====

    Keys ToggleKey = Keys.F8, DropKey = Keys.X, DropTypeKey = Keys.Q;
    int vehicleLimit = 20, npcLimit = 150;
    readonly List<Vehicle> droppedCars = new List<Vehicle>();
    readonly List<Ped> droppedPeds = new List<Ped>();

    void ApplyConfig()
    {
        ToggleKey = cfg.Key("Key_Fly", Keys.F8);
        DropKey = cfg.Key("Key_FlyDrop", Keys.X);
        DropTypeKey = cfg.Key("Key_FlyDropType", Keys.Q);
        vehicleLimit = cfg.Int("VehicleLimit", 20, 1, 100);
        npcLimit = cfg.Int("NpcLimit", 150, 5, 300);
    }

    string DropName(int i)
    {
        if (!cfg.Ru) return DropNames[i];
        string[] ru = { "БОМБА", "ОГНЕННАЯ БОМБА", "МАШИНА", "NPC" };
        return ru[i];
    }

    // сброшенные машины и NPC тоже подчиняются лимитам из настроек
    void EnforceDropLimits()
    {
        droppedCars.RemoveAll(delegate(Vehicle v) { try { return v == null || !v.Exists(); } catch (Exception) { return true; } });
        droppedPeds.RemoveAll(delegate(Ped p) { try { return p == null || !p.Exists(); } catch (Exception) { return true; } });
        while (droppedCars.Count > vehicleLimit) { try { droppedCars[0].Delete(); } catch (Exception) { } droppedCars.RemoveAt(0); }
        while (droppedPeds.Count > npcLimit) { try { droppedPeds[0].Delete(); } catch (Exception) { } droppedPeds.RemoveAt(0); }
    }

    const float FlySpeed = 15f;      // м/с
    const float BoostSpeed = 55f;    // м/с с Shift
    const float BombPower = 6f;

    static readonly string[] DropNames = { "BOMB", "FIRE BOMB", "CAR", "NPC" };
    static readonly string[] DropCars = { "INFERNUS", "TURISMO", "BUS", "TAXI", "POLICE", "AMBULANCE", "FIRETRUK", "STRETCH", "MRTASTY", "BENSON", "PATRIOT", "BANSHEE" };
    static readonly string[] DropPeds = { "M_Y_COP", "M_Y_SWAT", "M_Y_GANGELS_01", "M_M_FATCOP_01", "F_Y_TOURIST_01", "M_Y_TOURIST_02", "M_M_BUSINESS_02", "IG_ROMAN", "IG_BRUCIE", "M_M_TRAMP_01" };

    class Bomb { public GTA.Object Obj; public Vector3 Pos; public Vector3 Vel; public bool Fire; public int Born; public float Ground; }

    // Попала ли бомба в машину. Раньше проверялась только земля: бомба без коллизии пролетала
    // сквозь крышу, а изнутри кузова «земля» не находилась — бомба проваливалась и пропадала без взрыва.
    static bool HitsVehicle(Vector3 p)
    {
        Vehicle[] cars;
        try { cars = World.GetVehicles(p, 8f); } catch (Exception) { return false; }
        if (cars == null) return false;
        foreach (Vehicle v in cars)
        {
            try
            {
                if (v == null || !v.Exists()) continue;
                Vector3 size = v.Model.GetDimensions();
                Vector3 local = v.GetOffset(p);          // точка бомбы в координатах машины
                if (Math.Abs(local.X) <= size.X * 0.5f + 0.2f &&
                    Math.Abs(local.Y) <= size.Y * 0.5f + 0.2f &&
                    Math.Abs(local.Z) <= size.Z * 0.5f + 0.2f) return true;
            }
            catch (Exception) { }
        }
        return false;
    }

    bool flying = false;
    bool landingProtect = false;
    int landingSince = 0;
    int dropType = 0;
    Vector3 pos;                 // желаемая позиция в «пространстве чтения» Position
    Vector3 vel;
    const float MaxFlyHeight = 1500f;
    int lastTick;
    // точный таймер: Environment.TickCount шагает по ~16 мс, из-за этого шаги были неравные и камеру дёргало
    readonly System.Diagnostics.Stopwatch frameTimer = System.Diagnostics.Stopwatch.StartNew();
    double lastFrameSec = 0;
    readonly List<Bomb> bombs = new List<Bomb>();
    readonly Random rnd = new Random();
    readonly GTA.Font hudFont;

    public FlyMode()
    {
        hudFont = new GTA.Font("Arial", 18f, FontScaling.Pixel, false, false);
        fRow = new GTA.Font("Arial", 0.024f, FontScaling.ScreenUnits, false, false);
        fRow.Effect = FontEffect.None;          // уведомление на тёмной плашке — контур не нужен
        hudFont.Effect = FontEffect.Shadow;     // строка полёта поверх мира — лёгкая тень для читаемости
        ApplyConfig();
        Interval = 0;
        lastTick = Environment.TickCount;
        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.Tick += new EventHandler(OnTick);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
    }

    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (e.Key == ToggleKey) { if (flying) StopFlying(); else StartFlying(); return; }
        if (!flying) return;
        if (e.Key == DropTypeKey) { dropType = (dropType + 1) % DropNames.Length; Notify(T("Drop: ", "Сброс: ") + DropName(dropType), 1000); }
        else if (e.Key == DropKey) Drop();
    }

    void StartFlying()
    {
        Ped me = Player.Character;
        if (me == null || !me.Exists() || !me.isAlive) return;
        if (me.isInVehicle()) { Notify(T("Get out of the vehicle to fly", "Чтобы летать, выйди из машины"), 2000); return; }
        pos = me.Position + new Vector3(0f, 0f, 1f); // чуть приподнимаемся
        vel = new Vector3(0f, 0f, 0f);
        me.FreezePosition = true;
        me.Invincible = true;
        flying = true;
        landingProtect = false;
        Notify(T("FLY: WASD + Space/Ctrl, Shift fast, ", "ПОЛЁТ: WASD + Space/Ctrl, Shift быстрее, ") + DropTypeKey + T(" type, ", " — что, ") + DropKey + T(" drop, ", " — сброс, ") + ToggleKey + T(" land", " — сесть"), 4000);
    }

    void StopFlying()
    {
        flying = false;
        Ped me = Player.Character;
        try
        {
            if (me != null && me.Exists())
            {
                me.FreezePosition = false;
                me.Velocity = vel * 0.3f;
                landingProtect = true;   // бессмертие до приземления
                landingSince = Environment.TickCount;
            }
        }
        catch (Exception) { }
    }

    static Vector3 Normalize(Vector3 v)
    {
        float l = v.Length();
        return l < 0.0001f ? new Vector3(0f, 0f, 0f) : v * (1f / l);
    }

    void OnTick(object sender, EventArgs e)
    {
        int now = Environment.TickCount;
        lastTick = now;
        double t = frameTimer.Elapsed.TotalSeconds;
        float dt = (float)(t - lastFrameSec);
        lastFrameSec = t;
        if (dt <= 0f) return;
        if (dt > 0.1f) dt = 0.1f;

        if (cfg.Reload(false)) { ApplyConfig(); EnforceDropLimits(); }
        UpdateBombs(dt, now);

        Ped me = Player.Character;

        // защита от разбивания после приземления
        if (landingProtect && me != null && me.Exists())
        {
            bool onGround = false;
            try { onGround = !me.isInAir && now - landingSince > 400; } catch (Exception) { }
            if ((onGround && now - landingSince > 1500) || now - landingSince > 20000)
            {
                try { me.Invincible = false; } catch (Exception) { }
                landingProtect = false;
            }
        }

        if (!flying) return;
        if (me == null || !me.Exists() || !me.isAlive || me.isInVehicle()) { StopFlying(); return; }

        // направление камеры — только по горизонтали. Наклон камеры больше не учитываем:
        // на скорости камера отстаёт и задирается, полёт уходил вверх, камера задиралась ещё сильнее — и по кругу.
        // Высота меняется только Space / Ctrl.
        Vector3 fwd = new Vector3(0f, 1f, 0f);
        float camHeading = me.Heading;
        try
        {
            Camera cam = Game.CurrentCamera;
            Vector3 d = cam.Direction;
            Vector3 flat = Normalize(new Vector3(d.X, d.Y, 0f));
            if (flat.Length() > 0.5f) fwd = flat;
            camHeading = cam.Heading;
        }
        catch (Exception) { }
        Vector3 right = new Vector3(fwd.Y, -fwd.X, 0f);

        Vector3 dir = new Vector3(0f, 0f, 0f);
        if (isKeyPressed(Keys.W)) dir = dir + fwd;
        if (isKeyPressed(Keys.S)) dir = dir - fwd;
        if (isKeyPressed(Keys.D)) dir = dir + right;
        if (isKeyPressed(Keys.A)) dir = dir - right;
        if (isKeyPressed(Keys.Space)) dir = dir + new Vector3(0f, 0f, 1f);
        if (isKeyPressed(Keys.ControlKey)) dir = dir - new Vector3(0f, 0f, 1f);
        dir = Normalize(dir);

        float speed = isKeyPressed(Keys.ShiftKey) ? BoostSpeed : FlySpeed;
        Vector3 targetVel = dir * speed;
        // плавный разгон/торможение
        float k = Math.Min(1f, dt * 4f);
        vel = vel + (targetVel - vel) * k;
        Vector3 next = pos + vel * dt;

        // Не уходим под землю. ВАЖНО: раньше землю «щупали» от точки выше Нико — проверка натыкалась
        // на тело самого Нико, считала его землёй и каждый кадр выталкивала вверх (тот самый улёт).
        // Теперь щупаем строго НИЖЕ НОГ и только запрещаем опускаться — вверх эта проверка не толкает никогда.
        if (vel.Z < 0f)
        {
            try
            {
                float feet = me.Position.Z - 1.0f;                     // ноги ~на метр ниже точки персонажа
                float gz = World.GetGroundZ(new Vector3(next.X, next.Y, feet - 0.1f));
                float drop = pos.Z - next.Z;                           // на сколько опускаемся в этом кадре
                if (gz != 0f && feet - drop < gz + 0.3f)
                {
                    next = new Vector3(next.X, next.Y, pos.Z);         // остаёмся на текущей высоте
                    vel = new Vector3(vel.X, vel.Y, 0f);
                }
            }
            catch (Exception) { }
        }
        pos = next;

        if (pos.Z > MaxFlyHeight) { pos = new Vector3(pos.X, pos.Y, MaxFlyHeight); if (vel.Z > 0f) vel = new Vector3(vel.X, vel.Y, 0f); }

        // Ставим Нико прямо в pos. Никаких поправок по прочитанной высоте — любая «обратная связь»
        // от Position раскачивала полёт. Небольшой постоянный сдвиг по высоте в воздухе не мешает.
        me.Position = pos;
        me.Heading = camHeading;
    }

    // ---------- Сброс ----------
    void Drop()
    {
        Ped me = Player.Character;
        Vector3 below = pos - new Vector3(0f, 0f, 2.5f);
        try
        {
            switch (dropType)
            {
                case 0:
                case 1:
                {
                    Bomb b = new Bomb();
                    b.Pos = below;
                    b.Vel = new Vector3(vel.X, vel.Y, Math.Min(0f, vel.Z));
                    b.Fire = dropType == 1;
                    b.Born = Environment.TickCount;
                    try
                    {
                        b.Obj = World.CreateObject(new Model(b.Fire ? "w_molotov" : "w_grenade"), below);
                        if (b.Obj != null && b.Obj.Exists()) { b.Obj.Collision = false; b.Obj.FreezePosition = true; }
                    }
                    catch (Exception) { b.Obj = null; } // нет модели — бомба просто невидимая
                    bombs.Add(b);
                    break;
                }
                case 2:
                {
                    Vehicle v = World.CreateVehicle(new Model(DropCars[rnd.Next(DropCars.Length)]), pos - new Vector3(0f, 0f, 5f));
                    if (v != null && v.Exists())
                    {
                        v.Heading = (float)rnd.NextDouble() * 360f;
                        v.Velocity = new Vector3(vel.X, vel.Y, -5f);
                        droppedCars.Add(v);
                        EnforceDropLimits();
                    }
                    break;
                }
                case 3:
                {
                    Ped p = World.CreatePed(new Model(DropPeds[rnd.Next(DropPeds.Length)]), pos - new Vector3(0f, 0f, 3.5f));
                    if (p != null && p.Exists())
                    {
                        p.Velocity = new Vector3(vel.X, vel.Y, -3f);
                        p.ForceRagdoll(8000, false);
                        droppedPeds.Add(p);
                        EnforceDropLimits();
                    }
                    break;
                }
            }
        }
        catch (Exception ex) { Notify(T("Drop error: ", "Ошибка сброса: ") + ex.Message, 2000); }
    }

    void UpdateBombs(float dt, int now)
    {
        for (int i = bombs.Count - 1; i >= 0; i--)
        {
            Bomb b = bombs[i];
            b.Vel = new Vector3(b.Vel.X * 0.995f, b.Vel.Y * 0.995f, b.Vel.Z - 9.8f * dt);
            Vector3 next = b.Pos + b.Vel * dt;
            float gz = 0f;
            try { gz = World.GetGroundZ(new Vector3(next.X, next.Y, b.Pos.Z + 0.5f)); } catch (Exception) { }
            if (gz != 0f) b.Ground = gz; else gz = b.Ground;   // земля не нашлась — берём последнюю известную
            bool hitCar = HitsVehicle(next);
            bool hit = !hitCar && gz != 0f && next.Z <= gz + 0.15f;
            if (hitCar || hit || now - b.Born > 20000)
            {
                Vector3 at = hit ? new Vector3(next.X, next.Y, gz + 0.3f) : next;
                try { World.AddExplosion(at, b.Fire ? ExplosionType.Molotov : ExplosionType.Rocket, BombPower); } catch (Exception) { }
                try { if (b.Obj != null && b.Obj.Exists()) b.Obj.Delete(); } catch (Exception) { }
                bombs.RemoveAt(i);
                continue;
            }
            b.Pos = next;
            try { if (b.Obj != null && b.Obj.Exists()) b.Obj.Position = next; } catch (Exception) { }
        }
    }

    // ---------- HUD ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        DrawToast(g);
        if (!flying) return;
        g.Scaling = FontScaling.Pixel;
        Size res = Game.Resolution;
        float s = res.Height / 1080f;
        int alt = 0;
        try
        {
            float gz = World.GetGroundZ(new Vector3(pos.X, pos.Y, Player.Character.Position.Z - 1.1f)); // ниже ног, чтобы не «увидеть» самого Нико
            if (gz != 0f) alt = (int)(pos.Z - gz);
        }
        catch (Exception) { }
        g.DrawText(T("FLYING   alt ", "ПОЛЁТ   высота ") + alt + T(" m   ", " м   ") + (int)(vel.Length() * 3.6f) + T(" km/h   |   DROP [", " км/ч   |   СБРОС [") + DropTypeKey + "]: " + DropName(dropType)
            + "   " + DropKey + T(" - drop   ", " — сбросить   ") + ToggleKey + T(" - land", " — приземлиться"),
            new RectangleF(20f * s, res.Height - 60f * s, res.Width - 40f * s, 30f * s), TextAlignment.Left, cfg.Accent, hudFont);
    }
}

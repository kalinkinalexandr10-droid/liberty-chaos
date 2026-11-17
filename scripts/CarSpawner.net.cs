// CarSpawner — меню спавна всего транспорта GTA IV с фото-превью.
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Клавиши (по умолчанию, меняются в Mod menu settings — F9):
//   F5             — открыть/закрыть меню
//   Left / Right   — категория
//   Up / Down      — выбор (PageUp/PageDown — по 10)
//   Enter          — заспавнить и сесть за руль
//   R              — случайный транспорт из ВСЕХ категорий
//   Backspace      — закрыть меню
// Лимит машин (VehicleLimit) — сколько заспавненных машин держится в мире; лишние, самые старые, удаляются.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;

public class CarSpawner : Script
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

    // обычный (не жирный) Arial — буквы не слипаются, и умеет кириллицу
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
    const float ToastY = 0.86f;
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
    Keys keyMenu = Keys.F5, keyRandom = Keys.R;
    int vehicleLimit = 20;

    class CarEntry { public string Model; public string Name; public bool IsRandom; }
    class Category { public string Name; public List<CarEntry> Items = new List<CarEntry>(); }

    readonly Random rnd = new Random();
    readonly List<Category> categories = new List<Category>();
    readonly List<Vehicle> spawnedCars = new List<Vehicle>();
    readonly Dictionary<string, Texture> previews = new Dictionary<string, Texture>();
    readonly Dictionary<string, bool> missingPreviews = new Dictionary<string, bool>(); // HashSet недоступен (нет System.Core)
    readonly string modDir;

    bool menuOpen = false;
    int catIndex = 0;
    int selected = 0;
    int scroll = 0;

    public CarSpawner()
    {
        modDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\CarSpawner");
        InitFonts();
        ApplyConfig();
        LoadCars();
        foreach (Category c in categories)
        {
            CarEntry r = new CarEntry(); r.IsRandom = true; r.Model = "";
            c.Items.Insert(0, r);
        }
        Interval = 100;
        this.Tick += new EventHandler(OnTick);
        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
    }

    void ApplyConfig()
    {
        keyMenu = cfg.Key("Key_CarMenu", Keys.F5);
        keyRandom = cfg.Key("Key_RandomCar", Keys.R);
        vehicleLimit = cfg.Int("VehicleLimit", 20, 1, 100);
    }

    void OnTick(object sender, EventArgs e)
    {
        if (cfg.Reload(false)) { ApplyConfig(); EnforceLimit(); }
        ProcessPendingWarp();
    }

    // ---------- безопасная посадка ----------
    // Ped.WarpIntoVehicle (нативная WARP_CHAR_INTO_CAR) на Steam-версии 1.2.0.59 иногда падает ошибкой
    // доступа к памяти (по логу — на фургонах и др.). Её нельзя поймать try/catch: SHDN выключал весь скрипт.
    // Поэтому никаких телепортов в машину: машина появляется вплотную, водительской дверью к Нико,
    // и он садится обычной игровой посадкой (Task.EnterVehicle) — она скрипт никогда не роняла.
    Vehicle pendingWarp = null;
    int pendingSince = 0;
    int warpStage = 0; // 0 — ждём выхода из старой машины, 2 — обычная посадка выдана

    void ProcessPendingWarp()
    {
        if (pendingWarp == null) return;
        int now = Environment.TickCount;
        Ped me = Player.Character;
        try
        {
            if (now - pendingSince > 8000 || me == null || !me.Exists() || !me.isAlive ||
                !pendingWarp.Exists() || !pendingWarp.isAlive || !pendingWarp.isDriveable)
            { pendingWarp = null; return; }
            if (me.isInVehicle(pendingWarp)) { pendingWarp = null; return; } // сели — готово
            if (warpStage == 0)
            {
                if (me.isInVehicle()) return;                     // ещё выходит из старой машины
                if (now - pendingSince < 300) return;             // даём новой машине «встать»
                if (pendingWarp.GetPedOnSeat(VehicleSeat.Driver) != null) { pendingWarp = null; return; }
                me.Task.EnterVehicle(pendingWarp, VehicleSeat.Driver);
                warpStage = 2;
                pendingSince = now;
            }
        }
        catch (Exception) { pendingWarp = null; }
    }

    string CatName(string c)
    {
        if (!cfg.Ru) return c;
        switch (c)
        {
            case "Cars": return "Машины";
            case "Bikes": return "Мотоциклы";
            case "Helicopters": return "Вертолёты";
            case "Boats": return "Лодки";
        }
        return c;
    }

    string EntryName(Category c, CarEntry e)
    {
        if (!e.IsRandom) return e.Name;
        return T("* Random " + c.Name.ToLower() + " *", "* Случайно: " + CatName(c.Name).ToLower() + " *");
    }

    // ---------- Список ----------
    void LoadCars()
    {
        string file = Path.Combine(modDir, "cars.txt");
        try
        {
            if (File.Exists(file))
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    CarEntry c = new CarEntry();
                    c.Model = parts[0].Trim().ToUpper();
                    c.Name = parts.Length > 1 ? parts[1].Trim() : c.Model;
                    string cat = parts.Length > 2 ? parts[2].Trim() : "Vehicles";
                    GetCategory(cat).Items.Add(c);
                }
            }
        }
        catch (Exception ex) { Game.Console.Print("CarSpawner: cars.txt error: " + ex.Message); }

        if (categories.Count == 0)
        {
            foreach (string m in new string[] { "INFERNUS", "TURISMO", "COMET", "SULTANRS", "BANSHEE" })
            {
                CarEntry c = new CarEntry(); c.Model = m; c.Name = m;
                GetCategory("Cars").Items.Add(c);
            }
        }
    }

    Category GetCategory(string name)
    {
        foreach (Category c in categories) if (c.Name == name) return c;
        Category n = new Category(); n.Name = name;
        categories.Add(n);
        return n;
    }

    Texture GetPreview(string model)
    {
        Texture t;
        if (previews.TryGetValue(model, out t)) return t;
        if (missingPreviews.ContainsKey(model)) return null;
        foreach (string ext in new string[] { ".png", ".jpg", ".dds" })
        {
            string path = Path.Combine(modDir, "images\\" + model + ext);
            if (!File.Exists(path)) continue;
            try { t = new Texture(File.ReadAllBytes(path)); previews[model] = t; return t; }
            catch (Exception ex) { Game.Console.Print("CarSpawner: can't load " + path + ": " + ex.Message); }
        }
        missingPreviews[model] = true;
        return null;
    }

    // ---------- Ввод ----------
    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (e.Key == keyMenu) { menuOpen = !menuOpen; return; }
        if (!menuOpen || categories.Count == 0) return;

        List<CarEntry> items = categories[catIndex].Items;
        if (e.Key == keyRandom) { SpawnCar(PickRandom(null)); return; }
        switch (e.Key)
        {
            case Keys.Left: SwitchCategory(-1); return;
            case Keys.Right: SwitchCategory(+1); return;
            case Keys.Up: selected = (selected - 1 + items.Count) % items.Count; break;
            case Keys.Down: selected = (selected + 1) % items.Count; break;
            case Keys.PageUp: selected = Math.Max(0, selected - 10); break;
            case Keys.PageDown: selected = Math.Min(items.Count - 1, selected + 10); break;
            case Keys.Enter: SpawnCar(items[selected].IsRandom ? PickRandom(categories[catIndex]) : items[selected]); break;
            case Keys.Back: menuOpen = false; break;
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
    }

    CarEntry PickRandom(Category cat)
    {
        List<CarEntry> pool = new List<CarEntry>();
        foreach (Category c in categories)
            if (cat == null || c == cat)
                foreach (CarEntry e in c.Items) if (!e.IsRandom) pool.Add(e);
        return pool.Count > 0 ? pool[rnd.Next(pool.Count)] : null;
    }

    void SwitchCategory(int dir)
    {
        catIndex = (catIndex + dir + categories.Count) % categories.Count;
        selected = 0;
        scroll = 0;
    }

    // ---------- Спавн и лимит ----------
    void SpawnCar(CarEntry car)
    {
        if (car == null) return;
        try
        {
            Ped me = Player.Character;
            if (me == null || !me.Exists() || !me.isAlive) return;
            if (pendingWarp != null) return; // прошлая посадка ещё не закончена
            Vehicle oldCar = me.isInVehicle() ? me.CurrentVehicle : null;
            // пешком — машина встаёт справа вплотную, водительская (левая) дверь к Нико;
            // в машине — чуть дальше вперёд-вправо, чтобы не врезаться
            Model model = new Model(car.Model);
            bool small = false;
            try { small = model.isCar || model.isBike; } catch (Exception) { }
            Vector3 pos = oldCar != null ? me.GetOffsetPosition(new Vector3(4f, 9f, 0f))
                        : small ? me.GetOffsetPosition(new Vector3(2.6f, 0.5f, 0f))   // машины и байки — вплотную справа
                        : me.GetOffsetPosition(new Vector3(0f, 8f, 0f));              // вертолёты и лодки — впереди, места больше
            Vehicle v = World.CreateVehicle(model, pos);
            if (v == null || !v.Exists()) { Notify(T("Failed to spawn ", "Не удалось заспавнить ") + car.Model, 2000); return; }
            v.Heading = me.Heading;
            v.PlaceOnGroundProperly();
            spawnedCars.Add(v);
            if (oldCar != null) me.Task.LeaveVehicleImmediately(oldCar); // сначала выходим из старой
            pendingWarp = v;                                             // посадка — в следующих тиках
            pendingSince = Environment.TickCount;
            warpStage = 0;
            EnforceLimit();
            menuOpen = false;
            Notify(car.Name + T(" spawned", " — готово"), 1500);
        }
        catch (Exception ex) { Notify(T("Spawn error: ", "Ошибка спавна: ") + ex.Message, 3000); }
    }

    // держим не больше vehicleLimit машин из меню; удаляем самые старые (кроме той, где сидит игрок)
    void EnforceLimit()
    {
        spawnedCars.RemoveAll(delegate(Vehicle v) { try { return v == null || !v.Exists(); } catch (Exception) { return true; } });
        Ped me = Player.Character;
        int i = 0;
        while (spawnedCars.Count > vehicleLimit && i < spawnedCars.Count)
        {
            Vehicle v = spawnedCars[i];
            bool mine = false;
            try { mine = (me != null && me.isInVehicle(v)) || v == pendingWarp; } catch (Exception) { }
            if (mine) { i++; continue; }
            try { v.Delete(); } catch (Exception) { }
            spawnedCars.RemoveAt(i);
        }
    }

    // ---------- Отрисовка ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        DrawToast(g);
        if (!menuOpen || categories.Count == 0) return;
        g.Scaling = FontScaling.ScreenUnits;

        Category cat = categories[catIndex];
        List<CarEntry> items = cat.Items;
        if (items.Count == 0) return;
        if (selected >= items.Count) selected = items.Count - 1;

        float x = 0.03f, y = 0.17f, w = 0.25f, rowH = 0.032f;

        g.DrawRectangle(new RectangleF(x, y - 0.08f, w, 0.08f + rowH * VisibleRows + 0.07f), Color.FromArgb(190, 0, 0, 0));
        g.DrawText(T("VEHICLES", "ТРАНСПОРТ"), x + 0.01f, y - 0.077f, Color.Orange, fTitle);
        g.DrawText("<  " + CatName(cat.Name).ToUpper() + "  (" + (catIndex + 1) + "/" + categories.Count + ")  >", x + 0.01f, y - 0.037f, Color.White, fRow);

        for (int i = 0; i < VisibleRows && scroll + i < items.Count; i++)
        {
            int idx = scroll + i;
            float ry = y + i * rowH;
            if (idx == selected) g.DrawRectangle(new RectangleF(x, ry, w, rowH), Color.FromArgb(200, 255, 140, 0));
            g.DrawText(EntryName(cat, items[idx]), x + 0.01f, ry + 0.004f, idx == selected ? Color.Black : Color.White, fRow);
        }

        float by = y + rowH * VisibleRows + 0.006f;
        g.DrawText((selected + 1) + "/" + items.Count + T("   Enter - spawn   ", "   Enter — спавн   ") + keyRandom + T(" - random", " — случайно"), x + 0.01f, by, Color.LightGray, fSmall);
        g.DrawText(T("Left/Right - category   Backspace - close", "Влево/вправо — категория   Backspace — закрыть"), x + 0.01f, by + 0.027f, Color.LightGray, fSmall);

        CarEntry cur = items[selected];
        float px = x + w + 0.01f, py = y - 0.08f, pw = 0.28f, ph = 0.25f; // ~2:1 на экране 16:9 — под картинки 512x256
        g.DrawRectangle(new RectangleF(px, py, pw, ph), Color.FromArgb(190, 0, 0, 0));
        Texture tex = cur.IsRandom ? null : GetPreview(cur.Model);
        RectangleF inner = new RectangleF(px + 0.005f, py + 0.005f, pw - 0.01f, ph - 0.01f);
        if (tex != null) g.DrawSprite(tex, inner);
        else g.DrawText(cur.IsRandom ? T("?  RANDOM  ?", "?  СЛУЧАЙНО  ?") : T("No photo", "Нет фото"), inner,
            TextAlignment.Center | TextAlignment.VerticalCenter, cur.IsRandom ? Color.Orange : Color.Gray, fTitle);
    }
}

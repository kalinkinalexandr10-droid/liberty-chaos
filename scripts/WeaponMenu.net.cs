// WeaponMenu — меню выдачи оружия GTA IV с максимальными патронами и картинками.
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Клавиши (по умолчанию, меняются в Mod menu settings — F9):
//   F6             — открыть/закрыть меню
//   Left / Right   — категория
//   Up / Down      — выбор
//   Enter          — получить оружие (с полным боезапасом) и взять в руки
//   Backspace      — закрыть меню
// В GTA IV в каждом слоте может быть только одно оружие — новое в том же слоте заменит старое.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;

public class WeaponMenu : Script
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
    const float ToastY = 0.81f;
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

    const int VisibleRows = 10;
    const int FallbackAmmo = 9999;
    const int PlayerMaxHealth = 200; // полная полоска здоровья игрока в GTA IV
    Keys keyMenu = Keys.F6;

    class Item { public string Key; public string Name; public string NameRu; public Weapon Type; public bool IsArmor; public bool IsHealth; public bool IsAll; }
    class Category { public string Name; public string NameRu; public List<Item> Items = new List<Item>(); }

    readonly List<Category> categories = new List<Category>();
    readonly Dictionary<string, Texture> previews = new Dictionary<string, Texture>();
    readonly Dictionary<string, bool> missingPreviews = new Dictionary<string, bool>();
    readonly string modDir;

    bool menuOpen = false;
    int catIndex = 0;
    int selected = 0;
    int scroll = 0;

    public WeaponMenu()
    {
        modDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\WeaponMenu");
        InitFonts();
        keyMenu = cfg.Key("Key_WeaponMenu", Keys.F6);
        BuildList();
        Interval = 250;
        this.Tick += new EventHandler(OnTick);
        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
    }

    void OnTick(object sender, EventArgs e)
    {
        if (cfg.Reload(false)) keyMenu = cfg.Key("Key_WeaponMenu", Keys.F6);
    }

    // ---------- Список ----------
    Item Add(string cat, string catRu, string key, string name, string nameRu, Weapon w)
    {
        Item it = new Item(); it.Key = key; it.Name = name; it.NameRu = nameRu; it.Type = w;
        GetCategory(cat, catRu).Items.Add(it);
        return it;
    }

    void BuildList()
    {
        Add("All", "Всё сразу", "ALL", "Everything: weapons + armor + health", "Всё: оружие + броня + здоровье", Weapon.Unarmed).IsAll = true;

        Add("Melee", "Ближний бой", "BAT", "Baseball Bat", "Бейсбольная бита", Weapon.Melee_BaseballBat);
        Add("Melee", "Ближний бой", "POOLCUE", "Pool Cue", "Бильярдный кий", Weapon.Melee_PoolCue);
        Add("Melee", "Ближний бой", "KNIFE", "Knife", "Нож", Weapon.Melee_Knife);

        Add("Thrown", "Метательное", "GRENADE", "Grenades", "Гранаты", Weapon.Thrown_Grenade);
        Add("Thrown", "Метательное", "MOLOTOV", "Molotov Cocktails", "Коктейли Молотова", Weapon.Thrown_Molotov);

        Add("Handguns", "Пистолеты", "PISTOL", "Pistol (Glock)", "Пистолет (Glock)", Weapon.Handgun_Glock);
        Add("Handguns", "Пистолеты", "COMBATPISTOL", "Combat Pistol (Desert Eagle)", "Боевой пистолет (Desert Eagle)", Weapon.Handgun_DesertEagle);

        Add("Shotguns", "Дробовики", "PUMPSHOTGUN", "Pump Shotgun", "Помповый дробовик", Weapon.Shotgun_Basic);
        Add("Shotguns", "Дробовики", "COMBATSHOTGUN", "Combat Shotgun", "Боевой дробовик", Weapon.Shotgun_Baretta);

        Add("SMG", "Пистолеты-пулемёты", "MICROSMG", "Micro SMG (Uzi)", "Микро-ПП (Uzi)", Weapon.SMG_Uzi);
        Add("SMG", "Пистолеты-пулемёты", "SMG", "SMG (MP5)", "ПП (MP5)", Weapon.SMG_MP5);

        Add("Rifles", "Автоматы", "ASSAULTRIFLE", "Assault Rifle (AK-47)", "Автомат (АК-47)", Weapon.Rifle_AK47);
        Add("Rifles", "Автоматы", "CARBINERIFLE", "Carbine Rifle (M4)", "Карабин (M4)", Weapon.Rifle_M4);

        Add("Snipers", "Снайперские", "COMBATSNIPER", "Combat Sniper", "Боевая снайперка", Weapon.SniperRifle_Basic);
        Add("Snipers", "Снайперские", "SNIPERRIFLE", "Sniper Rifle (M40A1)", "Снайперская винтовка (M40A1)", Weapon.SniperRifle_M40A1);

        Add("Heavy", "Тяжёлое", "RPG", "Rocket Launcher (RPG)", "Гранатомёт (РПГ)", Weapon.Heavy_RocketLauncher);

        Add("Armor & Health", "Броня и здоровье", "ARMOR", "Body Armor (100%)", "Бронежилет (100%)", Weapon.Unarmed).IsArmor = true;
        Add("Armor & Health", "Броня и здоровье", "HEALTH", "Health (100%)", "Здоровье (100%)", Weapon.Unarmed).IsHealth = true;
    }

    Category GetCategory(string name, string nameRu)
    {
        foreach (Category c in categories) if (c.Name == name) return c;
        Category n = new Category(); n.Name = name; n.NameRu = nameRu;
        categories.Add(n);
        return n;
    }

    Texture GetPreview(string key)
    {
        Texture t;
        if (previews.TryGetValue(key, out t)) return t;
        if (missingPreviews.ContainsKey(key)) return null;
        string path = Path.Combine(modDir, "images\\" + key + ".png");
        if (File.Exists(path))
        {
            try { t = new Texture(File.ReadAllBytes(path)); previews[key] = t; return t; }
            catch (Exception ex) { Game.Console.Print("WeaponMenu: can't load " + path + ": " + ex.Message); }
        }
        missingPreviews[key] = true;
        return null;
    }

    // ---------- Выдача ----------
    void GiveWeapon(Ped me, Weapon w, bool select)
    {
        Function.Call("GIVE_WEAPON_TO_CHAR", me, (int)w, FallbackAmmo, false);
        try
        {
            GTA.value.Weapon wp = me.Weapons.FromType(w);
            int max = wp.MaxAmmo;
            wp.Ammo = max > 0 ? max : FallbackAmmo;
            if (wp.MaxAmmoInClip > 0) wp.AmmoInClip = wp.MaxAmmoInClip;
            if (select) wp.Select();
        }
        catch (Exception ex) { Game.Console.Print("WeaponMenu: ammo error: " + ex.Message); }
    }

    void Give(Item it)
    {
        Ped me = Player.Character;
        if (me == null || !me.Exists()) return;
        try
        {
            if (it.IsArmor) { me.Armor = 100; Notify(T("Armor 100%", "Броня 100%"), 1500); }
            else if (it.IsHealth) { me.Health = PlayerMaxHealth; Notify(T("Health 100%", "Здоровье 100%"), 1500); }
            else if (it.IsAll)
            {
                Weapon[] best = {
                    Weapon.Melee_Knife, Weapon.Thrown_Grenade, Weapon.Handgun_DesertEagle, Weapon.Shotgun_Baretta,
                    Weapon.SMG_MP5, Weapon.Rifle_M4, Weapon.SniperRifle_M40A1, Weapon.Heavy_RocketLauncher };
                foreach (Weapon w in best) GiveWeapon(me, w, false);
                me.Armor = 100;
                me.Health = PlayerMaxHealth;
                Notify(T("All weapons + armor + full health", "Всё оружие + броня + полное здоровье"), 1800);
            }
            else
            {
                GiveWeapon(me, it.Type, true);
                Notify(ItemName(it) + T(" - max ammo", " — полный боезапас"), 1500);
            }
        }
        catch (Exception ex) { Notify(T("Weapon error: ", "Ошибка оружия: ") + ex.Message, 3000); }
    }

    string ItemName(Item it) { return cfg.Ru ? it.NameRu : it.Name; }

    // ---------- Ввод ----------
    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (e.Key == keyMenu) { menuOpen = !menuOpen; return; }
        if (!menuOpen || categories.Count == 0) return;

        List<Item> items = categories[catIndex].Items;
        switch (e.Key)
        {
            case Keys.Left: catIndex = (catIndex - 1 + categories.Count) % categories.Count; selected = 0; scroll = 0; return;
            case Keys.Right: catIndex = (catIndex + 1) % categories.Count; selected = 0; scroll = 0; return;
            case Keys.Up: selected = (selected - 1 + items.Count) % items.Count; break;
            case Keys.Down: selected = (selected + 1) % items.Count; break;
            case Keys.Enter: Give(items[selected]); break;
            case Keys.Back: menuOpen = false; break;
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
    }

    // ---------- Отрисовка (справа) ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        DrawToast(g);
        if (!menuOpen || categories.Count == 0) return;
        g.Scaling = FontScaling.ScreenUnits;

        Category cat = categories[catIndex];
        List<Item> items = cat.Items;
        if (selected >= items.Count) selected = items.Count - 1;

        float w = 0.27f, x = 1f - 0.03f - w, y = 0.17f, rowH = 0.032f;

        g.DrawRectangle(new RectangleF(x, y - 0.08f, w, 0.08f + rowH * VisibleRows + 0.07f), Color.FromArgb(190, 0, 0, 0));
        g.DrawText(T("WEAPONS", "ОРУЖИЕ"), x + 0.01f, y - 0.077f, Color.Orange, fTitle);
        g.DrawText("<  " + (cfg.Ru ? cat.NameRu : cat.Name).ToUpper() + "  (" + (catIndex + 1) + "/" + categories.Count + ")  >", x + 0.01f, y - 0.037f, Color.White, fRow);

        for (int i = 0; i < VisibleRows && scroll + i < items.Count; i++)
        {
            int idx = scroll + i;
            float ry = y + i * rowH;
            if (idx == selected) g.DrawRectangle(new RectangleF(x, ry, w, rowH), Color.FromArgb(200, 255, 140, 0));
            g.DrawText(ItemName(items[idx]), x + 0.01f, ry + 0.004f, idx == selected ? Color.Black : Color.White, fRow);
        }
        float by = y + rowH * VisibleRows + 0.006f;
        g.DrawText(T("Enter - get (max ammo)", "Enter — получить (полный боезапас)"), x + 0.01f, by, Color.LightGray, fSmall);
        g.DrawText(T("Left/Right - category   Backspace - close", "Влево/вправо — категория   Backspace — закрыть"), x + 0.01f, by + 0.027f, Color.LightGray, fSmall);

        // картинка — под списком
        Item cur = items[selected];
        float px = x, py = y + rowH * VisibleRows + 0.08f, pw = w, ph = 0.24f;
        RectangleF box = new RectangleF(px, py, pw, ph);
        RectangleF inner = new RectangleF(px + 0.005f, py + 0.005f, pw - 0.01f, ph - 0.01f);
        g.DrawRectangle(box, Color.FromArgb(190, 0, 0, 0));
        Texture tex = (cur.IsAll || cur.IsHealth) ? null : GetPreview(cur.Key);
        if (tex != null) g.DrawSprite(tex, inner);
        else if (cur.IsHealth) g.DrawText(T("+ Restores health to 100%", "+ Восстанавливает здоровье до 100%"), inner, TextAlignment.Center | TextAlignment.VerticalCenter, Color.FromArgb(255, 120, 230, 120), fRow);
        else if (cur.IsAll) g.DrawText(T("Knife, Grenades, Combat Pistol, Combat Shotgun,\nSMG, Carbine, Sniper, RPG + Armor + Health",
                                         "Нож, гранаты, боевой пистолет, боевой дробовик,\nПП, карабин, снайперка, РПГ + броня + здоровье"),
                                       inner, TextAlignment.Center | TextAlignment.VerticalCenter, Color.LightGray, fSmall);
        else g.DrawText(T("No photo", "Нет фото"), inner, TextAlignment.Center | TextAlignment.VerticalCenter, Color.Gray, fTitle);
    }
}

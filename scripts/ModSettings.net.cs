// ModSettings — окно «Mod menu settings» для всех модов сборки.
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Меню паузы GTA IV рисует сам движок, и скрипты не могут добавить туда свой пункт,
// поэтому настройки открываются отдельным окном на своей клавише.
//
// Управление:
//   F9 (по умолчанию)  — открыть/закрыть настройки
//   Up / Down          — выбор строки
//   Left / Right       — изменить значение (язык, лимиты); PageUp/PageDown — лимит шагом побольше
//   Enter              — на строке клавиши: «нажми новую клавишу»; на «Сбросить» — вернуть всё по умолчанию
//   Esc                — отменить ожидание новой клавиши
//   Backspace          — закрыть
//
// Всё сохраняется сразу в scripts\ModMenu.ini; остальные моды подхватывают изменения примерно за секунду.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GTA;

public class ModSettings : Script
{
    class KeyAction { public string Id; public string En; public string Ru; public Keys Default; public Keys Current; }

    readonly List<KeyAction> actions = new List<KeyAction>();
    readonly string iniPath;
    string language = "EN";
    int vehicleLimit = 20;
    int npcLimit = 150;

    const int DefVehicleLimit = 20, MinVehicleLimit = 1, MaxVehicleLimit = 100;
    const int DefNpcLimit = 150, MinNpcLimit = 5, MaxNpcLimit = 300;

    // строки окна: 0 — язык, 1 — лимит машин, 2 — лимит NPC, 3.. — клавиши, последняя — сброс
    const int RowLang = 0, RowVeh = 1, RowNpc = 2, RowKeys = 3;
    int RowReset { get { return RowKeys + actions.Count; } }
    int RowCount { get { return RowReset + 1; } }

    const int VisibleRows = 15;
    bool open = false;
    int selected = 0;
    int scroll = 0;
    KeyAction capturing = null;

    GTA.Font fTitle, fRow, fSmall;
    string toast = null; int toastUntil = 0;

    bool Ru { get { return language == "RU"; } }
    string T(string en, string ru) { return Ru ? ru : en; }

    // клавиши, которые заняты навигацией меню и полётом — на них действия не назначаем
    static readonly Keys[] Reserved = {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Back, Keys.Escape,
        Keys.PageUp, Keys.PageDown, Keys.Add, Keys.Subtract, Keys.Oemplus, Keys.OemMinus,
        Keys.W, Keys.A, Keys.S, Keys.D, Keys.Space,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Tab, Keys.Capital };

    public ModSettings()
    {
        iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\ModMenu.ini");

        AddAction("Key_Settings", "Open these settings", "Открыть эти настройки", Keys.F9);
        AddAction("Key_CarMenu", "Vehicle menu", "Меню транспорта", Keys.F5);
        AddAction("Key_RandomCar", "Vehicle menu: random", "Транспорт: случайный", Keys.R);
        AddAction("Key_WeaponMenu", "Weapon menu", "Меню оружия", Keys.F6);
        AddAction("Key_NpcMenu", "NPC menu", "Меню NPC", Keys.F7);
        AddAction("Key_NpcFight", "NPC: fight", "NPC: драка", Keys.B);
        AddAction("Key_NpcGuard", "NPC: bodyguards", "NPC: охрана", Keys.G);
        AddAction("Key_NpcHunt", "NPC: hunt me", "NPC: охота на меня", Keys.H);
        AddAction("Key_NpcCalm", "NPC: calm down", "NPC: успокоить", Keys.N);
        AddAction("Key_NpcRemoveMine", "NPC: remove mine", "NPC: убрать своих", Keys.Delete);
        AddAction("Key_NpcClearAll", "NPC: clear ALL", "NPC: убрать ВСЕХ", Keys.C);
        AddAction("Key_Fly", "Fly on / off", "Полёт вкл / выкл", Keys.F8);
        AddAction("Key_FlyDrop", "Fly: drop", "Полёт: сбросить", Keys.X);
        AddAction("Key_FlyDropType", "Fly: what to drop", "Полёт: что сбрасывать", Keys.Q);

        Load();
        if (!File.Exists(iniPath)) Save(); // первый запуск — создаём файл со значениями по умолчанию

        fTitle = new GTA.Font("Arial", 0.030f, FontScaling.ScreenUnits, true, false);
        fRow = new GTA.Font("Arial", 0.024f, FontScaling.ScreenUnits, false, false);
        fSmall = new GTA.Font("Arial", 0.019f, FontScaling.ScreenUnits, false, false);
        // без тёмного контура: на выделенной (оранжевой) строке он сливался с чёрными буквами
        fTitle.Effect = FontEffect.None; fRow.Effect = FontEffect.None; fSmall.Effect = FontEffect.None;

        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
    }

    void AddAction(string id, string en, string ru, Keys def)
    {
        KeyAction a = new KeyAction(); a.Id = id; a.En = en; a.Ru = ru; a.Default = def; a.Current = def;
        actions.Add(a);
    }

    KeyAction Find(string id) { foreach (KeyAction a in actions) if (a.Id == id) return a; return null; }

    // ---------- ini ----------
    void Load()
    {
        try
        {
            if (!File.Exists(iniPath)) return;
            foreach (string raw in File.ReadAllLines(iniPath))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith(";") || l.StartsWith("[")) continue;
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                int n;
                if (k == "Language") language = v.ToUpper() == "RU" ? "RU" : "EN";
                else if (k == "VehicleLimit" && int.TryParse(v, out n)) vehicleLimit = Clamp(n, MinVehicleLimit, MaxVehicleLimit);
                else if (k == "NpcLimit" && int.TryParse(v, out n)) npcLimit = Clamp(n, MinNpcLimit, MaxNpcLimit);
                else
                {
                    KeyAction a = Find(k);
                    if (a != null) { try { a.Current = (Keys)Enum.Parse(typeof(Keys), v, true); } catch (Exception) { } }
                }
            }
        }
        catch (Exception ex) { Game.Console.Print("ModSettings: load error: " + ex.Message); }
    }

    void Save()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Mod menu settings (GTA IV). Можно менять в игре через F9 или прямо здесь.");
            sb.AppendLine("# Language: EN или RU. Клавиши — имена из System.Windows.Forms.Keys (F5, R, Delete, NumPad1...).");
            sb.AppendLine("[General]");
            sb.AppendLine("Language=" + language);
            sb.AppendLine("VehicleLimit=" + vehicleLimit);
            sb.AppendLine("NpcLimit=" + npcLimit);
            sb.AppendLine("[Keys]");
            foreach (KeyAction a in actions) sb.AppendLine(a.Id + "=" + a.Current);
            File.WriteAllText(iniPath, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Notify(T("Can't save settings: ", "Не удалось сохранить: ") + ex.Message); }
    }

    static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }

    void Notify(string text) { toast = text; toastUntil = Environment.TickCount + 2000; }

    static bool IsReserved(Keys k) { foreach (Keys r in Reserved) if (r == k) return true; return false; }

    // ---------- Ввод ----------
    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        // ждём новую клавишу для действия
        if (capturing != null)
        {
            if (e.Key == Keys.Escape) { capturing = null; Notify(T("Cancelled", "Отменено")); return; }
            if (IsReserved(e.Key)) { Notify(T("This key is used by menus or flying", "Эта клавиша занята меню или полётом")); return; }
            // если клавиша уже у другого действия — меняем их местами
            foreach (KeyAction other in actions)
                if (other != capturing && other.Current == e.Key) { other.Current = capturing.Current; Notify(T("Swapped with: ", "Поменялись местами с: ") + (Ru ? other.Ru : other.En)); }
            capturing.Current = e.Key;
            capturing = null;
            Save();
            return;
        }

        KeyAction settingsKey = Find("Key_Settings");
        if (e.Key == settingsKey.Current) { open = !open; if (open) Load(); return; }
        if (!open) return;

        switch (e.Key)
        {
            case Keys.Up: selected = (selected - 1 + RowCount) % RowCount; break;
            case Keys.Down: selected = (selected + 1) % RowCount; break;
            case Keys.Left: Change(-1, false); break;
            case Keys.Right: Change(+1, false); break;
            case Keys.PageDown: Change(-1, true); break;
            case Keys.PageUp: Change(+1, true); break;
            case Keys.Enter:
                if (selected >= RowKeys && selected < RowReset) capturing = actions[selected - RowKeys];
                else if (selected == RowReset) ResetDefaults();
                else Change(+1, false);
                break;
            case Keys.Back: open = false; break;
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
    }

    void Change(int dir, bool big)
    {
        if (selected == RowLang) language = Ru ? "EN" : "RU";
        else if (selected == RowVeh) vehicleLimit = Clamp(vehicleLimit + dir * (big ? 10 : 1), MinVehicleLimit, MaxVehicleLimit);
        else if (selected == RowNpc) npcLimit = Clamp(npcLimit + dir * (big ? 50 : 5), MinNpcLimit, MaxNpcLimit);
        else return;
        Save();
    }

    void ResetDefaults()
    {
        language = "EN";
        vehicleLimit = DefVehicleLimit;
        npcLimit = DefNpcLimit;
        foreach (KeyAction a in actions) a.Current = a.Default;
        Save();
        Notify(T("Settings reset to defaults", "Настройки сброшены по умолчанию"));
    }

    // ---------- Отрисовка ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        g.Scaling = FontScaling.ScreenUnits;

        if (toast != null && Environment.TickCount < toastUntil)
        {
            RectangleF r = new RectangleF(0.30f, 0.71f, 0.40f, 0.042f);
            g.DrawRectangle(r, Color.FromArgb(200, 0, 0, 0));
            g.DrawText(toast, r, TextAlignment.Center | TextAlignment.VerticalCenter, Color.White, fRow);
        }
        if (!open) return;

        float w = 0.40f, x = 0.5f - w / 2f, y = 0.16f, rowH = 0.034f;
        float h = 0.075f + rowH * VisibleRows + 0.075f;

        g.DrawRectangle(new RectangleF(x, y - 0.075f, w, h), Color.FromArgb(215, 0, 0, 0));
        g.DrawRectangle(new RectangleF(x, y - 0.075f, w, 0.004f), Color.Orange);
        g.DrawText("MOD MENU SETTINGS", x + 0.012f, y - 0.066f, Color.Orange, fTitle);
        g.DrawText(T("saved to scripts\\ModMenu.ini", "сохраняется в scripts\\ModMenu.ini"), new RectangleF(x, y - 0.062f, w - 0.012f, 0.03f), TextAlignment.Right, Color.Gray, fSmall);

        for (int i = 0; i < VisibleRows && scroll + i < RowCount; i++)
        {
            int idx = scroll + i;
            float ry = y + i * rowH;
            bool sel = idx == selected;
            if (sel) g.DrawRectangle(new RectangleF(x, ry, w, rowH), Color.FromArgb(200, 255, 140, 0));
            Color c = sel ? Color.Black : Color.White;

            string label, value;
            if (idx == RowLang) { label = T("Language", "Язык"); value = Ru ? "<  Русский  >" : "<  English  >"; }
            else if (idx == RowVeh) { label = T("Vehicle limit", "Лимит машин"); value = "<  " + vehicleLimit + "  >"; }
            else if (idx == RowNpc) { label = T("NPC limit", "Лимит NPC"); value = "<  " + npcLimit + "  >"; }
            else if (idx == RowReset) { label = T("Reset everything to defaults", "Сбросить всё по умолчанию"); value = "Enter"; }
            else
            {
                KeyAction a = actions[idx - RowKeys];
                label = Ru ? a.Ru : a.En;
                value = a == capturing ? T("press a key...  (Esc - cancel)", "нажми клавишу...  (Esc — отмена)") : "[ " + a.Current + " ]";
            }
            // разделитель перед блоком клавиш
            if (idx == RowKeys) g.DrawRectangle(new RectangleF(x + 0.01f, ry - 0.002f, w - 0.02f, 0.002f), Color.FromArgb(120, 255, 255, 255));

            g.DrawText(label, x + 0.012f, ry + 0.005f, c, fRow);
            g.DrawText(value, new RectangleF(x, ry + 0.005f, w - 0.012f, rowH), TextAlignment.Right, c, fRow);
        }

        float by = y + rowH * VisibleRows + 0.008f;
        g.DrawText(T("Up/Down - select   Left/Right - change   Enter - set key   Backspace - close",
                     "Вверх/вниз — выбор   Влево/вправо — изменить   Enter — назначить   Backspace — закрыть"), x + 0.012f, by, Color.LightGray, fSmall);
        g.DrawText(T("Limits: how many spawned vehicles / NPC stay in the world; the oldest are removed.",
                     "Лимиты: сколько заспавненных машин / NPC держится в мире; самые старые удаляются."), x + 0.012f, by + 0.027f, Color.Gray, fSmall);
    }
}

// ExtrasMenu — «Меню возможностей» для GTA IV: трафик, починка машины, телепорт к метке,
// быстрое сохранение и тур по 200 голубям (Flying Rats).
// Для ScriptHookDotNet (GTA IV). Кладётся в папку <GTA IV>\scripts\
//
// Управление (клавишу меню можно сменить в F9):
//   F10            — открыть / закрыть меню
//   Вверх / вниз   — выбор строки
//   Влево / вправо — изменить значение (трафик, номер голубя)
//   Enter          — выполнить пункт
//   End            — голубь убит → к следующему (работает и при закрытом меню)
//   Backspace      — закрыть меню
// Настройки трафика и прогресс по голубям: scripts\ExtrasMenu.ini
//
// Координаты голубей: «Pigeon Jumper 1.01» (по файлу локаций Gforce для GTA IV Location Handler).
// Это место, где встать, и направление взгляда на голубя; сам голубь в нескольких метрах.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Native;

public class ExtrasMenu : Script
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

    GTA.Font fTitle, fRow, fSmall;
    string toast = null; int toastUntil = 0;
    void Notify(string text, int ms) { toast = text; toastUntil = Environment.TickCount + ms; }
    void DrawToast(GTA.Graphics g)
    {
        if (toast == null || Environment.TickCount > toastUntil) return;
        g.Scaling = FontScaling.ScreenUnits;
        RectangleF r = new RectangleF(0.25f, 0.76f, 0.50f, 0.042f);
        g.DrawRectangle(r, Color.FromArgb(200, 0, 0, 0));
        g.DrawText(toast, r, TextAlignment.Center | TextAlignment.VerticalCenter, Color.White, fRow);
    }
    // ===== конец общего блока =====

    // x;y;z;heading — 200 точек, номер голубя = номер строки
    static readonly string[] Points = {
        "1383.285;-740.899;9.416676;323.5584",
        "1600.553;176.6339;23.06749;235.2389",
        "1434.26;207.7654;31.65166;178.4481",
        "1088.2745;42.2053;41.5247;135.3475",
        "851.2717;-175.5297;13.76238;246.6126",
        "934.554;-848.7029;1.611683;110.399",
        "1151.703;-589.5602;39.07723;305.4143",
        "1002.1315;-655.6766;17.7107;286.4843",
        "819.5607;-585.6087;17.0262;271.5810",
        "1515.798;-420.8813;33.09132;87.5694",
        "1314.177;-508.7329;15.06778;109.5210",
        "1285.1742;-316.4314;25.0464;276.5133",
        "793.163;-232.9077;22.07077;106.0238",
        "1160.622;-458.762;14.07339;308.9102",
        "721.6820;-440.0259;3.2570;268.8303",
        "959.0553;-293.523;25.63162;77.7806",
        "1068.73;-171.8173;29.9158;347.5034",
        "1131.2736;236.9722;36.9350;257.2211",
        "1302.736;163.7834;32.64115;247.0886",
        "1309.407;-176.9174;28.52095;47.2476",
        "1326.699;-45.38095;26.29384;321.4253",
        "797.7863;130.8565;11.21149;101.9958",
        "949.525;419.7842;15.37073;162.3789",
        "1406.677;404.5299;34.08654;287.2391",
        "1175.9348;441.9866;32.1274;186.7382",
        "858.4955;586.0189;10.19532;317.7452",
        "1318.031;666.8534;51.55698;259.0385",
        "1267.582;773.151;48.82607;262.2472",
        "1622.288;443.4826;44.24736;109.9884",
        "1611.998;851.67;15.84786;92.7201",
        "1488.626;615.8231;29.87448;214.0062",
        "1380.223;532.2262;45.04935;276.8433",
        "1151.2571;686.8099;40.7997;100.8311",
        "894.6882;700.5655;19.0958;54.0379",
        "908.8833;943.3969;11.95245;215.0709",
        "1168.6318;780.7050;36.12385;198.7474",
        "1255.965;997.7284;13.69034;143.4326",
        "1256.5928;999.0252;13.69034;149.6828",
        "1120.766;914.2169;32.71244;95.6927",
        "1272.756;897.1676;30.49624;143.7759",
        "1097.8457;1000.7336;16.96753;133.9240",
        "1745.382;651.2075;33.1183;277.6213",
        "1830.5061;399.0555;33.30363;100.9558",
        "1888.728;781.4185;22.40464;108.4054",
        "2306.737;613.4068;17.02295;56.4391",
        "2620.785;416.8716;79.35115;121.9735",
        "2366.842;365.9368;12.74285;358.6936",
        "2319.946;336.0617;5.921841;80.7880",
        "747.8826;2099.4143;1.45123;172.0587",
        "625.7219;1759.83;38.40894;202.3147",
        "408.6021;2053.654;6.704692;270.4292",
        "806.4045;1804.4945;39.3746;333.5993",
        "612.8613;1867.4178;33.20713;76.3805",
        "783.539;1403.63;15.1128;264.0440",
        "701.3419;1292.367;9.811462;188.2473",
        "394.4674;1709.2351;19.4610;278.2657",
        "387.5847;1655.8699;14.9850;290.9471",
        "966.6344;1630.7975;33.3168;241.3181",
        "1031.042;1573.752;8.682259;247.9034",
        "1252.0646;1553.1099;22.5909;9.6287",
        "1503.936;1820.952;3.433424;297.1246",
        "906.5904;1896.427;38.11735;251.9811",
        "1254.798;1833.884;10.16821;290.9968",
        "1073.138;1819.125;12.75187;329.6127",
        "299.3426;1359.3217;8.3788;10.1082",
        "513.4402;1250.0426;2.6471;171.0679",
        "483.647;1493.084;11.6666;35.6367",
        "482.739;1498.871;17.80412;188.5360",
        "571.6302;1506.615;22.69814;241.7429",
        "571.5735;1508.8665;22.6981;229.0048",
        "453.2568;1112.64;2.718945;107.9832",
        "344.927;1012.221;36.14861;185.3477",
        "742.874;608.1206;34.59634;233.7561",
        "-359.9076;-678.3126;2.512487;63.7899",
        "-30.03113;-955.344;13.15468;313.5756",
        "111.9983;-963.4715;5.183868;317.6277",
        "-282.3553;-389.2843;8.736195;145.6278",
        "-86.30613;-339.443;14.71113;212.3926",
        "-208.0092;1235.391;22.33825;216.8851",
        "139.1682;1302.995;2.519422;295.4556",
        "-28.0231;1393.44;30.20756;75.0356",
        "39.06916;110.9621;14.95967;252.7628",
        "237.9668;-171.7449;4.966308;276.4021",
        "241.5198;-419.7524;9.014573;16.8836",
        "335.3305;-640.4701;4.684227;290.7975",
        "-12.8181;411.9324;90.4552;271.2514",
        "147.2867;482.5543;15.19174;158.9817",
        "-679.8953;1168.988;12.08342;180.7973",
        "-668.459;1157.071;19.3763;187.1863",
        "117.3897;919.8654;14.71668;179.0776",
        "201.9003;263.9229;8.432988;355.1497",
        "154.3948;226.5417;20.86341;279.6035",
        "495.0918;-391.4711;85.40078;53.4189",
        "277.8798;682.8503;5.33554;304.2930",
        "-261.3190;710.554;12.3349;115.2337",
        "-212.0801;1040.172;11.25033;297.0136",
        "-33.43482;722.3671;19.99577;118.5545",
        "-24.76914;843.3276;19.61367;174.8987",
        "-73.0489;945.5134;18.1216;192.4595",
        "-599.8979;841.4153;14.7133;320.5447",
        "-523.6762;643.7216;13.09177;289.0952",
        "-389.0036;874.2890;18.1903;101.3588",
        "-415.8018;1310.263;94.10352;217.5175",
        "-439.0022;1286.241;41.5662;62.1338",
        "-369.2202;1276.3971;24.6813;5.7491",
        "-607.2336;1396.384;8.856962;80.1005",
        "-205.8305;1512.9873;29.5307;182.5688",
        "-250.25;1769.298;3.036861;335.3689",
        "-428.4367;1548.678;22.77744;345.0095",
        "-320.0247;1507.756;19.22275;14.4681",
        "-10.09543;1495.568;18.43006;5.7147",
        "270.5848;34.24569;4.730324;183.9399",
        "-420.966;437.3576;12.48775;218.9459",
        "-174.3938;210.0668;14.7641;94.8932",
        "-119.3120;427.8420;14.6096;296.3322",
        "-289.1924;238.1573;204.3569;246.3204",
        "-246.948;245.6846;14.81791;172.3754",
        "-453.2754;-256.6083;7.920385;66.3034",
        "-318.3607;-290.9279;14.16998;127.0191",
        "-119.9337;-706.1703;10.68728;273.3777",
        "29.89901;-600.777;14.68614;51.6255",
        "104.7059;-762.7701;4.945235;351.8055",
        "-462.8968;4.4506;12.4180;23.2200",
        "-410.1084;-86.1474;15.24263;319.6714",
        "-299.5299;-87.13524;334.7692;337.0668",
        "-122.0459;16.4138;32.6816;117.1678",
        "-69.71039;-89.14568;19.62749;226.2583",
        "-466.7071;1023.0390;14.9370;185.1889",
        "-505.567;1125.57;12.84894;278.5105",
        "-505.9778;282.1847;20.73759;286.1236",
        "-1653.411;413.7862;46.89301;240.1678",
        "-1324.434;516.8026;20.73969;0.6895",
        "-1769.425;260.9086;22.42062;337.1423",
        "-1423.07;405.0429;18.88369;96.2722",
        "-1266.0066;-252.1225;3.2728;274.2257",
        "-1266.0066;-252.1225;3.2728;265.1406",
        "-1442.543;-540.6039;9.588781;98.9276",
        "-1675.439;-486.9536;51.1483;87.3287",
        "-2019.197;-410.2822;4.370708;9.4103",
        "-2016.546;-491.181;7.179315;174.3658",
        "-1035.699;-295.3102;12.58006;219.3775",
        "-1701.049;-329.4572;2.93644;77.2212",
        "-843.715;825.903;4.220965;92.1965",
        "-1259.989;1075.347;23.72748;307.6093",
        "-986.5027;1032.3661;31.6032;270.5611",
        "-1421.007;888.954;23.03611;196.8506",
        "-1241.611;1087.416;24.79119;25.4162",
        "-898.5242;772.4515;7.6794;182.7326",
        "-1197.601;988.6091;19.76106;93.7904",
        "-1390.362;995.0355;22.26179;107.0984",
        "-1015.375;939.4417;23.01725;105.8467",
        "-837.1313;1030.7878;15.6335;79.2022",
        "-1399.563;1146.322;19.74726;44.6165",
        "-1371.4835;742.0445;19.7305;163.7292",
        "-1649.537;604.081;24.69057;355.8536",
        "-1597.918;507.162;30.48269;49.6442",
        "-1500.17;580.5726;23.33288;293.5972",
        "-1593.9229;840.0094;23.4767;52.0435",
        "-1499.854;1391.769;22.14738;332.3361",
        "-1032.172;1187.303;23.3954;1.9148",
        "-931.6993;1340.272;24.84639;287.5217",
        "-1313.046;1270.708;22.37042;61.1242",
        "-1073.457;1282.583;39.10277;279.2809",
        "-727.8417;1245.613;1.941154;296.4911",
        "-854.4346;-399.9461;7.565543;32.4411",
        "-1248.075;626.0641;-1.14737;299.0793",
        "-1182.168;638.9814;7.952938;92.7801",
        "-1264.043;228.1638;5.357387;337.7398",
        "-1034.292;46.88329;10.48765;3.3650",
        "-915.2019;349.3284;4.508037;252.2116",
        "-1365.0349;278.8444;19.3062;33.33299",
        "-2247.302;-24.00233;5.097257;24.3397",
        "-2132.161;18.01088;16.09418;221.5258",
        "-1790.545;113.6732;18.91089;116.4781",
        "-1435.265;-75.17609;34.36533;12.6048",
        "-1997.672;201.0731;16.89466;253.6190",
        "-1505.8075;-148.8124;13.7608;188.2738",
        "-1585.076;26.78789;13.72593;107.4789",
        "-1595.637;147.4583;12.81961;82.9955",
        "-1992.87;-121.9282;31.77801;281.7652",
        "-1170.251;1833.316;4.383008;206.5801",
        "-1368.061;1526.635;22.52939;249.8397",
        "-1302.1476;1696.0741;33.4487;97.3806",
        "-957.9724;1763.157;18.95356;307.9717",
        "-1224.84;1459.704;21.69226;242.0359",
        "-1072.687;1574.995;34.6039;203.4185",
        "-608.7179;-741.8929;20.6431;352.0535",
        "-609.4792;-750.3654;87.4757;308.5541",
        "-609.4792;-750.3654;87.4757;303.9124",
        "-608.2710;-750.6586;79.1245;137.8931",
        "-610.3657;-751.2927;84.3303;261.2173",
        "-613.2906;-757.3104;72.5530;35.3476",
        "583.6985;727.7499;2.801165;98.1739",
        "376.5974;125.6199;5.8811;225.0225",
        "484.537;102.5513;7.963133;170.6147",
        "429.6814;237.8659;14.72012;71.3860",
        "508.8451;-51.78592;16.87663;281.1568",
        "505.6121;218.0988;30.70965;29.9183",
        "648.7534;242.0278;42.3706;290.3705",
        "1412.357;1243.246;2.446855;246.3507",
    };

    // строки меню
    const int RowTraffic = 0, RowPeds = 1, RowRepair = 2, RowWaypoint = 3, RowBack = 4, RowSave = 5,
              RowPigeon = 6, RowPigeonNext = 7, RowPigeonUnmark = 8, RowCount = 9;

    const int Count = 200;
    readonly float[] px = new float[Count], py = new float[Count], pz = new float[Count], ph = new float[Count];
    readonly bool[] done = new bool[Count];
    int current = 0;                 // голубь 0..199
    int traffic = 100, peds = 100;   // плотность, %
    bool densityTouched = false;     // плотность меняли — надо держать её каждый кадр

    bool open = false;
    int selected = 0;
    Keys MenuKey = Keys.F10;
    readonly string savePath;

    // телепорт в два шага: сначала грузим район, потом ставим Нико (или его машину)
    int jumpPhase = 0, jumpAt = 0, jumpTries = 0;
    float jx, jy, jz, jh;
    bool jumpFindGround = false;     // к метке на карте: высоту земли ищем после загрузки района
    bool haveBack = false;
    float bx, by, bz, bh;

    public ExtrasMenu()
    {
        savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\ExtrasMenu.ini");
        for (int i = 0; i < Count; i++)
        {
            string[] p = Points[i].Split(';');
            px[i] = float.Parse(p[0], CultureInfo.InvariantCulture);
            py[i] = float.Parse(p[1], CultureInfo.InvariantCulture);
            pz[i] = float.Parse(p[2], CultureInfo.InvariantCulture);
            ph[i] = float.Parse(p[3], CultureInfo.InvariantCulture);
        }
        // прогресс из прошлой версии (тур по голубям) — подхватываем, если нового файла ещё нет
        string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts\\PigeonTour.ini");
        LoadState(File.Exists(savePath) ? savePath : old);
        densityTouched = traffic != 100 || peds != 100;

        fTitle = new GTA.Font("Arial", 0.030f, FontScaling.ScreenUnits, true, false);
        fRow = new GTA.Font("Arial", 0.024f, FontScaling.ScreenUnits, false, false);
        fSmall = new GTA.Font("Arial", 0.019f, FontScaling.ScreenUnits, false, false);
        fTitle.Effect = FontEffect.None; fRow.Effect = FontEffect.None; fSmall.Effect = FontEffect.None;
        MenuKey = cfg.Key("Key_Extras", Keys.F10);

        Interval = 0;
        this.KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        this.Tick += new EventHandler(OnTick);
        this.PerFrameDrawing += new GTA.GraphicsEventHandler(OnDraw);
    }

    // ---------- сохранение состояния ----------
    void LoadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                int n;
                if (k == "Current" && int.TryParse(v, out n) && n >= 1 && n <= Count) current = n - 1;
                else if (k == "Traffic" && int.TryParse(v, out n)) traffic = Math.Max(0, Math.Min(100, n));
                else if (k == "Peds" && int.TryParse(v, out n)) peds = Math.Max(0, Math.Min(100, n));
                else if (k == "Done")
                    foreach (string s in v.Split(','))
                        if (int.TryParse(s.Trim(), out n) && n >= 1 && n <= Count) done[n - 1] = true;
            }
        }
        catch (Exception) { }
    }

    void SaveState()
    {
        try
        {
            StringBuilder list = new StringBuilder();
            for (int i = 0; i < Count; i++) if (done[i]) { if (list.Length > 0) list.Append(','); list.Append(i + 1); }
            File.WriteAllText(savePath,
                "# Extras menu. Traffic/Peds — плотность в %, Done — голуби, отмеченные как убитые.\r\n" +
                "Traffic=" + traffic + "\r\nPeds=" + peds + "\r\n" +
                "Current=" + (current + 1) + "\r\nDone=" + list + "\r\n", new UTF8Encoding(false));
        }
        catch (Exception ex) { Notify(T("Can't save: ", "Не удалось сохранить: ") + ex.Message, 2500); }
    }

    int DoneCount() { int c = 0; for (int i = 0; i < Count; i++) if (done[i]) c++; return c; }

    int NextUndone(int from)
    {
        for (int k = 1; k <= Count; k++) { int i = (from + k) % Count; if (!done[i]) return i; }
        return -1;
    }

    // ---------- ввод ----------
    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (e.Key == MenuKey) { open = !open; return; }
        if (e.Key == Keys.End) { PigeonKilledNext(); return; }   // удобно стрелять с закрытым меню
        if (!open) return;
        switch (e.Key)
        {
            case Keys.Up: selected = (selected - 1 + RowCount) % RowCount; break;
            case Keys.Down: selected = (selected + 1) % RowCount; break;
            case Keys.Left: Change(-1); break;
            case Keys.Right: Change(+1); break;
            case Keys.Enter: Activate(); break;
            case Keys.Back: open = false; break;
        }
    }

    void Change(int dir)
    {
        if (selected == RowTraffic) { traffic = Math.Max(0, Math.Min(100, traffic + dir * 10)); densityTouched = true; SaveState(); }
        else if (selected == RowPeds) { peds = Math.Max(0, Math.Min(100, peds + dir * 10)); densityTouched = true; SaveState(); }
        else if (selected == RowPigeon) { current = (current + dir + Count) % Count; SaveState(); }
    }

    void Activate()
    {
        switch (selected)
        {
            case RowTraffic: case RowPeds: Change(+1); break;
            case RowRepair: RepairCar(); break;
            case RowWaypoint: TeleportToWaypoint(); break;
            case RowBack: GoBack(); break;
            case RowSave: QuickSave(); break;
            case RowPigeon: JumpToPigeon(current); break;
            case RowPigeonNext: PigeonKilledNext(); break;
            case RowPigeonUnmark: done[current] = false; SaveState(); Notify(T("Mark removed", "Отметка снята"), 1500); break;
        }
    }

    Ped Me()
    {
        Ped me = Player.Character;
        if (me == null || !me.Exists() || !me.isAlive) return null;
        return me;
    }

    // ---------- починка ----------
    void RepairCar()
    {
        Ped me = Me();
        if (me == null) return;
        if (!me.isInVehicle()) { Notify(T("Get in a vehicle first", "Сначала сядь в машину"), 2000); return; }
        try
        {
            Vehicle v = me.CurrentVehicle;
            v.Repair();
            v.Dirtyness = 0f;
            Notify(T("Vehicle repaired and washed", "Машина починена и помыта"), 2000);
        }
        catch (Exception ex) { Notify(T("Repair error: ", "Ошибка починки: ") + ex.Message, 2500); }
    }

    // ---------- телепорт ----------
    void TeleportToWaypoint()
    {
        Blip w = null;
        try { w = Game.GetWaypoint(); } catch (Exception) { }
        if (w == null) { Notify(T("Set a waypoint on the map first", "Сначала поставь метку на карте"), 2500); return; }
        Vector3 p;
        try { p = w.Position; } catch (Exception) { Notify(T("Set a waypoint on the map first", "Сначала поставь метку на карте"), 2500); return; }
        Ped me = Me();
        if (me == null) return;
        StartJump(p.X, p.Y, 0f, me.Heading, true);
    }

    void GoBack()
    {
        if (!haveBack) { Notify(T("Nowhere to go back to yet", "Возвращаться пока некуда"), 2000); return; }
        StartJump(bx, by, bz, bh, false);
    }

    void JumpToPigeon(int i)
    {
        Ped me = Me();
        if (me == null) return;
        if (me.isInVehicle()) { Notify(T("Get out of the vehicle first", "Сначала выйди из машины"), 2000); return; }
        if (StartJump(px[i], py[i], pz[i], ph[i], false)) GiveGuns(me);
    }

    void PigeonKilledNext()
    {
        done[current] = true;
        int n = NextUndone(current);
        if (n < 0) { SaveState(); Notify(T("All 200 pigeons are marked!", "Все 200 голубей отмечены!"), 4000); return; }
        current = n;
        SaveState();
        JumpToPigeon(current);
    }

    bool StartJump(float x, float y, float z, float h, bool findGround)
    {
        Ped me = Me();
        if (me == null || jumpPhase != 0) return false;
        // запоминаем, откуда прыгаем — для пункта «Вернуться назад»
        try
        {
            Vector3 cur = me.isInVehicle() ? me.CurrentVehicle.Position : me.Position;
            bx = cur.X; by = cur.Y; bz = cur.Z; bh = me.isInVehicle() ? me.CurrentVehicle.Heading : me.Heading;
            haveBack = true;
        }
        catch (Exception) { }
        jx = x; jy = y; jz = z; jh = h; jumpFindGround = findGround; jumpTries = 0;
        try { Player.CanControlCharacter = false; } catch (Exception) { }
        try { if (me.isInVehicle()) me.CurrentVehicle.FreezePosition = true; else me.FreezePosition = true; } catch (Exception) { }
        try { Function.Call("LOAD_SCENE", x, y, findGround ? 50f : z); } catch (Exception) { }
        try { Function.Call("REQUEST_COLLISION_AT_POSN", x, y, findGround ? 50f : z); } catch (Exception) { }
        jumpPhase = 1;
        jumpAt = Environment.TickCount + 500;
        return true;
    }

    void FinishJump()
    {
        jumpPhase = 0;
        Ped me = Player.Character;
        try
        {
            if (me == null || !me.Exists()) return;
            float z = jz;
            if (jumpFindGround)
            {
                float gz = 0f;
                try { gz = World.GetGroundZ(new Vector3(jx, jy, 1000f), GroundType.Highest); } catch (Exception) { }
                if (gz == 0f && jumpTries < 6)
                {
                    // земля ещё не подгрузилась — ждём и пробуем снова
                    jumpTries++;
                    try { Function.Call("REQUEST_COLLISION_AT_POSN", jx, jy, 50f); } catch (Exception) { }
                    jumpPhase = 1; jumpAt = Environment.TickCount + 300;
                    return;
                }
                z = gz != 0f ? gz + 1.0f : 100f;   // совсем не нашли — сверху, игра сама уронит на землю
            }
            if (me.isInVehicle())
            {
                Vehicle v = me.CurrentVehicle;
                v.Position = new Vector3(jx, jy, z + (jumpFindGround ? 0.5f : 0f));
                v.Heading = jh;
                v.FreezePosition = false;
            }
            else
            {
                Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", me, jx, jy, z);
                me.Heading = jh;
                me.FreezePosition = false;
            }
            Function.Call("SET_CAM_BEHIND_PED", me);
        }
        catch (Exception ex) { Notify(T("Teleport error: ", "Ошибка телепорта: ") + ex.Message, 2500); }
        finally
        {
            // пока ждём подгрузку земли (jumpPhase снова 1), Нико остаётся замороженным
            if (jumpPhase == 0)
            {
                try { if (me != null && me.Exists()) { me.FreezePosition = false; if (me.isInVehicle()) me.CurrentVehicle.FreezePosition = false; } } catch (Exception) { }
                try { Player.CanControlCharacter = true; } catch (Exception) { }
            }
        }
    }

    // пистолет для близких голубей и снайперка для тех, что высоко
    void GiveGuns(Ped me)
    {
        try { Function.Call("GIVE_WEAPON_TO_CHAR", me, (int)Weapon.SniperRifle_Basic, 60, false); } catch (Exception) { }
        try { Function.Call("GIVE_WEAPON_TO_CHAR", me, (int)Weapon.Handgun_Glock, 300, false); } catch (Exception) { }
    }

    // ---------- быстрое сохранение ----------
    void QuickSave()
    {
        Ped me = Me();
        if (me == null) return;
        bool mission = false;
        try { mission = Function.Call<bool>("GET_MISSION_FLAG"); } catch (Exception) { }
        if (mission) { Notify(T("Can't save during a mission", "Во время миссии сохраняться нельзя"), 2500); return; }
        int wanted = 0;
        try { wanted = Player.WantedLevel; } catch (Exception) { }
        if (wanted > 0) { Notify(T("Lose the cops first", "Сначала оторвись от полиции"), 2500); return; }
        open = false;
        try { Function.Call("ACTIVATE_SAVE_MENU"); }   // то же меню сохранения, что и у кровати
        catch (Exception ex) { Notify(T("Save error: ", "Ошибка сохранения: ") + ex.Message, 2500); }
    }

    void OnTick(object sender, EventArgs e)
    {
        if (cfg.Reload(false)) MenuKey = cfg.Key("Key_Extras", Keys.F10);
        if (jumpPhase == 1 && Environment.TickCount >= jumpAt) FinishJump();

        // плотность трафика игра сбрасывает сама, поэтому держим её каждый кадр
        if (densityTouched)
        {
            try
            {
                Function.Call("SET_CAR_DENSITY_MULTIPLIER", traffic / 100f);
                Function.Call("SET_PED_DENSITY_MULTIPLIER", peds / 100f);
            }
            catch (Exception) { }
            if (traffic == 100 && peds == 100) densityTouched = false;   // вернули как было — больше не вмешиваемся
        }
    }

    string Hint(int n)
    {
        switch (n)
        {
            case 36: case 114: case 115: return T("In the tree", "На дереве");
            case 86: return T("On top of the other cross", "На верхушке другого креста");
            case 192: return T("Aim a bit left, or snipe from below", "Целься чуть левее или снимай снизу из снайперки");
        }
        return T("Look ahead and up - it glows orange", "Смотри вперёд и вверх — голубь светится оранжевым");
    }

    // ---------- отрисовка ----------
    void OnDraw(object sender, GTA.GraphicsEventArgs e)
    {
        GTA.Graphics g = e.Graphics;
        DrawToast(g);
        if (!open) return;
        g.Scaling = FontScaling.ScreenUnits;
        Color accent = cfg.Accent;

        float w = 0.36f, x = 0.5f - w / 2f, y = 0.17f, rowH = 0.034f;
        float h = 0.075f + rowH * RowCount + 0.022f + 0.140f;
        float top = y - 0.075f;
        g.DrawRectangle(new RectangleF(x, top, w, h), Color.FromArgb(215, 0, 0, 0));
        g.DrawRectangle(new RectangleF(x, top, w, 0.004f), accent);
        g.DrawText(T("EXTRAS", "ВОЗМОЖНОСТИ"), x + 0.012f, top + 0.009f, accent, fTitle);

        float ry = y;
        for (int i = 0; i < RowCount; i++)
        {
            // разделитель перед блоком голубей
            if (i == RowPigeon)
            {
                g.DrawText(T("PIGEONS", "ГОЛУБИ"), x + 0.012f, ry + 0.002f, accent, fSmall);
                ry += 0.022f;
            }
            bool sel = i == selected;
            if (sel) g.DrawRectangle(new RectangleF(x, ry, w, rowH), Color.FromArgb(200, accent));
            Color c = sel ? cfg.SelText : Color.White;

            string label = "", value = "";
            switch (i)
            {
                case RowTraffic: label = T("Traffic", "Трафик машин"); value = "<  " + traffic + "%  >"; break;
                case RowPeds: label = T("Pedestrians", "Пешеходы"); value = "<  " + peds + "%  >"; break;
                case RowRepair: label = T("Repair vehicle", "Починить машину"); value = "Enter"; break;
                case RowWaypoint: label = T("Teleport to map waypoint", "Телепорт к метке на карте"); value = "Enter"; break;
                case RowBack: label = T("Go back (before teleport)", "Вернуться назад"); value = "Enter"; break;
                case RowSave: label = T("Quick save", "Быстрое сохранение"); value = "Enter"; break;
                case RowPigeon:
                    label = T("Go to pigeon", "К голубю");
                    value = "<  " + (current + 1) + " / " + Count + (done[current] ? T("  killed", "  убит") : "") + "  >";
                    break;
                case RowPigeonNext: label = T("Killed - go to next", "Убит — к следующему"); value = "End"; break;
                case RowPigeonUnmark: label = T("Remove mark", "Снять отметку"); value = "Enter"; break;
            }
            g.DrawText(label, x + 0.012f, ry + 0.005f, c, fRow);
            g.DrawText(value, new RectangleF(x, ry + 0.005f, w - 0.012f, rowH), TextAlignment.Right, c, fRow);
            ry += rowH;
        }

        // прогресс по голубям
        int dc = DoneCount();
        float bx0 = x + 0.012f, bw = w - 0.024f, byy = ry + 0.010f;
        g.DrawText(Hint(current + 1), bx0, byy, Color.LightGray, fSmall);
        byy += 0.028f;
        g.DrawRectangle(new RectangleF(bx0, byy, bw, 0.008f), Color.FromArgb(120, 255, 255, 255));
        g.DrawRectangle(new RectangleF(bx0, byy, bw * dc / Count, 0.008f), accent);
        g.DrawText(T("Pigeons marked: ", "Голубей отмечено: ") + dc + " / " + Count + T("   (exact: Stats)", "   (точно — в Статистике)"), bx0, byy + 0.012f, Color.Gray, fSmall);
        // подсказка в две строки: одной строкой она вылезала за правый край панели
        g.DrawText(T("Up/Down - select   Left/Right - change", "Вверх/вниз — выбор   Влево/вправо — изменить"), bx0, byy + 0.040f, Color.LightGray, fSmall);
        g.DrawText(T("Enter - do   Backspace - close", "Enter — выполнить   Backspace — закрыть"), bx0, byy + 0.064f, Color.LightGray, fSmall);
    }
}

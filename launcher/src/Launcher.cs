// WCE-Hack Launcher — 单文件 exe：自解压外挂 mod + 修复启用状态 + 启动游戏
// 编译: csc.exe /target:winexe /langversion:5 /win32manifest:app.manifest /resource:... 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WceLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                var sb = new StringBuilder();
                try
                {
                    sb.AppendLine("gameDir   = " + Cfg.GameDir() + " exists=" + Directory.Exists(Cfg.GameDir()));
                    sb.AppendLine("gameProc  = " + Engine.GameRunning());
                    sb.AppendLine("steam     = " + Cfg.SteamExe() + " exists=" + File.Exists(Cfg.SteamExe()));
                    sb.AppendLine("deploy    -> " + Engine.Deploy());
                    sb.AppendLine("selection -> " + Engine.FixSelection());
                    sb.AppendLine("modVer    = " + (Engine.ModVersion() ?? "<null>"));
                    sb.AppendLine("enabled   = " + Engine.SelectionEnabled());
                    sb.AppendLine("RESULT    = OK");
                }
                catch (Exception e)
                {
                    sb.AppendLine("RESULT    = FAIL " + e);
                }
                string outp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt");
                File.WriteAllText(outp, sb.ToString(), new UTF8Encoding(false));
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal static class Cfg
    {
        public const string GameProcessName = "Worst Class Ever";
        public const string ModId = "wcehack";
        public const string PkgFolder = "wcehack";
        public const string AssemblyFile = "WceHack.dll";
        public const string SteamAppId = "5175310";
        public const string ResDll = "payload.dll";
        public const string ResJson = "payload.json";

        public static string GameDir()
        {
            string hard = @"C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever";
            if (Directory.Exists(hard)) return hard;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SteamPath");
                        if (v != null)
                        {
                            string p = Path.Combine(v.ToString().Replace('/', '\\'),
                                @"steamapps\common\Worst Class Ever");
                            if (Directory.Exists(p)) return p;
                        }
                    }
                }
            }
            catch { }
            return hard;
        }

        public static string InstallMods() { return Path.Combine(GameDir(), "Mods"); }
        public static string PkgDir() { return Path.Combine(InstallMods(), PkgFolder); }
        public static string GameExe() { return Path.Combine(GameDir(), GameProcessName + ".exe"); }

        public static string LocalLowRoot()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(Path.Combine(up, @"AppData\LocalLow\Funcore Games\Worst Class Ever"));
        }

        public static string StateMods() { return Path.Combine(LocalLowRoot(), "Mods"); }
        public static string SelectionPath() { return Path.Combine(StateMods(), "load-order.json"); }
        public static string PlayerLog() { return Path.Combine(LocalLowRoot(), "Player.log"); }
        public static string SteamExe()
        {
            string hard = @"C:\Program Files (x86)\Steam\steam.exe";
            if (File.Exists(hard)) return hard;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SteamExe");
                        if (v != null)
                        {
                            string p = v.ToString().Replace('/', '\\');
                            if (File.Exists(p)) return p;
                        }
                    }
                }
            }
            catch { }
            return hard;
        }
    }

    internal static class Engine
    {
        public static bool GameRunning()
        {
            try { return Process.GetProcessesByName(Cfg.GameProcessName).Length > 0; }
            catch { return false; }
        }

        public static string ReadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("embedded resource missing: " + name);
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        public static byte[] ReadResourceBytes(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("embedded resource missing: " + name);
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>把 mod 包释放到 游戏安装目录\Mods\wcehack\</summary>
        public static string Deploy()
        {
            string dir = Cfg.PkgDir();
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, Cfg.AssemblyFile), ReadResourceBytes(Cfg.ResDll));
                File.WriteAllText(Path.Combine(dir, "mod.json"), ReadResource(Cfg.ResJson), new UTF8Encoding(false));
            }
            catch (IOException)
            {
                if (GameRunning())
                    throw new Exception("游戏正在运行，WceHack.dll 被占用 —— 请先完全退出游戏再点部署。");
                throw;
            }
            return dir;
        }

        /// <summary>确保 load-order.json 里 wcehack = enabled 且 missing=false，保留其他 mod</summary>
        public static string FixSelection()
        {
            Directory.CreateDirectory(Cfg.StateMods());
            string path = Cfg.SelectionPath();
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;

            Dictionary<string, object> root = null;
            if (File.Exists(path))
            {
                try
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(text.Trim()))
                        root = ser.DeserializeObject(text) as Dictionary<string, object>;
                }
                catch { root = null; }
            }
            if (root == null) root = new Dictionary<string, object>();

            if (!root.ContainsKey("version")) root["version"] = 2;
            root["sourceChoiceRequired"] = false;

            var list = new List<object>();
            object rawMods;
            if (root.TryGetValue("mods", out rawMods) && rawMods is object[])
            {
                var arr = (object[])rawMods;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in arr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    object idObj;
                    string id = (d.TryGetValue("id", out idObj) && idObj != null) ? idObj.ToString() : "";
                    if (string.IsNullOrEmpty(id)) continue;
                    if (id == Cfg.ModId)
                    {
                        // 只保留一条 wcehack，且强制启用
                        if (!seen.Add("wcehack:1")) { }
                        d["enabled"] = true;
                        d["missing"] = false;
                        d["packageKey"] = "local:" + Cfg.ModId + ":" + Cfg.PkgFolder;
                        if (!seen.Contains("wcehack")) { }
                        seen.Add("wcehack");
                        list.Add(d);
                        continue;
                    }
                    if (!seen.Add(id)) continue;
                    list.Add(d);
                }
            }

            bool have = false;
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d != null && d.ContainsKey("id") && Convert.ToString(d["id"]) == Cfg.ModId) { have = true; break; }
            }
            if (!have) list.Add(NewEntry());
            else
            {
                // 补全字段（旧配置可能缺 name/version 等）
                var full = NewEntry();
                foreach (var o in list)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || Convert.ToString(d["id"]) != Cfg.ModId) continue;
                    foreach (var kv in full)
                        if (!d.ContainsKey(kv.Key) || d[kv.Key] == null) d[kv.Key] = kv.Value;
                    d["enabled"] = true;
                    d["missing"] = false;
                    d["packageKey"] = "local:" + Cfg.ModId + ":" + Cfg.PkgFolder;
                }
            }

            root["mods"] = list;
            string outp = ser.Serialize(root);
            File.WriteAllText(path, Pretty(outp), new UTF8Encoding(false));
            return path;
        }

        private static Dictionary<string, object> NewEntry()
        {
            var d = new Dictionary<string, object>();
            d["id"] = Cfg.ModId;
            d["enabled"] = true;
            d["packageKey"] = "local:" + Cfg.ModId + ":" + Cfg.PkgFolder;
            d["missing"] = false;
            d["name"] = "WCE Hack";
            d["author"] = "local";
            d["version"] = "1.0.0";
            d["supportedGameVersion"] = "1.0.0";
            d["description"] = "ESP, teleport, infinite sprint";
            d["nameKey"] = "";
            d["descriptionKey"] = "";
            d["source"] = "本地";
            d["workshopId"] = 0;
            d["assembly"] = Cfg.AssemblyFile;
            d["entryType"] = "WceHack.HackPlugin";
            return d;
        }

        // 让输出好看一点（不影响解析）
        private static string Pretty(string json)
        {
            try
            {
                var sb = new StringBuilder();
                int indent = 0;
                bool inStr = false;
                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];
                    if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                    if (!inStr && (c == '{' || c == '['))
                    {
                        sb.Append(c).Append("\r\n").Append(new string(' ', (++indent) * 4));
                    }
                    else if (!inStr && (c == '}' || c == ']'))
                    {
                        sb.Append("\r\n").Append(new string(' ', (--indent) * 4)).Append(c);
                    }
                    else if (!inStr && c == ',')
                    {
                        sb.Append(",\r\n").Append(new string(' ', indent * 4));
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            catch { return json; }
        }

        public static bool SelectionEnabled()
        {
            try
            {
                string p = Cfg.SelectionPath();
                if (!File.Exists(p)) return false;
                string t = File.ReadAllText(p, Encoding.UTF8);
                int i = t.IndexOf("\"" + Cfg.ModId + "\"", StringComparison.Ordinal);
                if (i < 0) return false;
                int seg = t.IndexOf("}", i, StringComparison.Ordinal);
                if (seg < 0) seg = t.Length;
                string block = t.Substring(i, seg - i);
                int e = block.IndexOf("\"enabled\"", StringComparison.Ordinal);
                if (e < 0) return false;
                int colon = block.IndexOf(':', e);
                if (colon < 0) return false;
                string tail = block.Substring(colon + 1, Math.Min(12, block.Length - colon - 1)).Trim();
                return tail.StartsWith("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string ModVersion()
        {
            try
            {
                string p = Path.Combine(Cfg.PkgDir(), "mod.json");
                if (!File.Exists(p)) return null;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(p, Encoding.UTF8))
                        as Dictionary<string, object>;
                if (d == null) return null;
                object v;
                return d.TryGetValue("version", out v) && v != null ? v.ToString() : "?";
            }
            catch { return null; }
        }

        /// <summary>嵌入的 payload 里 mod.json 的版本号</summary>
        public static string PayloadVersion()
        {
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(ReadResource(Cfg.ResJson))
                        as Dictionary<string, object>;
                if (d == null) return null;
                object v;
                return d.TryGetValue("version", out v) && v != null ? v.ToString() : null;
            }
            catch { return null; }
        }

        public static void LaunchGame()
        {
            string steam = Cfg.SteamExe();
            if (File.Exists(steam))
                Process.Start(steam, "-applaunch " + Cfg.SteamAppId);
            else
                Process.Start(new ProcessStartInfo(Cfg.GameExe()) { WorkingDirectory = Cfg.GameDir() });
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(24, 26, 30);
        private static readonly Color Fg = Color.FromArgb(232, 236, 240);
        private static readonly Color Dim = Color.FromArgb(150, 158, 168);
        private static readonly Color Acc = Color.FromArgb(78, 224, 179);
        private static readonly Color Warn = Color.FromArgb(255, 168, 88);
        private static readonly Color Bad = Color.FromArgb(255, 96, 96);

        private Label _lGame, _lMod, _lSel, _lMsg;
        private Button _bDeploy, _bLaunch;
        private Timer _timer;

        public MainForm()
        {
            Text = "WCE Hack Launcher   v2";
            ClientSize = new Size(580, 452);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Microsoft YaHei UI", 9f);

            var t = new Label();
            t.Text = "WCE HACK";
            t.Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold);
            t.ForeColor = Acc;
            t.SetBounds(18, 14, 240, 34);
            Controls.Add(t);

            var s = new Label();
            s.Text = "Worst Class Ever · 官方 Mod 通道 · 无需改游戏文件";
            s.ForeColor = Dim;
            s.SetBounds(20, 48, 460, 20);
            Controls.Add(s);

            var box = new Panel();
            box.SetBounds(18, 76, 544, 116);
            box.BackColor = Color.FromArgb(32, 35, 40);
            Controls.Add(box);

            _lGame = AddRow(box, 10, "游戏进程");
            _lMod = AddRow(box, 38, "外挂包");
            _lSel = AddRow(box, 66, "启用状态");
            var lPath = AddRow(box, 92, "配置");
            lPath.Text = @"配置  ...\Worst Class Ever\Mods\load-order.json";
            lPath.Font = new Font("Consolas", 7.5f);
            lPath.ForeColor = Dim;

            _bDeploy = MakeBtn("① 部署 / 修复", 18, 204, 180, 38);
            _bDeploy.Click += delegate { DoDeploy(false); };
            Controls.Add(_bDeploy);

            _bLaunch = MakeBtn("② 启动游戏（带挂）", 210, 204, 220, 38);
            _bLaunch.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _bLaunch.Click += delegate { DoDeploy(true); };
            Controls.Add(_bLaunch);

            var bLog = MakeBtn("看日志", 442, 204, 120, 38);
            bLog.Click += delegate { OpenSafe(Cfg.PlayerLog()); };
            Controls.Add(bLog);

            var bDir = MakeBtn("打开 Mods 目录", 18, 250, 180, 30);
            bDir.Font = new Font("Microsoft YaHei UI", 8.5f);
            bDir.Click += delegate { OpenSafe(Cfg.InstallMods()); };
            Controls.Add(bDir);

            var bRebuild = MakeBtn("重新部署外挂包", 210, 250, 220, 30);
            bRebuild.Font = new Font("Microsoft YaHei UI", 8.5f);
            bRebuild.Click += delegate { DoDeploy(false); };
            Controls.Add(bRebuild);

            var bExit = MakeBtn("退出", 442, 250, 120, 30);
            bExit.Font = new Font("Microsoft YaHei UI", 8.5f);
            bExit.Click += delegate { Close(); };
            Controls.Add(bExit);

            var keys = new Label();
            keys.Text =
                "游戏内热键\r\n" +
                "  F1 透视ESP    F2 菜单     F3 散射    F4 速射\r\n" +
                "  F5 传送→老师  F6 传送→学生  F7 冲刺    F8 加速×2.5\r\n" +
                "  F9 头顶名字   F10 显示NPC   F11 模型染色  End 染色透墙\r\n" +
                "  Home 无限体力 Ins 自动脱困  Del 完全隐形  1 穿墙投掷\r\n" +
                "  2 秒解救     3 不可被抓    4 不可打晕   5 视角锁定\r\n" +
                "说明：外挂只在游戏启动时加载。已经开着游戏时请先完全退出。";
            keys.ForeColor = Dim;
            keys.SetBounds(20, 292, 544, 120);
            Controls.Add(keys);

            _lMsg = new Label();
            _lMsg.SetBounds(20, 418, 544, 24);
            _lMsg.ForeColor = Acc;
            Controls.Add(_lMsg);

            _timer = new Timer();
            _timer.Interval = 1200;
            _timer.Tick += delegate { Refresh_(); };
            _timer.Start();
            Refresh_();
        }

        private Label AddRow(Panel p, int y, string caption)
        {
            var c = new Label();
            c.Text = caption;
            c.ForeColor = Dim;
            c.SetBounds(14, y, 96, 20);
            p.Controls.Add(c);
            var v = new Label();
            v.Text = "-";
            v.SetBounds(116, y, 412, 20);
            p.Controls.Add(v);
            return v;
        }

        private Button MakeBtn(string text, int x, int y, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 88);
            b.BackColor = Color.FromArgb(40, 44, 50);
            b.ForeColor = Fg;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void Refresh_()
        {
            bool running = Engine.GameRunning();
            _lGame.Text = running ? "运行中（外挂未加载）" : "未运行";
            _lGame.ForeColor = running ? Warn : Dim;

            string ver = Engine.ModVersion();
            string newVer = Engine.PayloadVersion();
            if (ver == null)
            {
                _lMod.Text = "未部署（点 ① 部署）";
                _lMod.ForeColor = Bad;
            }
            else if (newVer != null && newVer != ver)
            {
                _lMod.Text = "已部署 v" + ver + " → 可升级 v" + newVer + "（退出游戏后点 ①）";
                _lMod.ForeColor = Warn;
            }
            else
            {
                _lMod.Text = "已部署  v" + ver + "    Mods\\" + Cfg.PkgFolder;
                _lMod.ForeColor = Acc;
            }

            bool en = Engine.SelectionEnabled();
            _lSel.Text = en ? "已启用 (load-order.json)" : "未启用";
            _lSel.ForeColor = en ? Acc : Bad;

            _bLaunch.Enabled = !running;
            if (running) _bLaunch.Text = "游戏运行中，请先退出";
            else _bLaunch.Text = "② 启动游戏（带挂）";
        }

        private void DoDeploy(bool thenLaunch)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                if (thenLaunch && Engine.GameRunning())
                {
                    Say("游戏正在运行，请先完全退出再启动。", Warn);
                    return;
                }
                string dir = Engine.Deploy();
                string sel = Engine.FixSelection();
                Say("OK  包 -> " + dir, Acc);
                Say("OK  启用 -> " + sel, Acc);

                if (thenLaunch)
                {
                    Engine.LaunchGame();
                    Say("已通过 Steam 启动，进游戏后按 F2 看面板。", Acc);
                }
            }
            catch (Exception e)
            {
                Say("失败: " + e.Message, Bad);
            }
            finally
            {
                Cursor = Cursors.Default;
                Refresh_();
            }
        }

        private void Say(string m, Color c)
        {
            _lMsg.Text = m;
            _lMsg.ForeColor = c;
        }

        private static void OpenSafe(string path)
        {
            try
            {
                if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                {
                    string d = Path.GetDirectoryName(path);
                    if (Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\"");
                }
            }
            catch { }
        }
    }
}

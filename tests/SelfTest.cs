using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskBoost
{
    // TaskBoost's self-test. It is built together with the app's own sources (see test.bat), so it calls the
    // real code, but it keeps its data in tests\out and never in the user's TaskBoost folder.
    //
    //   SelfTest.exe            everything that changes nothing on the PC: logic, sandboxed files and registry,
    //                           read-only scans, the MCP server, and the window (shown off-screen)
    //   SelfTest.exe live       also the round trips on the real system, each of which puts back what it changed:
    //                           a test entry in the sign-in list, the Boost tweaks, gaming and charging mode
    //                           (the parts that need no administrator), the laptop modes
    //   SelfTest.exe <group>    one group only: logic, sandbox, scans, mcp, ui, live
    static class SelfTest
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static int passed, failed;
        static string root, outDir;
        static readonly List<string> failures = new List<string>();

        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        static void Section(string name) { Console.WriteLine(); Console.WriteLine("== " + name); }

        static void Check(string what, bool ok, string detail = null)
        {
            if (ok) passed++;
            else
            {
                failed++;
                failures.Add(what + (detail == null ? "" : "  (" + detail + ")"));
            }
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what + (detail == null || ok ? "" : "  (" + detail + ")"));
        }

        static void Note(string text) { Console.WriteLine("        " + text); }

        static object Call(Type type, string method, params object[] args) { return type.GetMethod(method, Any).Invoke(null, args); }
        static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Any).Invoke(target, args); }
        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Any).GetValue(target); }

        static void Pump(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(5); }
        }

        static bool PumpUntil(Func<bool> done, int timeoutMs)
        {
            DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                if (done()) return true;
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return done();
        }

        static string FreshDir(string name)
        {
            string path = Path.Combine(outDir, name);
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
            return path;
        }

        [STAThread]
        static int Main(string[] args)
        {
            // tests\bin\SelfTest.exe -> the project folder is two levels up
            root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\.."));
            outDir = Path.Combine(root, @"tests\out");
            Directory.CreateDirectory(outDir);
            string group = args.Length > 0 ? args[0] : "";
            Func<string, bool> wanted = delegate(string g) { return group == "" ? g != "live" : group == g || (group == "live" && g == "live"); };

            App.Init(FreshDir("data"));
            // Anything that writes the exe's path into a setting must write the real one, never this test program
            App.ExePath = Path.Combine(root, "TaskBoost.exe");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            // Outside a message loop Windows Forms takes its own synchronisation context away again, and what the pages
            // do when their background work ends would then run on a worker thread. Here it stays for the whole run.
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Motion.Attach();

            Console.WriteLine("TaskBoost self-test   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "   data in " + App.DataDir);
            try
            {
                if (wanted("logic")) Logic();
                if (wanted("sandbox")) Sandbox();
                if (wanted("scans")) Scans();
                if (wanted("mcp")) McpServer();
                if (wanted("ui")) Window();
                if (wanted("live")) Live();
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Check("the test run itself", false, inner.GetType().Name + ": " + inner.Message + " @ " + inner.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            foreach (string f in failures) Console.WriteLine("  FAILED: " + f);
            return failed == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- pure logic

        static void Logic()
        {
            Section("Health score tiers");
            Check("90 and up is Excellent", HealthScore.Tier(90) == "Excellent" && HealthScore.Tier(100) == "Excellent");
            Check("75 to 89 is Good", HealthScore.Tier(89) == "Good" && HealthScore.Tier(75) == "Good");
            Check("50 to 74 needs attention", HealthScore.Tier(74) == "Needs attention" && HealthScore.Tier(50) == "Needs attention");
            Check("below 50 is in trouble", HealthScore.Tier(49) == "In trouble" && HealthScore.Tier(0) == "In trouble");

            Section("Command lines of startup entries");
            string a, win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Check("quoted path with arguments", StartupInfo.ExePath("\"C:\\Program Files\\App\\app.exe\" --tray", out a) == @"C:\Program Files\App\app.exe" && a == "--tray");
            Check("unquoted path with spaces and arguments", StartupInfo.ExePath(@"C:\Program Files\App\app.exe /min", out a) == @"C:\Program Files\App\app.exe" && a == "/min");
            Check("environment variable is expanded", StartupInfo.ExePath(@"%SystemRoot%\System32\x.exe", out a).Equals(win + @"\System32\x.exe", StringComparison.OrdinalIgnoreCase));
            Check(@"\SystemRoot\ prefix of services", StartupInfo.ExePath(@"\SystemRoot\System32\svc.exe -k", out a).Equals(win + @"\System32\svc.exe", StringComparison.OrdinalIgnoreCase) && a == "-k");
            Check(@"system32\ prefix of services", StartupInfo.ExePath(@"system32\drv.exe", out a).Equals(win + @"\system32\drv.exe", StringComparison.OrdinalIgnoreCase));
            Check(@"\??\ prefix is dropped", StartupInfo.ExePath(@"\??\C:\Tools\t.exe", out a) == @"C:\Tools\t.exe");
            Check("a bare name is looked for in System32", StartupInfo.ExePath("ctfmon.exe", out a).Equals(Path.Combine(Environment.SystemDirectory, "ctfmon.exe"), StringComparison.OrdinalIgnoreCase));

            Section("Scheduled-task list parsing");
            var cells = (List<string>)Call(typeof(StartupInfo), "Csv", "\"PC\",\"\\Task, with comma\",\"say \"\"hi\"\"\",plain,");
            Check("quoted cells, commas and doubled quotes", cells.Count == 5 && cells[1] == "\\Task, with comma" && cells[2] == "say \"hi\"" && cells[3] == "plain" && cells[4] == "");

            Section("Built-in knowledge");
            string importance, what;
            Check("the Defender engine is marked Keep", Knowledge.Program("msmpeng", out importance, out what) && importance == "K" && what.Length > 10);
            Check("OneDrive is marked Optional", Knowledge.Program("OneDrive", out importance, out what) && importance == "O");
            Check("an unknown program is not in the list", !Knowledge.Program("definitely-not-a-real-program", out importance, out what));
            Check("a Logitech keyboard is named", Knowledge.KeyboardName("VID_046D&PID_C31C").StartsWith("Logitech"), Knowledge.KeyboardName("VID_046D&PID_C31C"));
            Check("an Arduino is a programmable board", Knowledge.ProgrammableBoard("VID_2341&PID_8036"));
            Check("a Logitech keyboard is not", !Knowledge.ProgrammableBoard("VID_046D&PID_C31C"));
            Check("an unknown maker says so", Knowledge.KeyboardName("VID_FFFF&PID_0001").StartsWith("Unknown maker"));
            int programs = 0;
            foreach (string line in ((string)typeof(Knowledge).GetField("Programs", Any).GetValue(null)).Split('\n'))
            {
                string[] p = line.Trim().Split('|');
                if (p.Length == 1 && p[0] == "") continue;
                programs++;
                if (p.Length != 3 || "KUO".IndexOf(p[1]) < 0 || p[1].Length != 1 || p[2].Length < 5) Check("program list line is well formed: " + line.Trim(), false);
            }
            Check("the program list has about 290 well-formed entries", programs > 250, programs + " entries");

            Section("Which addresses count as the internet");
            Func<string, bool> pub = delegate(string ip) { return (bool)Call(typeof(Guard), "IsPublic", IPAddress.Parse(ip)); };
            Check("public IPv4 and IPv6 addresses", pub("8.8.8.8") && pub("160.79.104.10") && pub("2001:4860:4860::8888"));
            Check("home and office networks are not", !pub("10.1.2.3") && !pub("192.168.1.10") && !pub("172.16.0.1") && !pub("172.31.255.255") && pub("172.32.0.1"));
            Check("loopback, link-local and multicast are not", !pub("127.0.0.1") && !pub("169.254.10.10") && !pub("224.0.0.251") && !pub("::1") && !pub("fe80::1") && !pub("fd12:3456::1") && !pub("ff02::fb"));

            Section("Menu, colours and movement");
            PopItem chip = PopMenu.Chip("low", "Low power  -  longest battery (sleepy spider)", "sleepy", "low");
            Check("a mode text splits into name and purpose", chip.Title == "Low power" && chip.Subtitle == "Longest battery (sleepy spider)" && chip.Key == "low");
            Check("a text without a purpose keeps its name", PopMenu.Chip("x", "Plain", "", "").Title == "Plain" && PopMenu.Chip("x", "Plain", "", "").Subtitle == "");
            Check("colour mixing hits both ends and the middle", Theme.Mix(Color.Black, Color.White, 0F).ToArgb() == Color.Black.ToArgb() && Theme.Mix(Color.Black, Color.White, 1F).ToArgb() == Color.White.ToArgb() && Theme.Mix(Color.Black, Color.White, 0.5F).R == 127);
            Check("colour mixing never leaves the range", Theme.Mix(Color.Black, Color.White, 1.4F).R == 255 && Theme.Mix(Color.Black, Color.White, -0.3F).R == 0);
            bool rising = true;
            for (float t = 0.05F; t <= 1F; t += 0.05F) if (Motion.EaseOut(t) < Motion.EaseOut(t - 0.05F)) rising = false;
            Check("ease-out runs from 0 to 1 and only forwards", Motion.EaseOut(0F) == 0F && Motion.EaseOut(1F) == 1F && rising);
            Check("the spring ends exactly on target after overshooting", Math.Abs(Motion.Spring(1F) - 1F) < 0.0001F && Motion.Spring(0.7F) > 1F);
            foreach (string mood in BuddySprite.Moods)
                using (var b = new Bitmap(BuddySprite.Grid, BuddySprite.Grid))
                    foreach (string costume in new[] { "", "work", "turbo", "low" })
                        BuddySprite.Draw(b, 1, true, 1, mood, false, costume);
            Check("every mood draws in every costume", true, BuddySprite.Moods.Length + " moods");
            using (var icon = new Bitmap(48, 48))
            using (var g = Graphics.FromImage(icon))
            {
                int drawn = 0;
                foreach (string key in new[] { "theme", "animations", "background", "taskbar", "power", "autostart", "services", "textboot", "startmenu", "manual", "auto", "game", "trash", "reports", "recycle", "clipboard", "pulse", "disk", "sensors", "network", "keyboards", "remote", "shield", "notify", "buddy", "buddyhide", "buddyclaude", "tray", "signin", "search", "memory", "clock", "spark", "exit" })
                {
                    g.Clear(Color.Black);
                    Icons.Draw(g, key, new RectangleF(0, 0, 48, 48), Color.White, Color.Black);
                    bool any = false;
                    for (int y = 0; y < 48 && !any; y += 2)
                        for (int x = 0; x < 48 && !any; x += 2) any = icon.GetPixel(x, y).R > 100;
                    if (any) drawn++; else Check("icon \"" + key + "\" draws something", false);
                }
                Check("every icon the app asks for exists", drawn == 34, drawn + " of 34");
            }

            Section("Translation");
            Lang.Romanian = true;
            Check("the Romanian table loads (no duplicate entries)", Lang.T("Home") == "Acasă");
            var missing = UntranslatedLiterals();
            foreach (string m in missing) Note("no Romanian for: \"" + m.Replace("\n", "\\n") + "\"");
            Check("every text the code passes to the translator has a Romanian version", missing.Count == 0, missing.Count + " missing");
            Lang.Romanian = false;
            Check("English is what the code says", Lang.T("Home") == "Home");
        }

        // The literal strings in the sources that go through Lang.T, straight or by way of a dialog or the buddy
        static List<string> UntranslatedLiterals()
        {
            var ro = (Dictionary<string, string>)typeof(Lang).GetField("Ro", Any).GetValue(null);
            var missing = new List<string>();
            var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (string file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs"))
            {
                if (Path.GetFileName(file) == "Lang.cs") continue;
                string code = File.ReadAllText(file);
                foreach (string opener in new[] { "Lang.T(", "Dialog.Show(", "Dialog.Ask(", ".Say(" })
                {
                    int at = 0;
                    while ((at = code.IndexOf(opener, at)) >= 0)
                    {
                        at += opener.Length;
                        // The argument list: up to the bracket that closes the call
                        int depth = 1, end = at;
                        bool inString = false;
                        for (; end < code.Length && depth > 0; end++)
                        {
                            char ch = code[end];
                            if (inString) { if (ch == '\\') end++; else if (ch == '"') inString = false; }
                            else if (ch == '"') inString = true;
                            else if (ch == '(') depth++;
                            else if (ch == ')') depth--;
                        }
                        string arguments = code.Substring(at, end - at - 1);
                        // A dialog or a saying built from translated pieces is covered by those pieces
                        if (opener != "Lang.T(" && arguments.Contains("Lang.T(")) continue;
                        if (opener != "Lang.T(" && arguments.Contains(" + ")) continue;
                        foreach (Match m in literal.Matches(arguments))
                        {
                            string text = Regex.Unescape(m.Groups[1].Value);
                            // Not texts: punctuation, the buddy's moods and the laptop modes' own keys
                            if (text.Trim().Length < 2 || Regex.IsMatch(text, @"^[\W\d]+$") || BuddyMood(text) || Array.IndexOf(LaptopMode.Modes, text) >= 0) continue;
                            if (!ro.ContainsKey(text) && !missing.Contains(text)) missing.Add(text);
                        }
                    }
                }
            }
            return missing;
        }

        static bool BuddyMood(string text) { return Array.IndexOf(BuddySprite.Moods, text) >= 0; }

        // ---------------------------------------------------------------- files and registry, in a sandbox

        static void Sandbox()
        {
            Section("Saved selections");
            var defaults = Settings.Load("unit.txt", new[] { "a", "b" });
            Check("defaults until something is saved", defaults.Count == 2 && defaults[0] == "a");
            Settings.Save("unit.txt", new[] { "x", "y", "z" });
            Check("what was saved comes back", string.Join(",", Settings.Load("unit.txt", new[] { "a" })) == "x,y,z");
            Settings.Save("unit.txt", new string[0]);
            Check("an empty selection stays empty", Settings.Load("unit.txt", new[] { "a" }).Count == 0);
            Motion.Save(false);
            Motion.Load();
            bool off = !Motion.On;
            Motion.Save(true);
            Motion.Load();
            Check("the animations switch is remembered", off && Motion.On);

            Section("Undo record");
            State.FileName = "state-unit.txt";
            State.Clear();
            State.Record("svc", "Fax", "3");
            State.Record("svc", "Fax", "4");
            State.Record("reg", @"HKLM\SOFTWARE\X|Name", "DWord:1");
            State.Record("reg", @"HKCU\Software\X|Name", "DWord:1");
            var entries = State.Load();
            Check("only the first value recorded for a setting counts", entries.Count == 3 && entries[0].Value == "3");
            Check("system and per-user entries are told apart", State.IsSystem(entries[0]) && State.IsSystem(entries[1]) && !State.IsSystem(entries[2]));
            State.Remove(State.IsSystem);
            Check("removing the system entries leaves the user ones", State.Load().Count == 1 && State.Load()[0].Key.StartsWith("HKCU"));
            State.Clear();
            Check("clearing empties the record", State.Load().Count == 0);

            Section("Registry changes and their undo (on a scratch key)");
            const string scratch = @"Software\TaskBoostSelfTest";
            Registry.CurrentUser.DeleteSubKeyTree(scratch, false);
            using (var k = Registry.CurrentUser.CreateSubKey(scratch))
            {
                k.SetValue("Number", 5, RegistryValueKind.DWord);
                k.SetValue("Text", "hello", RegistryValueKind.String);
                k.SetValue("Path", @"%TEMP%\x", RegistryValueKind.ExpandString);
                k.SetValue("List", new[] { "one", "two" }, RegistryValueKind.MultiString);
                k.SetValue("Bytes", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
                k.SetValue("Big", 123456789012L, RegistryValueKind.QWord);
                k.SetValue("Doomed", "still here", RegistryValueKind.String);
            }
            Reg.Set("HKCU", scratch, "Number", 9);
            Reg.Set("HKCU", scratch, "Number", 11); // a second change must not overwrite the true original
            Reg.Set("HKCU", scratch, "Text", "changed");
            Reg.Set("HKCU", scratch, "Path", "plain", RegistryValueKind.ExpandString);
            Reg.Set("HKCU", scratch, "List", new[] { "three" }, RegistryValueKind.MultiString);
            Reg.Set("HKCU", scratch, "Bytes", new byte[] { 9 }, RegistryValueKind.Binary);
            Reg.Set("HKCU", scratch, "Big", 1L, RegistryValueKind.QWord);
            Reg.Set("HKCU", scratch, "Brand new", 1);
            Reg.Delete("HKCU", scratch, "Doomed");
            using (var k = Registry.CurrentUser.OpenSubKey(scratch))
                Check("the changes were written", (int)k.GetValue("Number") == 11 && (string)k.GetValue("Text") == "changed" && k.GetValue("Doomed") == null && (int)k.GetValue("Brand new") == 1);
            Check("one undo line per value", State.Load().Count == 8, State.Load().Count + " lines");
            foreach (var e in State.Load()) Reg.Restore(e);
            using (var k = Registry.CurrentUser.OpenSubKey(scratch))
            {
                Check("numbers go back", (int)k.GetValue("Number") == 5 && (long)k.GetValue("Big") == 123456789012L);
                Check("text goes back", (string)k.GetValue("Text") == "hello" && (string)k.GetValue("Doomed") == "still here");
                Check("an expandable path goes back unexpanded, as the same kind", (string)k.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == @"%TEMP%\x" && k.GetValueKind("Path") == RegistryValueKind.ExpandString);
                Check("lists and bytes go back", string.Join("|", (string[])k.GetValue("List")) == "one|two" && BitConverter.ToString((byte[])k.GetValue("Bytes")) == "01-02-03");
                Check("a value that did not exist is removed again", k.GetValue("Brand new") == null);
            }
            Registry.CurrentUser.DeleteSubKeyTree(scratch, false);
            State.Clear();
            State.FileName = "state.txt";
            Check("no errors were logged on the way", Log.Errors == 0, Log.Errors + " errors");

            Section("Settings that travel (export and import)");
            File.WriteAllText(Path.Combine(App.DataDir, "settings.txt"), "theme,power");
            File.WriteAllText(Path.Combine(App.DataDir, "guard.txt"), "v2,v3,v4,v5,sensors");
            File.WriteAllText(Path.Combine(App.DataDir, "lang.txt"), "ro");
            File.WriteAllText(Path.Combine(App.DataDir, "games.txt"), "C:\\Games\\one.exe\r\nC:\\Games\\two.exe");
            string pack = Path.Combine(outDir, "unit.taskboost");
            int exported = Portable.Export(pack);
            Check("the export holds the choices that exist", exported >= 4 && File.ReadAllText(pack).Contains("== games.txt"), exported + " files");
            foreach (string f in new[] { "settings.txt", "guard.txt", "lang.txt", "games.txt" }) File.Delete(Path.Combine(App.DataDir, f));
            File.AppendAllText(pack, "\r\n== ..\\evil.txt\r\nboom\r\n== state.txt\r\nreg\tx\ty\r\n== log.txt\r\nfake\r\n");
            int imported = Portable.Import(pack);
            Check("the import brings the choices back", File.ReadAllText(Path.Combine(App.DataDir, "settings.txt")) == "theme,power" && File.ReadAllLines(Path.Combine(App.DataDir, "games.txt")).Length == 2);
            Check("it writes only the files it knows: no path tricks, no undo record, no log", !File.Exists(Path.Combine(outDir, "evil.txt")) && !File.Exists(Path.Combine(App.DataDir, "state.txt")) && imported == exported, imported + " of " + exported);
            foreach (string f in new[] { "settings.txt", "guard.txt", "lang.txt", "games.txt" }) File.Delete(Path.Combine(App.DataDir, f));

            Section("Safe clean-up (on a sandbox temp folder)");
            string area = FreshDir("clean"), temp = Path.Combine(area, "Temp"), outside = Path.Combine(area, "outside");
            Directory.CreateDirectory(Path.Combine(temp, "sub"));
            Directory.CreateDirectory(Path.Combine(temp, "empty-old"));
            Directory.CreateDirectory(outside);
            DateTime old = DateTime.UtcNow.AddDays(-10);
            Action<string, bool> make = delegate(string path, bool aged)
            {
                File.WriteAllText(path, new string('x', 2048));
                if (!aged) return;
                File.SetCreationTimeUtc(path, old);
                File.SetLastWriteTimeUtc(path, old);
            };
            make(Path.Combine(temp, "old.tmp"), true);
            make(Path.Combine(temp, "new.tmp"), false);
            make(Path.Combine(temp, @"sub\old-inside.tmp"), true);
            make(Path.Combine(temp, @"sub\new-inside.tmp"), false);
            make(Path.Combine(temp, "in-use.tmp"), true);
            make(Path.Combine(outside, "victim.txt"), true);
            Directory.SetLastWriteTimeUtc(Path.Combine(temp, "empty-old"), old);
            string output;
            Util.Run("cmd.exe", "/c mklink /J \"" + Path.Combine(temp, "link") + "\" \"" + outside + "\"", out output);
            Check("the test junction was made", Directory.Exists(Path.Combine(temp, "link")) && File.Exists(Path.Combine(temp, @"link\victim.txt")), output);

            string tmpBefore = Environment.GetEnvironmentVariable("TMP"), tempBefore = Environment.GetEnvironmentVariable("TEMP");
            Environment.SetEnvironmentVariable("TMP", temp);
            Environment.SetEnvironmentVariable("TEMP", temp);
            try
            {
                Check("the sandbox is what the cleaner will see as the temp folder", Path.GetTempPath().TrimEnd('\\').Equals(temp, StringComparison.OrdinalIgnoreCase), Path.GetTempPath());
                string preview = Mcp.Capture(delegate { Cleaner.Run(new[] { "temp" }, true); });
                Check("the preview counts the three old files", preview.Contains("(3 files)") && preview.Contains("would free"), preview.Trim().Replace("\r\n", " / "));
                Check("the preview deletes nothing", File.Exists(Path.Combine(temp, "old.tmp")) && File.Exists(Path.Combine(temp, @"sub\old-inside.tmp")));
                using (new FileStream(Path.Combine(temp, "in-use.tmp"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    string cleaned = Mcp.Capture(delegate { Cleaner.Run(new[] { "temp" }, false); });
                    Check("old files are deleted", !File.Exists(Path.Combine(temp, "old.tmp")) && !File.Exists(Path.Combine(temp, @"sub\old-inside.tmp")), cleaned.Trim().Replace("\r\n", " / "));
                    Check("files newer than a week stay", File.Exists(Path.Combine(temp, "new.tmp")) && File.Exists(Path.Combine(temp, @"sub\new-inside.tmp")));
                    Check("a file that is in use is skipped without an error", File.Exists(Path.Combine(temp, "in-use.tmp")) && cleaned.Contains("(2 files)"));
                }
                Check("an old empty folder goes, a folder with new files stays", !Directory.Exists(Path.Combine(temp, "empty-old")) && Directory.Exists(Path.Combine(temp, "sub")));
                Check("nothing is deleted through a junction", File.Exists(Path.Combine(outside, "victim.txt")) && Directory.Exists(Path.Combine(temp, "link")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMP", tmpBefore);
                Environment.SetEnvironmentVariable("TEMP", tempBefore);
            }
            Func<string, bool> safe = delegate(string dir) { return (bool)Call(typeof(Cleaner), "IsSafe", dir); };
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Check("only folders named like temp folders may be cleaned", safe(temp) && !safe(outside) && !safe(Path.Combine(profile, "Documents")) && !safe(profile));
            Check("never a folder straight under a drive, never a system folder", !safe(@"C:\Temp") && !safe(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) && safe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")));
            Directory.Delete(Path.Combine(temp, "link")); // the junction only, not what it points at
            Directory.Delete(area, true);
        }

        // ---------------------------------------------------------------- read-only looks at the real PC

        static void Scans()
        {
            Section("Guard scan");
            var all = new[] { "sensors", "network", "keyboards", "remote", "startup", "shield" };
            var first = Guard.Scan(all);
            Check("every check runs", !first.Exists(delegate(Finding f) { return f.Category == "error"; }), Describe(first.FindAll(delegate(Finding f) { return f.Category == "error"; })));
            Check("the first scan takes what is there as the starting point: no alerts for existing things",
                !first.Exists(delegate(Finding f) { return f.Level == 2 && (f.Category == "net" || f.Category == "kbd" || f.Category == "run"); }), Describe(first.FindAll(delegate(Finding f) { return f.Level == 2; })));
            Check("the starting point was saved", File.Exists(Path.Combine(App.DataDir, "known.txt")) && File.ReadAllText(Path.Combine(App.DataDir, "known.txt")).Contains("baseline|net"));
            Check("findings are sorted most serious first", Sorted(first));
            Note(first.Count + " findings: " + first.FindAll(delegate(Finding f) { return f.Level == 2; }).Count + " alerts, " + first.FindAll(delegate(Finding f) { return f.Level == 1; }).Count + " notices");
            var none = Guard.Scan(new string[0]);
            Check("with every check switched off nothing is reported", none.Count == 0);
            var online = Guard.OnlinePrograms();
            Check("programs that are online are listed by file name", online.TrueForAll(delegate(string n) { return n.IndexOf('\\') < 0 && n.Length > 0; }), string.Join(", ", online));
            string own = Guard.ProcessPath(Process.GetCurrentProcess().Id);
            Check("a process's path can be read", own != null && own.EndsWith("SelfTest.exe", StringComparison.OrdinalIgnoreCase), own);
            Check("Windows' own files count as signed by Windows", Guard.Publisher(Path.Combine(Environment.SystemDirectory, "notepad.exe")) != null);
            Check("an unsigned program has no publisher", Guard.Publisher(own) == null);

            Section("Startup list");
            var startup = StartupInfo.Scan();
            Check("something starts with Windows, and every row is explained", startup.Count > 0 && startup.TrueForAll(delegate(Finding f) { return !string.IsNullOrEmpty(f.Title) && !string.IsNullOrEmpty(f.Detail) && !string.IsNullOrEmpty(f.Tag) && f.Extra != null; }), startup.Count + " items");
            Check("every verdict is one the list knows how to show", startup.TrueForAll(delegate(Finding f) { return Array.IndexOf(new[] { "Keep", "Useful", "Optional", "OK", "Check", "Leftover", "Suspicious", "Off" }, f.Tag) >= 0; }));
            Finding self = startup.Find(delegate(Finding f) { return f.Path != null && f.Path.Equals(App.ExePath, StringComparison.OrdinalIgnoreCase); });
            if (self != null) Check("TaskBoost's own entry is not marked down for being unsigned", self.Level == 0 && self.Tag == "Keep", self.Tag + ": " + self.Detail);
            else Note("TaskBoost has no sign-in entry on this PC, so that check was skipped");
            int services = startup.FindAll(delegate(Finding f) { return f.Category == "service"; }).Count, tasks = startup.FindAll(delegate(Finding f) { return f.Category == "task"; }).Count;
            Note(startup.Count + " items: " + (startup.Count - services - tasks) + " sign-in entries, " + services + " services, " + tasks + " scheduled tasks");
            Finding missing = StartupInfo.Describe("Ghost", @"C:\No\Such\Folder\ghost.exe --start", "Starts at sign-in, this user");
            Check("an entry whose file is gone is a leftover", missing.Tag == "Leftover" && missing.Level == 1);
            Finding hidden = StartupInfo.Describe("Updater", "powershell.exe -WindowStyle Hidden -enc SQBFAFgA", "Starts at sign-in, this user");
            Check("a hidden, encoded PowerShell command is suspicious", hidden.Tag == "Suspicious" && hidden.Level == 2, hidden.Tag);
            Finding script = StartupInfo.Describe("Script", "cmd.exe /c echo hello", "Starts at sign-in, this user");
            Check("a plain command through cmd is worth a check", script.Tag == "Check" && script.Level == 1, script.Tag);
            Finding defender = StartupInfo.Describe("SecurityHealth", Path.Combine(Environment.SystemDirectory, "SecurityHealthSystray.exe"), "Starts at sign-in, all users");
            Check("the Windows Security icon is marked Keep", defender.Tag == "Keep" || !File.Exists(Path.Combine(Environment.SystemDirectory, "SecurityHealthSystray.exe")), defender.Tag);

            Section("Health");
            string report = Mcp.Capture(delegate { Care.Run(new[] { "health" }); });
            Check("the health report has uptime, memory, drives and disks", report.Contains("since the last restart") && report.Contains("Memory:") && report.Contains("Drive ") && report.Contains("Disk:"), report.Trim().Replace("\r\n", " / "));
            long total = Memory.TotalMb(), free = Memory.AvailableMb();
            Check("memory readings make sense", total > 1000 && free > 0 && free < total, free + " of " + total + " MB");
            Check("uptime is read", Care.Uptime.TotalSeconds > 30, Care.Uptime.ToString());
            var parts = HealthScore.Compute();
            int max = 0, score = 0;
            foreach (var p in parts) { max += p.Max; score += p.Points; }
            Check("the score has seven parts that add up to 100", parts.Count == 7 && max == 100);
            Check("each part's points are within its range, and the score is saved", parts.TrueForAll(delegate(HealthScore.Part p) { return p.Points >= 0 && p.Points <= p.Max && !string.IsNullOrEmpty(p.Why); }) && HealthScore.Last == score, "score " + score);
            foreach (var p in parts) Note(p.Name + ": " + p.Points + " / " + p.Max + "   " + p.Why);

            Section("Clean-up preview (real folders, nothing deleted)");
            string dry = Mcp.Capture(delegate { Cleaner.Run(new[] { "temp", "wintemp", "reports", "recycle", "dns", "clipboard" }, true); });
            Check("the preview covers every item and says it is a preview", dry.Contains("Temporary files:") && dry.Contains("Recycle Bin:") && dry.Contains("would flush") && dry.Contains("Clipboard: would be emptied") && dry.Contains("Preview:"), dry.Trim().Replace("\r\n", " / "));

            Section("Other things that are only read");
            Check("the Windows Update switch can be read", Updates.Stopped || !Updates.Stopped);
            string page = Updates.MakerPage();
            Check("the maker's driver page is a web address", page.StartsWith("https://"), page);
            Check("the sign-in task can be looked up", Startup.IsEnabled() || !Startup.IsEnabled());
            string mode = LaptopMode.Effective;
            Check("the laptop mode is one of the five", Array.IndexOf(LaptopMode.Modes, mode) >= 0, mode);
            Check("looking for Claude Code does not fail", ClaudeLink.ClaudeCodeInstalled() || !ClaudeLink.ClaudeCodeInstalled());
        }

        static string Describe(List<Finding> list)
        {
            var sb = new StringBuilder();
            foreach (var f in list) sb.Append(f.Title).Append(" | ").Append(f.Detail).Append("; ");
            return sb.ToString();
        }

        static bool Sorted(List<Finding> list)
        {
            for (int i = 1; i < list.Count; i++) if (list[i].Level > list[i - 1].Level) return false;
            return true;
        }

        // ---------------------------------------------------------------- the MCP server, as Claude talks to it

        static void McpServer()
        {
            Section("MCP server (TaskBoost.exe --mcp)");
            if (!File.Exists(App.ExePath))
            {
                Check("TaskBoost.exe is built", false, App.ExePath);
                return;
            }
            string data = FreshDir("mcp-data");
            var psi = new ProcessStartInfo(App.ExePath, "--mcp --data \"" + data + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            var json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
            using (var p = Process.Start(psi))
            {
                var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                input.NewLine = "\n";
                input.AutoFlush = true;
                Func<string, Dictionary<string, object>> ask = delegate(string line)
                {
                    input.WriteLine(line);
                    Task<string> answer = p.StandardOutput.ReadLineAsync();
                    if (!answer.Wait(90000)) throw new TimeoutException("no answer to " + line);
                    return json.Deserialize<Dictionary<string, object>>(answer.Result);
                };
                Func<Dictionary<string, object>, string> text = delegate(Dictionary<string, object> reply)
                {
                    var result = (Dictionary<string, object>)reply["result"];
                    var content = (System.Collections.ArrayList)result["content"];
                    return (string)((Dictionary<string, object>)content[0])["text"];
                };
                Func<string, string, Dictionary<string, object>> call = delegate(string tool, string arguments)
                {
                    return ask("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}");
                };

                var init = ask("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"selftest\",\"version\":\"1\"}}}");
                var info = (Dictionary<string, object>)init["result"];
                Check("initialize answers with the server's name and the protocol version asked for", (string)((Dictionary<string, object>)info["serverInfo"])["name"] == "taskboost" && (string)info["protocolVersion"] == "2025-06-18" && ((string)info["instructions"]).Length > 50);
                input.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); // a notification: no reply expected
                var ping = ask("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}");
                Check("a notification gets no reply, and ping does", Convert.ToInt32(ping["id"]) == 2 && ping.ContainsKey("result"));

                var list = (System.Collections.ArrayList)((Dictionary<string, object>)ask("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}")["result"])["tools"];
                var names = new List<string>();
                bool schemas = true;
                foreach (Dictionary<string, object> tool in list)
                {
                    names.Add((string)tool["name"]);
                    var schema = (Dictionary<string, object>)tool["inputSchema"];
                    if ((string)schema["type"] != "object" || !schema.ContainsKey("properties") || ((string)tool["description"]).Length < 20) schemas = false;
                }
                Check("the eight tools are offered, each with a description and a schema", schemas && string.Join(",", names) == "guard_scan,startup_items,health_report,cleanup_preview,free_memory,gaming_mode,buddy_say,buddy_visible", string.Join(",", names));

                string guard = text(call("guard_scan", "{}"));
                Check("guard_scan reports lines that start with ALERT, NOTICE or INFO", Regex.IsMatch(guard, @"^(ALERT|NOTICE|INFO)\s", RegexOptions.Multiline) || guard.StartsWith("All quiet"), guard.Split('\n')[0]);
                string items = text(call("startup_items", "{}"));
                Check("startup_items gives a verdict for every item", Regex.Matches(items, @"^\[(Keep|Useful|Optional|OK|Check|Leftover|Suspicious|Off)\] ", RegexOptions.Multiline).Count == items.Trim().Split('\n').Length, items.Trim().Split('\n').Length + " lines");
                string health = text(call("health_report", "{}"));
                Check("health_report has memory and drives", health.Contains("Memory:") && health.Contains("Drive "));
                string preview = text(call("cleanup_preview", "{}"));
                Check("cleanup_preview says what would be freed", preview.Contains("Preview: this clean-up would free about"));

                var said = call("buddy_say", "{\"text\":\"Salut! ăîșț\",\"mood\":\"love\"}");
                string inbox = Path.Combine(data, BuddyForm.InboxFile);
                bool written = File.Exists(inbox);
                string[] say = written ? File.ReadAllLines(inbox)[0].Split('|') : new string[0];
                Check("buddy_say leaves the words and the mood for the buddy, accents intact", written && say.Length == 3 && say[0] == "say" && say[1] == "love" && Encoding.UTF8.GetString(Convert.FromBase64String(say[2])) == "Salut! ăîșț");
                call("buddy_say", "{\"text\":\"x\",\"mood\":\"not-a-mood\"}");
                Check("an unknown mood falls back to happy", File.ReadAllLines(inbox)[1].Split('|')[1] == "happy");
                call("buddy_visible", "{\"visible\":false}");
                call("buddy_visible", "{\"visible\":true}");
                string[] lines = File.ReadAllLines(inbox);
                Check("buddy_visible leaves hide and show", lines[2] == "hide" && lines[3] == "show");
                Check("the server keeps a sign of life for the buddy while connected", File.Exists(Path.Combine(data, BuddyForm.AliveFile)));

                var unknown = call("no_such_tool", "{}");
                Check("an unknown tool is an error answer, not a crash", (bool)((Dictionary<string, object>)unknown["result"])["isError"]);
                var missingArgument = call("buddy_say", "{}");
                Check("a missing argument is an error answer", (bool)((Dictionary<string, object>)missingArgument["result"])["isError"]);
                var method = ask("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"resources/list\"}");
                Check("an unknown method gets the standard error", Convert.ToInt32(((Dictionary<string, object>)method["error"])["code"]) == -32601);
                var broken = ask("{ this is not json");
                Check("a broken message gets a parse error and the server carries on", Convert.ToInt32(((Dictionary<string, object>)broken["error"])["code"]) == -32700 && ask("{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"ping\"}").ContainsKey("result"));

                p.StandardInput.Close();
                Check("the server stops when Claude disconnects", p.WaitForExit(8000));
                Check("and takes its sign of life with it", !File.Exists(Path.Combine(data, BuddyForm.AliveFile)));
            }
        }

        // ---------------------------------------------------------------- the window, off-screen

        static IntPtr Pt(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }

        static void Click(Control c, int x, int y, bool right = false)
        {
            PostMessage(c.Handle, right ? 0x204u : 0x201u, (IntPtr)(right ? 2 : 1), Pt(x, y));
            Pump(60);
            PostMessage(c.Handle, right ? 0x205u : 0x202u, IntPtr.Zero, Pt(x, y));
            Pump(80);
        }

        // How many clearly different colours a control paints: a blank or broken one has very few
        static int Colours(Control c)
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
                var seen = new HashSet<int>();
                for (int y = 0; y < bmp.Height; y += 7)
                    for (int x = 0; x < bmp.Width; x += 7) seen.Add(bmp.GetPixel(x, y).ToArgb() & 0xF0F0F0);
                return seen.Count;
            }
        }

        static void Window()
        {
            Section("The window (shown off-screen, Romanian, on sandbox settings)");
            // Never a first run: that would switch "start with Windows" on
            Settings.Save("guard.txt", new[] { "v2", "v3", "v4", "v5", "sensors", "network", "keyboards", "remote", "startup", "shield", "notify", "tray", "buddy" });
            File.WriteAllText(Path.Combine(App.DataDir, "laptop-mode.txt"), "work");
            object runBefore = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Guard.OwnRunName, null);
            Lang.Romanian = true;
            Lang.Missed = new HashSet<string>();
            Motion.On = true;

            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                var form = new MainForm(false, show, exit);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                Pump(300);
                var pages = Field<Control[]>(form, "pages");
                var tabs = Field<TabBar>(form, "tabs");
                var slide = Field<PageSlide>(form, "slide");
                var buddy = Field<BuddyForm>(form, "buddy");
                string[] names = { "Home", "Boost", "Guard", "Startup", "Turbo", "Care" };

                Check("it opens on the Home tab", pages[0].Visible && tabs.Selected == 0);
                var homeList = Field<FindingList>(pages[0], "list");
                Check("the health score is worked out and listed", PumpUntil(delegate { return homeList.Items.Count == 7; }, 40000), homeList.Items.Count + " rows");

                // Tabs: by the method the tab bar and Ctrl+number use, by a click on the bar, and by the arrow keys
                for (int i = 1; i < pages.Length; i++)
                {
                    Call(form, "ShowPage", i);
                    bool sliding = slide.Visible;
                    Pump(450);
                    int colours = Colours(pages[i]);
                    Check(names[i] + " tab shows, slides in, and draws its content", pages[i].Visible && !pages[i - 1].Visible && tabs.Selected == i && sliding && !slide.Visible && colours > 12, colours + " colours, sliding " + sliding);
                }
                Click(tabs, tabs.Width / 12, tabs.Height / 2);
                Pump(450);
                Check("a click on the first tab goes back to Home", pages[0].Visible && Field<int>(form, "current") == 0);
                Call(form, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.D3));
                Pump(450);
                Check("Ctrl+3 opens Guard", pages[2].Visible);
                Call(tabs, "OnKeyDown", new KeyEventArgs(Keys.Right));
                Pump(450);
                Check("the right arrow on the tab bar opens the next tab", pages[3].Visible && tabs.Selected == 3);

                // Lists
                var startupList = Field<FindingList>(pages[3], "list");
                Check("the Startup list fills", PumpUntil(delegate { return startupList.Items.Count > 0; }, 40000), startupList.Items.Count + " rows");
                var guardList = Field<FindingList>(pages[2], "list");
                Check("the Guard list fills", PumpUntil(delegate { return guardList.Items.Count > 0; }, 20000), guardList.Items.Count + " rows");

                // A switch on the Boost tab: the click flips it, saves the choice, and the knob travels
                Call(form, "ShowPage", 1);
                Pump(450);
                var cards = Field<Dictionary<string, ToggleCard>>(pages[1], "cards");
                ToggleCard card = cards["theme"];
                bool before = card.Checked;
                Anim knob = Field<Anim>(card, "slide");
                Click(card, card.Width / 2, card.Height / 2);
                bool flipped = card.Checked != before, saved = Settings.Load().Contains("theme") == card.Checked;
                Check("a click flips a switch and saves the choice", flipped && saved);
                Check("the knob glides rather than jumps, and arrives", PumpUntil(delegate { return knob.Value == (card.Checked ? 1F : 0F); }, 1500) && knob.Target == (card.Checked ? 1F : 0F));
                Click(card, card.Width / 2, card.Height / 2);
                Pump(400);
                Check("a second click puts it back", card.Checked == before);

                // Movement itself
                float seenMid = -1F;
                var anim = new Anim(0F, delegate { });
                anim.To(1F, 300);
                Pump(110);
                seenMid = anim.Value;
                Pump(400);
                Check("an animation is part-way after a third of its time and done at the end", seenMid > 0.05F && seenMid < 0.99F && anim.Value == 1F, "mid " + seenMid);
                Pump(600); // nothing else is moving now, so the shared timer is at rest
                var crossed = new Anim(0F, delegate { });
                Task.Factory.StartNew(delegate { crossed.To(1F, 200); }).Wait();
                Check("an animation started from a worker thread is handed to the window's thread and still runs", PumpUntil(delegate { return crossed.Value == 1F; }, 2500), "value " + crossed.Value);
                var after = new Anim(0F, delegate { });
                after.To(1F, 150);
                Check("and the ones started after it run too", PumpUntil(delegate { return after.Value == 1F; }, 2500));
                Motion.On = false;
                var still = new Anim(0F, delegate { });
                still.To(1F, 300);
                Check("with animations switched off, things are at once where they belong", still.Value == 1F);
                Call(form, "ShowPage", 4);
                Check("and tabs change without a slide", !slide.Visible && pages[4].Visible);
                Motion.On = true;

                // Keyboard help and the two kinds of dialog
                bool answer = false;
                Action<DialogResult> closeSoon = delegate(DialogResult result)
                {
                    // Keeps looking until the dialog is really open, then answers it
                    var t = new System.Windows.Forms.Timer();
                    t.Interval = 200;
                    t.Tick += delegate
                    {
                        foreach (Form f in Application.OpenForms)
                            if (f is Dialog && f.Visible)
                            {
                                t.Stop();
                                t.Dispose();
                                f.DialogResult = result;
                                return;
                            }
                    };
                    t.Start();
                };
                closeSoon(DialogResult.Yes);
                answer = Dialog.Ask(form, "Put every setting back to the value it had before TaskBoost first changed it?");
                Check("a question answered Yes returns yes", answer);
                closeSoon(DialogResult.No);
                answer = Dialog.Ask(form, "Put every setting back to the value it had before TaskBoost first changed it?");
                Check("answered No (or closed) returns no", !answer);
                closeSoon(DialogResult.Yes);
                bool fromWorker = false;
                Task worker = Task.Factory.StartNew(delegate { fromWorker = Dialog.Ask(null, "Select an item in the list first."); });
                PumpUntil(delegate { return worker.IsCompleted; }, 5000);
                Check("a question asked from a worker thread is shown by the window's thread and answered", worker.IsCompleted && fromWorker);
                closeSoon(DialogResult.No);
                Dialog.Shortcuts(form, MainForm.ShortcutHelp);
                Check("the shortcut list opens and closes", true);

                // Tray menu
                var trayMenu = Field<PopMenu>(form, "trayMenu");
                trayMenu.Pinned = true; // the real mouse, wherever it is, must not close it
                trayMenu.ShowAt(new Point(-29000, -29000));
                Pump(400);
                var trayItems = Field<List<PopItem>>(trayMenu, "items");
                Check("the tray menu opens with its six commands", trayMenu.IsOpen && trayItems.FindAll(delegate(PopItem i) { return i.Kind == PopKind.Row; }).Count == 6 && trayItems.TrueForAll(delegate(PopItem i) { return i.Kind != PopKind.Row || i.Title.Length > 0; }));
                trayMenu.Dismiss();
                Pump(300);
                Check("and closes", !trayMenu.Visible);

                // Shortcuts arrive as WM_HOTKEY; index 5 is Ctrl+Alt+F7, the buddy away and back
                bool goneBefore = buddy.IsGone;
                PostMessage(form.Handle, 0x312, (IntPtr)5, IntPtr.Zero);
                Pump(300);
                bool gone = buddy.IsGone;
                PostMessage(form.Handle, 0x312, (IntPtr)5, IntPtr.Zero);
                Pump(300);
                Check("the buddy shortcut sends it away and calls it back", !goneBefore && gone && !buddy.IsGone);

                // Pomodoro: 25 minutes of work, 5 of break, while the laptop mode is Study / work
                typeof(MainForm).GetField("phase", Any).SetValue(form, ""); // the window's own timer may have started one already
                Call(form, "PomodoroTick");
                Check("in Study / work mode a focus period starts", Field<string>(form, "phase") == "work" && ((string)Call(form, "PomodoroStatus")).Contains("25 min"), (string)Call(form, "PomodoroStatus"));
                typeof(MainForm).GetField("phaseStart", Any).SetValue(form, DateTime.Now.AddMinutes(-26));
                Call(form, "PomodoroTick");
                Check("after 25 minutes comes the break", Field<string>(form, "phase") == "break");
                typeof(MainForm).GetField("phaseStart", Any).SetValue(form, DateTime.Now.AddMinutes(-6));
                Call(form, "PomodoroTick");
                Check("after 5 minutes it is back to work", Field<string>(form, "phase") == "work");
                File.WriteAllText(Path.Combine(App.DataDir, "laptop-mode.txt"), "normal");
                Call(form, "PomodoroTick");
                Check("leaving Study / work ends it", Field<string>(form, "phase") == "" && Call(form, "PomodoroStatus") == null);

                // The buddy: clicks, the menu, and a drop on another program's window
                Buddy(buddy);

                // Esc tucks the window away in the tray; the exit signal really closes it
                typeof(MainForm).GetField("trayHintShown", Any).SetValue(form, true); // no "still here" balloon from a test
                Call(form, "OnKeyDown", new KeyEventArgs(Keys.Escape));
                Pump(300);
                Check("Esc hides the window but keeps TaskBoost running", !form.Visible && !form.IsDisposed);
                show.Set();
                Check("a second launch (the show signal) brings the window back", PumpUntil(delegate { return form.Visible; }, 3000));
                exit.Set();
                Check("the exit signal closes TaskBoost", PumpUntil(delegate { return form.IsDisposed; }, 4000));
            }

            var untranslated = new List<string>();
            foreach (string s in Lang.Missed)
                if (s.Trim().Length > 1 && !Regex.IsMatch(s, @"^[\W\d]+$") && !Regex.IsMatch(s, @"^\d+ / \d+$") && s != "TaskBoost" && s != "Boost" && s != "Turbo" && s != "OK") untranslated.Add(s);
            Lang.Missed = null;
            Lang.Romanian = false;
            foreach (string s in untranslated) Note("shown in English while Romanian was on: \"" + s.Replace("\n", "\\n") + "\"");
            Check("nothing on the six tabs, the menus or the dialogs was left in English", untranslated.Count == 0, untranslated.Count + " texts");
            object runAfter = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Guard.OwnRunName, null);
            Check("the test left TaskBoost's real sign-in entry exactly as it was", Equals(runBefore, runAfter), runBefore + " -> " + runAfter);
            Check("no errors were logged", Log.Errors == 0, Log.Errors + " errors");
        }

        static void Buddy(BuddyForm buddy)
        {
            Section("The buddy");
            var menu = Field<PopMenu>(buddy, "menu");
            var ask = Field<PopMenu>(buddy, "ask");
            menu.Pinned = ask.Pinned = true; // the real mouse, wherever it is, must not close them
            string picked = null;
            IntPtr focusWindow = IntPtr.Zero;
            int focusCalls = 0;
            buddy.SetLaptopMode = delegate(string m) { picked = m; };
            buddy.FocusChanged = delegate(IntPtr w, int pid) { focusWindow = w; focusCalls++; };
            PumpUntil(delegate { return buddy.Visible; }, 3000);
            Check("it is on screen", buddy.Visible);

            // A long press while it walks: this used to turn into a drag and a drop
            typeof(BuddyForm).GetField("mode", Any).SetValue(buddy, Enum.Parse(typeof(BuddyForm).GetNestedType("Mode", Any), "Walk"));
            typeof(BuddyForm).GetField("modeTicks", Any).SetValue(buddy, 200);
            Pump(300);
            Point before = buddy.Location;
            PostMessage(buddy.Handle, 0x201, (IntPtr)1, Pt(buddy.Width / 2, buddy.Height / 2));
            Pump(700);
            bool stoodStill = buddy.Location == before;
            PostMessage(buddy.Handle, 0x202, IntPtr.Zero, Pt(buddy.Width / 2, buddy.Height / 2));
            Pump(500);
            Check("it stands still while the mouse button is held on it", stoodStill, before + " -> " + buddy.Location);
            Check("a left click opens the menu and is not taken for a drag", menu.IsOpen && !Field<bool>(buddy, "dragging") && focusCalls == 0);
            Point shown = buddy.Location;
            Pump(900);
            Check("it stays put while its menu is open, turned red and waving", buddy.Location == shown && Field<string>(buddy, "mood") == "selected");

            var items = Field<List<PopItem>>(menu, "items");
            PopItem chips = items.Find(delegate(PopItem i) { return i.Kind == PopKind.Chips; });
            PopItem emotes = items.Find(delegate(PopItem i) { return i.Opens != null; });
            Check("the menu has the five modes, the folded emotes and four commands", chips.Chips.Length == 5 && emotes.Opens.Faces.Length == BuddySprite.Moods.Length && items.FindAll(delegate(PopItem i) { return i.Kind == PopKind.Row; }).Count == 5);
            int foldedHeight = menu.Height;
            Click(menu, emotes.Bounds.X + 30, emotes.Bounds.Y + 8);
            Check("a click on Emotes unfolds the faces and the menu grows", PumpUntil(delegate { return emotes.Opens.Open.Value >= 1F; }, 1500) && menu.Height > foldedHeight && menu.IsOpen);
            PopItem grid = emotes.Opens;
            Click(menu, grid.Bounds.X + grid.Bounds.Width / 2, grid.Bounds.Y + grid.Bounds.Height / 2);
            Pump(300);
            string mood = Field<string>(buddy, "mood");
            Check("picking a face closes the menu and the buddy makes that face", !menu.IsOpen && Array.IndexOf(BuddySprite.Moods, mood) >= 0, mood);

            Click(buddy, buddy.Width / 2, buddy.Height / 2, true);
            Pump(400);
            Check("a right click opens the same menu", menu.IsOpen);
            chips = items.Find(delegate(PopItem i) { return i.Kind == PopKind.Chips; });
            Click(menu, chips.Bounds.X + 16, chips.Bounds.Y + 16);
            Pump(300);
            Check("picking the first mode face asks for Low power", picked == "low" && !menu.IsOpen, picked);
            Click(buddy, buddy.Width / 2, buddy.Height / 2);
            Pump(350);
            bool opened = menu.IsOpen;
            Click(buddy, buddy.Width / 2, buddy.Height / 2);
            Pump(350);
            Check("a click while the menu is open puts it away", opened && !menu.IsOpen);

            // A drop on another program's window: the buddy sits there and asks; net focus starts only on request
            // The buddy is carried to the middle of another program's window and let go there, as the mouse would
            RECT r = new RECT();
            bool found = false;
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    IntPtr h = p.MainWindowHandle;
                    if (found || h == IntPtr.Zero || p.Id == Process.GetCurrentProcess().Id || !IsWindowVisible(h) || IsIconic(h) || Guard.ProcessPath(p.Id) == null || !GetWindowRect(h, out r)) continue;
                    var middle = new Point((r.L + r.R) / 2, (r.T + r.B) / 2);
                    found = r.R - r.L > 300 && r.B - r.T > 300 && Array.Exists(Screen.AllScreens, delegate(Screen s) { return s.Bounds.Contains(middle); });
                }
            if (!found)
            {
                Note("no other program has a window on screen, so the drop was not tried");
                return;
            }
            buddy.Location = new Point((r.L + r.R - buddy.Width) / 2, (r.T + r.B - buddy.Height) / 2);
            Call(buddy, "Drop");
            Pump(500);
            IntPtr other = Field<IntPtr>(buddy, "focusWindow");
            var askItems = Field<List<PopItem>>(ask, "items");
            if (other == IntPtr.Zero)
            {
                Note("the window in front could not be sat on (a shell window), so the drop was not tried");
                return;
            }
            Check("dropped on a program's window it sits on that window", Field<bool>(buddy, "perched") && other != IntPtr.Zero);
            Check("and asks, naming the program, instead of starting net focus", ask.IsOpen && focusCalls == 0 && askItems[0].Title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), askItems[0].Title);
            PopItem sit = askItems.FindLast(delegate(PopItem i) { return i.Kind == PopKind.Row; });
            Click(ask, sit.Bounds.X + 40, sit.Bounds.Y + 8);
            Pump(300);
            Check("\"Just sit here\" leaves the network alone", !ask.IsOpen && focusCalls == 0 && !Field<bool>(buddy, "netFocus"));
            ask.ShowBeside(new Rectangle(buddy.Left, buddy.Bottom - buddy.Width, buddy.Width, buddy.Width));
            Pump(400);
            PopItem go = askItems.Find(delegate(PopItem i) { return i.Kind == PopKind.Row; });
            Click(ask, go.Bounds.X + 40, go.Bounds.Y + 8);
            Pump(300);
            Check("\"Net focus\" starts it for that window, once", focusCalls == 1 && focusWindow == other && Field<bool>(buddy, "netFocus"));
            Call(buddy, "EndFocus");
            Check("leaving the window ends net focus", focusCalls == 2 && focusWindow == IntPtr.Zero && !Field<bool>(buddy, "netFocus"));

            buddy.Say("Checking the PC...", "think");
            Pump(200);
            var bubble = Field<BubbleForm>(buddy, "bubble");
            Check("it speaks in a bubble", bubble.Visible);
        }

        // ---------------------------------------------------------------- round trips on the real system

        static string ActiveScheme()
        {
            string output;
            Util.Run("powercfg.exe", "/getactivescheme", out output);
            return Regex.Match(output, "[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", RegexOptions.IgnoreCase).Value;
        }

        // The current AC and DC values of a power setting
        static string PowerValue(string group, string setting)
        {
            string output;
            Util.Run("powercfg.exe", "/qh SCHEME_CURRENT " + group + " " + setting, out output);
            var m = Regex.Matches(output, @":\s*0x([0-9a-fA-F]{8})\s*$", RegexOptions.Multiline);
            return m.Count >= 2 ? m[m.Count - 2].Groups[1].Value + "/" + m[m.Count - 1].Groups[1].Value : "?";
        }

        static void Live()
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run", approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            const string entry = "TaskBoostSelfTest";
            string realDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);

            Section("LIVE: the guard notices a new sign-in entry (a test entry, removed again)");
            var watch = new[] { "startup" };
            Guard.Scan(watch);
            Guard.Scan(watch); // twice, so the starting point is on record before the entry appears
            string command = "\"" + Path.Combine(Environment.SystemDirectory, "notepad.exe") + "\" selftest";
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(run)) k.SetValue(entry, command);
                var after = Guard.Scan(watch);
                Finding alert = after.Find(delegate(Finding f) { return f.Title.Contains(entry); });
                Check("a program that adds itself to startup raises an alert", alert != null && alert.Level == 2, Describe(after));
                Guard.MarkKnown(new[] { alert == null ? "" : alert.Key });
                Check("after Mark as known it is quiet", !Guard.Scan(watch).Exists(delegate(Finding f) { return f.Title.Contains(entry); }));

                Section("LIVE: switching a sign-in entry off and on (the same test entry)");
                Finding listed = StartupInfo.Scan().Find(delegate(Finding f) { return f.Title == entry; });
                Check("the entry is in the Startup list with a switch", listed != null && listed.Ref != null && !listed.Off, listed == null ? "not listed" : listed.Tag);
                StartupInfo.SetEnabled(listed.Ref, false);
                byte[] flag;
                using (var k = Registry.CurrentUser.OpenSubKey(approved)) flag = k == null ? null : k.GetValue(entry) as byte[];
                Check("switched off: Windows' own startup switch says off, the entry itself is untouched", flag != null && flag[0] == 3 && (string)Registry.GetValue(@"HKEY_CURRENT_USER\" + run, entry, null) == command);
                Check("the list shows it as Off", StartupInfo.Scan().Find(delegate(Finding f) { return f.Title == entry; }).Off);
                StartupInfo.SetEnabled(listed.Ref, true);
                using (var k = Registry.CurrentUser.OpenSubKey(approved)) flag = k.GetValue(entry) as byte[];
                Check("switched on again", flag != null && flag[0] == 2 && !StartupInfo.Scan().Find(delegate(Finding f) { return f.Title == entry; }).Off);
            }
            finally
            {
                using (var k = Registry.CurrentUser.OpenSubKey(run, true)) if (k != null) k.DeleteValue(entry, false);
                using (var k = Registry.CurrentUser.OpenSubKey(approved, true)) if (k != null) k.DeleteValue(entry, false);
            }
            Check("the test entry is gone again", Registry.GetValue(@"HKEY_CURRENT_USER\" + run, entry, null) == null);

            Section("LIVE: Boost tweaks, applied and put back (the per-user ones; nothing changes if they are already applied)");
            var selected = new List<string>();
            foreach (var g in Tweaks.Groups) if (!g.NeedsAdmin) selected.Add(g.Key);
            string plan = ActiveScheme(), cpu = PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX"), video = PowerValue("SUB_VIDEO", "VIDEOIDLE");
            object theme = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
            object wallpaper = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
            object small = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSmallIcons", null);
            int errors = Log.Errors;
            State.FileName = "state.txt";
            State.Clear();
            string applied = Mcp.Capture(delegate { Tweaks.Apply(selected, false); });
            Check("all six per-user groups apply without an error", Regex.Matches(applied, "^done: ", RegexOptions.Multiline).Count == 6 && Log.Errors == errors, applied.Trim().Replace("\r\n", " / "));
            Check("dark mode, black desktop and the thin taskbar are in place", Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0 && (string)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "") == "0 0 0" && Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSmallIcons", 0)) == 1);
            Check("the battery settings are in place (CPU capped at 99% on battery)", PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX").EndsWith("/00000063"), PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX"));
            var record = State.Load();
            Check("every change has an undo line", record.Count > 40 && record.Exists(delegate(State.Entry e) { return e.Kind == "spi"; }) && record.Exists(delegate(State.Entry e) { return e.Kind == "pwr"; }), record.Count + " lines");
            string restored = Mcp.Capture(Tweaks.RestoreUser);
            Check("Restore runs without an error and empties the undo record", restored.Contains("done: restored per-user settings") && Log.Errors == errors && State.Load().Count == 0, restored.Trim().Replace("\r\n", " / "));
            Check("every setting is back exactly where it was before the test",
                ActiveScheme() == plan && PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX") == cpu && PowerValue("SUB_VIDEO", "VIDEOIDLE") == video
                && Equals(theme, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null))
                && Equals(wallpaper, Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null))
                && Equals(small, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSmallIcons", null)),
                "cpu " + cpu + " -> " + PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX"));

            Section("LIVE: gaming mode on and off (the part that needs no administrator; no programs are closed)");
            object dvr = Registry.GetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", null);
            string gameOn = Mcp.Capture(delegate { GameMode.On(new string[0]); });
            Check("it switches on: Windows Game Mode on, recording off, memory freed with no program closed", GameMode.IsOn && gameOn.Contains("Gaming mode is ON") && Regex.IsMatch(gameOn, @"memory: \d+ MB free before, \d+ MB after \(\d+ programs trimmed") && Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", 0)) == 1 && Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 1)) == 0, gameOn.Trim().Replace("\r\n", " / "));
            Check("while it is on, the laptop mode counts as gaming", LaptopMode.Effective == "turbo");
            string gameOff = Mcp.Capture(GameMode.Off);
            Check("it switches off and puts the power plan and settings back", !GameMode.IsOn && gameOff.Contains("Gaming mode is OFF") && ActiveScheme() == plan && Equals(dvr, Registry.GetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", null)) && Log.Errors == errors, gameOff.Trim().Replace("\r\n", " / "));

            Section("LIVE: phone charging mode on and off (without administrator)");
            string lid = PowerValue("SUB_BUTTONS", "LIDACTION"), sleep = PowerValue("SUB_SLEEP", "STANDBYIDLE"), hibernate = PowerValue("SUB_SLEEP", "HIBERNATEIDLE");
            string chargeOn = Mcp.Capture(ChargeMode.On);
            Check("on: the lid does nothing and the PC does not sleep", ChargeMode.IsOn && PowerValue("SUB_BUTTONS", "LIDACTION") == "00000000/00000000" && PowerValue("SUB_SLEEP", "STANDBYIDLE") == "00000000/00000000", chargeOn.Trim().Replace("\r\n", " / "));
            string chargeOff = Mcp.Capture(ChargeMode.Off);
            Check("off: lid, sleep and hibernate are back exactly as they were", !ChargeMode.IsOn && PowerValue("SUB_BUTTONS", "LIDACTION") == lid && PowerValue("SUB_SLEEP", "STANDBYIDLE") == sleep && PowerValue("SUB_SLEEP", "HIBERNATEIDLE") == hibernate && Log.Errors == errors,
                "lid " + lid + " -> " + PowerValue("SUB_BUTTONS", "LIDACTION") + ", sleep " + sleep + " -> " + PowerValue("SUB_SLEEP", "STANDBYIDLE"));

            Section("LIVE: laptop modes (ends on the mode TaskBoost is really in)");
            string realModeFile = Path.Combine(realDir, "laptop-mode.txt");
            string realMode = File.Exists(realModeFile) ? File.ReadAllText(realModeFile).Trim() : "normal";
            if (Array.IndexOf(new[] { "low", "normal", "best", "work" }, realMode) < 0) realMode = "normal";
            object toastsBefore = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", null);
            string cpuBefore = PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX");
            // Start from the real mode, so the "before" values the sandbox remembers are the real current ones
            LaptopMode.Set(realMode);
            foreach (string mode in new[] { "work", "low", "best", "normal" })
            {
                // A mode whose power plan this PC does not have would make TaskBoost create that plan: not in a test
                string alias = mode == "low" ? "SCHEME_MAX" : mode == "best" ? "SCHEME_MIN" : null, probe;
                if (alias != null && Util.Run("powercfg.exe", "/query " + alias + " SUB_SLEEP STANDBYIDLE", out probe) != 0)
                {
                    Note("mode \"" + mode + "\" skipped: this PC does not have that power plan, and the test must not create one");
                    continue;
                }
                string said = Mcp.Capture(delegate { LaptopMode.Set(mode); });
                bool toastsOff = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1)) == 0;
                bool ok = LaptopMode.Current == mode && said.Contains("laptop mode: " + mode) && (said.Contains("power plan:") || said.Contains("using Balanced with the power mode slider"));
                if (mode == "work") ok = ok && toastsOff && PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX").EndsWith("/00000064");
                Check("mode \"" + mode + "\" is set" + (mode == "work" ? ": notifications off, full CPU speed on battery" : ""), ok, said.Trim().Replace("\r\n", " / "));
            }
            LaptopMode.Set(realMode);
            Check("back in \"" + realMode + "\", with notifications and the CPU limit as before the test",
                Equals(toastsBefore, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", null)) && PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX") == cpuBefore && Log.Errors == errors,
                "cpu " + cpuBefore + " -> " + PowerValue("SUB_PROCESSOR", "PROCTHROTTLEMAX") + ", toasts " + toastsBefore + " -> " + Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", null));


            Section("LIVE: looking online for updates (read-only; this can take a minute)");
            string updates = Mcp.Capture(Updates.Check);
            Check("the device and its BIOS are identified", updates.Contains("Device: ") && updates.Contains("BIOS installed: "), updates.Trim().Replace("\r\n", " / "));
            Check("Windows Update answers, or says why it cannot", updates.Contains("Windows Update") && updates.Contains("Update check finished."));
            foreach (string line in updates.Trim().Split('\n')) Note(line.Trim());
        }
    }
}

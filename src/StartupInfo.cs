using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TaskBoost
{
    // Lists everything that starts with Windows and explains, in plain words, what each item is
    static class StartupInfo
    {
        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static readonly string[] ScriptHosts = { "powershell", "pwsh", "cmd", "wscript", "cscript", "mshta", "rundll32", "regsvr32" };
        // Names malware likes to borrow; the real ones only live in the Windows folder
        public static readonly string[] SystemNames = { "svchost", "lsass", "csrss", "winlogon", "services", "smss", "explorer", "dllhost", "taskhostw", "runtimebroker", "spoolsv", "conhost", "dwm", "sihost" };

        public static List<Finding> Scan()
        {
            var list = new List<Finding>();
            string[] runKeys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string path in runKeys)
                    using (var k = hive.OpenSubKey(path))
                    {
                        if (k == null) continue;
                        string hiveName = hive == Registry.CurrentUser ? "HKCU" : "HKLM";
                        // Windows keeps the on/off switch for these in a matching "StartupApproved" key; RunOnce has none
                        string approved = path.EndsWith("RunOnce") ? null : path.Contains("WOW6432Node") ? "Run32" : "Run";
                        foreach (string name in k.GetValueNames())
                            list.Add(Mark(Describe(name, Convert.ToString(k.GetValue(name)), hive == Registry.CurrentUser ? "Starts at sign-in, this user" : "Starts at sign-in, all users"),
                                approved == null ? null : new[] { "run", hiveName, approved, name }));
                    }

            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                string dir = Environment.GetFolderPath(folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir))
                    if (!file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        list.Add(Mark(Describe(Path.GetFileNameWithoutExtension(file), file, "Startup folder"),
                            new[] { "run", folder == Environment.SpecialFolder.Startup ? "HKCU" : "HKLM", "StartupFolder", Path.GetFileName(file) }));
            }

            Services(list);
            Tasks(list);
            list.Sort(delegate(Finding a, Finding b)
            {
                int c = b.Level.CompareTo(a.Level);
                if (c == 0) c = Rank(a.Category).CompareTo(Rank(b.Category)); // sign-in items, then services, then scheduled tasks
                return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static int Rank(string category) { return category == "startup" ? 0 : category == "service" ? 1 : 2; }

        // Scheduled tasks outside Windows' own folders: the other common place for things that start by themselves
        static void Tasks(List<Finding> list)
        {
            string output;
            if (Util.Run("schtasks.exe", "/query /fo CSV /v", out output) != 0) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in output.Split('\n'))
            {
                var c = Csv(line.TrimEnd('\r'));
                if (c.Count < 19 || c[0] == "HostName") continue; // the header repeats before every task folder
                string name = c[1], command = c[8], state = c[11], kind = c[18];
                if (name.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) || name == @"\" + App.Name || !seen.Add(name)) continue;
                if (command.IndexOf('\\') < 0) continue; // COM handlers and the like: nothing to explain or switch
                var f = Describe(name.TrimStart('\\'), command, "Scheduled task, " + kind.ToLowerInvariant());
                f.Category = "task";
                list.Add(Mark(f, new[] { "task", name, state }));
            }
        }

        static List<string> Csv(string line)
        {
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch != '"') cur.Append(ch);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else quoted = false;
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { cells.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(ch);
            }
            cells.Add(cur.ToString());
            return cells;
        }

        // Services that start by themselves and are not part of Windows
        static void Services(List<Finding> list)
        {
            using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                foreach (string name in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(name))
                    {
                        // Automatic services, plus the ones TaskBoost itself switched off so they can be switched back on
                        bool off = OffServices().Contains(name);
                        if (k == null || (Convert.ToInt32(k.GetValue("Start", 4)) != 2 && !off) || (Convert.ToInt32(k.GetValue("Type", 0)) & 0x30) == 0) continue;
                        string image = k.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (string.IsNullOrEmpty(image)) continue;
                        string args, path = ExePath(image, out args);
                        if (path.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase) && path.IndexOf("\\DriverStore\\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var f = Describe(Indirect(k.GetValue("DisplayName") as string) ?? name, image, "Background service (" + name + ")");
                        f.Category = "service";
                        list.Add(Mark(f, new[] { "service", name }));
                    }
        }

        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        static string OffFile { get { return Path.Combine(App.DataDir, "startup-off.txt"); } }

        static List<string> OffServices()
        {
            return File.Exists(OffFile) ? new List<string>(File.ReadAllLines(OffFile)) : new List<string>();
        }

        static Finding Mark(Finding f, string[] reference)
        {
            f.Ref = reference;
            if (reference == null || !IsOff(reference)) return f;
            f.Off = true;
            f.Level = 0;
            f.Tag = "Off";
            f.Detail = "Switched off. " + f.Detail;
            return f;
        }

        static bool IsOff(string[] r)
        {
            if (r[0] == "service") return OffServices().Contains(r[1]);
            if (r[0] == "task") return r.Length > 2 && r[2].Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            using (var k = (r[1] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(Approved + r[2]))
            {
                var state = k == null ? null : k.GetValue(r[3]) as byte[];
                return state != null && state.Length > 0 && (state[0] & 1) == 1; // 02 = on, 03 = off: the same flag Task Manager sets
            }
        }

        // Sign-in entries use the switch Task Manager uses, so nothing is deleted and it can be undone from either place.
        // Services are changed between Automatic and Manual.
        public static void SetEnabled(string[] r, bool on)
        {
            try
            {
                if (r[0] == "task")
                {
                    if (Util.RunLogged("schtasks.exe", "/change /tn \"" + r[1] + "\" " + (on ? "/enable" : "/disable")))
                        Log.Info("scheduled task " + r[1] + (on ? ": enabled" : ": disabled"));
                    return;
                }
                if (r[0] == "service")
                {
                    if (!Util.RunLogged("sc.exe", "config \"" + r[1] + "\" start= " + (on ? "auto" : "demand"))) return;
                    var off = OffServices();
                    off.Remove(r[1]);
                    if (!on) off.Add(r[1]);
                    File.WriteAllLines(OffFile, off);
                    Log.Info("service " + r[1] + (on ? ": starts with Windows again" : ": no longer starts with Windows (from the next restart)"));
                    return;
                }
                var state = new byte[12];
                state[0] = (byte)(on ? 2 : 3);
                if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(state, 4);
                using (var k = (r[1] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey(Approved + r[2]))
                    k.SetValue(r[3], state, RegistryValueKind.Binary);
                Log.Info("startup entry " + r[3] + (on ? ": switched on" : ": switched off"));
            }
            catch (Exception ex) { Log.Error("could not switch " + r[r.Length - 1] + ": " + ex.Message); }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);

        // Service names are often stored as "@file.dll,-123" resource references
        static string Indirect(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '@') return string.IsNullOrEmpty(s) ? null : s;
            var sb = new StringBuilder(512);
            return SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }

        // Title = name, Detail = what it does and whether to keep it, Extra = signer, source and command, Tag = verdict
        public static Finding Describe(string name, string command, string source)
        {
            var f = new Finding { Category = "startup", Key = name + "|" + command, Title = name };
            string args, path = ExePath(command, out args);
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) path = ShortcutTarget(path, ref args);
            string exe = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool exists = File.Exists(path);
            string publisher = exists ? Guard.Publisher(path) : null;
            f.Path = exists ? path : null;
            f.Extra = (exists ? (publisher != null ? "Signed by " + publisher : "NOT SIGNED") + "  -  " : "") + source + "  -  " + command;

            // Apps installed with an "Update.exe" launcher name the real program in the arguments
            string lookup = exe;
            var squirrel = Regex.Match(args, @"--processStart\s+""?([^""\s]+?)(\.exe)?""?(\s|$)", RegexOptions.IgnoreCase);
            if (exe == "update" && squirrel.Success) lookup = squirrel.Groups[1].Value;

            string importance, what;
            bool inWindows = path.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase);
            if (!exists)
            {
                Set(f, 1, "Leftover", "The file it points to no longer exists, so this entry does nothing. It can be removed.");
            }
            else if (Array.IndexOf(SystemNames, exe) >= 0 && !inWindows)
            {
                Set(f, 2, "Suspicious", "Uses the name of a Windows component but is not in the Windows folder. Real copies of " + exe + ".exe never live here.");
            }
            else if (Array.IndexOf(ScriptHosts, exe) >= 0)
            {
                bool hidden = Regex.IsMatch(args, @"-e(nc|ncodedcommand)?\s|hidden|https?:|frombase64|downloadstring|\\temp\\", RegexOptions.IgnoreCase);
                Set(f, hidden ? 2 : 1, hidden ? "Suspicious" : "Check",
                    "Runs a command through " + exe + ".exe instead of starting a normal program" + (hidden ? ", and the command is hidden, encoded or downloads something." : ". Read the command on the line below to see what it launches."));
            }
            else if (Knowledge.Program(lookup, out importance, out what))
            {
                string advice = importance == "K" ? "Keep it on." : importance == "U" ? "Worth keeping." : "Optional: switch it off for a faster start.";
                Set(f, 0, importance == "K" ? "Keep" : importance == "U" ? "Useful" : "Optional", Sentence(what) + " " + advice);
            }
            else
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                string described = string.IsNullOrWhiteSpace(info.FileDescription) ? "No description is stored in the file" : info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.CompanyName)) described += ", from " + info.CompanyName.Trim();
                Set(f, 0, "OK", Sentence(described) + " Not in TaskBoost's list, so judge it by the publisher.");
            }

            // TaskBoost's own entry is this very program: it is not marked down for being unsigned
            bool self = string.Equals(path, App.ExePath, StringComparison.OrdinalIgnoreCase);
            if (exists && f.Level < 2 && publisher == null && !self)
            {
                bool temp = path.IndexOf("\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0;
                f.Level = temp ? 2 : Math.Max(f.Level, 1);
                f.Tag = temp ? "Suspicious" : f.Level == 1 && f.Tag != "Leftover" ? "Check" : f.Tag;
                f.Detail += temp ? " It is unsigned and runs from a temporary folder." : " It has no valid signature, so nobody vouches for who made it.";
            }
            return f;
        }

        static void Set(Finding f, int level, string tag, string detail) { f.Level = level; f.Tag = tag; f.Detail = detail; }

        static string Sentence(string s) { return char.ToUpper(s[0]) + s.Substring(1) + (s.EndsWith(".") ? "" : "."); }

        // The program part of a command line, with service-style prefixes and environment variables resolved
        public static string ExePath(string command, out string args)
        {
            string c = Environment.ExpandEnvironmentVariables(command.Trim());
            args = "";
            string path = c;
            int quote = c.StartsWith("\"") ? c.IndexOf('"', 1) : -1;
            int ext = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (quote > 0)
            {
                path = c.Substring(1, quote - 1);
                args = c.Substring(quote + 1).Trim();
            }
            else if (ext >= 0)
            {
                path = c.Substring(0, ext + 4);
                args = c.Substring(ext + 4).Trim();
            }
            if (path.StartsWith(@"\??\")) path = path.Substring(4);
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = WinDir + path.Substring(11);
            if (path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(WinDir, path);
            if (path.IndexOf('\\') < 0) path = FindByName(path);
            return path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint SearchPath(string path, string file, string extension, int size, StringBuilder buffer, out IntPtr filePart);

        // Where Windows finds a program that is given by name alone: its own folders, then the PATH. (PowerShell,
        // for one, is not in System32 itself; looked for only there, a "powershell -enc ..." entry would pass as
        // a harmless leftover of a program that is gone.)
        static string FindByName(string file)
        {
            var found = new StringBuilder(520);
            IntPtr part;
            uint length = SearchPath(null, file, ".exe", found.Capacity, found, out part);
            return length > 0 && length < found.Capacity ? found.ToString() : Path.Combine(Environment.SystemDirectory, file);
        }

        static string ShortcutTarget(string lnk, ref string args)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                args = Convert.ToString(sc.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, sc, null));
                string target = Convert.ToString(sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null));
                return string.IsNullOrEmpty(target) ? lnk : target;
            }
            catch (Exception) { return lnk; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace TaskBoost
{
    // Settings as one file, so the same choices can be carried to another PC
    static class Portable
    {
        public const string Extension = ".taskboost";
        // Only choices travel: undo records and logs stay on the machine they belong to
        static readonly string[] Files = { "settings.txt", "guard.txt", "turbo.txt", "care.txt", "games.txt", "known.txt", "laptop-mode.txt", "lang.txt" };

        public static int Export(string target)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# TaskBoost settings, exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " on " + Environment.MachineName);
            int count = 0;
            foreach (string name in Files)
            {
                string path = Path.Combine(App.DataDir, name);
                if (!File.Exists(path)) continue;
                sb.AppendLine("== " + name);
                sb.AppendLine(File.ReadAllText(path).TrimEnd());
                count++;
            }
            File.WriteAllText(target, sb.ToString(), Encoding.UTF8);
            return count;
        }

        public static int Import(string source)
        {
            string current = null;
            var content = new StringBuilder();
            int count = 0;
            foreach (string raw in File.ReadAllLines(source, Encoding.UTF8))
            {
                if (raw.StartsWith("== "))
                {
                    count += Flush(current, content);
                    current = raw.Substring(3).Trim();
                    content.Length = 0;
                }
                else if (current != null) content.AppendLine(raw);
            }
            count += Flush(current, content);
            return count;
        }

        static int Flush(string name, StringBuilder content)
        {
            if (name == null || Array.IndexOf(Files, name) < 0) return 0; // only the known files, never anything else
            File.WriteAllText(Path.Combine(App.DataDir, name), content.ToString().TrimEnd(), Encoding.UTF8);
            return 1;
        }
    }

    // Puts every setting back the way it was and removes every trace of TaskBoost except its own folder
    static class Uninstall
    {
        public static void Run(Action<string> progress)
        {
            progress("Leaving gaming, charging and net focus modes...");
            if (GameMode.IsOn) Helper.Run("game-off");
            if (ChargeMode.IsOn) Helper.Run("charge-off");
            if (NetFocus.IsOn) Helper.Run("net-off");
            try { LaptopMode.Set("normal"); } catch (Exception ex) { Log.Error(ex.Message); }

            progress("Restoring Windows settings...");
            var entries = State.Load();
            if (entries.Count > 0)
            {
                Tweaks.RestoreUser();
                if (entries.Exists(State.IsSystem)) Helper.Run("restore");
            }

            progress("Removing sign-in entries...");
            if (Startup.IsEnabled()) Helper.Run("startup-off");
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null) k.DeleteValue(Guard.OwnRunName, false);

            progress("Disconnecting from Claude...");
            string output;
            if (ClaudeLink.ClaudeCodeInstalled()) Util.Run("cmd.exe", "/c claude mcp remove taskboost --scope user", out output);
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Claude\claude_desktop_config.json");
                if (File.Exists(file) && File.ReadAllText(file).Contains("\"taskboost\""))
                {
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                    var servers = root != null && root.ContainsKey("mcpServers") ? root["mcpServers"] as Dictionary<string, object> : null;
                    if (servers != null && servers.Remove("taskboost")) File.WriteAllText(file, json.Serialize(root));
                }
            }
            catch (Exception ex) { Log.Error("Claude Desktop config: " + ex.Message); }

            progress("Removing the desktop shortcut...");
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), App.Name + ".lnk");
            if (File.Exists(shortcut)) File.Delete(shortcut);
            Log.Info("TaskBoost removed: settings restored, sign-in entries and Claude link gone");
        }

        // The data folder goes once this process has exited; the exe and its folder are left for the user to delete
        public static void DeleteDataAfterExit()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + App.DataDir + "\"");
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }
    }
}

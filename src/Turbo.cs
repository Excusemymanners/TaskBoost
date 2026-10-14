using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TaskBoost
{
    // Temporary "everything for the game" state. Every change is recorded and put back by Off().
    static class GameMode
    {
        const string StateFile = "game-state.txt";
        const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        // Stopped while gaming, started again afterwards: updates and downloads, indexing, preloading, telemetry
        static readonly string[] PausedServices = { "wuauserv", "BITS", "DoSvc", "WSearch", "SysMain", "DiagTrack" };
        // Safe to close: they hold no unsaved work and come back at the next sign-in
        static readonly string[] BackgroundApps = { "OneDrive", "Dropbox", "GoogleDriveFS", "Teams", "ms-teams", "Skype", "PhoneExperienceHost", "YourPhone", "Creative Cloud", "CCXProcess", "jusched", "GoogleUpdate", "MicrosoftEdgeUpdate" };
        static readonly string[] BrowsersAndChat = { "chrome", "msedge", "firefox", "opera", "brave", "Discord", "slack", "Telegram", "WhatsApp", "Viber", "Spotify" };

        public static bool IsOn
        {
            get
            {
                var f = new FileInfo(Path.Combine(App.DataDir, StateFile));
                return f.Exists && f.Length > 0;
            }
        }

        public static void On(ICollection<string> options)
        {
            State.FileName = StateFile;
            State.Record("mode", "on", DateTime.Now.ToString("s"));

            string output;
            Util.Run("powercfg.exe", "/getactivescheme", out output);
            var scheme = Regex.Match(output, "[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
            if (scheme.Success) State.Record("scheme", "previous", scheme.Value);
            if (Util.Run("powercfg.exe", "/setactive " + HighPerformance, out output) == 0) Log.Info("power plan: High performance");
            else Log.Info("power plan: this PC has no High performance plan, left as it is");

            // Windows' own Game Mode on, background game recording off
            Reg.Set("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
            Reg.Set("HKCU", @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
            Reg.Set("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0);
            Reg.Set("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
            Log.Info("Windows Game Mode on, background recording off");

            if (App.IsElevated)
            {
                // Do not hold back network packets while media is playing
                Reg.Set("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1);
                if (options.Contains("services"))
                {
                    var paused = new List<string>();
                    foreach (string s in PausedServices)
                    {
                        if (Util.Run("sc.exe", "query \"" + s + "\"", out output) != 0 || !Regex.IsMatch(output, @":\s*4\s")) continue; // not running
                        if (Util.Run("sc.exe", "stop \"" + s + "\"", out output) != 0) continue;
                        State.Record("gsvc", s, "running");
                        paused.Add(s);
                    }
                    Log.Info("paused services: " + (paused.Count > 0 ? string.Join(", ", paused) : "none were running"));
                }
            }
            else Log.Info("not administrator: network setting and service pause skipped");

            if (options.Contains("apps")) Close(BackgroundApps, "background apps");
            if (options.Contains("browsers")) Close(BrowsersAndChat, "browsers and chat apps");
            Util.Run("ipconfig.exe", "/flushdns", out output);
            Memory.Free();
            Log.Info("Gaming mode is ON");
        }

        public static void Off()
        {
            State.FileName = StateFile;
            foreach (var e in State.Load())
            {
                if (e.Kind == "reg") Reg.Restore(e);
                else if (e.Kind == "gsvc") Util.StartService(e.Key);
                else if (e.Kind == "scheme") Util.RunLogged("powercfg.exe", "/setactive " + e.Value);
            }
            State.Clear();
            Log.Info("Gaming mode is OFF: power plan, services and settings are back as they were");
        }

        static void Close(string[] names, string label)
        {
            var closed = new List<string>();
            foreach (string name in names)
                foreach (var p in Process.GetProcessesByName(name))
                    using (p)
                    {
                        try
                        {
                            p.Kill();
                            if (!closed.Contains(name)) closed.Add(name);
                        }
                        catch (Exception) { }
                    }
            Log.Info("closed " + label + ": " + (closed.Count > 0 ? string.Join(", ", closed) : "none were running"));
        }
    }

    // What is left of the old "charge my phone" mode, which kept the PC awake so a phone could charge from it.
    // It was taken out because no program can cut a USB port's power, so it could not stop the charging either.
    // Off() stays, to put the settings back for anyone who still had the mode on.
    static class ChargeMode
    {
        const string StateFile = "charge-state.txt";

        public static bool IsOn
        {
            get
            {
                var f = new FileInfo(Path.Combine(App.DataDir, StateFile));
                return f.Exists && f.Length > 0;
            }
        }

        static int PowerShell(string script, out string output)
        {
            return Util.Run("powershell.exe", "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)), out output);
        }

        public static void Off()
        {
            State.FileName = StateFile;
            var entries = State.Load();
            var schemeEntry = entries.Find(delegate(State.Entry e) { return e.Kind == "scheme"; });
            string scheme = schemeEntry == null ? "SCHEME_CURRENT" : schemeEntry.Value, output;
            var hubs = new List<string>();
            foreach (var e in entries)
            {
                if (e.Kind == "hub") hubs.Add("'" + e.Key.Replace("'", "'") + "'");
                if (e.Kind != "pwr") continue;
                string[] p = e.Key.Split('|');
                Util.RunLogged("powercfg.exe", "/set" + p[0] + "valueindex " + scheme + " " + p[1] + " " + p[2] + " " + e.Value);
            }
            // Re-apply the plan that is active now, so restored values take effect without switching plans
            Util.Run("powercfg.exe", "/getactivescheme", out output);
            string active = Regex.Match(output, "[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", RegexOptions.IgnoreCase).Value;
            if (active.Length > 0) Util.RunLogged("powercfg.exe", "/setactive " + active);
            if (hubs.Count > 0)
                PowerShell("$names = @(" + string.Join(",", hubs) + "); Get-CimInstance -Namespace root\\wmi -ClassName MSPower_DeviceEnable | Where-Object { $names -contains $_.InstanceName } | ForEach-Object { Set-CimInstance -InputObject $_ -Property @{ Enable = $true } }", out output);
            State.Clear();
            Log.Info("Phone charging mode is off: sleep, lid and USB power settings are back as they were");
        }
    }

    // Four one-click laptop modes, offered by the buddy. Turbo is gaming mode; leaving it puts its changes back.
    static class LaptopMode
    {
        const string ModeFile = "laptop-mode.txt", ToastFile = "notifications-before.txt";
        const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        public static readonly string[] Modes = { "low", "normal", "best", "turbo", "work" };

        public static string Current
        {
            get
            {
                string path = Path.Combine(App.DataDir, ModeFile);
                try
                {
                    string m = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
                    return Array.IndexOf(Modes, m) >= 0 ? m : "normal";
                }
                catch (IOException) { return "normal"; }
            }
        }

        // What is really in effect: gaming mode switched on from the Turbo tab also counts as turbo
        public static string Effective
        {
            get
            {
                if (GameMode.IsOn) return "turbo";
                string c = Current;
                return c == "turbo" ? "normal" : c;
            }
        }

        // Runs on a worker thread. Only entering or leaving turbo asks for administrator approval.
        public static void Set(string mode)
        {
            if (mode != "turbo" && GameMode.IsOn) Helper.Run("game-off");
            Notifications(mode != "work");
            WorkCpu(mode == "work");
            string output;
            if (mode == "turbo")
            {
                if (!GameMode.IsOn) Helper.Run("game-on --options apps,services");
                Util.Run("powercfg.exe", "/overlaysetactive OVERLAY_SCHEME_MAX", out output);
            }
            else if (mode == "low") Plan("SCHEME_MAX", "a1841308-3bce-4f6c-8f5f-e9a6c5e5a8b9", "Power saver", "OVERLAY_SCHEME_MIN");
            else if (mode == "best") Plan("SCHEME_MIN", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High performance", "OVERLAY_SCHEME_MAX");
            // Work: the Balanced plan pushed towards performance, so apps open quickly without the cost of full power
            else if (mode == "work") Plan("SCHEME_BALANCED", null, "Balanced, tuned for quick response", "OVERLAY_SCHEME_HIGH");
            else Plan("SCHEME_BALANCED", null, "Balanced", "OVERLAY_SCHEME_NONE");
            File.WriteAllText(Path.Combine(App.DataDir, ModeFile), mode);
            Log.Info("laptop mode: " + mode);
        }

        // Switches the Windows power plan and the "power mode" slider. This is plain Windows, so it works on any
        // laptop or desktop, unlike a maker's own key such as Fn+Q. Laptops that only list Balanced usually still
        // have the other plans hidden; if not, a copy is made from the built-in template.
        static void Plan(string alias, string templateGuid, string name, string overlay)
        {
            string output;
            bool ok = Util.Run("powercfg.exe", "/setactive " + alias, out output) == 0;
            if (!ok && templateGuid != null && Util.Run("powercfg.exe", "/duplicatescheme " + templateGuid, out output) == 0)
            {
                string copy = Regex.Match(output, "[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", RegexOptions.IgnoreCase).Value;
                ok = copy.Length > 0 && Util.Run("powercfg.exe", "/setactive " + copy, out output) == 0;
            }
            if (!ok)
            {
                Util.Run("powercfg.exe", "/setactive SCHEME_BALANCED", out output);
                Log.Info("this PC has no " + name + " plan; using Balanced with the power mode slider instead");
            }
            else Log.Info("power plan: " + name);
            Util.Run("powercfg.exe", "/overlaysetactive " + overlay, out output); // not available on older Windows 10: ignored
        }

        // Work mode lifts any processor cap on battery in the Balanced plan (the Boost tab's battery setting caps it
        // at 99%, which switches turbo boost off and makes programs slow to open). Leaving work puts the cap back.
        static void WorkCpu(bool on)
        {
            string saved = Path.Combine(App.DataDir, "work-cpu-before.txt"), output;
            if (on)
            {
                if (!File.Exists(saved) && Util.Run("powercfg.exe", "/qh SCHEME_BALANCED SUB_PROCESSOR PROCTHROTTLEMAX", out output) == 0)
                {
                    var m = Regex.Matches(output, @":\s*0x([0-9a-fA-F]{8})\s*$", RegexOptions.Multiline);
                    if (m.Count >= 2) File.WriteAllText(saved, Convert.ToUInt32(m[m.Count - 1].Groups[1].Value, 16).ToString());
                }
                Util.Run("powercfg.exe", "/setdcvalueindex SCHEME_BALANCED SUB_PROCESSOR PROCTHROTTLEMAX 100", out output);
                Log.Info("processor: full speed on battery too");
            }
            else if (File.Exists(saved))
            {
                Util.Run("powercfg.exe", "/setdcvalueindex SCHEME_BALANCED SUB_PROCESSOR PROCTHROTTLEMAX " + File.ReadAllText(saved).Trim(), out output);
                File.Delete(saved);
                Log.Info("processor: battery limit back as it was");
            }
        }

        // Work mode silences app notifications; any other mode puts back what was there before
        static void Notifications(bool on)
        {
            string saved = Path.Combine(App.DataDir, ToastFile);
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PushKey))
            {
                if (!on)
                {
                    if (!File.Exists(saved))
                    {
                        object before = k.GetValue("ToastEnabled");
                        File.WriteAllText(saved, before == null ? "absent" : before.ToString());
                    }
                    k.SetValue("ToastEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    Log.Info("app notifications: off");
                }
                else if (File.Exists(saved))
                {
                    string before = File.ReadAllText(saved).Trim();
                    if (before == "absent") k.DeleteValue("ToastEnabled", false);
                    else k.SetValue("ToastEnabled", int.Parse(before), Microsoft.Win32.RegistryValueKind.DWord);
                    File.Delete(saved);
                    Log.Info("app notifications: back as they were");
                }
            }
        }
    }

    // "Net focus": the program you are working in goes first on the network. Its traffic is marked as priority,
    // the other programs that are online have their upload rate held down, and Windows' background downloaders are
    // paused. Windows can only shape what a PC sends, not what it receives, so pausing the downloaders is what
    // frees download speed. The rules live in the active store only and disappear at the next restart.
    static class NetFocus
    {
        const string StateFile = "net-state.txt";
        static readonly string[] Downloaders = { "BITS", "DoSvc", "wuauserv" };

        public static bool IsOn
        {
            get
            {
                var f = new FileInfo(Path.Combine(App.DataDir, StateFile));
                return f.Exists && f.Length > 0;
            }
        }

        static string Safe(string name) { return Regex.Replace(name ?? "", @"[^\w\.\- ]", ""); }

        public static void On(string exe, string[] others)
        {
            if (!App.IsElevated)
            {
                Log.Info("Net focus needs administrator approval to set network rules; nothing was changed");
                return;
            }
            State.FileName = StateFile;
            State.Record("mode", "on", DateTime.Now.ToString("s"));
            var script = new StringBuilder();
            script.AppendLine("Remove-NetQosPolicy -Name 'TaskBoost*' -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue");
            script.AppendLine("New-NetQosPolicy -Name 'TaskBoost-focus' -AppPathNameMatchCondition '" + Safe(exe) + "' -DSCPAction 46 -PolicyStore ActiveStore | Out-Null");
            int n = 0;
            foreach (string other in others)
            {
                if (Safe(other).Length == 0 || string.Equals(other, exe, StringComparison.OrdinalIgnoreCase)) continue;
                script.AppendLine("New-NetQosPolicy -Name 'TaskBoost-slow-" + (++n) + "' -AppPathNameMatchCondition '" + Safe(other) + "' -ThrottleRateActionBitsPerSecond 500KB -PolicyStore ActiveStore | Out-Null");
            }
            script.AppendLine("'rules in place: ' + @(Get-NetQosPolicy -PolicyStore ActiveStore -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'TaskBoost*' }).Count");
            string output;
            Util.PowerShell(script.ToString(), out output);
            Log.Info("net focus on " + exe + ": " + n + " other program(s) held back; " + output.Trim());

            var paused = new List<string>();
            foreach (string s in Downloaders)
            {
                if (Util.Run("sc.exe", "query \"" + s + "\"", out output) != 0 || !Regex.IsMatch(output, @":\s*4\s")) continue;
                if (Util.Run("sc.exe", "stop \"" + s + "\"", out output) != 0) continue;
                State.Record("gsvc", s, "running");
                paused.Add(s);
            }
            Log.Info("background downloaders paused: " + (paused.Count > 0 ? string.Join(", ", paused) : "none were running"));
        }

        public static void Off()
        {
            State.FileName = StateFile;
            string output;
            Util.PowerShell("Remove-NetQosPolicy -Name 'TaskBoost*' -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue", out output);
            System.Threading.Thread.Sleep(2000); // a service that is still stopping cannot be started yet
            foreach (var e in State.Load())
                if (e.Kind == "gsvc") Util.Run("sc.exe", "start \"" + e.Key + "\"", out output);
            State.Clear();
            Log.Info("net focus off: network rules removed, background downloaders running again");
        }

        // Net focus that was still on when TaskBoost last stopped: nothing keeps it any more. After a restart of
        // Windows its rules are gone already and only the record is left; otherwise it is switched off properly.
        public static void EndLeftover()
        {
            if (!IsOn) return;
            string path = Path.Combine(App.DataDir, StateFile);
            DateTime since = DateTime.MinValue;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] p = line.Split('\t');
                if (p.Length == 3 && p[0] == "mode") DateTime.TryParse(p[2], out since);
            }
            if (since < DateTime.Now - Care.Uptime)
            {
                File.Delete(path);
                Log.Info("net focus: it was on before Windows restarted; its rules ended with the restart");
            }
            else Helper.Run("net-off");
        }
    }

    static class Memory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attributes; }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returned);
        [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        public static long TotalMb()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            return (long)(m.ullTotalPhys / (1024 * 1024));
        }

        public static long AvailableMb()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            return (long)(m.ullAvailPhys / (1024 * 1024));
        }

        // Asks every program to hand back memory it is not actively using, then (as administrator) empties
        // Windows' file cache. Nothing is closed and no data is lost; programs simply reload what they need.
        public static void Free()
        {
            long before = AvailableMb();
            int trimmed = 0;
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    IntPtr h = OpenProcess(0x1100, false, p.Id); // query limited information + set quota
                    if (h == IntPtr.Zero) continue;
                    if (EmptyWorkingSet(h)) trimmed++;
                    CloseHandle(h);
                }

            bool purged = false;
            IntPtr token;
            long luid;
            if (App.IsElevated && OpenProcessToken(GetCurrentProcess(), 0x28, out token) && LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out luid))
            {
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                int purgeStandbyList = 4;
                purged = NtSetSystemInformation(80, ref purgeStandbyList, 4) == 0; // SystemMemoryListInformation
                CloseHandle(token);
            }
            Log.Info("memory: " + before + " MB free before, " + AvailableMb() + " MB after (" + trimmed + " programs trimmed" + (purged ? ", file cache emptied" : "") + ")");
        }
    }

    // Deletes only inside a fixed list of temporary folders, and only files older than a week (the same rule Windows Disk Cleanup uses)
    static class Cleaner
    {
        static readonly TimeSpan MinAge = TimeSpan.FromDays(7);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size, i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);

        public static void Run(ICollection<string> items, bool dryRun)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            long total = 0;
            string verb = dryRun ? "would free " : "freed ";

            if (items.Contains("temp")) total += Folder("Temporary files", Path.GetTempPath(), dryRun);
            if (items.Contains("wintemp")) total += Folder("Windows temp files", Path.Combine(windows, "Temp"), dryRun);
            if (items.Contains("reports"))
            {
                total += Folder("Crash dumps", Path.Combine(local, "CrashDumps"), dryRun);
                foreach (string root in new[] { local, common })
                    foreach (string leaf in new[] { "ReportQueue", "ReportArchive" })
                        total += Folder("Error reports", Path.Combine(root, @"Microsoft\Windows\WER\" + leaf), dryRun);
            }
            if (items.Contains("recycle"))
            {
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                SHQueryRecycleBin(null, ref info);
                if (!dryRun && info.i64NumItems > 0) SHEmptyRecycleBin(IntPtr.Zero, null, 7); // no confirmation, no progress window, no sound
                Log.Info("Recycle Bin: " + verb + Mb(info.i64Size) + " (" + info.i64NumItems + " items)");
                total += info.i64Size;
            }
            if (items.Contains("dns"))
            {
                string output;
                if (dryRun) Log.Info("Network buffers: would flush the DNS cache");
                else Log.Info("Network buffers: DNS cache " + (Util.Run("ipconfig.exe", "/flushdns", out output) == 0 ? "flushed" : "could not be flushed"));
            }
            if (items.Contains("clipboard"))
            {
                if (dryRun) Log.Info("Clipboard: would be emptied");
                else
                {
                    try { Clipboard.Clear(); Log.Info("Clipboard: emptied"); }
                    catch (Exception ex) { Log.Info("Clipboard: could not be emptied (" + ex.Message + ")"); }
                }
            }
            Log.Info((dryRun ? "Preview: this clean-up would free about " : "Clean-up finished: freed ") + Mb(total) + ". Files in use and files newer than a week were left alone.");
        }

        static string Mb(long bytes) { return (bytes / (1024.0 * 1024.0)).ToString("0.#") + " MB"; }

        static long Folder(string label, string dir, bool dryRun)
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\');
            if (!Directory.Exists(full)) return 0;
            if (!IsSafe(full))
            {
                Log.Info(label + ": skipped, " + full + " is not a folder TaskBoost is allowed to clean");
                return 0;
            }
            int files = 0;
            long bytes = Sweep(new DirectoryInfo(full), dryRun, ref files);
            Log.Info(label + ": " + (dryRun ? "would free " : "freed ") + Mb(bytes) + " (" + files + " files) in " + full);
            return bytes;
        }

        // Second line of defence: even a wrong TEMP variable can never point the cleaner at a real folder
        static bool IsSafe(string full)
        {
            string leaf = Path.GetFileName(full);
            if (Array.IndexOf(new[] { "temp", "crashdumps", "reportqueue", "reportarchive" }, leaf.ToLowerInvariant()) < 0) return false;
            if (full.Split('\\').Length < 3) return false; // never a folder directly under a drive root
            foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                                            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyDocuments,
                                            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData })
                if (string.Equals(full, Environment.GetFolderPath(special).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static long Sweep(DirectoryInfo dir, bool dryRun, ref int files)
        {
            long bytes = 0;
            DateTime cutoff = DateTime.UtcNow - MinAge;
            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos(); }
            catch (Exception) { return 0; }
            foreach (var entry in entries)
            {
                // Links and junctions lead out of the temp folder: never follow or delete through them
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var sub = entry as DirectoryInfo;
                if (sub != null)
                {
                    bytes += Sweep(sub, dryRun, ref files);
                    if (!dryRun && sub.LastWriteTimeUtc < cutoff)
                    {
                        try { sub.Delete(false); } // only succeeds when the folder is now empty
                        catch (Exception) { }
                    }
                    continue;
                }
                var file = (FileInfo)entry;
                if (file.LastWriteTimeUtc > cutoff || file.CreationTimeUtc > cutoff) continue;
                try
                {
                    long length = file.Length;
                    if (!dryRun) file.Delete(); // fails harmlessly for files in use
                    bytes += length;
                    files++;
                }
                catch (Exception) { }
            }
            return bytes;
        }
    }
}

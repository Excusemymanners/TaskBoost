using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TaskBoost
{
    class Group
    {
        public readonly string Key, Title, Description;
        public readonly bool NeedsAdmin, DefaultOn;
        public Group(string key, string title, string description, bool needsAdmin, bool defaultOn = true) { Key = key; Title = title; Description = description; NeedsAdmin = needsAdmin; DefaultOn = defaultOn; }
    }

    static class Tweaks
    {
        // Key also names the group's illustration in Icons.Draw
        public static readonly Group[] Groups =
        {
            new Group("theme", "Dark mode", "Solid black desktop, no transparency", false),
            new Group("animations", "No animations", "Window, menu and taskbar effects off", false),
            new Group("background", "Quiet background", "No background apps, tips or startup delay", false),
            new Group("taskbar", "Thin taskbar", "Small icons, window titles, no search box", false),
            new Group("power", "Battery first", "Short timeouts, CPU capped at 99% on battery", false),
            new Group("autostart", "No browser autostart", "Chrome and Edge stay closed at sign-in", false),
            new Group("services", "Trim services", "Telemetry, Xbox, maps and fax off", true),
            new Group("textboot", "Text boot", "Driver list at boot, detailed sign-in status", true, false),
            new Group("startmenu", "Bare Start menu", "No tiles; the layout is locked", true, false)
        };

        static readonly Dictionary<string, Action> Actions = new Dictionary<string, Action>
        {
            { "theme", Theme }, { "animations", Animations }, { "background", Background }, { "taskbar", Taskbar },
            { "power", Power }, { "autostart", AutoStart }, { "services", Services }, { "textboot", TextBoot },
            { "startmenu", StartMenu }
        };

        const string HKCU = "HKCU", HKLM = "HKLM";
        const string CurrentVersion = @"Software\Microsoft\Windows\CurrentVersion";
        const string Advanced = CurrentVersion + @"\Explorer\Advanced";
        const string DesktopKey = @"Control Panel\Desktop";
        const string ColorsKey = @"Control Panel\Colors";
        const uint SpiSave = 3; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

        // Runs the selected groups that match the given privilege level
        public static void Apply(ICollection<string> keys, bool system)
        {
            foreach (var g in Groups)
            {
                if (g.NeedsAdmin != system || !keys.Contains(g.Key)) continue;
                int before = Log.Errors;
                try { Actions[g.Key](); }
                catch (Exception ex) { Log.Error(ex.Message); }
                Log.Info((Log.Errors == before ? "done: " : "finished with errors: ") + g.Title);
            }
            if (!system) BroadcastSettingChange();
        }

        // ---- per-user groups ----

        static void Theme()
        {
            string p = CurrentVersion + @"\Themes\Personalize";
            Reg.Set(HKCU, p, "AppsUseLightTheme", 0);
            Reg.Set(HKCU, p, "SystemUsesLightTheme", 0);
            Reg.Set(HKCU, p, "EnableTransparency", 0);
            Reg.Set(HKCU, ColorsKey, "Background", "0 0 0");
            Reg.Record(HKCU, DesktopKey, "Wallpaper");
            Native.SystemParametersInfo(20, 0, "", SpiSave);
            Native.SetSysColors(1, new[] { 1 }, new[] { 0 });
        }

        // Client-area, menu, combo box, smooth-scroll, tooltip, selection-fade, menu-fade, cursor-shadow, drop-shadow, tooltip-fade
        static readonly uint[] SpiOff = { 0x1043, 0x1003, 0x1005, 0x1007, 0x1017, 0x1015, 0x1013, 0x101B, 0x1025, 0x1019 };

        static void Animations()
        {
            // Custom visual effects; font smoothing is left on so text stays readable
            Reg.Set(HKCU, CurrentVersion + @"\Explorer\VisualEffects", "VisualFXSetting", 3);
            Reg.Set(HKCU, Advanced, "TaskbarAnimations", 0);
            Reg.Set(HKCU, Advanced, "ListviewAlphaSelect", 0);
            Reg.Set(HKCU, Advanced, "ListviewShadow", 0);
            Reg.Set(HKCU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0);
            Reg.Set(HKCU, DesktopKey, "MenuShowDelay", "0");
            Reg.Set(HKCU, DesktopKey + @"\WindowMetrics", "MinAnimate", "0");

            foreach (uint set in SpiOff)
            {
                int current = 0;
                if (Native.SystemParametersInfo(set - 1, 0, ref current, 0)) State.Record("spi", set.ToString("X"), current != 0 ? "1" : "0");
                Native.SystemParametersInfo(set, 0, IntPtr.Zero, SpiSave);
            }
            var ai = new Native.ANIMATIONINFO();
            ai.cbSize = 8;
            if (Native.SystemParametersInfo(0x48, 8, ref ai, 0)) State.Record("spi", "49", ai.iMinAnimate.ToString());
            ai.iMinAnimate = 0;
            Native.SystemParametersInfo(0x49, 8, ref ai, SpiSave);
            int drag = 0;
            if (Native.SystemParametersInfo(0x26, 0, ref drag, 0)) State.Record("spi", "25", drag != 0 ? "1" : "0");
            Native.SystemParametersInfo(0x25, 0, IntPtr.Zero, SpiSave);
        }

        static void Background()
        {
            Reg.Set(HKCU, CurrentVersion + @"\BackgroundAccessApplications", "GlobalUserDisabled", 1);
            Reg.Set(HKCU, CurrentVersion + @"\Search", "BackgroundAppGlobalToggle", 0);
            string[] suggestions =
            {
                "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "SilentInstalledAppsEnabled",
                "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-310093Enabled",
                "SubscribedContent-353698Enabled", "RotatingLockScreenOverlayEnabled"
            };
            foreach (string name in suggestions) Reg.Set(HKCU, CurrentVersion + @"\ContentDeliveryManager", name, 0);
            // No artificial delay before startup programs load
            Reg.Set(HKCU, CurrentVersion + @"\Explorer\Serialize", "StartupDelayInMSec", 0);
        }

        static void Taskbar()
        {
            Reg.Set(HKCU, Advanced, "TaskbarSmallIcons", 1);
            Reg.Set(HKCU, Advanced, "TaskbarGlomLevel", 2);
            Reg.Set(HKCU, Advanced, "ShowTaskViewButton", 0);
            Reg.Set(HKCU, Advanced, "ShowCortanaButton", 0);
            Reg.Set(HKCU, Advanced + @"\People", "PeopleBand", 0);
            Reg.Set(HKCU, CurrentVersion + @"\Search", "SearchboxTaskbarMode", 0);
            Reg.Set(HKCU, Advanced, "Start_TrackProgs", 0);
            Reg.Set(HKCU, Advanced, "Start_TrackDocs", 0);
            if (IsWindows11)
            {
                // Windows 11 ignores the small-icon and label settings; give it the same plain look its own way
                Reg.Set(HKCU, Advanced, "TaskbarAl", 0);  // Start button on the left
                Reg.Set(HKCU, Advanced, "TaskbarMn", 0);  // no Chat button
            }
        }

        static bool IsWindows11 { get { return Environment.OSVersion.Version.Build >= 22000; } }

        // ac/dc, subgroup, setting, value
        static readonly string[][] PowerValues =
        {
            new[] { "dc", "SUB_VIDEO", "VIDEOIDLE", "180" },
            new[] { "ac", "SUB_VIDEO", "VIDEOIDLE", "600" },
            new[] { "dc", "SUB_SLEEP", "HIBERNATEIDLE", "900" },
            new[] { "dc", "SUB_DISK", "DISKIDLE", "300" },
            new[] { "dc", "SUB_PROCESSOR", "PROCTHROTTLEMAX", "99" },
            new[] { "dc", "SUB_PROCESSOR", "PROCTHROTTLEMIN", "5" },
            new[] { "ac", "SUB_PROCESSOR", "PROCTHROTTLEMAX", "100" },
            new[] { "dc", "SUB_BUTTONS", "LIDACTION", "2" },
            new[] { "dc", "SUB_ENERGYSAVER", "ESBATTTHRESHOLD", "100" }
        };

        static void Power()
        {
            foreach (string[] v in PowerValues)
            {
                string output;
                if (Util.Run("powercfg.exe", "/qh SCHEME_CURRENT " + v[1] + " " + v[2], out output) == 0)
                {
                    // The last two hex values are the current AC and DC indexes
                    var m = Regex.Matches(output, @":\s*0x([0-9a-fA-F]{8})\s*$", RegexOptions.Multiline);
                    if (m.Count >= 2)
                    {
                        string hex = m[m.Count - (v[0] == "ac" ? 2 : 1)].Groups[1].Value;
                        State.Record("pwr", v[0] + "|" + v[1] + "|" + v[2], Convert.ToUInt32(hex, 16).ToString());
                    }
                }
                Util.RunLogged("powercfg.exe", "/set" + v[0] + "valueindex SCHEME_CURRENT " + v[1] + " " + v[2] + " " + v[3]);
            }
            Util.RunLogged("powercfg.exe", "/setactive SCHEME_CURRENT");
        }

        static void AutoStart()
        {
            string run = CurrentVersion + @"\Run";
            string[] names;
            using (var k = Registry.CurrentUser.OpenSubKey(run))
            {
                if (k == null) return;
                names = k.GetValueNames();
            }
            foreach (string n in names)
            {
                if (!Regex.IsMatch(n, "^(MicrosoftEdgeAutoLaunch|GoogleChromeAutoLaunch)_")) continue;
                Reg.Delete(HKCU, run, n);
                Log.Info("removed sign-in entry " + n);
            }
        }

        // ---- administrator groups ----

        static readonly string[] DisabledServices =
        {
            "DiagTrack", "dmwappushservice", "MapsBroker", "RetailDemo", "Fax", "WMPNetworkSvc",
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc", "dptftcs"
        };
        // Only needed occasionally: start on demand
        static readonly string[] ManualServices = { "SQLWriter", "TbtP2pShortcutService" };

        static readonly string[] TelemetryTasks =
        {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
            @"\Microsoft\Windows\Feedback\Siuf\DmClient",
            @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
            @"\Microsoft\Windows\Maps\MapsUpdateTask",
            @"\Microsoft\Windows\Maps\MapsToastTask"
        };

        static void Services()
        {
            foreach (string s in DisabledServices) SetService(s, "disabled");
            foreach (string s in ManualServices) SetService(s, "demand");
            foreach (string t in TelemetryTasks) DisableTask(t);
            // Edge: no preloading at sign-in, no running after its windows are closed
            Reg.Set(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
            Reg.Set(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
        }

        static void SetService(string name, string mode)
        {
            object start = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + name, "Start", null);
            if (start == null) return; // not installed on this machine
            State.Record("svc", name, start.ToString());
            if (!Util.RunLogged("sc.exe", "config \"" + name + "\" start= " + mode)) return;
            string ignored;
            Util.Run("sc.exe", "stop \"" + name + "\"", out ignored);
        }

        static void DisableTask(string path)
        {
            string xml;
            if (Util.Run("schtasks.exe", "/query /tn \"" + path + "\" /xml", out xml) != 0) return; // not present
            var m = Regex.Match(xml, "<Settings>.*?<Enabled>(true|false)</Enabled>", RegexOptions.Singleline);
            bool enabled = !m.Success || m.Groups[1].Value == "true";
            State.Record("task", path, enabled ? "1" : "0");
            if (enabled) Util.RunLogged("schtasks.exe", "/change /tn \"" + path + "\" /disable");
        }

        static void TextBoot()
        {
            string output;
            if (Util.Run("bcdedit.exe", "/enum {current}", out output) != 0)
            {
                Log.Error("bcdedit: " + Regex.Replace(output, @"\s+", " "));
                return;
            }
            foreach (string name in new[] { "sos", "quietboot" })
            {
                var m = Regex.Match(output, "^" + name + @"\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                State.Record("bcd", name, m.Success ? m.Groups[1].Value : "absent");
            }
            // List drivers as they load, and show detailed status lines at sign-in/shutdown
            Util.RunLogged("bcdedit.exe", "/set {current} sos on");
            Util.RunLogged("bcdedit.exe", "/set {current} quietboot off");
            Reg.Set(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1);
        }

        const string EmptyStartLayout =
            "<LayoutModificationTemplate xmlns=\"http://schemas.microsoft.com/Start/2014/LayoutModification\" xmlns:defaultlayout=\"http://schemas.microsoft.com/Start/2014/FullDefaultLayout\" xmlns:start=\"http://schemas.microsoft.com/Start/2014/StartLayout\" Version=\"1\">\r\n" +
            "  <LayoutOptions StartTileGroupCellWidth=\"6\" />\r\n" +
            "  <DefaultLayoutOverride>\r\n" +
            "    <StartLayoutCollection>\r\n" +
            "      <defaultlayout:StartLayout GroupCellWidth=\"6\" />\r\n" +
            "    </StartLayoutCollection>\r\n" +
            "  </DefaultLayoutOverride>\r\n" +
            "</LayoutModificationTemplate>\r\n";

        static void StartMenu()
        {
            string pol11 = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
            if (IsWindows11)
            {
                // Windows 11 has no tiles to remove and does not accept this layout file
                Reg.Set(HKLM, pol11, "HideRecentlyAddedApps", 1);
                Log.Info("Windows 11: Start has no tiles, so only the \"recently added\" list was hidden");
                return;
            }
            // No tiles (narrow single column), no "recently added" list
            string dir =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), App.Name);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "empty-start.xml");
            File.WriteAllText(file, EmptyStartLayout, Encoding.UTF8);
            string pol = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
            Reg.Set(HKLM, pol, "LockedStartLayout", 1);
            Reg.Set(HKLM, pol, "StartLayoutFile", file, RegistryValueKind.ExpandString);
            Reg.Set(HKLM, pol, "HideRecentlyAddedApps", 1);
        }

        // ---- restore ----

        public static void RestoreUser()
        {
            bool power = false, wallpaper = false, colour = false;
            foreach (var e in State.Load())
            {
                if (State.IsSystem(e)) continue;
                if (e.Kind == "reg")
                {
                    Reg.Restore(e);
                    if (e.Key == HKCU + "\\" + DesktopKey + "|Wallpaper") wallpaper = true;
                    if (e.Key == HKCU + "\\" + ColorsKey + "|Background") colour = true;
                }
                else if (e.Kind == "spi") RestoreSpi(e);
                else if (e.Kind == "pwr")
                {
                    string[] p = e.Key.Split('|');
                    Util.RunLogged("powercfg.exe", "/set" + p[0] + "valueindex SCHEME_CURRENT " + p[1] + " " + p[2] + " " + e.Value);
                    power = true;
                }
            }
            if (power) Util.RunLogged("powercfg.exe", "/setactive SCHEME_CURRENT");
            if (wallpaper)
            {
                string w = Registry.GetValue(@"HKEY_CURRENT_USER\" + DesktopKey, "Wallpaper", "") as string;
                Native.SystemParametersInfo(20, 0, w ?? "", SpiSave);
            }
            if (colour)
            {
                string[] rgb = ((Registry.GetValue(@"HKEY_CURRENT_USER\" + ColorsKey, "Background", "") as string) ?? "").Split(' ');
                int r, g, b;
                if (rgb.Length == 3 && int.TryParse(rgb[0], out r) && int.TryParse(rgb[1], out g) && int.TryParse(rgb[2], out b))
                    Native.SetSysColors(1, new[] { 1 }, new[] { r | (g << 8) | (b << 16) });
            }
            BroadcastSettingChange();
            State.Remove(delegate(State.Entry e) { return !State.IsSystem(e); });
            Log.Info("done: restored per-user settings");
        }

        static void RestoreSpi(State.Entry e)
        {
            int value = int.Parse(e.Value);
            if (e.Key == "49")
            {
                var ai = new Native.ANIMATIONINFO();
                ai.cbSize = 8;
                ai.iMinAnimate = value;
                Native.SystemParametersInfo(0x49, 8, ref ai, SpiSave);
            }
            else if (e.Key == "25") Native.SystemParametersInfo(0x25, (uint)value, IntPtr.Zero, SpiSave);
            else Native.SystemParametersInfo(Convert.ToUInt32(e.Key, 16), 0, (IntPtr)value, SpiSave);
        }

        public static void RestoreSystem()
        {
            foreach (var e in State.Load())
            {
                if (!State.IsSystem(e)) continue;
                if (e.Kind == "reg") Reg.Restore(e);
                else if (e.Kind == "svc")
                {
                    string mode = e.Value == "2" ? "auto" : e.Value == "4" ? "disabled" : "demand";
                    Util.RunLogged("sc.exe", "config \"" + e.Key + "\" start= " + mode);
                }
                else if (e.Kind == "task")
                {
                    if (e.Value == "1") Util.RunLogged("schtasks.exe", "/change /tn \"" + e.Key + "\" /enable");
                }
                else if (e.Kind == "bcd")
                {
                    if (e.Value == "absent") Util.RunLogged("bcdedit.exe", "/deletevalue {current} " + e.Key);
                    else if (e.Value == "Yes" || e.Value == "No") Util.RunLogged("bcdedit.exe", "/set {current} " + e.Key + (e.Value == "Yes" ? " on" : " off"));
                    else Log.Error("bcd " + e.Key + ": original value '" + e.Value + "' not understood, left as is");
                }
            }
            State.Remove(State.IsSystem);
            Log.Info("done: restored system settings");
        }

        static void BroadcastSettingChange()
        {
            IntPtr result;
            Native.SendMessageTimeout((IntPtr)0xffff, 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 3000, out result);
        }
    }
}

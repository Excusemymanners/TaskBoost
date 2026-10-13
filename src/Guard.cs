using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskBoost
{
    // Level: 2 = alert (red), 1 = notice (amber), 0 = information
    class Finding
    {
        public string Category, Key, Title, Detail, Path;
        public string Extra, Tag; // optional third line and right-hand verdict label
        public string[] Ref;      // startup items: where the entry lives, so it can be switched off and on
        public bool Off;
        public int Level;
        public float Bar = -1F;   // health score parts: how full the row's gauge is, 0 to 1
        public string Signature { get { return Level + "|" + Key + "|" + Title + "|" + Detail; } }
    }

    // Watches for things worth knowing about. It observes and reports; it does not block or remove anything.
    static class Guard
    {
        public const string OwnRunName = "TaskBoostGuard";

        static readonly object Sync = new object();
        static HashSet<string> known;                       // keys the user accepted, or that were already there on the first scan
        static readonly List<string> fresh = new List<string>();
        static readonly Dictionary<string, string> publishers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string KnownFile { get { return Path.Combine(App.DataDir, "known.txt"); } }

        public static List<Finding> Scan(ICollection<string> watchers)
        {
            lock (Sync)
            {
                LoadKnown();
                var list = new List<Finding>();
                if (watchers.Contains("sensors")) Run(list, Sensors);
                if (watchers.Contains("network")) Run(list, Network);
                if (watchers.Contains("keyboards")) Run(list, Keyboards);
                if (watchers.Contains("remote")) Run(list, Remote);
                if (watchers.Contains("startup")) Run(list, StartupEntries);
                if (watchers.Contains("shield")) Run(list, Protection);
                if (watchers.Contains("shield")) Run(list, Suspicious);
                if (fresh.Count > 0)
                {
                    AddKnown(fresh);
                    fresh.Clear();
                }
                list.Sort(delegate(Finding a, Finding b)
                {
                    int c = b.Level.CompareTo(a.Level);
                    if (c == 0) c = string.CompareOrdinal(a.Category, b.Category);
                    return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });
                return list;
            }
        }

        public static void MarkKnown(IEnumerable<string> keys)
        {
            lock (Sync)
            {
                LoadKnown();
                AddKnown(keys);
            }
        }

        static void Run(List<Finding> list, Action<List<Finding>> watcher)
        {
            try { watcher(list); }
            catch (Exception ex) { Add(list, "error", "", 0, "A check could not run", ex.Message, null); }
        }

        static void LoadKnown()
        {
            if (known != null) return;
            known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(KnownFile)) foreach (string line in File.ReadAllLines(KnownFile)) known.Add(line);
        }

        static void AddKnown(IEnumerable<string> keys)
        {
            var added = new List<string>();
            foreach (string k in keys) if (known.Add(k)) added.Add(k);
            if (added.Count > 0) File.AppendAllLines(KnownFile, added, Encoding.UTF8);
        }

        // True if accepted before. The first time a category is ever scanned, whatever is already there
        // becomes the starting point instead of raising an alert for every existing item.
        static bool Seen(string category, string key)
        {
            if (known.Contains(key)) return true;
            if (known.Contains("baseline|" + category)) return false;
            fresh.Add(key);
            return true;
        }

        static void BaselineDone(string category)
        {
            if (!known.Contains("baseline|" + category)) fresh.Add("baseline|" + category);
        }

        static void Add(List<Finding> list, string category, string key, int level, string title, string detail, string path)
        {
            list.Add(new Finding { Category = category, Key = key, Level = level, Title = title, Detail = detail, Path = path });
        }

        // ---- camera, microphone, location ----

        static void Sensors(List<Finding> list)
        {
            Sensor(list, "webcam", "Camera");
            Sensor(list, "microphone", "Microphone");
            Sensor(list, "location", "Location");
        }

        // Windows records per app when it started and stopped using each capability; stop = 0 means "using it now"
        static void Sensor(List<Finding> list, string capability, string label)
        {
            string store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + capability;
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                using (var root = hive.OpenSubKey(store))
                {
                    if (root == null) continue;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (name != "NonPackaged")
                        {
                            using (var app = root.OpenSubKey(name)) SensorApp(list, capability, label, app, name.Split('_')[0], null);
                            continue;
                        }
                        using (var desktop = root.OpenSubKey(name))
                            foreach (string encoded in desktop.GetSubKeyNames())
                            {
                                string path = encoded.Replace('#', '\\');
                                using (var app = desktop.OpenSubKey(encoded)) SensorApp(list, capability, label, app, Path.GetFileName(path), path);
                            }
                    }
                }
        }

        static void SensorApp(List<Finding> list, string capability, string label, RegistryKey app, string name, string path)
        {
            if (app == null) return;
            long start = Convert.ToInt64(app.GetValue("LastUsedTimeStart", 0L)), stop = Convert.ToInt64(app.GetValue("LastUsedTimeStop", 0L));
            if (start == 0) return;
            string key = "sensor|" + capability + "|" + (path ?? name);
            if (list.Exists(delegate(Finding f) { return f.Key == key; })) return;
            if (stop == 0)
                Add(list, "sensor", key, known.Contains(key) ? 1 : 2, label + " in use: " + name, "Using it right now" + (path != null ? "  -  " + path : ""), path);
            else
            {
                // In the last quarter of an hour. (A time still ahead of the clock means the clock was set back
                // since: that is not "just now".)
                DateTime ended = DateTime.FromFileTime(stop);
                if (ended <= DateTime.Now && DateTime.Now - ended < TimeSpan.FromMinutes(15))
                    Add(list, "sensor", key, 0, label + " was used by " + name, "Stopped at " + ended.ToString("HH:mm"), path);
            }
        }

        // ---- network ----

        class Online { public string Name, Path; public List<string> Remotes = new List<string>(); }

        static void Network(List<Finding> list)
        {
            var byPid = new Dictionary<int, List<string>>();
            ReadTcp(2, byPid);   // IPv4
            ReadTcp(23, byPid);  // IPv6
            var programs = new Dictionary<string, Online>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in byPid)
            {
                string path = ProcessPath(kv.Key);
                string name = path != null ? Path.GetFileName(path) : ProcessName(kv.Key);
                Online o;
                if (!programs.TryGetValue(path ?? name, out o)) programs[path ?? name] = o = new Online { Name = name, Path = path };
                o.Remotes.AddRange(kv.Value);
            }
            foreach (var o in programs.Values)
            {
                string key = "net|" + (o.Path ?? o.Name);
                // No readable path means a protected system process
                string publisher = o.Path == null ? "system process" : Publisher(o.Path);
                // Windows' own components going online are not news
                bool seen = Seen("net", key) || publisher == "Windows" || o.Path == null;
                int n = o.Remotes.Count;
                string detail = n + (n == 1 ? " connection: " : " connections: ") + string.Join(", ", o.Remotes.GetRange(0, Math.Min(2, n))) + (n > 2 ? ", ..." : "")
                    + "  -  " + (publisher ?? "NOT SIGNED") + (publisher == null ? "  -  " + o.Path : "");
                if (seen) Add(list, "net", key, 0, "Online: " + o.Name, detail, o.Path);
                else if (publisher == null) Add(list, "net", key, 2, "Unsigned program is online: " + o.Name, detail, o.Path);
                else Add(list, "net", key, 1, "New program is online: " + o.Name, detail, o.Path);
            }
            BaselineDone("net");
        }

        // File names of the programs that have an internet connection open right now
        public static List<string> OnlinePrograms()
        {
            var byPid = new Dictionary<int, List<string>>();
            ReadTcp(2, byPid);
            ReadTcp(23, byPid);
            var names = new List<string>();
            foreach (int pid in byPid.Keys)
            {
                string path = ProcessPath(pid);
                if (path == null) continue; // protected system process: never touched
                string name = System.IO.Path.GetFileName(path);
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

        // Established TCP connections to public addresses, grouped by owning process
        static void ReadTcp(int family, Dictionary<int, List<string>> byPid)
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
            size += 8192;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, family, 5, 0) != 0) return;
                int count = Marshal.ReadInt32(buffer);
                bool v4 = family == 2;
                int rowSize = v4 ? 24 : 56;
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(buffer, 4 + i * rowSize);
                    if (Marshal.ReadInt32(row, v4 ? 0 : 48) != 5) continue; // not established
                    var bytes = new byte[v4 ? 4 : 16];
                    Marshal.Copy(IntPtr.Add(row, v4 ? 12 : 24), bytes, 0, bytes.Length);
                    var ip = new IPAddress(bytes);
                    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                    if (!IsPublic(ip)) continue;
                    int rawPort = Marshal.ReadInt32(row, v4 ? 16 : 44);
                    int port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                    int pid = Marshal.ReadInt32(row, v4 ? 20 : 52);
                    List<string> remotes;
                    if (!byPid.TryGetValue(pid, out remotes)) byPid[pid] = remotes = new List<string>();
                    remotes.Add((ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString()) + ":" + port);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        static bool IsPublic(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return false;
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4)
                return !(b[0] == 0 || b[0] == 10 || b[0] >= 224 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC || ip.Equals(IPAddress.IPv6Any));
        }

        // ---- processes and signatures ----

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        static string ProcessName(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch (Exception) { return "process " + pid; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile, pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData, pwszURLReference;
            public uint dwProvFlags, dwUIContextFlags;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll")]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        static bool HasValidSignature(string path)
        {
            var file = new WINTRUST_FILE_INFO();
            file.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            file.pcwszFilePath = path;
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new WINTRUST_DATA();
                data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                data.dwUIChoice = 2;        // no UI
                data.dwUnionChoice = 1;     // file
                data.pFile = pFile;
                data.dwProvFlags = 0x1000;  // do not go online to check
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }

        // Signer name, or null when the file has no valid signature
        public static string Publisher(string path)
        {
            lock (publishers) return PublisherLocked(path);
        }

        static string PublisherLocked(string path)
        {
            string result;
            if (publishers.TryGetValue(path, out result)) return result;
            try
            {
                if (HasValidSignature(path))
                    result = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false);
                // Windows components and Store apps are signed through a catalog, not inside the file
                else if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) result = "Windows";
                else if (path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0) result = "Microsoft Store app";
            }
            catch (Exception) { result = null; }
            publishers[path] = result;
            return result;
        }

        // ---- keyboards ----

        // A device that suddenly registers as a keyboard may be a "BadUSB" stick typing commands
        static void Keyboards(List<Finding> list)
        {
            var ids = new List<string>();
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\kbdhid\Enum"))
            {
                int count = k == null ? 0 : Convert.ToInt32(k.GetValue("Count", 0));
                for (int i = 0; i < count; i++)
                {
                    string[] parts = (k.GetValue(i.ToString()) as string ?? "").Split('\\');
                    if (parts.Length < 2) continue;
                    string id = Regex.Replace(parts[1], @"&(MI|COL)_?\w+", "", RegexOptions.IgnoreCase).ToUpperInvariant();
                    if (!ids.Contains(id)) ids.Add(id);
                }
            }
            var names = new List<string>();
            if (HasPs2Keyboard()) names.Add("Built-in keyboard (PS/2)");
            foreach (string id in ids)
            {
                string key = "kbd|" + id, name = Knowledge.KeyboardName(id);
                names.Add(name);
                bool board = Knowledge.ProgrammableBoard(id);
                if (!Seen("kbd", key))
                    Add(list, "kbd", key, 2, "New keyboard connected: " + name, (board ? "This maker's boards are what keystroke-injection sticks are built from. " : "") + "If you did not just plug in a keyboard, unplug the last USB device you connected", null);
                else if (board && !known.Contains(key + "|board"))
                    Add(list, "kbd", key + "|board", 1, "Keyboard made from a programmable board: " + name, "Normal keyboards do not use this maker. Fine if it is your own project; otherwise unplug it", null);
                else Add(list, "kbd", key, 0, "Keyboard: " + name, "Known keyboard device", null);
            }
            BaselineDone("kbd");

            // More than one keyboard is normal with an external one plugged in, and exactly what a hidden injector looks like
            if (names.Count > 1)
            {
                string key = "kbd|set|" + string.Join("+", ids);
                // Laptops often expose their own keyboard twice (keys plus hotkey/backlight controller)
                bool quiet = known.Contains(key) || names.TrueForAll(delegate(string n) { return n.IndexOf("built-in", StringComparison.OrdinalIgnoreCase) >= 0; });
                Add(list, "kbd", key, quiet ? 0 : 1, names.Count + " keyboards are connected at the same time",
                    string.Join("  +  ", names) + (quiet ? "" : ".  If you only use one, find out what the other is"), null);
            }
        }

        static bool HasPs2Keyboard()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\i8042prt\Enum"))
            {
                int count = k == null ? 0 : Convert.ToInt32(k.GetValue("Count", 0));
                for (int i = 0; i < count; i++)
                {
                    object cls = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\" + k.GetValue(i.ToString()), "ClassGUID", null);
                    if (string.Equals(cls as string, "{4d36e96b-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase)) return true; // keyboard class
                }
            }
            return false;
        }

        // ---- remote control and mirroring ----

        static readonly Dictionary<string, string> RemoteTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "teamviewer", "TeamViewer" }, { "teamviewer_service", "TeamViewer" }, { "anydesk", "AnyDesk" }, { "rustdesk", "RustDesk" },
            { "winvnc", "VNC server" }, { "winvnc4", "VNC server" }, { "vncserver", "VNC server" }, { "tvnserver", "TightVNC server" }, { "uvnc_service", "UltraVNC" },
            { "screenconnect.clientservice", "ScreenConnect" }, { "screenconnect.windowsclient", "ScreenConnect" },
            { "logmein", "LogMeIn" }, { "lmiguardiansvc", "LogMeIn" }, { "srservice", "Splashtop" }, { "srserver", "Splashtop" },
            { "remoting_host", "Chrome Remote Desktop" }, { "parsecd", "Parsec" }, { "msra", "Windows Remote Assistance" }, { "quickassist", "Quick Assist" },
            { "rserver3", "Radmin Server" }, { "aa_v3", "Ammyy Admin" }, { "ultraviewer_service", "UltraViewer" }, { "ultraviewer_desktop", "UltraViewer" },
            { "supremo", "Supremo" }, { "supremoservice", "Supremo" }, { "dwagent", "DWService" }, { "dwagsvc", "DWService" },
            { "client32", "NetSupport" }, { "zaservice", "Zoho Assist" }, { "ateraagent", "Atera" }, { "hoptodesk", "HopToDesk" }, { "sunshine", "Sunshine streaming host" },
            { "letsview", "LetsView mirroring" }, { "apowermirror", "ApowerMirror" }, { "airserver", "AirServer mirroring" }, { "reflector", "Reflector mirroring" }
        };

        static void Remote(List<Finding> list)
        {
            if (SystemInformation.TerminalServerSession)
                Add(list, "remote", "remote|session", 2, "This session is running over Remote Desktop", "Someone is connected to this PC remotely and can see and control it", null);

            object deny = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1);
            if (deny != null && Convert.ToInt32(deny) == 0)
                Add(list, "remote", "remote|rdp-on", known.Contains("remote|rdp-on") ? 0 : 1, "Remote Desktop is switched on", "Other computers are allowed to connect to this PC", null);

            var found = new List<string>();
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    string label;
                    if (!RemoteTools.TryGetValue(p.ProcessName, out label) || found.Contains(label)) continue;
                    found.Add(label);
                    string key = "remote|" + label;
                    Add(list, "remote", key, known.Contains(key) ? 1 : 2, "Remote-access tool running: " + label, "It can show or control this screen from another device", ProcessPath(p.Id));
                }

            int screens = Screen.AllScreens.Length;
            if (screens > 1)
            {
                string key = "remote|screens|" + screens;
                Add(list, "remote", key, Seen("screens", key) ? 0 : 1, screens + " displays are active", "The picture may be mirrored or extended to another screen, projector or wireless display", null);
            }
            BaselineDone("screens");
        }

        // ---- startup entries ----

        static void StartupEntries(List<Finding> list)
        {
            string[] keys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string path in keys)
                    using (var k = hive.OpenSubKey(path))
                    {
                        if (k == null) continue;
                        foreach (string name in k.GetValueNames())
                            if (name != OwnRunName) StartupEntry(list, name, Convert.ToString(k.GetValue(name)));
                    }
            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                string dir = Environment.GetFolderPath(folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir))
                    if (!file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) StartupEntry(list, Path.GetFileName(file), file);
            }
            BaselineDone("run");
        }

        static void StartupEntry(List<Finding> list, string name, string command)
        {
            string key = "run|" + name + "|" + command;
            if (Seen("run", key)) return;
            var info = StartupInfo.Describe(name, command, "");
            Add(list, "run", key, 2, "New program set to start with Windows: " + name, info.Detail + "  -  " + command, info.Path);
        }

        // ---- protection status ----

        static void Protection(List<Finding> list)
        {
            bool realtimeOff = Flag(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring")
                            || Flag(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring")
                            || Flag(@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware");
            if (realtimeOff)
                Add(list, "shield", "shield|defender-off", known.Contains("shield|defender-off") ? 0 : 2, "Windows Defender real-time protection is off", "Nothing is scanning files as they are opened, unless another antivirus is installed", null);

            foreach (string profile in new[] { "StandardProfile", "PublicProfile" })
            {
                object on = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profile, "EnableFirewall", 1);
                if (on == null || Convert.ToInt32(on) != 0) continue;
                string key = "shield|firewall-off|" + profile;
                Add(list, "shield", key, known.Contains(key) ? 0 : 1, "Windows Firewall is off (" + (profile == "PublicProfile" ? "public" : "private") + " networks)", "Incoming connections are not being filtered", null);
            }
        }

        // ---- suspicious signs ----

        static void Suspicious(List<Finding> list)
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    string path = ProcessPath(p.Id);
                    if (path == null || !seen.Add(path)) continue;
                    string file = System.IO.Path.GetFileName(path), exe = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    string key = "odd|" + path;
                    if (known.Contains(key)) continue;
                    if (Array.IndexOf(StartupInfo.SystemNames, exe) >= 0 && !path.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
                        Add(list, "odd", key, 2, "Program disguised as a Windows component: " + file, "Real copies of " + file + " only live in the Windows folder. This one runs from " + path, path);
                    else if (Regex.IsMatch(file, @"\.(pdf|docx?|xlsx?|jpe?g|png|txt|zip|mp4)\.(exe|scr|com)$", RegexOptions.IgnoreCase))
                        Add(list, "odd", key, 2, "Program pretending to be a document: " + file, "The double file extension is a classic trick. It runs from " + path, path);
                    else if (path.IndexOf("\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0 && Publisher(path) == null)
                        Add(list, "odd", key, 2, "Unsigned program running from a temporary folder: " + file, "Installers do this briefly; anything that stays is worth a look. " + path, path);
                }

            const string internet = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            string proxy = Convert.ToInt32(Registry.GetValue(internet, "ProxyEnable", 0) ?? 0) == 1 ? Registry.GetValue(internet, "ProxyServer", "") as string : null;
            string script = Registry.GetValue(internet, "AutoConfigURL", null) as string;
            if (!string.IsNullOrEmpty(proxy ?? script))
            {
                string key = "odd|proxy|" + (proxy ?? script);
                Add(list, "odd", key, known.Contains(key) ? 0 : 1, "Internet traffic goes through a proxy", (proxy ?? script) + "  -  whoever runs it can see which sites this PC visits", null);
            }

            string hosts = System.IO.Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
            int redirects = 0;
            if (File.Exists(hosts))
                foreach (string line in File.ReadAllLines(hosts))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && t[0] != '#' && !Regex.IsMatch(t, @"\slocalhost(\s|$)", RegexOptions.IgnoreCase)) redirects++;
                }
            if (redirects > 0)
            {
                string key = "odd|hosts|" + redirects;
                Add(list, "odd", key, known.Contains(key) ? 0 : 1, "The hosts file redirects " + redirects + (redirects == 1 ? " address" : " addresses"), "Ad blockers do this on purpose; malware does it to send you to fake sites. " + hosts, hosts);
            }
        }

        static bool Flag(string path, string name)
        {
            object v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + path, name, 0);
            return v != null && Convert.ToInt32(v) == 1;
        }
    }
}

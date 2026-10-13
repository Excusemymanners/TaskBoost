using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskBoost
{
    static class App
    {
        public const string Name = "TaskBoost";
        public static string DataDir;

        public static string LogFile { get { return Path.Combine(DataDir, "log.txt"); } }
        // The exe that is running. (The tests point this at the real TaskBoost.exe, so that nothing they do
        // can leave the test program's own path behind in a setting.)
        public static string ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        // Per-machine data (log, undo state, selection) lives outside the app folder so the folder can be shared as-is
        public static void Init(string dataDir)
        {
            DataDir = dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
            Directory.CreateDirectory(DataDir);
            var log = new FileInfo(LogFile);
            if (log.Exists && log.Length > 512 * 1024) log.Delete();
        }

        public static bool IsElevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }

    static class Log
    {
        public static event Action<string> Line;
        public static int Errors;

        public static void Info(string message) { Write(message); }
        public static void Error(string message) { Errors++; Write("ERROR  " + message); }

        static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            try { File.AppendAllText(App.LogFile, line + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { }
            var handler = Line;
            if (handler != null) handler(line);
        }
    }

    static class Settings
    {
        const string BoostFile = "settings.txt";

        // Selected tweak group keys; everything is selected until the user saves a choice
        public static List<string> Load()
        {
            var all = new List<string>();
            foreach (var g in Tweaks.Groups) if (g.DefaultOn) all.Add(g.Key);
            return Load(BoostFile, all);
        }

        public static void Save(IEnumerable<string> keys) { Save(BoostFile, keys); }

        // A named list of switched-on keys, with defaults until it is first saved
        public static List<string> Load(string file, IEnumerable<string> defaults)
        {
            string path = Path.Combine(App.DataDir, file);
            if (!File.Exists(path)) return new List<string>(defaults);
            return new List<string>(File.ReadAllText(path).Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public static void Save(string file, IEnumerable<string> keys)
        {
            File.WriteAllText(Path.Combine(App.DataDir, file), string.Join(",", keys));
        }
    }

    static class Util
    {
        public static int Run(string exe, string args, out string output) { return Run(exe, args, out output, null); }

        public static int Run(string exe, string args, out string output, Encoding encoding)
        {
            var psi = new ProcessStartInfo(exe, args);
            if (encoding != null) psi.StandardOutputEncoding = psi.StandardErrorEncoding = encoding;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                output = (output + err.Result).Trim();
                return p.ExitCode;
            }
        }

        // Runs a PowerShell script without any quoting trouble
        public static int PowerShell(string script, out string output)
        {
            script = "$ProgressPreference = 'SilentlyContinue'\r\n" + script; // no progress records mixed into the output
            return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)), out output);
        }

        public static bool RunLogged(string exe, string args)
        {
            string output;
            int rc = Run(exe, args, out output);
            if (rc != 0) Log.Error(exe + " " + args + " -> exit " + rc + ": " + Regex.Replace(output, @"\s+", " "));
            return rc == 0;
        }

        // Starts a service again. One that is running already (code 1056) is what was wanted, not an error.
        public static void StartService(string name)
        {
            string output;
            int rc = Run("sc.exe", "start \"" + name + "\"", out output);
            if (rc != 0 && rc != 1056) Log.Error("sc.exe start \"" + name + "\" -> exit " + rc + ": " + Regex.Replace(output, @"\s+", " "));
        }
    }

    // Runs a command in a second copy of this exe, elevated when possible, and returns what it logged
    static class Helper
    {
        public static string Run(string command)
        {
            long offset = File.Exists(App.LogFile) ? new FileInfo(App.LogFile).Length : 0;
            // One approval per session: the elevated helper takes the command if the user has approved it
            if (!App.IsElevated && Session.Ensure(true) && Session.Send(command)) return Tail(offset);
            var psi = new ProcessStartInfo(App.ExePath, "--elevated " + command + " --data \"" + App.DataDir + "\"");
            psi.UseShellExecute = true;
            if (!App.IsElevated) psi.Verb = "runas";
            try
            {
                using (var p = Process.Start(psi)) p.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Declined: do the part that works without administrator rights
                Log.Info("Administrator approval was declined: doing only what works without it.");
                psi.Verb = "";
                using (var p = Process.Start(psi)) p.WaitForExit();
            }
            return Tail(offset);
        }

        static string Tail(long offset)
        {
            if (!File.Exists(App.LogFile)) return "";
            using (var fs = new FileStream(App.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(Math.Min(offset, fs.Length), SeekOrigin.Begin);
                return new StreamReader(fs).ReadToEnd();
            }
        }
    }

    // "Approve once per session": an elevated copy of this exe, started with one Windows prompt, that runs
    // TaskBoost's own administrator commands for the rest of the session. It lives only as long as the window
    // that started it, and it takes commands only from that window: the pipe tells it which process connected,
    // and anything but the parent window (same process id, same exe) is turned away.
    static class Session
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out int clientPid);

        static Process helper;
        static bool declined;

        static string PipeName(int windowPid) { return "TaskBoost-session-" + windowPid; }

        public static bool Alive
        {
            get
            {
                if (helper != null && !helper.HasExited) return true;
                try
                {
                    using (var probe = new NamedPipeClientStream(".", PipeName(Process.GetCurrentProcess().Id), PipeDirection.InOut)) { probe.Connect(150); return true; }
                }
                catch (Exception) { return false; }
            }
        }

        // Starts the helper if it is not running. With ask = true the user is asked first, once per session.
        public static bool Ensure(bool ask)
        {
            if (Alive) return true;
            if (System.Windows.Forms.Application.OpenForms.Count == 0) return false; // no window (the MCP server): keep the per-action prompt
            if (ask)
            {
                if (declined) return false;
                if (!Dialog.Ask(null, "TaskBoost needs administrator approval for this.\n\nApprove once for this session, so it does not ask again until you exit TaskBoost?\n\n(No: Windows asks at every action, as before.)"))
                {
                    declined = true;
                    return false;
                }
            }
            return Start();
        }

        public static bool Start()
        {
            var psi = new ProcessStartInfo(App.ExePath, "--elevated-session --parent " + Process.GetCurrentProcess().Id + " --data \"" + App.DataDir + "\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try { helper = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception)
            {
                Log.Info("Administrator approval was declined: TaskBoost will ask at each action instead.");
                declined = true;
                return false;
            }
            // Wait for the helper to open its pipe
            for (int i = 0; i < 100 && Alive; i++)
            {
                try
                {
                    using (var probe = new NamedPipeClientStream(".", PipeName(Process.GetCurrentProcess().Id), PipeDirection.InOut))
                    {
                        probe.Connect(100);
                        Log.Info("Administrator approved for this session: TaskBoost will not ask again until you exit it.");
                        return true;
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { System.Threading.Thread.Sleep(100); }
            }
            return Alive;
        }

        // Hands one command to the helper and waits until it is done
        public static bool Send(string command)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName(Process.GetCurrentProcess().Id), PipeDirection.InOut))
                {
                    pipe.Connect(5000);
                    var writer = new StreamWriter(pipe);
                    writer.WriteLine(command);
                    writer.Flush();
                    return new StreamReader(pipe).ReadLine() == "ok";
                }
            }
            catch (Exception ex)
            {
                Log.Info("session helper did not answer (" + ex.Message + "); asking Windows instead");
                return false;
            }
        }

        public static void Stop()
        {
            if (!Alive) return;
            try { helper.Kill(); }
            catch (Exception) { }
        }

        // Helper side. Serves the parent window until that window exits.
        public static int Serve(int parentPid, Func<string, int> run)
        {
            if (!App.IsElevated) return 2;
            Process parent;
            try { parent = Process.GetProcessById(parentPid); }
            catch (ArgumentException) { return 2; }
            string parentExe;
            try { parentExe = parent.MainModule.FileName; }
            catch (Exception) { parentExe = null; }
            if (!string.Equals(parentExe, App.ExePath, StringComparison.OrdinalIgnoreCase)) return 2; // only our own window may own a helper
            var watch = new System.Threading.Thread(delegate() { parent.WaitForExit(); Environment.Exit(0); });
            watch.IsBackground = true;
            watch.Start();

            // The pipe must be reachable from the non-elevated window of the same user
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            while (true)
            {
                using (var server = new NamedPipeServerStream(PipeName(parentPid), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security))
                {
                    try
                    {
                        server.WaitForConnection();
                        int clientPid;
                        bool ours = GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out clientPid) && clientPid == parentPid;
                        var reader = new StreamReader(server);
                        var writer = new StreamWriter(server);
                        string command = reader.ReadLine();
                        if (!ours) { Log.Error("session helper: a command from another process (pid " + clientPid + ") was refused"); writer.WriteLine("refused"); }
                        else if (!string.IsNullOrEmpty(command)) { run(command); writer.WriteLine("ok"); }
                        writer.Flush();
                    }
                    catch (IOException) { }
                }
            }
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, ref int pv, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO pv, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SystemParametersInfo(uint action, uint param, string pv, uint flags);
        [DllImport("user32.dll")]
        public static extern bool SetSysColors(int count, int[] elements, int[] colors);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        // Dark title bar (attribute 20 on current Windows 10/11, 19 on older Windows 10 builds)
        public static void DarkTitleBar(IntPtr window)
        {
            int on = 1;
            if (DwmSetWindowAttribute(window, 20, ref on, 4) != 0) DwmSetWindowAttribute(window, 19, ref on, 4);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}

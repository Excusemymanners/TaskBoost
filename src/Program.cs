using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("TaskBoost")]
[assembly: AssemblyDescription("Applies a lean, fast, battery-friendly Windows setup")]
[assembly: AssemblyProduct("TaskBoost")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace TaskBoost
{
    static class Program
    {
        // TaskBoost.exe                         open the window
        // TaskBoost.exe --mcp                   Model Context Protocol server on stdin/stdout, started by Claude
        // TaskBoost.exe --guard                 start hidden in the tray with the guard watching (used at sign-in)
        // TaskBoost.exe --exit                  ask the running TaskBoost to close (leaves the tray too)
        // TaskBoost.exe --apply                 apply the saved selection without a window (used at sign-in);
        //                                       administrator groups run only if the process is already elevated
        // TaskBoost.exe --elevated <command>    internal: the window relaunches itself elevated for admin work
        [STAThread]
        static int Main(string[] args)
        {
            App.Init(Value(args, "--data"));
            Lang.Load();

            if (Array.IndexOf(args, "--mcp") >= 0) return Mcp.Run();
            if (Array.IndexOf(args, "--elevated-session") >= 0)
            {
                int parent;
                if (!int.TryParse(Value(args, "--parent"), out parent)) return 2;
                return Session.Serve(parent, delegate(string line)
                {
                    string[] queued = Split(line);
                    return queued.Length == 0 ? 0 : RunElevated(queued[0], queued);
                });
            }

            string elevated = Value(args, "--elevated");
            if (elevated != null) return RunElevated(elevated, args);

            if (Array.IndexOf(args, "--apply") >= 0)
            {
                var keys = Settings.Load();
                Log.Info("--- applying at sign-in / from command line ---");
                Tweaks.Apply(keys, false);
                if (App.IsElevated) Tweaks.Apply(keys, true);
                else Log.Info("not elevated: administrator groups skipped");
                return Log.Errors > 0 ? 1 : 0;
            }

            if (Array.IndexOf(args, "--exit") >= 0)
            {
                EventWaitHandle running;
                if (EventWaitHandle.TryOpenExisting(ExitSignal, out running))
                    using (running) running.Set();
                return 0;
            }

            bool hidden = Array.IndexOf(args, "--guard") >= 0;
            bool first;
            using (var mutex = new Mutex(true, "TaskBoost.Window", out first))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "TaskBoost.Show"))
            {
                if (!first)
                {
                    // Already running (perhaps in the tray): bring that window forward instead
                    if (!hidden) showSignal.Set();
                    return 0;
                }
                using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignal))
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Crash(e.Exception); };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject as Exception); };
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Theme.Init(); // before any control is made: their sizes depend on the screen scale
                    Motion.Load();
                    System.Threading.Tasks.Task.Factory.StartNew(Housekeeping);
                    Application.Run(new MainForm(hidden, showSignal, exitSignal));
                }
            }
            return 0;
        }

        const string ExitSignal = "TaskBoost.Exit";

        // Done in the background at every start
        static void Housekeeping()
        {
            // Keep the link to Claude in place without the user doing anything (also repairs it if the folder was moved)
            try { Log.Info(ClaudeLink.EnsureRegistered()); }
            catch (Exception) { }
            // The phone charging mode is gone; anyone who still had it on gets their sleep and lid settings back
            try { if (ChargeMode.IsOn) Helper.Run("charge-off"); }
            catch (Exception ex) { Log.Error(ex.Message); }
            // Net focus that was still on when TaskBoost last stopped: nothing is keeping it any more
            try { NetFocus.EndLeftover(); }
            catch (Exception ex) { Log.Error(ex.Message); }
        }

        // Log the problem and tell the user, instead of the window just disappearing
        static void Crash(Exception ex)
        {
            Log.Error("unexpected problem: " + ex);
            MessageBox.Show("TaskBoost ran into an unexpected problem.\n\n" + (ex == null ? "" : ex.Message) + "\n\nThe details are in the log (Boost tab, Open log folder).", App.Name);
        }

        static int RunElevated(string command, string[] args)
        {
            try
            {
                if (!App.IsElevated) Log.Info("not running as administrator: only the parts that work without it will be done");
                switch (command)
                {
                    case "apply":
                        var keys = new List<string>((Value(args, "--groups") ?? "").Split(','));
                        Tweaks.Apply(keys, true);
                        break;
                    case "restore": Tweaks.RestoreSystem(); break;
                    case "startup-on": Startup.Install(Value(args, "--user")); break;
                    case "startup-off": Startup.Remove(); break;
                    case "game-on": GameMode.On(new List<string>((Value(args, "--options") ?? "").Split(','))); break;
                    case "game-off": GameMode.Off(); break;
                    case "startup-set":
                        string target = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Value(args, "--target")));
                        StartupInfo.SetEnabled(target.Split('\n'), Value(args, "--on") == "1");
                        break;
                    case "charge-off": ChargeMode.Off(); break;
                    case "updates-check": Updates.Check(); break;
                    case "updates-install": Updates.Install(); break;
                    case "wu-stop": Updates.Stop(); break;
                    case "wu-allow": Updates.Allow(); break;
                    case "net-on": NetFocus.On(Value(args, "--exe"), (Value(args, "--others") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)); break;
                    case "net-off": NetFocus.Off(); break;
                    case "free-memory": Memory.Free(); break;
                    case "care": Care.Run(new List<string>((Value(args, "--items") ?? "").Split(',')));  break;
                    case "clean": Cleaner.Run(new List<string>((Value(args, "--items") ?? "").Split(',')), false); break;
                    case "clean-preview": Cleaner.Run(new List<string>((Value(args, "--items") ?? "").Split(',')), true); break;
                    default: Log.Error("unknown command " + command); break;
                }
            }
            catch (Exception ex) { Log.Error(ex.Message); }
            return Log.Errors > 0 ? 1 : 0;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string line, out int count);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr memory);

        // The same splitting Windows does for a command line, so quoted arguments survive
        static string[] Split(string commandLine)
        {
            int count;
            IntPtr argv = CommandLineToArgvW("x " + commandLine, out count);
            if (argv == IntPtr.Zero) return new string[0];
            var args = new string[Math.Max(0, count - 1)];
            for (int i = 1; i < count; i++)
                args[i - 1] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, i * IntPtr.Size));
            LocalFree(argv);
            return args;
        }

        static string Value(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

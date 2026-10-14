using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TaskBoost
{
    // The window shell: header with the mascot, the tabs, and the tray icon
    class MainForm : Form
    {
        const int GuardTab = 2, TurboTab = 4;

        public static string ShortcutHelp { get { return Lang.Romanian ? ShortcutHelpRo : ShortcutHelpEn; } }

        const string ShortcutHelpRo =
            "Oriunde în Windows (TaskBoost pornit, chiar și în tray):\n" +
            "Ctrl+Alt+F1\tArată sau ascunde TaskBoost\n" +
            "Ctrl+Alt+F2\tGaming pornit / oprit\n" +
            "Ctrl+Alt+F3\tEliberează memoria\n" +
            "Ctrl+Alt+F5\tÎncărcare telefon pornit / oprit\n" +
            "Ctrl+Alt+F6\tÎntreabă Claude\n" +
            "Ctrl+Alt+F7\tTrimite păianjenul / cheamă-l înapoi\n" +
            "Ctrl+Alt+F8\tRaport rapid de sănătate (îl spune păianjenul)\n" +
            "Ctrl+Alt+F9\tConsum redus\n" +
            "Ctrl+Alt+F10\tNormal\n" +
            "Ctrl+Alt+F11\tPerformanță maximă\n\n" +
            "În fereastra TaskBoost:\n" +
            "Ctrl+1 ... Ctrl+6\tAcasă, Boost, Gardă, Pornire, Turbo, Îngrijire\n" +
            "Esc\tÎnchide fereastra în tray\n" +
            "F1\tLista asta";

        // The shortcut preset. Ctrl+Alt+F-keys work everywhere, even with the window closed; they are F-keys
        // rather than letters because Ctrl+Alt+letter types accented characters on many keyboards.
        public const string ShortcutHelpEn =
            "Anywhere in Windows (TaskBoost running, even in the tray):\n" +
            "Ctrl+Alt+F1\tShow or hide TaskBoost\n" +
            "Ctrl+Alt+F2\tGaming mode on / off\n" +
            "Ctrl+Alt+F3\tFree memory\n" +
            "Ctrl+Alt+F5\tCharge my phone on / off\n" +
            "Ctrl+Alt+F6\tAsk Claude\n" +
            "Ctrl+Alt+F7\tSend the buddy away / call it back\n" +
            "Ctrl+Alt+F8\tQuick health report (the buddy tells you)\n" +
            "Ctrl+Alt+F9\tLow power mode\n" +
            "Ctrl+Alt+F10\tNormal mode\n" +
            "Ctrl+Alt+F11\tBest performance mode\n\n" +
            "Inside the TaskBoost window:\n" +
            "Ctrl+1 ... Ctrl+6\tHome, Boost, Guard, Startup, Turbo, Care\n" +
            "Esc\tClose the window to the tray\n" +
            "F1\tThis list";
        static readonly Keys[] HotKeys = { Keys.F1, Keys.F2, Keys.F3, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11 };

        readonly HeaderPanel header = new HeaderPanel();
        readonly HomePage home = new HomePage();
        readonly BoostPage boost = new BoostPage();
        readonly GuardPage guard = new GuardPage();
        readonly StartupPage startup = new StartupPage();
        readonly TurboPage turbo = new TurboPage();
        readonly CarePage care = new CarePage();
        readonly TabBar tabs = new TabBar(new[] { "Home", "Boost", "Guard", "Startup", "Turbo", "Care" });
        readonly PageSlide slide = new PageSlide();
        // Pomodoro: runs while the laptop is in Study / work mode
        readonly System.Windows.Forms.Timer pomodoro = new System.Windows.Forms.Timer();
        DateTime phaseStart;
        string phase = ""; // "", "work" or "break"
        readonly Control[] pages;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly PopMenu trayMenu = new PopMenu();
        BuddyForm buddy;
        readonly Icon[] moods = new Icon[3]; // calm, notice, alert
        bool startHidden, exiting, trayHintShown;
        int current = -1;

        public MainForm(bool hidden, EventWaitHandle showSignal, EventWaitHandle exitSignal)
        {
            startHidden = hidden;
            Dialog.Home = this;
            pages = new Control[] { home, boost, guard, startup, turbo, care };
            Text = App.Name;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.Px(940), Theme.Px(790));
            MinimumSize = SizeFromClientSize(new Size(Theme.Px(900), Theme.Px(680)));
            for (int i = 0; i < 3; i++)
                using (var bmp = Icons.AppIconBitmap(32, Theme.LevelColor(i, Theme.Accent))) moods[i] = Icon.FromHandle(bmp.GetHicon());
            Icon = moods[0];

            // The slide, the tabs and the Ask Claude button are added first so they sit on top of what comes after
            Controls.Add(slide);
            tabs.Picked += ShowPage;
            Controls.Add(tabs);
            guard.btnClaude.Text = "Ask Claude";
            guard.btnClaude.Primary = true;
            guard.btnClaude.Click += delegate { guard.AskClaude(); };
            Controls.Add(guard.btnClaude);
            Controls.Add(header);
            Controls.AddRange(pages);

            turbo.ModeRequested += SetLaptopMode;
            guard.StatusChanged += OnGuardStatus;
            guard.AlertRaised += OnGuardAlert;
            turbo.GameModeChanged += delegate(bool on) { tabs.SetLight(TurboTab, on); };
            home.ScoreChanged += OnScore;
            home.LanguageChanged += Relaunch;
            home.RemoveRequested += RemoveTaskBoost;

            // The tray icon's menu: the same small dark menu the buddy has
            trayMenu.AddCaption(PomodoroStatus);
            trayMenu.Add(Lang.T("Open TaskBoost"), ShowFromTray).Icon = "app";
            trayMenu.Add(Lang.T("Keyboard shortcuts"), delegate { Dialog.Shortcuts(this, ShortcutHelp); }).Icon = "keyboards";
            trayMenu.Add(Lang.T("Ask Claude about this PC"), delegate { ShowFromTray(); ShowPage(GuardTab); guard.AskClaude(); }).Icon = "spark";
            PopItem buddyItem = trayMenu.Add("", delegate { if (buddy.IsGone) buddy.ComeBack(); else buddy.GoAway(); });
            buddyItem.Mood = "happy";
            buddyItem.Text = delegate { return Lang.T(buddy.IsGone ? "Call the buddy back" : "Send the buddy away"); };
            bool approved = false;
            PopItem adminItem = trayMenu.Add("", delegate { Session.Ensure(false); });
            adminItem.Icon = "shield";
            adminItem.Text = delegate
            {
                approved = Session.Alive;
                return Lang.T(approved ? "Administrator: approved for this session" : "Administrator: approve once for this session");
            };
            adminItem.Enabled = delegate { return !approved; };
            trayMenu.AddSeparator();
            trayMenu.Add(Lang.T("Exit"), Quit).Icon = "exit";
            tray.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) trayMenu.ShowAt(Cursor.Position); };
            tray.Icon = moods[0];
            tray.Text = App.Name;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.BalloonTipClicked += delegate { ShowFromTray(); ShowPage(GuardTab); };

            tabs.SetLight(TurboTab, GameMode.IsOn);
            int lastScore = HealthScore.Last;
            BuddySprite.Accessory = lastScore >= 90 ? "crown" : lastScore >= 75 ? "bow" : "";
            pomodoro.Interval = 15000;
            pomodoro.Tick += delegate { PomodoroTick(); };
            pomodoro.Start();
            // The desktop buddy; its switches are on the Guard tab
            buddy = new BuddyForm();
            buddy.Option = guard.Option;
            buddy.OpenApp = ShowFromTray;
            buddy.Costume = delegate { return LaptopMode.Effective; };
            buddy.SetLaptopMode = SetLaptopMode;
            buddy.FocusChanged = OnNetFocus;
            buddy.Status = PomodoroStatus;
            buddy.Perch = delegate { return Visible && WindowState != FormWindowState.Minimized ? (Rectangle?)Bounds : null; };
            FormClosed += delegate
            {
                buddy.Dispose();
                trayMenu.Dispose();
                pomodoro.Dispose();
                Session.Stop();
            };

            OnGuardStatus(0, 0);
            ShowPage(hidden ? GuardTab : 0);
            Relayout();

            IntPtr handle = Handle;
            KeyPreview = true;
            for (int i = 0; i < HotKeys.Length; i++)
                if (!Native.RegisterHotKey(handle, i, 0x1 | 0x2 | 0x4000, (uint)HotKeys[i])) // Alt + Ctrl, no auto-repeat
                    Log.Info("shortcut Ctrl+Alt+" + HotKeys[i] + " is already used by another program");
            FormClosed += delegate { for (int i = 0; i < HotKeys.Length; i++) Native.UnregisterHotKey(handle, i); };
            // A second launch of the exe signals this one to come forward instead of opening twice,
            // and "TaskBoost.exe --exit" signals it to close
            var listener = new Thread(delegate()
            {
                var signals = new WaitHandle[] { showSignal, exitSignal };
                while (true)
                {
                    int which = WaitHandle.WaitAny(signals);
                    try { BeginInvoke(which == 0 ? new Action(ShowFromTray) : new Action(Quit)); }
                    catch (InvalidOperationException) { return; }
                    if (which != 0) return;
                }
            });
            listener.IsBackground = true;
            listener.Start();
        }

        // With --guard the window stays hidden and only the tray icon shows
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312) OnShortcut(HotKeys[(int)m.WParam]); // WM_HOTKEY
            base.WndProc(ref m);
        }

        int focusPid;

        // Really closes TaskBoost, rather than tucking the window away in the tray
        void Quit()
        {
            exiting = true;
            Close();
        }

        // ---- Pomodoro: 25 minutes of focus, 5 of break, as long as Study / work is the laptop mode ----

        string PomodoroStatus()
        {
            if (phase == "") return null;
            int left = (int)Math.Ceiling((phase == "work" ? 25 : 5) - (DateTime.Now - phaseStart).TotalMinutes);
            return Lang.T(phase == "work" ? "Pomodoro: focus, " : "Pomodoro: break, ") + Math.Max(0, left) + " min";
        }

        void PomodoroTick()
        {
            if (LaptopMode.Effective != "work")
            {
                if (phase != "") { phase = ""; buddy.Say("Pomodoro off.", "happy"); }
                return;
            }
            double minutes = (DateTime.Now - phaseStart).TotalMinutes;
            if (phase == "")
            {
                phase = "work";
                phaseStart = DateTime.Now;
                buddy.Say("Pomodoro: 25 minutes of focus. I'll tell you when to take a break.", "think");
            }
            else if (phase == "work" && minutes >= 25)
            {
                phase = "break";
                phaseStart = DateTime.Now;
                buddy.Say("Break time! 5 minutes away from the screen.", "happy");
            }
            else if (phase == "break" && minutes >= 5)
            {
                phase = "work";
                phaseStart = DateTime.Now;
                buddy.Say("Back to work: another 25 minutes.", "think");
            }
        }

        // ---- Home tab ----

        void OnScore(int score)
        {
            string before = BuddySprite.Accessory;
            BuddySprite.Accessory = score >= 90 ? "crown" : score >= 75 ? "bow" : "";
            if (BuddySprite.Accessory == "crown" && before != "crown") buddy.Say("The PC is in great shape. I got a crown!", "star");
        }

        // The language is read once at start, so the window is started again
        void Relaunch()
        {
            exiting = true;
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & start \"\" \"" + App.ExePath + "\"");
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi);
            Close();
        }

        void RemoveTaskBoost()
        {
            if (!Dialog.Ask(this, "Remove TaskBoost? Every setting it changed goes back, sign-in entries and the Claude link are removed, and its data folder is deleted. The TaskBoost folder itself stays for you to delete.\n\nContinue?")) return;
            exiting = true;
            Enabled = false;
            header.SetStatus(Lang.T("Removing TaskBoost..."), 1);
            System.Threading.Tasks.Task.Factory.StartNew(delegate { Uninstall.Run(Log.Info); }).ContinueWith(delegate(System.Threading.Tasks.Task t)
            {
                if (t.Exception != null) Log.Error(t.Exception.InnerException.Message);
                Uninstall.DeleteDataAfterExit();
                Close();
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        // The buddy was told to give a program net focus (window and its process), or net focus ended (zero)
        void OnNetFocus(IntPtr window, int pid)
        {
            if (focusPid != 0)
            {
                try { using (var old = System.Diagnostics.Process.GetProcessById(focusPid)) old.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal; }
                catch (Exception) { }
                focusPid = 0;
            }
            if (window == IntPtr.Zero)
            {
                if (!NetFocus.IsOn) return;
                buddy.Say("Net focus off. Approve to put the network back.", "happy");
                System.Threading.Tasks.Task.Factory.StartNew(delegate { Helper.Run("net-off"); });
                return;
            }
            string path = Guard.ProcessPath(pid);
            if (path == null)
            {
                buddy.Say("I can not manage that program's network.", "cry");
                return;
            }
            string exe = System.IO.Path.GetFileName(path);
            focusPid = pid;
            try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) p.PriorityClass = System.Diagnostics.ProcessPriorityClass.High; }
            catch (Exception) { }
            buddy.Say(Lang.T("Net focus on ") + exe + Lang.T(". Approve the Windows prompt..."), "think");
            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                var others = Guard.OnlinePrograms();
                others.RemoveAll(delegate(string n) { return string.Equals(n, exe, StringComparison.OrdinalIgnoreCase) || n.StartsWith("TaskBoost", StringComparison.OrdinalIgnoreCase); });
                Helper.Run("net-on --exe \"" + exe + "\" --others \"" + string.Join(",", others) + "\"");
                return others.Count;
            }).ContinueWith(delegate(System.Threading.Tasks.Task<int> t)
            {
                if (t.Exception != null) buddy.Say(Lang.T("Net focus did not start: ") + t.Exception.InnerException.Message, "cry");
                else if (NetFocus.IsOn) buddy.Say(exe + Lang.T(" goes first on the internet. Other programs held back: ") + t.Result, "cool");
                else buddy.Say("Net focus needs your approval; nothing changed.", "cry");
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Called from the buddy's menu, the Turbo tab and the shortcuts
        void SetLaptopMode(string mode)
        {
            string[] said =
                mode == "low" ? new[] { "Low power: saving battery. Zzz...", "sleepy" }
                : mode == "best" ? new[] { "Best performance: full power!", "fierce" }
                : mode == "turbo" ? new[] { "GAMING! Everything goes to the game.", "fierce" }
                : mode == "work" ? new[] { "Work mode: quick apps, no notifications. Let's focus.", "think" }
                : new[] { "Back to normal.", "happy" };
            if (mode == "turbo" && !GameMode.IsOn) buddy.Say("Approve the Windows prompt to go turbo...", "think");
            System.Threading.Tasks.Task.Factory.StartNew(delegate { LaptopMode.Set(mode); }).ContinueWith(delegate(System.Threading.Tasks.Task t)
            {
                turbo.RefreshGameButton();
                if (t.Exception != null) buddy.Say(Lang.T("That did not work: ") + t.Exception.InnerException.Message, "cry");
                else buddy.Say(said[0], said[1]);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OnShortcut(Keys key)
        {
            switch (key)
            {
                case Keys.F1:
                    if (Visible && WindowState != FormWindowState.Minimized) Hide(); else ShowFromTray();
                    break;
                case Keys.F2:
                    buddy.Say(GameMode.IsOn ? "Leaving gaming mode" : "Gaming mode: let's go!", GameMode.IsOn ? "happy" : "cool");
                    turbo.ToggleGameMode();
                    break;
                case Keys.F3:
                    buddy.Say("Freeing memory", "music");
                    turbo.FreeMemory();
                    break;
                case Keys.F5:
                    buddy.Say(ChargeMode.IsOn ? "Charging mode off. The phone still charges while the laptop is on: unplug it to stop." : "Keeping the USB ports powered for your phone", "happy");
                    turbo.ToggleCharge();
                    break;
                case Keys.F6:
                    ShowFromTray();
                    guard.AskClaude();
                    break;
                case Keys.F9: SetLaptopMode("low"); break;
                case Keys.F10: SetLaptopMode("normal"); break;
                case Keys.F11: SetLaptopMode("best"); break;
                case Keys.F7:
                    if (buddy.IsGone) { buddy.ComeBack(); buddy.Say("I'm back!", "star"); } else buddy.GoAway();
                    break;
                case Keys.F8:
                    buddy.Say("Checking the PC...", "think");
                    System.Threading.Tasks.Task.Factory.StartNew(delegate { return Mcp.Capture(delegate { Care.Run(new[] { "health" }); }); })
                        .ContinueWith(delegate(System.Threading.Tasks.Task<string> t)
                        {
                            if (t.Exception != null) return;
                            var parts = new System.Collections.Generic.List<string>();
                            bool bad = false;
                            foreach (string line in t.Result.Split('\n'))
                            {
                                string s = line.Trim();
                                if (s.Contains("NOT HEALTHY") || s.Contains("LOW")) { parts.Add(s); bad = true; }
                                else if (s.StartsWith("Drive") || s.StartsWith("Battery") || s.StartsWith("Memory")) parts.Add(s);
                            }
                            buddy.Say(string.Join(".  ", parts), bad ? "alert" : "happy");
                        }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                    break;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) ShowPage(e.KeyCode - Keys.D1);
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.F1 && !e.Control && !e.Alt) Dialog.Shortcuts(this, ShortcutHelp);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) { Height = area.Height; Top = area.Top; }
            guard.ScanNow();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing the window keeps TaskBoost in the tray when the guard or the buddy should stay around
            if (!exiting && e.CloseReason == CloseReason.UserClosing && (guard.Option("tray") || guard.Option("buddy")))
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown) tray.ShowBalloonTip(4000, App.Name, Lang.T("Still here in the tray. Right-click the spider icon to exit."), ToolTipIcon.Info);
                trayHintShown = true;
                return;
            }
            // Net focus must not outlive the buddy that keeps it. (At shutdown Windows drops the rules by itself.)
            if (e.CloseReason != CloseReason.WindowsShutDown && NetFocus.IsOn)
            {
                try { Helper.Run("net-off"); }
                catch (Exception ex) { Log.Error(ex.Message); }
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void Relayout()
        {
            if (pages == null) return;
            int m = Theme.Px(20);
            int w = ClientSize.Width - 2 * m;
            header.SetBounds(m - Theme.Px(6), Theme.Px(12), w, Theme.Px(76));
            int th = Theme.Px(38), ask = Theme.Px(132), ty = header.Top + (header.Height - th) / 2;
            guard.btnClaude.SetBounds(m + w - ask, ty + Theme.Px(1), ask, th - Theme.Px(2));
            int left = m + Theme.Px(286), room = guard.btnClaude.Left - Theme.Px(14) - left;
            tabs.SetBounds(left, ty, Math.Max(Theme.Px(300), room), th);
            int y = header.Bottom + Theme.Px(10);
            foreach (var page in pages) page.SetBounds(m, y, w, ClientSize.Height - y - m);
            slide.SetBounds(m, y, w, ClientSize.Height - y - m);
        }

        void ShowPage(int index)
        {
            // While the window is on screen, the old page slides out and the new one in
            bool animate = Motion.On && current >= 0 && current != index && Visible && WindowState != FormWindowState.Minimized;
            Bitmap before = animate ? Picture(pages[current]) : null;
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
            tabs.Selected = index;
            if (animate) slide.Run(before, Picture(pages[index]), index > current ? 1 : -1);
            current = index;
            if (index == 1) ActiveControl = boost.DefaultControl;
        }

        static Bitmap Picture(Control page)
        {
            var bmp = new Bitmap(Math.Max(1, page.Width), Math.Max(1, page.Height));
            page.DrawToBitmap(bmp, new Rectangle(Point.Empty, page.Size));
            return bmp;
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnGuardStatus(int alerts, int notices)
        {
            int level = alerts > 0 ? 2 : notices > 0 ? 1 : 0;
            string text = level == 0 ? Lang.T("Fast, clean and guarded. All quiet.")
                : (alerts > 0 ? alerts + Lang.T(alerts == 1 ? " alert" : " alerts") : "")
                + (alerts > 0 && notices > 0 ? ", " : "")
                + (notices > 0 ? notices + Lang.T(notices == 1 ? " notice" : " notices") : "") + Lang.T(" on the Guard tab");
            header.SetStatus(text, level);
            if (buddy != null) buddy.AlertLevel = level;
            tabs.SetBadge(GuardTab, alerts + notices, level);
            if (tray.Icon != moods[level])
            {
                tray.Icon = moods[level];
                Icon = moods[level];
            }
        }

        void OnGuardAlert(Finding f)
        {
            if (guard.Option("notify")) tray.ShowBalloonTip(6000, f.Title, f.Detail, ToolTipIcon.Warning);
            buddy.Say(f.Title, "alert");
        }
    }
}

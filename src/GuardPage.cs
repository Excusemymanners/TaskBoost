using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskBoost
{
    // The "Guard" tab: choose what to watch and see what is happening right now
    class GuardPage : Panel
    {
        const string SettingsFile = "guard.txt";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // key (also the illustration), title, description
        static readonly string[][] Watchers =
        {
            new[] { "sensors", "Camera and mic", "Shows which apps are using them, or your location" },
            new[] { "network", "Network activity", "Programs online now; flags unsigned and new ones" },
            new[] { "keyboards", "Fake keyboards", "A new keyboard, or two connected at once" },
            new[] { "remote", "Remote and mirroring", "Remote Desktop, remote tools, extra screens" },
            new[] { "startup", "New startup programs", "Alerts when something adds itself to startup" },
            new[] { "shield", "Suspicious signs", "Disguised programs, proxy, hosts file, Defender off" }
        };
        static readonly string[][] Behaviour =
        {
            new[] { "notify", "Notifications", "Pop up a warning when an alert appears" },
            new[] { "tray", "Stay in the tray", "Keep watching after the window is closed" },
            new[] { "signin", "Start with Windows", "Start the guard hidden at every sign-in" },
            new[] { "buddy", "Desktop buddy", "A pixel spider that walks the screen edges" },
            new[] { "buddyhide", "Hide while I work", "Only on desktop, browsers, Explorer, terminals" },
            new[] { "buddyclaude", "Only with Claude", "Appears while Claude is connected (MCP)" }
        };

        readonly Dictionary<string, ToggleCard> cards = new Dictionary<string, ToggleCard>();
        readonly Label capWatch = Ui.Caption("WHAT TO WATCH"), capBehave = Ui.Caption("HOW IT BEHAVES"), capNow = Ui.Caption("RIGHT NOW");
        readonly Panel listPanel = new Panel();
        readonly FindingList list = new FindingList();
        readonly FlatButton btnKnown = new FlatButton(), btnAllKnown = new FlatButton(), btnEnd = new FlatButton(), btnLocate = new FlatButton();
        // The Ask Claude button sits in the window header so it is visible on every tab
        public FlatButton btnClaude = new FlatButton();
        readonly Timer timer = new Timer();
        readonly HashSet<string> notified = new HashSet<string>();
        string lastSignature;
        bool scanning, loading = true;

        // alerts, notices
        public event Action<int, int> StatusChanged;
        // a new red finding appeared
        public event Action<Finding> AlertRaised;

        public GuardPage()
        {
            BackColor = Theme.Bg;
            Controls.AddRange(new Control[] { capWatch, capBehave, capNow });

            var defaults = new List<string> { "notify" };
            foreach (string[] w in Watchers) defaults.Add(w[0]);
            var on = Settings.Load(SettingsFile, defaults);
            // Settings saved before the buddy existed: start with its switches on
            if (!on.Contains("v2")) on.AddRange(new[] { "buddy", "buddyhide" });
            // The buddy is now there all the time by default, not only while Claude is connected
            if (!on.Contains("v3")) on.Remove("buddyclaude");
            // Runs in the background by default: starts hidden at sign-in and stays in the tray when the window closes
            if (!on.Contains("v4"))
            {
                if (!on.Contains("tray")) on.Add("tray");
                SetSignIn(true);
                foreach (string marker in new[] { "v2", "v3", "v4" }) if (!on.Contains(marker)) on.Add(marker);
                Settings.Save(SettingsFile, on);
            }
            // The buddy no longer hides by default: it stays on screen whatever window is in front
            if (!on.Contains("v5"))
            {
                on.Remove("buddyhide");
                on.Add("v5");
                Settings.Save(SettingsFile, on);
            }
            foreach (string[][] set in new[] { Watchers, Behaviour })
                foreach (string[] o in set)
                {
                    var card = new ToggleCard(o[0], o[1], o[2]);
                    card.Checked = o[0] == "signin" ? SignInEnabled() : on.Contains(o[0]);
                    string key = o[0];
                    card.CheckedChanged += delegate { OptionChanged(key); };
                    cards[key] = card;
                    Controls.Add(card);
                }

            listPanel.BackColor = Theme.LogBg;
            listPanel.Padding = new Padding(Theme.Px(6));
            list.Dock = DockStyle.Fill;
            listPanel.Controls.Add(list);
            Controls.Add(listPanel);

            btnKnown.Text = "Mark as known";
            btnAllKnown.Text = "Mark all as known";
            btnEnd.Text = "End program";
            btnLocate.Text = "Open file location";
            btnKnown.Click += delegate { MarkKnown(false); };
            btnAllKnown.Click += delegate { MarkKnown(true); };
            btnEnd.Click += delegate { EndProgram(); };
            btnLocate.Click += delegate { Locate(); };
            Controls.AddRange(new Control[] { btnKnown, btnAllKnown, btnEnd, btnLocate });

            timer.Interval = 4000;
            timer.Tick += delegate { ScanNow(); };
            timer.Start();
            Disposed += delegate { timer.Dispose(); };
            loading = false;
        }

        public bool Option(string key) { return cards[key].Checked; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), capH = Theme.Px(22);
            int w = ClientSize.Width, y = 0;
            int cw = (w - 2 * gap) / 3, ch = Theme.Px(74);

            foreach (bool behaviour in new[] { false, true })
            {
                (behaviour ? capBehave : capWatch).Location = new Point(0, y);
                y += capH;
                string[][] set = behaviour ? Behaviour : Watchers;
                for (int i = 0; i < set.Length; i++)
                    cards[set[i][0]].SetBounds((i % 3) * (cw + gap), y + (i / 3) * (ch + gap), cw, ch);
                y += ((set.Length + 2) / 3) * (ch + gap) + Theme.Px(4);
            }

            capNow.Location = new Point(0, y);
            y += capH;
            int bh = Theme.Px(36);
            int by = ClientSize.Height - bh;
            listPanel.SetBounds(0, y, w, Math.Max(Theme.Px(60), by - Theme.Px(14) - y));
            btnKnown.SetBounds(0, by, Theme.Px(150), bh);
            btnAllKnown.SetBounds(btnKnown.Right + gap, by, Theme.Px(170), bh);
            btnEnd.SetBounds(btnAllKnown.Right + gap, by, Theme.Px(140), bh);
            btnLocate.SetBounds(btnEnd.Right + gap, by, Theme.Px(170), bh);
        }

        void OptionChanged(string key)
        {
            if (loading) return;
            if (key == "signin") SetSignIn(cards[key].Checked);
            var on = new List<string> { "v2", "v3", "v4", "v5" };
            foreach (var kv in cards) if (kv.Value.Checked && kv.Key != "signin") on.Add(kv.Key);
            Settings.Save(SettingsFile, on);
            ScanNow();
        }

        // "Start with Windows" is a per-user Run entry; it needs no administrator rights
        static bool SignInEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Guard.OwnRunName) != null;
        }

        static void SetSignIn(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Guard.OwnRunName, "\"" + App.ExePath + "\" --guard");
                else k.DeleteValue(Guard.OwnRunName, false);
            }
        }

        public void ScanNow()
        {
            if (scanning) return;
            scanning = true;
            var watchers = new List<string>();
            foreach (string[] w in Watchers) if (cards[w[0]].Checked) watchers.Add(w[0]);
            Task.Factory.StartNew(delegate { return Guard.Scan(watchers); }).ContinueWith(delegate(Task<List<Finding>> t)
            {
                scanning = false;
                if (t.Exception == null) Present(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Present(List<Finding> findings)
        {
            int alerts = 0, notices = 0;
            var signature = new StringBuilder();
            foreach (var f in findings)
            {
                if (f.Level == 2) alerts++;
                if (f.Level == 1) notices++;
                signature.AppendLine(f.Signature);
                if (f.Level == 2 && notified.Add(f.Key) && AlertRaised != null) AlertRaised(f);
            }
            if (StatusChanged != null) StatusChanged(alerts, notices);
            if (signature.ToString() == lastSignature) return; // nothing changed: leave the list and its scroll position alone
            lastSignature = signature.ToString();

            if (findings.Count == 0)
                findings.Add(new Finding { Key = "", Title = Lang.T("All quiet"), Detail = Lang.T("Nothing to report from the checks that are switched on") });
            var selected = list.SelectedItem as Finding;
            int top = list.TopIndex;
            bool first = list.Items.Count == 0;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var f in findings) list.Items.Add(f);
            if (selected != null)
                for (int i = 0; i < findings.Count; i++)
                    if (findings[i].Key == selected.Key) { list.SelectedIndex = i; break; }
            if (top < list.Items.Count) list.TopIndex = top;
            list.EndUpdate();
            if (first) list.Reveal(); // later refreshes just change the rows in place
        }

        void MarkKnown(bool all)
        {
            var keys = new List<string>();
            if (all)
            {
                foreach (Finding f in list.Items) if (f.Level > 0) keys.Add(f.Key);
            }
            else
            {
                var f = list.SelectedItem as Finding;
                if (f != null && !string.IsNullOrEmpty(f.Key)) keys.Add(f.Key);
            }
            if (keys.Count == 0) return;
            Guard.MarkKnown(keys);
            ScanNow();
        }

        void EndProgram()
        {
            var f = list.SelectedItem as Finding;
            if (f == null || f.Path == null)
            {
                Dialog.Show(this, "Select a row that belongs to a program first.");
                return;
            }
            if (!Dialog.Ask(this, Lang.T("Close every running copy of this program?") + "\n\n" + f.Path + "\n\n" + Lang.T("Unsaved work in it will be lost."))) return;
            int closed = 0;
            string error = null;
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    if (!string.Equals(Guard.ProcessPath(p.Id), f.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    try { p.Kill(); closed++; }
                    catch (Exception ex) { error = ex.Message; }
                }
            if (error != null) Dialog.Show(this, Lang.T("Could not close it: ") + error + "\n\n" + Lang.T("Programs running as administrator can only be closed by an administrator."));
            else if (closed == 0) Dialog.Show(this, "That program is no longer running.");
            ScanNow();
        }

        // Connects TaskBoost to Claude if it is not connected yet, then opens Claude already looking at this PC
        public void AskClaude()
        {
            if (btnClaude.Busy) return;
            btnClaude.Enabled = false;
            btnClaude.Busy = true;
            btnClaude.Text = "Connecting...";
            Task.Factory.StartNew(delegate
            {
                string status = ClaudeLink.EnsureRegistered();
                Log.Info(status);
                return ClaudeLink.ClaudeCodeInstalled() ? null : status;
            }).ContinueWith(delegate(Task<string> t)
            {
                btnClaude.Enabled = true;
                btnClaude.Busy = false;
                btnClaude.Text = "Ask Claude";
                if (t.Exception != null) Dialog.Show(this, Lang.T("Could not connect to Claude: ") + t.Exception.InnerException.Message);
                else if (t.Result != null)
                    Dialog.Show(this, t.Result + ".\n\n" + Lang.T("Claude Code is needed to start Claude from here. Install it from claude.com/claude-code, then press Ask Claude again."));
                else ClaudeLink.Launch();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Locate()
        {
            var f = list.SelectedItem as Finding;
            if (f != null && f.Path != null) Process.Start("explorer.exe", "/select,\"" + f.Path + "\"");
        }
    }
}

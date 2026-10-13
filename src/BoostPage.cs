using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskBoost
{
    // The "Boost" tab: pick tweaks, choose when they run, apply or restore
    class BoostPage : Panel
    {
        readonly Dictionary<string, ToggleCard> cards = new Dictionary<string, ToggleCard>();
        readonly Label capUser = Ui.Caption("EVERYDAY TWEAKS"), capAdmin = Ui.Caption("NEEDS ADMINISTRATOR APPROVAL"), capWhen = Ui.Caption("WHEN TO RUN");
        readonly OptionCard optManual = new OptionCard("manual", "Manual", "Only when I press Apply now, e.g. before a heavy task");
        readonly OptionCard optStartup = new OptionCard("auto", "Automatic", "Applies the switched-on items at every sign-in");
        readonly FlatButton btnApply = new FlatButton(), btnAll = new FlatButton(), btnRestore = new FlatButton(), btnFolder = new FlatButton();
        readonly Panel logPanel = new Panel();
        readonly TextBox logBox = new TextBox();
        bool loading = true;

        public BoostPage()
        {
            BackColor = Theme.Bg;
            Controls.AddRange(new Control[] { capUser, capAdmin, capWhen });

            var selected = Settings.Load();
            foreach (var g in Tweaks.Groups)
            {
                var card = new ToggleCard(g.Key, g.Title, g.Description);
                card.Checked = selected.Contains(g.Key);
                card.CheckedChanged += delegate { if (!loading) Settings.Save(SelectedKeys()); };
                cards[g.Key] = card;
                Controls.Add(card);
            }

            optManual.Click += delegate { SetStartup(false); };
            optStartup.Click += delegate { SetStartup(true); };
            Controls.Add(optManual);
            Controls.Add(optStartup);

            btnApply.Text = "Apply now";
            btnApply.Primary = true;
            btnRestore.Text = "Restore previous settings";
            btnFolder.Text = "Open log folder";
            btnAll.Text = "My full setup";
            // Everything on, then apply: the complete look and speed setup in one click, on any Windows 10 or 11 PC
            btnAll.Click += delegate
            {
                foreach (var card in cards.Values) card.Checked = true;
                ApplyNow();
            };
            btnApply.Click += delegate { ApplyNow(); };
            btnRestore.Click += delegate { Restore(); };
            btnFolder.Click += delegate { Process.Start(App.DataDir); };
            Controls.AddRange(new Control[] { btnApply, btnAll, btnRestore, btnFolder });

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.TabStop = false;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Dock = DockStyle.Fill;
            logBox.BackColor = Theme.LogBg;
            logBox.ForeColor = Theme.LogText;
            logBox.Font = Theme.Mono;
            logBox.Text = Lang.T("Ready. Switch on what you want and press Apply now.") + Environment.NewLine;
            logBox.HandleCreated += delegate { Native.SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null); }; // dark scrollbar
            logPanel.BackColor = Theme.LogBg;
            logPanel.Padding = new Padding(Theme.Px(10));
            logPanel.Controls.Add(logBox);
            Controls.Add(logPanel);

            Log.Line += OnLogLine;
            Disposed += delegate { Log.Line -= OnLogLine; };
            RefreshStartup();
            loading = false;
        }

        public Control DefaultControl { get { return btnApply; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), capH = Theme.Px(22);
            int w = ClientSize.Width, y = 0;

            // Three cards per row: everyday tweaks first, administrator ones under their own caption
            int cw = (w - 2 * gap) / 3, ch = Theme.Px(74);
            foreach (bool admin in new[] { false, true })
            {
                (admin ? capAdmin : capUser).Location = new Point(0, y);
                y += capH;
                int i = 0;
                foreach (var g in Tweaks.Groups)
                {
                    if (g.NeedsAdmin != admin) continue;
                    cards[g.Key].SetBounds((i % 3) * (cw + gap), y + (i / 3) * (ch + gap), cw, ch);
                    i++;
                }
                y += ((i + 2) / 3) * (ch + gap) + Theme.Px(4);
            }

            capWhen.Location = new Point(0, y);
            y += capH;
            int ow = (w - gap) / 2, oh = Theme.Px(66);
            optManual.SetBounds(0, y, ow, oh);
            optStartup.SetBounds(ow + gap, y, w - ow - gap, oh);
            y += oh + Theme.Px(16);

            int bh = Theme.Px(36);
            btnApply.SetBounds(0, y, Theme.Px(140), bh);
            btnAll.SetBounds(btnApply.Right + gap, y, Theme.Px(150), bh);
            btnRestore.SetBounds(btnAll.Right + gap, y, Theme.Px(210), bh);
            btnFolder.SetBounds(btnRestore.Right + gap, y, Theme.Px(150), bh);
            y += bh + Theme.Px(14);

            logPanel.SetBounds(0, y, w, Math.Max(Theme.Px(60), ClientSize.Height - y));
        }

        List<string> SelectedKeys()
        {
            var keys = new List<string>();
            foreach (var g in Tweaks.Groups) if (cards[g.Key].Checked) keys.Add(g.Key);
            return keys;
        }

        // The selection always mirrors whether the sign-in task really exists
        void RefreshStartup()
        {
            bool on = Startup.IsEnabled();
            optStartup.Selected = on;
            optManual.Selected = !on;
        }

        void OnLogLine(string line)
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(OnLogLine), line); return; }
            logBox.AppendText(line + Environment.NewLine);
        }

        void SetBusy(bool busy)
        {
            foreach (Control c in Controls)
                if (c is DarkControl) c.Enabled = !busy;
            btnApply.Text = busy ? "Working..." : "Apply now";
            btnApply.Busy = busy;
        }

        // Runs work off the UI thread with the controls disabled
        void RunBusy(Action work)
        {
            SetBusy(true);
            Task.Factory.StartNew(work).ContinueWith(delegate(Task t)
            {
                if (t.Exception != null) Log.Error(t.Exception.InnerException.Message);
                RefreshStartup();
                SetBusy(false);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ApplyNow()
        {
            var keys = SelectedKeys();
            Settings.Save(keys);
            string system = string.Join(",", keys.FindAll(delegate(string k) { return Array.Find(Tweaks.Groups, delegate(Group g) { return g.Key == k; }).NeedsAdmin; }));
            RunBusy(delegate
            {
                Tweaks.Apply(keys, false);
                if (system.Length > 0) Elevate("apply --groups " + system, delegate { Tweaks.Apply(keys, true); });
                Log.Info("Finished. Some changes only show after you sign out and back in, or restart.");
            });
        }

        void Restore()
        {
            var entries = State.Load();
            if (entries.Count == 0)
            {
                Dialog.Show(this, "Nothing to restore: TaskBoost has not changed anything on this PC yet.");
                return;
            }
            if (!Dialog.Ask(this, "Put every setting back to the value it had before TaskBoost first changed it?")) return;
            bool system = entries.Exists(State.IsSystem);
            RunBusy(delegate
            {
                Tweaks.RestoreUser();
                if (system) Elevate("restore", Tweaks.RestoreSystem);
                Log.Info("Finished. Sign out and back in, or restart, to see every change.");
            });
        }

        void SetStartup(bool on)
        {
            if (on == Startup.IsEnabled()) { RefreshStartup(); return; }
            string user = WindowsIdentity.GetCurrent().Name;
            RunBusy(delegate
            {
                if (on) Elevate("startup-on --user \"" + user + "\"", delegate { Startup.Install(user); });
                else Elevate("startup-off", delegate { Startup.Remove(); });
            });
        }

        // Runs admin work in-process when already elevated, otherwise through an elevated copy of this exe
        void Elevate(string command, Action inProcess)
        {
            if (App.IsElevated) { inProcess(); return; }
            long offset = File.Exists(App.LogFile) ? new FileInfo(App.LogFile).Length : 0;
            var psi = new ProcessStartInfo(App.ExePath, "--elevated " + command + " --data \"" + App.DataDir + "\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try
            {
                using (var p = Process.Start(psi)) p.WaitForExit();
            }
            catch (Win32Exception)
            {
                Log.Info("Administrator approval was declined: the administrator part was skipped.");
                return;
            }
            // Show what the elevated copy wrote to the shared log
            using (var fs = new FileStream(App.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                string text = new StreamReader(fs).ReadToEnd();
                if (text.Length > 0) BeginInvoke(new Action(delegate { logBox.AppendText(text); }));
            }
        }
    }
}

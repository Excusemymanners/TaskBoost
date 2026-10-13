using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskBoost
{
    // The "Care" tab: routine maintenance with Windows' own tools
    class CarePage : Panel
    {
        const string SettingsFile = "care.txt";

        // key, title, description, illustration
        static readonly string[][] Tasks =
        {
            new[] { "health", "Health report", "Disks, free space, battery wear, uptime", "pulse" },
            new[] { "chkdsk", "Check the disk", "Scans the system drive for errors", "disk" },
            new[] { "optimize", "Optimise drives", "TRIM on SSDs, defrag on hard disks", "recycle" },
            new[] { "defender", "Quick virus scan", "Windows Defender quick scan, a few minutes", "shield" },
            new[] { "sfc", "Check system files", "Repairs damaged Windows files, 10 to 20 minutes", "reports" },
            new[] { "dism", "Windows image", "Checks and tidies the component store, slow", "background" }
        };

        readonly Dictionary<string, ToggleCard> cards = new Dictionary<string, ToggleCard>();
        readonly Label caption = Ui.Caption("MAINTENANCE"), hint = new Label();
        readonly FlatButton btnRun = new FlatButton(), btnCheck = new FlatButton(), btnInstall = new FlatButton(), btnMaker = new FlatButton();
        readonly Label capUpdates = Ui.Caption("UPDATES FOR THIS DEVICE");
        readonly ToggleCard stopCard = new ToggleCard("shield", "Stop updates", "Windows stops updating itself until you switch this off");
        bool syncing;
        readonly Panel logPanel = new Panel();
        readonly TextBox logBox = new TextBox();
        readonly Timer tail = new Timer();
        long offset;
        bool loading = true;

        public CarePage()
        {
            BackColor = Theme.Bg;
            Controls.Add(caption);

            var on = Settings.Load(SettingsFile, new[] { "health", "chkdsk", "optimize", "defender" });
            foreach (string[] t in Tasks)
            {
                var card = new ToggleCard(t[3], t[1], t[2]);
                card.Checked = on.Contains(t[0]);
                card.CheckedChanged += delegate { if (!loading) Settings.Save(SettingsFile, Selected()); };
                cards[t[0]] = card;
                Controls.Add(card);
            }

            btnRun.Text = "Run maintenance";
            btnRun.Primary = true;
            btnRun.Click += delegate { RunNow(); };
            Controls.Add(btnRun);

            hint.Text = Lang.T("The PC stays usable while this runs. Nothing here deletes your files.");
            hint.AutoSize = true;
            hint.Font = Theme.Body;
            hint.ForeColor = Theme.Muted;
            hint.BackColor = Theme.Bg;
            Controls.Add(hint);

            Controls.Add(capUpdates);
            stopCard.CheckedChanged += delegate { StopChanged(); };
            Controls.Add(stopCard);
            btnCheck.Text = "Check for updates";
            btnInstall.Text = "Install updates";
            btnMaker.Text = "Maker's driver page";
            btnCheck.Click += delegate { RunHelper("updates-check", btnCheck); };
            btnInstall.Click += delegate { RunHelper("updates-install", btnInstall); };
            btnMaker.Click += delegate { System.Diagnostics.Process.Start(Updates.MakerPage()); };
            Controls.AddRange(new Control[] { btnCheck, btnInstall, btnMaker });
            RefreshStop();

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.TabStop = false;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Dock = DockStyle.Fill;
            logBox.BackColor = Theme.LogBg;
            logBox.ForeColor = Theme.LogText;
            logBox.Font = Theme.Mono;
            logBox.Text = Lang.T("Pick what to run. A monthly run of the first four is plenty for most PCs.") + Environment.NewLine;
            logBox.HandleCreated += delegate { Native.SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null); };
            logPanel.BackColor = Theme.LogBg;
            logPanel.Padding = new Padding(Theme.Px(10));
            logPanel.Controls.Add(logBox);
            Controls.Add(logPanel);

            // The work runs in a helper copy of the exe; show its log lines as they arrive
            tail.Interval = 1000;
            tail.Tick += delegate { ReadNew(); };
            Disposed += delegate { tail.Dispose(); };
            loading = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), bh = Theme.Px(36);
            int w = ClientSize.Width, y = 0;
            int cw = (w - 2 * gap) / 3, ch = Theme.Px(74);
            caption.Location = new Point(0, y);
            y += Theme.Px(22);
            for (int i = 0; i < Tasks.Length; i++) cards[Tasks[i][0]].SetBounds((i % 3) * (cw + gap), y + (i / 3) * (ch + gap), cw, ch);
            y += 2 * (ch + gap) + Theme.Px(4);
            btnRun.SetBounds(0, y, Theme.Px(180), bh);
            hint.Location = new Point(btnRun.Right + Theme.Px(14), y + Theme.Px(10));
            y += bh + Theme.Px(14);

            capUpdates.Location = new Point(0, y);
            y += Theme.Px(22);
            stopCard.SetBounds(0, y, cw, ch);
            int by = y + (ch - bh) / 2;
            btnCheck.SetBounds(cw + gap, by, Theme.Px(170), bh);
            btnInstall.SetBounds(btnCheck.Right + gap, by, Theme.Px(150), bh);
            btnMaker.SetBounds(btnInstall.Right + gap, by, Theme.Px(180), bh);
            y += ch + Theme.Px(14);
            logPanel.SetBounds(0, y, w, Math.Max(Theme.Px(60), ClientSize.Height - y));
        }

        List<string> Selected()
        {
            var keys = new List<string>();
            foreach (string[] t in Tasks) if (cards[t[0]].Checked) keys.Add(t[0]);
            return keys;
        }

        void RunNow()
        {
            var items = Selected();
            if (items.Count > 0) RunHelper("care --items " + string.Join(",", items), btnRun);
        }

        // The switch always shows what Windows is really set to
        void RefreshStop()
        {
            syncing = true;
            stopCard.Checked = Updates.Stopped;
            syncing = false;
        }

        void StopChanged()
        {
            if (syncing) return;
            if (stopCard.Checked && !Dialog.Ask(this, "Windows will stop installing updates by itself, including security fixes.\n\nYou can still check and install by hand from here. Stop automatic updates?"))
            {
                RefreshStop();
                return;
            }
            RunHelper(stopCard.Checked ? "wu-stop" : "wu-allow", btnCheck);
        }

        // Runs a command in the helper copy of the exe and shows its log lines as they arrive
        void RunHelper(string command, FlatButton source)
        {
            SetBusy(true);
            source.Busy = true;
            offset = File.Exists(App.LogFile) ? new FileInfo(App.LogFile).Length : 0;
            tail.Start();
            Task.Factory.StartNew(delegate { Helper.Run(command); }).ContinueWith(delegate(Task t)
            {
                tail.Stop();
                ReadNew();
                if (t.Exception != null) logBox.AppendText(t.Exception.InnerException.Message + Environment.NewLine);
                SetBusy(false);
                source.Busy = false;
                RefreshStop();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetBusy(bool busy)
        {
            foreach (Control c in Controls)
                if (c is DarkControl) c.Enabled = !busy;
            btnRun.Text = busy ? "Working..." : "Run maintenance";
        }

        void ReadNew()
        {
            if (!File.Exists(App.LogFile)) return;
            using (var fs = new FileStream(App.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length <= offset) return;
                fs.Seek(offset, SeekOrigin.Begin);
                string text = new StreamReader(fs).ReadToEnd();
                offset = fs.Length;
                logBox.AppendText(text);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskBoost
{
    // The "Turbo" tab: gaming mode, memory, and a clean-up that only touches temporary folders
    class TurboPage : Panel
    {
        const string SettingsFile = "turbo.txt", GamesFile = "games.txt";
        const string GpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

        // key, title, description, illustration
        static readonly string[][] GameOptions =
        {
            new[] { "apps", "Close extras", "Sync tools, updaters and phone link", "background" },
            new[] { "browsers", "Close browsers", "Browsers and chat apps; open pages are lost", "autostart" },
            new[] { "services", "Pause services", "Updates, indexing and telemetry, until you leave", "services" }
        };
        static readonly string[][] CleanOptions =
        {
            new[] { "temp", "Temporary files", "Your temp folder, files older than a week", "trash" },
            new[] { "wintemp", "Windows temp files", "The Windows temp folder, older than a week", "trash" },
            new[] { "reports", "Crash reports", "Old error reports and crash dumps", "reports" },
            new[] { "dns", "Network buffers", "Flushes the DNS cache", "network" },
            new[] { "clipboard", "Clipboard", "Empties whatever was copied last", "clipboard" },
            new[] { "recycle", "Recycle Bin", "Empties it for good, so it is off by default", "recycle" }
        };

        readonly Dictionary<string, ToggleCard> cards = new Dictionary<string, ToggleCard>();
        readonly Label capMode = Ui.Caption("LAPTOP MODE"), capGame = Ui.Caption("GAMING MODE"), capGames = Ui.Caption("MY GAMES  (get the fast graphics card and high priority)"), capClean = Ui.Caption("SAFE CLEAN-UP");
        readonly Panel gamesPanel = new Panel(), logPanel = new Panel();
        readonly ListBox games = new ListBox();
        readonly TextBox logBox = new TextBox();
        readonly FlatButton btnGame = new FlatButton(), btnAdd = new FlatButton(), btnRemove = new FlatButton(), btnMemory = new FlatButton(), btnCharge = new FlatButton(), btnPreview = new FlatButton(), btnClean = new FlatButton();
        readonly Timer priorityTimer = new Timer();
        bool loading = true;

        public event Action<bool> GameModeChanged;
        // The same five modes the buddy offers on a left click
        public event Action<string> ModeRequested;
        static readonly string[][] ModeNames =
        {
            new[] { "low", "Low power" }, new[] { "normal", "Normal" }, new[] { "work", "Study / work" },
            new[] { "best", "Best performance" }, new[] { "turbo", "Gaming" }
        };
        readonly Dictionary<string, FlatButton> modeButtons = new Dictionary<string, FlatButton>();

        public TurboPage()
        {
            BackColor = Theme.Bg;
            Controls.AddRange(new Control[] { capMode, capGame, capGames, capClean });
            foreach (string[] m in ModeNames)
            {
                string key = m[0];
                var button = new FlatButton();
                button.Text = m[1];
                button.Click += delegate { if (ModeRequested != null) ModeRequested(key); };
                modeButtons[key] = button;
                Controls.Add(button);
            }

            var on = Settings.Load(SettingsFile, new[] { "apps", "services", "temp", "wintemp", "reports", "dns" });
            foreach (string[][] set in new[] { GameOptions, CleanOptions })
                foreach (string[] o in set)
                {
                    var card = new ToggleCard(o[3], o[1], o[2]);
                    card.Checked = on.Contains(o[0]);
                    card.CheckedChanged += delegate { if (!loading) Settings.Save(SettingsFile, Selected(GameOptions, CleanOptions)); };
                    cards[o[0]] = card;
                    Controls.Add(card);
                }

            games.BorderStyle = BorderStyle.None;
            games.IntegralHeight = false;
            games.BackColor = Theme.LogBg;
            games.ForeColor = Theme.LogText;
            games.Font = Theme.Body;
            games.Dock = DockStyle.Fill;
            games.HandleCreated += delegate { Native.SetWindowTheme(games.Handle, "DarkMode_Explorer", null); };
            string gamesPath = Path.Combine(App.DataDir, GamesFile);
            if (File.Exists(gamesPath)) foreach (string g in File.ReadAllLines(gamesPath)) if (g.Length > 0) games.Items.Add(g);
            gamesPanel.BackColor = Theme.LogBg;
            gamesPanel.Padding = new Padding(Theme.Px(8));
            gamesPanel.Controls.Add(games);
            Controls.Add(gamesPanel);

            btnGame.Primary = true;
            btnAdd.Text = "Add a game";
            btnRemove.Text = "Remove game";
            btnMemory.Text = "Free memory now";
            btnPreview.Text = "Preview clean-up";
            btnClean.Text = "Clean now";
            btnGame.Click += delegate { ToggleGameMode(); };
            btnAdd.Click += delegate { AddGame(); };
            btnRemove.Click += delegate { RemoveGame(); };
            btnMemory.Click += delegate { RunBusy("free-memory", btnMemory); };
            // Keeps the USB ports powered (no sleep, lid does nothing) so a phone keeps charging
            btnCharge.Click += delegate { ToggleCharge(); };
            btnPreview.Click += delegate { RunBusy("clean-preview --items " + string.Join(",", Selected(CleanOptions)), btnPreview); };
            btnClean.Click += delegate { Clean(); };
            Controls.AddRange(new Control[] { btnGame, btnAdd, btnRemove, btnMemory, btnCharge, btnPreview, btnClean });

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.TabStop = false;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Dock = DockStyle.Fill;
            logBox.BackColor = Theme.LogBg;
            logBox.ForeColor = Theme.LogText;
            logBox.Font = Theme.Mono;
            logBox.Text = Lang.T("Gaming mode changes are temporary: leaving it puts everything back.") + Environment.NewLine;
            logBox.HandleCreated += delegate { Native.SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null); };
            logPanel.BackColor = Theme.LogBg;
            logPanel.Padding = new Padding(Theme.Px(10));
            logPanel.Controls.Add(logBox);
            Controls.Add(logPanel);

            // While gaming mode is on, keep the listed games at high priority as they start
            priorityTimer.Interval = 5000;
            priorityTimer.Tick += delegate { BoostGames(); };
            priorityTimer.Start();
            Disposed += delegate { priorityTimer.Dispose(); };
            RefreshGameButton();
            loading = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), capH = Theme.Px(22), bh = Theme.Px(36);
            int w = ClientSize.Width, y = 0;
            int cw = (w - 2 * gap) / 3, ch = Theme.Px(74);

            capMode.Location = new Point(0, y);
            y += capH;
            int mw = (w - 4 * gap) / 5;
            for (int i = 0; i < ModeNames.Length; i++) modeButtons[ModeNames[i][0]].SetBounds(i * (mw + gap), y, mw, bh);
            y += bh + Theme.Px(12);

            capGame.Location = new Point(0, y);
            y += capH;
            for (int i = 0; i < GameOptions.Length; i++) cards[GameOptions[i][0]].SetBounds(i * (cw + gap), y, cw, ch);
            y += ch + gap + Theme.Px(2);

            capGames.Location = new Point(0, y);
            y += capH;
            gamesPanel.SetBounds(0, y, w, Theme.Px(58));
            y = gamesPanel.Bottom + gap;
            btnGame.SetBounds(0, y, Theme.Px(200), bh);
            btnAdd.SetBounds(btnGame.Right + gap, y, Theme.Px(120), bh);
            btnRemove.SetBounds(btnAdd.Right + gap, y, Theme.Px(140), bh);
            btnMemory.SetBounds(btnRemove.Right + gap, y, Theme.Px(170), bh);
            btnCharge.SetBounds(btnMemory.Right + gap, y, Theme.Px(200), bh);
            y += bh + Theme.Px(14);

            capClean.Location = new Point(0, y);
            y += capH;
            for (int i = 0; i < CleanOptions.Length; i++) cards[CleanOptions[i][0]].SetBounds((i % 3) * (cw + gap), y + (i / 3) * (ch + gap), cw, ch);
            y += 2 * (ch + gap) + Theme.Px(2);
            btnPreview.SetBounds(0, y, Theme.Px(170), bh);
            btnClean.SetBounds(btnPreview.Right + gap, y, Theme.Px(130), bh);
            y += bh + Theme.Px(14);

            logPanel.SetBounds(0, y, w, Math.Max(Theme.Px(50), ClientSize.Height - y));
        }

        List<string> Selected(params string[][][] sets)
        {
            var keys = new List<string>();
            foreach (string[][] set in sets)
                foreach (string[] o in set) if (cards[o[0]].Checked) keys.Add(o[0]);
            return keys;
        }

        public void RefreshGameButton()
        {
            bool on = GameMode.IsOn;
            btnGame.Text = on ? "Leave gaming mode" : "Enter gaming mode";
            string current = LaptopMode.Effective;
            foreach (var kv in modeButtons)
            {
                kv.Value.Primary = kv.Key == current;
                kv.Value.Invalidate();
            }
            bool charging = ChargeMode.IsOn;
            btnCharge.Text = charging ? "Stop phone charging" : "Charge my phone";
            btnCharge.Primary = charging;
            btnCharge.Invalidate();
            if (GameModeChanged != null) GameModeChanged(on);
        }

        // The work runs in a helper copy of the exe (elevated if you approve), so the window stays responsive
        void RunBusy(string command, FlatButton source)
        {
            foreach (Control c in Controls)
                if (c is DarkControl) c.Enabled = false;
            source.Busy = true;
            Task.Factory.StartNew(delegate { return Helper.Run(command); }).ContinueWith(delegate(Task<string> t)
            {
                logBox.AppendText(t.Exception != null ? t.Exception.InnerException.Message + Environment.NewLine : t.Result);
                foreach (Control c in Controls)
                    if (c is DarkControl) c.Enabled = true;
                source.Busy = false;
                RefreshGameButton();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Also reachable from the global shortcuts
        public void ToggleCharge() { if (btnCharge.Enabled) RunBusy(ChargeMode.IsOn ? "charge-off" : "charge-on", btnCharge); }
        public void FreeMemory() { if (btnMemory.Enabled) RunBusy("free-memory", btnMemory); }

        public void ToggleGameMode()
        {
            if (!btnGame.Enabled) return;
            if (GameMode.IsOn) { RunBusy("game-off", btnGame); return; }
            if (cards["browsers"].Checked && !Dialog.Ask(this, "Browsers and chat apps will be closed without asking them to save.\n\nContinue?")) return;
            RunBusy("game-on --options " + string.Join(",", Selected(GameOptions)), btnGame);
        }

        void Clean()
        {
            var items = Selected(CleanOptions);
            if (items.Count == 0) return;
            if (items.Contains("recycle") && !Dialog.Ask(this, "The Recycle Bin will be emptied for good.\n\nContinue?")) return;
            RunBusy("clean --items " + string.Join(",", items), btnClean);
        }

        void AddGame()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("Choose the game's program file");
                dialog.Filter = "Programs (*.exe)|*.exe";
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK || games.Items.Contains(dialog.FileName)) return;
                games.Items.Add(dialog.FileName);
                // Tell Windows to run it on the high-performance graphics card
                using (var k = Registry.CurrentUser.CreateSubKey(GpuKey)) k.SetValue(dialog.FileName, "GpuPreference=2;");
                SaveGames();
            }
        }

        void RemoveGame()
        {
            string path = games.SelectedItem as string;
            if (path == null) return;
            games.Items.Remove(path);
            using (var k = Registry.CurrentUser.OpenSubKey(GpuKey, true))
                if (k != null) k.DeleteValue(path, false);
            SaveGames();
        }

        void SaveGames()
        {
            var lines = new List<string>();
            foreach (string g in games.Items) lines.Add(g);
            File.WriteAllLines(Path.Combine(App.DataDir, GamesFile), lines);
        }

        void BoostGames()
        {
            if (games.Items.Count == 0 || !GameMode.IsOn) return;
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    try
                    {
                        string path = Guard.ProcessPath(p.Id);
                        if (path == null || games.FindStringExact(path) < 0 || p.PriorityClass == ProcessPriorityClass.High) continue;
                        p.PriorityClass = ProcessPriorityClass.High;
                        logBox.AppendText("High priority: " + Path.GetFileName(path) + Environment.NewLine);
                    }
                    catch (Exception) { }
                }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskBoost
{
    // One number for the whole PC, and what it is made of
    static class HealthScore
    {
        public class Part
        {
            public string Name, Why;
            public int Points, Max;
            public int Level { get { return Points * 2 < Max ? 2 : Points < Max ? 1 : 0; } }
        }

        public static string Tier(int score) { return score >= 90 ? "Excellent" : score >= 75 ? "Good" : score >= 50 ? "Needs attention" : "In trouble"; }

        static string LastFile { get { return Path.Combine(App.DataDir, "score.txt"); } }

        public static int Last
        {
            get
            {
                int v;
                try { return File.Exists(LastFile) && int.TryParse(File.ReadAllText(LastFile).Trim(), out v) ? v : -1; }
                catch (IOException) { return -1; }
            }
        }

        // Slow: scans the guard, the startup list and the temp folders. Call it off the UI thread.
        public static List<Part> Compute()
        {
            var parts = new List<Part>();

            var findings = Guard.Scan(new[] { "sensors", "network", "keyboards", "remote", "startup", "shield" });
            int alerts = 0, notices = 0;
            bool defenderOff = false, firewallOff = false;
            foreach (var f in findings)
            {
                if (f.Key != null && f.Key.StartsWith("shield|defender")) { defenderOff = true; continue; }
                if (f.Key != null && f.Key.StartsWith("shield|firewall")) { firewallOff = true; continue; }
                if (f.Level == 2) alerts++;
                if (f.Level == 1) notices++;
            }
            parts.Add(new Part { Name = "Security", Max = 25, Points = Math.Max(0, 25 - alerts * 10 - notices * 3),
                Why = alerts + notices == 0 ? Lang.T("Nothing suspicious on the Guard tab") : alerts + Lang.T(" alert(s) and ") + notices + Lang.T(" notice(s) on the Guard tab") });

            int suspicious = 0, check = 0;
            foreach (var f in StartupInfo.Scan())
            {
                if (f.Level == 2) suspicious++;
                if (f.Level == 1) check++;
            }
            parts.Add(new Part { Name = "Startup programs", Max = 20, Points = Math.Max(0, 20 - suspicious * 10 - check * 3),
                Why = suspicious + check == 0 ? Lang.T("Everything that starts with Windows looks fine") : suspicious + Lang.T(" suspicious and ") + check + Lang.T(" to check on the Startup tab") });

            double worstFree = 100;
            string drive = "";
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                double free = d.AvailableFreeSpace * 100.0 / d.TotalSize;
                if (free < worstFree) { worstFree = free; drive = d.Name.TrimEnd('\\'); }
            }
            parts.Add(new Part { Name = "Disk space", Max = 15, Points = worstFree >= 20 ? 15 : worstFree >= 10 ? 8 : 0,
                Why = drive + Lang.T(" has ") + worstFree.ToString("0") + Lang.T("% free") + (worstFree < 10 ? Lang.T(": free some space") : "") });

            long total = Memory.TotalMb(), avail = Memory.AvailableMb();
            double freePct = total > 0 ? avail * 100.0 / total : 100;
            parts.Add(new Part { Name = "Memory", Max = 10, Points = freePct >= 25 ? 10 : freePct >= 10 ? 5 : 0,
                Why = avail + Lang.T(" MB of ") + total + Lang.T(" MB free right now") });

            parts.Add(new Part { Name = "Protection", Max = 15, Points = defenderOff ? 0 : firewallOff ? 8 : 15,
                Why = Lang.T(defenderOff ? "Windows Defender real-time protection is off" : firewallOff ? "Windows Firewall is off" : "Defender and the firewall are on") });

            double junkMb = 0;
            string text = Mcp.Capture(delegate { Cleaner.Run(new[] { "temp", "wintemp", "reports" }, true); });
            var m = System.Text.RegularExpressions.Regex.Match(text, @"would free about ([\d\.]+) MB");
            if (m.Success) double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out junkMb);
            parts.Add(new Part { Name = "Clean-up", Max = 10, Points = junkMb < 500 ? 10 : junkMb < 2000 ? 5 : 0,
                Why = junkMb.ToString("0") + Lang.T(" MB of old temporary files (Turbo tab, Clean now)") });

            bool stopped = Updates.Stopped;
            parts.Add(new Part { Name = "Updates", Max = 5, Points = stopped ? 0 : 5,
                Why = Lang.T(stopped ? "Automatic Windows updates are stopped (Care tab)" : "Windows updates itself") });

            int score = 0;
            foreach (var p in parts) score += p.Points;
            try { File.WriteAllText(LastFile, score.ToString()); }
            catch (IOException) { }
            return parts;
        }
    }

    // The big number: a ring that fills up to the score while the number counts up, and the buddy beside it,
    // wearing what the score has earned
    class ScorePanel : Control
    {
        static readonly Font Big = new Font("Segoe UI Semibold", 22F);

        readonly Anim shown;
        readonly Timer life = new Timer();
        readonly Bitmap sprite = new Bitmap(BuddySprite.Grid, BuddySprite.Grid);
        int score = -1, frame, countIn;
        float turn;
        bool working;
        string tier = "Calculating...";

        public ScorePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            shown = new Anim(0F, Invalidate);
            // Moves the buddy, and the arc that runs round the ring while the score is being worked out
            life.Interval = 40;
            life.Tick += delegate
            {
                turn = (turn + 9F) % 360F;
                if (countIn > 0 && --countIn == 0) shown.To(score, 950);
                if (++frame % 8 == 0 || working) Invalidate();
            };
            Disposed += delegate { life.Dispose(); sprite.Dispose(); };
        }

        public void Set(int newScore, string newTier)
        {
            score = newScore;
            tier = newTier;
            Count();
            Invalidate();
        }

        public bool Working
        {
            get { return working; }
            set { working = value; Invalidate(); }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            life.Enabled = Visible && Motion.On;
            Count();
        }

        // The ring fills from empty each time the score is shown anew. It waits a moment first, so the
        // filling is not lost behind the tab sliding in.
        void Count()
        {
            if (score < 0 || !Visible || !Motion.On)
            {
                countIn = 0;
                shown.Jump(Math.Max(0, score));
                return;
            }
            shown.Jump(0F);
            countIn = 7;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            var card = new RectangleF(0.5F, 0.5F, Width - 1, Height - 1);
            Theme.FillRound(g, card, Theme.Px(12), Theme.Card);
            Theme.DrawRound(g, card, Theme.Px(12), Theme.Border, 1F);

            // Ring: a gauge that fills with the score, coloured by how good it is
            float thick = Theme.Px(8), value = Math.Max(0F, Math.Min(100F, shown.Value));
            int d = Height - Theme.Px(30);
            var ring = new RectangleF(Theme.Px(19), Theme.Px(15), d, d);
            Color c = score < 0 ? Theme.Track : score >= 75 ? Theme.Accent : score >= 50 ? Theme.Notice : Theme.Alert;
            using (var track = new Pen(Theme.Mix(Theme.Card, Theme.Track, 0.75F), thick)) g.DrawEllipse(track, ring);
            if (value > 0.5F)
            {
                float sweep = 360F * value / 100F;
                // A soft light round the arc, then the arc itself, brighter towards its start
                for (int k = 2; k >= 1; k--)
                    using (var halo = new Pen(Color.FromArgb(k == 1 ? 34 : 16, c), thick + k * Theme.Px(5)))
                    {
                        halo.StartCap = halo.EndCap = LineCap.Round;
                        g.DrawArc(halo, ring, -90F, sweep);
                    }
                using (var brush = new LinearGradientBrush(RectangleF.Inflate(ring, thick, thick), c == Theme.Accent ? Theme.Accent2 : Theme.Mix(c, Color.White, 0.3F), c, 60F))
                using (var pen = new Pen(brush, thick))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ring, -90F, sweep);
                }
            }
            if (working)
                using (var pen = new Pen(Color.FromArgb(210, Theme.Accent2), Theme.Px(3)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, RectangleF.Inflate(ring, -thick, -thick), turn, 70F);
                }
            TextRenderer.DrawText(g, score < 0 ? "..." : ((int)Math.Round(value)).ToString(), Big, Rectangle.Round(ring), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // The buddy on the right: big pixels, moving, dressed by the score
            int unit = Math.Max(2, (Height - Theme.Px(22)) / BuddySprite.Grid), size = unit * BuddySprite.Grid;
            var seat = new Rectangle(Width - size - Theme.Px(22), (Height - size) / 2, size, size);
            string mood = working ? "think" : score < 0 ? "" : score >= 90 ? "star" : score >= 75 ? "happy" : score >= 50 ? "think" : "cry";
            bool badge = BuddySprite.NetBadge;
            BuddySprite.NetBadge = false;
            BuddySprite.Draw(sprite, Motion.On ? (frame / 8) % 2 : 0, false, 0, mood, false, "");
            BuddySprite.NetBadge = badge;
            sprite.MakeTransparent(BuddySprite.Key);
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(sprite, seat);
            g.Restore(st);

            int x = (int)ring.Right + Theme.Px(22), w = seat.Left - x - Theme.Px(10);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Lang.T(tier), Theme.Header, new Rectangle(x, Height / 2 - Theme.Px(30), w, Theme.Px(36)), c == Theme.Track ? Theme.Muted : c, flags);
            string hint = score < 0 ? "" : score >= 90 ? Lang.T("The buddy wears a crown while the PC stays this healthy.") : score >= 75 ? Lang.T("Nearly there: see the rows below for the last few points.") : Lang.T("The rows below say where the points went.");
            TextRenderer.DrawText(g, hint, Theme.Body, new Rectangle(x, Height / 2 + Theme.Px(8), w, Theme.Px(18)), Theme.Muted, flags);
        }
    }

    // One live reading on the Home tab: an icon, what it measures, the value in words, and a gauge that glides
    class StatTile : Control
    {
        readonly string icon, title;
        readonly Anim fill;
        string value = "";
        int level;
        bool gauge = true;

        public StatTile(string icon, string title)
        {
            this.icon = icon;
            this.title = Lang.T(title);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            fill = new Anim(0F, Invalidate);
        }

        // amount: how full the gauge is, 0 to 1, or negative for no gauge. warn: 0 fine, 1 notice, 2 alert
        public void Set(string text, float amount, int warn)
        {
            gauge = amount >= 0F;
            float target = Math.Max(0F, Math.Min(1F, amount));
            if (text == value && warn == level && fill.Target == target) return;
            value = text;
            level = warn;
            if (IsHandleCreated && Visible) fill.To(target, 700);
            else fill.Jump(target);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            var card = new RectangleF(0.5F, 0.5F, Width - 1, Height - 1);
            Theme.FillRound(g, card, Theme.Px(10), Theme.Card);
            Theme.DrawRound(g, card, Theme.Px(10), Theme.Border, 1F);

            int pad = Theme.Px(11), tile = Theme.Px(36);
            var tileRect = new RectangleF(pad, (Height - tile) / 2F, tile, tile);
            Color mark = Theme.LevelColor(level, Theme.Accent);
            Theme.FillRound(g, tileRect, Theme.Px(9), Theme.Tile);
            float inset = Theme.Px(8);
            Icons.Draw(g, icon, RectangleF.Inflate(tileRect, -inset, -inset), mark, Theme.Tile);

            int x = pad + tile + Theme.Px(11), w = Width - x - pad;
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, title, Theme.Body, new Rectangle(x, Theme.Px(7), w, Theme.Px(16)), Theme.Muted, flags);
            TextRenderer.DrawText(g, value, Theme.Caption, new Rectangle(x, Theme.Px(23), w, Theme.Px(17)), Theme.Text, flags);
            if (!gauge) return;
            var bar = new RectangleF(x, Height - Theme.Px(15), w, Theme.Px(4));
            Theme.FillRound(g, bar, Theme.Px(2), Theme.Mix(Theme.Card, Theme.Track, 0.7F));
            float full = bar.Width * fill.Value;
            if (full >= bar.Height) Theme.FillRound(g, new RectangleF(bar.X, bar.Y, full, bar.Height), Theme.Px(2), mark);
        }
    }

    // The "Home" tab: health score, live readings, settings that travel, language, and the way out
    class HomePage : Panel
    {
        readonly Label capScore = Ui.Caption("HEALTH SCORE"), capParts = Ui.Caption("WHAT MAKES UP THE SCORE"), capSettings = Ui.Caption("SETTINGS");
        readonly ScorePanel scorePanel = new ScorePanel();
        readonly StatTile memory = new StatTile("memory", "Memory"), disk = new StatTile("disk", "Disk"), battery = new StatTile("power", "Battery"), uptime = new StatTile("clock", "Running for");
        readonly Panel listPanel = new Panel();
        readonly FindingList list = new FindingList();
        readonly FlatButton btnRecalc = new FlatButton(), btnExport = new FlatButton(), btnImport = new FlatButton(), btnLang = new FlatButton(), btnMotion = new FlatButton(), btnRemove = new FlatButton();
        readonly Timer vitals = new Timer();
        bool computing, loaded;

        public event Action<int> ScoreChanged;
        public event Action LanguageChanged, RemoveRequested;

        public HomePage()
        {
            BackColor = Theme.Bg;
            Controls.AddRange(new Control[] { capScore, capParts, capSettings, scorePanel, memory, disk, battery, uptime });

            listPanel.BackColor = Theme.LogBg;
            listPanel.Padding = new Padding(Theme.Px(6));
            list.Dock = DockStyle.Fill;
            listPanel.Controls.Add(list);
            Controls.Add(listPanel);

            btnRecalc.Text = "Recalculate";
            btnRecalc.Primary = true;
            btnExport.Text = "Export settings";
            btnImport.Text = "Import settings";
            btnLang.Text = "Language: English";
            btnMotion.Text = Motion.On ? "Animations: on" : "Animations: off";
            btnRemove.Text = "Remove TaskBoost";
            btnRecalc.Click += delegate { Recalculate(); };
            btnExport.Click += delegate { Export(); };
            btnImport.Click += delegate { Import(); };
            btnLang.Click += delegate
            {
                Lang.Save(!Lang.Romanian);
                if (LanguageChanged != null) LanguageChanged();
            };
            btnMotion.Click += delegate
            {
                Motion.Save(!Motion.On);
                btnMotion.Text = Motion.On ? "Animations: on" : "Animations: off";
            };
            btnRemove.Click += delegate { if (RemoveRequested != null) RemoveRequested(); };
            Controls.AddRange(new Control[] { btnRecalc, btnExport, btnImport, btnLang, btnMotion, btnRemove });

            int last = HealthScore.Last;
            if (last >= 0) scorePanel.Set(last, HealthScore.Tier(last));

            // The readings are cheap to take, so they stay live while the tab is open
            vitals.Interval = 3000;
            vitals.Tick += delegate { ReadVitals(); };
            Disposed += delegate { vitals.Dispose(); };
            ReadVitals();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            vitals.Enabled = Visible;
            if (Visible) ReadVitals();
            if (Visible && !loaded) Recalculate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), capH = Theme.Px(22), bh = Theme.Px(36), w = ClientSize.Width, y = 0;
            capScore.Location = new Point(0, y);
            y += capH;
            scorePanel.SetBounds(0, y, w, Theme.Px(118));
            y = scorePanel.Bottom + gap;
            StatTile[] tiles = { memory, disk, battery, uptime };
            int tw = (w - 3 * gap) / 4;
            for (int i = 0; i < tiles.Length; i++) tiles[i].SetBounds(i * (tw + gap), y, i == tiles.Length - 1 ? w - i * (tw + gap) : tw, Theme.Px(62));
            y += Theme.Px(62) + Theme.Px(12);
            capParts.Location = new Point(0, y);
            y += capH;
            int settingsH = capH + bh + Theme.Px(10);
            listPanel.SetBounds(0, y, w, Math.Max(Theme.Px(60), ClientSize.Height - settingsH - y - Theme.Px(12)));
            y = listPanel.Bottom + Theme.Px(12);
            capSettings.Location = new Point(0, y);
            y += capH;
            FlatButton[] buttons = { btnRecalc, btnExport, btnImport, btnLang, btnMotion, btnRemove };
            int bw = (w - (buttons.Length - 1) * gap) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++) buttons[i].SetBounds(i * (bw + gap), y, i == buttons.Length - 1 ? w - i * (bw + gap) : bw, bh);
        }

        static string Gb(double bytes) { return (bytes / 1073741824.0).ToString(bytes >= 10737418240.0 ? "0" : "0.0") + " GB"; }

        void ReadVitals()
        {
            long total = Memory.TotalMb(), free = Memory.AvailableMb();
            double freeShare = total > 0 ? free / (double)total : 1;
            memory.Set(Gb(free * 1048576.0) + Lang.T(" free of ") + Gb(total * 1048576.0), (float)(1 - freeShare), freeShare < 0.10 ? 2 : freeShare < 0.25 ? 1 : 0);

            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double share = d.AvailableFreeSpace / (double)d.TotalSize;
                disk.Set(d.Name.TrimEnd('\\') + "  " + Gb(d.AvailableFreeSpace) + Lang.T(" free of ") + Gb(d.TotalSize), (float)(1 - share), share < 0.10 ? 2 : share < 0.20 ? 1 : 0);
            }
            catch (Exception) { disk.Set("", -1F, 0); }

            PowerStatus power = SystemInformation.PowerStatus;
            if ((power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 || power.BatteryLifePercent > 1F)
                battery.Set(Lang.T("No battery: runs on mains"), -1F, 0);
            else
            {
                bool plugged = power.PowerLineStatus == PowerLineStatus.Online, charging = (power.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
                int percent = (int)Math.Round(power.BatteryLifePercent * 100);
                battery.Set(percent + "%" + Lang.T(charging ? ", charging" : plugged ? ", plugged in" : ", on battery"), power.BatteryLifePercent, plugged ? 0 : percent < 15 ? 2 : percent < 30 ? 1 : 0);
            }

            TimeSpan up = Care.Uptime;
            string since = up.TotalHours < 1 ? up.Minutes + " min" : up.TotalDays < 1 ? up.Hours + " h " + up.Minutes + " min" : (int)up.TotalDays + Lang.T(" days ") + up.Hours + " h";
            uptime.Set(since + (up.TotalDays >= 7 ? Lang.T(": a restart would help") : ""), (float)Math.Min(1.0, up.TotalDays / 7.0), up.TotalDays >= 7 ? 1 : 0);
        }

        public void Recalculate()
        {
            if (computing) return;
            computing = loaded = true;
            btnRecalc.Text = "Calculating...";
            btnRecalc.Enabled = false;
            btnRecalc.Busy = scorePanel.Working = true;
            Task.Factory.StartNew(delegate { return HealthScore.Compute(); }).ContinueWith(delegate(Task<List<HealthScore.Part>> t)
            {
                computing = false;
                btnRecalc.Text = "Recalculate";
                btnRecalc.Enabled = true;
                btnRecalc.Busy = scorePanel.Working = false;
                if (t.Exception != null)
                {
                    scorePanel.Set(-1, t.Exception.InnerException.Message);
                    return;
                }
                int score = 0;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var p in t.Result)
                {
                    score += p.Points;
                    list.Items.Add(new Finding { Key = p.Name, Title = Lang.T(p.Name), Detail = p.Why, Level = p.Level, Tag = p.Points + " / " + p.Max, Bar = p.Points / (float)p.Max });
                }
                list.EndUpdate();
                list.Reveal();
                scorePanel.Set(score, HealthScore.Tier(score));
                if (ScoreChanged != null) ScoreChanged(score);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Export()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = Lang.T("Export settings");
                dialog.Filter = "TaskBoost settings (*" + Portable.Extension + ")|*" + Portable.Extension;
                dialog.FileName = "TaskBoost-" + Environment.MachineName + Portable.Extension;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    Portable.Export(dialog.FileName);
                    Dialog.Show(this, Lang.T("Saved. Import this file on another PC and press My full setup there.") + "\n\n" + dialog.FileName);
                }
                catch (Exception ex) { Dialog.Show(this, ex.Message); }
            }
        }

        void Import()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("Import settings");
                dialog.Filter = "TaskBoost settings (*" + Portable.Extension + ")|*" + Portable.Extension;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    int n = Portable.Import(dialog.FileName);
                    Dialog.Show(this, Lang.T("Imported. Restart TaskBoost to see the settings, then press Apply now on the Boost tab.") + " (" + n + ")");
                }
                catch (Exception ex) { Dialog.Show(this, ex.Message); }
            }
        }
    }
}

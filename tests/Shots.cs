using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskBoost
{
    // Makes the pictures in docs\ from the app itself, so they always show what the code really draws.
    // It opens the real window on a sandbox copy of the settings (tests\out\shots), walks through it and captures
    // the screen; the two short films are recorded the same way. See shots.bat.
    static class Shots
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static string docs;
        static bool filmOnly;

        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        static void Pump(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(4); }
        }

        static bool PumpUntil(Func<bool> done, int timeoutMs)
        {
            DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end && !done()) { Application.DoEvents(); Thread.Sleep(10); }
            return done();
        }

        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Any).GetValue(target); }
        static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Any).Invoke(target, args); }
        static IntPtr Pt(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }

        static void Click(Control c, int x, int y, bool right = false)
        {
            PostMessage(c.Handle, right ? 0x204u : 0x201u, (IntPtr)(right ? 2 : 1), Pt(x, y));
            Pump(40);
            PostMessage(c.Handle, right ? 0x205u : 0x202u, IntPtr.Zero, Pt(x, y));
        }

        static Bitmap Grab(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            return bmp;
        }

        static void Save(Rectangle r, string name)
        {
            using (var bmp = Grab(r)) bmp.Save(Path.Combine(docs, name + ".png"), ImageFormat.Png);
            Console.WriteLine("  " + name + ".png  " + r.Width + "x" + r.Height);
        }

        // A plain dark sheet behind what is being pictured, so nothing of the real desktop shows
        class Backdrop : Form
        {
            public Backdrop(Rectangle bounds)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                Bounds = bounds;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(9, 14, 26), Color.FromArgb(3, 5, 9), 90F)) e.Graphics.FillRectangle(b, ClientRectangle);
                base.OnPaint(e);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\.."));
            docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(docs);
            string only = args.Length > 0 ? args[0] : "";

            string data = Path.Combine(root, @"tests\out\shots");
            if (Directory.Exists(data)) Directory.Delete(data, true);
            Directory.CreateDirectory(data);
            // The sandbox starts from the real choices, so the pictures show a real PC; the undo record and the
            // modes that are on stay behind
            string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
            foreach (string f in new[] { "settings.txt", "turbo.txt", "care.txt", "known.txt", "startup-off.txt" })
                if (File.Exists(Path.Combine(real, f))) File.Copy(Path.Combine(real, f), Path.Combine(data, f));
            App.Init(data);
            App.ExePath = Path.Combine(root, "TaskBoost.exe");
            // Never a first run (that would switch "start with Windows" on), and the buddy in its usual blue
            Settings.Save("guard.txt", new[] { "v2", "v3", "v4", "v5", "sensors", "network", "keyboards", "remote", "startup", "shield", "notify", "tray", "buddy" });
            File.WriteAllText(Path.Combine(data, "laptop-mode.txt"), "normal");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            // Outside a message loop Windows Forms takes its own synchronisation context away again, and what the pages
            // do when their background work ends would then run on a worker thread. Here it stays for the whole run.
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Motion.Attach();
            Motion.On = true;
            // A calm PC: whatever the guard would flag on this machine today is taken as known
            var keys = new List<string>();
            foreach (Finding f in Guard.Scan(new[] { "sensors", "network", "keyboards", "remote", "startup", "shield" })) if (f.Level > 0 && !string.IsNullOrEmpty(f.Key)) keys.Add(f.Key);
            Guard.MarkKnown(keys);

            Console.WriteLine("Pictures for " + docs);
            if (only == "" || only == "sprites") Sprites();
            filmOnly = only == "film";
            if (only == "" || only == "window" || filmOnly) Window(false);
            if (only == "" || only == "ro") Window(true);
            if (only == "" || only == "buddy") Buddy();
            return 0;
        }

        // ---------------------------------------------------------------- the buddy's faces and costumes

        static void Sheet(string name, string[][] cells, int perRow)
        {
            // cells: label, mood, costume, accessory
            const int scale = 6, pad = 14, text = 26;
            int cell = BuddySprite.Grid * scale, rows = (cells.Length + perRow - 1) / perRow;
            using (var sheet = new Bitmap(pad + perRow * (cell + pad), pad + rows * (cell + text + pad), PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(sheet))
            using (var sprite = new Bitmap(BuddySprite.Grid, BuddySprite.Grid))
            using (var font = new Font("Segoe UI", 9.5F))
            using (var ink = new SolidBrush(Theme.LogText))
            {
                g.Clear(Theme.Card);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                string wore = BuddySprite.Accessory;
                for (int i = 0; i < cells.Length; i++)
                {
                    BuddySprite.Accessory = cells[i][3];
                    BuddySprite.Draw(sprite, 0, false, 0, cells[i][1], false, cells[i][2]);
                    sprite.MakeTransparent(BuddySprite.Key);
                    int x = pad + i % perRow * (cell + pad), y = pad + i / perRow * (cell + text + pad);
                    g.DrawImage(sprite, new Rectangle(x, y, cell, cell));
                    var format = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString(cells[i][0], font, ink, new RectangleF(x - pad / 2, y + cell + 2, cell + pad, text), format);
                }
                BuddySprite.Accessory = wore;
                sheet.Save(Path.Combine(docs, name + ".png"), ImageFormat.Png);
                Console.WriteLine("  " + name + ".png  " + sheet.Width + "x" + sheet.Height);
            }
        }

        static void Sprites()
        {
            var moods = new List<string[]> { new[] { "normal", "", "", "" } };
            foreach (string m in BuddySprite.Moods) moods.Add(new[] { m, m, "", "" });
            Sheet("emotes", moods.ToArray(), 9);
            Sheet("costumes", new[]
            {
                new[] { "Normal", "", "", "" }, new[] { "clicked", "selected", "", "" }, new[] { "Low power", "sleepy", "low", "" }, new[] { "Study / work", "", "work", "" },
                new[] { "work, thinking", "think", "work", "" }, new[] { "Best performance", "fierce", "turbo", "" }, new[] { "Gaming", "cool", "turbo", "" }
            }, 7);
            Sheet("accessories", new[]
            {
                new[] { "crown (score 90+)", "happy", "", "crown" }, new[] { "bow tie (score 75+)", "", "", "bow" },
                new[] { "crown, full power", "fierce", "turbo", "crown" }, new[] { "at work the hat stays", "think", "work", "crown" }
            }, 4);
        }

        // ---------------------------------------------------------------- the window, tab by tab

        static void Window(bool romanian)
        {
            Lang.Romanian = romanian;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                var form = new MainForm(false, show, exit);
                int marginX = Theme.Px(70), top = Theme.Px(210), bottom = Theme.Px(50);
                var stage = new Rectangle(screen.Left + (screen.Width - form.Width - 2 * marginX) / 2, screen.Top + Math.Max(0, (screen.Height - form.Height - top - bottom) / 2), form.Width + 2 * marginX, form.Height + top + bottom);
                using (var backdrop = new Backdrop(stage))
                {
                    backdrop.Show();
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(stage.Left + marginX, stage.Top + top);
                    form.TopMost = true;
                    typeof(MainForm).GetField("trayHintShown", Any).SetValue(form, true);
                    form.Show();
                    form.Activate();
                    var pages = Field<Control[]>(form, "pages");
                    var buddy = Field<BuddyForm>(form, "buddy");
                    var homeList = Field<FindingList>(pages[0], "list");
                    PumpUntil(delegate { return homeList.Items.Count == 7; }, 40000);
                    Pump(2600);

                    if (romanian)
                    {
                        Save(form.Bounds, "romana");
                    }
                    else if (filmOnly)
                    {
                        Calm(pages);
                        Film(form, pages, stage);
                    }
                    else
                    {
                        Save(form.Bounds, "home");
                        // The window with the buddy on it, saying hello
                        PumpUntil(delegate { return buddy.Visible && buddy.Left > form.Left + Theme.Px(120) && buddy.Right < form.Right - Theme.Px(380); }, 25000);
                        buddy.Say("Hi! I keep this PC fast, clean and guarded.", "happy");
                        Pump(1900);
                        Save(stage, "hero");
                        Pump(5200);

                        string[] names = { "home", "boost", "guard", "startup", "turbo", "care" };
                        for (int i = 1; i < names.Length; i++)
                        {
                            Call(form, "ShowPage", i);
                            if (i == 3) PumpUntil(delegate { return Field<FindingList>(pages[3], "list").Items.Count > 0; }, 40000);
                            Pump(i == 3 ? 1600 : 1300);
                            Save(form.Bounds, names[i]);
                        }

                        // The shortcut list
                        var timer = new System.Windows.Forms.Timer();
                        timer.Interval = 250;
                        int waited = 0;
                        timer.Tick += delegate
                        {
                            foreach (Form f in Application.OpenForms)
                                if (f is Dialog && f.Visible && ++waited > 3)
                                {
                                    timer.Stop();
                                    Save(f.Bounds, "keys");
                                    f.DialogResult = DialogResult.No;
                                    return;
                                }
                        };
                        timer.Start();
                        Dialog.Shortcuts(form, MainForm.ShortcutHelp);
                        timer.Dispose();

                        // The tray icon's menu, over the corner of the stage
                        var tray = Field<PopMenu>(form, "trayMenu");
                        tray.Pinned = true;
                        Call(form, "ShowPage", 0);
                        Pump(600);
                        tray.ShowAt(new Point(form.Right - Theme.Px(60), form.Bottom - Theme.Px(20)));
                        Pump(500);
                        Call(tray, "Track", new Point(Theme.Px(60), Theme.Px(70)));
                        Pump(350);
                        Save(tray.Bounds, "tray");
                        tray.Dismiss();
                        Pump(300);

                        Calm(pages);
                        Film(form, pages, stage);
                    }

                    typeof(MainForm).GetField("exiting", Any).SetValue(form, true);
                    form.Close();
                    Pump(300);
                }
            }
            Lang.Romanian = false;
        }

        // Whatever the guard has started to flag since the run began (a program that just went online, say) is
        // taken as known, so the film shows the window at rest
        static void Calm(Control[] pages)
        {
            var list = Field<FindingList>(pages[2], "list");
            var keys = new List<string>();
            foreach (Finding f in list.Items) if (f.Level > 0 && !string.IsNullOrEmpty(f.Key)) keys.Add(f.Key);
            if (keys.Count == 0) return;
            Guard.MarkKnown(keys);
            Call(pages[2], "ScanNow");
            Pump(2500);
        }

        // A short film of the window: the score ring filling, tabs sliding, a switch being flipped
        static void Film(MainForm form, Control[] pages, Rectangle stage)
        {
            var tabs = Field<TabBar>(form, "tabs");
            var film = new GifFilm(0.5F);
            Rectangle shot = form.Bounds;
            Func<Control, Point, Point> at = delegate(Control c, Point p) { Point s = c.PointToScreen(p); return new Point(s.X - shot.X, s.Y - shot.Y); };
            Point pointer = new Point(shot.Width / 2, shot.Height / 2);
            Action<Point, int> glide = delegate(Point to, int frames)
            {
                Point from = pointer;
                for (int i = 1; i <= frames; i++)
                {
                    float t = Motion.EaseOut(i / (float)frames);
                    pointer = new Point((int)(from.X + (to.X - from.X) * t), (int)(from.Y + (to.Y - from.Y) * t));
                    film.Add(Grab(shot), pointer, 70);
                    Pump(45);
                }
            };
            Action<int> roll = delegate(int ms)
            {
                DateTime end = DateTime.Now.AddMilliseconds(ms);
                while (DateTime.Now < end)
                {
                    film.Add(Grab(shot), pointer, 60);
                    Pump(45);
                }
            };
            Func<int, Point> tab = delegate(int i) { return at(tabs, new Point((int)(tabs.Width * (i + 0.5F) / 6F), tabs.Height / 2)); };

            // Home: come back to it so the ring fills from empty
            Call(form, "ShowPage", 1);
            Pump(500);
            glide(tab(0), 6);
            Click(tabs, (int)(tabs.Width * 0.5F / 6F), tabs.Height / 2);
            roll(2300);
            // Boost: flip a switch off and on
            glide(tab(1), 7);
            Click(tabs, (int)(tabs.Width * 1.5F / 6F), tabs.Height / 2);
            roll(900);
            var cards = Field<Dictionary<string, ToggleCard>>(pages[1], "cards");
            ToggleCard card = cards["animations"];
            Point on = at(card, new Point(card.Width - Theme.Px(30), Theme.Px(23)));
            glide(on, 8);
            Click(card, card.Width - Theme.Px(30), Theme.Px(23));
            roll(800);
            Click(card, card.Width - Theme.Px(30), Theme.Px(23));
            roll(800);
            // Guard, Startup, and home again
            glide(tab(2), 7);
            Click(tabs, (int)(tabs.Width * 2.5F / 6F), tabs.Height / 2);
            roll(1300);
            glide(tab(3), 5);
            Click(tabs, (int)(tabs.Width * 3.5F / 6F), tabs.Height / 2);
            roll(1700);
            glide(tab(0), 9);
            Click(tabs, (int)(tabs.Width * 0.5F / 6F), tabs.Height / 2);
            roll(2400);
            film.Save(Path.Combine(docs, "motion.gif"));
        }

        // ---------------------------------------------------------------- the buddy and its menu

        static void Buddy()
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            var stage = new Rectangle(screen.Left + Theme.Px(120), screen.Top + Theme.Px(60), Theme.Px(430), Theme.Px(560));
            // A ledge for the buddy to walk on, low on the stage, with room above it for the menu
            var ledge = new Rectangle(stage.Left + Theme.Px(30), stage.Bottom - Theme.Px(46), stage.Width - Theme.Px(60), Theme.Px(30));
            using (var backdrop = new Backdrop(stage))
            using (var buddy = new BuddyForm())
            {
                backdrop.Paint += delegate(object s, PaintEventArgs e)
                {
                    var r = backdrop.RectangleToClient(ledge);
                    using (var b = new SolidBrush(Theme.Tile)) e.Graphics.FillRectangle(b, r.X, r.Y, r.Width, Theme.Px(4));
                };
                backdrop.Show();
                string asked = null;
                buddy.Status = delegate { return null; };
                buddy.Costume = delegate { return asked ?? "normal"; };
                buddy.SetLaptopMode = delegate(string mode) { asked = mode; };
                buddy.Option = delegate(string key) { return key == "buddy"; };
                buddy.Perch = delegate { return ledge; };
                buddy.Show();
                var menu = Field<PopMenu>(buddy, "menu");
                menu.Pinned = true;
                var items = Field<List<PopItem>>(menu, "items");
                PumpUntil(delegate { return Field<bool>(buddy, "perched"); }, 5000);
                Pump(1500);

                var film = new GifFilm(0.75F);
                Point pointer = new Point(stage.Width - Theme.Px(60), Theme.Px(80));
                Action<int> roll = delegate(int ms)
                {
                    DateTime end = DateTime.Now.AddMilliseconds(ms);
                    while (DateTime.Now < end)
                    {
                        film.Add(Grab(stage), pointer, 70);
                        Pump(55);
                    }
                };
                Action<Point, int> glide = delegate(Point to, int frames)
                {
                    Point from = pointer;
                    for (int i = 1; i <= frames; i++)
                    {
                        float t = Motion.EaseOut(i / (float)frames);
                        pointer = new Point((int)(from.X + (to.X - from.X) * t), (int)(from.Y + (to.Y - from.Y) * t));
                        film.Add(Grab(stage), pointer, 70);
                        Pump(55);
                    }
                };
                Func<Control, Point, Point> at = delegate(Control c, Point p) { Point s = c.PointToScreen(p); return new Point(s.X - stage.X, s.Y - stage.Y); };
                Func<Point> onBuddy = delegate { return at(buddy, new Point(buddy.Width / 2, buddy.Height / 2)); };

                // It walks; when it is near the middle of the stage the pointer comes, and a click opens the menu
                roll(1200);
                DateTime limit = DateTime.Now.AddSeconds(14);
                while (DateTime.Now < limit && Math.Abs(buddy.Left + buddy.Width / 2 - (stage.Left + stage.Width / 2)) > Theme.Px(26)) roll(60);
                typeof(BuddyForm).GetField("mode", Any).SetValue(buddy, Enum.Parse(typeof(BuddyForm).GetNestedType("Mode", Any), "Idle"));
                typeof(BuddyForm).GetField("modeTicks", Any).SetValue(buddy, 400);
                glide(onBuddy(), 9);
                Click(buddy, buddy.Width / 2, buddy.Height / 2);
                roll(900);

                // Over the mode faces, one by one
                PopItem chips = items.Find(delegate(PopItem i) { return i.Kind == PopKind.Chips; });
                for (int i = 0; i < chips.Chips.Length; i++)
                {
                    var cell = (Rectangle)Call(menu, "ChipCell", chips, i);
                    Point middle = new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                    glide(at(menu, middle), 4);
                    Call(menu, "Track", middle);
                    roll(i == 0 ? 500 : 420);
                    if (i == 1) Save(Rectangle.Union(menu.Bounds, Rectangle.Inflate(buddy.Bounds, Theme.Px(30), Theme.Px(12))), "menu");
                }
                // Unfold the emotes and pick one
                PopItem emotes = items.Find(delegate(PopItem i) { return i.Opens != null; });
                Point row = new Point(emotes.Bounds.X + Theme.Px(70), emotes.Bounds.Y + emotes.Bounds.Height / 2);
                glide(at(menu, row), 5);
                Call(menu, "Track", row);
                roll(300);
                Click(menu, row.X, row.Y);
                roll(700);
                PopItem grid = emotes.Opens;
                int[] visit = { 1, 8, 4 };
                Point face = Point.Empty;
                foreach (int k in visit)
                {
                    var cell = (Rectangle)Call(menu, "FaceCell", grid, k);
                    face = new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                    glide(at(menu, face), 4);
                    Call(menu, "Track", face);
                    roll(520);
                }
                Save(Rectangle.Union(menu.Bounds, Rectangle.Inflate(buddy.Bounds, Theme.Px(30), Theme.Px(12))), "menu-emotes");
                Click(menu, face.X, face.Y);
                roll(2300);
                // Once more: a mode this time, and the buddy changes its costume
                glide(onBuddy(), 6);
                Click(buddy, buddy.Width / 2, buddy.Height / 2);
                roll(700);
                chips = items.Find(delegate(PopItem i) { return i.Kind == PopKind.Chips; });
                var work = (Rectangle)Call(menu, "ChipCell", chips, 2);
                Point target = new Point(work.X + work.Width / 2, work.Y + work.Height / 2);
                glide(at(menu, target), 6);
                Call(menu, "Track", target);
                roll(450);
                Click(menu, target.X, target.Y);
                roll(700);
                typeof(BuddyForm).GetField("mode", Any).SetValue(buddy, Enum.Parse(typeof(BuddyForm).GetNestedType("Mode", Any), "Walk"));
                typeof(BuddyForm).GetField("modeTicks", Any).SetValue(buddy, 200);
                glide(new Point(stage.Width - Theme.Px(60), Theme.Px(80)), 8);
                roll(2200);
                film.Save(Path.Combine(docs, "buddy.gif"));
            }
        }
    }

    // A small animated-GIF writer: one palette for the whole film, and each frame holds only the rectangle that
    // changed since the one before. A pointer is drawn on every frame, because screen captures do not include it.
    class GifFilm
    {
        readonly float scale;
        readonly List<Bitmap> frames = new List<Bitmap>();
        readonly List<int> delays = new List<int>();

        public GifFilm(float scale) { this.scale = scale; }

        // Takes over the bitmap. pointer is in the capture's own pixels.
        public void Add(Bitmap capture, Point pointer, int ms)
        {
            var small = new Bitmap((int)(capture.Width * scale), (int)(capture.Height * scale), PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(capture, new Rectangle(0, 0, small.Width, small.Height));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float u = Math.Max(1F, Theme.Scale * scale * 1.15F), x = pointer.X * scale, y = pointer.Y * scale;
                var arrow = new[] { new PointF(x, y), new PointF(x, y + 16 * u), new PointF(x + 4.2F * u, y + 12.4F * u), new PointF(x + 7 * u, y + 18.6F * u), new PointF(x + 9.6F * u, y + 17.4F * u), new PointF(x + 6.8F * u, y + 11.4F * u), new PointF(x + 12 * u, y + 11.4F * u) };
                using (var edge = new Pen(Color.FromArgb(20, 20, 24), 1.6F * u)) g.DrawPolygon(edge, arrow);
                g.FillPolygon(Brushes.White, arrow);
            }
            capture.Dispose();
            frames.Add(small);
            delays.Add(ms);
        }

        static byte[] Pixels(Bitmap b)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var rows = new byte[data.Stride * b.Height];
            Marshal.Copy(data.Scan0, rows, 0, rows.Length);
            b.UnlockBits(data);
            if (data.Stride == b.Width * 3) return rows;
            var packed = new byte[b.Width * b.Height * 3];
            for (int y = 0; y < b.Height; y++) Buffer.BlockCopy(rows, y * data.Stride, packed, y * b.Width * 3, b.Width * 3);
            return packed;
        }

        public void Save(string file)
        {
            if (frames.Count == 0) return;
            int w = frames[0].Width, h = frames[0].Height;
            // The 256 colours used most, counted in bins of 32 levels a channel; each bin's colour is the average of its pixels
            var count = new int[32768];
            var sum = new long[32768, 3];
            var raw = new List<byte[]>();
            foreach (Bitmap f in frames)
            {
                byte[] p = Pixels(f);
                raw.Add(p);
                for (int i = 0; i < p.Length; i += 3)
                {
                    int bin = (p[i + 2] >> 3 << 10) | (p[i + 1] >> 3 << 5) | (p[i] >> 3);
                    count[bin]++;
                    sum[bin, 0] += p[i + 2];
                    sum[bin, 1] += p[i + 1];
                    sum[bin, 2] += p[i];
                }
                f.Dispose();
            }
            var bins = new List<int>();
            for (int i = 0; i < 32768; i++) if (count[i] > 0) bins.Add(i);
            bins.Sort(delegate(int a, int b) { return count[b].CompareTo(count[a]); });
            int colours = Math.Min(256, bins.Count);
            var palette = new byte[256 * 3];
            for (int i = 0; i < colours; i++)
                for (int c = 0; c < 3; c++) palette[i * 3 + c] = (byte)(sum[bins[i], c] / count[bins[i]]);
            var nearest = new short[32768];
            for (int i = 0; i < 32768; i++) nearest[i] = -1;
            for (int i = 0; i < colours; i++) nearest[bins[i]] = (short)i;
            Func<int, byte> index = delegate(int bin)
            {
                if (nearest[bin] >= 0) return (byte)nearest[bin];
                int r = (bin >> 10 << 3) + 4, g = ((bin >> 5) & 31) * 8 + 4, b = (bin & 31) * 8 + 4, best = 0, bestGap = int.MaxValue;
                for (int i = 0; i < colours; i++)
                {
                    int dr = palette[i * 3] - r, dg = palette[i * 3 + 1] - g, db = palette[i * 3 + 2] - b, gap = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (gap < bestGap) { bestGap = gap; best = i; }
                }
                nearest[bin] = (short)best;
                return (byte)best;
            };

            using (var output = new FileStream(file, FileMode.Create))
            {
                var writer = new BinaryWriter(output);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
                writer.Write((ushort)w);
                writer.Write((ushort)h);
                writer.Write(new byte[] { 0xF7, 0, 0 }); // one palette of 256 colours for every frame
                writer.Write(palette);
                writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
                writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                writer.Write(new byte[] { 3, 1, 0, 0, 0 }); // repeat for ever

                byte[] before = null;
                long delayAt = 0;
                int pending = 0, written = 0;
                for (int f = 0; f < raw.Count; f++)
                {
                    var now = new byte[w * h];
                    byte[] p = raw[f];
                    for (int i = 0, j = 0; i < now.Length; i++, j += 3) now[i] = index((p[j + 2] >> 3 << 10) | (p[j + 1] >> 3 << 5) | (p[j] >> 3));
                    raw[f] = null;
                    // The rectangle that differs from the frame before
                    int left = 0, top = 0, right = w - 1, bottom = h - 1;
                    if (before != null)
                    {
                        left = w; top = h; right = -1; bottom = -1;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                if (now[y * w + x] != before[y * w + x])
                                {
                                    if (x < left) left = x;
                                    if (x > right) right = x;
                                    if (y < top) top = y;
                                    if (y > bottom) bottom = y;
                                }
                        if (right < 0)
                        {
                            // Nothing moved: the frame before simply stays on screen longer
                            pending += delays[f];
                            long here = output.Position;
                            output.Position = delayAt;
                            writer.Write((ushort)Math.Min(65535, pending / 10));
                            output.Position = here;
                            continue;
                        }
                    }
                    pending = delays[f];
                    writer.Write(new byte[] { 0x21, 0xF9, 4, 4 }); // leave the frame in place when the next one comes
                    delayAt = output.Position;
                    writer.Write((ushort)(pending / 10));
                    writer.Write(new byte[] { 0, 0, 0x2C });
                    writer.Write((ushort)left);
                    writer.Write((ushort)top);
                    writer.Write((ushort)(right - left + 1));
                    writer.Write((ushort)(bottom - top + 1));
                    writer.Write((byte)0);
                    var part = new byte[(right - left + 1) * (bottom - top + 1)];
                    for (int y = top, k = 0; y <= bottom; y++, k += right - left + 1) Buffer.BlockCopy(now, y * w + left, part, k, right - left + 1);
                    Pack(output, part);
                    before = now;
                    written++;
                }
                writer.Write((byte)0x3B);
                Console.WriteLine("  " + Path.GetFileName(file) + "  " + w + "x" + h + ", " + written + " frames, " + output.Length / 1024 + " KB");
            }
        }

        // LZW, as GIF wants it: codes grow from 9 to 12 bits and the table starts afresh when it is full
        static void Pack(Stream output, byte[] pixels)
        {
            const int clear = 256, end = 257;
            output.WriteByte(8);
            var block = new byte[255];
            int used = 0, bits = 0, held = 0, size = 9, next = end + 1;
            Action<int> emit = delegate(int code)
            {
                held |= code << bits;
                bits += size;
                while (bits >= 8)
                {
                    block[used++] = (byte)held;
                    held >>= 8;
                    bits -= 8;
                    if (used == 255)
                    {
                        output.WriteByte(255);
                        output.Write(block, 0, 255);
                        used = 0;
                    }
                }
            };
            var table = new Dictionary<int, int>();
            emit(clear);
            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int key = (prefix << 8) | pixels[i], known;
                if (table.TryGetValue(key, out known))
                {
                    prefix = known;
                    continue;
                }
                emit(prefix);
                if (next < 4096)
                {
                    table[key] = next++;
                    if (next > (1 << size) && size < 12) size++;
                }
                else
                {
                    emit(clear);
                    table.Clear();
                    next = end + 1;
                    size = 9;
                }
                prefix = pixels[i];
            }
            emit(prefix);
            emit(end);
            if (bits > 0) block[used++] = (byte)held;
            if (used > 0)
            {
                output.WriteByte((byte)used);
                output.Write(block, 0, used);
            }
            output.WriteByte(0);
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskBoost
{
    // The buddy's picture: a blue spider drawn pixel by pixel on a 28x28 grid, feet at the bottom
    static class BuddySprite
    {
        public const int Grid = 28;
        public static readonly Color Key = Color.FromArgb(255, 0, 255); // transparent
        // Shows a small signal mark in the corner while net focus is on
        public static bool NetBadge;
        // Earned by the health score on the Home tab: "crown" or "bow"
        public static string Accessory = "";

        // Every expression the buddy can show
        public static readonly string[] Moods =
        {
            "happy", "love", "laugh", "wink", "cool", "star", "music", "think",
            "surprised", "scared", "dizzy", "cry", "angry", "fierce", "alert", "yawn", "sleepy"
        };

        static readonly Color Outline = Color.FromArgb(8, 30, 100), Body = Color.FromArgb(38, 120, 255), Belly = Color.FromArgb(24, 88, 214),
                              Shine = Color.FromArgb(120, 205, 255), Leg = Color.FromArgb(18, 72, 200), White = Color.White,
                              Pupil = Color.FromArgb(5, 10, 30), Red = Color.FromArgb(240, 70, 70), Yellow = Color.FromArgb(255, 215, 60);

        // Right-side legs as attach, knee, foot (x, y relative to the body centre row); the left side is mirrored
        static readonly int[][] Legs =
        {
            new[] { 18, -4, 21, -10, 24, -6 },   // front leg, held up like a feeler
            new[] { 20, -2, 24, -7, 27, 1 },
            new[] { 20, 1, 25, -2, 26, 9 },
            new[] { 19, 3, 22, 4, 22, 9 }
        };

        static void Dot(Bitmap b, int x, int y, Color c)
        {
            if (x >= 0 && x < Grid && y >= 0 && y < Grid) b.SetPixel(x, y, c);
        }

        static void Line(Bitmap b, int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Dot(b, x0, y0, c);
                if (x0 == x1 && y0 == y1) return;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // Stamps a small picture written as rows of '#' and '.'
        static void Stamp(Bitmap b, int x, int y, Color c, params string[] rows)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                    if (rows[r][i] == '#') Dot(b, x + i, y + r, c);
        }

        static bool Is(string mood, params string[] any) { return Array.IndexOf(any, mood) >= 0; }

        // look: -1, 0, 1 along the walking direction; frame: 0 or 1
        // costume: the laptop mode ("", "low", "turbo", "work"): red for turbo, a hard hat and glasses for work
        public static void Draw(Bitmap b, int frame, bool walking, int look, string mood, bool blink, string costume)
        {
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++) b.SetPixel(x, y, Key);
            bool red = costume == "turbo" || mood == "selected" || mood == "fierce";
            Color ol = red ? Color.FromArgb(70, 5, 15) : Outline;
            Color body = red ? Color.FromArgb(225, 45, 60) : mood == "angry" ? Color.FromArgb(70, 110, 235) : Body;
            Color belly = red ? Color.FromArgb(170, 25, 40) : Belly, shine = red ? Color.FromArgb(255, 150, 160) : Shine, leg = red ? Color.FromArgb(150, 20, 35) : Leg;
            bool lively = walking || Is(mood, "happy", "love", "laugh", "music", "star", "selected");
            int cy = 17 - (lively && frame == 1 ? 1 : 0); // the body bobs as it steps

            for (int i = 0; i < Legs.Length; i++)
            {
                int[] l = Legs[i];
                bool lifted = lively && (i + frame) % 2 == 0;
                int fx = l[4] + (lifted && walking ? look : 0), fy = 17 + l[5] - (lifted ? 1 : 0);
                if (i == 0 && Is(mood, "happy", "love", "laugh", "music", "star", "selected")) fy -= frame * 3; // wave the front legs
                if (i == 0 && Is(mood, "angry", "scared", "fierce")) fy -= 3;                                   // front legs thrown up
                foreach (bool mirror in new[] { false, true })
                {
                    int ax = mirror ? Grid - l[0] : l[0], kx = mirror ? Grid - l[2] : l[2], tx = mirror ? Grid - fx : fx;
                    Line(b, ax, cy + l[1], kx, cy + l[3], leg);
                    Line(b, kx, cy + l[3], tx, fy, leg);
                }
            }

            for (int y = cy - 8; y <= cy + 8; y++)
                for (int x = 4; x <= 24; x++)
                {
                    double d = Math.Pow((x - 14) / 6.5, 2) + Math.Pow((y - cy) / 6.0, 2);
                    if (d <= 1) Dot(b, x, y, y > cy + 2 ? belly : body);
                    else if (d <= 1.45) Dot(b, x, y, ol);
                }
            Stamp(b, 10, cy - 5, shine, ".##", "#..", "#..");

            // ---- eyes ----
            bool plainEyes = false;
            foreach (int ex in new[] { 10, 16 })
            {
                bool closed = blink || Is(mood, "sleepy", "yawn") || (mood == "wink" && ex == 16);
                if (closed) Stamp(b, ex, cy - 1, ol, "###");
                else if (Is(mood, "happy", "love", "laugh", "music", "selected")) Stamp(b, ex, cy - 2, White, ".#.", "#.#");
                else if (mood == "star") Stamp(b, ex, cy - 2, Yellow, ".#.", "###", ".#.");
                else if (mood == "cool") Stamp(b, ex - 1, cy - 2, Pupil, "#####", "#####", ".###.");
                else
                {
                    plainEyes = true;
                    Stamp(b, ex, cy - 2, White, "###", "###", "###");
                    if (mood == "dizzy") Stamp(b, ex, cy - 2, Pupil, "#.#", ".#.", "#.#");
                    else
                    {
                        Color p = Is(mood, "alert", "fierce") ? Red : Pupil;
                        int px = ex + 1 + look;
                        if (Is(mood, "surprised", "scared")) Dot(b, ex + 1, cy - 1, p);
                        else if (mood == "think") { Dot(b, ex + 2, cy - 2, p); Dot(b, ex + 2, cy - 1, p); }
                        else if (mood == "cry") { Dot(b, ex + 1, cy - 1, p); Dot(b, ex + 1, cy, p); Dot(b, ex + 1, cy + 1 + frame * 2, Shine); Dot(b, ex + 1, cy + 2 + frame * 2, Shine); }
                        else { Dot(b, px, cy - 1, p); Dot(b, px, cy, p); }
                    }
                }
            }
            if (mood == "cool") { Dot(b, 14, cy - 2, Pupil); Dot(b, 10, cy - 2, Shine); Dot(b, 16, cy - 2, Shine); }
            if (Is(mood, "angry", "fierce"))
            {
                Stamp(b, 10, cy - 3, ol, "#..", ".##");
                Stamp(b, 16, cy - 3, ol, "..#", "##.");
            }
            if (costume == "work" && plainEyes) // glasses
            {
                Stamp(b, 9, cy - 3, Pupil, "#####", "#...#", "#...#", "#...#", "#####");
                Stamp(b, 15, cy - 3, Pupil, "#####", "#...#", "#...#", "#...#", "#####");
                Dot(b, 14, cy - 2, Pupil);
            }

            // ---- mouth ----
            if (Is(mood, "happy", "love", "music", "star", "wink", "selected")) Stamp(b, 12, cy + 2, ol, "#...#", ".###.");
            else if (mood == "laugh") Stamp(b, 12, cy + 2, ol, "#####", ".###.");
            else if (mood == "fierce") { Stamp(b, 12, cy + 2, ol, "#####", "#####"); Dot(b, 12, cy + 3, White); Dot(b, 16, cy + 3, White); }
            else if (mood == "cool") Stamp(b, 13, cy + 2, ol, "..#", "##.");
            else if (Is(mood, "surprised", "yawn")) Stamp(b, 13, cy + 2, ol, "##", "##", mood == "yawn" ? "##" : "..");
            else if (mood == "scared") Stamp(b, 12, cy + 3, ol, "#.#.#", ".#.#.");
            else if (Is(mood, "angry", "cry")) Stamp(b, 12, cy + 3, ol, ".###.", "#...#");
            else if (mood == "alert") Stamp(b, 12, cy + 3, ol, "#####");
            else if (mood == "sleepy") Dot(b, 14, cy + 3, ol);
            else Stamp(b, 13, cy + 3, ol, "##");

            // ---- hard hat ----
            if (costume == "work")
            {
                Stamp(b, 10, cy - 10, Yellow, "..#####..", ".#######.", "#########");
                Stamp(b, 9, cy - 7, Yellow, "###########");
                Stamp(b, 14, cy - 10, Color.FromArgb(200, 150, 0), "#", "#", "#");
            }

            if (Accessory == "crown" && costume != "work") Stamp(b, 10, cy - 12, Yellow, "#.#.#.#.#", "#########", ".#######.");
            else if (Accessory == "bow") Stamp(b, 11, cy + 5, Red, "##.##", "#####", "##.##");

            // ---- little symbols over the head ----
            if (mood == "alert") Stamp(b, 13, 1, Red, "##", "##", "##", "##", "..", "##");
            else if (mood == "love") Stamp(b, 11, 1 - frame, Red, ".#.#.", "#####", ".###.", "..#..");
            else if (mood == "sleepy") Stamp(b, 17 + frame, 3 - frame * 2, White, "####", "..#.", ".#..", "####");
            else if (mood == "happy") { Dot(b, 5 + frame, 6, shine); Dot(b, 22 - frame, 5, shine); }
            else if (mood == "angry") Stamp(b, 19, 1 + frame, Red, "#.#", ".#.", "#.#");
            else if (mood == "fierce") { Stamp(b, 3, 4 - frame, Yellow, ".#.", "#.#"); Stamp(b, 22, 3 + frame, Yellow, ".#.", "#.#"); }
            else if (mood == "think") Stamp(b, 18, 0 + frame, White, "###", "..#", ".#.", "...", ".#.");
            else if (mood == "music") Stamp(b, 18 + frame, 0, Shine, "..##", "..#.", "..#.", "###.", "###.");
            else if (mood == "star") { Stamp(b, 3 + frame, 3, Yellow, ".#.", "###", ".#."); Stamp(b, 21 - frame, 1, Yellow, ".#.", "###", ".#."); }
            else if (mood == "dizzy") { Dot(b, 8 + frame * 3, 4, Yellow); Dot(b, 19 - frame * 3, 3, Yellow); Dot(b, 13 + frame, 2, Yellow); }
            else if (mood == "scared") Stamp(b, 21, cy - 9 + frame, Shine, ".#.", "###", "###");
            else if (mood == "laugh") { Dot(b, 4, 7 - frame, Shine); Dot(b, 23, 7 - frame, Shine); Dot(b, 6, 4 + frame, Shine); Dot(b, 21, 4 + frame, Shine); }
            else if (mood == "surprised") Stamp(b, 13, 1, White, "##", "##", "..", "##");
            else if (mood == "selected") { Dot(b, 4, 6 - frame, Yellow); Dot(b, 23, 5 + frame, Yellow); }

            if (NetBadge) Stamp(b, 0, 0, Yellow, ".###.", "#...#", "..#..", ".#.#.", "..#.."); // signal waves
        }
    }
    // A small always-on-top window that never takes focus
    class FloatingForm : Form
    {
        protected FloatingForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = BuddySprite.Key;
            TransparencyKey = BuddySprite.Key;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // tool window, never activated, topmost
                return cp;
            }
        }
    }

    // Speech bubble shown next to the buddy. It fades in, and its words appear one after the other, as if spoken.
    class BubbleForm : FloatingForm
    {
        readonly Timer hide = new Timer();
        readonly Anim said, fade;
        string text = "";

        public BubbleForm()
        {
            hide.Tick += delegate { hide.Stop(); Hide(); };
            said = new Anim(1F, Invalidate);
            fade = new Anim(1F, delegate { Opacity = Math.Max(0F, Math.Min(1F, fade.Value)); });
            Disposed += delegate { hide.Dispose(); };
        }

        public void ShowNear(Rectangle buddy, Rectangle area, string message, int milliseconds)
        {
            text = message;
            said.Jump(0F);
            said.To(1F, Math.Min(1500, 200 + 32 * text.Length));
            fade.Jump(0F);
            fade.To(1F, 150);
            int pad = Theme.Px(10);
            Size measured = TextRenderer.MeasureText(text, Theme.Title, new Size(Theme.Px(250), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size = new Size(measured.Width + 2 * pad, measured.Height + 2 * pad);
            // Above, below, left or right of the buddy: the first place that fits on screen
            int cx = buddy.Left + (buddy.Width - Width) / 2, cyy = buddy.Top + (buddy.Height - Height) / 2;
            Point[] spots = { new Point(cx, buddy.Top - Height), new Point(cx, buddy.Bottom), new Point(buddy.Left - Width, cyy), new Point(buddy.Right, cyy) };
            Point best = spots[0];
            foreach (Point p in spots)
                if (area.Contains(new Rectangle(p, Size))) { best = p; break; }
            best.X = Math.Max(area.Left, Math.Min(area.Right - Width, best.X));
            best.Y = Math.Max(area.Top, Math.Min(area.Bottom - Height, best.Y));
            Location = best;
            hide.Stop();
            hide.Interval = milliseconds;
            hide.Start();
            Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int u = Theme.Px(3); // blocky corners and a thick border, to match the pixel art
            g.Clear(BuddySprite.Key);
            using (var border = new SolidBrush(Theme.Accent))
            using (var fill = new SolidBrush(Theme.Card))
            {
                g.FillRectangle(border, u, 0, Width - 2 * u, Height);
                g.FillRectangle(border, 0, u, Width, Height - 2 * u);
                g.FillRectangle(fill, 2 * u, u, Width - 4 * u, Height - 2 * u);
                g.FillRectangle(fill, u, 2 * u, Width - 2 * u, Height - 4 * u);
            }
            int pad = Theme.Px(10);
            // Whole words only, so the lines break in the same places from the first word to the last
            int reached = (int)(text.Length * said.Value), upTo = said.Value >= 1F ? text.Length : Math.Max(0, text.LastIndexOf(' ', Math.Max(0, Math.Min(text.Length - 1, reached))));
            TextRenderer.DrawText(g, text.Substring(0, upTo), Theme.Title, new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad), Theme.Text, Theme.Card, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    // The desktop buddy: walks the edges of the screen (or the top of the TaskBoost window), reacts to the mouse,
    // to guard alerts and to Claude, and opens TaskBoost when clicked
    class BuddyForm : FloatingForm
    {
        enum Mode { Walk, Idle, Sleep, Thread }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);

        public const string InboxFile = "buddy-inbox.txt", AliveFile = "claude-alive.txt";
        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow" };
        // With "Hide while I work" on, the buddy still shows in these: browsers, File Explorer and terminals
        static readonly string[] FriendlyClasses = { "CabinetWClass", "ExploreWClass", "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS" };
        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "explorer", "cmd", "powershell", "pwsh", "windowsterminal", "conhost", "openconsole" };
        string searchFolder; // the File Explorer folder that was in front when the buddy was clicked
        // Drag and drop: pick the buddy up with the left button and put it down anywhere; it settles on the nearest edge
        bool pressed, dragging, perchIgnore;
        // The window the buddy was dropped on: it sits on that window and follows it. Net focus goes to that
        // program only if the user says so in the small menu that opens after the drop.
        IntPtr focusWindow;
        int focusPid;
        bool netFocus;
        string askName;
        public Action<IntPtr, int> FocusChanged = delegate { };
        // Where the buddy was grabbed (inside its window) and where the pointer was on screen at that moment
        Point grab, grabScreen, lastCursor;
        // What it does by itself when it stops for a moment
        static readonly string[] IdleEmotes = { "think", "music", "yawn", "wink", "cool", "star", "laugh", "happy", "dizzy" };

        public Func<string, bool> Option = delegate { return true; };
        public Action OpenApp = delegate { };
        // The TaskBoost window while it is open: the buddy walks along its top edge instead of round the screen
        public Func<Rectangle?> Perch = delegate { return null; };
        public int AlertLevel;
        // The laptop mode decides the costume and the pace; the buddy's menu switches it
        public Func<string> Costume = delegate { return ""; };
        public Action<string> SetLaptopMode = delegate { };
        string costume = "";
        // menu: opens on any click. ask: opens after a drop on another program's window.
        readonly PopMenu menu = new PopMenu(), ask = new PopMenu();

        readonly Bitmap sprite = new Bitmap(BuddySprite.Grid, BuddySprite.Grid);
        readonly BubbleForm bubble = new BubbleForm();
        readonly Timer timer = new Timer();
        readonly Random random = new Random();
        readonly int cell;
        Mode mode = Mode.Walk;
        int edge, dir = 1, frame, look = 1, ticks, modeTicks = 80, moodTicks, hop, blinkTicks, hurry, forceTicks;
        // How long it has waited for a pointer that is close by, and how long it then keeps clear of it
        int patience, shy;
        float along;
        string mood = "";
        bool gone, claude, napping, perched;
        Rectangle perchRect;
        // Hanging from the top edge on a thread: how far down, how far it will go, and the pause at the bottom
        int threadLen, threadMax, threadDir, threadWait;
        // Which monitor it is walking round
        int screenIndex;
        // One line of status shown at the top of the menu (the Pomodoro timer)
        public Func<string> Status = delegate { return null; };

        public BuddyForm()
        {
            // One sprite pixel on screen: small, but scaled up a little on high-resolution screens
            cell = Math.Max(Theme.Px(3), Screen.PrimaryScreen.Bounds.Height / 380);
            Size = new Size(BuddySprite.Grid * cell, BuddySprite.Grid * cell);
            Cursor = Cursors.Hand;

            // Any click opens the same small menu: the laptop modes as a strip of faces (each one is the face the
            // buddy wears in that mode), the emotes folded away under their own row, and the buddy's commands
            menu.Host = delegate { return SpriteBounds; };
            menu.AddCaption(delegate { return Status(); });
            menu.AddChips(delegate { string now = Costume(); return now == "" ? "normal" : now; }, delegate(string key) { SetMood("", 0); SetLaptopMode(key); },
                PopMenu.Chip("low", Lang.T("Low power  -  longest battery (sleepy spider)"), "sleepy", "low"),
                PopMenu.Chip("normal", Lang.T("Normal  -  everyday balance"), "", ""),
                PopMenu.Chip("work", Lang.T("Study / work  -  quick apps, quiet, easy on battery (engineer)"), "", "work"),
                PopMenu.Chip("best", Lang.T("Best performance  -  full power (red spider)"), "fierce", "turbo"),
                PopMenu.Chip("turbo", Lang.T("Gaming  -  full power, background paused (asks approval)"), "cool", "turbo"));
            menu.AddSeparator();
            PopItem emotes = menu.Add(Lang.T("Emotes"), null);
            emotes.Mood = "happy";
            emotes.Opens = menu.AddFaces(BuddySprite.Moods, delegate(string name) { napping = false; mode = Mode.Idle; modeTicks = 70; SetMood(name, 70); });
            menu.Add(Lang.T("Search for files..."), delegate { SearchFiles(); }).Icon = "search";
            menu.Add(Lang.T("Open TaskBoost"), delegate { OpenApp(); }).Icon = "app";
            menu.Add(Lang.T("Take a nap"), delegate { mode = Mode.Sleep; mood = "sleepy"; napping = true; }).Mood = "sleepy";
            menu.Add(Lang.T("Go away"), delegate { GoAway(); }).Mood = "cry";
            menu.Dismissed += delegate { if (mood == "selected") SetMood("", 0); };

            // After a drop on another program's window: net focus is offered, never started unasked
            ask.Host = delegate { return SpriteBounds; };
            ask.AddCaption(delegate { return askName; });
            ask.Add(Lang.T("Net focus: this program goes first online"), delegate { StartNetFocus(); }).Icon = "network";
            ask.Add(Lang.T("Just sit here"), null).Mood = "happy";

            timer.Interval = 70; // short ticks and whole-pixel steps: quick, but still chunky and pixelated
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Disposed += delegate { timer.Dispose(); bubble.Dispose(); sprite.Dispose(); menu.Dispose(); ask.Dispose(); };
        }

        public bool IsGone { get { return gone; } }

        // The sprite itself: the whole window, or its bottom part while it hangs from a thread
        Rectangle SpriteBounds { get { return new Rectangle(Left, Bottom - Width, Width, Width); } }

        public void GoAway()
        {
            gone = true;
            bubble.Hide();
            menu.Dismiss();
            ask.Dismiss();
            Hide();
        }

        public void ComeBack()
        {
            gone = false;
            forceTicks = 100;
        }

        // Speak and show a mood. The buddy stops to talk, and shows up for it even if it was hiding.
        public void Say(string text, string newMood)
        {
            if (gone || !Option("buddy")) return;
            text = Lang.T(text);
            LeaveThread();
            forceTicks = 110;
            SetMood(newMood, 95);
            napping = false;
            mode = Mode.Idle;
            modeTicks = 95;
            UpdateVisibility();
            if (!Visible || string.IsNullOrEmpty(text)) return;
            // The bubble takes the place beside the buddy where a menu would be
            menu.Dismiss();
            ask.Dismiss();
            SetMood(newMood, 95);
            bubble.ShowNear(Bounds, Area, text.Length > 220 ? text.Substring(0, 220) + "..." : text, 6500);
        }

        void SetMood(string newMood, int forTicks)
        {
            mood = newMood ?? "";
            moodTicks = forTicks;
            if (mood == "happy" || mood == "love" || mood == "surprised" || mood == "laugh" || mood == "star") hop = 5;
        }

        Rectangle Area
        {
            get
            {
                var screens = Screen.AllScreens;
                if (screenIndex >= screens.Length) screenIndex = 0;
                return screens[screenIndex].WorkingArea;
            }
        }

        void LeaveThread()
        {
            if (mode != Mode.Thread) return;
            mode = Mode.Idle;
            modeTicks = 20;
            threadLen = 0;
            Size = new Size(BuddySprite.Grid * cell, BuddySprite.Grid * cell);
        }

        void Tick()
        {
            ticks++;
            if (forceTicks > 0) forceTicks--;

            // Hop onto the app window when it opens, and down onto the screen when it closes
            if (dragging)
            {
                // Carried: legs kicking, no walking
                if (ticks % 2 == 0) frame ^= 1;
                BuddySprite.Draw(sprite, frame, true, 0, "scared", false, costume);
                Invalidate();
                return;
            }
            Rectangle? perchNow = Perch();
            if (!perchNow.HasValue) perchIgnore = false; // dropped off the window: stay off it until it is reopened
            if (perchIgnore) perchNow = null;
            if (focusWindow != IntPtr.Zero)
            {
                RECT fr;
                if (!IsWindow(focusWindow) || !IsWindowVisible(focusWindow) || IsIconic(focusWindow) || !GetWindowRect(focusWindow, out fr)) EndFocus();
                else perchNow = Rectangle.FromLTRB(fr.L, fr.T, fr.R, fr.B);
            }
            BuddySprite.NetBadge = netFocus;
            if (perchNow.HasValue) perchRect = perchNow.Value;
            if (perchNow.HasValue != perched)
            {
                perched = perchNow.HasValue;
                edge = 0;
                along = Math.Max(0, Left - (perched ? perchRect.Left : Area.Left));
                mode = Mode.Walk;
                modeTicks = 60;
                SetMood("happy", 20);
            }

            if (ticks % 5 == 0)
            {
                costume = Costume();
                if (costume == "normal") costume = "";
                if (costume == "best") costume = "turbo"; // same look: red and fast
                ReadInbox();
                UpdateClaude();
                UpdateVisibility();
            }
            if (!Visible) return;

            if (moodTicks > 0 && --moodTicks == 0) mood = "";
            if (blinkTicks > 0) blinkTicks--;
            else if (random.Next(60) == 0) blinkTicks = 3;
            if (hop > 0 && !pressed) hop--; // held in mid-hop, it hangs there rather than dropping from under the pointer
            if (hurry > 0) hurry--;

            // The mouse: the buddy watches the pointer when it is near and waits for it, so it is easy to click.
            // If nobody clicks for a few seconds it scuttles out of the pointer's way instead.
            Point c = Cursor.Position, centre = new Point(Left + Width / 2, Bottom - Width / 2);
            int fx = edge == 0 ? 1 : edge == 2 ? -1 : 0, fy = edge == 3 ? 1 : edge == 1 ? -1 : 0; // forward direction on this edge
            int forward = (c.X - centre.X) * fx + (c.Y - centre.Y) * fy;
            double distance = Math.Sqrt(Math.Pow(c.X - centre.X, 2) + Math.Pow(c.Y - centre.Y, 2));
            double rush = Math.Sqrt(Math.Pow(c.X - lastCursor.X, 2) + Math.Pow(c.Y - lastCursor.Y, 2));
            lastCursor = c;
            if (distance < Width * 3) look = forward > cell * 3 ? 1 : forward < -cell * 3 ? -1 : 0;
            else if (mode == Mode.Walk) look = dir;
            // Pressed, or showing a menu: it stays exactly where it is, so a click is never taken for a drag
            bool held = pressed || menu.Visible || ask.Visible;
            if (shy > 0) shy--;
            if (held)
            {
                patience = hurry = 0;
                if (mode == Mode.Walk) mode = Mode.Idle;
                if (mode == Mode.Idle) modeTicks = Math.Max(modeTicks, 8);
            }
            else if (distance > Width * 0.9 || shy > 0 || mode == Mode.Sleep || mode == Mode.Thread) patience = 0;
            else if (++patience < 45)
            {
                // A pointer that arrives in a rush startles it, but it stays put
                if (patience == 1 && rush > Width * 0.5 && mood == "" && !bubble.Visible) SetMood("surprised", 8);
                if (mode == Mode.Walk) mode = Mode.Idle;
                modeTicks = Math.Max(modeTicks, 6);
                hurry = 0;
            }
            else
            {
                patience = 0;
                shy = 90;
                dir = forward > 0 ? -1 : 1;
                mode = Mode.Walk;
                modeTicks = 40;
                hurry = 16;
            }

            if (mode == Mode.Sleep)
            {
                if (!napping && IdleSeconds() < 2) { mode = Mode.Idle; modeTicks = 25; SetMood("surprised", 12); }
                else if (ticks % 8 == 0) frame ^= 1;
            }
            else if (mode == Mode.Walk)
            {
                if (ticks % 2 == 0 || hurry > 0) frame ^= 1;
                // Turbo runs twice as fast with more dashes; low power ambles at half speed
                if (costume != "low" || hurry > 0 || ticks % 2 == 0)
                    Advance(cell * (hurry > 0 ? 3 : costume == "turbo" ? 2 : 1) * dir);
                // Agile: sudden dashes and little jumps while it runs about
                if (hurry == 0 && random.Next(costume == "turbo" ? 25 : costume == "low" ? 200 : 70) == 0) hurry = 12;
                if (hop == 0 && random.Next(90) == 0) hop = 5;
                // On the top edge, now and then let itself down on a thread
                if (edge == 2 && !perched && hurry == 0 && hop == 0 && random.Next(250) == 0)
                {
                    mode = Mode.Thread;
                    threadLen = 0;
                    threadDir = 1;
                    threadWait = 0;
                    threadMax = random.Next(Height * 2, Math.Max(Height * 3, Area.Height / 2));
                }
                else if (--modeTicks <= 0)
                {
                    if (random.Next(3) == 0)
                    {
                        mode = Mode.Idle;
                        modeTicks = random.Next(18, 55);
                        // Focused at work, yawning on low power, otherwise a random emote now and then
                        if (mood == "" && costume != "work" && random.Next(2) == 0)
                            SetMood(costume == "low" ? "yawn" : IdleEmotes[random.Next(IdleEmotes.Length)], modeTicks);
                    }
                    else { modeTicks = random.Next(40, 170); if (random.Next(3) == 0) dir = -dir; }
                }
            }
            else if (mode == Mode.Thread)
            {
                if (ticks % 3 == 0) frame ^= 1;
                if (threadWait > 0)
                {
                    if (!held && --threadWait == 0) threadDir = -1;
                }
                else if (!held)
                {
                    threadLen += cell * 2 * threadDir;
                    if (threadLen >= threadMax)
                    {
                        threadLen = threadMax;
                        threadWait = random.Next(15, 45);
                        if (mood == "") SetMood(random.Next(2) == 0 ? "cool" : "happy", threadWait);
                    }
                    else if (threadLen <= 0)
                    {
                        LeaveThread();
                        mode = Mode.Walk;
                        modeTicks = random.Next(40, 120);
                    }
                }
            }
            else
            {
                if (mood != "" && ticks % 4 == 0) frame ^= 1; // moods animate even when standing still
                if (mood == "" && ticks % 12 == 0) look = random.Next(-1, 2);
                if (IdleSeconds() > 120 && mood == "") { mode = Mode.Sleep; mood = "sleepy"; moodTicks = 0; }
                else if (--modeTicks <= 0 && !bubble.Visible) { mode = Mode.Walk; modeTicks = random.Next(40, 170); }
            }
            if (mode == Mode.Sleep) mood = "sleepy";

            Place();
            string face = mood != "" ? mood : AlertLevel == 2 ? "alert" : costume == "turbo" ? "fierce" : costume == "low" ? "sleepy" : "";
            BuddySprite.Draw(sprite, frame, mode == Mode.Walk, look, face, blinkTicks > 0, costume);
            Invalidate();
        }

        // Forward runs anticlockwise round the work area: along the bottom, up the right side, back along the top, down the left
        void Advance(int pixels)
        {
            along += pixels;
            if (perched)
            {
                // Back and forth along the top of the window
                float room = Math.Max(0, perchRect.Width - Width);
                if (along > room) { along = room; dir = -1; }
                else if (along < 0) { along = 0; dir = 1; }
                return;
            }
            float length = EdgeLength(edge);
            if (along > length)
            {
                // A full lap done: with more than one monitor, sometimes wander over to another one
                var screens = Screen.AllScreens;
                if (edge == 3 && screens.Length > 1 && random.Next(2) == 0) screenIndex = (screenIndex + 1 + random.Next(screens.Length - 1)) % screens.Length;
                edge = (edge + 1) % 4;
                along = 0;
            }
            else if (along < 0) { edge = (edge + 3) % 4; along = EdgeLength(edge); }
        }

        float EdgeLength(int e) { return (e % 2 == 0 ? Area.Width : Area.Height) - Width; }

        void Place()
        {
            Rectangle a = Area;
            int t = (int)along, jump = cell * new[] { 0, 1, 2, 3, 2, 1 }[hop];
            if (mood == "scared" || mood == "angry") t += (ticks % 2 == 0 ? cell : -cell) / 2; // trembling
            if (mode == Mode.Thread)
            {
                // The window grows downwards to hold the thread; the sprite hangs at its bottom
                var tall = new Size(BuddySprite.Grid * cell, BuddySprite.Grid * cell + threadLen);
                if (Size != tall) Size = tall;
                var hang = new Point(a.Right - Width - t, a.Top);
                if (Location != hang) Location = hang;
                return;
            }
            if (perched)
            {
                // Feet on the top edge of the window; inside it when the window touches the top of the screen
                int top = perchRect.Top - Height + cell;
                if (top < Screen.FromRectangle(perchRect).Bounds.Top) top = perchRect.Top;
                var onWindow = new Point(perchRect.Left + Math.Max(0, Math.Min(t, perchRect.Width - Width)), top - jump);
                if (Location != onWindow) Location = onWindow;
                return;
            }
            Point p = edge == 0 ? new Point(a.Left + t, a.Bottom - Height - jump)
                    : edge == 1 ? new Point(a.Right - Width - jump, a.Bottom - Height - t)
                    : edge == 2 ? new Point(a.Right - Width - t, a.Top + jump)
                    : new Point(a.Left + jump, a.Top + t);
            if (Location != p) Location = p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Feet always point at the edge the buddy is walking on
            int drop = mode == Mode.Thread ? threadLen : 0;
            if (drop > 0)
                using (var thread = new Pen(Color.FromArgb(150, 200, 255), Math.Max(1, cell / 2)))
                    e.Graphics.DrawLine(thread, Width / 2, 0, Width / 2, drop + cell * 5);
            using (var turned = (Bitmap)sprite.Clone())
            {
                turned.RotateFlip(edge == 1 ? RotateFlipType.Rotate270FlipNone : edge == 2 ? RotateFlipType.Rotate180FlipNone : edge == 3 ? RotateFlipType.Rotate90FlipNone : RotateFlipType.RotateNoneFlipNone);
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.DrawImage(turned, 0, drop, Width, Width);
            }
        }

        // Asks what to look for, then opens a Windows search in the File Explorer folder that was in front
        // (or in the user's own folder when no Explorer window was)
        void SearchFiles()
        {
            string folder = searchFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string query = Dialog.Prompt(Lang.T("Search in ") + folder, SpriteBounds);
            if (string.IsNullOrWhiteSpace(query)) return;
            try
            {
                Process.Start("search-ms:displayname=" + Uri.EscapeDataString("TaskBoost search: " + query)
                    + "&crumb=System.Generic.String%3A" + Uri.EscapeDataString(query) + "&crumb=location:" + Uri.EscapeDataString(folder));
                Say(Lang.T("Looking for \"") + query + "\"...", "think");
            }
            catch (Exception ex) { Say(Lang.T("I could not start the search: ") + ex.Message, "cry"); }
        }

        // The folder shown by the File Explorer window in front, or null
        static string ExplorerFolder()
        {
            try
            {
                IntPtr front = GetForegroundWindow();
                Type t = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(t);
                object windows = t.InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                int count = (int)windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null);
                for (int i = 0; i < count; i++)
                {
                    object w = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { i });
                    if (w == null) continue;
                    long hwnd = Convert.ToInt64(w.GetType().InvokeMember("HWND", System.Reflection.BindingFlags.GetProperty, null, w, null));
                    string url = Convert.ToString(w.GetType().InvokeMember("LocationURL", System.Reflection.BindingFlags.GetProperty, null, w, null));
                    if (hwnd == front.ToInt64() && url.StartsWith("file:")) return new Uri(url).LocalPath;
                }
            }
            catch (Exception) { }
            return null;
        }

        // A click with either button opens the menu; holding the left button and moving the pointer carries the buddy
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            napping = false;
            pressed = true;
            grab = e.Location;
            grabScreen = Cursor.Position;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            // Only the pointer really moving on screen is a drag. (Measured inside the window, a buddy that walks
            // on under a still pointer looked like a drag, and a click ended up as a drop.)
            Point c = Cursor.Position;
            if (!dragging && Math.Abs(c.X - grabScreen.X) + Math.Abs(c.Y - grabScreen.Y) > Math.Max(cell * 3, SystemInformation.DragSize.Width * 2))
            {
                menu.Dismiss();
                ask.Dismiss();
                // Picked off its thread: the window shrinks back to the sprite, so the grip moves up with it
                int drop = mode == Mode.Thread ? threadLen : 0;
                LeaveThread();
                grab.Y = Math.Max(0, grab.Y - drop);
                dragging = true;
                edge = 0; // carried upright
                bubble.Hide();
            }
            if (dragging) Location = new Point(c.X - grab.X, c.Y - grab.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && !pressed)
            {
                ToggleMenu();
                return;
            }
            if (e.Button != MouseButtons.Left || !pressed) return;
            pressed = false;
            if (dragging)
            {
                dragging = false;
                Drop();
                return;
            }
            ToggleMenu();
        }

        // Opens the menu beside the buddy, which turns red and waves; a second click puts the menu away
        void ToggleMenu()
        {
            if (menu.IsOpen)
            {
                menu.Dismiss();
                return;
            }
            searchFolder = ExplorerFolder();
            ask.Dismiss();
            bubble.Hide();
            if (mode != Mode.Thread) mode = Mode.Idle;
            modeTicks = 60;
            SetMood("selected", 100000);
            hop = 0; // feet down, so the menu opens beside where it will stay
            Place();
            BuddySprite.Draw(sprite, frame, false, look, "selected", false, costume);
            Invalidate();
            menu.ShowBeside(SpriteBounds);
        }

        // The top-most ordinary window at a point: zero for the desktop, own = true for a TaskBoost window
        IntPtr WindowAt(Point p, out bool own, out int ownerPid)
        {
            IntPtr found = IntPtr.Zero;
            bool mine = false;
            int foundPid = 0, me = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                RECT r;
                if (h == Handle || !IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0 || !GetWindowRect(h, out r)) return true;
                if (p.X < r.L || p.X >= r.R || p.Y < r.T || p.Y >= r.B) return true;
                var cls = new StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                if (Array.IndexOf(ShellClasses, cls.ToString()) >= 0) return true;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                found = h;
                foundPid = pid;
                mine = pid == me;
                return false; // windows come top-most first: this is the one under the buddy
            }, IntPtr.Zero);
            own = mine;
            ownerPid = foundPid;
            return found;
        }

        // Leaves the window it was sitting on; net focus, if it was on, ends with it
        void EndFocus()
        {
            if (focusWindow == IntPtr.Zero) return;
            focusWindow = IntPtr.Zero;
            ask.Dismiss();
            perched = false;
            perchIgnore = true;
            edge = 0;
            along = Math.Max(0, Math.Min(EdgeLength(0), Left - Area.Left));
            StopNetFocus();
        }

        void StartNetFocus()
        {
            if (focusWindow == IntPtr.Zero || netFocus) return;
            netFocus = true;
            FocusChanged(focusWindow, focusPid);
        }

        void StopNetFocus()
        {
            if (!netFocus) return;
            netFocus = false;
            FocusChanged(IntPtr.Zero, 0);
        }

        // Put down after a drag: on another program's window (it sits there and offers net focus), on the
        // TaskBoost window, or on the nearest screen edge
        void Drop()
        {
            Rectangle a = Area;
            Point c = new Point(Left + Width / 2, Top + Height / 2);
            bool own;
            int pid;
            IntPtr target = WindowAt(c, out own, out pid);
            Rectangle? window = Perch();
            if (target != IntPtr.Zero && !own)
            {
                bool moved = target != focusWindow;
                if (moved) StopNetFocus();
                RECT r;
                GetWindowRect(target, out r);
                focusWindow = target;
                focusPid = pid;
                perched = true;
                perchRect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                edge = 0;
                along = Math.Max(0, Left - perchRect.Left);
                mode = Mode.Idle;
                modeTicks = 30;
                hop = 5;
                string path = Guard.ProcessPath(pid);
                askName = path == null ? null : Path.GetFileName(path);
                if (moved && askName != null)
                {
                    hop = 0;
                    Place();
                    ask.ShowBeside(SpriteBounds);
                }
                return;
            }
            EndFocus();
            if (own && window.HasValue)
            {
                perchIgnore = false;
                perched = true;
                perchRect = window.Value;
                edge = 0;
                along = Left - perchRect.Left;
            }
            else
            {
                perchIgnore = true;
                perched = false;
                int toBottom = a.Bottom - c.Y, toTop = c.Y - a.Top, toLeft = c.X - a.Left, toRight = a.Right - c.X;
                int nearest = Math.Min(Math.Min(toBottom, toTop), Math.Min(toLeft, toRight));
                if (nearest == toBottom) { edge = 0; along = Left - a.Left; }
                else if (nearest == toRight) { edge = 1; along = a.Bottom - Height - Top; }
                else if (nearest == toTop) { edge = 2; along = a.Right - Width - Left; }
                else { edge = 3; along = Top - a.Top; }
                along = Math.Max(0, Math.Min(EdgeLength(edge), along));
            }
            mode = Mode.Idle;
            modeTicks = 25;
            SetMood("dizzy", 25);
            hop = 5;
        }

        // ---- when to be on screen ----

        void UpdateVisibility()
        {
            bool wanted = Option("buddy") && !gone && (claude || forceTicks > 0 || !Option("buddyclaude"));
            bool show = wanted && !ForegroundIsFullScreen() && (forceTicks > 0 || !Option("buddyhide") || OnDesktopOrBrowser());
            if (show && !Visible)
            {
                Place();
                BuddySprite.Draw(sprite, frame, false, look, mood, false, costume);
                Show();
                if (mood == "") SetMood("happy", 20);
            }
            else if (!show && Visible)
            {
                bubble.Hide();
                menu.Dismiss();
                ask.Dismiss();
                Hide();
            }
        }

        static string ForegroundClass(out IntPtr window)
        {
            window = GetForegroundWindow();
            var sb = new StringBuilder(128);
            if (window != IntPtr.Zero) GetClassName(window, sb, sb.Capacity);
            return sb.ToString();
        }

        // The desktop, the taskbar or Start, TaskBoost itself, a web browser, File Explorer or a terminal
        static bool OnDesktopOrBrowser()
        {
            IntPtr h;
            string cls = ForegroundClass(out h);
            if (h == IntPtr.Zero || Array.IndexOf(ShellClasses, cls) >= 0 || Array.IndexOf(FriendlyClasses, cls) >= 0) return true;
            int pid;
            GetWindowThreadProcessId(h, out pid);
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return pid == Process.GetCurrentProcess().Id || Array.IndexOf(Browsers, p.ProcessName.ToLowerInvariant()) >= 0;
            }
            catch (Exception) { return false; }
        }

        // Films and games: never walk over a full-screen window
        static bool ForegroundIsFullScreen()
        {
            IntPtr h;
            string cls = ForegroundClass(out h);
            RECT r;
            if (h == IntPtr.Zero || Array.IndexOf(ShellClasses, cls) >= 0 || !GetWindowRect(h, out r)) return false;
            Rectangle screen = Screen.FromHandle(h).Bounds;
            return r.L <= screen.Left && r.T <= screen.Top && r.R >= screen.Right && r.B >= screen.Bottom;
        }

        static int IdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            return GetLastInputInfo(ref info) ? (int)(((uint)Environment.TickCount - info.dwTime) / 1000) : 0;
        }

        // ---- Claude ----

        // The MCP server (a separate copy of the exe started by Claude) keeps touching a file while it is connected
        void UpdateClaude()
        {
            var alive = new FileInfo(Path.Combine(App.DataDir, AliveFile));
            bool now = alive.Exists && (DateTime.UtcNow - alive.LastWriteTimeUtc).TotalSeconds < 12;
            if (now == claude) return;
            claude = now;
            if (claude)
            {
                gone = false;
                Say("Claude is connected. I'm on watch!", "star");
            }
            else Say("Claude disconnected. See you!", "sleepy");
        }

        // Commands from the MCP server: "say|mood|base64 text", "show", "hide"
        void ReadInbox()
        {
            string path = Path.Combine(App.DataDir, InboxFile);
            if (!File.Exists(path)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
                File.Delete(path);
            }
            catch (IOException) { return; } // being written right now; pick it up on the next tick
            foreach (string line in lines)
            {
                string[] p = line.Split('|');
                if (p[0] == "hide") GoAway();
                else if (p[0] == "show") ComeBack();
                else if (p[0] == "say" && p.Length == 3)
                {
                    try { Say(Encoding.UTF8.GetString(Convert.FromBase64String(p[2])), p[1]); }
                    catch (FormatException) { }
                }
            }
        }
    }
}

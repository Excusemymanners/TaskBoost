using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskBoost
{
    enum PopKind { Row, Separator, Caption, Chips, Faces }

    // One part of a PopMenu: a row, a line of small text, a strip of picture buttons or a grid of faces
    class PopItem
    {
        public PopKind Kind;
        public string Key, Title = "", Subtitle = "";
        // The picture of a row or a chip: a spider face (Mood, Costume) or a line icon ("app" is the emblem)
        public string Mood, Costume = "", Icon;
        public Action Run;
        // Live text for a row or a caption, read each time the menu opens; a caption with no text is left out
        public Func<string> Text;
        public Func<bool> Enabled;
        public PopItem[] Chips;
        public Func<string> Current;   // Chips: the key of the one in use
        public string Now;
        public string[] Faces;
        public Action<string> Pick;    // Chips and Faces: what was chosen
        public PopItem Opens;          // Row: unfolds or folds this block instead of closing the menu
        public Anim Open;              // Faces: 0 folded away, 1 fully shown
        public Rectangle Bounds;
        public bool Off;
    }

    // The small dark menu of the buddy and of the tray icon. It is a window of its own that never takes the
    // focus, so it opens the same way whatever program is in front; it closes on a click anywhere else, on Esc,
    // or when the mouse stays away from it.
    class PopMenu : Form
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        const int Columns = 6;
        static int Radius { get { return Theme.Px(10); } }

        readonly List<PopItem> items = new List<PopItem>();
        readonly Dictionary<string, Bitmap> faces = new Dictionary<string, Bitmap>();
        readonly Timer watch = new Timer();
        readonly Anim fade, glide, glow;
        // The highlight under the mouse glides from one item to the next instead of jumping
        RectangleF glideFrom, glideTo;
        PopItem hover;
        int part = -1; // which chip or face of the item under the mouse
        int frame, ticks, away;
        bool leaving, above;
        Rectangle home; // what the menu opened beside
        // One sprite pixel on screen, a face, one square of the face grid, one chip, and the menu's width
        int unit, box, cell, chip, width;

        // Bounds of whatever the menu belongs to (the buddy): a click there is left to it to handle
        public Func<Rectangle> Host = delegate { return Rectangle.Empty; };
        // For the tests and the picture tool, which work the menu without the real mouse: a pinned menu does
        // not close by itself when the real mouse is elsewhere or is clicked
        public bool Pinned = false;
        public event Action Dismissed = delegate { };

        public PopMenu()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            fade = new Anim(0F, Faded);
            glide = new Anim(1F, delegate { Invalidate(); });
            glow = new Anim(0F, delegate { Invalidate(); });
            watch.Interval = 40;
            watch.Tick += delegate { Watch(); };
            Disposed += delegate
            {
                watch.Dispose();
                foreach (Bitmap b in faces.Values) b.Dispose();
            };
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

        public bool IsOpen { get { return Visible && !leaving; } }

        // ---- building ----

        public PopItem Add(string title, Action run)
        {
            var it = new PopItem { Title = title, Run = run };
            items.Add(it);
            return it;
        }

        public void AddSeparator() { items.Add(new PopItem { Kind = PopKind.Separator }); }

        public void AddCaption(Func<string> text) { items.Add(new PopItem { Kind = PopKind.Caption, Text = text }); }

        // A strip of picture buttons, one of which is the one in use; the name and purpose of the chip under
        // the mouse are written below the strip
        public void AddChips(Func<string> current, Action<string> pick, params PopItem[] chips)
        {
            items.Add(new PopItem { Kind = PopKind.Chips, Chips = chips, Current = current, Pick = pick });
        }

        // text is "Title  -  what it is for"
        public static PopItem Chip(string key, string text, string mood, string costume)
        {
            int cut = text.IndexOf("  -  ");
            var c = new PopItem { Key = key, Mood = mood, Costume = costume, Title = cut < 0 ? text : text.Substring(0, cut) };
            if (cut >= 0) c.Subtitle = char.ToUpper(text[cut + 5]) + text.Substring(cut + 6);
            return c;
        }

        // A grid of spider faces, folded away until a row opens it
        public PopItem AddFaces(string[] moods, Action<string> pick)
        {
            var it = new PopItem { Kind = PopKind.Faces, Faces = moods, Pick = pick };
            it.Open = new Anim(0F, delegate { if (Visible) { Stack(); Invalidate(); } });
            items.Add(it);
            return it;
        }

        // ---- opening and closing ----

        // Opens above or below the given rectangle (the buddy, or the point that was clicked)
        public void ShowBeside(Rectangle owner)
        {
            home = owner;
            leaving = false;
            hover = null;
            part = -1;
            away = 0;
            glow.Jump(0F);
            foreach (PopItem it in items)
                if (it.Open != null) it.Open.Jump(0F);
            Arrange();
            fade.Jump(0F);
            if (!Visible) Show();
            fade.To(1F, 150);
            watch.Start();
        }

        public void ShowAt(Point p) { ShowBeside(new Rectangle(p, Size.Empty)); }

        public void Dismiss()
        {
            if (!Visible || leaving) return;
            leaving = true;
            Dismissed();
            fade.To(0F, 90);
        }

        void Faded()
        {
            if (leaving && fade.Value <= 0.01F)
            {
                leaving = false;
                watch.Stop();
                Hide();
                return;
            }
            // Just under 1 keeps the window layered, so nothing flashes when the fade ends
            Opacity = Math.Min(0.995, fade.Value);
            if (Visible || !leaving) Settle(Height);
        }

        void Watch()
        {
            if (leaving) return;
            ticks++;
            Point c = Cursor.Position;
            bool near = Bounds.Contains(c) || Host().Contains(c);
            bool pressed = GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0 || GetAsyncKeyState(4) < 0; // any mouse button down
            if (!Pinned && ((pressed && !near) || GetAsyncKeyState(0x1B) < 0))
            {
                Dismiss();
                return;
            }
            away = near || Pinned ? 0 : away + 1;
            if (away > 200) // eight seconds with the mouse elsewhere
            {
                Dismiss();
                return;
            }
            // The face under the mouse comes alive
            if (ticks % 6 == 0)
            {
                frame ^= 1;
                if (hover != null) Invalidate();
            }
        }

        // ---- layout ----

        static int Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        // Reads the live texts and measures everything; called each time the menu opens
        void Arrange()
        {
            unit = Math.Max(1, (int)Math.Round(Theme.Scale));
            box = BuddySprite.Grid * unit;
            cell = box + Theme.Px(8);
            chip = box + Theme.Px(14);
            int inner = Theme.Px(214);
            foreach (PopItem it in items)
            {
                if (it.Text != null) it.Title = it.Text() ?? "";
                it.Off = it.Enabled != null && !it.Enabled();
                if (it.Kind == PopKind.Row) inner = Math.Max(inner, Theme.Px(16) + box + Measure(it.Title, Theme.Item) + Theme.Px(it.Opens != null ? 90 : 14));
                else if (it.Kind == PopKind.Caption) inner = Math.Max(inner, Theme.Px(32) + Measure(it.Title, Theme.Caption));
                else if (it.Kind == PopKind.Faces) inner = Math.Max(inner, Columns * cell);
                else if (it.Kind == PopKind.Chips)
                {
                    it.Now = it.Current();
                    inner = Math.Max(inner, it.Chips.Length * (chip + Theme.Px(4)));
                    foreach (PopItem c in it.Chips) inner = Math.Max(inner, Theme.Px(16) + Math.Max(Measure(c.Title, Theme.Title), Measure(c.Subtitle, Theme.Body)));
                }
            }
            width = inner + 2 * Theme.Px(6);

            // Above the owner when there is room for it there, below otherwise
            int folded = Stack(false);
            above = home.Top - Theme.Px(6) - folded >= Screen.FromRectangle(home).WorkingArea.Top;
            Stack();
        }

        void Stack() { Settle(Stack(true)); }

        // Puts the items one under the other and returns the height they need
        int Stack(bool keep)
        {
            int pad = Theme.Px(6), y = pad;
            foreach (PopItem it in items)
            {
                int h = it.Kind == PopKind.Separator ? Theme.Px(9)
                      : it.Kind == PopKind.Caption ? (it.Title == "" ? 0 : Theme.Px(26))
                      : it.Kind == PopKind.Chips ? chip + Theme.Px(46)
                      : it.Kind == PopKind.Faces ? (int)Math.Round(((it.Faces.Length + Columns - 1) / Columns * cell + Theme.Px(4)) * Math.Max(0F, Math.Min(1F, it.Open.Value)))
                      : Math.Max(box + Theme.Px(4), Theme.Px(32));
                if (keep) it.Bounds = new Rectangle(pad, y, width - 2 * pad, h);
                y += h;
            }
            return y + pad;
        }

        // Keeps the menu beside what it opened from while it slides in or changes height
        void Settle(int height)
        {
            Rectangle area = Screen.FromRectangle(home).WorkingArea;
            int gap = Theme.Px(6), slide = (int)((1F - fade.Value) * Theme.Px(10));
            int x = home.Left + (home.Width - width) / 2;
            int y = above ? home.Top - gap - height + slide : home.Bottom + gap - slide;
            x = Math.Max(area.Left + gap, Math.Min(area.Right - width - gap, x));
            y = Math.Max(area.Top + gap, Math.Min(area.Bottom - height - gap, y));
            var b = new Rectangle(x, y, width, height);
            if (Bounds == b) return;
            bool resized = Size != b.Size;
            Bounds = b;
            if (resized)
                using (var path = Theme.Round(new RectangleF(0, 0, b.Width, b.Height), Radius)) Region = new Region(path);
        }

        Rectangle ChipCell(PopItem it, int i)
        {
            int n = it.Chips.Length;
            float step = n > 1 ? (it.Bounds.Width - chip) / (float)(n - 1) : 0F;
            return new Rectangle(it.Bounds.X + (int)(i * step), it.Bounds.Y + Theme.Px(2), chip, chip);
        }

        Rectangle FaceCell(PopItem it, int i)
        {
            int left = it.Bounds.X + (it.Bounds.Width - Columns * cell) / 2;
            return new Rectangle(left + i % Columns * cell, it.Bounds.Y + Theme.Px(2) + i / Columns * cell, cell, cell);
        }

        // ---- drawing ----

        Bitmap Face(string mood, string costume, int step, bool walking)
        {
            string key = mood + "|" + costume + "|" + step + "|" + walking + "|" + BuddySprite.Accessory;
            Bitmap b;
            if (faces.TryGetValue(key, out b)) return b;
            b = new Bitmap(BuddySprite.Grid, BuddySprite.Grid);
            bool badge = BuddySprite.NetBadge;
            BuddySprite.NetBadge = false;
            BuddySprite.Draw(b, step, walking, 0, mood, false, costume);
            BuddySprite.NetBadge = badge;
            b.MakeTransparent(BuddySprite.Key);
            faces[key] = b;
            return b;
        }

        // Whole sprite pixels only, so the face stays as sharp as the buddy itself. A lively one moves its legs.
        void DrawFace(Graphics g, string mood, string costume, bool lively, Rectangle slot)
        {
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Face(mood, costume, lively ? frame : 0, lively), new Rectangle(slot.X + (slot.Width - box) / 2, slot.Y + (slot.Height - box) / 2, box, box));
            g.Restore(st);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            if (glow.Value > 0.01F)
                Theme.FillRound(g, Theme.Mix(glideFrom, glideTo, glide.Value), Theme.Px(8), Color.FromArgb((int)(Math.Min(1F, glow.Value) * 255), Theme.CardHover));

            foreach (PopItem it in items)
            {
                Rectangle b = it.Bounds;
                if (b.Height == 0) continue;
                if (it.Kind == PopKind.Separator)
                {
                    using (var p = new Pen(Theme.Border)) g.DrawLine(p, b.X + Theme.Px(6), b.Y + b.Height / 2, b.Right - Theme.Px(6), b.Y + b.Height / 2);
                }
                else if (it.Kind == PopKind.Caption)
                {
                    float d = Theme.Px(7);
                    using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, b.X + Theme.Px(9), b.Y + (b.Height - d) / 2F, d, d);
                    TextRenderer.DrawText(g, it.Title, Theme.Caption, new Rectangle(b.X + Theme.Px(24), b.Y, b.Width - Theme.Px(30), b.Height), Theme.Accent2, flags | TextFormatFlags.VerticalCenter);
                }
                else if (it.Kind == PopKind.Chips) PaintChips(g, it, flags);
                else if (it.Kind == PopKind.Faces)
                {
                    g.SetClip(b);
                    for (int i = 0; i < it.Faces.Length; i++)
                        DrawFace(g, it.Faces[i], "", hover == it && part == i, FaceCell(it, i));
                    g.ResetClip();
                }
                else PaintRow(g, it, flags);
            }
            Theme.DrawRound(g, new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), Radius, Theme.Track, 1F);
        }

        void PaintChips(Graphics g, PopItem it, TextFormatFlags flags)
        {
            Rectangle b = it.Bounds;
            PopItem shown = null;
            for (int i = 0; i < it.Chips.Length; i++)
            {
                PopItem c = it.Chips[i];
                Rectangle r = ChipCell(it, i);
                bool on = c.Key == it.Now, hot = hover == it && part == i;
                if (on)
                {
                    Theme.FillRound(g, r, Theme.Px(9), Theme.TileOn);
                    Theme.DrawRound(g, new RectangleF(r.X + 0.5F, r.Y + 0.5F, r.Width - 1, r.Height - 1), Theme.Px(9), Color.FromArgb(150, Theme.Accent), 1F);
                }
                DrawFace(g, c.Mood, c.Costume, hot, r);
                if (hot || (on && shown == null)) shown = c;
            }
            if (shown == null) return;
            int y = b.Y + chip + Theme.Px(6);
            var line = new Rectangle(b.X + Theme.Px(8), y, b.Width - Theme.Px(16), Theme.Px(20));
            TextRenderer.DrawText(g, shown.Title, Theme.Title, line, shown.Key == it.Now ? Theme.Accent2 : Theme.Text, flags);
            line.Offset(0, Theme.Px(19));
            TextRenderer.DrawText(g, shown.Subtitle, Theme.Body, line, Theme.Muted, flags);
        }

        void PaintRow(Graphics g, PopItem it, TextFormatFlags flags)
        {
            Rectangle b = it.Bounds;
            bool hot = hover == it;
            var slot = new Rectangle(b.X + Theme.Px(6), b.Y + (b.Height - box) / 2, box, box);
            if (it.Mood != null) DrawFace(g, it.Mood, it.Costume, hot, slot);
            else if (it.Icon != null)
            {
                float s = Theme.Px(it.Icon == "app" ? 22 : 18);
                var spot = new RectangleF(slot.X + (box - s) / 2F, slot.Y + (box - s) / 2F, s, s);
                if (it.Icon == "app") Icons.AppIcon(g, spot, Theme.Accent);
                else Icons.Draw(g, it.Icon, spot, hot ? Theme.Text : Theme.Muted, hot ? Theme.CardHover : Theme.Card);
            }

            int x = slot.Right + Theme.Px(10), right = b.Right - Theme.Px(8);
            if (it.Opens != null)
            {
                // An arrow that turns downwards as the block unfolds
                float u = Theme.Scale * 2F, turn = Math.Max(0F, Math.Min(1F, it.Opens.Open.Value));
                var st = g.Save();
                g.TranslateTransform(right - Theme.Px(6), b.Y + b.Height / 2F);
                g.RotateTransform(90F * turn);
                using (var pen = new Pen(Theme.Muted, Theme.Scale * 1.6F))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new[] { new PointF(-u, -2F * u), new PointF(u, 0F), new PointF(-u, 2F * u) });
                }
                g.Restore(st);
                right -= Theme.Px(18);
                // The name of the face under the mouse
                if (hover == it.Opens && part >= 0)
                {
                    string name = it.Opens.Faces[part];
                    TextRenderer.DrawText(g, char.ToUpper(name[0]) + name.Substring(1), Theme.Caption, new Rectangle(x, b.Y, right - x, b.Height), Theme.Accent2,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                }
            }
            TextRenderer.DrawText(g, it.Title, Theme.Item, new Rectangle(x, b.Y, right - x, b.Height), it.Off ? Theme.Muted : Theme.Text, flags | TextFormatFlags.VerticalCenter);
        }

        // ---- the mouse ----

        void Track(Point p)
        {
            PopItem found = null;
            int foundPart = -1;
            RectangleF target = RectangleF.Empty;
            foreach (PopItem it in items)
            {
                if (it.Off || !it.Bounds.Contains(p)) continue;
                if (it.Kind == PopKind.Row)
                {
                    found = it;
                    target = it.Bounds;
                }
                else if (it.Kind == PopKind.Chips || (it.Kind == PopKind.Faces && it.Open.Value > 0.9F))
                {
                    int n = it.Kind == PopKind.Chips ? it.Chips.Length : it.Faces.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Rectangle r = it.Kind == PopKind.Chips ? ChipCell(it, i) : FaceCell(it, i);
                        if (!r.Contains(p)) continue;
                        found = it;
                        foundPart = i;
                        target = r;
                    }
                }
            }
            if (found == hover && foundPart == part) return;
            if (found != null)
            {
                glideFrom = glow.Value > 0.05F ? Theme.Mix(glideFrom, glideTo, glide.Value) : target;
                glideTo = target;
                glide.Jump(0F);
                glide.To(1F, 130);
                glow.To(1F, 110);
            }
            else glow.To(0F, 180);
            hover = found;
            part = foundPart;
            Cursor = found != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) { Track(e.Location); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { Track(new Point(-1, -1)); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || leaving) return;
            Track(e.Location);
            PopItem it = hover;
            int chosen = part;
            if (it == null) return;
            if (it.Kind == PopKind.Row && it.Opens != null)
            {
                it.Opens.Open.To(it.Opens.Open.Target > 0.5F ? 0F : 1F, 220);
                return;
            }
            Dismiss();
            if (it.Kind == PopKind.Chips) it.Pick(it.Chips[chosen].Key);
            else if (it.Kind == PopKind.Faces) it.Pick(it.Faces[chosen]);
            else if (it.Run != null) it.Run();
        }
    }
}

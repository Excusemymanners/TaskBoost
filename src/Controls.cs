using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskBoost
{
    // Owner-drawn, clickable, keyboard-focusable base for the dark controls. It also keeps the two things every
    // one of them animates: the glow as the mouse comes over, and the ripple that spreads from a click.
    abstract class DarkControl : Control
    {
        protected bool Hover, Down;
        protected readonly Anim Glow, Ripple;
        Point rippleAt;

        protected DarkControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Glow = new Anim(0F, Invalidate);
            Ripple = new Anim(1F, Invalidate);
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Glow.To(1F, 120); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Glow.To(0F, 220); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                Down = true;
                StartRipple(e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                StartRipple(new Point(Width / 2, Height / 2));
                OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        void StartRipple(Point at)
        {
            rippleAt = at;
            Ripple.Jump(0F);
            Ripple.To(1F, 450);
        }

        protected RectangleF Bounds1 { get { return new RectangleF(0.5F, 0.5F, Width - 1, Height - 1); } }

        // The ring of light that spreads from where the control was clicked, kept inside its rounded shape
        protected void PaintRipple(Graphics g, RectangleF shape, float radius)
        {
            float t = Ripple.Value;
            if (t >= 1F) return;
            float reach = Math.Max(Width, Height) * (0.15F + t);
            var st = g.Save();
            using (var clip = Theme.Round(shape, radius)) g.SetClip(clip, CombineMode.Intersect);
            using (var b = new SolidBrush(Color.FromArgb((int)(40 * (1F - t)), 255, 255, 255)))
                g.FillEllipse(b, rippleAt.X - reach, rippleAt.Y - reach, 2 * reach, 2 * reach);
            g.Restore(st);
        }

        // Card background, icon tile, title and wrapped subtitle; the right-hand widget is drawn by the caller.
        // on runs from 0 to 1 as the card is switched on, so its colours change smoothly.
        protected void PaintCard(Graphics g, string icon, string title, string subtitle, float on, int rightReserved)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            float radius = Theme.Px(10), glow = Enabled ? Glow.Value : 0F;
            Theme.FillRound(g, Bounds1, radius, Theme.Mix(Theme.Card, Theme.CardHover, glow));
            PaintRipple(g, Bounds1, radius);
            Color edge = Focused && ShowFocusCues ? Theme.Accent2
                : Theme.Mix(Theme.Mix(Theme.Border, Theme.Track, glow), Theme.Mix(Theme.Card, Theme.Accent, 0.5F), on);
            Theme.DrawRound(g, Bounds1, radius, edge, 1F);

            int pad = Theme.Px(12), tile = Theme.Px(40);
            var tileRect = new RectangleF(pad, (Height - tile) / 2F, tile, tile);
            Color tileColor = Theme.Mix(Theme.Tile, Theme.TileOn, on);
            Theme.FillRound(g, tileRect, Theme.Px(9), tileColor);
            float inset = Theme.Px(8);
            Icons.Draw(g, icon, RectangleF.Inflate(tileRect, -inset, -inset), Theme.Mix(Theme.Muted, Theme.Accent, on), tileColor);

            int x = pad + tile + Theme.Px(12);
            int w = Width - x - rightReserved;
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, title, Theme.Title, new Rectangle(x, Theme.Px(11), w, Theme.Px(21)), Enabled ? Theme.Text : Theme.Muted, flags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, subtitle, Theme.Body, new Rectangle(x, Theme.Px(32), w, Height - Theme.Px(38)), Theme.Muted, flags | TextFormatFlags.WordBreak);
        }
    }

    // One tweak group: illustration, name, description and an on/off switch
    class ToggleCard : DarkControl
    {
        readonly string icon, title, subtitle;
        readonly Anim slide;
        bool isChecked;
        public event EventHandler CheckedChanged;

        public ToggleCard(string icon, string title, string subtitle)
        {
            this.icon = icon;
            this.title = Lang.T(title);
            this.subtitle = Lang.T(subtitle);
            slide = new Anim(0F, Invalidate);
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = this.title;
            AccessibleDescription = this.subtitle;
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                // A switch flipped while nobody can see it (another tab, or at start-up) is simply in its new place
                if (IsHandleCreated && Visible) slide.To(value ? 1F : 0F, 240);
                else slide.Jump(value ? 1F : 0F);
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int pad = Theme.Px(12), tw = Theme.Px(36), th = Theme.Px(20);
            float on = slide.Value;
            PaintCard(g, icon, title, subtitle, on, pad + tw + Theme.Px(8));

            var track = new RectangleF(Width - pad - tw, Theme.Px(13), tw, th);
            Theme.FillRound(g, track, th / 2F, Theme.Mix(Theme.Track, Theme.Accent, on));
            // The knob stretches while it travels, like something soft being pushed, and never leaves the track
            float knob = th - Theme.Px(6), gap = Theme.Px(3), travel = tw - knob - 2 * gap;
            float stretch = knob * 0.45F * (float)Math.Sin(Math.PI * on);
            using (var b = new SolidBrush(Theme.Mix(Theme.Text, Theme.OnAccent, on)))
            using (var path = Theme.Round(new RectangleF(track.X + gap + (travel - stretch) * on, track.Y + gap, knob + stretch, knob), knob / 2F))
                g.FillPath(b, path);
        }
    }

    // One of the two "when to run" choices
    class OptionCard : DarkControl
    {
        readonly string icon, title, subtitle;
        readonly Anim pick;
        bool selected;

        public OptionCard(string icon, string title, string subtitle)
        {
            this.icon = icon;
            this.title = Lang.T(title);
            this.subtitle = Lang.T(subtitle);
            pick = new Anim(0F, Invalidate);
            pick.Springy = true; // the dot pops in a little too big, then settles
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = this.title;
            AccessibleDescription = this.subtitle;
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                if (IsHandleCreated && Visible) pick.To(value ? 1F : 0F, 300);
                else pick.Jump(value ? 1F : 0F);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int pad = Theme.Px(14), d = Theme.Px(18);
            float on = Math.Max(0F, Math.Min(1F, pick.Value));
            PaintCard(g, icon, title, subtitle, on, pad + d + Theme.Px(8));

            var ring = new RectangleF(Width - pad - d, (Height - d) / 2F, d, d);
            using (var p = new Pen(Theme.Mix(Theme.Track, Theme.Accent, on), Theme.Px(2))) g.DrawEllipse(p, ring);
            float dot = (d - Theme.Px(9)) * Math.Max(0F, pick.Value);
            if (dot > 0.5F)
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillEllipse(b, ring.X + (d - dot) / 2F, ring.Y + (d - dot) / 2F, dot, dot);
        }
    }

    class FlatButton : DarkControl
    {
        public bool Primary;
        readonly Timer spinner = new Timer();
        bool busy;
        float turn;

        public FlatButton()
        {
            AccessibleRole = AccessibleRole.PushButton;
            spinner.Interval = 30;
            spinner.Tick += delegate
            {
                turn = (turn + 14F) % 360F;
                Invalidate();
            };
            Disposed += delegate { spinner.Dispose(); };
        }

        // Shows a turning arc next to the text while work is going on
        public bool Busy
        {
            get { return busy; }
            set
            {
                busy = value;
                spinner.Enabled = value && Motion.On;
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            AccessibleName = Lang.T(Text);
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            float radius = Theme.Px(8), glow = Enabled ? Glow.Value : 0F;
            bool pressed = Down && Hover && Enabled;
            Color text;
            if (!Enabled && !busy)
            {
                Theme.FillRound(g, Bounds1, radius, Theme.Card);
                text = Theme.Muted;
            }
            else if (Primary)
            {
                // A little lighter at the top; lighter still under the mouse, darker while held down
                float lift = Enabled ? (pressed ? -0.10F : 0.12F * glow) : -0.25F;
                Color top = Shade(Theme.Mix(Theme.Accent, Theme.Accent2, 0.35F), lift), bottom = Shade(Theme.Accent, lift);
                using (var path = Theme.Round(Bounds1, radius))
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, Width, Height + 2), top, bottom, 90F)) g.FillPath(b, path);
                text = Theme.OnAccent;
            }
            else
            {
                Theme.FillRound(g, Bounds1, radius, pressed ? Theme.Mix(Theme.CardHover, Theme.Bg, 0.4F) : Theme.Mix(Theme.Card, Theme.CardHover, glow));
                text = Enabled ? Theme.Text : Theme.Muted;
            }
            PaintRipple(g, Bounds1, radius);
            if (Focused && ShowFocusCues) Theme.DrawRound(g, Bounds1, radius, Theme.Accent2, 1F);
            else if (!Primary || (!Enabled && !busy)) Theme.DrawRound(g, Bounds1, radius, Theme.Mix(Theme.Border, Theme.Track, glow), 1F);

            string label = Lang.T(Text);
            var area = new Rectangle(0, pressed ? 1 : 0, Width, Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (!busy)
            {
                TextRenderer.DrawText(g, label, Theme.Title, area, text, flags | TextFormatFlags.HorizontalCenter);
                return;
            }
            int d = Theme.Px(14), gap = Theme.Px(8);
            int tw = TextRenderer.MeasureText(g, label, Theme.Title, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int x = Math.Max(Theme.Px(6), (Width - d - gap - tw) / 2);
            using (var p = new Pen(text, Theme.Px(2)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawArc(p, x, (Height - d) / 2F, d, d, turn, 270F);
            }
            TextRenderer.DrawText(g, label, Theme.Title, new Rectangle(x + d + gap, 0, Width - x - d - gap, Height), text, flags | TextFormatFlags.NoPadding);
        }

        // Towards white for a positive amount, towards black for a negative one
        static Color Shade(Color c, float amount)
        {
            return amount >= 0 ? Theme.Mix(c, Color.White, amount) : Theme.Mix(c, Color.Black, -amount);
        }
    }

    // The tabs: one rounded track, with a highlight that slides to the tab that is opened
    class TabBar : DarkControl
    {
        readonly string[] names;
        readonly int[] count, level;
        readonly bool[] lit;
        readonly Anim slide;
        int selected, hot = -1;

        // The user chose a tab (by mouse or arrow keys)
        public event Action<int> Picked;

        public TabBar(string[] names)
        {
            this.names = names;
            count = new int[names.Length];
            level = new int[names.Length];
            lit = new bool[names.Length];
            slide = new Anim(0F, Invalidate);
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                if (IsHandleCreated && Visible) slide.To(value, 280);
                else slide.Jump(value);
                Invalidate();
            }
        }

        // A small numbered mark on a tab (alerts and notices on Guard); zero takes it away
        public void SetBadge(int tab, int number, int badgeLevel)
        {
            if (count[tab] == number && level[tab] == badgeLevel) return;
            count[tab] = number;
            level[tab] = badgeLevel;
            Invalidate();
        }

        // A small light on a tab (gaming mode is on)
        public void SetLight(int tab, bool on)
        {
            if (lit[tab] == on) return;
            lit[tab] = on;
            Invalidate();
        }

        float Pad { get { return Theme.Px(3); } }
        float TabWidth { get { return (Width - 2 * Pad) / names.Length; } }

        int TabAt(int x) { return Math.Max(0, Math.Min(names.Length - 1, (int)((x - Pad) / TabWidth))); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int now = TabAt(e.X);
            if (now != hot)
            {
                hot = now;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Pick(TabAt(e.X));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Pick((selected + names.Length - 1) % names.Length);
            else if (e.KeyCode == Keys.Right) Pick((selected + 1) % names.Length);
            else base.OnKeyDown(e);
        }

        void Pick(int tab)
        {
            if (tab == selected) return;
            Selected = tab;
            if (Picked != null) Picked(tab);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            float radius = Theme.Px(10), pad = Pad, w = TabWidth, inner = radius - pad + 1;
            Theme.FillRound(g, Bounds1, radius, Theme.Card);
            Theme.DrawRound(g, Bounds1, radius, Focused && ShowFocusCues ? Theme.Accent2 : Theme.Border, 1F);

            if (hot >= 0 && hot != selected)
                Theme.FillRound(g, new RectangleF(pad + hot * w, pad, w, Height - 2 * pad), inner, Theme.Mix(Theme.Card, Theme.CardHover, Glow.Value));

            var pill = new RectangleF(pad + slide.Value * w, pad, w, Height - 2 * pad);
            using (var path = Theme.Round(pill, inner))
            using (var b = new LinearGradientBrush(new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height + 2), Theme.Mix(Theme.Accent, Theme.Accent2, 0.35F), Theme.Accent, 90F))
                g.FillPath(b, path);

            for (int i = 0; i < names.Length; i++)
            {
                string label = Lang.T(names[i]);
                var cell = new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
                // How much of this tab the highlight covers decides how white its label is
                float covered = Math.Max(0F, 1F - Math.Abs(slide.Value - i));
                Color ink = Theme.Mix(i == hot ? Theme.Text : Theme.LogText, Theme.OnAccent, covered);
                int mark = count[i] > 0 ? Theme.Px(18) : lit[i] ? Theme.Px(8) : 0, gap = mark > 0 ? Theme.Px(5) : 0;
                int tw = Math.Min((int)w - mark - gap - Theme.Px(6), TextRenderer.MeasureText(g, label, Theme.Title, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
                int x = (int)(cell.X + (w - tw - gap - mark) / 2F);
                TextRenderer.DrawText(g, label, Theme.Title, new Rectangle(x, (int)cell.Y, tw, (int)cell.Height), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (count[i] > 0)
                {
                    var spot = new RectangleF(x + tw + gap, cell.Y + (cell.Height - mark) / 2F, mark, mark);
                    Theme.FillRound(g, spot, mark / 2F, Theme.LevelColor(level[i], Theme.Accent2));
                    TextRenderer.DrawText(g, count[i] > 9 ? "9+" : count[i].ToString(), Theme.Caption, Rectangle.Round(spot), Theme.Bg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                else if (lit[i])
                    using (var b = new SolidBrush(Theme.Mix(Theme.Accent2, Theme.OnAccent, covered)))
                        g.FillEllipse(b, x + tw + gap, cell.Y + (cell.Height - mark) / 2F, mark, mark);
            }
        }
    }

    // Emblem, name and guard status. While there is something to look at on the Guard tab, the emblem glows
    // and the status dot sends out rings.
    class HeaderPanel : Control
    {
        readonly Timer pulse = new Timer();
        string status = "";
        int level;
        float beat;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            pulse.Interval = 40;
            pulse.Tick += delegate
            {
                beat = (beat + 0.025F) % 1F;
                Invalidate();
            };
            Disposed += delegate { pulse.Dispose(); };
        }

        // level: 0 calm, 1 notice, 2 alert; it colours the emblem's rim and the status line
        public void SetStatus(string text, int newLevel)
        {
            if (text == status && newLevel == level) return;
            status = text;
            level = newLevel;
            UpdatePulse();
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdatePulse(); }

        // The pulse only runs while it can be seen and there is something to draw attention to
        void UpdatePulse() { pulse.Enabled = level > 0 && Visible && Motion.On; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            Color mood = Theme.LevelColor(level, Theme.Accent);

            int logo = Theme.Px(60), margin = Theme.Px(6);
            var emblem = new RectangleF(margin, (Height - logo) / 2F, logo, logo);
            if (level > 0)
            {
                // A soft halo that breathes
                float breath = 0.55F + 0.45F * (float)Math.Sin(beat * 2 * Math.PI);
                for (int k = 3; k >= 1; k--)
                    Theme.DrawRound(g, RectangleF.Inflate(emblem, k * Theme.Scale * 1.6F, k * Theme.Scale * 1.6F), logo * 0.24F + k * Theme.Scale * 1.6F,
                        Color.FromArgb((int)(70 * breath / k), mood), Theme.Scale * 2F);
            }
            Icons.AppIcon(g, emblem, mood);

            int x = margin + logo + Theme.Px(14);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, App.Name, Theme.Header, new Point(x, Height / 2 - Theme.Px(30)), Theme.Text, flags);

            // Status dot, with a ring spreading from it while it wants attention
            float d = Theme.Px(7), cy = Height / 2F + Theme.Px(15), cx = x + Theme.Px(2) + d / 2F;
            if (level > 0)
            {
                float reach = d / 2F + beat * Theme.Px(9);
                using (var p = new Pen(Color.FromArgb((int)(190 * (1F - beat)), mood), Theme.Scale * 1.5F)) g.DrawEllipse(p, cx - reach, cy - reach, 2 * reach, 2 * reach);
            }
            using (var b = new SolidBrush(mood)) g.FillEllipse(b, cx - d / 2F, cy - d / 2F, d, d);
            TextRenderer.DrawText(g, status, Theme.Body, new Rectangle(x + Theme.Px(15), Height / 2 + Theme.Px(6), Width - x - Theme.Px(15), Theme.Px(18)), level == 0 ? Theme.Muted : mood, flags | TextFormatFlags.EndEllipsis);
        }
    }

    // A list of findings: one row each, coloured by level. Rows light up under the mouse, freshly listed rows
    // fade in one after the other, and a row with a Bar value (the health score parts) grows a small gauge.
    class FindingList : ListBox
    {
        readonly int lines;
        readonly Anim reveal;
        int hot = -1;

        public FindingList() : this(2) { }

        // lines: 2 = title and detail; 3 = also the Extra line
        public FindingList(int lines)
        {
            this.lines = lines;
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            BackColor = Theme.LogBg;
            ItemHeight = Theme.Px(lines == 3 ? 62 : 44);
            // Painted here, into a buffer, instead of row by row by Windows: no flicker when rows change or animate
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            reveal = new Anim(1F, Invalidate);
        }

        // Plays the fade-in for rows that were just listed
        public void Reveal()
        {
            reveal.Jump(0F);
            reveal.To(1F, Math.Min(900, 350 + Items.Count * 45));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowTheme(Handle, "DarkMode_Explorer", null); // dark scrollbar
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = IndexFromPoint(e.Location);
            if (now == ListBox.NoMatches) now = -1;
            if (now == hot) return;
            hot = now;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot < 0) return;
            hot = -1;
            Invalidate();
        }

        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.LogBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = TopIndex; i < Items.Count; i++)
            {
                Rectangle r = GetItemRectangle(i);
                if (r.Top >= ClientSize.Height) break;
                if (e.ClipRectangle.IntersectsWith(r)) PaintRow(g, i, r);
            }
        }

        void PaintRow(Graphics g, int index, Rectangle r)
        {
            var f = Items[index] as Finding;
            if (f == null) return;
            bool selected = index == SelectedIndex;
            // Rows appear one after the other: each starts a little later than the one above
            float shown = reveal.Value >= 1F ? 1F : Math.Max(0F, Math.Min(1F, reveal.Value * (Items.Count + 3) / 3F - index / 3F));

            Color back = Theme.LogBg;
            var body = new RectangleF(r.X + Theme.Px(1), r.Y + Theme.Px(1), r.Width - Theme.Px(3), r.Height - Theme.Px(2));
            if (selected) back = Color.FromArgb(12, 27, 60);
            else if (index == hot) back = Color.FromArgb(10, 13, 21);
            if (back != Theme.LogBg) Theme.FillRound(g, body, Theme.Px(8), back);
            if (selected) Theme.FillRound(g, new RectangleF(body.X + Theme.Px(2), body.Y + Theme.Px(9), Theme.Px(3), body.Height - Theme.Px(18)), Theme.Px(1.5F), Theme.Accent);

            float d = Theme.Px(9), dx = r.X + Theme.Px(13), dy = r.Y + Theme.Px(lines == 3 ? 12 : (44 - 9) / 2F);
            Color mark = Theme.Mix(back, Theme.LevelColor(f.Level, Theme.Track), shown);
            if (f.Level > 0)
                using (var halo = new SolidBrush(Color.FromArgb((int)(45 * shown), mark))) g.FillEllipse(halo, dx - d * 0.45F, dy - d * 0.45F, d * 1.9F, d * 1.9F);
            using (var b = new SolidBrush(mark)) g.FillEllipse(b, dx, dy, d, d);

            int x = r.X + Theme.Px(34), w = r.Width - Theme.Px(44);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            int titleW = w;
            if (f.Tag != null)
            {
                // Verdict label on the right of the title line
                int tagW = TextRenderer.MeasureText(g, f.Tag, Theme.Caption).Width + Theme.Px(12);
                var tag = new RectangleF(r.Right - Theme.Px(12) - tagW, r.Y + Theme.Px(6), tagW, Theme.Px(19));
                Color c = Theme.LevelColor(f.Level, f.Tag == "Keep" || f.Tag == "Useful" ? Theme.Accent2 : Theme.Muted);
                Color fill = Theme.Mix(back, c, 0.17F * shown);
                Theme.FillRound(g, tag, Theme.Px(9.5F), fill);
                TextRenderer.DrawText(g, Lang.T(f.Tag), Theme.Caption, Rectangle.Round(tag), Theme.Mix(fill, c, shown), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                titleW = w - tagW - Theme.Px(12);
                if (f.Bar >= 0F)
                {
                    // A gauge under the label that fills as the row appears
                    var gauge = new RectangleF(tag.X, tag.Bottom + Theme.Px(6), tag.Width, Theme.Px(4));
                    Theme.FillRound(g, gauge, Theme.Px(2), Theme.Mix(back, Theme.Track, 0.6F));
                    float fill2 = gauge.Width * Math.Min(1F, f.Bar) * Motion.EaseOut(shown);
                    if (fill2 >= gauge.Height) Theme.FillRound(g, new RectangleF(gauge.X, gauge.Y, fill2, gauge.Height), Theme.Px(2), Theme.LevelColor(f.Level, Theme.Accent));
                }
            }
            Color title = f.Off ? Theme.Muted : f.Level == 0 && !selected ? Theme.LogText : Theme.Text;
            TextRenderer.DrawText(g, f.Title, Theme.Title, new Rectangle(x, r.Y + Theme.Px(4), titleW, Theme.Px(22)), Theme.Mix(back, title, shown), flags);
            TextRenderer.DrawText(g, f.Detail, Theme.Body, new Rectangle(x, r.Y + Theme.Px(25), w, Theme.Px(16)), Theme.Mix(back, f.Extra != null ? Theme.LogText : Theme.Muted, shown), flags);
            if (f.Extra != null) TextRenderer.DrawText(g, f.Extra, Theme.Body, new Rectangle(x, r.Y + Theme.Px(42), w, Theme.Px(16)), Theme.Mix(back, Theme.Muted, shown), flags);
        }
    }

    // Shown over the pages for a moment while one tab gives way to another: the old page fades out as it slides
    // away and the new one slides in. Both are pictures of the real pages, so nothing flickers.
    class PageSlide : Control
    {
        readonly Anim progress;
        Bitmap from, to;
        int direction;

        public PageSlide()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Visible = false;
            progress = new Anim(1F, Step);
            Disposed += delegate { Drop(); };
        }

        // direction: 1 when the new page is to the right of the old one, -1 when it is to the left
        public void Run(Bitmap oldPage, Bitmap newPage, int towards)
        {
            Drop();
            from = oldPage;
            to = newPage;
            direction = towards;
            progress.Jump(0F);
            Visible = true;
            BringToFront();
            progress.To(1F, 240);
        }

        void Step()
        {
            if (progress.Value >= 1F && Visible)
            {
                Visible = false;
                Drop();
            }
            else Invalidate();
        }

        void Drop()
        {
            if (from != null) from.Dispose();
            if (to != null) to.Dispose();
            from = to = null;
        }

        static void Draw(Graphics g, Bitmap page, float x, float alpha)
        {
            if (page == null || alpha <= 0.01F) return;
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                var matrix = new System.Drawing.Imaging.ColorMatrix();
                matrix.Matrix33 = Math.Min(1F, alpha);
                attributes.SetColorMatrix(matrix);
                g.DrawImage(page, new Rectangle((int)x, 0, page.Width, page.Height), 0, 0, page.Width, page.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            float t = progress.Value, travel = Theme.Px(46);
            Draw(g, from, -direction * travel * t, 1F - t * 1.6F);
            Draw(g, to, direction * travel * (1F - t), t);
        }
    }

    static class Ui
    {
        public static Label Caption(string text)
        {
            var l = new Label();
            l.Text = Lang.T(text);
            l.AutoSize = true;
            l.Font = Theme.Caption;
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Bg;
            return l;
        }

        // The dark boxes that hold a log or a list
        public static Panel Well(Control inside, int padding)
        {
            var p = new Panel();
            p.BackColor = Theme.LogBg;
            p.Padding = new Padding(Theme.Px(padding));
            inside.Dock = DockStyle.Fill;
            p.Controls.Add(inside);
            return p;
        }
    }
}

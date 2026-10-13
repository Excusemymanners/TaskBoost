using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskBoost
{
    // TaskBoost's own message boxes, in its own colours: a message with OK, a question with Yes and No,
    // a one-line text prompt, and the list of keyboard shortcuts.
    class Dialog : Form
    {
        // The main window. A dialog asked for from a worker thread is shown on this window's thread.
        public static Form Home;

        readonly List<FlatButton> buttons = new List<FlatButton>();
        readonly string message;
        readonly string[][] keys; // shortcut list: { keys, what they do }, or { heading }
        readonly TextBox box;
        Size body;

        Dialog(string text, bool question, string[][] shortcuts, string prompt)
        {
            message = text ?? "";
            keys = shortcuts;
            Text = prompt ?? App.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = ShowIcon = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            KeyPreview = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            int pad = Theme.Px(20), bw = Theme.Px(104), bh = Theme.Px(36), gap = Theme.Px(10);
            if (prompt != null)
            {
                body = new Size(Theme.Px(400), Theme.Px(40));
                box = new TextBox();
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Theme.Tile;
                box.ForeColor = Theme.Text;
                box.Font = Theme.Item;
                box.SetBounds(pad + Theme.Px(12), pad + (Theme.Px(40) - box.PreferredHeight) / 2, body.Width - Theme.Px(24), box.PreferredHeight);
                box.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter) return;
                    e.SuppressKeyPress = true; // no "ding"
                    DialogResult = DialogResult.Yes;
                };
                Controls.Add(box);
            }
            else if (keys != null)
            {
                int height = 0;
                foreach (string[] row in keys) height += Theme.Px(row.Length == 1 ? (row[0] == "" ? 10 : 30) : 30);
                body = new Size(Theme.Px(470), height);
            }
            else
            {
                int textWidth = Theme.Px(380);
                Size measured = TextRenderer.MeasureText(message, Theme.Item, new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                body = new Size(Theme.Px(58) + Math.Max(Theme.Px(250), measured.Width), Math.Max(Theme.Px(46), measured.Height));
            }

            string[] labels = question ? new[] { "Yes", "No" } : prompt != null ? new[] { "Search", "Cancel" } : new[] { "OK" };
            ClientSize = new Size(body.Width + 2 * pad, pad + body.Height + Theme.Px(28) + bh + pad);
            for (int i = 0; i < labels.Length; i++)
            {
                var b = new FlatButton();
                b.Text = labels[i];
                b.Primary = i == 0;
                b.SetBounds(ClientSize.Width - pad - (labels.Length - i) * bw - (labels.Length - 1 - i) * gap, ClientSize.Height - pad - bh, bw, bh);
                bool accept = i == 0;
                b.Click += delegate { DialogResult = accept ? DialogResult.Yes : DialogResult.No; };
                buttons.Add(b);
                Controls.Add(b);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = box != null ? (Control)box : buttons[0];
            Activate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.No;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            int pad = Theme.Px(20);
            if (box != null)
            {
                var field = new RectangleF(pad + 0.5F, pad + 0.5F, body.Width - 1, body.Height - 1);
                Theme.FillRound(g, field, Theme.Px(8), Theme.Tile);
                Theme.DrawRound(g, field, Theme.Px(8), Theme.Accent, 1F);
            }
            else if (keys != null) PaintKeys(g, pad);
            else
            {
                Icons.AppIcon(g, new RectangleF(pad, pad, Theme.Px(44), Theme.Px(44)), Theme.Accent);
                TextRenderer.DrawText(g, message, Theme.Item, new Rectangle(pad + Theme.Px(58), pad, body.Width - Theme.Px(58), body.Height), Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | (body.Height <= Theme.Px(46) ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top));
            }
            // A hairline above the buttons
            int line = ClientSize.Height - pad - Theme.Px(36) - Theme.Px(14);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, line, ClientSize.Width, line);
        }

        // Each shortcut as key caps followed by what it does
        void PaintKeys(Graphics g, int pad)
        {
            int y = pad;
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            foreach (string[] row in keys)
            {
                if (row.Length == 1)
                {
                    if (row[0] == "") { y += Theme.Px(10); continue; }
                    TextRenderer.DrawText(g, row[0], Theme.Caption, new Rectangle(pad, y, body.Width, Theme.Px(30)), Theme.Muted, flags);
                    y += Theme.Px(30);
                    continue;
                }
                int x = pad;
                // "Ctrl+Alt+F1" is three caps; a range such as "Ctrl+1 ... Ctrl+6" stays in one piece
                foreach (string key in row[0].Contains(" ") ? new[] { row[0] } : row[0].Split('+'))
                {
                    string cap = key.Trim();
                    int w = Math.Max(Theme.Px(30), TextRenderer.MeasureText(g, cap, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Px(14));
                    var r = new RectangleF(x + 0.5F, y + Theme.Px(4) + 0.5F, w, Theme.Px(22));
                    Theme.FillRound(g, r, Theme.Px(5), Theme.Tile);
                    Theme.DrawRound(g, r, Theme.Px(5), Theme.Track, 1F);
                    TextRenderer.DrawText(g, cap, Theme.Caption, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    x += w + Theme.Px(5);
                }
                int textX = pad + Theme.Px(190);
                TextRenderer.DrawText(g, row[1], Theme.Item, new Rectangle(textX, y, pad + body.Width - textX, Theme.Px(30)), Theme.LogText, flags);
                y += Theme.Px(30);
            }
        }

        // ---- the ways to open one ----

        public static void Show(IWin32Window owner, string text) { Run(owner, text, false); }
        public static bool Ask(IWin32Window owner, string text) { return Run(owner, text, true); }

        static bool Run(IWin32Window owner, string text, bool question)
        {
            Form home = Home;
            if (home != null && home.IsHandleCreated && home.InvokeRequired)
                return (bool)home.Invoke(new Func<bool>(delegate { return Run(home, text, question); }));
            using (var d = new Dialog(Lang.T(text), question, null, null)) return d.Open(owner) == DialogResult.Yes;
        }

        // What the user typed, or null when the prompt was closed without searching
        public static string Prompt(string title, Rectangle near)
        {
            using (var d = new Dialog(null, false, null, title))
            {
                d.StartPosition = FormStartPosition.Manual;
                d.TopMost = true;
                Rectangle area = Screen.FromRectangle(near).WorkingArea;
                d.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - d.Width, near.Left + (near.Width - d.Width) / 2)),
                                       Math.Max(area.Top, Math.Min(area.Bottom - d.Height, near.Top - d.Height - Theme.Px(8))));
                return d.ShowDialog() == DialogResult.Yes ? d.box.Text.Trim() : null;
            }
        }

        // The shortcut manual: lines of "keys <tab> what they do", headings ending in a colon
        public static void Shortcuts(IWin32Window owner, string manual)
        {
            var rows = new List<string[]>();
            foreach (string line in manual.Split('\n'))
            {
                string[] p = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2) rows.Add(new[] { p[0], p[p.Length - 1] });
                else rows.Add(new[] { line.Trim().TrimEnd(':').ToUpper() });
            }
            using (var d = new Dialog(null, false, rows.ToArray(), null)) d.Open(owner);
        }

        // Over its owner when that is on screen; otherwise in the middle of the screen, on top
        DialogResult Open(IWin32Window owner)
        {
            var form = owner as Form;
            if (form == null && owner is Control) form = ((Control)owner).FindForm();
            if (form != null && form.Visible && form.WindowState != FormWindowState.Minimized)
            {
                StartPosition = FormStartPosition.CenterParent;
                return ShowDialog(form);
            }
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            return ShowDialog();
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskBoost
{
    static class Theme
    {
        // Palette taken from the emblem: near-black, electric blue, white
        public static readonly Color Bg = Color.FromArgb(5, 7, 11);
        public static readonly Color Card = Color.FromArgb(13, 17, 26);
        public static readonly Color CardHover = Color.FromArgb(20, 27, 42);
        public static readonly Color Border = Color.FromArgb(28, 37, 56);
        public static readonly Color Tile = Color.FromArgb(22, 29, 44);
        public static readonly Color TileOn = Color.FromArgb(12, 38, 92);
        public static readonly Color Track = Color.FromArgb(46, 56, 78);
        public static readonly Color Text = Color.FromArgb(240, 244, 250);
        public static readonly Color Muted = Color.FromArgb(128, 142, 168);
        public static readonly Color Accent = Color.FromArgb(38, 120, 255);
        public static readonly Color Accent2 = Color.FromArgb(64, 178, 255);
        public static readonly Color OnAccent = Color.White;
        public static readonly Color Alert = Color.FromArgb(240, 84, 84);
        public static readonly Color Notice = Color.FromArgb(240, 180, 41);
        public static readonly Color LogBg = Color.FromArgb(1, 2, 4);
        public static readonly Color LogText = Color.FromArgb(160, 174, 198);

        public static readonly Font Header = new Font("Segoe UI Semibold", 17F);
        public static readonly Font Title = new Font("Segoe UI Semibold", 10F);
        public static readonly Font Body = new Font("Segoe UI", 8.25F);
        public static readonly Font Item = new Font("Segoe UI", 9.5F);
        public static readonly Font Caption = new Font("Segoe UI Semibold", 8F);
        public static readonly Font Mono = new Font("Consolas", 9F);

        public static Color LevelColor(int level, Color calm) { return level >= 2 ? Alert : level == 1 ? Notice : calm; }

        // A colour part of the way from a to b: how hover glows and switches change colour smoothly
        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0F, Math.Min(1F, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static RectangleF Mix(RectangleF a, RectangleF b, float t)
        {
            return new RectangleF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);
        }

        // Layout is written in 96-dpi pixels and scaled to the system DPI
        public static float Scale = 1F;
        public static int Px(float v) { return (int)Math.Round(v * Scale); }

        // Reads the screen scale. Call it before the first control is made: sizes are worked out as controls are built.
        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96F;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color fill)
        {
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }

        public static void DrawRound(Graphics g, RectangleF r, float radius, Color stroke, float width)
        {
            using (var path = Round(r, radius))
            using (var p = new Pen(stroke, width)) g.DrawPath(p, path);
        }
    }

    // Line illustrations drawn on a 24x24 grid, so they stay sharp at any DPI and need no image files
    static class Icons
    {
        static readonly PointF[] BoltShape =
        {
            new PointF(13.5F, 2F), new PointF(5F, 13.5F), new PointF(11F, 13.5F),
            new PointF(10F, 22F), new PointF(19F, 10F), new PointF(13F, 10F)
        };

        public static void Bolt(Graphics g, RectangleF r, Color c)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24F, r.Height / 24F);
            using (var b = new SolidBrush(c)) g.FillPolygon(b, BoltShape);
            g.Restore(st);
        }

        // bg is the colour behind the icon, used to cut shapes out
        public static void Draw(Graphics g, string key, RectangleF r, Color fg, Color bg)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24F, r.Height / 24F);
            using (var pen = new Pen(fg, 1.8F))
            using (var brush = new SolidBrush(fg))
            using (var cut = new SolidBrush(bg))
            using (var cutPen = new Pen(bg, 5F))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (key)
                {
                    case "theme": // crescent moon
                        g.FillEllipse(brush, 4F, 4F, 16F, 16F);
                        g.FillEllipse(cut, 9.5F, 1.5F, 14F, 14F);
                        break;
                    case "animations": // play triangle, struck through
                        g.DrawPolygon(pen, new[] { new PointF(8F, 6F), new PointF(18F, 12F), new PointF(8F, 18F) });
                        g.DrawLine(cutPen, 4F, 20F, 20F, 4F);
                        g.DrawLine(pen, 4F, 20F, 20F, 4F);
                        break;
                    case "background": // stacked layers
                        g.DrawPolygon(pen, new[] { new PointF(12F, 4F), new PointF(20F, 8.5F), new PointF(12F, 13F), new PointF(4F, 8.5F) });
                        g.DrawLines(pen, new[] { new PointF(4F, 12.5F), new PointF(12F, 17F), new PointF(20F, 12.5F) });
                        g.DrawLines(pen, new[] { new PointF(4F, 16.5F), new PointF(12F, 21F), new PointF(20F, 16.5F) });
                        break;
                    case "taskbar": // screen with a thin bar at the bottom
                        using (var p = Theme.Round(new RectangleF(3F, 5F, 18F, 14F), 2.5F)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 3.5F, 15.5F, 20.5F, 15.5F);
                        g.FillEllipse(brush, 5.2F, 16.4F, 1.6F, 1.6F);
                        g.FillEllipse(brush, 8F, 16.4F, 1.6F, 1.6F);
                        break;
                    case "power": // battery
                        using (var p = Theme.Round(new RectangleF(3F, 8F, 16F, 9F), 2.2F)) g.DrawPath(pen, p);
                        g.FillRectangle(brush, 20.2F, 10.5F, 1.6F, 4F);
                        g.FillRectangle(brush, 5.4F, 10.4F, 7F, 4.2F);
                        break;
                    case "autostart": // browser window with a cross
                        using (var p = Theme.Round(new RectangleF(3F, 5F, 18F, 14F), 2.5F)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 3.5F, 9F, 20.5F, 9F);
                        g.DrawLine(pen, 10F, 11.8F, 14F, 15.8F);
                        g.DrawLine(pen, 14F, 11.8F, 10F, 15.8F);
                        break;
                    case "services": // gear
                        g.DrawEllipse(pen, 7.5F, 7.5F, 9F, 9F);
                        using (var tooth = new Pen(fg, 2.8F))
                            for (int k = 0; k < 8; k++)
                            {
                                double a = k * Math.PI / 4;
                                float cx = (float)Math.Cos(a), cy = (float)Math.Sin(a);
                                g.DrawLine(tooth, 12F + cx * 5.4F, 12F + cy * 5.4F, 12F + cx * 8.4F, 12F + cy * 8.4F);
                            }
                        break;
                    case "textboot": // terminal prompt
                        using (var p = Theme.Round(new RectangleF(3F, 5F, 18F, 14F), 2.5F)) g.DrawPath(pen, p);
                        g.DrawLines(pen, new[] { new PointF(7F, 9.5F), new PointF(10F, 12F), new PointF(7F, 14.5F) });
                        g.DrawLine(pen, 12.5F, 14.5F, 16.5F, 14.5F);
                        break;
                    case "startmenu": // four squares
                        foreach (float x in new[] { 4F, 13F })
                            foreach (float y in new[] { 4F, 13F })
                                using (var p = Theme.Round(new RectangleF(x, y, 7F, 7F), 1.5F)) g.FillPath(brush, p);
                        break;
                    case "manual": // play button
                        g.DrawEllipse(pen, 3F, 3F, 18F, 18F);
                        g.FillPolygon(brush, new[] { new PointF(10F, 8F), new PointF(16.5F, 12F), new PointF(10F, 16F) });
                        break;
                    case "auto": // power-on symbol
                    case "startup":
                        g.DrawArc(pen, 5F, 6F, 14F, 14F, -60F, 300F);
                        g.DrawLine(pen, 12F, 3.5F, 12F, 11.5F);
                        break;
                    case "game": // gamepad
                        using (var p = Theme.Round(new RectangleF(2.5F, 7F, 19F, 10.5F), 5F)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 7.5F, 10.3F, 7.5F, 14.3F);
                        g.DrawLine(pen, 5.5F, 12.3F, 9.5F, 12.3F);
                        g.FillEllipse(brush, 14F, 10.2F, 2F, 2F);
                        g.FillEllipse(brush, 16.6F, 12.6F, 2F, 2F);
                        break;
                    case "trash":
                        g.DrawLine(pen, 4.5F, 7F, 19.5F, 7F);
                        g.DrawLines(pen, new[] { new PointF(9.5F, 7F), new PointF(9.5F, 4.2F), new PointF(14.5F, 4.2F), new PointF(14.5F, 7F) });
                        g.DrawLines(pen, new[] { new PointF(6.3F, 7F), new PointF(7.4F, 20F), new PointF(16.6F, 20F), new PointF(17.7F, 7F) });
                        g.DrawLine(pen, 10.3F, 10.5F, 10.3F, 16.5F);
                        g.DrawLine(pen, 13.7F, 10.5F, 13.7F, 16.5F);
                        break;
                    case "reports": // document
                        g.DrawPolygon(pen, new[] { new PointF(6F, 3F), new PointF(14F, 3F), new PointF(18F, 7F), new PointF(18F, 21F), new PointF(6F, 21F) });
                        g.DrawLine(pen, 9F, 11.5F, 15F, 11.5F);
                        g.DrawLine(pen, 9F, 15.5F, 15F, 15.5F);
                        break;
                    case "recycle": // two arrows going round
                        g.DrawArc(pen, 5F, 5F, 14F, 14F, 200F, 130F);
                        g.DrawArc(pen, 5F, 5F, 14F, 14F, 20F, 130F);
                        g.DrawLines(pen, new[] { new PointF(15.2F, 4.2F), new PointF(18.2F, 7.4F), new PointF(14.2F, 8.6F) });
                        g.DrawLines(pen, new[] { new PointF(8.8F, 19.8F), new PointF(5.8F, 16.6F), new PointF(9.8F, 15.4F) });
                        break;
                    case "clipboard":
                        using (var p = Theme.Round(new RectangleF(5F, 5F, 14F, 16F), 2.2F)) g.DrawPath(pen, p);
                        using (var p = Theme.Round(new RectangleF(9F, 3F, 6F, 4F), 1.2F)) g.FillPath(brush, p);
                        g.DrawLine(pen, 8.5F, 12F, 15.5F, 12F);
                        g.DrawLine(pen, 8.5F, 16F, 13.5F, 16F);
                        break;
                    case "pulse": // heartbeat line
                        g.DrawLines(pen, new[] { new PointF(3F, 12F), new PointF(8F, 12F), new PointF(10.5F, 6F), new PointF(13.5F, 18F), new PointF(16F, 12F), new PointF(21F, 12F) });
                        break;
                    case "disk": // storage cylinder
                        g.DrawEllipse(pen, 5F, 4F, 14F, 5F);
                        g.DrawLine(pen, 5F, 6.5F, 5F, 17.5F);
                        g.DrawLine(pen, 19F, 6.5F, 19F, 17.5F);
                        g.DrawArc(pen, 5F, 9.5F, 14F, 5F, 0F, 180F);
                        g.DrawArc(pen, 5F, 15F, 14F, 5F, 0F, 180F);
                        break;
                    case "sensors": // video camera
                        using (var p = Theme.Round(new RectangleF(2.5F, 7F, 13F, 10F), 2.2F)) g.DrawPath(pen, p);
                        g.DrawPolygon(pen, new[] { new PointF(15.5F, 11F), new PointF(21F, 8F), new PointF(21F, 16F), new PointF(15.5F, 13F) });
                        break;
                    case "network": // up and down arrows
                        g.DrawLine(pen, 8F, 19F, 8F, 6F);
                        g.DrawLines(pen, new[] { new PointF(4.8F, 9.2F), new PointF(8F, 5.5F), new PointF(11.2F, 9.2F) });
                        g.DrawLine(pen, 16F, 5F, 16F, 18F);
                        g.DrawLines(pen, new[] { new PointF(12.8F, 14.8F), new PointF(16F, 18.5F), new PointF(19.2F, 14.8F) });
                        break;
                    case "keyboards":
                        using (var p = Theme.Round(new RectangleF(2.5F, 7F, 19F, 10F), 2.2F)) g.DrawPath(pen, p);
                        foreach (float x in new[] { 5.6F, 9.1F, 12.6F, 16.1F }) g.FillEllipse(brush, x, 9.6F, 1.7F, 1.7F);
                        g.DrawLine(pen, 8F, 14F, 16F, 14F);
                        break;
                    case "buddyclaude":
                    case "remote": // one screen shown on another
                        using (var p = Theme.Round(new RectangleF(2.5F, 4.5F, 13F, 9.5F), 2F)) g.DrawPath(pen, p);
                        using (var p = Theme.Round(new RectangleF(8.5F, 10.5F, 13F, 9.5F), 2F))
                        {
                            g.FillPath(cut, p);
                            g.DrawPath(pen, p);
                        }
                        break;
                    case "shield":
                        g.DrawPolygon(pen, new[] { new PointF(12F, 3F), new PointF(19.5F, 6F), new PointF(19.5F, 12F), new PointF(12F, 21F), new PointF(4.5F, 12F), new PointF(4.5F, 6F) });
                        g.DrawLines(pen, new[] { new PointF(8.8F, 11.5F), new PointF(11.3F, 14F), new PointF(15.4F, 9.4F) });
                        break;
                    case "notify": // bell
                        g.DrawArc(pen, 7F, 4.5F, 10F, 10F, 180F, 180F);
                        g.DrawLine(pen, 7F, 9.5F, 7F, 16F);
                        g.DrawLine(pen, 17F, 9.5F, 17F, 16F);
                        g.DrawLine(pen, 4.5F, 16.5F, 19.5F, 16.5F);
                        g.FillEllipse(brush, 10.6F, 18.2F, 2.8F, 2.8F);
                        break;
                    case "buddy": // little spider
                        g.FillEllipse(brush, 8F, 8.5F, 8F, 9F);
                        foreach (float s in new[] { -1F, 1F })
                        {
                            g.DrawLines(pen, new[] { new PointF(12F + s * 3F, 11F), new PointF(12F + s * 7F, 7F), new PointF(12F + s * 9F, 10F) });
                            g.DrawLines(pen, new[] { new PointF(12F + s * 3.5F, 13F), new PointF(12F + s * 8F, 13F), new PointF(12F + s * 9.5F, 16.5F) });
                            g.DrawLines(pen, new[] { new PointF(12F + s * 3F, 15.5F), new PointF(12F + s * 6.5F, 18F), new PointF(12F + s * 7F, 21F) });
                        }
                        break;
                    case "buddyhide":
                    case "tray": // eye
                        g.DrawBezier(pen, 3F, 12F, 8F, 5F, 16F, 5F, 21F, 12F);
                        g.DrawBezier(pen, 3F, 12F, 8F, 19F, 16F, 19F, 21F, 12F);
                        g.FillEllipse(brush, 9.6F, 9.6F, 4.8F, 4.8F);
                        break;
                    case "search": // magnifying glass
                        g.DrawEllipse(pen, 4F, 4F, 11.5F, 11.5F);
                        g.DrawLine(pen, 14.2F, 14.2F, 20F, 20F);
                        break;
                    case "memory": // memory chip with its pins
                        using (var p = Theme.Round(new RectangleF(6F, 6F, 12F, 12F), 2.2F)) g.DrawPath(pen, p);
                        g.FillRectangle(brush, 10F, 10F, 4F, 4F);
                        foreach (float at in new[] { 9.5F, 14.5F })
                        {
                            g.DrawLine(pen, at, 3F, at, 5.6F);
                            g.DrawLine(pen, at, 18.4F, at, 21F);
                            g.DrawLine(pen, 3F, at, 5.6F, at);
                            g.DrawLine(pen, 18.4F, at, 21F, at);
                        }
                        break;
                    case "clock":
                        g.DrawEllipse(pen, 3.5F, 3.5F, 17F, 17F);
                        g.DrawLines(pen, new[] { new PointF(12F, 7.5F), new PointF(12F, 12F), new PointF(15.4F, 14F) });
                        break;
                    case "spark": // a four-pointed star, and a small one beside it
                        g.FillPolygon(brush, new[] { new PointF(10F, 3F), new PointF(11.9F, 9.1F), new PointF(18F, 11F), new PointF(11.9F, 12.9F), new PointF(10F, 19F), new PointF(8.1F, 12.9F), new PointF(2F, 11F), new PointF(8.1F, 9.1F) });
                        g.FillPolygon(brush, new[] { new PointF(18.5F, 14F), new PointF(19.4F, 16.6F), new PointF(22F, 17.5F), new PointF(19.4F, 18.4F), new PointF(18.5F, 21F), new PointF(17.6F, 18.4F), new PointF(15F, 17.5F), new PointF(17.6F, 16.6F) });
                        break;
                    case "exit": // arrow out of a door
                        g.DrawLine(pen, 10F, 12F, 20.5F, 12F);
                        g.DrawLines(pen, new[] { new PointF(17.3F, 8.8F), new PointF(20.8F, 12F), new PointF(17.3F, 15.2F) });
                        g.DrawLines(pen, new[] { new PointF(10F, 4.5F), new PointF(4.5F, 4.5F), new PointF(4.5F, 19.5F), new PointF(10F, 19.5F) });
                        break;
                    case "signin": // arrow into a door
                        g.DrawLine(pen, 3.5F, 12F, 13.5F, 12F);
                        g.DrawLines(pen, new[] { new PointF(10.3F, 8.8F), new PointF(13.8F, 12F), new PointF(10.3F, 15.2F) });
                        g.DrawLines(pen, new[] { new PointF(14F, 4.5F), new PointF(19.5F, 4.5F), new PointF(19.5F, 19.5F), new PointF(14F, 19.5F) });
                        break;
                }
            }
            g.Restore(st);
        }

        static Image emblem;

        // The spider-in-a-shield emblem, stored inside the exe (src\emblem.png)
        static Image Emblem
        {
            get
            {
                if (emblem == null) emblem = Image.FromStream(typeof(Icons).Assembly.GetManifestResourceStream("TaskBoost.emblem.png"));
                return emblem;
            }
        }

        // Black rounded tile with the emblem; the rim is blue when calm and takes the mood colour otherwise.
        // Used for the header logo, the tray icon and the exe icon.
        public static void AppIcon(Graphics g, RectangleF r, Color mood)
        {
            float rim = Math.Max(1F, r.Width * 0.045F);
            var inner = RectangleF.Inflate(r, -rim / 2F, -rim / 2F);
            Theme.FillRound(g, inner, r.Width * 0.24F, Color.FromArgb(2, 3, 8));
            using (var path = Theme.Round(inner, r.Width * 0.24F))
            using (var b = new LinearGradientBrush(r, mood, mood == Theme.Accent ? Theme.Accent2 : mood, 45F))
            using (var p = new Pen(b, rim)) g.DrawPath(p, path);
            float inset = r.Width * 0.07F;
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            lock (Emblem) g.DrawImage(Emblem, RectangleF.Inflate(r, -inset, -inset));
            g.Restore(st);
        }

        public static Bitmap AppIconBitmap(int size, Color mood)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                AppIcon(g, new RectangleF(0, 0, size, size), mood);
            }
            return bmp;
        }

        // Header picture: a laptop with a bolt on screen and speed lines behind it, on a 200x80 grid
        public static void Hero(Graphics g, RectangleF r)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 200F, r.Height / 80F);
            float[][] lines = { new[] { 10F, 24F, 52F, 150F }, new[] { 26F, 40F, 60F, 230F }, new[] { 2F, 56F, 48F, 90F } };
            foreach (float[] l in lines)
                using (var p = new Pen(Color.FromArgb((int)l[3], Theme.Accent), 3.4F))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLine(p, l[0], l[1], l[2], l[1]);
                }
            var screen = new RectangleF(74F, 6F, 96F, 58F);
            Theme.FillRound(g, screen, 7F, Theme.Card);
            Theme.DrawRound(g, screen, 7F, Color.FromArgb(96, 104, 120), 2.6F);
            using (var p = new Pen(Color.FromArgb(96, 104, 120), 4.4F))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, 60F, 73F, 184F, 73F);
            }
            Bolt(g, new RectangleF(101F, 13F, 42F, 44F), Theme.Accent);
            using (var b = new SolidBrush(Color.FromArgb(200, Theme.Accent2))) g.FillEllipse(b, 186F, 10F, 7F, 7F);
            using (var b = new SolidBrush(Color.FromArgb(120, Theme.Accent))) g.FillEllipse(b, 192F, 34F, 5F, 5F);
            g.Restore(st);
        }
    }
}

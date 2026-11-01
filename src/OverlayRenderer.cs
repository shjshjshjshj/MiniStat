using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace MiniStat
{
    public static class OverlayRenderer
    {
        public const int BaseWidth = 260;
        public const int BaseHeight = 196;
        // Collapsed state: header plus a single row of two tiles.
        public const int CompactHeight = 73;
        public const int PairCount = 4;
        public const int PadX = 10;
        public const int PadY = 9;
        public const int TileW = 116;
        public const int TileH = 36;
        public const int GapX = 8;
        public const int GapY = 5;
        public const int Row0 = 27;
        public const float CardRadius = 14f;
        public const float TileRadius = 9f;

        public static readonly Color CpuColor = Color.FromArgb(0x4F, 0xC3, 0xF7);
        public static readonly Color GpuColor = Color.FromArgb(0x81, 0xC7, 0x84);
        public static readonly Color RamColor = Color.FromArgb(0xFF, 0xB7, 0x4D);
        public static readonly Color NetColor = Color.FromArgb(0x64, 0xB5, 0xF6);
        public static readonly Color FpsColor = Color.FromArgb(0xBA, 0x68, 0xC8);
        public static readonly Color PingColor = Color.FromArgb(0x4D, 0xB6, 0xAC);
        public static readonly Color CpuTempColor = Color.FromArgb(0xEF, 0x53, 0x50);
        public static readonly Color GpuTempColor = Color.FromArgb(0xFF, 0x8A, 0x65);

        private static readonly Color CardBg = Color.FromArgb(13, 16, 21);
        private static readonly Color CardBorder = Color.FromArgb(42, 255, 255, 255);
        private static readonly Color TileBg = Color.FromArgb(255, 33, 40, 50);
        private static readonly Color TileBorder = Color.FromArgb(28, 255, 255, 255);
        private static readonly Color LabelColor = Color.FromArgb(255, 186, 198, 213);
        private static readonly Color ValueColor = Color.FromArgb(255, 248, 251, 254);
        private static readonly Color UnitColor = Color.FromArgb(255, 177, 189, 204);
        private static readonly Color MutedColor = Color.FromArgb(255, 141, 153, 168);
        private static readonly Color StatusColor = Color.FromArgb(255, 188, 199, 212);
        private static readonly Color WarnColor = Color.FromArgb(255, 255, 186, 88);
        private static readonly Color TrackColor = Color.FromArgb(255, 63, 74, 90);
        private static readonly Color HeaderColor = Color.FromArgb(255, 150, 164, 180);

        // Value is the loud element: bold and bright. Labels stay regular weight
        // but sit higher in contrast than before so they read at a glance.
        private static readonly Font ValueFont = CreateFont("Segoe UI", 11.5f, FontStyle.Bold, true);
        private static readonly Font LabelFont = CreateFont("Segoe UI", 8.25f, FontStyle.Regular, false);
        private static readonly Font UnitFont = CreateFont("Segoe UI", 8.25f, FontStyle.Regular, false);
        private static readonly Font HeaderFont = CreateFont("Segoe UI Semibold", 7.5f, FontStyle.Regular, true);
        private static readonly Font StatusFont = CreateFont("Segoe UI", 8.5f, FontStyle.Regular, false);
        private static readonly Font StatusSmallFont = CreateFont("Segoe UI", 7.75f, FontStyle.Regular, false);
        private static readonly Font NetFont = CreateFont("Segoe UI", 9f, FontStyle.Bold, true);
        private static readonly Font NetSmallFont = CreateFont("Segoe UI", 8.25f, FontStyle.Bold, true);
        private static readonly Font TinyFont = CreateFont("Segoe UI", 6.5f, FontStyle.Regular, false);

        private static readonly StringFormat Typo = CreateTypo();

        // Layout numbers below stay in 96-DPI units. Rendering happens directly
        // in device pixels: scaling the Graphics transform would grow the glyph
        // outlines after hinting, which is what made 125%/150% look soft.
        private static float _k = 1f;
        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        private static float Px(float v)
        {
            return v * _k;
        }

        private static Font F(Font f)
        {
            if (_k > 0.999f && _k < 1.001f) return f;
            string key = f.Name + "|" + f.Size.ToString("0.###", CultureInfo.InvariantCulture)
                + "|" + ((int)f.Style).ToString(CultureInfo.InvariantCulture)
                + "|" + _k.ToString("0.###", CultureInfo.InvariantCulture);
            Font cached;
            if (FontCache.TryGetValue(key, out cached)) return cached;
            Font made;
            try { made = new Font(f.FontFamily, f.Size * _k, f.Style, GraphicsUnit.Pixel); }
            catch { made = f; }
            FontCache[key] = made;
            return made;
        }

        private static StringFormat CreateTypo()
        {
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            sf.Trimming = StringTrimming.None;
            return sf;
        }

        // Everything below is laid out in 96-DPI base pixels and then scaled once
        // by the window scale. Fonts therefore use pixel units: a point-sized font
        // would also grow with the screen DPI, so a 125% display drew 1.25x text
        // inside a window that was already 1.25x larger.
        public static Font CreateFont(string family, float sizePt, FontStyle style, bool semiboldFallback)
        {
            float px = sizePt * 96f / 72f;
            try
            {
                var f = new Font(family, px, style, GraphicsUnit.Pixel);
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
            try
            {
                FontStyle s2 = semiboldFallback ? FontStyle.Bold : style;
                return new Font("Segoe UI", px, s2, GraphicsUnit.Pixel);
            }
            catch { }
            return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------------------------------------------------------------- text

        private static float EmPx(Graphics g, Font f)
        {
            return F(f).Size;
        }

        private static float Ascent(Graphics g, Font f)
        {
            FontFamily fam = f.FontFamily;
            return fam.GetCellAscent(f.Style) * EmPx(g, f) / fam.GetEmHeight(f.Style);
        }

        private static float Descent(Graphics g, Font f)
        {
            FontFamily fam = f.FontFamily;
            return fam.GetCellDescent(f.Style) * EmPx(g, f) / fam.GetEmHeight(f.Style);
        }

        private static float TextWidth(Graphics g, string text, Font f)
        {
            return g.MeasureString(text, F(f), PointF.Empty, Typo).Width;
        }

        private static void DrawBaseline(Graphics g, string text, Font f, Brush b, float x, float baseline)
        {
            g.DrawString(text, F(f), b, x, baseline - Ascent(g, f), Typo);
        }

        private static void DrawRightBaseline(Graphics g, string text, Font f, Brush b, float right, float baseline)
        {
            DrawBaseline(g, text, f, b, right - TextWidth(g, text, f), baseline);
        }

        private static void DrawTracked(Graphics g, string text, Font f, Brush b, float x, float baseline, float tracking)
        {
            float cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                string ch = text.Substring(i, 1);
                DrawBaseline(g, ch, f, b, cursor, baseline);
                cursor += TextWidth(g, ch, f) + tracking;
            }
        }

        // --------------------------------------------------------------- layout

        public static RectangleF TileRect(int col, int row)
        {
            float x = Px(PadX + col * (TileW + GapX));
            float y = Px(Row0 + row * (TileH + GapY));
            return new RectangleF(x, y, Px(TileW), Px(TileH));
        }

        private static float TileBaseline(Graphics g, RectangleF r)
        {
            float contentH = r.Height - Px(6f);
            float asc = Ascent(g, ValueFont);
            float desc = Descent(g, ValueFont);
            return r.Y + (contentH + asc - desc) / 2f;
        }

        private static double? Normalize01(double? value, double max)
        {
            if (!value.HasValue) return null;
            double v = value.Value / max;
            if (v < 0) v = 0;
            if (v > 1) v = 1;
            return v;
        }

        // --------------------------------------------------------------- paint

        public static int HeightFor(bool compact)
        {
            return compact ? CompactHeight : BaseHeight;
        }

        public static void Render(Graphics g, Snapshot s, DateTime now, int backgroundAlpha)
        {
            Render(g, s, now, backgroundAlpha, false, 0, 1f);
        }

        // compact draws one row (two metrics) chosen by pair. Everything else is
        // identical, so the card grows in place when the pointer arrives.
        public static void Render(Graphics g, Snapshot s, DateTime now, int backgroundAlpha, bool compact, int pair, float scale)
        {
            _k = scale <= 0.2f ? 1f : (scale > 3f ? 3f : scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // ClearType keeps light text on the dark card crisp; grayscale AA
            // turns 11px glyphs into a soft blur at this size.
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float cardH = HeightFor(compact);
            using (GraphicsPath card = RoundRect(new RectangleF(Px(0.5f), Px(0.5f), Px(BaseWidth - 1f), Px(cardH - 1f)), Px(CardRadius)))
            {
                using (var fill = new SolidBrush(Color.FromArgb(backgroundAlpha, CardBg)))
                    g.FillPath(fill, card);
                using (var pen = new Pen(CardBorder, Px(1f)))
                    g.DrawPath(pen, card);
            }

            DrawHeader(g, now);

            if (compact)
            {
                int first = (((pair % PairCount) + PairCount) % PairCount) * 2;
                DrawTile(g, first, TileRect(0, 0), s);
                DrawTile(g, first + 1, TileRect(1, 0), s);
                return;
            }

            for (int i = 0; i < 8; i++)
                DrawTile(g, i, TileRect(i % 2, i / 2), s);
        }

        private static void DrawTile(Graphics g, int index, RectangleF r, Snapshot s)
        {
            switch (index)
            {
                case 0:
                    DrawMetric(g, r, "CPU", Fmt.Pct(s.CpuPercent), "%", CpuColor, Normalize01(s.CpuPercent, 100));
                    break;
                case 1:
                    DrawMetric(g, r, "GPU", Fmt.Pct(s.GpuPercent), "%", GpuColor, Normalize01(s.GpuPercent, 100));
                    break;
                case 2:
                    DrawMetric(g, r, "메모리", Fmt.Pct(s.RamPercent), "%", RamColor, Normalize01(s.RamPercent, 100));
                    break;
                case 3:
                    DrawNetwork(g, r, s);
                    break;
                case 4:
                    DrawFps(g, r, s);
                    break;
                case 5:
                    DrawPing(g, r, s);
                    break;
                case 6:
                    DrawTemp(g, r, "CPU 온도", s.CpuTemp, s.CpuTempStatus, CpuTempColor);
                    break;
                default:
                    DrawTemp(g, r, "GPU 온도", s.GpuTemp, "센서 없음", GpuTempColor);
                    break;
            }
        }

        private static void DrawHeader(Graphics g, DateTime now)
        {
            float baseline = Px(PadY) + Ascent(g, HeaderFont) + Px(1.5f);
            using (var b = new SolidBrush(HeaderColor))
            {
                DrawTracked(g, "MINISTAT", HeaderFont, b, Px(PadX), baseline, Px(0.7f));
                DrawRightBaseline(g, now.ToString("HH:mm"), HeaderFont, b, Px(BaseWidth - PadX), baseline);
            }
        }

        private static void DrawCellBackground(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = RoundRect(r, Px(TileRadius)))
            {
                using (var fill = new SolidBrush(TileBg)) g.FillPath(fill, p);
                using (var pen = new Pen(TileBorder, Px(1f))) g.DrawPath(pen, p);
            }
        }

        private static void DrawTrack(Graphics g, RectangleF r)
        {
            float w = r.Width - Px(16f);
            using (GraphicsPath tp = RoundRect(new RectangleF(r.X + Px(8f), r.Bottom - Px(6f), w, Px(2f)), Px(1f)))
            using (var tb = new SolidBrush(TrackColor))
                g.FillPath(tb, tp);
        }

        private static void DrawBar(Graphics g, RectangleF r, double? bar01, Color accent)
        {
            DrawTrack(g, r);
            if (!bar01.HasValue) return;
            float w = r.Width - Px(16f);
            float filled = (float)(w * Math.Max(0, Math.Min(1, bar01.Value)));
            if (filled < Px(1.2f)) return;
            using (GraphicsPath fp = RoundRect(new RectangleF(r.X + Px(8f), r.Bottom - Px(6f), filled, Px(2f)), Px(1f)))
            using (var fb = new SolidBrush(Color.FromArgb(235, accent)))
                g.FillPath(fb, fp);
        }

        private static void DrawValue(Graphics g, RectangleF r, float baseline, string value, string unit, Color color)
        {
            float right = r.Right - Px(8f);
            float unitW = 0f;
            float unitWidth = 0f;
            if (!string.IsNullOrEmpty(unit))
            {
                unitWidth = TextWidth(g, unit, UnitFont);
                unitW = unitWidth + Px(2f);
            }
            float vw = TextWidth(g, value, ValueFont);
            float x = right - unitW - vw;
            if (x < r.X + Px(8f)) x = r.X + Px(8f);
            using (var b = new SolidBrush(color))
                DrawBaseline(g, value, ValueFont, b, x, baseline);
            if (unitW > 0f)
            {
                using (var b = new SolidBrush(UnitColor))
                    DrawBaseline(g, unit, UnitFont, b, right - unitWidth, baseline);
            }
        }

        private static void DrawStatus(Graphics g, RectangleF r, float baseline, string label, string status, Color color)
        {
            float contentL = r.X + Px(8f);
            float contentR = r.Right - Px(8f);
            float avail = contentR - (contentL + TextWidth(g, label, LabelFont) + Px(5f));
            Font f = StatusFont;
            if (TextWidth(g, status, f) > avail) f = StatusSmallFont;
            if (TextWidth(g, status, f) > avail) f = TinyFont;
            float w = TextWidth(g, status, f);
            if (w > avail) w = avail;
            using (var b = new SolidBrush(color))
                DrawBaseline(g, status, f, b, contentR - w, baseline);
        }

        private static void DrawMetric(Graphics g, RectangleF r, string label, string value, string unit,
            Color accent, double? bar01)
        {
            DrawCellBackground(g, r);
            float baseline = TileBaseline(g, r);
            using (var b = new SolidBrush(LabelColor))
                DrawBaseline(g, label, LabelFont, b, r.X + Px(8f), baseline);

            if (value != Fmt.Dash)
            {
                DrawValue(g, r, baseline, value, unit, ValueColor);
                DrawBar(g, r, bar01, accent);
            }
            else
            {
                DrawValue(g, r, baseline, Fmt.Dash, "", MutedColor);
                DrawTrack(g, r);
            }
        }

        private static void DrawNetwork(Graphics g, RectangleF r, Snapshot s)
        {
            DrawCellBackground(g, r);
            float baseline = TileBaseline(g, r);
            const string label = "네트";
            using (var b = new SolidBrush(LabelColor))
                DrawBaseline(g, label, LabelFont, b, r.X + Px(8f), baseline);

            string down = Fmt.Mbps(s.NetDownMbps);
            string up = Fmt.Mbps(s.NetUpMbps);

            float upW = TextWidth(g, up, NetSmallFont);
            float upX = r.Right - Px(8f) - upW;
            float upArrowX = upX - Px(2.5f) - Px(ArrowWidth);

            float downRight = upArrowX - Px(8f);
            Font downFont = NetFont;
            float downW = TextWidth(g, down, downFont);
            float avail = downRight - (r.X + Px(8f) + TextWidth(g, label, LabelFont) + Px(5f));
            if (downW > avail) { downFont = NetSmallFont; downW = TextWidth(g, down, downFont); }
            if (downW > avail) { downFont = TinyFont; downW = TextWidth(g, down, downFont); }
            float downX = downRight - downW;
            float downArrowX = downX - Px(2.5f) - Px(ArrowWidth);

            DrawArrow(g, downArrowX, baseline, true, NetArrowColor);
            using (var b = new SolidBrush(ValueColor))
                DrawBaseline(g, down, downFont, b, downX, baseline);
            DrawArrow(g, upArrowX, baseline, false, UnitColor);
            using (var b = new SolidBrush(UnitColor))
                DrawBaseline(g, up, NetSmallFont, b, upX, baseline);
        }

        private const float ArrowWidth = 5.4f;
        private const float ArrowHeight = 7.2f;
        private static readonly Color NetArrowColor = Color.FromArgb(255, 118, 178, 224);

        // Drawn instead of a glyph: ↓/↑ at 8pt renders as a hairline that reads
        // like the digit 1 on a small overlay.
        private static void DrawArrow(Graphics g, float x, float baseline, bool down, Color color)
        {
            float aw = Px(ArrowWidth);
            float cx = x + aw / 2f;
            float top = baseline - Px(ArrowHeight);
            float bottom = baseline - Px(0.6f);
            float tipY = down ? bottom : top;
            float wingY = down ? bottom - Px(2.6f) : top + Px(2.6f);
            using (var pen = new Pen(color, Px(1.35f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLine(pen, cx, down ? top : top + Px(0.2f), cx, down ? bottom - Px(0.2f) : bottom);
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(cx - aw / 2f, wingY),
                    new PointF(cx, tipY),
                    new PointF(cx + aw / 2f, wingY)
                });
            }
        }

        private static void DrawFps(Graphics g, RectangleF r, Snapshot s)
        {
            DrawCellBackground(g, r);
            float baseline = TileBaseline(g, r);
            using (var b = new SolidBrush(LabelColor))
                DrawBaseline(g, "FPS", LabelFont, b, r.X + Px(8f), baseline);

            bool hasValue = s.Fps.HasValue && s.Fps.Value > 0;
            if (hasValue)
            {
                DrawValue(g, r, baseline, ((int)Math.Round(s.Fps.Value)).ToString(), "", ValueColor);
                DrawBar(g, r, Math.Min(1.0, s.Fps.Value / 165.0), FpsColor);
            }
            else
            {
                string text = string.IsNullOrEmpty(s.FpsStatus) ? "대기" : s.FpsStatus;
                DrawStatus(g, r, baseline, "FPS", text, text == "실패" ? WarnColor : StatusColor);
                DrawTrack(g, r);
            }
        }

        private static void DrawPing(Graphics g, RectangleF r, Snapshot s)
        {
            DrawCellBackground(g, r);
            float baseline = TileBaseline(g, r);
            const string label = "지연시간";
            using (var b = new SolidBrush(LabelColor))
                DrawBaseline(g, label, LabelFont, b, r.X + Px(8f), baseline);

            if (s.PingTimeout || !s.PingMs.HasValue)
            {
                DrawStatus(g, r, baseline, label, s.PingTimeout ? "시간 초과" : Fmt.Dash,
                    s.PingTimeout ? WarnColor : StatusColor);
                DrawTrack(g, r);
                return;
            }
            Color c = PingColor;
            if (s.PingMs.Value >= 80) c = Color.FromArgb(255, 239, 83, 80);
            else if (s.PingMs.Value >= 40) c = RamColor;
            DrawValue(g, r, baseline, ((int)Math.Round(s.PingMs.Value)).ToString(), "ms", c);
            DrawBar(g, r, s.PingMs.Value / 100.0, c);
        }

        private static void DrawTemp(Graphics g, RectangleF r, string label, double? value, string status, Color accent)
        {
            DrawCellBackground(g, r);
            float baseline = TileBaseline(g, r);
            using (var b = new SolidBrush(LabelColor))
                DrawBaseline(g, label, LabelFont, b, r.X + Px(8f), baseline);

            if (value.HasValue)
            {
                DrawValue(g, r, baseline, Fmt.Temp(value), "\u2103", ValueColor);
                DrawBar(g, r, (value.Value - 30.0) / 70.0, accent);
            }
            else
            {
                DrawStatus(g, r, baseline, label, string.IsNullOrEmpty(status) ? "센서 없음" : status, StatusColor);
                DrawTrack(g, r);
            }
        }
    }
}

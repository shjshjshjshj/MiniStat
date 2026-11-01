using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace MiniStat
{
    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 29, 38); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(46, 56, 70); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(46, 56, 70); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(46, 56, 70); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(46, 56, 70); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 29, 38); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 29, 38); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 29, 38); } }
        public override Color SeparatorDark { get { return Color.FromArgb(44, 52, 64); } }
        public override Color SeparatorLight { get { return Color.FromArgb(44, 52, 64); } }
        public override Color MenuBorder { get { return Color.FromArgb(48, 57, 70); } }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : Color.FromArgb(214, 221, 230);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(160, 170, 184);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            using (var back = new SolidBrush(Color.FromArgb(24, 29, 38)))
                e.Graphics.FillRectangle(back, r);
            using (var pen = new Pen(Color.FromArgb(0x64, 0xB5, 0xF6), 1.7f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float x = r.Left + r.Width * 0.18f;
                float y = r.Top + r.Height * 0.52f;
                float w = r.Width * 0.64f;
                float h = r.Height * 0.50f;
                e.Graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(x, y),
                    new PointF(x + w * 0.34f, y + h * 0.48f),
                    new PointF(x + w, y - h * 0.42f)
                });
            }
        }
    }

    public class OverlayForm : Form
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        // A click-through window swallows every mouse message, so the card menu
        // is unreachable while it is on. The tray menu and this hotkey are the
        // way back out.
        private const int HotkeyId = 0xB10;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // The card is pushed as a 32-bit surface with per-pixel alpha. Clipping a
        // window Region to a rounded rect leaves a hard 1px staircase around the
        // corners, which is what read as "broken background pixels".
        private const int ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst,
            ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private readonly AppSettings _settings;
        private readonly CpuUsageSensor _cpu = new CpuUsageSensor();
        private readonly MemorySensor _mem = new MemorySensor();
        private readonly NvmlGpuSensor _gpu = new NvmlGpuSensor();
        private readonly NetSpeedSensor _net = new NetSpeedSensor();
        private readonly PingSensor _ping = new PingSensor("1.1.1.1");
        private readonly CpuTemperatureSensor _cpuTemp = new CpuTemperatureSensor();
        private readonly FpsReader _fps = new FpsReader();
        private readonly Snapshot _snap = new Snapshot();
        private readonly Timer _timer = new Timer();
        private readonly bool _elevated;
        private float _scale = 1f;
        private bool _dragging;
        private Point _dragStart;
        private Point _dragOrigin;
        private NotifyIcon _tray;
        private Icon _trayIcon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _displayItem;
        private ToolStripMenuItem _alwaysItem;
        private ToolStripMenuItem _clickItem;
        private ToolStripMenuItem _trayClickItem;
        private ToolStripMenuItem _trayShowItem;
        private ToolStripMenuItem _trayTopItem;
        private ToolStripMenuItem _autoItem;
        private ToolStripMenuItem _trayAutoItem;
        private string _hotkeyLabel = "";
        private readonly Timer _hoverTimer = new Timer();
        private bool _compact;
        private bool _expanded;
        private int _pair;
        private DateTime _pairAt = DateTime.UtcNow;
        // Screen Y of the card's bottom edge while it is anchored to the bottom
        // of the screen; int.MinValue when it simply grows downward from its top.
        private int _anchorBottom = int.MinValue;
        private const int PairCycleMs = 4000;
        private double _opacity = 0.92;
        private bool _layered = true;
        private DateTime _hoverGrace = DateTime.MinValue;
        private int _ticks;
        private bool _closing;

        public OverlayForm(AppSettings settings)
        {
            _settings = settings;
            _elevated = IsElevated();
            _compact = settings.CompactMode;
            _scale = settings.Scale > 0 ? settings.Scale : AutoScaleForScreen();
            Text = "MiniStat";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(14, 17, 22);
            TopMost = settings.AlwaysOnTop;
            _opacity = Math.Max(0.3, Math.Min(1.0, settings.OpacityPercent / 100.0));
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _hoverTimer.Interval = 120;
            _hoverTimer.Tick += OnHoverTick;
            ApplyScale();
            BuildMenu();
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        // Small rotating trace so a disappearing card can be diagnosed from the
        // machine it happened on.
        private void Trace(string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(_settings.PathName);
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "trace.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 65536) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private static string ScreenMap()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Screen s in Screen.AllScreens)
            {
                sb.Append("[").Append(s.DeviceName).Append(" b=").Append(s.Bounds)
                  .Append(" wa=").Append(s.WorkingArea).Append(" primary=").Append(s.Primary).Append("] ");
            }
            return sb.ToString();
        }

        private static float AutoScaleForScreen()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = g.DpiX / 96f;
                    s = (float)Math.Round(s * 4f, MidpointRounding.AwayFromZero) / 4f;
                    if (s < 1f) s = 1f;
                    if (s > 2f) s = 2f;
                    return s;
                }
            }
            catch { return 1f; }
        }

        private void ApplyScale()
        {
            ApplyLayout();
        }

        // Collapsed the card is one row tall; expanding only changes the height,
        // so the top-left corner stays where the user put it.
        private int CurrentHeight()
        {
            bool full = !_compact || _expanded;
            return (int)Math.Round(OverlayRenderer.HeightFor(!full) * _scale);
        }

        private void ApplyLayout()
        {
            int w = (int)Math.Round(OverlayRenderer.BaseWidth * _scale);
            int h = CurrentHeight();
            bool resized = ClientSize.Width != w || ClientSize.Height != h;
            if (resized) ClientSize = new Size(w, h);
            if (_layered)
            {
                Region = null;
            }
            else
            {
                using (GraphicsPath path = OverlayRenderer.RoundRect(new RectangleF(0, 0, w, h), OverlayRenderer.CardRadius * _scale))
                    Region = new Region(path);
            }
            RenderSurface();
            if (resized && IsHandleCreated)
                Trace("layout=" + w + "x" + h + " expanded=" + _expanded);
            if (IsHandleCreated) EnsureFullyOnSomeScreen();
        }

        private void EnsureFullyOnSomeScreen()
        {
            try
            {
                if (Width <= 0 || Height <= 0) return;
                foreach (Screen s in Screen.AllScreens)
                    if (s.WorkingArea.IntersectsWith(Bounds)) return;
                Trace("offscreen pos=" + Left + "," + Top + " " + Width + "x" + Height + " -> snap");
                SnapToBottomRight();
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        private void RenderSurface()
        {
            if (!IsHandleCreated) return;
            if (!_layered)
            {
                Invalidate();
                return;
            }
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    OverlayRenderer.Render(g, _snap, DateTime.Now, 255, _compact && !_expanded, _pair, _scale);
                }
                if (!PushLayered(bmp)) FallBackToWindowRegion();
            }
        }

        // GDI+ gives straight alpha, the desktop compositor wants premultiplied.
        private bool PushLayered(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[data.Stride * h];
            try { Marshal.Copy(data.Scan0, buf, 0, buf.Length); }
            finally { bmp.UnlockBits(data); }

            int op = (int)Math.Round(_opacity * 255);
            for (int i = 0; i < buf.Length; i += 4)
            {
                int a = buf[i + 3];
                if (a == 0)
                {
                    buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0;
                    continue;
                }
                if (op < 255) a = a * op / 255;
                buf[i] = (byte)(buf[i] * a / 255);
                buf[i + 1] = (byte)(buf[i + 1] * a / 255);
                buf[i + 2] = (byte)(buf[i + 2] * a / 255);
                buf[i + 3] = (byte)a;
            }

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            var bmi = new BitmapInfoHeader();
            bmi.biSize = Marshal.SizeOf(typeof(BitmapInfoHeader));
            bmi.biWidth = w;
            bmi.biHeight = -h;
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            IntPtr bits;
            IntPtr dib = CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
            {
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
                return false;
            }

            Marshal.Copy(buf, 0, bits, buf.Length);
            IntPtr old = SelectObject(memDc, dib);
            var dst = new NativePoint();
            dst.X = Left;
            dst.Y = Top;
            var size = new NativeSize();
            size.cx = w;
            size.cy = h;
            var src = new NativePoint();
            var blend = new BlendFunction();
            blend.BlendOp = 0;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = 1;
            bool ok = UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            SelectObject(memDc, old);
            DeleteObject(dib);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            return ok;
        }

        private void FallBackToWindowRegion()
        {
            if (!_layered) return;
            _layered = false;
            try { Opacity = _opacity; } catch { }
            ApplyLayout();
            Invalidate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_settings.X == int.MinValue || _settings.Y == int.MinValue)
                PlaceInitial();
            else
            {
                Location = new Point(_settings.X, _settings.Y);
                EnsureOnScreen();
            }
            _timer.Interval = Math.Max(300, _settings.RefreshMs);
            _timer.Tick += OnTick;
            _timer.Start();
            _cpuTemp.Start();
            _timer_Tick(DateTime.MinValue);
            CreateTray();
            ApplyClickThrough(_settings.ClickThrough);
            if (_compact && _settings.HoverExpand) _hoverTimer.Start();
            RegisterEscapeHotkey();
            RenderSurface();
            Trace("load pos=" + _settings.X + "," + _settings.Y + " scale=" + _scale
                + " compact=" + _compact + " hover=" + _settings.HoverExpand
                + " clickThrough=" + _settings.ClickThrough + " " + ScreenMap());
        }

        // Ctrl+Alt+M is the shortcut users are told about, but other programs
        // often own it, so the first combination that actually registers wins and
        // the menus show the real one.
        private void RegisterEscapeHotkey()
        {
            uint[] mods = new uint[]
            {
                MOD_CONTROL | MOD_ALT,
                MOD_CONTROL | MOD_ALT | MOD_SHIFT,
                MOD_CONTROL | MOD_ALT,
                MOD_CONTROL | MOD_ALT,
                MOD_CONTROL | MOD_ALT | MOD_SHIFT
            };
            Keys[] keys = new Keys[] { Keys.M, Keys.M, Keys.H, Keys.O, Keys.H };
            string[] names = new string[] { "Ctrl+Alt+M", "Ctrl+Alt+Shift+M", "Ctrl+Alt+H", "Ctrl+Alt+O", "Ctrl+Alt+Shift+H" };
            for (int i = 0; i < keys.Length; i++)
            {
                try
                {
                    if (RegisterHotKey(Handle, HotkeyId, mods[i] | MOD_NOREPEAT, (uint)keys[i]))
                    {
                        _hotkeyLabel = names[i];
                        break;
                    }
                }
                catch { }
            }
            string suffix = _hotkeyLabel.Length == 0 ? "" : " (" + _hotkeyLabel + ")";
            if (_clickItem != null) _clickItem.Text = "클릭 통과" + suffix;
            if (_trayClickItem != null) _trayClickItem.Text = "클릭 통과" + suffix;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                SetClickThrough(!_settings.ClickThrough, true);
                return;
            }
            base.WndProc(ref m);
        }

        private void OnTick(object sender, EventArgs e)
        {
            _timer_Tick(DateTime.MinValue);
        }

        private void _timer_Tick(DateTime ignored)
        {
            try
            {
                double? cpu = _cpu.Sample();
                if (cpu.HasValue) _snap.CpuPercent = cpu;

                double pct, usedGb, totalGb;
                if (_mem.Sample(out pct, out usedGb, out totalGb))
                {
                    _snap.RamPercent = pct;
                    _snap.RamUsedGb = usedGb;
                    _snap.RamTotalGb = totalGb;
                }

                double? gpu = _gpu.Utilization();
                _snap.GpuPercent = gpu;
                _snap.GpuTemp = _gpu.Temperature();
                _snap.GpuName = _gpu.Name;

                double down, up;
                if (_net.Sample(out down, out up))
                {
                    _snap.NetDownMbps = down;
                    _snap.NetUpMbps = up;
                }

                _ping.Poll();
                _snap.PingMs = _ping.LastMs;
                _snap.PingTimeout = _ping.LastTimeout;
                _snap.CpuTemp = _cpuTemp.Value;
                _snap.CpuTempSource = _cpuTemp.Source;
                _snap.CpuTempStatus = _cpuTemp.Status;

                if (_settings.FpsEnabled)
                {
                    _fps.EnsureRunning(_elevated);
                    _snap.Fps = _fps.Fps;
                    _snap.FpsApp = _fps.App;
                    _snap.FpsStatus = _fps.Status;
                }
                else
                {
                    _snap.Fps = null;
                    _snap.FpsStatus = "꺼짐";
                }

                if (_compact && (DateTime.UtcNow - _pairAt).TotalMilliseconds >= PairCycleMs)
                {
                    _pairAt = DateTime.UtcNow;
                    _pair = (_pair + 1) % OverlayRenderer.PairCount;
                }

                _ticks++;
                if (_ticks % 60 == 0)
                    Trace("alive vis=" + Visible + " pos=" + Left + "," + Top + " size=" + Width + "x" + Height
                        + " expanded=" + _expanded + " anchor=" + _anchorBottom + " layered=" + _layered);

                RenderSurface();
            }
            catch
            {
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!_layered) base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_layered) return;
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            OverlayRenderer.Render(g, _snap, DateTime.Now, 255, _compact && !_expanded, _pair, _scale);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RenderSurface();
        }

        // A timer rather than MouseEnter/Leave: click-through windows never get
        // mouse messages, so the pointer position is sampled directly.
        private void OnHoverTick(object sender, EventArgs e)
        {
            if (!_compact) return;
            if (!_settings.HoverExpand) return;
            if (DateTime.UtcNow < _hoverGrace) return;
            // Mouse capture keeps dragging alive outside the window; collapsing
            // mid-drag would yank the card away from the pointer.
            if (_dragging) return;
            // The menu hangs off the card, so keep it open while a choice is made.
            if (_menu != null && _menu.Visible)
            {
                if (!_expanded) SetExpanded(true);
                return;
            }
            bool? inside = PointerInside();
            if (!inside.HasValue) return;
            if (inside.Value != _expanded) SetExpanded(inside.Value);
        }

        // Null when the pointer cannot be read at all (session without a mouse
        // queue); the card then simply stays in its current state.
        private bool? PointerInside()
        {
            try { return Bounds.Contains(Cursor.Position); }
            catch { return null; }
        }

        private void SetExpanded(bool expand)
        {
            _expanded = expand;
            ApplyLayout();
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            if (expand)
            {
                // Growing down is the normal case. When the full card would run
                // past the bottom of the screen the strip's bottom edge stays put
                // and the card unfolds upward, so the collapsed strip never jumps
                // as the pointer comes and goes.
                if (Bounds.Bottom > wa.Bottom - 6)
                {
                    int bottom = AnchorUsable() ? _anchorBottom : Bounds.Bottom;
                    int newTop = bottom - Height;
                    if (newTop < wa.Top + 4) newTop = wa.Top + 4;
                    Location = new Point(Location.X, newTop);
                    _anchorBottom = Location.Y + Height;
                }
            }
            else
            {
                if (AnchorUsable())
                {
                    int newTop = _anchorBottom - Height;
                    if (newTop < wa.Top + 4) newTop = wa.Top + 4;
                    Location = new Point(Location.X, newTop);
                }
                _pairAt = DateTime.UtcNow;
            }
            RenderSurface();
            Trace("expand=" + expand + " pos=" + Left + "," + Top + " " + Width + "x" + Height + " anchor=" + _anchorBottom);
        }

        // An anchor kept from another monitor would pull the card onto that
        // monitor when it collapses, which looks like the card vanished.
        private bool AnchorUsable()
        {
            if (_anchorBottom == int.MinValue) return false;
            try
            {
                Screen here = Screen.FromRectangle(Bounds);
                Screen at = Screen.FromPoint(new Point(Left + Width / 2, _anchorBottom - 1));
                if (!at.Equals(here))
                {
                    Trace("anchor reset " + _anchorBottom + " here=" + here.DeviceName + " at=" + at.DeviceName);
                    _anchorBottom = int.MinValue;
                    return false;
                }
            }
            catch { return false; }
            return true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _dragOrigin = Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                Point now = Cursor.Position;
                Location = new Point(_dragOrigin.X + (now.X - _dragStart.X), _dragOrigin.Y + (now.Y - _dragStart.Y));
                if (_anchorBottom != int.MinValue) _anchorBottom = Location.Y + Height;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                // Dropping settles on the compact strip so the saved position is
                // the one the user sees; otherwise the expanded card's top-left
                // was stored and the strip jumped after collapsing.
                if (_expanded) SetExpanded(false);
                _hoverGrace = DateTime.UtcNow.AddMilliseconds(800);
                RememberPosition();
            }
            base.OnMouseUp(e);
        }

        private void RememberPosition()
        {
            _settings.X = Location.X;
            _settings.Y = Location.Y;
            _settings.Save();
        }

        private void PlaceInitial()
        {
            Screen target = Screen.PrimaryScreen;
            if (Screen.AllScreens.Length > 1)
            {
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    if (!Screen.AllScreens[i].Primary) { target = Screen.AllScreens[i]; break; }
                }
            }
            MoveToScreen(target, 24);
        }

        private void MoveToScreen(Screen target, int inset)
        {
            Rectangle wa = target.WorkingArea;
            int x = wa.Right - Width - (int)Math.Round(inset * _scale);
            int y = wa.Top + (int)Math.Round(inset * _scale);
            _anchorBottom = int.MinValue;
            Location = new Point(x, y);
            RememberPosition();
        }

        private static readonly string[] SnapNames = new string[]
        {
            "왼쪽 위", "위 가운데", "오른쪽 위",
            "왼쪽 가운데", "화면 가운데", "오른쪽 가운데",
            "왼쪽 아래", "아래 가운데", "오른쪽 아래"
        };

        private ToolStripMenuItem CreateSnapMenu()
        {
            var root = new ToolStripMenuItem("화면에 붙이기");
            for (int i = 0; i < SnapNames.Length; i++)
            {
                int index = i;
                root.DropDownItems.Add(new ToolStripMenuItem(SnapNames[i], null,
                    delegate { SnapToPosition(index % 3, index / 3); }));
            }
            return root;
        }

        // Keep the card on the monitor it already lives on; when it is hidden or
        // parked off-screen, the monitor under the pointer is the best guess.
        private Screen SnapTargetScreen()
        {
            try
            {
                if (IsHandleCreated && Visible && Width > 0 && Height > 0)
                {
                    Rectangle b = Bounds;
                    foreach (Screen s in Screen.AllScreens)
                        if (s.WorkingArea.IntersectsWith(b)) return s;
                }
                if (IsHandleCreated) return Screen.FromPoint(Cursor.Position);
            }
            catch { }
            return Screen.PrimaryScreen;
        }

        // col and row are 0 (left/top), 1 (center) and 2 (right/bottom).
        private void SnapToPosition(int col, int row)
        {
            if (!Visible) SetVisible(true);
            if (_expanded) SetExpanded(false);
            Screen target = SnapTargetScreen();
            Rectangle wa = target.WorkingArea;
            int inset = (int)Math.Round(24 * _scale);
            int w = Width;
            int h = Height;
            int x = col == 0 ? wa.Left + inset : col == 1 ? wa.Left + (wa.Width - w) / 2 : wa.Right - w - inset;
            int y = row == 0 ? wa.Top + inset : row == 1 ? wa.Top + (wa.Height - h) / 2 : wa.Bottom - h - inset;
            Location = new Point(x, y);
            _anchorBottom = row == 2 ? y + h : int.MinValue;
            RememberPosition();
            Trace("snap=" + col + "," + row + " pos=" + x + "," + y + " screen=" + target.DeviceName);
            RenderSurface();
        }

        private void SnapToBottomRight()
        {
            SnapToPosition(2, 2);
        }

        private void EnsureOnScreen()
        {
            Rectangle b = Bounds;
            bool visible = false;
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(b)) { visible = true; break; }
            if (!visible) PlaceInitial();
        }

        private void CreateTray()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Color.FromArgb(24, 29, 38);
            menu.ForeColor = Color.FromArgb(220, 227, 236);
            _trayShowItem = new ToolStripMenuItem("숨기기");
            _trayShowItem.Click += delegate { SetVisible(!Visible); };
            _trayClickItem = new ToolStripMenuItem("클릭 통과");
            _trayClickItem.Checked = _settings.ClickThrough;
            _trayClickItem.Click += delegate { SetClickThrough(!_settings.ClickThrough, true); };
            _trayTopItem = new ToolStripMenuItem("항상 위");
            _trayTopItem.Checked = _settings.AlwaysOnTop;
            _trayTopItem.Click += delegate { SetAlwaysOnTop(!TopMost); };
            _trayAutoItem = new ToolStripMenuItem("Windows 시작 시 자동 실행");
            _trayAutoItem.Click += delegate { ToggleAutoStart(); };
            var exit = new ToolStripMenuItem("종료");
            exit.Click += delegate { Close(); };
            menu.Items.Add(_trayShowItem);
            menu.Items.Add(_trayClickItem);
            menu.Items.Add(_trayTopItem);
            menu.Items.Add(_trayAutoItem);
            menu.Items.Add(CreateSnapMenu());
            menu.Items.Add(exit);
            menu.Opening += delegate { SyncTrayMenu(); };
            _tray = new NotifyIcon();
            try { _trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tray.Icon = _trayIcon != null ? _trayIcon : SystemIcons.Application;
            _tray.Text = "MiniStat";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { SetVisible(!Visible); };
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkMenuRenderer();
            _menu.BackColor = Color.FromArgb(24, 29, 38);
            _menu.ForeColor = Color.FromArgb(220, 227, 236);
            _menu.Font = new Font("Segoe UI", 9f);
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;

            var refresh = new ToolStripMenuItem("새로 고침");
            AddRadio(refresh, "0.5초", _settings.RefreshMs == 500, delegate { SetRefresh(500); });
            AddRadio(refresh, "1초", _settings.RefreshMs == 1000, delegate { SetRefresh(1000); });
            AddRadio(refresh, "2초", _settings.RefreshMs == 2000, delegate { SetRefresh(2000); });
            _menu.Items.Add(refresh);

            var scale = new ToolStripMenuItem("배율");
            AddRadio(scale, "100%", Math.Abs(_scale - 1f) < 0.01f, delegate { SetScale(1f); });
            AddRadio(scale, "125%", Math.Abs(_scale - 1.25f) < 0.01f, delegate { SetScale(1.25f); });
            AddRadio(scale, "150%", Math.Abs(_scale - 1.5f) < 0.01f, delegate { SetScale(1.5f); });
            _menu.Items.Add(scale);

            var opacity = new ToolStripMenuItem("투명도");
            int[] steps = new int[] { 95, 90, 80, 70, 50 };
            foreach (int p in steps)
            {
                int captured = p;
                AddRadio(opacity, p + "%", _settings.OpacityPercent == p, delegate { SetOpacity(captured); });
            }
            _menu.Items.Add(opacity);

            _displayItem = new ToolStripMenuItem("표시 방식");
            AddRadio(_displayItem, "전체 표시", !_settings.CompactMode, delegate { SetCompact(false, false); });
            AddRadio(_displayItem, "2칸 (고정)", _settings.CompactMode && !_settings.HoverExpand, delegate { SetCompact(true, false); });
            AddRadio(_displayItem, "2칸 (마우스 올리면 전체)", _settings.CompactMode && _settings.HoverExpand, delegate { SetCompact(true, true); });
            _menu.Items.Add(_displayItem);

            _alwaysItem = new ToolStripMenuItem("항상 위", null, delegate
            {
                SetAlwaysOnTop(!TopMost);
            });
            _alwaysItem.Checked = _settings.AlwaysOnTop;
            _menu.Items.Add(_alwaysItem);

            _autoItem = new ToolStripMenuItem("Windows 시작 시 자동 실행", null, delegate { ToggleAutoStart(); });
            _menu.Items.Add(_autoItem);

            _clickItem = new ToolStripMenuItem("클릭 통과", null, delegate { SetClickThrough(!_settings.ClickThrough, true); });
            _clickItem.Checked = _settings.ClickThrough;
            _menu.Items.Add(_clickItem);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("보조 모니터로 이동", null, delegate { MoveToRelativeScreen(false); }));
            _menu.Items.Add(new ToolStripMenuItem("주 모니터로 이동", null, delegate { MoveToRelativeScreen(true); }));
            _menu.Items.Add(new ToolStripMenuItem("위치 초기화", null, delegate { PlaceInitial(); }));
            _menu.Items.Add(CreateSnapMenu());
            if (!_elevated)
            {
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(new ToolStripMenuItem("관리자 권한으로 다시 시작", null, delegate { RestartAsAdmin(); }));
            }
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("종료", null, delegate { Close(); }));

            ContextMenuStrip = _menu;
            _menu.Opening += delegate { SyncTrayMenu(); };
        }

        private static void AddRadio(ToolStripMenuItem parent, string text, bool isChecked, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            item.Checked = isChecked;
            parent.DropDownItems.Add(item);
        }

        private void SetRefresh(int ms)
        {
            _settings.RefreshMs = ms;
            _timer.Interval = ms;
            _settings.Save();
            MarkRadios(_menu.Items[0] as ToolStripMenuItem, ms == 500 ? "0.5초" : ms == 1000 ? "1초" : "2초");
        }

        private void SetScale(float scale)
        {
            _settings.Scale = scale;
            _scale = scale;
            ApplyScale();
            LayoutKeepingAnchor();
            EnsureOnScreen();
            _settings.Save();
            string label = scale <= 1.01f ? "100%" : scale <= 1.26f ? "125%" : "150%";
            MarkRadios(_menu.Items[1] as ToolStripMenuItem, label);
        }

        private void SetOpacity(int percent)
        {
            _settings.OpacityPercent = percent;
            _opacity = Math.Max(0.3, Math.Min(1.0, percent / 100.0));
            if (!_layered) Opacity = _opacity;
            _settings.Save();
            MarkRadios(_menu.Items[2] as ToolStripMenuItem, percent + "%");
            RenderSurface();
        }

        private void SetCompact(bool on, bool hover)
        {
            _settings.CompactMode = on;
            _settings.HoverExpand = on && hover;
            _compact = on;
            if (!on || !hover) _expanded = false;
            if (on && hover)
            {
                _pairAt = DateTime.UtcNow;
                _hoverTimer.Start();
            }
            else
            {
                _hoverTimer.Stop();
            }
            LayoutKeepingAnchor();
            EnsureOnScreen();
            _settings.Save();
            MarkRadios(_displayItem, on ? (hover ? "2칸 (마우스 올리면 전체)" : "2칸 (고정)") : "전체 표시");
        }

        // Height changes keep a bottom-anchored card welded to the same screen
        // edge, so switching modes near the bottom does not shift the strip.
        private void LayoutKeepingAnchor()
        {
            int previousBottom = Bounds.Bottom;
            ApplyLayout();
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            if (AnchorUsable())
            {
                int top = _anchorBottom - Height;
                if (top < wa.Top + 4) top = wa.Top + 4;
                Location = new Point(Location.X, top);
            }
            else if (Bounds.Bottom > wa.Bottom - 6)
            {
                int top = previousBottom - Height;
                if (top < wa.Top + 4) top = wa.Top + 4;
                Location = new Point(Location.X, top);
                _anchorBottom = Location.Y + Height;
            }
            RenderSurface();
            Trace("relayout pos=" + Left + "," + Top + " " + Width + "x" + Height + " anchor=" + _anchorBottom);
        }

        private static void MarkRadios(ToolStripMenuItem parent, string chosen)
        {
            if (parent == null) return;
            foreach (ToolStripItem item in parent.DropDownItems)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null) mi.Checked = mi.Text == chosen;
            }
        }

        private void MoveToRelativeScreen(bool primary)
        {
            Screen target = null;
            if (primary) target = Screen.PrimaryScreen;
            else
            {
                foreach (Screen s in Screen.AllScreens)
                    if (!s.Primary) { target = s; break; }
            }
            if (target == null) target = Screen.PrimaryScreen;
            MoveToScreen(target, 24);
        }

        private void ApplyClickThrough(bool enable)
        {
            if (!IsHandleCreated) return;
            IntPtr style = GetWindowLongPtr(Handle, GWL_EXSTYLE);
            long value = style.ToInt64();
            if (enable) value |= WS_EX_TRANSPARENT;
            else value &= ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(value));
        }

        // A click-through card cannot be right-clicked, so the tray menu has to
        // offer the same switch: it is the only way back out.
        private void SyncTrayMenu()
        {
            if (_trayShowItem != null) _trayShowItem.Text = Visible ? "숨기기" : "보이기";
            if (_trayClickItem != null) _trayClickItem.Checked = _settings.ClickThrough;
            if (_trayTopItem != null) _trayTopItem.Checked = TopMost;
            if (_alwaysItem != null) _alwaysItem.Checked = TopMost;
            if (_clickItem != null) _clickItem.Checked = _settings.ClickThrough;
            bool auto = AutoStart.IsEnabled();
            if (_autoItem != null) _autoItem.Checked = auto;
            if (_trayAutoItem != null) _trayAutoItem.Checked = auto;
        }

        private void ToggleAutoStart()
        {
            bool on = AutoStart.IsEnabled();
            string error;
            bool ok = on ? AutoStart.Disable(out error) : AutoStart.Enable(out error);
            Trace("autostart " + (on ? "off" : "on") + " ok=" + ok + (ok ? "" : " err=" + error));
            SyncTrayMenu();
            if (!ok)
            {
                MessageBox.Show("자동 실행 설정을 바꾸지 못했습니다." + Environment.NewLine + Environment.NewLine + error,
                    "MiniStat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_tray != null)
            {
                try
                {
                    _tray.ShowBalloonTip(5000, on ? "자동 실행 해제" : "자동 실행 등록",
                        on ? "다음 로그온부터 자동으로 실행되지 않습니다."
                           : "다음 로그온부터 MiniStat이 자동으로 실행됩니다.",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void SetVisible(bool show)
        {
            Visible = show;
            if (_trayShowItem != null) _trayShowItem.Text = show ? "숨기기" : "보이기";
            Trace("visible=" + show + " pos=" + Left + "," + Top);
            if (show)
            {
                EnsureOnScreen();
                EnsureFullyOnSomeScreen();
                if (TopMost) { try { BringToFront(); } catch { } }
                RenderSurface();
            }
            else if (_tray != null)
            {
                try
                {
                    _tray.ShowBalloonTip(5000, "MiniStat 숨김",
                        "다시 보려면 트레이 아이콘을 더블클릭하거나 메뉴에서 보이기를 고르세요.", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void SetAlwaysOnTop(bool on)
        {
            _settings.AlwaysOnTop = on;
            TopMost = on;
            if (_alwaysItem != null) _alwaysItem.Checked = on;
            if (_trayTopItem != null) _trayTopItem.Checked = on;
            _settings.Save();
            RenderSurface();
        }

        private void SetClickThrough(bool enable, bool notify)
        {
            _settings.ClickThrough = enable;
            ApplyClickThrough(enable);
            if (_clickItem != null) _clickItem.Checked = enable;
            if (_trayClickItem != null) _trayClickItem.Checked = enable;
            _settings.Save();
            if (notify && enable && _tray != null)
            {
                try
                {
                    string hint = _hotkeyLabel.Length == 0 ? "트레이 아이콘을 우클릭" : _hotkeyLabel + " 또는 트레이 아이콘 우클릭";
                    _tray.ShowBalloonTip(6000, "클릭 통과 켜짐", "끄려면 " + hint + "하세요.", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.Arguments = "--elevated";
                Process.Start(psi);
                _closing = true;
                Close();
            }
            catch
            {
                MessageBox.Show("관리자 권한 요청이 취소되었습니다.", "MiniStat");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing)
            {
                try { _settings.X = Location.X; _settings.Y = Location.Y; } catch { }
            }
            _settings.Save();
            try { UnregisterHotKey(Handle, HotkeyId); } catch { }
            _timer.Stop();
            _timer.Dispose();
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            try { _fps.Dispose(); } catch { }
            try { _ping.Dispose(); } catch { }
            try { _cpuTemp.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_trayIcon != null) { _trayIcon.Dispose(); _trayIcon = null; }
            if (_menu != null) { _menu.Dispose(); _menu = null; }
            base.OnFormClosing(e);
        }
    }
}

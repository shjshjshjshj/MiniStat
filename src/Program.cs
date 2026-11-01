using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiniStat
{
    public static class CrashLog
    {
        public static void Write(Exception ex)
        {
            if (ex == null) return;
            string local = null;
            string temp = null;
            try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
            try { temp = Path.GetTempPath(); } catch { }
            foreach (string root in new string[] { local, temp })
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string dir = Path.Combine(root, "MiniStat");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
                    return;
                }
                catch { }
            }
        }
    }

    public static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int processId);
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (mode == "--preview") return PreviewMode(args);
            if (mode == "--dump") return DumpMode(args);

            try { Native.SetProcessDPIAware(); } catch { }

            string tag = ReadArg(args, "--instance");
            bool createdNew;
            string mutexName = "MiniStat.Overlay.SingleInstance" + (tag.Length == 0 ? "" : "." + tag);
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    CrashLog.Write(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    CrashLog.Write(e.ExceptionObject as Exception);
                };
                try
                {
                    AppSettings settings = AppSettings.Load(tag);
                    var form = new OverlayForm(settings);
                    if (tag.Length != 0) form.Text = "MiniStat-" + tag;
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    MessageBox.Show("MiniStat을 시작하지 못했습니다." + Environment.NewLine + ex.Message,
                        "MiniStat", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            return 0;
        }

        private static string ReadArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    string v = args[i + 1];
                    foreach (char bad in Path.GetInvalidFileNameChars())
                        if (v.IndexOf(bad) >= 0) return "";
                    return v.Length > 24 ? v.Substring(0, 24) : v;
                }
            }
            return "";
        }

        private static Snapshot MockSnapshot()
        {
            var s = new Snapshot();
            s.CpuPercent = 42;
            s.GpuPercent = 67;
            s.RamPercent = 58;
            s.RamUsedGb = 18.6;
            s.RamTotalGb = 32;
            s.NetDownMbps = 12.4;
            s.NetUpMbps = 0.8;
            s.Fps = 144;
            s.FpsApp = "Cyberpunk2077.exe";
            s.PingMs = 24;
            s.CpuTemp = 62;
            s.GpuTemp = 71;
            return s;
        }

        private static int PreviewMode(string[] args)
        {
            string path = null;
            float scale = 1f;
            bool empty = false;
            int compactPair = -1;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--scale" && i + 1 < args.Length)
                {
                    float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
                }
                else if (a == "--compact" && i + 1 < args.Length)
                {
                    int p;
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out p)) compactPair = p;
                }
                else if (a == "--state" && i + 1 < args.Length)
                {
                    empty = args[++i] == "empty";
                }
                else if (!a.StartsWith("--")) path = a;
            }
            if (scale <= 0.2f || scale > 4f) scale = 1f;
            if (compactPair >= OverlayRenderer.PairCount) compactPair = OverlayRenderer.PairCount - 1;
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), "ministat-preview.png");

            bool compact = compactPair >= 0;
            int w = (int)Math.Round(OverlayRenderer.BaseWidth * scale);
            int h = (int)Math.Round(OverlayRenderer.HeightFor(compact) * scale);
            int pad = 40;
            using (var bmp = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, bmp.Width, bmp.Height),
                        Color.FromArgb(64, 73, 86), Color.FromArgb(30, 35, 43), 40f))
                    {
                        g.FillRectangle(bg, 0, 0, bmp.Width, bmp.Height);
                    }
                    GraphicsState state = g.Save();
                    g.TranslateTransform(pad, pad);
                    OverlayRenderer.Render(g, empty ? EmptySnapshot() : MockSnapshot(), DateTime.Now, 235,
                        compact, Math.Max(0, compactPair), scale);
                    g.Restore(state);
                }
                bmp.Save(path, ImageFormat.Png);
            }
            WriteConsole("preview=" + path);
            return 0;
        }

        private static Snapshot EmptySnapshot()
        {
            var s = new Snapshot();
            s.FpsStatus = "대기";
            s.CpuTempStatus = "센서 없음";
            return s;
        }

        private static int DumpMode(string[] args)
        {
            string outFile = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
            }

            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            try
            {
                var cpu = new CpuUsageSensor();
                cpu.Sample();
                Thread.Sleep(400);
                double? cpuVal = cpu.Sample();

                var mem = new MemorySensor();
                double ramPct, ramUsed, ramTotal;
                bool memOk = mem.Sample(out ramPct, out ramUsed, out ramTotal);

                var gpu = new NvmlGpuSensor();
                double? gpuPct = gpu.Utilization();
                double? gpuTemp = gpu.Temperature();

                var net = new NetSpeedSensor();
                double down, up;
                net.Sample(out down, out up);
                Thread.Sleep(600);
                net.Sample(out down, out up);

                var ping = new PingSensor("1.1.1.1");
                ping.PingOnce(1500);

                var temp = new CpuTemperatureSensor();
                temp.SampleOnce();
                string tempDetail = temp.Diagnostics;
                temp.Dispose();

                bool elevated = false;
                try
                {
                    using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                        elevated = new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { }

                var fps = new FpsReader();
                fps.EnsureRunning(elevated);
                Thread.Sleep(1500);
                sb.AppendLine("fps=" + (fps.Fps.HasValue ? fps.Fps.Value.ToString("0.0", inv) : "-"));
                sb.AppendLine("fpsStatus=" + fps.Status);
                sb.AppendLine("fpsApp=" + (fps.App.Length == 0 ? "-" : fps.App));
                sb.AppendLine("fpsError=" + (fps.LastError.Length == 0 ? "-" : fps.LastError));
                fps.Dispose();

                sb.AppendLine("cpu=" + (cpuVal.HasValue ? cpuVal.Value.ToString("0.0", inv) : "-"));
                sb.AppendLine("gpu=" + (gpuPct.HasValue ? gpuPct.Value.ToString("0", inv) : "-"));
                sb.AppendLine("gpuTemp=" + (gpuTemp.HasValue ? gpuTemp.Value.ToString("0", inv) : "-"));
                sb.AppendLine("gpuName=" + gpu.Name);
                sb.AppendLine("ram=" + (memOk ? ramPct.ToString("0", inv) : "-"));
                sb.AppendLine("ramUsedGb=" + (memOk ? ramUsed.ToString("0.0", inv) : "-"));
                sb.AppendLine("ramTotalGb=" + (memOk ? ramTotal.ToString("0.0", inv) : "-"));
                sb.AppendLine("downMbps=" + down.ToString("0.00", inv));
                sb.AppendLine("upMbps=" + up.ToString("0.00", inv));
                sb.AppendLine("ping=" + (ping.LastMs.HasValue ? ping.LastMs.Value.ToString("0", inv) : (ping.LastTimeout ? "timeout" : "-")));
                sb.AppendLine("cpuTemp=" + (temp.Value.HasValue ? temp.Value.Value.ToString("0.0", inv) : "-"));
                sb.AppendLine("cpuTempSource=" + temp.Source);
                sb.AppendLine("cpuTempStatus=" + temp.Status);
                sb.AppendLine("cpuTempDetail=" + tempDetail);
                sb.AppendLine("presentMon=" + (FpsReader.FindPresentMon() ?? "-"));
                sb.AppendLine("elevated=" + (elevated ? "1" : "0"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("error=" + ex.GetType().Name + ": " + ex.Message);
            }

            string text = sb.ToString();
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, text, Encoding.UTF8); } catch { }
            }
            WriteConsole(text);
            return 0;
        }

        private static void WriteConsole(string text)
        {
            try
            {
                if (Native.AttachConsole(-1))
                {
                    var stdout = new StreamWriter(Console.OpenStandardOutput());
                    stdout.AutoFlush = true;
                    stdout.Write(text);
                    if (!text.EndsWith("\n")) stdout.Write(Environment.NewLine);
                    stdout.Flush();
                }
            }
            catch { }
        }
    }
}

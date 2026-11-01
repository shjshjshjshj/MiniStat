using System;

namespace MiniStat
{
    public class Snapshot
    {
        public double? CpuPercent;
        public double? GpuPercent;
        public double? GpuTemp;
        public string GpuName = "";
        public double? RamPercent;
        public double? RamUsedGb;
        public double? RamTotalGb;
        public double? NetDownMbps;
        public double? NetUpMbps;
        public double? Fps;
        public string FpsApp = "";
        public string FpsStatus = "";
        public double? PingMs;
        public bool PingTimeout;
        public double? CpuTemp;
        public string CpuTempSource = "";
        public string CpuTempStatus = "";
    }

    public static class Fmt
    {
        public const string Dash = "\u2014";

        public static string Pct(double? v)
        {
            if (!v.HasValue) return Dash;
            return ((int)Math.Round(v.Value)).ToString();
        }

        public static string Temp(double? v)
        {
            if (!v.HasValue) return Dash;
            return ((int)Math.Round(v.Value)).ToString();
        }

        public static string Mbps(double? v)
        {
            if (!v.HasValue) return Dash;
            double x = v.Value;
            if (x < 0) x = 0;
            if (x < 0.05) return "0.0";
            if (x < 10) return x.ToString("0.0");
            if (x < 100) return x.ToString("0.0");
            return x.ToString("0");
        }
    }
}

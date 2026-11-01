using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MiniStat
{
    public class CpuUsageSensor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        private ulong _prevIdle, _prevKernel, _prevUser;
        private bool _hasPrev;
        private double _smoothed;
        private bool _hasSmoothed;

        public double? Sample()
        {
            FileTime idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return null;
            ulong i = ((ulong)idle.High << 32) | idle.Low;
            ulong k = ((ulong)kernel.High << 32) | kernel.Low;
            ulong u = ((ulong)user.High << 32) | user.Low;
            if (!_hasPrev)
            {
                _prevIdle = i; _prevKernel = k; _prevUser = u; _hasPrev = true;
                return _hasSmoothed ? (double?)_smoothed : null;
            }
            ulong di = i - _prevIdle;
            ulong dk = k - _prevKernel;
            ulong du = u - _prevUser;
            _prevIdle = i; _prevKernel = k; _prevUser = u;
            ulong total = dk + du;
            if (total == 0) return _hasSmoothed ? (double?)_smoothed : null;
            double instant = 100.0 * (1.0 - (double)di / (double)total);
            if (instant < 0) instant = 0;
            if (instant > 100) instant = 100;
            if (!_hasSmoothed) { _smoothed = instant; _hasSmoothed = true; }
            else _smoothed = _smoothed * 0.65 + instant * 0.35;
            return _smoothed;
        }
    }

    public class MemorySensor
    {
        [StructLayout(LayoutKind.Sequential)]
        private class MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MemoryStatusEx() { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        public bool Sample(out double percent, out double usedGb, out double totalGb)
        {
            percent = 0; usedGb = 0; totalGb = 0;
            var m = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(m)) return false;
            percent = m.dwMemoryLoad;
            totalGb = m.ullTotalPhys / 1073741824.0;
            usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            return true;
        }
    }

    public class NvmlGpuSensor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint Gpu;
            public uint Memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        private static extern int NvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int NvmlGetHandle(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        private static extern int NvmlGetUtilization(IntPtr device, ref NvmlUtilization utilization);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int NvmlGetTemperature(IntPtr device, int sensorType, out uint temperature);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        private static extern int NvmlGetMemory(IntPtr device, ref NvmlMemory memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
        private static extern int NvmlGetName(IntPtr device, StringBuilder name, uint length);

        private bool _tried;
        private bool _ready;
        private IntPtr _device = IntPtr.Zero;
        private DateTime _lastTry = DateTime.MinValue;
        private string _name = "";

        public string Name { get { return _name; } }

        public bool EnsureReady()
        {
            if (_ready) return true;
            if (_tried && (DateTime.UtcNow - _lastTry).TotalSeconds < 60) return false;
            _tried = true;
            _lastTry = DateTime.UtcNow;
            try
            {
                int rc = NvmlInit();
                if (rc != 0) return false;
                IntPtr dev;
                rc = NvmlGetHandle(0, out dev);
                if (rc != 0) return false;
                _device = dev;
                var sb = new StringBuilder(96);
                if (NvmlGetName(dev, sb, 96) == 0) _name = sb.ToString();
                _ready = true;
                return true;
            }
            catch
            {
                _ready = false;
                return false;
            }
        }

        public double? Utilization()
        {
            if (!EnsureReady()) return null;
            try
            {
                var u = new NvmlUtilization();
                if (NvmlGetUtilization(_device, ref u) != 0) return null;
                return u.Gpu;
            }
            catch { return null; }
        }

        public double? Temperature()
        {
            if (!EnsureReady()) return null;
            try
            {
                uint t;
                if (NvmlGetTemperature(_device, 0, out t) != 0) return null;
                return t;
            }
            catch { return null; }
        }

        public bool Memory(out double usedGb, out double totalGb)
        {
            usedGb = 0; totalGb = 0;
            if (!EnsureReady()) return false;
            try
            {
                var m = new NvmlMemory();
                if (NvmlGetMemory(_device, ref m) != 0) return false;
                totalGb = m.Total / 1073741824.0;
                usedGb = m.Used / 1073741824.0;
                return true;
            }
            catch { return false; }
        }
    }

    public class NetSpeedSensor
    {
        private ulong _prevRx;
        private ulong _prevTx;
        private bool _hasPrev;
        private DateTime _prevAt = DateTime.MinValue;
        private NetworkInterface[] _ifaces;
        private DateTime _ifacesAt = DateTime.MinValue;

        private NetworkInterface[] Interfaces()
        {
            if (_ifaces != null && _ifaces.Length > 0 && (DateTime.UtcNow - _ifacesAt).TotalSeconds < 30) return _ifaces;
            try
            {
                var list = new List<NetworkInterface>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var t = ni.NetworkInterfaceType;
                    if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
                    list.Add(ni);
                }
                _ifaces = list.ToArray();
                _ifacesAt = DateTime.UtcNow;
            }
            catch { }
            return _ifaces ?? new NetworkInterface[0];
        }

        public bool Sample(out double downMbps, out double upMbps)
        {
            downMbps = 0; upMbps = 0;
            ulong rx = 0, tx = 0;
            try
            {
                foreach (var ni in Interfaces())
                {
                    IPv4InterfaceStatistics st;
                    try { st = ni.GetIPv4Statistics(); }
                    catch { continue; }
                    if (st.BytesReceived < 0 || st.BytesSent < 0) continue;
                    rx += (ulong)st.BytesReceived;
                    tx += (ulong)st.BytesSent;
                }
            }
            catch { return _hasPrev; }

            DateTime now = DateTime.UtcNow;
            if (!_hasPrev)
            {
                _prevRx = rx; _prevTx = tx; _prevAt = now; _hasPrev = true;
                return false;
            }
            double seconds = (now - _prevAt).TotalSeconds;
            if (seconds < 0.05) { return true; }
            double drx = rx >= _prevRx ? (double)(rx - _prevRx) : 0;
            double dtx = tx >= _prevTx ? (double)(tx - _prevTx) : 0;
            _prevRx = rx; _prevTx = tx; _prevAt = now;
            downMbps = drx * 8.0 / 1000000.0 / seconds;
            upMbps = dtx * 8.0 / 1000000.0 / seconds;
            return true;
        }
    }

    public class PingSensor : IDisposable
    {
        private readonly string _target;
        private readonly object _lock = new object();
        private bool _busy;
        private DateTime _lastAt = DateTime.MinValue;
        private double? _ms;
        private bool _timeout;
        private bool _disposed;

        public PingSensor(string target)
        {
            _target = target;
        }

        public string Target { get { return _target; } }

        public double? LastMs
        {
            get { lock (_lock) { return _ms; } }
        }

        public bool LastTimeout
        {
            get { lock (_lock) { return _timeout; } }
        }

        public void Poll()
        {
            lock (_lock)
            {
                if (_disposed || _busy) return;
                if ((DateTime.UtcNow - _lastAt).TotalSeconds < 1.8) return;
                _busy = true;
                _lastAt = DateTime.UtcNow;
            }
            try
            {
                Task<long?> task = Task.Run<long?>(delegate
                {
                    try
                    {
                        using (var p = new Ping())
                        {
                            var reply = p.Send(_target, 1200);
                            if (reply == null) return null;
                            if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
                            return null;
                        }
                    }
                    catch { return null; }
                });
                task.ContinueWith(delegate(Task<long?> t)
                {
                    lock (_lock)
                    {
                        _busy = false;
                        if (t.Result.HasValue) { _ms = t.Result.Value; _timeout = false; }
                        else { _timeout = true; }
                    }
                });
            }
            catch
            {
                lock (_lock) { _busy = false; _timeout = true; }
            }
        }

        public void PingOnce(int timeoutMs)
        {
            try
            {
                using (var p = new Ping())
                {
                    var reply = p.Send(_target, timeoutMs);
                    lock (_lock)
                    {
                        if (reply != null && reply.Status == IPStatus.Success) { _ms = reply.RoundtripTime; _timeout = false; }
                        else { _timeout = true; }
                    }
                }
            }
            catch { lock (_lock) { _timeout = true; } }
        }

        public void Dispose()
        {
            lock (_lock) { _disposed = true; }
        }
    }
}

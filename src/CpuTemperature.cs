using System;
using System.Collections;
using System.IO;
using System.Management;
using System.Reflection;
using System.Threading;

namespace MiniStat
{
    public class CpuTemperatureSensor : IDisposable
    {
        private readonly object _lock = new object();
        private double? _value;
        private string _source = "";
        private string _status = "측정 중";
        private int _failures;
        private DateTime _lastAttempt = DateTime.MinValue;
        private bool _disposed;
        private Thread _thread;
        private object _computer;
        private PropertyInfo _hardwareProp;
        private MethodInfo _updateMethod;
        private bool _lhmTried;
        private bool _lhmLoaded;
        private bool _resolveHooked;
        private string _lhmError = "";
        private string _lhmSensor = "";
        private DateTime _lastLog = DateTime.MinValue;

        public double? Value { get { lock (_lock) { return _value; } } }
        public string Source { get { lock (_lock) { return _source; } } }
        public string Status { get { lock (_lock) { return _status; } } }

        public string Diagnostics
        {
            get
            {
                lock (_lock)
                {
                    return "lhmLoaded=" + (_lhmLoaded ? "1" : "0")
                        + " lhmError=" + (_lhmError.Length == 0 ? "-" : _lhmError)
                        + " lhmSensor=" + (_lhmSensor.Length == 0 ? "-" : _lhmSensor);
                }
            }
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "MiniStat.CpuTemp";
            _thread.Start();
        }

        private void Loop()
        {
            while (!_disposed)
            {
                SampleOnce();
                for (int i = 0; i < 100 && !_disposed; i++) Thread.Sleep(100);
            }
        }

        public void SampleOnce()
        {
            if (!_lhmTried) TryInitLibreHardwareMonitor();
            if (_computer != null)
            {
                double? lhm = ReadLibreHardwareMonitor();
                if (lhm.HasValue)
                {
                    lock (_lock) { _value = lhm; _source = "LibreHardwareMonitor"; _status = ""; }
                    return;
                }
            }

            if (_failures > 2 && (DateTime.UtcNow - _lastAttempt).TotalSeconds < 60) return;
            _lastAttempt = DateTime.UtcNow;

            bool denied;
            double? acpi = ReadAcpiThermalZone(out denied);
            string source = "ACPI";
            if (!acpi.HasValue)
            {
                bool denied2;
                double? perf = ReadThermalZonePerf(out denied2);
                denied = denied || denied2;
                if (perf.HasValue) { acpi = perf; source = "ThermalZone"; }
            }

            lock (_lock)
            {
                if (acpi.HasValue)
                {
                    _value = acpi;
                    _source = source;
                    _status = "";
                    _failures = 0;
                }
                else
                {
                    _failures++;
                    _value = null;
                    _source = "";
                    _status = denied ? "권한 필요" : "센서 없음";
                    LogDiagnosticsIfDue();
                }
            }
        }

        private void TryInitLibreHardwareMonitor()
        {
            _lhmTried = true;
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string dll = Path.Combine(dir, "LibreHardwareMonitorLib.dll");
                if (!File.Exists(dll))
                {
                    _lhmError = "dll not found in " + dir;
                    return;
                }
                if (!_resolveHooked)
                {
                    _resolveHooked = true;
                    AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
                }
                Assembly asm = Assembly.LoadFrom(dll);
                Type t = asm.GetType("LibreHardwareMonitor.Hardware.Computer");
                if (t == null)
                {
                    _lhmError = "Computer type missing in " + asm.GetName().Name;
                    return;
                }
                object computer = Activator.CreateInstance(t);
                PropertyInfo cpu = t.GetProperty("IsCpuEnabled");
                if (cpu != null) cpu.SetValue(computer, true, null);
                foreach (string name in new string[] { "IsGpuEnabled", "IsMotherboardEnabled", "IsMemoryEnabled", "IsStorageEnabled", "IsNetworkEnabled" })
                {
                    PropertyInfo p = t.GetProperty(name);
                    if (p != null) p.SetValue(computer, false, null);
                }
                MethodInfo open = t.GetMethod("Open", Type.EmptyTypes);
                if (open != null) open.Invoke(computer, null);
                _hardwareProp = t.GetProperty("Hardware");
                _updateMethod = null;
                _computer = computer;
                _lhmLoaded = true;
                _lhmError = "";
            }
            catch (Exception ex)
            {
                _computer = null;
                _hardwareProp = null;
                _lhmLoaded = false;
                _lhmError = Describe(ex);
            }
        }

        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = new AssemblyName(args.Name).Name + ".dll";
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, simple);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            catch { }
            return null;
        }

        private static string Describe(Exception ex)
        {
            Exception e = ex;
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        private double? ReadLibreHardwareMonitor()
        {
            try
            {
                if (_hardwareProp == null) return null;
                IEnumerable list = _hardwareProp.GetValue(_computer, null) as IEnumerable;
                if (list == null) return null;
                double best = double.MinValue;
                int bestScore = int.MinValue;
                string bestName = "";
                foreach (object hw in list)
                {
                    string typeName = GetStringProperty(hw, "HardwareType");
                    if (typeName != "Cpu") continue;
                    if (_updateMethod == null) _updateMethod = hw.GetType().GetMethod("Update", Type.EmptyTypes);
                    if (_updateMethod != null) _updateMethod.Invoke(hw, null);
                    PickSensors(hw, ref best, ref bestScore, ref bestName);
                    foreach (object sub in GetEnumerable(hw, "SubHardware"))
                    {
                        if (_updateMethod != null) _updateMethod.Invoke(sub, null);
                        PickSensors(sub, ref best, ref bestScore, ref bestName);
                    }
                }
                if (best == double.MinValue) return null;
                if (bestName.Length > 0) _lhmSensor = bestName;
                return best;
            }
            catch (Exception ex)
            {
                _computer = null;
                _lhmLoaded = false;
                _lhmError = Describe(ex);
                return null;
            }
        }

        private static void PickSensors(object hw, ref double best, ref int bestScore, ref string bestName)
        {
            foreach (object sensor in GetEnumerable(hw, "Sensors"))
            {
                if (GetStringProperty(sensor, "SensorType") != "Temperature") continue;
                object val = sensor.GetType().GetProperty("Value").GetValue(sensor, null);
                if (val == null) continue;
                double d = Convert.ToDouble(val);
                if (!(d > 0 && d < 130)) continue;
                string name = GetStringProperty(sensor, "Name");
                int score = ScoreSensor(name);
                if (score > bestScore || (score == bestScore && d > best))
                {
                    bestScore = score;
                    best = d;
                    bestName = name;
                }
            }
        }

        // Ryzen boards expose several temperature sensors. Tctl/Tdie is what
        // people read as "CPU temperature", so it wins over CCD or generic ones.
        private static int ScoreSensor(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.IndexOf("tctl", StringComparison.Ordinal) >= 0) return 100;
            if (n.IndexOf("package", StringComparison.Ordinal) >= 0) return 85;
            if (n.IndexOf("core max", StringComparison.Ordinal) >= 0) return 75;
            if (n.IndexOf("ccd", StringComparison.Ordinal) >= 0) return 50;
            return 10;
        }

        private static IEnumerable GetEnumerable(object obj, string property)
        {
            try
            {
                PropertyInfo p = obj.GetType().GetProperty(property);
                if (p == null) return new object[0];
                return p.GetValue(obj, null) as IEnumerable ?? new object[0];
            }
            catch { return new object[0]; }
        }

        private static string GetStringProperty(object obj, string property)
        {
            try
            {
                PropertyInfo p = obj.GetType().GetProperty(property);
                if (p == null) return "";
                object v = p.GetValue(obj, null);
                return v == null ? "" : v.ToString();
            }
            catch { return ""; }
        }

        private double? ReadAcpiThermalZone(out bool denied)
        {
            denied = false;
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    double best = double.MinValue;
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        try
                        {
                            object raw = mo["CurrentTemperature"];
                            if (raw == null) continue;
                            double celsius = Convert.ToDouble(raw) / 10.0 - 273.15;
                            if (celsius > 1 && celsius < 125) best = Math.Max(best, celsius);
                        }
                        catch { }
                    }
                    if (best == double.MinValue) return null;
                    return best;
                }
            }
            catch (Exception ex)
            {
                denied = IsAccessDenied(ex);
                return null;
            }
        }

        private double? ReadThermalZonePerf(out bool denied)
        {
            denied = false;
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                {
                    double best = double.MinValue;
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        double celsius = double.NaN;
                        try
                        {
                            object hp = mo["HighPrecisionTemperature"];
                            if (hp != null) celsius = Convert.ToDouble(hp) / 10.0 - 273.15;
                        }
                        catch { }
                        if (double.IsNaN(celsius) || celsius <= 1 || celsius >= 125)
                        {
                            try
                            {
                                object t = mo["Temperature"];
                                if (t != null) celsius = Convert.ToDouble(t) / 10.0 - 273.15;
                            }
                            catch { }
                        }
                        if (celsius > 1 && celsius < 125) best = Math.Max(best, celsius);
                    }
                    if (best == double.MinValue) return null;
                    return best;
                }
            }
            catch (Exception ex)
            {
                denied = IsAccessDenied(ex);
                return null;
            }
        }

        private static bool IsAccessDenied(Exception ex)
        {
            if (ex == null) return false;
            if (ex is UnauthorizedAccessException) return true;
            string m = ex.Message ?? "";
            return m.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("액세스", StringComparison.Ordinal) >= 0
                || m.IndexOf("거부", StringComparison.Ordinal) >= 0
                || m.IndexOf("0x80041003", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("0x80070005", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Written at most once a minute while the temperature is unavailable, so
        // a report of "센서 없음" comes with the actual sensor tree attached.
        private void LogDiagnosticsIfDue()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastLog).TotalSeconds < 60) return;
            _lastLog = now;
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(root, "MiniStat");
                Directory.CreateDirectory(dir);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] status=" + _status
                    + " elevated=" + (IsElevatedProcess() ? "1" : "0")
                    + " lhmLoaded=" + (_lhmLoaded ? "1" : "0")
                    + " lhmError=" + _lhmError);
                sb.AppendLine("  " + DumpLibreHardwareMonitor());
                File.AppendAllText(Path.Combine(dir, "sensors.log"), sb.ToString());
            }
            catch { }
        }

        private static bool IsElevatedProcess()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private string DumpLibreHardwareMonitor()
        {
            if (_computer == null || _hardwareProp == null) return "lhm=none";
            try
            {
                var sb = new System.Text.StringBuilder();
                IEnumerable list = _hardwareProp.GetValue(_computer, null) as IEnumerable;
                if (list == null) return "hardware=null";
                foreach (object hw in list)
                {
                    sb.Append(GetStringProperty(hw, "HardwareType") + ":" + GetStringProperty(hw, "Name") + " [");
                    MethodInfo upd = hw.GetType().GetMethod("Update", Type.EmptyTypes);
                    if (upd != null) upd.Invoke(hw, null);
                    foreach (object sensor in GetEnumerable(hw, "Sensors"))
                    {
                        object val = sensor.GetType().GetProperty("Value").GetValue(sensor, null);
                        sb.Append(GetStringProperty(sensor, "SensorType") + "/" + GetStringProperty(sensor, "Name")
                            + "=" + (val == null ? "null" : val.ToString()) + " ");
                    }
                    sb.Append("] ");
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex) { return "dump error: " + Describe(ex); }
        }

        public void Dispose()
        {
            _disposed = true;
            try
            {
                if (_computer != null)
                {
                    MethodInfo close = _computer.GetType().GetMethod("Close", Type.EmptyTypes);
                    if (close != null) close.Invoke(_computer, null);
                }
            }
            catch { }
            _computer = null;
        }
    }
}

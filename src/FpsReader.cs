using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace MiniStat
{
    public class FpsReader : IDisposable
    {
        private class ProcWindow
        {
            public readonly List<double> FrameMs = new List<double>();
            public readonly List<DateTime> FrameAt = new List<DateTime>();
            public DateTime LastFrame = DateTime.MinValue;
        }

        private static readonly string[] FrameTimeColumns = new string[]
        {
            "msBetweenPresents",
            "msBetweenDisplayChange",
            "msGPUFrameTime",
            "FrameTime"
        };

        private readonly object _lock = new object();
        private readonly Dictionary<int, ProcWindow> _windows = new Dictionary<int, ProcWindow>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private Process _proc;
        private bool _stopping;
        private bool _usedLegacyArgs;
        private bool _startFailed;
        private string _status = "시작 중";
        private double? _fps;
        private string _app = "";
        private int _appIdx = -1, _pidIdx = -1, _msIdx = -1;
        private DateTime _lastTrim = DateTime.MinValue;
        private DateTime _nextTry = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;
        private int _startAttempts;
        private readonly int _selfPid = Process.GetCurrentProcess().Id;
        private readonly string _sessionName;
        private string _lastError = "";
        private bool _reaped;

        public FpsReader()
        {
            _sessionName = "MiniStat" + _selfPid;
        }

        public string Status { get { lock (_lock) { return _status; } } }
        public double? Fps { get { lock (_lock) { return _fps; } } }
        public string App { get { lock (_lock) { return _app; } } }
        public string LastError { get { lock (_lock) { return _lastError; } } }

        public static string FindPresentMon()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(dir, "PresentMon_x64.exe"),
                Path.Combine(dir, "PresentMon.exe"),
                Path.Combine(dir, "tools\\PresentMon_x64.exe"),
                @"C:\Program Files\NVIDIA Corporation\FrameViewSDK\bin\PresentMon_x64.exe",
                @"C:\Program Files\PresentMon\PresentMon.exe",
                @"C:\Program Files (x86)\PresentMon\PresentMon.exe"
            };
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch { }
            }
            return null;
        }

        public void EnsureRunning(bool elevated)
        {
            if (_stopping) return;
            if (!elevated)
            {
                lock (_lock) { _status = "관리자 필요"; _fps = null; }
                return;
            }
            if (_proc != null && !_proc.HasExited) return;
            if (_startFailed) return;
            if (DateTime.UtcNow < _nextTry) return;
            ReapOrphans();

            string exe = FindPresentMon();
            if (exe == null)
            {
                lock (_lock) { _status = "없음"; _fps = null; }
                _startFailed = true;
                return;
            }

            _startAttempts++;
            if (_startAttempts > 5)
            {
                // Keep trying, just slowly: the environment can change while
                // the overlay is running.
                _startAttempts = 0;
                _nextTry = DateTime.UtcNow.AddSeconds(45);
                lock (_lock) { _status = "실패"; _fps = null; }
                return;
            }

            _appIdx = -1;
            _pidIdx = -1;
            _msIdx = -1;
            _startedAt = DateTime.UtcNow;
            _nextTry = DateTime.UtcNow.AddSeconds(10);
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                if (_usedLegacyArgs)
                    psi.Arguments = "-output_stdout -no_top -stop_existing_session";
                else
                    psi.Arguments = "--output_stdout --no_console_stats --session_name " + _sessionName + " --stop_existing_session";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                var proc = new Process();
                proc.StartInfo = psi;
                proc.EnableRaisingEvents = true;
                proc.OutputDataReceived += OnOutput;
                proc.ErrorDataReceived += OnError;
                proc.Exited += OnExited;
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                _proc = proc;
                lock (_lock) { _status = "대기"; }
            }
            catch
            {
                _nextTry = DateTime.UtcNow.AddSeconds(10);
                lock (_lock) { _status = "실패"; _fps = null; }
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            Process proc = sender as Process;
            if (_proc == proc) _proc = null;
            bool quick = (DateTime.UtcNow - _startedAt).TotalSeconds < 4;

            lock (_lock)
            {
                _fps = null;
                _app = "";
                _windows.Clear();
            }

            if (!quick)
            {
                _nextTry = DateTime.UtcNow.AddSeconds(2);
                _startAttempts = 0;
                lock (_lock) { _status = "재시작"; }
                return;
            }

            if (!_usedLegacyArgs)
            {
                _usedLegacyArgs = true;
                _nextTry = DateTime.MinValue;
                _startAttempts = 0;
                lock (_lock) { _status = "재시도"; }
                return;
            }

            _nextTry = DateTime.UtcNow.AddSeconds(15);
            lock (_lock) { _status = "실패"; }
        }

        private void OnError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            string raw = e.Data.Trim();
            if (raw.Length > 0)
            {
                if (raw.Length > 300) raw = raw.Substring(0, 300);
                lock (_lock) { _lastError = raw; }
            }
            string t = e.Data.ToLowerInvariant();
            if (t.IndexOf("admin", StringComparison.Ordinal) >= 0 || t.IndexOf("elevat", StringComparison.Ordinal) >= 0)
                lock (_lock) { _status = "관리자 필요"; }
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            try { ParseLine(e.Data); }
            catch { }
        }

        private void ParseLine(string line)
        {
            if (line.Length == 0) return;
            if (line.IndexOf("ProcessID", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("Application", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ParseHeader(line);
                return;
            }
            if (_pidIdx < 0) return;

            string[] parts = line.Split(',');
            if (parts.Length <= Math.Max(_pidIdx, 0)) return;
            int pid;
            if (!int.TryParse(parts[_pidIdx].Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) return;
            if (pid <= 4 || pid == _selfPid) return;
            string name = _appIdx >= 0 && parts.Length > _appIdx ? Path.GetFileName(parts[_appIdx].Trim().Trim('"')) : "";
            string lower = name.ToLowerInvariant();
            if (lower == "dwm.exe" || lower == "explorer.exe" || lower == "lockapp.exe" || lower.StartsWith("presentmon")) return;

            double ms = 0;
            if (_msIdx >= 0 && parts.Length > _msIdx)
                double.TryParse(parts[_msIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ms);
            if (ms <= 0 || ms > 1000) return;

            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                ProcWindow w;
                if (!_windows.TryGetValue(pid, out w))
                {
                    w = new ProcWindow();
                    _windows[pid] = w;
                }
                w.FrameMs.Add(ms);
                w.FrameAt.Add(now);
                w.LastFrame = now;
                _names[pid] = name;
                TrimLocked(now);
                RecomputeLocked();
            }
        }

        private void ParseHeader(string line)
        {
            if (line.IndexOf("ProcessID", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (line.IndexOf("Application", StringComparison.OrdinalIgnoreCase) < 0) return;

            string[] header = line.Split(',');
            int app = -1, pid = -1, ms = -1;
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim().Trim('"');
                if (string.Equals(h, "Application", StringComparison.OrdinalIgnoreCase)) app = i;
                else if (string.Equals(h, "ProcessID", StringComparison.OrdinalIgnoreCase)) pid = i;
            }
            for (int c = 0; c < FrameTimeColumns.Length && ms < 0; c++)
            {
                for (int i = 0; i < header.Length; i++)
                {
                    if (string.Equals(header[i].Trim().Trim('"'), FrameTimeColumns[c], StringComparison.OrdinalIgnoreCase))
                    {
                        ms = i;
                        break;
                    }
                }
            }
            if (pid < 0 || ms < 0) return;
            _appIdx = app;
            _pidIdx = pid;
            _msIdx = ms;
            lock (_lock)
            {
                _windows.Clear();
                _names.Clear();
                _fps = null;
                _app = "";
                if (_status != "관리자 필요") _status = "대기";
            }
        }

        private void TrimLocked(DateTime now)
        {
            if ((now - _lastTrim).TotalMilliseconds < 250) return;
            _lastTrim = now;
            List<int> dead = new List<int>();
            foreach (var kv in _windows)
            {
                ProcWindow w = kv.Value;
                while (w.FrameAt.Count > 0 && (now - w.FrameAt[0]).TotalSeconds > 1.2)
                {
                    w.FrameAt.RemoveAt(0);
                    w.FrameMs.RemoveAt(0);
                }
                if (w.FrameAt.Count == 0 && (now - w.LastFrame).TotalSeconds > 3) dead.Add(kv.Key);
            }
            foreach (int pid in dead)
            {
                _windows.Remove(pid);
                _names.Remove(pid);
            }
        }

        private void RecomputeLocked()
        {
            int bestPid = 0;
            double bestFps = 0;
            foreach (var kv in _windows)
            {
                ProcWindow w = kv.Value;
                if (w.FrameMs.Count < 4) continue;
                double sum = 0;
                for (int i = 0; i < w.FrameMs.Count; i++) sum += w.FrameMs[i];
                double avg = sum / w.FrameMs.Count;
                if (avg <= 0) continue;
                double fps = 1000.0 / avg;
                if (fps > 1000) fps = 1000;
                if (fps > bestFps) { bestFps = fps; bestPid = kv.Key; }
            }
            if (bestPid == 0)
            {
                _fps = null;
                _app = "";
                if (_status == "동작") _status = "대기";
                return;
            }
            _fps = bestFps;
            string nm;
            _app = _names.TryGetValue(bestPid, out nm) ? nm : "";
            _status = "동작";
        }

        // A PresentMon left behind by a previous MiniStat (crash, kill from Task
        // Manager) keeps its ETW session and its CPU cost. Only ours are reaped:
        // the command line has to carry a MiniStat session name and the parent
        // process has to be gone.
        private void ReapOrphans()
        {
            if (_reaped) return;
            _reaped = true;
            try
            {
                const string wql = "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'PresentMon_x64.exe'";
                using (var searcher = new System.Management.ManagementObjectSearcher(wql))
                {
                    foreach (System.Management.ManagementBaseObject mo in searcher.Get())
                    {
                        try
                        {
                            string cmd = Convert.ToString(mo["CommandLine"]) ?? "";
                            if (cmd.IndexOf("MiniStat", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (cmd.IndexOf(_sessionName, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            int ppid = Convert.ToInt32(mo["ParentProcessId"]);
                            if (ppid == _selfPid) continue;
                            if (ProcessExists(ppid)) continue;
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            using (Process orphan = Process.GetProcessById(pid)) orphan.Kill();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static bool ProcessExists(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) return !p.HasExited;
            }
            catch { return false; }
        }

        // Releases the ETW session even if our child died without cleaning up.
        private void StopSession()
        {
            try
            {
                string exe = FindPresentMon();
                if (exe == null || _usedLegacyArgs) return;
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = "--terminate_existing_session --session_name " + _sessionName;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                using (Process p = Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(3000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _stopping = true;
            Process proc = _proc;
            _proc = null;
            try
            {
                if (proc != null && !proc.HasExited) proc.Kill();
            }
            catch { }
            StopSession();
        }
    }
}

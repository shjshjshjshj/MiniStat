using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace MiniStat
{
    // A logon scheduled task instead of a Startup shortcut: the exe demands
    // administrator, so a shortcut would raise a UAC prompt at every logon.
    // A task registered with HighestAvailable starts elevated without one.
    public static class AutoStart
    {
        public const string TaskName = "MiniStat";

        private static bool _cached;
        private static DateTime _cachedAt = DateTime.MinValue;

        public static void Invalidate()
        {
            _cachedAt = DateTime.MinValue;
        }

        public static bool IsEnabled()
        {
            if ((DateTime.UtcNow - _cachedAt).TotalSeconds < 3) return _cached;
            bool on = false;
            try { on = Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", 8000) == 0; }
            catch { }
            _cached = on;
            _cachedAt = DateTime.UtcNow;
            return on;
        }

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool Enable(out string error)
        {
            error = "";
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string user;
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent()) user = id.Name;
            }
            catch { user = Environment.UserDomainName + "\\" + Environment.UserName; }

            string file = Path.Combine(Path.GetTempPath(), "ministat-task.xml");
            try { File.WriteAllText(file, BuildTaskXml(user, exe, dir), Encoding.Unicode); }
            catch (Exception e) { error = e.Message; return false; }

            int code;
            try { code = Run("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F", 20000); }
            finally { try { File.Delete(file); } catch { } }

            Invalidate();
            if (code != 0 || !IsEnabled())
            {
                error = IsElevated()
                    ? "작업 스케줄러가 작업을 만들지 못했습니다. (코드 " + code + ")"
                    : "관리자 권한이 필요합니다. MiniStat을 관리자 권한으로 실행한 뒤 다시 시도해 주세요.";
                return false;
            }
            return true;
        }

        public static bool Disable(out string error)
        {
            error = "";
            Run("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", 15000);
            Invalidate();
            if (IsEnabled())
            {
                error = "작업 스케줄러에서 작업을 지우지 못했습니다. 관리자 권한으로 다시 시도해 주세요.";
                return false;
            }
            return true;
        }

        private static int Run(string file, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo(file, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(psi))
            {
                if (p == null) return -1;
                string ignored = p.StandardOutput.ReadToEnd();
                ignored += p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return -1;
                }
                return p.ExitCode;
            }
        }

        private static string BuildTaskXml(string user, string exe, string workingDirectory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo><Description>MiniStat system monitor overlay</Description></RegistrationInfo>");
            sb.AppendLine("  <Triggers><LogonTrigger><Enabled>true</Enabled>");
            sb.AppendLine("    <UserId>" + Esc(user) + "</UserId><Delay>PT30S</Delay>");
            sb.AppendLine("  </LogonTrigger></Triggers>");
            sb.AppendLine("  <Principals><Principal id=\"Author\">");
            sb.AppendLine("    <UserId>" + Esc(user) + "</UserId>");
            sb.AppendLine("    <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("    <RunLevel>HighestAvailable</RunLevel>");
            sb.AppendLine("  </Principal></Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
            sb.AppendLine("    <WakeToRun>false</WakeToRun>");
            sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>6</Priority>");
            // At logon the desktop is still settling; starting too early kills the
            // process in the loader (0xc0000142) before any managed code runs.
            // Retry a few times so a failed early start recovers on its own.
            sb.AppendLine("    <RestartOnFailure><Interval>PT30S</Interval><Count>3</Count></RestartOnFailure>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\"><Exec>");
            sb.AppendLine("    <Command>" + Esc(exe) + "</Command>");
            sb.AppendLine("    <WorkingDirectory>" + Esc(workingDirectory) + "</WorkingDirectory>");
            sb.AppendLine("  </Exec></Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }

        private static string Esc(string value)
        {
            if (value == null) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}

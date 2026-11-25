using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace MotionDesk.Services
{
    /// <summary>
    /// Opt-in "start Opsis before the other start-up apps" (plan §22-A.1): a per-user logon task of the Windows Task Scheduler. A task started at logon has NO start-up
    /// delay (the Run key waits until the desktop is up) and a higher priority, so the desktop layer, the DeskStrip and the plugins (Crawler) load with the desktop.
    /// The Run-key entry stays as a fallback: a second start is refused by the single-instance guard. No administrator rights (limited token, current user only).
    /// Never runs in an isolated test / audit run (OPSIS_APPDATA).
    /// </summary>
    public static class StartupTask
    {
        public const string TaskName = "Opsis\\Start with the desktop";

        public static bool Isolated => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSIS_APPDATA"));

        /// <summary>The task definition (Task Scheduler 1.2 schema). Pure: tested without touching the scheduler.</summary>
        public static string BuildXml(string exePath, string userId)
        {
            string exe = SecurityElement.Escape(exePath) ?? "";
            string user = SecurityElement.Escape(userId) ?? "";
            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Starts Opsis with the desktop, before the other start-up apps. Created from Opsis Settings; removed when the setting is turned off.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{exe}</Command><Arguments>--background</Arguments></Exec></Actions>
</Task>";
        }

        public static string CreateArguments(string xmlFile) => $"/Create /TN \"{TaskName}\" /XML \"{xmlFile}\" /F";
        public static string DeleteArguments => $"/Delete /TN \"{TaskName}\" /F";

        /// <summary>Creates or removes the task. Returns false when the scheduler refused (the Run key still starts Opsis, just later).</summary>
        public static bool Set(bool enable)
        {
            if (Isolated) return true;   // tests, UI audit, CI: never touch the real Task Scheduler
            try
            {
                string schtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                if (!File.Exists(schtasks)) return false;
                if (!enable) return Run(schtasks, DeleteArguments) || !Exists(schtasks);
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                string xml = Path.Combine(Path.GetTempPath(), "opsis-start-task-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(xml, BuildXml(exe, Environment.UserDomainName + "\\" + Environment.UserName), Encoding.Unicode);
                    return Run(schtasks, CreateArguments(xml));
                }
                finally { try { File.Delete(xml); } catch (Exception) { } }
            }
            catch (Exception) { return false; }
        }

        public static bool IsEnabled()
        {
            if (Isolated) return false;
            try { return Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe")); } catch (Exception) { return false; }
        }

        private static bool Exists(string schtasks) => Run(schtasks, $"/Query /TN \"{TaskName}\"");

        private static bool Run(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return false;
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } return false; }
            return p.ExitCode == 0;
        }
    }
}

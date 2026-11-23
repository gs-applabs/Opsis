using System.Diagnostics;
using System.Security.Principal;

namespace MotionDesk.Services
{
    /// <summary>
    /// Windows blocks drag and drop from Explorer / the desktop into a window of a more privileged process (UIPI), so an Opsis that runs elevated can never receive the files the user drops on
    /// a container. The installer's "launch Opsis" step used to start the app with the installer's elevated token. A plain start that finds itself elevated therefore starts the app again through
    /// Explorer (normal rights) and exits. A stamp file prevents a loop when UAC is off and every process counts as elevated.
    /// </summary>
    internal static class ElevationGuard
    {
        public static bool IsElevated() { try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch (Exception) { return false; } }

        /// <summary>Returns true when the app was started again with normal rights and this process should exit.</summary>
        public static bool RelaunchNotElevated()
        {
            try
            {
                if (!IsElevated()) return false;
                var stamp = Path.Combine(Path.GetTempPath(), "opsis-relaunch.stamp");
                if (File.Exists(stamp) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromSeconds(60)) return false;     // already tried: keep running (UAC off)
                File.WriteAllText(stamp, "1");
                var exe = Environment.ProcessPath; if (string.IsNullOrEmpty(exe)) return false;
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

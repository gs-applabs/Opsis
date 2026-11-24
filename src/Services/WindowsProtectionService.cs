using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;

namespace MotionDesk.Services;

/// <summary>
/// Use Windows' official System Protection facilities. Opsis does not edit or
/// restore Windows-protected files. Restore point creation is explicit and may
/// be unavailable due to disabled protection, policy, elevation or throttling.
/// </summary>
public static class WindowsProtectionService
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void OpenProtectionSettings()
    {
        Process.Start(new ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true });
    }

    public static async Task<int> RequestSystemRestorePointAsync()
    {
        // Pass a fixed script, never external input. PowerShell exits with a
        // nonzero code on error instead of reporting false success to the UI.
        const string script = "$ErrorActionPreference='Stop'; " +
            "Checkpoint-Computer -Description 'Opsis pre-change safety' -RestorePointType 'MODIFY_SETTINGS'; " +
            "if (-not $?) { exit 1 }";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\""
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows declined to launch System Restore.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}

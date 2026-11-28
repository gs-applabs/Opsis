using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using MotionDesk.UI;

namespace MotionDesk.Services;

// Same Opsis apphost, installed beside Opsis.dll as Opsis.scr. Handle SCR
// commands before the normal application mutex or any background service.
internal static class OpsisScreensaverHost
{
    internal enum Command { Invalid, Configure, Fullscreen, Preview }
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string BackupKey = @"Software\Opsis\ScreensaverBackup";
    private const string ValueName = "SCRNSAVE.EXE";
    private static string SaverPath => Path.Combine(AppContext.BaseDirectory, "Opsis.scr");
    internal static bool IsScreenSaverProcess => string.Equals(Path.GetExtension(Environment.ProcessPath), ".scr", StringComparison.OrdinalIgnoreCase);
    internal static (Command Mode, IntPtr Parent) Parse(string[] args)
    {
        if (args.Length == 0) return (Command.Configure, IntPtr.Zero);
        string first = args[0].ToLowerInvariant();
        if (first is "/s" or "-s" && args.Length == 1) return (Command.Fullscreen, IntPtr.Zero);
        if (first is "/c" or "-c" && args.Length <= 2 || (first.StartsWith("/c:") || first.StartsWith("-c:")) && args.Length == 1)
            return (Command.Configure, IntPtr.Zero);
        string? value = null;
        if ((first is "/p" or "-p") && args.Length == 2) value = args[1];
        else if ((first.StartsWith("/p:") || first.StartsWith("-p:")) && args.Length == 1) value = args[0][3..];
        if (value != null && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long handle) &&
            handle > 0 && (IntPtr.Size == 8 || handle <= int.MaxValue))
            return (Command.Preview, new IntPtr(handle));
        return (Command.Invalid, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    internal static bool TryPreviewSize(IntPtr parent, out Size size)
    {
        size = Size.Empty;
        if (parent == IntPtr.Zero || !IsWindow(parent) || !GetClientRect(parent, out var r)) return false;
        size = new Size(Math.Max(0, r.Right-r.Left), Math.Max(0, r.Bottom-r.Top));
        return true;
    }

    internal static void Run(string[] args)
    {
        var (mode, parent) = Parse(args);
        if (mode == Command.Invalid || mode == Command.Preview && !TryPreviewSize(parent, out _)) return;
        ApplicationConfiguration.Initialize();
        if (mode == Command.Configure)
        {
            using var config = new OpsisScreensaverSettingsForm();
            Application.Run(config);
            return;
        }
        try
        {
            OpsisScreensaverPreviewForm form;
            try { form = new OpsisScreensaverPreviewForm(OpsisScreensaverStudio.Read(), parent, mode == Command.Fullscreen); }
            catch (Exception ex)
            {
                RotatingLogger.Write("Screensaver profile fallback: " + ex.Message);
                form = new OpsisScreensaverPreviewForm(new OpsisScreensaverStudio.Profile(), parent, mode == Command.Fullscreen);
            }
            using (form) Application.Run(form);
        }
        catch (Exception ex) { RotatingLogger.Write("Screensaver could not start: " + ex.Message); }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    internal static bool IsInstalled => File.Exists(SaverPath);
    private static void CheckWritable()
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold)
            throw new InvalidOperationException("Windows compatibility review required.");
    }
    private static void VerifyInstalledCopy()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Opsis.exe");
        if (!PackagePaths.IsExistingUnlinkedPath(AppContext.BaseDirectory, SaverPath) ||
            !PackagePaths.IsExistingUnlinkedPath(AppContext.BaseDirectory, exe))
            throw new InvalidDataException("Installed Opsis screensaver is unavailable.");
        using var a = File.OpenRead(exe);
        using var b = File.OpenRead(SaverPath);
        if (a.Length != b.Length || a.Length is <= 0 or > 150L * 1024 * 1024 ||
            !SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))
            throw new InvalidDataException("Installed screensaver does not match the Opsis apphost.");
    }

    // Only the current-user saver path. Timeout, activation and secure resume
    // are intentionally managed by the official Windows settings UI.
    internal static void SelectInstalled()
    {
        CheckWritable();
        VerifyInstalledCopy();
        using var desktop = Registry.CurrentUser.CreateSubKey(DesktopKey);
        object? previous = desktop.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (Same(previous as string, SaverPath)) return;
        if (previous != null && (previous is not string || desktop.GetValueKind(ValueName) != RegistryValueKind.String))
            throw new InvalidDataException("Unsupported existing screensaver registry value; use Windows settings.");
        using var backup = Registry.CurrentUser.CreateSubKey(BackupKey);
        // Preserve the first original until the selection is explicitly restored.
        var marker = backup.GetValue("SelectedPath") as string;
        if (marker != null)
        {
            if (!Same(marker, SaverPath) ||
                backup.GetValue("OriginalPresent") is not int present ||
                present is not (0 or 1) ||
                (present == 1 ? !Equals(backup.GetValue("OriginalValue"), previous) : previous != null))
                throw new InvalidOperationException("A previous selection backup exists and Windows settings have changed.");
        }
        else
        {
            backup.SetValue("OriginalPresent", previous == null ? 0 : 1, RegistryValueKind.DWord);
            backup.SetValue("OriginalValue", previous as string ?? "", RegistryValueKind.String);
            backup.SetValue("SelectedPath", SaverPath, RegistryValueKind.String);
            backup.Flush();
        }
        desktop.SetValue(ValueName, SaverPath, RegistryValueKind.String);
        desktop.Flush();
        if (!Same(desktop.GetValue(ValueName) as string, SaverPath)) throw new IOException("Screensaver selection could not be verified.");
    }
    internal static void RestoreSelection()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(DesktopKey, true)
            ?? throw new InvalidOperationException("Windows desktop settings unavailable.");
        using var backup = Registry.CurrentUser.OpenSubKey(BackupKey, true)
            ?? throw new InvalidOperationException("No original screensaver selection backup.");
        if (!Same(desktop.GetValue(ValueName) as string, SaverPath) || !Same(backup.GetValue("SelectedPath") as string, SaverPath))
            throw new InvalidOperationException("Windows screensaver selection has changed; no settings overwritten.");
        if (backup.GetValue("OriginalPresent") is not int present || present is not (0 or 1) ||
            backup.GetValue("OriginalValue") is not string prior)
            throw new InvalidDataException("Invalid screensaver backup.");
        if (present == 1) desktop.SetValue(ValueName, prior, RegistryValueKind.String);
        else desktop.DeleteValue(ValueName, false);
        desktop.Flush();
        backup.DeleteValue("SelectedPath", false);
        backup.DeleteValue("OriginalValue", false);
        backup.DeleteValue("OriginalPresent", false);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>Explicit capture of local folder locations exposed by the Windows Shell
/// automation interface. No global input hook, COM injection or background polling.</summary>
public static class ExplorerSessionStore
{
    public const int MaximumFolders = 64;
    private static string FilePath => Path.Combine(AppPaths.AppDataFolder, "explorer-session.json");

    public static bool IsLocalFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(char.IsControl) ||
            path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;
        if (path[3..].Contains(':') || path.Contains('"') || path.Split('\\').Any(x => x is "." or ".."))
            return false;
        try { return Path.IsPathFullyQualified(path) && new DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Fixed or DriveType.Removable; }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return false; }
    }

    public static string[] Normalize(IEnumerable<string> paths) =>
        paths.Where(IsLocalFolderPath).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumFolders).ToArray();

    public static string[] Capture()
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
            throw new InvalidOperationException("Compatibility review required.");
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new PlatformNotSupportedException("Shell automation unavailable.");
        object? shell = null, windows = null;
        var locations = new List<string>();
        try
        {
            shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Cannot open Shell automation.");
            windows = ((dynamic)shell).Windows();
            int count = Math.Min(MaximumFolders * 4, (int)((dynamic)windows!).Count);
            for (int i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = ((dynamic)windows!).Item(i);
                    if (window == null) continue;
                    string executable = (string)((dynamic)window).FullName;
                    if (!string.Equals(Path.GetFileName(executable), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string address = (string)((dynamic)window).LocationURL;
                    if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsFile && !uri.IsUnc)
                        locations.Add(uri.LocalPath);
                }
                catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Release(window); }
            }
        }
        finally { Release(windows); Release(shell); }
        return Normalize(locations);
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    public static string[] Read()
    {
        if (!File.Exists(FilePath)) return Array.Empty<string>();
        if (new FileInfo(FilePath).Length > 256 * 1024) throw new InvalidDataException("Explorer session oversized.");
        var paths = JsonSerializer.Deserialize<string[]>(AtomicFile.ReadAllText(FilePath)) ?? throw new InvalidDataException("Invalid Explorer session.");
        if (paths.Length > MaximumFolders || paths.Any(x => !IsLocalFolderPath(x))) throw new InvalidDataException("Invalid saved folder.");
        return Normalize(paths);
    }

    public static void Save(IEnumerable<string> paths)
    {
        var selected = Normalize(paths);
        if (selected.Length == 0) throw new InvalidOperationException("No local Explorer folders selected.");
        if (Read().SequenceEqual(selected, StringComparer.OrdinalIgnoreCase)) return;
        OpsisRecoveryHub.Snapshot("Before saving Explorer folder session");
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(selected));
    }

    // Native Explorer decides whether to reuse a window or create one.
    // There is no supported public API to guarantee tabs on every Windows build.
    public static int Restore(IEnumerable<string> paths)
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
            throw new InvalidOperationException("Compatibility review required.");
        int opened = 0;
        foreach (var folder in Normalize(paths))
        {
            if (!Directory.Exists(folder)) continue;
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            using var process = Process.Start(start);
            opened++;
        }
        return opened;
    }
}

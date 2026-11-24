using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace MotionDesk.Services;

/// <summary>
/// Unified recovery for Opsis-owned settings, and read-only Windows shell inventory.
/// Never overwrite Windows Resource Protection files: only CBS/DISM/SFC or System Restore
/// can repair Windows. An inventory is diagnostic, not a substitute for a restore point.
/// </summary>
public static class OpsisRecoveryHub
{
    public sealed record FileEntry(string RelativePath, string Sha256);
    public sealed class SnapshotManifest
    {
        public string Id { get; set; } = "";
        public string Reason { get; set; } = "";
        public string WindowsBuild { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public List<FileEntry> Files { get; set; } = new();
    }
    public sealed record SystemFile(string Name, string Version, string Sha256, bool Available);
    public sealed class SystemInventory
    {
        public DateTime RecordedUtc { get; set; }
        public string Fingerprint { get; set; } = "";
        public List<SystemFile> Files { get; set; } = new();
    }
    private static readonly object Sync = new();
    public static string Root => Path.Combine(AppPaths.AppDataFolder, "RecoveryHub");
    private static string SnapshotsRoot => Path.Combine(Root, "snapshots");
    private static string InventoryFile => Path.Combine(Root, "system-inventory.json");
    private static string ImpactReportFile => Path.Combine(Root, "windows-update-impact.txt");

    public static string WindowsFingerprint()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string V(string name) => Convert.ToString(key?.GetValue(name)) ?? "?";
        return "Build=" + V("CurrentBuildNumber") + ";UBR=" + V("UBR") +
               ";Display=" + V("DisplayVersion") + ";Lab=" + V("BuildLabEx");
    }
    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsAllowed(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Contains("..", StringComparison.Ordinal) || p.StartsWith('/') || p.Contains(':')) return false;
        if (p is "settings.json" or "wallpaper.json" or "module-core.json" or "automations.json" or "window-rules.json" or "start-studio.json" or "taskbar-designer.json" or "taskbar-monitor-profiles.json" or "native-start-original.json" or "native-shell-theme-backups.json" or "screensaver-studio.json") return true;
        if (p is "native-profiles/explorer-view-before-opsis.json" or
            "native-profiles/taskbar-before-opsis.json") return true;
        if (p.StartsWith("widget-designs/", StringComparison.OrdinalIgnoreCase) &&
            p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.Count(ch => ch == '/') == 1)
            return true;
        if (p.StartsWith("weather-scenes/", StringComparison.OrdinalIgnoreCase) &&
            p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.Count(ch => ch == '/') == 1)
            return true;
        return p.StartsWith("appearance-presets/", StringComparison.OrdinalIgnoreCase) &&
               p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.Count(ch => ch == '/') == 1;
    }

    private static string SourceFor(string relative) => relative switch
    {
        "native-profiles/explorer-view-before-opsis.json" => ExplorerViewProfiles.BackupPath,
        "native-profiles/taskbar-before-opsis.json" => TaskbarViewProfiles.BackupPath,
        _ => Path.Combine(AppPaths.AppDataFolder, relative.Replace('/', Path.DirectorySeparatorChar))
    };

    private static IEnumerable<string> OwnedFiles()
    {
        if (File.Exists(ExplorerViewProfiles.BackupPath))
            yield return "native-profiles/explorer-view-before-opsis.json";
        if (File.Exists(TaskbarViewProfiles.BackupPath))
            yield return "native-profiles/taskbar-before-opsis.json";
        yield return "settings.json";
        yield return "module-core.json";
        yield return "wallpaper.json";
        yield return "automations.json";
        yield return "window-rules.json";
        yield return "start-studio.json";
        yield return "taskbar-designer.json";
        yield return "taskbar-monitor-profiles.json";
        yield return "native-start-original.json";
        yield return "native-shell-theme-backups.json";
        yield return "screensaver-studio.json";
        var widgetFolder = Path.Combine(AppPaths.AppDataFolder, "widget-designs");
        if (Directory.Exists(widgetFolder) && (File.GetAttributes(widgetFolder) & FileAttributes.ReparsePoint) == 0)
            foreach (var path in Directory.EnumerateFiles(widgetFolder, "*.json", SearchOption.TopDirectoryOnly).Take(250))
                yield return "widget-designs/" + Path.GetFileName(path);

        var sceneFolder = Path.Combine(AppPaths.AppDataFolder, "weather-scenes");
        if (Directory.Exists(sceneFolder) && (File.GetAttributes(sceneFolder) & FileAttributes.ReparsePoint) == 0)
            foreach (var path in Directory.EnumerateFiles(sceneFolder, "*.json", SearchOption.TopDirectoryOnly).Take(250))
                yield return "weather-scenes/" + Path.GetFileName(path);
        var folder = Path.Combine(AppPaths.AppDataFolder, "appearance-presets");
        if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) yield break;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Take(250))
            yield return "appearance-presets/" + Path.GetFileName(path);
    }

    public static SnapshotManifest Snapshot(string reason)
    {
        lock (Sync)
        {
            if (reason.Length is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(reason));
            var id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..10];
            var directory = Path.Combine(SnapshotsRoot, id);
            Directory.CreateDirectory(directory);
            var manifest = new SnapshotManifest
            {
                Id = id, Reason = reason, WindowsBuild = WindowsFingerprint(), CreatedUtc = DateTime.UtcNow
            };
            long total = 0;
            foreach (var relative in OwnedFiles().Where(IsAllowed).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = SourceFor(relative);
                if (!File.Exists(source)) continue;
                var info = new FileInfo(source);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (info.Length > 4 * 1024 * 1024 || (total += info.Length) > 24 * 1024 * 1024)
                    throw new InvalidDataException("Opsis backup storage quota exceeded.");
                var destination = Path.Combine(directory, "files", relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, false);
                manifest.Files.Add(new FileEntry(relative, Sha(destination)));
            }
            AtomicFile.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
            return manifest;
        }
    }

    public static IReadOnlyList<SnapshotManifest> List()
    {
        if (!Directory.Exists(SnapshotsRoot)) return Array.Empty<SnapshotManifest>();
        var list = new List<SnapshotManifest>();
        foreach (var folder in Directory.EnumerateDirectories(SnapshotsRoot).Take(500))
        {
            try
            {
                var path = Path.Combine(folder, "manifest.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 128 * 1024) continue;
                var value = JsonSerializer.Deserialize<SnapshotManifest>(File.ReadAllText(path));
                if (value != null && value.Id == Path.GetFileName(folder)) list.Add(value);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    public static void RestoreOwnedSettings(string id)
    {
        lock (Sync)
        {
            if (id.Length is < 1 or > 60 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
                throw new ArgumentException("Invalid snapshot identifier.", nameof(id));
            var folder = Path.Combine(SnapshotsRoot, id);
            var path = Path.Combine(folder, "manifest.json");
            if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Invalid manifest size.");
            var manifest = JsonSerializer.Deserialize<SnapshotManifest>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Missing manifest.");
            if (manifest.Id != id || manifest.Files.Count > 600) throw new InvalidDataException("Malformed manifest.");
            var validated = new List<(string src, string dest)>();
            foreach (var entry in manifest.Files)
            {
                if (!IsAllowed(entry.RelativePath)) throw new InvalidDataException("Unsupported restore path.");
                var rel = entry.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                var source = Path.Combine(folder, "files", rel);
                var destination = SourceFor(entry.RelativePath);
                if (!File.Exists(source) ||
                    (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
                    !Sha(source).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Backup integrity verification failed.");
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Cannot restore over file links.");
                validated.Add((source, destination));
            }
            Snapshot("Automatic backup before settings restore");
            foreach (var (source, dest) in validated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                AtomicFile.WriteAllText(dest, File.ReadAllText(source));
            }
        }
    }

    private static readonly string[] MonitoredNames =
    {
        "explorer.exe", "shell32.dll", "twinui.pcshell.dll",
        "windows.storage.dll", "user32.dll", "uxtheme.dll"
    };

    /// <summary>
    /// Archive a bounded selection of original Windows shell binaries for forensics.
    /// Do NOT restore these copies over protected system files: use DISM/SFC/System
    /// Restore to repair Windows. The archive is not a full OS/system image backup.
    /// </summary>
    public static SystemInventory BackupMonitoredWindowsOriginals()
    {
        lock (Sync)
        {
            var fingerprint = WindowsFingerprint();
            var folderId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)))[..20];
            var target = Path.Combine(Root, "WindowsOriginals", folderId);
            var manifestPath = Path.Combine(target, "manifest.json");
            if (File.Exists(manifestPath))
            {
                if (new FileInfo(manifestPath).Length > 64 * 1024)
                    throw new InvalidDataException("Windows originals manifest exceeds limit.");
                var old = JsonSerializer.Deserialize<SystemInventory>(File.ReadAllText(manifestPath));
                if (old != null && old.Fingerprint == fingerprint) return old;
            }
            var result = new SystemInventory { RecordedUtc = DateTime.UtcNow, Fingerprint = fingerprint };
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var bytes = 0L;
            foreach (var name in MonitoredNames)
            {
                var source = name == "explorer.exe" ? Path.Combine(windows, name) : Path.Combine(system, name);
                if (!File.Exists(source)) { result.Files.Add(new SystemFile(name, "", "", false)); continue; }
                var info = new FileInfo(source);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.Length > 96 * 1024 * 1024 || (bytes += info.Length) > 320 * 1024 * 1024)
                    throw new InvalidDataException("Windows original file archive exceeded safety limit.");
                var sha = Sha(source);
                var destination = Path.Combine(target, "files", name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Copy(source, destination, false);
                if (!Sha(destination).Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Windows archive checksum mismatch: " + name);
                result.Files.Add(new SystemFile(name,
                    FileVersionInfo.GetVersionInfo(source).FileVersion ?? "", sha, true));
            }
            Directory.CreateDirectory(target);
            AtomicFile.WriteAllText(manifestPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
    }

    public static SystemInventory InspectWindowsShell()
    {
        var inventory = new SystemInventory { RecordedUtc = DateTime.UtcNow, Fingerprint = WindowsFingerprint() };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var name in MonitoredNames)
        {
            var path = name == "explorer.exe" ? Path.Combine(windows, name)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
            try
            {
                var file = new FileInfo(path);
                inventory.Files.Add(file.Exists
                    ? new SystemFile(name, FileVersionInfo.GetVersionInfo(path).FileVersion ?? "", Sha(path), true)
                    : new SystemFile(name, "", "", false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { inventory.Files.Add(new SystemFile(name, "", "", false)); }
        }
        Directory.CreateDirectory(Root);
        AtomicFile.WriteAllText(InventoryFile, JsonSerializer.Serialize(inventory));
        return inventory;
    }

    public static string ReadLastUpdateImpact()
    {
        if (!File.Exists(ImpactReportFile)) return "No prior Windows shell change report recorded.";
        if (new FileInfo(ImpactReportFile).Length > 64 * 1024)
            throw new InvalidDataException("Update impact report too large.");
        return File.ReadAllText(ImpactReportFile);
    }

    public static string InspectUpdateImpact()
    {
        var previous = File.Exists(InventoryFile)
            ? JsonSerializer.Deserialize<SystemInventory>(File.ReadAllText(InventoryFile)) : null;
        var current = InspectWindowsShell();
        if (previous == null) return "Baseline Windows inventory recorded. No previous snapshot to compare.";
        var messages = new List<string>();
        if (previous.Fingerprint != current.Fingerprint)
            messages.Add("Windows build/update fingerprint changed: " + previous.Fingerprint + " -> " + current.Fingerprint);
        foreach (var item in current.Files)
        {
            var before = previous.Files.FirstOrDefault(x => x.Name == item.Name);
            if (before == null || before.Sha256 != item.Sha256 || before.Version != item.Version)
                messages.Add("Changed: " + item.Name + " (" + (before?.Version ?? "unknown") + " -> " + item.Version + ")");
        }
        var report = messages.Count == 0 ? "No monitored Windows shell changes detected."
            : "Recorded UTC " + DateTime.UtcNow.ToString("O") + Environment.NewLine +
              string.Join(Environment.NewLine, messages);
        // Keep the last meaningful update report; repeated checks against an
        // already-updated baseline must not erase evidence of the prior change.
        if (messages.Count > 0)
            AtomicFile.WriteAllText(ImpactReportFile, report);
        return report;
    }
}

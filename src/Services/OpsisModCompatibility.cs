using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace MotionDesk.Services;

/// <summary>
/// Advisory, read-only native mod coexistence diagnostics. Identifies a handful
/// of known shell tools; does not inspect their private data, inject into their
/// processes or falsely assume all Windhawk mods conflict with Opsis.
/// </summary>
public static class OpsisModCompatibility
{
    public sealed record Finding(string Product, string Risk, string Area, string Advice);
    private static readonly (string Name, string Risk, string Area, string Advice)[] Known =
    {
        ("StartAllBack", "High", "Start / Taskbar / Explorer",
            "Avoid running another shell/taskbar replacement simultaneously; keep native Windows taskbar fallback."),
        ("ExplorerPatcher", "High", "Start / Taskbar / Explorer",
            "Disable competing shell replacement before activating Opsis taskbar overlays."),
        ("Windhawk", "Review", "Per-mod shell changes",
            "Review which individual Windhawk mods are enabled; non-shell mods may safely coexist."),
        ("Rainmeter", "Low", "Desktop widgets",
            "Rainmeter widgets can coexist; avoid duplicating high-frequency monitors or overlapping desktop regions."),
        ("TranslucentTB", "Review", "Taskbar material",
            "Choose one owner for taskbar transparency; overlapping materials can flicker.")
    };

    public static IReadOnlyList<Finding> Evaluate(IEnumerable<string> names)
    {
        var found = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            foreach (var product in Known)
                if (name.Contains(product.Name, StringComparison.OrdinalIgnoreCase))
                    found.TryAdd(product.Name, new Finding(product.Name, product.Risk, product.Area, product.Advice));
        }
        return found.Values.OrderBy(x => x.Risk == "High" ? 0 : x.Risk == "Review" ? 1 : 2)
            .ThenBy(x => x.Product).ToArray();
    }

    // Explicit catalog of known overlap candidates. Do not infer conflicts from
    // substring matches or claim that a configured mod is currently injected.
    private static readonly Dictionary<string, string> WindhawkAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows-11-start-menu-styler"] = "Start",
        ["windows-11-file-explorer-styler"] = "Explorer",
        ["windows-11-taskbar-styler"] = "Taskbar",
        ["taskbar-icon-size"] = "Taskbar",
        ["taskbar-clock-customization"] = "Taskbar",
        ["taskbar-labels"] = "Taskbar",
        ["taskbar-tray-system-icon-tweaks"] = "System tray",
        ["windows-11-notification-center-styler"] = "Notification center",
        ["taskbar-volume-control"] = "Taskbar",
        ["disable-taskbar-thumbnails"] = "Taskbar",
        ["taskbar-margin"] = "Taskbar",
        ["taskbar-drag-lock"] = "Taskbar",
        ["taskbar-notification-icon-spacing"] = "System tray"
    };

    // Only explicit competing visual/geometry providers cause an automatic hold.
    // Other known mods remain advisory; a configured mod is not proof of injection.
    private static readonly HashSet<string> ExclusiveTaskbarMods = new(StringComparer.OrdinalIgnoreCase)
    { "windows-11-taskbar-styler", "taskbar-margin", "taskbar-drag-lock" };

    public static bool BlocksTaskbarOverlay(Finding finding) =>
        finding.Risk == "High" || (finding.Risk == "Hold" && finding.Area == "Taskbar");

    public static IReadOnlyList<Finding> EvaluateWindhawkMods(IEnumerable<(string Id, bool? Disabled)> mods)
    {
        return mods.Where(m => !string.IsNullOrWhiteSpace(m.Id) && m.Id.Length <= 128)
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(m => m.Disabled != true))
            .Select(g =>
            {
                bool configured = g.Any(m => m.Disabled == false);
                var known = WindhawkAreas.TryGetValue(g.Key, out var area);
                return new Finding("Windhawk: " + g.Key, configured && ExclusiveTaskbarMods.Contains(g.Key) ? "Hold" : "Review", area ?? "Unknown mod",
                    (configured ? "Configured enabled; runtime injection not verified. " : "Enabled state unknown. ") +
                    (known ? "Check overlap with Opsis before changing this shell area."
                           : "Review this mod manually; no automatic compatibility assumption."));
            }).OrderBy(f => f.Product, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Upstream storage_manager.cpp / core/services/storage.rs: portable
    // <AppDataPath>/Engine/Mods/<id>.ini [Mod], installed registry Engine/Mods.
    // Current Windhawk user scope is non-portable only and opt-in (UserScoped).
    public static bool? ResolveWindhawkDisabled(int? global, int? userScoped, int? userDisabled, bool nuked)
    {
        if (nuked) return true;
        int? effective = userScoped is not null and not 0 && userDisabled.HasValue ? userDisabled : global;
        return effective.HasValue ? effective.Value != 0 : null;
    }

    private static Dictionary<string, string> ReadIniSection(string path, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        var info = new FileInfo(path);
        if (info.Length > 256 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) return result;
        bool inside = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { inside = line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside || line.StartsWith(";")) continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            if (key is "Disabled" or "Nuked" or "Portable" or "AppDataPath" or "EnginePath" ||
                new[] { "Disabled", "Nuked", "Portable", "AppDataPath", "EnginePath" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                result[key] = line[(equals + 1)..].Trim().Trim('"');
        }
        return result;
    }

    private static int? Number(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var text) && int.TryParse(text, out var number) ? number : null;

    private static bool LocalPath(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) &&
        new DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Fixed or DriveType.Removable;

    // No whole-disk scan. Inspect only known installation paths or the running
    // Windhawk executable's actual directory, with bounded local INI reads.
    private static IEnumerable<(string Id, bool? Disabled)> ScanPortableWindhawk()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(root)) roots.Add(Path.Combine(root, "Windhawk"));
        }
        try
        {
            foreach (var process in Process.GetProcessesByName("windhawk"))
                using (process)
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && Path.GetDirectoryName(path) is string parent) roots.Add(parent);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var candidates = new List<(string Id, bool? Disabled)>();
        foreach (var root in roots.Take(16))
        {
            try
            {
                if (!LocalPath(root)) continue;
                var storage = ReadIniSection(Path.Combine(root, "windhawk.ini"), "Storage");
                if (Number(storage, "Portable") is not int portable || portable == 0 ||
                    !storage.TryGetValue("AppDataPath", out var configured) || string.IsNullOrWhiteSpace(configured)) continue;
                var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured), root);
                if (!LocalPath(path)) continue;
                var mods = Path.Combine(path, "Engine", "Mods");
                if (!Directory.Exists(mods) || (File.GetAttributes(mods) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in Directory.EnumerateFiles(mods, "*.ini", SearchOption.TopDirectoryOnly).Take(1000))
                {
                    var values = ReadIniSection(file, "Mod");
                    if (values.Count == 0) continue;
                    var id = Path.GetFileNameWithoutExtension(file);
                    bool nuked = values.TryGetValue("Nuked", out var reason) && !string.IsNullOrWhiteSpace(reason);
                    candidates.Add((id, ResolveWindhawkDisabled(Number(values, "Disabled"), null, null, nuked)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { RotatingLogger.Write("Optional portable Windhawk inventory: " + ex.Message); }
        }
        return candidates;
    }

    private static IReadOnlyList<Finding> ScanWindhawkMods()
    {
        var candidates = new List<(string Id, bool? Disabled)>();
        string? sid = null;
        try { using var identity = WindowsIdentity.GetCurrent(); sid = identity.User?.Value; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var root = machine.OpenSubKey(@"SOFTWARE\Windhawk\Engine\Mods", false);
                if (root == null) continue;
                foreach (var id in root.GetSubKeyNames().Take(1000))
                {
                    using var mod = root.OpenSubKey(id, false);
                    if (mod == null) continue;
                    int? global = mod.GetValue("Disabled") is int flag ? flag : null;
                    bool nuked = mod.GetValue("Nuked") is string reason && !string.IsNullOrWhiteSpace(reason);
                    using var user = sid == null ? null : mod.OpenSubKey(@"Users\" + sid, false);
                    int? scoped = user?.GetValue("UserScoped") is int scope ? scope : null;
                    int? userDisabled = user?.GetValue("Disabled") is int disabled ? disabled : null;
                    candidates.Add((id, ResolveWindhawkDisabled(global, scoped, userDisabled, nuked)));
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or PlatformNotSupportedException)
            { RotatingLogger.Write("Optional Windhawk registry inventory inaccessible: " + e.Message); }
        }
        candidates.AddRange(ScanPortableWindhawk());
        return EvaluateWindhawkMods(candidates);
    }

    private static readonly object CacheGate = new();
    private static IReadOnlyList<Finding>? _lastScan;
    private static long _lastScanTicks;

    /// <summary>Cache uninstall/process inventory to avoid synchronous repeat scans
    /// during native taskbar settings refresh. Explicit diagnostics call Scan()
    /// for fresh results instead.</summary>
    public static IReadOnlyList<Finding> ScanCached(int maxAgeSeconds = 45)
    {
        if (maxAgeSeconds is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(maxAgeSeconds));
        lock (CacheGate)
        {
            if (_lastScan != null &&
                Environment.TickCount64 - _lastScanTicks < maxAgeSeconds * 1000L)
                return _lastScan;
        }
        var result = Scan();
        lock (CacheGate)
        {
            _lastScan = result;
            _lastScanTicks = Environment.TickCount64;
        }
        return result;
    }

    public static IReadOnlyList<Finding> Scan()
    {
        var names = new List<string>();
        foreach (var path in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        })
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var root = hive.OpenSubKey(path);
                    if (root == null) continue;
                    foreach (var id in root.GetSubKeyNames().Take(3500))
                    {
                        using var entry = root.OpenSubKey(id);
                        if (entry?.GetValue("DisplayName") is string name && name.Length <= 250)
                            names.Add(name);
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
                { RotatingLogger.Write("Optional shell mod inventory inaccessible: " + e.Message); }
            }
        }
        foreach (var name in new[] { "StartAllBackCfg", "ExplorerPatcher", "windhawk", "Rainmeter", "TranslucentTB" })
        {
            try
            {
                var running = Process.GetProcessesByName(name);
                if (running.Length > 0) names.Add(name);
                foreach (var process in running) process.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            { RotatingLogger.Write("Optional shell process scan: " + e.Message); }
        }
        return Evaluate(names).Concat(ScanWindhawkMods()).ToArray();
    }
}

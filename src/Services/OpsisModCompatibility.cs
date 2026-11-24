using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        ["disable-taskbar-thumbnails"] = "Taskbar"
    };

    public static IReadOnlyList<Finding> EvaluateWindhawkMods(IEnumerable<(string Id, bool? Disabled)> mods)
    {
        return mods.Where(m => !string.IsNullOrWhiteSpace(m.Id) && m.Id.Length <= 128)
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(m => m.Disabled != true))
            .Select(g =>
            {
                bool configured = g.Any(m => m.Disabled == false);
                var known = WindhawkAreas.TryGetValue(g.Key, out var area);
                return new Finding("Windhawk: " + g.Key, "Review", area ?? "Unknown mod",
                    (configured ? "Configured enabled; runtime injection not verified. " : "Enabled state unknown. ") +
                    (known ? "Check overlap with Opsis before changing this shell area."
                           : "Review this mod manually; no automatic compatibility assumption."));
            }).OrderBy(f => f.Product, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<Finding> ScanWindhawkMods()
    {
        var candidates = new List<(string Id, bool? Disabled)>();
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
                    var raw = mod.GetValue("Disabled");
                    candidates.Add((id, raw is int flag && flag is 0 or 1 ? flag == 1 : (bool?)null));
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or PlatformNotSupportedException)
            { RotatingLogger.Write("Optional Windhawk registry inventory inaccessible: " + e.Message); }
        }
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

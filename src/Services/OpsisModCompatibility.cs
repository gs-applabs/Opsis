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
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
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
        return Evaluate(names);
    }
}

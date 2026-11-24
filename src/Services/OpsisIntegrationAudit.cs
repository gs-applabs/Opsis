using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MotionDesk.Services;

/// <summary>
/// Single-source inventory: the 2.8.0.2510 core remains the authority for all
/// pre-existing features. New 3.0 studios are settings/adapters, never second
/// wallpaper engines, taskbar services, widget hosts or plugin loaders.
/// All diagnostics are read-only, with no plugin assembly activation.
/// </summary>
public static class OpsisIntegrationAudit
{
    public sealed record BaselineFeature(string Key, string Domain, string Owner, string Source);
    public sealed record PluginEntry(string Id, string Version, bool Enabled);
    public sealed record Collision(string Severity, string Domain, string Description, string Resolution);
    public sealed record Report(
        string BaselineVersion, string TargetVersion, DateTime InspectedUtc,
        IReadOnlyList<BaselineFeature> Baseline, int RegisteredModules,
        IReadOnlyList<PluginEntry> LoadedPlugins, int GalleryContributions,
        IReadOnlyList<Collision> Collisions);

    // Verified from the release/2.8.0.2510 source files, not guessed from the
    // 3.0 proposal list. Multiple features may deliberately share one owner.
    public static IReadOnlyList<BaselineFeature> Baseline28 { get; } = new[]
    {
        new BaselineFeature("wallpaper.host", "wallpaper.renderer", "WallpaperHostEngine", "src/Widgets/WallpaperWindow.cs"),
        new BaselineFeature("wallpaper.weather", "wallpaper.renderer", "WallpaperHostEngine", "src/Widgets/WallpaperWindow.cs"),
        new BaselineFeature("wallpaper.scheduling", "wallpaper.renderer", "WallpaperHostEngine", "src/Widgets/WallpaperWindow.cs"),
        new BaselineFeature("wallpaper.multimonitor", "wallpaper.renderer", "WallpaperHostEngine", "src/Widgets/WallpaperWindow.cs"),
        new BaselineFeature("wallpaper.pause-policy", "desktop.activity", "AppActivity", "src/Widgets/TrayApplicationContext.cs"),
        new BaselineFeature("widgets.native", "widgets.host", "Opsis Widget Host", "src/UI/MainWindow.cs"),
        new BaselineFeature("widgets.system-monitor", "system.monitor", "AdvancedSystemMonitorService", "src/Services/AdvancedSystemMonitorService.cs"),
        new BaselineFeature("desktop.deskstrip", "desktop.deskstrip", "DeskStripHostEngine", "src/Widgets/DeskStripWindow.cs"),
        new BaselineFeature("desktop.virtual-workspaces", "desktop.workspaces", "VirtualWorkspaces", "src/Widgets/TrayApplicationContext.cs"),
        new BaselineFeature("desktop.auto-tile", "desktop.layout", "AutoTileService", "src/Widgets/TrayApplicationContext.cs"),
        new BaselineFeature("desktop.toolbar", "desktop.toolbar", "DesktopToolbar", "src/Widgets/TrayApplicationContext.cs"),
        new BaselineFeature("desktop.icons", "desktop.icon-visibility", "DesktopIconVisibilityEngine", "src/Widgets/DesktopIconVisibilityEngine.cs"),
        new BaselineFeature("desktop.containers", "desktop.containers", "Opsis Containers", "src/UI/MainWindow.cs"),
        new BaselineFeature("shell.opsis-taskbar", "shell.taskbar", "ReplacementTaskbar", "src/Services/AppSettings.cs"),
        new BaselineFeature("shell.taskbar-pins", "shell.taskbar", "TaskbarPinsReader", "src/Services/TaskbarPinsReader.cs"),
        new BaselineFeature("theme.shared", "theme.palette", "ThemeManager", "src/UI/ThemeManager.cs"),
        new BaselineFeature("theme.wallpaper-lockscreen", "windows.lockscreen", "WallpaperHostEngine", "src/Services/AppSettings.cs"),
        new BaselineFeature("music.overlay", "media.overlay", "Opsis Music Overlay", "src/Services/AppSettings.cs"),
        new BaselineFeature("plugins.host", "plugins.loader", "PluginHost", "src/Services/PluginHost.cs"),
        new BaselineFeature("plugins.widget-replacement", "widgets.host", "PluginHost", "src/Services/PluginHost.cs"),
        new BaselineFeature("plugins.approval", "plugins.trust", "PluginTrust", "src/Services/PluginHost.cs"),
        new BaselineFeature("ui.command-palette", "ui.command", "MainWindow", "src/UI/MainWindow.cs"),
        new BaselineFeature("ui.settings", "ui.settings", "MainWindow", "src/UI/MainWindow.cs"),
        new BaselineFeature("system.diagnostics", "system.diagnostics", "DesktopDiagnostics", "src/Services/DesktopDiagnostics.cs"),
        new BaselineFeature("system.preferences", "system.settings", "AppSettings", "src/Services/AppSettings.cs"),
        new BaselineFeature("system.tray", "desktop.tray", "TrayApplicationContext", "src/Widgets/TrayApplicationContext.cs")
    };

    public static IReadOnlyList<Collision> Evaluate(
        IEnumerable<string> moduleIds, IEnumerable<PluginEntry> pluginEntries,
        IEnumerable<(string PluginId, string ItemId, string Origin)> contributions,
        bool replaceWindowsTaskbar, IEnumerable<OpsisModCompatibility.Finding> external,
        bool guardRequiresReview)
    {
        var problems = new List<Collision>();
        var modules = moduleIds.ToArray();
        foreach (var group in modules.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            problems.Add(new Collision("Block", "core.catalog",
                "Duplicate module registration: " + group.Key, "Retain one module ID and one service owner."));
        // Existing IDs intentionally represent the same 2.8 owner: reusing an
        // ID does not by itself create a second engine. Only duplicates in the
        // NEW registry or competing runtime providers are collisions.
        foreach (var group in pluginEntries.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
            problems.Add(new Collision("Block", "plugins.loader",
                "Multiple loaded plugin identities: " + group.Key,
                "Keep only the newest compatible approved plugin version; preserve plugin settings."));
        foreach (var group in contributions.GroupBy(
                     x => x.PluginId + ":" + x.ItemId, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
            problems.Add(new Collision("Review", "plugins.gallery",
                "Duplicate contributed item: " + group.Key,
                "Resolve source precedence/version; display just one Gallery item."));
        if (replaceWindowsTaskbar)
        {
            foreach (var externalShell in external.Where(x => x.Risk == "High"))
                problems.Add(new Collision("Block", "shell.taskbar",
                    "Opsis replacement taskbar overlaps with " + externalShell.Product,
                    "Disable the competing shell replacement or disable Opsis taskbar overlay."));
        }
        if (guardRequiresReview)
            problems.Add(new Collision("Block", "windows.update",
                "Windows build has not passed the Opsis compatibility review.",
                "Keep shell replacements/optional plugins held until the user completes review."));
        return problems
            .OrderBy(x => x.Severity == "Block" ? 0 : 1)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static Report Inspect()
    {
        var s = AppSettings.Peek();
        var plugins = new List<PluginEntry>();
        try
        {
            foreach (var p in PluginHost.Instance.Plugins)
                plugins.Add(new PluginEntry(p.Id, p.Version,
                    s.PluginsEnabled && !s.DisabledPluginIds.Contains(p.Id)));
        }
        catch (Exception ex) { RotatingLogger.Write("Integration audit plugin inventory: " + ex.Message); }
        IReadOnlyList<OpsisEcosystemGallery.Item> assets;
        try { assets = OpsisEcosystemGallery.Discover(); }
        catch (Exception ex)
        {
            RotatingLogger.Write("Integration audit Gallery: " + ex.Message);
            assets = Array.Empty<OpsisEcosystemGallery.Item>();
        }
        IReadOnlyList<OpsisModCompatibility.Finding> external;
        try { external = OpsisModCompatibility.Scan(); }
        catch (Exception ex)
        {
            RotatingLogger.Write("Integration audit shell scan: " + ex.Message);
            external = Array.Empty<OpsisModCompatibility.Finding>();
        }
        return new Report("2.8.0.2510", "3.0.0.3200", DateTime.UtcNow,
            Baseline28, OpsisModCore.Catalog.Count, plugins, assets.Count,
            Evaluate(OpsisModCore.Catalog.Select(x => x.Id), plugins,
                OpsisEcosystemGallery.ContributionOrigins(),
                s.ReplaceWindowsTaskbar, external, WindowsUpdateGuard.RequiresReview));
    }

    public static bool TaskbarOverlayAllowed(
        bool optedIn, IEnumerable<OpsisModCompatibility.Finding> external,
        bool windowsReviewPending) =>
        optedIn && !windowsReviewPending && !SafeModeService.IsSafeMode &&
        !external.Any(x => x.Risk == "High");

    /// <summary>Nonexecuting inventory of candidate packages on disk, including approval-pending packages.</summary>
    public static IReadOnlyList<string> CandidatePluginFolders()
    {
        try
        {
            return PluginHost.EnumerateCandidateFolders()
                .Select(x => x.Folder).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(250).ToArray();
        }
        catch (Exception ex)
        {
            RotatingLogger.Write("Integration audit candidate packages: " + ex.Message);
            return Array.Empty<string>();
        }
    }
}

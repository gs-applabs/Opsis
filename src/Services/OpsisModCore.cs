using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Data-driven registry of independently usable Opsis desktop capabilities.
/// Windhawk-style per-mod controls with Rainmeter-style shared services/skins:
/// this catalog does not load, copy, or execute either project's third-party mods.
/// </summary>
public static class OpsisModCore
{
    public sealed record ModuleDefinition(
        string Id, string Group, string Label, string[] Requires, string[] Conflicts,
        int MinimumBuild = 19041, bool HasNativeImplementation = false);

    public sealed class ModuleConfiguration
    {
        public int Schema { get; set; } = 1;
        public Dictionary<string, bool> Enabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Explicitly state what's implemented versus a future feature. Never list third-party
    // binary injection as compatible with Opsis when no native implementation exists.
    // Feature registry is the single inventory for core, optional mods and plugin
    // contributions. HasNativeImplementation means an Opsis-owned implementation
    // exists; it does not imply a Windows shell replacement is production-certified.
    public static IReadOnlyList<ModuleDefinition> Catalog { get; } = new[]
    {
        new ModuleDefinition("safety.compat-guard", "Safety", "Windows Compatibility Guard", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("safety.recovery-hub", "Safety", "Opsis Recovery Hub", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("system.diagnostics", "Safety", "Desktop Diagnostics", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("start.launcher", "Start", "Start Studio with tiles/folders/pins", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("start.shell-replacement", "Start", "Custom Windows Start replacement (planned)", Array.Empty<string>(), new[] { "start.external-replacement" }, 22000),
        new ModuleDefinition("taskbar.native", "Taskbar", "Windows Taskbar options", Array.Empty<string>(), Array.Empty<string>(), 22000, true),
        new ModuleDefinition("taskbar.opsis-replacement", "Taskbar", "Opsis Replacement Taskbar", Array.Empty<string>(), new[] { "taskbar.external-replacement" }, 19041, true),
        new ModuleDefinition("taskbar.designer", "Taskbar", "Opsis Taskbar Visual Designer", new[] { "taskbar.opsis-replacement" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("taskbar.animation", "Taskbar", "Low-frequency active app pulse", new[] { "taskbar.opsis-replacement" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("explorer.native", "Explorer", "Explorer view preferences", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("explorer.advanced", "Explorer", "Explorer Studio 2.0 extensions (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("explorer.context-menu", "Explorer", "HKCU-owned Opsis Context Menu Studio", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("windows.hot-corners", "Desktop", "Hot Corners", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("windows.window-rules", "Desktop", "Window Rules", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.snap-layouts", "Desktop", "Desktop Snap Layouts and AutoTile", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.window-switcher", "Desktop", "DeskFlip window switcher and visual layouts", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.spaces", "Desktop", "Virtual Workspaces", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.multi-monitor", "Desktop", "Per-monitor profiles and wallpaper", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.command-palette", "Desktop", "Opsis Command Palette", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("desktop.lock-screen", "Desktop", "Lock Screen settings", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("cursor.trails", "Mouse", "Native pointer trails", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("cursor.effects", "Mouse", "Cursor Halo/Ripple FX overlay", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("screensaver.native", "Desktop", "Windows Screen Saver controls", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("screensaver.creator", "Desktop", "Complete Windows .scr installation (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("screensaver.preview", "Desktop", "Native clock/image screensaver scene preview", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("theme.appearance-presets", "Themes", "Appearance Presets", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("theme.icon-atlas", "Themes", "IconAtlas pack manager", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("theme.dynamic-accent", "Themes", "Opt-in wallpaper-derived Opsis accents", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("theme.gallery-marketplace", "Themes", "Theme Marketplace (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("theme.pack-export", "Themes", "Safe .opsis-theme pack import/export", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.weather", "Wallpaper", "Live Weather Wallpaper", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.scene-composer", "Wallpaper", "Weather Scene Composer", new[] { "wallpaper.weather" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.scene-rules", "Wallpaper", "Weather Scene scheduled rules", new[] { "wallpaper.scene-composer" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.scene-3d", "Wallpaper", "Spatial 2.5D/WebGL2 weather overlays with 2D fallback", new[] { "wallpaper.weather" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.performance", "Wallpaper", "Adaptive wallpaper performance profiles", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.gpu-manager", "Wallpaper", "GPU rendering controls", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.opsis", "Widgets", "Opsis native widget engine", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.rainmeter-bridge", "Widgets", "Full Rainmeter skin runtime (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("widgets.rainmeter-ini-import", "Widgets", "Safe Rainmeter String/Time INI importer", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.designer", "Widgets", "Native drag-and-drop Widget Visual Designer", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.layout-studio", "Widgets", "Native Clock/Date/Text Widget Layout Studio", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.data-sources", "Widgets", "Widget Data Sources and Plugin feeds", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("automation.studio", "Automation", "Opsis Automation Studio", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("automation.profile-scheduler", "Automation", "Scheduled appearance and scene profiles", new[] { "automation.studio" }, Array.Empty<string>(), 19041, true),
        new ModuleDefinition("plugin.sdk", "Plugins", "Opsis Plugin SDK and ABI", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("plugin.gallery", "Plugins", "Unified contribution Gallery", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("control.opsis-center", "Control Center", "Unified Opsis Control Center", Array.Empty<string>(), Array.Empty<string>(), 19041, true)
    };

    private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "module-core.json");
    private static readonly object Sync = new();

    public static ModuleConfiguration Read()
    {
        lock (Sync)
        {
            if (!File.Exists(ConfigPath)) return new ModuleConfiguration();
            var info = new FileInfo(ConfigPath);
            if (info.Length > 256 * 1024) throw new InvalidDataException("Module configuration too large.");
            var state = JsonSerializer.Deserialize<ModuleConfiguration>(File.ReadAllText(ConfigPath))
                ?? throw new InvalidDataException("Invalid Opsis module configuration.");
            if (state.Schema != 1) throw new InvalidDataException("Unsupported module schema.");
            // System.Text.Json may replace the initialized dictionaries with ordinal ones.
            // Reject ambiguous aliases rather than choosing an arbitrary enabled state.
            state.Enabled = Normalize(state.Enabled);
            state.Options = Normalize(state.Options);
            foreach (var key in state.Options.Keys.ToArray())
                state.Options[key] = Normalize(state.Options[key]);
            return state;
        }
    }

    private static Dictionary<string, T> Normalize<T>(Dictionary<string, T>? source)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (source == null) return result;
        foreach (var pair in source)
            if (!result.TryAdd(pair.Key, pair.Value))
                throw new InvalidDataException("Duplicate case-insensitive module preference: " + pair.Key);
        return result;
    }

    /// <summary>Plan effective modules without modifying Windows or running plugins.</summary>
    public static IReadOnlyList<string> Validate(ModuleConfiguration state, int windowsBuild, bool safeMode)
    {
        var errors = new List<string>();
        var byId = Catalog.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Enabled.Where(p => p.Value))
        {
            if (!byId.TryGetValue(pair.Key, out var mod))
            {
                errors.Add("Unknown module: " + pair.Key); continue;
            }
            if (!mod.HasNativeImplementation) errors.Add("Module is not yet implemented: " + mod.Id);
            if (windowsBuild < mod.MinimumBuild) errors.Add("Unsupported Windows build for " + mod.Id);
            if (safeMode) errors.Add("Safe Mode prohibits activation of " + mod.Id);
            foreach (var requirement in mod.Requires)
                if (!state.Enabled.TryGetValue(requirement, out var enabled) || !enabled)
                    errors.Add(mod.Id + " requires " + requirement);
            foreach (var conflict in mod.Conflicts)
                if (state.Enabled.TryGetValue(conflict, out var enabled) && enabled)
                    errors.Add(mod.Id + " conflicts with " + conflict);
        }
        return errors;
    }

    /// <summary>Per-module independent preferences; no automatic OS modification.</summary>
    public static void SetModule(string id, bool enabled, int build, bool safeMode)
    {
        lock (Sync)
        {
            if (!Catalog.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Unregistered module", nameof(id));
            var state = Read();
            bool unchanged = state.Enabled.TryGetValue(id, out var previous) && previous == enabled;
            state.Enabled[id] = enabled;
            var problems = Validate(state, build, safeMode);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
            if (unchanged) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void SetOption(string id, string option, string value)
    {
        if (!Catalog.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unregistered module", nameof(id));
        if (option.Length is < 1 or > 64 || !option.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("Invalid option name.", nameof(option));
        if (value.Length > 1024) throw new ArgumentException("Option value too long.", nameof(value));
        lock (Sync)
        {
            var state = Read();
            if (!state.Options.TryGetValue(id, out var options))
                state.Options[id] = options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.TryGetValue(option, out var previous) && previous == value) return;
            options[option] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

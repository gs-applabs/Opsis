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
    public static IReadOnlyList<ModuleDefinition> Catalog { get; } = new[]
    {
        new ModuleDefinition("wallpaper.weather", "Desktop", "Weather Wallpaper", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.opsis", "Widgets", "Opsis Widgets", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("widgets.rainmeter-bridge", "Widgets", "Rainmeter-compatible skin bridge (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("start.launcher", "Start", "Opsis Start Studio", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("start.shell-replacement", "Start", "Native shell replacement (planned)", Array.Empty<string>(), new[] { "start.external-replacement" }, 22000),
        new ModuleDefinition("taskbar.native", "Taskbar", "Native taskbar preferences", Array.Empty<string>(), Array.Empty<string>(), 22000, true),
        new ModuleDefinition("taskbar.opsis-replacement", "Taskbar", "Opsis Replacement Taskbar", Array.Empty<string>(), new[] { "taskbar.external-replacement" }, 19041, true),
        new ModuleDefinition("explorer.native", "Explorer", "Explorer view options", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("windows.hot-corners", "Automation", "Opsis Hot Corners", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("windows.window-rules", "Automation", "Window rules (planned)", Array.Empty<string>(), Array.Empty<string>()),
        new ModuleDefinition("cursor.trails", "Mouse", "Native Windows pointer trails", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("theme.appearance-presets", "Themes", "Appearance Presets", Array.Empty<string>(), Array.Empty<string>(), 19041, true),
        new ModuleDefinition("wallpaper.scene-composer", "Desktop", "Scene Composer (planned)", new[] { "wallpaper.weather" }, Array.Empty<string>())
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
            state.Enabled ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            state.Options ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            return state;
        }
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
            state.Enabled[id] = enabled;
            var problems = Validate(state, build, safeMode);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
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
            options[option] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

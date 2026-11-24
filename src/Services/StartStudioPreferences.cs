using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>Per-user Start Studio pinned shortcuts. Never stores executable blobs.</summary>
public static class StartStudioPreferences
{
    public sealed class Data
    {
        public int Schema { get; set; } = 1;
        public List<string> PinnedShortcuts { get; set; } = new();
        // Native Opsis launcher presentation; does not replace Windows Start.
        public string DisplayStyle { get; set; } = "List"; // List | Tiles | Compact
        public bool GroupByFolder { get; set; } = true;
        public string StartButtonStyle { get; set; } = "Opsis Glass"; // Safe, Opsis-owned taskbar only
    }
    private static readonly object Gate = new();
    private static string Config => Path.Combine(AppPaths.AppDataFolder, "start-studio.json");

    private static bool SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 ||
            !Path.IsPathFullyQualified(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = Path.GetFullPath(path);
        var roots = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        return roots.Any(root => candidate.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static Data Read()
    {
        lock (Gate)
        {
            if (!File.Exists(Config)) return new Data();
            if (new FileInfo(Config).Length > 128 * 1024)
                throw new InvalidDataException("Start Studio settings too large.");
            var data = JsonSerializer.Deserialize<Data>(AtomicFile.ReadAllText(Config))
                ?? throw new InvalidDataException("Invalid Start Studio settings.");
            if (data.Schema != 1 || data.PinnedShortcuts?.Count > 250 ||
                data.PinnedShortcuts == null || data.PinnedShortcuts.Any(p => !SafePath(p)) ||
                data.DisplayStyle is not ("List" or "Tiles" or "Compact") ||
                !MotionDesk.UI.OpsisStartButtonArt.IsValid(data.StartButtonStyle))
                throw new InvalidDataException("Invalid pinned shortcut metadata.");
            return data;
        }
    }

    public static bool IsPinned(string path) => Read().PinnedShortcuts.Contains(path, StringComparer.OrdinalIgnoreCase);

    public static void SetDisplay(string style, bool groupByFolder)
    {
        if (style is not ("List" or "Tiles" or "Compact"))
            throw new ArgumentOutOfRangeException(nameof(style));
        lock (Gate)
        {
            var data = Read();
            data.DisplayStyle = style;
            data.GroupByFolder = groupByFolder;
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void SetStartButton(string style)
    {
        if (!MotionDesk.UI.OpsisStartButtonArt.IsValid(style))
            throw new ArgumentOutOfRangeException(nameof(style));
        lock (Gate)
        {
            var data = Read();
            if (data.StartButtonStyle == style) return; // Avoid needless SSD writes.
            data.StartButtonStyle = style;
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void SetPinned(string path, bool pinned)
    {
        if (!SafePath(path) || !File.Exists(path)) throw new InvalidOperationException("Shortcut is not in the Windows Start Menu.");
        lock (Gate)
        {
            var d = Read();
            d.PinnedShortcuts.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (pinned) d.PinnedShortcuts.Add(path);
            if (d.PinnedShortcuts.Count > 250) throw new InvalidOperationException("Maximum 250 pinned shortcuts.");
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

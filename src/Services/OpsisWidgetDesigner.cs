using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Safe, portable custom widget layouts. Binding keys are a fixed built-in
/// vocabulary. No HTML, scripts, executable or arbitrary data-source paths.
/// </summary>
public static class OpsisWidgetDesigner
{
    public sealed class Layout
    {
        public int Schema { get; set; } = 1;
        public string Name { get; set; } = "";
        public string Binding { get; set; } = "Clock"; // Clock | Date | Text | CPU | RAM | Battery
        public string Text { get; set; } = "";
        public string AccentHex { get; set; } = "#00D2FF";
        public int FontSize { get; set; } = 30;
        public int Width { get; set; } = 330;
        public int Height { get; set; } = 135;
        public int OpacityPercent { get; set; } = 95;
        public bool AlwaysOnTop { get; set; } = false;
        // Position of the primary label inside the widget canvas (0..100 percent).
        public int ContentXPercent { get; set; } = 50;
        public int ContentYPercent { get; set; } = 50;
        public bool ShowWidgetBorder { get; set; } = true;
    }

    private static readonly object Sync = new();
    private static string Folder => Path.Combine(AppPaths.AppDataFolder, "widget-designs");
    private static string FileFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
            name.Any(x => !char.IsLetterOrDigit(x) && x is not (' ' or '_' or '-')))
            throw new ArgumentException("Widget name must be 1–60 letters, digits, spaces, '_' or '-'.");
        return Path.Combine(Folder, name.Trim() + ".json");
    }

    public static void Validate(Layout? layout)
    {
        if (layout == null) throw new InvalidDataException("Null layout.");
        FileFor(layout.Name);
        if (layout.Schema != 1 || layout.Binding is not ("Clock" or "Date" or "Text" or "CPU" or "RAM" or "Battery") ||
            layout.Text == null || layout.Text.Length > 250 || layout.AccentHex == null ||
            layout.AccentHex.Length != 7 || layout.AccentHex[0] != '#' ||
            !int.TryParse(layout.AccentHex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _) ||
            layout.FontSize is < 12 or > 80 || layout.Width is < 190 or > 1200 ||
            layout.Height is < 80 or > 700 || layout.OpacityPercent is < 60 or > 100 ||
            layout.ContentXPercent is < 0 or > 100 || layout.ContentYPercent is < 0 or > 100)
            throw new InvalidDataException("Unsupported widget layout field.");
    }

    public static IReadOnlyList<string> List() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
                .OrderBy(x => x).Take(250).ToArray()
            : Array.Empty<string>();

    public static void Save(Layout layout)
    {
        Validate(layout);
        lock (Sync)
        {
            Directory.CreateDirectory(Folder);
            var file = FileFor(layout.Name);
            if (File.Exists(file)) throw new IOException("Widget layout name already exists.");
            AtomicFile.WriteAllText(file, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static Layout Read(string name)
    {
        var file = FileFor(name);
        if (new FileInfo(file).Length > 32 * 1024) throw new InvalidDataException("Widget layout too large.");
        var layout = JsonSerializer.Deserialize<Layout>(AtomicFile.ReadAllText(file))
            ?? throw new InvalidDataException("Invalid widget layout.");
        Validate(layout);
        if (!string.Equals(layout.Name, name.Trim(), StringComparison.Ordinal))
            throw new InvalidDataException("Widget name mismatch.");
        return layout;
    }

    /// <summary>Save a visual edit only to the selected existing layout, after backup.</summary>
    public static void Update(string existingName, Layout changed)
    {
        Validate(changed);
        if (!string.Equals(existingName, changed.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("Use Save As to rename a widget layout.");
        lock (Sync)
        {
            var file = FileFor(existingName);
            if (!File.Exists(file)) throw new FileNotFoundException("Widget design not found.", file);
            OpsisRecoveryHub.Snapshot("Before updating widget " + existingName);
            AtomicFile.WriteAllText(file, JsonSerializer.Serialize(changed, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Pure geometry calculation for canvas/editor, no OS handles.</summary>
    public static (int X, int Y) Position(int canvasWidth, int canvasHeight,
        int contentWidth, int contentHeight, int xPercent, int yPercent)
    {
        var w = Math.Max(0, canvasWidth - contentWidth);
        var h = Math.Max(0, canvasHeight - contentHeight);
        return ((int)Math.Round(w * Math.Clamp(xPercent, 0, 100) / 100.0),
                (int)Math.Round(h * Math.Clamp(yPercent, 0, 100) / 100.0));
    }

    public static void Delete(string name) => File.Delete(FileFor(name));
}

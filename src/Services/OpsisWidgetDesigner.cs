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
    public sealed record Row(string Binding, string Text, string AccentHex, int FontSize);

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
        public Row[]? Rows { get; set; }
    }

    private static readonly object Sync = new();
    private static string Folder => Path.Combine(AppPaths.AppDataFolder, "widget-designs");
    private static string FileFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
            name.Any(x => !char.IsLetterOrDigit(x) && x is not (' ' or '_' or '-')))
            throw new ArgumentException("Widget name must be 1–60 letters, digits, spaces, '_' or '-'.");
        if (name != name.Trim()) throw new ArgumentException("Widget name cannot start or end with spaces.");
        string stem = name.ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³'))
            throw new ArgumentException("Reserved Windows widget name.");
        return Path.Combine(Folder, name + ".json");
    }

    public static Layout Copy(Layout layout)
    {
        Validate(layout);
        return JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout))!;
    }

    public static Layout ReadPortable(string path)
    {
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a local widget JSON file.");
        for (string? item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
            if (PackagePaths.IsLink(item)) throw new InvalidDataException("Linked widget files are not supported.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[32769];
        int count = stream.ReadAtLeast(buffer, buffer.Length, false);
        if (count > 32768) throw new InvalidDataException("Widget layout too large.");
        var layout = JsonSerializer.Deserialize<Layout>(buffer.AsSpan(0, count)) ??
            throw new InvalidDataException("Invalid widget layout.");
        Validate(layout);
        return layout;
    }

    public static void Export(string name, string path)
    {
        var layout = Read(name);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a JSON destination.");
        for (string? item = Path.GetDirectoryName(Path.GetFullPath(path)); item != null; item = Path.GetDirectoryName(item))
            if (PackagePaths.IsLink(item)) throw new InvalidDataException("Linked export path.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, layout, new JsonSerializerOptions { WriteIndented = true });
        output.Flush(true);
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
        if (layout.Rows != null && (layout.Rows.Length is < 2 or > 8 || layout.Rows.Any(row => row == null ||
            row.Binding is not ("Clock" or "Date" or "Text" or "CPU" or "RAM" or "Battery") ||
            row.Text == null || row.Text.Length > 250 || row.FontSize is < 12 or > 80 ||
            row.AccentHex == null || row.AccentHex.Length != 7 || row.AccentHex[0] != '#' ||
            !int.TryParse(row.AccentHex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))))
            throw new InvalidDataException("Unsupported composite widget row.");
    }

    public static IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Folder) || !PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, Folder))
            return Array.Empty<string>();
        var names = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly).Take(250))
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(file);
                _ = FileFor(name);
                if (PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, file)) names.Add(name);
            }
            catch (ArgumentException) { }
        }
        return names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static void Save(Layout layout)
    {
        Validate(layout);
        lock (Sync)
        {
            Directory.CreateDirectory(Folder);
            var file = FileFor(layout.Name);
            if (!PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, Folder)) throw new InvalidDataException("Linked widget storage.");
            if (File.Exists(file)) throw new IOException("Widget layout name already exists.");
            if (Directory.EnumerateFiles(Folder, "*.json").Take(250).Count() >= 250)
                throw new InvalidDataException("Widget layout quota reached.");
            AtomicFile.WriteAllText(file, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static Layout Read(string name)
    {
        var file = FileFor(name);
        if (!PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, file)) throw new InvalidDataException("Missing or linked widget layout.");
        var layout = ReadPortable(file);
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
            if (!PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, file)) throw new InvalidDataException("Linked widget layout.");
            if (JsonSerializer.Serialize(Read(existingName)) == JsonSerializer.Serialize(changed)) return;
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

    public static void Delete(string name)
    {
        lock (Sync)
        {
            var file = FileFor(name);
            if (!File.Exists(file)) return;
            if (!PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, file)) throw new InvalidDataException("Linked widget layout.");
            OpsisRecoveryHub.Snapshot("Before deleting widget " + name);
            File.Delete(file);
        }
    }
}

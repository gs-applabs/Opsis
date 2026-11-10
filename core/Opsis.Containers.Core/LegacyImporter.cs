using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opsis.Containers;

/// <summary>A monitor the importer normalizes against (desktop rectangle in pixels).</summary>
public sealed record MonitorInfo(string Id, Rectangle Bounds, bool Primary);

public sealed class ImportReport
{
    public int Profiles { get; set; }
    public int Containers { get; set; }
    public int Items { get; set; }
    public int Tabs { get; set; }
    public int HiddenOriginals { get; set; }
    public List<string> Warnings { get; } = new();
    /// <summary>What the 2.4.6 data had that the new core does not carry (shown to the user, plan 2.2).</summary>
    public List<string> NotImported { get; } = new();
}

/// <summary>
/// Reads the data of the 2.4.6 engine (<c>%AppData%\Opsis\containers\Profiles\&lt;profile&gt;\containers.json</c>, <c>options.json</c>, <c>hidden_originals.json</c>) into the new model.
/// STRICTLY READ-ONLY: files are opened for reading with shared access, nothing is written, renamed or deleted under the legacy folder (the old data stays, so the user
/// can go back to 2.4.6). Unknown fields are kept in <c>Extra</c>; what cannot be carried is listed in <see cref="ImportReport.NotImported"/>.
/// </summary>
public static class LegacyImporter
{
    private static readonly string[] Known = { "Id", "Title", "X", "Y", "Width", "Height", "ItemsType", "Items", "IsLocked", "IsHidden", "IsRolled", "UnrolledHeight", "TabsEnabled", "CurrentTab", "Tabs",
        "CustomColor", "ContainerBorderThickness", "IconSize", "IconSpacing", "CustomLaunchEffect", "TextColor", "TitleTextColor", "TitleTextSize", "BoldTitleText", "DisableTextShadow", "GrayscaleIcons", "ContainerBorderColor", "TitleBarColor", "TitleBarOpacity", "TitleBarHeight", "Path", "Source", "Text", "Note" };

    /// <summary>Imports every profile found under <paramref name="legacyRoot"/>; the profile named "Default" (or the first) is returned first.</summary>
    public static (List<ContainersDocument> Documents, ImportReport Report) Import(string legacyRoot, IReadOnlyList<MonitorInfo> monitors)
    {
        var report = new ImportReport();
        var docs = new List<ContainersDocument>();
        var profilesDir = Path.Combine(legacyRoot, "Profiles");
        if (!Directory.Exists(profilesDir)) { report.Warnings.Add("No Profiles folder: nothing to import."); return (docs, report); }
        foreach (var dir in Directory.GetDirectories(profilesDir).OrderBy(d => string.Equals(Path.GetFileName(d), "Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(d => d))
        {
            var doc = ImportProfile(dir, monitors, report);
            if (doc != null) { docs.Add(doc); report.Profiles++; }
        }
        return (docs, report);
    }

    public static ContainersDocument? ImportProfile(string profileDir, IReadOnlyList<MonitorInfo> monitors, ImportReport report)
    {
        var doc = new ContainersDocument { Profile = Path.GetFileName(profileDir) };
        var containersFile = Path.Combine(profileDir, "containers.json");
        var arr = ReadJson(containersFile, report) as JsonArray;
        if (arr == null) { report.Warnings.Add($"{doc.Profile}: containers.json missing or not a list."); return null; }

        foreach (var node in arr)
        {
            if (node is not JsonObject o) { report.Warnings.Add($"{doc.Profile}: a container entry is not an object (skipped)."); continue; }
            var c = ImportContainer(o, monitors, report);
            doc.Containers.Add(c); report.Containers++;
        }
        if (ReadJson(Path.Combine(profileDir, "hidden_originals.json"), report) is JsonNode hidden)
        {
            foreach (var h in hidden is JsonArray ha ? ha : (hidden is JsonObject ho ? new JsonArray(ho.Select(kv => (JsonNode?)JsonValue.Create(kv.Key)).ToArray()) : new JsonArray()))
                if (h?.ToString() is { Length: > 0 } s) { doc.HiddenOriginals.Add(s); report.HiddenOriginals++; }
        }
        if (File.Exists(Path.Combine(profileDir, "options.json")))
            report.NotImported.Add($"{doc.Profile}: options.json is read by the settings importer (one global options file), not per container.");
        if (Directory.Exists(Path.Combine(profileDir, "Shortcuts")))
            report.NotImported.Add($"{doc.Profile}: the Shortcuts folder (copies of .lnk files) stays where it is; items point to the original targets.");
        return doc;
    }

    private static Container ImportContainer(JsonObject o, IReadOnlyList<MonitorInfo> monitors, ImportReport report)
    {
        var c = new Container
        {
            Id = Str(o, "Id") is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            Title = Str(o, "Title") ?? "",
            Kind = Enum.TryParse<ContainerKind>(Str(o, "ItemsType"), true, out var k) ? k : ContainerKind.Data,
            Locked = Bool(o, "IsLocked"), Hidden = Bool(o, "IsHidden"), RolledUp = Bool(o, "IsRolled"),
            UnrolledHeight = Int(o, "UnrolledHeight"), TabsEnabled = Bool(o, "TabsEnabled"), CurrentTab = Int(o, "CurrentTab"),
        };
        c.Geometry = Normalize(Int(o, "X"), Int(o, "Y"), Int(o, "Width"), Int(o, "Height"), monitors);
        c.Appearance = new ContainerAppearance
        {
            FrameColor = Str(o, "CustomColor"), BorderColor = Str(o, "ContainerBorderColor"), BorderThickness = o["ContainerBorderThickness"] is null ? 1 : Int(o, "ContainerBorderThickness"),
            IconSize = Int(o, "IconSize"), IconSpacing = Int(o, "IconSpacing"), TextColor = Str(o, "TextColor"), GrayscaleIcons = Bool(o, "GrayscaleIcons"),
            TextShadow = !Bool(o, "DisableTextShadow"), LaunchEffect = Str(o, "CustomLaunchEffect")
        };
        c.TitleBar = new TitleBarStyle
        {
            Color = Str(o, "TitleBarColor") ?? "Auto", Opacity = o["TitleBarOpacity"] is null ? 90 : Int(o, "TitleBarOpacity"), Height = o["TitleBarHeight"] is null ? 28 : Int(o, "TitleBarHeight"),
            FontSize = o["TitleTextSize"] is null ? 10f : (float)Dbl(o, "TitleTextSize"), Bold = o["BoldTitleText"] is null || Bool(o, "BoldTitleText"), TextColor = Str(o, "TitleTextColor")
        };
        c.Source = c.Kind == ContainerKind.Portal ? (Str(o, "Path") ?? Str(o, "Source")) : c.Kind == ContainerKind.Note ? (Str(o, "Text") ?? Str(o, "Note")) : null;

        if (o["Items"] is JsonArray items) c.Items = ImportItems(items, report);
        if (o["Tabs"] is JsonArray tabs)
            foreach (var t in tabs)
            {
                if (t is not JsonObject to) continue;
                var tab = new ContainerTab { Name = Str(to, "Name") ?? Str(to, "Title") ?? "" };
                if (to["Items"] is JsonArray ti) tab.Items = ImportItems(ti, report);
                c.Tabs.Add(tab); report.Tabs++;
            }
        report.Items += c.Items.Count + c.Tabs.Sum(t => t.Items.Count);

        // keep every field the new model has no name for
        foreach (var kv in o)
            if (!Known.Contains(kv.Key) && kv.Value is not null)
            {
                c.Extra ??= new();
                c.Extra[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
                report.NotImported.Add($"container '{c.Title}': field '{kv.Key}' kept as-is (no equivalent yet)");
            }
        return c;
    }

    private static List<ContainerItem> ImportItems(JsonArray items, ImportReport report)
    {
        var list = new List<ContainerItem>();
        foreach (var n in items)
        {
            if (n is not JsonObject io) { report.Warnings.Add("an item is not an object (skipped)"); continue; }
            var it = new ContainerItem
            {
                Path = Str(io, "Filename") ?? Str(io, "Path") ?? "", DisplayName = Str(io, "DisplayName") ?? "", IsFolder = Bool(io, "IsFolder"), Arguments = Str(io, "Arguments")
            };
            foreach (var kv in io)
                if (kv.Key is not ("Filename" or "Path" or "DisplayName" or "IsFolder" or "Arguments") && kv.Value is not null)
                { it.Extra ??= new(); it.Extra[kv.Key] = JsonSerializer.SerializeToElement(kv.Value); }
            list.Add(it);
        }
        return list;
    }

    /// <summary>Pixel rectangle -> normalized geometry on the monitor that contains its centre (the primary one when none does).</summary>
    public static ContainerGeometry Normalize(int x, int y, int w, int h, IReadOnlyList<MonitorInfo> monitors)
    {
        var g = new ContainerGeometry { X = x, Y = y, Width = w, Height = h };
        if (monitors.Count == 0 || w <= 0 || h <= 0) return g;
        var centre = new Point(x + w / 2, y + h / 2);
        var m = monitors.FirstOrDefault(mm => mm.Bounds.Contains(centre)) ?? monitors.FirstOrDefault(mm => mm.Primary) ?? monitors[0];
        g.Monitor = m.Id;
        g.NX = Math.Clamp((x - m.Bounds.X) / (double)m.Bounds.Width, 0, 1);
        g.NY = Math.Clamp((y - m.Bounds.Y) / (double)m.Bounds.Height, 0, 1);
        g.NW = Math.Clamp(w / (double)m.Bounds.Width, 0, 1);
        g.NH = Math.Clamp(h / (double)m.Bounds.Height, 0, 1);
        return g;
    }

    /// <summary>The pixel rectangle of a normalized geometry on a (possibly different) monitor; falls back to the primary monitor when the saved one is gone.</summary>
    public static Rectangle Denormalize(ContainerGeometry g, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return new Rectangle(g.X, g.Y, g.Width, g.Height);
        var m = monitors.FirstOrDefault(mm => mm.Id == g.Monitor) ?? monitors.FirstOrDefault(mm => mm.Primary) ?? monitors[0];
        var r = new Rectangle(m.Bounds.X + (int)Math.Round(g.NX * m.Bounds.Width), m.Bounds.Y + (int)Math.Round(g.NY * m.Bounds.Height), Math.Max(60, (int)Math.Round(g.NW * m.Bounds.Width)), Math.Max(40, (int)Math.Round(g.NH * m.Bounds.Height)));
        r.X = Math.Clamp(r.X, m.Bounds.Left, Math.Max(m.Bounds.Left, m.Bounds.Right - r.Width));
        r.Y = Math.Clamp(r.Y, m.Bounds.Top, Math.Max(m.Bounds.Top, m.Bounds.Bottom - r.Height));
        return r;
    }

    private static JsonNode? ReadJson(string path, ImportReport report)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);   // read-only, never locks the engine out
            using var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
            var text = sr.ReadToEnd();
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"([A-Za-z_]*Fence[A-Za-z_]*)\"(\\s*:)", m => "\"" + m.Groups[1].Value.Replace("Fences", "Containers").Replace("Fence", "Container") + "\"" + m.Groups[2].Value);   // pre-2.2.0 key spelling
            return JsonNode.Parse(text);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            report.Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : o[k]?.ToString();
    private static bool Bool(JsonObject o, string k) => o[k] switch { JsonValue v when v.TryGetValue<bool>(out var b) => b, JsonValue v when v.TryGetValue<string>(out var s) => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase), _ => false };
    private static int Int(JsonObject o, string k) => o[k] switch { JsonValue v when v.TryGetValue<int>(out var i) => i, JsonValue v when v.TryGetValue<double>(out var d) => (int)Math.Round(d), JsonValue v when v.TryGetValue<string>(out var s) && int.TryParse(s, out var n) => n, _ => 0 };
    private static double Dbl(JsonObject o, string k) => o[k] switch { JsonValue v when v.TryGetValue<double>(out var d) => d, JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) => n, _ => 0 };
}

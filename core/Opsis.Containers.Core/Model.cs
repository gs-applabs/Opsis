using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Opsis.Containers;

public enum ContainerKind { Data, Portal, Note }

/// <summary>The title bar of a container: its OWN colour (not the frame's), height and font (user request 2026-11-10).</summary>
public sealed class TitleBarStyle
{
    /// <summary>"Auto" (follows the frame / theme) or "#RRGGBB".</summary>
    public string Color { get; set; } = "Auto";
    public int Opacity { get; set; } = 90;           // 0-100
    public int Height { get; set; } = 28;            // DIPs; also the height of the rolled-up container
    public float FontSize { get; set; } = 10f;       // points
    public string? FontFamily { get; set; }
    public bool Bold { get; set; } = true;
    public string? TextColor { get; set; }           // null = automatic contrast
}

/// <summary>Where a container sits. Stored NORMALIZED per monitor (0..1 of the monitor) so it survives a change of resolution or DPI; the last pixel values are kept as a hint.</summary>
public sealed class ContainerGeometry
{
    public string Monitor { get; set; } = "";        // device name / stable id of the monitor, "" = primary
    public double NX { get; set; }
    public double NY { get; set; }
    public double NW { get; set; }
    public double NH { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class ContainerItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsFolder { get; set; }
    public string? Arguments { get; set; }
    /// <summary>Fields of the old data that the new model has no name for yet; kept so nothing is lost on import and written back unchanged.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ContainerTab
{
    public string Name { get; set; } = "";
    public List<ContainerItem> Items { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ContainerAppearance
{
    public string? FrameColor { get; set; }          // custom tint of the frame, null = global
    public string? BorderColor { get; set; }
    public int BorderThickness { get; set; } = 1;
    public int IconSize { get; set; }                // 0 = "Desktop" size (same as the desktop icons)
    public int IconSpacing { get; set; }
    public string? TextColor { get; set; }
    public bool GrayscaleIcons { get; set; }
    public bool TextShadow { get; set; } = true;
    public string? LaunchEffect { get; set; }
    // schema 2 (2.6.0): look and behaviour per container
    public bool Glossy { get; set; }                 // a soft highlight over the top of the frame and the title bar
    public int TintOpacity { get; set; } = 35;       // 0-100, strength of FrameColor (the tint) over the glass
    public string? ItemFontFamily { get; set; }      // label font of the icons (null = Segoe UI)
    public float ItemFontSize { get; set; } = 9f;
    public bool OpenOnSingleClick { get; set; }      // false = double click launches (as the desktop does)
    public bool HoverRollUp { get; set; }            // a rolled-up container opens while the pointer is over it and rolls up again after it leaves
    public int HoverDelayMs { get; set; } = 400;
}

public sealed class Container
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public ContainerKind Kind { get; set; } = ContainerKind.Data;
    public ContainerGeometry Geometry { get; set; } = new();
    public bool Locked { get; set; }
    public bool Hidden { get; set; }
    public bool RolledUp { get; set; }
    /// <summary>Quick-hide / quick-show of all containers leaves this one alone (schema 2).</summary>
    public bool ExcludeFromQuickHide { get; set; }
    public int UnrolledHeight { get; set; }
    public bool TabsEnabled { get; set; }
    public int CurrentTab { get; set; }
    public List<ContainerTab> Tabs { get; set; } = new();
    public List<ContainerItem> Items { get; set; } = new();
    public ContainerAppearance Appearance { get; set; } = new();
    public TitleBarStyle TitleBar { get; set; } = new();
    /// <summary>Portal: the folder shown live. Note: the text.</summary>
    public string? Source { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The whole file: one profile's containers plus the desktop icons that were hidden for them (so they can ALWAYS be restored).</summary>
public sealed class ContainersDocument
{
    public int SchemaVersion { get; set; } = ContainersStore.CurrentVersion;
    public string Profile { get; set; } = "Default";
    public List<Container> Containers { get; set; } = new();
    public List<string> HiddenOriginals { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>JSON file with <c>schemaVersion</c>, step migrations, atomic write (temp + replace), one .bak of the last good file, damaged file moved to .corrupt.</summary>
public sealed class ContainersStore
{
    public const int CurrentVersion = 2;   // 2 = glossy / tint strength / label font / click mode / hover roll-up / exclude from quick-hide (all have defaults, so a version-1 file loads unchanged and is rewritten as 2)
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly object gate = new();
    private readonly SortedDictionary<int, Action<JsonObject>> migrations = new();
    public string Path { get; }

    public ContainersStore(string path) { Path = path; }

    public ContainersStore AddMigration(int from, Action<JsonObject> step) { migrations[from] = step; return this; }

    /// <summary>Reads the file; a sharing violation / lock (IOException) is retried for up to about 10 s before giving up.</summary>
    internal string ReadWithRetry(int attempts = 40, int delayMs = 250)
    {
        for (int i = 1; ; i++)
        {
            try { return File.ReadAllText(Path); }
            catch (IOException) when (i < attempts) { System.Threading.Thread.Sleep(delayMs); }
        }
    }

    public ContainersDocument Load()
    {
        lock (gate)
        {
            if (!File.Exists(Path)) return new ContainersDocument();
            try
            {
                var node = JsonNode.Parse(ReadWithRetry()) as JsonObject ?? throw new JsonException("not an object");
                var v = node["schemaVersion"]?.GetValue<int>() ?? 0;
                while (v < CurrentVersion) { if (migrations.TryGetValue(v, out var step)) step(node); v++; }
                node["schemaVersion"] = Math.Max(v, node["schemaVersion"]?.GetValue<int>() ?? 0);
                return node.Deserialize<ContainersDocument>(Json) ?? new ContainersDocument();
            }
            catch (IOException)
            {
                // the file is only LOCKED (another process / an antivirus scan holds it for longer than the retry): it is not damaged, so it is never renamed to .corrupt
                return new ContainersDocument();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                try { File.Move(Path, Path + ".corrupt", true); } catch (IOException) { }
                return new ContainersDocument();
            }
        }
    }

    public void Save(ContainersDocument doc)
    {
        lock (gate)
        {
            doc.SchemaVersion = CurrentVersion;
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, Json));
            if (File.Exists(Path)) File.Replace(tmp, Path, Path + ".bak"); else File.Move(tmp, Path);
        }
    }
}

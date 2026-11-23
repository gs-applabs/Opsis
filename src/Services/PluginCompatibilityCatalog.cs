using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// In-release compatibility table. It describes the planned branches and assembly
/// identities, NOT a claim that a plugin was successfully loaded on this PC.
/// Never probes the disk, registry or internet just to show the table.
/// </summary>
internal static class PluginCompatibilityCatalog
{
    internal sealed record Entry(string Name, string Repository, string Integration,
        string Branch, string? Dll, string? AssemblyVersion, string? ProductVersion,
        string Installation, string Validation);

    internal static IReadOnlyList<Entry> Read()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "suite", "plugin-compatibility.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return Array.Empty<Entry>();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                return Array.Empty<Entry>();
            var rows = new List<Entry>();
            foreach (var item in doc.RootElement.GetProperty("plugins").EnumerateArray())
            {
                static string? Str(JsonElement obj, string key) =>
                    obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString() : null;
                if (rows.Count >= 16) break;
                string? name = Str(item, "name");
                if (string.IsNullOrWhiteSpace(name) || name.Length > 150) continue;
                rows.Add(new Entry(name, Str(item, "repository") ?? "", Str(item, "integration") ?? "",
                    Str(item, "branch") ?? "", Str(item, "dll"), Str(item, "assemblyVersion"),
                    Str(item, "productVersion"), Str(item, "installation") ?? "", Str(item, "validation") ?? ""));
            }
            return rows;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        { return Array.Empty<Entry>(); }
    }
}

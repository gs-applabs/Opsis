using System.Text.Json;
using System.Reflection;

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

    internal sealed record InstalledProbe(string State, string? FoundDll);

    /// <summary>
    /// Safe metadata-only ABI probe. Never loads a plugin assembly into Opsis,
    /// executes code, follows reparse-point folders or claims that E2E passed.
    /// This is a preliminary gate; installer/plugin host tests are separate.
    /// </summary>
    internal static InstalledProbe Probe(Entry entry)
    {
        if (entry.Dll is null)
            return new InstalledProbe("standalone / external process (no plugin DLL)", null);
        if (string.Equals(entry.Dll, "OpsisAppearancePlugin.dll", StringComparison.OrdinalIgnoreCase))
            return new InstalledProbe("built into Opsis; legacy plugin superseded", null);
        if (entry.Dll != Path.GetFileName(entry.Dll))
            return new InstalledProbe("invalid manifest DLL name", null);
        string[] roots =
        [
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis", "plugins"),
            Path.Combine(AppPaths.AppDataFolder, "plugins")
        ];
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var candidates = new[] { root }.Concat(Directory.EnumerateDirectories(root).Take(64));
                foreach (string folder in candidates)
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    var dll = Path.Combine(folder, entry.Dll);
                    if (!File.Exists(dll) || (File.GetAttributes(dll) & FileAttributes.ReparsePoint) != 0) continue;
                    var meta = AssemblyName.GetAssemblyName(dll);
                    string expectedName = Path.GetFileNameWithoutExtension(entry.Dll);
                    if (!string.Equals(meta.Name, expectedName, StringComparison.OrdinalIgnoreCase))
                        return new InstalledProbe("assembly name mismatch / blocked", dll);
                    if (entry.AssemblyVersion is { } expected &&
                        !string.Equals(meta.Version?.ToString(4), expected, StringComparison.Ordinal))
                        return new InstalledProbe("DLL ABI mismatch: " + meta.Version + " != " + expected, dll);
                    return new InstalledProbe("assembly metadata compatible / host E2E pending", dll);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException)
            { return new InstalledProbe("cannot verify DLL: " + ex.GetType().Name, null); }
        }
        return new InstalledProbe("not installed", null);
    }

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

using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// A transparent DECLARATION, not an OS sandbox. In-process managed DLLs can
/// call Windows APIs directly. User-granted plugin approval remains SHA-bound;
/// users must disable an installed plugin and restart Opsis to unload its code.
/// </summary>
internal static class PluginCapabilityManifest
{
    // API negotiation is independent of the existing assembly ABI 1.0.0.0.
    // Only the legacy API is implemented today. Never advertise SDK2 support prematurely.
    internal const int SupportedApi = 1;
    internal static bool SupportsApi(int minimum, int maximum) => minimum >= 1 && minimum <= SupportedApi && maximum >= SupportedApi;

    internal static void RequireCompatibleApi(string folder)
    {
        var path = Path.Combine(folder, "opsis-sdk.json");
        if (!File.Exists(path)) return; // legacy contract adapter
        if (!PackagePaths.IsExistingUnlinkedPath(folder, path) || new FileInfo(path).Length > 4096)
            throw new InvalidDataException("Unsafe SDK manifest.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1 ||
            !root.TryGetProperty("minApi", out var min) || min.ValueKind != JsonValueKind.Number || !min.TryGetInt32(out int minimum) ||
            !root.TryGetProperty("maxApi", out var max) || max.ValueKind != JsonValueKind.Number || !max.TryGetInt32(out int maximum) || !SupportsApi(minimum, maximum) ||
            !root.TryGetProperty("execution", out var execution) || execution.ValueKind != JsonValueKind.String || execution.GetString() != "trusted-in-process")
            throw new InvalidDataException("Unsupported plugin API or execution model; isolated worker is not implemented.");
    }

    private sealed class Declaration
    {
        public bool Network { get; set; }
        public bool Audio { get; set; }
        public bool LocalFiles { get; set; }
    }

    internal static string[] Read(string folder)
    {
        var path = Path.Combine(folder, "opsis-permissions.json");
        if (!File.Exists(path)) return Array.Empty<string>();
        if (!PackagePaths.IsExistingUnlinkedPath(folder, path) || new FileInfo(path).Length > 4096)
            throw new InvalidDataException("Unsafe plugin permission declaration.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<Declaration>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Empty plugin permission declaration.");
        var caps = new List<string>(3);
        if (data.Network) caps.Add("network");
        if (data.Audio) caps.Add("audio");
        if (data.LocalFiles) caps.Add("local-files");
        return caps.ToArray();
    }

    internal static string Summary(string folder)
    {
        try
        {
            var caps = Read(folder);
            return caps.Length == 0 ? "Not declared / Δεν δηλώθηκαν" : string.Join(", ", caps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { return "Invalid declaration: " + ex.GetType().Name; }
    }
}

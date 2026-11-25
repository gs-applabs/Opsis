using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// A transparent DECLARATION, not an OS sandbox. In-process managed DLLs can
/// call Windows APIs directly. User-granted plugin approval remains SHA-bound;
/// users must disable an installed plugin and restart Opsis to unload its code.
/// </summary>
internal static class PluginCapabilityManifest
{
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

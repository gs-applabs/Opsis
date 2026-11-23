using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>Revocable permission only for the limited, message-based Opsis SDK API.
/// WebView2's network and browser permissions remain denied regardless of the grant.
/// Grants are bound to a digest of the widget manifest and its entry page.</summary>
internal static class WidgetConsentStore
{
    private static readonly object Sync = new();
    private static string PathName => Path.Combine(AppPaths.AppDataFolder, "widget-consent.json");
    private static Dictionary<string,string> Read()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal static string Fingerprint(string folder, string entry)
    {
        var manifest = WidgetPackageFormat.ExistingResource(folder, WidgetPackageFormat.ManifestName)
            ?? throw new InvalidDataException("Missing widget manifest");
        var page = WidgetPackageFormat.ExistingResource(folder, entry)
            ?? throw new InvalidDataException("Missing widget entry page");
        using var hash = SHA256.Create();
        var a = SHA256.HashData(File.ReadAllBytes(manifest));
        var b = SHA256.HashData(File.ReadAllBytes(page));
        return Convert.ToHexString(hash.ComputeHash(a.Concat(b).ToArray()));
    }
    internal static bool Allowed(string id, string fingerprint)
    { lock (Sync) { var grants = Read(); return grants.TryGetValue(id, out var digest) && digest == fingerprint; } }
    internal static void Grant(string id, string fingerprint)
    {
        lock (Sync)
        {
            var grants = Read(); grants[id] = fingerprint;
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            AtomicFile.WriteAllText(PathName, JsonSerializer.Serialize(grants));
        }
    }
    internal static void Revoke(string id)
    {
        lock (Sync)
        {
            var grants = Read(); grants.Remove(id);
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            AtomicFile.WriteAllText(PathName, JsonSerializer.Serialize(grants));
        }
    }
}

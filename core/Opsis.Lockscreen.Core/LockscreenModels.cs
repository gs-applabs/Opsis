using System.Security.Cryptography;
using System.Text.Json;

namespace Opsis.Lockscreen;

/// <summary>What the helper saved before changing anything: the original cache files, the original ACLs (icacls /save) and the original Windows feature override.</summary>
public sealed class BackupManifest
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string Sid { get; set; } = "";
    public string CacheRoot { get; set; } = "";
    public bool Applied { get; set; }
    public string? AppliedSha256 { get; set; }
    public List<BackupFile> Files { get; set; } = new();
    public List<AclEntry> Acls { get; set; } = new();
    public FeatureBackup Feature { get; set; } = new();

    public static BackupManifest? Load(string backupRoot)
    {
        string p = LockscreenPaths.ManifestPath(backupRoot);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(p));
    }

    public void Save(string backupRoot)
    {
        string p = LockscreenPaths.ManifestPath(backupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        string tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, p, overwrite: true);
    }
}

public sealed class BackupFile
{
    public string Rel { get; set; } = "";          // relative to the cache root
    public string BackupName { get; set; } = "";   // file name inside backup\files
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class AclEntry
{
    public string Rel { get; set; } = "";          // "" = the cache root itself
    public string SaveFile { get; set; } = "";     // icacls /save output, inside backup\acl
    public bool OwnerChanged { get; set; }        // takeown was needed: the owner is put back to SYSTEM after /restore
}

public sealed class FeatureBackup
{
    public bool Captured { get; set; }
    public bool KeyExisted { get; set; }
    public int? EnabledState { get; set; }
    public int? EnabledStateOptions { get; set; }
    public bool Changed { get; set; }
}

public sealed record OpResult(bool Ok, string Code, string Message, List<string> Details)
{
    public static OpResult Success(string code, string msg, List<string>? d = null) => new(true, code, msg, d ?? new());
    public static OpResult Fail(string code, string msg, List<string>? d = null) => new(false, code, msg, d ?? new());
}

/// <summary>Read-only view for the UI (no elevation needed): is there something to restore?</summary>
public sealed record LockscreenState(bool HasBackup, bool Applied, bool FeatureChanged)
{
    public static LockscreenState Read(string backupRoot)
    {
        try
        {
            var m = BackupManifest.Load(backupRoot);
            return m == null ? new(false, false, false) : new(true, m.Applied, m.Feature.Changed);
        }
        catch (Exception) { return new(false, false, false); }
    }
}

internal static class Hashing
{
    public static string Sha256(string path) { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(s)); }
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}

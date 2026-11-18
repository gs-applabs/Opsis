using System.Security.Principal;

namespace Opsis.Lockscreen;

/// <summary>
/// Fixed locations and path validation of the animated lock screen feature. The cache layout (SystemData\&lt;SID&gt;\ReadOnly\LockScreen*\LockScreen.jpg and *_notdimmed.jpg)
/// and the path rules are adapted from LockscreenGif (MIT, (c) 2026 Leapward-Koex), see THIRD_PARTY_NOTICES.md.
/// </summary>
public static class LockscreenPaths
{
    public const string MainImageName = "LockScreen.jpg";
    public const string VariantSuffix = "_notdimmed.jpg";
    /// <summary>Largest GIF accepted (the helper copies it into a system folder).</summary>
    public const long MaxGifBytes = 64L * 1024 * 1024;
    /// <summary>The only Windows feature the helper may touch (ID 38943831, encoded leaf 2920700556 of the priority-8 override store).</summary>
    public const string FeatureKeyPath = @"SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\2920700556";

    public static string DataRoot(string appDataFolder) => Path.Combine(appDataFolder, "lockscreen");
    public static string StagingGif(string dataRoot) => Path.Combine(dataRoot, "staging", "anim.gif");
    /// <summary>Admin-only folder (created by the elevated helper under ProgramData): the backup and the last result. Users cannot tamper with it.</summary>
    public static string BackupRoot(string programData) => Path.Combine(programData, "Opsis", "lockscreen");
    public static string ManifestPath(string backupRoot) => Path.Combine(backupRoot, "manifest.json");
    public static string ResultPath(string backupRoot) => Path.Combine(backupRoot, "result.json");

    public static string DefaultCacheRoot(string sid, string programData) =>
        Path.Combine(programData, "Microsoft", "Windows", "SystemData", new SecurityIdentifier(sid).Value, "ReadOnly");

    /// <summary>True for a GIF87a / GIF89a header.</summary>
    public static bool IsGif(ReadOnlySpan<byte> head) =>
        head.Length >= 6 && head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8' && (head[4] == '7' || head[4] == '9') && head[5] == 'a';

    /// <summary>
    /// Accepts only the cache root itself (read), a LockScreen* folder directly below it, or LockScreen.jpg / *_notdimmed.jpg inside such a folder; refuses links on the whole chain up to the root.
    /// </summary>
    public static void ValidateCachePath(string root, string path, bool write)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            if (write) throw new UnauthorizedAccessException("The cache root only accepts read access.");
        }
        else
        {
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Outside the current user's cache.");
            var parts = full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);
            if (parts.Length > 2 || !parts[0].StartsWith("LockScreen", StringComparison.OrdinalIgnoreCase)
                || (parts.Length == 2 && !parts[1].Equals(MainImageName, StringComparison.OrdinalIgnoreCase) && !parts[1].EndsWith(VariantSuffix, StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("Unexpected cache path.");
        }
        RejectLinks(full, root);
    }

    /// <summary>Throws when the path or any parent up to (and including) <paramref name="stopAt"/> is a reparse point (junction / symlink).</summary>
    public static void RejectLinks(string path, string stopAt)
    {
        stopAt = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        for (string? cur = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); cur != null; cur = Path.GetDirectoryName(cur))
        {
            if ((File.Exists(cur) || Directory.Exists(cur)) && (File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Links are not followed: " + cur);
            if (cur.Equals(stopAt, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
}

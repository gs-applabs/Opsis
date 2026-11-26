using System.Diagnostics;
using System.Security.Cryptography;

namespace MotionDesk.Services;

/// <summary>
/// The optional "Wallpapers add-on" package (112 wallpapers): state of the installed pack, and a user-started download + install of the
/// pinned package. The package comes ONLY from this repository's releases (https, no redirects to other hosts that are not GitHub's CDN),
/// its SHA-256 is pinned in this build (so a replaced release asset is rejected), the file is verified through a locked handle right before
/// it is started (no swap between check and run), and the installer itself asks for elevation (UAC) and shows its own wizard.
/// </summary>
public static class WallpaperAddOnService
{
    public const string Version = "1.3.0";
    public const int WallpaperCount = 112;
    public const string FileName = "OpsisWallpapersAddOnSetup-1.3.0.exe";
    public const long ExpectedBytes = 213233755;
    public const string ExpectedSha256 = "f46b09acb8236e1fe2befd83102c1b4ad6e58885c6fdac11c64d15c685a5d624";
    public const string DownloadUrl = "https://github.com/gs-applabs/Opsis/releases/download/wallpapers-addon-v1.3.0/" + FileName;
    private const long MaxBytes = 256L * 1024 * 1024;

    public enum PackState { NotInstalled, UpdateAvailable, UpToDate }

    public sealed record Status(PackState State, int InstalledCount);

    /// <summary>Tests only: a different download folder.</summary>
    public static string? DownloadDirOverride { get; set; }

    public static string DownloadDir => DownloadDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Opsis", "addons");

    /// <summary>The installed pack: the wallpapers folders under <c>&lt;Opsis&gt;\wallpapers\packs\opsis-100</c> (no scan of anything else).</summary>
    public static Status ReadStatus(string? opsisFolder = null)
    {
        string root = opsisFolder ?? AppContext.BaseDirectory;
        string pack = Path.Combine(root, "wallpapers", "packs", "opsis-100");
        int count = 0;
        try { if (Directory.Exists(pack)) count = Directory.EnumerateDirectories(pack).Take(1000).Count(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { count = 0; }
        var state = count >= WallpaperCount ? PackState.UpToDate : count > 0 ? PackState.UpdateAvailable : PackState.NotInstalled;
        return new Status(state, count);
    }

    /// <summary>True only for the exact pinned asset of this repository.</summary>
    public static bool IsTrustedUrl(string? url) => string.Equals(url, DownloadUrl, StringComparison.Ordinal) && UpdateCheckService.IsTrustedDownloadUrl(url);

    /// <summary>Downloads the package to <see cref="DownloadDir"/>, checks size and the pinned SHA-256 and returns the verified path. The file is deleted when it does not match.</summary>
    public static async Task<string> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsTrustedUrl(DownloadUrl)) throw new InvalidOperationException("untrusted-url");
        Directory.CreateDirectory(DownloadDir);
        string path = Path.Combine(DownloadDir, FileName);
        string part = path + ".part";
        // an earlier, complete and verified download is reused
        if (File.Exists(path) && FileMatches(path)) { progress?.Report(1); return path; }
        TryDelete(path); TryDelete(part);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Opsis-Addon");
        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? ExpectedBytes;
        if (total > MaxBytes || total != ExpectedBytes) throw new InvalidDataException("unexpected-size");

        using var sha = SHA256.Create();
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0; int n; int lastPercent = -1;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    read += n;
                    if (read > ExpectedBytes) throw new InvalidDataException("too-large");
                    await target.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    int percent = (int)(read * 100 / ExpectedBytes);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(read / (double)ExpectedBytes); }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (read != ExpectedBytes) throw new InvalidDataException("incomplete");
            }
            if (!string.Equals(Convert.ToHexString(sha.Hash!), ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("hash-mismatch");
            File.Move(part, path, true);
        }
        catch { TryDelete(part); throw; }
        return path;
    }

    /// <summary>Verifies the package through a handle nobody else may write to, then starts its wizard (UAC is asked by the installer itself).</summary>
    public static void Launch(string path)
    {
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using (var sha = SHA256.Create())
        {
            if (!string.Equals(Convert.ToHexString(sha.ComputeHash(locked)), ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("hash-mismatch");
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
    }

    private static bool FileMatches(string path)
    {
        try
        {
            if (new FileInfo(path).Length != ExpectedBytes) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            return string.Equals(Convert.ToHexString(sha.ComputeHash(fs)), ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Opt-in previous-binary recovery after a failed profile migration. Never
/// automatically runs a setup or treats a damaged JSON profile as authority.
/// A previous installer must already have been downloaded AND SHA-256 checked
/// by Opsis. No candidate means the app keeps its safe profile rollback only.
/// </summary>
internal static class VerifiedRollbackService
{
    private sealed record Candidate(string Version, string Sha256, string FileName);
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "rollback");
    private static string Metadata => Path.Combine(Root, "candidate.json");
    private static string FailedProfile => Path.Combine(Root, "failed-profile.txt");
    private const long MaxBytes = 150L * 1024 * 1024;

    internal static bool ValidDigest(string? digest) => digest is { Length: 64 } && digest.All(Uri.IsHexDigit);
    internal static string ArchiveName(string version, string digest) => "OpsisSetup-" + version + "-" + digest.ToLowerInvariant() + ".exe";
    private static bool SafeExisting(string path) => File.Exists(path) && PackagePaths.IsExistingUnlinkedPath(Root, path);
    private static bool ValidArchiveName(string version, string digest, string? name) =>
        name == ArchiveName(version, digest) || name == "OpsisSetup-" + version + ".exe"; // read legacy metadata only

    internal static void ArchiveCurrentInstallerBeforeUpdate()
    {
        try
        {
            var source = Path.Combine(UpdateInstallerService.UpdateDir, "OpsisSetup.exe");
            var shaFile = source + ".sha256";
            if (!File.Exists(source) || !File.Exists(shaFile) ||
                !PackagePaths.IsExistingUnlinkedPath(UpdateInstallerService.UpdateDir, source) ||
                !PackagePaths.IsExistingUnlinkedPath(UpdateInstallerService.UpdateDir, shaFile) || new FileInfo(shaFile).Length > 4096) return;
            var version = FileVersionInfo.GetVersionInfo(source).ProductVersion;
            if (!string.Equals(version, AppVersion.Short, StringComparison.Ordinal)) return;
            if (new FileInfo(source).Length is <= 0 or > MaxBytes) return;
            var digest = File.ReadAllText(shaFile).Trim();
            if (!ValidDigest(digest)) return;
            using var stream = UpdateInstallerService.OpenVerified(source, digest);
            Directory.CreateDirectory(Root);
            if (PackagePaths.IsLink(Root) || (File.Exists(Metadata) && !SafeExisting(Metadata))) return;
            var fileName = ArchiveName(AppVersion.Short, digest);
            var dest = Path.Combine(Root, fileName);
            var stage = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".part");
            try
            {
                if (!File.Exists(dest))
                {
                    using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { stream.CopyTo(output); output.Flush(flushToDisk: true); }
                    using (UpdateInstallerService.OpenVerified(stage, digest)) { }
                    File.Move(stage, dest, overwrite: false);
                }
                if (!SafeExisting(dest)) return;
                using (UpdateInstallerService.OpenVerified(dest, digest)) { }
                // Content-addressed archive first, metadata last. Never overwrite
                // a previously recorded candidate before its replacement is complete.
                AtomicFile.WriteAllText(Metadata, JsonSerializer.Serialize(new Candidate(AppVersion.Short, digest, fileName)));
            }
            finally { try { File.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { RotatingLogger.Write("Rollback archive unavailable: " + ex.GetType().Name); }
    }

    internal static void NoteProfileFailed(string name)
    {
        if (!string.Equals(name, "Last Session", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (!TryGetVerifiedCandidate(out _, out _)) return;
            Directory.CreateDirectory(Root);
            if (PackagePaths.IsLink(Root) || (File.Exists(FailedProfile) && !SafeExisting(FailedProfile))) return;
            AtomicFile.WriteAllText(FailedProfile, AppVersion.Short);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { RotatingLogger.Write("Profile rollback marker unavailable: " + ex.GetType().Name); }
    }

    internal static bool TryConsumeOffer(out string setupPath, out string sha256)
    {
        setupPath = sha256 = string.Empty;
        try
        {
            if (!SafeExisting(FailedProfile) || new FileInfo(FailedProfile).Length > 128) return false;
            var failedAt = File.ReadAllText(FailedProfile).Trim();
            File.Delete(FailedProfile); // prompt once; no repeating on every startup
            return failedAt == AppVersion.Short && TryGetVerifiedCandidate(out setupPath, out sha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryGetVerifiedCandidate(out string setupPath, out string sha256)
    {
        setupPath = sha256 = string.Empty;
        try
        {
            if (!SafeExisting(Metadata) || new FileInfo(Metadata).Length > 4096) return false;
            // Do not recover executable authority from an arbitrary .bak or .tmp.
            var row = JsonSerializer.Deserialize<Candidate>(File.ReadAllText(Metadata));
            if (row == null || row.Version == AppVersion.Short ||
                !Version.TryParse(row.Version, out var prior) || prior >= AppVersion.Number ||
                !ValidDigest(row.Sha256) || !ValidArchiveName(row.Version, row.Sha256, row.FileName)) return false;
            var candidate = Path.Combine(Root, row.FileName);
            if (!SafeExisting(candidate) || new FileInfo(candidate).Length is <= 0 or > MaxBytes) return false;
            using var fs = File.OpenRead(candidate);
            var actual = Convert.ToHexString(SHA256.HashData(fs));
            if (!actual.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(FileVersionInfo.GetVersionInfo(candidate).ProductVersion, row.Version, StringComparison.Ordinal)) return false;
            setupPath = candidate; sha256 = row.Sha256;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return false; }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Η πραγματική εγκατάσταση μιας νεότερης έκδοσης — ίδια λογική με το self-update του GearWin,
    // portαρισμένη από το ήδη ζωντανά δοκιμασμένο UpdateInstaller.cs του OpsisNewsCrawler. Το setup
    // έρχεται ΜΟΝΟ από τα releases αυτού του repo, επαληθεύεται με SHA-256 (digest που δημοσιεύει
    // το ίδιο το GitHub, με fallback το .sha256 asset) και διαγράφεται αν δεν ταιριάζει· τρέχει
    // ΜΟΝΟ αφού το Opsis έχει κλείσει (ο installer αντικαθιστά τα τρέχοντα αρχεία), σιωπηλά, και το
    // Opsis ξανανοίγει μετά.
    public static class UpdateInstallerService
    {
        private const long MaxSetupBytes = 150L * 1024 * 1024;

        public static string UpdateDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Opsis", "update");

        // Κατεβάζει+επαληθεύει. Πετάει InvalidOperationException όταν δεν υπάρχει τίποτα να
        // επαληθευτεί απέναντι, InvalidDataException σε αναντιστοιχία hash (το αρχείο διαγράφεται).
        public static async Task<(string Path, string Sha256)> DownloadAsync(
            UpdateCheckService.UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (update.SetupUrl is null || !UpdateCheckService.IsReleaseAssetUrl(update.SetupUrl, update.Tag, "OpsisSetup.exe"))
                throw new InvalidOperationException("untrusted-url");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Opsis-Update");

            var expected = update.Sha256;
            if (expected is null && update.ChecksumUrl is not null && UpdateCheckService.IsReleaseAssetUrl(update.ChecksumUrl, update.Tag, "OpsisSetup.exe.sha256"))
            {
                using var checksumResponse = await http.GetAsync(update.ChecksumUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                checksumResponse.EnsureSuccessStatusCode();
                if (checksumResponse.Content.Headers.ContentLength > 4096) throw new InvalidDataException("checksum-too-large");
                await using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(ct);
                var checksumBytes = new byte[4097];
                int count = 0, got;
                while (count < checksumBytes.Length && (got = await checksumStream.ReadAsync(checksumBytes.AsMemory(count), ct)) > 0) count += got;
                if (count > 4096) throw new InvalidDataException("checksum-too-large");
                var text = Encoding.UTF8.GetString(checksumBytes, 0, count);
                var first = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                if (first.Length == 64 && first.All(Uri.IsHexDigit)) expected = first;
            }
            if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new InvalidOperationException("no-digest");
            expected = expected.ToLowerInvariant();

            Directory.CreateDirectory(UpdateDir);
            if (PackagePaths.IsLink(UpdateDir)) throw new IOException("linked-update-directory");
            var path = Path.Combine(UpdateDir, "OpsisSetup.exe");
            var lockPath = Path.Combine(UpdateDir, "download.lock");
            if (File.Exists(lockPath) && PackagePaths.IsLink(lockPath)) throw new IOException("linked-update-lock");
            using var downloadLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var stage = Path.Combine(UpdateDir, "download-" + Guid.NewGuid().ToString("N") + ".part");
            var digestStage = stage + ".sha256";
            try
            {
                using var response = await http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                if (total > MaxSetupBytes) throw new InvalidDataException("too-large");
                using var sha = SHA256.Create();
                long read = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        read += n;
                        if (read > MaxSetupBytes) throw new InvalidDataException("too-large");
                        await target.WriteAsync(buffer.AsMemory(0, n), ct);
                        sha.TransformBlock(buffer, 0, n, null, 0);
                        if (total > 0) progress?.Report(Math.Min(1.0, read / (double)total));
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    target.Flush(flushToDisk: true);
                }
                var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (read == 0 || (total > 0 && read != total) || actual != expected)
                    throw new InvalidDataException("hash-or-length-mismatch");
                ct.ThrowIfCancellationRequested();
                using (OpenVerified(stage, expected)) { } // verify persisted bytes, not just network buffers
                using (var digestFile = new FileStream(digestStage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    digestFile.Write(Encoding.ASCII.GetBytes(actual));
                    digestFile.Flush(flushToDisk: true);
                }
                // Only now archive the previous verified setup; failures before
                // publication leave the previous installer and unrelated files intact.
                if ((File.Exists(path) && PackagePaths.IsLink(path)) ||
                    (File.Exists(path + ".sha256") && PackagePaths.IsLink(path + ".sha256")))
                    throw new IOException("linked-update-destination");
                VerifiedRollbackService.ArchiveCurrentInstallerBeforeUpdate();
                File.Move(stage, path, overwrite: true);
                // If interrupted between renames, the mismatching digest fails closed.
                File.Move(digestStage, path + ".sha256", overwrite: true);
                return (path, actual);
            }
            finally
            {
                foreach (var temp in new[] { stage, digestStage })
                    try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Opens the downloaded setup so that NOBODY can change or delete it afterwards (FileShare.Read), and checks its SHA-256 from that very handle.
        /// The setup runs elevated (installer in Program Files), so a gap between "hash checked" and "file executed" would let any user-level process
        /// swap it (TOCTOU). The returned stream must stay open until the setup process has been created (the running image is then write-protected).
        /// Throws InvalidDataException when the hash does not match (the stream is closed first).
        /// </summary>
        internal static FileStream OpenVerified(string setupPath, string sha256)
        {
            if (PackagePaths.IsLink(setupPath)) throw new IOException("linked-setup");
            var fs = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var sha = SHA256.Create();
                var actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("hash-mismatch");
                fs.Position = 0;
                return fs;
            }
            catch { fs.Dispose(); throw; }
        }

        // Verifies the setup through a locked handle and starts it right away (silent; /CLOSEAPPLICATIONS lets the installer close Opsis through its
        // AppMutex, see installer.iss) - the lock is released only after the process exists. /RESTARTOPSIS=1 makes the setup itself open Opsis again when it
        // has finished (a [Run] entry of installer.iss), so no helper process, script or hidden window is involved at all. The caller terminates this
        // process right after.
        internal const string SetupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTOPSIS=1";

        /// <summary>Where the installer writes its log of the update (the first thing to read when "it downloaded but did not install").</summary>
        public static string LastUpdateLog => Path.Combine(UpdateDir, "last-update.log");

        /// <summary>The setup arguments plus <c>/LOG</c>: a silent installer that stops (declined UAC, a locked file, the app still running) leaves no other trace.</summary>
        internal static string BuildArguments(string logPath) => SetupArguments + " \"/LOG=" + logPath + "\"";

        public static void LaunchAfterExit(string setupPath, string sha256)
        {
            using var locked = OpenVerified(setupPath, sha256);
            try { File.WriteAllText(Path.Combine(UpdateDir, "last-update-started.txt"), DateTime.Now.ToString("o") + "\nsetup=" + setupPath + "\nfrom=" + UpdateCheckService.CurrentVersion + "\n"); } catch (Exception) { }
            Process.Start(new ProcessStartInfo(setupPath)
            {
                Arguments = BuildArguments(LastUpdateLog),
                UseShellExecute = true,                       // lets the installer's manifest ask for elevation (UAC)
                WorkingDirectory = Path.GetDirectoryName(setupPath)!
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpsisNewsCrawler.Plugin.Abstractions;

namespace MotionDesk.Services
{
    /// <summary>A loaded plugin that tells where its releases are (IPluginUpdateSource).</summary>
    public sealed record PluginUpdateCandidate(string Id, string Name, string FolderName, string Version, PluginUpdateSource Source);

    /// <summary>A newer release of a plugin, found by <see cref="PluginUpdateService.CheckAsync"/>.</summary>
    public sealed record PluginUpdateInfo(PluginUpdateCandidate Plugin, Version Latest, string Tag, string PageUrl, string ZipUrl, string? Sha256, string? ChecksumUrl)
    {
        public bool CanInstallAutomatically => Sha256 is not null || ChecksumUrl is not null;
    }

    /// <summary>
    /// Updates of plugins (Opsis 2.6.0, task 15): ONE place - the Opsis update button (About page) - checks and installs them; a plugin never updates itself (it is told so with
    /// PluginHostSettings.HostManagesUpdates). Same rules as the updater of Opsis itself (UpdateCheckService / UpdateInstallerService):
    ///  - only the public releases API of the plugin's GitHub repository, and only when the repository owner is one of the two accounts of the project (gs-applabs, and the old name
    ///    giorstergiopoulos-design which GitHub redirects);
    ///  - the asset is downloaded only from https://github.com/&lt;trusted owner&gt;/&lt;the plugin's repository&gt;/releases/download/ - nothing else;
    ///  - the SHA-256 of the zip (GitHub's digest, or the asset "&lt;zip&gt;.sha256") must match before anything is extracted; no digest = nothing is installed;
    ///  - extraction with SafeZip (no path escapes, size limits) into a STAGING folder; the files of a running plugin are locked, so the staged folder replaces the installed one at the
    ///    next start (ApplyStaged), BEFORE anything is loaded;
    ///  - the replaced folder has a new content hash, so PluginTrust does not know it: the user is asked ONCE to approve the new content (PluginTrustRequest.IsUpdate). Never trusted silently.
    /// Nothing is downloaded or installed without the click of the user.
    /// </summary>
    public static class PluginUpdateService
    {
        public static readonly string[] TrustedOwners = { "gs-applabs", "giorstergiopoulos-design" };
        private const long MaxZipBytes = 150L * 1024 * 1024;
        private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.Compiled);

        /// <summary>"owner/name" with an owner of the project's accounts and a plain repository name.</summary>
        public static bool TryParseRepository(string? repository, out string owner, out string name)
        {
            owner = name = "";
            var parts = (repository ?? "").Trim().Split('/');
            if (parts.Length != 2 || !NamePattern.IsMatch(parts[0]) || !NamePattern.IsMatch(parts[1])) return false;
            if (!TrustedOwners.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) return false;
            owner = parts[0]; name = parts[1];
            return true;
        }

        /// <summary>The asset must be a plain file name (no path), a .zip.</summary>
        public static bool IsValidAssetName(string? asset) =>
            !string.IsNullOrWhiteSpace(asset) && asset!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && asset.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '%' }) < 0 && !asset.Contains("..");

        /// <summary>true ONLY for https://github.com/&lt;a trusted owner&gt;/&lt;this repository&gt;/releases/download/...</summary>
        public static bool IsTrustedDownloadUrl(string? url, string repository)
        {
            if (!TryParseRepository(repository, out _, out var name)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || u.Host != "github.com" || !string.IsNullOrEmpty(u.UserInfo)) return false;
            return TrustedOwners.Any(o => url!.StartsWith($"https://github.com/{o}/{name}/releases/download/", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTrustedPage(string? url, string repository)
        {
            if (!TryParseRepository(repository, out _, out var name)) return false;
            return url != null && TrustedOwners.Any(o => url.StartsWith($"https://github.com/{o}/{name}/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The newer release described by a GitHub "latest release" answer, or null (not newer, prerelease, draft, untrusted page/asset).</summary>
        public static PluginUpdateInfo? Parse(string json, PluginUpdateCandidate plugin)
        {
            var repo = plugin.Source.Repository;
            if (!TryParseRepository(repo, out _, out _) || !IsValidAssetName(plugin.Source.AssetName)) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!UpdateCheckService.TryParseVersion(tag, out var latest)) return null;
            if (!UpdateCheckService.TryParseVersion(plugin.Version, out var current)) current = new Version(0, 0);
            if (latest <= current) return null;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            if (!IsTrustedPage(page, repo)) return null;

            string? zip = null, sum = null, digest = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    var url = a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                    if (!IsTrustedDownloadUrl(url, repo)) continue;
                    if (name == plugin.Source.AssetName)
                    {
                        zip = url;
                        digest = UpdateCheckService.ParseDigest(a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String ? dg.GetString() : null);
                    }
                    else if (name == plugin.Source.AssetName + ".sha256") sum = url;
                }
            if (zip is null) return null;
            return new PluginUpdateInfo(plugin, latest, tag, page, zip, digest, sum);
        }

        private static HttpClient NewClient(HttpMessageHandler? handler, TimeSpan timeout)
        {
            var c = handler is null ? new HttpClient { Timeout = timeout } : new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Opsis-PluginUpdateCheck");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Asks GitHub about the latest release of one plugin. Returns (succeeded, update or null).</summary>
        public static async Task<(bool Succeeded, PluginUpdateInfo? Update)> CheckAsync(PluginUpdateCandidate plugin, CancellationToken ct = default, HttpMessageHandler? handler = null)
        {
            if (!TryParseRepository(plugin.Source.Repository, out var owner, out var name)) return (false, null);
            try
            {
                using var http = NewClient(handler, TimeSpan.FromSeconds(15));
                http.MaxResponseContentBufferSize = 2 * 1024 * 1024;
                var json = await http.GetStringAsync($"https://api.github.com/repos/{owner}/{name}/releases/latest", ct);
                return (true, Parse(json, plugin));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) { return (false, null); }
            catch (Exception) { return (true, null); }
        }

        /// <summary>Checks every plugin of the list; a plugin that cannot be checked is simply left out.</summary>
        public static async Task<IReadOnlyList<PluginUpdateInfo>> CheckAllAsync(IEnumerable<PluginUpdateCandidate> plugins, CancellationToken ct = default, HttpMessageHandler? handler = null)
        {
            var found = new List<PluginUpdateInfo>();
            foreach (var p in plugins)
            {
                var (_, update) = await CheckAsync(p, ct, handler);
                if (update != null) found.Add(update);
            }
            return found;
        }

        // ---- staging ---------------------------------------------------------------------------------------------------

        public static string StagingRoot(string? pluginsRoot = null) =>
            Path.Combine(Path.GetDirectoryName((pluginsRoot ?? Path.Combine(AppPaths.AppDataFolder, "plugins")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!, "plugin-updates");

        private static bool IsSafeFolderName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        /// <summary>
        /// Downloads the zip, verifies its SHA-256, extracts it safely into the staging folder of the plugin (replacing an older staged copy) and checks that it holds a plugin DLL whose contract
        /// this Opsis can load. Throws InvalidOperationException ("untrusted-url", "no-digest", "invalid-package"), InvalidDataException ("hash-mismatch", "too-large", unsafe zip).
        /// </summary>
        public static async Task<string> DownloadAndStageAsync(PluginUpdateInfo update, string stagingRoot, IProgress<double>? progress = null, CancellationToken ct = default, HttpMessageHandler? handler = null)
        {
            if (!IsTrustedDownloadUrl(update.ZipUrl, update.Plugin.Source.Repository)) throw new InvalidOperationException("untrusted-url");
            if (!IsSafeFolderName(update.Plugin.FolderName)) throw new InvalidOperationException("invalid-package");
            using var http = NewClient(handler, TimeSpan.FromMinutes(10));

            var expected = update.Sha256;
            if (expected is null && update.ChecksumUrl is not null && IsTrustedDownloadUrl(update.ChecksumUrl, update.Plugin.Source.Repository))
            {
                try
                {
                    var text = await http.GetStringAsync(update.ChecksumUrl, ct);
                    var first = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                    if (first.Length == 64 && first.All(Uri.IsHexDigit)) expected = first;
                }
                catch (HttpRequestException) { /* no usable checksum asset: handled as "no-digest" below */ }
            }
            if (expected is null) throw new InvalidOperationException("no-digest");

            Directory.CreateDirectory(stagingRoot);
            var zipPath = Path.Combine(stagingRoot, update.Plugin.FolderName + ".download.zip");
            var tmp = Path.Combine(stagingRoot, update.Plugin.FolderName + ".tmp");
            try
            {
                using var response = await http.GetAsync(update.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                if (total > MaxZipBytes) throw new InvalidDataException("too-large");
                using var sha = SHA256.Create();
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long read = 0; int n;
                    while ((n = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        read += n;
                        if (read > MaxZipBytes) throw new InvalidDataException("too-large");
                        await target.WriteAsync(buffer.AsMemory(0, n), ct);
                        sha.TransformBlock(buffer, 0, n, null, 0);
                        if (total > 0) progress?.Report(Math.Min(1.0, read / (double)total));
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                }
                if (Convert.ToHexString(sha.Hash!).ToLowerInvariant() != expected) throw new InvalidDataException("hash-mismatch");

                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                SafeZip.Extract(zipPath, tmp, maxEntries: 2000, maxTotalBytes: 600L * 1024 * 1024, maxEntryBytes: 300L * 1024 * 1024);
                var dlls = Directory.GetFiles(tmp, "*Plugin.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0) throw new InvalidOperationException("invalid-package");
                foreach (var dll in dlls)
                    if (!PluginContractCheck.Check(dll, PluginHost.ContractVersion).Ok) throw new InvalidOperationException("invalid-package");

                var staged = Path.Combine(stagingRoot, update.Plugin.FolderName);
                if (Directory.Exists(staged)) Directory.Delete(staged, true);
                Directory.Move(tmp, staged);
                return staged;
            }
            catch
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { }
                throw;
            }
            finally { try { File.Delete(zipPath); } catch (Exception) { } }
        }

        /// <summary>
        /// At start-up, before any plugin is loaded: every staged folder replaces the installed one of the same name in <paramref name="pluginsRoot"/>. A folder that cannot be replaced now
        /// (something still holds a file) stays staged for the next start. Returns the names that were put in place.
        /// </summary>
        public static IReadOnlyList<string> ApplyStaged(string pluginsRoot, Action<string>? log = null)
        {
            var applied = new List<string>();
            var root = StagingRoot(pluginsRoot);
            if (!Directory.Exists(root)) return applied;
            foreach (var staged in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(staged);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || !IsSafeFolderName(name)) continue;
                var target = Path.Combine(pluginsRoot, name);
                try
                {
                    Directory.CreateDirectory(pluginsRoot);
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.Move(staged, target);
                    applied.Add(name);
                    log?.Invoke($"Plugin '{name}': the update downloaded through the Opsis update button was put in place; its new content must be approved before it loads.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke($"Plugin '{name}': the staged update could not be put in place now ({ex.Message}); it stays staged for the next start.");
                }
            }
            return applied;
        }
    }
}

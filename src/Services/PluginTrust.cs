using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services
{
    /// <summary>
    /// Trust model for plugins. A plugin folder under the install directory of Opsis is trusted only when that directory is itself protected
    /// (Program Files: writable by administrators only). Plugin folders anywhere else - %AppData%\Opsis\plugins, registry paths, a per-user install -
    /// are writable by the user (or by any program running as the user), so a dropped DLL would be code execution inside Opsis: such a folder is loaded
    /// only after the user approved its exact content (SHA-256 over every .dll / .deps.json in the folder). The approval is stored in plugin-trust.json;
    /// any change of the content invalidates it.
    /// </summary>
    internal static class PluginTrust
    {
        /// <summary>True when <paramref name="dir"/> is inside Program Files (64/32-bit): only administrators can write there.</summary>
        public static bool IsUnderProtectedInstall(string dir, IEnumerable<string>? protectedRoots = null)
        {
            string full = Normalize(dir);
            var roots = protectedRoots ?? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            foreach (var r in roots)
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                string root = Normalize(r);
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && PackagePaths.IsExistingUnlinkedPath(root, full)) return true;
            }
            return false;
        }

        private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>SHA-256 over relative names and executable/dependency/SDK declarations, in stable order. Not a complete package sandbox or immutable copy.</summary>
        public static string HashFolder(string folder)
        {
            using var total = SHA256.Create();
            var files = PluginFiles(folder)
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || (f.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || f.EndsWith("opsis-permissions.json", StringComparison.OrdinalIgnoreCase) || f.EndsWith("opsis-sdk.json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var sb = new StringBuilder();
            long bytes = 0;
            foreach (var f in files)
            {
                using var sha = SHA256.Create();
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length > 2L * 1024 * 1024 * 1024 - bytes)
                    throw new InvalidDataException("Plugin trust inspection exceeds 2 GiB quota.");
                bytes += fs.Length;
                string h = Convert.ToHexString(sha.ComputeHash(fs));
                sb.Append(Path.GetRelativePath(folder, f).ToLowerInvariant()).Append('|').Append(h).Append('\n');
            }
            return Convert.ToHexString(total.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        // Only direct children of the existing host-managed package roots can be
        // deleted by Opsis. Registry-discovered arbitrary directories are read-only.
        internal static bool IsManagedPackageFolder(string folder, IEnumerable<string>? roots = null)
        {
            try
            {
                if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                string full = Normalize(folder);
                var managed = roots ?? new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "plugins"),
                    Path.Combine(AppPaths.AppDataFolder, "plugins"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis", "plugins")
                };
                if (!managed.Any(root => string.Equals(Path.GetDirectoryName(full), Normalize(root), StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (!Directory.Exists(full)) return false;
                for (string? ancestor = full; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                    if (PackagePaths.IsLink(ancestor)) return false;
                return Directory.EnumerateFiles(full, "*Plugin.dll", SearchOption.TopDirectoryOnly).Any();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { return false; }
        }

        internal static string RemovalFingerprint(string folder)
        {
            // Include all files, unlike the execution trust fingerprint. Changing
            // any data in a pending package invalidates the deletion request.
            using var total = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count = 0;
            long bytes = 0;
            foreach (string file in PluginFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(file);
                if (++count > 10000 || stream.Length > 2L * 1024 * 1024 * 1024 - bytes)
                    throw new InvalidDataException("Package removal exceeds verification quota.");
                bytes += stream.Length;
                total.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file).ToLowerInvariant() + "\\n"));
                total.AppendData(SHA256.HashData(stream));
            }
            return Convert.ToHexString(total.GetHashAndReset());
        }
        internal static bool CanRemovePackage(string folder, string? fingerprint)
        {
            try { return fingerprint is { Length: 64 } && IsManagedPackageFolder(folder) &&
                string.Equals(RemovalFingerprint(folder), fingerprint, StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { return false; }
        }

        internal static IEnumerable<string> PluginFiles(string folder)
        {
            var directories = new Stack<string>();
            directories.Push(folder);
            int inspected = 0;
            while (directories.TryPop(out var directory))
            {
                if (PackagePaths.IsLink(directory)) throw new InvalidDataException("Linked plugin folders are not supported.");
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++inspected > 10000)
                        throw new InvalidDataException("Plugin directory inspection exceeds 10000 entries.");
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Linked plugin files are not supported.");
                    if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
                    else yield return entry;
                }
            }
        }

        public sealed class Entry
        {
            public string Name { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public DateTime ApprovedUtc { get; set; }
            public string[]? GrantedCapabilities { get; set; }
        }

        public sealed class Store
        {
            private readonly string _path;
            private List<Entry> _entries;
            private readonly object _gate = new();

            public Store(string path)
            {
                _path = path;
                try
                {
                    _entries = File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024
                        ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new() : new();
                    _entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Name) ||
                        e.Sha256 is not { Length: 64 } || !e.Sha256.All(Uri.IsHexDigit) ||
                        (e.GrantedCapabilities?.Any(x => !PluginCapabilityManifest.KnownCapability(x)) ?? false));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { _entries = new(); }
            }

            public bool IsApproved(string name, string sha256, IEnumerable<string>? requested = null)
            {
                var required = PluginCapabilityManifest.NormalizeCapabilities(requested);
                lock (_gate)
                    return _entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
                        PluginCapabilityManifest.NormalizeCapabilities(e.GrantedCapabilities).SequenceEqual(required));
            }

            private void Save(List<Entry> next)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
                _entries = next; // publish in-memory only after durable write returns
            }

            public void Revoke(string name)
            {
                lock (_gate) Save(_entries.Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList());
            }

            public void Approve(string name, string sha256, IEnumerable<string>? granted = null)
            {
                var caps = PluginCapabilityManifest.NormalizeCapabilities(granted);
                if (caps.Any(x => !PluginCapabilityManifest.KnownCapability(x)))
                    throw new InvalidDataException("Unsupported plugin capability.");
                lock (_gate)
                {
                    var next = _entries.Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                    next.Add(new Entry { Name = name, Sha256 = sha256.ToUpperInvariant(), ApprovedUtc = DateTime.UtcNow, GrantedCapabilities = caps });
                    Save(next);
                }
            }
        }
    }

    public sealed record PluginTrustRequest(string Name, string Folder, string Sha256,
        string Publisher = "Unknown / Άγνωστος", string SignatureStatus = "Unknown / Άγνωστη", string Capabilities = "Not declared / Δεν δηλώθηκαν", IReadOnlyList<string>? RequestedCapabilities = null);

    /// <summary>
    /// Inspect both publisher certificate METADATA and Windows Authenticode
    /// verification. An embedded certificate alone is not proof of trust.
    /// Offline verification avoids unexpected network traffic in the approval UI;
    /// certificate revocation is therefore not verified and is reported as such.
    /// </summary>
    internal static class PluginPublisherInfo
    {
        internal static (string Publisher, string SignatureStatus) Inspect(string folder)
        {
            try
            {
                var entries = Directory.EnumerateFiles(folder, "*Plugin.dll", SearchOption.TopDirectoryOnly)
                    .Where(path => PackagePaths.IsExistingUnlinkedPath(folder, path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(17).ToArray();
                if (entries.Length == 0) return ("Unknown", "No unlinked plugin entry DLL found");
                var publishers = new List<string>();
                var signatures = new List<string>();
                foreach (string dll in entries.Take(16))
                {
                    string publisher = "Unknown / Άγνωστος";
                    try
                    {
                        using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(dll);
                        publisher = cert.Subject;
                    }
                    catch (System.Security.Cryptography.CryptographicException)
                    {
                        // An unsigned DLL has no embedded publisher certificate.
                    }
                    string name = Path.GetFileName(dll);
                    publishers.Add(name + ": " + publisher);
                    signatures.Add(name + ": " + AuthenticodeVerifier.VerifyOffline(dll));
                }
                if (entries.Length > 16)
                {
                    publishers.Add("Additional DLLs not inspected / Επιπλέον DLL χωρίς έλεγχο");
                    signatures.Add("Incomplete inspection / Μη πλήρης έλεγχος");
                }
                return (string.Join(Environment.NewLine, publishers), string.Join(Environment.NewLine, signatures));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { return ("Unknown", "Could not inspect: " + ex.GetType().Name); }
        }
    }
}

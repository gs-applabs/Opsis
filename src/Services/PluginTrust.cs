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

        /// <summary>SHA-256 (hex) over the relative names and contents of all .dll and .deps.json files below the folder, in a stable order.</summary>
        public static string HashFolder(string folder)
        {
            using var total = SHA256.Create();
            var files = PluginFiles(folder)
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || (f.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || f.EndsWith("opsis-permissions.json", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                using var sha = SHA256.Create();
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                string h = Convert.ToHexString(sha.ComputeHash(fs));
                sb.Append(Path.GetRelativePath(folder, f).ToLowerInvariant()).Append('|').Append(h).Append('\n');
            }
            return Convert.ToHexString(total.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        private static IEnumerable<string> PluginFiles(string folder)
        {
            var directories = new Stack<string>();
            directories.Push(folder);
            while (directories.TryPop(out var directory))
            {
                if (PackagePaths.IsLink(directory)) throw new InvalidDataException("Linked plugin folders are not supported.");
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
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
        }

        public sealed class Store
        {
            private readonly string _path;
            private readonly List<Entry> _entries;

            public Store(string path)
            {
                _path = path;
                try { _entries = File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new() : new(); }
                catch (Exception ex) when (ex is JsonException or IOException) { _entries = new(); }
            }

            public bool IsApproved(string name, string sha256) =>
                _entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

            public void Revoke(string name)
            {
                _entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_entries));
            }

            public void Approve(string name, string sha256)
            {
                _entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));      // one approved content per plugin name
                _entries.Add(new Entry { Name = name, Sha256 = sha256.ToUpperInvariant(), ApprovedUtc = DateTime.UtcNow });
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    public sealed record PluginTrustRequest(string Name, string Folder, string Sha256,
        string Publisher = "Unknown / Άγνωστος", string SignatureStatus = "Unknown / Άγνωστη", string Capabilities = "Not declared / Δεν δηλώθηκαν");

    internal static class PluginPublisherInfo
    {
        internal static (string Publisher, string SignatureStatus) Inspect(string folder)
        {
            try
            {
                var dll = Directory.EnumerateFiles(folder, "*Plugin.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
                if (dll == null) return ("Unknown", "No plugin entry DLL found");
                try
                {
                    using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(dll);
                    return (certificate.Subject, "Embedded certificate found; signature trust NOT verified");
                }
                catch (System.Security.Cryptography.CryptographicException)
                { return ("Unsigned / Χωρίς υπογραφή", "No embedded Authenticode certificate"); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { return ("Unknown", "Could not inspect: " + ex.GetType().Name); }
        }
    }
}

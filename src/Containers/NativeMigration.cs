using System.Security.Cryptography;
using System.Text.Json;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>Read-only legacy import. Prepare a durable immutable plan before
    /// publishing any snapshot, and commit the live layout last. Never replace
    /// an existing native layout or an unrelated snapshot.</summary>
    internal static class NativeMigration
    {
        private const string Owner = "OPSIS-NATIVE-MIGRATION-V1";
        private const string OwnerFile = "owner.txt";
        private const string ManifestFile = "manifest.json";
        private const int Version = 1;
        private sealed record SnapshotEntry(string Name, string Sha256);
        private sealed record Manifest(int Version, string LiveSha256, SnapshotEntry[] Snapshots, string Summary);

        public static string? ImportIfNeeded(ContainersStore store, string legacyRoot, IReadOnlyList<MonitorInfo> monitors)
        {
            string live = Path.GetFullPath(store.Path);
            string parent = Path.GetDirectoryName(live)!;
            string stage = Path.Combine(parent, "container-migration-v1");
            string manifestPath = Path.Combine(stage, ManifestFile);
            string snapDir = Path.Combine(parent, "container-snapshots");
            if (File.Exists(live)) return null; // native layout always wins
            RejectReparsePath(parent);
            RejectReparsePath(stage);
            RejectReparsePath(snapDir);
            RejectReparsePath(manifestPath);
            if (!File.Exists(manifestPath) && !Directory.Exists(Path.Combine(legacyRoot, "Profiles"))) return null;

            // A second process may see an incomplete import. Do not let it
            // prepare or publish concurrently. No persistent lock handle.
            Directory.CreateDirectory(parent);
            string lockPath = Path.Combine(parent, "container-migration-v1.lock");
            RejectReparsePath(lockPath);
            using var migrationLock = new FileStream(lockPath,
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(live)) return null;
            Manifest plan;
            if (File.Exists(manifestPath))
            {
                RequireOwnedStage(stage);
                plan = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                    ?? throw new IOException("Missing native migration manifest.");
            }
            else
            {
                // Incomplete preparation has never published anything. Only
                // discard our recognized staging directory, not legacy data.
                if (Directory.Exists(stage))
                {
                    if (Directory.EnumerateFileSystemEntries(stage).Any()) RequireOwnedStage(stage);
                    DeleteOwnedStage(stage);
                }
                Directory.CreateDirectory(stage);
                WriteDurable(Path.Combine(stage, OwnerFile), System.Text.Encoding.UTF8.GetBytes(Owner));
                var (docs, report) = LegacyImporter.Import(legacyRoot, monitors);
                if (docs.Count == 0)
                {
                    DeleteOwnedStage(stage);
                    return "legacy import: nothing importable (" + string.Join("; ", report.Warnings) + ")";
                }
                var entries = new List<SnapshotEntry>();
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(snapDir))
                    foreach (var file in Directory.EnumerateFileSystemEntries(snapDir))
                        reserved.Add(Path.GetFileName(file));
                for (int i = 1; i < docs.Count; i++)
                {
                    // Let the existing snapshot serializer choose the actual
                    // filename, including sanitization. Try distinct names
                    // before publication, not by overwriting a user's file.
                    string scratch = Path.Combine(stage, "scratch");
                    bool saved = false;
                    for (int attempt = 0; attempt < 64; attempt++)
                    {
                        Directory.CreateDirectory(scratch);
                        string name = attempt == 0 ? docs[i].Profile : docs[i].Profile + $" (Legacy {i}-{attempt})";
                        ContainerSnapshots.Save(docs[i], scratch, name);
                        var files = Directory.GetFiles(scratch, "*", SearchOption.TopDirectoryOnly);
                        if (files.Length != 1 || !ValidSnapshotName(Path.GetFileName(files[0])))
                            throw new IOException("Unexpected legacy snapshot serializer output; original data retained.");
                        string fileName = Path.GetFileName(files[0]);
                        if (reserved.Add(fileName))
                        {
                            string payload = Payload(stage, entries.Count);
                            File.Move(files[0], payload, overwrite: false);
                            entries.Add(new SnapshotEntry(fileName, Hash(payload)));
                            saved = true;
                        }
                        DeleteOwnedStage(scratch);
                        if (saved) break;
                    }
                    if (!saved) throw new IOException("Could not allocate a unique legacy snapshot name.");
                }
                string stagedLive = Path.Combine(stage, "live.json");
                new ContainersStore(stagedLive).Save(docs[0]);
                plan = new Manifest(Version, Hash(stagedLive), entries.ToArray(),
                    $"legacy import: {report.Containers} containers, {report.Items} items, {report.Profiles} profile(s) ({entries.Count} as snapshots), {report.NotImported.Count} not carried");
                // Manifest is the commit point for the preparation stage.
                // No snapshot is visible before this durable write completes.
                WriteDurable(manifestPath, JsonSerializer.SerializeToUtf8Bytes(plan));
            }

            ValidatePlan(stage, snapDir, plan);
            Directory.CreateDirectory(snapDir);
            for (int i = 0; i < plan.Snapshots.Length; i++)
            {
                var entry = plan.Snapshots[i];
                PublishStagedFile(Payload(stage, i), Path.Combine(snapDir, entry.Name),
                    entry.Sha256, Path.Combine(stage, "publish.tmp"));
            }
            PublishStagedFile(Path.Combine(stage, "live.json"), live, plan.LiveSha256, Path.Combine(stage, "publish.tmp"));
            try { DeleteOwnedStage(stage); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Migration cleanup: " + ex.Message); }
            return plan.Summary;
        }

        private static string Payload(string stage, int index) => Path.Combine(stage, $"snapshot-{index:D6}.json");
        internal static bool ValidSnapshotName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) &&
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        internal static string Hash(string path)
        {
            RejectReparsePath(path);
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void ValidatePlan(string stage, string snapDir, Manifest plan)
        {
            RejectReparsePath(stage);
            RejectReparsePath(snapDir);
            if (plan.Version != Version || plan.Snapshots == null || plan.Snapshots.Length > 10000 ||
                !string.Equals(Hash(Path.Combine(stage, "live.json")), plan.LiveSha256, StringComparison.Ordinal))
                throw new IOException("Invalid or damaged native migration plan.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plan.Snapshots.Length; i++)
            {
                var e = plan.Snapshots[i];
                if (e == null || !ValidSnapshotName(e.Name) || !names.Add(e.Name) ||
                    !string.Equals(Hash(Payload(stage, i)), e.Sha256, StringComparison.Ordinal))
                    throw new IOException("Invalid native migration snapshot.");
                string target = Path.Combine(snapDir, e.Name);
                RejectReparsePath(target);
                if (File.Exists(target) && !string.Equals(Hash(target), e.Sha256, StringComparison.Ordinal))
                    throw new IOException("A snapshot changed during migration: " + e.Name + ". Existing files preserved.");
                if (Directory.Exists(target)) throw new IOException("Snapshot destination is a directory: " + e.Name);
            }
        }

        // All bytes are verified before any rename. An interrupted or repeated
        // publication is a no-op if the exact content already exists.
        internal static void PublishStagedFile(string source, string destination, string expectedHash, string temp)
        {
            RejectReparsePath(destination);
            RejectReparsePath(temp);
            if (!string.Equals(Hash(source), expectedHash, StringComparison.Ordinal))
                throw new IOException("Migration payload failed integrity check.");
            if (File.Exists(destination))
            {
                if (string.Equals(Hash(destination), expectedHash, StringComparison.Ordinal)) return;
                throw new IOException("Migration destination already contains different data: " + Path.GetFileName(destination));
            }
            try
            {
                // Remove only the previous staging entry; never truncate through a link.
                if (File.Exists(temp)) File.Delete(temp);
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                if (!string.Equals(Hash(temp), expectedHash, StringComparison.Ordinal))
                    throw new IOException("Migration staging copy failed integrity check.");
                File.Move(temp, destination, overwrite: false);
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            RejectReparsePath(path);
            RejectReparsePath(tmp);
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(tmp, path, overwrite: false);
        }

        private static void RequireOwnedStage(string stage)
        {
            RejectReparsePath(Path.Combine(stage, OwnerFile));
            if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(Path.Combine(stage, OwnerFile)) ||
                File.ReadAllText(Path.Combine(stage, OwnerFile)) != Owner)
                throw new IOException("Unrecognized native migration staging folder; no data removed.");
        }

        internal static void RejectReparsePath(string path)
        {
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Migration path contains a reparse point; original data retained: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static void ValidateCleanupTree(string directory)
        {
            RejectReparsePath(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Migration staging entry is a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) ValidateCleanupTree(entry);
            }
        }

        private static void DeleteOwnedStage(string directory)
        {
            // Preflight the entire tree before deleting any entry; keep the owner
            // marker until the end so a partial cleanup remains recognizable.
            ValidateCleanupTree(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (Path.GetFileName(entry).Equals(OwnerFile, StringComparison.OrdinalIgnoreCase)) continue;
                RejectReparsePath(entry);
                if (Directory.Exists(entry)) DeleteOwnedStage(entry);
                else File.Delete(entry);
            }
            string marker = Path.Combine(directory, OwnerFile);
            RejectReparsePath(marker);
            if (File.Exists(marker)) File.Delete(marker);
            Directory.Delete(directory);
        }
    }
}

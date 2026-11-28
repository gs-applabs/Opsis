using System.Reflection;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>One explicit, local user-owned plugin package transaction. No installer
/// execution, download, trust grant, or change to a loaded DLL. Journal authority is
/// the primary file only. Renames occur before discovery on a later process start.</summary>
internal static class PluginPackageMaintenance
{
    internal sealed record Plan(string Name, string OldHash, string NewHash, bool Pending, bool Reverse);
    private static readonly object Gate = new();
    private static bool _scheduledHere;
    private static string Plugins => Path.Combine(AppPaths.AppDataFolder, "plugins");
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "plugin-maintenance");
    private static string Journal => Path.Combine(Root, "transaction.json");
    private static string Incoming => Path.Combine(Root, "incoming");
    private static string Previous => Path.Combine(Root, "previous");

    internal static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 100 && name != "." && name != ".." && !name.EndsWith('.') && !name.EndsWith(' ') &&
        name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.');

    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        for (string? ancestor = Path.GetFullPath(path); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            if (PackagePaths.IsLink(ancestor)) throw new InvalidDataException("Linked package paths are not supported.");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new InvalidDataException("Use a local fixed drive.");
    }

    private static void CheckWritable()
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold)
            throw new InvalidOperationException("Compatibility review required.");
    }

    private static string Target(string name)
    {
        if (!ValidName(name)) throw new InvalidDataException("Invalid package name.");
        return Path.Combine(Plugins, name);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Matches(string path, string hash)
    {
        if (!Directory.Exists(path)) return false;
        RequireDirectory(path);
        return Same(PluginTrust.RemovalFingerprint(path), hash);
    }
    private static bool Missing(string path) => !Directory.Exists(path) && !File.Exists(path);
    private static string[] Entries(string folder)
    {
        RequireDirectory(folder);
        var files = Directory.EnumerateFiles(folder, "*Plugin.dll").Take(81).ToArray();
        if (files.Length is < 1 or > 80) throw new InvalidDataException("Invalid package entrypoint count.");
        foreach (var file in files)
        {
            if (!PackagePaths.IsExistingUnlinkedPath(folder, file) || new FileInfo(file).Length > 100L * 1024 * 1024)
                throw new InvalidDataException("Unsafe package entrypoint.");
            var meta = AssemblyName.GetAssemblyName(file);
            if (OpsisPluginPackageAudit.Classify(Path.GetFileName(file), meta.Version) == "IncompatibleABI")
                throw new InvalidDataException("Incompatible package ABI: " + Path.GetFileName(file));
        }
        return files.Select(Path.GetFileName).Select(x => x!).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static Plan? ReadPlan()
    {
        if (!File.Exists(Journal)) return null;
        RequireDirectory(Root);
        if (!PackagePaths.IsExistingUnlinkedPath(Root, Journal) || new FileInfo(Journal).Length > 4096)
            throw new InvalidDataException("Unsafe package maintenance journal.");
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(Journal));
        if (plan == null || !ValidName(plan.Name) || !VerifiedRollbackService.ValidDigest(plan.OldHash) ||
            !VerifiedRollbackService.ValidDigest(plan.NewHash) || Same(plan.OldHash, plan.NewHash))
            throw new InvalidDataException("Invalid package maintenance journal.");
        return plan;
    }
    internal static Plan? Status() { lock (Gate) return ReadPlan(); }

    private static void CheckRemoval(string target)
    {
        if (AppSettings.Peek().PendingPluginRemovals.Any(p => Same(p, target)))
            throw new InvalidOperationException("Package has a pending removal.");
    }

    internal static bool OwnsTransaction(string folder)
    {
        lock (Gate)
        {
            var plan = ReadPlan();
            return plan != null && Same(PluginHost.PackageIdentity(folder), Target(plan.Name));
        }
    }

    internal static void Stage(string targetFolder, string sourceFolder)
    {
        lock (Gate)
        {
            CheckWritable();
            var target = PluginHost.PackageIdentity(targetFolder);
            var source = PluginHost.PackageIdentity(sourceFolder);
            string name = Path.GetFileName(target);
            if (!Same(target, Target(name)) || !PluginTrust.IsManagedPackageFolder(target, new[] { Plugins }))
                throw new InvalidOperationException("Only direct packages in the current Opsis user plugins folder are supported.");
            RequireDirectory(target);
            RequireDirectory(source);
            CheckRemoval(target);
            if (Same(source, target) || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Same(source, Root) || source.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Root.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a separate extracted package folder.");
            if (ReadPlan() != null || !Missing(Incoming) || !Missing(Previous))
                throw new InvalidOperationException("A previous transaction or archive must be resolved first.");
            if (!Entries(target).SequenceEqual(Entries(source), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Replacement must contain the same entrypoint filenames.");
            if (PluginHost.IsReplacedByBuiltIn(source)) throw new InvalidDataException("Package superseded by built-in Opsis.");
            PluginCapabilityManifest.RequireCompatibleApi(source);
            _ = PluginCapabilityManifest.Read(source);
            string oldHash = PluginTrust.RemovalFingerprint(target);
            string newHash = PluginTrust.RemovalFingerprint(source);
            if (Same(oldHash, newHash)) throw new InvalidOperationException("Package contents are unchanged.");
            Directory.CreateDirectory(Root);
            RequireDirectory(Root);
            Directory.CreateDirectory(Incoming);
            // Copy only enumerated unlinked files, with a second total quota and
            // readback. A failed/interrupted stage is never discovered as a plugin.
            long total = 0;
            foreach (var file in PluginTrust.PluginFiles(source))
            {
                if (!PackagePaths.IsExistingUnlinkedPath(source, file)) throw new InvalidDataException("Linked source file.");
                string dest = Path.Combine(Incoming, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                long remaining = input.Length;
                if (remaining > 2L * 1024 * 1024 * 1024 - total) throw new InvalidDataException("Package exceeds copy quota.");
                total += remaining;
                var buffer = new byte[65536];
                while (remaining > 0)
                {
                    int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (n == 0) throw new IOException("Package source changed during copy.");
                    output.Write(buffer, 0, n); remaining -= n;
                }
                if (input.ReadByte() != -1) throw new IOException("Package source grew during copy.");
                output.Flush(flushToDisk: true);
            }
            if (!Matches(Incoming, newHash) || !Matches(source, newHash) || !Matches(target, oldHash))
                throw new InvalidDataException("Package changed during staging.");
            CheckWritable();
            CheckRemoval(target);
            AtomicFile.WriteAllText(Journal, JsonSerializer.Serialize(new Plan(name, oldHash, newHash, true, false)));
            _scheduledHere = true;
        }
    }

    internal static void ScheduleRollback()
    {
        lock (Gate)
        {
            CheckWritable();
            var p = ReadPlan() ?? throw new InvalidOperationException("No verified previous package.");
            if (p.Pending) throw new InvalidOperationException("Restart to finish the pending operation first.");
            var live = Target(p.Name);
            CheckRemoval(live);
            string archive = p.Reverse ? Incoming : Previous;
            if (!Matches(live, p.NewHash) || !Matches(archive, p.OldHash))
                throw new InvalidDataException("Live or archived package changed; rollback refused.");
            AtomicFile.WriteAllText(Journal, JsonSerializer.Serialize(new Plan(p.Name, p.NewHash, p.OldHash, true, !p.Reverse)));
            _scheduledHere = true;
        }
    }

    // Repeatable recovery after interruption between the two directory renames
    // or after publication but before journal completion. Never guess/delete.
    internal static void ApplyPending()
    {
        lock (Gate)
        {
            if (_scheduledHere) return;
            var p = ReadPlan();
            if (p == null || !p.Pending) return;
            CheckWritable();
            var live = Target(p.Name);
            CheckRemoval(live);
            RequireDirectory(Plugins);
            RequireDirectory(Root);
            string next = p.Reverse ? Previous : Incoming;
            string archive = p.Reverse ? Incoming : Previous;
            if (Matches(live, p.OldHash) && Matches(next, p.NewHash) && Missing(archive))
            {
                Directory.Move(live, archive);
            }
            if (Missing(live) && Matches(archive, p.OldHash) && Matches(next, p.NewHash))
            {
                Directory.Move(next, live);
            }
            if (!Matches(live, p.NewHash) || !Matches(archive, p.OldHash) || !Missing(next))
                throw new InvalidDataException("Unverified package transaction; no further changes made.");
            AtomicFile.WriteAllText(Journal, JsonSerializer.Serialize(p with { Pending = false }));
            // New content enters the normal user-writable SHA-bound trust path.
        }
    }

    internal static void ForgetArchive()
    {
        lock (Gate)
        {
            CheckWritable();
            var p = ReadPlan() ?? throw new InvalidOperationException("No verified archive.");
            if (p.Pending) throw new InvalidOperationException("Pending transaction cannot be cleared.");
            if (!Matches(Target(p.Name), p.NewHash)) throw new InvalidDataException("Live package changed.");
            string archive = p.Reverse ? Incoming : Previous;
            string empty = p.Reverse ? Previous : Incoming;
            if (!Missing(empty)) throw new InvalidDataException("Unexpected maintenance content.");
            if (!Missing(archive))
            {
                if (!Matches(archive, p.OldHash)) throw new InvalidDataException("Archive changed; deletion refused.");
                Directory.Delete(archive, recursive: true);
            }
            File.Delete(Journal); // no fallback to stale .bak
        }
    }
}

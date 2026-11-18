using System.Security.Principal;

namespace Opsis.Lockscreen;

/// <summary>
/// The three things the elevated helper can do: apply the staged GIF to the lock-screen cache, disable the Windows feature override, and restore everything.
/// Nothing is configurable from outside: the staged GIF, the backup folder and the cache root are derived from the data folder and the user SID.
/// Every change is preceded by a backup (files + ACLs + feature override); the ACLs are put back after every operation.
/// The cache layout and the icacls / takeown approach are adapted from LockscreenGif (MIT, (c) 2026 Leapward-Koex).
/// </summary>
public sealed class LockscreenOperations
{
    private const string AdminsSid = "S-1-5-32-544";
    private readonly string _data, _bk, _root, _sid;
    private readonly IAclTool _acl; private readonly IFeatureStore _feature; private readonly Action<string> _log;
    private readonly List<string> _details = new();

    public LockscreenOperations(string dataRoot, string backupRoot, string cacheRoot, string sid, IAclTool acl, IFeatureStore feature, Action<string>? log = null)
    {
        _data = Path.GetFullPath(dataRoot); _bk = Path.GetFullPath(backupRoot); _root = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar);
        _sid = new SecurityIdentifier(sid).Value; _acl = acl; _feature = feature; _log = log ?? (_ => { });
    }

    private void Log(string s) { _details.Add(s); _log(s); }
    private string BackupFiles => Path.Combine(_bk, "files");
    private string BackupAcl => Path.Combine(_bk, "acl");

    private void CheckDataFolder()
    {
        // the staged GIF comes from the user's data folder (read only): refuse links there; the backup folder is admin-only and must not be a link either
        string staging = Path.GetDirectoryName(LockscreenPaths.StagingGif(_data))!;
        if (Directory.Exists(staging)) LockscreenPaths.RejectLinks(staging, _data);
        if (Directory.Exists(_bk)) LockscreenPaths.RejectLinks(_bk, _bk);
        foreach (var d in new[] { BackupFiles, BackupAcl }) if (Directory.Exists(d)) LockscreenPaths.RejectLinks(d, _bk);
    }

    /// <summary>A name read from the manifest must be a plain file name.</summary>
    private static string Plain(string name) => name.Length > 0 && name == Path.GetFileName(name) && name != ".." ? name : throw new UnauthorizedAccessException("Bad name in the backup manifest");

    private BackupManifest NewManifest() => new() { Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), CreatedUtc = DateTime.UtcNow, Sid = _sid, CacheRoot = _root };

    // ---- access: save the original ACL once, then grant the administrators what the operation needs ------------------------------------------------
    private void Acquire(BackupManifest m, string path, bool isDir, bool write)
    {
        LockscreenPaths.ValidateCachePath(_root, path, write);
        string rel = Path.GetRelativePath(_root, path); if (rel == ".") rel = "";
        var entry = m.Acls.FirstOrDefault(a => a.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            Directory.CreateDirectory(BackupAcl);
            entry = new AclEntry { Rel = rel, SaveFile = "acl-" + m.Acls.Count + ".txt" };
            string save = Path.Combine(BackupAcl, entry.SaveFile);
            if (_acl.Run("icacls.exe", new[] { path, "/save", save, "/c", "/q" }) != 0 || !File.Exists(save))
            {
                // the ACL could not even be read: take ownership (administrators) and read it again; the owner is put back to SYSTEM on restore
                Log("Taking ownership to read the ACL of " + rel);
                if (_acl.Run("takeown.exe", new[] { "/f", path, "/a" }) != 0) throw new UnauthorizedAccessException("Cannot read or take the ACL of " + rel);
                entry.OwnerChanged = true;
                if (_acl.Run("icacls.exe", new[] { path, "/save", save, "/c", "/q" }) != 0 || !File.Exists(save)) throw new UnauthorizedAccessException("Cannot save the ACL of " + rel);
            }
            m.Acls.Add(entry); m.Save(_bk);                          // recorded BEFORE the grant, so a crash can still be undone
        }
        string rights = isDir ? "(RX)" : "(M)";
        if (_acl.Run("icacls.exe", new[] { path, "/grant", "*" + AdminsSid + ":" + rights, "/c", "/q" }) != 0)
        {
            if (_acl.Run("takeown.exe", new[] { "/f", path, "/a" }) != 0) throw new UnauthorizedAccessException("Cannot get access to " + rel);
            entry.OwnerChanged = true; m.Save(_bk);
            if (_acl.Run("icacls.exe", new[] { path, "/grant", "*" + AdminsSid + ":" + rights, "/c", "/q" }) != 0) throw new UnauthorizedAccessException("Cannot get access to " + rel);
        }
    }

    private void RevertAcls(BackupManifest m)
    {
        for (int i = m.Acls.Count - 1; i >= 0; i--)
        {
            var a = m.Acls[i];
            string path = a.Rel.Length == 0 ? _root : Path.Combine(_root, a.Rel); LockscreenPaths.ValidateCachePath(_root, path, false);
            string save = Path.Combine(BackupAcl, Plain(a.SaveFile));
            if (!File.Exists(save)) { Log("ACL backup missing for " + a.Rel); continue; }
            string parent = Path.GetDirectoryName(path)!;
            // /restore applies the stored descriptor to the (relative) names inside the parent folder
            int rc = _acl.Run("icacls.exe", new[] { parent, "/restore", save, "/c", "/q" });
            if (rc == 0 && a.OwnerChanged) rc = _acl.Run("icacls.exe", new[] { path, "/setowner", "*S-1-5-18", "/c", "/q" });   // NT AUTHORITY\SYSTEM
            Log("ACL " + (rc == 0 ? "restored" : "restore FAILED (exit " + rc + ")") + ": " + (a.Rel.Length == 0 ? "<root>" : a.Rel));
        }
    }

    // ---- discovery -----------------------------------------------------------------------------------------------------------------------------
    private List<string> Discover(BackupManifest m)
    {
        var list = new List<string>();
        Acquire(m, _root, true, false);
        foreach (var dir in Directory.GetDirectories(_root).Where(d => Path.GetFileName(d).StartsWith("LockScreen", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            Acquire(m, dir, true, false);
            foreach (var f in Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string n = Path.GetFileName(f);
                if (n.Equals(LockscreenPaths.MainImageName, StringComparison.OrdinalIgnoreCase) || n.EndsWith(LockscreenPaths.VariantSuffix, StringComparison.OrdinalIgnoreCase)) list.Add(f);
            }
        }
        return list;
    }

    // ---- apply ---------------------------------------------------------------------------------------------------------------------------------
    public OpResult Apply()
    {
        _details.Clear();
        BackupManifest? m = null;
        try
        {
            CheckDataFolder();
            string gif = LockscreenPaths.StagingGif(_data);
            if (!File.Exists(gif)) return OpResult.Fail("NoSource", "The staged GIF was not found.");
            var info = new FileInfo(gif);
            if (info.Length is 0 or > LockscreenPaths.MaxGifBytes) return OpResult.Fail("BadSource", "The GIF is empty or too large.");
            byte[] data = File.ReadAllBytes(gif);
            if (!LockscreenPaths.IsGif(data)) return OpResult.Fail("BadSource", "The staged file is not a GIF.");
            if (!Directory.Exists(_root)) return OpResult.Fail("NoCache", "The lock-screen cache folder does not exist. Choose a Picture lock screen in Windows Settings first.");
            string sha = Hashing.Sha256(data);

            m = BackupManifest.Load(_bk) ?? NewManifest();
            m.Save(_bk);
            var targets = Discover(m);
            if (targets.Count == 0) return OpResult.Fail("NoCache", "No lock-screen cache images were found. Choose a Picture lock screen, lock and unlock Windows once, then try again.");

            Directory.CreateDirectory(BackupFiles);
            foreach (var t in targets)                                   // 1) back up every original file BEFORE any write
            {
                Acquire(m, t, false, true);
                string rel = Path.GetRelativePath(_root, t);
                if (m.Files.Any(f => f.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase))) continue;     // the very first original is kept
                string name = "f" + m.Files.Count + ".bak";
                File.Copy(t, Path.Combine(BackupFiles, name), true);
                m.Files.Add(new BackupFile { Rel = rel, BackupName = name, Size = new FileInfo(t).Length, Sha256 = Hashing.Sha256(t) });
                m.Save(_bk);
            }
            m.Applied = true; m.AppliedSha256 = sha; m.Save(_bk);       // from here a crash is recoverable with Restore
            int done = 0;
            foreach (var t in targets)                                   // 2) overwrite in place, verify each file
            {
                WriteInPlace(t, data);
                if (Hashing.Sha256(t) != sha) { Log("Verification failed: " + Path.GetFileName(t)); RestoreFiles(m, force: true); return OpResult.Fail("VerifyFailed", "A cache file did not verify; the original files were put back.", _details.ToList()); }
                done++;
            }
            Log("Applied to " + done + " files");
            return OpResult.Success("Applied", "The GIF was written to " + done + " lock-screen cache files.", _details.ToList());
        }
        catch (Exception ex)
        {
            Log("Apply failed: " + ex.GetType().Name + ": " + ex.Message);
            if (m != null && m.Applied) { try { RestoreFiles(m, force: true); } catch (Exception) { } }
            return OpResult.Fail("Failed", ex.Message, _details.ToList());
        }
        finally { try { if (m != null) RevertAcls(m); } catch (Exception ex) { Log("ACL revert failed: " + ex.Message); } }
    }

    private static void WriteInPlace(string path, byte[] data)
    {
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        fs.SetLength(0); fs.Write(data, 0, data.Length); fs.Flush(true);
    }

    // ---- restore -------------------------------------------------------------------------------------------------------------------------------
    private void RestoreFiles(BackupManifest m, bool force)
    {
        foreach (var f in m.Files)
        {
            string path = Path.Combine(_root, f.Rel); LockscreenPaths.ValidateCachePath(_root, path, true);
            string bak = Path.Combine(BackupFiles, Plain(f.BackupName));
            if (!File.Exists(bak)) { Log("Backup file missing: " + f.Rel); continue; }
            if (!File.Exists(path)) { Log("Cache file no longer exists (Windows rebuilt the cache): " + f.Rel); continue; }
            string cur = Hashing.Sha256(path);
            if (!force && m.AppliedSha256 != null && cur != m.AppliedSha256 && cur != f.Sha256)
            { Log("Left alone (Windows replaced it after the apply): " + f.Rel); continue; }
            Acquire(m, Path.GetDirectoryName(path)!, true, false); Acquire(m, path, false, true);
            WriteInPlace(path, File.ReadAllBytes(bak));
            Log((Hashing.Sha256(path) == f.Sha256 ? "Restored: " : "RESTORE MISMATCH: ") + f.Rel);
        }
    }

    public OpResult Restore()
    {
        _details.Clear();
        BackupManifest? m = null;
        try
        {
            CheckDataFolder();
            m = BackupManifest.Load(_bk);
            if (m == null) return OpResult.Success("NothingToRestore", "There is nothing to restore.");
            bool filesNeeded = m.Files.Count > 0 && Directory.Exists(_root);
            if (filesNeeded) { Acquire(m, _root, true, false); RestoreFiles(m, force: false); }
            if (m.Feature.Captured && m.Feature.Changed) { _feature.Restore(m.Feature); m.Feature.Changed = false; m.Save(_bk); Log("Windows feature override restored"); }
            m.Applied = false; m.Save(_bk);
            RevertAcls(m);
            Directory.Delete(_bk, true);     // the originals are back: the next apply takes a fresh backup
            Log("Backup removed");
            return OpResult.Success("Restored", "The original lock screen, permissions and Windows feature setting were restored.", _details.ToList());
        }
        catch (Exception ex)
        {
            Log("Restore failed: " + ex.GetType().Name + ": " + ex.Message);
            try { if (m != null) RevertAcls(m); } catch (Exception) { }
            return OpResult.Fail("Failed", ex.Message, _details.ToList());
        }
    }

    // ---- Windows feature ----------------------------------------------------------------------------------------------------------------------
    public OpResult DisableFeature()
    {
        _details.Clear();
        try
        {
            CheckDataFolder();
            var m = BackupManifest.Load(_bk) ?? NewManifest();
            if (!m.Feature.Captured) { m.Feature = _feature.Capture(); m.Save(_bk); }          // the ORIGINAL value, once
            _feature.SetDisabled();
            m.Feature.Changed = true; m.Save(_bk);
            Log("Windows feature override set to Disabled");
            return OpResult.Success("FeatureDisabled", "The Windows feature was disabled. A restart may be needed.", _details.ToList());
        }
        catch (Exception ex) { Log("Failed: " + ex.Message); return OpResult.Fail("Failed", ex.Message, _details.ToList()); }
    }
}

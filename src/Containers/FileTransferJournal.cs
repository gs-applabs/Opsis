using System.Security.Cryptography;
using System.Text;

namespace MotionDesk.Containers;

internal sealed class FileTransferJournal
{
    private const int HistoryLimit = 25;
    private const int FingerprintEntryLimit = 10000;
    private const long FingerprintByteLimit = 2L * 1024 * 1024 * 1024;
    private const int FingerprintDepthLimit = 64;
    private readonly List<List<Entry>> _history = [];
    private readonly SemaphoreSlim _operation = new(1, 1);
    private sealed record Entry(string Source, string Target, bool Move, bool Directory, string Stamp);
    internal sealed record Outcome(int Completed, int WithoutUndo, string[] Warnings);
    internal bool CanUndo { get { lock (_history) return _history.Count > 0 && _operation.CurrentCount != 0; } }
    private static Outcome Busy() => new(0, 0, ["A file transfer or undo is already running. / Εκτελείται ήδη μεταφορά ή αναίρεση."]);

    internal async Task<Outcome> TransferAsync(IEnumerable<string> paths, string folder, bool move)
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false)) return Busy();
        try { return await Task.Run(() => Transfer(paths, folder, move)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(0, 0, [ex.Message]); }
        finally { _operation.Release(); }
    }

    internal async Task<Outcome> UndoAsync(Action<string, bool> recycle)
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false)) return Busy();
        try { return await Task.Run(() => Undo(recycle)).ConfigureAwait(false); }
        finally { _operation.Release(); }
    }

    private Outcome Transfer(IEnumerable<string> paths, string folder, bool move)
    {
        var recorded = new List<Entry>();
        var warnings = new List<string>();
        int completed = 0;
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) return new(0, 0, [folder]);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var original = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                string name = Path.GetFileName(original);
                bool directory = Directory.Exists(original);
                if (name.Length == 0 || (!directory && !File.Exists(original))) continue;
                if (SamePath(original, Path.Combine(folder, name)) || directory && IsWithin(original, folder)) continue;
                string target = AvailablePath(folder, name, directory);
                string? before = TryFingerprint(original, directory);
                if (move) Move(original, target, directory);
                else Copy(original, target, directory);

                completed++;
                var stamp = TryFingerprint(target, directory);
                if (stamp != null && before == stamp) recorded.Add(new(original, target, move, directory, stamp));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { warnings.Add(Path.GetFileName(path) + " · " + ex.Message); }
        }
        if (recorded.Count > 0)
        {
            lock (_history)
            {
                _history.Add(recorded);
                if (_history.Count > HistoryLimit) _history.RemoveAt(0);
            }
        }
        return new(completed, completed - recorded.Count, warnings.ToArray());
    }

    private Outcome Undo(Action<string, bool> recycle)
    {
        List<Entry> batch;
        lock (_history) { if (_history.Count == 0) return new(0, 0, []); batch = _history[^1]; }
        var warnings = new List<string>();
        int count = 0;
        foreach (var item in batch.ToArray().Reverse())
        {
            try
            {
                if (TryFingerprint(item.Target, item.Directory) != item.Stamp)
                {
                    warnings.Add(Path.GetFileName(item.Target));
                    continue;
                }
                if (item.Move)
                {
                    if (File.Exists(item.Source) || Directory.Exists(item.Source) ||
                        !Directory.Exists(Path.GetDirectoryName(item.Source)))
                    {
                        warnings.Add(item.Source);
                        continue;
                    }
                    Move(item.Target, item.Source, item.Directory);
                }
                else recycle(item.Target, item.Directory);
                if (File.Exists(item.Target) || Directory.Exists(item.Target))
                {
                    warnings.Add(item.Target);
                    continue;
                }
                batch.Remove(item);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
            { warnings.Add(Path.GetFileName(item.Target) + " · " + ex.Message); }
        }
        lock (_history) if (batch.Count == 0) _history.Remove(batch);
        return new(count, 0, warnings.ToArray());
    }

    private static bool SamePath(string path, string other) =>
        string.Equals(path, other, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsWithin(string parent, string child)
    {
        string relative = Path.GetRelativePath(parent, child);
        return SamePath(parent, child) || !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string AvailablePath(string folder, string name, bool directory)
    {
        var target = Path.Combine(folder, name);
        var extension = directory ? "" : Path.GetExtension(name);
        var stem = name[..(name.Length - extension.Length)];
        for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(folder, $"{stem} ({i}){extension}");
        return target;
    }

    private static void Move(string source, string target, bool directory)
    {
        if (!directory) { File.Move(source, target, overwrite: false); return; }
        if (OperatingSystem.IsWindows() && !SamePath(Path.GetPathRoot(source)!, Path.GetPathRoot(target)!))
        {
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The destination already exists.");
            Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(source, target, overwrite: false);
        }
        else Directory.Move(source, target);
    }

    private static void Copy(string source, string target, bool directory)
    {
        if (!directory) { File.Copy(source, target, overwrite: false); return; }
        var stage = Path.Combine(Path.GetDirectoryName(target)!, ".opsis-transfer-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyTree(new DirectoryInfo(source), stage);
            Directory.Move(stage, target);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static void CopyTree(DirectoryInfo source, string target)
    {
        RejectLink(source);
        Directory.CreateDirectory(target);
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            RejectLink(entry);
            var destination = Path.Combine(target, entry.Name);
            if (entry is DirectoryInfo directory) CopyTree(directory, destination);
            else File.Copy(entry.FullName, destination, overwrite: false);
        }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A junction or symbolic link requires Explorer.");
    }

    private static string? TryFingerprint(string path, bool directory)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int entries = 0;
            long totalBytes = 0;
            void CountEntry() { if (++entries > FingerprintEntryLimit) throw new IOException("Undo fingerprint entry quota exceeded."); }
            void Append(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\0"));
            void AddFile(FileInfo file)
            {
                RejectLink(file);
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = stream.Length;
                if (length > FingerprintByteLimit - totalBytes) throw new IOException("Undo fingerprint byte quota exceeded.");
                totalBytes += length;
                Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                long remaining = length;
                while (remaining > 0)
                {
                    int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new IOException("File changed during fingerprint.");
                    fileHash.AppendData(buffer, 0, read);
                    remaining -= read;
                }
                if (stream.ReadByte() != -1) throw new IOException("File changed during fingerprint.");
                hash.AppendData(fileHash.GetHashAndReset());
            }
            void AddDirectory(DirectoryInfo folder, int depth)
            {
                if (depth > FingerprintDepthLimit) throw new IOException("Undo fingerprint depth quota exceeded.");
                RejectLink(folder);
                var children = folder.EnumerateFileSystemInfos().Take(FingerprintEntryLimit - entries + 1).ToArray();
                if (children.Length > FingerprintEntryLimit - entries) throw new IOException("Undo fingerprint entry quota exceeded.");
                foreach (var item in children.OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    CountEntry();
                    Append(Path.GetRelativePath(path, item.FullName));
                    Append(item is DirectoryInfo ? "folder" : "file");
                    if (item is DirectoryInfo child) AddDirectory(child, depth + 1);
                    else AddFile((FileInfo)item);
                }
            }
            if (directory) AddDirectory(new DirectoryInfo(path), 0);
            else AddFile(new FileInfo(path));
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }
}

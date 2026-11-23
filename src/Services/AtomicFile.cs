using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Ατομική εγγραφή αρχείου ρυθμίσεων: γράφει σε προσωρινό αρχείο και μετά το μετονομάζει πάνω στο
/// τελικό. Το απλό File.WriteAllText πρώτα "κόβει" το αρχείο σε 0 bytes — ένα ταυτόχρονο Load()
/// (πολλά σημεία της εφαρμογής διαβάζουν settings συνεχώς) έβλεπε μισο-γραμμένο JSON, έπεφτε στα
/// προεπιλεγμένα και το επόμενο Save() έσβηνε τις πραγματικές ρυθμίσεις του χρήστη. Το ίδιο
/// συνέβαινε και σε διακοπή ρεύματος/crash στη μέση της εγγραφής.
/// </summary>
internal static class AtomicFile
{
    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static object Sync(string path) => FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    public static void WriteAllText(string path, string contents)
    {
        lock (Sync(path)) WriteLocked(path, contents);
    }

    private static void WriteLocked(string path, string contents)
    {
        // Idempotent saves should not create new SSD writes or refresh the backup.
        try { if (File.Exists(path) && string.Equals(File.ReadAllText(path), contents, StringComparison.Ordinal)) return; }
        catch (IOException) { /* transient sharing error: retry with the normal write path */ }
        catch (UnauthorizedAccessException) { /* normal write reports the real permission error */ }
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(contents);

            KeepBackup(path);
            // A reader or antivirus can briefly hold the destination open on Windows.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); return; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException && attempt < 6)
                {
                    System.Threading.Thread.Sleep(25 * (attempt + 1));
                }
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Keeps the previous GOOD file as "<name>.bak" (refreshed at most every 10 minutes so frequent saves do not rewrite it constantly,
    // and never overwritten with a file that is not valid JSON). ReadAllText restores it automatically if the main file is damaged.
    private static void KeepBackup(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string bak = path + ".bak";
            if (File.Exists(bak) && DateTime.UtcNow - File.GetLastWriteTimeUtc(bak) < TimeSpan.FromMinutes(10)) return;
            if (!IsValidJson(File.ReadAllText(path))) return;
            // Refresh the backup atomically as well: power loss must never truncate
            // the only valid recovery copy while writing a replacement.
            string nextBackup = bak + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(path, nextBackup); File.Move(nextBackup, bak, overwrite: true); }
            finally { try { File.Delete(nextBackup); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        catch (Exception) { /* a backup is a convenience, never a reason to fail a save */ }
    }

    private static bool IsValidJson(string text)
    {
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    // Ανάγνωση με λίγες επαναλήψεις: ένα στιγμιαίο sharing violation (το rename της εγγραφής,
    // antivirus, OneDrive) δεν πρέπει να μετατρέπεται σε "χάθηκαν οι ρυθμίσεις → προεπιλογές".
    public static string ReadAllText(string path)
    {
        lock (Sync(path)) return ReadLocked(path);
    }

    private static string ReadLocked(string path)
    {
        string text = ReadWithRetry(path);
        if (IsValidJson(text)) return text;

        // The main file is empty/corrupt (crash mid-write on an old version, disk problem, manual edit): restore the last good copy.
        string bak = path + ".bak";
        try
        {
            if (File.Exists(bak))
            {
                string good = File.ReadAllText(bak);
                if (IsValidJson(good))
                {
                    // Recover with an atomic rename: a crash during recovery must not truncate the main file.
                    string recovery = path + "." + Guid.NewGuid().ToString("N") + ".recover";
                    try { File.Copy(bak, recovery); File.Move(recovery, path, overwrite: true); }
                    finally { try { File.Delete(recovery); } catch (IOException) { } }
                    return good;
                }
            }
        }
        catch (Exception) { /* fall through with what we have */ }
        return text;
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(25); }
        }
    }
}

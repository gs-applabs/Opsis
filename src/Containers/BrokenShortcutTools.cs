using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Containers;

/// <summary>Checks only .lnk references in a container. Never changes target files.
/// Repair creates a backup of every changed shortcut, and requires the user's consent.</summary>
internal static class BrokenShortcutTools
{
    internal sealed record Broken(string Shortcut, string MissingTarget);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> KnownBroken =
        new(StringComparer.OrdinalIgnoreCase);
    internal static bool IsKnownBroken(string path) => KnownBroken.ContainsKey(path);

    internal static List<Broken> Find(IEnumerable<string> paths)
    {
        var found = new List<Broken>();
        if (KnownBroken.Count > 4096) KnownBroken.Clear();
        foreach (var link in paths.Where(p => p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                  .Distinct(StringComparer.OrdinalIgnoreCase).Take(500))
        {
            if (!File.Exists(link))
            {
                KnownBroken[link] = 1;
                found.Add(new(link, "(shortcut file missing)"));
                continue;
            }
            var target = Target(link);
            if (!string.IsNullOrWhiteSpace(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                KnownBroken[link] = 1;
                found.Add(new(link, target));
            }
            else KnownBroken.TryRemove(link, out _);
        }
        return found;
    }

    private static string? Target(string link)
    {
        object? shell = null, shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            shell = Activator.CreateInstance(type);
            if (shell == null) return null;
            shortcut = ((dynamic)shell).CreateShortcut(link);
            return Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut).TargetPath);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool Repair(string link, string replacement)
    {
        if (!File.Exists(link) || (!File.Exists(replacement) && !Directory.Exists(replacement))) return false;
        object? shell = null, shortcut = null;
        try
        {
            var backup = link + ".opsis-original.bak";
            if (File.Exists(backup)) return false; // never overwrite the first original
            File.Copy(link, backup, overwrite: false);
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link);
            ((dynamic)shortcut!).TargetPath = replacement;
            ((dynamic)shortcut).Save();
            KnownBroken.TryRemove(link, out _);
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    internal static async Task InspectAndRepairAsync(IWin32Window owner, IEnumerable<string> paths)
    {
        var collected = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
        var broken = await Task.Run(() => Find(collected));
        if (broken.Count == 0)
        {
            MessageBox.Show(owner, "Δεν εντοπίστηκαν σπασμένες συντομεύσεις. / No broken shortcuts found.",
                "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var details = string.Join(Environment.NewLine,
            broken.Take(12).Select(item => Path.GetFileName(item.Shortcut) + " → " + item.MissingTarget));
        var choice = MessageBox.Show(owner,
            $"Βρέθηκαν {broken.Count} συντομεύσεις χωρίς προορισμό / {broken.Count} broken shortcuts:" +
            Environment.NewLine + Environment.NewLine + details + Environment.NewLine + Environment.NewLine +
            "Επιλογή φακέλου για ασφαλή μαζική επιδιόρθωση; / Choose a folder for batch repair?" +
            Environment.NewLine + "Τα αρχικά .lnk αποθηκεύονται ως .opsis-original.bak.",
            "Opsis · Broken shortcuts", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes) return;
        using var picker = new FolderBrowserDialog { Description = "Select a trusted directory containing replacement executables" };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        // Avoid recursive filesystem scans, network walks or executables with ambiguous names.
        var candidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var folders = new[] { picker.SelectedPath }
                .Concat(Directory.EnumerateDirectories(picker.SelectedPath).Take(50));
            foreach (var folder in folders)
                foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Take(1000)
                    .Concat(Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly).Take(100)))
                {
                    var key = Path.GetFileName(path);
                    if (!candidates.TryGetValue(key, out var list)) candidates[key] = list = [];
                    if (list.Count < 3) list.Add(path);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(owner, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var matches = broken.Where(item => candidates.TryGetValue(Path.GetFileName(item.MissingTarget), out var list) && list.Count == 1)
            .Select(item => (item.Shortcut, Replacement: candidates[Path.GetFileName(item.MissingTarget)][0]))
            .ToArray();
        if (matches.Length == 0) { MessageBox.Show(owner, "Δεν βρέθηκαν μοναδικοί προορισμοί. / No unambiguous matches."); return; }
        if (MessageBox.Show(owner, $"Repair {matches.Length} shortcuts? The originals will be backed up first.",
                "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        int updated = 0;
        foreach (var m in matches) if (Repair(m.Shortcut, m.Replacement)) updated++;
        MessageBox.Show(owner, $"Επιδιορθώθηκαν / Repaired: {updated} of {matches.Length}", "Opsis");
    }
}

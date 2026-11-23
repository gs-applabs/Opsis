using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text.Json;
using Opsis.Containers;
using static MotionDesk.Containers.LayoutImport;

namespace MotionDesk.Containers;

internal static class DesktopFiles
{
    internal sealed record Entry(string Path, string Name, bool Folder, long Bytes, DateTime Created,
        DateTime Newest, string Origin, string? Shortcut);

    private static readonly Dictionary<string, HashSet<string>> Extensions = new()
    {
        ["documents"] = Ext("doc docx xls xlsx ppt pptx pdf txt md rtf odt ods odp csv tsv one epub json xml"),
        ["images"] = Ext("png jpg jpeg gif bmp webp heic svg tif tiff ico psd raw avif"),
        ["music"] = Ext("mp3 flac wav m4a aac ogg wma opus"),
        ["video"] = Ext("mp4 mkv avi mov wmv webm m4v ts"),
        ["archives"] = Ext("zip 7z rar tar gz bz2 xz iso cab"),
        ["programs"] = Ext("exe msi bat cmd appref-ms"),
        ["installers"] = Ext("msi msix msixbundle appx appxbundle")
    };

    private static HashSet<string> Ext(string text) => text.Split(' ').Select(value => "." + value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    { "type", "ext", "exactName", "name", "glob", "shortcutTarget", "foldersOnly", "filesOnly", "plainFiles", "sizeMb", "createdTime", "createdWeekday", "origin", "idleDays" };

    internal static bool Supports(JsonElement condition)
    {
        var kind = String(condition, "cond");
        var value = Get(condition, "value");
        if (kind == null || !Kinds.Contains(kind)) return false;
        return kind switch
        {
            "type" or "ext" or "exactName" or "createdWeekday" => value.ValueKind == JsonValueKind.Array,
            "name" or "shortcutTarget" => String(value, "op") is "contains" or "notContains" or "startsWith" or "endsWith" or "is"
                && String(value, "value") != null,
            "sizeMb" => value.ValueKind == JsonValueKind.Object,
            "idleDays" => Number(value, "min", -1) >= 0,
            "createdTime" => Number(value, "from_min", -1) is >= 0 and <= 1439 && Number(value, "to_min", -1) is >= 0 and <= 1439,
            "origin" => AsString(value) is "userDesktop" or "publicDesktop" or "namespace",
            "glob" => AsString(value) != null,
            _ => true
        };
    }

    internal static bool IsLocalPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && path.Length <= 4096 && !path.StartsWith(@"\\", StringComparison.Ordinal)
            && !path.StartsWith("//", StringComparison.Ordinal) && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
            && Path.IsPathFullyQualified(path);
    }

    internal static bool Matches(JsonElement condition, Entry entry, DateTime now)
    {
        var value = Get(condition, "value");
        var kind = String(condition, "cond");
        var options = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
        double mb = entry.Bytes / 1048576d;
        var creation = entry.Created.ToLocalTime();
        double minutes = creation.TimeOfDay.TotalMinutes;
        double from = Number(value, "from_min", -1), to = Number(value, "to_min", -1);
        return kind switch
        {
            "type" => options.Any(option => Category(AsString(option), entry)),
            "ext" => options.Any(option => AsString(option)?.TrimStart('.') is { Length: > 0 } ext
                && entry.Name.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)),
            "exactName" => options.Any(option => string.Equals(AsString(option), entry.Name, StringComparison.OrdinalIgnoreCase)),
            "name" => Compare(entry.Name, String(value, "op"), String(value, "value")),
            "glob" => FileSystemName.MatchesSimpleExpression(AsString(value) ?? "", entry.Name, ignoreCase: true),
            "shortcutTarget" => entry.Shortcut != null && Compare(entry.Shortcut, String(value, "op"), String(value, "value")),
            "foldersOnly" => entry.Folder,
            "filesOnly" when !Bool(condition, "plain") => !entry.Folder,
            "filesOnly" or "plainFiles" => !entry.Folder && !Category("programs", entry) && !Category("shortcuts", entry),
            "sizeMb" => mb >= Number(value, "min", double.NegativeInfinity) && mb <= Number(value, "max", double.PositiveInfinity),
            "createdTime" => entry.Created != default && (from <= to ? minutes >= from && minutes < to + 1 : minutes >= from || minutes < to + 1),
            "createdWeekday" => entry.Created != default && options.Any(option => option.TryGetInt32Safe(out var day) && day == ((int)creation.DayOfWeek + 6) % 7),
            "origin" => string.Equals(entry.Origin, AsString(value), StringComparison.Ordinal),
            "idleDays" => entry.Newest != default && (now - entry.Newest).TotalDays >= Number(value, "min", double.PositiveInfinity),
            _ => false
        };
    }

    private static bool Compare(string actual, string? op, string? wanted)
    {
        if (wanted == null) return false;
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        return op switch
        {
            "contains" => actual.Contains(wanted, comparison),
            "notContains" => !actual.Contains(wanted, comparison),
            "startsWith" => actual.StartsWith(wanted, comparison),
            "endsWith" => actual.EndsWith(wanted, comparison),
            "is" => actual.Equals(wanted, comparison),
            _ => false
        };
    }

    private static bool Category(string? category, Entry file)
    {
        if (category == "folders") return file.Folder;
        if (file.Folder || category == null) return false;
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        bool shortcut = ext is ".lnk" or ".url";
        if (category == "shortcuts") return shortcut && !Category("programs", file);
        if (category == "programs" && shortcut)
            return file.Shortcut == null && ext == ".lnk" || Extensions["programs"].Contains(Path.GetExtension(file.Shortcut) ?? "");
        if (category == "installers" && ext == ".exe")
            return file.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) || file.Name.Contains("install", StringComparison.OrdinalIgnoreCase);
        return Extensions.TryGetValue(category, out var exts) &&
            (exts.Contains(ext) || (shortcut && exts.Contains(Path.GetExtension(file.Shortcut) ?? "")));
    }

    internal static List<ContainerItem> Search(SmartFolderSpec spec, DateTime now, CancellationToken cancel)
    {
        if (spec.Folders.Length == 0 || !string.IsNullOrWhiteSpace(spec.Everything) || !spec.Conditions.All(Supports)) return [];
        bool shortcuts = NeedsShortcuts(spec.Conditions);
        return spec.Folders.SelectMany(folder => Enumerate(folder, spec.Recursive, "", shortcuts, cancel))
            .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Where(entry => SmartMatches(spec, entry, now))
            .OrderByDescending(entry => entry.Newest).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(spec.Limit).Select(ToItem).ToList();
    }

    internal static bool SmartMatches(SmartFolderSpec spec, Entry entry, DateTime now)
    {
        var checks = spec.Conditions.Select(condition => Matches(condition, entry, now)).ToList();
        if (spec.Days is int days) checks.Add(entry.Newest != default && entry.Newest >= now.AddDays(-days));
        if (!string.IsNullOrWhiteSpace(spec.Name))
            checks.Add(spec.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(part => entry.Name.Contains(part, StringComparison.OrdinalIgnoreCase)));
        return checks.Count == 0 || (spec.Any ? checks.Any(value => value) : checks.All(value => value));
    }

    private static bool NeedsShortcuts(IEnumerable<JsonElement> conditions) =>
        conditions.Any(condition => String(condition, "cond") is "shortcutTarget" or "type");

    internal static Dictionary<string, List<ContainerItem>> Route(DesktopOrganization package, IReadOnlyList<Container> containers,
        string userDesktop, string publicDesktop, CancellationToken cancel,
        IReadOnlyDictionary<string, string>? prior = null)
    {
        var result = new Dictionary<string, List<ContainerItem>>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? inbox = null;
        foreach (var c in containers.Where(c => c.Kind == ContainerKind.Data && !HasSmart(c)))
        {
            targets.Add(c.Id);
            if (c.Extra?.ContainsKey(InboxKey) == true) inbox ??= c.Id;
            foreach (var tab in c.Tabs)
                if (TabTarget(tab) is { } value) targets.Add(value);
        }
        if (!package.RulesEnabled || targets.Count == 0) return result;

        var assigned = new HashSet<string>(containers.SelectMany(c => c.Items.Concat(c.Tabs.SelectMany(tab => tab.Items)))
            .Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        bool shortcuts = NeedsShortcuts(package.Rules.Where(rule => rule.Enabled).SelectMany(rule => rule.Conditions));
        var files = Enumerate(userDesktop, false, "userDesktop", shortcuts, cancel)
            .Concat(Enumerate(publicDesktop, false, "publicDesktop", shortcuts, cancel));
        bool ValidTarget(string id) => id == "inbox" ? inbox != null : targets.Contains(id);
        var activeRules = package.Rules.Where(rule => rule.Enabled && ValidTarget(rule.Target))
            .OrderByDescending(rule => rule.Priority).ToArray();
        foreach (var file in files)
        {
            if (!assigned.Add(file.Path)) continue;
            var target = activeRules.FirstOrDefault(rule => rule.Conditions.All(c => Matches(c, file, now)))?.Target;
            if (target == null)
                target = package.KeepUnmatched && prior != null && prior.TryGetValue(file.Path, out var previous)
                    && targets.Contains(previous) ? previous : package.DefaultTarget;
            if (target == "inbox") target = inbox;
            if (target == null || !targets.Contains(target)) continue;
            if (!result.TryGetValue(target, out var items)) result[target] = items = [];
            if (items.Count < 500) items.Add(ToItem(file));
        }
        return result;
    }

    private static ContainerItem ToItem(Entry entry) => new()
    { Id = StableId(entry.Path), Path = entry.Path, DisplayName = entry.Name, IsFolder = entry.Folder };

    private static IEnumerable<Entry> Enumerate(string folder, bool recursive, string origin, bool shortcuts, CancellationToken cancel)
    {
        if (!IsLocalPath(folder) || !Directory.Exists(folder)) yield break;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true, RecurseSubdirectories = recursive, MaxRecursionDepth = 24,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
        };
        var directory = new DirectoryInfo(folder);
        using var files = directory.EnumerateFileSystemInfos("*", options).GetEnumerator();
        for (int count = 0; count < 30000; count++)
        {
            cancel.ThrowIfCancellationRequested();
            Entry? entry;
            try { if (!files.MoveNext()) yield break; entry = Read(files.Current, origin, shortcuts); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
            if (entry != null) yield return entry;
        }
    }

    private static Entry? Read(FileSystemInfo file, string origin, bool shortcuts)
    {
        if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith("~$") ||
            new[] { ".tmp", ".part", ".crdownload" }.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)) return null;
        var modified = file.LastWriteTimeUtc;
        var created = file.CreationTimeUtc;
        var target = shortcuts && file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ShortcutTarget(file.FullName) : null;
        return new(file.FullName, file.Name, file is DirectoryInfo, file is FileInfo info ? info.Length : 0,
            created, modified > created ? modified : created, origin, target);
    }

    private static string? ShortcutTarget(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        object? shell = null, shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            shell = Activator.CreateInstance(type);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            return ((dynamic)shortcut).TargetPath as string;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return null; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}

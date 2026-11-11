using System.Text.Json;

namespace Opsis.Containers;

/// <summary>What a rule looks at. Pure data: the host fills a <see cref="RuleContext"/> from the system and the engine decides (no polling inside the library).</summary>
public enum RuleWhen { MonitorCount, ProcessRunning, TimeBetween, FullscreenApp }
public enum RuleDo { HideContainers, ShowContainers, LoadSnapshot }

public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public RuleWhen When { get; set; }
    /// <summary>MonitorCount: "2" (at least); ProcessRunning: "game.exe"; TimeBetween: "22:00-07:00" (may wrap midnight); FullscreenApp: ignored.</summary>
    public string Value { get; set; } = "";
    public RuleDo Do { get; set; }
    /// <summary>Containers for Hide/Show (empty = all); snapshot name for LoadSnapshot.</summary>
    public List<string> Targets { get; set; } = new();
    /// <summary>When the condition stops being true, undo Hide/Show. Not used for LoadSnapshot.</summary>
    public bool RevertWhenFalse { get; set; } = true;
}

public sealed record RuleContext(int MonitorCount, IReadOnlySet<string> RunningProcesses, TimeSpan TimeOfDay, bool FullscreenApp);
public sealed record RuleAction(string RuleId, RuleDo Do, IReadOnlyList<string> Targets);

/// <summary>Edge-triggered rule evaluation: an action is produced when a condition TURNS true (and its inverse when it turns false, if requested) - not on every tick, so a user's manual change is not fought.</summary>
public static class RuleEngine
{
    public static bool Holds(Rule r, RuleContext c)
    {
        switch (r.When)
        {
            case RuleWhen.MonitorCount: return int.TryParse(r.Value, out var n) && c.MonitorCount >= n;
            case RuleWhen.ProcessRunning: return !string.IsNullOrWhiteSpace(r.Value) && c.RunningProcesses.Contains(r.Value.Trim().ToLowerInvariant());
            case RuleWhen.FullscreenApp: return c.FullscreenApp;
            case RuleWhen.TimeBetween:
            {
                var parts = r.Value.Split('-');
                if (parts.Length != 2 || !TimeSpan.TryParse(parts[0], out var a) || !TimeSpan.TryParse(parts[1], out var b)) return false;
                return a <= b ? c.TimeOfDay >= a && c.TimeOfDay < b : c.TimeOfDay >= a || c.TimeOfDay < b;
            }
            default: return false;
        }
    }

    /// <param name="previouslyActive">ids of rules that held at the last evaluation</param>
    public static (List<RuleAction> Actions, HashSet<string> Active) Evaluate(IEnumerable<Rule> rules, RuleContext ctx, IReadOnlySet<string> previouslyActive)
    {
        var actions = new List<RuleAction>(); var active = new HashSet<string>();
        foreach (var r in rules)
        {
            if (!r.Enabled) continue;
            var now = Holds(r, ctx); var was = previouslyActive.Contains(r.Id);
            if (now) active.Add(r.Id);
            if (now && !was) actions.Add(new RuleAction(r.Id, r.Do, r.Targets));
            else if (!now && was && r.RevertWhenFalse && r.Do != RuleDo.LoadSnapshot)
                actions.Add(new RuleAction(r.Id, r.Do == RuleDo.HideContainers ? RuleDo.ShowContainers : RuleDo.HideContainers, r.Targets));
        }
        return (actions, active);
    }

    /// <summary>Applies Hide/Show actions to the document (LoadSnapshot is the caller's: it replaces the document).</summary>
    public static bool Apply(ContainersDocument doc, RuleAction a)
    {
        if (a.Do == RuleDo.LoadSnapshot) return false;
        var changed = false; var hide = a.Do == RuleDo.HideContainers;
        foreach (var c in doc.Containers.Where(c => a.Targets.Count == 0 || a.Targets.Contains(c.Id)))
            if (c.Hidden != hide) { c.Hidden = hide; changed = true; }
        return changed;
    }
}

/// <summary>Named layout snapshots of the whole document, one file each beside the data file. Atomic write, names are sanitized, nothing outside the folder is ever touched.</summary>
public static class ContainerSnapshots
{
    private const string Ext = ".snapshot.json";

    public static string Sanitize(string name)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var s = new string(name.Trim().Select(ch => bad.Contains(ch) || ch == '.' ? '_' : ch).ToArray());
        return s.Length == 0 ? "snapshot" : s.Length > 60 ? s[..60] : s;
    }

    private static string FileFor(string dir, string name) => System.IO.Path.Combine(dir, Sanitize(name) + Ext);

    public static void Save(ContainersDocument doc, string dir, string name)
    {
        System.IO.Directory.CreateDirectory(dir);
        var f = FileFor(dir, name); var tmp = f + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(doc, ContainersStore.Json));
        System.IO.File.Move(tmp, f, true);
    }

    public static ContainersDocument? Load(string dir, string name)
    {
        var f = FileFor(dir, name);
        if (!System.IO.File.Exists(f)) return null;
        try { return JsonSerializer.Deserialize<ContainersDocument>(System.IO.File.ReadAllText(f), ContainersStore.Json); } catch (JsonException) { return null; }
    }

    public static List<string> List(string dir) => !System.IO.Directory.Exists(dir) ? new() :
        System.IO.Directory.EnumerateFiles(dir, "*" + Ext).Select(p => System.IO.Path.GetFileName(p)[..^Ext.Length]).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool Delete(string dir, string name) { var f = FileFor(dir, name); if (!System.IO.File.Exists(f)) return false; System.IO.File.Delete(f); return true; }
}

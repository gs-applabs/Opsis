using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Safe data-only automation engine. No arbitrary processes, command execution,
/// downloadable scripts or privilege escalation. Works independently of plugins.
/// </summary>
public static class OpsisAutomationStudio
{
    public sealed class Rule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public bool Enabled { get; set; }
        // Startup | DailyAt | WeekdayAt
        public string Trigger { get; set; } = "DailyAt";
        public int Hour { get; set; } = 9;
        public int Minute { get; set; }
        // all days when DailyAt, Monday through Friday when WeekdayAt
        // Scene | AppearancePreset | ThemeMode | HotCorners
        public string Action { get; set; } = "Scene";
        public string Target { get; set; } = "";
    }
    public sealed class Configuration
    {
        public int Schema { get; set; } = 1;
        public List<Rule> Rules { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateOnly> FiredDates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FiredStartup = new(StringComparer.OrdinalIgnoreCase);
    private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "automations.json");
    private static readonly HashSet<string> Triggers = new(StringComparer.Ordinal) { "Startup","DailyAt","WeekdayAt" };
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "Scene","AppearancePreset","ThemeMode","HotCorners" };

    public static Configuration Read()
    {
        lock (Gate)
        {
            if (!File.Exists(ConfigPath)) return new Configuration();
            if (new FileInfo(ConfigPath).Length > 128 * 1024)
                throw new InvalidDataException("Automation file exceeds quota.");
            var conf = JsonSerializer.Deserialize<Configuration>(AtomicFile.ReadAllText(ConfigPath))
                ?? throw new InvalidDataException("Invalid automation settings.");
            Validate(conf);
            return conf;
        }
    }

    public static void Validate(Configuration conf)
    {
        if (conf.Schema != 1 || conf.Rules == null || conf.Rules.Count > 100)
            throw new InvalidDataException("Unsupported automations schema or count.");
        if (conf.Rules.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Name) || r.Target == null))
            throw new InvalidDataException("Incomplete automation rule.");
        if (conf.Rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != conf.Rules.Count)
            throw new InvalidDataException("Duplicate rule identifiers.");
        foreach (var r in conf.Rules)
        {
            if (r.Id.Length is < 1 or > 64 || r.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                r.Name.Length is < 1 or > 100 ||
                !Triggers.Contains(r.Trigger) || !Actions.Contains(r.Action) ||
                r.Hour is < 0 or > 23 || r.Minute is < 0 or > 59 ||
                r.Target.Length > 120) throw new InvalidDataException("Unsupported automation rule.");
            if (r.Action == "ThemeMode" && r.Target is not ("Light" or "Dark" or "Follow"))
                throw new InvalidDataException("Invalid theme action.");
            if (r.Action == "HotCorners" && r.Target is not ("On" or "Off"))
                throw new InvalidDataException("Invalid Hot Corners action.");
            if (r.Action is "Scene" or "AppearancePreset" && string.IsNullOrWhiteSpace(r.Target))
                throw new InvalidDataException("Missing automation target.");
        }
    }

    public static void Save(Configuration conf)
    {
        lock (Gate)
        {
            Validate(conf);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(conf, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Pure scheduler; can be tested without Windows, I/O or side effects.</summary>
    public static IReadOnlyList<Rule> Plan(Configuration conf, DateTime nowLocal, bool startup)
    {
        Validate(conf);
        var weekday = nowLocal.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        return conf.Rules.Where(r => r.Enabled && (
            r.Trigger == "Startup" && startup ||
            r.Trigger == "DailyAt" && !startup && r.Hour == nowLocal.Hour && r.Minute == nowLocal.Minute ||
            r.Trigger == "WeekdayAt" && !startup && weekday && r.Hour == nowLocal.Hour && r.Minute == nowLocal.Minute
        )).ToArray();
    }

    /// <summary>Called only when enabled in Opsis. Each rule runs once per startup/day.</summary>
    public static int ExecuteDue(bool startup)
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview ||
            !AppSettings.Load().OpsisAutomationsEnabled) return 0;
        var now = DateTime.Now;
        int count = 0;
        lock (Gate)
        {
            foreach (var rule in Plan(Read(), now, startup))
            {
                if (rule.Trigger == "Startup" ? FiredStartup.Contains(rule.Id)
                    : FiredDates.TryGetValue(rule.Id, out var date) && date == DateOnly.FromDateTime(now)) continue;
                try
                {
                    ExecuteAction(rule);
                    if (rule.Trigger == "Startup") FiredStartup.Add(rule.Id);
                    else FiredDates[rule.Id] = DateOnly.FromDateTime(now);
                    count++;
                }
                catch (Exception e)
                {
                    RotatingLogger.Write("Opsis automation " + rule.Id + ": " + e.Message);
                }
            }
        }
        return count;
    }

    private static void ExecuteAction(Rule r)
    {
        switch (r.Action)
        {
            case "Scene": WeatherSceneComposer.Apply(r.Target); return;
            case "AppearancePreset": AppearancePresets.Apply(r.Target); return;
            case "ThemeMode":
                {
                    var settings = AppSettings.Load();
                    settings.ThemeMode = r.Target;
                    settings.Save();
                    return;
                }
            case "HotCorners":
                {
                    var settings = AppSettings.Load();
                    settings.HotCornersEnabled = r.Target == "On";
                    settings.Save();
                    return;
                }
            default: throw new InvalidOperationException("Unknown automation action.");
        }
    }
}

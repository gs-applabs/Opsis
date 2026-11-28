using System.Diagnostics;
using System.Text.Json;

namespace MotionDesk.Services;

public sealed class AutomationRule
{
    public string Name { get; set; } = "Gaming Mode";
    public string TriggerProcess { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string PowerCondition { get; set; } = "Any"; // Any | Battery | AC
    public int BatteryBelowPercent { get; set; } // 0: no battery threshold
}

public sealed class AutomationService : IDisposable
{
    private static AutomationService? _instance;
    public static AutomationService Instance => _instance ??= new AutomationService();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };
    private static bool _hostReady;
    private bool _disposed;
    private readonly HashSet<AutomationRule> _wasRunning = new();
    private readonly string _path = Path.Combine(AppPaths.AppDataFolder, "automation.json");
    public event EventHandler<string>? StatusChanged;
    public List<AutomationRule> Rules { get; } = new();

    private AutomationService() { Load(); _timer.Tick += (_, _) => Evaluate(); }

    internal static bool HasTrigger(AutomationRule rule) => rule.Enabled &&
        !string.IsNullOrWhiteSpace(rule.Profile) &&
        (!string.IsNullOrWhiteSpace(rule.TriggerProcess) || rule.PowerCondition is "Battery" or "AC" || rule.BatteryBelowPercent > 0);
    internal static bool MayRun(bool ready, bool demand, bool permitted, bool review, bool hold) =>
        ready && demand && permitted && !review && !hold;
    internal static bool HasEnabledRules => _instance?.Rules.Any(HasTrigger) == true;
    internal static void StartForHost() { _hostReady = true; Instance.Reconcile(); }
    internal static void ReconcileIfCreated() => _instance?.Reconcile();
    internal static void SuspendIfCreated() => _instance?._timer.Stop();
    internal static void StopForHost() { _hostReady = false; _instance?.Dispose(); _instance = null; }
    private bool Admission => !_disposed && MayRun(_hostReady, Rules.Any(HasTrigger),
        RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.ScheduledAutomations),
        WindowsUpdateGuard.RequiresReview, WindowsUpdateGuard.RuntimeHold);
    private void Reconcile()
    {
        if (_disposed) return;
        _wasRunning.RemoveWhere(rule => !Rules.Contains(rule) || !HasTrigger(rule));
        if (Admission) _timer.Start(); else _timer.Stop();
    }
    public void Load()
    {
        List<AutomationRule> next;
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 1024 * 1024) throw new InvalidDataException("Profile rules exceed quota.");
                next = JsonSerializer.Deserialize<List<AutomationRule>>(File.ReadAllText(_path)) ?? new();
                if (next.Any(rule => rule == null)) throw new InvalidDataException("Invalid profile rule.");
            }
            else next = new();
        }
        catch (Exception ex) { RotatingLogger.Write("Profile rules load: " + ex.GetType().Name); return; }
        if (next.Count == 0) next.Add(new AutomationRule { Name = "Gaming Mode", TriggerProcess = "steamwebhelper", Profile = "Gaming", Enabled = false });
        // Replace rather than append on repeated Load; preserve the on-disk schema.
        Rules.Clear(); Rules.AddRange(next);
        Reconcile();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Rules, new JsonSerializerOptions { WriteIndented = true }));
        Reconcile();
    }

    // Εφαρμόζει το προφίλ ΜΟΝΟ τη στιγμή που ξεκινά η διεργασία (μετάβαση "δεν τρέχει" → "τρέχει"). Πριν,
    // όσο έτρεχε π.χ. ένα παιχνίδι το προφίλ ξαναφορτωνόταν κάθε 30s — κάθε φορά έκλεινε και ξανάνοιγε ΟΛΑ
    // τα widgets/containers (flicker) και έσβηνε τις χειροκίνητες αλλαγές του χρήστη.
    private void Evaluate()
    {
        if (!Admission) { _timer.Stop(); return; }
        _wasRunning.RemoveWhere(rule => !Rules.Contains(rule) || !HasTrigger(rule));
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        AutomationRule? winner = null;
        foreach (var r in Rules.Where(HasTrigger).ToList())
        {
            bool powerMatches = r.PowerCondition switch
            {
                "Battery" => power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline,
                "AC" => power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online,
                _ => true
            };
            if (r.BatteryBelowPercent > 0)
                powerMatches &= power.BatteryLifePercent is >= 0 and <= 1 &&
                    power.BatteryLifePercent * 100 <= Math.Clamp(r.BatteryBelowPercent, 1, 100);
            bool running = powerMatches && (string.IsNullOrWhiteSpace(r.TriggerProcess) ||
                RunningProcessCache.IsRunningByName(Path.GetFileNameWithoutExtension(r.TriggerProcess)));
            if (!running) { _wasRunning.Remove(r); continue; }
            if (!_wasRunning.Add(r)) continue;
            // One workspace owner per polling cycle: first newly matching
            // rule in the saved list wins, rather than reloading several profiles.
            winner ??= r;
        }
        if (winner != null && Admission)
        {
            try
            {
                if (WorkspaceProfileService.Load(winner.Profile))
                    StatusChanged?.Invoke(this, $"Automation: loaded {winner.Profile}");
            }
            catch (Exception ex) { RotatingLogger.Write("Profile automation: " + ex.GetType().Name); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Dispose(); _wasRunning.Clear();
    }
}

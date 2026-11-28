using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Opt-in, bounded diagnostic recording. Only process/resource counters and coarse
/// event categories are stored; never captures screen, names, locations, URLs,
/// installed-app lists or raw file paths. Nothing is transmitted over the network.
/// Samples remain in memory and the JSON is atomically written once on completion.
/// </summary>
internal sealed class DiagnosticSessionService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint kind);

    private sealed record BrowserTotals(int Count, double CpuCorePercent, long PrivateBytes);
    private sealed record Sample(double ElapsedSeconds, double MainCpuCorePercent,
        long MainPrivateBytes, long MainWorkingSetBytes, long ManagedBytes,
        int Handles, uint GdiObjects, uint UserObjects, BrowserTotals WebView2);
    private sealed record Activity(double ElapsedSeconds, string Kind);

    private static readonly object StaticGate = new();
    private static DiagnosticSessionService? _active;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly string? _destination;
    private readonly Action<string>? _preview;
    private readonly TimeSpan _duration;
    private readonly List<Sample> _samples = new();
    private readonly List<Activity> _events = new();
    private readonly Dictionary<int, (double CpuMs, long AtMs)> _browserPrevious = new();
    private System.Threading.Timer? _timer;
    private TimeSpan _previousCpu;
    private long _previousMs;
    private bool _done;

    public static bool Recording { get { lock (StaticGate) return _active != null; } }
    public static event Action<string?, string?>? Finished;

    private DiagnosticSessionService(string? destination, TimeSpan duration, Action<string>? preview = null)
    {
        _destination = destination;
        _duration = duration;
        _preview = preview;
        _previousCpu = _process.TotalProcessorTime;
        _previousMs = _clock.ElapsedMilliseconds;
        UiResourceAudit.Activity += OnActivity;
        // A bounded session never creates screenshots and requires no elevation.
    }

    private void Begin() => _timer = new System.Threading.Timer(_ => Tick(), null, 0, 5000);

    internal static bool StartPreview(TimeSpan duration, Action<string> preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        duration = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 20, 600));
        lock (StaticGate)
        {
            if (_active != null) return false;
            _active = new DiagnosticSessionService(null, duration, preview);
            _active.Begin();
            return true;
        }
    }

    public static bool Start(string destination, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
            throw new ArgumentException("Choose an absolute output file path.", nameof(destination));
        duration = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 20, 600));
        lock (StaticGate)
        {
            if (_active != null) return false;
            _active = new DiagnosticSessionService(destination, duration);
            _active.Begin();
            return true;
        }
    }

    public static bool Cancel()
    {
        DiagnosticSessionService? current;
        lock (StaticGate) current = _active;
        if (current == null) return false;
        current.Complete(cancelled: true);
        return true;
    }

    private void OnActivity(string kind, string ignoredPrivateValue)
    {
        // The value can be a path, plugin name or window title, so never record it.
        if (string.IsNullOrEmpty(kind) || kind.Length > 48) return;
        if (kind.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '-' and not '.')) return;
        lock (_gate)
            if (!_done && _events.Count < 400) _events.Add(new Activity(Math.Round(_clock.Elapsed.TotalSeconds, 1), kind));
    }

    private void Tick()
    {
        bool finish = false;
        lock (_gate)
        {
            if (_done) return;
            try
            {
                _process.Refresh();
                long elapsed = _clock.ElapsedMilliseconds;
                var cpu = _process.TotalProcessorTime;
                long delta = Math.Max(1, elapsed - _previousMs);
                double cpuPercent = Math.Max(0, (cpu - _previousCpu).TotalMilliseconds / delta * 100);
                _previousCpu = cpu;
                _previousMs = elapsed;

                long webBytes = 0;
                double webCpu = 0;
                int webCount = 0;
                var stillRunning = new HashSet<int>();
                foreach (var (pid, _) in MotionDesk.Widgets.WebView2Support.AuditProcesses())
                {
                    try
                    {
                        using var child = Process.GetProcessById(pid);
                        double childCpu = child.TotalProcessorTime.TotalMilliseconds;
                        webBytes += child.PrivateMemorySize64;
                        webCount++;
                        stillRunning.Add(pid);
                        if (_browserPrevious.TryGetValue(pid, out var last))
                            webCpu += Math.Max(0, (childCpu - last.CpuMs) / Math.Max(1, elapsed - last.AtMs) * 100);
                        _browserPrevious[pid] = (childCpu, elapsed);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                foreach (int pid in _browserPrevious.Keys.Where(id => !stillRunning.Contains(id)).ToArray()) _browserPrevious.Remove(pid);
                _samples.Add(new Sample(Math.Round(_clock.Elapsed.TotalSeconds, 1), Math.Round(cpuPercent, 2),
                    _process.PrivateMemorySize64, _process.WorkingSet64, GC.GetTotalMemory(false),
                    _process.HandleCount, GetGuiResources(_process.Handle, 0), GetGuiResources(_process.Handle, 1),
                    new BrowserTotals(webCount, Math.Round(webCpu, 2), webBytes)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finish = _clock.Elapsed >= _duration;
        }
        if (finish) Complete(cancelled: false);
    }

    private void Complete(bool cancelled)
    {
        string? saved = null, error = null;
        lock (_gate)
        {
            if (_done) return;
            _done = true;
        }
        _timer?.Dispose(); _timer = null;
        UiResourceAudit.Activity -= OnActivity;
        try
        {
            List<Sample> samples;
            List<Activity> events;
            lock (_gate) { samples = _samples.ToList(); events = _events.ToList(); }
            if (!cancelled)
            {
                // Save no raw identifiers, filesystem layout, machine name or network information.
                var json = JsonSerializer.Serialize(new
                {
                    schema = 1,
                    appVersion = AppVersion.Label,
                    createdUtc = DateTime.UtcNow,
                    requestedSeconds = _duration.TotalSeconds,
                    actualSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 1),
                    sampleIntervalSeconds = 5,
                    privacy = "Only local resource counters, no names, paths, addresses, locations, screenshots or telemetry.",
                    samples,
                    activityCategories = events,
                    cpuSpikes = samples.Where(s => s.MainCpuCorePercent >= 35 || s.WebView2.CpuCorePercent >= 35).Select(s => new
                    {
                        s.ElapsedSeconds, s.MainCpuCorePercent, WebViewCpuCorePercent = s.WebView2.CpuCorePercent,
                        RelatedCategory = events.LastOrDefault(e => e.ElapsedSeconds <= s.ElapsedSeconds && s.ElapsedSeconds - e.ElapsedSeconds <= 10)?.Kind
                    }).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true });
                if (_preview != null) _preview(json);
                else if (_destination != null)
                {
                    AtomicFile.WriteAllText(_destination, json);
                    saved = _destination;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { error = ex.Message; }
        finally
        {
            _process.Dispose();
            lock (StaticGate) if (ReferenceEquals(_active, this)) _active = null;
            try { Finished?.Invoke(saved, cancelled ? "cancelled" : error); }
            catch (Exception ex) { RotatingLogger.Write("Diagnostic notification failed: " + ex.GetType().Name); }
        }
    }

    public void Dispose() => Complete(cancelled: true);
}

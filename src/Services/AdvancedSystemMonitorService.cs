using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MotionDesk.Services;

public readonly record struct AdvancedMetrics(double CpuPercent, double AvailableMemoryMb, double TotalMemoryMb, double NetworkDownKbps, double NetworkUpKbps, int ProcessCount, string BatteryMode);

public sealed class AdvancedSystemMonitorService
{
    private static readonly Lazy<AdvancedSystemMonitorService> Lazy = new(() => new());
    public static AdvancedSystemMonitorService Instance => Lazy.Value;
    // Stable NetworkInterface.Id (GUID), not display Name (which Windows can rename).
    private readonly Dictionary<string, (long Down, long Up)> _adapterCounters = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastNetwork = DateTime.UtcNow;
    private DateTime _lastSnapshot = DateTime.MinValue;
    private AdvancedMetrics _cached;
    private int _processCount;
    private DateTime _lastProcessCount = DateTime.MinValue;

    // Το Process.GetProcesses() δημιουργεί ένα Process object ανά διεργασία (εκατοντάδες, ανά
    // δευτερόλεπτο, από κάθε widget) και δεν γίνονταν ποτέ Dispose — συνεχής πίεση στο GC/handles.
    // Ο αριθμός διεργασιών δεν χρειάζεται ανανέωση πιο συχνά από κάθε 5s.
    private int GetProcessCount(DateTime now)
    {
        if ((now - _lastProcessCount).TotalSeconds < 5 && _lastProcessCount != DateTime.MinValue) return _processCount;
        var procs = Process.GetProcesses();
        _processCount = procs.Length;
        foreach (var p in procs) p.Dispose();
        _lastProcessCount = now;
        return _processCount;
    }

    public AdvancedMetrics GetSnapshot()
    {
        lock (_snapshotLock) return ReadSnapshot();
    }

    private readonly object _snapshotLock = new();
    private AdvancedMetrics ReadSnapshot()
    {
        var now = DateTime.UtcNow;
        if (_lastSnapshot != DateTime.MinValue && (now - _lastSnapshot).TotalMilliseconds < 750)
            return _cached;

        var basic = SystemMonitorService.Instance.GetSnapshot();
        long deltaDown = 0, deltaUp = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || string.IsNullOrWhiteSpace(ni.Id)) continue;
            try
            {
                var stats = ni.GetIPv4Statistics();
                seen.Add(ni.Id);
                if (_adapterCounters.TryGetValue(ni.Id, out var previous))
                {
                    // Counter reset, replaced dongle or a driver restart must not create a huge spike.
                    if (stats.BytesReceived >= previous.Down) deltaDown += stats.BytesReceived - previous.Down;
                    if (stats.BytesSent >= previous.Up) deltaUp += stats.BytesSent - previous.Up;
                }
                _adapterCounters[ni.Id] = (stats.BytesReceived, stats.BytesSent);
            }
            catch (NetworkInformationException) { }
        }
        foreach (var missing in _adapterCounters.Keys.Where(id => !seen.Contains(id)).ToArray()) _adapterCounters.Remove(missing);

        var seconds = Math.Max(0.25, (now - _lastNetwork).TotalSeconds);
        var downK = deltaDown / 1024d / seconds;
        var upK = deltaUp / 1024d / seconds;
        _lastNetwork = now;

        _cached = new AdvancedMetrics(
            basic.CpuPercent, basic.AvailableMemoryMb, basic.TotalMemoryMb,
            downK, upK, GetProcessCount(now),
            PerformanceModeManager.GetRecommendedMode());
        _lastSnapshot = now;
        return _cached;
    }
}

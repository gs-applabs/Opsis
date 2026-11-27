using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MotionDesk.UI;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

internal static class UiResourceAudit
{
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint resource);

    private sealed record Sample(string Phase, double ElapsedMs, double CpuMs, double CpuCorePercent,
        long ManagedBytes, long PrivateBytes, long WorkingSetBytes, int Handles, uint GdiObjects, uint UserObjects, List<BrowserSample> Browsers);

    private sealed record BrowserSample(int Pid, string Kind, double CpuMs, long PrivateBytes, double CpuCorePercent);

    internal static event Action<string, string>? Activity;
    internal static void Mark(string kind, string value) => Activity?.Invoke(kind, value);

    internal static string? Run(MainWindow window, IReadOnlyList<string> pages, string folder, Action<int> pump)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("OPSIS_AUDIT_RESOURCE_CYCLES"), out int cycles) || cycles < 1) return null;
        cycles = Math.Clamp(cycles, 1, 30);

        using var process = Process.GetCurrentProcess();
        var elapsed = Stopwatch.StartNew();
        var samples = new List<Sample>();
        var events = new List<(double AtMs, string Kind, string Value)>();
        var browserCounters = new Dictionary<int, (double CpuMs, double AtMs)>();
        void RecordEvent(string kind, string value)
        {
            lock (events)
                if (events.Count < 2048) events.Add((elapsed.Elapsed.TotalMilliseconds, kind, value));
        }
        Activity += RecordEvent;
        try
        {
            double lastElapsed = 0, lastCpu = process.TotalProcessorTime.TotalMilliseconds;
            void Capture(string phase)
            {
                process.Refresh();
                double at = elapsed.Elapsed.TotalMilliseconds;
                double cpu = process.TotalProcessorTime.TotalMilliseconds;
                double cpuPercent = at <= lastElapsed ? 0 : Math.Max(0, (cpu - lastCpu) / (at - lastElapsed) * 100);
                var browsers = new List<BrowserSample>();
                foreach (var (pid, kind) in WebView2Support.AuditProcesses())
                {
                    try
                    {
                        using var child = Process.GetProcessById(pid);
                        double browserCpu = child.TotalProcessorTime.TotalMilliseconds;
                        double browserPercent = browserCounters.TryGetValue(pid, out var previous)
                            ? Math.Max(0, (browserCpu - previous.CpuMs) / Math.Max(1, at - previous.AtMs) * 100) : 0;
                        browsers.Add(new(pid, kind, browserCpu, child.PrivateMemorySize64, Math.Round(browserPercent, 2)));
                        browserCounters[pid] = (browserCpu, at);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                // Do not retain lifetime counters for retired WebView2 processes.
                var livePids = browsers.Select(b => b.Pid).ToHashSet();
                foreach (var pid in browserCounters.Keys.Where(pid => !livePids.Contains(pid)).ToArray())
                    browserCounters.Remove(pid);
                samples.Add(new Sample(phase, Math.Round(at, 1), Math.Round(cpu, 1), Math.Round(cpuPercent, 2),
                    GC.GetTotalMemory(false), process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount,
                    GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), browsers));
                lastElapsed = at;
                lastCpu = cpu;
            }

            const int sampleMs = 200;
            void MeasureIdle(string phase)
            {
                for (int i = 0; i < 15; i++) { pump(sampleMs); Capture(phase); }
            }

            window.NavigateTo("Dashboard");
            pump(1000);
            var navigation = new List<object>();
            var originalSettings = AppSettings.Peek();
            var originalSkin = WindowSkins.FromSettings(originalSettings.MainWindowSkin, originalSettings.WindowSkinRevision);
            Capture("baseline");
            MeasureIdle("idle-before");
            for (int cycle = 1; cycle <= cycles && !window.IsDisposed; cycle++)
            {
                foreach (string page in pages)
                {
                    double? firstPaintMs = window.MeasurePagePaint(page, pump);
                    navigation.Add(new { cycle, page, firstPaintMs });
                    pump(sampleMs);
                    Capture($"cycle-{cycle}/{page}");
                }
                window.NavigateTo("Dashboard");
                pump(600);
                Capture($"cycle-{cycle}/settled");
                if (AppSettings.IsIsolatedForTests())
                {
                    foreach (var id in new[] { "clock", "sysmon", "network", "audio", "weather", "disk", "calendar", "quicksettings", "bluetooth", "media", "notifications" })
                    {
                        if (id == "notifications")
                        {
                            try
                            {
                                var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                                if (listener.GetAccessStatus() != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) continue;
                            }
                            catch (Exception) { continue; }
                        }
                        Capture($"cycle-{cycle}/widget/{id}/before");
                        using (var widget = new WidgetWindow(id, 40, 40, 320, 220))
                        {
                            widget.Show();
                            pump(600);
                            widget.Close();
                        }
                        pump(200);
                        Capture($"cycle-{cycle}/widget/{id}/closed");
                    }
                    try
                    {
                        foreach (var skin in Enum.GetValues<WindowSkin>())
                        {
                            window.SetWindowSkin(skin);
                            pump(300);
                            Capture($"cycle-{cycle}/skin/{skin}");
                        }
                    }
                    finally { window.SetWindowSkin(originalSkin); pump(300); }
                }
            }
            MeasureIdle("idle-after");

            window.Hide();
            try
            {
                MeasureIdle("tray-idle");
                // Optional extended observation: one sample every 30 seconds, bounded to
                // 240 samples (120 minutes), with the existing UI message pump.
                if (int.TryParse(Environment.GetEnvironmentVariable("OPSIS_AUDIT_SOAK_MINUTES"), out int minutes))
                {
                    int intervals = Math.Clamp(minutes, 0, 120) * 2;
                    for (int i = 0; i < intervals && !window.IsDisposed; i++)
                    {
                        pump(30000);
                        Capture("tray-soak");
                    }
                }
            }
            finally { window.Show(); }
            var frames = WallpaperHostEngine.Instance.AuditFramesAsync();
            var frameWait = Stopwatch.StartNew();
            while (!frames.IsCompleted && frameWait.ElapsedMilliseconds < 2000) pump(20);

            var first = samples[0];
            var last = samples.Last(s => s.Phase == "idle-after");
            // Counter differences are measured after a real widget close / skin change,
            // not inferred from GC totals. The audit reports numbers without asserting a leak.
            var widgetDeltas = samples.Where(s => s.Phase.Contains("/widget/") && s.Phase.EndsWith("/closed"))
                .Select(after =>
                {
                    var before = samples.LastOrDefault(s => s.Phase == after.Phase.Replace("/closed", "/before"));
                    return new
                    {
                        widget = after.Phase.Split('/')[^2],
                        cycle = after.Phase.Split('/')[0],
                        privateBytesDelta = after.PrivateBytes - (before?.PrivateBytes ?? after.PrivateBytes),
                        gdiDelta = (long)after.GdiObjects - (before?.GdiObjects ?? after.GdiObjects),
                        userDelta = (long)after.UserObjects - (before?.UserObjects ?? after.UserObjects)
                    };
                }).ToArray();
            var skinDeltas = samples.Where(s => s.Phase.Contains("/skin/"))
                .Select(s => new { skin = s.Phase.Split('/')[^1],
                    privateBytesDelta = s.PrivateBytes - first.PrivateBytes,
                    gdiDelta = (long)s.GdiObjects - first.GdiObjects,
                    userDelta = (long)s.UserObjects - first.UserObjects }).ToArray();
            var eventSnapshot = SnapshotEvents();
            var cpuSpikes = samples.Where(s => s.CpuCorePercent >= 35 || s.Browsers.Sum(b => b.CpuCorePercent) >= 35)
                .Take(100).Select(s =>
                {
                    var lastEvent = eventSnapshot.LastOrDefault(e => e.AtMs <= s.ElapsedMs && s.ElapsedMs - e.AtMs <= 10000);
                    return new { s.Phase, s.ElapsedMs, s.CpuCorePercent,
                        webViewCpuCorePercent = Math.Round(s.Browsers.Sum(b => b.CpuCorePercent), 2),
                        relatedEventCategory = lastEvent.Kind,
                        relatedEvent = lastEvent.Value };
                }).ToArray();
            double Median(IEnumerable<double> values)
            {
                var ordered = values.OrderBy(x => x).ToArray();
                return ordered.Length == 0 ? 0 : (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2;
            }
            var idleBefore = samples.Where(s => s.Phase == "idle-before").Skip(2).ToArray();
            var idleAfter = samples.Where(s => s.Phase == "idle-after").Skip(2).ToArray();
            var settled = samples.Where(s => s.Phase.EndsWith("/settled", StringComparison.Ordinal)).ToArray();
            var soak = samples.Where(s => s.Phase == "tray-soak").ToArray();
            object Describe(Sample[] group) => new
            {
                count = group.Length,
                medianCpuCorePercent = Median(group.Select(s => s.CpuCorePercent + s.Browsers.Sum(b => b.CpuCorePercent))),
                medianPrivateBytes = Median(group.Select(s => (double)s.PrivateBytes + s.Browsers.Sum(b => b.PrivateBytes))),
                medianManagedBytes = Median(group.Select(s => (double)s.ManagedBytes)),
                medianHandles = Median(group.Select(s => (double)s.Handles)),
                medianGdiObjects = Median(group.Select(s => (double)s.GdiObjects)),
                medianUserObjects = Median(group.Select(s => (double)s.UserObjects))
            };
            var trend = new
            {
                idleBefore = Describe(idleBefore), idleAfter = Describe(idleAfter),
                firstSettled = Describe(settled.Take(1).ToArray()),
                lastSettled = Describe(settled.TakeLast(1).ToArray()),
                soakFirst = Describe(soak.Take(5).ToArray()), soakLast = Describe(soak.TakeLast(5).ToArray())
            };
            // Expose the measured baseline and trend in CI logs, not just a ZIP.
            Console.WriteLine("OPSIS_RESOURCE_TREND " + JsonSerializer.Serialize(trend));
            const string name = "ui-resources.json";
            File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(new
            {
                scope = "Main-process CPU is per phase in units of one core. Browser CPU is cumulative milliseconds per PID.",
                navigation,
                events = eventSnapshot.Select(e => new { elapsedMs = e.AtMs, kind = e.Kind, value = e.Value }),
                cpuSpikes,
                trend,
                widgetDeltas,
                skinDeltas,
                wallpaperFrames = frames.IsCompletedSuccessfully ? frames.Result : null,
                sampleMs,
                cycles,
                pages,
                peakCpuCorePercent = samples.Max(s => s.CpuCorePercent),
                privateBytesDelta = last.PrivateBytes - first.PrivateBytes,
                managedBytesDelta = last.ManagedBytes - first.ManagedBytes,
                handleDelta = last.Handles - first.Handles,
                gdiDelta = (long)last.GdiObjects - first.GdiObjects,
                userDelta = (long)last.UserObjects - first.UserObjects,
                samples
            }, new JsonSerializerOptions { WriteIndented = true }));
            return name;
        }
        finally { Activity -= RecordEvent; }

        (double AtMs, string Kind, string Value)[] SnapshotEvents()
        { lock (events) return events.ToArray(); }
    }
}

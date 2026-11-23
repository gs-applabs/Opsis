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

    private sealed record BrowserSample(int Pid, string Kind, double CpuMs, long PrivateBytes);

    internal static event Action<string, string>? Activity;
    internal static void Mark(string kind, string value) => Activity?.Invoke(kind, value);

    internal static string? Run(MainWindow window, IReadOnlyList<string> pages, string folder, Action<int> pump)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("OPSIS_AUDIT_RESOURCE_CYCLES"), out int cycles) || cycles < 1) return null;
        cycles = Math.Clamp(cycles, 1, 30);

        using var process = Process.GetCurrentProcess();
        var elapsed = Stopwatch.StartNew();
        var samples = new List<Sample>();
        var events = new List<object>();
        void RecordEvent(string kind, string value)
        {
            lock (events)
                if (events.Count < 2048) events.Add(new { elapsedMs = elapsed.Elapsed.TotalMilliseconds, kind, value });
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
                        browsers.Add(new(pid, kind, child.TotalProcessorTime.TotalMilliseconds, child.PrivateMemorySize64));
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
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
            try { MeasureIdle("tray-idle"); }
            finally { window.Show(); }
            var frames = WallpaperHostEngine.Instance.AuditFramesAsync();
            var frameWait = Stopwatch.StartNew();
            while (!frames.IsCompleted && frameWait.ElapsedMilliseconds < 2000) pump(20);

            var first = samples[0];
            var last = samples.Last(s => s.Phase == "idle-after");
            const string name = "ui-resources.json";
            File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(new
            {
                scope = "Main-process CPU is per phase in units of one core. Browser CPU is cumulative milliseconds per PID.",
                navigation,
                events = SnapshotEvents(),
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

        object[] SnapshotEvents() { lock (events) return events.ToArray(); }
    }
}

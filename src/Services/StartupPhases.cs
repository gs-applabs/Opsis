using System.Diagnostics;

namespace MotionDesk.Services
{
    /// <summary>
    /// Start-up phases measured with a Stopwatch and written to the Opsis log ("[startup] ..."), see docs/STARTUP-PHASES.md. <see cref="Mark"/> closes the phase that began at the previous mark
    /// (one line of code per phase, so the start-up code itself stays readable). <see cref="Deferred"/> runs the heavy phases one per timer tick AFTER the shell is up, so the UI thread
    /// (and with it the pointer, because the low-level hooks live there) is never held for more than one step at a time.
    /// </summary>
    public static class StartupPhases
    {
        public sealed record Phase(string Name, long Ms, long AtMs, int Thread, bool Ui);

        /// <summary>The one queue of heavy start-up steps (wallpaper, containers, restore of the last session); started once, after the main window object exists.</summary>
        public static readonly DeferredStartup Queue = new();

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<Phase> Done = new();
        private static long _last;
        private static int _uiThread = -1;

        /// <summary>Remembers the UI thread (called once from the start-up code that runs on it).</summary>
        public static void BindUiThread() { _uiThread = Environment.CurrentManagedThreadId; }

        /// <summary>Closes the phase that ran since the previous mark, on the calling thread.</summary>
        public static void Mark(string name)
        {
            long now = Clock.ElapsedMilliseconds; Phase p;
            lock (Done) { p = new Phase(name, now - _last, now, Environment.CurrentManagedThreadId, Environment.CurrentManagedThreadId == _uiThread); _last = now; Done.Add(p); }
            Log(p);
        }

        /// <summary>Times one block separately (used by the deferred steps). Does not move the <see cref="Mark"/> baseline.</summary>
        public static void Measure(string name, Action work)
        {
            long t0 = Clock.ElapsedMilliseconds;
            try { work(); }
            catch (Exception ex) { RotatingLogger.Write("[startup] " + name + " failed: " + ex.Message); }
            long t1 = Clock.ElapsedMilliseconds; var p = new Phase(name, t1 - t0, t1, Environment.CurrentManagedThreadId, Environment.CurrentManagedThreadId == _uiThread);
            lock (Done) Done.Add(p);
            Log(p);
        }

        public static IReadOnlyList<Phase> Snapshot() { lock (Done) return Done.ToList(); }
        private static void Log(Phase p) { try { RotatingLogger.Write($"[startup] {p.Name,-34} {p.Ms,5} ms  (t={p.AtMs} ms, thread {p.Thread}{(p.Ui ? ", UI" : "")})"); } catch (Exception) { } }

        // ---- UI stall probe: a 50 ms timer on the UI thread; its lateness is the longest time the UI thread (and so the pointer hooks) did not answer
        private static System.Windows.Forms.Timer? _probe; private static long _probeLast; private static long _maxStall; private static long _probeEnd;
        public static void StartStallProbe(int seconds = 25)
        {
            if (_probe != null) return;
            _probeLast = Clock.ElapsedMilliseconds; _probeEnd = _probeLast + seconds * 1000L;
            _probe = new System.Windows.Forms.Timer { Interval = 50 };
            _probe.Tick += (_, _) =>
            {
                long now = Clock.ElapsedMilliseconds; _maxStall = Math.Max(_maxStall, now - _probeLast - 50); _probeLast = now;
                if (now >= _probeEnd) { _probe!.Stop(); _probe.Dispose(); _probe = null; RotatingLogger.Write($"[startup] longest UI-thread stall in the first {seconds} s: {_maxStall} ms"); }
            };
            _probe.Start();
        }
        public static long MaxStallMs => _maxStall;
    }

    /// <summary>Runs queued start-up steps one at a time, first after <c>initialDelayMs</c>, then every <c>stepMs</c>. A failing step is logged and never stops the others.</summary>
    public sealed class DeferredStartup : IDisposable
    {
        private readonly Queue<(string Name, Action Work)> _queue = new();
        private System.Windows.Forms.Timer? _timer;
        public int Pending => _queue.Count;

        /// <summary>Diagnostic only: OPSIS_STARTUP_NODEFER=1 runs every step at once (the old behaviour) so the two start-ups can be compared with the same binary.</summary>
        private static readonly bool NoDefer = Environment.GetEnvironmentVariable("OPSIS_STARTUP_NODEFER") == "1";

        public void Add(string name, Action work) { if (NoDefer) StartupPhases.Measure("immediate: " + name, work); else _queue.Enqueue((name, work)); }

        public void Start(int initialDelayMs = 1200, int stepMs = 400)
        {
            if (_timer != null || _queue.Count == 0) return;
            _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, initialDelayMs) };
            _timer.Tick += (_, _) => { _timer!.Interval = Math.Max(1, stepMs); if (!Step()) { _timer.Stop(); } };
            _timer.Start();
        }

        /// <summary>Runs the next step. False when nothing is left afterwards.</summary>
        public bool Step()
        {
            if (_queue.Count == 0) return false;
            var (name, work) = _queue.Dequeue();
            StartupPhases.Measure("deferred: " + name, work);
            return _queue.Count > 0;
        }

        public void Dispose() { _timer?.Stop(); _timer?.Dispose(); _timer = null; }
    }
}

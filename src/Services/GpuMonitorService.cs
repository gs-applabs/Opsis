using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace MotionDesk.Services
{
    public sealed class GpuSnapshot
    {
        public string Name { get; set; } = "—";
        public double? LoadPercent { get; set; }
        public double? TemperatureC { get; set; }
        public double? CoreVoltage { get; set; }
        public double? MemoryUsedMb { get; set; }
        public bool Available { get; set; }
        public int Count { get; set; }
    }

    // Πραγματικά sensor data GPU (φόρτος/θερμοκρασία/τάση πυρήνα) μέσω LibreHardwareMonitorLib
    // (ανοιχτού κώδικα, MPL-2.0, ο διάδοχος του OpenHardwareMonitor) — ζητήθηκε ρητά "voltage της
    // GPU" στο System Monitor widget. Οι στάνταρ Win32 performance counters (αυτό που ήδη
    // χρησιμοποιεί το SystemMonitorService για CPU/RAM) ΔΕΝ εκθέτουν καθόλου τάση — μόνο μια
    // πραγματική βιβλιοθήκη hardware sensors μπορεί να τη διαβάσει. Ενεργοποιείται ΜΟΝΟ το GPU
    // (όχι CPU/motherboard) ώστε να ΜΗΝ χρειάζεται ο πυρηνικός driver (WinRing0) που απαιτεί
    // Administrator — τα GPU sensors διαβάζονται μέσω NVAPI/ADL/Intel vendor APIs, διαθέσιμα
    // συνήθως και χωρίς elevation. Αν κάποιος sensor δεν είναι διαθέσιμος σε αυτό το σύστημα,
    // γυρνάει null αντί να πετάξει exception — το widget δείχνει "—" σε αυτή την περίπτωση.
    public sealed class GpuMonitorService : IDisposable
    {
        public static GpuMonitorService Instance { get; } = new();
        private const int RefreshIntervalMs = 1500;
        private const int IdleReleaseMs = 30_000;
        private long _lastDemand;
        private System.Threading.Timer? _idleTimer;
        private readonly object _hardwareLock = new();
        private readonly UpdateVisitor _visitor = new();
        private Computer? _computer;
        private bool _initializationAttempted;
        private bool _disposed;
        private int _sampling;
        private long _nextSample;
        private GpuSnapshot[]? _readings;

        private GpuMonitorService() { }

        public bool HasSample => Volatile.Read(ref _readings) != null;
        public int Count => RequestSample().Length;

        /// <summary>Dedicated adapters first, integrated adapters last, in driver order within each group.</summary>
        public static List<(HardwareType Type, string Name)> Adapters(IEnumerable<(HardwareType Type, string Name)> all) =>
            all.Where(h => h.Type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
               .OrderBy(h => h.Type == HardwareType.GpuIntel).ToList();

        public GpuSnapshot GetSnapshot() => GetSnapshot(0);

        public List<GpuSnapshot> GetAllSnapshots() => RequestSample().Select(Copy).ToList();

        public GpuSnapshot GetSnapshot(int index)
        {
            var samples = RequestSample();
            if (samples.Length == 0) return new GpuSnapshot();
            return Copy(samples[((index % samples.Length) + samples.Length) % samples.Length]);
        }

        // UI, wallpaper and widgets can request readings at once. Only the background sampler
        // touches drivers, and it updates every adapter in a single pass.
        internal static bool ShouldReleaseIdle(long elapsedMs, bool sampling) => elapsedMs >= IdleReleaseMs && !sampling;

        private GpuSnapshot[] RequestSample()
        {
            Volatile.Write(ref _lastDemand, Environment.TickCount64);
            if (!Volatile.Read(ref _disposed) && Environment.TickCount64 >= Volatile.Read(ref _nextSample) &&
                Interlocked.CompareExchange(ref _sampling, 1, 0) == 0)
                _ = Task.Run(ReadSensors);
            return Volatile.Read(ref _readings) ?? [];
        }

        private void ReadSensors()
        {
            try
            {
                lock (_hardwareLock)
                {
                    if (_disposed) return;
                    if (!_initializationAttempted)
                    {
                        _initializationAttempted = true;
                        _computer = new Computer { IsGpuEnabled = true };
                        try { _computer.Open(); }
                        catch
                        {
                            try { _computer.Close(); } catch (Exception) { }
                            _computer = null;
                        }
                    }

                    if (_computer == null) { Volatile.Write(ref _readings, []); return; }
                    _computer.Accept(_visitor);
                    var adapters = _computer.Hardware
                        .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        .OrderBy(h => h.HardwareType == HardwareType.GpuIntel).ToArray();
                    Volatile.Write(ref _readings, adapters.Select(gpu => ReadAdapter(gpu, adapters.Length)).ToArray());
                }
            }
            catch (Exception)
            {
                lock (_hardwareLock)
                    if (!_disposed && _readings == null) Volatile.Write(ref _readings, []);
            }
            finally
            {
                lock (_hardwareLock)
                {
                    Volatile.Write(ref _nextSample, Environment.TickCount64 + RefreshIntervalMs);
                    Volatile.Write(ref _sampling, 0);
                    if (!_disposed)
                    {
                        // One deadline, owned by the sensor service. No sampling
                        // timer: only actual snapshot requests touch drivers.
                        _idleTimer ??= new System.Threading.Timer(_ => ReleaseIdle(), null,
                            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                        _idleTimer.Change(IdleReleaseMs, System.Threading.Timeout.Infinite);
                    }
                }
            }
        }

        private static GpuSnapshot ReadAdapter(IHardware gpu, int count)
        {
            var reading = new GpuSnapshot { Name = gpu.Name, Available = true, Count = count };
            foreach (var sensor in gpu.Sensors)
            {
                if (sensor.Value is not float value) continue;
                switch (sensor.SensorType)
                {
                    case SensorType.Load when reading.LoadPercent == null && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                        reading.LoadPercent = value; break;
                    case SensorType.Temperature when reading.TemperatureC == null:
                        reading.TemperatureC = value; break;
                    case SensorType.Voltage when reading.CoreVoltage == null:
                        reading.CoreVoltage = value; break;
                    case SensorType.SmallData when reading.MemoryUsedMb == null && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase):
                        reading.MemoryUsedMb = value; break;
                }
            }
            return reading;
        }

        private static GpuSnapshot Copy(GpuSnapshot value) => new()
        {
            Name = value.Name, Available = value.Available, Count = value.Count, LoadPercent = value.LoadPercent,
            CoreVoltage = value.CoreVoltage, TemperatureC = value.TemperatureC, MemoryUsedMb = value.MemoryUsedMb
        };

        private void ReleaseIdle()
        {
            lock (_hardwareLock)
            {
                if (_disposed || _idleTimer == null) return;
                long elapsed = Environment.TickCount64 - Volatile.Read(ref _lastDemand);
                if (!ShouldReleaseIdle(elapsed, Volatile.Read(ref _sampling) != 0))
                {
                    _idleTimer.Change((int)Math.Clamp(IdleReleaseMs - elapsed, 1000L, IdleReleaseMs),
                        System.Threading.Timeout.Infinite);
                    return;
                }
                _idleTimer.Dispose(); _idleTimer = null;
                CloseSensors();
                // Reopen on a subsequent request, including after device changes.
                _initializationAttempted = false;
                Volatile.Write(ref _nextSample, 0);
                Volatile.Write(ref _readings, null);
            }
        }

        private void CloseSensors()
        {
            try { _computer?.Close(); } catch (Exception) { }
            _computer = null;
        }

        public void Dispose()
        {
            lock (_hardwareLock)
            {
                Volatile.Write(ref _disposed, true);
                _idleTimer?.Dispose(); _idleTimer = null;
                CloseSensors();
                Volatile.Write(ref _readings, []);
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}

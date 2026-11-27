using System;
using System.Linq;
using System.Management;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>What the machine is, in one cheap snapshot (no LibreHardwareMonitor): used for the tier decision log and the diagnostics.</summary>
    public sealed record HardwareInfo(long RamMb, int LogicalCores, int PhysicalCores, string[] Adapters, int Monitors, long TotalPixels, bool OnBattery, string PowerPlan)
    {
        private static readonly object MetadataGate = new();
        private static string[]? _adapters;
        private static int? _physical;
        private static long _adapterReadAt;
        private const long AdapterCacheMs = 60_000;

        internal static void InvalidateAdapterMetadata()
        {
            lock (MetadataGate) _adapters = null;
        }

        internal static string[] AdapterNames()
        {
            lock (MetadataGate)
            {
                if (_adapters != null && Environment.TickCount64 - _adapterReadAt < AdapterCacheMs)
                    return (string[])_adapters.Clone();
                var names = new List<string>();
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    using var results = searcher.Get();
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                            if (mo["Name"] is string name && name.Length > 0) names.Add(name);
                    }
                }
                catch (Exception) { }
                _adapters = names.ToArray();
                _adapterReadAt = Environment.TickCount64;
                return (string[])_adapters.Clone();
            }
        }

        private static int PhysicalCoresOnce()
        {
            lock (MetadataGate)
            {
                if (_physical is int cached) return cached;
                int physical = Environment.ProcessorCount;
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                    using var results = searcher.Get();
                    int sum = 0;
                    foreach (ManagementObject mo in results)
                        using (mo) sum += Convert.ToInt32(mo["NumberOfCores"]);
                    if (sum > 0) physical = sum;
                }
                catch (Exception) { }
                _physical = physical;
                return physical;
            }
        }

        public static HardwareInfo Detect()
        {
            int physical = PhysicalCoresOnce();
            string[] adapters = AdapterNames();
            long pixels = 0; int monitors = 1;
            try { var all = Screen.AllScreens; monitors = all.Length; pixels = all.Sum(x => (long)x.Bounds.Width * x.Bounds.Height); } catch (Exception) { }
            bool battery = false;
            try { battery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; } catch (Exception) { }
            return new HardwareInfo(HardwareProfileService.TotalRamMb(), Environment.ProcessorCount, physical, adapters, monitors, pixels, battery, "");
        }

        public override string ToString() =>
            $"RAM {RamMb} MB, cores {PhysicalCores}/{LogicalCores}, GPUs [{string.Join("; ", Adapters)}], {Monitors} monitor(s) {TotalPixels / 1_000_000.0:0.0} Mpx, battery={OnBattery}";
    }
}

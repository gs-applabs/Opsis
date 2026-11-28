using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MotionDesk.Services
{
    /// <summary>
    /// Single lightweight source of system metrics for the whole application.
    /// Widgets consume cached samples instead of creating their own PerformanceCounter instances.
    /// </summary>
    public sealed class SystemMonitorService : IDisposable
    {
        private static readonly Lazy<SystemMonitorService> _lazy = new(() => new SystemMonitorService());
        public static SystemMonitorService Instance => _lazy.Value;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private SystemMetrics _cached;
        private long _sampledAt = -500;
        private ulong _lastIdle;
        private ulong _lastKernel;
        private ulong _lastUser;
        private double _cpuPercent;
        private readonly object _sync = new();

        private SystemMonitorService()
        {
            if (ReadSystemTimes(out var idle, out var kernel, out var user))
            {
                _lastIdle = idle;
                _lastKernel = kernel;
                _lastUser = user;
            }
        }

        public SystemMetrics GetSnapshot()
        {
            lock (_sync)
            {
                long now = _clock.ElapsedMilliseconds;
                if (now - _sampledAt < 500) return _cached;
                if (ReadSystemTimes(out var idle, out var kernel, out var user))
                {
                    _cpuPercent = CalculateCpuPercent(_lastIdle, _lastKernel, _lastUser,
                        idle, kernel, user, _cpuPercent);

                    _lastIdle = idle;
                    _lastKernel = kernel;
                    _lastUser = user;
                }

                var memory = GetMemoryStatus();
                _sampledAt = now;
                return _cached = new SystemMetrics(
                    CpuPercent: Math.Round(_cpuPercent, 1),
                    AvailableMemoryMb: memory.AvailableMb,
                    TotalMemoryMb: memory.TotalMb,
                    Timestamp: DateTime.Now);
            }
        }

        internal static double CalculateCpuPercent(ulong previousIdle, ulong previousKernel, ulong previousUser,
            ulong idle, ulong kernel, ulong user, double previous)
        {
            // Resume, driver/system counter reset: do not underflow unsigned
            // deltas or feed false high-load signals to the governor.
            if (idle < previousIdle || kernel < previousKernel || user < previousUser) return 0;
            double total = (double)(kernel - previousKernel) + (user - previousUser);
            return total > 0 ? Math.Clamp((1.0 - (idle - previousIdle) / total) * 100.0, 0, 100) : previous;
        }

        private static (double AvailableMb, double TotalMb) GetMemoryStatus()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status))
                return (0, 0);

            const double mb = 1024d * 1024d;
            return (status.ullAvailPhys / mb, status.ullTotalPhys / mb);
        }

        public void Dispose() { }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        private static ulong ToUInt64(FILETIME time) =>
            ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        private static bool ReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
        {
            idle = kernel = user = 0;
            if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
                return false;

            idle = ToUInt64(idleTime);
            kernel = ToUInt64(kernelTime);
            user = ToUInt64(userTime);
            return true;
        }
    }

    public readonly record struct SystemMetrics(
        double CpuPercent,
        double AvailableMemoryMb,
        double TotalMemoryMb,
        DateTime Timestamp);
}

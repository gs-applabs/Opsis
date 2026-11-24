using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MotionDesk.Services
{
    /// <summary>Native Windows controls for P0 mouse and screen saver; no shell hooks.</summary>
    public static class DesktopEffects
    {
        private const uint GetMouseTrails = 0x005E;
        private const uint SetMouseTrails = 0x005D;
        private const uint PersistAndBroadcast = 0x03;
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Spi(uint action, uint value, ref int data, uint flags);

        public static int GetMouseTrails()
        {
            int value = 0;
            if (!Spi(GetMouseTrails, 0, ref value, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return value;
        }
        public static void SetMouseTrails(int count)
        {
            // 0 disables Windows native pointer trails; 2-7 produce progressively longer trails.
            if (count != 0 && (count < 2 || count > 7))
                throw new ArgumentOutOfRangeException(nameof(count));
            int unused = 0;
            if (!Spi(SetMouseTrails, (uint)count, ref unused, PersistAndBroadcast))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public static bool CanPreviewSystemScreenSaver()
        {
            return ResolveSystemScreenSaver() != null;
        }

        public static void PreviewSystemScreenSaver()
        {
            var file = ResolveSystemScreenSaver()
                ?? throw new InvalidOperationException("No built-in system screen saver is configured.");
            // Never execute arbitrary paths read from the user's registry.
            Process.Start(new ProcessStartInfo(file, "/s") { UseShellExecute = false });
        }

        private static string? ResolveSystemScreenSaver()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var configured = (key?.GetValue("SCRNSAVE.EXE") as string)?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(configured)) return null;
            var system32 = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System));
            var full = Path.GetFullPath(Path.IsPathRooted(configured)
                ? configured : Path.Combine(system32, configured));
            // Only Microsoft system-directory savers, never paths in user-writable locations.
            if (!string.Equals(Path.GetDirectoryName(full), system32, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(full), ".scr", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(full)) return null;
            return full;
        }

        public static void OpenMouseSettings() => Open("ms-settings:mousetouchpad");
        public static void OpenScreenSaverSettings()
        {
            Process.Start(new ProcessStartInfo("control.exe", "desk.cpl,,@screensaver") { UseShellExecute = true });
        }
        private static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}

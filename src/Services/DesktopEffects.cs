using System;
using System.Diagnostics;
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

        public static void OpenMouseSettings() => Open("ms-settings:mousetouchpad");
        public static void OpenScreenSaverSettings()
        {
            Process.Start(new ProcessStartInfo("control.exe", "desk.cpl,,@screensaver") { UseShellExecute = true });
        }
        private static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}

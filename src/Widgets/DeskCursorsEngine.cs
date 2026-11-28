using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MotionDesk.Services;
using System.IO;
using System.Linq;

namespace MotionDesk.Widgets
{
    // "DeskCursors" — διαχειριστής δείκτη ποντικιού μέσω του επίσημου registry μηχανισμού των
    // ίδιων των Windows (HKCU\Control Panel\Cursors + SystemParametersInfo), όχι τρίτου κώδικα.
    // Οι ρόλοι/ονόματα εδώ επαληθεύτηκαν έναντι πραγματικού registry state στη μηχανή-στόχο πριν
    // γραφτούν (Arrow/IBeam/Hand/Wait/AppStarting/No — οι πιο συχνά αλλαγμένοι ρόλοι σε ένα scheme).
    internal static class DeskCursorsEngine
    {
        public static readonly (string RegistryName, string LabelKey)[] Roles =
        {
            ("Arrow", "Cursors.RoleArrow"),
            ("IBeam", "Cursors.RoleIBeam"),
            ("Hand", "Cursors.RoleHand"),
            ("Wait", "Cursors.RoleWait"),
            ("AppStarting", "Cursors.RoleAppStarting"),
            ("No", "Cursors.RoleNo"),
        };

        private const string CursorsKeyPath = @"Control Panel\Cursors";

        internal static ShellAssetPack.Slot PackSlot(string role)
        {
            if (!Roles.Any(r => r.RegistryName == role)) throw new InvalidDataException("Unsupported cursor role: " + role);
            return new ShellAssetPack.Slot(CursorsKeyPath, role, "");
        }

        public static string? GetCursor(string registryName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath);
            var v = key?.GetValue(registryName) as string;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static void SetCursor(string registryName, string? path)
        {
            ShellAssetPack.CheckWritable();
            _ = PackSlot(registryName);
            if (!string.IsNullOrEmpty(path)) ShellAssetPack.ValidateLocalAsset(path, true, allowAnimated: true);
            using var key = Registry.CurrentUser.CreateSubKey(CursorsKeyPath);
            if (key == null) return;
            if (string.IsNullOrEmpty(path)) key.DeleteValue(registryName, false);
            else key.SetValue(registryName, path, RegistryValueKind.ExpandString);
        }

        public static void ApplyNow(string schemeName = "Opsis Custom")
        {
            ShellAssetPack.CheckWritable();
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath, true))
                key?.SetValue(string.Empty, schemeName, RegistryValueKind.String);
            RefreshCurrent();
        }

        private static int _refreshRunning, _refreshRequested;
        internal static void RefreshCurrent()
        {
            System.Threading.Interlocked.Exchange(ref _refreshRequested, 1);
            if (System.Threading.Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0) return;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // One worker even if a broadcast is blocked by another app.
                    while (System.Threading.Interlocked.Exchange(ref _refreshRequested, 0) != 0)
                    {
                        try
                        {
                            if (!SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE))
                                RotatingLogger.Write("Cursor reload failed: " + Marshal.GetLastWin32Error());
                        }
                        catch (Exception ex) { RotatingLogger.Write("Cursor reload: " + ex.Message); }
                    }
                }
                finally
                {
                    System.Threading.Volatile.Write(ref _refreshRunning, 0);
                    if (System.Threading.Volatile.Read(ref _refreshRequested) != 0) RefreshCurrent();
                }
            });
        }

        public static void ResetAllToWindowsDefault()
        {
            foreach (var (regName, _) in Roles) SetCursor(regName, null);
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath, true))
                key?.SetValue(string.Empty, "Windows Default", RegistryValueKind.String);
            RefreshCurrent();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_SENDCHANGE = 0x02;
    }
}

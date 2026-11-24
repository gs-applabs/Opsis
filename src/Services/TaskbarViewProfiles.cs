using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    /// <summary>
    /// Safe Windows 11 taskbar presets. HKCU-only, opt-in and reversible.
    /// Does not hook Explorer, patch system binaries or replace Start.
    /// </summary>
    public static class TaskbarViewProfiles
    {
        private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
        private static readonly (string Path, string Name)[] Keys =
        {
            (Advanced, "TaskbarAl"),           // 0 left, 1 center (Windows 11)
            (Advanced, "TaskbarDa"),           // Widgets visibility
            (Advanced, "ShowTaskViewButton"), // Task View visibility
            (Search, "SearchboxTaskbarMode"),   // Search display (build dependent)
            (Advanced, "TaskbarGlomLevel"),
            (Advanced, "MMTaskbarGlomLevel")
        };

        public sealed class SavedValue
        {
            public string Path { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Exists { get; set; }
            public int Value { get; set; }
        }

        public static string BackupPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opsis", "Backups", "taskbar-before-opsis.json");

        public static bool HasBackup => File.Exists(BackupPath);
        public static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        public static void ApplyMinimal()
        {
            if (!IsWindows11) throw new PlatformNotSupportedException("This taskbar profile requires Windows 11.");
            CheckWritable();
            BackupIfMissing();
            Set(Advanced, "TaskbarAl", 0);
            Set(Advanced, "TaskbarDa", 0);
            Set(Advanced, "ShowTaskViewButton", 0);
            Set(Search, "SearchboxTaskbarMode", 0);
            NotifyShell();
        }

        public static void ApplyStandard()
        {
            if (!IsWindows11) throw new PlatformNotSupportedException("This taskbar profile requires Windows 11.");
            CheckWritable();
            BackupIfMissing();
            Set(Advanced, "TaskbarAl", 1);
            Set(Advanced, "TaskbarDa", 1);
            Set(Advanced, "ShowTaskViewButton", 1);
            Set(Search, "SearchboxTaskbarMode", 1);
            NotifyShell();
        }

        /// <summary>Read only known Taskbar preferences; defaults follow Windows 11.</summary>
        public static int ReadOption(string name)
        {
            var index = Array.FindIndex(Keys, entry => entry.Name == name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            using var key = Registry.CurrentUser.OpenSubKey(Keys[index].Path);
            return key?.GetValue(name) is int value ? value : name switch
            {
                "TaskbarAl" => 1,
                "TaskbarDa" => 1,
                "ShowTaskViewButton" => 1,
                "SearchboxTaskbarMode" => 1,
                "TaskbarGlomLevel" or "MMTaskbarGlomLevel" => 0,
                _ => 0
            };
        }

        /// <summary>Change one supported Taskbar option and retain the original P0 backup.</summary>
        public static void ApplyOption(string name, int value)
        {
            if (!IsWindows11) throw new PlatformNotSupportedException("Windows 11 is required.");
            var index = Array.FindIndex(Keys, entry => entry.Name == name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            bool valid = IsValid(name, value);
            if (!valid) throw new ArgumentOutOfRangeException(nameof(value));
            CheckWritable();
            if (ReadOption(name) == value) return;
            BackupIfMissing();
            Set(Keys[index].Path, name, value);
            NotifyShell();
        }

        public static bool IsValid(string name, int value) =>
            Array.FindIndex(Keys, x => x.Name == name) >= 0 &&
            (name == "SearchboxTaskbarMode" ? value is >= 0 and <= 3 :
            name is "TaskbarGlomLevel" or "MMTaskbarGlomLevel" ? value is >= 0 and <= 2 : value is 0 or 1);

        private static void CheckWritable()
        {
            if (!IsWindows11 || SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
                throw new InvalidOperationException("Windows compatibility review required.");
            foreach (var mod in OpsisModCompatibility.ScanCached())
                if (mod.Risk == "High" || mod.Product.StartsWith("Windhawk: ", StringComparison.Ordinal) && mod.Area == "Taskbar")
                    throw new InvalidOperationException("Competing Taskbar customization detected: " + mod.Product);
        }

        private static List<SavedValue> ReadBackup()
        {
            if (new FileInfo(BackupPath).Length > 32768) throw new InvalidDataException("Taskbar backup too large.");
            var values = JsonSerializer.Deserialize<List<SavedValue>>(File.ReadAllText(BackupPath))
                ?? throw new InvalidDataException("Invalid Taskbar backup.");
            if (values.Count < 4 || values.Count > Keys.Length) throw new InvalidDataException("Unexpected Taskbar backup.");
            foreach (var value in values)
                if (Array.FindIndex(Keys, x => x.Path == value.Path && x.Name == value.Name) < 0 ||
                    values.FindAll(x => x.Name == value.Name).Count != 1)
                    throw new InvalidDataException("Invalid Taskbar backup item.");
            foreach (var (_, name) in Keys[..4])
                if (!values.Exists(x => x.Name == name)) throw new InvalidDataException("Missing original Taskbar backup item.");
            return values;
        }

        public static void Restore()
        {
            CheckWritable();
            if (!HasBackup) throw new FileNotFoundException("Taskbar backup missing.", BackupPath);
            foreach (var value in ReadBackup())
            {
                using var key = Registry.CurrentUser.CreateSubKey(value.Path)
                    ?? throw new InvalidOperationException("Could not access Taskbar registry settings.");
                if (value.Exists) key.SetValue(value.Name, value.Value, RegistryValueKind.DWord);
                else key.DeleteValue(value.Name, false);
            }
            NotifyShell();
        }

        private static void BackupIfMissing()
        {
            var values = HasBackup ? ReadBackup() : new List<SavedValue>();
            int originalCount = values.Count;
            foreach (var (path, name) in Keys)
            {
                if (values.Exists(x => x.Path == path && x.Name == name)) continue;
                using var key = Registry.CurrentUser.OpenSubKey(path);
                var raw = key?.GetValue(name);
                if (raw != null && raw is not int) throw new InvalidOperationException("Unexpected Taskbar registry value type: " + name);
                values.Add(new SavedValue { Path = path, Name = name, Exists = raw is int, Value = raw is int i ? i : 0 });
            }
            if (originalCount == values.Count) return;
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            AtomicFile.WriteAllText(BackupPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void Set(string path, string name, int value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path)
                ?? throw new InvalidOperationException("Could not access Taskbar registry settings.");
            key.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static string AutoHideBackup => Path.Combine(AppPaths.AppDataFolder, "native-taskbar-autohide.json");
        private static readonly object AutoHideGate = new();

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint CallbackMessage, Edge;
            public int Left, Top, Right, Bottom;
            public IntPtr Parameter;
        }

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string? windowName);

        private static AppBarData AppBar() => new() { Size = (uint)Marshal.SizeOf<AppBarData>() };
        public static int ReadAutoHide()
        {
            var data = AppBar();
            return (SHAppBarMessage(4, ref data).ToUInt64() & 1) != 0 ? 1 : 0;
        }

        private static void SetAutoHide(int value)
        {
            var data = AppBar();
            // ABM_SETSTATE requires the HWND. Windows 7+ always-on-top is
            // implicit and ABM_GETSTATE no longer returns its bit.
            data.Window = FindWindow("Shell_TrayWnd", null);
            if (data.Window == IntPtr.Zero) throw new InvalidOperationException("Native taskbar is not available.");
            data.Parameter = new IntPtr(2 | value);
            SHAppBarMessage(10, ref data);
            if (ReadAutoHide() != value) throw new InvalidOperationException("Native taskbar did not accept auto-hide change.");
        }

        public static void ApplyAutoHide(int value)
        {
            if (value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value));
            lock (AutoHideGate)
            {
                CheckWritable();
                if (AppSettings.Peek().ReplaceWindowsTaskbar) throw new InvalidOperationException("Turn off Opsis taskbar replacement first.");
                int before = ReadAutoHide();
                if (before == value) return;
                if (!File.Exists(AutoHideBackup))
                    AtomicFile.WriteAllText(AutoHideBackup, JsonSerializer.Serialize(before));
                SetAutoHide(value);
            }
        }

        public static void RestoreAutoHide()
        {
            lock (AutoHideGate)
            {
                CheckWritable();
                if (!File.Exists(AutoHideBackup)) return;
                if (new FileInfo(AutoHideBackup).Length > 32) throw new InvalidDataException("Invalid auto-hide backup.");
                int original = JsonSerializer.Deserialize<int>(AtomicFile.ReadAllText(AutoHideBackup));
                if (original is not (0 or 1)) throw new InvalidDataException("Invalid auto-hide backup.");
                if (AppSettings.Peek().ReplaceWindowsTaskbar) throw new InvalidOperationException("Turn off Opsis taskbar replacement first.");
                if (ReadAutoHide() != original) SetAutoHide(original);
            }
        }

        public static void OpenWindowsStartSettings()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:personalization-start") { UseShellExecute = true });
        }

        public static void OpenWindowsTaskbarSettings()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
        }

        private static void NotifyShell()
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}

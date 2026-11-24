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
            (Search, "SearchboxTaskbarMode")   // Search display (build dependent)
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
                _ => 0
            };
        }

        /// <summary>Change one supported Taskbar option and retain the original P0 backup.</summary>
        public static void ApplyOption(string name, int value)
        {
            if (!IsWindows11) throw new PlatformNotSupportedException("Windows 11 is required.");
            var index = Array.FindIndex(Keys, entry => entry.Name == name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            bool valid = name == "SearchboxTaskbarMode" ? value >= 0 && value <= 3
                : value is 0 or 1;
            if (!valid) throw new ArgumentOutOfRangeException(nameof(value));
            BackupIfMissing();
            Set(Keys[index].Path, name, value);
            NotifyShell();
        }

        public static void Restore()
        {
            if (!HasBackup) throw new FileNotFoundException("Taskbar backup missing.", BackupPath);
            var values = JsonSerializer.Deserialize<List<SavedValue>>(File.ReadAllText(BackupPath))
                         ?? throw new InvalidDataException("Invalid Taskbar backup.");
            if (values.Count != Keys.Length)
                throw new InvalidDataException("Unexpected Taskbar backup.");
            foreach (var (path, name) in Keys)
            {
                var matches = values.FindAll(x => x.Path == path && x.Name == name);
                if (matches.Count != 1) throw new InvalidDataException("Invalid backup item: " + name);
            }
            foreach (var (path, name) in Keys)
            {
                var value = values.Find(x => x.Path == path && x.Name == name)!;
                using var key = Registry.CurrentUser.CreateSubKey(path)
                    ?? throw new InvalidOperationException("Could not access Taskbar registry settings.");
                if (value.Exists) key.SetValue(name, value.Value, RegistryValueKind.DWord);
                else key.DeleteValue(name, false);
            }
            NotifyShell();
        }

        private static void BackupIfMissing()
        {
            if (HasBackup) return;
            var values = new List<SavedValue>();
            foreach (var (path, name) in Keys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(path);
                var raw = key?.GetValue(name);
                if (raw != null && raw is not int)
                    throw new InvalidOperationException("Unexpected Taskbar registry value type: " + name);
                values.Add(new SavedValue { Path = path, Name = name, Exists = raw is int, Value = raw is int i ? i : 0 });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            var temp = BackupPath + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, BackupPath, false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void Set(string path, string name, int value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path)
                ?? throw new InvalidOperationException("Could not access Taskbar registry settings.");
            key.SetValue(name, value, RegistryValueKind.DWord);
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

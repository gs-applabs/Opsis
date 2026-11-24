using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    /// <summary>
    /// P0 Explorer view profiles: reversible, current-user Windows settings only.
    /// No Explorer injection, binary patching, elevation or third-party dependency.
    /// </summary>
    public static class ExplorerViewProfiles
    {
        private const string AdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private static readonly (string Path, string Name)[] Entries =
        {
            (AdvancedPath, "HideFileExt"),
            (AdvancedPath, "Hidden"),
            (AdvancedPath, "UseCompactMode"),
            (AdvancedPath, "LaunchTo")
        };
        public static string BackupPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opsis", "Backups", "explorer-view-before-opsis.json");

        public sealed class ValueSnapshot
        {
            public string Path { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Exists { get; set; }
            public int Value { get; set; }
        }

        public static bool HasBackup => File.Exists(BackupPath);

        public static void BackupIfMissing()
        {
            if (HasBackup) return;
            var values = new List<ValueSnapshot>();
            foreach (var (path, name) in Entries)
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, false);
                var raw = key?.GetValue(name);
                if (raw != null && raw is not int)
                    throw new InvalidOperationException("Unexpected registry value type: " + name);
                values.Add(new ValueSnapshot { Path = path, Name = name, Exists = raw is int, Value = raw is int i ? i : 0 });
            }
            var folder = Path.GetDirectoryName(BackupPath)!;
            Directory.CreateDirectory(folder);
            var temp = BackupPath + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, BackupPath, false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static void ApplyProductivity()
        {
            BackupIfMissing();
            // Keep changes limited to supported HKCU Explorer preferences.
            SetDword(AdvancedPath, "HideFileExt", 0); // show all file extensions
            SetDword(AdvancedPath, "Hidden", 1);      // show hidden items
            SetDword(AdvancedPath, "UseCompactMode", 1);
            SetDword(AdvancedPath, "LaunchTo", 1);    // This PC
            NotifyExplorer();
        }

        /// <summary>Read one of the explicitly supported Explorer settings.</summary>
        public static int ReadOption(string name)
        {
            if (Array.FindIndex(Entries, e => e.Name == name) < 0)
                throw new ArgumentOutOfRangeException(nameof(name));
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedPath);
            return key?.GetValue(name) is int value ? value : name switch
            {
                "HideFileExt" => 1,
                "Hidden" => 2,
                "UseCompactMode" => 0,
                "LaunchTo" => 2,
                _ => 0
            };
        }

        /// <summary>Apply an individual Explorer preference, preserving the P0 backup.</summary>
        public static void ApplyOption(string name, int value)
        {
            bool valid = name switch
            {
                "HideFileExt" or "UseCompactMode" => value is 0 or 1,
                "Hidden" => value is 1 or 2,
                "LaunchTo" => value is 1 or 2,
                _ => false
            };
            if (!valid) throw new ArgumentOutOfRangeException(nameof(value));
            BackupIfMissing();
            SetDword(AdvancedPath, name, value);
            NotifyExplorer();
        }

        public static void Restore()
        {
            if (!HasBackup) throw new FileNotFoundException("No Explorer settings backup found.", BackupPath);
            var values = JsonSerializer.Deserialize<List<ValueSnapshot>>(File.ReadAllText(BackupPath))
                         ?? throw new InvalidDataException("Invalid Explorer backup.");
            if (values.Count != Entries.Length)
                throw new InvalidDataException("Unexpected Explorer backup format.");
            foreach (var (path, name) in Entries)
            {
                var entry = values.Find(x => x.Path == path && x.Name == name);
                if (entry == null) throw new InvalidDataException("Missing backup entry: " + name);
                // Validate all data before writing any registry values.
            }
            foreach (var (path, name) in Entries)
            {
                var entry = values.Find(x => x.Path == path && x.Name == name)!;
                using var key = Registry.CurrentUser.CreateSubKey(path, true)
                    ?? throw new InvalidOperationException("Cannot open Explorer preferences.");
                if (entry.Exists) key.SetValue(name, entry.Value, RegistryValueKind.DWord);
                else key.DeleteValue(name, false);
            }
            NotifyExplorer();
        }

        private static void SetDword(string path, string name, int value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true)
                ?? throw new InvalidOperationException("Cannot open Explorer preferences.");
            key.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static void NotifyExplorer()
        {
            // SHCNE_ASSOCCHANGED; Shell may apply some view changes on next Explorer window.
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            (AdvancedPath, "LaunchTo"),
            (AdvancedPath, "ShowStatusBar"),
            (AdvancedPath, "ShowSyncProviderNotifications"),
            (AdvancedPath, "ShowPreviewHandlers")
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
            var values = HasBackup ? ReadBackup() : new List<ValueSnapshot>();
            int originalCount = values.Count;
            foreach (var (path, name) in Entries)
            {
                if (values.Exists(x => x.Path == path && x.Name == name)) continue;
                using var key = Registry.CurrentUser.OpenSubKey(path, false);
                var raw = key?.GetValue(name);
                if (raw != null && raw is not int)
                    throw new InvalidOperationException("Unexpected registry value type: " + name);
                values.Add(new ValueSnapshot { Path = path, Name = name, Exists = raw is int, Value = raw is int i ? i : 0 });
            }
            if (values.Count == originalCount) return;
            var folder = Path.GetDirectoryName(BackupPath)!;
            Directory.CreateDirectory(folder);
            AtomicFile.WriteAllText(BackupPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void ApplyProductivity()
        {
            ApplyOptions(new Dictionary<string, int>
            {
                ["HideFileExt"] = 0, ["Hidden"] = 1, ["UseCompactMode"] = 1, ["LaunchTo"] = 1
            });
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
                "ShowStatusBar" or "ShowSyncProviderNotifications" or "ShowPreviewHandlers" => 1,
                _ => 0
            };
        }

        /// <summary>Apply an individual Explorer preference, preserving the P0 backup.</summary>
        public static bool IsValid(string name, int value) => name switch
            {
                "HideFileExt" or "UseCompactMode" or "ShowStatusBar" or "ShowSyncProviderNotifications" or "ShowPreviewHandlers" => value is 0 or 1,
                "Hidden" => value is 1 or 2,
                "LaunchTo" => value is 1 or 2,
                _ => false
            };

        private static void CheckWritable()
        {
            if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold) throw new InvalidOperationException("Windows compatibility review required.");
            foreach (var mod in OpsisModCompatibility.ScanCached())
                if (mod.Risk == "High" || mod.Product.StartsWith("Windhawk: ", StringComparison.Ordinal) && mod.Area == "Explorer")
                    throw new InvalidOperationException("Competing Explorer customization detected: " + mod.Product);
        }

        public static void ApplyOption(string name, int value)
        {
            // Preserve the single-option API contract used by legacy callers and ShellSmoke.
            if (Array.FindIndex(Entries, e => e.Name == name) < 0) throw new ArgumentOutOfRangeException(nameof(name));
            if (!IsValid(name, value)) throw new ArgumentOutOfRangeException(nameof(value));
            ApplyOptions(new Dictionary<string, int> { [name] = value });
        }

        // Validate the entire selection before any write. Preserve exact prior
        // value presence, not the display default; one Shell notification per batch.
        public static void ApplyOptions(IReadOnlyDictionary<string, int> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var requested = options.ToArray();
            if (requested.Length > Entries.Length || requested.Any(x => !IsValid(x.Key, x.Value)))
                throw new ArgumentException("Unsupported Explorer preference.", nameof(options));
            CheckWritable();
            var before = new List<ValueSnapshot>();
            using (var read = Registry.CurrentUser.OpenSubKey(AdvancedPath))
            {
                foreach (var (name, value) in requested)
                {
                    var raw = read?.GetValue(name);
                    if (raw != null && raw is not int) throw new InvalidOperationException("Unexpected registry value type: " + name);
                    if (ReadOption(name) == value) continue;
                    before.Add(new ValueSnapshot { Path = AdvancedPath, Name = name, Exists = raw is int, Value = raw is int i ? i : 0 });
                }
            }
            if (before.Count == 0) return;
            BackupIfMissing();
            using var key = Registry.CurrentUser.CreateSubKey(AdvancedPath, true)
                ?? throw new InvalidOperationException("Cannot open Explorer preferences.");
            var applied = new List<ValueSnapshot>();
            try
            {
                foreach (var prior in before)
                {
                    if (WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold || SafeModeService.IsSafeMode)
                        throw new InvalidOperationException("Compatibility review required.");
                    int value = requested.First(x => x.Key == prior.Name).Value;
                    if (!Equals(key.GetValue(prior.Name), prior.Exists ? (object)prior.Value : null))
                        throw new InvalidOperationException("Explorer preference changed outside Opsis: " + prior.Name);
                    applied.Add(prior);
                    key.SetValue(prior.Name, value, RegistryValueKind.DWord);
                    if (!Equals(key.GetValue(prior.Name), value)) throw new IOException("Explorer preference could not be verified: " + prior.Name);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var prior in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (prior.Exists) key.SetValue(prior.Name, prior.Value, RegistryValueKind.DWord);
                        else key.DeleteValue(prior.Name, false);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                }
                NotifyExplorer();
                if (errors.Count > 1) throw new AggregateException("Explorer settings update and rollback failed; original backup retained.", errors);
                throw;
            }
            NotifyExplorer();
        }

        private static List<ValueSnapshot> ReadBackup()
        {
            if (new FileInfo(BackupPath).Length > 32768) throw new InvalidDataException("Explorer backup too large.");
            var values = JsonSerializer.Deserialize<List<ValueSnapshot>>(File.ReadAllText(BackupPath))
                ?? throw new InvalidDataException("Invalid Explorer backup.");
            if (values.Count < 4 || values.Count > Entries.Length) throw new InvalidDataException("Unexpected Explorer backup format.");
            foreach (var value in values)
                if (value.Path != AdvancedPath || Array.FindIndex(Entries, x => x.Name == value.Name) < 0 ||
                    values.FindAll(x => x.Name == value.Name).Count != 1)
                    throw new InvalidDataException("Invalid Explorer backup item.");
            foreach (var (_, name) in Entries[..4])
                if (!values.Exists(x => x.Name == name)) throw new InvalidDataException("Missing original Explorer backup item.");
            return values;
        }

        public static void Restore()
        {
            CheckWritable();
            if (!HasBackup) throw new FileNotFoundException("No Explorer settings backup found.", BackupPath);
            foreach (var entry in ReadBackup())
            {
                using var key = Registry.CurrentUser.CreateSubKey(entry.Path, true)
                    ?? throw new InvalidOperationException("Cannot open Explorer preferences.");
                if (entry.Exists) key.SetValue(entry.Name, entry.Value, RegistryValueKind.DWord);
                else key.DeleteValue(entry.Name, false);
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

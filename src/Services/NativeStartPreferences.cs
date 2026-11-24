using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace MotionDesk.Services;

/// <summary>Documented Windows Start preferences only. No process injection, policy
/// override, pinned database edits or Explorer restart. Values may be ignored by
/// particular Windows builds or policy; confirm in the Windows Start UI.</summary>
public static class NativeStartPreferences
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Start = @"Software\Microsoft\Windows\CurrentVersion\Start";
    private static readonly (string Id, string Path, int Max)[] Options =
    {
        ("Start_Layout", Advanced, 2),
        ("ShowAllPinsList", Start, 1),
        ("Start_IrisRecommendations", Advanced, 1)
    };
    private static readonly object Gate = new();
    private static string BackupPath => Path.Combine(AppPaths.AppDataFolder, "native-start-original.json");
    public static bool HasBackup => File.Exists(BackupPath);
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    public static bool IsValid(string id, int value) =>
        Options.Any(x => x.Id == id && value >= 0 && value <= x.Max);

    private static void CheckWritable()
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Windows 11 is required.");
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
            throw new InvalidOperationException("Windows compatibility review required.");
        var mods = OpsisModCompatibility.ScanCached();
        if (mods.Any(x => x.Risk == "High" ||
            (x.Product.StartsWith("Windhawk: ", StringComparison.Ordinal) && x.Area == "Start")))
            throw new InvalidOperationException("Competing Start customization detected. Review it before applying Opsis preferences.");
    }

    public static Dictionary<string, int?> Read()
    {
        var values = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var entry in Options)
        {
            using var key = Registry.CurrentUser.OpenSubKey(entry.Path, false);
            var raw = key?.GetValue(entry.Id);
            if (raw != null && (raw is not int v || !IsValid(entry.Id, v)))
                throw new InvalidDataException("Unsupported current Start preference: " + entry.Id);
            values.Add(entry.Id, raw is int value ? value : null);
        }
        return values;
    }

    private static void Write(string id, int? value)
    {
        var entry = Options.Single(x => x.Id == id);
        if (value == null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(entry.Path, true);
            key?.DeleteValue(entry.Id, false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.Path);
            key.SetValue(entry.Id, value.Value, RegistryValueKind.DWord);
        }
    }

    private static void ApplyChanges(Dictionary<string, int?> before, Dictionary<string, int?> changes)
    {
        var written = new List<string>();
        try
        {
            foreach (var pair in changes)
            {
                if (before[pair.Key] == pair.Value) continue;
                written.Add(pair.Key);
                Write(pair.Key, pair.Value);
            }
        }
        catch
        {
            foreach (var id in written.AsEnumerable().Reverse())
                try { Write(id, before[id]); }
                catch (Exception ex) { RotatingLogger.Write("Native Start rollback failed: " + id + ": " + ex.Message); }
            throw;
        }
    }

    public static void Apply(Dictionary<string, int> changes)
    {
        if (changes.Any(p => !IsValid(p.Key, p.Value))) throw new InvalidDataException("Invalid Start preference.");
        lock (Gate)
        {
            CheckWritable();
            var before = Read();
            if (changes.All(p => before[p.Key] == p.Value)) return;
            if (!HasBackup)
                AtomicFile.WriteAllText(BackupPath, JsonSerializer.Serialize(before));
            ApplyChanges(before, changes.ToDictionary(p => p.Key, p => (int?)p.Value, StringComparer.Ordinal));
        }
    }

    public static void Restore()
    {
        lock (Gate)
        {
            CheckWritable();
            if (!HasBackup || new FileInfo(BackupPath).Length > 4096)
                throw new InvalidDataException("Start backup unavailable or invalid.");
            var saved = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(BackupPath))
                ?? throw new InvalidDataException("Invalid Start backup.");
            if (saved.Count != Options.Length || Options.Any(x => !saved.ContainsKey(x.Id)) ||
                saved.Any(p => p.Value.HasValue && !IsValid(p.Key, p.Value.Value)))
                throw new InvalidDataException("Invalid Start backup values.");
            ApplyChanges(Read(), saved);
        }
    }

    public static void OpenWindowsSettings(bool folders = false)
    {
        Process.Start(new ProcessStartInfo(folders ? "ms-settings:personalization-start-places" : "ms-settings:personalization-start") { UseShellExecute = true });
    }
}

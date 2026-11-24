using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services;

/// <summary>Optional adapter to an already installed, compatible Windhawk CLI.
/// No bundled mods, installs, updates, elevation, injection, arbitrary commands,
/// XAML/JS evaluation, or direct writes to another product's storage.</summary>
public static class NativeShellThemeBridge
{
    public sealed record Target(string Id, string Label, string[] Themes);
    public static IReadOnlyList<Target> Targets { get; } = Array.AsReadOnly(new[]
    {
        new Target("windows-11-start-menu-styler", "Start Menu", new[] { "", "TranslucentStartMenu", "NoRecommendedSection", "SideBySide", "Windows10", "WindowGlass", "LayerMicaUI", "Borderless", "FullScreen" }),
        new Target("windows-11-file-explorer-styler", "File Explorer", new[] { "", "Translucent Explorer11", "MicaBar", "NoCommandBar", "Minimal Explorer11", "Tabless", "WindowGlass", "AddressSearchOnly", "TintedGlass", "LiquidGlass" }),
        new Target("windows-11-taskbar-styler", "Taskbar", new[] { "", "TranslucentTaskbar", "DockLike", "SimplyTransparent", "Squircle", "WinXP", "WinVista", "CleanSlate", "TaskbarXII" }),
        new Target("windows-11-notification-center-styler", "Notification Center + Quick Settings", new[] { "", "TranslucentShell", "Matter", "Unified", "WindowGlass", "TintedGlass", "Fluid", "LiquidGlass", "BetterControl11", "LayerMicaUI", "Borderless", "Densy", "FrostyGlass" })
    });

    private static string BackupFile => Path.Combine(AppPaths.AppDataFolder, "native-shell-theme-backups.json");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool IsKnownTheme(string id, string theme) =>
        Targets.Any(x => x.Id == id && x.Themes.Contains(theme, StringComparer.Ordinal));
    private static bool KnownId(string id) => Targets.Any(x => x.Id == id);
    private static bool SafeStoredTheme(string? theme) => theme != null && theme.Length <= 128 && !theme.Any(char.IsControl);

    public static string? FindCli()
    {
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(special);
            if (string.IsNullOrEmpty(root)) continue;
            var directory = Path.Combine(root, "Windhawk");
            var path = Path.Combine(directory, "windhawk-cli.exe");
            if (File.Exists(path) && File.Exists(Path.Combine(directory, "windhawk.ini")) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                return path;
        }
        return null;
    }

    private static void EnsureWritable(string id)
    {
        if (!KnownId(id)) throw new ArgumentOutOfRangeException(nameof(id));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
            throw new InvalidOperationException("Windows 11 compatibility review is required.");
        if (id == "windows-11-taskbar-styler" && AppSettings.Peek().ReplaceWindowsTaskbar)
            throw new InvalidOperationException("Turn off the Opsis replacement taskbar before using native Taskbar Styler.");
        if (OpsisModCompatibility.ScanCached().Any(x => x.Risk == "High"))
            throw new InvalidOperationException("A competing shell replacement is installed. Resolve it first.");
    }

    // Parse only the official schema-1 CLI envelope, never execute or render it.
    public static string ParseTheme(string json, string id)
    {
        if (!KnownId(id) || json.Length > 65536) throw new InvalidDataException("Invalid CLI response.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || !root.GetProperty("success").GetBoolean())
            throw new InvalidDataException("Unsupported Windhawk CLI response.");
        var data = root.GetProperty("data");
        if (data.GetProperty("id").GetString() != id || data.GetProperty("key").GetString() != "theme")
            throw new InvalidDataException("Unexpected Windhawk theme response.");
        var value = data.GetProperty("value");
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Theme is not set. Select an initial theme in Windhawk before using Opsis.");
        var result = value.GetString();
        if (!SafeStoredTheme(result)) throw new InvalidDataException("Unsupported theme value.");
        return result!;
    }

    private static async Task<string> Run(string id, string? theme = null)
    {
        if (!KnownId(id) || (theme != null && !SafeStoredTheme(theme))) throw new ArgumentException("Invalid theme request.");
        var cli = FindCli() ?? throw new FileNotFoundException("Compatible windhawk-cli.exe was not found in the installed Windhawk folder.");
        var info = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(cli)! };
        foreach (var token in new[] { "--json", "--quiet", "mod", "settings", theme == null ? "get" : "set", id })
            info.ArgumentList.Add(token);
        info.ArgumentList.Add(theme == null ? "theme" : "theme=" + theme);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windhawk CLI could not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0 || output.Length > 65536 || error.Length > 65536)
                throw new InvalidOperationException("Windhawk CLI rejected the request (exit " + process.ExitCode + "). Confirm the compatible mod is installed.");
            return output;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            throw new TimeoutException("Windhawk CLI timed out. Re-read the actual theme before retrying.");
        }
    }

    public static async Task<string> ReadTheme(string id) => ParseTheme(await Run(id), id);

    private static Dictionary<string, string> ReadBackups()
    {
        if (!File.Exists(BackupFile)) return new();
        if (new FileInfo(BackupFile).Length > 8192) throw new InvalidDataException("Native shell theme backup oversized.");
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(AtomicFile.ReadAllText(BackupFile))
            ?? throw new InvalidDataException("Invalid native theme backup.");
        if (values.Any(x => !KnownId(x.Key) || !SafeStoredTheme(x.Value))) throw new InvalidDataException("Invalid native theme backup entry.");
        return values;
    }

    public static async Task<string> Apply(string id, string theme)
    {
        if (!IsKnownTheme(id, theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        await Gate.WaitAsync();
        try
        {
            EnsureWritable(id);
            var current = await ReadTheme(id);
            if (current == theme) return current;
            var backups = ReadBackups();
            if (!backups.ContainsKey(id))
            {
                backups.Add(id, current);
                AtomicFile.WriteAllText(BackupFile, JsonSerializer.Serialize(backups));
            }
            EnsureWritable(id);
            await Run(id, theme);
            var actual = await ReadTheme(id);
            if (actual != theme) throw new InvalidOperationException("Theme readback differs; restore the original if necessary.");
            return actual;
        }
        finally { Gate.Release(); }
    }

    public static async Task<string> Restore(string id)
    {
        await Gate.WaitAsync();
        try
        {
            EnsureWritable(id);
            if (!ReadBackups().TryGetValue(id, out var original)) throw new InvalidOperationException("No original theme backup for this mod.");
            if (await ReadTheme(id) != original) await Run(id, original);
            var actual = await ReadTheme(id);
            if (actual != original) throw new InvalidOperationException("Theme restore readback differs.");
            return actual;
        }
        finally { Gate.Release(); }
    }
}

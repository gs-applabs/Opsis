using System.Diagnostics;
using Microsoft.Win32;

namespace Opsis.Lockscreen;

/// <summary>Runs icacls.exe / takeown.exe (System32 only, arguments passed as a list, never through a shell). Replaced by a fake in the tests.</summary>
public interface IAclTool
{
    int Run(string tool, IReadOnlyList<string> args);
}

public sealed class SystemAclTool : IAclTool
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "icacls.exe", "takeown.exe" };

    public int Run(string tool, IReadOnlyList<string> args)
    {
        if (!Allowed.Contains(tool)) throw new ArgumentException("Tool not allowed: " + tool);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException(tool + " did not start");
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000)) { try { p.Kill(true); } catch (Exception) { } throw new TimeoutException(tool + " timed out"); }
        Task.WaitAll(o, e);
        return p.ExitCode;
    }
}

/// <summary>The Windows feature override that stops animated lock-screen images (LockscreenGif documents ID 38943831 on build 26200+). Backed up before it is changed.</summary>
public interface IFeatureStore
{
    FeatureBackup Capture();
    void SetDisabled();
    void Restore(FeatureBackup original);
    /// <summary>true = override says Disabled, false = not disabled, null = unknown.</summary>
    bool? IsDisabled();
}

public sealed class RegistryFeatureStore : IFeatureStore
{
    private readonly RegistryKey _hive; private readonly string _path;
    /// <param name="hive">Registry.LocalMachine in production; any key in tests.</param>
    public RegistryFeatureStore(RegistryKey hive, string? path = null) { _hive = hive; _path = path ?? LockscreenPaths.FeatureKeyPath; }

    public FeatureBackup Capture()
    {
        using var k = _hive.OpenSubKey(_path);
        return k == null ? new FeatureBackup { Captured = true, KeyExisted = false }
            : new FeatureBackup { Captured = true, KeyExisted = true, EnabledState = k.GetValue("EnabledState") as int?, EnabledStateOptions = k.GetValue("EnabledStateOptions") as int? };
    }

    public void SetDisabled()
    {
        using var k = _hive.CreateSubKey(_path, true) ?? throw new IOException("Cannot open the feature override key");
        k.SetValue("EnabledState", 1, RegistryValueKind.DWord);          // 1 = Disabled
        k.SetValue("EnabledStateOptions", 0, RegistryValueKind.DWord);
    }

    public void Restore(FeatureBackup o)
    {
        if (!o.Captured) return;
        if (!o.KeyExisted) { try { _hive.DeleteSubKeyTree(_path, false); } catch (Exception) { } return; }
        using var k = _hive.CreateSubKey(_path, true)!;
        Set(k, "EnabledState", o.EnabledState); Set(k, "EnabledStateOptions", o.EnabledStateOptions);
    }

    private static void Set(RegistryKey k, string name, int? v) { if (v is int i) k.SetValue(name, i, RegistryValueKind.DWord); else k.DeleteValue(name, false); }

    public bool? IsDisabled()
    {
        try { using var k = _hive.OpenSubKey(_path); if (k == null) return null; return k.GetValue("EnabledState") is int s ? s == 1 : null; }
        catch (Exception) { return null; }
    }
}

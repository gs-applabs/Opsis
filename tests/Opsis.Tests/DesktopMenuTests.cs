using System.Text.Json.Nodes;
using MotionDesk.Services;
using Opsis.Containers;
using Xunit;

namespace Opsis.Tests;

public sealed class DesktopMenuTests
{
    [Fact]
    public void OnlyWhitelistedActionsAreAcceptedThroughThePipe()
    {
        var name = "opsis-control-test-" + Environment.ProcessId;
        using var server = new PipeServer(name, new DesktopMenu.Api()); server.Start();
        var ok = PipeClient.Send(name, "ping");
        Assert.True(ok["ok"]!.GetValue<bool>());
        var bad = PipeClient.Send(name, "format-c");
        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("unknown-command", bad["error"]!["code"]!.ToString());
    }

    [Fact]
    public void ActionsOfTheRegistryVerbsAreAllAllowed()
    {
        foreach (var a in new[] { "new-container", "next-video" }) Assert.Contains(a, DesktopMenu.Allowed);
    }
}

public sealed class DesktopMenuRegistryTests : IDisposable
{
    private const string Hive = @"Software\OpsisTests\DesktopMenu";
    private readonly Microsoft.Win32.RegistryKey _root;
    public DesktopMenuRegistryTests()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Hive + Environment.ProcessId, false);
        _root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Hive + Environment.ProcessId);
    }
    public void Dispose() { _root.Dispose(); Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Hive + Environment.ProcessId, false); }

    [Fact]
    public void RegisterWritesTwoClassicVerbsAndUnregisterRemovesThemAll()
    {
        Assert.False(DesktopMenu.IsRegistered(_root));
        DesktopMenu.Register(@"C:\x\Opsis.exe", _root);
        Assert.True(DesktopMenu.IsRegistered(_root));
        var paths = DesktopMenu.VerbKeyPaths.ToList();
        Assert.Equal(2, paths.Count);
        foreach (var p in paths)
        {
            using var k = _root.OpenSubKey(p)!; Assert.NotNull(k);
            Assert.False(string.IsNullOrWhiteSpace((string?)k.GetValue("MUIVerb")));
            using var c = k.OpenSubKey("command")!;
            Assert.Contains("--desktop-action", (string)c.GetValue("")!);
            Assert.Contains(((string)c.GetValue("")!).Split(' ').Last(), DesktopMenu.Allowed);
        }
        DesktopMenu.Unregister(_root);
        Assert.False(DesktopMenu.IsRegistered(_root));
        foreach (var p in paths) Assert.Null(_root.OpenSubKey(p));
        DesktopMenu.Unregister(_root); // second call must not throw
    }

    [Fact]
    public void InstallerRemovesExactlyTheKeysTheCodeWrites()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "installer.iss"))) d = d.Parent;
        var iss = File.ReadAllText(Path.Combine(d!.FullName, "installer.iss"));
        foreach (var p in DesktopMenu.VerbKeyPaths) Assert.Contains("Subkey: \"" + p + "\"", iss);
    }
}

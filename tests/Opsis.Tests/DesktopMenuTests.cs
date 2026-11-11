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

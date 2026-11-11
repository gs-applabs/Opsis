using System.Text.Json.Nodes;
using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class ServiceTests
{
    private static ContainersService New(out string file)
    {
        file = Path.Combine(Path.GetTempPath(), "opsis-svc-" + Guid.NewGuid().ToString("N") + ".json");
        return new ContainersService(new ContainersStore(file), () => new[] { new MonitorInfo("D1", new System.Drawing.Rectangle(0, 0, 1920, 1080), true) });
    }

    [Fact]
    public void ShowHideChangeHiddenAndRaiseMutated()
    {
        var s = New(out var file); var raised = 0; s.Mutated += () => raised++;
        try
        {
            var id = s.Handle("create", new JsonObject { ["title"] = "A" })!["id"]!.ToString();
            s.Handle("hide", new JsonObject { ["id"] = id }); Assert.True(s.Snapshot().Containers[0].Hidden);
            s.Handle("show", new JsonObject { ["id"] = id }); Assert.False(s.Snapshot().Containers[0].Hidden);
            Assert.Equal(3, raised);
            s.Handle("list", new JsonObject()); Assert.Equal(3, raised);   // read-only commands do not raise it
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void SnapshotSaveListLoadKeepsTheRulesOfTheInstallation()
    {
        var s = New(out var file); var dir = Path.Combine(Path.GetDirectoryName(file)!, "container-snapshots");
        try
        {
            s.Handle("create", new JsonObject { ["title"] = "A" });
            s.Handle("snapshot-save", new JsonObject { ["name"] = "work" });
            s.Update(d => { d.Containers.Clear(); d.Rules.Add(new Rule { Name = "keep me" }); return true; });
            Assert.Equal("work", ((JsonArray)s.Handle("snapshot-list", new JsonObject())!)[0]!.ToString());
            s.Handle("snapshot-load", new JsonObject { ["name"] = "work" });
            var d2 = s.Snapshot(); Assert.Single(d2.Containers); Assert.Equal("keep me", d2.Rules[0].Name);
            Assert.Throws<PipeException>(() => s.Handle("snapshot-load", new JsonObject { ["name"] = "nope" }));
        }
        finally { File.Delete(file); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void UpdateWritesOnlyWhenTheChangeSaysSo()
    {
        var s = New(out var file);
        try
        {
            s.Handle("create", new JsonObject { ["title"] = "A" });
            var before = File.GetLastWriteTimeUtc(file);
            s.Update(_ => false); Assert.Equal(before, File.GetLastWriteTimeUtc(file));
            s.Update(d => { d.Containers[0].Title = "B"; return true; }); Assert.Equal("B", s.Snapshot().Containers[0].Title);
        }
        finally { File.Delete(file); }
    }
}

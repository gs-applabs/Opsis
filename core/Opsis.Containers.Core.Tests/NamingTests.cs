using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("", null)] [InlineData("   ", null)] [InlineData(null, null)] [InlineData("A", null)]   // empty, blank, cancelled (Esc), unchanged
    [InlineData("  Games  ", "Games")] [InlineData("Wo\trk\n", "Work")]
    public void RejectsEmptyAndUnchangedAndCleansTheRest(string? typed, string? expected) =>
        Assert.Equal(expected, ContainerNaming.Normalize(typed, "A", new[] { "B" }));

    [Fact]
    public void DuplicateNamesAreMadeUniqueCaseInsensitively()
    {
        Assert.Equal("work (2)", ContainerNaming.Normalize("work", "A", new[] { "Work" }));
        Assert.Equal("Work (3)", ContainerNaming.Normalize("Work", "A", new[] { "Work", "Work (2)" }));
    }

    [Fact]
    public void LongNamesAreCut() => Assert.Equal(ContainerNaming.MaxLength, ContainerNaming.Normalize(new string('x', 200), "A", Array.Empty<string>())!.Length);

    [Fact]
    public void RenameShowsInTheStoredListAndSurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "opsis-naming-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var svc = new ContainersService(new ContainersStore(path), () => new[] { new MonitorInfo("M", new System.Drawing.Rectangle(0, 0, 1920, 1080), true) });
            var a = svc.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = "One" })!["id"]!.ToString();
            svc.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = "Two" });
            svc.Update(d => ContainerRename.Apply(d, a, "Two"));
            var again = new ContainersService(new ContainersStore(path), () => new[] { new MonitorInfo("M", new System.Drawing.Rectangle(0, 0, 1920, 1080), true) });      // "restart"
            Assert.Equal(new[] { "Two (2)", "Two" }, again.Snapshot().Containers.Select(c => c.Title).ToArray());
            Assert.False(ContainerRename.Apply(again.Snapshot(), a, "  "));
        }
        finally { try { File.Delete(path); File.Delete(path + ".bak"); } catch (Exception) { } }
    }

    [Fact]
    public void StyleKeyChangesOnlyWhenWhatIsPaintedChanges()
    {
        var c = new Container { Title = "T" }; var k = ContainerStyleKey.Of(c);
        c.Geometry.X = 500; c.Items.Add(new ContainerItem { Path = "x" });                  // position / items do not repaint through ApplyStyle
        Assert.Equal(k, ContainerStyleKey.Of(c));
        c.TitleBar.Opacity = 50; Assert.NotEqual(k, ContainerStyleKey.Of(c));
        k = ContainerStyleKey.Of(c); c.Title = "U"; Assert.NotEqual(k, ContainerStyleKey.Of(c));
        k = ContainerStyleKey.Of(c); c.Appearance.Glossy = true; Assert.NotEqual(k, ContainerStyleKey.Of(c));
    }
}

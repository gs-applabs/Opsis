using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class RulesTests
{
    private static RuleContext Ctx(int mon = 1, string[]? procs = null, string time = "12:00", bool fs = false) => new(mon, new HashSet<string>(procs ?? Array.Empty<string>()), TimeSpan.Parse(time), fs);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    [Fact]
    public void FiresOnlyWhenTheConditionTurnsTrueAndRevertsWhenItStops()
    {
        var r = new Rule { Id = "r", When = RuleWhen.ProcessRunning, Value = "game.exe", Do = RuleDo.HideContainers };
        var (a1, act1) = RuleEngine.Evaluate(new[] { r }, Ctx(procs: new[] { "game.exe" }), None);
        Assert.Single(a1); Assert.Equal(RuleDo.HideContainers, a1[0].Do);
        var (a2, act2) = RuleEngine.Evaluate(new[] { r }, Ctx(procs: new[] { "game.exe" }), act1);
        Assert.Empty(a2);
        var (a3, _) = RuleEngine.Evaluate(new[] { r }, Ctx(), act2);
        Assert.Single(a3); Assert.Equal(RuleDo.ShowContainers, a3[0].Do);
    }

    [Theory]
    [InlineData("23:30", true)]
    [InlineData("03:00", true)]
    [InlineData("12:00", false)]
    [InlineData("07:00", false)]
    public void TimeRangeMayWrapMidnight(string t, bool expected) =>
        Assert.Equal(expected, RuleEngine.Holds(new Rule { When = RuleWhen.TimeBetween, Value = "22:00-07:00" }, Ctx(time: t)));

    [Fact]
    public void MonitorCountAndDisabledRules()
    {
        Assert.True(RuleEngine.Holds(new Rule { When = RuleWhen.MonitorCount, Value = "2" }, Ctx(mon: 2)));
        Assert.False(RuleEngine.Holds(new Rule { When = RuleWhen.MonitorCount, Value = "2" }, Ctx(mon: 1)));
        var (a, _) = RuleEngine.Evaluate(new[] { new Rule { Enabled = false, When = RuleWhen.FullscreenApp } }, Ctx(fs: true), None);
        Assert.Empty(a);
    }

    [Fact]
    public void ApplyHidesOnlyTheTargets()
    {
        var doc = new ContainersDocument(); doc.Containers.Add(new Container { Id = "a" }); doc.Containers.Add(new Container { Id = "b" });
        Assert.True(RuleEngine.Apply(doc, new RuleAction("r", RuleDo.HideContainers, new[] { "a" })));
        Assert.True(doc.Containers[0].Hidden); Assert.False(doc.Containers[1].Hidden);
        Assert.False(RuleEngine.Apply(doc, new RuleAction("r", RuleDo.HideContainers, new[] { "a" })));
    }

    [Fact]
    public void SnapshotsRoundTripAndNamesCannotEscapeTheFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opsis-snap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = new ContainersDocument(); doc.Containers.Add(new Container { Title = "X" }); doc.Rules.Add(new Rule { Name = "n" });
            ContainerSnapshots.Save(doc, dir, "..\\..\\evil/name");
            Assert.Single(Directory.GetFiles(dir));
            var name = ContainerSnapshots.List(dir)[0];
            var back = ContainerSnapshots.Load(dir, name)!;
            Assert.Equal("X", back.Containers[0].Title); Assert.Equal("n", back.Rules[0].Name);
            Assert.True(ContainerSnapshots.Delete(dir, name)); Assert.Empty(ContainerSnapshots.List(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class DropTests
{
    private static ContainerItem I(string p) => new() { Path = p, DisplayName = Path.GetFileNameWithoutExtension(p) };

    [Fact]
    public void DroppedFilesBecomeReferencesAtTheInsertionPoint()
    {
        var existing = new[] { I(@"C:\a\one.exe"), I(@"C:\a\two.exe") };
        var plan = DropRules.Plan(new[] { @"C:\b\new.lnk", @"C:\b\dir" }, existing, 1, p => p.EndsWith("dir"));
        Assert.Equal(DropEffect.AddReference, plan.Effect); Assert.Equal(1, plan.InsertAt); Assert.Equal(2, plan.Add.Count);
        Assert.Equal("new", plan.Add[0].DisplayName); Assert.True(plan.Add[1].IsFolder);
    }

    [Fact]
    public void DuplicatesAndPathsInsideTheOwnFolderAreSkippedCaseInsensitively()
    {
        var existing = new[] { I(@"C:\a\One.exe") };
        var plan = DropRules.Plan(new[] { @"c:\A\one.exe", @"C:\a\one.exe", @"D:\portal\x.txt", @"C:\ok\y.txt" }, existing, 0, null, @"D:\portal");
        Assert.Single(plan.Add); Assert.Equal(@"C:\ok\y.txt", plan.Add[0].Path); Assert.Equal(3, plan.Skipped.Count);
    }

    [Fact]
    public void NothingNewMeansNoEffectAndTheIndexIsClamped()
    {
        Assert.Equal(DropEffect.None, DropRules.Plan(Array.Empty<string>(), Array.Empty<ContainerItem>(), 0).Effect);
        Assert.Equal(DropEffect.None, DropRules.Plan(new[] { "", "  " }, Array.Empty<ContainerItem>(), 0).Effect);
        Assert.Equal(2, DropRules.Plan(new[] { @"C:\z.txt" }, new[] { I("a"), I("b") }, 99).InsertAt);
        Assert.Equal(0, DropRules.Plan(new[] { @"C:\z.txt" }, new[] { I("a"), I("b") }, -5).InsertAt);
    }

    [Theory]
    [InlineData(0, 3, new[] { 1, 2, 0, 3, 4 })]     // move item 0 before item 3 -> lands after item 2
    [InlineData(3, 1, new[] { 0, 3, 1, 2, 4 })]
    [InlineData(2, 2, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(1, 5, new[] { 0, 2, 3, 4, 1 })]     // to the end
    public void ReorderMovesOneItemToTheInsertionPoint(int from, int before, int[] expected)
    {
        var items = Enumerable.Range(0, 5).Select(i => I("C:\\" + i)).ToList();
        var r = DropRules.Reorder(items, from, before);
        Assert.Equal(expected, r.Select(x => int.Parse(x.Path[3..])).ToArray());
        Assert.Equal(5, r.Count);
    }
}

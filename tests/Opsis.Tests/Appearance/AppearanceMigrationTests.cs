using System.Text.Json;
using MotionDesk.Appearance;
using Xunit;

namespace Opsis.Tests.AppearanceEngineTests;

public sealed class AppearanceMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis-migration-" + Guid.NewGuid().ToString("N"));
    public AppearanceMigrationTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

    [Fact]
    public void AnEmptyFolderFindsNothingAndCreatesNothing()
    {
        var r = AppearanceMigration.Scan(_dir);
        Assert.False(r.AnythingFound);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void ExistingFilesAreReportedAndNeverChangedOrDeleted()
    {
        File.WriteAllText(Path.Combine(_dir, "appearance.json"), JsonSerializer.Serialize(new AppearanceSettings { Mode = ThemeMode.AutoSun }));
        File.WriteAllText(Path.Combine(_dir, "appearance-original.json"), "not json");
        Directory.CreateDirectory(Path.Combine(_dir, "themes", "My"));
        File.WriteAllText(Path.Combine(_dir, "themes", "My", "theme.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_dir, "appearance"));
        File.WriteAllText(Path.Combine(_dir, "appearance", "suite.json"), "{}");
        var before = Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Select(f => (f, File.ReadAllText(f))).ToList();

        var r = AppearanceMigration.Scan(_dir);

        Assert.True(r.AnythingFound);
        Assert.True(r.Items.First(i => i.What == "Settings").Usable);
        Assert.False(r.Items.First(i => i.What.StartsWith("Original")).Usable);   // damaged: reported, not touched
        Assert.Contains(r.Items, i => i.What.StartsWith("Saved themes (1)"));
        Assert.Contains(@"appearance\suite.json", r.NotMigrated);
        Assert.Equal(before, Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Select(f => (f, File.ReadAllText(f))).ToList());
        Assert.Contains("not migrated", AppearanceMigration.Describe(r));
    }
}

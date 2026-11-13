using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class SettingsMigrationTests
{
    [Fact]
    public void AnEarlierBetaThatSavedFalseGetsTheNativeCoreSwitchedOnOnce()
    {
        var f = Path.Combine(Path.GetTempPath(), "opsis-set-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(f, "{ \"schemaVersion\": 1, \"UseNativeContainers\": false }");
            var s = AppSettings.NewStore(f).Load();
            Assert.True(s.UseNativeContainers);
            AppSettings.NewStore(f).Save(s);
            // the user turns it off afterwards: the migration must not run again
            s.UseNativeContainers = false; AppSettings.NewStore(f).Save(s);
            Assert.False(AppSettings.NewStore(f).Load().UseNativeContainers);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void AFileFromTheOldVersionWithoutTheKeyAlsoGetsTrue()
    {
        var f = Path.Combine(Path.GetTempPath(), "opsis-set-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(f, "{ \"GridSnap\": true }"); Assert.True(AppSettings.NewStore(f).Load().UseNativeContainers); }
        finally { File.Delete(f); }
    }
}

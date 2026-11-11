using MotionDesk.Containers;
using Opsis.Containers;
using Xunit;

namespace Opsis.Tests;

public sealed class NativeMigrationTests
{
    private static readonly IReadOnlyList<MonitorInfo> Mons = new[] { new MonitorInfo("D1", new System.Drawing.Rectangle(0, 0, 1920, 1080), true) };

    [Fact]
    public void ImportsOnlyOnceAndNeverTouchesTheLegacyFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "opsis-mig-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "containers"); var def = Path.Combine(legacy, "Profiles", "Default"); var work = Path.Combine(legacy, "Profiles", "Work");
        Directory.CreateDirectory(def); Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(def, "containers.json"), "[{\"Id\":\"a\",\"Title\":\"One\",\"X\":10,\"Y\":10,\"Width\":300,\"Height\":200,\"Items\":[]}]");
        File.WriteAllText(Path.Combine(work, "containers.json"), "[{\"Id\":\"b\",\"Title\":\"Two\",\"X\":50,\"Y\":50,\"Width\":300,\"Height\":200,\"Items\":[]}]");
        var before = File.GetLastWriteTimeUtc(Path.Combine(def, "containers.json"));
        try
        {
            var store = new ContainersStore(Path.Combine(root, "containers-native.json"));
            var note = NativeMigration.ImportIfNeeded(store, legacy, Mons);
            Assert.StartsWith("legacy import: 2 containers", note);
            Assert.Equal("One", store.Load().Containers[0].Title);
            Assert.Contains("Work", ContainerSnapshots.List(Path.Combine(root, "container-snapshots")));
            Assert.Null(NativeMigration.ImportIfNeeded(store, legacy, Mons));               // second start: the native file exists, nothing happens
            Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(def, "containers.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NoLegacyDataMeansNoFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "opsis-mig-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { var store = new ContainersStore(Path.Combine(root, "n.json")); Assert.Null(NativeMigration.ImportIfNeeded(store, Path.Combine(root, "none"), Mons)); Assert.False(File.Exists(store.Path)); }
        finally { Directory.Delete(root, true); }
    }
}

using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class LockedFileTests
{
    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "opsis-core-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void ALockedFileIsNeverQuarantinedAsCorrupt()
    {
        var path = Path.Combine(Temp(), "c.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"containers\":[]}");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))        // another process holds it exclusively
        {
            var store = new ContainersStore(path);
            Assert.Throws<IOException>(() => store.ReadWithRetry(attempts: 2, delayMs: 10));
            var doc = new ContainersStore(path).Load();                                      // gives up after the retries (about 10 s): an empty document, file untouched
            Assert.Empty(doc.Containers);
        }
        Assert.True(File.Exists(path)); Assert.False(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void ALockReleasedDuringTheRetryIsReadNormally()
    {
        var path = Path.Combine(Temp(), "c.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"profile\":\"Kept\",\"containers\":[]}");
        var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Task.Run(async () => { await Task.Delay(600); hold.Dispose(); });
        Assert.Equal("Kept", new ContainersStore(path).Load().Profile);
    }
}

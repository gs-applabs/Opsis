using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class CoreTests
{
    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "opsis-core-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
    private static readonly MonitorInfo[] OneMonitor = { new("DISPLAY1", new Rectangle(0, 0, 2560, 1440), true) };
    private static readonly MonitorInfo[] TwoMonitors = { new("DISPLAY1", new Rectangle(0, 0, 2560, 1440), true), new("DISPLAY2", new Rectangle(2560, 0, 1920, 1080), false) };

    // ---------------- store
    [Fact]
    public void StoreRoundTripsWithSchemaVersionAndKeepsABackup()
    {
        var path = Path.Combine(Temp(), "containers.json");
        var store = new ContainersStore(path);
        var doc = new ContainersDocument();
        doc.Containers.Add(new Container { Title = "Work", TitleBar = new TitleBarStyle { Color = "#112233", Height = 40, FontSize = 12.5f } });
        store.Save(doc);
        store.Save(doc);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(ContainersStore.CurrentVersion, (int)JsonNode.Parse(File.ReadAllText(path))!["schemaVersion"]!);
        var back = store.Load();
        Assert.Equal("#112233", back.Containers[0].TitleBar.Color); Assert.Equal(40, back.Containers[0].TitleBar.Height); Assert.Equal(12.5f, back.Containers[0].TitleBar.FontSize);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void StoreMigratesAndQuarantinesADamagedFile()
    {
        var dir = Temp(); var path = Path.Combine(dir, "c.json");
        File.WriteAllText(path, "{\"profile\":\"Old\",\"containers\":[]}");           // no schemaVersion = 0
        var migrated = new ContainersStore(path).AddMigration(0, n => n["Profile"] = "Migrated").Load();
        Assert.Equal("Migrated", migrated.Profile);
        File.WriteAllText(path, "{ nope");
        var fresh = new ContainersStore(path).Load();
        Assert.Empty(fresh.Containers); Assert.True(File.Exists(path + ".corrupt")); Assert.False(File.Exists(path));
    }

    [Fact]
    public void UnknownFieldsSurviveALoadSaveCycle()
    {
        var path = Path.Combine(Temp(), "c.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"containers\":[{\"id\":\"a\",\"title\":\"T\",\"futureField\":{\"x\":1}}]}");
        var store = new ContainersStore(path);
        var doc = store.Load(); store.Save(doc);
        Assert.Contains("futureField", File.ReadAllText(path));
    }

    // ---------------- importer
    private static string LegacyTree()
    {
        var root = Temp(); var prof = Path.Combine(root, "Profiles", "Default"); Directory.CreateDirectory(prof); Directory.CreateDirectory(Path.Combine(prof, "Shortcuts"));
        File.WriteAllText(Path.Combine(prof, "containers.json"), @"[
 {""Id"":""c1"",""Title"":""Games"",""X"":100,""Y"":200,""Width"":320,""Height"":240,""ItemsType"":""Data"",""IsLocked"":true,""IsRolled"":false,""TabsEnabled"":""true"",""CurrentTab"":1,
  ""Items"":[{""Filename"":""C:\\Games\\a.lnk"",""DisplayName"":""A"",""IsFolder"":false,""Arguments"":""-x"",""OpenCount"":5},{""Filename"":""C:\\Games"",""DisplayName"":""Folder"",""IsFolder"":true}],
  ""Tabs"":[{""Name"":""One"",""Items"":[{""Filename"":""C:\\t.exe"",""DisplayName"":""T""}]}],
  ""CustomColor"":""#aa0000"",""IconSize"":48,""TitleTextSize"":""12"",""BoldTitleText"":false,""DisableTextShadow"":true,""ContainerBorderThickness"":2,""SomethingNew"":7},
 {""Id"":""c2"",""Title"":""Folder"",""X"":3000,""Y"":50,""Width"":400,""Height"":300,""ItemsType"":""Portal"",""Path"":""D:\\Docs""},
 {""Id"":""c3"",""Title"":""Notes"",""X"":10,""Y"":10,""Width"":200,""Height"":150,""ItemsType"":""Note"",""Text"":""hello""}
]");
        File.WriteAllText(Path.Combine(prof, "hidden_originals.json"), "[\"C:\\\\Users\\\\x\\\\Desktop\\\\a.lnk\",\"b.lnk\"]");
        File.WriteAllText(Path.Combine(prof, "options.json"), "{\"TintValue\":80}");
        return root;
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + "|" + File.GetLastWriteTimeUtc(f).Ticks);

    [Fact]
    public void ImporterMapsContainersItemsTabsAndHiddenOriginals()
    {
        var root = LegacyTree();
        var (docs, rep) = LegacyImporter.Import(root, TwoMonitors);
        Assert.Single(docs); Assert.Equal(1, rep.Profiles); Assert.Equal(3, rep.Containers); Assert.Equal(3, rep.Items); Assert.Equal(1, rep.Tabs); Assert.Equal(2, rep.HiddenOriginals);
        var d = docs[0];
        var games = d.Containers[0];
        Assert.Equal("Games", games.Title); Assert.True(games.Locked); Assert.True(games.TabsEnabled); Assert.Equal(1, games.CurrentTab);
        Assert.Equal("A", games.Items[0].DisplayName); Assert.Equal("-x", games.Items[0].Arguments); Assert.True(games.Items[1].IsFolder);
        Assert.Equal(5, games.Items[0].Extra!["OpenCount"].GetInt32());                      // unknown item field kept
        Assert.Equal("One", games.Tabs[0].Name); Assert.Single(games.Tabs[0].Items);
        Assert.Equal("#aa0000", games.Appearance.FrameColor); Assert.Equal(48, games.Appearance.IconSize); Assert.Equal(2, games.Appearance.BorderThickness); Assert.False(games.Appearance.TextShadow);
        Assert.Equal(12f, games.TitleBar.FontSize); Assert.False(games.TitleBar.Bold);
        Assert.Equal(7, games.Extra!["SomethingNew"].GetInt32());                             // unknown container field kept
        Assert.Contains(rep.NotImported, s => s.Contains("SomethingNew"));
        Assert.Equal(ContainerKind.Portal, d.Containers[1].Kind); Assert.Equal("D:\\Docs", d.Containers[1].Source);
        Assert.Equal(ContainerKind.Note, d.Containers[2].Kind); Assert.Equal("hello", d.Containers[2].Source);
        Assert.Contains(rep.NotImported, s => s.Contains("Shortcuts"));
    }

    [Fact]
    public void ImporterNeverWritesTouchesOrDeletesTheLegacyData()
    {
        var root = LegacyTree();
        var before = Snapshot(root);
        Thread.Sleep(30);
        LegacyImporter.Import(root, TwoMonitors);
        LegacyImporter.Import(root, OneMonitor);
        Assert.Equal(before, Snapshot(root));            // same files, same bytes, same timestamps
        Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void ImporterReadsAFileThatAnotherProcessHasOpen()
    {
        var root = LegacyTree();
        using var holder = new FileStream(Path.Combine(root, "Profiles", "Default", "containers.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);   // the old engine still running
        var (docs, _) = LegacyImporter.Import(root, OneMonitor);
        Assert.Equal(3, docs[0].Containers.Count);
    }

    [Fact]
    public void ImporterHandlesMissingDamagedAndPre220FileNames()
    {
        var root = Temp();
        Assert.Empty(LegacyImporter.Import(root, OneMonitor).Documents);
        var p = Path.Combine(root, "Profiles", "Default"); Directory.CreateDirectory(p);
        File.WriteAllText(Path.Combine(p, "containers.json"), "{ not json");
        var (docs, rep) = LegacyImporter.Import(root, OneMonitor);
        Assert.Empty(docs); Assert.NotEmpty(rep.Warnings);
        File.WriteAllText(Path.Combine(p, "containers.json"), "[{\"Id\":\"x\",\"Title\":\"Old\",\"X\":1,\"Y\":2,\"Width\":100,\"Height\":100,\"ItemsType\":\"Data\",\"FenceBorderThickness\":3}]");
        var again = LegacyImporter.Import(root, OneMonitor);
        Assert.Equal(3, again.Documents[0].Containers[0].Appearance.BorderThickness);   // "FenceBorderThickness" -> "ContainerBorderThickness"
    }

    // ---------------- geometry
    [Fact]
    public void GeometryIsNormalizedPerMonitorAndSurvivesAResolutionChange()
    {
        var g = LegacyImporter.Normalize(2560 + 480, 108, 480, 216, TwoMonitors);       // on the second monitor
        Assert.Equal("DISPLAY2", g.Monitor); Assert.Equal(0.25, g.NX, 3); Assert.Equal(0.1, g.NY, 3);
        var smaller = new[] { new MonitorInfo("DISPLAY2", new Rectangle(0, 0, 1280, 720), true) };
        var r = LegacyImporter.Denormalize(g, smaller);
        Assert.Equal(new Rectangle(320, 72, 320, 144), r);
    }

    [Fact]
    public void AMissingMonitorFallsBackToThePrimaryAndStaysOnScreen()
    {
        var g = LegacyImporter.Normalize(2560 + 1500, 900, 400, 300, TwoMonitors);
        var r = LegacyImporter.Denormalize(g, OneMonitor);                                // DISPLAY2 is unplugged
        Assert.True(new Rectangle(0, 0, 2560, 1440).Contains(r));
    }

    // ---------------- pipe
    private sealed class Probe : IDisposable
    {
        public readonly string Name = "opsis-test-" + Guid.NewGuid().ToString("N");
        public readonly PipeServer Server; public readonly ContainersStore Store;
        public Probe()
        {
            Store = new ContainersStore(Path.Combine(Temp(), "c.json"));
            Server = new PipeServer(Name, new ContainersService(Store, () => OneMonitor)) { ReadTimeoutMs = 800 }; Server.Start();
        }
        public void Dispose() => Server.Dispose();
    }

    [Fact]
    public void PipeAnswersPingCreateListMoveStyleAndDelete()
    {
        using var p = new Probe();
        Assert.True((bool)PipeClient.Send(p.Name, "ping")["ok"]!);
        var id = PipeClient.Send(p.Name, "create", new JsonObject { ["title"] = "Mine", ["x"] = 640, ["y"] = 360 })["data"]!["id"]!.ToString();
        var list = PipeClient.Send(p.Name, "list")["data"]!.AsArray();
        Assert.Single(list); Assert.Equal("Mine", list[0]!["title"]!.ToString());
        var moved = PipeClient.Send(p.Name, "move", new JsonObject { ["id"] = id, ["x"] = 1280, ["y"] = 720, ["width"] = 320, ["height"] = 240 })["data"]!;
        Assert.Equal(0.5, (double)moved["nx"]!, 3);
        var style = PipeClient.Send(p.Name, "set-title-style", new JsonObject { ["id"] = id, ["color"] = "#FF8800", ["height"] = 44, ["fontSize"] = 13 })["data"]!;
        Assert.Equal("#FF8800", style["color"]!.ToString()); Assert.Equal(44, (int)style["height"]!); Assert.Equal(13, (double)style["fontSize"]!, 3);
        Assert.Equal(44, p.Store.Load().Containers[0].TitleBar.Height);                              // really saved
        Assert.True((bool)PipeClient.Send(p.Name, "delete", new JsonObject { ["id"] = id })["ok"]!);
        Assert.Empty(PipeClient.Send(p.Name, "list")["data"]!.AsArray());
    }

    [Fact]
    public void PipeReportsErrorsWithCodesAndNeverCrashesTheServer()
    {
        using var p = new Probe();
        Assert.Equal("unknown-command", PipeClient.Send(p.Name, "explode")["error"]!["code"]!.ToString());
        Assert.Equal("not-found", PipeClient.Send(p.Name, "get", new JsonObject { ["id"] = "nope" })["error"]!["code"]!.ToString());
        Assert.Equal("bad-args", PipeClient.Send(p.Name, "delete", new JsonObject())["error"]!["code"]!.ToString());
        // raw garbage and a wrong protocol version
        using (var c = new System.IO.Pipes.NamedPipeClientStream(".", p.Name)) { c.Connect(2000); var b = System.Text.Encoding.UTF8.GetBytes("this is not json\n"); c.Write(b); c.Flush(); Assert.Contains("bad-json", new StreamReader(c).ReadLine()); }
        using (var c = new System.IO.Pipes.NamedPipeClientStream(".", p.Name)) { c.Connect(2000); var b = System.Text.Encoding.UTF8.GetBytes("{\"v\":99,\"cmd\":\"ping\"}\n"); c.Write(b); c.Flush(); Assert.Contains("bad-version", new StreamReader(c).ReadLine()); }
        Assert.True((bool)PipeClient.Send(p.Name, "ping")["ok"]!);                                   // still alive
    }

    [Fact]
    public void PipeTimesOutAClientThatNeverFinishesItsRequest()
    {
        using var p = new Probe();
        using var c = new System.IO.Pipes.NamedPipeClientStream(".", p.Name); c.Connect(2000);
        var part = System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"cmd\":\"pi"); c.Write(part); c.Flush();        // no newline, ever
        var reply = new StreamReader(c).ReadLine();
        Assert.Contains("timeout", reply);
        Assert.True((bool)PipeClient.Send(p.Name, "ping")["ok"]!);
    }

    [Fact]
    public void PipeRefusesARequestOverOneMegabyte()
    {
        using var p = new Probe();
        using var c = new System.IO.Pipes.NamedPipeClientStream(".", p.Name); c.Connect(2000);
        var big = new byte[(1 << 20) + 10000]; Array.Fill(big, (byte)'a');
        try { c.Write(big); c.Flush(); } catch (IOException) { }
        string? reply = null; try { reply = new StreamReader(c).ReadLine(); } catch (IOException) { }
        Assert.True(reply is null || reply.Contains("too-large"));
        Assert.True((bool)PipeClient.Send(p.Name, "ping")["ok"]!);
    }

    [Fact]
    public void ImportThroughThePipeIsADryRunByDefault()
    {
        using var p = new Probe();
        var root = LegacyTree();
        var r = PipeClient.Send(p.Name, "import-legacy", new JsonObject { ["root"] = root })["data"]!;
        Assert.True((bool)r["dryRun"]!); Assert.Equal(3, (int)r["containers"]!);
        Assert.Empty(p.Store.Load().Containers);                                                      // nothing written
        PipeClient.Send(p.Name, "import-legacy", new JsonObject { ["root"] = root, ["dryRun"] = false });
        Assert.Equal(3, p.Store.Load().Containers.Count);
    }
}

public sealed class RealDataCheck
{
    // Only runs when OPSIS_REAL_LEGACY_ROOT points at a real 2.4.6 data folder: a READ-ONLY dry run that checks the importer survives real data (counts only, nothing printed from the content).
    [Fact]
    public void RealLegacyDataImportsWithoutErrorsAndWithoutChange()
    {
        var root = Environment.GetEnvironmentVariable("OPSIS_REAL_LEGACY_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        string Sig() => string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => f + File.GetLastWriteTimeUtc(f).Ticks + new FileInfo(f).Length));
        var before = Sig();
        var (docs, rep) = LegacyImporter.Import(root, new[] { new MonitorInfo("PRIMARY", new Rectangle(0, 0, 2560, 1440), true) });
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "opsis-real-import-report.txt"), $"profiles={rep.Profiles} containers={rep.Containers} items={rep.Items} tabs={rep.Tabs} hidden={rep.HiddenOriginals} warnings={rep.Warnings.Count} notImported={rep.NotImported.Count}");
        Assert.Equal(before, Sig());
        Assert.Empty(rep.Warnings);
        Assert.True(docs.Count >= 1);
    }
}

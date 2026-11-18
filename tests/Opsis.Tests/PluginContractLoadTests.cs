using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using MotionDesk.Services;
using OpsisNewsCrawler.Plugin.Abstractions;
using Xunit;

namespace Opsis.Tests;

// Plugin mechanism 1.0.0 (Opsis 2.6.0): contract 1.0.0.0, load matrix with REAL plugin DLLs (tests/PluginFixtures), PluginTrust re-approval, updates only through the Opsis button.
public sealed class PluginContractLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opsis_pc_" + Guid.NewGuid().ToString("N")[..8]);
    public PluginContractLoadTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }

    private static string FixtureDll(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "PluginFixtures", "out", name, name + ".dll");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("fixture not built: " + name);
    }

    private string PluginFolder(string fixture, string? folderName = null)
    {
        var dir = Path.Combine(_root, "plugins", folderName ?? fixture);
        Directory.CreateDirectory(dir);
        File.Copy(FixtureDll(fixture), Path.Combine(dir, fixture + ".dll"), overwrite: true);
        return dir;
    }

    // ---- the contract of this Opsis ----

    [Fact]
    public void TheHostContractIs1000AndLibsHoldTheSameFile()
    {
        Assert.Equal(new Version(1, 0, 0, 0), PluginHost.ContractVersion);
        var libs = FindLibs();
        var recorded = File.ReadAllText(Path.Combine(libs, "contract.sha256")).Trim().Split(' ', '\t')[0].ToLowerInvariant();
        Assert.Equal(recorded, Sha(Path.Combine(libs, "OpsisNewsCrawler.Plugin.Abstractions.dll")));
        // the Crawler repository (when it is next to this one, or OPSIS_CRAWLER_REPO says where) holds the canonical copy: same SHA-256
        var crawler = Environment.GetEnvironmentVariable("OPSIS_CRAWLER_REPO");
        var candidates = new List<string>(); if (!string.IsNullOrEmpty(crawler)) candidates.Add(crawler);
        candidates.Add(Path.Combine(Directory.GetParent(libs)!.Parent!.FullName, "crawler-p"));
        candidates.Add(Path.Combine(Directory.GetParent(libs)!.Parent!.FullName, "OpsisNewsCrawler"));
        foreach (var c in candidates)
        {
            var other = Path.Combine(c, "tests", "OpsisNewsCrawler.Plugin.Tests", "reference", "OpsisNewsCrawler.Plugin.Abstractions.dll");
            if (File.Exists(other)) { Assert.Equal(recorded, Sha(other)); break; }
        }
    }

    private static string FindLibs()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "libs", "OpsisNewsCrawler.Plugin.Abstractions.dll"))) return Path.Combine(d.FullName, "libs");
        throw new DirectoryNotFoundException("libs");
    }
    private static string Sha(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }

    // ---- load matrix: old plugin -> new host ----

    [Fact]
    public void APluginBuiltAgainstContract0800LoadsInTheNewHost()
    {
        var dir = PluginFolder("StubOldPlugin");
        Assert.Equal(new Version(0, 8, 0, 0), PluginContractCheck.ReadRequiredContract(Path.Combine(dir, "StubOldPlugin.dll")));
        var host = new PluginHost();
        host.LoadFolder(dir);
        var p = Assert.Single(host.Plugins);
        Assert.Equal("Stub old", p.Name);
        Assert.Empty(host.Skipped);
        // the plugin sees the contract assembly of the host (1.0.0.0), not a second copy
        Assert.Same(typeof(IOpsisNewsCrawlerPlugin).Assembly, typeof(IOpsisNewsCrawlerPlugin).Assembly);
        Assert.Equal(new Version(1, 0, 0, 0), p.GetType().GetInterfaces().First(i => i.Name == nameof(IOpsisNewsCrawlerPlugin)).Assembly.GetName().Version);
    }

    [Fact]
    public void AnOldPluginIsNotAskedForTheNewCapabilities()
    {
        var host = new PluginHost();
        host.LoadFolder(PluginFolder("StubOldPlugin"));
        Assert.Empty(host.GetUpdateCandidates());
        Assert.IsNotAssignableFrom<IPluginHostSettingsAware>(host.Plugins[0]);
    }

    // ---- new plugin -> new host ----

    [Fact]
    public void ANewPluginLoadsAndOffersUpdateSourceAndReceivesSharedSettings()
    {
        var host = new PluginHost();
        host.LoadFolder(PluginFolder("StubNewPlugin"));
        var p = Assert.Single(host.Plugins);
        var c = Assert.Single(host.GetUpdateCandidates());
        Assert.Equal("gs-applabs/stub-plugin", c.Source.Repository);
        Assert.Equal("stub-Plugin.zip", c.Source.AssetName);
        var settings = PluginHost.BuildHostSettings(p);
        Assert.True(settings.HostManagesUpdates);          // Opsis, not the plugin, updates it
        Assert.False(string.IsNullOrEmpty(settings.Language));
        ((IPluginHostSettingsAware)p).SetHostSettings(settings);
        Assert.StartsWith(settings.Language + "|", (string)p.GetType().GetField("LastSettings")!.GetValue(p)!);
    }

    [Fact]
    public void APluginBuiltForANewerContractThanTheHostIsRefusedWithAClearMessage()
    {
        var dll = Path.Combine(PluginFolder("StubNewPlugin"), "StubNewPlugin.dll");
        var r = PluginContractCheck.Check(dll, new Version(0, 8, 0, 0));      // a host whose contract is older than 1.0.0.0
        Assert.False(r.Ok);
        Assert.Contains("needs a newer Opsis", r.Message);
        Assert.Equal(new Version(1, 0, 0, 0), r.Required);
        Assert.True(PluginContractCheck.Check(dll, new Version(1, 0, 0, 0)).Ok);
        Assert.True(PluginContractCheck.Check(Path.Combine(PluginFolder("StubOldPlugin"), "StubOldPlugin.dll"), new Version(1, 0, 0, 0)).Ok);
        Assert.False(PluginContractCheck.Check(Path.Combine(_root, "nothing.dll"), new Version(1, 0, 0, 0)).Ok);
    }

    // ---- new plugin -> OLD host (Opsis 2.4.6: contract 0.8.0.0; PluginLoadContext identical to the one of this host) ----

    private sealed class Old246PluginContext : AssemblyLoadContext
    {
        private readonly Assembly _hostContract;
        public Old246PluginContext(Assembly hostContract) : base("old246-plugin", isCollectible: true) => _hostContract = hostContract;
        protected override Assembly? Load(AssemblyName name) => name.Name == "OpsisNewsCrawler.Plugin.Abstractions" ? _hostContract : null;
    }

    private static (AssemblyLoadContext Host, Old246PluginContext Plugin) OldHost()
    {
        var hostCtx = new AssemblyLoadContext("old246-host", isCollectible: true);
        var contract = hostCtx.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contract0800", "contract-0.8.0.0.bin"));
        Assert.Equal(new Version(0, 8, 0, 0), contract.GetName().Version);
        return (hostCtx, new Old246PluginContext(contract));
    }

    [Fact]
    public void ANewPluginThatUsesNoNewTypeStillLoadsInOpsis246()
    {
        var (hostCtx, ctx) = OldHost();
        try
        {
            var asm = ctx.LoadFromAssemblyPath(FixtureDll("StubNewLitePlugin"));
            var type = asm.GetTypes().Single(t => t.Name == "StubPlugin");
            dynamic plugin = Activator.CreateInstance(type)!;
            Assert.Equal("Stub newlite", (string)plugin.Name);
        }
        finally { ctx.Unload(); hostCtx.Unload(); }
    }

    [Fact]
    public void ANewPluginThatImplementsTheNewInterfacesFailsInOpsis246WithATypeLoadError()
    {
        // this is the case of the Crawler 0.25.0 ("requires Opsis 2.6.0"): 2.4.6 logs the error in plugins.log and skips the plugin, it never crashes
        var (hostCtx, ctx) = OldHost();
        try
        {
            var asm = ctx.LoadFromAssemblyPath(FixtureDll("StubNewPlugin"));
            var ex = Assert.ThrowsAny<Exception>(() => { foreach (var t in asm.GetTypes()) { } });
            Assert.True(ex is ReflectionTypeLoadException or TypeLoadException);
            Assert.Contains("IPluginUpdateSource", ex is ReflectionTypeLoadException r ? string.Join(" ", r.LoaderExceptions.Select(e => e?.Message)) : ex.Message);
        }
        finally { ctx.Unload(); hostCtx.Unload(); }
    }

    // ---- PluginTrust: an update is never trusted silently ----

    [Fact]
    public void AKnownPluginWhoseContentChangedAsksOnceMoreAndIsNotLoadedMeanwhile()
    {
        var folder = PluginFolder("StubOldPlugin", "Stub Plugin");
        var loaded = new List<string>();
        var host = new PluginHost { TrustStoreOverride = new PluginTrust.Store(Path.Combine(_root, "trust.json")), FolderLoader = f => loaded.Add(f) };
        Assert.False(host.ConsiderFolder(folder, false));
        var first = Assert.Single(host.PendingApprovals);
        Assert.False(first.IsUpdate);                                   // never seen: the ordinary question
        Assert.Equal(1, host.ResolvePending(_ => true));

        File.AppendAllText(Path.Combine(folder, "StubOldPlugin.deps.json"), "{}");             // the content changes (an update)
        var host2 = new PluginHost { TrustStoreOverride = host.TrustStoreOverride, FolderLoader = f => loaded.Add(f + "#2") };
        Assert.False(host2.ConsiderFolder(folder, false));              // not loaded: no silent trust
        var req = Assert.Single(host2.PendingApprovals);
        Assert.True(req.IsUpdate);
        Assert.Equal(first.Sha256, req.PreviousSha256, ignoreCase: true);
        Assert.NotEqual(first.Sha256, req.Sha256);
        Assert.Equal(1, host2.ResolvePending(_ => true));               // ONE prompt, then trusted for this content
        var host3 = new PluginHost { TrustStoreOverride = host.TrustStoreOverride, FolderLoader = f => loaded.Add(f + "#3") };
        Assert.True(host3.ConsiderFolder(folder, false));
        Assert.Empty(host3.PendingApprovals);
    }

    [Fact]
    public void DecliningTheReapprovalKeepsTheUpdatedPluginUnloaded()
    {
        var folder = PluginFolder("StubOldPlugin", "Stub Plugin");
        var store = new PluginTrust.Store(Path.Combine(_root, "trust.json"));
        store.Approve("Stub Plugin", PluginTrust.HashFolder(folder));
        File.AppendAllText(Path.Combine(folder, "x.deps.json"), "{}");
        var loaded = new List<string>();
        var host = new PluginHost { TrustStoreOverride = store, FolderLoader = f => loaded.Add(f) };
        host.ConsiderFolder(folder, false);
        Assert.Equal(0, host.ResolvePending(_ => false));
        Assert.Empty(loaded);
        Assert.Single(host.PendingApprovals);
    }

    // ---- updates only from the Opsis button: trust rules ----

    private static PluginUpdateCandidate Candidate(string repo = "gs-applabs/stub-plugin", string version = "1.2.3", string asset = "stub-Plugin.zip") =>
        new("stub.id", "Stub", "Stub Plugin", version, new PluginUpdateSource(repo, asset));

    private static string Release(string tag, string owner = "gs-applabs", string repo = "stub-plugin", string asset = "stub-Plugin.zip", string? digest = null, string? assetOwner = null, string? assetRepo = null, bool pre = false)
    {
        var url = $"https://github.com/{assetOwner ?? owner}/{assetRepo ?? repo}/releases/download/{tag}/{asset}";
        var dg = digest is null ? "" : $", \"digest\": \"sha256:{digest}\"";
        return $"{{\"tag_name\":\"{tag}\",\"prerelease\":{(pre ? "true" : "false")},\"html_url\":\"https://github.com/{owner}/{repo}/releases/tag/{tag}\",\"assets\":[{{\"name\":\"{asset}\",\"browser_download_url\":\"{url}\"{dg}}},{{\"name\":\"{asset}.sha256\",\"browser_download_url\":\"{url}.sha256\"}}]}}";
    }

    [Fact]
    public void ANewerReleaseOfTheTrustedRepositoryIsAccepted_BothAccountNames()
    {
        var hash = new string('a', 64);
        var u = PluginUpdateService.Parse(Release("v1.3.0", digest: hash), Candidate());
        Assert.NotNull(u);
        Assert.Equal(new Version(1, 3, 0), u!.Latest);
        Assert.Equal(hash, u.Sha256);
        Assert.True(u.CanInstallAutomatically);
        // the renamed account, in the plugin's declaration and in the release URLs (GitHub redirects the old name)
        Assert.NotNull(PluginUpdateService.Parse(Release("v1.3.0", owner: "giorstergiopoulos-design", digest: hash), Candidate("giorstergiopoulos-design/stub-plugin")));
        Assert.NotNull(PluginUpdateService.Parse(Release("v1.3.0", owner: "giorstergiopoulos-design", digest: hash), Candidate("gs-applabs/stub-plugin")));
    }

    [Fact]
    public void NotNewerPrereleaseForeignOwnerForeignAssetAndBadAssetNamesAreRefused()
    {
        var hash = new string('a', 64);
        Assert.Null(PluginUpdateService.Parse(Release("v1.2.3", digest: hash), Candidate()));                    // same version
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", digest: hash, pre: true), Candidate()));         // prerelease
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", owner: "evil", digest: hash), Candidate("evil/stub-plugin")));   // not one of our accounts
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", digest: hash, assetOwner: "evil"), Candidate()));                // asset URL elsewhere
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", digest: hash, assetRepo: "other-repo"), Candidate()));           // another repository of the same account
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", digest: hash), Candidate(asset: "../evil.zip")));
        Assert.Null(PluginUpdateService.Parse(Release("v1.3.0", digest: hash), Candidate(asset: "setup.exe")));
        Assert.False(PluginUpdateService.TryParseRepository("gs-applabs/../x", out _, out _));
        Assert.False(PluginUpdateService.TryParseRepository("gs-applabs", out _, out _));
        Assert.False(PluginUpdateService.IsTrustedDownloadUrl("http://github.com/gs-applabs/stub-plugin/releases/download/v1/x.zip", "gs-applabs/stub-plugin"));
        Assert.False(PluginUpdateService.IsTrustedDownloadUrl("https://github.com.evil.com/gs-applabs/stub-plugin/releases/download/v1/x.zip", "gs-applabs/stub-plugin"));
        Assert.False(PluginUpdateService.IsTrustedDownloadUrl("https://user@github.com/gs-applabs/stub-plugin/releases/download/v1/x.zip", "gs-applabs/stub-plugin"));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files = new();
        public List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(Files.TryGetValue(request.RequestUri!.ToString(), out var b)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private byte[] ZipOf(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, d) in entries) { using var s = z.CreateEntry(n).Open(); s.Write(d); }
        return ms.ToArray();
    }

    private static string HexSha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private PluginUpdateInfo Info(byte[] zip, string? digest, FakeHandler h, string tag = "v1.3.0")
    {
        var url = $"https://github.com/gs-applabs/stub-plugin/releases/download/{tag}/stub-Plugin.zip";
        h.Files[url] = zip;
        return new PluginUpdateInfo(Candidate(), new Version(1, 3, 0), tag, "https://github.com/gs-applabs/stub-plugin/releases/tag/" + tag, url, digest, url + ".sha256");
    }

    [Fact]
    public async Task AVerifiedZipIsStagedAndOnlyPutInPlaceAtTheNextStartThenNeedsReapproval()
    {
        var dllBytes = File.ReadAllBytes(FixtureDll("StubNewPlugin"));
        var zip = ZipOf(("StubNewPlugin.dll", dllBytes));
        var h = new FakeHandler();
        var info = Info(zip, HexSha(zip), h);
        var pluginsRoot = Path.Combine(_root, "AppData", "plugins");
        var installed = Path.Combine(pluginsRoot, "Stub Plugin"); Directory.CreateDirectory(installed);
        File.Copy(FixtureDll("StubOldPlugin"), Path.Combine(installed, "StubOldPlugin.dll"));       // the installed (old) content
        var store = new PluginTrust.Store(Path.Combine(_root, "trust.json"));
        store.Approve("Stub Plugin", PluginTrust.HashFolder(installed));
        var oldHash = PluginTrust.HashFolder(installed);

        var staging = PluginUpdateService.StagingRoot(pluginsRoot);
        var staged = await PluginUpdateService.DownloadAndStageAsync(info, staging, handler: h);
        Assert.True(File.Exists(Path.Combine(staged, "StubNewPlugin.dll")));
        Assert.Equal(oldHash, PluginTrust.HashFolder(installed));            // nothing installed yet: the running plugin is untouched

        var applied = PluginUpdateService.ApplyStaged(pluginsRoot);          // next start, before anything is loaded
        Assert.Equal(new[] { "Stub Plugin" }, applied);
        Assert.True(File.Exists(Path.Combine(installed, "StubNewPlugin.dll")));
        Assert.False(Directory.Exists(staged));

        var host = new PluginHost { TrustStoreOverride = store, FolderLoader = _ => Assert.Fail("an updated plugin must not load before the user approves it") };
        Assert.False(host.ConsiderFolder(installed, false));
        var req = Assert.Single(host.PendingApprovals);
        Assert.True(req.IsUpdate);
        Assert.Equal(oldHash, req.PreviousSha256, ignoreCase: true);
    }

    [Fact]
    public async Task AWrongSha256IsRefusedAndNothingIsStaged()
    {
        var zip = ZipOf(("StubNewPlugin.dll", File.ReadAllBytes(FixtureDll("StubNewPlugin"))));
        var h = new FakeHandler();
        var info = Info(zip, new string('0', 64), h);
        var staging = Path.Combine(_root, "staging");
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginUpdateService.DownloadAndStageAsync(info, staging, handler: h));
        Assert.Empty(Directory.EnumerateDirectories(staging));
        Assert.Empty(Directory.EnumerateFiles(staging));
    }

    [Fact]
    public async Task WithoutAnyDigestNothingIsInstalled_TheChecksumAssetIsUsedWhenThereIsOne()
    {
        var zip = ZipOf(("StubNewPlugin.dll", File.ReadAllBytes(FixtureDll("StubNewPlugin"))));
        var h = new FakeHandler();
        var info = Info(zip, null, h);
        var staging = Path.Combine(_root, "staging");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PluginUpdateService.DownloadAndStageAsync(info, staging, handler: h));     // .sha256 asset is a 404 here
        h.Files[info.ChecksumUrl!] = System.Text.Encoding.ASCII.GetBytes(HexSha(zip) + "  stub-Plugin.zip\n");
        var staged = await PluginUpdateService.DownloadAndStageAsync(info, staging, handler: h);
        Assert.True(Directory.Exists(staged));
    }

    [Fact]
    public async Task AZipThatEscapesItsFolderOrHasNoPluginDllOrNeedsANewerContractIsRefused()
    {
        var h = new FakeHandler(); var staging = Path.Combine(_root, "staging");
        var slip = ZipOf(("..\\..\\evil.dll", new byte[] { 1 }), ("StubNewPlugin.dll", new byte[] { 1 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginUpdateService.DownloadAndStageAsync(Info(slip, HexSha(slip), h), staging, handler: h));
        var none = ZipOf(("readme.txt", new byte[] { 1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PluginUpdateService.DownloadAndStageAsync(Info(none, HexSha(none), h), staging, handler: h));
        Assert.False(Directory.Exists(Path.Combine(staging, "Stub Plugin")));
        Assert.False(Directory.Exists(Path.Combine(staging, "Stub Plugin.tmp")));
        // untrusted download URL: refused before any request
        var evil = new PluginUpdateInfo(Candidate(), new Version(1, 3, 0), "v1.3.0", "https://github.com/gs-applabs/stub-plugin", "https://evil.example/x.zip", new string('a', 64), null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PluginUpdateService.DownloadAndStageAsync(evil, staging, handler: h));
        Assert.DoesNotContain(h.Requests, r => r.Contains("evil"));
    }
}

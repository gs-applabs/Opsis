using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Opsis.Lockscreen;
using Xunit;

namespace Opsis.Tests;

// Everything runs against a temp folder that imitates the lock-screen cache (never the real one) with fake icacls/takeown and a fake or HKCU-test feature store.
public sealed class LockscreenCoreTests : IDisposable
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "opsis-lock-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_tmp, "data");
    private string Bk => Path.Combine(_tmp, "bk");
    private string Cache => Path.Combine(_tmp, "cache", "ReadOnly");

    private sealed class FakeAcl : IAclTool
    {
        public readonly List<string> Calls = new();
        public Func<string, IReadOnlyList<string>, int>? Hook;
        public int Run(string tool, IReadOnlyList<string> args)
        {
            Calls.Add(tool + " " + string.Join(" ", args));
            if (Hook != null) { int h = Hook(tool, args); if (h >= 0) return h; }
            if (tool == "icacls.exe" && args.Count > 2 && args[1] == "/save") File.WriteAllText(args[2], "sddl");
            return 0;
        }
    }
    private sealed class FakeFeature : IFeatureStore
    {
        public FeatureBackup Original = new() { Captured = true, KeyExisted = true, EnabledState = 2, EnabledStateOptions = 0 };
        public int? State; public int Restores;
        public FeatureBackup Capture() => Original;
        public void SetDisabled() => State = 1;
        public void Restore(FeatureBackup o) { Restores++; State = o.EnabledState; }
        public bool? IsDisabled() => State == null ? null : State == 1;
    }

    private static readonly byte[] GifBytes = Encoding.ASCII.GetBytes("GIF89a").Concat(Enumerable.Repeat((byte)7, 200)).ToArray();
    private static byte[] Orig(string n) => Encoding.ASCII.GetBytes("ORIGINAL-" + n);

    public LockscreenCoreTests()
    {
        foreach (var (d, files) in new[] { ("LockScreen_A", new[] { "LockScreen.jpg", "LockScreen___1920x1080_notdimmed.jpg", "other.txt" }), ("LockScreen_B", new[] { "LockScreen.jpg" }) })
        {
            Directory.CreateDirectory(Path.Combine(Cache, d));
            foreach (var f in files) File.WriteAllBytes(Path.Combine(Cache, d, f), Orig(d + f));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LockscreenPaths.StagingGif(Data))!);
        File.WriteAllBytes(LockscreenPaths.StagingGif(Data), GifBytes);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (Exception) { } }

    private LockscreenOperations Ops(FakeAcl? acl = null, IFeatureStore? f = null) => new(Data, Bk, Cache, Sid, acl ?? new FakeAcl(), f ?? new FakeFeature());
    private string P(string d, string f) => Path.Combine(Cache, d, f);

    [Fact]
    public void ApplyWritesTheGifToEveryCacheImageAndLeavesOtherFilesAlone()
    {
        var acl = new FakeAcl();
        var r = Ops(acl).Apply();
        Assert.True(r.Ok, r.Message);
        Assert.Equal(GifBytes, File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
        Assert.Equal(GifBytes, File.ReadAllBytes(P("LockScreen_A", "LockScreen___1920x1080_notdimmed.jpg")));
        Assert.Equal(GifBytes, File.ReadAllBytes(P("LockScreen_B", "LockScreen.jpg")));
        Assert.Equal(Orig("LockScreen_Aother.txt"), File.ReadAllBytes(P("LockScreen_A", "other.txt")));
        var m = BackupManifest.Load(Bk)!;
        Assert.True(m.Applied); Assert.Equal(3, m.Files.Count); Assert.NotEmpty(m.Acls);
        // every ACL was saved before it was changed, and put back afterwards
        int firstSave = acl.Calls.FindIndex(c => c.Contains("/save")), firstGrant = acl.Calls.FindIndex(c => c.Contains("/grant"));
        Assert.InRange(firstSave, 0, firstGrant - 1);
        Assert.Contains(acl.Calls, c => c.Contains("/restore"));
    }

    [Fact]
    public void RestoreBringsBackTheOriginalFilesAclAndRemovesTheBackup()
    {
        var f = new FakeFeature(); var acl = new FakeAcl();
        var ops = Ops(acl, f);
        Assert.True(ops.DisableFeature().Ok); Assert.Equal(1, f.State);
        Assert.True(ops.Apply().Ok);
        acl.Calls.Clear();
        var r = ops.Restore();
        Assert.True(r.Ok, r.Message);
        Assert.Equal(Orig("LockScreen_ALockScreen.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
        Assert.Equal(Orig("LockScreen_BLockScreen.jpg"), File.ReadAllBytes(P("LockScreen_B", "LockScreen.jpg")));
        Assert.Equal(2, f.State); Assert.Equal(1, f.Restores);
        Assert.Contains(acl.Calls, c => c.Contains("/restore"));
        Assert.False(Directory.Exists(Bk));
        Assert.False(LockscreenState.Read(Bk).HasBackup);
    }

    [Fact]
    public void ApplyingTwiceStillRestoresTheVeryFirstOriginal()
    {
        var ops = Ops();
        Assert.True(ops.Apply().Ok);
        File.WriteAllBytes(LockscreenPaths.StagingGif(Data), Encoding.ASCII.GetBytes("GIF87a").Concat(new byte[] { 1, 2, 3 }).ToArray());
        Assert.True(ops.Apply().Ok);
        Assert.True(ops.Restore().Ok);
        Assert.Equal(Orig("LockScreen_ALockScreen.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
    }

    [Fact]
    public void RestoreLeavesAFileWindowsReplacedAfterTheApply()
    {
        var ops = Ops(); Assert.True(ops.Apply().Ok);
        File.WriteAllText(P("LockScreen_B", "LockScreen.jpg"), "user picked a new picture");
        Assert.True(ops.Restore().Ok);
        Assert.Equal("user picked a new picture", File.ReadAllText(P("LockScreen_B", "LockScreen.jpg")));
        Assert.Equal(Orig("LockScreen_ALockScreen.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
    }

    [Fact]
    public void NonGifOrMissingSourceChangesNothing()
    {
        File.WriteAllBytes(LockscreenPaths.StagingGif(Data), Encoding.ASCII.GetBytes("PNG not a gif"));
        var r = Ops().Apply();
        Assert.False(r.Ok); Assert.Equal("BadSource", r.Code);
        Assert.Equal(Orig("LockScreen_ALockScreen.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
        File.Delete(LockscreenPaths.StagingGif(Data));
        Assert.Equal("NoSource", Ops().Apply().Code);
    }

    [Fact]
    public void AFailureHalfWayPutsTheOriginalsBack()
    {
        using var locked = new FileStream(P("LockScreen_B", "LockScreen.jpg"), FileMode.Open, FileAccess.Read, FileShare.None);   // the last target cannot be written
        var r = Ops().Apply();
        Assert.False(r.Ok);
        locked.Dispose();
        Assert.Equal(Orig("LockScreen_ALockScreen.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen.jpg")));
        Assert.Equal(Orig("LockScreen_ALockScreen___1920x1080_notdimmed.jpg"), File.ReadAllBytes(P("LockScreen_A", "LockScreen___1920x1080_notdimmed.jpg")));
    }

    [Fact]
    public void NoCacheFolderIsReportedNotCreated()
    {
        Directory.Delete(Path.GetDirectoryName(Cache)!, true);
        Assert.Equal("NoCache", Ops().Apply().Code);
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public void WhenTheAclCannotBeReadOwnershipIsTakenAndPutBackToSystem()
    {
        var acl = new FakeAcl();
        bool first = true;
        acl.Hook = (t, a) => { if (t == "icacls.exe" && a[1] == "/save" && first) { first = false; return 5; } return -1; };   // the very first /save is denied
        Assert.True(Ops(acl).Apply().Ok);
        Assert.Contains(acl.Calls, c => c.StartsWith("takeown.exe"));
        Assert.Contains(acl.Calls, c => c.Contains("/setowner *S-1-5-18"));
    }

    [Fact]
    public void ATamperedManifestCannotPointOutsideTheCache()
    {
        var ops = Ops(); Assert.True(ops.Apply().Ok);
        var m = BackupManifest.Load(Bk)!;
        m.Files[0].Rel = @"..\..\Windows\win.ini"; m.Save(Bk);
        Assert.False(ops.Restore().Ok);
        m.Files[0].Rel = "LockScreen_A\\LockScreen.jpg"; m.Files[0].BackupName = @"..\x"; m.Save(Bk);
        Assert.False(ops.Restore().Ok);
    }

    [Theory]
    [InlineData("apply", true)] [InlineData("restore", true)] [InlineData("disable-feature", true)]
    [InlineData("APPLY", false)] [InlineData("", false)] [InlineData("apply ", false)] [InlineData("--path", false)] [InlineData("restore-all", false)]
    public void HelperAcceptsOnlyTheThreeCommandWords(string word, bool ok) => Assert.Equal(ok, HelperCommand.TryParse(new[] { word }, out _));

    [Fact]
    public void HelperRefusesExtraArgumentsAndNone()
    {
        Assert.False(HelperCommand.TryParse(Array.Empty<string>(), out _));
        Assert.False(HelperCommand.TryParse(new[] { "apply", @"C:\Windows\System32\x.gif" }, out _));
        Assert.False(HelperCommand.TryParse(new[] { "restore", "--sid", "S-1-5-18" }, out _));
    }

    [Theory]
    [InlineData(@"LockScreen_A\LockScreen.jpg", true)] [InlineData(@"LockScreen_A", true)]
    [InlineData(@"..\x.jpg", false)] [InlineData(@"LockScreen_A\evil.exe", false)] [InlineData(@"Other\LockScreen.jpg", false)]
    [InlineData(@"LockScreen_A\sub\LockScreen.jpg", false)] [InlineData(@"LockScreen_A\..\..\x\LockScreen.jpg", false)]
    public void OnlyCacheImagesAndFoldersPassPathValidation(string rel, bool ok)
    {
        Directory.CreateDirectory(Path.Combine(Cache, "Other"));
        string path = Path.Combine(Cache, rel);
        if (ok) LockscreenPaths.ValidateCachePath(Cache, path, true);
        else Assert.Throws<UnauthorizedAccessException>(() => LockscreenPaths.ValidateCachePath(Cache, path, true));
    }

    [Fact]
    public void TheCacheRootAcceptsReadOnly()
    {
        LockscreenPaths.ValidateCachePath(Cache, Cache, false);
        Assert.Throws<UnauthorizedAccessException>(() => LockscreenPaths.ValidateCachePath(Cache, Cache, true));
        Assert.Throws<UnauthorizedAccessException>(() => LockscreenPaths.ValidateCachePath(Cache, Path.GetDirectoryName(Cache)!, false));
    }

    [Fact]
    public void AJunctionInsideTheCacheIsRefused()
    {
        string target = Path.Combine(_tmp, "elsewhere"); Directory.CreateDirectory(target);
        string link = Path.Combine(Cache, "LockScreen_Link");
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!; p.WaitForExit();
        Assert.True(Directory.Exists(link));
        Assert.Throws<UnauthorizedAccessException>(() => LockscreenPaths.ValidateCachePath(Cache, link, false));
        var r = Ops().Apply();                         // discovery meets the junction: nothing is written through it
        Assert.False(r.Ok);
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public void TheBackupFolderMustBeOwnedByTrustedAccounts()
    {
        string d = Path.Combine(_tmp, "pd"); Directory.CreateDirectory(d);
        Assert.Throws<UnauthorizedAccessException>(() => SecureFolder.Ensure(d, new[] { "S-1-1-0" }));
        var owner = new DirectoryInfo(d).GetAccessControl().GetOwner(typeof(SecurityIdentifier))!.Value;
        SecureFolder.Ensure(d, new[] { owner });
    }

    [Fact]
    public void FeatureOverrideIsBackedUpChangedAndRestoredInTheRegistryStore()
    {
        string path = @"Software\OpsisTests\" + Guid.NewGuid().ToString("N");     // an HKCU scratch key, not the Windows feature store
        try
        {
            var store = new RegistryFeatureStore(Registry.CurrentUser, path);
            Assert.Null(store.IsDisabled());
            var absent = store.Capture(); Assert.False(absent.KeyExisted);
            store.SetDisabled(); Assert.True(store.IsDisabled());
            store.Restore(absent); Assert.Null(store.IsDisabled()); Assert.Null(Registry.CurrentUser.OpenSubKey(path));
            using (var k = Registry.CurrentUser.CreateSubKey(path)) { k.SetValue("EnabledState", 2, RegistryValueKind.DWord); }
            var had = store.Capture(); Assert.Equal(2, had.EnabledState); Assert.Null(had.EnabledStateOptions);
            store.SetDisabled(); store.Restore(had);
            using var back = Registry.CurrentUser.OpenSubKey(path)!;
            Assert.Equal(2, back.GetValue("EnabledState")); Assert.Null(back.GetValue("EnabledStateOptions"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); Registry.CurrentUser.DeleteSubKeyTree(@"Software\OpsisTests", false); }
    }

    [Fact]
    public void TheFeatureKeyIsTheDocumentedFixedOne() =>
        Assert.EndsWith(@"FeatureManagement\Overrides\8\2920700556", LockscreenPaths.FeatureKeyPath);
}

using System.Text.Json;
using MotionDesk.Services;

var folder = Path.Combine(Path.GetTempPath(), "opsis-experience-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
Environment.SetEnvironmentVariable("OPSIS_APPDATA", folder);
int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED " + description);
    Console.WriteLine("PASS " + description);
    passed++;
}
try
{
    Check(!WorkspaceProfileService.MayRestoreSession(true, true, true) &&
        !WorkspaceProfileService.MayRestoreSession(false, false, true) &&
        !WorkspaceProfileService.MayRestoreSession(false, true, false) &&
        WorkspaceProfileService.MayRestoreSession(false, true, true),
        "R67 startup session restoration respects user preference, existence and safe mode");
    Check(!WorkspaceProfileService.MaySaveLastSession(true, false) &&
        !WorkspaceProfileService.MaySaveLastSession(false, true) &&
        WorkspaceProfileService.MaySaveLastSession(false, false),
        "R67 safe mode cannot overwrite last good session and exit save is idempotent");
    Check(!MotionDesk.Widgets.TrayApplicationContext.NeedsDesktopMenuPipe(false, false) &&
        !MotionDesk.Widgets.TrayApplicationContext.NeedsDesktopMenuPipe(true, true) &&
        MotionDesk.Widgets.TrayApplicationContext.NeedsDesktopMenuPipe(true, false),
        "R67 optional desktop menu IPC respects selection and safe mode");
    Check(!PerformanceGovernor.NeedsSampler(false, HardwareTier.High, false) &&
        !PerformanceGovernor.NeedsSampler(true, HardwareTier.Low, false) &&
        !PerformanceGovernor.NeedsSampler(true, HardwareTier.High, true) &&
        PerformanceGovernor.NeedsSampler(true, HardwareTier.Balanced, false),
        "R68 governor only samples if adaptive scaling can lower the active hardware tier");
    Check(!PluginHost.MayRunPlugin(false, false, false, false) &&
        !PluginHost.MayRunPlugin(true, true, false, false) &&
        !PluginHost.MayRunPlugin(true, false, true, false) &&
        !PluginHost.MayRunPlugin(true, false, false, true) &&
        PluginHost.MayRunPlugin(true, false, false, false),
        "R68 plugin activation requires global opt-in, individual selection and safety clearance");
    var noDemand = AppActivity.SelectDemand(false,false,false,false,false,false,false,false,false,false);
    var dockDemand = AppActivity.SelectDemand(false,false,false,false,false,false,false,false,true,false);
    var widgetDemand = AppActivity.SelectDemand(false,true,false,false,false,false,false,false,false,false);
    var taskbarDemand = AppActivity.SelectDemand(false,false,true,false,false,false,false,false,false,false);
    var wallpaperDemand = AppActivity.SelectDemand(true,false,false,false,false,false,false,false,false,false);
    var rulesDemand = AppActivity.SelectDemand(false,false,false,false,false,true,false,false,false,false);
    var diagnosticDemand = AppActivity.SelectDemand(false,false,false,false,false,false,false,false,false,true);
    Check(!noDemand.Any && dockDemand.Any && !dockDemand.Detailed && !dockDemand.Coverage,
        "R72 activity sampler off without consumers; dock uses fullscreen only");
    Check(widgetDemand.Detailed && !widgetDemand.Coverage && taskbarDemand.Detailed && taskbarDemand.Coverage &&
        wallpaperDemand.Detailed && wallpaperDemand.Coverage && rulesDemand.Detailed && diagnosticDemand.Coverage,
        "R72 detailed monitoring and coverage follow actual consumer requirements");
    var retained = MotionDesk.Widgets.WallpaperHostEngine.SelectRetainedRenderers(
        new[] { ("A", false), ("A", true), ("a", true), ("B", true), ("gone", true), ("B", true) },
        new[] { "A", "B" });
    Check(retained.SequenceEqual(new[] { 1, 3 }), "R72 renderer retirement keeps one visible attached candidate per connected display");
    Check(MotionDesk.Widgets.WallpaperHostEngine.SelectRetainedRenderers(
        new[] { ("A", false), ("gone", true) }, new[] { "A" }).Length == 0,
        "R72 no hidden or disconnected renderer is retained");
    Check(!DiscordPresence.ShouldRun(false, "123456789012345", false) &&
        !DiscordPresence.ShouldRun(true, "123456789012345", true) &&
        !DiscordPresence.ShouldRun(true, "invalid", false) &&
        DiscordPresence.ShouldRun(true, "123456789012345", false),
        "R73 Discord IPC only runs on explicit selection and valid identity outside safe mode");
    Check(!DiscordPresence.ValidFrameLength(0) && !DiscordPresence.ValidFrameLength(-1) &&
        !DiscordPresence.ValidFrameLength((1 << 20) + 1) && DiscordPresence.ValidFrameLength(1 << 20),
        "R73 IPC frames have a strict allocation bound");
    using (var ready = JsonDocument.Parse("{\"evt\":\"READY\"}"))
    using (var reply = JsonDocument.Parse("{\"nonce\":\"a\"}"))
    {
        Check(DiscordPresence.MatchesResponse(ready.RootElement, null) &&
            !DiscordPresence.MatchesResponse(ready.RootElement, "a") &&
            DiscordPresence.MatchesResponse(reply.RootElement, "a") &&
            !DiscordPresence.MatchesResponse(reply.RootElement, "b"),
            "R73 IPC waits for READY or the matching command response");
    }
    Check(!GpuMonitorService.ShouldReleaseIdle(29999, false) &&
        !GpuMonitorService.ShouldReleaseIdle(30000, true) &&
        GpuMonitorService.ShouldReleaseIdle(30000, false),
        "R74 GPU resources retire only after idle deadline and no in-flight sample");
    Check(AtomicFile.LockStripe(Path.Combine(folder, "A.json")) ==
        AtomicFile.LockStripe(Path.Combine(folder, "a.json")) &&
        Enumerable.Range(0, 4096).Select(i => AtomicFile.LockStripe(Path.Combine(folder, i + ".json"))).All(i => i >= 0 && i < 128),
        "R74 atomic settings lock table stays bounded and case-insensitive");
    Check(SystemMonitorService.CalculateCpuPercent(100, 200, 300, 150, 300, 400, 0) == 75 &&
        SystemMonitorService.CalculateCpuPercent(100, 200, 300, 90, 190, 290, 75) == 0 &&
        SystemMonitorService.CalculateCpuPercent(100, 200, 300, 100, 200, 300, 42) == 42,
        "R74 CPU counter reset and unchanged samples do not generate false load");
    var repeatBudget = new RotatingLogger.RepeatBudget();
    Check(repeatBudget.Accept(0, out var firstSuppressed) && firstSuppressed == 0 &&
        !repeatBudget.Accept(1, out _) && !repeatBudget.Accept(59999, out _) &&
        repeatBudget.Accept(60000, out var repeated) && repeated == 2,
        "R75 recurring log budget records first error and aggregates repeated failures at minute boundary");
    Check(new RotatingLogger.RepeatBudget().Accept(1, out _),
        "R75 independent diagnostic sources have independent budgets");
    var snapshotPath = Path.Combine(folder, "r75-snapshot-test.png");
    File.WriteAllBytes(snapshotPath, new byte[] { 1, 2, 3 });
    File.SetLastWriteTimeUtc(snapshotPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    var snapshotStamp = File.GetLastWriteTimeUtc(snapshotPath);
    using (var image = new MemoryStream())
    {
        image.Write(new byte[] { 1, 2, 3 });
        Check(MotionDesk.Widgets.WallpaperWindow.PublishSnapshotAsync(image, snapshotPath, () => true).GetAwaiter().GetResult() &&
            File.GetLastWriteTimeUtc(snapshotPath) == snapshotStamp,
            "R75 unchanged snapshot does not rewrite the existing image");
        image.Position = 0;
        image.Write(new byte[] { 4, 5, 6 });
        int approvals = 0;
        Check(!MotionDesk.Widgets.WallpaperWindow.PublishSnapshotAsync(image, snapshotPath, () => ++approvals < 3).GetAwaiter().GetResult() &&
            File.ReadAllBytes(snapshotPath).SequenceEqual(new byte[] { 1, 2, 3 }) &&
            !Directory.EnumerateFiles(folder, "*.capture").Any(),
            "R75 invalidated capture preserves previous image and cleans staging");
        Check(MotionDesk.Widgets.WallpaperWindow.PublishSnapshotAsync(image, snapshotPath, () => true).GetAwaiter().GetResult() &&
            File.ReadAllBytes(snapshotPath).SequenceEqual(new byte[] { 4, 5, 6 }),
            "R75 valid changed snapshot replaces previous image atomically");
    }
    var migrationDir = Path.Combine(folder, "migration-fixture");
    Directory.CreateDirectory(migrationDir);
    var migrationSource = Path.Combine(migrationDir, "source.json");
    var migrationTarget = Path.Combine(migrationDir, "target.json");
    var migrationTemp = Path.Combine(migrationDir, "publish.tmp");
    File.WriteAllText(migrationSource, "{\"layout\":1}");
    string migrationHash = MotionDesk.Containers.NativeMigration.Hash(migrationSource);
    MotionDesk.Containers.NativeMigration.PublishStagedFile(migrationSource, migrationTarget, migrationHash, migrationTemp);
    Check(File.ReadAllText(migrationTarget) == "{\"layout\":1}" && !File.Exists(migrationTemp),
        "R76 migration publishes verified payload without leaving temporary file");
    File.SetLastWriteTimeUtc(migrationTarget, new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc));
    var migrationStamp = File.GetLastWriteTimeUtc(migrationTarget);
    MotionDesk.Containers.NativeMigration.PublishStagedFile(migrationSource, migrationTarget, migrationHash, migrationTemp);
    Check(File.GetLastWriteTimeUtc(migrationTarget) == migrationStamp,
        "R76 migration retry is idempotent and does not rewrite matching snapshot");
    File.WriteAllText(migrationTarget, "{\"user\":true}");
    bool migrationCollision = false;
    try { MotionDesk.Containers.NativeMigration.PublishStagedFile(migrationSource, migrationTarget, migrationHash, migrationTemp); }
    catch (IOException) { migrationCollision = true; }
    Check(migrationCollision && File.ReadAllText(migrationTarget) == "{\"user\":true}",
        "R76 migration refuses to overwrite a user's existing snapshot");
    bool migrationTamper = false;
    File.WriteAllText(migrationSource, "{\"layout\":2}");
    try { MotionDesk.Containers.NativeMigration.PublishStagedFile(migrationSource, migrationTarget, migrationHash, migrationTemp); }
    catch (IOException) { migrationTamper = true; }
    Check(migrationTamper && File.ReadAllText(migrationTarget) == "{\"user\":true}" && !File.Exists(migrationTemp),
        "R76 corrupted staged payload cannot publish");
    Check(MotionDesk.Containers.NativeMigration.ValidSnapshotName("Default.json") &&
        !MotionDesk.Containers.NativeMigration.ValidSnapshotName("../Default.json") &&
        !MotionDesk.Containers.NativeMigration.ValidSnapshotName("Default.exe"),
        "R76 migration only accepts safe snapshot filenames");
    // Exercise the real native serializer and recovery entry point with a durable staged plan.
    var recoveryRoot = Path.Combine(migrationDir, "recovery");
    var recoveryStage = Path.Combine(recoveryRoot, "container-migration-v1");
    var recoverySnapshots = Path.Combine(recoveryRoot, "container-snapshots");
    var recoveryLive = Path.Combine(recoveryRoot, "containers-native.json");
    Directory.CreateDirectory(recoveryStage); Directory.CreateDirectory(recoverySnapshots);
    var migrationDoc = new Opsis.Containers.ContainersDocument();
    migrationDoc.Containers.Add(new Opsis.Containers.Container { Title = "Recovery fixture", Hidden = true });
    var stagedLive = Path.Combine(recoveryStage, "live.json");
    new Opsis.Containers.ContainersStore(stagedLive).Save(migrationDoc);
    var stagedSnapshot = Path.Combine(recoveryStage, "snapshot-000000.json");
    var serializerScratch = Path.Combine(migrationDir, "serializer");
    Directory.CreateDirectory(serializerScratch);
    Opsis.Containers.ContainerSnapshots.Save(migrationDoc, serializerScratch, "Recovery fixture");
    var serializedSnapshot = Directory.GetFiles(serializerScratch).Single();
    File.Copy(serializedSnapshot, stagedSnapshot);
    var snapshotName = Path.GetFileName(serializedSnapshot);
    var snapshotTarget = Path.Combine(recoverySnapshots, snapshotName);
    var liveHash = MotionDesk.Containers.NativeMigration.Hash(stagedLive);
    var snapshotHash = MotionDesk.Containers.NativeMigration.Hash(stagedSnapshot);
    File.WriteAllText(Path.Combine(recoveryStage, "owner.txt"), "OPSIS-NATIVE-MIGRATION-V1");
    File.WriteAllText(Path.Combine(recoveryStage, "manifest.json"), JsonSerializer.Serialize(new
    {
        Version = 1, LiveSha256 = liveHash, Snapshots = new[] { new { Name = snapshotName, Sha256 = snapshotHash } }, Summary = "Recovery fixture"
    }));
    var legacyMissing = Path.Combine(recoveryRoot, "legacy-missing");
    var noMonitors = Array.Empty<Opsis.Containers.MonitorInfo>();
    var recoveryStore = new Opsis.Containers.ContainersStore(recoveryLive);
    File.WriteAllText(snapshotTarget, "unrelated user snapshot");
    bool refusedRecovery = false;
    try { MotionDesk.Containers.NativeMigration.ImportIfNeeded(recoveryStore, legacyMissing, noMonitors); }
    catch (IOException) { refusedRecovery = true; }
    Check(refusedRecovery && !File.Exists(recoveryLive) && File.ReadAllText(snapshotTarget) == "unrelated user snapshot" && Directory.Exists(recoveryStage),
        "R89 full recovery refuses conflicting snapshot and retains staged plan");
    File.Delete(snapshotTarget);
    File.Copy(stagedSnapshot, snapshotTarget); // simulate crash after snapshot publication
    var recoveryStamp = File.GetLastWriteTimeUtc(snapshotTarget);
    Check(MotionDesk.Containers.NativeMigration.ImportIfNeeded(recoveryStore, legacyMissing, noMonitors) == "Recovery fixture" &&
        MotionDesk.Containers.NativeMigration.Hash(recoveryLive) == liveHash && MotionDesk.Containers.NativeMigration.Hash(snapshotTarget) == snapshotHash &&
        File.GetLastWriteTimeUtc(snapshotTarget) == recoveryStamp && !Directory.Exists(recoveryStage),
        "R89 full recovery resumes after partial publication, live layout last, without snapshot rewrite");
    Check(MotionDesk.Containers.NativeMigration.ImportIfNeeded(recoveryStore, legacyMissing, noMonitors) == null &&
        MotionDesk.Containers.NativeMigration.Hash(recoveryLive) == liveHash,
        "R89 existing native layout wins and repeated import is a no-op");
    var iconEntries = new[] { Path.Combine(folder,"user","Shared.txt"), Path.Combine(folder,"public","Shared.lnk"),
        Path.Combine(folder,"user","Unique.txt") };
    var uniqueIcons = MotionDesk.Widgets.DesktopIconVisibilityEngine.UniqueIconNames(new[] { iconEntries[0], iconEntries[2] }, iconEntries);
    Check(!uniqueIcons.Contains("Shared") && uniqueIcons.Contains("Shared.txt") && uniqueIcons.Contains("Unique"),
        "R77 ambiguous Explorer alias never hides another desktop file");
    Check(MotionDesk.Widgets.DesktopIconVisibilityEngine.ReleasedIconNames(new[] { iconEntries[0] }, new[] { iconEntries[1] })
        .SetEquals(new[] { "Shared.txt" }),
        "R77 shared display alias remains protected while another reference exists");
    Check(MotionDesk.Widgets.DesktopIconVisibilityEngine.ReleasedIconNames(new[] { iconEntries[0] },
        new[] { iconEntries[0].ToUpperInvariant() }).Count == 0,
        "R77 shared references are case insensitive");
    Check(MotionDesk.Widgets.TrayApplicationContext.ExperienceCadenceMs(false, false, false) == 900000 &&
        MotionDesk.Widgets.TrayApplicationContext.ExperienceCadenceMs(true, false, false) == 60000 &&
        MotionDesk.Widgets.TrayApplicationContext.ExperienceCadenceMs(false, true, false) == 60000 &&
        MotionDesk.Widgets.TrayApplicationContext.ExperienceCadenceMs(false, false, true) == 60000,
        "R79 experience timer uses safety-only cadence when optional consumers are off");
    Check(GpuMonitorService.NextProbeDelayMs(true) == 1500 &&
        GpuMonitorService.NextProbeDelayMs(false) == 300000,
        "R80 unavailable GPU sensor probe has demand-only five minute retry budget");
    var gpuAudit = GpuMonitorService.Instance.AuditState();
    Check(!gpuAudit.Sampling && gpuAudit.OpenAttempts == 0 && gpuAudit.CachedAdapters == 0,
        "R80 diagnostics do not initialize GPU sensors or trigger sampling");
    Check(!MotionDesk.Widgets.DeskStripHostEngine.NeedsMirrorMonitor(false, true) &&
        !MotionDesk.Widgets.DeskStripHostEngine.NeedsMirrorMonitor(true, false) &&
        MotionDesk.Widgets.DeskStripHostEngine.NeedsMirrorMonitor(true, true),
        "R81 DeskStrip mirror polling needs an enabled multi-monitor dock");
    Check(PluginHost.MayDispatch(true, false, false, true) &&
        !PluginHost.MayDispatch(false, false, false, true) &&
        !PluginHost.MayDispatch(true, true, false, true) &&
        !PluginHost.MayDispatch(true, false, true, true) &&
        !PluginHost.MayDispatch(true, false, false, false),
        "R81 retired, stopped, failed or held plugin cannot dispatch");
    var ownMenuExe = Path.Combine(folder, "Opsis.exe");
    Check(DesktopMenu.OwnsClassicFallback($"\"{ownMenuExe}\" --desktop-action new-container", ownMenuExe) &&
        !DesktopMenu.OwnsClassicFallback($"\"{Path.Combine(folder, "other", "Opsis.exe")}\" --desktop-action new-container", ownMenuExe) &&
        !DesktopMenu.OwnsClassicFallback(null, ownMenuExe),
        "R81 classic desktop fallback cleanup checks executable ownership");
    Check(GlassService.NeedsPolicyMonitor(1, GpuGlassMode.Auto, false) &&
        GlassService.NeedsPolicyMonitor(1, GpuGlassMode.Always, false) &&
        !GlassService.NeedsPolicyMonitor(0, GpuGlassMode.Auto, false) &&
        !GlassService.NeedsPolicyMonitor(1, GpuGlassMode.Blur, false) &&
        !GlassService.NeedsPolicyMonitor(1, GpuGlassMode.Solid, false) &&
        !GlassService.NeedsPolicyMonitor(1, GpuGlassMode.Always, true),
        "R82 glass policy monitoring requires an eligible visible surface and GPU mode");
    Check(!GlassService.MayRetireManager(1, false, 30000, false) &&
        !GlassService.MayRetireManager(0, true, 30000, false) &&
        !GlassService.MayRetireManager(0, false, 29999, false) &&
        !GlassService.MayRetireManager(0, false, 30000, true) &&
        GlassService.MayRetireManager(0, false, 30000, false),
        "R82 shared glass manager retires only after idle grace and no active pane/demand/diagnostic hold");
    var sessionMask = SessionBackgroundService.DockRestore | SessionBackgroundService.DiscordPresence;
    Check(RuntimeSessionPolicy.Allows(sessionMask, SessionBackgroundService.DockRestore, true, false) &&
        RuntimeSessionPolicy.Allows(sessionMask, SessionBackgroundService.DiscordPresence, true, false) &&
        !RuntimeSessionPolicy.Allows(sessionMask, SessionBackgroundService.UsageAlerts, true, false),
        "R83 session mask permits independent selected services to coexist");
    Check(!RuntimeSessionPolicy.Allows(SessionBackgroundService.All, SessionBackgroundService.UsageAlerts, false, false) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.All, SessionBackgroundService.UsageAlerts, true, true) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.All, SessionBackgroundService.None, true, false) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.All, (SessionBackgroundService)4096, true, false),
        "R83 session selection cannot bypass saved opt-in, safe mode or known capabilities");
    var sessionPrefs = new AppSettings { RestoreWindowsAfterDocking = true, UsageAlertsEnabled = true };
    Check(MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(sessionPrefs, false, SessionBackgroundService.DockRestore) == (true, false) &&
        MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(sessionPrefs, false, SessionBackgroundService.None) == (false, false) &&
        MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(sessionPrefs, false, SessionBackgroundService.All) == (true, true) &&
        sessionPrefs.RestoreWindowsAfterDocking && sessionPrefs.UsageAlertsEnabled,
        "R83 reversible session filters leave persisted preferences unchanged");
    var desktopMask = SessionBackgroundService.All & ~SessionBackgroundService.Background;
    Check(RuntimeSessionPolicy.ReplaceBackground(desktopMask, true) == SessionBackgroundService.All &&
        RuntimeSessionPolicy.ReplaceBackground(SessionBackgroundService.All, false) == desktopMask &&
        RuntimeSessionPolicy.ReplaceBackground(SessionBackgroundService.None, true) == SessionBackgroundService.Background,
        "R84 background bulk toggle preserves independent desktop service selection");
    Check(MotionDesk.Widgets.WallpaperHostEngine.AllowsAnimatedScene(true, true) &&
        !MotionDesk.Widgets.WallpaperHostEngine.AllowsAnimatedScene(true, false) &&
        !MotionDesk.Widgets.WallpaperHostEngine.AllowsAnimatedScene(false, true),
        "R84 animation session admission is separate from requested wallpaper state");
    Check(OpsisMusicOverlay.DesktopMusicController.CanRun(true, true, false) &&
        OpsisMusicOverlay.DesktopMusicController.CanRun(true, false, true) &&
        !OpsisMusicOverlay.DesktopMusicController.CanRun(false, true, true) &&
        !OpsisMusicOverlay.DesktopMusicController.CanRun(true, false, false),
        "R86 music session admission gates both saved playback and explicit preview");
    Check(RuntimeSessionPolicy.Allows(SessionBackgroundService.MusicVisualizer | SessionBackgroundService.Widgets, SessionBackgroundService.MusicVisualizer, true, false) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.AnimatedWallpaper, SessionBackgroundService.MusicVisualizer, true, false),
        "R86 music visualizer admission is independent of animation and widgets");
    Check(MotionDesk.Appearance.AppearanceService.MayRunAutomatic(true, true, true, true, false) &&
        !MotionDesk.Appearance.AppearanceService.MayRunAutomatic(false, true, true, true, false) &&
        !MotionDesk.Appearance.AppearanceService.MayRunAutomatic(true, true, true, false, false) &&
        !MotionDesk.Appearance.AppearanceService.MayRunAutomatic(true, true, false, true, false) &&
        !MotionDesk.Appearance.AppearanceService.MayRunAutomatic(true, true, true, true, true),
        "R87 automatic appearance requires session admission, running owner and successful cleanup");
    Check(RuntimeSessionPolicy.Allows(SessionBackgroundService.AppearanceAutomatic | SessionBackgroundService.MusicVisualizer, SessionBackgroundService.AppearanceAutomatic, true, false) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.Widgets, SessionBackgroundService.AppearanceAutomatic, true, false),
        "R87 automatic appearance admission independent of other desktop services");
    Check(AutoTileService.AllowsAutomatic(true, true, false, false) &&
        !AutoTileService.AllowsAutomatic(false, true, false, false) &&
        !AutoTileService.AllowsAutomatic(true, false, false, false) &&
        !AutoTileService.AllowsAutomatic(true, true, true, false) &&
        !AutoTileService.AllowsAutomatic(true, true, false, true),
        "R88 automatic tiling requires saved and session demand and compatibility clearance");
    Check(PerformanceGovernor.DropWithoutSampler(2, true) == 2 && PerformanceGovernor.DropWithoutSampler(2, false) == 0,
        "R88 session pause retains adaptive reduction while disabling the saved profile resets it");
    Check(RuntimeSessionPolicy.Allows(SessionBackgroundService.AutoTile | SessionBackgroundService.AdaptiveSampling, SessionBackgroundService.AutoTile, true, false) &&
        !RuntimeSessionPolicy.Allows(SessionBackgroundService.AutoTile, SessionBackgroundService.AdaptiveSampling, true, false),
        "R88 independent automatic tiling and adaptive sampling admission");
    var widgetFixture = new MotionDesk.Widgets.WidgetSnapshot { WidgetId = "clock", X = 24, Y = 40, Width = 320, Height = 180, IsLocked = true, MonitorId = "m1" };
    var widgetPlan = MotionDesk.Widgets.WidgetHostEngine.CopyLayout(new[] { widgetFixture, new MotionDesk.Widgets.WidgetSnapshot { WidgetId = "CLOCK" } });
    widgetFixture.Width = 999;
    Check(widgetPlan.Count == 1 && widgetPlan[0].Width == 320 && widgetPlan[0].IsLocked && widgetPlan[0].MonitorId == "m1",
        "R85 retained widget layout copies geometry and lock state and avoids duplicate identities");
    var widgetBounds = MotionDesk.Widgets.WidgetHostEngine.ResolveSnapshotBounds(widgetPlan[0], new System.Drawing.Rectangle(0,0,1000,700));
    Check(widgetBounds == new System.Drawing.Rectangle(24,40,320,180),
        "R85 restores both dimensions and position from retained widget snapshot");
    var mappedWidget = new MotionDesk.Widgets.WidgetSnapshot { WidgetId = "clock", Width = 400, Height = 200, RelativeX = 0.9, RelativeY = 0.9 };
    Check(MotionDesk.Widgets.WidgetHostEngine.ResolveSnapshotBounds(mappedWidget, new System.Drawing.Rectangle(-800,0,800,600)) ==
        new System.Drawing.Rectangle(-400,400,400,200),
        "R85 retained widget geometry clamps to the available monitor");
    bool invalidWidgetId = false;
    try { MotionDesk.Widgets.WidgetHostEngine.CopyLayout(new[] { new MotionDesk.Widgets.WidgetSnapshot { WidgetId = "../other" } }); }
    catch (ArgumentException) { invalidWidgetId = true; }
    Check(invalidWidgetId, "R85 invalid widget identity rejected before layout mutation");
    var opt = new AppSettings { RestoreWindowsAfterDocking = false, UsageAlertsEnabled = false };
    Check(MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(opt, false) == (false, false),
        "R65 disabled optional tray services have no timer demand");
    opt.RestoreWindowsAfterDocking = true;
    Check(MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(opt, false) == (true, false),
        "R65 docking restore selection does not enable usage alerts");
    opt.UsageAlertsEnabled = true;
    Check(MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(opt, false) == (true, true) &&
        MotionDesk.Widgets.TrayApplicationContext.SelectOptionalServices(opt, true) == (false, false),
        "R65 explicit selections are held in safe mode");
    Check(!MotionDesk.UI.BackdropContrast.NeedsSampling(true, true, false) &&
        !MotionDesk.UI.BackdropContrast.NeedsSampling(false, true, true) &&
        !MotionDesk.UI.BackdropContrast.NeedsSampling(true, false, true) &&
        MotionDesk.UI.BackdropContrast.NeedsSampling(true, true, true),
        "R65 backdrop sampler needs transparent visible widgets and user opt-in");
    Check(!VirtualWorkspaces.NeedsPolling(0, 0) && VirtualWorkspaces.NeedsPolling(1, 0) &&
        VirtualWorkspaces.NeedsPolling(0, 1),
        "R65 desktop polling needs a saved assignment or an open observer");
    Check(!MotionDesk.Widgets.WallpaperHostEngine.NeedsScene(false, false, false) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsScene(true, false, false) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsScene(false, true, false) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsScene(false, false, true),
        "R66 renderer demand is retained by any selected scene consumer");
    Check(!MotionDesk.Widgets.WallpaperHostEngine.NeedsAutoMute(false, true, true) &&
        !MotionDesk.Widgets.WallpaperHostEngine.NeedsAutoMute(true, false, true) &&
        !MotionDesk.Widgets.WallpaperHostEngine.NeedsAutoMute(true, true, false) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsAutoMute(true, true, true),
        "R66 audio detector demand requires active audio wallpaper and auto mute");
    Check(!MotionDesk.Widgets.WallpaperHostEngine.NeedsLockScreenSync(false, true, "snapshot.png") &&
        !MotionDesk.Widgets.WallpaperHostEngine.NeedsLockScreenSync(true, false, "snapshot.png") &&
        !MotionDesk.Widgets.WallpaperHostEngine.NeedsLockScreenSync(true, true, null) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsLockScreenSync(true, true, "snapshot.png"),
        "R66 lock screen timer needs scene, opt-in and snapshot destination");
    Check(!MotionDesk.Widgets.WallpaperHostEngine.NeedsSchedule(false, true) &&
        !MotionDesk.Widgets.WallpaperHostEngine.NeedsSchedule(true, false) &&
        MotionDesk.Widgets.WallpaperHostEngine.NeedsSchedule(true, true),
        "R66 schedule timer needs enabled schedule and active rules");
    Check(OpsisMusicOverlay.DesktopMusicController.SelectTickInterval(false, true, true) == 42 &&
        OpsisMusicOverlay.DesktopMusicController.SelectTickInterval(false, true, false) == 100 &&
        OpsisMusicOverlay.DesktopMusicController.SelectTickInterval(false, false, false) == 250 &&
        OpsisMusicOverlay.DesktopMusicController.SelectTickInterval(true, true, true) == 500,
        "R63 music cadence throttles idle, backpressure and suspension while preserving active rendering");
    Check(MotionDesk.UI.OpsisStartButtonArt.Styles.Length == 11 &&
        MotionDesk.UI.OpsisStartButtonArt.IsValid("Opsis Glass") &&
        !MotionDesk.UI.OpsisStartButtonArt.IsValid("not-a-design"),
        "P1 Start button gallery has eleven validated design choices");
    Check(StartStudioPreferences.Read().StartButtonStyle == "Opsis Glass",
        "existing users default to the Opsis Glass emblem");
    StartStudioPreferences.SetStartButton("Aurora");
    Check(StartStudioPreferences.Read().StartButtonStyle == "Aurora",
        "selected Opsis Start button survives configuration round-trip");
    bool invalidStartRejected = false;
    try { StartStudioPreferences.SetStartButton("UnsafeStyle"); }
    catch (ArgumentOutOfRangeException) { invalidStartRejected = true; }
    Check(invalidStartRejected, "unrecognized Start design cannot be persisted");
    using (var canvas = new System.Drawing.Bitmap(64, 64))
    using (var g = System.Drawing.Graphics.FromImage(canvas))
    {
        foreach (var design in MotionDesk.UI.OpsisStartButtonArt.Styles)
            MotionDesk.UI.OpsisStartButtonArt.Draw(g,
                new System.Drawing.Rectangle(0, 0, 64, 64), design);
    }
    Check(true, "all eleven P1/P2 button designs render on the Windows GDI+ runner");
    StartStudioPreferences.SetStartButton("Win11 Glass");
    Check(StartStudioPreferences.Read().StartButtonStyle == "Win11 Glass",
        "P2 rounded-square Windows 11 Start button persists");
    Check(MotionDesk.UI.OpsisStartButtonArt.IsValid("Win11 Sharp") &&
        MotionDesk.UI.OpsisStartButtonArt.IsValid("Opsis Square"),
        "P2 sharp and rounded square Start variants are available");
    StartStudioPreferences.SetStartButton("Opsis Glass");
    var catalogRoot = Path.Combine(folder, "gallery-test");
    void CreateCatalogFixture(string relative, string id)
    {
        var dir = Path.Combine(catalogRoot, relative);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<!doctype html><title>fixture</title>");
        File.WriteAllText(Path.Combine(dir, "opsis-wallpaper.json"), JsonSerializer.Serialize(new {
            WallpaperId=id, Name=id, Category="Abstract", Collection="OPSIS 100 3D Wallpapers"
        }));
        using var image = new System.Drawing.Bitmap(8, 8);
        image.Save(Path.Combine(dir, "poster.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
    CreateCatalogFixture("wallpapers/samples/opsis-glass-signature", "opsis-glass-signature");
    CreateCatalogFixture("wallpapers/packs/opsis-100/opsis-glass-signature", "opsis-glass-signature");
    CreateCatalogFixture("wallpapers/packs/opsis-100/animated-one", "animated-one");
    CreateCatalogFixture("wallpapers/packs/opsis-100/ancient-athens", "ancient-athens");
    var gallery = WallpaperGalleryCatalog.Discover(catalogRoot);
    Check(gallery.Count == 2 && gallery.Count(x => x.Id == "opsis-glass-signature") == 1,
        "web wallpaper catalog discovers addon scenes, deduplicates Signature and excludes Ancient Athens");
    Check(gallery.Single(x => x.Id == "opsis-glass-signature").IndexPath ==
        Path.Combine(catalogRoot, "wallpapers/samples/opsis-glass-signature/index.html".Replace('/', Path.DirectorySeparatorChar)),
        "built-in Signature has priority over legacy addon copy");
    Check(OpsisPluginPackageAudit.Classify("OpsisNewsCrawler.OpsisPlugin.dll",
              new Version(1, 0, 0, 0)) == "CompatibleABI",
        "known plugin identity matches Opsis pinned 1.0.0.0 ABI");
    Check(OpsisPluginPackageAudit.Classify("OpsisNewsCrawler.OpsisPlugin.dll",
              new Version(0, 24, 1, 0)) == "IncompatibleABI",
        "incompatible NewsCrawler plugin binary version is rejected before execution");
    Check(OpsisPluginPackageAudit.Classify("UnknownPlugin.dll",
              new Version(1, 0, 0, 0)) == "RequiresTrustReview",
        "unknown plugin is not incorrectly certified by matching an assembly version");
    Check(OpsisPluginPackageAudit.Classify("Chronikologio.OpsisPlugin.dll", null) == "InvalidMetadata",
        "missing plugin assembly metadata blocks ABI certification");
    Check(OpsisIntegrationAudit.Baseline28.Count >= 25, "2.8.0.2510 baseline inventory recorded");
    Check(OpsisIntegrationAudit.Baseline28.Select(x => x.Key)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count() == OpsisIntegrationAudit.Baseline28.Count,
        "baseline feature identifiers uniquely map to existing engine owners");
    var auditNoConflict = OpsisIntegrationAudit.Evaluate(
        OpsisModCore.Catalog.Select(m => m.Id),
        Array.Empty<OpsisIntegrationAudit.PluginEntry>(),
        Array.Empty<(string, string, string)>(),
        false, Array.Empty<OpsisModCompatibility.Finding>(), false);
    Check(!auditNoConflict.Any(x => x.Severity == "Block"),
        "shared 2.8 features do not falsely trigger duplicate-engine warnings");
    var auditCollisions = OpsisIntegrationAudit.Evaluate(
        new[] { "taskbar.native", "taskbar.native" },
        new[] { new OpsisIntegrationAudit.PluginEntry("news", "1.0", true),
                new OpsisIntegrationAudit.PluginEntry("news", "0.9", true) },
        new[] { ("plugin.one", "clock", "installed"),
                ("plugin.one", "clock", "user") },
        true,
        new[] { new OpsisModCompatibility.Finding("StartAllBack", "High",
            "Start / Taskbar", "Use one shell owner") }, false);
    Check(auditCollisions.Count(x => x.Severity == "Block") >= 3,
        "audit rejects duplicate module IDs, loaded plugin identities and colliding shell owners");
    Check(auditCollisions.Any(x => x.Domain == "plugins.gallery"),
        "audit identifies duplicate Gallery contribution identities");
    Check(!OpsisIntegrationAudit.TaskbarOverlayAllowed(true,
        new[] { new OpsisModCompatibility.Finding("ExplorerPatcher", "High", "Taskbar", "Disable competing shell") },
        false), "shell guard blocks overlapping active replacement taskbars");
    Check(OpsisIntegrationAudit.TaskbarOverlayAllowed(true,
        Array.Empty<OpsisModCompatibility.Finding>(), false),
        "shell guard permits explicitly requested Opsis taskbar without known conflicts");
    var placementConflict = OpsisIntegrationAudit.Evaluate(
        Array.Empty<string>(), Array.Empty<OpsisIntegrationAudit.PluginEntry>(),
        Array.Empty<(string, string, string)>(), false,
        Array.Empty<OpsisModCompatibility.Finding>(), false,
        autoTileEnabled: true, windowRulesEnabled: true);
    Check(placementConflict.Any(x => x.Severity == "Block" &&
        x.Domain == "desktop.window-position"),
        "only one native engine may own app window placement");
    var dockAndAccentOverlaps = OpsisIntegrationAudit.Evaluate(
        Array.Empty<string>(), Array.Empty<OpsisIntegrationAudit.PluginEntry>(),
        Array.Empty<(string, string, string)>(), true,
        Array.Empty<OpsisModCompatibility.Finding>(), false,
        desktopToolbarEnabled: true, deskStripEnabled: true,
        wallpaperAccentEnabled: true, followWindowsAccent: true);
    Check(dockAndAccentOverlaps.Count(x => x.Severity == "Review") >= 3,
        "audit warns about overlapping taskbar, DeskStrip, toolbar and accent owners");


    var windhawkMods = OpsisModCompatibility.EvaluateWindhawkMods(new (string Id, bool? Disabled)[]
    {
        ("windows-11-taskbar-styler", false), ("WINDOWS-11-TASKBAR-STYLER", false),
        ("windows-11-start-menu-styler", true), ("custom-unknown", null)
    });
    Check(windhawkMods.Count == 2 && windhawkMods.Any(m => m.Area == "Taskbar") &&
        windhawkMods.Any(m => m.Area == "Unknown mod" && m.Risk == "Review") &&
        windhawkMods.Count(OpsisModCompatibility.BlocksTaskbarOverlay) == 1,
        "Windhawk per-mod detection deduplicates, excludes disabled and holds competing taskbar styling");
    Check(!OpsisIntegrationAudit.TaskbarOverlayAllowed(true, windhawkMods, false), "configured competing taskbar mod prevents simultaneous Opsis overlay");
    Check(!OpsisModCompatibility.EvaluateWindhawkMods(new (string Id, bool? Disabled)[] { ("windows-11-taskbar-styler", true) }).Any(), "disabled competing Windhawk mod does not block Opsis");
    Check(NativeStartPreferences.IsValid("Start_Layout", 2) && !NativeStartPreferences.IsValid("Start_Layout", 3) &&
        NativeStartPreferences.IsValid("ShowAllPinsList", 1) && !NativeStartPreferences.IsValid("unregistered", 1),
        "native Start preference allowlist validates values without modifying runner registry");
    Check(OpsisModCore.Catalog.Count >= 10, "unified module catalog available");
    Check(OpsisModCore.Read().Enabled.Count == 0, "module flags disabled by default");
    OpsisModCore.SetModule("windows.hot-corners", true, 22631, false);
    Check(OpsisModCore.Read().Enabled["windows.hot-corners"], "module enabled with compatible build");
    OpsisModCore.SetModule("windows.hot-corners", false, 22631, false);
    Check(!OpsisModCore.Read().Enabled["windows.hot-corners"], "module independently disabled");
    bool rejected = false;
    try { OpsisModCore.SetModule("start.shell-replacement", true, 22631, false); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "planned modules cannot be activated as implemented");
    var weatherPlan = new OpsisModCore.ModuleConfiguration();
    weatherPlan.Enabled["wallpaper.scene-composer"] = true;
    Check(OpsisModCore.Validate(weatherPlan, 22631, false).Any(e => e.Contains("requires wallpaper.weather")),
        "implemented weather composer requires its existing weather engine");
    weatherPlan.Enabled["wallpaper.weather"] = true;
    Check(OpsisModCore.Validate(weatherPlan, 22631, false).Count == 0,
        "implemented weather composer is valid once its prerequisite is enabled");
    Check(OpsisModCore.Validate(weatherPlan, 22631, true).Count > 0,
        "safe mode blocks optional module activation");
    OpsisModCore.SetOption("widgets.opsis", "category", "Weather");
    Check(OpsisModCore.Read().Options["widgets.opsis"]["category"] == "Weather", "typed module options persisted");
    OpsisModCore.SetOption("WIDGETS.OPSIS", "CATEGORY", "Weather");
    Check(OpsisModCore.Read().Options.Keys.Count(k => k.Equals("widgets.opsis", StringComparison.OrdinalIgnoreCase)) == 1 &&
        OpsisModCore.Read().Options["widgets.opsis"].Count == 1, "case-insensitive module and option identity preserved after JSON round trip");
    OpsisModCore.SetModule("WINDOWS.HOT-CORNERS", false, 22631, false);
    Check(OpsisModCore.Read().Enabled.Count == 1, "case aliases do not duplicate module flags");
    Check(MotionDesk.Widgets.DeskFlipEngine.Style == "Flip 3D", "DeskFlip default remains compatible");
    foreach (var style in new[] { "Flip 3D", "Thumbnail grid", "Compact list" })
    {
        MotionDesk.Widgets.DeskFlipEngine.SetStyle(style);
        Check(MotionDesk.Widgets.DeskFlipEngine.Style == style, "DeskFlip style roundtrip " + style);
    }
    Check(!MotionDesk.Widgets.DeskFlipEngine.IsValidStyle("unknown"), "unknown DeskFlip style rejected");

    var unchangedAppearance = new AppearancePresets.Preset { Name = "Sample" };
    var sameAppearance = new AppearancePresets.Preset { Name = "Different name" };
    Check(AppearancePresets.Equivalent(unchangedAppearance, sameAppearance),
        "P2 appearance equivalent comparison ignores metadata and avoids needless writes");
    sameAppearance.Hidden = 1;
    Check(!AppearancePresets.Equivalent(unchangedAppearance, sameAppearance),
        "P2 appearance change detection notices Explorer changes");

    var portableSource = Path.Combine(folder, "P2 portable source.json");
    var portable = new AppearancePresets.Preset { Name = "P2 portable test" };
    AppearancePresets.SavePortable(portable);
    AppearancePresets.ExportPortable(portable.Name, portableSource);
    Check(File.Exists(portableSource), "P2 portable JSON export created");
    var exportJson = File.ReadAllText(portableSource);
    Check(exportJson.Contains("P2 portable test"), "P2 export preserves validated preset");
    AppearancePresets.Delete(portable.Name);
    Check(AppearancePresets.ImportPortable(portableSource) == portable.Name,
        "P2 validated portable import round-trip without applying settings");
    var unsafeSource = Path.Combine(folder, "P2 unsafe.json");
    File.WriteAllText(unsafeSource, """{"Schema":98,"Name":"invalid"}""");
    bool invalidImportRejected = false;
    try { AppearancePresets.ImportPortable(unsafeSource); }
    catch (InvalidDataException) { invalidImportRejected = true; }
    Check(invalidImportRejected, "P2 imported JSON rejects unexpected schema");
    AppearancePresets.Delete(portable.Name);

    var scene = new WeatherSceneComposer.Scene {
        Name = "Athens Dawn", Weather = "Rain", Time = "Dawn",
        Wind = "FreshBreeze", Landmark = "Auto"
    };
    WeatherSceneComposer.Validate(scene);
    WeatherSceneComposer.Save(scene);
    Check(WeatherSceneComposer.List().Contains("Athens Dawn"), "scene saved");
    Check(WeatherSceneComposer.Read("Athens Dawn").Time == "Dawn", "scene round trip");
    rejected = false;
    try { WeatherSceneComposer.Validate(new WeatherSceneComposer.Scene { Name = "../escape" }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "unsafe scene path rejected");
    var themeFile = Path.Combine(folder, "Exported.opsis-theme");
    OpsisThemeBundle.ExportCurrent("Portable Theme", themeFile);
    Check(File.Exists(themeFile), "theme exported");
    var imported = OpsisThemeBundle.Import(themeFile);
    Check(imported == "Portable Theme", "theme imported with matching name");
    Check(AppearancePresets.List().Contains("Portable Theme"), "appearance contribution installed");
    Check(WeatherSceneComposer.List().Contains("Portable Theme"), "weather scene contribution installed");
    rejected = false;
    try { OpsisThemeBundle.Import(themeFile); }
    catch (IOException) { rejected = true; }
    Check(rejected, "theme import refuses overwrite");
    scene.Weather = "Unsafe";
    rejected = false;
    try { WeatherSceneComposer.Validate(scene); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "unknown renderer settings rejected");

    var conf = new OpsisAutomationStudio.Configuration();
    conf.Rules.Add(new OpsisAutomationStudio.Rule {
        Name = "Morning theme", Id = "morning", Enabled = true, Trigger = "DailyAt",
        Hour = 9, Minute = 15, Action = "ThemeMode", Target = "Light"
    });
    conf.Rules.Add(new OpsisAutomationStudio.Rule {
        Name = "Startup theme", Id = "boot", Enabled = true, Trigger = "Startup",
        Action = "ThemeMode", Target = "Dark"
    });
    OpsisAutomationStudio.Save(conf);
    Check(OpsisAutomationStudio.Read().Rules.Count == 2, "automation persisted");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 15, 0), false).Count == 1,
        "time rule triggers at requested minute");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 16, 0), false).Count == 0,
        "time rule does not trigger outside requested minute");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 15, 0), true).Count == 1,
        "startup rule isolated from clock rules");
    rejected = false;
    conf.Rules[0].Target = "NotAWindowsTheme";
    try { OpsisAutomationStudio.Validate(conf); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "automation rejects unsupported actions");
    conf.Rules[0].Target = "Light";

    Check(!WindowsUpdateGuard.EvaluateStartup(), "fresh Windows baseline requires no review");
    Check(!WindowsUpdateGuard.RequiresReview, "baseline approved");
    var stateFile = Path.Combine(OpsisRecoveryHub.Root, "update-guard.json");
    File.WriteAllText(stateFile, JsonSerializer.Serialize(new WindowsUpdateGuard.State
    {
        LastApprovedFingerprint = "Build=OLD;UBR=0"
    }));
    Check(WindowsUpdateGuard.EvaluateStartup(), "changed Windows build triggers review");
    Check(WindowsUpdateGuard.RequiresReview, "hold persists after change");
    WindowsUpdateGuard.ApproveCurrentBuild();
    Check(WindowsUpdateGuard.RuntimeHold && WindowsUpdateGuard.RequiresReview && WindowsUpdateGuard.RestartRequired, "user approval keeps the process latch until restart");
    Check(string.IsNullOrEmpty(WindowsUpdateGuard.Read().PendingFingerprint), "approval is persisted");
    WindowsUpdateGuard.ResetProcessLatchForTests();
    Check(!WindowsUpdateGuard.RequiresReview && !WindowsUpdateGuard.RuntimeHold, "after a restart the approved build is no longer held");
    File.WriteAllText(stateFile, "{ malformed");
    Check(WindowsUpdateGuard.RequiresReview && WindowsUpdateGuard.RuntimeHold, "malformed guard state fails closed");
    File.WriteAllText(stateFile, JsonSerializer.Serialize(new WindowsUpdateGuard.State { LastApprovedFingerprint = OpsisRecoveryHub.WindowsFingerprint() }));
    WindowsUpdateGuard.ApproveCurrentBuild();
    Check(WindowsUpdateGuard.RequiresReview && WindowsUpdateGuard.RestartRequired, "explicit approval after a repaired state still waits for a restart");
    WindowsUpdateGuard.ResetProcessLatchForTests();
    Check(!WindowsUpdateGuard.RequiresReview, "restart clears the latch once the state is valid");

    var windowsInventory = OpsisRecoveryHub.InspectWindowsShell();
    Check(windowsInventory.Files.Count == 6, "Windows shell file inventory includes six monitored binaries");
    Check(windowsInventory.Files.Any(x => x.Available), "Windows shell inventory found genuine system binaries");
    var originalArchive = OpsisRecoveryHub.BackupMonitoredWindowsOriginals();
    Check(originalArchive.Files.Count == 6, "original Windows shell archive manifest created");
    Check(OpsisRecoveryHub.BackupMonitoredWindowsOriginals().Fingerprint == originalArchive.Fingerprint,
        "Windows original archive deduplicates matching OS versions");
    var previousShell = new OpsisRecoveryHub.SystemInventory
    {
        Fingerprint = "Build=OUTDATED;UBR=0", RecordedUtc = DateTime.UtcNow
    };
    File.WriteAllText(Path.Combine(OpsisRecoveryHub.Root, "system-inventory.json"),
        JsonSerializer.Serialize(previousShell));
    var detectedChange = OpsisRecoveryHub.InspectUpdateImpact();
    Check(detectedChange.Contains("fingerprint changed"), "shell impact inspection detects build change");
    Check(OpsisRecoveryHub.ReadLastUpdateImpact().Contains("OUTDATED"),
        "prior update report persists after comparison");
    OpsisRecoveryHub.InspectUpdateImpact();
    Check(OpsisRecoveryHub.ReadLastUpdateImpact().Contains("OUTDATED"),
        "repeat no-change scan does not erase update evidence");


    Check(OpsisFeatureSwitcher.CanSwitch("windows.hot-corners"), "core module has operational hot corner adapter");
    Check(!OpsisFeatureSwitcher.CanSwitch("start.shell-replacement"), "planned Start replacement not switchable");
    Check(OpsisFeatureSwitcher.CanSwitch("cursor.effects"), "Cursor FX has a real optional module adapter");
    Check(!AppSettings.Load().CursorFxEnabled, "Cursor FX defaults to disabled for existing users");
    var cursorPreferences = AppSettings.Load();
    cursorPreferences.CursorFxStyle = "Ripple";
    cursorPreferences.CursorFxIntensity = 80;
    cursorPreferences.Save();
    Check(AppSettings.Load().CursorFxStyle == "Ripple" &&
          AppSettings.Load().CursorFxIntensity == 80,
          "Cursor FX settings persist without changing system mouse hooks");

    OpsisFeatureSwitcher.Set("windows.hot-corners", true);
    Check(AppSettings.Load().HotCornersEnabled && OpsisFeatureSwitcher.IsEnabled("windows.hot-corners"),
        "core enabled a real Hot Corners service preference");
    OpsisFeatureSwitcher.Set("windows.hot-corners", false);
    Check(!AppSettings.Load().HotCornersEnabled, "core restored Hot Corners opt-out");
    var plugin = Path.Combine(folder, "plugins", "example-plugin");
    Directory.CreateDirectory(plugin);
    File.WriteAllText(Path.Combine(plugin, "cover.webp"), "fixture");
    File.WriteAllText(Path.Combine(plugin, "opsis-contributions.json"), JsonSerializer.Serialize(new
    {
        Schema = 1, PluginId = "example.plugin", Name = "Example",
        Contributions = new[] { new {
            Id = "city-wallpaper", Type = "wallpaper", Category = "Greece",
            Title = "Athens", Asset = "cover.webp", Author = "Test", Description = "Fixture"
        } }
    }));
    Check(OpsisEcosystemGallery.Discover().Any(x => x.PluginId == "example.plugin"), "plugin wallpaper contribution discovered");
    var addonZip = Path.Combine(folder, "addon-fixture.zip");
    using (var zip = System.IO.Compression.ZipFile.Open(addonZip, System.IO.Compression.ZipArchiveMode.Create))
    {
        using (var writer = new StreamWriter(zip.CreateEntry("opsis-contributions.json").Open()))
            writer.Write(JsonSerializer.Serialize(new OpsisEcosystemGallery.Manifest { PluginId = "fixture.images", Version = "1.0.0",
                Contributions = new() { new() { Id = "image1", Type = "wallpaper", Title = "Fixture image", Category = "Test", Asset = "images/fixture.png" } } }));
        using var png = new System.Drawing.Bitmap(8, 8);
        using var pngData = new MemoryStream();
        png.Save(pngData, System.Drawing.Imaging.ImageFormat.Png);
        using var entry = zip.CreateEntry("images/fixture.png").Open();
        pngData.Position = 0; pngData.CopyTo(entry);
    }
    Check(OpsisEcosystemGallery.ImportWallpaperPack(addonZip) == 1 &&
        OpsisEcosystemGallery.Discover().Any(x => x.PluginId == "fixture.images" && x.Category == "Test"),
        "data-only wallpaper pack imported and categorized without applying wallpaper");
    // optional: the real 200-image static pack (OPSIS_TEST_WEBP200=<path of Opsis-Abstract-Horizons-200-WebP.zip>)
    var webp200 = Environment.GetEnvironmentVariable("OPSIS_TEST_WEBP200");
    if (!string.IsNullOrEmpty(webp200) && File.Exists(webp200))
    {
        var clock200 = System.Diagnostics.Stopwatch.StartNew();
        int imported200 = OpsisEcosystemGallery.ImportWallpaperPack(webp200);
        Check(imported200 == 200 && OpsisEcosystemGallery.Discover().Count(x => x.PluginId == "opsis.procedural.static.200") == 200,
            "real 200 static WebP pack imported: 200 distinct, decoded, static (" + clock200.ElapsedMilliseconds + " ms)");
    }
    {
        // Explorer capture is bounded: a Shell automation that never answers times out instead of freezing the UI
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool timedOut = false;
        try { ExplorerSessionStore.Capture(() => { System.Threading.Thread.Sleep(60000); return Array.Empty<string>(); }, 300); }
        catch (TimeoutException) { timedOut = true; }
        Check(timedOut && clock.ElapsedMilliseconds < 5000, "Explorer capture times out instead of hanging");
        bool sta = false;
        ExplorerSessionStore.Capture(() => { sta = System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA; return Array.Empty<string>(); }, 3000);
        Check(sta, "Explorer capture runs on an STA worker");
    }
    var localFolder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
    Check(ExplorerSessionStore.IsLocalFolderPath(localFolder) &&
        !ExplorerSessionStore.IsLocalFolderPath(@"\\server\share") &&
        !ExplorerSessionStore.IsLocalFolderPath("https://example.invalid") &&
        ExplorerSessionStore.Normalize(new[] { localFolder, localFolder }).Length == 1,
        "Explorer session accepts only distinct local filesystem paths");
    ExplorerSessionStore.Save(new[] { localFolder });
    Check(ExplorerSessionStore.Read().SequenceEqual(new[] { localFolder }, StringComparer.OrdinalIgnoreCase),
        "Explorer folder session roundtrip without launching Explorer");
    var webpBytes = Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvB8ABAAdQs840s/+BiOh/AAA=");
    var webpZip = Path.Combine(folder, "addon-webp.zip");
    using (var zip = System.IO.Compression.ZipFile.Open(webpZip, System.IO.Compression.ZipArchiveMode.Create))
    {
        using (var writer = new StreamWriter(zip.CreateEntry("opsis-contributions.json").Open()))
            writer.Write(JsonSerializer.Serialize(new OpsisEcosystemGallery.Manifest { PluginId = "fixture.webp", Version = "1.0.0",
                Contributions = new() { new() { Id = "webp", Type = "wallpaper", Title = "WebP fixture", Category = "Test", Asset = "images/fixture.webp" } } }));
        using var entry = zip.CreateEntry("images/fixture.webp").Open();
        entry.Write(webpBytes);
    }
    Check(OpsisEcosystemGallery.ImportWallpaperPack(webpZip) == 1, "static WebP pack imported");
    var webpItem = OpsisEcosystemGallery.Discover().Single(x => x.PluginId == "fixture.webp");
    Check(Path.GetExtension(webpItem.AssetPath) == ".webp" && File.ReadAllBytes(webpItem.AssetPath).SequenceEqual(webpBytes),
        "WebP original preserved without transcoding");
    using (var thumbnail = WallpaperPackImage.ReadThumbnail(webpItem.AssetPath, 4))
        Check(thumbnail.Width == 4 && thumbnail.Height == 4, "WebP thumbnail decoded without Windows codec extension");
    var largePack = Path.Combine(folder, "addon-200.zip");
    using (var zip = System.IO.Compression.ZipFile.Open(largePack, System.IO.Compression.ZipArchiveMode.Create))
    {
        var manifest = new OpsisEcosystemGallery.Manifest { PluginId = "fixture.200", Version = "1.0.0" };
        for (int i = 0; i < 200; i++)
        {
            var asset = $"images/image-{i:D3}.png";
            manifest.Contributions.Add(new() { Id = $"image-{i:D3}", Type = "wallpaper", Title = $"Image {i}", Category = "Test", Asset = asset });
            using var bmp = new System.Drawing.Bitmap(8, 8);
            bmp.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, i, 0, 0));
            using var data = new MemoryStream();
            bmp.Save(data, System.Drawing.Imaging.ImageFormat.Png);
            using var entry = zip.CreateEntry(asset).Open();
            data.Position = 0; data.CopyTo(entry);
        }
        using var writer = new StreamWriter(zip.CreateEntry("opsis-contributions.json").Open());
        writer.Write(JsonSerializer.Serialize(manifest));
    }
    Check(OpsisEcosystemGallery.ImportWallpaperPack(largePack) == 200 &&
        OpsisEcosystemGallery.Discover().Count(x => x.PluginId == "fixture.200") == 200,
        "200-image pack imported and all entries discoverable");
    bool duplicatePackRejected = false;
    try { OpsisEcosystemGallery.ImportWallpaperPack(addonZip); }
    catch (InvalidDataException) { duplicatePackRejected = true; }
    Check(duplicatePackRejected, "duplicate gallery pack identity rejected");
    var overlapFolder = Path.Combine(folder, "gallery-packs", "example-override");
    Directory.CreateDirectory(overlapFolder);
    File.WriteAllText(Path.Combine(overlapFolder, "cover.webp"), "updated fixture");
    File.WriteAllText(Path.Combine(overlapFolder, "opsis-contributions.json"),
        JsonSerializer.Serialize(new
        {
            Schema = 1, PluginId = "example.plugin", Version = "2.0.0", Name = "Override",
            Contributions = new[] { new
            {
                Id = "city-wallpaper", Type = "wallpaper", Category = "Greece",
                Title = "New Athens", Asset = "cover.webp", Author = "Test", Description = "Updated version"
            }}
        }));
    var selectedWallpaper = OpsisEcosystemGallery.Discover()
        .Single(x => x.PluginId == "example.plugin" && x.Id == "city-wallpaper");
    Check(selectedWallpaper.Origin == "gallery-pack" && selectedWallpaper.Version == "2.0.0",
        "Gallery picks newer signed-by-metadata version without duplicate cards");
    Check(OpsisEcosystemGallery.ContributionOrigins().Count(x =>
        x.PluginId == "example.plugin" && x.ItemId == "city-wallpaper") == 2,
        "raw manifest audit preserves both duplicate sources for conflict reporting");
    Check(OpsisIntegrationAudit.CandidatePluginFolders().Any(x =>
        x.EndsWith("example-plugin", StringComparison.OrdinalIgnoreCase)),
        "inventory discovers user-installed plugins without activating their code");

    // A second contributor may supply a non-executable native widget layout.
    File.WriteAllText(Path.Combine(plugin, "clock.json"), JsonSerializer.Serialize(new OpsisWidgetDesigner.Layout
    {
        Name = "Plugin Clock", Binding = "Clock", AccentHex = "#44BBDD"
    }));
    File.WriteAllText(Path.Combine(plugin, "opsis-contributions.json"), JsonSerializer.Serialize(new
    {
        Schema = 1, PluginId = "example.plugin", Name = "Example",
        Contributions = new[] {
            new { Id = "city-wallpaper", Type = "wallpaper", Category = "Greece",
                Title = "Athens", Asset = "cover.webp", Author = "Test", Description = "Fixture" },
            new { Id = "clock-preview", Type = "widget", Category = "Clock",
                Title = "Plugin Clock", Asset = "clock.json", Author = "Test", Description = "Native widget" }
        }
    }));
    var pluginWidget = OpsisEcosystemGallery.Discover().Single(x => x.Type == "widget" && x.PluginId == "example.plugin");
    Check(OpsisEcosystemGallery.ReadWidgetContribution(pluginWidget).Binding == "Clock",
        "safe plugin widget contribution instantiated as native layout");


    const string supportedRainmeter = "[Rainmeter]\nUpdate=1000\n[MeasureClock]\nMeasure=Time\nFormat=%H:%M\n[MeterClock]\nMeter=String\nMeasureName=MeasureClock\nFontColor=64,160,224\nFontSize=28\nW=350\nH=160";
    var cpuRain = OpsisRainmeterSkinImporter.PreviewIni("[CPU]\nMeasure=CPU\nProcessor=0\n[Meter]\nMeter=String\nMeasureName=CPU", "CPU");
    var ramRain = OpsisRainmeterSkinImporter.PreviewIni("[RAM]\nMeasure=PhysicalMemory\n[Meter]\nMeter=String\nMeasureName=RAM", "RAM");
    Check(cpuRain.Binding == "CPU" && ramRain.Binding == "RAM", "Rainmeter CPU and used physical memory mapped to existing percentage widgets");
    var rainPreview = OpsisRainmeterSkinImporter.PreviewIni(supportedRainmeter, "Converted Clock");
    Check(rainPreview.Binding == "Clock" && rainPreview.AccentHex == "#40A0E0",
        "Rainmeter built-in Time/clock INI mapped to native Opsis widget");
    var rainFile = Path.Combine(folder, "Converted Clock.ini");
    File.WriteAllText(rainFile, supportedRainmeter);
    Check(OpsisRainmeterSkinImporter.Import(rainFile) == "Converted Clock",
        "safe Rainmeter INI import writes widget layout");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[Rainmeter]\nUpdate=1000\n[MeasureScript]\nMeasure=Plugin\nPlugin=RunCommand\n[MeterA]\nMeter=String\nMeasureName=MeasureScript",
            "Blocked");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter plugin/script measure rejected");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[MeterA]\nMeter=String\nText=[!Execute \"notepad.exe\"]",
            "Blocked");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter bang commands rejected");
    var multi = OpsisRainmeterSkinImporter.PreviewIni("[MeterA]\nMeter=String\nText=Hello\n[MeterB]\nMeter=String\nText=World", "Two Meters");
    Check(multi.Rows is { Length: 2 } && multi.Rows[0].Text == "Hello" && multi.Rows[1].Text == "World",
        "Rainmeter multi-meter data preserved as stacked native rows");
    var multiFile = Path.Combine(folder, "Multi Meter.ini");
    File.WriteAllText(multiFile, "[Clock]\nMeasure=Time\n[MeterA]\nMeter=String\nMeasureName=Clock\n[MeterB]\nMeter=String\nText=Welcome");
    OpsisRainmeterSkinImporter.Import(multiFile);
    Check(OpsisWidgetDesigner.Read("Multi Meter").Rows is { Length: 2 }, "composite widget persistence");
    rejected = false;
    try { OpsisRainmeterSkinImporter.PreviewIni("[MeterA]\nMeter=String\nText=Hello\n[MeterB]\nMeter=String\nText=World\n[Other]\nMeasure=Plugin\nPlugin=RunCommand", "Blocked"); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "composite Rainmeter import rejects unused executable measures");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(new string('A', 66_000), "Oversized");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter oversized INI refused");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[MeterA]\nMeter=String\nText=Hello\nPlugin=RunCommand",
            "Extra Commands");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter unknown executable meter property refused");


    var combinedMods = OpsisModCompatibility.Evaluate(new[]
    {
        "StartAllBack", "Windhawk", "Rainmeter", "A Different Product", "StartAllBack"
    });
    Check(combinedMods.Count == 3, "shell mod advisory de-duplicates installed products");
    Check(combinedMods.Any(x => x.Product == "StartAllBack" && x.Risk == "High"),
        "competing Start/Taskbar shell replacement requires review");
    Check(combinedMods.Any(x => x.Product == "Rainmeter" && x.Risk == "Low"),
        "Rainmeter widget coexistence distinguished from shell injection");
    Check(OpsisModCompatibility.Evaluate(new[] { "Notepad" }).Count == 0,
        "unknown applications are not classified as unsafe");

    var taskbar = OpsisTaskbarDesigner.Read();
    Check(taskbar.Style == "Classic", "new Opsis taskbar defaults to non-invasive Classic visual style");
    taskbar.Style = "Glass";
    taskbar.Alignment = "Center";
    taskbar.ButtonWidth = 155;
    taskbar.ButtonGap = 7;
    taskbar.OpacityPercent = 78;
    OpsisTaskbarDesigner.Save(taskbar);
    Check(OpsisTaskbarDesigner.Read().ButtonWidth == 155 &&
          OpsisTaskbarDesigner.Read().Alignment == "Center",
        "taskbar visual profile round-trips without writing Windows registry");
    taskbar.ActiveButtonAnimation = true;
    taskbar.AnimationIntervalMs = 240;
    OpsisTaskbarDesigner.Save(taskbar);
    Check(OpsisTaskbarDesigner.Read().ActiveButtonAnimation &&
          OpsisTaskbarDesigner.Read().AnimationIntervalMs == 240,
        "taskbar animated active button opt-in settings persist");
    rejected = false;
    try { OpsisTaskbarDesigner.Validate(new OpsisTaskbarDesigner.Profile { AnimationIntervalMs = 10 }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "taskbar animation rejects extremely fast CPU-intensive polling");

    var desktop = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
    var work = new System.Drawing.Rectangle(-1920, 0, 1920, 1032);
    var nativeBar = new System.Drawing.Rectangle(-1920, 1032, 1920, 48);
    Check(OpsisTaskbarDesigner.CalculateBounds(new OpsisTaskbarDesigner.Profile(), desktop, work, nativeBar, 3) == nativeBar,
        "unchanged taskbar profile preserves native bounds");
    foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
    {
        var geometry = new OpsisTaskbarDesigner.Profile { Edge = edge, Floating = true, Segmented = true, DynamicSize = true };
        var small = OpsisTaskbarDesigner.CalculateBounds(geometry, desktop, work, nativeBar, 2);
        var large = OpsisTaskbarDesigner.CalculateBounds(geometry, desktop, work, nativeBar, 15);
        Check(work.Contains(small) && work.Contains(large) && !small.IntersectsWith(nativeBar) &&
            (edge is "Left" or "Right" ? large.Height >= small.Height : large.Width >= small.Width),
            "detached adaptive bar respects work area at " + edge);
    }
    Check(TaskbarViewProfiles.IsValid("TaskbarGlomLevel", 2) && !TaskbarViewProfiles.IsValid("TaskbarGlomLevel", 3) &&
        ExplorerViewProfiles.IsValid("ShowStatusBar", 0) && !ExplorerViewProfiles.IsValid("ShowStatusBar", 2),
        "native Explorer and Taskbar options validate without touching registry");
    Check(NativeShellThemeBridge.IsKnownTheme("windows-11-notification-center-styler", "LiquidGlass") &&
        !NativeShellThemeBridge.IsKnownTheme("unregistered", "LiquidGlass") &&
        !NativeShellThemeBridge.IsKnownTheme("windows-11-notification-center-styler", "unknown"),
        "native shell provider only accepts known theme choices");
    Check(NativeShellThemeBridge.ParseTheme("{\"schemaVersion\":1,\"success\":true,\"data\":{\"id\":\"windows-11-notification-center-styler\",\"key\":\"theme\",\"value\":\"LiquidGlass\"}}",
        "windows-11-notification-center-styler") == "LiquidGlass", "native theme CLI schema validated without starting third party executable");
    Check(NativeShellThemeBridge.IsKnownTheme("taskbar-volume-control", "classic") &&
        NativeShellThemeBridge.IsKnownTheme("confirm-closing-multiple-explorer-tabs", "cancel") &&
        NativeShellThemeBridge.ParseTheme("{\"schemaVersion\":1,\"success\":true,\"data\":{\"id\":\"explorer-command-bar\",\"key\":\"showSettingsItem\",\"value\":true}}", "explorer-command-bar") == "true",
        "external native provider validates known volume/tab/command bar settings and typed readback");
    Check(OpsisModCompatibility.ResolveWindhawkDisabled(1, 1, 0, false) == false &&
        OpsisModCompatibility.ResolveWindhawkDisabled(0, 0, 1, false) == false &&
        OpsisModCompatibility.ResolveWindhawkDisabled(0, 1, 0, true) == true &&
        OpsisModCompatibility.ResolveWindhawkDisabled(null, null, null, false) == null,
        "Windhawk user scope and revoked mods follow upstream disabled semantics");
    Check(OpsisTaskbarDesigner.ValidGesture("Quick settings") && !OpsisTaskbarDesigner.ValidGesture("run.exe") &&
        OpsisTaskbarDesigner.ValidVolumeStyle("Compact") && !OpsisTaskbarDesigner.ValidVolumeStyle("unknown"),
        "P2 gesture and sound styles have fixed allowlists");
    var monitorProfile = new OpsisTaskbarDesigner.Profile { Edge = "Left", EmptyMiddleClick = "DeskFlip", VolumeStyle = "Large" };
    OpsisTaskbarDesigner.SaveMonitorProfile("id:test-monitor-a", monitorProfile);
    Check(OpsisTaskbarDesigner.ReadMonitorProfiles().TryGetValue("ID:TEST-MONITOR-A", out var restoredMonitor) &&
        restoredMonitor.Edge == "Left" && restoredMonitor.EmptyMiddleClick == "DeskFlip" && restoredMonitor.VolumeStyle == "Large",
        "P2 monitor profile roundtrip case-insensitive without changing global profile");
    OpsisTaskbarDesigner.SaveMonitorProfile("id:test-monitor-a", null);
    Check(!OpsisTaskbarDesigner.ReadMonitorProfiles().ContainsKey("id:test-monitor-a"), "P2 monitor override removal returns to default");
    Check(OpsisTaskbarDesigner.Profile.IsThumbnailStyle("Large") && !OpsisTaskbarDesigner.Profile.IsThumbnailStyle("Unknown"),
        "taskbar thumbnail preferences use fixed allowlist");
    Check(StartStudioPreferences.ValidPowerActions(new[] { "Lock", "Restart" }) &&
        !StartStudioPreferences.ValidPowerActions(new[] { "Restart", "Restart" }) &&
        !StartStudioPreferences.ValidPowerActions(new[] { "custom.exe" }),
        "power menu allows only known distinct actions without executing power operations");
    StartStudioPreferences.SetPowerActions(new[] { "Lock" });
    Check(StartStudioPreferences.Read().PowerActions.SequenceEqual(new[] { "Lock" }), "power menu preference roundtrip");
    StartStudioPreferences.SetPowerActions(StartStudioPreferences.AvailablePowerActions.ToArray());
    Check(OpsisTaskbarDesigner.ButtonHeight(taskbar, false, 44) == 38,
        "taskbar designer calculates horizontal button geometry");
    rejected = false;
    try { OpsisTaskbarDesigner.Validate(new OpsisTaskbarDesigner.Profile { Style = "ShellPatch" }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "taskbar designer rejects undocumented shell modification styles");
    StartStudioPreferences.SetDisplay("Tiles", true);
    Check(StartStudioPreferences.Read().DisplayStyle == "Tiles" &&
          StartStudioPreferences.Read().GroupByFolder,
        "Start Studio presentation and categories persist");
    rejected = false;
    try { StartStudioPreferences.SetDisplay("InjectedStart", false); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Start Studio refuses unsupported third-party shell mode");

    var screensaverProfile = OpsisScreensaverStudio.Read();
    Check(screensaverProfile.Mode == "Clock", "screensaver first-run mode is safe clock-only");
    screensaverProfile.DimPercent = 35;
    OpsisScreensaverStudio.Save(screensaverProfile);
    Check(OpsisScreensaverStudio.Read().DimPercent == 35,
        "native screensaver scene profile persists");
    rejected = false;
    try { OpsisScreensaverStudio.Validate(new OpsisScreensaverStudio.Profile { Mode = "ExternalCode" }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "screensaver rejects executable/unknown modes");
    rejected = false;
    try { OpsisScreensaverStudio.Validate(new OpsisScreensaverStudio.Profile
        { Mode = "StaticImage", ImagePath = "../evil.exe" }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "screensaver rejects executable images and directory traversal");

    var widget = new OpsisWidgetDesigner.Layout {
        Name = "Clock Demo", Binding = "Clock", AccentHex = "#00D2FF", FontSize = 30
    };
    OpsisWidgetDesigner.Save(widget);
    Check(OpsisWidgetDesigner.List().Contains("Clock Demo"), "native widget layout appears in Gallery storage");
    Check(OpsisWidgetDesigner.Read("Clock Demo").Binding == "Clock", "native widget layout roundtrip");
    var edit = OpsisWidgetDesigner.Read("Clock Demo");
    edit.ContentXPercent = 75;
    edit.ContentYPercent = 20;
    edit.ShowWidgetBorder = false;
    OpsisWidgetDesigner.Update("Clock Demo", edit);
    var updatedWidget = OpsisWidgetDesigner.Read("Clock Demo");
    Check(updatedWidget.ContentXPercent == 75 && updatedWidget.ContentYPercent == 20,
        "visual drag positions are persisted");
    Check(!updatedWidget.ShowWidgetBorder, "widget border preference survives save");
    var origin = OpsisWidgetDesigner.Position(400, 200, 100, 50, 0, 0);
    var far = OpsisWidgetDesigner.Position(400, 200, 100, 50, 100, 100);
    Check(origin == (0, 0) && far == (300, 150), "visual editor geometry covers entire canvas");
    Check(OpsisWidgetDesigner.Position(20, 15, 80, 30, 70, 60) == (0, 0),
        "visual editor clamps small canvas without negative positions");
    rejected = false;
    try { OpsisWidgetDesigner.Validate(new OpsisWidgetDesigner.Layout { Name = "Bad Coordinates", ContentXPercent = 120 }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "invalid widget drag coordinates rejected");

    rejected = false;
    try { OpsisWidgetDesigner.Validate(new OpsisWidgetDesigner.Layout { Name = "../escape", Binding = "Clock" }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "widget layout traversal rejected");
    rejected = false;
    try { OpsisWidgetDesigner.Validate(new OpsisWidgetDesigner.Layout { Name = "Bad Widget", Binding = "Script" }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "widget designer refuses arbitrary script bindings");

    // Image-to-accent extraction must be deterministic and never modify Windows colors.
    var imagePath = Path.Combine(folder, "test-accent.png");
    using (var bmp = new System.Drawing.Bitmap(30, 30))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bmp);
        graphics.Clear(System.Drawing.Color.FromArgb(25, 170, 205));
        bmp.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
    }
    var computed = WallpaperAccentEngine.Extract(imagePath);
    Check(computed.B > computed.R, "wallpaper accent extracts representative blue color");
    Check(WallpaperAccentEngine.ToHex(computed).Length == 7, "accent encoded as validated hex");
    var appSettings = AppSettings.Load();
    appSettings.WallpaperAccentEnabled = true;
    appSettings.WallpaperAccentHex = WallpaperAccentEngine.ToHex(computed);
    appSettings.Save();
    MotionDesk.UI.ThemeManager.RefreshOpsisAccent();
    Check(MotionDesk.UI.ThemeManager.Current.AccentCyan.ToArgb() == computed.ToArgb(),
        "unified Opsis palette uses wallpaper-derived accent");
    WallpaperAccentEngine.Disable();
    Check(!AppSettings.Load().WallpaperAccentEnabled, "dynamic accent reversible");

    // Registry context menu writes only to HKCU-owned keys; CI runner uses an ephemeral account.
    var fakeOpsis = Path.Combine(folder, "Opsis.exe");
    File.WriteAllBytes(fakeOpsis, new byte[] {77, 90}); // file name check; never execute
    try
    {
        OpsisContextMenuStudio.Enable(fakeOpsis);
        Check(OpsisContextMenuStudio.IsEnabled(), "Opsis owned context menu installed");
    }
    finally { OpsisContextMenuStudio.Disable(); }
    Check(!OpsisContextMenuStudio.IsEnabled(), "Opsis context menu removed safely");
    var before = File.ReadAllText(Path.Combine(folder, "module-core.json"));
    var snapshot = OpsisRecoveryHub.Snapshot("Integration test");
    Check(snapshot.Files.Any(x => x.RelativePath == "module-core.json"), "recovery saved modules");
    Check(snapshot.Files.Any(x => x.RelativePath == "automations.json"), "recovery saved automations");
    Check(snapshot.Files.Any(x => x.RelativePath.StartsWith("weather-scenes/", StringComparison.Ordinal)), "recovery saved scenes");
    Check(snapshot.Files.Any(x => x.RelativePath.StartsWith("widget-designs/", StringComparison.Ordinal)), "recovery saved widget designs");
    Check(snapshot.Files.Any(x => x.RelativePath == "taskbar-designer.json"), "recovery includes Opsis taskbar designer");
    Check(snapshot.Files.Any(x => x.RelativePath == "screensaver-studio.json"), "recovery includes screensaver scene settings");
    Check(snapshot.Files.Any(x => x.RelativePath == "start-studio.json"), "recovery includes Start Studio presentation preferences");
    File.WriteAllText(Path.Combine(folder, "module-core.json"), "{}");
    OpsisRecoveryHub.RestoreOwnedSettings(snapshot.Id);
    Check(File.ReadAllText(Path.Combine(folder, "module-core.json")) == before, "recovery restored original settings");
    Check(OpsisRecoveryHub.List().Count >= 2, "restore creates pre-restore safety snapshot");
    var tamper = OpsisRecoveryHub.Snapshot("Tamper-detection test");
    var tamperFile = Path.Combine(OpsisRecoveryHub.Root, "snapshots", tamper.Id, "files", "module-core.json");
    File.AppendAllText(tamperFile, "tampered");
    var untouched = File.ReadAllText(Path.Combine(folder, "module-core.json"));
    rejected = false;
    try { OpsisRecoveryHub.RestoreOwnedSettings(tamper.Id); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "recovery detects altered backup hash");
    Check(File.ReadAllText(Path.Combine(folder, "module-core.json")) == untouched,
        "failed recovery cannot modify live settings");

    // High repetition of the pure rules planner does not spawn work or touch
    // disk. Clock-bound and startup actions must remain mutually exclusive.
    var planned = OpsisAutomationStudio.Read();
    var dueCount = 0;
    for (var i = 0; i < 10_000; i++)
        dueCount += OpsisAutomationStudio.Plan(planned,
            new DateTime(2026, 11, 24, 9, i % 60, 0), false).Count;
    Check(dueCount > 0 && dueCount < 10_000, "ten thousand in-memory automation evaluations stable");


    // Create real Win32 form handles from an STA Windows thread. This confirms
    // the native visual editor and runtime widget are constructible without
    // opening modal dialogs or relying on a developer desktop session.
    Exception? uiFailure = null;
    var actualWindowsCreated = false;
    var uiThread = new System.Threading.Thread(() =>
    {
        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            var widgetLayout = OpsisWidgetDesigner.Read("Clock Demo");
            using var editor = new MotionDesk.UI.OpsisWidgetVisualDesignerForm(widgetLayout);
            using var taskbarDesigner = new MotionDesk.UI.OpsisTaskbarDesignerForm();
            using var explorerDesigner = new MotionDesk.UI.OpsisExplorerStudioForm();
            using var startStudio = new MotionDesk.UI.StartStudioForm();
            using var screensaver = new MotionDesk.UI.OpsisScreensaverPreviewForm(OpsisScreensaverStudio.Read());
            using var window = new MotionDesk.UI.OpsisCustomWidgetWindow(widgetLayout);
            var editorHandle = editor.Handle;
            var widgetHandle = window.Handle;
            actualWindowsCreated = editorHandle != IntPtr.Zero && widgetHandle != IntPtr.Zero &&
                taskbarDesigner.Handle != IntPtr.Zero && explorerDesigner.Handle != IntPtr.Zero &&
                startStudio.Handle != IntPtr.Zero && screensaver.Handle != IntPtr.Zero;
        }
        catch (Exception ex) { uiFailure = ex; }
    })
    { IsBackground = true };
    uiThread.SetApartmentState(System.Threading.ApartmentState.STA);
    uiThread.Start();
    Check(uiThread.Join(TimeSpan.FromSeconds(15)), "native UI smoke completed without hanging");
    if (uiFailure != null) throw new InvalidOperationException("WinForms visual widget smoke failed", uiFailure);
    Check(actualWindowsCreated, "visual editor and widget Win32 handles created on Windows VM");

    // SSD-idle regression: read-only application/catalog/profile operations may
    // read cache, but must never rewrite settings files while doing so.
    var monitoredFiles = new[] { "settings.json", "module-core.json", "taskbar-designer.json",
        "start-studio.json", "automations.json", "screensaver-studio.json" };
    var originalStamps = monitoredFiles.ToDictionary(x => x,
        x => File.Exists(Path.Combine(folder, x))
            ? File.GetLastWriteTimeUtc(Path.Combine(folder, x)).Ticks : 0L);
    for (var i = 0; i < 5_000; i++)
    {
        _ = AppSettings.Peek();
        _ = OpsisTaskbarDesigner.Read();
        if (i % 25 == 0)
        {
            _ = StartStudioPreferences.Read();
            _ = OpsisModCore.Read();
            _ = OpsisAutomationStudio.Read();
            _ = OpsisScreensaverStudio.Read();
        }
    }
    Check(monitoredFiles.All(x =>
        (File.Exists(Path.Combine(folder, x))
            ? File.GetLastWriteTimeUtc(Path.Combine(folder, x)).Ticks : 0L) == originalStamps[x]),
        "five thousand hot-path reads perform zero configuration writes");

    Console.WriteLine($"COMPLETE {passed} validated assertions");
}
finally
{
    try { Directory.Delete(folder, true); } catch { }
}

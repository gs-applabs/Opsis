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
        windhawkMods.Any(m => m.Area == "Unknown mod") && windhawkMods.All(m => m.Risk == "Review"),
        "Windhawk per-mod detection deduplicates, excludes disabled and never claims verified injection");
    Check(OpsisModCore.Catalog.Count >= 10, "unified module catalog available");
    Check(OpsisModCore.Read().Enabled.Count == 0, "module flags disabled by default");
    OpsisModCore.SetModule("windows.hot-corners", true, 22631, false);
    Check(OpsisModCore.Read().Enabled["windows.hot-corners"], "module enabled with compatible build");
    OpsisModCore.SetModule("windows.hot-corners", false, 22631, false);
    Check(!OpsisModCore.Read().Enabled["windows.hot-corners"], "module independently disabled");
    bool rejected = false;
    try { OpsisModCore.SetModule("wallpaper.scene-composer", true, 22631, false); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "planned modules cannot be activated as implemented");
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
    Check(!WindowsUpdateGuard.RequiresReview, "user approval clears hold");

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


    const string supportedRainmeter = "[Rainmeter]\\nUpdate=1000\\n[MeasureClock]\\nMeasure=Time\\nFormat=%H:%M\\n[MeterClock]\\nMeter=String\\nMeasureName=MeasureClock\\nFontColor=64,160,224\\nFontSize=28\\nW=350\\nH=160";
    var rainPreview = OpsisRainmeterSkinImporter.PreviewIni(supportedRainmeter.Replace("\\\\n", "\\n"), "Converted Clock");
    Check(rainPreview.Binding == "Clock" && rainPreview.AccentHex == "#40A0E0",
        "Rainmeter built-in Time/clock INI mapped to native Opsis widget");
    var rainFile = Path.Combine(folder, "Converted Clock.ini");
    File.WriteAllText(rainFile, supportedRainmeter.Replace("\\\\n", "\\n"));
    Check(OpsisRainmeterSkinImporter.Import(rainFile) == "Converted Clock",
        "safe Rainmeter INI import writes widget layout");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[Rainmeter]\\nUpdate=1000\\n[MeasureScript]\\nMeasure=Plugin\\nPlugin=RunCommand\\n[MeterA]\\nMeter=String\\nMeasureName=MeasureScript".Replace("\\\\n", "\\n"),
            "Blocked");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter plugin/script measure rejected");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[MeterA]\\nMeter=String\\nText=[!Execute \"notepad.exe\"]".Replace("\\\\n", "\\n"),
            "Blocked");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter bang commands rejected");
    rejected = false;
    try
    {
        OpsisRainmeterSkinImporter.PreviewIni(
            "[MeterA]\\nMeter=String\\nText=Hello\\n[MeterB]\\nMeter=String\\nText=World".Replace("\\\\n", "\\n"),
            "Two Meters");
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Rainmeter multiple meters fail closed instead of losing elements");
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
            "[MeterA]\\nMeter=String\\nText=Hello\\nPlugin=RunCommand".Replace("\\\\n", "\\n"),
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

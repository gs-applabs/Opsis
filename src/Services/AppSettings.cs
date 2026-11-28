using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    public sealed class AppSettings
    {
        public bool GridSnap { get; set; }
        public int SnapThreshold { get; set; } = 15;
        public int GridSize { get; set; } = 20;
        public bool AutoPerformanceMode { get; set; } = true;
        // Pause conditions (AppActivity): the wallpaper and the widgets stop rendering while the session is locked, in a remote (RDP) session or in Battery Saver.
        public bool PauseOnLock { get; set; } = true;
        public bool PauseOnRemoteSession { get; set; } = true;
        public bool PauseOnBatterySaver { get; set; } = true;
        // After a pause lasts 15 s the WebView2 process tree is suspended (0% CPU/GPU); resumed instantly when the pause ends.
        public bool WallpaperHeavyPause { get; set; } = true;
        public bool PauseOnScreensaver { get; set; } = true;
        // Wallpaper audio (only when the user switched it on) is muted while another application plays sound.
        public bool AutoMuteWhenOtherAudio { get; set; } = true;
        // Optional Discord Rich Presence (off by default; needs the user's own Discord application ID).
        public bool DiscordPresenceEnabled { get; set; } = false;
        public string DiscordApplicationId { get; set; } = "";
        public bool KeepPcAwake { get; set; } = false;          // keep the PC and the display awake while the wallpaper is visible
        // Pause while the system is under load: percent thresholds, 0 = off. Entered after 2 checks (10 s) above, left 10 points lower.
        public int PauseCpuAbove { get; set; } = 0;
        public int PauseGpuAbove { get; set; } = 0;
        public int PauseRamAbove { get; set; } = 0;
        public string WallpaperPerformanceMode { get; set; } = "Balanced";
        // Χειροκίνητο override της ανιχνευμένης ικανότητας υλικού (HardwareProfileService) — "" /
        // null = Auto (ανίχνευση RAM/πυρήνων/GPU). Τιμές: "Low" | "Balanced" | "High" (HardwareTier).
        // Ξεχωριστή έννοια από το WallpaperPerformanceMode παραπάνω: αυτό απαντά "πόσο δυνατό είναι
        // αυτό το μηχάνημα", όχι "τι θέλει ο χρήστης να δει τώρα" — βλ. PerformanceModeManager.
        public string? HardwareProfileOverride { get; set; }
        // 2.4.0: dynamic scaling (lowers the tier while the machine is under pressure, with hysteresis) and optional per-feature tier overrides (PerfFeature -> Low/Balanced/High).
        public bool PerfDynamic { get; set; } = true;
        public string PluginUiStyle { get; set; } = "Opsis";     // Opsis | Appearance | Crawler: the default look of plugin pages and standalone family apps (see PluginUiStyle.cs)
        public System.Collections.Generic.Dictionary<string, string>? PerfFeatureTier { get; set; }
        public bool ShowSystemMonitor { get; set; }
        public bool RestoreLastSession { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        // DeskZones: applications (process names, without .exe) whose windows are never snapped (e.g. media players, games)
        public List<string> ZoneExcludedApps { get; set; } = new();
        /// <summary>Optional explicit per-program and document-extension zone assignments.</summary>
        public List<WindowZoneRule> ZoneRules { get; set; } = new();
        // Notify (tray balloon) when CPU or memory stays above 90% for a minute
        public bool UsageAlertsEnabled { get; set; } = false;
        // Check GitHub for a newer release once a day / show the changelog once after an update
        public bool UpdateCheckEnabled { get; set; } = true;
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        // Update mechanism βάσει GearWin (ήδη portαρισμένο/δοκιμασμένο στο OpsisNewsCrawler): ένα
        // tray balloon ανά ΈΚΔΟΣΗ, όχι ανά check — θυμόμαστε το tag που ειδοποιήθηκε τελευταία.
        public string LastNotifiedUpdateTag { get; set; } = "";
        public string LastRunVersion { get; set; } = "";
        public bool StartWallpaperWithWindows { get; set; }
        public bool EnableAnimations { get; set; } = true;
        public string ThemeMode { get; set; } = "Follow"; // Follow | Light | Dark
        //  αντί για σταθερή παλέτα accent χρωμάτων, ακολούθησε το πραγματικό
        // accent color που έχει επιλέξει ο χρήστης στα Windows (Ρυθμίσεις > Εξατομίκευση > Χρώματα).
        public bool FollowWindowsAccent { get; set; } = false;
        // Opsis-only wallpaper accent; never changes the actual Windows accent.
        public bool WallpaperAccentEnabled { get; set; } = false;
        public string WallpaperAccentHex { get; set; } = "";

        public string Language { get; set; } = "Follow";  // Follow | el | en
        public double WindowOpacity { get; set; } = 1.0;  // 0.6 - 1.0, εφαρμόζεται στο κύριο παράθυρο
        public double DarkIntensity { get; set; } = 0.5;  // 0 (πιο ανοιχτό σκούρο) - 1 (σχεδόν μαύρο), 0.5 = η αρχική βάση
        public bool UiSoundsEnabled { get; set; }
        public bool SidebarCollapsed { get; set; }
        public bool ReplaceWindowsTaskbar { get; set; }
        public bool DesktopToolbarEnabled { get; set; }
        public bool DesktopToolbarAllMonitors { get; set; }
        public bool AutoTileEnabled { get; set; }
        public int AutoTileGap { get; set; } = 8;
        public string AutoTileMode { get; set; } = "Grid";
        public string MainWindowSkin { get; set; } = "Studio";
        public int WindowSkinRevision { get; set; }

        // Μηχανισμός plugin (φάκελος "plugins"): false = κανένα plugin δεν φορτώνεται (π.χ. για διάγνωση προβλήματος).
        public bool PluginsEnabled { get; set; } = true;
        // Προσωπικά presets του ισοσταθμιστή 10 ζωνών (Audio Enhancement) + το τελευταίο ενεργό.
        public List<AudioEqProfile> CustomEqProfiles { get; set; } = new();
        public string? ActiveEqProfile { get; set; }
        // Ζητήθηκε ρητά: ξεχωριστό ενεργό/ανενεργό ΑΝΑ plugin (π.χ. το News Crawler), πέρα από τον
        // γενικό διακόπτη PluginsEnabled παραπάνω. Αποθηκεύει το Id όσων ο χρήστης έχει απενεργοποιήσει.
        public HashSet<string> DisabledPluginIds { get; set; } = new();

        // Αφαίρεση plugin: ο φάκελός του δεν μπορεί να διαγραφεί όσο το Opsis τρέχει (το .dll του
        // είναι ήδη φορτωμένο, Windows file lock), οπότε ο φάκελος μπαίνει εδώ και σαρώνεται/
        // διαγράφεται στην ΕΠΟΜΕΝΗ εκκίνηση, πριν φορτωθεί κανένα plugin (βλ. PluginHost.StartAll).
        public List<string> PendingPluginRemovals { get; set; } = new();
        public Dictionary<string, string> PendingPluginRemovalHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Φάκελος όπου αποθηκεύονται τα μετατρεπόμενα .wmv->.mp4 (κενό = προεπιλογή στο AppData).
        public string? WmvConversionOutputDir { get; set; }

        // MD roadmap item: η οθόνη κλειδώματος/υποδοχής να μπορεί να γίνει ακριβές στιγμιότυπο του
        // τρέχοντος ζωντανού wallpaper (waves/βίντεο + overlay), όχι μια τυχαία επιλεγμένη εικόνα.
        // Το LockScreenSnapshotPath είναι το αρχείο που γράφει το στιγμιότυπο· όταν το
        // LockScreenFollowsWallpaper είναι true, το Opsis το ξανατραβάει αυτόματα σε κάθε αλλαγή
        // wallpaper ώστε η οθόνη κλειδώματος να μένει ταιριαστή (βλ. LockScreenEngine, WallpaperHostEngine).
        public bool LockScreenFollowsWallpaper { get; set; } = false;
        public string? LockScreenSnapshotPath { get; set; }
        /// <summary>2.6.0: the "start before other start-up apps" task was offered once (it is created once after the first run when Start with Windows is on; the person can switch it off in Settings).</summary>
        public bool StartEarlyInitialized { get; set; }

        // Ανεξάρτητο θέμα για widgets/DeskContainers από αυτό της ίδιας της εφαρμογής — ζητήθηκε
        // ρητά "αν η εφαρμογή είναι σε φωτεινό θέμα, τα widgets/containers να μπορούν είτε να
        // ακολουθούν τα windows είτε να έχουν διαφορετικά χρώματα και από το σύστημα και από την
        // εφαρμογή". "App" = ακολουθεί το θέμα της εφαρμογής (προηγούμενη, μοναδική συμπεριφορά),
        // "Windows" = ακολουθεί ανεξάρτητα το θέμα των Windows, "Dark"/"Light" = πάντα σταθερό.
        public string WidgetsThemeMode { get; set; } = "App";
        // "Transparent" widgets: pick the text colour from the desktop behind them, and optionally add a coloured tint (same colours as the DeskContainers).
        public bool WidgetsAutoContrast { get; set; } = true;
        public string WidgetsTintColor { get; set; } = "Black";
        public int WidgetsTintPercent { get; set; } = 0;          // 0 = fully transparent (colour-key); > 0 = tinted translucent surface

        // Ενιαίο προεπιλεγμένο μέγεθος για όλα τα νέα widgets — ζητήθηκε ρητά "όλα τα widgets να
        // έχουν το ίδιο μέγεθος και να μπορεί ο χρήστης να το αλλάζει".
        public int WidgetDefaultWidth { get; set; } = 300;
        public int WidgetDefaultHeight { get; set; } = 220;

        // "Audio Enhancement" preset (βλ. AudioSpectrumService.Presets) — κοινό ανάμεσα στο
        // πλωτό widget και στη σελίδα "Audio Enhancement" της εφαρμογής, ώστε να δείχνουν το ίδιο.
        public string AudioEnhancementPreset { get; set; } = "Flat";
        // 2.2.1: which graphics adapter the System Monitor widget shows (dedicated + integrated are both listed; arrows switch) and which disk the Disk widget shows.
        public int SystemMonitorGpuIndex { get; set; } = 0;
        /// <summary>Opt-in recovery of external window placements after a docking monitor reconnects.</summary>
        public bool RestoreWindowsAfterDocking { get; set; } = false;
        /// <summary>Optional monitor stable IDs on which private wallpaper information is not drawn.</summary>
        public List<string> PresentationMonitorIds { get; set; } = new();

        public int DiskWidgetIndex { get; set; } = 0;

        // Fences-style διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας για απόκρυψη/επαναφορά
        // εικονιδίων (DesktopIconVisibilityEngine) — υπήρχε ήδη ο μηχανισμός αλλά ήταν πάντα
        // ενεργός χωρίς καμία ρύθμιση ορατή στο χρήστη· προεπιλογή true ώστε η υπάρχουσα
        // συμπεριφορά να ΜΗΝ αλλάζει για όσους το χρησιμοποιούσαν ήδη χωρίς να το ξέρουν.
        public Dictionary<string, string> ContainerMonitorLayouts { get; set; } = new();
        public bool ContainerPeekDim { get; set; } = true;
        public bool QuickHideDoubleClickEnabled { get; set; } = true;
        // v2.2.0 (ιδέες από το DesktopFences, MIT): γενικές ρυθμίσεις DeskContainers.
        // DeskContainers are provided by the bundled engine (containers\OpsisContainers.exe); false = the engine is not started.
        public bool DeskContainersEnabled { get; set; } = true;
        // The four settings below belong to the removed built-in containers; they are only READ once by ContainersMigration.MigrateLegacySettings.
        public bool ContainersAutoHide { get; set; } = false;          // κρύβονται μετά από αδράνεια χρήστη
        public int ContainersAutoHideSeconds { get; set; } = 20;
        public bool ContainersChameleon { get; set; } = false;         // χρώμα από το κυρίαρχο χρώμα του wallpaper
        public bool ContainersDailyBackup { get; set; } = true;        // καθημερινό backup των ρυθμίσεων/περιεχομένων
        public bool ContainersSpotSearch { get; set; } = false;       // Ctrl+` : γρήγορη αναζήτηση/εκκίνηση (ισχύει μετά από επανεκκίνηση)
        // Ποια εικονίδια ΜΕΝΟΥΝ ορατά όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας. Προεπιλογή: Ο Υπολογιστής μου, ο φάκελος του χρήστη,
        // ο Πίνακας Ελέγχου και ο Κάδος Ανακύκλωσης· μπορείς να τα αλλάξεις/να προσθέσεις ό,τι θέλεις (QuickHideExtraKept = ονόματα εικονιδίων).
        public bool QuickHideKeepThisPc { get; set; } = true;
        public bool QuickHideKeepUserFolder { get; set; } = true;
        public bool QuickHideKeepControlPanel { get; set; } = true;
        public bool QuickHideKeepRecycleBin { get; set; } = true;
        public List<string> QuickHideExtraKept { get; set; } = new();

        /// <summary>Last version whose first-run update notice was processed. Null on older profiles.</summary>
        public string? LastSeenAppVersion { get; set; }

        /// <summary>2.6.0: size and position of the main window remembered per screen (Opsis.UI.WindowMemory); a missing screen falls back to a safe place.</summary>
        public WindowMemoryData WindowMemory { get; set; } = new();

        /// <summary>2.6.0: UI scale / text size of the main window in percent (100, 125, 150).</summary>
        public int UiScalePercent { get; set; } = 100;
        /// <summary>Text on cards only; independent of the main window's UI scale.</summary>
        public int CardFontScalePercent { get; set; } = 100;

        /// <summary>2.6.0: pause the live wallpaper on a monitor whose desktop is fully covered by an opaque window (nothing to see, nothing to render). No quality loss.</summary>
        public bool PauseWhenDesktopCovered { get; set; } = true;

        /// <summary>2.6.0: draw the DeskStrip glass with the GPU glass layer (Opsis.Glass): real transparency, live refraction of what is behind. Off until tested on real machines.</summary>
        /// <summary>Old on/off switch; kept so an explicit "on" from an earlier beta keeps its choice (see <see cref="GpuGlassMode"/>).</summary>
        public bool UseGpuGlass { get; set; }
        /// <summary>GPU glass of widgets / DeskStrip / containers: "Auto" (default: decided from the computer and the current load), "Always", "Blur", "Solid". Null = take it from <c>UseGpuGlass</c>.</summary>
        public string? GpuGlassMode { get; set; }
        /// <summary>2.6.0: the native DeskContainers core (own data file containers-native.json, pipe opsis-containers-v1-*). The WPF fork was removed; on first start the 2.4.x data is imported read-only.</summary>
        public bool UseNativeContainers { get; set; } = true;
        /// <summary>Dropping a desktop icon into a container hides the desktop original (as the old engine did). The originals are ALWAYS given back on exit, at the next start and by --restore-desktop-icons.</summary>
        public bool HideOriginalsOnDrop { get; set; } = true;
        /// <summary>Dragging an icon from one container into another: false = Move (Ctrl copies), true = Copy (Ctrl moves).</summary>
        public bool ContainerDragCopies { get; set; }
        /// <summary>After a rubber-band selection of two or more desktop icons, offer to make a container from them.</summary>
        public bool OfferContainerFromSelection { get; set; } = true;
        /// <summary>Music overlay (plan 2.1.6): a glass card with the cover / title / artist of whatever any player is playing. Off by default.</summary>
        public bool MusicOverlayEnabled { get; set; }
        /// <summary>Opt-in: "New DeskContainer" / "Next animated wallpaper" in the desktop right-click menu (per-user registry verbs, see DesktopMenu).</summary>
        public bool DesktopMenuEnabled { get; set; } = true;
        public string MusicOverlayAnchor { get; set; } = "BottomRight";
        // P0 optional desktop interactions. Values: None | OpenOpsis | ShowDesktop | TaskView.
        // Explicit opt-in. Automation rules are inactive until the user enables the studio.
        public bool OpsisAutomationsEnabled { get; set; } = false;
        public bool WindowRulesEnabled { get; set; } = false; // P3: disabled until explicit opt-in
        // Cursor FX: opt-in per-user overlay; no mouse hooks, scripts or system modifications.
        public bool CursorFxEnabled { get; set; } = false;
        public string CursorFxStyle { get; set; } = "Both"; // Halo | Ripple | Both
        public int CursorFxIntensity { get; set; } = 70; // 20 .. 100

        public bool HotCornersEnabled { get; set; } = false;
        public string HotCornerTopLeft { get; set; } = "None";
        public string HotCornerTopRight { get; set; } = "None";
        public string HotCornerBottomLeft { get; set; } = "None";
        public string HotCornerBottomRight { get; set; } = "None";
        // P1 user-defined activation sensitivity, clamped by the runtime to safe ranges.
        public int HotCornerDwellMilliseconds { get; set; } = 700;
        public int HotCornerEdgePixels { get; set; } = 5;
        public bool HotCornersPauseOnFullscreen { get; set; } = true;

        // Keep the user's original Windows mouse trails value for a genuine restore.
        public int? MouseTrailsOriginalCount { get; set; }



        /// <summary>2.6.0: the shared Glass theme choices (Opsis.UI.GlassSettings): Solid by default.</summary>
        public GlassSettings Glass { get; set; } = new();
        public DesktopMaterial ContainerMaterial { get; set; } = new();
        public DesktopMaterial DeskStripMaterial { get; set; } = new();
        public DesktopMaterial WidgetMaterial { get; set; } = new();

        private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "settings.json");

        // 2.6.0: the shared settings file of the Opsis family (Opsis.UI.SettingsStore): "schemaVersion" in the file, step migrations (none yet: version 1 is the
        // first numbered shape), atomic write with a .bak of the last good file, and a damaged file is moved to .corrupt instead of being silently ignored.
        // The store is cheap to create, so it follows AppPaths.AppDataFolder (tests / OPSIS_APPDATA) on every call.
        public const int SchemaVersion = 2;
        private static SettingsStore<AppSettings> Store() => NewStore(ConfigPath);

        /// <summary>Version 2 (2.6.0): the native DeskContainers core is the default. Earlier betas wrote UseNativeContainers=false into the file, which would keep the new core OFF after an upgrade: the step turns it on once.</summary>
        internal static SettingsStore<AppSettings> NewStore(string path) => new SettingsStore<AppSettings>(path, SchemaVersion).AddMigration(1, o => o["UseNativeContainers"] = true);

        public static AppSettings Load() => Store().Load();

        // READ-ONLY snapshot for hot paths (paint, timers, property getters): the file is parsed at most every 1.5 s and after a Save. The returned object is
        // shared: never change it and never Save it (use Load() for that).
        private static AppSettings? _peek;
        private static string? _peekPath;
        private static long _peekAt;
        private static readonly object PeekLock = new();

        /// <summary>True when the data folder is the isolated one of a test or audit run (OPSIS_APPDATA).</summary>
        internal static bool IsIsolatedForTests() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSIS_APPDATA"));

        public static AppSettings Peek()
        {
            lock (PeekLock)
            {
                if (_peek != null && _peekPath == ConfigPath && Environment.TickCount64 - _peekAt < 1500) return _peek;
                _peek = Load(); _peekPath = ConfigPath; _peekAt = Environment.TickCount64;
                return _peek;
            }
        }

        internal static event Action? SettingsSaved;

        public void Save()
        {
            Store().Save(this);
            lock (PeekLock) _peek = null;
            // A successful persistent write must not become a reported failure
            // merely because an optional runtime listener failed.
            try { SettingsSaved?.Invoke(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    public static class PerformanceModeManager
    {
        public static string GetRecommendedMode()
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
                return "Battery";
            if (power.BatteryLifePercent >= 0 && power.BatteryLifePercent < 0.30f)
                return "Low Power";

            // Συνδυάζεται με το HardwareProfileService (ικανότητα μηχανήματος) αντί να μείνει ως
            // δεύτερο, ασύνδετο σύστημα — ένα Low-End μηχάνημα μένει ελαφρύτερο ακόμα και στο
            // ρεύμα, ένα High-End μπορεί να έχει το πλουσιότερο εφέ από προεπιλογή. Σε Balanced
            // ικανότητα η συμπεριφορά μένει ακριβώς η ίδια με πριν ("Balanced").
            return HardwareProfileService.CurrentTier switch
            {
                HardwareTier.Low => "Low Power",
                HardwareTier.High => "High",
                _ => "Balanced"
            };
        }
    }
}

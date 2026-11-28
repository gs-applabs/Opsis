using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    public class TrayApplicationContext : ApplicationContext
    {
        private MotionDesk.Containers.ContainerHost? _nativeContainers;
        private Opsis.Containers.PipeServer? _controlPipe;
        private readonly NotifyIcon _trayIcon = null!;
        private MainWindow? _mainWindow;
        private readonly HotkeyManager _hotkeys = new(); // mandatory recovery/show-window shortcut
        private HotkeyManager? _optionalHotkeys;
        private bool _desktopServicesReady;
        private HotkeyManager? _containerHotkeys;
        private System.Windows.Forms.Timer? _startupHealthyTimer, _pluginApprovalTimer;
        private DockWindowRestorer? _dockRestorer;
        private int _optionalRefreshQueued;
        private void NavigatePlugin(string key) => ShowMainWindow("plugin:" + key);
        private ToolStripMenuItem _wallpaperToggleItem = null!;
        private bool _wallpaperEnabled;
        // E2 (v2.0 handoff PHASE0_REPORT): η μετάφραση της taskbar (GlassBlur.SetTaskbarTranslucency)
        // εφαρμοζόταν ΜΟΝΟ μία φορά στο Program.cs, στην εκκίνηση του Opsis — ποτέ ξανά αν η ίδια η
        // Explorer επανεκκινήσει (crash, "Restart Explorer" από Task Manager, Windows theme service).
        // Ξεχωριστό instance από αυτό που ήδη χρησιμοποιεί το wallpaper (WallpaperWindow) — αυτό εδώ
        // πρέπει να ζει όσο ζει η ίδια η εφαρμογή, ανεξάρτητα από το αν το wallpaper είναι ενεργό.
        private readonly ExplorerRestartWatcher _taskbarWatcher = new();
        private readonly HotCornersService _hotCorners;
        private readonly System.Windows.Forms.Timer _experienceTimer = new() { Interval = 60_000 };
        private long _lastExperienceSafetyCheck = Environment.TickCount64;
        private const int ExperienceSafetyIntervalMs = 15 * 60_000;
        internal static int ExperienceCadenceMs(bool automations, bool taskbar, bool reviewPending) =>
            automations || taskbar || reviewPending ? 60_000 : ExperienceSafetyIntervalMs;

        private void RefreshExperienceCadence()
        {
            if (SafeModeService.IsSafeMode || !_experienceTimer.Enabled) return;
            var settings = AppSettings.Peek();
            // The Windows-update safety check remains unconditional and is
            // never postponed by a settings change. Optional work has demand.
            long untilSafety = ExperienceSafetyIntervalMs - (Environment.TickCount64 - _lastExperienceSafetyCheck);
            int next = (int)Math.Clamp(untilSafety, 1000L,
                ExperienceCadenceMs(RuntimeSessionPolicy.Allows(SessionBackgroundService.ScheduledAutomations,
                    settings.OpsisAutomationsEnabled, SafeModeService.IsSafeMode), RuntimeSessionPolicy.Allows(SessionBackgroundService.ReplacementTaskbar, settings.ReplaceWindowsTaskbar, SafeModeService.IsSafeMode), WindowsUpdateGuard.RequiresReview));
            if (_experienceTimer.Interval != next) _experienceTimer.Interval = next;
        }
        private bool _compatibilityIsolationApplied;

        private int _exitStarted;
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
        private readonly System.Threading.SynchronizationContext _exitUi = new WindowsFormsSynchronizationContext();
        public static event Action? ExitRequested;
        public static void RequestExit() => ExitRequested?.Invoke();

        /// <summary>Lets any window ("Settings…" in its own right-click menu) open a page of the main window.</summary>
        public static Action<string>? OpenPage;

        public TrayApplicationContext(string? desktopAction = null)
        {
            OpenPage = p => { try { ShowMainWindow(p); } catch (Exception) { } };
            _hotCorners = new HotCornersService(() => ShowMainWindow("Personalization"));
            // Safe mode (v2.0 handoff, Φάση 1 §S): αν ενεργό, παραλείπονται plugins/wallpaper/
            // "Last Session" παρακάτω — μόνο το βασικό UI. Ο μετρητής crash μηδενίζεται μετά από
            // 30s επιτυχούς λειτουργίας (one-shot timer πιο κάτω), όχι αμέσως, ώστε ένα crash λίγο
            // μετά την εκκίνηση να εξακολουθεί να μετράει.
            bool safeMode = SafeModeService.IsSafeMode;
            bool reviewRequired = false;
            try
            {
                reviewRequired = WindowsUpdateGuard.EvaluateStartup();
                if (reviewRequired)
                    RotatingLogger.Write("New Windows build detected. Opsis shell replacement held for compatibility review.");
            }
            catch (Exception e) { RotatingLogger.Write("Windows compatibility guard failed: " + e.Message); reviewRequired = true; }
            if (safeMode)
            {
                RotatingLogger.Write("Starting in SAFE MODE - plugins/wallpaper/last-session skipped.");
                new SafeModeBanner().Show();
            }
            _startupHealthyTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _startupHealthyTimer.Tick += (_, _) =>
            {
                _startupHealthyTimer?.Stop();
                _startupHealthyTimer?.Dispose();
                _startupHealthyTimer = null;
                if (System.Threading.Volatile.Read(ref _exitStarted) == 0) SafeModeService.MarkStartupHealthy();
            };
            _startupHealthyTimer.Start();

            // Plugin (π.χ. News Crawler): ξεκινούν πριν χτιστεί το μενού ώστε να προσθέσουν τα δικά τους στοιχεία.
            // Dock monitor fingerprint and window return (opt-in); never captures user files.
            Application.ApplicationExit += OnApplicationExit;
            PluginHost.Instance.NavigationRequested += NavigatePlugin;
            // Plugins from user-writable folders run code inside Opsis: ask once, show the content hash, remember the approval (plugin-trust.json).
            // (the approval question is NOT asked while starting: see PluginHost.ConsiderFolder / ResolvePending)
            if (!safeMode)
            {
                if (!reviewRequired) { PluginHost.Instance.StartAll(); SchedulePluginApproval(); }
                else RotatingLogger.Write("Optional plugins held until Windows build compatibility review and Opsis restart.");
                try { if (!reviewRequired) MotionDesk.Appearance.AppearanceService.Instance.Start(); }
                catch (Exception e) { RotatingLogger.Write("Appearance service: " + e.Message); }
            }
            // Start-up before the other start-up apps: switched on ONCE after the first run when Start with Windows is on (Settings can turn it off; never repeated)
            try
            {
                var early = AppSettings.Load();
                if (!early.StartEarlyInitialized)
                {
                    early.StartEarlyInitialized = true; early.Save();
                    if (StartupManager.IsStartupEnabled()) System.Threading.Tasks.Task.Run(() => StartupTask.Set(true));
                }
            }
            catch (Exception) { }

            // Δημιουργία Context Menu για το Tray Icon — ομαδοποιημένο σε: παράθυρο, DeskZones,
            // wallpaper, γρήγορα προφίλ, γρήγορες ρυθμίσεις, έξοδος.
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            // Grouped by function / plugin: window, Wallpaper, DeskContainers, desktop tools, workspace profiles, one submenu per plugin, exit.
            contextMenu.Items.Add(LocalizationManager.T("Tray.Open"), null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add(LocalizationManager.T("Tray.CommandPalette"), null, (s, e) => { ShowMainWindow(); _mainWindow?.OpenCommandPalette(); });
            contextMenu.Items.Add(LocalizationManager.T("Tray.Settings"), null, (s, e) => ShowMainWindow("Settings"));
            contextMenu.Items.Add(LocalizationManager.T("Tray.ToggleLightDark"), null, (s, e) => { try { var a = MotionDesk.Appearance.AppearanceService.Instance; a.Backup(); a.Engine.SetDark(!a.Engine.IsDark); } catch (Exception) { } });
            // Opsis' own help entries: ONE welcome guide and ONE family window for the whole suite (plugins do not offer them when hosted: PluginHost.IsHostOwnedEntry)
            contextMenu.Items.Add(LocalizationManager.T("Tray.Welcome"), null, (s, e) => FamilyWindows.ShowWelcome());
            contextMenu.Items.Add(LocalizationManager.T("Tray.Family"), null, (s, e) => FamilyWindows.ShowFamily());
            // These are reversible, session-only filters over the saved
            // preferences. They do not silently change startup/profile data.
            contextMenu.Items.Add(BuildSessionBackgroundMenu());
            contextMenu.Items.Add(BuildSessionDesktopMenu());
            // plugins that are installed but not approved yet (user-writable folder): one click asks again
            _pluginApprovalItem = new ToolStripMenuItem { Visible = false };
            _pluginApprovalItem.Click += (s, e) => AskPluginApproval();
            contextMenu.Items.Add(_pluginApprovalItem);
            contextMenu.Opening += (s, e) => UpdatePluginApprovalItem();
            contextMenu.Items.Add(new ToolStripSeparator());

            // ---- Wallpaper
            var wallpaperMenu = new ToolStripMenuItem(LocalizationManager.T("Tray.GroupWallpaper"));
            _wallpaperToggleItem = new ToolStripMenuItem(LocalizationManager.T("Tray.WallpaperToggle"));
            _wallpaperToggleItem.Click += (s, e) => ToggleWallpaper();
            wallpaperMenu.DropDownItems.Add(_wallpaperToggleItem);
            wallpaperMenu.DropDownItems.Add(LocalizationManager.T("Tray.WallpaperAddVideo"), null, (s, e) => ChooseWallpaperVideo());
            wallpaperMenu.DropDownItems.Add(LocalizationManager.T("Tray.WallpaperAddFolder"), null, (s, e) => ChooseWallpaperFolder());
            wallpaperMenu.DropDownItems.Add(LocalizationManager.T("Tray.WallpaperSettings"), null, (s, e) => ShowMainWindow("Wallpaper"));
            contextMenu.Items.Add(wallpaperMenu);

            // ---- DeskContainers (items and state come from the engine through the bridge)
            if (AppSettings.Load().UseNativeContainers) contextMenu.Items.Add(BuildContainersTrayMenu());

            // ---- Desktop tools
            var desktopMenu = new ToolStripMenuItem(LocalizationManager.T("Tray.GroupDesktop"));
            desktopMenu.DropDownItems.Add(LocalizationManager.T("Tray.DeskZonesEdit"), null, (s, e) => AddDeskZone());
            desktopMenu.DropDownItems.Add("DeskFlip  (Ctrl+Alt+F)", null, (s, e) => DeskFlipEngine.Show());
            ToolStripMenuItem gridSnapItem = new ToolStripMenuItem(LocalizationManager.T("Tray.GridSnap"));
            gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            gridSnapItem.Click += (s, e) => {
                WidgetSnapEngine.EnableGridSnap = !gridSnapItem.Checked;
                gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            };
            desktopMenu.DropDownItems.Add(gridSnapItem);
            desktopMenu.DropDownItems.Add(new ToolStripSeparator());
            desktopMenu.DropDownItems.Add("OPSIS: " + (LocalizationManager.CurrentLanguage.StartsWith("el") ? "Αποθήκευση θέσεων εικονιδίων" : "Capture desktop icon positions"), null, (_, _) =>
            {
                var count=DesktopIconVisibilityEngine.CapturePositionsForUndo();
                MessageBox.Show(count>0 ? $"Captured {count} icon positions for undo." : "No positions captured (desktop unavailable or auto-arrange enabled).", "OPSIS");
            });
            desktopMenu.DropDownItems.Add("OPSIS: " + (LocalizationManager.CurrentLanguage.StartsWith("el") ? "Αναίρεση μετακίνησης εικονιδίων" : "Undo desktop icon move"), null, (_, _) =>
            {
                var count=DesktopIconVisibilityEngine.UndoLastBulkMove();
                MessageBox.Show(count>0 ? $"Restored {count} icon positions." : "There is no usable icon move snapshot.", "OPSIS");
            });
            contextMenu.Items.Add(desktopMenu);

            // ---- Workspace profiles (Work / Gaming / Focus keep their names in every language: they are profile ids)
            var profilesMenu = new ToolStripMenuItem(LocalizationManager.T("Tray.QuickProfile"));
            profilesMenu.DropDownItems.Add("Work", null, (s, e) => ApplyProfile("Work"));
            profilesMenu.DropDownItems.Add("Gaming", null, (s, e) => ApplyProfile("Gaming"));
            profilesMenu.DropDownItems.Add("Focus", null, (s, e) => ApplyProfile("Focus"));
            contextMenu.Items.Add(profilesMenu);

            // ---- Plugins: one submenu per plugin, rebuilt every time the menu opens (a plugin approved or started after start-up appears without a restart)
            _pluginMenuHost = contextMenu;
            _pluginMenuIndex = contextMenu.Items.Count;
            RebuildPluginMenu();
            contextMenu.Opening += (s, e) => RebuildPluginMenu();

            contextMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem startupItem = new ToolStripMenuItem(LocalizationManager.T("Tray.Startup"));
            startupItem.Checked = StartupManager.IsStartupEnabled();
            startupItem.Click += (s, e) => {
                bool newState = !startupItem.Checked;
                StartupManager.SetStartup(newState);
                startupItem.Checked = newState;
            };
            contextMenu.Items.Add(startupItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(LocalizationManager.T("Common.Exit"), null, (s, e) => PerformExit());
            // Επιτρέπει σε ΟΠΟΙΟΔΗΠΟΤΕ άλλο κομμάτι της εφαρμογής (π.χ. το κουμπί "Λήψη & εγκατάσταση"
            // στη σελίδα About) να ζητήσει τον ΙΔΙΟ, πλήρη καθαρισμό εξόδου αντί για ένα γυμνό
            // Application.Exit() που θα παρέκαμπτε το save session/plugin stop/hotkey dispose — ίδιο
            // μοτίβο με το host.RequestExit()/ExitRequested του ήδη δουλεμένου OpsisNewsCrawler.
            ExitRequested += PerformExit;

            // Αρχικοποίηση NotifyIcon — στατικό εικονίδιο (η "ζωντανή" λάμψη μετακόμισε στο
            // λογότυπο του sidebar μέσα στο κύριο παράθυρο, όχι πλέον στο tray).
            _trayIcon = new NotifyIcon()
            {
                Icon = MotionDesk.UI.AppWindowIcon.Value,
                ContextMenuStrip = contextMenu,
                Text = "Opsis - Desktop Customization " + AppVersion.Label,
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
            // Reuse the current NotifyIcon; no added background process or timer.
            var trayStudio = new ToolStripMenuItem("Opsis tray icon / Εικονίδιο Opsis");
            foreach (var option in new[] { "Original", "Information", "Shield" })
            {
                var entry = new ToolStripMenuItem(option);
                entry.Click += (_, _) =>
                {
                    try
                    {
                        OpsisModCore.SetOption("control.opsis-center", "tray-icon", option);
                        ApplyTrayIconStyle();
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Opsis"); }
                };
                trayStudio.DropDownItems.Add(entry);
            }
            trayStudio.DropDownItems.Add("Windows taskbar / notification area", null, (_, _) => TaskbarViewProfiles.OpenWindowsTaskbarSettings());
            contextMenu.Items.Insert(1, trayStudio);
            _pluginMenuIndex++; // preserve insertion point after adding the icon studio
            ApplyTrayIconStyle();

            AppSettings.SettingsSaved += QueueOptionalRefresh;
            RefreshOptionalServices();

            MainWindow.RegisterDesktopHotkeys(_hotkeys,
                () => { if (_mainWindow is { IsDisposed: false } window) window.ToggleGamingMode(); else MainWindow.ToggleGamingWithoutWindow(); },
                () => ShowMainWindow(), false);
            if (!safeMode && !reviewRequired)
            {
                ZoneSnapEngine.AllowOwnWindow = h => _mainWindow is { IsDisposed: false, IsHandleCreated: true } window && window.Handle == h;
                ZoneSnapEngine.Start();
            }
            // 2.4.6.1: ενημέρωση χρήστη αν κάποια global συντόμευση ήταν ήδη πιασμένη (δεν θα δουλέψει).


            // Fences-style: διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας κρύβει/επαναφέρει
            // όλα τα εικονίδια εκτός του Ο Υπολογιστής μου/φάκελος χρήστη/Πίνακας Ελέγχου/Κάδος
            // Ανακύκλωσης — ζητήθηκε ρητά. Τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής, εκτός αν ο
            // χρήστης το απενεργοποίησε ρητά (DeskZones page, "AppSettings.QuickHideDoubleClickEnabled").
            _taskbarWatcher.ExplorerRestarted += () =>
            {
                try { if (!WindowsUpdateGuard.RequiresReview) GlassBlur.SetTaskbarTranslucency(WindowsSystemSettings.GetTransparencyEffectsEnabled()); }
                catch (Exception) { } // σιωπηλό fallback — ζητήθηκε ρητά, βλ. E2
            };

            if (!safeMode && AppSettings.Load().QuickHideDoubleClickEnabled) DesktopIconVisibilityEngine.Instance.Start();
            else DesktopIconVisibilityEngine.Instance.RestoreIfHidden();
            ThemeManager.UiContext = _exitUi;
            AppActivity.Start(() => _nativeContainers?.HasActivityRules == true);
            _desktopServicesReady = !reviewRequired;
            ReconcileDesktopServices();
            // No background thread or I/O during idle except a once-per-minute rule check.
            // Build fingerprint is read only every 15 minutes; no Windows file hashing in the timer.
            if (!safeMode)
            {
                try
                {
                    if (!reviewRequired && RuntimeSessionPolicy.Allows(SessionBackgroundService.ScheduledAutomations,
                        AppSettings.Peek().OpsisAutomationsEnabled, SafeModeService.IsSafeMode))
                        OpsisAutomationStudio.ExecuteDue(true);
                }
                catch (Exception e) { RotatingLogger.Write("Automation startup: " + e.Message); }
                _experienceTimer.Tick += (_, _) =>
                {
                    try
                    {
                        var settings = AppSettings.Peek();
                        long now = Environment.TickCount64;
                        if (now - _lastExperienceSafetyCheck >= ExperienceSafetyIntervalMs)
                        {
                            _lastExperienceSafetyCheck = now;
                            if (WindowsUpdateGuard.EvaluateStartup()) IsolateShellForUpdate();
                            // Opsis-only accent; no work for disabled accent extraction.
                            if (RuntimeSessionPolicy.Allows(SessionBackgroundService.PeriodicWallpaperAccent,
                                settings.WallpaperAccentEnabled, SafeModeService.IsSafeMode))
                            {
                                try { WallpaperAccentEngine.RefreshIfChanged(); }
                                catch (Exception e) { RotatingLogger.WriteRecurring(RotatingLogger.RecurringSource.WallpaperAccent, "Wallpaper accent refresh: " + e.Message); }
                            }
                        }
                        if (WindowsUpdateGuard.RequiresReview) IsolateShellForUpdate();
                        else if (!_compatibilityIsolationApplied)
                        {
                            if (RuntimeSessionPolicy.Allows(SessionBackgroundService.ReplacementTaskbar, settings.ReplaceWindowsTaskbar, SafeModeService.IsSafeMode)) ReplacementTaskbar.RefreshSettings();
                            if (RuntimeSessionPolicy.Allows(SessionBackgroundService.ScheduledAutomations,
                                settings.OpsisAutomationsEnabled, SafeModeService.IsSafeMode)) OpsisAutomationStudio.ExecuteDue(false);
                        }
                    }
                    catch (Exception e) { RotatingLogger.WriteRecurring(RotatingLogger.RecurringSource.Experience, "Opsis experience check: " + e.Message); }
                    finally { RefreshExperienceCadence(); }
                };
                _experienceTimer.Start();
                RefreshExperienceCadence();
            }
            DesktopToolbar.RefreshSettings();
            if (!reviewRequired) ReplacementTaskbar.RefreshSettings();
            if (!safeMode) VirtualWorkspaces.Start();
            AutoTileService.RefreshSettings();
            MotionDesk.Services.DiscordPresence.Instance.Apply();
            RefreshDesktopMenuPipe();
            if (!safeMode)
            {
                try
                {
                    bool enabled = AppSettings.Peek().DesktopMenuEnabled;
                    if (enabled != DesktopMenu.IsRegistered()) DesktopMenu.Apply(enabled);
                }
                catch (Exception ex) { RotatingLogger.Write("Desktop menu setup failed: " + ex.Message); }
            }
            if (!safeMode) { try { StudioMusic.Start(); } catch (Exception ex) { RotatingLogger.Write("music overlay failed to start: " + ex.Message); } }
            if (!safeMode && DeskStripHostEngine.Instance.GetSettings().StartEnabled)
                DeskStripHostEngine.Instance.Enable();
            if (!safeMode && AppSettings.Load().UseNativeContainers) StartNativeContainers();
            // Include the seven native-container registrations in the same conflict summary.
            var failedHotkeys = HotkeyManager.TakeFailureSummary();
            if (failedHotkeys.Length > 0)
                ShowPrivateBalloon(8000, "Opsis", string.Format(LocalizationManager.T("Hotkey.Conflict"), failedHotkeys), ToolTipIcon.Warning);

            // Restore lightweight persistent preferences before showing the manager.
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            _wallpaperEnabled = !safeMode && WallpaperHostEngine.Instance.IsEnabled;

            if (!safeMode)
            {
                // Time-of-day schedule (may switch the wallpaper mode before the wallpaper starts)
                WallpaperHostEngine.Instance.ApplyScheduleNow();

                if (appSettings.StartWallpaperWithWindows)
                {
                    WallpaperHostEngine.Instance.Enable();
                    _wallpaperEnabled = true;
                }

                // The clock/date/temperature overlay keeps working over the normal Windows wallpaper when the animated wallpaper is off
                WallpaperHostEngine.Instance.UpdateOverlayOnly();

                CheckLockScreenFollowsWallpaper();
            }

            // The tray owns startup restoration and desktop shortcuts. The management
            // window and its Dashboard are constructed only when explicitly opened.
            WorkspaceProfileService.ExitSaveDone = false;
            WorkspaceProfileService.RestoreStartupSession();
            if (!safeMode && !reviewRequired)
            {
                try { AutomationService.StartForHost(); }
                catch (Exception ex) { RotatingLogger.Write("Profile automation startup: " + ex.GetType().Name); }
            }
            AppActivity.RequestRefresh();
            _wallpaperEnabled = WallpaperHostEngine.Instance.IsEnabled;
            _wallpaperToggleItem.Checked = _wallpaperEnabled;
            WallpaperHostEngine.Instance.StateChanged += OnWallpaperStateChanged;
            bool launchedInBackground = desktopAction != null || Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (launchedInBackground)
            {
                if (!safeMode && desktopAction != null && AppSettings.Peek().DesktopMenuEnabled) DesktopMenu.Run(desktopAction);
            }
            else
            {
                // "--page <key>": ανοίγει κατευθείαν σε μια σελίδα (χρήσιμο για δοκιμές και συντομεύσεις), π.χ. --page plugin:OpsisNewsCrawler
                var cmd = Environment.GetCommandLineArgs();
                int pageArg = Array.FindIndex(cmd, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
                using var intro = pageArg < 0 && !safeMode ? CreateStartupIntro() : null;
                intro?.Show();
                intro?.Refresh();
                ShowMainWindow(pageArg >= 0 && pageArg + 1 < cmd.Length ? cmd[pageArg + 1] : null);
            }
        }

        // One startup path for full and future reduced runtime modes.
        private void StartNativeContainers()
        {
            if (_nativeContainers != null || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
            try
            {
                var nativeStore = new Opsis.Containers.ContainersStore(Path.Combine(AppPaths.AppDataFolder, "containers-native.json"));
                try
                {
                    var mons = Screen.AllScreens.Select(sc => new Opsis.Containers.MonitorInfo(sc.DeviceName, sc.Bounds, sc.Primary)).ToList();
                    var note = MotionDesk.Containers.NativeMigration.ImportIfNeeded(nativeStore, Path.Combine(AppPaths.AppDataFolder, "containers"), mons);
                    if (note != null) RotatingLogger.Write(note);
                }
                catch (Exception ex)
                {
                    RotatingLogger.Write("native containers: legacy import failed; host not started: " + ex.Message);
                    throw;
                }
                var host = _nativeContainers = new MotionDesk.Containers.ContainerHost(nativeStore, DesktopMenu.ContainerPipeName);
                var containerKeys = _containerHotkeys = new HotkeyManager();
                // global hotkeys of the native containers: Ctrl+Alt+Shift+H = hide / show all, Ctrl+Alt+Shift+N = new container at the cursor (neither is a Windows default)
                containerKeys.RegisterCtrlAltShift(Keys.H, () => host.ToggleAll());
                containerKeys.RegisterCtrlAltShift(Keys.Space, host.TogglePeek);
                containerKeys.RegisterCtrlAltShift(Keys.N, () => host.NewContainerAtCursor());
                containerKeys.RegisterCtrl(Keys.Oemtilde, () => host.ShowSpotSearch());                              // Spot Search (Ctrl+`), as in the old engine
                containerKeys.RegisterCtrlAltShift(Keys.F, () => host.FocusNext());                                    // focus-container hotkey
                containerKeys.RegisterCtrlAltShift(Keys.OemCloseBrackets, () => host.CycleSnapshot(1));            // next / previous layout snapshot = profile hotkeys
                containerKeys.RegisterCtrlAltShift(Keys.OemOpenBrackets, () => host.CycleSnapshot(-1));
            }
            catch (Exception ex)
            {
                try { _containerHotkeys?.Dispose(); } catch { }
                _containerHotkeys = null;
                try { _nativeContainers?.Dispose(); } catch { }
                _nativeContainers = null;
                RotatingLogger.Write("native containers failed to start: " + ex.Message);
            }
        }

        private void StopNativeContainers()
        {
            try { _containerHotkeys?.Dispose(); }
            finally
            {
                _containerHotkeys = null;
                try { _nativeContainers?.Dispose(); }
                finally { _nativeContainers = null; AppActivity.RequestRefresh(); }
            }
        }

        private static Form CreateStartupIntro()
        {
            var form = new Form
            {
                Text = $"Opsis - Desktop Customization {AppVersion.Label}",
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(545, 340),
                ShowInTaskbar = false, ControlBox = false,
                BackColor = UiTheme.PageBackground
            };
            var brand = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = UiTheme.Surface };
            brand.Paint += (_, e) =>
            {
                int iconSize = Math.Min(brand.Height - 20, brand.Width / 4);
                if (iconSize < 16) return;
                MotionDesk.UI.OpsisBrandMark.Draw(e.Graphics, new RectangleF(20, 10, iconSize, iconSize));
                using var title = new Font("Segoe UI", 24, FontStyle.Bold);
                using var sub = new Font("Segoe UI", 10);
                var left = 30 + iconSize;
                TextRenderer.DrawText(e.Graphics, "OPSIS", title, new Rectangle(left, 14, Math.Max(1, brand.Width-left-12), 42),
                    UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, "Desktop Customization  •  " + AppVersion.Label, sub,
                    new Rectangle(left, 58, Math.Max(1, brand.Width-left-12), 32), UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            form.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(18),
                Text = MainWindow.StudioText(
                    "Opening the Opsis workspace…\n\nFind tools and settings: Ctrl+K.\nHelp for the current page or selected control: F1.\nVersion changes and plugin compatibility: About.\n\nThe tray menu also provides quick access to desktop functions.",
                    "Άνοιγμα του χώρου εργασίας Opsis…\n\nΕύρεση εργαλείων και ρυθμίσεων: Ctrl+K.\nΒοήθεια για τη σελίδα ή το επιλεγμένο στοιχείο: F1.\nΑλλαγές έκδοσης και συμβατότητα προσθέτων: Σχετικά.\n\nΤο μενού του tray προσφέρει γρήγορη πρόσβαση στις λειτουργίες επιφάνειας εργασίας."),
                Font = UiTheme.FontBody, ForeColor = UiTheme.TextPrimary });
            form.Controls.Add(brand);
            return form;
        }

        private ContextMenuStrip? _pluginMenuHost;
        private int _pluginMenuIndex;
        private readonly List<ToolStripItem> _pluginMenuItems = new();

        // The plugin part of the tray menu: built from SNAPSHOTS of the plugin state (PluginHost), retried when a plugin changes while it is read, and never allowed to throw into the UI.
        private void RebuildPluginMenu()
        {
            if (_pluginMenuHost == null) return;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var groups = PluginHost.Instance.GetTrayMenuGroups();
                    foreach (var old in _pluginMenuItems) { _pluginMenuHost.Items.Remove(old); old.Dispose(); }
                    _pluginMenuItems.Clear();
                    int at = Math.Min(_pluginMenuIndex, _pluginMenuHost.Items.Count);
                    if (groups.Count > 0)
                    {
                        var sep = new ToolStripSeparator(); _pluginMenuHost.Items.Insert(at++, sep); _pluginMenuItems.Add(sep);
                        foreach (var (pluginName, items) in groups)
                        {
                            var sub = new ToolStripMenuItem(pluginName);
                            foreach (var pluginMenu in items)
                            {
                                if (pluginMenu.IsSeparator) { sub.DropDownItems.Add(new ToolStripSeparator()); continue; }
                                var invoke = pluginMenu.Invoke;
                                sub.DropDownItems.Add(LocalizationManager.CurrentLanguage == "el" ? pluginMenu.TitleEl : pluginMenu.TitleEn, null,
                                    (s, e) => { try { invoke(); } catch (Exception ex) { RotatingLogger.Write("Plugin menu item failed: " + ex.Message); } });
                            }
                            _pluginMenuHost.Items.Insert(at++, sub); _pluginMenuItems.Add(sub);
                        }
                    }
                    return;
                }
                catch (Exception ex) when (attempt < 3) { RotatingLogger.Write($"Tray plugin menu, attempt {attempt}: {ex.Message}"); System.Threading.Thread.Sleep(20 * attempt); }
                catch (Exception ex) { RotatingLogger.Write("Tray plugin menu failed: " + ex.Message); }
            }
        }

        private ToolStripMenuItem? _pluginApprovalItem;

        private void UpdatePluginApprovalItem()
        {
            if (_pluginApprovalItem == null) return;
            int n = PluginHost.Instance.PendingApprovals.Count;
            _pluginApprovalItem.Visible = n > 0;
            _pluginApprovalItem.Text = string.Format(OpsisStrings.T("ui.pending.plugins"), n);
        }

        // A few seconds after start-up (never while the tray / main window are being built): ask once about every plugin that waits for approval.
        private void SchedulePluginApproval()
        {
            if (PluginHost.Instance.PendingApprovals.Count == 0) return;
            if (_pluginApprovalTimer != null || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
            _pluginApprovalTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            _pluginApprovalTimer.Tick += (_, _) =>
            {
                _pluginApprovalTimer?.Stop();
                _pluginApprovalTimer?.Dispose();
                _pluginApprovalTimer = null;
                if (System.Threading.Volatile.Read(ref _exitStarted) == 0) AskPluginApproval();
            };
            _pluginApprovalTimer.Start();
        }

        private void IsolateShellForUpdate()
        {
            if (_compatibilityIsolationApplied) return;
            _compatibilityIsolationApplied = true;
            void Hold(Action action, string name)
            {
                try { action(); }
                catch (Exception e) { RotatingLogger.Write("Windows update isolation " + name + ": " + e.Message); }
            }
            Hold(ReplacementTaskbar.Stop, "taskbar");
            Hold(WindowRulesEngine.Instance.Stop, "window rules");
            Hold(AutoTileService.Stop, "auto tile");
            Hold(AutomationService.SuspendIfCreated, "profile automations");
            Hold(ZoneSnapEngine.Stop, "zone hooks");
            Hold(StopOptionalHotkeys, "optional hotkeys");
            Hold(_hotCorners.Stop, "hot corners");
            Hold(MotionDesk.UI.OpsisCursorFx.Stop, "cursor effects");
            Hold(() => MotionDesk.Appearance.AppearanceService.Instance.Stop(), "appearance");
            Hold(PluginHost.Instance.SuspendForCompatibility, "plugins");
            RotatingLogger.Write("Windows update isolation applied. Preferences preserved; restart after explicit approval.");
        }

        private void AskPluginApproval()
        {
            if (WindowsUpdateGuard.RequiresReview || _compatibilityIsolationApplied) return;
            // a hidden top-most owner so the box is never lost behind other windows (it used to appear behind the desktop at login and was never answered)
            using var owner = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0, Size = new System.Drawing.Size(1, 1), StartPosition = FormStartPosition.CenterScreen };
            owner.Show();
            PluginHost.Instance.ResolvePending(req =>
            {
                if (MessageBox.Show(owner,
                string.Format(LocalizationManager.T("Plugin.TrustBody"), req.Name, req.Folder, req.Sha256.Substring(0, 16))
                + Environment.NewLine + Environment.NewLine
                + "Publisher / Εκδότης: " + req.Publisher
                + Environment.NewLine + "Signature / Υπογραφή: " + req.SignatureStatus
                + Environment.NewLine + "Requested capabilities / Δηλωμένες δυνατότητες: " + req.Capabilities
                + Environment.NewLine + Environment.NewLine
                + "⚠ In-process plugins are not sandboxed. / Τα DLL plugins εκτελούνται χωρίς sandbox.",
                LocalizationManager.T("Plugin.TrustTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
                foreach (string capability in req.RequestedCapabilities ?? Array.Empty<string>())
                {
                    if (MessageBox.Show(owner,
                        req.Name + ": " + capability + Environment.NewLine +
                        "Approve this declared capability? Denial keeps the entire plugin disabled. This is not an OS permission sandbox." + Environment.NewLine +
                        "Έγκριση δηλωμένης δυνατότητας; Η άρνηση εμποδίζει τη φόρτωση ολόκληρου του plugin. Δεν αποτελεί απομόνωση δικαιωμάτων Windows.",
                        LocalizationManager.T("Plugin.TrustTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
                }
                return true;
            });
            UpdatePluginApprovalItem();
        }

        // Privacy boundary for our own Windows toast/balloon messages. Windows and
        // third-party application notifications are outside Opsis' control.
        private void ShowPrivateBalloon(int timeout, string title, string message, ToolTipIcon icon)
        {
            if (MonitorTopologyService.IsPresentationDisplay()) return;
            _trayIcon.ShowBalloonTip(timeout, title, message, icon);
        }

        private void ShowMainWindow(string? page = null)
        {
            if (System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new MainWindow(hostedByTray: true);
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
                _mainWindow.Show();
            }
            else
            {
                if (_mainWindow.WindowState == FormWindowState.Minimized)
                    _mainWindow.WindowState = FormWindowState.Normal;

                _mainWindow.Show();
                _mainWindow.BringToFront();
                _mainWindow.Activate();
            }

            if (page != null) _mainWindow.NavigateTo(page);
        }

        // "Auto-detect failure" για το MD roadmap item "lock screen matches wallpaper, με
        // self-heal": ΧΩΡΙΣ Scheduled Task (ρητή απόφαση του χρήστη — ένα scheduled task που
        // ξανατρέχει με highest privileges σε κάθε logon μοιάζει με τεχνική persistence που
        // χρησιμοποιεί και malware). Αντ' αυτού, σε κάθε κανονική εκκίνηση του Opsis με τα Windows,
        // αν η ρύθμιση είναι ενεργή αλλά το πραγματικό registry key δεν ταιριάζει πλέον με το
        // αναμενόμενο αρχείο (π.χ. το επανέφερε ένα Windows update ή το άλλαξε ο χρήστης από το
        // Settings app), ρωτάμε με pop-up αν θέλει να το ξαναενεργοποιήσει — ποτέ σιωπηλή επανα-ανύψωση.
        private async void CheckLockScreenFollowsWallpaper()
        {
            var settings = AppSettings.Load();
            if (!settings.LockScreenFollowsWallpaper || string.IsNullOrEmpty(settings.LockScreenSnapshotPath)) return;
            if (LockScreenEngine.MatchesExpected(settings.LockScreenSnapshotPath)) return;
            if (!File.Exists(settings.LockScreenSnapshotPath)) return;

            var choice = MessageBox.Show(
                LocalizationManager.T("Dialog.LockScreenDriftedBody"),
                LocalizationManager.T("Dialog.LockScreenDriftedTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
            {
                settings.LockScreenFollowsWallpaper = false;
                settings.Save();
                return;
            }

            string error = string.Empty;
            string path = settings.LockScreenSnapshotPath;
            bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(path, out error));
            if (!ok && error != "cancelled")
            {
                // Μη κρίσιμο αν αποτύχει ξανά — ο χρήστης το είδε ήδη και το ενέκρινε, απλά δεν
                // ξαναενοχλούμε σε αυτή τη συνεδρία.
            }
        }

        private void ApplyProfile(string name)
        {
            WorkspaceProfileService.Load(name);
            ShowPrivateBalloon(1500, "Opsis", string.Format(LocalizationManager.T("Tray.ProfileAppliedFormat"), name), ToolTipIcon.Info);
        }

        private void OnWallpaperStateChanged()
        {
            _wallpaperEnabled = WallpaperHostEngine.Instance.IsEnabled;
            _wallpaperToggleItem.Checked = _wallpaperEnabled;
        }

        private void ToggleWallpaper()
        {
            _wallpaperEnabled = !WallpaperHostEngine.Instance.IsEnabled;
            _wallpaperToggleItem.Checked = _wallpaperEnabled;

            if (_wallpaperEnabled)
                WallpaperHostEngine.Instance.Enable();
            else
                WallpaperHostEngine.Instance.Disable();
        }

        private void ChooseWallpaperVideo()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.Video") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitleTray"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddVideosAsync(dialog.FileNames);
        }

        // Τα .wmv μετατρέπονται αυτόματα σε .mp4 μέσω FFmpeg (WmvConversionService) πριν
        // προστεθούν — ο ενσωματωμένος player (WebView2/Chromium) δεν έχει decoder για τον παλιό
        // codec WMV3/VC-1. Βλ. αναλυτικό σχόλιο στο WmvConversionService.
        private async Task AddVideosAsync(string[] fileNames)
        {
            var wmvFiles = fileNames.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToArray();
            var finalPaths = fileNames.Except(wmvFiles).ToList();

            if (wmvFiles.Length > 0)
            {
                if (!WmvConversionService.IsFfmpegAvailable)
                {
WmvConversionService.PromptInstallFfmpeg(wmvFiles.Length);
                }
                else
                {
                    foreach (var wmv in wmvFiles)
                    {
                        string? mp4 = await WmvConversionService.ConvertToMp4Async(wmv);
                        if (mp4 != null) finalPaths.Add(mp4);
                    }
                }
            }

            if (finalPaths.Count == 0) return;
            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(finalPaths.ToArray());
            settings.Mode = "Video";
            settings.Save();
            if (!_wallpaperEnabled) ToggleWallpaper();
            WallpaperHostEngine.Instance.Enable();
            // Βλ. αναλυτικό σχόλιο στο MainWindow.AddWallpaperVideosAsync — χωρίς αυτό, ένα ήδη
            // ανοιχτό wallpaper window δεν μαθαίνει ποτέ ότι το Mode/playlist άλλαξε.
            _ = WallpaperHostEngine.Instance.RefreshAllAsync();
        }

        private System.Windows.Forms.Timer? _alertTimer;
        private int _cpuHighTicks, _ramHighTicks;
        private DateTime _lastAlertUtc = DateTime.MinValue;

        // Called every 5 s: CPU or memory >= 90% for 12 consecutive ticks (one minute) -> one tray balloon, then a 10-minute pause
        internal static (bool DockRestore, bool UsageAlerts) SelectOptionalServices(AppSettings settings, bool safeMode) =>
            SelectOptionalServices(settings, safeMode, RuntimeSessionPolicy.Allowed);

        internal static (bool DockRestore, bool UsageAlerts) SelectOptionalServices(AppSettings settings, bool safeMode,
            SessionBackgroundService allowed) =>
            (RuntimeSessionPolicy.Allows(allowed, SessionBackgroundService.DockRestore, settings.RestoreWindowsAfterDocking, safeMode),
             RuntimeSessionPolicy.Allows(allowed, SessionBackgroundService.UsageAlerts, settings.UsageAlertsEnabled, safeMode));

        private ToolStripMenuItem BuildSessionBackgroundMenu()
        {
            string T(string en, string el) => MainWindow.StudioText(en, el);
            var menu = new ToolStripMenuItem(T("Background services · this session", "Υπηρεσίες παρασκηνίου · αυτή η συνεδρία"));
            var hint = new ToolStripMenuItem(T("Allow only if enabled in saved settings", "Επιτρέπονται μόνο αν είναι ενεργές στις ρυθμίσεις")) { Enabled = false };
            menu.DropDownItems.Add(hint);
            var entries = new (SessionBackgroundService Flag, string Title, Func<AppSettings, bool> Saved)[]
            {
                (SessionBackgroundService.DockRestore, T("Restore windows after docking", "Επαναφορά παραθύρων μετά από σύνδεση οθόνης"), s => s.RestoreWindowsAfterDocking),
                (SessionBackgroundService.UsageAlerts, T("CPU/RAM alerts", "Ειδοποιήσεις CPU/RAM"), s => s.UsageAlertsEnabled),
                (SessionBackgroundService.DiscordPresence, T("Discord presence", "Παρουσία Discord"), s => s.DiscordPresenceEnabled),
                (SessionBackgroundService.ScheduledAutomations, T("Automations (scheduled and profile rules)", "Αυτοματισμοί (χρονοπρόγραμμα και κανόνες προφίλ)"), s => s.OpsisAutomationsEnabled || AutomationService.HasEnabledRules),
                (SessionBackgroundService.PeriodicWallpaperAccent, T("Periodic wallpaper accent", "Περιοδική ανανέωση χρώματος φόντου"), s => s.WallpaperAccentEnabled)
            };
            var items = new List<(ToolStripMenuItem Item, SessionBackgroundService Flag, Func<AppSettings, bool> Saved)>();
            foreach (var entry in entries)
            {
                var item = new ToolStripMenuItem(entry.Title);
                item.Click += (_, _) =>
                {
                    if (SafeModeService.IsSafeMode || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
                    RuntimeSessionPolicy.SetAllowed(entry.Flag, (RuntimeSessionPolicy.Allowed & entry.Flag) == 0);
                    RefreshSessionBackgroundServices();
                };
                items.Add((item, entry.Flag, entry.Saved));
                menu.DropDownItems.Add(item);
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            var allow = new ToolStripMenuItem(T("Use saved selections", "Χρήση αποθηκευμένων επιλογών"));
            var pause = new ToolStripMenuItem(T("Suspend these five services", "Αναστολή αυτών των πέντε υπηρεσιών"));
            void Change(SessionBackgroundService mask)
            {
                if (SafeModeService.IsSafeMode || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
                RuntimeSessionPolicy.SetAllowed(mask);
                RefreshSessionBackgroundServices();
            }
            allow.Click += (_, _) => Change(RuntimeSessionPolicy.ReplaceBackground(RuntimeSessionPolicy.Allowed, true));
            pause.Click += (_, _) => Change(RuntimeSessionPolicy.ReplaceBackground(RuntimeSessionPolicy.Allowed, false));
            menu.DropDownItems.Add(allow); menu.DropDownItems.Add(pause);
            menu.DropDownOpening += (_, _) =>
            {
                var s = AppSettings.Peek();
                bool safe = SafeModeService.IsSafeMode;
                var mask = RuntimeSessionPolicy.Allowed;
                foreach (var (item, flag, saved) in items)
                {
                    item.Checked = (mask & flag) != 0;
                    item.Enabled = !safe;
                    item.ToolTipText = safe ? T("Safe mode", "Ασφαλής λειτουργία") :
                        !saved(s) ? T("Off in saved settings", "Ανενεργή στις αποθηκευμένες ρυθμίσεις") :
                        (mask & flag) == 0 ? T("Suspended for this session", "Σε αναστολή για αυτή τη συνεδρία") :
                        T("Allowed for this session; other safety checks still apply", "Επιτρέπεται στη συνεδρία· ισχύουν οι έλεγχοι ασφαλείας");
                }
                allow.Enabled = !safe && (mask & SessionBackgroundService.Background) != SessionBackgroundService.Background;
                pause.Enabled = !safe && (mask & SessionBackgroundService.Background) != SessionBackgroundService.None;
            };
            return menu;
        }

        private ToolStripMenuItem BuildSessionDesktopMenu()
        {
            string T(string en, string el) => MainWindow.StudioText(en, el);
            var menu = new ToolStripMenuItem(T("Desktop services · this session", "Υπηρεσίες επιφάνειας εργασίας · αυτή η συνεδρία"));
            var wallpaper = new ToolStripMenuItem(T("Allow animated wallpaper (keep independent overlays)", "Κινούμενο φόντο (διατήρηση ανεξάρτητων overlays)"));
            var dock = new ToolStripMenuItem(T("Allow DeskStrip", "Επιτρέπεται το DeskStrip"));
            var widgets = new ToolStripMenuItem(T("Allow widgets (retain layout)", "Επιτρέπονται τα widgets (διατήρηση διάταξης)"));
            var music = new ToolStripMenuItem(T("Allow music visualizer", "Επιτρέπεται το μουσικό visualizer"));
            var appearance = new ToolStripMenuItem(T("Allow automatic appearance and live effects", "Αυτόματη εμφάνιση και ζωντανά εφέ"));
            var autoTile = new ToolStripMenuItem(T("Allow automatic window tiling", "Αυτόματη διάταξη παραθύρων"));
            var adaptive = new ToolStripMenuItem(T("Allow adaptive performance sampling", "Δειγματοληψία προσαρμοστικής απόδοσης"));
            var taskbar = new ToolStripMenuItem(T("Allow Opsis replacement taskbar", "Επιτρέπεται η taskbar του Opsis"));
            var zones = new ToolStripMenuItem(T("Allow automatic zone snapping", "Επιτρέπεται η αυτόματη προσκόλληση σε ζώνες"));
            var shellOptions = new[] {
                (SessionBackgroundService.HotCorners, new ToolStripMenuItem(T("Allow hot corners", "Επιτρέπονται οι ενεργές γωνίες"))),
                (SessionBackgroundService.WindowRules, new ToolStripMenuItem(T("Allow window rules", "Επιτρέπονται οι κανόνες παραθύρων"))),
                (SessionBackgroundService.CursorEffects, new ToolStripMenuItem(T("Allow cursor effects", "Επιτρέπονται τα εφέ δείκτη"))),
                (SessionBackgroundService.DesktopHotkeys, new ToolStripMenuItem(T("Allow optional desktop shortcuts", "Επιτρέπονται οι προαιρετικές συντομεύσεις")))
            };
            void Change(SessionBackgroundService flag, Action reconcile)
            {
                if (SafeModeService.IsSafeMode || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
                RuntimeSessionPolicy.SetAllowed(flag, !RuntimeSessionPolicy.IsPermitted(flag));
                try { reconcile(); }
                catch (Exception ex)
                {
                    RotatingLogger.Write("Session desktop transition: " + ex.Message);
                    MessageBox.Show(ex.Message, "OPSIS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                AppActivity.RequestRefresh();
            }
            wallpaper.Click += (_, _) => Change(SessionBackgroundService.AnimatedWallpaper, WallpaperHostEngine.ReconcileSessionIfCreated);
            dock.Click += (_, _) => Change(SessionBackgroundService.DeskStrip, DeskStripHostEngine.ReconcileSessionIfCreated);
            widgets.Click += (_, _) => Change(SessionBackgroundService.Widgets, WidgetHostEngine.ReconcileSessionIfCreated);
            music.Click += (_, _) => Change(SessionBackgroundService.MusicVisualizer, StudioMusic.ReconcileSession);
            appearance.Click += (_, _) =>
            {
                if (WindowsUpdateGuard.RequiresReview || _compatibilityIsolationApplied) return;
                Change(SessionBackgroundService.AppearanceAutomatic, () =>
                {
                    var svc = MotionDesk.Appearance.AppearanceService.Instance;
                    if (svc.Started) svc.SetSessionAllowed(RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.AppearanceAutomatic));
                });
            };
            autoTile.Click += (_, _) =>
            {
                if (WindowsUpdateGuard.RequiresReview || _compatibilityIsolationApplied) return;
                Change(SessionBackgroundService.AutoTile, () => { AutoTileService.RefreshSettings(); ReconcileDesktopServices(); });
            };
            adaptive.Click += (_, _) => Change(SessionBackgroundService.AdaptiveSampling, PerformanceGovernor.ReconcileSession);
            menu.DropDownItems.Add(wallpaper); menu.DropDownItems.Add(dock); menu.DropDownItems.Add(widgets); menu.DropDownItems.Add(music); menu.DropDownItems.Add(appearance);
            taskbar.Click += (_, _) =>
            {
                if (WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold || _compatibilityIsolationApplied) return;
                Change(SessionBackgroundService.ReplacementTaskbar, () => { ReplacementTaskbar.RefreshSettings(); RefreshExperienceCadence(); });
            };
            zones.Click += (_, _) =>
            {
                if (WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold || _compatibilityIsolationApplied) return;
                Change(SessionBackgroundService.ZoneSnap, () =>
                {
                    if (RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.ZoneSnap)) ZoneSnapEngine.Start();
                    else ZoneSnapEngine.Stop();
                });
            };
            menu.DropDownItems.Add(autoTile); menu.DropDownItems.Add(adaptive); menu.DropDownItems.Add(taskbar); menu.DropDownItems.Add(zones);
            foreach (var (flag, item) in shellOptions)
            {
                item.Click += (_, _) => { if (!WindowsUpdateGuard.RequiresReview && !WindowsUpdateGuard.RuntimeHold && !_compatibilityIsolationApplied) Change(flag, ReconcileDesktopServices); };
                menu.DropDownItems.Add(item);
            }
            menu.DropDownOpening += (_, _) =>
            {
                wallpaper.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.AnimatedWallpaper);
                dock.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.DeskStrip);
                widgets.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.Widgets);
                music.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.MusicVisualizer);
                appearance.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.AppearanceAutomatic) && !MotionDesk.Appearance.AppearanceService.Instance.AutomaticSessionSuspended;
                appearance.Enabled = !SafeModeService.IsSafeMode && !WindowsUpdateGuard.RequiresReview && !_compatibilityIsolationApplied && MotionDesk.Appearance.AppearanceService.Instance.Started && MotionDesk.Appearance.AppearanceService.Instance.EngineActive;
                autoTile.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.AutoTile);
                autoTile.Enabled = !SafeModeService.IsSafeMode && !WindowsUpdateGuard.RequiresReview && !_compatibilityIsolationApplied;
                adaptive.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.AdaptiveSampling);
                adaptive.Enabled = !SafeModeService.IsSafeMode;
                taskbar.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.ReplacementTaskbar);
                taskbar.Enabled = !SafeModeService.IsSafeMode && !WindowsUpdateGuard.RequiresReview && !WindowsUpdateGuard.RuntimeHold && !_compatibilityIsolationApplied;
                zones.Checked = RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.ZoneSnap);
                zones.Enabled = !SafeModeService.IsSafeMode && !WindowsUpdateGuard.RequiresReview && !WindowsUpdateGuard.RuntimeHold && !_compatibilityIsolationApplied;
                foreach (var (flag, item) in shellOptions)
                { item.Checked = RuntimeSessionPolicy.IsPermitted(flag); item.Enabled = zones.Enabled; }
                wallpaper.Enabled = dock.Enabled = widgets.Enabled = music.Enabled = !SafeModeService.IsSafeMode;
            };
            return menu;
        }

        private void QueueOptionalRefresh()
        {
            if (System.Threading.Volatile.Read(ref _exitStarted) != 0 ||
                System.Threading.Interlocked.Exchange(ref _optionalRefreshQueued, 1) != 0) return;
            try
            {
                _exitUi.Post(_ =>
                {
                    System.Threading.Interlocked.Exchange(ref _optionalRefreshQueued, 0);
                    if (System.Threading.Volatile.Read(ref _exitStarted) == 0) RefreshOptionalServices();
                }, null);
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Exchange(ref _optionalRefreshQueued, 0);
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void StopOptionalHotkeys()
        {
            var keys = _optionalHotkeys;
            _optionalHotkeys = null;
            keys?.Dispose();
        }

        private void ReconcileDesktopServices()
        {
            if (!_desktopServicesReady || System.Threading.Volatile.Read(ref _exitStarted) != 0) return;
            bool held = _compatibilityIsolationApplied || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold;
            void Apply(Action action, string name)
            { try { action(); } catch (Exception ex) { RotatingLogger.Write("Desktop service " + name + ": " + ex.Message); } }
            if (held || SafeModeService.IsSafeMode)
            {
                Apply(_hotCorners.Stop, "hot corners");
                Apply(WindowRulesEngine.Instance.Stop, "window rules");
                Apply(MotionDesk.UI.OpsisCursorFx.Stop, "cursor effects");
                Apply(StopOptionalHotkeys, "hotkeys");
                return;
            }
            Apply(_hotCorners.ReconcileSession, "hot corners");
            Apply(WindowRulesEngine.Instance.ReconcileSession, "window rules");
            Apply(MotionDesk.UI.OpsisCursorFx.ReconcileSession, "cursor effects");
            Apply(() =>
            {
                if (!RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.DesktopHotkeys)) { StopOptionalHotkeys(); return; }
                if (_optionalHotkeys != null) return;
                var keys = new HotkeyManager();
                try
                {
                    MainWindow.RegisterOptionalDesktopHotkeys(keys, () => { if (_mainWindow is { IsDisposed: false } window) window.ToggleGamingMode(); else MainWindow.ToggleGamingWithoutWindow(); });
                    keys.RegisterCtrlAlt('F', () => DeskFlipEngine.Show());
                    keys.RegisterCtrlAltShift(Keys.K, DesktopLauncher.Open);
                    _optionalHotkeys = keys;
                }
                catch { keys.Dispose(); throw; }
            }, "hotkeys");
        }

        private void RefreshOptionalServices()
        {
            ReconcileDesktopServices();
            RefreshSessionBackgroundServices();
            BackdropContrast.Refresh();
            WallpaperHostEngine.RefreshOptionalIfCreated();
            RefreshDesktopMenuPipe();
            PluginHost.Instance.HoldIfGloballyDisabled();
            AppActivity.RequestRefresh();
        }

        private void RefreshSessionBackgroundServices()
        {
            var desired = SelectOptionalServices(AppSettings.Peek(), SafeModeService.IsSafeMode);
            try
            {
                if (desired.DockRestore) _dockRestorer ??= new DockWindowRestorer();
                else { _dockRestorer?.Dispose(); _dockRestorer = null; }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            try
            {
                if (desired.UsageAlerts && _alertTimer == null)
                {
                    _alertTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                    _alertTimer.Tick += (_, _) => UsageAlertTick();
                    _alertTimer.Start();
                }
                else if (!desired.UsageAlerts && _alertTimer != null)
                {
                    _alertTimer.Dispose(); _alertTimer = null;
                    _cpuHighTicks = _ramHighTicks = 0;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            DiscordPresence.Instance.Apply();
            if (_compatibilityIsolationApplied) AutomationService.SuspendIfCreated();
            else AutomationService.ReconcileIfCreated();
            RefreshExperienceCadence();
            AppActivity.RequestRefresh();
        }

        internal static bool NeedsDesktopMenuPipe(bool enabled, bool safeMode) => enabled && !safeMode;

        private void RefreshDesktopMenuPipe()
        {
            try
            {
                bool desired = NeedsDesktopMenuPipe(AppSettings.Peek().DesktopMenuEnabled, SafeModeService.IsSafeMode);
                if (desired && _controlPipe == null)
                {
                    var pipe = new Opsis.Containers.PipeServer(DesktopMenu.PipeName, new DesktopMenu.Api());
                    try { pipe.Start(); _controlPipe = pipe; }
                    catch { pipe.Dispose(); throw; }
                }
                else if (!desired && _controlPipe != null)
                {
                    var pipe = _controlPipe; _controlPipe = null; pipe.Dispose();
                }
            }
            catch (Exception ex) { RotatingLogger.Write("Desktop menu pipe: " + ex.Message); }
        }

        private void UsageAlertTick()
        {
            try
            {
                if (!RuntimeSessionPolicy.Allows(SessionBackgroundService.UsageAlerts, AppSettings.Peek().UsageAlertsEnabled, SafeModeService.IsSafeMode))
                { _cpuHighTicks = _ramHighTicks = 0; return; }
                var m = SystemMonitorService.Instance.GetSnapshot();
                double ramPct = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                _cpuHighTicks = m.CpuPercent >= 90 ? _cpuHighTicks + 1 : 0;
                _ramHighTicks = ramPct >= 90 ? _ramHighTicks + 1 : 0;
                if (DateTime.UtcNow - _lastAlertUtc < TimeSpan.FromMinutes(10)) return;
                if (_ramHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _ramHighTicks = 0;
                    var top = TopProcessesService.TopByMemory(1).FirstOrDefault();
                    ShowPrivateBalloon(8000, LocalizationManager.T("Alert.Title"), string.Format(LocalizationManager.T("Alert.RamHigh"), top.Name ?? "?"), ToolTipIcon.Warning);
                }
                else if (_cpuHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _cpuHighTicks = 0;
                    ShowPrivateBalloon(8000, LocalizationManager.T("Alert.Title"), LocalizationManager.T("Alert.CpuHigh"), ToolTipIcon.Warning);
                }
            }
            catch (Exception) { /* an alert must never break the tray */ }
        }

        // Κοινός, πλήρης καθαρισμός εξόδου — καλείται ΚΑΙ από το μενού "Έξοδος" ΚΑΙ από ένα
        // self-update install (μέσω RequestExit/ExitRequested) ώστε και στις δύο περιπτώσεις να
        // αποθηκεύεται η συνεδρία και να σταματούν σωστά τα plugins/hotkeys πριν το Application.Exit().
        // DeskContainers group of the tray menu. The engine publishes its state (profiles, switches, hidden containers) in bridge\state.json;
        // the submenu is rebuilt every time it opens, and every entry sends a command to the engine through the bridge.
        // The tray menu of the NEW native DeskContainers (UseNativeContainers): same group, entries talk to the in-process host instead of the WPF engine.
        private void FillNativeContainersMenu(ToolStripMenuItem menu, MotionDesk.Containers.ContainerHost host)
        {
            string T(string key) => LocalizationManager.T(key);
            menu.DropDownItems.Add(T("DCM.BackupNow"), null, (s, e) => { try { host.Backup(true); } catch (Exception) { } });
            menu.DropDownItems.Add(T("DCM.NewContainer"), null, (s, e) => host.NewContainerAtCursor());
            menu.DropDownItems.Add(T("DCX.NewNote"), null, (s, e) => host.NewContainerAtCursor("Note"));
            menu.DropDownItems.Add(T("DCM.NewPortal"), null, (s, e) => host.NewContainerAtCursor("Portal"));
            menu.DropDownItems.Add(MainWindow.StudioText("Preview containers", "Προσωρινή εμφάνιση containers"), null, (_, _) => host.TogglePeek());
            menu.DropDownItems.Add(new ToolStripSeparator());
            var list = host.Containers();
            var focus = new ToolStripMenuItem(T("DCM.FocusContainer")) { Enabled = list.Any(c => !c.Hidden) };
            var hidden = new ToolStripMenuItem(T("DCM.HiddenContainers")) { Enabled = list.Any(c => c.Hidden) };
            foreach (var c in list)
            {
                string id = c.Id; string title = string.IsNullOrWhiteSpace(c.Title) ? id[..Math.Min(6, id.Length)] : c.Title;
                if (c.Hidden) hidden.DropDownItems.Add(title, null, (s, e) => host.SetHidden(id, false));
                else focus.DropDownItems.Add(title, null, (s, e) => host.Focus(id));
            }
            menu.DropDownItems.Add(focus); menu.DropDownItems.Add(hidden);
            var snaps = new ToolStripMenuItem(T("DCM.Snapshots"));
            snaps.DropDownItems.Add(T("DCM.SaveSnapshot"), null, (s, e) => host.SaveSnapshot("Layout " + DateTime.Now.ToString("yyyy-MM-dd HH.mm")));
            var names = host.SnapshotNames();
            if (names.Count > 0) snaps.DropDownItems.Add(new ToolStripSeparator());
            foreach (var n in names) { string name = n; snaps.DropDownItems.Add(name, null, (s, e) => host.LoadSnapshot(name)); }
            menu.DropDownItems.Add(snaps);
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(T("DCM.QuickHide") + " / " + T("DCM.QuickShow"), null, (s, e) => host.ToggleAll());
            menu.DropDownItems.Add(T("DCM.RestoreIcons"), null, (s, e) => { DesktopIconVisibilityEngine.Instance.RestoreIfHidden(); });
            menu.DropDownItems.Add(T("DCM.OpenSettingsPage"), null, (s, e) => ShowMainWindow("DeskContainers"));
        }

        private ToolStripMenuItem BuildContainersTrayMenu()
        {
            string T(string key) => LocalizationManager.T(key);
            var menu = new ToolStripMenuItem(T("DCM.GroupTitle"));
            void Fill()
            {
                var previous = menu.DropDownItems.Cast<ToolStripItem>().ToArray();
                menu.DropDownItems.Clear();
                foreach (var old in previous) old.Dispose();
                try
                {
                    if (MotionDesk.Containers.ContainerHost.Current is { } native)
                    {
                        FillNativeContainersMenu(menu, native);
                        return;
                    }
                }
                catch (Exception ex) { RotatingLogger.Write("DeskContainers tray submenu: " + ex.Message); }
                menu.DropDownItems.Add(T("DCM.OpenSettingsPage"), null, (_, _) => ShowMainWindow("DeskContainers"));
            }
            menu.DropDownOpening += (s, e) => Fill();
            Fill();
            return menu;
        }

        private void OnApplicationExit(object? sender, EventArgs e)
        {
            Application.ApplicationExit -= OnApplicationExit;
            AppSettings.SettingsSaved -= QueueOptionalRefresh;
            BackdropContrast.Stop();
            _dockRestorer?.Dispose();
            _dockRestorer = null;
        }

        private void PerformExit()
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
            {
                _exitUi.Post(_ => PerformExit(), null);
                return;
            }
            if (System.Threading.Interlocked.Exchange(ref _exitStarted, 1) != 0) return;
            ExitRequested -= PerformExit;
            OpenPage = null;
            // Keep UI-affine cleanup on its owning thread; do not move WinForms plugin Stop to Task.Run.
            void Step(string name, Action action)
            {
                HangTrace.Mark("shutdown begin: " + name);
                System.Diagnostics.Trace.WriteLine("Opsis shutdown begin: " + name);
                try { RotatingLogger.Write("Shutdown begin: " + name); } catch { }
                try { action(); }
                catch (Exception ex) { try { RotatingLogger.Write("Shutdown error: " + name + ": " + ex.Message); } catch { } }
                finally { HangTrace.Mark("shutdown end: " + name); try { RotatingLogger.Write("Shutdown end: " + name); } catch { } }
            }
            Step("startup timer", () => { _startupHealthyTimer?.Stop(); _startupHealthyTimer?.Dispose(); _startupHealthyTimer = null; });
            Step("plugin approval timer", () => { _pluginApprovalTimer?.Stop(); _pluginApprovalTimer?.Dispose(); _pluginApprovalTimer = null; });
            Step("plugin navigation", () => PluginHost.Instance.NavigationRequested -= NavigatePlugin);
            Step("dock restorer", () => OnApplicationExit(null, EventArgs.Empty));
            Step("experience timer", () => { _experienceTimer.Stop(); _experienceTimer.Dispose(); });
            Step("profile automation", AutomationService.StopForHost);
            Step("usage alert timer", () => { _alertTimer?.Stop(); _alertTimer?.Dispose(); _alertTimer = null; });
            Step("save session", WorkspaceProfileService.SaveLastSessionOnce);
            Step("appearance restore", () => MotionDesk.Appearance.AppearanceService.Instance.Stop());
            Step("optional hotkeys", StopOptionalHotkeys);
            Step("hotkeys", () => _hotkeys.Dispose());
            Step("zone hooks", () => { ZoneSnapEngine.Stop(); ZoneSnapEngine.AllowOwnWindow = null; });
            Step("wallpaper event", () => WallpaperHostEngine.Instance.StateChanged -= OnWallpaperStateChanged);
            Step("cursor effects", MotionDesk.UI.OpsisCursorFx.Stop);
            Step("window rules", () => WindowRulesEngine.Instance.Dispose());
            Step("hot corners", () => _hotCorners.Dispose());
            Step("taskbar watcher", () => _taskbarWatcher.Dispose());
            Step("containers and hotkeys", StopNativeContainers);
            Step("music", StudioMusic.Stop);
            Step("wallpaper host", WallpaperHostEngine.StopForExit);
            Step("activity monitor", AppActivity.Stop);
            Step("performance governor", PerformanceGovernor.Stop);
            Step("Discord presence", () => MotionDesk.Services.DiscordPresence.Instance.Stop());
            Step("desktop toolbar", DesktopToolbar.Stop);
            Step("replacement taskbar", ReplacementTaskbar.Stop);
            Step("virtual workspaces", VirtualWorkspaces.Stop);
            Step("auto tile", AutoTileService.Stop);
            Step("desk strip", () => DeskStripHostEngine.Instance.CloseForExit());
            Step("control pipe", () => _controlPipe?.Dispose());
            Step("desktop icons restore", () => DesktopIconVisibilityEngine.Instance.Stop());
            Step("plugins", () => PluginHost.Instance.StopAll());
            Step("tray icon", () => { _trayIcon.Visible = false; _trayIcon.Dispose(); });
            Step("application exit", Application.Exit);
            ExitThread();
        }

        private void ApplyTrayIconStyle()
        {
            try
            {
                var state = OpsisModCore.Read();
                string? style = null;
                if (state.Options.TryGetValue("control.opsis-center", out var options))
                    options.TryGetValue("tray-icon", out style);
                _trayIcon.Icon = style switch
                {
                    "Information" => System.Drawing.SystemIcons.Information,
                    "Shield" => System.Drawing.SystemIcons.Shield,
                    _ => MotionDesk.UI.AppWindowIcon.Value
                };
            }
            catch (Exception ex) { RotatingLogger.Write("Tray icon style: " + ex.Message); }
        }

        private void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                int added = WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
                if (added > 0 && !_wallpaperEnabled) ToggleWallpaper();
            }
        }

        // Το DeskZones έγινε πραγματικό FancyZones-style στυλ (κανένα μόνιμα ορατό
        // παράθυρο-ζώνη πλέον) — το tray μενού ανοίγει απευθείας τον editor διάταξης.
        private void AddDeskZone()
        {
            using var editor = new ZoneLayoutEditorForm();
            editor.ShowDialog();
        }
    }
}

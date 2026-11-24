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
        private readonly HotkeyManager _hotkeys = new();
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
        private int _experienceChecks;
        private bool _compatibilityIsolationApplied;

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
            var healthyTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            healthyTimer.Tick += (_, _) => { SafeModeService.MarkStartupHealthy(); healthyTimer.Stop(); healthyTimer.Dispose(); };
            healthyTimer.Start();

            // Plugin (π.χ. News Crawler): ξεκινούν πριν χτιστεί το μενού ώστε να προσθέσουν τα δικά τους στοιχεία.
            // Dock monitor fingerprint and window return (opt-in); never captures user files.
            var dockRestorer = new DockWindowRestorer();
            Application.ApplicationExit += (_, _) => dockRestorer.Dispose();
            PluginHost.Instance.NavigationRequested += key => ShowMainWindow("plugin:" + key);
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
            ExitRequested += () => PerformExit();

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
            ApplyTrayIconStyle();

            _alertTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _alertTimer.Tick += (_, _) => UsageAlertTick();
            _alertTimer.Start();

            _hotkeys.RegisterCtrlAlt('F', () => DeskFlipEngine.Show());
            if (!safeMode) _hotkeys.RegisterCtrlAltShift(Keys.K, DesktopLauncher.Open);
            // 2.4.6.1: ενημέρωση χρήστη αν κάποια global συντόμευση ήταν ήδη πιασμένη (δεν θα δουλέψει).
            var failedHotkeys = HotkeyManager.TakeFailureSummary();
            if (failedHotkeys.Length > 0)
                _trayIcon.ShowBalloonTip(8000, "Opsis", string.Format(LocalizationManager.T("Hotkey.Conflict"), failedHotkeys), ToolTipIcon.Warning);

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
            AppActivity.Start();
            if (!safeMode && !reviewRequired)
            {
                if (AppSettings.Load().WindowRulesEnabled) WindowRulesEngine.Instance.Start();
                if (AppSettings.Load().CursorFxEnabled)
                {
                    try { MotionDesk.UI.OpsisCursorFx.Start(); }
                    catch (Exception e) { RotatingLogger.Write("Cursor FX startup: " + e.Message); }
                }
            }
            // No background thread or I/O during idle except a once-per-minute rule check.
            // Build fingerprint is read only every 15 minutes; no Windows file hashing in the timer.
            if (!safeMode)
            {
                try { if (!reviewRequired) OpsisAutomationStudio.ExecuteDue(true); }
                catch (Exception e) { RotatingLogger.Write("Automation startup: " + e.Message); }
                _experienceTimer.Tick += (_, _) =>
                {
                    try
                    {
                        if ((++_experienceChecks % 15) == 0)
                        {
                            if (WindowsUpdateGuard.EvaluateStartup()) IsolateShellForUpdate();
                            // Opsis-only accent; no Windows registry writes and only
                            // one settings save when the actual color changes.
                            try { WallpaperAccentEngine.RefreshIfChanged(); }
                            catch (Exception e) { RotatingLogger.Write("Wallpaper accent refresh: " + e.Message); }
                        }
                        if (WindowsUpdateGuard.RequiresReview) IsolateShellForUpdate();
                        else if (AppSettings.Peek().ReplaceWindowsTaskbar) ReplacementTaskbar.RefreshSettings();
                        OpsisAutomationStudio.ExecuteDue(false);
                    }
                    catch (Exception e) { RotatingLogger.Write("Opsis experience check: " + e.Message); }
                };
                _experienceTimer.Start();
            }
            if (!safeMode && !reviewRequired) _hotCorners.Start();
            DesktopToolbar.RefreshSettings();
            if (!reviewRequired) ReplacementTaskbar.RefreshSettings();
            if (!safeMode) VirtualWorkspaces.Start();
            AutoTileService.RefreshSettings();
            MotionDesk.Services.DiscordPresence.Instance.Apply();
            BackdropContrast.EnsureStarted();
            if (!safeMode)
            {
                try
                {
                    _controlPipe = new Opsis.Containers.PipeServer(DesktopMenu.PipeName, new DesktopMenu.Api());
                    _controlPipe.Start();
                    DesktopMenu.Apply(AppSettings.Load().DesktopMenuEnabled);
                }
                catch (Exception ex) { RotatingLogger.Write("Desktop menu setup failed: " + ex.Message); }
            }
            if (!safeMode) { try { StudioMusic.Start(); } catch (Exception ex) { RotatingLogger.Write("music overlay failed to start: " + ex.Message); } }
            if (!safeMode && DeskStripHostEngine.Instance.GetSettings().StartEnabled)
                DeskStripHostEngine.Instance.Enable();
            if (!safeMode && AppSettings.Load().UseNativeContainers)
            {
                try
                {
                    var nativeStore = new Opsis.Containers.ContainersStore(Path.Combine(AppPaths.AppDataFolder, "containers-native.json"));
                    try
                    {
                        var mons = Screen.AllScreens.Select(sc => new Opsis.Containers.MonitorInfo(sc.DeviceName, sc.Bounds, sc.Primary)).ToList();
                        var note = MotionDesk.Containers.NativeMigration.ImportIfNeeded(nativeStore, Path.Combine(AppPaths.AppDataFolder, "containers"), mons);
                        if (note != null) RotatingLogger.Write(note);
                    }
                    catch (Exception ex) { RotatingLogger.Write("native containers: legacy import failed: " + ex.Message); }
                    var host = _nativeContainers = new MotionDesk.Containers.ContainerHost(nativeStore, DesktopMenu.ContainerPipeName);
                    // global hotkeys of the native containers: Ctrl+Alt+Shift+H = hide / show all, Ctrl+Alt+Shift+N = new container at the cursor (neither is a Windows default)
                    _hotkeys.RegisterCtrlAltShift(Keys.H, () => host.ToggleAll());
                    _hotkeys.RegisterCtrlAltShift(Keys.Space, host.TogglePeek);
                    _hotkeys.RegisterCtrlAltShift(Keys.N, () => host.NewContainerAtCursor());
                    _hotkeys.RegisterCtrl(Keys.Oemtilde, () => host.ShowSpotSearch());                              // Spot Search (Ctrl+`), as in the old engine
                    _hotkeys.RegisterCtrlAltShift(Keys.F, () => host.FocusNext());                                    // focus-container hotkey
                    _hotkeys.RegisterCtrlAltShift(Keys.OemCloseBrackets, () => host.CycleSnapshot(1));            // next / previous layout snapshot = profile hotkeys
                    _hotkeys.RegisterCtrlAltShift(Keys.OemOpenBrackets, () => host.CycleSnapshot(-1));
                }
                catch (Exception ex) { RotatingLogger.Write("native containers failed to start: " + ex.Message); }
            }

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

            // "--background": η αυτόματη εκκίνηση με τα Windows (StartupManager.SetStartup) περνάει
            // αυτό το flag — ζητήθηκε ρητά ότι όταν υπάρχουν αποθηκευμένα widgets/DeskContainers, η
            // εφαρμογή πρέπει να ανοίγει στο παρασκήνιο (μόνο tray icon), όχι με το κύριο παράθυρο
            // διαχείρισης να αναδύεται κάθε φορά. Το MainWindow ΔΗΜΙΟΥΡΓΕΙΤΑΙ κανονικά (ώστε να
            // τρέξει η επαναφορά του "Last Session" — widgets/DeskContainers/wallpaper μέσα στον
            // constructor του), απλά δεν καλείται Show() πάνω του.
            bool launchedInBackground = desktopAction != null || Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (launchedInBackground)
            {
                _mainWindow = new MainWindow();
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
                if (!safeMode && desktopAction != null && AppSettings.Load().DesktopMenuEnabled) DesktopMenu.Run(desktopAction);
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

        private static Form CreateStartupIntro()
        {
            var form = new Form
            {
                Text = $"Opsis - Desktop Customization {AppVersion.Label}",
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(545, 300),
                ShowInTaskbar = false, ControlBox = false,
                BackColor = UiTheme.PageBackground
            };
            form.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(18),
                Text = LocalizationManager.T("Welcome.Body"), Font = UiTheme.FontBody, ForeColor = UiTheme.TextPrimary });
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
            var t = new System.Windows.Forms.Timer { Interval = 4000 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); AskPluginApproval(); };
            t.Start();
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
            PluginHost.Instance.ResolvePending(req => MessageBox.Show(owner,
                string.Format(LocalizationManager.T("Plugin.TrustBody"), req.Name, req.Folder, req.Sha256.Substring(0, 16))
                + Environment.NewLine + Environment.NewLine
                + "Publisher / Εκδότης: " + req.Publisher
                + Environment.NewLine + "Signature / Υπογραφή: " + req.SignatureStatus
                + Environment.NewLine + "Requested capabilities / Δηλωμένες δυνατότητες: " + req.Capabilities
                + Environment.NewLine + Environment.NewLine
                + "⚠ In-process plugins are not sandboxed. / Τα DLL plugins εκτελούνται χωρίς sandbox.",
                LocalizationManager.T("Plugin.TrustTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
            UpdatePluginApprovalItem();
        }

        // Privacy boundary for our own Windows toast/balloon messages. Windows and
        // third-party application notifications are outside Opsis' control.
        private void ShowPrivateBalloon(int timeout, string title, string message, ToolTipIcon icon)
        {
            var ids = AppSettings.Peek().PresentationMonitorIds;
            if (ids != null && Screen.AllScreens.Any(screen => ids.Any(id =>
                    id.Equals(screen.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                    id.Equals(MonitorTopologyService.ForDevice(screen.DeviceName)?.Id, StringComparison.OrdinalIgnoreCase))))
                return;
            _trayIcon.ShowBalloonTip(timeout, title, message, icon);
        }

        private void ShowMainWindow(string? page = null)
        {
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new MainWindow();
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

        private void ToggleWallpaper()
        {
            _wallpaperEnabled = !_wallpaperEnabled;
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
        private void UsageAlertTick()
        {
            try
            {
                if (!AppSettings.Load().UsageAlertsEnabled) { _cpuHighTicks = _ramHighTicks = 0; return; }
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
            menu.DropDownItems.Add(T("DCM.OpenSettingsPage"), null, (s, e) => ShowMainWindow("DeskZones"));
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
                menu.DropDownItems.Add(T("DCM.OpenSettingsPage"), null, (_, _) => ShowMainWindow("DeskZones"));
            }
            menu.DropDownOpening += (s, e) => Fill();
            Fill();
            return menu;
        }

        private void PerformExit()
        {
            try { WorkspaceProfileService.Save("Last Session"); WorkspaceProfileService.ExitSaveDone = true; } catch { }
            PluginHost.Instance.StopAll();
            try { MotionDesk.Appearance.AppearanceService.Instance.Stop(); } catch (Exception) { }   // taskbar look and window effects always go back to Windows
            _hotkeys.Dispose();
            _experienceTimer.Stop(); _experienceTimer.Dispose();
            MotionDesk.UI.OpsisCursorFx.Stop();
            WindowRulesEngine.Instance.Dispose();
            _hotCorners.Dispose();
            _taskbarWatcher.Dispose();
            try { _nativeContainers?.Dispose(); } catch { }
            try { StudioMusic.Stop(); } catch { }
            DesktopToolbar.Stop();
            ReplacementTaskbar.Stop();
            VirtualWorkspaces.Stop();
            AutoTileService.Stop();
            try { DeskStripHostEngine.Instance.CloseForExit(); } catch { }
            try { _controlPipe?.Dispose(); } catch { }
            DesktopIconVisibilityEngine.Instance.Stop();
            _trayIcon.Visible = false;
            Application.Exit();
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

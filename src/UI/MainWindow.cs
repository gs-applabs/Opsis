using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MotionDesk.Services;
using MotionDesk.Widgets;
using System.Diagnostics;

namespace MotionDesk.UI
{
    /// <summary>
    /// The real application shell. Feature windows/widgets remain independent,
    /// while this window provides a single navigation point for the desktop manager.
    /// </summary>
    public sealed partial class MainWindow : Form
    {
        private Panel _sidebar = null!;
        private TableLayoutPanel _sidebarLayout = null!;
        private Panel _brandPanel = null!;
        private FlowLayoutPanel _nav = null!;
        private int? _navBaseHeight;
        private PulsingLogoPanel _brandDot = null!;
        private Label _brandLabel = null!;
        private Label _brandSubLabel = null!;
        private Label _brandVersionLabel = null!;
        private SidebarToggleButton _collapseToggle = null!;
        private System.Windows.Forms.Timer? _sidebarAnimTimer;
        private bool _sidebarCollapsed;
        private const int SidebarExpandedWidth = 232;
        private const int SidebarCollapsedWidth = 80;
        private Panel _header = null!;
        private double _headerWaveT;
        private System.Windows.Forms.Timer? _headerWaveTimer;
        private Panel _headerRule = null!;
        private Panel _content = null!;
        private Label _pageTitle = null!;
        // 2.6.0: the shared status bar of Opsis.UI (permanent text + transient message with severity). The old ToolStripStatusLabel API is kept by a thin shim so
        // the ~20 call sites (`_statusLabel.Text = ...`) behave as before: a plain Text is the permanent text, overwritten by the next 5-second system-load update.
        private OpsisStatusBar _status = null!;
        private readonly StatusTextShim _statusLabel = new();
        private readonly ProfileAnnouncementLabel _profileLive = new();

        // Kept in the accessible tree even while visually unobtrusive.
        // WinForms' LiveSetting tells UI Automation readers about changes to Text.
        private sealed class ProfileAnnouncementLabel : Label
        {
            public ProfileAnnouncementLabel()
            {
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = "Active workspace profile / Ενεργό προφίλ";
                LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
                AutoSize = false;
                Size = new Size(1, 1);
                TabStop = false;
            }
            public void Announce(string text)
            {
                Text = text;
                AccessibleDescription = text;
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
                AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }

        private sealed class StatusTextShim
        {
            public OpsisStatusBar? Bar;
            public string Text { get => Bar?.CurrentText ?? ""; set { if (Bar != null) Bar.Status = value ?? ""; } }
            public Color ForeColor { get; set; }
        }

        internal enum StatusKind { Info, Success, Error }

        /// <summary>A message in the status bar for a change the user just made (saved / failed): green or red, back to the normal text after a few seconds. Safe from any thread.</summary>
        internal static void ReportStatus(string text, StatusKind kind = StatusKind.Success)
        {
            foreach (var w in Application.OpenForms.OfType<MainWindow>()) w.ShowStatus(text, kind);
        }

        internal static string SavedText(string label) => string.Format(OpsisStrings.T("ui.saved"), CleanLabel(label));
        internal static string FailedText(string label, string reason) => string.Format(OpsisStrings.T("ui.failed"), CleanLabel(label), reason);
        internal static string CleanLabel(string label) => (label ?? "").Replace("&", "").Trim().TrimEnd(':');

        /// <summary>Runs a settings change and reports it in the status bar: "Saved: ..." or "Failed: ..." (the exception does not escape into the UI thread).</summary>
        internal static void ApplySetting(string label, Action change)
        {
            try { change(); ReportStatus(SavedText(label), StatusKind.Success); }
            catch (Exception ex) { ReportStatus(FailedText(label, ex.Message), StatusKind.Error); RotatingLogger.Write($"Setting '{label}' failed: {ex}"); }
        }

        private void ShowStatus(string text, StatusKind kind)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => ShowStatus(text, kind))); } catch (Exception) { } return; }
            _status.ShowMessage(text, kind switch { StatusKind.Success => StatusSeverity.Success, StatusKind.Error => StatusSeverity.Error, _ => StatusSeverity.Info }, 6000);
        }
        private readonly System.Windows.Forms.Timer _statusTimer;
        private System.Windows.Forms.Timer? _brandPulseTimer;
        private double _brandPulseT;
        private NavButton? _activeButton;
        private readonly System.Collections.Generic.Dictionary<string, NavButton> _navButtons = new();
        private readonly System.Collections.Generic.Dictionary<string, Action> _pageBuilders = new();
        private string _currentPageKey = "Dashboard";
        private readonly HotkeyManager? _hotkeys;
        private readonly bool _hostedByTray;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);

        // ΔΙΟΡΘΩΣΗ "λευκό πλαίσιο παραθύρου εμφανίζεται στιγμιαία πάνω-αριστερά κατά την αλλαγή
        // καρτέλας": το MainWindow δεν είχε ΠΟΤΕ εφαρμόσει DWMWA_BORDER_COLOR="none" (μόνο
        // widgets/DeskContainers το είχαν) — το προεπιλεγμένο λεπτό accent-περίγραμμα των Windows
        // 11 γύρω από κάθε top-level παράθυρο είναι κανονικά αόρατο πίσω από το δικό μας σκούρο
        // θέμα, αλλά σε κάποιο ενδιάμεσο frame κατά το Controls.Clear()+Add() του SetPage (βλ.
        // DoubleBuffered πιο κάτω για ένα σχετικό αλλά διαφορετικό bug) γίνεται στιγμιαία ορατό.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            GlassBlur.DisableNativeBorderAccent(Handle);
            _micaBackdrop = null;
            RefreshMainWindowBackdrop();
        }

        public MainWindow() : this(false) { }

        internal MainWindow(bool hostedByTray)
        {
            _hostedByTray = hostedByTray;
            // Διορθώνει "λευκά τετραγωνάκια" κατά την αλλαγή σκουρότητας θέματος: η πλήρης
            // ανακατασκευή σελίδας/sidebar σε κάθε ThemeManager.Changed (OnThemeOrLanguageChanged)
            // κάνει Controls.Clear()+Add() σε πολλά containers — χωρίς DoubleBuffered στο ίδιο το
            // Form, τα Windows ζωγραφίζουν προσωρινά το προεπιλεγμένο φόντο (λευκό) στις περιοχές
            // που μόλις αδειάσανε πριν προλάβουν να ζωγραφιστούν τα νέα controls από πάνω.
            //
            // ΔΙΟΡΘΩΣΗ πραγματικού regression bug (επιβεβαιώθηκε ζωντανά): το αρχικό fix πρόσθεσε
            // ΚΑΙ ControlStyles.UserPaint — αυτό λέει στα WinForms "θα ζωγραφίζω μόνος μου ΟΛΟ το
            // φόντο", απενεργοποιώντας το κανονικό WM_ERASEBKGND· αφού το MainWindow ΔΕΝ έχει δικό
            // του πλήρες OnPaint που γεμίζει όλη την περιοχή πελάτη, παλιά pixels από προηγούμενο
            // paint pass έμεναν ορατά κάτω από τα νέα — φαινόταν σαν "διπλό", θολό/φαντασματικό
            // κείμενο (π.χ. "Πίνακας Ελέγχου" σε γιγάντια γράμματα πάνω από το κανονικό). Το απλό
            // DoubleBuffered = true (η καθιερωμένη τεχνική για Form, χωρίς UserPaint) διπλασιάζει
            // την απόδοση χωρίς να πειράξει το background-erase pipeline.
            DoubleBuffered = true;
            Flicker.Attach(this);   // κάθε container/παιδί (τωρινό και μελλοντικό) με double buffering
            var appSettings = AppSettings.Load();
            // Release roadmap #94. Show exactly once after a version change, not
            // at every launch; a fresh profile has no previous version, so the
            // welcome guide remains its only introduction.
            string? previousVersion = appSettings.LastSeenAppVersion;
            string currentVersion = Services.AppVersion.Short;
            bool announceUpgrade = !string.IsNullOrWhiteSpace(previousVersion) &&
                !string.Equals(previousVersion, currentVersion, StringComparison.Ordinal);
            if (!announceUpgrade && !string.Equals(previousVersion, currentVersion, StringComparison.Ordinal))
            {
                appSettings.LastSeenAppVersion = currentVersion;
                appSettings.Save();
            }
            if (announceUpgrade)
            {
                Shown += (_, _) => BeginInvoke((Action)(() =>
                {
                    if (IsDisposed) return;
                    string notesKey = "VersionNotes." + currentVersion;
                    string notes = LocalizationManager.T(notesKey);
                    if (notes == notesKey) notes = StudioText("Release notes are available in Help → History.", "Οι αλλαγές βρίσκονται στη Βοήθεια → Ιστορικό.");
                    string intro = StudioText($"Opsis upgraded to {currentVersion}.", $"Το Opsis αναβαθμίστηκε στην {currentVersion}.");
                    MessageBox.Show(this, intro + Environment.NewLine + Environment.NewLine + notes,
                        "Opsis · " + currentVersion, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    // Do not mark the update read during a background-only start.
                    var seen = AppSettings.Load();
                    seen.LastSeenAppVersion = currentVersion;
                    seen.Save();
                }));
            }
            UiTheme.ApplyScale(appSettings.UiScalePercent);
            UiTheme.ApplyCardFontScale(appSettings.CardFontScalePercent);
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            Text = "Opsis " + Services.AppVersion.Label;
            Icon = AppWindowIcon.Value;
            StartPosition = FormStartPosition.CenterScreen;
            // Μεγαλύτερο προεπιλεγμένο μέγεθος — ζητήθηκε ρητά ώστε να χωράει άνετα η νέα σελίδα
            // "Audio Enhancement" (equalizer bands + πολλαπλές ρυθμίσεις + visualizer μαζί).
            MinimumSize = new Size(1040, 680);
            Size = new Size(1280, 800);
            try { Opsis.UI.WindowMemory.Restore(this, appSettings.WindowMemory, MinimumSize); } catch (Exception ex) { RotatingLogger.Write($"WindowMemory.Restore: {ex.Message}"); }
            _opacitySaveTimer.Tick += (_, _) => SaveWindowOpacity();
            ApplyWindowOpacity(appSettings.WindowOpacity);

            _pageBuilders["Dashboard"] = () => SafeBuildPage("Dashboard", ShowDashboard);
            _pageBuilders["Widgets"] = () => SafeBuildPage("Widgets", ShowWidgets);
            _pageBuilders["DeskZones"] = () => SafeBuildPage("DeskZones", ShowDeskZones);
            _pageBuilders["Theme"] = () => SafeBuildPage("Theme", ShowTheme);
            _pageBuilders["DeskContainers"] = () => SafeBuildPage("DeskContainers", ShowDeskContainers);
            _pageBuilders["DeskMusicVisualizer"] = () => SafeBuildPage("DeskMusicVisualizer", ShowDeskMusicVisualizer);
            _pageBuilders["DeskStrip"] = () => SafeBuildPage("DeskStrip", ShowDeskStrip);
            _pageBuilders["Wallpaper"] = () => SafeBuildPage("Wallpaper", ShowWallpaper);
            _pageBuilders["Settings"] = () => SafeBuildPage("Settings", ShowSettings);
            _pageBuilders["Profiles"] = () => SafeBuildPage("Profiles", ShowProfiles);
            _pageBuilders["Performance"] = () => SafeBuildPage("Performance", ShowPerformance);
            _pageBuilders["Automation"] = () => SafeBuildPage("Automation", ShowAutomation);
            _pageBuilders["Personalization"] = () => SafeBuildPage("Personalization", ShowPersonalization);
            _pageBuilders["About"] = () => SafeBuildPage("About", ShowAbout);
            _pageBuilders["AudioEnhancement"] = () => SafeBuildPage("AudioEnhancement", ShowAudioEnhancement);

            _sidebarCollapsed = appSettings.SidebarCollapsed;
            _windowSkin = SelectedSkin(appSettings);
            BuildSelectedChrome();

            _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusTimer.Tick += (_, _) => UpdateStatus();
            _statusTimer.Enabled = Visible && WindowState != FormWindowState.Minimized;
            if (!_hostedByTray)
            {
                _hotkeys = new HotkeyManager();
                RegisterDesktopHotkeys(_hotkeys, ToggleGamingMode, ShowAndActivate, !SafeModeService.IsSafeMode);
            }

            ThemeManager.UiContext = System.Threading.SynchronizationContext.Current;
            AnimationsSettingChanged += UpdateAnimationTimers;
            if (!_hostedByTray) WorkspaceProfileService.ExitSaveDone = false;
            WorkspaceProfileService.ProfileActivated += AnnounceActiveProfile;
            Controls.Add(_profileLive);
            _profileLive.BringToFront();
            ThemeManager.Changed += OnThemeOrLanguageChanged;
            ThemeManager.Repainted += OnThemeRepainted;
            LocalizationManager.Changed += OnThemeOrLanguageChanged;
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

            // Χαμηλού-κόστους hook, τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής — δεν κάνει
            // τίποτα εκτός αν ο χρήστης κρατάει Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine).
            // After an update, show the Version History once (the first time the window is shown with a new version)
            if (!_hostedByTray && !SafeModeService.IsSafeMode)
            {
                ZoneSnapEngine.AllowOwnWindow = h => IsHandleCreated && h == Handle;
                ZoneSnapEngine.Start();
            }

            FormClosed += (_, _) =>
            {
                if (!_hostedByTray) { try { WorkspaceProfileService.SaveLastSessionOnce(); } catch { } }
                AnimationsSettingChanged -= UpdateAnimationTimers;
                WorkspaceProfileService.ProfileActivated -= AnnounceActiveProfile;
                _studioNavTips?.Dispose();
                ThemeManager.Changed -= OnThemeOrLanguageChanged;
                ThemeManager.Repainted -= OnThemeRepainted;
                LocalizationManager.Changed -= OnThemeOrLanguageChanged;
                SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
                if (!_hostedByTray) { ZoneSnapEngine.Stop(); ZoneSnapEngine.AllowOwnWindow = null; }
                _statusTimer.Dispose();
                _opacitySaveTimer.Dispose();
                _brandPulseTimer?.Dispose();
                _sidebarAnimTimer?.Dispose();
                _headerWaveTimer?.Dispose();
                _hotkeys?.Dispose();
            };

            ShowDashboard();
            if (!_hostedByTray) WorkspaceProfileService.RestoreStartupSession();
            // A failed startup profile can offer a known, SHA-256-verified
            // previous installer. It never starts automatically and only after
            // explicit user approval; otherwise the app stays in safe mode.
            Shown += (_, _) => BeginInvoke((Action)(() =>
            {
                if (IsDisposed || !VerifiedRollbackService.TryConsumeOffer(out var previousSetup, out var sha256)) return;
                var choice = MessageBox.Show(this,
                    StudioText("Your last-session profile could not be loaded after the update. Restore the verified previous Opsis version? The existing profile files are kept.",
                        "Το προηγούμενο προφίλ δεν άνοιξε μετά την ενημέρωση. Επαναφορά της επαληθευμένης προηγούμενης έκδοσης Opsis; Τα αρχεία προφίλ θα διατηρηθούν."),
                    StudioText("Version recovery", "Ανάκτηση έκδοσης"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes) return;
                try { UpdateInstallerService.LaunchAfterExit(previousSetup, sha256); TrayApplicationContext.RequestExit(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));
            if (_navButtons.TryGetValue("Dashboard", out var dashboardButton))
                SetActiveButton(dashboardButton);
            UpdateAnimationTimers(); // παράθυρο ακόμη αόρατο (π.χ. --background) → οι animations ξεκινούν μόνο όταν εμφανιστεί
        }

        private void OnWindowsPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) ThemeManager.RefreshFromWindows();
        }

        // ΠΑΝΤΑ μέσω BeginInvoke (όχι μόνο όταν InvokeRequired) — ζητήθηκε bug fix: όταν η
        // εφαρμογή άνοιγε σε Dark και ο χρήστης διάλεγε "Light" από το FlatComboBox της σελίδας
        // Ρυθμίσεων, η εφαρμογή "δεν αποκρινόταν". Αιτία: το SelectedIndexChanged του combo
        // (μέσα στο click handler του δικού του popup ContextMenuStrip) καλούσε συγχρονισμένα
        // ThemeManager.SetMode -> Changed -> εδώ -> πλήρη ανακατασκευή της τρέχουσας σελίδας
        // (dispose ΟΛΩΝ των controls, ΣΥΜΠΕΡΙΛΑΜΒΑΝΟΜΕΝΟΥ του ίδιου του combo) ΕΝΩ το popup ήταν
        // ακόμη στη μέση του δικού του closing/click-handling — κλασικό re-entrancy hazard, ίδιας
        // οικογένειας με το ήδη τεκμηριωμένο "ΟΧΙ using στο ContextMenuStrip" bug pattern αυτού
        // του project. Το BeginInvoke αναβάλλει την ανακατασκευή μέχρι να ξετυλιχτεί πλήρως το
        // τρέχον event πρώτα.
        private void OnThemeOrLanguageChanged()
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                // SuspendLayout/ResumeLayout γύρω από ΟΛΗ την ανακατασκευή (χρώματα + sidebar +
                // τρέχουσα σελίδα) — χωρίς αυτό, κάθε ενδιάμεσο Controls.Clear()/Add() προκαλούσε
                // δικό του layout+repaint pass, ορατό ως στιγμιαία "λευκά τετραγωνάκια" στο σημείο
                // που μόλις άδειασε πριν γεμίσει ξανά.
                Flicker.BatchRedraw(this, () =>
                {
                    SuspendLayout();
                    try
                    {
                        ApplyChromeColors();
                        RebuildSidebarNav();
                        if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                    }
                    finally { ResumeLayout(true); }
                });
            }));
        }

        // Φτηνιά ζωντανή προεπισκόπηση ενώ σέρνεις το slider σκουρότητας — καμία ανακατασκευή
        // control tree (αυτό παραμένει αποκλειστικά στο MouseUp μέσω Changed/OnThemeOrLanguageChanged).
        //
        // Bug fix: πριν, εδώ γινόταν ΜΟΝΟ Invalidate(true) — αυτό ενημερώνει ζωντανά μόνο τα
        // owner-draw controls (NavButton/StatCard/PulsingLogoPanel κ.λπ., που διαβάζουν το
        // UiTheme.* ζωντανά μέσα στο δικό τους OnPaint), ενώ τα απλά Panel (_content/_header/
        // _status κ.λπ.) ζωγραφίζουν με το ΗΔΗ αποθηκευμένο BackColor property τους — που ΔΕΝ
        // αλλάζει μόνο του από ένα Invalidate. Αυτό ακριβώς εξηγούσε το αναφερόμενο σύμπτωμα "η
        // σκουρότητα εφαρμόζεται μόνο στο πλευρικό μενού και στην κορυφή (τα owner-draw κομμάτια),
        // όχι σε όλο το παράθυρο" όσο ο χρήστης έσερνε το slider. Το ApplyChromeColors() είναι
        // φτηνό (μόνο αναθέσεις χρωμάτων σε ήδη υπάρχοντα controls, καμία Controls.Clear()/Add()),
        // οπότε ασφαλές να τρέχει σε κάθε tick χωρίς να επαναφέρει το αρχικό flicker bug.
        private void OnThemeRepainted()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeRepainted)); return; }
            ApplyChromeColors();
            Invalidate(true);
        }

        // 1.7.8 - διπλό buffering για container controls (Panel/TableLayoutPanel/FlowLayoutPanel) που δεν το
        // εκθέτουν δημόσια: χωρίς αυτό το animation πλάτους του sidebar άφηνε "κυματισμούς" από παλιά καρέ.
        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(c, true);
            }
            catch { }
        }

        private void BuildChrome()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = SidebarExpandedWidth, Padding = new Padding(16, 20, 16, 12) };
            EnableDoubleBuffer(_sidebar);

            // TableLayoutPanel αντί για Dock=Top (brand) + Dock=Fill (nav) απευθείας μέσα στο
            // padded _sidebar: σε αυτόν τον συνδυασμό το Fill rectangle του _nav υπολογιζόταν
            // λανθασμένα σαν να μην υπήρχε καθόλου δεσμευμένος χώρος από το _brandPanel (bug
            // που ΔΕΝ διορθωνόταν ούτε με ρητό PerformLayout/Invalidate μετά το Shown), με
            // αποτέλεσμα το _nav να ξεκινάει επικαλυπτόμενο με το brand και να κρύβει εντελώς
            // το πρώτο κουμπί πλοήγησης (Dashboard). Οι σταθερές δύο σειρές ενός
            // TableLayoutPanel δεν έχουν αυτή την ασάφεια.
            _sidebarLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            EnableDoubleBuffer(_sidebarLayout);
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _sidebar.Controls.Add(_sidebarLayout);

            _brandPanel = new Panel { Dock = DockStyle.Fill };
            // Canonical Opsis 3 brand, fixed proportions at every sidebar width.
            const int logoSize = 56;
            _brandDot = new PulsingLogoPanel { Size = new Size(logoSize, logoSize), Location = new Point(0, 2) };
            _brandDot.Paint += (_, e) =>
            {
                int size = Math.Max(1, Math.Min(_brandDot.Width, _brandDot.Height) - 4);
                e.Graphics.DrawIcon(AppWindowIcon.Value, new Rectangle((_brandDot.Width - size) / 2,
                    (_brandDot.Height - size) / 2, size, size));
            };
            _brandPanel.Controls.Add(_brandDot);
            const int brandTextX = logoSize + 10;
            int brandLabelWidth = SidebarExpandedWidth - brandTextX - _sidebar.Padding.Horizontal;
            _brandLabel = new Label { AutoSize = true, Location = new Point(brandTextX, 2), Text = "Opsis", Font = new Font("Segoe UI", 16f, FontStyle.Bold), AutoEllipsis = true };
            _brandPanel.Controls.Add(_brandLabel);
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // Ζητήθηκε ρητά "εφόσον στον τίτλο η έκδοση δεν φαίνεται, βάλε την σε τρίτη γραμμή από
            // κάτω" — στο προηγούμενο πέρασμα η έκδοση ήταν στην ΙΔΙΑ γραμμή με το tagline
            // ("Desktop Customization  ·  vX.X.X"), και το AutoEllipsis έκοβε το κείμενο πριν καν
            // φτάσει στην έκδοση (το σύνολο δεν χωρούσε στα ~142px). Τώρα 3 ξεχωριστές γραμμές:
            // τίτλος / tagline / έκδοση — η καθεμία χωράει πλέον άνετα μόνη της.
            _brandSubLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(brandTextX, 30),
                Size = new Size(brandLabelWidth, 16),
                Text = "Desktop Customization",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _brandPanel.Controls.Add(_brandSubLabel);
            _brandVersionLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(brandTextX, 46),
                Size = new Size(brandLabelWidth, 16),
                Text = "v" + Services.AppVersion.Label,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _brandPanel.Controls.Add(_brandVersionLabel);
            _sidebarLayout.Controls.Add(_brandPanel, 0, 0);

            // ΟΧΙ AutoScroll: μια κάθετη scrollbar (όταν τα 9 nav items δεν χωρούν καθ' ύψος)
            // "έτρωγε" πλάτος από το ήδη στενό, συμπτυγμένο μενού (64px), προκαλώντας ΚΑΙ
            // οριζόντια scrollbar σε καταρράκτη — αφού τα τετράγωνα πλακίδια είχαν υπολογιστεί
            // να γεμίζουν ΑΚΡΙΒΩΣ το διαθέσιμο πλάτος, χωρίς περιθώριο. Ζητήθηκε ρητά καμία
            // scrollbar· καλύτερα να κόβεται καθαρά ένα τελευταίο στοιχείο παρά να εμφανίζεται.
            _nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 14, 0, 0) };
            EnableDoubleBuffer(_nav);
            _sidebarLayout.Controls.Add(_nav, 0, 1);
            RebuildSidebarNav();

            _collapseToggle = new SidebarToggleButton();
            _collapseToggle.Click += (_, _) => ToggleSidebarCollapsed();
            _sidebarLayout.Controls.Add(_collapseToggle, 0, 2);

            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 24, 32, 24) };
            EnableDoubleBuffer(_content);

            // Απαλό, κινούμενο κύμα-φόντο πίσω από τον τίτλο κάθε σελίδας (ζητήθηκε ρητά) — ίδια
            // "γλώσσα" με το wallpaper/λογότυπο, αλλά πολύ χαμηλής έντασης ώστε να μην αποσπά.
            _header = new HeaderWavePanel(() => _headerWaveT) { Dock = DockStyle.Top, Height = 64 };
            _pageTitle = new Label { Dock = DockStyle.Fill, Text = "Dashboard", Font = UiTheme.FontHeading, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _header.Controls.Add(_pageTitle);
            _headerRule = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            _header.Controls.Add(_headerRule);
            AttachUniversalSearch();
            _headerWaveTimer = new System.Windows.Forms.Timer { Interval = HardwareProfileService.ScaleDecorativeInterval(45) };
            _headerWaveTimer.Tick += (_, _) => { _headerWaveT += 0.03; _header.Invalidate(); };
            _headerWaveTimer.Start();

            _status = new OpsisStatusBar { Dock = DockStyle.Bottom, Status = "Opsis ready" };
            _statusLabel.Bar = _status;

            Controls.Add(_content);
            Controls.Add(_header);
            Controls.Add(_sidebar);
            Controls.Add(_status);
        }

        // Οι διακοσμητικές animations (παλμός λογότυπου, κύμα header) τρέχαν συνεχώς σε 20Hz/16Hz
        // ακόμη κι όταν το παράθυρο ήταν κρυμμένο στο tray ή ελαχιστοποιημένο — άσκοπη κατανάλωση
        // CPU/GPU για μια εφαρμογή που ζει κυρίως στο παρασκήνιο. Τρέχουν μόνο όταν φαίνονται.
        // Καλείται και από τη ρύθμιση Animations ώστε η αλλαγή να ισχύει αμέσως.
        internal static event Action? AnimationsSettingChanged;

        private void RunWhilePageVisible(Control page, System.Windows.Forms.Timer timer, Action<bool>? activity = null)
        {
            void Update(object? sender, EventArgs args)
            {
                bool enabled = !Disposing && !IsDisposed && Visible && WindowState != FormWindowState.Minimized && page.Visible && page.Parent != null;
                timer.Enabled = enabled;
                activity?.Invoke(enabled);
            }
            VisibleChanged += Update;
            SizeChanged += Update;
            page.VisibleChanged += Update;
            page.ParentChanged += Update;
            page.Disposed += (_, _) =>
            {
                VisibleChanged -= Update;
                SizeChanged -= Update;
                page.VisibleChanged -= Update;
                page.ParentChanged -= Update;
            };
            Update(null, EventArgs.Empty);
        }

        private void UpdateAnimationTimers()
        {
            bool shown = Visible && WindowState != FormWindowState.Minimized;
            if (_statusTimer != null) _statusTimer.Enabled = shown;
            bool visible = shown && AppSettings.Peek().EnableAnimations;
            if (_brandPulseTimer != null) _brandPulseTimer.Stop(); // static canonical logo
            if (_headerWaveTimer != null) _headerWaveTimer.Enabled = visible;
        }

        // ΔΙΟΡΘΩΣΗ πραγματικού bug: το κλείσιμο (X) του κύριου παραθύρου το ΚΑΤΕΣΤΡΕΦΕ — και το
        // FormClosed handler σταματούσε ZoneSnapEngine + όλα τα global hotkeys (Ctrl+Alt+G/M/Shift+βέλη)
        // ενώ η εφαρμογή συνέχιζε να ζει στο tray (DeskZones/συντομεύσεις "έσβηναν" σιωπηλά), και το
        // επόμενο άνοιγμα ξανάτρεχε όλον τον constructor (επαναφορά "Last Session" ξανά). Η ρύθμιση
        // MinimizeToTray (προεπιλογή true) υπήρχε αλλά ΔΕΝ χρησιμοποιούνταν πουθενά. Τώρα το X κρύβει
        // στο tray· η πραγματική έξοδος γίνεται μόνο από το "Έξοδος" του tray menu (CloseReason != UserClosing).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveWindowOpacity();
            if (e.CloseReason == CloseReason.UserClosing && AppSettings.Load().MinimizeToTray)
            {
                e.Cancel = true;
                // Φεύγουμε από βαριές σελίδες (live equalizer: WASAPI capture + timer 25Hz) ώστε να μην
                // τρέχουν αόρατες στο παρασκήνιο.
                if (_currentPageKey is "AudioEnhancement" or "Performance") NavigateTo("Dashboard");
                SaveWindowPlacement();
                Hide();
                return;
            }
            // Πραγματικό κλείσιμο (έξοδος/shutdown): αποθήκευση ΤΩΡΑ, όσο τα widgets ζουν ακόμα.
            if (!_hostedByTray || e.CloseReason != CloseReason.UserClosing)
            {
                try { WorkspaceProfileService.SaveLastSessionOnce(); } catch { }
            }
            SaveWindowPlacement();
            base.OnFormClosing(e);
        }

        /// <summary>2.6.0: remembers where the window was, per screen (restored on the next start; a removed screen falls back to the primary one).</summary>
        private void SaveWindowPlacement()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) return;
                var s = AppSettings.Load();
                Opsis.UI.WindowMemory.Capture(this, s.WindowMemory);
                s.Save();
            }
            catch (Exception ex) { RotatingLogger.Write($"WindowMemory.Capture: {ex.Message}"); }
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimationTimers(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateAnimationTimers(); }

        // Controls.Clear() ΜΟΝΟ αφαιρεί — δεν κάνει Dispose. Οι σελίδες Widgets/DeskZones/DeskStrip ξαναχτίζουν
        // τις λίστες τους κάθε 2s, άρα πριν διέρρεαν panels/labels/buttons/Regions συνεχώς όσο ήταν ανοιχτή η σελίδα.
        private static void DisposeChildren(Control parent)
        {
            foreach (Control child in parent.Controls.Cast<Control>().ToList()) child.Dispose();
            parent.Controls.Clear();
        }

        private void RebuildSidebarNav()
        {
            DisposeChildren(_nav);
            _navButtons.Clear();
            _activeButton = null;
            AddNavButton(_nav, LocalizationManager.T("Nav.Dashboard"), "Dashboard", ShowDashboard, 1);
            AddNavButton(_nav, OpsisStrings.T("ui.theme.title"), "Theme", ShowTheme, (string?)null);
            AddNavButton(_nav, LocalizationManager.T("Nav.Personalization"), "Personalization", ShowPersonalization, 0);
            AddNavButton(_nav, LocalizationManager.T("Nav.Wallpaper"), "Wallpaper", ShowWallpaper, 4);
            AddNavButton(_nav, LocalizationManager.T("Nav.Widgets"), "Widgets", ShowWidgets, 2);
            AddNavButton(_nav, LocalizationManager.T("Nav.DeskZones"), "DeskZones", ShowDeskZones, 3);
            AddNavButton(_nav, LocalizationManager.T("DeskZones.SectionContainers"), "DeskContainers", ShowDeskContainers, (string?)null);
            AddNavButton(_nav, LocalizationManager.T("Personalization.SectionDeskStrip"), "DeskStrip", ShowDeskStrip, (string?)null);
            AddNavButton(_nav, "Desk Music Visualizer", "DeskMusicVisualizer", ShowDeskMusicVisualizer, (string?)null);
            if (Services.MediaSuiteDetector.IsInstalled())
                AddNavButton(_nav, LocalizationManager.T("Nav.AudioEnhancement"), "AudioEnhancement", ShowAudioEnhancement, (string?)null);
            AddNavButton(_nav, LocalizationManager.T("Nav.Performance"), "Performance", ShowPerformance, 5);
            AddNavButton(_nav, LocalizationManager.T("Nav.Automation"), "Automation", ShowAutomation, 8);
            AddNavButton(_nav, LocalizationManager.T("Nav.Settings"), "Settings", ShowSettings, 6);
            // Σελίδες των plugin (π.χ. News Crawler) — μπαίνουν πριν το "Σχετικά" που μένει τελευταίο.
            foreach (var pluginItem in Services.PluginHost.Instance.GetNavigationItems())
            {
                var captured = pluginItem;
                AddNavButton(_nav, Services.PluginHost.TitleOf(captured), "plugin:" + captured.Key, () => ShowPluginPage(captured), (string?)null);
            }
            // "Σχετικά" ζητήθηκε ρητά να είναι το ΤΕΛΕΥΤΑΙΟ στοιχείο του nav, κάτω από τη
            // "Διαχείριση Ήχου" (πριν ήταν ανάποδα).
            AddNavButton(_nav, LocalizationManager.T("Nav.About"), "About", ShowAbout, 9);

            if (_usesStudio)
            {
                RebuildStudioNavigation();
                ColorStudioChrome();
            }
            else if (_sidebarCollapsed) foreach (var btn in _navButtons.Values) btn.Collapsed = true;
            if (_navButtons.TryGetValue(_currentPageKey, out var active)) SetActiveButton(active);
            EnsureSidebarFits();
        }

        // Plugins add pages to the side menu: the window grows (up to the screen) so the whole menu stays visible; if the screen is too small the menu scrolls.
        private void EnsureSidebarFits()
        {
            if (_usesStudio || !IsHandleCreated) return;
            void Fit()
            {
                if (_usesStudio || IsDisposed || WindowState != FormWindowState.Normal) return;
                PerformLayout();                       // the sizes below are only right once the layout of the sidebar has run
                var buttons = _nav.Controls.OfType<NavButton>().ToList();
                if (buttons.Count > 0)
                {
                    _navBaseHeight ??= buttons[0].Height;
                    foreach (var b in buttons) b.Height = _navBaseHeight.Value;   // start from the normal size every time
                }
                int need = _nav.Padding.Vertical + _nav.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                int missing = need - _nav.ClientSize.Height;
                if (missing > 0)
                {
                    var wa = Screen.FromControl(this).WorkingArea;
                    int newHeight = Math.Min(Height + missing + 8, wa.Height);
                    if (newHeight > Height)
                    {
                        Height = newHeight;
                        if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
                    }
                }
                // Still not enough room (a small screen, many plugins): make the entries a little lower so that EVERY entry is visible.
                if (need > _nav.ClientSize.Height + 1 && buttons.Count > 0)
                {
                    int per = (_nav.ClientSize.Height - _nav.Padding.Vertical - (_nav.Controls.Count - buttons.Count) * 24) / buttons.Count;
                    foreach (var b in buttons) b.Height = Math.Clamp(per - b.Margin.Vertical, 28, _navBaseHeight ?? 40);
                    need = _nav.Padding.Vertical + _nav.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                }
                _nav.AutoScroll = need > _nav.ClientSize.Height + 1;
            }
            // Twice: once now, and once after the window has settled (with BOTH plugins - News Crawler and Appearance - the first pass ran before the
            // final layout, so the last tabs stayed hidden below the visible area).
            BeginInvoke(new Action(Fit));
            var again = new System.Windows.Forms.Timer { Interval = 600 };
            again.Tick += (_, _) => { again.Stop(); again.Dispose(); Fit(); };
            again.Start();
        }

        private void ApplyChromeColors()
        {
            RefreshSkinPalette();
            BackColor = CurrentSkinColors.Background;
            ForeColor = CurrentSkinColors.Text;
            if (!_usesStudio)
            {
                _sidebar.BackColor = UiTheme.NavBackground;
                _sidebarLayout.BackColor = UiTheme.NavBackground;
                _brandPanel.BackColor = UiTheme.NavBackground;
                _brandDot.BackColor = UiTheme.NavBackground;
                _brandLabel.ForeColor = UiTheme.TextPrimary;
                _brandSubLabel.ForeColor = UiTheme.TextMuted;
                _brandVersionLabel.ForeColor = UiTheme.TextMuted;
            }
            _content.BackColor = UiTheme.PageBackground;
            _header.BackColor = UiTheme.PageBackground;
            _headerRule.BackColor = UiTheme.Border;
            _pageTitle.ForeColor = UiTheme.TextPrimary;
            _status.BackColor = UiTheme.NavBackground;
            _statusLabel.ForeColor = UiTheme.TextSecondary;
            if (_usesStudio) ColorStudioChrome();
            ColorUniversalSearch();

            int useDark = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0;
            try { if (IsHandleCreated) DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)); }
            catch (DllNotFoundException) { }

            if (IsHandleCreated)
            {
                RefreshMainWindowBackdrop();
                GlassBlur.ApplyRoundedCorners(Handle);
            }

            Invalidate(true);
        }

        private NavButton AddNavButton(Control parent, string text, string key, Action action, int shortcutIndex) =>
            AddNavButton(parent, text, key, action, $"Ctrl+{shortcutIndex}");

        private NavButton AddNavButton(Control parent, string text, string key, Action action, string? shortcutLabel)
        {
            var button = new NavButton(key, text, _usesStudio ? null : shortcutLabel) { Width = _usesStudio ? 164 : 184 };
            button.Click += (_, _) =>
            {
                _currentPageKey = key;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Control? newPage = null;
                void FirstPaint(object? sender, PaintEventArgs e)
                {
                    if (newPage != null) newPage.Paint -= FirstPaint;
                    elapsed.Stop();
                    UiResourceAudit.Mark("page-first-paint", key + ":" + elapsed.ElapsedMilliseconds);
                    if (elapsed.ElapsedMilliseconds > 3000)
                        RotatingLogger.Write($"Slow page first paint: {key}, {elapsed.ElapsedMilliseconds} ms");
                }
                void NewControl(object? sender, ControlEventArgs e)
                {
                    _content.ControlAdded -= NewControl;
                    newPage = e.Control;
                    if (newPage != null) newPage.Paint += FirstPaint;
                }
                _content.ControlAdded += NewControl;
                try { SafeBuildPage(key, action); }
                finally
                {
                    // If the builder reused content and did not add a control, avoid a lingering handler.
                    _content.ControlAdded -= NewControl;
                }
            };
            button.Click += (_, _) => SetActiveButton(button);
            button.Click += (_, _) => Services.UiSounds.PlayClick();
            parent.Controls.Add(button);
            _navButtons[key] = button;
            return button;
        }

        /// <summary>Επιτρέπει σε εξωτερικό caller (π.χ. το tray context menu) να ανοίξει το
        /// κύριο παράθυρο κατευθείαν σε συγκεκριμένη σελίδα αντί για το Dashboard.</summary>
        /// <summary>The keys of the pages in the side menu (plugin pages included), for the click audit.</summary>
        public IReadOnlyList<string> PageKeys => _navButtons.Keys.ToList();

        internal double? MeasurePagePaint(string key, Action<int> pump)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            double? firstPaint = null;
            Control? page = null;
            void Painted(object? sender, PaintEventArgs args) => firstPaint ??= elapsed.Elapsed.TotalMilliseconds;
            void Added(object? sender, ControlEventArgs args)
            {
                if (page != null) page.Paint -= Painted;
                page = args.Control;
                if (page != null) page.Paint += Painted;
            }
            _content.ControlAdded += Added;
            try
            {
                NavigateTo(key);
                while (firstPaint == null && elapsed.ElapsedMilliseconds < 1000 && !IsDisposed) pump(10);
                return firstPaint;
            }
            finally
            {
                _content.ControlAdded -= Added;
                if (page != null) page.Paint -= Painted;
            }
        }

        public void NavigateTo(string pageKey)
        {
            if (pageKey == "Profiles") pageKey = "Automation";
            if (_navButtons.TryGetValue(pageKey, out var button))
                button.PerformClick();
        }

        /// <summary>Notify screen readers about a successfully restored workspace.</summary>
        private void AnnounceActiveProfile(string profile)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => AnnounceActiveProfile(profile))); return; }
            string announcement = StudioText("Active profile: ", "Ενεργό προφίλ: ") + profile;
            AccessibleDescription = announcement;
            _profileLive.Announce(announcement);
            AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            ShowStatus(announcement, StatusKind.Info);
        }

        private void SetActiveButton(NavButton button)
        {
            if (_activeButton != null)
            {
                _activeButton.IsActive = false;
                _activeButton.Invalidate();
            }

            _activeButton = button;
            if (_usesStudio) UpdateStudioNavigation();
            _activeButton.IsActive = true;
            _activeButton.Invalidate();
        }

        // Απλό double-buffered Panel για το owner-draw λογότυπο — χωρίς αυτό, οι συχνές
        // Invalidate() κλήσεις του animation timer προκαλούσαν εμφανές flicker.
        private sealed class PulsingLogoPanel : Panel
        {
            public PulsingLogoPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        // Header background: 3 πολύ χαμηλής έντασης κυματιστές γραμμές που ταξιδεύουν αργά,
        // πίσω από τον τίτλο κάθε σελίδας — ζητήθηκε ρητά "κινούμενο φόντο με κύματα".
        private sealed class HeaderWavePanel : Panel
        {
            private readonly Func<double> _getT;
            public HeaderWavePanel(Func<double> getT)
            {
                _getT = getT;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(UiTheme.PageBackground); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                double t = _getT();
                var colors = new[] { UiTheme.AccentCyan, UiTheme.AccentBlue, UiTheme.AccentCyan };
                for (int i = 0; i < 3; i++)
                {
                    double phase = t * (0.5 + i * 0.15) + i * 1.7;
                    double amp = 6 + i * 3;
                    double baseline = Height * (0.35 + i * 0.28);
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= Width; x += 16)
                        pts.Add(new PointF(x, (float)(baseline + Math.Sin(phase + x * 0.012) * amp)));
                    if (pts.Count < 2) continue;
                    using var pen = new Pen(Color.FromArgb(HeaderWaveStyle.Alpha(UiTheme.PageBackground, colors[i], i), colors[i]), 2f) { LineJoin = LineJoin.Round };
                    g.DrawLines(pen, pts.ToArray());
                }
            }
        }

        // Owner-draw nav item: εικονίδιο (Segoe Fluent Icons) + ετικέτα, με accent bar και
        // hover/active state. Βασισμένο σε Panel (όχι Button) — ένα ButtonBase-specific quirk
        // (η αυτόματη "default button" ειδοποίηση του Form προς το πρώτο Button ενός container)
        // εμπόδιζε ολοκληρωτικά το πρώτο owner-draw Button του sidebar να ζωγραφιστεί ποτέ.
        private sealed class NavButton : Panel
        {
            private readonly string _iconKey;
            private bool _hover;
            private bool _collapsed;
            public bool IsActive { get; set; }
            public new event EventHandler? Click;

            // Σε "συμπτυγμένη" λειτουργία δείχνει ΜΟΝΟ το (μεγαλύτερο, κεντραρισμένο) εικονίδιο —
            // ζητήθηκε ρητά: το πλευρικό μενού να μπορεί να συμπτύσσεται με μεγαλύτερα εικονίδια.
            public bool Collapsed
            {
                get => _collapsed;
                set
                {
                    if (_collapsed == value) return;
                    _collapsed = value;
                    // 56 (όχι ίσο με το πλήρες πλάτος του container, 64) — σκόπιμο περιθώριο
                    // ασφαλείας ώστε τίποτα να μην ακουμπάει/κόβεται στο δεξί άκρο, ό,τι κι αν
                    // προκαλούσε το προηγούμενο ζήτημα ("κόβονται τα εικονίδια").
                    if (!SuppressWidth) ApplyWidth();
                    Invalidate();
                }
            }

            // 1.7.8 - κατά το animation του sidebar το πλάτος του κάθε κουμπιού ΑΚΟΛΟΥΘΕΙ το πλάτος του sidebar
            // (ορίζεται από το MainWindow ανά καρέ) αντί να πηδά αμέσως 56 <-> 184 - αυτό προκαλούσε τα "κύματα".
            public bool SuppressWidth { get; set; }
            public void ApplyWidth() => Width = _collapsed ? 56 : 184;

            private readonly ToolTip _tip = new() { InitialDelay = 300, AutoPopDelay = 4000 };
            private readonly string? _shortcutHint;

            // Tooltip με τη συντόμευση πληκτρολογίου (π.χ. "Widgets (Ctrl+2)") — πάντα ορατό,
            // όχι μόνο στη συμπτυγμένη λειτουργία, ώστε οι συντομεύσεις να είναι ανακαλύψιμες
            // μέσα στο ίδιο το UI (ζητήθηκε ρητά στο roadmap).
            public NavButton(string iconKey, string text, string? shortcutHint = null)
            {
                _iconKey = iconKey;
                Text = text;
                _shortcutHint = shortcutHint;
                Height = 42;
                Margin = new Padding(0, 0, 0, 4);
                TabStop = false;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
                Disposed += (_, _) => _tip.Dispose();
                if (_shortcutHint != null) _tip.SetToolTip(this, $"{Text} ({_shortcutHint})");
            }

            public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var iconColor = IsActive ? UiTheme.AccentCyan : UiTheme.TextSecondary;

                // Συμπτυγμένη λειτουργία: ξεχωριστό, τετράγωνο πλακίδιο-κουμπί (όχι το ίδιο
                // επίμηκες pill του πλήρους sidebar) με μεγαλύτερο εικονίδιο — ζητήθηκε ρητά
                // "τετράγωνα κουμπιά" + μεγαλύτερα εικονίδια στη σύμπτυξη.
                if (_collapsed)
                {
                    // Το πλακίδιο ήταν 42 px μέσα σε κουμπί ύψους 42 px: το κάτω περίγραμμα κοβόταν και το πάνω έβγαινε παχύτερο.
                    // Τώρα 40 px με 1 px περιθώριο, και το περίγραμμα πέφτει ακριβώς πάνω σε pixel (offset 0,5), ίδιο πάχος σε όλες τις πλευρές.
                    const int tile = 40;
                    var tileRect = new Rectangle((Width - tile) / 2, (Height - tile) / 2, tile - 1, tile - 1);
                    var bg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.NavBackground);
                    g.TranslateTransform(0.5f, 0.5f);
                    using (var bgBrush = new SolidBrush(bg))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.FillPath(bgBrush, path);
                    using (var borderPen = new Pen(IsActive ? UiTheme.AccentCyan : UiTheme.Border, IsActive ? 1.6f : 1f))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.DrawPath(borderPen, path);
                    g.ResetTransform();

                    const int iconSize = 26;
                    IconRenderer.Draw(g, _iconKey, new RectangleF(tileRect.X + (tile - iconSize) / 2f, tileRect.Y + (tile - iconSize) / 2f, iconSize, iconSize), iconColor);
                    return;
                }

                var expandedBg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.NavBackground);
                using (var bgBrush = new SolidBrush(expandedBg))
                using (var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8))
                    g.FillPath(bgBrush, path);

                if (IsActive)
                {
                    using var accentBrush = new SolidBrush(UiTheme.AccentCyan);
                    g.FillRectangle(accentBrush, 0, 8, 3, Height - 16);
                }

                IconRenderer.Draw(g, _iconKey, new RectangleF(14, (Height - 20) / 2f, 20, 20), iconColor);

                var textColor = IsActive ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                using var textFont = new Font("Segoe UI", 9.5f, IsActive ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, Text, textFont, new Rectangle(42, 0, Math.Max(1, Width - 50), Height), textColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        // Μικρό κουμπί σύμπτυξης/επέκτασης στο κάτω μέρος του sidebar — το βέλος αλλάζει
        // κατεύθυνση ανάλογα με την τρέχουσα κατάσταση.
        private sealed class SidebarToggleButton : Panel
        {
            private bool _hover;
            private bool _collapsed;
            public new event EventHandler? Click;

            public bool Collapsed { get => _collapsed; set { _collapsed = value; Invalidate(); } }

            public SidebarToggleButton()
            {
                Dock = DockStyle.Fill;
                Cursor = Cursors.Hand;
                // ResizeRedraw: το κουμπί ξαναζωγραφίζεται σε ΚΑΘΕ αλλαγή πλάτους (animation) ώστε το βέλος να μένει
                // κεντραρισμένο και να μη μένουν υπολείμματα ("κυματισμοί") από παλιά καρέ.
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_hover)
                {
                    using var bg = new SolidBrush(UiTheme.Surface);
                    using var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8);
                    g.FillPath(bg, path);
                }
                using var pen = new Pen(UiTheme.TextSecondary, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = Width / 2f, cy = Height / 2f;
                float dir = _collapsed ? 1 : -1;
                g.DrawLines(pen, new[] { new PointF(cx + 3 * dir, cy - 5), new PointF(cx - 3 * dir, cy), new PointF(cx + 3 * dir, cy + 5) });
            }
        }

        // Ζωντανή animation (ease-out) για το πλάτος του sidebar κατά τη σύμπτυξη/επέκταση —
        // ζητήθηκε ρητά "εφέ κύλισης" αντί για στιγμιαία αλλαγή μεγέθους.
        private void ToggleSidebarCollapsed()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            var appSettings = AppSettings.Load();
            appSettings.SidebarCollapsed = _sidebarCollapsed;
            appSettings.Save();

            // Η σειρά έχει σημασία (πριν όχι — αυτό έδινε ένα "άκυρο" απότομο snap): όταν
            // ΣΥΜΠΤΥΣΣΟΥΜΕ, πρώτα ζωγραφίζουμε πλήρες μενού που απλά στενεύει/κόβεται καθώς
            // τρέχει το animation, και ΜΟΝΟ όταν φτάσει στο τελικό στενό πλάτος αλλάζουμε σε
            // τετράγωνα πλακίδια/μεγάλα εικονίδια. Όταν ΕΠΕΚΤΕΙΝΟΥΜΕ, κάνουμε το αντίστροφο:
            // πρώτα ετοιμάζουμε την πλήρη διάταξη, μετά φαρδαίνει το sidebar να την αποκαλύψει.
            if (_sidebarCollapsed)
                AnimateSidebarWidth(SidebarCollapsedWidth, () => ApplySidebarCollapsedVisuals(true));
            else
            {
                ApplySidebarCollapsedVisuals(false);
                _collapseToggle.Collapsed = true; // το βέλος αλλάζει κατεύθυνση ΜΟΝΟ όταν τελειώσει η επέκταση
                AnimateSidebarWidth(SidebarExpandedWidth, () => _collapseToggle.Collapsed = false);
            }
        }

        // Με πλάτος sidebar 64px στη συμπτυγμένη λειτουργία και το ΙΔΙΟ Padding(16,...,16,...)
        // του πλήρους sidebar, το χρησιμοποιήσιμο πλάτος έπεφτε στα 32px — μικρότερο από το
        // λογότυπο (48px) και τα τετράγωνα πλακίδια των κουμπιών (48px), δηλαδή "δεν χωρούσαν".
        // Εδώ μειώνουμε το πλευρικό padding ΚΑΙ ξαναμεγεθύνουμε/κεντράρουμε το λογότυπο ώστε να
        // χωράει σωστά στο πραγματικά διαθέσιμο πλάτος.
        private void ApplySidebarCollapsedVisuals(bool collapsed)
        {
            _sidebar.Padding = collapsed ? new Padding(8, 20, 8, 12) : new Padding(16, 20, 16, 12);

            // Ζητήθηκε ρητά "όταν το πλευρικό μενού γίνεται μόνο εικονίδια να μην μικραίνει το
            // εικονίδιο της εφαρμογής & να είναι aligned με εκείνα του πλευρικού μενού" — πριν
            // μίκραινε (48->40) και κεντραριζόταν ανεξάρτητα μέσα στο usableWidth, όχι στο ίδιο
            // σημείο με τα τετράγωνα πλακίδια (42px) των NavButton, που κεντράρονται μέσα στο δικό
            // τους πλάτος 56px (δηλ. κέντρο στο x=28 μέσα στο padded περιεχόμενο). Το λογότυπο
            // μένει πλέον στο ΙΔΙΟ μέγεθος (48) και στο ίδιο κέντρο (x=28) με τα nav εικονίδια.
            const int logoSizeFixed = 48;
            const int navTileCenterX = 28;
            if (collapsed)
            {
                _brandDot.Size = new Size(logoSizeFixed, logoSizeFixed);
                _brandDot.Location = new Point(navTileCenterX - logoSizeFixed / 2, 6);
            }
            else
            {
                _brandDot.Size = new Size(logoSizeFixed, logoSizeFixed);
                _brandDot.Location = new Point(0, 2);
            }

            _collapseToggle.Collapsed = collapsed;
            _brandLabel.Visible = !collapsed;
            _brandSubLabel.Visible = !collapsed;
            _brandVersionLabel.Visible = !collapsed;
            foreach (var btn in _navButtons.Values) { btn.Collapsed = collapsed; btn.Refresh(); }
            _nav.Refresh();
        }

        private void AnimateSidebarWidth(int targetWidth, Action? onComplete = null)
        {
            // Η ρύθμιση "Animations" (Ρυθμίσεις → Εκκίνηση) δεν είχε καμία επίδραση — τώρα την τηρούμε.
            if (!AppSettings.Load().EnableAnimations)
            {
                _sidebar.Width = targetWidth;
                foreach (var btn in _navButtons.Values) { btn.SuppressWidth = false; btn.ApplyWidth(); }
                onComplete?.Invoke();
                return;
            }
            _sidebarAnimTimer?.Stop();
            _sidebarAnimTimer?.Dispose();
            int startWidth = _sidebar.Width;
            // 1.7.8 - animation βασισμένη στον ΠΡΑΓΜΑΤΙΚΟ χρόνο (220 ms, ease-out cubic) αντί για 10 σταθερά βήματα:
            // ο timer των WinForms έχει ανάλυση ~15 ms, οπότε τα 10 βήματα ήταν λίγα/ανομοιόμορφα (σκαλοπάτια).
            // Το πλάτος κάθε κουμπιού μενού ακολουθεί το πλάτος του sidebar ανά καρέ, ώστε περιεχόμενο και κουμπί
            // να μένουν κεντραρισμένα χωρίς να "πηδούν".
            const double durationMs = 220;
            foreach (var btn in _navButtons.Values) btn.SuppressWidth = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _sidebarAnimTimer = new System.Windows.Forms.Timer { Interval = 8 };
            _sidebarAnimTimer.Tick += (_, _) =>
            {
                double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
                double eased = 1 - Math.Pow(1 - t, 3);
                int w = (int)Math.Round(startWidth + (targetWidth - startWidth) * eased);
                if (w != _sidebar.Width)
                {
                    _sidebar.Width = w;
                    int usable = Math.Max(56, w - _sidebar.Padding.Horizontal);
                    foreach (var btn in _navButtons.Values) btn.Width = Math.Clamp(usable, 56, 184);
                }
                if (t >= 1.0)
                {
                    _sidebarAnimTimer?.Stop();
                    _sidebar.Width = targetWidth;
                    foreach (var btn in _navButtons.Values) { btn.SuppressWidth = false; btn.ApplyWidth(); }
                    onComplete?.Invoke();
                }
            };
            _sidebarAnimTimer.Start();
        }

        // Σελίδα που παρέχεται από plugin. Αν το plugin αποτύχει να φτιάξει τη σελίδα, δείχνουμε μήνυμα αντί να ρίξουμε το Opsis.
        private void ShowPluginPage(OpsisNewsCrawler.Plugin.Abstractions.PluginNavigationItem item)
        {
            Control page;
            try { page = item.CreatePage(); }
            catch (Exception ex)
            {
                page = new Label { Text = "Plugin error: " + ex.Message, ForeColor = UiTheme.TextSecondary, AutoSize = true, Padding = new Padding(24) };
            }
            SetPage(Services.PluginHost.TitleOf(item), page); // ένα κλειδί που λείπει από τα locale files εμφανίζεται ως το ίδιο το κείμενο
        }

        // Ίδια λογική με το ShowPluginPage παρακάτω, εφαρμοσμένη ΚΑΙ στις ενσωματωμένες σελίδες:
        // αν ένας page builder ρίξει exception στα μισά του χτισίματος, δείχνουμε μήνυμα σφάλματος
        // αντί για μισοχτισμένη/κενή σελίδα, και το καταγράφουμε ώστε να μην εξαφανίζεται σιωπηλά
        // (ζητήθηκε ρητά μετά από αναφορά χρήστη για κενή σελίδα Wallpaper χωρίς καταγεγραμμένο σφάλμα).
        private void SafeBuildPage(string key, Action builder)
        {
            try { builder(); }
            catch (Exception ex)
            {
                LogPageError(key, ex);
                _currentPageKey = key;
                var errorPage = new Label { Text = "Page error: " + ex.Message, ForeColor = UiTheme.TextSecondary, AutoSize = true, Padding = new Padding(24) };
                SetPage(key, errorPage);
            }
        }

        private static void LogPageError(string key, Exception ex)
        {
            try
            {
                var path = Path.Combine(AppPaths.AppDataFolder, "ui-errors.log");
                File.AppendAllText(path, $"[{DateTime.Now:O}] Page '{key}' failed to build: {ex}\n");
            }
            catch (Exception) { /* logging must never itself throw */ }
        }

        private void SetPage(string titleKey, Control control)
        {
            _pageTitle.Text = LocalizationManager.T(titleKey);
            Flicker.BatchRedraw(_content, () => SetPageCore(control));
        }

        // #90: Each disabled command is explained IN its page instead of
        // silently appearing grey. Specific descriptions take precedence; the
        // generic fallback does not pretend to know a missing prerequisite.
        private void WireDisabledActionReasons(Control page)
        {
            if (page is not FlowLayoutPanel flow) return;
            var actions = new List<Button>();
            static void Collect(Control parent, List<Button> buttons)
            {
                foreach (Control item in parent.Controls)
                {
                    if (item is Button button) buttons.Add(button);
                    if (item.HasChildren) Collect(item, buttons);
                }
            }
            Collect(page, actions);
            if (actions.Count == 0) return;
            var explanation = new Label
            {
                AutoSize = true, MaximumSize = new Size(700, 0),
                ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody,
                AccessibleRole = AccessibleRole.StaticText, Visible = false,
                Margin = new Padding(6, 2, 6, 8)
            };
            void Refresh()
            {
                if (explanation.IsDisposed) return;
                var disabled = actions.Where(x => !x.IsDisposed && !x.Enabled).Take(12).ToArray();
                explanation.Visible = disabled.Length != 0;
                explanation.Text = string.Join(Environment.NewLine, disabled.Select(button =>
                {
                    string caption = (button.Text ?? button.AccessibleName ?? button.Name ?? "Action").Replace('\r', ' ').Replace('\n', ' ').Trim();
                    string reason = !string.IsNullOrWhiteSpace(button.AccessibleDescription)
                        ? button.AccessibleDescription
                        : StudioText("Unavailable while prerequisites are missing or an operation is in progress.",
                            "Μη διαθέσιμο όσο λείπουν προϋποθέσεις ή εκτελείται κάποια ενέργεια.");
                    return caption + ": " + reason;
                }));
            }
            foreach (var button in actions) button.EnabledChanged += (_, _) => Refresh();
            flow.Controls.Add(explanation);
            Refresh();
        }

        private void SetPageCore(Control control)
        {
            StyleStudioInputs(control);
            ApplySkinColors(control);
            WireDisabledActionReasons(control);
            _visitedSearchText[_currentPageKey] = PageSearch.TextOf(control);
            _content.SuspendLayout();
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Controls.Clear() αφαιρεί τα child controls από τη συλλογή
            // αλλά ΔΕΝ τα κάνει Dispose — έτσι το παλιό page panel (π.χ. του Audio Enhancement, με
            // το δικό του System.Windows.Forms.Timer) έμενε "ορφανό" αλλά ζωντανό, ο Timer του
            // συνέχιζε να τρέχει επ' άπειρον, και το panel.Disposed cleanup ΠΟΤΕ δεν εκτελούνταν
            // ντετερμινιστικά στο UI thread — μόνο (ενδεχομένως) αργότερα από τον GC finalizer σε
            // ΔΙΑΦΟΡΕΤΙΚΟ thread, προκαλώντας διαλείπον NullReferenceException σε πεδία που
            // μοιράζονταν μεταξύ διαδοχικών επισκέψεων στην ίδια σελίδα. Ρητό Dispose εδώ κάνει το
            // cleanup άμεσο και στο σωστό thread.
            // a page that is rebuilt while it is shown (mode change, add/remove of items, language...) keeps the scroll position instead of jumping to the top
            int keepY = -1;
            foreach (Control old in _content.Controls) if (old is ScrollableControl sc) keepY = -sc.AutoScrollPosition.Y;
            bool samePage = _lastPageKey == _currentPageKey && keepY > 0;
            _lastPageKey = _currentPageKey;
            DisposeChildren(_content);
            control.Dock = DockStyle.Fill;
            _content.Controls.Add(control);
            _content.ResumeLayout();
            if (samePage && control is ScrollableControl target)
                BeginInvoke(new Action(() => { if (!target.IsDisposed) target.AutoScrollPosition = new Point(0, keepY); }));
        }

        internal static void RegisterDesktopHotkeys(HotkeyManager keys, Action toggleGaming, Action showWindow, bool optionalAllowed)
        {
            keys.RegisterCtrlAlt('M', showWindow);
            if (!optionalAllowed || !RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.DesktopHotkeys) || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold) return;
            RegisterOptionalDesktopHotkeys(keys, toggleGaming);
        }

        internal static void RegisterOptionalDesktopHotkeys(HotkeyManager keys, Action toggleGaming)
        {
            keys.RegisterCtrlAlt('G', toggleGaming);
            keys.RegisterCtrlAltShift(Keys.Left, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Left));
            keys.RegisterCtrlAltShift(Keys.Right, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Right));
            keys.RegisterCtrlAltShift(Keys.Up, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Up));
            keys.RegisterCtrlAltShift(Keys.Down, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Down));
            keys.RegisterCtrlShift(Keys.Left, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Left));
            keys.RegisterCtrlShift(Keys.Right, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Right));
            keys.RegisterCtrlShift(Keys.Up, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Up));
            keys.RegisterCtrlShift(Keys.Down, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Down));
        }

        private void ShowAndActivate()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        // Συντομεύσεις πληκτρολογίου για κάθε βασική λειτουργία της εφαρμογής (ζητήθηκε ρητά).
        // ProcessCmdKey αντί για απλό KeyDown ώστε να δουλεύουν ΟΠΟΥΔΗΠΟΤΕ μέσα στο παράθυρο,
        // ακόμη κι όταν ένα child control (π.χ. TextBox) έχει focus.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData is >= (Keys.Control | Keys.D1) and <= (Keys.Control | Keys.D9))
            {
                int index = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                var order = new[] { "Dashboard", "Widgets", "DeskZones", "Wallpaper", "Performance", "Settings", "Profiles", "Automation", "About" };
                if (index >= 0 && index < order.Length) { NavigateTo(order[index]); return true; }
            }

            switch (keyData)
            {
                case Keys.Control | Keys.D0:
                    NavigateTo("Personalization");
                    return true;
                case Keys.Control | Keys.K:
                    OpenCommandPalette();
                    return true;
                case Keys.Control | Keys.B:
                    if (!_usesStudio) ToggleSidebarCollapsed();
                    return true;
                case Keys.Control | Keys.E:
                    if (WallpaperHostEngine.Instance.IsEnabled) WallpaperHostEngine.Instance.Disable();
                    else WallpaperHostEngine.Instance.Enable();
                    if (_currentPageKey == "Wallpaper" || _currentPageKey == "Dashboard") { if (_pageBuilders.TryGetValue(_currentPageKey, out var b)) b(); }
                    return true;
                case Keys.Control | Keys.Shift | Keys.N:
                    using (var editor = new ZoneLayoutEditorForm())
                        if (editor.ShowDialog(this) == DialogResult.OK) _statusLabel.Text = LocalizationManager.T("Status.ZonesUpdated");
                    if (_currentPageKey == "DeskZones" && _pageBuilders.TryGetValue("DeskZones", out var dzBuilder)) dzBuilder();
                    return true;
                case Keys.Control | Keys.Oemcomma:
                    NavigateTo("Settings");
                    return true;
                case Keys.F1:
                    // Drill down to the actual focused button/edit/checkbox. The page
                    // remains the fallback; controls can attach their own description.
                    var focused = FindFocusedHelpControl(_content) ?? ActiveControl;
                    string? topic = focused?.AccessibleDescription;
                    if (string.IsNullOrWhiteSpace(topic)) topic = focused?.AccessibleName;
                    if (string.IsNullOrWhiteSpace(topic)) topic = focused?.Text;
                    if (string.IsNullOrWhiteSpace(topic)) topic = focused?.Name;
                    HelpForm.ShowSingle(this, VersionHistory, HelpSection.Instructions,
                        _currentPageKey, string.IsNullOrWhiteSpace(topic) ? null : topic.Trim());
                    return true;
                case Keys.F5:
                    if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                    return true;
                case Keys.Control | Keys.Alt | Keys.F:
                    DeskFlipEngine.Show();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static Control? FindFocusedHelpControl(Control root)
        {
            if (!root.ContainsFocus) return null;
            foreach (Control child in root.Controls)
            {
                if (!child.ContainsFocus) continue;
                return FindFocusedHelpControl(child) ?? child;
            }
            return root.Focused ? root : null;
        }

        private void ShowDashboard()
        {
            if (_usesStudio) { ShowStudioHome(); return; }
            _currentPageKey = "Dashboard";
            var panel = CreatePagePanel();
            AddHeading(panel, "Opsis — Desktop Customization");
            AddText(panel, LocalizationManager.T("Dashboard.Intro"));

            var metrics = SystemMonitorService.Instance.GetSnapshot();
            // "System" card = ζωντανή σύνοψη + συντόμευση στο πλήρες System Monitor (εκεί μένει η λεπτομέρεια:
            // CPU/RAM/δίκτυο/processes). Η γραμμή κατάστασης κάτω-κάτω δείχνει μόνο ένα ambient CPU/RAM glance.
            var systemCard = AddCard(panel, LocalizationManager.T("Dashboard.SystemCardTitle"),
                string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), metrics.CpuPercent.ToString("0.0"), metrics.AvailableMemoryMb.ToString("0"), metrics.TotalMemoryMb.ToString("0"), RamUsedPercent(metrics.AvailableMemoryMb, metrics.TotalMemoryMb)),
                onClick: () => NavigateTo("Performance"));
            AddCard(panel, LocalizationManager.T("Dashboard.DesktopCardTitle"), string.Format(LocalizationManager.T("Dashboard.DesktopCardBodyFormat"),
                Screen.AllScreens.Length, WidgetHostEngine.Instance.GetActiveWidgets().Count, Screen.AllScreens.Sum(s => ZoneLayoutStore.GetLayout(s.DeviceName).Zones.Count)));
            AddCard(panel, LocalizationManager.T("Dashboard.WallpaperCardTitle"), $"{LocalizationManager.T(WallpaperHostEngine.Instance.IsEnabled ? "Common.On" : "Common.Off")}   •   {WallpaperSettings.Load().Mode}   •   {WallpaperSettings.Load().PerformanceMode}");

            var dashboardRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            dashboardRefreshTimer.Tick += (_, _) =>
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                systemCard.Text = string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), m.TotalMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
            };
            RunWhilePageVisible(panel, dashboardRefreshTimer);
            panel.Disposed += (_, _) => dashboardRefreshTimer.Dispose();

            AddSection(panel, LocalizationManager.T("Dashboard.SectionQuickLaunch"));
            AddIconButtonGrid(panel,
                ("Widgets", LocalizationManager.T("Dashboard.QuickOpenWidgets"), (_, _) => NavigateTo("Widgets")),
                ("DeskZones", LocalizationManager.T("Dashboard.QuickEditZones"), (_, _) => { using var editor = new ZoneLayoutEditorForm(); editor.ShowDialog(this); }),
                ("Wallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper"), (_, _) => { WallpaperHostEngine.Instance.Enable(); _statusLabel.Text = LocalizationManager.T("Dashboard.WallpaperEnabledStatus"); }),
                ("Command", LocalizationManager.T("Dashboard.QuickCommandPalette"), (_, _) => ShowCommandPalette()));

            AddSection(panel, LocalizationManager.T("Dashboard.SectionWorkspace"));
            // Τα "Work"/"Gaming"/"Focus" παραμένουν ως έχουν (ονόματα προφίλ/IconKey, βλ. σχόλιο
            // στο TrayApplicationContext) — μόνο η λέξη "Profile/Προφίλ" γύρω τους μεταφράζεται.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            AddIconButtonGrid(panel,
                ("Work", string.Format(profileFmt, "Work"), (_, _) => LoadProfileFromQuickButton("Work")),
                ("Gaming", string.Format(profileFmt, "Gaming"), (_, _) => LoadProfileFromQuickButton("Gaming")),
                ("Focus", string.Format(profileFmt, "Focus"), (_, _) => LoadProfileFromQuickButton("Focus")));
            SetPage("Page.Dashboard.Title", panel);
        }

        private void ShowWidgets()
        {
            _currentPageKey = "Widgets";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Widgets.Intro"));
            // Ζητήθηκε ρητά "όλα τα widgets να έχουν το ίδιο μέγεθος" — ενιαίο προεπιλεγμένο
            // μέγεθος από τις Ρυθμίσεις αντί για διαφορετικό ανά τύπο widget (ο χρήστης μπορεί να
            // το αλλάξει μετά ανά widget, μέσω της νέας λαβής αλλαγής μεγέθους στη γωνία).
            var wds = AppSettings.Load();
            int ww = wds.WidgetDefaultWidth, wh = wds.WidgetDefaultHeight;
            AddSection(panel, LocalizationManager.T("Widgets.SectionDesktopWidgets"));
            AddIconButtonGrid(panel,
                ("SystemMonitor", LocalizationManager.T("Widgets.SystemMonitor"), (_, _) => WidgetHostEngine.Instance.RequestWidget("sysmon", 100, 100, ww, wh)),
                ("Clock", LocalizationManager.T("Widgets.Clock"), (_, _) => WidgetHostEngine.Instance.RequestWidget("clock", 450, 100, ww, wh)),
                ("Network", LocalizationManager.T("Widgets.Network"), (_, _) => WidgetHostEngine.Instance.RequestWidget("network", 100, 350, ww, wh)),
                ("AudioVisualizer", LocalizationManager.T("Widgets.AudioVisualizer"), (_, _) => WidgetHostEngine.Instance.RequestWidget("audio", 450, 350, ww, wh)),
                ("Weather", LocalizationManager.T("Widgets.Weather"), (_, _) => WidgetHostEngine.Instance.RequestWidget("weather", 800, 100, ww, wh)),
                ("Disk", LocalizationManager.T("Widgets.Disk"), (_, _) => WidgetHostEngine.Instance.RequestWidget("disk", 800, 350, ww, wh)),
                ("Settings", LocalizationManager.T("Widgets.QuickSettings"), (_, _) => WidgetHostEngine.Instance.RequestWidget("quicksettings", 1100, 100, ww, wh)),
                // Πλήρες σύνολο widgets - τα υπόλοιπα 4: quick-settings
                // (ήδη πάνω, "Settings"), notifications, media/now-playing, calendar, Bluetooth.
                ("Media", LocalizationManager.T("Widgets.Media"), (_, _) => WidgetHostEngine.Instance.RequestWidget("media", 1100, 350, ww, wh)),
                ("Notifications", LocalizationManager.T("Widgets.Notifications"), (_, _) => WidgetHostEngine.Instance.RequestWidget("notifications", 100, 600, ww, wh)),
                ("Calendar", LocalizationManager.T("Widgets.Calendar"), (_, _) => WidgetHostEngine.Instance.RequestWidget("calendar", 450, 600, ww, wh)),
                ("Bluetooth", LocalizationManager.T("Widgets.Bluetooth"), (_, _) => WidgetHostEngine.Instance.RequestWidget("bluetooth", 800, 600, ww, wh)));

            // Μετακινήθηκε εδώ από τις γενικές Ρυθμίσεις (ζητήθηκε ρητά "διάσπαρτες ρυθμίσεις που
            // μπορούν να μπουν στις αντίστοιχες καρτέλες") - το θέμα widgets/containers και το
            // προεπιλεγμένο μέγεθος νέων widgets αφορούν ΑΠΟΚΛΕΙΣΤΙΚΑ αυτή τη σελίδα.
            AddSection(panel, LocalizationManager.T("Settings.SectionWidgetsContainers"));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.ShowSystemMonitorWidget"),
                AppSettings.Load().ShowSystemMonitor || WidgetHostEngine.Instance.HasRequestedWidget("sysmon"),
                v =>
                {
                    if (v) WidgetHostEngine.Instance.RequestWidget("sysmon", 100, 100, ww, wh);
                    else WidgetHostEngine.Instance.CloseWidget("sysmon");
                    var a = AppSettings.Load(); a.ShowSystemMonitor = v; a.Save();
                }));
            panel.Controls.Add(SettingsView.BuildWidgetsThemeRow());
            panel.Controls.Add(BuildWidgetsTransparencyRows());
            BuildDesktopMaterialCard(panel, DesktopSurface.Widgets);
            var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
            sizeRow.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.WidgetDefaultWidthLabel"), AppSettings.Load().WidgetDefaultWidth, 160, 800,
                v => { var a = AppSettings.Load(); a.WidgetDefaultWidth = v; a.Save(); }));
            sizeRow.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.HeightLabel"), AppSettings.Load().WidgetDefaultHeight, 100, 800,
                v => { var a = AppSettings.Load(); a.WidgetDefaultHeight = v; a.Save(); }, marginLeft: 24));
            panel.Controls.Add(sizeRow);
            panel.Controls.Add(SettingsView.Checkbox(StudioText("Align all widgets to one grid", "Στοίχιση όλων των widgets στο ίδιο πλέγμα"),
                AppSettings.Load().GridSnap, value =>
                {
                    WidgetSnapEngine.EnableGridSnap = value;
                    var saved = AppSettings.Load(); saved.GridSnap = value; saved.Save();
                }));
            panel.Controls.Add(SettingsView.NumericRow(StudioText("Common snap grid size (px)", "Μέγεθος κοινού πλέγματος (px)"),
                AppSettings.Load().GridSize, 5, 100, value =>
                {
                    WidgetSnapEngine.GridSize = value;
                    var saved = AppSettings.Load(); saved.GridSize = value; saved.Save();
                }));

            AddSection(panel, LocalizationManager.T("Widgets.SectionWorkspaceActions"));
            // #72: the temporary unlock never writes IsLocked=false to disk.
            var unlockRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var countdown = new Label { AutoSize = false, Width = 230, Height = 37,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary };
            var unlockButton = new Button { Text = StudioText("Unlock widgets for 2 min", "Ξεκλείδωμα widgets για 2 λεπτά"), AutoSize = true };
            unlockButton.Click += (_, _) => WidgetHostEngine.Instance.UnlockTemporarily(TimeSpan.FromMinutes(2));
            var endUnlock = new Button { Text = StudioText("Lock now", "Κλείδωμα τώρα"), AutoSize = true };
            endUnlock.Click += (_, _) => WidgetHostEngine.Instance.UnlockTemporarily(TimeSpan.Zero);
            var unlockTick = new System.Windows.Forms.Timer { Interval = 1000 };
            unlockTick.Tick += (_, _) =>
            {
                var remaining = WidgetHostEngine.Instance.RemainingTemporaryUnlock;
                countdown.Text = remaining > TimeSpan.Zero
                    ? StudioText($"Temporary access: {remaining.Minutes}:{remaining.Seconds:00}", $"Προσωρινή πρόσβαση: {remaining.Minutes}:{remaining.Seconds:00}")
                    : StudioText("Locks are active", "Τα κλειδώματα είναι ενεργά");
            };
            unlockTick.Start();
            panel.Disposed += (_, _) => { unlockTick.Stop(); unlockTick.Dispose(); };
            unlockRow.Controls.Add(unlockButton); unlockRow.Controls.Add(endUnlock); unlockRow.Controls.Add(countdown);
            panel.Controls.Add(unlockRow);
            AddIconButtonGrid(panel,
                ("Save", LocalizationManager.T("Widgets.SaveLayout"), (_, _) => { WorkspaceProfileService.Save("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.LayoutSavedStatus"); }),
                ("Restore", LocalizationManager.T("Widgets.RestoreLayout"), (_, _) => { WorkspaceProfileService.Load("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.WorkspaceRestoredStatus"); }),
                ("Close", LocalizationManager.T("Widgets.CloseAllWidgets"), (_, _) => { WidgetHostEngine.Instance.CloseAll(); }));

            AddSection(panel, LocalizationManager.T("Widgets.SectionActiveWidgets"));
            var activeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeList);

            // ΔΙΟΡΘΩΣΗ "εξάλειψη flicker στα ενεργά widgets": το παλιό κώδικας έκανε πλήρες
            // DisposeChildren+rebuild ΚΑΘΕ 2s ΑΣΧΕΤΑ αν κάτι είχε πραγματικά αλλάξει — καταστροφή/
            // αναδημιουργία πραγματικών child HWNDs κάθε tick είναι ο πιο συνηθισμένος λόγος
            // ορατού flicker στο WinForms. Τώρα συγκρίνεται μια φθηνή "υπογραφή" (id+lock state)
            // και η ανοικοδόμηση γίνεται ΜΟΝΟ όταν κάτι πραγματικά άλλαξε.
            (WidgetWindow Window, bool Locked)[]? lastWidgets = null;
            string? lastSuspended = null;
            void RefreshActiveList()
            {
                var active = WidgetHostEngine.Instance.GetActiveWidgets()
                    .Where(window => !window.IsDisposed).Select(window => (Window: window, Locked: window.IsLocked)).ToArray();
                var host = WidgetHostEngine.Instance;
                string suspended = host.SessionSuspended
                    ? string.Join("|", host.GetSnapshots().Select(s => s.WidgetId))
                    : "";
                string signature = host.SessionSuspended ? "paused:" + suspended : "active";
                if (lastWidgets != null && lastWidgets.SequenceEqual(active) && signature == lastSuspended) return;
                lastWidgets = active;
                lastSuspended = signature;

                // Ίδια διόρθωση scroll-reset με το RefreshPinnedApps (DeskStrip)/topLabel (Performance):
                // το Controls.Clear()+rebuild μέσα σε AutoScroll container επαναφέρει σιωπηλά στην κορυφή.
                var savedScroll = panel.AutoScrollPosition;
                activeList.SuspendLayout();
                DisposeChildren(activeList);
                if (host.SessionSuspended)
                {
                    activeList.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0),
                        Text = StudioText("Widgets suspended for this session. Resume from the tray.", "Τα widgets είναι σε αναστολή για αυτή τη συνεδρία. Επαναφορά από το tray."),
                        ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                    foreach (var snapshot in host.GetSnapshots())
                    {
                        string id = snapshot.WidgetId;
                        var pending = new Button { AutoSize = true, Text = id + " · " + StudioText("Remove from layout", "Αφαίρεση από τη διάταξη") };
                        pending.Click += (_, _) => { host.CloseWidget(id); RefreshActiveList(); };
                        activeList.Controls.Add(pending);
                    }
                }
                else if (active.Length == 0)
                {
                    activeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoActiveWidgets"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var (w, locked) in active)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = w.WidgetId, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var lockBtn = new HoverButton { Text = LocalizationManager.T(locked ? "Common.Unlock" : "Common.Lock"), Width = 80, Height = 28, Location = new Point(310, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    lockBtn.FlatAppearance.BorderSize = 0;
                    lockBtn.Click += (_, _) => { if (!w.IsDisposed) w.IsLocked = !w.IsLocked; RefreshActiveList(); };
                    row.Controls.Add(lockBtn);
                    var closeBtn = new HoverButton { Text = LocalizationManager.T("Common.Close"), Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    closeBtn.FlatAppearance.BorderSize = 0;
                    closeBtn.Click += (_, _) => { w.Close(); RefreshActiveList(); };
                    row.Controls.Add(closeBtn);
                    activeList.Controls.Add(row);
                }
                ApplySkinColors(activeList);
                activeList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }
            RefreshActiveList();
            var widgetsRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            widgetsRefreshTimer.Tick += (_, _) => RefreshActiveList();
            RunWhilePageVisible(panel, widgetsRefreshTimer);
            panel.Disposed += (_, _) => widgetsRefreshTimer.Dispose();

            BuildWidgetCatalog(panel);
            SetPage("Page.Widgets.Title", panel);
        }

        private void ShowDeskZones()
        {
            _currentPageKey = "DeskZones";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("DeskZones.Intro"));
            BuildDesktopSnappingCard(panel);
            AddSection(panel, LocalizationManager.T("DeskZones.SectionLayouts"));
            AddText(panel, LocalizationManager.T("DeskZones.ShiftDragHint"));
            AddText(panel, LocalizationManager.T("DeskZones.KeyboardAndNeighborHint"));

            foreach (var screen in Screen.AllScreens)
            {
                var row = new Panel { Width = 720, Height = 130, Margin = new Padding(0, 0, 0, 10) };
                var preview = new ZonePreviewPanel { Location = new Point(0, 0), Size = new Size(220, 124) };
                var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
                preview.SetLayout(layout);
                row.Controls.Add(preview);

                string screenLabel = Screen.AllScreens.Length > 1
                    ? string.Format(LocalizationManager.T(screen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), Array.IndexOf(Screen.AllScreens, screen) + 1)
                    : LocalizationManager.T("DeskZones.EditorSingleScreen");
                row.Controls.Add(new Label { Text = $"{screenLabel}  ({screen.Bounds.Width}×{screen.Bounds.Height})", ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Location = new Point(236, 8), AutoSize = true });
                row.Controls.Add(new Label { Text = LocalizationManager.T("DeskZones.Template." + layout.Template.Replace(" ", "")), ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Location = new Point(236, 34), AutoSize = true, Name = "templateLabel" });

                string editText = LocalizationManager.T("DeskZones.EditorApply2");
                var editBtn = new PillButton { Text = editText, Width = 220,
                    Height = CaptionHeight(editText, 220, 28, UiTheme.FontBody, 32), Font = UiTheme.FontBody, Location = new Point(236, 64), BackColor = UiTheme.Surface, ForeColor = UiTheme.AccentCyan };
                var capturedScreen = screen;
                editBtn.Click += (_, _) =>
                {
                    using var editor = new ZoneLayoutEditorForm(capturedScreen);
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        var updated = ZoneLayoutStore.GetLayout(capturedScreen.DeviceName);
                        preview.SetLayout(updated);
                        foreach (Control c in row.Controls) if (c.Name == "templateLabel") c.Text = LocalizationManager.T("DeskZones.Template." + updated.Template.Replace(" ", ""));
                    }
                };
                row.Controls.Add(editBtn);
                panel.Controls.Add(row);
            }

            // Applications that are never snapped (one process name per line, e.g. vlc, obs64)
            AddSection(panel, LocalizationManager.T("DeskZones.SectionExcludedApps"));
            AddText(panel, LocalizationManager.T("DeskZones.ExcludedAppsHint"));
            var excludedBox = new TextBox
            {
                Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = UiTheme.FontBody,
                BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 10),
                Text = string.Join(Environment.NewLine, AppSettings.Load().ZoneExcludedApps)
            };
            AddText(panel, StudioText("Rule format: program|.extension|zone (zero based). Leave extension blank for all documents, e.g. notepad||0.",
                "Κανόνες: πρόγραμμα|.επέκταση|ζώνη (από 0), π.χ. notepad||0. Η επέκταση μπορεί να μείνει κενή."));
            var ruleBox = new TextBox { Multiline=true, Width=465,Height=115,ScrollBars=ScrollBars.Vertical,
                Font=UiTheme.FontBody,BackColor=UiTheme.Surface,ForeColor=UiTheme.TextPrimary,
                Text=string.Join(Environment.NewLine,(AppSettings.Load().ZoneRules ?? new())
                    .Select(r=>$"{r.Process}|{r.Extension}|{r.ZoneIndex}")) };
            panel.Controls.Add(ruleBox);
            panel.Controls.Add(StudioButton(StudioText("Save app/document zone rules", "Αποθήκευση κανόνων ζωνών"), () =>
            {
                var rules=new List<WindowZoneRule>();
                foreach(var line in ruleBox.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)))
                {
                    var parts=line.Trim().Split('|');
                    if(parts.Length!=3 || !int.TryParse(parts[2].Trim(),out var zone) || zone is < 0 or > 47 || parts[0].Length is < 1 or > 80)
                    { MessageBox.Show(this,StudioText("Invalid rule line", "Μη έγκυρη γραμμή κανόνα")+": "+line);return; }
                    var process=Path.GetFileNameWithoutExtension(parts[0].Trim());
                    var ext=parts[1].Trim();
                    if(ext.Length>20 || (ext.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(ext,@"^\.?[a-zA-Z0-9]{1,16}$")))
                    { MessageBox.Show(this,StudioText("Invalid extension", "Μη έγκυρη επέκταση")+": "+ext);return; }
                    rules.Add(new WindowZoneRule{Process=process,Extension=ext,ZoneIndex=zone});
                }
                if(rules.Count>48){MessageBox.Show(this,"Maximum 48 rules.");return;}
                var a=AppSettings.Load();a.ZoneRules=rules;a.Save();AutoTileService.RefreshSettings();
            },width:293));
            excludedBox.Leave += (_, _) =>
            {
                var a = AppSettings.Load();
                a.ZoneExcludedApps = excludedBox.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Path.GetFileNameWithoutExtension(x.Trim())).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                a.Save();
            };
            panel.Controls.Add(excludedBox);
            var tiling = AddCardSection(panel, StudioText("Automatic window tiling", "Αυτόματη διάταξη παραθύρων"));
            var tilingSettings = AppSettings.Load();
            var tilingModes = new[] { "Grid", "Columns", "Stack", "Split" };
            var tilingLabels = new[] { StudioText("Grid", "Πλέγμα"), StudioText("Columns", "Στήλες"), StudioText("Main and stack", "Κύριο και στοίβα"), StudioText("Binary split", "Δυαδικός διαχωρισμός") };
            FxCombo(tiling, StudioText("Layout", "Διάταξη"), tilingLabels,
                tilingLabels[Math.Max(0, Array.IndexOf(tilingModes, tilingSettings.AutoTileMode))], label =>
                {
                    var settings = AppSettings.Load();
                    settings.AutoTileMode = tilingModes[Array.IndexOf(tilingLabels, label)];
                    settings.Save();
                    AutoTileService.RefreshSettings();
                });
            tiling.Controls.Add(SettingsView.NumericRow(StudioText("Window padding", "Περιθώριο παραθύρων"), Math.Clamp(tilingSettings.AutoTileGap, 0, 28), 0, 28,
                value => { var settings = AppSettings.Load(); settings.AutoTileGap = (int)value; settings.Save(); }, 0, "px"));
            tiling.Controls.Add(SettingsView.Checkbox(StudioText("Arrange windows as they open or close", "Τακτοποίηση όταν ανοίγουν ή κλείνουν παράθυρα"),
                tilingSettings.AutoTileEnabled, enabled =>
                {
                    var settings = AppSettings.Load(); settings.AutoTileEnabled = enabled; settings.Save();
                    AutoTileService.RefreshSettings();
                }));
            tiling.Controls.Add(StudioButton(StudioText("Preview and tile windows", "Προεπισκόπηση και τακτοποίηση"), () =>
            {
                var plan = AutoTileService.Preview();
                if (plan.Count == 0)
                {
                    MessageBox.Show(this, StudioText("No eligible windows to arrange.", "Δεν υπάρχουν κατάλληλα παράθυρα για διάταξη."));
                    return;
                }
                var description = string.Join(Environment.NewLine, plan.Take(14).Select(item =>
                    item.Title + " → " + item.Monitor + " / " + item.After.Width + "×" + item.After.Height));
                if (plan.Count > 14) description += $"{Environment.NewLine}… +{plan.Count - 14}";
                if (MessageBox.Show(this,
                        StudioText("Apply exactly this preview? Windows that change before confirmation will be skipped.", "Εφαρμογή ακριβώς αυτής της προεπισκόπησης; Παράθυρα που άλλαξαν ενδιάμεσα θα παραλειφθούν.")
                        + Environment.NewLine + Environment.NewLine + description,
                        StudioText("Preview tiling", "Προεπισκόπηση διάταξης"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    var moved = AutoTileService.ApplyApprovedPreview(plan);
                    ShowStatus(StudioText($"{moved} windows arranged from the approved preview.", $"{moved} παράθυρα τακτοποιήθηκαν βάσει της εγκεκριμένης προεπισκόπησης."), StatusKind.Info);
                }
            }, 240));
            tiling.Controls.Add(StudioButton(StudioText("Undo last manual tiling", "Αναίρεση τελευταίας διάταξης"), () =>
            {
                if (!AutoTileService.UndoLastManual())
                    MessageBox.Show(this, StudioText("No previous manual window arrangement is available.", "Δεν υπάρχει προηγούμενη χειροκίνητη διάταξη."));
            }, 240));

            SetPage("Page.DeskZones.Title", panel);
        }

        private void ShowWallpaperDetails()
        {
            _currentPageKey = "Wallpaper";
            var panel = CreatePagePanel();

            AddButton(panel, StudioText("Back to wallpaper gallery", "Επιστροφή στη συλλογή φόντων"), (_, _) => ShowWallpaper());

            var settings = WallpaperSettings.Load();
            var statusCard = AddCard(panel, LocalizationManager.T("Wallpaper.StatusLabel"), DescribeWallpaperState(settings));
            var playlistCard = settings.Mode == "Video"
                ? AddCard(panel, LocalizationManager.T("Wallpaper.PlaylistLabel"), DescribePlaylist(settings)) : null;

            void RefreshStatus()
            {
                var s = WallpaperSettings.Load();
                statusCard.Text = DescribeWallpaperState(s);
                if (playlistCard != null) playlistCard.Text = DescribePlaylist(s);
            }

            var wallpaper = WallpaperHostEngine.Instance;
            void RefreshIfMounted()
            {
                if (!panel.IsDisposed && !IsDisposed) RefreshStatus();
            }
            void OnWallpaperStateChanged()
            {
                if (panel.IsDisposed || IsDisposed || !IsHandleCreated) return;
                if (!InvokeRequired) { RefreshIfMounted(); return; }
                try { BeginInvoke(RefreshIfMounted); } catch (InvalidOperationException) { }
            }
            wallpaper.StateChanged += OnWallpaperStateChanged;
            panel.Disposed += (_, _) => wallpaper.StateChanged -= OnWallpaperStateChanged;

            if (settings.Mode == WallpaperSettings.AthensMode)
            {
                var athens = AddCardSection(panel, WallpaperModeLabel(WallpaperSettings.AthensMode));
                string[] phases = ["Auto", "Golden", "Day", "Blue", "Night"];
                string[] labels = [StudioText("Automatic · Athens solar time", "Αυτόματα · ήλιος Αθήνας"),
                    StudioText("Golden hour", "Χρυσή ώρα"), StudioText("Day", "Ημέρα"),
                    StudioText("Blue hour", "Μπλε ώρα"), StudioText("Night", "Νύχτα")];
                FxCombo(athens, StudioText("Lighting", "Φωτισμός"), labels,
                    labels[Math.Max(0, Array.IndexOf(phases, settings.AthensPhase))],
                    label => WallpaperHostEngine.Instance.SetAthensPhase(phases[Array.IndexOf(labels, label)]));
            }

            if (WallpaperSettings.IsWeatherMode(settings.Mode)) BuildWeatherWallpaperSection(panel, settings);
            if (settings.Mode is "Desktop" or "Waves" or "Particles")
            {
                var preview = AddCardSection(panel, StudioText("Wallpaper preview", "Προεπισκόπηση φόντου"));
                preview.Controls.Add(new WallpaperTile(settings.Mode, WallpaperModeLabel(settings.Mode),
                    settings.Mode == "Desktop" ? StudioText("Current Windows background", "Τρέχον φόντο των Windows")
                        : StudioText("Hover to play the preview", "Πέρασε τον δείκτη για κίνηση"), null, false)
                { Size = new Size(360, 254), TabStop = false, Cursor = Cursors.Default });
            }

            if (settings.Mode == "Video")
            {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionVideoLibrary"));
            // Ξαναχτίζει ΟΛΟΚΛΗΡΗ τη σελίδα μετά από προσθήκη — ζητήθηκε ρητά "τόσο με την
            // προσθήκη video όσο και με την προσθήκη φακέλου η εφαρμογή να ενημερώνει τη λίστα
            // με τα βίντεο". Πριν, η λίστα (dropdown/κάρτα) δεν ανανεωνόταν καθόλου μετά την
            // προσθήκη — μόνο το στατιστικό κείμενο του playlistCard.
            AddIconButtonGrid(panel, 220,
                ("Add", LocalizationManager.T("Wallpaper.AddVideoButton"), (_, _) => ChooseWallpaperVideoFiles(ShowWallpaperDetails)),
                ("Add", LocalizationManager.T("Wallpaper.AddFolderButton"), (_, _) => { ChooseWallpaperFolder(); ShowWallpaperDetails(); }),
                ("Delete", LocalizationManager.T("Wallpaper.ClearPlaylist"), (_, _) => { WallpaperHostEngine.Instance.ClearVideo(); ShowWallpaperDetails(); }));

            var subfoldersCheck = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.IncludeSubfolders"), Checked = settings.IncludeSubfolders, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 4, 0, 6) };
            subfoldersCheck.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetIncludeSubfolders(subfoldersCheck.Checked);
            panel.Controls.Add(subfoldersCheck);

            // Επιλογή ποια από τα φορτωμένα βίντεο (αρχεία ή περιεχόμενο φακέλου) θα αναπαράγονται: όλα / κανένα με ένα κλικ,
            // και μετά τικάρισμα μεμονωμένων γραμμών παρακάτω (κλικ και πάνω στο όνομα).
            var selectionButtons = new List<(string IconKey, string Text, EventHandler Action)>
            {
                ("Restore", LocalizationManager.T("Wallpaper.SelectAll"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(true); ShowWallpaperDetails(); }),
                ("Close", LocalizationManager.T("Wallpaper.SelectNone"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(false); ShowWallpaperDetails(); }),
            };
            int missingCount = settings.VideoPaths.Count(vp => !File.Exists(vp));
            if (missingCount > 0)
                selectionButtons.Add(("Delete", string.Format(LocalizationManager.T("Wallpaper.RemoveMissing"), missingCount), (_, _) => { WallpaperHostEngine.Instance.RemoveMissingVideos(); ShowWallpaperDetails(); }));
            AddIconButtonGrid(panel, 220, selectionButtons.ToArray());
            AddPlaybackControls(panel);

            // Πραγματική λίστα με ΟΛΑ τα φορτωμένα βίντεο (όχι μόνο ένα στατιστικό "N αρχεία") —
            // κάθε γραμμή έχει το όνομα αρχείου και ένα ✕ για αφαίρεση, ζητήθηκε ρητά "να μπορεί
            // να προσθαφαιρεί αρχεία βίντεο" απευθείας από τη βιβλιοθήκη.
            var videoListPanel = new Panel { Width = 720, Height = Math.Min(300, Math.Max(50, settings.VideoPaths.Count * 46 + 10)), Margin = new Padding(0, 0, 0, 10), AutoScroll = true, BackColor = UiTheme.Surface };
            GlassBlur.ApplyScrollbarTheme(videoListPanel, UiTheme.Surface.GetBrightness() < 0.5f);
            int rowY = 4;
            // drag and drop of the playback order: press on a row (name / thumbnail), drag up or down, a cyan line shows where it will land
            const int VideoRowStep = 46;
            int dropLineY = -1;
            int InsertIndexAt(Point clientPt) => Math.Clamp((clientPt.Y - 4 + VideoRowStep / 2) / VideoRowStep, 0, settings.VideoPaths.Count);
            DragEventHandler vEnter = (_, de) => de.Effect = de.Data?.GetDataPresent("Opsis.VideoRow") == true ? DragDropEffects.Move : DragDropEffects.None;
            DragEventHandler vOver = (_, de) =>
            {
                de.Effect = de.Data?.GetDataPresent("Opsis.VideoRow") == true ? DragDropEffects.Move : DragDropEffects.None;
                var pt = videoListPanel.PointToClient(new Point(de.X, de.Y));
                var y = 4 + InsertIndexAt(pt) * VideoRowStep - 2 + videoListPanel.AutoScrollPosition.Y;
                if (y != dropLineY) { dropLineY = y; videoListPanel.Invalidate(true); }
                if (pt.Y < 24) videoListPanel.AutoScrollPosition = new Point(0, -videoListPanel.AutoScrollPosition.Y - 20);
                else if (pt.Y > videoListPanel.Height - 24) videoListPanel.AutoScrollPosition = new Point(0, -videoListPanel.AutoScrollPosition.Y + 20);
            };
            EventHandler vLeave = (_, _) => { dropLineY = -1; videoListPanel.Invalidate(true); };
            DragEventHandler vDrop = (_, de) =>
            {
                dropLineY = -1;
                if (de.Data?.GetData("Opsis.VideoRow") is int from)
                {
                    var pt = videoListPanel.PointToClient(new Point(de.X, de.Y));
                    pt.Y -= videoListPanel.AutoScrollPosition.Y;
                    WallpaperHostEngine.Instance.MoveVideo(from, InsertIndexAt(pt));
                    ShowWallpaperDetails();
                }
            };
            void AttachDrop(Control c) { c.AllowDrop = true; c.DragEnter += vEnter; c.DragOver += vOver; c.DragLeave += vLeave; c.DragDrop += vDrop; }
            AttachDrop(videoListPanel);
            videoListPanel.Paint += (_, pe) => { if (dropLineY >= 0) using (var br = new SolidBrush(UiTheme.AccentCyan)) pe.Graphics.FillRectangle(br, 4, dropLineY, 700, 3); };
            int videoIndex = 0;
            foreach (var videoPath in settings.VideoPaths.ToArray())
            {
                int myIndex = videoIndex++;
                bool exists = File.Exists(videoPath);
                var row = new Panel { Location = new Point(4, rowY), Size = new Size(700, 42), BackColor = UiTheme.Surface };
                // Ζητήθηκε ρητά "να επιλέγει ο χρήστης 1 ή περισσότερα βίντεο για να
                // αναπαράγονται" — ένα φορτωμένο βίντεο μπορεί να μείνει στη βιβλιοθήκη χωρίς να
                // συμμετέχει στην ενεργή αναπαραγωγή/shuffle (checkbox, όχι αφαίρεση).
                var enabledCheck = new FlatCheckBox { Checked = !settings.DisabledVideoPaths.Contains(videoPath), Location = new Point(2, 7), Size = new Size(28, 28), BackColor = UiTheme.Surface };
                // Ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση ενός media player" (MediaPlayerPro) —
                // λεπτή έγχρωμη λωρίδα αριστερά (ίδια τεχνική με το accent border selection strip
                // στη λίστα playlist του MediaPlayerPro), εδώ δηλώνει "ενεργό στο playlist" αντί
                // για "επιλεγμένο" αφού δεν υπάρχει έννοια επιλογής γραμμής εδώ.
                row.Paint += (_, pe) =>
                {
                    if (enabledCheck.Checked)
                        using (var stripBrush = new SolidBrush(UiTheme.AccentCyan)) pe.Graphics.FillRectangle(stripBrush, 0, 0, 3, row.Height);
                };
                enabledCheck.CheckedChanged += (_, _) => { WallpaperHostEngine.Instance.SetVideoEnabled(videoPath, enabledCheck.Checked); row.Invalidate(); RefreshStatus(); };
                var nameLabel = new Label
                {
                    Text = Path.GetFileName(videoPath) + (exists ? "" : LocalizationManager.T("Wallpaper.MissingSuffix")),
                    AutoSize = false,
                    Size = new Size(500, 28),
                    Location = new Point(102, 7),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = exists ? UiTheme.TextPrimary : UiTheme.TextMuted,
                    Font = UiTheme.FontBody,
                    AutoEllipsis = true
                };
                // Στρογγυλό, borderless κουμπί αφαίρεσης με κυκλικό hover-highlight αντί για απλή
                // αλλαγή χρώματος κειμένου — ίδιο πνεύμα με τα icon-only κουμπιά του MediaPlayerPro.
                var removeBtn = new Label { Text = "", AutoSize = false, Size = new Size(24, 24), Location = new Point(670, 9), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), BackColor = Color.Transparent };
                bool removeHot = false;
                removeBtn.Paint += (_, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (removeHot) using (var hb = new SolidBrush(Color.FromArgb(40, 231, 76, 60))) pe.Graphics.FillEllipse(hb, 0, 0, removeBtn.Width - 1, removeBtn.Height - 1);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using var textBrush = new SolidBrush(removeHot ? Color.FromArgb(231, 76, 60) : UiTheme.TextSecondary);
                    pe.Graphics.DrawString("✕", removeBtn.Font, textBrush, new RectangleF(0, 0, removeBtn.Width, removeBtn.Height), sf);
                };
                removeBtn.MouseEnter += (_, _) => { removeHot = true; removeBtn.Invalidate(); };
                removeBtn.MouseLeave += (_, _) => { removeHot = false; removeBtn.Invalidate(); };
                removeBtn.Click += (_, _) => { WallpaperHostEngine.Instance.RemoveVideo(videoPath); ShowWallpaperDetails(); };
                nameLabel.Click += (_, _) => enabledCheck.Checked = !enabledCheck.Checked;
                Point pressAt = Point.Empty; bool pressed = false;
                void StartDrag(object? s, MouseEventArgs me) { pressed = me.Button == MouseButtons.Left; pressAt = me.Location; }
                void MaybeDrag(object? s, MouseEventArgs me)
                {
                    if (!pressed || (me.Button & MouseButtons.Left) == 0) return;
                    if (Math.Abs(me.X - pressAt.X) < SystemInformation.DragSize.Width && Math.Abs(me.Y - pressAt.Y) < SystemInformation.DragSize.Height) return;
                    pressed = false;
                    var data = new DataObject(); data.SetData("Opsis.VideoRow", myIndex);
                    ((Control)s!).DoDragDrop(data, DragDropEffects.Move);
                }
                nameLabel.MouseDown += StartDrag; nameLabel.MouseMove += MaybeDrag; nameLabel.MouseUp += (_, _) => pressed = false;
                // thumbnail (Windows Shell) + duration (FFmpeg, if present) — loaded in the background, the row never waits for them
                var thumb = new PictureBox { Location = new Point(28, 3), Size = new Size(68, 36), SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.PageBackground, Cursor = Cursors.Hand };
                thumb.Click += (_, _) => enabledCheck.Checked = !enabledCheck.Checked;
                thumb.MouseDown += StartDrag; thumb.MouseMove += MaybeDrag; thumb.MouseUp += (_, _) => pressed = false;
                foreach (var dc in new Control[] { row, enabledCheck, thumb, nameLabel, removeBtn }) AttachDrop(dc);
                var durationLabel = new Label { Text = "", AutoSize = false, Size = new Size(60, 28), Location = new Point(604, 7), TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody };
                if (exists) _ = LoadVideoRowMetaAsync(videoPath, thumb, durationLabel);
                row.Controls.Add(enabledCheck);
                row.Controls.Add(thumb);
                row.Controls.Add(durationLabel);
                row.Controls.Add(nameLabel);
                row.Controls.Add(removeBtn);
                videoListPanel.Controls.Add(row);
                rowY += 46;
            }
            if (settings.VideoPaths.Count == 0)
                videoListPanel.Controls.Add(new Label { Text = LocalizationManager.T("Wallpaper.NoVideosYet"), AutoSize = true, Location = new Point(6, 6), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
            panel.Controls.Add(videoListPanel);

            var shuffle = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.Shuffle"), Checked = settings.Shuffle, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 4, 0, 10) };
            shuffle.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetShuffle(shuffle.Checked);
            panel.Controls.Add(shuffle);

            // Επαναλήψεις ανά βίντεο + εναλλαγή ανά N λεπτά (αποθηκεύονται με καθυστέρηση 500ms μετά το τελευταίο σύρσιμο)
            FlatSlider? repeatTrack = null, rotateTrack = null;
            void CommitPlayback() { if (repeatTrack != null && rotateTrack != null) WallpaperHostEngine.Instance.SetPlaybackOptions(repeatTrack.Value, rotateTrack.Value); }
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.RepeatPerVideo"), settings.RepeatPerVideo, 1, 10, v => v == 1 ? LocalizationManager.T("Wallpaper.RepeatOnce") : $"{v}×", CommitPlayback, t => repeatTrack = t));
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.RotateEvery"), settings.RotateEveryMinutes, 0, 60, v => v == 0 ? LocalizationManager.T("Wallpaper.RotateWhenEnds") : string.Format(LocalizationManager.T("Wallpaper.RotateMinutesFormat"), v), CommitPlayback, t => rotateTrack = t));

            AddText(panel, LocalizationManager.T("Wallpaper.SupportedFormats"));

            // Πραγματικό DSP (bass/mid/treble/volume) πάνω στον ΔΙΚΟ ΜΑΣ ήχο του wallpaper video,
            // μέσω Web Audio API στο wallpaper/index.html (ensureAudioGraph/applyAudioConfig) — όχι
            // system-wide, μόνο η αναπαραγωγή του Opsis. Προεπιλογή ΚΛΕΙΣΤΟ (AudioEnabled
            // false): το wallpaper ήταν πάντα σιωπηλό πριν, δεν αλλάζει συμπεριφορά χωρίς ρητή
            // ενεργοποίηση από τον χρήστη.
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionAudio"));
            var audioEnabledCheck = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.AudioEnabled"), Checked = settings.AudioEnabled, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 4, 0, 8) };
            panel.Controls.Add(audioEnabledCheck);
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Wallpaper.AutoMuteOtherAudio"), AppSettings.Load().AutoMuteWhenOtherAudio,
                v => { var a = AppSettings.Load(); a.AutoMuteWhenOtherAudio = v; a.Save(); }));

            var volumeTrack = new FlatSlider { Minimum = 0, Maximum = 100, Value = (int)Math.Clamp(settings.AudioVolume * 100, 0, 100), Width = 220 };
            var bassTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioBassGain, -12, 12), Width = 220 };
            var midTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioMidGain, -12, 12), Width = 220 };
            var trebleTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioTrebleGain, -12, 12), Width = 220 };

            void ApplyAudioSettings() => WallpaperHostEngine.Instance.SetAudioSettings(
                audioEnabledCheck.Checked, volumeTrack.Value / 100.0, bassTrack.Value, midTrack.Value, trebleTrack.Value);

            Panel AudioRow(string label, FlatSlider track, string unit)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
                var valueLabel = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, Padding = new Padding(8, 6, 0, 0) };
                void UpdateLabel() => valueLabel.Text = unit == "%" ? $"{track.Value}%" : $"{(track.Value > 0 ? "+" : "")}{track.Value} dB";
                UpdateLabel();
                track.ValueChanged += (_, _) => { UpdateLabel(); ApplyAudioSettings(); };
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            audioEnabledCheck.CheckedChanged += (_, _) => ApplyAudioSettings();
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioVolume"), volumeTrack, "%"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioBass"), bassTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioMid"), midTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioTreble"), trebleTrack, "dB"));
            AddText(panel, LocalizationManager.T("Wallpaper.AudioNote"));
            }

            // Ζητήθηκε ρητά "διαφορετικό βίντεο ανά οθόνη" — εμφανίζεται μόνο όταν υπάρχουν
            // πράγματι πολλαπλές οθόνες ΚΑΙ το mode είναι Video (άσχετο για Waves/Particles).
            // Κάθε οθόνη μπορεί να "καρφιτσωθεί" σε ένα συγκεκριμένο βίντεο της βιβλιοθήκης, ή να
            // μείνει στο κοινό μοιρασμένο playlist (προεπιλογή).
            if (Screen.AllScreens.Length > 1 && settings.Mode == "Video")
            {
                AddSection(panel, LocalizationManager.T("Wallpaper.SectionPerScreenVideo"));
                string sharedOption = LocalizationManager.T("Wallpaper.SharedPlaylistOption");
                for (int screenIndex = 0; screenIndex < Screen.AllScreens.Length; screenIndex++)
                {
                    var targetScreen = Screen.AllScreens[screenIndex];
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
                    string screenLabel = string.Format(LocalizationManager.T(targetScreen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), screenIndex + 1);
                    row.Controls.Add(new Label { Text = screenLabel + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0), Width = 140 });

                    var screenCombo = new FlatComboBox { Width = 260 };
                    var videoNames = settings.VideoPaths.Where(File.Exists).ToList();
                    var options = new List<string> { sharedOption };
                    options.AddRange(videoNames.Select(Path.GetFileName)!);
                    string? currentOverride = WallpaperHostEngine.Instance.GetScreenVideoOverride(targetScreen.DeviceName);
                    string selected = currentOverride != null && File.Exists(currentOverride) ? Path.GetFileName(currentOverride)! : sharedOption;
                    screenCombo.SetItems(options, selected);
                    screenCombo.SelectedIndexChanged += (_, _) =>
                    {
                        var pick = screenCombo.SelectedItem;
                        if (pick == null || pick == sharedOption)
                            WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, null);
                        else
                        {
                            var match = videoNames.FirstOrDefault(v => Path.GetFileName(v) == pick);
                            if (match != null) WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, match);
                        }
                    };
                    row.Controls.Add(screenCombo);
                    panel.Controls.Add(row);
                }
                AddText(panel, LocalizationManager.T("Wallpaper.PerScreenNote"));
            }

            // Theme/Palette και Wave Tuning ισχύουν για Waves ΚΑΙ Particles (μοιράζονται το ίδιο
            // cfg.speed/glowIntensity/palette στο JS engine) — άσχετα μόνο σε Video mode.
            if (settings.Mode is "Page" or "Image") BuildMediaWallpaperSection(panel, settings);
            BuildElementsSection(panel, settings);
            if (settings.Mode == "Desktop") AddText(panel, LocalizationManager.T("Wallpaper.DesktopIntro"));

            if (settings.Mode is "Waves" or "Particles")
            {
            var colors = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionThemeColors"));
            var themeCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            themeCombo.SetItems(new[] { "Follow", "Light", "Dark" }, settings.ThemeMode);
            colors.Controls.Add(themeCombo);

            var styleCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            styleCombo.SetItems(new[] { "Ribbons", "Aurora", "TechGrid" }, settings.WaveStyle);
            styleCombo.SelectedIndexChanged += (_, _) => WallpaperHostEngine.Instance.SetWaveStyle(styleCombo.SelectedItem!);
            colors.Controls.Add(styleCombo);

            var paletteCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
            void RepopulatePalettes()
            {
                bool light = themeCombo.SelectedItem switch
                {
                    "Light" => true,
                    "Dark" => false,
                    _ => ThemeService.IsLightTheme(),
                };
                var names = (light ? WavePalettes.Light : WavePalettes.Dark).Select(p => p.Name).ToArray();
                var current = WallpaperSettings.Load().PaletteName;
                paletteCombo.SetItems(names, names.Contains(current) ? current : names.FirstOrDefault());
            }
            RepopulatePalettes();
            themeCombo.SelectedIndexChanged += (_, _) => { WallpaperHostEngine.Instance.SetThemeMode(themeCombo.SelectedItem!); RepopulatePalettes(); };
            paletteCombo.SelectedIndexChanged += (_, _) => { if (paletteCombo.SelectedItem is { } p) WallpaperHostEngine.Instance.SetPalette(p); };
            colors.Controls.Add(paletteCombo);

            var tuning = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionWaveTuning"));
            tuning.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.Speed"), (int)(settings.WaveSpeed * 10), 3, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(v / 10.0, WallpaperSettings.Load().GlowIntensity, WallpaperSettings.Load().LineThickness)));
            tuning.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.GlowIntensity"), (int)(settings.GlowIntensity * 10), 0, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, v / 10.0, WallpaperSettings.Load().LineThickness)));
            tuning.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.LineThickness"), (int)(settings.LineThickness * 10), 5, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, WallpaperSettings.Load().GlowIntensity, v / 10.0)));
            }

            BuildInfoOverlaySection(panel, settings);
            BuildWallpaperScheduleSection(panel, settings);

            BuildWorkspaceCard(panel);
            var performance = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionPerformance"));
            Dictionary<string, HoverButton>? perfButtons = null;
            perfButtons = AddToggleButtonGrid(performance,
                ("High", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("High"); RefreshStatus(); MarkActive(perfButtons!, "High"); }),
                ("Balanced", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Balanced"); RefreshStatus(); MarkActive(perfButtons!, "Balanced"); }),
                ("Low Power", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Low Power"); RefreshStatus(); MarkActive(perfButtons!, "Low Power"); }),
                ("Battery", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Battery"); RefreshStatus(); MarkActive(perfButtons!, "Battery"); }));
            MarkActive(perfButtons, settings.PerformanceMode);

            // Μετακινήθηκε εδώ από τις γενικές Ρυθμίσεις (ζητήθηκε ρητά "διάσπαρτες ρυθμίσεις που
            // μπορούν να μπουν στις αντίστοιχες καρτέλες") - αφορούν ΑΠΟΚΛΕΙΣΤΙΚΑ το wallpaper.
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.AutoPerformanceMode"), AppSettings.Load().AutoPerformanceMode,
                v => { var a = AppSettings.Load(); a.AutoPerformanceMode = v; a.Save(); }));
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnLock"), AppSettings.Load().PauseOnLock,
                v => { var a = AppSettings.Load(); a.PauseOnLock = v; a.Save(); }));
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnRemote"), AppSettings.Load().PauseOnRemoteSession,
                v => { var a = AppSettings.Load(); a.PauseOnRemoteSession = v; a.Save(); }));
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnBatterySaver"), AppSettings.Load().PauseOnBatterySaver,
                v => { var a = AppSettings.Load(); a.PauseOnBatterySaver = v; a.Save(); }));
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.HeavyPause"), AppSettings.Load().WallpaperHeavyPause,
                v => { var a = AppSettings.Load(); a.WallpaperHeavyPause = v; a.Save(); }));
            BuildLoadPauseRows(performance);
            performance.Controls.Add(SettingsView.BuildHardwareProfileRow());
            BuildPerformanceRows(performance);
            performance.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.StartWallpaperWithApp"), AppSettings.Load().StartWallpaperWithWindows,
                v => { var a = AppSettings.Load(); a.StartWallpaperWithWindows = v; a.Save(); }));
            performance.Controls.Add(SettingsView.BuildWmvOutputRow());

            SetPage("Page.Wallpaper.Title", panel);
        }

        private void ShowDeskMusicVisualizer()
        {
            _currentPageKey = "DeskMusicVisualizer";
            var panel = CreatePagePanel();
            panel.Controls.Add(StudioMusic.CreateSettings(StudioGreek, UiTheme.PageBackground, UiTheme.Surface, UiTheme.TextPrimary));
            _pageTitle.Text = "Desk Music Visualizer";
            Flicker.BatchRedraw(_content, () => SetPageCore(panel));
        }

        // ---- "Weather" wallpaper mode: location, live status, simulated weather/time, window-glass effect
        private static readonly string[] WeatherSimIds = { "Auto", "Clear", "PartlyCloudy", "Cloudy", "Drizzle", "Rain", "HeavyRain", "Thunderstorm", "Snow", "Fog" };
        private static readonly string[] TimeSimIds = { "Auto", "Dawn", "Day", "Dusk", "Night" };
        private static readonly string[] WindSimIds = { "Auto", "Calm", "LightBreeze", "FreshBreeze", "Strong", "Gale" };

        // Weather-mode only: simulated weather / wind / time of day + window-glass effect
        private void BuildWeatherWallpaperSection(Panel panel, WallpaperSettings settings)
        {
            var host = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionWeather"));
            AddText(host, LocalizationManager.T("Wallpaper.WeatherIntro"));
            // simulated weather / time of day (Automatic = live)
            FlowLayoutPanel ComboRow(string labelKey, string[] ids, string idPrefix, string currentId, Action<string> onChange)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), AutoSize = false, Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
                var labels = ids.Select(id => LocalizationManager.T(idPrefix + id)).ToArray();
                var combo = new FlatComboBox { Width = 240 };
                combo.SetItems(labels, labels[Math.Max(0, Array.IndexOf(ids, currentId))]);
                combo.SelectedIndexChanged += (_, _) => { int i = combo.SelectedIndex; if (i >= 0 && i < ids.Length) onChange(ids[i]); };
                row.Controls.Add(combo);
                return row;
            }
            host.Controls.Add(ComboRow("Wallpaper.WeatherSimLabel", WeatherSimIds, "Wallpaper.Sim.", settings.WeatherSimulation, id => WallpaperHostEngine.Instance.SetWeatherSimulation(id)));
            host.Controls.Add(ComboRow("Wallpaper.WindSimLabel", WindSimIds, "Wallpaper.Wind.", settings.WindSimulation, id => WallpaperHostEngine.Instance.SetWindSimulation(id)));
            host.Controls.Add(ComboRow("Wallpaper.TimeSimLabel", TimeSimIds, "Wallpaper.Time.", settings.TimeSimulation, id => WallpaperHostEngine.Instance.SetTimeSimulation(id)));

            host.Controls.Add(WeatherDepthPicker(settings.WeatherSpatialMode));
            AddText(host, StudioText(
                "3D needs WebGL2 hardware support and is reduced on low-power profiles; the normal 2D engine remains the automatic fallback.",
                "Το 3D χρειάζεται WebGL2 και περιορίζεται στα προφίλ χαμηλής κατανάλωσης. Ο κλασικός 2D renderer παραμένει διαθέσιμος ως fallback."));

            var glass = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.WeatherGlass"), Checked = settings.WeatherGlass, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 6, 0, 10) };
            glass.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetWeatherGlass(glass.Checked);
            host.Controls.Add(glass);

            void AddRainSlider(string english, string greek, bool onGlass, int initial)
            {
                var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(2, 4, 0, 4) };
                line.Controls.Add(new Label { Text = StudioText(english, greek), Width = 190, Height = 34,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary });
                var slider = new TrackBar { Width = 250, Minimum = 0, Maximum = 200,
                    TickFrequency = 25, SmallChange = 5, LargeChange = 25,
                    Value = Math.Clamp(initial, 0, 200), AutoSize = false, Height = 40 };
                var amount = new Label { Text = slider.Value + "%", AutoSize = false, Width = 55, Height = 34,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary };
                // Show intermediate value without saving to SSD on every pixel of movement.
                slider.ValueChanged += (_, _) => amount.Text = slider.Value + "%";
                slider.MouseUp += (_, _) => WallpaperHostEngine.Instance.SetRainIntensity(onGlass, slider.Value);
                slider.KeyUp += (_, _) => WallpaperHostEngine.Instance.SetRainIntensity(onGlass, slider.Value);
                line.Controls.Add(slider); line.Controls.Add(amount);
                host.Controls.Add(line);
            }
            AddRainSlider("Rain in landscape", "Βροχή στο τοπίο", false, settings.RainLandscapeIntensity);
            AddRainSlider("Rain on window glass", "Βροχή στο τζάμι", true, settings.RainGlassIntensity);
            var focusLabel = new Label { Text = StudioText("Portrait crop focus (horizontal)", "Σημείο ενδιαφέροντος σε κατακόρυφη οθόνη"),
                AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody,
                Margin = new Padding(0, 8, 0, 4) };
            host.Controls.Add(focusLabel);
            var focusSlider = new FlatSlider { Minimum = 0, Maximum = 100,
                Value = Math.Clamp(settings.WeatherPhotoFocusX, 0, 100), Width = 300, Height = 28 };
            focusSlider.MouseUp += (_, _) => WallpaperHostEngine.Instance.SetWeatherPhotoFocus(focusSlider.Value);
            focusSlider.KeyUp += (_, _) => WallpaperHostEngine.Instance.SetWeatherPhotoFocus(focusSlider.Value);
            host.Controls.Add(focusSlider);
            var skyline = new FlatToggle { RowWidth = 700,
                Text = StudioText("Show detected skyline over the photo (preview)",
                    "Εμφάνιση ανιχνευμένης γραμμής ουρανού πάνω στη φωτογραφία (προεπισκόπηση)"),
                Checked = settings.WeatherSkylinePreview, BackColor = UiTheme.PageBackground,
                Margin = new Padding(0, 6, 0, 8) };
            skyline.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetWeatherSkylinePreview(skyline.Checked);
            host.Controls.Add(skyline);

            var selectedLandscape = settings.WeatherLandmarks && LandmarkCatalog.IsValid(settings.WeatherLandmarkId)
                ? settings.WeatherLandmarkId : settings.WeatherLandmarks ? LandmarkCatalog.Auto : LandmarkCatalog.Off;
            var currentLandscape = new Label
            {
                Text = CityScenePicker.Describe(selectedLandscape), AutoSize = true, Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary, Margin = new Padding(9, 11, 0, 0)
            };
            var landscapes = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 10) };
            landscapes.Controls.Add(StudioButton(StudioText("Choose Greek landscape", "Επιλογή ελληνικού τοπίου"), () =>
            {
                using var dialog = new CityScenePicker(selectedLandscape);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedId is not { } id) return;
                WallpaperHostEngine.Instance.SetWeatherLandmark(id);
                selectedLandscape = id;
                currentLandscape.Text = CityScenePicker.Describe(id);
            }, width: 210));
            landscapes.Controls.Add(currentLandscape);
            host.Controls.Add(landscapes);
            var wxTools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            wxTools.Controls.Add(StudioButton(StudioText("Edit photo skyline", "Πινέλο γραμμής ουρανού"), () =>
            {
                if (!SkylineMaskEditorForm.OpenFor(selectedLandscape, this))
                    MessageBox.Show(this, StudioText("Choose a photographic landscape first.", "Επίλεξε πρώτα φωτογραφικό τοπίο."));
            }, width: 210));
            wxTools.Controls.Add(StudioButton(StudioText("Dawn / noon / night", "Χαραυγή / μεσημέρι / νύχτα"), () =>
            {
                using var previewForm = new WeatherTriptychForm();
                previewForm.ShowDialog(this);
            }, width: 230));
            host.Controls.Add(wxTools);
        }

        // ALL wallpaper modes (videos, Waves, Particles, Weather): live weather, location and the clock / date / temperature overlay
        private void BuildInfoOverlaySection(Panel panel, WallpaperSettings settings)
        {
            var host = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionInfo"));
            AddText(host, LocalizationManager.T("Wallpaper.InfoIntro"));

            var liveLabel = new Label { Text = LocalizationManager.T("Wallpaper.WeatherLoading"), AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.AccentCyan, Margin = new Padding(0, 0, 0, 12) };
            host.Controls.Add(liveLabel);
            _ = RefreshLiveWeatherLabelAsync(liveLabel, settings.WeatherLat, settings.WeatherLon);

            // location
            string cityShown = string.IsNullOrWhiteSpace(settings.WeatherCity) ? LocalizationManager.T("Wallpaper.WeatherLocationDefault") : settings.WeatherCity;
            var locLabel = new Label { Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), cityShown) + PlaceSourceLine(settings.WeatherPlaceDetail, settings.WeatherPlaceSource), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4) };
            host.Controls.Add(locLabel);
            var locRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var cityBox = new TextBox { Width = 240, Text = settings.WeatherCity, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 5, 10, 0) };
            var setBtn = new HoverButton { AccessibleDescription = StudioText("Searching for the entered city; enabled when the search finishes.", "Αναζήτηση πόλης· το κουμπί ενεργοποιείται μόλις ολοκληρωθεί."), Text = LocalizationManager.T("Wallpaper.WeatherLocationSet"), AutoSize = true, Padding = new Padding(16, 6, 16, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = UiTheme.AccentBlue, BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), TabStop = false, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 0) };
            setBtn.FlatAppearance.BorderSize = 0;

            // Ζητήθηκε ρητά μετά από αναφορά χρήστη ότι η αναζήτηση "δεν βγάζει επιλογές πόλεων" όταν
            // υπάρχουν πολλές πόλεις με το ίδιο όνομα (π.χ. "Αθήνα, Ελλάδα" vs "Αθήνα, Ουκρανία") — πριν
            // επιλεγόταν σιωπηλά η πρώτη. Τώρα, όταν το GeocodeMultipleAsync βρει περισσότερα από ένα
            // αποτελέσματα, εμφανίζονται ως κλικαρίσμενες κάρτες εδώ αντί να μαντεύουμε.
            var choicesPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 10), BackColor = Color.Transparent, Visible = false };

            // Controls.Clear() αφαιρεί τα child controls από τη συλλογή αλλά ΔΕΝ τα κάνει Dispose —
            // ίδιο bug class με το SetPage() παραπάνω (βλ. σχόλιο εκεί). Χωρίς αυτό, κάθε νέα αναζήτηση
            // τοποθεσίας θα άφηνε πίσω της ορφανά HoverButton handles.
            int locationRequest = 0;
            void ClearChoices()
            {
                DisposeChildren(choicesPanel);
                choicesPanel.Visible = false;
            }

            void ApplyMatch(WeatherService.GeocodeMatch m)
            {
                if (locLabel.IsDisposed) return;
                ++locationRequest;
                WallpaperHostEngine.Instance.SetWeatherLocation(m.Name, m.Lat, m.Lon, m.Source, string.IsNullOrEmpty(m.Country) ? m.Name : $"{m.Name}, {m.Country}", followLocation: true);
                locLabel.Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), m.Name);
                cityBox.Text = m.Name;
                ClearChoices();
                _ = RefreshLiveWeatherLabelAsync(liveLabel, m.Lat, m.Lon);
            }

            async void ApplyCity()
            {
                var name = cityBox.Text.Trim();
                if (name.Length == 0 || !setBtn.Enabled) return;
                int request = ++locationRequest;
                setBtn.Enabled = false;
                ClearChoices();
                try
                {
                    var found = await WeatherService.GeocodeMultipleAsync(name, 5);
                    if (locLabel.IsDisposed || request != locationRequest || !string.Equals(cityBox.Text.Trim(), name, StringComparison.Ordinal)) return;
                    if (found.Count == 1)
                    {
                        ApplyMatch(found[0]);
                    }
                    else if (found.Count > 1)
                    {
                        locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationChoose");
                        foreach (var m in found)
                        {
                            var choiceBtn = new HoverButton
                            {
                                Text = string.IsNullOrEmpty(m.Country) ? m.Name : $"{m.Name}, {m.Country}",
                                AutoSize = true, Padding = new Padding(10, 5, 10, 5), FlatStyle = FlatStyle.Flat,
                                ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, BaseColor = UiTheme.Surface,
                                HoverBackColor = ControlPaint.Light(UiTheme.Surface, 0.15f), TabStop = false, Cursor = Cursors.Hand,
                                Margin = new Padding(0, 0, 0, 4), TextAlign = ContentAlignment.MiddleLeft,
                            };
                            choiceBtn.FlatAppearance.BorderSize = 1;
                            choiceBtn.FlatAppearance.BorderColor = UiTheme.Border;
                            choiceBtn.Click += (_, _) => ApplyMatch(m);
                            choicesPanel.Controls.Add(choiceBtn);
                        }
                        choicesPanel.Visible = true;
                    }
                    else locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationNotFound");
                }
                catch (Exception)
                {
                    if (!locLabel.IsDisposed && request == locationRequest && string.Equals(cityBox.Text.Trim(), name, StringComparison.Ordinal))
                        locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationNotFound");
                }
                finally { if (!setBtn.IsDisposed) setBtn.Enabled = true; }
            }
            setBtn.Click += (_, _) => ApplyCity();
            cityBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyCity(); } };
            locRow.Controls.Add(cityBox);
            locRow.Controls.Add(setBtn);
            host.Controls.Add(locRow);

            // Opt-in device location (explicit click + confirmation; rounded to ~1 km; only the rounded point is stored) and a way to delete it again.
            var reverseToggle = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.WeatherReverseOn"), Checked = settings.WeatherReverseGeocode, BackColor = UiTheme.Surface, Margin = new Padding(0, 2, 0, 2) };
            reverseToggle.CheckedChanged += (_, _) =>
            {
                if (reverseToggle.Checked && MessageBox.Show(this, LocalizationManager.T("Wallpaper.WeatherReverseConsent"), LocalizationManager.T("Wallpaper.WeatherReverseOn"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                { reverseToggle.Checked = false; return; }
                WallpaperHostEngine.Instance.SetWeatherReverseGeocode(reverseToggle.Checked);
            };
            host.Controls.Add(reverseToggle);
            var privacyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 10) };
            var myLocBtn = NewHoverButton(LocalizationManager.T("Wallpaper.WeatherUseMyLocation"), () => { });
            myLocBtn.AccessibleDescription = StudioText("The Windows location request is running; the action is temporarily unavailable.", "Εκκρεμεί αίτημα τοποθεσίας Windows· η ενέργεια είναι προσωρινά μη διαθέσιμη.");
            myLocBtn.Width = 220;
            myLocBtn.Click += async (_, _) =>
            {
                if (MessageBox.Show(this, LocalizationManager.T("Wallpaper.WeatherLocationConsent"), LocalizationManager.T("Wallpaper.WeatherUseMyLocation"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int request = ++locationRequest;
                ClearChoices();
                myLocBtn.Enabled = false;
                try
                {
                    var here = await DeviceLocation.GetApproximateAsync();
                    if (locLabel.IsDisposed || request != locationRequest) return;
                    if (here == null)
                    {
                        if (!locLabel.IsDisposed) locLabel.Text = StudioText(
                            "Windows did not return an accurate location. Select your city or enter coordinates below.",
                            "Τα Windows δεν έδωσαν αρκετά ακριβή τοποθεσία. Διάλεξε πόλη ή βάλε συντεταγμένες παρακάτω.");
                        return;
                    }
                    if (locLabel.IsDisposed) return;
                    // the name: the offline table of Greece first; the online lookup only if the person allowed it (one request, only now)
                    bool greek = LocalizationManager.CurrentLanguage == "el";
                    var placed = await PlaceNaming.ResolveAsync(here.Value.Lat, here.Value.Lon, greek, WallpaperSettings.Load().WeatherReverseGeocode, LocalizationManager.T("Wallpaper.WeatherMyLocation"));
                    string name = placed.Name;
                    if (locLabel.IsDisposed || request != locationRequest) return;
                    WallpaperHostEngine.Instance.SetWeatherLocation(name, here.Value.Lat, here.Value.Lon, placed.Source, placed.Detail, followLocation: true);
                    locLabel.Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), name) + PlaceSourceLine(placed.Detail, placed.Source);
                    cityBox.Text = "";
                    _ = RefreshLiveWeatherLabelAsync(liveLabel, here.Value.Lat, here.Value.Lon);
                }
                finally { if (!myLocBtn.IsDisposed) myLocBtn.Enabled = true; }
            };
            var clearLocBtn = NewHoverButton(LocalizationManager.T("Wallpaper.WeatherLocationClear"), () =>
            {
                ++locationRequest;
                ClearChoices();
                WallpaperHostEngine.Instance.SetWeatherLocation("", 37.9838, 23.7275);
                cityBox.Text = "";
                locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationCleared");
                _ = RefreshLiveWeatherLabelAsync(liveLabel, 37.9838, 23.7275);
            });
            clearLocBtn.Width = 240; clearLocBtn.Margin = new Padding(10, 0, 0, 0);
            privacyRow.Controls.Add(myLocBtn);
            privacyRow.Controls.Add(clearLocBtn);
            host.Controls.Add(privacyRow);
            var coordinateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            var latitude = new TextBox { Width = 125, PlaceholderText = StudioText("Latitude", "Γεωγρ. πλάτος") };
            var longitude = new TextBox { Width = 125, PlaceholderText = StudioText("Longitude", "Γεωγρ. μήκος") };
            coordinateRow.Controls.Add(latitude);
            coordinateRow.Controls.Add(longitude);
            var usePoint = StudioButton(StudioText("Use coordinates", "Χρήση συντεταγμένων"), async () =>
            {
                bool ParseCoordinate(string value, out double point) => double.TryParse(value.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out point);
                if (!ParseCoordinate(latitude.Text, out double lat) || !ParseCoordinate(longitude.Text, out double lon) || !WeatherCoord.Plausible(lat, lon))
                {
                    locLabel.Text = StudioText("Enter decimal latitude and longitude.", "Βάλε έγκυρο γεωγραφικό πλάτος και μήκος σε δεκαδική μορφή.");
                    return;
                }
                int request = ++locationRequest;
                ClearChoices();
                lat = WeatherCoord.Round(lat);
                lon = WeatherCoord.Round(lon);
                var place = await PlaceNaming.ResolveAsync(lat, lon, StudioGreek, WallpaperSettings.Load().WeatherReverseGeocode,
                    LocalizationManager.T("Wallpaper.WeatherMyLocation"));
                if (locLabel.IsDisposed || request != locationRequest) return;
                WallpaperHostEngine.Instance.SetWeatherLocation(place.Name, lat, lon, place.Source, place.Detail, followLocation: true);
                locLabel.Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), place.Name) + PlaceSourceLine(place.Detail, place.Source);
                _ = RefreshLiveWeatherLabelAsync(liveLabel, lat, lon);
            }, 190);
            coordinateRow.Controls.Add(usePoint);
            host.Controls.Add(coordinateRow);
            host.Controls.Add(choicesPanel);

            // Ζητήθηκε ρητά: ρώτα μία φορά αν δεν έχει οριστεί ποτέ πόλη, αντί να δείχνουμε σιωπηλά
            // τον καιρό της Αθήνας χωρίς ο χρήστης να το καταλάβει (έτσι βρέθηκε μια αναφορά "δείχνει
            // συννεφιά ενώ βρέχει" που ήταν απλά λάθος τοποθεσία, όχι bug). BeginInvoke ώστε η σελίδα
            // να έχει προλάβει να ζωγραφιστεί πριν εμφανιστεί το MessageBox.
            if (string.IsNullOrWhiteSpace(settings.WeatherCity) && !settings.WeatherLocationPrompted)
            {
                BeginInvoke(new Action(() =>
                {
                    WallpaperHostEngine.Instance.MarkWeatherLocationPrompted();
                    var result = MessageBox.Show(this,
                        LocalizationManager.T("Wallpaper.WeatherLocationPromptBody"),
                        LocalizationManager.T("Wallpaper.WeatherLocationPromptTitle"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes && !cityBox.IsDisposed) cityBox.Focus();
                }));
            }

            // ---- clock / date / temperature overlay + its position (centre of the block, % of the screen)
            FlatSlider? infoX = null, infoY = null, infoScale = null;
            CheckBox? infoShow = null, infoF = null, infoAll = null;
            void CommitInfo()
            {
                if (infoX == null || infoY == null || infoScale == null || infoShow == null || infoF == null || infoAll == null) return;
                WallpaperHostEngine.Instance.SetWeatherInfo(infoShow.Checked, infoAll.Checked, infoX.Value, infoY.Value, infoScale.Value, infoF.Checked);
            }
            infoShow = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.WeatherInfo"), Checked = settings.WeatherShowInfo, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 8, 0, 4) };
            infoF = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.Fahrenheit"), Checked = settings.WeatherFahrenheit, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 0, 0, 6) };
            infoAll = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.InfoAllModes"), Checked = settings.InfoOnAllModes, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 0, 0, 4) };
            infoShow.CheckedChanged += (_, _) => CommitInfo();
            infoAll.CheckedChanged += (_, _) => CommitInfo();
            infoF.CheckedChanged += (_, _) => CommitInfo();
            host.Controls.Add(infoShow);
            host.Controls.Add(infoAll);
            host.Controls.Add(infoF);
            // ---- font of the overlay: common Windows fonts that are installed, plus "More…" for any other installed font
            var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            fontRow.Controls.Add(new Label { Text = LocalizationManager.T("Wallpaper.InfoFont"), AutoSize = false, Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
            var installed = new HashSet<string>(new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var fontNames = new List<string> { "Segoe UI Light", "Segoe UI", "Segoe UI Semibold", "Segoe UI Black", "Bahnschrift Light", "Bahnschrift", "Calibri Light", "Calibri", "Arial", "Arial Narrow", "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Cambria", "Consolas", "Courier New", "Impact", "Comic Sans MS", "Times New Roman" }
                .Where(installed.Contains).ToList();
            string currentFont = string.IsNullOrWhiteSpace(settings.InfoFont) ? "Segoe UI Light" : settings.InfoFont;
            if (!fontNames.Contains(currentFont, StringComparer.OrdinalIgnoreCase)) fontNames.Insert(0, currentFont);
            var fontCombo = new FlatComboBox { Width = 240 };
            fontCombo.SetItems(fontNames, currentFont);
            fontCombo.SelectedIndexChanged += (_, _) => { if (fontCombo.SelectedItem is { } f) WallpaperHostEngine.Instance.SetInfoFont(f); };
            var moreFonts = NewHoverButton(LocalizationManager.T("Wallpaper.InfoFontMore"), () =>
            {
                using var dlg = new FontDialog { FontMustExist = true, ShowEffects = false, AllowScriptChange = false, Font = new Font(fontCombo.SelectedItem ?? "Segoe UI", 12f) };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var family = dlg.Font.FontFamily.Name;
                if (!fontNames.Contains(family, StringComparer.OrdinalIgnoreCase)) fontNames.Insert(0, family);
                fontCombo.SetItems(fontNames, family);
                WallpaperHostEngine.Instance.SetInfoFont(family);
            });
            moreFonts.Margin = new Padding(10, 0, 0, 0);
            fontRow.Controls.Add(fontCombo);
            fontRow.Controls.Add(moreFonts);
            host.Controls.Add(fontRow);
            host.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoX"), settings.WeatherInfoX, 0, 100, v => $"{v}%", CommitInfo, t => infoX = t));
            host.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoY"), settings.WeatherInfoY, 0, 100, v => $"{v}%", CommitInfo, t => infoY = t));
            host.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoScale"), settings.WeatherInfoScale, 40, 250, v => $"{v}%", CommitInfo, t => infoScale = t));
            host.Controls.Add(NewHoverButton(LocalizationManager.T("Wallpaper.InfoReset"), () =>
            {
                // back to the default position: centred horizontally, upper third of the screen (the size slider is left alone)
                if (infoX != null) infoX.Value = 50;
                if (infoY != null) infoY.Value = 34;
            }));
        }

        private async System.Threading.Tasks.Task RefreshLiveWeatherLabelAsync(Label target, double lat, double lon)
        {
            // A slower request for the previous city must not replace the latest result.
            var request = new object();
            target.Tag = request;
            string text;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await WeatherService.GetWallpaperWeatherJsonAsync(lat, lon));
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                {
                    int code = root.GetProperty("code").GetInt32();
                    string cond = LocalizationManager.T("Wallpaper.Sim." + CodeToSimId(code));
                    // Ζητήθηκε ρητά "η θερμοκρασία να είναι με 1 δεκαδικό" — "0.#" έδειχνε "18°C" όταν
                    // η τιμή έτυχε να είναι ακέραια (παραλείπει το .0), ασυνεπές με την επικάλυψη του
                    // wallpaper (που δείχνει πάντα 1 δεκαδικό). "0.0" δείχνει πάντα ακριβώς 1 δεκαδικό.
                    text = string.Format(LocalizationManager.T("Wallpaper.WeatherLive"), root.GetProperty("temp").GetDouble().ToString("0.0"), root.GetProperty("wind").GetDouble().ToString("0"), cond, WeatherService.ToBeaufort(root.GetProperty("wind").GetDouble()));
                    text += SourceLine(root);
                }
                else text = LocalizationManager.T("Wallpaper.WeatherLiveUnavailable");
            }
            catch (Exception) { text = LocalizationManager.T("Wallpaper.WeatherLiveUnavailable"); }
            if (!target.IsDisposed && ReferenceEquals(target.Tag, request)) target.Text = text;
        }

        // "Source: MET Norway (model for this hour) · observed 12:15" (+ the offline note when the reading is an old one)
        private static string SourceLine(System.Text.Json.JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("source", out var src) || !root.TryGetProperty("observedAt", out var at)) return "";
                if (!DateTime.TryParse(at.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var utc)) return "";
                bool model = root.TryGetProperty("isModel", out var m) && m.GetBoolean();
                string kind = LocalizationManager.T(model ? "Wallpaper.WeatherKindModel" : "Wallpaper.WeatherKindStation");
                string line = Environment.NewLine + string.Format(LocalizationManager.T("Wallpaper.WeatherSourceLine"), src.GetString(), kind, utc.ToLocalTime().ToString("t"));
                if (root.TryGetProperty("stale", out var st) && st.ValueKind == System.Text.Json.JsonValueKind.True) line += Environment.NewLine + LocalizationManager.T("Wallpaper.WeatherStale");
                return line;
            }
            catch (Exception) { return ""; }
        }

        // "(Ampelokipoi . Athens . Attica) source: GeoNames": exactly which name was used and where it came from
        private static string PlaceSourceLine(string? detail, string? source) =>
            string.IsNullOrWhiteSpace(source) ? "" : Environment.NewLine + string.Format(LocalizationManager.T("Wallpaper.WeatherPlaceLine"), string.IsNullOrWhiteSpace(detail) ? "-" : detail, source);

        private static string CodeToSimId(int code) => code switch
        {
            0 => "Clear", 1 or 2 => "PartlyCloudy", 3 => "Cloudy", 45 or 48 => "Fog", 51 or 53 or 55 or 56 or 57 => "Drizzle",
            61 or 66 or 67 or 80 => "Rain", 63 or 81 => "Rain", 65 or 82 => "HeavyRain", 71 or 73 or 75 or 77 or 85 or 86 => "Snow", 95 or 96 or 99 => "Thunderstorm", _ => "Clear"
        };

        private static async System.Threading.Tasks.Task LoadVideoRowMetaAsync(string path, PictureBox thumb, Label duration)
        {
            try
            {
                var bmp = await VideoMetaService.GetThumbnailAsync(path);
                if (bmp != null && !thumb.IsDisposed) thumb.Image = bmp;      // the bitmap is shared/cached: never dispose it here
                var len = await VideoMetaService.GetDurationAsync(path);
                if (len != null && !duration.IsDisposed) duration.Text = len;
            }
            catch (Exception) { /* thumbnails/durations are decoration only */ }
        }

        // Slider with a live value readout that commits 500 ms after the last movement
        private static Panel BuildOptionSlider(string label, int value, int min, int max, Func<int, string> format, Action commit, Action<FlatSlider> register)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 230, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
            var track = new FlatSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220 };
            var readout = new Label { Text = format(track.Value), AutoSize = false, Width = 130, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.AccentCyan, Font = UiTheme.FontBody };
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) => { timer.Stop(); commit(); };
            track.ValueChanged += (_, _) => { readout.Text = format(track.Value); timer.Stop(); timer.Start(); };
            track.Disposed += (_, _) => timer.Dispose();
            register(track);
            row.Controls.Add(track);
            row.Controls.Add(readout);
            return row;
        }

        // Time-of-day schedule: up to 3 windows, each switching the wallpaper MODE
        private static string WallpaperModeLabel(string mode) => StudioWallpaperCatalog.Find(mode)?.Title(StudioGreek)
            ?? (mode == WallpaperSettings.AthensMode ? StudioText("Ancient Athens", "Αρχαία Αθήνα") : mode);

        private static readonly string[] ScheduleModes = ["Waves", "Particles", "Weather", WallpaperSettings.AthensMode, "Video", "Image", "Page",
            "Desktop", .. StudioWallpaperCatalog.Scenes.Select(scene => scene.Mode)];

        private void BuildWallpaperScheduleSection(Panel panel, WallpaperSettings settings)
        {
            var card = AddCardSection(panel, LocalizationManager.T("Wallpaper.SectionSchedule"));
            AddText(card, LocalizationManager.T("Wallpaper.ScheduleNote"));

            var rules = settings.Schedule.Count > 0
                ? settings.Schedule.Select(r => new WallpaperScheduleRule { Enabled = r.Enabled, StartMinutes = r.StartMinutes, EndMinutes = r.EndMinutes, Mode = r.Mode }).ToList()
                : new List<WallpaperScheduleRule>
                {
                    new() { Enabled = false, StartMinutes = 7 * 60, EndMinutes = 19 * 60, Mode = "Waves" },
                    new() { Enabled = false, StartMinutes = 19 * 60, EndMinutes = 23 * 60, Mode = "Weather" },
                    new() { Enabled = false, StartMinutes = 23 * 60, EndMinutes = 7 * 60, Mode = "Particles" },
                };
            while (rules.Count < 3) rules.Add(new WallpaperScheduleRule { Enabled = false, StartMinutes = 0, EndMinutes = 0, Mode = "Waves" });

            var master = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Wallpaper.ScheduleEnable"), Checked = settings.ScheduleEnabled, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 2, 0, 8) };
            card.Controls.Add(master);

            var times = Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{i % 2 * 30:00}").ToArray();
            string Fmt(int m) => $"{m / 60 % 24:00}:{m % 60 / 30 * 30:00}";
            void Commit() => WallpaperHostEngine.Instance.SetSchedule(master.Checked, rules.Select(r => new WallpaperScheduleRule { Enabled = r.Enabled, StartMinutes = r.StartMinutes, EndMinutes = r.EndMinutes, Mode = r.Mode }).ToList());

            foreach (var rule in rules)
            {
                var rowPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
                var on = new FlatCheckBox { Checked = rule.Enabled, AutoSize = false, Width = 30, Height = 30, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 2, 6, 0) };
                var start = new FlatComboBox { Width = 90 }; start.SetItems(times, Fmt(rule.StartMinutes));
                var end = new FlatComboBox { Width = 90, Margin = new Padding(8, 3, 3, 3) }; end.SetItems(times, Fmt(rule.EndMinutes));
                var modeLabels = ScheduleModes.Select(WallpaperModeLabel).ToArray();
                var mode = new FlatComboBox { Width = 160, Margin = new Padding(16, 3, 3, 3) }; mode.SetItems(modeLabels, WallpaperModeLabel(ScheduleModes.Contains(rule.Mode) ? rule.Mode : "Waves"));
                var arrow = new Label { Text = "→", AutoSize = false, Width = 24, Height = 28, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Margin = new Padding(6, 3, 0, 0) };
                int ParseMinutes(string? t) => t != null && t.Length == 5 && int.TryParse(t[..2], out var h) && int.TryParse(t[3..], out var mi) ? h * 60 + mi : 0;
                on.CheckedChanged += (_, _) => { rule.Enabled = on.Checked; Commit(); };
                start.SelectedIndexChanged += (_, _) => { rule.StartMinutes = ParseMinutes(start.SelectedItem); Commit(); };
                end.SelectedIndexChanged += (_, _) => { rule.EndMinutes = ParseMinutes(end.SelectedItem); Commit(); };
                mode.SelectedIndexChanged += (_, _) => { rule.Mode = ScheduleModes[Math.Max(0, mode.SelectedIndex)]; Commit(); };
                rowPanel.Controls.Add(on); rowPanel.Controls.Add(start); rowPanel.Controls.Add(arrow); rowPanel.Controls.Add(end); rowPanel.Controls.Add(mode);
                card.Controls.Add(rowPanel);
            }
            master.CheckedChanged += (_, _) => Commit();
        }

        private static string DescribeWallpaperState(WallpaperSettings s) =>
            string.Format(LocalizationManager.T("Wallpaper.StateFormat"), WallpaperHostEngine.Instance.IsEnabled ? LocalizationManager.T("Wallpaper.EnabledShort") : LocalizationManager.T("Wallpaper.DisabledShort"), s.Mode, s.PerformanceMode);

        private static string DescribePlaylist(WallpaperSettings s)
        {
            var all = s.VideoPaths.Where(File.Exists).ToList();
            if (all.Count == 0) return LocalizationManager.T("Wallpaper.PlaylistEmpty");
            // only the videos whose tick is on take part in the playlist: the count is that of the ACTIVE ones, with "active/total" when some are off
            var playable = all.Where(p => !s.DisabledVideoPaths.Contains(p)).ToList();
            return ActivePlaylistSuffix(playable.Count, all.Count, string.Format(LocalizationManager.T("Wallpaper.PlaylistFormat"), playable.Count, s.Shuffle ? LocalizationManager.T("Wallpaper.ShuffleSuffix") : "", Path.GetFileName(s.CurrentPlaylistFile() ?? "")));
        }

        internal static string ActivePlaylistSuffix(int active, int total, string text) => active == total ? text : text + $"  [{active}/{total}]";

        private static Panel BuildWaveSlider(string label, int value, int min, int max, Action<int> onChange)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
            var track = new FlatSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220 };
            track.ValueChanged += (_, _) => onChange(track.Value);
            row.Controls.Add(track);
            return row;
        }

        private static void ChooseWallpaperVideoFiles(Action? onCompleted = null)
        {
            // Ζητήθηκε ρητά "δυνατότητα φόρτωσης video μεμονωμένων ή playlists με αρχεία
            // zip/7zip" — το ίδιο dialog δέχεται πλέον και αρχεία συμπίεσης· η εξαγωγή τους
            // γίνεται στο AddWallpaperVideosAsync.
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.VideoPlaylist") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitle"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddWallpaperVideosAsync(dialog.FileNames, onCompleted);
        }

        // Το Wallpaper Studio αποδίδει τα βίντεο μέσα σε WebView2 (Chromium <video>), το οποίο ΔΕΝ
        // περιλαμβάνει decoder για τον κλασικό WMV3/VC-1 codec (μόνο H.264/VP8/VP9/AV1/Theora).
        // Αντί για απλή προειδοποίηση, τα .wmv μετατρέπονται πλέον ΑΥΤΟΜΑΤΑ σε .mp4 μέσω FFmpeg
        // (WmvConversionService) πριν προστεθούν στη λίστα — βλ. αναλυτικό σχόλιο εκεί για το
        // γιατί επιλέχθηκε FFmpeg αντί για COM/ActiveX εναλλακτικές.
        private static async Task AddWallpaperVideosAsync(string[] fileNames, Action? onCompleted)
        {
            // Αρχεία .zip/.7z εξάγονται πρώτα — τα βίντεο που βρίσκονται μέσα τους μπαίνουν στην
            // ίδια ροή (ίδιος έλεγχος .wmv->mp4 παρακάτω) σαν να τα είχε επιλέξει ένα-ένα ο
            // χρήστης. Ένα .7z χωρίς εγκατεστημένο 7-Zip στο σύστημα παραλείπεται σιωπηλά εδώ,
            // με προειδοποίηση+σύνδεσμο λήψης μετά την επεξεργασία των υπολοίπων.
            var archiveFiles = fileNames.Where(ArchivePlaylistService.IsArchive).ToArray();
            var plainFiles = fileNames.Except(archiveFiles).ToList();
            bool anySevenZipMissing = false;

            foreach (var archive in archiveFiles)
            {
                if (string.Equals(Path.GetExtension(archive), ".7z", StringComparison.OrdinalIgnoreCase) &&
                    !await Task.Run(() => ArchivePlaylistService.IsSevenZipAvailable))
                {
                    anySevenZipMissing = true;
                    continue;
                }
                plainFiles.AddRange(await Task.Run(() => ArchivePlaylistService.ExtractVideos(archive))); // εκτός UI thread — η εξαγωγή μεγάλου αρχείου πάγωνε το παράθυρο
            }

            if (anySevenZipMissing)
            {
var choice = MessageBox.Show(
                    LocalizationManager.T("Dialog.SevenZipMissing"),
                    LocalizationManager.T("Dialog.SevenZipTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes) ArchivePlaylistService.OpenSevenZipDownloadPage();
            }

            fileNames = plainFiles.ToArray();

            var wmvFiles = fileNames.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToArray();
            var finalPaths = fileNames.Except(wmvFiles).ToList();

            if (wmvFiles.Length > 0)
            {
                if (!await Task.Run(() => WmvConversionService.IsFfmpegAvailable))
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
                    if (finalPaths.Count == 0)
                        MessageBox.Show(LocalizationManager.T("Dialog.ConvertFailed"), LocalizationManager.T("Dialog.ConvertFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (finalPaths.Count > 0)
            {
                var settings = WallpaperSettings.Load();
                settings.AddVideoFiles(finalPaths.ToArray());
                settings.Mode = "Video";
                settings.Save();
                WallpaperHostEngine.Instance.Enable();
                // ΚΡΙΣΙΜΟ: το Enable() από μόνο του ΔΕΝ ζητάει motionDeskRefresh() από ένα ήδη
                // ανοιχτό/ορατό wallpaper window (μόνο BringToFront/Show) — χωρίς αυτό, ένα ήδη
                // τρέχον WebView2 σε λειτουργία Waves δεν μαθαίνει ποτέ ότι το Mode έγινε Video,
                // το .mp4 μετατρέπεται επιτυχώς αλλά δεν παίζει ποτέ.
                _ = WallpaperHostEngine.Instance.RefreshAllAsync();
            }
            onCompleted?.Invoke();
        }

        private static void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
            if (dialog.ShowDialog() == DialogResult.OK)
                WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
        }


        // "Εξατομίκευση" — τα ασφαλή (registry/native API μόνο, καμία τροποποίηση αρχείων
        // συστήματος) κομμάτια του Customization Vision: IconAtlas, DeskCursors, DeskStrip,
        // DeskSounds. Όλα τα ονόματα ελέγχθηκαν ρητά για συγκρούσεις επωνυμίας πριν επιλεγούν.
        private void ShowPersonalization()
        {
            _currentPageKey = "Personalization";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Personalization.Intro"));
            BuildWindowsAppearance(panel);   // Windows look, themes, safety (2.6.0): the same Personalization tab, no sub-tab
            BuildEcosystemStudios(panel); // Shared P3/P4/P5 controls, backed by the Opsis Core contracts
            bool deskFlipGreek = LocalizationManager.CurrentLanguage == "el";
            var flipCard = AddCardSection(panel, deskFlipGreek ? "DeskFlip — Εναλλαγή παραθύρων" : "DeskFlip — Window switcher");
            var flipPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
            flipPicker.Items.AddRange(new object[] { "Flip 3D", "Thumbnail grid", "Compact list" });
            flipPicker.SelectedItem = DeskFlipEngine.Style;
            flipCard.Controls.Add(flipPicker);
            AddText(flipCard, deskFlipGreek
                ? "Επιλέξτε εμφάνιση για το Ctrl+Alt+F. Η συντόμευση Alt+Tab των Windows δεν αλλάζει."
                : "Choose the Ctrl+Alt+F appearance. Windows Alt+Tab remains unchanged.");
            AddButtonGrid(flipCard,
                (deskFlipGreek ? "Αποθήκευση εμφάνισης" : "Save style", (_, _) =>
                {
                    try
                    {
                        if (flipPicker.SelectedItem is string style) DeskFlipEngine.SetStyle(style);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "DeskFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }),
                (deskFlipGreek ? "Άνοιγμα DeskFlip" : "Open DeskFlip", (_, _) => DeskFlipEngine.Show()));

            var nativeThemeCard = AddCardSection(panel, StudioText("Native Shell themes (optional provider)", "Εγγενή θέματα Shell (προαιρετικός πάροχος)"));
            AddText(nativeThemeCard, StudioText("Use a compatible existing Windhawk styler to avoid duplicate native hooks. Experimental, requires installed CLI and mod.", "Χρήση συμβατού υπάρχοντος Windhawk styler χωρίς διπλούς μηχανισμούς. Πειραματικό, απαιτεί εγκατεστημένο CLI και mod."));
            AddButtonGrid(nativeThemeCard, (StudioText("Native Shell Theme Editor", "Επεξεργαστής εγγενών θεμάτων"), (_, _) =>
            {
                using var dialog = new NativeShellThemeForm(); dialog.ShowDialog(this);
            }));

            // ---------- Unified Opsis ecosystem: core modules, gallery, recovery ----------
            var coreCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "Opsis Core — Ενιαίο οικοσύστημα" : "Opsis Core — Unified ecosystem");
            AddText(coreCard, LocalizationManager.CurrentLanguage == "el"
                ? "Όλες οι λειτουργίες και τα πρόσθετα εμφανίζονται στον ίδιο κατάλογο. Οι ρυθμίσεις αρθρώματος ελέγχονται για συμβατότητα· δεν εκτελούν αυτόματα κώδικα ή αλλαγές Windows. Οι μη υλοποιημένες δυνατότητες εμφανίζονται ως σχεδιαζόμενες."
                : "All capabilities and plugin contributions share a catalog. Module switches validate compatibility and do not run arbitrary code or change Windows settings. Unimplemented features are marked as planned.");
            var coreCatalog = new ListView
            {
                Width = 720, Height = 250, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false
            };
            coreCatalog.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Ομάδα" : "Group", 120);
            coreCatalog.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Λειτουργία" : "Capability", 355);
            coreCatalog.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Υλοποίηση" : "Implementation", 185);
            foreach (var module in OpsisModCore.Catalog.OrderBy(x => x.Group).ThenBy(x => x.Label))
            {
                var row = new ListViewItem(module.Group);
                row.SubItems.Add(module.Label);
                row.SubItems.Add(module.HasNativeImplementation ? "Built-in" : "Planned");
                row.Tag = module;
                coreCatalog.Items.Add(row);
            }
            coreCard.Controls.Add(coreCatalog);
            AddButtonGrid(coreCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Στοιχεία αρθρώματος" : "Module details", (_, _) =>
                {
                    if (coreCatalog.SelectedItems.Count != 1 ||
                        coreCatalog.SelectedItems[0].Tag is not OpsisModCore.ModuleDefinition module) return;
                    var state = OpsisModCore.Read();
                    bool flag = state.Enabled.TryGetValue(module.Id, out var enabled) && enabled;
                    var info = "ID: " + module.Id + Environment.NewLine +
                        "Group: " + module.Group + Environment.NewLine +
                        "Native implementation: " + module.HasNativeImplementation + Environment.NewLine +
                        "Recorded opt-in: " + flag + Environment.NewLine +
                        "Dependencies: " + string.Join(", ", module.Requires) + Environment.NewLine +
                        "Conflicts: " + string.Join(", ", module.Conflicts) + Environment.NewLine +
                        "Module preferences are metadata; the corresponding settings remain under their original sections.";
                    MessageBox.Show(this, info, "Opsis Core", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Έλεγχος συμβατότητας" : "Check compatibility", (_, _) =>
                {
                    try
                    {
                        var issues = OpsisModCore.Validate(OpsisModCore.Read(), Environment.OSVersion.Version.Build,
                            SafeModeService.IsSafeMode);
                        MessageBox.Show(this, issues.Count == 0
                            ? "No catalog conflicts detected." : string.Join(Environment.NewLine, issues),
                            "Opsis Core", MessageBoxButtons.OK,
                            issues.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Core", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            AddButtonGrid(coreCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Εναλλαγή λειτουργίας" : "Toggle selected feature", (_, _) =>
                {
                    if (coreCatalog.SelectedItems.Count != 1 ||
                        coreCatalog.SelectedItems[0].Tag is not OpsisModCore.ModuleDefinition module) return;
                    if (!OpsisFeatureSwitcher.CanSwitch(module.Id))
                    {
                        MessageBox.Show(this,
                            StudioText("This feature has its own dedicated settings or is still planned; there is no independent activation adapter.",
                                "Η λειτουργία έχει δικές της ρυθμίσεις ή βρίσκεται υπό σχεδιασμό. Δεν υπάρχει ανεξάρτητος διακόπτης."),
                            "Opsis Core", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    var next = !OpsisFeatureSwitcher.IsEnabled(module.Id);
                    if (MessageBox.Show(this,
                        (next ? StudioText("Enable ", "Ενεργοποίηση ") : StudioText("Disable ", "Απενεργοποίηση ")) +
                        module.Label + "?",
                        "Opsis Core", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        OpsisFeatureSwitcher.Set(module.Id, next);
                        _statusLabel.Text = "Opsis Core — " + module.Label + (next ? " enabled" : " disabled");
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Core", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            var ecosystemCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "Προσθήκες οικοσυστήματος — Gallery" : "Ecosystem contributions — Gallery");
            AddText(ecosystemCard, LocalizationManager.CurrentLanguage == "el"
                ? "Διαβάζει δηλωμένα περιεχόμενα plugins από τους φακέλους εγκατάστασης και από τη Gallery, χωρίς να εκτελεί το πρόσθετο."
                : "Discovers declared wallpaper, widget, theme and related plugin assets without executing a plugin.");
            var ecosystemList = new ListView
            {
                Width = 720, Height = 190, View = View.Details, FullRowSelect = true
            };
            ecosystemList.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Τύπος" : "Type", 135);
            ecosystemList.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Κατηγορία" : "Category", 155);
            ecosystemList.Columns.Add(LocalizationManager.CurrentLanguage == "el" ? "Περιεχόμενο" : "Contribution", 315);
            var galleryFilters = new FlowLayoutPanel
            {
                Width = 720, AutoSize = true, WrapContents = true
            };
            var galleryType = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            galleryType.Items.AddRange(new object[]
            {
                "All", "wallpaper", "widget", "theme", "icon-pack", "cursor-pack",
                "automation-template", "layout"
            });
            galleryType.SelectedIndex = 0;
            var galleryCategory = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            galleryCategory.Items.Add("All"); galleryCategory.SelectedIndex = 0;
            var gallerySearch = new TextBox
            {
                Width = 285, PlaceholderText = StudioText("Search Gallery", "Αναζήτηση στη Gallery")
            };
            galleryFilters.Controls.Add(galleryType);
            galleryFilters.Controls.Add(galleryCategory);
            galleryFilters.Controls.Add(gallerySearch);
            ecosystemCard.Controls.Add(galleryFilters);
            ecosystemCard.Controls.Add(ecosystemList);
            System.Collections.Generic.IReadOnlyList<OpsisEcosystemGallery.Item> contributions =
                Array.Empty<OpsisEcosystemGallery.Item>();

            void DrawContributions()
            {
                var type = galleryType.SelectedItem?.ToString() ?? "All";
                var category = galleryCategory.SelectedItem?.ToString() ?? "All";
                var search = gallerySearch.Text.Trim();
                ecosystemList.BeginUpdate();
                try
                {
                    ecosystemList.Items.Clear();
                    ecosystemList.Groups.Clear();
                    var groups = new System.Collections.Generic.Dictionary<string, ListViewGroup>(
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var item in contributions.Where(x =>
                        (type == "All" || string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)) &&
                        (category == "All" || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)) &&
                        (search.Length == 0 ||
                            x.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                            x.PluginId.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                            x.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase))))
                    {
                        var groupKey = item.Type + " / " + item.Category;
                        if (!groups.TryGetValue(groupKey, out var group))
                        {
                            group = new ListViewGroup(groupKey);
                            groups[groupKey] = group;
                            ecosystemList.Groups.Add(group);
                        }
                        var line = new ListViewItem(item.Type, group);
                        line.SubItems.Add(item.Category);
                        line.SubItems.Add(item.Title + " [" + item.PluginId + "]");
                        line.Tag = item;
                        ecosystemList.Items.Add(line);
                    }
                }
                finally { ecosystemList.EndUpdate(); }
            }

            void RefreshContributions()
            {
                contributions = OpsisEcosystemGallery.Discover();
                var category = galleryCategory.SelectedItem?.ToString() ?? "All";
                galleryCategory.BeginUpdate();
                try
                {
                    galleryCategory.Items.Clear();
                    galleryCategory.Items.Add("All");
                    foreach (var name in contributions.Select(x => x.Category)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                        galleryCategory.Items.Add(name);
                    galleryCategory.SelectedItem = galleryCategory.Items.Contains(category) ? category : "All";
                }
                finally { galleryCategory.EndUpdate(); }
                DrawContributions();
            }
            galleryType.SelectedIndexChanged += (_, _) => DrawContributions();
            galleryCategory.SelectedIndexChanged += (_, _) => DrawContributions();
            gallerySearch.TextChanged += (_, _) => DrawContributions();
            try { RefreshContributions(); }
            catch (Exception ex) { RotatingLogger.Write("Ecosystem Gallery discovery: " + ex.Message); }
            AddButtonGrid(ecosystemCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Ανανέωση προσθηκών" : "Refresh contributions", (_, _) =>
                {
                    try { RefreshContributions(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Εγκατάσταση θέματος plugin" : "Install contributed theme", (_, _) =>
                {
                    if (ecosystemList.SelectedItems.Count != 1 ||
                        ecosystemList.SelectedItems[0].Tag is not OpsisEcosystemGallery.Item item) return;
                    if (MessageBox.Show(this,
                        StudioText("Install the selected data-only theme without applying any Windows changes?",
                            "Να εγκατασταθεί το επιλεγμένο θέμα μόνο ως δεδομένα χωρίς αλλαγές Windows;"),
                        "Opsis Gallery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        var name = OpsisEcosystemGallery.InstallThemeContribution(item);
                        MessageBox.Show(this, StudioText("Theme added to available profiles: ",
                            "Το θέμα προστέθηκε στα διαθέσιμα προφίλ: ") + name,
                            "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Άνοιγμα widget plugin" : "Open contributed widget", (_, _) =>
                {
                    if (ecosystemList.SelectedItems.Count != 1 ||
                        ecosystemList.SelectedItems[0].Tag is not OpsisEcosystemGallery.Item item) return;
                    try
                    {
                        var layout = OpsisEcosystemGallery.ReadWidgetContribution(item);
                        new OpsisCustomWidgetWindow(layout).Show(this);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Ορισμός ως wallpaper" : "Set as wallpaper", (_, _) =>
                {
                    if (ecosystemList.SelectedItems.Count != 1 ||
                        ecosystemList.SelectedItems[0].Tag is not OpsisEcosystemGallery.Item item) return;
                    if (MessageBox.Show(this, StudioText("Apply selected static image wallpaper?", "Να εφαρμοστεί το επιλεγμένο στατικό wallpaper;"),
                        "Opsis Gallery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { OpsisEcosystemGallery.ApplyStaticWallpaper(item); _statusLabel.Text = "Opsis — gallery wallpaper applied."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Πληροφορίες περιεχομένου" : "Contribution information", (_, _) =>
                {
                    if (ecosystemList.SelectedItems.Count != 1 ||
                        ecosystemList.SelectedItems[0].Tag is not OpsisEcosystemGallery.Item item) return;
                    MessageBox.Show(this, item.Title + Environment.NewLine + item.Description +
                        Environment.NewLine + "Type: " + item.Type + " / " + item.Category +
                        Environment.NewLine + "Plugin: " + item.PluginId +
                        Environment.NewLine + "Local asset: " + item.AssetPath,
                        "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));

            var recoveryCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "Recovery Hub — Ασφάλεια και Windows Updates" : "Recovery Hub — Safety and Windows Updates");
            AddText(recoveryCard, LocalizationManager.CurrentLanguage == "el"
                ? "Ενιαίος φάκελος για αντίγραφα ρυθμίσεων Opsis. Τα αρχεία των Windows ελέγχονται με hashes και εκδόσεις, αλλά δεν αντικαθίστανται ούτε επαναφέρονται με απλή αντιγραφή."
                : "Unified Opsis settings snapshots. Windows shell files are checked by version and hash but are never overwritten or restored by simple file copy.");
            AddButtonGrid(recoveryCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Backup ρυθμίσεων" : "Back up settings", (_, _) =>
                {
                    try
                    {
                        var snapshot = OpsisRecoveryHub.Snapshot("Manual user backup");
                        MessageBox.Show(this, "Snapshot: " + snapshot.Id +
                            Environment.NewLine + "Files: " + snapshot.Files.Count +
                            Environment.NewLine + "Path: " + OpsisRecoveryHub.Root,
                            "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Έλεγχος Windows Update" : "Check Windows Update impact", (_, _) =>
                {
                    try
                    {
                        var message = OpsisRecoveryHub.InspectUpdateImpact();
                        MessageBox.Show(this, message, "Opsis Windows Compatibility", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Windows Compatibility", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Επαναφορά backup Opsis" : "Restore Opsis backup", (_, _) =>
                {
                    try
                    {
                        var snapshots = OpsisRecoveryHub.List();
                        if (snapshots.Count == 0)
                        {
                            MessageBox.Show(this, "No saved Opsis snapshots.", "Opsis Recovery Hub",
                                MessageBoxButtons.OK, MessageBoxIcon.Information); return;
                        }
                        var latest = snapshots[0];
                        if (MessageBox.Show(this, "Restore most recent Opsis settings? " + latest.Id +
                            Environment.NewLine + "Current settings will be saved first. Restart Opsis afterwards.",
                            "Opsis Recovery Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        OpsisRecoveryHub.RestoreOwnedSettings(latest.Id);
                        MessageBox.Show(this, "Opsis settings restored. Restart the app to reload them.",
                            "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            // ---------- IconAtlas ----------
            var iconCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionIconAtlas"));
            AddText(iconCard, LocalizationManager.T("Personalization.IconAtlasIntro"));

            Control BuildIconSlotRow(string labelKey, Func<string?> get, Action<string?> set)
            {
                var row = new Panel { Width = 720, Height = 44, Margin = new Padding(0, 0, 0, 8) };
                var preview = new PictureBox { Location = new Point(0, 4), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };

                void RefreshPreview()
                {
                    preview.Image?.Dispose();
                    preview.Image = null;
                    var p = get();
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        try { using var ic = Icon.ExtractAssociatedIcon(p); if (ic != null) preview.Image = ic.ToBitmap(); }
                        catch (Exception) { }
                    }
                }
                RefreshPreview();
                row.Controls.Add(preview);

                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = true, Location = new Point(42, 13) });

                var chooseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ChooseIcon"), Width = 160, Height = 30, Location = new Point(320, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                chooseBtn.FlatAppearance.BorderSize = 0;
                chooseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        set(dlg.FileName);
                        RefreshPreview();
                        _statusLabel.Text = LocalizationManager.T("Personalization.IconAppliedStatus");
                    }
                };
                row.Controls.Add(chooseBtn);

                var resetBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Reset"), Width = 90, Height = 30, Location = new Point(490, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                resetBtn.FlatAppearance.BorderSize = 0;
                resetBtn.Click += (_, _) => { set(null); RefreshPreview(); _statusLabel.Text = LocalizationManager.T("Personalization.IconResetStatus"); };
                row.Controls.Add(resetBtn);

                return row;
            }

            iconCard.Controls.Add(BuildIconSlotRow("Personalization.ThisPcIcon", IconAtlasEngine.GetThisPcIcon, IconAtlasEngine.SetThisPcIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinEmpty", IconAtlasEngine.GetRecycleBinEmptyIcon, IconAtlasEngine.SetRecycleBinEmptyIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinFull", IconAtlasEngine.GetRecycleBinFullIcon, IconAtlasEngine.SetRecycleBinFullIcon));
            // Επέκταση κάλυψης (ζητήθηκε ρητά v1.5.0) — τα υπόλοιπα τυπικά εικονίδια επιφάνειας
            // εργασίας που υποστηρίζουν επίσημα custom icon μέσω registry.
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.NetworkIcon", IconAtlasEngine.GetNetworkIcon, IconAtlasEngine.SetNetworkIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.ControlPanelIcon", IconAtlasEngine.GetControlPanelIcon, IconAtlasEngine.SetControlPanelIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.UsersFilesIcon", IconAtlasEngine.GetUsersFilesIcon, IconAtlasEngine.SetUsersFilesIcon));

            // "Εισαγωγή icon pack (depot)…" — σαρώνει έναν φάκελο για έτοιμα ζευγάρια εικονιδίων
            // Κάδου Ανακύκλωσης (σύμβαση "-empty"/"-full", π.χ. sdushantha/recycle-bin-themes στο
            // GitHub) και τα εφαρμόζει αυτόματα, αντί να χρειάζεται να επιλέξει ο χρήστης τα δύο
            // αρχεία ένα-ένα από τις παραπάνω γραμμές.
            AddButtonGrid(iconCard, (LocalizationManager.T("Personalization.IconPackImport"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.IconPackFolderPrompt") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                var depot = IconAtlasEngine.ScanDepotFolder(folderDlg.SelectedPath);
                if (depot.EmptyIconPath != null || depot.FullIconPath != null)
                {
                    IconAtlasEngine.ApplyDepotToRecycleBin(depot);
                    _statusLabel.Text = string.Format(LocalizationManager.T("Personalization.IconPackAppliedFormat"),
                        LocalizationManager.T(depot.EmptyIconPath != null ? "Personalization.IconPackEmptyOk" : "Personalization.IconPackEmptyMissing"),
                        LocalizationManager.T(depot.FullIconPath != null ? "Personalization.IconPackFullOk" : "Personalization.IconPackFullMissing"));
                }
                else if (depot.SingleIconPath != null)
                {
                    IconAtlasEngine.SetThisPcIcon(depot.SingleIconPath);
                    _statusLabel.Text = LocalizationManager.T("Personalization.IconPackSingleApplied");
                }
                else
                {
                    MessageBox.Show(this, LocalizationManager.T("Personalization.IconPackNotFound"), LocalizationManager.T("Personalization.IconPackTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                ShowPersonalization();
            }),
            // 1.7.8 - "Εισαγωγή icon pack" και "Ορισμός εικονιδίου φακέλου" στην ΙΔΙΑ γραμμή (ένα AddButtonGrid).
            (LocalizationManager.T("Personalization.FolderIcon"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.FolderIcon") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                using var iconDlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                if (iconDlg.ShowDialog(this) == DialogResult.OK)
                {
                    IconAtlasEngine.SetFolderIcon(folderDlg.SelectedPath, iconDlg.FileName);
                    _statusLabel.Text = LocalizationManager.T("Personalization.FolderIconAppliedStatus");
                }
            }));

            // ---------- P0: Explorer View Profiles ----------
            var explorerCard = AddCardSection(panel,
                LocalizationManager.CurrentLanguage == "el" ? "Explorer — Προφίλ προβολής" : "Explorer — View profiles");
            AddText(explorerCard, LocalizationManager.CurrentLanguage == "el"
                ? "Προφίλ παραγωγικότητας: εμφανίζει επεκτάσεις και κρυφά αρχεία, ενεργοποιεί συμπαγή προβολή και ανοίγει στην επιλογή Αυτός ο υπολογιστής. Αποθηκεύεται αντίγραφο των προηγούμενων ρυθμίσεων πριν την πρώτη αλλαγή. Ορισμένες αλλαγές εφαρμόζονται στο επόμενο παράθυρο Explorer."
                : "Productivity profile: shows extensions and hidden files, uses compact view and opens This PC. Original settings are backed up before the first change. Some options apply to newly opened Explorer windows.");
            AddButtonGrid(explorerCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Εφαρμογή προφίλ" : "Apply profile", (_, _) =>
                {
                    if (MessageBox.Show(this,
                        LocalizationManager.CurrentLanguage == "el"
                            ? "Να εφαρμοστεί το προφίλ προβολής Explorer; Οι προηγούμενες ρυθμίσεις θα αποθηκευτούν."
                            : "Apply the Explorer view profile? Previous settings will be backed up.",
                        "Opsis — Explorer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        ExplorerViewProfiles.ApplyProductivity();
                        _statusLabel.Text = LocalizationManager.CurrentLanguage == "el"
                            ? "Το προφίλ Explorer εφαρμόστηκε." : "Explorer profile applied.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Επαναφορά προηγούμενων" : "Restore previous settings", (_, _) =>
                {
                    if (!ExplorerViewProfiles.HasBackup)
                    {
                        MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                            ? "Δεν υπάρχει προηγούμενο αντίγραφο ρυθμίσεων." : "No previous settings backup exists.",
                            "Opsis — Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Να επαναφερθούν οι αποθηκευμένες ρυθμίσεις Explorer;"
                        : "Restore the saved Explorer settings?", "Opsis — Explorer",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        ExplorerViewProfiles.Restore();
                        _statusLabel.Text = LocalizationManager.CurrentLanguage == "el"
                            ? "Οι προηγούμενες ρυθμίσεις Explorer επανήλθαν." : "Previous Explorer settings restored.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            // ---------- P0: Windows 11 Start & Taskbar ----------
            var taskbarCard = AddCardSection(panel,
                LocalizationManager.CurrentLanguage == "el" ? "Windows 11 — Start και Taskbar" : "Windows 11 — Start and Taskbar");
            AddText(taskbarCard, LocalizationManager.CurrentLanguage == "el"
                ? "Προαιρετικά προφίλ Taskbar χωρίς αντικατάσταση του Explorer. Το πρώτο πάτημα αποθηκεύει τις πραγματικές προηγούμενες ρυθμίσεις. Ορισμένες αλλαγές εμφανίζονται μετά από νέα σύνδεση στα Windows. Οι επιλογές προφίλ υποστηρίζονται μόνο στα Windows 11."
                : "Optional Taskbar presets without replacing Explorer. The first application backs up your actual existing settings. Some changes may need a new Windows sign-in. Presets support Windows 11 only.");
            AddButtonGrid(taskbarCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Λιτό Taskbar" : "Minimal Taskbar", (_, _) =>
                {
                    if (!TaskbarViewProfiles.IsWindows11)
                    {
                        MessageBox.Show(this, "Windows 11 required.", "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Εφαρμογή: αριστερή στοίχιση, χωρίς Widgets / Task View / Search; να συνεχίσω;"
                        : "Apply left alignment and hide Widgets, Task View and Search?",
                        "Opsis — Taskbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { TaskbarViewProfiles.ApplyMinimal(); _statusLabel.Text = "Opsis — Taskbar profile applied."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Τυπικό Taskbar" : "Standard Taskbar", (_, _) =>
                {
                    if (!TaskbarViewProfiles.IsWindows11)
                    {
                        MessageBox.Show(this, "Windows 11 required.", "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Εφαρμογή: κεντρική στοίχιση, Widgets / Task View / Search ενεργά; να συνεχίσω;"
                        : "Apply centered alignment with Widgets, Task View and Search enabled?",
                        "Opsis — Taskbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { TaskbarViewProfiles.ApplyStandard(); _statusLabel.Text = "Opsis — Taskbar profile applied."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Επαναφορά Taskbar" : "Restore Taskbar", (_, _) =>
                {
                    if (!TaskbarViewProfiles.HasBackup)
                    {
                        MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el" ? "Δεν υπάρχει αντίγραφο Taskbar." : "No Taskbar backup exists.",
                            "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Επαναφορά των ρυθμίσεων Taskbar πριν από το Opsis;"
                        : "Restore original Taskbar settings from before Opsis?",
                        "Opsis — Taskbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { if (TaskbarViewProfiles.HasBackup) TaskbarViewProfiles.Restore(); TaskbarViewProfiles.RestoreAutoHide(); _statusLabel.Text = "Opsis — Taskbar settings restored."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Ρυθμίσεις Start" : "Start settings", (_, _) =>
                {
                    try { TaskbarViewProfiles.OpenWindowsStartSettings(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Ρυθμίσεις Taskbar" : "Taskbar settings", (_, _) =>
                {
                    try { TaskbarViewProfiles.OpenWindowsTaskbarSettings(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));


            // ---------- P1: granular native shell controls ----------
            var shellAdvancedCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "P1 — Προηγμένες ρυθμίσεις Explorer και Taskbar"
                : "P1 — Advanced Explorer and Taskbar");
            AddText(shellAdvancedCard, LocalizationManager.CurrentLanguage == "el"
                ? "Αλλάζεις κάθε επιλογή ξεχωριστά. Οι αρχικές τιμές αποθηκεύονται στο ίδιο αντίγραφο ασφαλείας με τα προφίλ P0. Καμία τροποποίηση αρχείων συστήματος."
                : "Configure each setting independently. Original settings use the same backup as P0 presets. System files remain untouched.");

            void AddShellOption(string label, string[] choices, int[] values, Func<int> read, Action<int> apply, bool enabled = true)
            {
                var line = new FlowLayoutPanel { Width = 720, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
                line.Controls.Add(new Label { Text = label, Width = 275, Height = 30,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextPrimary });
                var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Enabled = enabled };
                picker.Items.AddRange(choices);
                try
                {
                    var value = read();
                    var found = Array.IndexOf(values, value);
                    picker.SelectedIndex = found >= 0 ? found : 0;
                }
                catch { picker.SelectedIndex = 0; picker.Enabled = false; }
                // No registry writes on selection: only the explicit Apply button commits a change.
                line.Controls.Add(picker);
                var button = new Button { Text = LocalizationManager.CurrentLanguage == "el" ? "Εφαρμογή" : "Apply",
                    Width = 105, Height = 29, Enabled = picker.Enabled };
                button.Click += (_, _) =>
                {
                    if (picker.SelectedIndex < 0) return;
                    var chosen = values[picker.SelectedIndex];
                    if (MessageBox.Show(this, label + ": " + choices[picker.SelectedIndex] + "?",
                        "Opsis — P1", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { apply(chosen); _statusLabel.Text = "Opsis — " + label + " updated."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P1", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                line.Controls.Add(button);
                shellAdvancedCard.Controls.Add(line);
            }

            bool isEl = LocalizationManager.CurrentLanguage == "el";
            AddShellOption(isEl ? "Explorer: επεκτάσεις αρχείων" : "Explorer: file extensions",
                isEl ? new[] { "Εμφάνιση", "Απόκρυψη" } : new[] { "Show", "Hide" },
                new[] { 0, 1 }, () => ExplorerViewProfiles.ReadOption("HideFileExt"),
                value => ExplorerViewProfiles.ApplyOption("HideFileExt", value));
            AddShellOption(isEl ? "Explorer: κρυφά αρχεία" : "Explorer: hidden files",
                isEl ? new[] { "Εμφάνιση", "Απόκρυψη" } : new[] { "Show", "Hide" },
                new[] { 1, 2 }, () => ExplorerViewProfiles.ReadOption("Hidden"),
                value => ExplorerViewProfiles.ApplyOption("Hidden", value));
            AddShellOption(isEl ? "Explorer: συμπαγής προβολή" : "Explorer: compact view",
                isEl ? new[] { "Ενεργή", "Ανενεργή" } : new[] { "On", "Off" },
                new[] { 1, 0 }, () => ExplorerViewProfiles.ReadOption("UseCompactMode"),
                value => ExplorerViewProfiles.ApplyOption("UseCompactMode", value));
            AddShellOption(isEl ? "Explorer: αρχικός φάκελος" : "Explorer: launch location",
                isEl ? new[] { "Αυτός ο υπολογιστής", "Αρχική" } : new[] { "This PC", "Home" },
                new[] { 1, 2 }, () => ExplorerViewProfiles.ReadOption("LaunchTo"),
                value => ExplorerViewProfiles.ApplyOption("LaunchTo", value));
            AddShellOption(isEl ? "Taskbar: στοίχιση" : "Taskbar: alignment",
                isEl ? new[] { "Αριστερά", "Κέντρο" } : new[] { "Left", "Center" },
                new[] { 0, 1 }, () => TaskbarViewProfiles.ReadOption("TaskbarAl"),
                value => TaskbarViewProfiles.ApplyOption("TaskbarAl", value), TaskbarViewProfiles.IsWindows11);
            AddShellOption(isEl ? "Taskbar: Widgets" : "Taskbar: Widgets",
                isEl ? new[] { "Απόκρυψη", "Εμφάνιση" } : new[] { "Hide", "Show" },
                new[] { 0, 1 }, () => TaskbarViewProfiles.ReadOption("TaskbarDa"),
                value => TaskbarViewProfiles.ApplyOption("TaskbarDa", value), TaskbarViewProfiles.IsWindows11);
            AddShellOption(isEl ? "Taskbar: Προβολή εργασιών" : "Taskbar: Task View",
                isEl ? new[] { "Απόκρυψη", "Εμφάνιση" } : new[] { "Hide", "Show" },
                new[] { 0, 1 }, () => TaskbarViewProfiles.ReadOption("ShowTaskViewButton"),
                value => TaskbarViewProfiles.ApplyOption("ShowTaskViewButton", value), TaskbarViewProfiles.IsWindows11);
            AddShellOption(isEl ? "Taskbar: Αναζήτηση" : "Taskbar: Search",
                isEl ? new[] { "Κρυφή", "Εικονίδιο", "Εικονίδιο και ετικέτα", "Πλαίσιο" }
                     : new[] { "Hidden", "Icon", "Icon and label", "Box" },
                new[] { 0, 1, 3, 2 }, () => TaskbarViewProfiles.ReadOption("SearchboxTaskbarMode"),
                value => TaskbarViewProfiles.ApplyOption("SearchboxTaskbarMode", value), TaskbarViewProfiles.IsWindows11);

            AddShellOption(StudioText("Taskbar: Combine buttons", "Taskbar: Ομαδοποίηση κουμπιών"),
                new[] { StudioText("Always", "Πάντα"), StudioText("When full", "Όταν γεμίσει"), StudioText("Never", "Ποτέ") },
                new[] { 0, 1, 2 }, () => TaskbarViewProfiles.ReadOption("TaskbarGlomLevel"),
                value => TaskbarViewProfiles.ApplyOption("TaskbarGlomLevel", value), TaskbarViewProfiles.IsWindows11);
            AddShellOption(StudioText("Secondary taskbars: Combine buttons", "Δευτερεύουσες Taskbar: Ομαδοποίηση"),
                new[] { StudioText("Always", "Πάντα"), StudioText("When full", "Όταν γεμίσει"), StudioText("Never", "Ποτέ") },
                new[] { 0, 1, 2 }, () => TaskbarViewProfiles.ReadOption("MMTaskbarGlomLevel"),
                value => TaskbarViewProfiles.ApplyOption("MMTaskbarGlomLevel", value), TaskbarViewProfiles.IsWindows11);

            AddShellOption(StudioText("Native taskbar: Auto-hide", "Εγγενής Taskbar: Αυτόματη απόκρυψη"),
                new[] { StudioText("Always visible", "Πάντα ορατή"), StudioText("Auto-hide", "Αυτόματη απόκρυψη") },
                new[] { 0, 1 }, TaskbarViewProfiles.ReadAutoHide, TaskbarViewProfiles.ApplyAutoHide, TaskbarViewProfiles.IsWindows11);

            // ---------- P1 Start Studio ----------
            AddButtonGrid(shellAdvancedCard, (LocalizationManager.CurrentLanguage == "el"
                ? "Άνοιγμα Start Studio" : "Open Start Studio", (_, _) =>
            {
                using var launcher = new StartStudioForm();
                launcher.ShowDialog(this);
            }));

            // ---------- P0 Hot Corners ----------
            var cornersCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el" ? "Hot Corners — Γωνίες οθόνης" : "Hot Corners");
            AddText(cornersCard, LocalizationManager.CurrentLanguage == "el"
                ? "Προαιρετικές ενέργειες μετά από παραμονή του δείκτη για 0,7 δευτερόλεπτα σε μία γωνία. Ισχύουν σε όλες τις οθόνες, χωρίς global hooks. Αρχικά ανενεργές."
                : "Opt-in corner actions after holding the pointer at an edge for 0.7 seconds; works across monitors without global hooks. Off by default.");
            var cornersSettings = AppSettings.Load();
            var cornerToggle = new CheckBox
            {
                Text = LocalizationManager.CurrentLanguage == "el" ? "Ενεργοποίηση Hot Corners" : "Enable Hot Corners",
                Checked = cornersSettings.HotCornersEnabled, AutoSize = true, ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(8)
            };
            cornerToggle.CheckedChanged += (_, _) =>
            {
                var settings = AppSettings.Load(); settings.HotCornersEnabled = cornerToggle.Checked; settings.Save();
            };
            cornersCard.Controls.Add(cornerToggle);
            void CornerOption(string name, Func<AppSettings, string> current, Action<AppSettings, string> assign)
            {
                var line = new FlowLayoutPanel { Width = 690, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
                line.Controls.Add(new Label { Text = name, Width = 190, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextPrimary });
                var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                var values = new[] { "None", "OpenOpsis", "ShowDesktop", "TaskView" };
                picker.Items.AddRange(values);
                picker.SelectedItem = values.Contains(current(cornersSettings)) ? current(cornersSettings) : "None";
                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (picker.SelectedItem is not string selected) return;
                    var settings = AppSettings.Load(); assign(settings, selected); settings.Save();
                };
                line.Controls.Add(picker);
                cornersCard.Controls.Add(line);
            }
            CornerOption(LocalizationManager.CurrentLanguage == "el" ? "Επάνω αριστερά" : "Top left",
                x => x.HotCornerTopLeft, (x,v) => x.HotCornerTopLeft = v);
            CornerOption(LocalizationManager.CurrentLanguage == "el" ? "Επάνω δεξιά" : "Top right",
                x => x.HotCornerTopRight, (x,v) => x.HotCornerTopRight = v);
            CornerOption(LocalizationManager.CurrentLanguage == "el" ? "Κάτω αριστερά" : "Bottom left",
                x => x.HotCornerBottomLeft, (x,v) => x.HotCornerBottomLeft = v);
            CornerOption(LocalizationManager.CurrentLanguage == "el" ? "Κάτω δεξιά" : "Bottom right",
                x => x.HotCornerBottomRight, (x,v) => x.HotCornerBottomRight = v);

            // ---------- P1 Hot Corner activation options ----------
            var sensitivityCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "P1 — Ευαισθησία Hot Corners" : "P1 — Hot Corner sensitivity");
            AddText(sensitivityCard, LocalizationManager.CurrentLanguage == "el"
                ? "Χρόνος παραμονής και μέγεθος ενεργής περιοχής. Η προεπιλογή αποφεύγει τυχαίες ενεργοποιήσεις."
                : "Adjust dwell time and activation area; defaults help avoid accidental triggers.");
            void AddCornerParameter(string title, int[] values, Func<AppSettings, int> read, Action<AppSettings, int> write, string suffix)
            {
                var row = new FlowLayoutPanel { Width = 700, Height = 40, WrapContents = false };
                row.Controls.Add(new Label { Text = title, Width = 245, Height = 27,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextPrimary });
                var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
                foreach (var value in values) picker.Items.Add(value + suffix);
                var ix = Array.IndexOf(values, read(AppSettings.Load()));
                picker.SelectedIndex = ix < 0 ? 0 : ix;
                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (picker.SelectedIndex < 0) return;
                    var settings = AppSettings.Load();
                    write(settings, values[picker.SelectedIndex]);
                    settings.Save();
                };
                row.Controls.Add(picker);
                sensitivityCard.Controls.Add(row);
            }
            AddCornerParameter(LocalizationManager.CurrentLanguage == "el" ? "Χρόνος παραμονής" : "Activation dwell",
                new[] { 300, 500, 700, 1000, 1500, 2000, 2500 },
                s => s.HotCornerDwellMilliseconds, (s,v) => s.HotCornerDwellMilliseconds = v, " ms");
            AddCornerParameter(LocalizationManager.CurrentLanguage == "el" ? "Ενεργή περιοχή" : "Activation edge size",
                new[] { 2, 3, 5, 8, 12, 16, 20 },
                s => s.HotCornerEdgePixels, (s,v) => s.HotCornerEdgePixels = v, " px");
            var fullscreenToggle = new CheckBox
            {
                Text = LocalizationManager.CurrentLanguage == "el" ? "Παύση σε εφαρμογές πλήρους οθόνης" : "Pause when fullscreen apps are active",
                Checked = AppSettings.Load().HotCornersPauseOnFullscreen,
                AutoSize = true, ForeColor = UiTheme.TextPrimary, Margin = new Padding(8)
            };
            fullscreenToggle.CheckedChanged += (_, _) =>
            {
                var settings = AppSettings.Load();
                settings.HotCornersPauseOnFullscreen = fullscreenToggle.Checked;
                settings.Save();
            };
            sensitivityCard.Controls.Add(fullscreenToggle);

            // ---------- P2: Appearance Preset Manager ----------
            var presetCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "P2 — Προφίλ εμφάνισης" : "P2 — Appearance presets");
            AddText(presetCard, LocalizationManager.CurrentLanguage == "el"
                ? "Αποθήκευση και φόρτωση συνδυασμένων επιλογών Explorer, Taskbar και Hot Corners. Πριν από κάθε εφαρμογή υπάρχει προεπισκόπηση και επιβεβαίωση, με προσπάθεια αυτόματης επαναφοράς αν αποτύχει."
                : "Save and load combined Explorer, Taskbar and Hot Corner preferences. Review changes before applying; rollback is attempted on failure.");
            var presetName = new TextBox { Width = 240, PlaceholderText = LocalizationManager.CurrentLanguage == "el"
                ? "Όνομα νέου προφίλ" : "New preset name" };
            var presetPicker = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            var presetLine = new FlowLayoutPanel { Width = 700, Height = 44, WrapContents = false };
            presetLine.Controls.Add(presetName);
            presetLine.Controls.Add(presetPicker);
            presetCard.Controls.Add(presetLine);
            void ReloadPresets()
            {
                var selected = presetPicker.SelectedItem?.ToString();
                presetPicker.Items.Clear();
                foreach (var saved in AppearancePresets.List()) presetPicker.Items.Add(saved);
                if (selected != null && presetPicker.Items.Contains(selected)) presetPicker.SelectedItem = selected;
                else if (presetPicker.Items.Count > 0) presetPicker.SelectedIndex = 0;
            }
            ReloadPresets();
            AddButtonGrid(presetCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Αποθήκευση τρέχοντος" : "Save current", (_, _) =>
                {
                    try
                    {
                        AppearancePresets.SaveCurrent(presetName.Text);
                        ReloadPresets();
                        presetPicker.SelectedItem = presetName.Text.Trim();
                        _statusLabel.Text = "Opsis — appearance preset saved.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Προεπισκόπηση αλλαγών" : "Preview changes", (_, _) =>
                {
                    if (presetPicker.SelectedItem is not string selected) return;
                    try { MessageBox.Show(this, AppearancePresets.Preview(selected), "Opsis — P2 Preview", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Γραφική προεπισκόπηση" : "Visual preview", (_, _) =>
                {
                    if (presetPicker.SelectedItem is not string selected) return;
                    try
                    {
                        using var previewWindow = new AppearancePreviewForm(AppearancePresets.Read(selected));
                        previewWindow.ShowDialog(this);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Εφαρμογή προφίλ" : "Apply preset", (_, _) =>
                {
                    if (presetPicker.SelectedItem is not string selected) return;
                    try
                    {
                        var preview = AppearancePresets.Preview(selected);
                        if (MessageBox.Show(this, preview + Environment.NewLine + Environment.NewLine +
                            (LocalizationManager.CurrentLanguage == "el" ? "Να εφαρμοστεί το προφίλ;" : "Apply this preset?"),
                            "Opsis — P2", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        AppearancePresets.Apply(selected);
                        _statusLabel.Text = "Opsis — appearance preset applied.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Εξαγωγή JSON" : "Export JSON", (_, _) =>
                {
                    if (presetPicker.SelectedItem is not string selected) return;
                    using var dialog = new SaveFileDialog
                    {
                        Filter = "Opsis appearance preset (*.json)|*.json",
                        FileName = selected + ".json",
                        Title = "Export Opsis appearance preset",
                        OverwritePrompt = false
                    };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        AppearancePresets.ExportPortable(selected, dialog.FileName);
                        _statusLabel.Text = "Opsis — appearance preset exported.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Εισαγωγή JSON" : "Import JSON", (_, _) =>
                {
                    using var dialog = new OpenFileDialog
                    {
                        Filter = "Opsis appearance preset (*.json)|*.json",
                        CheckFileExists = true, Multiselect = false,
                        Title = "Import data-only Opsis appearance preset"
                    };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        var importedName = AppearancePresets.ImportPortable(dialog.FileName);
                        ReloadPresets();
                        presetPicker.SelectedItem = importedName;
                        MessageBox.Show(this,
                            LocalizationManager.CurrentLanguage == "el"
                              ? "Το προφίλ εισήχθη χωρίς να εφαρμοστεί. Χρησιμοποίησε την προεπισκόπηση πριν από την εφαρμογή."
                              : "Preset imported without applying changes. Review the preview before applying.",
                            "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Διαγραφή προφίλ" : "Delete preset", (_, _) =>
                {
                    if (presetPicker.SelectedItem is not string selected) return;
                    if (MessageBox.Show(this, selected + "?", "Opsis — Delete preset",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    try { AppearancePresets.Delete(selected); ReloadPresets(); _statusLabel.Text = "Opsis — preset deleted."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — P2", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            // ---------- P0 Native Mouse Effects and Screen Saver ----------
            var effectsCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "Εφέ ποντικιού και προφύλαξη οθόνης" : "Mouse effects and screen saver");
            AddText(effectsCard, LocalizationManager.CurrentLanguage == "el"
                ? "Εφέ ίχνους δείκτη με τον εγγενή μηχανισμό Windows. Το Opsis αποθηκεύει την προηγούμενη ρύθμιση για επαναφορά. Η προφύλαξη οθόνης ρυθμίζεται από το επίσημο παράθυρο των Windows."
                : "Native Windows cursor trails with a backup of the previous value. Screen saver uses the official Windows configuration panel.");
            AddButtonGrid(effectsCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Ενεργοποίηση ίχνους" : "Enable pointer trails", (_, _) =>
                {
                    try
                    {
                        var settings = AppSettings.Load();
                        if (!settings.MouseTrailsOriginalCount.HasValue)
                        {
                            settings.MouseTrailsOriginalCount = DesktopEffects.GetMouseTrails();
                            settings.Save();
                        }
                        DesktopEffects.SetMouseTrails(4);
                        _statusLabel.Text = "Opsis — pointer trails enabled.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Mouse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Επαναφορά δείκτη" : "Restore pointer trails", (_, _) =>
                {
                    try
                    {
                        var settings = AppSettings.Load();
                        if (!settings.MouseTrailsOriginalCount.HasValue) return;
                        DesktopEffects.SetMouseTrails(settings.MouseTrailsOriginalCount.Value);
                        settings.MouseTrailsOriginalCount = null;
                        settings.Save();
                        _statusLabel.Text = "Opsis — pointer trails restored.";
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Mouse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Ρυθμίσεις ποντικιού" : "Mouse settings", (_, _) =>
                {
                    try { DesktopEffects.OpenMouseSettings(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Mouse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Προεπισκόπηση screensaver" : "Preview screen saver", (_, _) =>
                {
                    try { DesktopEffects.PreviewSystemScreenSaver(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Screen saver", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Προφύλαξη οθόνης" : "Screen saver settings", (_, _) =>
                {
                    try { DesktopEffects.OpenScreenSaverSettings(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Screen saver", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));

            // ---------- P0 Appearance preview (read-only) ----------
            var previewCard = AddCardSection(panel, LocalizationManager.CurrentLanguage == "el"
                ? "Προεπισκόπηση προφίλ εμφάνισης" : "Appearance profile preview");
            AddText(previewCard, LocalizationManager.CurrentLanguage == "el"
                ? "Προεπισκόπηση των αλλαγών πριν εφαρμοστούν, χωρίς τροποποίηση συστήματος."
                : "Read-only summary of proposed changes before applying anything.");
            AddButtonGrid(previewCard,
                (LocalizationManager.CurrentLanguage == "el" ? "Προεπισκόπηση Explorer" : "Preview Explorer", (_, _) =>
                {
                    MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Προφίλ Explorer:\n• Επεκτάσεις αρχείων: ορατές\n• Κρυφά αρχεία: ορατά\n• Συμπαγής προβολή: ενεργή\n• Αρχική προβολή: Αυτός ο υπολογιστής\n\nΔεν έγινε καμία αλλαγή."
                        : "Explorer preview:\n• File extensions: visible\n• Hidden files: visible\n• Compact view: enabled\n• Start view: This PC\n\nNo settings were modified.",
                        "Opsis — Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                (LocalizationManager.CurrentLanguage == "el" ? "Προεπισκόπηση Taskbar" : "Preview Taskbar", (_, _) =>
                {
                    MessageBox.Show(this, LocalizationManager.CurrentLanguage == "el"
                        ? "Λιτό: αριστερή στοίχιση, απόκρυψη Search / Widgets / Task View.\nΤυπικό: κεντρική στοίχιση, εμφάνιση Search / Widgets / Task View.\n\nΔεν έγινε καμία αλλαγή."
                        : "Minimal: left alignment; Search / Widgets / Task View hidden.\nStandard: center alignment; Search / Widgets / Task View shown.\n\nNo settings were modified.",
                        "Opsis — Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));

            // ---------- DeskCursors ----------
            var cursorCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskCursors"));
            AddText(cursorCard, LocalizationManager.T("Personalization.DeskCursorsIntro"));

            foreach (var (registryName, labelKey) in DeskCursorsEngine.Roles)
            {
                var row = new Panel { Width = 720, Height = 36, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 6), Size = new Size(185, 24) });
                var pathLabel = new Label { Text = Path.GetFileName(DeskCursorsEngine.GetCursor(registryName) ?? "") is { Length: > 0 } fn ? fn : LocalizationManager.T("Personalization.NotSet"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(190, 6), Size = new Size(230, 24) };
                row.Controls.Add(pathLabel);

                var browseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Browse"), Width = 110, Height = 28, Location = new Point(430, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                browseBtn.FlatAppearance.BorderSize = 0;
                string capturedRole = registryName;
                browseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Cursors"), Title = LocalizationManager.T("Personalization.Browse") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DeskCursorsEngine.SetCursor(capturedRole, dlg.FileName);
                        pathLabel.Text = Path.GetFileName(dlg.FileName);
                    }
                };
                row.Controls.Add(browseBtn);
                cursorCard.Controls.Add(row);
            }

            AddButtonGrid(cursorCard,
                (LocalizationManager.T("Personalization.ApplyCursors"), (_, _) => { DeskCursorsEngine.ApplyNow(); _statusLabel.Text = LocalizationManager.T("Personalization.CursorsAppliedStatus"); }),
                (LocalizationManager.T("Personalization.ResetCursors"), (_, _) => { DeskCursorsEngine.ResetAllToWindowsDefault(); if (_pageBuilders.TryGetValue("Personalization", out var rebuild)) rebuild(); }));

            // ---------- DeskSounds ----------
            var soundsCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskSounds"));
            AddText(soundsCard, LocalizationManager.T("Personalization.DeskSoundsIntro"));

            foreach (var (eventLabel, labelKey) in DeskSoundsEngine.Events)
            {
                var row = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Margin = new Padding(0, 1, 0, 10)
                };
                var details = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                details.Controls.Add(new Label
                {
                    Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody,
                    AutoSize = true, Margin = new Padding(0, 3, 8, 3)
                });
                var pathLabel = new Label
                {
                    Text = Path.GetFileName(DeskSoundsEngine.GetSound(eventLabel) ?? "") is { Length: > 0 } file
                        ? file : LocalizationManager.T("Personalization.NotSet"),
                    ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false,
                    AutoEllipsis = true, Width = 340, Height = 23, TextAlign = ContentAlignment.MiddleLeft
                };
                details.Controls.Add(pathLabel);
                row.Controls.Add(details);
                var actions = ActionGrid();
                actions.Controls.Add(StudioButton(LocalizationManager.T("Personalization.Browse"), () =>
                {
                    using var dialog = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Wav"), Title = LocalizationManager.T("Personalization.Browse") };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    DeskSoundsEngine.SetSound(eventLabel, dialog.FileName);
                    pathLabel.Text = Path.GetFileName(dialog.FileName);
                }, 126));
                actions.Controls.Add(StudioButton(LocalizationManager.T("Personalization.Preview"), () =>
                {
                    var path = DeskSoundsEngine.GetSound(eventLabel);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) DeskSoundsEngine.Preview(path);
                }, 140));
                actions.Controls.Add(StudioButton(LocalizationManager.T("Personalization.Clear"), () =>
                {
                    DeskSoundsEngine.SetSound(eventLabel, null);
                    pathLabel.Text = LocalizationManager.T("Personalization.NotSet");
                }, 112));
                row.Controls.Add(actions);
                soundsCard.Controls.Add(row);
                FitActionGrid(soundsCard, actions, 214, 32, 40);
            }

            // (the .theme packages are part of the single "Themes" card built by BuildWindowsAppearance)

            // ---------- Lock Screen Background ----------
            var lockScreenCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionLockScreen"));
            AddText(lockScreenCard, LocalizationManager.T("Personalization.LockScreenIntro"));
            AddText(lockScreenCard, string.Format(LocalizationManager.T("Personalization.LockScreenEditionNote"), LockScreenEngine.GetWindowsEdition()));
            AddButtonGrid(lockScreenCard,
            (StudioText("Windows lock-screen settings", "Ρυθμίσεις κλειδώματος Windows"), (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:lockscreen") { UseShellExecute = true }); }
                catch (Exception ex) { ShowStatus(ex.Message, StatusKind.Error); }
            }),
            (LocalizationManager.T("Personalization.LockGifFrame"), async (_, _) =>
            {
                string gif;
                using (var dlg = new OpenFileDialog { Filter = "GIF (*.gif)|*.gif", Title = LocalizationManager.T("Personalization.LockGifFrame") }) { if (dlg.ShowDialog(this) != DialogResult.OK) return; gif = dlg.FileName; }
                var info = LockScreenGif.Validate(gif);
                if (!info.Ok) { MessageBox.Show(this, info.Reason, LocalizationManager.T("Personalization.LockGifFrame"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                using var frameDialog = new LockScreenFrameDialog(gif, info.Frames);
                if (frameDialog.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, LocalizationManager.T("Personalization.LockGifWarn"), LocalizationManager.T("Personalization.LockGifFrame"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                var png = LockScreenGif.ExtractFrame(gif, frameDialog.SelectedFrame, System.IO.Path.Combine(AppPaths.AppDataFolder, "lockscreen"));
                if (png == null) return;
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(png, out error));
                if (IsDisposed) return;
                if (ok) { var st = AppSettings.Load(); st.LockScreenFollowsWallpaper = false; st.Save(); }
                _statusLabel.Text = ok ? LocalizationManager.T("Personalization.LockScreenSuccess") : error == "cancelled" ? LocalizationManager.T("Personalization.LockScreenCancelled") : LocalizationManager.T("Personalization.LockScreenFailed");
            }),
            (LocalizationManager.T("Personalization.SetLockScreenImage"), async (_, _) =>
            {
                string fileName;
                using (var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Images"), Title = LocalizationManager.T("Personalization.SetLockScreenImage") })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    fileName = dlg.FileName;
                }
                // Εκτός UI thread: το helper περιμένει έως 15s (UAC prompt) — στο UI thread πάγωνε όλο το
                // παράθυρο ΚΑΙ (λόγω του global mouse hook του quick-hide) το ποντίκι όλου του συστήματος.
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(fileName, out error));
                if (IsDisposed) return;
                if (ok)
                {
                    var settings = AppSettings.Load();
                    settings.LockScreenFollowsWallpaper = false;
                    settings.Save();
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenSuccess");
                }
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }),
            // MD roadmap item: αντί για τυχαία επιλεγμένη εικόνα, ακριβές στιγμιότυπο του τρέχοντος
            // ζωντανού wallpaper (waves/βίντεο + overlay) ως οθόνη κλειδώματος — με "self-heal" μέσω
            // ενός Scheduled Task (βλ. LockScreenEngine.EnsureSelfHealTask) που το ξαναφαρμόζει αυτόματα
            // σε κάθε logon αν τα Windows/ένα update επαναφέρουν το registry key.
            (LocalizationManager.T("Personalization.MatchLockScreenToWallpaper"), async (_, _) =>
            {
                if (!WallpaperHostEngine.Instance.IsRunning)
                {
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenNeedsWallpaper");
                    return;
                }
                string snapshotPath = Path.Combine(AppPaths.AppDataFolder, "lockscreen_wallpaper.png");
                bool captured = await WallpaperHostEngine.Instance.CaptureSnapshotAsync(snapshotPath);
                if (IsDisposed) return;
                if (!captured)
                {
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCaptureFailed");
                    return;
                }
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(snapshotPath, out error));
                if (IsDisposed) return;
                if (ok)
                {
                    var settings = AppSettings.Load();
                    settings.LockScreenFollowsWallpaper = true;
                    settings.LockScreenSnapshotPath = snapshotPath;
                    settings.Save();
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenSuccess");
                }
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }),
            // ΔΙΟΡΘΩΣΗ "έβαλα άλλη οθόνη κλειδώματος και δεν μπορώ να επαναφέρω την προεπιλεγμένη":
            // δεν υπήρχε καμία λειτουργία επαναφοράς - βλ. LockScreenEngine.ClearLockScreenRegistry.
            (LocalizationManager.T("Personalization.ResetLockScreenImage"), async (_, _) =>
            {
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TryResetLockScreenElevated(out error));
                if (IsDisposed) return;
                if (ok)
                {
                    var settings = AppSettings.Load();
                    settings.LockScreenFollowsWallpaper = false;
                    settings.Save();
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenResetSuccess");
                }
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }));

            SetPage("Page.Personalization.Title", panel);
        }

        private void ShowSettings()
        {
            SaveWindowOpacity();
            _currentPageKey = "Settings";
            var settings = new SettingsView();
            SetPage("Page.Settings.Title", settings);
        }

        public void OpenCommandPalette()
        {
            if (_universalSearch != null) { _universalSearch.Focus(); _universalSearch.SelectAll(); }
            else ShowCommandPalette();
        }

        // 2.6.0: DeskStrip has its own page (it used to be the middle of the long Personalization page).
        private void ShowDeskStrip()
        {
            _currentPageKey = "DeskStrip";
            var panel = CreatePagePanel();
            // ---------- DeskStrip ----------
            var deskStripCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskStrip"));
            AddText(deskStripCard, LocalizationManager.T("Personalization.DeskStripIntro"));

            var deskStripToggle = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Personalization.EnableDeskStrip"), Checked = DeskStripHostEngine.Instance.IsEnabled, BackColor = UiTheme.Surface, Margin = new Padding(0, 4, 0, 10) };
            deskStripCard.Controls.Add(deskStripToggle);

            // Ρυθμίσεις dock (θέση, auto-hide, μέγεθος, μεγέθυνση, τρέχουσες εφαρμογές, προεπισκοπήσεις) — εφαρμόζονται αμέσως.
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var dockCfg = DeskStripHostEngine.Instance.GetSettings();
            var displays = Screen.AllScreens;
            var ids = displays.Select(screen => screen.DeviceName).Prepend("").ToArray();
            var labels = displays.Select(screen => screen.DeviceName).Prepend(Ld("Κύρια οθόνη", "Primary monitor")).ToArray();
            FxCombo(deskStripCard, Ld("Οθόνη DeskStrip", "DeskStrip monitor"), labels,
                labels[Math.Max(0, Array.IndexOf(ids, dockCfg.Monitor ?? ""))],
                label => DeskStripHostEngine.Instance.UpdateSettings(cfg => cfg.Monitor = ids[Array.IndexOf(labels, label)]));

            var posValues = new[] { DockPosition.Bottom, DockPosition.Top, DockPosition.Left, DockPosition.Right };
            var posLabels = new[] { Ld("Κάτω", "Bottom"), Ld("Πάνω", "Top"), Ld("Αριστερά", "Left"), Ld("Δεξιά", "Right") };
            deskStripCard.Controls.Add(new Label { AutoSize = true, Text = Ld("Θέση στην οθόνη", "Position on screen"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Margin = new Padding(0, 2, 0, 2) });
            var posCombo = new FlatComboBox { Width = 220, Margin = new Padding(0, 0, 0, 8) };
            posCombo.SetItems(posLabels, posLabels[Math.Max(0, Array.IndexOf(posValues, dockCfg.Position))]);
            posCombo.SelectedIndexChanged += (_, _) => DeskStripHostEngine.Instance.UpdateSettings(c => c.Position = posValues[Math.Max(0, posCombo.SelectedIndex)]);
            deskStripCard.Controls.Add(posCombo);

            FlatCheckBox DockCheck(string text, bool value, Action<DeskStripSettings, bool> apply)
            {
                var cb = new FlatCheckBox { Text = text, AutoSize = true, Checked = value, BackColor = UiTheme.Surface, Margin = new Padding(0, 2, 0, 4) };
                cb.CheckedChanged += (_, _) => DeskStripHostEngine.Instance.UpdateSettings(c => apply(c, cb.Checked));
                deskStripCard.Controls.Add(cb);
                return cb;
            }
            DockCheck(Ld("Αυτόματη απόκρυψη (εμφάνιση από την άκρη της οθόνης)", "Auto-hide (reveal from the screen edge)"), dockCfg.AutoHide, (c, v) => c.AutoHide = v);
            DockCheck(Ld("Μεγέθυνση εικονιδίων στο hover", "Magnify icons on hover"), dockCfg.Magnify, (c, v) => c.Magnify = v);
            DockCheck(Ld("Εμφάνιση τρεχουσών εφαρμογών", "Show running apps"), dockCfg.ShowRunning, (c, v) => c.ShowRunning = v);
            DockCheck(Ld("DeskStrip σε κάθε οθόνη", "DeskStrip on every monitor"), dockCfg.AllMonitors,
                (settings, value) => settings.AllMonitors = value);
            DockCheck(Ld("Παράθυρα μόνο από την οθόνη του DeskStrip", "Only windows on the DeskStrip monitor"),
                dockCfg.OnlyLocalWindows, (c, v) => c.OnlyLocalWindows = v);
            DockCheck(Ld("Απόκρυψη σε πλήρη οθόνη", "Hide over fullscreen windows"),
                dockCfg.HideOnFullscreen, (c, v) => c.HideOnFullscreen = v);
            DockCheck(Ld("Προεπισκόπηση παραθύρου στο hover", "Window preview on hover"), dockCfg.ShowPreviews, (c, v) => c.ShowPreviews = v);
            DockCheck(Ld("Ένδειξη τρέχοντος workspace στο DeskStrip", "Show active workspace in DeskStrip"),
                dockCfg.ShowWorkspaceLabel, (c, v) => c.ShowWorkspaceLabel = v);
            var sizeLabel = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Margin = new Padding(0, 6, 0, 2) };
            void ShowSize(int v) => sizeLabel.Text = Ld($"Μέγεθος εικονιδίων: {v}px", $"Icon size: {v}px");
            ShowSize(dockCfg.IconSize);
            deskStripCard.Controls.Add(sizeLabel);
            var sizeSlider = new FlatSlider { Minimum = 28, Maximum = 72, Value = Math.Clamp(dockCfg.IconSize, 28, 72), Width = 300, Height = 28, Margin = new Padding(0, 0, 0, 10) };
            var iconSizeTimer = new System.Windows.Forms.Timer { Interval = 140 };
            int appliedSize = sizeSlider.Value, pendingSize = appliedSize;
            void ApplyIconSize()
            {
                iconSizeTimer.Stop();
                if (appliedSize == pendingSize) return;
                appliedSize = pendingSize;
                DeskStripHostEngine.Instance.UpdateSettings(c => c.IconSize = appliedSize);
            }
            iconSizeTimer.Tick += (_, _) => ApplyIconSize();
            sizeSlider.ValueChanged += (_, _) =>
            {
                pendingSize = sizeSlider.Value;
                ShowSize(pendingSize);
                iconSizeTimer.Stop();
                iconSizeTimer.Start();
            };
            panel.Disposed += (_, _) => { ApplyIconSize(); iconSizeTimer.Dispose(); };
            deskStripCard.Controls.Add(sizeSlider);
            var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            fontRow.Controls.Add(new Label
            {
                AutoSize = true, Text = Ld("Μέγεθος τίτλων dock:", "Dock label size:"),
                ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 12, 0)
            });
            var fontPresets = new[] { 85, 100, 110, 125, 150 };
            var fontLabels = fontPresets.Select(n => n + "%").ToArray();
            var dockFontScale = new FlatComboBox { Width = 142 };
            int fontIndex = Array.IndexOf(fontPresets, dockCfg.LabelFontScalePercent);
            dockFontScale.SetItems(fontLabels, fontLabels[fontIndex < 0 ? 1 : fontIndex]);
            dockFontScale.SelectedIndexChanged += (_, _) =>
                DeskStripHostEngine.Instance.UpdateSettings(c => c.LabelFontScalePercent = fontPresets[Math.Max(0, dockFontScale.SelectedIndex)]);
            fontRow.Controls.Add(dockFontScale);
            deskStripCard.Controls.Add(fontRow);

            var pinnedList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            deskStripCard.Controls.Add(pinnedList);

            // Ίδια διόρθωση flicker με RefreshActiveList/RefreshActiveContainers: skip το rebuild
            // όταν η λίστα δεν άλλαξε — πέρα από το flicker, γλιτώνει και το άσκοπο save/restore
            // scroll σε κάθε tick.
            string? lastPinnedSignature = null;
            void RefreshPinnedApps()
            {
                var pinned = DeskStripHostEngine.Instance.GetPinnedApps();
                string signature = string.Join("|", pinned);
                if (signature == lastPinnedSignature) return;
                lastPinnedSignature = signature;

                // Bug fix: αυτή η συνάρτηση καλείται κάθε 2 δευτερόλεπτα από timer (βλ.
                // deskStripRefreshTimer παρακάτω) ενώ ο χρήστης μπορεί να έχει ήδη κυλήσει
                // ΠΟΛΥ πιο κάτω στη σελίδα (π.χ. στο "Φόντο οθόνης κλειδώματος"). Το
                // Controls.Clear()+rebuild ενός FlowLayoutPanel μέσα σε ένα AutoScroll container
                // επαναφέρει σιωπηλά τη θέση κύλισης — αναφέρθηκε ως "autoscrolling πίσω στα
                // DeskSounds". Λύση: αποθήκευση/επαναφορά της θέσης κύλισης της ΣΕΛΙΔΑΣ γύρω από
                // την ανακατασκευή (το AutoScrollPosition GET επιστρέφει αρνητικές τιμές, το SET
                // περιμένει θετικές — WinForms ιδιαιτερότητα).
                var savedScroll = panel.AutoScrollPosition;
                pinnedList.SuspendLayout();
                DisposeChildren(pinnedList);
                if (pinned.Count == 0)
                {
                    pinnedList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoPinnedApps"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var appPath in pinned)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = Path.GetFileNameWithoutExtension(appPath), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var removeBtn = new HoverButton { Text = "✕", Width = 36, Height = 28, Location = new Point(444, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    removeBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = appPath;
                    removeBtn.Click += (_, _) => { DeskStripHostEngine.Instance.UnpinApp(capturedPath); RefreshPinnedApps(); };
                    row.Controls.Add(removeBtn);
                    pinnedList.Controls.Add(row);
                }
                ApplySkinColors(pinnedList);
                pinnedList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }

            deskStripToggle.CheckedChanged += (_, _) =>
            {
                if (deskStripToggle.Checked) DeskStripHostEngine.Instance.Enable(); else DeskStripHostEngine.Instance.Disable();
                RefreshPinnedApps();
            };

            AddButtonGrid(deskStripCard, (LocalizationManager.T("Personalization.PinApp"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Apps"), Title = LocalizationManager.T("Personalization.PinApp") };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    DeskStripHostEngine.Instance.PinApp(dlg.FileName);
                    deskStripToggle.Checked = true;
                    RefreshPinnedApps();
                }
            }));

            RefreshPinnedApps();
            var deskStripRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            deskStripRefreshTimer.Tick += (_, _) => RefreshPinnedApps();
            RunWhilePageVisible(panel, deskStripRefreshTimer);
            panel.Disposed += (_, _) => deskStripRefreshTimer.Dispose();


            BuildDesktopMaterialCard(panel, DesktopSurface.DeskStrip);
            var toolbar = AddCardSection(panel, StudioText("Desktop toolbar", "Γραμμή εργαλείων επιφάνειας"));
            var toolbarSettings = AppSettings.Load();
            toolbar.Controls.Add(SettingsView.Checkbox(StudioText("Show toolbar", "Εμφάνιση γραμμής εργαλείων"), toolbarSettings.DesktopToolbarEnabled,
                enabled => { var settings = AppSettings.Load(); settings.DesktopToolbarEnabled = enabled; settings.Save(); DesktopToolbar.RefreshSettings(); }));
            toolbar.Controls.Add(SettingsView.Checkbox(StudioText("Toolbar on every monitor", "Γραμμή εργαλείων σε κάθε οθόνη"), toolbarSettings.DesktopToolbarAllMonitors,
                enabled => { var settings = AppSettings.Load(); settings.DesktopToolbarAllMonitors = enabled; settings.Save(); DesktopToolbar.RefreshSettings(); }));
            toolbar.Controls.Add(SettingsView.Checkbox(StudioText("Use the Opsis taskbar and Start menu", "Χρήση γραμμής εργασιών και Έναρξης Opsis"),
                toolbarSettings.ReplaceWindowsTaskbar,
                enabled => { var settings = AppSettings.Load(); settings.ReplaceWindowsTaskbar = enabled; settings.Save(); ReplacementTaskbar.RefreshSettings(); }));
            toolbar.Controls.Add(StudioButton(StudioText("App launcher · Ctrl+Alt+Shift+K", "Εκκινητής · Ctrl+Alt+Shift+K"), DesktopLauncher.Open, 265));
            SetPage("Personalization.SectionDeskStrip", panel);
        }

        // 2.6.0: DeskContainers (and the double-click "hide desktop icons") have their own page; DeskZones keeps only the window zones.
        private void ShowDeskContainers()
        {
            _currentPageKey = "DeskContainers";
            if (_windowSkin.HasContainerWorkspace() && MotionDesk.Containers.ContainerHost.Current is { } host)
            {
                ShowContainerWorkspace(host);
                return;
            }
            var panel = CreatePagePanel();
            FillClassicContainersPage(panel);
            SetPage("DeskZones.SectionContainers", panel);
        }

        private void FillClassicContainersPage(Control panel, bool includeContainerPicker = true)
        {
            // Ξεχωριστή, δεύτερη λειτουργία στην ίδια σελίδα: DeskContainers — η "πραγματική"
            // έννοια των Fences (ομαδοποίηση αρχείων/φακέλων/συντομεύσεων, όχι παραθύρων).
            AddSection(panel, LocalizationManager.T("DeskZones.SectionContainers"));
            AddText(panel, LocalizationManager.T("DeskZones.ContainersIntro"));

            // Ζητήθηκε ρητά: ο μηχανισμός "διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας
            // κρύβει/επαναφέρει τα εικονίδια" (DesktopIconVisibilityEngine) υπήρχε ήδη πλήρως
            // λειτουργικός αλλά ήταν πάντα ενεργός χωρίς καμία ρύθμιση ορατή στο χρήστη. Τώρα
            // μπορεί να απενεργοποιηθεί από εδώ (ενεργοποιείται/απενεργοποιείται ζωντανά, χωρίς
            // restart της εφαρμογής).
            // Επιλογή εικονιδίων που ΜΕΝΟΥΝ όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας.
            string LdKeep(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var keepIconsBtn = new HoverButton { Text = LdKeep("Εικονίδια που μένουν ορατά…", "Icons that stay visible…"), Width = 260, Height = 32, Margin = new Padding(0, 0, 0, 10), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
            keepIconsBtn.FlatAppearance.BorderSize = 0;
            keepIconsBtn.Click += (_, _) => ShowKeepIconsDialog();
            var quickHideCheck = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("DeskZones.QuickHideDoubleClick"), Checked = AppSettings.Load().QuickHideDoubleClickEnabled, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 0, 0, 10) };
            keepIconsBtn.Enabled = quickHideCheck.Checked;
            quickHideCheck.CheckedChanged += (_, _) =>
            {
                keepIconsBtn.Enabled = quickHideCheck.Checked;
                var a = AppSettings.Load(); a.QuickHideDoubleClickEnabled = quickHideCheck.Checked; a.Save();
                if (quickHideCheck.Checked) MotionDesk.Widgets.DesktopIconVisibilityEngine.Instance.Start();
                else MotionDesk.Widgets.DesktopIconVisibilityEngine.Instance.Stop();
            };
            panel.Controls.Add(quickHideCheck);
            panel.Controls.Add(keepIconsBtn);

            // DeskContainers engine: only its on/off control lives here; every other DeskContainers setting is in the three-bars menu of a container.
            BuildDesktopMaterialCard(panel, DesktopSurface.Containers);
            BuildContainersSettings(panel, includeContainerPicker);
        }

        // Theme page: the base theme (Follow Windows / Light / Dark / Midnight / Forest / Dawn / High contrast), applied live. The app has no "Glass" theme any more (decision 2026-11-18);
        // the GPU glass of widgets, DeskStrip and containers is separate (Settings -> Appearance -> Advanced).
        private void ShowTheme()
        {
            _currentPageKey = "Theme";
            var panel = CreatePagePanel();
            var card = AddCardSection(panel, OpsisStrings.T("ui.theme.title"));
            string[] modes = { "Follow", "Light", "Dark", "Teal", "Midnight", "Forest", "Dawn", "Contrast", "SignatureGlass" };
            string[] labels = { LocalizationManager.T("Settings.ThemeFollow"), LocalizationManager.T("Settings.ThemeLight"), LocalizationManager.T("Settings.ThemeDark"), "Opsis teal", "Midnight", "Forest", "Dawn", "High contrast", "Signature Glass" };
            int idx = Array.IndexOf(modes, AppSettings.Load().ThemeMode); if (idx < 0) idx = 0;
            FxCombo(card, LocalizationManager.T("Settings.Theme"), labels, labels[idx], v => ThemeManager.SetMode(modes[Math.Max(0, Array.IndexOf(labels, v))]));
            AddText(card, StudioText("Apply a coordinated glass appearance to desktop widgets, DeskStrip and containers. Individual material settings remain available in their respective pages.",
                "Εφάρμοσε ενιαία εμφάνιση glass σε widgets, DeskStrip και containers. Οι επιμέρους ρυθμίσεις υλικών παραμένουν στις αντίστοιχες καρτέλες."));
            var signature = AddButton(card, StudioText("Apply Signature Glass to desktop", "Signature Glass στην επιφάνεια"), (_, _) =>
            {
                if (MessageBox.Show(this, StudioText("Replace current widget, DeskStrip and container material settings with Signature Glass?",
                    "Να αντικατασταθούν οι τρέχουσες ρυθμίσεις υλικών των widgets, DeskStrip και containers με Signature Glass;"),
                    "Signature Glass", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    DesktopMaterials.ApplySignature();
            });
            signature.AccessibleDescription = StudioText("Changes the shared desktop material settings after confirmation.",
                "Αλλάζει τις κοινές ρυθμίσεις υλικών της επιφάνειας εργασίας μετά από επιβεβαίωση.");
            AddSkinSelector(card, this);
            BuildGearThemeCards(panel);
            _pageTitle.Text = OpsisStrings.T("ui.theme.title");
            Flicker.BatchRedraw(_content, () => SetPageCore(panel));
        }

        private void ShowCommandPalette(string initial = "")
        {
            var paletteArea = Screen.FromControl(this).WorkingArea;
            bool compactPalette = paletteArea.Width < 1100 || paletteArea.Height < 800;
            using var dialog = new MotionDesk.UI.AppDialog
            {
                Text = LocalizationManager.T("Command.PaletteTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = compactPalette ? new Size(480, 350) : new Size(640, 470),
                Padding = compactPalette ? new Padding(10, 8, 10, 8) : new Padding(18, 16, 18, 16),
                BackColor = UiTheme.PageBackground,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new Size(360, 280),
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            var box = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", compactPalette ? 11 : 14),
                PlaceholderText = LocalizationManager.T("Command.PalettePlaceholder"),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", compactPalette ? 9.5f : 11),
                BackColor = UiTheme.PageBackground,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = compactPalette ? 24 : 30
            };
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var bg = new SolidBrush(selected ? UiTheme.SurfaceHover : UiTheme.PageBackground);
                e.Graphics.FillRectangle(bg, e.Bounds);
                using var textBrush = new SolidBrush(selected ? UiTheme.AccentCyan : UiTheme.TextPrimary);
                e.Graphics.DrawString(list.Items[e.Index].ToString(), e.Font!, textBrush, e.Bounds.X + 10, e.Bounds.Y + 5);
            };

            // ΔΙΟΡΘΩΣΗ REQ-008: οι εντολές ΗΤΑΝ ταυτόχρονα το εμφανιζόμενο κείμενο ΚΑΙ το κλειδί
            // αντιστοίχισης στο switch — πάντα αγγλικά, ανεξάρτητα από την επιλεγμένη γλώσσα.
            // Τώρα κάθε εντολή έχει ξεχωριστό σταθερό "id" (για το switch) από το μεταφρασμένο
            // label που βλέπει ο χρήστης (για εμφάνιση/αναζήτηση), μέσω ενός dictionary label->id.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            var commandDefs = new (string Id, string Label)[]
            {
                ("Dashboard", LocalizationManager.T("Nav.Dashboard")),
                ("WidgetGallery", LocalizationManager.T("Command.WidgetGallery")),
                ("DeskZones", LocalizationManager.T("Nav.DeskZones")),
                ("DeskContainers", LocalizationManager.T("DeskZones.SectionContainers")),
                ("DeskStrip", LocalizationManager.T("Personalization.SectionDeskStrip")),
                ("WallpaperStudio", LocalizationManager.T("Nav.Wallpaper")),
                ("DeskMusicVisualizer", "Desk Music Visualizer"),
                ("Profiles", LocalizationManager.T("Nav.Profiles")),
                ("Performance", LocalizationManager.T("Nav.Performance")),
                ("Automation", LocalizationManager.T("Nav.Automation")),
                ("Personalization", LocalizationManager.T("Nav.Personalization")),
                ("Settings", LocalizationManager.T("Nav.Settings")),
                ("About", LocalizationManager.T("Nav.About")),
                ("AudioEnhancement", LocalizationManager.T("Nav.AudioEnhancement")),
                ("EnableWallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper")),
                ("DisableWallpaper", LocalizationManager.T("Command.DisableWallpaper")),
                ("WorkProfile", string.Format(profileFmt, "Work")),
                ("GamingProfile", string.Format(profileFmt, "Gaming")),
                ("FocusProfile", string.Format(profileFmt, "Focus")),
                ("OpsisTaskbarDesigner", StudioText("Taskbar Visual Designer", "Οπτικός σχεδιαστής Taskbar")),
                ("OpsisExplorerStudio", StudioText("Explorer Studio", "Στούντιο Explorer")),
                ("OpsisStartStudio", StudioText("Start Studio", "Στούντιο Έναρξης")),
                ("OpsisScreensaverStudio", StudioText("Screensaver Studio", "Στούντιο προφύλαξης οθόνης")),
                ("OpsisEcosystem", StudioText("Unified Opsis Mod and Plugin Gallery", "Κοινή Gallery mods και plugins")),
            };
            // Οι σελίδες των plugin (π.χ. News Crawler) εμφανίζονται κι εδώ.
            var commandList = commandDefs.ToList();
            foreach (var pluginPage in Services.PluginHost.Instance.GetNavigationItems())
                commandList.Add(("plugin:" + pluginPage.Key, Services.PluginHost.TitleOf(pluginPage)));
            // Command Palette 2.0: all implemented core modules are searchable
            // alongside plugin page commands; planned modules are not advertised
            // as executable, and Gallery metadata never executes third-party code.
            foreach (var module in OpsisModCore.Catalog.Where(x => x.HasNativeImplementation))
                commandList.Add(("feature:" + module.Id, "Opsis · " + module.Label));
            var galleryCommands = new Dictionary<string, OpsisEcosystemGallery.Item>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var asset in OpsisEcosystemGallery.Discover().Take(150))
                {
                    var id = "gallery:" + asset.PluginId + ":" + asset.Id;
                    galleryCommands.TryAdd(id, asset);
                    commandList.Add((id, "Gallery · " + asset.Title + " (" + asset.Category + ")"));
                }
            }
            catch (Exception ex) { RotatingLogger.Write("Command Gallery indexing: " + ex.Message); }
            // v2.0.0 (t35, "Start Menu replacement" σε ασφαλή μορφή): το Ctrl+K γίνεται και app
            // launcher — ΧΩΡΙΣ να πειράζει το πραγματικό Start. Πρώτα οι καρφιτσωμένες της taskbar,
            // μετά οι συντομεύσεις του Start Menu· Enter τις εκκινεί (όπως ένα διπλό-κλικ σε .lnk).
            var appByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                };
                var lnks = TaskbarPinsReader.GetPinnedAppPaths().Concat(roots.Where(Directory.Exists).SelectMany(r => Directory.EnumerateFiles(r, "*.lnk", SearchOption.AllDirectories)));
                foreach (var lnk in lnks)
                {
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || commandList.Any(c => c.Label.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    appByLabel.TryAdd(name, lnk);
                }
            }
            catch (Exception) { }
            foreach (var appLabel in appByLabel.Keys) commandList.Add(("app:" + appLabel, appLabel));
            // Roadmap #62/#63: display the actual keyboard shortcut and keep an
            // editable, bounded LOCAL recent-command list (no filenames/arguments).
            var shortcutHints = new Dictionary<string, string>
            {
                ["Dashboard"] = "Ctrl+1", ["WidgetGallery"] = "Ctrl+2",
                ["DeskZones"] = "Ctrl+3", ["WallpaperStudio"] = "Ctrl+4",
                ["Performance"] = "Ctrl+5", ["Settings"] = "Ctrl+6",
                ["Profiles"] = "Ctrl+7", ["Automation"] = "Ctrl+8",
                ["About"] = "Ctrl+9", ["Personalization"] = "Ctrl+0"
            };
            var displayCommands = commandList.Select(c => (c.Id,
                Label: c.Label + (shortcutHints.TryGetValue(c.Id, out string? hint) ? "    ·    " + hint : ""))).ToArray();
            var idByLabel = displayCommands.GroupBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var recentIds = CommandHistoryStore.RecentIds();
            string[] OrderLabels() => displayCommands.OrderBy(c =>
            {
                int index = Array.IndexOf(recentIds.ToArray(), c.Id);
                return index < 0 ? int.MaxValue : index;
            }).Select(c => c.Label).ToArray();
            var allLabels = OrderLabels();
            list.Items.AddRange(allLabels);
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            var historyMenu = new ContextMenuStrip();
            var removeHistory = new ToolStripMenuItem(StudioText("Remove command from history", "Αφαίρεση εντολής από το ιστορικό"));
            historyMenu.Items.Add(removeHistory);
            historyMenu.Opening += (_, args) =>
            {
                if (list.SelectedItem is not string text || !idByLabel.TryGetValue(text, out string? id) || !recentIds.Contains(id))
                    args.Cancel = true;
            };
            removeHistory.Click += (_, _) =>
            {
                if (list.SelectedItem is not string selected || !idByLabel.TryGetValue(selected, out string? id)) return;
                CommandHistoryStore.Remove(id);
                recentIds = CommandHistoryStore.RecentIds();
                allLabels = OrderLabels();
                list.Items.Clear(); list.Items.AddRange(allLabels.Where(c => PageSearch.Matches(c, box.Text)).ToArray());
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
            list.ContextMenuStrip = historyMenu;
            dialog.FormClosed += (_, _) => historyMenu.Dispose();

            void Execute()
            {
                if (list.SelectedItem is not string label || !idByLabel.TryGetValue(label, out var id)) return;
                try { CommandHistoryStore.RecordUse(id); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RotatingLogger.Write("Command history not saved: " + ex.GetType().Name); }
                switch (id) {
                    case "Dashboard": NavigateTo("Dashboard"); break; case "WidgetGallery": NavigateTo("Widgets"); break; case "DeskZones": NavigateTo("DeskZones"); break; case "DeskContainers": NavigateTo("DeskContainers"); break; case "DeskStrip": NavigateTo("DeskStrip"); break;
                    case "DeskMusicVisualizer": NavigateTo("DeskMusicVisualizer"); break;
                    case "WallpaperStudio": NavigateTo("Wallpaper"); break; case "Profiles": NavigateTo("Profiles"); break;
                    case "Performance": NavigateTo("Performance"); break; case "Automation": NavigateTo("Automation"); break;
                    case "Personalization": NavigateTo("Personalization"); break; case "Settings": NavigateTo("Settings"); break;
                    case "About": NavigateTo("About"); break;
                    case "AudioEnhancement": NavigateTo("AudioEnhancement"); break;
                    case "OpsisTaskbarDesigner":
                        using (var form = new OpsisTaskbarDesignerForm()) form.ShowDialog(this);
                        break;
                    case "OpsisExplorerStudio":
                        using (var form = new OpsisExplorerStudioForm()) form.ShowDialog(this);
                        break;
                    case "OpsisStartStudio":
                        using (var form = new StartStudioForm()) form.ShowDialog(this);
                        break;
                    case "OpsisScreensaverStudio":
                    case "OpsisEcosystem": NavigateTo("Personalization"); break;
                    case string featureId when featureId.StartsWith("feature:", StringComparison.Ordinal):
                        NavigateTo("Personalization"); break;
                    case string galleryId when galleryId.StartsWith("gallery:", StringComparison.Ordinal):
                        if (galleryCommands.TryGetValue(galleryId, out var asset))
                        {
                            try
                            {
                                if (asset.Type.Equals("widget", StringComparison.OrdinalIgnoreCase))
                                    new OpsisCustomWidgetWindow(OpsisEcosystemGallery.ReadWidgetContribution(asset)).Show(this);
                                else if (asset.Type.Equals("wallpaper", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (MessageBox.Show(this, StudioText("Apply this plugin wallpaper?", "Εφαρμογή αυτού του wallpaper;"),
                                        "Opsis Gallery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                        OpsisEcosystemGallery.ApplyStaticWallpaper(asset);
                                }
                                else if (asset.Type.Equals("theme", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (MessageBox.Show(this, StudioText("Import this theme?", "Εισαγωγή αυτού του θέματος;"),
                                        "Opsis Gallery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                        OpsisEcosystemGallery.InstallThemeContribution(asset);
                                }
                                else NavigateTo("Personalization");
                            }
                            catch (Exception ex)
                            {
                                RotatingLogger.Write("Gallery command failed: " + ex.Message);
                                MessageBox.Show(this, ex.Message, "Opsis Gallery", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        break;
                    case string pluginId when pluginId.StartsWith("plugin:", StringComparison.Ordinal): NavigateTo(pluginId); break;
                    case string appId when appId.StartsWith("app:", StringComparison.Ordinal):
                        if (appByLabel.TryGetValue(appId[4..], out var appPath) && MotionDesk.Services.SafeLaunch.IsAllowed(appPath))
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true }); } catch (Exception) { }
                        break;
                    case "EnableWallpaper": WallpaperHostEngine.Instance.Enable(); break;
                    case "DisableWallpaper": WallpaperHostEngine.Instance.Disable(); break; case "WorkProfile": LoadProfileFromQuickButton("Work"); break;
                    case "GamingProfile": LoadProfileFromQuickButton("Gaming"); break; case "FocusProfile": LoadProfileFromQuickButton("Focus"); break;
                }
                dialog.Close();
            }
            box.TextChanged += (_, _) =>
            {
                list.Items.Clear();
                list.Items.AddRange(allLabels.Where(c => PageSearch.Matches(c, box.Text)).ToArray());
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
            list.DoubleClick += (_, _) => Execute();
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Execute(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) dialog.Close();
            };
            dialog.Controls.Add(list); dialog.Controls.Add(box);
            box.Text = initial;
            box.Focus();
            dialog.ShowDialog(this);
        }

        // 2.6.0: Profiles and Automation are ONE page now (a profile is what an automation rule switches to, e.g. "Gaming Mode: steam -> Gaming").
        // The old "Profiles" key stays as an alias so palette entries and links keep working.
        private void ShowProfiles() => NavigateTo("Automation");

        private void BuildProfilesSection(Control panel)
        {
            AddText(panel, LocalizationManager.T("Profiles.Intro"));
            var list = new ListBox { Width = 440, Height = 180, Font = new Font("Segoe UI", 10) };
            void Refresh() { list.Items.Clear(); foreach (var p in WorkspaceProfileService.ListProfiles()) list.Items.Add(p); }
            Refresh(); panel.Controls.Add(list);
            var name = new TextBox { Width = 240, Text = "Work" }; panel.Controls.Add(name);
            AddIconButtonGrid(panel,
                ("Save", LocalizationManager.T("Profiles.SaveCurrentAsProfile"), (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { WorkspaceProfileService.Save(name.Text.Trim()); Refresh(); } }),
                ("Restore", LocalizationManager.T("Profiles.LoadSelected"), (_, _) => { if (list.SelectedItem is string p && WorkspaceProfileService.Load(p)) { NavigateTo("Dashboard"); } }),
                ("Preview", StudioText("Preview for 30 seconds", "Προεπισκόπηση 30 δευτερολέπτων"), (_, _) =>
                {
                    if (list.SelectedItem is string p) PreviewProfileWithRollback(p);
                }),
                ("Add", LocalizationManager.T("Profiles.CreateUpdateDefaults"), (_, _) => { foreach (var p in new[] { "Work", "Gaming", "Focus" }) { if (!WorkspaceProfileService.ListProfiles().Contains(p, StringComparer.OrdinalIgnoreCase)) WorkspaceProfileService.Save(p); } Refresh(); }),
                ("Delete", LocalizationManager.T("Profiles.DeleteSelected"), (_, _) => { if (list.SelectedItem is string p && !p.Equals("Last Session", StringComparison.OrdinalIgnoreCase)) { WorkspaceProfileService.Delete(p); Refresh(); } }));
            var portability = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            portability.Controls.Add(StudioButton(StudioText("Export selected profile", "Εξαγωγή προφίλ"), () =>
            {
                if (list.SelectedItem is not string item) return;
                using var dialog = new SaveFileDialog { Filter = "OPSIS profile (*.json)|*.json", FileName = item + ".json" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { WorkspaceProfileService.ExportPortable(item, dialog.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "OPSIS", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }, width: 215));
            portability.Controls.Add(StudioButton(StudioText("Import with monitor mapping", "Εισαγωγή με αντιστοίχιση οθονών"), () =>
            {
                using var file = new OpenFileDialog { Filter = "OPSIS profile (*.json)|*.json", Multiselect = false };
                if (file.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var monitors = WorkspaceProfileService.PreviewPortableMonitors(file.FileName);
                    using var mapping = new ProfileMonitorMappingDialog(monitors);
                    if (mapping.ShowDialog(this) != DialogResult.OK) return;
                    var imported = WorkspaceProfileService.ImportPortable(file.FileName, mapping.Mapping);
                    Refresh(); list.SelectedItem = imported;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
                { MessageBox.Show(this, ex.Message, "OPSIS", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }, width: 273));
            panel.Controls.Add(portability);
            AddText(panel, StudioText("Presentation monitors and docking", "Οθόνες παρουσίασης και docking"));
            var dock = new CheckBox { AutoSize = true,
                Text = StudioText("Restore external windows when docking monitors reconnect (opt-in)",
                    "Επαναφορά εξωτερικών παραθύρων κατά την επανασύνδεση docking (προαιρετικό)"),
                Checked = AppSettings.Load().RestoreWindowsAfterDocking };
            dock.CheckedChanged += (_,_) => { var a=AppSettings.Load();a.RestoreWindowsAfterDocking=dock.Checked;a.Save(); };
            panel.Controls.Add(dock);
            foreach (var monitor in MonitorTopologyService.Current())
            {
                var saved = AppSettings.Load();
                var chosen = new CheckBox { AutoSize = true,
                    Text = StudioText("Presentation privacy on", "Απόκρυψη ιδιωτικών στοιχείων στην οθόνη") + " " + monitor.DeviceName,
                    Checked = saved.PresentationMonitorIds?.Contains(monitor.Id,StringComparer.OrdinalIgnoreCase) == true };
                chosen.CheckedChanged += (_,_) =>
                {
                    var a=AppSettings.Load();a.PresentationMonitorIds ??= new();
                    a.PresentationMonitorIds.RemoveAll(x=>x.Equals(monitor.Id,StringComparison.OrdinalIgnoreCase));
                    if(chosen.Checked)a.PresentationMonitorIds.Add(monitor.Id);
                    a.Save();WallpaperHostEngine.Instance.RefreshAll();
                };
                panel.Controls.Add(chosen);
            }
            // Which automation rules switch to a profile (so "Gaming Mode" for Steam is visible next to the profiles it uses, plan R.1).
            var ruleLines = AutomationService.Instance.Rules.Select(r => $"{r.Name}:  {r.TriggerProcess}  →  {r.Profile}" + (WorkspaceProfileService.ListProfiles().Contains(r.Profile) ? "" : "  (!)")).ToList();
            if (ruleLines.Count > 0) AddText(panel, string.Join("\n", ruleLines));
        }

        /// <summary>Keep the original workspace in a temporary profile and revert if not confirmed within 30 seconds.</summary>
        private void PreviewProfileWithRollback(string profile)
        {
            string backupName = "__opsis-preview-" + Guid.NewGuid().ToString("N");
            bool mayDeleteBackup = false;
            bool backupSaved = false;
            try
            {
                WorkspaceProfileService.Save(backupName);
                backupSaved = true;
                if (!WorkspaceProfileService.Load(profile))
                {
                    ReportStatus(StudioText("Profile could not be previewed.", "Αποτυχία προεπισκόπησης προφίλ."), StatusKind.Error);
                    return;
                }
                using var prompt = new MotionDesk.UI.AppDialog
                {
                    Text = StudioText("Confirm workspace", "Επιβεβαίωση χώρου εργασίας"),
                    StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(490, 160),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MinimizeBox = false, MaximizeBox = false
                };
                int seconds = 30;
                var note = new Label { Dock = DockStyle.Top, Height = 78, TextAlign = ContentAlignment.MiddleCenter,
                    Text = StudioText("Apply this workspace permanently? Automatically restores in 30 seconds.",
                        "Να διατηρηθεί το προφίλ; Αυτόματη επαναφορά σε 30 δευτερόλεπτα.") };
                var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft };
                var yes = new Button { Text = StudioText("Keep", "Διατήρηση"), DialogResult = DialogResult.Yes, Width = 120 };
                var no = new Button { Text = StudioText("Restore now", "Επαναφορά τώρα"), DialogResult = DialogResult.No, Width = 150 };
                actions.Controls.Add(no); actions.Controls.Add(yes);
                prompt.Controls.Add(note); prompt.Controls.Add(actions);
                prompt.AcceptButton = yes; prompt.CancelButton = no;
                using var countdown = new System.Windows.Forms.Timer { Interval = 1000 };
                countdown.Tick += (_, _) =>
                {
                    seconds--;
                    note.Text = StudioText($"Keep this workspace? Restoring in {seconds}s.",
                        $"Να διατηρηθεί το προφίλ; Επαναφορά σε {seconds} δευτ.");
                    if (seconds <= 0) { prompt.DialogResult = DialogResult.No; prompt.Close(); }
                };
                countdown.Start();
                bool keep = prompt.ShowDialog(this) == DialogResult.Yes;
                countdown.Stop();
                if (!keep)
                {
                    mayDeleteBackup = WorkspaceProfileService.Load(backupName);
                    if (!mayDeleteBackup)
                        ReportStatus(StudioText($"Automatic recovery failed. Backup profile preserved: {backupName}",
                            $"Η αυτόματη ανάκτηση απέτυχε. Το αντίγραφο διατηρήθηκε: {backupName}"), StatusKind.Error);
                }
                else mayDeleteBackup = true;
            }
            catch (Exception ex)
            {
                RotatingLogger.Write("Profile preview failed: " + ex.GetType().Name);
                mayDeleteBackup = backupSaved && WorkspaceProfileService.Load(backupName);
                if (backupSaved && !mayDeleteBackup)
                    ReportStatus(StudioText($"Recovery backup was retained: {backupName}",
                        $"Το αντίγραφο ανάκτησης διατηρήθηκε: {backupName}"), StatusKind.Error);
            }
            finally { if (mayDeleteBackup) WorkspaceProfileService.Delete(backupName); }
        }

        // Ενοποιημένη καρτέλα: πρώην ξεχωριστά "System Monitor" + "Performance Center" (το
        // δεύτερο ήταν σχεδόν πανομοιότυπο με το πρώτο) -> τώρα ΜΙΑ καρτέλα, με το ίδιο live
        // hardware περιεχόμενο, κάτω από τον τίτλο/εικονίδιο "Απόδοση".
        private void ShowPerformance()
        {
            _currentPageKey = "Performance";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Performance.Intro"));
            var cpu = AddMeter(panel, LocalizationManager.T("Performance.CpuLabel"), 0);
            var ram = AddMeter(panel, LocalizationManager.T("Performance.MemoryLabel"), 0);
            // GPU (φόρτος) — ζητήθηκε ρητά "βελτιώσεις για ram cpu gpu usage": το Performance page
            // έδειχνε μόνο CPU/RAM, ενώ το System Monitor widget έχει ήδη GPU load/temp μέσω
            // GpuMonitorService. Ίδιο μοτίβο "ζέσταμα σε background thread" με το widget (βλ.
            // WidgetEngine.InitializeNativeSystemMonitor) ώστε να ΜΗΝ ξαναεισάγουμε το ίδιο
            // UI-thread hitch που διορθώθηκε εκεί.
            var gpu = AddMeter(panel, LocalizationManager.T("Performance.GpuLabel"), 0);
            gpu.SetValue(0, LocalizationManager.T("Performance.GpuLoading"));
            var gpuMonitor = GpuMonitorService.Instance;
            _ = gpuMonitor.GetSnapshot();
            // ΔΙΟΡΘΩΣΗ "όλα τα πλαίσια εδώ να είναι ίδιου μήκους": το SparklineCard είχε Width=500
            // ενώ τα CPU/Μνήμη/GPU (AddMeter) και η κάρτα Ενέργειας (AddCard) είναι όλα 760 -
            // ίδιο πλάτος με τα υπόλοιπα, συνεπές με όλη τη σελίδα.
            var net = new SparklineCard(LocalizationManager.T("Performance.NetworkLabel"), 2, 1024) { Width = 760, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(net);
            var procs = new SparklineCard(LocalizationManager.T("Performance.ProcessesLabel"), 1, 400) { Width = 760, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(procs);
            var powerCard = AddCard(panel, LocalizationManager.T("Performance.PowerLabel"), "");

            // 1.7.8: οι κάρτες ιστορικού "CPU & RAM"/"GPU" στο τέλος της σελίδας αφαιρέθηκαν (ζητήθηκε ρητά) - το ποσοστό RAM
            // φαίνεται πλέον μέσα στη γραμμή "Μνήμη" επάνω, τα CPU/GPU στις δικές τους γραμμές.

            // top 5 memory users (refreshed every ~6 s on a background thread)
            AddSection(panel, LocalizationManager.T("Performance.TopMemory"));
            var topLabel = new Label { Text = "…", AutoSize = true, MaximumSize = new Size(740, 0), Font = new Font("Consolas", 10f), ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(topLabel);
            int topTick = 0; bool topBusy = false;

            // notification when CPU/RAM stay above 90% for a minute (works while the app lives in the tray)
            var alertsCheck = new FlatToggle { RowWidth = 700, Text = LocalizationManager.T("Performance.AlertsEnable"), Checked = AppSettings.Load().UsageAlertsEnabled, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 4, 0, 10) };
            alertsCheck.CheckedChanged += (_, _) => { var a = AppSettings.Load(); a.UsageAlertsEnabled = alertsCheck.Checked; a.Save(); };
            panel.Controls.Add(alertsCheck);

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            async Task RefreshTopProcesses()
            {
                topBusy = true;
                try
                {
                    var processes = await Task.Run(() => TopProcessesService.TopByMemory(5));
                    if (panel.IsDisposed || topLabel.IsDisposed) return;
                    string text = string.Join("\n", processes.Select((entry, index) =>
                        $"{index + 1}. {entry.Name,-28} {(entry.Mb >= 1024 ? (entry.Mb / 1024).ToString("0.0") + " GB" : entry.Mb.ToString("0") + " MB"),9}"));
                    if (topLabel.Text == text) return;
                    var scroll = panel.AutoScrollPosition;
                    topLabel.Text = text;
                    panel.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
                }
                catch (Exception ex) { RotatingLogger.Write($"Top processes: {ex.Message}"); }
                finally { topBusy = false; }
            }
            timer.Tick += (_, _) =>
            {
                if (!panel.Visible) return;
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                if (topTick++ % 3 == 0 && !topBusy) _ = RefreshTopProcesses();
                cpu.SetValue(m.CpuPercent, $"{m.CpuPercent:0.0}%");
                double ramPercent = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                ram.SetValue(ramPercent, string.Format(LocalizationManager.T("Performance.MemoryValueFormat"), $"{m.AvailableMemoryMb:0}", $"{m.TotalMemoryMb:0}", RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb)));
                var g = gpuMonitor.GetSnapshot();
                if (gpuMonitor.HasSample)
                {
                    gpu.SetValue(g.Available ? g.LoadPercent ?? 0 : 0,
                        g.Available ? $"{g.LoadPercent:0.0}%" + (g.TemperatureC.HasValue ? $"   {g.TemperatureC:0}°C" : "") : LocalizationManager.T("Performance.GpuUnavailable"));
                }
                net.Push(m.NetworkDownKbps, m.NetworkUpKbps, string.Format(LocalizationManager.T("Performance.NetworkValueFormat"), $"{m.NetworkDownKbps:0.0}", $"{m.NetworkUpKbps:0.0}"));
                procs.Push(m.ProcessCount, null, string.Format(LocalizationManager.T("Performance.ProcessesValueFormat"), m.ProcessCount));
                powerCard.Text = string.Format(LocalizationManager.T("Performance.RecommendedFormat"), m.BatteryMode);
            };
            RunWhilePageVisible(panel, timer);
            panel.Disposed += (_, _) => timer.Dispose();
            SetPage("Page.Performance.Title", panel);
        }

        private void ShowAutomation()
        {
            _currentPageKey = "Automation";
            var panel = CreatePagePanel();
            AddSection(panel, LocalizationManager.T("Page.Profiles.Title"));
            BuildProfilesSection(panel);
            AddSection(panel, LocalizationManager.T("Nav.Automation"));
            AddText(panel, LocalizationManager.T("Automation.Intro"));
            foreach (var rule in AutomationService.Instance.Rules)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                var check = new FlatCheckBox { Text = $"{rule.Name}  ·  {rule.TriggerProcess}  [{rule.PowerCondition}, ≤{rule.BatteryBelowPercent}%] →  {rule.Profile}", Checked = rule.Enabled, AutoSize = true, BackColor = UiTheme.PageBackground, Padding = new Padding(0, 4, 0, 0) };
                check.CheckedChanged += (_, _) => { rule.Enabled = check.Checked; AutomationService.Instance.Save(); };
                row.Controls.Add(check);

                row.Controls.Add(StudioButton(LocalizationManager.T("Common.Edit"), () =>
                {
                    if (PromptAutomationRule(rule)) { AutomationService.Instance.Save(); ShowAutomation(); }
                }, 96));
                row.Controls.Add(StudioButton(LocalizationManager.T("Common.Delete"), () =>
                {
                    AutomationService.Instance.Rules.Remove(rule);
                    AutomationService.Instance.Save();
                    ShowAutomation();
                }, 96));

                panel.Controls.Add(row);
            }

            var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddRule"), () =>
            {
                var newRule = new AutomationRule { Name = LocalizationManager.T("Automation.NewRuleDefaultName"), TriggerProcess = "", Profile = "" };
                if (PromptAutomationRule(newRule)) { AutomationService.Instance.Rules.Add(newRule); AutomationService.Instance.Save(); ShowAutomation(); }
            }));
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddGamingRule"), () =>
            {
                AutomationService.Instance.Rules.Add(new AutomationRule { Name = LocalizationManager.T("Automation.GamingRuleName"), TriggerProcess = "steam.exe", Profile = "Gaming" });
                AutomationService.Instance.Save();
                ShowAutomation();
            }));
            panel.Controls.Add(addRow);

            SetPage("Page.Automation.Title", panel);
        }

        private static HoverButton NewHoverButton(string text, Action onClick)
        {
            int width = Math.Clamp(TextRenderer.MeasureText(text, UiTheme.FontBody).Width + 28, 140, 320);
            var btn = new HoverButton(keyboard: true) { Text = text, Width = width, Height = CaptionHeight(text, width, 28, UiTheme.FontBody, 30), Font = UiTheme.FontBody, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) => onClick();
            return btn;
        }

        // Ζητήθηκε ρητά "εκτός από Add Gaming role να μπορώ να τον παραμετροποιώ & διαγράφω" —
        // ίδιο πρότυπο διαλόγου με το RenameContainer/PromptWeatherLocation, χρησιμοποιείται και
        // για Add (νέος, άδειος κανόνας) και για Edit (υπάρχων κανόνας) στο ίδιο dialog.
        private bool PromptAutomationRule(AutomationRule rule)
        {
            using var dialog = new MotionDesk.UI.AppDialog
            {
                Text = LocalizationManager.T("Automation.RuleDialogTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(440, 340),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = UiTheme.Surface
            };
            var nameLabel = new Label { Text = LocalizationManager.T("Common.NameLabel"), Location = new Point(14, 14), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var nameBox = new TextBox { Location = new Point(14, 34), Width = 380, Text = rule.Name };
            var procLabel = new Label { Text = LocalizationManager.T("Automation.TriggerProcessLabel"), Location = new Point(14, 66), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var procBox = new TextBox { Location = new Point(14, 86), Width = 380, Text = rule.TriggerProcess };
            var profileLabel = new Label { Text = LocalizationManager.T("Automation.ProfileToLoadLabel"), Location = new Point(14, 118), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var profileBox = new TextBox { Location = new Point(14, 138), Width = 380, Text = rule.Profile };
            var powerLabel = new Label { Text = StudioText("Power condition", "Κατάσταση τροφοδοσίας"), Location = new Point(14, 170), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var powerCombo = new ComboBox { Location = new Point(14, 190), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            powerCombo.Items.AddRange(new object[] { "Any", "Battery", "AC" });
            powerCombo.SelectedItem = rule.PowerCondition is "Battery" or "AC" ? rule.PowerCondition : "Any";
            var belowLabel = new Label { Text = StudioText("Battery below % (0 = off)", "Μπαταρία κάτω από % (0 = χωρίς όριο)"),
                Location = new Point(195, 170), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var belowNumber = new NumericUpDown { Location = new Point(195, 190), Width = 160, Minimum = 0, Maximum = 100,
                Value = Math.Clamp(rule.BatteryBelowPercent, 0, 100) };
            var okBtn = new Button { Text = LocalizationManager.T("Common.OK"), Location = new Point(228, 244), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = LocalizationManager.T("Common.Cancel"), Location = new Point(316, 244), DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(nameLabel); dialog.Controls.Add(nameBox);
            dialog.Controls.Add(procLabel); dialog.Controls.Add(procBox);
            dialog.Controls.Add(profileLabel); dialog.Controls.Add(profileBox);
            dialog.Controls.Add(powerLabel); dialog.Controls.Add(powerCombo);
            dialog.Controls.Add(belowLabel); dialog.Controls.Add(belowNumber);
            dialog.Controls.Add(okBtn); dialog.Controls.Add(cancelBtn);
            dialog.AcceptButton = okBtn; dialog.CancelButton = cancelBtn;
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(nameBox.Text)) return false;
            rule.Name = nameBox.Text.Trim();
            rule.TriggerProcess = procBox.Text.Trim();
            rule.Profile = profileBox.Text.Trim();
            rule.PowerCondition = powerCombo.SelectedItem?.ToString() ?? "Any";
            rule.BatteryBelowPercent = (int)belowNumber.Value;
            return true;
        }

        private static bool _gamingModeActive;
        internal void ToggleGamingMode() => LoadProfileFromQuickButton(_gamingModeActive ? "Work" : "Gaming", true);
        internal static void ToggleGamingWithoutWindow()
        {
            if (WorkspaceProfileService.Load(_gamingModeActive ? "Work" : "Gaming")) _gamingModeActive = !_gamingModeActive;
        }
        private void LoadProfileFromQuickButton(string profile, bool toggle = false)
        {
            if (WorkspaceProfileService.Load(profile)) { if (toggle) _gamingModeActive = !_gamingModeActive; _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileLoadedStatus"), profile); }
            else _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileNotFoundStatus"), profile);
        }

        // Ιστορικό εκδόσεων — απλή, in-code λίστα· ανοίγει σε ξεχωριστό (δευτερεύον) παράθυρο.
        // Οι σημειώσεις κάθε έκδοσης ζουν στα locale αρχεία (κλειδί "VersionNotes.<έκδοση>") ώστε να
        // ακολουθούν την επιλεγμένη γλώσσα — πριν ήταν hardcoded και μισές ελληνικά / μισές αγγλικά.
        // ΥΠΕΝΘΥΜΙΣΗ για κάθε μελλοντικό version bump: αυτός ο πίνακας ΔΕΝ ενημερώνεται αυτόματα από
        // το VersionNotes.* κλειδί — ξεχάστηκε 3 φορές στη σειρά (1.9.1/1.9.2/1.9.3), ο χρήστης το
        // έπιασε ζωντανά ("το ιστορικό μένει στην 1.9.0"). Πρόσθεσε ΚΑΙ εδώ τη νέα έκδοση.
        private static readonly (string Version, string Date)[] HistoricalVersions =
        {
            ("2.8.0.2511", "2026-11"), ("2.8.0.2510", "2026-11"), ("2.8.0.2500", "2026-11"),
            ("2.7.7.2402", "2026-11"), ("2.7.6.2401", "2026-11"), ("2.7.5.2400", "2026-11"), ("2.6.0-studio.23", "2026-11"), ("2.6.0-studio.22", "2026-11"), ("2.6.0-studio.21", "2026-11"), ("2.6.0-studio.20", "2026-11"), ("2.6.0-studio.19", "2026-11"), ("2.6.0", "2026-11"), ("2.4.6.1", "2026-11"), ("2.4.6", "2026-11"), ("2.4.5", "2026-11"), ("2.4.4", "2026-11"), ("2.4.3", "2026-11"), ("2.4.2", "2026-11"), ("2.4.1", "2026-11"), ("2.4.0", "2026-11"), ("2.3.1", "2026-10"), ("2.3.0", "2026-10"), ("2.2.6", "2026-10"), ("2.2.5", "2026-10"), ("2.2.0", "2026-10"), ("2.1.3", "2026-10"), ("2.1.2", "2026-10"), ("2.1.1", "2026-10"), ("2.1.0", "2026-10"), ("2.0.0", "2026-10"), ("1.9.12", "2026-10"), ("1.9.11", "2026-10"), ("1.9.10", "2026-10"), ("1.9.9", "2026-10"), ("1.9.8", "2026-10"), ("1.9.7", "2026-10"), ("1.9.6", "2026-10"), ("1.9.5", "2026-10"), ("1.9.4", "2026-10"), ("1.9.3", "2026-10"), ("1.9.2", "2026-10"), ("1.9.1", "2026-10"), ("1.9.0", "2026-10"), ("1.8.9", "2026-10"), ("1.8.8", "2026-10"), ("1.8.7", "2026-10"), ("1.8.6", "2026-10"), ("1.8.5", "2026-10"), ("1.7.8", "2026-10"), ("1.7.6", "2026-10"), ("1.7.5", "2026-10"), ("1.6.8", "2026-10"), ("1.6.2", "2026-10"), ("1.6.1", "2026-10"), ("1.6.0", "2026-09"), ("1.5.0", "2026-09"), ("1.4.1", "2026-09"), ("1.4.0", "2026-09"), ("1.3.0", "2026-09"), ("1.2.9", "2026-09"), ("1.2.8", "2026-09"), ("1.2.7", "2026-09"), ("1.2.6", "2026-09"), ("1.2.5", "2026-09"), ("1.2.4", "2026-09"), ("1.2.3", "2026-09"), ("1.2.2", "2026-09"), ("1.2.1", "2026-09"), ("1.2.0", "2026-09"), ("1.1.2", "2026-09"), ("1.1.1", "2026-09"), ("1.1.0", "2026-09"), ("1.0.0", "2026-09")
        };


        // VersionHistory follows the compiled version automatically; no separate
        // manually maintained current-version entry or beta label is required.
        private static IReadOnlyList<(string Version, string Date)> VersionHistory =>
            new[] { (Services.AppVersion.Short, "") }
            .Concat(HistoricalVersions.Where(v => v.Version != Services.AppVersion.Short)).ToArray();

        // "Audio Enhancement" — ζητήθηκε ρητά, εμπνευσμένο από τη ΛΟΓΙΚΗ του FXSound (github.com/
        // fxsound2/fxsound-app), όχι αντιγραφή του κώδικά του. Έρευνα στο ίδιο το repo (WebFetch)
        // επιβεβαίωσε ότι ΑΚΟΜΑ ΚΙ ΕΚΕΙ η πραγματική system-wide επεξεργασία γίνεται μέσω
        // ξεχωριστού, ΚΛΕΙΣΤΟΥ virtual audio driver — δεν είναι καν μέρος του δικού τους open-
        // source κώδικα, χρειάζεται kernel-mode driver signing (πέρα από απλό code-signing cert).
        // Η ΠΡΑΓΜΑΤΙΚΗ, εφικτή λύση βρέθηκε ερευνώντας παρόμοια GitHub projects (π.χ.
        // github.com/psidex/EACS): το Equalizer APO (equalizerapo.sourceforge.io) είναι ένα ήδη
        // δωρεάν, ανοιχτού κώδικα, ήδη-υπογεγραμμένο, system-wide Windows Audio Processing Object
        // — ελέγχεται προγραμματιστικά γράφοντας το δικό του config.txt (EqualizerApoService, ίδιο
        // μοτίβο ενσωμάτωσης εξωτερικού εργαλείου με FFmpeg/7-Zip σε αυτό το project). Όταν είναι
        // εγκατεστημένο, τα presets ΕΔΩ αλλάζουν πραγματικά τον ήχο ΟΛΩΝ των εφαρμογών· χωρίς αυτό,
        // εξακολουθούν έστω να ενισχύουν πραγματικά τον live visualizer (WASAPI loopback).
        private AudioSpectrumService? _enhancementSpectrum;
        private void ShowAudioEnhancement()
        {
            _currentPageKey = "AudioEnhancement";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("AudioEnhancement.Intro"));

            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Dock=Fill (κείμενο) + Dock=Right (κουμπί) στον ίδιο γονέα
            // δεν μείωνε σωστά το πλάτος του Fill label — το κουμπί απλά ζωγραφιζόταν ΠΑΝΩ από το
            // κείμενο (BringToFront), όχι δίπλα του, αφού το layout δεν αφαιρούσε ποτέ πραγματικά
            // τον χώρο του κουμπιού. Λύση: κάθετη στοίβα (FlowLayoutPanel, TopDown) — κείμενο πάνω,
            // κουμπί από κάτω, ΠΟΤΕ στο ίδιο ύψος, άρα αδύνατο να επικαλυφθούν.
            bool apoInstalled = EqualizerApoService.IsInstalled;
            var noteCard = new Panel { Width = 720, BackColor = UiTheme.Surface, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 0, 0, 10) };
            UiTheme.ApplyRoundedRegion(noteCard, 8);
            var noteStack = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
            var statusLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Text = apoInstalled
                    ? "✓ " + LocalizationManager.T("AudioEnhancement.ApoFound")
                    : LocalizationManager.T("AudioEnhancement.ApoMissing"),
                ForeColor = apoInstalled ? Color.FromArgb(60, 210, 140) : UiTheme.TextSecondary,
                Font = UiTheme.FontBody
            };
            noteStack.Controls.Add(statusLabel);
            if (!apoInstalled)
            {
                var installBtn = new HoverButton { Text = LocalizationManager.T("AudioEnhancement.ApoInstallBtn"), Width = 190, Height = 32, Margin = new Padding(0, 8, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.AccentBlue, BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), Cursor = Cursors.Hand };
                installBtn.FlatAppearance.BorderSize = 0;
                installBtn.Click += (_, _) => EqualizerApoService.OpenDownloadPage();
                noteStack.Controls.Add(installBtn);
            }
            noteCard.Controls.Add(noteStack);
            noteCard.Height = noteStack.PreferredSize.Height + noteCard.Padding.Vertical;
            panel.Controls.Add(noteCard);

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionPresets"));
            var settings = AppSettings.Load();
            Dictionary<string, HoverButton>? presetButtons = null;
            var presetItems = AudioSpectrumService.Presets.Select(p =>
                (p.Name, (EventHandler)((_, _) =>
                {
                    var a = AppSettings.Load(); a.AudioEnhancementPreset = p.Name; a.Save();
                    _enhancementSpectrum?.ApplyPreset(p.Name);
                    MarkActive(presetButtons!, p.Name);
                    if (apoInstalled)
                    {
                        var (ok, msg) = EqualizerApoService.ApplyPreset(p.Name);
                        _statusLabel.Text = ok ? string.Format(LocalizationManager.T("Status.AudioPresetSystemWideFormat"), p.Name) : $"Equalizer APO: {msg}";
                    }
                }))).ToArray();
            presetButtons = AddToggleButtonGrid(panel, presetItems);
            MarkActive(presetButtons, settings.AudioEnhancementPreset);
            AddText(panel, LocalizationManager.T("AudioEnhancement.PresetsNote"));

            BuildEqStudioSection(panel, apoInstalled);

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionVisualizer"));
            var equalizer = new EqualizerControl { Width = 700, Height = 200, Style = "WMP", Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(equalizer);

            var styleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var wmpBtn = new HoverButton { Text = "WMP Legacy", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            var winampBtn = new HoverButton { Text = "Winamp", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
            wmpBtn.FlatAppearance.BorderSize = 0; winampBtn.FlatAppearance.BorderSize = 0;
            wmpBtn.Click += (_, _) => { equalizer.Style = "WMP"; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.SurfaceHover; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.Surface; };
            winampBtn.Click += (_, _) => { equalizer.Style = "Winamp"; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.SurfaceHover; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.Surface; };
            styleRow.Controls.Add(wmpBtn);
            styleRow.Controls.Add(winampBtn);
            panel.Controls.Add(styleRow);

            _enhancementSpectrum?.Dispose();
            var spectrum = new AudioSpectrumService(20);
            _enhancementSpectrum = spectrum;
            spectrum.ApplyPreset(settings.AudioEnhancementPreset);
            var timer = new System.Windows.Forms.Timer { Interval = 40 };
            // Το closure πιάνει το δικό του "spectrum" local (όχι το κοινόχρηστο πεδίο
            // _enhancementSpectrum) ώστε ΚΑΘΕ επίσκεψη στη σελίδα να έχει το δικό της, απομονωμένο
            // instance — ακόμη κι αν ο Timer μιας παλιότερης επίσκεψης καθυστερήσει να σταματήσει.
            timer.Tick += (_, _) => { if (!equalizer.IsDisposed) equalizer.PushBands(spectrum.GetBands()); };
            RunWhilePageVisible(panel, timer, spectrum.SetActive);
            panel.Disposed += (_, _) =>
            {
                timer.Stop(); timer.Dispose(); spectrum.Dispose();
                if (ReferenceEquals(_enhancementSpectrum, spectrum)) _enhancementSpectrum = null;
            };

            SetPage("AudioEnhancement.Title", panel);
        }


        // ---- Ισοσταθμιστής 10 ζωνών ΣΕ ΟΛΟ ΤΟ ΣΥΣΤΗΜΑ (Equalizer APO) + ανταλλαγή presets με το Waveframe ----
        private void BuildEqStudioSection(FlowLayoutPanel panel, bool apoInstalled)
        {
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            AddSection(panel, Ld("Ισοσταθμιστής 10 ζωνών (σε όλο το σύστημα)", "10-band equalizer (system-wide)"));
            AddText(panel, Ld(
                "Σύρε τις ζώνες (διπλό κλικ = μηδέν). Εφαρμόζεται σε ΟΛΟΝ τον ήχο των Windows μέσω Equalizer APO. Μπορείς να εισάγεις presets από το Opsis Media Suite (αρχείο .txt ή .json) και να εξάγεις τα δικά σου.",
                "Drag the bands (double-click = zero). Applied to ALL Windows audio through Equalizer APO. You can import presets from Opsis Media Suite (.txt or .json) and export your own."));

            var settings = AppSettings.Load();
            var eq = new EqBandsControl { Width = 700, Height = 220, Margin = new Padding(0, 0, 0, 8) };
            var current = new AudioEqProfile { Name = Ld("Τρέχον", "Current") };
            var saved = settings.CustomEqProfiles.FirstOrDefault(p => p.Name == settings.ActiveEqProfile);
            if (saved != null) current = saved.Clone();
            eq.Bands = current.Bands;
            panel.Controls.Add(eq);

            var applyTimer = new System.Windows.Forms.Timer { Interval = 400 };
            void ApplyNow()
            {
                applyTimer.Stop();
                current.Bands = eq.Bands;
                if (!apoInstalled) { _statusLabel.Text = LocalizationManager.T("AudioEnhancement.ApoMissing"); return; }
                var (ok, msg) = EqualizerApoService.ApplyProfile(current);
                _statusLabel.Text = ok ? Ld($"Ισοσταθμιστής εφαρμόστηκε σε όλο το σύστημα: {current.Name}", $"Equalizer applied system-wide: {current.Name}") : $"Equalizer APO: {msg}";
            }
            applyTimer.Tick += (_, _) => ApplyNow();
            eq.BandsChanged += (_, _) => { applyTimer.Stop(); applyTimer.Start(); };   // debounce: ένα αρχείο ανά "σταμάτημα" του σύρματος
            panel.Disposed += (_, _) => { applyTimer.Stop(); applyTimer.Dispose(); };

            HoverButton Btn(string text, int width, EventHandler click)
            {
                var b = new HoverButton { Text = text, Width = width, Height = 32, Margin = new Padding(0, 0, 8, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderSize = 0; b.Click += click; return b;
            }

            // Presets: τα 16 του Opsis Media Suite + τα δικά σου
            var combo = new FlatComboBox { Width = 260, Margin = new Padding(0, 0, 8, 6) };
            List<(string Label, AudioEqProfile Profile)> entries = new();
            void FillCombo(string? select)
            {
                entries = AudioEqProfile.BuiltIn.Select(b => (Ld(b.El, b.En), new AudioEqProfile { Name = Ld(b.El, b.En), Bands = (double[])b.Bands.Clone() })).ToList();
                foreach (var c in AppSettings.Load().CustomEqProfiles) entries.Add(("★ " + c.Name, c.Clone()));
                combo.SetItems(entries.Select(x => x.Label).ToArray(), select != null && entries.Any(x => x.Label == select) ? select : null);
            }
            FillCombo(null);
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0 || combo.SelectedIndex >= entries.Count) return;
                current = entries[combo.SelectedIndex].Profile.Clone();
                eq.Bands = current.Bands;
                var a2 = AppSettings.Load(); a2.ActiveEqProfile = current.Name; a2.Save();
                ApplyNow();
            };

            var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row1.Controls.Add(combo);
            row1.Controls.Add(Btn(Ld("Μηδενισμός", "Reset to flat"), 120, (_, _) => { current = new AudioEqProfile { Name = Ld("Επίπεδο", "Flat") }; eq.Bands = current.Bands; ApplyNow(); }));
            row1.Controls.Add(Btn(Ld("Αποθήκευση ως…", "Save as…"), 130, (_, _) =>
            {
                string? name = PromptForText(Ld("Όνομα preset", "Preset name"), current.Name.Length > 0 ? current.Name : Ld("Το preset μου", "My preset"));
                if (string.IsNullOrWhiteSpace(name)) return;
                var st = AppSettings.Load();
                var p = current.Clone(); p.Name = name.Trim(); p.Bands = eq.Bands;
                st.CustomEqProfiles.RemoveAll(x => x.Name == p.Name); st.CustomEqProfiles.Add(p); st.ActiveEqProfile = p.Name; st.Save();
                current = p; FillCombo("★ " + p.Name);
            }));
            row1.Controls.Add(Btn(Ld("Διαγραφή", "Delete"), 100, (_, _) =>
            {
                var st = AppSettings.Load();
                if (st.CustomEqProfiles.RemoveAll(x => x.Name == current.Name) > 0) { st.Save(); FillCombo(null); }
            }));
            panel.Controls.Add(row1);

            var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            row2.Controls.Add(Btn(Ld("Εισαγωγή (Opsis Media Suite / Equalizer APO)…", "Import (Opsis Media Suite / Equalizer APO)…"), 290, (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Opsis Media Suite / Equalizer APO (*.txt;*.json)|*.txt;*.json|*.*|*.*", Title = Ld("Εισαγωγή EQ", "Import EQ") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dlg.FileName);
                    if (info.Length > 256 * 1024) { _statusLabel.Text = Ld("Το αρχείο είναι πολύ μεγάλο για preset EQ.", "The file is too large to be an EQ preset."); return; }
                    var imported = AudioEqProfile.Import(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
                    if (imported == null) { _statusLabel.Text = Ld("Δεν βρέθηκαν φίλτρα EQ σε αυτό το αρχείο.", "No EQ filters found in this file."); return; }
                    current = imported; eq.Bands = current.Bands;
                    _statusLabel.Text = Ld($"Εισήχθη: {current.Name}" + (current.ExtraLines.Count > 0 ? $" (+{current.ExtraLines.Count} επιπλέον φίλτρα)" : ""), $"Imported: {current.Name}" + (current.ExtraLines.Count > 0 ? $" (+{current.ExtraLines.Count} extra filters)" : ""));
                    ApplyNow();
                }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            }));
            row2.Controls.Add(Btn(Ld("Εξαγωγή (.txt)…", "Export (.txt)…"), 150, (_, _) =>
            {
                using var dlg = new SaveFileDialog { Filter = "Equalizer APO (*.txt)|*.txt", FileName = "opsis-eq.txt", Title = Ld("Εξαγωγή EQ", "Export EQ") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { current.Bands = eq.Bands; File.WriteAllText(dlg.FileName, current.ToEqualizerApo()); _statusLabel.Text = Ld("Αποθηκεύτηκε.", "Saved."); }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            }));
            panel.Controls.Add(row2);
        }

        private string? PromptForText(string prompt, string initial)
        {
            using var f = new MotionDesk.UI.AppDialog { Text = "Opsis", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Width = 380, Height = 160, MinimizeBox = false, MaximizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
            var l = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
            var t = new TextBox { Left = 12, Top = 36, Width = 340, Text = initial };
            var ok = new Button { Text = "OK", Left = 196, Top = 72, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = LocalizationManager.T("Common.Cancel"), Left = 277, Top = 72, Width = 75, DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { l, t, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }


        // Παράθυρο επιλογής: ποια εικονίδια ΜΕΝΟΥΝ όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας.
        private void ShowKeepIconsDialog()
        {
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var settings = AppSettings.Load();
            using var dialog = new MotionDesk.UI.AppDialog
            {
                Text = Ld("Εικονίδια που μένουν ορατά", "Icons that stay visible"),
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 470), FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary
            };
            var intro = new Label { Text = Ld("Τσέκαρε ό,τι θέλεις να ΜΕΝΕΙ ορατό όταν κάνεις διπλό κλικ σε κενό σημείο της επιφάνειας εργασίας. Τα υπόλοιπα κρύβονται (και ξαναφαίνονται με νέο διπλό κλικ).",
                "Tick what should STAY visible when you double-click an empty spot of the desktop. Everything else is hidden (and comes back with another double-click)."),
                Left = 12, Top = 10, Width = 436, Height = 52, ForeColor = UiTheme.TextSecondary };
            var list = new CheckedListBox { Left = 12, Top = 66, Width = 436, Height = 340, CheckOnClick = true, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };

            var special = new (string Label, Guid Id, Func<bool> Get, Action<bool> Set)[]
            {
                (Ld("Αυτός ο υπολογιστής", "This PC"), DesktopIconVisibilityEngine.ThisPcId, () => settings.QuickHideKeepThisPc, v => settings.QuickHideKeepThisPc = v),
                (Ld("Φάκελος χρήστη", "User folder"), DesktopIconVisibilityEngine.ProfileId, () => settings.QuickHideKeepUserFolder, v => settings.QuickHideKeepUserFolder = v),
                (Ld("Πίνακας Ελέγχου", "Control Panel"), DesktopIconVisibilityEngine.ControlPanelId, () => settings.QuickHideKeepControlPanel, v => settings.QuickHideKeepControlPanel = v),
                (Ld("Κάδος Ανακύκλωσης", "Recycle Bin"), DesktopIconVisibilityEngine.RecycleBinId, () => settings.QuickHideKeepRecycleBin, v => settings.QuickHideKeepRecycleBin = v),
            };
            var specialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sp in special)
            {
                specialNames.UnionWith(DesktopIconVisibilityEngine.SpecialIconNames(sp.Id));
                specialNames.Add(sp.Label);
                list.Items.Add(sp.Label, sp.Get());
            }
            var savedExtras = (settings.QuickHideExtraKept ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var extra in DesktopIconVisibilityEngine.ListDesktopItemNames().Concat(savedExtras)
                .Where(name => !specialNames.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
                list.Items.Add(extra, savedExtras.Contains(extra));
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 262, Top = 424, Width = 90, Height = 32 };
            var cancel = new Button { Text = LocalizationManager.T("Common.Cancel"), DialogResult = DialogResult.Cancel, Left = 358, Top = 424, Width = 90, Height = 32 };
            dialog.Controls.AddRange(new Control[] { intro, list, ok, cancel });
            dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
            StyleStudioDialog(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            for (int i = 0; i < special.Length; i++) special[i].Set(list.GetItemChecked(i));
            var extras = new List<string>();
            for (int i = special.Length; i < list.Items.Count; i++) if (list.GetItemChecked(i)) extras.Add(list.Items[i]?.ToString() ?? "");
            settings.QuickHideExtraKept = extras.Where(x => x.Length > 0).ToList();
            settings.Save();
            DesktopIconVisibilityEngine.Instance.ApplyKeepSettings();
        }

        private void ShowAbout()
        {
            _currentPageKey = "About";
            var panel = CreatePagePanel();
            var appCard = AddCardSection(panel, "Opsis - Desktop Customization");
            AddText(appCard, string.Format(LocalizationManager.T("About.VersionLine"), AppVersion.Label));
            AddText(appCard, LocalizationManager.T("About.Body"));
            // what this version brings (the family section is further down, from the shared catalogue; the full history is behind the button)
            // the notes of the RUNNING version (never a fixed old version number)
            var currentNotes = LocalizationManager.T("VersionNotes." + AppVersion.Short);
            if (!string.IsNullOrWhiteSpace(currentNotes) && !currentNotes.StartsWith("VersionNotes.")) { AddSection(appCard, string.Format(LocalizationManager.T("About.WhatBrings"), AppVersion.Label)); AddText(appCard, currentNotes); }

            AddSection(appCard, LocalizationManager.T("About.SectionCapabilities"));
            AddText(appCard, LocalizationManager.T("About.CapabilitiesList"));
            AddText(appCard, string.Format(LocalizationManager.T("About.RuntimeLine"), WidgetHostEngine.Instance.GetActiveWidgets().Count));
            AddButton(appCard, LocalizationManager.T("Help.Button"), (_, _) => HelpForm.ShowSingle(this, VersionHistory)).Name = "open-opsis-help";
            AddButton(appCard, StudioText("Version history", "Ιστορικό εκδόσεων"), (_, _) =>
                HelpForm.ShowSingle(this, VersionHistory, HelpSection.History)).Name = "open-opsis-history";
            AddButton(appCard, StudioText("Find a feature (Ctrl+K)", "Εύρεση λειτουργίας (Ctrl+K)"), (_, _) =>
                OpenCommandPalette()).Name = "open-opsis-feature-search";

            // the Opsis family: only the apps of the shared catalogue (suite/apps.json), with their state
            var familyCard = AddCardSection(panel, LocalizationManager.T("Tray.Family"));
            foreach (var app in FamilyApp.Load().Where(a => a.Id != "opsis"))
                AddText(familyCard, $"{app.Name}  {(app.InstalledPath != null ? "✓" : "")}\n{app.Tagline}");
            AddButtonGrid(familyCard, (LocalizationManager.T("Tray.Family"), (_, _) => FamilyWindows.ShowFamily()));

            // #97: source-controlled compatibility matrix, with honest validation status.
            // Showing a branch name or intended ABI here must never imply an E2E pass.
            var pluginCard = AddCardSection(panel, LocalizationManager.T("About.SectionPlugins"));
            AddSection(pluginCard, StudioText("Plugin compatibility for " + AppVersion.Short,
                "Συμβατότητα plugins για " + AppVersion.Short));
            foreach (var plugin in PluginCompatibilityCatalog.Read())
            {
                var observed = PluginCompatibilityCatalog.Probe(plugin);
                AddText(pluginCard, $"{plugin.Name} · {plugin.Integration}\n" +
                    $"{plugin.Repository} [{plugin.Branch}]\n" +
                    $"{(plugin.Dll == null ? "standalone" : plugin.Dll + " · ABI " + (plugin.AssemblyVersion ?? "pending"))}\n" +
                    observed.State + " · " + plugin.Validation);
            }

            // ---- plugins (π.χ. News Crawler): ιδιότητες, βοήθεια, οδηγίες, ιστορικό και άδεια ζουν ΕΔΩ, όχι στη σελίδα του plugin
            var pluginInfos = Services.PluginHost.Instance.GetAboutInfos();
            if (pluginInfos.Count == 0) AddText(pluginCard, StudioText("No loaded plugins.", "Δεν υπάρχουν φορτωμένα plugins."));
            if (pluginInfos.Count > 0)
            {
                AddSection(pluginCard, StudioText("Installed plugins", "Εγκατεστημένα plugins"));
                bool greek = LocalizationManager.CurrentLanguage == "el";
                foreach (var (id, info) in pluginInfos)
                {
                    var captured = info;
                    var capturedId = id;
                    var details = AddCardSection(pluginCard, captured.Name + "  v" + captured.Version, 710);
                    AddText(details, $"{captured.Name}  v{captured.Version}\n{(greek ? captured.DescriptionEl : captured.DescriptionEn)}\n{captured.Author} · {captured.License}");

                    // Ζητήθηκε ρητά: ξεχωριστό κουμπί ενεργοποίησης/απενεργοποίησης ΓΙΑ ΑΥΤΟ το plugin
                    // (όχι μόνο ο γενικός διακόπτης "Φόρτωση plugins" στις Ρυθμίσεις). Αλλαγές στα
                    // στοιχεία πλοήγησης/tray χρειάζονται επανεκκίνηση· το ίδιο το plugin σταματά/ξεκινά αμέσως.
                    var enableToggle = new FlatCheckBox
                    {
                        Text = LocalizationManager.T("About.PluginEnabled"),
                        AutoSize = true,
                        Checked = Services.PluginHost.Instance.IsPluginEnabled(capturedId),
                        BackColor = UiTheme.Surface,
                        Margin = new Padding(0, 0, 0, 6)
                    };
                    bool refreshingPluginToggle = false;
                    enableToggle.CheckedChanged += (_, _) =>
                    {
                        if (refreshingPluginToggle) return;
                        try { Services.PluginHost.Instance.SetPluginEnabled(capturedId, enableToggle.Checked); }
                        catch (Exception ex)
                        {
                            refreshingPluginToggle = true;
                            try { enableToggle.Checked = Services.PluginHost.Instance.IsPluginEnabled(capturedId); }
                            finally { refreshingPluginToggle = false; }
                            MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    };
                    details.Controls.Add(enableToggle);
                    AddText(details, StudioText("Declared plugin capabilities: ", "Δηλωμένες δυνατότητες plugin: ")
                        + Services.PluginHost.Instance.PluginPermissions(capturedId));
                    var revokeBtn = NewHoverButton(StudioText("Revoke plugin approval", "Ανάκληση έγκρισης plugin"), () =>
                    {
                        if (MessageBox.Show(this, StudioText(
                            "Stop this plugin and revoke its approval? Restart Opsis to unload the DLL completely.",
                            "Να σταματήσει το plugin και να ανακληθεί η έγκριση; Απαιτείται επανεκκίνηση του Opsis για πλήρη αποφόρτωση του DLL."),
                            "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        Services.PluginHost.Instance.RevokePluginTrust(capturedId);
                        ShowAbout();
                    });
                    details.Controls.Add(revokeBtn);

                    // Ζητήθηκε (σύγκριση με τον μηχανισμό plugin του Kombos): αφαίρεση plugin από
                    // μέσα στο Opsis, όχι μόνο ενεργό/ανενεργό. Ο φάκελος διαγράφεται αμέσως αν
                    // γίνεται (δεν θα γίνεται συνήθως — το .dll είναι ήδη φορτωμένο σε αυτή τη
                    // διεργασία), αλλιώς προγραμματίζεται καθαρή διαγραφή στην επόμενη εκκίνηση.
                    var uninstallBtn = NewHoverButton(LocalizationManager.T("About.PluginUninstall"), () =>
                    {
                        var choice = MessageBox.Show(
                            string.Format(LocalizationManager.T("About.PluginUninstallConfirmBody"), captured.Name),
                            LocalizationManager.T("About.PluginUninstallConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (choice != DialogResult.Yes) return;
                        bool removedNow = Services.PluginHost.Instance.UninstallPlugin(capturedId);
                        _statusLabel.Text = LocalizationManager.T(removedNow ? "About.PluginUninstalledNow" : "About.PluginUninstallQueued");
                        ShowAbout();
                    });
                    uninstallBtn.Margin = new Padding(0, 0, 0, 6);
                    details.Controls.Add(uninstallBtn);

                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 12) };
                    foreach (var section in captured.Sections)
                    {
                        var s = section;
                        var title = greek ? s.TitleEl : s.TitleEn;
                        row.Controls.Add(NewHoverButton(title, () => ShowTextDialog(captured.Name + " — " + title, captured.Name + " v" + captured.Version, s.Text)));
                    }
                    details.Controls.Add(row);
                }
            }

            // ---- updates: self-update βάσει GearWin (ζητήθηκε ρητά· ήδη portαρισμένο/ζωντανά
            // δοκιμασμένο στο OpsisNewsCrawler) — κατεβάζει+εγκαθιστά ΜΟΝΟ μετά από κλικ του χρήστη
            // ΚΑΙ επιβεβαίωση, μόνο από τα releases αυτού του repo, επαληθευμένο με SHA-256.
            var buildCard = AddCardSection(panel, StudioText("Build and dependencies", "Έκδοση και εξαρτήσεις"));
            AddText(buildCard, $"Opsis - Desktop Customization {AppVersion.Label}");
            var missingDependencies = AddText(buildCard, "");
            missingDependencies.Visible = false;
            panel.HandleCreated += async (_, _) =>
            {
                var missing = await Task.WhenAll(DependencyManagerService.All.Select(async dep =>
                    await dep.CheckInstalledAsync() ? null : dep.Name));
                if (missingDependencies.IsDisposed) return;
                var names = missing.OfType<string>().ToArray();
                missingDependencies.Visible = names.Length != 0;
                if (names.Length > 0)
                    missingDependencies.Text = string.Format(LocalizationManager.T("About.UpdateDependenciesMissing"), string.Join(", ", names));
            };

            var guideCard = AddCardSection(panel, LocalizationManager.T("About.SectionInstructions"));
            AddText(guideCard, LocalizationManager.T("About.InstructionsList"));
            AddSection(guideCard, LocalizationManager.T("Nav.Widgets"));
            AddText(guideCard, LocalizationManager.T("Widgets.Intro"));
            AddSection(guideCard, LocalizationManager.T("Nav.DeskZones"));
            AddText(guideCard, LocalizationManager.T("DeskZones.Intro"));
            AddSection(guideCard, LocalizationManager.T("DeskZones.SectionContainers"));
            AddText(guideCard, LocalizationManager.T("DeskZones.ContainersIntro"));
            AddSection(guideCard, LocalizationManager.T("About.DeskContainersGuideTitle"));
            AddText(guideCard, LocalizationManager.T("About.DeskContainersGuide"));
            AddSection(guideCard, LocalizationManager.T("Nav.Wallpaper"));
            AddText(guideCard, LocalizationManager.T("Wallpaper.Intro"));
            AddSection(guideCard, LocalizationManager.T("Nav.Personalization"));
            AddText(guideCard, LocalizationManager.T("Personalization.Intro"));
            AddSection(guideCard, LocalizationManager.T("Nav.Automation"));
            AddText(guideCard, LocalizationManager.T("Automation.Intro"));

            AddSection(guideCard, LocalizationManager.T("About.SectionShortcuts"));
            guideCard.Controls.Add(new ShortcutsCard(LocalizationManager.T("About.ShortcutsList")) { Margin = new Padding(0, 0, 0, 14) });

            AddSection(appCard, LocalizationManager.T("About.SectionLicense"));
            AddText(appCard, LocalizationManager.T("About.LicenseSummary"));
            AddText(appCard, LocalizationManager.T("About.LandmarkCredits"));
            SetPage("Page.About.Title", panel);
        }

        // Ζητήθηκε ρητά "βελτίωσε το κείμενο και την εμφάνιση του παραθύρου άδειας χρήσης" — πριν
        // ήταν ένα flat TextBox με το LICENSE file χυμένο μέσα ως ένα αδιαφοροποίητο μπλοκ μονοσπέικ
        // κειμένου (χωρίς ιεραρχία ανάμεσα στην περίληψη, τη νομική ρήτρα και τα third-party
        // components). Τώρα: επικεφαλίδα με εικονίδιο+τίτλο, RichTextBox που ξεχωρίζει οπτικά τις 4
        // ενότητες του LICENSE (διαχωρισμένες ήδη με "----" γραμμές στο ίδιο το αρχείο) — επικεφαλίδα
        // κάθε ενότητας σε accent χρώμα/bold, το ίδιο το νομικό κείμενο MIT σε μικρότερη γραμματοσειρά
        // (είναι το λιγότερο σημαντικό για τον μέσο χρήστη να διαβάσει προσεκτικά) — plus pill κουμπί
        // συνεπές με το υπόλοιπο MediaPlayerPro-style πέρασμα της εφαρμογής.
        // Γενικό themed παράθυρο κειμένου (βοήθεια/οδηγίες/ιστορικό/άδεια plugin). Ίδια αισθητική με το παράθυρο άδειας.
        private void ShowTextDialog(string title, string subtitle, string text)
        {
            using var dialog = new MotionDesk.UI.AppDialog
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(720, 640),
                BackColor = UiTheme.PageBackground,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };

            var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = UiTheme.Surface };
            header.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(20, 8) });
            header.Controls.Add(new Label { Text = subtitle, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(20, 34) });

            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = UiTheme.PageBackground,
                ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10f), DetectUrls = false, Text = text.Replace("\r\n", "\n")
            };
            var footer = new Panel { Dock = DockStyle.Bottom, Size = new Size(dialog.ClientSize.Width, 56), BackColor = UiTheme.PageBackground };
            var ok = new HoverButton { Text = "OK", Width = 110, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.AccentCyan, ForeColor = Color.White, Cursor = Cursors.Hand, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(footer.ClientSize.Width - 126, 10) };
            ok.FlatAppearance.BorderSize = 0;
            footer.Controls.Add(ok);
            dialog.Controls.Add(box);
            dialog.Controls.Add(footer);
            dialog.Controls.Add(header);
            dialog.AcceptButton = ok;
            dialog.ShowDialog(this);
        }

        internal static void SaveHelpPreview(string png, int tab) => HelpForm.SavePreview(png, VersionHistory, tab);

        // A scrolling page that does NOT jump when a control inside it gets the focus or changes size (the "auto scroll-up" bug: WinForms scrolls a focused child
        // into view, which on a tall page throws the user back to the top while they are working further down).
        private sealed class NoJumpFlowPanel : FlowLayoutPanel
        {
            protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
        }

        private string? _lastPageKey;

        private FlowLayoutPanel CreatePagePanel()
        {
            var panel = new NoJumpFlowPanel
            {
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4),
                BackColor = UiTheme.PageBackground
            };
            // Ζητήθηκε ρητά "να μην είναι το scrollbar λευκό στο dark mode".
            GlassBlur.ApplyScrollbarTheme(panel, UiTheme.Background.GetBrightness() < 0.5f);
            // ΔΙΟΡΘΩΣΗ πραγματικού bug, live-verified ("scroll up issues στις καρτέλες"): τα Windows
            // στέλνουν WM_MOUSEWHEEL στο control ΚΑΤΩ από τον κέρσορα, όχι απαραίτητα στο
            // AutoScroll FlowLayoutPanel της σελίδας — μόλις ο κέρσορας βρεθεί πάνω από ένα Button/
            // CheckBox/Label (πρακτικά αναπόφευκτο σε μια σελίδα γεμάτη κουμπιά), η κύλιση φαινόταν
            // να "κολλάει" εκεί, ενώ στην πραγματικότητα απλώς δεν έφτανε ΠΟΤΕ στο panel. Λύση:
            // προώθηση του MouseWheel από ΚΑΘΕ child control (καθώς προστίθεται, αναδρομικά) πίσω
            // στο scroll του page panel — εκτός από controls με δικό τους νόημα για το wheel
            // (TextBox/ComboBox/NumericUpDown, ή ήδη ξεχωριστά AutoScroll containers).
            EnableWheelPassthrough(panel);
            return panel;
        }

        private static void EnableWheelPassthrough(ScrollableControl scrollHost)
        {
            void Hook(Control c)
            {
                bool hasOwnWheelMeaning = c is TextBoxBase || c is ListControl || c is NumericUpDown
                    || (c is ScrollableControl inner && !ReferenceEquals(inner, scrollHost) && inner.AutoScroll);
                if (!ReferenceEquals(c, scrollHost) && !hasOwnWheelMeaning)
                {
                    c.MouseWheel += (_, e) =>
                    {
                        if (e is HandledMouseEventArgs hme) hme.Handled = true;
                        var pos = scrollHost.AutoScrollPosition;
                        int newY = Math.Max(0, -pos.Y - e.Delta);
                        scrollHost.AutoScrollPosition = new Point(-pos.X, newY);
                    };
                }
                if (!hasOwnWheelMeaning)
                {
                    c.ControlAdded += (_, ce) => { if (ce.Control != null) Hook(ce.Control); };
                    foreach (Control child in c.Controls) Hook(child);
                }
            }
            Hook(scrollHost);
        }

        private static void AddHeading(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Width = 760,
                Text = text,
                Font = UiTheme.FontHeading,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, 0, 0, 4)
            });
        }

        private static Label AddText(Control parent, string text)
        {
            var label = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(parent is FixedWidthFlowPanel fw ? fw.FixedWidth - 4 : 780, 0),
                Text = text,
                Font = parent is FixedWidthFlowPanel ? UiTheme.FontCard : UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, 14)
            };
            parent.Controls.Add(label);
            return label;
        }

        private static void AddSection(Control parent, string text, Action? reset = null)
        {
            var title = new Label
            {
                AutoSize = true,
                Text = UiTheme.UpperNoAccents(text),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.AccentCyan,
                Margin = new Padding(0, 14, 0, 6)
            };
            if (reset == null) { parent.Controls.Add(title); return; }
            // card title with a "Reset" button on the right: puts the settings of this section back to their defaults (after a confirmation)
            var row = new Panel { Width = 700, Height = 40, Margin = new Padding(0, 6, 0, 2), BackColor = Color.Transparent };
            title.Margin = new Padding(0); title.Location = new Point(0, 12);
            var btn = new FlatButton { Text = OpsisStrings.T("ui.reset"), Width = 96, Height = 28, Location = new Point(700 - 96, 6) };
            btn.Click += (_, _) =>
            {
                var owner = btn.FindForm();
                if (MessageBox.Show(owner, LocalizationManager.T("Settings.ResetConfirm"), text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                reset();
                (owner as MainWindow)?.NavigateTo("Settings");
            };
            row.Controls.Add(title); row.Controls.Add(btn);
            parent.Controls.Add(row);
        }

        private sealed class FixedWidthPanel : Panel
        {
            private bool _hovered;

            public FixedWidthPanel() { DoubleBuffered = true; }
            public int FixedWidth { get; set; }

            public override Size GetPreferredSize(Size proposedSize) =>
                new(FixedWidth, base.GetPreferredSize(new Size(FixedWidth, proposedSize.Height)).Height);

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (Width > 2 && Height > 2) UiTheme.ApplyRoundedRegion(this, 12);
            }

            public void TrackHover(Control child)
            {
                child.MouseEnter += EnterCard;
                child.MouseLeave += LeaveCard;
                child.ControlAdded += (_, args) => { if (args.Control is { } next) TrackHover(next); };
                foreach (Control nested in child.Controls) TrackHover(nested);
            }

            private void EnterCard(object? sender, EventArgs e) => SetHovered(true);

            private void LeaveCard(object? sender, EventArgs e)
            {
                if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) SetHovered(false);
            }

            private void SetHovered(bool hovered)
            {
                if (_hovered == hovered || IsDisposed || Disposing) return;
                _hovered = hovered;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 8 || Height < 8) return;
                int top = _hovered ? 1 : 3;
                using var outline = UiTheme.RoundedPath(new Rectangle(1, top, Width - 3, Height - 5), 11);
                using var pen = new Pen(_hovered ? BlendChrome(UiTheme.Border, UiTheme.AccentCyan, .32f) : UiTheme.Border);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, outline);
            }
        }

        private sealed class FixedWidthFlowPanel : FlowLayoutPanel
        {
            public FixedWidthFlowPanel() { DoubleBuffered = true; }
            public int FixedWidth { get; set; }
            public override Size GetPreferredSize(Size proposedSize)
            {
                var baseSize = base.GetPreferredSize(new Size(FixedWidth, proposedSize.Height));
                return new Size(FixedWidth, baseSize.Height);
            }
        }

        private static FlowLayoutPanel AddCardSection(Control parent, string title, int width = 760)
        {
            var inner = new FixedWidthFlowPanel { FixedWidth = width - 36, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = width - 36 };
            inner.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiTheme.UpperNoAccents(title),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.AccentCyan,
                Margin = new Padding(0, 0, 0, 10)
            });

            var card = new FixedWidthPanel
            {
                FixedWidth = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                Padding = new Padding(18, 14, 18, 16),
                Margin = new Padding(0, 0, 0, 16)
            };
            card.Controls.Add(inner);
            card.TrackHover(card);
            parent.Controls.Add(card);
            if (parent is NoJumpFlowPanel)
            {
                void FitCard(object? sender, EventArgs args)
                {
                    int available = parent.ClientSize.Width - parent.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
                    if (available < 500) return; // ordinary design-time size before the page is mounted
                    int fit = Math.Min(width, available);
                    if (fit == card.FixedWidth) return;
                    card.FixedWidth = fit;
                    inner.FixedWidth = fit - card.Padding.Horizontal;
                    inner.Width = inner.FixedWidth;
                    card.Width = fit;
                }
                parent.ClientSizeChanged += FitCard;
                card.Disposed += (_, _) => parent.ClientSizeChanged -= FitCard;
                FitCard(null, EventArgs.Empty);
            }
            return inner;
        }

        private static StatCard AddCard(Control parent, string title, string value, Action? onClick = null)
        {
            var card = new StatCard(title, value) { Width = 760, Height = 80, Margin = new Padding(0, 0, 0, 10) };
            if (onClick != null)
            {
                card.Cursor = Cursors.Hand;
                card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) onClick(); };
            }
            parent.Controls.Add(card);
            return card;
        }

        private static MeterCard AddMeter(Control parent, string title, double initial)
        {
            var card = new MeterCard(title, initial) { Width = 760, Height = 90, Margin = new Padding(0, 0, 0, 10) };
            parent.Controls.Add(card);
            return card;
        }

        // Πλήρως owner-draw στατιστική κάρτα (τίτλος + τιμή) — τα παιδιά-Label μέσα σε
        // Panel/FlowLayoutPanel εμφάνιζαν σποραδικά αόρατο κείμενο σε αυτό το build/runtime
        // (ίδιας οικογένειας quirk με το NavButton), οπότε ζωγραφίζουμε το κείμενο απευθείας.
        // Συντομεύσεις πληκτρολογίου σε ΕΝΑ ενιαίο στοιχείο: ομάδες, "πλήκτρα" (keycaps) αριστερά και περιγραφή ευθυγραμμισμένη δεξιά.
        // Πηγή: το locale κλειδί About.ShortcutsList — γραμμή "# Τίτλος" = νέα ομάδα, αλλιώς "Πλήκτρα  Περιγραφή" (διπλό κενό ως διαχωριστικό).
        private sealed class ShortcutsCard : Control
        {
            private readonly List<(bool IsHeader, string Left, string Right)> _rows = new();
            private const int CardWidth = 760, KeysX = 20, DescX = 330, RowH = 32, HeaderH = 36, PadTop = 12, PadBottom = 14;

            public ShortcutsCard(string source)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                foreach (var raw in source.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("# ")) { _rows.Add((true, line[2..].Trim(), "")); continue; }
                    int sep = line.IndexOf("  ", StringComparison.Ordinal);
                    if (sep <= 0) continue;
                    _rows.Add((false, line[..sep].Trim(), line[(sep + 2)..].Trim()));
                }
                int h = PadTop + PadBottom + _rows.Sum(r => r.IsHeader ? HeaderH : RowH);
                Size = new Size(CardWidth, h);
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UiTheme.ApplyRoundedRegion(this, 8); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                using var headerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using var keyFont = new Font("Segoe UI Semibold", 9f);
                using var descFont = new Font("Segoe UI", 10f);
                using var accent = new SolidBrush(UiTheme.AccentCyan);
                using var descBrush = new SolidBrush(UiTheme.TextSecondary);
                using var keyText = new SolidBrush(UiTheme.TextPrimary);
                using var keyFill = new SolidBrush(UiTheme.PageBackground);
                using var keyBorder = new Pen(UiTheme.Border, 1f);
                using var plusBrush = new SolidBrush(UiTheme.TextMuted);
                using var sepPen = new Pen(Color.FromArgb(40, UiTheme.TextMuted), 1f);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                int y = PadTop;
                bool first = true;
                foreach (var row in _rows)
                {
                    if (row.IsHeader)
                    {
                        if (!first) g.DrawLine(sepPen, KeysX, y + 2, Width - KeysX, y + 2);
                        g.DrawString(UiTheme.UpperNoAccents(row.Left), headerFont, accent, KeysX, y + 12);
                        y += HeaderH;
                        first = false;
                        continue;
                    }
                    first = false;
                    // keycaps: κάθε κομμάτι πριν/μετά το '+' ως ξεχωριστό "πλήκτρο"
                    int x = KeysX;
                    var parts = row.Left.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (row.Left.EndsWith("+,")) parts = row.Left[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(",").ToArray();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var size = TextRenderer.MeasureText(parts[i], keyFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                        int w = Math.Max(26, size.Width + 16);
                        var capRect = new Rectangle(x, y + 3, w, RowH - 8);
                        using (var path = UiTheme.RoundedPath(capRect, 6))
                        {
                            g.FillPath(keyFill, path);
                            g.DrawPath(keyBorder, path);
                        }
                        g.DrawString(parts[i], keyFont, keyText, new RectangleF(capRect.X, capRect.Y, capRect.Width, capRect.Height), sf);
                        x += w;
                        if (i < parts.Length - 1) { g.DrawString("+", descFont, plusBrush, x + 1, y + 5); x += 16; }
                    }
                    g.DrawString(row.Right, descFont, descBrush, new RectangleF(DescX, y + 3, Width - DescX - KeysX, RowH - 6), new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    y += RowH;
                }
            }
        }

        private sealed class StatCard : Panel
        {
            private readonly string _title;
            private string _value;

            public StatCard(string title, string value)
            {
                _title = UiTheme.UpperNoAccents(title);
                _value = value;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public new string Text
            {
                get => _value;
                set { _value = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 12);

                using var valueFont = new Font("Segoe UI", 13f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_value, valueFont, valueBrush, 18, 36);
            }
        }

        // Κάρτα μέτρησης με δυναμική, χρωματιστή μπάρα (πράσινο → κίτρινο → κόκκινο ανάλογα με %).
        // Πραγματικό sparkline chart (area fill + γραμμή + τελευταίο σημείο τονισμένο) αντί για
        // απλό κείμενο — ζητήθηκε ρητά "όμορφα γραφήματα" για Network/Processes/Power. Στηρίζεται
        // αυτόματα στη μεγαλύτερη τιμή του ιστορικού (min floor) ώστε να μη χρειάζεται fixed cap.
        private sealed class SparklineCard : Panel
        {
            private readonly string _title;
            private readonly double _minScaleFloor;
            private readonly int _maxPoints;
            private readonly Queue<double> _series1 = new();
            private readonly Queue<double>? _series2;
            private readonly bool _hasSeries2;
            private string _valueText = "";

            public SparklineCard(string title, int seriesCount, double minScaleFloor, int maxPoints = 30)
            {
                _title = title;
                _minScaleFloor = minScaleFloor;
                _maxPoints = maxPoints;
                _hasSeries2 = seriesCount >= 2;
                if (_hasSeries2) _series2 = new Queue<double>();
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void Push(double value1, double? value2, string valueText)
            {
                _series1.Enqueue(value1);
                while (_series1.Count > _maxPoints) _series1.Dequeue();
                if (_hasSeries2 && value2.HasValue)
                {
                    _series2!.Enqueue(value2.Value);
                    while (_series2.Count > _maxPoints) _series2.Dequeue();
                }
                _valueText = valueText;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 10);
                using var valueFont = new Font("Segoe UI", 11.5f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, 18, 28);

                if (_series1.Count < 2) return;
                int chartX = 18, chartY = 54, chartW = Math.Max(10, Width - 36), chartH = Math.Max(10, Height - 66);

                using (var gridPen = new Pen(UiTheme.Border))
                    for (int i = 0; i <= 2; i++)
                    {
                        int y = chartY + chartH * i / 2;
                        g.DrawLine(gridPen, chartX, y, chartX + chartW, y);
                    }

                double scaleMax = Math.Max(_minScaleFloor, Math.Max(MaxOf(_series1), _hasSeries2 ? MaxOf(_series2!) : 0));
                DrawSeries(g, _series1, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentCyan, true);
                if (_hasSeries2) DrawSeries(g, _series2!, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentBlue, false);
            }

            private static double MaxOf(Queue<double> q) { double m = 0; foreach (var v in q) if (v > m) m = v; return m; }

            private void DrawSeries(Graphics g, Queue<double> series, int chartX, int chartY, int chartW, int chartH, double scaleMax, Color color, bool fillArea)
            {
                var values = series.ToArray();
                var pts = new PointF[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    float x = chartX + chartW * i / (float)Math.Max(1, _maxPoints - 1);
                    double v = scaleMax > 0 ? Math.Clamp(values[i] / scaleMax, 0, 1) : 0;
                    pts[i] = new PointF(x, chartY + chartH - (float)(v * chartH));
                }
                if (pts.Length < 2) return;

                if (fillArea)
                {
                    var areaPts = new PointF[pts.Length + 2];
                    Array.Copy(pts, areaPts, pts.Length);
                    areaPts[pts.Length] = new PointF(pts[^1].X, chartY + chartH);
                    areaPts[pts.Length + 1] = new PointF(pts[0].X, chartY + chartH);
                    using var areaBrush = new LinearGradientBrush(new RectangleF(chartX, chartY, chartW, chartH), Color.FromArgb(70, color), Color.FromArgb(0, color), 90f);
                    g.FillPolygon(areaBrush, areaPts);
                }

                using (var linePen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round })
                    g.DrawLines(linePen, pts);

                var last = pts[^1];
                using var dotBrush = new SolidBrush(color);
                g.FillEllipse(dotBrush, last.X - 3, last.Y - 3, 6, 6);
            }
        }

        private sealed class MeterCard : Panel
        {
            private readonly string _title;
            private double _percent;
            private string _valueText;

            public MeterCard(string title, double percent)
            {
                _title = UiTheme.UpperNoAccents(title);
                _percent = percent;
                _valueText = "";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void SetValue(double percent, string valueText)
            {
                _percent = Math.Clamp(percent, 0, 100);
                _valueText = valueText;
                Invalidate();
            }

            private static Color BarColor(double percent)
            {
                if (percent < 50) return Color.FromArgb(46, 204, 113);   // green
                if (percent < 80) return Color.FromArgb(241, 196, 15);   // yellow
                return Color.FromArgb(231, 76, 60);                      // red
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                // Ζητήθηκε ρητά "τα στατιστικά απόδοσης δίπλα στον τίτλο και όχι από κάτω" —
                // τίτλος αριστερά, τιμή δεξιά-στοιχισμένη, στην ΙΔΙΑ γραμμή αντί για από κάτω.
                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 14);

                using var valueFont = new Font("Segoe UI", 13f);
                using var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                var valueRect = new RectangleF(18, 2, Width - 36, 28);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, valueRect, valueFormat);

                var barRect = new Rectangle(18, Height - 22, Width - 36, 8);
                using (var track = new SolidBrush(UiTheme.SurfaceHover))
                using (var trackPath = UiTheme.RoundedPath(barRect, 4))
                    g.FillPath(track, trackPath);

                int fillWidth = (int)(barRect.Width * (_percent / 100.0));
                if (fillWidth > 4)
                {
                    var fillRect = new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height);
                    using var fillBrush = new SolidBrush(BarColor(_percent));
                    using var fillPath = UiTheme.RoundedPath(fillRect, 4);
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        // Δύο-στηλη σειρά κουμπιών (π.χ. Quick Launch / Workspace) για εξοικονόμηση κάθετου χώρου.
        private static FlowLayoutPanel ActionGrid() => new()
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = Padding.Empty
        };

        private static void FitActionGrid(Control parent, FlowLayoutPanel grid, int preferredWidth, int captionInset, int minimumHeight)
        {
            grid.Name = "action-grid";
            bool fitting = false;
            void Fit(object? sender, EventArgs args)
            {
                if (fitting || grid.IsDisposed || parent.ClientSize.Width <= 0) return;
                fitting = true;
                grid.SuspendLayout();
                try
                {
                    int available = parent is FixedWidthFlowPanel card
                        ? card.FixedWidth
                        : parent.ClientSize.Width - parent.Padding.Horizontal
                            - (parent is ScrollableControl { AutoScroll: true } ? SystemInformation.VerticalScrollBarWidth : 0);
                    int maxWidth = grid.LogicalToDeviceUnits(740);
                    available = Math.Clamp(available, 1, maxWidth);
                    grid.MaximumSize = new Size(available, 0);
                    var buttons = grid.Controls.OfType<Button>().ToArray();
                    if (buttons.Length == 0) return;
                    int gap = buttons.Max(button => button.Margin.Horizontal);
                    int desired = grid.LogicalToDeviceUnits(preferredWidth);
                    int columns = Math.Max(1, Math.Min(maxWidth / (desired + gap),
                        available / (grid.LogicalToDeviceUnits(160) + gap)));
                    int width = Math.Max(1, Math.Min(desired, available / columns - gap));
                    for (int row = 0; row < buttons.Length; row += columns)
                    {
                        int end = Math.Min(row + columns, buttons.Length);
                        int height = 0;
                        for (int i = row; i < end; i++)
                            height = Math.Max(height, CaptionHeight(buttons[i].Text, width,
                                grid.LogicalToDeviceUnits(captionInset), buttons[i].Font,
                                grid.LogicalToDeviceUnits(minimumHeight), grid.LogicalToDeviceUnits(14)));
                        for (int i = row; i < end; i++)
                            buttons[i].Size = new Size(width, height);
                    }
                }
                finally
                {
                    grid.ResumeLayout();
                    fitting = false;
                }
            }
            parent.ClientSizeChanged += Fit;
            grid.DpiChangedAfterParent += Fit;
            foreach (Control button in grid.Controls) button.TextChanged += Fit;
            grid.Disposed += (_, _) => parent.ClientSizeChanged -= Fit;
            Fit(null, EventArgs.Empty);
        }

        private const TextFormatFlags ActionCaptionFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        private static int CaptionHeight(string text, int width, int horizontalInset, Font font, int minimum = 40, int verticalInset = 14) =>
            Math.Max(minimum, TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width - horizontalInset), int.MaxValue),
                ActionCaptionFlags).Height + verticalInset);

        private static void AddButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var grid = ActionGrid();
            foreach (var (text, action) in items)
            {
                var button = new HoverButton(keyboard: true)
                {
                    Text = text,
                    // ΔΙΟΡΘΩΣΗ "μέσα στα πλαίσια έχεις μεγάλο μέγεθος κουμπιών, δεν φαίνεται το 3ο
                    // κουμπί": 372px x2 + περιθώριο ξεπερνούσε το (πλέον σταθερό, βλ. AddCardSection)
                    // εσωτερικό πλάτος της κάρτας (724px) - δύο κουμπιά δίπλα-δίπλα ήδη δεν χωρούσαν
                    // καθαρά, πόσο μάλλον τρία να τυλιχτούν σε 2η γραμμή. 350px x2 + 8px περιθώριο =
                    // 708px, χωράει άνετα μέσα στο 724px.
                    Width = 350,
                    Height = CaptionHeight(text, 350, 32, UiTheme.FontBody),
                    Font = UiTheme.FontBody,
                    UseCompatibleTextRendering = true,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
            FitActionGrid(parent, grid, 350, 32, 40);
        }

        // Ίδιο με το AddButtonGrid, αλλά επιστρέφει τα ίδια τα κουμπιά ώστε ο caller να μπορεί να
        // "σημαδέψει" ποιο/ποια είναι αυτή τη στιγμή ενεργά (π.χ. το τρέχον performance mode) —
        // ζητήθηκε ρητά: ενεργές επιλογές πρέπει να έχουν αντίστοιχη οπτική σήμανση στο κουμπί.
        private static Dictionary<string, HoverButton> AddToggleButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var map = new Dictionary<string, HoverButton>();
            var grid = ActionGrid();
            foreach (var (text, action) in items)
            {
                var button = new HoverButton(keyboard: true)
                {
                    Text = text,
                    Width = 180,
                    Height = CaptionHeight(text, 180, 28, UiTheme.FontBody, 38),
                    Font = UiTheme.FontBody,
                    UseCompatibleTextRendering = true,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
                map[text] = button;
            }
            parent.Controls.Add(grid);
            FitActionGrid(parent, grid, 180, 28, 38);
            return map;
        }

        // Εφαρμόζει οπτική σήμανση στο ενεργό κουμπί ενός AddToggleButtonGrid — ζητήθηκε ρητά
        // "ενσωμάτωσε την παρουσίαση ενός media player": το ενεργό κουμπί γεμίζει πλήρως με το
        // accent χρώμα (λευκό κείμενο), ίδιο "hero button" στυλ με το γεμάτο Play/Pause κουμπί
        // του MediaPlayerPro, αντί για το προηγούμενο ημιδιάφανο tint + λεπτό περίγραμμα.
        private static void MarkActive(Dictionary<string, HoverButton> buttons, string? activeKey)
        {
            foreach (var (key, btn) in buttons)
            {
                bool active = key == activeKey;
                // ΔΙΟΡΘΩΣΗ πραγματικού bug ("μόνο το τικ φαίνεται, όχι το μπλε χρώμα"): εδώ οριζόταν
                // μόνο το BackColor απευθείας - το HoverButton.MouseLeave όμως το επαναφέρει πάντα σε
                // BaseColor (βλ. σχόλιο στο HoverButton), που έμενε στο προεπιλεγμένο Surface αφού
                // κανείς δεν το ενημέρωνε ποτέ εδώ. Αποτέλεσμα: το μπλε χρωμα του επιλεγμένου κουμπιού
                // εξαφανιζόταν μόλις το ποντίκι έμπαινε και έβγαινε από πάνω του - έμενε μόνο το "✓".
                // Τώρα ενημερώνονται ΚΑΙ το BaseColor/HoverBackColor, ώστε το επιλεγμένο κουμπί να
                // παραμένει μπλε (με ελαφρώς πιο ανοιχτό μπλε στο hover αντί να πέφτει πίσω σε γκρι).
                btn.BaseColor = active ? UiTheme.AccentCyan : UiTheme.Surface;
                btn.HoverBackColor = active ? ControlPaint.Light(UiTheme.AccentCyan, 0.15f) : UiTheme.SurfaceHover;
                btn.BackColor = btn.BaseColor;
                btn.ForeColor = active ? AccentCaptionColor(btn.BackColor) : UiTheme.TextPrimary;
                btn.FlatAppearance.BorderSize = 0;
                btn.Text = active ? "✓  " + key : key;
            }
        }

        // Ίδια διάταξη δύο-στηλών με το AddButtonGrid, αλλά με διανυσματικό εικονίδιο (IconRenderer)
        // αντί για μόνο κείμενο — π.χ. τα κουμπιά "+ System Monitor" της Widget Gallery δεν είχαν
        // κανένα πραγματικό εικονίδιο, μόνο τον χαρακτήρα "＋".
        // 2.6.0: Previous / Pause-Play / Next for the video library (plan 2.1.9).
        private static void AddPlaybackControls(Control parent)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            var tips = new ToolTip();
            row.Disposed += (_, _) => tips.Dispose();
            FlatButton Make(string glyph, string tip, Action action)
            {
                var b = new FlatButton { AutoSize = false, Width = 72, Height = 36, Text = glyph, Margin = new Padding(0, 0, 8, 0), Font = new Font("Segoe UI Symbol", 11f) };
                tips.SetToolTip(b, tip);
                b.AccessibleName = tip;
                b.Click += (_, _) => action();
                return b;
            }
            row.Controls.Add(Make("\u23EE", LocalizationManager.T("Wallpaper.PreviousVideo"), () => WallpaperHostEngine.Instance.StepVideo(false)));
            var pause = Make(WallpaperHostEngine.UserPaused ? "\u25B6" : "\u23F8", LocalizationManager.T("Wallpaper.PauseVideo"), () => { });
            pause.Click += (_, _) => { WallpaperHostEngine.Instance.TogglePause(); pause.Text = WallpaperHostEngine.UserPaused ? "\u25B6" : "\u23F8"; };
            row.Controls.Add(pause);
            row.Controls.Add(Make("\u23ED", LocalizationManager.T("Wallpaper.NextVideo"), () => WallpaperHostEngine.Instance.StepVideo(true)));
            parent.Controls.Add(row);
        }

        private static void AddIconButtonGrid(Control parent, params (string IconKey, string Text, EventHandler Action)[] items) => AddIconButtonGrid(parent, 350, items);

        // 2.6.0: the width is a parameter. Three 350 px buttons did not fit one row in the default window (the third wrapped or was cut off, plan R.1); the video library
        // uses narrower ones so all three stay on one line.
        private static void AddIconButtonGrid(Control parent, int buttonWidth, params (string IconKey, string Text, EventHandler Action)[] items)
        {
            var grid = ActionGrid();
            foreach (var (iconKey, text, action) in items)
            {
                var button = new IconButton(iconKey, text);
                button.Click += action;
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
            FitActionGrid(parent, grid, buttonWidth, 62, 44);
        }

        private sealed class IconButton : Button
        {
            private static readonly Font CaptionFont = new("Segoe UI", 10f);
            private readonly string _iconKey;
            private bool _hovered;

            public IconButton(string iconKey, string text)
            {
                _iconKey = iconKey;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Font = CaptionFont;
                Text = text;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
                UseMnemonic = false;
                Margin = new Padding(0, 3, 8, 3);
                SizeChanged += (_, _) => ClipActionButton(this);
                ClipActionButton(this);
            }

            protected override void OnPaintBackground(PaintEventArgs e) => PaintActionBackdrop(this, e);

            private int Px(int value) => LogicalToDeviceUnits(value);

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 8 || Height < 8) return;
                var g = e.Graphics;
                var previous = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var outline = UiTheme.RoundedPath(ClientRectangle, Math.Min(Width, Height) / 2);
                var colors = (FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                using var background = new SolidBrush(Enabled && _hovered ? colors.Hover : colors.Surface);
                g.FillPath(background, outline);

                int iconSize = Px(22);
                IconRenderer.Draw(g, _iconKey, new RectangleF(Px(14), (Height - iconSize) / 2f, iconSize, iconSize),
                    Enabled ? colors.Accent : colors.Muted);
                if (Focused && ShowFocusCues)
                {
                    var bounds = Rectangle.Inflate(ClientRectangle, -3, -3);
                    using var focus = UiTheme.RoundedPath(bounds, Math.Min(bounds.Width, bounds.Height) / 2);
                    using var pen = new Pen(colors.Accent);
                    g.DrawPath(pen, focus);
                }

                g.SmoothingMode = previous;
                var caption = new Rectangle(Px(46), 0, Math.Max(1, Width - Px(62)), Height);
                DrawActionCaption(g, Text, Font, caption, Enabled ? colors.Text : colors.Muted, TextFormatFlags.Left);
            }
        }

        private static Button AddButton(Control parent, string text, EventHandler action)
        {
            var button = new HoverButton(keyboard: true)
            {
                Text = text,
                Width = 300,
                Height = CaptionHeight(text, 300, 28, UiTheme.FontBody),
                Font = UiTheme.FontBody,
                UseCompatibleTextRendering = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.TextPrimary,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 3, 0, 3),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (sender, e) => action(sender, e);
            parent.Controls.Add(button);
            return button;
        }

        private sealed class HoverButton : Button
        {
            private bool _hovered;
            public Color BaseColor { get; set; } = UiTheme.Surface;
            public Color HoverBackColor { get; set; } = UiTheme.SurfaceHover;

            public HoverButton(bool keyboard = false)
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                UseMnemonic = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, keyboard);
                TabStop = keyboard;
                SizeChanged += (_, _) => ClipActionButton(this);
                ClipActionButton(this);
                MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
                MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // Match the parent surface under antialiased edge pixels.
                PaintActionBackdrop(this, e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var colors = (FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                PaintActionButton(this, e.Graphics, Enabled && _hovered ? HoverBackColor : BackColor,
                    Enabled ? ForeColor : colors.Muted, colors.Accent);
            }
        }

        internal static void ClipActionButton(Button button)
        {
            if (button.ClientSize.Width > 2 && button.ClientSize.Height > 2)
                UiTheme.ApplyRoundedRegion(button, Math.Min(button.ClientSize.Width, button.ClientSize.Height) / 2);
        }

        internal static void PaintActionBackdrop(Button button, PaintEventArgs e)
        {
            Control? parent = button.Parent;
            while (parent != null && parent.BackColor.A < 255) parent = parent.Parent;
            using var brush = new SolidBrush(parent?.BackColor ?? UiTheme.PageBackground);
            e.Graphics.FillRectangle(brush, e.ClipRectangle);
        }

        private static void DrawActionCaption(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
            TextFormatFlags alignment)
        {
            var measured = TextRenderer.MeasureText(graphics, text, font,
                new Size(bounds.Width, int.MaxValue), ActionCaptionFlags);
            int shift = Math.Max(0, (bounds.Height - measured.Height) / 2);
            bounds.Y += shift;
            bounds.Height -= shift;
            TextRenderer.DrawText(graphics, text, font, bounds, color,
                ActionCaptionFlags | alignment | TextFormatFlags.PreserveGraphicsClipping);
        }

        internal static void PaintActionButton(Button button, Graphics g, Color fill, Color text, Color accent)
        {
            if (button.Width < 8 || button.Height < 8) return;
            var bounds = button.ClientRectangle;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = UiTheme.RoundedPath(bounds, Math.Min(bounds.Width, bounds.Height) / 2);
            if (AppSettings.Peek().ThemeMode == "SignatureGlass" && !SystemInformation.HighContrast && bounds.Width > 4 && bounds.Height > 4)
            {
                using var glass = new System.Drawing.Drawing2D.LinearGradientBrush(bounds,
                    Color.FromArgb(Math.Min(255, fill.R + 24), Math.Min(255, fill.G + 32), Math.Min(255, fill.B + 38)),
                    fill, System.Drawing.Drawing2D.LinearGradientMode.Vertical);
                g.FillPath(glass, shape);
                var rimBounds = Rectangle.Inflate(bounds, -1, -1);
                using var rimPath = UiTheme.RoundedPath(rimBounds, Math.Min(rimBounds.Width, rimBounds.Height) / 2);
                using var rimPen = new Pen(Color.FromArgb(150, 126, 219, 255), 1f);
                g.DrawPath(rimPen, rimPath);
            }
            else
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, shape);
            }
            if (button.FlatAppearance.BorderSize > 0)
            {
                var border = Rectangle.Inflate(bounds, -1, -1);
                using var edge = UiTheme.RoundedPath(border, Math.Min(border.Width, border.Height) / 2);
                using var pen = new Pen(button.FlatAppearance.BorderColor, button.FlatAppearance.BorderSize);
                g.DrawPath(pen, edge);
            }
            if (button.Focused && button.TabStop)
            {
                var inset = Rectangle.Inflate(bounds, -3, -3);
                using var focus = UiTheme.RoundedPath(inset, Math.Min(inset.Width, inset.Height) / 2);
                using var pen = new Pen(fill == accent ? text : accent);
                g.DrawPath(pen, focus);
            }
            g.Restore(state);

            int padding = Math.Max(button.Padding.Horizontal / 2, button.LogicalToDeviceUnits(14));
            var caption = Rectangle.Inflate(bounds, -Math.Min(padding, (bounds.Width - 1) / 2), -2);
            var alignment = button.TextAlign switch
            {
                ContentAlignment.MiddleLeft => TextFormatFlags.Left,
                ContentAlignment.MiddleRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };
            DrawActionCaption(g, button.Text, button.Font, caption, text, alignment);
        }

        private static void SaveAppSettings()
        {
            var a = AppSettings.Load();
            a.GridSnap = WidgetSnapEngine.EnableGridSnap;
            a.SnapThreshold = WidgetSnapEngine.SnapThreshold;
            a.GridSize = WidgetSnapEngine.GridSize;
            a.Save();
        }

        private void UpdateStatus()
        {
            var m = SystemMonitorService.Instance.GetSnapshot();
            _statusLabel.Text = string.Format(LocalizationManager.T("Status.SystemFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
        }

        // Ποσοστό χρήσης RAM (χρησιμοποιούμενη/συνολική) — εμφανίζεται δίπλα στα MB στην Αρχική, στη γραμμή κατάστασης, στο Performance και στο widget.
        private static string RamUsedPercent(double availableMb, double totalMb) =>
            totalMb > 0 ? Math.Clamp((totalMb - availableMb) / totalMb * 100.0, 0, 100).ToString("0") : "0";

        private sealed class SettingsView : Panel
        {
            public SettingsView()
            {
                DoubleBuffered = true;
                BackColor = UiTheme.PageBackground;
                AutoScroll = true;
                GlassBlur.ApplyScrollbarTheme(this, UiTheme.Background.GetBrightness() < 0.5f);
                AutoScroll = false;
                var page = new NoJumpFlowPanel
                {
                    Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
                    WrapContents = false, Padding = new Padding(4), BackColor = UiTheme.PageBackground
                };
                GlassBlur.ApplyScrollbarTheme(page, UiTheme.Background.GetBrightness() < 0.5f);
                EnableWheelPassthrough(page);
                AddText(page, LocalizationManager.T("Settings.Intro"));
                Controls.Add(page);

                var discovery = AddCardSection(page, StudioText("Find and use features", "Εύρεση και χρήση λειτουργιών"));
                AddText(discovery, StudioText(
                    "Use search to locate settings and tools. Each feature has its own category; press F1 on a focused control for contextual help.",
                    "Χρησιμοποίησε την αναζήτηση για ρυθμίσεις και εργαλεία. Κάθε λειτουργία έχει τη δική της κατηγορία· πάτησε F1 σε επιλεγμένο στοιχείο για σχετική βοήθεια."));
                AddButton(discovery, StudioText("Find a feature (Ctrl+K)", "Εύρεση λειτουργίας (Ctrl+K)"), (_, _) =>
                    (FindForm() as MainWindow)?.OpenCommandPalette());
                AddButton(discovery, StudioText("Settings help (F1)", "Βοήθεια ρυθμίσεων (F1)"), (_, _) =>
                    HelpForm.ShowSingle(FindForm(), VersionHistory, HelpSection.Instructions, "Settings"));
                AddButton(discovery, StudioText("What's new / History", "Τι νέο υπάρχει / Ιστορικό"), (_, _) =>
                    HelpForm.ShowSingle(FindForm(), VersionHistory, HelpSection.History));

                FlowLayoutPanel NewCard(string title, Action? reset = null)
                {
                    var card = AddCardSection(page, title);
                    if (reset != null)
                    {
                        card.Controls.Add(StudioButton(OpsisStrings.T("ui.reset"), () =>
                        {
                            var owner = FindForm();
                            if (MessageBox.Show(owner, LocalizationManager.T("Settings.ResetConfirm"), title,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                            reset();
                            (owner as MainWindow)?.NavigateTo("Settings");
                        }));
                    }
                    return card;
                }

                // ---- Appearance
                var appearance = NewCard(LocalizationManager.T("Settings.Appearance"));
                appearance.Controls.Add(Checkbox(LocalizationManager.T("Settings.FollowWindowsAccent"), AppSettings.Load().FollowWindowsAccent,
                    v => ThemeManager.SetFollowWindowsAccent(v)));
                appearance.Controls.Add(BuildLanguageRow());
                appearance.Controls.Add(BuildUiScaleRow());
                appearance.Controls.Add(BuildCardFontScaleRow());
                appearance.Controls.Add(Checkbox(LocalizationManager.T("Settings.UiSoundCues"), AppSettings.Load().UiSoundsEnabled,
                    v => { var a = AppSettings.Load(); a.UiSoundsEnabled = v; a.Save(); }));
                appearance.Controls.Add(BuildOpacityRow());
                var appearanceAdvanced = new StudioAdvancedSection();
                {
                    // GPU glass of widgets / DeskStrip / containers: Auto (default) decides from the computer and the current load; the line below says what is used now and why
                    var modes = new[] { "Auto", "Always", "Blur", "Solid" };
                    var names = new[] { Lx("Αυτόματο (προτεινόμενο)", "Automatic (recommended)"), Lx("Πάντα glass", "Always glass"), Lx("Απλό blur", "Simple blur"), Lx("Συμπαγές", "Solid") };
                    var cur = GlassPolicy.ParseMode(AppSettings.Load().GpuGlassMode, AppSettings.Load().UseGpuGlass).ToString();
                    var status = new Label { AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Margin = new Padding(0, 0, 0, 8), MaximumSize = new Size(640, 0), Text = Lx("Τώρα: ", "Now: ") + GlassService.Describe() };
                    var holder = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
                    FxCombo(holder, Lx("Glass σε widgets / DeskStrip / containers", "Glass for widgets / DeskStrip / containers"), names, names[Math.Max(0, Array.IndexOf(modes, cur))],
                        v => { var a = AppSettings.Load(); a.GpuGlassMode = modes[Math.Max(0, Array.IndexOf(names, v))]; a.Save(); status.Text = Lx("Τώρα: ", "Now: ") + GlassService.Describe(); });
                    holder.Controls.Add(status);
                    appearanceAdvanced.AddControl(holder);
                }
                appearanceAdvanced.AddControl(Checkbox(LocalizationManager.T("Settings.DesktopMenu"), AppSettings.Load().DesktopMenuEnabled,
                    v => { var a = AppSettings.Load(); a.DesktopMenuEnabled = v; a.Save(); DesktopMenu.Apply(v); }, LocalizationManager.T("Settings.DesktopMenuDesc")));
                appearanceAdvanced.AddControl(BuildDarkIntensityRow());
                appearance.Controls.Add(appearanceAdvanced);

                // ---- Startup
                var startup = NewCard(LocalizationManager.T("Settings.SectionStartup"), () =>
                {
                    var d = new AppSettings(); var a = AppSettings.Load();
                    a.RestoreLastSession = d.RestoreLastSession; a.EnableAnimations = d.EnableAnimations; a.MinimizeToTray = d.MinimizeToTray; a.Save();
                    AnimationsSettingChanged?.Invoke();
                });
                startup.Controls.Add(Checkbox(LocalizationManager.T("Settings.StartWithWindows"), StartupManager.IsStartupEnabled(), StartupManager.SetStartup, LocalizationManager.T("Settings.StartWithWindowsDesc")));
                startup.Controls.Add(Checkbox(LocalizationManager.T("Settings.StartEarly"), StartupTask.IsEnabled(), v => System.Threading.Tasks.Task.Run(() => StartupTask.Set(v)), LocalizationManager.T("Settings.StartEarlyDesc")));
                startup.Controls.Add(Checkbox(LocalizationManager.T("Settings.RestoreLastSession"), AppSettings.Load().RestoreLastSession,
                    v => { var a = AppSettings.Load(); a.RestoreLastSession = v; a.Save(); }));
                startup.Controls.Add(Checkbox(LocalizationManager.T("Settings.EnableAnimations"), AppSettings.Load().EnableAnimations,
                    v => { var a = AppSettings.Load(); a.EnableAnimations = v; a.Save(); AnimationsSettingChanged?.Invoke(); }));
                startup.Controls.Add(Checkbox(LocalizationManager.T("Settings.MinimizeToTray"), AppSettings.Load().MinimizeToTray,
                    v => { var a = AppSettings.Load(); a.MinimizeToTray = v; a.Save(); }));

                // ---- Plugins and integrations (Advanced: rarely changed)
                var integrations = NewCard(OpsisStrings.T("ui.about.plugins"));
                integrations.Controls.Add(Checkbox(LocalizationManager.T("Settings.PluginsEnabled"), AppSettings.Load().PluginsEnabled,
                    v => { var a = AppSettings.Load(); a.PluginsEnabled = v; a.Save(); }, restart: true));
                var integrationsAdvanced = new StudioAdvancedSection();
                {
                    // (plan 2.1.1: the "default look of plugins" setting was removed - plugins always follow the Opsis theme; the Crawler bars follow Windows by their own rule)
                    var discordHost = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
                    BuildDiscordRows(discordHost);
                    integrationsAdvanced.AddControl(discordHost);
                }
                integrations.Controls.Add(integrationsAdvanced);

                // Ζητήθηκε ρητά "ρύθμιση για διαφάνεια παραθύρων των Windows και γρήγορη εκκίνηση
                // στον πίνακα ελέγχου" — πραγματικές ρυθμίσεις των ίδιων των Windows (μητρώο),
                // όχι δικές μας· βλ. WindowsSystemSettings.cs.
                var windowsCard = NewCard(LocalizationManager.T("Settings.SectionWindows"));
                windowsCard.Controls.Add(Checkbox(LocalizationManager.T("Settings.FastStartup"), WindowsSystemSettings.GetFastStartupEnabled(),
                    v =>
                    {
                        if (!WindowsSystemSettings.TrySetFastStartupEnabled(v))
                            MessageBox.Show(this, LocalizationManager.T("Settings.FastStartupAdminRequired"), LocalizationManager.T("Page.Settings.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));

                // Ζητήθηκε ρητά: ο installer εγκαθιστά ήδη το WebView2 Runtime/.NET 11 Runtime μία
                // φορά, αλλά ο χρήστης πρέπει να μπορεί να δει/εγκαταστήσει ξανά τα εξωτερικά
                // dependencies (π.χ. FFmpeg για .wmv) και από μέσα από την εφαρμογή, χωρίς να
                // ξανατρέξει τον installer.
                var dependencies = NewCard(LocalizationManager.T("Settings.SectionDependencies"));
                var dependenciesAdvanced = new StudioAdvancedSection();
                foreach (var dep in DependencyManagerService.All)
                    dependenciesAdvanced.AddControl(BuildDependencyRow(dep));
                dependencies.Controls.Add(dependenciesAdvanced);
            }

            private static Panel BuildDependencyRow(DependencyInfo dep)
            {
                var row = new Panel { Width = 700, Height = 56, BackColor = UiTheme.Surface, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 8) };
                UiTheme.ApplyRoundedRegion(row, 10);

                var nameLabel = new Label { Text = dep.Name, AutoSize = true, Location = new Point(0, 2), Font = new Font(UiTheme.FontBody.FontFamily, 10, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
                var descLabel = new Label { Text = dep.Description, AutoSize = true, Location = new Point(0, 22), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
                var statusLabel = new Label
                {
                    Text = LocalizationManager.T("Settings.DependencyChecking"),
                    AutoSize = true,
                    Location = new Point(420, 8),
                    Font = new Font(UiTheme.FontBody.FontFamily, 9, FontStyle.Bold),
                    ForeColor = UiTheme.TextMuted
                };
                row.Controls.Add(nameLabel);
                row.Controls.Add(descLabel);
                row.Controls.Add(statusLabel);

                HoverButton? installBtn = null;
                CancellationTokenSource? installCancellation = null;
                row.Disposed += (_, _) => installCancellation?.Cancel();
                void ShowStatus(bool installed)
                {
                    if (row.IsDisposed) return;
                    statusLabel.Text = LocalizationManager.T(installed ? "Settings.DependencyInstalled" : "Settings.DependencyMissing");
                    statusLabel.ForeColor = installed ? Color.FromArgb(60, 210, 140) : Color.FromArgb(235, 120, 66);
                    if (installBtn == null) return;
                    installBtn.Text = LocalizationManager.T(installed ? "Settings.DependencyReinstall" : "Settings.DependencyInstall");
                    installBtn.Enabled = true;
                }

                if (dep.WingetId is { } wingetId)
                {
                    installBtn = new HoverButton { AccessibleDescription = StudioText("Checking the dependency or installing it; this action is temporarily unavailable.", "Έλεγχος ή εγκατάσταση εξάρτησης· η ενέργεια είναι προσωρινά μη διαθέσιμη."), Text = LocalizationManager.T("Settings.DependencyInstall"), Enabled = false, Width = 140, Height = 30, Location = new Point(540, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    installBtn.FlatAppearance.BorderSize = 0;
                    installBtn.Click += async (_, _) =>
                    {
                        if (installCancellation != null)
                        {
                            installCancellation.Cancel();
                            installBtn.Enabled = false;
                            return;
                        }
                        using var current = new CancellationTokenSource();
                        installCancellation = current;
                        installBtn.Text = StudioText("Cancel", "Ακύρωση");
                        installBtn.Enabled = true;
                        statusLabel.Text = LocalizationManager.T("Settings.DependencyInstalling");
                        statusLabel.ForeColor = UiTheme.TextMuted;
                        var (success, output) = await DependencyManagerService.InstallViaWingetAsync(wingetId, current.Token);
                        installCancellation = null;
                        bool nowInstalled = await dep.CheckInstalledAsync();
                        if (row.IsDisposed) return;
                        ShowStatus(nowInstalled);
                        if (!success && !nowInstalled && !current.IsCancellationRequested)
                            MessageBox.Show(string.Format(LocalizationManager.T("Settings.DependencyInstallFailedFormat"), dep.Name, output), "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    };
                    row.Controls.Add(installBtn);
                }
                row.HandleCreated += async (_, _) => ShowStatus(await dep.CheckInstalledAsync());
                return row;
            }

            internal static Panel BuildWidgetsThemeRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WidgetsThemeLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 260 };
                // ΔΙΟΡΘΩΣΗ "δυσκολεύεσαι όταν ακολουθεί το θέμα των Windows να είναι full λευκό ή
                // μαύρο": αντί να προσπαθεί να γίνει ΠΡΑΓΜΑΤΙΚΟ blur-through το περιεχόμενο (απαιτεί
                // WS_EX_LAYERED σε κάθε widget control - μεγάλη αλλαγή αρχιτεκτονικής, μεγάλο ρίσκο),
                // νέα ρητή επιλογή "Πλήρης διαφάνεια": απλά ΔΕΝ ζωγραφίζεται ΚΑΝΕΝΑ φόντο στο
                // περιεχόμενο του widget - ό,τι ήδη δείχνει το glass-blur του ίδιου του παραθύρου
                // (GlassBlur.Apply, το ίδιο μηχανισμό με το ήδη σωστό DeskStrip) μένει ορατό χωρίς
                // κανένα συμπαγές μπλε/άσπρο/μαύρο πλαίσιο από πάνω.
                string[] modes = { "App", "Windows", "Dark", "Light", "Transparent" };
                string[] labels = { LocalizationManager.T("Settings.WidgetsThemeFollowApp"), LocalizationManager.T("Settings.WidgetsThemeFollowWindows"), LocalizationManager.T("Settings.WidgetsThemeAlwaysDark"), LocalizationManager.T("Settings.WidgetsThemeAlwaysLight"), LocalizationManager.T("Settings.WidgetsThemeTransparent") };
                var current = AppSettings.Load().WidgetsThemeMode;
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var a = AppSettings.Load(); a.WidgetsThemeMode = modes[Math.Max(0, combo.SelectedIndex)]; a.Save();
                    ThemeManager.NotifyChanged();
                };
                row.Controls.Add(combo);
                return row;
            }

            // Χειροκίνητο override του HardwareProfileService (v2.0 handoff, Φάση 1 §Α) — "Αυτόματο"
            // αφήνει την ανίχνευση RAM/πυρήνων/GPU να αποφασίσει, οι άλλες επιλογές το παρακάμπτουν
            // μόνιμα (αποθηκεύεται, όχι μόνο για αυτή τη συνεδρία).
            internal static Panel BuildHardwareProfileRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.HardwareProfileLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 260 };
                string[] modes = { "Auto", "Low", "Balanced", "High" };
                string[] labels =
                {
                    LocalizationManager.T("Settings.HardwareProfileAuto"),
                    LocalizationManager.T("Settings.HardwareProfileLow"),
                    LocalizationManager.T("Settings.HardwareProfileBalanced"),
                    LocalizationManager.T("Settings.HardwareProfileHigh"),
                };
                var current = HardwareProfileService.GetOverride()?.ToString() ?? "Auto";
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var selected = modes[Math.Max(0, combo.SelectedIndex)];
                    HardwareProfileService.SetOverride(selected == "Auto" ? null : Enum.Parse<HardwareTier>(selected));
                };
                row.Controls.Add(combo);
                return row;
            }

            internal static Panel BuildWmvOutputRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WmvOutputLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var pathLabel = new Label { Text = WmvConversionService.CacheDir, AutoSize = false, Width = 380, Height = 24, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoEllipsis = true, Padding = new Padding(0, 4, 0, 0) };
                row.Controls.Add(pathLabel);
                var changeBtn = StudioButton(LocalizationManager.T("Settings.ChangeFolder"), () =>
                {
                    using var dlg = new FolderBrowserDialog { Description = LocalizationManager.T("Settings.WmvOutputFolderPrompt"), SelectedPath = WmvConversionService.CacheDir };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        var a = AppSettings.Load(); a.WmvConversionOutputDir = dlg.SelectedPath; a.Save();
                        pathLabel.Text = WmvConversionService.CacheDir;
                    }
                }, 136);
                row.Controls.Add(changeBtn);
                return row;
            }

            // 2.6.0: UI scale / text size. Applies to the shared fonts (headings, body, icons); pages are rebuilt so it shows at once. Text with a fixed size inside a control stays as it is.
            private static Panel BuildUiScaleRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = OpsisStrings.T("ui.uiScale") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 12, 0) });
                var combo = new FlatComboBox { Width = 200 };
                var labels = UiScale.Presets.Select(p => p + "%").ToArray();
                var cur = Array.IndexOf(UiScale.Presets, AppSettings.Load().UiScalePercent); if (cur < 0) cur = 0;
                combo.SetItems(labels, labels[cur]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var percent = UiScale.Presets[Math.Max(0, combo.SelectedIndex)];
                    ApplySetting(OpsisStrings.T("ui.uiScale"), () => { var a = AppSettings.Load(); a.UiScalePercent = percent; a.Save(); UiTheme.ApplyScale(percent); ThemeManager.NotifyChanged(); });
                };
                row.Controls.Add(combo);
                return row;
            }

            // #87: separate text size for card descriptions, while the sidebar and
            // main window keep the regular UI scale.
            private static Panel BuildCardFontScaleRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = MainWindow.StudioText("Settings card text size:", "Μέγεθος κειμένου καρτών:"),
                    AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 12, 0) });
                var combo = new FlatComboBox { Width = 145 };
                var presets = new[] { 85, 100, 110, 125, 150 };
                var labels = presets.Select(n => n + "%").ToArray();
                int current = Array.IndexOf(presets, Math.Clamp(AppSettings.Load().CardFontScalePercent, 85, 150));
                combo.SetItems(labels, labels[current < 0 ? 1 : current]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    int scale = presets[Math.Max(0, combo.SelectedIndex)];
                    ApplySetting(MainWindow.StudioText("Card text", "Κείμενο καρτών"), () =>
                    {
                        var a = AppSettings.Load(); a.CardFontScalePercent = scale; a.Save();
                        UiTheme.ApplyCardFontScale(scale);
                        ThemeManager.NotifyChanged();
                    });
                };
                row.Controls.Add(combo);
                return row;
            }

            // Πραγματικές emoji σημαίες: χρειάζονται τη γραμματοσειρά "Segoe UI Emoji" για να
            // αποδοθούν ως έγχρωμα εικονίδια αντί για γράμματα περιφερειακού δείκτη (GR/GB) —
            // αυτό ήταν το ζητούμενο πρόβλημα ("δεν έχεις βάλει τις σημαίες στο dropdown").
            private static Panel BuildLanguageRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.Language") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                // Σημαίες ως πραγματικά bitmap (βλ. FlagIcons.cs) - τα unicode emoji σημαίες δεν αποδίδονται έγχρωμα σε WinForms.
                // 1.7.8: ΟΛΕΣ οι 14 γλώσσες του GearWin + "Follow Windows" (πρώτη θέση).
                var combo = new FlatComboBox { Width = 240 };
                var codes = new List<string> { "Follow" };
                var labels = new List<string> { LocalizationManager.T("Settings.LanguageFollow") };
                var icons = new List<Image> { FlagIcons.Globe() };
                foreach (var (code, _, native) in LocalizationManager.Supported)
                {
                    codes.Add(code);
                    labels.Add(native);
                    icons.Add(FlagIcons.For(code));
                }
                var currentLang = AppSettings.Load().Language;
                var idx = codes.IndexOf(currentLang);
                combo.SetItems(labels, icons, labels[idx < 0 ? 0 : idx]);
                combo.SelectedIndexChanged += (_, _) => LocalizationManager.SetLanguage(codes[combo.SelectedIndex]);
                row.Controls.Add(combo);
                return row;
            }

            // Ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση ενός media player" (MediaPlayerPro, αδελφό
            // project) στα checkboxes/κουμπιά — FlatCheckBox (custom-σχεδιασμένο, στο ίδιο πνεύμα)
            // αντικαθιστά εδώ το προεπιλεγμένο CheckBox των Windows σε ΟΛΗ τη σελίδα Ρυθμίσεων με
            // μία αλλαγή, αφού κάθε checkbox της σελίδας περνάει από αυτό το ίδιο helper.
            // Windows 11 style row: the text on the left, the switch at the right end (Opsis.UI.FlatToggle). `description` is one muted sentence under the
            // text; `restart` adds the "Restart needed" tag (everything else applies immediately).
            internal static CheckBox Checkbox(string text, bool initial, Action<bool> onChange, string description = "", bool restart = false)
            {
                var box = new FlatToggle
                {
                    Text = text,
                    Description = description,
                    Badge = restart ? OpsisStrings.T("ui.restartNeeded") : "",
                    RowWidth = 700,
                    Checked = initial,
                    BackColor = UiTheme.PageBackground,
                    Margin = new Padding(0, 2, 0, 2)
                };
                box.CheckedChanged += (_, _) =>
                {
                    ApplySetting(text, () => onChange(box.Checked));
                    // never prompt (or relaunch!) in the automated UI audit / e2e runs: they click every switch
                    if (restart && Environment.GetEnvironmentVariable("OPSIS_E2E") != "1")
                    {
                        // 2.6.0: the shared mechanism of Opsis.UI - "Restart now / Later" and an automatic relaunch (through the normal exit path: save session, stop plugins, hotkeys).
                        RestartCoordinator.Require(CleanLabel(text));
                        RestartPrompt.AskIfPending(box.FindForm(), MotionDesk.Widgets.TrayApplicationContext.RequestExit);
                    }
                };
                return box;
            }

            // "Διαφάνεια εφαρμογής" ανάλογη της γνωστής επιλογής διαφάνειας παραθύρων των Windows —
            // εδώ εφαρμόζεται στο ΚΥΡΙΟ παράθυρο μέσω Form.Opacity (πραγματική λειτουργία, όχι
            // απλή προσομοίωση), με ζωντανό preview καθώς σέρνεις το slider.
            private FlowLayoutPanel BuildOpacityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WindowOpacityLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = (int)Math.Round(WindowOpacityPolicy.Normalize(AppSettings.Load().WindowOpacity) * 100);
                var track = new FlatSlider { Name = "settings-window-opacity", Minimum = 60, Maximum = 100,
                    Value = initial, Width = 200, SmallChange = 1, LargeChange = 5,
                    AccessibleName = LocalizationManager.T("Settings.WindowOpacityLabel").TrimEnd(':') };
                static string DescribeOpacity(int percent) => StudioText($"{percent}% opaque", $"{percent}% αδιαφάνεια");
                var valueLabel = new Label { Text = DescribeOpacity(initial), AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.ValueChanged += (_, _) =>
                {
                    valueLabel.Text = DescribeOpacity(track.Value);
                    if (FindForm() is MainWindow form) form.ApplyWindowOpacity(track.Value / 100.0, save: true);
                };
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            // Πόσο σκούρο είναι το dark theme — ζητήθηκε ρητά. 0 = πιο ανοιχτό σκούρο, 100 = σχεδόν
            // μαύρο, 50 = η αρχική, αμετάβλητη βάση.
            //
            // Bug fix: πριν, κάθε Scroll tick (δεκάδες/δευτερόλεπτο ενώ σέρνεις) καλούσε απευθείας
            // το ΑΚΡΙΒΟ SetDarkIntensity (save σε δίσκο + πλήρης ανακατασκευή σελίδας/sidebar) —
            // ανέφερε "τρεμόπαιγμα" και "πηγαίνει κάθε 5%" επειδή το ίδιο το TrackBar καταστρεφόταν
            // και ξαναφτιαχνόταν ενώ ο χρήστης ακόμη το έσερνε. Τώρα: Scroll κάνει μόνο ΦΤΗΝΗ
            // προεπισκόπηση (PreviewDarkIntensity — Invalidate, καμία ανακατασκευή), και η ακριβή
            // εφαρμογή/αποθήκευση γίνεται ΜΙΑ φορά στο MouseUp (όταν αφήνει το slider).
            private FlowLayoutPanel BuildDarkIntensityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.DarkIntensity") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().DarkIntensity * 100), 0, 100);
                var track = new FlatSlider { Minimum = 0, Maximum = 100, Value = initial, Width = 200, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    ThemeManager.PreviewDarkIntensity(track.Value / 100.0);
                };
                track.MouseUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0);
                track.KeyUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0); // πληκτρολόγιο/βελάκια: δεν έχουν MouseUp
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            internal static FlowLayoutPanel NumericRow(string label, int value, int min, int max, Action<int> onChange, int marginLeft = 0, string unit = "px")
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(marginLeft, 2, 0, 4) };
                row.Controls.Add(new Label { Text = label + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var numeric = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 80 };   // a stored value outside the range must never break the page
                numeric.ValueChanged += (_, _) => ApplySetting(label, () => onChange((int)numeric.Value));
                row.Controls.Add(numeric);
                row.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) });
                return row;
            }
        }


    }
}

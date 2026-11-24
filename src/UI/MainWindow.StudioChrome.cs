using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private static readonly string[] DesktopPages = ["Wallpaper", "DeskContainers", "Widgets", "DeskStrip", "DeskZones", "DeskMusicVisualizer"];
    private static readonly string[] SystemPages = ["Personalization", "Theme", "Automation", "AudioEnhancement", "Performance", "Settings"];
    private readonly Dictionary<string, StudioLink> _studioLinks = new(StringComparer.Ordinal);
    private WindowSkin _windowSkin;
    private bool _usesStudio => _windowSkin != WindowSkin.Classic;
    private ToolTip? _studioNavTips;
    private Button? _studioMore;
    private bool _iconRail => _windowSkin == WindowSkin.Inspector;
    private FlowLayoutPanel? _studioNavigation;
    private Panel? _studioBrand;
    private Panel? _studioRailRule;

    private static WindowSkin SelectedSkin(AppSettings settings) => WindowSkins.FromSettings(settings.MainWindowSkin, settings.WindowSkinRevision);

    private void BuildSelectedChrome()
    {
        if (_usesStudio) BuildStudioChrome();
        else
        {
            BuildChrome();
            if (_sidebarCollapsed)
            {
                _sidebar.Width = SidebarCollapsedWidth;
                ApplySidebarCollapsedVisuals(true);
            }
            _brandPulseTimer = new System.Windows.Forms.Timer { Interval = HardwareProfileService.ScaleDecorativeInterval(60) };
            _brandPulseTimer.Tick += (_, _) => { _brandPulseT += .08; _brandDot.Invalidate(); };
        }
        Text = "Opsis - Desktop Customization";
        ApplyChromeColors();
        UpdateAnimationTimers();
    }

    internal void SetWindowSkin(WindowSkin skin)
    {
        if (skin == _windowSkin) return;
        BeginInvoke(() =>
        {
            if (IsDisposed || skin == _windowSkin) return;
            var settings = AppSettings.Load();
            settings.MainWindowSkin = skin.ToString();
            settings.WindowSkinRevision = 1;
            settings.Save();
            Flicker.BatchRedraw(this, () =>
            {
                SuspendLayout();
                try
                {
                    _brandPulseTimer?.Dispose();
                    _brandPulseTimer = null;
                    _headerWaveTimer?.Dispose();
                    _headerWaveTimer = null;
                    _sidebarAnimTimer?.Dispose();
                    _sidebarAnimTimer = null;
                    _universalSearchResults = null;
                    _universalSearchField = null;
                    _studioNavTips?.Dispose();
                    _studioNavTips = null;
                    _studioMore = null;
                    _studioBrand = null;
                    _studioNavigation = null;
                    _studioRailRule = null;
                    _studioLinks.Clear();
                    DisposeChildren(this);
                    _navBaseHeight = null;
                    _windowPalette = null;
                    _windowSkin = skin;
                    BuildSelectedChrome();
                    NavigateTo(_navButtons.ContainsKey(_currentPageKey) ? _currentPageKey : "Dashboard");
                    UpdateStatus();
                }
                finally { ResumeLayout(true); }
            });
        });
    }

    private void BuildStudioChrome()
    {
        bool top = _windowSkin.HasTopNavigation();
        bool rail = _iconRail;
        var frame = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty
        };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, top ? 0 : rail ? 64 : 228));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, top ? 0 : 1));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, top ? 47 : 0));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _studioBrand = new Panel { Dock = top ? DockStyle.Left : DockStyle.Fill, Width = 122, Margin = Padding.Empty };
        _studioBrand.Paint += (_, e) =>
        {
            if (Icon != null) e.Graphics.DrawIcon(Icon, new Rectangle(rail ? 18 : 16, 15, 28, 28));
            if (!rail) TextRenderer.DrawText(e.Graphics, "Opsis", UiTheme.FontSubheading, new Rectangle(54, 0, 68, 58),
                CurrentSkinColors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        _studioRailRule = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _studioNavigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = top ? FlowDirection.LeftToRight : FlowDirection.TopDown, WrapContents = false,
            AutoScroll = !top, Padding = top ? new Padding(16, 4, 10, 4) : new Padding(rail ? 5 : 10, 0, rail ? 5 : 10, 8),
            Margin = Padding.Empty
        };
        if (!top) EnableWheelPassthrough(_studioNavigation);
        _studioNavigation.ClientSizeChanged += (_, _) => SizeStudioNavigation();

        _header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(rail ? 16 : 12, 0, 12, 0) };
        _pageTitle = new Label
        {
            Dock = DockStyle.Fill, Font = UiTheme.FontSubheading, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, UseMnemonic = false
        };
        _headerRule = new Panel { Dock = DockStyle.Bottom, Height = 1 };
        _header.Controls.AddRange([_pageTitle, _headerRule]);
        if (top) _header.Controls.Add(_studioBrand);
        AttachUniversalSearch();
        _header.Controls.SetChildIndex(_pageTitle, 0);

        _content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16, 10, 16, 8) };
        _nav = new FlowLayoutPanel { Visible = false };
        _studioBrand.Controls.Add(_nav);
        _status = new OpsisStatusBar { Dock = DockStyle.Fill, Margin = Padding.Empty, Status = "Opsis ready" };
        _statusLabel.Bar = _status;
        if (!top)
        {
            frame.Controls.Add(_studioBrand, 0, 0);
            frame.Controls.Add(_studioNavigation, 0, 2);
            frame.Controls.Add(_studioRailRule, 1, 0);
            frame.SetRowSpan(_studioRailRule, 3);
        }
        else frame.Controls.Add(_studioNavigation, 2, 1);
        frame.Controls.Add(_header, 2, 0);
        frame.Controls.Add(_content, 2, 2);
        frame.Controls.Add(_status, 0, 3);
        frame.SetColumnSpan(_status, 3);
        Controls.Add(frame);
        RebuildSidebarNav();
    }

    private void RebuildStudioNavigation()
    {
        if (_studioNavigation is not { } nav) return;
        nav.SuspendLayout();
        try
        {
            DisposeChildren(nav);
            _studioLinks.Clear();
            _studioNavTips?.Dispose();
            _studioNavTips = new ToolTip();
            _studioMore = null;
            bool top = _windowSkin.HasTopNavigation();
            void AddLink(string key)
            {
                if (!_navButtons.TryGetValue(key, out var page)) return;
                var link = new StudioLink(key)
                {
                    Text = top ? CompactPageName(key, page.Text) : page.Text,
                    AccessibleName = page.AccessibleName ?? page.Text,
                    Height = top || _iconRail ? 38 : 32, IconOnly = _iconRail, TopLink = top
                };
                _studioNavTips?.SetToolTip(link, page.Text);
                _studioLinks[key] = link;
                link.Click += (_, _) => NavigateTo(key);
                nav.Controls.Add(link);
            }
            void AddGroup(string caption, string[] keys)
            {
                if (keys.Length == 0) return;
                nav.Controls.Add(new Label
                {
                    Text = caption, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, TextAlign = ContentAlignment.BottomLeft,
                    Height = 25, Padding = new Padding(9, 0, 0, 3), Margin = new Padding(0, 3, 0, 2), UseMnemonic = false
                });
                foreach (string key in keys) AddLink(key);
            }

            AddLink("Dashboard");
            if (top)
            {
                foreach (var key in new[] { "Wallpaper", "DeskContainers", "Widgets", "DeskMusicVisualizer", "Theme", "Settings" }) AddLink(key);
                AddMoreNavigation(nav);
                SizeStudioNavigation();
                UpdateStudioNavigation();
                return;
            }
            if (_iconRail)
            {
                foreach (var key in DesktopPages.Concat(SystemPages)) AddLink(key);
                AddMoreNavigation(nav);
                SizeStudioNavigation();
                UpdateStudioNavigation();
                return;
            }
            AddGroup(StudioText("Desktop", "Επιφάνεια εργασίας"), DesktopPages);
            AddGroup(StudioText("System", "Σύστημα"), SystemPages);
            AddGroup(StudioText("Add-ons", "Πρόσθετα"), _navButtons.Keys.Where(key => !_studioLinks.ContainsKey(key) && key != "About").ToArray());
            AddLink("About");
            SizeStudioNavigation();
            UpdateStudioNavigation();
        }
        finally { nav.ResumeLayout(true); }
    }

    private void SizeStudioNavigation()
    {
        if (_studioNavigation is not { } nav) return;
        if (_windowSkin.HasTopNavigation())
        {
            int width = Math.Clamp((nav.ClientSize.Width - nav.Padding.Horizontal - 64) / 7 - 3, 58, 140);
            foreach (var link in _studioLinks.Values) link.Width = width;
            return;
        }
        int sideWidth = Math.Max(1, nav.ClientSize.Width - nav.Padding.Horizontal - (_iconRail ? 0 : SystemInformation.VerticalScrollBarWidth));
        foreach (Control item in nav.Controls) item.Width = sideWidth;
    }

    private static string CompactPageName(string key, string label) => key switch
    {
        "DeskContainers" => "Containers",
        "DeskMusicVisualizer" => StudioText("Music", "Μουσική"),
        _ => label
    };

    private void AddMoreNavigation(FlowLayoutPanel nav)
    {
        _studioMore = StudioButton("⋯", () =>
        {
            if (_studioMore is not { } anchor) return;
            var menu = new ContextMenuStrip();
            foreach (var (key, page) in _navButtons)
            {
                if (_studioLinks.ContainsKey(key)) continue;
                menu.Items.Add(page.Text, null, (_, _) => NavigateTo(key));
            }
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(anchor, new Point(0, anchor.Height));
        }, 52);
        _studioMore.AccessibleName = StudioText("More pages", "Περισσότερες σελίδες");
        _studioNavTips?.SetToolTip(_studioMore, _studioMore.AccessibleName);
        nav.Controls.Add(_studioMore);
    }

    private void UpdateStudioNavigation()
    {
        foreach (var (key, link) in _studioLinks)
        {
            link.CurrentPage = key == _currentPageKey;
            link.Invalidate();
        }
    }

    private void ColorStudioChrome()
    {
        if (_studioNavigation is not { } nav || _studioBrand == null || _studioRailRule == null) return;
        var palette = CurrentSkinColors;
        nav.BackColor = palette.Navigation;
        _studioBrand.BackColor = _windowSkin.HasTopNavigation() ? palette.Surface : palette.Navigation;
        _studioRailRule.BackColor = palette.Border;
        _header.BackColor = palette.Surface;
        _headerRule.BackColor = palette.Border;
        _pageTitle.ForeColor = palette.Text;
        _content.BackColor = palette.Background;
        _status.BackColor = palette.Navigation;
        _statusLabel.ForeColor = palette.Secondary;
        foreach (var label in nav.Controls.OfType<Label>()) label.ForeColor = palette.Secondary;
        if (_studioMore is HoverButton more)
        {
            more.BaseColor = more.BackColor = palette.Surface;
            more.HoverBackColor = palette.Hover;
            more.ForeColor = palette.Text;
        }
        UpdateStudioNavigation();
    }

    private static Color BlendChrome(Color baseColor, Color accent, float amount) => Color.FromArgb(
        (int)(baseColor.R + (accent.R - baseColor.R) * amount),
        (int)(baseColor.G + (accent.G - baseColor.G) * amount),
        (int)(baseColor.B + (accent.B - baseColor.B) * amount));

    private void ShowStudioHome()
    {
        _currentPageKey = "Dashboard";
        var page = CreatePagePanel();
        AddHeading(page, $"Opsis - Desktop Customization {AppVersion.Label}");
        AddText(page, StudioText("Arrange windows with DeskZones, group files in DeskContainers and bring your desktop to life with weather, widgets and music.", "Οργάνωσε παράθυρα με DeskZones και αρχεία με DeskContainers. Πρόσθεσε ζωντανό καιρό, widgets και οπτικοποίηση μουσικής."));
        AddSection(page, StudioText("Desktop", "Επιφάνεια εργασίας"));
        var wallpaper = WallpaperSettings.Load();
        AddLink("Wallpaper", StudioText("Wallpaper", "Φόντο επιφάνειας"),
            WallpaperHostEngine.Instance.IsEnabled ? WallpaperModeLabel(wallpaper.Mode) : StudioText("Wallpaper is paused", "Το φόντο είναι ανενεργό"));
        AddLink("DeskContainers", "DeskContainers", StudioText("Organize desktop files and shortcuts", "Οργάνωση αρχείων και συντομεύσεων"));
        AddLink("Widgets", "Widgets", StudioText("Manage and add widgets", "Διαχείριση και προσθήκη widgets"));
        AddLink("DeskStrip", "DeskStrip", StudioText("Desktop toolbar and dock", "Γραμμή εργαλείων και dock"));
        AddSection(page, StudioText("Appearance and settings", "Εμφάνιση και ρυθμίσεις"));
        AddLink("Theme", StudioText("Theme", "Θέμα"), StudioText("Colors, materials and window skin", "Χρώματα, υλικά και εμφάνιση παραθύρου"));
        AddLink("DeskMusicVisualizer", "Desk Music Visualizer", StudioText("Desktop visualization and track details", "Οπτικοποίηση και στοιχεία τραγουδιού"));
        AddLink("Settings", StudioText("Settings", "Ρυθμίσεις"), null);
        SetPage("Nav.Dashboard", page);

        void AddLink(string key, string title, string? detail)
        {
            var link = new StudioLink(key) { Text = title, Detail = detail, AccessibleName = title, Height = detail == null ? 42 : 58, Width = 680 };
            link.Click += (_, _) => NavigateTo(key);
            page.Controls.Add(link);
            void Fit() => link.Width = Math.Max(120, Math.Min(760, page.ClientSize.Width - page.Padding.Horizontal - 24));
            page.ClientSizeChanged += (_, _) => Fit();
            Fit();
        }
    }

    private sealed class StudioLink : Button
    {
        private readonly string _key;
        internal string? Detail { get; init; }
        internal bool CurrentPage { get; set; }
        internal bool IconOnly { get; init; }
        internal bool TopLink { get; init; }

        internal StudioLink(string key)
        {
            _key = key;
            Margin = new Padding(0, 0, 0, 2);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += (_, _) => Invalidate();
            MouseLeave += (_, _) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 33 || Height < 13) return;
            var g = e.Graphics;
            bool isRow = Height >= 42 && !IconOnly && !TopLink;
            var palette = (FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
            Color accent = palette.Accent;
            bool hot = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            PaintActionBackdrop(this, e);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shape = UiTheme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 8);
            if (hot || CurrentPage || isRow)
            {
                using var fill = new SolidBrush(hot || CurrentPage ? palette.Hover : palette.Surface);
                g.FillPath(fill, shape);
            }
            if (isRow || Focused)
            {
                using var border = new Pen(Focused ? accent : palette.Border);
                g.DrawPath(border, shape);
            }
            if (CurrentPage)
            {
                using var bar = new SolidBrush(accent);
                if (TopLink) g.FillRectangle(bar, 8, Height - 4, Width - 16, 2);
                else g.FillRectangle(bar, 3, 8, 2, Height - 16);
            }
            var iconColor = CurrentPage || isRow ? accent : palette.Secondary;
            if (!TopLink) IconRenderer.Draw(g, _key, new RectangleF(IconOnly ? (Width - 18) / 2f : 12, (Height - 18) / 2f, 18, 18), iconColor);
            if (IconOnly) return;
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (TopLink)
            {
                TextRenderer.DrawText(g, Text, UiTheme.FontBody, ClientRectangle, CurrentPage ? accent : palette.Text, flags | TextFormatFlags.HorizontalCenter);
                return;
            }
            int right = isRow ? 30 : 5;
            int titleY = Detail == null ? 0 : 5;
            int titleHeight = Detail == null ? Height : 26;
            TextRenderer.DrawText(g, Text, UiTheme.FontBody, new Rectangle(37, titleY, Math.Max(1, Width - 37 - right), titleHeight), palette.Text, flags);
            if (Detail != null)
                TextRenderer.DrawText(g, Detail, UiTheme.FontBody, new Rectangle(37, 29, Math.Max(1, Width - 37 - right), 22), palette.Secondary, flags);
            if (isRow)
            {
                using var arrow = new Pen(palette.Secondary, 1.4f);
                int x = Width - 17, y = Height / 2;
                g.DrawLines(arrow, [new Point(x - 3, y - 4), new Point(x + 1, y), new Point(x - 3, y + 4)]);
            }
        }
    }
}

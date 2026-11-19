using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    /// <summary>
    /// In-app help of Opsis (F1 / the "?" button): how to use, the pages, the keyboard shortcuts, the restore commands and the version history.
    /// Modeless, one instance (a second F1 closes it), colours from UiTheme and live theme change, text from the locale files (Help.*).
    /// </summary>
    internal sealed class HelpForm : Form
    {
        private static HelpForm? _instance;
        internal static HelpForm? Current => _instance != null && !_instance.IsDisposed ? _instance : null;
        internal void SelectTabForAudit(int i) => SelectTab(i);

        /// <summary>The pages of the help: (page key, title key, description key). Internal so a test can check every key exists in every language.</summary>
        internal static readonly (string Page, string TitleKey, string IntroKey)[] Pages =
        {
            ("Dashboard", "Nav.Dashboard", "Dashboard.Intro"),
            ("Widgets", "Nav.Widgets", "Widgets.Intro"),
            ("DeskZones", "Nav.DeskZones", "DeskZones.Intro"),
            ("DeskContainers", "DeskZones.SectionContainers", "DeskZones.ContainersIntro"),
            ("DeskStrip", "Personalization.SectionDeskStrip", "Personalization.DeskStripIntro"),
            ("Wallpaper", "Nav.Wallpaper", "Wallpaper.Intro"),
            ("Performance", "Nav.Performance", "Performance.Intro"),
            ("Settings", "Nav.Settings", "Settings.Intro"),
            ("Automation", "Nav.Automation", "Automation.Intro"),
            ("Personalization", "Nav.Personalization", "Personalization.Intro"),
            ("AudioEnhancement", "Nav.AudioEnhancement", "AudioEnhancement.Intro"),
        };

        /// <summary>The commands the Restore tab lists (the command line is the same in every language).</summary>
        internal static readonly string[] RestoreCommands = { "--restore-desktop-icons", "--restore-lockscreen" };

        private readonly string _currentPage;
        private readonly Func<IEnumerable<Control>> _history;
        private readonly List<Button> _tabButtons = new();
        private readonly List<Panel> _tabPanels = new();
        private readonly Panel _body = new() { Dock = DockStyle.Fill };
        private int _active;

        public static void ShowHelp(IWin32Window? owner, string currentPage, Func<IEnumerable<Control>> history)
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                if (_instance.WindowState == FormWindowState.Minimized) _instance.WindowState = FormWindowState.Normal;
                _instance.BringToFront(); _instance.Activate();
                return;
            }
            _instance = new HelpForm(currentPage, history);
            if (owner != null) _instance.Show(owner); else _instance.Show();
        }

        private HelpForm(string currentPage, Func<IEnumerable<Control>> history)
        {
            _currentPage = currentPage; _history = history;
            Text = LocalizationManager.T("Help.Title");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 640); MinimumSize = new Size(520, 420);
            ShowInTaskbar = false;
            KeyPreview = true;
            if (LocalizationManager.CurrentLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1) { Close(); e.Handled = true; } };
            HandleCreated += (_, _) => ApplyDarkTitle();
            ThemeManager.Changed += OnThemeChanged;
            FormClosed += (_, _) => { ThemeManager.Changed -= OnThemeChanged; if (ReferenceEquals(_instance, this)) _instance = null; };
            Build();
        }

        private void OnThemeChanged() { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(new Action(Rebuild)); else Rebuild(); }

        private void Rebuild()
        {
            int keep = _active;
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
            _tabButtons.Clear(); _tabPanels.Clear();
            Build(); SelectTab(keep);
            ResumeLayout(true); ApplyDarkTitle();
        }

        private void ApplyDarkTitle()
        {
            try { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; if (IsHandleCreated) DwmSetWindowAttribute(Handle, 20, ref d, sizeof(int)); } catch (DllNotFoundException) { }
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void Build()
        {
            BackColor = UiTheme.PageBackground; ForeColor = UiTheme.TextPrimary; Font = UiTheme.FontBody;
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 10, 12, 6), WrapContents = true, BackColor = UiTheme.PageBackground };
            string[] names = { "Help.TabGuide", "Help.TabPages", "Help.TabShortcuts", "Help.TabRestore", "Help.TabHistory" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button { Text = LocalizationManager.T(names[i]), AutoSize = true, MinimumSize = new Size(0, 32), FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 6, 4), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (_, _) => SelectTab(idx);
                _tabButtons.Add(b); tabs.Controls.Add(b);
            }
            var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PageBackground };
            _tabPanels.Add(Scroller(BuildGuide));
            _tabPanels.Add(Scroller(BuildPages));
            _tabPanels.Add(Scroller(BuildShortcuts));
            _tabPanels.Add(Scroller(BuildRestore));
            _tabPanels.Add(Scroller(BuildHistory));
            foreach (var p in _tabPanels) { p.Visible = false; body.Controls.Add(p); }
            Controls.Add(body);
            Controls.Add(tabs);
            SelectTab(0);
        }

        private void SelectTab(int i)
        {
            _active = Math.Clamp(i, 0, _tabPanels.Count - 1);
            for (int k = 0; k < _tabPanels.Count; k++)
            {
                _tabPanels[k].Visible = k == _active;
                _tabButtons[k].BackColor = k == _active ? UiTheme.AccentCyan : UiTheme.Surface;
                _tabButtons[k].ForeColor = k == _active ? Color.White : UiTheme.TextPrimary;
            }
        }

        private static Panel Scroller(Action<FlowLayoutPanel> fill)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18, 8, 18, 18), BackColor = UiTheme.PageBackground };
            GlassBlur.ApplyScrollbarTheme(p, UiTheme.Background.GetBrightness() < 0.5f);
            fill(p);
            // the text must wrap at the width of the window, not run off to the right
            void Fit() { int w = Math.Max(200, p.ClientSize.Width - p.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth); foreach (Control c in p.Controls) if (c is Label l) l.MaximumSize = new Size(w, 0); }
            p.Resize += (_, _) => Fit(); p.HandleCreated += (_, _) => Fit();
            return p;
        }

        private static Label Line(string text, bool heading = false, bool mono = false, Color? color = null) => new()
        {
            AutoSize = true, Text = text,
            Font = heading ? new Font("Segoe UI", 10.5f, FontStyle.Bold) : mono ? new Font("Consolas", 10f, FontStyle.Bold) : UiTheme.FontBody,
            ForeColor = color ?? (heading ? UiTheme.AccentCyan : UiTheme.TextPrimary),
            Margin = new Padding(0, heading ? 12 : 2, 0, heading ? 4 : 6)
        };

        private void BuildGuide(FlowLayoutPanel p)
        {
            foreach (var line in LocalizationManager.T("About.InstructionsList").Split('\n', StringSplitOptions.RemoveEmptyEntries)) p.Controls.Add(Line(line.Trim()));
        }

        private void BuildPages(FlowLayoutPanel p)
        {
            foreach (var (page, titleKey, introKey) in Pages)
            {
                bool here = page == _currentPage;
                p.Controls.Add(Line(LocalizationManager.T(titleKey) + (here ? "   •   " + LocalizationManager.T("Help.CurrentPage") : ""), heading: true));
                p.Controls.Add(Line(LocalizationManager.T(introKey), color: UiTheme.TextSecondary));
            }
        }

        private void BuildShortcuts(FlowLayoutPanel p)
        {
            // one source of truth: the same card (and About.ShortcutsList) as the About page
            p.Controls.Add(new MainWindow.ShortcutsCard(LocalizationManager.T("About.ShortcutsList")) { Margin = new Padding(0, 0, 0, 14) });
        }

        private void BuildRestore(FlowLayoutPanel p)
        {
            p.Controls.Add(Line(LocalizationManager.T("Help.RestoreIntro"), color: UiTheme.TextSecondary));
            foreach (var key in new[] { "Help.RestoreDesktopIcons", "Help.RestoreLockscreen" })
            {
                var parts = LocalizationManager.T(key).Split('\n', 2);
                p.Controls.Add(Line(parts[0], mono: true, color: UiTheme.AccentCyan));
                if (parts.Length > 1) p.Controls.Add(Line(parts[1]));
            }
        }

        private void BuildHistory(FlowLayoutPanel p)
        {
            foreach (var c in _history()) p.Controls.Add(c);
        }
    }

    /// <summary>The "?" button of the header: a circle with a question mark drawn as a vector (no emoji), tooltip "Help (F1)".</summary>
    internal sealed class HelpGlyphButton : Control
    {
        private bool _hover;
        public HelpGlyphButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(36, 36); Cursor = Cursors.Hand; BackColor = Color.Transparent; TabStop = true;
            new Opsis.UI.FlatToolTip().SetToolTip(this, LocalizationManager.T("Help.Tooltip"));
            AccessibleName = LocalizationManager.T("Help.Title"); AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(6, 6, Width - 13, Height - 13);
            using (var br = new SolidBrush(_hover ? Color.FromArgb(60, UiTheme.AccentCyan) : Color.Transparent)) g.FillEllipse(br, r);
            using var pen = new Pen(_hover ? UiTheme.AccentCyan : UiTheme.TextSecondary, 1.8f);
            g.DrawEllipse(pen, r);
            using var f = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, "?", f, r, pen.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Text.Json;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    // Theme page: the palettes imported from GearWin (colours only), each as a dark and a light preview that applies live when clicked.
    public sealed partial class MainWindow
    {
        private sealed class PaletteStrip : Control
        {
            private readonly UiPalette _p; private readonly bool _selected;
            public PaletteStrip(UiPalette p, bool selected, string tip, Action onClick)
            {
                _p = p; _selected = selected; Size = new Size(190, 34); Cursor = Cursors.Hand; Margin = new Padding(0, 2, 12, 2);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                var tooltip = new ToolTip();
                tooltip.SetToolTip(this, tip);
                Disposed += (_, _) => tooltip.Dispose();
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                Click += (_, _) => onClick();
                KeyDown += (_, e) =>
                {
                    if (e.KeyCode is not (Keys.Enter or Keys.Space)) return;
                    e.SuppressKeyPress = true;
                    onClick();
                };
                GotFocus += (_, _) => Invalidate();
                LostFocus += (_, _) => Invalidate();
                AccessibleName = tip; AccessibleRole = AccessibleRole.PushButton;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = UiTheme.RoundedPath(r, 8)) { using var bg = new SolidBrush(_p.Background); g.FillPath(bg, path); using var pen = new Pen(_selected || Focused ? _p.AccentCyan : _p.Border, _selected || Focused ? 2.5f : 1f); g.DrawPath(pen, path); }
                var chips = new[] { _p.Sidebar, _p.Surface, _p.SurfaceHover, _p.AccentCyan, _p.TextPrimary };
                int x = 10, w = (Width - 20 - 4 * 6) / 5;
                foreach (var c in chips) { using var b = new SolidBrush(c); g.FillRectangle(b, x, 9, w, Height - 20); using var bp = new Pen(Color.FromArgb(90, _p.TextSecondary)); g.DrawRectangle(bp, x, 9, w, Height - 20); x += w + 6; }
            }
        }

        // Roadmap #81/#82: visual inspection without changing the real palette.
        // The longer labels come from the actual packaged locale files (not fake
        // example translations), so text overflow can be reviewed before release.
        private void ShowThemeReadabilityPreview()
        {
            using var dialog = new Form
            {
                Text = StudioText("Theme accessibility preview", "Προεπισκόπηση προσβασιμότητας θέματος"),
                Size = new Size(900, 560), MinimumSize = new Size(710, 450),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 4, ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Label
            {
                Text = StudioText("Buttons: normal / hover / pressed / keyboard focus / disabled",
                    "Κουμπιά: κανονικό / hover / πάτημα / εστίαση / ανενεργό"),
                AutoSize = true, Font = UiTheme.FontSubheading, ForeColor = UiTheme.TextPrimary
            }, 0, 0);
            var states = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            var names = new[]
            {
                StudioText("Normal", "Κανονικό"), StudioText("Hover", "Hover"),
                StudioText("Pressed", "Πατημένο"), StudioText("Focused", "Εστίαση"),
                StudioText("Disabled", "Ανενεργό")
            };
            var backs = new[] { UiTheme.Surface, UiTheme.SurfaceHover, UiTheme.AccentCyan, UiTheme.Surface, UiTheme.Surface };
            for (int i = 0; i < names.Length; i++)
            {
                var sample = new Button
                {
                    Text = names[i], Width = 140, Height = 36, Margin = new Padding(4, 3, 9, 0),
                    BackColor = backs[i], ForeColor = i == 2 ? Color.Black : UiTheme.TextPrimary,
                    FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                    AccessibleName = names[i], Enabled = i != 4
                };
                sample.FlatAppearance.BorderSize = i == 3 ? 2 : 1;
                sample.FlatAppearance.BorderColor = i == 3 ? UiTheme.AccentCyan : UiTheme.Border;
                states.Controls.Add(sample);
            }
            root.Controls.Add(states, 0, 1);
            root.Controls.Add(new Label
            {
                Text = StudioText("Longest available button/setting translations (actual installed locales):",
                    "Μεγαλύτερες μεταφράσεις κουμπιών/ρυθμίσεων από τα εγκατεστημένα αρχεία:"),
                AutoSize = true, Font = UiTheme.FontSubheading, ForeColor = UiTheme.TextPrimary
            }, 0, 2);
            var list = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false,
                Font = UiTheme.FontCard, BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary, HorizontalScrollbar = true
            };
            var samples = new List<(string Lang, string Key, string Value)>();
            foreach (var lang in LocalizationManager.Supported)
            {
                string filename = Path.Combine(AppContext.BaseDirectory, "locales", lang.File);
                if (!File.Exists(filename)) continue;
                try
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filename));
                    if (values == null) continue;
                    foreach (var (key, value) in values)
                        if ((key.StartsWith("Settings.", StringComparison.Ordinal) ||
                             key.StartsWith("Command.", StringComparison.Ordinal) ||
                             key.StartsWith("Nav.", StringComparison.Ordinal)) &&
                            !string.IsNullOrWhiteSpace(value) && !value.Contains('\n'))
                            samples.Add((lang.Native, key, value));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { MotionDesk.Services.RotatingLogger.Write("Theme locale preview: " + ex.GetType().Name); }
            }
            foreach (var sample in samples.OrderByDescending(x => x.Value.Length).Take(80))
                list.Items.Add($"{sample.Lang}  ·  {sample.Key}  ·  {sample.Value}");
            if (list.Items.Count == 0)
                list.Items.Add(StudioText("No localised labels found", "Δεν βρέθηκαν μεταφράσεις"));
            root.Controls.Add(list, 0, 3);
            dialog.Controls.Add(root);
            dialog.ShowDialog(this);
        }

        private void BuildGearThemeCards(Control panel)
        {
            var current = AppSettings.Load().ThemeMode;
            var card = AddCardSection(panel, Lx("Παλέτες GearWin (σκούρο και φωτεινό)", "GearWin palettes (dark and light)"));
            AddText(card, Lx("Μόνο χρώματα: χωρίς κινούμενο φόντο. Κάνε κλικ σε σκούρο ή φωτεινό δείγμα για άμεση εφαρμογή.", "Colours only: no animated background. Click a dark or light sample to apply it at once."));
            var rtlPreview = new Button
            {
                Text = Lx("Preview Arabic right-to-left layout…", "Προεπισκόπηση αραβικών (δεξιά προς αριστερά)…"),
                AutoSize = true, Height = 34, Margin = new Padding(0, 6, 0, 12)
            };
            rtlPreview.Click += (_, _) =>
            {
                using var preview = new Form
                {
                    Text = "OPSIS · العربية · RTL", StartPosition = FormStartPosition.CenterParent,
                    Width = 510, Height = 250, MinimumSize = new Size(430, 220),
                    RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
                    BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary
                };
                var sample = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                    WrapContents = false, Padding = new Padding(20), RightToLeft = RightToLeft.Yes
                };
                sample.Controls.Add(new Label { Text = "الإعدادات · تخصيص سطح المكتب", AutoSize = true, Font = UiTheme.FontBody });
                sample.Controls.Add(new CheckBox { Text = "تفعيل الخلفية المتحركة", AutoSize = true, Checked = true });
                sample.Controls.Add(new TextBox { Width = 310, Text = "البحث في الإعدادات", RightToLeft = RightToLeft.Yes });
                sample.Controls.Add(new Button { Text = "حفظ التغييرات", AutoSize = true });
                preview.Controls.Add(sample);
                preview.ShowDialog(FindForm());
            };
            card.Controls.Add(rtlPreview);
            var statesPreview = new Button
            {
                Text = Lx("Προεπισκόπηση καταστάσεων και μεγάλων μεταφράσεων…",
                    "Preview button states and longest translations…"),
                AutoSize = true, Height = 34, Margin = new Padding(0, 0, 0, 12)
            };
            statesPreview.Click += (_, _) => ShowThemeReadabilityPreview();
            card.Controls.Add(statesPreview);
            foreach (var t in GearWinThemes.All)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
                row.Controls.Add(new Label { Text = t.Name, AutoSize = false, Width = 230, Height = 34, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody });
                foreach (var dark in new[] { true, false })
                {
                    var mode = GearWinThemes.Mode(t.Name, dark); var name = t.Name + " - " + (dark ? Lx("σκούρο", "dark") : Lx("φωτεινό", "light"));
                    row.Controls.Add(new PaletteStrip(dark ? t.Dark() : t.Light(), current == mode, name, () => ThemeManager.SetMode(mode)));
                }
                card.Controls.Add(row);
            }
        }
    }
}

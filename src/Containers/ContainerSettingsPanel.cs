using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>A palette of colour swatches: "Auto", ready-made shades and "Custom...". Replaces typing a hex code. <see cref="Chosen"/> gets "Auto" or "#RRGGBB".</summary>
    internal sealed class ColorPalette : FlowLayoutPanel
    {
        public static readonly string[] Shades = { "#1E1E1E", "#3B3B3B", "#6B7280", "#2563EB", "#0EA5E9", "#14B8A6", "#22C55E", "#84CC16", "#EAB308", "#F97316", "#EF4444", "#EC4899", "#A855F7", "#6366F1", "#FFFFFF" };
        public event Action<string>? Chosen;
        private readonly List<Panel> _swatches = new();
        private string _value;

        public ColorPalette(string current, bool dark)
        {
            var tips = new ToolTip();
            Disposed += (_, _) => tips.Dispose();
            AutoSize = true; WrapContents = true; MaximumSize = new Size(340, 0); Margin = new Padding(0, 2, 0, 6); _value = current;
            Panel Swatch(string value, Color c, string tip)
            {
                var p = new Panel { Width = 22, Height = 22, Margin = new Padding(2), BackColor = c, Cursor = Cursors.Hand, Tag = value };
                tips.SetToolTip(p, tip);
                p.Paint += (_, e) => { if (string.Equals(_value, value, StringComparison.OrdinalIgnoreCase)) { using var pen = new Pen(dark ? Color.White : Color.Black, 2); e.Graphics.DrawRectangle(pen, 1, 1, p.Width - 3, p.Height - 3); } };
                p.Click += (_, _) => Pick(value);
                _swatches.Add(p); Controls.Add(p); return p;
            }
            var auto = Swatch("Auto", Color.FromArgb(70, 80, 110), LocalizationManager.T("DCM.Auto"));
            auto.Paint += (_, e) => TextRenderer.DrawText(e.Graphics, "A", auto.Font, auto.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            foreach (var h in Shades) Swatch(h, ColorTranslator.FromHtml(h), h);
            var custom = new Panel { Width = 22, Height = 22, Margin = new Padding(2), BackColor = Color.FromArgb(120, 120, 120), Cursor = Cursors.Hand };
            tips.SetToolTip(custom, LocalizationManager.T("DCM.Custom"));
            custom.Paint += (_, e) => TextRenderer.DrawText(e.Graphics, "…", custom.Font, custom.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            custom.Click += (_, _) =>
            {
                using var d = new ColorDialog { FullOpen = true, AnyColor = true }; if (!_value.Equals("Auto", StringComparison.OrdinalIgnoreCase)) try { d.Color = ColorTranslator.FromHtml(_value); } catch (Exception) { }
                if (d.ShowDialog() == DialogResult.OK) Pick("#" + d.Color.R.ToString("X2") + d.Color.G.ToString("X2") + d.Color.B.ToString("X2"));
            };
            Controls.Add(custom);
        }

        public void Pick(string value) { _value = value; foreach (var s in _swatches) s.Invalidate(); Chosen?.Invoke(value); }
    }

    /// <summary>Every setting of ONE container; used in the container's own settings window and on the DeskContainers page. Changes apply live through <see cref="ContainerHost.Apply"/>.</summary>
    internal sealed class ContainerSettingsPanel : FlowLayoutPanel
    {
        internal static readonly object SectionMarker = new();

        public ContainerSettingsPanel(ContainerHost host, Container c, bool dark, bool compact = false)
        {
            AutoSize = true; FlowDirection = FlowDirection.TopDown; WrapContents = false; Margin = new Padding(0);
            string T(string k) => LocalizationManager.T(k); var id = c.Id;
            var fore = UiTheme.TextPrimary;
            Label Head(string t) => new Label { Tag = SectionMarker, Text = t, AutoSize = true, ForeColor = UiTheme.AccentCyan, Margin = new Padding(0, 6, 0, 2), Font = UiTheme.FontSubheading };
            void Row(string label, Control input) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) }; r.Controls.Add(new Label { Text = label, AutoSize = false, Width = compact ? 148 : 210, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = fore }); r.Controls.Add(input); Controls.Add(r); }
            NumericUpDown Num(decimal v, int min, int max, Action<Container, decimal> set, int decimals = 0)
            {
                var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals > 0 ? 0.5m : 1, Value = Math.Clamp(v, min, max), Width = 80 };
                n.ValueChanged += (_, _) => host.Apply(id, k => { set(k, n.Value); return true; });
                return n;
            }
            void Chk(string text, bool v, Action<Container, bool> set)
            {
                var toggle = new FlatToggle
                {
                    Text = text, Checked = v, RowWidth = compact ? 302 : 382,
                    BackColor = UiTheme.PageBackground, Margin = new Padding(0, 2, 0, 2)
                };
                toggle.CheckedChanged += (_, _) => host.Apply(id, k => { set(k, toggle.Checked); return true; });
                Controls.Add(toggle);
            }
            FlatComboBox Fonts(string? current, Action<Container, string?> set)
            {
                var cb = new FlatComboBox { Width = compact ? 136 : 190 };
                string auto = "(" + T("DCM.Auto") + ")";
                var names = new[] { auto }.Concat(FontFamily.Families.Select(font => font.Name)).ToArray();
                cb.SetItems(names, current ?? auto);
                cb.SelectedIndexChanged += (_, _) => host.Apply(id, k => { set(k, cb.SelectedIndex <= 0 ? null : cb.SelectedItem as string); return true; });
                return cb;
            }

            Controls.Add(Head(T("DCM.TitleColourP")));
            Controls.Add(new Label { Text = T("DCM.ContainerTitle"), AutoSize = true, ForeColor = fore, Margin = new Padding(0, 4, 0, 2) });
            var titleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(compact ? 304 : 420, 0), Margin = Padding.Empty };
            string savedTitle = c.Title;
            var title = new TextBox { Text = savedTitle, Width = compact ? 145 : 190, AccessibleName = T("DCM.ContainerTitle") };
            void SaveTitle()
            {
                string value = title.Text.Trim();
                if (value.Length > 0 && value != savedTitle) host.Rename(id, value);
                title.Text = savedTitle;
            }
            void OnContainerRenamed(string containerId, string value)
            {
                if (containerId != id) return;
                savedTitle = value;
                title.Text = value;
            }
            host.ContainerRenamed += OnContainerRenamed;
            // The dialog moves these controls into tabs and disposes the original panel.
            title.Disposed += (_, _) => host.ContainerRenamed -= OnContainerRenamed;
            var rename = MainWindow.StudioButton(T("DCM.ApplyContainerTitle"), SaveTitle, 140);
            title.Validated += (_, _) => SaveTitle();
            title.KeyDown += (_, e) =>
            {
                if (e.KeyCode is not (Keys.Enter or Keys.Escape)) return;
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Enter) SaveTitle();
                else title.Text = savedTitle;
            };
            titleRow.Controls.Add(title);
            titleRow.Controls.Add(rename);
            Controls.Add(titleRow);
            var pal = new ColorPalette(c.TitleBar.Color, dark); pal.Chosen += v => host.Apply(id, k => { k.TitleBar.Color = v; return true; }); Controls.Add(pal);
            Row(T("DCM.TitleOpacity"), Num(c.TitleBar.Opacity, 0, 100, (k, v) => k.TitleBar.Opacity = (int)v));
            Row(T("DCM.TitleHeight"), Num(c.TitleBar.Height, 16, 96, (k, v) => k.TitleBar.Height = (int)v));
            Row(T("DCM.TitleFontFamily"), Fonts(c.TitleBar.FontFamily, (k, f) => k.TitleBar.FontFamily = f));
            Row(T("DCM.TitleFont"), Num((decimal)c.TitleBar.FontSize, 6, 36, (k, v) => k.TitleBar.FontSize = (float)v, 1));

            Controls.Add(Head(T("DCM.ItemsHead")));
            if (c.Kind != ContainerKind.Note)
            {
                string L(string english, string greek) => MotionDesk.UI.MainWindow.StudioText(english, greek);
                string[] modes = ["grid", "list", "details"];
                var view = new FlatComboBox { Width = compact ? 136 : 190 };
                string[] labels = [L("Icons", "Εικονίδια"), L("List", "Λίστα"), L("Details", "Λεπτομέρειες")];
                view.SetItems(labels, labels[Math.Max(0, Array.IndexOf(modes, ContainerItemLayout.ViewOf(c)))]);
                view.SelectedIndexChanged += (_, _) => host.Apply(id, k =>
                {
                    (k.Extra ??= new())[ContainerItemLayout.ViewKey] = System.Text.Json.JsonSerializer.SerializeToElement(modes[view.SelectedIndex]);
                    return true;
                });
                Row(L("File view", "Προβολή αρχείων"), view);
            }
            Row(T("DCM.IconSize"), Num(c.Appearance.IconSize > 0 ? c.Appearance.IconSize : 48, 16, 128, (k, v) => k.Appearance.IconSize = (int)v));
            Row(T("DCM.ItemFont"), Fonts(c.Appearance.ItemFontFamily, (k, f) => k.Appearance.ItemFontFamily = f));
            Row(T("DCM.ItemFontSize"), Num((decimal)(c.Appearance.ItemFontSize > 0 ? c.Appearance.ItemFontSize : 9f), 6, 24, (k, v) => k.Appearance.ItemFontSize = (float)v, 1));
            Chk(T("DCM.SingleClick"), c.Appearance.OpenOnSingleClick, (k, v) => k.Appearance.OpenOnSingleClick = v);
            Chk(T("DCM.LaunchPulse"), string.Equals(c.Appearance.LaunchEffect, "Pulse", StringComparison.OrdinalIgnoreCase), (k, v) => k.Appearance.LaunchEffect = v ? "Pulse" : null);

            Controls.Add(Head(T("DCM.Tint")));
            var tint = new ColorPalette(string.IsNullOrWhiteSpace(c.Appearance.FrameColor) ? "Auto" : c.Appearance.FrameColor!, dark); tint.Chosen += v => host.Apply(id, k => { k.Appearance.FrameColor = v.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : v; return true; }); Controls.Add(tint);
            Row(T("DCM.TintStrength"), Num(c.Appearance.TintOpacity, 0, 100, (k, v) => k.Appearance.TintOpacity = (int)v));
            Chk(T("DCM.Glossy"), c.Appearance.Glossy, (k, v) => k.Appearance.Glossy = v);

            Controls.Add(Head(T("DCM.BehaviourHead")));
            Chk(T("DCM.Locked"), c.Locked, (k, v) => k.Locked = v);
            Chk(T("DCM.ExcludeQuick"), c.ExcludeFromQuickHide, (k, v) => k.ExcludeFromQuickHide = v);
            if (c.Kind != ContainerKind.Note)
                Chk(MotionDesk.UI.MainWindow.StudioText("Automatic height", "Αυτόματο ύψος"), ContainerWindow.AutoHeightOf(c), (model, enabled) =>
                    (model.Extra ??= new())[ContainerWindow.AutoHeightKey] = System.Text.Json.JsonSerializer.SerializeToElement(enabled));
            Chk(T("DCM.HoverRollUp"), c.Appearance.HoverRollUp, (k, v) => { k.Appearance.HoverRollUp = v; k.RolledUp = false; });
            Row(T("DCM.HoverDelay"), Num(c.Appearance.HoverDelayMs, 50, 3000, (k, v) => k.Appearance.HoverDelayMs = (int)v));
            MainWindow.StyleStudioInputs(this);
        }
    }
}

using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
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
            AutoSize = true; WrapContents = true; MaximumSize = new Size(340, 0); Margin = new Padding(0, 2, 0, 6); _value = current;
            Panel Swatch(string value, Color c, string tip)
            {
                var p = new Panel { Width = 22, Height = 22, Margin = new Padding(2), BackColor = c, Cursor = Cursors.Hand, Tag = value };
                new Opsis.UI.FlatToolTip().SetToolTip(p, tip);
                p.Paint += (_, e) => { if (string.Equals(_value, value, StringComparison.OrdinalIgnoreCase)) { using var pen = new Pen(dark ? Color.White : Color.Black, 2); e.Graphics.DrawRectangle(pen, 1, 1, p.Width - 3, p.Height - 3); } };
                p.Click += (_, _) => Pick(value);
                _swatches.Add(p); Controls.Add(p); return p;
            }
            var auto = Swatch("Auto", Color.FromArgb(70, 80, 110), LocalizationManager.T("DCM.Auto"));
            auto.Paint += (_, e) => TextRenderer.DrawText(e.Graphics, "A", auto.Font, auto.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            foreach (var h in Shades) Swatch(h, ColorTranslator.FromHtml(h), h);
            var custom = new Panel { Width = 22, Height = 22, Margin = new Padding(2), BackColor = Color.FromArgb(120, 120, 120), Cursor = Cursors.Hand };
            new Opsis.UI.FlatToolTip().SetToolTip(custom, LocalizationManager.T("DCM.Custom"));
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
        public ContainerSettingsPanel(ContainerHost host, Container c, bool dark)
        {
            AutoSize = true; FlowDirection = FlowDirection.TopDown; WrapContents = false; Margin = new Padding(0);
            string T(string k) => LocalizationManager.T(k); var id = c.Id;
            var fore = dark ? Color.FromArgb(225, 228, 238) : SystemColors.ControlText;
            Label Head(string t) => new Label { Text = t, AutoSize = true, ForeColor = fore, Margin = new Padding(0, 6, 0, 2), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            void Row(string label, Control input) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) }; r.Controls.Add(new Label { Text = label, AutoSize = false, Width = 210, Height = 24, TextAlign = ContentAlignment.MiddleLeft, ForeColor = fore }); r.Controls.Add(input); Controls.Add(r); }
            NumericUpDown Num(decimal v, int min, int max, Action<Container, decimal> set, int decimals = 0)
            {
                var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals > 0 ? 0.5m : 1, Value = Math.Clamp(v, min, max), Width = 80 };
                n.ValueChanged += (_, _) => host.Apply(id, k => { set(k, n.Value); return true; });
                return n;
            }
            CheckBox Chk(string text, bool v, Action<Container, bool> set) { var b = new CheckBox { Text = text, Checked = v, AutoSize = true, ForeColor = fore, Margin = new Padding(0, 2, 0, 2) }; b.CheckedChanged += (_, _) => host.Apply(id, k => { set(k, b.Checked); return true; }); Controls.Add(b); return b; }
            ComboBox Fonts(string? current, Action<Container, string?> set)
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
                cb.Items.Add("(" + T("DCM.Auto") + ")"); foreach (var f in FontFamily.Families) cb.Items.Add(f.Name);
                var at = string.IsNullOrWhiteSpace(current) ? 0 : cb.Items.IndexOf(current); cb.SelectedIndex = at < 0 ? 0 : at;
                cb.SelectedIndexChanged += (_, _) => host.Apply(id, k => { set(k, cb.SelectedIndex <= 0 ? null : (string)cb.SelectedItem!); return true; });
                return cb;
            }

            Controls.Add(Head(T("DCM.TitleColourP")));
            var pal = new ColorPalette(c.TitleBar.Color, dark); pal.Chosen += v => host.Apply(id, k => { k.TitleBar.Color = v; return true; }); Controls.Add(pal);
            Row(T("DCM.TitleOpacity"), Num(c.TitleBar.Opacity, 0, 100, (k, v) => k.TitleBar.Opacity = (int)v));
            Row(T("DCM.TitleHeight"), Num(c.TitleBar.Height, 16, 96, (k, v) => k.TitleBar.Height = (int)v));
            Row(T("DCM.TitleFontFamily"), Fonts(c.TitleBar.FontFamily, (k, f) => k.TitleBar.FontFamily = f));
            Row(T("DCM.TitleFont"), Num((decimal)c.TitleBar.FontSize, 6, 36, (k, v) => k.TitleBar.FontSize = (float)v, 1));

            Controls.Add(Head(T("DCM.ItemsHead")));
            Row(T("DCM.IconSize"), Num(c.Appearance.IconSize > 0 ? c.Appearance.IconSize : 48, 16, 128, (k, v) => k.Appearance.IconSize = (int)v));
            Row(T("DCM.ItemFont"), Fonts(c.Appearance.ItemFontFamily, (k, f) => k.Appearance.ItemFontFamily = f));
            Row(T("DCM.ItemFontSize"), Num((decimal)(c.Appearance.ItemFontSize > 0 ? c.Appearance.ItemFontSize : 9f), 6, 24, (k, v) => k.Appearance.ItemFontSize = (float)v, 1));
            Chk(T("DCM.SingleClick"), c.Appearance.OpenOnSingleClick, (k, v) => k.Appearance.OpenOnSingleClick = v);

            Controls.Add(Head(T("DCM.Tint")));
            var tint = new ColorPalette(string.IsNullOrWhiteSpace(c.Appearance.FrameColor) ? "Auto" : c.Appearance.FrameColor!, dark); tint.Chosen += v => host.Apply(id, k => { k.Appearance.FrameColor = v.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : v; return true; }); Controls.Add(tint);
            Row(T("DCM.TintStrength"), Num(c.Appearance.TintOpacity, 0, 100, (k, v) => k.Appearance.TintOpacity = (int)v));
            Chk(T("DCM.Glossy"), c.Appearance.Glossy, (k, v) => k.Appearance.Glossy = v);

            Controls.Add(Head(T("DCM.BehaviourHead")));
            Chk(T("DCM.Locked"), c.Locked, (k, v) => k.Locked = v);
            Chk(T("DCM.ExcludeQuick"), c.ExcludeFromQuickHide, (k, v) => k.ExcludeFromQuickHide = v);
            Chk(T("DCM.HoverRollUp"), c.Appearance.HoverRollUp, (k, v) => k.Appearance.HoverRollUp = v);
            Row(T("DCM.HoverDelay"), Num(c.Appearance.HoverDelayMs, 50, 3000, (k, v) => k.Appearance.HoverDelayMs = (int)v));
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

/// <summary>Visual preview and editor of Opsis' OWN replacement taskbar, not Explorer injection.</summary>
public sealed class OpsisTaskbarDesignerForm : Form
{
    private OpsisTaskbarDesigner.Profile _profile = OpsisTaskbarDesigner.Read();
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox _middle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox _double = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox _volume = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly System.Collections.Generic.List<string?> _monitorKeys = new() { null };
    private bool _loading;
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox _thumbnail = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _edge = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly CheckBox _floating = new() { Text = "Floating", AutoSize = true };
    private readonly CheckBox _segmented = new() { Text = "Segmented", AutoSize = true };
    private readonly CheckBox _dynamic = new() { Text = "Dynamic length", AutoSize = true };
    private readonly NumericUpDown _margin = new() { Minimum = 0, Maximum = 32, Width = 74 };
    private readonly NumericUpDown _length = new() { Minimum = 40, Maximum = 100, Width = 74 };
    private readonly NumericUpDown _thickness = new() { Minimum = 36, Maximum = 112, Width = 74 };
    private readonly ComboBox _alignment = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _width = new() { Minimum = 72, Maximum = 240, Width = 74 };
    private readonly NumericUpDown _gap = new() { Minimum = 0, Maximum = 20, Width = 74 };
    private readonly NumericUpDown _opacity = new() { Minimum = 60, Maximum = 100, Width = 74 };
    private readonly CheckBox _taskView = new() { Text = "Task View", AutoSize = true };
    private readonly CheckBox _clock = new() { Text = "Clock", AutoSize = true };
    private readonly CheckBox _animate = new() { Text = "Pulse active app / Εφέ ενεργής εφαρμογής", AutoSize = true };
    private readonly NumericUpDown _speed = new() { Minimum = 180, Maximum = 800, Increment = 60, Width = 78 };
    private readonly Panel _preview = new() { Height = 72, Dock = DockStyle.Bottom };
    private readonly Label _heading = new() { Dock = DockStyle.Top, AutoSize = false, Height = 62 };

    public OpsisTaskbarDesignerForm()
    {
        Text = "Opsis Taskbar Designer";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(710, 340);
        ClientSize = new Size(820, 500);
        Font = UiTheme.FontBody;
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;
        _heading.Text = "Opsis Taskbar Designer\nNative position is unchanged. Detached Opsis bars use the monitor work area; Windows taskbar remains available.";
        _heading.Padding = new Padding(12, 10, 10, 5);
        Controls.Add(_heading);
        var grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            Padding = new Padding(12, 10, 8, 8)
        };
        Controls.Add(grid);
        _style.Items.AddRange(new object[] { "Classic", "Compact", "Glass" });
        _alignment.Items.AddRange(new object[] { "Left", "Center" });
        _edge.Items.AddRange(new object[] { "Native", "Top", "Bottom", "Left", "Right" });
        _monitor.Items.Add("All monitors / Default");
        var displays = MonitorTopologyService.Current();
        foreach (var display in displays)
        {
            _monitorKeys.Add(OpsisTaskbarDesigner.MonitorKey(display, displays));
            _monitor.Items.Add(display.DeviceName + (display.Primary ? " (primary)" : ""));
        }
        _monitor.SelectedIndex = 0;
        _middle.Items.AddRange(OpsisTaskbarDesigner.Gestures.Cast<object>().ToArray());
        _double.Items.AddRange(OpsisTaskbarDesigner.Gestures.Cast<object>().ToArray());
        _volume.Items.AddRange(new object[] { "Windows", "Compact", "Large" });
        _middle.SelectedItem = _profile.EmptyMiddleClick; _double.SelectedItem = _profile.EmptyDoubleClick;
        _volume.SelectedItem = _profile.VolumeStyle;
        _thumbnail.Items.AddRange(new object[] { "Off", "Compact", "Large" });
        _thumbnail.SelectedItem = _profile.ThumbnailStyle;
        LoadGeometry(_profile);
        _style.SelectedItem = _profile.Style;
        _alignment.SelectedItem = _profile.Alignment;
        _width.Value = _profile.ButtonWidth;
        _gap.Value = _profile.ButtonGap;
        _opacity.Value = _profile.OpacityPercent;
        _taskView.Checked = _profile.ShowTaskView;
        _clock.Checked = _profile.ShowClock;
        _animate.Checked = _profile.ActiveButtonAnimation;
        _speed.Value = _profile.AnimationIntervalMs;

        grid.Controls.Add(Labeled("Monitor / Οθόνη", _monitor));
        grid.Controls.Add(Labeled("Empty area middle click", _middle));
        grid.Controls.Add(Labeled("Empty area double click", _double));
        grid.Controls.Add(Labeled("Volume flyout (Opsis)", _volume));
        var inherit = new Button { Text = "Inherit global / Κοινό προφίλ", AutoSize = true, Height = 34 };
        grid.Controls.Add(inherit);
        grid.Controls.Add(Labeled("Style", _style));
        grid.Controls.Add(Labeled("Alignment", _alignment));
        grid.Controls.Add(Labeled("Button width", _width));
        grid.Controls.Add(Labeled("Button gap", _gap));
        grid.Controls.Add(Labeled("Glass opacity %", _opacity));
        grid.Controls.Add(_taskView);
        grid.Controls.Add(_clock);
        grid.Controls.Add(_animate);
        grid.Controls.Add(Labeled("Pulse ms", _speed));
        grid.Controls.Add(Labeled("Window preview", _thumbnail));
        grid.Controls.Add(Labeled("Edge / Πλευρά", _edge));
        grid.Controls.Add(Labeled("Margin", _margin));
        grid.Controls.Add(Labeled("Length %", _length));
        grid.Controls.Add(Labeled("Thickness", _thickness));
        grid.Controls.Add(_floating); grid.Controls.Add(_segmented); grid.Controls.Add(_dynamic);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 7, 5, 5) };
        var apply = new Button { Text = "Apply / Εφαρμογή", Width = 145, Height = 34 };
        var restore = new Button { Text = "Default / Προεπιλογή", Width = 155, Height = 34 };
        var system = new Button { Text = "Windows taskbar settings", Width = 195, Height = 34 };
        actions.Controls.Add(apply); actions.Controls.Add(restore); actions.Controls.Add(system);
        Controls.Add(actions);
        _preview.BackColor = UiTheme.NavBackground;
        Controls.Add(_preview);
        _preview.BringToFront();
        actions.BringToFront();
        _heading.BringToFront();

        void Redraw() { if (_loading) return; FillProfile(); DrawPreview(); }
        _middle.SelectedIndexChanged += (_, _) => Redraw();
        _double.SelectedIndexChanged += (_, _) => Redraw();
        _volume.SelectedIndexChanged += (_, _) => Redraw();
        _monitor.SelectedIndexChanged += (_, _) => LoadMonitor();
        inherit.Click += (_, _) =>
        {
            if (_monitor.SelectedIndex <= 0) return;
            if (MessageBox.Show(this, "Remove this monitor override and inherit the global profile?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                if (WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode) throw new InvalidOperationException("Compatibility review required.");
                OpsisTaskbarDesigner.SaveMonitorProfile(_monitorKeys[_monitor.SelectedIndex]!, null);
                LoadMonitor(); ReplacementTaskbar.RefreshSettings();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };
        _style.SelectedIndexChanged += (_, _) => Redraw();
        _alignment.SelectedIndexChanged += (_, _) => Redraw();
        _width.ValueChanged += (_, _) => Redraw();
        _gap.ValueChanged += (_, _) => Redraw();
        _opacity.ValueChanged += (_, _) => Redraw();
        _taskView.CheckedChanged += (_, _) => Redraw();
        _clock.CheckedChanged += (_, _) => Redraw();
        _animate.CheckedChanged += (_, _) => Redraw();
        _speed.ValueChanged += (_, _) => Redraw();
        _thumbnail.SelectedIndexChanged += (_, _) => Redraw();
        _edge.SelectedIndexChanged += (_, _) => Redraw();
        _margin.ValueChanged += (_, _) => Redraw();
        _length.ValueChanged += (_, _) => Redraw();
        _thickness.ValueChanged += (_, _) => Redraw();
        _floating.CheckedChanged += (_, _) => Redraw();
        _segmented.CheckedChanged += (_, _) => Redraw();
        _dynamic.CheckedChanged += (_, _) => Redraw();
        _preview.Resize += (_, _) => DrawPreview();
        apply.Click += (_, _) =>
        {
            try
            {
                if (WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode)
                    throw new InvalidOperationException("Windows compatibility review is required.");
                FillProfile();
                if (_monitor.SelectedIndex > 0) OpsisTaskbarDesigner.SaveMonitorProfile(_monitorKeys[_monitor.SelectedIndex]!, _profile);
                else OpsisTaskbarDesigner.Save(_profile);
                ReplacementTaskbar.RefreshSettings();
                MessageBox.Show(this, "Opsis Taskbar profile saved.", "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        restore.Click += (_, _) =>
        {
            var defaults = OpsisTaskbarDesigner.Default;
            _style.SelectedItem = defaults.Style;
            _alignment.SelectedItem = defaults.Alignment;
            _width.Value = defaults.ButtonWidth; _gap.Value = defaults.ButtonGap;
            _opacity.Value = defaults.OpacityPercent;
            _taskView.Checked = defaults.ShowTaskView; _clock.Checked = defaults.ShowClock;
            _animate.Checked = defaults.ActiveButtonAnimation; _speed.Value = defaults.AnimationIntervalMs;
            _thumbnail.SelectedItem = defaults.ThumbnailStyle;
            _middle.SelectedItem = defaults.EmptyMiddleClick; _double.SelectedItem = defaults.EmptyDoubleClick; _volume.SelectedItem = defaults.VolumeStyle;
            LoadGeometry(defaults);
        };
        system.Click += (_, _) => TaskbarViewProfiles.OpenWindowsTaskbarSettings();
        DrawPreview();
    }

    private static Control Labeled(string caption, Control input)
    {
        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Width = Math.Max(125, input.Width + 12), Height = 65
        };
        column.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = UiTheme.TextSecondary });
        column.Controls.Add(input);
        return column;
    }

    private void LoadMonitor()
    {
        _loading = true;
        try
        {
            var key = _monitor.SelectedIndex > 0 ? _monitorKeys[_monitor.SelectedIndex] : null;
            var overrides = OpsisTaskbarDesigner.ReadMonitorProfiles();
            _profile = key != null && overrides.TryGetValue(key, out var saved) ? saved : OpsisTaskbarDesigner.Read();
            _style.SelectedItem = _profile.Style; _alignment.SelectedItem = _profile.Alignment;
            _width.Value = _profile.ButtonWidth; _gap.Value = _profile.ButtonGap;
            _opacity.Value = _profile.OpacityPercent; _taskView.Checked = _profile.ShowTaskView;
            _clock.Checked = _profile.ShowClock; _animate.Checked = _profile.ActiveButtonAnimation;
            _speed.Value = _profile.AnimationIntervalMs; _thumbnail.SelectedItem = _profile.ThumbnailStyle;
            _middle.SelectedItem = _profile.EmptyMiddleClick; _double.SelectedItem = _profile.EmptyDoubleClick;
            _volume.SelectedItem = _profile.VolumeStyle;
            LoadGeometry(_profile);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        finally { _loading = false; }
        DrawPreview();
    }

    private void LoadGeometry(OpsisTaskbarDesigner.Profile p)
    {
        _edge.SelectedItem = p.Edge; _floating.Checked = p.Floating; _segmented.Checked = p.Segmented;
        _dynamic.Checked = p.DynamicSize; _margin.Value = p.EdgeMargin; _length.Value = p.LengthPercent;
        _thickness.Value = p.Thickness;
    }

    private void FillProfile()
    {
        _profile.Style = _style.SelectedItem?.ToString() ?? "Classic";
        _profile.Alignment = _alignment.SelectedItem?.ToString() ?? "Left";
        _profile.ButtonWidth = (int)_width.Value;
        _profile.ButtonGap = (int)_gap.Value;
        _profile.OpacityPercent = (int)_opacity.Value;
        _profile.ShowTaskView = _taskView.Checked;
        _profile.ShowClock = _clock.Checked;
        _profile.ActiveButtonAnimation = _animate.Checked;
        _profile.AnimationIntervalMs = (int)_speed.Value;
        _profile.EmptyMiddleClick = _middle.SelectedItem?.ToString() ?? "None";
        _profile.EmptyDoubleClick = _double.SelectedItem?.ToString() ?? "None";
        _profile.VolumeStyle = _volume.SelectedItem?.ToString() ?? "Windows";
        _profile.ThumbnailStyle = _thumbnail.SelectedItem?.ToString() ?? "Off";
        _profile.Edge = _edge.SelectedItem?.ToString() ?? "Native";
        _profile.Floating = _floating.Checked; _profile.Segmented = _segmented.Checked; _profile.DynamicSize = _dynamic.Checked;
        _profile.EdgeMargin = (int)_margin.Value; _profile.LengthPercent = (int)_length.Value;
        _profile.Thickness = (int)_thickness.Value;
        OpsisTaskbarDesigner.Validate(_profile);
    }

    private void DrawPreview()
    {
        if (_preview.IsDisposed || _preview.Width < 120) return;
        // Disposing a child removes it from ControlCollection; do not mutate
        // a collection while iterating its enumerator.
        var oldControls = _preview.Controls.Cast<Control>().ToArray();
        _preview.Controls.Clear();
        foreach (var control in oldControls) control.Dispose();
        _preview.BackColor = UiTheme.NavBackground;
        var description = new Label { Text = _profile.Edge + " • " + (_profile.Floating ? "Floating " : "") +
            (_profile.Segmented ? "Segmented " : "") + (_profile.DynamicSize ? "Adaptive" : "Fixed"),
            AutoSize = true, Location = new Point(8, 1), ForeColor = UiTheme.TextSecondary };
        _preview.Controls.Add(description);
        var items = new[] { "Opsis", "Explorer", "Browser" };
        var total = items.Length * ((int)_width.Value + (int)_gap.Value * 2);
        var left = _alignment.SelectedItem?.ToString() == "Center"
            ? Math.Max(4, (_preview.ClientSize.Width - total) / 2) : 8;
        foreach (var name in items)
        {
            var btn = new Label
            {
                Text = name, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
                ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface,
                Size = new Size((int)_width.Value, _style.SelectedItem?.ToString() == "Compact" ? 32 : 42),
                Location = new Point(left, 22)
            };
            _preview.Controls.Add(btn);
            left += btn.Width + (int)_gap.Value * 2;
        }
    }
}

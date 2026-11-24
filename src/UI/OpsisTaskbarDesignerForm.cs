using System;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

/// <summary>Visual preview and editor of Opsis' OWN replacement taskbar, not Explorer injection.</summary>
public sealed class OpsisTaskbarDesignerForm : Form
{
    private readonly OpsisTaskbarDesigner.Profile _profile = OpsisTaskbarDesigner.Read();
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
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
        ClientSize = new Size(750, 355);
        Font = UiTheme.FontBody;
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;
        _heading.Text = "Opsis Taskbar Designer\nPreview changes only affect the Opsis replacement bar, never Windows system files.";
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
        _style.SelectedItem = _profile.Style;
        _alignment.SelectedItem = _profile.Alignment;
        _width.Value = _profile.ButtonWidth;
        _gap.Value = _profile.ButtonGap;
        _opacity.Value = _profile.OpacityPercent;
        _taskView.Checked = _profile.ShowTaskView;
        _clock.Checked = _profile.ShowClock;
        _animate.Checked = _profile.ActiveButtonAnimation;
        _speed.Value = _profile.AnimationIntervalMs;

        grid.Controls.Add(Labeled("Style", _style));
        grid.Controls.Add(Labeled("Alignment", _alignment));
        grid.Controls.Add(Labeled("Button width", _width));
        grid.Controls.Add(Labeled("Button gap", _gap));
        grid.Controls.Add(Labeled("Glass opacity %", _opacity));
        grid.Controls.Add(_taskView);
        grid.Controls.Add(_clock);
        grid.Controls.Add(_animate);
        grid.Controls.Add(Labeled("Pulse ms", _speed));

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

        void Redraw() { FillProfile(); DrawPreview(); }
        _style.SelectedIndexChanged += (_, _) => Redraw();
        _alignment.SelectedIndexChanged += (_, _) => Redraw();
        _width.ValueChanged += (_, _) => Redraw();
        _gap.ValueChanged += (_, _) => Redraw();
        _opacity.ValueChanged += (_, _) => Redraw();
        _taskView.CheckedChanged += (_, _) => Redraw();
        _clock.CheckedChanged += (_, _) => Redraw();
        _animate.CheckedChanged += (_, _) => Redraw();
        _speed.ValueChanged += (_, _) => Redraw();
        _preview.Resize += (_, _) => DrawPreview();
        apply.Click += (_, _) =>
        {
            try
            {
                if (WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode)
                    throw new InvalidOperationException("Windows compatibility review is required.");
                FillProfile();
                OpsisTaskbarDesigner.Save(_profile);
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
        OpsisTaskbarDesigner.Validate(_profile);
    }

    private void DrawPreview()
    {
        if (_preview.IsDisposed || _preview.Width < 120) return;
        foreach (Control child in _preview.Controls) child.Dispose();
        _preview.Controls.Clear();
        _preview.BackColor = UiTheme.NavBackground;
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
                Location = new Point(left, 14)
            };
            _preview.Controls.Add(btn);
            left += btn.Width + (int)_gap.Value * 2;
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// Native drag-and-drop editor for a declarative Opsis widget. No embedded
/// scripts, injectors, third-party windows or separate background services.
/// Preview elements are inert controls and never execute the widget data source.
/// </summary>
public sealed class OpsisWidgetVisualDesignerForm : Form
{
    private readonly OpsisWidgetDesigner.Layout _layout;
    private readonly string _originalName;
    private readonly Panel _canvas = new();
    private readonly Label _element = new();
    private readonly NumericUpDown _x = new() { Minimum = 0, Maximum = 100, Width = 65 };
    private readonly NumericUpDown _y = new() { Minimum = 0, Maximum = 100, Width = 65 };
    private readonly NumericUpDown _fontSize = new() { Minimum = 12, Maximum = 80, Width = 65 };
    private readonly NumericUpDown _opacity = new() { Minimum = 60, Maximum = 100, Width = 65 };
    private readonly CheckBox _border = new() { Text = "Widget border / Περίγραμμα", AutoSize = true };
    private readonly TextBox _text = new() { Width = 175 };
    private readonly ComboBox _binding = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _color = new() { Width = 85 };
    private bool _dragging, _updating;
    private Point _startMouse;
    private Point _startElement;
    private Font? _previewFont;

    public OpsisWidgetVisualDesignerForm(OpsisWidgetDesigner.Layout layout)
    {
        OpsisWidgetDesigner.Validate(layout);
        if (layout.Rows is { Length: > 0 }) throw new InvalidOperationException("Composite Rainmeter widgets retain their imported rows; the single-element editor cannot modify them yet.");
        _layout = layout;
        _originalName = layout.Name;
        Text = "Opsis Visual Widget Designer — " + layout.Name;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.FontBody;
        MinimumSize = new Size(620, 510);
        ClientSize = new Size(780, 575);
        StartPosition = FormStartPosition.CenterParent;

        _canvas.Location = new Point(18, 16);
        _canvas.Size = new Size(740, 340);
        _canvas.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _canvas.BackColor = UiTheme.Surface;
        _canvas.BorderStyle = BorderStyle.FixedSingle;

        _element.TextAlign = ContentAlignment.MiddleCenter;
        _element.BackColor = UiTheme.SurfaceHover;
        _element.ForeColor = ColorTranslator.FromHtml(layout.AccentHex);
        _element.Cursor = Cursors.SizeAll;
        _element.AutoSize = false;
        _element.Size = new Size(205, 75);
        _element.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _startMouse = _element.PointToScreen(e.Location);
            _startElement = _element.Location;
            _element.Capture = true;
        };
        _element.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var pointer = _element.PointToScreen(e.Location);
            var maxX = Math.Max(0, _canvas.ClientSize.Width - _element.Width);
            var maxY = Math.Max(0, _canvas.ClientSize.Height - _element.Height);
            var px = Math.Clamp(_startElement.X + pointer.X - _startMouse.X, 0, maxX);
            var py = Math.Clamp(_startElement.Y + pointer.Y - _startMouse.Y, 0, maxY);
            _updating = true;
            _x.Value = maxX == 0 ? 0 : Math.Clamp((int)Math.Round(100.0 * px / maxX), 0, 100);
            _y.Value = maxY == 0 ? 0 : Math.Clamp((int)Math.Round(100.0 * py / maxY), 0, 100);
            _updating = false;
            _element.Location = new Point(px, py);
        };
        _element.MouseUp += (_, _) => { _dragging = false; _element.Capture = false; };
        _canvas.Controls.Add(_element);
        Controls.Add(_canvas);

        var row = new FlowLayoutPanel
        {
            Location = new Point(18, 365), Width = 745, Height = 82,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            WrapContents = true, FlowDirection = FlowDirection.LeftToRight
        };
        _binding.Items.AddRange(new object[] { "Clock", "Date", "Text", "CPU", "RAM", "Battery" });
        _binding.SelectedItem = layout.Binding;
        _x.Value = layout.ContentXPercent;
        _y.Value = layout.ContentYPercent;
        _fontSize.Value = layout.FontSize;
        _opacity.Value = layout.OpacityPercent;
        _border.Checked = layout.ShowWidgetBorder;
        _color.Text = layout.AccentHex;
        _text.Text = layout.Text;
        foreach (var control in new Control[]
        {
            Caption("Binding"), _binding, Caption("X %"), _x, Caption("Y %"), _y,
            Caption("Font"), _fontSize, Caption("Opacity"), _opacity,
            _border, Caption("Accent"), _color, _text
        })
            row.Controls.Add(control);
        Controls.Add(row);

        var actions = new FlowLayoutPanel
        {
            Location = new Point(18, 460), Width = 745, Height = 42,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var save = new Button { Text = "Save / Αποθήκευση", Width = 155, Height = 34 };
        var saveAs = new Button { Text = "Save as / Νέο όνομα", Width = 180, Height = 34 };
        var preview = new Button { Text = "Open widget / Προβολή", Width = 180, Height = 34 };
        actions.Controls.Add(save);
        actions.Controls.Add(saveAs);
        actions.Controls.Add(preview);
        Controls.Add(actions);

        save.Click += (_, _) => Save(false);
        saveAs.Click += (_, _) => Save(true);
        preview.Click += (_, _) =>
        {
            try
            {
                Collect();
                new OpsisCustomWidgetWindow(_layout).Show(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Opsis Widget Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        foreach (var control in new Control[] { _binding, _color, _text, _border, _fontSize, _opacity })
        {
            switch (control)
            {
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => RefreshPreview(); break;
                case TextBox edit: edit.TextChanged += (_, _) => RefreshPreview(); break;
                case CheckBox check: check.CheckedChanged += (_, _) => RefreshPreview(); break;
                case NumericUpDown num: num.ValueChanged += (_, _) => RefreshPreview(); break;
            }
        }
        _x.ValueChanged += (_, _) => RefreshPosition();
        _y.ValueChanged += (_, _) => RefreshPosition();
        _canvas.Resize += (_, _) => RefreshPosition();
        FormClosed += (_, _) => _previewFont?.Dispose();
        RefreshPreview();
    }

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(5, 9, 1, 0)
    };

    private void RefreshPosition()
    {
        if (_updating || _dragging) return;
        var (x, y) = OpsisWidgetDesigner.Position(_canvas.ClientSize.Width,
            _canvas.ClientSize.Height, _element.Width, _element.Height,
            (int)_x.Value, (int)_y.Value);
        _element.Location = new Point(x, y);
    }

    private void RefreshPreview()
    {
        _element.Text = _binding.SelectedItem?.ToString() switch
        {
            "Clock" => "12:34:56", "Date" => "24 NOV 2026",
            "CPU" => "CPU 37%", "RAM" => "RAM 62%",
            "Battery" => "Battery 85%", _ => _text.Text
        };
        try { _element.ForeColor = ColorTranslator.FromHtml(_color.Text); }
        catch (ArgumentException) { _element.ForeColor = UiTheme.TextSecondary; }
        if (_previewFont == null || Math.Abs(_previewFont.Size - (float)_fontSize.Value) > 0.05f)
        {
            var oldFont = _previewFont;
            _previewFont = new Font("Segoe UI Variable Display", (float)_fontSize.Value, FontStyle.Regular);
            _element.Font = _previewFont;
            oldFont?.Dispose();
        }
        _element.BorderStyle = _border.Checked ? BorderStyle.FixedSingle : BorderStyle.None;
        _element.Width = Math.Min(290, Math.Max(190, _canvas.Width / 3));
        RefreshPosition();
    }

    private void Collect()
    {
        _layout.ContentXPercent = (int)_x.Value;
        _layout.ContentYPercent = (int)_y.Value;
        _layout.FontSize = (int)_fontSize.Value;
        _layout.OpacityPercent = (int)_opacity.Value;
        _layout.Binding = _binding.SelectedItem?.ToString() ?? "Clock";
        _layout.AccentHex = _color.Text.Trim();
        _layout.Text = _text.Text;
        _layout.ShowWidgetBorder = _border.Checked;
        OpsisWidgetDesigner.Validate(_layout);
    }

    private void Save(bool newName)
    {
        try
        {
            Collect();
            if (newName)
            {
                using var dialog = new Form
                {
                    Text = "Save widget as", Size = new Size(365, 145),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MinimizeBox = false, MaximizeBox = false
                };
                var field = new TextBox { Left = 16, Top = 18, Width = 315, Text = _layout.Name + " Copy" };
                var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 225, Top = 54, Width = 105 };
                dialog.Controls.Add(field); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var copy = OpsisWidgetDesigner.Read(_originalName);
                copy.Name = field.Text.Trim();
                copy.Binding = _layout.Binding;
                copy.Text = _layout.Text;
                copy.AccentHex = _layout.AccentHex;
                copy.FontSize = _layout.FontSize;
                copy.Width = _layout.Width;
                copy.Height = _layout.Height;
                copy.OpacityPercent = _layout.OpacityPercent;
                copy.AlwaysOnTop = _layout.AlwaysOnTop;
                copy.ContentXPercent = _layout.ContentXPercent;
                copy.ContentYPercent = _layout.ContentYPercent;
                copy.ShowWidgetBorder = _layout.ShowWidgetBorder;
                OpsisWidgetDesigner.Save(copy);
            }
            else OpsisWidgetDesigner.Update(_originalName, _layout);
            MessageBox.Show(this, "Saved.", "Opsis Widget Designer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opsis Widget Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

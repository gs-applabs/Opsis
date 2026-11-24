using System;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// Small, standalone user-designed widget, powered by the same WinForms
/// process/theme as Opsis. No WebView2, plugin DLLs, polling threads or scripts.
/// </summary>
public sealed class OpsisCustomWidgetWindow : Form
{
    private readonly OpsisWidgetDesigner.Layout _layout;
    private readonly Label _content;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    public OpsisCustomWidgetWindow(OpsisWidgetDesigner.Layout layout)
    {
        OpsisWidgetDesigner.Validate(layout);
        _layout = layout;
        Text = "Opsis Widget — " + layout.Name;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(layout.Width, layout.Height);
        MinimumSize = new Size(190, 80);
        TopMost = layout.AlwaysOnTop;
        Opacity = layout.OpacityPercent / 100.0;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        BackColor = UiTheme.Surface;
        var accent = ColorTranslator.FromHtml(layout.AccentHex);
        _content = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = accent,
            BackColor = UiTheme.Surface,
            Font = new Font("Segoe UI Variable Display", layout.FontSize, FontStyle.Regular),
            AutoEllipsis = true
        };
        Controls.Add(_content);
        _refresh.Tick += (_, _) => UpdateText();
        Shown += (_, _) => { UpdateText(); if (layout.Binding != "Text") _refresh.Start(); };
        FormClosed += (_, _) => { _refresh.Stop(); _refresh.Dispose(); _content.Font.Dispose(); };
    }

    private void UpdateText()
    {
        _content.Text = _layout.Binding switch
        {
            "Clock" => DateTime.Now.ToString("HH:mm:ss"),
            "Date" => DateTime.Now.ToString("dddd, dd MMMM yyyy"),
            _ => _layout.Text
        };
    }
}

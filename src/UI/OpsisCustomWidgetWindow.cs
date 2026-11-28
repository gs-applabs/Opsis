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
    private readonly System.Collections.Generic.List<(Label Label, OpsisWidgetDesigner.Row Row)> _rows = new();
    private static readonly HashSet<OpsisCustomWidgetWindow> OpenWindows = new();
    private static System.Windows.Forms.Timer? _refresh;
    private readonly bool _dynamic;
    private bool _hiddenForSession;
    private bool _registered, _resourcesDisposed;
    private static bool Permitted => RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.Widgets) &&
        !WindowsUpdateGuard.RequiresReview && !WindowsUpdateGuard.RuntimeHold;
    private static bool NeedsRefresh => Permitted && OpenWindows.Any(w => !w.IsDisposed && w.Visible && w._dynamic);
    internal static void ReconcileSession()
    {
        foreach (var window in OpenWindows.ToArray())
        {
            if (window.IsDisposed) continue;
            if (!Permitted && window.Visible) { window._hiddenForSession = true; window.Hide(); }
            else if (Permitted && window._hiddenForSession) { window._hiddenForSession = false; window.Show(); window.UpdateText(); }
        }
        SyncRefresh();
    }
    private static void SyncRefresh()
    {
        if (!NeedsRefresh)
        {
            _refresh?.Dispose(); _refresh = null; return;
        }
        if (_refresh != null) return;
        _refresh = new System.Windows.Forms.Timer { Interval = 1000 };
        _refresh.Tick += (_, _) =>
        {
            if (!Permitted) { ReconcileSession(); return; }
            foreach (var window in OpenWindows.ToArray())
                if (window.Visible && !window.IsDisposed && window._dynamic) window.UpdateText();
        };
        _refresh.Start();
    }
    public OpsisCustomWidgetWindow(OpsisWidgetDesigner.Layout layout)
    {
        if (!Permitted) throw new InvalidOperationException("Widgets are suspended or compatibility review is required.");
        layout = OpsisWidgetDesigner.Copy(layout);
        _layout = layout;
        _dynamic = layout.Rows is { Length: > 0 } rowsData ? rowsData.Any(r => r.Binding != "Text") : layout.Binding != "Text";
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
            AutoSize = false,
            Size = new Size(Math.Min(330, Math.Max(160, layout.Width - 40)), Math.Min(115, Math.Max(60, layout.Height - 30))),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = accent,
            BackColor = UiTheme.Surface,
            Font = new Font("Segoe UI Variable Display", layout.FontSize, FontStyle.Regular),
            AutoEllipsis = true,
            BorderStyle = layout.ShowWidgetBorder ? BorderStyle.FixedSingle : BorderStyle.None
        };
        Controls.Add(_content);
        void PositionContent()
        {
            var (x, y) = OpsisWidgetDesigner.Position(ClientSize.Width, ClientSize.Height,
                _content.Width, _content.Height, layout.ContentXPercent, layout.ContentYPercent);
            _content.Location = new Point(x, y);
        }
        Resize += (_, _) => PositionContent();
        PositionContent();
        if (layout.Rows is { Length: > 0 } rows)
        {
            _content.Visible = false;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length,
                Padding = new Padding(8), BackColor = UiTheme.Surface };
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
                    ForeColor = ColorTranslator.FromHtml(row.AccentHex), Font = new Font("Segoe UI Variable Display", row.FontSize),
                    BackColor = UiTheme.Surface };
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows.Length));
                table.Controls.Add(label, 0, i);
                _rows.Add((label, row));
            }
            Controls.Add(table); table.BringToFront();
        }
        Shown += (_, _) =>
        {
            if (!_registered) { OpenWindows.Add(this); _registered = true; ThemeManager.Changed += RefreshTheme; }
            RefreshTheme(); UpdateText(); ReconcileSession();
        };
        VisibleChanged += (_, _) => { if (_registered) SyncRefresh(); };
    }

    private void RefreshTheme()
    {
        if (IsDisposed || Disposing) return;
        var surface = SystemInformation.HighContrast ? SystemColors.Window : UiTheme.Surface;
        BackColor = surface;
        _content.BackColor = surface;
        _content.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : TextContrast.Readable(ColorTranslator.FromHtml(_layout.AccentHex), surface);
        foreach (var (label, row) in _rows)
        {
            label.BackColor = surface;
            if (label.Parent != null) label.Parent.BackColor = surface;
            label.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : TextContrast.Readable(ColorTranslator.FromHtml(row.AccentHex), surface);
        }
        Opacity = SystemInformation.HighContrast ? 1 : _layout.OpacityPercent / 100.0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            if (_registered) { _registered = false; OpenWindows.Remove(this); ThemeManager.Changed -= RefreshTheme; SyncRefresh(); }
            _content?.Font.Dispose();
            foreach (var entry in _rows) entry.Label.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdateText()
    {
        try
        {
            var needsSystem = _rows.Count > 0 ? _rows.Any(x => x.Row.Binding is "CPU" or "RAM") : _layout.Binding is "CPU" or "RAM";
            var system = needsSystem ? SystemMonitorService.Instance.GetSnapshot() : default;
            string Value(string binding, string text) => binding switch
            {
                "Clock" => DateTime.Now.ToString("HH:mm:ss"),
                "Date" => DateTime.Now.ToString("dddd, dd MMMM yyyy"),
                "CPU" => $"CPU {system.CpuPercent:0.0}%",
                "RAM" => system.TotalMemoryMb > 0
                    ? $"RAM {(system.TotalMemoryMb - system.AvailableMemoryMb) / system.TotalMemoryMb * 100:0.0}%"
                    : "RAM —",
                "Battery" => SystemInformation.PowerStatus.BatteryLifePercent is >= 0 and <= 1
                    ? $"Battery {SystemInformation.PowerStatus.BatteryLifePercent * 100:0}%"
                    : "Battery —",
                _ => text
            };
            if (_rows.Count > 0)
                foreach (var entry in _rows) entry.Label.Text = Value(entry.Row.Binding, entry.Row.Text);
            else _content.Text = Value(_layout.Binding, _layout.Text);
        }
        catch (Exception)
        {
            if (_rows.Count > 0) foreach (var entry in _rows) entry.Label.Text = entry.Row.Binding + " —";
            else _content.Text = _layout.Binding + " —";
        }
    }
}

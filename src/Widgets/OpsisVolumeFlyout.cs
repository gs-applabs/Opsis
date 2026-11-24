using System;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;
using NAudio.CoreAudioApi;

namespace MotionDesk.Widgets;

/// <summary>On-demand default-output control using the already shipped WASAPI
/// library. No polling, no replacement of Windows volume flyout, no persistent COM session.</summary>
internal sealed class OpsisVolumeFlyout : Form
{
    private static OpsisVolumeFlyout? _current;
    private readonly TrackBar _volume = new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Dock = DockStyle.Top };
    private readonly CheckBox _mute = new() { Text = "Mute / Σίγαση", Dock = DockStyle.Top };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 48, AutoEllipsis = true };
    private bool _loading;

    internal static void CloseCurrent() { _current?.Close(); _current = null; }
    internal static void Open(Rectangle anchor, string style)
    {
        if (style is not ("Compact" or "Large") || SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview) return;
        CloseCurrent();
        var form = _current = new OpsisVolumeFlyout(style);
        var work = Screen.FromRectangle(anchor).WorkingArea;
        form.Location = new Point(Math.Clamp(anchor.Left, work.Left, Math.Max(work.Left, work.Right - form.Width)),
            Math.Clamp(anchor.Top - form.Height, work.Top, Math.Max(work.Top, work.Bottom - form.Height)));
        form.Show();
    }

    private OpsisVolumeFlyout(string style)
    {
        Text = "Opsis — Volume / Ένταση";
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ClientSize = style == "Large" ? new Size(420, 200) : new Size(280, 152);
        BackColor = UiTheme.PageBackground; ForeColor = UiTheme.TextPrimary;
        var apply = new Button { Text = "Apply / Εφαρμογή", Dock = DockStyle.Bottom, Height = 32 };
        Controls.Add(apply); Controls.Add(_mute); Controls.Add(_volume); Controls.Add(_status);
        _volume.ValueChanged += (_, _) => { if (!_loading) apply.Text = "Apply " + _volume.Value + "%"; };
        apply.Click += (_, _) => ApplyVolume();
        Shown += (_, _) => Reload();
        FormClosed += (_, _) => { if (ReferenceEquals(_current, this)) _current = null; };
    }
    private void Reload()
    {
        _loading = true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _volume.Value = Math.Clamp((int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), 0, 100);
            _mute.Checked = device.AudioEndpointVolume.Mute;
            _status.Text = device.FriendlyName;
            _volume.Enabled = _mute.Enabled = true;
        }
        catch (Exception ex) { _status.Text = "Audio unavailable: " + ex.Message; _volume.Enabled = _mute.Enabled = false; }
        finally { _loading = false; }
    }
    private void ApplyVolume()
    {
        if (!_volume.Enabled || WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            // Avoid writing an old device's settings to a new device after a switch.
            if (!string.Equals(device.FriendlyName, _status.Text, StringComparison.Ordinal)) { Reload(); return; }
            var endpoint = device.AudioEndpointVolume;
            if (Math.Abs(endpoint.MasterVolumeLevelScalar - _volume.Value / 100f) > .005f)
                endpoint.MasterVolumeLevelScalar = _volume.Value / 100f;
            if (endpoint.Mute != _mute.Checked) endpoint.Mute = _mute.Checked;
            Reload();
        }
        catch (Exception ex) { _status.Text = "Audio unavailable: " + ex.Message; }
    }
}

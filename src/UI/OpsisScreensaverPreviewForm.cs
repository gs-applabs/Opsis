using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// User-initiated native screensaver preview. Does not install a Windows .scr
/// handler, launch scripts or poll user input globally.
/// </summary>
public sealed class OpsisScreensaverPreviewForm : Form
{
    private readonly OpsisScreensaverStudio.Profile _profile;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private Image? _image;
    private readonly DateTime _opened = DateTime.UtcNow;
    private Point? _lastMouse;

    public OpsisScreensaverPreviewForm(OpsisScreensaverStudio.Profile profile)
    {
        OpsisScreensaverStudio.Validate(profile);
        _profile = profile;
        Text = "Opsis — Screensaver Preview";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.FromArgb(7, 13, 22);
        DoubleBuffered = true;
        if (_profile.Mode != "Clock")
        {
            if (!File.Exists(_profile.ImagePath) || new FileInfo(_profile.ImagePath).Length > 64L * 1024 * 1024)
                throw new InvalidDataException("Screensaver image missing or too large.");
            using var input = Image.FromFile(_profile.ImagePath);
            if ((long)input.Width * input.Height > 45_000_000)
                throw new InvalidDataException("Screensaver image exceeds the safe pixel budget.");
            _image = new Bitmap(input);
        }
        _clock.Tick += (_, _) => Invalidate();
        Shown += (_, _) => { Activate(); _clock.Start(); };
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.Enter) Close(); };
        MouseDown += (_, _) => Close();
        MouseMove += (_, e) =>
        {
            if ((DateTime.UtcNow - _opened).TotalSeconds < 1.5) return;
            if (_lastMouse.HasValue &&
                (Math.Abs(_lastMouse.Value.X - e.X) > 8 || Math.Abs(_lastMouse.Value.Y - e.Y) > 8))
                Close();
            _lastMouse = e.Location;
        };
        FormClosed += (_, _) => { _clock.Stop(); _clock.Dispose(); _image?.Dispose(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_image != null)
        {
            var bounds = ClientRectangle;
            var factor = Math.Max((double)bounds.Width / _image.Width,
                (double)bounds.Height / _image.Height);
            var width = (int)Math.Ceiling(_image.Width * factor);
            var height = (int)Math.Ceiling(_image.Height * factor);
            g.DrawImage(_image, (bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        }
        using var shade = new SolidBrush(Color.FromArgb((int)(255 * _profile.DimPercent / 100.0),
            Color.Black));
        g.FillRectangle(shade, ClientRectangle);
        if (_profile.Mode == "StaticImage") return;
        var text = DateTime.Now.ToString(_profile.ShowSeconds ? "HH:mm:ss" : "HH:mm");
        using var font = new Font("Segoe UI Variable Display", Math.Clamp(Width / 20f, 32f, 90f),
            FontStyle.Light, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var center = new StringFormat { Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, ClientRectangle, center);
    }
}

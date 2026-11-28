using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// Shared native screensaver scene, fullscreen or embedded in the Windows
/// settings preview HWND. No global input hook or core/tray initialization.
/// </summary>
public sealed class OpsisScreensaverPreviewForm : Form
{
    private readonly OpsisScreensaverStudio.Profile _profile;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private Image? _image;
    private readonly DateTime _opened = DateTime.UtcNow;
    private Point? _lastMouse;
    private readonly IntPtr _parent;
    private readonly bool _embedded;
    protected override bool ShowWithoutActivation => _embedded;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_embedded) { cp.Parent = _parent; cp.Style = (cp.Style & unchecked((int)~0x80000000u)) | 0x40000000; cp.ExStyle |= 0x08000000; }
            return cp;
        }
    }

    public OpsisScreensaverPreviewForm(OpsisScreensaverStudio.Profile profile, IntPtr parent = default, bool allScreens = false)
    {
        _parent = parent; _embedded = parent != IntPtr.Zero;
        OpsisScreensaverStudio.Validate(profile);
        _profile = profile;
        Text = "Opsis — Screensaver Preview";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = !_embedded;
        if (_embedded)
        {
            StartPosition = FormStartPosition.Manual;
            if (!OpsisScreensaverHost.TryPreviewSize(parent, out var size)) throw new InvalidOperationException("Preview window is no longer available.");
            Bounds = new Rectangle(Point.Empty, size);
        }
        else if (allScreens)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
        }
        else WindowState = FormWindowState.Maximized;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.FromArgb(7, 13, 22);
        DoubleBuffered = true;
        if (_profile.Mode != "Clock")
        {
            OpsisScreensaverStudio.RequireLocalImage(_profile.ImagePath);
            using var input = Image.FromFile(_profile.ImagePath);
            if ((long)input.Width * input.Height > 45_000_000)
                throw new InvalidDataException("Screensaver image exceeds the safe pixel budget.");
            _image = new Bitmap(input);
        }
        _clock.Tick += (_, _) =>
        {
            if (_embedded)
            {
                if (!OpsisScreensaverHost.TryPreviewSize(_parent, out var size)) { Close(); return; }
                if (Size != size) Size = size;
            }
            if (_profile.Mode != "StaticImage") Invalidate();
        };
        Shown += (_, _) =>
        {
            if (!_embedded) Activate();
            if (_embedded || _profile.Mode != "StaticImage") _clock.Start();
        };
        if (!_embedded)
        {
            KeyDown += (_, _) => Close();
            MouseDown += (_, _) => Close();
            MouseMove += (_, e) =>
            {
                if ((DateTime.UtcNow - _opened).TotalSeconds < 1.5) return;
                _lastMouse ??= e.Location;
                if (Math.Abs(_lastMouse.Value.X - e.X) > 8 || Math.Abs(_lastMouse.Value.Y - e.Y) > 8) Close();
            };
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _clock.Stop(); _clock.Dispose(); _image?.Dispose(); _image = null; }
        base.Dispose(disposing);
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
        using var font = new Font("Segoe UI Variable Display", Math.Clamp(Width / 20f, _embedded ? 8f : 32f, 90f),
            FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var center = new StringFormat { Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, ClientRectangle, center);
    }
}

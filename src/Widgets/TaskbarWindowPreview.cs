using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

/// <summary>One explicitly opened, compositor-owned live thumbnail. No PrintWindow,
/// capture bitmaps, hover polling or changes to Explorer thumbnails.</summary>
internal sealed class TaskbarWindowPreview : Form
{
    private static TaskbarWindowPreview? _current;
    private IntPtr _thumbnail;
    private readonly IntPtr _source;
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 32, AutoEllipsis = true };

    internal static void CloseCurrent() { _current?.Close(); _current = null; }
    internal static void Open(IntPtr source, string title, Rectangle anchor, string style)
    {
        if (source == IntPtr.Zero || !IsWindow(source) || style is not ("Compact" or "Large") ||
            WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode) return;
        CloseCurrent();
        var window = _current = new TaskbarWindowPreview(source, title, style);
        var work = Screen.FromRectangle(anchor).WorkingArea;
        window.Location = new Point(Math.Clamp(anchor.Left, work.Left, Math.Max(work.Left, work.Right - window.Width)),
            Math.Clamp(anchor.Top - window.Height, work.Top, Math.Max(work.Top, work.Bottom - window.Height)));
        window.Show();
    }

    private TaskbarWindowPreview(IntPtr source, string title, string style)
    {
        _source = source; Text = title;
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ClientSize = style == "Large" ? new Size(480, 300) : new Size(280, 190);
        BackColor = UiTheme.PageBackground; ForeColor = UiTheme.TextPrimary;
        _status.Text = title; Controls.Add(_status);
        var activate = new Button { Dock = DockStyle.Bottom, Height = 32, Text = "Activate / Ενεργοποίηση" };
        activate.Click += (_, _) => { Close(); if (IsWindow(_source)) WinApi.BringToFront(_source); };
        Controls.Add(activate);
        Shown += (_, _) => Attach();
        Resize += (_, _) => UpdateThumbnail();
        FormClosed += (_, _) => { Release(); if (ReferenceEquals(_current, this)) _current = null; };
        Disposed += (_, _) => Release();
    }

    private void Attach()
    {
        if (DwmRegisterThumbnail(Handle, _source, out _thumbnail) != 0) { _thumbnail = IntPtr.Zero; _status.Text = "Live thumbnail unavailable"; return; }
        UpdateThumbnail();
    }
    private void UpdateThumbnail()
    {
        if (_thumbnail == IntPtr.Zero) return;
        if (DwmQueryThumbnailSourceSize(_thumbnail, out var size) != 0 || size.Width <= 0 || size.Height <= 0)
        { Release(); _status.Text = "Live thumbnail unavailable"; return; }
        var available = new Rectangle(8, 36, Math.Max(1, ClientSize.Width - 16), Math.Max(1, ClientSize.Height - 76));
        double scale = Math.Min((double)available.Width / size.Width, (double)available.Height / size.Height);
        int w = Math.Max(1, (int)(size.Width * scale)), h = Math.Max(1, (int)(size.Height * scale));
        int x = available.Left + (available.Width - w) / 2, y = available.Top + (available.Height - h) / 2;
        var p = new Properties { Flags = 0x1 | 0x4 | 0x8 | 0x10, Destination = new Rect { Left = x, Top = y, Right = x + w, Bottom = y + h },
            Opacity = 255, Visible = true, SourceClientAreaOnly = false };
        if (DwmUpdateThumbnailProperties(_thumbnail, ref p) != 0) { Release(); _status.Text = "Live thumbnail unavailable"; }
    }
    private void Release() { if (_thumbnail != IntPtr.Zero) { DwmUnregisterThumbnail(_thumbnail); _thumbnail = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Properties
    {
        public uint Flags; public Rect Destination; public Rect Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out NativeSize size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref Properties properties);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
}

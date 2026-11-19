using MotionDesk.Containers;
using MotionDesk.Services;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var stable = new IntPtr[] { 30, 1, 11, 2, 12, 9, 80 };
        Check(DesktopZOrder.PlanBand(stable, new HashSet<IntPtr> { 9 }, new HashSet<IntPtr> { 1, 2 },
            new HashSet<IntPtr> { 11, 12 }, out _).Length == 0, "Stable container and glass ordering needs no native move");
        var scattered = new IntPtr[] { 1, 11, 30, 2, 12, 9, 80 };
        var band = DesktopZOrder.PlanBand(scattered, new HashSet<IntPtr> { 9 }, new HashSet<IntPtr> { 1, 2 },
            new HashSet<IntPtr> { 11, 12 }, out IntPtr after);
        Check(after == 30 && new IntPtr[] { 1, 11, 2, 12 }.SequenceEqual(band), "Repair preserves each glass pair below a regular window");

        using var container = new LayeredForm
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(140, 140, 320, 200), ShowInTaskbar = false
        };
        using var surface = new LayeredSurface();
        nint handle = container.Handle;
        void Paint(Rectangle bounds)
        {
            using (var g = surface.Begin(bounds.Width, bounds.Height)) g.Clear(Color.DodgerBlue);
            if (!surface.Present(handle, bounds.Location)) throw new InvalidOperationException("UpdateLayeredWindow rejected the frame");
            GetWindowRect(handle, out var actual);
            if (actual.Left != bounds.Left || actual.Top != bounds.Top || actual.Right != bounds.Right || actual.Bottom != bounds.Bottom)
                throw new InvalidOperationException("Layered frame did not commit its native geometry");
        }
        Paint(container.Bounds);
        Check(!container.Visible, "First fully drawn frame can be installed before the form becomes visible");
        container.Show();
        Application.DoEvents();
        for (int i = 0; i < 30; i++)
        {
            Paint(new Rectangle(140 + i, 140, 320, i % 2 == 0 ? 32 : 205));
            Application.DoEvents();
        }
        Check(container.Visible, "Thirty collapse and expand frames commit their geometry while visible");

        var desktop = DesktopZOrder.DesktopWindows();
        Check(desktop.Count != 0, "Current Explorer desktop anchor is found");
        using var app = new Form { ShowInTaskbar = false, Bounds = new Rectangle(155, 155, 250, 140), StartPosition = FormStartPosition.Manual };
        app.Show();
        app.BringToFront();
        container.SendToBack();
        DesktopZOrder.PlaceGroup(new HashSet<IntPtr> { handle });
        Application.DoEvents();
        var chain = DesktopZOrder.Chain();
        var desktopIndex = desktop.Select(handle => chain.IndexOf(handle)).Where(i => i >= 0).Min();
        Check(chain.IndexOf(app.Handle) >= 0 && chain.IndexOf(app.Handle) < chain.IndexOf(handle) &&
            chain.IndexOf(handle) < desktopIndex, "Recovered container lies below a normal application and above the real Explorer desktop");
    }

    private static void Check(bool test, string name)
    {
        if (!test) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    private sealed class LayeredForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x8000000 | 0x80; return cp; }
        }
    }
}

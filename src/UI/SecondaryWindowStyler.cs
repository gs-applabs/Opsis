using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MotionDesk.UI;

/// <summary>
/// 2.8.0.2510: every secondary window of Opsis (settings dialogs, the zone editor, mapping / mask / weather editors, the dialogs of the containers, prompts...) follows the design of the main
/// window: the colours of the active window skin (the same tokens the shared Opsis.UI controls use), a dark / skin-coloured title bar, the Opsis icon, flat Opsis-style standard controls and dark
/// scroll bars. It is done in ONE place: a message filter styles a top-level window the moment before its first paint, so no dialog needs its own code and none flashes in system colours.
/// Borderless windows (containers, widgets, wallpaper, overlays, the dock) and windows that already draw their own chrome (<see cref="OpsisWindow"/>, the main window) are left alone.
/// </summary>
internal sealed class SecondaryWindowStyler : IMessageFilter
{
    private const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085;
    private static SecondaryWindowStyler? _instance;
    private static readonly ConditionalWeakTable<Control, object> Done = new();

    /// <summary>Installs the filter once for the UI thread.</summary>
    public static void Install()
    {
        if (_instance != null) return;
        _instance = new SecondaryWindowStyler();
        Application.AddMessageFilter(_instance);
        // the skin / theme changed: windows that are open now follow it too
        ThemeManager.Changed += () => { foreach (Form open in Application.OpenForms.Cast<Form>().ToList()) if (open.IsHandleCreated) open.BeginInvoke(new Action(RestyleOpen)); };
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is WM_PAINT or WM_NCPAINT && Control.FromHandle(m.HWnd) is Form f && ShouldStyle(f) && !Done.TryGetValue(f, out _))
            Style(f);
        return false;
    }

    private static void RestyleOpen()
    {
        foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
            if (ShouldStyle(f)) { Done.Remove(f); Style(f); }
    }

    /// <summary>Pure rule (tested): which windows are secondary windows of the application.</summary>
    internal static bool ShouldStyle(Form f) =>
        f is not MainWindow and not OpsisWindow && f.TopLevel && !f.IsDisposed && f.FormBorderStyle != FormBorderStyle.None && !"opsis-skip".Equals(f.Tag);

    internal static void Style(Form f)
    {
        Done.AddOrUpdate(f, new object());
        var t = new OpsisUiTokens();
        try
        {
            if (f.BackColor == SystemColors.Control || f.BackColor == SystemColors.Window || f.BackColor == Color.Empty || IsOurColour(f.BackColor)) f.BackColor = t.Background;
            if (f.ForeColor == SystemColors.ControlText || f.ForeColor == SystemColors.WindowText) f.ForeColor = t.Text;
            if (ReferenceEquals(f.Icon, SystemIcons.Application) || f.ShowIcon) { try { f.Icon = AppWindowIcon.Value; } catch (Exception) { } }
            Recolor(f, t);
            f.ControlAdded += (_, e) => { if (e.Control != null) Recolor(e.Control, new OpsisUiTokens()); };
            TitleBar(f, t);
            f.Invalidate(true);
        }
        catch (Exception ex) { Services.RotatingLogger.Write("Secondary window style failed: " + ex.GetType().Name + ": " + ex.Message); }
    }

    // a colour that already belongs to the Opsis palettes (a dialog that colours itself): kept as it is
    private static bool IsOurColour(Color c) => false;

    private static void Recolor(Control c, OpsisUiTokens t)
    {
        if (c.GetType().Namespace == "Opsis.UI") return;                 // the shared controls draw from the tokens themselves
        bool defaultBack = c.BackColor == SystemColors.Control || c.BackColor == SystemColors.Window || c.BackColor == SystemColors.ControlLight || c.BackColor == SystemColors.ButtonFace;
        bool defaultFore = c.ForeColor == SystemColors.ControlText || c.ForeColor == SystemColors.WindowText;
        switch (c)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderColor = t.Border; b.FlatAppearance.MouseOverBackColor = t.SurfaceHover; b.FlatAppearance.MouseDownBackColor = t.SurfaceHover;
                if (defaultBack) b.BackColor = t.Surface;
                if (defaultFore) b.ForeColor = t.Text;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat; if (defaultBack) cb.BackColor = t.Surface; if (defaultFore) cb.ForeColor = t.Text;
                if (cb.DropDownStyle != ComboBoxStyle.Simple && cb.DrawMode == DrawMode.Normal)
                {
                    // the native selection box and list ignore BackColor: owner-draw them in the token colours
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.DrawItem += (s, e) =>
                    {
                        var box = (ComboBox)s!; var tk = new OpsisUiTokens();
                        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                        using (var bg = new SolidBrush(sel ? tk.SurfaceHover : tk.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
                        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, new Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height), tk.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    };
                }
                break;
            case TextBoxBase or ListBox or ListView or TreeView or NumericUpDown or DataGridView:
                if (defaultBack) c.BackColor = t.Surface; if (defaultFore) c.ForeColor = t.Text;
                if (c is TextBoxBase or ListBox) { try { ((dynamic)c).BorderStyle = BorderStyle.FixedSingle; } catch (Exception) { } }
                GlassBlur.ApplyScrollbarTheme(c, t.IsDark);
                break;
            case CheckBox or RadioButton:
                if (defaultFore) c.ForeColor = t.Text; if (c.BackColor == SystemColors.Control) c.BackColor = Color.Transparent; break;
            default:
                if (defaultBack && c is not PictureBox) c.BackColor = t.Background;
                if (defaultFore) c.ForeColor = t.Text;
                if (c is ScrollableControl { AutoScroll: true }) GlassBlur.ApplyScrollbarTheme(c, t.IsDark);
                break;
        }
        foreach (Control child in c.Controls) Recolor(child, t);
        c.ControlAdded -= OnChildAdded; c.ControlAdded += OnChildAdded;
    }

    private static void OnChildAdded(object? sender, ControlEventArgs e) { if (e.Control != null) Recolor(e.Control, new OpsisUiTokens()); }

    // ---- title bar: dark (immersive dark mode) and in the colours of the skin (Windows 11) ----
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void TitleBar(Form f, OpsisUiTokens t)
    {
        void Apply()
        {
            try
            {
                int dark = t.IsDark ? 1 : 0; DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int caption = Bgr(t.Background), text = Bgr(t.Text), border = Bgr(t.Border);
                DwmSetWindowAttribute(f.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(f.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
                DwmSetWindowAttribute(f.Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch (Exception) { }
        }
        if (f.IsHandleCreated) Apply(); else f.HandleCreated += (_, _) => Apply();
    }

    private static int Bgr(Color c) => c.R | (c.G << 8) | (c.B << 16);
}

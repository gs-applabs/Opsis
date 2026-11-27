using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    internal static bool StudioGreek => LocalizationManager.CurrentLanguage == "el";
    internal static string StudioText(string english, string greek) => StudioGreek ? greek : english;
    internal static Color StudioAccent => UiTheme.AccentCyan;

    private static Color AccentCaptionColor(Color background)
    {
        static double Linear(byte component)
        {
            double channel = component / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }

        double luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
        return luminance >= .179 ? Color.Black : Color.White;
    }

    internal static Button StudioButton(string text, Action action, int width = 140, bool emphasized = false)
    {
        const int inset = 32;
        int captionWidth = TextRenderer.MeasureText(text, UiTheme.FontBody, Size.Empty, TextFormatFlags.NoPadding).Width;
        width = Math.Max(width, Math.Min(392, captionWidth + inset));
        var background = emphasized ? UiTheme.AccentCyan : UiTheme.Surface;
        var button = new HoverButton(keyboard: true)
        {
            Text = text, Width = width, Height = CaptionHeight(text, width, inset, UiTheme.FontBody),
            Padding = new Padding(14, 3, 14, 3), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = background, BaseColor = background,
            HoverBackColor = emphasized ? BlendChrome(background, UiTheme.SurfaceHover, .19f) : UiTheme.SurfaceHover,
            ForeColor = emphasized ? AccentCaptionColor(background) : UiTheme.TextPrimary,
            Font = UiTheme.FontBody, Cursor = Cursors.Hand, UseMnemonic = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        return button;
    }

    internal static void StyleStudioDialog(Form dialog)
    {
        dialog.BackColor = UiTheme.PageBackground;
        dialog.ForeColor = UiTheme.TextPrimary;
        dialog.Font = UiTheme.FontBody;
        StyleStudioInputs(dialog);
        void Caption()
        {
            int dark = ThemeManager.Current.IsDark ? 1 : 0;
            try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            GlassBlur.ApplyRoundedCorners(dialog.Handle);
        }
        if (dialog.IsHandleCreated) Caption();
        else dialog.HandleCreated += (_, _) => Caption();
    }

    internal static void StyleStudioInputs(Control root)
    {
        switch (root)
        {
            case TextBox text:
                text.Font = UiTheme.FontBody;
                text.BackColor = UiTheme.Surface;
                text.ForeColor = UiTheme.TextPrimary;
                text.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox list:
                list.Font = UiTheme.FontBody;
                list.BackColor = UiTheme.Surface;
                list.ForeColor = UiTheme.TextPrimary;
                list.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.Font = UiTheme.FontBody;
                combo.BackColor = UiTheme.Surface;
                combo.ForeColor = UiTheme.TextPrimary;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown numeric:
                numeric.Font = UiTheme.FontBody;
                numeric.BackColor = UiTheme.Surface;
                numeric.ForeColor = UiTheme.TextPrimary;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                return;
            case CheckBox check:
                check.Font = UiTheme.FontBody;
                check.ForeColor = UiTheme.TextPrimary;
                check.FlatStyle = FlatStyle.Flat;
                break;
            case RadioButton radio:
                radio.Font = UiTheme.FontBody;
                radio.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : UiTheme.TextPrimary;
                if (radio.Appearance == Appearance.Button)
                {
                    radio.UseVisualStyleBackColor = false;
                    radio.BackColor = SystemInformation.HighContrast ? SystemColors.Control : UiTheme.Surface;
                    radio.FlatStyle = FlatStyle.Flat;
                    radio.FlatAppearance.BorderSize = 1;
                    radio.FlatAppearance.BorderColor = SystemInformation.HighContrast ? SystemColors.WindowText : UiTheme.TextSecondary;
                    radio.FlatAppearance.CheckedBackColor = SystemInformation.HighContrast ? SystemColors.ControlDark : UiTheme.SurfaceHover;
                    radio.FlatAppearance.MouseOverBackColor = SystemInformation.HighContrast ? SystemColors.Control : UiTheme.SurfaceHover;
                }
                break;
            case Button button when button.GetType() == typeof(Button) && button.FlatStyle == FlatStyle.Standard:
                button.Font = UiTheme.FontBody;
                if (string.IsNullOrWhiteSpace(button.AccessibleName) && !string.IsNullOrWhiteSpace(button.Text))
                    button.AccessibleName = button.Text;
                if (SystemInformation.HighContrast)
                {
                    button.FlatStyle = FlatStyle.System;
                    button.UseVisualStyleBackColor = true;
                    break;
                }
                button.ForeColor = UiTheme.TextPrimary;
                button.BackColor = UiTheme.Surface;
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = UiTheme.SurfaceHover;
                button.Cursor = Cursors.Hand;
                button.SizeChanged += (_, _) => ClipActionButton(button);
                ClipActionButton(button);
                button.Paint += (_, args) =>
                {
                    var colors = (button.FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                    bool hot = button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Cursor.Position));
                    PaintActionBackdrop(button, args);
                    PaintActionButton(button, args.Graphics, hot ? colors.Hover : button.BackColor,
                        button.Enabled ? button.ForeColor : colors.Muted, colors.Accent);
                };
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = UiTheme.PageBackground;
                page.ForeColor = UiTheme.TextPrimary;
                break;
            case TabControl tabs when tabs.DrawMode == TabDrawMode.Normal:
                tabs.Font = UiTheme.FontBody;
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.Padding = new Point(12, 6);
                tabs.DrawItem += (_, e) =>
                {
                    if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
                    var palette = (tabs.FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                    bool current = e.Index == tabs.SelectedIndex;
                    var at = tabs.GetTabRect(e.Index);
                    using var brush = new SolidBrush(current ? palette.Hover : palette.Navigation);
                    e.Graphics.FillRectangle(brush, at);
                    TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, UiTheme.FontBody, Rectangle.Inflate(at, -4, 0),
                        current ? palette.Text : palette.Secondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (current)
                    {
                        using var pen = new Pen(palette.Accent, 2);
                        e.Graphics.DrawLine(pen, at.Left + 5, at.Bottom - 2, at.Right - 5, at.Bottom - 2);
                    }
                    e.DrawFocusRectangle();
                };
                break;
        }
        foreach (Control child in root.Controls) StyleStudioInputs(child);
    }
}

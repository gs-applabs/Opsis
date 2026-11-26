using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private WindowSkinColors? _windowPalette;
    private UiPalette? _windowBaseColors;
    private WindowSkinColors CurrentSkinColors => _windowPalette ?? ColorsFromTheme(ThemeManager.Current);
    private Color SkinAccent => CurrentSkinColors.Accent;

    private static WindowSkinColors ColorsFromTheme(UiPalette theme) => new(theme.Background, theme.Sidebar, theme.Surface,
        theme.SurfaceHover, theme.Border, theme.TextPrimary, theme.TextSecondary, theme.TextMuted, theme.AccentCyan);

    private void RefreshSkinPalette()
    {
        var previous = _windowPalette;
        var theme = ThemeManager.Current;
        var settings = AppSettings.Peek();
        _windowBaseColors = theme;
        _windowPalette = SystemInformation.HighContrast || settings.ThemeMode == "Contrast" ? null :
            settings.ThemeMode == "SignatureGlass" ? ColorsFromTheme(theme) : WindowSkinColors.Default(_windowSkin, theme.IsDark);
        if (_windowPalette is { } colors)
        {
            if (theme.IsDark && settings.ThemeMode is "Dark" or "Follow")
                colors = colors.ShiftSurfaces(Math.Clamp((theme.Background.R - 18) / 2, -8, 16));
            if (settings.FollowWindowsAccent) colors = colors with { Accent = theme.AccentCyan };
            _windowPalette = colors;
        }
        OpsisUiTokens.Skin = _windowPalette;
        Opsis.UI.OpsisTheme.NotifyChanged();
        if (_windowPalette != previous)
            foreach (Control page in _content.Controls)
                ApplySkinColors(page, previous ?? ColorsFromTheme(theme));
    }

    private void ApplySkinColors(Control root) => ApplySkinColors(root, ColorsFromTheme(_windowBaseColors ?? ThemeManager.Current));

    private void ApplySkinColors(Control root, WindowSkinColors from)
    {
        if (_windowPalette is not { } palette) return;
        Color Map(Color value)
        {
            if (value == from.Background) return palette.Background;
            if (value == from.Navigation) return palette.Navigation;
            if (value == from.Surface) return palette.Surface;
            if (value == from.Hover) return palette.Hover;
            if (value == from.Border) return palette.Border;
            if (value == from.Accent) return palette.Accent;
            if (value == from.Text) return palette.Text;
            if (value == from.Secondary) return palette.Secondary;
            if (value == from.Muted) return palette.Muted;
            return value;
        }

        void Apply(Control node)
        {
            var back = Map(node.BackColor);
            var fore = Map(node.ForeColor);
            if (node is HoverButton button)
            {
                bool accented = button.BaseColor == from.Accent;
                button.BaseColor = Map(button.BaseColor);
                button.HoverBackColor = accented ? BlendChrome(palette.Accent, palette.Hover, .19f) : Map(button.HoverBackColor);
                if (accented) fore = AccentCaptionColor(palette.Accent);
            }
            node.BackColor = back;
            node.ForeColor = fore;
            foreach (Control child in node.Controls) Apply(child);
        }

        Apply(root);
    }
}

using System.Drawing;

namespace MotionDesk.UI
{
    // Colour tokens of the shared Opsis.UI controls: a live adapter on Opsis' own theme (UiTheme/ThemeManager), nothing is copied.
    internal sealed class OpsisUiTokens : Opsis.UI.IOpsisTheme
    {
        /// <summary>The palette of the active window skin (Atlas / Inspector / Index / Split...), or null for the plain theme: the shared controls (check boxes, switches, buttons, combo boxes) draw from these tokens, so they must follow the skin like the chrome does (they stayed on the theme colours, e.g. cyan accents in a violet skin).</summary>
        internal static WindowSkinColors? Skin;

        public bool IsDark => ThemeManager.Current.IsDark;
        public Color Background => Skin?.Background ?? UiTheme.Background;
        public Color Surface => Skin?.Surface ?? UiTheme.Surface;
        public Color Border => Skin?.Border ?? UiTheme.Border;
        public Color Text => Skin?.Text ?? UiTheme.TextPrimary;
        public Color TextMuted => Skin?.Secondary ?? UiTheme.TextSecondary;       // the former controls used TextSecondary for the chevron / disabled text
        public Color Accent => Skin?.Accent ?? UiTheme.AccentCyan;
        public Color SurfaceHover => Skin?.Hover ?? UiTheme.SurfaceHover;

        private static bool _installed;

        /// <summary>Installs the tokens once, before the first control paints, and forwards theme changes to the library.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Opsis.UI.OpsisTheme.Use(new OpsisUiTokens());
            ThemeManager.Changed += () => Opsis.UI.OpsisTheme.NotifyChanged();
            ThemeManager.Repainted += () => Opsis.UI.OpsisTheme.NotifyChanged();
            // the common words (ui.*) follow the app's language, and Opsis' own translation of a key (if it has one) wins over the shared one
            Opsis.UI.OpsisStrings.Language = MotionDesk.Services.LocalizationManager.CurrentLanguage;
            MotionDesk.Services.LocalizationManager.Changed += () => Opsis.UI.OpsisStrings.Language = MotionDesk.Services.LocalizationManager.CurrentLanguage;
        }
    }
}

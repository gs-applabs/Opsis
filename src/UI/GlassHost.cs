using System;
using System.Drawing;
using Opsis.UI;

namespace MotionDesk.UI
{
    // 2.6.0: the bridge between the shared Glass state (Opsis.UI.GlassManager) and Opsis' own palette (UiPalette / ThemeManager).
    // ThemeManager.Current returns the raw palette while Glass is off and a tinted copy while it is on, so EVERY consumer (UiTheme, the Opsis.UI controls through
    // OpsisUiTokens, widgets, dialogs) follows without changing a line. The tinted copy is cached: one new palette per (raw palette, Glass change).
    internal static class GlassHost
    {
        /// <summary>Set by the main window once it is attached to the Glass manager.</summary>
        public static bool MainAttached;
        /// <summary>True while the main window shows the Windows backdrop (Acrylic / Mica): surfaces painted with the key colour (pure black) then let the backdrop show through.</summary>
        public static bool SeeThrough => MainAttached && GlassManager.State is { Active: true } st && st.Backdrop != BackdropKind.None && ThemeManager.Current.IsDark;     // light themes keep their own light surfaces (dark text on a see-through backdrop is not reliably readable)
        private static UiPalette? _forRaw, _result;
        private static int _forVersion = -1;
        private static int _version;

        /// <summary>Called once at start: the machine tier comes from the performance governor, every Glass change re-colours the app.</summary>
        private static string? _lastBase;

        /// <summary>The Opsis theme mode that a base-theme choice of the Theme page stands for (null = unknown key).</summary>
        internal static string? ModeForBase(string? baseKey) => baseKey?.ToLowerInvariant() switch
        {
            "light" => "Light", "dark" => "Dark", "midnight" => "Midnight", "forest" => "Forest", "dawn" => "Dawn", "contrast" => "Contrast", _ => null
        };

        /// <summary>The Theme page stores the chosen base theme in the Glass settings, but Opsis' palette comes from ThemeManager: when the choice CHANGES, ThemeManager follows (live, saved). Returns true when it switched.</summary>
        private static bool SyncBaseTheme()
        {
            var b = GlassManager.Settings.BaseTheme;
            if (string.Equals(b, _lastBase, StringComparison.OrdinalIgnoreCase)) return false;
            _lastBase = b;
            var mode = ModeForBase(b);
            if (mode == null || string.Equals(MotionDesk.Services.AppSettings.Load().ThemeMode, mode, StringComparison.Ordinal)) return false;
            ThemeManager.SetMode(mode);                                   // saves, rebuilds the palette and raises Changed
            return true;
        }

        public static void Install(GlassSettings saved)
        {
            _lastBase = saved.BaseTheme;                                  // what is stored now is not a change
            GlassManager.ManageTheme = false; // Opsis keeps its own live theme adapter (OpsisUiTokens)
            GlassEnvironment.MachineTierOverride = () => MotionDesk.Services.PerformanceGovernor.EffectiveTier switch
            {
                MotionDesk.Services.HardwareTier.High => GlassTier.High,
                MotionDesk.Services.HardwareTier.Balanced => GlassTier.Medium,
                _ => GlassTier.Low
            };
            GlassManager.Changed += () => { _version++; if (!SyncBaseTheme()) ThemeManager.NotifyChanged(); };
            GlassManager.Apply(saved);
        }

        public static UiPalette Apply(UiPalette raw)
        {
            var state = GlassManager.State;
            if (!state.Active) return raw;
            if (ReferenceEquals(raw, _forRaw) && _forVersion == _version && _result != null) return _result;

            var s = GlassManager.Settings;
            Color tint;
            try { tint = ColorTranslator.FromHtml(s.TintColor); } catch (Exception) { tint = raw.AccentCyan; }
            var k = state.Intensity;
            Color Mix(Color a, double t) => Color.FromArgb(
                (int)(a.R + (tint.R - a.R) * t), (int)(a.G + (tint.G - a.G) * t), (int)(a.B + (tint.B - a.B) * t));
            var accent = raw.AccentCyan;
            if (!string.IsNullOrWhiteSpace(s.AccentColor)) { try { accent = ColorTranslator.FromHtml(s.AccentColor); } catch (Exception) { } }

            var p = new UiPalette
            {
                Background = Mix(raw.Background, 0.22 * k),
                Sidebar = Mix(raw.Sidebar, 0.30 * k),
                Surface = Mix(raw.Surface, 0.34 * k),
                SurfaceHover = Mix(raw.SurfaceHover, 0.40 * k),
                Border = Mix(raw.Border, 0.30 * k),
                TextPrimary = raw.TextPrimary,
                TextSecondary = raw.TextSecondary,
                TextMuted = raw.TextMuted,
                AccentCyan = accent,
                AccentBlue = raw.AccentBlue,
                IsDark = raw.IsDark
            };
            // text stays readable on the tinted surfaces (WCAG 4.5:1 for the primary and secondary text)
            p.TextPrimary = Readable(raw.TextPrimary, p.Surface, p.Background, 7.0);
            p.TextSecondary = Readable(raw.TextSecondary, p.Surface, p.Background, 4.5);
            p.TextMuted = Readable(raw.TextMuted, p.Surface, p.Background, 3.0);
            _forRaw = raw; _forVersion = _version; _result = p;
            return p;
        }

        private static Color Readable(Color text, Color a, Color b, double min)
        {
            double Worst(Color c) => Math.Min(Contrast.Ratio(c, a), Contrast.Ratio(c, b));
            if (Worst(text) >= min) return text;
            var target = Contrast.Ratio(Color.White, a) + Contrast.Ratio(Color.White, b) >= Contrast.Ratio(Color.Black, a) + Contrast.Ratio(Color.Black, b) ? Color.White : Color.Black;
            for (var i = 1; i <= 10; i++)
            {
                var c = Color.FromArgb((int)(text.R + (target.R - text.R) * i / 10.0), (int)(text.G + (target.G - text.G) * i / 10.0), (int)(text.B + (target.B - text.B) * i / 10.0));
                if (Worst(c) >= min) return c;
            }
            return target;
        }
    }
}

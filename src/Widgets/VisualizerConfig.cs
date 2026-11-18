using System;
using System.Collections.Generic;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// Settings of the "Music visualizer" wallpaper element (ray spectrum + clock/date), fed by the existing AudioSpectrumService.
    /// Opsis's own design: every value is clamped here, so the page and the settings UI never see an out-of-range number.
    /// See docs/VISUALIZER-FEATURES.md for the mapping from the feature list of the Lively wallpaper that inspired the feature set.
    /// </summary>
    public sealed class VisualizerConfig
    {
        public bool Enabled { get; set; } = false;               // the whole element (off by default: no audio capture, no rendering)
        // colours
        public bool UseBackgroundColor { get; set; } = false;
        public string BackgroundColor { get; set; } = "#000000";
        public bool UseBackgroundImage { get; set; } = false;
        public string BackgroundImagePath { get; set; } = "";    // .jpg/.png/.webp or .webm/.mp4 picked by the user
        public bool CustomGradient { get; set; } = false;       // false = rainbow
        public string Color1 { get; set; } = "#FFA903";
        public string Color2 { get; set; } = "#9900FF";
        public bool WhiteInside { get; set; } = false;
        // rays
        public bool ShowRays { get; set; } = true;
        public string Layout { get; set; } = "Radial";           // Radial | Linear
        public bool CleanLineEnd { get; set; } = false;
        public bool Interpolate { get; set; } = false;           // more rays
        public bool Rounded { get; set; } = true;
        public bool HideWhenSilent { get; set; } = false;
        public bool Reverse { get; set; } = false;
        public int MinLevel { get; set; } = 0;                   // 0..1000 (per mille)
        public int LineWidth { get; set; } = 25;                 // 0..100
        public int Intensity { get; set; } = 60;                 // 0..99
        public int AddLevel { get; set; } = 45;                  // 0..500 (per mille)
        public int RayAngle { get; set; } = 40;                  // 0..100
        public int TrebleBoost { get; set; } = 0;                // 0..100
        public int PosX { get; set; } = 50;                      // % of the screen width
        public int PosY { get; set; } = 52;                      // % of the screen height, measured from the bottom
        public int IdleSeed { get; set; } = 1;                   // "regenerate" button: changes the pattern shown while nothing plays
        // time
        public string Font { get; set; } = "Segoe UI";
        public bool Bold { get; set; } = true;
        public bool ShowClock { get; set; } = true;
        public string ClockColor { get; set; } = "#FFFFFF";
        public int ClockSize { get; set; } = 10;                 // 0..100 (% of screen height)
        public int ClockX { get; set; } = 51;
        public int ClockY { get; set; } = 54;
        public bool ShowSeconds { get; set; } = false;
        public bool Hour12 { get; set; } = false;
        public bool ShowDate { get; set; } = true;
        public string DateColor { get; set; } = "#FFFFFF";
        public int DateSize { get; set; } = 3;
        public int DateX { get; set; } = 51;
        public int DateY { get; set; } = 34;
        public bool ShowDayName { get; set; } = true;
        public bool ShowDayNumber { get; set; } = true;
        public bool ShowYear { get; set; } = false;
        public bool NumericDate { get; set; } = false;           // MM/DD/YYYY instead of "18 November"
        // shadow / glow
        public bool Shadow { get; set; } = true;
        public int ShadowIntensity { get; set; } = 30;           // 0..500
        public bool ShadowFollowsMusic { get; set; } = true;

        public static string SafeColor(string? c, string fallback)
        {
            if (string.IsNullOrWhiteSpace(c)) return fallback;
            c = c.Trim();
            if (c.Length is 7 or 4 && c[0] == '#')
            {
                for (int i = 1; i < c.Length; i++) if (!Uri.IsHexDigit(c[i])) return fallback;
                return c;
            }
            return fallback;
        }

        public static string SafeFont(string? f)
        {
            if (string.IsNullOrWhiteSpace(f)) return "Segoe UI";
            var chars = new List<char>();
            foreach (char ch in f.Trim()) if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' or '(' or ')') chars.Add(ch);
            string s = new string(chars.ToArray());
            return s.Length == 0 ? "Segoe UI" : s.Length > 60 ? s[..60] : s;
        }

        private static int C(int v, int lo, int hi) => Math.Clamp(v, lo, hi);

        /// <summary>Clamped, camelCase snapshot sent to the wallpaper page (visualizer.js).</summary>
        public Dictionary<string, object> ToPageObject(string bgImageUri = "")
        {
            return new Dictionary<string, object>
            {
                ["enabled"] = Enabled,
                ["useBg"] = UseBackgroundColor, ["bg"] = SafeColor(BackgroundColor, "#000000"),
                ["useBgImage"] = UseBackgroundImage && bgImageUri.Length > 0, ["bgImage"] = bgImageUri,
                ["gradient"] = CustomGradient, ["c1"] = SafeColor(Color1, "#FFA903"), ["c2"] = SafeColor(Color2, "#9900FF"),
                ["whiteInside"] = WhiteInside,
                ["showRays"] = ShowRays, ["layout"] = Layout == "Linear" ? "Linear" : "Radial",
                ["cleanEnd"] = CleanLineEnd, ["interpolate"] = Interpolate, ["rounded"] = Rounded,
                ["hideSilent"] = HideWhenSilent, ["reverse"] = Reverse,
                ["minLevel"] = C(MinLevel, 0, 1000), ["lineWidth"] = C(LineWidth, 0, 100), ["intensity"] = C(Intensity, 0, 99),
                ["addLevel"] = C(AddLevel, 0, 500), ["rayAngle"] = C(RayAngle, 0, 100), ["treble"] = C(TrebleBoost, 0, 100),
                ["x"] = C(PosX, 0, 100), ["y"] = C(PosY, 0, 100), ["seed"] = IdleSeed,
                ["font"] = SafeFont(Font), ["bold"] = Bold,
                ["showClock"] = ShowClock, ["clockColor"] = SafeColor(ClockColor, "#FFFFFF"), ["clockSize"] = C(ClockSize, 0, 100),
                ["clockX"] = C(ClockX, 0, 100), ["clockY"] = C(ClockY, 0, 100), ["seconds"] = ShowSeconds, ["h12"] = Hour12,
                ["showDate"] = ShowDate, ["dateColor"] = SafeColor(DateColor, "#FFFFFF"), ["dateSize"] = C(DateSize, 0, 100),
                ["dateX"] = C(DateX, 0, 100), ["dateY"] = C(DateY, 0, 100),
                ["dayName"] = ShowDayName, ["dayNumber"] = ShowDayNumber, ["year"] = ShowYear, ["numericDate"] = NumericDate,
                ["shadow"] = Shadow, ["shadowIntensity"] = C(ShadowIntensity, 0, 500), ["shadowMusic"] = ShadowFollowsMusic,
            };
        }
    }
}

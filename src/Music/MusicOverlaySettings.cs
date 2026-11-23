namespace OpsisMusicOverlay;

internal enum VisualPreset
{
    Radial, MirrorBars, AlbumArt, Bars, Oscilloscope, DualWave, Particles, Starfield, Tunnel,
    Spiral, BatteryBars, PlasmaBlobs, Fireworks, Ribbon, Kaleidoscope, Rain, PulseRings,
    GridWave, Vortex, Sunburst, Bubbles
}

internal static class VisualPresets
{
    public static readonly (VisualPreset Preset, string Title, string Mode)[] All =
    [
        (VisualPreset.AlbumArt, "Album Art", "none"), (VisualPreset.Bars, "Bars", "bars"),
        (VisualPreset.Oscilloscope, "Oscilloscope", "wave"), (VisualPreset.Radial, "Radial Spectrum", "circular"),
        (VisualPreset.MirrorBars, "Mirror Bars", "mirrorBars"), (VisualPreset.DualWave, "Dual Wave", "dualWave"),
        (VisualPreset.Particles, "Particles", "particles"), (VisualPreset.Starfield, "Starfield", "starfield"),
        (VisualPreset.Tunnel, "Tunnel", "tunnel"), (VisualPreset.Spiral, "Spiral", "spiral"),
        (VisualPreset.BatteryBars, "Battery Bars", "battery"), (VisualPreset.PlasmaBlobs, "Plasma Blobs", "plasma"),
        (VisualPreset.Fireworks, "Fireworks", "fireworks"), (VisualPreset.Ribbon, "Ribbon", "ribbon"),
        (VisualPreset.Kaleidoscope, "Kaleidoscope", "kaleidoscope"), (VisualPreset.Rain, "Rain", "rain"),
        (VisualPreset.PulseRings, "Pulse Rings", "pulseRings"), (VisualPreset.GridWave, "Grid Wave", "gridWave"),
        (VisualPreset.Vortex, "Vortex", "vortex"), (VisualPreset.Sunburst, "Sunburst", "sunburst"),
        (VisualPreset.Bubbles, "Bubbles", "bubbles")
    ];
    public static string ModeOf(VisualPreset preset) => All.FirstOrDefault(item => item.Preset == preset).Mode ?? "circular";
}

internal sealed record MusicOverlaySettings(
    string? Display = null,
    double X = 0.5,
    double Y = 0.5,
    VisualPreset Preset = VisualPreset.Radial,
    string? Typeface = null,
    bool Enabled = false,
    int WidthPercent = 100,
    int HeightPercent = 100,
    string[]? RadialColors = null,
    string[]? MirrorColors = null,
    int LayoutVersion = 0,
    Dictionary<VisualPreset, string[]>? PresetColors = null)
{
    public const int MinSize = 50;
    public const int MaxSize = 300;
    private static readonly string[] DefaultColors = ["#87eaff", "#a896ff", "#ff8bc9", "#ffe7ac"];

    public MusicOverlaySettings Normalize()
    {
        var useCenter = LayoutVersion == 0 && Display == null && X == 0.96 && Y == 0.87;
        return this with
        {
            X = useCenter ? 0.5 : Math.Clamp(X, 0, 1),
            Y = useCenter ? 0.5 : Math.Clamp(Y, 0, 1),
            WidthPercent = Math.Clamp(WidthPercent, MinSize, MaxSize),
            HeightPercent = Math.Clamp(HeightPercent, MinSize, MaxSize),
            RadialColors = NormalizePalette(RadialColors),
            MirrorColors = NormalizePalette(MirrorColors),
            Preset = Enum.IsDefined(Preset) ? Preset : VisualPreset.Radial,
            PresetColors = PresetColors?.Where(pair => Enum.IsDefined(pair.Key) && NormalizePalette(pair.Value) != null)
                .ToDictionary(pair => pair.Key, pair => NormalizePalette(pair.Value)!),
            LayoutVersion = 1
        };
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string[] Palette => (Preset switch
    {
        VisualPreset.Radial => RadialColors,
        VisualPreset.MirrorBars => MirrorColors,
        _ => PresetColors?.GetValueOrDefault(Preset)
    }) ?? DefaultColors;

    public MusicOverlaySettings WithPalette(string[]? colors)
    {
        var normalized = NormalizePalette(colors);
        if (Preset == VisualPreset.Radial) return this with { RadialColors = normalized };
        if (Preset == VisualPreset.MirrorBars) return this with { MirrorColors = normalized };
        var palettes = PresetColors == null ? new Dictionary<VisualPreset, string[]>() : new Dictionary<VisualPreset, string[]>(PresetColors);
        if (normalized == null) palettes.Remove(Preset);
        else palettes[Preset] = normalized;
        return this with { PresetColors = palettes };
    }

    public Color PaletteColor(int index) => ColorTranslator.FromHtml(Palette[index]);

    private static string[]? NormalizePalette(string[]? colors)
    {
        if (colors == null || colors.Length != DefaultColors.Length) return null;
        return colors.Select((color, index) =>
            color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit)
                ? color.ToLowerInvariant()
                : DefaultColors[index]).ToArray();
    }
}

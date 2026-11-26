using System.Globalization;
using System.Text.Json;
using MotionDesk.UI;

namespace MotionDesk.Services;

public enum DesktopSurface { Containers, DeskStrip, Widgets }

public sealed record DesktopMaterial(string Mode = "Default", string Tint = "#1E2128", int Opacity = 32,
    int Blur = 8, int Refraction = 85, int Rim = 70, int Radius = 18,
    string? TitleFont = null, string? ItemFont = null, int ItemFontSize = 0,
    string? TitleColor = null, string? ItemColor = null, string? AccentColor = null, bool RadiusSet = false)
{
    internal bool UsesCustom => Mode != "Default";
    /// <summary>The corner radius to draw: the chosen one for a custom material, or in the Default look when the person set the radius on its own (RadiusSet); otherwise the built-in value.</summary>
    internal int EffectiveRadius(int builtIn) => UsesCustom || RadiusSet ? Radius : builtIn;
    internal bool WantsGlass => Mode is "Clear" or "Frosted" or "Custom";
    internal Color Color => ColorTranslator.FromHtml(Tint);

    internal DesktopMaterial Normalized() => this with
    {
        Mode = Mode is "Clear" or "Frosted" or "Solid" or "Custom" ? Mode : "Default",
        Tint = TryTint(Tint, out var tint, out _) ? tint : "#1E2128",
        Opacity = Math.Clamp(Opacity, 0, 100), Blur = Math.Clamp(Blur, 0, 60),
        Refraction = Math.Clamp(Refraction, 0, 200), Rim = Math.Clamp(Rim, 0, 300),
        Radius = Math.Clamp(Radius, 0, 32),
        ItemFontSize = ItemFontSize == 0 ? 0 : Math.Clamp(ItemFontSize, 6, 24),
        TitleColor = SafeColor(TitleColor), ItemColor = SafeColor(ItemColor), AccentColor = SafeColor(AccentColor)
    };

    internal static DesktopMaterial Preset(string mode) => mode switch
    {
        "Clear" => new(mode, Opacity: 16, Blur: 2, Refraction: 100, Radius: 16),
        "Frosted" => new(mode, Opacity: 38, Blur: 26, Refraction: 40, Radius: 22),
        "Solid" => new(mode, Opacity: 100, Blur: 0, Refraction: 0, Rim: 20, Radius: 8),
        _ => new()
    };

    internal static DesktopMaterial ImportTheme(string path, bool dark)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 256 * 1024) throw new InvalidDataException("Theme JSON exceeds 256 KB.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("base", out var basis)
            || basis.ValueKind != JsonValueKind.String || basis.GetString() is not ("liquidGlass" or "fluent"))
            throw new InvalidDataException("Expected a theme with base fluent or liquidGlass.");

        var material = Preset(basis.GetString() == "liquidGlass" ? "Clear" : "Frosted");
        material = Apply(root, material);
        if (root.TryGetProperty(dark ? "dark" : "light", out var colorMode) && colorMode.ValueKind == JsonValueKind.Object)
            material = Apply(colorMode, material);
        return (material with { Mode = "Custom" }).Normalized();

        static DesktopMaterial Apply(JsonElement obj, DesktopMaterial current)
        {
            if (obj.TryGetProperty("tint", out var candidate) && candidate.ValueKind == JsonValueKind.String
                && TryTint(candidate.GetString(), out var tint, out var opacity))
                current = current with { Tint = tint, Opacity = opacity ?? current.Opacity };
            return current with
            {
                Blur = Read(obj, "blur", current.Blur, 1, 60),
                Refraction = Read(obj, "refraction", current.Refraction, 100, 200),
                Rim = Read(obj, "rimLight", current.Rim, 100, 300),
                Radius = Read(obj, "cornerRadius", current.Radius, 1, 32),
                TitleFont = ReadFont(obj, "titleFont", current.TitleFont),
                ItemFont = ReadFont(obj, "labelFont", current.ItemFont),
                ItemFontSize = Read(obj, "labelFontSize", current.ItemFontSize, .75, 24),
                TitleColor = ReadColor(obj, "titleColor", current.TitleColor),
                ItemColor = ReadColor(obj, "textPrimary", current.ItemColor),
                AccentColor = ReadColor(obj, "accent", current.AccentColor)
            };
        }

        static string? ReadFont(JsonElement obj, string key, string? fallback) =>
            obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: > 0 and <= 128 } font ? font : fallback;

        static string? ReadColor(JsonElement obj, string key, string? fallback) =>
            obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? SafeColor(value.GetString()) ?? fallback : fallback;

        static int Read(JsonElement obj, string key, int fallback, double multiplier, int max) =>
            obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var parsed) && double.IsFinite(parsed)
                ? (int)Math.Round(Math.Clamp(parsed * multiplier, 0, max)) : fallback;
    }

    private static string? SafeColor(string? value) => TryTint(value, out var color, out _) ? color : null;

    internal static Color OptionalColor(string? value, Color fallback) =>
        SafeColor(value) is { } color ? ColorTranslator.FromHtml(color) : fallback;

    private static bool TryTint(string? value, out string tint, out int? opacity)
    {
        tint = ""; opacity = null;
        if (value is not { Length: 7 or 9 } || value[0] != '#' || !value.AsSpan(1).ToString().All(Uri.IsHexDigit)) return false;
        tint = value[..7];
        if (value.Length == 9)
            opacity = (int)Math.Round(byte.Parse(value.AsSpan(7), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * 100d / 255);
        return true;
    }
}

internal static class DesktopMaterials
{
    internal static DesktopMaterial Get(DesktopSurface surface)
    {
        var settings = AppSettings.Peek();
        var material = surface switch
        {
            DesktopSurface.Containers => settings.ContainerMaterial,
            DesktopSurface.DeskStrip => settings.DeskStripMaterial,
            _ => settings.WidgetMaterial
        };
        return (material ?? new()).Normalized();
    }

    // One explicit user action, one settings save; reuse existing GPU/DWM policy.
    internal static void ApplySignature()
    {
        var settings = AppSettings.Load();
        var material = new DesktopMaterial("Custom", "#153451", 30, 14, 85, 90, 20,
            TitleColor: "#F5FAFF", ItemColor: "#F5FAFF", AccentColor: "#7EDBFF").Normalized();
        settings.ContainerMaterial = material;
        settings.DeskStripMaterial = material with { Radius = 24 };
        settings.WidgetMaterial = material;
        settings.WidgetsThemeMode = "Transparent";
        settings.WidgetsTintPercent = 0;
        settings.WidgetsTintColor = "Material";
        // Do not override an explicit GPU disable or the existing accessibility policy.
        settings.ThemeMode = "SignatureGlass";
        settings.Save();
        ThemeManager.RefreshOpsisAccent();
    }

    internal static void Set(DesktopSurface surface, DesktopMaterial material)
    {
        var settings = AppSettings.Load();
        material = material.Normalized();
        switch (surface)
        {
            case DesktopSurface.Containers: settings.ContainerMaterial = material; break;
            case DesktopSurface.DeskStrip: settings.DeskStripMaterial = material; break;
            case DesktopSurface.Widgets:
                settings.WidgetMaterial = material;
                if (material.WantsGlass) { settings.WidgetsThemeMode = "Transparent"; settings.WidgetsTintPercent = 0; }
                else if (material.Mode == "Solid") { settings.WidgetsThemeMode = "Transparent"; settings.WidgetsTintPercent = 100; settings.WidgetsTintColor = "Material"; }
                break;
        }
        settings.Save();
        ThemeManager.RaiseRepainted();
    }
}

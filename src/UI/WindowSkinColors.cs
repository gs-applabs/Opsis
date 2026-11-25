using System.Drawing;

namespace MotionDesk.UI;

internal readonly record struct WindowSkinColors(Color Background, Color Navigation, Color Surface, Color Hover, Color Border,
    Color Text, Color Secondary, Color Muted, Color Accent)
{
    internal static WindowSkinColors? Default(WindowSkin skin, bool dark) => (skin, dark) switch
    {
        (WindowSkin.Atlas, false) => Create(0xF8F2EC, 0xF3ECE7, 0xFFFAF7, 0xE7DFF6, 0xD8CCD5, 0x231E3B, 0x514361, 0x675778, 0x614AB1),
        (WindowSkin.Atlas, true)  => Create(0x1C1823, 0x231D2C, 0x2C2437, 0x3F3254, 0x594965, 0xFCF4FF, 0xDFCEE7, 0xB8A7C6, 0xBDAAFF),
        (WindowSkin.Inspector, false) => Create(0xEEF2F5, 0xE2E9EE, 0xF8FAFB, 0xF3DFD9, 0xC4CED7, 0x212C35, 0x455561, 0x506270, 0xA6432E),
        (WindowSkin.Inspector, true)  => Create(0x1A2630, 0x18232C, 0x22313E, 0x354453, 0x495B69, 0xF4F7FA, 0xCBD7DF, 0xA7BAC7, 0xF39A83),
        (WindowSkin.Index, false) => Create(0xEDF3EF, 0xE3EDE8, 0xF7FBF9, 0xD7EBE2, 0xBFCFC8, 0x172722, 0x3F5750, 0x506660, 0x246E59),
        (WindowSkin.Index, true)  => Create(0x192226, 0x20292E, 0x252F34, 0x31504A, 0x46595B, 0xF4F9F7, 0xCEDBD9, 0xA5B9B6, 0x85C7B2),
        (WindowSkin.Split, false) => Create(0xEAF2F9, 0xDEEAF5, 0xF7FBFF, 0xD5EBFC, 0xB8CDD9, 0x152638, 0x425A70, 0x4C6478, 0x00668C),
        (WindowSkin.Split, true)  => Create(0x101C2B, 0x152233, 0x192A3D, 0x28405A, 0x3B5570, 0xEDF7FF, 0xC1D8EA, 0x9EB9D0, 0x82D0F7),
        _ => null
    };

    private static WindowSkinColors Create(int background, int navigation, int surface, int hover, int border, int text, int secondary, int muted, int accent) =>
        new(Rgb(background), Rgb(navigation), Rgb(surface), Rgb(hover), Rgb(border), Rgb(text), Rgb(secondary), Rgb(muted), Rgb(accent));

    private static Color Rgb(int value) => Color.FromArgb(unchecked((int)0xff000000) | value);

    internal WindowSkinColors ShiftSurfaces(int value)
    {
        Color Shift(Color color) => Color.FromArgb(Math.Clamp(color.R + value, 0, 255), Math.Clamp(color.G + value, 0, 255), Math.Clamp(color.B + value, 0, 255));
        return this with { Background = Shift(Background), Navigation = Shift(Navigation), Surface = Shift(Surface), Hover = Shift(Hover), Border = Shift(Border) };
    }
}

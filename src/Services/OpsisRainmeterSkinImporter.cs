using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MotionDesk.Services;

/// <summary>
/// A strictly limited, DATA-ONLY compatibility importer for simple Rainmeter INI
/// String skins. No Rainmeter binary, Lua, plugin measure, dynamic variables,
/// !Bang commands, external network or filesystem actions are ever executed.
/// Unsupported active elements are rejected rather than silently simulated.
/// </summary>
public static class OpsisRainmeterSkinImporter
{
    public sealed record Preview(string Name, string Binding, string Text, string AccentHex,
        int FontSize, int Width, int Height);

    private static readonly HashSet<string> HeaderSections = new(StringComparer.OrdinalIgnoreCase)
    { "Rainmeter", "Variables", "Metadata" };
    private static readonly HashSet<string> HeaderFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Update", "AccurateText", "DynamicWindowSize", "BackgroundMode",
        "SolidColor", "Author", "Version", "Information", "License",
        "Name", "X", "Y", "W", "H"
    };

    private static string SafeName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) return "Imported Skin";
        var result = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-'
            ? ch : '_').Take(55).ToArray()).Trim();
        return result.Length == 0 ? "Imported Skin" : result;
    }

    private static Dictionary<string, Dictionary<string, string>> Parse(string ini)
    {
        if (Encoding.UTF8.GetByteCount(ini) > 64 * 1024)
            throw new InvalidDataException("Skin exceeds safe size quota.");
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
            {
                var key = line[1..^1];
                if (key.Length > 90 || sections.ContainsKey(key))
                    throw new InvalidDataException("Duplicate or oversized INI section.");
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections.Add(key, current);
                continue;
            }
            var index = line.IndexOf('=');
            if (current == null || index < 1)
                throw new InvalidDataException("Unsupported or malformed INI line.");
            var name = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (name.Length is < 1 or > 90 || value.Length > 1000 ||
                current.ContainsKey(name))
                throw new InvalidDataException("Unsafe or duplicate INI property.");
            current[name] = value;
        }
        if (sections.Count > 20) throw new InvalidDataException("Too many INI sections.");
        return sections;
    }

    private static int Number(string value, int fallback, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ||
            n < min || n > max)
            throw new InvalidDataException("Unsupported numeric Rainmeter option.");
        return n;
    }

    private static string Color(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "#00D2FF";
        var values = raw.Split(',');
        if (values.Length is < 3 or > 4) throw new InvalidDataException("Unsupported Rainmeter color.");
        var rgb = values.Take(3).Select(v => Number(v, -1, 0, 255)).ToArray();
        if (values.Length == 4) Number(values[3], 255, 0, 255);
        return $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
    }

    public static Preview PreviewIni(string text, string importedName)
    {
        var sections = Parse(text);
        if (sections.Keys.Any(s => s.Contains("Script", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Plugin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Executable/plugin skin components are unsupported.");
        // Do not attempt to execute Rainmeter expressions/variables/bangs.
        if (text.Contains("[#", StringComparison.Ordinal) ||
            text.Contains("[!", StringComparison.Ordinal) ||
            text.Contains("!Execute", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dynamic expressions or commands are unsupported.");

        var meters = sections.Where(pair => pair.Value.TryGetValue("Meter", out var meter) &&
            meter.Equals("String", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (meters.Length != 1)
            throw new InvalidDataException("This importer supports exactly one String meter.");
        if (sections.Any(pair => pair.Value.TryGetValue("Meter", out var meter) &&
            !meter.Equals("String", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Other meter types must be converted manually.");
        var meterOptions = meters[0].Value;
        string Read(string key, string fallback = "") =>
            meterOptions.TryGetValue(key, out var value) ? value : fallback;
        var binding = "Text";
        var display = Read("Text");
        if (Read("MeasureName") is { Length: > 0 } measure)
        {
            if (!sections.TryGetValue(measure, out var options) ||
                !options.TryGetValue("Measure", out var type))
                throw new InvalidDataException("Missing or unsupported built-in measure.");
            var format = options.TryGetValue("Format", out var f) ? f : "";
            if (type.Equals("Time", StringComparison.OrdinalIgnoreCase))
                binding = format is "" or "%H:%M" or "%H:%M:%S" ? "Clock"
                    : format is "%d/%m/%Y" or "%A, %d %B %Y" ? "Date"
                    : throw new InvalidDataException("Unsupported Rainmeter Time format.");
            else if (type.Equals("CPU", StringComparison.OrdinalIgnoreCase) && format == "" &&
                (!options.TryGetValue("Processor", out var processor) || processor == "0"))
                binding = "CPU";
            else if (type.Equals("PhysicalMemory", StringComparison.OrdinalIgnoreCase) && format == "" &&
                (!options.TryGetValue("Total", out var total) || total == "0"))
                binding = "RAM";
            else throw new InvalidDataException("Only built-in Time, total CPU and used physical-memory measures are supported.");
        }

        // No active components other than one meter and one allowlisted built-in measure.
        foreach (var (section, opts) in sections)
        {
            if (section.Equals(meters[0].Key, StringComparison.OrdinalIgnoreCase))
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Meter","MeasureName","Text","FontColor","FontSize","AntiAlias",
                    "StringAlign","StringStyle","X","Y","W","H","SolidColor","Hidden"
                };
                if (opts.Keys.Any(k => !allowed.Contains(k)))
                    throw new InvalidDataException("Unsupported String meter property.");
            }
            else if (HeaderSections.Contains(section))
            {
                if (section.Equals("Variables", StringComparison.OrdinalIgnoreCase) && opts.Count > 0)
                    throw new InvalidDataException("Skin variables are not evaluated.");
                if (opts.Keys.Any(k => !HeaderFields.Contains(k)))
                    throw new InvalidDataException("Unsupported global Rainmeter property.");
            }
            else if (section.Equals(Read("MeasureName"), StringComparison.OrdinalIgnoreCase))
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Measure", "UpdateDivider", "DynamicVariables" };
                if (binding is "Clock" or "Date") allowed.Add("Format");
                if (binding == "CPU") allowed.Add("Processor");
                if (binding == "RAM") allowed.Add("Total");
                if (opts.Keys.Any(k => !allowed.Contains(k)))
                    throw new InvalidDataException("Unsupported measure option.");
                if (opts.TryGetValue("DynamicVariables", out var flag) && flag != "0")
                    throw new InvalidDataException("Dynamic measures cannot be imported.");
            }
            else throw new InvalidDataException("Unknown active Rainmeter skin section.");
        }
        if (binding == "Text" && string.IsNullOrWhiteSpace(display))
            throw new InvalidDataException("Static String skin has no content.");
        var font = Number(Read("FontSize"), 30, 12, 80);
        var w = Number(Read("W"), 330, 190, 1200);
        var h = Number(Read("H"), 135, 80, 700);
        var accent = Color(Read("FontColor"));
        return new Preview(importedName, binding, display, accent, font, w, h);
    }

    public static Preview PreviewFile(string filename)
    {
        if (!Path.GetExtension(filename).Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
            new FileInfo(filename).Length > 64 * 1024)
            throw new InvalidDataException("Expected a Rainmeter .ini file under 64 KiB.");
        return PreviewIni(File.ReadAllText(filename), SafeName(filename));
    }

    public static string Import(string filename, string? nameOverride = null)
    {
        var preview = PreviewFile(filename);
        var name = nameOverride ?? preview.Name;
        var layout = new OpsisWidgetDesigner.Layout
        {
            Name = name, Binding = preview.Binding, Text = preview.Text,
            AccentHex = preview.AccentHex, FontSize = preview.FontSize,
            Width = preview.Width, Height = preview.Height
        };
        OpsisWidgetDesigner.Save(layout);
        return layout.Name;
    }
}

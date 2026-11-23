namespace MotionDesk.Services;

/// <summary>Opt-in application/document type to a zero-based DeskZones slot.</summary>
public sealed class WindowZoneRule
{
    public string Process { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public int ZoneIndex { get; set; }
}

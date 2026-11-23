namespace MotionDesk.Services
{
    /// <summary>How the GPU glass of widgets / DeskStrip / containers is shown right now.</summary>
    internal enum GlassLevel { Full, Frosted, Blur, Solid }

    /// <summary>User choice: Auto (default, decided from the computer), Always (glass whenever it is safe), Blur (simple blur only), Solid.</summary>
    internal enum GpuGlassMode { Auto, Always, Blur, Solid }

    internal sealed record GlassInputs(long RamMb, int PhysicalCores, string[] Adapters, bool OnBattery, int WindowsBuild, bool SafeMode, bool TransparencyOn, bool HighContrast,
        bool Remote, bool Locked, bool Fullscreen, bool BatterySaver, bool GpuFailed, HardwareTier EffectiveTier);

    /// <summary>
    /// The decision "which glass level can this computer and this moment carry" as a pure function (tested). The live part (CPU / GPU / RAM pressure for N seconds, recovery with
    /// hysteresis) is already done by <see cref="PerformanceGovernor"/>: its effective tier goes down after 10 s of pressure and back up after 45 s of calm, and this policy follows it.
    /// Documented minimum / maximum specs: docs/GLASS-ADAPTIVE.md.
    /// </summary>
    internal static class GlassPolicy
    {
        public static bool IsAmd(string[] adapters) => adapters.Any(a => a.Contains("AMD", StringComparison.OrdinalIgnoreCase) || a.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || a.Contains("ATI ", StringComparison.OrdinalIgnoreCase));
        public static bool HasDiscrete(string[] adapters) => adapters.Any(n =>
            n.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || n.Contains("RTX", StringComparison.OrdinalIgnoreCase) || n.Contains("GTX", StringComparison.OrdinalIgnoreCase) || n.Contains("Quadro", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Arc ", StringComparison.OrdinalIgnoreCase) || (n.Contains("Radeon", StringComparison.OrdinalIgnoreCase) && (n.Contains(" RX", StringComparison.OrdinalIgnoreCase) || n.Contains("PRO", StringComparison.OrdinalIgnoreCase))));

        public static GpuGlassMode ParseMode(string? value, bool legacyUseGpuGlass) =>
            Enum.TryParse<GpuGlassMode>(value, true, out var m) ? m : legacyUseGpuGlass ? GpuGlassMode.Always : GpuGlassMode.Auto;     // an old explicit "on" keeps its choice

        public static (GlassLevel Level, string Reason) Decide(GpuGlassMode mode, GlassInputs i)
        {
            if (mode == GpuGlassMode.Solid) return (GlassLevel.Solid, "you chose Solid");
            // never glass, whatever the user chose
            if (i.WindowsBuild < 22000) return (GlassLevel.Solid, "needs Windows 11");
            if (i.SafeMode) return (GlassLevel.Solid, "Safe Mode");
            if (i.HighContrast) return (GlassLevel.Solid, "High Contrast is on");
            if (!i.TransparencyOn) return (GlassLevel.Solid, "Windows transparency effects are off");
            if (i.Locked) return (GlassLevel.Solid, "the session is locked");
            if (i.Remote) return (GlassLevel.Solid, "remote desktop session");
            if (i.GpuFailed) return (GlassLevel.Blur, "the GPU path failed (device reset), using the simple blur");
            if (mode == GpuGlassMode.Blur) return (GlassLevel.Blur, "you chose the simple blur");
            if (mode == GpuGlassMode.Always) return (GlassLevel.Full, "you chose Always");
            // Auto: from the machine and the moment
            if (i.Fullscreen) return (GlassLevel.Blur, "a full-screen app is running");
            if (i.BatterySaver) return (GlassLevel.Blur, "battery saver is on");
            var level = i.EffectiveTier switch { HardwareTier.High => GlassLevel.Full, HardwareTier.Balanced => GlassLevel.Frosted, _ => GlassLevel.Blur };
            var why = i.EffectiveTier switch { HardwareTier.High => "the computer can carry full glass", HardwareTier.Balanced => "medium performance tier", _ => "low performance tier (or the load is high right now)" };
            void Cap(GlassLevel max, string reason) { if (level < max) { level = max; why = reason; } }
            if (i.RamMb > 0 && i.RamMb < 4096) Cap(GlassLevel.Blur, "less than 4 GB of RAM");
            else if (i.RamMb > 0 && i.RamMb < 8192) Cap(GlassLevel.Frosted, "less than 8 GB of RAM");
            if (i.PhysicalCores > 0 && i.PhysicalCores < 2) Cap(GlassLevel.Blur, "a single CPU core");
            else if (i.PhysicalCores > 0 && i.PhysicalCores < 4) Cap(GlassLevel.Frosted, "fewer than 4 CPU cores");
            if (IsAmd(i.Adapters)) Cap(GlassLevel.Frosted, "AMD graphics: refraction is off by default (use Always to try it)");
            else if (!HasDiscrete(i.Adapters) && i.RamMb < 12288) Cap(GlassLevel.Frosted, "integrated graphics with less than 12 GB of RAM");
            if (i.OnBattery) Cap(GlassLevel.Frosted, "running on battery");
            return (level, why);
        }

        public static string Describe(GlassLevel l, string reason) =>
            (l switch { GlassLevel.Full => "full GPU glass", GlassLevel.Frosted => "frosted glass", GlassLevel.Blur => "simple blur", _ => "solid" }) + " - " + reason;
    }
}

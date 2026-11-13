using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public class GlassPolicyTests
{
    private static GlassInputs Pc(HardwareTier tier = HardwareTier.High, long ram = 16384, int cores = 8, string gpu = "NVIDIA GeForce RTX 3060", bool battery = false, bool trans = true, bool hc = false, bool remote = false, bool locked = false, bool full = false, bool saver = false, bool failed = false, bool safe = false, int build = 26100) =>
        new(ram, cores, new[] { gpu }, battery, build, safe, trans, hc, remote, locked, full, saver, failed, tier);

    [Fact] public void StrongPcGetsFullGlassInAuto() => Assert.Equal(GlassLevel.Full, GlassPolicy.Decide(GpuGlassMode.Auto, Pc()).Level);
    [Fact] public void MediumTierGetsFrosted() => Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(HardwareTier.Balanced)).Level);
    [Fact] public void LowTierGetsSimpleBlur() => Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(HardwareTier.Low)).Level);
    [Fact] public void AmdIsCappedAtFrostedInAuto() => Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(gpu: "AMD Radeon RX 6600")).Level);
    [Fact] public void AlwaysOverridesTheAmdCapAndTheLoad() => Assert.Equal(GlassLevel.Full, GlassPolicy.Decide(GpuGlassMode.Always, Pc(HardwareTier.Low, gpu: "AMD Radeon RX 6600")).Level);
    [Fact] public void SmallRamAndFewCoresStepDown()
    {
        Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(ram: 6000)).Level);
        Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(ram: 3000)).Level);
        Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(cores: 2)).Level);
    }
    [Fact] public void IntegratedGraphicsNeedsMoreRamForFull() => Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(gpu: "Intel(R) UHD Graphics 630", ram: 8192)).Level);
    [Fact] public void BatteryCapsAtFrostedAndSaverGoesToBlur()
    {
        Assert.Equal(GlassLevel.Frosted, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(battery: true)).Level);
        Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(saver: true)).Level);
    }
    [Theory]
    [InlineData("build")] [InlineData("safe")] [InlineData("hc")] [InlineData("trans")] [InlineData("locked")] [InlineData("remote")]
    public void HardFallbacksWinOverEveryChoice(string what)
    {
        var i = what switch { "build" => Pc(build: 19045), "safe" => Pc(safe: true), "hc" => Pc(hc: true), "trans" => Pc(trans: false), "locked" => Pc(locked: true), _ => Pc(remote: true) };
        foreach (var m in new[] { GpuGlassMode.Auto, GpuGlassMode.Always }) Assert.Equal(GlassLevel.Solid, GlassPolicy.Decide(m, i).Level);
    }
    [Fact] public void GpuFailureFallsBackToBlurEvenWhenAlways() => Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Always, Pc(failed: true)).Level);
    [Fact] public void FullScreenAppDropsAutoToBlurButNotAlways()
    {
        Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Auto, Pc(full: true)).Level);
        Assert.Equal(GlassLevel.Full, GlassPolicy.Decide(GpuGlassMode.Always, Pc(full: true)).Level);
    }
    [Fact] public void UserChoicesBlurAndSolidAreHonoured()
    {
        Assert.Equal(GlassLevel.Blur, GlassPolicy.Decide(GpuGlassMode.Blur, Pc()).Level);
        Assert.Equal(GlassLevel.Solid, GlassPolicy.Decide(GpuGlassMode.Solid, Pc()).Level);
    }
    [Fact] public void OldExplicitOnKeepsItsChoiceAndNoValueMeansAuto()
    {
        Assert.Equal(GpuGlassMode.Always, GlassPolicy.ParseMode(null, true));
        Assert.Equal(GpuGlassMode.Auto, GlassPolicy.ParseMode(null, false));
        Assert.Equal(GpuGlassMode.Blur, GlassPolicy.ParseMode("Blur", true));
    }
    [Fact] public void ReasonIsNeverEmpty() => Assert.False(string.IsNullOrWhiteSpace(GlassPolicy.Decide(GpuGlassMode.Auto, Pc(HardwareTier.Low)).Reason));
}

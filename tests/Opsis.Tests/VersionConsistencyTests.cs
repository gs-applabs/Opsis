using System.Diagnostics;
using System.Text.RegularExpressions;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// One version everywhere: csproj, the bundled containers project, the installer, README badge, CHANGELOG, the version history and the running assembly.
public sealed class VersionConsistencyTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel));
    private static string Csproj(string rel, string tag) => Regex.Match(Read(rel), $"<{tag}>([^<]+)</{tag}>").Groups[1].Value;

    [Fact]
    public void AllPlacesAgreeOnTheVersion()
    {
        var v = Csproj("MotionDeskStudio.csproj", "Version");
        var pre = Regex.Match(Csproj("MotionDeskStudio.csproj", "InformationalVersion"), @"^[0-9.]+(?:-([A-Za-z0-9.]+))?$").Groups[1].Value;
        var label = pre.Length > 0 ? v + " " + pre : v;

        Assert.Equal(v, Csproj("containers/OpsisContainers.csproj", "Version"));
        Assert.StartsWith(v, Csproj("MotionDeskStudio.csproj", "AssemblyVersion"));
        Assert.StartsWith(v, Csproj("MotionDeskStudio.csproj", "FileVersion"));
        Assert.StartsWith(v, Csproj("containers/OpsisContainers.csproj", "FileVersion"));

        var iss = Read("installer.iss");
        Assert.Contains($"#define MyAppVersion \"{v}\"", iss);
        Assert.Contains($"#define MyAppLabel \"{label}\"", iss);
        Assert.Contains("AppVersion={#MyAppLabel}", iss);

        Assert.Contains("version-" + label.Replace(" ", "%20"), Read("README.md"));
        Assert.Matches(@"(?m)^## " + Regex.Escape(label), Read("CHANGELOG.md"));

        var asm = typeof(AppVersion).Assembly;
        Assert.Equal(v, AppVersion.Short);
        Assert.Equal(label, AppVersion.Label);
        Assert.Equal(label, AppVersion.Describe(asm.GetName().Version!, "2.6.0-beta+abcdef1"));
        var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
        Assert.StartsWith(v, fvi.FileVersion);
        Assert.DoesNotContain("+", fvi.ProductVersion);       // no commit hash in the product version
    }

    [Theory]
    [InlineData("2.6.0-beta", "2.6.0 beta")]
    [InlineData("2.6.0", "2.6.0")]
    [InlineData(null, "2.6.0")]
    [InlineData("2.6.0-rc.1+abc", "2.6.0 rc.1")]
    public void LabelFromInformationalVersion(string? info, string expected) => Assert.Equal(expected, AppVersion.Describe(new Version(2, 6, 0, 0), info));

    [Fact]
    public void FourthPartShownOnlyWhenNotZero() => Assert.Equal("2.4.6.1", AppVersion.Format(new Version(2, 4, 6, 1)));
}

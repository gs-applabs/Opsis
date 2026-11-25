using System.Reflection;

namespace MotionDesk.Services
{
    /// <summary>
    /// The ONE place the shown version comes from: the assembly version ("2.6.0") and, for pre-release builds, the label from InformationalVersion ("2.6.0-beta" -> "2.6.0 beta").
    /// Window / About / sidebar / tray / Discord / history all use <see cref="Label"/>, so a beta is shown as a beta everywhere and the number is never typed twice.
    /// </summary>
    public static class AppVersion
    {
        public static Version Number => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        /// <summary>"2.6.0" (three parts; the fourth is shown only when it is not zero, e.g. 2.4.6.1).</summary>
        public static string Short => Format(Number);

        /// <summary>"2.6.0 beta" for a pre-release build, otherwise the same as <see cref="Short"/>.</summary>
        public static string Label => Describe(Number, Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        public static bool IsPreRelease => Label != Short;

        // The installer reads the published EXE's FileVersion; Help and updater
        // deliberately derive their version from this same compiled assembly.
        // This metadata record makes it visible to release tooling without
        // duplicating a version literal in app pages or update requests.
        public static object ReleaseIdentity => new
        {
            app = Short,
            help = Short,
            update = Short,
            installer = Short,
            preview = IsPreRelease,
            label = Label
        };

        public static string Format(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

        public static string Describe(Version v, string? informational)
        {
            var s = Format(v);
            if (string.IsNullOrWhiteSpace(informational)) return s;
            var plus = informational.IndexOf('+'); if (plus >= 0) informational = informational[..plus];        // build metadata (commit) is never shown
            var dash = informational.IndexOf('-');
            if (dash < 0) return s;
            var pre = informational[(dash + 1)..].Trim();
            return pre.Length == 0 ? s : s + " " + pre;
        }
    }
}

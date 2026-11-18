using System;
using System.Collections.Generic;
using OpsisNewsCrawler.Plugin.Abstractions;

// A stub plugin compiled in three flavours (see the csproj files next to this folder):
//   StubOldPlugin     - built against the contract 0.8.0.0 (what every plugin of Opsis 2.4.x is)
//   StubNewLitePlugin - built against the contract 1.0.0.0 but uses none of the new types (loads in 2.4.x as well)
//   StubNewPlugin     - built against 1.0.0.0 and implements the two new optional interfaces
namespace Stub
{
#if NEW_FEATURES
    public sealed class StubPlugin : IOpsisNewsCrawlerPlugin, IPluginUpdateSource, IPluginHostSettingsAware
#else
    public sealed class StubPlugin : IOpsisNewsCrawlerPlugin
#endif
    {
        public string Id => "stub." + FlavourName;
        public string Name => "Stub " + FlavourName;
        public string Version => "1.2.3";
        public static string FlavourName =>
#if NEW_FEATURES
            "new";
#elif LITE
            "newlite";
#else
            "old";
#endif
        public string? LastSettings;
        public void Start() { } public void Stop() { } public void Pause() { } public void Resume() { } public void Refresh() { }
        public void SetMode(string mode) { } public void SetPosition(string position) { }
        public void SetTheme(bool dark, int background, int surface, int border, int text, int textMuted, int accent, double opacity) { }
        public void SetLanguage(string code) { }
        public PluginAboutInfo GetAboutInfo() => new(Name, Version, "", "", "test", "MIT", Array.Empty<PluginAboutSection>());
        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems() => Array.Empty<PluginMenuItem>();
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems() => Array.Empty<PluginNavigationItem>();
#pragma warning disable CS0067
        public event Action<string>? NavigationRequested;
#pragma warning restore CS0067
#if NEW_FEATURES
        public PluginUpdateSource GetUpdateSource() => new("gs-applabs/stub-plugin", "stub-Plugin.zip");
        public void SetHostSettings(PluginHostSettings settings) => LastSettings = settings.Language + "|" + settings.HardwareTier + "|" + settings.HostManagesUpdates;
#endif
    }
}

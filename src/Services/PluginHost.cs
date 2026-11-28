using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Win32;
using MotionDesk.UI;
using MotionDesk.Widgets;
using OpsisNewsCrawler.Plugin.Abstractions;

namespace MotionDesk.Services
{
    // Μηχανισμός plugin του Opsis - Desktop Customization.
    //
    // Ένα plugin είναι ένας φάκελος μέσα σε έναν από τους φακέλους "plugins" με ένα *Plugin.dll που περιέχει μια κλάση
    // που υλοποιεί IOpsisNewsCrawlerPlugin (το contract βρίσκεται στο libs\OpsisNewsCrawler.Plugin.Abstractions.dll).
    // Φάκελοι αναζήτησης (με αυτή τη σειρά):
    //   1. <φάκελος Opsis>\plugins\*\
    //   2. %AppData%\Opsis\plugins\*\
    //   3. κάθε διαδρομή στο HKCU\Software\Opsis\PluginPaths (την γράφει ο installer του plugin)
    // Κάθε plugin φορτώνεται σε δικό του AssemblyLoadContext ώστε οι εξαρτήσεις του να μη μπλέκονται με του Opsis·
    // μόνο το contract (και το framework) μοιράζονται. Ένα plugin που αποτυγχάνει ΠΟΤΕ δεν ρίχνει το Opsis —
    // το σφάλμα γράφεται στο plugins.log και το plugin παραλείπεται.
    public sealed class PluginHost
    {
        public static PluginHost Instance { get; } = new();

        private sealed class Loaded
        {
            public IOpsisNewsCrawlerPlugin Plugin = null!;
            public string Folder = "";
            public volatile bool Started;
            public volatile bool Retired;
            public volatile bool StopFailed;
            public volatile bool TrustRevoked;
            public Action<string>? NavigationHandler;
        }

        private readonly List<Loaded> _plugins = new();
        // Plugins are started, approved (LoadFolder), removed and read from several threads and from timers: every read works on a SNAPSHOT taken under this lock, and every change happens
        // under it. Enumerating the live list while another thread changed it threw "Collection was modified" in the tray menu.
        private readonly object _gate = new();
        /// <summary>Runs a call into plugin code again (up to 3 times, a few ms apart) when it fails with "Collection was modified" - a plugin that is being started or stopped on another thread; any other error is passed on.</summary>
        internal static T Retry<T>(Func<T> call)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return call(); }
                catch (InvalidOperationException ex) when (attempt < 3 && ex.Message.Contains("modified", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"retry {attempt}: {ex.Message}");
                    System.Threading.Thread.Sleep(15 * attempt);
                }
            }
        }

        private Loaded[] Snap() { lock (_gate) return _plugins.ToArray(); }
        private void AddLoaded(Loaded l)
        {
            lock (_gate) _plugins.Add(l);
            UiResourceAudit.Mark("plugin-loaded", Path.GetFileName(l.Folder));
        }
        private void RemoveLoaded(Loaded l)
        {
            l.Retired = true;
            l.Started = false;
            lock (_gate) _plugins.Remove(l);
            DetachNavigation(l);
        }

        internal static bool MayDispatch(bool started, bool retired, bool stopFailed, bool allowed) =>
            started && !retired && !stopFailed && allowed;

        private bool CanDispatch(Loaded p) =>
            p.Started && !p.Retired && !p.StopFailed &&
            MayDispatch(p.Started, p.Retired, p.StopFailed, CanRun(p.Plugin.Id));

        private void AttachNavigation(Loaded p)
        {
            if (p.NavigationHandler != null || p.Retired) return;
            Action<string> handler = key => { if (CanDispatch(p)) NavigationRequested?.Invoke(key); };
            p.Plugin.NavigationRequested += handler;
            p.NavigationHandler = handler;
        }

        private static void DetachNavigation(Loaded p)
        {
            var handler = p.NavigationHandler;
            p.NavigationHandler = null;
            if (handler == null) return;
            try { p.Plugin.NavigationRequested -= handler; }
            catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' navigation unsubscribe failed: {ex.Message}"); }
        }

        // Close the host callback gate BEFORE calling plugin Stop, even if
        // Stop raises navigation synchronously or fails. Uncertain ownership
        // must never be treated as permission to start a second instance.
        private static bool TryStop(Loaded p, string reason)
        {
            bool wasStarted = p.Started;
            p.Started = false;
            if (p.StopFailed) return false;
            if (!wasStarted) return true;
            try { p.Plugin.Stop(); return true; }
            catch (Exception ex)
            {
                p.StopFailed = true;
                Log($"Plugin '{p.Plugin.Name}' stop failed ({reason}); restart required: {ex.Message}");
                return false;
            }
        }
        private bool _started;

        // A failed Start may have created native windows, hooks or workers before throwing.
        // Attempt the same owner cleanup once; prohibit same-process restart if it is uncertain.
        private static void StartOwned(Loaded p)
        {
            if (p.Started || p.Retired || p.StopFailed) return;
            try { p.Plugin.Start(); p.Started = true; }
            catch
            {
                p.Started = false;
                try { p.Plugin.Stop(); }
                catch (Exception ex) { p.StopFailed = true; Log($"Plugin '{p.Plugin.Name}' cleanup after failed start failed: {ex.Message}"); }
                DetachNavigation(p);
                throw;
            }
        }

        /// <summary>Asked (on the UI thread) before an UNTRUSTED plugin folder is loaded for the first time; returning false skips it. Default: deny.</summary>

        private PluginTrust.Store? _trustStore;

        public IReadOnlyList<IOpsisNewsCrawlerPlugin> Plugins => Snap().Select(p => p.Plugin).ToList();

        // Ζητήθηκε ρητά: ξεχωριστό κουμπί ενεργοποίησης/απενεργοποίησης ΓΙΑ ΚΑΘΕ plugin (π.χ. το
        // News Crawler), όχι μόνο ο γενικός διακόπτης "Φόρτωση plugins" (AppSettings.PluginsEnabled)
        // που τα αφορά όλα μαζί. Ένα απενεργοποιημένο plugin παραμένει φορτωμένο (ώστε να φαίνεται
        // στη σελίδα About με το δικό του κουμπί) αλλά ΔΕΝ τρέχει (Start/Stop) και δεν συνεισφέρει
        // στοιχεία πλοήγησης/tray όσο είναι απενεργοποιημένο.
        internal static bool MayRunPlugin(bool globalEnabled, bool disabled, bool safeMode, bool compatibilityHeld) =>
            globalEnabled && !disabled && !safeMode && !compatibilityHeld;

        private bool CanRun(string id)
        {
            var settings = AppSettings.Peek();
            if (Snap().Any(p => p.TrustRevoked && p.Plugin.Id == id)) return false;
            return MayRunPlugin(settings.PluginsEnabled, settings.DisabledPluginIds.Contains(id),
                SafeModeService.IsSafeMode, _globalHoldUntilRestart || _compatibilitySuspended || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold);
        }

        public bool IsPluginEnabled(string id) => !AppSettings.Peek().DisabledPluginIds.Contains(id);

        /// <summary>Revoke a SHA-bound approval and stop plugin callbacks. The DLL stays resident until restart.</summary>
        public bool RevokePluginTrust(string id)
        {
            var found = Snap().FirstOrDefault(p => p.Plugin.Id == id);
            if (found == null) return false;
            // Close dispatch and stop before persisting revocation. A failed
            // settings write must not permit the resident instance to restart.
            found.TrustRevoked = true;
            TryStop(found, "trust revoked");
            DetachNavigation(found);
            try { TrustStore.Revoke(Path.GetFileName(found.Folder)); }
            finally { SetPluginEnabled(id, false); }
            return true;
        }

        public string PluginPermissions(string id)
        {
            var found = Snap().FirstOrDefault(p => p.Plugin.Id == id);
            return found == null ? "Unknown" : PluginCapabilityManifest.Summary(found.Folder);
        }

        private volatile bool _compatibilitySuspended;
        private volatile bool _globalHoldUntilRestart;

        public void SetPluginEnabled(string id, bool enabled)
        {
            if (enabled && (_globalHoldUntilRestart || !AppSettings.Peek().PluginsEnabled || SafeModeService.IsSafeMode))
                throw new InvalidOperationException("Enable global plugins and restart Opsis before activating this plugin.");
            if (enabled && (_compatibilitySuspended || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold))
                throw new InvalidOperationException("Windows compatibility review and Opsis restart required before enabling plugins.");
            var p = Snap().FirstOrDefault(x => x.Plugin.Id == id);
            if (enabled && p is { TrustRevoked: true })
                throw new InvalidOperationException("Trust was revoked. Restart Opsis and explicitly approve the package again.");
            if (enabled && p is { StopFailed: true })
                throw new InvalidOperationException("The previous plugin stop failed. Restart Opsis before activating it.");
            var settings = AppSettings.Load();
            if (enabled) { settings.DisabledPluginIds.Remove(id); PluginFailureQuarantine.Clear(id); }
            else if (!settings.DisabledPluginIds.Contains(id)) settings.DisabledPluginIds.Add(id);
            settings.Save();

            if (p != null)
            {
                try
                {
                    if (enabled && !p.Started && !p.Retired && !p.StopFailed && CanRun(id)) { AttachNavigation(p); PushLanguage(p.Plugin); PushTheme(p.Plugin); PushHardwareProfile(p.Plugin); StartOwned(p); }
                    else if (!enabled) { TryStop(p, "toggle"); DetachNavigation(p); }
                }
                catch (Exception ex)
                {
                    Log($"Plugin '{p.Plugin.Name}' failed to {(enabled ? "start" : "stop")} after toggle: {ex.Message}");
                    if (PluginFailureQuarantine.RecordFailure(p.Plugin.Id))
                        Log($"Plugin '{p.Plugin.Name}' temporarily quarantined after repeated failures.");
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Σταματά και αφαιρεί ένα plugin από τη λίστα αμέσως (η σελίδα About/tray το χάνουν
        /// στη στιγμή), και προγραμματίζει τη διαγραφή του φακέλου του. Μια πρώτη άμεση απόπειρα
        /// γίνεται εδώ (συνήθως αποτυγχάνει όσο το .dll είναι ακόμη φορτωμένο σε αυτή τη διεργασία —
        /// Windows file lock), αλλιώς ο φάκελος μένει σε ουρά και σαρώνεται καθαρά στην επόμενη
        /// εκκίνηση, πριν προλάβει να φορτωθεί οτιδήποτε (βλ. StartAll). Δεν χρειάζεται collectible
        /// AssemblyLoadContext για αυτό — πιο απλό και αξιόπιστο από το να βασιστούμε σε unload ενώ ο
        /// host κρατάει ήδη μόνιμες αναφορές (π.χ. η συνδρομή στο NavigationRequested παρακάτω).</summary>
        public bool UninstallPlugin(string id)
        {
            var p = Snap().FirstOrDefault(x => x.Plugin.Id == id);
            if (p == null) return false;

            if (!TryStop(p, "uninstall"))
            {
                DetachNavigation(p);
                Changed?.Invoke();
                return false; // do not delete files while stop is uncertain
            }

            RemoveLoaded(p);
            var settings = AppSettings.Load();
            settings.DisabledPluginIds.Remove(id);

            bool deletedNow = false;
            try { Directory.Delete(p.Folder, recursive: true); deletedNow = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!settings.PendingPluginRemovals.Contains(p.Folder, StringComparer.OrdinalIgnoreCase))
                    settings.PendingPluginRemovals.Add(p.Folder);
            }
            settings.Save();
            Changed?.Invoke();
            return deletedNow;
        }

        /// <summary>Ένα plugin ζήτησε να εμφανιστεί μία από τις σελίδες του (π.χ. δεξί κλικ στη μπάρα ειδήσεων).</summary>
        public event Action<string>? NavigationRequested;

        /// <summary>Η λίστα στοιχείων του tray menu ή του sidebar μπορεί να έχει αλλάξει.</summary>
        public event Action? Changed;

        /// <summary>Φορτώνει και ξεκινά όλα τα plugin. Πρέπει να κληθεί στο UI thread.</summary>
        public void StartAll()
        {
            if (_started || _compatibilitySuspended || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold) return;
            _started = true;
            SweepPendingRemovals();
            if (!AppSettings.Peek().PluginsEnabled || SafeModeService.IsSafeMode) { _globalHoldUntilRestart = true; return; }

            foreach (var (folder, trustedRoot) in EnumerateCandidateFolders())
            {
                try
                {
                    ConsiderFolder(folder, trustedRoot);
                }
                catch (Exception ex) { Log($"Failed to load '{folder}': {ex}"); }
            }

            var disabled = AppSettings.Load().DisabledPluginIds;
            foreach (var p in Snap())
            {
                try
                {
                    AttachNavigation(p);
                    if (!disabled.Contains(p.Plugin.Id) && CanRun(p.Plugin.Id) && !PluginFailureQuarantine.IsQuarantined(p.Plugin.Id))
                    {
                        PushLanguage(p.Plugin); PushTheme(p.Plugin); PushHardwareProfile(p.Plugin);
                        StartOwned(p);
                    }
                    else if (PluginFailureQuarantine.IsQuarantined(p.Plugin.Id))
                        Log($"Plugin '{p.Plugin.Name}' is temporarily isolated; use the plugin toggle to re-enable it intentionally.");
                }
                catch (Exception ex)
                {
                    Log($"Plugin '{p.Plugin.Name}' failed to start: {ex}");
                    if (PluginFailureQuarantine.RecordFailure(p.Plugin.Id))
                        Log($"Plugin '{p.Plugin.Name}' quarantined after three failures.");
                    // Keep metadata and per-plugin toggle available for user recovery.
                }
            }

            ThemeManager.Changed += OnThemeChanged;
            LocalizationManager.Changed += OnLanguageChanged;
            HardwareProfileService.ProfileChanged += OnHardwareChanged;
            Changed?.Invoke();
        }

        // In-process DLLs cannot be unloaded safely. Prevent future host starts and
        // stop active plugins; full isolation is achieved on the next Opsis restart.
        public void SuspendForCompatibility()
        {
            if (_compatibilitySuspended) return;
            _compatibilitySuspended = true;
            foreach (var p in Snap())
            {
                TryStop(p, "compatibility hold");
                DetachNavigation(p);
            }
            Changed?.Invoke();
        }

        private void OnThemeChanged() { foreach (var p in Snap().Where(CanDispatch)) PushTheme(p.Plugin); }
        private void OnLanguageChanged() { foreach (var p in Snap().Where(CanDispatch)) PushLanguage(p.Plugin); }
        private void OnHardwareChanged(HardwareTier _) { foreach (var p in Snap().Where(CanDispatch)) PushHardwareProfile(p.Plugin); }

        // Global enable remains restart-based as advertised by Settings. Disable
        // is immediate; already loaded assemblies stay resident until restart.
        internal void HoldIfGloballyDisabled()
        {
            if (AppSettings.Peek().PluginsEnabled || _globalHoldUntilRestart) return;
            _globalHoldUntilRestart = true;
            StopAll();
            Changed?.Invoke();
        }

        public void StopAll()
        {
            ThemeManager.Changed -= OnThemeChanged;
            LocalizationManager.Changed -= OnLanguageChanged;
            HardwareProfileService.ProfileChanged -= OnHardwareChanged;
            foreach (var p in Snap())
            {
                if (p.Started)
                {
                    Log($"Stopping plugin '{p.Plugin.Name}'");
                    TryStop(p, "host shutdown");
                    Log($"Stop returned for plugin '{p.Plugin.Name}'");
                }
                DetachNavigation(p);
            }
        }

        // test hooks (InternalsVisibleTo Opsis.Tests): a host instance with plugins added by hand
        internal void AddForTest(IOpsisNewsCrawlerPlugin plugin, bool started) => AddLoaded(new Loaded { Plugin = plugin, Started = started });
        internal void StartAllForTest() { foreach (var p in Snap()) { p.Plugin.Start(); p.Started = true; } }

        /// <summary>The desktop widget a started plugin offers to REPLACE a built-in one (for example "calendar"), or null. A plugin that fails is logged and skipped.</summary>
        public PluginWidget? FindWidgetReplacing(string builtInId)
        {
            foreach (var p in Snap().Where(CanDispatch).ToList())
            {
                if (p.Plugin is not IPluginWidgetProvider provider) continue;
                try
                {
                    var widget = provider.GetWidgets().FirstOrDefault(w => string.Equals(w.ReplacesBuiltIn, builtInId, StringComparison.OrdinalIgnoreCase));
                    if (widget != null) { Log($"Plugin '{p.Plugin.Name}': widget '{widget.Key}' replaces built-in '{builtInId}'"); return widget; }
                }
                catch (Exception ex)
                {
                    Log($"Plugin '{p.Plugin.Name}' widgets failed: {ex.Message}");
                    if (PluginFailureQuarantine.RecordFailure(p.Plugin.Id))
                    {
                        TryStop(p, "quarantine");
                        DetachNavigation(p);
                        Log($"Plugin '{p.Plugin.Name}' quarantined after three failures; other plugins remain active.");
                    }
                }
            }
            return null;
        }

        /// <summary>Σελίδες των plugin για το sidebar, ταξινομημένες κατά Order. Ένα απενεργοποιημένο plugin δεν συνεισφέρει.</summary>
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems()
        {
            var items = new List<PluginNavigationItem>();
            foreach (var p in Snap().Where(CanDispatch))
            {
                try { items.AddRange(Retry(() => p.Plugin.GetNavigationItems().ToList())); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' navigation failed: {ex.Message}"); }
            }
            return items.OrderBy(i => i.Order).ToList();
        }

        /// <summary>Tray items per plugin (plugin display name -> its items) so the tray menu can group them by plugin.</summary>
        /// <summary>The "welcome guide" and "Opsis family" entries belong to Opsis itself (one of each in the tray, wherever the plugins are hosted); a plugin copy would show them several times.</summary>
        internal static bool IsHostOwnedEntry(PluginMenuItem item)
        {
            if (item.IsSeparator) return false;
            var k = (item.Key ?? "").Trim().ToLowerInvariant();
            return k is "welcome" or "suite" or "family" || k.EndsWith(".welcome") || k.EndsWith(".suite") || k.EndsWith(".family");
        }

        internal static List<PluginMenuItem> WithoutHostOwned(IEnumerable<PluginMenuItem> items)
        {
            var list = items.Where(i => !IsHostOwnedEntry(i)).ToList();
            while (list.Count > 0 && list[^1].IsSeparator) list.RemoveAt(list.Count - 1);          // no dangling separators
            while (list.Count > 0 && list[0].IsSeparator) list.RemoveAt(0);
            return list;
        }

        public IReadOnlyList<(string PluginName, IReadOnlyList<PluginMenuItem> Items)> GetTrayMenuGroups()
        {
            var groups = new List<(string, IReadOnlyList<PluginMenuItem>)>();
            foreach (var p in Snap().Where(CanDispatch))
            {
                try
                {
                    var items = WithoutHostOwned(Retry(() => p.Plugin.GetTrayMenuItems().ToList()));
                    if (items.Count > 0) groups.Add((p.Plugin.Name, items));
                }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' tray items failed: {ex.Message}"); }
            }
            return groups;
        }

        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems()
        {
            var items = new List<PluginMenuItem>();
            foreach (var p in Snap().Where(CanDispatch))
            {
                try { items.AddRange(WithoutHostOwned(Retry(() => p.Plugin.GetTrayMenuItems().ToList()))); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' tray items failed: {ex.Message}"); }
            }
            return items;
        }

        /// <summary>Πληροφορίες "Σχετικά" όλων των plugin (ιδιότητες, βοήθεια, οδηγίες, ιστορικό, άδεια) για τη σελίδα About,
        /// μαζί με το Id τους (ώστε η σελίδα About να μπορεί να βάλει το σωστό κουμπί ενεργό/ανενεργό δίπλα στο καθένα).
        /// Τα δείχνει ΟΛΑ, ακόμη και τα απενεργοποιημένα — αλλιώς δεν θα υπήρχε πώς να τα ξανα-ενεργοποιήσεις.</summary>
        public IReadOnlyList<(string Id, PluginAboutInfo Info)> GetAboutInfos()
        {
            var infos = new List<(string, PluginAboutInfo)>();
            foreach (var p in Snap())
            {
                try { infos.Add((p.Plugin.Id, Retry(() => p.Plugin.GetAboutInfo()))); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' about info failed: {ex.Message}"); }
            }
            return infos;
        }

        public static string TitleOf(PluginNavigationItem item) =>
            LocalizationManager.CurrentLanguage == "el" ? item.TitleEl : item.TitleEn;

        private void PushLanguage(IOpsisNewsCrawlerPlugin plugin)
        {
            try { plugin.SetLanguage(LocalizationManager.CurrentLanguage); }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetLanguage failed: {ex.Message}"); }
            PushHostSettings(plugin);
        }

        /// <summary>Contract 1.0.0.0: the settings that belong to Opsis (language, theme, hardware tier, UI scale, "Opsis manages the updates of this plugin"), for a plugin that implements IPluginHostSettingsAware.</summary>
        internal static PluginHostSettings BuildHostSettings(IOpsisNewsCrawlerPlugin plugin)
        {
            bool dark = true; try { dark = ThemeManager.Current.IsDark; } catch (Exception) { }
            int scale = 100; try { scale = AppSettings.Load().UiScalePercent; } catch (Exception) { }
            return new PluginHostSettings(LocalizationManager.CurrentLanguage, dark, HardwareProfileService.CurrentTier.ToString(), scale, plugin is IPluginUpdateSource);
        }

        private void PushHostSettings(IOpsisNewsCrawlerPlugin plugin)
        {
            if (plugin is IPluginHostSettingsAware aware)
            {
                try { aware.SetHostSettings(BuildHostSettings(plugin)); }
                catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetHostSettings failed: {ex.Message}"); }
            }
            // Optional Crawler-specific P0 bridge. A separate, exact ABI 1.0.0.0
            // DLL is not needed, so Chronikologio and other plugins are unchanged.
            PushCrawlerP0Environment(plugin);
        }

        /// <summary>Notify only active Crawler plugins when DeskStrip ownership changes.
        /// No polling, registry modifications, or callbacks to unrelated plugins.</summary>
        public void NotifyCrawlerP0EnvironmentChanged()
        {
            foreach (var entry in Snap().Where(CanDispatch))
                PushCrawlerP0Environment(entry.Plugin);
        }

        private static readonly Func<double, double, System.Threading.Tasks.Task<string>> SharedCrawlerWeather =
            (latitude, longitude) => WeatherService.GetWallpaperWeatherJsonAsync(latitude, longitude);

        /// <summary>
        /// Integrates only the known Crawler plugin with Opsis' existing cached
        /// WeatherHub and shell guard. This is opt-in duck typing; an older
        /// Crawler simply lacks the method and continues to run unchanged.
        /// No location settings or credentials are passed: the Crawler asks
        /// for weather only for cities already in its own user configuration.
        /// </summary>
        private static void PushCrawlerP0Environment(IOpsisNewsCrawlerPlugin plugin)
        {
            if (!string.Equals(plugin.Id, "giorstergiopoulos.opsisnewscrawler",
                    StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var signature = new[] {
                    typeof(bool), typeof(bool),
                    typeof(Func<double, double, System.Threading.Tasks.Task<string>>)
                };
                var setter = plugin.GetType().GetMethod("SetOpsisP0Environment",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null, types: signature, modifiers: null);
                if (setter is null) return; // ABI-compatible older Crawler
                var safeMode = SafeModeService.IsSafeMode;
                // DeskStrip occupies screen edge. Yield control rather than
                // installing another Windows AppBar or fighting its z-order.
                bool shellHold = safeMode;
                try { shellHold |= DeskStripHostEngine.SessionPermitted && DeskStripHostEngine.Instance.GetSettings().StartEnabled; }
                catch (Exception) { shellHold = true; }
                // P3/P5 hosts may provide the extra post-Windows-Update hold.
                // It is deliberately optional for the stable 2.8.0 host.
                var guard = typeof(PluginHost).Assembly.GetType(
                    "MotionDesk.Services.WindowsUpdateGuard");
                var prop = guard?.GetProperty("RequiresReview",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(null) is true) shellHold = true;
                setter.Invoke(plugin, new object[] { safeMode, shellHold, SharedCrawlerWeather });
            }
            catch (Exception ex)
            {
                Log($"Crawler P0 optional integration unavailable: {ex.GetBaseException().Message}");
                // Never prevent other plugins or the Opsis tray from starting.
            }
        }

        // Handoff Φάση 6 (ελάχιστο): προαιρετική ικανότητα — καλείται ΜΟΝΟ αν το plugin υλοποιεί το
        // νέο, προαιρετικό IPluginProfileAware (δεν αγγίζει καθόλου το υποχρεωτικό
        // IOpsisNewsCrawlerPlugin contract, άρα κανένα υπάρχον plugin δεν σπάει).
        private void PushHardwareProfile(IOpsisNewsCrawlerPlugin plugin)
        {
            if (plugin is not OpsisNewsCrawler.Plugin.Abstractions.IPluginProfileAware aware) { PushHostSettings(plugin); return; }
            try { aware.SetHardwareProfile(HardwareProfileService.CurrentTier.ToString()); }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetHardwareProfile failed: {ex.Message}"); }
        }

        private void PushTheme(IOpsisNewsCrawlerPlugin plugin)
        {
            try
            {
                var t = ThemeManager.Current;
                var opacity = Math.Clamp(AppSettings.Load().WindowOpacity, 0.3, 1.0);
                plugin.SetTheme(t.IsDark, t.Background.ToArgb(), t.Surface.ToArgb(), t.Border.ToArgb(),
                    t.TextPrimary.ToArgb(), t.TextMuted.ToArgb(), t.AccentCyan.ToArgb(), opacity);
            }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetTheme failed: {ex.Message}"); }
        }

        // Διαγράφει τους φακέλους plugin που σημειώθηκαν για αφαίρεση σε προηγούμενη συνεδρία, ΠΡΙΝ
        // φορτωθεί οτιδήποτε άλλο σε αυτή — τώρα κανένα .dll τους δεν είναι ακόμη φορτωμένο, οπότε η
        // διαγραφή πετυχαίνει αξιόπιστα.
        private static void SweepPendingRemovals()
        {
            var settings = AppSettings.Load();
            if (settings.PendingPluginRemovals.Count == 0) return;
            var remaining = new List<string>();
            foreach (var folder in settings.PendingPluginRemovals)
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                catch (Exception ex) { Log($"Could not remove pending plugin folder '{folder}': {ex.Message}"); remaining.Add(folder); }
            }
            settings.PendingPluginRemovals = remaining;
            settings.Save();
        }

        // ---- discovery & loading ---------------------------------------------------------------

        // A folder of an untrusted root (user-writable) is loaded only after the user approved its exact content (see PluginTrust).
        // Start-up NEVER waits for that answer (a modal box at login, maybe hidden behind other windows, used to block the tray and left the plugin's page missing for good):
        // an unapproved folder goes to PendingApprovals and is asked about right after start-up (ResolvePending) or from the tray menu.
        private readonly List<PluginTrustRequest> _pending = new();
        internal PluginTrust.Store? TrustStoreOverride;          // tests
        internal Action<string>? FolderLoader;                   // tests
        private PluginTrust.Store TrustStore => TrustStoreOverride ?? (_trustStore ??= new PluginTrust.Store(Path.Combine(AppPaths.AppDataFolder, "plugin-trust.json")));

        /// <summary>Plugin folders found but not approved yet (user-writable location, content hash not in plugin-trust.json).</summary>
        public IReadOnlyList<PluginTrustRequest> PendingApprovals { get { lock (_pending) return _pending.ToList(); } }

        /// <summary>Loads a folder if its root is trusted or its exact content is approved; otherwise remembers it as pending. Returns true when it was loaded.</summary>
        internal bool ConsiderFolder(string folder, bool trustedRoot)
        {
            PluginCapabilityManifest.RequireCompatibleApi(folder);
            var requested = PluginCapabilityManifest.Read(folder); // malformed declaration fails before assembly loading
            // Protected legacy packages keep their established admission path.
            // A package explicitly requesting capabilities requires consent even there.
            if (!trustedRoot || requested.Length > 0)
            {
                string name = Path.GetFileName(folder), hash;
                try { hash = PluginTrust.HashFolder(folder); }
                catch (Exception ex) { Log($"Plugin '{folder}' skipped: trust check failed: {ex.Message}"); return false; }
                if (!TrustStore.IsApproved(name, hash, requested))
                {
                    RefreshPending(folder, name, hash);
                    Log($"Plugin '{name}' in '{folder}' is waiting for approval (sha256 {hash}); it is not loaded until the user approves it.");
                    return false;
                }
            }
            (FolderLoader ?? LoadFolder)(folder);
            return true;
        }

        private void RefreshPending(string folder, string name, string hash)
        {
            var (publisher, signature) = PluginPublisherInfo.Inspect(folder);
            var requested = PluginCapabilityManifest.Read(folder);
            var request = new PluginTrustRequest(name, folder, hash, publisher, signature,
                requested.Length == 0 ? "Not declared / Δεν δηλώθηκαν" : string.Join(", ", requested), Array.AsReadOnly(requested));
            lock (_pending) { _pending.RemoveAll(p => string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase)); _pending.Add(request); }
        }

        /// <summary>Asks about every pending plugin (on the UI thread, after start-up); the approved ones are loaded and started at once. Returns how many were approved.</summary>
        public int ResolvePending(Func<PluginTrustRequest, bool> ask)
        {
            if (_globalHoldUntilRestart || _compatibilitySuspended || WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode || !AppSettings.Peek().PluginsEnabled || WindowsUpdateGuard.RuntimeHold) return 0;
            int approved = 0;
            foreach (var req in PendingApprovals)
            {
                try
                {
                    PluginCapabilityManifest.RequireCompatibleApi(req.Folder);
                    var currentCapabilities = PluginCapabilityManifest.Read(req.Folder);
                    string beforePrompt = PluginTrust.HashFolder(req.Folder);
                    if (!string.Equals(beforePrompt, req.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        !currentCapabilities.SequenceEqual(PluginCapabilityManifest.NormalizeCapabilities(req.RequestedCapabilities)))
                    { RefreshPending(req.Folder, req.Name, beforePrompt); continue; }
                }
                catch (Exception ex) { Log($"Plugin '{req.Name}' preapproval validation failed: {ex.Message}"); continue; }
                bool yes;
                try { yes = ask(req); } catch (Exception ex) { Log($"Plugin '{req.Name}' approval prompt failed: {ex.Message}"); yes = false; }
                if (!yes) { Log($"Plugin '{req.Name}' not approved by the user (sha256 {req.Sha256})."); continue; }
                if (_globalHoldUntilRestart || _compatibilitySuspended || WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode || !AppSettings.Peek().PluginsEnabled || WindowsUpdateGuard.RuntimeHold) break;
                try
                {
                    string currentHash = PluginTrust.HashFolder(req.Folder);
                    if (!string.Equals(currentHash, req.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        RefreshPending(req.Folder, req.Name, currentHash);
                        Log($"Plugin '{req.Name}' changed before approval completed; it requires a new approval.");
                        continue;
                    }
                    if (_globalHoldUntilRestart || _compatibilitySuspended || WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode || !AppSettings.Peek().PluginsEnabled || WindowsUpdateGuard.RuntimeHold) break;
                    PluginCapabilityManifest.RequireCompatibleApi(req.Folder);
                    var granted = PluginCapabilityManifest.Read(req.Folder);
                    if (!granted.SequenceEqual(PluginCapabilityManifest.NormalizeCapabilities(req.RequestedCapabilities)))
                    { RefreshPending(req.Folder, req.Name, currentHash); continue; }
                    TrustStore.Approve(req.Name, req.Sha256, granted);
                    Log($"Plugin '{req.Name}' approved by the user (sha256 {req.Sha256}).");
                    var before = Snap().ToHashSet();
                    (FolderLoader ?? LoadFolder)(req.Folder);
                    foreach (var p in Snap().Where(p => !before.Contains(p))) Activate(p);
                    lock (_pending) _pending.RemoveAll(p => string.Equals(p.Folder, req.Folder, StringComparison.OrdinalIgnoreCase));
                    approved++;
                }
                catch (Exception ex) { Log($"Plugin '{req.Name}' failed to load after approval: {ex}"); }
            }
            if (approved > 0) Changed?.Invoke();
            return approved;
        }

        private void Activate(Loaded p)
        {
            try
            {
                AttachNavigation(p);
                if (CanRun(p.Plugin.Id) && !PluginFailureQuarantine.IsQuarantined(p.Plugin.Id))
                {
                    PushLanguage(p.Plugin); PushTheme(p.Plugin); PushHardwareProfile(p.Plugin);
                    StartOwned(p);
                }
            }
            catch (Exception ex)
            {
                Log($"Plugin '{p.Plugin.Name}' failed to start: {ex}");
                if (PluginFailureQuarantine.RecordFailure(p.Plugin.Id))
                    Log($"Plugin '{p.Plugin.Name}' quarantined after three failures.");
            }
        }

        internal static IEnumerable<(string Folder, bool TrustedRoot)> EnumerateCandidateFolders()
        {
            // The install directory is trusted only when it is itself protected (Program Files); a per-user install or a dev build is not.
            string installPlugins = Path.Combine(AppContext.BaseDirectory, "plugins");
            bool installTrusted = PluginTrust.IsUnderProtectedInstall(AppContext.BaseDirectory);
            var locations = new List<(string Path, bool Trusted, bool MayBePlugin)>
            {
                (installPlugins, installTrusted, false),
                (Path.Combine(AppPaths.AppDataFolder, "plugins"), false, false),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis", "plugins"), false, false)
            };
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Opsis\PluginPaths");
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        if (key.GetValue(name) is string path && path.Length > 0) locations.Add((path, false, true));
            }
            catch (Exception) { }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (location, trusted, direct) in locations)
            {
                string root;
                try
                {
                    if (!Path.IsPathFullyQualified(location) || location.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                    root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { continue; }
                if (!Directory.Exists(root)) continue;
                string[] folders;
                try
                {
                    folders = direct && Directory.EnumerateFiles(root, "*Plugin.dll").Any()
                        ? [root] : Directory.GetDirectories(root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (var folder in folders)
                    if (seen.Add(folder) && !IsReplacedByBuiltIn(folder))
                        yield return (folder, trusted && PackagePaths.IsExistingUnlinkedPath(AppContext.BaseDirectory, folder));
            }
        }

        /// <summary>
        /// Plugins whose features were moved INTO Opsis (2.6.0): Opsis Appearance is now the built-in "Windows appearance" page, so an installed
        /// copy of the old plugin must not load (two engines would fight over the same Windows settings). Never asks for approval, never deletes anything.
        /// </summary>
        internal static readonly string[] ReplacedPluginDlls = { "OpsisAppearancePlugin.dll" };

        internal static bool IsReplacedByBuiltIn(string folder)
        {
            try { return ReplacedPluginDlls.Any(d => File.Exists(Path.Combine(folder, d))); }
            catch (Exception) { return false; }
        }

        internal void LoadFolder(string folder)
        {
            PluginCapabilityManifest.RequireCompatibleApi(folder);
            _ = PluginCapabilityManifest.Read(folder);
            if (IsReplacedByBuiltIn(folder)) { Log($"Plugin folder '{Path.GetFileName(folder)}' skipped: its features are built into Opsis 2.6.0"); return; }
            foreach (var dll in Directory.EnumerateFiles(folder, "*Plugin.dll"))
            {
                if (!PackagePaths.IsExistingUnlinkedPath(folder, dll)) continue;
                // #97: only known 1.0.0.0 plugin entrypoints are gated.
                // Unknown third-party plugins retain the established ABI policy.
                var fileName = Path.GetFileName(dll);
                if (fileName.Equals("OpsisNewsCrawler.OpsisPlugin.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Chronikologio.OpsisPlugin.dll", StringComparison.OrdinalIgnoreCase))
                {
                    var identity = AssemblyName.GetAssemblyName(dll);
                    if (identity.Version != new Version(1, 0, 0, 0))
                    {
                        Log($"Plugin '{fileName}' skipped: ABI {identity.Version} (" + AppVersion.Short + " requires 1.0.0.0). Update this plugin before enabling it.");
                        continue;
                    }
                }
                var context = new PluginLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(IOpsisNewsCrawlerPlugin).IsAssignableFrom(type)) continue;
                    if (Activator.CreateInstance(type) is not IOpsisNewsCrawlerPlugin plugin) continue;

                    // Το ίδιο plugin μπορεί να βρεθεί σε δύο φακέλους (π.χ. η παλιά έκδοση που ήρθε
                    // bundled με τον installer του Opsis στο <app>\plugins, ΚΑΙ μια νεότερη που έγραψε
                    // ο δικός του αυτόματος updater στο %AppData%\Opsis\plugins). Ζητήθηκε ρητά να μην
                    // μένει "κολλημένη" η παλιά — κρατάμε πάντα τη ΝΕΟΤΕΡΗ έκδοση, όχι αυτή που απλά
                    // βρέθηκε πρώτη (ο παλιός κώδικας έκανε ακριβώς αυτό το λάθος: "first wins").
                    var existing = Snap().FirstOrDefault(p => p.Plugin.Id == plugin.Id);
                    if (existing != null)
                    {
                        if (!IsNewerVersion(plugin.Version, existing.Plugin.Version)) continue;
                        if (!TryStop(existing, "replacement")) continue;
                        RemoveLoaded(existing);
                    }
                    AddLoaded(new Loaded { Plugin = plugin, Folder = folder });
                }
            }
        }

        private static bool IsNewerVersion(string candidate, string current)
        {
            if (System.Version.TryParse(candidate, out var c) && System.Version.TryParse(current, out var u)) return c > u;
            return string.CompareOrdinal(candidate, current) > 0; // fallback: ασυνήθιστη μορφή έκδοσης, τουλάχιστον σταθερή σύγκριση
        }

        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;
            private readonly string _pluginDirectory;

            public PluginLoadContext(string pluginPath) : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
            {
                string assemblyPath = Path.GetFullPath(pluginPath);
                _pluginDirectory = Path.GetDirectoryName(assemblyPath)!;
                _resolver = new AssemblyDependencyResolver(assemblyPath);
            }

            protected override Assembly? Load(AssemblyName name)
            {
                // Το contract πρέπει να είναι το ίδιο Assembly instance με του host.
                // Το return null αφήνει την επίλυση στο default context και μπορεί να αποτύχει
                // όταν το contract δεν βρίσκεται στο probing path της εφαρμογής.
                var contractAssembly = typeof(IOpsisNewsCrawlerPlugin).Assembly;
                if (string.Equals(name.Name, contractAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                    return CompatibleHostAssembly(name, contractAssembly);

                // Opsis.UI must be shared as well: a separate copy inside every plugin's
                // AssemblyLoadContext creates independent OpsisTheme/GlassSettings statics.
                // The host owns theme, language and accessibility state for all plugins.
                var sharedUiAssembly = typeof(Opsis.UI.FlatButton).Assembly;
                if (string.Equals(name.Name, sharedUiAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                    return CompatibleHostAssembly(name, sharedUiAssembly);

                // Πρώτα χρησιμοποιούμε το .deps.json μέσω του AssemblyDependencyResolver.
                var path = _resolver.ResolveAssemblyToPath(name);
                if (path != null && File.Exists(path) && PackagePaths.IsExistingUnlinkedPath(_pluginDirectory, path))
                    return LoadFromAssemblyPath(path);

                // Fallback: αρκετά plugin distributions περιλαμβάνουν τις εξαρτήσεις δίπλα
                // στο *Plugin.dll, αλλά το .deps.json μπορεί να μην τις καταγράφει σωστά.
                if (!string.IsNullOrEmpty(name.Name))
                {
                    var candidate = Path.Combine(_pluginDirectory, name.Name + ".dll");
                    if (File.Exists(candidate) && PackagePaths.IsExistingUnlinkedPath(_pluginDirectory, candidate))
                    {
                        // Έλεγχος ταυτότητας ώστε να μη φορτωθεί DLL άλλης έκδοσης με το ίδιο όνομα.
                        try
                        {
                            var candidateName = AssemblyName.GetAssemblyName(candidate);
                            if (AssemblyName.ReferenceMatchesDefinition(name, candidateName))
                                return LoadFromAssemblyPath(candidate);
                        }
                        catch (BadImageFormatException)
                        {
                            // Δεν είναι managed assembly· αφήνουμε το runtime να συνεχίσει την επίλυση.
                        }
                    }
                }

                return null;
            }

            private static Assembly CompatibleHostAssembly(AssemblyName requested, Assembly host)
            {
                var available = host.GetName();
                if (!AssemblyName.ReferenceMatchesDefinition(requested, available) ||
                    (requested.Version != null && available.Version != null && requested.Version > available.Version))
                    throw new FileLoadException($"Plugin requires {requested.FullName}; Opsis provides {available.FullName}. Update Opsis or the plugin.");
                return host;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null && PackagePaths.IsExistingUnlinkedPath(_pluginDirectory, path) ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }

        private static void Log(string message)
        {
            try
            {
                var path = Path.Combine(AppPaths.AppDataFolder, "plugins.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:O}] {message}\n");
            }
            catch (Exception) { }
        }
    }
}

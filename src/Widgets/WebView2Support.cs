using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MotionDesk.Widgets
{
    /// <summary>Creates one shared WebView2 environment for all rich-content surfaces.</summary>
    public static class WebView2Support
    {
        // ΟΧΙ Lazy<Task>: ένα αποτυχημένο Task (π.χ. λείπει το WebView2 Runtime) θα έμενε cached για όλη τη
        // συνεδρία και το wallpaper δεν θα ανέκαμπτε ποτέ, ούτε αφού ο χρήστης εγκαταστήσει το runtime.
        private static Task<CoreWebView2Environment>? _environment;
        private static readonly object _envLock = new();

        public static Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            lock (_envLock)
            {
                if (_environment == null || _environment.IsFaulted || _environment.IsCanceled)
                    _environment = CreateEnvironmentAsyncCore();
                return _environment;
            }
        }

        /// <summary>Έκδοση WebView2 Runtime ή null αν λείπει (για διαγνωστικά και μηνύματα σφάλματος).</summary>
        public static string? GetRuntimeVersion()
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception) { return null; }
        }

        // Σκληραγώγηση των WebView2 που φιλοξενούν ΜΟΝΟ τοπικές σελίδες της εφαρμογής (wallpaper/widgets) και
        // εκθέτουν host objects (WallpaperBridge/WidgetBridge): κανένα link/σφάλμα δεν πρέπει να μπορεί να
        // φορτώσει εξωτερικό περιεχόμενο στο ίδιο WebView (θα είχε πρόσβαση στα host objects), ούτε να ανοίξει
        // νέο παράθυρο ή DevTools/μενού περιβάλλοντος πάνω στην επιφάνεια εργασίας.
        private static readonly Queue<DateTime> _failures = new();

        /// <summary>
        /// The CoreWebView2 of a control, or null when there is none YET or the control is gone. Reading CoreWebView2 (or calling a member of it) after the WebView2 control was disposed throws
        /// "CoreWebView2 members cannot be accessed after the WebView2 control is disposed" - which timers, restart handlers and async continuations hit when Explorer restarts and the
        /// wallpaper windows are rebuilt. Every access goes through here.
        /// </summary>
        public static CoreWebView2? TryCore(Microsoft.Web.WebView2.WinForms.WebView2? view)
        {
            if (view == null) return null;
            try { return view.IsDisposed || view.Disposing ? null : view.CoreWebView2; }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { return null; }
        }

        public static void Harden(CoreWebView2 core)
        {
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            // YouTube embeds refuse to play without a Referer (error 153); a file:// page sends none, so a neutral one is added for the embed host only.
            core.AddWebResourceRequestedFilter("https://www.youtube-nocookie.com/*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter("https://www.youtube.com/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => { try { e.Request.Headers.SetHeader("Referer", "https://opsis.local/"); } catch (Exception) { } };
            // Crash του GPU/renderer process: χωρίς handler το wallpaper έμενε παγωμένο/λευκό μέχρι επανεκκίνηση.
            core.ProcessFailed += (_, e) =>
            {
                MotionDesk.Services.AttachLog.Write($"WebView2 ProcessFailed: {e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode}");
                // A frozen (heavy pause) or just-thawed renderer reports itself as unresponsive: that is not a crash.
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive && ProcessFreezer.IsFrozenOrRecent) return;
                // Retry budget: a renderer that keeps dying (bad driver, corrupt page) must not make Opsis reload in a loop.
                var now = DateTime.UtcNow;
                lock (_failures) { _failures.Enqueue(now); while (_failures.Count > 0 && (now - _failures.Peek()).TotalSeconds > 120) _failures.Dequeue(); if (_failures.Count > 4) { MotionDesk.Services.AttachLog.Write("WebView2 failure budget exceeded - not reloading"); return; } }
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                    or CoreWebView2ProcessFailedKind.GpuProcessExited)
                {
                    try { core.Reload(); } catch (Exception) { }
                }
            };
        }

        /// <summary>The WebView2 user-data folder: the normal one under LocalAppData\Opsis, but inside the isolated data folder when OPSIS_APPDATA is set (tests and measurements),
        /// so an isolated run never shares - or fights over - the browser profile of the real installation (it failed with 0x800700AA while the real Opsis was running).</summary>
        internal static string UserDataFolder(string? isolatedAppData = null, string? localAppData = null, string browserArguments = "")
        {
            isolatedAppData ??= Environment.GetEnvironmentVariable("OPSIS_APPDATA");
            string baseFolder = !string.IsNullOrWhiteSpace(isolatedAppData) ? Path.Combine(isolatedAppData, "WebView2")
                : Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Opsis", "WebView2");
            if (browserArguments.Length == 0) return baseFolder;
            // the browser arguments are part of the environment: WebView2 refuses a second environment on the SAME folder with DIFFERENT arguments while another process uses it (0x8007139F).
            // Another Opsis edition running at the same time (Opsis Studio) did exactly that to the wallpaper of 2.4.6.2 ("WebView2 not ready" for ever): the folder is keyed by the arguments.
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Path.Combine(baseFolder, "p" + Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(browserArguments)))[..8].ToLowerInvariant());
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsyncCore()
        {
            // (LocalApplicationData, όχι ApplicationData/AppPaths.AppDataFolder: cache δεδομένα του
            // WebView2 runtime, όχι ρυθμίσεις χρήστη — σωστά τοπικό/μη-roaming, ξαναδημιουργείται
            // αβλαβώς αν χαθεί, οπότε δεν χρειάζεται μετάβαση από το παλιό όνομα.)
            const string args = "--disable-features=msSmartScreenProtection,Translate,msEdgeSync --disable-background-networking " +
                "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows --autoplay-policy=no-user-gesture-required";
            string userDataFolder = UserDataFolder(browserArguments: args);
            Directory.CreateDirectory(userDataFolder);
            // Λιγότερη μνήμη/CPU σε αδύναμα συστήματα: χωρίς background networking/sync/translate, χωρίς renderer
            // backgrounding (το wallpaper είναι πίσω από όλα, τα Windows θα το "κοιμίζανε").
            var options = new CoreWebView2EnvironmentOptions(args);
            try { return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options).ConfigureAwait(true); }
            catch (Exception ex)
            {
                MotionDesk.Services.AttachLog.Write("WebView2 environment failed: " + ex.GetType().Name + ": " + ex.Message + " - retrying with a private profile");
                string own = Path.Combine(userDataFolder, "private-" + Environment.ProcessId);
                Directory.CreateDirectory(own);
                return await CoreWebView2Environment.CreateAsync(null, own, options).ConfigureAwait(true);
            }
        }
    }
}

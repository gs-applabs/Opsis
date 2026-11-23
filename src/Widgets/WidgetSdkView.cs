using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal static class WidgetSdkView
{
    internal static async Task InitializeAsync(WebView2 view, WidgetCatalog.Package package, Func<int> refreshPercent)
    {
        await view.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
        if (view.IsDisposed || view.CoreWebView2 is not { } core) return;
        var origin = "https://" + package.Manifest.Id + ".widget.opsis.invalid/";
        bool OwnUrl(string uri) => uri.StartsWith(origin, StringComparison.Ordinal);
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.NavigationStarting += (_, e) => e.Cancel = !OwnUrl(e.Uri);
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            string uri = e.Request.Uri;
            string? relative = OwnUrl(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var url)
                ? Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')) : null;
            string? path = relative != null ? WidgetPackageFormat.ExistingResource(package.Directory, relative) : null;
            string? mime = path != null ? WidgetPackageFormat.BrowserType(path) : null;
            if (path != null && mime != null && e.Request.Method == "GET" && File.Exists(path))
            {
                try
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length > 40 * 1024 * 1024) throw new IOException("Invalid widget resource.");
                    var bytes = new byte[(int)input.Length];
                    input.ReadExactly(bytes);
                    var file = new MemoryStream(bytes, writable: false);
                    e.Response = core.Environment.CreateWebResourceResponse(file, 200, "OK",
                        "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
                        "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'; frame-src 'none'; worker-src 'none'");
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store");
        };
        // Local system data is NEVER part of the initial state. Only an explicit
        // user-approved, revokeable, origin-checked SDK message can request it.
        string digest;
        try { digest = WidgetConsentStore.Fingerprint(package.Directory, package.Manifest.Entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { digest = string.Empty; }
        long lastSystemPush = 0;
        void PublishSystem()
        {
            if (digest.Length == 0 || !WidgetConsentStore.Allowed(package.Manifest.Id, digest) ||
                !OwnUrl(core.Source ?? ""))
            {
                core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "opsis:system-denied" }));
                return;
            }
            // A widget cannot force 60Hz sensor polling through postMessage.
            if (Environment.TickCount64 - lastSystemPush < Math.Clamp(1000 * refreshPercent() / 100, 250, 4000)) return;
            lastSystemPush = Environment.TickCount64;
            var metrics = SystemMonitorService.Instance.GetSnapshot();
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "opsis:system", cpu = metrics.CpuPercent,
                ramAvailableMb = metrics.AvailableMemoryMb, ramTotalMb = metrics.TotalMemoryMb
            }));
        }
        void Publish()
        {
            if (view.IsDisposed || !OwnUrl(core.Source ?? "")) return;
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "opsis:state", api = 1, refreshPercent = Math.Clamp(refreshPercent(), 25, 400), language = LocalizationManager.CurrentLanguage,
                dark = ThemeManager.Current.Background.GetBrightness() < .5,
                accent = ColorTranslator.ToHtml(ThemeManager.Current.AccentCyan),
                now = DateTimeOffset.Now.ToString("O")
            }));
        }
        core.WebMessageReceived += (_, e) =>
        {
            if (!OwnUrl(e.Source) || e.WebMessageAsJson.Length > 512) return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (message.RootElement.ValueKind == JsonValueKind.Object &&
                    message.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString() == "opsis:ready") Publish();
                else if (message.RootElement.ValueKind == JsonValueKind.Object &&
                    message.RootElement.TryGetProperty("type", out var capability) &&
                    capability.ValueKind == JsonValueKind.String && capability.GetString() == "opsis:request-system")
                {
                    if (!WidgetConsentStore.Allowed(package.Manifest.Id, digest) && digest.Length > 0)
                    {
                        var answer = MessageBox.Show(view.FindForm(),
                            MainWindow.StudioText($"Allow '{package.Manifest.Name}' to read CPU/RAM statistics? This can be revoked from its context menu.",
                                $"Επιτρέπεις στο '{package.Manifest.Name}' να διαβάζει CPU/RAM; Μπορείς να ανακαλέσεις τη συναίνεση από το μενού του widget."),
                            "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                        if (answer == DialogResult.Yes) WidgetConsentStore.Grant(package.Manifest.Id, digest);
                    }
                    PublishSystem();
                }
            }
            catch (JsonException) { }
        };
        core.NavigationCompleted += (_, e) => { if (e.IsSuccess) Publish(); };
        ThemeManager.Changed += Publish;
        LocalizationManager.Changed += Publish;
        view.Disposed += (_, _) => { ThemeManager.Changed -= Publish; LocalizationManager.Changed -= Publish; };
        core.Navigate(origin + string.Join('/', package.Manifest.Entry.Split('/').Select(Uri.EscapeDataString)));
    }

    internal static void NotifyRefresh(WebView2? view, int refreshPercent)
    {
        if (view?.IsDisposed != false || view.CoreWebView2 == null) return;
        view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "opsis:refresh", refreshPercent = Math.Clamp(refreshPercent, 25, 400)
        }));
    }
}

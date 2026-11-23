using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal static class WidgetSdkView
{
    internal static async Task InitializeAsync(WebView2 view, WidgetCatalog.Package package)
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
        void Publish()
        {
            if (view.IsDisposed || !OwnUrl(core.Source ?? "")) return;
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "opsis:state", api = 1, language = LocalizationManager.CurrentLanguage,
                dark = ThemeManager.Current.Background.GetBrightness() < .5,
                accent = ColorTranslator.ToHtml(ThemeManager.Current.AccentCyan),
                now = DateTimeOffset.Now.ToString("O")
            }));
        }
        core.WebMessageReceived += (_, e) =>
        {
            if (!OwnUrl(e.Source) || e.WebMessageAsJson.Length > 128) return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (message.RootElement.ValueKind == JsonValueKind.Object &&
                    message.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString() == "opsis:ready") Publish();
            }
            catch (JsonException) { }
        };
        core.NavigationCompleted += (_, e) => { if (e.IsSuccess) Publish(); };
        ThemeManager.Changed += Publish;
        LocalizationManager.Changed += Publish;
        view.Disposed += (_, _) => { ThemeManager.Changed -= Publish; LocalizationManager.Changed -= Publish; };
        core.Navigate(origin + string.Join('/', package.Manifest.Entry.Split('/').Select(Uri.EscapeDataString)));
    }
}

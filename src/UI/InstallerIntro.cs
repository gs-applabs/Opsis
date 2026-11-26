using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

// Standalone setup-only player. No shell, wallpaper, plugin or single-instance initialization.
internal sealed class InstallerIntro : Form
{
    private const string Host = "opsis-intro.local";
    private readonly System.Windows.Forms.Timer _deadline = new() { Interval = 22000 };
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "assets", "intro");
    private readonly string _language;
    private Image? _poster;
    private static readonly HashSet<string> Languages = new(StringComparer.OrdinalIgnoreCase)
    { "greek", "english", "german", "french", "spanish", "korean", "chinese", "italian", "russian", "japanese", "portuguese", "turkish", "arabic", "hindi" };

    internal InstallerIntro(string language)
    {
        _language = Languages.Contains(language) ? language.ToLowerInvariant() : "english";
        Text = "OPSIS · GS App Labs";
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int width = Math.Min(1280, Math.Min(area.Width - 48, (area.Height - 80) * 16 / 9));
        ClientSize = new Size(Math.Max(320, width), Math.Max(180, width * 9 / 16));
        BackColor = Color.FromArgb(5, 8, 19);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        try
        {
            using var image = Image.FromFile(Path.Combine(_folder, "poster-clean.png"));
            _poster = new Bitmap(image);
            BackgroundImage = _poster;
            BackgroundImageLayout = ImageLayout.Zoom;
        }
        catch { }
        Controls.Add(_view);
        _deadline.Tick += (_, _) => Close();
        Shown += async (_, _) => await StartAsync();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
    }

    private async Task StartAsync()
    {
        _deadline.Start();
        try
        {
            if (!File.Exists(Path.Combine(_folder, "intro.webm"))) { Close(); return; }
            await _view.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
            if (IsDisposed || Disposing) return;
            var core = _view.CoreWebView2;
            var url = $"https://{Host}/index.html?lang={_language}";
            core.SetVirtualHostNameToFolderMapping(Host, _folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NavigationStarting += (_, e) => e.Cancel = !string.Equals(e.Uri, url, StringComparison.Ordinal);
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host != Host)
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (e.Source != url) return;
                try { if (e.TryGetWebMessageAsString() is "ended" or "skip" or "error") Close(); } catch { }
            };
            core.ProcessFailed += (_, _) => { if (!IsDisposed) Close(); };
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess && !IsDisposed) Close(); };
            _view.Source = new Uri(url);
            _view.Visible = true;
        }
        catch { if (!IsDisposed && !Disposing) Close(); } // missing WebView2 must not block setup
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _deadline.Stop(); _deadline.Dispose(); _view.Dispose(); BackgroundImage = null; _poster?.Dispose(); }
        base.Dispose(disposing);
    }
}

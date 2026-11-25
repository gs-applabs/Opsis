using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

/// <summary>Photo-specific, WebP-capable skyline brush editor hosted in an isolated bundled page.</summary>
internal sealed class SkylineMaskEditorForm : Form
{
    private readonly string sourceFile;
    private readonly string sourceHash;
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };

    private SkylineMaskEditorForm(string asset, string fullPath)
    {
        sourceFile = asset;
        using (var input = File.OpenRead(fullPath)) sourceHash = Convert.ToHexString(SHA256.HashData(input));
        Text = MainWindow.StudioText("Skyline brush — photo-specific", "Πινέλο ουρανού — ανά φωτογραφία");
        MinimumSize = new Size(650, 470);
        ClientSize = new Size(1040, 710);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.PageBackground;
        Controls.Add(web);
        Shown += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await web.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
            if (IsDisposed || web.CoreWebView2 == null) return;
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper", "skyline-editor.html");
            if (!File.Exists(htmlPath)) throw new FileNotFoundException("Skyline editor HTML not found", htmlPath);
            WebView2Support.Harden(web.CoreWebView2, htmlPath);
            web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (IsDisposed || !Uri.TryCreate(args.Source, UriKind.Absolute, out var uri) ||
                    !uri.IsFile || !Path.GetFullPath(uri.LocalPath).Equals(htmlPath, StringComparison.OrdinalIgnoreCase)) return;
                try
                {
                    using var message = JsonDocument.Parse(args.WebMessageAsJson);
                    var root = message.RootElement;
                    if (root.GetProperty("action").GetString() != "save") return;
                    var width = root.GetProperty("width").GetInt32();
                    var height = root.GetProperty("height").GetInt32();
                    var rows = root.GetProperty("rows").EnumerateArray().Select(x => x.GetInt32()).ToList();
                    if (width != 512 || height is < 1 or > 512 || rows.Count != 512 || rows.Any(x => x < 0 || x > height)) return;
                    var settings = WallpaperSettings.Load();
                    settings.WeatherSkylineEdits ??= new(StringComparer.OrdinalIgnoreCase);
                    settings.WeatherSkylineEdits[sourceFile] = new WallpaperSkylineEdit
                    {
                        Sha256 = sourceHash, Width = width, Height = height, Rows = rows
                    };
                    settings.Save(); WallpaperHostEngine.Instance.RefreshAll(); DialogResult = DialogResult.OK;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
                { MessageBox.Show(this, ex.Message, "OPSIS", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            web.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess || IsDisposed || web.CoreWebView2 == null) return;
                var existing = WallpaperSettings.Load().WeatherSkylineEdits;
                object? previous = null;
                if (existing != null && existing.TryGetValue(sourceFile, out var edit) && edit != null &&
                    edit.Sha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                    previous = new { width = edit.Width, height = edit.Height, rows = edit.Rows };
                var json = JsonSerializer.Serialize(new { file = sourceFile, existing = previous });
                await web.CoreWebView2.ExecuteScriptAsync("window.opsisEditorLoad(" + json + ");");
            };
            web.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, ex.Message, "OPSIS", MessageBoxButtons.OK, MessageBoxIcon.Warning); Close(); }
    }

    internal static bool OpenFor(string id, IWin32Window owner)
    {
        string? asset = id == LandmarkCatalog.MountainLake ? "scenes/mountain-lake.webp" :
            id == "parthenon" ? "scenes/acropolis.webp" : LandmarkCatalog.Scenes.FirstOrDefault(s => s.Id == id)?.File;
        if (string.IsNullOrWhiteSpace(asset) || !asset.StartsWith("scenes/", StringComparison.Ordinal) || asset.Contains("..")) return false;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper"));
        var full = Path.GetFullPath(Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full) || new FileInfo(full).Length > 40L * 1024 * 1024) return false;
        try { using var editor = new SkylineMaskEditorForm(asset, full); editor.ShowDialog(owner); return true; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(owner, ex.Message, "Skyline mask", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }
}

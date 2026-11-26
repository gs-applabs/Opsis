using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>Optional add-on packages offered in the Plugins area of the About page, and the live progress card in the sidebar.</summary>
public sealed partial class MainWindow
{
    private SidebarTaskPanel _sidebarTask = null!;
    private CancellationTokenSource? _addonCts;

    // ---- live progress in the sidebar (mirrored in the status bar, because the top-navigation skins have no sidebar) ----
    private void ShowSidebarTask(string title, string detail, double fraction)
    {
        if (IsDisposed || _sidebarTask == null) return;
        _sidebarTask.Compact = _sidebarCollapsed;
        _sidebarTask.Update(title, detail, fraction);
        _statusLabel.Text = title + " — " + detail;
    }

    private void HideSidebarTask()
    {
        if (!IsDisposed && _sidebarTask != null) _sidebarTask.Finish();
    }

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>The wallpapers add-on card (state, download + install, cancel). <paramref name="addHeader"/>: the Plugins heading is not on the page yet.</summary>
    private void BuildWallpaperAddOnCard(Control panel, bool addHeader)
    {
        if (addHeader) AddSection(panel, T("About.SectionPlugins"));
        AddText(panel, string.Format(T("Addon.Title"), WallpaperAddOnService.Version));
        AddText(panel, string.Format(T("Addon.Describe"), WallpaperAddOnService.WallpaperCount, (WallpaperAddOnService.ExpectedBytes + 524288) / 1048576));
        var status = WallpaperAddOnService.ReadStatus();
        var state = AddText(panel, status.State switch
        {
            WallpaperAddOnService.PackState.UpToDate => string.Format(T("Addon.StateOk"), status.InstalledCount),
            WallpaperAddOnService.PackState.UpdateAvailable => string.Format(T("Addon.StatePartial"), status.InstalledCount) + "\n" + T("Addon.UpgradeNote"),
            _ => T("Addon.StateNone")
        });
        state.Name = "addon-wallpapers-state";
        var install = AddButton(panel, T("Addon.Button"), (_, _) => { });
        install.Name = "addon-wallpapers-install";
        var cancel = AddButton(panel, T("Addon.Cancel"), (_, _) => _addonCts?.Cancel());
        cancel.Name = "addon-wallpapers-cancel";
        cancel.Visible = false;
        install.Visible = status.State != WallpaperAddOnService.PackState.UpToDate;
        install.Click += async (_, _) =>
        {
            if (_addonCts != null) return; // one download at a time
            if (MessageBox.Show(this, string.Format(T("Addon.Confirm"), (WallpaperAddOnService.ExpectedBytes + 524288) / 1048576),
                    "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            using var cts = new CancellationTokenSource();
            _addonCts = cts;
            install.Enabled = false; cancel.Visible = true;
            string title = string.Format(T("Addon.Downloading"), WallpaperAddOnService.Version);
            try
            {
                ShowSidebarTask(title, "0 %", 0);
                var progress = new Progress<double>(f =>
                {
                    ShowSidebarTask(title, (int)(f * 100) + " %", f);
                    if (!state.IsDisposed) state.Text = title + ": " + (int)(f * 100) + " %";
                });
                string path = await WallpaperAddOnService.DownloadAsync(progress, cts.Token);
                ShowSidebarTask(T("Addon.Verifying"), "SHA-256", -1);
                WallpaperAddOnService.Launch(path);
                _statusLabel.Text = T("Addon.Started");
                if (!state.IsDisposed) state.Text = T("Addon.Started");
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = T("Addon.Canceled");
                if (!state.IsDisposed) state.Text = T("Addon.Canceled");
            }
            catch (InvalidDataException ex) when (ex.Message is "hash-mismatch" or "unexpected-size" or "too-large" or "incomplete")
            {
                string text = string.Format(T("Addon.Failed"), T("Addon.HashMismatch"));
                _statusLabel.Text = text;
                if (!state.IsDisposed) state.Text = text;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                string text = string.Format(T("Addon.Failed"), ex.Message);
                _statusLabel.Text = text;
                if (!state.IsDisposed) state.Text = text;
            }
            finally
            {
                _addonCts = null;
                HideSidebarTask();
                if (!install.IsDisposed) { install.Enabled = true; }
                if (!cancel.IsDisposed) cancel.Visible = false;
            }
        };
    }
}

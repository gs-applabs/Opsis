using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    // Plugin updates (Opsis 2.6.0, task 15): the ONE place that checks and installs updates of plugins is the update button of Opsis (About page). The same click that asks GitHub
    // about Opsis asks about every plugin that tells where its releases are (IPluginUpdateSource). Installing needs a second click + confirmation; the package is verified (SHA-256)
    // and staged, and put in place at the next start where the user is asked ONCE to approve the new content (PluginTrust).
    public sealed partial class MainWindow
    {
        private IReadOnlyList<PluginUpdateInfo> _pluginUpdates = Array.Empty<PluginUpdateInfo>();
        private bool _installingPluginUpdates;

        private async Task CheckPluginUpdatesAsync(Label status, ProgressBar progress, FlowLayoutPanel buttonRow)
        {
            var candidates = PluginHost.Instance.GetUpdateCandidates();
            if (candidates.Count == 0) return;
            IReadOnlyList<PluginUpdateInfo> found;
            try { found = await PluginUpdateService.CheckAllAsync(candidates); }
            catch (Exception) { return; }
            if (status.IsDisposed) return;
            _pluginUpdates = found;
            foreach (var old in buttonRow.Controls.OfType<HoverButton>().Where(b => (b.Tag as string) == "plugin-update").ToList()) { buttonRow.Controls.Remove(old); old.Dispose(); }
            if (found.Count == 0) { status.Text += "\n" + LocalizationManager.T("About.PluginUpdateUpToDate"); return; }
            foreach (var u in found)
                status.Text += "\n" + string.Format(LocalizationManager.T("About.PluginUpdateNewer"), u.Plugin.Name, u.Latest.ToString(3), u.Plugin.Version);
            var btn = NewHoverButton(string.Format(LocalizationManager.T("About.PluginUpdateInstall"), found.Count), async () => await InstallPluginUpdatesAsync(status, progress));
            btn.Tag = "plugin-update";
            btn.Width = 200;
            buttonRow.Controls.Add(btn);
        }

        private async Task InstallPluginUpdatesAsync(Label status, ProgressBar progress)
        {
            if (_installingPluginUpdates || _pluginUpdates.Count == 0) return;
            var names = string.Join(", ", _pluginUpdates.Select(u => u.Plugin.Name + " " + u.Latest.ToString(3)));
            if (MessageBox.Show(this, string.Format(LocalizationManager.T("About.PluginUpdateConfirm"), names),
                    LocalizationManager.T("About.PluginUpdateConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _installingPluginUpdates = true;
            progress.Value = 0; progress.Visible = true;
            int done = 0;
            try
            {
                var root = PluginUpdateService.StagingRoot();
                foreach (var u in _pluginUpdates)
                {
                    var p = new Progress<double>(v => { if (!progress.IsDisposed) progress.Value = Math.Clamp((int)Math.Round(v * 100), 0, 100); });
                    try { await PluginUpdateService.DownloadAndStageAsync(u, root, p); done++; }
                    catch (InvalidDataException) { status.Text = LocalizationManager.T("About.UpdateVerifyFailed"); }
                    catch (InvalidOperationException) { status.Text = LocalizationManager.T("About.UpdateNoDigest"); }
                    catch (Exception ex) { status.Text = string.Format(LocalizationManager.T("About.PluginUpdateFailed"), ex.Message); }
                }
                if (done > 0 && !status.IsDisposed) status.Text = LocalizationManager.T("About.PluginUpdateStaged");
            }
            finally
            {
                _installingPluginUpdates = false;
                if (!progress.IsDisposed) progress.Visible = false;
            }
        }
    }
}

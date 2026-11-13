using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>The settings of ONE container, opened from its own right-click menu. Every change is applied live through <see cref="ContainerHost.Apply"/> (no restart, no Apply button).</summary>
    internal sealed class ContainerSettingsForm : Form
    {
        private static ContainerSettingsForm? _open;

        public static void Open(ContainerHost host, string id)
        {
            if (_open != null && !_open.IsDisposed) { _open.Close(); }
            var c = host.Containers().FirstOrDefault(x => x.Id == id); if (c == null) return;
            _open = new ContainerSettingsForm(host, c); _open.Show();
        }

        private ContainerSettingsForm(ContainerHost host, Container c)
        {
            Text = LocalizationManager.T("DCM.ContainerSettings").TrimEnd('.', '…') + " - " + (string.IsNullOrWhiteSpace(c.Title) ? "Container" : c.Title);
            FormBorderStyle = FormBorderStyle.FixedToolWindow; StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f); AutoScroll = true; ClientSize = new Size(400, 620); Padding = new Padding(12, 4, 12, 8);
            Controls.Add(new ContainerSettingsPanel(host, c, dark: false) { Dock = DockStyle.Top });
            FormClosed += (_, _) => { if (ReferenceEquals(_open, this)) _open = null; };
        }
    }
}

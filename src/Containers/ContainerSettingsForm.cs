using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>The settings of ONE container, opened from its own right-click menu. Every change is applied live through <see cref="ContainerHost.Apply"/> (no restart, no Apply button).</summary>
    internal sealed class ContainerSettingsForm : MotionDesk.UI.AppDialog
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
            string heading = LocalizationManager.T("DCM.ContainerSettings").TrimEnd('.', '…');
            void OnContainerRenamed(string id, string title) { if (id == c.Id) Text = heading + " - " + title; }
            OnContainerRenamed(c.Id, string.IsNullOrWhiteSpace(c.Title) ? "Container" : c.Title);
            void OnContainerDeleted(string id) { if (id == c.Id) Close(); }
            host.ContainerRenamed += OnContainerRenamed;
            host.ContainerDeleted += OnContainerDeleted;
            Disposed += (_, _) =>
            {
                host.ContainerRenamed -= OnContainerRenamed;
                host.ContainerDeleted -= OnContainerDeleted;
            };
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimumSize = new Size(470, 380);
            ClientSize = new Size(540, 490);
            Padding = new Padding(15);
            BackColor = MotionDesk.UI.UiTheme.PageBackground;
            ForeColor = MotionDesk.UI.UiTheme.TextPrimary;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            bool dark = BackColor.GetBrightness() < .5f;
            using var settings = new ContainerSettingsPanel(host, c, dark);
            FlowLayoutPanel? group = null;
            foreach (Control control in settings.Controls.Cast<Control>().ToArray())
            {
                if (ReferenceEquals(control.Tag, ContainerSettingsPanel.SectionMarker))
                {
                    var page = new TabPage(control.Text) { BackColor = BackColor, ForeColor = ForeColor };
                    group = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
                        WrapContents = false, Padding = new Padding(14, 12, 8, 8) };
                    page.Controls.Add(group);
                    tabs.TabPages.Add(page);
                }
                else if (group != null) group.Controls.Add(control);
            }
            Controls.Add(tabs);
            MotionDesk.UI.MainWindow.StyleStudioDialog(this);
            FormClosed += (_, _) => { if (ReferenceEquals(_open, this)) _open = null; };
        }
    }
}

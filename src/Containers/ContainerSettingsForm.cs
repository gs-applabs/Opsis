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
            string T(string k) => LocalizationManager.T(k); var id = c.Id;
            Text = T("DCM.ContainerSettings").TrimEnd('.', '…') + " - " + (string.IsNullOrWhiteSpace(c.Title) ? "Container" : c.Title);
            FormBorderStyle = FormBorderStyle.FixedToolWindow; StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            ClientSize = new Size(380, 232); Font = new Font("Segoe UI", 9f);
            int y = 12;
            void Row(string label, Control input) { Controls.Add(new Label { Text = label, Location = new Point(12, y + 4), Size = new Size(190, 20) }); input.Location = new Point(206, y); Controls.Add(input); y += 32; }
            NumericUpDown Num(int v, int min, int max, Action<Container, int> set)
            {
                var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(v, min, max), Width = 80 };
                n.ValueChanged += (_, _) => host.Apply(id, k => { set(k, (int)n.Value); return true; });
                return n;
            }
            var color = new TextBox { Width = 100, Text = c.TitleBar.Color };
            color.Leave += (_, _) =>
            {
                var t = color.Text.Trim();
                if (t.Equals("Auto", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$")) host.Apply(id, k => { k.TitleBar.Color = t; return true; });
                else color.Text = host.Containers().FirstOrDefault(x => x.Id == id)?.TitleBar.Color ?? "Auto";
            };
            Row(T("DCM.TitleColour"), color);
            Row(T("DCM.TitleOpacity"), Num(c.TitleBar.Opacity, 0, 100, (k, v) => k.TitleBar.Opacity = v));
            Row(T("DCM.TitleHeight"), Num(c.TitleBar.Height, 16, 96, (k, v) => k.TitleBar.Height = v));
            Row(T("DCM.TitleFont"), Num((int)c.TitleBar.FontSize, 6, 36, (k, v) => k.TitleBar.FontSize = v));
            Row(T("DCM.IconSize"), Num(c.Appearance.IconSize > 0 ? c.Appearance.IconSize : 48, 16, 128, (k, v) => k.Appearance.IconSize = v));
            var lk = new CheckBox { Text = T("DCM.Locked"), Checked = c.Locked, Location = new Point(12, y), AutoSize = true };
            lk.CheckedChanged += (_, _) => host.Apply(id, k => { k.Locked = lk.Checked; return true; });
            Controls.Add(lk);
            FormClosed += (_, _) => { if (ReferenceEquals(_open, this)) _open = null; };
        }
    }
}

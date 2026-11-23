using System.Drawing;
using System.Windows.Forms;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>Spot Search popup (Ctrl+`): type a name, Up/Down to choose, Enter launches, Esc or clicking elsewhere closes it. Searches every container (tabs included) through <see cref="ContainerSearch"/>.</summary>
    internal sealed class SpotSearchForm : MotionDesk.UI.AppDialog
    {
        private readonly TextBox _box = new() { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 13f), Dock = DockStyle.Top };
        private readonly ListBox _list = new() { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11f), Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Func<ContainersDocument> _doc; private List<SearchHit> _hits = new();

        public SpotSearchForm(Func<ContainersDocument> doc)
        {
            _doc = doc;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; Size = new Size(520, 300);
            BackColor = MotionDesk.UI.UiTheme.Surface; ForeColor = MotionDesk.UI.UiTheme.TextPrimary; Padding = new Padding(12);
            _box.BackColor = BackColor; _box.ForeColor = ForeColor; _list.BackColor = BackColor; _list.ForeColor = ForeColor;
            Controls.Add(_list); Controls.Add(_box);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + wa.Height / 5);
            _box.TextChanged += (_, _) => Refill();
            _box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up && _list.Items.Count > 0) { _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && e.Shift) { ShowContainingContainer(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Launch(); e.Handled = e.SuppressKeyPress = true; }
            };
            _list.DoubleClick += (_, _) => Launch();
            _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && e.Shift) { ShowContainingContainer(); e.Handled = e.SuppressKeyPress = true; } };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show containing container (Shift+Enter)", null, (_, _) => ShowContainingContainer());
            menu.Items.Add("Open selected file (Enter)", null, (_, _) => Launch());
            _list.ContextMenuStrip = menu;
            FormClosed += (_, _) => menu.Dispose();
            Deactivate += (_, _) => Close();
            Shown += (_, _) => { Activate(); _box.Focus(); };
        }

        private void Refill()
        {
            _hits = ContainerSearch.Find(_doc(), _box.Text);
            _list.BeginUpdate(); _list.Items.Clear();
            foreach (var h in _hits) _list.Items.Add((string.IsNullOrEmpty(h.Item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(h.Item.Path) : h.Item.DisplayName) + "   -   " + h.ContainerTitle);
            _list.EndUpdate(); if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        // #55: jump to the real container rather than executing its selected file.
        private void ShowContainingContainer()
        {
            var i = _list.SelectedIndex;
            var host = ContainerHost.Current;
            if (host == null || i < 0 || i >= _hits.Count) return;
            var hit = _hits[i];
            var matches = host.Containers().Where(container =>
                string.Equals(container.Title, hit.ContainerTitle, StringComparison.OrdinalIgnoreCase) &&
                container.Items.Concat(container.Tabs.SelectMany(tab => tab.Items))
                    .Any(item => string.Equals(item.Path, hit.Item.Path, StringComparison.OrdinalIgnoreCase)))
                .Take(2).ToArray();
            if (matches.Length != 1) return; // ambiguous titles/paths: never jump to the wrong container
            Close();
            host.Focus(matches[0].Id);
        }

        private void Launch()
        {
            var i = _list.SelectedIndex; if (i < 0 || i >= _hits.Count) return;
            var it = _hits[i].Item;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true, Arguments = it.Arguments ?? "" }); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("spot search: " + ex.Message); }
            Close();
        }

        protected override bool ShowWithoutActivation => false;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }   // CS_DROPSHADOW
    }
}

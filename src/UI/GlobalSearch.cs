using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    /// <summary>One result of the global search: the page it opens and the text (a card title, a setting, an intro) to scroll to.</summary>
    internal sealed record SearchEntry(string Page, string PageLabel, string Text);

    /// <summary>
    /// Pure part of the app-wide search (like the search bar of GearWin: type, pick "text / page", the page opens). The index comes from the locale strings, grouped into pages by their key prefix;
    /// matching ignores case and accents (every word must occur), with a small typo tolerance (Levenshtein) when nothing matches exactly.
    /// </summary>
    internal static class GlobalSearch
    {
        /// <summary>Key prefix to page key (page keys are the ones of the navigation). First match wins, so the longer prefixes come first.</summary>
        internal static readonly (string Prefix, string Page)[] PrefixPages =
        {
            ("DeskZones.SectionContainers", "DeskContainers"), ("DeskZones.ContainersIntro", "DeskContainers"), ("DeskContainer.", "DeskContainers"), ("DCM.", "DeskContainers"), ("DCX.", "DeskContainers"),
            ("Personalization.SectionDeskStrip", "DeskStrip"), ("Personalization.DeskStrip", "DeskStrip"),
            ("Settings.MusicOverlay", "Music"), ("Dashboard.", "Dashboard"), ("Widgets.", "Widgets"), ("WidgetText.", "Widgets"), ("DeskZones.", "DeskZones"), ("Wallpaper.", "Wallpaper"), ("Viz.", "Music"), ("Weather.", "Wallpaper"),
            ("Performance.", "Performance"), ("Perf.", "Performance"), ("Settings.", "Settings"), ("Automation.", "Automation"), ("Profiles.", "Automation"),
            ("Personalization.", "Personalization"), ("LockGif.", "Personalization"), ("Sounds.", "Personalization"), ("Cursors.", "Personalization"),
            ("AudioEnhancement.", "AudioEnhancement"), ("Apo.", "AudioEnhancement"), ("About.", "About"),
        };

        public static string? PageOf(string key)
        {
            foreach (var (p, page) in PrefixPages) if (key.StartsWith(p, StringComparison.Ordinal)) return page;
            return null;
        }

        /// <summary>The searchable entries: short single-line texts of the strings that belong to a page, without format placeholders, plus the pages themselves.</summary>
        public static List<SearchEntry> BuildIndex(IReadOnlyDictionary<string, string> strings, IReadOnlyCollection<string> pageKeys, Func<string, string> pageLabel)
        {
            var list = new List<SearchEntry>();
            var seen = new HashSet<string>();
            foreach (var kv in strings)
            {
                var page = PageOf(kv.Key);
                if (page == null || !pageKeys.Contains(page)) continue;
                var text = kv.Value.Split('\n')[0].Trim();
                if (text.Length < 3 || text.Length > 110 || text.Contains('{')) continue;
                if ((kv.Key.Contains("Intro") || kv.Key.Contains("Desc") || kv.Key.Contains("Note")) && text.Length > 70) continue;
                if (seen.Add(page + "|" + text)) list.Add(new SearchEntry(page, pageLabel(page), text));
            }
            foreach (var page in pageKeys) list.Add(new SearchEntry(page, pageLabel(page), pageLabel(page)));
            return list;
        }

        public static List<SearchEntry> Search(IReadOnlyList<SearchEntry> index, string? query, int max = 8)
        {
            if (SearchPhonetic(index, query, max) is { Count: > 0 } ph && !index.Any(e => PageSearch.Matches(e.PageLabel + " " + e.Text, query))) return ph;
            var words = PageSearch.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return new List<SearchEntry>();
            var hits = index.Where(e => PageSearch.Matches(e.PageLabel + " " + e.Text, query)).OrderBy(e => e.Text == e.PageLabel ? 0 : 1).ThenBy(e => e.Text.Length).Take(max).ToList();
            if (hits.Count > 0) return hits;
            return index.Select(e => (E: e, S: Fuzzy(words, PageSearch.Normalize(e.PageLabel + " " + e.Text)))).Where(x => x.S < int.MaxValue).OrderBy(x => x.S).Select(x => x.E).Take(max).ToList();
        }

        /// <summary>Greek and Greeklish meet in a common "sound" spelling: "rologi", "ρολόι", "roloï" all become the same letters, so a Greek setting is found with Latin letters.</summary>
        internal static string Phonetic(string? s)
        {
            var t = PageSearch.Normalize(s);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in t)
                sb.Append(ch switch
                {
                    'α' => "a", 'β' => "v", 'γ' => "g", 'δ' => "d", 'ε' => "e", 'ζ' => "z", 'η' => "i", 'θ' => "th", 'ι' => "i", 'κ' => "k", 'λ' => "l", 'μ' => "m", 'ν' => "n", 'ξ' => "x", 'ο' => "o",
                    'π' => "p", 'ρ' => "r", 'σ' or 'ς' => "s", 'τ' => "t", 'υ' => "i", 'φ' => "f", 'χ' => "ch", 'ψ' => "ps", 'ω' => "o", '8' => "th", 'w' => "o", 'y' => "i", 'u' => "u", 'j' => "i", _ => ch.ToString()
                });
            return sb.ToString().Replace("th", "0001").Replace("ch", "0002").Replace("h", "i").Replace("ou", "u").Replace("oi", "i").Replace("ei", "i").Replace("ai", "e").Replace("0001", "th").Replace("0002", "ch");
        }

        private static List<SearchEntry> SearchPhonetic(IReadOnlyList<SearchEntry> index, string? query, int max)
        {
            var words = Phonetic(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return new List<SearchEntry>();
            return index.Where(e => { var t = Phonetic(e.PageLabel + " " + e.Text); return words.All(w => t.Contains(w)); }).OrderBy(e => e.Text.Length).Take(max).ToList();
        }

        private static int Fuzzy(string[] q, string label)
        {
            var lw = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int total = 0;
            foreach (var w in q)
            {
                int th = w.Length <= 4 ? 1 : 2, best = int.MaxValue;
                foreach (var l in lw) best = Math.Min(best, Levenshtein(w, l));
                if (best > th) return int.MaxValue;
                total += best;
            }
            return total;
        }

        internal static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }
    }

    /// <summary>
    /// The search box of the header (where the "?" used to be): a rounded outline with a magnifier and a clear "x", no fill of its own (the header shows through), and a list of results under it.
    /// </summary>
    internal sealed class HeaderSearchBox : Control
    {
        private readonly TextBox _box = new() { BorderStyle = BorderStyle.None };
        private readonly ListBox _list = new() { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44, IntegralHeight = false, Visible = false };
        private List<SearchEntry> _index = new();
        private List<SearchEntry> _shown = new();
        public event Action<SearchEntry>? Picked;

        public HeaderSearchBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent; Size = new Size(280, 34); Cursor = Cursors.IBeam;
            Controls.Add(_box);
            _box.TextChanged += (_, _) => { Refresh_(); Invalidate(); };
            _box.GotFocus += (_, _) => Invalidate();
            _box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Clear(); e.Handled = true; }
                else if (e.KeyCode == Keys.Down && _list.Visible && _list.Items.Count > 0) { _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up && _list.Visible && _list.Items.Count > 0) { _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && _shown.Count > 0) { Pick(_list.SelectedIndex >= 0 ? _list.SelectedIndex : 0); e.Handled = e.SuppressKeyPress = true; }
            };
            _box.LostFocus += (_, _) => { Invalidate(); if (IsHandleCreated) BeginInvoke(new Action(() => { if (!_box.Focused && !_list.Focused) _list.Visible = false; })); };
            _list.MouseClick += (_, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0) Pick(i); };
            _list.DrawItem += DrawResult;
            _list.MouseMove += (_, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0 && i != _list.SelectedIndex) _list.SelectedIndex = i; };
        }

        public void FocusBox() { _box.Focus(); _box.SelectAll(); }
        public string Query => _box.Text;
        internal ListBox ResultsList => _list;
        internal void SetQueryForTest(string q) => _box.Text = q;

        /// <summary>The list is a child of the window (so it can hang below the header): the owner gives it a parent.</summary>
        public void AttachResults(Control host) { host.Controls.Add(_list); _list.BringToFront(); }

        public void RebuildIndex(IReadOnlyCollection<string> pageKeys)
        {
            _index = GlobalSearch.BuildIndex(LocalizationManager.AllStrings(), pageKeys, p => { var s = LocalizationManager.T("Nav." + p); return s == "Nav." + p ? p : s; });
        }

        private void Clear() { _box.Text = ""; _list.Visible = false; }

        private void Pick(int i)
        {
            if (i < 0 || i >= _shown.Count) return;
            var e = _shown[i]; Clear(); Picked?.Invoke(e);
        }

        private void Refresh_()
        {
            _shown = GlobalSearch.Search(_index, _box.Text);
            _list.Items.Clear();
            foreach (var s in _shown) _list.Items.Add(s.Text);
            if (_shown.Count == 0) { _list.Visible = false; return; }
            _list.BackColor = UiTheme.Surface; _list.ForeColor = UiTheme.TextPrimary;
            var host = _list.Parent;
            if (host != null)
            {
                var p = host.PointToClient(PointToScreen(new Point(0, Height + 4)));
                _list.SetBounds(Math.Max(0, Math.Min(p.X, host.ClientSize.Width - 360)), p.Y, 360, Math.Min(8, _shown.Count) * _list.ItemHeight + 4);
            }
            _list.SelectedIndex = 0; _list.Visible = true; _list.BringToFront();
        }

        private void DrawResult(object? s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _shown.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? UiTheme.SurfaceHover : UiTheme.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
            var it = _shown[e.Index];
            using var f1 = new Font("Segoe UI", 9.5f); using var f2 = new Font("Segoe UI", 8f);
            TextRenderer.DrawText(e.Graphics, it.Text, f1, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4, e.Bounds.Width - 20, 20), UiTheme.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, it.PageLabel, f2, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 24, e.Bounds.Width - 20, 16), UiTheme.AccentCyan, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            _box.Font = UiTheme.FontBody; _box.ForeColor = UiTheme.TextPrimary; _box.BackColor = UiTheme.Surface; _box.PlaceholderText = Opsis.UI.OpsisStrings.T("ui.searchPlaceholder");
            _box.Font = new Font("Segoe UI", 10f); _box.SetBounds(34, (Height - _box.PreferredHeight) / 2, Width - 34 - 30, _box.PreferredHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.X > Width - 28 && _box.Text.Length > 0) Clear(); else _box.Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            _box.BackColor = UiTheme.Surface; _box.ForeColor = UiTheme.TextPrimary;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.RoundedPath(r, 10))
            {
                using (var fill = new SolidBrush(UiTheme.Surface)) g.FillPath(fill, path);          // clearly visible: a filled rounded field, not a black box
                using var pen = new Pen(_box.Focused ? UiTheme.AccentCyan : UiTheme.TextMuted, 1.4f); g.DrawPath(pen, path);
            }
            using var pm = new Pen(UiTheme.TextSecondary, 1.6f);
            g.DrawEllipse(pm, 12, Height / 2 - 7, 9, 9); g.DrawLine(pm, 20, Height / 2 + 1, 25, Height / 2 + 6);
            if (_box.Text.Length > 0) { g.DrawLine(pm, Width - 22, Height / 2 - 4, Width - 14, Height / 2 + 4); g.DrawLine(pm, Width - 22, Height / 2 + 4, Width - 14, Height / 2 - 4); }
        }
    }
}

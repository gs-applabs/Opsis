using System.Globalization;
using System.Text;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.UI;

namespace MotionDesk.UI
{
    // Shared text matching for the settings search.
    // The matching is accent- and case-insensitive (Greek tonos, German umlauts...) and pure, so it is unit-tested.
    internal static class PageSearch
    {
        public static string Normalize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Replace("&", "");
        }

        /// <summary>True when every word of the query occurs in the text.</summary>
        public static bool Matches(string? text, string? query)
        {
            var q = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (q.Length == 0) return true;
            var t = Normalize(text);
            foreach (var w in q) if (!t.Contains(w)) return false;
            return true;
        }

        private static void Collect(Control c, StringBuilder sb)
        {
            if (c is Label or ButtonBase) sb.Append(c.Text).Append(' ');
            if (c is FlatToggle t) sb.Append(t.Description).Append(' ');
            foreach (Control child in c.Controls) Collect(child, sb);
        }

        public static string TextOf(Control c) { var sb = new StringBuilder(); Collect(c, sb); return sb.ToString(); }

        internal static void Reveal(Control root, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            void Expand(Control control)
            {
                if (control is StudioAdvancedSection section && section.ContainsMatch(query)) section.SearchExpansion(true);
                foreach (Control child in control.Controls) Expand(child);
            }
            Control? Match(Control control)
            {
                if (control is Label or ButtonBase && Matches(control.Text, query) && control.Visible) return control;
                foreach (Control child in control.Controls)
                    if (Match(child) is { } found) return found;
                return null;
            }
            Expand(root);
            if (Match(root) is not { } target) return;
            for (Control? ancestor = target.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is ScrollableControl scroll && scroll.AutoScroll) scroll.ScrollControlIntoView(target);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    /// <summary>Tag put on the title of a section (AddSection / AddCardSection): the page is cut into Opsis.UI cards at these marks.</summary>
    internal sealed class SectionMark
    {
        public SectionMark(string title, Action? reset) { Title = title; Reset = reset; }
        public string Title { get; }
        public Action? Reset { get; }
    }

    /// <summary>One card of a page: the always visible rows and the rows of the collapsed "Advanced" group.</summary>
    internal sealed class CardSection
    {
        public string Title { get; set; } = "";
        public Action? Reset { get; set; }
        public List<Control> Main { get; } = new();
        public List<Control> Advanced { get; } = new();
    }

    internal static class PageCards
    {
        public const string AdvancedTag = "opsis-advanced-start";

        /// <summary>Zero-size marker: every row added to the same section after it belongs to the collapsed "Advanced" group.</summary>
        public static Control AdvancedMarker() => new Panel { Size = new Size(0, 0), Margin = new Padding(0), Tag = AdvancedTag, TabStop = false };

        private static bool IsAdvancedMarker(Control c) => AdvancedTag.Equals(c.Tag);

        /// <summary>Cuts the controls of an old-style page into cards. Loose controls before the first section are returned in <paramref name="lead"/> (a text label first, then rows).</summary>
        public static List<CardSection> Split(IEnumerable<Control> items, string leadTitle, out string? intro)
        {
            var result = new List<CardSection>();
            intro = null;
            CardSection? cur = null;
            bool inAdvanced = false;
            void Add(Control c)
            {
                if (IsAdvancedMarker(c)) { inAdvanced = true; return; }
                if (cur == null) { cur = new CardSection { Title = leadTitle }; result.Add(cur); }
                (inAdvanced ? cur.Advanced : cur.Main).Add(c);
            }
            foreach (var c in items)
            {
                if ("search".Equals(c.Tag)) continue;
                if (c.Tag is SectionMark m)
                {
                    cur = new CardSection { Title = m.Title, Reset = m.Reset };
                    result.Add(cur); inAdvanced = false;
                    // a card of AddCardSection: the content column is the single child, its first label is the title
                    if (c is Panel card && card.Controls.Count == 1 && card.Controls[0] is FlowLayoutPanel inner)
                    {
                        var kids = inner.Controls.Cast<Control>().ToList();
                        if (kids.Count > 0 && kids[0] is Label) kids.RemoveAt(0);
                        foreach (var k in kids) Add(k);
                    }
                    continue;
                }
                if (cur == null && intro == null && c is Label l && !IsAdvancedMarker(c)) { intro = l.Text; continue; }
                Add(c);
            }
            return result.Where(s => s.Main.Count + s.Advanced.Count > 0).ToList();
        }
    }
}

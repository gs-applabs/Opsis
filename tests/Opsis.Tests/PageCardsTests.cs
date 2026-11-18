using System;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests
{
    public class PageCardsTests
    {
        private static Control Title(string t, Action? reset = null) => new Label { Text = t, Tag = new SectionMark(t, reset) };

        [Fact]
        public void Splits_page_into_cards_with_intro_reset_and_advanced()
        {
            bool reset = false;
            var items = new Control[]
            {
                new TextBox { Tag = "search" },
                new Label { Text = "intro" },
                new Panel { Name = "lead" },
                Title("A", () => reset = true),
                new Panel { Name = "a1" },
                PageCards.AdvancedMarker(),
                new Panel { Name = "a2" },
                Title("B"),
                new Panel { Name = "b1" },
            };
            var s = PageCards.Split(items, "Lead", out var intro);
            Assert.Equal("intro", intro);
            Assert.Equal(new[] { "Lead", "A", "B" }, s.Select(x => x.Title).ToArray());
            Assert.Equal("lead", s[0].Main.Single().Name);
            Assert.Equal("a1", s[1].Main.Single().Name);
            Assert.Equal("a2", s[1].Advanced.Single().Name);
            Assert.Null(s[2].Reset);
            s[1].Reset!(); Assert.True(reset);
            Assert.DoesNotContain(s.SelectMany(x => x.Main.Concat(x.Advanced)), c => PageCards.AdvancedTag.Equals(c.Tag) || "search".Equals(c.Tag));
        }

        [Fact]
        public void AddCardSection_style_card_is_unpacked_and_empty_cards_are_dropped()
        {
            var inner = new FlowLayoutPanel();
            inner.Controls.Add(new Label { Text = "TITLE" });
            inner.Controls.Add(new Panel { Name = "x" });
            inner.Controls.Add(PageCards.AdvancedMarker());
            inner.Controls.Add(new Panel { Name = "y" });
            var card = new Panel { Tag = new SectionMark("Card", null) };
            card.Controls.Add(inner);
            var s = PageCards.Split(new Control[] { card, Title("Empty") }, "Lead", out var intro);
            Assert.Null(intro);
            var c = Assert.Single(s);
            Assert.Equal("Card", c.Title);
            Assert.Equal("x", c.Main.Single().Name);
            Assert.Equal("y", c.Advanced.Single().Name);
        }
    }
}

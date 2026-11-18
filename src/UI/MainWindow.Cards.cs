using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.UI;

namespace MotionDesk.UI
{
    public sealed partial class MainWindow
    {
        /// <summary>Host of a card page: remembers the SettingsPage so that a rebuild keeps the scroll position.</summary>
        private sealed class CardHost : Panel
        {
            public Control? Page { get; set; }
        }

        // the card hands its rows the width it has while it is still being built (tiny): a row of fixed size (a preview strip, a stat card, a list) would be squeezed to a few pixels
        private static void KeepSize(Control c)
        {
            if ((c.AutoSize && c is not TextBoxBase) || c.Dock != DockStyle.None || c.Width < 40 || c.Height < 1) return;
            c.MinimumSize = c.Size;
        }

        private static Control AdvancedStart() => PageCards.AdvancedMarker();

        /// <summary>
        /// 2.6.0: turns a page built the old way (a flow panel of sections and rows) into the shared Opsis.UI layout: SettingsPage with one SettingsCard per section,
        /// a Reset per card (when the section gave one), rows after <see cref="PageCards.AdvancedMarker"/> in a collapsed "Advanced" group, and the shared search box.
        /// </summary>
        private Control Cardify(FlowLayoutPanel panel, string leadTitle)
        {
            var host = new CardHost { BackColor = UiTheme.PageBackground };
            var page = new SettingsPage { Dock = DockStyle.Fill };
            host.Page = page;
            GlassBlur.ApplyScrollbarTheme(page, UiTheme.Background.GetBrightness() < 0.5f);
            var items = panel.Controls.Cast<Control>().ToList();
            var sections = PageCards.Split(items, leadTitle, out var intro);
            string key = _currentPageKey;
            foreach (var sec in sections)
            {
                var card = new SettingsCard { Title = sec.Title, ShowReset = sec.Reset != null };
                if (sec.Reset != null)
                {
                    var reset = sec.Reset; var title = sec.Title;
                    card.ResetRequested += (_, _) =>
                    {
                        if (MessageBox.Show(FindForm(), LocalizationManager.T("Settings.ResetConfirm"), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        reset();
                        if (_pageBuilders.TryGetValue(key, out var rebuild)) rebuild();
                    };
                }
                page.AddCard(card);
                foreach (var c in sec.Main) { KeepSize(c); card.AddControl(c); }
                if (sec.Advanced.Count > 0)
                {
                    var adv = new AdvancedSection();
                    foreach (var c in sec.Advanced) { KeepSize(c); adv.AddControl(c); }
                    card.Add(adv);
                }
            }
            var top = new Panel { Dock = DockStyle.Top, Height = string.IsNullOrEmpty(intro) ? 40 : 78, BackColor = UiTheme.PageBackground };
            if (!string.IsNullOrEmpty(intro))
                top.Controls.Add(new Label { Text = intro, Dock = DockStyle.Fill, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Padding = new Padding(8, 0, 8, 0), AutoEllipsis = true });
            top.Controls.Add(new SettingsSearchBox { Dock = DockStyle.Bottom, Page = page, Font = UiTheme.FontBody, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary });
            host.Controls.Add(page);
            host.Controls.Add(top);
            // timers etc. that the page builder hooked to panel.Disposed must still be released when the page goes away
            host.Disposed += (_, _) => panel.Dispose();
            return host;
        }
    }
}

using System.Drawing;

namespace GlassSpike2;

internal static class Stage
{
    public static void Start(string page, ManualResetEventSlim ready)
    {
        var t = new Thread(() =>
        {
            var form = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 1700, 900), TopMost = true, ShowInTaskbar = false };
            var wv = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
            form.Controls.Add(wv);
            form.Shown += async (_, _) =>
            {
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "glassspike2-wv"));
                await wv.EnsureCoreWebView2Async(env);
                wv.CoreWebView2.NavigationCompleted += (_, _) => ready.Set();
                wv.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
            };
            System.Windows.Forms.Application.Run(form);
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }
}

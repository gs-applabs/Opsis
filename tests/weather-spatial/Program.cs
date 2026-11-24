using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var wallpaper = Path.GetFullPath(args[0]);
        var results = Directory.CreateDirectory(args[1]);
        // Hosted Windows runners have no dedicated GPU. SwiftShader exercises the
        // WebView2 WebGL2 implementation and never changes Opsis runtime options.
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            "--use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader");
        Application.EnableVisualStyles();
        using var form = new Form { ClientSize = new Size(960, 540) };
        using var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        int status = 1;
        form.Shown += async (_, _) =>
        {
            try
            {
                await web.EnsureCoreWebView2Async().WaitAsync(TimeSpan.FromSeconds(45));
                var core = web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("weather.test", wallpaper, CoreWebView2HostResourceAccessKind.DenyCors);
                var loaded = new TaskCompletionSource<bool>();
                core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                core.Navigate("https://weather.test/index.html");
                if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new Exception("Wallpaper navigation failed");
                var cities = File.ReadAllText(Path.Combine(wallpaper, "scenes", "capitals.json"));
                await core.ExecuteScriptAsync($"OpsisLandmarks.setCities({cities})");
                foreach (var id in new[] { "parthenon", "whitetower", "rioantirrio", "patras-lighthouse", "nafplio-bourtzi" })
                {
                    var result = await core.ExecuteScriptAsync($$"""
                        (() => {
                          const c = document.getElementById('test-frame') || document.createElement('canvas');
                          c.id='test-frame'; c.width=innerWidth; c.height=innerHeight;
                          c.style='position:fixed;inset:0;z-index:10000'; document.body.appendChild(c);
                          WeatherWallpaper.resize(c.width,c.height);
                          OpsisWeatherSpatial.setMode('3D');
                          WeatherWallpaper.setConfig({realistic:true,landmarks:true,reducedMotion:false,landmarkId:'{{id}}',spatialMode:'3D',timeSim:'Day',weatherSim:'Clear'});
                          let sample=null, requested=null;
                          const renderer=window.OpsisWeatherSpatial;
                          const update=renderer.update;
                          renderer.update=(...args)=>{
                            requested={quality:args[2],reduced:args[3],id:args[4]?.id,mode:renderer.mode()};
                            const surface=update(...args);
                            if (surface) {
                              const gl=surface.getContext('webgl2'), px=new Uint8Array(4);
                              gl.readPixels(gl.drawingBufferWidth>>1,gl.drawingBufferHeight>>2,1,1,gl.RGBA,gl.UNSIGNED_BYTE,px);
                              sample={rgba:[...px],error:gl.getError()};
                            }
                            return surface;
                          };
                          try { WeatherWallpaper.draw(c.getContext('2d'),.033,{id:'High'}); }
                          finally { renderer.update=update; }
                          return requested?.id==='{{id}}' && sample?.rgba[3]===255 && sample.error===0 ? true :
                            {sample,requested,location:OpsisLandmarks.byId('{{id}}'),size:[c.width,c.height],status:renderer.failureReason()};
                        })()
                        """);
                    if (result != "true") throw new Exception($"Missing 3D foreground for {id}: {result}");
                    await Task.Delay(150);
                    using var png = File.Create(Path.Combine(results.FullName, id + ".png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
                }
                foreach (var mode in new[] { "2.5D", "Off" })
                {
                    var cleared = await core.ExecuteScriptAsync($"OpsisWeatherSpatial.setMode({JsonSerializer.Serialize(mode)}); !document.getElementById('opsis-weather-spatial')");
                    if (cleared != "true") throw new Exception("Unexpected GL layer in " + mode);
                }
                status = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { form.Close(); }
        };
        Application.Run(form);
        return status;
    }
}

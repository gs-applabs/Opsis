// Smoke + measurement of the PRODUCT library Opsis.Glass: four glass panes over the live Opsis wallpaper page, then the policy states
// (live / Low tier static / locked / back). Not shipped.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Opsis.Glass;
using GlassDemo;

var outFile = args.Length > 0 ? args[0] : "demo.txt";
var shots = args.Length > 1 ? args[1] : Path.GetDirectoryName(Path.GetFullPath(outFile))!;
File.WriteAllText(outFile, "");
void Log(string s) { var l = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s; Console.WriteLine(l); File.AppendAllText(outFile, l + Environment.NewLine); }

var page = Environment.GetEnvironmentVariable("SPIKE_STAGE_PAGE");
if (!string.IsNullOrEmpty(page) && File.Exists(page)) { var ready = new ManualResetEventSlim(); Stage.Start(page, ready); Log(ready.Wait(15000) ? "stage loaded" : "stage NOT loaded"); Thread.Sleep(2500); }

using var glass = new GlassManager();
var ids = new List<int>();
for (var i = 0; i < 4; i++) ids.Add(glass.AddPane(new Rectangle(120 + i * 330, 200, 320, 240), new GlassStyle()));
glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = true });
Thread.Sleep(1500);

var proc = Process.GetCurrentProcess();
var cores = Environment.ProcessorCount;
(double cpu, GlassStats s0, GlassStats s1, double secs) Run(int ms)
{
    var a = glass.Stats; proc.Refresh(); var c0 = proc.TotalProcessorTime; var sw = Stopwatch.StartNew();
    Thread.Sleep(ms);
    sw.Stop(); proc.Refresh();
    return ((proc.TotalProcessorTime - c0).TotalSeconds / sw.Elapsed.TotalSeconds / cores * 100, a, glass.Stats, sw.Elapsed.TotalSeconds);
}

var live = Run(8000);
Log($"LIVE High, 4 panes: {(live.s1.Renders - live.s0.Renders) / live.secs:0.0} pane-renders/s, {(live.s1.Captures - live.s0.Captures) / live.secs:0.0} captures/s (ONE per frame for all panes), CPU {live.cpu:0.0}%, private {proc.PrivateMemorySize64 / 1048576.0:0} MB, errors='{live.s1.LastError}'");

void Shot(string name)
{
    glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = false }); Thread.Sleep(400);   // freeze: no capture while the panes are visible to capture tools (else they would see themselves)
    glass.SetVisibleToCaptureTools(true); Thread.Sleep(500);
    using var bmp = new Bitmap(1500, 340, PixelFormat.Format32bppRgb);
    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(80, 160, 0, 0, bmp.Size);
    bmp.Save(Path.Combine(shots, name + ".png"), ImageFormat.Png);
    glass.SetVisibleToCaptureTools(false);
}
Shot("product-live");

glass.SetConditions(new GlassConditions { Tier = GlassTier.Balanced, LiveBackdrop = true });
var bal = Run(4000);
Log($"LIVE Balanced (blur only, 30 fps cap): {(bal.s1.Captures - bal.s0.Captures) / bal.secs:0.0} captures/s, CPU {bal.cpu:0.0}%");
Shot("product-balanced");

glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = false });          // a STATIC wallpaper: nothing changes behind
Thread.Sleep(800);
var idle = Run(4000);
Log($"STATIC background (nothing changing): {(idle.s1.Captures - idle.s0.Captures) / idle.secs:0.00} captures/s, {(idle.s1.Renders - idle.s0.Renders) / idle.secs:0.00} renders/s, CPU {idle.cpu:0.00}%");

glass.SetConditions(new GlassConditions { Tier = GlassTier.Low, LiveBackdrop = true });
Thread.Sleep(800);
var low = Run(3000);
Log($"LOW tier: {(low.s1.Captures - low.s0.Captures) / low.secs:0.00} captures/s (static frosted tint), CPU {low.cpu:0.00}%");
Shot("product-low");

glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = true, Locked = true });
Thread.Sleep(800);
var locked = Run(3000);
Log($"LOCKED (suspended): {(locked.s1.Captures - locked.s0.Captures) / locked.secs:0.00} captures/s, CPU {locked.cpu:0.00}%");

glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = true });
Thread.Sleep(1000);
var back = Run(3000);
Log($"RESUMED: {(back.s1.Captures - back.s0.Captures) / back.secs:0.0} captures/s");

// move a pane: the window motion alone must trigger captures on a static background
glass.SetConditions(new GlassConditions { Tier = GlassTier.High, LiveBackdrop = false, WindowMovingBehind = true });
Thread.Sleep(500);
var st = glass.Stats;
Log($"stats: captures {st.Captures}, renders {st.Renders}, static renders {st.StaticRenders}, skipped {st.SkippedNoChange}, device resets {st.DeviceResets}, panes {st.Panes}, lastError='{st.LastError}'");
Log("done");

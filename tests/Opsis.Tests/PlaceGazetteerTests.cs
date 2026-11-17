using System.Net;
using System.Text;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

/// <summary>Plan 0B.5: place names from an offline table (GeoNames, Greece) and an opt-in online lookup. No network.</summary>
public sealed class PlaceGazetteerTests
{
    private static PlaceGazetteer Tiny() => new(new[]
    {
        "A1\tESYE31\tΑττική\tAttica", "A2\tESYE31\t99\tΝομός Αττικής\tNomos Attikis",
        "P\tΑθήνα\tAthens\t37.9838\t23.7275\tESYE31\t99\tPPLC",
        "P\tΑμπελόκηποι\tAmpelokipoi\t37.9891\t23.7642\tESYE31\t99\tPPLX",
        "P\tΆγιος Δημήτριος\tAgios Dimitrios\t37.9333\t23.7333\tESYE31\t99\tPPLA3",
    });

    [Fact]
    public void ANeighbourhoodCarriesItsTownAndASettlementItsRegion()
    {
        var g = Tiny();
        var n = g.Find(37.9890, 23.7650)!;
        Assert.Equal("Αθήνα — Αμπελόκηποι", n.Display(true));
        Assert.Equal("Athens — Ampelokipoi", n.Display(false));
        Assert.Equal("Αμπελόκηποι · Αθήνα · Νομός Αττικής · Αττική", n.Full(true));
        var d = g.Find(37.9340, 23.7330)!;
        Assert.Equal("Άγιος Δημήτριος, Αττική", d.Display(true));
        Assert.Equal("Agios Dimitrios, Attica", d.Display(false));
        Assert.Equal(PlaceGazetteer.SourceName, d.Source);
    }

    [Fact]
    public void OutsideTheTableGivesNoNameAndNothingIsInvented()
    {
        var g = Tiny();
        Assert.Null(g.Find(51.5, -0.12));     // London
        Assert.Null(g.Find(35.0, 18.0));      // open sea, nothing within 8 km
    }

    [Fact]
    public void TheRealTableKnowsGreeceFromNeighbourhoodsToIslands()
    {
        var g = PlaceGazetteer.Default();
        Assert.InRange(g.Count, 10000, 30000);
        Assert.Contains("Αμπελόκηποι", g.Find(37.9891, 23.7642)!.Display(true));
        Assert.Equal("Άγιος Δημήτριος", g.Find(37.9333, 23.7333)!.NameEl);
        Assert.Equal("Θεσσαλονίκη", g.Find(40.6407, 22.9349)!.NameEl);
        Assert.Equal("Οία", g.Find(36.4626, 25.3764)!.NameEl);          // an island village
        Assert.Equal("Oia", g.Find(36.4626, 25.3764)!.NameEn);
        Assert.Null(g.Find(51.5, -0.12));                                  // outside Greece
        Assert.NotNull(g.Find(37.9891, 23.7642)!.RegionEl);
        Assert.Same(g, PlaceGazetteer.Default());                          // shared while it lives
    }

    // ---- online lookup (opt-in)
    private const string Osm = """{"address":{"suburb":"Αμπελόκηποι","city":"Αθήνα","state":"Αττική","country":"Ελλάδα"}}""";

    [Fact]
    public void TheOnlineAddressBecomesNeighbourhoodAndTown()
    {
        Assert.Equal("Αθήνα — Αμπελόκηποι", PlaceReverse.Parse(Osm));
        Assert.Equal("Σαντορίνη", PlaceReverse.Parse("""{"address":{"village":"Σαντορίνη"}}"""));
        Assert.Null(PlaceReverse.Parse("""{"address":{"country":"x"}}"""));
        Assert.Null(PlaceReverse.Parse("not json"));
    }

    [Fact]
    public void TheOnlineLookupIsRoundedCachedAndNeverFasterThanOnePerSecond()
    {
        var urls = new List<string>(); var times = new List<DateTime>();
        var handler = new Handler(r => { urls.Add(r.RequestUri!.ToString()); times.Add(DateTime.UtcNow); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Osm, Encoding.UTF8, "application/json") }; });
        using var http = WeatherHttp.CreateClient(handler);
        var rev = new PlaceReverse(http);
        Assert.Equal("Αθήνα — Αμπελόκηποι", rev.NameAsync(37.98912345, 23.76421234, true, default).Result);
        Assert.Equal("Αθήνα — Αμπελόκηποι", rev.NameAsync(37.98912345, 23.76421234, true, default).Result);   // cached: no second request
        rev.NameAsync(38.5, 24.5, true, default).Wait();
        Assert.Equal(2, urls.Count);
        Assert.Contains("lat=37.99&lon=23.76", urls[0]);                       // rounded to 2 decimals
        Assert.Contains("accept-language=el", urls[0]);
        Assert.StartsWith("Opsis/", handler.UserAgents[0]);                    // identifying User-Agent
        Assert.True((times[1] - times[0]).TotalMilliseconds >= 900);           // 1 request per second at most
    }

    [Fact]
    public async Task TheOfflineTableIsUsedFirstAndTheOnlineLookupOnlyWhenAllowed()
    {
        var off = await PlaceNaming.ResolveAsync(37.9891, 23.7642, true, onlineAllowed: false, "My location");
        Assert.Equal(PlaceGazetteer.SourceName, off.Source);
        Assert.Contains("Αμπελόκηποι", off.Name);
        var none = await PlaceNaming.ResolveAsync(51.5, -0.12, true, onlineAllowed: false, "My location (approx.)");
        Assert.Equal(("My location (approx.)", "My location (approx.)", ""), none);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<string> UserAgents = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { UserAgents.Add(r.Headers.UserAgent.ToString()); return Task.FromResult(f(r)); }
    }
}

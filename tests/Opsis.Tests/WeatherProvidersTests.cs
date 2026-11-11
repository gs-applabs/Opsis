using System.Net;
using System.Text;
using System.Text.Json;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

/// <summary>Plan 0B.5: weather providers behind one interface. All mocked, no network.</summary>
public sealed class WeatherProvidersTests
{
    private sealed class FuncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        public int Calls;
        public List<HttpRequestMessage> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); Requests.Add(request); return f(request); }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeProvider(string name, Func<Task<WeatherObservation?>> f, bool covers = true) : IWeatherProvider
    {
        public string Name => name;
        public bool Covers(double lat, double lon) => covers;
        public Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct) => f();
    }

    private static WeatherObservation Obs(string provider, double temp = 15, int code = 1) =>
        new(provider, new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc), temp, 10, 270, null, 50, 20, 0, null, code, 0, true);

    // ---- Open-Meteo
    private const string OpenMeteoSample = """
        {"utc_offset_seconds":7200,"current":{"time":"2026-11-11T12:15","interval":900,"temperature_2m":18.4,"relative_humidity_2m":61,"precipitation":0.4,"weather_code":63,"cloud_cover":90,"wind_speed_10m":22.5,"wind_direction_10m":200,"wind_gusts_10m":41.0,"visibility":9000}}
        """;

    [Fact]
    public void OpenMeteoParsesEveryFieldAndConvertsTheLocalTimeToUtc()
    {
        var o = OpenMeteoProvider.Parse(OpenMeteoSample)!;
        Assert.Equal(18.4, o.TempC); Assert.Equal(63, o.WmoCode); Assert.Equal(41.0, o.GustKmh); Assert.Equal(9000, o.VisibilityM);
        Assert.Equal(new DateTime(2026, 11, 11, 10, 15, 0), o.ObservedUtc);   // 12:15 local at +02:00
        Assert.Equal(7200, o.UtcOffsetSec);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"error\":true,\"reason\":\"bad\"}")]
    [InlineData("{\"current\":{\"weather_code\":3}}")]   // no temperature
    public void OpenMeteoBadPayloadsGiveNull(string json) => Assert.Null(OpenMeteoProvider.Parse(json));

    [Fact]
    public void ProviderUrlsUseRoundedInvariantCoordinatesAndNoKey()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("el-GR");
            string om = OpenMeteoProvider.BuildUrl(37.98381234, 23.72754321);
            string met = MetNoProvider.BuildUrl(37.98381234, 23.72754321);
            Assert.Contains("latitude=37.98&longitude=23.73", om);
            Assert.Contains("lat=37.98&lon=23.73", met);
            Assert.DoesNotContain("key", om, StringComparison.OrdinalIgnoreCase);
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    // ---- MET Norway
    private static string MetSample(string firstTime, string secondTime) => """
        {"properties":{"timeseries":[
          {"time":"@@T1@@","data":{"instant":{"details":{"air_temperature":9.5,"wind_speed":5.0,"wind_from_direction":310,"relative_humidity":80,"cloud_area_fraction":100}},"next_1_hours":{"summary":{"symbol_code":"heavyrainshowers_day"},"details":{"precipitation_amount":3.2}}}},
          {"time":"@@T2@@","data":{"instant":{"details":{"air_temperature":1.0,"wind_speed":1.0}},"next_1_hours":{"summary":{"symbol_code":"clearsky_night"},"details":{"precipitation_amount":0}}}}]}}
        """.Replace("@@T1@@", firstTime).Replace("@@T2@@", secondTime);

    [Fact]
    public void MetNorwayPicksTheCurrentStepAndConvertsWindToKmh()
    {
        var now = new DateTime(2026, 11, 11, 10, 30, 0, DateTimeKind.Utc);
        var o = MetNoProvider.Parse(MetSample("2026-11-11T10:00:00Z", "2026-11-11T11:00:00Z"), now)!;
        Assert.Equal(9.5, o.TempC); Assert.Equal(18.0, o.WindKmh, 3); Assert.Equal(65, o.WmoCode); Assert.Equal(3.2, o.PrecipMmPerHour); Assert.True(o.IsModel);
        var later = MetNoProvider.Parse(MetSample("2026-11-11T10:00:00Z", "2026-11-11T11:00:00Z"), now.AddHours(1))!;
        Assert.Equal(1.0, later.TempC); Assert.Equal(0, later.WmoCode);
    }

    [Theory]
    [InlineData("clearsky_day", 0)] [InlineData("fair_night", 1)] [InlineData("partlycloudy_polartwilight", 2)] [InlineData("cloudy", 3)] [InlineData("fog", 45)]
    [InlineData("lightrain", 61)] [InlineData("rainshowers_day", 63)] [InlineData("heavyrain", 65)] [InlineData("lightsnow", 71)] [InlineData("snow", 73)] [InlineData("heavysnowshowers_night", 75)]
    [InlineData("sleet", 66)] [InlineData("heavysleet", 67)] [InlineData("rainandthunder", 95)] [InlineData("heavyrainandthunder", 99)] [InlineData("lightsnowandthunder", 95)]
    public void MetSymbolCodesMapToTheMatchingWmoIntensity(string symbol, int wmo) => Assert.Equal(wmo, MetNoProvider.WmoFromSymbol(symbol));

    [Fact]
    public async Task MetNorwaySendsUserAgentRespectsExpiresAndUsesIfModifiedSince()
    {
        var now = new DateTime(2026, 11, 11, 10, 30, 0, DateTimeKind.Utc);
        int n = 0;
        var handler = new FuncHandler(req =>
        {
            n++;
            if (n == 1)
            {
                var r = Json(MetSample("2026-11-11T10:00:00Z", "2026-11-11T11:00:00Z"));
                r.Content.Headers.Expires = new DateTimeOffset(now.AddMinutes(20));
                r.Content.Headers.LastModified = new DateTimeOffset(now.AddMinutes(-5));
                return Task.FromResult(r);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        var clock = now;
        using var http = WeatherHttp.CreateClient(handler);
        var p = new MetNoProvider(http, () => clock);

        Assert.NotNull(await p.GetCurrentAsync(37.9838, 23.7275, default));
        Assert.Contains("Opsis/", handler.Requests[0].Headers.UserAgent.ToString());
        Assert.NotNull(await p.GetCurrentAsync(37.9838, 23.7275, default));
        Assert.Equal(1, handler.Calls);                       // inside Expires: no request at all

        clock = now.AddMinutes(30);
        Assert.NotNull(await p.GetCurrentAsync(37.9838, 23.7275, default));
        Assert.Equal(2, handler.Calls);
        Assert.NotNull(handler.Requests[1].Headers.IfModifiedSince);   // conditional request; 304 served from the cached body
    }

    // ---- NWS
    private static string NwsObs(string timestamp, string text = "Mostly Cloudy", string? tempJson = null) => """
        {"properties":{"timestamp":"@@TS@@","textDescription":"@@TX@@","temperature":@@TEMP@@,
         "windSpeed":{"unitCode":"wmoUnit:km_h-1","value":18.5},"windGust":{"unitCode":"wmoUnit:km_h-1","value":null},"windDirection":{"value":240},
         "relativeHumidity":{"value":70.2},"visibility":{"value":16090},"precipitationLastHour":{"value":null},
         "cloudLayers":[{"amount":"FEW"},{"amount":"BKN"}]}}
        """.Replace("@@TS@@", timestamp).Replace("@@TX@@", text).Replace("@@TEMP@@", tempJson ?? "{\"unitCode\":\"wmoUnit:degC\",\"value\":12.3}");

    [Fact]
    public void NwsParsesStationObservationAndRejectsOldOnes()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var o = NwsProvider.Parse(NwsObs("2026-11-11T11:53:00+00:00"), now)!;
        Assert.Equal(12.3, o.TempC); Assert.Equal(18.5, o.WindKmh); Assert.Equal(75, o.CloudPct); Assert.Equal(3, o.WmoCode); Assert.False(o.IsModel);
        Assert.Null(o.GustKmh);
        Assert.Null(NwsProvider.Parse(NwsObs("2026-11-11T07:00:00+00:00"), now));                                                 // 5 h old
        Assert.Null(NwsProvider.Parse(NwsObs("2026-11-11T11:53:00+00:00", tempJson: "{\"value\":null}"), now));                    // no temperature
        var f = NwsProvider.Parse(NwsObs("2026-11-11T11:53:00+00:00", tempJson: "{\"unitCode\":\"wmoUnit:degF\",\"value\":50}"), now)!;
        Assert.Equal(10.0, f.TempC, 3);
    }

    [Theory]
    [InlineData("Clear", 0)] [InlineData("Fair", 0)] [InlineData("Mostly Clear", 1)] [InlineData("Partly Cloudy", 2)] [InlineData("Mostly Cloudy", 3)] [InlineData("Overcast", 3)]
    [InlineData("Light Rain", 61)] [InlineData("Rain", 63)] [InlineData("Heavy Rain", 65)] [InlineData("Light Drizzle", 53)] [InlineData("Snow", 73)] [InlineData("Light Snow", 71)]
    [InlineData("Heavy Snow", 75)] [InlineData("Fog/Mist", 45)] [InlineData("Thunderstorm in Vicinity", 95)] [InlineData("Freezing Rain", 67)]
    public void NwsWordsMapToWmoCodes(string text, int wmo) => Assert.Equal(wmo, NwsProvider.WmoFromText(text));

    [Fact]
    public void NwsCoversOnlyUsTerritory()
    {
        var p = new NwsProvider(new HttpClient());
        Assert.True(p.Covers(40.7, -74.0)); Assert.True(p.Covers(61.2, -149.9)); Assert.True(p.Covers(21.3, -157.8));
        Assert.False(p.Covers(37.98, 23.73)); Assert.False(p.Covers(51.5, -0.1));
    }

    [Fact]
    public async Task NwsFlowUsesTheNextStationWhenTheNearestIsSilentAndTheLocalTimeZone()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var handler = new FuncHandler(req =>
        {
            string u = req.RequestUri!.ToString();
            if (u.Contains("/points/")) return Task.FromResult(Json("""{"properties":{"timeZone":"UTC","observationStations":"https://api.weather.gov/gridpoints/OKX/33,35/stations"}}"""));
            if (u.EndsWith("/stations")) return Task.FromResult(Json("""{"features":[{"properties":{"stationIdentifier":"KAAA"}},{"properties":{"stationIdentifier":"KBBB"}}]}"""));
            if (u.Contains("KAAA")) return Task.FromResult(Json(NwsObs("2026-11-10T01:00:00+00:00")));      // stale
            return Task.FromResult(Json(NwsObs("2026-11-11T11:55:00+00:00", "Light Rain")));
        });
        using var http = WeatherHttp.CreateClient(handler);
        var o = await new NwsProvider(http, () => now).GetCurrentAsync(40.71, -74.0, default);
        Assert.NotNull(o); Assert.Equal(61, o!.WmoCode); Assert.Equal(0, o.UtcOffsetSec);
        Assert.All(handler.Requests, r => Assert.StartsWith("https://api.weather.gov/", r.RequestUri!.ToString()));
    }

    // ---- hub
    [Fact]
    public async Task HubFallsBackInOrderAndReportsAttempts()
    {
        var hub = new WeatherHub(new IWeatherProvider[]
        {
            new FakeProvider("A", () => Task.FromResult<WeatherObservation?>(null)),
            new FakeProvider("B", () => throw new InvalidOperationException("boom")),
            new FakeProvider("C", () => Task.FromResult<WeatherObservation?>(Obs("C"))),
            new FakeProvider("D", () => Task.FromResult<WeatherObservation?>(Obs("D"))),
        });
        var r = await hub.GetAsync(37.98, 23.73);
        Assert.Equal("C", r.Observation!.Provider);
        Assert.Equal(new[] { "A: no data", "B: InvalidOperationException", "C: ok" }, r.Attempts);
    }

    [Fact]
    public async Task HubSkipsProvidersThatDoNotCoverThePlaceAndRejectsImplausibleReadings()
    {
        var hub = new WeatherHub(new IWeatherProvider[]
        {
            new FakeProvider("US only", () => Task.FromResult<WeatherObservation?>(Obs("US only")), covers: false),
            new FakeProvider("Broken", () => Task.FromResult<WeatherObservation?>(Obs("Broken", temp: 999))),
            new FakeProvider("Good", () => Task.FromResult<WeatherObservation?>(Obs("Good"))),
        });
        var r = await hub.GetAsync(37.98, 23.73);
        Assert.Equal("Good", r.Observation!.Provider);
        Assert.DoesNotContain(r.Attempts, a => a.StartsWith("US only"));
        Assert.Contains("Broken: implausible", r.Attempts);
    }

    [Fact]
    public async Task HubTimeoutMovesToTheNextProvider()
    {
        var hub = new WeatherHub(new IWeatherProvider[]
        {
            new SlowProvider(),
            new FakeProvider("Fast", () => Task.FromResult<WeatherObservation?>(Obs("Fast"))),
        }, TimeSpan.FromMilliseconds(150));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await hub.GetAsync(37.98, 23.73);
        Assert.Equal("Fast", r.Observation!.Provider);
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }

    private sealed class SlowProvider : IWeatherProvider
    {
        public string Name => "Slow";
        public bool Covers(double lat, double lon) => true;
        public async Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct) { await Task.Delay(TimeSpan.FromSeconds(30), ct); return Obs("Slow"); }
    }

    [Fact]
    public async Task OfflineAndBadJsonEverywhereGivesNullWithoutThrowing()
    {
        var offline = new FuncHandler(_ => throw new HttpRequestException("offline"));
        var garbage = new FuncHandler(_ => Task.FromResult(Json("<html>nope</html>")));
        foreach (var h in new[] { offline, garbage })
        {
            using var http = WeatherHttp.CreateClient(h);
            var hub = new WeatherHub(new IWeatherProvider[] { new NwsProvider(http), new OpenMeteoProvider(http), new MetNoProvider(http) });
            var r = await hub.GetAsync(40.71, -74.0);
            Assert.Null(r.Observation);
            Assert.Equal(3, r.Attempts.Count);
        }
    }

    [Fact]
    public async Task HubRejectsInvalidCoordinates() => Assert.Null((await new WeatherHub(Array.Empty<IWeatherProvider>()).GetAsync(123, 400)).Observation);

    // ---- sun, moon, payload
    [Fact]
    public void SunIsHighAtNoonAndMidnightSunAndPolarNightWork()
    {
        Assert.InRange(SolarCalc.SunElevation(new DateTime(2026, 6, 21, 10, 25, 0, DateTimeKind.Utc), 37.98, 23.73), 73.5, 77.5);   // Athens, solstice solar noon
        Assert.True(SolarCalc.IsDay(new DateTime(2026, 6, 21, 22, 0, 0, DateTimeKind.Utc), 69.65, 18.96));                            // Tromso midnight sun
        Assert.False(SolarCalc.IsDay(new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc), 69.65, 18.96));                          // polar night at noon
        Assert.False(SolarCalc.IsDay(new DateTime(2026, 11, 11, 23, 0, 0, DateTimeKind.Utc), 37.98, 23.73));
    }

    [Fact]
    public void SunriseAndSunsetAreOrderedAndDayLengthIsPlausible()
    {
        var now = new DateTime(2026, 3, 20, 0, 30, 0, DateTimeKind.Utc);   // equinox, equator, Greenwich: ~6:00 rise, ~18:00 set
        var (rise, set) = SolarCalc.NextSunEvents(now, 0, 0);
        Assert.NotNull(rise); Assert.NotNull(set);
        Assert.InRange((rise!.Value - now).TotalHours, 5.0, 5.9);
        Assert.InRange((set!.Value - rise.Value).TotalHours, 11.8, 12.3);
        var polar = SolarCalc.NextSunEvents(new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc), 80, 0);   // polar night: none in 36 h
        Assert.Null(polar.Sunrise); Assert.Null(polar.Sunset);
    }

    [Fact]
    public void MoonPhaseMatchesKnownNewAndFullMoons()
    {
        double Dist(double a, double b) { double d = Math.Abs(a - b); return Math.Min(d, 1 - d); }
        Assert.True(Dist(SolarCalc.MoonPhase(new DateTime(2024, 1, 11, 12, 0, 0, DateTimeKind.Utc)), 0.0) < 0.03);   // new moon 2024-01-11
        Assert.True(Dist(SolarCalc.MoonPhase(new DateTime(2024, 1, 25, 18, 0, 0, DateTimeKind.Utc)), 0.5) < 0.03);   // full moon 2024-01-25
    }

    [Fact]
    public void PayloadKeepsTheOldKeysAndAddsSourceAndObservationTime()
    {
        var now = new DateTime(2026, 11, 11, 10, 20, 0, DateTimeKind.Utc);
        using var doc = JsonDocument.Parse(WeatherPayload.Build(Obs("Open-Meteo", 18.4, 63), 37.98, 23.73, now, stale: true));
        var r = doc.RootElement;
        foreach (var k in new[] { "ok", "code", "temp", "humidity", "precip", "cloud", "wind", "windDir", "isDay", "utcOffsetSec", "source" }) Assert.True(r.TryGetProperty(k, out _), k);
        Assert.Equal("Open-Meteo", r.GetProperty("source").GetString());
        Assert.True(r.GetProperty("stale").GetBoolean());
        Assert.True(r.GetProperty("isDay").GetBoolean());
        Assert.Equal(DateTimeKind.Utc, DateTime.Parse(r.GetProperty("observedAt").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).Kind);
        Assert.InRange(r.GetProperty("moonPhase").GetDouble(), 0, 1);
    }

    [Fact]
    public void ProvidersWithoutALocalZoneGetAnOffsetFromTheClockOrTheLongitude()
    {
        var now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        int pc = (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalSeconds;
        Assert.Equal(pc, WeatherPayload.GuessOffsetSeconds(pc / 3600.0 * 15, now));
        int far = WeatherPayload.GuessOffsetSeconds(pc / 3600.0 * 15 + 150, now);   // ten zones away: solar zone of the longitude
        Assert.Equal((int)Math.Round((pc / 3600.0 * 15 + 150) / 15.0) * 3600, far);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    /// <summary>One normalised current-conditions reading, whatever provider it came from. Codes are WMO codes (the wallpaper scene table keys on them).</summary>
    public sealed record WeatherObservation(
        string Provider, DateTime ObservedUtc, double TempC, double WindKmh, double WindDirDeg, double? GustKmh,
        double? HumidityPct, double? CloudPct, double PrecipMmPerHour, double? VisibilityM, int WmoCode, int? UtcOffsetSec, bool IsModel);

    /// <summary>One free weather source. Implementations never throw to the caller for network / JSON problems: they return null.</summary>
    public interface IWeatherProvider
    {
        string Name { get; }
        bool Covers(double lat, double lon);
        Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct);
    }

    /// <summary>Coordinates are sent to providers rounded to 2 decimals (about 1 km): enough for weather, not an exact position.</summary>
    public static class WeatherCoord
    {
        public static double Round(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        public static string Inv(double v) => Round(v).ToString("0.##", CultureInfo.InvariantCulture);
        public static bool Plausible(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
    }

    public static class WeatherHttp
    {
        /// <summary>Identifying User-Agent (MET Norway and NWS require one). No personal data, only the app name and the public project page.</summary>
        public static string UserAgent => $"Opsis/{Version()} (+https://github.com/gs-applabs/Opsis)";

        private static string Version()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0);
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }

        public static HttpClient CreateClient(HttpMessageHandler? handler = null)
        {
            var c = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return c;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    public sealed class OpenMeteoProvider : IWeatherProvider
    {
        private readonly HttpClient _http;
        public OpenMeteoProvider(HttpClient http) { _http = http; }
        public string Name => "Open-Meteo";
        public bool Covers(double lat, double lon) => true;

        // No "models" parameter on purpose: the default "best_match" picks, per location, the highest resolution regional model
        // (e.g. AROME / ICON-D2 / UKMO / HRRR) over the global ones (documented at open-meteo.com/en/docs).
        public static string BuildUrl(double lat, double lon) =>
            $"https://api.open-meteo.com/v1/forecast?latitude={WeatherCoord.Inv(lat)}&longitude={WeatherCoord.Inv(lon)}" +
            "&current=temperature_2m,relative_humidity_2m,precipitation,weather_code,cloud_cover,wind_speed_10m,wind_direction_10m,wind_gusts_10m,visibility&timezone=auto";

        public async Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(BuildUrl(lat, lon), ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                return Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            catch (Exception) { return null; }
        }

        public static WeatherObservation? Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("current", out var cur) || cur.ValueKind != JsonValueKind.Object) return null;
                double? Num(string n) => cur.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                var temp = Num("temperature_2m");
                if (temp == null) return null;
                int offset = root.TryGetProperty("utc_offset_seconds", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
                DateTime observed = DateTime.UtcNow;
                if (cur.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var local))
                    observed = local.AddSeconds(-offset);   // "time" is local wall clock because timezone=auto
                return new WeatherObservation("Open-Meteo", observed, temp.Value, Num("wind_speed_10m") ?? 0, Num("wind_direction_10m") ?? 270, Num("wind_gusts_10m"),
                    Num("relative_humidity_2m"), Num("cloud_cover"), Num("precipitation") ?? 0, Num("visibility"), (int)(Num("weather_code") ?? 0), offset, IsModel: true);
            }
            catch (Exception) { return null; }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    /// <summary>MET Norway locationforecast 2.0 (global, free, CC BY 4.0). Terms: identifying User-Agent, at most 4 decimals, respect Expires / If-Modified-Since.</summary>
    public sealed class MetNoProvider : IWeatherProvider
    {
        private sealed class CacheEntry { public string Body = ""; public string? LastModified; public DateTime ExpiresUtc; }
        private readonly HttpClient _http;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly object _lock = new();
        public MetNoProvider(HttpClient http, Func<DateTime>? utcNow = null) { _http = http; _utcNow = utcNow ?? (() => DateTime.UtcNow); }
        public string Name => "MET Norway";
        public bool Covers(double lat, double lon) => true;

        public static string BuildUrl(double lat, double lon) =>
            $"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={WeatherCoord.Inv(lat)}&lon={WeatherCoord.Inv(lon)}";

        public async Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct)
        {
            string key = BuildUrl(lat, lon);
            try
            {
                CacheEntry? cached;
                lock (_lock) _cache.TryGetValue(key, out cached);
                if (cached != null && _utcNow() < cached.ExpiresUtc) return Parse(cached.Body, _utcNow());   // not before "Expires" (terms of service)

                using var req = new HttpRequestMessage(HttpMethod.Get, key);
                if (cached?.LastModified != null && DateTimeOffset.TryParse(cached.LastModified, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lm))
                    req.Headers.IfModifiedSince = lm;
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                string body;
                if (resp.StatusCode == HttpStatusCode.NotModified && cached != null) body = cached.Body;
                else if (!resp.IsSuccessStatusCode) return cached == null ? null : Parse(cached.Body, _utcNow());
                else body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                var entry = new CacheEntry { Body = body, LastModified = resp.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture) ?? cached?.LastModified };
                entry.ExpiresUtc = resp.Content.Headers.Expires?.UtcDateTime ?? _utcNow().AddMinutes(10);
                lock (_lock) { _cache[key] = entry; if (_cache.Count > 8) _cache.Remove(_cache.Keys.First(k => k != key)); }
                return Parse(body, _utcNow());
            }
            catch (Exception) { return null; }
        }

        /// <summary>Picks the time step that is current (the last one not after <paramref name="nowUtc"/>). It is a model forecast for this hour, not a station measurement.</summary>
        public static WeatherObservation? Parse(string json, DateTime nowUtc)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries");
                JsonElement? pick = null; DateTime pickTime = default;
                foreach (var step in series.EnumerateArray())
                {
                    if (!DateTime.TryParse(step.GetProperty("time").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) continue;
                    if (t > nowUtc && pick != null) break;
                    pick = step; pickTime = t;
                }
                if (pick == null) return null;
                var data = pick.Value.GetProperty("data");
                var d = data.GetProperty("instant").GetProperty("details");
                double? Num(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                var temp = Num("air_temperature");
                if (temp == null) return null;
                string symbol = ""; double precip = 0;
                if (data.TryGetProperty("next_1_hours", out var n1))
                {
                    if (n1.TryGetProperty("summary", out var s) && s.TryGetProperty("symbol_code", out var sc)) symbol = sc.GetString() ?? "";
                    if (n1.TryGetProperty("details", out var nd) && nd.TryGetProperty("precipitation_amount", out var pa) && pa.ValueKind == JsonValueKind.Number) precip = pa.GetDouble();
                }
                return new WeatherObservation("MET Norway", pickTime, temp.Value, (Num("wind_speed") ?? 0) * 3.6, Num("wind_from_direction") ?? 270, Num("wind_speed_of_gust") is { } g ? g * 3.6 : null,
                    Num("relative_humidity"), Num("cloud_area_fraction"), precip, null, WmoFromSymbol(symbol, Num("cloud_area_fraction")), null, IsModel: true);
            }
            catch (Exception) { return null; }
        }

        /// <summary>MET symbol codes ("lightrainshowers_day", "heavysnowandthunder") to WMO codes with the same intensity.</summary>
        public static int WmoFromSymbol(string symbol, double? cloudPct = null)
        {
            string s = symbol.ToLowerInvariant();
            int us = s.IndexOf('_'); if (us >= 0) s = s[..us];
            bool thunder = s.Contains("thunder");
            bool heavy = s.StartsWith("heavy"), light = s.StartsWith("light");
            if (s.Contains("snow") && !s.Contains("sleet")) return thunder ? 95 : heavy ? 75 : light ? 71 : 73;
            if (s.Contains("sleet")) return thunder ? 95 : heavy ? 67 : 66;
            if (s.Contains("rain")) return thunder ? (heavy ? 99 : 95) : heavy ? 65 : light ? 61 : 63;
            return s switch
            {
                "clearsky" => 0,
                "fair" => 1,
                "partlycloudy" => 2,
                "cloudy" => 3,
                "fog" => 45,
                _ => cloudPct is { } c ? (c < 15 ? 0 : c < 40 ? 1 : c < 75 ? 2 : 3) : 2,
            };
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    /// <summary>US National Weather Service (api.weather.gov): the latest REAL station observation near the point. US territory only. Needs an identifying User-Agent, no key (yet).</summary>
    public sealed class NwsProvider : IWeatherProvider
    {
        private readonly HttpClient _http;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<string, string[]> _stations = new();   // rounded point -> nearest station ids (the grid rarely changes)
        private readonly Dictionary<string, string> _zones = new();        // rounded point -> IANA time zone of the point (for the local clock of the wallpaper)
        private readonly object _lock = new();
        public NwsProvider(HttpClient http, Func<DateTime>? utcNow = null) { _http = http; _utcNow = utcNow ?? (() => DateTime.UtcNow); }
        public string Name => "NWS";
        public static readonly TimeSpan MaxObservationAge = TimeSpan.FromHours(3);

        public bool Covers(double lat, double lon) =>
            (lat is >= 24.4 and <= 49.6 && lon is >= -125 and <= -66.9) ||       // contiguous US
            (lat is >= 51 and <= 71.6 && lon is >= -170 and <= -129) ||          // Alaska
            (lat is >= 18.5 and <= 22.5 && lon is >= -161 and <= -154) ||        // Hawaii
            (lat is >= 17.5 and <= 18.7 && lon is >= -68 and <= -65);            // Puerto Rico / USVI

        public async Task<WeatherObservation?> GetCurrentAsync(double lat, double lon, CancellationToken ct)
        {
            try
            {
                string key = $"{WeatherCoord.Inv(lat)},{WeatherCoord.Inv(lon)}";
                string[]? ids;
                lock (_lock) _stations.TryGetValue(key, out ids);
                if (ids == null)
                {
                    string points = await GetAsync($"https://api.weather.gov/points/{key}", ct).ConfigureAwait(false);
                    if (points.Length == 0) return null;
                    using var pdoc = JsonDocument.Parse(points);
                    var props = pdoc.RootElement.GetProperty("properties");
                    if (props.TryGetProperty("timeZone", out var tz) && tz.GetString() is { Length: > 0 } zoneId) lock (_lock) _zones[key] = zoneId;
                    string? stationsUrl = props.GetProperty("observationStations").GetString();
                    if (string.IsNullOrEmpty(stationsUrl) || !stationsUrl.StartsWith("https://api.weather.gov/", StringComparison.Ordinal)) return null;
                    string list = await GetAsync(stationsUrl, ct).ConfigureAwait(false);
                    if (list.Length == 0) return null;
                    using var ldoc = JsonDocument.Parse(list);
                    ids = ldoc.RootElement.GetProperty("features").EnumerateArray()
                        .Select(f => f.GetProperty("properties").GetProperty("stationIdentifier").GetString() ?? "").Where(x => x.Length > 0).Take(3).ToArray();
                    lock (_lock) { _stations[key] = ids; if (_stations.Count > 8) _stations.Remove(_stations.Keys.First(k => k != key)); }
                }
                foreach (var id in ids)   // the nearest station is sometimes silent: try the next two
                {
                    string obs = await GetAsync($"https://api.weather.gov/stations/{Uri.EscapeDataString(id)}/observations/latest", ct).ConfigureAwait(false);
                    var parsed = obs.Length == 0 ? null : Parse(obs, _utcNow());
                    if (parsed != null) return parsed with { UtcOffsetSec = ZoneOffsetSeconds(key, parsed.ObservedUtc) };
                }
                return null;
            }
            catch (Exception) { return null; }
        }

        private int? ZoneOffsetSeconds(string key, DateTime utc)
        {
            try
            {
                string? id; lock (_lock) _zones.TryGetValue(key, out id);
                return id == null ? null : (int)TimeZoneInfo.FindSystemTimeZoneById(id).GetUtcOffset(utc).TotalSeconds;
            }
            catch (Exception) { return null; }
        }

        private async Task<string> GetAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/geo+json");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : "";
        }

        public static WeatherObservation? Parse(string json, DateTime nowUtc)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var p = doc.RootElement.GetProperty("properties");
                if (!DateTime.TryParse(p.GetProperty("timestamp").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var observed)) return null;
                if (nowUtc - observed > MaxObservationAge) return null;   // an old reading is worse than the next provider
                (double? v, string unit) Q(string n)
                {
                    if (!p.TryGetProperty(n, out var q) || q.ValueKind != JsonValueKind.Object) return (null, "");
                    double? val = q.TryGetProperty("value", out var vv) && vv.ValueKind == JsonValueKind.Number ? vv.GetDouble() : null;
                    return (val, q.TryGetProperty("unitCode", out var u) ? u.GetString() ?? "" : "");
                }
                var (t, tu) = Q("temperature");
                if (t == null) return null;
                double tempC = tu.Contains("degF", StringComparison.OrdinalIgnoreCase) ? (t.Value - 32) / 1.8 : t.Value;
                double? Speed(string n) { var (v, u) = Q(n); return v == null ? null : u.Contains("m_s-1") ? v * 3.6 : u.Contains("mi_h") ? v * 1.609344 : v; }
                var (rh, _) = Q("relativeHumidity");
                var (dir, _) = Q("windDirection");
                var (vis, _) = Q("visibility");
                var (pr, _) = Q("precipitationLastHour");
                double? cloud = null;
                if (p.TryGetProperty("cloudLayers", out var layers) && layers.ValueKind == JsonValueKind.Array)
                    foreach (var l in layers.EnumerateArray())
                    {
                        double c = (l.TryGetProperty("amount", out var a) ? a.GetString() : "") switch { "CLR" or "SKC" => 0, "FEW" => 18, "SCT" => 44, "BKN" => 75, "OVC" or "VV" => 100, _ => -1 };
                        if (c >= 0) cloud = Math.Max(cloud ?? 0, c);
                    }
                string text = p.TryGetProperty("textDescription", out var td) ? td.GetString() ?? "" : "";
                return new WeatherObservation("NWS", observed, tempC, Speed("windSpeed") ?? 0, dir ?? 270, Speed("windGust"), rh, cloud, pr ?? 0, vis, WmoFromText(text, cloud), null, IsModel: false);
            }
            catch (Exception) { return null; }
        }

        /// <summary>The NWS describes the sky in words ("Mostly Cloudy", "Light Rain", "Thunderstorm in Vicinity"): map them to WMO codes.</summary>
        public static int WmoFromText(string text, double? cloudPct = null)
        {
            string s = text.ToLowerInvariant();
            bool heavy = s.Contains("heavy"), light = s.Contains("light");
            if (s.Contains("thunder")) return heavy ? 99 : 95;
            if (s.Contains("freezing rain") || s.Contains("sleet") || s.Contains("ice pellets") || s.Contains("wintry")) return 67;
            if (s.Contains("snow") || s.Contains("blizzard") || s.Contains("flurr")) return heavy || s.Contains("blizzard") ? 75 : light || s.Contains("flurr") ? 71 : 73;
            if (s.Contains("drizzle")) return 53;
            if (s.Contains("rain") || s.Contains("shower")) return heavy ? 65 : light ? 61 : 63;
            if (s.Contains("fog") || s.Contains("mist")) return 45;
            if (s.Contains("overcast") || s.Contains("mostly cloudy")) return 3;
            if (s.Contains("partly")) return 2;
            if (s.Contains("mostly clear") || s.Contains("mostly sunny") || s.Contains("few clouds")) return 1;
            if (s.Contains("clear") || s.Contains("fair") || s.Contains("sunny")) return 0;
            if (s.Contains("cloud")) return 3;
            return cloudPct is { } c ? (c < 15 ? 0 : c < 40 ? 1 : c < 75 ? 2 : 3) : 2;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    public sealed record WeatherFetch(WeatherObservation? Observation, IReadOnlyList<string> Attempts);

    /// <summary>The ONE interface the app uses: providers in order of preference for the place, each with its own timeout, first plausible reading wins.</summary>
    public sealed class WeatherHub
    {
        private readonly IReadOnlyList<IWeatherProvider> _providers;
        private readonly TimeSpan _perProviderTimeout;
        public WeatherHub(IEnumerable<IWeatherProvider> providers, TimeSpan? perProviderTimeout = null)
        {
            _providers = providers.ToList();
            _perProviderTimeout = perProviderTimeout ?? TimeSpan.FromSeconds(8);
        }

        private static WeatherHub? _default;
        /// <summary>Order: NWS (real stations, US only) -> Open-Meteo (best regional model) -> MET Norway (independent global model).</summary>
        public static WeatherHub Default
        {
            get
            {
                if (_default != null) return _default;
                var http = WeatherHttp.CreateClient();
                return _default = new WeatherHub(new IWeatherProvider[] { new NwsProvider(http), new OpenMeteoProvider(http), new MetNoProvider(http) });
            }
        }

        public static bool Plausible(WeatherObservation o) =>
            o.TempC is > -90 and < 60 && o.WindKmh is >= 0 and < 400 && o.WmoCode is >= 0 and <= 99;

        public async Task<WeatherFetch> GetAsync(double lat, double lon, CancellationToken ct = default)
        {
            var attempts = new List<string>();
            if (!WeatherCoord.Plausible(lat, lon)) { attempts.Add("invalid coordinates"); return new WeatherFetch(null, attempts); }
            foreach (var p in _providers)
            {
                if (!p.Covers(lat, lon)) continue;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(_perProviderTimeout);
                    var obs = await p.GetCurrentAsync(lat, lon, cts.Token).ConfigureAwait(false);
                    if (obs != null && Plausible(obs)) { attempts.Add(p.Name + ": ok"); return new WeatherFetch(obs, attempts); }
                    attempts.Add(p.Name + (obs == null ? ": no data" : ": implausible"));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { attempts.Add(p.Name + ": " + ex.GetType().Name); }
            }
            return new WeatherFetch(null, attempts);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    /// <summary>Sun and moon from latitude, longitude and the real UTC time (no network). Low-precision astronomical formulas: good to about a minute for sunrise / sunset.</summary>
    public static class SolarCalc
    {
        private const double Rad = Math.PI / 180;
        private static double Mod(double x, double m) { double r = x % m; return r < 0 ? r + m : r; }
        private static double JulianDay(DateTime utc) => utc.ToUniversalTime().ToOADate() + 2415018.5;

        /// <summary>Elevation of the sun's centre above the horizon in degrees (no refraction).</summary>
        public static double SunElevation(DateTime utc, double lat, double lon)
        {
            double n = JulianDay(utc) - 2451545.0;
            double L = Mod(280.460 + 0.9856474 * n, 360);
            double g = Mod(357.528 + 0.9856003 * n, 360) * Rad;
            double lambda = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Rad;
            double eps = (23.439 - 0.0000004 * n) * Rad;
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
            double gmst = Mod(18.697374558 + 24.06570982441908 * n, 24) * 15;   // degrees
            double h = (gmst + lon) * Rad - ra;
            double sinEl = Math.Sin(lat * Rad) * Math.Sin(dec) + Math.Cos(lat * Rad) * Math.Cos(dec) * Math.Cos(h);
            return Math.Asin(Math.Clamp(sinEl, -1, 1)) / Rad;
        }

        /// <summary>The upper limb touches the horizon (standard refraction + radius): -0.833 degrees.</summary>
        public static bool IsDay(DateTime utc, double lat, double lon) => SunElevation(utc, lat, lon) > -0.833;

        /// <summary>Next sunrise and sunset after <paramref name="utc"/> within 36 h (null at the poles when there is none).</summary>
        public static (DateTime? Sunrise, DateTime? Sunset) NextSunEvents(DateTime utc, double lat, double lon)
        {
            DateTime? rise = null, set = null;
            var step = TimeSpan.FromMinutes(10);
            DateTime t = utc; bool prev = IsDay(t, lat, lon);
            for (int i = 0; i < 36 * 6 && (rise == null || set == null); i++)
            {
                var next = t + step; bool now = IsDay(next, lat, lon);
                if (now != prev)
                {
                    DateTime lo = t, hi = next;
                    for (int k = 0; k < 10; k++) { var mid = lo + (hi - lo) / 2; if (IsDay(mid, lat, lon) == prev) lo = mid; else hi = mid; }
                    if (now) rise ??= hi; else set ??= hi;
                }
                t = next; prev = now;
            }
            return (rise, set);
        }

        /// <summary>Moon phase as a fraction of the synodic month: 0 = new, 0.25 = first quarter, 0.5 = full, 0.75 = last quarter.</summary>
        public static double MoonPhase(DateTime utc) => Mod((JulianDay(utc) - 2451550.1) / 29.530588853, 1);
    }

    // ---------------------------------------------------------------------------------------------------------------
    /// <summary>The JSON the wallpaper page and the settings label read. Old keys are unchanged; new keys only add information.</summary>
    public static class WeatherPayload
    {
        /// <summary>For providers that give no local time zone (MET Norway): the PC's own offset when the place is in about the same longitude band (keeps daylight saving right), else the solar zone of the longitude.</summary>
        public static int GuessOffsetSeconds(double lon, DateTime nowUtc)
        {
            int pc = (int)TimeZoneInfo.Local.GetUtcOffset(nowUtc).TotalSeconds;
            return Math.Abs(lon / 15.0 - pc / 3600.0) <= 2 ? pc : (int)Math.Round(lon / 15.0) * 3600;
        }

        public static string Build(WeatherObservation o, double lat, double lon, DateTime nowUtc, bool stale)
        {
            var (rise, set) = SolarCalc.NextSunEvents(nowUtc, lat, lon);
            return JsonSerializer.Serialize(new
            {
                ok = true,
                code = o.WmoCode,
                temp = o.TempC,
                humidity = o.HumidityPct ?? -1,
                precip = o.PrecipMmPerHour,
                cloud = o.CloudPct ?? -1,
                wind = o.WindKmh,
                windDir = o.WindDirDeg,
                gust = o.GustKmh ?? -1,
                visibility = o.VisibilityM ?? -1,
                isDay = SolarCalc.IsDay(nowUtc, lat, lon),
                utcOffsetSec = o.UtcOffsetSec ?? GuessOffsetSeconds(lon, nowUtc),
                sunrise = rise?.ToString("o", CultureInfo.InvariantCulture),
                sunset = set?.ToString("o", CultureInfo.InvariantCulture),
                moonPhase = Math.Round(SolarCalc.MoonPhase(nowUtc), 3),
                source = o.Provider,
                isModel = o.IsModel,
                observedAt = o.ObservedUtc.ToString("o", CultureInfo.InvariantCulture),
                fetchedAt = nowUtc.ToString("o", CultureInfo.InvariantCulture),
                stale,
            });
        }
    }
}

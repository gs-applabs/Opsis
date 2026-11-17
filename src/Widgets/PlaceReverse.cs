using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    /// <summary>
    /// OPT-IN, user-triggered reverse geocoding with OpenStreetMap Nominatim, for a more exact place name than the offline table (neighbourhood, village, anywhere on Earth).
    /// Usage policy read on 2026-11-12 (operations.osmfoundation.org/policies/nominatim): at most 1 request per second, an identifying User-Agent, cache results, no periodic
    /// or bulk queries (so this runs only when the person presses "Use my location", once per click), attribution to OpenStreetMap contributors (ODbL).
    /// The point is rounded to 2 decimals (about 1 km) before it is sent; nothing is stored except the resulting NAME.
    /// </summary>
    public sealed class PlaceReverse
    {
        public const string SourceName = "OpenStreetMap (ODbL)";
        private readonly HttpClient _http;
        private readonly Func<DateTime> _now;
        private readonly Dictionary<string, string?> _cache = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _last = DateTime.MinValue;

        public PlaceReverse(HttpClient http, Func<DateTime>? now = null) { _http = http; _now = now ?? (() => DateTime.UtcNow); }

        public static string Url(double lat, double lon, bool greek) =>
            $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=14&addressdetails=1&accept-language={(greek ? "el" : "en")}" +
            $"&lat={WeatherCoord.Inv(lat)}&lon={WeatherCoord.Inv(lon)}";

        /// <summary>Asks for the name (cached per rounded point and language, never faster than 1 request per second). Null when unavailable.</summary>
        public async Task<string?> NameAsync(double lat, double lon, bool greek, CancellationToken ct)
        {
            string key = $"{WeatherCoord.Inv(lat)},{WeatherCoord.Inv(lon)},{(greek ? "el" : "en")}";
            lock (_cache) if (_cache.TryGetValue(key, out var hit)) return hit;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var wait = TimeSpan.FromSeconds(1) - (_now() - _last);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
                _last = _now();
                using var resp = await _http.GetAsync(Url(lat, lon, greek), ct).ConfigureAwait(false);
                string? name = resp.IsSuccessStatusCode ? Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : null;
                lock (_cache) _cache[key] = name;
                return name;
            }
            catch (Exception) { return null; }
            finally { _gate.Release(); }
        }

        /// <summary>"Ampelokipoi, Athens" from the address: the neighbourhood or village first, then the town / municipality.</summary>
        public static string? Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object) return null;
                string? Get(params string[] keys)
                {
                    foreach (var k in keys) if (a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) return v.GetString();
                    return null;
                }
                string? small = Get("suburb", "neighbourhood", "quarter", "city_district", "hamlet", "village");
                string? town = Get("city", "town", "municipality", "village", "county", "state");
                if (small == null && town == null) return null;
                if (small == null || string.Equals(small, town, StringComparison.OrdinalIgnoreCase)) return town;
                return town == null ? small : $"{town} — {small}";
            }
            catch (Exception) { return null; }
        }
    }
}

namespace MotionDesk.Services
{
    /// <summary>One place to ask for a name: the offline table first, the online lookup only when the person allowed it.</summary>
    public static class PlaceNaming
    {
        private static readonly Lazy<PlaceReverse> Online = new(() => new PlaceReverse(WeatherHttp.CreateClient()));

        /// <returns>The display name, the full hierarchy, and the source of the name (empty when only a generic name is possible).</returns>
        public static async Task<(string Name, string Detail, string Source)> ResolveAsync(double lat, double lon, bool greek, bool onlineAllowed, string genericName, CancellationToken ct = default)
        {
            PlaceName? local = null;
            try { local = PlaceGazetteer.Default().Find(lat, lon); } catch (Exception) { }
            if (onlineAllowed)
            {
                string? online = await Online.Value.NameAsync(lat, lon, greek, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(online)) return (online!, local?.Full(greek) ?? online!, PlaceReverse.SourceName);
            }
            if (local != null) return (local.Display(greek), local.Full(greek), PlaceGazetteer.SourceName);
            return (genericName, genericName, "");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MotionDesk.Services
{
    /// <summary>A place found for a (rounded) point: the name, the part of the hierarchy it belongs to and where the name came from.</summary>
    public sealed record PlaceName(string NameEl, string NameEn, string? ParentEl, string? ParentEn, string? UnitEl, string? UnitEn, string? RegionEl, string? RegionEn,
        double DistanceKm, string Source)
    {
        /// <summary>The short name for the wallpaper and the weather lines: "Athens — Ampelokipoi" (a neighbourhood with its town), else "Agios Dimitrios, Attica".</summary>
        public string Display(bool greek)
        {
            string name = greek ? NameEl : NameEn;
            string? parent = greek ? ParentEl : ParentEn, region = greek ? RegionEl : RegionEn;
            if (!string.IsNullOrEmpty(parent) && !string.Equals(parent, name, StringComparison.OrdinalIgnoreCase)) return $"{parent} — {name}";
            return !string.IsNullOrEmpty(region) && !string.Equals(region, name, StringComparison.OrdinalIgnoreCase) ? $"{name}, {region}" : name;
        }

        /// <summary>The whole hierarchy for the settings page: "Ampelokipoi · Athens · Nomos Attikis · Attica".</summary>
        public string Full(bool greek)
        {
            var parts = new[] { greek ? NameEl : NameEn, greek ? ParentEl : ParentEn, greek ? UnitEl : UnitEn, greek ? RegionEl : RegionEn };
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// OFFLINE place names for a point (plan 0B.5): the nearest settlement or neighbourhood of Greece from a small local gazetteer, so the coordinates never leave the PC
    /// for a name. Data: GeoNames (geonames.org, CC BY 4.0) - about 15 000 populated places with their Greek names and the prefecture / region (tools/build-gazetteer.py).
    /// The table (a ~290 KB resource) is read only when a name is asked for and held through a weak reference, so memory pressure frees it.
    /// </summary>
    public sealed class PlaceGazetteer
    {
        public const string SourceName = "GeoNames (CC BY 4.0)";
        private const double CellDegrees = 0.1;

        private readonly string[] _el, _en;
        private readonly float[] _lat, _lon;
        private readonly byte[] _kind;           // 0 settlement, 1 neighbourhood / section of a settlement, 2 capital / seat of an administrative division
        private readonly short[] _a1, _a2;       // index into the region / unit tables, -1 = unknown
        private readonly List<(string El, string En)> _regions = new(), _units = new();
        private readonly Dictionary<long, List<int>> _grid = new();

        public int Count => _el.Length;

        /// <summary>Builds the table from the lines of the resource (also used by the tests with a tiny fixture).</summary>
        public PlaceGazetteer(IEnumerable<string> lines)
        {
            var regionIndex = new Dictionary<string, int>(); var unitIndex = new Dictionary<string, int>();
            var places = new List<(string El, string En, float Lat, float Lon, string A1, string A2, byte Kind)>();
            foreach (var line in lines)
            {
                var f = line.Split('\t');
                if (f.Length < 4) continue;
                if (f[0] == "A1" && f.Length >= 4) { regionIndex[f[1]] = _regions.Count; _regions.Add((f[2], f[3])); }
                else if (f[0] == "A2" && f.Length >= 5) { unitIndex[f[1] + "." + f[2]] = _units.Count; _units.Add((f[3], f[4])); }
                else if (f[0] == "P" && f.Length >= 8 && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) && float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
                    places.Add((f[1], f[2], la, lo, f[5], f[6], (byte)(f[7] == "PPLX" ? 1 : f[7] is "PPLC" or "PPLA" or "PPLA2" ? 2 : 0)));
            }
            int n = places.Count;
            _el = new string[n]; _en = new string[n]; _lat = new float[n]; _lon = new float[n]; _kind = new byte[n]; _a1 = new short[n]; _a2 = new short[n];
            for (int i = 0; i < n; i++)
            {
                var p = places[i];
                _el[i] = p.El; _en[i] = p.En; _lat[i] = p.Lat; _lon[i] = p.Lon; _kind[i] = p.Kind;
                _a1[i] = regionIndex.TryGetValue(p.A1, out var r) ? (short)r : (short)-1;
                _a2[i] = unitIndex.TryGetValue(p.A1 + "." + p.A2, out var u) ? (short)u : (short)-1;
                long key = Cell(p.Lat, p.Lon);
                if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>(4);
                list.Add(i);
            }
        }

        private static long Cell(double lat, double lon) => ((long)Math.Floor(lat / CellDegrees) << 20) ^ (long)(Math.Floor(lon / CellDegrees) + 100000);

        private static double Km(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private IEnumerable<int> Near(double lat, double lon, int rings)
        {
            for (int dy = -rings; dy <= rings; dy++)
                for (int dx = -rings; dx <= rings; dx++)
                    if (_grid.TryGetValue(Cell(lat + dy * CellDegrees, lon + dx * CellDegrees), out var list))
                        foreach (var i in list) yield return i;
        }

        /// <summary>
        /// The place for the point, or null when no settlement is within <paramref name="maxKm"/> (outside Greece, open sea). The nearest settlement wins; a neighbourhood
        /// wins when it is within 1.5 km (and then carries the nearest town as its parent: "Athens - Ampelokipoi").
        /// </summary>
        public PlaceName? Find(double lat, double lon, double maxKm = 8)
        {
            int best = -1, bestSection = -1, bestTown = -1, bestSeat = -1; double dBest = double.MaxValue, dSection = double.MaxValue, dTown = double.MaxValue, dSeat = double.MaxValue;
            foreach (var i in Near(lat, lon, 3))
            {
                double d = Km(lat, lon, _lat[i], _lon[i]);
                if (d < dBest) { dBest = d; best = i; }
                if (_kind[i] == 1 && d < dSection) { dSection = d; bestSection = i; }
                if (_kind[i] != 1 && d < dTown) { dTown = d; bestTown = i; }
                if (_kind[i] == 2 && d < dSeat) { dSeat = d; bestSeat = i; }
            }
            if (best < 0 || dBest > maxKm) return null;
            int pick = bestSection >= 0 && dSection <= 1.5 ? bestSection : best;
            double dPick = pick == bestSection ? dSection : dBest;
            int parent = _kind[pick] == 1 ? (bestSeat >= 0 && dSeat <= 12 ? bestSeat : bestTown) : -1;   // the town a neighbourhood belongs to: the nearby seat (Athens), else the nearest settlement
            var region = _a1[pick] >= 0 ? _regions[_a1[pick]] : default;
            var unit = _a2[pick] >= 0 ? _units[_a2[pick]] : default;
            return new PlaceName(_el[pick], _en[pick], parent >= 0 ? _el[parent] : null, parent >= 0 ? _en[parent] : null,
                unit.El, unit.En, region.El, region.En, Math.Round(dPick, 2), SourceName);
        }

        // ---- the shared, lazily loaded instance
        private static WeakReference<PlaceGazetteer>? _shared;
        private static readonly object Gate = new();

        /// <summary>The real table of Greece (loaded on the first call; the garbage collector may drop it under memory pressure).</summary>
        public static PlaceGazetteer Default()
        {
            lock (Gate)
            {
                if (_shared != null && _shared.TryGetTarget(out var g)) return g;
                using var res = typeof(PlaceGazetteer).Assembly.GetManifestResourceStream("gazetteer-gr.tsv.gz") ?? throw new FileNotFoundException("gazetteer-gr.tsv.gz");
                using var gz = new GZipStream(res, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, System.Text.Encoding.UTF8);
                var lines = new List<string>(16000);
                string? line; while ((line = reader.ReadLine()) != null) lines.Add(line);
                var table = new PlaceGazetteer(lines);
                _shared = new WeakReference<PlaceGazetteer>(table);
                return table;
            }
        }
    }
}

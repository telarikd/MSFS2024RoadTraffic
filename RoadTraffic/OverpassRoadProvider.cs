using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
// Pokud nemáš Newtonsoft, můžeš přejít na System.Text.Json — viz komentář dole
// using System.Text.Json;

namespace MSFSTraffic.Roads
{
    using MSFSTraffic.Models;

    /// <summary>
    /// Stahuje silniční data z OpenStreetMap přes Overpass API.
    /// Vrací RoadSegment objekty připravené pro traffic engine.
    /// </summary>
    public class OverpassRoadProvider : IRoadProvider
    {
        // Veřejné Overpass API endpointy (fallback pokud hlavní nefunguje)
        private static readonly string[] OverpassEndpoints = new[]
        {
            "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass-api.de/api/interpreter"
        };

        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, List<RoadSegment>> _cache;
        private readonly double _tileSizeDeg;  // velikost cache tile ve stupních

        /// <summary>Počet úspěšných API volání.</summary>
        public int ApiCallCount { get; private set; }

        /// <summary>Počet cache hitů.</summary>
        public int CacheHitCount { get; private set; }

        public OverpassRoadProvider(double tileSizeDeg = 0.05)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSTrafficEngine/1.0");

            _cache = new Dictionary<string, List<RoadSegment>>();
            _tileSizeDeg = tileSizeDeg;  // ~5.5 km tile
        }

        /// <summary>
        /// Získá silnice v okolí dané pozice.
        /// Používá tile-based cache — stáhne jen nové tiles.
        /// </summary>
        /// <param name="center">Pozice hráče.</param>
        /// <param name="radiusMeters">Poloměr v metrech.</param>
        /// <returns>Seznam silničních segmentů.</returns>
        public async Task<List<RoadSegment>> GetRoadsAroundAsync(GeoCoordinate center, double radiusMeters = 5000)
        {
            var bbox = BoundingBox.FromCenter(center, radiusMeters);
            var tileKeys = GetTileKeys(bbox);
            var result = new List<RoadSegment>();
            var tilesToFetch = new List<string>();

            // Zkontroluj cache
            foreach (var key in tileKeys)
            {
                if (_cache.ContainsKey(key))
                {
                    result.AddRange(_cache[key]);
                    CacheHitCount++;
                }
                else
                {
                    tilesToFetch.Add(key);
                }
            }

            // Stáhni chybějící tiles
            foreach (var tileKey in tilesToFetch)
            {
                try
                {
                    var tileBbox = TileKeyToBbox(tileKey);
                    var segments = await FetchRoadsFromOverpassAsync(tileBbox);
                    _cache[tileKey] = segments;
                    result.AddRange(segments);
                    ApiCallCount++;

                    Console.WriteLine($"  [Overpass] Tile {tileKey}: {segments.Count} road segments fetched.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [Overpass] ERROR fetching tile {tileKey}: {ex.Message}");
                    _cache[tileKey] = new List<RoadSegment>(); // prázdný cache aby se neopakoval
                }
            }

            return result;
        }

        /// <summary>
        /// Stáhne silnice z Overpass API pro daný bounding box.
        /// </summary>
        private async Task<List<RoadSegment>> FetchRoadsFromOverpassAsync(BoundingBox bbox)
        {
            string bboxStr = string.Format(CultureInfo.InvariantCulture,
                "{0:F6},{1:F6},{2:F6},{3:F6}",
                bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon);

            // Overpass query — silnice s geometrií, včetně metadat
            string query = $@"
                [out:json][timeout:25];
                (
                  way[""highway""~""motorway|trunk|primary|secondary|tertiary|residential|unclassified|track""]
                    ({bboxStr});
                );
                out geom;";

            string encodedQuery = "data=" + Uri.EscapeDataString(query);

            // Zkus endpointy postupně
            foreach (var endpoint in OverpassEndpoints)
            {
                try
                {
                    var content = new StringContent(encodedQuery,
                        System.Text.Encoding.UTF8,
                        "application/x-www-form-urlencoded");

                    var response = await _httpClient.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    return ParseOverpassResponse(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [Overpass] Endpoint {endpoint} failed: {ex.Message}");
                    continue;
                }
            }

            throw new Exception("All Overpass API endpoints failed.");
        }

        /// <summary>
        /// Parsuje JSON response z Overpass API do RoadSegment objektů.
        /// Jednoduchý parser bez závislosti na JSON knihovně.
        /// </summary>
        private List<RoadSegment> ParseOverpassResponse(string json)
        {
            var segments = new List<RoadSegment>();

            // Najdi všechny "elements" — každý element je jedna cesta (way)
            int searchFrom = 0;
            while (true)
            {
                // Hledej další way element (compact i pretty-printed JSON)
                int typeIdx = json.IndexOf("\"type\":\"way\"", searchFrom, StringComparison.Ordinal);
                if (typeIdx < 0)
                    typeIdx = json.IndexOf("\"type\": \"way\"", searchFrom, StringComparison.Ordinal);
                if (typeIdx < 0) break;

                // Najdi začátek tohoto elementu (předchozí '{')
                int elementStart = json.LastIndexOf('{', typeIdx);
                if (elementStart < 0) { searchFrom = typeIdx + 1; continue; }

                // Najdi konec elementu — matchuj závorky
                int elementEnd = FindMatchingBrace(json, elementStart);
                if (elementEnd < 0) { searchFrom = typeIdx + 1; continue; }

                string element = json.Substring(elementStart, elementEnd - elementStart + 1);
                searchFrom = elementEnd + 1;

                try
                {
                    var segment = ParseWayElement(element);
                    if (segment != null && segment.Nodes.Count >= 2)
                        segments.Add(segment);
                }
                catch
                {
                    // Přeskoč nevalidní elementy
                }
            }

            return segments;
        }

        /// <summary>
        /// Parsuje jeden way element z Overpass JSON.
        /// </summary>
        private RoadSegment ParseWayElement(string element)
        {
            long osmId = ParseLong(element, "\"id\":");
            string highway = ParseString(element, "\"highway\":\"");
            string name = ParseString(element, "\"name\":\"");
            string maxspeedStr = ParseString(element, "\"maxspeed\":\"");
            string lanesStr = ParseString(element, "\"lanes\":\"");
            string onewayStr = ParseString(element, "\"oneway\":\"");

            RoadType roadType = MapHighwayToRoadType(highway);

            int maxSpeed = 0;
            if (!string.IsNullOrEmpty(maxspeedStr))
                int.TryParse(maxspeedStr.Replace(" km/h", "").Replace("mph", "").Trim(), out maxSpeed);

            int lanes = 0;
            if (!string.IsNullOrEmpty(lanesStr))
                int.TryParse(lanesStr, out lanes);

            bool isOneWay = onewayStr == "yes" || onewayStr == "1" ||
                            highway == "motorway"; // dálnice jsou implicitně jednosměrné

            // Parsuj geometrii (nodes)
            var nodes = ParseGeometry(element);
            if (nodes.Count < 2) return null;

            return new RoadSegment(osmId, roadType, nodes, maxSpeed, lanes, isOneWay, name ?? "");
        }

        /// <summary>
        /// Parsuje pole geometry bodů z Overpass "geometry":[...] pole.
        /// </summary>
        private List<GeoCoordinate> ParseGeometry(string element)
        {
            var nodes = new List<GeoCoordinate>();

            int geomIdx = element.IndexOf("\"geometry\":[", StringComparison.Ordinal);
            if (geomIdx < 0)
                geomIdx = element.IndexOf("\"geometry\": [", StringComparison.Ordinal);
            if (geomIdx < 0) return nodes;

            int arrayStart = element.IndexOf('[', geomIdx);
            int arrayEnd = FindMatchingBracket(element, arrayStart);
            if (arrayEnd < 0) return nodes;

            string geomArray = element.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

            // Parsuj jednotlivé {lat:..., lon:...} objekty
            int pos = 0;
            while (pos < geomArray.Length)
            {
                int objStart = geomArray.IndexOf('{', pos);
                if (objStart < 0) break;

                int objEnd = geomArray.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string obj = geomArray.Substring(objStart, objEnd - objStart + 1);
                pos = objEnd + 1;

                double lat = ParseDouble(obj, "\"lat\":");
                double lon = ParseDouble(obj, "\"lon\":");

                if (lat != 0 && lon != 0)
                    nodes.Add(new GeoCoordinate(lat, lon));
            }

            return nodes;
        }

        // ── Tile cache helpers ──

        /// <summary>
        /// Vrátí klíče tile-ů, které pokrývají daný bounding box.
        /// </summary>
        private List<string> GetTileKeys(BoundingBox bbox)
        {
            var keys = new List<string>();

            int minTileLat = (int)Math.Floor(bbox.MinLat / _tileSizeDeg);
            int maxTileLat = (int)Math.Floor(bbox.MaxLat / _tileSizeDeg);
            int minTileLon = (int)Math.Floor(bbox.MinLon / _tileSizeDeg);
            int maxTileLon = (int)Math.Floor(bbox.MaxLon / _tileSizeDeg);

            for (int tLat = minTileLat; tLat <= maxTileLat; tLat++)
            {
                for (int tLon = minTileLon; tLon <= maxTileLon; tLon++)
                {
                    keys.Add($"{tLat}_{tLon}");
                }
            }

            return keys;
        }

        /// <summary>
        /// Převede tile klíč zpět na bounding box.
        /// </summary>
        private BoundingBox TileKeyToBbox(string key)
        {
            var parts = key.Split('_');
            int tLat = int.Parse(parts[0]);
            int tLon = int.Parse(parts[1]);

            return new BoundingBox(
                tLat * _tileSizeDeg,
                tLon * _tileSizeDeg,
                (tLat + 1) * _tileSizeDeg,
                (tLon + 1) * _tileSizeDeg);
        }

        /// <summary>
        /// Vyčistí cache (např. pro tiles příliš daleko od hráče).
        /// </summary>
        public void ClearDistantTiles(GeoCoordinate center, double maxDistanceDeg = 0.2)
        {
            var toRemove = new List<string>();
            foreach (var kvp in _cache)
            {
                var tileBbox = TileKeyToBbox(kvp.Key);
                double centerLat = (tileBbox.MinLat + tileBbox.MaxLat) / 2;
                double centerLon = (tileBbox.MinLon + tileBbox.MaxLon) / 2;

                double dist = Math.Abs(centerLat - center.Latitude) +
                              Math.Abs(centerLon - center.Longitude);

                if (dist > maxDistanceDeg)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _cache.Remove(key);

            if (toRemove.Count > 0)
                Console.WriteLine($"  [Cache] Cleared {toRemove.Count} distant tiles.");
        }

        // ── Mapping helpers ──

        private static RoadType MapHighwayToRoadType(string highway)
        {
            if (string.IsNullOrEmpty(highway)) return RoadType.Unknown;

            // Motorway links mají stejný typ jako parent
            if (highway.StartsWith("motorway")) return RoadType.Motorway;
            if (highway.StartsWith("trunk"))    return RoadType.Trunk;
            if (highway.StartsWith("primary"))  return RoadType.Primary;
            if (highway.StartsWith("secondary"))return RoadType.Secondary;
            if (highway.StartsWith("tertiary")) return RoadType.Tertiary;

            switch (highway)
            {
                case "residential":  return RoadType.Residential;
                case "unclassified": return RoadType.Unclassified;
                case "track":        return RoadType.Track;
                default:             return RoadType.Unknown;
            }
        }

        // ── Minimal JSON parsing helpers (no external dependencies) ──

        private static long ParseLong(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + key.Length;
            // Přeskoč whitespace (pretty-printed JSON)
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
                start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;
            if (end == start) return 0;
            long.TryParse(json.Substring(start, end - start), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out long result);
            return result;
        }

        private static double ParseDouble(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + key.Length;
            // Přeskoč whitespace
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
                start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' ||
                                         json[end] == '-' || json[end] == 'e' || json[end] == 'E'))
                end++;
            if (end == start) return 0;
            double.TryParse(json.Substring(start, end - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double result);
            return result;
        }

        private static string ParseString(string json, string key)
        {
            // Zkus kompaktní JSON ("key":"value"), pak pretty ("key": "value")
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
            {
                // Vlož mezeru před poslední uvozovku: "foo":"  →  "foo": "
                string altKey = key.Insert(key.Length - 1, " ");
                idx = json.IndexOf(altKey, StringComparison.Ordinal);
                if (idx < 0) return null;
                int s = idx + altKey.Length;
                int e = json.IndexOf('"', s);
                if (e < 0) return null;
                return json.Substring(s, e - s);
            }
            int start = idx + key.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        private static int FindMatchingBrace(string json, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static int FindMatchingBracket(string json, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
    }
}

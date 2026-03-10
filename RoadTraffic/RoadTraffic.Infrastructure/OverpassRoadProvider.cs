using RoadTraffic.Core;
using RoadTraffic.Core.Models;
using RoadTraffic.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace RoadTraffic.Infrastructure
{
    public class OverpassRoadProvider : IRoadProvider
    {
        private static readonly string[] OverpassEndpoints = new[]
        {
            "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass-api.de/api/interpreter"
        };

        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, List<RoadSegment>> _cache;
        private readonly double _tileSizeDeg;
        private readonly ILogger _logger;

        public OverpassRoadProvider(ILogger logger, double tileSizeDeg = 0.05)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSTrafficEngine/1.0");
            _cache = new Dictionary<string, List<RoadSegment>>();
            _tileSizeDeg = tileSizeDeg;
        }

        public async Task<IReadOnlyList<RoadSegment>> GetRoadsAroundAsync(GeoCoordinate center, double radiusMeters = 5000)
        {
            var bbox = BoundingBox.FromCenter(center, radiusMeters);
            var tileKeys = GetTileKeys(bbox);
            var result = new List<RoadSegment>();
            var tilesToFetch = new List<string>();

            foreach (var key in tileKeys)
            {
                if (_cache.ContainsKey(key))
                {
                    result.AddRange(_cache[key]);
                }
                else
                {
                    tilesToFetch.Add(key);
                }
            }

            foreach (var tileKey in tilesToFetch)
            {
                try
                {
                    var segments = await FetchRoadsFromOverpassAsync(TileKeyToBbox(tileKey));
                    _cache[tileKey] = segments;
                    result.AddRange(segments);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Overpass tile fetch failed for {tileKey}", ex);
                    _cache[tileKey] = new List<RoadSegment>();
                }
            }

            return result;
        }

        public void ClearDistantTiles(GeoCoordinate center, double maxDistanceDeg = 0.2)
        {
            var toRemove = new List<string>();
            foreach (var pair in _cache)
            {
                var tileBbox = TileKeyToBbox(pair.Key);
                double centerLat = (tileBbox.MinLat + tileBbox.MaxLat) / 2;
                double centerLon = (tileBbox.MinLon + tileBbox.MaxLon) / 2;
                double distance = Math.Abs(centerLat - center.Latitude) + Math.Abs(centerLon - center.Longitude);
                if (distance > maxDistanceDeg)
                {
                    toRemove.Add(pair.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _cache.Remove(key);
            }
        }

        private async Task<List<RoadSegment>> FetchRoadsFromOverpassAsync(BoundingBox bbox)
        {
            string bboxStr = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6},{3:F6}", bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon);
            string query = $@"
                [out:json][timeout:25];
                (
                  way[""highway""~""motorway|trunk|primary|secondary|tertiary|residential|unclassified|track""]
                    ({bboxStr});
                );
                out geom;";
            string encodedQuery = "data=" + Uri.EscapeDataString(query);

            foreach (var endpoint in OverpassEndpoints)
            {
                try
                {
                    _logger.Info($"Overpass request sent to {endpoint}");
                    var content = new StringContent(encodedQuery, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                    var response = await _httpClient.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    _logger.Info($"Overpass response received from {endpoint}");
                    return ParseOverpassResponse(json);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Overpass request failed for {endpoint}", ex);
                }
            }

            throw new InvalidOperationException("All Overpass API endpoints failed.");
        }

        private List<RoadSegment> ParseOverpassResponse(string json)
        {
            var segments = new List<RoadSegment>();
            int searchFrom = 0;

            while (true)
            {
                int typeIdx = json.IndexOf("\"type\":\"way\"", searchFrom, StringComparison.Ordinal);
                if (typeIdx < 0)
                {
                    typeIdx = json.IndexOf("\"type\": \"way\"", searchFrom, StringComparison.Ordinal);
                }

                if (typeIdx < 0)
                {
                    break;
                }

                int elementStart = json.LastIndexOf('{', typeIdx);
                if (elementStart < 0)
                {
                    searchFrom = typeIdx + 1;
                    continue;
                }

                int elementEnd = FindMatchingBrace(json, elementStart);
                if (elementEnd < 0)
                {
                    searchFrom = typeIdx + 1;
                    continue;
                }

                string element = json.Substring(elementStart, elementEnd - elementStart + 1);
                searchFrom = elementEnd + 1;

                try
                {
                    var segment = ParseWayElement(element);
                    if (segment != null && segment.Nodes.Count >= 2)
                    {
                        segments.Add(segment);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to parse Overpass way element", ex);
                }
            }

            return segments;
        }

        private RoadSegment ParseWayElement(string element)
        {
            long osmId = ParseLong(element, "\"id\":");
            string highway = ParseString(element, "\"highway\":\"");
            string name = ParseString(element, "\"name\":\"");
            string maxspeedStr = ParseString(element, "\"maxspeed\":\"");
            string lanesStr = ParseString(element, "\"lanes\":\"");
            string onewayStr = ParseString(element, "\"oneway\":\"");

            int.TryParse((maxspeedStr ?? string.Empty).Replace(" km/h", string.Empty).Replace("mph", string.Empty).Trim(), out int maxSpeed);
            int.TryParse(lanesStr, out int lanes);
            bool isOneWay = onewayStr == "yes" || onewayStr == "1" || highway == "motorway";

            var nodes = ParseGeometry(element);
            if (nodes.Count < 2)
            {
                return null;
            }

            return new RoadSegment(osmId, MapHighwayToRoadType(highway), nodes, maxSpeed, lanes, isOneWay, name ?? string.Empty);
        }

        private List<GeoCoordinate> ParseGeometry(string element)
        {
            var nodes = new List<GeoCoordinate>();
            int geomIdx = element.IndexOf("\"geometry\":[", StringComparison.Ordinal);
            if (geomIdx < 0)
            {
                geomIdx = element.IndexOf("\"geometry\": [", StringComparison.Ordinal);
            }

            if (geomIdx < 0)
            {
                return nodes;
            }

            int arrayStart = element.IndexOf('[', geomIdx);
            int arrayEnd = FindMatchingBracket(element, arrayStart);
            if (arrayEnd < 0)
            {
                return nodes;
            }

            string geomArray = element.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            int position = 0;
            while (position < geomArray.Length)
            {
                int objectStart = geomArray.IndexOf('{', position);
                if (objectStart < 0)
                {
                    break;
                }

                int objectEnd = geomArray.IndexOf('}', objectStart);
                if (objectEnd < 0)
                {
                    break;
                }

                string obj = geomArray.Substring(objectStart, objectEnd - objectStart + 1);
                position = objectEnd + 1;
                double lat = ParseDouble(obj, "\"lat\":");
                double lon = ParseDouble(obj, "\"lon\":");
                if (lat != 0 && lon != 0)
                {
                    nodes.Add(new GeoCoordinate(lat, lon));
                }
            }

            return nodes;
        }

        private List<string> GetTileKeys(BoundingBox bbox)
        {
            var keys = new List<string>();
            int minTileLat = (int)Math.Floor(bbox.MinLat / _tileSizeDeg);
            int maxTileLat = (int)Math.Floor(bbox.MaxLat / _tileSizeDeg);
            int minTileLon = (int)Math.Floor(bbox.MinLon / _tileSizeDeg);
            int maxTileLon = (int)Math.Floor(bbox.MaxLon / _tileSizeDeg);

            for (int tileLat = minTileLat; tileLat <= maxTileLat; tileLat++)
            {
                for (int tileLon = minTileLon; tileLon <= maxTileLon; tileLon++)
                {
                    keys.Add($"{tileLat}_{tileLon}");
                }
            }

            return keys;
        }

        private BoundingBox TileKeyToBbox(string key)
        {
            var parts = key.Split('_');
            int tileLat = int.Parse(parts[0]);
            int tileLon = int.Parse(parts[1]);
            return new BoundingBox(tileLat * _tileSizeDeg, tileLon * _tileSizeDeg, (tileLat + 1) * _tileSizeDeg, (tileLon + 1) * _tileSizeDeg);
        }

        private static RoadType MapHighwayToRoadType(string highway)
        {
            if (string.IsNullOrEmpty(highway))
            {
                return RoadType.Unknown;
            }

            if (highway.StartsWith("motorway", StringComparison.Ordinal)) return RoadType.Motorway;
            if (highway.StartsWith("trunk", StringComparison.Ordinal)) return RoadType.Trunk;
            if (highway.StartsWith("primary", StringComparison.Ordinal)) return RoadType.Primary;
            if (highway.StartsWith("secondary", StringComparison.Ordinal)) return RoadType.Secondary;
            if (highway.StartsWith("tertiary", StringComparison.Ordinal)) return RoadType.Tertiary;

            switch (highway)
            {
                case "residential":
                    return RoadType.Residential;
                case "unclassified":
                    return RoadType.Unclassified;
                case "track":
                    return RoadType.Track;
                default:
                    return RoadType.Unknown;
            }
        }

        private static long ParseLong(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + key.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            return end == start ? 0 : long.Parse(json.Substring(start, end - start), CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + key.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == 'e' || json[end] == 'E')) end++;
            return end == start ? 0 : double.Parse(json.Substring(start, end - start), CultureInfo.InvariantCulture);
        }

        private static string ParseString(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
            {
                string alternateKey = key.Insert(key.Length - 1, " ");
                idx = json.IndexOf(alternateKey, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return null;
                }

                int startAlternate = idx + alternateKey.Length;
                int endAlternate = json.IndexOf('"', startAlternate);
                return endAlternate < 0 ? null : json.Substring(startAlternate, endAlternate - startAlternate);
            }

            int start = idx + key.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? null : json.Substring(start, end - start);
        }

        private static int FindMatchingBrace(string json, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static int FindMatchingBracket(string json, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }
    }
}

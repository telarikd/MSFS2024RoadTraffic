using System;
using System.Collections.Generic;

namespace MSFSTraffic.Engine
{
    using MSFSTraffic.Models;

    /// <summary>
    /// Spravuje vrstvu vzdáleného provozu (5–15 km) jako světelné body.
    /// Žádné SimObjects, žádný SimConnect — čistě vizuální simulace.
    ///
    /// Princip:
    ///   - motorway + trunk segmenty v pásmu 5–15 km od hráče
    ///   - max 150 světel celkem
    ///   - každý tick: pohyb po segmentu, wrap-around na konci
    ///   - každou sekundu (~20 ticků): refresh seznamu (přidání/odebrání)
    ///   - GetLightPositions() → UI vrstva vykreslí bílé/červené tečky
    /// </summary>
    public class FarTrafficManager
    {
        // ── Konfigurace ──
        private const int    MaxFarLights = 150;
        private const double MinDistM     = 5_000;
        private const double MaxDistM     = 15_000;
        private const int    RefreshEvery = 20;    // ticků (≈ 1 s při 20 Hz)

        // ── Stav ──
        private readonly List<FarTrafficLight> _lights = new List<FarTrafficLight>(MaxFarLights);
        private readonly Random _rng = new Random();
        private int _tickCount;

        /// <summary>Počet aktivních světelných bodů.</summary>
        public int LightCount => _lights.Count;

        // ════════════════════════════════════════
        //  HLAVNÍ UPDATE
        // ════════════════════════════════════════

        /// <summary>
        /// Voláno každý tick z TrafficManager.Update().
        /// </summary>
        public void Update(List<RoadSegment> activeRoads, GeoCoordinate playerPos, double deltaTime)
        {
            _tickCount++;
            MoveLights(deltaTime);

            // Refresh je dražší (prochází všechny segmenty) — stačí 1× za sekundu
            if (_tickCount % RefreshEvery == 0)
                RefreshLights(activeRoads, playerPos);
        }

        // ════════════════════════════════════════
        //  POHYB
        // ════════════════════════════════════════

        private void MoveLights(double deltaTime)
        {
            foreach (var light in _lights)
            {
                double step = light.SpeedMs * deltaTime;

                if (light.IsForward)
                {
                    light.DistanceOnSegment += step;
                    if (light.DistanceOnSegment >= light.Segment.LengthMeters)
                        light.DistanceOnSegment = 0;                  // wrap na začátek
                }
                else
                {
                    light.DistanceOnSegment -= step;
                    if (light.DistanceOnSegment <= 0)
                        light.DistanceOnSegment = light.Segment.LengthMeters; // wrap na konec
                }
            }
        }

        // ════════════════════════════════════════
        //  REFRESH SEZNAMU
        // ════════════════════════════════════════

        private void RefreshLights(List<RoadSegment> roads, GeoCoordinate playerPos)
        {
            // Odstraň světla jejichž segment opustil pásmo 5–15 km
            for (int i = _lights.Count - 1; i >= 0; i--)
            {
                double d = _lights[i].Segment.DistanceToPoint(playerPos);
                if (d < MinDistM || d > MaxDistM)
                    _lights.RemoveAt(i);
            }

            int needed = MaxFarLights - _lights.Count;
            if (needed <= 0) return;

            // Sbírej kandidátní segmenty: jen motorway/trunk v pásmu
            var candidates = new List<RoadSegment>();
            foreach (var road in roads)
            {
                if (road.RoadType != RoadType.Motorway && road.RoadType != RoadType.Trunk)
                    continue;

                double d = road.DistanceToPoint(playerPos);
                if (d >= MinDistM && d <= MaxDistM)
                    candidates.Add(road);
            }

            if (candidates.Count == 0) return;

            // Spawn až do limitu — rozloží se náhodně po kandidátních segmentech
            for (int i = 0; i < needed; i++)
                SpawnLight(candidates[_rng.Next(candidates.Count)]);
        }

        private void SpawnLight(RoadSegment seg)
        {
            // Jednosměrka → vždy Forward; obousměrka → náhodně
            bool forward = seg.IsOneWay || (_rng.NextDouble() > 0.5);

            // Rychlost: max speed segmentu ± 10 %
            double speedMs = (seg.MaxSpeedKmh / 3.6) * (0.9 + _rng.NextDouble() * 0.2);

            _lights.Add(new FarTrafficLight
            {
                Segment           = seg,
                DistanceOnSegment = _rng.NextDouble() * seg.LengthMeters,
                SpeedMs           = speedMs,
                IsForward         = forward
            });
        }

        // ════════════════════════════════════════
        //  POZICE PRO UI VRSTVU
        // ════════════════════════════════════════

        /// <summary>
        /// Vrací pozice všech světel.
        /// isForward=true → bílá tečka (přijíždějící čelo vozidla);
        /// isForward=false → červená tečka (zadní světla).
        /// </summary>
        public List<(GeoCoordinate pos, bool isForward)> GetLightPositions()
        {
            var result = new List<(GeoCoordinate pos, bool isForward)>(_lights.Count);
            foreach (var light in _lights)
                result.Add((InterpolatePosition(light), light.IsForward));
            return result;
        }

        /// <summary>
        /// Interpoluje pozici světla po segmentu — stejná logika jako TrafficVehicle.GetCurrentPosition(),
        /// bez laterálního offsetu (světelné body stačí na ose silnice).
        /// </summary>
        private static GeoCoordinate InterpolatePosition(FarTrafficLight light)
        {
            var nodes = light.Segment.Nodes;
            if (nodes.Count == 1) return nodes[0];

            // DistanceOnSegment je vždy vzdálenost od Nodes[0].
            // IsForward=true  → pohyb 0 → LengthMeters (stejná interpretace)
            // IsForward=false → pohyb LengthMeters → 0
            // Pro pozici stačí přímo DistanceOnSegment (je to vždy vzdálenost od začátku).
            double dist = Math.Max(0, Math.Min(light.DistanceOnSegment, light.Segment.LengthMeters));

            double accumulated = 0;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                double edgeLen = nodes[i].DistanceTo(nodes[i + 1]);
                if (accumulated + edgeLen >= dist || i == nodes.Count - 2)
                {
                    double t = edgeLen > 0 ? (dist - accumulated) / edgeLen : 0;
                    t = Math.Max(0, Math.Min(1, t));
                    double lat = nodes[i].Latitude  + t * (nodes[i + 1].Latitude  - nodes[i].Latitude);
                    double lon = nodes[i].Longitude + t * (nodes[i + 1].Longitude - nodes[i].Longitude);
                    return new GeoCoordinate(lat, lon);
                }
                accumulated += edgeLen;
            }

            return nodes[nodes.Count - 1]; // fallback: konec segmentu
        }

        // ════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════

        public void Clear() => _lights.Clear();
    }
}

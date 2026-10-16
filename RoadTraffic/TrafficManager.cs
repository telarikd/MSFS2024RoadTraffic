using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MSFSTraffic.Engine
{
    using MSFSTraffic.Models;
    using MSFSTraffic.Roads;

    /// <summary>
    /// Hlavní traffic engine. Spravuje vozidla na reálných silnicích.
    /// Volá se z Program.cs — SimConnect vrstva zůstává tam.
    /// </summary>
    public class TrafficManager
    {
        private readonly IRoadProvider _roadProvider;
        private readonly ITrafficDensityCalculator _densityCalc;
        private readonly Random _rng;

        // ── Stav ──
        private List<RoadSegment> _activeRoads;
        private readonly List<TrafficVehicle> _vehicles;
        private GeoCoordinate _lastRoadFetchPosition;
        private bool _isLoadingRoads;

        // Staging: background thread sem zapise nove silnice,
        // UI thread je prevezme na zacatku Update() — zadna soubeznnost
        private List<RoadSegment> _pendingRoads;
        private readonly object _pendingRoadsLock = new object();

        // Junction index: coord klíč → segmenty s endpointem na tomto místě.
        // Builduje se na UI threadu po každém načtení silnic.
        // Tuple: (RoadSegment seg, bool atStart) — atStart=true = Nodes[0] je na tomto uzlu.
        private Dictionary<string, List<(RoadSegment seg, bool atStart)>> _junctionIndex;

        // Typy silnic na kterych smime spawnovat vozidla (uzivatelem rizeno pres checkboxy).
        private readonly HashSet<RoadType> _enabledRoadTypes;

        // Citac ticku — pro throttling LOD.Light pozicovych updatu.
        private int _tickCount;

        // ── Konfigurace ──

        /// <summary>Poloměr pro načítání silnic v metrech.</summary>
        public double RoadFetchRadiusM { get; set; } = 6000;

        /// <summary>Minimální vzdálenost přesunu hráče pro nové načtení silnic (metry).</summary>
        public double RoadRefetchThresholdM { get; set; } = 2000;

        /// <summary>Vnitřní poloměr, ve kterém se vytváří plná SimConnect vozidla.</summary>
        public double SpawnRadiusM { get; set; } = 3000;

        /// <summary>Vnější poloměr, za kterým se vozidla odstraní.</summary>
        public double DespawnRadiusM { get; set; } = 3000;

        /// <summary>Maximální počet vozidel najednou.</summary>
        public int MaxVehicles { get; set; } = 50;

        /// <summary>SimObject title pro spawn.</summary>
        public string VehicleTitle { get; set; } = "HAmphibiusFemale";

        /// <summary>Aktuální herní čas (hodiny 0-24, nastavuje se z SimConnect).</summary>
        public double SimTimeHours { get; set; } = 12.0;

        /// <summary>Je víkend? (nastavuje se z SimConnect).</summary>
        public bool IsWeekend { get; set; } = false;

        // ── Events pro SimConnect vrstvu ──

        /// <summary>
        /// Zavolá se když engine chce spawnout vozidlo.
        /// Program.cs tohle přeloží na AICreateSimulatedObject_EX1.
        /// </summary>
        public event Action<TrafficVehicle> OnVehicleSpawnRequested;

        /// <summary>
        /// Zavolá se když engine chce odstranit vozidlo.
        /// Program.cs tohle přeloží na AIRemoveObject.
        /// </summary>
        public event Action<TrafficVehicle> OnVehicleDespawnRequested;

        /// <summary>
        /// Zavolá se když engine updatuje pozici vozidla.
        /// Program.cs tohle přeloží na SetDataOnSimObject.
        /// </summary>
        public event Action<TrafficVehicle> OnVehiclePositionUpdated;

        public TrafficManager(IRoadProvider roadProvider, ITrafficDensityCalculator densityCalc)
        {
            _roadProvider = roadProvider;
            _densityCalc = densityCalc;
            _rng = new Random();
            _vehicles = new List<TrafficVehicle>();
            _activeRoads = new List<RoadSegment>();
            _lastRoadFetchPosition = new GeoCoordinate(0, 0);

            // Vychozi: vsechny bezne typy silnic povoleny (Track a Unknown vypnuty)
            _enabledRoadTypes = new HashSet<RoadType>
            {
                RoadType.Motorway,
                RoadType.Trunk,
                RoadType.Primary,
                RoadType.Secondary,
                RoadType.Tertiary,
                RoadType.Residential,
                RoadType.Unclassified
            };
        }

        /// <summary>
        /// Povoli nebo zakaze spawnovani vozidel na danem typu silnice.
        /// Existujici vozidla na zakazanem typu jsou odstavena na dalsim ticku.
        /// </summary>
        public void SetRoadTypeEnabled(RoadType type, bool enabled)
        {
            if (enabled) _enabledRoadTypes.Add(type);
            else         _enabledRoadTypes.Remove(type);
        }

        /// <summary>Uživatelský density slider (0.0 - 2.0).</summary>
        public double UserDensityMultiplier
        {
            get => _densityCalc.UserDensityMultiplier;
            set => _densityCalc.UserDensityMultiplier = value;
        }

        /// <summary>Počet aktivních vozidel.</summary>
        public int ActiveVehicleCount => _vehicles.Count;

        /// <summary>Počet načtených silničních segmentů.</summary>
        public int ActiveRoadCount => _activeRoads?.Count ?? 0;

        /// <summary>Celková délka načtených silnic v km.</summary>
        public double TotalRoadKm
        {
            get
            {
                if (_activeRoads == null) return 0;
                double total = 0;
                foreach (var road in _activeRoads)
                    total += road.LengthMeters;
                return total / 1000.0;
            }
        }

        // ════════════════════════════════════════
        //  HLAVNÍ UPDATE METODY
        // ════════════════════════════════════════

        /// <summary>
        /// Volá se každý tick (50ms). Hlavní update loop.
        /// </summary>
        /// <param name="playerPos">Aktuální pozice hráče.</param>
        /// <param name="deltaTime">Čas od posledního updatu v sekundách.</param>
        public async Task UpdateAsync(GeoCoordinate playerPos, double deltaTime)
        {
            // 1) Načti silnice pokud je potřeba
            await EnsureRoadsLoadedAsync(playerPos);

            // 2) Aktualizuj existující vozidla
            UpdateVehicles(playerPos, deltaTime);

            // 3) Spawnuj nová vozidla pokud je potřeba
            SpawnVehiclesOnRoads(playerPos);
        }

        /// <summary>
        /// Synchronní update — pro případ že nechceš async.
        /// Silnice načítá na pozadí.
        /// </summary>
        public void Update(GeoCoordinate playerPos, double deltaTime)
        {
            // Prevezmi nova silnicni data z background threadu (bez race condition)
            lock (_pendingRoadsLock)
            {
                if (_pendingRoads != null)
                {
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Applying pending roads count={_pendingRoads.Count}");
                    _activeRoads = _pendingRoads;
                    _pendingRoads = null;
                    BuildJunctionIndex(_activeRoads);
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Active roads applied count={_activeRoads.Count}");
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] ActiveRoadCount after apply={ActiveRoadCount}");
                }
            }

            // Spusť načítání silnic na pozadí (fire and forget)
            if (!_isLoadingRoads && ShouldRefetchRoads(playerPos))
            {
                _isLoadingRoads = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await EnsureRoadsLoadedAsync(playerPos);
                    }
                    finally
                    {
                        _isLoadingRoads = false;
                    }
                });
            }

            UpdateVehicles(playerPos, deltaTime);
            SpawnVehiclesOnRoads(playerPos);
        }

        // ════════════════════════════════════════
        //  SILNIČNÍ DATA
        // ════════════════════════════════════════

        private bool ShouldRefetchRoads(GeoCoordinate playerPos)
        {
            if (_activeRoads.Count == 0) return true;
            return _lastRoadFetchPosition.DistanceTo(playerPos) > RoadRefetchThresholdM;
        }

        private async Task EnsureRoadsLoadedAsync(GeoCoordinate playerPos)
        {
            if (!ShouldRefetchRoads(playerPos)) return;

            Console.WriteLine($"[TrafficManager] Loading roads around ({playerPos.Latitude:F4}, {playerPos.Longitude:F4})...");
            RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Road refresh center=({playerPos.Latitude:F6},{playerPos.Longitude:F6}) radius={RoadFetchRadiusM:F0}");

            var loaded = await _roadProvider.GetRoadsAroundAsync(playerPos, RoadFetchRadiusM);
            RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] TrafficManager provider returned count={loaded.Count}");

            if (loaded.Count == 0)
            {
                RoadTraffic.RuntimeDiagnostics.Log("[RoadTraffic.Diag] TrafficManager retained active roads because provider returned no segments.");
                return;
            }

            _lastRoadFetchPosition = playerPos;

            // Bezpecny predani UI threadu — zadny primy zapis do _activeRoads z BG threadu
            lock (_pendingRoadsLock)
            {
                _pendingRoads = loaded;
                RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Pending roads assigned count={_pendingRoads.Count}");
            }

            // Statistiky
            int totalLength = (int)loaded.Sum(r => r.LengthMeters);
            var byType = loaded.GroupBy(r => r.RoadType)
                               .Select(g => $"{g.Key}: {g.Count()}")
                               .ToArray();

            RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Loaded road types=[{string.Join(", ", byType)}]");

            Console.WriteLine($"[TrafficManager] Loaded {loaded.Count} road segments " +
                              $"({totalLength / 1000.0:F1} km total)");
            Console.WriteLine($"  Types: {string.Join(", ", byType)}");

            // Vyčisti vzdálený cache
            _roadProvider.ClearDistantTiles(playerPos);
        }

        // ════════════════════════════════════════
        //  VEHICLE UPDATE
        // ════════════════════════════════════════

        private void UpdateVehicles(GeoCoordinate playerPos, double deltaTime)
        {
            _tickCount++;

            var toRemove = new List<TrafficVehicle>();

            foreach (var vehicle in _vehicles)
            {
                // Despawn vozidla na zakazanem typu silnice (uzivatel odskrtl checkbox)
                if (!_enabledRoadTypes.Contains(vehicle.Segment.RoadType))
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                // Pohyb po segmentu
                bool stillOnSegment = vehicle.UpdatePosition(deltaTime);

                if (!stillOnSegment)
                {
                    // Vozidlo dojelo na konec segmentu — zkus navazujici segment
                    if (!TryTransitionVehicle(vehicle))
                    {
                        // Slepa ulice nebo okraj mapy → despawn
                        toRemove.Add(vehicle);
                        continue;
                    }
                    // Uspesny prechod — padneme dal a aktualizujeme pozici v MSFS
                }

                // Vzdálenost od hráče
                var (pos, heading) = vehicle.GetCurrentPosition();
                double dist = playerPos.DistanceTo(pos);

                if (dist > DespawnRadiusM)
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                // V ramci despawn bubliny zustavaji vsechna vozidla plne SimConnect objekty.
                var newLod = vehicle.DetermineLOD(dist, DespawnRadiusM);

                if (newLod == VehicleLOD.None)
                {
                    // Mimo viditelnost → despawn
                    toRemove.Add(vehicle);
                    continue;
                }

                // LOD přechod
                if (vehicle.HasLODChanged)
                {
                    // TODO: přepnout SimObject model (full vs light)
                }

                // LOD.Light: MSFS pozice se updatuje jen kazdy 30. tick (~500 ms pri 60 Hz).
                // Vozidla jedou 120-140 km/h = ~35 m/s → za 500 ms ujela ~17 m.
                // Tato nepresnost je pri 5-15 km vzdalenosti zcela nepostrehnutelna.
                if (newLod == VehicleLOD.Light && (_tickCount % 30) != 0)
                    continue;

                // Update pozice v MSFS
                OnVehiclePositionUpdated?.Invoke(vehicle);
            }

            // Despawn
            foreach (var vehicle in toRemove)
            {
                _vehicles.Remove(vehicle);
                OnVehicleDespawnRequested?.Invoke(vehicle);
            }
        }

        // ════════════════════════════════════════
        //  VEHICLE SPAWNING
        // ════════════════════════════════════════

        private void SpawnVehiclesOnRoads(GeoCoordinate playerPos)
        {
            if (_activeRoads.Count == 0) return;

            // MaxVehicles remains the user cap. Full SimConnect objects also have a local
            // resource budget so a high UI cap cannot overload MSFS inside the traffic bubble.
            const int MaxLocalFullVehicles = 80;
            int targetVehicleCount = Math.Min(
                Math.Min(MaxVehicles, MaxLocalFullVehicles),
                (int)Math.Round(MaxVehicles * UserDensityMultiplier));
            if (_vehicles.Count >= targetVehicleCount) return;

            bool logSpawnDiag = (_tickCount % 60) == 0;
            int disabledTypeSkipCount = 0;
            int distanceSkipCount = 0;
            int densityZeroCount = 0;
            int representativeLogCount = 0;
            double nearestEnabledRoadDistance = double.MaxValue;
            var candidateCountsByType = new Dictionary<RoadType, int>();

            if (logSpawnDiag)
            {
                RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn update activeRoads={_activeRoads.Count} enabledRoadTypes={string.Join(",", _enabledRoadTypes)}");
            }

            // Kandidati musi protinat lokalni spawn bublinu. Serad je od nejblizsiho.
            var candidates = new List<KeyValuePair<double, RoadSegment>>();
            foreach (var road in _activeRoads)
            {
                if (!_enabledRoadTypes.Contains(road.RoadType))
                {
                    disabledTypeSkipCount++;
                    continue;
                }

                double d = road.DistanceToPoint(playerPos);
                if (d < nearestEnabledRoadDistance)
                    nearestEnabledRoadDistance = d;

                if (d <= SpawnRadiusM)
                {
                    candidates.Add(new KeyValuePair<double, RoadSegment>(d, road));
                    if (!candidateCountsByType.ContainsKey(road.RoadType))
                        candidateCountsByType[road.RoadType] = 0;
                    candidateCountsByType[road.RoadType]++;
                }
                else
                {
                    distanceSkipCount++;
                }
            }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));

            if (logSpawnDiag)
            {
                string byType = string.Join(", ", candidateCountsByType.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                string nearestEnabled = nearestEnabledRoadDistance == double.MaxValue ? "none" : nearestEnabledRoadDistance.ToString("F0");
                RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn candidates count={candidates.Count} byType=[{byType}] nearestEnabledRoad={nearestEnabled} spawnRadius={SpawnRadiusM:F0} skippedDisabledType={disabledTypeSkipCount} skippedDistanceThreshold={distanceSkipCount}");
            }

            foreach (var kv in candidates)
            {
                if (_vehicles.Count >= targetVehicleCount) break;

                var road = kv.Value;
                double roadDist = kv.Key;

                int idealCount = _densityCalc.CalculateVehicleCount(road, SimTimeHours, IsWeekend);

                if (idealCount <= 0)
                {
                    densityZeroCount++;
                    if (logSpawnDiag && representativeLogCount < 8)
                    {
                        RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn candidate road={road.OsmId} type={road.RoadType} dist={roadDist:F0} ideal=0 skippedDensityResult0");
                        representativeLogCount++;
                    }
                    continue;
                }

                int currentCount = _vehicles.Count(v => v.Segment.OsmId == road.OsmId);
                int toSpawn = idealCount - currentCount;

                if (logSpawnDiag && representativeLogCount < 8)
                {
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn candidate road={road.OsmId} type={road.RoadType} dist={roadDist:F0} ideal={idealCount} current={currentCount} toSpawn={toSpawn}");
                    representativeLogCount++;
                }

                for (int i = 0; i < toSpawn && _vehicles.Count < targetVehicleCount; i++)
                {
                    SpawnVehicleOnRoad(road, playerPos, logSpawnDiag);
                }
            }

            if (logSpawnDiag)
            {
                RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn densityZero count={densityZeroCount}");
                RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn target vehicles={targetVehicleCount} active={_vehicles.Count}");
            }
        }

        private void SpawnVehicleOnRoad(RoadSegment road, GeoCoordinate playerPos, bool logSpawnDiag)
        {
            // Vyber nahodnou pozici jen z casti silnice, ktera lezi uvnitr spawn bubliny.
            // Kandidat mohl bublinu pouze liznout; nahodny bod po cele delce by pak byl daleko.
            double nearestRoadDistance = road.DistanceToPoint(playerPos);
            double nearestDistanceAlongRoad = road.GetNearestDistanceAlongSegment(playerPos);
            double allowedOffsetAlongRoad = Math.Max(0, SpawnRadiusM - nearestRoadDistance);
            double minDistOnSegment = Math.Max(0, nearestDistanceAlongRoad - allowedOffsetAlongRoad);
            double maxDistOnSegment = Math.Min(road.LengthMeters, nearestDistanceAlongRoad + allowedOffsetAlongRoad);

            if (maxDistOnSegment <= minDistOnSegment)
                return;

            // Bias toward the nearest point on the road so traffic starts below the player
            // and fans out to the edge of the local bubble instead of clustering at its edge.
            double offsetBefore = nearestDistanceAlongRoad - minDistOnSegment;
            double offsetAfter = maxDistOnSegment - nearestDistanceAlongRoad;
            bool spawnAfterNearestPoint = _rng.NextDouble() < offsetAfter / (offsetBefore + offsetAfter);
            double signedOffset = (spawnAfterNearestPoint ? 1 : -1)
                                * (spawnAfterNearestPoint ? offsetAfter : offsetBefore)
                                * Math.Pow(_rng.NextDouble(), 2);
            double distOnSegment = nearestDistanceAlongRoad + signedOffset;

            // Směr — pokud jednosměrka, jen Forward; jinak náhodně
            TravelDirection direction;
            if (road.IsOneWay)
            {
                direction = TravelDirection.Forward;
            }
            else
            {
                direction = _rng.NextDouble() > 0.5 ? TravelDirection.Forward : TravelDirection.Reverse;
            }

            // Rychlost s variací
            var profile = TrafficProfile.CreateDefaults();
            double speedVariation = 0;
            if (profile.ContainsKey(road.RoadType))
                speedVariation = profile[road.RoadType].SpeedVariationKmh;

            double speed = road.MaxSpeedKmh + (_rng.NextDouble() * 2 - 1) * speedVariation;
            speed = Math.Max(10, speed); // minimálně 10 km/h

            var vehicle = new TrafficVehicle(road, distOnSegment, direction, speed, VehicleTitle);

            // Lateralni offset — pravostranný provoz, s malym jitter
            double jitter = (_rng.NextDouble() - 0.5) * 0.4; // ±0.2 m
            vehicle.LateralOffsetM = GetLaneOffsetM(road.RoadType) + jitter;

            // Zkontroluj vzdálenost od hráče — nespawnuj příliš blízko (pop-in)
            var (pos, _) = vehicle.GetCurrentPosition();
            double dist = playerPos.DistanceTo(pos);

            if (dist < 50)
            {
                if (logSpawnDiag)
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn attempt skippedMinDistance road={road.OsmId} type={road.RoadType} dist={dist:F0}");
                return;
            }
            if (dist > SpawnRadiusM)
            {
                if (logSpawnDiag)
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn attempt skippedDistanceThreshold road={road.OsmId} type={road.RoadType} dist={dist:F0} max={SpawnRadiusM:F0}");
                return;
            }

            // Minimalni rozestup — nespawnuj vozidlo blizko jineho na stejnem segmentu
            const double MinSeparationM = 20.0;
            bool tooClose = false;
            foreach (var existing in _vehicles)
            {
                if (existing.Segment.OsmId != road.OsmId) continue;
                var (exPos, _) = existing.GetCurrentPosition();
                if (pos.DistanceTo(exPos) < MinSeparationM) { tooClose = true; break; }
            }
            if (tooClose)
            {
                if (logSpawnDiag)
                    RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Spawn attempt skippedSpacingTooClose road={road.OsmId} type={road.RoadType} dist={dist:F0}");
                return;
            }

            _vehicles.Add(vehicle);
            RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] Vehicle created vehicle={vehicle.VehicleId} road={road.OsmId} type={road.RoadType} dist={dist:F0}");
            RoadTraffic.RuntimeDiagnostics.Log($"[RoadTraffic.Diag] OnVehicleSpawnRequested raising vehicle={vehicle.VehicleId} road={road.OsmId}");
            OnVehicleSpawnRequested?.Invoke(vehicle);
        }

        /// <summary>
        /// Vraci lateralni offset od stredni cary silnice v metrech (stred praveho pruhu).
        /// </summary>
        private static double GetLaneOffsetM(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:     return 2.5;
                case RoadType.Primary:   return 2.0;
                case RoadType.Secondary: return 1.75;
                default:                 return 1.5;  // Tertiary, Residential, atd.
            }
        }

        // ════════════════════════════════════════
        //  JUNCTION INDEX & SEGMENT CHAINING
        // ════════════════════════════════════════

        /// <summary>
        /// Diskretizuje souradnici na 5 des. mist (~1.1 m presnost).
        /// OSM uzly sdilene dvema ways maji vzdy identickou lat/lon —
        /// toto klicovani je tedy spolehlivym matchem krizovatkoveho uzlu.
        /// </summary>
        private static string CoordKey(GeoCoordinate c)
        {
            int lat = (int)Math.Round(c.Latitude  * 100000);
            int lon = (int)Math.Round(c.Longitude * 100000);
            return lat + "," + lon;
        }

        /// <summary>
        /// Postavi index krizovatkových uzlů ze seznamu segmentů.
        /// Klic = CoordKey(endpoint), hodnota = segmenty ktere tam zacinaji/konci.
        /// </summary>
        private void BuildJunctionIndex(List<RoadSegment> roads)
        {
            _junctionIndex = new Dictionary<string, List<(RoadSegment seg, bool atStart)>>();

            foreach (var seg in roads)
            {
                if (seg.Nodes.Count < 2) continue;

                string startKey = CoordKey(seg.Nodes[0]);
                string endKey   = CoordKey(seg.Nodes[seg.Nodes.Count - 1]);

                if (!_junctionIndex.ContainsKey(startKey))
                    _junctionIndex[startKey] = new List<(RoadSegment, bool)>();
                if (!_junctionIndex.ContainsKey(endKey))
                    _junctionIndex[endKey]   = new List<(RoadSegment, bool)>();

                _junctionIndex[startKey].Add((seg, true));   // Nodes[0] je tady
                _junctionIndex[endKey].Add((seg, false));    // Nodes[last] je tady
            }

            Console.WriteLine($"[TrafficManager] Junction index: {_junctionIndex.Count} uzlu, {roads.Count} segmentu.");
        }

        /// <summary>
        /// Pokusi se prevest vozidlo na navazujici segment v krizovatkе.
        /// Vraci true = prechod probehl (vozidlo pokracuje), false = slepa ulice / okraj mapy.
        /// Prechod meni Segment+Direction+_distTraveled na vozidle IN-PLACE —
        /// SimObjectId zusatva stejne, zadny pop-in v MSFS.
        /// </summary>
        private bool TryTransitionVehicle(TrafficVehicle vehicle)
        {
            if (_junctionIndex == null) return false;

            // Vystupni uzel = ten ke kteremu vozidlo prave dorazilo
            // Forward → Nodes[last], Reverse → Nodes[0]
            GeoCoordinate exitNode = vehicle.Direction == TravelDirection.Forward
                ? vehicle.Segment.Nodes[vehicle.Segment.Nodes.Count - 1]
                : vehicle.Segment.Nodes[0];

            string key = CoordKey(exitNode);
            List<(RoadSegment seg, bool atStart)> candidates;
            if (!_junctionIndex.TryGetValue(key, out candidates) || candidates.Count == 0)
                return false;

            // Filtruj kandidaty:
            //  - vyrad aktualni segment (nechceme U-turn zpet)
            //  - respektuj jednosmerky
            var options = new List<(RoadSegment seg, TravelDirection dir)>();
            foreach (var entry in candidates)
            {
                if (entry.seg.OsmId == vehicle.Segment.OsmId) continue;

                if (entry.atStart)
                {
                    // Vstupujeme od Nodes[0] → jedeme Forward
                    // OneWay: Forward je vzdy povoleny
                    options.Add((entry.seg, TravelDirection.Forward));
                }
                else
                {
                    // Vstupujeme od Nodes[last] → jedeme Reverse
                    // OneWay: Reverse neni povoleny
                    if (!entry.seg.IsOneWay)
                        options.Add((entry.seg, TravelDirection.Reverse));
                }
            }

            if (options.Count == 0) return false;

            // Nahodne vyber nasledujici segment
            var chosen = options[_rng.Next(options.Count)];
            vehicle.TransitionToSegment(chosen.seg, chosen.dir);
            return true;
        }

        // ════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════

        /// <summary>
        /// Odstraní všechna vozidla (při odpojení, shutdown).
        /// </summary>
        public void RemoveAllVehicles()
        {
            foreach (var vehicle in _vehicles.ToList())
            {
                OnVehicleDespawnRequested?.Invoke(vehicle);
            }
            _vehicles.Clear();
            Console.WriteLine("[TrafficManager] All vehicles removed.");
        }

        /// <summary>
        /// Registruje SimConnect ObjectID k vozidlu po úspěšném spawnu.
        /// Volá se z Program.cs v OnRecvAssignedObjectId.
        /// </summary>
        public void RegisterSimObjectId(int vehicleId, uint simObjectId)
        {
            var vehicle = _vehicles.FirstOrDefault(v => v.VehicleId == vehicleId);
            if (vehicle != null)
            {
                vehicle.SimObjectId = simObjectId;
                vehicle.IsSpawned = true;
            }
        }

        /// <summary>
        /// Najde vozidlo podle jeho interního ID.
        /// </summary>
        public TrafficVehicle GetVehicleById(int vehicleId)
        {
            return _vehicles.FirstOrDefault(v => v.VehicleId == vehicleId);
        }
    }
}

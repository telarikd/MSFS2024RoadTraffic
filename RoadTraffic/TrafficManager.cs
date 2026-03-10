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
        private readonly OverpassRoadProvider _roadProvider;
        private readonly TrafficDensityCalculator _densityCalc;
        private readonly Random _rng;
        private readonly FarTrafficManager _farTraffic;

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

        public TrafficManager()
        {
            _roadProvider = new OverpassRoadProvider();
            _densityCalc = new TrafficDensityCalculator();
            _rng = new Random();
            _farTraffic = new FarTrafficManager();
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

        /// <summary>Počet aktivních far-traffic světel (5–15 km vrstva).</summary>
        public int FarLightCount => _farTraffic.LightCount;

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
                    _activeRoads = _pendingRoads;
                    _pendingRoads = null;
                    BuildJunctionIndex(_activeRoads);
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
            _farTraffic.Update(_activeRoads, playerPos, deltaTime);
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

            var loaded = await _roadProvider.GetRoadsAroundAsync(playerPos, RoadFetchRadiusM);
            _lastRoadFetchPosition = playerPos;

            // Bezpecny predani UI threadu — zadny primy zapis do _activeRoads z BG threadu
            lock (_pendingRoadsLock)
            {
                _pendingRoads = loaded;
            }

            var _activeRoads = loaded; // jen pro statistiky nize

            // Statistiky
            int totalLength = (int)_activeRoads.Sum(r => r.LengthMeters);
            var byType = _activeRoads.GroupBy(r => r.RoadType)
                                     .Select(g => $"{g.Key}: {g.Count()}")
                                     .ToArray();

            Console.WriteLine($"[TrafficManager] Loaded {_activeRoads.Count} road segments " +
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

                // LOD update
                var newLod = vehicle.DetermineLOD(dist);

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
            if (_vehicles.Count >= MaxVehicles) return;

            // Collect kandidaty — motorway/trunk az 15 km (LOD.Light zona), ostatni do 5 km.
            // Filtruj zakazane typy. Serad od nejblizsi.
            var candidates = new List<KeyValuePair<double, RoadSegment>>();
            foreach (var road in _activeRoads)
            {
                if (!_enabledRoadTypes.Contains(road.RoadType)) continue;

                double d = road.DistanceToPoint(playerPos);
                bool isHighway = road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk;
                double maxDist = isHighway ? 15000 : 5000;

                if (d <= maxDist)
                    candidates.Add(new KeyValuePair<double, RoadSegment>(d, road));
            }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));

            foreach (var kv in candidates)
            {
                if (_vehicles.Count >= MaxVehicles) break;

                var road = kv.Value;
                double roadDist = kv.Key;

                int idealCount = _densityCalc.CalculateVehicleCount(road, SimTimeHours, IsWeekend);

                // LOD.Light zona (5–15 km): 4× density boost pro motorway/trunk —
                // vytvori vizualni efekt hustého provozu viditelného jako světelné tečky
                bool isLodLight = roadDist > 5000 &&
                                  (road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk);
                if (isLodLight)
                    idealCount = Math.Max(idealCount * 4, 6);

                if (idealCount <= 0) continue;

                int currentCount = _vehicles.Count(v => v.Segment.OsmId == road.OsmId);
                int toSpawn = idealCount - currentCount;

                for (int i = 0; i < toSpawn && _vehicles.Count < MaxVehicles; i++)
                {
                    SpawnVehicleOnRoad(road, playerPos);
                }
            }
        }

        private void SpawnVehicleOnRoad(RoadSegment road, GeoCoordinate playerPos)
        {
            // Náhodná pozice na segmentu
            double distOnSegment = _rng.NextDouble() * road.LengthMeters;

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

            if (dist < 50) return;                              // příliš blízko — pop-in
            if (dist > GetMaxSpawnDistM(road.RoadType)) return; // per-type max radius

            // LOD.Light zona (>5 km): povoleno jen pro motorway/trunk
            bool isHighway = road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk;
            if (dist > 5000 && !isHighway) return;

            // Minimalni rozestup — nespawnuj vozidlo blizko jineho na stejnem segmentu
            const double MinSeparationM = 20.0;
            bool tooClose = false;
            foreach (var existing in _vehicles)
            {
                if (existing.Segment.OsmId != road.OsmId) continue;
                var (exPos, _) = existing.GetCurrentPosition();
                if (pos.DistanceTo(exPos) < MinSeparationM) { tooClose = true; break; }
            }
            if (tooClose) return;

            _vehicles.Add(vehicle);
            OnVehicleSpawnRequested?.Invoke(vehicle);
        }

        /// <summary>
        /// Maximalni vzdalenost spawnu od hrace per road type (metry).
        /// Motorway/Trunk jsou casto 3-5 km od letiste, last resort.
        /// </summary>
        private static double GetMaxSpawnDistM(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:   return 15000; // LOD.Light zona: světelné tečky 5–15 km
                case RoadType.Primary: return 2500;
                default:               return 2000;
            }
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
            _farTraffic.Clear();
            Console.WriteLine("[TrafficManager] All vehicles removed.");
        }

        /// <summary>
        /// Vrací pozice všech far-traffic světel pro vykreslení UI vrstvou.
        /// isForward=true → bílá (přijíždějící); isForward=false → červená (zadní světla).
        /// </summary>
        public List<(GeoCoordinate pos, bool isForward)> GetFarLightPositions()
        {
            return _farTraffic.GetLightPositions();
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

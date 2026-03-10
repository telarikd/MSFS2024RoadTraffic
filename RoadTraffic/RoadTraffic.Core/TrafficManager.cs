using RoadTraffic.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RoadTraffic.Core
{
    public class TrafficManager
    {
        private readonly IRoadProvider _roadProvider;
        private readonly TrafficDensityCalculator _densityCalc;
        private readonly Random _rng;
        private readonly List<TrafficVehicle> _vehicles;
        private readonly HashSet<RoadType> _enabledRoadTypes;

        private List<RoadSegment> _activeRoads;
        private GeoCoordinate _lastRoadFetchPosition;
        private Dictionary<string, List<(RoadSegment seg, bool atStart)>> _junctionIndex;
        private int _tickCount;

        public TrafficManager(IRoadProvider roadProvider)
        {
            _roadProvider = roadProvider;
            _densityCalc = new TrafficDensityCalculator();
            _rng = new Random();
            _vehicles = new List<TrafficVehicle>();
            _activeRoads = new List<RoadSegment>();
            _lastRoadFetchPosition = new GeoCoordinate(0, 0);
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

        public event Action<TrafficVehicle> VehicleSpawnRequested;
        public event Action<TrafficVehicle> VehicleDespawnRequested;
        public event Action<TrafficVehicle> VehiclePositionUpdated;

        public double RoadFetchRadiusM { get; set; } = 6000;
        public double RoadRefetchThresholdM { get; set; } = 2000;
        public int MaxVehicles { get; set; } = 50;
        public string VehicleTitle { get; set; } = "HAmphibiusFemale";
        public double SimTimeHours { get; set; } = 12.0;
        public bool IsWeekend { get; set; }

        public double UserDensityMultiplier
        {
            get => _densityCalc.UserDensityMultiplier;
            set => _densityCalc.UserDensityMultiplier = value;
        }

        public int ActiveVehicleCount => _vehicles.Count;
        public int ActiveRoadCount => _activeRoads.Count;
        public double TotalRoadKm => _activeRoads.Sum(road => road.LengthMeters) / 1000.0;

        public void SetRoadTypeEnabled(RoadType type, bool enabled)
        {
            if (enabled)
            {
                _enabledRoadTypes.Add(type);
            }
            else
            {
                _enabledRoadTypes.Remove(type);
            }
        }

        public async Task RefreshRoadsAsync(GeoCoordinate playerPos)
        {
            if (!ShouldRefetchRoads(playerPos))
            {
                return;
            }

            var loaded = await _roadProvider.GetRoadsAroundAsync(playerPos, RoadFetchRadiusM);
            _activeRoads = loaded.ToList();
            _lastRoadFetchPosition = playerPos;
            BuildJunctionIndex(_activeRoads);
            _roadProvider.ClearDistantTiles(playerPos);
        }

        public void Update(GeoCoordinate playerPos, double deltaTime)
        {
            UpdateVehicles(playerPos, deltaTime);
            TrimVehiclesToMaxCount();
            SpawnVehiclesOnRoads(playerPos);
        }

        public void RegisterVehicleSpawn(TrafficVehicle vehicle, uint simObjectId)
        {
            if (vehicle == null)
            {
                return;
            }

            vehicle.SimObjectId = simObjectId;
            vehicle.IsSpawned = true;
        }

        public void RemoveAllVehicles()
        {
            foreach (var vehicle in _vehicles.ToList())
            {
                VehicleDespawnRequested?.Invoke(vehicle);
            }

            _vehicles.Clear();
        }

        private bool ShouldRefetchRoads(GeoCoordinate playerPos)
        {
            return _activeRoads.Count == 0 || _lastRoadFetchPosition.DistanceTo(playerPos) > RoadRefetchThresholdM;
        }

        private void UpdateVehicles(GeoCoordinate playerPos, double deltaTime)
        {
            _tickCount++;
            var toRemove = new List<TrafficVehicle>();

            foreach (var vehicle in _vehicles)
            {
                if (!_enabledRoadTypes.Contains(vehicle.Segment.RoadType))
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                if (!vehicle.UpdatePosition(deltaTime) && !TryTransitionVehicle(vehicle))
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                var current = vehicle.GetCurrentPosition();
                double distance = playerPos.DistanceTo(current.pos);
                var newLod = vehicle.DetermineLOD(distance);
                if (newLod == VehicleLOD.None)
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                if (newLod == VehicleLOD.Light && (_tickCount % 30) != 0)
                {
                    continue;
                }

                VehiclePositionUpdated?.Invoke(vehicle);
            }

            foreach (var vehicle in toRemove)
            {
                _vehicles.Remove(vehicle);
                VehicleDespawnRequested?.Invoke(vehicle);
            }
        }

        private void TrimVehiclesToMaxCount()
        {
            while (_vehicles.Count > MaxVehicles)
            {
                var vehicle = _vehicles[_vehicles.Count - 1];
                _vehicles.RemoveAt(_vehicles.Count - 1);
                VehicleDespawnRequested?.Invoke(vehicle);
            }
        }

        private void SpawnVehiclesOnRoads(GeoCoordinate playerPos)
        {
            if (_activeRoads.Count == 0 || _vehicles.Count >= MaxVehicles)
            {
                return;
            }

            var candidates = new List<KeyValuePair<double, RoadSegment>>();
            foreach (var road in _activeRoads)
            {
                if (!_enabledRoadTypes.Contains(road.RoadType))
                {
                    continue;
                }

                double distance = road.DistanceToPoint(playerPos);
                bool isHighway = road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk;
                if (distance <= (isHighway ? 15000 : 5000))
                {
                    candidates.Add(new KeyValuePair<double, RoadSegment>(distance, road));
                }
            }

            candidates.Sort((left, right) => left.Key.CompareTo(right.Key));

            foreach (var candidate in candidates)
            {
                if (_vehicles.Count >= MaxVehicles)
                {
                    break;
                }

                var road = candidate.Value;
                int idealCount = _densityCalc.CalculateVehicleCount(road, SimTimeHours, IsWeekend);
                bool isLodLight = candidate.Key > 5000 && (road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk);
                if (isLodLight)
                {
                    idealCount = Math.Max(idealCount * 4, 6);
                }

                if (idealCount <= 0)
                {
                    continue;
                }

                int currentCount = _vehicles.Count(vehicle => vehicle.Segment.OsmId == road.OsmId);
                int toSpawn = idealCount - currentCount;
                for (int i = 0; i < toSpawn && _vehicles.Count < MaxVehicles; i++)
                {
                    SpawnVehicleOnRoad(road, playerPos);
                }
            }
        }

        private void SpawnVehicleOnRoad(RoadSegment road, GeoCoordinate playerPos)
        {
            double distOnSegment = _rng.NextDouble() * road.LengthMeters;
            TravelDirection direction = road.IsOneWay || _rng.NextDouble() > 0.5 ? TravelDirection.Forward : TravelDirection.Reverse;

            var profile = TrafficProfile.CreateDefaults();
            double speedVariation = profile.ContainsKey(road.RoadType) ? profile[road.RoadType].SpeedVariationKmh : 0;
            double speed = Math.Max(10, road.MaxSpeedKmh + (_rng.NextDouble() * 2 - 1) * speedVariation);

            var vehicle = new TrafficVehicle(road, distOnSegment, direction, speed, VehicleTitle)
            {
                LateralOffsetM = GetLaneOffsetM(road.RoadType) + (_rng.NextDouble() - 0.5) * 0.4
            };

            var current = vehicle.GetCurrentPosition();
            double distance = playerPos.DistanceTo(current.pos);
            if (distance < 50 || distance > GetMaxSpawnDistM(road.RoadType))
            {
                return;
            }

            bool isHighway = road.RoadType == RoadType.Motorway || road.RoadType == RoadType.Trunk;
            if (distance > 5000 && !isHighway)
            {
                return;
            }

            foreach (var existing in _vehicles)
            {
                if (existing.Segment.OsmId != road.OsmId)
                {
                    continue;
                }

                if (current.pos.DistanceTo(existing.GetCurrentPosition().pos) < 20.0)
                {
                    return;
                }
            }

            _vehicles.Add(vehicle);
            VehicleSpawnRequested?.Invoke(vehicle);
        }

        private static double GetMaxSpawnDistM(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:
                    return 15000;
                case RoadType.Primary:
                    return 2500;
                default:
                    return 2000;
            }
        }

        private static double GetLaneOffsetM(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:
                    return 2.5;
                case RoadType.Primary:
                    return 2.0;
                case RoadType.Secondary:
                    return 1.75;
                default:
                    return 1.5;
            }
        }

        private static string CoordKey(GeoCoordinate coordinate)
        {
            int lat = (int)Math.Round(coordinate.Latitude * 100000);
            int lon = (int)Math.Round(coordinate.Longitude * 100000);
            return lat + "," + lon;
        }

        private void BuildJunctionIndex(List<RoadSegment> roads)
        {
            _junctionIndex = new Dictionary<string, List<(RoadSegment seg, bool atStart)>>();

            foreach (var segment in roads)
            {
                if (segment.Nodes.Count < 2)
                {
                    continue;
                }

                string startKey = CoordKey(segment.Nodes[0]);
                string endKey = CoordKey(segment.Nodes[segment.Nodes.Count - 1]);

                if (!_junctionIndex.ContainsKey(startKey))
                {
                    _junctionIndex[startKey] = new List<(RoadSegment seg, bool atStart)>();
                }

                if (!_junctionIndex.ContainsKey(endKey))
                {
                    _junctionIndex[endKey] = new List<(RoadSegment seg, bool atStart)>();
                }

                _junctionIndex[startKey].Add((segment, true));
                _junctionIndex[endKey].Add((segment, false));
            }
        }

        private bool TryTransitionVehicle(TrafficVehicle vehicle)
        {
            if (_junctionIndex == null)
            {
                return false;
            }

            GeoCoordinate exitNode = vehicle.Direction == TravelDirection.Forward
                ? vehicle.Segment.Nodes[vehicle.Segment.Nodes.Count - 1]
                : vehicle.Segment.Nodes[0];

            if (!_junctionIndex.TryGetValue(CoordKey(exitNode), out List<(RoadSegment seg, bool atStart)> candidates) || candidates.Count == 0)
            {
                return false;
            }

            var options = new List<(RoadSegment seg, TravelDirection dir)>();
            foreach (var entry in candidates)
            {
                if (entry.seg.OsmId == vehicle.Segment.OsmId)
                {
                    continue;
                }

                if (entry.atStart)
                {
                    options.Add((entry.seg, TravelDirection.Forward));
                }
                else if (!entry.seg.IsOneWay)
                {
                    options.Add((entry.seg, TravelDirection.Reverse));
                }
            }

            if (options.Count == 0)
            {
                return false;
            }

            var chosen = options[_rng.Next(options.Count)];
            vehicle.TransitionToSegment(chosen.seg, chosen.dir);
            return true;
        }
    }
}

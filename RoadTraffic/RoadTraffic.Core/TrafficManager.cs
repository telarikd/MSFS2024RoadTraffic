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

        public event Action<TrafficVehicle> VehicleDespawnRequested;
        public event Action<TrafficVehicle> VehiclePositionUpdated;

        public double RoadFetchRadiusM { get; set; } = 6000;
        public double RoadRefetchThresholdM { get; set; } = 2000;
        public double ForwardPredictionSeconds { get; set; } = 20.0;
        public double SpawnRadiusM { get; set; } = 6000.0;
        public double DespawnRadiusM { get; set; } = 7500.0;
        public double FullVisualEnterRadiusM { get; set; } = 3800.0;
        public double FullVisualExitRadiusM { get; set; } = 4200.0;
        public double LightVisualRadiusM { get; set; } = 10000.0;
        public int MaxVehicles { get; set; } = 50;
        public string VehicleTitle { get; set; } = "HAmphibiusFemale";
        public double SimTimeHours { get; set; } = 12.0;
        public bool IsWeekend { get; set; }

        public double UserDensityMultiplier
        {
            get => _densityCalc.UserDensityMultiplier;
            set => _densityCalc.UserDensityMultiplier = value;
        }

        public int ActiveVehicleCount => _vehicles.Count(vehicle => vehicle.LifecycleState != VehicleLifecycleState.Despawning);
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

        public void Update(GeoCoordinate playerPos, double deltaTime, double playerHeadingDeg, double playerGroundSpeedMs)
        {
            UpdateVehicles(playerPos, deltaTime);
            TrimVehiclesToMaxCount();
            SpawnVehiclesOnRoads(playerPos, playerHeadingDeg, playerGroundSpeedMs);
        }

        public TrafficVehicle GetNextPendingSpawn()
        {
            return _vehicles.FirstOrDefault(vehicle =>
                vehicle.LifecycleState == VehicleLifecycleState.PendingSpawn &&
                vehicle.VisualTier == TrafficVisualTier.Full3D);
        }

        public void RegisterVehicleSpawn(TrafficVehicle vehicle, uint simObjectId)
        {
            if (vehicle == null || !_vehicles.Contains(vehicle))
            {
                return;
            }

            if (vehicle.LifecycleState != VehicleLifecycleState.Spawning)
            {
                return;
            }

            vehicle.MarkSpawned(simObjectId);
        }

        public void RemoveAllVehicles()
        {
            foreach (var vehicle in _vehicles.ToList())
            {
                vehicle.MarkDespawning();
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
                if (vehicle.LifecycleState == VehicleLifecycleState.Despawning)
                {
                    toRemove.Add(vehicle);
                    continue;
                }

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
                vehicle.UpdateVisualTier(distance, FullVisualEnterRadiusM, FullVisualExitRadiusM, LightVisualRadiusM);

                if (distance > DespawnRadiusM)
                {
                    toRemove.Add(vehicle);
                    continue;
                }

                var newLod = vehicle.DetermineLOD(distance);

                if (vehicle.HasVisualTierChanged)
                {
                    VehiclePositionUpdated?.Invoke(vehicle);
                }

                if (!vehicle.IsSpawned)
                {
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
                vehicle.MarkDespawning();
                _vehicles.Remove(vehicle);
                VehicleDespawnRequested?.Invoke(vehicle);
            }
        }

        private void TrimVehiclesToMaxCount()
        {
            while (ActiveVehicleCount > MaxVehicles)
            {
                var vehicle = _vehicles[_vehicles.Count - 1];
                vehicle.MarkDespawning();
                _vehicles.RemoveAt(_vehicles.Count - 1);
                VehicleDespawnRequested?.Invoke(vehicle);
            }
        }

        private void SpawnVehiclesOnRoads(GeoCoordinate playerPos, double playerHeadingDeg, double playerGroundSpeedMs)
        {
            if (_activeRoads.Count == 0 || ActiveVehicleCount >= MaxVehicles)
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

                GeoCoordinate candidatePos = GetCandidatePosition(road);
                if (IsWithinForwardBiasedSpawnArea(playerPos, playerHeadingDeg, playerGroundSpeedMs, candidatePos))
                {
                    double distance = playerPos.DistanceTo(candidatePos);
                    candidates.Add(new KeyValuePair<double, RoadSegment>(distance, road));
                }
            }

            candidates.Sort((left, right) => left.Key.CompareTo(right.Key));

            foreach (var candidate in candidates)
            {
                if (ActiveVehicleCount >= MaxVehicles)
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

                int currentCount = _vehicles.Count(vehicle => vehicle.Segment.OsmId == road.OsmId && vehicle.LifecycleState != VehicleLifecycleState.Despawning);
                int toSpawn = idealCount - currentCount;
                for (int i = 0; i < toSpawn && ActiveVehicleCount < MaxVehicles; i++)
                {
                    SpawnVehicleOnRoad(road, playerPos, playerHeadingDeg, playerGroundSpeedMs);
                }
            }
        }

        private void SpawnVehicleOnRoad(RoadSegment road, GeoCoordinate playerPos, double playerHeadingDeg, double playerGroundSpeedMs)
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
            if (!IsWithinForwardBiasedSpawnArea(playerPos, playerHeadingDeg, playerGroundSpeedMs, current.pos))
            {
                return;
            }

            vehicle.UpdateVisualTier(playerPos.DistanceTo(current.pos), FullVisualEnterRadiusM, FullVisualExitRadiusM, LightVisualRadiusM);

            foreach (var existing in _vehicles)
            {
                if (existing.Segment.OsmId != road.OsmId || existing.LifecycleState == VehicleLifecycleState.Despawning)
                {
                    continue;
                }

                if (current.pos.DistanceTo(existing.GetCurrentPosition().pos) < 20.0)
                {
                    return;
                }
            }

            vehicle.MarkPending();
            _vehicles.Add(vehicle);
        }

        private bool IsWithinForwardBiasedSpawnArea(GeoCoordinate playerPos, double playerHeadingDeg, double playerGroundSpeedMs, GeoCoordinate candidatePos)
        {
            double distance = playerPos.DistanceTo(candidatePos);
            double forwardBonus = playerGroundSpeedMs * ForwardPredictionSeconds;

            if (distance > SpawnRadiusM + forwardBonus)
            {
                return false;
            }

            double bearingToCandidate = playerPos.BearingTo(candidatePos);
            double angleDiff = Math.Abs(NormalizeAngle(playerHeadingDeg - bearingToCandidate));

            if (angleDiff < 90)
            {
                return distance <= SpawnRadiusM + forwardBonus;
            }

            return distance <= SpawnRadiusM;
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= 360;
            if (angle > 180) angle -= 360;
            if (angle < -180) angle += 360;
            return angle;
        }

        private static GeoCoordinate GetCandidatePosition(RoadSegment road)
        {
            if (road.Nodes.Count == 0)
            {
                return new GeoCoordinate(0, 0);
            }

            return road.Nodes[road.Nodes.Count / 2];
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

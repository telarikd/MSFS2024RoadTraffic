using System;

namespace RoadTraffic.Core.Models
{
    public enum VehicleLifecycleState
    {
        PendingSpawn,
        Spawning,
        Spawned,
        Despawning
    }

    public class TrafficVehicle
    {
        private static int _nextId = 1;
        private double _distTraveled;
        private double _speedMs;

        public TrafficVehicle(RoadSegment segment, double distOnSegment, TravelDirection direction, double speedKmh, string simObjectTitle)
        {
            VehicleId = _nextId++;
            Segment = segment;
            Direction = direction;
            SimObjectTitle = simObjectTitle;
            _distTraveled = Math.Max(0, Math.Min(distOnSegment, segment.LengthMeters));
            _speedMs = Math.Max(0, speedKmh / 3.6);
            LifecycleState = VehicleLifecycleState.PendingSpawn;
        }

        public int VehicleId { get; }

        public uint SimObjectId { get; private set; }

        public bool IsSpawned => LifecycleState == VehicleLifecycleState.Spawned;

        public string SimObjectTitle { get; }

        public RoadSegment Segment { get; private set; }

        public TravelDirection Direction { get; private set; }

        public double LateralOffsetM { get; set; }

        public VehicleLOD CurrentLOD { get; private set; } = VehicleLOD.Full;

        public VehicleLOD PreviousLOD { get; private set; } = VehicleLOD.Full;

        public VehicleLifecycleState LifecycleState { get; private set; }

        public TrafficVisualTier VisualTier { get; private set; } = TrafficVisualTier.None;

        public TrafficVisualTier PreviousVisualTier { get; private set; } = TrafficVisualTier.None;

        public bool HasLODChanged => CurrentLOD != PreviousLOD;

        public bool HasVisualTierChanged => VisualTier != PreviousVisualTier;

        public void MarkPending()
        {
            SimObjectId = 0;
            LifecycleState = VehicleLifecycleState.PendingSpawn;
        }

        public void MarkSpawning()
        {
            LifecycleState = VehicleLifecycleState.Spawning;
        }

        public void MarkSpawned(uint simObjectId)
        {
            SimObjectId = simObjectId;
            LifecycleState = VehicleLifecycleState.Spawned;
        }

        public void MarkDespawning()
        {
            LifecycleState = VehicleLifecycleState.Despawning;
        }

        public void UpdateVisualTier(double distance, double fullEnterRadius, double fullExitRadius, double lightRadius)
        {
            PreviousVisualTier = VisualTier;

            switch (VisualTier)
            {
                case TrafficVisualTier.Full3D:
                    VisualTier = distance <= fullExitRadius ? TrafficVisualTier.Full3D :
                        distance <= lightRadius ? TrafficVisualTier.LightPoint : TrafficVisualTier.None;
                    break;
                case TrafficVisualTier.LightPoint:
                    VisualTier = distance <= fullEnterRadius ? TrafficVisualTier.Full3D :
                        distance <= lightRadius ? TrafficVisualTier.LightPoint : TrafficVisualTier.None;
                    break;
                default:
                    VisualTier = distance <= fullEnterRadius ? TrafficVisualTier.Full3D :
                        distance <= lightRadius ? TrafficVisualTier.LightPoint : TrafficVisualTier.None;
                    break;
            }
        }

        public bool UpdatePosition(double deltaTime)
        {
            PreviousLOD = CurrentLOD;
            double step = _speedMs * deltaTime;

            if (Direction == TravelDirection.Forward)
            {
                _distTraveled += step;
                return _distTraveled < Segment.LengthMeters;
            }

            _distTraveled -= step;
            return _distTraveled > 0;
        }

        public (GeoCoordinate pos, double headingDeg) GetCurrentPosition()
        {
            var nodes = Segment.Nodes;
            if (nodes.Count == 1)
            {
                return (nodes[0], 0);
            }

            double distance = Math.Max(0, Math.Min(_distTraveled, Segment.LengthMeters));
            double accumulated = 0;

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                double edgeLength = nodes[i].DistanceTo(nodes[i + 1]);
                if (accumulated + edgeLength >= distance || i == nodes.Count - 2)
                {
                    double t = edgeLength > 0 ? (distance - accumulated) / edgeLength : 0;
                    t = Math.Max(0, Math.Min(1, t));

                    double lat = nodes[i].Latitude + t * (nodes[i + 1].Latitude - nodes[i].Latitude);
                    double lon = nodes[i].Longitude + t * (nodes[i + 1].Longitude - nodes[i].Longitude);
                    double heading = nodes[i].BearingTo(nodes[i + 1]);
                    if (Direction == TravelDirection.Reverse)
                    {
                        heading = (heading + 180) % 360;
                    }

                    var position = new GeoCoordinate(lat, lon);
                    if (LateralOffsetM != 0)
                    {
                        position = position.Offset((heading + 90.0) % 360.0, LateralOffsetM);
                    }

                    return (position, heading);
                }

                accumulated += edgeLength;
            }

            GeoCoordinate from = Direction == TravelDirection.Forward ? nodes[nodes.Count - 2] : nodes[1];
            GeoCoordinate to = Direction == TravelDirection.Forward ? nodes[nodes.Count - 1] : nodes[0];
            double fallbackHeading = from.BearingTo(to);
            GeoCoordinate fallbackPosition = LateralOffsetM == 0 ? to : to.Offset((fallbackHeading + 90.0) % 360.0, LateralOffsetM);
            return (fallbackPosition, fallbackHeading);
        }

        public void TransitionToSegment(RoadSegment newSegment, TravelDirection newDirection)
        {
            Segment = newSegment;
            Direction = newDirection;
            _distTraveled = newDirection == TravelDirection.Forward ? 0.0 : newSegment.LengthMeters;
        }

        public VehicleLOD DetermineLOD(double distFromPlayer)
        {
            PreviousLOD = CurrentLOD;

            if (distFromPlayer > 15000)
            {
                CurrentLOD = VehicleLOD.None;
            }
            else if (distFromPlayer > 5000)
            {
                CurrentLOD = VehicleLOD.Light;
            }
            else
            {
                CurrentLOD = VehicleLOD.Full;
            }

            return CurrentLOD;
        }
    }
}

using System;

namespace MSFSTraffic.Models
{
    public class TrafficVehicle
    {
        private static int _nextId = 1;

        // ── Identifikace ──
        public int    VehicleId      { get; }
        public uint   SimObjectId    { get; set; }
        public bool   IsSpawned      { get; set; }
        public string SimObjectTitle { get; }

        // ── Silnicni kontext ──
        public RoadSegment     Segment   { get; }
        public TravelDirection Direction { get; }

        // ── Pohyb ──
        private double _distTraveled;   // metry od zacatku segmentu
        private double _speedMs;        // rychlost v m/s

        // ── LOD ──
        public VehicleLOD CurrentLOD  { get; private set; } = VehicleLOD.Full;
        public VehicleLOD PreviousLOD { get; private set; } = VehicleLOD.Full;
        public bool HasLODChanged     => CurrentLOD != PreviousLOD;

        public TrafficVehicle(RoadSegment segment, double distOnSegment,
                              TravelDirection direction, double speedKmh, string simObjectTitle)
        {
            VehicleId      = _nextId++;
            Segment        = segment;
            Direction      = direction;
            SimObjectTitle = simObjectTitle;
            _distTraveled  = Math.Max(0, Math.Min(distOnSegment, segment.LengthMeters));
            _speedMs       = Math.Max(0, speedKmh / 3.6);
        }

        /// <summary>
        /// Posune vozidlo o deltaTime sekund. Vraci false kdyz dojelo na konec segmentu.
        /// </summary>
        public bool UpdatePosition(double deltaTime)
        {
            PreviousLOD = CurrentLOD;

            double step = _speedMs * deltaTime;

            if (Direction == TravelDirection.Forward)
            {
                _distTraveled += step;
                if (_distTraveled >= Segment.LengthMeters)
                    return false;   // dojelo na konec
            }
            else
            {
                _distTraveled -= step;
                if (_distTraveled <= 0)
                    return false;   // dojelo na zacatek
            }

            return true;
        }

        /// <summary>
        /// Vrati aktualni pozici a heading vozidla interpolaci mezi uzly segmentu.
        /// </summary>
        public (GeoCoordinate pos, double headingDeg) GetCurrentPosition()
        {
            var nodes = Segment.Nodes;
            if (nodes.Count == 1)
                return (nodes[0], 0);

            // Zjisti na ktere hrane se vozidlo nachazi
            double dist = Direction == TravelDirection.Forward
                ? _distTraveled
                : Segment.LengthMeters - _distTraveled;

            dist = Math.Max(0, Math.Min(dist, Segment.LengthMeters));

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

                    double heading = nodes[i].BearingTo(nodes[i + 1]);

                    // Pokud jedeme pozpatku, otocime heading o 180°
                    if (Direction == TravelDirection.Reverse)
                        heading = (heading + 180) % 360;

                    return (new GeoCoordinate(lat, lon), heading);
                }
                accumulated += edgeLen;
            }

            // Fallback — konec segmentu
            var last = nodes[nodes.Count - 1];
            var prev = nodes[nodes.Count - 2];
            double fallbackHeading = prev.BearingTo(last);
            if (Direction == TravelDirection.Reverse)
                fallbackHeading = (fallbackHeading + 180) % 360;

            return (last, fallbackHeading);
        }

        /// <summary>
        /// Aktualizuje LOD podle vzdalenosti od hrace. Vraci novy LOD.
        /// </summary>
        public VehicleLOD DetermineLOD(double distFromPlayer)
        {
            PreviousLOD = CurrentLOD;

            if (distFromPlayer > 2500)
                CurrentLOD = VehicleLOD.None;
            else
                CurrentLOD = VehicleLOD.Full;

            return CurrentLOD;
        }
    }
}

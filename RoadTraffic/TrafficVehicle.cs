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
        public RoadSegment     Segment   { get; private set; }
        public TravelDirection Direction { get; private set; }

        // ── Pohyb ──
        private double _distTraveled;   // metry od zacatku segmentu
        private double _speedMs;        // rychlost v m/s

        /// <summary>
        /// Lateralni offset od stredni cary silnice v metrech.
        /// Kladna hodnota = vpravo od smeru jizdy (pravostranný provoz).
        /// </summary>
        public double LateralOffsetM { get; set; } = 0;

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

            // Zjisti na ktere hrane se vozidlo nachazi.
            // _distTraveled = vzdalenost od Nodes[0] pro OBA smery:
            //   Forward: roste 0 → LengthMeters, exit u Nodes[last]
            //   Reverse: klesa LengthMeters → 0,  exit u Nodes[0]
            double dist = _distTraveled;

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

                    var pos = new GeoCoordinate(lat, lon);

                    // Lateralni offset — pravostranný provoz (+90° od smeru jizdy)
                    if (LateralOffsetM != 0)
                    {
                        double perpBearing = (heading + 90.0) % 360.0;
                        pos = pos.Offset(perpBearing, LateralOffsetM);
                    }

                    return (pos, heading);
                }
                accumulated += edgeLen;
            }

            // Fallback — vozidlo je na konci posledni hrany
            // Forward → u Nodes[last], Reverse → u Nodes[0]
            GeoCoordinate fbA, fbB;
            if (Direction == TravelDirection.Forward)
            {
                fbA = nodes[nodes.Count - 2];
                fbB = nodes[nodes.Count - 1];
            }
            else
            {
                fbA = nodes[1];
                fbB = nodes[0];
            }

            double fallbackHeading = fbA.BearingTo(fbB);
            GeoCoordinate fallbackPos = fbB;
            if (LateralOffsetM != 0)
            {
                double perpBearing = (fallbackHeading + 90.0) % 360.0;
                fallbackPos = fbB.Offset(perpBearing, LateralOffsetM);
            }

            return (fallbackPos, fallbackHeading);
        }

        /// <summary>
        /// Prejde na novy navazujici segment bez despawnu/respawnu (stejne SimObjectId).
        /// Forward → zacina u Nodes[0] (_distTraveled = 0).
        /// Reverse → zacina u Nodes[last] (_distTraveled = LengthMeters, klesa k Nodes[0]).
        /// </summary>
        public void TransitionToSegment(RoadSegment newSegment, TravelDirection newDirection)
        {
            Segment   = newSegment;
            Direction = newDirection;
            _distTraveled = newDirection == TravelDirection.Forward
                ? 0.0
                : newSegment.LengthMeters;
        }

        /// <summary>
        /// Aktualizuje LOD podle vzdalenosti od hrace. Vraci novy LOD.
        /// </summary>
        public VehicleLOD DetermineLOD(double distFromPlayer)
        {
            PreviousLOD = CurrentLOD;

            if (distFromPlayer > 15000)
                CurrentLOD = VehicleLOD.None;
            else if (distFromPlayer > 5000)
                CurrentLOD = VehicleLOD.Light;   // světelné tečky 5–15 km (motorway/trunk)
            else
                CurrentLOD = VehicleLOD.Full;

            return CurrentLOD;
        }
    }
}

using System;
using System.Collections.Generic;

namespace MSFSTraffic.Models
{
    public class RoadSegment
    {
        public long             OsmId       { get; }
        public RoadType         RoadType    { get; }
        public List<GeoCoordinate> Nodes    { get; }
        public int              MaxSpeedKmh { get; }
        public int              Lanes       { get; }
        public bool             IsOneWay    { get; }
        public string           Name        { get; }

        /// <summary>Celkova delka segmentu v metrech (pocitano z uzlu).</summary>
        public double LengthMeters { get; }

        public RoadSegment(long osmId, RoadType roadType, List<GeoCoordinate> nodes,
                           int maxSpeedKmh, int lanes, bool isOneWay, string name)
        {
            OsmId       = osmId;
            RoadType    = roadType;
            Nodes       = nodes;
            IsOneWay    = isOneWay;
            Name        = name ?? string.Empty;
            Lanes       = Math.Max(1, lanes);

            // Dosad default rychlost pokud OSM neobsahuje maxspeed
            MaxSpeedKmh = maxSpeedKmh > 0 ? maxSpeedKmh : DefaultSpeedForType(roadType);

            LengthMeters = ComputeLength(nodes);
        }

        /// <summary>
        /// Priblizna vzdalenost nejblizsiho bodu na tomto segmentu k danemu bodu.
        /// Prochazi vsechny hrany a vraci minimum.
        /// </summary>
        public double DistanceToPoint(GeoCoordinate point)
        {
            if (Nodes.Count == 0) return double.MaxValue;
            if (Nodes.Count == 1) return Nodes[0].DistanceTo(point);

            double minDist = double.MaxValue;

            for (int i = 0; i < Nodes.Count - 1; i++)
            {
                double d = DistanceToSegment(point, Nodes[i], Nodes[i + 1]);
                if (d < minDist) minDist = d;
            }

            return minDist;
        }

        // ── Private helpers ──

        private static double ComputeLength(List<GeoCoordinate> nodes)
        {
            double total = 0;
            for (int i = 0; i < nodes.Count - 1; i++)
                total += nodes[i].DistanceTo(nodes[i + 1]);
            return total;
        }

        /// <summary>
        /// Vzdalenost bodu P k usecce AB (v metrech, priblizna — pres lat/lon linear approx).
        /// </summary>
        private static double DistanceToSegment(GeoCoordinate p, GeoCoordinate a, GeoCoordinate b)
        {
            // Prevod na local Cartesian (metry) pro geometrii
            double cosLat = Math.Cos(a.Latitude * Math.PI / 180.0);
            const double metersPerDeg = 111_320.0;

            double ax = 0, ay = 0;
            double bx = (b.Longitude - a.Longitude) * metersPerDeg * cosLat;
            double by = (b.Latitude  - a.Latitude)  * metersPerDeg;
            double px = (p.Longitude - a.Longitude) * metersPerDeg * cosLat;
            double py = (p.Latitude  - a.Latitude)  * metersPerDeg;

            double dx = bx - ax;
            double dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            double t = lenSq > 0 ? Math.Max(0, Math.Min(1, (px * dx + py * dy) / lenSq)) : 0;

            double closestX = ax + t * dx;
            double closestY = ay + t * dy;

            double diffX = px - closestX;
            double diffY = py - closestY;
            return Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        private static int DefaultSpeedForType(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:     return 120;
                case RoadType.Trunk:        return 90;
                case RoadType.Primary:      return 70;
                case RoadType.Secondary:    return 60;
                case RoadType.Tertiary:     return 50;
                case RoadType.Residential:  return 30;
                case RoadType.Unclassified: return 30;
                case RoadType.Track:        return 20;
                default:                    return 30;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace RoadTraffic.Core.Models
{
    public class RoadSegment
    {
        public RoadSegment(long osmId, RoadType roadType, List<GeoCoordinate> nodes, int maxSpeedKmh, int lanes, bool isOneWay, string name)
        {
            OsmId = osmId;
            RoadType = roadType;
            Nodes = nodes;
            IsOneWay = isOneWay;
            Name = name ?? string.Empty;
            Lanes = Math.Max(1, lanes);
            MaxSpeedKmh = maxSpeedKmh > 0 ? maxSpeedKmh : DefaultSpeedForType(roadType);
            LengthMeters = ComputeLength(nodes);
        }

        public long OsmId { get; }

        public RoadType RoadType { get; }

        public List<GeoCoordinate> Nodes { get; }

        public int MaxSpeedKmh { get; }

        public int Lanes { get; }

        public bool IsOneWay { get; }

        public string Name { get; }

        public double LengthMeters { get; }

        public double DistanceToPoint(GeoCoordinate point)
        {
            if (Nodes.Count == 0)
            {
                return double.MaxValue;
            }

            if (Nodes.Count == 1)
            {
                return Nodes[0].DistanceTo(point);
            }

            double minDist = double.MaxValue;
            for (int i = 0; i < Nodes.Count - 1; i++)
            {
                double distance = DistanceToSegment(point, Nodes[i], Nodes[i + 1]);
                if (distance < minDist)
                {
                    minDist = distance;
                }
            }

            return minDist;
        }

        private static double ComputeLength(List<GeoCoordinate> nodes)
        {
            double total = 0;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                total += nodes[i].DistanceTo(nodes[i + 1]);
            }

            return total;
        }

        private static double DistanceToSegment(GeoCoordinate point, GeoCoordinate start, GeoCoordinate end)
        {
            double cosLat = Math.Cos(start.Latitude * Math.PI / 180.0);
            const double metersPerDeg = 111320.0;

            double bx = (end.Longitude - start.Longitude) * metersPerDeg * cosLat;
            double by = (end.Latitude - start.Latitude) * metersPerDeg;
            double px = (point.Longitude - start.Longitude) * metersPerDeg * cosLat;
            double py = (point.Latitude - start.Latitude) * metersPerDeg;

            double lenSq = bx * bx + by * by;
            double t = lenSq > 0 ? Math.Max(0, Math.Min(1, (px * bx + py * by) / lenSq)) : 0;

            double closestX = t * bx;
            double closestY = t * by;
            double diffX = px - closestX;
            double diffY = py - closestY;
            return Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        private static int DefaultSpeedForType(RoadType type)
        {
            switch (type)
            {
                case RoadType.Motorway:
                    return 140;
                case RoadType.Trunk:
                    return 90;
                case RoadType.Primary:
                    return 70;
                case RoadType.Secondary:
                    return 60;
                case RoadType.Tertiary:
                    return 50;
                case RoadType.Residential:
                case RoadType.Unclassified:
                default:
                    return 30;
            }
        }
    }
}

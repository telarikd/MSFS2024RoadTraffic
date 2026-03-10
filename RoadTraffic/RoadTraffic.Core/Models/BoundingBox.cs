using System;

namespace RoadTraffic.Core.Models
{
    public class BoundingBox
    {
        public BoundingBox(double minLat, double minLon, double maxLat, double maxLon)
        {
            MinLat = minLat;
            MinLon = minLon;
            MaxLat = maxLat;
            MaxLon = maxLon;
        }

        public double MinLat { get; }

        public double MinLon { get; }

        public double MaxLat { get; }

        public double MaxLon { get; }

        public static BoundingBox FromCenter(GeoCoordinate center, double radiusMeters)
        {
            double latDelta = radiusMeters / 111320.0;
            double lonDelta = radiusMeters / (111320.0 * Math.Cos(center.Latitude * Math.PI / 180.0));

            return new BoundingBox(
                center.Latitude - latDelta,
                center.Longitude - lonDelta,
                center.Latitude + latDelta,
                center.Longitude + lonDelta);
        }

        public override string ToString()
        {
            return $"[{MinLat:F6},{MinLon:F6} -> {MaxLat:F6},{MaxLon:F6}]";
        }
    }
}

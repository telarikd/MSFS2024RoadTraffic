using System;

namespace MSFSTraffic.Models
{
    public class BoundingBox
    {
        public double MinLat { get; }
        public double MinLon { get; }
        public double MaxLat { get; }
        public double MaxLon { get; }

        public BoundingBox(double minLat, double minLon, double maxLat, double maxLon)
        {
            MinLat = minLat;
            MinLon = minLon;
            MaxLat = maxLat;
            MaxLon = maxLon;
        }

        /// <summary>
        /// Vytvori bounding box ze stredu a polomeru v metrech.
        /// </summary>
        public static BoundingBox FromCenter(GeoCoordinate center, double radiusMeters)
        {
            // 1 stupen latitude ~ 111 320 m
            double latDelta = radiusMeters / 111_320.0;

            // 1 stupen longitude zavisi na latitude
            double lonDelta = radiusMeters / (111_320.0 * Math.Cos(center.Latitude * Math.PI / 180.0));

            return new BoundingBox(
                center.Latitude  - latDelta,
                center.Longitude - lonDelta,
                center.Latitude  + latDelta,
                center.Longitude + lonDelta);
        }

        public override string ToString() =>
            $"[{MinLat:F6},{MinLon:F6} → {MaxLat:F6},{MaxLon:F6}]";
    }
}

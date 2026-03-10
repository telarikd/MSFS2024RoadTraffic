using System;

namespace RoadTraffic.Core.Models
{
    public class GeoCoordinate
    {
        private const double EarthRadiusM = 6371000.0;

        public GeoCoordinate(double latitude, double longitude)
            : this(latitude, longitude, 0)
        {
        }

        public GeoCoordinate(double latitude, double longitude, double altitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public double Altitude { get; }

        public double DistanceTo(GeoCoordinate other)
        {
            double dLat = ToRad(other.Latitude - Latitude);
            double dLon = ToRad(other.Longitude - Longitude);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(Latitude)) * Math.Cos(ToRad(other.Latitude))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusM * c;
        }

        public double BearingTo(GeoCoordinate other)
        {
            double dLon = ToRad(other.Longitude - Longitude);
            double lat1 = ToRad(Latitude);
            double lat2 = ToRad(other.Latitude);

            double x = Math.Sin(dLon) * Math.Cos(lat2);
            double y = Math.Cos(lat1) * Math.Sin(lat2)
                - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

            double bearing = Math.Atan2(x, y);
            return (ToDeg(bearing) + 360) % 360;
        }

        public GeoCoordinate Offset(double bearingDeg, double distanceMeters)
        {
            double delta = distanceMeters / EarthRadiusM;
            double theta = ToRad(bearingDeg);
            double lat1 = ToRad(Latitude);
            double lon1 = ToRad(Longitude);

            double lat2 = Math.Asin(
                Math.Sin(lat1) * Math.Cos(delta)
                + Math.Cos(lat1) * Math.Sin(delta) * Math.Cos(theta));

            double lon2 = lon1 + Math.Atan2(
                Math.Sin(theta) * Math.Sin(delta) * Math.Cos(lat1),
                Math.Cos(delta) - Math.Sin(lat1) * Math.Sin(lat2));

            return new GeoCoordinate(ToDeg(lat2), (ToDeg(lon2) + 540.0) % 360.0 - 180.0);
        }

        private static double ToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        private static double ToDeg(double rad)
        {
            return rad * 180.0 / Math.PI;
        }

        public override string ToString()
        {
            return $"({Latitude:F6}, {Longitude:F6})";
        }
    }
}

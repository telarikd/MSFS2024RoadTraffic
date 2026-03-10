namespace RoadTraffic.Core.Models
{
    public sealed class PlayerPosition
    {
        public PlayerPosition(double latitude, double longitude, double altitude, double headingDeg, double groundSpeedMs)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
            HeadingDeg = headingDeg;
            GroundSpeedMs = groundSpeedMs;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public double Altitude { get; }

        public double HeadingDeg { get; }

        public double GroundSpeedMs { get; }

        public GeoCoordinate ToGeoCoordinate()
        {
            return new GeoCoordinate(Latitude, Longitude, Altitude);
        }
    }
}

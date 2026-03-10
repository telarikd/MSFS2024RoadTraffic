namespace RoadTraffic.Core.Models
{
    public sealed class PlayerPosition
    {
        public PlayerPosition(double latitude, double longitude, double altitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public double Altitude { get; }

        public GeoCoordinate ToGeoCoordinate()
        {
            return new GeoCoordinate(Latitude, Longitude, Altitude);
        }
    }
}

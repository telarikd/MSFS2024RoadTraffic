namespace RoadTraffic.Core.Models
{
    public class LightMarker
    {
        public int VehicleId { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public bool IsRed { get; set; }

        public long RoadOsmId { get; set; }
    }
}

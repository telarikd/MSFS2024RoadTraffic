namespace RoadTraffic.Core
{
    public sealed class TrafficSessionSnapshot
    {
        public bool IsConnected { get; set; }

        public int ActiveVehicles { get; set; }

        public int MaxVehicles { get; set; }

        public int ActiveRoads { get; set; }

        public double TotalRoadKm { get; set; }
    }
}

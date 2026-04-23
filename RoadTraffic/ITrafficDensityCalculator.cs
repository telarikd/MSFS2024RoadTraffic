namespace MSFSTraffic.Models
{
    public interface ITrafficDensityCalculator
    {
        double UserDensityMultiplier { get; set; }
        int CalculateVehicleCount(RoadSegment road, double simTimeHours, bool isWeekend);
    }
}

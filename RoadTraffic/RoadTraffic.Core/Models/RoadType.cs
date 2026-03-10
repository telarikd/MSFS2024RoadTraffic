namespace RoadTraffic.Core.Models
{
    public enum RoadType
    {
        Unknown,
        Motorway,
        Trunk,
        Primary,
        Secondary,
        Tertiary,
        Residential,
        Unclassified,
        Track
    }

    public enum VehicleLOD
    {
        None,
        Light,
        Full
    }

    public enum TravelDirection
    {
        Forward,
        Reverse
    }
}

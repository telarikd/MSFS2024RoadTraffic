namespace MSFSTraffic.Models
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
        None,   // mimo dosah — despawn
        Full    // plny detail
    }

    public enum TravelDirection
    {
        Forward,
        Reverse
    }
}

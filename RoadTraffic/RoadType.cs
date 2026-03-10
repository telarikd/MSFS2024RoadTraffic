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
        Light,  // vzdálená světelná tečka (5–15 km, jen motorway/trunk)
        Full    // plný detail (0–5 km)
    }

    public enum TravelDirection
    {
        Forward,
        Reverse
    }
}

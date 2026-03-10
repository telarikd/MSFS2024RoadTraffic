namespace MSFSTraffic.Models
{
    /// <summary>
    /// Čistě vizuální objekt reprezentující vzdálené světlo provozu (5–15 km).
    /// Nemá SimObject, nezávisí na TrafficVehicle — jen pohyb po silnici + pozice.
    /// </summary>
    public class FarTrafficLight
    {
        public RoadSegment Segment;
        public double      DistanceOnSegment;   // metry od Nodes[0]
        public double      SpeedMs;             // m/s
        public bool        IsForward;           // smer pohybu
    }
}

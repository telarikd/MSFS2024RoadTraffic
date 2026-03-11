namespace MSFSTraffic.Engine
{
    /// <summary>
    /// Lehký datový objekt pro jeden pohyblivý světelný bod (FlareEffect SimObject).
    /// Záměrně bez properties — žádné alokace v hot loop.
    /// </summary>
    public class TrafficCar
    {
        public double Lat;       // zeměpisná šířka
        public double Lon;       // zeměpisná délka
        public double Heading;   // směr jízdy ve stupních (0 = sever)
        public float  Speed;     // rychlost m/s
        public uint   ObjectId;  // SimConnect AI object ID; 0 = ještě nespawnováno
        public bool   IsSpawned; // true po potvrzení od OnRecvAssignedObjectId
        public uint   RequestId; // ID AICreate requestu, slouží k párování callbacku
    }
}

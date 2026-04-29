using System.Runtime.InteropServices;

namespace RoadTraffic
{
    internal enum SimConnectRequests : uint
    {
        PlayerPosition = 1
    }

    internal enum SimConnectDefinitions : uint
    {
        InitPosition = 1,
        PlayerPosition = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlayerPositionData
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
    }
}

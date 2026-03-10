using Microsoft.FlightSimulator.SimConnect;
using RoadTraffic.Core.Models;
using System;

namespace RoadTraffic.SimConnect
{
    public interface ISimConnectService
    {
        event Action<PlayerPosition> PlayerPositionReceived;
        event Action<uint> ObjectSpawned;
        event Action<bool> ConnectionStateChanged;

        void Connect(IntPtr handle);

        void Disconnect();

        void RequestPlayerPosition();

        void SpawnObject(string title, SIMCONNECT_DATA_INITPOSITION init);

        void UpdateObject(uint objectId, SIMCONNECT_DATA_INITPOSITION position);

        void RemoveObject(uint objectId);
    }
}

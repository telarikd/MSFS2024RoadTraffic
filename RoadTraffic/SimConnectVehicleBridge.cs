using Microsoft.FlightSimulator.SimConnect;
using MSFSTraffic.Engine;
using MSFSTraffic.Models;
using System.Collections.Generic;

namespace RoadTraffic
{
    public class SimConnectVehicleBridge
    {
        private readonly TrafficManager _trafficManager;
        private readonly Dictionary<uint, int> _pendingSpawns = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _simObjectToVehicle = new Dictionary<uint, int>();
        private uint _nextRequestId = 100;
        private SimConnect _simConnect;

        private enum Requests : uint
        {
            PlayerPosition = 1
        }

        public SimConnectVehicleBridge(TrafficManager trafficManager)
        {
            _trafficManager = trafficManager;
        }

        public void SetSimConnect(SimConnect simConnect)
        {
            _simConnect = simConnect;
        }

        public void ClearTracking()
        {
            _pendingSpawns.Clear();
            _simObjectToVehicle.Clear();
        }

        public void HandleAssignedObjectId(uint requestId, uint simObjectId)
        {
            if (_pendingSpawns.ContainsKey(requestId))
            {
                int vehicleId = _pendingSpawns[requestId];
                _pendingSpawns.Remove(requestId);
                _trafficManager.RegisterSimObjectId(vehicleId, simObjectId);
                _simObjectToVehicle[simObjectId] = vehicleId;
            }
        }

        public void HandleEngineSpawnRequest(TrafficVehicle vehicle)
        {
            if (_simConnect == null) return;
            try
            {
                var current = vehicle.GetCurrentPosition();
                var pos = current.pos;
                double heading = current.headingDeg;

                var initPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude = pos.Latitude,
                    Longitude = pos.Longitude,
                    Altitude = 0,
                    Pitch = 0,
                    Bank = 0,
                    Heading = heading,
                    OnGround = 1,
                    Airspeed = 0
                };

                uint requestId = _nextRequestId++;
                _pendingSpawns[requestId] = vehicle.VehicleId;

                _simConnect.AICreateSimulatedObject_EX1(
                    vehicle.SimObjectTitle, "", initPos, (Requests)requestId);
            }
            catch { }
        }

        public void HandleEngineDespawnRequest(TrafficVehicle vehicle)
        {
            if (vehicle.SimObjectId == 0 || !vehicle.IsSpawned) return;
            try
            {
                uint reqId = _nextRequestId++;
                _simConnect.AIRemoveObject(vehicle.SimObjectId, (Requests)reqId);
                _simObjectToVehicle.Remove(vehicle.SimObjectId);
            }
            catch { }
        }

        public void HandleEnginePositionUpdate(TrafficVehicle vehicle)
        {
            if (_simConnect == null) return;
            if (vehicle.SimObjectId == 0 || !vehicle.IsSpawned) return;
            try
            {
                var current = vehicle.GetCurrentPosition();
                var pos = current.pos;
                double heading = current.headingDeg;

                var simPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude = pos.Latitude,
                    Longitude = pos.Longitude,
                    Altitude = 0,
                    Pitch = 0,
                    Bank = 0,
                    Heading = heading,
                    OnGround = 1,
                    Airspeed = 0
                };

                _simConnect.SetDataOnSimObject(
                    MainWindow.Definitions.InitPosition,
                    vehicle.SimObjectId,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    simPos);
            }
            catch { }
        }
    }
}

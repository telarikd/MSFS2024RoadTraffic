using Microsoft.FlightSimulator.SimConnect;
using MsfsSimConnect = Microsoft.FlightSimulator.SimConnect.SimConnect;
using RoadTraffic.Core.Models;
using RoadTraffic.Infrastructure.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RoadTraffic.SimConnect
{
    public class SimConnectService : ISimConnectService
    {
        private const int WM_USER_SIMCONNECT = 0x0402;
        private readonly ILogger _logger;
        private MsfsSimConnect _simConnect;
        private CancellationTokenSource _receiveLoopCts;
        private Task _receiveLoopTask;
        private uint _nextRequestId = 100;

        private enum Requests : uint
        {
            PlayerPosition = 1
        }

        private enum Definitions : uint
        {
            InitPosition = 1,
            PlayerPosition = 2
        }

        public SimConnectService(ILogger logger)
        {
            _logger = logger;
        }

        public event Action<PlayerPosition> PlayerPositionReceived;
        public event Action<uint> ObjectSpawned;
        public event Action<bool> ConnectionStateChanged;

        public void Connect(IntPtr handle)
        {
            if (_simConnect != null)
            {
                return;
            }

            try
            {
                _simConnect = new MsfsSimConnect("RoadTrafficEngine", handle, WM_USER_SIMCONNECT, null, 0);
                RegisterDataDefinitions();
                _simConnect.OnRecvOpen += OnRecvOpen;
                _simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
                _simConnect.OnRecvAssignedObjectId += OnRecvAssignedObjectId;
                _simConnect.OnRecvException += OnRecvException;
                _simConnect.OnRecvQuit += OnRecvQuit;
                _receiveLoopCts = new CancellationTokenSource();
                _receiveLoopTask = ReceiveMessagesAsync(_receiveLoopCts.Token);
                _logger.Info("SimConnect receive loop started");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to connect SimConnect", ex);
                Disconnect();
                ConnectionStateChanged?.Invoke(false);
            }
        }

        public void Disconnect()
        {
            _receiveLoopCts?.Cancel();
            _receiveLoopCts = null;

            if (_simConnect != null)
            {
                _simConnect.OnRecvOpen -= OnRecvOpen;
                _simConnect.OnRecvSimobjectData -= OnRecvSimobjectData;
                _simConnect.OnRecvAssignedObjectId -= OnRecvAssignedObjectId;
                _simConnect.OnRecvException -= OnRecvException;
                _simConnect.OnRecvQuit -= OnRecvQuit;
                _simConnect.Dispose();
                _simConnect = null;
                _logger.Info("SimConnect disconnected");
            }
        }

        public void RequestPlayerPosition()
        {
            _simConnect?.RequestDataOnSimObject(
                Requests.PlayerPosition,
                Definitions.PlayerPosition,
                MsfsSimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);
        }

        public void SpawnObject(string title, SIMCONNECT_DATA_INITPOSITION init)
        {
            if (_simConnect == null)
            {
                return;
            }

            uint requestId = _nextRequestId++;
            _simConnect.AICreateSimulatedObject_EX1(title, string.Empty, init, (Requests)requestId);
        }

        public void UpdateObject(uint objectId, SIMCONNECT_DATA_INITPOSITION position)
        {
            _simConnect?.SetDataOnSimObject(Definitions.InitPosition, objectId, SIMCONNECT_DATA_SET_FLAG.DEFAULT, position);
        }

        public void RemoveObject(uint objectId)
        {
            if (_simConnect == null || objectId == 0)
            {
                return;
            }

            uint requestId = _nextRequestId++;
            _simConnect.AIRemoveObject(objectId, (Requests)requestId);
        }

        private void RegisterDataDefinitions()
        {
            _simConnect.AddToDataDefinition(Definitions.InitPosition, "INITIAL POSITION", null, SIMCONNECT_DATATYPE.INITPOSITION, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<SIMCONNECT_DATA_INITPOSITION>(Definitions.InitPosition);

            _simConnect.AddToDataDefinition(Definitions.PlayerPosition, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.PlayerPosition, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.PlayerPosition, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.PlayerPosition, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.PlayerPosition, "GROUND VELOCITY", "meters per second", SIMCONNECT_DATATYPE.FLOAT64, 0, MsfsSimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<PlayerPositionData>(Definitions.PlayerPosition);
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _simConnect?.ReceiveMessage();
                    await Task.Delay(16, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error("SimConnect receive loop failed", ex);
                ConnectionStateChanged?.Invoke(false);
            }
        }

        private void OnRecvOpen(MsfsSimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            _logger.Info("SimConnect connected");
            ConnectionStateChanged?.Invoke(true);
        }

        private void OnRecvSimobjectData(MsfsSimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if ((Definitions)data.dwDefineID != Definitions.PlayerPosition)
            {
                return;
            }

            var position = (PlayerPositionData)data.dwData[0];
            PlayerPositionReceived?.Invoke(new PlayerPosition(
                position.Latitude,
                position.Longitude,
                position.Altitude,
                position.HeadingDeg,
                position.GroundSpeedMs));
        }

        private void OnRecvAssignedObjectId(MsfsSimConnect sender, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID data)
        {
            ObjectSpawned?.Invoke(data.dwObjectID);
        }

        private void OnRecvException(MsfsSimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            if ((SIMCONNECT_EXCEPTION)data.dwException == SIMCONNECT_EXCEPTION.CREATE_OBJECT_FAILED)
            {
                _logger.Warn("SimConnect object creation failed");
                return;
            }

            _logger.Warn($"SimConnect exception received: {(SIMCONNECT_EXCEPTION)data.dwException}");
        }

        private void OnRecvQuit(MsfsSimConnect sender, SIMCONNECT_RECV data)
        {
            _logger.Warn("SimConnect quit received");
            ConnectionStateChanged?.Invoke(false);
            Disconnect();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PlayerPositionData
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
            public double HeadingDeg;
            public double GroundSpeedMs;
        }
    }
}

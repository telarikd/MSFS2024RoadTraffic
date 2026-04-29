using Microsoft.FlightSimulator.SimConnect;
using MSFSTraffic.Models;
using System;
using System.Windows.Threading;

namespace RoadTraffic
{
    internal class PlayerPositionTracker
    {
        private readonly int _pollIntervalMs;
        private SimConnect _simConnect;
        private DispatcherTimer _playerPollTimer;
        private GeoCoordinate _playerPos;
        private bool _playerPosReceived;

        public event Action OnFirstPositionReceived;

        public GeoCoordinate PlayerPosition
        {
            get { return _playerPos; }
        }

        public bool IsPositionReceived
        {
            get { return _playerPosReceived; }
        }

        public PlayerPositionTracker(int pollIntervalMs)
        {
            _pollIntervalMs = pollIntervalMs;
        }

        public void Start(SimConnect simConnect)
        {
            _simConnect = simConnect;

            // Periodicky nacitej pozici hrace
            _playerPollTimer = new DispatcherTimer();
            _playerPollTimer.Interval = TimeSpan.FromMilliseconds(_pollIntervalMs);
            _playerPollTimer.Tick += (_, __) => RequestPlayerPosition();
            _playerPollTimer.Start();

            RequestPlayerPosition();
        }

        public void Stop()
        {
            _playerPollTimer?.Stop();
            _simConnect = null;
        }

        public void HandleSimObjectData(SIMCONNECT_RECV_SIMOBJECT_DATA e)
        {
            if ((SimConnectDefinitions)e.dwDefineID != SimConnectDefinitions.PlayerPosition) return;

            var pos = (PlayerPositionData)e.dwData[0];
            _playerPos = new GeoCoordinate(pos.Latitude, pos.Longitude, pos.Altitude);

            if (!_playerPosReceived)
            {
                _playerPosReceived = true;
                OnFirstPositionReceived?.Invoke();
            }
        }

        private void RequestPlayerPosition()
        {
            _simConnect.RequestDataOnSimObject(
                SimConnectRequests.PlayerPosition,
                SimConnectDefinitions.PlayerPosition,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
    }
}

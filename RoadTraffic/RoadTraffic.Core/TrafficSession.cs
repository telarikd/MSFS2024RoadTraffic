using Microsoft.FlightSimulator.SimConnect;
using RoadTraffic.Core.Models;
using RoadTraffic.SimConnect;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RoadTraffic.Core
{
    public class TrafficSession
    {
        private readonly ISimConnectService _simConnectService;
        private readonly TrafficManager _trafficManager;
        private readonly IntPtr _windowHandle;
        private readonly ConcurrentQueue<TrafficVehicle> _pendingSpawnQueue;

        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private bool _running;
        private bool _isConnected;
        private bool _playerPositionReceived;
        private GeoCoordinate _playerPosition;
        private DateTime _lastPlayerPositionRequestUtc = DateTime.MinValue;
        private TrafficVehicle _spawnInFlight;
        private DateTime _spawnRequestedAtUtc = DateTime.MinValue;

        public TrafficSession(ISimConnectService simConnectService, TrafficManager trafficManager, IntPtr windowHandle)
        {
            _simConnectService = simConnectService;
            _trafficManager = trafficManager;
            _windowHandle = windowHandle;
            _pendingSpawnQueue = new ConcurrentQueue<TrafficVehicle>();

            _trafficManager.VehicleSpawnRequested += OnVehicleSpawnRequested;
            _trafficManager.VehicleDespawnRequested += OnVehicleDespawnRequested;
            _trafficManager.VehiclePositionUpdated += OnVehiclePositionUpdated;
        }

        public event Action<TrafficSessionSnapshot> SessionUpdated;

        public int UpdateIntervalMs { get; set; } = 16;

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _simConnectService.PlayerPositionReceived += OnPlayerPositionReceived;
            _simConnectService.ObjectSpawned += OnObjectSpawned;
            _simConnectService.ConnectionStateChanged += OnConnectionStateChanged;
            _simConnectService.Connect(_windowHandle);

            _loopCts = new CancellationTokenSource();
            _loopTask = RunLoop(_loopCts.Token);
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _loopCts?.Cancel();
            _trafficManager.RemoveAllVehicles();
            _simConnectService.PlayerPositionReceived -= OnPlayerPositionReceived;
            _simConnectService.ObjectSpawned -= OnObjectSpawned;
            _simConnectService.ConnectionStateChanged -= OnConnectionStateChanged;
            _simConnectService.Disconnect();
            _playerPositionReceived = false;
            _isConnected = false;
            _spawnInFlight = null;
            while (_pendingSpawnQueue.TryDequeue(out _)) { }
            PublishSnapshot();
        }

        private async Task RunLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_isConnected)
                    {
                        if (DateTime.UtcNow - _lastPlayerPositionRequestUtc >= TimeSpan.FromSeconds(1))
                        {
                            _simConnectService.RequestPlayerPosition();
                            _lastPlayerPositionRequestUtc = DateTime.UtcNow;
                        }

                        if (_playerPositionReceived)
                        {
                            await _trafficManager.RefreshRoadsAsync(_playerPosition);
                            _trafficManager.Update(_playerPosition, UpdateIntervalMs / 1000.0);
                            DispatchQueuedSpawn();
                        }
                    }

                    PublishSnapshot();
                    await Task.Delay(UpdateIntervalMs, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void DispatchQueuedSpawn()
        {
            if (!_isConnected)
            {
                return;
            }

            if (_spawnInFlight != null)
            {
                if (DateTime.UtcNow - _spawnRequestedAtUtc < TimeSpan.FromSeconds(3))
                {
                    return;
                }

                _spawnInFlight = null;
            }

            if (!_pendingSpawnQueue.TryDequeue(out TrafficVehicle vehicle))
            {
                return;
            }

            var current = vehicle.GetCurrentPosition();
            var initPosition = new SIMCONNECT_DATA_INITPOSITION
            {
                Latitude = current.pos.Latitude,
                Longitude = current.pos.Longitude,
                Altitude = 0,
                Pitch = 0,
                Bank = 0,
                Heading = current.headingDeg,
                OnGround = 1,
                Airspeed = 0
            };

            _spawnInFlight = vehicle;
            _spawnRequestedAtUtc = DateTime.UtcNow;
            _simConnectService.SpawnObject(vehicle.SimObjectTitle, initPosition);
        }

        private void OnPlayerPositionReceived(PlayerPosition position)
        {
            _playerPosition = position.ToGeoCoordinate();
            _playerPositionReceived = true;
        }

        private void OnObjectSpawned(uint objectId)
        {
            if (_spawnInFlight == null)
            {
                return;
            }

            _trafficManager.RegisterVehicleSpawn(_spawnInFlight, objectId);
            _spawnInFlight = null;
        }

        private void OnConnectionStateChanged(bool isConnected)
        {
            _isConnected = isConnected;
            if (!isConnected)
            {
                _playerPositionReceived = false;
                _spawnInFlight = null;
                while (_pendingSpawnQueue.TryDequeue(out _)) { }
            }

            PublishSnapshot();
        }

        private void OnVehicleSpawnRequested(TrafficVehicle vehicle)
        {
            _pendingSpawnQueue.Enqueue(vehicle);
        }

        private void OnVehicleDespawnRequested(TrafficVehicle vehicle)
        {
            if (vehicle.IsSpawned && vehicle.SimObjectId != 0)
            {
                _simConnectService.RemoveObject(vehicle.SimObjectId);
            }
        }

        private void OnVehiclePositionUpdated(TrafficVehicle vehicle)
        {
            if (!vehicle.IsSpawned || vehicle.SimObjectId == 0)
            {
                return;
            }

            var current = vehicle.GetCurrentPosition();
            var position = new SIMCONNECT_DATA_INITPOSITION
            {
                Latitude = current.pos.Latitude,
                Longitude = current.pos.Longitude,
                Altitude = 0,
                Pitch = 0,
                Bank = 0,
                Heading = current.headingDeg,
                OnGround = 1,
                Airspeed = 0
            };

            _simConnectService.UpdateObject(vehicle.SimObjectId, position);
        }

        private void PublishSnapshot()
        {
            SessionUpdated?.Invoke(new TrafficSessionSnapshot
            {
                IsConnected = _isConnected,
                ActiveVehicles = _trafficManager.ActiveVehicleCount,
                MaxVehicles = _trafficManager.MaxVehicles,
                ActiveRoads = _trafficManager.ActiveRoadCount,
                TotalRoadKm = _trafficManager.TotalRoadKm
            });
        }
    }
}

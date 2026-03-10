using Microsoft.FlightSimulator.SimConnect;
using RoadTraffic.Core.Models;
using RoadTraffic.Infrastructure.Logging;
using RoadTraffic.SimConnect;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoadTraffic.Core
{
    public class TrafficSession
    {
        private readonly ISimConnectService _simConnectService;
        private readonly TrafficManager _trafficManager;
        private readonly IntPtr _windowHandle;
        private readonly ILogger _logger;
        private readonly TimeSpan _minSpawnInterval = TimeSpan.FromMilliseconds(200);

        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private bool _running;
        private bool _isConnected;
        private bool _playerPositionReceived;
        private GeoCoordinate _playerPosition;
        private double _playerHeadingDeg;
        private double _playerGroundSpeedMs;
        private DateTime _lastPlayerPositionRequestUtc = DateTime.MinValue;
        private TrafficVehicle _spawningVehicle;
        private DateTime _lastSpawnUtc = DateTime.MinValue;

        public TrafficSession(ISimConnectService simConnectService, TrafficManager trafficManager, IntPtr windowHandle, ILogger logger)
        {
            _simConnectService = simConnectService;
            _trafficManager = trafficManager;
            _windowHandle = windowHandle;
            _logger = logger;

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
            _logger.Info("Traffic session started");
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
            _spawningVehicle = null;
            PublishSnapshot();
            _logger.Info("Traffic session stopped");
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
                            _trafficManager.Update(_playerPosition, UpdateIntervalMs / 1000.0, _playerHeadingDeg, _playerGroundSpeedMs);
                            DispatchNextSpawn();
                        }
                    }

                    PublishSnapshot();
                    await Task.Delay(UpdateIntervalMs, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error("Traffic session loop failed", ex);
            }
        }

        private void DispatchNextSpawn()
        {
            if (!_isConnected)
            {
                return;
            }

            if (_spawningVehicle != null)
            {
                return;
            }

            if (_trafficManager.ActiveVehicleCount >= _trafficManager.MaxVehicles)
            {
                return;
            }

            if (DateTime.UtcNow - _lastSpawnUtc < _minSpawnInterval)
            {
                return;
            }

            TrafficVehicle vehicle = _trafficManager.GetNextPendingSpawn();
            if (vehicle == null)
            {
                return;
            }

            vehicle.MarkSpawning();
            _spawningVehicle = vehicle;

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

            _simConnectService.SpawnObject(vehicle.SimObjectTitle, initPosition);
            _lastSpawnUtc = DateTime.UtcNow;
            _logger.Info($"Spawn cycle executed for vehicle {vehicle.VehicleId}");
        }

        private void OnPlayerPositionReceived(PlayerPosition position)
        {
            _playerPosition = position.ToGeoCoordinate();
            _playerHeadingDeg = position.HeadingDeg;
            _playerGroundSpeedMs = position.GroundSpeedMs;
            _playerPositionReceived = true;
        }

        private void OnObjectSpawned(uint objectId)
        {
            if (_spawningVehicle == null)
            {
                return;
            }

            _trafficManager.RegisterVehicleSpawn(_spawningVehicle, objectId);
            _spawningVehicle = null;
        }

        private void OnConnectionStateChanged(bool isConnected)
        {
            _isConnected = isConnected;
            if (!isConnected)
            {
                _playerPositionReceived = false;
                _spawningVehicle = null;
                _trafficManager.RemoveAllVehicles();
            }

            PublishSnapshot();
        }

        private void OnVehicleDespawnRequested(TrafficVehicle vehicle)
        {
            if (ReferenceEquals(vehicle, _spawningVehicle))
            {
                _spawningVehicle = null;
            }

            if (vehicle.IsSpawned && vehicle.SimObjectId != 0)
            {
                _simConnectService.RemoveObject(vehicle.SimObjectId);
            }
        }

        private void OnVehiclePositionUpdated(TrafficVehicle vehicle)
        {
            if (vehicle.HasVisualTierChanged)
            {
                if (vehicle.VisualTier != TrafficVisualTier.Full3D && vehicle.IsSpawned && vehicle.SimObjectId != 0)
                {
                    _simConnectService.RemoveObject(vehicle.SimObjectId);
                    vehicle.MarkPending();
                    return;
                }

                if (vehicle.VisualTier == TrafficVisualTier.Full3D && !vehicle.IsSpawned && vehicle.LifecycleState == VehicleLifecycleState.PendingSpawn)
                {
                    _logger.Info($"Vehicle {vehicle.VehicleId} entered Full3D tier and is ready for spawn scheduling");
                }
            }

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

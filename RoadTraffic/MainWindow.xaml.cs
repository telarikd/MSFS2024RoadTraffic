using Microsoft.FlightSimulator.SimConnect;
using MSFSTraffic.Engine;
using MSFSTraffic.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RoadTraffic
{
    public partial class MainWindow : Window
    {
        // ── Konfigurace ──
        private const int    WM_USER_SIMCONNECT = 0x0402;
        private const string VEHICLE_TITLE      = "HAmphibiusFemale";
        private const string FLARE_EFFECT_TITLE = "RoadTrafficLight";     // SimObject\RoadTrafficLight package
        private int _updateIntervalMs = 16;            // ~60 Hz default; meni se pres ComboBox
        private const int PLAYER_POLL_INTERVAL_MS = 1000;

        // ── SimConnect ──
        private SimConnect _simConnect;
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _playerPollTimer;

        // ── Traffic Engine ──
        private TrafficManager _trafficManager;
        private TrafficEngine  _trafficEngine;

        // ── TrafficEngine spawn tracking ──
        private readonly Dictionary<uint, TrafficCar> _pendingCarSpawns = new Dictionary<uint, TrafficCar>();

        // ── Hráčova pozice ──
        private GeoCoordinate _playerPos;
        private bool _playerPosReceived;
        private DateTime _lastUpdateTime;

        // ── Spawn tracking ──
        private readonly Dictionary<uint, int> _pendingSpawns = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _simObjectToVehicle = new Dictionary<uint, int>();
        private uint _nextRequestId = 100;

        // ── UI sync guard ──
        private bool _sliderSyncing;

        // ── UI refresh throttle (update physics 60 Hz, UI ~1× za sekundu) ──
        private int _uiRefreshCounter;

        // ── Debounce pro full-respawn po zmene MaxVehicles ──
        private DispatcherTimer _respawnDebounceTimer;

        // ── MaxVehicles slider sync guard ──
        private bool _maxVehiclesSyncing;

        // ── Enums pro SimConnect ──
        private enum Requests : uint { PlayerPosition = 1 }
        private enum Definitions : uint { InitPosition = 1, PlayerPosition = 2 }

        [StructLayout(LayoutKind.Sequential)]
        private struct PlayerPositionData
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
        }

        // ════════════════════════════════════════
        //  KONSTRUKTOR
        // ════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();

            _trafficManager = new TrafficManager();
            _trafficManager.VehicleTitle = VEHICLE_TITLE;
            _trafficManager.MaxVehicles = 30;
            _trafficManager.UserDensityMultiplier = 0.5;

            _trafficManager.OnVehicleSpawnRequested  += OnEngineSpawnRequest;
            _trafficManager.OnVehicleDespawnRequested += OnEngineDespawnRequest;
            _trafficManager.OnVehiclePositionUpdated  += OnEnginePositionUpdate;

            DensitySlider.ValueChanged      += OnDensitySliderChanged;
            DensityTextBox.LostFocus        += OnDensityTextBoxLostFocus;
            DensityTextBox.KeyDown          += OnDensityTextBoxKeyDown;

            // MaxVehicles slider + TextBox
            MaxVehiclesSlider.ValueChanged   += OnMaxVehiclesSliderChanged;
            MaxVehiclesTextBox.LostFocus     += OnMaxVehiclesTextBoxLostFocus;
            MaxVehiclesTextBox.KeyDown       += OnMaxVehiclesTextBoxKeyDown;

            // Tickrate ComboBox
            TickrateComboBox.SelectionChanged += OnTickrateChanged;

            // Road type checkboxy — Checked i Unchecked → stejny handler
            ChkMotorway.Checked      += OnRoadTypeCheckboxChanged;
            ChkMotorway.Unchecked    += OnRoadTypeCheckboxChanged;
            ChkTrunk.Checked         += OnRoadTypeCheckboxChanged;
            ChkTrunk.Unchecked       += OnRoadTypeCheckboxChanged;
            ChkPrimary.Checked       += OnRoadTypeCheckboxChanged;
            ChkPrimary.Unchecked     += OnRoadTypeCheckboxChanged;
            ChkSecondary.Checked     += OnRoadTypeCheckboxChanged;
            ChkSecondary.Unchecked   += OnRoadTypeCheckboxChanged;
            ChkTertiary.Checked      += OnRoadTypeCheckboxChanged;
            ChkTertiary.Unchecked    += OnRoadTypeCheckboxChanged;
            ChkResidential.Checked   += OnRoadTypeCheckboxChanged;
            ChkResidential.Unchecked += OnRoadTypeCheckboxChanged;
            ChkUnclassified.Checked  += OnRoadTypeCheckboxChanged;
            ChkUnclassified.Unchecked += OnRoadTypeCheckboxChanged;

            // Debounce timer pro full-respawn
            _respawnDebounceTimer = new DispatcherTimer();
            _respawnDebounceTimer.Interval = TimeSpan.FromMilliseconds(800);
            _respawnDebounceTimer.Tick += OnRespawnDebounce;

            // TrafficEngine — pohyblivá světelná vrstva (FlareEffect SimObjects)
            _trafficEngine = new TrafficEngine();
            _trafficEngine.MaxCars = 150;
            _trafficEngine.OnSpawnRequested    += OnCarSpawnRequested;
            _trafficEngine.OnDespawnRequested  += OnCarDespawnRequested;
            _trafficEngine.OnPositionUpdated   += OnCarPositionUpdated;

            TrafficCarsSlider.ValueChanged += OnTrafficCarsSliderChanged;
        }

        // ════════════════════════════════════════
        //  WPF LIFETIME
        // ════════════════════════════════════════

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            HwndSource.FromHwnd(helper.Handle).AddHook(WndProc);
            TryConnect(helper.Handle);
        }

        protected override void OnClosed(EventArgs e)
        {
            Cleanup();
            base.OnClosed(e);
        }

        // SimConnect message pump (nahrazuje WinForms WndProc)
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER_SIMCONNECT)
                _simConnect?.ReceiveMessage();
            return IntPtr.Zero;
        }

        // ════════════════════════════════════════
        //  SIMCONNECT SETUP
        // ════════════════════════════════════════

        private void TryConnect(IntPtr handle)
        {
            try
            {
                _simConnect = new SimConnect("RoadTrafficEngine", handle, WM_USER_SIMCONNECT, null, 0);

                // INIT POSITION (pro spawn + update pozice vozidel)
                _simConnect.AddToDataDefinition(
                    Definitions.InitPosition, "INITIAL POSITION", null,
                    SIMCONNECT_DATATYPE.INITPOSITION, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<SIMCONNECT_DATA_INITPOSITION>(Definitions.InitPosition);

                // PLAYER POSITION
                _simConnect.AddToDataDefinition(
                    Definitions.PlayerPosition, "PLANE LATITUDE", "degrees",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(
                    Definitions.PlayerPosition, "PLANE LONGITUDE", "degrees",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(
                    Definitions.PlayerPosition, "PLANE ALTITUDE", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<PlayerPositionData>(Definitions.PlayerPosition);

                _simConnect.OnRecvOpen              += OnSimConnectOpen;
                _simConnect.OnRecvSimobjectData     += OnRecvSimobjectData;
                _simConnect.OnRecvAssignedObjectId  += OnRecvAssignedObjectId;
                _simConnect.OnRecvException         += OnRecvException;
                _simConnect.OnRecvQuit              += OnRecvQuit;
            }
            catch (Exception)
            {
                // MSFS nespusten — zustaneme v Disconnected stavu
            }
        }

        // ════════════════════════════════════════
        //  SIMCONNECT EVENT HANDLERS
        // ════════════════════════════════════════

        private void OnSimConnectOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Dispatcher.Invoke(() =>
            {
                StatusDot.Fill  = new SolidColorBrush(Color.FromRgb(0x44, 0xFF, 0x44));
                StatusText.Text = "Connected";
            });

            // Periodicky nacitej pozici hrace
            _playerPollTimer = new DispatcherTimer();
            _playerPollTimer.Interval = TimeSpan.FromMilliseconds(PLAYER_POLL_INTERVAL_MS);
            _playerPollTimer.Tick += (_, __) => RequestPlayerPosition();
            _playerPollTimer.Start();

            RequestPlayerPosition();
        }

        private void RequestPlayerPosition()
        {
            _simConnect.RequestDataOnSimObject(
                Requests.PlayerPosition,
                Definitions.PlayerPosition,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }

        private void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA e)
        {
            if ((Definitions)e.dwDefineID != Definitions.PlayerPosition) return;

            var pos = (PlayerPositionData)e.dwData[0];
            _playerPos = new GeoCoordinate(pos.Latitude, pos.Longitude, pos.Altitude);

            if (!_playerPosReceived)
            {
                _playerPosReceived = true;
                _lastUpdateTime = DateTime.UtcNow;
                StartUpdateLoop();
            }
        }

        private void OnRecvAssignedObjectId(SimConnect sender, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID e)
        {
            uint requestId   = e.dwRequestID;
            uint simObjectId = e.dwObjectID;

            if (_pendingSpawns.ContainsKey(requestId))
            {
                int vehicleId = _pendingSpawns[requestId];
                _pendingSpawns.Remove(requestId);
                _trafficManager.RegisterSimObjectId(vehicleId, simObjectId);
                _simObjectToVehicle[simObjectId] = vehicleId;
            }
            else if (_pendingCarSpawns.ContainsKey(requestId))
            {
                TrafficCar car = _pendingCarSpawns[requestId];
                _pendingCarSpawns.Remove(requestId);
                _trafficEngine.ConfirmSpawn(requestId, simObjectId);
            }
        }

        private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION e)
        {
            // Ticho pro CREATE_OBJECT_FAILED — bezne selhani pri spawnu
            if ((SIMCONNECT_EXCEPTION)e.dwException == SIMCONNECT_EXCEPTION.CREATE_OBJECT_FAILED)
                return;
        }

        private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusDot.Fill  = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
                StatusText.Text = "Disconnected";
                VehiclesText.Text = string.Format("Vehicles: 0/{0}", _trafficManager.MaxVehicles);
            });
            Cleanup();
        }

        // ════════════════════════════════════════
        //  TRAFFIC ENGINE ↔ SIMCONNECT BRIDGE
        // ════════════════════════════════════════

        private void OnEngineSpawnRequest(TrafficVehicle vehicle)
        {
            if (_simConnect == null) return;
            try
            {
                var current = vehicle.GetCurrentPosition();
                var pos     = current.pos;
                double heading = current.headingDeg;

                var initPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude  = pos.Latitude,
                    Longitude = pos.Longitude,
                    Altitude  = 0,
                    Pitch     = 0,
                    Bank      = 0,
                    Heading   = heading,
                    OnGround  = 1,
                    Airspeed  = 0
                };

                uint requestId = _nextRequestId++;
                _pendingSpawns[requestId] = vehicle.VehicleId;

                _simConnect.AICreateSimulatedObject_EX1(
                    vehicle.SimObjectTitle, "", initPos, (Requests)requestId);
            }
            catch { }
        }

        private void OnEngineDespawnRequest(TrafficVehicle vehicle)
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

        private void OnEnginePositionUpdate(TrafficVehicle vehicle)
        {
            if (_simConnect == null) return;
            if (vehicle.SimObjectId == 0 || !vehicle.IsSpawned) return;
            try
            {
                var current = vehicle.GetCurrentPosition();
                var pos     = current.pos;
                double heading = current.headingDeg;

                var simPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude  = pos.Latitude,
                    Longitude = pos.Longitude,
                    Altitude  = 0,
                    Pitch     = 0,
                    Bank      = 0,
                    Heading   = heading,
                    OnGround  = 1,
                    Airspeed  = 0
                };

                _simConnect.SetDataOnSimObject(
                    Definitions.InitPosition,
                    vehicle.SimObjectId,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    simPos);
            }
            catch { }
        }

        // ════════════════════════════════════════
        //  UPDATE LOOP
        // ════════════════════════════════════════

        private void StartUpdateLoop()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(_updateIntervalMs);
            _updateTimer.Tick += OnUpdateTick;
            _updateTimer.Start();
        }

        private void OnUpdateTick(object sender, EventArgs e)
        {
            if (!_playerPosReceived) return;

            var now = DateTime.UtcNow;
            double deltaTime = (now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;
            deltaTime = Math.Min(deltaTime, 0.5);

            _trafficManager.Update(_playerPos, deltaTime);
            _trafficEngine.Update(_playerPos.Latitude, _playerPos.Longitude, deltaTime);

            // UI refresh jen ~1× za sekundu (ne kazdy physics tick)
            if (++_uiRefreshCounter >= 60)
            {
                _uiRefreshCounter = 0;

                int vehicles = _trafficManager.ActiveVehicleCount;
                int roads    = _trafficManager.ActiveRoadCount;
                double km    = _trafficManager.TotalRoadKm;

                VehiclesText.Text = string.Format("Vehicles: {0}/{1}   Cars: {2}",
                    vehicles, _trafficManager.MaxVehicles, _trafficEngine.ActiveCarCount);

                FlareCountText.Text = string.Format("Rendered flares: {0}", _trafficEngine.ActiveCars);

                if (roads > 0)
                {
                    RoadsText.Text = string.Format("Roads: {0} segments", roads);
                    KmText.Text    = string.Format("{0:F1} km total", km);
                }
            }
        }

        // ════════════════════════════════════════
        //  DENSITY SLIDER
        // ════════════════════════════════════════

        private void OnDensitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_sliderSyncing || DensityTextBox == null) return;

            int val = (int)Math.Round(DensitySlider.Value);
            _sliderSyncing = true;
            DensityTextBox.Text = val.ToString();
            _sliderSyncing = false;

            _trafficManager.UserDensityMultiplier = val / 100.0;
        }

        private void OnDensityTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                ApplyDensityTextBox();
        }

        private void OnDensityTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            ApplyDensityTextBox();
        }

        private void ApplyDensityTextBox()
        {
            int val;
            if (!int.TryParse(DensityTextBox.Text, out val))
                val = 50;
            val = Math.Max(0, Math.Min(100, val));

            _sliderSyncing = true;
            DensitySlider.Value = val;
            DensityTextBox.Text = val.ToString();
            _sliderSyncing = false;

            _trafficManager.UserDensityMultiplier = val / 100.0;
        }

        // ════════════════════════════════════════
        //  MAX VEHICLES SLIDER
        // ════════════════════════════════════════

        private void OnMaxVehiclesSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_maxVehiclesSyncing || MaxVehiclesTextBox == null) return;

            int val = (int)Math.Round(MaxVehiclesSlider.Value);
            _maxVehiclesSyncing = true;
            MaxVehiclesTextBox.Text = val.ToString();
            _maxVehiclesSyncing = false;

            if (_trafficManager != null)
                _trafficManager.MaxVehicles = val;

            // Restart debounce — 800ms po posledni zmene provede full respawn
            _respawnDebounceTimer.Stop();
            _respawnDebounceTimer.Start();
        }

        private void OnMaxVehiclesTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyMaxVehiclesTextBox();
        }

        private void OnMaxVehiclesTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            ApplyMaxVehiclesTextBox();
        }

        private void ApplyMaxVehiclesTextBox()
        {
            int val;
            if (!int.TryParse(MaxVehiclesTextBox.Text, out val)) val = 30;
            val = Math.Max(0, Math.Min(400, val));

            _maxVehiclesSyncing = true;
            MaxVehiclesSlider.Value = val;
            MaxVehiclesTextBox.Text = val.ToString();
            _maxVehiclesSyncing = false;

            if (_trafficManager != null)
                _trafficManager.MaxVehicles = val;

            _respawnDebounceTimer.Stop();
            _respawnDebounceTimer.Start();
        }

        private void OnRespawnDebounce(object sender, EventArgs e)
        {
            _respawnDebounceTimer.Stop();
            if (_simConnect == null || !_playerPosReceived) return;

            // Full respawn: odeber vsechna vozidla, engine auto-respawnuje dle noveho maxima
            _pendingSpawns.Clear();
            _simObjectToVehicle.Clear();
            _trafficManager?.RemoveAllVehicles();
        }

        // ════════════════════════════════════════
        //  ROAD TYPE CHECKBOXES
        // ════════════════════════════════════════

        private void OnRoadTypeCheckboxChanged(object sender, RoutedEventArgs e)
        {
            var chk = sender as CheckBox;
            if (chk == null || _trafficManager == null) return;

            bool enabled = chk.IsChecked == true;

            RoadType type;
            if      (chk == ChkMotorway)     type = RoadType.Motorway;
            else if (chk == ChkTrunk)        type = RoadType.Trunk;
            else if (chk == ChkPrimary)      type = RoadType.Primary;
            else if (chk == ChkSecondary)    type = RoadType.Secondary;
            else if (chk == ChkTertiary)     type = RoadType.Tertiary;
            else if (chk == ChkResidential)  type = RoadType.Residential;
            else if (chk == ChkUnclassified) type = RoadType.Unclassified;
            else return;

            _trafficManager.SetRoadTypeEnabled(type, enabled);
        }

        // ════════════════════════════════════════
        //  TICKRATE DROPDOWN
        // ════════════════════════════════════════

        private void OnTickrateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updateTimer == null) return;
            var item = TickrateComboBox.SelectedItem as ComboBoxItem;
            if (item == null) return;

            int ms;
            if (int.TryParse(item.Tag as string, out ms) && ms > 0)
            {
                _updateIntervalMs = ms;
                _updateTimer.Interval = TimeSpan.FromMilliseconds(ms);
            }
        }

        // ════════════════════════════════════════
        //  TRAFFIC CARS (FlareEffect)
        // ════════════════════════════════════════

        private void OnCarSpawnRequested(TrafficCar car)
        {
            if (_simConnect == null) return;
            try
            {
                var initPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude  = car.Lat,
                    Longitude = car.Lon,
                    Altitude  = 0,
                    Pitch     = 0,
                    Bank      = 0,
                    Heading   = car.Heading,
                    OnGround  = 1,
                    Airspeed  = 0
                };

                uint requestId = _nextRequestId++;
                car.RequestId  = requestId;
                _pendingCarSpawns[requestId] = car;

                _simConnect.AICreateSimulatedObject_EX1(FLARE_EFFECT_TITLE, "", initPos, (Requests)requestId);
            }
            catch { }
        }

        private void OnCarDespawnRequested(TrafficCar car)
        {
            if (_simConnect == null || car.ObjectId == 0 || !car.IsSpawned) return;
            try
            {
                uint reqId = _nextRequestId++;
                _simConnect.AIRemoveObject(car.ObjectId, (Requests)reqId);
            }
            catch { }
        }

        private void OnCarPositionUpdated(TrafficCar car)
        {
            if (_simConnect == null || car.ObjectId == 0 || !car.IsSpawned) return;
            try
            {
                var simPos = new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude  = car.Lat,
                    Longitude = car.Lon,
                    Altitude  = 0,
                    Pitch     = 0,
                    Bank      = 0,
                    Heading   = car.Heading,
                    OnGround  = 1,
                    Airspeed  = 0
                };

                _simConnect.SetDataOnSimObject(
                    Definitions.InitPosition,
                    car.ObjectId,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    simPos);
            }
            catch { }
        }

        private void OnTrafficCarsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)TrafficCarsSlider.Value;
            if (TrafficCarsValueText != null)
                TrafficCarsValueText.Text = val.ToString();
            if (_trafficEngine != null)
                _trafficEngine.MaxCars = val;
        }

        // ════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════

        private void Cleanup()
        {
            _updateTimer?.Stop();
            _playerPollTimer?.Stop();
            // Nulluj _simConnect PRED despawnem — handlery pak bezpecne returnuji
            _simConnect = null;
            _trafficManager?.RemoveAllVehicles();
            _trafficEngine?.RemoveAll();
        }
    }
}

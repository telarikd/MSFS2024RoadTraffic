using Microsoft.FlightSimulator.SimConnect;
using MSFSTraffic.Engine;
using MSFSTraffic.Models;
using MSFSTraffic.Roads;
using System;
using System.Collections.Generic;
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
        private const int WM_USER_SIMCONNECT = 0x0402;
        private const string VEHICLE_TITLE = "HAmphibiusFemale";
        private int _updateIntervalMs = 16;            // ~60 Hz default; meni se pres ComboBox
        private const int PLAYER_POLL_INTERVAL_MS = 1000;

        // ── SimConnect ──
        private SimConnect _simConnect;
        private DispatcherTimer _updateTimer;

        // ── Traffic Engine ──
        private TrafficManager _trafficManager;

        // ── Hráčova pozice ──
        private DateTime _lastUpdateTime;
        private readonly PlayerPositionTracker _playerPositionTracker;

        // ── Spawn tracking ──
        private readonly SimConnectVehicleBridge _vehicleBridge;

        // ── UI sync guard ──
        private bool _sliderSyncing;

        // ── UI refresh throttle (update physics 60 Hz, UI ~1× za sekundu) ──
        private int _uiRefreshCounter;

        // ── Debounce pro full-respawn po zmene MaxVehicles ──
        private DispatcherTimer _respawnDebounceTimer;

        // ── MaxVehicles slider sync guard ──
        private bool _maxVehiclesSyncing;

        // ════════════════════════════════════════
        //  KONSTRUKTOR
        // ════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();

            var roadProvider = new OverpassRoadProvider();
            var densityCalculator = new TrafficDensityCalculator();
            _trafficManager = new TrafficManager(roadProvider, densityCalculator);
            _vehicleBridge = new SimConnectVehicleBridge(_trafficManager);
            _playerPositionTracker = new PlayerPositionTracker(PLAYER_POLL_INTERVAL_MS);
            _trafficManager.VehicleTitle = VEHICLE_TITLE;
            _trafficManager.MaxVehicles = 30;
            _trafficManager.UserDensityMultiplier = 0.5;

            _trafficManager.OnVehicleSpawnRequested  += _vehicleBridge.HandleEngineSpawnRequest;
            _trafficManager.OnVehicleDespawnRequested += _vehicleBridge.HandleEngineDespawnRequest;
            _trafficManager.OnVehiclePositionUpdated  += _vehicleBridge.HandleEnginePositionUpdate;

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
                    SimConnectDefinitions.InitPosition, "INITIAL POSITION", null,
                    SIMCONNECT_DATATYPE.INITPOSITION, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<SIMCONNECT_DATA_INITPOSITION>(SimConnectDefinitions.InitPosition);

                // PLAYER POSITION
                _simConnect.AddToDataDefinition(
                    SimConnectDefinitions.PlayerPosition, "PLANE LATITUDE", "degrees",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(
                    SimConnectDefinitions.PlayerPosition, "PLANE LONGITUDE", "degrees",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(
                    SimConnectDefinitions.PlayerPosition, "PLANE ALTITUDE", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<PlayerPositionData>(SimConnectDefinitions.PlayerPosition);

                _simConnect.OnRecvOpen              += OnSimConnectOpen;
                _simConnect.OnRecvSimobjectData     += OnRecvSimobjectData;
                _simConnect.OnRecvAssignedObjectId  += OnRecvAssignedObjectId;
                _simConnect.OnRecvException         += OnRecvException;
                _simConnect.OnRecvQuit              += OnRecvQuit;
                _vehicleBridge.SetSimConnect(_simConnect);
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

            _playerPositionTracker.Start(_simConnect);
        }

        private void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA e)
        {
            if (_playerPositionTracker.HandleSimObjectData(e))
                EnsureUpdateLoopStarted();
        }

        private void OnRecvAssignedObjectId(SimConnect sender, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID e)
        {
            RuntimeDiagnostics.Log($"[RoadTraffic.Diag] AssignedObjectId request={e.dwRequestID} object={e.dwObjectID}");
            _vehicleBridge.HandleAssignedObjectId(e.dwRequestID, e.dwObjectID);
        }

        private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION e)
        {
            var exception = (SIMCONNECT_EXCEPTION)e.dwException;
            RuntimeDiagnostics.Log($"[RoadTraffic.Diag] SimConnect exception={exception} send={e.dwSendID} index={e.dwIndex}");

            if (exception == SIMCONNECT_EXCEPTION.CREATE_OBJECT_FAILED)
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

        // ════════════════════════════════════════
        //  UPDATE LOOP
        // ════════════════════════════════════════

        private void StartUpdateLoop()
        {
            if (_updateTimer == null)
            {
                _updateTimer = new DispatcherTimer();
                _updateTimer.Tick += OnUpdateTick;
            }

            if (_updateTimer.IsEnabled) return;

            _lastUpdateTime = DateTime.UtcNow;
            _updateTimer.Interval = TimeSpan.FromMilliseconds(_updateIntervalMs);
            _updateTimer.Start();
        }

        private void EnsureUpdateLoopStarted()
        {
            if (_playerPositionTracker.IsPositionReceived)
                StartUpdateLoop();
        }

        private void OnUpdateTick(object sender, EventArgs e)
        {
            if (!_playerPositionTracker.IsPositionReceived) return;

            var now = DateTime.UtcNow;
            double deltaTime = (now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;
            deltaTime = Math.Min(deltaTime, 0.5);

            _trafficManager.Update(_playerPositionTracker.PlayerPosition, deltaTime);

            // UI refresh jen ~1× za sekundu (ne kazdy physics tick)
            if (++_uiRefreshCounter >= 60)
            {
                _uiRefreshCounter = 0;

                int vehicles = _trafficManager.ActiveVehicleCount;
                int roads    = _trafficManager.ActiveRoadCount;
                double km    = _trafficManager.TotalRoadKm;

                VehiclesText.Text = string.Format("Vehicles: {0}/{1}", vehicles, _trafficManager.MaxVehicles);
                RuntimeDiagnostics.Log($"[RoadTraffic.Diag] UI refresh roads={roads} vehicles={vehicles}");

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
            if (_simConnect == null || !_playerPositionTracker.IsPositionReceived) return;

            // Full respawn: odeber vsechna vozidla, engine auto-respawnuje dle noveho maxima
            _vehicleBridge.ClearTracking();
            _trafficManager?.RemoveAllVehicles();
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
        //  ROAD TYPE CHECKBOXY
        // ════════════════════════════════════════

        private void OnRoadTypeCheckboxChanged(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null || checkBox.Tag == null) return;

            RoadType roadType;
            if (!Enum.TryParse(checkBox.Tag.ToString(), out roadType)) return;

            _trafficManager.SetRoadTypeEnabled(roadType, checkBox.IsChecked == true);
        }

        // ════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════

        private void Cleanup()
        {
            _updateTimer?.Stop();
            _playerPositionTracker.Stop();

            // Despawnuj dokud bridge stale drzi platny SimConnect.
            _trafficManager?.RemoveAllVehicles();

            _vehicleBridge.ClearTracking();
            _vehicleBridge.SetSimConnect(null);
            _simConnect = null;
        }
    }
}

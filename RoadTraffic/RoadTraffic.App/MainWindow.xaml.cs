using RoadTraffic.Core;
using RoadTraffic.Core.Models;
using RoadTraffic.Infrastructure;
using RoadTraffic.Infrastructure.Logging;
using RoadTraffic.SimConnect;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace RoadTraffic.App
{
    public partial class MainWindow : Window
    {
        private const string VehicleTitle = "HAmphibiusFemale";

        private readonly ILogger _logger;
        private readonly TrafficManager _trafficManager;
        private readonly ISimConnectService _simConnectService;
        private TrafficSession _trafficSession;
        private bool _densitySyncing;
        private bool _maxVehiclesSyncing;

        public MainWindow()
        {
            InitializeComponent();

            _logger = new SimpleFileLogger();
            _trafficManager = new TrafficManager(new OverpassRoadProvider(_logger))
            {
                VehicleTitle = VehicleTitle,
                MaxVehicles = 30,
                UserDensityMultiplier = 0.5
            };
            _simConnectService = new SimConnectService(_logger);

            DensitySlider.ValueChanged += OnDensitySliderChanged;
            DensityTextBox.LostFocus += OnDensityTextBoxLostFocus;
            DensityTextBox.KeyDown += OnDensityTextBoxKeyDown;
            MaxVehiclesSlider.ValueChanged += OnMaxVehiclesSliderChanged;
            MaxVehiclesTextBox.LostFocus += OnMaxVehiclesTextBoxLostFocus;
            MaxVehiclesTextBox.KeyDown += OnMaxVehiclesTextBoxKeyDown;
            TickrateComboBox.SelectionChanged += OnTickrateChanged;

            ChkMotorway.Checked += OnRoadTypeCheckboxChanged;
            ChkMotorway.Unchecked += OnRoadTypeCheckboxChanged;
            ChkTrunk.Checked += OnRoadTypeCheckboxChanged;
            ChkTrunk.Unchecked += OnRoadTypeCheckboxChanged;
            ChkPrimary.Checked += OnRoadTypeCheckboxChanged;
            ChkPrimary.Unchecked += OnRoadTypeCheckboxChanged;
            ChkSecondary.Checked += OnRoadTypeCheckboxChanged;
            ChkSecondary.Unchecked += OnRoadTypeCheckboxChanged;
            ChkTertiary.Checked += OnRoadTypeCheckboxChanged;
            ChkTertiary.Unchecked += OnRoadTypeCheckboxChanged;
            ChkResidential.Checked += OnRoadTypeCheckboxChanged;
            ChkResidential.Unchecked += OnRoadTypeCheckboxChanged;
            ChkUnclassified.Checked += OnRoadTypeCheckboxChanged;
            ChkUnclassified.Unchecked += OnRoadTypeCheckboxChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            _trafficSession = new TrafficSession(_simConnectService, _trafficManager, helper.Handle, _logger);
            _trafficSession.SessionUpdated += OnSessionUpdated;
            _trafficSession.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _trafficSession?.Stop();
            base.OnClosed(e);
        }

        private void OnSessionUpdated(TrafficSessionSnapshot snapshot)
        {
            Dispatcher.Invoke(() =>
            {
                StatusDot.Fill = new SolidColorBrush(snapshot.IsConnected ? Color.FromRgb(0x44, 0xFF, 0x44) : Color.FromRgb(0xFF, 0x44, 0x44));
                StatusText.Text = snapshot.IsConnected ? "Connected" : "Disconnected";
                VehiclesText.Text = $"Vehicles: {snapshot.ActiveVehicles}/{snapshot.MaxVehicles}";
                RoadsText.Text = snapshot.ActiveRoads > 0 ? $"Roads: {snapshot.ActiveRoads} segments" : "Roads: - segments";
                KmText.Text = snapshot.ActiveRoads > 0 ? $"{snapshot.TotalRoadKm:F1} km total" : string.Empty;
            });
        }

        private void OnDensitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_densitySyncing)
            {
                return;
            }

            int value = (int)Math.Round(DensitySlider.Value);
            _densitySyncing = true;
            DensityTextBox.Text = value.ToString();
            _densitySyncing = false;
            _trafficManager.UserDensityMultiplier = value / 100.0;
        }

        private void OnDensityTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyDensityTextBox();
            }
        }

        private void OnDensityTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            ApplyDensityTextBox();
        }

        private void ApplyDensityTextBox()
        {
            if (!int.TryParse(DensityTextBox.Text, out int value))
            {
                value = 50;
            }

            value = Math.Max(0, Math.Min(100, value));
            _densitySyncing = true;
            DensitySlider.Value = value;
            DensityTextBox.Text = value.ToString();
            _densitySyncing = false;
            _trafficManager.UserDensityMultiplier = value / 100.0;
        }

        private void OnMaxVehiclesSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_maxVehiclesSyncing)
            {
                return;
            }

            int value = (int)Math.Round(MaxVehiclesSlider.Value);
            _maxVehiclesSyncing = true;
            MaxVehiclesTextBox.Text = value.ToString();
            _maxVehiclesSyncing = false;
            _trafficManager.MaxVehicles = value;
        }

        private void OnMaxVehiclesTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyMaxVehiclesTextBox();
            }
        }

        private void OnMaxVehiclesTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            ApplyMaxVehiclesTextBox();
        }

        private void ApplyMaxVehiclesTextBox()
        {
            if (!int.TryParse(MaxVehiclesTextBox.Text, out int value))
            {
                value = 30;
            }

            value = Math.Max(0, Math.Min(400, value));
            _maxVehiclesSyncing = true;
            MaxVehiclesSlider.Value = value;
            MaxVehiclesTextBox.Text = value.ToString();
            _maxVehiclesSyncing = false;
            _trafficManager.MaxVehicles = value;
        }

        private void OnTickrateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_trafficSession == null)
            {
                return;
            }

            if (TickrateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag as string, out int milliseconds) && milliseconds > 0)
            {
                _trafficSession.UpdateIntervalMs = milliseconds;
            }
        }

        private void OnRoadTypeCheckboxChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkBox) || !(checkBox.Tag is string tag) || !Enum.TryParse(tag, out RoadType roadType))
            {
                return;
            }

            _trafficManager.SetRoadTypeEnabled(roadType, checkBox.IsChecked == true);
        }
    }
}

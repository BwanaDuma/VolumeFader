using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace VolumeFader.Midi
{
    public partial class MidiConfigWindow : Window
    {
        private readonly string _mapFilePath;
        private readonly MidiListenerService _service;
        public ObservableCollection<MidiMapping> Mappings { get; set; } = new ObservableCollection<MidiMapping>();
        private const string DesiredDeviceName = "loopMIDI Port";

        // Accept the shared service to avoid multiple instances trying to open the same device
        public MidiConfigWindow(string mapFilePath, MidiListenerService service)
        {
            InitializeComponent();
            _mapFilePath = mapFilePath;
            _service = service ?? new MidiListenerService(_mapFilePath);

            var list = _service.GetMappings().Mappings ?? new System.Collections.Generic.List<MidiMapping>();
            foreach (var m in list) Mappings.Add(m);

            MappingsGrid.ItemsSource = Mappings;

            // populate devices and auto-select the one containing DesiredDeviceName
            int selectIndex = -1;
            for (int i = 0; i < NAudio.Midi.MidiIn.NumberOfDevices; i++)
            {
                var name = NAudio.Midi.MidiIn.DeviceInfo(i).ProductName;
                DeviceCombo.Items.Add(name);
                if (selectIndex == -1 && !string.IsNullOrEmpty(name) && name.IndexOf(DesiredDeviceName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selectIndex = i;
                }
            }
            if (DeviceCombo.Items.Count > 0)
            {
                if (selectIndex >= 0 && selectIndex < DeviceCombo.Items.Count)
                    DeviceCombo.SelectedIndex = selectIndex;
                else
                    DeviceCombo.SelectedIndex = 0;
            }

            // Initial button states based on shared service
            StartListenButton.IsEnabled = !_service.IsRunning;
            StopListenButton.IsEnabled = _service.IsRunning;
        }

        private void StartListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceCombo.SelectedIndex < 0) return;

            int idx = DeviceCombo.SelectedIndex;
            var name = DeviceCombo.SelectedItem?.ToString() ?? string.Empty;

            // Ensure selected device contains the desired name
            if (string.IsNullOrEmpty(name) || name.IndexOf(DesiredDeviceName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                System.Windows.MessageBox.Show($"Selected device does not match required device '{DesiredDeviceName}'. Start aborted.", "Invalid Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_service.IsRunning)
            {
                System.Windows.MessageBox.Show("A MIDI device is already being listened to.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                StartListenButton.IsEnabled = false;
                StopListenButton.IsEnabled = true;
                return;
            }

            try
            {
                _service.Start(idx);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start MIDI listener: {ex}");
                System.Windows.MessageBox.Show($"Failed to start MIDI listener: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StartListenButton.IsEnabled = !_service.IsRunning;
            StopListenButton.IsEnabled = _service.IsRunning;
        }

        private void StopListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsRunning)
            {
                _service.Stop();
            }

            StartListenButton.IsEnabled = !_service.IsRunning;
            StopListenButton.IsEnabled = _service.IsRunning;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _service.SetMappings(new MidiMappingsFile{ Mappings = Mappings.ToList() });
            _service.SaveMappings();
            System.Windows.MessageBox.Show("Mappings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Do not dispose the shared service here; ownership remains with the main window
            this.Close();
        }
    }
}

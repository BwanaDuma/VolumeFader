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

        // Accept the shared service to avoid multiple instances trying to open the same device
        public MidiConfigWindow(string mapFilePath, MidiListenerService service)
        {
            InitializeComponent();
            _mapFilePath = mapFilePath;
            _service = service ?? new MidiListenerService(_mapFilePath);

            var list = _service.GetMappings().Mappings ?? new System.Collections.Generic.List<MidiMapping>();
            foreach (var m in list) Mappings.Add(m);

            MappingsGrid.ItemsSource = Mappings;

            // populate devices
            for (int i = 0; i < NAudio.Midi.MidiIn.NumberOfDevices; i++)
            {
                DeviceCombo.Items.Add(NAudio.Midi.MidiIn.DeviceInfo(i).ProductName);
            }
            if (DeviceCombo.Items.Count > 0) DeviceCombo.SelectedIndex = 0;

            // Disable the start button if the service is already running
            StartListenButton.IsEnabled = !_service.IsRunning;
        }

        private void StartListenButton_Click(object sender, RoutedEventArgs e)
        {
            int idx = DeviceCombo.SelectedIndex;
            if (idx < 0) return;

            if (_service.IsRunning)
            {
                System.Windows.MessageBox.Show("A MIDI device is already being listened to.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                StartListenButton.IsEnabled = false;
                return;
            }

            _service.Start(idx);
            StartListenButton.IsEnabled = false;
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

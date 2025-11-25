using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace VolumeFader
{
    public class DeviceCommands
    {
        public string? ProjectorOnString { get; set; }
        public string? ProjectorOffString1 { get; set; }
        public string? ProjectorOffString2 { get; set; }
        public string? TrackingOnString { get; set; }
        public string? TrackingOffString { get; set; }
        public bool? DebugMode { get; set; }
    }

    public partial class MainWindow : Window
    {
        // Reference to your audio logic (better done via ViewModel in MVVM)
        private AudioControlService _audioService;
        private Midi.MidiListenerService? _midiService;
        private string _midiMapFile;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize Audio Service (consider Dependency Injection or MVVM)
            _audioService = new AudioControlService();

            // Hook up events (Needs Dispatcher for thread safety)
            _audioService.VolumeChanged += AudioService_VolumeChanged;
            _audioService.MuteStateChanged += AudioService_MuteStateChanged;
            _audioService.PeakLevelChanged += AudioService_PeakLevelChanged;

            // Set initial UI state
            UpdateVolumeSlider(_audioService.GetMasterVolume());
            UpdateMuteButton(_audioService.IsMuted());

            _midiMapFile = Path.Combine(AppContext.BaseDirectory, "midiMappings.json");
            _midiService = new Midi.MidiListenerService(_midiMapFile);
            // start listening on default device if available
            if (NAudio.Midi.MidiIn.NumberOfDevices > 0)
            {
                _midiService.Start(-1);
            }

            // Add right-click menu to open config
            this.MouseRightButtonUp += (s,e) => {
                var cfg = new Midi.MidiConfigWindow(_midiMapFile, _midiService) { Owner = this };
                cfg.ShowDialog();
                // reload mappings after config window closes
                _midiService.LoadMappings();
            };
        }

        private void AudioService_PeakLevelChanged(object sender, float peak)
        {
            // Ensure UI updates happen on the UI thread
            Dispatcher.Invoke(() => {
                VuMeter.Value = peak;
            });
        }

        private void AudioService_MuteStateChanged(object sender, bool isMuted)
        {
            Dispatcher.Invoke(() => {
                UpdateMuteButton(isMuted);
            });
        }

        private void AudioService_VolumeChanged(object sender, float volume)
        {
             Dispatcher.Invoke(() => {
                 UpdateVolumeSlider(volume);
            });
        }

        // --- UI Event Handlers (Direct method - MVVM with Commands/Binding is cleaner) ---

        private bool _isInternalVolumeChange = false; // Flag to prevent feedback loop

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioService != null && !_isInternalVolumeChange)
            {
                // Convert slider value (0-100) to scalar (0.0-1.0)
                float newVolumeScalar = (float)(e.NewValue / 100.0);
                _audioService.SetMasterVolume(newVolumeScalar);
            }
        }

         private void MuteButton_Checked(object sender, RoutedEventArgs e)
         {
             _audioService?.SetMute(true);

            // Create a LinearGradientBrush
            LinearGradientBrush gradientBrush = new LinearGradientBrush();
            gradientBrush.StartPoint = new System.Windows.Point(0.5, 0);
            gradientBrush.EndPoint = new System.Windows.Point(0.5,1);

            // Add gradient stops (red to green)
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 0));
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Firebrick, 0.5));
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 1));

            // Apply the gradient to the Border element
            this.Background = gradientBrush;
         }

         private void MuteButton_Unchecked(object sender, RoutedEventArgs e)
         {
              _audioService?.SetMute(false);

            // Create a LinearGradientBrush
            LinearGradientBrush gradientBrush = new LinearGradientBrush();
            gradientBrush.StartPoint = new System.Windows.Point(0.5, 0);
            gradientBrush.EndPoint = new System.Windows.Point(0.5,1);

            // Add gradient stops (red to green)
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 0));
            gradientBrush.GradientStops.Add(new GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF272727"), 0.5));
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 1));

            // Apply the gradient to the Border element
            this.Background = gradientBrush;
         }

        // --- Helper methods to update UI ---

         private void UpdateVolumeSlider(float volumeScalar)
         {
             // Set flag to prevent ValueChanged handler from re-calling SetMasterVolume
             _isInternalVolumeChange = true;
             VolumeSlider.Value = volumeScalar * 100.0;
             _isInternalVolumeChange = false;
         }

         private void UpdateMuteButton(bool isMuted)
         {
            MuteButton.IsChecked = isMuted;
         }


        // --- Window State Persistence ---

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Load window position and size
            this.Top = Properties.Settings.Default.WindowTop;
            this.Left = Properties.Settings.Default.WindowLeft;
            this.Height = Properties.Settings.Default.WindowHeight;
            this.Width = Properties.Settings.Default.WindowWidth;

            // Optional: Add checks to ensure the window is visible on a screen
             EnsureWindowIsVisible();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
             // Save window position and size (only if not minimized)
             if (this.WindowState == WindowState.Normal)
             {
                 Properties.Settings.Default.WindowTop = this.Top;
                 Properties.Settings.Default.WindowLeft = this.Left;
                 Properties.Settings.Default.WindowHeight = this.Height;
                 Properties.Settings.Default.WindowWidth = this.Width;
             }
             else // Handle saving maximized state if needed, or just save last normal coords
             {
                  Properties.Settings.Default.WindowTop = this.RestoreBounds.Top;
                  Properties.Settings.Default.WindowLeft = this.RestoreBounds.Left;
                  Properties.Settings.Default.WindowHeight = this.RestoreBounds.Height;
                  Properties.Settings.Default.WindowWidth = this.RestoreBounds.Width;
             }

            Properties.Settings.Default.Save();

            // Clean up audio service
            _audioService?.Dispose();
            _midiService?.Dispose();
        }

        private void EnsureWindowIsVisible()
        {
            // Basic check: Reset if off-screen (e.g., monitor disconnected)
            // More robust checks involving Screen class might be needed for complex multi-monitor setups
            double virtualScreenWidth = SystemParameters.VirtualScreenWidth;
            double virtualScreenHeight = SystemParameters.VirtualScreenHeight;
            double virtualScreenLeft = SystemParameters.VirtualScreenLeft;
            double virtualScreenTop = SystemParameters.VirtualScreenTop;

            if (this.Left < virtualScreenLeft || this.Top < virtualScreenTop ||
                this.Left + this.Width > virtualScreenLeft + virtualScreenWidth ||
                this.Top + this.Height > virtualScreenTop + virtualScreenHeight)
            {
                 // Window is potentially off-screen, reset to default or center
                 this.Left = 100;
                 this.Top = 100;
                 // You might want to use Properties.Settings.Default defaults here
            }
        }

        private async void ProjectorOnButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "deviceCommands.json");

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var commands = JsonSerializer.Deserialize<DeviceCommands>(json);

                    if (!string.IsNullOrWhiteSpace(commands?.ProjectorOnString))
                    {
                        System.Diagnostics.Debug.WriteLine($"Sending Projector On command: {commands.ProjectorOnString}");
                        await SendWebCommandAsync(commands.ProjectorOnString, (bool)commands?.DebugMode!);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] deviceCommands.json not found at: {filePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to read or parse deviceCommands.json: {ex}");
            }
        }

        private async void ProjectorOffButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "deviceCommands.json");

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var commands = JsonSerializer.Deserialize<DeviceCommands>(json);

                    if (!string.IsNullOrWhiteSpace(commands?.ProjectorOffString1))
                    {
                        System.Diagnostics.Debug.WriteLine($"Sending Projector Off command 1: {commands.ProjectorOffString1}");
                        await SendWebCommandAsync(commands.ProjectorOffString1, (bool)commands.DebugMode!);
                    }

                    if (!string.IsNullOrWhiteSpace(commands?.ProjectorOffString2))
                    {
                        System.Diagnostics.Debug.WriteLine($"Sending Projector Off command 2: {commands.ProjectorOffString2}");
                        await SendWebCommandAsync(commands.ProjectorOffString2, (bool)commands.DebugMode!);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] deviceCommands.json not found at: {filePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to read or parse deviceCommands.json: {ex}");
            }
        }

        private async void TrackingOnButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "deviceCommands.json");

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var commands = JsonSerializer.Deserialize<DeviceCommands>(json);

                    if (!string.IsNullOrWhiteSpace(commands?.TrackingOnString))
                    {
                        await SendWebCommandAsync(commands.TrackingOnString, (bool)commands?.DebugMode!);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] deviceCommands.json not found at: {filePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to read or parse deviceCommands.json: {ex}");
            }
        }

        private async void TrackingOffButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "deviceCommands.json");

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var commands = JsonSerializer.Deserialize<DeviceCommands>(json);

                    if (!string.IsNullOrWhiteSpace(commands?.TrackingOffString))
                    {
                        await SendWebCommandAsync(commands.TrackingOffString, (bool)commands?.DebugMode!);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] deviceCommands.json not found at: {filePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to read or parse deviceCommands.json: {ex}");
            }
        }

        private async Task SendWebCommandAsync(string url, bool DebugMode = false)
        {
            if (DebugMode)
                System.Windows.MessageBox.Show($"Sending command:\n{url}", "Command Sent", MessageBoxButton.OK, MessageBoxImage.Information);

            try
            {
                // Parse username and password from the URL if present
                var uri = new System.Uri(url);
                string username = uri.UserInfo.Split(':').FirstOrDefault();
                string password = uri.UserInfo.Split(':').Skip(1).FirstOrDefault();

                var handler = new HttpClientHandler();
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    handler.Credentials = new System.Net.NetworkCredential(username, password);
                    // Remove credentials from URL for the actual request
                    url = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{uri.PathAndQuery}";
                }

                using var httpClient = new HttpClient(handler);
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    if (DebugMode)
                        System.Windows.MessageBox.Show($"Success: {response.StatusCode}", "Command Result", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show($"Command failed: {response.StatusCode}", "Command Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    System.Diagnostics.Debug.WriteLine($"[ERROR] Command failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Command Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[ERROR] Exception sending web command: {ex}");
            }
        }

        public Midi.MidiListenerService? MidiService => _midiService;
    }
}
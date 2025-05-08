using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace VolumeFader
{
    public partial class MainWindow : Window
    {
        // Reference to your audio logic (better done via ViewModel in MVVM)
        private AudioControlService _audioService;    

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
            gradientBrush.StartPoint = new Point(0.5, 0);
            gradientBrush.EndPoint = new Point(0.5,1);

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
            gradientBrush.StartPoint = new Point(0.5, 0);
            gradientBrush.EndPoint = new Point(0.5,1);

            // Add gradient stops (red to green)
            gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 0));
            gradientBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF272727"), 0.5));
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
        
    }
}
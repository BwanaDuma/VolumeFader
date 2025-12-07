using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

            // Set VuMeter tooltip to current default audio device name (permanent tooltip)
            try
            {
                var deviceName = _audioService.DefaultDeviceName ?? "(no audio device)";
                Dispatcher.InvokeAsync(() => { try { VuMeter.ToolTip = deviceName; } catch { } });
            }
            catch { }

            _midiMapFile = Path.Combine(AppContext.BaseDirectory, "midiMappings.json");
            _midiService = new Midi.MidiListenerService(_midiMapFile);

            // watch for incoming messages to animate - subscribe before starting the service to avoid missed events
            if (_midiService != null)
            {
                _midiService.DebugMessageLogged += (msg) => {
                    // schedule animation on UI thread - start animation without awaited
                    Dispatcher.InvokeAsync(() => { _ = AnimateListeningRuns(); });
                };

                // Prefer direct MIDI notifications for animation (fired as soon as a message is received)
                _midiService.MidiMessageReceived += () => {
                    Dispatcher.InvokeAsync(() => { _ = AnimateListeningRuns(); });
                };

                // Also subscribe to the debug messages collection changed as a backup
                if (_midiService.DebugMessages is System.Collections.Specialized.INotifyCollectionChanged coll)
                {
                    coll.CollectionChanged += (s, e) => {
                        if (e.Action == NotifyCollectionChangedAction.Add)
                        {
                            Dispatcher.InvokeAsync(() => { _ = AnimateListeningRuns(); });
                        }
                    };
                }

                // Subscribe to property changes to track IsRunning and animate "NOT LISTENING"
                _midiService.PropertyChanged += MidiService_PropertyChanged;

                System.Diagnostics.Debug.WriteLine("MainWindow: subscribed to MIDI service debug events");
            }

            // start listening on default device if available
            // Defer start logic to Loaded handler so we can use async/await
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Attempt initial start; if not found, show prompt and retry loop
            bool started = false;
            try
            {
                started = _midiService?.Start(-1) ?? false;
            }
            catch { started = false; }

            if (!started)
            {
                // Show blocking wait dialog to the user and retry inside it
                var dlg = new LoopMidiWaitWindow("The application loopMIDI is not running and must be started now!") { Owner = this };
                var result = false;
                // Show dialog modeless and run wait logic, but block caller until dialog completes
                dlg.Show();
                try
                {
                    result = await dlg.WaitFor(() => {
                        try { return _midiService?.Start(-1) ?? false; } catch { return false; }
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
                }
                finally
                {
                    dlg.Close();
                }

                if (!result)
                {
                    // Show final message box informing user the retry failed
                    System.Windows.MessageBox.Show("Failed to start loopMIDI after retries.", "loopMIDI not running", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
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

        private void DebugMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Only animate when items are added
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Start animation on UI thread but don't block
                Dispatcher.BeginInvoke(new Action(async () => await AnimateListeningRuns()));
            }
        }

        private bool _isAnimating = false;
        private CancellationTokenSource? _animationCts;

        private async Task AnimateListeningRuns()
        {
            // Cancel any currently running animation and start fresh
            _animationCts?.Cancel();
            _animationCts = new CancellationTokenSource();
            var token = _animationCts.Token;

            try
            {
                System.Diagnostics.Debug.WriteLine("AnimateListeningRuns: start (restart)");

                // Find the ListeningRunsBlock TextBlock from the visual tree
                var tb = this.FindName("ListeningRunsBlock") as TextBlock;
                if (tb == null)
                {
                    System.Diagnostics.Debug.WriteLine("AnimateListeningRuns: ListeningRunsBlock not found");
                    return;
                }

                // Support InlineUIContainer/TextBlock children (used to tighten spacing)
                var uiContainers = tb.Inlines.OfType<InlineUIContainer>().ToArray();
                if (uiContainers != null && uiContainers.Length > 0)
                {
                    var textBlocks = uiContainers.Select(c => c.Child as TextBlock).Where(t => t != null).ToArray()!;
                    if (textBlocks.Length > 0)
                    {
                        foreach (var tblock in textBlocks)
                        {
                            token.ThrowIfCancellationRequested();
                            var original = tblock!.Foreground;
                            try
                            {
                                tblock.Foreground = System.Windows.Media.Brushes.Red; // animate to Red
                                await Task.Delay(250, token);
                            }
                            finally
                            {
                                tblock.Foreground = System.Windows.Media.Brushes.White; // revert to White
                            }
                        }
                    }
                }
                else
                {
                    // Fallback to Runs if present
                    var runs = tb.Inlines.OfType<Run>().ToArray();
                    if (runs != null && runs.Length > 0)
                    {
                        foreach (var run in runs)
                        {
                            token.ThrowIfCancellationRequested();
                            var original = run.Foreground;
                            try
                            {
                                run.Foreground = System.Windows.Media.Brushes.Red;
                                await Task.Delay(250, token);
                            }
                            finally
                            {
                                run.Foreground = System.Windows.Media.Brushes.White;
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine("AnimateListeningRuns: finished");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("AnimateListeningRuns: cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimateListeningRuns exception: {ex}");
            }
        }

        private void ListenerStatus_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var cfg = new Midi.MidiConfigWindow(_midiMapFile, _midiService) { Owner = this };
                cfg.ShowDialog();
                _midiService?.LoadMappings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening MIDI config: {ex}");
            }
        }

        private DateTime _lastLeftClick = DateTime.MinValue;
        private readonly TimeSpan _doubleClickWindow = TimeSpan.FromMilliseconds(1000);

        private void ListenerStatus_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastLeftClick <= _doubleClickWindow)
                {
                    // Detected simulated double-click
                    _lastLeftClick = DateTime.MinValue;
                    ToggleMidiListener();
                }
                else
                {
                    _lastLeftClick = now;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling left click: {ex}");
            }
        }

        private void ToggleMidiListener()
        {
            try
            {
                if (_midiService == null && _midiService == null) return; // defensively check both names

                var service = _midiService ?? _midiService;
                if (service == null) return;

                if (service.IsRunning)
                {
                    service.Stop();
                    System.Diagnostics.Debug.WriteLine("MIDI listener stopped via simulated double-click.");
                }
                else
                {
                    try
                    {
                        service.Start(-1);
                        System.Diagnostics.Debug.WriteLine("MIDI listener started via simulated double-click.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to start MIDI listener via UI: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleMidiListener exception: {ex}");
            }
        }

        private CancellationTokenSource? _notListeningCts;

        private void MidiService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Midi.MidiListenerService.IsRunning))
            {
                // If service stopped, start NOT LISTENING animation; if started, cancel it
                var running = _midiService?.IsRunning ?? false;
                if (!running)
                {
                    // start animation loop
                    _ = AnimateNotListeningLoop();
                }
                else
                {
                    // cancel any running not-listening animation and reset color
                    try { _notListeningCts?.Cancel(); } catch { }
                    Dispatcher.Invoke(() => {
                        if (NotListeningText != null)
                            NotListeningText.Foreground = System.Windows.Media.Brushes.Yellow;
                    });
                }
            }
        }

        private async Task AnimateNotListeningLoop()
        {
            // Cancel existing animation and start a fresh one
            _notListeningCts?.Cancel();
            _notListeningCts = new CancellationTokenSource();
            var token = _notListeningCts.Token;

            try
            {
                while (!token.IsCancellationRequested && (_midiService?.IsRunning == false))
                {
                    // Set DarkGray
                    Dispatcher.Invoke(() => {
                        if (NotListeningText != null)
                            NotListeningText.Foreground = System.Windows.Media.Brushes.Black;
                    });

                    await Task.Delay(250, token);

                    // Set Yellow
                    Dispatcher.Invoke(() => {
                        if (NotListeningText != null)
                            NotListeningText.Foreground = System.Windows.Media.Brushes.Yellow;
                    });

                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimateNotListeningLoop exception: {ex}");
            }
            finally
            {
                try { _notListeningCts?.Dispose(); } catch { }
                _notListeningCts = null;
            }
        }

        private async Task HandleDeviceDisconnectedAsync()
        {
            try
            {
                // Ensure listener is stopped
                try { _midiService?.Stop(); } catch { }

                var dlg = new LoopMidiWaitWindow("loopMIDI was stopped. Please start loopMIDI now to continue.") { Owner = this };
                dlg.Show();
                try
                {
                    bool ok = await dlg.WaitFor(() => { try { return _midiService?.Start(-1) ?? false; } catch { return false; } }, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
                    if (!ok)
                    {
                        System.Windows.MessageBox.Show("Failed to restart loopMIDI after retries.", "loopMIDI not running", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    dlg.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandleDeviceDisconnectedAsync exception: {ex}");
            }
        }

        private void RefreshAudioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ok = _audioService?.RefreshDefaultDevice() ?? false;
                if (!ok)
                {
                    System.Windows.MessageBox.Show("Failed to refresh audio device.", "Audio Refresh", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Audio device refreshed successfully.");
                    // Update UI state from refreshed audio service
                    try
                    {
                        UpdateVolumeSlider(_audioService.GetMasterVolume());
                        UpdateMuteButton(_audioService.IsMuted());

                        // Update permanent VuMeter tooltip to show new default device name
                        try
                        {
                            var deviceName = _audioService.DefaultDeviceName ?? "(no audio device)";
                            Dispatcher.Invoke(() => { VuMeter.ToolTip = deviceName; });
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating UI after audio refresh: {ex}");
                    }

                    // Animate the refresh button to indicate success
                    try
                    {
                        var sb = new System.Windows.Media.Animation.Storyboard();
                        var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.4, new Duration(TimeSpan.FromMilliseconds(120))) { AutoReverse = true };
                        var scaleY = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.4, new Duration(TimeSpan.FromMilliseconds(120))) { AutoReverse = true };

                        // Target the RefreshAudioButton directly (more reliable than SetTargetName)
                        System.Windows.Media.Animation.Storyboard.SetTarget(scaleX, RefreshAudioButton);
                        System.Windows.Media.Animation.Storyboard.SetTarget(scaleY, RefreshAudioButton);
                        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
                        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));

                        sb.Children.Add(scaleX);
                        sb.Children.Add(scaleY);

                        sb.Begin();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error playing refresh animation: {ex}");
                    }

                    // Show a temporary tooltip on the refresh button to indicate success
                    try
                    {
                        var originalTooltip = RefreshAudioButton.ToolTip;
                        var tip = new System.Windows.Controls.ToolTip { Content = "Refreshed", PlacementTarget = RefreshAudioButton, IsOpen = true, StaysOpen = false };

                        RefreshAudioButton.ToolTip = tip;

                        // Close after 1 second and restore original tooltip
                        Task.Run(async () => {
                            await Task.Delay(1000);
                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    tip.IsOpen = false;
                                    RefreshAudioButton.ToolTip = originalTooltip;
                                }
                                catch { }
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing temporary tooltip: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshAudioButton_Click exception: {ex}");
            }
        }
    }
}
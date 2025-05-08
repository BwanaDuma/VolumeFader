 using System;
 using NAudio.CoreAudioApi;
 using System.Timers; // Use System.Timers.Timer for background thread

namespace VolumeFader
{
    public class AudioControlService : IDisposable
    {
        private MMDeviceEnumerator _deviceEnumerator;
        private MMDevice _defaultDevice;
        private AudioEndpointVolume _volumeControl;
        private AudioMeterInformation _meterInformation;
        private double _lastSetVolumeLevel;
        private System.Timers.Timer _peakMeterTimer;

        // Events to notify the UI about changes
        public event EventHandler<float> VolumeChanged;
        public event EventHandler<bool> MuteStateChanged;
        public event EventHandler<float> PeakLevelChanged;

        public AudioControlService()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                // Get the default rendering device (speakers/headphones)
                _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (_defaultDevice != null)
                {
                    _volumeControl = _defaultDevice.AudioEndpointVolume;
                    _meterInformation = _defaultDevice.AudioMeterInformation;

                    // Subscribe to system volume/mute changes
                    _volumeControl.OnVolumeNotification += OnSystemVolumeNotification;

                    // Setup timer for VU Meter updates (e.g., 20 times per second)
                    _peakMeterTimer = new System.Timers.Timer(500); // 50ms interval
                    _peakMeterTimer.Elapsed += PeakMeterTimer_Elapsed;
                    _peakMeterTimer.AutoReset = true;
                    _peakMeterTimer.Start();
                }
                else
                {
                     // Handle case where no default audio device is found
                     Console.WriteLine("Error: No default audio rendering device found.");
                     // You might want to throw an exception or disable functionality
                }
            }
            catch (Exception ex)
            {
                // Log or handle initialization errors
                Console.WriteLine($"AudioControlService Initialization Error: {ex.Message}");
                // Rethrow or handle gracefully
            }
        }

        private void PeakMeterTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_meterInformation != null)
            {
                try
                {
                    // Get the peak value (0.0 to 1.0)
                    float peak = _meterInformation.MasterPeakValue;
                    // Raise event on the UI thread if using WPF/WinForms
                    // We'll handle thread marshalling in the ViewModel/Code-behind
                    PeakLevelChanged?.Invoke(this, peak);
                }
                catch (Exception ex)
                {
                     // Handle potential errors reading peak value (e.g., device unplugged)
                     Console.WriteLine($"Error reading peak value: {ex.Message}");
                     _peakMeterTimer?.Stop(); // Stop timer if device is problematic
                }
            }
        }

        private void OnSystemVolumeNotification(AudioVolumeNotificationData data)
        {
            // This event handler might be called on a different thread.
            // We need to marshal the calls back to the UI thread later.
            // Using Dispatcher for WPF or Control.Invoke for WinForms.

            // Notify subscribers about the changes
            VolumeChanged?.Invoke(this, data.MasterVolume);
            MuteStateChanged?.Invoke(this, data.Muted);
        }

        // --- Public Methods for UI Interaction ---

        public float GetMasterVolume()
        {
            return _volumeControl?.MasterVolumeLevelScalar ?? 0.0f; // Returns 0.0 to 1.0
        }

        public void SetMasterVolume(float volume) // volume expected 0.0 to 1.0
        {
            if (_volumeControl != null)
            {               
                // Clamp value to be safe
                volume = Math.Max(0.0f, Math.Min(1.0f, volume));
                _volumeControl.MasterVolumeLevelScalar = volume;
            }

            _lastSetVolumeLevel = volume;
        }

        public bool IsMuted()
        {
            return _volumeControl?.Mute ?? false;
        }

        public void SetMute(bool isMuted)
        {
            if (_volumeControl != null)
            {
                //if (IsMuted()) {
                //    await FadeIn();
                //} else {
                //    await FadeOut();                    
                //}

                _volumeControl.Mute = isMuted;
            }
        }

        public async Task FadeOut()
        {         
            if (_volumeControl != null) {
                while (_volumeControl.MasterVolumeLevel > 0.1)
                {
                    _volumeControl.VolumeStepDown();
                    await Task.Delay(200);
                }
            }
        }

        public async Task FadeIn()
        {           
            if (_volumeControl != null) {
                while (_volumeControl.MasterVolumeLevel < _lastSetVolumeLevel)
                {
                    _volumeControl.VolumeStepUp();
                    await Task.Delay(200);
                }
            }
        }

        public void ToggleMute()
        {
             if (_volumeControl != null)
             {                
                 _volumeControl.Mute = !_volumeControl.Mute;
             }
        }

        public void Dispose()
        {
            _peakMeterTimer?.Stop();
            _peakMeterTimer?.Dispose();

            if (_volumeControl != null)
            {
                _volumeControl.OnVolumeNotification -= OnSystemVolumeNotification;
                _volumeControl.Dispose();
                _volumeControl = null;
            }
            // Meter information doesn't seem to require explicit Dispose in NAudio docs
             _meterInformation = null;

            _defaultDevice?.Dispose();
            _defaultDevice = null;

            _deviceEnumerator?.Dispose();
            _deviceEnumerator = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using NAudio.Midi;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using System.ComponentModel;

namespace VolumeFader.Midi
{
    public class MidiListenerService : IDisposable, INotifyPropertyChanged
    {
        private MidiIn? _midiIn;
        private MidiMappingsFile _mappings = new MidiMappingsFile();
        private string _mapFilePath;
        private string _defaultMidiDevice = "loopMIDI Port";

        public MidiListenerService(string mapFilePath)
        {
            _mapFilePath = mapFilePath;
            LoadMappings();
        }

        // Indicates whether the service currently has an open MIDI input
        public bool IsRunning => _midiIn != null;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Start(int deviceIndex = 0)
        {
            if (MidiIn.NumberOfDevices == 0) return;

            int chosenIndex = deviceIndex;

            // If provided index is out of range, try to find the configured default device by name
            if (deviceIndex < 0 || deviceIndex >= MidiIn.NumberOfDevices)
            {
                chosenIndex = -1;
                for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                {
                    try
                    {
                        var info = MidiIn.DeviceInfo(i);
                        if (!string.IsNullOrEmpty(info.ProductName) && info.ProductName.IndexOf(_defaultMidiDevice, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            chosenIndex = i;
                            break;
                        }
                    }
                    catch
                    {
                        // ignore device info read errors and continue
                    }
                }

                if (chosenIndex == -1)
                {
                    System.Diagnostics.Debug.WriteLine($"No valid MIDI device index provided and no device matching '{_defaultMidiDevice}' was found. Aborting Start.");
                    return;
                }
            }

            // Prevent attempting to open the device if already started in this process
            if (_midiIn != null)
            {
                // Already running; no-op
                System.Diagnostics.Debug.WriteLine($"MIDI listener already running (deviceIndex={chosenIndex}).");
                return;
            }

            try
            {
                _midiIn = new MidiIn(chosenIndex);
                _midiIn.MessageReceived += MidiIn_MessageReceived;
                _midiIn.ErrorReceived += MidiIn_ErrorReceived;
                _midiIn.Start();

                // Debug: indicate listener started and show device info
                try
                {
                    var info = MidiIn.DeviceInfo(chosenIndex);
                    System.Diagnostics.Debug.WriteLine($"MIDI listener started on device {chosenIndex}: {info.ProductName}");
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"MIDI listener started on device {chosenIndex}.");
                }

                // Additional status line to make it explicit when the listener is running
                System.Diagnostics.Debug.WriteLine($"MIDI listener running: {IsRunning} (deviceIndex={chosenIndex})");

                // Notify bindings that IsRunning changed
                OnPropertyChanged(nameof(IsRunning));
            }
            catch (NAudio.MmException mmex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open MIDI device {chosenIndex}: {mmex}");
                throw;
            }
        }

        public void Stop()
        {
            if (_midiIn != null)
            {
                try
                {
                    // Try to capture device info for logging
                    int idx = 0;
                    var name = "";
                    //try { idx = _midiIn.DeviceNumber; } catch { }
                    try { name = MidiIn.DeviceInfo(idx).ProductName; } catch { }

                    _midiIn.Stop();
                    _midiIn.MessageReceived -= MidiIn_MessageReceived;
                    _midiIn.ErrorReceived -= MidiIn_ErrorReceived;
                    _midiIn.Dispose();
                    _midiIn = null;

                    System.Diagnostics.Debug.WriteLine($"MIDI listener stopped on device {idx}: {name}");

                    // Notify bindings that IsRunning changed
                    OnPropertyChanged(nameof(IsRunning));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping MIDI listener: {ex}");
                    _midiIn = null;
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("MIDI listener Stop called but it was not running.");
            }
        }

        private void MidiIn_ErrorReceived(object sender, MidiInMessageEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"MIDI Error: {e.RawMessage}");
        }

        private void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e)
        {
            try
            {
                var midiEvent = e.MidiEvent;
                int command = midiEvent.CommandCode switch
                {
                    MidiCommandCode.NoteOn => 144,
                    MidiCommandCode.NoteOff => 128,
                    MidiCommandCode.ControlChange => 176,
                    _ => (int)midiEvent.CommandCode
                };

                int data1 = 0;
                int data2 = 0;

                if (midiEvent is NoteEvent ne)
                {
                    data1 = ne.NoteNumber;
                    data2 = ne.Velocity;
                }
                else if (midiEvent is ControlChangeEvent cce)
                {
                    // cce.Controller is an enum (NAudio.Midi.MidiController); cast explicitly to int
                    data1 = (int)cce.Controller;
                    data2 = cce.ControllerValue;
                }

                int channel = midiEvent.Channel;

                // Debug: log the parsed MIDI message for troubleshooting
                System.Diagnostics.Debug.WriteLine($"MIDI Received: Raw=0x{e.RawMessage:X}, Event={midiEvent}, Command={command}, Data1={data1}, Data2={data2}, Channel={channel}");

                // Normalize raw hex for matching
                string rawHex = e.RawMessage.ToString("X");
                string normalizedRaw = NormalizeHexString(rawHex);

                // Use a switch-like control over the raw message value
                switch (normalizedRaw)
                {
                    default:
                        // Find mappings with matching Raw field (if any)
                        var rawMatches = (_mappings?.Mappings ?? new List<MidiMapping>())
                            .Where(m => !string.IsNullOrEmpty(m.Raw) && NormalizeHexString(m.Raw!) == normalizedRaw)
                            .ToList();

                        if (rawMatches.Count > 0)
                        {
                            foreach (var m in rawMatches)
                            {
                                // Debug: show which mapping matched and the keys we will send
                                System.Diagnostics.Debug.WriteLine($"MIDI Raw Mapping matched: Raw={m.Raw} -> Keys='{m.Keys}'");

                                // Parse the Keys field into SendKeys syntax
                                var sendKeys = ParseKeysToSendKeys(m.Keys);
                                System.Diagnostics.Debug.WriteLine($"Parsed SendKeys: '{sendKeys}'");

                                if (!string.IsNullOrEmpty(sendKeys))
                                {
                                    // Send keys asynchronously to avoid blocking
                                    Task.Run(() => {
                                        try
                                        {
                                            System.Windows.Forms.SendKeys.SendWait(sendKeys);
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"Error sending keys: {ex}");
                                        }
                                    });
                                }
                            }

                            // Since we handled raw matches, don't continue with regular mapping matching
                            return;
                        }

                        break;
                }

                // Find mapping by Channel/Command/Data1/Data2 if no raw mapping matched
                if (_mappings?.Mappings == null) return;

                foreach (var m in _mappings.Mappings)
                {
                    if (!string.IsNullOrEmpty(m.Raw)) continue; // skip raw-based mappings here

                    if (m.Channel.HasValue && m.Channel.Value != channel) continue;
                    if (m.Command.HasValue && m.Command.Value != command) continue;
                    if (m.Data1.HasValue && m.Data1.Value != data1) continue;
                    // data2 match only if specified
                    if (m.Data2.HasValue && m.Data2.Value != data2) continue;

                    if (!string.IsNullOrEmpty(m.Keys))
                    {
                        // Debug: show which mapping matched and the keys we will send
                        System.Diagnostics.Debug.WriteLine($"MIDI Mapping matched: Channel={channel} Command={command} Data1={data1} Data2={data2} -> Keys='{m.Keys}'");

                        var sendKeys = ParseKeysToSendKeys(m.Keys);
                        System.Diagnostics.Debug.WriteLine($"Parsed SendKeys: '{sendKeys}'");

                        // Send keys asynchronously to avoid blocking
                        Task.Run(() => {
                            try
                            {
                                // Use SendKeys from Windows Forms (fully qualified)
                                System.Windows.Forms.SendKeys.SendWait(sendKeys);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error sending keys: {ex}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Midi receive exception: {ex}");
            }
        }

        private static string NormalizeHexString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            // Remove spaces and non-hex characters
            var chars = t.Where(c => Uri.IsHexDigit(c)).ToArray();
            return new string(chars).ToUpperInvariant();
        }

        private static string ParseKeysToSendKeys(string? keys)
        {
            if (string.IsNullOrWhiteSpace(keys)) return string.Empty;
            var s = keys.Trim();

            // If the string is standard SendKeys syntax and not our custom brace format, return as-is
            // We treat brace-enclosed strings as potential custom syntax and parse them below.
            if (!s.StartsWith("{") && (s.Contains("%") || s.Contains("^") || s.Contains("+") || s.Contains("(") || s.Contains(")") || (s.Contains("{") && s.Contains("}"))))
                return s;

            // Support custom syntax like {Alt+Shift+D0} or {Ctrl+Alt+A}
            if (s.StartsWith("{") && s.EndsWith("}"))
            {
                var inner = s.Substring(1, s.Length - 2);
                var parts = inner.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                string modifiers = string.Empty;
                var keyChars = new List<string>();

                foreach (var part in parts)
                {
                    if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                        modifiers += "%"; // Alt
                    else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                        modifiers += "+"; // Shift
                    else if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                        modifiers += "^"; // Ctrl
                    else
                    {
                        var token = part;
                        // If token like D0 or d0 means digit 0 (or D10 -> 10)
                        if ((token.StartsWith("D", StringComparison.OrdinalIgnoreCase) && token.Length >= 2) || token.StartsWith("VK", StringComparison.OrdinalIgnoreCase))
                        {
                            if (token.StartsWith("D", StringComparison.OrdinalIgnoreCase))
                                token = token.Substring(1);
                            else if (token.StartsWith("VK", StringComparison.OrdinalIgnoreCase))
                                token = token.Substring(2);

                            // token now contains digits or a key name; add raw characters
                            keyChars.Add(token);
                        }
                        else
                        {
                            // If token represents a named key like ENTER, TAB, use {ENTER} syntax
                            if (token.Length > 1 && token.All(c => char.IsLetter(c)))
                            {
                                keyChars.Add("{" + token.ToUpperInvariant() + "}");
                            }
                            else
                            {
                                keyChars.Add(token);
                            }
                        }
                    }
                }

                var keyString = string.Concat(keyChars);
                if (string.IsNullOrEmpty(keyString)) return modifiers;

                // If multiple characters (e.g., "10"), wrap in parentheses so modifiers apply to all
                if (keyString.Length > 1 && !keyString.StartsWith("{"))
                    return modifiers + "(" + keyString + ")";

                return modifiers + keyString;
            }

            // Fallback: return original
            return s;
        }

        public void LoadMappings()
        {
            try
            {
                if (File.Exists(_mapFilePath))
                {
                    var json = File.ReadAllText(_mapFilePath);
                    _mappings = JsonSerializer.Deserialize<MidiMappingsFile>(json) ?? new MidiMappingsFile();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load midi mappings: {ex}");
                _mappings = new MidiMappingsFile();
            }
        }

        public void SaveMappings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_mappings, new JsonSerializerOptions{ WriteIndented = true });
                File.WriteAllText(_mapFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save midi mappings: {ex}");
            }
        }

        public MidiMappingsFile GetMappings() => _mappings;

        public void SetMappings(MidiMappingsFile mappings)
        {
            _mappings = mappings;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

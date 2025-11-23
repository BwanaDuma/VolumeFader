using System.Collections.Generic;

namespace VolumeFader.Midi
{
    public class MidiMapping
    {
        public string? Name { get; set; }
        public int? Channel { get; set; }
        public int? Command { get; set; } // e.g., 144 for note on
        public int? Data1 { get; set; } // note number or cc number
        public int? Data2 { get; set; } // velocity or value
        public string? Keys { get; set; } // Keys to send (SendKeys string or custom format)
        public string? Raw { get; set; } // Optional raw hex string to match (e.g., "0x90..." or "90...")
    }

    public class MidiMappingsFile
    {
        public List<MidiMapping>? Mappings { get; set; }
    }
}

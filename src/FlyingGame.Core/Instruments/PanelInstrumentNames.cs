using System;

namespace FlyingGame.Core.Instruments
{
    public enum PanelInstrument { None = 0, Altimeter, AltimeterKnob, AltimeterWindow, GMeter, GMeterReset }
    public enum PanelSeat { Rear = 0, Front = 1 }

    /// <summary>
    /// Which model nodes make up an altimeter or a g-meter, and in which seat, so a touch anywhere on the instrument can
    /// open its Kollsman popup or push its reset (Ty 2026-11-05). The fleet's GLBs name them every which way — Pitts
    /// <c>RearPanel_Altimeter_Face</c> / <c>FrontPanel_G_meter_Bezel</c>, Extra <c>Face_P3b_Altimeter_R</c> /
    /// <c>Needle_GMax_F</c>, Great Lakes <c>Needle_Alt100_R</c>, 2-33 <c>Dial_ALT_F</c>, J-3 <c>Needle_Alt_100ft</c>, C172
    /// <c>C172_Altimeter_case</c> — and the contract adds <c>&lt;prefix&gt;Altimeter_Knob</c>,
    /// <c>&lt;prefix&gt;Altimeter_Kollsman</c> and <c>&lt;prefix&gt;G_meter_Reset</c> for rebuilt panels. Names starting
    /// <c>Legacy_</c> are an older panel kept hidden in the Extra and are ignored.
    /// </summary>
    public static class PanelInstrumentNames
    {
        public static (PanelInstrument Kind, PanelSeat Seat) Classify(string name)
        {
            if (string.IsNullOrEmpty(name)) return (PanelInstrument.None, PanelSeat.Rear);
            if (name.StartsWith("Model:", StringComparison.Ordinal)) name = name.Substring(6);   // AirframeBuilder's prefix on placed nodes
            string n = name.ToLowerInvariant();
            if (n.StartsWith("legacy_") || n.StartsWith("live:")) return (PanelInstrument.None, PanelSeat.Rear);
            PanelInstrument k = Kind(n, name);
            return (k, k == PanelInstrument.None ? PanelSeat.Rear : SeatOf(name));
        }

        private static PanelInstrument Kind(string n, string raw)
        {
            if (n.Contains("altimeter"))
            {
                if (n.Contains("knob") || n.Contains("baro")) return PanelInstrument.AltimeterKnob;
                if (n.Contains("kollsman")) return PanelInstrument.AltimeterWindow;
                return PanelInstrument.Altimeter;
            }
            if (n.Contains("kollsman")) return PanelInstrument.AltimeterWindow;
            foreach (string pre in new[] { "needle_alt", "dial_alt", "bezel_alt", "face_alt" })
                if (n.StartsWith(pre) && n.Length > pre.Length && (char.IsDigit(n[pre.Length]) || n[pre.Length] == '_')) return PanelInstrument.Altimeter;
            if (n.Contains("g_meter") || n.Contains("gmeter") || n.Contains("accelerometer"))
                return n.Contains("reset") ? PanelInstrument.GMeterReset : PanelInstrument.GMeter;
            // Needle_G_R, Needle_GMax_F, Face_P3b_G_R, Bezel_P3b_G_F: a capital-G token on a dial part.
            string[] tok = raw.Split('_');
            if (tok.Length >= 2)
            {
                string first = tok[0].ToLowerInvariant();
                bool part = first == "needle" || first == "face" || first == "bezel" || first == "dial";
                for (int i = 1; i < tok.Length && part; i++)
                    if (tok[i] == "G" || tok[i] == "GMax" || tok[i] == "GMin" || tok[i] == "Gmeter")
                        return n.Contains("reset") ? PanelInstrument.GMeterReset : PanelInstrument.GMeter;
            }
            return PanelInstrument.None;
        }

        private static PanelSeat SeatOf(string name)
        {
            if (name.StartsWith("FrontPanel", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Front_", StringComparison.OrdinalIgnoreCase))
                return PanelSeat.Front;
            string[] tok = name.Split('_');
            string last = tok[tok.Length - 1];
            if (last == "F" || last.Equals("Front", StringComparison.OrdinalIgnoreCase)) return PanelSeat.Front;
            if (tok.Length >= 2 && tok[tok.Length - 2].Equals("Front", StringComparison.OrdinalIgnoreCase)) return PanelSeat.Front;
            return PanelSeat.Rear;
        }
    }
}

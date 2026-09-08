using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Subtle dev HUD (gauges stay quiet per the design). OnGUI is fine for the skeleton.</summary>
    public sealed class FlightHud : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public RaceController Race;
        public StolController Stol;
        public CropDustController Dust;
        private StructuralDamage _damage;
        public TowController Tow;
        private GUIStyle _style;
        private Net.NetSession _net;

        private void OnGUI()
        {
            if (Driver == null || SessionSettings.MenuOpen)
            {
                return;
            }

            // Scale text to the device: OnGUI uses native pixels, so a fixed 16 px is invisible on a
            // ~2600 px Retina phone. Size off the short screen edge (matches the touch buttons).
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.024f);
            // From scratch (no GUI.skin base): the built-in skin is stripped on iOS and NREs.
            _style ??= new GUIStyle
            {
                font = UiFont.Get(),
                alignment = TextAnchor.UpperCenter,   // HUD lives top-centre; controls own the bottom edge
                normal = { textColor = new Color(0.25f, 1f, 0.35f, 0.8f) },
            };
            _style.fontSize = fs;

            float m = fs;                 // left/top margin
            float lh = fs * 1.5f;         // line height
            float w = Screen.width - 2 * m;

            // Airspeed/altitude/AoA/sideslip now live in HudOverlay (green HUD over the aircraft).
            GUI.Label(new Rect(m, m * 0.5f, w, lh),
                $"{Driver.AircraftName}  ·  1-0 aircraft · arrows/A-D/S-W · T turb · C challenge · Y tow / G release · R reset", _style);

            string net = (_net ??= Driver.GetComponent<Net.NetSession>())?.StatusLine;
            if (net != null) GUI.Label(new Rect(m, m * 0.5f + lh * 2f, w, lh), net, _style);   // "FFA · 12 pilots" / "Room K7Q2ZP · 3 pilots"
            string ev = Race != null && Race.Line != null ? Race.Line : Stol != null && Stol.Line != null ? Stol.Line
                : Dust != null && Dust.Line != null ? Dust.Line : Tow != null && Tow.StatusLine != null ? Tow.StatusLine
                : (_damage ??= Driver.GetComponent<StructuralDamage>())?.LostLine;
            if (ev != null)
            {
                GUI.Label(new Rect(m, m * 0.5f + lh, w, lh), ev, _style);
            }
            // Spin grading line — only when the wing is stalled and rotation is established.
            if (Driver.AlphaDeg > 16.0 && Driver.SecPerTurn > 0)
            {
                GUI.Label(new Rect(m, m * 0.5f + lh, w, lh),
                    $"SPIN  {Driver.SecPerTurn:F1} s/turn   {Driver.FtPerTurn:F0} ft/turn   {Driver.DescentFtPerSec:F0} ft/s down", _style);
            }
        }
    }
}

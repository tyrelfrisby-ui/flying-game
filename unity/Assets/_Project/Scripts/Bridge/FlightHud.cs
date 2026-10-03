using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Subtle dev HUD (gauges stay quiet per the design). OnGUI is fine for the skeleton.</summary>
    public sealed class FlightHud : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public RaceController Race;
        public CombatController Combat;
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

            if (UiLayout.Modal) return;
            // Status line: its own row in the shared text stack, shrunk to fit (no-overlap rule). The keyboard hints only
            // where there is a keyboard (the Mac app).
            UiLayout.Label(UiLayout.NextLine(lh), $"{Driver.AircraftName}  ·  build {Application.version}  ·  β {Driver.BetaDeg:+0.0;-0.0}°", _style);
            // Mac: the key list gets its own line (sharing the status line shrank both to unreadable).
            if (TouchFlightControls.DeskMode)
                UiLayout.Label(UiLayout.NextLine(lh), "arrows stick · A/D rudder · W/S power · =/- trim · F flaps · L gear · T turb · C challenge · Y tow / G release · R reset", _style);

            string net = (_net ??= Driver.GetComponent<Net.NetSession>())?.StatusLine;
            if (net != null) UiLayout.Label(UiLayout.NextLine(lh), net, _style);   // "FFA · 12 pilots" / "Room K7Q2ZP · 3 pilots"
            string ev = Race != null && Race.Line != null ? Race.Line : Stol != null && Stol.Line != null ? Stol.Line
                : Dust != null && Dust.Line != null ? Dust.Line : Tow != null && Tow.StatusLine != null ? Tow.StatusLine
                : Combat != null && Combat.InZone && Combat.Line != null ? Combat.Line
                : (_damage ??= Driver.GetComponent<StructuralDamage>())?.LostLine;
            if (ev != null) UiLayout.Label(UiLayout.NextLine(lh), ev, _style);
            // Spin grading line — only when the wing is stalled and rotation is established.
            if (Driver.AlphaDeg > 16.0 && Driver.SecPerTurn > 0)
            {
                UiLayout.Label(UiLayout.NextLine(lh),
                    $"SPIN  {Driver.SecPerTurn:F1} s/turn   {Driver.FtPerTurn:F0} ft/turn   {Driver.DescentFtPerSec:F0} ft/s down", _style);
            }
        }
    }
}

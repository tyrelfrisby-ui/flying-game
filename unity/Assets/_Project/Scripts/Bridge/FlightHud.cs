using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Subtle dev HUD (gauges stay quiet per the design). OnGUI is fine for the skeleton.</summary>
    public sealed class FlightHud : MonoBehaviour
    {
        public FlightSimDriver Driver;
        private GUIStyle _style;

        private void OnGUI()
        {
            if (Driver == null)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = new Color(1f, 1f, 1f, 0.75f) },
            };

            double kt = Driver.IasMs * 1.9438;
            GUI.Label(new Rect(16, 12, 640, 24),
                $"IAS {Driver.IasMs:F1} m/s ({kt:F0} kt)   ALT {Driver.AltitudeM:F0} m   " +
                $"AoA {Driver.AlphaDeg:F1}°   β {Driver.BetaDeg:F1}°", _style);
            GUI.Label(new Rect(16, 34, 760, 22),
                "arrows = stick (Down pulls)  ·  A/D = rudder  ·  S/W = speed brake  ·  R = reset", _style);
        }
    }
}

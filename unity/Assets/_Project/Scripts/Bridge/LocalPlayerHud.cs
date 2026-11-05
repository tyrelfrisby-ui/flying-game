using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// LOCAL 2-PLAYER: the smallest per-player line, drawn inside the player's own half — who he is, which pad is his
    /// (or how to get one), and his airspeed and height when the green HUD is not showing them. A placeholder for the
    /// split-screen HUD lane's stripped layout, which reads the same <see cref="LocalPlayerView"/> and replaces this.
    /// </summary>
    public sealed class LocalPlayerHud : MonoBehaviour
    {
        public LocalPlayerView View;
        private GUIStyle _style;

        private void OnGUI()
        {
            if (!LocalSplit.Active || View == null || View.Driver == null || SessionSettings.MenuOpen || UiLayout.Modal) return;
            if (VrMode.SuppressFlatOverlays) return;
            Rect r = View.GuiRect;
            int fs = UiLayout.MinText;
            _style ??= new GUIStyle { font = UiFont.Get(), alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(0.25f, 1f, 0.35f, 0.85f) } };
            _style.fontSize = fs;
            string pad = GamepadInput.NameFor(View.PlayerIndex);
            string who = $"P{View.PlayerIndex + 1}  " + (pad ?? (View.PlayerIndex == 0 ? "touch" : "no pad: connect a second controller"));
            var d = View.Driver;
            string data = SessionSettings.Instruments == SessionSettings.InstrumentMode.Hud ? ""
                : $"   {d.IasMs * 1.9438:F0} kt   {d.AltitudeM * 3.28084:N0} ft";
            // Top of the player's half, inset from the outer edge (P1's line on the left, P2's on the right of his half).
            float m = fs * 0.8f, w = r.width - 2 * m;
            _style.alignment = View.PlayerIndex == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            GUI.Label(new Rect(r.x + m, r.y + m + fs * 2.2f, w, fs * 1.6f), who + data, _style);
        }
    }
}

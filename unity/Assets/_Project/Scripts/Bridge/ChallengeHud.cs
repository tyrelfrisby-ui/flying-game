using FlyingGame.Sim.Challenge;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Draws the challenge run: the current callout, a live per-tolerance in/out-of-band strip (green =
    /// on target, red = drifting), the phase countdown and running score, and a pass/fail card when the
    /// challenge completes. Subtle by design (gauges stay quiet); this is the graded overlay.
    /// </summary>
    public sealed class ChallengeHud : MonoBehaviour
    {
        public ChallengeController Controller;
        private GUIStyle _label, _big, _band;

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen) return;   // owner 2026-10-03: no HUD text over the landing page
            Init();
            // Top-centre, below FlightHud's lines; sized off the short screen edge like the rest of the GUI.
            float lh = _fs * 1.5f;
            float w = Screen.width - 2f * _fs;
            float y = _fs * 0.5f + 3f * lh;
            Rect Line(float extra = 1f) { var r = new Rect(_fs, y, w, lh * extra); y += lh * extra; return r; }

            if (Controller == null || Controller.Runner == null)
            {
                // Keyboard hint — only where there IS a keyboard (Mac / editor); on a touchscreen it's noise over the HUD.
                if (!Input.touchSupported) GUI.Label(Line(), "Press C to start a graded challenge · N for next", _label);
                return;
            }

            ChallengeRunner r = Controller.Runner;

            if (r.Complete)
            {
                var card = new GUIStyle(_big) { normal = { textColor = r.Passed ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f) } };
                GUI.Label(Line(1.4f), $"{(r.Passed ? "PASS" : "TRY AGAIN")}   {r.Score:F0}%", card);
                GUI.Label(Line(), $"{Controller.CurrentId}  ·  C to retry  ·  N for next", _label);
                return;
            }

            // Callout + timer + live score.
            string callout = r.LastCalloutKey != null ? Prettify(r.LastCalloutKey) : Controller.CurrentId;
            GUI.Label(Line(1.4f), callout, _big);
            GUI.Label(Line(), $"phase {r.PhaseIndex + 1}/{r.Def.Phases.Count}   {r.PhaseTimeLeft:F0}s   score {r.LiveScore:F0}%", _label);

            // Live tolerance strip.
            foreach (var b in r.LiveBands)
            {
                var s = new GUIStyle(_band) { normal = { textColor = b.InBand ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.55f, 0.4f) } };
                GUI.Label(Line(), $"{(b.InBand ? "●" : "○")} {Prettify(b.Signal)}", s);
            }
        }

        private int _fs;

        private void Init()
        {
            // From scratch (no GUI.skin base): the built-in skin/font is stripped on iOS and NREs.
            // OnGUI uses native pixels, so scale off min(w,h) (a fixed 15 px is microscopic on Retina).
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.024f);
            if (_label != null && fs == _fs) return;
            _fs = fs;
            _label = new GUIStyle { fontSize = fs, font = UiFont.Get(), alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1, 1, 1, 0.8f) } };
            _big = new GUIStyle { fontSize = Mathf.RoundToInt(fs * 1.4f), fontStyle = FontStyle.Bold, font = UiFont.Get(), alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            _band = new GUIStyle { fontSize = fs, font = UiFont.Get(), alignment = TextAnchor.UpperCenter };
        }

        private static string Prettify(string key)
        {
            string s = key;
            int dot = s.LastIndexOf('.');
            if (dot >= 0) s = s[(dot + 1)..];
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(sb.Length == 0 ? char.ToUpper(c) : c);
            }
            return sb.ToString().Replace("Rad", "").Replace("Ms", " speed").Trim();
        }
    }
}

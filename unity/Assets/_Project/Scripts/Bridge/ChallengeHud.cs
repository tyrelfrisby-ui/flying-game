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
            if (Controller == null || Controller.Runner == null)
            {
                Init();
                GUI.Label(new Rect(16, 84, 700, 22), "Press C to start a graded challenge · N for next", _label);
                return;
            }

            Init();
            ChallengeRunner r = Controller.Runner;

            if (r.Complete)
            {
                var card = new GUIStyle(_big) { normal = { textColor = r.Passed ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f) } };
                GUI.Label(new Rect(16, 84, 700, 40), $"{(r.Passed ? "PASS" : "TRY AGAIN")}   {r.Score:F0}%", card);
                GUI.Label(new Rect(16, 122, 700, 22), $"{Controller.CurrentId}  ·  C to retry  ·  N for next", _label);
                return;
            }

            // Callout + timer + live score.
            string callout = r.LastCalloutKey != null ? Prettify(r.LastCalloutKey) : Controller.CurrentId;
            GUI.Label(new Rect(16, 84, 760, 26), callout, _big);
            GUI.Label(new Rect(16, 112, 400, 22),
                $"phase {r.PhaseIndex + 1}/{r.Def.Phases.Count}   {r.PhaseTimeLeft:F0}s   score {r.LiveScore:F0}%", _label);

            // Live tolerance strip.
            float y = 136;
            foreach (var b in r.LiveBands)
            {
                var s = new GUIStyle(_band) { normal = { textColor = b.InBand ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.55f, 0.4f) } };
                GUI.Label(new Rect(16, y, 400, 20), $"{(b.InBand ? "●" : "○")} {Prettify(b.Signal)}", s);
                y += 20;
            }
        }

        private void Init()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(1, 1, 1, 0.8f) } };
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _band ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };
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

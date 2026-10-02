using FlyingGame.Core.Combat;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Combat symbology (owner 2026-10-01), on the main camera:
    ///  • TARGET INDICATOR — a dot on a ring round the middle of the view showing where the other aircraft is in YOUR
    ///    aircraft's frame: at the top = he is "above" you in the pilot's sense, so roll until the dot is top centre and pull.
    ///    Next to it: the angle between your nose and him, his clock position (horizontal bearing, 12 = dead ahead) and range.
    ///    The dogfight opponent, or in the combat zone the nearest drone.
    ///  • GUNSIGHT — a pipper ring where your rounds will be when they have flown to his range (300 m without a target),
    ///    from the same ballistics as the bullets (Gunnery.PredictRound), with a small flight-path marker for reference.
    ///    Green when he is inside the ring.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        public CombatController Combat;
        private Camera _cam;
        private Texture2D _ring, _disc;
        private GUIStyle _label;
        private int _fs;
        private static readonly Color Amber = new(1f, 0.6f, 0.1f, 0.95f), Hot = new(0.3f, 1f, 0.4f, 0.95f), Faint = new(1f, 1f, 1f, 0.18f);

        private void Awake() { _cam = GetComponent<Camera>(); }

        private static Texture2D MakeCircle(int size, float thickness)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, r = c - 1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = thickness <= 0 ? Mathf.Clamp01(r - d + 0.5f) : Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(d - (r - thickness * 0.5f)) + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (Combat == null || _cam == null || SessionSettings.MenuOpen || SessionSettings.ReplayActive || Event.current.type != EventType.Repaint) return;
            var me = Combat.Driver?.Sim?.Aircraft;
            if (me == null || (!Combat.Dogfight && !Combat.InZone)) return;
            _ring ??= MakeCircle(128, 7f);
            _disc ??= MakeCircle(64, 0f);
            Rect vp = _cam.pixelRect;
            float s = Mathf.Min(vp.width, vp.height);
            int fs = Mathf.RoundToInt(s * 0.028f);
            if (_label == null || fs != _fs)
            {
                _fs = fs;
                _label = new GUIStyle { font = UiFont.Get(), fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            }
            Vector2 centre = new Vector2(vp.center.x, Screen.height - vp.center.y);
            Aircraft tgt = Combat.TargetAircraft;
            double range = tgt != null ? (tgt.State.Position - me.State.Position).Length : 0;

            // ---- target indicator
            if (tgt != null)
            {
                Vec3 rel = tgt.State.Position - me.State.Position;
                Vec3 b = me.State.Attitude.Conjugate().Rotate(rel);   // body: x ahead, y right, z down
                float off = (float)(System.Math.Atan2(System.Math.Sqrt(b.Y * b.Y + b.Z * b.Z), b.X) * 180 / System.Math.PI);
                float theta = (float)System.Math.Atan2(b.Y, -b.Z);     // 0 = top (pull to him), + = to the right
                (_, _, double psi) = FormationPilot.Euler(me.State.Attitude);
                double rb = System.Math.Atan2(rel.Y, rel.X) - psi;
                while (rb > System.Math.PI) rb -= 2 * System.Math.PI; while (rb < -System.Math.PI) rb += 2 * System.Math.PI;
                int clock = ((int)System.Math.Round(rb * 6 / System.Math.PI) + 12) % 12; if (clock == 0) clock = 12;
                float R = s * 0.30f, dot = s * 0.045f;
                GUI.color = Faint;
                GUI.DrawTexture(new Rect(centre.x - R, centre.y - R, 2 * R, 2 * R), _ring);
                Vector2 p = centre + new Vector2(Mathf.Sin(theta), -Mathf.Cos(theta)) * R;
                bool lined = Mathf.Abs(theta) < 10f * Mathf.Deg2Rad || off < 3f;
                GUI.color = lined ? Hot : Amber;
                GUI.DrawTexture(new Rect(p.x - dot * 0.5f, p.y - dot * 0.5f, dot, dot), _disc);
                string rng = range < 1000 ? $"{range:F0} m" : $"{range / 1000:F1} km";
                Vector2 lp = centre + new Vector2(Mathf.Sin(theta), -Mathf.Cos(theta)) * (R + s * 0.09f);
                string txt = $"{off:F0}°  {clock} o'clock\n{rng}  {Combat.TargetName}";
                var box = new Rect(lp.x - s * 0.2f, lp.y - fs * 1.6f, s * 0.4f, fs * 3.2f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);   // dark backing: readable over dials and terrain
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = Color.white;
                _label.normal.textColor = lined ? Hot : Color.white;
                GUI.Label(box, txt, _label);
            }

            // ---- gunsight pipper + flight-path marker
            if (me.Guns != null && me.Guns.Guns.Count > 0)
            {
                double pr = tgt != null && range < 1000 ? System.Math.Max(150, range) : 300;
                (Vec3 aim, _) = Gunnery.PredictRound(me.Guns, me.Config, me.State, pr);
                Vector3 sp = _cam.WorldToScreenPoint(CoordinateMap.ToUnity(aim));
                if (sp.z > 0)
                {
                    Vector2 g = new Vector2(sp.x, Screen.height - sp.y);
                    float rr = s * 0.05f;
                    bool onTarget = false;
                    if (tgt != null)
                    {
                        Vector3 ts = _cam.WorldToScreenPoint(CoordinateMap.ToUnity(tgt.State.Position));
                        onTarget = ts.z > 0 && Vector2.Distance(new Vector2(ts.x, Screen.height - ts.y), g) < rr;
                    }
                    Color c = Combat.GunsHot ? (onTarget ? Hot : Amber) : new Color(0.7f, 0.7f, 0.7f, 0.6f);
                    GUI.color = c;
                    GUI.DrawTexture(new Rect(g.x - rr, g.y - rr, 2 * rr, 2 * rr), _ring);
                    GUI.DrawTexture(new Rect(g.x - rr * 0.12f, g.y - rr * 0.12f, rr * 0.24f, rr * 0.24f), _disc);
                    _label.normal.textColor = c;
                    GUI.Label(new Rect(g.x - rr * 2, g.y + rr, rr * 4, fs * 1.4f), $"{pr:F0} m", _label);
                }
                // Flight-path marker: where the aircraft is actually going (the pipper sits relative to it).
                Vec3 vw = me.State.Attitude.Rotate(me.State.Velocity);
                if (vw.Length > 5)
                {
                    Vector3 fp = _cam.WorldToScreenPoint(CoordinateMap.ToUnity(me.State.Position + vw * (1000 / vw.Length)));
                    if (fp.z > 0)
                    {
                        Vector2 f = new Vector2(fp.x, Screen.height - fp.y);
                        float fr = s * 0.018f;
                        GUI.color = new Color(0.3f, 1f, 0.4f, 0.85f);
                        GUI.DrawTexture(new Rect(f.x - fr, f.y - fr, 2 * fr, 2 * fr), _ring);
                        GUI.DrawTexture(new Rect(f.x - fr * 2.6f, f.y - 1.5f, fr * 1.6f, 3f), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(f.x + fr, f.y - 1.5f, fr * 1.6f, 3f), Texture2D.whiteTexture);
                    }
                }
            }
            GUI.color = Color.white;
        }
    }
}

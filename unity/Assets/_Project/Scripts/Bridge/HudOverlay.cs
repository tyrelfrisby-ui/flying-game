using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Green head-up display drawn OVER the aircraft (owner request: "look through the HUD at the
    /// airplane"). Conformal symbology, projected through the chase camera so it sits on the world:
    ///   - pitch ladder + horizon line (level with the world; the aircraft rolls against it),
    ///   - bank scale + pointer at the top,
    ///   - flight-path marker (velocity vector) and waterline (nose) symbol — the vertical gap between
    ///     them IS the angle of attack, the lateral gap IS the sideslip,
    ///   - airspeed (kt) left, altitude (ft) right, heading top, AoA / sideslip numerics under airspeed.
    /// Lines are thin DPI-scaled quads drawn with GL in OnPostRender; text is OnGUI in the same green.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class HudOverlay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public Color Green = new(0.25f, 1f, 0.35f, 0.9f);
        public float LadderHalfWidthDeg = 9f;   // half-length of ladder bars in azimuth degrees
        public float LineWidthFrac = 0.0022f;   // line width as a fraction of min(w,h)

        private Camera _cam;
        private Material _mat;
        private GUIStyle _text, _small;
        private int _fs;

        private struct Sym { public Vector2 Pos; public string Text; public TextAnchor Anchor; public bool Small; }
        private readonly System.Collections.Generic.List<Sym> _labels = new();

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        // ---- projection helpers -------------------------------------------------------------------

        /// <summary>Screen point (px, bottom-left origin) of a world DIRECTION seen from the camera; null if behind.</summary>
        private Vector2? Dir(Vector3 worldDir)
        {
            Vector3 p = _cam.WorldToScreenPoint(_cam.transform.position + worldDir.normalized * 5000f);
            if (p.z <= 0f) return null;
            return new Vector2(p.x, p.y);
        }

        private static Vector3 DirFrom(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        // ---- drawing ------------------------------------------------------------------------------

        private float _w; // line width px

        private void Line(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-4f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (_w * 0.5f);
            GL.Vertex3(a.x + n.x, a.y + n.y, 0); GL.Vertex3(b.x + n.x, b.y + n.y, 0);
            GL.Vertex3(b.x - n.x, b.y - n.y, 0); GL.Vertex3(a.x - n.x, a.y - n.y, 0);
        }

        private void Circle(Vector2 c, float r, int seg = 24)
        {
            Vector2 prev = c + new Vector2(r, 0);
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                Line(prev, p); prev = p;
            }
        }

        private void OnPostRender()
        {
            if (Driver == null || Driver.Sim == null || _mat == null || SessionSettings.MenuOpen) { _labels.Clear(); return; }
            _labels.Clear();
            float s = Mathf.Min(Screen.width, Screen.height);
            _w = Mathf.Max(1.5f, s * LineWidthFrac);
            float u = s * 0.01f; // 1 % of the short edge — symbol unit

            Vector3 vel = Driver.WorldVelocityUnity;
            Vector3 nose = Driver.transform.forward;
            Vector3 velDir = vel.magnitude > 2f ? vel.normalized : nose;
            float heading = Mathf.Atan2(velDir.x, velDir.z) * Mathf.Rad2Deg;
            if (heading < 0) heading += 360f;
            float noseHeading = Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg;

            // Bank from the aircraft's right vector (world y-up).
            Vector3 right = Driver.transform.right;
            float bank = Mathf.Atan2(-right.y, Mathf.Max(0.001f, Vector3.Cross(Vector3.up, nose).normalized.magnitude) * Vector3.Dot(right, Vector3.Cross(Vector3.up, nose).normalized)) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(nose.y, -1f, 1f)) * Mathf.Rad2Deg;

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(Green);

            // --- pitch ladder (conformal, centred on the flight-path azimuth) ---
            for (int deg = -60; deg <= 60; deg += 5)
            {
                bool major = deg % 10 == 0;
                float halfW = deg == 0 ? LadderHalfWidthDeg * 2.2f : (major ? LadderHalfWidthDeg : LadderHalfWidthDeg * 0.5f);
                Vector2? l = Dir(DirFrom(heading - halfW, deg)), r = Dir(DirFrom(heading + halfW, deg));
                Vector2? li = Dir(DirFrom(heading - halfW * 0.35f, deg)), ri = Dir(DirFrom(heading + halfW * 0.35f, deg));
                if (l == null || r == null || li == null || ri == null) continue;
                if (deg == 0)
                {
                    Line(l.Value, r.Value);
                }
                else
                {
                    // Gapped bar (open in the middle like a real ladder); negative pitch bars dashed by halves.
                    Line(l.Value, li.Value);
                    Line(ri.Value, r.Value);
                    // Short tick toward the horizon at each end.
                    Vector2? lt = Dir(DirFrom(heading - halfW, deg - Mathf.Sign(deg) * 1.2f));
                    Vector2? rt = Dir(DirFrom(heading + halfW, deg - Mathf.Sign(deg) * 1.2f));
                    if (lt != null) Line(l.Value, lt.Value);
                    if (rt != null) Line(r.Value, rt.Value);
                    if (major)
                    {
                        _labels.Add(new Sym { Pos = l.Value + new Vector2(-u * 1.2f, 0), Text = deg.ToString(), Anchor = TextAnchor.MiddleRight, Small = true });
                        _labels.Add(new Sym { Pos = r.Value + new Vector2(u * 1.2f, 0), Text = deg.ToString(), Anchor = TextAnchor.MiddleLeft, Small = true });
                    }
                }
            }

            // --- flight-path marker (velocity vector): circle with wings and a fin ---
            Vector2? fpm = Dir(velDir);
            if (fpm != null)
            {
                Vector2 c = fpm.Value;
                float r = u * 1.4f;
                Circle(c, r);
                Line(c + new Vector2(r, 0), c + new Vector2(r * 2.6f, 0));
                Line(c - new Vector2(r, 0), c - new Vector2(r * 2.6f, 0));
                Line(c + new Vector2(0, r), c + new Vector2(0, r * 2.0f));
            }

            // --- waterline (nose direction): -W- symbol ---
            Vector2? wl = Dir(nose);
            if (wl != null)
            {
                Vector2 c = wl.Value;
                float a = u * 1.3f;
                Line(c + new Vector2(-a * 3f, 0), c + new Vector2(-a, 0));
                Line(c + new Vector2(-a, 0), c + new Vector2(-a * 0.5f, -a * 0.8f));
                Line(c + new Vector2(-a * 0.5f, -a * 0.8f), c);
                Line(c, c + new Vector2(a * 0.5f, -a * 0.8f));
                Line(c + new Vector2(a * 0.5f, -a * 0.8f), c + new Vector2(a, 0));
                Line(c + new Vector2(a, 0), c + new Vector2(a * 3f, 0));
            }

            // --- bank scale at the top: arc ticks, pointer at current bank (world-level reference) ---
            {
                Vector2 centre = new(Screen.width * 0.5f, Screen.height * 0.5f);
                float rad = s * 0.36f;
                foreach (int t in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
                {
                    float ang = (90f + t) * Mathf.Deg2Rad;
                    float len = t % 30 == 0 ? u * 2.2f : u * 1.2f;
                    Vector2 o = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    Vector2 i = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (rad - len);
                    Line(i, o);
                }
                float b = (90f - bank) * Mathf.Deg2Rad;
                Vector2 tip = centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (rad - u * 2.6f);
                Vector2 bl = centre + new Vector2(Mathf.Cos(b - 0.02f), Mathf.Sin(b - 0.02f)) * (rad - u * 4.8f);
                Vector2 br = centre + new Vector2(Mathf.Cos(b + 0.02f), Mathf.Sin(b + 0.02f)) * (rad - u * 4.8f);
                Line(tip, bl); Line(bl, br); Line(br, tip);
            }

            // --- data boxes: airspeed left, altitude right (framed) ---
            {
                Vector2 centre = new(Screen.width * 0.5f, Screen.height * 0.52f);
                float bx = s * 0.30f, bw = u * 11f, bh = u * 4.2f;
                Rect asr = new(centre.x - bx - bw, centre.y - bh * 0.5f, bw, bh);
                Rect alr = new(centre.x + bx, centre.y - bh * 0.5f, bw, bh);
                Frame(asr); Frame(alr);
                double kt = Driver.IasMs * 1.9438;
                double ft = Driver.AltitudeM * 3.28084;
                _labels.Add(new Sym { Pos = new Vector2(asr.xMax - u, asr.center.y), Text = $"{kt:F0}", Anchor = TextAnchor.MiddleRight });
                _labels.Add(new Sym { Pos = new Vector2(asr.x, asr.yMax + u * 1.6f), Text = "KT", Anchor = TextAnchor.MiddleLeft, Small = true });
                _labels.Add(new Sym { Pos = new Vector2(alr.xMax - u, alr.center.y), Text = $"{ft:N0}", Anchor = TextAnchor.MiddleRight });
                _labels.Add(new Sym { Pos = new Vector2(alr.x, alr.yMax + u * 1.6f), Text = "FT", Anchor = TextAnchor.MiddleLeft, Small = true });
                // AoA / sideslip under the airspeed box; heading above the ladder.
                _labels.Add(new Sym { Pos = new Vector2(asr.xMax - u, asr.y - u * 2.2f), Text = $"α {Driver.AlphaDeg:F1}°", Anchor = TextAnchor.MiddleRight, Small = true });
                _labels.Add(new Sym { Pos = new Vector2(asr.xMax - u, asr.y - u * 4.6f), Text = $"β {Driver.BetaDeg:F1}°", Anchor = TextAnchor.MiddleRight, Small = true });
                _labels.Add(new Sym { Pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f + s * 0.40f), Text = $"{noseHeading + (noseHeading < 0 ? 360f : 0f):000}°", Anchor = TextAnchor.MiddleCenter });
                _ = pitch;
            }

            GL.End();
            GL.PopMatrix();
        }

        private void Frame(Rect r)
        {
            Line(new Vector2(r.x, r.y), new Vector2(r.xMax, r.y));
            Line(new Vector2(r.xMax, r.y), new Vector2(r.xMax, r.yMax));
            Line(new Vector2(r.xMax, r.yMax), new Vector2(r.x, r.yMax));
            Line(new Vector2(r.x, r.yMax), new Vector2(r.x, r.y));
        }

        private void OnGUI()
        {
            if (_labels.Count == 0) return;
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.030f);
            if (_text == null || fs != _fs)
            {
                _fs = fs;
                _text = new GUIStyle { font = UiFont.Get(), fontSize = fs, fontStyle = FontStyle.Bold, normal = { textColor = Green } };
                _small = new GUIStyle { font = UiFont.Get(), fontSize = Mathf.RoundToInt(fs * 0.7f), normal = { textColor = Green } };
            }
            foreach (Sym l in _labels)
            {
                GUIStyle st = l.Small ? _small : _text;
                st.alignment = l.Anchor;
                float w = fs * 8f, h = fs * 1.6f;
                float x = l.Anchor is TextAnchor.MiddleRight ? l.Pos.x - w : l.Anchor is TextAnchor.MiddleCenter ? l.Pos.x - w * 0.5f : l.Pos.x;
                GUI.Label(new Rect(x, Screen.height - l.Pos.y - h * 0.5f, w, h), l.Text, st);
            }
        }
    }
}

using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Two round, semi-transparent analog instruments floating either side of the aircraft, a little above
    /// it: an airspeed indicator (kt) on the left and an altimeter (ft, 1 000 ft per revolution with a short
    /// thousands hand) on the right. No attitude symbology — the aircraft itself is the attitude reference.
    /// Faces and needles are GL quads drawn after the scene; numerals are bold GUI labels.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class AnalogGauges : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float Alpha = 0.55f;
        public float RadiusFrac = 0.10f;        // dial radius as a fraction of min(viewport w, h)
        public float SideOffsetFrac = 0.30f;    // dial centre offset from the aircraft, sideways (fraction of min dim)
        public float UpOffsetFrac = 0.14f;      // and upward

        private Camera _cam;
        private Material _mat;
        private GUIStyle _big, _num, _label;
        private int _fs;
        private float _w;
        private Vector2 _asiCentre, _altCentre, _gCentre; private float _r, _gr;
        private float _gMaxSeen = 1f, _gMinSeen = 1f; private object _gWatched;
        private static readonly Color Limit = new(1f, 0.85f, 0.2f);
        private float _asiMaxKt = 160f;

        private static readonly Color Face = new(0.05f, 0.07f, 0.09f), Ring = new(0.9f, 0.92f, 0.95f), Needle = new(1f, 1f, 1f), Warn = new(1f, 0.3f, 0.2f);

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d) { GL.Vertex3(a.x, a.y, 0); GL.Vertex3(b.x, b.y, 0); GL.Vertex3(c.x, c.y, 0); GL.Vertex3(d.x, d.y, 0); }

        private void Line(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 1e-3f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (w * 0.5f);
            Quad(a + n, b + n, b - n, a - n);
        }

        private void Disc(Vector2 c, float r, Color col, int segs = 64)
        {
            GL.Color(col);
            for (int i = 0; i < segs; i++)
            {
                float a0 = 2f * Mathf.PI * i / segs, a1 = 2f * Mathf.PI * (i + 1) / segs;
                Vector2 p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
                Quad(c, p0, p1, c);
            }
        }

        private void Circle(Vector2 c, float r, float w, Color col, int segs = 72)
        {
            GL.Color(col);
            for (int i = 0; i < segs; i++)
            {
                float a0 = 2f * Mathf.PI * i / segs, a1 = 2f * Mathf.PI * (i + 1) / segs;
                Line(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, w);
            }
        }

        /// <summary>Dial angle (radians, screen y-up) for a fraction 0..1 of the sweep: an airspeed-style dial
        /// sweeps 300° clockwise from 7 o'clock; an altimeter sweeps a full 360° clockwise from 12.</summary>
        private static float DialAngle(float frac, bool fullCircle) => fullCircle
            ? Mathf.PI * 0.5f - frac * 2f * Mathf.PI
            : (Mathf.PI * 1.25f) - frac * (Mathf.PI * 5f / 3f);

        private void NeedleAt(Vector2 c, float ang, float len, float w, Color col)
        {
            GL.Color(col);
            Vector2 dir = new(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 tip = c + dir * len, tail = c - dir * len * 0.18f;
            Vector2 n = new Vector2(-dir.y, dir.x) * (w * 0.5f);
            Quad(tail + n, c + n * 1.4f, tip, c - n * 1.4f);
            Quad(tail + n, tail - n, c - n * 1.4f, c + n * 1.4f);
        }

        private void OnPostRender()
        {
            if (Driver == null || _mat == null || SessionSettings.MenuOpen) return;
            float vw = _cam.pixelWidth, vh = _cam.pixelHeight, s = Mathf.Min(vw, vh);
            _r = s * RadiusFrac; _w = Mathf.Max(1.5f, s * 0.003f);
            Vector3 sp = _cam.WorldToViewportPoint(Driver.transform.position);
            Vector2 ac = sp.z > 0 ? new Vector2(sp.x * vw, sp.y * vh) : new Vector2(vw * 0.5f, vh * 0.5f);
            _asiCentre = ac + new Vector2(-SideOffsetFrac * s, UpOffsetFrac * s);
            _altCentre = ac + new Vector2(SideOffsetFrac * s, UpOffsetFrac * s);
            _asiCentre.x = Mathf.Clamp(_asiCentre.x, _r * 1.1f, vw - _r * 1.1f); _altCentre.x = Mathf.Clamp(_altCentre.x, _r * 1.1f, vw - _r * 1.1f);
            _asiCentre.y = Mathf.Clamp(_asiCentre.y, _r * 1.1f, vh - _r * 1.1f); _altCentre.y = Mathf.Clamp(_altCentre.y, _r * 1.1f, vh - _r * 1.1f);
            _gr = _r * 0.72f;
            _gCentre = new Vector2(ac.x, Mathf.Clamp(ac.y + (UpOffsetFrac + RadiusFrac + 0.09f) * s, _gr * 1.1f, vh - _gr * 1.1f));

            // Airspeed scale: round the dial to the type (twice its cruise/spawn speed, to the nearest 20 kt).
            float spawnKt = (float)Driver.SpawnIasMs * 1.9438f;
            _asiMaxKt = Mathf.Max(120f, Mathf.Ceil(spawnKt * 2.2f / 20f) * 20f);
            float kt = (float)Driver.IasMs * 1.9438f;
            float ft = (float)Driver.AltitudeM * 3.28084f;

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            Color face = Face; face.a = Alpha; Color ring = Ring; ring.a = Alpha + 0.3f; Color needle = Needle; needle.a = Mathf.Min(1f, Alpha + 0.4f);

            // ---- airspeed indicator
            Disc(_asiCentre, _r, face);
            Circle(_asiCentre, _r, _w * 1.5f, ring);
            int ktStep = _asiMaxKt <= 200 ? 10 : _asiMaxKt <= 400 ? 20 : 50;
            for (int v = 0; v <= _asiMaxKt; v += ktStep)
            {
                bool major = v % (ktStep * 2) == 0;
                float a = DialAngle(v / _asiMaxKt, false);
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                GL.Color(ring);
                Line(_asiCentre + dir * _r * (major ? 0.80f : 0.87f), _asiCentre + dir * _r * 0.95f, major ? _w * 1.4f : _w);
            }
            // Stall speed arc (red) at the bottom of the scale: 0.6× spawn speed.
            float vsKt = spawnKt * 0.62f;
            for (float v = 0; v < vsKt; v += 2f)
            {
                float a0 = DialAngle(v / _asiMaxKt, false), a1 = DialAngle(Mathf.Min(vsKt, v + 2f) / _asiMaxKt, false);
                Color warn = Warn; warn.a = Alpha + 0.2f; GL.Color(warn);
                Line(_asiCentre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _r * 0.98f, _asiCentre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * _r * 0.98f, _w * 2.5f);
            }
            NeedleAt(_asiCentre, DialAngle(Mathf.Clamp01(kt / _asiMaxKt), false), _r * 0.82f, _w * 3f, needle);
            Disc(_asiCentre, _w * 3f, ring, 16);

            // ---- altimeter: 1 000 ft per revolution, short hand for thousands (10 000 ft per revolution)
            Disc(_altCentre, _r, face);
            Circle(_altCentre, _r, _w * 1.5f, ring);
            for (int i = 0; i < 50; i++)
            {
                bool major = i % 5 == 0;
                float a = DialAngle(i / 50f, true);
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                GL.Color(ring);
                Line(_altCentre + dir * _r * (major ? 0.80f : 0.88f), _altCentre + dir * _r * 0.95f, major ? _w * 1.4f : _w);
            }
            float thousands = Mathf.Repeat(ft / 10000f, 1f), hundreds = Mathf.Repeat(ft / 1000f, 1f);
            NeedleAt(_altCentre, DialAngle(thousands, true), _r * 0.50f, _w * 4.5f, needle);
            NeedleAt(_altCentre, DialAngle(hundreds, true), _r * 0.82f, _w * 2.6f, needle);
            Disc(_altCentre, _w * 3f, ring, 16);

            // ---- accelerometer (g meter): scale from ultimate negative to ultimate positive, limit marks in
            // yellow, ultimate in red, current-g needle plus max/min tell-tale hands that hold the extremes.
            var aircraft = Driver.Sim?.Aircraft;
            if (aircraft != null)
            {
                if (!ReferenceEquals(_gWatched, aircraft)) { _gWatched = aircraft; _gMaxSeen = 1f; _gMinSeen = 1f; }
                var st = aircraft.Structure;
                float gPos = (float)st.LimitPosG, gNeg = (float)st.LimitNegG, uPos = (float)st.UltimatePosG, uNeg = (float)st.UltimateNegG;
                float lo = Mathf.Floor(uNeg - 1f), hi = Mathf.Ceil(uPos + 1f);
                float g = (float)aircraft.LoadFactorZ;
                _gMaxSeen = Mathf.Max(_gMaxSeen, g); _gMinSeen = Mathf.Min(_gMinSeen, g);
                float GAng(float v) => DialAngle(Mathf.Clamp01((v - lo) / (hi - lo)), false);
                Disc(_gCentre, _gr, face);
                Circle(_gCentre, _gr, _w * 1.5f, ring);
                for (float v = lo; v <= hi + 0.01f; v += 1f)
                {
                    float a = GAng(v); Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                    GL.Color(ring); Line(_gCentre + dir * _gr * 0.80f, _gCentre + dir * _gr * 0.95f, _w * 1.3f);
                }
                void Arc(float from, float to, Color col, float rad, float width)
                {
                    Color cc = col; cc.a = Alpha + 0.3f; GL.Color(cc);
                    int n = 24;
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = GAng(from + (to - from) * i / n), a1 = GAng(from + (to - from) * (i + 1) / n);
                        Line(_gCentre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, _gCentre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rad, width);
                    }
                }
                Arc(gPos, uPos, Limit, _gr * 0.98f, _w * 2.5f); Arc(uPos, hi, Warn, _gr * 0.98f, _w * 2.5f);
                Arc(uNeg, gNeg, Limit, _gr * 0.98f, _w * 2.5f); Arc(lo, uNeg, Warn, _gr * 0.98f, _w * 2.5f);
                foreach (float mark in new[] { gPos, gNeg }) { float a = GAng(mark); Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a)); Color cc = Limit; cc.a = 1f; GL.Color(cc); Line(_gCentre + d * _gr * 0.72f, _gCentre + d * _gr * 0.98f, _w * 2.5f); }
                foreach (float mark in new[] { uPos, uNeg }) { float a = GAng(mark); Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a)); Color cc = Warn; cc.a = 1f; GL.Color(cc); Line(_gCentre + d * _gr * 0.72f, _gCentre + d * _gr * 0.98f, _w * 2.5f); }
                Color tell = ring; tell.a = Alpha + 0.15f;
                NeedleAt(_gCentre, GAng(_gMaxSeen), _gr * 0.78f, _w * 1.6f, tell);
                NeedleAt(_gCentre, GAng(_gMinSeen), _gr * 0.78f, _w * 1.6f, tell);
                NeedleAt(_gCentre, GAng(g), _gr * 0.82f, _w * 3f, needle);
                Disc(_gCentre, _w * 3f, ring, 16);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            if (Driver == null || _mat == null || SessionSettings.MenuOpen || _r <= 0f) return;
            float s = Mathf.Min(_cam.pixelWidth, _cam.pixelHeight);
            int fs = Mathf.RoundToInt(s * 0.040f);
            if (_big == null || fs != _fs)
            {
                _fs = fs;
                Font f = UiFont.Get();
                Color c = new(1f, 1f, 1f, Mathf.Min(1f, Alpha + 0.4f));
                _big = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.35f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                _num = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.62f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                _label = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.5f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
            }
            Rect vp = _cam.pixelRect;
            void Label(Vector2 p, string text, GUIStyle st, float w, float h)
            {
                float px = p.x + vp.x, py = p.y + vp.y;
                GUI.Label(new Rect(px - w * 0.5f, Screen.height - py - h * 0.5f, w, h), text, st);
            }
            float kt = (float)Driver.IasMs * 1.9438f, ft = (float)Driver.AltitudeM * 3.28084f;
            // Airspeed numerals around the dial, big digital readout in the lower half, unit label.
            int ktStep = _asiMaxKt <= 200 ? 20 : _asiMaxKt <= 400 ? 40 : 100;
            for (int v = 0; v <= _asiMaxKt; v += ktStep)
            {
                float a = DialAngle(v / _asiMaxKt, false);
                Label(_asiCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _r * 0.66f, v.ToString(), _num, fs * 3f, fs);
            }
            Label(_asiCentre + new Vector2(0, -_r * 0.42f), $"{kt:F0}", _big, fs * 5f, fs * 1.8f);
            Label(_asiCentre + new Vector2(0, _r * 0.30f), "KNOTS", _label, fs * 4f, fs);
            // Altimeter numerals 0..9, digital altitude, unit label.
            for (int i = 0; i < 10; i++)
            {
                float a = DialAngle(i / 10f, true);
                Label(_altCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _r * 0.66f, i.ToString(), _num, fs * 2f, fs);
            }
            Label(_altCentre + new Vector2(0, -_r * 0.42f), $"{ft:N0}", _big, fs * 6f, fs * 1.8f);
            Label(_altCentre + new Vector2(0, _r * 0.30f), "FEET", _label, fs * 4f, fs);
            // g meter numerals every g, current g and the limit/ultimate values.
            var aircraft = Driver.Sim?.Aircraft;
            if (aircraft != null)
            {
                var st = aircraft.Structure;
                float uPos = (float)st.UltimatePosG, uNeg = (float)st.UltimateNegG;
                float lo = Mathf.Floor(uNeg - 1f), hi = Mathf.Ceil(uPos + 1f);
                int step = hi - lo > 14 ? 2 : 1;
                for (float v = lo; v <= hi + 0.01f; v += step)
                {
                    float a = DialAngle(Mathf.Clamp01((v - lo) / (hi - lo)), false);
                    Label(_gCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _gr * 0.62f, v.ToString("0"), _num, fs * 2f, fs);
                }
                Label(_gCentre + new Vector2(0, -_gr * 0.40f), $"{aircraft.LoadFactorZ:F1} g", _big, fs * 5f, fs * 1.8f);
                Label(_gCentre + new Vector2(0, _gr * 0.28f), $"LIMIT +{st.LimitPosG:0.#}/{st.LimitNegG:0.#}  ULT +{uPos:0.#}/{uNeg:0.#}", _label, fs * 9f, fs);
            }
        }
    }
}

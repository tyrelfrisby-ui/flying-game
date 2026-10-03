using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Round, semi-transparent analog instruments floating around the aircraft: airspeed indicator (kt) left,
    /// altimeter (ft, 1 000 ft per revolution + thousands hand) right, both a little above the aircraft, and
    /// an accelerometer (g meter, limit and ultimate loads marked, max/min tell-tales) above them. No attitude
    /// symbology — the aircraft is the attitude reference. Drawn entirely in IMGUI (dial faces are generated
    /// textures, needles are rotated textures, numerals are labels) so every element shares one coordinate
    /// space in landscape and portrait alike.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class AnalogGauges : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float Alpha = 0.55f;
        public float RadiusFrac = 0.10f;        // dial radius as a fraction of min(viewport w, h)
        public float SideOffsetFrac = 0.34f;
        private Vector2 _anchor; private bool _anchorValid;    // dial centre offset from the aircraft, sideways (fraction of min dim)
        public float UpOffsetFrac = 0.14f;      // and upward

        private Camera _cam;
        private Texture2D _asiFace, _altFace, _gFace, _varioFace, _needle, _dot;
        private GUIStyle _dialCap;
        private GUIStyle _big, _num, _label;
        private int _fs, _faceSize;
        private float _asiMaxKt = 160f, _gLo, _gHi, _stallKt;
        private object _facesFor;               // the sim Aircraft the faces were generated for
        private float _gMaxSeen = 1f, _gMinSeen = 1f;

        private static readonly Color Face = new(0.05f, 0.07f, 0.09f), Ring = new(0.92f, 0.94f, 0.97f), Warn = new(1f, 0.3f, 0.2f), Limit = new(1f, 0.85f, 0.2f);

        private void Awake() { _cam = GetComponent<Camera>(); }

        /// <summary>Dial angle (degrees, clockwise from 12 o'clock) for a fraction 0..1 of the sweep: 300° sweep
        /// starting at 7 o'clock for the airspeed / g dials, a full 360° from 12 for the altimeter.</summary>
        private static float DialDeg(float frac, bool fullCircle) => fullCircle ? frac * 360f : -150f + frac * 300f;
        /// <summary>Variometer: 0 at 9 o'clock, +10 kt at 3 o'clock over the top, −10 kt at 3 o'clock under the bottom.</summary>
        private static float VarioDeg(float kt) => -90f + Mathf.Clamp(kt, -10f, 10f) * 18f;

        // ---- face generation ----------------------------------------------------------------------

        private Texture2D NewFace(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size * 0.5f, r = size * 0.48f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = d < r - 1.5f ? Alpha : d < r + 1.5f ? Mathf.Clamp01((r + 1.5f - d) / 3f) * Alpha : 0f;
                Color col = Face; col.a = a;
                if (d > r * 0.95f && d < r) { col = Ring; col.a = Alpha + 0.35f; }
                px[y * size + x] = col;
            }
            t.SetPixels32(px);
            return t;
        }

        private static void Tick(Texture2D t, float deg, float r0, float r1, float w, Color col)
        {
            int size = t.width; float c = size * 0.5f, rad = size * 0.48f;
            float a = deg * Mathf.Deg2Rad; Vector2 d = new(Mathf.Sin(a), Mathf.Cos(a)), n = new(-d.y, d.x);
            Vector2 p0 = new Vector2(c, c) + d * rad * r0, p1 = new Vector2(c, c) + d * rad * r1;
            int minX = Mathf.FloorToInt(Mathf.Min(p0.x, p1.x) - w), maxX = Mathf.CeilToInt(Mathf.Max(p0.x, p1.x) + w);
            int minY = Mathf.FloorToInt(Mathf.Min(p0.y, p1.y) - w), maxY = Mathf.CeilToInt(Mathf.Max(p0.y, p1.y) + w);
            float len = (p1 - p0).magnitude;
            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(size - 1, maxY); y++)
            for (int x = Mathf.Max(0, minX); x <= Mathf.Min(size - 1, maxX); x++)
            {
                Vector2 q = new Vector2(x + 0.5f, y + 0.5f) - p0;
                float along = Vector2.Dot(q, d), across = Mathf.Abs(Vector2.Dot(q, n));
                if (along < 0f || along > len || across > w * 0.5f) continue;
                t.SetPixel(x, y, col);
            }
        }

        private static void Arc(Texture2D t, float deg0, float deg1, float r, float w, Color col)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(deg1 - deg0) / 2f));
            for (int i = 0; i < n; i++)
            {
                float a = deg0 + (deg1 - deg0) * (i + 0.5f) / n;
                Tick(t, a, r - w * 0.5f / (t.width * 0.48f), r + w * 0.5f / (t.width * 0.48f), w * 1.2f, col);
            }
        }

        private void BuildFaces(FlyingGame.Sim.Aircraft aircraft, int size)
        {
            _faceSize = size; _facesFor = aircraft;
            float lineW = Mathf.Max(1.5f, size * 0.008f);
            Color ring = Ring; ring.a = Alpha + 0.35f;

            // Airspeed: 300° dial, scale rounded to the type (2.2× spawn speed to the nearest 20 kt), red stall arc.
            float spawnKt = (float)Driver.SpawnIasMs * 1.9438f;
            _asiMaxKt = Mathf.Max(120f, Mathf.Ceil(spawnKt * 2.2f / 20f) * 20f);
            _stallKt = spawnKt * 0.62f;
            _asiFace = NewFace(size);
            int ktStep = _asiMaxKt <= 200 ? 10 : _asiMaxKt <= 400 ? 20 : 50;
            for (int v = 0; v <= _asiMaxKt; v += ktStep)
            {
                bool major = v % (ktStep * 2) == 0;
                Tick(_asiFace, DialDeg(v / _asiMaxKt, false), major ? 0.80f : 0.87f, 0.95f, major ? lineW * 1.5f : lineW, ring);
            }
            Color warn = Warn; warn.a = Alpha + 0.3f;
            Arc(_asiFace, DialDeg(0f, false), DialDeg(Mathf.Min(1f, _stallKt / _asiMaxKt), false), 0.985f, lineW * 2.5f, warn);
            _asiFace.Apply();

            // Altimeter: 50 ticks, every 5th major.
            _altFace = NewFace(size);
            for (int i = 0; i < 50; i++) Tick(_altFace, DialDeg(i / 50f, true), i % 5 == 0 ? 0.80f : 0.88f, 0.95f, i % 5 == 0 ? lineW * 1.5f : lineW, ring);
            _altFace.Apply();

            // g meter: ultimate-negative − 1 … ultimate-positive + 1; yellow limit→ultimate arcs, red beyond.
            var st = aircraft.Structure;
            float gPos = (float)st.LimitPosG, gNeg = (float)st.LimitNegG, uPos = (float)st.UltimatePosG, uNeg = (float)st.UltimateNegG;
            _gLo = Mathf.Floor(uNeg - 1f); _gHi = Mathf.Ceil(uPos + 1f);
            _gFace = NewFace(size);
            float G(float v) => DialDeg(Mathf.Clamp01((v - _gLo) / (_gHi - _gLo)), false);
            for (float v = _gLo; v <= _gHi + 0.01f; v += 1f) Tick(_gFace, G(v), 0.80f, 0.95f, lineW * 1.4f, ring);
            Color lim = Limit; lim.a = Alpha + 0.3f;
            Arc(_gFace, G(gPos), G(uPos), 0.985f, lineW * 2.5f, lim); Arc(_gFace, G(uPos), G(_gHi), 0.985f, lineW * 2.5f, warn);
            Arc(_gFace, G(uNeg), G(gNeg), 0.985f, lineW * 2.5f, lim); Arc(_gFace, G(_gLo), G(uNeg), 0.985f, lineW * 2.5f, warn);
            Color limS = Limit; limS.a = 1f; Color warnS = Warn; warnS.a = 1f;
            Tick(_gFace, G(gPos), 0.70f, 0.98f, lineW * 2.5f, limS); Tick(_gFace, G(gNeg), 0.70f, 0.98f, lineW * 2.5f, limS);
            Tick(_gFace, G(uPos), 0.70f, 0.98f, lineW * 2.5f, warnS); Tick(_gFace, G(uNeg), 0.70f, 0.98f, lineW * 2.5f, warnS);
            _gFace.Apply();

            // Variometer (gliders): ±10 kt, zero at 9 o'clock, climb over the top, sink under the bottom.
            _varioFace = NewFace(size);
            for (int v = -10; v <= 10; v++)
            {
                bool major = v % 5 == 0;
                Tick(_varioFace, VarioDeg(v), major ? 0.80f : 0.88f, 0.95f, major ? lineW * 1.5f : lineW, ring);
            }
            Color climbCol = new Color(0.4f, 1f, 0.5f, Alpha + 0.3f), sinkCol = new Color(1f, 0.5f, 0.3f, Alpha + 0.3f);
            Arc(_varioFace, VarioDeg(0.3f), VarioDeg(10f), 0.985f, lineW * 2.2f, climbCol);
            Arc(_varioFace, VarioDeg(-10f), VarioDeg(-0.3f), 0.985f, lineW * 2.2f, sinkCol);
            _varioFace.Apply();

            // Needle: a tapered white bar in a tall texture (pivot at its centre; the bar runs up from the centre).
            if (_needle == null)
            {
                const int nw = 16, nh = 128;
                _needle = new Texture2D(nw, nh, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[nw * nh];
                for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    float up = (y - nh * 0.5f) / (nh * 0.5f);          // -1 tail .. +1 tip
                    float half = up >= 0 ? Mathf.Lerp(0.42f, 0.06f, up) : (up > -0.22f ? 0.42f : 0f);
                    bool inside = Mathf.Abs((x + 0.5f) / nw - 0.5f) < half;
                    px[y * nw + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
                _needle.SetPixels32(px); _needle.Apply();
                _dot = new Texture2D(1, 1); _dot.SetPixel(0, 0, Color.white); _dot.Apply();
            }
            _gMaxSeen = 1f; _gMinSeen = 1f;
        }

        // ---- drawing ------------------------------------------------------------------------------

        private void DrawNeedle(Vector2 c, float deg, float len, float width, float alpha)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(deg, GUI.matrix.MultiplyPoint3x4(c));   // pivot in output space (the clip pass scales GUI.matrix)
            GUI.color = new Color(1f, 1f, 1f, alpha);
            // The texture's centre sits on the pivot; the bar extends up (screen -y) by len.
            GUI.DrawTexture(new Rect(c.x - width * 0.5f, c.y - len, width, 2f * len), _needle);
            GUI.matrix = m;
        }

        private void OnGUI() => Draw(true);

        /// <summary>Clip recorder: draw the dials again into the clip image (GUI.matrix maps the screen view onto it).
        /// No state is advanced (anchor smoothing, g tell-tales) — the live pass owns that.</summary>
        public void DrawForClip() => Draw(false);

        private void Draw(bool live)
        {
            if (Driver == null || Driver.Sim == null || SessionSettings.MenuOpen || SessionSettings.Instruments != SessionSettings.InstrumentMode.Analog) return;
            if (live && UiLayout.Modal) return;   // a lesson card is up
            var aircraft = Driver.Sim.Aircraft;
            float s = Mathf.Min(_cam.pixelWidth, _cam.pixelHeight);
            float r = s * RadiusFrac, gr = r * 0.72f;
            int size = Mathf.Clamp(Mathf.RoundToInt(r * 2.2f), 128, 1024);
            if (!ReferenceEquals(_facesFor, aircraft) || _asiFace == null || Mathf.Abs(size - _faceSize) > _faceSize * 0.3f) BuildFaces(aircraft, size);

            int fs = Mathf.RoundToInt(s * 0.040f);
            if (_big == null || fs != _fs)
            {
                _fs = fs; Font f = UiFont.Get(); Color c = new(1f, 1f, 1f, Mathf.Min(1f, Alpha + 0.4f));
                _big = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.35f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                _num = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.62f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                _label = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.5f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
            }

            // Aircraft position on screen (GUI space: top-left origin). Dials either side and above, clamped on screen.
            Vector3 sp = _cam.WorldToScreenPoint(Driver.transform.position);
            Vector2 acRaw = sp.z > 0 ? new Vector2(sp.x, Screen.height - sp.y) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            // Stable anchor (owner: no jitter): the chase camera keeps the aircraft near one spot, so follow it slowly
            // (0.5 s) and snap the result to whole pixels.
            if (!_anchorValid) { _anchor = acRaw; _anchorValid = true; }
            if (live) _anchor += (acRaw - _anchor) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.5f));
            Vector2 ac = new Vector2(Mathf.Round(_anchor.x), Mathf.Round(_anchor.y));
            Rect view = _cam.pixelRect; float top = Screen.height - view.yMax, bottom = Screen.height - view.y;
            // NO-OVERLAP RULE (owner 2026-10-03): the dials live in the free region — the centre band between the pads,
            // under the toolbar + text stack, above the bottom buttons — with each readout UNDER its dial. They shrink to fit.
            float readH = fs * 1.9f;
            float regL = Mathf.Max(view.x, UiLayout.BandLeft), regR = Mathf.Min(view.xMax, UiLayout.BandRight);
            float regT = Mathf.Max(top, UiLayout.StackBottomLastFrame + UiLayout.Gap), regB = Mathf.Min(bottom, UiLayout.BottomLimit) - readH;
            if (!ChaseCamera.InCockpit)
            {
                bool gl = aircraft.Config.Propulsion == null;
                float maxR = Mathf.Min((regR - regL) / (gl ? 4.8f : 4.6f), (regB - regT) / 3.4f);
                if (maxR > 8f && r > maxR) { float k = maxR / r; r *= k; gr *= k; }
                top = regT; bottom = regB; view = new Rect(regL, Screen.height - regB, regR - regL, regB - regT);
            }
            Vector2 Clamp(Vector2 p, float rad) => new(Mathf.Clamp(p.x, view.x + rad * 1.05f, view.xMax - rad * 1.05f), Mathf.Clamp(p.y, top + rad * 1.05f, Mathf.Max(top + rad * 1.05f, bottom - rad * 1.05f)));
            // RULE (owner): no instrument covers the aircraft — each dial is pushed out of the aircraft's screen rect
            // along its own side (airspeed left, altimeter right, g meter / vario up) before being clamped on screen.
            Rect ko = ScreenLayout.AircraftKeepOut;   // bottom-left origin
            Rect koGui = ScreenLayout.HasAircraftKeepOut ? Rect.MinMaxRect(ko.xMin, Screen.height - ko.yMax, ko.xMax, Screen.height - ko.yMin) : new Rect(-1, -1, 0, 0);
            Vector2 PushOut(Vector2 c, float rad, Vector2 dir)
            {
                if (koGui.width <= 0f) return c;
                for (int i = 0; i < 40; i++)
                {
                    var circle = new Rect(c.x - rad, c.y - rad, 2f * rad, 2f * rad);
                    if (!circle.Overlaps(koGui)) break;
                    c += dir * (s * 0.02f);
                }
                return c;
            }
            // Owner: the dials must not move with bank — fixed offsets from the aircraft's screen position, wide enough
            // to clear the span (the keep-out push-out is only used for the buttons, which have room to move).
            Vector2 asi = Clamp(ac + new Vector2(-SideOffsetFrac * s, -UpOffsetFrac * s), r);
            Vector2 alt = Clamp(ac + new Vector2(SideOffsetFrac * s, -UpOffsetFrac * s), r);
            bool glider = aircraft.Config.Propulsion == null;
            float topY = ac.y - (UpOffsetFrac + RadiusFrac + 0.09f) * s;
            // Glider: g meter and variometer side by side, centred high; powered: g meter alone in the centre.
            // The two readouts under them ("1.0 g", "+1.7") must not run into each other: space the dials by the text too.
            float topSep = Mathf.Max(gr * 1.15f, _big.CalcSize(new GUIContent("-8.8 g")).x * 0.55f + fs * 0.3f);
            Vector2 gc = Clamp(new Vector2(glider ? ac.x - topSep : ac.x, topY), gr);
            Vector2 vc = Clamp(new Vector2(ac.x + topSep, topY), gr);
            // The top dials' readout + limits line must clear the side dials: lift them, and if the region is too short,
            // shrink everything a little (no-overlap rule).
            for (int it = 0; it < 4 && !ChaseCamera.InCockpit; it++)
            {
                float below = gr + fs * 2.7f, clear = Mathf.Min(asi.y, alt.y) - r - below;
                if (gc.y <= clear) break;
                float lifted = Mathf.Max(top + gr * 1.05f, clear);
                gc.y = vc.y = lifted;
                if (lifted <= clear) break;
                r *= 0.88f; gr *= 0.88f;
                asi = Clamp(ac + new Vector2(-SideOffsetFrac * s, -UpOffsetFrac * s), r);
                alt = Clamp(ac + new Vector2(SideOffsetFrac * s, -UpOffsetFrac * s), r);
            }

            // Cockpit view: the dials become an instrument panel along the bottom of the view.
            if (ChaseCamera.InCockpit)
            {
                float panelH = Mathf.Min(view.height * 0.30f, s * 0.36f), pTop = bottom - panelH;
                r = Mathf.Min(r, panelH * 0.42f); gr = r * 0.85f;
                GUI.color = new Color(0.10f, 0.11f, 0.12f, 1f);
                GUI.DrawTexture(new Rect(view.x, pTop, view.width, panelH), Texture2D.whiteTexture);
                GUI.color = new Color(0.22f, 0.23f, 0.25f, 1f);
                GUI.DrawTexture(new Rect(view.x, pTop, view.width, Mathf.Max(3f, panelH * 0.05f)), Texture2D.whiteTexture);   // glareshield edge
                GUI.color = Color.white;
                int n = glider ? 4 : 3; float step = view.width / (n + 1), cy = pTop + panelH * 0.54f;
                asi = new Vector2(view.x + step, cy);
                gc = new Vector2(view.x + step * 2f, cy);
                vc = new Vector2(view.x + step * 3f, cy);
                alt = new Vector2(view.x + step * n, cy);
            }

            float kt = (float)Driver.IasMs * 1.9438f, ft = (float)Driver.AltitudeM * 3.28084f, g = (float)aircraft.LoadFactorZ;
            if (live) { _gMaxSeen = Mathf.Max(_gMaxSeen, g); _gMinSeen = Mathf.Min(_gMinSeen, g); }
            float nAlpha = Mathf.Min(1f, Alpha + 0.4f);
            // On-dial numbers and captions scale WITH the dial: a dial shrunk to fit its region keeps its scale legible
            // instead of crowding full-size digits into a smaller face (no-overlap rule).
            float dk = Mathf.Clamp01(r / Mathf.Max(1f, s * RadiusFrac));
            _num.fontSize = Mathf.Max(6, Mathf.RoundToInt(fs * 0.62f * dk));
            _dialCap ??= new GUIStyle(_label);
            _dialCap.font = _label.font; _dialCap.normal.textColor = _label.normal.textColor;
            _dialCap.fontSize = Mathf.Max(6, Mathf.RoundToInt(fs * 0.5f * dk));
            void Label(Vector2 p, string text, GUIStyle st, float w, float h) => GUI.Label(new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h), text, st);
            Vector2 OnDial(Vector2 c, float deg, float rad) { float a = deg * Mathf.Deg2Rad; return c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rad; }

            // ---- airspeed
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(asi.x - r, asi.y - r, 2f * r, 2f * r), _asiFace);
            int ktStep = _asiMaxKt <= 200 ? 20 : _asiMaxKt <= 400 ? 40 : 100;
            for (int v = 0; v <= _asiMaxKt; v += ktStep) Label(OnDial(asi, DialDeg(v / _asiMaxKt, false), r * 0.66f), v.ToString(), _num, fs * 3f, fs);
            Label(asi + new Vector2(0, r + fs * 0.95f), $"{kt:F0}", _big, fs * 5f, fs * 1.8f);   // under the dial, clear of the scale
            Label(asi + new Vector2(0, -r * 0.30f), "KNOTS", _dialCap, fs * 4f, fs);
            DrawNeedle(asi, DialDeg(Mathf.Clamp01(kt / _asiMaxKt), false), r * 0.82f, r * 0.11f, nAlpha);

            // ---- altimeter
            GUI.DrawTexture(new Rect(alt.x - r, alt.y - r, 2f * r, 2f * r), _altFace);
            for (int i = 0; i < 10; i++) Label(OnDial(alt, DialDeg(i / 10f, true), r * 0.66f), i.ToString(), _num, fs * 2f, fs);
            Label(alt + new Vector2(0, r + fs * 0.95f), $"{ft:N0}", _big, fs * 6f, fs * 1.8f);
            Label(alt + new Vector2(0, -r * 0.30f), "FEET", _dialCap, fs * 4f, fs);
            DrawNeedle(alt, DialDeg(Mathf.Repeat(ft / 10000f, 1f), true), r * 0.50f, r * 0.16f, nAlpha);   // thousands (short, fat)
            DrawNeedle(alt, DialDeg(Mathf.Repeat(ft / 1000f, 1f), true), r * 0.82f, r * 0.10f, nAlpha);    // hundreds

            // ---- g meter
            var st = aircraft.Structure;
            GUI.DrawTexture(new Rect(gc.x - gr, gc.y - gr, 2f * gr, 2f * gr), _gFace);
            float G(float v) => DialDeg(Mathf.Clamp01((v - _gLo) / (_gHi - _gLo)), false);
            int gStep = _gHi - _gLo > 14 ? 2 : 1;
            for (float v = _gLo; v <= _gHi + 0.01f; v += gStep) Label(OnDial(gc, G(v), gr * 0.62f), v.ToString("0"), _num, fs * 2f, fs);
            Label(gc + new Vector2(0, gr + fs * 0.95f), $"{g:F1} g", _big, fs * 5f, fs * 1.8f);
            Label(gc + new Vector2(0, gr + fs * 2.05f), $"LIMIT +{st.LimitPosG:0.#}/{st.LimitNegG:0.#}  ULT +{st.UltimatePosG:0.#}/{st.UltimateNegG:0.#}", _label, fs * 9f, fs);
            DrawNeedle(gc, G(_gMaxSeen), gr * 0.78f, gr * 0.06f, Alpha + 0.15f);   // tell-tales hold the extremes
            DrawNeedle(gc, G(_gMinSeen), gr * 0.78f, gr * 0.06f, Alpha + 0.15f);
            DrawNeedle(gc, G(g), gr * 0.82f, gr * 0.12f, nAlpha);

            // ---- variometer (gliders)
            if (glider)
            {
                var st2 = aircraft.State;
                float vzKt = (float)(-st2.Attitude.Rotate(st2.Velocity).Z) * 1.9438f;   // up positive
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(vc.x - gr, vc.y - gr, 2f * gr, 2f * gr), _varioFace);
                for (int v = -5; v <= 10; v += 5) Label(OnDial(vc, VarioDeg(v), gr * 0.62f), v == 0 ? "0" : v == 10 ? "10" : (v > 0 ? "+" : "") + v, _num, fs * 2f, fs);   // ±10 share 3 o'clock: one "10"
                Label(vc + new Vector2(0, gr + fs * 0.95f), $"{vzKt:+0.0;-0.0}", _big, fs * 5f, fs * 1.8f);
                Label(vc + new Vector2(0, -gr * 0.28f), "KT", _dialCap, fs * 5f, fs);
                DrawNeedle(vc, VarioDeg(vzKt), gr * 0.82f, gr * 0.12f, nAlpha);
            }
            GUI.color = Color.white;
        }
    }
}

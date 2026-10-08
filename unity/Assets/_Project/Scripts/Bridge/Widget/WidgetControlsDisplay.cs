using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// NTSB-STYLE CONTROL DISPLAY (protocol 4, owner 2026-10-08): a flat schematic inset drawn INTO the widget's frame (so it
    /// is on Syphon + NDI): dark translucent panel, white line art, small caps labels, a value under each control —
    ///   CONTROL WHEEL (rotates with aileron, 90° at full) + column bar PUSH / PULL %; RUDDER PEDALS (the pressed one slides
    ///   forward; toe brakes light); THROTTLE (a lever per engine); FLAPS (the handle on the type's detent gate, the actual
    ///   position as a pointer when it lags); SPOILERS / SPEED BRAKE (RET · ARM · EXT); GEAR (handle + three lights).
    ///   Controls a type lacks are dimmed FIXED / N/A (never hidden — the layout doesn't jump). Header "PF: name" with the
    ///   pilot's colour chip, HANDS OFF, and for 2.5 s after a handover "YOU HAVE THE FLIGHT CONTROLS — name".
    ///   Optional 10 s time-history traces (elevator, aileron, rudder, throttle) from the review history.
    /// In review it shows the inputs of the frame being reviewed. While it shows, the scene camera renders into its own texture
    /// the size of the rest of the picture, and the frame is composed at the end of each frame: the scene in its area, the
    /// panel in its strip — the panel never covers the airplane, the vectors or the readout. (A camera rect on the
    /// multisampled frame stretched instead of clipping on Metal.)
    /// Drawn at the end of each frame straight into the frame texture with GL (line art and the font's glyph quads) — extra
    /// cameras on the same multisampled target blanked the whole frame on Metal.
    /// </summary>
    public sealed class WidgetControlsDisplay : MonoBehaviour
    {
        public AeroWidget Widget;
        private Material _mat, _copy;
        private RenderTexture _scene;
        private Rect _sceneRect;   // pixels in the frame (origin bottom-left)
        private Font _font;
        private const int GlyphPx = 48;
        private readonly List<(Vector2 at, string s, float px, Color c, TextAnchor anchor)> _texts = new();
        private bool _on;
        private const float N = AeroWidget.FrameSize;

        // Design units: a cell is CW × CH; the panel scales to fit its strip of the picture.
        private const float CW = 180f, CH = 262f, HDR = 48f, TRC = 152f, PAD = 10f;

        // Queued geometry for this frame (built in LateUpdate, drawn in the GL camera's OnPostRender).
        private readonly List<(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)> _quads = new();

        private static readonly Color Ink = new(1f, 1f, 1f, 0.95f), Dim = new(1f, 1f, 1f, 0.28f), Bg = new(0.04f, 0.05f, 0.07f, 0.78f),
            Amber = new(1f, 0.72f, 0.15f), Green = new(0.25f, 1f, 0.35f), Red = new(1f, 0.22f, 0.2f), Grey = new(1f, 1f, 1f, 0.55f),
            TElev = new(0.3f, 0.9f, 1f), TAil = new(1f, 0.9f, 0.25f), TRud = new(1f, 0.4f, 1f), TThr = new(1f, 0.6f, 0.2f);

        private void Start()
        {
            _mat = new Material(Shader.Find("FlyingGame/HudLine")) { hideFlags = HideFlags.HideAndDontSave, color = Color.white };
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var cs = Shader.Find("FlyingGame/WidgetCopy");
            if (cs != null) _copy = new Material(cs) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("[Widget] WidgetCopy shader missing: the control display can't compose");
            StartCoroutine(EndOfFrame());
        }

        private System.Collections.IEnumerator EndOfFrame()
        {
            var eof = new WaitForEndOfFrame();
            while (true) { yield return eof; if (_on) DrawInto(Widget.Frame); }
        }

        // ---- primitives (pixel space, origin bottom-left) ----
        private float _s; private Vector2 _o;   // design → pixels: scale and the content's top-left corner
        private Vector2 P(float dx, float dy) => new(_o.x + dx * _s, _o.y - dy * _s);

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) => _quads.Add((a, b, c, d, col));
        private void Rect(float x, float y, float w, float h, Color c) => Quad(P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h), c);
        private void Seg(Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a; if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * _s * 0.5f);
            Quad(a + n, b + n, b - n, a - n, c);
        }
        private void SegD(float x0, float y0, float x1, float y1, float w, Color c) => Seg(P(x0, y0), P(x1, y1), w, c);
        private void Box(float x, float y, float w, float h, float lw, Color c)
        { SegD(x, y, x + w, y, lw, c); SegD(x + w, y, x + w, y + h, lw, c); SegD(x + w, y + h, x, y + h, lw, c); SegD(x, y + h, x, y, lw, c); }
        private void Disc(float cx, float cy, float r, Color c)
        {
            Vector2 ctr = P(cx, cy);
            for (int i = 0; i < 20; i++)
            {
                float a0 = i * Mathf.PI / 10f, a1 = (i + 1) * Mathf.PI / 10f;
                Vector2 p0 = ctr + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r * _s, p1 = ctr + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r * _s;
                Quad(ctr, p0, p1, p1, c);
            }
        }
        private void Ring(float cx, float cy, float r, float lw, Color c)
        {
            for (int i = 0; i < 20; i++)
            {
                float a0 = i * Mathf.PI / 10f, a1 = (i + 1) * Mathf.PI / 10f;
                SegD(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, lw, c);
            }
        }

        private void Text(float dx, float dy, string str, float size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter) =>
            _texts.Add((P(dx, dy), str, size * _s, c, anchor));

        private void LateUpdate()
        {
            _quads.Clear(); _texts.Clear();
            var w = Widget;
            bool on = w != null && w.Ac != null && w.Show["controlsDisplay"] && _copy != null;
            _on = on;
            float size = Mathf.Clamp(w != null ? w.ControlsSize : 0.28f, 0.2f, 0.5f);
            string place = w != null ? w.ControlsPlace : "bottom";
            if (w == null || w.Cam == null) return;
            if (!on) { if (w.Cam.targetTexture != w.Frame) w.Cam.targetTexture = w.Frame; return; }
            // The scene's own texture, sized to the picture outside the panel's strip.
            _sceneRect = place == "left" ? new Rect(size * N, 0, (1 - size) * N, N) : place == "right" ? new Rect(0, 0, (1 - size) * N, N) : new Rect(0, size * N, N, (1 - size) * N);
            int sw = Mathf.RoundToInt(_sceneRect.width), sh = Mathf.RoundToInt(_sceneRect.height);
            if (_scene == null || _scene.width != sw || _scene.height != sh)
            {
                if (_scene != null) { w.Cam.targetTexture = w.Frame; _scene.Release(); Destroy(_scene); }
                _scene = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32) { name = "AeroWidgetScene", antiAliasing = 4 };
                _scene.Create();
            }
            if (w.Cam.targetTexture != _scene) w.Cam.targetTexture = _scene;
            if (_mat == null) return;

            bool traces = w.Show["controlTraces"];
            bool side = place == "left" || place == "right";
            int cols = side ? 2 : 6, rows = side ? 3 : 1;
            float W = cols * CW + 2 * PAD, H = HDR + rows * CH + (traces ? TRC : 0) + PAD;
            float rw = side ? size * N : N, rh = side ? N : size * N;
            _s = Mathf.Min(rw / W, rh / H);
            float cw = W * _s, ch = H * _s;
            float rx = place == "right" ? N - rw : 0f;
            _o = new Vector2(rx + (rw - cw) * 0.5f, side ? (N + ch) * 0.5f : ch + (rh - ch) * 0.5f);

            var f = w.Shown ?? w.Review.Capture();
            var spec = w.Controls.Spec;
            Rect(0, 0, W, H, Bg);
            Box(0, 0, W, H, 1.5f, new Color(1, 1, 1, 0.35f));
            Header(f, W);
            for (int i = 0; i < 6; i++)
            {
                float cx = PAD + (i % cols) * CW + CW * 0.5f, top = HDR + (i / cols) * CH;
                switch (i)
                {
                    case 0: Wheel(cx, top, f); break;
                    case 1: Pedals(cx, top, f); break;
                    case 2: Throttle(cx, top, f, spec); break;
                    case 3: Flaps(cx, top, f, spec); break;
                    case 4: Spoilers(cx, top, f, spec); break;
                    case 5: Gear(cx, top, f, spec); break;
                }
            }
            if (traces) Traces(PAD, HDR + rows * CH, W - 2 * PAD, TRC - 8);
        }

        private static Color Hex(string h) => !string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.white;

        private void Header(WidgetHistory.Snap f, float W)
        {
            var c = Widget.Controls;
            string name = (f.PilotName ?? c.SourceLabel ?? "").ToUpperInvariant();
            Color chip = f.PilotName != null ? Hex(f.PilotColor) : Grey;
            bool banner = !Widget.Paused && Time.unscaledTime < c.BannerUntil && c.PilotName != null;
            if (banner)
            {
                Color bc = Hex(c.PilotColor);
                Rect(4, 4, W - 8, HDR - 8, new Color(bc.r, bc.g, bc.b, 0.45f));
                Text(W * 0.5f, HDR * 0.5f, $"YOU HAVE THE FLIGHT CONTROLS — {c.PilotName.ToUpperInvariant()}", 26, Ink);
            }
            else
            {
                Rect(14, 12, 24, 24, chip);
                Text(48, HDR * 0.5f, $"PF: {name}", 26, Ink, TextAnchor.MiddleLeft);
            }
            if (f.HandsOff && !banner)
            {
                Rect(W - 160, 9, 146, 30, Amber);
                Text(W - 87, HDR * 0.5f, "HANDS OFF", 22, Color.black);
            }
            SegD(PAD, HDR, W - PAD, HDR, 1f, new Color(1, 1, 1, 0.25f));
        }

        private void Title(float cx, float top, string t, bool dim) => Text(cx, top + 20, t, 18, dim ? Dim : Grey);
        private void Value(float cx, float top, string v, bool dim, string v2 = null)
        {
            Text(cx, top + 222, v, 32, dim ? Dim : Ink);
            if (!string.IsNullOrEmpty(v2)) Text(cx, top + 250, v2, 17, dim ? Dim : Grey);
        }

        private static string Pct(double v) => $"{System.Math.Round(System.Math.Abs(v) * 100):0}%";

        private void Wheel(float cx, float top, WidgetHistory.Snap f)
        {
            Title(cx, top, "CONTROL WHEEL", false);
            float wx = cx - 14, wy = top + 118;
            float th = -(float)f.Ail * 90f * Mathf.Deg2Rad;   // + aileron = right = clockwise as the pilot sees it
            Vector2 R(float x, float y) { float c = Mathf.Cos(th), s = Mathf.Sin(th); return new Vector2(wx + x * c - y * s, wy + x * s + y * c); }
            void L(float x0, float y0, float x1, float y1) { var a = R(x0, y0); var b = R(x1, y1); SegD(a.x, a.y, b.x, b.y, 3.5f, Ink); }
            L(-56, 6, 56, 6); L(-56, 6, -56, -30); L(56, 6, 56, -30);   // ram's-horn wheel: a bar and two grips
            L(-56, -30, -44, -40); L(56, -30, 44, -40);
            L(0, 6, 0, 30);                                                // the shaft stub (top marker)
            Ring(wx, wy, 11, 3f, Ink);
            // Column: fore/aft. PUSH at the top, PULL at the bottom; elevator + = push.
            float bx = cx + 72, y0 = top + 52, y1 = top + 186, ym = (y0 + y1) * 0.5f;
            Box(bx - 5, y0, 10, y1 - y0, 1.5f, Grey);
            SegD(bx - 10, ym, bx + 10, ym, 1.5f, Grey);
            float my = ym - (float)f.Ele * (y1 - y0) * 0.5f;
            Rect(bx - 10, my - 5, 20, 10, (float)f.Ele < -0.02f ? TElev : Ink);
            Text(bx, y0 - 13, "PUSH", 15, Grey); Text(bx, y1 + 14, "PULL", 15, Grey);
            string col = System.Math.Abs(f.Ele) < 0.01 ? "NEUTRAL" : (f.Ele < 0 ? "PULL " : "PUSH ") + Pct(f.Ele);
            string ail = System.Math.Abs(f.Ail) < 0.01 ? "WHEEL LEVEL" : $"WHEEL {(f.Ail < 0 ? "L" : "R")} {Pct(f.Ail)}";
            Value(cx, top, col, false, ail);
        }

        private void Pedals(float cx, float top, WidgetHistory.Snap f)
        {
            Title(cx, top, "RUDDER PEDALS", false);
            float mid = top + 122, travel = 34f;
            SegD(cx - 70, mid + 58, cx + 70, mid + 58, 1.5f, Grey);   // the floor line
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f, px = cx + side * 38f;
                float fwd = (float)f.Rud * side * travel;                // + rudder: right pedal forward (up), left back
                float y = mid - fwd;
                bool pressed = side * f.Rud > 0.02;
                Box(px - 22, y - 34, 44, 68, 3f, pressed ? Ink : Grey);
                double br = k == 0 ? f.BrakeL : f.BrakeR;
                // Toe brake: the pedal's upper strip.
                if (br > 0.03) Rect(px - 20, y - 32, 40, 18, new Color(Red.r, Red.g, Red.b, 0.35f + 0.65f * (float)br));
                else Box(px - 18, y - 30, 36, 14, 1.2f, Grey);
                Text(px, y + 14, k == 0 ? "L" : "R", 17, pressed ? Ink : Grey);
            }
            string v = System.Math.Abs(f.Rud) < 0.01 ? "NEUTRAL" : $"{(f.Rud < 0 ? "L" : "R")} {Pct(f.Rud)}";
            string b = f.BrakeL > 0.03 || f.BrakeR > 0.03 ? $"BRAKES L {f.BrakeL * 100:0} · R {f.BrakeR * 100:0}" : "BRAKES OFF";
            Value(cx, top, v, false, b);
        }

        private void Throttle(float cx, float top, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec)
        {
            bool na = spec.Engines == 0;
            Title(cx, top, spec.Engines > 1 ? "THROTTLES" : "THROTTLE", na);
            float y0 = top + 52, y1 = top + 186;
            int n = Mathf.Clamp(spec.Engines, 1, 8);
            float span = Mathf.Min(120f, 26f * n), dx = n > 1 ? span / (n - 1) : 0f;
            Text(cx - 64, y0, "FULL", 15, na ? Dim : Grey, TextAnchor.MiddleLeft);
            Text(cx - 64, y1, "IDLE", 15, na ? Dim : Grey, TextAnchor.MiddleLeft);
            for (int i = 0; i < n; i++)
            {
                float x = cx + 14 + (n > 1 ? -span * 0.5f + i * dx : 0f);
                SegD(x, y0, x, y1, 2f, na ? Dim : Grey);
                float y = y1 - (na ? 0f : (float)f.Thr) * (y1 - y0);
                float kw = n > 4 ? 12f : 22f;
                Rect(x - kw * 0.5f, y - 7, kw, 14, na ? Dim : Ink);
            }
            Value(cx, top, na ? "N/A" : Pct(f.Thr), na, na ? "NO ENGINE" : n > 1 ? $"{n} ENGINES" : null);
        }

        private float GateY(float top, double frac) => top + 56 + (float)frac * 126f;

        private void Flaps(float cx, float top, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec)
        {
            bool na = spec.Flaps.Length < 2;
            Title(cx, top, "FLAPS", na);
            float gx = cx + 4;
            SegD(gx, GateY(top, 0), gx, GateY(top, 1), 2.5f, na ? Dim : Grey);
            if (na)
            {
                Rect(gx - 17, GateY(top, 0) - 6, 34, 12, Dim);
                Value(cx, top, "N/A", true); return;
            }
            double full = spec.Flaps[^1];
            float labelSize = spec.Flaps.Length > 6 ? 13f : 15f;
            // Like the real quadrant: the detents are evenly spaced along the gate, whatever their degrees.
            double G(double frac)
            {
                double deg = frac * full; var d = spec.Flaps;
                for (int i = 1; i < d.Length; i++)
                    if (deg <= d[i] + 1e-9) return (i - 1 + (deg - d[i - 1]) / System.Math.Max(1e-9, d[i] - d[i - 1])) / (d.Length - 1);
                return 1;
            }
            foreach (double d in spec.Flaps)
            {
                float y = GateY(top, G(d / full));
                SegD(gx - 9, y, gx + 9, y, 1.5f, Grey);
                Text(gx - 23, y, d < 1e-3 ? "UP" : $"{d:0}", labelSize, Grey, TextAnchor.MiddleRight);
            }
            float hy = GateY(top, G(f.FlapsCmd));
            Rect(gx - 17, hy - 6, 34, 12, Ink);
            // The actual flap position: a pointer on the right of the gate (shown whenever it differs from the handle).
            float ay = GateY(top, G(f.FlapsActual));
            bool lag = System.Math.Abs(f.FlapsActual - f.FlapsCmd) > 0.004;
            Color pc = lag ? Amber : Grey;
            SegD(gx + 22, ay, gx + 34, ay - 7, 2.5f, pc); SegD(gx + 22, ay, gx + 34, ay + 7, 2.5f, pc); SegD(gx + 34, ay - 7, gx + 34, ay + 7, 2.5f, pc);
            Text(gx + 38, ay, "ACT", 12, pc, TextAnchor.MiddleLeft);
            Value(cx, top, spec.Label(f.FlapsCmd), false, lag ? $"ACTUAL {f.FlapsActual * full:0}°" : $"{f.FlapsActual * full:0}°");
        }

        private void Spoilers(float cx, float top, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec)
        {
            bool na = !spec.Spoilers;
            bool jet = spec.Engines > 0;
            Title(cx, top, jet && !na ? "SPEED BRAKE" : "SPOILERS", na);
            float gx = cx + 10, yRet = top + 56, yArm = top + 80, y0 = top + 104, y1 = top + 182;
            Color g = na ? Dim : Grey;
            SegD(gx, yRet, gx, y1, 2.5f, g);
            SegD(gx - 9, yRet, gx + 9, yRet, 1.5f, g); Text(gx - 23, yRet, "RET", 15, g, TextAnchor.MiddleRight);
            if (jet) { SegD(gx - 9, yArm, gx + 9, yArm, 1.5f, g); Text(gx - 23, yArm, "ARM", 15, g, TextAnchor.MiddleRight); }
            SegD(gx - 9, y1, gx + 9, y1, 1.5f, g); Text(gx - 23, y1, "EXT", 15, g, TextAnchor.MiddleRight);
            float hy = na ? yRet : f.SpoilersArmed && f.Spoilers < 0.01 ? yArm : f.Spoilers < 0.01 ? yRet : y0 + (float)f.Spoilers * (y1 - y0);
            Rect(gx - 17, hy - 6, 34, 12, na ? Dim : f.Spoilers > 0.01 ? Amber : f.SpoilersArmed ? Green : Ink);
            string v = na ? "N/A" : f.Spoilers > 0.99 ? "EXT" : f.Spoilers > 0.01 ? $"EXT {Pct(f.Spoilers)}" : f.SpoilersArmed ? "ARMED" : "RET";
            Value(cx, top, v, na);
        }

        private void Gear(float cx, float top, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec)
        {
            bool fixedGear = !spec.Retractable;
            Title(cx, top, "GEAR", fixedGear);
            float gx = cx, yUp = top + 58, yDn = top + 140;
            Color g = fixedGear ? Dim : Grey;
            SegD(gx, yUp, gx, yDn, 2.5f, g);
            Text(gx - 24, yUp, "UP", 15, g, TextAnchor.MiddleRight); Text(gx - 24, yDn, "DN", 15, g, TextAnchor.MiddleRight);
            bool down = fixedGear || f.Gear == "down";
            float hy = down ? yDn : yUp;
            Ring(gx, hy, 12, 4f, fixedGear ? Dim : Ink);   // the wheel-shaped knob
            Disc(gx, hy, 4, fixedGear ? Dim : Ink);
            string lights = fixedGear ? "down" : f.GearLights;
            for (int i = -1; i <= 1; i++)
            {
                float lx = cx + i * 32, ly = top + 180;
                if (fixedGear) Ring(lx, ly, 10, 1.5f, Dim);
                else if (lights == "down") Disc(lx, ly, 10, Green);
                else if (lights == "transit") Disc(lx, ly, 10, Red);
                else Ring(lx, ly, 10, 1.5f, Grey);
            }
            string v = fixedGear ? "FIXED" : lights == "transit" ? "TRANSIT" : lights == "down" ? "DOWN" : "UP";
            Value(cx, top, v, fixedGear, fixedGear ? null : $"HANDLE {(f.Gear == "down" ? "DN" : "UP")}");
        }

        /// <summary>The last 10 s of the pilot's inputs (from the review history, ending at the frame shown).</summary>
        private void Traces(float x, float top, float w, float h)
        {
            var rv = Widget.Review; var hist = rv.History;
            int start = Widget.Paused ? rv.Index : 0;
            int n = Mathf.Min(WidgetHistory.Hz * 10, hist.Count - start);
            float lab = 118f, px0 = x + lab, pw = w - lab - 6f, rowH = (h - 20f) / 4f;
            string[] names = { "ELEV ▲PULL", "AILERON ▲R", "RUDDER ▲R", "THROTTLE" };
            Color[] cols = { TElev, TAil, TRud, TThr };
            for (int r = 0; r < 4; r++)
            {
                float cy = top + 6 + rowH * (r + 0.5f);
                Text(x + 4, cy, names[r], 17, cols[r], TextAnchor.MiddleLeft);
                if (r < 3) SegD(px0, cy, px0 + pw, cy, 1f, new Color(1, 1, 1, 0.22f));
                else SegD(px0, cy + rowH * 0.42f, px0 + pw, cy + rowH * 0.42f, 1f, new Color(1, 1, 1, 0.22f));
                Vector2? prev = null;
                for (int k = 0; k < n; k++)
                {
                    var s = hist.Get(start + k);
                    double v = r == 0 ? -s.Ele : r == 1 ? s.Ail : r == 2 ? s.Rud : s.Thr * 2 - 1;
                    float xx = px0 + pw * (1f - k / (float)(WidgetHistory.Hz * 10)), yy = cy - (float)v * rowH * 0.42f;
                    var p = new Vector2(xx, yy);
                    if (prev.HasValue) SegD(prev.Value.x, prev.Value.y, p.x, p.y, 2f, cols[r]);
                    prev = p;
                }
            }
            float ay = top + h - 4;
            Text(px0, ay, "-10 s", 15, Grey, TextAnchor.MiddleLeft);
            Text(px0 + pw, ay, Widget.Paused ? $"{rv.OffsetMs / 1000.0:0.0} s" : "NOW", 15, Grey, TextAnchor.MiddleRight);
        }

        /// <summary>The panel into the frame: the line art, then the text as the font's own glyph quads.</summary>
        private void DrawInto(RenderTexture rt)
        {
            if (rt == null || _mat == null || _scene == null) return;
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, N, 0, N);
            // The scene, exactly, in its area.
            _copy.mainTexture = _scene; _copy.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0, 0); GL.Vertex3(_sceneRect.xMin, _sceneRect.yMin, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(_sceneRect.xMax, _sceneRect.yMin, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(_sceneRect.xMax, _sceneRect.yMax, 0);
            GL.TexCoord2(0, 1); GL.Vertex3(_sceneRect.xMin, _sceneRect.yMax, 0);
            GL.End();
            _mat.SetPass(0);
            GL.Begin(GL.QUADS);
            foreach (var q in _quads) { GL.Color(q.col); GL.Vertex3(q.a.x, q.a.y, 0); GL.Vertex3(q.b.x, q.b.y, 0); GL.Vertex3(q.c.x, q.c.y, 0); GL.Vertex3(q.d.x, q.d.y, 0); }
            GL.End();
            // Glyphs: request every string first (the atlas may rebuild), then read the UVs.
            foreach (var t in _texts) _font.RequestCharactersInTexture(t.s, GlyphPx, FontStyle.Normal);
            _font.material.SetPass(0);
            GL.Begin(GL.QUADS);
            foreach (var t in _texts)
            {
                float k = t.px / GlyphPx, width = 0f;
                foreach (char ch in t.s) if (_font.GetCharacterInfo(ch, out var ci, GlyphPx)) width += ci.advance * k;
                float x = t.anchor is TextAnchor.MiddleLeft or TextAnchor.UpperLeft or TextAnchor.LowerLeft ? t.at.x
                        : t.anchor is TextAnchor.MiddleRight or TextAnchor.UpperRight or TextAnchor.LowerRight ? t.at.x - width : t.at.x - width * 0.5f;
                float baseline = t.at.y - t.px * 0.36f;   // vertically centred on the point (cap height ≈ 0.72 em)
                GL.Color(t.c);
                foreach (char ch in t.s)
                {
                    if (!_font.GetCharacterInfo(ch, out var ci, GlyphPx)) continue;
                    float x0 = x + ci.minX * k, x1 = x + ci.maxX * k, y0 = baseline + ci.minY * k, y1 = baseline + ci.maxY * k;
                    GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x0, y0, 0);
                    GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x1, y0, 0);
                    GL.TexCoord(ci.uvTopRight); GL.Vertex3(x1, y1, 0);
                    GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x0, y1, 0);
                    x += ci.advance * k;
                }
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = was;
        }
    }
}

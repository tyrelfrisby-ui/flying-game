using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// NTSB ANIMATION LAYOUT (protocol 6, owner 2026-10-09; the Colgan 3407 reconstruction style, keyed over the show on a
    /// TRANSPARENT background): the airplane above, the cockpit's instruments and control positions below, the warnings
    /// lighting up. Nothing fills the frame — each instrument is its own small plate (black at ~55 %, a thin grey rule), the
    /// attitude indicator's sky and ground only inside its face, the text outlined instead of boxed. No clock, no CVR.
    ///   PFD (attitude, airspeed tape with the type's stall speed as a red band for the current flaps, altitude tape with a
    ///   vertical-speed pointer, heading) · AoA gauge (critical α red, warning amber) · control column from the side, the
    ///   wheel from the front, rudder pedals · power · flaps / gear / spoilers · annunciators (STALL / STICK SHAKER, SPIN,
    ///   HOLD, REVIEW).
    /// Everything reads the frame being shown, so review replays the whole panel.
    /// </summary>
    public sealed partial class WidgetControlsDisplay
    {
        // Design space: the panel is 1080 × 486 units (the 0.45 split at 1080²), scaled to its strip.
        private const float NW = 1080f, NH = 486f;
        private static readonly Color PlateBg = new(0f, 0f, 0f, 0.55f), Rule = new(0.85f, 0.87f, 0.9f, 0.55f),
            Sky = new(0.20f, 0.45f, 0.78f, 0.95f), Ground = new(0.50f, 0.33f, 0.18f, 0.95f), LitRed = new(0.85f, 0.1f, 0.08f, 0.95f),
            LitAmber = new(0.95f, 0.65f, 0.1f, 0.95f), LitGreen = new(0.15f, 0.7f, 0.3f, 0.95f), OffText = new(1f, 1f, 1f, 0.32f);

        private void NtsbLayout(AeroWidget w, float split)
        {
            float ph = split * N;                                   // the panel strip's height in pixels
            _s = Mathf.Min(N / NW, ph / NH);
            _o = new Vector2((N - NW * _s) * 0.5f, ph - (ph - NH * _s) * 0.5f);   // centred in the strip (top-left)
            var f = w.Shown ?? w.Review.Capture();
            var r = w.Read();
            var spec = w.Controls.Spec;
            double critA = w.CriticalAlphaDeg(f.FlapsActual), warnA = critA - (spec.StickShaker ? 2.0 : 3.0);
            bool stall = r.AlphaDeg >= warnA && !r.OnGround;
            bool spin = !r.OnGround && System.Math.Abs(r.YawRateDps) > 40 && r.AlphaDeg > critA;

            Pfd(8, 8, 420, 424, r, f, w.StallKias(f.FlapsActual));
            Aoa(436, 8, 100, 424, r.AlphaDeg, critA, warnA);
            Column(544, 8, 204, 424, f);
            // Touch layout (addendum): the yoke trackpad over the column + wheel, the pedal slider, the levers.
            TouchBox(552, 36, 188, 144); LayoutRect("yoke", 552, 36, 188, 144);
            TouchBox(552, 226, 188, 100); LayoutRect("pedals", 552, 226, 188, 100);
            // Thumbs at the current inputs: the yoke (aileron →, push ↑) and the pedals.
            { float cx = 552 + 94, cy = 36 + 72; float tx = cx + (float)f.Ail * 94f, ty = cy - (float)f.Ele * 72f; Ring(tx, ty, 9, 2.5f, new Color(1, 1, 1, 0.8f)); Disc(tx, ty, 3, Color.white); }
            { float cx = 552 + 94, ty = 226 + 92; float tx = cx + (float)f.Rud * 94f; Rect(tx - 10, ty - 5, 20, 10, new Color(1, 1, 1, 0.8f)); SegD(560, ty, 732, ty, 1f, new Color(1, 1, 1, 0.25f)); }
            Power(756, 8, 96, 424, f, spec, w.Ac);
            if (spec.Engines > 0) LayoutRect("throttle", 756 + 36, 8 + 54, 52, 424 - 90 - 54);   // exactly the lever's travel: top = full
            Levers(860, 8, 212, 424, f, spec);
            if (spec.Flaps.Length > 1) LayoutRect("flaps", 860 + 46 - 26, 8 + 54, 52, 196);      // the gate: top = up
            if (spec.Spoilers) LayoutRect("spoilers", 860 + 178 - 26, 8 + 70, 52, 160);      // top = retracted
            FlapDetentPositions = spec.Flaps.Length > 1 ? FlapDetents(spec) : null;
            Annunciators(8, 440, 1064, 38, stall, spec.StickShaker, spin, w);
        }

        /// <summary>The flap detents' positions along the flaps rect (0 = top … 1 = bottom), evenly spaced like the gate.</summary>
        public double[] FlapDetentPositions { get; private set; }
        private static double[] FlapDetents(WidgetAircraftControls.Spec spec)
        {
            // The rect is exactly the gate (g0 … g1), and the detents are evenly spaced along it.
            var r = new double[spec.Flaps.Length];
            for (int i = 0; i < r.Length; i++) r[i] = System.Math.Round((double)i / (r.Length - 1), 3);
            return r;
        }
        /// <summary>A faint dashed box: where the presenter can drag (Glass Overlay lays its touch control exactly here).</summary>
        private void TouchBox(float x, float y, float w, float h)
        {
            var c = new Color(1, 1, 1, 0.22f);
            for (float t = 0; t < w; t += 12) { SegD(x + t, y, x + Mathf.Min(w, t + 6), y, 1.2f, c); SegD(x + t, y + h, x + Mathf.Min(w, t + 6), y + h, 1.2f, c); }
            for (float t = 0; t < h; t += 12) { SegD(x, y + t, x, y + Mathf.Min(h, t + 6), 1.2f, c); SegD(x + w, y + t, x + w, y + Mathf.Min(h, t + 6), 1.2f, c); }
        }

        // ---- plates and helpers ----
        private void Plate(float x, float y, float w, float h)
        {
            RoundRect(x, y, w, h, 8, PlateBg);
            RoundRule(x, y, w, h, 8, 1.2f, Rule);
        }
        private void RoundRect(float x, float y, float w, float h, float r, Color c)
        {
            Rect(x + r, y, w - 2 * r, h, c); Rect(x, y + r, r, h - 2 * r, c); Rect(x + w - r, y + r, r, h - 2 * r, c);
            Corner(x + r, y + r, r, 180, c); Corner(x + w - r, y + r, r, 270, c); Corner(x + w - r, y + h - r, r, 0, c); Corner(x + r, y + h - r, r, 90, c);
        }
        private void Corner(float cx, float cy, float r, float startDeg, Color c)
        {
            // a quarter disc; design y grows downward, so angles are measured with y flipped
            Vector2 ctr = P(cx, cy);
            for (int i = 0; i < 6; i++)
            {
                float a0 = (startDeg + i * 15f) * Mathf.Deg2Rad, a1 = (startDeg + (i + 1) * 15f) * Mathf.Deg2Rad;
                Vector2 p0 = P(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r), p1 = P(cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r);
                Quad(ctr, p0, p1, p1, c);
            }
        }
        private void RoundRule(float x, float y, float w, float h, float r, float lw, Color c)
        {
            SegD(x + r, y, x + w - r, y, lw, c); SegD(x + r, y + h, x + w - r, y + h, lw, c);
            SegD(x, y + r, x, y + h - r, lw, c); SegD(x + w, y + r, x + w, y + h - r, lw, c);
            void Arc(float cx, float cy, float a0)
            {
                for (int i = 0; i < 6; i++)
                {
                    float b0 = (a0 + i * 15f) * Mathf.Deg2Rad, b1 = (a0 + (i + 1) * 15f) * Mathf.Deg2Rad;
                    SegD(cx + Mathf.Cos(b0) * r, cy + Mathf.Sin(b0) * r, cx + Mathf.Cos(b1) * r, cy + Mathf.Sin(b1) * r, lw, c);
                }
            }
            Arc(x + r, y + r, 180); Arc(x + w - r, y + r, 270); Arc(x + w - r, y + h - r, 0); Arc(x + r, y + h - r, 90);
        }
        private void OText(float x, float y, string s, float size, Color c, TextAnchor a = TextAnchor.MiddleCenter) =>
            _texts.Add((P(x, y), s, Mathf.Max(size, 14f) * _s, c, a, true));

        /// <summary>Fill a convex polygon (design coordinates) clipped to a design rect.</summary>
        private void FillClipped(List<Vector2> poly, Rect clip, Color c)
        {
            var pts = ClipPoly(poly, clip);
            if (pts.Count < 3) return;
            Vector2 a = P(pts[0].x, pts[0].y);
            for (int i = 1; i + 1 < pts.Count; i++) { Vector2 b = P(pts[i].x, pts[i].y), d = P(pts[i + 1].x, pts[i + 1].y); Quad(a, b, d, d, c); }
        }
        private static List<Vector2> ClipPoly(List<Vector2> poly, Rect r)
        {
            List<Vector2> Clip(List<Vector2> input, System.Func<Vector2, bool> inside, System.Func<Vector2, Vector2, Vector2> cross)
            {
                var o = new List<Vector2>();
                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 cur = input[i], prev = input[(i + input.Count - 1) % input.Count];
                    bool ci = inside(cur), pi = inside(prev);
                    if (ci) { if (!pi) o.Add(cross(prev, cur)); o.Add(cur); }
                    else if (pi) o.Add(cross(prev, cur));
                }
                return o;
            }
            Vector2 X(Vector2 a, Vector2 b, float x) { float t = (x - a.x) / (b.x - a.x); return new Vector2(x, a.y + (b.y - a.y) * t); }
            Vector2 Y(Vector2 a, Vector2 b, float y) { float t = (y - a.y) / (b.y - a.y); return new Vector2(a.x + (b.x - a.x) * t, y); }
            var p = Clip(poly, v => v.x >= r.xMin, (a, b) => X(a, b, r.xMin));
            if (p.Count > 0) p = Clip(p, v => v.x <= r.xMax, (a, b) => X(a, b, r.xMax));
            if (p.Count > 0) p = Clip(p, v => v.y >= r.yMin, (a, b) => Y(a, b, r.yMin));
            if (p.Count > 0) p = Clip(p, v => v.y <= r.yMax, (a, b) => Y(a, b, r.yMax));
            return p;
        }
        /// <summary>A segment clipped to a design rect (Liang–Barsky).</summary>
        private void SegClipped(Vector2 a, Vector2 b, Rect r, float lw, Color c)
        {
            float t0 = 0, t1 = 1; Vector2 d = b - a;
            bool Edge(float p, float q) { if (Mathf.Abs(p) < 1e-6f) return q >= 0; float t = q / p; if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; } else { if (t < t0) return false; if (t < t1) t1 = t; } return true; }
            if (Edge(-d.x, a.x - r.xMin) && Edge(d.x, r.xMax - a.x) && Edge(-d.y, a.y - r.yMin) && Edge(d.y, r.yMax - a.y))
                SegD(a.x + d.x * t0, a.y + d.y * t0, a.x + d.x * t1, a.y + d.y * t1, lw, c);
        }

        // ---- a. PRIMARY FLIGHT DISPLAY ----
        private void Pfd(float x, float y, float w, float h, AeroWidget.Readout r, WidgetHistory.Snap f, double vsKias)
        {
            Plate(x, y, w, h);
            // Attitude indicator, centred.
            var face = new Rect(x + 92, y + 30, 236, 286);
            float cx = face.center.x, cy = face.center.y, ppd = 5.2f;   // pixels per degree of pitch
            float roll = (float)r.RollDeg * Mathf.Deg2Rad, pitch = (float)r.PitchDeg;
            // The horizon: a line through the centre offset by pitch, rotated by -roll (the world tilts the other way).
            // Bank right: the world rolls the other way, so the horizon's right end rises (design y grows downward).
            Vector2 dir = new(Mathf.Cos(roll), -Mathf.Sin(roll)), nrm = new(-dir.y, dir.x);   // nrm points to the ground (down when level, up when inverted)
            Vector2 h0 = new Vector2(cx, cy) + nrm * (pitch * ppd);   // pitch up → the horizon moves down the face
            float big = 2000f;
            var skyPoly = new List<Vector2> { h0 - dir * big, h0 + dir * big, h0 + dir * big - nrm * big, h0 - dir * big - nrm * big };
            var gndPoly = new List<Vector2> { h0 - dir * big, h0 + dir * big, h0 + dir * big + nrm * big, h0 - dir * big + nrm * big };
            FillClipped(skyPoly, face, Sky); FillClipped(gndPoly, face, Ground);
            SegClipped(h0 - dir * big, h0 + dir * big, face, 2f, Color.white);
            // Pitch ladder every 5° (labels every 10°).
            for (int deg = -30; deg <= 30; deg += 5)
            {
                if (deg == 0) continue;
                Vector2 c0 = h0 - nrm * (deg * ppd);
                float half = deg % 10 == 0 ? 34f : 16f;
                SegClipped(c0 - dir * half, c0 + dir * half, face, 1.5f, Color.white);
                if (deg % 10 == 0)
                {
                    Vector2 lp = c0 + dir * (half + 14);
                    if (face.Contains(lp)) OText(lp.x, lp.y, $"{System.Math.Abs(deg)}", 14, Color.white);
                }
            }
            // The fixed airplane symbol.
            SegD(cx - 60, cy, cx - 22, cy, 4f, LitAmber); SegD(cx + 22, cy, cx + 60, cy, 4f, LitAmber);
            SegD(cx - 22, cy, cx - 22, cy + 10, 4f, LitAmber); SegD(cx + 22, cy, cx + 22, cy + 10, 4f, LitAmber);
            Rect(cx - 3, cy - 3, 6, 6, LitAmber);
            // Bank scale (an arc above the face centre) with a pointer.
            float br = 110f;
            foreach (int b in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
            {
                float a = (-90f + b) * Mathf.Deg2Rad, len = b % 30 == 0 ? 12f : 7f;
                var p0 = new Vector2(cx + Mathf.Cos(a) * br, cy + Mathf.Sin(a) * br); var p1 = new Vector2(cx + Mathf.Cos(a) * (br + len), cy + Mathf.Sin(a) * (br + len));
                SegClipped(p0, p1, face, 1.5f, Color.white);
            }
            {
                float a = (-90f - (float)r.RollDeg) * Mathf.Deg2Rad;
                var tip = new Vector2(cx + Mathf.Cos(a) * (br - 2), cy + Mathf.Sin(a) * (br - 2));
                var t2 = new Vector2(cx + Mathf.Cos(a) * (br - 14), cy + Mathf.Sin(a) * (br - 14));
                Vector2 side = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * 6f;
                SegClipped(tip, t2 + side, face, 2f, Color.white); SegClipped(tip, t2 - side, face, 2f, Color.white); SegClipped(t2 + side, t2 - side, face, 2f, Color.white);
            }
            RoundRule(face.x, face.y, face.width, face.height, 4, 1.2f, Rule);

            // Airspeed tape (left): ±30 kt, the red low-speed band below the stall speed for these flaps.
            var tape = new Rect(x + 12, y + 30, 70, 286);
            Box(tape.x, tape.y, tape.width, tape.height, 1.2f, Rule);
            float kias = (float)r.Kias, kpp = tape.height / 70f;   // 70 kt over the tape
            float Ys(float v) => tape.center.y - (v - kias) * kpp;
            float redTop = Mathf.Clamp(Ys((float)vsKias), tape.yMin, tape.yMax);
            if (redTop < tape.yMax) Rect(tape.xMax - 9, redTop, 7, tape.yMax - redTop, LitRed);
            for (int v = Mathf.FloorToInt((kias - 36) / 5) * 5; v <= kias + 36; v += 5)
            {
                if (v < 0) continue;
                float yy = Ys(v); if (yy < tape.yMin + 2 || yy > tape.yMax - 2) continue;
                SegD(tape.xMax - 12, yy, tape.xMax - 2, yy, 1.2f, Color.white);
                if (v % 10 == 0 && Mathf.Abs(yy - tape.center.y) > 16) OText(tape.xMax - 16, yy, $"{v}", 15, Color.white, TextAnchor.MiddleRight);
            }
            Rect(tape.x + 2, tape.center.y - 15, tape.width - 4, 30, Color.black);
            Box(tape.x + 2, tape.center.y - 15, tape.width - 4, 30, 1.6f, Color.white);
            OText(tape.center.x, tape.center.y, $"{System.Math.Max(0, r.Kias):0}", 20, kias < vsKias ? LitRed : Color.white);
            OText(tape.center.x, tape.y - 12, "KIAS", 14, Grey);
            OText(tape.center.x, tape.yMax + 14, $"STALL {vsKias:0}", 14, LitRed);

            // Altitude tape (right): ±300 ft, plus a vertical-speed pointer beside it.
            var at = new Rect(x + 334, y + 30, 64, 286);
            Box(at.x, at.y, at.width, at.height, 1.2f, Rule);
            float alt = (float)r.HeightFt, fpp = at.height / 700f;
            float Ya(float v) => at.center.y - (v - alt) * fpp;
            for (int v = Mathf.FloorToInt((alt - 360) / 20) * 20; v <= alt + 360; v += 20)
            {
                float yy = Ya(v); if (yy < at.yMin + 2 || yy > at.yMax - 2) continue;
                SegD(at.x + 2, yy, at.x + (v % 100 == 0 ? 14 : 8), yy, 1.2f, Color.white);
                if (v % 100 == 0 && Mathf.Abs(yy - at.center.y) > 16) OText(at.x + 18, yy, $"{v}", 14, Color.white, TextAnchor.MiddleLeft);
            }
            Rect(at.x + 2, at.center.y - 15, at.width - 4, 30, Color.black);
            Box(at.x + 2, at.center.y - 15, at.width - 4, 30, 1.6f, Color.white);
            OText(at.center.x, at.center.y, $"{r.HeightFt:0}", 17, Color.white);
            OText(at.center.x, at.y - 12, "ALT FT", 14, Grey);
            // VS scale: ±2,000 fpm along the tape's right edge.
            float vsx = at.xMax + 8, vs = -(float)r.SinkFpm;
            SegD(vsx, at.y + 20, vsx, at.yMax - 20, 1.2f, Rule);
            foreach (int t in new[] { -2000, -1000, 0, 1000, 2000 }) { float yy = at.center.y - t / 2000f * (at.height * 0.5f - 20); SegD(vsx - 3, yy, vsx + 3, yy, 1.2f, Color.white); }
            float vy = at.center.y - Mathf.Clamp(vs, -2000, 2000) / 2000f * (at.height * 0.5f - 20);
            SegD(vsx - 8, vy, vsx + 6, vy, 3f, LitGreen);
            OText(at.center.x, at.yMax + 14, $"VS {vs:+0;-0;0}", 14, Color.white);

            // Heading below.
            OText(cx, y + 352, $"HDG {r.HeadingDeg:000}°", 18, Color.white);
            OText(cx, y + 378, $"PITCH {r.PitchDeg:+0;-0;0}°  BANK {System.Math.Abs(r.RollDeg):0}° {(r.RollDeg < -0.5 ? "L" : r.RollDeg > 0.5 ? "R" : "")}", 15, Grey);
            OText(cx, y + 402, $"G {r.LoadFactor:0.0}", 15, Grey);
        }

        // ---- b. ANGLE OF ATTACK ----
        private void Aoa(float x, float y, float w, float h, double alpha, double crit, double warn)
        {
            Plate(x, y, w, h);
            OText(x + w * 0.5f, y + 18, "AOA", 15, Grey);
            float top = y + 44, bot = y + h - 70, gx = x + 40;
            const float aMin = -5f, aMax = 35f;
            float Ya(double a) => bot - (Mathf.Clamp((float)a, aMin, aMax) - aMin) / (aMax - aMin) * (bot - top);
            SegD(gx, top, gx, bot, 2f, Rule);
            Rect(gx - 4, Ya(aMax), 8, Ya(crit) - Ya(aMax), LitRed);          // critical and beyond
            Rect(gx - 4, Ya(crit), 8, Ya(warn) - Ya(crit), LitAmber);         // the warning band
            for (int a = 0; a <= 30; a += 5) { float yy = Ya(a); SegD(gx - 7, yy, gx + 7, yy, 1.2f, Color.white); OText(gx + 12, yy, $"{a}", 14, Color.white, TextAnchor.MiddleLeft); }
            float py = Ya(alpha);
            Color pc = alpha >= crit ? LitRed : alpha >= warn ? LitAmber : Color.white;
            SegD(gx - 22, py, gx - 6, py, 4f, pc); SegD(gx - 6, py, gx - 12, py - 6, 3f, pc); SegD(gx - 6, py, gx - 12, py + 6, 3f, pc);
            OText(x + w * 0.5f, y + h - 44, $"α {alpha:0.0}°", 18, pc);
            OText(x + w * 0.5f, y + h - 20, $"CRIT {crit:0}°", 14, LitRed);
        }

        // ---- c. CONTROL COLUMN (side), WHEEL (front), PEDALS ----
        private void Column(float x, float y, float w, float h, WidgetHistory.Snap f)
        {
            Plate(x, y, w, h);
            OText(x + w * 0.5f, y + 18, "CONTROLS", 15, Grey);
            // Side view: the column pivots at the floor; the yoke moves fore (PUSH, left = forward) / aft (PULL).
            float px = x + 70, py = y + 150, len = 96f;
            float ang = (float)f.Ele * 18f * Mathf.Deg2Rad;   // + = forward
            Vector2 top = new(px - Mathf.Sin(ang) * len, py - Mathf.Cos(ang) * len);
            SegD(x + 22, py, x + 118, py, 1.5f, Rule);                       // the floor
            SegD(px, py, top.x, top.y, 5f, Color.white);
            SegD(top.x, top.y, top.x + 18, top.y - 4, 5f, Color.white);     // the yoke horn, toward the pilot (right = aft)
            OText(x + 22, y + 44, "FWD", 14, Grey, TextAnchor.MiddleLeft); OText(x + 118, y + 44, "AFT", 14, Grey, TextAnchor.MiddleRight);
            string col = System.Math.Abs(f.Ele) < 0.01 ? "NEUTRAL" : (f.Ele < 0 ? "PULL " : "PUSH ") + Pct(f.Ele);
            OText(x + 54, y + 200, col, 17, f.Ele < -0.02 ? TElev : Color.white);
            // Front view: the wheel rotates with aileron (90° at full).
            float wx = x + 162, wy = y + 100, th = -(float)f.Ail * 90f * Mathf.Deg2Rad;
            Vector2 R(float a, float b) { float c = Mathf.Cos(th), s = Mathf.Sin(th); return new Vector2(wx + a * c - b * s, wy + a * s + b * c); }
            void L(float a0, float b0, float a1, float b1) { var p = R(a0, b0); var q = R(a1, b1); SegD(p.x, p.y, q.x, q.y, 3.5f, Color.white); }
            L(-30, 4, 30, 4); L(-30, 4, -30, -16); L(30, 4, 30, -16); L(0, 4, 0, 16);
            Ring(wx, wy, 6, 2.5f, Color.white);
            OText(x + 152, y + 200, System.Math.Abs(f.Ail) < 0.01 ? "WHEEL LEVEL" : $"WHEEL {(f.Ail < 0 ? "L" : "R")} {Pct(f.Ail)}", 15, Color.white);
            // Pedals: the pressed one slides forward (up).
            float my = y + 270, travel = 26f;
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f, ex = x + w * 0.5f + side * 40f;
                float yy = my - (float)f.Rud * side * travel;
                bool pressed = side * f.Rud > 0.02;
                Box(ex - 20, yy - 30, 40, 60, 3f, pressed ? Color.white : Grey);
                double brk = k == 0 ? f.BrakeL : f.BrakeR;
                if (brk > 0.03) Rect(ex - 18, yy - 28, 36, 14, new Color(LitRed.r, LitRed.g, LitRed.b, 0.35f + 0.6f * (float)brk));
                OText(ex, yy + 12, k == 0 ? "L" : "R", 15, pressed ? Color.white : Grey);
            }
            OText(x + w * 0.5f, y + 340, System.Math.Abs(f.Rud) < 0.01 ? "PEDALS NEUTRAL" : $"{(f.Rud < 0 ? "LEFT" : "RIGHT")} PEDAL {Pct(f.Rud)}", 16, Color.white);
            OText(x + w * 0.5f, y + 366, f.BrakeL > 0.03 || f.BrakeR > 0.03 ? $"BRAKES L {f.BrakeL * 100:0} R {f.BrakeR * 100:0}" : "RUDDER PEDALS", 14, Grey);
            if (f.HandsOff) OText(x + w * 0.5f, y + 398, "HANDS OFF", 16, LitAmber);
        }

        // ---- d. POWER ----
        private void Power(float x, float y, float w, float h, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec, FlyingGame.Sim.Aircraft ac)
        {
            Plate(x, y, w, h);
            bool na = spec.Engines == 0;
            OText(x + w * 0.5f, y + 18, spec.Engines > 1 ? "POWER ×" + spec.Engines : "POWER", 15, na ? OffText : Grey);
            float y0 = y + 54, y1 = y + h - 90;
            OText(x + 10, y0, "MAX", 14, na ? OffText : Grey, TextAnchor.MiddleLeft); OText(x + 10, y1, "IDLE", 14, na ? OffText : Grey, TextAnchor.MiddleLeft);
            int n = Mathf.Clamp(spec.Engines, 1, 4);
            for (int i = 0; i < n; i++)
            {
                float lx = x + 62 + (n > 1 ? (i - (n - 1) * 0.5f) * 14f : 0f);
                SegD(lx, y0, lx, y1, 2f, na ? OffText : Rule);
                float ly = y1 - (na ? 0f : (float)f.Thr) * (y1 - y0);
                Rect(lx - 9, ly - 6, 18, 12, na ? OffText : Color.white);
            }
            OText(x + w * 0.5f, y + h - 58, na ? "N/A" : Pct(f.Thr), 20, na ? OffText : Color.white);
            if (!na && ac != null && ac.EngineRpm > 1) OText(x + w * 0.5f, y + h - 30, $"{ac.EngineRpm:0} RPM", 14, Grey);
        }

        // ---- e. FLAPS · GEAR · SPOILERS ----
        private void Levers(float x, float y, float w, float h, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec)
        {
            Plate(x, y, w, h);
            // FLAPS
            bool nf = spec.Flaps.Length < 2;
            float fx = x + 46, g0 = y + 54, g1 = y + 250;
            OText(fx, y + 18, "FLAPS", 15, nf ? OffText : Grey);
            SegD(fx, g0, fx, g1, 2.5f, nf ? OffText : Rule);
            if (!nf)
            {
                double full = spec.Flaps[^1]; var d = spec.Flaps;
                double G(double frac) { double deg = frac * full; for (int i = 1; i < d.Length; i++) if (deg <= d[i] + 1e-9) return (i - 1 + (deg - d[i - 1]) / System.Math.Max(1e-9, d[i] - d[i - 1])) / (d.Length - 1); return 1; }
                float lastY = -999f;
                for (int i = 0; i < d.Length; i++)
                {
                    double dd = d[i];
                    float yy = g0 + (float)G(dd / full) * (g1 - g0);
                    SegD(fx - 7, yy, fx + 7, yy, 1.2f, Rule);
                    // Label a detent only when it has room (UP and full flap always; the 737's close ones skip).
                    bool must = i == 0 || i == d.Length - 1;
                    float nextRoom = i + 1 < d.Length ? (g0 + (float)G(d[i + 1] / full) * (g1 - g0)) - yy : 99f;
                    if ((must || (yy - lastY >= 18f && (nextRoom >= 18f || i + 1 == d.Length - 1 && nextRoom >= 18f))) && (must || yy - lastY >= 18f))
                    {
                        if (i == d.Length - 1 && yy - lastY < 18f) continue;
                        OText(fx - 16, yy, dd < 1e-3 ? "UP" : $"{dd:0}", 14, Grey, TextAnchor.MiddleRight); lastY = yy;
                    }
                }
                float hy = g0 + (float)G(f.FlapsCmd) * (g1 - g0), ay = g0 + (float)G(f.FlapsActual) * (g1 - g0);
                Rect(fx - 9, hy - 5, 18, 10, Color.white);
                bool lag = System.Math.Abs(f.FlapsActual - f.FlapsCmd) > 0.004;
                SegD(fx + 16, ay, fx + 26, ay - 6, 2.5f, lag ? LitAmber : Grey); SegD(fx + 16, ay, fx + 26, ay + 6, 2.5f, lag ? LitAmber : Grey);
                OText(fx, y + 280, $"{f.FlapsActual * full:0}°", 18, Color.white);
            }
            else { Rect(fx - 13, g0 - 5, 26, 10, OffText); OText(fx, y + 280, "N/A", 18, OffText); }
            // GEAR
            bool fixedGear = !spec.Retractable;
            float gx = x + 112, u = y + 70, dn = y + 170;
            OText(gx, y + 18, "GEAR", 15, fixedGear ? OffText : Grey);
            SegD(gx, u, gx, dn, 2.5f, fixedGear ? OffText : Rule);
            float ky = fixedGear || f.Gear == "down" ? dn : u;
            Ring(gx, ky, 11, 3.5f, fixedGear ? OffText : Color.white);
            string lights = fixedGear ? "down" : f.GearLights;
            for (int i = -1; i <= 1; i++)
            {
                float lx = gx + i * 22, ly = y + 214;
                if (fixedGear) Ring(lx, ly, 8, 1.4f, OffText);
                else if (lights == "down") Disc(lx, ly, 8, LitGreen);
                else if (lights == "transit") Disc(lx, ly, 8, LitRed);
                else Ring(lx, ly, 8, 1.4f, Grey);
            }
            OText(gx, y + 250, fixedGear ? "FIXED" : lights == "transit" ? "TRANSIT" : lights == "down" ? "DOWN" : "UP", 16, fixedGear ? OffText : Color.white);
            // SPOILERS
            bool ns = !spec.Spoilers;
            float sx = x + 178, s0 = y + 70, s1 = y + 230;
            OText(sx, y + 18, spec.Engines > 0 && !ns ? "SPD BRK" : "SPLR", 15, ns ? OffText : Grey);
            SegD(sx, s0, sx, s1, 2.5f, ns ? OffText : Rule);
            float sy = ns ? s0 : s0 + (float)f.Spoilers * (s1 - s0);
            Rect(sx - 11, sy - 5, 22, 10, ns ? OffText : f.Spoilers > 0.01 ? LitAmber : f.SpoilersArmed ? LitGreen : Color.white);
            OText(sx, y + 250, ns ? "N/A" : f.Spoilers > 0.01 ? $"EXT" : f.SpoilersArmed ? "ARM" : "RET", 16, ns ? OffText : Color.white);
        }

        // ---- f. ANNUNCIATORS ----
        private void Annunciators(float x, float y, float w, float h, bool stall, bool shaker, bool spin, AeroWidget wd)
        {
            var items = new List<(string text, bool on, Color lit)>
            {
                (shaker ? "STICK SHAKER" : "STALL", stall, LitRed),
                ("SPIN", spin, LitRed),
                ("HOLD", wd.Controls.Hold, LitAmber),
                (wd.Paused ? $"REVIEW {wd.Review.OffsetMs / 1000.0:+0.0;-0.0;0.0} s" : "REVIEW", wd.Paused, LitAmber),
            };
            if (wd.Controls.Managed && wd.Controls.PilotName != null) items.Add(($"PF {wd.Controls.PilotName.ToUpperInvariant()}", true, new Color(0.25f, 0.45f, 0.75f, 0.9f)));
            float bw = (w - (items.Count - 1) * 8f) / items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                float bx = x + i * (bw + 8f);
                var it = items[i];
                if (it.on) RoundRect(bx, y, bw, h, 6, it.lit); else Plate(bx, y, bw, h);
                OText(bx + bw * 0.5f, y + h * 0.5f, it.text, 18, it.on ? Color.white : OffText);
            }
        }
    }
}

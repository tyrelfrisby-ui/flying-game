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
        // Design space: the panel is 1080 × 550 units (the autopilot's MCP strip on top, then the instruments), scaled to its strip.
        // Owner 2026-10-11: "more wide, less tall" — 1080 × 380 (≈ 2.85 : 1).
        private const float NW = 1080f, NH = 380f, MY = 52f, IH = 282f;   // MY: the instruments sit below the MCP; IH: their height
        /// <summary>The panel box's height over its width.</summary>
        public const float PanelAspect = NH / NW;
        private Rect _panelPx;   // the panel box in frame pixels (origin bottom-left)
        /// <summary>Panel labels: bold and brighter than the old grey (owner: "it is on a tiny teleprompter").</summary>
        private static readonly Color Lbl = new(1f, 1f, 1f, 0.95f);

        /// <summary>The panel's box in frame pixels: where the presenter put it, or the default (bottom, full width).</summary>
        private Rect NtsbPanelRect(AeroWidget w)
        {
            float wf = w.PanelW > 0 ? w.PanelW : Mathf.Min(1f, w.Split / PanelAspect), hf = wf * PanelAspect;
            float x = w.PanelW > 0 ? w.PanelX : (1f - wf) * 0.5f, y = w.PanelW > 0 ? w.PanelY : 1f - hf;
            return new Rect(x * N, N - (y + hf) * N, wf * N, hf * N);
        }
        /// <summary>The airplane's picture: the biggest clear band beside the panel (the whole frame if none is big enough).</summary>
        private static Rect FreeBand(Rect p)
        {
            var c = new[] { new Rect(0, p.yMax, N, N - p.yMax), new Rect(0, 0, N, p.y), new Rect(0, 0, p.x, N), new Rect(p.xMax, 0, N - p.xMax, N) };
            Rect best = c[0]; foreach (var r in c) if (r.width * r.height > best.width * best.height) best = r;
            return best.width * best.height >= 0.3f * N * N && best.width >= 0.3f * N && best.height >= 0.3f * N ? best : new Rect(0, 0, N, N);
        }
        private static readonly Color PlateBg = new(0f, 0f, 0f, 0.55f), Rule = new(0.85f, 0.87f, 0.9f, 0.55f),
            Sky = new(0.20f, 0.45f, 0.78f, 0.95f), Ground = new(0.50f, 0.33f, 0.18f, 0.95f), LitRed = new(0.85f, 0.1f, 0.08f, 0.95f),
            LitAmber = new(0.95f, 0.65f, 0.1f, 0.95f), LitGreen = new(0.15f, 0.7f, 0.3f, 0.95f), OffText = new(1f, 1f, 1f, 0.32f);

        private void NtsbLayout(AeroWidget w, Rect box)
        {
            _s = box.width / NW;
            _o = new Vector2(box.x, box.yMax);                      // top-left of the box
            var f = w.Shown ?? w.Review.Capture();
            var r = w.Read();
            var spec = w.Controls.Spec;
            double critA = w.CriticalAlphaDeg(f.FlapsActual), warnA = critA - (spec.StickShaker ? 2.0 : 3.0);
            bool stall = r.AlphaDeg >= warnA && !r.OnGround;
            bool spin = !r.OnGround && System.Math.Abs(r.YawRateDps) > 40 && r.AlphaDeg > critA;

            Mcp(8, 6, NW - 16, 46, w.Autopilot);
            // The instrument row: only the controls this airplane has (owner 2026-10-11) — no power plate on a glider, no gear
            // lever on fixed gear, no flaps or spoilers it doesn't have. The row is centred under the MCP.
            bool power = spec.Engines > 0, flaps = spec.Flaps.Length > 1, gear = spec.Retractable, splr = spec.Spoilers;
            float leversW = LeversWidth(flaps, gear, splr);
            float rowW = 420 + 8 + 90 + 8 + 204 + (power ? 8 + 92 : 0) + (leversW > 0 ? 8 + leversW : 0);
            float x = (NW - rowW) * 0.5f, y = 6 + MY;
            Pfd(x, y, 420, IH, w.Autopilot, r, f, w.StallKias(f.FlapsActual)); x += 428;
            Aoa(x, y, 90, IH, r.AlphaDeg, critA, warnA); x += 98;
            Column(x, y, 204, IH, f);
            // Touch layout (addendum): the yoke trackpad over the column + wheel, the pedal slider, the levers.
            TouchBox(x + 8, y + 24, 188, 128); LayoutRect("yoke", x + 8, y + 24, 188, 128);
            TouchBox(x + 8, y + 168, 188, 92); LayoutRect("pedals", x + 8, y + 168, 188, 92);
            // Thumbs at the current inputs: the pedals.
            { float cx = x + 102, ty = y + 254; float tx = cx + (float)f.Rud * 94f; Rect(tx - 10, ty - 5, 20, 10, new Color(1, 1, 1, 0.8f)); SegD(x + 16, ty, x + 188, ty, 1f, new Color(1, 1, 1, 0.25f)); }
            x += 212;
            if (power)
            {
                Power(x, y, 92, IH, f, spec, w.Ac);
                LayoutRect("throttle", x + 34, y + 54, 52, IH - 90 - 54);   // exactly the lever's travel: top = full
                x += 100;
            }
            FlapDetentPositions = null;
            if (leversW > 0) Levers(x, y, leversW, IH, f, spec, flaps, gear, splr);
            float gw = Mathf.Max(44f, 0.045f * N / _s), gh = NH - (6 + MY + IH + 2);   // the resize grip: ≥ 4.5 % of the frame wide at any size (Glass)
            Annunciators(8, 6 + MY + IH + 6, NW - 16 - gw, 28, stall, spec.StickShaker, spin, w);
            // The box itself: the presenter drags it anywhere and pulls its corner grip to resize (Glass lays its handles here).
            LayoutRect("panel", 0, 0, NW, NH);
            LayoutRect("panelMove", 8, 6, NW - 16, 46);   // the MCP strip (display only): the obvious place to grab the box
            LayoutRect("panelResize", NW - gw, NH - gh, gw, gh);
            var gc = new Color(1, 1, 1, 0.75f);
            float gk = Mathf.Min(gw, gh) - 8;   // the corner grip: diagonal ridges filling the corner
            for (int i = 1; i <= 4; i++) { float d = gk * i / 4f; SegD(NW - 4 - d, NH - 4, NW - 4, NH - 4 - d, 3f, gc); }
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
            _texts.Add((P(x, y), s, Mathf.Max(size * 1.15f, 17f) * _s, c == Grey ? Lbl : c, a, true, true));   // bigger + bold for small displays

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
        private void Pfd(float x, float y, float w, float h, WidgetAutopilot ap, AeroWidget.Readout r, WidgetHistory.Snap f, double vsKias)
        {
            Plate(x, y, w, h);
            Fma(x + 12, y + 5, 396, 44, ap, r);
            // Attitude indicator, centred (below the FMA).
            var face = new Rect(x + 92, y + 66, 236, 168);
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
                    if (face.Contains(lp) && Vector2.Distance(lp, new Vector2(cx, cy)) < 72f - 16f) OText(lp.x, lp.y, $"{System.Math.Abs(deg)}", 14, Color.white);   // (never under the bank scale)
                }
            }
            // The fixed airplane symbol.
            SegD(cx - 60, cy, cx - 22, cy, 4f, LitAmber); SegD(cx + 22, cy, cx + 60, cy, 4f, LitAmber);
            SegD(cx - 22, cy, cx - 22, cy + 10, 4f, LitAmber); SegD(cx + 22, cy, cx + 22, cy + 10, 4f, LitAmber);
            Rect(cx - 3, cy - 3, 6, 6, LitAmber);
            // Bank scale (an arc above the face centre) with a pointer.
            float br = 72f;
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
            var tape = new Rect(x + 12, y + 66, 70, 168);
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
            var at = new Rect(x + 334, y + 66, 64, 168);
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
            OText(cx, y + 248, $"HDG {r.HeadingDeg:000}°", 18, Color.white);
            OText(cx, y + 270, $"PITCH {r.PitchDeg:+0;-0;0}°   BANK {System.Math.Abs(r.RollDeg):0}° {(r.RollDeg < -0.5 ? "L" : r.RollDeg > 0.5 ? "R" : "")}   G {r.LoadFactor:0.0}", 15, Grey);
        }

        // ---- MCP: the autopilot mode control panel, across the top (owner 2026-10-11: "a traditional layout") ----
        // Glareshield order: SPEED · HEADING · ALTITUDE · VERT SPEED · CWS · AP/YD. A button's light bar is lit while its
        // mode is ENGAGED; a window shows its target only while that target is being flown ("---" otherwise).
        // (LNAV / LOC / APP aren't built yet, so they aren't on the panel.)
        private void Mcp(float x, float y, float w, float h, WidgetAutopilot ap)
        {
            Plate(x, y, w, h);
            bool on = ap.On, aoa = ap.PitchMode == "aoa", roll = on && !ap.PitchOnly;
            bool flc = on && ap.PitchMode == "flc", atSpd = ap.AutoThrottle && !flc;
            bool altTgt = on && !aoa && (ap.PitchMode == "alt" || ap.PitchMode == "alt*" || flc || ap.PitchMode == "vs" && ap.HasAltTarget);
            const float bw = 52f, ww = 72f, g = 4f;
            float top = y + 18;
            var groups = new List<(string title, List<(float w, System.Action<float> draw)> items)>
            {
                ("SPEED", new() { (bw, bx => Btn(bx, "A/T", ap.AutoThrottle, !ap.HasEngine, ap.AtDisc)), (ww, bx => Win(bx, atSpd || flc ? $"{ap.Kias:0}" : null)), (bw, bx => Btn(bx, "FLC", flc)) }),
                ("HEADING", new() { (ww, bx => Win(bx, roll && ap.RollMode == "hdg" ? $"{ap.HdgDeg:000}°" : null)), (bw, bx => Btn(bx, "HDG", roll && ap.RollMode == "hdg")) }),
                ("ALTITUDE", new() { (ww, bx => Win(bx, altTgt ? $"{ap.AltFt:0}" : null)), (bw, bx => Btn(bx, "ALT", on && (ap.PitchMode == "alt" || ap.PitchMode == "alt*"))) }),
                ("VERT SPEED", new() { (ww, bx => Win(bx, on && ap.PitchMode == "vs" ? $"{ap.VsFpm:+0;-0;0}" : null)), (bw, bx => Btn(bx, "V/S", on && ap.PitchMode == "vs")) }),
                ("CWS  PITCH · BANK", new() { (ww, bx => Win(bx, on && ap.PitchMode == "pit" ? $"{ap.PitchDeg:+0.0;-0.0;0.0}°" : null)), (bw, bx => Btn(bx, "PIT", on && ap.PitchMode == "pit")),
                                    (ww, bx => Win(bx, roll && ap.RollMode == "bank" ? BankText(ap.BankDeg) : null)), (bw, bx => Btn(bx, "BANK", roll && ap.RollMode == "bank")) }),
                ("", new() { (bw, bx => Btn(bx, "AP", on, false, !on && ap.Disc)), (bw, bx => Btn(bx, "YD", ap.YawDamper)) }),
            };
            float total = 0; foreach (var gr in groups) { foreach (var it in gr.items) total += it.w; total += (gr.items.Count - 1) * g; }
            float gap = (w - 16 - total) / (groups.Count - 1), cx = x + 8;
            for (int i = 0; i < groups.Count; i++)
            {
                var gr = groups[i]; float gx0 = cx;
                foreach (var it in gr.items) { it.draw(cx); cx += it.w + g; }
                float gx1 = cx - g;
                if (gr.title.Length > 0) OText((gx0 + gx1) * 0.5f, y + 9, gr.title, 13, Grey);
                if (i + 1 < groups.Count) { float sx = gx1 + (gap - g) * 0.5f + g * 0.5f; SegD(sx, y + 8, sx, y + h - 6, 1f, Rule); }
                cx += gap - g;
            }
            void Btn(float bx, string label, bool lit, bool dark = false, bool warn = false)
            {
                Rect(bx, top, bw, 24, new Color(0.08f, 0.08f, 0.09f, 0.9f));
                Box(bx, top, bw, 24, 1.2f, Rule);
                bool flash = warn && Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.35f;
                Rect(bx + 8, top + 3, bw - 16, 3, lit ? LitGreen : flash ? LitAmber : new Color(1, 1, 1, 0.12f));   // the light bar
                OText(bx + bw * 0.5f, top + 15, label, 14, dark ? OffText : lit ? Color.white : Grey);
            }
            void Win(float bx, string value)
            {
                Rect(bx, top, ww, 24, Color.black);
                Box(bx, top, ww, 24, 1.2f, Rule);
                OText(bx + ww * 0.5f, top + 12, value ?? "- - -", value != null ? 17 : 14, value != null ? LitAmber : OffText);
            }
        }
        private static string BankText(double b) => System.Math.Abs(b) < 0.5 ? "0°" : $"{System.Math.Abs(b):0}° {(b < 0 ? "L" : "R")}";

        // ---- FMA: the flight mode annunciator, on top of the attitude indicator ----
        // Columns, as on a transport PFD: THRUST · ROLL · PITCH · AFDS status. Row 1 = the ENGAGED mode and its target (green),
        // row 2 = the ARMED mode (white) — or, when the AP can't hold, why (amber). A mode that just changed is boxed for 10 s.
        private readonly string[] _fmaLast = new string[4];
        private readonly float[] _fmaAt = { -99f, -99f, -99f, -99f };
        private void Fma(float x, float y, float w, float h, WidgetAutopilot ap, AeroWidget.Readout r)
        {
            Rect(x, y, w, h, new Color(0, 0, 0, 0.75f));
            float[] cw = { 86, 120, 118, 72 };
            bool on = ap.On, roll = on && !ap.PitchOnly;
            string thr = on && ap.PitchMode == "flc" ? (ap.AltFt > r.HeightFt ? "THR CLB" : "THR IDLE") : ap.AutoThrottle ? $"SPD {ap.Kias:0}" : ap.AtDisc ? "A/T DISC" : null;
            string lat = !roll ? null : ap.RollMode == "hdg" ? $"HDG {ap.HdgDeg:000}" : ap.RollMode == "bank" ? $"BANK {BankText(ap.BankDeg)}" : "ROL";
            string vert = !on ? null : ap.PitchMode switch
            {
                "pit" => $"PIT {ap.PitchDeg:+0.0;-0.0;0.0}°", "vs" => $"VS {ap.VsFpm:+0;-0;0}", "flc" => $"FLC {ap.Kias:0}",
                "alt*" => $"ALT* {ap.AltFt:0}", "aoa" => $"AOA {ap.AoaCmd:0.0}°", _ => $"ALT {ap.AltFt:0}",
            };
            string vertArm = on && (ap.PitchMode == "flc" || ap.PitchMode == "vs" && ap.HasAltTarget) ? $"ALT {ap.AltFt:0}" : null;
            string why = !on ? null : ap.Status switch { "cantHold:power" => "FULL POWER", "cantHold:elevator" => "ELEV LIMIT", "cantHold:stall" => "STALL", "oscillating" => "OSCILLATING", _ => null };
            string afds = on ? (ap.CwsOverride ? "CWS" : "AP") : ap.Disc ? "AP" : null;
            string[] act = { thr, lat, vert, afds };
            string[] arm = { null, null, why == null ? vertArm : why, ap.YawDamper ? "YD" : null };
            float cx = x;
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) SegD(cx, y + 4, cx, y + h - 4, 1.2f, Rule);
                float mx = cx + cw[i] * 0.5f;
                if (act[i] != _fmaLast[i]) { _fmaLast[i] = act[i]; _fmaAt[i] = act[i] != null ? Time.unscaledTime : -99f; }
                if (act[i] != null)
                {
                    bool discFlash = i == 3 && !on || i == 0 && ap.AtDisc && !ap.AutoThrottle;
                    Color ac = discFlash ? (Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.35f ? LitAmber : OffText) : i == 3 && ap.CwsOverride ? LitAmber : FmaGreen;
                    OText(mx, y + 13, act[i], 15, ac);
                    if (Time.unscaledTime - _fmaAt[i] < 10f && !discFlash) Box(cx + 4, y + 2, cw[i] - 8, 22, 1.4f, Color.white);   // the mode-change box
                }
                if (arm[i] != null) OText(mx, y + 33, arm[i], 14, i == 2 && why != null ? LitAmber : Color.white);
                cx += cw[i];
            }
        }
        private static readonly Color FmaGreen = new(0.3f, 0.95f, 0.4f, 1f);

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
            OText(x + w * 0.5f, y + h - 44, System.Math.Abs(alpha) >= 100 ? $"α {alpha:0}°" : $"α {alpha:0.0}°", 16, pc);
            OText(x + w * 0.5f, y + h - 20, $"CRIT {crit:0}°", 14, LitRed);
        }

        // ---- c. CONTROL COLUMN (side), WHEEL (front), PEDALS ----
        private void Column(float x, float y, float w, float h, WidgetHistory.Snap f)
        {
            Plate(x, y, w, h);
            OText(x + w * 0.5f, y + 18, "CONTROLS", 15, Grey);
            // THE YOKE (owner 2026-10-09): one wheel showing BOTH axes, in front of a fixed black shadow of itself at neutral.
            // Aileron rotates it (90° at full). The column pivots at the floor, so the yoke drops either way from neutral:
            // pulled AFT it comes toward the pilot — lower and BIGGER; pushed FORWARD it goes away — lower and SMALLER.
            // A thin elevator gauge on the right, a thin aileron gauge underneath; the whole box is the touch control.
            float cx = x + 92, cy = y + 74;
            void Wheel(float ox, float oy, float scale, float rot, Color c, float lw)
            {
                float co = Mathf.Cos(rot), si = Mathf.Sin(rot);
                Vector2 Rr(float a, float b) => new(ox + (a * co - b * si) * scale, oy + (a * si + b * co) * scale);
                void Ln(float a0, float b0, float a1, float b1) { var p = Rr(a0, b0); var q = Rr(a1, b1); SegD(p.x, p.y, q.x, q.y, lw * scale, c); }
                Ln(-46, 6, 46, 6); Ln(-46, 6, -46, -26); Ln(46, 6, 46, -26); Ln(-46, -26, -38, -34); Ln(46, -26, 38, -34); Ln(0, 6, 0, 26);
                var hub = Rr(0, 6); Ring(hub.x, hub.y, 9 * scale, lw * 0.8f * scale, c);
            }
            float ele = (float)f.Ele;                        // + = push (forward)
            float sc = 1f - 0.24f * ele;                     // aft (−) bigger, forward (+) smaller
            float drop = 22f * ele * ele;                    // lower either way (the column's arc)
            Wheel(cx, cy, 1f, 0f, new Color(0, 0, 0, 0.8f), 9f);                          // the neutral shadow
            Wheel(cx, cy + drop, sc, (float)f.Ail * 90f * Mathf.Deg2Rad, Color.white, 5f);    // the yoke (+ = right = clockwise; screen y is down)
            // Elevator gauge (thin, beside the box): up = PUSH, down = PULL.
            float ex0 = x + 186, ey0 = y + 30, ey1 = y + 134, eym = (ey0 + ey1) * 0.5f;
            SegD(ex0, ey0, ex0, ey1, 1.5f, Rule); SegD(ex0 - 4, eym, ex0 + 4, eym, 1.2f, Rule);
            float emk = eym - ele * (ey1 - ey0) * 0.5f;
            Rect(ex0 - 5, emk - 2.5f, 10, 5, ele < -0.02f ? TElev : Color.white);
            // Aileron gauge (thin, under the yoke): right = right aileron.
            float ax0 = x + 20, ax1 = x + 166, ay = y + 140, axm = (ax0 + ax1) * 0.5f;
            SegD(ax0, ay, ax1, ay, 1.5f, Rule); SegD(axm, ay - 4, axm, ay + 4, 1.2f, Rule);
            float amk = axm + (float)f.Ail * (ax1 - ax0) * 0.5f;
            Rect(amk - 2.5f, ay - 5, 5, 10, Color.white);
            string col = System.Math.Abs(f.Ele) < 0.01 ? "NEUTRAL" : (f.Ele < 0 ? "PULL " : "PUSH ") + Pct(f.Ele);
            OText(x + 54, y + 156, col, 16, f.Ele < -0.02 ? TElev : Color.white);
            OText(x + 150, y + 156, System.Math.Abs(f.Ail) < 0.01 ? "AIL NEUTRAL" : $"AIL {(f.Ail < 0 ? "L" : "R")} {Pct(f.Ail)}", 15, Color.white);
            // Pedals: the pressed one slides forward — up a little and smaller (away from the pilot); the other comes back, bigger.
            float my = y + 210, travel = 10f;
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f, ex = x + w * 0.5f + side * 40f;
                float push = (float)f.Rud * side;
                float yy = my - push * travel, s = 1f - 0.2f * push;
                bool pressed = side * f.Rud > 0.02;
                Box(ex - 16 * s, yy - 20 * s, 32 * s, 40 * s, 3f, pressed ? Color.white : Grey);
                double brk = k == 0 ? f.BrakeL : f.BrakeR;
                if (brk > 0.03) Rect(ex - 14 * s, yy - 18 * s, 28 * s, 10 * s, new Color(LitRed.r, LitRed.g, LitRed.b, 0.35f + 0.6f * (float)brk));
                OText(ex, yy + 6 * s, k == 0 ? "L" : "R", Mathf.RoundToInt(15 * s), pressed ? Color.white : Grey);
            }
            OText(x + w * 0.5f, y + 270, f.HandsOff ? "HANDS OFF" : f.BrakeL > 0.03 || f.BrakeR > 0.03 ? $"BRAKES L {f.BrakeL * 100:0} R {f.BrakeR * 100:0}" : System.Math.Abs(f.Rud) < 0.01 ? "PEDALS NEUTRAL" : $"{(f.Rud < 0 ? "LEFT" : "RIGHT")} PEDAL {Pct(f.Rud)}", 16, f.HandsOff ? LitAmber : Color.white);
        }

        // ---- d. POWER ----
        private void Power(float x, float y, float w, float h, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec, FlyingGame.Sim.Aircraft ac)
        {
            Plate(x, y, w, h);
            bool na = spec.Engines == 0;
            OText(x + w * 0.5f, y + (spec.Engines > 1 ? 12 : 18), "POWER", 15, na ? OffText : Grey); if (spec.Engines > 1) OText(x + w * 0.5f, y + 30, "×" + spec.Engines, 14, Grey);
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

        // ---- e. FLAPS · GEAR · SPOILERS — only the ones the airplane has ----
        private const float FlapColW = 80f, GearColW = 66f, SplrColW = 70f;
        private static float LeversWidth(bool flaps, bool gear, bool splr)
        {
            float w = (flaps ? FlapColW : 0) + (gear ? GearColW : 0) + (splr ? SplrColW : 0);
            return w > 0 ? w + 8 : 0;
        }
        private void Levers(float x, float y, float w, float h, WidgetHistory.Snap f, WidgetAircraftControls.Spec spec, bool flaps, bool gear, bool splr)
        {
            Plate(x, y, w, h);
            float cx = x + 4;
            if (flaps)
            {
                float fx = cx + 50, g0 = y + 50, g1 = y + h - 62;
                OText(fx - 4, y + 18, "FLAPS", 15, Grey);
                SegD(fx, g0, fx, g1, 2.5f, Rule);
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
                    if (i == d.Length - 1 && yy - lastY < 18f) continue;
                    if (must || yy - lastY >= 18f) { OText(fx - 14, yy, dd < 1e-3 ? "UP" : $"{dd:0}", 15, Grey, TextAnchor.MiddleRight); lastY = yy; }
                }
                float hy = g0 + (float)G(f.FlapsCmd) * (g1 - g0), ay = g0 + (float)G(f.FlapsActual) * (g1 - g0);
                Rect(fx - 10, hy - 6, 20, 12, Color.white);
                bool lag = System.Math.Abs(f.FlapsActual - f.FlapsCmd) > 0.004;
                SegD(fx + 16, ay, fx + 26, ay - 6, 2.5f, lag ? LitAmber : Grey); SegD(fx + 16, ay, fx + 26, ay + 6, 2.5f, lag ? LitAmber : Grey);
                OText(fx - 4, y + h - 34, $"{f.FlapsActual * full:0}°", 18, Color.white);
                LayoutRect("flaps", fx - 26, g0, 52, g1 - g0);       // the gate: top = up
                FlapDetentPositions = FlapDetents(spec);
                cx += FlapColW;
            }
            if (gear)
            {
                // THE GEAR LEVER: a slot with UP at the top and DN at the bottom, the handle's wheel-shaped knob in it; the three
                // gear lights above the state (green = down and locked, red = in transit).
                float gx = cx + GearColW * 0.4f, u = y + 62, dn = y + 142;
                OText(gx + 6, y + 18, "GEAR", 15, Grey);
                Box(gx - 6, u - 12, 12, dn - u + 24, 1.5f, Rule);
                OText(gx + 19, u, "UP", 14, Grey, TextAnchor.MiddleLeft); OText(gx + 19, dn, "DN", 14, Grey, TextAnchor.MiddleLeft);
                float ky = f.Gear == "down" ? dn : u;
                SegD(gx, (u + dn) * 0.5f, gx, ky, 4f, Color.white);   // the handle's arm from the pivot
                Disc(gx, ky, 15, Color.white);                        // the knob: a little wheel
                Disc(gx, ky, 9, new Color(0.15f, 0.15f, 0.15f, 1f));
                Disc(gx, ky, 4, Color.white);
                string lights = f.GearLights;
                for (int i = -1; i <= 1; i++)
                {
                    float lx = gx + 6 + i * 20, ly = y + h - 70;
                    if (lights == "down") Disc(lx, ly, 8, LitGreen);
                    else if (lights == "transit") Disc(lx, ly, 8, LitRed);
                    else Ring(lx, ly, 8, 1.4f, Grey);
                }
                OText(gx + 6, y + h - 34, lights == "transit" ? "TRANSIT" : lights == "down" ? "DOWN" : "UP", 16, Color.white);
                LayoutRect("gear", gx - 26, u - 20, 52, dn - u + 40);   // tap: toggle
                cx += GearColW;
            }
            if (splr)
            {
                float sx = cx + SplrColW * 0.5f, s0 = y + 56, s1 = y + h - 70;
                if (spec.Engines > 0) { OText(sx, y + 12, "SPD", 14, Grey); OText(sx, y + 30, "BRK", 14, Grey); } else OText(sx, y + 18, "SPLR", 15, Grey);
                SegD(sx, s0, sx, s1, 2.5f, Rule);
                float sy = s0 + (float)f.Spoilers * (s1 - s0);
                Rect(sx - 11, sy - 5, 22, 10, f.Spoilers > 0.01 ? LitAmber : f.SpoilersArmed ? LitGreen : Color.white);
                OText(sx, y + h - 34, f.Spoilers > 0.01 ? "EXT" : f.SpoilersArmed ? "ARM" : "RET", 16, Color.white);
                LayoutRect("spoilers", sx - 26, s0, 52, s1 - s0);     // top = retracted
            }
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
            // The autopilot's modes are on the FMA (top of the PFD) and the MCP, not here.
            if (wd.AltLockAnnunciation is string al) items.Add((al, true, new Color(0.25f, 0.45f, 0.75f, 0.9f)));
            if (!wd.Loading.WithinLimits) items.Add(("OUT OF LIMITS", true, LitAmber));
            if (wd.Controls.Managed && wd.Controls.PilotName != null) items.Add(($"PF {wd.Controls.PilotName.ToUpperInvariant()}", true, new Color(0.25f, 0.45f, 0.75f, 0.9f)));
            float bw = (w - (items.Count - 1) * 8f) / items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                float bx = x + i * (bw + 8f);
                var it = items[i];
                if (it.on) RoundRect(bx, y, bw, h, 6, it.lit); else Plate(bx, y, bw, h);
                OText(bx + bw * 0.5f, y + h * 0.5f, it.text, Mathf.Min(18f, (bw - 14f) / Mathf.Max(1, it.text.Length * 0.6f)), it.on ? Color.white : OffText);   // long mode lines shrink to fit
            }
        }
    }
}

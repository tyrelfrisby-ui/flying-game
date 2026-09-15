using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// Renders the lesson pictures live from the game (owner 2026-09-15: "screenshots from the game"): the aircraft is posed,
    /// an off-screen camera renders the scene, and the force arrows for the lesson are drawn over it. Two pictures for
    /// the "Straight" lesson: a rear view with the lift tilted by the bank, and a top view with the relative wind of the
    /// slip, the vertical tail's force and the resulting yaw.
    /// </summary>
    public static class LessonIllustrator
    {
        private sealed class Painter : MonoBehaviour
        {
            public readonly List<(Vector3 from, Vector3 to, Color c)> Lines = new();
            private Material _mat;
            private void OnPostRender()
            {
                _mat ??= new Material(Shader.Find("FlyingGame/HudLine") ?? Shader.Find("Unlit/Color")) { color = Color.white };
                _mat.SetPass(0);
                GL.PushMatrix(); GL.Begin(GL.LINES);
                foreach (var (a, b, c) in Lines) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
                GL.End(); GL.PopMatrix();
            }
            public void Arrow(Vector3 from, Vector3 v, Color c, float thick = 0.06f)
            {
                Vector3 to = from + v; Vector3 d = v.normalized;
                Vector3 side = Vector3.Cross(d, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(d, Vector3.right); side.Normalize();
                Vector3 up2 = Vector3.Cross(d, side).normalized;
                // a fat shaft: several parallel lines
                for (int i = -2; i <= 2; i++) { Vector3 o = side * (i * thick) ; Lines.Add((from + o, to + o, c)); Vector3 o2 = up2 * (i * thick); Lines.Add((from + o2, to + o2, c)); }
                float h = Mathf.Min(0.9f, v.magnitude * 0.25f);
                for (int i = -2; i <= 2; i++) { Vector3 o = up2 * (i * thick * 0.5f); Lines.Add((to + o, to - d * h + side * h * 0.6f + o, c)); Lines.Add((to + o, to - d * h - side * h * 0.6f + o, c)); }
            }
            public void Arc(Vector3 centre, Vector3 axis, float radius, float fromDeg, float toDeg, Color c)
            {
                Vector3 r0 = Vector3.Cross(axis, Vector3.forward); if (r0.sqrMagnitude < 1e-4f) r0 = Vector3.Cross(axis, Vector3.right); r0.Normalize();
                Vector3 prev = Vector3.zero; int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n);
                    Vector3 p = centre + Quaternion.AngleAxis(a, axis) * r0 * radius;
                    if (i > 0) { Lines.Add((prev, p, c)); Lines.Add((prev + axis * 0.05f, p + axis * 0.05f, c)); }
                    prev = p;
                }
                Vector3 tip = prev, tan = (prev - (centre + Quaternion.AngleAxis(toDeg - 4f, axis) * r0 * radius)).normalized;
                Arrow(tip - tan * 0.6f, tan * 0.6f, c, 0.04f);
            }
        }

        /// <summary>Render one picture: pose the aircraft (bank/yaw in degrees), place the camera, draw the arrows, restore.</summary>
        private static Texture2D Render(Transform aircraft, float bankDeg, float yawDeg, bool topView, System.Action<Painter, Transform> draw, int w = 1024, int h = 640)
        {
            Vector3 pos0 = aircraft.position; Quaternion rot0 = aircraft.rotation;
            aircraft.rotation = Quaternion.AngleAxis(yawDeg, Vector3.up) * rot0 * Quaternion.AngleAxis(-bankDeg, Vector3.forward);
            var go = new GameObject("LessonCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 45f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 20000f;
            var painter = go.AddComponent<Painter>();
            float span = 12f;
            var vis = aircraft.GetComponent<AirframeVisual>();
            var main = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (main != null) span = main.Distance / 1.3f;
            if (topView)
            {
                cam.transform.position = aircraft.position + Vector3.up * (span * 2.2f) + aircraft.forward * 1.5f;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, aircraft.forward);
            }
            else
            {
                cam.transform.position = aircraft.position - aircraft.forward * (span * 1.6f) + Vector3.up * (span * 0.25f);
                cam.transform.rotation = Quaternion.LookRotation(aircraft.position + Vector3.up * 0.5f - cam.transform.position, Vector3.up);
            }
            draw(painter, aircraft);
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(go);
            aircraft.position = pos0; aircraft.rotation = rot0;
            return tex;
        }

        // ---- charts drawn pixel by pixel (no font: the legend text sits under the picture) ----
        private static void Line(Texture2D t, int x0, int y0, int x1, int y1, Color c, int thick = 2)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 100000; guard++)
            {
                for (int ox = -thick / 2; ox <= thick / 2; ox++) for (int oy = -thick / 2; oy <= thick / 2; oy++)
                { int px = x0 + ox, py = y0 + oy; if (px >= 0 && py >= 0 && px < t.width && py < t.height) t.SetPixel(px, py, c); }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err; if (e2 >= dy) { err += dy; x0 += sx; } if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
        private static void Dot(Texture2D t, int x, int y, int r, Color c) { for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) if (i * i + j * j <= r * r && x + i >= 0 && y + j >= 0 && x + i < t.width && y + j < t.height) t.SetPixel(x + i, y + j, c); }
        private static Texture2D Blank(int w, int h) { var t = new Texture2D(w, h, TextureFormat.RGB24, false); var px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = new Color(0.08f, 0.1f, 0.14f); t.SetPixels(px); return t; }

        /// <summary>Glide page 1: the polar (sink against speed, power off), the min-sink point, the best-L/D tangent from the
        /// origin and the wind-shifted tangent to the speed to fly.</summary>
        public static Texture2D PolarChart(FlyingGame.Sim.Practice.PracticeScenario sc)
        {
            int w = 1024, h = 640; var t = Blank(w, h);
            var cfg = sc.Config; double alt = sc.SurfaceM + FlyingGame.Sim.Practice.PracticeScenario.AirworkAglM;
            float vMax = (float)(2.2 * sc.VsoMs), sinkMax = 0f;
            var pts = new System.Collections.Generic.List<(float v, float sink)>();
            for (double v = sc.VsoMs * 1.02; v <= vMax; v += 0.5)
            {
                var r = FlyingGame.Sim.TrimSolver.SolveGliderTrim(cfg, v, alt);
                if (!r.Converged || r.GlideRatio <= 0) continue;
                float sink = (float)(v / r.GlideRatio); pts.Add(((float)v, sink)); sinkMax = Mathf.Max(sinkMax, sink);
            }
            if (pts.Count < 2) return t;
            int left = 90, bottom = h - 70, right = w - 40, top = 40;
            float sinkScale = Mathf.Max(0.5f, sinkMax) * 1.15f;
            int X(float v) => left + Mathf.RoundToInt((right - left) * v / vMax);
            int Y(float sink) => bottom - Mathf.RoundToInt((bottom - top) * sink / sinkScale);   // sink drawn DOWN from the top axis
            var axis = new Color(0.8f, 0.85f, 0.9f);
            Line(t, left, top, left, bottom, axis); Line(t, left, top, right, top, axis);   // speed axis along the top, sink downward (a real polar)
            for (int i = 1; i < pts.Count; i++) Line(t, X(pts[i - 1].v), top + (bottom - Y(pts[i - 1].sink)), X(pts[i].v), top + (bottom - Y(pts[i].sink)), new Color(0.6f, 0.95f, 1f), 3);
            int YY(float sink) => top + Mathf.RoundToInt((bottom - top) * sink / sinkScale);
            // min sink
            float ms = (float)sc.MinSinkMs; float sinkAtMs = 1e9f; foreach (var p2 in pts) if (Mathf.Abs(p2.v - ms) < 0.3f) sinkAtMs = p2.sink;
            if (sinkAtMs < 1e8f) Dot(t, X(ms), YY(sinkAtMs), 7, new Color(1f, 0.85f, 0.2f));
            // best L/D tangent from the origin
            float bl = (float)sc.BestLdMs; float sinkAtBl = 1e9f; foreach (var p2 in pts) if (Mathf.Abs(p2.v - bl) < 0.3f) sinkAtBl = p2.sink;
            if (sinkAtBl < 1e8f) { Line(t, X(0), YY(0), X(vMax), YY(sinkAtBl * vMax / bl), new Color(0.3f, 1f, 0.4f), 2); Dot(t, X(bl), YY(sinkAtBl), 7, new Color(0.3f, 1f, 0.4f)); }
            // wind-shifted tangent: origin moves along the speed axis by the wind (headwind → origin to the right)
            Vector3 wv = new Vector3((float)sc.WindNow.X, (float)sc.WindNow.Y, 0f);
            float wAlong = (float)(sc.WindNow.X * sc.Runway.AlongX + sc.WindNow.Y * sc.Runway.AlongY);
            float stf = (float)sc.SpeedToFlyMs; float sinkAtStf = 1e9f; foreach (var p2 in pts) if (Mathf.Abs(p2.v - stf) < 0.3f) sinkAtStf = p2.sink;
            if (Mathf.Abs(wAlong) > 0.3f && sinkAtStf < 1e8f)
            {
                float originV = -wAlong;   // headwind: wAlong < 0 → origin shifts right
                Line(t, X(Mathf.Max(0f, originV)), YY(0), X(vMax), YY(sinkAtStf * (vMax - originV) / (stf - originV)), new Color(1f, 0.6f, 0.2f), 2);
                Dot(t, X(stf), YY(sinkAtStf), 7, new Color(1f, 0.6f, 0.2f));
            }
            t.Apply(); return t;
        }

        /// <summary>Climb page 1: rate of climb against speed at full power, the Vy peak and the Vx tangent from the origin.</summary>
        public static Texture2D ClimbChart(FlyingGame.Sim.Practice.PracticeScenario sc)
        {
            int w = 1024, h = 640; var t = Blank(w, h);
            var cfg = sc.Config; double alt = sc.SurfaceM + FlyingGame.Sim.Practice.PracticeScenario.AirworkAglM;
            if (cfg.Propulsion is null) return t;
            float vMax = (float)(2.2 * sc.VsoMs), rocMax = 0.1f;
            double weight = cfg.Mass.MassKg * 9.81;
            var pts = new System.Collections.Generic.List<(float v, float roc)>();
            for (double v = sc.VsoMs * 1.02; v <= vMax; v += 0.5)
            {
                var r = FlyingGame.Sim.TrimSolver.SolveGliderTrim(cfg, v, alt);
                if (!r.Converged || r.GlideRatio <= 0) continue;
                var (tf, _) = FlyingGame.Core.PropModel.Compute(cfg.Propulsion, 1.0, new FlyingGame.Core.MathTypes.Vec3(v, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero, 1.2);
                double thrust = tf.X;
                float roc = (float)((thrust - weight / r.GlideRatio) * v / weight);
                pts.Add(((float)v, roc)); rocMax = Mathf.Max(rocMax, roc);
            }
            int left = 90, bottom = h - 60, right = w - 40, top = 40;
            int X(float v) => left + Mathf.RoundToInt((right - left) * v / vMax);
            int Y(float roc) => bottom - Mathf.RoundToInt((bottom - top) * Mathf.Max(0f, roc) / (rocMax * 1.15f));
            var axis = new Color(0.8f, 0.85f, 0.9f);
            Line(t, left, top, left, bottom, axis); Line(t, left, bottom, right, bottom, axis);
            for (int i = 1; i < pts.Count; i++) if (pts[i - 1].roc > 0 && pts[i].roc > 0) Line(t, X(pts[i - 1].v), Y(pts[i - 1].roc), X(pts[i].v), Y(pts[i].roc), new Color(0.6f, 0.95f, 1f), 3);
            Dot(t, X((float)sc.VyMs), Y((float)sc.RocAtVyMs), 7, new Color(0.3f, 1f, 0.4f));                                   // Vy
            Line(t, X(0), Y(0), X(vMax), Y((float)(sc.RocAtVxMs * vMax / System.Math.Max(1, sc.VxMs))), new Color(1f, 0.6f, 0.2f), 2);   // Vx tangent
            Dot(t, X((float)sc.VxMs), Y((float)sc.RocAtVxMs), 7, new Color(1f, 0.6f, 0.2f));
            t.Apply(); return t;
        }

        /// <summary>Glide page 2: side view with the best-glide and min-sink paths from the aircraft (and the wind-corrected one).</summary>
        public static Texture2D SideViewGlides(Transform aircraft, FlyingGame.Sim.Practice.PracticeScenario sc)
        {
            return RenderSide(aircraft, (p, ac) =>
            {
                float len = 40f;
                Vector3 fwd = ac.forward; fwd.y = 0f; fwd.Normalize();
                float gBest = 1f / Mathf.Max(3f, (float)sc.BestGlideRatio);
                var alt = sc.SurfaceM + FlyingGame.Sim.Practice.PracticeScenario.AirworkAglM;
                var rMin = FlyingGame.Sim.TrimSolver.SolveGliderTrim(sc.Config, sc.MinSinkMs, alt);
                float gMin = 1f / Mathf.Max(3f, (float)(rMin.Converged ? rMin.GlideRatio : sc.BestGlideRatio * 0.85));
                p.Arrow(ac.position, (fwd - Vector3.up * gBest).normalized * len, new Color(0.3f, 1f, 0.4f));          // best glide: farthest
                p.Arrow(ac.position, (fwd - Vector3.up * gMin).normalized * (len * 0.8f), new Color(1f, 0.85f, 0.2f)); // min sink: slower, steeper over the ground
                float wAlong = (float)(sc.WindNow.X * sc.Runway.AlongX + sc.WindNow.Y * sc.Runway.AlongY);
                if (Mathf.Abs(wAlong) > 0.3f) p.Arrow(ac.position, (fwd - Vector3.up * (1f / Mathf.Max(3f, (float)sc.SpeedToFlyGroundRatio))).normalized * len, new Color(1f, 0.6f, 0.2f));   // speed to fly, over the ground
            });
        }

        /// <summary>Climb page 2: side view with the Vx (steep) and Vy (shallower) climb paths and an obstacle ahead.</summary>
        public static Texture2D SideViewClimbs(Transform aircraft, FlyingGame.Sim.Practice.PracticeScenario sc)
        {
            return RenderSide(aircraft, (p, ac) =>
            {
                float len = 40f;
                Vector3 fwd = ac.forward; fwd.y = 0f; fwd.Normalize();
                float ax = (float)(sc.RocAtVxMs / System.Math.Max(1, sc.VxMs)), ay = (float)(sc.RocAtVyMs / System.Math.Max(1, sc.VyMs));
                p.Arrow(ac.position, (fwd + Vector3.up * ax).normalized * len, new Color(1f, 0.6f, 0.2f));   // Vx: steeper
                p.Arrow(ac.position, (fwd + Vector3.up * ay).normalized * len, new Color(0.3f, 1f, 0.4f));   // Vy: shallower, faster
                Vector3 obs = ac.position + fwd * 30f - Vector3.up * 2f;                                       // an obstacle ahead
                for (int i = -2; i <= 2; i++) p.Lines.Add((obs + ac.right * (i * 0.3f), obs + ac.right * (i * 0.3f) + Vector3.up * 9f, new Color(0.9f, 0.9f, 0.9f)));
            });
        }

        /// <summary>Climb/level/descend page 1: top view at full power — the slipstream spiral striking the fin and the nose yawing left.</summary>
        public static Texture2D TopViewPropYaw(Transform aircraft)
        {
            return Render(aircraft, 0f, 0f, true, (p, ac) =>
            {
                float span = 8f;
                Vector3 nose = ac.position + ac.forward * (span * 0.5f);
                // slipstream spiral: a helix drawn from the prop back along the fuselage
                Vector3 prev = nose;
                for (int i = 1; i <= 40; i++)
                {
                    float s2 = i / 40f; float ang = s2 * 720f * Mathf.Deg2Rad;
                    Vector3 pt = nose - ac.forward * (span * 1.2f * s2) + ac.right * (Mathf.Sin(ang) * span * 0.18f) + Vector3.up * (Mathf.Cos(ang) * span * 0.18f);
                    p.Lines.Add((prev, pt, new Color(0.7f, 0.85f, 1f))); prev = pt;
                }
                Vector3 tail = ac.position - ac.forward * (span * 0.55f);
                p.Arrow(tail, ac.right * (span * 0.4f), new Color(1f, 0.9f, 0.2f));                     // swirl pushes the fin RIGHT → nose LEFT
                p.Arc(ac.position + Vector3.up * 0.2f, Vector3.up, span * 0.5f, -20f, -100f, new Color(1f, 0.6f, 0.2f));   // nose swings LEFT
                p.Arrow(ac.position + ac.forward * (span * 0.45f) + ac.right * (span * 0.12f), ac.forward * (span * 0.35f), new Color(0.3f, 1f, 0.4f));   // descending blade: more thrust on the right
            });
        }

        /// <summary>Climb/level/descend page 2: rear view — the trimmed aircraft, with the stick force arrow and the trim tab.</summary>
        public static Texture2D RearViewTrim(Transform aircraft)
        {
            return Render(aircraft, 0f, 0f, false, (p, ac) =>
            {
                Vector3 tail = ac.position - ac.forward * 4.5f;
                p.Arrow(tail, -Vector3.up * 2.5f, new Color(1f, 0.9f, 0.2f));                          // tail load at this speed
                p.Arrow(tail - ac.forward * 0.6f, Vector3.up * 1.2f, new Color(1f, 0.6f, 0.2f));      // the trim tab deflected to carry it
            });
        }

        private static Texture2D RenderSide(Transform aircraft, System.Action<Painter, Transform> draw)
        {
            Vector3 pos0 = aircraft.position; Quaternion rot0 = aircraft.rotation;
            var go = new GameObject("LessonCamera"); var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f); cam.fieldOfView = 45f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 20000f;
            var painter = go.AddComponent<Painter>();
            Vector3 fwd = aircraft.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            cam.transform.position = aircraft.position + fwd * 12f + right * 55f;
            cam.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
            draw(painter, aircraft);
            var rt = new RenderTexture(1024, 640, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1024, 640, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1024, 640), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; Object.Destroy(rt); Object.Destroy(go);
            aircraft.position = pos0; aircraft.rotation = rot0;
            return tex;
        }

        /// <summary>Page 1: rear view, banked 30°: lift tilted with the wings, its sideways part, and the weight.</summary>
        public static Texture2D RearViewBank(Transform aircraft)
        {
            return Render(aircraft, 30f, 0f, false, (p, ac) =>
            {
                Vector3 wing = ac.position + ac.up * 0.6f;
                float L = 7f;
                Vector3 lift = ac.up * L;
                p.Arrow(wing, lift, new Color(0.3f, 1f, 0.4f));                                  // lift, tilted with the bank
                p.Arrow(wing, Vector3.up * (lift.y), new Color(0.3f, 1f, 0.4f, 0.5f), 0.03f);   // its vertical part
                Vector3 side = new Vector3(lift.x, 0f, lift.z);
                p.Arrow(wing + Vector3.up * lift.y, side, new Color(1f, 0.6f, 0.2f));              // the sideways part: the slip begins
                p.Arrow(ac.position, Vector3.down * L, new Color(0.8f, 0.8f, 0.8f));               // weight
            });
        }

        /// <summary>Page 2: top view, slipping toward the low (right) wing: relative wind from the right, tail pushed left, nose swings right.</summary>
        public static Texture2D TopViewWeathervane(Transform aircraft)
        {
            return Render(aircraft, 0f, 0f, true, (p, ac) =>
            {
                float span = 8f;
                Vector3 windFrom = (ac.forward * 0.85f + ac.right * 0.5f).normalized;                 // the air comes from the front-right in a right slip
                p.Arrow(ac.position + windFrom * (span * 1.6f), -windFrom * (span * 1.2f), Color.white);  // relative wind arriving at the aircraft
                Vector3 tail = ac.position - ac.forward * (span * 0.55f);
                p.Arrow(tail, -ac.right * (span * 0.45f), new Color(1f, 0.9f, 0.2f));               // vertical tail pushes the tail LEFT
                p.Arc(ac.position + Vector3.up * 0.2f, Vector3.up, span * 0.5f, 100f, 20f, new Color(1f, 0.6f, 0.2f));   // nose swings RIGHT
            });
        }
    }
}

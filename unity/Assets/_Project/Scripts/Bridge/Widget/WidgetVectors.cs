using System.Collections.Generic;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's force and airflow vectors, drawn into its frame (owner 2026-10-07):
    ///   FLARE — total lift (green), drag (red), weight (yellow) and thrust (blue) at the CG, the relative wind (cyan) with α,
    ///           each wheel's load (orange) once it touches;
    ///   SPIN  — for EACH wing at mid-semispan: the local relative wind (the rotation adds ω × r — the outer, rising wing
    ///           meets the air at a lower α than the inner, descending one, which is stalled deeper: autorotation) with its α,
    ///           that wing's lift and drag; weight, the total aerodynamic force, and the rotation axis (magenta).
    /// Labels are 3-D text facing the camera (so they're in the Syphon/NDI frame), sized to the view.
    /// </summary>
    public sealed class WidgetVectors : MonoBehaviour
    {
        public AeroWidget Widget;
        private Material _mat;
        private readonly List<TextMesh> _labels = new();
        private int _used;
        private Font _font;
        public static readonly Color Lift = new(0.25f, 1f, 0.35f), Drag = new(1f, 0.3f, 0.25f), Weight = new(1f, 0.9f, 0.2f),
            Thrust = new(0.35f, 0.6f, 1f), Wind = new(0.3f, 0.95f, 1f), Total = new(1f, 1f, 1f), Axis = new(1f, 0.3f, 1f), Wheel = new(1f, 0.6f, 0.15f);

        private void Start()
        {
            // The game's HUD line shader: vertex-coloured, always on top (it is in the build's always-included shaders).
            _mat = new Material(Shader.Find("FlyingGame/HudLine")) { hideFlags = HideFlags.HideAndDontSave, color = Color.white };
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---- labels (pooled TextMesh, billboarded) ----
        private void BeginLabels() { _used = 0; }
        private void Label(Vector3 at, string text, Color c, float scale, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            if (!Widget.Show["labels"]) return;
            if (anchor == TextAnchor.MiddleCenter)
            {
                // Keep a label inside the picture and below the readout (an arrow running off the frame left it half out).
                var cm = Widget.Cam; Vector3 vp = cm.WorldToViewportPoint(at);
                if (vp.z > 0 && (vp.x < 0.12f || vp.x > 0.88f || vp.y < 0.05f || vp.y > 0.84f))
                { vp.x = Mathf.Clamp(vp.x, 0.12f, 0.88f); vp.y = Mathf.Clamp(vp.y, 0.05f, 0.84f); at = cm.ViewportToWorldPoint(vp); }
            }
            // A dark drop shadow first, so the text reads over bright video too.
            float px = PixelM(at) * 2f; var cam = Widget.Cam.transform;
            Text(at + (cam.right - cam.up) * px + cam.forward * px, text, new Color(0f, 0f, 0f, 0.8f), scale, anchor);
            Text(at, text, c, scale, anchor);
        }

        private void Text(Vector3 at, string text, Color c, float scale, TextAnchor anchor)
        {
            TextMesh tm;
            if (_used < _labels.Count) tm = _labels[_used];
            else
            {
                var go = new GameObject("WidgetLabel"); go.transform.SetParent(Widget.transform, false);
                tm = go.AddComponent<TextMesh>(); tm.font = _font; tm.fontSize = 64; tm.anchor = TextAnchor.MiddleCenter;
                // The font's own material follows its dynamic atlas (a copy made at creation went blank when the atlas grew).
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = _font.material;
                _labels.Add(tm);
            }
            _used++;
            tm.gameObject.SetActive(true);
            tm.text = text; tm.color = c; tm.characterSize = scale; tm.anchor = anchor;
            tm.transform.position = at; tm.transform.rotation = Widget.Cam.transform.rotation;
        }
        private void EndLabels() { for (int i = _used; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false); }

        /// <summary>World size of one screen pixel of the 1080 frame at a point (to size arrows and text to the view).</summary>
        private float PixelM(Vector3 at)
        {
            var cam = Widget.Cam;
            float ph = Mathf.Max(1f, cam.pixelHeight);   // the viewport shrinks when the control display takes a strip
            if (cam.orthographic) return cam.orthographicSize * 2f / ph;
            float d = Vector3.Distance(cam.transform.position, at);
            return 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / ph;
        }

        private void LateUpdate()
        {
            if (Widget == null || Widget.Ac == null) return;
            BeginLabels();
            var s = Widget.ShownState; Vector3 cgU = CoordinateMap.ToUnity(s.Position);
            float px = PixelM(cgU), txt = px * 9f;
            var rd = Widget.Read();
            if (Widget.Show["readout"])
            {
                // A corner readout, in the frame's top-left.
                var cam = Widget.Cam; Vector3 corner = cam.ViewportToWorldPoint(new Vector3(0.03f, 0.97f, cam.orthographic ? 50f : Vector3.Distance(cam.transform.position, cgU)));
                string spin = Widget.Current == AeroWidget.Scenario.Cruise
                    ? $"\nALT {rd.HeightFt:F0} ft  VS {-rd.SinkFpm:+0;-0} fpm  PWR {Widget.Controls.Throttle01 * 100:F0}%"
                    : Widget.Current == AeroWidget.Scenario.Spin
                    ? $"\nYAW {rd.YawRateDps:+0;-0}°/s  ROLL {rd.RollRateDps:+0;-0}°/s\nL WING α {rd.LeftAlphaDeg:F0}°{(rd.LeftStalled ? " STALLED" : "")}\nR WING α {rd.RightAlphaDeg:F0}°{(rd.RightStalled ? " STALLED" : "")}"
                    : $"\nSINK {rd.SinkFpm:F0} fpm  HT {rd.HeightFt:F0} ft{(rd.OnGround ? "  ON THE WHEELS" : "")}";
                string rev = Widget.Paused && Widget.Show["review"] ? $"REVIEW {(Widget.Review.OffsetMs / 1000.0):+0.0;-0.0;0.0} s{(Widget.Review.Playing ? (Widget.Review.Direction == "reverse" ? "  ◀ " : "  ▶ ") + "×" + Widget.Review.Rate : "")}\n" : "";
                Label(corner, rev + $"{Widget.AircraftId.Replace("-like", "")}   {rd.Kias:F0} KIAS   α {rd.AlphaDeg:F1}°   PITCH {rd.PitchDeg:+0;-0}°{spin}", Color.white, txt * 0.42f, TextAnchor.UpperLeft);
            }
            EndLabels();
        }

        // Lines are drawn after the camera renders the airframe (into the same frame).
        private void OnPostRender()
        {
            if (Widget == null || Widget.Ac == null || _mat == null) return;
            var ac = Widget.Ac; var s = Widget.ShownState; var cfg = ac.Config; Vec3 cg = cfg.Mass.CgVec();
            Vector3 cgU = CoordinateMap.ToUnity(s.Position);
            float px = PixelM(cgU);
            double weightN = ac.MassProperties.MassKg * 9.81;
            // One weight's worth = ~260 px: every force reads against the weight.
            float perN = 260f * px / (float)weightN;
            Vector3 W(Vec3 body) => CoordinateMap.ToUnity(s.Attitude.Rotate(body));
            Vector3 P(Vec3 bodyPos) => CoordinateMap.ToUnity(s.Position + s.Attitude.Rotate(bodyPos - cg));

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            var samples = Widget.ShownForces;   // review mode: the shown frame's forces
            Vec3 lift = Vec3.Zero, drag = Vec3.Zero, thrust = Vec3.Zero, total = Vec3.Zero, wL = Vec3.Zero, wR = Vec3.Zero, dL = Vec3.Zero, dR = Vec3.Zero;
            foreach (var f in samples)
            {
                switch (f.Kind)
                {
                    case "lift": lift += f.ForceBody; total += f.ForceBody; if (f.PosBody.Y < -0.3) wL += f.ForceBody; else if (f.PosBody.Y > 0.3) wR += f.ForceBody; break;
                    case "drag": drag += f.ForceBody; total += f.ForceBody; if (f.PosBody.Y < -0.3) dL += f.ForceBody; else if (f.PosBody.Y > 0.3) dR += f.ForceBody; break;
                    case "fuselage": case "spoiler": total += f.ForceBody; break;
                    case "thrust": thrust += f.ForceBody; break;
                    case "gear":
                        if (Widget.Show["wheels"] && f.ForceBody.Length > weightN * 0.02) Arrow(P(f.PosBody), W(f.ForceBody) * perN, Wheel, px);
                        break;
                }
                if (Widget.Show["strips"] && (f.Kind == "lift" || f.Kind == "drag")) Arrow(P(f.PosBody), W(f.ForceBody) * perN * 4f, f.Kind == "lift" ? Lift * 0.7f : Drag * 0.7f, px * 0.6f);
            }
            Vector3 weightU = Vector3.down * (float)weightN * perN;
            if (Widget.Current != AeroWidget.Scenario.Spin)   // flare and cruise: the forces at the CG
            {
                if (Widget.Show["lift"]) Arrow(cgU, W(lift) * perN, Lift, px);
                if (Widget.Show["drag"]) Arrow(cgU, W(drag) * perN * 3f, Drag, px);   // drag ×3 so it reads
                if (Widget.Show["weight"]) Arrow(cgU, weightU, Weight, px);
                if (Widget.Show["thrust"] && thrust.Length > 1) Arrow(cgU, W(thrust) * perN * 3f, Thrust, px);
                if (Widget.Show["wind"])
                {
                    // Relative wind: arriving at the CG from ahead along the flight path (an arrow pointing INTO the aircraft).
                    Vector3 v = CoordinateMap.ToUnity(s.Attitude.Rotate(s.Velocity)); float len = 200f * px;
                    Arrow(cgU - v.normalized * len * 1.3f, v.normalized * len, Wind, px);
                    // The chord line through the CG (for α against the wind).
                    Vector3 fwd = W(new Vec3(1, 0, 0)); Line(cgU - fwd * len * 0.6f, cgU + fwd * len * 0.8f, new Color(1f, 1f, 1f, 0.55f));
                }
            }
            else
            {
                double half = Widget.SpanM * 0.5;
                Vec3 vAir = s.Velocity - s.Attitude.Conjugate().Rotate(FlyingGame.Core.Atmosphere.WindAtPosition(s.Position));
                foreach (double y in new[] { -half * 0.5, half * 0.5 })
                {
                    Vec3 at = cg + new Vec3(0, y, 0);
                    Vector3 pU = P(at);
                    if (Widget.Show["wind"])
                    {
                        Vec3 local = vAir + Vec3.Cross(s.Rates, new Vec3(0, y, 0));   // the airflow there: the rotation adds ω × r
                        Vector3 v = W(local).normalized; float len = 170f * px;
                        Arrow(pU - v * len * 1.25f, v * len, Wind, px);
                    }
                    if (Widget.Show["lift"]) Arrow(pU, W(y < 0 ? wL : wR) * perN * 1.6f, Lift, px);
                    if (Widget.Show["drag"]) Arrow(pU, W(y < 0 ? dL : dR) * perN * 1.6f, Drag, px);
                }
                if (Widget.Show["weight"]) Arrow(cgU, weightU, Weight, px);
                if (Widget.Show["total"]) Arrow(cgU, W(total) * perN, Total, px);
                if (Widget.Show["axis"] && s.Rates.Length > 0.2)
                {
                    Vector3 ax = W(s.Rates).normalized * Mathf.Max(Widget.SpanM, Widget.LengthM) * 0.9f;
                    Line(cgU - ax, cgU + ax, Axis);
                }
            }
            DrawWorld(px);
            GL.End();
            GL.PopMatrix();

            // Labels at the arrow tips (placed here, drawn by the camera next frame — a frame's lag is invisible).
            PlaceForceLabels(cgU, lift, drag, thrust, total, weightU, perN, px, W, P, cg, wL, wR);
        }

        private void PlaceForceLabels(Vector3 cgU, Vec3 lift, Vec3 drag, Vec3 thrust, Vec3 total, Vector3 weightU, float perN, float px,
                                      System.Func<Vec3, Vector3> W, System.Func<Vec3, Vector3> P, Vec3 cg, Vec3 wL, Vec3 wR)
        {
            // Done in LateUpdate's pool next frame: stash the positions.
            _pending.Clear();
            var rd = Widget.Read(); float t = px * 9f;
            if (Widget.Current != AeroWidget.Scenario.Spin)
            {
                if (Widget.Show["lift"]) _pending.Add((cgU + W(lift) * perN * 1.08f, "LIFT", Lift, cgU));
                if (Widget.Show["weight"]) _pending.Add((cgU + weightU * 1.08f, "WEIGHT", Weight, null));
                if (Widget.Show["drag"]) _pending.Add((cgU + W(drag) * perN * 3.3f - Widget.Cam.transform.up * t * 1.4f, "DRAG ×3", Drag, cgU));   // just below the wind's label
                if (Widget.Show["wind"]) _pending.Add((cgU - W(Widget.ShownState.Velocity).normalized * 150f * px + Widget.Cam.transform.up * t * 1.4f, $"RELATIVE WIND  α {rd.AlphaDeg:F1}°", Wind, cgU));   // mid-arrow, just above it
            }
            else
            {
                double half = Widget.SpanM * 0.5;
                string lt = $"L WING α {rd.LeftAlphaDeg:F0}°{(rd.LeftStalled ? " STALLED" : "")}", rt = $"R WING α {rd.RightAlphaDeg:F0}°{(rd.RightStalled ? " STALLED" : "")}";
                if (Widget.View == "body") { }   // airplane-fixed, looking along the span: the tips line up — the readout carries each wing's α
                else
                {
                    // Out past each wingtip (clear of the airframe): the left / right wing's α at mid-semispan.
                    _pending.Add((P(cg + new Vec3(0, -half * 1.35, 0)), lt, rd.LeftStalled ? Drag : Lift, null));
                    _pending.Add((P(cg + new Vec3(0, half * 1.35, 0)), rt, rd.RightStalled ? Drag : Lift, null));
                }
                if (Widget.Show["weight"]) _pending.Add((cgU + weightU * 1.08f, "WEIGHT", Weight, null));
                if (Widget.Show["total"]) _pending.Add((cgU + W(total) * perN * 1.08f, "TOTAL AERO", Total, cgU));
            }
            _pendingScale = t;
        }
        private readonly List<(Vector3 at, string text, Color c, Vector3? cg)> _pending = new();
        private float _pendingScale;

        private void Update()
        {
            // Draw last frame's force labels through the pool (LateUpdate adds the readout after these).
        }

        private void OnPreCull()
        {
            if (Widget == null) return;
            // Re-place the force labels for this frame's render (the readout was placed in LateUpdate).
            int keep = _used;
            foreach (var p in _pending)
            {
                // An arrow seen nearly end-on (drag and the relative wind from behind) puts its label on the airplane: skip it.
                if (p.cg.HasValue)
                {
                    var cam = Widget.Cam; Vector3 a = cam.WorldToScreenPoint(p.at), b = cam.WorldToScreenPoint(p.cg.Value);
                    if (a.z <= 0 || new Vector2(a.x - b.x, a.y - b.y).magnitude < 75f * cam.pixelHeight / AeroWidget.FrameSize) continue;
                }
                Label(p.at, p.text, p.c, _pendingScale * 0.45f);   // small labels (owner's pick)
            }
            EndLabels();
            _used = keep;
        }

        /// <summary>World references (protocol 5): FINAL — the ground line, the runway as a thick white line from its threshold,
        /// the threshold mark and the aim point; spin and cruise — the horizon and a ground grid, so the airplane-fixed view
        /// shows the world turning around the airplane. Pixel-thick at any zoom.</summary>
        private void DrawWorld(float px)
        {
            var w = Widget;
            if (w.Current == AeroWidget.Scenario.Flare)
            {
                if (w.Condition != "final") return;
                float L = (float)w.Runway.LengthM, z0 = -L / 2f, z1 = L / 2f, aim = z0 + (float)FlyingGame.Sim.Practice.PracticeScenario.NumbersPastThresholdM;
                Vector3 up = Vector3.up;
                Line(new Vector3(0, 0, -20000), new Vector3(0, 0, 20000), new Color(1, 1, 1, 0.45f));                  // the ground
                for (int k = 0; k <= 4; k++) Line(new Vector3(0, -k * px, z0), new Vector3(0, -k * px, z1), Color.white);   // the runway: 5 px thick
                for (int k = -1; k <= 1; k++) Line(new Vector3(0, 0, z0 + k * px), new Vector3(0, 14 * px, z0 + k * px), Color.white);   // the threshold
                for (int k = 0; k <= 8; k++) Line(new Vector3(0, k * px * 0.5f, aim), new Vector3(0, k * px * 0.5f, aim + 45f), Color.white);   // the aim point (a 45 m bar)
                return;
            }
            if (!w.Show["horizon"]) return;
            var cam = w.Cam.transform.position;
            // The horizon: a circle at eye height far out (on a flat world it sits on the eye's level).
            const float R = 15000f; Color hz = new(0.85f, 0.92f, 1f, 0.75f);
            Vector3 prev = cam + new Vector3(R, 0, 0);
            for (int i = 1; i <= 96; i++) { float a = i * Mathf.PI * 2f / 96f; var p = cam + new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R); Line(prev, p, hz); prev = p; }
            // The ground: a 250 m grid fixed to the earth, around the aircraft.
            Vector3 ac = CoordinateMap.ToUnity(w.ShownState.Position);
            const float G = 250f, E = 4000f; Color gc = new(0.95f, 0.8f, 0.55f, 0.4f);
            float cx = Mathf.Round(ac.x / G) * G, cz = Mathf.Round(ac.z / G) * G;
            for (float d = -E; d <= E; d += G)
            {
                Line(new Vector3(cx + d, 0, cz - E), new Vector3(cx + d, 0, cz + E), gc);
                Line(new Vector3(cx - E, 0, cz + d), new Vector3(cx + E, 0, cz + d), gc);
            }
        }

        // ---- GL helpers ----
        private static void Line(Vector3 a, Vector3 b, Color c) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
        private void Arrow(Vector3 from, Vector3 vec, Color c, float px)
        {
            if (vec.sqrMagnitude < 1e-8f) return;
            Vector3 to = from + vec;
            // A thicker shaft: three parallel lines a pixel apart (GL lines are 1 px).
            Vector3 camUp = Widget.Cam.transform.up, camRight = Widget.Cam.transform.right;
            Vector3 side = Vector3.Cross(vec.normalized, Widget.Cam.transform.forward).normalized;
            if (side.sqrMagnitude < 1e-6f) side = camRight;
            for (int k = -1; k <= 1; k++) Line(from + side * k * px, to + side * k * px, c);
            float head = Mathf.Min(vec.magnitude * 0.3f, 22f * px);
            Vector3 back = -vec.normalized * head;
            for (int k = -1; k <= 1; k++) { Line(to + side * k * px * 0.5f, to + back + side * head * 0.45f, c); Line(to + side * k * px * 0.5f, to + back - side * head * 0.45f, c); }
        }
    }
}

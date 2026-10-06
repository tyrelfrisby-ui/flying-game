using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Flight path vector (owner 2026-09-10): a green curve from the aircraft to where it will be, through the AIR, in
    /// <see cref="HorizonSec"/> if it keeps doing what it is doing now — the current velocity carried forward under the
    /// current acceleration, with the acceleration's TURNING part rotating along with the velocity (so a loop or a
    /// turn predicts an arc, not a parabola) and its along-track part changing the speed. A cone arrowhead marks the end.
    /// </summary>
    public sealed class FlightPathVector : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float HorizonSec = 3f;   // owner 2026-09-10: three seconds
        public int Segments = 40;
        public Color Colour = new(0.25f, 1f, 0.35f, 0.95f);   // owner 2026-09-14: green, no arrowhead

        private LineRenderer _line;
        private GameObject _cone;
        private GameObject _target;
        private Vector3 _prevVel, _accel;
        private bool _havePrev;

        private void Start()
        {
            var go = new GameObject("FlightPathVector");
            _line = go.AddComponent<LineRenderer>();
            _line.material = new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = Colour };
            _line.startColor = _line.endColor = Colour;
            _line.useWorldSpace = true; _line.alignment = LineAlignment.View;
            _line.numCornerVertices = 2; _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cone = new GameObject("FlightPathArrow");
            _cone.AddComponent<MeshFilter>().sharedMesh = Cone(1f, 2.2f, 16);
            _cone.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = Colour };
            _target = new GameObject("ImpactPoint");
            _target.AddComponent<MeshFilter>().sharedMesh = TargetMesh();
            _target.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = Colour };
            _target.SetActive(false);
        }

        /// <summary>
        /// Point of impact (owner 2026-10-05: "have it also show a point of impact on the surface … a little target type
        /// symbol"): the straight-line projection of the current path OVER THE GROUND to where it meets the terrain, a deck
        /// or the water — the aim point of the descent, as the flight-path marker on a HUD shows it. Only while descending.
        /// </summary>
        private void UpdateImpact(Vector3 pos, Vector3 groundVel)
        {
            if (groundVel.y > -0.2f) { _target.SetActive(false); return; }
            float Surface(Vector3 p)
            {
                var sp = CoordinateMap.ToSim(p);
                double g = FlyingGame.Core.WorldTerrain.GroundHeightAt(sp.X, sp.Y);
                double? w = FlyingGame.Core.FloatHydro.WaterSurfaceAt(sp.X, sp.Y);
                return (float)(w.HasValue && w.Value > g ? w.Value : g);
            }
            Vector3 dir = groundVel.normalized;
            float step = 4f, maxDist = 6000f, prev = 0f;
            float t = 0f; bool hit = false;
            for (t = step; t <= maxDist; t += step)
            {
                Vector3 p = pos + dir * t;
                if (p.y <= Surface(p)) { hit = true; break; }
                prev = t;
                step = Mathf.Min(40f, step * 1.15f);
            }
            if (!hit) { _target.SetActive(false); return; }
            float lo = prev, hi = t;
            for (int i = 0; i < 12; i++) { float m = 0.5f * (lo + hi); Vector3 p = pos + dir * m; if (p.y <= Surface(p)) hi = m; else lo = m; }
            Vector3 at = pos + dir * hi;
            at.y = Surface(at) + 0.25f;   // just above the surface (and the runway slabs)
            _target.SetActive(true);
            _target.transform.position = at;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            _target.transform.rotation = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
            Camera cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, at) : 50f;
            _target.transform.localScale = Vector3.one * Mathf.Clamp(camDist * 0.04f, 2f, 40f);   // a few % of the view: visible near and far
        }

        /// <summary>A flat target lying on the surface (local xz, radius 1): a ring and a cross with a gap at the centre.</summary>
        private static Mesh TargetMesh()
        {
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });   // both faces
            }
            const int seg = 32; const float r0 = 0.82f, r1 = 1f, w = 0.07f;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                Quad(new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0), new Vector3(Mathf.Cos(a0) * r1, 0f, Mathf.Sin(a0) * r1),
                     new Vector3(Mathf.Cos(a1) * r1, 0f, Mathf.Sin(a1) * r1), new Vector3(Mathf.Cos(a1) * r0, 0f, Mathf.Sin(a1) * r0));
            }
            foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
            {
                Vector3 along = new Vector3(dx, 0f, dz), side = new Vector3(-dz, 0f, dx) * w;
                Quad(along * 0.25f - side, along * 1.25f - side, along * 1.25f + side, along * 0.25f + side);   // ticks through the ring
            }
            var m = new Mesh { name = "ImpactTarget" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        private void LateUpdate()
        {
            bool show = SessionSettings.ShowFlightPath && Driver != null && Driver.Sim != null && !SessionSettings.MenuOpen && !(GetComponent<PilotEgress>()?.PilotOut ?? false);
            _line.enabled = show; _cone.SetActive(false);   // no arrowhead (owner)
            if (!show) { _havePrev = false; _target.SetActive(false); return; }

            // AIR-relative (owner): the path through the air, not over the ground — the wind is left out, so in a
            // crosswind the vector points where the nose is going through the air mass.
            Vector3 pos = transform.position;
            // ... except in ground-reference mode (runway lessons, low altitude), where the path over the GROUND is what counts.
            Vector3 vel = Driver.GroundReference ? Driver.WorldVelocityUnity : Driver.AirVelocityUnity;
            float dt = Time.deltaTime;
            if (_havePrev && dt > 1e-4f)
            {
                Vector3 a = (vel - _prevVel) / dt;
                _accel = Vector3.Lerp(_accel, a, 1f - Mathf.Exp(-dt / 0.12f));   // light smoothing: frame jitter, not lag
            }
            _prevVel = vel; _havePrev = true;

            float speed = vel.magnitude;
            if (speed < 1f) { _line.enabled = false; _cone.SetActive(false); return; }
            // Decompose: along-track acceleration changes the speed; the normal part is a turning rate of the velocity.
            Vector3 dir = vel / speed;
            float aAlong = Vector3.Dot(_accel, dir);
            Vector3 aNormal = _accel - dir * aAlong;
            Vector3 omega = Vector3.Cross(dir, aNormal) / Mathf.Max(speed, 1f);   // rad/s, axis of the velocity's rotation

            int n = Mathf.Max(4, Segments);
            float h = HorizonSec / n;
            var pts = new Vector3[n + 1];
            Vector3 p = pos, v = vel;
            pts[0] = p;
            Quaternion step = omega.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(omega.magnitude * Mathf.Rad2Deg * h, omega.normalized) : Quaternion.identity;
            for (int i = 1; i <= n; i++)
            {
                v = step * v;
                float sp = Mathf.Max(0.5f, v.magnitude + aAlong * h);
                v = v.normalized * sp;
                p += v * h;
                pts[i] = p;
            }
            float width = Mathf.Clamp(speed * 0.012f, 0.25f, 1.2f);
            _line.positionCount = n + 1; _line.SetPositions(pts);
            _line.startWidth = width; _line.endWidth = width;
            Vector3 tipDir = (pts[n] - pts[n - 1]).normalized;
            _cone.transform.position = pts[n];
            _cone.transform.rotation = Quaternion.LookRotation(tipDir, Vector3.up);
            _cone.transform.localScale = Vector3.one * (width * 3.2f);
            UpdateImpact(pos, Driver.WorldVelocityUnity);   // the impact point is where the GROUND track meets the surface
        }

        /// <summary>Cone along local +z, base radius r, length l.</summary>
        private static Mesh Cone(float r, float l, int seg)
        {
            var v = new Vector3[seg + 2]; var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2f / seg; v[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f); }
            v[seg] = new Vector3(0f, 0f, l); v[seg + 1] = Vector3.zero;
            for (int i = 0; i < seg; i++) { int j = (i + 1) % seg; t.Add(i); t.Add(seg); t.Add(j); t.Add(j); t.Add(seg + 1); t.Add(i); }
            var m = new Mesh(); m.vertices = v; m.triangles = t.ToArray(); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
    }
}

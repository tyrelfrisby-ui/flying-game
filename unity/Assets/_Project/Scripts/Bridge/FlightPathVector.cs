using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Flight path vector (owner 2026-09-10): a magenta curve from the aircraft to where it will be in
    /// <see cref="HorizonSec"/> if it keeps doing what it is doing now — the current velocity carried forward under the
    /// current acceleration, with the acceleration's TURNING part rotating along with the velocity (so a loop or a
    /// turn predicts an arc, not a parabola) and its along-track part changing the speed. A cone arrowhead marks the end.
    /// </summary>
    public sealed class FlightPathVector : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float HorizonSec = 5f;
        public int Segments = 40;
        public Color Colour = new(1f, 0.15f, 1f, 0.95f);

        private LineRenderer _line;
        private GameObject _cone;
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
        }

        private void LateUpdate()
        {
            bool show = SessionSettings.ShowFlightPath && Driver != null && Driver.Sim != null && !SessionSettings.MenuOpen && !(GetComponent<PilotEgress>()?.PilotOut ?? false);
            _line.enabled = show; _cone.SetActive(show);
            if (!show) { _havePrev = false; return; }

            Vector3 pos = transform.position;
            Vector3 vel = Driver.WorldVelocityUnity;
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

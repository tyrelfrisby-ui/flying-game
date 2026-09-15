using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// Weight on wheels, drawn on the aircraft in the side-view exercises: at each wheel an UP arrow for the ground's push on
    /// the tyre (the aircraft's weight ≈ 6 m of arrow) and an AFT arrow for the braking force, so the user can see how
    /// elevator position shifts weight between the wheels and how braking loads the mains (owner 2026-09-15).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class WheelForceOverlay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public PracticeController Practice;
        public float WeightLengthM = 6f;
        private Material _mat;

        private void Awake()
        {
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private void OnPostRender()
        {
            if (_mat == null || Driver == null || Driver.Sim == null || SessionSettings.MenuOpen) return;
            if (Practice == null || !Practice.Active || Practice.Scenario == null || !Practice.Scenario.SideView) return;
            var ac = Driver.Sim.Aircraft;
            var samples = ac.LastForces;
            if (samples == null || samples.Count == 0) return;
            var st = ac.State; var cg = ac.Config.Mass.CgVec();
            float weight = (float)(ac.MassProperties.MassKg * 9.81);
            float scale = WeightLengthM / Mathf.Max(1f, weight);
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (var f in samples)
            {
                if (f.Kind != "gear") continue;
                Vector3 p0 = CoordinateMap.ToUnity(st.Position + st.Attitude.Rotate(f.PosBody - cg));
                // Ground push on the tyre: the vertical part (world up) and the braking part (against the aircraft's motion).
                Vector3 fw = CoordinateMap.ToUnity(st.Attitude.Rotate(f.ForceBody));
                float up = Mathf.Max(0f, fw.y);
                Vector3 fwd = CoordinateMap.ToUnity(st.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0))); fwd.y = 0f; fwd.Normalize();
                float aft = Mathf.Max(0f, -Vector3.Dot(fw, fwd));
                if (up > 1f) Arrow(p0, Vector3.up * (up * scale), new Color(0.3f, 0.9f, 1f));
                if (aft > 5f) Arrow(p0, -fwd * (aft * scale), new Color(1f, 0.3f, 0.25f));
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void Arrow(Vector3 from, Vector3 v, Color c)
        {
            Vector3 to = from + v;
            GL.Color(c); GL.Vertex(from); GL.Vertex(to);
            // head: two short barbs
            Vector3 d = v.normalized; Vector3 side = Vector3.Cross(d, Vector3.forward); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(d, Vector3.right); side.Normalize();
            float h = Mathf.Min(0.35f, v.magnitude * 0.25f);
            GL.Vertex(to); GL.Vertex(to - d * h + side * h * 0.6f);
            GL.Vertex(to); GL.Vertex(to - d * h - side * h * 0.6f);
        }
    }
}

using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// The stall, side view (owner 2026-09-15): the wing's lift (green, at the wing), the tail's force (yellow, at the tail),
    /// the relative wind arriving at the tail (white — as the aircraft sinks it comes from below, the tail force changes and
    /// the nose drops by itself), and the weight at the CG (grey). Drawn only in the stall side-view exercise.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class StallVectorOverlay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public PracticeController Practice;
        public float WeightLengthM = 5f;
        private Material _mat;
        private string _cfgId; private float _tailCutX;

        private void Awake()
        {
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private void OnPostRender()
        {
            if (_mat == null || Driver?.Sim == null || SessionSettings.MenuOpen) return;
            if (Practice == null || !Practice.Active || Practice.Scenario == null || Practice.Scenario.Kind != FlyingGame.Sim.Practice.PracticeKind.StallSideView) return;
            var ac = Driver.Sim.Aircraft;
            var samples = ac.LastForces;
            if (samples == null || samples.Count == 0) return;
            if (_cfgId != ac.Config.Id)
            {
                // Tail = anything aft of the wing's trailing edge by more than a chord.
                _cfgId = ac.Config.Id; float minTe = float.MaxValue, chord = 1f;
                foreach (var sf in ac.Config.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (var st in sf.Strips) { minTe = Mathf.Min(minTe, (float)(st.Pos[0] - 0.75 * st.Chord)); chord = (float)st.Chord; }
                _tailCutX = minTe == float.MaxValue ? -1.5f : minTe - chord;
            }
            var st0 = ac.State; var cg = ac.Config.Mass.CgVec();
            float weight = (float)(ac.MassProperties.MassKg * 9.81);
            float scale = WeightLengthM / Mathf.Max(1f, weight);
            FlyingGame.Core.MathTypes.Vec3 wingF = FlyingGame.Core.MathTypes.Vec3.Zero, tailF = FlyingGame.Core.MathTypes.Vec3.Zero, wingP = FlyingGame.Core.MathTypes.Vec3.Zero, tailP = FlyingGame.Core.MathTypes.Vec3.Zero;
            double wingW = 0, tailW = 0;
            foreach (var f in samples)
            {
                if (f.Kind != "lift" && f.Kind != "drag") continue;
                double w = f.ForceBody.Length;
                if (f.PosBody.X < _tailCutX) { tailF += f.ForceBody; tailP += f.PosBody * w; tailW += w; }
                else { wingF += f.ForceBody; wingP += f.PosBody * w; wingW += w; }
            }
            if (wingW > 0) wingP = wingP / wingW; if (tailW > 0) tailP = tailP / tailW;
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            Vector3 P(FlyingGame.Core.MathTypes.Vec3 body) => CoordinateMap.ToUnity(st0.Position + st0.Attitude.Rotate(body - cg));
            Vector3 F(FlyingGame.Core.MathTypes.Vec3 body) => CoordinateMap.ToUnity(st0.Attitude.Rotate(body));
            if (wingW > 0) Arrow(P(wingP), F(wingF) * scale, new Color(0.3f, 1f, 0.4f));
            if (tailW > 0) Arrow(P(tailP), F(tailF) * scale * 3f, new Color(1f, 0.9f, 0.2f));   // ×3: the tail's force is small next to the wing's
            Arrow(CoordinateMap.ToUnity(st0.Position), Vector3.down * WeightLengthM, new Color(0.75f, 0.75f, 0.75f));
            // Relative wind at the tail: arrives along −(air velocity); drawn as an arrow ENDING at the tail.
            Vector3 air = Driver.AirVelocityUnity;
            if (air.magnitude > 1f && tailW > 0) { Vector3 tp = P(tailP); Arrow(tp - air.normalized * 4f, air.normalized * 3.5f, Color.white); }
            GL.End();
            GL.PopMatrix();
        }

        private static void Arrow(Vector3 from, Vector3 v, Color c)
        {
            if (v.magnitude < 0.05f) return;
            Vector3 to = from + v;
            GL.Color(c); GL.Vertex(from); GL.Vertex(to);
            Vector3 d = v.normalized; Vector3 side = Vector3.Cross(d, Vector3.forward); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(d, Vector3.right); side.Normalize();
            float h = Mathf.Min(0.4f, v.magnitude * 0.25f);
            GL.Vertex(to); GL.Vertex(to - d * h + side * h * 0.6f);
            GL.Vertex(to); GL.Vertex(to - d * h - side * h * 0.6f);
        }
    }
}

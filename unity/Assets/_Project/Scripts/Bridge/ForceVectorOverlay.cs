using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Every force the physics model applied this step, drawn on the airframe in world space: lift (green) and
    /// drag (red) per wing/tail strip, section pitching moments (yellow bars along the chord), fuselage crossflow
    /// (orange), spoiler drag (orange), thrust (magenta), wheel loads (cyan), airframe contact (pink), the tow
    /// rope (white) and weight at the CG (grey). Arrow length: the aircraft's weight ≈ 12 m, so a wing strip's
    /// share is a short tooth and the total lift comb sums to the weight in level flight.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ForceVectorOverlay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float WeightLengthM = 12f;
        private Material _mat;

        private void Awake()
        {
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private static Color ColourFor(string kind) => kind switch
        {
            "lift" => new Color(0.3f, 1f, 0.4f), "drag" => new Color(1f, 0.25f, 0.2f), "moment" => new Color(1f, 0.9f, 0.2f),
            "fuselage" => new Color(1f, 0.6f, 0.2f), "spoiler" => new Color(1f, 0.5f, 0.1f), "thrust" => new Color(1f, 0.3f, 1f),
            "gear" => new Color(0.3f, 0.9f, 1f), "contact" => new Color(1f, 0.5f, 0.8f), "rope" => Color.white, "weight" => new Color(0.75f, 0.75f, 0.75f),
            _ => Color.white,
        };

        private void OnPostRender()
        {
            if (!SessionSettings.ShowForceVectors || Driver == null || Driver.Sim == null || _mat == null || SessionSettings.MenuOpen) return;
            var ac = Driver.Sim.Aircraft;
            var samples = ac.LastForces;
            if (samples == null || samples.Count == 0) return;
            var st = ac.State; var cg = ac.Config.Mass.CgVec();
            float weight = (float)(ac.MassProperties.MassKg * 9.81);
            float scale = WeightLengthM / Mathf.Max(1f, weight);          // metres per newton
            float mScale = WeightLengthM / Mathf.Max(1f, weight * 2f);    // metres per N·m (section moments are small)
            Vector3 camUp = transform.up;

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (var f in samples)
            {
                Vector3 p0 = CoordinateMap.ToUnity(st.Position + st.Attitude.Rotate(f.PosBody - cg));
                if (f.Kind == "tailflow") continue;   // a flow diagnostic (the widget's tail winds), not a force
                Color c = ColourFor(f.Kind);
                if (f.Kind == "moment")
                {
                    // Pitching moment: a bar along the chord line, forward for nose-up (+My), aft for nose-down.
                    float m = (float)f.MomentBody.Y;
                    if (Mathf.Abs(m) < 1e-3f) continue;
                    Vector3 dir = CoordinateMap.ToUnity(st.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0)));
                    Vector3 p1 = p0 + dir * (m * mScale);
                    GL.Color(c); GL.Vertex(p0); GL.Vertex(p1);
                    continue;
                }
                Vector3 fw = CoordinateMap.ToUnity(st.Attitude.Rotate(f.ForceBody));
                float len = fw.magnitude * scale;
                if (len < 0.02f) continue;
                Vector3 d = fw / fw.magnitude;
                Vector3 tip = p0 + d * len;
                GL.Color(c); GL.Vertex(p0); GL.Vertex(tip);
                // Arrowhead: two short barbs.
                Vector3 side = Vector3.Cross(d, camUp).normalized * Mathf.Min(0.25f, len * 0.2f);
                Vector3 back = -d * Mathf.Min(0.35f, len * 0.25f);
                GL.Vertex(tip); GL.Vertex(tip + back + side);
                GL.Vertex(tip); GL.Vertex(tip + back - side);
            }
            GL.End();
            GL.PopMatrix();
        }
    }
}

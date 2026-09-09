using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Air Racing: feeds the aircraft position to the Core scorer and shows the race line in the HUD.</summary>
    public sealed class RaceController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public AirRace Race { get; private set; }
        public bool Active { get; private set; }

        public void Begin()
        {
            Race = new AirRace(); Race.Reset(); Active = true;
            foreach (PylonTopBurst top in WorldBuilder.PylonTops.Values) if (top != null) top.ResetTop();
        }
        public void End() { Active = false; }

        private AirRace _strikeRace;   // pylon strikes are live even outside a race (free flight through the course)

        private void Update()
        {
            if (Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft; var s = ac.State; var q = s.Attitude;
            if (Active && Race != null)
            {
                double bank = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                Race.Update(s.Position, bank, Time.deltaTime);
            }
            // Wing tips against the pylons.
            AirRace strikes = Active && Race != null ? Race : (_strikeRace ??= new AirRace());
            double half = 0; foreach (var sf in ac.Config.Surfaces) foreach (var st in sf.Strips) half = System.Math.Max(half, System.Math.Abs(st.Pos[1]));
            if (ac.IsLost(FlyingGame.Core.AirframeComponent.WingLeft) || ac.IsLost(FlyingGame.Core.AirframeComponent.WingRight)) half *= 0.3;
            Vec3 left = s.Position + q.Rotate(new Vec3(0, -half, 0)), right = s.Position + q.Rotate(new Vec3(0, half, 0));
            var hit = strikes.CheckPylonStrike(left, right, Time.deltaTime);
            if (hit.HasValue)
            {
                if (WorldBuilder.PylonTops.TryGetValue(hit.Value, out PylonTopBurst top) && top != null) top.Launch();
                Driver.GetComponent<FlightAudio>()?.PylonBurst();
            }
        }

        public string Line => !Active || Race == null ? null
            : Race.Finished ? $"AIR RACE  {Race.LastEvent}   (penalties {Race.PenaltySec:F0} s)"
            : $"AIR RACE  next {Race.Next + 1}/{RaceCourse.Elements.Length}   {Race.TotalSec:F1} s   {Race.LastEvent}";
    }

    /// <summary>STOL contest on the gravel strip: touchdown at/after the line, stop short. Shows the score.</summary>
    public sealed class StolController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public StolRun Run { get; private set; }
        public bool Active { get; private set; }

        public void Begin(WorldTerrain.Airport a) { Run = StolRun.ForAirport(a); Active = true; }
        public void End() { Active = false; }

        private void Update()
        {
            if (!Active || Run == null || Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft;
            var s = ac.State;
            bool onGround = LandingGear.AnyMainWheelOnGround(ac.Config, s);
            var vW = s.Attitude.Rotate(s.Velocity);
            double gs = System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            Run.Update(s.Position, onGround, gs);
        }

        public string Line => !Active || Run == null ? null : Run.Phase switch
        {
            StolRun.Phases.Approach => "STOL  land at or past the white line, stop short",
            StolRun.Phases.Rolling => $"STOL  touchdown +{Run.TouchdownPastLineM * 3.28084:F0} ft — stop!",
            StolRun.Phases.Stopped => $"STOL  SCORE {Run.StopPastLineM * 3.28084:F0} ft   (touchdown +{Run.TouchdownPastLineM * 3.28084:F0} ft)",
            _ => $"STOL  DQ — {Run.Reason}",
        };
    }

    /// <summary>Crop dusting over the farmer's field: spray covers the cells under a low pass, the field greens up
    /// behind the aircraft, every crossing of the power line UNDER the wires is a pass; a wire strike ends the
    /// run and takes the speed out of the aircraft.</summary>
    public sealed class CropDustController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public CropDust Run { get; private set; }
        public bool Active { get; private set; }
        private ParticleSystem _spray;
        private Material _mat;
        private bool _struck;

        public void Begin()
        {
            Run = new CropDust(); Active = true; _struck = false;
            // Fresh field colours.
            Mesh m = WorldBuilder.CropFieldMesh;
            if (m != null)
            {
                var cols = m.colors; int ny = CropField.CellsY;
                for (int v = 0; v < cols.Length; v++) { int cell = v / 4, j = cell % ny; Color c = j % 2 == 0 ? WorldBuilder.Ploughed : WorldBuilder.Ploughed * 0.85f; c.a = 1f; cols[v] = c; }
                m.colors = cols;
            }
        }

        public void End() { Active = false; if (_spray != null) { var em = _spray.emission; em.rateOverTime = 0f; } }

        private void Update()
        {
            if (!Active || Run == null || Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft; var s = ac.State;
            double agl = -s.Position.Z - WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
            var vW = s.Attitude.Rotate(s.Velocity);
            double gs = System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            Run.Update(s.Position, agl, gs);

            if (Run.NewlyCovered.Count > 0 && WorldBuilder.CropFieldMesh != null)
            {
                Mesh m = WorldBuilder.CropFieldMesh; var cols = m.colors; int ny = CropField.CellsY;
                foreach ((int i, int j) in Run.NewlyCovered)
                {
                    int b = (i * ny + j) * 4; Color c = j % 2 == 0 ? WorldBuilder.Sprayed : WorldBuilder.Sprayed * 0.9f; c.a = 1f;
                    for (int q = 0; q < 4; q++) cols[b + q] = c;
                }
                m.colors = cols;
            }

            if (_spray == null) BuildSpray();
            var em = _spray.emission; em.rateOverTime = Run.Spraying ? 350f : 0f;
            // Emit from just behind and below the aircraft, drifting back along the ground track.
            Vector3 back = -Driver.WorldVelocityUnity.normalized;
            _spray.transform.position = Driver.transform.position + back * 2.5f + Vector3.down * 0.8f;
            var vel = _spray.velocityOverLifetime; vel.enabled = true;
            Vector3 drift = Driver.WorldVelocityUnity * 0.15f;
            vel.x = drift.x; vel.y = -1.2f; vel.z = drift.z;

            if (Run.WireStrike && !_struck)
            {
                _struck = true;
                // The wires take the speed out of the aircraft: it staggers, nose dropping, into the field.
                ac.State = new RigidBodyState(s.Position, s.Attitude, s.Velocity * 0.35, new Vec3(s.Rates.X, s.Rates.Y - 0.6, s.Rates.Z));
                em.rateOverTime = 0f;
            }
        }

        private void BuildSpray()
        {
            var go = new GameObject("CropSpray");
            _spray = go.AddComponent<ParticleSystem>();
            var main = _spray.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 2.2f; main.startSize = 1.6f;
            main.startSpeed = 0f; main.gravityModifier = 0.02f; main.maxParticles = 1500; main.loop = true; main.playOnAwake = true;
            main.startColor = new Color(0.95f, 0.97f, 0.9f, 0.55f);
            var em = _spray.emission; em.rateOverTime = 0f;
            var sh = _spray.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(16f, 0.5f, 1.5f); // 16 m swath across the wing
            var col = _spray.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.95f, 0.85f), 1f) },
                      new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = _spray.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.5f)));
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.95f, 0.97f, 0.9f, 0.6f) };
            var r = _spray.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = _mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        public string Line => !Active || Run == null ? null
            : Run.WireStrike ? "CROP DUST  HIT THE WIRES — run over.  R to reset"
            : $"CROP DUST  coverage {Run.Coverage:P0}   passes under the wires {Run.PassesUnder}   {(Run.Spraying ? "SPRAYING" : Run.LastEvent)}";
    }
}

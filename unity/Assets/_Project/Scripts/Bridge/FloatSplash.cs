using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Interactive water spray for the floatplane (owner request), driven by the sim's per-float hydrodynamics:
    ///  - each float has its OWN spray at the step/chines whose emission and size scale with THAT float's
    ///    hydrodynamic drag (deep in the water = big; up on the step = a low sheet; lifted out = nothing);
    ///  - a secondary splash at the stern when the afterbody (float tail) touches;
    ///  - a bow-wave burst when the bow keel digs in (the water-loop precursor).
    /// Particle systems are world-space, unlit soft sprites (FlyingGame/Spray), cheap enough for mobile.
    /// </summary>
    public sealed class FloatSplash : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public float DragForFullSpray = 2500f;   // N of float drag that gives the biggest spray

        private ParticleSystem[] _step = new ParticleSystem[2], _stern = new ParticleSystem[2], _bow = new ParticleSystem[2];
        private Material _mat;
        private bool _built;

        private void Build()
        {
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.92f, 0.97f, 1f, 0.75f) };
            for (int i = 0; i < 2; i++)
            {
                _step[i] = Make($"Spray{i}", 0.45f, 0.9f, 160);
                _stern[i] = Make($"SternSplash{i}", 0.5f, 0.8f, 60);
                _bow[i] = Make($"BowWave{i}", 0.7f, 1.0f, 60);
            }
            _built = true;
        }

        private ParticleSystem Make(string name, float size, float life, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null, true);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = 0f;
            main.gravityModifier = 0.9f;
            main.maxParticles = maxParticles * 4;
            main.startColor = new Color(0.92f, 0.97f, 1f, 0.7f);
            main.loop = true;
            main.playOnAwake = true;
            var em = ps.emission; em.rateOverTime = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.93f, 1f), 1f) },
                      new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 1.4f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var sh = ps.shape; sh.enabled = false;
            return ps;
        }

        private void LateUpdate()
        {
            if (Driver?.Sim == null || Driver.Sim.Aircraft.Config.Floats == null)
            {
                if (_built) foreach (var p in _step) { if (p) SetRate(p, 0); }
                return;
            }
            if (!_built) Build();
            var cfg = Driver.Sim.Aircraft.Config.Floats;
            var att = Driver.Sim.Aircraft.State.Attitude;
            Vector3 fwd = CoordinateMap.ToUnity(att.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0)));
            Vector3 right = CoordinateMap.ToUnity(att.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 1, 0)));
            fwd.y = 0; fwd.Normalize(); right.y = 0; right.Normalize();

            for (int i = 0; i < 2; i++)
            {
                FloatHydro.FloatState f = FloatHydro.Floats[i];
                float speed = (float)f.SpeedMs;
                Vector3 step = CoordinateMap.ToUnity(f.StepWorld);
                Vector3 stern = CoordinateMap.ToUnity(f.SternWorld);
                Vector3 bow = CoordinateMap.ToUnity(f.BowWorld);
                double? water = FloatHydro.WaterSurfaceAt(f.StepWorld.X, f.StepWorld.Y);
                float waterY = water.HasValue ? (float)water.Value : step.y;

                // Main spray: driven by this float's drag (owner: more drag = bigger splash).
                float k = f.Wet ? Mathf.Clamp01((float)f.DragN / DragForFullSpray) : 0f;
                float sizeK = 0.25f + 1.6f * k;                    // planing sheet → deep plunge
                float rate = k * (40f + 120f * Mathf.Clamp01(speed / 15f));
                Emit(_step[i], rate, sizeK, step, waterY, fwd, right, i == 0 ? -1f : 1f, speed, (float)f.StepDraftM);

                // Stern (tail) splash when the afterbody wets.
                float sternK = f.SternDraftM > 0.01 ? Mathf.Clamp01((float)f.SternDraftM / 0.25f) : 0f;
                Emit(_stern[i], sternK * (25f + 60f * Mathf.Clamp01(speed / 10f)), 0.5f + sternK, stern, waterY, fwd, right, i == 0 ? -1f : 1f, speed * 0.5f, (float)f.SternDraftM);

                // Bow wave when the bow keel digs in.
                float bowK = f.BowDraftM > 0.05 ? Mathf.Clamp01((float)f.BowDraftM / 0.4f) : 0f;
                Emit(_bow[i], bowK * (30f + 80f * Mathf.Clamp01(speed / 12f)), 0.8f + 1.5f * bowK, bow, waterY, fwd, right, i == 0 ? -1f : 1f, speed * 0.8f, (float)f.BowDraftM);
            }
        }

        private static void SetRate(ParticleSystem ps, float rate) { var em = ps.emission; em.rateOverTime = rate; }

        /// <summary>Position the emitter on the water at the keel point and throw spray outboard/up with the boat speed.</summary>
        private static void Emit(ParticleSystem ps, float rate, float size, Vector3 keelPoint, float waterY, Vector3 fwd, Vector3 right, float side, float speed, float draft)
        {
            var em = ps.emission; em.rateOverTime = rate;
            if (rate <= 0f) return;
            var main = ps.main;
            main.startSize = size;
            ps.transform.position = new Vector3(keelPoint.x, waterY + 0.05f, keelPoint.z);
            // Spray sheet: outboard and up, plus a little aft (it's left behind), speed ~ boat speed.
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            Vector3 dir = (right * side * 0.9f + Vector3.up * (0.6f + Mathf.Clamp01(draft / 0.3f)) - fwd * 0.4f).normalized;
            float v = Mathf.Clamp(speed * 0.6f, 1.5f, 14f);
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * v * 0.6f, dir.x * v * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(dir.y * v * 0.6f, dir.y * v * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(dir.z * v * 0.6f, dir.z * v * 1.2f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f + 0.4f * draft;
        }
    }
}

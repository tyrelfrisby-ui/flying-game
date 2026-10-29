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

        private ParticleSystem[] _step = new ParticleSystem[2], _stern = new ParticleSystem[2], _bow = new ParticleSystem[2], _trail = new ParticleSystem[2];
        private Material _mat, _mistMat;
        private ParticleSystem[] _prop = new ParticleSystem[0];
        private Transform[] _discs = new Transform[0];
        private string _propAircraft;
        private readonly System.Collections.Generic.List<ParticleSystem> _owned = new System.Collections.Generic.List<ParticleSystem>();
        private bool _built;

        private void Build()
        {
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.92f, 0.97f, 1f, 0.75f) };
            _mistMat = new Material(_mat); _mistMat.SetFloat("_Mist", 1f);
            for (int i = 0; i < 2; i++)
            {
                _step[i] = Make($"Spray{i}", 0.45f, 0.9f, 160);
                _stern[i] = Make($"SternSplash{i}", 0.5f, 0.8f, 60);
                _bow[i] = Make($"BowWave{i}", 0.7f, 1.0f, 60);
                _trail[i] = Make($"SprayTrail{i}", 0.9f, 1.6f, 140);
                var mistRenderer = _trail[i].GetComponent<ParticleSystemRenderer>();
                mistRenderer.sharedMaterial = _mistMat;
                mistRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            _built = true;
        }

        private ParticleSystem Make(string name, float size, float life, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null, true);
            var ps = go.AddComponent<ParticleSystem>();
            _owned.Add(ps);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .25f, size);
            main.startSpeed = 0f;
            main.gravityModifier = 0.9f;
            main.maxParticles = maxParticles * 2;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.15f, .15f);
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
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = .12f; r.lengthScale = 2.4f;
            var sh = ps.shape; sh.enabled = false;
            return ps;
        }

        private void LateUpdate()
        {
            if (Driver?.Sim == null || Driver.Sim.Aircraft.Config.Floats == null)
            {
                if (_built) foreach (var p in _owned) { if (p) SetRate(p, 0); }
                return;
            }
            if (!_built) Build();
            UpdatePropSpray();
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

                // Spray only while PLANING (owner 2026-10-07: "an area of spray behind the actual body but only while it is
                // planing — once it goes to displacement the spray should go away"): planing lift carrying more than buoyancy.
                bool planing = f.Wet && f.PlaningLiftN > f.BuoyancyN && speed > 4f;
                // A digging bow can throw water before planing; retain the planing-only step/mist rule.
                float side = i == 0 ? -1f : 1f;
                float bowK = f.Wet && f.BowDraftM > .05 ? Mathf.Clamp01((float)f.BowDraftM / .4f) : 0f;
                Emit(_bow[i], bowK * Mathf.Clamp01(speed / 4f) * (30f + 80f * Mathf.Clamp01(speed / 12f)),
                    .35f + .7f * bowK, bow, waterY, fwd, right, side, speed * .8f, (float)f.BowDraftM);
                if (!planing) { SetRate(_step[i], 0); SetRate(_stern[i], 0); SetRate(_trail[i], 0); continue; }
                // Main spray: driven by this float's drag (owner: more drag = bigger splash).
                float k = Mathf.Clamp01((float)f.DragN / DragForFullSpray);
                float sizeK = 0.25f + 1.6f * k;                    // planing sheet → deep plunge
                float rate = Mathf.Sqrt(k) * (100f + 220f * Mathf.Clamp01(speed / 15f));
                Emit(_step[i], rate, sizeK, step, waterY, fwd, right, i == 0 ? -1f : 1f, speed, (float)f.StepDraftM);
                // The spray area behind the body: thrown up from just aft of the step with little forward speed, so the
                // aircraft runs away from it and it hangs behind as a field of mist over the wake.
                Vector3 aft = step - fwd * (float)(0.25 * cfg.LengthM);
                float trailRate = (30f + 140f * Mathf.Clamp01(speed / 20f)) * Mathf.Clamp(0.3f + k, 0.3f, 1f) * Mathf.Clamp((float)cfg.BeamM, 0.6f, 4f);
                EmitTrail(_trail[i], trailRate, 0.8f + 0.25f * (float)cfg.BeamM, aft, waterY, right, i == 0 ? -1f : 1f, speed, (float)cfg.BeamM);

                // Stern (tail) splash when the afterbody wets.
                float sternK = f.SternDraftM > 0.01 ? Mathf.Clamp01((float)f.SternDraftM / 0.25f) : 0f;
                Emit(_stern[i], sternK * (25f + 60f * Mathf.Clamp01(speed / 10f)), 0.5f + sternK, stern, waterY, fwd, right, i == 0 ? -1f : 1f, speed * 0.5f, (float)f.SternDraftM);

            }
        }

        // Read the existing drawn discs, including twin pushers; never duplicate StyleFor hub dimensions.
        private void UpdatePropSpray()
        {
            var ac = Driver.Sim.Aircraft;
            if (_propAircraft != ac.Config.Id || _discs.Length == 0 || !_discs[0])
            {
                foreach (var p in _prop) if (p) { _owned.Remove(p); Destroy(p.gameObject); }
                var discs = new System.Collections.Generic.List<Transform>();
                foreach (var mf in Driver.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name == "PropDisc") discs.Add(mf.transform);
                _discs = discs.ToArray(); _prop = new ParticleSystem[_discs.Length];
                for (int i = 0; i < _prop.Length; i++) _prop[i] = Make("PropSpray" + i, .35f, .85f, 100);
                _propAircraft = ac.Config.Id;
            }
            float rpm = ac.EngineStopped ? 0f : (float)ac.EngineRpm;
            float power = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(650, 2200, rpm));
            for (int i = 0; i < _discs.Length; i++)
            {
                var disc = _discs[i];
                if (!disc || !disc.gameObject.activeInHierarchy) { SetRate(_prop[i], 0); continue; }
                var mesh = disc.GetComponent<MeshFilter>().sharedMesh;
                float radius = mesh.bounds.extents.x * Mathf.Abs(disc.lossyScale.x);
                Vector3 hub = disc.position;
                double? water = WorldTerrain.Active?.WaterSurfaceAt(hub.z, hub.x);
                // Project the disc's vertical radius, so pitch/roll and pusher installations count.
                float verticalRadius = radius * Mathf.Sqrt(Mathf.Clamp01(1f - disc.forward.y * disc.forward.y));
                float clearance = water.HasValue ? hub.y - verticalRadius - (float)water.Value : float.PositiveInfinity;
                float proximity = 1f - Mathf.SmoothStep(0f, Mathf.Max(.3f, radius * .85f), Mathf.Max(0, clearance));
                float strength = water.HasValue && hub.y > water.Value ? power * proximity : 0f;
                // Slipstream entrains water at the bottom of the disc, then carries it aft.
                var ps = _prop[i];
                SetRate(ps, strength * 220);
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = _mistMat;
                if (strength <= 0) continue;
                ps.transform.position = new Vector3(hub.x, (float)water.Value + .06f, hub.z);
                var main = ps.main; main.startSize = new ParticleSystem.MinMaxCurve(.04f, .2f + strength * .35f);
                var shape = ps.shape; shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius * .35f;
                ps.transform.rotation = Quaternion.Euler(90, 0, 0);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                Vector3 aft = -Driver.transform.forward * (3 + strength * 9);
                vel.x = new ParticleSystem.MinMaxCurve(aft.x - 1, aft.x + 1);
                vel.y = new ParticleSystem.MinMaxCurve(1f, 2f + strength * 3);
                vel.z = new ParticleSystem.MinMaxCurve(aft.z - 1, aft.z + 1);
            }
        }

        private void OnDisable()
        {
            foreach (var p in _owned) if (p) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            foreach (var p in _owned) if (p) Destroy(p.gameObject);
            if (_mat) Destroy(_mat);
            if (_mistMat) Destroy(_mistMat);
        }

        // Negative world components still need an ascending MinMaxCurve range.
        private static ParticleSystem.MinMaxCurve Spread(float v, float low, float high)
        {
            return new ParticleSystem.MinMaxCurve(Mathf.Min(v * low, v * high), Mathf.Max(v * low, v * high));
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission; em.rateOverTime = rate;
            if (rate > 0 && !ps.isPlaying) ps.Play();
        }

        /// <summary>The hanging spray area: a wide, low emitter behind the step; mostly up and outboard, barely moving forward.</summary>
        private static void EmitTrail(ParticleSystem ps, float rate, float size, Vector3 at, float waterY, Vector3 right, float side, float speed, float beam)
        {
            SetRate(ps, rate);
            var main = ps.main; main.startSize = new ParticleSystem.MinMaxCurve(size * .35f, size * .65f); main.gravityModifier = .35f;
            ps.transform.position = new Vector3(at.x, waterY + 0.1f, at.z);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            float up = Mathf.Clamp(speed * 0.055f, .35f, 1.8f), out_ = Mathf.Clamp(speed * 0.08f, 0.5f, 3f);
            Vector3 o = right * side * out_;
            vel.x = Spread(o.x, .3f, 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(up * 0.5f, up);
            vel.z = Spread(o.z, .3f, 1.2f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(.3f + .4f * beam, .06f, .5f);
        }

        /// <summary>Position the emitter on the water at the keel point and throw spray outboard/up with the boat speed.</summary>
        private static void Emit(ParticleSystem ps, float rate, float size, Vector3 keelPoint, float waterY, Vector3 fwd, Vector3 right, float side, float speed, float draft)
        {
            SetRate(ps, rate);
            if (rate <= 0f) return;
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .28f, size * .82f);
            ps.transform.position = new Vector3(keelPoint.x, waterY + 0.05f, keelPoint.z);
            // Spray sheet: outboard and up, plus a little aft (it's left behind), speed ~ boat speed.
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            Vector3 dir = (right * side * 0.9f + Vector3.up * (0.20f + .45f * Mathf.Clamp01(draft / 0.3f)) - fwd * 0.55f).normalized;
            float v = Mathf.Clamp(speed * 0.5f, 1.5f, 12f);
            vel.x = Spread(dir.x * v, .6f, 1.2f);
            vel.y = Spread(dir.y * v, .6f, 1.2f);
            vel.z = Spread(dir.z * v, .6f, 1.2f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
            ps.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            sh.scale = new Vector3(.12f, .05f, .45f + Mathf.Max(0, draft));
        }
    }
}

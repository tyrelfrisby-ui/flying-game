using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The floats' water spray, PHYSICS ONLY (Ty 2026-11-02: "it sprays in weird directions make it purely physics
    /// based"). Nothing here decides where water goes: <see cref="SprayField"/> (Core, tested) emits every parcel from the
    /// spray root on the hull's own waterline cut with the Wagner/Savitsky jet velocity, in proportion to the jet's mass
    /// flux, and flies it under gravity and sphere drag until it lands; it also lays the Kelvin cusp foam (19.47°) at
    /// displacement speeds. This component only steps that field once per frame and hands the parcels to one
    /// ParticleSystem with SetParticles (position, velocity for the stretch, a size from the droplet cloud) — 600 parcels
    /// + 160 foam marks at most, under the old ~1,500-particle spray on iPhone and Quest.
    /// The prop-wash mist below is unchanged (slipstream picking up water under the disc).
    /// </summary>
    public sealed class FloatSplash : MonoBehaviour
    {
        public FlightSimDriver Driver;

        private ParticleSystem _spray, _foam;
        private ParticleSystem.Particle[] _buf = new ParticleSystem.Particle[SprayField.MaxParcels], _foamBuf = new ParticleSystem.Particle[SprayField.MaxFoam];
        private readonly SprayField _field = new SprayField();
        private Material _mat, _mistMat;
        private ParticleSystem[] _prop = new ParticleSystem[0];
        private Transform[] _discs = new Transform[0];
        private string _propAircraft, _fieldAircraft;
        private readonly System.Collections.Generic.List<ParticleSystem> _owned = new System.Collections.Generic.List<ParticleSystem>();
        private bool _built;

        private void Build()
        {
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(0.92f, 0.97f, 1f, 0.75f) };
            _mistMat = new Material(_mat); _mistMat.SetFloat("_Mist", 1f);
            _spray = MakeDriven("FloatSpray", SprayField.MaxParcels, _mat, ParticleSystemRenderMode.Stretch);
            _foam = MakeDriven("KelvinFoam", SprayField.MaxFoam, _mistMat, ParticleSystemRenderMode.HorizontalBillboard);
            _built = true;
        }

        /// <summary>A particle system that simulates nothing itself: SetParticles drives every particle.</summary>
        private ParticleSystem MakeDriven(string name, int max, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null, true);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _owned.Add(ps);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max; main.loop = false; main.playOnAwake = false;
            main.startSpeed = 0f; main.gravityModifier = 0f; main.startLifetime = 1000f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.04f; r.lengthScale = 1.2f; }
            ps.Play();
            ps.Pause();   // SetParticles each frame; the system itself never advances
            return ps;
        }

        private void LateUpdate()
        {
            if (Driver?.Sim == null || Driver.Sim.Aircraft.Config.Floats == null)
            {
                if (_built) { foreach (var p in _owned) { if (p) SetRate(p, 0); } if (_spray) _spray.SetParticles(_buf, 0); if (_foam) _foam.SetParticles(_foamBuf, 0); }
                return;
            }
            if (!_built) Build();
            UpdatePropSpray();
            var ac = Driver.Sim.Aircraft;
            if (_fieldAircraft != ac.Config.Id) { _field.Clear(); _fieldAircraft = ac.Config.Id; }
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            if (dt > 0f) _field.Step(ac.Config, ac.State, dt);

            int n = 0;
            foreach (SprayField.Parcel p in _field.Parcels)
            {
                if (!p.Alive) continue;
                ref ParticleSystem.Particle q = ref _buf[n++];
                q.position = CoordinateMap.ToUnity(p.Pos);
                q.velocity = CoordinateMap.ToUnity(p.Vel);
                float fade = 1f - (float)(p.Age / SprayField.MaxLifeS);
                // A parcel is a cloud of droplets that spreads as it flies: drawn 0.12 m at the root, growing ~0.25 m/s.
                q.startSize = 0.12f + 0.25f * (float)p.Age;
                q.startColor = new Color(0.93f, 0.97f, 1f, 0.75f * fade);
                q.startLifetime = 1000f; q.remainingLifetime = 999f; q.rotation = 0f;
            }
            _spray.SetParticles(_buf, n);
            int m = 0;
            foreach (SprayField.Foam f in _field.Foams)
            {
                if (!f.Alive) continue;
                ref ParticleSystem.Particle q = ref _foamBuf[m++];
                q.position = CoordinateMap.ToUnity(f.Pos) + Vector3.up * 0.02f;
                q.velocity = Vector3.zero;
                q.startSize = 0.6f;
                q.startColor = new Color(1f, 1f, 1f, 0.5f * (1f - (float)(f.Age / f.Life)));
                q.startLifetime = 1000f; q.remainingLifetime = 999f;
            }
            _foam.SetParticles(_foamBuf, m);
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
            main.startColor = new Color(0.92f, 0.97f, 1f, 0.7f);
            main.loop = true;
            main.playOnAwake = true;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var sh = ps.shape; sh.enabled = false;
            return ps;
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

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission; em.rateOverTime = rate;
            if (rate > 0 && !ps.isPlaying) ps.Play();
        }

    }
}

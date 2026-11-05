using FlyingGame.Core;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Tyre smoke (Ty 2026-11-04, Q026: "locked tire screeches, smokes"). Nothing here decides WHEN a tyre smokes: each wheel
    /// reports the rubber its tread lost (Core TyreWear, <see cref="WheelState.AbradedM3"/>), and that volume becomes
    /// particles at the wheel's patch — a few for a spin-up's puff, a thick trail for a locked skid on asphalt, almost none
    /// on grass, where the same skid tears off a tenth as much. Installed with ContactEffects (StructuralDamage.Repair).
    /// Shared by every platform.
    /// </summary>
    public sealed class TyreSmoke : MonoBehaviour
    {
        /// <summary>Rubber (m³) per particle: a 172's spin-up scrubs ~1.5 cm³ a wheel (≈ 10 puffs), a lock from 60 kt ~200 cm³.</summary>
        private const double RubberPerParticleM3 = 0.15e-6;
        private const int MaxPerFrame = 24;
        private FlightSimDriver _driver;
        private ParticleSystem _ps;
        private Material _mat;
        private double[] _carry = System.Array.Empty<double>();

        private void Awake() { _driver = GetComponent<FlightSimDriver>(); }

        private void OnDestroy()
        {
            if (_ps != null) Destroy(_ps.gameObject);
            if (_mat != null) Destroy(_mat);
        }

        private void Build()
        {
            var go = new GameObject("TyreSmoke");
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 900;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.gravityModifier = -0.015f;
            main.startColor = new Color(0.86f, 0.86f, 0.84f, 0.55f);
            var em = _ps.emission; em.rateOverTime = 0f;
            var shape = _ps.shape; shape.enabled = false;
            var size = _ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 4f)));
            var col = _ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            Shader sh = Shader.Find("FlyingGame/UnlitTransparent");
            if (sh != null) { _mat = new Material(sh) { color = new Color(0.9f, 0.9f, 0.88f, 0.5f) }; _ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = _mat; }
            _ps.Play();
        }

        private void LateUpdate()
        {
            Aircraft ac = _driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null;
            var wheels = ac?.Wheels;
            if (wheels == null) return;
            if (_carry.Length != wheels.Count) _carry = new double[wheels.Count];
            for (int i = 0; i < wheels.Count; i++)
            {
                WheelState w = wheels[i];
                if (w.Tyre == null) continue;
                double v = w.AbradedM3; w.AbradedM3 = 0;
                if (!(v > 0)) continue;
                _carry[i] += v / RubberPerParticleM3;
                int n = (int)System.Math.Min(MaxPerFrame, System.Math.Floor(_carry[i]));
                if (n <= 0) continue;
                _carry[i] -= n;
                if (_carry[i] > MaxPerFrame) _carry[i] = MaxPerFrame;
                if (_ps == null) Build();
                var p = new ParticleSystem.EmitParams
                {
                    position = transform.position + CoordinateMap.ToUnity(w.PatchWorld - ac.State.Position),
                    applyShapeToPosition = false,
                };
                _ps.Emit(p, n);
            }
        }
    }
}

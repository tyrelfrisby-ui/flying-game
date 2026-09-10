using FlyingGame.Core;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Fuel streaming and fire on the inner wing panels (owner 2026-09-10): a white vapour stream trailing
    /// from a holed tank, orange flame with black smoke once it has caught. Driven from the sim's DamageState.</summary>
    public sealed class DamageFx
    {
        private readonly ParticleSystem[] _leak = new ParticleSystem[2], _fire = new ParticleSystem[2], _smoke = new ParticleSystem[2];

        public DamageFx(Transform root, AircraftConfig cfg)
        {
            double semi = WingPanels.Semispan(cfg);
            float y = (float)(semi * 0.35);
            float x = 0f, z = 0f;
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                if (!sf.Id.ToLowerInvariant().Contains("wing") || sf.Id.ToLowerInvariant().Contains("aileron")) continue;
                StripConfig near = sf.Strips[0]; foreach (StripConfig st in sf.Strips) if (System.Math.Abs(System.Math.Abs(st.Pos[1]) - y) < System.Math.Abs(System.Math.Abs(near.Pos[1]) - y)) near = st;
                x = (float)(near.Pos[0] - 0.4 * near.Chord); z = (float)(near.Pos[2] + 0.05); break;
            }
            for (int i = 0; i < 2; i++)
            {
                float sy = i == 0 ? -y : y;
                Vector3 local = new Vector3(sy, -z, x);   // sim (x fwd, y right, z down) → Unity local (x right, y up, z fwd)
                _leak[i] = Make(root, "FuelLeak", local, new Color(0.95f, 0.97f, 1f, 0.45f), 0.35f, 1.6f, 260f);
                _fire[i] = Make(root, "Fire", local, new Color(1f, 0.55f, 0.1f, 0.9f), 0.9f, 0.7f, 220f);
                _smoke[i] = Make(root, "Smoke", local, new Color(0.12f, 0.12f, 0.12f, 0.6f), 1.6f, 3.5f, 90f);
            }
        }

        private static ParticleSystem Make(Transform root, string name, Vector3 local, Color c, float size, float life, float rate)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = life; main.startSize = size;
            main.startSpeed = 0.5f; main.maxParticles = 2000; main.loop = true; main.playOnAwake = true; main.startColor = c;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, name == "Smoke" ? 3f : 1.6f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = c };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        public void Update(DamageState d, Vector3 worldVel)
        {
            for (int i = 0; i < 2; i++)
            {
                bool leak = i == 0 ? d.FuelLeakLeft : d.FuelLeakRight, fire = i == 0 ? d.FireLeft : d.FireRight;
                Set(_leak[i], leak && !fire ? 260f : 0f, worldVel, 0.25f, -0.4f);
                Set(_fire[i], fire ? 220f : 0f, worldVel, 0.15f, 1.5f);
                Set(_smoke[i], fire ? 90f : 0f, worldVel, 0.3f, 2.5f);
            }
        }

        private static void Set(ParticleSystem ps, float rate, Vector3 worldVel, float drag, float rise)
        {
            if (ps == null) return;
            var em = ps.emission; em.rateOverTime = rate;
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            Vector3 v = worldVel * drag;   // the stream trails, then slows relative to the air
            vel.x = v.x; vel.y = v.y + rise; vel.z = v.z;
        }

        public void Destroy() { foreach (var ps in _leak) if (ps != null) Object.Destroy(ps.gameObject); foreach (var ps in _fire) if (ps != null) Object.Destroy(ps.gameObject); foreach (var ps in _smoke) if (ps != null) Object.Destroy(ps.gameObject); }
    }
}

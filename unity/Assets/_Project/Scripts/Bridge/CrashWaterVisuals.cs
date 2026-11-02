using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Bounded world-space spray/foam pools. Installed by FlightAudio; no scene edits required.</summary>
    public sealed class CrashWaterVisuals : MonoBehaviour
    {
        private ParticleSystem _spray, _foam;
        private Material _material, _ringMaterial;
        private readonly Ring[] _rings = new Ring[8];
        private readonly Vent[] _vents = new Vent[24];
        private int _ringNext, _impactFrame = -1, _impactBudget, _ventCursor;
        private MaterialPropertyBlock _ringTint;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private struct Ring { public LineRenderer Line; public Vector3 Point; public float Age, Radius, Life; }
        private struct Vent { public bool Active; public int Id; public Vector3 Point; public float Flow, Air, Seen, Carry; }

        private void Awake()
        {
            Shader shader = Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent");
            if (shader == null) { enabled = false; return; }
            _ringTint = new MaterialPropertyBlock();
            _material = new Material(shader) { color = new Color(.92f, .97f, 1f, .75f) };
            _spray = Make("CrashSpray", 768, true);
            _foam = Make("FloodFoam", 384, false);
            Shader ringShader = Shader.Find("FlyingGame/UnlitTransparent") ?? shader;
            _ringMaterial = new Material(ringShader) { color = new Color(.85f, .93f, 1f, .4f) };
            for (int i = 0; i < _rings.Length; i++)
            {
                var go = new GameObject("CrashWaterRing"); go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _ringMaterial; line.useWorldSpace = true; line.loop = true;
                line.positionCount = 48; line.enabled = false;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                _rings[i].Line = line;
            }
        }
        private ParticleSystem Make(string label, int capacity, bool spray)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity; main.startSpeed = 0; main.startLifetime = 1;
            main.gravityModifier = spray ? 1f : 0; main.loop = true; main.playOnAwake = false;   // spray ballistic under g (lifetime below assumes 9.81); drop air drag not modelled
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(.85f, .93f, 1), 1) },
                new[] { new GradientAlphaKey(.75f, 0), new GradientAlphaKey(.45f, .4f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = _material;
            renderer.renderMode = spray ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.velocityScale = .12f; renderer.lengthScale = 2.4f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.Play(); return ps;
        }
        private void OnEnable() { CrashEvents.Impact += Impact; CrashEvents.Flood += Flood; }
        private void OnDisable()
        {
            CrashEvents.Impact -= Impact; CrashEvents.Flood -= Flood;
            if (_spray != null) _spray.Clear(); if (_foam != null) _foam.Clear();
            for (int i = 0; i < _vents.Length; i++) _vents[i].Active = false;
            for (int i = 0; i < _rings.Length; i++) if (_rings[i].Line != null) _rings[i].Line.enabled = false;
        }
        private void OnDestroy()
        {
            CrashEvents.Impact -= Impact; CrashEvents.Flood -= Flood;
            if (_material != null) Destroy(_material); if (_ringMaterial != null) Destroy(_ringMaterial);
        }
        private void Impact(CrashImpact impact)
        {
            if (impact.Surface != ImpactSurface.Water || _spray == null) return;
            Vector3 point = CoordinateMap.ToUnity(impact.Position);
            Vector3 motion = CoordinateMap.ToUnity(impact.Velocity); motion.y = 0;
            float length = CrashScale.Cavity((float)impact.MassKg, (float)impact.NormalSpeedMs);
            // E=rho*g*L^4, launch v~sqrt(gL), ballistic return time~2v/g. Counts are L^2 coverage,
            // capped for iOS; droplet/sheet appearance follows FloatSplash's shader and stretch settings.
            float launch = Mathf.Sqrt(9.81f * length);
            int count = Mathf.Clamp(Mathf.CeilToInt(48 * length * length), 12, 180);
            if (_impactFrame != Time.frameCount) { _impactFrame = Time.frameCount; _impactBudget = 360; }
            count = Mathf.Min(count, _impactBudget); _impactBudget -= count;
            for (int i = 0; i < count; i++)
            {
                float a = Random.value * Mathf.PI * 2;
                var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var emit = new ParticleSystem.EmitParams();
                emit.position = point + radial * Random.Range(0, length * .35f);
                emit.velocity = radial * launch * Random.Range(.4f, 1.2f) + Vector3.up * launch * Random.Range(.45f, 1.4f) + motion * .08f;
                emit.startLifetime = Mathf.Clamp(2 * emit.velocity.y / 9.81f, .15f, 2f);
                emit.startSize = Random.Range(.04f, .18f) * Mathf.Sqrt(length);
                emit.startColor = new Color(.92f, .97f, 1f, .75f);
                _spray.Emit(emit, 1);
            }
            int slot = _ringNext++ % _rings.Length;
            _rings[slot].Point = point + Vector3.up * .035f; _rings[slot].Age = 0;
            _rings[slot].Radius = length * .4f; _rings[slot].Life = Mathf.Clamp(length, .5f, 3f);
            _rings[slot].Line.enabled = true;
        }
        private void Flood(FloodState state)
        {
            int slot = -1;
            for (int i = 0; i < _vents.Length; i++) if (_vents[i].Active && _vents[i].Id == state.PieceId) { slot = i; break; }
            if (slot < 0) for (int i = 0; i < _vents.Length; i++) if (!_vents[i].Active) { slot = i; break; }
            if (slot < 0) return;
            if (!_vents[slot].Active) _vents[slot].Carry = 0;
            Vector3 point = CoordinateMap.ToUnity(state.Position);
            // Contract gives body depth, so projection onto the local surface needs no terrain query.
            point.y += (float)state.DepthM + .025f;
            _vents[slot].Active = state.AirEscapeM3s > 0 && state.TrappedAirM3 > 0;
            _vents[slot].Id = state.PieceId; _vents[slot].Point = point;
            _vents[slot].Flow = (float)state.AirEscapeM3s; _vents[slot].Air = (float)state.TrappedAirM3;
            _vents[slot].Seen = Time.unscaledTime;
        }
        private void Update()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Line == null || !_rings[i].Line.enabled) continue;
                _rings[i].Age += Time.deltaTime;
                float age = _rings[i].Age, fade = Mathf.Clamp01(1 - age / _rings[i].Life);
                if (fade <= 0) { _rings[i].Line.enabled = false; continue; }
                // Same propagation speed as WakeWaves; this short cosmetic ring needs no GPU height field.
                float radius = _rings[i].Radius + WakeWaves.WaveSpeedMs * age;
                var line = _rings[i].Line; line.widthMultiplier = .03f + .10f * fade;
                line.startColor = line.endColor = Color.white;
                _ringTint.SetColor(ColorId, new Color(.85f, .93f, 1, fade * .45f));
                line.SetPropertyBlock(_ringTint);
                for (int j = 0; j < 48; j++)
                {
                    float a = j * Mathf.PI * 2 / 48;
                    line.SetPosition(j, _rings[i].Point + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
                }
            }
            int budget = 16;
            int first = _ventCursor++ % _vents.Length;
            for (int k = 0; k < _vents.Length; k++)
            {
                int i = (first + k) % _vents.Length;
                if (!_vents[i].Active) continue;
                if (Time.unscaledTime - _vents[i].Seen > .75f) { _vents[i].Active = false; _vents[i].Carry = 0; continue; }
                float radius = CrashScale.BubbleRadius(_vents[i].Flow);
                float bubbleVolume = 4f * Mathf.PI / 3f * radius * radius * radius;
                float rate = Mathf.Min(40, _vents[i].Flow / (256 * bubbleVolume)) * Mathf.Clamp01(_vents[i].Air / .01f);
                _vents[i].Carry = Mathf.Min(4, _vents[i].Carry + rate * Time.deltaTime);
                int n = Mathf.Min(budget, (int)_vents[i].Carry); budget -= n; _vents[i].Carry -= n;
                for (int j = 0; j < n; j++)
                {
                    var emit = new ParticleSystem.EmitParams();
                    Vector2 offset = Random.insideUnitCircle * Mathf.Max(.08f, radius * 18);
                    emit.position = _vents[i].Point + new Vector3(offset.x, 0, offset.y);
                    emit.velocity = new Vector3(offset.x * .15f, .015f, offset.y * .15f);
                    emit.startSize = radius * Random.Range(5f, 12f); emit.startLifetime = Random.Range(.5f, 1.2f);
                    emit.startColor = new Color(.92f, .97f, 1f, .6f); _foam.Emit(emit, 1);
                }
            }
        }
    }
}

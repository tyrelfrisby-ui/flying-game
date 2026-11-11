using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Bits of tree (Ty 2026-11-05 13:52: "tree hits should throw bits of tree flying off and occasionally leave bits stuck on
    /// wing/gear leading edges"). Each <see cref="ContactEventKind.WoodThrown"/> the sim raises — a snapped top, or the
    /// branches and needles a third of an edge has cut — throws a few sprigs of needles and twigs from where it happened,
    /// with the struck part's speed less what they lost being cut free; foliage is light and broad, so the air takes their
    /// speed in a second or so and they fall behind and drift down. Now and then (<see cref="StickChance"/>) a sprig catches
    /// on the leading edge that cut it and rides there, on the aeroplane's own frame, until that part comes off or the
    /// flight ends. Cosmetic: the sim has already charged the airframe for the wood. Installed on StructuralDamage's object
    /// (StructuralDamage.Repair), like PropDebris.
    /// </summary>
    public sealed class TreeDebris : MonoBehaviour
    {
        private sealed class Piece { public Transform T; public Vector3 V, Spin; public float Age; public bool Resting; }
        private sealed class Stuck { public Transform T; public AirframeComponent Part; }
        private const float LifeS = 6f;
        /// <summary>The share of wood events that leave a sprig on the leading edge, and the most a flight carries.</summary>
        public const float StickChance = 0.12f;
        public const int MaxStuck = 6;
        private const int MaxFlying = 120;
        private readonly List<Piece> _pieces = new();
        private readonly List<Stuck> _stuck = new();
        private FlightSimDriver _driver;
        private FlyingGame.Sim.Aircraft _watched;
        private static Mesh _box;
        private static Material _needles, _needlesDry, _twig;

        private void Awake() { _driver = GetComponent<FlightSimDriver>(); }

        private void OnDisable() { Watch(null); }

        private void OnDestroy()
        {
            Watch(null);
            foreach (Piece p in _pieces) if (p.T != null) Destroy(p.T.gameObject);
            _pieces.Clear();
        }

        private void Watch(FlyingGame.Sim.Aircraft ac)
        {
            if (ReferenceEquals(ac, _watched)) return;
            if (_watched != null) { _watched.Contact -= OnContact; _watched.ComponentLost -= OnComponentLost; }
            _watched = ac;
            // A new aeroplane is a clean one: whatever rode on the old one goes.
            foreach (Stuck s in _stuck) if (s.T != null) Destroy(s.T.gameObject);
            _stuck.Clear();
            if (ac != null) { ac.Contact += OnContact; ac.ComponentLost += OnComponentLost; }
        }

        private void OnContact(ContactEvent e)
        {
            if (e.Kind != ContactEventKind.WoodThrown || _watched == null) return;
            float kg = (float)e.Intensity;
            Vector3 at = CoordinateMap.ToUnity(e.PositionWorld), v = CoordinateMap.ToUnity(e.VelocityWorld);
            // A few sprigs for a handful of twigs, up to six for a snapped top.
            int count = Mathf.Clamp(Mathf.RoundToInt(2f + 2.5f * Mathf.Log10(1f + 4f * kg)), 2, 6);
            for (int i = 0; i < count && _pieces.Count < MaxFlying; i++)
            {
                bool twig = (i & 1) == 1;
                Transform t = Make(twig ? "TreeTwig" : "TreeSprig", twig);
                t.position = at + Random.insideUnitSphere * 0.3f;
                t.rotation = Random.rotation;
                _pieces.Add(new Piece
                {
                    T = t,
                    // Cut free at the edge's speed, less what cutting them took; thrown off to the sides and up a little.
                    V = v * Random.Range(0.55f, 0.95f) + Random.insideUnitSphere * 4f + Vector3.up * 1.5f,
                    Spin = Random.insideUnitSphere * 720f,
                });
            }
            if (_stuck.Count < MaxStuck && Random.value < StickChance && CanCarry(e.Part)) StickOn(e);
        }

        /// <summary>Leading edges a sprig can catch on: wings, tails, legs, floats. (Not the nose: the propeller would have it.)</summary>
        private static bool CanCarry(string part) =>
            part.StartsWith("wing") || part.StartsWith("gear") || part.StartsWith("float") || part.StartsWith("hstab") || part == "fin";

        /// <summary>A sprig caught on the edge where the wood was cut, held in the aeroplane's own frame (the driver's transform
        /// is the sim's CG and attitude: its local axes are the body's, mapped by <see cref="CoordinateMap"/>).</summary>
        private void StickOn(ContactEvent e)
        {
            RigidBodyState s = _watched.State;
            Vec3 body = s.Attitude.Conjugate().Rotate(e.PositionWorld - s.Position);
            bool twig = Random.value < 0.5f;
            Transform t = Make(twig ? "StuckTwig" : "StuckSprig", twig);
            t.SetParent(transform, false);
            t.localPosition = CoordinateMap.ToUnity(body);
            // Draped across the edge: lying along the span or the leg, a little askew.
            t.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(60f, 120f), Random.Range(-30f, 30f));
            _stuck.Add(new Stuck { T = t, Part = e.Component });
        }

        private void OnComponentLost(AirframeComponent c)
        {
            for (int i = _stuck.Count - 1; i >= 0; i--)
            {
                if (!Covers(c, _stuck[i].Part)) continue;
                // It leaves with the part: let it fall as a loose piece from where it was.
                Stuck s = _stuck[i];
                _stuck.RemoveAt(i);
                if (s.T == null) continue;
                s.T.SetParent(null, true);
                _pieces.Add(new Piece { T = s.T, V = _driver != null ? _driver.WorldVelocityUnity : Vector3.zero, Spin = Random.insideUnitSphere * 360f });
            }
        }

        private static bool Covers(AirframeComponent lost, AirframeComponent on) =>
            lost == on
            || (lost == AirframeComponent.WingLeft && on is AirframeComponent.WingTipLeft or AirframeComponent.WingTipLowerLeft or AirframeComponent.WingLeftInner or AirframeComponent.WingLeftOuter)
            || (lost == AirframeComponent.WingRight && on is AirframeComponent.WingTipRight or AirframeComponent.WingTipLowerRight or AirframeComponent.WingRightInner or AirframeComponent.WingRightOuter)
            || (lost == AirframeComponent.TailBoom && on is AirframeComponent.TailHorizontal or AirframeComponent.TailVertical);

        private Transform Make(string name, bool twig)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = Box();
            go.AddComponent<MeshRenderer>().sharedMaterial = twig
                ? (_twig ??= Mat(new Color(0.36f, 0.26f, 0.17f)))
                : Random.value < 0.7f ? (_needles ??= Mat(new Color(0.13f, 0.27f, 0.12f))) : (_needlesDry ??= Mat(new Color(0.27f, 0.33f, 0.16f)));
            // A twig: a stick 0.25–0.6 m long, 1–2.5 cm through. A sprig of needles: a flat fan 0.2–0.45 m across.
            go.transform.localScale = twig
                ? new Vector3(Random.Range(0.01f, 0.025f), Random.Range(0.01f, 0.025f), Random.Range(0.25f, 0.6f))
                : new Vector3(Random.Range(0.2f, 0.45f), 0.03f, Random.Range(0.15f, 0.35f));
            return go.transform;
        }

        private void Update()
        {
            Watch(_driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null);
            float dt = Time.deltaTime;
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                Piece p = _pieces[i];
                p.Age += dt;
                if (p.T == null || p.Age > LifeS) { if (p.T != null) Destroy(p.T.gameObject); _pieces.RemoveAt(i); continue; }
                if (p.Resting) continue;
                // Light and broad: gravity, and a drag that takes most of their speed in under a second; they flutter down.
                p.V += Vector3.down * (9.81f * dt);
                p.V *= Mathf.Exp(-2.5f * dt);
                Vector3 pos = p.T.position + p.V * dt;
                p.T.rotation = Quaternion.Euler(p.Spin * dt) * p.T.rotation;
                Vec3 sim = CoordinateMap.ToSim(pos);
                // The deck-aware floor, and the sprig rests ON it rather than with its origin in it
                // (EgressAir.FloorUnder / RestLift: the one convention the canopy and the wreck pieces use).
                float floor = EgressAir.FloorUnder(pos);
                double? water = FloatHydro.WaterSurfaceAt(sim.X, sim.Y, pos.y);
                if (water.HasValue && pos.y < water.Value)
                {
                    // On the water a sprig floats where it lands.
                    pos.y = (float)water.Value;
                    p.Resting = true;
                }
                else if (pos.y < floor)
                {
                    // Lifted by its own half-height, and only on the one frame it comes to rest: RestLift walks the
                    // piece's renderers, which is not something to do every frame of every sprig's fall.
                    pos.y = floor + EgressAir.RestLift(p.T);
                    p.Resting = true;
                }
                p.T.position = pos;
            }
        }

        private static Material Mat(Color c) => new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = c };

        /// <summary>A unit box (scaled per piece).</summary>
        private static Mesh Box()
        {
            if (_box != null) return _box;
            var m = new Mesh { name = "TreeDebrisBox" };
            m.vertices = new Vector3[]
            {
                new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f), new(-.5f, .5f, -.5f),
                new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(.5f, .5f, .5f), new(-.5f, .5f, .5f),
            };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 0, 4, 7, 0, 7, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return _box = m;
        }
    }
}

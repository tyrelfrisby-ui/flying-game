using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The piece a wooden or composite blade loses (Ty 2026-11-05 9:04: "wood props shatter ... they only break off the
    /// parts that hit"; the sim's <see cref="ContactEventKind.PropBladeBroken"/>). It leaves with its tip's speed (the sim
    /// gives it: across the bottom of the disc, on top of the aeroplane's own), as a few splinters for wood or a few
    /// chunks and a puff of fibre for a composite, tumbling under gravity and drag until they come to rest on the ground or
    /// sink into the water, and are gone after a few seconds. Cosmetic: the sim has already taken the span off the blade.
    /// Installed on StructuralDamage's object (StructuralDamage.Repair), like ContactEffects.
    /// </summary>
    public sealed class PropDebris : MonoBehaviour
    {
        private sealed class Piece { public Transform T; public Vector3 V, Spin; public float Age; public bool Resting; }
        private const float LifeS = 5f;
        private readonly List<Piece> _pieces = new();
        private FlightSimDriver _driver;
        private FlyingGame.Sim.Aircraft _watched;
        private static Mesh _sliver;
        private static Material _wood, _composite;

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
            if (_watched != null) _watched.Contact -= OnContact;
            _watched = ac;
            if (ac != null) ac.Contact += OnContact;
        }

        private void OnContact(ContactEvent e)
        {
            if (e.Kind != ContactEventKind.PropBladeBroken) return;
            bool wood = e.Material == PropMaterial.Wood;
            float len = Mathf.Max(0.01f, (float)e.Value);
            Vector3 at = CoordinateMap.ToUnity(e.PositionWorld), v = CoordinateMap.ToUnity(e.VelocityWorld);
            int count = wood ? 4 : 3;
            for (int i = 0; i < count; i++)
            {
                float share = wood ? Random.Range(0.4f, 1f) : Random.Range(0.25f, 0.6f);
                var go = new GameObject(wood ? "PropSplinter" : "PropShard");
                go.AddComponent<MeshFilter>().sharedMesh = Sliver();
                go.AddComponent<MeshRenderer>().sharedMaterial = wood ? (_wood ??= Mat(new Color(0.84f, 0.71f, 0.48f))) : (_composite ??= Mat(new Color(0.8f, 0.8f, 0.76f)));
                go.transform.position = at;
                go.transform.rotation = Random.rotation;
                // A splinter: long and thin. A composite shard: shorter and flatter.
                go.transform.localScale = wood ? new Vector3(0.012f, 0.006f, len * share) : new Vector3(0.03f * share + 0.01f, 0.004f, Mathf.Min(len, 0.06f) * share + 0.01f);
                _pieces.Add(new Piece
                {
                    T = go.transform,
                    V = v * Random.Range(0.85f, 1f) + Random.insideUnitSphere * 3f,
                    Spin = Random.insideUnitSphere * 1800f,
                });
            }
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
                // A light flat piece: gravity, and a drag that takes most of its speed in a second or two.
                p.V += Vector3.down * (9.81f * dt);
                p.V *= Mathf.Exp(-1.2f * dt);
                Vector3 pos = p.T.position + p.V * dt;
                p.T.rotation = Quaternion.Euler(p.Spin * dt) * p.T.rotation;
                Vec3 sim = CoordinateMap.ToSim(pos);
                // The deck-aware floor, and the piece rests ON it rather than with its origin in it
                // (EgressAir.FloorUnder / RestLift: the one convention the canopy and the wreck pieces use).
                float floor = EgressAir.FloorUnder(pos);
                double? water = FloatHydro.WaterSurfaceAt(sim.X, sim.Y, pos.y);
                if (water.HasValue && pos.y < water.Value - 0.02)
                {
                    // Into the water: it slows at once and goes under.
                    p.V *= 0.2f; p.Spin *= 0.2f;
                    if (pos.y < water.Value - 0.3) { Destroy(p.T.gameObject); _pieces.RemoveAt(i); continue; }
                }
                else if (pos.y < floor)
                {
                    // Lifted by its own half-height, and only on the one frame it comes to rest: RestLift walks the
                    // piece's renderers, which is not something to do every frame of every shard's fall.
                    pos.y = floor + EgressAir.RestLift(p.T);
                    p.Resting = true;
                }
                p.T.position = pos;
            }
        }

        private static Material Mat(Color c) => new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = c };

        /// <summary>A unit box (scaled per piece).</summary>
        private static Mesh Sliver()
        {
            if (_sliver != null) return _sliver;
            var m = new Mesh { name = "PropSliver" };
            Vector3[] c =
            {
                new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f), new(-.5f, .5f, -.5f),
                new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(.5f, .5f, .5f), new(-.5f, .5f, .5f),
            };
            m.vertices = c;
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 0, 4, 7, 0, 7, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return _sliver = m;
        }
    }
}

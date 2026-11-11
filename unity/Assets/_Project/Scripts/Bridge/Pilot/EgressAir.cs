using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Shared helpers for the pilot-egress bodies (pilot, seat, canopy shell, parachute): the air they fall
    /// through and the ground they land on, plus the iOS-safe primitives/materials they are drawn with.
    /// All positions are Unity world (x east, y up, z north); the sim helpers are called through
    /// <see cref="CoordinateMap"/> so the terrain/atmosphere stay the single source of truth.
    /// </summary>
    internal static class EgressAir
    {
        public const float G = 9.80665f;

        /// <summary>ISA density (kg/m³) at a Unity altitude (y), with the day's ISA deviation applied.</summary>
        public static float Density(float altitudeM)
        {
            float rho = (float)Atmosphere.DensityAtAltitude(Mathf.Max(0f, altitudeM));
            return rho > 0.01f ? rho : 1.225f * Mathf.Exp(-Mathf.Max(0f, altitudeM) / 8500f);
        }

        /// <summary>
        /// Wind (m/s, Unity axes) at a Unity position: the session's steady wind (SessionSettings WindFromDeg /
        /// WindSpeedMs is what Atmosphere.SteadyWind is set from) plus thermals/turbulence/ridge lift, so a
        /// canopy drifts in exactly the air the aircraft was flying in.
        /// </summary>
        public static Vector3 Wind(Vector3 unityPos)
        {
            Vector3 w = CoordinateMap.ToUnity(Atmosphere.WindAtPosition(CoordinateMap.ToSim(unityPos)));
            if (float.IsNaN(w.x) || float.IsNaN(w.y) || float.IsNaN(w.z)) w = Vector3.zero;
            return w;
        }

        /// <summary>Surface height (m, Unity y) under a Unity position: terrain, or the water surface where there is water.</summary>
        public static float SurfaceHeight(Vector3 unityPos)
        {
            double simX = unityPos.z, simY = unityPos.x;   // sim x = north (Unity z), sim y = east (Unity x)
            double ground = WorldTerrain.GroundHeightAt(simX, simY);
            double? water = WorldTerrain.Active?.WaterSurfaceAt(simX, simY);
            return (float)(water.HasValue ? System.Math.Max(ground, water.Value) : ground);
        }

        /// <summary>
        /// The surface a falling FRAGMENT comes to rest on under a Unity position, m (y) — the terrain with its
        /// wheel-scale bumps, or the deck, apron or bridge it is over (<c>WorldTerrain.HardPointGroundAt</c>, the same
        /// lookup the airframe's hard points make and that <c>AirframeVisual.WingDebris.KeepAboveGround</c> already
        /// uses for the wreck pieces' drawn meshes). <see cref="SurfaceHeight"/> is the BARE height field with the
        /// water over it, which is right for a canopy or a parachute coming down in the open and wrong for a shard:
        /// a blade fragment thrown onto a raised apron fell through it to the terrain below, because the bare field
        /// has no idea the apron is there.
        /// </summary>
        public static float FloorUnder(Vector3 unityPos) =>
            (float)WorldTerrain.HardPointGroundAt(unityPos.z, unityPos.x, unityPos.y);

        /// <summary>
        /// How far above that floor to put an object's ORIGIN so the thing that is DRAWN rests on the surface instead
        /// of half through it: half the vertical extent of what it draws, in the attitude it has come to rest in
        /// (Renderer.bounds is the world AABB, so this is the turned object's own height and not its modelling-time
        /// one). Ty 2026-11-09: "its parts and pieces need to stay above the surface post crash" — a fragment whose
        /// ORIGIN was put on the ground is buried by exactly this, which for a blade chunk or a sprig is a few
        /// centimetres. Zero for an object that draws nothing.
        /// </summary>
        public static float RestLift(Transform t)
        {
            if (t == null) return 0f;
            float half = 0f;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.bounds.extents.y > half) half = r.bounds.extents.y;
            return half;
        }

        public static bool IsWater(Vector3 unityPos)
        {
            double simX = unityPos.z, simY = unityPos.x;
            double? water = WorldTerrain.Active?.WaterSurfaceAt(simX, simY);
            return water.HasValue && water.Value >= WorldTerrain.GroundHeightAt(simX, simY) - 0.01;
        }

        /// <summary>
        /// Quadratic drag, integrated IMPLICITLY so a huge drag area at high speed (opening shock at
        /// 80 m/s) can never overshoot through zero in one frame: v' = v / (1 + k|v|dt), k = ½ρCdA/m.
        /// Returns the velocity change for this step.
        /// </summary>
        public static Vector3 ImplicitDragDelta(Vector3 vRel, float rho, float cdA, float massKg, float dt)
        {
            float s = vRel.magnitude;
            if (s < 1e-4f || cdA <= 0f) return Vector3.zero;
            float k = 0.5f * rho * cdA / massKg;
            return vRel / (1f + k * s * dt) - vRel;
        }

        // ---- drawing (iOS shader stripping: Unlit/Color is force-included; the project's transparent
        //      shader is optional and falls back to it) -------------------------------------------------

        public static Material Unlit(Color c) => new Material(Shader.Find("Unlit/Color")) { color = c };

        public static Material Transparent(Color c) =>
            new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = c };

        /// <summary>A primitive with its collider removed (no Unity physics on gameplay objects).</summary>
        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Unit dome: the upper hemisphere (skirt ring at y = 0, radius 1; apex at y = 1), lofted from
        /// `rings` latitude rings × `segments` gores, DOUBLE-SIDED so the inside is visible from below
        /// (Unlit/Color back-face culls). Scale it to (r, h, r) for a canopy or (w/2, h/2, l/2) for a
        /// cockpit-canopy shell.
        /// </summary>
        public static Mesh Dome(int segments = 24, int rings = 8)
        {
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= rings; j++)
            {
                float phi = j / (float)rings * Mathf.PI * 0.5f;
                float r = Mathf.Cos(phi), y = Mathf.Sin(phi);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    int k = j * (segments + 1) + i;
                    verts[k] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    uvs[k] = new Vector2(i / (float)segments, j / (float)rings);
                }
            }
            var tris = new int[segments * rings * 12];
            int t = 0;
            for (int j = 0; j < rings; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * (segments + 1) + i, b = a + 1, c = a + segments + 1, d = c + 1;
                // outside
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                // inside (reverse winding)
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            var m = new Mesh { name = "Dome" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Cumulus clouds (owner 2026-10-03: "research thermal inducing clouds and put one that is accurate in appearance
    /// over each thermal"; and puffy clouds in the combat zone to hide in).
    ///
    /// The meteorology a pilot reads off a thermal cumulus, and how each is modelled:
    ///  * The thermal's TOP is the cloud BASE. Rising air cools at the dry adiabatic rate until it reaches its dew point
    ///    (the lifting condensation level, ~400 ft per °C of temperature–dew-point spread); there it condenses. Every
    ///    thermal of the day shares that level, so all bases are FLAT and at about the same height — here, each
    ///    thermal's <see cref="Thermal.TopAltitudeM"/>.
    ///  * The cloud sits over the TOP of the column, not its ground source: in a wind the plume leans, so the cloud is
    ///    displaced downwind of the trigger (<see cref="Thermal.CoreAt"/> at the top).
    ///  * Size and shape follow strength: weak lift makes cumulus humilis (wider than tall); stronger lift makes
    ///    cumulus mediocris (about as tall as wide) with a cauliflower crown of rising turrets. The cloud is about as
    ///    wide as the thermal at the top (the column spreads with height).
    ///  * The base is darkest (the cloud shades it) — grey and flat from below, sunlit white on top.
    /// Flying INTO a cloud whites out the view (instrument conditions), deeper = whiter — the hiding place.
    ///
    /// Ty, 2026-11-11: "make the playground clouds double as the thermal clouds but make their shape look cumulus
    /// enough to be accurate". So this class no longer has a renderer of its own. The puff field below IS the
    /// meteorology above and it is what makes this a cumulus rather than a bank — a ring of flat-bottomed lobes on
    /// the condensation level, a body over them, a cauliflower crown of turrets climbing to the top, granulation on
    /// the sunlit crown — and every lobe is drawn as one of the cloud playground's billowing impostors
    /// (Shaders/CloudPlayground.shader, Resources/Clouds/CloudBillow), the renderer he likes, with its new
    /// world-space flat base switched on. FlyingGame/Cumulus, the opaque sphere-mesh shader, is deleted.
    ///
    /// What the game thinks is cloud is still <see cref="Puffs"/>, the analytic spheres: <see cref="Blocks"/> and
    /// <see cref="Whiteout"/> read them. The drawn lobe now agrees with them far better than the displaced mesh
    /// did — an impostor's alpha reaches zero within about 8 % of the lobe's radius (the shader's noise erosion is
    /// ±0.16 on r², so r = 1 ± 0.08), against ±35 % for the noise-displaced sphere it replaces.
    /// </summary>
    public sealed class Cumulus : MonoBehaviour
    {
        private sealed class Cloud
        {
            public Transform T; public MeshRenderer R; public Thermal Th;
            public float BaseY, TopY, RadiusM;
            public readonly List<(Vector3 c, float r)> Puffs = new();   // local to T
        }

        private static Cumulus _inst;
        private static Material _mat;
        private readonly List<Cloud> _clouds = new();
        private MaterialPropertyBlock _mpb;
        private Light _sun;
        private float _whiteout;
        private static Texture2D _white;
        private static readonly int BaseId = Shader.PropertyToID("_BaseY"), TopId = Shader.PropertyToID("_TopY"),
                                    SunId = Shader.PropertyToID("_SunDir"), CumulusId = Shader.PropertyToID("_Cumulus");

        /// <summary>0 (clear) .. 1 (deep in cloud) at the main camera this frame.</summary>
        public static float Whiteout => _inst != null ? _inst._whiteout : 0f;

        private static Cumulus Inst
        {
            get
            {
                if (_inst != null) return _inst;
                var go = GameObject.Find("Clouds") ?? new GameObject("Clouds");
                _inst = go.GetComponent<Cumulus>() ?? go.AddComponent<Cumulus>();
                return _inst;
            }
        }

        /// <summary>A cumulus over <paramref name="th"/>: base at its top, sized from its core and strength.</summary>
        public static void ForThermal(Thermal th, int seed)
        {
            float top = (float)th.TopAltitudeM;
            float radius = (float)(th.CoreRadiusM * 1.8 * 1.05);                     // the column's width at the top
            float w = (float)th.CoreUpdraftMs;
            // Humilis (weak, ~0.6 x as tall as wide) .. mediocris (strong, ~1.0 x).
            float tall = Mathf.Lerp(0.6f, 1.05f, Mathf.InverseLerp(4f, 14f, w));
            Inst.Add(th, Vector3.zero, top, radius, 2f * radius * tall, seed);
        }

        /// <summary>A free-standing puffy cloud (no thermal) — combat-zone cover. <paramref name="centre"/> is the base centre (Unity).</summary>
        public static void Puffy(Vector3 centre, float radius, float height, int seed) => Inst.Add(null, centre, centre.y, radius, height, seed);

        /// <summary>True when the straight line between two points passes through a cloud (line of sight blocked).</summary>
        public static bool Blocks(Vector3 a, Vector3 b)
        {
            if (_inst == null) return false;
            foreach (Cloud c in _inst._clouds)
            {
                if (!c.T.gameObject.activeInHierarchy) continue;
                Vector3 la = c.T.InverseTransformPoint(a), lb = c.T.InverseTransformPoint(b), d = lb - la;
                float len2 = Mathf.Max(1e-4f, d.sqrMagnitude);
                foreach (var (pc, pr) in c.Puffs)
                {
                    float t = Mathf.Clamp01(Vector3.Dot(pc - la, d) / len2);
                    Vector3 q = la + d * t;
                    if (q.y >= 0f && (q - pc).sqrMagnitude < pr * pr * 0.7f) return true;
                }
            }
            return false;
        }

        private void Add(Thermal th, Vector3 pos, float baseY, float radius, float height, int seed)
        {
            // The playground's own billow material, shared with its banks and low patches, so a cumulus and a bank
            // are the same cloud drawn the same way. The per-cloud base and top ride in a property block.
            _mat ??= Resources.Load<Material>("Clouds/CloudBillow");
            if (_mat == null)
            {
                Debug.LogError("[Cumulus] Resources/Clouds/CloudBillow missing from the build: no thermal clouds");
                return;
            }
            var c = new Cloud { Th = th, BaseY = baseY, TopY = baseY + height, RadiusM = radius };
            var rng = new System.Random(9173 + seed * 7919);
            float U(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---- the puff field ---------------------------------------------------------------------------------
            // Base ring: wide, flat-bottomed lobes, cut off at the condensation level by the shader. Every lobe
            // OVERLAPS its neighbours by about a third of its radius. That is the difference between a puff field
            // for opaque spheres and one for blended impostors: with spheres a gap between two lobes read as a
            // crevice in one body, because the body behind it was still drawn; with impostors a gap is a hole with
            // sky through it, and an isolated lobe is a disc. A ring of twelve to fifteen at this size leaves none.
            int ring = 12 + rng.Next(4);
            for (int i = 0; i < ring; i++)
            {
                float a = (i + U(-0.35f, 0.35f)) / ring * Mathf.PI * 2f;
                float pr = radius * U(0.30f, 0.46f);
                float d = radius - pr * U(0.55f, 0.85f);
                c.Puffs.Add((new Vector3(Mathf.Cos(a) * d, pr * U(0.30f, 0.55f), Mathf.Sin(a) * d), pr));
            }
            // Body over the middle of the base: big lobes, well inside the ring, so there is cloud over the whole
            // base and not a doughnut. Looking straight up at one is the view that finds every hole.
            for (int i = 0; i < 4; i++)
            {
                float pr = radius * U(0.44f, 0.60f);
                c.Puffs.Add((new Vector3(U(-0.26f, 0.26f) * radius, pr * U(0.40f, 0.65f), U(-0.26f, 0.26f) * radius), pr));
            }
            // Cauliflower crown: turrets climbing to the top, shrinking as they go, with side lobes. The side lobes
            // now stand as far out from their turret as they are tall (they used to sit almost on its axis, which
            // stacked the whole cloud into a snowman), and each turret's size is drawn over a band wide enough that
            // no two levels match.
            int levels = 4 + rng.Next(3);
            for (int j = 1; j <= levels; j++)
            {
                float t = j / (float)levels;
                float pr = radius * Mathf.Lerp(0.54f, 0.26f, t) * U(0.85f, 1.20f);
                float y = Mathf.Lerp(height * 0.26f, height - pr * 0.85f, t);
                float spread = (1f - t * 0.5f) * radius * 0.34f;
                Vector3 centre = new(U(-1f, 1f) * spread, y, U(-1f, 1f) * spread);
                c.Puffs.Add((centre, pr));
                int lobes = 2 + rng.Next(2);
                for (int k = 0; k < lobes; k++)
                {
                    float a = U(0f, Mathf.PI * 2f), lr = pr * U(0.60f, 0.95f);
                    c.Puffs.Add((centre + new Vector3(Mathf.Cos(a) * pr * U(0.40f, 0.70f), U(-0.3f, 0.35f) * pr,
                                                      Mathf.Sin(a) * pr * U(0.40f, 0.70f)), lr));
                }
            }
            // Granulation: small puffs sitting ON the sunlit upper surface. A cumulus crown is bumpy at a scale far
            // below its turrets, and that granular edge against the blue is most of what makes a photograph of one
            // read as cloud rather than as a shape. They sit SEVEN tenths of the way out along their parent's radius,
            // not eight: a granule that stands proud of the surface it grew on is a wart, and the first impostor
            // version of this had a dozen of them on every crown.
            int grains = 8 + rng.Next(5), placed = c.Puffs.Count, shapeLobes = c.Puffs.Count;
            for (int i = 0; i < grains; i++)
            {
                var (pc, pr) = c.Puffs[rng.Next(placed)];
                if (pc.y < height * 0.2f) continue;
                float a = U(0f, Mathf.PI * 2f), e = U(0.1f, 1.0f), s = Mathf.Sqrt(1f - e * e);
                Vector3 dir = new(Mathf.Cos(a) * s, e, Mathf.Sin(a) * s);
                c.Puffs.Add((pc + dir * pr * 0.70f, Mathf.Min(pr * 0.38f, radius * 0.16f) * U(0.75f, 1.1f)));
            }

            // ---- one mesh per cloud -----------------------------------------------------------------------------
            // Four vertices a lobe, all of them at the lobe's centre; the vertex shader swings them into a
            // camera-facing quad of half-size TEXCOORD1 and the fragment shader shades it as a sphere and erodes it
            // with seeded noise. That is 2 triangles a lobe where the sphere mesh it replaces was about 400, and it
            // is the whole of the cost argument in docs/CLOUDS-WAKE-VISUALS.md: the geometry and the startup build
            // all but vanish, and what is paid instead is overdraw, because these blend and do not write depth.
            //
            // COLOR.a is the playground's "secondary lobe" flag, which fades a lobe out beyond 7.5 km. The lobes
            // that carry the SHAPE — the ring, the body, the turrets — are primary, because the cloud is the
            // landmark a glider pilot steers at from 5 km out and it has to hold its outline all the way in. The
            // granulation is secondary: it is the surface texture of a crown, it is invisible past a few kilometres
            // anyway, and it is a third of the lobes. That is overdraw bought back for nothing given up.
            var verts = new List<Vector3>(c.Puffs.Count * 4); var uvs = new List<Vector2>(c.Puffs.Count * 4);
            var sizes = new List<Vector2>(c.Puffs.Count * 4); var cols = new List<Color>(c.Puffs.Count * 4);
            var tris = new List<int>(c.Puffs.Count * 6);
            for (int i = 0; i < c.Puffs.Count; i++)
            {
                var (pc, pr) = c.Puffs[i];
                float secondary = i < shapeLobes ? 0f : 1f;
                int o = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(pc); uvs.Add(new Vector2(k % 2, k / 2));
                    sizes.Add(new Vector2(pr, pr * 0.95f));   // a shade flatter than round: a lobe is not a ball
                    cols.Add(new Color(1f, 1f, 1f, secondary));
                }
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1);
                tris.Add(o + 1); tris.Add(o + 2); tris.Add(o + 3);
            }

            var mesh = new Mesh { name = "Cumulus" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetUVs(1, sizes);
            mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            // The quads expand round their centres in the shader, so the bounds must allow for the biggest lobe
            // sticking out of the puff field in every direction, or a cloud is culled while part of it is on screen.
            mesh.bounds = new Bounds(new Vector3(0, height * 0.5f, 0), new Vector3(radius * 3.2f, height * 1.8f, radius * 3.2f));

            var go = new GameObject(th != null ? "ThermalCumulus" : "Cumulus");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, baseY, pos.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            c.R = go.AddComponent<MeshRenderer>();
            c.R.sharedMaterial = _mat;
            c.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.R.receiveShadows = false;
            c.R.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            c.R.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            c.T = go.transform;
            _clouds.Add(c);
            Place(c);
        }

        private static void Place(Cloud c)
        {
            if (c.Th == null) return;
            var core = c.Th.CoreAt(c.Th.TopAltitudeM * 0.98);   // the top of the leaning plume
            Vector3 u = CoordinateMap.ToUnity(core);
            c.T.position = new Vector3(u.x, c.BaseY, u.z);
        }

        /// <summary>Edit-mode entry for the render tool (Editor/CumulusRender.cs): LateUpdate does all the per-frame
        /// shader work — the flat base, the sun direction, the fade — so a batch-mode render has to drive it by hand
        /// or the material's defaults (base y = 1000) put the condensation level in the wrong place.</summary>
        public static void StepForTesting() { if (_inst != null) _inst.LateUpdate(); }

        private void LateUpdate()
        {
            _mpb ??= new MaterialPropertyBlock();
            if (_sun == null) foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { _sun = l; break; }
            Vector3 toSun = _sun != null ? -_sun.transform.forward : new Vector3(0.3f, 0.8f, 0.4f).normalized;
            float scale = (float)Atmosphere.ThermalStrengthScale;
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            float deepest = 0f;
            foreach (Cloud c in _clouds)
            {
                // Thermal clouds come and go with the day's thermals (Options: Thermals 0x = blue sky).
                bool on = c.Th == null || scale > 0.05f;
                if (c.R.enabled != on) c.R.enabled = on;
                if (!on) continue;
                Place(c);
                _mpb.SetFloat(BaseId, c.BaseY);
                _mpb.SetFloat(TopId, c.TopY);
                _mpb.SetVector(SunId, toSun);
                _mpb.SetFloat(CumulusId, 1f);   // the flat base and the whole-cloud shading; the banks leave it 0
                c.R.SetPropertyBlock(_mpb);

                // In-cloud whiteout: how far inside the nearest puff the camera is.
                if (cam == null || !c.T.gameObject.activeInHierarchy) continue;
                Vector3 lp = c.T.InverseTransformPoint(cp);
                if (lp.y < 0f || lp.y > c.TopY - c.BaseY + 50f || new Vector2(lp.x, lp.z).sqrMagnitude > c.RadiusM * c.RadiusM * 2.2f) continue;
                foreach (var (pc, pr) in c.Puffs)
                {
                    float pen = pr - Vector3.Distance(lp, pc);
                    if (pen > 0f) deepest = Mathf.Max(deepest, Mathf.Min(pen, lp.y) / 35f);
                }
            }
            _whiteout = Mathf.Clamp01(deepest);
        }

        private void OnGUI()
        {
            // NOT suppressed in VR, unlike the instrument overlays. Greyout and redout are not information
            // drawn on the glass, they are what the pilot's own eyes do under G, and a veil laid evenly over
            // the whole screen covers both eyes equally — which is exactly right in a headset, and the one
            // cue an aerobatic pilot would most notice missing. The compositor draws behind it.
            if (_whiteout <= 0.01f || SessionSettings.MenuOpen || Event.current.type != EventType.Repaint) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            GUI.depth = 100;   // under the HUD, over the world
            GUI.color = new Color(0.9f, 0.92f, 0.95f, 0.96f * _whiteout);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
        }

    }
}

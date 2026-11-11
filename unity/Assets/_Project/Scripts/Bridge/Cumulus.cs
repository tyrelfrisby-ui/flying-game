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
    ///  * The base is darkest (the cloud shades it) — grey and flat from below, sunlit white on top (Cumulus.shader).
    /// Flying INTO a cloud whites out the view (instrument conditions), deeper = whiter — the hiding place.
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
        private static readonly Dictionary<int, Blob> _spheres = new();   // unit sphere templates by tessellation
        private static Material _mat;
        /// <summary>The flat base is drawn this far above the condensation level, so it survives the shader's
        /// clip at _BaseY (which is exactly the cloud object's own y = 0) instead of fighting it.</summary>
        private const float CapY = 0.6f;
        private readonly List<Cloud> _clouds = new();
        private MaterialPropertyBlock _mpb;
        private Light _sun;
        private float _whiteout;
        private static Texture2D _white;
        private static readonly int BaseId = Shader.PropertyToID("_BaseY"), TopId = Shader.PropertyToID("_TopY"),
                                    SunId = Shader.PropertyToID("_SunDir"), FadeId = Shader.PropertyToID("_Fade");

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
            if (_mat == null)
            {
                Shader sh = Shader.Find("FlyingGame/Cumulus") ?? Shader.Find("Unlit/Color");
                _mat = new Material(sh);
            }
            var c = new Cloud { Th = th, BaseY = baseY, TopY = baseY + height, RadiusM = radius };
            var rng = new System.Random(9173 + seed * 7919);
            float U(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---- the puff field ---------------------------------------------------------------------------------
            // Base ring: wide, flat-bottomed lobes (the lower third of each puff is cut off at the base). More of
            // them, and over a far wider range of sizes, than the seven-to-nine near-identical lobes this used to
            // have — a ring of same-sized balls is read as artificial before anything else about a cloud is read.
            int ring = 9 + rng.Next(3);
            for (int i = 0; i < ring; i++)
            {
                float a = (i + U(-0.35f, 0.35f)) / ring * Mathf.PI * 2f;
                float pr = radius * U(0.20f, 0.40f);
                float d = radius - pr * U(0.75f, 1.05f);
                c.Puffs.Add((new Vector3(Mathf.Cos(a) * d, pr * U(0.25f, 0.5f), Mathf.Sin(a) * d), pr));
            }
            // Body over the middle of the base.
            for (int i = 0; i < 3; i++)
            {
                float pr = radius * U(0.36f, 0.54f);
                c.Puffs.Add((new Vector3(U(-0.32f, 0.32f) * radius, pr * U(0.35f, 0.6f), U(-0.32f, 0.32f) * radius), pr));
            }
            // Cauliflower crown: turrets climbing to the top, shrinking as they go, with side lobes. The side lobes
            // now stand as far out from their turret as they are tall (they used to sit almost on its axis, which
            // stacked the whole cloud into a snowman), and each turret's size is drawn over a band wide enough that
            // no two levels match.
            int levels = 4 + rng.Next(3);
            for (int j = 1; j <= levels; j++)
            {
                float t = j / (float)levels;
                float pr = radius * Mathf.Lerp(0.46f, 0.20f, t) * U(0.78f, 1.28f);
                float y = Mathf.Lerp(height * 0.28f, height - pr, t);
                float spread = (1f - t * 0.55f) * radius * 0.50f;
                Vector3 centre = new(U(-1f, 1f) * spread, y, U(-1f, 1f) * spread);
                c.Puffs.Add((centre, pr));
                int lobes = 1 + rng.Next(3);
                for (int k = 0; k < lobes; k++)
                {
                    float a = U(0f, Mathf.PI * 2f), lr = pr * U(0.50f, 0.90f);
                    c.Puffs.Add((centre + new Vector3(Mathf.Cos(a) * pr * U(0.55f, 0.85f), U(-0.3f, 0.35f) * pr,
                                                      Mathf.Sin(a) * pr * U(0.55f, 0.85f)), lr));
                }
            }
            // Granulation: small puffs sitting ON the sunlit upper surface. A cumulus crown is bumpy at a scale far
            // below its turrets, and that granular edge against the blue is most of what makes a photograph of one
            // read as cloud rather than as a shape. They are cheap — a 60 m puff is 88 vertices before the cull.
            int grains = 8 + rng.Next(5), placed = c.Puffs.Count;
            for (int i = 0; i < grains; i++)
            {
                var (pc, pr) = c.Puffs[rng.Next(placed)];
                if (pc.y < height * 0.2f) continue;
                float a = U(0f, Mathf.PI * 2f), e = U(0.1f, 1.0f), s = Mathf.Sqrt(1f - e * e);
                Vector3 dir = new(Mathf.Cos(a) * s, e, Mathf.Sin(a) * s);
                c.Puffs.Add((pc + dir * pr * 0.82f, Mathf.Min(pr * 0.32f, radius * 0.14f) * U(0.7f, 1.1f)));
            }

            // ---- one mesh per cloud -----------------------------------------------------------------------------
            // Ty 2026-11-11: "clouds that look fake like a bunch of circles glued together". That was this, exactly:
            // every puff was a mathematically perfect sphere, so wherever a puff was the nearest surface its outline
            // was a circular arc and its shading was a billiard ball's, and the eye adds the arcs up into a pile of
            // balls. Three things break it, and all three are paid for at BUILD time, so the per-pixel cost on the
            // phone does not move (Editor/CumulusRender.cs measures both ends):
            //   * every vertex is displaced along its own radius by a value-noise field evaluated in CLOUD space —
            //     ONE field for the whole cloud, so neighbouring puffs' lumps line up into a single turbulent body
            //     instead of each wobbling on its own. Nothing circular survives it.
            //   * the normals are rebuilt from the displaced triangles (welded by position, or the sphere's repeated
            //     seam column and its poles would show as creases), so the shading follows the lumps.
            //   * every vertex is told how deeply it lies inside its NEIGHBOURS. That darkens the valleys where two
            //     puffs meet — in a real cumulus the light that reaches the bottom of a valley between two turrets
            //     has been through cloud twice, which is why the gaps are grey while the shoulders are white — and
            //     it lets the triangles BURIED inside another puff be thrown away before they are ever submitted.
            //     That cull is what pays for the extra lobes above.
            float lump = Mathf.Max(20f, radius * 0.14f);           // the coarsest lump, metres
            const float Amp = 0.70f;                               // so ±35 % of a puff's radius
            Vector3 grain = new(seed * 13.7f, seed * 5.3f, seed * 29.1f);   // another cloud is another cloud

            int guess = c.Puffs.Count * 260;
            var verts = new List<Vector3>(guess); var norms = new List<Vector3>(guess);
            var cols = new List<Color>(guess); var tris = new List<int>(guess * 6);
            var owner = new List<int>(guess); var flat = new List<bool>(guess); var weldTo = new List<int>(guess);
            for (int pi = 0; pi < c.Puffs.Count; pi++)
            {
                var (pc, pr) = c.Puffs[pi];
                // Rings from the puff's size: about 30 m of surface per quad, which is finer than a kilometre-wide
                // cloud is ever resolved on a phone, and no finer than the lumps the noise can put on it.
                int lat = Mathf.Clamp(Mathf.RoundToInt(pr / 18f), 7, 16), lon = lat + lat / 2;
                Blob sp = UnitSphere(lon, lat);
                Vector3[] sv = sp.Dir; int[] st = sp.Tri;
                // The cloud's lump is sized to the whole cloud, but a 60 m granule is smaller than one lump and
                // would come out a perfect little ball — exactly the fault being fixed. A small puff gets a
                // proportionally smaller one; it is small enough that its lumps not lining up cannot be seen.
                float pl = Mathf.Min(lump, pr * 0.75f);
                int o = verts.Count;
                for (int i = 0; i < sv.Length; i++)
                {
                    Vector3 dir = sv[i];
                    float f = Fbm3((pc + dir * pr) / pl + grain);
                    verts.Add(pc + dir * (pr * (1f + Amp * (f - 0.55f))));
                    // g = the displacement noise itself, kept for the shader: it mottles the sunlit face and ragged
                    // the dithered rim, and a surface brighter where it bulges is what a cumulus does. Sampling a
                    // SECOND field for it was half the noise cost of the whole build for no new information.
                    norms.Add(dir); cols.Add(new Color(1f, f, 0f, 1f)); owner.Add(pi); flat.Add(false);
                    weldTo.Add(o + sp.Weld[i]);
                }
                foreach (int ti in st) tris.Add(o + ti);

                // The flat base. A cumulus base IS the condensation level and is dead flat; the shader cuts every
                // puff off there, but a cut sphere is a bowl, and looking up at the old cloud you saw the inside of
                // a ring of bowls instead of a base. Cap each puff that crosses the base with a disc lying on it,
                // the rim following the displaced surface (2 % inside it, so no rim pokes out of its own puff).
                if (pc.y >= pr || pc.y <= -pr) continue;
                float rc = Mathf.Sqrt(Mathf.Max(0f, pr * pr - pc.y * pc.y));
                int seg = Mathf.Max(10, lon);
                int centreIdx = verts.Count;
                verts.Add(new Vector3(pc.x, CapY, pc.z)); norms.Add(Vector3.down);
                cols.Add(Color.white); owner.Add(pi); flat.Add(true); weldTo.Add(centreIdx);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a) * rc, -pc.y, Mathf.Sin(a) * rc).normalized;
                    float f = Fbm3((pc + dir * pr) / pl + grain);
                    Vector3 rim = pc + dir * (pr * (1f + Amp * (f - 0.55f))) * 0.98f;
                    verts.Add(new Vector3(rim.x, CapY, rim.z)); norms.Add(Vector3.down);
                    cols.Add(Color.white); owner.Add(pi); flat.Add(true); weldTo.Add(verts.Count - 1);
                }
                for (int i = 0; i < seg; i++) { tris.Add(centreIdx); tris.Add(centreIdx + 1 + i); tris.Add(centreIdx + 2 + i); }
            }

            // How exposed each vertex is: 1 = clear air round it, 0 = deep inside a neighbour. A vertex further
            // inside another puff than the largest inward displacement (Amp × 0.55 = 38.5 % of its radius) is
            // certainly under that puff's surface, whatever the noise did to it — so 45 % is a safe cull.
            // Which puffs can possibly touch which: a puff's own surface reaches 1.35 × its radius and a neighbour's
            // influence 1.3 × its own, so anything further apart than the sum cannot matter. Testing every puff
            // against every vertex instead was a third of the startup cost.
            var near = new List<int>[c.Puffs.Count];
            for (int a = 0; a < c.Puffs.Count; a++)
            {
                near[a] = new List<int>();
                for (int b = 0; b < c.Puffs.Count; b++)
                {
                    if (b == a) continue;
                    float sum = c.Puffs[a].r * 1.35f + c.Puffs[b].r * 1.3f;
                    if ((c.Puffs[a].c - c.Puffs[b].c).sqrMagnitude < sum * sum) near[a].Add(b);
                }
            }
            var buried = new bool[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                float deepest = 0f, crowd = 0f;
                List<int> ns = near[owner[i]];
                for (int k = 0; k < ns.Count; k++)
                {
                    var (oc, orad) = c.Puffs[ns[k]];
                    float reach = orad * 1.3f, d2 = (p - oc).sqrMagnitude;
                    if (d2 > reach * reach) continue;
                    float d = Mathf.Sqrt(d2);
                    if (d < orad) deepest = Mathf.Max(deepest, (orad - d) / orad);
                    crowd += Mathf.Max(0f, 1f - d / reach);
                }
                buried[i] = deepest > 0.45f;
                // r = exposure (the crease shading); g was set with the vertex (the displacement noise).
                // The flat base takes ONE value for the whole cloud: a cumulus base is an even grey sheet, and a
                // per-vertex value interpolated across a disc's wedge-shaped fan drew the wedges instead of it.
                cols[i] = flat[i] ? new Color(0.5f, 0.5f, 0f, 1f)
                                  : new Color(Mathf.Clamp01(1f - crowd * 0.55f), cols[i].g, 0f, 1f);
            }
            var kept = new List<int>(tris.Count);
            for (int t = 0; t < tris.Count; t += 3)
                if (!(buried[tris[t]] && buried[tris[t + 1]] && buried[tris[t + 2]]))
                { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }

            // Normals from the displaced surface, area-weighted and accumulated through the template's weld map, so
            // the sphere's repeated seam column and its poles do not show as creases. The flat caps keep theirs.
            var sums = new Vector3[verts.Count];
            for (int t = 0; t < kept.Count; t += 3)
            {
                int ia = kept[t], ib = kept[t + 1], ic = kept[t + 2];
                if (flat[ia]) continue;
                Vector3 fn = Vector3.Cross(verts[ib] - verts[ia], verts[ic] - verts[ia]);   // unnormalised = area weight
                sums[weldTo[ia]] += fn; sums[weldTo[ib]] += fn; sums[weldTo[ic]] += fn;
            }
            for (int i = 0; i < verts.Count; i++)
            {
                if (flat[i]) continue;
                Vector3 n = sums[weldTo[i]];
                if (n.sqrMagnitude > 1e-8f) norms[i] = n.normalized;
            }

            // Drop the vertices no surviving triangle uses — in a cloud this dense that is most of them.
            var remap = new int[verts.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var fverts = new List<Vector3>(); var fnorms = new List<Vector3>(); var fcols = new List<Color>();
            for (int t = 0; t < kept.Count; t++)
            {
                int i = kept[t];
                if (remap[i] < 0) { remap[i] = fverts.Count; fverts.Add(verts[i]); fnorms.Add(norms[i]); fcols.Add(cols[i]); }
                kept[t] = remap[i];
            }

            var mesh = new Mesh { name = "Cumulus", indexFormat = fverts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(fverts); mesh.SetNormals(fnorms); mesh.SetColors(fcols); mesh.SetTriangles(kept, 0);
            mesh.bounds = new Bounds(new Vector3(0, height * 0.5f, 0), new Vector3(radius * 2.8f, height * 1.4f, radius * 2.8f));

            var go = new GameObject(th != null ? "ThermalCumulus" : "Cumulus");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, baseY, pos.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            c.R = go.AddComponent<MeshRenderer>();
            c.R.sharedMaterial = _mat;
            c.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.R.receiveShadows = false;
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
                _mpb.SetFloat(FadeId, 1f);
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

        // The noise the circles are broken with: value noise in three dimensions, the same KIND the wisp layers use
        // (Shaders/CloudWispSeed.cginc), so the clouds and the sheets below them break up with the same grain.
        // Build-time only, and that is why this is NOT the shader's sin-based hash: Noise3 is called four times per
        // vertex over 300,000 vertices, eight lattice corners each, and 30 million sines at scene build would show up
        // in the startup frame the Quest already struggles with. The lattice coordinates are integers, so hash their
        // bits instead (the multipliers are Teschner's spatial-hashing primes).
        private static float Hash3(float x, float y, float z)
        {
            uint h = (uint)((int)x * 73856093) ^ (uint)((int)y * 19349663) ^ (uint)((int)z * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFFFu) * (1f / 0x1000000);
        }

        private static float Noise3(Vector3 p)
        {
            float ix = Mathf.Floor(p.x), iy = Mathf.Floor(p.y), iz = Mathf.Floor(p.z);
            float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float n00 = Mathf.Lerp(Hash3(ix, iy, iz), Hash3(ix + 1f, iy, iz), fx);
            float n10 = Mathf.Lerp(Hash3(ix, iy + 1f, iz), Hash3(ix + 1f, iy + 1f, iz), fx);
            float n01 = Mathf.Lerp(Hash3(ix, iy, iz + 1f), Hash3(ix + 1f, iy, iz + 1f), fx);
            float n11 = Mathf.Lerp(Hash3(ix, iy + 1f, iz + 1f), Hash3(ix + 1f, iy + 1f, iz + 1f), fx);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, fy), Mathf.Lerp(n01, n11, fy), fz);
        }

        /// <summary>Three octaves, 0..1; <paramref name="p"/> is in units of the coarsest lump.</summary>
        private static float Fbm3(Vector3 p)
            => Noise3(p) * 0.55f
             + Noise3(p * 2.07f + new Vector3(19.3f, 7.1f, 31.7f)) * 0.30f
             + Noise3(p * 4.31f + new Vector3(3.9f, 27.4f, 11.2f)) * 0.15f;

        /// <summary>
        /// A unit sphere at one tessellation: directions, triangles, and the WELD map that says which of its vertices
        /// are the same point. A lat-long sphere repeats its seam column (j = lon is j = 0) and fans both poles, and
        /// the rebuilt normals have to be accumulated per POINT or those seams show as creases down the cloud. The
        /// map is structural, so the welding costs an array index instead of a hash lookup — over four hundred
        /// thousand triangles that was a fifth of a second of the startup frame.
        /// </summary>
        private sealed class Blob
        {
            public Vector3[] Dir; public int[] Tri; public int[] Weld;
        }

        private static Blob UnitSphere(int lon, int lat)
        {
            int key = lon * 1000 + lat;
            if (_spheres.TryGetValue(key, out Blob cached)) return cached;
            var dir = new Vector3[(lat + 1) * (lon + 1)];
            var weld = new int[dir.Length];
            for (int i = 0; i <= lat; i++)
            {
                float th = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = 2f * Mathf.PI * j / lon;
                    int k = i * (lon + 1) + j;
                    dir[k] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    weld[k] = i == 0 ? 0 : i == lat ? lat * (lon + 1) : i * (lon + 1) + (j == lon ? 0 : j);
                }
            }
            var tri = new int[lat * lon * 6];
            int n = 0;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    tri[n++] = a; tri[n++] = a + 1; tri[n++] = b;
                    tri[n++] = a + 1; tri[n++] = b + 1; tri[n++] = b;
                }
            var blob = new Blob { Dir = dir, Tri = tri, Weld = weld };
            _spheres[key] = blob;
            return blob;
        }
    }
}

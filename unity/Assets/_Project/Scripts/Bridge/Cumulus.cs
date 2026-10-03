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
        private static Mesh _sphere;
        private static Material _mat;
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
            _sphere ??= BuildSphere(14, 10);
            if (_mat == null)
            {
                Shader sh = Shader.Find("FlyingGame/Cumulus") ?? Shader.Find("Unlit/Color");
                _mat = new Material(sh);
            }
            var c = new Cloud { Th = th, BaseY = baseY, TopY = baseY + height, RadiusM = radius };
            var rng = new System.Random(9173 + seed * 7919);
            float U(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Base ring: wide, flat-bottomed lobes (the lower third of each puff is cut off at the base).
            int ring = 7 + rng.Next(3);
            for (int i = 0; i < ring; i++)
            {
                float a = (i + U(-0.25f, 0.25f)) / ring * Mathf.PI * 2f;
                float pr = radius * U(0.30f, 0.42f);
                float d = radius - pr * U(0.8f, 1.0f);
                c.Puffs.Add((new Vector3(Mathf.Cos(a) * d, pr * U(0.25f, 0.45f), Mathf.Sin(a) * d), pr));
            }
            // Body over the middle of the base.
            for (int i = 0; i < 3; i++)
            {
                float pr = radius * U(0.42f, 0.55f);
                c.Puffs.Add((new Vector3(U(-0.3f, 0.3f) * radius, pr * U(0.35f, 0.6f), U(-0.3f, 0.3f) * radius), pr));
            }
            // Cauliflower crown: turrets climbing to the top, shrinking as they go, with side lobes.
            int levels = 4 + rng.Next(3);
            for (int j = 1; j <= levels; j++)
            {
                float t = j / (float)levels;
                float pr = radius * Mathf.Lerp(0.5f, 0.24f, t) * U(0.85f, 1.15f);
                float y = Mathf.Lerp(height * 0.3f, height - pr, t);
                float spread = (1f - t * 0.7f) * radius * 0.35f;
                Vector3 centre = new(U(-1f, 1f) * spread, y, U(-1f, 1f) * spread);
                c.Puffs.Add((centre, pr));
                int lobes = 1 + rng.Next(2);
                for (int k = 0; k < lobes; k++)
                {
                    float a = U(0f, Mathf.PI * 2f), lr = pr * U(0.55f, 0.8f);
                    c.Puffs.Add((centre + new Vector3(Mathf.Cos(a) * pr * 0.75f, U(-0.2f, 0.25f) * pr, Mathf.Sin(a) * pr * 0.75f), lr));
                }
            }

            // One mesh per cloud.
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            Vector3[] sv = _sphere.vertices; Vector3[] sn = _sphere.normals; int[] st = _sphere.triangles;
            foreach (var (pc, pr) in c.Puffs)
            {
                int o = verts.Count;
                for (int i = 0; i < sv.Length; i++) { verts.Add(pc + sv[i] * pr); norms.Add(sn[i]); }
                foreach (int ti in st) tris.Add(o + ti);
            }
            var mesh = new Mesh { name = "Cumulus", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0, height * 0.5f, 0), new Vector3(radius * 2.6f, height * 1.3f, radius * 2.6f));

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
            if (_whiteout <= 0.01f || SessionSettings.MenuOpen || Event.current.type != EventType.Repaint) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            GUI.depth = 100;   // under the HUD, over the world
            GUI.color = new Color(0.9f, 0.92f, 0.95f, 0.96f * _whiteout);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
        }

        private static Mesh BuildSphere(int lon, int lat)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float th = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = 2f * Mathf.PI * j / lon;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(v); m.SetTriangles(t, 0);
            return m;
        }
    }
}

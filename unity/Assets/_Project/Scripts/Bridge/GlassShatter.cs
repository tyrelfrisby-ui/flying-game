using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The canopy, windshield and windows: tough plastic that shatters only on a direct hit (Ty 2026-11-05 Q028b; the rule is
    /// Core <see cref="Glazing"/>, the sim's half Sim AircraftGlazing).
    ///
    /// Two jobs. First, MEASURE: every flight, once the model (or the procedural shell) is built, the drawn glass is found —
    /// the model's transparent or glass-named meshes, never an instrument cover, a fuel sight, a light or the Lexan floor
    /// windows — and each pane's outermost vertices are handed to the sim as its sensor points (<c>Aircraft.SetGlazing</c>),
    /// so the glass shatters where the glass the pilot sees is, on every aeroplane. Second, SHATTER: on
    /// <see cref="ContactEventKind.GlazingShattered"/> the pane is drawn broken: the jagged ring of glass still held in its
    /// frame (bigger pieces left the further they are from the blow, a saw-toothed edge), its fracture edges picked out
    /// pale, and a spray of shards thrown off from the blow, which tumble down and lie where they land — the same language
    /// as the tree's thrown bits (<see cref="TreeDebris"/>). The remnant hangs on the pane's own transform, so a canopy that
    /// slides, opens or is jettisoned carries it. Cosmetic: the sim has already done the physics. Installed on
    /// StructuralDamage's object (StructuralDamage.Repair), like TreeDebris.
    /// </summary>
    public sealed class GlassShatter : MonoBehaviour
    {
        private sealed class Pane
        {
            public string Id;
            public GlazingKind Kind;
            public readonly List<(Renderer R, int[] Subs)> Parts = new();
            public bool Broken;
        }
        private sealed class Shard { public Transform T; public Vector3 V, Spin; public float Age; public bool Resting; }
        private sealed class Undo { public Renderer R; public Mesh Mesh; public bool Enabled; public GameObject Remnant; }

        private const float ShardLifeS = 12f;
        private const int MaxShards = 160;
        /// <summary>Most triangles a remnant is subdivided into (per renderer), and the target edge of a subdivided triangle.</summary>
        private const int MaxRemnantTris = 9000;
        private const float TargetEdgeM = 0.035f;

        private FlightSimDriver _driver;
        private AirframeVisual _visual;
        private FlyingGame.Sim.Aircraft _watched;
        private Transform _measuredRoot;   // the model root (or the driver, for the shell) the panes were measured on
        private bool _measured;
        private int _tries;
        private float _nextTry;
        private readonly Dictionary<string, Pane> _panes = new();
        private readonly List<Shard> _shards = new();
        private readonly List<Undo> _undo = new();
        private static Mesh _shardMesh;
        private static Material _edgeMat, _shardMat;

        private void Awake() { _driver = GetComponent<FlightSimDriver>(); _visual = GetComponent<AirframeVisual>(); }

        private void OnDisable() { Watch(null); }

        private void OnDestroy()
        {
            Watch(null);
            foreach (Shard s in _shards) if (s.T != null) Destroy(s.T.gameObject);
            _shards.Clear();
        }

        private void Watch(FlyingGame.Sim.Aircraft ac)
        {
            if (ReferenceEquals(ac, _watched)) return;
            if (_watched != null) _watched.Contact -= OnContact;
            _watched = ac;
            Restore();
            _measured = false; _measuredRoot = null; _panes.Clear(); _tries = 0;
            if (ac != null) ac.Contact += OnContact;
        }

        /// <summary>A new aeroplane is whole: every pane drawn broken on the old one is put back (the model is usually rebuilt
        /// anyway; when it is not, this is what mends it).</summary>
        private void Restore()
        {
            foreach (Undo u in _undo)
            {
                if (u.Remnant != null) Destroy(u.Remnant);
                if (u.R == null) continue;
                u.R.enabled = u.Enabled;
                if (u.Mesh != null && u.R.TryGetComponent(out MeshFilter mf)) mf.sharedMesh = u.Mesh;
            }
            _undo.Clear();
        }

        private void Update()
        {
            if (_visual == null) _visual = GetComponent<AirframeVisual>();
            Watch(_driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null);
            if (_watched != null && _visual != null)
            {
                Transform root = _visual.ModelRoot;
                // Measure once the airframe is drawn (it is built after the aeroplane is made), again when the model or the
                // shell is rebuilt, and retry for a few seconds while nothing has been found.
                bool stale = false;
                foreach (Pane p in _panes.Values) foreach ((Renderer r, _) in p.Parts) if (r == null) stale = true;
                bool retry = _measured && _panes.Count == 0 && _tries < 10 && Time.unscaledTime >= _nextTry;
                if (!_measured || root != _measuredRoot || stale || retry)
                {
                    _tries = retry ? _tries + 1 : 0;
                    _nextTry = Time.unscaledTime + 0.5f;
                    Measure(root);
                }
            }
            StepShards(Time.deltaTime);
        }

        /// <summary>For an editor tool (no driver, no Update): watch <paramref name="ac"/> and measure the glass under
        /// <paramref name="modelRoot"/> (null: the procedural shell under this object) now. This object must be the
        /// airframe's root, whose local axes are the body's about the CG.</summary>
        public int SetActiveForTesting(FlyingGame.Sim.Aircraft ac, Transform modelRoot)
        {
            Watch(ac);
            Measure(modelRoot);
            return _panes.Count;
        }

        /// <summary>The measured panes (editor tools): id, kind, and the sensor points handed to the sim.</summary>
        public IEnumerable<(string Id, GlazingKind Kind, bool Broken)> PanesForTesting()
        {
            foreach (Pane p in _panes.Values) yield return (p.Id, p.Kind, p.Broken);
        }

        /// <summary>Raise a shatter as the sim would (editor tools).</summary>
        public void ShatterForTesting(ContactEvent e) => OnContact(e);

        /// <summary>Fly the shards on by <paramref name="seconds"/> (editor tools).</summary>
        public void StepShardsForTesting(float seconds) { for (float t = 0; t < seconds; t += 0.02f) StepShards(0.02f); }

        // ---- measuring the glass ----------------------------------------------------------------------------------------

        private static readonly System.Text.RegularExpressions.Regex GlassName = new(
            @"glass|window|windscreen|windshield|canopy|transparent|verre|perspex|plexi|acryl|skylight",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex NotGlass = new(
            @"inclinometer|instrument|dial|gauge|compass|fuel|sight|hose|lens|(?<!sky)light|lamp|edge|nav|strobe|beacon|card|placard|lcd|display|screen_face|avionics|lexan|floor|frame|trim|latch|hinge|seal|sill|bow|rail|handle|knob|jettison|hardware|red|green|propdisc|prop_disc|disc|shadow|decal",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>Find the drawn glass, hand its panes to the sim.</summary>
        private void Measure(Transform modelRoot)
        {
            _panes.Clear();
            _measured = true;
            _measuredRoot = modelRoot;
            if (_watched.GlazingMaterial == GlazingMaterial.None) return;
            var found = new List<Pane>();
            if (modelRoot != null)
            {
                foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is not MeshRenderer && r is not SkinnedMeshRenderer) continue;
                    Mesh mesh = MeshOf(r);
                    if (mesh == null || !mesh.isReadable || mesh.vertexCount < 3) continue;
                    string node = CanopyEgress.Bare(r.name);
                    var subs = new List<int>();
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                    {
                        Material m = mats[i];
                        if (m == null) continue;
                        string mn = m.name.Replace(" (Instance)", "");
                        bool named = GlassName.IsMatch(mn) || (mats.Length == 1 && GlassName.IsMatch(node));
                        bool clear = m.renderQueue >= 2900;
                        if (!named && !clear) continue;
                        if (NotGlass.IsMatch(mn) || NotGlass.IsMatch(node)) continue;
                        subs.Add(i);
                    }
                    if (subs.Count == 0) continue;
                    // A light lens or a gauge cover is a few centimetres; a pane is decimetres.
                    Bounds b = r.bounds;
                    if (b.size.magnitude < 0.15f) continue;
                    string id = node;
                    for (int k = 2; _panes.ContainsKey(id) || found.Exists(p => p.Id == id); k++) id = node + "#" + k;
                    var pane = new Pane { Id = id };
                    pane.Parts.Add((r, subs.ToArray()));
                    found.Add(pane);
                }
            }
            else
            {
                // The procedural shell: its one glass dome stands for the type's glazing.
                Transform dome = transform.Find("Canopy");
                if (dome != null && dome.TryGetComponent(out MeshRenderer mr))
                {
                    var pane = new Pane { Id = "canopy" };
                    pane.Parts.Add((mr, new[] { 0 }));
                    found.Add(pane);
                }
            }
            var simPanes = new List<GlazingPane>();
            Vec3 cg = _watched.Config.Mass.CgVec();
            CockpitEnclosure enc = Glazing.EnclosureFor(_watched.Config.Id);
            foreach (Pane p in found)
            {
                List<Vector3> pts = SamplePoints(p, 8, out Vector3 meanNormal, out Vector3 centre);
                if (pts.Count == 0) continue;
                p.Kind = KindOf(p.Id, meanNormal, centre, enc);
                var body = new Vec3[pts.Count];
                for (int i = 0; i < pts.Count; i++) body[i] = CoordinateMap.ToSim(pts[i]) + cg;   // driver-local is CG-relative body
                simPanes.Add(Glazing.Pane(p.Id, p.Kind, body));
                _panes[p.Id] = p;
            }
            if (simPanes.Count > 0) _watched.SetGlazing(simPanes);
            Debug.Log($"[Glass] {_watched.Config.Id}: {simPanes.Count} pane(s) measured on the {(modelRoot != null ? "model" : "shell")} "
                + $"({_watched.GlazingMaterial}, shatters above {Glazing.ShatterClosingMs(_watched.GlazingMaterial):0.#} m/s direct): "
                + string.Join(", ", simPanes.ConvertAll(q => $"{q.Id} {q.Kind}")) + (simPanes.Count == 0 ? "none drawn; the sim keeps its estimated panes" : ""));
        }

        private static GlazingKind KindOf(string id, Vector3 n, Vector3 c, CockpitEnclosure enc)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("windscreen") || s.Contains("windshield")) return enc == CockpitEnclosure.Open ? GlazingKind.Windscreen : GlazingKind.Windshield;
            if (s.Contains("canopy")) return GlazingKind.Canopy;
            // Driver-local: x right, y up, z forward. A pane facing sideways is a window; forward, a windshield; up, the canopy
            // (a cabin's roof glass is a window).
            if (Mathf.Abs(n.x) > 0.7f) return GlazingKind.SideWindow;
            if (n.z > 0.45f) return enc == CockpitEnclosure.Open ? GlazingKind.Windscreen : GlazingKind.Windshield;
            if (n.y > 0.6f) return enc == CockpitEnclosure.Canopy ? GlazingKind.Canopy : GlazingKind.Window;
            return GlazingKind.Window;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer sk) return sk.sharedMesh;
            return r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
        }

        /// <summary>The transform from <paramref name="t"/>'s local space to <paramref name="ancestor"/>'s, built from the
        /// local TRS chain (never through world space: the aeroplane may be kilometres from the origin).</summary>
        private static Matrix4x4 ToAncestor(Transform t, Transform ancestor)
        {
            Matrix4x4 m = Matrix4x4.identity;
            for (Transform x = t; x != null && x != ancestor; x = x.parent)
                m = Matrix4x4.TRS(x.localPosition, x.localRotation, x.localScale) * m;
            return m;
        }

        /// <summary>Up to <paramref name="n"/> of the pane's vertices spread over it (farthest-point sampling), in the
        /// driver's local frame; the pane's outward mean normal and centre too.</summary>
        private List<Vector3> SamplePoints(Pane p, int n, out Vector3 meanNormal, out Vector3 centre)
        {
            var all = new List<Vector3>();
            var nrm = Vector3.zero;
            foreach ((Renderer r, int[] subs) in p.Parts)
            {
                Mesh mesh = MeshOf(r);
                Matrix4x4 m = ToAncestor(r.transform, transform);
                Vector3[] v = mesh.vertices;
                foreach (int sub in subs)
                {
                    int[] tri = mesh.GetTriangles(sub);
                    int stride = Mathf.Max(1, tri.Length / 3 / 600) * 3;
                    for (int i = 0; i + 2 < tri.Length; i += stride)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                        all.Add(a); all.Add(b); all.Add(c);
                        Vector3 fn = Vector3.Cross(b - a, c - a);
                        Vector3 g = Outward((a + b + c) / 3f);
                        nrm += Vector3.Dot(fn, g) >= 0 ? fn : -fn;
                    }
                }
            }
            meanNormal = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.up;
            centre = Vector3.zero;
            if (all.Count == 0) return all;
            foreach (Vector3 a in all) centre += a;
            centre /= all.Count;
            var pts = new List<Vector3>();
            // Start from the vertex farthest out along the normal (the glass's outermost point), then spread.
            int best = 0; float bd = float.NegativeInfinity;
            for (int i = 0; i < all.Count; i++) { float d = Vector3.Dot(all[i] - centre, meanNormal); if (d > bd) { bd = d; best = i; } }
            pts.Add(all[best]);
            var dist = new float[all.Count];
            for (int i = 0; i < all.Count; i++) dist[i] = (all[i] - pts[0]).sqrMagnitude;
            while (pts.Count < n)
            {
                int far = 0; float fd = -1;
                for (int i = 0; i < all.Count; i++) if (dist[i] > fd) { fd = dist[i]; far = i; }
                if (fd < 0.01f) break;   // nothing more than 10 cm from the points so far
                pts.Add(all[far]);
                for (int i = 0; i < all.Count; i++) dist[i] = Mathf.Min(dist[i], (all[i] - all[far]).sqrMagnitude);
            }
            return pts;
        }

        /// <summary>Away from the fuselage's axis (driver-local: the CG line, x right, y up), for a point on its glass.</summary>
        private static Vector3 Outward(Vector3 p)
        {
            var g = new Vector3(p.x, p.y, 0f);
            return g.sqrMagnitude > 1e-6f ? g.normalized : Vector3.up;
        }

        // ---- shattering ---------------------------------------------------------------------------------------------------

        private void OnContact(ContactEvent e)
        {
            if (e.Kind != ContactEventKind.GlazingShattered || _watched == null) return;
            Vector3 hitWorld = CoordinateMap.ToUnity(e.PositionWorld), vWorld = CoordinateMap.ToUnity(e.VelocityWorld);
            RigidBodyState s = _watched.State;
            Vector3 hitLocal = CoordinateMap.ToUnity(s.Attitude.Conjugate().Rotate(e.PositionWorld - s.Position));
            if (!_panes.TryGetValue(e.Part, out Pane pane))
            {
                // The sim's estimated panes (nothing was measured): break the drawn glass nearest the blow, if any.
                pane = Nearest(hitLocal);
                if (pane == null) { Debug.Log($"[Glass] {e.Part} shattered ({e.Intensity:0.0} m/s); nothing drawn to break"); Spray(hitWorld, vWorld, 10); return; }
            }
            if (pane.Broken) return;
            pane.Broken = true;
            int seed = e.Part.GetHashCode() ^ (int)(Time.frameCount * 2654435761u);
            int removed = 0;
            foreach ((Renderer r, int[] subs) in pane.Parts) if (r != null) removed += BreakRenderer(r, subs, hitLocal, seed++);
            Debug.Log($"[Glass] {_watched.Config.Id}: {pane.Id} ({pane.Kind}) shattered at {e.Intensity:0.0} m/s; {removed} fragments gone");
            Spray(hitWorld, vWorld, Mathf.Clamp(8 + removed / 60, 10, 36));
        }

        private Pane Nearest(Vector3 local)
        {
            Pane best = null; float bd = float.MaxValue;
            foreach (Pane p in _panes.Values)
            {
                if (p.Broken) continue;
                foreach ((Renderer r, _) in p.Parts)
                {
                    if (r == null) continue;
                    Vector3 c = ToAncestor(r.transform, transform).MultiplyPoint3x4(MeshOf(r).bounds.center);
                    float d = (c - local).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
            }
            return bd < 4f ? best : null;
        }

        /// <summary>Draw one renderer's glass submeshes broken: hide them and hang the jagged remnant and its fracture edges on
        /// the renderer's transform. Returns how many subdivided fragments went.</summary>
        private int BreakRenderer(Renderer r, int[] subs, Vector3 hitDriverLocal, int seed)
        {
            Mesh src = MeshOf(r);
            if (src == null || !src.isReadable) return 0;
            var undo = new Undo { R = r, Enabled = r.enabled };
            Matrix4x4 toDriver = ToAncestor(r.transform, transform);
            Vector3 hit = toDriver.inverse.MultiplyPoint3x4(hitDriverLocal);   // the blow in the renderer's own frame
            Material glassMat = null;
            var shown = new List<int>();
            for (int i = 0; i < src.subMeshCount; i++) if (System.Array.IndexOf(subs, i) < 0) shown.Add(i);
            if (r is MeshRenderer && shown.Count > 0 && r.TryGetComponent(out MeshFilter mf))
            {
                // Some of this renderer is not glass (a frame drawn in the same mesh): keep that, drop only the glass.
                Mesh keep = Instantiate(src);
                foreach (int sub in subs) keep.SetTriangles(System.Array.Empty<int>(), sub);
                undo.Mesh = src;
                mf.sharedMesh = keep;
            }
            else r.enabled = false;
            Material[] mats = r.sharedMaterials;
            if (subs.Length > 0 && subs[0] < mats.Length) glassMat = mats[subs[0]];

            var b = new RemnantBuilder(src, subs, toDriver, hit, seed);
            Mesh remnant = b.Build(out Mesh edges);
            var go = new GameObject("GlassRemnant");
            go.transform.SetParent(r.transform, false);
            if (remnant != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = remnant;
                go.AddComponent<MeshRenderer>().sharedMaterial = Crazed(glassMat);
            }
            if (edges != null)
            {
                var eg = new GameObject("GlassFractureEdges");
                eg.transform.SetParent(go.transform, false);
                eg.AddComponent<MeshFilter>().sharedMesh = edges;
                eg.AddComponent<MeshRenderer>().sharedMaterial = EdgeMat();
            }
            undo.Remnant = go;
            _undo.Add(undo);
            return b.Removed;
        }

        /// <summary>The glass that is left, as cracked acrylic is: the pane's own material, paler and less clear.</summary>
        private static Material Crazed(Material m)
        {
            if (m == null) return EdgeMat();
            var c = new Material(m) { name = m.name + " (shattered)" };
            foreach (string prop in new[] { "baseColorFactor", "_BaseColor", "_Color" })
            {
                if (!c.HasProperty(prop)) continue;
                Color col = c.GetColor(prop);
                col = Color.Lerp(col, new Color(0.88f, 0.92f, 0.95f, col.a), 0.45f);
                col.a = Mathf.Clamp(col.a * 1.6f + 0.08f, 0.15f, 0.7f);
                c.SetColor(prop, col);
            }
            return c;
        }

        private static Material EdgeMat() => _edgeMat ??= new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = new Color(0.93f, 0.96f, 1f, 0.85f), name = "GlassFractureEdge" };
        private static Material ShardMat() => _shardMat ??= new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = new Color(0.82f, 0.9f, 0.96f, 0.6f), name = "GlassShard" };

        // ---- the shards -------------------------------------------------------------------------------------------------

        /// <summary>Shards thrown from the blow at the glass's own speed, less what breaking took, and scattered.</summary>
        private void Spray(Vector3 at, Vector3 v, int count)
        {
            for (int i = 0; i < count && _shards.Count < MaxShards; i++)
            {
                var go = new GameObject("GlassShard");
                go.AddComponent<MeshFilter>().sharedMesh = ShardMesh();
                go.AddComponent<MeshRenderer>().sharedMaterial = ShardMat();
                // Most shards are a few centimetres; now and then a long sliver.
                float size = Random.value < 0.15f ? Random.Range(0.10f, 0.22f) : Random.Range(0.025f, 0.08f);
                go.transform.localScale = new Vector3(size * Random.Range(0.5f, 1.2f), size, size * Random.Range(0.6f, 1.4f));
                go.transform.position = at + Random.insideUnitSphere * 0.25f;
                go.transform.rotation = Random.rotation;
                _shards.Add(new Shard
                {
                    T = go.transform,
                    V = v * Random.Range(0.45f, 0.9f) + Random.insideUnitSphere * 5f + Vector3.up * 2f,
                    Spin = Random.insideUnitSphere * 900f,
                });
            }
        }

        private void StepShards(float dt)
        {
            for (int i = _shards.Count - 1; i >= 0; i--)
            {
                Shard p = _shards[i];
                p.Age += dt;
                if (p.T == null || p.Age > ShardLifeS) { if (p.T != null) Destroy(p.T.gameObject); _shards.RemoveAt(i); continue; }
                if (p.Resting) continue;
                // Thin flakes: gravity, and a drag that takes their speed in a couple of seconds; they flutter as they fall.
                p.V += Vector3.down * (9.81f * dt);
                p.V *= Mathf.Exp(-1.2f * dt);
                Vector3 pos = p.T.position + p.V * dt;
                p.T.rotation = Quaternion.Euler(p.Spin * dt) * p.T.rotation;
                Vec3 sim = CoordinateMap.ToSim(pos);
                double floor = WorldTerrain.GroundHeightAt(sim.X, sim.Y);
                double? water = FloatHydro.WaterSurfaceAt(sim.X, sim.Y, pos.y);
                if (water.HasValue && pos.y < water.Value)
                {
                    // Acrylic is a little denser than water: it goes under where it lands.
                    Destroy(p.T.gameObject); _shards.RemoveAt(i); continue;
                }
                if (pos.y < floor) { pos.y = (float)floor + 0.005f; p.Resting = true; p.T.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f)); }
                p.T.position = pos;
            }
        }

        /// <summary>A unit shard: an irregular flat quadrilateral, double-sided (scaled per piece).</summary>
        private static Mesh ShardMesh()
        {
            if (_shardMesh != null) return _shardMesh;
            var m = new Mesh { name = "GlassShard" };
            var v = new[] { new Vector3(-0.5f, 0f, -0.35f), new Vector3(0.15f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.2f), new Vector3(-0.1f, 0f, 0.5f) };
            m.vertices = new[] { v[0], v[1], v[2], v[3], v[0], v[1], v[2], v[3] };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.down, Vector3.down, Vector3.down, Vector3.down };
            m.RecalculateBounds();
            return _shardMesh = m;
        }

        // ---- the remnant ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The jagged ring left in the frame. The pane's glass submeshes are subdivided evenly (midpoint subdivision, shared
        /// midpoints, so the result has no T-junctions) to about <see cref="TargetEdgeM"/>; each small triangle is kept if it
        /// is within a saw-toothed band of the pane's rim, the rim being the outline of the pane seen along its outward mean
        /// normal (a convex hull, which needs no mesh topology: the models' glass is often two coincident layers, or split at
        /// UV seams). The band widens with distance from the blow, so the far side keeps larger pieces. The fracture edges are
        /// the kept set's own boundary where the original mesh had none, drawn as a thin pale ribbon.
        /// </summary>
        private sealed class RemnantBuilder
        {
            private readonly List<Vector3> _v = new(), _n = new();
            private readonly List<Vector2> _uv = new();
            private List<int> _t = new();
            private readonly bool _hasUv, _hasN;
            private readonly Vector3 _hit;
            private readonly System.Random _rng;
            private readonly Matrix4x4 _toDriver;
            public int Removed;

            public RemnantBuilder(Mesh src, int[] subs, Matrix4x4 toDriver, Vector3 hitLocal, int seed)
            {
                _toDriver = toDriver;
                _hit = hitLocal;
                _rng = new System.Random(seed);
                Vector3[] v = src.vertices; Vector3[] n = src.normals; Vector2[] uv = src.uv;
                _hasN = n != null && n.Length == v.Length; _hasUv = uv != null && uv.Length == v.Length;
                var map = new Dictionary<int, int>();
                foreach (int sub in subs)
                {
                    int[] tri = src.GetTriangles(sub);
                    foreach (int i in tri)
                    {
                        if (!map.TryGetValue(i, out int j))
                        {
                            j = _v.Count; map[i] = j;
                            _v.Add(v[i]); _n.Add(_hasN ? n[i] : Vector3.up); _uv.Add(_hasUv ? uv[i] : Vector2.zero);
                        }
                        _t.Add(j);
                    }
                }
            }

            public Mesh Build(out Mesh edges)
            {
                edges = null;
                if (_t.Count < 3) return null;
                int tris0 = _t.Count / 3;
                // Even subdivision: levels until the longest edge is near the target, as far as the budget allows.
                float longest = 0;
                for (int i = 0; i < _t.Count; i += 3)
                    longest = Mathf.Max(longest, Mathf.Max((_v[_t[i]] - _v[_t[i + 1]]).magnitude, Mathf.Max((_v[_t[i + 1]] - _v[_t[i + 2]]).magnitude, (_v[_t[i + 2]] - _v[_t[i]]).magnitude)));
                float scale = _toDriver.lossyScale.x;
                int want = longest * scale > TargetEdgeM ? Mathf.CeilToInt(Mathf.Log(longest * scale / TargetEdgeM, 2f)) : 0;
                int budget = Mathf.FloorToInt(Mathf.Log(Mathf.Max(1f, (float)MaxRemnantTris / tris0), 4f));
                int levels = Mathf.Clamp(Mathf.Min(want, budget), 0, 6);
                for (int l = 0; l < levels; l++) Subdivide();
                var original = EdgeUse(_t);

                // The pane's outward normal and its outline, in the renderer's frame.
                Vector3 c = Vector3.zero; foreach (Vector3 p in _v) c += p; c /= _v.Count;
                Vector3 g = _toDriver.inverse.MultiplyVector(Outward(_toDriver.MultiplyPoint3x4(c)));
                Vector3 nrm = Vector3.zero;
                for (int i = 0; i < _t.Count; i += 3)
                {
                    Vector3 fn = Vector3.Cross(_v[_t[i + 1]] - _v[_t[i]], _v[_t[i + 2]] - _v[_t[i]]);
                    nrm += Vector3.Dot(fn, g) >= 0 ? fn : -fn;
                }
                nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : g.normalized;
                Vector3 u = Vector3.Cross(nrm, Mathf.Abs(nrm.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(nrm, u);
                Vector2 P(Vector3 p) => new(Vector3.Dot(p - c, u), Vector3.Dot(p - c, w));
                var proj = new List<Vector2>(_v.Count);
                foreach (Vector3 p in _v) proj.Add(P(p));
                List<Vector2> hull = Hull(proj);
                if (hull.Count < 3) return null;
                Vector2 min = proj[0], max = proj[0];
                foreach (Vector2 q in proj) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
                float extent = Mathf.Min(max.x - min.x, max.y - min.y);
                // Saw teeth around the rim (a knot every 1/teeth of a turn), with now and then a long spike of glass.
                int teeth = 18 + _rng.Next(10);
                var knots = new float[teeth];
                for (int k = 0; k < teeth; k++) knots[k] = _rng.NextDouble() < 0.12 ? 1.6f + (float)_rng.NextDouble() : 0.25f + 0.75f * (float)_rng.NextDouble();
                float baseBand = Mathf.Clamp(0.16f * extent, 0.025f / Mathf.Max(scale, 1e-4f), 0.16f / Mathf.Max(scale, 1e-4f));
                Vector2 hit2 = P(_hit);
                float reach = Mathf.Max(max.x - min.x, max.y - min.y);

                var kept = new List<int>();
                for (int i = 0; i < _t.Count; i += 3)
                {
                    Vector2 q = (proj[_t[i]] + proj[_t[i + 1]] + proj[_t[i + 2]]) / 3f;
                    float d = InsideDistance(hull, q);
                    float a = (Mathf.Atan2(q.y, q.x) / (2f * Mathf.PI) + 1f) % 1f * teeth;
                    int k0 = Mathf.FloorToInt(a) % teeth, k1 = (k0 + 1) % teeth;
                    float saw = Mathf.Lerp(knots[k0], knots[k1], a - Mathf.Floor(a));
                    float far = Mathf.Clamp((q - hit2).magnitude / Mathf.Max(1e-4f, 0.5f * reach), 0.35f, 1.8f);
                    if (d < baseBand * saw * far) { kept.Add(_t[i]); kept.Add(_t[i + 1]); kept.Add(_t[i + 2]); }
                    else Removed++;
                }
                if (kept.Count == 0) return null;
                edges = FractureEdges(kept, original, nrm, extent);
                return Make("GlassRemnant", kept);
            }

            private Mesh Make(string name, List<int> tris)
            {
                var m = new Mesh { name = name };
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v);
                if (_hasN) m.SetNormals(_n);
                if (_hasUv) m.SetUVs(0, _uv);
                m.SetTriangles(tris, 0);
                if (!_hasN) m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }

            /// <summary>Every triangle into four, the midpoints shared between neighbours.</summary>
            private void Subdivide()
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    m = _v.Count;
                    _v.Add((_v[a] + _v[b]) * 0.5f);
                    _n.Add((_n[a] + _n[b]).normalized);
                    _uv.Add((_uv[a] + _uv[b]) * 0.5f);
                    mid[key] = m;
                    return m;
                }
                var t = new List<int>(_t.Count * 4);
                for (int i = 0; i < _t.Count; i += 3)
                {
                    int a = _t[i], b = _t[i + 1], c = _t[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    t.Add(a); t.Add(ab); t.Add(ca);
                    t.Add(ab); t.Add(b); t.Add(bc);
                    t.Add(ca); t.Add(bc); t.Add(c);
                    t.Add(ab); t.Add(bc); t.Add(ca);
                }
                _t = t;
            }

            private static Dictionary<long, int> EdgeUse(List<int> tris)
            {
                var use = new Dictionary<long, int>();
                for (int i = 0; i < tris.Count; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = tris[i + e], b = tris[i + (e + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        use.TryGetValue(key, out int n); use[key] = n + 1;
                    }
                return use;
            }

            /// <summary>The kept glass's new edges (used once by the kept set, twice by the whole pane) as a thin ribbon lying on
            /// the glass, a hair proud of both faces.</summary>
            private Mesh FractureEdges(List<int> kept, Dictionary<long, int> original, Vector3 nrm, float extent)
            {
                var keptUse = EdgeUse(kept);
                var v = new List<Vector3>(); var t = new List<int>();
                float width = Mathf.Clamp(0.004f / Mathf.Max(_toDriver.lossyScale.x, 1e-4f), 0.001f, 0.02f * Mathf.Max(extent, 0.01f));
                float lift = width * 0.4f;
                for (int i = 0; i < kept.Count; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = kept[i + e], b = kept[i + (e + 1) % 3], c = kept[i + (e + 2) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (keptUse[key] != 1 || !original.TryGetValue(key, out int n) || n < 2) continue;
                        Vector3 pa = _v[a], pb = _v[b], pc = _v[c];
                        Vector3 fn = Vector3.Cross(pb - pa, pc - pa).normalized;
                        Vector3 side = Vector3.Cross(fn, pb - pa).normalized;
                        if (Vector3.Dot(side, pc - pa) < 0) side = -side;   // into the kept triangle
                        foreach (float s in new[] { lift, -lift })
                        {
                            int o = v.Count;
                            Vector3 up = fn * s;
                            v.Add(pa + up); v.Add(pb + up); v.Add(pb + up + side * width); v.Add(pa + up + side * width);
                            t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);
                            t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2);
                        }
                    }
                if (t.Count == 0) return null;
                var m = new Mesh { name = "GlassFractureEdges" };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }

            /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
            private static List<Vector2> Hull(List<Vector2> pts)
            {
                var p = new List<Vector2>(pts);
                p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var h = new List<Vector2>();
                static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
                foreach (Vector2 q in p) { while (h.Count >= 2 && Cross(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
                int lower = h.Count + 1;
                for (int i = p.Count - 2; i >= 0; i--) { Vector2 q = p[i]; while (h.Count >= lower && Cross(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
                if (h.Count > 1) h.RemoveAt(h.Count - 1);
                return h;
            }

            /// <summary>Distance from a point inside the hull to its outline.</summary>
            private static float InsideDistance(List<Vector2> hull, Vector2 q)
            {
                float best = float.MaxValue;
                for (int i = 0; i < hull.Count; i++)
                {
                    Vector2 a = hull[i], b = hull[(i + 1) % hull.Count], ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - q).magnitude);
                }
                return best;
            }
        }
    }
}

using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The timber, drawn. <see cref="Forest"/> in Core decides where every tree in the world stands and how
    /// big it is, out of a hash of its own cell — so there is no list to load and nothing to keep in step.
    /// This draws a square of that forest on demand.
    ///
    /// Nothing static builds the whole forest, because the whole forest is millions of stems. Instead the
    /// chunks round the camera are built and thrown away as it moves (<see cref="ForestStreamer"/>), and the
    /// country beyond them reads as forest because the ground is TINTED with the same cover function the
    /// trees come from. The trees you cannot see are still there in the collision — fly into painted forest
    /// at 50 ft and you are in timber, which is the honest answer.
    /// </summary>
    public static class ForestBuilder
    {
        /// <summary>
        /// A chunk is 12 × 12 forest cells — 216 m. Building one asks the height field about 144 cells, and
        /// a cell costs about seven height queries, so a chunk is a thousand of them: a millisecond or two on
        /// the Mac and an unknown on the phone. 20 × 20 was the first guess and would have been three times
        /// that in one frame. The streamer no longer has to guess: it builds against a millisecond budget
        /// (<see cref="ForestStreamer.BudgetMs"/>) and reports the worst frame it has had.
        /// </summary>
        public const double ChunkM = Forest.CellM * 12;

        private static readonly Color Needle = new(0.14f, 0.24f, 0.17f);
        private static readonly Color NeedleLit = new(0.22f, 0.34f, 0.22f);
        private static readonly Color NeedleDry = new(0.26f, 0.30f, 0.17f);
        private static readonly Color Trunk = new(0.28f, 0.22f, 0.17f);

        private static Vector3 U(double x, double y, double up) => new((float)y, (float)up, (float)x);

        private static readonly SeasideBuilder.Batch ChunkBatch = new();

        public static int ChunkOf(double v) => (int)System.Math.Floor(v / ChunkM);

        /// <summary>
        /// One chunk of forest, or null if that square has no trees in it. <paramref name="detail"/> true draws
        /// the trunk and three skirts (close enough to see one properly); false draws the silhouette only.
        /// </summary>
        public static GameObject BuildChunk(WorldTerrain t, Transform parent, int ci, int cj, bool detail)
        {
            double x0 = ci * ChunkM, y0 = cj * ChunkM;
            // One batch, reused: its four lists used to regrow from empty for every chunk (several a second of flying),
            // hundreds of KB of garbage each time for a headset's collector. Main thread only, as every caller is.
            SeasideBuilder.Batch b = ChunkBatch;
            b.Clear();
            int n = 0;
            foreach (Forest.Tree tr in Forest.InRect(x0, y0, x0 + ChunkM, y0 + ChunkM))
            {
                // Owner-requested Powell visual clearance; the sim forest registry is unchanged.
                float desert = WorldBuilder.PowellRegionMask(tr.X, tr.Y, rimTrees: true);
                float keep = (float)(tr.TwistDeg / 360.0);
                if (desert > .001f && keep < desert) continue;
                Add(b, tr, detail);
                n++;
            }
            if (n == 0) return null;
            GameObject go = b.Build($"Forest_{ci}_{cj}", parent, SeasideBuilder.VC);
            return go;
        }

        /// <summary>Every chunk whose square falls within <paramref name="radiusM"/> of (x, y).</summary>
        public static List<GameObject> BuildAround(WorldTerrain t, Transform parent, double x, double y, double radiusM, double detailRadiusM = 600)
        {
            var made = new List<GameObject>();
            int i0 = ChunkOf(x - radiusM), i1 = ChunkOf(x + radiusM), j0 = ChunkOf(y - radiusM), j1 = ChunkOf(y + radiusM);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    double cx = (i + 0.5) * ChunkM - x, cy = (j + 0.5) * ChunkM - y;
                    double d = System.Math.Sqrt(cx * cx + cy * cy);
                    if (d > radiusM + ChunkM) continue;
                    GameObject go = BuildChunk(t, parent, i, j, d < detailRadiusM);
                    if (go != null) made.Add(go);
                }
            return made;
        }

        /// <summary>
        /// A conifer. The silhouette is two stacked four-sided spires — eight triangles, because there are
        /// thousands of them in sight at once and a box-built tree costs six times that for no more shape.
        /// Up close it gets a trunk and a third skirt.
        /// </summary>
        private static void Add(SeasideBuilder.Batch b, in Forest.Tree tr, bool detail)
        {
            float h = (float)tr.HeightM, r = (float)tr.CrownRadiusM;
            double g = tr.GroundM;
            // A little colour variation per tree so a stand is not one flat green: the hash is already in the
            // twist, so re-use it rather than drawing again.
            Color needle = Color.Lerp(Needle, NeedleDry, (float)tr.TwistDeg / 360f * 0.55f);
            Color lit = Color.Lerp(NeedleLit, needle, 0.35f);
            float twist = (float)tr.TwistDeg, lean = (float)tr.LeanDeg;
            Quaternion rot = Quaternion.Euler(lean, twist, 0);

            if (detail)
            {
                b.Box(U(tr.X, tr.Y, g + h * 0.16), new Vector3((float)tr.RadiusM * 1.2f, h * 0.34f, (float)tr.RadiusM * 1.2f), rot, Trunk);
                // The three skirts are the physics' crown (Core TreeStructure.Skirts): what a wing meets is what is drawn.
                var sk = FlyingGame.Core.TreeStructure.Skirts;
                for (int i = 0; i < sk.Length; i++)
                    Spire(b, tr, g + h * sk[i].Base, h * (float)sk[i].Rise, r * (float)sk[i].Radius, needle, lit, twist + (i == 0 ? 0 : i == 1 ? 23 : 47), lean);
            }
            else
            {
                Spire(b, tr, g + h * 0.20, h * 0.52f, r, needle, lit, twist, lean);
                Spire(b, tr, g + h * 0.62, h * 0.40f, r * 0.60f, needle, lit, twist + 31, lean);
            }
        }

        /// <summary>One skirt: a four-sided spire with its base at <paramref name="baseUp"/>.</summary>
        private static void Spire(SeasideBuilder.Batch b, in Forest.Tree tr, double baseUp, float riseM, float r,
                                  Color needle, Color lit, float twistDeg, float leanDeg)
        {
            Quaternion rot = Quaternion.Euler(leanDeg, twistDeg, 0);
            Vector3 c = U(tr.X, tr.Y, baseUp);
            Vector3 apex = c + rot * new Vector3(0, riseM, 0);
            Vector3 P(float sx, float sz) => c + rot * new Vector3(sx * r, 0, sz * r);
            // Uneven branch tips soften the regular pyramid at the same triangle budget.
            const int sides = 4;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / sides + Mathf.PI * .25f, a1 = (k + 1) * Mathf.PI * 2 / sides + Mathf.PI * .25f;
                float r0 = ((k & 1) == 0 ? 1f : .83f) * 1.414214f;
                float r1 = (((k + 1) & 1) == 0 ? 1f : .83f) * 1.414214f;
                b.Tri(P(Mathf.Cos(a0)*r0, Mathf.Sin(a0)*r0),
                      P(Mathf.Cos(a1)*r1, Mathf.Sin(a1)*r1), apex,
                      Color.Lerp(needle, lit, .25f + .25f * Mathf.Cos(a0 + twistDeg * Mathf.Deg2Rad)));
            }
        }
    }

    /// <summary>
    /// Keeps the forest built round the camera and nowhere else: one chunk a frame, so a long cross-country
    /// never costs a stall, and chunks that fall behind are destroyed. The forest is a function, so a chunk
    /// rebuilt an hour later comes back identical.
    /// </summary>
    public sealed class ForestStreamer : MonoBehaviour
    {
        /// <summary>How far out the stems are drawn. Beyond this the ground's own colour carries the forest.</summary>
        public double RadiusM = 1250;
        /// <summary>Within this, a tree gets its trunk and a third skirt.</summary>
        public double DetailRadiusM = 550;
        /// <summary>
        /// The most chunks to build in one frame — a ceiling, not a target. What actually limits it is
        /// <see cref="BudgetMs"/>: a chunk costs what it costs on the device you are holding, which is not a
        /// number I can know from here, so the streamer spends time rather than counting chunks.
        /// </summary>
        public int MaxPerFrame = 4;
        /// <summary>
        /// How long building forest may take in one frame. 2 ms of a 16.7 ms frame: the timber comes in over a
        /// second or two of flying rather than all at once, and never at the cost of a dropped frame.
        /// </summary>
        public double BudgetMs = 2.0;
        /// <summary>
        /// If the LAST frame already took longer than this, build nothing this one — the phone is busy with
        /// something else and the forest is the one thing here that can wait. 24 ms is a frame and a half.
        /// </summary>
        public double StallGuardMs = 24.0;
        /// <summary>...but never stall out completely: after this long with nothing built, build one anyway, or
        /// a device that is slow for its own reasons would fly over bare ground for ever.</summary>
        public double StarveAfterS = 0.5;

        private WorldTerrain _t;
        private Transform _root;
        private Camera _cam;
        private readonly Dictionary<(int, int), GameObject> _live = new();
        private readonly List<(int i, int j, double d)> _want = new();
        private double _lastX = double.NaN, _lastY;
        private float _lastBuiltAt;
        private double _worstMs;
        /// <summary>The longest a single frame has spent building forest, ms — what to look at on a device.</summary>
        public double WorstFrameMs => _worstMs;
        /// <summary>Chunks built and still standing, and chunks still wanted: the other two numbers worth seeing.</summary>
        public (int live, int queued) Depth => (_live.Count, _want.Count);

        public static ForestStreamer Attach(WorldTerrain t, Transform parent)
        {
            var go = new GameObject("Forest");
            go.transform.SetParent(parent, false);
            ForestStreamer s = go.AddComponent<ForestStreamer>();
            s._t = t;
            s._root = go.transform;
            return s;
        }

        private void LateUpdate()
        {
            if (_t == null) return;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
            // Unity x is the sim's y, Unity z is the sim's x.
            Vector3 p = _cam.transform.position;
            double x = p.z, y = p.x;
            if (double.IsNaN(_lastX) || (x - _lastX) * (x - _lastX) + (y - _lastY) * (y - _lastY) > ForestBuilder.ChunkM * ForestBuilder.ChunkM * 0.25)
            {
                _lastX = x; _lastY = y;
                Replan(x, y);
            }
            if (_want.Count == 0) return;
            bool starved = Time.unscaledTime - _lastBuiltAt > StarveAfterS;
            // In a headset "a frame and a half" is 20.8 ms at 72 Hz, not the phone's 24: a frame that missed its
            // vsync must not be followed by a chunk build as well.
            float hz = XrHeadset.Active ? XrHeadset.CurrentRefreshHz : 0f;
            double guardMs = hz > 1f ? System.Math.Min(StallGuardMs, 1500.0 / hz) : StallGuardMs;
            if (!starved && Time.unscaledDeltaTime * 1000.0 > guardMs) return;

            double began = Time.realtimeSinceStartupAsDouble * 1000.0;
            for (int k = 0; k < MaxPerFrame && _want.Count > 0; k++)
            {
                var w = _want[_want.Count - 1];
                _want.RemoveAt(_want.Count - 1);
                if (_live.ContainsKey((w.i, w.j))) continue;
                GameObject go = ForestBuilder.BuildChunk(_t, _root, w.i, w.j, w.d < DetailRadiusM);
                _live[(w.i, w.j)] = go;        // null is a real answer: that square has no trees
                _lastBuiltAt = Time.unscaledTime;
                double spent = Time.realtimeSinceStartupAsDouble * 1000.0 - began;
                if (spent > _worstMs) _worstMs = spent;
                if (spent > BudgetMs || starved) break;      // starved: one chunk, then wait for a good frame
            }
        }

        private void Replan(double x, double y)
        {
            // Drop what has fallen behind.
            var gone = new List<(int, int)>();
            foreach (var kv in _live)
            {
                double cx = (kv.Key.Item1 + 0.5) * ForestBuilder.ChunkM - x, cy = (kv.Key.Item2 + 0.5) * ForestBuilder.ChunkM - y;
                if (cx * cx + cy * cy > (RadiusM + ForestBuilder.ChunkM * 2) * (RadiusM + ForestBuilder.ChunkM * 2)) gone.Add(kv.Key);
            }
            // Destroy the MESH as well as the object. Destroying a GameObject does not free the mesh it was
            // holding, and this builds and throws away a chunk every few seconds for as long as the flight
            // lasts — on a long cross-country that is hundreds of leaked meshes.
            foreach (var k in gone)
            {
                GameObject go = _live[k];
                if (go != null)
                {
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                    Destroy(go);
                }
                _live.Remove(k);
            }

            // Queue what is missing, nearest last so it comes off the end of the list first.
            _want.Clear();
            int i0 = ForestBuilder.ChunkOf(x - RadiusM), i1 = ForestBuilder.ChunkOf(x + RadiusM);
            int j0 = ForestBuilder.ChunkOf(y - RadiusM), j1 = ForestBuilder.ChunkOf(y + RadiusM);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (_live.ContainsKey((i, j))) continue;
                    double cx = (i + 0.5) * ForestBuilder.ChunkM - x, cy = (j + 0.5) * ForestBuilder.ChunkM - y;
                    double d = System.Math.Sqrt(cx * cx + cy * cy);
                    if (d > RadiusM + ForestBuilder.ChunkM) continue;
                    _want.Add((i, j, d));
                }
            _want.Sort((a, b) => b.d.CompareTo(a.d));
        }
    }
}

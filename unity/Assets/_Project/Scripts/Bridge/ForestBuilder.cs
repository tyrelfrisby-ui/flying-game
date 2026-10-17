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
        /// that in one frame. This wants a frame-time check on a device before it is trusted.
        /// </summary>
        public const double ChunkM = Forest.CellM * 12;

        private static readonly Color Needle = new(0.14f, 0.24f, 0.17f);
        private static readonly Color NeedleLit = new(0.22f, 0.34f, 0.22f);
        private static readonly Color NeedleDry = new(0.26f, 0.30f, 0.17f);
        private static readonly Color Trunk = new(0.28f, 0.22f, 0.17f);

        private static Vector3 U(double x, double y, double up) => new((float)y, (float)up, (float)x);

        public static int ChunkOf(double v) => (int)System.Math.Floor(v / ChunkM);

        /// <summary>
        /// One chunk of forest, or null if that square has no trees in it. <paramref name="detail"/> true draws
        /// the trunk and three skirts (close enough to see one properly); false draws the silhouette only.
        /// </summary>
        public static GameObject BuildChunk(WorldTerrain t, Transform parent, int ci, int cj, bool detail)
        {
            double x0 = ci * ChunkM, y0 = cj * ChunkM;
            var b = new SeasideBuilder.Batch();
            int n = 0;
            foreach (Forest.Tree tr in Forest.InRect(x0, y0, x0 + ChunkM, y0 + ChunkM))
            {
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
                Spire(b, tr, g + h * 0.22, h * 0.42f, r, needle, lit, twist, lean);
                Spire(b, tr, g + h * 0.52, h * 0.34f, r * 0.72f, needle, lit, twist + 23, lean);
                Spire(b, tr, g + h * 0.78, h * 0.24f, r * 0.42f, needle, lit, twist + 47, lean);
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
            Vector3 p00 = P(-1, -1), p10 = P(1, -1), p11 = P(1, 1), p01 = P(-1, 1);
            b.Tri(p00, p10, apex, needle);
            b.Tri(p10, p11, apex, lit);
            b.Tri(p11, p01, apex, needle);
            b.Tri(p01, p00, apex, lit);
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
        /// <summary>Chunks built per frame.</summary>
        public int PerFrame = 1;

        private WorldTerrain _t;
        private Transform _root;
        private Camera _cam;
        private readonly Dictionary<(int, int), GameObject> _live = new();
        private readonly List<(int i, int j, double d)> _want = new();
        private double _lastX = double.NaN, _lastY;

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
            for (int k = 0; k < PerFrame && _want.Count > 0; k++)
            {
                var w = _want[_want.Count - 1];
                _want.RemoveAt(_want.Count - 1);
                if (_live.ContainsKey((w.i, w.j))) continue;
                GameObject go = ForestBuilder.BuildChunk(_t, _root, w.i, w.j, w.d < DetailRadiusM);
                _live[(w.i, w.j)] = go;        // null is a real answer: that square has no trees
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

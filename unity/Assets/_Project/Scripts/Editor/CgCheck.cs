using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// VERTICAL CG CHECK (owner 2026-10-10: "the Extra seems to roll about a point below the aircraft — check vertical CG on
    /// all of the airplanes"). Builds every type as the game draws it (its real model when it has one) with the root on the CG,
    /// and casts vertical lines through the drawn skin (mesh triangles, read with AcquireReadOnlyMeshData so imported meshes
    /// work too):
    ///   belly / top at the CG station (centreline, ±0.25 m) → where the CG sits in the fuselage (0 = belly, 1 = top incl. canopy);
    ///   the fuselage's mid-line 1.2 m ahead of and 2 m behind the CG → the drawing's PITCH against the sim's body axis;
    ///   the outer wing (55–80 % semispan) → the drawn wing height against the config's (the sim's) wing.
    /// Config: wing, thrust line and main-wheel contact relative to the CG (+ = above). Writes build/cg-check.csv.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CgCheck.Run   (CG_IDS=a,b to limit)
    /// </summary>
    public static class CgCheck
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string cfgDir = Path.Combine(Application.streamingAssetsPath, "aircraft");
            var ids = Directory.GetFiles(cfgDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(s => s).ToArray();
            string only = System.Environment.GetEnvironmentVariable("CG_IDS");
            if (only != null) ids = only.Split(',');
            var lines = new List<string> { "id,model,belly,top,cgFrac,pitchDeg,wingDrawn,wingSim,wingErr,wingRootDrawn,rootErr,thrust,hubDrawn,ground" };
            foreach (string id in ids)
            {
                AircraftConfig cfg;
                try { cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id); } catch { continue; }
                var root = new GameObject(id);
                float halfSpan = new AirframeBuilder().Build(root.transform, cfg);
                bool model = root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Model"));
                var tris = Triangles(root, model);
                if (System.Environment.GetEnvironmentVariable("CG_PROFILE") != null && tris.Count > 0)
                {
                    float minY = tris.Min(t => Mathf.Min(t.Item1.y, Mathf.Min(t.Item2.y, t.Item3.y)));
                    var low = tris.Where(t => Mathf.Min(t.Item1.y, Mathf.Min(t.Item2.y, t.Item3.y)) < minY + 0.05f).Select(t => t.Item1).ToList();
                    Debug.Log($"[CgCheck] LOWEST {id}: skin min y {minY:F2} at x {low.Average(v => v.x):F2} z {low.Average(v => v.z):F2} ({low.Count} tris); sim mains at {-(cfg.Gear.Count > 0 ? cfg.Gear.Max(g => g.Pos[2]) - cfg.Mass.CgVec().Z : 0):F2}");
                    foreach (var (zlo, zhi, nm) in new[] { (-0.6f, 1.0f, "mains"), (-6f, -2.5f, "tail") })
                    {
                        var sel = tris.Where(t => t.Item1.z > zlo && t.Item1.z < zhi).ToList();
                        if (sel.Count == 0) continue;
                        float my = sel.Min(t => t.Item1.y); var at = sel.Where(t => t.Item1.y < my + 0.03f).Select(t => t.Item1).ToList();
                        Debug.Log($"[CgCheck] WHEEL {id} {nm}: bottom y {my:F2} at z {at.Average(v => v.z):F2} |x| {at.Average(v => Mathf.Abs(v.x)):F2}");
                    }
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name.StartsWith("Model") || r.enabled) { var b = r.bounds; Debug.Log($"[CgCheck] PART {id} {r.gameObject.name} en {r.enabled} y {root.transform.InverseTransformPoint(b.min).y:F2}..{root.transform.InverseTransformPoint(b.max).y:F2}"); }
                }
                (float lo, float hi) Cast(float x, float z)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var (A, B, C) in tris)
                    {
                        if (x < Mathf.Min(A.x, Mathf.Min(B.x, C.x)) || x > Mathf.Max(A.x, Mathf.Max(B.x, C.x)) || z < Mathf.Min(A.z, Mathf.Min(B.z, C.z)) || z > Mathf.Max(A.z, Mathf.Max(B.z, C.z))) continue;
                        float d = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                        if (Mathf.Abs(d) < 1e-9f) continue;
                        float l1 = ((B.z - C.z) * (x - C.x) + (C.x - B.x) * (z - C.z)) / d;
                        float l2 = ((C.z - A.z) * (x - C.x) + (A.x - C.x) * (z - C.z)) / d;
                        float l3 = 1f - l1 - l2;
                        if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                        float y = l1 * A.y + l2 * B.y + l3 * C.y;
                        lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                    }
                    return (lo, hi);
                }
                (float lo, float hi) Many(IEnumerable<Vector2> pts)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var p in pts) { var c = Cast(p.x, p.y); lo = Mathf.Min(lo, c.lo); hi = Mathf.Max(hi, c.hi); }
                    return (lo, hi);
                }
                var cg = Many(new[] { new Vector2(0, 0), new Vector2(0, 0.25f), new Vector2(0, -0.25f), new Vector2(0.08f, 0), new Vector2(-0.08f, 0) });
                var fwd = Many(new[] { new Vector2(0, 1.2f), new Vector2(0.08f, 1.2f), new Vector2(-0.08f, 1.2f) });
                var aft = Many(new[] { new Vector2(0, -2f), new Vector2(0.08f, -2f), new Vector2(-0.08f, -2f) });
                float pitch = fwd.hi > fwd.lo && aft.hi > aft.lo ? Mathf.Atan2((fwd.lo + fwd.hi) * 0.5f - (aft.lo + aft.hi) * 0.5f, 3.2f) * Mathf.Rad2Deg : float.NaN;
                // The drawn outer wing: the mean height of every skin crossing at 55–80 % semispan, both sides, over the chord.
                var ys = new List<float>();
                foreach (float side in new[] { -1f, 1f })
                    for (float f = 0.55f; f <= 0.8f; f += 0.05f)
                        for (float z = -3f; z <= 3f; z += 0.1f) { var c = Cast(side * f * halfSpan, z); if (c.lo < 1e30f) { ys.Add(c.lo); ys.Add(c.hi); } }
                float wingDrawn = ys.Count > 0 ? ys.Average() : float.NaN;
                // Near the root (22–32 % semispan, clear of the fuselage): little dihedral rise — the sim's strips are flat.
                var yr = new List<float>();
                foreach (float side in new[] { -1f, 1f })
                    for (float f = 0.22f; f <= 0.32f; f += 0.02f)
                        for (float z = -3f; z <= 3f; z += 0.1f) { var c = Cast(side * f * halfSpan, z); if (c.lo < 1e30f && c.hi - c.lo < 0.6f) { yr.Add(c.lo); yr.Add(c.hi); } }
                float wingRoot = yr.Count > 0 ? yr.Average() : float.NaN;
                double cgz = cfg.Mass.CgVec().Z;
                var wingStrips = cfg.Surfaces.Where(s => s.Id.ToLowerInvariant().Contains("wing")).SelectMany(s => s.Strips)
                    .Where(st => System.Math.Abs(st.Pos[1]) >= 0.5 * halfSpan && System.Math.Abs(st.Pos[1]) <= 0.85 * halfSpan).ToList();
                double wingSim = wingStrips.Count > 0 ? -(wingStrips.Average(st => st.Pos[2]) - cgz) : double.NaN;
                double thrust = cfg.Propulsion != null ? -(cfg.Propulsion.ThrustLineZ - cgz) : double.NaN;
                double ground = cfg.Gear.Count > 0 ? -(cfg.Gear.Max(g => g.Pos[2]) - cgz) : double.NaN;
                // The drawn propeller hub(s): the centre of the prop / spinner meshes (a model) or the procedural hubs.
                // (the rig moves each PropHub onto the model's own propeller)
                var hubs = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "PropHub").Select(t => root.transform.InverseTransformPoint(t.position).y).ToList();
                if (System.Environment.GetEnvironmentVariable("CG_PROFILE") != null)
                    for (float z = 2.5f; z >= -6f; z -= 0.5f) { var c = Many(new[] { new Vector2(0, z), new Vector2(0.1f, z), new Vector2(-0.1f, z) }); Debug.Log($"[CgCheck] PROFILE {id} x {z:+0.0;-0.0} belly {c.lo:F2} top {c.hi:F2}"); }
                float hub = hubs.Count > 0 ? hubs.Average() : float.NaN;
                float frac = cg.hi > cg.lo ? (0f - cg.lo) / (cg.hi - cg.lo) : float.NaN;
                string line = $"{id},{model},{cg.lo:F2},{cg.hi:F2},{frac:F2},{pitch:F1},{wingDrawn:F2},{wingSim:F2},{wingDrawn - wingSim:F2},{wingRoot:F2},{wingRoot - wingSim:F2},{thrust:F2},{hub:F2},{ground:F2}";
                lines.Add(line); Debug.Log("[CgCheck] " + line);
                Object.DestroyImmediate(root);
            }
            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/cg-check.csv"));
            File.WriteAllLines(outPath, lines);
            Debug.Log("[CgCheck] DONE " + outPath);
        }

        /// <summary>Every drawn triangle in the root's frame (the CG): a model's own meshes (the rig hides and re-parents some, so
        /// all of them), otherwise the visible procedural parts. </summary>
        private static List<(Vector3, Vector3, Vector3)> Triangles(GameObject root, bool model)
        {
            var res = new List<(Vector3, Vector3, Vector3)>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>(); string nm = mf.gameObject.name;
                if (r == null || mf.sharedMesh == null || (model ? !nm.StartsWith("Model") : !r.enabled)) continue;
                string low = nm.ToLowerInvariant();
                // (no name filter: a model's material groups mix the belly with the wheels — the centreline casts miss the main
                // wheels and the props anyway; the propeller is measured from its PropHub)
                Matrix4x4 to = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                using var arr = Mesh.AcquireReadOnlyMeshData(mf.sharedMesh);
                var md = arr[0];
                var verts = new Unity.Collections.NativeArray<Vector3>(md.vertexCount, Unity.Collections.Allocator.Temp);
                md.GetVertices(verts);
                var P = new Vector3[verts.Length]; for (int i = 0; i < P.Length; i++) P[i] = to.MultiplyPoint3x4(verts[i]);
                verts.Dispose();
                for (int sm = 0; sm < md.subMeshCount; sm++)
                {
                    var desc = md.GetSubMesh(sm); if (desc.topology != MeshTopology.Triangles) continue;
                    var idx = new Unity.Collections.NativeArray<int>(desc.indexCount, Unity.Collections.Allocator.Temp);
                    md.GetIndices(idx, sm);
                    for (int t = 0; t + 2 < idx.Length; t += 3) res.Add((P[idx[t]], P[idx[t + 1]], P[idx[t + 2]]));
                    idx.Dispose();
                }
            }
            return res;
        }
    }
}

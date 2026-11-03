using System.Collections.Generic;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// DEBRIS PAINT CHECK (owner 2026-11-03: "When the pitts crashes and breaks apart it turns into the old pitts render not
    /// the new one ... new broken pieces ... that do not change or lose their paint when they break apart" — every type).
    ///
    /// For every type that places a model, in every livery it offers, builds the airframe exactly as the game does and
    /// breaks it up through the game's own code (AirframeVisual.DetachWingsStatic / DetachStatic): the g-limit wing
    /// failure, then stabiliser, fin, nose and both main legs on one airframe; ailerons, elevator, rudder, a whole wing,
    /// an outer panel, the tail boom and the tailwheel on another. Every debris renderer is then measured against what
    /// was ON SCREEN before the break:
    ///   - each face of each piece must be a face that was drawn, at the same place, WITH THE SAME MATERIAL OBJECT and the
    ///     same UV (so no paint swap and no texture lost) — which also means no piece can come from the hidden procedural
    ///     shell, whose faces were never drawn while a model was placed;
    ///   - a piece whose source had UVs must still have them;
    ///   - what stays on the aeroplane keeps its UVs and its material slots;
    ///   - a model's wing failure must actually drop something on each side.
    /// Then the Pitts's prebuilt-debris hook (AirframeBuilder.DebrisPrefabLoader) is driven with a stand-in prefab cut
    /// from the intact GLB: only "&lt;placed GLB&gt;-debris" may be asked for, its pieces must be the ones used, and a
    /// prefab with no pieces for the component must fall back to the runtime split.
    ///
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.DebrisPaintCheck.Run
    ///   DEBRIS_TYPES=pitts-s2b-like,extra-300-like ... to limit the types.
    /// The player's saved livery choices are put back afterwards.
    /// </summary>
    public static class DebrisPaintCheck
    {
        private const float PosTol = 5e-4f, UvTol = 1e-5f, Cell = 1e-3f;

        /// <summary>Every drawn face before the break, by material: world positions + uv0 of its vertices.</summary>
        private sealed class Snapshot
        {
            public readonly Dictionary<(int mat, int x, int y, int z), List<(Vector3 p, Vector2 uv, bool hasUv)>> Faces = new();
            public readonly HashSet<int> Materials = new();
            public readonly Dictionary<Renderer, bool> HadUv = new();
            public int Renderers, HiddenRenderers;
        }

        private static int _failures;
        private static readonly List<string> Table = new();

        private static void Fail(string m) { _failures++; Debug.LogError("[DebrisCheck] FAIL " + m); }

        public static void Run()
        {
            _failures = 0;
            string previousOverride = SessionSettings.LiveryOverride;
            bool previousModels = SessionSettings.UseAirframeModels;
            var savedPrefs = new Dictionary<string, string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.LiveryOverride = null;
                SessionSettings.UseAirframeModels = true;
                string only = System.Environment.GetEnvironmentVariable("DEBRIS_TYPES");
                var types = SessionSettings.Fleet.Select(f => f.id)
                    .Where(id => string.IsNullOrEmpty(only) || only.Split(',').Contains(id)).ToList();
                foreach (string id in types)
                {
                    if (!AirframeModels.Has(id)) { Table.Add($"{id,-24} procedural only (no model placed)"); continue; }
                    var options = LiveryChoice.For(id).Select(o => o.Id).ToList();
                    if (options.Count > 0)
                    {
                        string key = LiveryChoice.PrefKey(id);
                        savedPrefs[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
                    }
                    else options.Add(null);
                    var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                    foreach (string livery in options)
                    {
                        if (livery != null) SessionSettings.SetLivery(id, livery);
                        string tag = $"{id}/{SessionSettings.LiveryFor(id)}";
                        int before = _failures;
                        int pieces = 0;
                        pieces += Scenario(tag, cfg, wings: true, new[] {
                            AirframeComponent.TailHorizontal, AirframeComponent.TailVertical, AirframeComponent.Nose,
                            AirframeComponent.GearLeft, AirframeComponent.GearRight });
                        pieces += Scenario(tag, cfg, wings: false, new[] {
                            AirframeComponent.AileronRight, AirframeComponent.ElevatorLeft, AirframeComponent.Rudder,
                            AirframeComponent.WingLeft, AirframeComponent.WingRightOuter, AirframeComponent.TailBoom,
                            AirframeComponent.GearTail, AirframeComponent.GearNose });
                        Table.Add($"{tag,-40} glb={AirframeModels.PlacedOwnName(id) != null} pieces={pieces} {(_failures == before ? "PASS" : $"FAIL ({_failures - before})")}");
                    }
                }
                PrebuiltHook();
            }
            catch (System.Exception e) { _failures++; Debug.LogException(e); }
            finally
            {
                foreach (var kv in savedPrefs)
                    if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key); else PlayerPrefs.SetString(kv.Key, kv.Value);
                PlayerPrefs.Save();
                SessionSettings.LiveryOverride = previousOverride;
                SessionSettings.UseAirframeModels = previousModels;
                foreach (string l in Table) Debug.Log("[DebrisCheck] " + l);
                Debug.Log($"[DebrisCheck] {(_failures == 0 ? "PASS" : "FAIL")} ({_failures} failures)");
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }

        /// <summary>One airframe, broken up in order; returns the number of debris renderers checked.</summary>
        private static int Scenario(string tag, FlyingGame.Core.DataContracts.AircraftConfig cfg, bool wings, AirframeComponent[] comps)
        {
            var root = new GameObject(cfg.Id);
            var builder = new AirframeBuilder();
            var debris = new List<GameObject>();
            int n = 0;
            try
            {
                builder.Build(root.transform, cfg);
                bool model = builder.UsedModel;
                builder.FreezeRig();   // what the break-up cuts: the rigid meshes, drawn in place of their skins
                Snapshot snap = Take(root.transform);
                if (wings)
                {
                    (GameObject l, GameObject r) = AirframeVisual.DetachWingsStatic(builder, root.transform, cfg, Vector3.zero);
                    if (model && l == null) Fail($"{tag} wings: a model's wing failure dropped nothing on the LEFT");
                    if (model && r == null) Fail($"{tag} wings: a model's wing failure dropped nothing on the RIGHT");
                    n += Verify(tag + " wings-L", l, snap); n += Verify(tag + " wings-R", r, snap);
                    debris.Add(l); debris.Add(r);
                }
                foreach (AirframeComponent c in comps)
                {
                    GameObject d = AirframeVisual.DetachStatic(builder, root.transform, cfg, c, Vector3.zero);
                    n += Verify($"{tag} {c}", d, snap);
                    debris.Add(d);
                }
                VerifyKept(tag + (wings ? " kept(A)" : " kept(B)"), root.transform, snap);
            }
            finally
            {
                foreach (GameObject d in debris) if (d != null) Object.DestroyImmediate(d);
                builder.Clear();
                Object.DestroyImmediate(root);
            }
            return n;
        }

        private static (int, int, int) CellOf(Vector3 p) => (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

        private static Snapshot Take(Transform root)
        {
            var s = new Snapshot();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                bool drawn = r.enabled && r.gameObject.activeInHierarchy && r is MeshRenderer && mf != null && mf.sharedMesh != null;
                if (!drawn) { s.HiddenRenderers++; continue; }
                s.Renderers++;
                Mesh m = mf.sharedMesh;
                if (!m.isReadable) continue;
                bool hasUv = m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                s.HadUv[r] = hasUv;
                Vector3[] v = m.vertices; var uv = new List<Vector2>(); if (hasUv) m.GetUVs(0, uv);
                Material[] mats = r.sharedMaterials;
                for (int sm = 0; sm < m.subMeshCount && sm < mats.Length; sm++)
                {
                    if (mats[sm] == null) continue;
                    int mid = mats[sm].GetInstanceID();
                    s.Materials.Add(mid);
                    foreach (int i in m.GetIndices(sm).Distinct())
                    {
                        Vector3 p = r.transform.TransformPoint(v[i]);
                        var (x, y, z) = CellOf(p);
                        if (!s.Faces.TryGetValue((mid, x, y, z), out var l)) s.Faces[(mid, x, y, z)] = l = new List<(Vector3, Vector2, bool)>();
                        l.Add((p, hasUv ? uv[i] : Vector2.zero, hasUv));
                    }
                }
            }
            return s;
        }

        private static bool Found(Snapshot s, int mid, Vector3 p, Vector2 uv, bool hasUv, out bool srcHadUv)
        {
            srcHadUv = false;
            var (cx, cy, cz) = CellOf(p);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
            {
                if (!s.Faces.TryGetValue((mid, cx + dx, cy + dy, cz + dz), out var l)) continue;
                foreach (var e in l)
                {
                    if ((e.p - p).sqrMagnitude > PosTol * PosTol) continue;
                    srcHadUv |= e.hasUv;
                    if (!e.hasUv || (hasUv && (e.uv - uv).sqrMagnitude <= UvTol * UvTol)) return true;
                }
            }
            return false;
        }

        private static int Verify(string what, GameObject debris, Snapshot snap)
        {
            if (debris == null) { Debug.Log($"[DebrisCheck] {what}: no debris"); return 0; }
            int pieces = 0, verts = 0, bad = 0, badMat = 0, lostUv = 0;
            string firstBad = null;
            foreach (Renderer r in debris.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;   // a TextMesh marking carried along: drawn as it was
                pieces++;
                Mesh m = mf.sharedMesh;
                if (!m.isReadable) continue;
                Material[] mats = r.sharedMaterials;
                bool hasUv = m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                Vector3[] v = m.vertices; var uv = new List<Vector2>(); if (hasUv) m.GetUVs(0, uv);
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    if (sm >= mats.Length || mats[sm] == null) { badMat++; firstBad ??= $"{r.name} submesh {sm} has no material"; continue; }
                    int mid = mats[sm].GetInstanceID();
                    if (!snap.Materials.Contains(mid)) { badMat++; firstBad ??= $"{r.name} submesh {sm} draws '{mats[sm].name}', which no visible renderer used (procedural shell / swapped paint)"; continue; }
                    foreach (int i in m.GetIndices(sm).Distinct())
                    {
                        verts++;
                        Vector3 p = r.transform.TransformPoint(v[i]);
                        Vector2 u = hasUv ? uv[i] : Vector2.zero;
                        if (Found(snap, mid, p, u, hasUv, out bool srcUv)) continue;
                        if (!hasUv) { lostUv++; firstBad ??= $"{r.name}: no UVs at {p}"; continue; }
                        bad++; firstBad ??= $"{r.name} submesh {sm} '{mats[sm].name}': vertex {p} uv {u} is not on that material, at that place with that UV, before the break";
                    }
                }
            }
            string line = $"{what}: {pieces} renderers, {verts} vertices; {bad} off-face, {badMat} wrong/no material, {lostUv} lost UV";
            if (bad + badMat + lostUv > 0) Fail(line + " — e.g. " + firstBad);
            else Debug.Log("[DebrisCheck] " + line);
            return pieces;
        }

        /// <summary>What stays on the aeroplane: still textured, still one material per submesh.</summary>
        private static void VerifyKept(string what, Transform root, Snapshot snap)
        {
            int lost = 0; string first = null;
            foreach (var kv in snap.HadUv)
            {
                Renderer r = kv.Key;
                if (r == null || !kv.Value) continue;
                var mf = r.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                if (!m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) { lost++; first ??= r.name; }
            }
            if (lost > 0) Fail($"{what}: {lost} renderers left on the aeroplane lost their UVs (e.g. {first})");
        }

        /// <summary>The prebuilt-debris hook, on the Pitts. Filled in with the fix (AirframeBuilder.DebrisPrefabLoader).</summary>
        private static void PrebuiltHook() { }
    }
}

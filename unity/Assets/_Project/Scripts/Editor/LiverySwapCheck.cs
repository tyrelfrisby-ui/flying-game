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
    /// LIVERY PICKER SWAP CHECK (owner 2026-10-28). For every type with a livery choice (LiveryChoice), picks each livery the
    /// way the landing page does (SessionSettings.SetLivery, no override) and rebuilds the SAME airframe root the way
    /// AirframeVisual does when the choice changes — A, B, A, B — and checks that each pick loads its own GLB (its own paint), that a repeat
    /// of a livery leaves exactly the hierarchy it left the first time (nothing leaked under the root or in the scene), and
    /// logs the rig binding line for each build. The player's saved choices are put back afterwards.
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.LiverySwapCheck.Run
    /// </summary>
    public static class LiverySwapCheck
    {
        public static void Run()
        {
            int failures = 0;
            string previousOverride = SessionSettings.LiveryOverride;
            bool previousModels = SessionSettings.UseAirframeModels;
            var savedPrefs = new Dictionary<string, string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.LiveryOverride = null;
                SessionSettings.UseAirframeModels = true;
                foreach (var (id, _) in SessionSettings.Fleet)
                {
                    var options = LiveryChoice.For(id);
                    if (options.Count < 2) continue;
                    string key = LiveryChoice.PrefKey(id);
                    savedPrefs[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
                    var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                    var root = new GameObject(id);
                    var builder = new AirframeBuilder();
                    var first = new Dictionary<string, (int objects, long vertices, string paint)>();
                    int sceneRoots = -1;
                    foreach (string livery in options.Select(o => o.Id).Concat(options.Select(o => o.Id)))
                    {
                        SessionSettings.SetLivery(id, livery);
                        string resolved = SessionSettings.LiveryFor(id), suffix = AirframeModels.OwnSuffix(id);
                        builder.Build(root.transform, cfg);
                        int objects = root.GetComponentsInChildren<Transform>(true).Length;
                        long vertices = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => (long)f.sharedMesh.vertexCount);
                        // The paint the drawn model wears: both Extras share their geometry and differ only in the baked texture.
                        string paint = string.Join(",", root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                            .Where(m => m != null && m.mainTexture != null).Select(m => m.mainTexture.GetInstanceID()).Distinct().OrderBy(t => t));
                        int roots = EditorSceneManager.GetActiveScene().rootCount;
                        Debug.Log($"[LiverySwap] {id} picked={livery} resolved={resolved} glb={id}{suffix} objects={objects} vertices={vertices} textures=[{paint}] sceneRoots={roots}");
                        if (resolved != livery) { failures++; Debug.LogError($"[LiverySwap] {id}: picked {livery} but it resolves to {resolved}"); }
                        if (suffix != LiveryChoice.OwnSuffix(id, livery)) { failures++; Debug.LogError($"[LiverySwap] {id}: {livery} loads {suffix}"); }
                        if (first.TryGetValue(livery, out var was))
                        {
                            if (was != (objects, vertices, paint)) { failures++; Debug.LogError($"[LiverySwap] {id} {livery}: rebuilt to {objects} objects / {vertices} verts / paint {paint}, first time {was.objects} / {was.vertices} / {was.paint} (leak?)"); }
                        }
                        else first[livery] = (objects, vertices, paint);
                        if (sceneRoots >= 0 && roots != sceneRoots) { failures++; Debug.LogError($"[LiverySwap] {id}: scene roots {sceneRoots} -> {roots} (leak?)"); }
                        sceneRoots = roots;
                    }
                    if (first.Values.Select(v => v.paint).Distinct().Count() != first.Count) { failures++; Debug.LogError($"[LiverySwap] {id}: two liveries drew the same paint"); }
                    builder.Clear();
                    Object.DestroyImmediate(root);
                }
            }
            catch (System.Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                foreach (var kv in savedPrefs)
                    if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key); else PlayerPrefs.SetString(kv.Key, kv.Value);
                PlayerPrefs.Save();
                SessionSettings.LiveryOverride = previousOverride;
                SessionSettings.UseAirframeModels = previousModels;
                Debug.Log($"[LiverySwap] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The break-up cuts the aeroplane's own meshes at run time (DebrisMesh.Split reads vertices, UVs and submeshes). In the
    /// editor a mesh's data can always be read; in a PLAYER build only a mesh imported Read/Write keeps its CPU copy. Every
    /// mesh of every aircraft model under Resources/Models must therefore be readable, or the device cannot cut it.
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.ModelReadableCheck.Run
    /// Prints one line per model and "MODELREADABLE PASS/FAIL"; exits 1 on a failure.
    /// </summary>
    public static class ModelReadableCheck
    {
        public static void Run()
        {
            int bad = 0, models = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/_Project/Resources/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".glb") && !path.EndsWith(".gltf") && !path.EndsWith(".fbx")) continue;
                models++;
                var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToList();
                int unreadable = meshes.Count(m => !m.isReadable);
                if (unreadable > 0) bad++;
                Debug.Log($"[ModelReadable] {path}: {meshes.Count} meshes, {unreadable} not readable{(unreadable > 0 ? " — e.g. " + meshes.First(m => !m.isReadable).name : "")}");
            }
            Debug.Log($"MODELREADABLE {(bad == 0 ? "PASS" : "FAIL")} ({models} models, {bad} with unreadable meshes)");
            if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
        }
    }
}

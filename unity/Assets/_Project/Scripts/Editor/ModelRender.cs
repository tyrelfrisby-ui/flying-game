using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Renders each imported airframe model (Resources/Models/&lt;id&gt;/&lt;id&gt;) raw and as placed by
    /// AirframeModels (rotated, scaled to the config span) from four views into build/models/, with the bounds logged —
    /// how the forward axis and scale of each USDZ were confirmed.</summary>
    public static class ModelRender
    {
        [MenuItem("FlyingGame/Render Models")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/models"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50f, -30f, 0f); light.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f); cam.fieldOfView = 35f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
            var rt = new RenderTexture(1000, 700, 24); cam.targetTexture = rt;
            foreach (string id in AirframeModels.Ids) AssetDatabase.ImportAsset($"Assets/_Project/Resources/Models/{id}/{id}.obj", ImportAssetOptions.ForceUpdate);
            foreach (string id in AirframeModels.Ids)
            {
                var prefab = Resources.Load<GameObject>($"Models/{id}/{id}");
                if (prefab != null)
                {
                    int withTex = 0, total = 0;
                    foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) foreach (Material mm in r.sharedMaterials) { total++; if (mm != null && mm.mainTexture != null) withTex++; }
                    Debug.Log($"ModelRender {id}: {withTex}/{total} materials textured");
                }
                if (prefab == null) { Debug.LogWarning($"ModelRender: no model for {id}"); continue; }
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                var root = new GameObject(id);
                GameObject inst = AirframeModels.Place(root.transform, id, cfg, out Bounds b);
                Debug.Log($"ModelRender {id}: placed bounds centre {b.center} size {b.size}");
                float size = Mathf.Max(b.size.x, b.size.z, 6f);
                (string name, Vector3 dir)[] views = { ("iso", new Vector3(1.2f, 0.7f, -1.0f)), ("side", new Vector3(1f, 0.05f, 0f)), ("front", new Vector3(0f, 0.1f, 1f)), ("top", new Vector3(0.001f, 1f, 0f)) };
                foreach ((string name, Vector3 dir) in views)
                {
                    cam.transform.position = b.center + dir.normalized * size * 1.7f;
                    cam.transform.LookAt(b.center, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{id}-{name}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                Object.DestroyImmediate(root);
            }
            RenderTexture.active = null;
        }
    }
}

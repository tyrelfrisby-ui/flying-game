using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Headless check that the bubble field actually renders on Metal: draws the dense (instanced) and the
    /// sparse field into a render texture and writes PNGs + pixel counts to build/bubbles/.</summary>
    public static class BubbleRenderCheck
    {
        [MenuItem("FlyingGame/Bubble Render Check")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/bubbles"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.transform.position = new Vector3(0f, 300f, -20f);
            cam.transform.LookAt(new Vector3(0f, 300f, 20f));
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var follow = new GameObject("Follow"); follow.transform.position = new Vector3(0f, 300f, 0f);
            var bubbles = new GameObject("Bubbles").AddComponent<BubbleField>();
            bubbles.Follow = follow.transform;
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            Debug.Log($"instancing supported: {SystemInfo.supportsInstancing}, device {SystemInfo.graphicsDeviceType}");
            foreach (bool dense in new[] { true, false })
            {
                SessionSettings.BubbleInstancing = dense;
                bubbles.Draw(cam);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                Color32[] px = tex.GetPixels32(); int changed = 0;
                foreach (Color32 c in px) if (Mathf.Abs(c.r - 115) + Mathf.Abs(c.g - 168) + Mathf.Abs(c.b - 242) > 12) changed++;
                File.WriteAllBytes(Path.Combine(outDir, dense ? "dense.png" : "sparse.png"), tex.EncodeToPNG());
                Debug.Log($"BubbleRenderCheck {(dense ? "DENSE/instanced" : "SPARSE")}: drawn {bubbles.DrawnCount}, non-background pixels {changed}");
                Object.DestroyImmediate(tex);
            }
            RenderTexture.active = null;
        }
    }
}

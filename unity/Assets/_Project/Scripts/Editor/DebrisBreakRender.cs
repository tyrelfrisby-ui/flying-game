using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-03 21:18 (iPhone): "the broken pieces lose their paint and render gray and white". Breaks the Pitts up
    /// through the game's own runtime path (AirframeVisual.DetachStatic, FreezeRig first) — the parts his crash lost — and
    /// renders it before and after, with the pieces laid beside the aeroplane, and logs what each piece draws with.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.DebrisBreakRender.Render
    /// OWN_MODELS=0 renders the downloaded-model setting instead. Images to build/debris-break/.
    /// </summary>
    public static class DebrisBreakRender
    {
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/debris-break"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = System.Environment.GetEnvironmentVariable("OWN_MODELS") != "0";
            var cam = new GameObject("RenderCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 35f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f;
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 4f;
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.42f, 0.5f, 0.3f) };

            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
            var root = new GameObject("pitts");
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            root.transform.position = new Vector3(0f, 1.2f, 0f);
            Snap(cam, rt, root.transform.position, outDir, "1-intact");

            var comps = new[] { AirframeComponent.WingTipLowerRight, AirframeComponent.WingTipRight, AirframeComponent.GearRight,
                                AirframeComponent.AileronRight, AirframeComponent.WingRightOuter, AirframeComponent.WingLeftOuter };
            float lay = 0f;
            foreach (AirframeComponent c in comps)
            {
                GameObject d = AirframeVisual.DetachStatic(builder, root.transform, cfg, c, Vector3.zero);
                if (d == null) { Debug.Log($"[DebrisBreak] {c}: no piece"); continue; }
                Object.DestroyImmediate(d.GetComponent<WingDebris>());
                d.transform.position = root.transform.position + new Vector3(6f + lay, 0f, -2f); lay += 3.5f;
                foreach (Renderer r in d.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Mesh m = mf != null ? mf.sharedMesh : null;
                    string mats = string.Join(" | ", r.sharedMaterials.Select(x => x == null ? "NULL" : $"{x.name} [{x.shader.name}] tex={(x.mainTexture != null ? x.mainTexture.name : "none")} col={(x.HasProperty("_Color") ? x.color.ToString() : x.HasProperty("baseColorFactor") ? x.GetColor("baseColorFactor").ToString() : "-")}"));
                    Debug.Log($"[DebrisBreak] {c} piece '{r.name}' enabled={r.enabled} subs={(m != null ? m.subMeshCount : -1)} uv0={(m != null && m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))} mats={r.sharedMaterials.Length}: {mats}");
                }
            }
            // And what is drawn on the aeroplane now: anything enabled that was not before is a revived shell part.
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && !r.name.StartsWith("Model:") && !r.name.StartsWith("Skin:") && !r.name.StartsWith("Decal"))
                    Debug.Log($"[DebrisBreak] drawn on the airframe, not the model: '{r.name}' mats {string.Join(",", r.sharedMaterials.Select(x => x != null ? x.name : "NULL"))}");
            Snap(cam, rt, root.transform.position + new Vector3(5f, 0f, -1f), outDir, "2-broken");
            RenderTexture.active = null;
            Debug.Log($"[DebrisBreak] wrote images to {outDir}");
        }

        private static void Snap(Camera cam, RenderTexture rt, Vector3 target, string outDir, string name)
        {
            foreach ((string v, Vector3 dir) in new[] { ("iso", new Vector3(1.0f, 0.8f, 1.2f)), ("front", new Vector3(0f, 0.25f, 1f)), ("top", new Vector3(0.01f, 1f, 0.2f)) })
            {
                cam.transform.position = target + dir.normalized * 16f;
                cam.transform.LookAt(target, Vector3.up);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"pitts-{name}-{v}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
        }
    }
}

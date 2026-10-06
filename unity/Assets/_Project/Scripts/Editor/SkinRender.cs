using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Close-up check of the downloaded models' MOVING parts (owner 2026-10-04: "make the props look like they are actually
    /// spinning … and make the control surfaces move again with these new skins"): each modelled type built with its model,
    /// controls neutral and deflected (+0.4 rad aileron/elevator/rudder), prop slow (blades) and fast (blur) — to
    /// build/skins/*.png.   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.SkinRender.Render
    /// </summary>
    public static class SkinRender
    {
        private static readonly string[] Ids =
        {
            "c172-like", "pa28-archer-like", "cirrus-sr22-like", "pa18-cub-like", "extra-300-like", "p51d-like",
            "stearman-pt17-like", "seminole-like", "pa18-floats-like", "pa18-bush-like", "dc3-like", "geebee-r2-like", "glider-2-33-like", "boeing-737-like", "f86-sabre-like",
        };

        public static void Render()
        {
            string only = System.Environment.GetEnvironmentVariable("SKIN_IDS");
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/skins"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionSettings.UseAirframeModels = true;
            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.72f, 0.95f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1000f;
            var rt = new RenderTexture(1200, 800, 24); cam.targetTexture = rt;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var builder = new AirframeBuilder();
            foreach (string id in Ids)
            {
                if (!string.IsNullOrEmpty(only) && !only.Contains(id)) continue;
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                var root = new GameObject(id);
                float half = builder.Build(root.transform, cfg);
                float size = Mathf.Max(half * 2f, 8f);
                Bounds whole = AirframeModels.LocalBounds(root, root.transform);
                // Edit-mode renders don't re-skin between Render() calls on their own.
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                void Shot(string name, Vector3 dir, float dist, Vector3? at = null)
                {
                    Vector3 target = at ?? whole.center;
                    cam.transform.position = target + dir.normalized * size * dist;
                    cam.transform.LookAt(target, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{id}-{name}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                // Unity: +z forward (nose), +x right, +y up.
                Vector3 rearQ = new Vector3(0.9f, 0.8f, -1.2f), front = new Vector3(0.35f, 0.15f, 1f);
                builder.SetDeflections(0f, 0f, 0f, 0f); builder.SpinProps(_ => 0f, 0.02f);
                Shot("neutral", rearQ, 1.6f);
                Shot("side", new Vector3(1f, 0.05f, 0f), 1.1f);
                if (cfg.RetractableGear)
                {
                    builder.SetGearExtension(0f);
                    Shot("side-gearup", new Vector3(1f, -0.15f, 0f), 1.1f);
                    builder.SetGearExtension(1f);
                }
                Shot("top", new Vector3(0f, 1f, -0.01f), 1.4f);
                Vector3 tail = new Vector3(0f, whole.center.y + 0.4f, whole.min.z + 1.2f), wing = new Vector3(whole.max.x * 0.7f, whole.center.y + 0.6f, whole.center.z);
                Shot("tail-neutral", new Vector3(0.7f, 0.5f, -1f), 0.35f, tail);
                Shot("wing-neutral", new Vector3(0.3f, 0.6f, -1f), 0.3f, wing);
                builder.SetDeflections(0.4f, 0.4f, 0.4f, 0f);
                Shot("deflected", rearQ, 1.6f);
                Shot("tail-deflected", new Vector3(0.7f, 0.5f, -1f), 0.35f, tail);
                Shot("wing-deflected", new Vector3(0.3f, 0.6f, -1f), 0.3f, wing);
                builder.SpinProps(_ => 150f, 0.13f);
                Shot("prop-slow", front, 0.9f);
                builder.SpinProps(_ => 2400f, 0.02f);
                Shot("prop-fast", front, 0.9f);
                Shot("prop-fast-headon", new Vector3(0f, 0.02f, 1f), 0.9f);
                builder.Clear();
                Object.DestroyImmediate(root);
            }
            RenderTexture.active = null;
            Debug.Log($"SkinRender: done → {outDir}");
        }
    }
}

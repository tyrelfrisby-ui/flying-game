using System;
using System.IO;
using System.Collections.Generic;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>Batchmode capture of the current in-game aircraft. Requires graphics (no -nographics).</summary>
    public static class AircraftRender
    {
        private const int Width = 1600;
        private const int Height = 1000;

        public static void RenderFromArgs()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Read(string flag)
                {
                    int i = Array.IndexOf(args, flag);
                    if (i < 0 || i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                        throw new ArgumentException("Missing argument " + flag);
                    return args[i + 1];
                }
                Render(Read("-aircraft"), Read("-livery"), Read("-out"));
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Render(string id, string livery, string outDir)
        {
            int exitCode = 1;
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            GameObject root = null, cameraObject = null, sunObject = null;
            Camera camera = null;
            var builder = new AirframeBuilder();
            string previousLivery = SessionSettings.LiveryId;
            bool previousModels = SessionSettings.UseAirframeModels, previousOwn = SessionSettings.UseOwnModels;
            try
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(outDir))
                    throw new ArgumentException("Aircraft id and output directory are required.");
                if (livery != "type" && livery != "talk-flying" && livery != "factory" && livery != "sunrise")
                    throw new ArgumentException("Livery must be type, talk-flying, factory or sunrise.");
                if (livery == "factory" && id != "glider-2-33-like")
                    throw new ArgumentException("Factory livery is only available for glider-2-33-like.");
                if (livery == "sunrise" && id != "extra-300-like")
                    throw new ArgumentException("Sunrise livery is only available for extra-300-like.");
                if (livery == "sunrise" && !AirframeModels.HasOwn(id))
                    throw new InvalidOperationException("Sunrise requires the mid-wing model. Run Blender --background --python tools/model_extra300.py, then let Unity import the GLB.");
                string output = Path.GetFullPath(outDir);
                Directory.CreateDirectory(output);
                SessionSettings.LiveryId = livery;
                SessionSettings.UseAirframeModels = true;
                if (id == "glider-2-33-like") SessionSettings.UseOwnModels = true;
                // Temporary scene only: never save or modify a project scene or asset.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                sunObject = new GameObject("Aircraft Render Sun");
                var sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun.intensity = 1.2f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.70f);
                RenderSettings.fog = false;
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);

                root = new GameObject(id);
                // Use the game's complete build path, including model selection, placement,
                // materials, overlays and carved/skinned moving parts. Keep session defaults.
                builder.Build(root.transform, UnityAircraftConfigLoader.LoadFromStreamingAssets(id));
                builder.SetDeflections(0f, 0f, 0f, 0f);
                builder.SpinProps(_ => 0f, 0.02f);
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.forceMatrixRecalculationPerRender = true;

                bool any = false;
                Bounds bounds = default;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!any) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
                if (!any || bounds.size.sqrMagnitude <= 0f)
                    throw new InvalidOperationException("Aircraft has no visible aircraft geometry.");

                // Fit the close 3/4 shot to visible vertices, rather than empty corners of the aircraft AABB.
                var fitPoints = new List<Vector3>();
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        Mesh mesh = null;
                        bool baked = false;
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            mesh = new Mesh();
                            skin.BakeMesh(mesh);
                            baked = true;
                        }
                        else
                        {
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (filter != null) mesh = filter.sharedMesh;
                        }
                        try
                        {
                            if (mesh != null && mesh.isReadable)
                                foreach (Vector3 vertex in mesh.vertices)
                                    fitPoints.Add(renderer.transform.TransformPoint(vertex) - bounds.center);
                            else
                            {
                                // Text and non-readable imported meshes still participate in framing.
                                Bounds rb = renderer.bounds;
                                for (int c = 0; c < 8; c++)
                                    fitPoints.Add(rb.center - bounds.center + new Vector3(
                                        (c & 1) == 0 ? -rb.extents.x : rb.extents.x,
                                        (c & 2) == 0 ? -rb.extents.y : rb.extents.y,
                                        (c & 4) == 0 ? -rb.extents.z : rb.extents.z));
                            }
                        }
                        finally { if (baked) Object.DestroyImmediate(mesh); }
                    }
                    if (fitPoints.Count == 0) throw new InvalidOperationException("No readable Aircraft vertices for framing.");
                }

                cameraObject = new GameObject("Aircraft Render Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.55f, 0.74f, 0.94f);
                camera.fieldOfView = 35f;
                camera.aspect = (float)Width / Height;
                camera.allowMSAA = true;
                camera.allowHDR = false;
                camera.renderingPath = RenderingPath.Forward;
                rt = new RenderTexture(Width, Height, 24) { antiAliasing = 8 };
                if (!rt.Create()) throw new InvalidOperationException("Could not create the Aircraft render texture.");
                camera.targetTexture = rt;

                // Unity aircraft axes: nose +z, right +x, up +y.
                for (int background = 0; background < 2; background++)
                {
                    string suffix = background == 1 ? "-black" : "";
                    camera.backgroundColor = background == 1 ? Color.black : new Color(0.55f, 0.74f, 0.94f);
                    Capture(camera, rt, bounds, new Vector3(1.2f, 0.65f, 1f), Vector3.up, false,
                        Path.Combine(output, id + "-3q-front" + suffix + ".png"), 0.70f, fitPoints);
                    Capture(camera, rt, bounds, Vector3.right, Vector3.up, true,
                        Path.Combine(output, id + "-side" + suffix + ".png"));
                    Capture(camera, rt, bounds, Vector3.up, Vector3.forward, true,
                        Path.Combine(output, id + "-top" + suffix + ".png"));
                    Capture(camera, rt, bounds, Vector3.down, Vector3.forward, true,
                        Path.Combine(output, id + "-bottom" + suffix + ".png"));
                }
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                SessionSettings.LiveryId = previousLivery;
                SessionSettings.UseAirframeModels = previousModels;
                SessionSettings.UseOwnModels = previousOwn;
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                builder.Clear();
                if (root != null) Object.DestroyImmediate(root);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (sunObject != null) Object.DestroyImmediate(sunObject);
                EditorApplication.Exit(exitCode);
            }
        }

        private static void Capture(Camera camera, RenderTexture rt, Bounds bounds,
            Vector3 direction, Vector3 up, bool orthographic, string path, float frameFill = 1f / 1.15f, List<Vector3> fitPoints = null)
        {
            camera.ResetProjectionMatrix();
            camera.orthographic = orthographic;
            camera.transform.rotation = Quaternion.LookRotation(-direction.normalized, up);
            Quaternion inverse = Quaternion.Inverse(camera.transform.rotation);
            float tanVertical = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHorizontal = tanVertical * camera.aspect;
            float distance = 1f, halfHeight = 0.1f;
            for (int i = 0; i < (fitPoints != null ? fitPoints.Count : 8); i++)
            {
                Vector3 corner = fitPoints != null ? fitPoints[i] : new Vector3(
                    (i & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (i & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (i & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 p = inverse * corner;
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(p.y), Mathf.Abs(p.x) / camera.aspect);
                distance = Mathf.Max(distance, Mathf.Abs(p.x) / frameFill / tanHorizontal - p.z,
                    Mathf.Abs(p.y) / frameFill / tanVertical - p.z);
            }
            float radius = bounds.extents.magnitude;
            camera.orthographicSize = halfHeight * 1.15f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = radius * 20f + 100f;
            camera.transform.position = bounds.center + direction.normalized * distance;
            if (!orthographic && fitPoints != null)
            {
                // Solve using actual perspective projections, including depth and asymmetric outlines.
                // Width is the target; height is a guard against cropping unusually tall aircraft.
                float low = 0.1f, high = radius * 20f + 10f;
                for (int iteration = 0; iteration < 48; iteration++)
                {
                    distance = (low + high) * 0.5f;
                    float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                    float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                    bool behind = false;
                    foreach (Vector3 point in fitPoints)
                    {
                        Vector3 p = inverse * point;
                        float depth = distance + p.z;
                        if (depth <= 0.1f) { behind = true; break; }
                        float x = p.x / (2f * depth * tanHorizontal);
                        float y = p.y / (2f * depth * tanVertical);
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                    if (behind || maxX - minX > frameFill || maxY - minY > 0.85f) low = distance;
                    else high = distance;
                }
                distance = high;
                camera.transform.position = bounds.center + direction.normalized * distance;
                // Shift the projection centre to the silhouette centre without changing view direction.
                float left = float.PositiveInfinity, right = float.NegativeInfinity;
                float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                foreach (Vector3 point in fitPoints)
                {
                    Vector3 p = inverse * point;
                    float depth = distance + p.z;
                    float x = p.x / (depth * tanHorizontal), y = p.y / (depth * tanVertical);
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                    bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                }
                Matrix4x4 projection = camera.projectionMatrix;
                projection.m02 = (left + right) * 0.5f;
                projection.m12 = (bottom + top) * 0.5f;
                camera.projectionMatrix = projection;
            }
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log($"AircraftRender: wrote {path}");
            }
            finally { Object.DestroyImmediate(image); }
        }
    }
}

using System;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The aircraft as the physics parks it (2026-10-28, gear re-measured from the models): LandingGear.RestingState's
    /// three-point attitude and CG height, the game's own airframe build on top, a runway plane at y = 0. Writes a side and
    /// a low 3/4 shot and logs how far the drawn tyres are from the ground — a model sitting on its wheels reads ~0 for the
    /// mains and the tailwheel (the physics settles 2 cm into the struts, so up to −2 cm).
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.GroundStanceRender.Run -aircraft pitts-s2b-like -livery n31dg -out /tmp/x
    /// </summary>
    public static class GroundStanceRender
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Read(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + flag); }
            int exit = 1;
            string id = Read("-aircraft"), livery = Read("-livery"), outDir = Path.GetFullPath(Read("-out"));
            string prevLivery = SessionSettings.LiveryOverride; bool prevModels = SessionSettings.UseAirframeModels;
            var builder = new AirframeBuilder(); RenderTexture rt = null; GameObject root = null;
            try
            {
                Directory.CreateDirectory(outDir);
                SessionSettings.LiveryOverride = livery; SessionSettings.UseAirframeModels = true;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f); sun.intensity = 1.2f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.70f);

                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                var rest = LandingGear.RestingState(cfg, 0, 0, 0);
                double pitch = Math.Asin(2 * (rest.Attitude.W * rest.Attitude.Y - rest.Attitude.Z * rest.Attitude.X)) * 180 / Math.PI;
                root = new GameObject(id);
                builder.Build(root.transform, cfg);   // the root is the CG, as in flight
                root.transform.SetPositionAndRotation(new Vector3(0f, (float)-rest.Position.Z, 0f), Quaternion.Euler((float)-pitch, 0f, 0f));

                // Drawn tyre bottoms: the lowest drawn point ahead of the CG (the main tyres) and behind 2 m aft (the tailwheel),
                // with the mesh each belongs to; everything between (belly, gear legs, wing tips) must clear the ground.
                float mainLow = float.MaxValue, tailLow = float.MaxValue, midLow = float.MaxValue; string mainPart = "", tailPart = "", midPart = "";
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable || !mf.gameObject.activeInHierarchy) continue;
                    foreach (Vector3 v in mesh.vertices)
                    {
                        Vector3 w = mf.transform.TransformPoint(v);
                        if (w.z > -0.6f) { if (w.y < mainLow) { mainLow = w.y; mainPart = mf.gameObject.name; } }
                        else if (w.z < -2.5f) { if (w.y < tailLow) { tailLow = w.y; tailPart = mf.gameObject.name; } }
                        else if (w.y < midLow) { midLow = w.y; midPart = mf.gameObject.name; }
                    }
                }
                Debug.Log($"[GroundStance] {id} {livery}: three-point {pitch:F2}°, CG {-rest.Position.Z:F3} m up; lowest drawn point ahead of the CG {mainLow * 100:F1} cm ({mainPart}), aft {tailLow * 100:F1} cm ({tailPart}), between {midLow * 100:F1} cm ({midPart})");
                bool sits = Mathf.Abs(mainLow) < 0.04f && Mathf.Abs(tailLow) < 0.04f && midLow > 0.0f;
                Debug.Log($"[GroundStance] {id} {livery}: {(sits ? "SITS ON ITS WHEELS" : "DOES NOT SIT ON ITS WHEELS")}");

                Bounds b = default; bool any = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; if (!any) b = r.bounds; else b.Encapsulate(r.bounds); any = true; }
                // A dark runway strip, top face at y = 0, thick enough to read as a line in the side view.
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.localScale = new Vector3(3f, 0.06f, 30f); ground.transform.position = new Vector3(0f, -0.03f, 0f);
                ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.25f, 0.26f, 0.27f) };
                b.Encapsulate(new Vector3(b.center.x, -0.05f, b.center.z));

                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.74f, 0.94f); cam.fieldOfView = 35f;
                cam.aspect = (float)AircraftRender.Width / AircraftRender.Height; cam.allowMSAA = true; cam.allowHDR = false;
                rt = new RenderTexture(AircraftRender.Width, AircraftRender.Height, 24) { antiAliasing = 8 }; rt.Create(); cam.targetTexture = rt;
                string name = $"{id}-{livery}";
                AircraftRender.Capture(cam, rt, b, Vector3.left, Vector3.up, true, Path.Combine(outDir, name + "-ground-side.png"));
                AircraftRender.Capture(cam, rt, b, new Vector3(-1.1f, 0.25f, 0.9f), Vector3.up, false, Path.Combine(outDir, name + "-ground-3q.png"));
                cam.targetTexture = null;
                exit = sits ? 0 : 2;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                SessionSettings.LiveryOverride = prevLivery; SessionSettings.UseAirframeModels = prevModels;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                builder.Clear(); if (root != null) Object.DestroyImmediate(root);
                EditorApplication.Exit(exit);
            }
        }
    }
}

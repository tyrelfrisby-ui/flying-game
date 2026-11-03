using System;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>Run with -batchmode -executeMethod FlyingGame.EditorTools.FuelSlickRenderCheck.Render
    /// -quit -force-metal (never -nographics). Uses the real game water shader on a flat surface.</summary>
    public static class FuelSlickRenderCheck
    {
        [MenuItem("FlyingGame/Fuel Slick Render Check")]
        public static void Render()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/fuel-slick"));
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var config = AircraftConfigLoader.LoadFromFile(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../configs/aircraft/c172-like.json")));
            var load = new AircraftLoad(config);
            foreach (var slot in load.Slots)
                if (slot.Kind == AircraftLoad.StationKind.Fuel) load.Set(slot.Id, slot.MaxKg);
            FuelSlick slick = FuelSlick.TryCreate(config, load, true, true, Vec3.Zero, Vec3.Zero);
            if (slick == null) throw new InvalidOperationException("C172 fixture has no fuel slick");
            Shader waterShader = Shader.Find("FlyingGame/Water");
            Shader slickShader = Shader.Find("FlyingGame/FuelSlick");
            if (waterShader == null || slickShader == null || !waterShader.isSupported || !slickShader.isSupported)
                throw new InvalidOperationException("Water/FuelSlick shader missing or unsupported; use Metal without -nographics");
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Real game water shader at sim surface zero"; water.transform.localScale = Vector3.one * 200;
            var waterMaterial = new Material(waterShader); waterMaterial.SetColor("_Color", new Color(.075f,.30f,.41f,.95f));
            water.GetComponent<Renderer>().sharedMaterial = waterMaterial;
            Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WakeOn", 0); Shader.SetGlobalFloat("_ReflOn", 0);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f,.8f,-.4f).normalized);
            var visual = new GameObject("Fuel slick fixture").AddComponent<FuelSlickVisual>();
            var camera = new GameObject("Oblique camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 350, -400); camera.transform.LookAt(Vector3.zero);
            camera.fieldOfView = 70; camera.nearClipPlane = .1f; camera.farClipPlane = 3000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f,.66f,.95f);
            camera.depthTextureMode = DepthTextureMode.Depth;
            var target = new RenderTexture(1600, 1200, 24);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture = target;
                foreach (int seconds in new[] { 0, 5, 60, 300 })
                {
                    visual.Show(slick, seconds); camera.Render(); RenderTexture.active = target;
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                        File.WriteAllBytes(Path.Combine(output, $"slick-{seconds:000}s.png"), image.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(image); }
                    Debug.Log($"FuelSlickRenderCheck t={seconds}s fuel={load.FuelKg:F3} kg radius={slick.RadiusAt(seconds):F3} m area={Math.PI * Math.Pow(slick.RadiusAt(seconds), 2):F3} m²");
                }
                // Extra egress witness: same slick, camera below the surface; shader must reject it.
                camera.transform.position = new Vector3(0,-10,-50); camera.transform.LookAt(Vector3.zero);
                camera.Render(); RenderTexture.active = target;
                var below = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try { below.ReadPixels(new Rect(0,0,target.width,target.height),0,0); below.Apply();
                    File.WriteAllBytes(Path.Combine(output,"slick-underwater.png"),below.EncodeToPNG()); }
                finally { Object.DestroyImmediate(below); }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target);
                Object.DestroyImmediate(visual.gameObject); Object.DestroyImmediate(water);
                Object.DestroyImmediate(waterMaterial); Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}

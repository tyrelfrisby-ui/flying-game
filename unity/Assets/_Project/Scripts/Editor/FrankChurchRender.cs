using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Review plates for the Frank Church strips (2026-11-03, lane/claude-frank-church-valleys). Cameras come from
    /// FrankChurch.Strips themselves, so the same file shoots any version of the island from the equivalent places:
    ///   overhead  straight down from 1.6 km above the strip, its landing heading up the picture;
    ///   approach  a pilot's short final: 900 m before the landing threshold on the landing heading (uphill; the
    ///             one-way heading where there is one), 4 degrees above it, looking at the far end;
    ///   wall      for a one-way strip, a 3/4 view up the canyon past it, showing what closes beyond the far end.
    /// Writes build/frank-church/&lt;tag&gt;/*.png. Editor only.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.FrankChurchRender.RenderStrips -fcTag after
    /// </summary>
    public static class FrankChurchRender
    {
        public static void RenderStrips()
        {
            var args = System.Environment.GetCommandLineArgs();
            int tagAt = System.Array.IndexOf(args, "-fcTag");
            string tag = tagAt >= 0 && tagAt + 1 < args.Length ? args[tagAt + 1] : "after";
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/frank-church", tag));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var terrain = new FlyingGame.Core.WorldTerrain();
            FlyingGame.Core.WorldTerrain.Active = terrain;
            FlyingGame.Core.WorldClock.TimeS = 20;
            float savedLodBias = QualitySettings.lodBias; QualitySettings.lodBias = .7f;   // the device's level of detail
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool training = false;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) training = true;
                if (training || (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "FlyingGame/Glass")) renderer.enabled = false;
            }
            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .5f; cam.farClipPlane = 80000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            // Sim x north, y east, metres; up metres MSL.
            Vector3 S(double x, double y, double up) => WorldBuilder.U(x, y, up);
            double H(double x, double y) => terrain.HeightAt(x, y);
            var log = new System.Text.StringBuilder();
            void Shoot(string name, Vector3 eye, Vector3 look, Vector3 up, float fov, Vector3 forestAt)
            {
                cam.fieldOfView = fov;
                cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, up);
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(terrain, forest.transform, forestAt.z, forestAt.x, 2600, 800);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(forest);
                log.AppendLine($"{name}\teye {eye}\tlook {look}\tfov {fov}");
            }
            foreach (var s in FlyingGame.Core.FrankChurch.Strips)
            {
                string slug = s.Name.ToLowerInvariant().Replace(' ', '-');
                double land = s.OneWayInHeadingDeg ?? s.HeadingDeg, h = land * System.Math.PI / 180;
                double ux = System.Math.Cos(h), uy = System.Math.Sin(h);
                double sign = System.Math.Abs(((land - s.HeadingDeg) % 360 + 360) % 360) < 1 ? 1 : -1;   // +1: landing toward the high end
                double tx = s.X - ux * s.LengthM / 2, ty = s.Y - uy * s.LengthM / 2;   // landing threshold
                double fx = s.X + ux * s.LengthM / 2, fy = s.Y + uy * s.LengthM / 2;   // far end
                double tz = s.ElevationM - sign * s.LengthM / 2 * s.SlopePercent / 100, fz = s.ElevationM + sign * s.LengthM / 2 * s.SlopePercent / 100;
                var centre = S(s.X, s.Y, s.ElevationM);
                // Overhead: straight down, the landing heading up the picture.
                Shoot($"{slug}-overhead", S(s.X, s.Y, s.ElevationM + 1600), centre, S(s.X + ux, s.Y + uy, 0) - S(s.X, s.Y, 0), 50, centre);
                // Short final: 900 m out, 4 degrees up from the threshold (never inside the ground).
                double ex = tx - ux * 900, ey = ty - uy * 900, ez = System.Math.Max(tz + 900 * System.Math.Tan(4 * System.Math.PI / 180), H(ex, ey) + 25);
                Shoot($"{slug}-approach", S(ex, ey, ez), S(fx, fy, fz), Vector3.up, 55, S(s.X, s.Y, s.ElevationM));
                if (s.OneWay)
                {
                    // 3/4 view: 1.2 km behind and 500 m to the side of the threshold, 300 m up, looking past the far end.
                    double sx = tx - ux * 700 - uy * 500, sy = ty - uy * 700 + ux * 500;
                    Shoot($"{slug}-wall", S(sx, sy, System.Math.Max(tz + 300, H(sx, sy) + 60)), S(fx + ux * 250, fy + uy * 250, fz + 60), Vector3.up, 55, S(s.X, s.Y, s.ElevationM));
                }
            }
            File.WriteAllText(Path.Combine(outDir, "cameras.txt"), log.ToString());
            RenderTexture.active = null;
            QualitySettings.lodBias = savedLodBias;
            Debug.Log($"FrankChurchRender: wrote plates to {outDir}");
        }
    }
}

using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Review plates for the windsocks (lane/claude-windsocks, 2026-11-05). Builds the island the game builds, sets a
    /// uniform wind, settles every sock in it (<see cref="Windsock.SettleForTesting"/>: Update does not run in edit
    /// mode) and shoots:
    ///   approach-*   short final to a runway end, on a 3° path, the end sock abeam the touchdown zone;
    ///   midfield-*   from the runway centreline at midfield, a pilot's eye height, looking at the midfield sock;
    ///   closeup-*    one sock from 14 m at calm, 3, 7 and 15 kt (wind across the picture, from the right).
    /// Writes renders/windsocks/*.png and cameras.txt. Editor only.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.WindsockShots.Render
    /// </summary>
    public static class WindsockShots
    {
        private const double Kt = 0.514444;

        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../renders/windsocks"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = new WorldTerrain(); WorldTerrain.Active = terrain;
            WorldClock.TimeS = 20;
            Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null; Atmosphere.Thermals.Clear();
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0); RenderSettings.sun = sun;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool training = false;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) training = true;
                if (training || (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "FlyingGame/Glass")) renderer.enabled = false;
            }
            var socks = Object.FindFirstObjectByType<Windsock>();
            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .3f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var log = new System.Text.StringBuilder();
            log.AppendLine($"socks: {(socks != null ? socks.Count : 0)} built, {IslandGeometry.Windsocks.Count} sites; unplaced: {string.Join(", ", WindsockPart.Unplaced(IslandGeometry.Windsocks))}");

            void Wind(double fromDeg, double kt)
            {
                double to = (fromDeg + 180) * System.Math.PI / 180;
                Atmosphere.SteadyWind = new Vec3(System.Math.Cos(to) * kt * Kt, System.Math.Sin(to) * kt * Kt, 0);
                socks?.SettleForTesting(20);
            }
            void Shoot(string name, Vector3 eye, Vector3 look, float fov, Vector3 forestAt)
            {
                cam.fieldOfView = fov;
                cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                socks?.Step(0, eye);   // draw ranges from this eye
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(terrain, forest.transform, forestAt.z, forestAt.x, 2600, 800);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(forest);
                log.AppendLine($"{name}\teye {eye}\tlook {look}\tfov {fov}\twind {Atmosphere.SteadyWind}");
            }
            Vector3 At(IslandPoint p, double up) => WorldBuilder.U(p.X, p.Y, up);
            WindsockPart.Site SockOf(IslandField f, WindsockPart.Station st) => IslandGeometry.Windsocks.First(s => s.FieldId == f.Id && s.Station == st);

            // A paved runway and a grass strip, both landing toward their far end with a 10 kt quartering headwind.
            foreach (string id in new[] { "PHDH 8/26", "AP1" })
            {
                var f = IslandTerrain.Fields.First(q => q.Id == id);
                string slug = id.Split(' ')[0].ToLowerInvariant();
                Wind(f.Heading + 30, 10);   // landing along +U: wind from 30° right of the landing heading
                var threshold = f.Centre - f.U * (f.Length / 2000); double tz = f.Plane(threshold);
                var sockA = SockOf(f, WindsockPart.Station.ApproachEnd);
                foreach (double back in new[] { 900.0, 350.0 })
                {
                    var eye = threshold - f.U * (back / 1000);
                    double ez = System.Math.Max(tz + back * System.Math.Tan(3 * System.Math.PI / 180) + 2, IslandTerrain.HeightKm(eye) + 8);
                    var aim = f.Centre - f.U * ((f.Length / 2 - 300) / 1000);
                    Shoot($"approach-{slug}-{back:F0}m", At(eye, ez), At(aim, f.Plane(aim)), back > 500 ? 30 : 45, At(threshold, tz));
                }
                // Midfield: on the centreline abeam the midfield sock, 2.5 m up, looking at it with the runway behind.
                var mid = SockOf(f, WindsockPart.Station.Midfield);
                var eyeM = f.Centre - f.U * (60.0 / 1000); double mz = f.SurfaceHeight(eyeM) + 2.5;
                Shoot($"midfield-{slug}", At(eyeM, mz), At(mid.Point, mid.GroundM + 4), 55, At(f.Centre, f.Elevation));
                // Down the runway from the approach end sock's side: all three in a line.
                var eyeL = threshold - f.U * (40.0 / 1000) + f.V * (sockA.AcrossM / 1000 * 2.2); double lz = f.Plane(threshold) + 6;
                Shoot($"row-{slug}", At(eyeL, lz), At(f.Centre + f.V * (sockA.AcrossM / 1000), f.Elevation + 4), 40, At(f.Centre, f.Elevation));
            }

            // Close-ups: the PHDH midfield sock from 14 m, wind from the camera's right.
            {
                var f = IslandTerrain.Fields.First(q => q.Id == "PHDH 8/26");
                var s = SockOf(f, WindsockPart.Station.Midfield);
                foreach (double kt in new[] { 0.0, 3.0, 7.0, 15.0 })
                {
                    Wind(f.Heading + 90, kt);   // from the runway's right (+V): the camera looks along +U, so the sock streams to the left
                    var eye = s.Point - f.U * (14.0 / 1000) + f.V * (1.5 / 1000);
                    Shoot($"closeup-{kt:F0}kt", At(eye, s.GroundM + 4.5), At(s.Point + f.V * (-1.5 / 1000), s.GroundM + 5.4), 40, At(s.Point, s.GroundM));
                }
            }
            File.WriteAllText(Path.Combine(outDir, "cameras.txt"), log.ToString());
            RenderTexture.active = null;
            Debug.Log($"WindsockShots: wrote plates to {outDir}\n{log}");
        }
    }
}

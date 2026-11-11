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
    /// Review plates for airport lighting (2026-11-05). Builds the island the game builds and shoots, from a camera
    /// placed on a stated angle above the aid (every colour comes from <see cref="AirportLights.Step"/> at that eye):
    ///   papi-*   PHDH runway 26, 1 NM out: slightly high (3.35°), on (3.00°), slightly low (2.65°), low (2.20°);
    ///            a 3° telephoto on the four boxes and a 40° pilot's view;
    ///   vasi-*   PATK runway 2, 1 NM out: on (3.00°), low (2.40°), high (3.70°);
    ///   night-*  lit runways at night on a 3° final: PANC 16 (instrument: yellow last 2,000 ft, PAPI), PATK 2 (VASI),
    ///            AP10 (a lit airpark), and 3U2 (unlit) for comparison.
    /// Writes renders/airport-lighting/*.png and cameras.txt. Editor only.
    ///   Unity -batchmode -quit -projectPath unity -buildTarget iOS -executeMethod FlyingGame.EditorTools.AirportLightingShots.Render
    /// </summary>
    public static class AirportLightingShots
    {
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../renders/airport-lighting"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = new WorldTerrain(); WorldTerrain.Active = terrain;
            Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null; Atmosphere.Thermals.Clear();
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            Shader.SetGlobalFloat("_AirportDusk", 0);
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
            var lights = Object.FindFirstObjectByType<AirportLights>();
            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .3f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var log = new System.Text.StringBuilder();
            log.AppendLine($"lights: {(lights != null ? lights.RowCount : 0)} glide path rows, {AirportLighting.GlidePathUnits.Count} boxes, {AirportLighting.EdgeLamps.Count} runway lights");

            Vector3 At(double x, double y, double up) => WorldBuilder.U(x, y, up);
            void Shoot(string name, Vector3 eye, Vector3 look, float fov, bool forest)
            {
                cam.fieldOfView = fov;
                cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                lights?.Step(eye);
                GameObject trees = null;
                if (forest)
                {
                    trees = new GameObject("ForestView");
                    ForestBuilder.BuildAround(terrain, trees.transform, look.z, look.x, 2600, 800);
                }
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); if (trees != null) Object.DestroyImmediate(trees);
                log.AppendLine($"{name}\teye {eye}\tlook {look}\tfov {fov}");
            }
            GlidePathUnit[] Row(string field, string runway) => AirportLighting.GlidePathUnits.Where(u => u.FieldId == field && u.Runway == runway).OrderBy(u => u.Index).ToArray();
            // An eye `range` m out along the aid's facing, `deg` above its lens height (the angle the aid reads).
            Vec3 Eye(GlidePathUnit[] row, double deg, double range)
            {
                double x = row.Average(u => u.Point.X), y = row.Average(u => u.Point.Y), h = row[0].FacingDeg * System.Math.PI / 180;
                return new Vec3(x + System.Math.Cos(h) * range, y + System.Math.Sin(h) * range, -(row[0].LensM + range * System.Math.Tan(deg * System.Math.PI / 180)));
            }
            string Reading(GlidePathUnit[] row, Vec3 eye)
                => string.Join(" ", row.Select(u => u.WhiteFraction(eye) > .5 ? "W" : "R")) + "  elev " + string.Join(" ", row.Select(u => AirportLighting.ElevationDeg(u, eye).ToString("F2")));
            Vector3 RowCentre(GlidePathUnit[] row) => At(row.Average(u => u.Point.X), row.Average(u => u.Point.Y), row[0].LensM);
            Vector3 ThresholdView(GlidePathUnit[] row, double aheadM)
            {
                var f = IslandTerrain.Fields.First(q => q.Id == row[0].FieldId);
                int s = AirportLighting.EndSide(f, row[0].Runway);
                var p = f.Centre + f.U * ((s * f.Length / 2 - s * aheadM) / 1000);
                return At(p.X, p.Y, f.Plane(p));
            }

            // PAPI: PHDH 26.
            var papi = Row("PHDH 8/26", "26");
            foreach (var (slug, deg) in new[] { ("slightly-high", 3.35), ("on-path", 3.0), ("slightly-low", 2.65), ("low", 2.2) })
            {
                var e = Eye(papi, deg, 1852); Vector3 eye = At(e.X, e.Y, -e.Z);
                log.AppendLine($"papi-{slug} {deg:F2} deg: {Reading(papi, e)}  (index 0 = nearest runway)");
                Shoot($"papi-{slug}-tele", eye, RowCentre(papi), 3f, false);
                Shoot($"papi-{slug}-pilot", eye, ThresholdView(papi, 300), 40f, true);
            }
            // VASI: PATK 2.
            var vasi = Row("PATK 2/20", "2");
            foreach (var (slug, deg) in new[] { ("on-path", 3.0), ("low", 2.4), ("high", 3.7) })
            {
                var e = Eye(vasi, deg, 1852); Vector3 eye = At(e.X, e.Y, -e.Z);
                log.AppendLine($"vasi-{slug} {deg:F2} deg: {Reading(vasi, e)}  (0-1 downwind/near bar, 2-3 upwind/far bar)");
                Shoot($"vasi-{slug}-tele", eye, RowCentre(vasi), 4f, false);
                Shoot($"vasi-{slug}-pilot", eye, ThresholdView(vasi, 300), 40f, true);
            }

            // Night: no night in the game, so this plate sets one up (dark sky, no sun, halos on).
            sun.intensity = 0; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.015f, .017f, .03f);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.008f, .012f, .03f);
            Shader.SetGlobalFloat("_AirportDusk", 1); Shader.SetGlobalFloat("_CityNight", 1);
            foreach (var (field, runway, back) in new[] { ("PANC 16/34", "16", 3000.0), ("PATK 2/20", "2", 2500.0), ("AP10", "", 1500.0), ("3U2 19/1", "19", 1500.0) })
            {
                var f = IslandTerrain.Fields.First(q => q.Id == field);
                int s = runway == "" ? -1 : AirportLighting.EndSide(f, runway);
                var thr = f.Centre + f.U * (s * f.Length / 2000);
                var eyeP = thr + f.U * (s * back / 1000);
                double tz = f.Plane(thr), ez = System.Math.Max(tz + back * System.Math.Tan(3 * System.Math.PI / 180), IslandTerrain.HeightKm(eyeP) + 30);
                var aim = f.Centre - f.U * (s * (f.Length / 2 - 400) / 1000);
                string slug = field.Split(' ')[0].ToLowerInvariant();
                Shoot($"night-{slug}-final", At(eyeP.X, eyeP.Y, ez), At(aim.X, aim.Y, f.Plane(aim)), 40f, false);
            }
            Shader.SetGlobalFloat("_AirportDusk", 0); Shader.SetGlobalFloat("_CityNight", 0);
            File.WriteAllText(Path.Combine(outDir, "cameras.txt"), log.ToString());
            RenderTexture.active = null;
            Debug.Log($"AirportLightingShots: wrote plates to {outDir}\n{log}");
        }
    }
}

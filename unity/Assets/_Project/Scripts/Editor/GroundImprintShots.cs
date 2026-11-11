using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Review plates for the marks the ground keeps (lane/claude-imprint-render, 2026-11-10; docs/GROUND-IMPRINTS.md).
    /// Ty 2026-11-10: *"if it is penetrating it should leave imprints behind as it slides and rolls displacing the dirt
    /// or snow etc"*.
    ///
    /// Builds the island the game builds, rolls an aeroplane down a strip of each surface so it lays its own marks
    /// through the real contact path (no mark is placed by hand here), refreshes
    /// <see cref="GroundImprintVisual"/> and shoots:
    ///   plan-*     straight down on the tracks from 30 m, which is where a ski track's width against a tyre's reads;
    ///   eye-*      a pilot's eye height beside the tracks, looking along them — how they look walking back to the
    ///              aeroplane, and the view that shows whether they sit ON the ground;
    ///   low-*      half a metre up, 4 m back, looking along one rut: the close look at the trough and its shoulders.
    /// Writes renders/ground-imprints/*.png and marks.txt. Editor only.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.GroundImprintShots.Render
    /// </summary>
    public static class GroundImprintShots
    {
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../renders/ground-imprints"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = new WorldTerrain(); WorldTerrain.Active = terrain;
            WorldClock.TimeS = 20;
            Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null; Atmosphere.Thermals.Clear();
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.SimTimeSec = 0;
            GameObject world = WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0); RenderSettings.sun = sun;

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .05f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var log = new System.Text.StringBuilder();

            GroundImprintVisual marks = GroundImprintVisual.Attach(world.transform);

            // A grass strip to roll on, so the ground under the tracks is the real island and not a test plane. The
            // SURFACE is overridden per scene: every ground that marks is soft ground, and the strips the island has
            // are not all of them.
            IslandField field = IslandTerrain.Fields.First(q => q.Id == "AP1");

            foreach (var scene in new (string name, string id, WorldTerrain.Surface surface, bool skis, double speed, double rudder, double seconds)[]
            {
                ("cub-grass",     "j3-cub-like",   WorldTerrain.Surface.Grass, false, 14.0, 0.0, 5.0),
                ("cub-snow",      "j3-cub-like",   WorldTerrain.Surface.Snow,  false, 12.0, 0.0, 5.0),
                ("cub-skis-snow", "pa18-cub-like", WorldTerrain.Surface.Snow,  true,  12.0, 0.0, 5.0),
                ("cub-snow-turn", "j3-cub-like",   WorldTerrain.Surface.Snow,  false, 10.0, 0.6, 6.0),
            })
            {
                GroundImprints.Clear();
                WorldTerrain.SurfaceOverride = scene.surface;
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(scene.id);
                if (scene.skis) cfg.Skis = true;
                // On the strip, at its own heading, so the tracks lie down the strip and the ground under them is the
                // island's own. The velocity is in the BODY frame, so (speed, 0, 0) is straight ahead.
                IslandPoint p0 = field.Centre - field.U * (250.0 / 1000);
                RigidBodyState rest = LandingGear.RestingState(cfg, p0.X, p0.Y, field.SurfaceHeight(p0),
                                                               field.Heading * System.Math.PI / 180.0,
                                                               scene.skis ? 1.0 : double.NaN);
                var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(scene.speed, 0, 0), Vec3.Zero);
                var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
                var sim = new SimLoop(ac);
                sim.RunFor(scene.seconds, new ControlInputs(0, 0, scene.rudder, 1.0));
                marks.Refresh();

                GroundImprintField ring = GroundImprints.World;
                double deepest = 0, widest = 0;
                var kinds = new System.Collections.Generic.Dictionary<GroundImprintKind, int>();
                Vec3 lo = new(double.MaxValue, double.MaxValue, 0), hi = new(double.MinValue, double.MinValue, 0);
                for (int i = 0; i < ring.Count; i++)
                {
                    GroundImprint k = ring[i];
                    kinds.TryGetValue(k.Kind, out int n); kinds[k.Kind] = n + 1;
                    deepest = System.Math.Max(deepest, k.DepthM); widest = System.Math.Max(widest, k.WidthM);
                    lo = new Vec3(System.Math.Min(lo.X, k.StartWorld.X), System.Math.Min(lo.Y, k.StartWorld.Y), 0);
                    hi = new Vec3(System.Math.Max(hi.X, k.EndWorld.X), System.Math.Max(hi.Y, k.EndWorld.Y), 0);
                }
                log.AppendLine($"{scene.name}: {ring.Count} stretches, {marks.DrawnTriangles} triangles, deepest {1000 * deepest:F1} mm, widest {1000 * widest:F0} mm, kinds [{string.Join(", ", kinds.Select(p => p.Key + " x" + p.Value))}]");
                if (ring.Count == 0) continue;

                Vec3 mid = (lo + hi) * 0.5;
                double ground = WorldTerrain.GroundHeightAt(mid.X, mid.Y);
                Vector3 centre = CoordinateMap.ToUnity(new Vec3(mid.X, mid.Y, -ground));
                Vector3 downStrip = (CoordinateMap.ToUnity(new Vec3(hi.X, hi.Y, -ground)) - CoordinateMap.ToUnity(new Vec3(lo.X, lo.Y, -ground))).normalized;
                Vector3 beside = Vector3.Cross(Vector3.up, downStrip).normalized;

                void Shoot(string name, Vector3 eye, Vector3 look, float fov)
                {
                    cam.fieldOfView = fov;
                    cam.transform.position = eye;
                    cam.transform.rotation = Quaternion.LookRotation((look - eye).normalized, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    log.AppendLine($"  {name}\teye {eye}\tlook {look}\tfov {fov}");
                }

                Shoot($"plan-{scene.name}", centre + Vector3.up * 30f, centre, 50);
                Shoot($"eye-{scene.name}", centre - downStrip * 18f + beside * 3f + Vector3.up * 1.7f, centre + Vector3.up * 0.1f, 45);
                Shoot($"low-{scene.name}", centre - downStrip * 4f + beside * 0.9f + Vector3.up * 0.5f, centre + downStrip * 6f, 35);
            }

            WorldTerrain.SurfaceOverride = null;
            File.WriteAllText(Path.Combine(outDir, "marks.txt"), log.ToString());
            RenderTexture.active = null;
            Debug.Log($"GroundImprintShots: wrote plates to {outDir}\n{log}");
        }
    }
}

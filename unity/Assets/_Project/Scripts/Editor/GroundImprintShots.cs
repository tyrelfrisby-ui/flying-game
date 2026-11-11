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
    /// Builds the island the game builds and rolls an aeroplane across ground that REALLY IS the surface the scene
    /// claims — the grass strip for grass, a measured snowfield for snow — so it lays its own marks through the real
    /// contact path. No mark is placed by hand and no surface is overridden. Then it refreshes
    /// <see cref="GroundImprintVisual"/> and shoots, framed from the START of the roll so the tracks run away from
    /// the camera:
    ///   plan-*     straight down from 30 m over the tracks, which is where a ski track's width against a tyre's
    ///              reads, and the view a mark must win the depth test face-on to appear in at all;
    ///   eye-*      a pilot's eye height, 6 m back and 3 m to the side: how they look walking back to the aeroplane;
    ///   low-*      half a metre up, just behind the start, looking 20 m along: the trough and its shoulders close to.
    /// Writes renders/ground-imprints/*.png and marks.txt. Editor only, and it needs a GPU:
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.GroundImprintShots.Render
    /// NOT with -nographics, which the build scripts use and which this REFUSES to run under
    /// (<see cref="RequireAGraphicsDevice"/>). It refuses in FOUR places rather than shoot a plate that is not what
    /// it says it is: no graphics device, no material to draw with, stretches in the record with no triangles in the
    /// mesh, and ground that is not the surface the scene claims
    /// (<see cref="RequireTheGroundItClaims"/> — the one that actually got through). A plate's whole job is to be
    /// trusted.
    /// </summary>
    public static class GroundImprintShots
    {
        /// <summary>
        /// Refuse to run without a graphics device, loudly, rather than write a picture of nothing.
        ///
        /// MEASURED 2026-11-10: run with -nographics (which is the idiom the BUILD scripts use, so it is the natural
        /// thing to reach for) this wrote one blank grey PNG and then segfaulted, exit 139, after the first frame.
        /// There is no device to rasterise into, so Camera.Render does nothing and ReadPixels reads an empty buffer.
        ///
        /// The blank frame is the part that matters. A harness that CRASHES is a nuisance; a harness that quietly
        /// emits a plausible-looking empty picture is a liar, and that night's blank plate was nearly sent on as "the
        /// real island render" (another agent's harness silently produced mirrored images on the same night — the
        /// same shape of failure). A review plate's whole job is to be trusted, so this one says what is wrong and
        /// stops.
        /// </summary>
        private static void RequireAGraphicsDevice()
        {
            // The project's own test for this (WaterMagentaCheck, WaterMirrorViewportCheck): the device type's NAME,
            // which needs no extra using and cannot pick up the wrong Rendering namespace in an Editor assembly.
            if (!SystemInfo.graphicsDeviceType.ToString().Contains("Null")) return;
            throw new System.InvalidOperationException(
                "GroundImprintShots needs a GPU: run without -nographics. With it, Camera.Render draws nothing and " +
                "ReadPixels returns an empty buffer, so this would write a BLANK PLATE and then crash (measured: one " +
                "grey PNG, then exit 139).");
        }

        /// <summary>
        /// Refuse to roll a scene on ground that is not what the scene says it is.
        ///
        /// This is the lie that got through: the first version set <c>WorldTerrain.SurfaceOverride</c>, which steers
        /// the physics and not the island's shader, so three plates labelled "snow" were snow-coloured marks on green
        /// turf and the ski-against-tyre comparison could not be seen in any of them. Nothing in the pictures said so.
        /// The ground is now real and this checks it, over the whole length of the intended roll rather than at the
        /// start — a site can begin on snow and run off it.
        /// </summary>
        private static void RequireTheGroundItClaims(string scene, double x, double y, double headingDeg,
                                                     WorldTerrain.Surface want)
        {
            double c = System.Math.Cos(headingDeg * System.Math.PI / 180.0), s = System.Math.Sin(headingDeg * System.Math.PI / 180.0);
            for (double d = -10; d <= 90; d += 5)
            {
                WorldTerrain.Surface got = WorldTerrain.SurfaceAt(x + c * d, y + s * d);
                if (got == want) continue;
                throw new System.InvalidOperationException(
                    $"GroundImprintShots: {scene} says it rolls on {want}, but the ground {d:F0} m along its heading " +
                    $"is {got}. The plate would show an aeroplane marking one surface while the picture shows " +
                    "another, which is the exact failure this harness was rewritten to make impossible.");
            }
        }

        public static void Render()
        {
            RequireAGraphicsDevice();
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../renders/ground-imprints"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = new WorldTerrain(); WorldTerrain.Active = terrain;
            WorldClock.TimeS = 20;
            Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null; Atmosphere.Thermals.Clear();
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.SimTimeSec = 0;
            // BEFORE the world is built, because IslandWorldBuilder.Build attaches the GroundImprintVisual itself and
            // the visual takes its shader once. Shader.Find returns NULL for this project's own shaders in a bare
            // -executeMethod batchmode editor: measured 2026-11-10, for FlyingGame/GroundImprint and for its
            // FlyingGame/UnlitTransparent fallback alike, both present and compiling clean, so the LOOKUP was the
            // problem and not the assets. An editor tool has the asset database instead — the same answer this repo
            // already uses for glTFast under IsEditorImport, and the same hand-off PilotBodyShots makes to
            // VrPilotBody.PrefabOverride.
            GroundImprintVisual.ShaderOverride = AssetDatabase.LoadAssetAtPath<Shader>(GroundImprintVisual.ShaderAssetPath);
            if (GroundImprintVisual.ShaderOverride == null)
                throw new System.InvalidOperationException(
                    $"GroundImprintShots: {GroundImprintVisual.ShaderAssetPath} did not load from the asset database. " +
                    "Nothing would draw the marks and every plate would be a picture of empty ground.");

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
            if (marks != null) marks.Refresh();   // picks up the shader if the world built before it was set
            // The second way this harness could lie: the marks are recorded, nothing draws them, and the plate is a
            // picture of empty ground that reads exactly like a flight that left none.
            if (marks == null || !marks.Drawable)
                throw new System.InvalidOperationException(
                    "GroundImprintShots: GroundImprintVisual has no material even though the shader asset loaded. The " +
                    "ground would keep its marks and the plates would show empty ground, which is a lie, not a render.");

            // Each scene rolls on ground that REALLY IS the surface it claims, and nothing is overridden.
            //
            // The first version of this harness set WorldTerrain.SurfaceOverride and rolled every scene down the AP1
            // grass strip. The override steers the PHYSICS and nothing else — the island's own shader paints what it
            // paints — so the three "snow" plates were snow-coloured marks lying on green turf, and the
            // ski-against-tyre comparison, which is the single most persuasive thing in this lane, could not be seen
            // in any of them: both aeroplanes were rolling on the same grass. A plate that lies about what the
            // aeroplane is on is worse than no plate (2026-11-10).
            //
            // MEASURED sites, so each scene's ground and its picture are the same ground by construction:
            //   AP1        the island's grass strip, on its own heading.
            //   (-4600, -4600) heading 225  snow for 295 m at a worst slope of 0.3 deg, the flattest long run of real
            //                  snow on the island (every rollable snow site was swept; 313 have 80 m or more).
            IslandField field = IslandTerrain.Fields.First(q => q.Id == "AP1");
            IslandPoint strip = field.Centre - field.U * (250.0 / 1000);

            foreach (var scene in new (string name, string id, WorldTerrain.Surface surface, bool skis,
                                       double x, double y, double headingDeg, double speed, double rudder, double seconds)[]
            {
                ("cub-grass",     "j3-cub-like",   WorldTerrain.Surface.Grass, false, strip.X, strip.Y, field.Heading, 14.0, 0.0, 5.0),
                ("cub-snow",      "j3-cub-like",   WorldTerrain.Surface.Snow,  false, -4600,   -4600,   225.0,         12.0, 0.0, 5.0),
                ("cub-skis-snow", "pa18-cub-like", WorldTerrain.Surface.Snow,  true,  -4600,   -4600,   225.0,         12.0, 0.0, 5.0),
                ("cub-snow-turn", "j3-cub-like",   WorldTerrain.Surface.Snow,  false, -4600,   -4600,   225.0,         10.0, 0.6, 6.0),
            })
            {
                GroundImprints.Clear();
                RequireTheGroundItClaims(scene.name, scene.x, scene.y, scene.headingDeg, scene.surface);
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(scene.id);
                if (scene.skis) cfg.Skis = true;
                // The velocity is in the BODY frame, so (speed, 0, 0) is straight ahead on the scene's heading.
                double headingRad = scene.headingDeg * System.Math.PI / 180.0;
                RigidBodyState rest = LandingGear.RestingState(cfg, scene.x, scene.y,
                                                               WorldTerrain.GroundHeightAt(scene.x, scene.y),
                                                               headingRad, scene.skis ? 1.0 : double.NaN);
                var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(scene.speed, 0, 0), Vec3.Zero);
                var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
                var sim = new SimLoop(ac);
                sim.RunFor(scene.seconds, new ControlInputs(0, 0, scene.rudder, 1.0));
                marks.Refresh();

                GroundImprintField ring = GroundImprints.World;
                GroundMaterial want = GroundMaterials.Of(scene.surface);
                double deepest = 0, widest = 0;
                var kinds = new System.Collections.Generic.Dictionary<GroundImprintKind, int>();
                var materials = new System.Collections.Generic.Dictionary<GroundMaterial, int>();
                for (int i = 0; i < ring.Count; i++)
                {
                    GroundImprint k = ring[i];
                    kinds.TryGetValue(k.Kind, out int n); kinds[k.Kind] = n + 1;
                    materials.TryGetValue(k.Material, out int m); materials[k.Material] = m + 1;
                    deepest = System.Math.Max(deepest, k.DepthM); widest = System.Math.Max(widest, k.WidthM);
                }
                log.AppendLine($"{scene.name}: on {scene.surface} at ({scene.x:F0}, {scene.y:F0}) hdg {scene.headingDeg:F0}; {ring.Count} stretches, {marks.DrawnTriangles} triangles, deepest {1000 * deepest:F1} mm, widest {1000 * widest:F0} mm, kinds [{string.Join(", ", kinds.Select(p => p.Key + " x" + p.Value))}], materials [{string.Join(", ", materials.Select(p => p.Key + " x" + p.Value))}]");
                // The marks' own MATERIAL is what the drawing takes its colour from, so it is the last place the
                // picture and the physics could still disagree about what the aeroplane is rolling on.
                foreach (GroundMaterial got in materials.Keys)
                    if (got != want)
                        throw new System.InvalidOperationException(
                            $"GroundImprintShots: {scene.name} says {scene.surface} ({want}) but laid marks in {got}. " +
                            "The plate would be coloured for one ground and drawn on another.");
                // And the third: stretches in the record but no triangles in the mesh. Shoot nothing rather than
                // something that cannot be told apart from an aeroplane that marked nothing.
                if (ring.Count == 0) { log.AppendLine($"  {scene.name}: NO MARKS LAID — no plates shot."); continue; }
                if (marks.DrawnTriangles == 0)
                    throw new System.InvalidOperationException(
                        $"GroundImprintShots: {scene.name} laid {ring.Count} stretches and the mesh has no triangles. " +
                        "Something between GroundImprintMesh.Refresh and the MeshFilter is broken; a plate of this " +
                        "would be a picture of nothing.");

                // The way the tracks RUN is the scene's own heading. It used to come from the corner-to-corner
                // diagonal of the marks' bounding box, which is only the direction of travel when the roll happens to
                // go toward +x and +y: for AP1, heading 090, it was reversed, so every camera stood past the end of
                // the tracks looking away from them and the plates were mostly empty field (2026-11-10).
                Vector3 along = CoordinateMap.ToUnity(new Vec3(System.Math.Cos(headingRad), System.Math.Sin(headingRad), 0)).normalized;
                Vector3 beside = Vector3.Cross(Vector3.up, along).normalized;
                Vector3 Ground(double downTrack)
                {
                    double gx = scene.x + System.Math.Cos(headingRad) * downTrack;
                    double gy = scene.y + System.Math.Sin(headingRad) * downTrack;
                    return CoordinateMap.ToUnity(new Vec3(gx, gy, -WorldTerrain.GroundHeightAt(gx, gy)));
                }

                void Shoot(string name, Vector3 eye, Vector3 look, float fov, Vector3 upHint)
                {
                    cam.fieldOfView = fov;
                    cam.transform.position = eye;
                    cam.transform.rotation = Quaternion.LookRotation((look - eye).normalized, upHint);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    log.AppendLine($"  {name}\teye {eye}\tlook {look}\tfov {fov}");
                }

                // Straight down takes `along` as its up hint, not Vector3.up: LookRotation with the two collinear is
                // degenerate, and it also puts the tracks running up the picture where they are easiest to compare.
                Shoot($"plan-{scene.name}", Ground(25) + Vector3.up * 30f, Ground(25), 50, along);
                Shoot($"eye-{scene.name}", Ground(-6) + beside * 3f + Vector3.up * 1.7f, Ground(25) + Vector3.up * 0.2f, 45, Vector3.up);
                Shoot($"low-{scene.name}", Ground(1) + beside * 0.55f + Vector3.up * 0.5f, Ground(20) + Vector3.up * 0.1f, 35, Vector3.up);
            }

            File.WriteAllText(Path.Combine(outDir, "marks.txt"), log.ToString());
            RenderTexture.active = null;
            Debug.Log($"GroundImprintShots: wrote plates to {outDir}\n{log}");
        }
    }
}

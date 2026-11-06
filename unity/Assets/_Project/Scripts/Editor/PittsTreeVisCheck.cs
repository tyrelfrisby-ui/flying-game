using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-05 13:41 (iPhone 2026.1105.1140, Q028): the Pitts into a tree "vanished while crash sounds continued".
    /// Flies the real sim Pitts square into a real island fir (Forest.Live), poses the real airframe model at the sim's
    /// state, builds the forest the game builds (ForestBuilder) and renders the crash camera's view (30° above, at its
    /// closest and farthest range, from behind and from the side). For each shot it counts how much of the OCCUPIED airframe
    /// is on screen: the airframe's own pixels (rendered alone on black and on white: what matches is airframe) that change
    /// when it is added to the scene. Debris is removed, so only the occupied airframe is counted.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.PittsTreeVisCheck.Run [-treeVisMode before|after]
    /// "before" never strips the tree (the drawing Ty saw); "after" runs StrippedTrees as StructuralDamage does. Images and
    /// the log line per shot go to build/pitts-tree-vis/. Exit 0; the numbers are the result.
    /// </summary>
    public static class PittsTreeVisCheck
    {
        private const int AirLayer = 30, W = 640, H = 400;

        public static void Run()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int ai = System.Array.IndexOf(args, "-treeVisMode");
            string mode = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "after";
            bool strip = mode != "before";
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/pitts-tree-vis"));
            Directory.CreateDirectory(outDir);
            var log = new System.Text.StringBuilder();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var terrain = new WorldTerrain();
                WorldTerrain.Active = terrain;
                WorldClock.TimeS = 20;
                WorldBuilder.BuildAll();
                SunsetLighting.ApplyDay();
                RenderSettings.fog = false;
                Forest.ClearBroken(); StrippedTrees.Clear();

                // A 22-32 m fir near the -crashtest grass site, with 45 m of open air south of it at mid-height.
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
                RigidBodyState grass = CrashScenario.PittsStart(cfg, true);
                Forest.Tree tree = default; bool found = false; double bestD = double.MaxValue;
                for (double r = 200; r <= 3200 && !found; r *= 2)
                    foreach (Forest.Tree ft in Forest.InRect(grass.Position.X - r, grass.Position.Y - r, grass.Position.X + r, grass.Position.Y + r))
                    {
                        if (ft.HeightM < 22 || ft.HeightM > 32) continue;
                        double d = System.Math.Abs(ft.X - grass.Position.X) + System.Math.Abs(ft.Y - grass.Position.Y);
                        if (d >= bestD) continue;
                        bool clear = true;
                        foreach (Forest.Tree _ in Forest.InRect(ft.X - 48, ft.Y - 7, ft.X - 1, ft.Y + 7)) { clear = false; break; }
                        if (!clear || System.Math.Abs(terrain.HeightAt(ft.X - 40, ft.Y) - ft.GroundM) > 3) continue;
                        tree = ft; bestD = d; found = true;
                    }
                if (!found) throw new System.Exception("no fir found");
                double up0 = tree.GroundM + 0.48 * tree.HeightM;
                log.AppendLine($"[TreeVis] mode {mode}: fir at ({tree.X:0.0}, {tree.Y:0.0}) {tree.HeightM:0.0} m tall, ground {tree.GroundM:0.0}; " +
                               $"Pitts north at 35 m/s, CG {up0 - tree.GroundM:0.0} m up, from 40 m south");

                var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(tree.X - 40, tree.Y, -up0), Quat.Identity, new Vec3(35, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
                ac.Trees = Forest.Live;
                var relay = new CrashRelay(ac, subscribe: false);
                var sim = new SimLoop(ac);

                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;
                var root = new GameObject("pitts");
                var builder = new AirframeBuilder();
                builder.Build(root.transform, cfg);
                var lostSeen = new HashSet<AirframeComponent>();

                var forestRoot = new GameObject("ForestView");
                int builtVersion = -1;
                void BuildForest()
                {
                    for (int i = forestRoot.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(forestRoot.transform.GetChild(i).gameObject);
                    ForestBuilder.BuildAround(terrain, forestRoot.transform, tree.X, tree.Y, 500, 550);
                    builtVersion = StrippedTrees.Version;
                }
                BuildForest();

                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 6000f;
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                Color32[] Grab(int mask, CameraClearFlags flags, Color bg)
                {
                    cam.cullingMask = mask; cam.clearFlags = flags; cam.backgroundColor = bg;
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                    return tex.GetPixels32();
                }

                double[] shots = { 1.0, 1.3, 1.6, 2.0, 2.5, 3.0, 4.0 };
                int next = 0; double t = 0, dt = 1.0 / 60;
                while (next < shots.Length)
                {
                    sim.RunFor(dt, new ControlInputs(0, 0, 0, 1));
                    relay.Step(dt);
                    t += dt;
                    if (strip) StrippedTrees.Observe(Forest.Live, ac.State.Position, cfg.Fuselage.Crossflow.BodyRadiusM,
                        0.5 * System.Math.Max(2.0 * WingPanels.Semispan(cfg), cfg.Fuselage.Crossflow.LengthM));
                    foreach (AirframeComponent c in ac.LostComponents)
                        if (lostSeen.Add(c))
                        {
                            GameObject d = AirframeVisual.DetachStatic(builder, root.transform, cfg, c, Vector3.zero);
                            if (d != null) Object.DestroyImmediate(d);   // only the occupied airframe is counted
                        }
                    if (t + 1e-9 < shots[next]) continue;
                    next++;
                    if (StrippedTrees.Version != builtVersion) BuildForest();
                    root.transform.SetPositionAndRotation(CoordinateMap.ToUnity(ac.State.Position), CoordinateMap.ToUnity(ac.State.Attitude));
                    foreach (Transform tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = AirLayer;
                    Vec3 p = ac.State.Position;
                    double rho = System.Math.Sqrt((p.X - tree.X) * (p.X - tree.X) + (p.Y - tree.Y) * (p.Y - tree.Y));
                    Vector3 focus = root.transform.position;
                    foreach ((string bearingName, Vector3 bearing) in new[] { ("behind", new Vector3(0, 0, -1)), ("side", new Vector3(1, 0, 0)) })
                        foreach (float range in new[] { 24.75f, 49.5f })
                        {
                            float e = 30f * Mathf.Deg2Rad;
                            cam.transform.position = focus + (bearing * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)) * range;
                            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
                            Color32[] onBlack = Grab(1 << AirLayer, CameraClearFlags.SolidColor, Color.black);
                            Color32[] onWhite = Grab(1 << AirLayer, CameraClearFlags.SolidColor, Color.white);
                            Color32[] without = Grab(~(1 << AirLayer), CameraClearFlags.SolidColor, new Color(0.55f, 0.7f, 0.95f));
                            Color32[] with = Grab(~0, CameraClearFlags.SolidColor, new Color(0.55f, 0.7f, 0.95f));
                            int own = 0, seen = 0;
                            for (int i = 0; i < with.Length; i++)
                            {
                                if (System.Math.Abs(onBlack[i].r - onWhite[i].r) + System.Math.Abs(onBlack[i].g - onWhite[i].g) + System.Math.Abs(onBlack[i].b - onWhite[i].b) > 6) continue;
                                own++;
                                if (System.Math.Abs(with[i].r - without[i].r) + System.Math.Abs(with[i].g - without[i].g) + System.Math.Abs(with[i].b - without[i].b) > 12) seen++;
                            }
                            string name = $"{mode}-t{shots[next - 1]:0.0}-{bearingName}-{range:0}m";
                            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                            log.AppendLine($"[TreeVis] {name}: airframe {own} px, on screen {seen} px = {(own > 0 ? 100.0 * seen / own : 0):0}% | " +
                                           $"CG {rho:0.00} m from the stem, {-p.Z - tree.GroundM:0.00} m up, stripped {StrippedTrees.DrawnMask(tree)}, lost [{string.Join(",", ac.LostComponents)}]");
                        }
                }
                foreach (var r in ac.TreeStrikeLog) log.AppendLine("[TreeVis] strike " + r);
            }
            catch (System.Exception ex) { log.AppendLine("[TreeVis] FAILED " + ex); }
            finally
            {
                WorldTerrain.Active = null;
                Forest.ClearBroken(); StrippedTrees.Clear();
                File.WriteAllText(Path.Combine(outDir, $"log-{mode}.txt"), log.ToString());
                Debug.Log(log.ToString());
            }
        }
    }
}

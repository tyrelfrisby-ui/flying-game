using System;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Review renders for the hinged wheel-skis and the shared WHEELS / SKIS selector (Ty 2026-11-05), built the way the game
    /// builds them: AirframeBuilder with the option on (the same <see cref="SkiGear"/> geometry the physics uses), the
    /// aeroplane resting as <see cref="LandingGear.RestingState"/> puts it, each ski laid on the ground by one
    /// LandingGear.Compute call, and the selector by <see cref="SkiSelectorLever"/> at its config anchor.
    ///   WS_EXT  = ids for the exterior (default pa18-cub-like,c172-like,j3-cub-like)
    ///   WS_COCKPIT = ids for the cockpit (default j3-cub-like,c172-like,pitts-s2b-like; seat = the first seat)
    ///   WS_OUT  = output directory (default ~/Developer/agent-lanes/renders/wheel-skis)
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.WheelSkiShots.Run   (no -nographics)
    /// </summary>
    public static class WheelSkiShots
    {
        private static readonly (string tag, double lowered)[] States = { ("raised-WHEELS", 0.0), ("transit-half", 0.5), ("lowered-SKIS", 1.0) };

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("WS_OUT")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Developer", "agent-lanes", "renders", "wheel-skis");
            int shots = 0, failures = 0;
            bool oldSkis = SessionSettings.Skis, oldBush = SessionSettings.Bushwheels, oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(outDir);
                SessionSettings.Skis = true; SessionSettings.Bushwheels = false;
                SessionSettings.UseAirframeModels = true; SessionSettings.UseOwnModels = true;
                string measure = Environment.GetEnvironmentVariable("WS_MEASURE");
                if (measure != null)
                {
                    // "id|dx,dy,dz;..." : cast from the first seat's eye along a body-frame direction against the placed
                    // model and log where it lands — the number a seat's skiSelector anchor is authored from.
                    foreach (string m in measure.Split(';'))
                    {
                        string[] f = m.Split('|'); string[] d = f[1].Split(',');
                        if (!Measure(f[0], new Vec3(F(d[0]), F(d[1]), F(d[2])))) failures++;
                    }
                    return;
                }
                foreach (string id in (Environment.GetEnvironmentVariable("WS_EXT") ?? "pa18-cub-like,c172-like,j3-cub-like").Split(','))
                    shots += Exterior(id.Trim(), outDir, ref failures);
                foreach (string id in (Environment.GetEnvironmentVariable("WS_COCKPIT") ?? "j3-cub-like,c172-like,pitts-s2b-like").Split(','))
                    shots += Cockpit(id.Trim(), outDir, ref failures);
            }
            catch (Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                SessionSettings.Skis = oldSkis; SessionSettings.Bushwheels = oldBush;
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[WheelSkiShots] {(failures == 0 ? "PASS" : "FAIL")}: {shots} shots, {failures} failures -> {outDir}");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static (GameObject root, AirframeBuilder builder, AircraftConfig cfg, Aircraft ac) Scene(string id, double lowered, bool onGround)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            if (cfg == null) return (null, null, null, null);
            cfg.Skis = true;
            RigidBodyState rest = LandingGear.RestingState(cfg, 0, 0, 0, 0, lowered);
            var ac = new Aircraft(cfg, rest);
            // Started on pavement the skis are up (SkiSelector.StartFor); SKIS selected, the hydraulics run them down to
            // the fraction the shot asks for (the lever already at SKIS and the lamps at TRANSIT half way).
            if (lowered > 0) { ac.Skis.Selector.Select(SkiSelector.Position.Skis); ac.Skis.Selector.Step(lowered * SkiGear.TransitSec); }
            LandingGear.Compute(cfg, rest, 0, 0, skis: ac.Skis);   // lays each bearing ski on the ground (PitchRad / Bearing)

            var root = new GameObject("Aircraft");
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            builder.SetSkis(ac.Skis);
            if (onGround)
            {
                root.transform.rotation = CoordinateMap.ToUnity(rest.Attitude);
                root.transform.position = new Vector3(0f, (float)-rest.Position.Z - 0.02f, 0f);
            }
            var lever = root.AddComponent<SkiSelectorLever>();
            lever.Build(builder, cfg, ac, Vector3.up * 1.0f);
            lever.Show(ac.Skis.Selector);
            Light(onGround);
            return (root, builder, cfg, ac);
        }

        private static double F(string x) => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture);

        private static bool Measure(string id, Vec3 dirBody)
        {
            var (root, builder, cfg, ac) = Scene(id, 0.0, onGround: false);
            if (root == null || cfg.Loading?.Seats == null || cfg.Loading.Seats.Count == 0) { Debug.LogError($"[WheelSkiShots] MEASURE {id}: no seat"); return false; }
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.name.StartsWith("SkiSelector") || !mf.GetComponent<Renderer>().enabled) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();
            Vec3 eyeB = cfg.Loading.Seats[0].EyeVec();
            Vector3 eye = root.transform.TransformPoint(CoordinateMap.ToUnity(eyeB));
            Vector3 dir = root.transform.TransformDirection(CoordinateMap.ToUnity(dirBody)).normalized;
            bool ok = Physics.Raycast(eye, dir, out RaycastHit hit, 2.0f);
            if (ok)
            {
                Vec3 hitB = CoordinateMap.ToSim(root.transform.InverseTransformPoint(hit.point));
                Vec3 back = hitB - (hitB - eyeB) * (0.012 / (hitB - eyeB).Length);
                Debug.Log($"[WheelSkiShots] MEASURE {id}: from eye {eyeB.X:F3},{eyeB.Y:F3},{eyeB.Z:F3} along {dirBody.X},{dirBody.Y},{dirBody.Z} hit '{hit.collider.name}' " +
                          $"at body {hitB.X:F3},{hitB.Y:F3},{hitB.Z:F3} ({hit.distance:F3} m); anchor 12 mm proud: [{back.X:F3}, {back.Y:F3}, {back.Z:F3}]");
            }
            else Debug.LogError($"[WheelSkiShots] MEASURE {id}: nothing within 2 m");
            builder.Clear(); Object.DestroyImmediate(root);
            return ok;
        }

        private static void Light(bool ground)
        {
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.51f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.32f);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = new Vector3(0f, ground ? 0f : -1.5f, 0f); plane.transform.localScale = new Vector3(60f, 1f, 60f);
            // Snow: the ground a ski is for.
            plane.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.90f, 0.92f, 0.95f) };
        }

        private static int Exterior(string id, string outDir, ref int failures)
        {
            int n = 0;
            foreach ((string tag, double lowered) in States)
            {
                var (root, builder, cfg, ac) = Scene(id, lowered, onGround: true);
                if (root == null) { failures++; Debug.LogError($"[WheelSkiShots] {id}: no config"); continue; }
                Vec3 cg = cfg.Mass.CgVec();
                GearConfig left = null;
                foreach (GearConfig g in cfg.Gear) if (SkiGear.Fitted(cfg, g) && !g.IsTailwheel && g.Pos[1] < -0.05 && (left == null || g.Pos[2] > left.Pos[2])) left = g;
                Vector3 legW = left != null ? root.transform.TransformPoint(CoordinateMap.ToUnity(left.PosVec() - cg)) : root.transform.position;
                float span = 10f;
                Vector3 c = root.transform.position + Vector3.up * 0.6f;
                // The whole aeroplane, 3/4 from the front left; then the left main leg from the side, and from ahead-left.
                Shoot(c + new Vector3(-1.2f, 0.55f, 1.1f).normalized * span * 1.25f, c, 32f, Path.Combine(outDir, $"{id}-exterior-{tag}.png"));
                Vector3 legC = legW + Vector3.up * 0.18f;
                Shoot(legC + new Vector3(-1f, 0.12f, 0.0f).normalized * 2.4f, legC, 40f, Path.Combine(outDir, $"{id}-leg-side-{tag}.png"));
                Shoot(legC + new Vector3(-0.8f, 0.35f, 0.9f).normalized * 2.2f, legC, 40f, Path.Combine(outDir, $"{id}-leg-34-{tag}.png"));
                n += 3;
                int bearing = 0; foreach (bool b in ac.Skis.Bearing) if (b) bearing++;
                Debug.Log($"[WheelSkiShots] {id} {tag}: lowered {ac.Skis.Lowered01:F2}, {bearing} skis bearing, {builder.SkiCount} skis drawn, " +
                          $"annunciator {ac.Skis.Selector.Annunciation}");
                if (builder.SkiCount == 0) { failures++; Debug.LogError($"[WheelSkiShots] {id}: no skis drawn"); }
                builder.Clear(); Object.DestroyImmediate(root);
            }
            return n;
        }

        private static int Cockpit(string id, string outDir, ref int failures)
        {
            int n = 0;
            foreach ((string tag, double lowered) in States)
            {
                var (root, builder, cfg, ac) = Scene(id, lowered, onGround: false);
                if (root == null) { failures++; continue; }
                StationConfig seat = cfg.Loading?.Seats != null && cfg.Loading.Seats.Count > 0 ? cfg.Loading.Seats[0] : null;
                if (seat == null) { failures++; Debug.LogError($"[WheelSkiShots] {id}: no seat"); builder.Clear(); Object.DestroyImmediate(root); continue; }
                var lever = root.GetComponent<SkiSelectorLever>();
                Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
                Vector3 part = Vector3.zero;
                foreach (Vector3 k in lever.KnobsWorld) { part = k; break; }
                if (lever.Count == 0) { failures++; Debug.LogError($"[WheelSkiShots] {id}: no selector built"); }
                // From the seat eye: straight ahead (where the selector sits in the pilot's view), looking at it, and the
                // plate close up as if leaning in.
                if (tag == "lowered-SKIS") { Shoot(eye, eye + new Vector3(0f, -0.12f, 1f), 72f, Path.Combine(outDir, $"{id}-cockpit-ahead.png"), 2160, 1000); n++; }
                Shoot(eye, part, 55f, Path.Combine(outDir, $"{id}-cockpit-selector-{tag}.png"));
                Vector3 toward = (eye - part).normalized;
                Shoot(part + toward * 0.22f, part, 40f, Path.Combine(outDir, $"{id}-selector-closeup-{tag}.png"));
                n += 2;
                Debug.Log($"[WheelSkiShots] {id} cockpit {tag}: eye {eye}, selector knob {part}, lever {ac.Skis.Selector.Commanded}, lamp {ac.Skis.Selector.Annunciation}");
                builder.Clear(); Object.DestroyImmediate(root);
            }
            return n;
        }

        private static void Shoot(Vector3 from, Vector3 at, float vfov, string path, int w = 1600, int h = 1200)
        {
            var camGo = new GameObject("Cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.70f, 0.88f);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 2000f; cam.fieldOfView = vfov;
            camGo.transform.position = from;
            camGo.transform.LookAt(at, Vector3.up);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture old = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(camGo);
            }
            Debug.Log($"[WheelSkiShots] -> {path}");
        }
    }
}

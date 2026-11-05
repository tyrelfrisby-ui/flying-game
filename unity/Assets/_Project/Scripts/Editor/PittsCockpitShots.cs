using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Vr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// THE PITTS S-2B COCKPIT v2 FOR TY'S REVIEW (spec 2026-11-05): the aeroplane built the way the game builds it
    /// (AirframeBuilder, own model, rig posed from RigInputs, the live panels), photographed from both seat eyes and close
    /// up at every spec item, with the rig in the pose each item is about (canopy on its stop tab, jettisoned and slid past
    /// it, latch levers back, fuel OFF, prop bar released and aft, trim nose-up, throttle closed).
    ///
    /// And two MEASUREMENTS, because a picture of a pose is only as good as the pose (CLAUDE.md: measure the mechanism):
    ///   tab      with the rig drawing the canopy at Core's rest angle (CanopyStopTab, slide 4 in), the smallest gap between
    ///            the drawn Canopy_Frame and Canopy_Stop_Rubber (≈ 0 if the rig's hinge turns the way Core's
    ///            does and the tab is where Core thinks); slid the jettison's 6 in at the same angle, that gap must open up.
    ///   levers   how far each latch knob, the trim tip, the prop bar and the fuel handle move between their two poses.
    ///
    /// Shots: PITTS_SHOTS overrides the built-in list (';'-separated "name|seat:rear,yaw,pitch | node:Name,dx,dy,dz,dist|
    /// vfov|state"); PNGs to PITTS_OUT (default ~/Developer/agent-lanes/renders/cockpit-1105/claude-pitts-cockpit-v2).
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.PittsCockpitShots.Run (no -nographics)
    /// </summary>
    public static class PittsCockpitShots
    {
        private const string Id = "pitts-s2b-like";
        private const int W = 1600, H = 1200;

        // name | where | vfov | state.  where: seat:<id>,<yaw>,<pitch>  or  node:<Name>,<dx>,<dy>,<dz>,<dist> (camera
        // at the node's bounds centre + dist·(dx,dy,dz) normalised, aircraft frame: +x right, +y up, +z nose, looking at it).
        private static readonly string[] Default =
        {
            "rear-eye-forward|seat:rear,0,-8|90|",
            "rear-eye-left-wall|seat:rear,-55,-30|90|",
            "rear-eye-right-wall|seat:rear,60,-32|90|",
            "rear-eye-left-wall-moved|seat:rear,-55,-30|90|moved",
            "rear-eye-right-wall-fuel-off|seat:rear,60,-32|90|moved",
            "front-eye-forward|seat:front,0,-6|90|",
            "front-eye-left-latch|seat:front,-75,-25|80|",
            "front-eye-up-cutout|seat:front,0,38|90|",
            "rear-panel|node:SequenceCard_Face,0,0.15,-1,0.55|60|",
            "front-panel|node:FrontPanel_SequenceCard_Face,0,0.1,-1,0.45|60|",
            "whiskey-compass|node:WhiskeyCompass_Case,0.2,0.1,-1,0.22|45|",
            "windshield-trim|node:Windshield_Trim,-0.6,0.5,-1,1.0|55|",
            "fuel-tank|node:FuelTank_Barrel,0.2,-0.1,-1,0.75|60|",
            "latch-front-latched|node:Canopy_Latch_Knob_Front,0.6,0.5,-0.3,0.18|50|",
            "latch-front-unlatched|node:Canopy_Latch_Knob_Front,0.6,0.5,-0.3,0.18|50|latch",
            "latch-rear-latched|node:Canopy_Latch_Knob,0.6,0.5,-0.3,0.18|50|",
            "latch-rear-unlatched|node:Canopy_Latch_Knob,0.6,0.5,-0.3,0.18|50|latch",
            "latch-cable|node:Pitts_Release_Cable_Left,0.8,0.6,0,0.75|60|",
            "stop-tab-closed|node:Canopy_Stop_Rubber,0.5,0.6,-0.6,0.45|50|",
            "stop-tab-canopy-resting|node:Canopy_Stop_Rubber,0.6,0.5,-0.6,0.7|55|canopy=tab",
            "stop-tab-canopy-resting-wide|node:Canopy_Stop_Rubber,1,0.45,-0.35,2.6|50|canopy=tab",
            "jettisoned-past-tab|node:Canopy_Stop_Rubber,1,0.45,-0.35,2.6|50|canopy=jett",
            "wing-cutout-handle|node:UpperWing_Entry_Cutout_Trim,0.2,1,-0.5,1.1|55|",
            "fuel-valve-on|node:FuelValve_Rear,-0.6,0.5,-0.5,0.3|50|",
            "fuel-valve-off|node:FuelValve_Rear,-0.6,0.5,-0.5,0.3|50|moved",
            "prop-bar-locked|node:PropLever_Rear,0.8,0.4,-0.4,0.35|50|",
            "prop-bar-released-aft|node:PropLever_Rear,0.8,0.4,-0.4,0.35|50|moved",
            "trim-lever-neutral|node:TrimLever_Rear,0.9,0.5,0.2,0.6|55|",
            "trim-lever-nose-up|node:TrimLever_Rear,0.9,0.5,0.2,0.6|55|moved",
            "throttle-rear|node:Throttle_Rear,0.8,0.4,-0.5,0.3|50|",
            "throttle-front|node:Throttle_Front,0.8,0.4,-0.5,0.3|50|",
        };

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("PITTS_OUT")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                            "Developer", "agent-lanes", "renders", "cockpit-1105", "claude-pitts-cockpit-v2");
            string env = Environment.GetEnvironmentVariable("PITTS_SHOTS");
            string[] shots = string.IsNullOrEmpty(env) ? Default : env.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            int failures = 0, done = 0;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(outDir);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                if (!Measure()) failures++;
                foreach (string raw in shots)
                {
                    string[] f = raw.Trim().Split('|');
                    if (f.Length < 4) { Debug.LogError("[PittsShots] bad shot: " + raw); failures++; continue; }
                    try { if (One(f[0], f[1], F(f[2]), f[3], outDir)) done++; else failures++; }
                    catch (Exception e) { failures++; Debug.LogException(e); }
                }
            }
            catch (Exception e) { failures++; Debug.LogException(e); }
            finally
            {
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[PittsShots] {(failures == 0 ? "PASS" : "FAIL")}: {done} shots, {failures} failures -> {outDir}");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The ramp reading every shot starts from, then the state's changes.</summary>
        private static RigInputs Reading(string state)
        {
            var r = new RigInputs
            {
                Throttle = 0.6, PropLever = 1.0, Mixture = 1.0, Magneto = 3.0, Master = 1.0, Alternator = 1.0, Rpm = 2300,
                OilPressPsi = 70, OilTempF = 185, AirspeedMph = 120, AirspeedKias = 104, AltitudeFt = 2500, GLoad = 1.0,
                GMaxSeen = 4.5, GMinSeen = -1.5, ManifoldInHg = 24, FuelCentreFrac = 0.8, HeadingDeg = 30, FuelValve = 1.0,
                CanopyLock = 1.0,
            };
            CanopySpec spec = CanopySpec.For(Id);
            foreach (string t in state.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (t.Trim())
                {
                    case "moved":   // every Pitts lever away from rest: blade released and aft, valve OFF, trim nose-up, idle
                        r.PropLock = 1.0; r.PropLever = 0.0; r.FuelValve = 0.0; r.Trim = 1.0; r.Throttle = 0.0;
                        break;
                    case "latch": r.CanopyLatch = 1.0; r.CanopyLock = 0.0; break;
                    case "canopy=tab":
                        r.CanopyLatch = 1.0; r.CanopyLock = 0.0;
                        r.CanopySlide = spec.SlideM / spec.TrackM;
                        r.CanopyOpen = spec.StopTab!.RestAngleDeg(spec.SlideM) / spec.StopDeg;
                        break;
                    case "canopy=jett":
                        r.CanopyLatch = 1.0; r.CanopyLock = 0.0; r.CanopyJettison = 1.0;
                        r.CanopySlide = 1.0;
                        r.CanopyOpen = 140.0 / spec.StopDeg;
                        break;
                }
            }
            return r;
        }

        private static AirframeBuilder Build(GameObject root, string state, out RigInputs reading)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Id);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            reading = Reading(state);
            builder.SetRig(reading, 0.02f);
            var live = new CockpitLiveParts();
            live.Bind(root.transform, builder.HasOwnRig, builder.FindInModel, AirframeModels.PlacedOwnName(Id));
            live.Update(reading);
            return builder;
        }

        private static bool One(string name, string where, float vfov, string state, string outDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Id);
            if (cfg == null) { Debug.LogError($"[PittsShots] {Id}: no config"); return false; }
            var root = new GameObject("Aircraft");
            AirframeBuilder builder = Build(root, state, out _);
            Lights(out Color sky);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0f, -1.5f, 0f); ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.36f, 0.45f, 0.30f) };
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var camGo = new GameObject("Cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = sky;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 2000f; cam.fieldOfView = vfov;
            string[] w = where.Split(':');
            string[] a = w.Length > 1 ? w[1].Split(',') : Array.Empty<string>();
            string what;
            if (w[0] == "seat")
            {
                StationConfig seat = cfg.Loading.Seats.Find(s => s.Id == a[0]);
                if (seat == null) { Debug.LogError($"[PittsShots] no seat {a[0]}"); return false; }
                Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
                camGo.transform.SetPositionAndRotation(eye, Quaternion.Euler(-F(a[2]), F(a[1]), 0f));
                what = $"seat {a[0]} eye {eye.x:F3},{eye.y:F3},{eye.z:F3} yaw {a[1]} pitch {a[2]}";
            }
            else
            {
                Transform n = builder.FindInModel(a[0]);
                if (n == null) { Debug.LogError($"[PittsShots] {name}: no node {a[0]} in the model"); Clean(builder, root, camGo, gm); return false; }
                Vector3 c = Centre(n);
                Vector3 dir = new Vector3(F(a[1]), F(a[2]), F(a[3])).normalized;
                camGo.transform.position = c + dir * F(a[4]);
                camGo.transform.LookAt(c, Vector3.up);
                what = $"node {a[0]} centre {c.x:F3},{c.y:F3},{c.z:F3}";
            }
            string path = Path.Combine(outDir, name + ".png");
            Shoot(cam, path, W, H);
            Debug.Log($"[PittsShots] {name}: {what} vfov {vfov} state '{state}' -> {path}");
            Clean(builder, root, camGo, gm);
            return true;
        }

        private static void Clean(AirframeBuilder builder, GameObject root, GameObject camGo, Material gm)
        {
            builder.Clear();
            Object.DestroyImmediate(root); Object.DestroyImmediate(camGo); Object.DestroyImmediate(gm);
        }

        private static void Lights(out Color sky)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.51f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.32f);
            sky = new Color(0.55f, 0.70f, 0.88f);
        }

        private static Vector3 Centre(Transform n)
        {
            var rs = n.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return n.position;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        /// <summary>World-space vertices of every mesh under the node (glTFast splits primitives onto children).</summary>
        private static List<Vector3> Verts(Transform n)
        {
            var o = new List<Vector3>();
            if (n == null) return o;
            foreach (MeshFilter f in n.GetComponentsInChildren<MeshFilter>())
                if (f.sharedMesh != null) foreach (Vector3 v in f.sharedMesh.vertices) o.Add(f.transform.TransformPoint(v));
            return o;
        }

        private static float MinGap(List<Vector3> a, List<Vector3> b)
        {
            float best = float.PositiveInfinity;
            foreach (Vector3 p in a) foreach (Vector3 q in b) { float d = (p - q).sqrMagnitude; if (d < best) best = d; }
            return Mathf.Sqrt(best);
        }

        /// <summary>The two measurements (class note). False if the rig and Core disagree about where the tab is.</summary>
        private static bool Measure()
        {
            bool ok = true;
            float Gap(string state)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Aircraft");
                AirframeBuilder b = Build(root, state, out _);
                var frame = Verts(b.FindInModel("Canopy_Frame"));
                var rubber = Verts(b.FindInModel("Canopy_Stop_Rubber"));
                float g = frame.Count > 0 && rubber.Count > 0 ? MinGap(frame, rubber) : float.NaN;
                Debug.Log($"[PittsShots] tab gap '{state}': {g * 1000:F1} mm ({frame.Count} frame, {rubber.Count} rubber vertices)");
                b.Clear(); Object.DestroyImmediate(root);
                return g;
            }
            float rest = Gap("canopy=tab");
            // Jettisoned: the same swing, slid the extra 2 in.
            CanopySpec spec = CanopySpec.For(Id);
            string jet = "canopy=tab";
            float past;
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Aircraft");
                AirframeBuilder b = Build(root, jet, out RigInputs r);
                r.CanopySlide = 1.0;
                b.SetRig(r, 0.02f);
                var frame = Verts(b.FindInModel("Canopy_Frame"));
                var rubber = Verts(b.FindInModel("Canopy_Stop_Rubber"));
                past = frame.Count > 0 && rubber.Count > 0 ? MinGap(frame, rubber) : float.NaN;
                b.Clear(); Object.DestroyImmediate(root);
            }
            Debug.Log($"[PittsShots] TAB: at Core's rest angle {spec.StopTab!.RestAngleDeg(spec.SlideM):F1}° slid 4 in the drawn frame is {rest * 1000:F1} mm from the rubber's surface points " +
                      $"(tube 4.8 + rubber 6 mm radii: touching reads ≲ 12 mm); slid 6 in at the same angle {past * 1000:F1} mm");
            if (float.IsNaN(rest) || rest > 0.020f) { Debug.LogError("[PittsShots] the rig's canopy at Core's rest angle is not on the tab"); ok = false; }
            if (!(past > rest + 0.010f)) { Debug.LogError("[PittsShots] slid the jettison's 6 in, the frame is no further from the tab"); ok = false; }

            // The levers between their poses.
            foreach ((string node, string state) in new[] { ("Canopy_Latch_Knob_Front", "latch"), ("Canopy_Latch_Knob", "latch"),
                                                             ("TrimLever_Rear", "moved"), ("PropLever_Rear", "moved"), ("FuelValve_Rear", "moved"),
                                                             ("Throttle_Rear", "moved"), ("Throttle_Front", "moved") })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Aircraft");
                AirframeBuilder b = Build(root, "", out _);
                List<Vector3> v0 = Verts(b.FindInModel(node));
                b.Clear(); Object.DestroyImmediate(root);
                root = new GameObject("Aircraft");
                b = Build(root, state, out _);
                List<Vector3> v1 = Verts(b.FindInModel(node));
                b.Clear(); Object.DestroyImmediate(root);
                float most = 0f;
                for (int i = 0; i < Mathf.Min(v0.Count, v1.Count); i++) most = Mathf.Max(most, (v1[i] - v0[i]).magnitude);
                Vector3 c0 = Mean(v0), c1 = Mean(v1);
                Debug.Log($"[PittsShots] lever {node} '{state}': farthest vertex moved {most * 1000:F0} mm; centroid moved " +
                          $"({(c1 - c0).x * 1000:F0}, {(c1 - c0).y * 1000:F0}, {(c1 - c0).z * 1000:F0}) mm (+x right, +y up, +z nose)");
                if (v0.Count == 0) { Debug.LogError($"[PittsShots] no {node} in the model"); ok = false; }
                else if (most < 0.005f) { Debug.LogError($"[PittsShots] {node} does not move for '{state}'"); ok = false; }
            }
            return ok;
        }

        private static Vector3 Mean(List<Vector3> v)
        {
            Vector3 s = Vector3.zero;
            foreach (Vector3 p in v) s += p;
            return v.Count > 0 ? s / v.Count : s;
        }

        private static void Shoot(Camera cam, string path, int w, int h)
        {
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
            finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

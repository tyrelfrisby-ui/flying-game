using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// THE GHOST AEROPLANE ON THE LANDING PAGE, PINNED (Ty 2026-11-09, "weird ghost plane that was rotating of axis to
    /// it about 90 degrees"; 2026-11-11, with a video: "The landing page ghost is still there").
    ///
    /// The planar water reflection is ONE texture, rendered for the MAIN camera, and Water.shader reads it in SCREEN
    /// SPACE. Any second camera that draws water therefore paints the main camera's mirror image at its own screen
    /// coordinates. <see cref="WaterReflection.SuppressedForSplit"/> is that same fact written down for the split
    /// screen; <see cref="LandingPreview"/> copies <c>Camera.main.cullingMask</c>, so it draws the water too, and had
    /// no such guard — which is what put an aeroplane in the lake at an attitude belonging to another camera's view,
    /// swinging about as the previewed aeroplane flew its circle. This is the SECOND time this class of defect shipped
    /// (the first was the mirror's own viewport, <see cref="WaterMirrorViewportCheck"/>), hence a check of its own.
    ///
    /// On the real island world, the real <see cref="WaterReflection"/> and the real <see cref="LandingPreview"/>
    /// component driven by hand (CLAUDE.md: "An editor batch tool has to drive the component itself"): a Pitts 10 m
    /// over the first float lane, the main camera looking at it from one bearing with its mirror rendered and
    /// <c>_ReflOn</c> = 1 exactly as in a live frame, and the preview camera looking from its own fixed bearing — the
    /// difference between those two views is what makes the ghost visible and is why both are set up.
    ///
    /// THE EVIDENCE IS THE PICTURE. <c>_ReflectionTex</c> is replaced with solid magenta before the preview draws, so
    /// any water that samples the mirror comes out unmistakably magenta, whatever the mirror actually holds (<see
    /// cref="Marker"/> says why magenta and not red):
    ///  1. the frame the real path draws must have NO such texel — the preview's water does not read the mirror;
    ///  2. the CONTROL — the same camera re-rendered the pre-fix way, with the globals left as the main camera set
    ///     them — must be full of them, or there is no water in the preview's frame and this check is not watching the
    ///     defect it guards (the trap in AGENT-LOG's "a renderer that lies quietly");
    ///  3. the globals must be exactly as they were once the preview's frame is over: the page is drawn over a live
    ///     world, and the main camera's own reflection is not the menu's to change;
    ///  4. the same for the thumbnail pipeline (<see cref="LandingThumbnails.Render"/>), which renders the preview
    ///     camera and writes it to DISK, where a ghost would stay for good — while the launch-view card, which is the
    ///     main camera's own picture, must keep its reflection.
    /// <c>Camera.onPreRender</c> adds the exact switch values each camera started drawing with, for the report.
    ///
    /// <c>Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.LandingPreviewMirrorCheck.Run</c> (no
    /// <c>-nographics</c>: it needs a GPU). Writes build/landing-preview-mirror/ (PNGs and report.txt). Exit 0 = pass.
    /// </summary>
    public static class LandingPreviewMirrorCheck
    {
        /// <summary>Every render observed while watching: the camera's name and the water's two screen-space switches
        /// as that camera started drawing.</summary>
        private static readonly List<(string cam, float reflOn, float depthOff)> Seen = new();

        private const string PreviewCam = "LandingPreviewCamera";

        /// <summary>The colour the mirror is replaced with. MAGENTA, not red: the first run of this check used red and
        /// counted 4,527 "mirrored" texels in a frame that was correct — the Pitts' own paint. Nothing in a daylight
        /// island frame is magenta (sky and water are blue, foliage green, ground tan, the fleet's paint red, white,
        /// yellow and blue), and white does not qualify because its green is high.</summary>
        private static readonly Color32 Marker = new Color32(255, 0, 255, 255);
        private static bool Mirrored(Color32 c) => c.r > 150 && c.b > 150 && c.g < 70;
        /// <summary>A frame may hold this fraction of marked texels and still count as clean — 1/1000. The control
        /// below measures about 70 % of its frame, so there is a factor of 700 between pass and fail: this is slack
        /// for a stray texel at a silhouette's edge, not a tolerance for a reflection.</summary>
        private const int CleanDivisor = 1000;

        private static object Call(object o, string name) =>
            o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, null);

        private static FieldInfo F(string name) => typeof(LandingPreview).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Run()
        {
            int fails = 0;
            var log = new StringBuilder();
            void Fail(string m) { fails++; log.AppendLine("FAIL " + m); Debug.LogError("[LandingPreviewMirror] FAIL " + m); }
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/landing-preview-mirror"));
            Camera.CameraCallback watch = c => Seen.Add((c == null ? "?" : c.name, Shader.GetGlobalFloat("_ReflOn"), Shader.GetGlobalFloat("_WaterSceneDepthOff")));
            try
            {
                Directory.CreateDirectory(output);
                if (SystemInfo.graphicsDeviceType.ToString().Contains("Null"))
                    throw new InvalidOperationException("needs a GPU: run without -nographics");

                // ---- the world, and a Pitts 10 m over the first float lane -------------------------------------
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WaterCalm", 1);
                WorldBuilder.BuildAll();
                SunsetLighting.ConfigureDayAtmosphere("Balanced", 0.000052f, 850f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);

                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
                var root = new GameObject("Pitts");
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                var state = CrashScenario.PittsWaterStart(cfg);
                double water = WorldTerrain.Active.WaterSurfaceAt(state.Position.X, state.Position.Y, -state.Position.Z) ?? -state.Position.Z;
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WaterReflection.AircraftLayer;
                var sim = new Vec3(state.Position.X, state.Position.Y, -(water + 10));
                root.transform.SetPositionAndRotation(CoordinateMap.ToUnity(sim), CoordinateMap.ToUnity(state.Attitude));
                driver.AdoptSim(new SimLoop(new Aircraft(cfg, new RigidBodyState(sim, state.Attitude, state.Velocity, Vec3.Zero), ControlDeflections.Neutral)));
                Vector3 ac = root.transform.position;
                float planeY = (float)water;

                // ---- the main camera, with its mirror rendered, as in a live frame -----------------------------
                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
                cam.nearClipPlane = .3f; cam.farClipPlane = 20000; cam.fieldOfView = 60;
                var reflection = cam.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; reflection.ReflectScenery = false;
                Call(reflection, "Awake");
                var mainRt = new RenderTexture(640, 360, 24); cam.targetTexture = mainRt;
                // A bearing of its own, well away from the preview's (14° down, 210°): the ghost IS the difference
                // between the two views, so a check that filmed both from the same place would prove nothing.
                cam.transform.position = ac + new Vector3(70f, 25f, 0f);
                cam.transform.LookAt(new Vector3(ac.x, planeY + 2f, ac.z));
                Call(reflection, "OnPreCull");
                float reflOnLive = Shader.GetGlobalFloat("_ReflOn");
                log.AppendLine($"main camera: _ReflOn {reflOnLive}, _ReflPlaneY {Shader.GetGlobalFloat("_ReflPlaneY"):F2} (lake surface {planeY:F2} m)");
                if (reflOnLive < 0.5f) Fail("the main camera did not reflect, so the ghost's precondition is not set up and nothing below is a test");
                cam.Render();
                Save(mainRt, Path.Combine(output, "main-view.png"));

                // The mirror becomes solid magenta: from here on, water that samples it is unmistakable in the picture.
                var marker = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "mirror marker" };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = Marker;
                marker.SetPixels32(px); marker.Apply();
                Shader.SetGlobalTexture("_ReflectionTex", marker);

                // ---- the landing preview, driven by hand -------------------------------------------------------
                SessionSettings.MenuOpen = true;
                XrHeadset.ForceActiveForTesting = false;
                LandingPreview.HoldAtStart = true;       // stand at the spawn: no display circle moving the aeroplane
                var previewRoot = new GameObject("LandingPreview");
                var preview = previewRoot.AddComponent<LandingPreview>();
                preview.Driver = driver;
                Call(preview, "Awake");
                // Tell it the flow has not changed, so Sync does not re-spawn the aeroplane at the flow's own airport:
                // this check wants it over water, the only place a ghost in the water can appear.
                F("_seenVersion").SetValue(preview, LandingSession.Flow.Version);

                Camera.onPreRender += watch;
                Call(preview, "LateUpdate");             // builds the camera and its target
                var previewCam = (Camera)F("_cam").GetValue(preview);
                if (previewCam == null) throw new InvalidOperationException("the landing preview built no camera");
                log.AppendLine($"preview camera: culling mask 0x{previewCam.cullingMask:x8}, water layer {WaterReflection.WaterLayer} "
                               + (((1 << WaterReflection.WaterLayer) & previewCam.cullingMask) != 0 ? "IS drawn by it" : "is not drawn by it"));
                Seen.Clear();
                Call(preview, "LateUpdate");             // the frame this check reads
                var shown = (RenderTexture)typeof(LandingPreview).GetProperty("Texture").GetValue(preview);
                if (shown == null) throw new InvalidOperationException("the landing preview rendered no picture");
                int markedFixed = Count(shown, Path.Combine(output, "preview-fixed.png"));

                // 3: the main camera's frame was handed back exactly as it was found.
                if (Shader.GetGlobalFloat("_ReflOn") != reflOnLive || Shader.GetGlobalFloat("_WaterSceneDepthOff") != 0f)
                    Fail($"the preview's frame left _ReflOn {Shader.GetGlobalFloat("_ReflOn")} / _WaterSceneDepthOff {Shader.GetGlobalFloat("_WaterSceneDepthOff")} behind: the menu changed the main camera's water");
                Report(log, Fail, PreviewCam, 0f, 1f);

                // 4: the thumbnail pipeline, which writes what it renders to disk.
                Seen.Clear();
                var card = LandingThumbnails.Render(previewCam);
                int markedCard = card == null ? -1 : Count(card.GetPixels32());
                int cardTexels = card == null ? 1 : card.width * card.height;
                if (card != null) Object.DestroyImmediate(card);
                Report(log, Fail, PreviewCam, 0f, 1f);
                log.AppendLine($"the aircraft card: {markedCard} marked texels of {cardTexels}");
                if (markedCard < 0 || markedCard * CleanDivisor > cardTexels)
                    Fail($"the aircraft card captured {markedCard} texels of the main camera's mirror: the ghost would be written to disk and kept");
                Seen.Clear();
                var launchCard = LandingThumbnails.Render(cam);
                if (launchCard != null) Object.DestroyImmediate(launchCard);
                Report(log, Fail, "Main Camera", reflOnLive, 0f);

                // 2: the control — the pre-fix path, emulated exactly, globals as the main camera left them.
                Camera.onPreRender -= watch;
                var control = new RenderTexture(shown.width, shown.height, 24);
                RenderTexture was = previewCam.targetTexture;
                previewCam.targetTexture = control;
                previewCam.Render();                      // no guard: this is the defect itself
                previewCam.targetTexture = was;
                int markedControl = Count(control, Path.Combine(output, "preview-control-prefix-emulated.png"));
                int total = shown.width * shown.height;
                log.AppendLine($"marked texels: control {markedControl} of {total} ({100f * markedControl / total:F2} %), the real path {markedFixed} ({100f * markedFixed / total:F2} %)");
                if (markedControl * 500 < total)
                    Fail($"the control drew only {markedControl} marked texels of {total}: no water in the preview's frame samples the mirror, so this check is not watching the defect it guards");
                if (markedFixed * CleanDivisor > total)
                    Fail($"{markedFixed} texels of the preview's own frame came from the main camera's mirror: the ghost aeroplane is still there");

                builder.Clear();
            }
            catch (Exception e) { fails++; log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                Camera.onPreRender -= watch;
                log.AppendLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
                try { File.WriteAllText(Path.Combine(output, "report.txt"), log.ToString()); } catch (Exception) { }
                Debug.Log("[LandingPreviewMirror]\n" + log);
                EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }

        /// <summary>Log every observed render, and fail for each render of <paramref name="camName"/> that did not
        /// start with the switches it must have. A render nobody observed is not a failure here: the picture is the
        /// evidence, and this is the instrument that says why.</summary>
        private static void Report(StringBuilder log, Action<string> fail, string camName, float reflOn, float depthOff)
        {
            foreach (var (c, refl, depth) in Seen)
            {
                log.AppendLine($"  render of {c}: _ReflOn {refl}, _WaterSceneDepthOff {depth}");
                if (c != camName) continue;
                if (refl != reflOn || depth != depthOff)
                    fail($"{c} rendered with _ReflOn {refl} / _WaterSceneDepthOff {depth}, wanted {reflOn} / {depthOff}"
                         + (refl > 0.5f && reflOn == 0f ? ": it is sampling the main camera's mirror in its own screen space — the ghost aeroplane" : ""));
            }
            Seen.Clear();
        }

        /// <summary>How many texels came from the marker mirror, saving the frame when given a path.</summary>
        private static int Count(RenderTexture rt, string png)
        {
            var tex = Read(rt);
            int n = Count(tex.GetPixels32());
            if (png != null) File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return n;
        }

        private static int Count(Color32[] px)
        {
            int n = 0;
            foreach (var c in px) if (Mirrored(c)) n++;
            return n;
        }

        private static void Save(RenderTexture rt, string png)
        {
            var tex = Read(rt);
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
    }
}

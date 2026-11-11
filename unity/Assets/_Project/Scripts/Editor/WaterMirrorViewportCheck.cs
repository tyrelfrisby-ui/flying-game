using System;
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
    /// t961 / t983 / t989 (state/water-image-t989-diagnosis.md §4 R1): does the water's mirror image sit where the water samples
    /// it, in landscape AND in portrait? The real island world and the real WaterReflection, the Pitts 10 m over the first float
    /// lane, the mirror drawing the aircraft only (its silhouette is the marker). For a full-rect main camera and an iPhone 17 Pro
    /// portrait one (rect (0, f, 1, 1−f), f = 0.37, ScreenLayout's tray), and at three heights:
    ///
    /// 1 FT (2026-11-10). Ty, with a J-3 on a lake at 1 KT and 1 FT: "The reflection is still above the airplane so that has not
    /// been nixed". "Still" because this check and the fix it guards (WaterReflection's rect reset) were written on 2026-11-07
    /// and then sat on an ORPHANED lane — lane/claude-q007-pitts-spin-t918, in no shipping lane — so the build he flew never had
    /// either. An aeroplane AT the water is the most informative case there is: its mirror image must be essentially on top of it,
    /// so any displacement at all is the whole error, and 0.3 m is now the third height below for exactly that reason.
    /// What his screenshot measures (1206 x 2622, iPhone 16/17 Pro portrait, build of 2026-11-10):
    ///   • the 3D view ends at row 1665 of 2622 — ScreenLayout's tray is the bottom 957 px, so f = 957/2622 = 0.365 (the 0.37
    ///     this file already assumed, measured rather than assumed);
    ///   • the aeroplane sits at viewport v ≈ 0.43, and the reflection is drawn ≈ 363 px higher than the aeroplane's own waterline;
    ///   • the rect bug predicts, with NO free parameter, a lift of f(1 − v)·H = 0.365 · 0.57 · 1665 = 353 px. 3 % out.
    /// The alternative — a mirror plane at the wrong height — was measured and REJECTED: it would need planeY about 2.7 m above the
    /// lake, there is no code path to it (planeY comes straight from FloatHydro.WaterSurfaceAt, up-positive metres = Unity Y), and
    /// Water.shader only applies the reflection where abs(wp.y − _ReflPlaneY) < 1.5, so a plane 2.7 m out would have switched the
    /// reflection off altogether instead of moving it. Not confirmed from the screenshot, and this check is what settles it: the
    /// bug also COMPRESSES the mirror by (1 − f), and a 190 px silhouette read through a HUD text mask cannot resolve 70 px of
    /// squash. The centroid assertions below do not care — they measure the placement directly.
    /// The seat (pilot) view needs no separate case: it is the SAME camera, so it carries the same rect and the same fix.
    ///  - the mirror camera's pixelRect must be its whole target;
    ///  - the reflected aircraft's centroid in the mirror target must be where the water reads it: the MAIN camera's viewport
    ///    position of the aircraft mirrored about the water plane (Water.shader samples the target by the main viewport's screen
    ///    position).
    /// Control: the same frame re-rendered with the mirror given the main camera's rect (the pre-fix behaviour, emulated)
    /// must show the displacement f + v(1 − f), or the check is not seeing what it claims.
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.WaterMirrorViewportCheck.Run   (no -nographics)
    /// Writes build/water-mirror-viewport/ (PNGs of the view and the mirror target, report.txt).
    /// </summary>
    public static class WaterMirrorViewportCheck
    {
        private const float Tray = 0.37f;     // iPhone 17 Pro portrait tray share (diagnosis §1 table)
        private const float Tolerance = 0.04f; // viewport units: centroid of a silhouette vs the projection of its bounds' centre

        private static object Call(object o, string name, params object[] args) =>
            o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, args);

        public static void Run()
        {
            int fails = 0;
            var log = new StringBuilder();
            void Fail(string m) { fails++; log.AppendLine("FAIL " + m); Debug.LogError("[WaterMirrorViewport] FAIL " + m); }
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/water-mirror-viewport"));
            try
            {
                Directory.CreateDirectory(output);
                if (SystemInfo.graphicsDeviceType.ToString().Contains("Null")) throw new InvalidOperationException("needs a GPU: run without -nographics");
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
                Vec3 along = state.Attitude.Rotate(new Vec3(1, 0, 0)); along = new Vec3(along.X, along.Y, 0) / Math.Sqrt(along.X * along.X + along.Y * along.Y);

                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
                cam.nearClipPlane = .3f; cam.farClipPlane = 20000; cam.fieldOfView = 60;
                var reflection = cam.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; reflection.ReflectScenery = false;
                Call(reflection, "Awake");
                const int W = 603, H = 1311;   // iPhone 17 Pro portrait pixels / 2
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                var reflCam = (Camera)typeof(WaterReflection).GetField("_refl", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reflection);

                foreach (float heightM in new[] { 10f, 4f, 0.3048f })   // 0.3048 m = Ty's 1 FT, 2026-11-10
                foreach (bool portrait in new[] { false, true })
                {
                    Vec3 sim = new Vec3(state.Position.X, state.Position.Y, -(water + heightM));
                    root.transform.SetPositionAndRotation(CoordinateMap.ToUnity(sim), CoordinateMap.ToUnity(state.Attitude));
                    driver.AdoptSim(new SimLoop(new Aircraft(cfg, new RigidBodyState(sim, state.Attitude, state.Velocity, Vec3.Zero), ControlDeflections.Neutral)));
                    float planeY = (float)water;
                    // Chase from behind and above, looking between the aircraft and its image, so both are in view.
                    Vector3 ac = root.transform.position;
                    Vector3 back = CoordinateMap.ToUnity(new Vec3(state.Position.X - 30 * along.X, state.Position.Y - 30 * along.Y, -(water + heightM + 6)));
                    cam.transform.position = back;
                    cam.transform.LookAt(new Vector3(ac.x, planeY + 0.2f * heightM, ac.z));
                    cam.rect = portrait ? new Rect(0, Tray, 1, 1 - Tray) : new Rect(0, 0, 1, 1);
                    string tag = $"{(portrait ? "portrait" : "full")}-{heightM:0.##}m";

                    // The aircraft mirrored about the plane, where the water will read it (main viewport, 0..1 in that rect).
                    Bounds b = new Bounds(ac, Vector3.zero);
                    foreach (var r in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    Vector3 mirrored = new Vector3(b.center.x, 2 * planeY - b.center.y, b.center.z);
                    Vector3 expect = cam.WorldToViewportPoint(mirrored);

                    Call(reflection, "OnPreCull");
                    cam.Render();
                    Save(rt, Path.Combine(output, $"view-{tag}.png"));
                    var refl = (RenderTexture)typeof(WaterReflection).GetField("_rt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reflection);
                    Rect pr = reflCam.pixelRect;
                    bool fills = Mathf.Approximately(pr.x, 0) && Mathf.Approximately(pr.y, 0) && (int)pr.width == refl.width && (int)pr.height == refl.height;
                    var (cu, cv, px) = Centroid(refl, Path.Combine(output, $"mirror-{tag}.png"));
                    log.AppendLine($"{tag}: plane {planeY:F2}, mirror pixelRect {pr} in {refl.width}x{refl.height}; expected image at ({expect.x:F3}, {expect.y:F3}), found ({cu:F3}, {cv:F3}) from {px} px; Δv {cv - expect.y:+0.000;-0.000}");
                    if (!fills) Fail($"{tag}: mirror pixelRect {pr} does not fill its {refl.width}x{refl.height} target");
                    if (px < 50) Fail($"{tag}: only {px} aircraft pixels in the mirror: the marker is not in view");
                    else if (Mathf.Abs(cu - expect.x) > Tolerance || Mathf.Abs(cv - expect.y) > Tolerance)
                        Fail($"{tag}: reflected aircraft at ({cu:F3}, {cv:F3}), water reads it at ({expect.x:F3}, {expect.y:F3})");

                    if (portrait)
                    {
                        // Control: the pre-fix mirror (rect inherited from the main camera), same frame.
                        reflCam.rect = cam.rect;
                        bool inv = GL.invertCulling; GL.invertCulling = !inv;
                        Shader.SetGlobalFloat("_ReflectionRendering", 1);
                        try { reflCam.Render(); } finally { GL.invertCulling = inv; Shader.SetGlobalFloat("_ReflectionRendering", 0); }
                        var (ou, ov, opx) = Centroid(refl, Path.Combine(output, $"mirror-{tag}-prefix-emulated.png"));
                        float predicted = Tray + expect.y * (1 - Tray);
                        log.AppendLine($"  control (pre-fix rect): found ({ou:F3}, {ov:F3}); predicted v f + v(1−f) = {predicted:F3}");
                        if (opx < 50 || Mathf.Abs(ov - predicted) > Tolerance) Fail($"{tag}: control did not show the pre-fix displacement (v {ov:F3}, predicted {predicted:F3})");
                        reflCam.rect = new Rect(0, 0, 1, 1);
                    }
                }
                builder.Clear();
            }
            catch (Exception e) { fails++; log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                log.AppendLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
                File.WriteAllText(Path.Combine(output, "report.txt"), log.ToString());
                Debug.Log("[WaterMirrorViewport]\n" + log);
                EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }

        /// <summary>Centroid (texture uv, 0..1) of the texels the aircraft drew into the mirror (alpha above ½; the mirror
        /// clears to alpha 0), and how many there are.</summary>
        private static (float u, float v, int n) Centroid(RenderTexture rt, string png)
        {
            var tex = Read(rt);
            double su = 0, sv = 0; int n = 0;
            var px = tex.GetPixels32();
            for (int y = 0; y < rt.height; y++)
                for (int x = 0; x < rt.width; x++)
                    if (px[y * rt.width + x].a > 127) { su += (x + 0.5) / rt.width; sv += (y + 0.5) / rt.height; n++; }
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return n == 0 ? (float.NaN, float.NaN, 0) : ((float)(su / n), (float)(sv / n), n);
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

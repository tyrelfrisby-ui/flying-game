using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Clouds;
using FlyingGame.Core;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityWakeField = Aero.Wake.WakeField;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Renders of the cloud playground and of the wispy layer reacting to a real wake (docs/WAKE-VORTEX.md,
    /// docs/CLOUDS-WAKE-VISUALS.md). The world is the game's own island (WorldBuilder.BuildAll, which attaches the
    /// CloudPlayground); the wake is the shared Core model fed by a kinematic aeroplane exactly as a recorded ship
    /// feeds it (load factor → lift → Γ), so "no lift, no vortex" is shown, not drawn.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CloudWakeRender.Render -outDir DIR
    /// (no -nographics: it renders and blits).
    /// </summary>
    public static class CloudWakeRender
    {
        static string _out;
        static Camera _cam;
        static RenderTexture _rt;

        [MenuItem("FlyingGame/Render Clouds + Wake")]
        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(args, "-outDir");
            _out = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/clouds-wake"));
            Directory.CreateDirectory(_out);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            WorldTerrain.Active = new WorldTerrain();
            WorldClock.TimeS = 20;
            VortexWake.Clear();
            Atmosphere.SteadyWind = Vec3.Zero;
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            SunsetLighting.ConfigureDayAtmosphere("Balanced", .000052f, 850f);

            // As WorldRender: the training volumes (translucent boxes that write depth) are not scenery.
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool hide = r.sharedMaterial != null && r.sharedMaterial.shader.name == "FlyingGame/Glass";
                for (Transform t = r.transform; t != null && !hide; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) hide = true;
                if (hide) { r.enabled = false; Debug.Log($"[CloudWakeRender] hidden: {r.name} ({r.sharedMaterial?.shader.name}) at {r.bounds.center} size {r.bounds.size}"); }
            }
            var camGo = new GameObject("RenderCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            _cam.depthTextureMode = DepthTextureMode.Depth;
            _cam.nearClipPlane = 1f; _cam.farClipPlane = 60000f;
            _rt = new RenderTexture(1600, 1000, 24);
            _cam.targetTexture = _rt;

            var report = new System.Text.StringBuilder();
            CloudPlayground cp = Object.FindFirstObjectByType<CloudPlayground>();
            if (cp == null) { Debug.LogError("[CloudWakeRender] no CloudPlayground in the island world"); return; }
            int banks = 0;
            foreach (Transform t in cp.transform) if (t.name == "Cloud bank") banks++;
            report.AppendLine($"banks {banks}");

            // ---- the playground
            float t0 = 0f;
            void Shot(string name, Vector3 eye, Vector3 look, float fov)
            {
                _cam.fieldOfView = fov;
                _cam.transform.position = eye;
                _cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                for (int i = 0; i < 3; i++) { t0 += 0.1f; cp.StepForTesting(_cam, t0, 0.1f); }
                Save(name);
            }
            Shot("clouds_island_from_sea_1500m", new Vector3(-21000, 1500, -12000), new Vector3(0, 1200, 0), 55);
            Shot("clouds_island_from_high_4500m", new Vector3(-17000, 4500, -17000), new Vector3(0, 600, 0), 60);
            Shot("clouds_low_band_canyon_600m", RingPoint(16500, 200, 600), RingPoint(16500, 240, 700), 70);
            Shot("clouds_mid_band_1250m", RingPoint(11000, 95, 1250), RingPoint(11000, 130, 1300), 70);
            Shot("clouds_high_band_2300m", RingPoint(6000, 110, 2300), RingPoint(6000, 160, 2350), 70);
            Shot("wisps_layers_from_1000m", new Vector3(0, 1000, -19500), new Vector3(0, 700, -12000), 70);

            // ---- the wake on a wispy sheet: three passes through the 650 m layer over the sea, south of the island
            float layer = CloudPlayground.LayerHeight(0);
            // Over the sea south of the island, at the middle of a patch of wisps (the layers are broken, WispCover).
            Vector3 centreU = new Vector3(0, layer, -19000);
            float best = -1f;
            for (float x = -4000; x <= 4000; x += 100)
                for (float z = -21000; z <= -17000; z += 100)
                {
                    float c = CloudPlayground.WispCover(new Vector2(x, z)) + CloudPlayground.WispCover(new Vector2(x + 250, z)) + CloudPlayground.WispCover(new Vector2(x - 250, z))
                        + CloudPlayground.WispCover(new Vector2(x, z + 250)) + CloudPlayground.WispCover(new Vector2(x, z - 250));
                    if (c > best) { best = c; centreU = new Vector3(x, layer, z); }
                }
            report.AppendLine($"wake case centre {centreU} cover {best / 5:F2}");
            report.AppendLine(WakeCase(ref cp, "wake_wisps_turn_30deg_n1.15", centreU, 30f, 30.0, 160.0, 1.0 / System.Math.Cos(30 * System.Math.PI / 180)));
            report.AppendLine(WakeCase(ref cp, "wake_wisps_zero_lift_pass_n0", centreU, 30f, 30.0, 160.0, 0.0));
            report.AppendLine(WakeCase(ref cp, "wake_wisps_hard_turn_60deg_n2", centreU, 60f, 30.0, 53.0, 2.0));
            File.WriteAllText(Path.Combine(_out, "cloud-wake-render-report.txt"), report.ToString());
            Debug.Log("[CloudWakeRender] done\n" + report);
            VortexWake.Clear();
        }

        static Vector3 RingPoint(float radius, float deg, float y) =>
            new(Mathf.Cos(deg * Mathf.Deg2Rad) * radius, y, Mathf.Sin(deg * Mathf.Deg2Rad) * radius);

        /// <summary>A C172-sized aeroplane circles 2 m above the layer at <paramref name="bankDeg"/> and load factor
        /// <paramref name="nz"/> for 26 s; the sheet is advanced through the real wake at 15 Hz the whole time; then a
        /// top-down shot from 200 m above, the wake sheet itself, and the circulation it left.</summary>
        static string WakeCase(ref CloudPlayground cp, string name, Vector3 centreU, float bankDeg, double v, double radius, double nz)
        {
            // A fresh sheet for each case.
            Transform parent = cp.transform.parent;
            Object.DestroyImmediate(cp.gameObject);
            cp = CloudPlayground.Attach(parent);
            VortexWake.Clear();
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("c172-like");
            double span = FlyingGame.Core.Aero.AeroModel.WingSpan(cfg), weight = cfg.Mass.MassKg * Atmosphere.GravityMs2;
            WakeSource src = VortexWake.CreateSource(name, span);
            Vec3 c = CoordinateMap.ToSim(centreU) + new Vec3(0, 0, -2.0);   // 2 m above the sheet (NED z up is −)
            double omega = v / radius, dt = 1.0 / 30.0;
            float now = 1000f;
            _cam.fieldOfView = 95f;
            _cam.transform.position = centreU + Vector3.up * 200f;
            _cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Vec3 pos = c; Quat att = Quat.Identity;
            double phi = bankDeg * System.Math.PI / 180;
            for (int i = 0; i < 26 * 30; i++)
            {
                double a = omega * i * dt;
                pos = c + new Vec3(System.Math.Cos(a) * radius, System.Math.Sin(a) * radius, 0);
                double psi = a + System.Math.PI / 2;   // tangent, turning right (clockwise seen from above in NED)
                Vec3 vel = new(System.Math.Cos(psi) * v, System.Math.Sin(psi) * v, 0);
                // Bank into the turn: yaw ψ about z, then roll φ about x (NED: positive roll = right wing down, a right
                // turn about +z).
                Quat qy = new(0, 0, System.Math.Sin(psi / 2), System.Math.Cos(psi / 2));
                Quat qx = new(System.Math.Sin(phi / 2), 0, 0, System.Math.Cos(phi / 2));
                att = Quat.Multiply(qy, qx);
                src.ShedFromLoadFactor(dt, pos, att, vel, nz, weight, Atmosphere.DensityAt(pos));
                VortexWake.Advance(dt);
                now += (float)dt;
                cp.StepForTesting(_cam, now, (float)dt);
            }
            // The aeroplane where it is now.
            var plane = new GameObject("Wake leader");
            try { new AirframeBuilder().Build(plane.transform, cfg); } catch (System.Exception e) { Debug.LogWarning(e.Message); }
            plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(pos), CoordinateMap.ToUnity(att));
            Save(name);
            Object.DestroyImmediate(plane);
            SaveRt(cp.WakeTextureForTesting, name + "_wake_sheet", true);
            var segs = new System.Collections.Generic.List<VortexSegment>();
            int n = VortexWake.GetActiveSegments(segs);
            double gmax = 0; foreach (var s in segs) gmax = System.Math.Max(gmax, System.Math.Abs(s.Gamma));
            return $"{name}: head Γ {src.Head.Gamma:F2} m²/s, {n} live segments, max |Γ| {gmax:F2}, texture segments {UnityWakeField.LastTextureSegments}/{UnityWakeField.LastRegionSegments}";
        }

        static void Save(string name)
        {
            _cam.Render();
            SaveRt(_rt, name, false);
        }

        /// <summary>PNG of a render target. <paramref name="field"/>: the wake sheet, drawn as R = |horizontal
        /// velocity| / 2 m/s, G = |vertical| / 2 m/s, B = |ω| / 4 per s.</summary>
        static void SaveRt(RenderTexture rt, string name, bool field)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, field ? TextureFormat.RGBAFloat : TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (field)
            {
                var px = tex.GetPixels();
                var o = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    float h = Mathf.Sqrt(c.r * c.r + c.g * c.g);
                    px[i] = new Color(Mathf.Clamp01(h / 2f), Mathf.Clamp01(Mathf.Abs(c.a) / 2f), Mathf.Clamp01(c.b / 4f));
                }
                o.SetPixels(px); o.Apply();
                File.WriteAllBytes(Path.Combine(_out, name + ".png"), o.EncodeToPNG());
                Object.DestroyImmediate(o);
            }
            else File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[CloudWakeRender] {name}.png");
        }
    }
}

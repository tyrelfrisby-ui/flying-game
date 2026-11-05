using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Clouds;
using FlyingGame.Core;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
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
            report.AppendLine(SeamProbe());
            if (System.Array.IndexOf(args, "-seamOnly") >= 0) { File.WriteAllText(Path.Combine(_out, "seam-report.txt"), report.ToString()); Debug.Log("[CloudWakeRender] done (seam only)\n" + report); return; }
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

            // ---- low patches and ground fog (Ty 08:39)
            report.AppendLine($"low patches: {cp.LowPuffyCount} puffy, {cp.LowWispCount} wisp; fog sites {cp.Fog.SiteCount}");
            for (int i = 0; i < cp.Fog.SiteCount; i++) { var f = cp.Fog.Site(i); report.AppendLine($"  fog {f.Id} lake={f.Lake} at ({f.X:F0}, {f.SurfaceM:F0}, {f.Z:F0}) {f.HalfLength * 2:F0} x {f.HalfWidth * 2:F0} m, {f.DepthM:F1} m deep"); }
            report.AppendLine(LowPatchShots(cp));
            report.AppendLine(FogCase(ref cp, "fog_strip_wake_swirl", lakeSite: false, aglM: 5, v: 28, throttle: 1.0, nz: 1.0, passSec: 12, afterSec: 5));
            report.AppendLine(FogCase(ref cp, "fog_strip_zero_lift_zero_thrust", lakeSite: false, aglM: 5, v: 28, throttle: 0.0, nz: 0.0, passSec: 12, afterSec: 5));
            report.AppendLine(FogCase(ref cp, "fog_lake_after_pass", lakeSite: true, aglM: 8, v: 30, throttle: 0.75, nz: 1.0, passSec: 14, afterSec: 25));
            report.AppendLine(CostProbe(cp));
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

        /// <summary>The mid-band seam (a screen rectangle where far clouds and both far sheets vanish; no single renderer
        /// closes it, 08:39 bisection): the same view yawed, with only the cloud playground drawn, with a nearer far
        /// plane, and with no depth texture, to tell a screen-space fault from a world-space one.</summary>
        static string SeamProbe()
        {
            var sb = new System.Text.StringBuilder();
            Transform t = _cam.transform;
            Vector3 p0 = t.position; Quaternion r0 = t.rotation; float far0 = _cam.farClipPlane;
            t.rotation = Quaternion.AngleAxis(8f, Vector3.up) * r0; Save("seam_yaw8"); t.rotation = r0;
            var off = new System.Collections.Generic.List<Renderer>();
            CloudPlayground cp = Object.FindFirstObjectByType<CloudPlayground>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && !r.transform.IsChildOf(cp.transform)) { r.enabled = false; off.Add(r); }
            Save("seam_clouds_only");
            // Which part of the playground: each group off in turn (still clouds only).
            void Group(string label, System.Func<Renderer, bool> pick)
            {
                var g = new System.Collections.Generic.List<Renderer>();
                foreach (var r in cp.GetComponentsInChildren<Renderer>()) if (r.enabled && pick(r)) { r.enabled = false; g.Add(r); }
                Save("seam_without_" + label);
                sb.AppendLine($"  without {label}: {g.Count} renderers off");
                foreach (var r in g) r.enabled = true;
            }
            Group("far_sheets", r => r.name == "Thin cloud sheet");
            Group("banks", r => r.name == "Cloud bank");
            Group("low", r => r.name.StartsWith("Low"));
            Group("fog", r => r.name.StartsWith("Ground fog"));
            Group("tiles", r => r.name == "Wake-reactive wisps");
            t.position = p0 + new Vector3(0, 0, -3000); Save("seam_eye_south3k");
            t.position = p0 + new Vector3(3000, 0, 0); Save("seam_eye_east3k");
            t.position = p0;
            foreach (var r in off) r.enabled = true;
            _cam.farClipPlane = 25000f; Save("seam_far25k"); _cam.farClipPlane = far0;
            _cam.depthTextureMode = DepthTextureMode.None; Save("seam_nodepth"); _cam.depthTextureMode = DepthTextureMode.Depth;
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cams) sb.AppendLine($"  camera {c.name} enabled={c.enabled} target={(c.targetTexture ? c.targetTexture.name : "screen")} depth={c.depth}");
            sb.AppendLine($"  eye {p0} forward {t.forward}");
            return "seam probe (seam_*.png):\n" + sb;
        }

        static string PathOf(Transform t) { string p = t.name; for (t = t.parent; t != null; t = t.parent) p = t.name + "/" + p; return p; }

        /// <summary>A low puffy patch from a kilometre off at its own height, and a low wisp patch from above.</summary>
        static string LowPatchShots(CloudPlayground cp)
        {
            var sb = new System.Text.StringBuilder();
            CloudSites.LowPatch? puffy = null, wisp = null;
            foreach (var lp in CloudSites.Low)
            {
                // Over the sea south of the island first: an unobstructed view.
                if (lp.Puffy && puffy == null && lp.Z < -15000) puffy = lp;
                if (!lp.Puffy && wisp == null && lp.Z < -15000) wisp = lp;
            }
            float t = 2000f;
            void Shot(string name, Vector3 eye, Vector3 look, float fov)
            {
                _cam.fieldOfView = fov;
                _cam.transform.position = eye;
                _cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                for (int i = 0; i < 3; i++) { t += 0.1f; cp.StepForTesting(_cam, t, 0.1f); }
                Save(name);
            }
            if (puffy is CloudSites.LowPatch p)
            {
                Vector3 c = new((float)p.X, (float)p.BaseM, (float)p.Z);
                Shot("low_patch_puffy", c + new Vector3(-700, 40, -650), c + Vector3.up * 60, 60);
                sb.AppendLine($"low_patch_puffy: patch at {c}, {p.AglM:F0} m AGL, radius {p.RadiusM:F0} m");
            }
            if (wisp is CloudSites.LowPatch w)
            {
                Vector3 c = new((float)w.X, (float)w.BaseM, (float)w.Z);
                Shot("low_patch_wisp", c + new Vector3(-500, 120, -700), c, 65);
                sb.AppendLine($"low_patch_wisp: patch at {c}, {w.AglM:F0} m AGL, radius {w.RadiusM:F0} m");
            }
            return sb.ToString();
        }

        /// <summary>A C172-sized aeroplane passes along a fogged site <paramref name="aglM"/> above it at
        /// <paramref name="v"/> m/s, load factor <paramref name="nz"/> (lift = nz·W, Γ from it) and throttle
        /// <paramref name="throttle"/> (slipstream from the real propeller, Aircraft.TailSlipstream); the fog is advanced
        /// through the wake the whole time and for <paramref name="afterSec"/> after; then two shots.</summary>
        static string FogCase(ref CloudPlayground cp, string name, bool lakeSite, double aglM, double v, double throttle, double nz, double passSec, double afterSec)
        {
            Transform parent = cp.transform.parent;
            Object.DestroyImmediate(cp.gameObject);
            cp = CloudPlayground.Attach(parent);
            VortexWake.Clear();
            GroundFog fog = cp.Fog;
            int si = -1;
            // The first strip / the largest lake.
            for (int i = 0; i < fog.SiteCount; i++)
                if (fog.Site(i).Lake == lakeSite && (si < 0 || (lakeSite && fog.Site(i).HalfLength * fog.Site(i).HalfWidth > fog.Site(si).HalfLength * fog.Site(si).HalfWidth))) si = i;
            if (si < 0) return name + ": no such site";
            CloudSites.FogSite site = fog.Site(si);
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("c172-like");
            double span = FlyingGame.Core.Aero.AeroModel.WingSpan(cfg), weight = cfg.Mass.MassKg * Atmosphere.GravityMs2;
            WakeSource src = VortexWake.CreateSource(name, span);
            float hdg = (float)site.HeadingDeg * Mathf.Deg2Rad;
            Vector3 along = new(Mathf.Sin(hdg), 0, Mathf.Cos(hdg)), across = new(along.z, 0, -along.x);
            Vector3 centre = new((float)site.X, (float)site.SurfaceM, (float)site.Z);
            float trackLen = (float)(v * passSec);
            Vector3 start = centre - along * (trackLen * .5f) + Vector3.up * (float)aglM;
            // Shot 1 (the patch follows this camera during the run): beside the track's middle, looking at it.
            Vector3 eye1 = centre + across * 75f + Vector3.up * 32f - along * 20f;
            Vector3 look1 = centre + along * 10f + Vector3.up * 3f;
            // Shot 2: from behind the start of the track, low, looking down it.
            Vector3 eye2 = centre - along * (trackLen * .5f + 25f) + across * 9f + Vector3.up * 10f;
            Vector3 look2 = centre - along * (trackLen * .5f - 70f) + Vector3.up * 1f;
            _cam.fieldOfView = 60f;
            _cam.transform.SetPositionAndRotation(eye1, Quaternion.LookRotation(look1 - eye1, Vector3.up));
            double dt = 1.0 / 30.0, rho = Atmosphere.DensityAt(CoordinateMap.ToSim(start));
            Quat att = new(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));   // NED yaw = heading, level
            Vec3 velBody = new(v, 0, 0);
            (double du, double rs, double sw) = Aircraft.TailSlipstream(cfg, throttle, velBody, Vec3.Zero, rho);
            float now = 3000f;
            Vec3 pos = CoordinateMap.ToSim(start);
            int steps = (int)(passSec / dt), after = (int)(afterSec / dt);
            var plane = new GameObject("Fog pass aeroplane");
            try { new AirframeBuilder().Build(plane.transform, cfg); } catch (System.Exception e) { Debug.LogWarning(e.Message); }
            plane.SetActive(false);
            for (int i = 0; i < steps + after; i++)
            {
                if (i < steps)
                {
                    pos = CoordinateMap.ToSim(start + along * (float)(v * i * dt));
                    Vec3 f = att.Rotate(new Vec3(0, 0, -nz * weight));
                    src.Shed(dt, pos, att, att.Rotate(velBody), f, rho, du, rs, sw);
                }
                else if (i == steps) src.Detach();
                VortexWake.Advance(dt);
                now += (float)dt;
                fog.StepForTesting(_cam, now, (float)dt);
                if (i == steps * 3 / 4 && nz > 0)
                {
                    // In the middle of it: the aeroplane over the strip, its wake behind it.
                    plane.SetActive(true);
                    Vector3 at = CoordinateMap.ToUnity(pos);
                    plane.transform.SetPositionAndRotation(at, CoordinateMap.ToUnity(att));
                    Vector3 eyeM = at - along * 60f + across * 16f + Vector3.up * 8f;
                    _cam.transform.SetPositionAndRotation(eyeM, Quaternion.LookRotation(at - along * 18f - eyeM, Vector3.up));
                    Save(name + "_during");
                    _cam.transform.SetPositionAndRotation(eye1, Quaternion.LookRotation(look1 - eye1, Vector3.up));
                    plane.SetActive(false);
                }
            }
            Save(name);
            _cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(look2 - eye2, Vector3.up));
            Save(name + "_along_track");
            Vector3 eyeT = centre + Vector3.up * 110f;
            _cam.transform.SetPositionAndRotation(eyeT, Quaternion.LookRotation(Vector3.down, along));
            Save(name + "_top");
            Object.DestroyImmediate(plane);
            _cam.transform.SetPositionAndRotation(eye1, Quaternion.LookRotation(look1 - eye1, Vector3.up));
            var segs = new System.Collections.Generic.List<VortexSegment>();
            int n = VortexWake.GetActiveSegments(segs);
            double gmax = 0; foreach (var s in segs) gmax = System.Math.Max(gmax, System.Math.Abs(s.Gamma));
            SaveAtlas(fog.WakeAtlasForTesting, name + "_wake_atlas");
            SaveSlices(fog, name + "_fog_slices");
            string xs = CrossSection(fog, centre, along, across, site);
            return $"{name}: site {site.Id} ({(site.Lake ? "lake" : "strip")}), pass {aglM} m AGL at {v} m/s, n {nz}, throttle {throttle}: slipstream +{du:F1} m/s r {rs:F2} m; " +
                   $"{after * dt:F0} s after: {n} live segments, max |Γ| {gmax:F2} m²/s; texture segs {UnityWakeField.LastTextureSegments}/{UnityWakeField.LastRegionSegments}, jets {UnityWakeField.LastTextureJets}; active fog site {fog.ActiveSite}\n{xs}";
        }

        /// <summary>A Mac-only proxy, NOT a device number: GPU time per frame of the current view (the lake after its pass)
        /// with the fog and the low patches drawn and hidden, 1600 x 1000, each frame forced to finish by a 1-pixel
        /// readback. Also the fog step's CPU time.</summary>
        static string CostProbe(CloudPlayground cp)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            double Frame(int n)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    _cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = _rt;
                    tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); tex.Apply(); RenderTexture.active = prev;
                }
                return sw.Elapsed.TotalMilliseconds / n;
            }
            var fogR = cp.Fog.GetComponentsInChildren<Renderer>();
            var lowR = new System.Collections.Generic.List<Renderer>();
            foreach (Transform t in cp.transform) if (t.name.StartsWith("Low")) lowR.Add(t.GetComponent<Renderer>());
            void Show(bool fog, bool low) { foreach (var r in fogR) r.enabled = fog; foreach (var r in lowR) if (r) r.enabled = low; }
            Frame(10);
            Show(false, false); double none = Frame(60);
            Show(true, false); double fog = Frame(60);
            Show(true, true); double both = Frame(60);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            float now = 9000f;
            for (int i = 0; i < 60; i++) { now += GroundFog.StepSec; cp.Fog.StepForTesting(_cam, now, GroundFog.StepSec); }
            GL.Flush();
            double step = sw2.Elapsed.TotalMilliseconds / 60;
            Object.DestroyImmediate(tex);
            return $"cost (Mac editor proxy, 1600x1000): frame {none:F2} ms without fog/low patches, {fog:F2} with fog, {both:F2} with both; fog step (CPU submit) {step:F3} ms";
        }

        /// <summary>The eight fog slices, 4 x 2 (bottom row = slices 0-3), grey = density; and their range.</summary>
        static void SaveSlices(GroundFog fog, string name)
        {
            int n = GroundFog.Resolution;
            var o = new Texture2D(n * 4, n * 2, TextureFormat.RGB24, false);
            float lo = 9, hi = -9;
            for (int h = 0; h < 2; h++)
            {
                RenderTexture rt = fog.SlicesForTesting(h);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(n, n, TextureFormat.RGBAFloat, false);
                tex.ReadPixels(new Rect(0, 0, n, n), 0, 0); tex.Apply(); RenderTexture.active = prev;
                var px = tex.GetPixels();
                for (int c = 0; c < 4; c++)
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = px[y * n + x][c];
                    lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                    o.SetPixel(c * n + x, h * n + y, new Color(d, d, d));
                }
                Object.DestroyImmediate(tex);
            }
            o.Apply();
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), o.EncodeToPNG());
            Object.DestroyImmediate(o);
            Debug.Log($"[CloudWakeRender] {name}: density {lo:F3}..{hi:F3}");
        }

        /// <summary>Across the track's middle at slice 1's height: the wake from the CPU (WakeField.SampleVelocity) and
        /// from the GPU atlas the fog read, side by side (lateral / vertical m/s, Unity axes).</summary>
        static string CrossSection(GroundFog fog, Vector3 centre, Vector3 along, Vector3 across, CloudSites.FogSite site)
        {
            RenderTexture rt = fog.WakeAtlasForTesting;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
            Vector4 patch = fog.PatchForTesting;
            float h = patch.w, y1 = (float)site.SurfaceM + 1.5f * h / 8;
            var sb = new System.Text.StringBuilder($"  cross-section, slice 1 at {1.5f * h / 8:F1} m (cpu | gpu):");
            for (float d = -20; d <= 20; d += 4)
            {
                Vector3 p = centre + across * d; p.y = y1;
                Vector3 c = UnityWakeField.SampleVelocity(p);
                Vector2 uv = new((p.x - patch.x) / patch.z, (p.z - patch.y) / patch.z);
                Color g = tex.GetPixelBilinear((1 + uv.x) / 4f, uv.y / 2f);
                sb.Append($" {d:F0}: {Vector3.Dot(c, across):F1}/{c.y:F1} | {Vector3.Dot(new Vector3(g.r, 0, g.g), across):F1}/{g.a:F1};");
            }
            Object.DestroyImmediate(tex);
            return sb.ToString();
        }

        static void SaveAtlas(RenderTexture rt, string name) => SaveRt(rt, name, true);

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

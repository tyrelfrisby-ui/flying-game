using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Clouds;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The thermal cumulus (Bridge/Cumulus.cs + Shaders/Cumulus.shader), rendered in the world it actually ships in —
    /// the island, with the CloudPlayground banks and the painted sky deck in the same frames, so the three cloud
    /// implementations can be judged against each other in ONE picture rather than three (Ty 2026-11-11: "there are
    /// clouds that look good realistic and there are clouds that look fake like a bunch of circles glued together").
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CumulusRender.Render -outDir DIR
    /// (no -nographics: it renders.)
    /// </summary>
    public static class CumulusRender
    {
        static string _out;
        static Camera _cam;
        static RenderTexture _rt;
        static CloudPlayground _cp;

        [MenuItem("FlyingGame/Render Cumulus")]
        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(args, "-outDir");
            _out = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/cumulus"));
            Directory.CreateDirectory(_out);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            WorldTerrain.Active = new WorldTerrain();
            WorldClock.TimeS = 20;
            Atmosphere.SteadyWind = Vec3.Zero;
            Atmosphere.Thermals.Clear();
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            SunsetLighting.ConfigureDayAtmosphere("Balanced", .000052f, 850f);

            // The same sun SceneBootstrap.BuildSun hangs up, so the shading in these frames is the shading in the game.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            // The thermals and their cumulus, exactly as SoaringScenery lays them at play start. Timed, because the
            // meshes are built on the CPU in the startup frame and the Quest's first frame is already 3.5-7.4 s
            // (docs/quest-performance.md); a cloud that looks right but costs a second of launch is not a win.
            var build = System.Diagnostics.Stopwatch.StartNew();
            SoaringScenery.BuildThermals();
            double buildMs = build.Elapsed.TotalMilliseconds;

            var camGo = new GameObject("RenderCam");
            camGo.tag = "MainCamera";                      // Cumulus.LateUpdate reads Camera.main for the whiteout
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            _cam.depthTextureMode = DepthTextureMode.Depth;
            _cam.nearClipPlane = 1f; _cam.farClipPlane = 60000f;
            _rt = new RenderTexture(1600, 1000, 24);
            _cam.targetTexture = _rt;
            _cp = Object.FindFirstObjectByType<CloudPlayground>();

            var report = new System.Text.StringBuilder();
            report.AppendLine($"thermals {Atmosphere.Thermals.Count}; every cloud's mesh built in {buildMs:F0} ms "
                            + $"({buildMs / System.Math.Max(1, Atmosphere.Thermals.Count):F1} ms each) on this Mac");

            // The first thermal over the default airport: the one a glider flying from the home field works first.
            Thermal th = Atmosphere.Thermals[0];
            float baseY = (float)th.TopAltitudeM;
            float radius = (float)(th.CoreRadiusM * 1.8 * 1.05);
            float tall = Mathf.Lerp(0.6f, 1.05f, Mathf.InverseLerp(4f, 14f, (float)th.CoreUpdraftMs));
            float height = 2f * radius * tall;
            Vector3 c = CoordinateMap.ToUnity(th.CoreAt(th.TopAltitudeM * 0.98));
            c.y = baseY;
            report.AppendLine($"cumulus 0 at {c}, base {baseY:F0} m, radius {radius:F0} m, height {height:F0} m, core {th.CoreUpdraftMs:F1} m/s");

            // Looking NORTH-EAST from the south-west so the sun (azimuth −35°, elevation 55°) is behind the camera's
            // shoulder: the lit side faces us, which is how a cumulus is usually seen and the hardest test of its shape.
            Vector3 lit = new Vector3(-0.72f, 0f, -0.69f);
            Shot("cumulus_1500m_lit", c + lit * 1500f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 42);
            Shot("cumulus_3km_lit", c + lit * 3000f + Vector3.up * (height * 0.30f), c + Vector3.up * (height * 0.45f), 34);
            // Backlit from the other side: the silhouette alone, nothing but the outline to look at.
            Shot("cumulus_1500m_backlit", c - lit * 1500f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 42);
            // From below the condensation level, the pilot's usual view of one.
            Shot("cumulus_under_base", c + lit * 700f - Vector3.up * 500f, c + Vector3.up * (height * 0.2f), 60);
            // Above it, looking down on the crown.
            Shot("cumulus_above_crown", c + lit * 1100f + Vector3.up * (height + 700f), c + Vector3.up * (height * 0.7f), 50);

            // The comparison shot: the thermal cumulus in the near field with a CloudPlayground bank behind it. The
            // banks stand on rings 6 / 11 / 16.5 km from the island centre, so sight along the line from the cloud
            // AWAY from the centre and a bank lands in the background.
            Vector3 outward = new Vector3(c.x, 0, c.z).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.right;
            Shot("compare_cumulus_vs_banks", c - outward * 2200f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 55);

            // A playground BANK, with the pre-pass inert (what ships) and armed (what it would do to a look Ty
            // already approved). Same camera both times: if the two frames differ anywhere but in noise, arming
            // it for the banks is a regression and the answer is to leave them alone.
            if (_cp != null)
            {
                Transform bank = null; float best = float.MaxValue;
                foreach (Transform t in _cp.transform)
                {
                    if (t.name != "Cloud bank") continue;
                    float d = Vector3.Distance(t.position, c);
                    if (d < best) { best = d; bank = t; }
                }
                Material billow = Resources.Load<Material>("Clouds/CloudBillow");
                if (bank != null && billow != null)
                {
                    Vector3 outward2 = new Vector3(bank.position.x, 0, bank.position.z).normalized;
                    if (outward2.sqrMagnitude < 0.5f) outward2 = Vector3.right;
                    Vector3 eye = bank.position - outward2 * 2400f + Vector3.up * 150f;
                    Vector3 at = bank.position + Vector3.up * 250f;
                    report.AppendLine($"bank at {bank.position}, {best / 1000:F1} km from the cumulus, shot from {eye}");
                    Shot("bank_prepass_inert", eye, at, 45);
                    ArmPlayground(0.94f); Shot("bank_prepass_armed", eye, at, 45); ArmPlayground(-1f);
                }
            }
            // Does the cumulus seam the way the bank does? The backlit view puts the cut against blue sky, which
            // is the worst background for it, and the view from above is where the impostors were losing anyway.
            foreach (var (tag, core) in new[] { ("off", 2f), ("094", 0.94f), ("080", 0.80f) })
            {
                Cumulus.CoreAlpha = core;
                Shot($"core_{tag}_backlit", c - lit * 1500f + Vector3.up * (height * 0.35f), c + Vector3.up * (height * 0.45f), 42);
                Shot($"core_{tag}_above", c + lit * 1100f + Vector3.up * (height + 700f), c + Vector3.up * (height * 0.7f), 50);
            }
            Cumulus.CoreAlpha = 0.94f;
            report.AppendLine(Cost(c, height));
            File.WriteAllText(Path.Combine(_out, "cumulus-render-report.txt"), report.ToString());
            Debug.Log("[CumulusRender] done\n" + report);
        }

        /// <summary>What the thermal cumulus cost the phone: the geometry every one of them submits (this is static
        /// mesh, built once and never touched again, so vertices and triangles ARE the budget), and the GPU time for
        /// one frame with them and one without — measured by rendering the same camera 60 times either way.</summary>
        /// <summary>
        /// The four-way measurement Ty asked for (2026-11-11, "build it and measure it"): the shipped baseline,
        /// plus a 19 km cull, plus the depth pre-pass over the opaque lobe cores, plus both — at the two cameras
        /// the earlier numbers were taken at, so the table sets beside the one he already has. Frame time is the
        /// best of three interleaved passes of forty frames; overdraw is summed on the CPU from the lobes' own
        /// projected area with the shader's fades applied, because a frame time on a desktop GPU can only RANK
        /// these options and a fill figure is the thing that carries over to a phone.
        /// </summary>
        static string Cost(Vector3 cloud, float height)
        {
            var sb = new System.Text.StringBuilder();
            var clouds = new List<MeshRenderer>();
            GameObject root = GameObject.Find("Clouds");
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) clouds.Add(r);
            int verts = 0, tris = 0;
            foreach (var r in clouds)
            {
                Mesh m = r.GetComponent<MeshFilter>().sharedMesh;
                if (m == null) continue;
                verts += m.vertexCount; tris += m.triangles.Length / 3;
            }
            sb.AppendLine($"cumulus geometry: {clouds.Count} clouds, {verts} vertices, {tris} triangles "
                        + $"({(clouds.Count > 0 ? verts / clouds.Count : 0)} v / {(clouds.Count > 0 ? tris / clouds.Count : 0)} t each), "
                        + $"{verts * 24.0 / 1024 / 1024:F1} MiB of vertex buffer (pos + normal + colour)");

            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            double Frame(int n)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    _cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = _rt;
                    px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = prev;
                }
                return sw.Elapsed.TotalMilliseconds / n;
            }
            void Measure(string label, Vector3 eye, float fov)
            {
                _cam.fieldOfView = fov;
                _cam.transform.position = eye;
                _cam.transform.rotation = Quaternion.LookRotation(cloud + Vector3.up * height * 0.45f - eye, Vector3.up);
                // The clouds-off frame ONCE for the whole camera, best of four. Measuring it again per config put
                // 0.6 ms of drift into every delta, which is the size of the effects being compared.
                foreach (var r in clouds) r.enabled = false;
                Frame(20);
                double without = 1e9;
                for (int pass = 0; pass < 4; pass++) without = System.Math.Min(without, Frame(60));
                sb.AppendLine($"  {label} — no thermal clouds at all: {without:F2} ms");
                foreach (var (name, cull, core) in new[] {
                    ("baseline: no cull, no pre-pass", false, 2f),
                    ("+ 19 km cull only", true, 2f),
                    ("+ pre-pass only, core 0.94", false, 0.94f),
                    ("+ pre-pass only, core 0.90", false, 0.90f),
                    ("+ pre-pass only, core 0.80", false, 0.80f),
                    ("+ both, core 0.94 (shipped)", true, 0.94f) })
                {
                    Cumulus.CullFar = cull; Cumulus.CoreAlpha = core;
                    foreach (var r in clouds) r.enabled = true;
                    Cumulus.StepForTesting();
                    Frame(20);
                    double with = 1e9;
                    for (int pass = 0; pass < 4; pass++) { Cumulus.StepForTesting(); with = System.Math.Min(with, Frame(60)); }
                    double fill = Fill(out string bands, out int drawn);
                    sb.AppendLine($"    {name,-30} {with:F2} ms = {with - without:+0.00;-0.00} ms for the clouds; "
                                + $"{drawn} lobes drawn, {fill:F2}x the frame in blended fill (CPU estimate, ignores the pre-pass)");
                    if (!cull && core > 1f) sb.AppendLine($"      where that fill is: {bands}");
                }
            }

            Measure("one cloud filling the frame", cloud + new Vector3(0, height * 0.4f, -height * 0.5f), 60);
            Measure("3 km cruise view", cloud + new Vector3(-0.72f, 0f, -0.69f) * 3000f + Vector3.up * (height * 0.30f), 34);

            // What the OTHER clouds cost, and whether arming the pre-pass for them would move anything. The banks
            // and the low patches keep _CoreAlpha = 2 in the shipped build, so their depth pass runs and clips at
            // once; this measures them with it inert and then with it armed, with the thermal clouds switched off.
            Cumulus.CullFar = true; Cumulus.CoreAlpha = 0.94f;
            foreach (var r in clouds) r.enabled = true;
            Cumulus.StepForTesting();
            var cp = Object.FindFirstObjectByType<CloudPlayground>();
            if (cp != null)
            {
                var others = new List<Renderer>();
                foreach (var r in cp.GetComponentsInChildren<Renderer>()) if (r.enabled) others.Add(r);
                foreach (var r in clouds) r.enabled = false;
                Frame(20);
                double banksOn = 1e9, banksOff = 1e9;
                for (int pass = 0; pass < 3; pass++)
                {
                    foreach (var r in others) r.enabled = true;
                    banksOn = System.Math.Min(banksOn, Frame(40));
                    foreach (var r in others) r.enabled = false;
                    banksOff = System.Math.Min(banksOff, Frame(40));
                }
                foreach (var r in others) r.enabled = true;
                double armed = 1e9;
                if (ArmPlayground(0.94f))
                {
                    Frame(20);
                    for (int pass = 0; pass < 3; pass++) armed = System.Math.Min(armed, Frame(40));
                    ArmPlayground(-1f);
                }
                sb.AppendLine($"  the playground alone (banks, low patches, wisps; thermal clouds off): {banksOn:F2} ms with / "
                            + $"{banksOff:F2} without = {banksOn - banksOff:+0.00;-0.00} ms. With the pre-pass ARMED for them "
                            + $"too: {armed:F2} ms ({armed - banksOn:+0.00;-0.00} ms).");
                foreach (var r in clouds) r.enabled = true;
                Cumulus.StepForTesting();
            }
            Object.DestroyImmediate(px);
            return sb.ToString();
        }

        /// <summary>
        /// How many times over the thermal clouds paint the frame from the camera as it stands. An impostor lobe
        /// is a blended quad: without the depth pre-pass it costs its whole disc in fill whether or not something
        /// nearer already covered it. Summed on the CPU from the lobes themselves, with the shader's distance and
        /// secondary-lobe fades applied and each disc clipped to the frame — and broken down by range, so it is
        /// visible how much of the bill a tighter fade could ever reach. Disabled renderers do not count, which is
        /// how the cull shows up here at all.
        /// </summary>
        static double Fill(out string bands, out int drawn)
        {
            bands = ""; drawn = 0;
            GameObject root = GameObject.Find("Clouds");
            if (root == null) return 0;
            float w = _rt.width, h = _rt.height;
            float focal = h * 0.5f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 eye = _cam.transform.position;
            float[] edges = { 3000f, 6000f, 10000f, 1e9f };
            var band = new double[edges.Length];
            double covered = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled) continue;                       // the cull has already spoken
                Mesh m = r.GetComponent<MeshFilter>().sharedMesh;
                if (m == null) continue;
                Vector3[] v = m.vertices; var size = new List<Vector2>(); m.GetUVs(1, size);
                Color[] col = m.colors;
                for (int i = 0; i < v.Length; i += 4)
                {
                    Vector3 centre = r.transform.TransformPoint(v[i]);
                    float d = Vector3.Distance(centre, eye);
                    if (Vector3.Dot(_cam.transform.forward, centre - eye) <= 0f) continue;
                    float fade = Mathf.Clamp01((18000f - d) / 3000f)
                               * Mathf.Clamp01((d - size[i].x * 0.3f) / Mathf.Max(12f, size[i].x * 0.5f));
                    if (col != null && col.Length > i)          // the secondary-lobe fade: granulation at 7.5 km
                        fade *= Mathf.Lerp(1f, Mathf.Clamp01((7500f - d) / 1500f), col[i].a);
                    if (fade <= 0.01f) continue;
                    float rpx = size[i].x * focal / Mathf.Max(1f, d);
                    double area = System.Math.Min(Mathf.PI * rpx * rpx, w * h) * fade;
                    covered += area; drawn++;
                    for (int b = 0; b < edges.Length; b++) if (d < edges[b]) { band[b] += area; break; }
                }
            }
            bands = $"under 3 km {band[0] / (w * h):F2}x, 3-6 km {band[1] / (w * h):F2}x, "
                  + $"6-10 km {band[2] / (w * h):F2}x, beyond 10 km {band[3] / (w * h):F2}x";
            return covered / (w * h);
        }

        /// <summary>
        /// Turn the depth pre-pass on for the PLAYGROUND's own clouds (the banks, the low patches), or off again
        /// with a negative core. It swaps a clone onto their renderers rather than setting the property on
        /// Resources/Clouds/CloudBillow, because that material is a file in the repo: setting a property on it
        /// makes Unity rewrite the .mat, and a measurement that dirties a committed asset is a measurement that
        /// will one day be committed by accident. (It was, once, and had to be reverted by hand.)
        /// </summary>
        static Material _armed, _billow;
        static bool ArmPlayground(float core)
        {
            _billow ??= Resources.Load<Material>("Clouds/CloudBillow");
            if (_billow == null || _cp == null) return false;
            if (core > 0f && _armed == null) { _armed = new Material(_billow); _armed.SetFloat("_CoreAlpha", core); }
            Material want = core > 0f ? _armed : _billow;
            foreach (var r in _cp.GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial == _billow || r.sharedMaterial == _armed) r.sharedMaterial = want;
            return true;
        }

        static void Shot(string name, Vector3 eye, Vector3 look, float fov)
        {
            _cam.fieldOfView = fov;
            _cam.transform.position = eye;
            _cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
            Cumulus.StepForTesting();
            if (_cp != null) for (int i = 0; i < 3; i++) _cp.StepForTesting(_cam, 10f + i * 0.1f, 0.1f);
            Save(name);
        }

        static void Save(string name)
        {
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[CumulusRender] {name}");
        }
    }
}

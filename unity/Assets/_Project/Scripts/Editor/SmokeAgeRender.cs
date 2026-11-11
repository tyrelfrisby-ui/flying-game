using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Smoke PERSISTENCE plates (Ty 2026-11-04 18:27: "disappear way too quickly … even more transparent … should stick
    /// around longer and be thicker, more white, less transparent"). The island in daylight, the Pitts on its own model, a
    /// long straight pass at 60 m/s, 300 m up, in a 4 m/s crosswind, and the game's own <see cref="SmokeTrail"/> driven as
    /// <see cref="SmokeController"/> drives it. One camera 80 m abeam of (and 35 m below) the stretch of trail laid -smokeAges seconds ago
    /// (default 0,5,20,45), the same framing at every age, so the plates compare like for like; and one 30 m from the 10 s
    /// stretch (close10), where the camera-proximity thinning shows.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.SmokeAgeRender.Render -smokeTag after
    /// For each age it writes &lt;tag&gt;_age&lt;A&gt;.png (as seen) and &lt;tag&gt;_age&lt;A&gt;_cover.png (the smoke's coverage
    /// alone: white puffs on a black ground whose alpha is 1, as the game's sky is), plus a view down onto a stand-in for
    /// water (an alpha-blended plane, which leaves destination alpha below 1 as FlyingGame/Water does), and
    /// &lt;tag&gt;_stats.txt: per age, the peak coverage across the trail, its width above 50 %, the puffs and their fill.
    /// <see cref="Variants"/> lets a temporary file render an older SmokeTrail through the same flight (the before plates).
    /// </summary>
    public static class SmokeAgeRender
    {
        /// <summary>One recorded moment of the flight, in the fields every SmokeTrail version has used.</summary>
        public struct Point { public double T; public Vec3 Pos; public Quat Att; public Vec3 VelBody; public Vec3 Rates; public float Flow; public double JetU0, AirDensity, Crank; public float Rpm; }

        /// <summary>The trail under test: its renderer, and a Build at time t from the recorded points (oldest first).</summary>
        public interface ITrail { MeshRenderer Renderer { get; } void Build(List<Point> hist, double nowT, Camera cam); }

        /// <summary>Other SmokeTrail implementations by name (-smokeImpl), registered by a temporary editor file.</summary>
        public static readonly Dictionary<string, System.Func<Transform, AircraftConfig, Vec3[], Vec3[], float, float, ITrail>> Variants = new();

        private sealed class Current : ITrail
        {
            private readonly SmokeTrail _s; private readonly GameObject _root;
            public MeshRenderer Renderer => _root.GetComponentInChildren<MeshRenderer>(true);
            public Current(Transform parent, AircraftConfig cfg, Vec3[] ex, Vec3[] jet, float lengthM, float bodyRadiusM)
            {
                _root = parent.gameObject;
                _s = new SmokeTrail(parent, 0);
                _s.SetExhausts(ex, jet);
                _s.Flow = new FlyingGame.Core.Aero.AirframeFlow(cfg, 0.4 * lengthM, bodyRadiusM);
                _s.ApplyConfig(cfg);
            }
            private List<SmokeTrail.Sample> _ring;
            public void Build(List<Point> h, double nowT, Camera cam)
            {
                // The ring as SmokeController keeps it: every sample, thinned past 2 s.
                if (_ring == null)
                {
                    _ring = new List<SmokeTrail.Sample>(h.Count);
                    foreach (var p in h) _ring.Add(new SmokeTrail.Sample { T = p.T, Pos = p.Pos, Att = p.Att, VelBody = p.VelBody, Rates = p.Rates, Flow = p.Flow, JetU0 = p.JetU0, AirDensity = p.AirDensity, Crank = p.Crank, Rpm = p.Rpm });
                    SmokeTrail.ThinRing(_ring, nowT);
                }
                _s.Build(k => { int i = _ring.Count - 1 - k; return i < 0 ? (SmokeTrail.Sample?)null : _ring[i]; }, nowT, p => CoordinateMap.ToUnity(p), cam);
            }
        }

        private const int SmokeLayer = 31;

        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            string tag = Arg("-smokeTag", "after"), impl = Arg("-smokeImpl", "");
            string outDir = Path.GetFullPath(Arg("-smokeOut", Path.Combine(Application.dataPath, "../../renders/smoke-thicker")));
            var ages = new List<double>(); foreach (string a in Arg("-smokeAges", "0,5,20,45").Split(',')) ages.Add(double.Parse(a, System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outDir);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var terrain = new WorldTerrain();
            WorldTerrain.Active = terrain;
            WorldClock.TimeS = 20;
            // A 4 m/s breeze across the pass (from the right), so the plates show the trail drifting off its line.
            float savedLodBias = QualitySettings.lodBias; QualitySettings.lodBias = .7f;
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool training = false;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) training = true;
                if (training) renderer.enabled = false;
            }

            IslandField field = null;
            foreach (var f in IslandTerrain.Fields) if (StripFinish.Painted(f.Surface) && (field == null || f.Length > field.Length)) field = f;
            double hdg = field.Heading * System.Math.PI / 180, ux = System.Math.Cos(hdg), uy = System.Math.Sin(hdg);
            const double V = 60, Total = 100, Wind = 4;
            double baseUp = field.Elevation + 300;
            // The pass runs along the runway's heading and ends over the field centre; the wind blows to its LEFT.
            Vec3 left = new Vec3(uy, -ux, 0);
            Vec3 savedWind = Atmosphere.SteadyWind;
            Atmosphere.SteadyWind = left * Wind;
            Vec3 PosAt(double t) => new Vec3(field.Centre.X + ux * V * (t - Total), field.Centre.Y + uy * V * (t - Total), -baseUp);
            Quat att = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));

            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = true;
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
            var plane = new GameObject("pitts");
            var builder = new AirframeBuilder();
            builder.Build(plane.transform, cfg);
            plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(PosAt(Total)), CoordinateMap.ToUnity(att));

            var smokeRoot = new GameObject("SmokeRoot");
            float lengthM = (float)cfg.Fuselage.Crossflow.LengthM, bodyRadiusM = (float)cfg.Fuselage.Crossflow.BodyRadiusM;
            var ex = SmokeTrail.ExhaustsFor(cfg, "pitts-s2b-like", builder.PropHubsBody(), lengthM, bodyRadiusM, out var jet);
            ITrail trail = impl.Length > 0 && Variants.TryGetValue(impl, out var make) ? make(smokeRoot.transform, cfg, ex, jet, lengthM, bodyRadiusM)
                                                                                     : new Current(smokeRoot.transform, cfg, ex, jet, lengthM, bodyRadiusM);
            if (impl.Length > 0 && !Variants.ContainsKey(impl)) throw new System.ArgumentException($"-smokeImpl {impl}: not registered");
            MeshRenderer smokeR = trail.Renderer;
            smokeR.gameObject.layer = SmokeLayer;
            Atmosphere.SimTimeSec = Total;

            // The airshow setting (0.85 throttle, 95 % rpm), as SmokeVolumeRender's "show".
            double rpm = cfg.Propulsion.MaxRpm * 0.95, thr = 0.85;
            var hist = new List<Point>();
            for (double t = 0; t <= Total + 1e-9; t += 1.0 / 60)
            {
                double rho = Atmosphere.DensityAtAltitude(baseUp);
                hist.Add(new Point { T = t, Pos = PosAt(t), Att = att, VelBody = new Vec3(V, 0, 0), Rates = Vec3.Zero, Flow = (float)(0.25 + 0.75 * thr), AirDensity = rho,
                    JetU0 = FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cfg.Smoke, cfg.Propulsion, rpm, thr, rho), Crank = rpm / 60.0 * t, Rpm = (float)rpm });
            }

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .3f; cam.farClipPlane = 80000f; cam.fieldOfView = 50f;
            const int W = 1280, H = 800;
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var forest = new GameObject("ForestView");
            ForestBuilder.BuildAround(terrain, forest.transform, field.Centre.X, field.Centre.Y, 2600, 800);

            var log = new System.Text.StringBuilder();
            log.AppendLine($"tag {tag}{(impl.Length > 0 ? " impl " + impl : "")}: Pitts, straight and level at {V} m/s, {baseUp - field.Elevation:0} m above the field, {Wind} m/s crosswind, " +
                           $"{rpm:0} rpm / throttle {thr:0.00} (flow {hist[^1].Flow:0.00}), shown {Total:0} s after the smoke went on; camera 80 m abeam, fov {cam.fieldOfView:0}");
            Vec3 Drift(double age) => Atmosphere.SteadyWind * age + new Vec3(0, 0, -System.Math.Min(2.0, 0.12 * age));
            Vec3 Right = new Vec3(-uy, ux, 0);
            void Shot(string name, Vector3 eye, Vector3 look, bool measure)
            {
                cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                cam.cullingMask = ~0;
                trail.Build(hist, Total, cam);
                // The CPU cost of one rebuild (the game does one a frame): the mean of ten, the airframe-flow cache warm.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int r = 0; r < 10; r++) trail.Build(hist, Total, cam);
                double buildMs = sw.Elapsed.TotalMilliseconds / 10;
                Shoot(cam, rt, Path.Combine(outDir, $"{tag}_{name}.png"));
                if (!measure) return;
                // Coverage alone: every puff white at its own alpha, over black whose alpha is 1 (as the game's sky writes).
                Mesh m = smokeR.GetComponent<MeshFilter>().sharedMesh;
                var cols = new List<Color>(); m.GetColors(cols);
                var white = new List<Color>(cols.Count); foreach (var c in cols) white.Add(new Color(1, 1, 1, c.a));
                m.SetColors(white);
                Material mat = smokeR.sharedMaterial; Color tint = mat.color; mat.color = Color.white;
                var clear = cam.clearFlags; var bg = cam.backgroundColor;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 1); cam.cullingMask = 1 << SmokeLayer;
                Color32[] cov = Shoot(cam, rt, Path.Combine(outDir, $"{tag}_{name}_cover.png"));
                cam.clearFlags = clear; cam.backgroundColor = bg; cam.cullingMask = ~0; mat.color = tint; m.SetColors(cols);
                // Across the trail: the middle fifth of the frame's columns, each scanned top to bottom.
                float metresPerPx = 2f * (look - eye).magnitude * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) / H;
                double peakSum = 0, widthSum = 0, meanSum = 0; int n = 0;
                for (int x = 2 * W / 5; x < 3 * W / 5; x += 4)
                {
                    float peak = 0, sum = 0; int over = 0;
                    for (int y = 0; y < H; y++) { float c = cov[y * W + x].r / 255f; peak = Mathf.Max(peak, c); sum += c; if (c > 0.5f) over++; }
                    peakSum += peak; widthSum += over * metresPerPx; meanSum += sum * metresPerPx; n++;
                }
                int quads = m.vertexCount / 4; double px = 0;
                var vs = m.vertices;
                for (int q = 0; q < quads; q++)
                {
                    Vector3 a = cam.WorldToScreenPoint(vs[4 * q]), b = cam.WorldToScreenPoint(vs[4 * q + 1]), c = cam.WorldToScreenPoint(vs[4 * q + 2]), d = cam.WorldToScreenPoint(vs[4 * q + 3]);
                    if (a.z <= cam.nearClipPlane || b.z <= cam.nearClipPlane || c.z <= cam.nearClipPlane || d.z <= cam.nearClipPlane) continue;
                    px += ClippedArea(new List<Vector2> { a, b, c, d }, W, H);
                }
                log.AppendLine($"  {name}: peak coverage {peakSum / n:0.000}  width >50% {widthSum / n:0.0} m  coverage-metres {meanSum / n:0.0}  | quads {quads}  fill {px / (W * H):0.00} screens/pass  build {buildMs:0.00} ms (editor)");
            }
            foreach (double age in ages)
            {
                Vector3 at = CoordinateMap.ToUnity(PosAt(Total - age) + Drift(age) + new Vec3(0, 0, 0.6));
                Vector3 eye = at + CoordinateMap.ToUnity(Right * 80.0) + Vector3.down * 35f;   // (looking up ~24°: mostly sky behind)
                // (age 0: the aeroplane in the frame, the first ~25 m of trail behind it)
                if (age < 0.5) { at = CoordinateMap.ToUnity(PosAt(Total) + new Vec3(-ux, -uy, 0) * 14 + new Vec3(0, 0, 0.6)); eye = at + CoordinateMap.ToUnity(Right * 40.0) + Vector3.down * 1.5f; }
                Shot($"age{age:0}", eye, at, true);
            }
            // Close: 30 m abeam of the 10 s stretch, as a chase camera sees its own smoke coming back round a loop or a turn.
            {
                Vector3 at = CoordinateMap.ToUnity(PosAt(Total - 10) + Drift(10) + new Vec3(0, 0, 0.6));
                Shot("close10", at + CoordinateMap.ToUnity(Right * 30.0) + Vector3.down * 8f, at, true);
            }
            // Down onto "water": an alpha-blended plane (alpha 0.85, as FlyingGame/Water blends) under the 20 s stretch.
            {
                Vector3 at = CoordinateMap.ToUnity(PosAt(Total - 20) + Drift(20));
                var water = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(water.GetComponent<Collider>());
                water.transform.SetPositionAndRotation(at + Vector3.down * 40f, Quaternion.Euler(90, 0, 0)); water.transform.localScale = Vector3.one * 600f;
                water.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Transparent")) { color = new Color(0.08f, 0.30f, 0.38f, 0.85f) };
                Shot("water20", at + Vector3.up * 90f + CoordinateMap.ToUnity(Right * 30.0), at, false);
                Object.DestroyImmediate(water);
            }
            File.WriteAllText(Path.Combine(outDir, $"{tag}_stats.txt"), log.ToString());
            Debug.Log("[SmokeAgeRender]\n" + log);
            RenderTexture.active = null;
            QualitySettings.lodBias = savedLodBias;
            Atmosphere.SteadyWind = savedWind;
            WorldTerrain.Active = null;
        }

        /// <summary>A screen-space polygon's area inside the W × H frame (Sutherland–Hodgman against each edge): the pixels the
        /// GPU actually fills, not the projected quad, which runs to thousands of screens for a puff near the frame's side.</summary>
        private static double ClippedArea(List<Vector2> poly, float W, float H)
        {
            List<Vector2> Clip(List<Vector2> pts, System.Func<Vector2, float> inside)
            {
                var o = new List<Vector2>();
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 p = pts[i], q = pts[(i + 1) % pts.Count]; float fp = inside(p), fq = inside(q);
                    if (fp >= 0) o.Add(p);
                    if ((fp >= 0) != (fq >= 0)) o.Add(p + (q - p) * (fp / (fp - fq)));
                }
                return o;
            }
            poly = Clip(poly, v => v.x); poly = Clip(poly, v => W - v.x); poly = Clip(poly, v => v.y); poly = Clip(poly, v => H - v.y);
            double area = 0; for (int i = 0; i < poly.Count; i++) { Vector2 p = poly[i], q = poly[(i + 1) % poly.Count]; area += p.x * q.y - q.x * p.y; }
            return 0.5 * System.Math.Abs(area);
        }

        private static Color32[] Shoot(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Color32[] px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }
    }
}

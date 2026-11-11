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
    /// Airshow smoke review plates (2026-11-04, lane/claude-smoke-volume). The island as the game builds it (WorldBuilder,
    /// SunsetLighting day, forest round the field), the Pitts on its own model, and the smoke drawn by the game's own
    /// <see cref="SmokeTrail"/> exactly as <see cref="SmokeController"/> drives it — the same exhausts, airframe flow, jet and
    /// material — from a scripted flight: an 8 s pass down the longest paved runway at 60 m/s and 150 m, a 140 m loop, and
    /// 6 s straight out. Three views at the moment it exits the loop, each also shot without the smoke, and
    /// what the smoke cost in that frame (puffs, quad pixels × passes) and how much it changed the picture.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.SmokeVolumeRender.Render -smokeTag before
    /// Writes &lt;tag&gt;_&lt;view&gt;.png and &lt;tag&gt;_stats.txt to -smokeOut (default build/smoke-volume).
    /// </summary>
    public static class SmokeVolumeRender
    {
        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            string tag = Arg("-smokeTag", "after");
            string outDir = Path.GetFullPath(Arg("-smokeOut", Path.Combine(Application.dataPath, "../../build/smoke-volume")));
            Directory.CreateDirectory(outDir);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            var terrain = new WorldTerrain();
            WorldTerrain.Active = terrain;
            WorldClock.TimeS = 20;
            Atmosphere.SteadyWind = Vec3.Zero;
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

            // The flight: down the longest paved runway, loop over its middle.
            IslandField field = null;
            foreach (var f in IslandTerrain.Fields) if (StripFinish.Painted(f.Surface) && (field == null || f.Length > field.Length)) field = f;
            double hdg = field.Heading * System.Math.PI / 180, ux = System.Math.Cos(hdg), uy = System.Math.Sin(hdg);
            double cx = field.Centre.X, cy = field.Centre.Y, baseUp = field.Elevation + 150;
            // The show line runs parallel to the runway, 350 m to its left; the crowd stands on the runway's right edge.
            double line = -350, crowdSide = field.Width / 2 + 25;
            const double V = 60, R = 140, Straight1 = 8, Straight2 = 6;
            double loopT = 2 * System.Math.PI * R / V, total = Straight1 + loopT + Straight2;
            // (along, up, pitch) at time t; the loop starts at along = 0, over the runway centre.
            (double along, double up, double pitch) At(double t)
            {
                if (t < Straight1) return (-V * (Straight1 - t), 0, 0);
                if (t < Straight1 + loopT) { double ph = (t - Straight1) * V / R; return (R * System.Math.Sin(ph), R * (1 - System.Math.Cos(ph)), ph); }
                return (V * (t - Straight1 - loopT), 0, 0);
            }
            bool InLoop(double t) => t >= Straight1 && t < Straight1 + loopT;
            Quat heading = new Quat(0, 0, System.Math.Sin(hdg / 2), System.Math.Cos(hdg / 2));
            Vec3 PosAt(double t) { var a = At(t); return new Vec3(cx + ux * a.along - uy * line, cy + uy * a.along + ux * line, -(baseUp + a.up)); }
            Quat AttAt(double t) { double p = At(t).pitch; return Quat.Multiply(heading, new Quat(0, System.Math.Sin(p / 2), 0, System.Math.Cos(p / 2))); }

            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = true;
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets("pitts-s2b-like");
            var plane = new GameObject("pitts");
            var builder = new AirframeBuilder();
            builder.Build(plane.transform, cfg);
            plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(PosAt(total)), CoordinateMap.ToUnity(AttAt(total)));

            // The smoke, as SmokeController builds it.
            var smokeRoot = new GameObject("SmokeRoot");
            var smoke = new SmokeTrail(smokeRoot.transform, 0);
            float lengthM = (float)cfg.Fuselage.Crossflow.LengthM, bodyRadiusM = (float)cfg.Fuselage.Crossflow.BodyRadiusM;
            var ex = SmokeTrail.ExhaustsFor(cfg, "pitts-s2b-like", builder.PropHubsBody(), lengthM, bodyRadiusM, out var jet);
            smoke.SetExhausts(ex, jet);
            smoke.Flow = new FlyingGame.Core.Aero.AirframeFlow(cfg, 0.4 * lengthM, bodyRadiusM);
            smoke.ApplyConfig(cfg);
            Atmosphere.SimTimeSec = total;
            // -smokePowers idle,cruise,full: the same flight at each power (idle 700 rpm closed, cruise 2,400 rpm 0.65, full
            // 2,700 rpm open), files <power>_<view>.png. Without it, one "show" setting (0.85, 2,565 rpm) as <tag>_<view>.png.
            var powers = new List<(string name, double rpm, double thr)>();
            foreach (string pw in Arg("-smokePowers", "show").Split(','))
                powers.Add(pw switch { "idle" => ("idle", cfg.Propulsion.IdleRpm, 0.0), "cruise" => ("cruise", 2400.0, 0.65),
                                       "full" => ("full", cfg.Propulsion.MaxRpm, 1.0), _ => ("show", cfg.Propulsion.MaxRpm * 0.95, 0.85) });
            List<SmokeTrail.Sample> History(double rpm, double thr)
            {
                float flow = (float)(0.25 + 0.75 * thr);
                var h = new List<SmokeTrail.Sample>();
                for (double t = 0; t <= total + 1e-9; t += 1.0 / 60)
                {
                    double alt = baseUp + At(t).up, rho = Atmosphere.DensityAtAltitude(alt);
                    h.Add(new SmokeTrail.Sample { T = t, Pos = PosAt(t), Att = AttAt(t), VelBody = new Vec3(V, 0, 0),
                        Rates = InLoop(t) ? new Vec3(0, V / R, 0) : Vec3.Zero, Flow = flow, AirDensity = rho,
                        JetU0 = FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cfg.Smoke, cfg.Propulsion, rpm, thr, rho),
                        Crank = rpm / 60.0 * t, Rpm = (float)rpm });
                }
                return h;
            }

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.nearClipPlane = .3f; cam.farClipPlane = 80000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var forest = new GameObject("ForestView");
            ForestBuilder.BuildAround(terrain, forest.transform, cx, cy, 2600, 800);

            Vector3 S(double along, double side, double up) => WorldBuilder.U(cx + ux * along - uy * side, cy + uy * along + ux * side, up);
            Vector3 loopCentre = S(0, line, baseUp + R);
            Vector3 planeU = plane.transform.position;
            Vector3 planeFwd = plane.transform.forward;
            var views = new (string name, Vector3 eye, Vector3 look, float fov)[]
            {
                // A spectator on the runway edge, eye height, ~400 m from the show line.
                ("crowd", S(-60, crowdSide, field.Elevation + 1.8), loopCentre + Vector3.down * 30, 60f),
                // Wide: 650 m abeam of the line, 60 m above the loop's base, the whole figure.
                ("wide", S(-80, line + 650, baseUp + 60), loopCentre, 45f),
                // Close: the aeroplane leaving the loop, 45 m off its right side and a little behind, the smoke streaming back.
                ("close", planeU - planeFwd * 18f + plane.transform.right * 45f + Vector3.up * 4f, planeU - planeFwd * 25f, 45f),
                // Near: 9 m off the right side, level with the exhaust, the aeroplane at the right and the first 20 m of smoke.
                ("near", planeU - planeFwd * 5f + plane.transform.right * 9f + Vector3.down * 0.4f, planeU - planeFwd * 8f + Vector3.down * 0.8f, 60f),
            };

            var log = new System.Text.StringBuilder();
            log.AppendLine($"tag {tag}  flight: {Straight1}s straight + loop R {R} m ({loopT:0.0}s) + {Straight2}s straight at {V} m/s, shown at t = {total:0.0}s, {cfg.Smoke.Cylinders} cylinders");
            var smokeMesh = smokeRoot.GetComponentInChildren<MeshFilter>(true);
            var smokeMat = smokeRoot.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            int passes = smokeMat.HasProperty("_CaptureAlpha") && smokeMat.GetFloat("_CaptureAlpha") < 0.5f ? 1 : smokeMat.passCount;
            foreach (var pw in powers)
            {
            var hist = History(pw.rpm, pw.thr);
            // A fresh airframe-flow cache per power: it is keyed by sample time, and every power flies the same times.
            smoke.SetExhausts(ex, jet);
            string prefix = pw.name == "show" ? tag : pw.name;
            log.AppendLine($"{pw.name}: {pw.rpm:0} rpm, throttle {pw.thr:0.00}, mean exit {hist[^1].JetU0:0.0} m/s, {FlyingGame.Core.Aero.ExhaustPulse.PulseHz(cfg.Smoke.Cylinders, pw.rpm):0} pulses/s");
            foreach (var v in views)
            {
                cam.fieldOfView = v.fov;
                cam.transform.position = v.eye; cam.transform.rotation = Quaternion.LookRotation(v.look - v.eye, Vector3.up);
                smoke.Build(k => { int i = hist.Count - 1 - k; return i < 0 ? (SmokeTrail.Sample?)null : hist[i]; }, total, p => CoordinateMap.ToUnity(p), cam);
                Color32[] with = Shoot(cam, rt, Path.Combine(outDir, $"{prefix}_{v.name}.png"));
                smokeMesh.gameObject.SetActive(false);
                Color32[] without = Shoot(cam, rt, Path.Combine(outDir, $"nosmoke_{v.name}.png"));
                smokeMesh.gameObject.SetActive(true);

                // Cost: puffs, and every quad's screen area in pixels (the fill the GPU pays), times the passes drawn.
                Mesh m = smokeMesh.sharedMesh; var vs = m.vertices; int quads = vs.Length / 4; double px = 0;
                for (int q = 0; q < quads; q++)
                {
                    Vector3 a = cam.WorldToScreenPoint(vs[4 * q]), b = cam.WorldToScreenPoint(vs[4 * q + 1]), c = cam.WorldToScreenPoint(vs[4 * q + 2]), d = cam.WorldToScreenPoint(vs[4 * q + 3]);
                    if (a.z <= 0 || b.z <= 0 || c.z <= 0 || d.z <= 0) continue;
                    double area = 0.5 * System.Math.Abs((a.x * b.y - b.x * a.y) + (b.x * c.y - c.x * b.y) + (c.x * d.y - d.x * c.y) + (d.x * a.y - a.x * d.y));
                    // clipped to the screen, roughly: no quad can cost more than the screen
                    px += System.Math.Min(area, rt.width * rt.height);
                }
                // Look: how far the smoke moved each pixel it touched (0-255), its mean and its 95th percentile.
                var deltas = new List<int>();
                for (int i = 0; i < with.Length; i++)
                {
                    int dd = System.Math.Max(System.Math.Abs(with[i].r - without[i].r), System.Math.Max(System.Math.Abs(with[i].g - without[i].g), System.Math.Abs(with[i].b - without[i].b)));
                    if (dd > 3) deltas.Add(dd);
                }
                deltas.Sort();
                double mean = 0; foreach (int dd in deltas) mean += dd; mean = deltas.Count > 0 ? mean / deltas.Count : 0;
                int p95 = deltas.Count > 0 ? deltas[(int)(0.95 * (deltas.Count - 1))] : 0, max = deltas.Count > 0 ? deltas[^1] : 0;
                double screen = rt.width * rt.height;
                log.AppendLine($"  {v.name}: quads {quads} (tubes {smoke.TubeCount})  quad px {px / screen:0.00} screens × {passes} passes = fill {px * passes / screen:0.00} screens  " +
                               $"| smoke covers {100.0 * deltas.Count / screen:0.0}% of the frame, change mean {mean:0.0} p95 {p95} max {max} (of 255)");
            }
            }
            File.WriteAllText(Path.Combine(outDir, $"{tag}_stats.txt"), log.ToString());
            Debug.Log("[SmokeVolumeRender]\n" + log);
            RenderTexture.active = null;
            QualitySettings.lodBias = savedLodBias;
            WorldTerrain.Active = null;
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

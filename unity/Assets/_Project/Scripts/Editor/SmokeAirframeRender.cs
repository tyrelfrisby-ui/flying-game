using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim.Replay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Evidence for Ty t912u / t914u (2026-11-06). Two parts, one batch run:
    ///
    /// AIRFRAME: each type on its own drawn model, held in a steady state (upright and inverted flat spin, high α, cruise,
    /// inverted cruise) for 6 s with smoke on, the game's SmokeTrail built as SmokeController builds it, twice: BEFORE (the
    /// analytic body, no clearance — the code Ty flew) and AFTER (the fuselage measured on the drawn model, and every piece
    /// near the airframe held out of it). For each, an INDEPENDENT count against the real mesh: MeshColliders on the model,
    /// and a piece counts as "through" when its spine crosses a skin (Physics.Linecast) or its centre is enclosed (rays in
    /// all six directions hit the model). Renders: from above the cowling (the report's view) and abeam.
    ///
    /// REPLAY: a recorded pass with the smoke switched on 2–6 s, shown at the 9 s play head in AS FLOWN / ON / OFF, the way
    /// SmokeController.BuildReplay draws it; and the same frame through a clip camera (Camera.CopyFrom the main one, as
    /// ClipRecorder does) compared pixel for pixel.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.SmokeAirframeRender.Render [-smokeOut dir] [-smokeTypes a,b]
    /// </summary>
    public static class SmokeAirframeRender
    {
        private const int W = 1280, H = 800;

        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            string outDir = Path.GetFullPath(Arg("-smokeOut", Path.Combine(Application.dataPath, "../../renders/smoke-airframe")));
            string[] types = Arg("-smokeTypes", "pitts-s2b-like,extra-300-like,great-lakes-like,c172-like,j3-cub-like,decathlon-8kcab-like,glider-2-33-like,glider-grob103-acro-like").Split(',');
            Directory.CreateDirectory(outDir);
            var log = new System.Text.StringBuilder();
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;
                Atmosphere.SteadyWind = Vec3.Zero;
                var camGo = new GameObject("RenderCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.42f, 0.6f, 0.85f, 1f);
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 5000f; cam.fieldOfView = 40f;
                var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
                bool savedBack = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;

                foreach (string id in types) if (id.Trim().Length > 0 && id.Trim() != "none") Airframe(id.Trim(), cam, rt, outDir, log);
                Replay(cam, rt, outDir, log);
                Physics.queriesHitBackfaces = savedBack;
            }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); exit = 1; Debug.LogException(e); }
            File.WriteAllText(Path.Combine(outDir, "stats.txt"), log.ToString());
            Debug.Log("[SmokeAirframeRender]\n" + log);
            EditorApplication.Exit(exit);
        }

        private struct State { public string Name; public Quat Att0; public Vec3 V, W; }

        private static IEnumerable<State> States()
        {
            Vec3 At(double v, double aDeg, double side = 0) { double a = aDeg * System.Math.PI / 180; return new Vec3(v * System.Math.Cos(a), side, v * System.Math.Sin(a)); }
            Quat Roll(double deg) { double h = deg * System.Math.PI / 360; return new Quat(System.Math.Sin(h), 0, 0, System.Math.Cos(h)); }
            Quat Pitch(double deg) { double h = deg * System.Math.PI / 360; return new Quat(0, System.Math.Sin(h), 0, System.Math.Cos(h)); }
            yield return new State { Name = "upright-flat", Att0 = Pitch(4), V = At(25, 93, 1.5), W = new Vec3(0.2, -0.3, -3.5) };   // Pitts approved: α 92.9°, θ +3.9°, 201 deg/s
            yield return new State { Name = "inverted-flat", Att0 = Roll(180), V = At(25, -88, -1.5), W = new Vec3(-0.2, 0.3, 3.3) };
            yield return new State { Name = "high-alpha", Att0 = Pitch(15), V = At(30, 30), W = new Vec3(0, 0.05, 0) };
            yield return new State { Name = "cruise", Att0 = Quat.Identity, V = At(55, 3), W = Vec3.Zero };
            yield return new State { Name = "inverted-cruise", Att0 = Roll(180), V = At(50, -6), W = Vec3.Zero };
        }

        private static Quat Turn(Vec3 w, double t)
        {
            double a = w.Length * t; if (a < 1e-12) return Quat.Identity;
            Vec3 u = w * (1.0 / w.Length); double s = System.Math.Sin(a / 2);
            return new Quat(u.X * s, u.Y * s, u.Z * s, System.Math.Cos(a / 2));
        }

        private static void Airframe(string id, Camera cam, RenderTexture rt, string outDir, System.Text.StringBuilder log)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            var plane = new GameObject(id);
            var builder = new AirframeBuilder();
            builder.Build(plane.transform, cfg);
            Transform model = builder.ModelRoot != null ? builder.ModelRoot : plane.transform;
            var hubs = new List<Transform>(); for (int i = 0; ; i++) { Transform h = builder.PropHubTransform(i); if (h == null) break; hubs.Add(h); }
            // Colliders on everything the smoke must keep out of (not the propellers), for the independent count.
            var colliders = new List<MeshCollider>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.GetComponent<MeshRenderer>() == null || !mf.GetComponent<MeshRenderer>().enabled) continue;
                bool prop = false;
                for (Transform t = mf.transform; t != null && t != plane.transform && !prop; t = t.parent)
                {
                    foreach (var h in hubs) if (t == h) prop = true;
                    string n = t.name.ToLowerInvariant(); if (n.Contains("prop") || n.Contains("blade") || n.Contains("disc")) prop = true;
                }
                if (prop) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; colliders.Add(mc);
            }
            float lengthM = (float)(cfg.Fuselage?.Crossflow?.LengthM > 0 ? cfg.Fuselage.Crossflow.LengthM : 7), bodyR = (float)(cfg.Fuselage?.Crossflow?.BodyRadiusM > 0 ? cfg.Fuselage.Crossflow.BodyRadiusM : 0.5);
            var ex = SmokeTrail.ExhaustsFor(cfg, id, builder.PropHubsBody(), lengthM, bodyR, out var jet);
            // Measured on the model at the identity pose (as SmokeController does once the model is placed).
            plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            List<Vec3> pts = SmokeController.ModelPoints(plane.transform, model, hubs);
            bool measured = FlyingGame.Core.Aero.AirframeFlow.MeasureFuselage(pts, cfg, out double[] xs, out double[] rs, out double[] zs);
            log.AppendLine($"{id}: model {(builder.ModelRoot != null ? builder.ModelRoot.name : "procedural")}, {pts.Count} points, {colliders.Count} colliders, fuselage {(measured ? $"measured {xs.Length} stations, max r {System.Linq.Enumerable.Max(rs):0.00} m" : "NOT measured (analytic)")}, exhausts {ex.Length}");

            var smokeRoot = new GameObject("SmokeRoot");
            foreach (State st in States())
            {
                // The flight: steady V, ω in body axes, 6 s at 60 Hz, from 300 m.
                const double T = 6, Hz = 60;
                var hist = new List<SmokeTrail.Sample>();
                Vec3 pos = new(0, 0, -300); double rho = Atmosphere.DensityAtAltitude(300), rpm = cfg.Propulsion != null ? cfg.Propulsion.MaxRpm * 0.6 : 0;
                for (int k = 0; k <= T * Hz; k++)
                {
                    double t = k / Hz;
                    Quat att = Quat.Multiply(st.Att0, Turn(st.W, t));
                    if (k > 0) pos += att.Rotate(st.V) * (1 / Hz);
                    hist.Add(new SmokeTrail.Sample { T = t, Pos = pos, Att = att, VelBody = st.V, Rates = st.W, Flow = 0.85f, AirDensity = rho,
                        JetU0 = cfg.Propulsion != null && cfg.Smoke != null ? FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cfg.Smoke, cfg.Propulsion, rpm, 0.6, rho) : 0,
                        Crank = rpm / 60.0 * t, Rpm = cfg.Propulsion != null && cfg.Propulsion.PropDiameterM > 0 ? (float)rpm : 0f });
                }
                SmokeTrail.Sample now = hist[^1];
                plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(now.Pos), CoordinateMap.ToUnity(now.Att));
                Physics.SyncTransforms();
                Vector3 cg = plane.transform.position;
                Vector3 fwd = CoordinateMap.ToUnity(now.Att.Rotate(new Vec3(1, 0, 0))), top = CoordinateMap.ToUnity(now.Att.Rotate(new Vec3(0, 0, -1))), right = CoordinateMap.ToUnity(now.Att.Rotate(new Vec3(0, 1, 0)));
                Vector3 cowl = cg + fwd * 0.8f + top * 0.3f;
                foreach (bool after in new[] { false, true })
                {
                    foreach (Transform c in smokeRoot.transform) Object.DestroyImmediate(c.gameObject);
                    var trail = new SmokeTrail(smokeRoot.transform, 0);
                    trail.SetExhausts(ex, jet);
                    trail.Flow = new FlyingGame.Core.Aero.AirframeFlow(cfg, 0.4 * lengthM, bodyR);
                    if (after && measured) trail.Flow.SetFuselage(xs, rs, zs);
                    trail.ClearAirframe = after;
                    trail.PipeDiameterM = (cfg.Smoke?.PipeDiameterIn ?? 4) * 0.0254;
                    trail.Stream = cfg.Propulsion == null;
                    trail.Cylinders = cfg.Smoke?.Cylinders ?? 6;
                    cam.transform.position = cowl + top * 3.2f + fwd * 2.2f + right * 1.6f;
                    cam.transform.rotation = Quaternion.LookRotation(cowl - cam.transform.position, top);
                    System.Func<int, SmokeTrail.Sample?> sample = k => { int i = hist.Count - 1 - k; return i < 0 ? (SmokeTrail.Sample?)null : hist[i]; };
                    trail.Build(sample, now.T, p => CoordinateMap.ToUnity(p), cam);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int r = 0; r < 5; r++) trail.Build(sample, now.T, p => CoordinateMap.ToUnity(p), cam);
                    double ms = sw.Elapsed.TotalMilliseconds / 5;
                    var (through, near, worst) = Through(trail, plane.transform, colliders);
                    string tag = $"{id}_{st.Name}_{(after ? "after" : "before")}";
                    Shoot(cam, rt, Path.Combine(outDir, tag + "_top.png"));
                    cam.transform.position = cg + right * 7f + top * 1.0f; cam.transform.rotation = Quaternion.LookRotation(cg - cam.transform.position, top);
                    trail.Build(sample, now.T, p => CoordinateMap.ToUnity(p), cam);
                    Shoot(cam, rt, Path.Combine(outDir, tag + "_side.png"));
                    log.AppendLine($"  {st.Name,-15} {(after ? "AFTER " : "BEFORE")}: pieces near the airframe {near,4}, THROUGH the drawn model {through,4}{(worst.Length > 0 ? "  (" + worst + ")" : "")}" +
                                   $"  | cleared tested {trail.ClearTested} trimmed {trail.ClearTrimmed} | build {ms:0.00} ms (editor)");
                    if (after && through > 0) log.AppendLine($"    !! {tag}: {through} pieces still cross the drawn model");
                }
            }
            Object.DestroyImmediate(smokeRoot);
            Object.DestroyImmediate(plane);
        }

        /// <summary>The independent count: pieces (alpha over 0.02) whose spine crosses the drawn model's skin, or whose centre is
        /// enclosed by it, among those within 8 m of the CG.</summary>
        private static (int through, int near, string worst) Through(SmokeTrail trail, Transform plane, List<MeshCollider> cols)
        {
            int through = 0, near = 0;
            var names = new Dictionary<string, int>();
            Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var pc in trail.Pieces)
            {
                if (pc.a < 0.02f || (pc.p - plane.position).magnitude > 8f) continue;
                near++;
                float spine = 0.5f * Mathf.Max(0f, pc.l - pc.w);
                Vector3 a = pc.p - pc.axis * spine, b = pc.p + pc.axis * spine;
                string hitName = null;
                if (spine > 1e-3f && Physics.Linecast(a, b, out RaycastHit hit)) hitName = hit.collider.name;
                else
                {
                    int enclosed = 0; string n = null;
                    foreach (var d in dirs) if (Physics.Raycast(pc.p, d, out RaycastHit h2, 6f)) { enclosed++; n = h2.collider.name; }
                    if (enclosed == 6) hitName = "enclosed:" + n;
                }
                if (hitName == null) continue;
                through++;
                names[hitName] = names.TryGetValue(hitName, out int c) ? c + 1 : 1;
            }
            var top = new List<KeyValuePair<string, int>>(names); top.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count && i < 4; i++) sb.Append($"{top[i].Key} {top[i].Value}; ");
            return (through, near, sb.ToString());
        }

        private static void Replay(Camera cam, RenderTexture rt, string outDir, System.Text.StringBuilder log)
        {
            const string id = "pitts-s2b-like";
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            var plane = new GameObject(id);
            var builder = new AirframeBuilder();
            builder.Build(plane.transform, cfg);
            // A recording: a straight pass at 50 m/s; the switch on from 2 s to 6 s.
            var rec = new FlightRecorder(30, 600);
            double rho = Atmosphere.DensityAtAltitude(300), rpm = cfg.Propulsion.MaxRpm * 0.8;
            for (int i = 0; i <= 300; i++)
            {
                double t = i / 30.0;
                var s = new RigidBodyState(new Vec3(50 * t, 0, -300), Quat.Identity, new Vec3(50, 0, 0), Vec3.Zero);
                var sm = new ReplaySmoke(t >= 2 && t < 6, 0.85f, FlyingGame.Core.Aero.ExhaustJet.ExitVelocity(cfg.Smoke, cfg.Propulsion, rpm, 0.8, rho), rho, rpm / 60.0 * t, (float)rpm);
                rec.Record(new ReplayFrame(t, s, new ControlDeflections(0, 0, 0, 0), 0.8, 0, 1, 1, Vec3.Zero, sm));
            }
            const double Head = 9;
            ReplayFrame hf = rec.Sample(Head);
            plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(hf.State.Position), CoordinateMap.ToUnity(hf.State.Attitude));
            var smokeRoot = new GameObject("SmokeRoot");
            var trail = new SmokeTrail(smokeRoot.transform, 0);
            float lengthM = (float)cfg.Fuselage.Crossflow.LengthM, bodyR = (float)cfg.Fuselage.Crossflow.BodyRadiusM;
            var ex = SmokeTrail.ExhaustsFor(cfg, id, builder.PropHubsBody(), lengthM, bodyR, out var jet);
            trail.SetExhausts(ex, jet); trail.Flow = new FlyingGame.Core.Aero.AirframeFlow(cfg, 0.4 * lengthM, bodyR);
            trail.PipeDiameterM = cfg.Smoke.PipeDiameterIn * 0.0254; trail.Cylinders = cfg.Smoke.Cylinders;
            Vector3 mid = CoordinateMap.ToUnity(new Vec3(50 * 6.5, 0, -300));
            cam.transform.position = mid + new Vector3(-110f, -12f, 0f); cam.transform.rotation = Quaternion.LookRotation(mid - cam.transform.position, Vector3.up);
            cam.fieldOfView = 50f;
            var pts = new List<ReplaySmokePoint>(); var samples = new List<SmokeTrail.Sample>();
            var clipRt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var clipGo = new GameObject("ClipCam"); var clip = clipGo.AddComponent<Camera>();
            // (the old replay: SmokeController returned as soon as the replay began, so nothing was drawn)
            trail.Hide();
            Shoot(cam, rt, Path.Combine(outDir, "replay_before_oldcode.png"));
            // Ty t982u: AS FLOWN, or MANUAL with its own SMOKE switch from the play head on. Each state is the control as a
            // pilot would leave it after these presses (play-head times in seconds), shown at the 9 s head.
            var states = new System.Collections.Generic.List<(string name, System.Action<ReplaySmokeControl> set)>
            {
                ("as_flown", c => c.ReturnToAsFlown()),
                ("manual_entered_at_9", c => { c.ReturnToAsFlown(); c.EnterManual(rec, 9); }),
                ("manual_off_from_4", c => { c.ReturnToAsFlown(); c.EnterManual(rec, 4); c.Toggle(rec, 4); }),
                ("manual_on_from_7", c => { c.ReturnToAsFlown(); c.EnterManual(rec, 7); c.Toggle(rec, 7); }),
                ("returned_as_flown", c => { c.EnterManual(rec, 4); c.Toggle(rec, 4); c.ReturnToAsFlown(); }),
            };
            var ctl = new ReplaySmokeControl();
            Color32[] asFlownPx = null;
            foreach (var (name0, set) in states)
            {
                set(ctl);
                bool any = ReplaySmokeView.Window(rec, Head, ctl, false, SmokeTrail.LifeMaxS, pts);
                if (!any) trail.Hide();
                else
                {
                    samples.Clear();
                    foreach (var p in pts) samples.Add(new SmokeTrail.Sample { T = p.T, Pos = p.Pos, Att = p.Att, VelBody = p.VelBody, Rates = p.Rates, Flow = p.Flow, JetU0 = p.JetU0, AirDensity = p.AirDensity, Crank = p.Crank, Rpm = p.Rpm });
                    trail.Build(k => k < samples.Count ? samples[k] : (SmokeTrail.Sample?)null, pts[0].T, p => CoordinateMap.ToUnity(p), cam);
                }
                string name = "replay_" + name0;
                Color32[] main = Shoot(cam, rt, Path.Combine(outDir, name + ".png"));
                clip.CopyFrom(cam); clip.targetTexture = clipRt;   // as ClipRecorder: a copy of the main camera
                Color32[] fromClip = Shoot(clip, clipRt, Path.Combine(outDir, name + "_clipcam.png"));
                int diff = 0; for (int i = 0; i < main.Length; i++) if (main[i].r != fromClip[i].r || main[i].g != fromClip[i].g || main[i].b != fromClip[i].b) diff++;
                int vsAsFlown = -1;
                if (name0 == "as_flown") asFlownPx = main;
                else if (asFlownPx != null) { vsAsFlown = 0; for (int i = 0; i < main.Length; i++) if (main[i].r != asFlownPx[i].r || main[i].g != asFlownPx[i].g || main[i].b != asFlownPx[i].b) vsAsFlown++; }
                int smoking = 0; foreach (var p in pts) if (p.Flow > 0.001f) smoking++;
                log.AppendLine($"replay {name0,-20}: manual {ctl.Manual}, switch at head {(ctl.OnAt(rec, Head) ? "ON" : "off")}, window {pts.Count} samples ({smoking} smoking), drawing {(any ? trail.Pieces.Count : 0)} pieces; " +
                               $"clip camera differs in {diff} of {main.Length} px{(vsAsFlown >= 0 ? $"; differs from AS FLOWN in {vsAsFlown} px" : "")}");
            }
            Object.DestroyImmediate(clipGo); Object.DestroyImmediate(smokeRoot); Object.DestroyImmediate(plane);
            cam.fieldOfView = 40f;
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

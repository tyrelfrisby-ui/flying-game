using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-06 06:34 AKST (t915u): "the pitts crash still fails to follow the occupants portion of the wreck."
    ///
    /// The chase camera follows the aeroplane's root (SceneBootstrap: chase.Target = aircraft.transform), and what stays
    /// drawn on that root after a break-up is whatever the runtime split (<see cref="AirframeVisual.DetachStatic"/>) did not
    /// cut off. This breaks each aeroplane up through that same path — a mid-fuselage break (nose and tail boom off), and
    /// sim-driven crashes whose lost parts (and tail / nose bends, which cut at the same station) are applied as they
    /// happen — and measures, for every seat, how much of the drawn geometry around it (the occupant's box: 0.35 m aft to
    /// 0.25 m ahead of the seat's mass station, 0.3 m either side, from 0.15 m under it to 0.8 m over it) stays on the root
    /// and how much went into each piece. FAIL if a nose or tail-boom piece takes more than 5 % of a seat, or the root keeps
    /// less than 80 %. Renders the camera's view of the root (pieces laid aside) to build/crash-cam-occupants/.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CrashCamOccupantShots.Run
    /// The log also reports, for comparison, how much of each seat lay beyond the cut the visual used before this fix
    /// (wing root only, no CG offset).
    /// </summary>
    public static class CrashCamOccupantShots
    {
        private const int W = 1280, H = 800;

        private sealed class Seat { public string Id; public Vec3 Station; public int Base; }

        public static void Run()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/crash-cam-occupants"));
            Directory.CreateDirectory(outDir);
            var log = new System.Text.StringBuilder();
            bool pass = true;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;
                WorldTerrain.Active = new WorldTerrain();
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 800f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.7f, 0.95f);
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.position = new Vector3(0f, -1.6f, 0f);
                ground.transform.localScale = Vector3.one * 8f;
                ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.42f, 0.5f, 0.3f) };

                foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "c172-like" })
                {
                    pass &= Scenario(id, "breakup", null, cam, rt, outDir, log);
                    if (id == "pitts-s2b-like")
                    {
                        pass &= Scenario(id, "crashtest-grass", c => CrashScenario.PittsStart(c, true), cam, rt, outDir, log);
                        pass &= Scenario(id, "dive60", c => Drop(c, -60, 0, 40, 30), cam, rt, outDir, log);
                        pass &= Scenario(id, "tailstrike", c => Drop(c, 15, 0, 30, 15), cam, rt, outDir, log);
                    }
                }
                log.AppendLine(pass ? "[CrashCamOccupants] PASS" : "[CrashCamOccupants] FAIL");
            }
            catch (System.Exception ex) { pass = false; log.AppendLine("[CrashCamOccupants] FAILED " + ex); }
            finally
            {
                WorldTerrain.Active = null;
                RenderTexture.active = null;
                File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString());
                Debug.Log(log.ToString());
            }
        }

        /// <summary>Level-winged, pitched, moving forward and sinking, its lowest point 5 cm over flat ground at the origin.</summary>
        private static RigidBodyState Drop(AircraftConfig c, double pitchDeg, double rollDeg, double fwd, double sink)
        {
            WorldTerrain.Active = null;
            double th = pitchDeg * System.Math.PI / 180, ph = rollDeg * System.Math.PI / 180;
            Quat att = Quat.Multiply(new Quat(System.Math.Sin(ph / 2), 0, 0, System.Math.Cos(ph / 2)), new Quat(0, System.Math.Sin(th / 2), 0, System.Math.Cos(th / 2)));
            double lowest = double.MaxValue;
            foreach (var p in AirframeContact.BuildPoints(c)) lowest = System.Math.Min(lowest, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
            foreach (GearConfig g in c.Gear) lowest = System.Math.Min(lowest, -att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
            return new RigidBodyState(new Vec3(0, 0, lowest - 0.05), att, att.Conjugate().Rotate(new Vec3(fwd, 0, sink)), Vec3.Zero);
        }

        private static bool Scenario(string id, string name, System.Func<AircraftConfig, RigidBodyState> start, Camera cam, RenderTexture rt, string outDir, System.Text.StringBuilder log)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            Vec3 cg = cfg.Mass.CgVec();
            var root = new GameObject(id + "-" + name);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            var seats = cfg.Loading.Seats.Select(s => new Seat { Id = s.Id, Station = s.StationVec() }).ToList();
            foreach (Seat s in seats) s.Base = Count(root.transform, s.Station - cg);
            (double noseCut, double tailCut) = AirframeContact.FuselageStations(cfg);
            (double oldNose, double oldTail) = OldStations(cfg);
            log.AppendLine($"[CrashCamOccupants] {id} {name}: model {builder.PlacedModelPath ?? "(procedural)"}; cuts nose {noseCut:0.000} tail {tailCut:0.000} (before the fix the visual cut at {oldNose:0.000} / {oldTail:0.000} root-local)");
            foreach (Seat s in seats)
            {
                int oldTaken = Count(root.transform, s.Station - cg, z => z < oldTail || z > oldNose);
                log.AppendLine($"[CrashCamOccupants]   seat {s.Id} at x {s.Station.X:0.000}: {s.Base} vertices drawn around it; the old cut would have sent {oldTaken} ({Pct(oldTaken, s.Base)}) off with the nose or tail");
            }

            // What breaks, in the order it breaks.
            var order = new List<AirframeComponent>();
            if (start == null) order.AddRange(new[] { AirframeComponent.Nose, AirframeComponent.TailBoom });
            else
            {
                if (name.StartsWith("crashtest")) WorldTerrain.Active = new WorldTerrain();
                var ac = new Aircraft(cfg, start(cfg), ControlDeflections.Neutral);
                var relay = new CrashRelay(ac, subscribe: false);
                var sim = new SimLoop(ac);
                bool tailBent = false, noseBent = false;
                for (double t = 0; t < 3.0; t += 1.0 / 60)
                {
                    sim.RunFor(1.0 / 60, new ControlInputs(0, 0, 0, 1));
                    relay.Step(1.0 / 60);
                    // StructuralDamage turns a bend into AirframeVisual.BendPivot, which cuts at the same station.
                    if (!tailBent && System.Math.Abs(ac.Crush.TailBendPitchRad) + System.Math.Abs(ac.Crush.TailBendYawRad) > 0.003) { tailBent = true; order.Add(AirframeComponent.TailBoom); }
                    if (!noseBent && System.Math.Abs(ac.EngineSagRad) > 0.003) { noseBent = true; order.Add(AirframeComponent.Nose); }
                    foreach (AirframeComponent c in ac.LostComponents) if (!order.Contains(c)) order.Add(c);
                }
                log.AppendLine($"[CrashCamOccupants]   sim: lost [{string.Join(",", ac.LostComponents)}], tail bent {tailBent}, nose bent {noseBent}");
                WorldTerrain.Active = null;
            }

            bool ok = true;
            float lay = 0f;
            var pieces = new List<GameObject>();
            foreach (AirframeComponent c in order)
            {
                GameObject d = AirframeVisual.DetachStatic(builder, root.transform, cfg, c, Vector3.zero);
                if (d == null) continue;
                Object.DestroyImmediate(d.GetComponent<WingDebris>());
                bool section = c is AirframeComponent.Nose or AirframeComponent.TailBoom;
                foreach (Seat s in seats)
                {
                    int n = Count(d.transform, s.Station - cg);
                    if (n == 0) continue;
                    bool bad = section && n > 0.05 * s.Base;
                    ok &= !bad;
                    log.AppendLine($"[CrashCamOccupants]   {c} piece takes {n} of seat {s.Id}'s {s.Base} ({Pct(n, s.Base)}){(bad ? "  <<< the occupant's cockpit leaves with it" : "")}");
                }
                d.transform.position = root.transform.position + new Vector3(9f, 0f, 6f - lay); lay += 4f;
                pieces.Add(d);
            }
            foreach (Seat s in seats)
            {
                int kept = Count(root.transform, s.Station - cg);
                bool bad = kept < 0.8 * s.Base;
                ok &= !bad;
                log.AppendLine($"[CrashCamOccupants]   seat {s.Id}: {kept} of {s.Base} ({Pct(kept, s.Base)}) still on the camera's target{(bad ? "  <<< FAIL" : "")}");
            }

            // The chase camera's view of its Target (behind and above, centred on the root), and a close look from the side.
            Snap(cam, rt, root.transform, root.transform.position + new Vector3(0f, 3.5f, -11f), Path.Combine(outDir, $"{id}-{name}-chase.png"));
            Snap(cam, rt, root.transform, root.transform.position + new Vector3(-6.5f, 2.5f, -1.5f), Path.Combine(outDir, $"{id}-{name}-side.png"));
            Snap(cam, rt, null, root.transform.position + new Vector3(-14f, 9f, -8f), Path.Combine(outDir, $"{id}-{name}-pieces.png"), root.transform.position + new Vector3(5f, 0f, 0f));
            log.AppendLine($"[CrashCamOccupants]   {(ok ? "ok" : "FAIL")}");
            foreach (GameObject p in pieces) Object.DestroyImmediate(p);
            Object.DestroyImmediate(root);
            return ok;
        }

        private static string Pct(int n, int of) => of > 0 ? $"{100.0 * n / of:0}%" : "-";

        /// <summary>FuselageStations as it was before 2026-11-06 (off the wing root alone), in root-local z as the visual
        /// used it (no CG offset).</summary>
        private static (double, double) OldStations(AircraftConfig c)
        {
            double rootX = 0.3, chord = 1.4;
            foreach (SurfaceConfig sf in c.Surfaces)
            {
                string sid = sf.Id.ToLowerInvariant();
                if (!sid.Contains("wing") || sid.Contains("aileron") || sf.Strips.Count == 0) continue;
                StripConfig r = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1]) < System.Math.Abs(r.Pos[1])) r = st;
                rootX = r.Pos[0]; chord = r.Chord;
                break;
            }
            return (rootX + 0.25 * chord + 0.6, rootX - 0.75 * chord - 0.3);
        }

        /// <summary>Drawn vertices under <paramref name="frame"/> inside the occupant box about a seat (body offset from the
        /// CG), optionally only those whose frame-local z passes <paramref name="zFilter"/>.</summary>
        private static int Count(Transform frame, Vec3 seatFromCg, System.Func<float, bool> zFilter = null)
        {
            Vector3 s = CoordinateMap.ToUnity(seatFromCg);
            int n = 0;
            foreach (MeshFilter mf in frame.GetComponentsInChildren<MeshFilter>(false))
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled || !mf.sharedMesh.isReadable) continue;
                Matrix4x4 m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v0 in mf.sharedMesh.vertices)
                {
                    Vector3 v = m.MultiplyPoint3x4(v0);
                    if (v.z < s.z - 0.35f || v.z > s.z + 0.25f || Mathf.Abs(v.x - s.x) > 0.3f || v.y < s.y - 0.15f || v.y > s.y + 0.8f) continue;
                    if (zFilter != null && !zFilter(v.z)) continue;
                    n++;
                }
            }
            return n;
        }

        private static void Snap(Camera cam, RenderTexture rt, Transform target, Vector3 from, string path, Vector3? at = null)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at ?? target.position, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}

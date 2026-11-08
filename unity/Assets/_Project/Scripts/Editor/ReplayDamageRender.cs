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
    /// Ty pt1001 (2026-11-07, on 2026.1107.0822): "after a crash the replay begins with the wings already missing, before the
    /// actual crash later in the recording". The Pitts on its own model flies straight and level at 50 m/s; at 2.0 s its wings
    /// come off through the game's own path (AirframeVisual.DetachWingsStatic, then ReplayActors.RegisterAirframePart as
    /// AirframeVisual.DetachWings does) and fall; FlightReplay records the parts at 30 Hz (its own RecordParts, through
    /// ReviewRecordParts). Then, with the aeroplane posed as the replay poses it:
    ///   live_end                the live wreck at 4 s (wings off and fallen)
    ///   replay_1s_oldcode       the replay at 1 s as 0822 draws it: the aeroplane re-posed, the parts not (wings missing)
    ///   replay_1s               the replay at 1 s now (ShowParts): wings on
    ///   intact_1s               a second Pitts that never broke, at the same pose: the reference
    ///   replay_3s               1 s after the break: wings off, where they were recorded falling
    ///   replay_1s_after_seek    back to 1 s after showing 3 s (must equal replay_1s)
    ///   replay_1s_clipcam       a Camera.CopyFrom the main one (as ClipRecorder), at 1 s
    ///   live_returned           Exit's restore of the live parts (must equal live_end)
    /// and stats.txt with the pixel comparisons. Batch:
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.ReplayDamageRender.Render -damageOut DIR
    /// </summary>
    public static class ReplayDamageRender
    {
        private const int W = 1280, H = 800;

        public static void Render()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            string outDir = Path.GetFullPath(Arg("-damageOut", Path.Combine(Application.dataPath, "../../renders/replay-damage")));
            Directory.CreateDirectory(outDir);
            var log = new System.Text.StringBuilder();
            int exit = 0;
            try { Run(outDir, log); }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); exit = 1; Debug.LogException(e); }
            File.WriteAllText(Path.Combine(outDir, "stats.txt"), log.ToString());
            Debug.Log("[ReplayDamageRender]\n" + log);
            EditorApplication.Exit(exit);
        }

        private static void Run(string outDir, System.Text.StringBuilder log)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = true;
            const string id = "pitts-s2b-like";
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);

            AirframeBuilder lastBuilder = null;
            GameObject Build(string name)
            {
                var go = new GameObject(name);
                lastBuilder = new AirframeBuilder(); lastBuilder.Build(go.transform, cfg);
                return go;
            }
            GameObject plane = Build("pitts");
            AirframeBuilder builder = lastBuilder;
            var replayGo = new GameObject("ReplayFixture");
            FlightReplay replay = replayGo.AddComponent<FlightReplay>();

            const double V = 50, BreakT = 2.0, EndT = 4.0, Hz = 30;
            Vec3 PosAt(double t) => new(V * t, 0, -300);
            Quat att = Quat.Identity;
            void Pose(double t) => plane.transform.SetPositionAndRotation(CoordinateMap.ToUnity(PosAt(t)), CoordinateMap.ToUnity(att));

            // The flight, recorded at 30 Hz; the wings off at BreakT, falling and tumbling after it.
            GameObject left = null, right = null;
            Vector3 vel = CoordinateMap.ToUnity(new Vec3(V, 0, 0));
            Vector3 vL = vel + plane.transform.right * -4f + Vector3.up * 3f, vR = vel + plane.transform.right * 4f + Vector3.up * 3f;
            for (int k = 0; k <= (int)(EndT * Hz); k++)
            {
                double t = k / Hz, dt = 1 / Hz;
                Pose(t);
                if (left == null && t >= BreakT - 1e-9)
                {
                    (left, right) = AirframeVisual.DetachWingsStatic(builder, plane.transform, cfg, vel);
                    ReplayActors.RegisterAirframePart(left, plane.transform);    // as AirframeVisual.DetachWings does
                    ReplayActors.RegisterAirframePart(right, plane.transform);
                    log.AppendLine($"break at {t:0.000} s: left {(left != null ? left.transform.childCount + " pieces" : "none")}, right {(right != null ? right.transform.childCount + " pieces" : "none")}");
                }
                else if (left != null)
                {
                    // (WingDebris.Update does not run in edit mode: the same fall, by hand)
                    vL += Vector3.down * (float)(9.81 * dt); vR += Vector3.down * (float)(9.81 * dt);
                    left.transform.position += vL * (float)dt; right.transform.position += vR * (float)dt;
                    left.transform.Rotate(new Vector3(40, 0, 25) * (float)dt, Space.World); right.transform.Rotate(new Vector3(-30, 0, -35) * (float)dt, Space.World);
                }
                replay.ReviewRecordParts(t);
            }
            log.AppendLine($"parts followed by the replay: {replay.PartCount}");

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.42f, 0.6f, 0.85f, 1f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 5000f; cam.fieldOfView = 40f;
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            // A chase-like view: behind, above and off to the left, looking at the aeroplane wherever it is.
            void Aim(Transform target, float back = 14f)
            {
                Vector3 at = target.position;
                cam.transform.position = at - target.forward * back + Vector3.up * 4.5f - target.right * 5f;
                cam.transform.rotation = Quaternion.LookRotation(at - cam.transform.position, Vector3.up);
            }

            // Live, at the end: wings off and fallen.
            Pose(EndT);
            replay.ReviewSaveLiveParts();
            Aim(plane.transform, 26f);
            Color32[] liveEnd = Shoot(cam, rt, Path.Combine(outDir, "live_end.png"));

            // The replay at 1 s, as 0822 drew it: the aeroplane re-posed, the parts left where they are.
            Pose(1.0); Aim(plane.transform);
            Color32[] oldCode = Shoot(cam, rt, Path.Combine(outDir, "replay_1s_oldcode.png"));
            // ... and now.
            replay.ReviewShowParts(1.0);
            Color32[] now1 = Shoot(cam, rt, Path.Combine(outDir, "replay_1s.png"));
            var clipGo = new GameObject("ClipCam"); var clip = clipGo.AddComponent<Camera>();
            var clipRt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            clip.CopyFrom(cam); clip.targetTexture = clipRt;
            Color32[] clip1 = Shoot(clip, clipRt, Path.Combine(outDir, "replay_1s_clipcam.png"));
            float lOff = left != null ? Vector3.Distance(left.transform.position, plane.transform.position) : -1, rOff = right != null ? Vector3.Distance(right.transform.position, plane.transform.position) : -1;
            float lAng = left != null ? Quaternion.Angle(left.transform.rotation, plane.transform.rotation) : -1;

            // The reference: a Pitts that never broke, at the same pose (the broken one hidden for the shot).
            GameObject intact = Build("pitts-intact");
            intact.transform.SetPositionAndRotation(plane.transform.position, plane.transform.rotation);
            plane.SetActive(false); if (left != null) left.SetActive(false); if (right != null) right.SetActive(false);
            Color32[] ref1 = Shoot(cam, rt, Path.Combine(outDir, "intact_1s.png"));
            Object.DestroyImmediate(intact);
            plane.SetActive(true); if (left != null) left.SetActive(true); if (right != null) right.SetActive(true);

            // 1 s after the break, then seek back to 1 s.
            Pose(3.0); replay.ReviewShowParts(3.0); Aim(plane.transform, 26f);
            Shoot(cam, rt, Path.Combine(outDir, "replay_3s.png"));
            float lOff3 = left != null ? Vector3.Distance(left.transform.position, plane.transform.position) : -1;
            Pose(1.0); replay.ReviewShowParts(1.0); Aim(plane.transform);
            Color32[] back1 = Shoot(cam, rt, Path.Combine(outDir, "replay_1s_after_seek.png"));

            // EXIT: the live parts put back as they were, the aeroplane where it was.
            replay.ReviewRestoreLiveParts(); Pose(EndT); Aim(plane.transform, 26f);
            Color32[] returned = Shoot(cam, rt, Path.Combine(outDir, "live_returned.png"));

            int Diff(Color32[] a, Color32[] b, int tol = 0) { int n = 0; for (int i = 0; i < a.Length; i++) if (System.Math.Abs(a[i].r - b[i].r) > tol || System.Math.Abs(a[i].g - b[i].g) > tol || System.Math.Abs(a[i].b - b[i].b) > tol) n++; return n; }
            int Plane(Color32[] a) { int n = 0; var bg = new Color32(107, 153, 217, 255); foreach (var c in a) if (System.Math.Abs(c.r - bg.r) > 6 || System.Math.Abs(c.g - bg.g) > 6 || System.Math.Abs(c.b - bg.b) > 6) n++; return n; }
            log.AppendLine($"at 1 s (before the break): wing debris {lOff:0.000} / {rOff:0.000} m from the aeroplane's origin, {lAng:0.00} deg off its attitude (0 = carried as cut)");
            log.AppendLine($"at 3 s (after the break): left wing {lOff3:0.0} m from the aeroplane (recorded fall)");
            log.AppendLine($"aeroplane pixels (not sky): 0822 at 1 s {Plane(oldCode)}, now at 1 s {Plane(now1)}, intact reference {Plane(ref1)}");
            log.AppendLine($"now vs intact reference at 1 s: {Diff(now1, ref1)} px differ ({Diff(now1, ref1, 8)} by more than 8/255) of {now1.Length}");
            log.AppendLine($"0822 vs intact reference at 1 s: {Diff(oldCode, ref1)} px differ ({Diff(oldCode, ref1, 8)} by more than 8/255)");
            log.AppendLine($"seek 1 s → 3 s → 1 s: {Diff(now1, back1)} px differ from the first 1 s");
            log.AppendLine($"clip camera at 1 s: {Diff(now1, clip1)} px differ from the screen");
            log.AppendLine($"return to live: {Diff(liveEnd, returned)} px differ from the live wreck before the replay");
            Object.DestroyImmediate(clipGo);
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

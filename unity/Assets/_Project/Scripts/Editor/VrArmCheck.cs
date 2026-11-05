using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlyingGame.Bridge;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// THE PILOT'S ARMS AGAINST THE COCKPIT, every headset aeroplane and seat (Ty 2026-11-04, Quest 3S: "the arms go into
    /// the structure on the great lakes like the switch box on the left"; "the wrists are super unrealistic").
    ///
    /// For each pose (stick neutral, full left + aft, full right + forward; throttle idle and full) both arms are solved
    /// two ways: BEFORE (the wrist on the line from the grip to the shoulder, the fixed elbow pole: what 2026.1104.1656 ran)
    /// and AFTER (Core <see cref="ArmPose"/> with the runtime's own <see cref="VrArmClearance"/> grid, exactly as
    /// VrCockpitHands does). Both are then MEASURED against the aeroplane's own triangles, exactly (closest point on every
    /// triangle in reach, signed by its face): the deepest point of the upper-arm and forearm capsules inside the structure,
    /// and the wrist's bend. The measurement does not use the grid the solver used.
    ///
    /// ASSERTED: after, no arm is more than <see cref="MaxAfterMm"/> mm into the structure in any pose, and no wrist bends
    /// more than <see cref="MaxWristDeg"/>°. The table goes to the log and to logs/arms/table.md.
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.VrArmCheck.Run   (no -nographics: PNGs too)
    /// ARMS_ONLY=id[,id]; ARMS_SHOTS=great-lakes-like,extra-300-like,glider-2-33-like (default) render before/after.
    /// </summary>
    public static class VrArmCheck
    {
        public const double MaxAfterMm = 15.0, MaxWristDeg = 30.0;
        private const int W = 1280, H = 960;
        private static readonly (string Id, string Seat)[] Fleet =
        {
            ("great-lakes-like", "rear"), ("great-lakes-like", "front"),
            ("extra-300-like", "rear"), ("extra-300-like", "front"),
            ("pitts-s2b-like", "rear"), ("pitts-s2b-like", "front"),
            ("glider-2-33-like", "front"), ("glider-2-33-like", "rear"),
            ("j3-cub-like", "rear"), ("j3-cub-floats-like", "rear"),
        };
        private static int _fail;
        private static readonly StringBuilder Table = new();
        private static void Fail(string m) { _fail++; Debug.LogError("[VrArms] FAIL " + m); }
        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
        private static Vec3 C(Vector3 v) => new(v.x, v.y, v.z);

        public static void Run()
        {
            _fail = 0;
            Table.Clear();
            Table.AppendLine("| aircraft | seat | pose | L before mm | L after mm | R before mm | R after mm | wrist L before/after ° | wrist R before/after ° | swing L/R ° |");
            Table.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "arms");
            bool render = !SystemInfo.graphicsDeviceType.ToString().Contains("Null");
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(dir);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                string only = Environment.GetEnvironmentVariable("ARMS_ONLY");
                string shots = Environment.GetEnvironmentVariable("ARMS_SHOTS") ?? "great-lakes-like,extra-300-like,glider-2-33-like";
                foreach ((string id, string seat) in Fleet)
                    if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), id) >= 0)
                        One(id, seat, dir, render && Array.IndexOf(shots.Split(','), id) >= 0);
                File.WriteAllText(Path.Combine(dir, "table.md"), Table.ToString());
                Debug.Log("[VrArms] table\n" + Table);
            }
            catch (Exception e) { _fail++; Debug.LogException(e); }
            finally
            {
                VrPilotBody.LegacyArmsForTesting = false;
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[VrArms] {(_fail == 0 ? "PASS" : "FAIL")} ({_fail} failures)");
                EditorApplication.Exit(_fail == 0 ? 0 : 1);
            }
        }

        /// <summary>Exact signed distance to the airframe's triangles in reach (aeroplane frame = world: root at identity).</summary>
        private sealed class Exact : IArmClearance
        {
            public readonly List<Vector3> A = new(), B = new(), Cc = new(), N = new();
            public double Distance(Vec3 q)
            {
                Vector3 p = U(q);
                float best = float.MaxValue; int bi = -1; Vector3 bc = default;
                for (int i = 0; i < A.Count; i++)
                {
                    // Cheap reject on the triangle's bounding sphere would help; the counts here are small enough.
                    Vector3 c = VrArmClearance.ClosestOnTriangle(p, A[i], B[i], Cc[i]);
                    float d = (c - p).sqrMagnitude;
                    if (d < best) { best = d; bi = i; bc = c; }
                }
                if (bi < 0) return 1.0;
                return Mathf.Sqrt(best);   // unsigned, as the runtime grid (a single-sided skin has no inside)
            }
        }

        private static Exact Gather(Transform air, Vector3 eye, IList<Transform> exclude)
        {
            var ex = new Exact();
            Vector3 lo = eye + VrArmClearance.BoxMin, hi = eye + VrArmClearance.BoxMax;
            var verts = new List<Vector3>(); var tris = new List<int>();
            void Add(Mesh m, Transform t)
            {
                if (m == null || !m.isReadable) return;
                for (Transform p = t; p != null; p = p.parent) if (exclude.Contains(p)) return;
                verts.Clear(); m.GetVertices(verts);
                for (int i = 0; i < verts.Count; i++) verts[i] = t.TransformPoint(verts[i]);
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                    tris.Clear(); m.GetTriangles(tris, s);
                    for (int k = 0; k + 2 < tris.Count; k += 3)
                    {
                        Vector3 a = verts[tris[k]], b = verts[tris[k + 1]], c = verts[tris[k + 2]];
                        Vector3 mn = Vector3.Min(a, Vector3.Min(b, c)), mx = Vector3.Max(a, Vector3.Max(b, c));
                        if (mx.x < lo.x || mx.y < lo.y || mx.z < lo.z || mn.x > hi.x || mn.y > hi.y || mn.z > hi.z) continue;
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-14f) continue;
                        ex.A.Add(a); ex.B.Add(b); ex.Cc.Add(c); ex.N.Add(n.normalized);
                    }
                }
            }
            foreach (MeshFilter mf in air.GetComponentsInChildren<MeshFilter>(false)) { var r = mf.GetComponent<Renderer>(); if (r != null && r.enabled) Add(mf.sharedMesh, mf.transform); }
            foreach (SkinnedMeshRenderer s in air.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (s.enabled) Add(s.sharedMesh, s.transform);
            return ex;
        }

        private struct ArmOut { public Vector3 S, E, Wr, G; public Quaternion Rot; public double PenMm, BendDeg, Swing; }

        private static ArmOut Before(bool left, Vector3 sh, Vector3 grip, Vector3 pole, float upper, float lower, IArmClearance exact)
        {
            Vector3 wrist = grip + (sh - grip).normalized * (float)SeatedPilot.WristToGripM;
            TwoBoneIk.Result r = TwoBoneIk.Solve(C(sh), C(wrist), upper, lower, C(pole));
            var o = new ArmOut { S = sh, E = U(r.Middle), Wr = U(r.End), G = r.Reached ? grip : U(r.End) + (grip - wrist), Rot = Quaternion.identity };
            o.PenMm = ArmPose.Penetration(C(o.S), C(o.E), C(o.Wr), exact) * 1000.0;
            o.BendDeg = ArmPose.AngleDeg(C(o.Wr - o.E), C(o.G - o.Wr));
            return o;
        }

        private static ArmOut After(bool left, Vector3 sh, Vector3 grip, Vector3 axis, Vector3 pole, float upper, float lower, IArmClearance grid, IArmClearance exact)
        {
            ArmPose.Result r = axis.sqrMagnitude > 1e-6f
                ? ArmPose.OnGrip(left, C(sh), C(grip), C(axis), upper, lower, C(pole), SeatedPilot.WristToGripM, grid, 0.0, left ? ArmPose.KnobTiltDeg : ArmPose.MaxTiltDeg)
                : ArmPose.Free(C(sh), C(grip + (sh - grip).normalized * (float)SeatedPilot.WristToGripM), upper, lower, C(pole), grid);
            var o = new ArmOut { S = sh, E = U(r.Elbow), Wr = U(r.Wrist), Swing = r.SwivelDeg };
            if (axis.sqrMagnitude > 1e-6f)
            {
                o.G = U(r.Grip);
                Vector3 fwd = U(r.HandForward), palm = U(r.Palm), x = left ? palm : -palm;
                o.Rot = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, x));
            }
            else { o.G = grip; o.Rot = Quaternion.identity; }
            o.PenMm = ArmPose.Penetration(C(o.S), C(o.E), C(o.Wr), exact) * 1000.0;
            o.BendDeg = ArmPose.AngleDeg(C(o.Wr - o.E), C(o.G - o.Wr));
            return o;
        }

        private static void One(string id, string seatId, string dir, bool render)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(id);
            var builder = new AirframeBuilder();
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            builder.Build(root.transform, cfg);
            Transform air = root.transform;
            StationConfig seat = cfg.Loading.Seats.Find(s => s.Id == seatId);
            if (seat == null || !seat.HasEye) { Debug.Log($"[VrArms] {id}/{seatId}: no such seat with an eye — skipped"); builder.Clear(); Object.DestroyImmediate(root); return; }
            Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
            CockpitProxy proxy = CockpitProxy.For(id);
            VrControlBinding.Stick stick = builder.Rig != null ? VrControlBinding.BindStick(builder.Rig, builder.FindInModel, air, seat.Id) : null;
            StickGimbal g = stick != null ? stick.Gimbal : new StickGimbal(C(eye + U(proxy.StickPivot)), proxy.StickLengthM, proxy.StickRollMaxDeg, proxy.StickAftMaxDeg, proxy.StickFwdMaxDeg);
            bool engine = cfg.Propulsion != null;
            VrControlBinding.Throttle th = engine && builder.Rig != null ? VrControlBinding.BindThrottle(builder.Rig, builder.FindInModel, air, seat.Id) : null;
            if (engine) th ??= VrControlBinding.ProxyThrottle(C(eye + U(proxy.ThrottlePivot)), proxy.ThrottleArmM, proxy.ThrottleIdleDeg, proxy.ThrottleSweepDeg);
            var exclude = new List<Transform>();
            if (stick?.Pitch?.T != null) exclude.Add(stick.Pitch.T);
            if (stick?.Roll?.T != null) exclude.Add(stick.Roll.T);
            if (th?.Node?.T != null) exclude.Add(th.Node.T);
            var grid = new VrArmClearance();
            grid.Build(air, eye, exclude, $"{id} {seatId}");
            Exact exact = Gather(air, eye, exclude);
            Debug.Log($"[VrArms] {id}/{seatId}: {exact.A.Count} triangles in reach; stick {(stick != null ? "rig" : "proxy")}, throttle {(th == null ? "none" : th.OnRig ? "rig" : "proxy")}");

            var body = new VrPilotBody();
            body.SetVisible(true);
            Vector3 hipL = eye + U(SeatedPilot.HipL), hipR = eye + U(SeatedPilot.HipR);
            body.PlaceRoot(0.5f * (hipL + hipR), air.rotation);
            Vector3 shL = eye + U(SeatedPilot.ShoulderL), shR = eye + U(SeatedPilot.ShoulderR);
            float upL = (float)SeatedPilot.UpperArmM, loL = (float)SeatedPilot.ForearmM, upR = upL, loR = loL;
            if (body.TryChain("L_UpperArm", "L_LowerArm", "L_Hand", out Vector3 r0, out float u0, out float l0)) { shL = r0; upL = u0; loL = l0; }
            if (body.TryChain("R_UpperArm", "R_LowerArm", "R_Hand", out Vector3 r1, out float u1, out float l1)) { shR = r1; upR = u1; loR = l1; }
            Vector3 poleL = eye + U(SeatedPilot.ElbowPoleL), poleR = eye + U(SeatedPilot.ElbowPoleR);

            var poses = new List<(string Name, double Roll, double Pitch, double Thr)>
            {
                ("neutral, throttle idle", 0, 0, 0), ("neutral, throttle full", 0, 0, 1),
                ("stick full left + aft", -g.RollMaxDeg, g.AftMaxDeg, 0.5), ("stick full right + fwd", g.RollMaxDeg, -g.FwdMaxDeg, 0.5),
            };
            foreach ((string name, double roll, double pitch, double thr) in poses)
            {
                Vector3 stickGrip = U(g.GripAt(roll, pitch));
                Vector3 stickAxis = (stickGrip - U(g.Pivot)).normalized;   // as StickGimbal.Up: pivot → grip
                Vector3 thrGrip, thrAxis;
                if (th != null)
                {
                    Vec3 knob = th.Lever.GripAt(th.TFor(thr));
                    thrGrip = U(knob); thrAxis = U(ArmPose.KnobAxis(th.Lever.Axis, left: true));
                }
                else { thrGrip = hipL + new Vector3(0f, 0.08f, 0.35f); thrAxis = Vector3.zero; }
                ArmOut bl = Before(true, shL, thrGrip, poleL, upL, loL, exact), br = Before(false, shR, stickGrip, poleR, upR, loR, exact);
                ArmOut al = After(true, shL, thrGrip, thrAxis, poleL, upL, loL, grid, exact), ar = After(false, shR, stickGrip, stickAxis, poleR, upR, loR, grid, exact);
                Table.AppendLine($"| {id} | {seatId} | {name} | {bl.PenMm:F0} | {al.PenMm:F0} | {br.PenMm:F0} | {ar.PenMm:F0} | {bl.BendDeg:F0}/{al.BendDeg:F0} | {br.BendDeg:F0}/{ar.BendDeg:F0} | {al.Swing:F0}/{ar.Swing:F0} |");
                Debug.Log($"[VrArms] {id}/{seatId} {name}: L {bl.PenMm:F0}→{al.PenMm:F0} mm, R {br.PenMm:F0}→{ar.PenMm:F0} mm; wrist L {bl.BendDeg:F0}→{al.BendDeg:F0}°, R {br.BendDeg:F0}→{ar.BendDeg:F0}°; swing L {al.Swing:F0}° R {ar.Swing:F0}°");
                if (al.PenMm > MaxAfterMm) Fail($"{id}/{seatId} {name}: left arm {al.PenMm:F0} mm into the structure");
                if (ar.PenMm > MaxAfterMm) Fail($"{id}/{seatId} {name}: right arm {ar.PenMm:F0} mm into the structure");
                if (thrAxis.sqrMagnitude > 1e-6f && al.BendDeg > MaxWristDeg) Fail($"{id}/{seatId} {name}: left wrist bent {al.BendDeg:F0}°");
                if (ar.BendDeg > MaxWristDeg) Fail($"{id}/{seatId} {name}: right wrist bent {ar.BendDeg:F0}°");
                if (render && (name.StartsWith("neutral") || name.Contains("left")))
                {
                    foreach (bool after in new[] { false, true })
                    {
                        ArmOut L = after ? al : bl, R = after ? ar : br;
                        VrPilotBody.LegacyArmsForTesting = !after;
                        body.PlaceRoot(0.5f * (hipL + hipR), air.rotation);
                        var p = new VrPilotBody.Pose
                        {
                            Up = air.up, CurlL = 1f, CurlR = 1f,
                            ShoulderL = L.S, ElbowL = L.E, WristL = L.Wr, GripL = L.G, HandRotL = after ? L.Rot : VrPilotBody.RestGripRot(true),
                            ShoulderR = R.S, ElbowR = R.E, WristR = R.Wr, GripR = R.G, HandRotR = after ? R.Rot : VrPilotBody.RestGripRot(false),
                        };
                        Legs(body, ref p, builder, seat.Id, eye, proxy, hipL, hipR);
                        body.Apply(p);
                        if (body.TryBone("R_LowerArm", out Vector3 bl0) && body.TryBone("R_Hand", out Vector3 bh0) && body.TryBone("L_LowerArm", out Vector3 bl1) && body.TryBone("L_Hand", out Vector3 bh1))
                            Debug.Log($"[VrArms] {(after ? "after" : "before")} {id}/{seatId} {name}: posed bones vs solved joints — R elbow {Vector3.Distance(bl0, R.E) * 1000:F0} mm, R wrist {Vector3.Distance(bh0, R.Wr) * 1000:F0} mm, L elbow {Vector3.Distance(bl1, L.E) * 1000:F0} mm, L wrist {Vector3.Distance(bh1, L.Wr) * 1000:F0} mm");
                        Shoot(air, eye, p, $"{(after ? "after" : "before")}-{id}-{seatId}-{Slug(name)}", dir);
                    }
                    VrPilotBody.LegacyArmsForTesting = false;
                }
            }
            builder.Clear();
            Object.DestroyImmediate(root);
            body.Destroy();
        }

        private static void Legs(VrPilotBody body, ref VrPilotBody.Pose p, AirframeBuilder builder, string seatId, Vector3 eye, CockpitProxy proxy, Vector3 hipL, Vector3 hipR)
        {
            Vector3 ballL = eye + U(proxy.FootL), ballR = eye + U(proxy.FootR);
            foreach (string s in VrControlBinding.Suffixes(seatId))
            {
                Transform l = builder.FindInModel($"Foot_L{s}"), r = builder.FindInModel($"Foot_R{s}");
                if (l != null) ballL = l.position;
                if (r != null) ballR = r.position;
            }
            foreach ((bool left, Vector3 hip, Vector3 ball, Vec3 pole) in new[] { (true, hipL, ballL, SeatedPilot.KneePoleL), (false, hipR, ballR, SeatedPilot.KneePoleR) })
            {
                float upper = (float)SeatedPilot.ThighM, lower = (float)SeatedPilot.ShinM;
                Vector3 h = hip;
                if (body.TryChain(left ? "L_UpperLeg" : "R_UpperLeg", left ? "L_LowerLeg" : "R_LowerLeg", left ? "L_Foot" : "R_Foot", out Vector3 r0, out float u, out float l)) { h = r0; upper = u; lower = l; }
                Vector3 ankle = ball + body.HeelFromBall(left);
                TwoBoneIk.Result r = TwoBoneIk.Solve(C(h), C(ankle), upper, lower, C(eye + U(pole)));
                if (left) { p.HipL = h; p.KneeL = U(r.Middle); p.AnkleL = U(r.End); p.BallL = U(r.End) + (ball - ankle); }
                else { p.HipR = h; p.KneeR = U(r.Middle); p.AnkleR = U(r.End); p.BallR = U(r.End) + (ball - ankle); }
            }
        }

        private static string Slug(string s) => s.ToLowerInvariant().Replace(", ", "-").Replace(" + ", "-").Replace(' ', '-');

        private static void Shoot(Transform air, Vector3 eye, in VrPilotBody.Pose p, string tag, string dir)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.0f; light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 0.6f; key.transform.rotation = air.rotation * Quaternion.Euler(35f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.51f, 0.53f);
            var camGo = new GameObject("Eye");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.70f, 0.88f);
            cam.nearClipPlane = 0.02f;
            // From the seat's eye (the pilot's own view), and two close-ups of each hand from outside the arm.
            var views = new List<(string, Vector3, Quaternion, float)>
            {
                ("eye-left", eye, air.rotation * Quaternion.Euler(50f, -40f, 0f), 90f),
                ("eye-right", eye, air.rotation * Quaternion.Euler(50f, 30f, 0f), 90f),
            };
            foreach ((string n, Vector3 hand, Vector3 elbow, float side) in new[] { ("handL", p.GripL, p.ElbowL, -1f), ("handR", p.GripR, p.ElbowR, 1f) })
            {
                Vector3 from = hand + new Vector3(side * 0.30f, 0.22f, 0.18f);
                views.Add((n, from, Quaternion.LookRotation(0.6f * hand + 0.4f * ((hand + elbow) * 0.5f) - from, air.up), 45f));
            }
            foreach ((string n, Vector3 pos, Quaternion rot, float fov) in views)
            {
                cam.fieldOfView = fov;
                camGo.transform.SetPositionAndRotation(pos, rot);
                string path = Path.Combine(dir, $"{tag}-{n}.png");
                RenderTexture rt = RenderTexture.GetTemporary(W, H, 24);
                RenderTexture active = RenderTexture.active;
                try
                {
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, W, H), 0, 0); shot.Apply();
                    File.WriteAllBytes(path, shot.EncodeToPNG());
                    Object.DestroyImmediate(shot);
                }
                finally { cam.targetTexture = null; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
            }
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(key.gameObject);
        }
    }
}

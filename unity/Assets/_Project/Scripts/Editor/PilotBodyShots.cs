using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
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
    /// The seated pilot's body as the headset sees it: for each aeroplane, the real <see cref="VrPilotBody"/> posed
    /// the way <c>VrCockpitHands.PoseBody</c> poses it with the controllers at rest (right hand on the stick, left on
    /// the throttle, boots on the pedals), checked, and drawn from the eye looking down.
    ///
    /// ASSERTED (headless is enough): after <see cref="VrPilotBody.PlaceRoot"/> the asset's LEFT shoulder is left of
    /// the eye and the RIGHT shoulder right of it, both below the eye and within a hand's breadth fore/aft — the bug
    /// where the left shoulder sat in front of the pilot's face was the asset turned a quarter turn.
    ///
    ///   Unity -batchmode -quit -executeMethod FlyingGame.EditorTools.PilotBodyShots.Run   (no -nographics: PNGs)
    /// PNGs go to logs/body/&lt;BODY_TAG or "shot"&gt;-&lt;aeroplane&gt;-&lt;seat&gt;-&lt;view&gt;.png.
    /// </summary>
    public static class PilotBodyShots
    {
        private const int W = 1280, H = 960;
        private static readonly (string Id, string Seat)[] Fleet =
        {
            ("extra-300-like", "rear"), ("pitts-s2b-like", "rear"), ("great-lakes-like", "rear"),
            ("j3-cub-like", "rear"), ("glider-2-33-like", "front"),
        };
        private static int _fail;

        private static void Fail(string m) { _fail++; Debug.LogError("[PilotBody] FAIL " + m); }
        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
        private static Vec3 C(Vector3 v) => new(v.x, v.y, v.z);

        public static void Run()
        {
            _fail = 0;
            string tag = Environment.GetEnvironmentVariable("BODY_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "shot";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "..", "logs", "body");
            bool render = !SystemInfo.graphicsDeviceType.ToString().Contains("Null");
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels;
            try
            {
                Directory.CreateDirectory(dir);
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                // Before/after helpers: BODY_ONLY=id[,id] one type; BODY_EYE_UP_M=-0.025 the eye (and the body on it)
                // that much lower; BODY_HIDE=node[,node] those model nodes off; BODY_LEVEL=1 adds a level look ahead.
                string only = Environment.GetEnvironmentVariable("BODY_ONLY");
                foreach ((string id, string seat) in Fleet)
                    if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), id) >= 0) One(id, seat, tag, dir, render);
            }
            catch (Exception e) { _fail++; Debug.LogException(e); }
            finally
            {
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn;
                Debug.Log($"[PilotBody] {(_fail == 0 ? "PASS" : "FAIL")} ({_fail} failures)");
                EditorApplication.Exit(_fail == 0 ? 0 : 1);
            }
        }

        private static void One(string id, string seatId, string tag, string dir, bool render)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(id);
            var builder = new AirframeBuilder();
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            builder.Build(root.transform, cfg);
            Transform air = root.transform;
            StationConfig seat = cfg.Loading.Seats.Find(s => s.Id == seatId) ?? cfg.Loading.Seats[0];
            Vector3 eye = CoordinateMap.ToUnity(seat.EyeVec());
            if (float.TryParse(Environment.GetEnvironmentVariable("BODY_EYE_UP_M"), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float eyeUp)) eye += Vector3.up * eyeUp;
            CockpitProxy proxy = CockpitProxy.For(id);

            VrControlBinding.Stick stick = builder.Rig != null ? VrControlBinding.BindStick(builder.Rig, builder.FindInModel, air, seat.Id) : null;
            VrControlBinding.Throttle th = cfg.Propulsion != null && builder.Rig != null
                ? VrControlBinding.BindThrottle(builder.Rig, builder.FindInModel, air, seat.Id) : null;
            Vector3 stickGrip = stick != null ? U(stick.Gimbal.GripAt(0, 0)) : eye + U(proxy.StickPivot) + Vector3.up * (float)proxy.StickLengthM;
            Vector3 hipL = eye + U(SeatedPilot.HipL), hipR = eye + U(SeatedPilot.HipR);
            Vector3 throttleGrip = th != null ? U(th.Lever.GripAt(th.TFor(0.3))) : hipL + new Vector3(0f, 0.08f, 0.35f);

            Vector3 ballL = Foot(builder, seat.Id, "L") ?? eye + U(proxy.FootL);
            Vector3 ballR = Foot(builder, seat.Id, "R") ?? eye + U(proxy.FootR);
            Vector3 heel = new(0f, (float)SeatedPilot.AnkleToBallDownM, -(float)SeatedPilot.AnkleToBallFwdM);

            var body = new VrPilotBody();
            body.SetVisible(true);
            body.PlaceRoot(0.5f * (hipL + hipR), air.rotation);
            var p = new VrPilotBody.Pose { Up = air.up, HandRotL = air.rotation * VrPilotBody.RestGripRot(true), HandRotR = air.rotation * VrPilotBody.RestGripRot(false), CurlL = 1f, CurlR = 1f };
            Arm(body, ref p, true, eye + U(SeatedPilot.ShoulderL), throttleGrip, eye + U(SeatedPilot.ElbowPoleL));
            Arm(body, ref p, false, eye + U(SeatedPilot.ShoulderR), stickGrip, eye + U(SeatedPilot.ElbowPoleR));
            Leg(body, ref p, true, hipL, ballL, ballL + heel, eye + U(SeatedPilot.KneePoleL));
            Leg(body, ref p, false, hipR, ballR, ballR + heel, eye + U(SeatedPilot.KneePoleR));
            body.Apply(p);
            if (Environment.GetEnvironmentVariable("BODY_PEDALS") == "1")
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || !mf.name.Contains("Pedal") && !mf.name.Contains("Foot") && !mf.name.Contains("Rudder_Bar")) continue;
                    Mesh m = mf.sharedMesh; Vector3[] v = m.vertices; int[] t = m.triangles; Vector3 n = Vector3.zero; float area = 0f;
                    Bounds wb = new(air.InverseTransformPoint(mf.transform.TransformPoint(v[0])), Vector3.zero);
                    foreach (Vector3 q in v) wb.Encapsulate(air.InverseTransformPoint(mf.transform.TransformPoint(q)));
                    for (int k = 0; k < t.Length; k += 3)
                    {
                        Vector3 a = air.InverseTransformPoint(mf.transform.TransformPoint(v[t[k]])), b = air.InverseTransformPoint(mf.transform.TransformPoint(v[t[k + 1]])), c = air.InverseTransformPoint(mf.transform.TransformPoint(v[t[k + 2]]));
                        Vector3 cr = Vector3.Cross(b - a, c - a); if (cr.z < -0.3f * cr.magnitude) { n += cr; area += cr.magnitude * 0.5f; }
                    }
                    Debug.Log($"[PilotBody] {id} pedal mesh {mf.name}: aft-facing normal {n.normalized:F2} (lean {Mathf.Atan2(n.y, -n.z) * Mathf.Rad2Deg:F0}° from vertical, top forward), {area * 1e4f:F0} cm²; bounds {wb.min - eye:F3}..{wb.max - eye:F3} from the eye");
                }
            Debug.Log($"[PilotBody] {id}/{seat.Id} feet: ball L {ballL - eye:F3} R {ballR - eye:F3} from the eye; ankle L {p.AnkleL - eye:F3}");

            // The shoulder check: in the aeroplane's frame relative to the eye (+x right, +y up, +z nose).
            Vector3 sl = p.ShoulderL - eye, sr = p.ShoulderR - eye;
            string where = $"{id}/{seat.Id} ({(body.Skinned ? "skinned" : "stand-in")}): shoulder L {sl:F3}, R {sr:F3} from the eye";
            bool ok = true;
            foreach ((Vector3 s, float side, string n) in new[] { (sl, -1f, "L"), (sr, 1f, "R") })
            {
                if (s.x * side < 0.10f || s.x * side > 0.28f) { ok = false; Fail($"{where}: {n} shoulder is {s.x * 100:F0} cm across (want 10–28 cm to its own side)"); }
                if (s.y > -0.10f || s.y < -0.40f) { ok = false; Fail($"{where}: {n} shoulder is {s.y * 100:F0} cm vertical (want 10–40 cm below the eye)"); }
                if (Mathf.Abs(s.z) > 0.15f) { ok = false; Fail($"{where}: {n} shoulder is {s.z * 100:F0} cm fore/aft (want within 15 cm; + is in front of the face)"); }
            }
            Debug.Log($"[PilotBody] {where}; grips L {Vector3.Distance(p.GripL, throttleGrip) * 100:F1} cm, R {Vector3.Distance(p.GripR, stickGrip) * 100:F1} cm off the control" + (ok ? " — OK" : ""));

            if (render)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.50f);
                var camGo = new GameObject("Eye");
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.70f, 0.88f);
                cam.nearClipPlane = 0.03f; cam.fieldOfView = 90f;
                string hide = Environment.GetEnvironmentVariable("BODY_HIDE");
                if (!string.IsNullOrEmpty(hide))
                    foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        foreach (string n in hide.Split(','))
                            if (t.name == n || t.name.EndsWith(":" + n)) { t.gameObject.SetActive(false); Debug.Log($"[PilotBody] hid {t.name}"); }
                var views = new System.Collections.Generic.List<(string, float, float)> { ("down", 55f, 0f), ("down-left", 50f, -35f), ("ahead", 20f, 0f) };
                if (Environment.GetEnvironmentVariable("BODY_LEVEL") == "1") views.Add(("level", 0f, 0f));
                foreach ((string v, float pitch, float yaw) in views)
                {
                    camGo.transform.SetPositionAndRotation(air.TransformPoint(eye), air.rotation * Quaternion.Euler(pitch, yaw, 0f));
                    Shoot(cam, Path.Combine(dir, $"{tag}-{id}-{seat.Id}-{v}.png"));
                }
                // BODY_SOLO=1: the aeroplane off, the body alone from outside (shoulders, folds, gloves, feet on the pedal points).
                if (Environment.GetEnvironmentVariable("BODY_SOLO") == "1")
                {
                    // The feet on the pedals, the aeroplane still there: from inboard-aft-above the left foot, and from the
                    // right of the right foot.
                    // Only the aeroplane's parts near the feet stay (pedals, floor, footwell), so the skins do not hide them.
                    Vector3 fm = (ballL + ballR) * 0.5f;
                    var off = new List<Renderer>();
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                    {
                        Bounds bb = r.bounds;
                        if (!r.enabled || (bb.SqrDistance(air.TransformPoint(fm)) < 0.30f * 0.30f && bb.size.magnitude < 1.0f)) continue;
                        r.enabled = false; off.Add(r);
                    }
                    foreach ((string v, Vector3 from, Vector3 at) in new[]
                    {
                        ("pedals", fm + new Vector3(-0.75f, 0.30f, -0.25f), fm + new Vector3(0f, 0.0f, -0.08f)),
                        ("pedals-side", fm + new Vector3(-0.90f, 0.02f, -0.12f), fm + new Vector3(0f, 0.0f, -0.12f)),
                    })
                    {
                        cam.fieldOfView = 40f;
                        Vector3 wf = air.TransformPoint(from), wa = air.TransformPoint(at);
                        camGo.transform.SetPositionAndRotation(wf, Quaternion.LookRotation(wa - wf, air.up));
                        Shoot(cam, Path.Combine(dir, $"{tag}-{id}-{seat.Id}-{v}.png"));
                    }
                    foreach (Renderer r in off) r.enabled = true;
                    root.SetActive(false);
                    cam.fieldOfView = 35f;
                    Vector3 chest = eye + new Vector3(0f, -0.30f, 0.10f), feet = (ballL + ballR) * 0.5f;
                    Vector3 handR = p.GripR, handL = p.GripL;
                    foreach ((string v, Vector3 from, Vector3 at) in new[]
                    {
                        ("solo-front3q", chest + new Vector3(-1.1f, 0.35f, 1.9f), chest + new Vector3(0f, -0.15f, 0.30f)),
                        ("solo-side", chest + new Vector3(-2.4f, 0.10f, 0.35f), chest + new Vector3(0f, -0.20f, 0.35f)),
                        ("solo-back3q", chest + new Vector3(1.3f, 0.70f, -1.5f), chest + new Vector3(0f, -0.10f, 0.20f)),
                        ("solo-feet", feet + new Vector3(-0.55f, 0.30f, 0.45f), feet + new Vector3(0f, 0.02f, -0.05f)),
                        ("solo-handR", handR + new Vector3(0.40f, 0.25f, 0.30f), handR + new Vector3(0f, 0f, -0.04f)),
                        ("solo-handL", handL + new Vector3(-0.40f, 0.25f, 0.30f), handL + new Vector3(0f, 0f, -0.04f)),
                    })
                    {
                        Vector3 wf = air.TransformPoint(from), wa = air.TransformPoint(at);
                        camGo.transform.SetPositionAndRotation(wf, Quaternion.LookRotation(wa - wf, air.up));
                        Shoot(cam, Path.Combine(dir, $"{tag}-{id}-{seat.Id}-{v}.png"));
                    }
                    root.SetActive(true);
                }
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(light.gameObject);
            }
            builder.Clear();
            Object.DestroyImmediate(root);
            body.Destroy();
        }

        private static Vector3? Foot(AirframeBuilder b, string seatId, string side)
        {
            foreach (string s in VrControlBinding.Suffixes(seatId))
            {
                Transform t = b.FindInModel($"Foot_{side}{s}");
                if (t != null) return t.position;
            }
            foreach (string s in VrControlBinding.Suffixes(seatId))
            {
                Transform t = b.FindInModel($"Pedal_{side}{s}") ?? b.FindInModel($"Pedal{s}_{side}");
                if (t != null) return t.position + Vector3.up * 0.12f;
            }
            return null;
        }

        // The same arithmetic as VrCockpitHands.Arm / Leg (controllers at rest, no tracked joints).
        private static void Arm(VrPilotBody body, ref VrPilotBody.Pose p, bool left, Vector3 shoulder, Vector3 grip, Vector3 pole)
        {
            float upper = (float)SeatedPilot.UpperArmM, lower = (float)SeatedPilot.ForearmM;
            if (body.TryChain(left ? "L_UpperArm" : "R_UpperArm", left ? "L_LowerArm" : "R_LowerArm", left ? "L_Hand" : "R_Hand",
                              out Vector3 r0, out float u, out float l)) { shoulder = r0; upper = u; lower = l; }
            Vector3 wrist = grip + (shoulder - grip).normalized * (float)SeatedPilot.WristToGripM;
            TwoBoneIk.Result r = TwoBoneIk.Solve(C(shoulder), C(wrist), upper, lower, C(pole));
            Vector3 elbow = U(r.Middle), w = U(r.End), g = r.Reached ? grip : w + (grip - wrist);
            if (left) { p.ShoulderL = shoulder; p.ElbowL = elbow; p.WristL = w; p.GripL = g; }
            else { p.ShoulderR = shoulder; p.ElbowR = elbow; p.WristR = w; p.GripR = g; }
        }

        private static void Leg(VrPilotBody body, ref VrPilotBody.Pose p, bool left, Vector3 hip, Vector3 ball, Vector3 ankle, Vector3 pole)
        {
            float upper = (float)SeatedPilot.ThighM, lower = (float)SeatedPilot.ShinM;
            if (body.TryChain(left ? "L_UpperLeg" : "R_UpperLeg", left ? "L_LowerLeg" : "R_LowerLeg", left ? "L_Foot" : "R_Foot",
                              out Vector3 r0, out float u, out float l)) { hip = r0; upper = u; lower = l; }
            TwoBoneIk.Result r = TwoBoneIk.Solve(C(hip), C(ankle), upper, lower, C(pole));
            Vector3 a = U(r.End), b = a + (ball - ankle);
            if (left) { p.HipL = hip; p.KneeL = U(r.Middle); p.AnkleL = a; p.BallL = b; }
            else { p.HipR = hip; p.KneeR = U(r.Middle); p.AnkleR = a; p.BallR = b; }
        }

        private static void Shoot(Camera cam, string path)
        {
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
                Debug.Log("[PilotBody] wrote " + Path.GetFullPath(path));
            }
            finally { cam.targetTexture = null; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}

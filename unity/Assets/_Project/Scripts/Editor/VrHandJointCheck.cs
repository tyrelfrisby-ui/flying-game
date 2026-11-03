using System;
using System.Collections.Generic;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Core.Vr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// REAL TRACKED HANDS ON EVERY STICK AND THROTTLE, through the game's own pipeline (Ty 2026-11-01: his hands moved
    /// the Extra's throttle and nothing else, with the gamepad off, while VrHandleCheck said HANDLE OK — that check
    /// hands CockpitGrab a ready-made HandPose and so never ran the part that failed).
    ///
    /// Here nothing is skipped: a scene built like SceneBootstrap's (QuestRig + VrCockpitHands on the camera,
    /// FlightSimDriver + AirframeVisual + TouchFlightControls on the aeroplane), the head recentred like the headset's,
    /// and XR Hands JOINT data (26 joints, tracking space, an anatomical hand that opens, reaches, closes into a fist
    /// or a pinch, then moves) fed through XrTrackedHands.Reader — the seam QuestHandTracking fills on the device. Each
    /// frame runs VrCockpitHands.Tick (arbiter, joints → seat, curl/pinch detector, grab) and TouchFlightControls'
    /// QuestInput + PublishToDriver, and the test asserts the DRIVER's inputs move: aileron and elevator for the stick,
    /// the throttle lever for the throttle. Every Quest-fleet aircraft, every seat, the room heading 0° and 90°.
    ///
    /// Run: Unity -batchmode -nographics -quit -projectPath unity -executeMethod FlyingGame.EditorTools.VrHandJointCheck.Run
    /// Prints "VRJ PASS …" / "VRJ FAIL …" per case and "VRJ OK" or "VRJ PROBLEMS n"; exit code 0 / 1.
    /// </summary>
    public static class VrHandJointCheck
    {
        private static readonly string[] Fleet =
            { "glider-2-33-like", "extra-300-like", "pitts-s2b-like", "great-lakes-like", "j3-cub-like", "j3-cub-floats-like" };

        private const float Dt = 1f / 72f;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static int _fails, _passes;

        // The hand the injected reader reports, per side (null = not tracked).
        private static readonly Vector3[][] JointPos = { new Vector3[HandJoint.Count], new Vector3[HandJoint.Count] };
        private static readonly Quaternion[][] JointRot = { new Quaternion[HandJoint.Count], new Quaternion[HandJoint.Count] };
        private static readonly bool[] Tracked = new bool[2];

        public static void Run()
        {
            int exit = 1;
            XrTrackedHands.JointReader oldReader = XrTrackedHands.Reader;
            bool oldModels = SessionSettings.UseAirframeModels, oldOwn = SessionSettings.UseOwnModels, oldMenu = SessionSettings.MenuOpen;
            try
            {
                SessionSettings.MenuOpen = false;   // flying, not the landing page
                SessionSettings.UseAirframeModels = SessionSettings.UseOwnModels = true;
                XrTrackedHands.Reader = (left, p, r) =>
                {
                    int s = left ? 0 : 1;
                    if (!Tracked[s]) return false;
                    Array.Copy(JointPos[s], p, HandJoint.Count);
                    Array.Copy(JointRot[s], r, HandJoint.Count);
                    return true;
                };
                XrHeadset.ForceActiveForTesting = true;
                VrCockpitHands.DeltaTimeForTesting = Dt;
                string only = Environment.GetEnvironmentVariable("VRJ_ONLY");   // e.g. extra-300-like: one type, for iterating
                foreach (string id in Fleet) if (string.IsNullOrEmpty(only) || id == only) Aircraft(id);
                Debug.Log(_fails == 0 ? $"VRJ OK ({_passes} cases)" : $"VRJ PROBLEMS {_fails} ({_passes} passed)");
                exit = _fails == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                XrTrackedHands.Reader = oldReader;
                XrHeadset.ForceActiveForTesting = false;
                VrCockpitHands.DeltaTimeForTesting = -1f;
                SessionSettings.UseAirframeModels = oldModels; SessionSettings.UseOwnModels = oldOwn; SessionSettings.MenuOpen = oldMenu;
            }
            EditorApplication.Exit(exit);
        }

        /// <summary>How a real hand closes on a control — the ideal grips and the imperfect ones a tracked hand
        /// really produces.</summary>
        private sealed class Grip
        {
            public string Name;
            public bool Pinch;
            public float Curl = 0.95f;          // the mean curl the fingers reach
            public Vector3 Shift;               // hand frame (x thumb side, y back of hand, z fingers): palm off the ideal
            public float EarlyM;                // the fingers finish closing this far short of the control, then move on
            public float ThumbNear;             // 0..1 thumb towards the index tip while still open
            public float JitterM;               // per-joint, per-frame tracking noise
        }

        private static readonly Grip[] Grips =
        {
            new() { Name = "fist" },
            new() { Name = "pinch", Pinch = true },
            new() { Name = "loose-fist", Curl = 0.62f },
            new() { Name = "fist-high", Shift = new Vector3(0f, 0.025f, -0.015f) },
            new() { Name = "fist-low", Shift = new Vector3(0f, -0.015f, 0.02f) },
            new() { Name = "fist-closed-early", EarlyM = 0.05f },
            new() { Name = "fist-thumb-near", ThumbNear = 0.75f },
            new() { Name = "fist-jitter", JitterM = 0.004f },
        };

        private static readonly System.Random Noise = new(7);

        private static void Fail(string m) { _fails++; Debug.LogError("VRJ FAIL " + m); }
        private static void Pass(string m) { _passes++; Debug.Log("VRJ PASS " + m); }

        private static object Call(object o, string name, params object[] args)
        {
            MethodInfo m = o.GetType().GetMethod(name, Any);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            return m.Invoke(o, args);
        }

        private static object Get(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, Any);
            if (f == null) throw new MissingFieldException(o.GetType().Name, name);
            return f.GetValue(o);
        }

        private static void Set(object o, string name, object v)
        {
            FieldInfo f = o.GetType().GetField(name, Any);
            if (f == null) throw new MissingFieldException(o.GetType().Name, name);
            f.SetValue(o, v);
        }

        private sealed class Scene
        {
            public GameObject Root, CamGo;
            public FlightSimDriver Driver;
            public CockpitView Cockpit;
            public QuestRig Rig;
            public VrCockpitHands Hands;
            public TouchFlightControls Touch;
            public AirframeVisual Visual;
            public XrHeadPose Head;     // QuestRig._head
            public Vector3 HeadTrackPos;
            public float RoomYaw;
        }

        private static void Aircraft(string id)
        {
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
            if (cfg == null) { Fail($"{id}: no config"); return; }
            int seats = 2;   // trimmed to the cockpit's own count once it is built
            for (int seat = 0; seat < seats; seat++)
                foreach (float yaw in new[] { 0f, 90f })
                {
                    Scene sc = null;
                    try
                    {
                        sc = Build(id, cfg, seat, yaw);
                        if (seat >= sc.Cockpit.SeatCount) { seats = sc.Cockpit.SeatCount; break; }
                        string seatId = SeatChoice.IdAt(sc.Cockpit.Seats, seat) ?? seat.ToString();
                        string tag = $"{id} seat {seatId} room {yaw:F0}°";
                        if (yaw == 0f) { Frames(sc, 2); Neighbours(sc, tag); }
                        foreach (Grip grip in Grips)
                        {
                            Reset(sc); StickCase(sc, $"{tag} STICK {grip.Name}", grip);
                            if (cfg.Propulsion != null) { Reset(sc); ThrottleCase(sc, $"{tag} THROTTLE {grip.Name}", grip); }
                        }
                        if ((CanopyLatchKind)Get(sc.Hands, "_latchKind") == CanopyLatchKind.Squeeze)
                            foreach (Grip grip in Grips)
                                foreach (string mode in new[] { "front", "both", "rear" })
                                { Reset(sc); CanopyHandleCase(sc, $"{tag} CANOPY-HANDLE {mode} {grip.Name}", grip, mode); }
                    }
                    catch (Exception e) { Fail($"{id} seat {seat}: {e.GetType().Name} {e.Message}"); Debug.LogException(e); }
                    finally
                    {
                        if (sc != null) { Object.DestroyImmediate(sc.Root); Object.DestroyImmediate(sc.CamGo); }
                    }
                }
        }

        private static Scene Build(string id, AircraftConfig cfg, int seat, float roomYaw)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sc = new Scene { RoomYaw = roomYaw };
            sc.CamGo = new GameObject("Main Camera") { tag = "MainCamera" };
            sc.CamGo.AddComponent<Camera>();
            ChaseCamera chase = sc.CamGo.AddComponent<ChaseCamera>();
            chase.CurrentView = ChaseCamera.View.Cockpit;
            sc.Rig = sc.CamGo.AddComponent<QuestRig>();
            sc.Hands = sc.CamGo.AddComponent<VrCockpitHands>();

            sc.Root = new GameObject("Aircraft");
            sc.Driver = sc.Root.AddComponent<FlightSimDriver>();
            sc.Touch = sc.Root.AddComponent<TouchFlightControls>();
            AirframeVisual visual = sc.Visual = sc.Root.AddComponent<AirframeVisual>();
            sc.Cockpit = sc.Root.AddComponent<CockpitView>();
            sc.Cockpit.Driver = sc.Driver;
            chase.Driver = sc.Driver; chase.Target = sc.Root.transform; chase.Cockpit = sc.Cockpit;

            typeof(FlightSimDriver).GetProperty("AircraftId", Any).SetValue(sc.Driver, id);
            sc.Driver.AdoptSim(new FlyingGame.Sim.SimLoop(new FlyingGame.Sim.Aircraft(cfg, new RigidBodyState())));
            Call(visual, "Awake");
            Call(visual, "Rebuild");
            sc.Cockpit.SelectSeat(seat, remember: false);
            typeof(ChaseCamera).GetProperty("InCockpit", Any).SetValue(null, true);
            Call(sc.Touch, "Awake");
            Call(sc.Hands, "OnEnable");
            Call(sc.Rig, "StartRig");

            // The headset's recentre: the head somewhere in the room, facing roomYaw.
            sc.HeadTrackPos = new Vector3(0.4f, 1.15f, -0.7f);
            sc.Head = (XrHeadPose)Get(sc.Rig, "_head");
            Quaternion hq = Quaternion.Euler(0f, roomYaw, 0f);
            sc.Head.Recentre(new Vec3(sc.HeadTrackPos.x, sc.HeadTrackPos.y, sc.HeadTrackPos.z), new Quat(hq.x, hq.y, hq.z, hq.w));
            return sc;
        }

        private static void Reset(Scene sc)
        {
            Tracked[0] = Tracked[1] = false;
            _jitter = 0f;
            // Fresh hands each case: past the detector's 0.25 s hold-through-loss, so whatever was held is let go.
            Frames(sc, 30);
        }

        // ---- seat ↔ tracking: the inverse of QuestRig.TrackingToSeat (XrHeadPose.ToSeat is a yaw + an offset) ----

        private static Vector3 ToTracking(Scene sc, Vector3 seatPos)
        {
            Vec3 o = sc.Head.ToSeat(new Vec3(sc.HeadTrackPos.x, sc.HeadTrackPos.y, sc.HeadTrackPos.z));
            Vector3 origin = new((float)o.X, (float)o.Y, (float)o.Z);   // ~0: the recentre point is the seat's eye
            Quaternion toSeat = ToSeatRot(sc);
            return sc.HeadTrackPos + Quaternion.Inverse(toSeat) * (seatPos - origin);
        }

        private static Quaternion ToSeatRot(Scene sc)
        {
            Quat q = sc.Head.ToSeat(Quat.Identity);
            return new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
        }

        /// <summary>Seat-frame point (aeroplane local, relative to the eye's seat origin _eye) → what the tracker reports.</summary>
        private static float _jitter;

        private static void PutHand(Scene sc, bool left, Vector3 palmLocal, Quaternion palmRotLocal, float curl, float thumbPinch)
        {
            Vector3 eye = (Vector3)Get(sc.Hands, "_eye");
            int s = left ? 0 : 1;
            SynthHand(left, palmLocal - eye, palmRotLocal, curl, thumbPinch, JointPos[s], JointRot[s]);
            if (_jitter > 0f)
                for (int i = 0; i < HandJoint.Count; i++)
                    JointPos[s][i] += new Vector3((float)Noise.NextDouble() - 0.5f, (float)Noise.NextDouble() - 0.5f, (float)Noise.NextDouble() - 0.5f) * (2f * _jitter);
            Quaternion inv = Quaternion.Inverse(ToSeatRot(sc));
            for (int i = 0; i < HandJoint.Count; i++)
            {
                JointPos[s][i] = ToTracking(sc, JointPos[s][i]);
                JointRot[s][i] = inv * JointRot[s][i];
            }
            Tracked[s] = true;
        }

        /// <summary>
        /// An adult hand's 26 XR Hands joints (Wrist, Palm, Thumb ×4, then Index/Middle/Ring/Little ×5: metacarpal,
        /// proximal, intermediate, distal, tip), in Unity's convention: the palm joint's +Z toward the fingers, +Y out
        /// of the BACK of the hand. curl 0 = flat, 1 = a full fist (MCP 90°, PIP 105°, DIP 70°). thumbPinch 0..1 brings
        /// the thumb tip onto the index tip.
        /// </summary>
        public static void SynthHand(bool left, Vector3 palm, Quaternion rot, float curl, float thumbPinch, Vector3[] p, Quaternion[] r)
        {
            Vector3 f = rot * Vector3.forward, back = rot * Vector3.up, palmar = -back;
            Vector3 thumbSide = rot * (left ? Vector3.right : Vector3.left);
            p[HandJoint.Wrist] = palm - f * 0.045f;
            p[HandJoint.Palm] = palm;
            float[] off = { 0.024f, 0.006f, -0.012f, -0.028f };
            float[] lp = { 0.045f, 0.048f, 0.044f, 0.036f }, li = { 0.026f, 0.030f, 0.028f, 0.021f }, ld = { 0.023f, 0.024f, 0.024f, 0.021f };
            for (int k = 0; k < 4; k++)
            {
                int m = HandJoint.Finger(k);
                Vector3 knuckle = palm + f * (0.045f - 0.004f * k) + thumbSide * off[k];
                p[m] = p[HandJoint.Wrist] + thumbSide * off[k] * 0.6f;
                p[m + 1] = knuckle;
                float a1 = 90f * curl, a2 = a1 + 105f * curl, a3 = a2 + 70f * curl;
                Vector3 D(float deg) => (f * Mathf.Cos(deg * Mathf.Deg2Rad) + palmar * Mathf.Sin(deg * Mathf.Deg2Rad)).normalized;
                p[m + 2] = p[m + 1] + D(a1) * lp[k];
                p[m + 3] = p[m + 2] + D(a2) * li[k];
                p[m + 4] = p[m + 3] + D(a3) * ld[k];
            }
            // Thumb: from the wrist's thumb side, out and forward when open; over the index's middle phalanx in a fist;
            // onto the index tip in a pinch.
            Vector3 tm = p[HandJoint.Wrist] + thumbSide * 0.022f + palmar * 0.01f;
            Vector3 tp = palm + thumbSide * 0.04f + palmar * 0.015f - f * 0.005f;
            Vector3 openTip = tp + (f * 0.8f + thumbSide * 0.5f + palmar * 0.2f).normalized * 0.06f;
            Vector3 fistTip = p[HandJoint.IndexMetacarpal + 2] + back * 0.012f;
            Vector3 tip = Vector3.Lerp(Vector3.Lerp(openTip, fistTip, Mathf.Clamp01((curl - 0.3f) / 0.6f)), p[HandJoint.IndexTip], thumbPinch);
            p[HandJoint.ThumbMetacarpal] = tm;
            p[HandJoint.ThumbProximal] = tp;
            p[HandJoint.ThumbDistal] = Vector3.Lerp(tp, tip, 0.55f) + palmar * 0.005f;
            p[HandJoint.ThumbTip] = tip;
            for (int i = 0; i < HandJoint.Count; i++) r[i] = rot;
        }

        private static void Frames(Scene sc, int n)
        {
            for (int i = 0; i < n; i++)
            {
                Set(sc.Hands, "_tickFrame", -12345);
                // The order of a frame on the device: TouchFlightControls.Update ticks the hands and drives the driver.
                Call(sc.Touch, "Update");          // ReadPointers, the joystick fallbacks, QuestInput, PublishToDriver
                Call(sc.Hands, "LateUpdate");      // the body and the panel (Tick already ran this frame)
                Call(sc.Visual, "LateUpdate");     // the rig follows the driver's controls
            }
        }

        private static CockpitGrab Grab(Scene sc) => (CockpitGrab)Get(sc.Hands, "_grab");

        private static string Dists(Scene sc, bool left)
        {
            var hands = (HandPose[])Get(sc.Hands, "_hands");
            Vec3 p = hands[left ? 0 : 1].Position;
            CockpitGrab g = Grab(sc);
            var sb = new System.Text.StringBuilder($"grab point ({p.X:F3},{p.Y:F3},{p.Z:F3})");
            foreach (Grabbable id in new[] { Grabbable.CanopyLatchAft, Grabbable.CanopyLatchFwd, Grabbable.CanopyLatch, Grabbable.SillLeft, Grabbable.SillRight })
                if (g.Points[(int)id].Enabled) sb.Append($" {id} {Math.Sqrt(g.Points[(int)id].Distance2(p)) * 1000:F0} mm");
            return sb.ToString();
        }

        private static string Diag(Scene sc, bool left)
        {
            int s = left ? 0 : 1;
            var hands = (HandPose[])Get(sc.Hands, "_hands");
            var gap = (double[])Get(sc.Hands, "_gapM");
            var pinch = (bool[])Get(sc.Hands, "_pinchMode");
            CockpitGrab g = Grab(sc);
            HandSide side = left ? HandSide.Left : HandSide.Right;
            HandPose h = hands[s];
            double dS = Math.Sqrt(g.Points[(int)Grabbable.Stick].Distance2(h.Position));
            double dT = Math.Sqrt(g.Points[(int)Grabbable.Throttle].Distance2(h.Position));
            ControlInputs ci = sc.Driver.Inputs;
            return $"src {h.Source} tracked {h.Tracked} grip {h.Grip:F2} pinchMode {pinch[s]} gap {gap[s] * 100:F1} cm; hover {g.Hover(side)} held {g.Held(side)}; " +
                   $"hand→stick {dS * 100:F1} cm, →throttle {dT * 100:F1} cm; driver ail {ci.Aileron:+0.00;-0.00} ele {ci.Elevator:+0.00;-0.00} thr {ci.ThrottleLever:F2}";
        }

        /// <summary>Open hand 15 cm off, reach in over 0.4 s, close over 0.25 s, hold, then move.</summary>
        private static void Approach(Scene sc, bool left, Vector3 palmAt, Quaternion rot, Vector3 from, Grip g)
        {
            bool pinch = g.Pinch;
            _jitter = g.JitterM;
            Vector3 dir = (palmAt - from).normalized;
            Vector3 closeAt = palmAt - dir * g.EarlyM;
            for (int i = 0; i <= 30; i++)
            {
                PutHand(sc, left, Vector3.Lerp(from, closeAt, i / 30f), rot, 0.1f, g.ThumbNear);
                Frames(sc, 1);
            }
            for (int i = 0; i <= 18; i++)
            {
                float t = i / 18f;
                PutHand(sc, left, closeAt, rot, pinch ? 0.15f + 0.15f * t : 0.1f + (g.Curl - 0.1f) * t, pinch ? t : g.ThumbNear * (1f - t));
                Frames(sc, 1);
            }
            for (int i = 1; i <= 10 && g.EarlyM > 0f; i++) { PutHand(sc, left, Vector3.Lerp(closeAt, palmAt, i / 10f), rot, g.Curl, 0f); Frames(sc, 1); }
            Frames(sc, 10);
        }

        private static void StickCase(Scene sc, string tag, Grip grip)
        {
            bool pinch = grip.Pinch;
            CockpitGrab g = Grab(sc);
            if (!sc.Hands.Active) { Fail($"{tag}: VrCockpitHands not active"); return; }
            CockpitGrab.Point sp = g.Points[(int)Grabbable.Stick];
            Vector3 top = U(sp.HasSegment ? sp.SegmentEnd : sp.Position), bot = U(sp.Position);
            Vector3 axis = (top - bot).sqrMagnitude > 1e-6f ? (top - bot).normalized : Vector3.up;
            Vector3 gripAt = pinch ? top : Vector3.Lerp(bot, top, 0.6f);
            // A right hand round the grip, thumb up the stick, back of the hand to the right, fingers forward.
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, axis).normalized * -1f);
            // Check the thumb side really is up the stick (right hand: thumb side = rot * left).
            if (Vector3.Dot(rot * Vector3.left, axis) < 0f) rot = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, axis).normalized);
            Vector3 palm = pinch
                ? gripAt - rot * Vector3.forward * 0.09f - rot * Vector3.down * 0.03f    // fingertips at the top of the grip
                : gripAt - rot * Vector3.forward * 0.03f + rot * Vector3.up * 0.03f;     // the grip in the curled fingers
            palm += rot * new Vector3(-grip.Shift.x, grip.Shift.y, grip.Shift.z);   // (right hand: thumb side = −x)
            Vector3 from = palm + new Vector3(0.10f, 0.08f, -0.10f);
            Approach(sc, false, palm, rot, from, grip);
            if (!sc.Hands.StickHeld) { Fail($"{tag}: the hand closed on the stick but did not hold it — {Diag(sc, false)}"); return; }
            ControlInputs c0 = sc.Driver.Inputs;
            float curl = pinch ? 0.3f : grip.Curl, tp = pinch ? 1f : 0f;
            for (int i = 1; i <= 20; i++) { PutHand(sc, false, palm + Vector3.right * 0.05f * i / 20f, rot, curl, tp); Frames(sc, 1); }
            ControlInputs c1 = sc.Driver.Inputs;
            for (int i = 1; i <= 20; i++) { PutHand(sc, false, palm + Vector3.right * 0.05f * (1f - i / 20f) - Vector3.forward * 0.05f * i / 20f, rot, curl, tp); Frames(sc, 1); }
            ControlInputs c2 = sc.Driver.Inputs;
            double dAil = c1.Aileron - c0.Aileron, dEle = c2.Elevator - c1.Elevator;
            string res = $"held; 5 cm right → driver aileron {c0.Aileron:+0.00;-0.00}→{c1.Aileron:+0.00;-0.00}; 5 cm aft → elevator {c1.Elevator:+0.00;-0.00}→{c2.Elevator:+0.00;-0.00} (hands ail {sc.Hands.AileronInput:+0.00;-0.00} ele {sc.Hands.ElevatorInput:+0.00;-0.00})";
            if (Math.Abs(dAil) < 0.15 || Math.Abs(dEle) < 0.15) Fail($"{tag}: {res} — the stick did not move the controls; {Diag(sc, false)}");
            else Pass($"{tag}: {res}");
        }

        private static void ThrottleCase(Scene sc, string tag, Grip grip)
        {
            bool pinch = grip.Pinch;
            CockpitGrab g = Grab(sc);
            CockpitGrab.Point tp = g.Points[(int)Grabbable.Throttle];
            if (!tp.Enabled) { Fail($"{tag}: throttle point disabled"); return; }
            Vector3 knob = U(tp.Position);
            // Left hand on top of the knob: back of the hand up, fingers forward, thumb inboard.
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            Vector3 palm = pinch
                ? knob - Vector3.forward * 0.09f + Vector3.up * 0.03f
                : knob - Vector3.forward * 0.03f + Vector3.up * 0.03f;
            palm += rot * grip.Shift;   // (left hand: thumb side = +x)
            Vector3 from = palm + new Vector3(0.12f, 0.08f, -0.08f);
            Approach(sc, true, palm, rot, from, grip);
            if (!sc.Hands.ThrottleHeld) { Fail($"{tag}: the hand closed on the throttle but did not hold it — {Diag(sc, true)}"); return; }
            double t0 = sc.Driver.Inputs.ThrottleLever, h0 = sc.Hands.Throttle01;
            float curl = pinch ? 0.3f : grip.Curl, th = pinch ? 1f : 0f;
            for (int i = 1; i <= 30; i++) { PutHand(sc, true, palm + Vector3.forward * 0.08f * i / 30f, rot, curl, th); Frames(sc, 1); }
            double t1 = sc.Driver.Inputs.ThrottleLever;
            for (int i = 1; i <= 30; i++) { PutHand(sc, true, palm + Vector3.forward * 0.08f * (1f - i / 30f) - Vector3.forward * 0.06f * i / 30f, rot, curl, th); Frames(sc, 1); }
            double t2 = sc.Driver.Inputs.ThrottleLever;
            string res = $"held; 8 cm fwd → driver throttle {t0:F2}→{t1:F2}; back 14 cm → {t2:F2} (hands {h0:F2}→{sc.Hands.Throttle01:F2})";
            if (Math.Abs(t1 - t0) < 0.1 && Math.Abs(t2 - t1) < 0.1) Fail($"{tag}: {res} — the lever did not move the throttle; {Diag(sc, true)}");
            else Pass($"{tag}: {res}");
        }

        /// <summary>
        /// THE EXTRA'S CANOPY HANDLES (Ty t289u: two 25 mm × 127 mm lateral cylinders on the canopy frame, the rear one
        /// fixed, the front one spring-loaded forward; squeezing them together pulls the pins). A tracked hand closes on
        /// each handle's own grab point and must HOLD that handle (CanopyLatchFwd / CanopyLatchAft, not the pair's
        /// midpoint), then:
        ///   front  the left hand alone pulls the front handle 40 mm aft  → pins clear, latch UNLOCKED;
        ///   both   right hand on the rear, left on the front, the hands close 40 mm together → UNLOCKED;
        ///   rear   the right hand alone pulls the FIXED rear handle 40 mm aft → still LOCKED.
        /// Released, no hand holds either handle. (The handle's spring runs on Time.deltaTime, which is 0 in an editor
        /// batch run, so the spring return and re-lock are checked by stepping the latch itself, shut, for 0.1 s.)
        /// The hand sits OVER the bar, the bar in the curled fingers (a pinch: finger and thumb tips on it), so the
        /// handle's own point is nearer than the pair's midpoint 45 mm away.
        /// </summary>
        private static void CanopyHandleCase(Scene sc, string tag, Grip grip, string mode)
        {
            CockpitGrab g = Grab(sc);
            CockpitRigState cockpit = sc.Visual.Cockpit;
            if (cockpit?.HandLatch is not ExtraCanopyLatch latch) { Fail($"{tag}: no Extra canopy latch"); return; }
            latch.Reset(); cockpit.SetCanopyLock(true); Frames(sc, 2);   // the editor's dt is 0: nothing springs back between cases
            if (!latch.Locked) { Fail($"{tag}: the latch is not locked at the start"); return; }
            CockpitGrab.Point fp = g.Points[(int)Grabbable.CanopyLatchFwd], ap = g.Points[(int)Grabbable.CanopyLatchAft];
            if (!fp.Enabled || !ap.Enabled) { Fail($"{tag}: handle points disabled (fwd {fp.Enabled}, aft {ap.Enabled})"); return; }
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, Vector3.up);   // over the bar, fingers wrapping forward
            float curl = grip.Pinch ? 0.3f : grip.Curl, th = grip.Pinch ? 1f : 0f;
            // A pinch takes hold with the thumb-index midpoint (VrCockpitHands), so put THAT on the bar: the synthetic
            // hand's own palm→pinch-point offset, per hand (the thumb is on the inboard side of each).
            Vector3 PinchOffset(bool left)
            {
                var pp = new Vector3[HandJoint.Count]; var rr = new Quaternion[HandJoint.Count];
                SynthHand(left, Vector3.zero, rot, curl, th, pp, rr);
                return 0.5f * (pp[HandJoint.ThumbTip] + pp[HandJoint.IndexTip]) - pp[HandJoint.Palm];
            }
            Vector3 Palm(CockpitGrab.Point p, bool left) => (grip.Pinch ? U(p.Position) - PinchOffset(left)
                                                                        : U(p.Position) + Vector3.up * 0.03f) + rot * grip.Shift;
            Vector3 fPalm = Palm(fp, true), aPalm = Palm(ap, false);
            bool useFwd = mode != "rear", useAft = mode != "front";
            if (useAft)
            {
                Approach(sc, false, aPalm, rot, aPalm + new Vector3(0.10f, 0.08f, -0.10f), grip);
                if (!g.IsHeldBy(Grabbable.CanopyLatchAft, out HandSide s) || s != HandSide.Right)
                { Fail($"{tag}: the right hand closed on the REAR handle but does not hold it (hover {g.Hover(HandSide.Right)}, held {g.Held(HandSide.Right)}; {Dists(sc, false)}) — {Diag(sc, false)}"); return; }
                if (mode == "rear") Debug.Log($"VRJ INFO {tag}: right hand {Dists(sc, false)}");
            }
            if (useFwd)
            {
                Approach(sc, true, fPalm, rot, fPalm + new Vector3(0.12f, 0.08f, 0.06f), grip);
                if (!g.IsHeldBy(Grabbable.CanopyLatchFwd, out HandSide s) || s != HandSide.Left)
                { Fail($"{tag}: the left hand closed on the FRONT handle but does not hold it (hover {g.Hover(HandSide.Left)}, held {g.Held(HandSide.Left)}) — {Diag(sc, true)}"); return; }
            }
            bool lock0 = latch.Locked;
            double maxS = 0;
            var hp = (HandPose[])Get(sc.Hands, "_hands");
            double z0L = hp[0].Position.Z, z0R = hp[1].Position.Z;
            int lostAt = -1;
            for (int i = 1; i <= 30; i++)
            {
                // A pinch closes at a 20 mm thumb-index gap and the fingers go on closing after it has taken hold, so its
                // grab point drifts ~10-15 mm FORWARD before the pull starts (the latch's reference is the grab moment):
                // a pinching pilot pulls that much further. The fist's 40 mm is 5 mm past the handle's 35 mm travel.
                float d = (grip.Pinch ? 0.055f : 0.040f) * i / 30f;
                if (mode == "both") { PutHand(sc, true, fPalm - Vector3.forward * d * 0.75f, rot, curl, th); PutHand(sc, false, aPalm + Vector3.forward * d * 0.25f, rot, curl, th); }
                else if (mode == "front") PutHand(sc, true, fPalm - Vector3.forward * d, rot, curl, th);
                else PutHand(sc, false, aPalm - Vector3.forward * d, rot, curl, th);
                Frames(sc, 1);
                maxS = Math.Max(maxS, latch.S);
                if (lostAt < 0 && ((useFwd && !g.IsHeld(Grabbable.CanopyLatchFwd)) || (useAft && !g.IsHeld(Grabbable.CanopyLatchAft)))) lostAt = i;
            }
            hp = (HandPose[])Get(sc.Hands, "_hands");
            string moved = $"grab points moved L {(hp[0].Position.Z - z0L) * 1000:F0} mm R {(hp[1].Position.Z - z0R) * 1000:F0} mm (z)" + (lostAt > 0 ? $", hold LOST at frame {lostAt}" : "");
            bool lock1 = latch.Locked, rigLock1 = cockpit.CanopyLocked;
            // Let go: the hands open and leave.
            for (int i = 0; i <= 12; i++)
            {
                if (useFwd) PutHand(sc, true, fPalm - Vector3.forward * 0.03f + Vector3.up * 0.01f * i, rot, curl * (1f - i / 12f) + 0.1f * i / 12f, 0f);
                if (useAft) PutHand(sc, false, aPalm + Vector3.up * 0.01f * i, rot, curl * (1f - i / 12f) + 0.1f * i / 12f, 0f);
                Frames(sc, 1);
            }
            Tracked[0] = Tracked[1] = false;
            Frames(sc, 30);
            bool stillHeld = g.IsHeld(Grabbable.CanopyLatchFwd) || g.IsHeld(Grabbable.CanopyLatchAft) || g.IsHeld(Grabbable.CanopyLatch);
            double sRel = latch.S;
            for (int i = 0; i < 100; i++) latch.Step(0.001, default, true, 0.0);
            bool lock2 = latch.Locked && !stillHeld;
            string res = $"locked {lock0} → after {(grip.Pinch ? 55 : 40)} mm {lock1} (rig {rigLock1}, max s {maxS:F2}) → let go (held {stillHeld}, s {sRel:F2}) → spring 0.1 s: locked {latch.Locked} (s {latch.S:F2}); {moved}";
            bool want = mode == "rear";
            if (lock1 != want || rigLock1 != want || !lock2) Fail($"{tag}: {res} — expected {(want ? "still locked" : "unlocked")} then relocked; {Diag(sc, true)}");
            else Pass($"{tag}: {res}");
        }

        /// <summary>Every other enabled grab point within 12 cm of the stick's grip and the throttle knob: a hand closing
        /// a few centimetres off the control takes whichever point is NEAREST (CockpitGrab.Nearest).</summary>
        private static void Neighbours(Scene sc, string tag)
        {
            CockpitGrab g = Grab(sc);
            foreach (Grabbable c in new[] { Grabbable.Stick, Grabbable.Throttle })
            {
                CockpitGrab.Point cp = g.Points[(int)c];
                if (!cp.Enabled) continue;
                Vec3 at = cp.HasSegment ? cp.Position + (cp.SegmentEnd - cp.Position) * 0.6 : cp.Position;
                var near = new List<string>();
                for (int i = 1; i < g.Points.Length; i++)
                {
                    CockpitGrab.Point p = g.Points[i];
                    if (!p.Enabled || p.Id == c) continue;
                    double d = Math.Sqrt(p.Distance2(at));
                    if (d < 0.12) near.Add($"{p.Id} {d * 100:F1} cm");
                }
                Debug.Log($"VRJ NEAR {tag} {c} (grip at {U(at) - (Vector3)Get(sc.Hands, "_eye")} from the eye): " + (near.Count == 0 ? "nothing else within 12 cm" : string.Join(", ", near)));
            }
        }

        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }
}

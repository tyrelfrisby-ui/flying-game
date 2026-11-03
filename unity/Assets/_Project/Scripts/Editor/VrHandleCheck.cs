using System;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Can a hand in the headset actually take the 2-33's handles, and do they go the right way? Builds the aeroplane
    /// exactly as the game does (as SgsRigCheck does), binds the rig, and MEASURES each handle with the same code the
    /// headset uses (<see cref="RigHandle"/>: the knob from the node's own meshes, its two ends from the rig's own
    /// rotation), in the aeroplane's frame, against the seat's eye:
    ///   - DiveBrake_Lever_F / _R: 33° of swing, the knob moving AFT from stowed to full, on the LEFT, within reach;
    ///   - Trim_Lever: 40° of swing, AFT = nose up (+1), on the left, within reach of the front seat;
    ///   - TowRelease_F / _R: 50 mm of pull, straight AFT, within reach.
    ///
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.VrHandleCheck.Run
    ///
    /// Exit 0 = every check passed; the log lines start "HANDLE".
    /// </summary>
    public static class VrHandleCheck
    {
        private const string Type = "glider-2-33-like";
        /// <summary>A seated pilot's comfortable reach from the eye to a knob, metres (shoulder 0.2 m below and 0.2 m to
        /// the side of the eye, a 0.65 m arm): anything past it a pilot would have to lean for.</summary>
        private const float ReachM = 0.85f;

        private static int _fails;

        /// <summary>Every aeroplane the Quest flies with a rig (QuestFleet).</summary>
        private static readonly string[] Fleet =
            { "glider-2-33-like", "extra-300-like", "pitts-s2b-like", "great-lakes-like", "j3-cub-like", "j3-cub-floats-like" };

        public static void Run()
        {
            int exit = 1;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;
                var root = new GameObject(Type);
                var builder = new AirframeBuilder();
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Type);
                builder.Build(root.transform, cfg);
                if (!builder.HasOwnRig) throw new InvalidOperationException("the 2-33's own rig did not bind — nothing to check.");

                Vector3 eyeF = Eye(cfg, "front"), eyeR = Eye(cfg, "rear");
                Debug.Log($"HANDLE eyes (aeroplane frame, m): front {eyeF:F3}, rear {eyeR:F3}");
                Lever(builder.Rig, root.transform, "DiveBrake_Lever_F", eyeF, 33f, wantAft: true);
                Lever(builder.Rig, root.transform, "DiveBrake_Lever_R", eyeR, 33f, wantAft: true);
                Lever(builder.Rig, root.transform, "Trim_Lever", eyeF, 40f, wantAft: true);
                Slide(builder.Rig, root.transform, "TowRelease_F", eyeF, 0.05f);
                Slide(builder.Rig, root.transform, "TowRelease_R", eyeR, 0.05f);
                foreach (string t in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like" }) Panel(t);
                foreach (string t in Fleet) Controls(t);
                exit = _fails == 0 ? 0 : 1;
                Debug.Log(exit == 0 ? "HANDLE OK" : $"HANDLE PROBLEMS: {_fails} — see the errors above");
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(exit);
        }

        private static Vector3 Eye(AircraftConfig cfg, string seat)
        {
            foreach (StationConfig s in cfg.Loading.Seats)
                if (s.Id == seat) return CoordinateMap.ToUnity(s.EyeVec());
            Fail($"no '{seat}' seat in {Type}");
            return Vector3.zero;
        }

        private static void Fail(string m) { _fails++; Debug.LogError("HANDLE FAIL " + m); }

        /// <summary>"12 cm fwd, 30 cm down, 25 cm left" — the aeroplane's own frame, from the eye.</summary>
        private static string From(Vector3 eye, Vector3 p)
        {
            Vector3 d = (p - eye) * 100f;
            return $"{Mathf.Abs(d.z):F0} cm {(d.z >= 0 ? "fwd" : "aft")}, {Mathf.Abs(d.y):F0} cm {(d.y >= 0 ? "up" : "down")}, {Mathf.Abs(d.x):F0} cm {(d.x >= 0 ? "right" : "left")} of the eye ({(p - eye).magnitude * 100f:F0} cm)";
        }

        private static void Lever(AirframeRig rig, Transform air, string node, Vector3 eye, float wantSweep, bool wantAft)
        {
            RigHandle h = RigHandle.Find(rig, node);
            if (h == null) { Fail($"{node} is not bound"); return; }
            int verts = RigHandle.VertexCount(h.T);
            Vector3 pivot = air.InverseTransformPoint(h.PivotWorld);
            Vector3 k0 = air.InverseTransformPoint(h.KnobWorld(h.V0)), k1 = air.InverseTransformPoint(h.KnobWorld(h.V1));
            var lever = new HingedLever(C(pivot), C(k0), C(k1), C(air.InverseTransformDirection(h.HingeWorld)));
            Debug.Log($"HANDLE {node}: {verts} vertices; pivot {From(eye, pivot)}; knob at {h.P0:+0.##;-0.##;0} {From(eye, k0)}; " +
                      $"at {h.P1:+0.##;-0.##;0} {From(eye, k1)}; arm {lever.ArmM * 100:F1} cm ({lever.AxialM * 100:F1} cm along the hinge), sweep {lever.SweepDeg:F1}° about ({lever.Axis.X:F2}, {lever.Axis.Y:F2}, {lever.Axis.Z:F2})");
            if (verts == 0) Fail($"{node}: no readable mesh — the knob fell back to the pivot");
            if (System.Math.Abs(lever.SweepDeg - wantSweep) > 1.0) Fail($"{node}: sweeps {lever.SweepDeg:F1}°, the rig says {wantSweep}°");
            if (lever.ArmM < 0.05 || lever.ArmM > 0.45) Fail($"{node}: a {lever.ArmM * 100:F0} cm arm is not a hand lever");
            float dz = k1.z - k0.z;
            if (wantAft && dz > -0.02f) Fail($"{node}: the knob moves {dz * 100:F1} cm fore-aft from {h.P0} to {h.P1}; it must go AFT");
            if (System.Math.Abs(lever.Axis.X) < 0.9) Fail($"{node}: hinge axis ({lever.Axis.X:F2}, {lever.Axis.Y:F2}, {lever.Axis.Z:F2}) is not lateral");
            if (k0.x > eye.x || k1.x > eye.x) Fail($"{node}: the knob is RIGHT of the eye; the 2-33's levers are on the left wall");
            float reach = Mathf.Max((k0 - eye).magnitude, (k1 - eye).magnitude);
            if (reach > ReachM) Fail($"{node}: {reach * 100:F0} cm from the eye, out of reach");
        }

        private static void Slide(AirframeRig rig, Transform air, string node, Vector3 eye, float wantTravel)
        {
            RigHandle h = RigHandle.Find(rig, node);
            if (h == null) { Fail($"{node} is not bound"); return; }
            int verts = RigHandle.VertexCount(h.T);
            Vector3 home = air.InverseTransformPoint(h.KnobWorld(h.V0)), out1 = air.InverseTransformPoint(h.KnobWorld(h.V1));
            Vector3 d = out1 - home;
            Debug.Log($"HANDLE {node}: {verts} vertices; knob home {From(eye, home)}; pulled ({d.x * 1000:F0}, {d.y * 1000:F0}, {d.z * 1000:F0}) mm (x right, y up, z fwd)");
            if (verts == 0) Fail($"{node}: no readable mesh");
            if (Mathf.Abs(d.magnitude - wantTravel) > 0.002f) Fail($"{node}: pulls {d.magnitude * 1000:F0} mm, the rig says {wantTravel * 1000:F0}");
            if (d.z > -0.9f * d.magnitude) Fail($"{node}: the pull is not straight aft");
            if ((home - eye).magnitude > ReachM) Fail($"{node}: {(home - eye).magnitude * 100:F0} cm from the eye, out of reach");
        }

        /// <summary>
        /// The switches and knobs (Codex's `controls`): for each seat, every control's node bound, its Grip_ and Poke_
        /// empties found under it, within reach of that seat's eye, and its travel as the rig turns it (a toggle's bat
        /// and a push-pull's shaft through RigHandle). Reach is a WARN here: Codex's positions are estimates (E) awaiting
        /// Ty's review, and moving them is the model's job.
        /// </summary>
        private static void Panel(string type)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(type);
            var builder = new AirframeBuilder();
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(type);
            builder.Build(root.transform, cfg);
            ModelRig table = builder.Rig?.Table;
            if (table == null) { Fail($"{type}: no rig bound"); return; }
            Debug.Log($"HANDLE {type}: rig {table.Model}, {table.Nodes.Count} rows after de-duplication, {table.Controls.Count} controls");
            foreach (string seat in new[] { "Rear", "Front" })
            {
                Vector3 eye = Eye(cfg, seat.ToLowerInvariant());
                int n = 0, far = 0;
                float worst = 0f; string worstName = "";
                foreach (FlyingGame.Core.Vr.PanelControlSpec c in table.Controls)
                {
                    if (c.Seat != seat) continue;
                    n++;
                    Transform grip = builder.FindInModel(c.Grip), poke = builder.FindInModel(c.Poke);
                    RigHandle h = RigHandle.Find(builder.Rig, c.Node, grip);
                    if (h == null) { Fail($"{type} {c.Node} is not bound"); continue; }
                    if (grip == null) Fail($"{type} {c.Node}: no {c.Grip}");
                    if (poke == null) Fail($"{type} {c.Node}: no {c.Poke}");
                    if (grip != null && !grip.IsChildOf(h.T)) Fail($"{type} {c.Grip} does not ride {c.Node}");
                    Vector3 k = root.transform.InverseTransformPoint(h.KnobWorld(h.V0));
                    float d = (k - eye).magnitude;
                    if (d > worst) { worst = d; worstName = c.Short; }
                    // Codex's panel positions are estimates (E) awaiting Ty's review: reach is reported, not failed.
                    if (d > ReachM) { far++; Debug.LogWarning($"HANDLE WARN {type} {seat} {c.Short}: {d * 100:F0} cm from the eye, past a comfortable reach ({ReachM * 100:F0} cm)"); }
                    if (c.Kind != FlyingGame.Core.Vr.PanelKind.Rotary)
                    {
                        Vector3 k1 = root.transform.InverseTransformPoint(h.KnobWorld(h.V1));
                        if ((k1 - k).magnitude < 0.003f) Fail($"{type} {seat} {c.Short}: its knob moves {(k1 - k).magnitude * 1000:F1} mm end to end");
                    }
                }
                Debug.Log($"HANDLE {type} {seat}: {n} controls; farthest {worstName} {worst * 100:F0} cm from the eye; {far} out of reach");
                if (n != 22) Fail($"{type} {seat}: {n} controls, the inventory says 22");
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>
        /// THE STICK AND THE THROTTLE, EVERY QUEST AEROPLANE, EVERY SEAT (Ty 2026-11-02: "verify grab for the stick and
        /// throttle in every aircraft with an automated test that places a tracked hand at each control and asserts the
        /// input changes"). Through the binding the headset ships (<see cref="VrControlBinding"/>):
        ///   - the stick and throttle bind on the rig (not the proxy) for every seat with a node;
        ///   - 1:1: the gimbal's grip matches the rig's own pose of the stick within 1 cm across its travel;
        ///   - a TRACKED hand closed on the visible grip (2 cm off it) grabs the stick, and moving it 5 cm right / aft /
        ///     forward moves aileron right, elevator aft, elevator forward;
        ///   - a tracked hand closed on the throttle knob grabs the throttle, and pushing it 6 cm forward opens it.
        /// </summary>
        private static void Controls(string type)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(type);
            var builder = new AirframeBuilder();
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(type);
            builder.Build(root.transform, cfg);
            Transform air = root.transform;
            if (builder.Rig == null) { Fail($"{type}: no rig bound"); UnityEngine.Object.DestroyImmediate(root); return; }
            bool engine = cfg.Propulsion != null;
            foreach (StationConfig seat in cfg.Loading.Seats)
            {
                if (seat.Id != "rear" && seat.Id != "front") continue;
                string tag = $"{type} {seat.Id}";
                VrControlBinding.Stick s = VrControlBinding.BindStick(builder.Rig, builder.FindInModel, air, seat.Id);
                if (s == null) { Fail($"STICK {tag}: not on the rig (no Stick_* elevator node) — the proxy would be used"); }
                else StickCheck(tag, s, air);
                if (!engine) { Debug.Log($"THROTTLE {tag}: none (no engine)"); continue; }
                VrControlBinding.Throttle th = VrControlBinding.BindThrottle(builder.Rig, builder.FindInModel, air, seat.Id);
                if (th == null) { Fail($"THROTTLE {tag}: not on the rig (no Throttle_* node) — the proxy would be used"); continue; }
                ThrottleCheck(tag, th, s);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static HandPose Hand(Vec3 at, double grip) =>
            new() { Source = HandSource.TrackedHand, Position = at, Rotation = Quat.Identity, Grip = grip };

        private static void StickCheck(string tag, VrControlBinding.Stick s, Transform air)
        {
            StickGimbal g = s.Gimbal;
            RigHandle roll = s.Roll, pitch = s.Pitch;
            Vec3 g0 = g.GripAt(0, 0), pv = g.Pivot;
            Debug.Log($"STICK {tag}: {roll?.Name ?? "no roll node"} / {pitch.Name}; grip {s.GripSource}; {g.LengthM * 100:F1} cm; " +
                      $"grip {(g0.Y - pv.Y) * 100:F1} up / {(g0.Z - pv.Z) * 100:F1} fwd / {(g0.X - pv.X) * 100:F1} right of the pitch pivot; " +
                      $"roll {g.RollMaxDeg:F0}°, {g.AftMaxDeg:F0}° aft / {g.FwdMaxDeg:F0}° fwd; bind check {s.SenseResidualM * 1000:F1} mm");

            // 1:1 against the rig's own pose (roll outer, pitch inner), at rest and across the travel.
            Quaternion kp = pitch.T.localRotation, kr = roll != null ? roll.T.localRotation : Quaternion.identity;
            double worst = 0;
            try
            {
                roll?.Pose(0.0); pitch.Pose(0.0);
                Vector3 gripInPitch = pitch.T.InverseTransformPoint(air.TransformPoint(U(g0)));
                foreach ((double a, double e) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0), (0.7, 0.7), (-0.7, -0.7), (0.5, -1.0) })
                {
                    double rv = roll != null ? VrControlBinding.Curve(roll.Node).Value(a) : 0, pvv = VrControlBinding.Curve(pitch.Node).Value(e);
                    roll?.Pose(rv); pitch.Pose(pvv);
                    Vector3 rig = air.InverseTransformPoint(pitch.T.TransformPoint(gripInPitch));
                    double err = (g.GripAt(roll != null ? rv : 0, pvv) - C(rig)).Length;
                    worst = System.Math.Max(worst, err);
                }
            }
            finally { pitch.T.localRotation = kp; if (roll != null) roll.T.localRotation = kr; }
            if (worst > 0.01) Fail($"STICK {tag}: the gimbal's grip is {worst * 1000:F0} mm from the rig's stick (want 1:1, < 10 mm)");

            // A tracked hand at the visible grip: 2 cm right of it and 4 cm down the stick (a fist round the grip).
            var grab = new CockpitGrab();
            (Vec3 a0, Vec3 b0) = VrControlBinding.StickCapsule(g);
            grab.Set(Grabbable.Stick, a0, b0, true);
            Vec3 at = g0 - g.Up * 0.04 + new Vec3(0.02, 0, 0);
            grab.Step(HandSide.Right, Hand(at, 0.0), false);
            GrabEvents ev = grab.Step(HandSide.Right, Hand(at, 1.0), false);
            if ((ev & GrabEvents.Grabbed) == 0 || grab.Held(HandSide.Right) != Grabbable.Stick)
            { Fail($"STICK {tag}: a tracked hand closed {System.Math.Sqrt(grab.Points[(int)Grabbable.Stick].Distance2(at)) * 100:F1} cm from the stick's capsule did not grab it"); return; }
            g.SetFromControls(0, 0);
            g.Grab(at);
            g.Follow(at + new Vec3(0.05, 0, 0)); double ail = g.Aileron;
            g.Follow(at + new Vec3(0, 0, -0.05)); double aft = g.ElevatorAft;
            g.Follow(at + new Vec3(0, 0, 0.05)); double fwd = g.ElevatorAft;
            g.Follow(at); double back = g.Aileron, backE = g.ElevatorAft;
            Debug.Log($"STICK {tag}: tracked hand grabbed; 5 cm right → aileron {ail:+0.00;-0.00}; 5 cm aft → elevator {aft:+0.00;-0.00} (aft +); 5 cm fwd → {fwd:+0.00;-0.00}; back → {back:+0.00;-0.00} / {backE:+0.00;-0.00}");
            if (ail < 0.15) Fail($"STICK {tag}: hand 5 cm right gave aileron {ail:F2} (want > +0.15, right)");
            if (aft < 0.15) Fail($"STICK {tag}: hand 5 cm aft gave elevator {aft:F2} (want > +0.15, aft)");
            if (fwd > -0.15) Fail($"STICK {tag}: hand 5 cm forward gave elevator {fwd:F2} (want < −0.15, forward)");
            if (System.Math.Abs(back) > 0.02 || System.Math.Abs(backE) > 0.02) Fail($"STICK {tag}: the hand back where it grabbed did not centre the stick");
        }

        private static void ThrottleCheck(string tag, VrControlBinding.Throttle th, VrControlBinding.Stick s)
        {
            HingedLever l = th.Lever;
            Vec3 k0 = l.GripAt(0), k1 = l.GripAt(1);
            Debug.Log($"THROTTLE {tag}: {th.Node.Name}, knob {th.GripSource}; arm {l.ArmM * 100:F1} cm, sweep {l.SweepDeg:F1}°; " +
                      $"knob idle→full moves ({(k1.X - k0.X) * 100:F1}, {(k1.Y - k0.Y) * 100:F1}, {(k1.Z - k0.Z) * 100:F1}) cm (x right, y up, z fwd)");
            if (k1.Z - k0.Z < 0.02) Fail($"THROTTLE {tag}: the knob does not move FORWARD from idle to full");
            var grab = new CockpitGrab();
            (Vec3 ta, Vec3 tb) = VrControlBinding.ThrottleCapsule(l, 0.0);
            grab.Set(Grabbable.Throttle, ta, tb, true);
            if (s != null) { (Vec3 sa, Vec3 sb) = VrControlBinding.StickCapsule(s.Gimbal); grab.Set(Grabbable.Stick, sa, sb, true); }
            Vec3 at = k0 + new Vec3(-0.015, 0.0, 0.0);
            grab.Step(HandSide.Left, Hand(at, 0.0), false);
            GrabEvents ev = grab.Step(HandSide.Left, Hand(at, 1.0), false);
            if ((ev & GrabEvents.Grabbed) == 0 || grab.Held(HandSide.Left) != Grabbable.Throttle)
            { Fail($"THROTTLE {tag}: a tracked hand on the knob grabbed {grab.Held(HandSide.Left)}, not the throttle"); return; }
            l.Grab(at, th.TFor(0.0));
            double idle = th.ThrottleFor(l.Follow(at));
            Vec3 fwdDir = (k1 - k0) * (1.0 / (k1 - k0).Length);
            double pushed = th.ThrottleFor(l.Follow(at + fwdDir * 0.06));
            double full = th.ThrottleFor(l.Follow(at + fwdDir * 0.6));
            Debug.Log($"THROTTLE {tag}: tracked hand grabbed; held {idle:F2}; 6 cm along the knob's travel → {pushed:F2}; far past → {full:F2}");
            if (idle > 0.02) Fail($"THROTTLE {tag}: grabbing at idle moved the throttle to {idle:F2}");
            if (pushed < 0.2) Fail($"THROTTLE {tag}: pushing the knob 6 cm forward gave throttle {pushed:F2} (want > 0.2)");
            if (full < 0.98) Fail($"THROTTLE {tag}: pushing far past the stop gave {full:F2} (want full)");
        }

        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        private static Vec3 C(Vector3 v) => new(v.x, v.y, v.z);
    }
}

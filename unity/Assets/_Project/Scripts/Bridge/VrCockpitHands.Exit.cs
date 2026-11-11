using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// "YOU CAN ALWAYS GET OUT" (Ty, 2026-11-02 13:28; docs/vr-cockpit.md P2d). Release the belt, then work the door handle
    /// or the canopy release — whichever the type has (Core ExitTable) — and the exit follows by itself (Core AutoExit):
    ///
    ///   in flight     the canopy opens (or goes), he rises out over the side and leaves as the P2 jumper (free fall, ripcord)
    ///   on the ground the door / canopy opens, a scripted climb (Core ClimbPath) carries his eye seat → sill → step → ground on
    ///                 the type's side, and he is on foot (Core GroundWalker): left stick walks (trigger or stick click runs),
    ///                 right stick turns only as the VR menu's "Turn on foot" says (snap 30°, smooth, off)
    ///   afloat        the same climb, over the sill into the water beside it, then he treads water (Core PilotSwim)
    ///   under water   the door waits on the water's load (Core DoorWaterLoad: Δp over its area, about its hinge, against a
    ///                 400 N push); then he goes out through the opening and swims up, and treads water at the surface
    ///
    /// A parachute landing on water (or land) ends the same way: treading water after the plunge, or on foot. The view is
    /// first person throughout: every move of it is a translation (smoothed: the climb is minimum-jerk, the walk
    /// acceleration-limited), and the only rotation is the one-time levelling as he stands up out of the seat — the same
    /// rule the bail-out already uses — plus the turn the player asks for.
    ///
    /// Water comes through the seam (Core EgressWater): LivePilotWater for the surface, LineageCockpitWater (a stated stub)
    /// for how far the cockpit has filled, until crash-water's AirframeFlooding replaces it (CrashWaterSeam.cs).
    /// </summary>
    public sealed partial class VrCockpitHands
    {
        /// <summary>The cockpit's floor below and its top (canopy / cabin roof) above the eye, m (E: a seated eye is 0.79 m
        /// over the cushion, which is ~0.15 m over the floor; the canopy 0.15–0.3 m over the head).</summary>
        public const float CockpitFloorBelowEyeM = 0.95f, CockpitTopAboveEyeM = 0.25f;
        /// <summary>A door swings open in this long once its handle is turned and the water lets it (E).</summary>
        public const float DoorOpenSec = 1.0f;
        /// <summary>In flight, the rise out over the sill once the way is clear (E: a deliberate stand-up).</summary>
        public const float AutoRiseSec = 1.2f;
        /// <summary>The automatic push on a canopy that will not open by itself: 60°/s on its hinge, after its slide (E).</summary>
        public const float AutoCanopyDps = 60f, AutoSlideMs = 0.25f;
        /// <summary>His body centre below the eye once he is standing or upright in the water (the jumper's 0.75 m).</summary>
        public const float EyeAboveCentreM = 0.75f;

        private enum ExitStage { None, Opening, Rising, Climbing, OnFoot, InWater }
        private static readonly Grabbable[] CueSlots =
            { Grabbable.HarnessMain, Grabbable.HarnessLap, Grabbable.CanopyLatch, Grabbable.CanopyLatchFwd, Grabbable.CanopyJettison, Grabbable.DoorHandle, Grabbable.CanopyRelease };

        private ExitSpec _exit;
        private HarnessRelease _doorHandle, _canopyRelease;
        private Transform _doorGrip;
        private readonly AutoExit _auto = new();
        private LineageCockpitWater _cockpitWater;
        private ExitStage _stage;
        private string _waitLogged;
        private float _exitT, _riseFrom;
        private bool _autoCanopy, _autoSqueeze;
        private double _autoBeta, _autoZ;
        private ClimbPath _path;
        private GroundWalker _walker;
        private PilotSwim _swim;
        private readonly List<KeepOutBox> _keepOut = new();
        private bool _pedestrian, _snapArmed = true, _wasInWater;
        private Vector3 _pedEye;
        private float _pedYaw;
        private double _entrySpeed;
        private bool _enteredWater;

        /// <summary>The pilot's eye is out of the aeroplane on foot or in the water, first person (the origin is ours).</summary>
        public bool OnFootOrSwimming => _pedestrian;
        /// <summary>His body is in the water (PilotWaterSound, the audio gate).</summary>
        public bool PilotInWater => _pedestrian && _swim != null;
        /// <summary>One frame: he just went in (the splash), and how fast.</summary>
        public bool TakeWaterEntry(out double speedMs) { speedMs = _entrySpeed; bool e = _enteredWater; _enteredWater = false; return e; }
        /// <summary>The exit's climb is moving his view (the body is hidden meanwhile).</summary>
        private bool ExitMovesView => _stage == ExitStage.Climbing || _pedestrian;

        // ---- binding ----

        private void BindExit(string seatId)
        {
            string id = _driver.AircraftId ?? "";
            _exit = ExitTable.For(id, seatId);
            ResetExitState();
            // A door handle is a lever pulled AFT and up 4 cm (E; HarnessRelease's lift-lever gesture); a generic canopy
            // release is pulled aft toward him.
            _doorHandle = _exit.Control == ExitControl.Door ? new HarnessRelease("door handle", HarnessReleaseKind.LiftLever, new Vec3(0, 0.5, -0.87)) : null;
            bool generic = _exit.Control == ExitControl.Canopy && _latchKind == CanopyLatchKind.None;
            _canopyRelease = generic ? new HarnessRelease("canopy release", HarnessReleaseKind.LiftLever, new Vec3(0, 0, -1)) : null;
            _doorGrip = null;
            string gripName = null, doorNode = null;
            foreach (string n in _exit.HandleNodes)
            {
                if (n.StartsWith("Grip_")) { gripName ??= n; if (_doorGrip == null) _doorGrip = Node(n); }
                else if (doorNode == null && Node(n) != null) doorNode = n;
            }
            if (_doorGrip == null && gripName != null && (_doorHandle != null || _canopyRelease != null)) Contract(gripName);
            if (_visual != null) _visual.Cockpit.DoorOpen01 = 0f;
            Debug.Log($"[VrEgress] {id} seat '{seatId}': exit {_exit.Control} on the {_exit.Side}{(_exit.Guessed ? " (a guess for this type: docs/vr-cockpit.md exit table)" : "")}; " +
                      (_exit.Control == ExitControl.Door ? $"handle {(_doorGrip != null ? "on " + gripName : "proxy")}, door {(doorNode != null ? doorNode + " (rig door_open)" : "not modelled")}; " :
                       generic ? $"canopy release {(_doorGrip != null ? "on " + gripName : "proxy")}; " : "") +
                      $"step {_exit.StepAboveGroundM:F2} m; {(_exit.WearsParachute ? "wears a parachute" : "no parachute")}");
        }

        /// <summary>A fresh cockpit (a new binding, or the flight restarted under the same one).</summary>
        private void ResetExitState()
        {
            _auto.Reset();
            _stage = ExitStage.None;
            _waitLogged = null;
            _autoCanopy = _autoSqueeze = false;
            _cockpitWater ??= new LineageCockpitWater(LivePilotWater.Instance);
            _cockpitWater.Reset();
            _doorHandle?.Reset();
            _canopyRelease?.Reset();
            if (_visual != null) _visual.Cockpit.DoorOpen01 = 0f;
        }

        private Vector3 ExitHandlePoint() => _doorGrip != null ? Local(_doorGrip.position) : _eye + U(_exit.HandleProxy);

        private void UpdateExitPoints()
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            bool door = _doorHandle != null && !_doorHandle.Released && !_auto.Started;
            _grab.Set(Grabbable.DoorHandle, door ? C(ExitHandlePoint()) : Vec3.Zero, door);
            bool rel = _canopyRelease != null && !_canopyRelease.Released && cockpit != null && !cockpit.CanopyGone && !_auto.Started;
            _grab.Set(Grabbable.CanopyRelease, rel ? C(ExitHandlePoint()) : Vec3.Zero, rel);
        }

        private void GrabExit(Grabbable g, HandPose h)
        {
            if (g == Grabbable.DoorHandle) _doorHandle?.Grab(h.Position, h.Rotation);
            else if (g == Grabbable.CanopyRelease) _canopyRelease?.Grab(h.Position, h.Rotation);
        }

        private void FollowExitHandle(HarnessRelease r, Grabbable slot)
        {
            if (r == null) return;
            if (_grab.IsHeldBy(slot, out HandSide hs))
            {
                if (r.Follow(_hands[(int)hs].Position, _hands[(int)hs].Rotation))
                {
                    XrHandInput.Tick(hs == HandSide.Left, 0.8f, 0.03f);
                    Debug.Log($"[VrEgress] {r.Name} worked");
                }
            }
            else if (r.Held) r.Release();
        }

        // ---- every frame in the cockpit ----

        private static double WheelGround(double x, double z, double y) => WorldTerrain.WheelGroundHeightAt(z, x, y);

        private bool AirframeOnGround(Aircraft ac)
        {
            if (LandingGear.AnyMainWheelOnGround(ac.Config, ac.State)) return true;
            foreach (FlyingGame.Core.ContactPoint p in ac.ContactPoints) if (p.Touching) return true;
            return false;
        }

        private bool ExitWorked(CockpitRigState cockpit)
        {
            switch (_exit.Control)
            {
                case ExitControl.BeltOnly: return true;
                case ExitControl.Door: return _doorHandle != null && _doorHandle.Released;
                case ExitControl.Canopy:
                    if (_canopyRelease != null) return _canopyRelease.Released || cockpit.CanopyGone;
                    return AutoExit.CanopyWorked(!cockpit.CanopyLocked, cockpit.HandJettison?.Fired ?? false, cockpit.CanopyGone);
                default: return false;
            }
        }

        private void DriveExit()
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            if (cockpit == null || _exit == null || _jumper || _pedestrian) return;
            // Back in the seat after an exit that finished (RESTART / reset flight keeps the same binding): start fresh.
            if (_auto.Started && _stage == ExitStage.None) { ResetExitState(); cockpit.Harness?.Reset(); }
            float dt = Time.deltaTime;
            FollowExitHandle(_doorHandle, Grabbable.DoorHandle);
            FollowExitHandle(_canopyRelease, Grabbable.CanopyRelease);

            Vector3 eyeW = World(_eye);
            _cockpitWater.Step(dt, eyeW.x, eyeW.z, eyeW.y - CockpitFloorBelowEyeM, eyeW.y + CockpitTopAboveEyeM);
            double? surf = LivePilotWater.Instance.SurfaceUpAt(eyeW.x, eyeW.z, eyeW.y);
            Aircraft ac = _driver.Sim.Aircraft;
            EgressSituation sit = AutoExit.Classify(AirframeOnGround(ac), eyeW.y, surf, _cockpitWater);
            bool belt = cockpit.Harness != null && cockpit.Harness.Latched;
            bool worked = ExitWorked(cockpit);
            bool held = false;
            double push = 0.0;
            if (sit == EgressSituation.Submerged && surf.HasValue && _exit.Control != ExitControl.BeltOnly)
            {
                push = DoorWaterLoad.PushNeededN(_exit.Opening, eyeW.y - _exit.Opening.CentreBelowEyeM, surf.Value,
                                                 _cockpitWater.InteriorWaterUp, _cockpitWater.WaterDensity, _cockpitWater.TopUp);
                held = push > DoorWaterLoad.PushAvailableN;
            }
            bool was = _auto.Started;
            ExitPath p = _auto.Step(belt, worked, sit, _driver.WorldVelocityUnity.magnitude, held);
            if (p == ExitPath.None)
            {
                string why = _auto.Waiting;
                if (why != _waitLogged && why != "belt" && !(why == "exit control" && _waitLogged == null))
                    Debug.Log($"[VrEgress] exit waiting: {why}" + (why == "water pressure" ? $" — {push:F0} N needed at the handle, {DoorWaterLoad.PushAvailableN:F0} N available; the cockpit is {_cockpitWater.FloodFraction * 100:F0} % full" : ""));
                _waitLogged = why;
                return;
            }
            if (!was) StartExit(p, sit, cockpit);
            StepExit(dt, cockpit);
        }

        private void StartExit(ExitPath p, EgressSituation sit, CockpitRigState cockpit)
        {
            _exitT = 0f;
            _stage = ExitStage.Opening;
            _grab.ReleaseAll();
            Debug.Log($"[VrEgress] EXIT {p} ({sit}): belt open, {_exit.Control} worked — out to the {_exit.Side}");
            // The jettison alone counts as the release (Ty): the exit opens the latch itself.
            if (cockpit.HandLatch is PittsCanopyLatch pl && pl.Locked) { pl.Grab(_pittsSeatLever); pl.Follow(_pittsSeatLever, -1.0); pl.Release(_pittsSeatLever); }
            _autoSqueeze = cockpit.HandLatch is ExtraCanopyLatch && cockpit.CanopyLocked;
            // A canopy with no hands-on model: it goes in flight, it opens on the ground.
            if (_canopyRelease != null && !cockpit.CanopyGone)
            {
                if (p == ExitPath.Bailout) _egress?.JettisonCanopyByHand(); else cockpit.CommandCanopy(1f);
            }
        }

        /// <summary>The way out is open: canopy past ClearToLeave (or gone), door open, or no canopy at all.</summary>
        private bool ExitClear(CockpitRigState cockpit, float dt)
        {
            switch (_exit.Control)
            {
                case ExitControl.Door:
                    cockpit.DoorOpen01 = Mathf.MoveTowards(cockpit.DoorOpen01, 1f, dt / DoorOpenSec);
                    return cockpit.DoorOpen01 >= 0.6f;
                case ExitControl.Canopy:
                    if (cockpit.CanopyGone || _openCockpit) return true;
                    CanopyDynamics c = cockpit.HandCanopy;
                    if (c != null)
                    {
                        if (c.ClearToLeave) { _autoCanopy = false; _autoSqueeze = false; return true; }
                        _autoCanopy = true;   // DriveBailout pushes it open on the next frame
                        return false;
                    }
                    return cockpit.CanopyOpen01 >= 0.7f;
                default: return true;
            }
        }

        /// <summary>The exit's own hand on a canopy that will not open by itself: it slides it aft (Pitts) at 0.25 m/s, then
        /// swings it up at 60°/s, at the free edge — the same servo a real hand drives (CanopyDynamics, capped at its 200 N).</summary>
        private Vector3 AutoCanopyHand(CanopyDynamics c, float dt, bool first)
        {
            if (first) { _autoBeta = c.AngleDeg; _autoZ = RailPoint().z; }
            if (!c.SlidComplete) _autoZ -= AutoSlideMs * dt;
            else _autoBeta = System.Math.Min(CanopyDynamics.ClearToLeaveDeg + 15.0, _autoBeta + AutoCanopyDps * dt);
            Vector3 h = Hinge();
            float w = (float)c.Spec.FreeEdgeM, b = (float)(_autoBeta * Mathf.Deg2Rad);
            return new Vector3(h.x - w * Mathf.Cos(b), h.y + w * Mathf.Sin(b), (float)_autoZ);
        }

        private void StepExit(float dt, CockpitRigState cockpit)
        {
            _exitT += dt;
            switch (_stage)
            {
                case ExitStage.Opening:
                    if (!ExitClear(cockpit, dt)) return;
                    _exitT = 0f;
                    if (_auto.Path == ExitPath.Bailout) { _stage = ExitStage.Rising; _riseFrom = _rise; }
                    else BeginClimb();
                    return;
                case ExitStage.Rising:
                {
                    float tau = Mathf.Clamp01(_exitT / AutoRiseSec);
                    float s = tau * tau * tau * (10f - 15f * tau + 6f * tau * tau);
                    _rise = Mathf.Lerp(_riseFrom, (float)ClimbOut.OverRiseM, s);
                    if (tau >= 1f) { _stage = ExitStage.None; Leave(_exit.Side, pulled: false); }
                    return;
                }
                case ExitStage.Climbing:
                    _pedEye = U(_path.Step(dt));
                    if (_path.Done) FinishClimb();
                    return;
            }
        }

        private void BeginClimb()
        {
            _pedYaw = _air.eulerAngles.y;
            System.Func<Vec3, Vec3> toWorld = l => C(World(_eye + Vector3.up * _rise + U(l)));
            // Where he steps off: water there (a shore, a floatplane, a cockpit floor still dry) means into the water.
            Vec3 off = toWorld(new Vec3(_exit.OutSign * _exit.GroundOutM, 0.0, -_exit.GroundAftM));
            double? offWater = LivePilotWater.Instance.SurfaceUpAt(off.X, off.Z, _air.position.y);
            if (_auto.Path == ExitPath.ClimbOutGround && offWater.HasValue && offWater.Value > WheelGround(off.X, off.Z, offWater.Value) + 0.3)
            {
                Debug.Log("[VrEgress] water where he would step down: into the water instead");
                _auto.Redirect(ExitPath.IntoWater);
            }
            bool water = _auto.Path == ExitPath.IntoWater || _auto.Path == ExitPath.SwimUp;
            if (_auto.Path == ExitPath.SwimUp)
                // Out through the opening, at eye height, 0.8 m outboard (E): then he swims.
                _path = new ClimbPath(new[] { toWorld(Vec3.Zero), toWorld(new Vec3(_exit.OutSign * 0.8, 0.1, 0.0)) });
            else
                _path = ClimbPath.For(_exit, toWorld, (x, z) =>
                {
                    double g = WheelGround(x, z, _air.position.y);
                    double? s = LivePilotWater.Instance.SurfaceUpAt(x, z, _air.position.y);
                    return water && s.HasValue ? System.Math.Max(g, s.Value) : g;
                }, intoWater: water);
            _stage = ExitStage.Climbing;
            _pedEye = U(_path.EyeAt(0.0));
            _grab.ReleaseAll();
            _rise = 0f;
            Debug.Log($"[VrEgress] climbing out to the {_exit.Side}: {_path.Points.Count - 1} legs, {_path.TotalSec:F1} s, " +
                      $"ends {(water ? "in the water" : $"standing {_path.End.Y - ClimbPath.StandingEyeM:F2} m up")} (view levelled once, as he stands)");
        }

        private void FinishClimb()
        {
            bool water = _auto.Path == ExitPath.IntoWater || _auto.Path == ExitPath.SwimUp;
            Vector3 eye = _pedEye;
            _egress?.LeaveOnFoot(eye - Vector3.up * EyeAboveCentreM, _pedYaw, water);
            _stage = water ? ExitStage.InWater : ExitStage.OnFoot;
            _pedestrian = true;
            BuildKeepOut();
            if (water)
            {
                bool swimUp = _auto.Path == ExitPath.SwimUp;
                StartSwim(eye, swimUp ? Vector3.zero : Vector3.down * 1.0f, swimUp ? SwimPhase.SwimUp : SwimPhase.Plunge,
                          PilotSwim.KitFor(_exit.WearsParachute, opened: false), entering: !swimUp);
            }
            else StartWalk(eye);
        }

        private void StartWalk(Vector3 eye)
        {
            _swim = null;
            _walker = new GroundWalker(C(eye) - new Vec3(0, ClimbPath.StandingEyeM, 0), _pedYaw);
            _pedEye = U(_walker.Eye);
            Debug.Log($"[VrEgress] on foot: walk {GroundWalker.WalkMs:F1} m/s, run {GroundWalker.RunMs:F1} (left trigger / stick click), turn {XrHeadset.TurnOnFoot}");
        }

        private void StartSwim(Vector3 eye, Vector3 vel, SwimPhase phase, PilotKit[] kit, bool entering)
        {
            _walker = null;
            _swim = new PilotSwim(LivePilotWater.Instance, C(eye), C(vel), phase, kit);
            _pedEye = eye;
            if (entering) { _enteredWater = true; _entrySpeed = vel.magnitude; }
            double eq = _swim.EquilibriumEyeAboveM(PilotSwim.TreadN);
            Debug.Log($"[VrEgress] in the water ({phase}, {_swim.MassKg:F1} kg with {kit.Length} items of kit): treading {PilotSwim.TreadN:F0} N holds the eye {eq:F2} m clear " +
                      $"(the water {(-eq < SwimmerBody.NotchM ? "at the top of the chest" : -eq < SwimmerBody.ChinM ? "at the neck" : "at the chin")}); without treading {_swim.EquilibriumEyeAboveM(0):F2}");
        }

        /// <summary>The aeroplane as boxes he cannot walk into: its lifting surfaces (Core EgressClearance, from the
        /// config's strips), a fuselage box and the propeller disc (E: 0.9 m wide, from 0.8 m above to 0.6 m below the CG,
        /// from the tail's strips to 1.2 m ahead of the wing's leading edge; the disc the config's diameter, 0.3 m deep).</summary>
        private void BuildKeepOut()
        {
            _keepOut.Clear();
            if (_air == null || _driver?.Sim?.Aircraft == null) return;
            var cfg = _driver.Sim.Aircraft.Config;
            Vec3 cg = cfg.Mass.CgVec();
            Vector3 Unity(Vec3 body) => new((float)(body.Y - cg.Y), (float)-(body.Z - cg.Z), (float)(body.X - cg.X));
            void Add(string name, Vec3 min, Vec3 max)
            {
                Vec3 c = (min + max) * 0.5, h = (max - min) * 0.5;
                _keepOut.Add(new KeepOutBox(name, C(World(Unity(c))), C(_air.right), C(_air.up), C(_air.forward), new Vec3(h.Y, h.Z, h.X)));
            }
            EgressClearance clear = _clear ?? EgressClearance.From(cfg);
            double tail = cg.X, nose = cg.X;
            foreach (EgressClearance.Box b in clear.Boxes)
            {
                Add(b.Surface, b.Min, b.Max);
                tail = System.Math.Min(tail, b.Min.X);
                nose = System.Math.Max(nose, b.Max.X);
            }
            nose += 1.2;
            Add("fuselage", new Vec3(tail, cg.Y - 0.45, cg.Z - 0.8), new Vec3(nose, cg.Y + 0.45, cg.Z + 0.6));
            if (cfg.Propulsion != null && cfg.Propulsion.PropDiameterM > 0.1)
            {
                double r = 0.5 * cfg.Propulsion.PropDiameterM;
                if (cfg.Engines != null && cfg.Engines.Count > 0)
                    foreach (var e in cfg.Engines) { Vec3 p = cg + e.PosVec(); Add("propeller", new Vec3(p.X - 0.15, p.Y - r, p.Z - r), new Vec3(p.X + 0.15, p.Y + r, p.Z + r)); }
                else Add("propeller", new Vec3(nose - 0.15, cg.Y - r, cg.Z - r), new Vec3(nose + 0.15, cg.Y + r, cg.Z + r));
            }
        }

        // ---- out of the aeroplane, on foot or in the water ----

        /// <summary>A parachute landing ends here (TickJumper): in the water, the plunge; on land, on foot.</summary>
        private void JumperLanded()
        {
            Vector3 body = _egress.PilotTransform.position;
            _pedYaw = _jRot.eulerAngles.y;
            EndJumper();
            _pedestrian = true;
            BuildKeepOut();
            if (_egress.PilotLandedOnWater)
            {
                double? s = LivePilotWater.Instance.SurfaceUpAt(body.x, body.z, body.y);
                float surf = s.HasValue ? (float)s.Value : body.y;
                // His soles at the surface the instant he reached it; the plunge from the canopy's sink and drift.
                StartSwim(new Vector3(body.x, surf + (float)ClimbPath.StandingEyeM, body.z), _egress.PilotLandingVelocity, SwimPhase.Plunge,
                          PilotSwim.KitFor(parachute: true, opened: true), entering: true);
                _stage = ExitStage.InWater;
            }
            else { StartWalk(body + Vector3.up * EyeAboveCentreM); _stage = ExitStage.OnFoot; }
        }

        private void TickPedestrian()
        {
            if (_egress == null || !_egress.PilotOut || !XrHeadset.Active) { EndPedestrian(); return; }
            float dt = Time.deltaTime;
            QuestControllerFrame f = XrControllers.Read(out bool any);
            bool menu = XrHeadset.MenuOpen;
            double sx = any && !menu ? f.LeftX : 0.0, sy = any && !menu ? f.LeftY : 0.0, tx = any && !menu ? f.RightX : 0.0;
            bool run = any && !menu && (f.LeftTrigger > 0.5 || f.LeftStickClick);
            double headYaw = HeadWorld(out _, out Quaternion hq) ? hq.eulerAngles.y : _pedYaw;

            if (_swim != null)
            {
                _swim.CanopyLoadN = _egress.ChuteLoadOnPilotN;
                _swim.Step(dt, sx, sy, headYaw);
                _pedYaw = (float)GroundWalker.Turn(ref _snapArmed, _pedYaw, tx, XrHeadset.TurnOnFoot, dt);
                _pedEye = U(_swim.Eye);
                if (_swim.CanStand)
                {
                    Debug.Log("[VrEgress] his feet find the bottom: wading out");
                    StartWalk(_pedEye);
                }
            }
            else if (_walker != null)
            {
                _walker.YawDeg = _pedYaw;
                _walker.Step(dt, sx, sy, run, headYaw, tx, XrHeadset.TurnOnFoot, (x, z) => WheelGround(x, z, _walker.Feet.Y + 1.0), _keepOut);
                _pedYaw = (float)_walker.YawDeg;
                _pedEye = U(_walker.Eye);
                // Walked into water deeper than his chest: he swims.
                double? s = LivePilotWater.Instance.SurfaceUpAt(_walker.Feet.X, _walker.Feet.Z, _walker.Feet.Y);
                if (s.HasValue && _walker.Feet.Y + ClimbPath.StandingEyeM - s.Value < 0.36)
                    StartSwim(_pedEye, U(_walker.Velocity), SwimPhase.Tread, PilotSwim.KitFor(_exit != null && _exit.WearsParachute, opened: false), entering: true);
            }
            bool inWater = _swim != null;
            if (inWater != _wasInWater) { _wasInWater = inWater; PilotWaterAudioGate.PilotInWater = inWater; }
            _egress.MoveOnFoot(_pedEye - Vector3.up * EyeAboveCentreM, inWater);
        }

        private void EndPedestrian()
        {
            _pedestrian = false;
            _swim = null; _walker = null;
            _stage = ExitStage.None;
            if (_wasInWater) { _wasInWater = false; PilotWaterAudioGate.PilotInWater = false; }
        }

        /// <summary>The exit's grab points' positions, for the underwater cue (PilotUnderwater): belt releases, the latch,
        /// the jettison / release, the door handle. Cockpit-world positions; only those still to be worked.</summary>
        public int ExitCuePoints(Vector3[] into)
        {
            int n = 0;
            if (!Active || into == null) return 0;
            foreach (Grabbable g in CueSlots)
            {
                CockpitGrab.Point p = _grab.Points[(int)g];
                if (!p.Enabled || n >= into.Length) continue;
                into[n++] = World(U(p.Position));
            }
            return n;
        }
    }
}

using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE PILOT'S HANDS ON THE AEROPLANE'S OWN CONTROLS, and his visible body (docs/vr-cockpit.md). Lives on the main
    /// camera beside <see cref="QuestRig"/>; does nothing unless a headset is on, the view is the cockpit and the pilot
    /// has chosen FLY BY HAND (<see cref="XrHeadset.FlyByHand"/>, off by default). Off, the body is never built (or is
    /// switched off if it was), nothing is grabbed, and the controllers fly through TouchFlightControls alone.
    ///
    /// Each frame, in the COCKPIT frame (the aeroplane's position and rotation, no scale: +x right, +y up, +z nose):
    ///  1. the hands — the controllers' grip poses through QuestRig's recentre (<see cref="QuestRig.TrackingToSeat"/>),
    ///     put at the seat's authored eye: a <see cref="HandPose"/> each, the input-agnostic hand Phase 3 also fills;
    ///  2. the grab points — the stick's grip, the throttle knob, the canopy latch — from Codex's nodes
    ///     (CONTRACT.md) or, where the model has none, the proxies in <see cref="CockpitProxy"/>, logged once;
    ///  3. <see cref="CockpitGrab"/> decides who holds what (6 cm, grip closing, menu gate) and the controllers tick;
    ///  4. the held control follows the hand: <see cref="StickGimbal"/>, <see cref="LeverArc"/>, the latch;
    ///  5. <see cref="TouchFlightControls"/> takes the result into the SAME control state the thumbsticks feed
    ///     (<see cref="AileronInput"/>, <see cref="ElevatorInput"/>, <see cref="Throttle01"/>) — no second path into
    ///     the flight model;
    ///  6. LateUpdate solves the two-bone IK for both arms and both legs and draws the body (<see cref="VrPilotBody"/>).
    ///
    /// LET GO OF THE STICK and it is FREE: TouchFlightControls then publishes the aileron and elevator as released, and
    /// on a reversible type (all the aerobatic ones) the sim floats each surface to where its hinge moment balances
    /// (Aircraft.FreeFloat: Chα·α_flow + Chδ·(δ − δ_tab) = 0, settling with the linkage's time constant). The stick in
    /// the cockpit is drawn from those deflections, so it trails exactly as the elevator does, and the next grab takes
    /// it from there without a jump.
    ///
    /// Nothing here moves the camera: grabbing can never move the pilot's head. The one exception is the pilot's OWN
    /// doing in the bail-out (VrCockpitHands.Bailout.cs): climbing out on the sill raises his seat by what his hand
    /// pushed, and once out he is the jumper, and the head rig is placed on him.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public sealed partial class VrCockpitHands : MonoBehaviour
    {
        public static VrCockpitHands Current { get; private set; }

        /// <summary>Headset on, cockpit view, fly by hand on, an aeroplane with a sim, the pilot still in it.</summary>
        public bool Active { get; private set; }
        public bool StickHeld => Active && _grab.IsHeld(Grabbable.Stick);
        /// <summary>Set by TouchFlightControls each frame before <see cref="Tick"/>: a thumbstick (Touch or a pad) is out of
        /// its dead zone, so it flies pitch and roll even with a hand on the stick (HandPadArbiter).</summary>
        public bool PadOwnsStick { get; set; }
        public bool ThrottleHeld => Active && _hasThrottle && _grab.IsHeld(Grabbable.Throttle);
        /// <summary>The held stick, as the sim's own inputs (aileron + right, elevator + PUSH), already through the
        /// inverse of the type's dead zone and expo so the surface is LINEAR in stick angle, as a cable is.</summary>
        public float AileronInput { get; private set; }
        public float ElevatorInput { get; private set; }
        /// <summary>The held throttle lever, 0 idle .. 1 full.</summary>
        public float Throttle01 { get; private set; }
        public Grabbable HeldBy(HandSide side) => _grab.Held(side);

        private readonly CockpitGrab _grab = new();
        private readonly VrPilotBody _body = new();
        private readonly HandPose[] _hands = { HandPose.None, HandPose.None };
        private bool _maskRight;

        private ChaseCamera _chase;
        private QuestRig _rig;
        private Transform _air;
        private FlightSimDriver _driver;
        private AirframeVisual _visual;
        private PilotEgress _egress;
        private int _tickFrame = -1;

        // ---- the cockpit, bound per aeroplane and seat ----
        private string _boundKey;
        private Vector3 _eye;
        private CockpitProxy _proxy;
        private StickGimbal _stick;
        private VrControlBinding.Throttle _lever;
        private VrControlBinding.Stick _stickRig;
        /// <summary>The cockpit's structure round this seat, for the elbows (ArmPose; Ty 2026-11-04: arms through the Great
        /// Lakes' switch box). Built at bind.</summary>
        private readonly VrArmClearance _clearance = new();
        /// <summary>Each elbow's swing from its natural pole, degrees, eased so it never hops.</summary>
        private readonly float[] _swivel = new float[2];
        public const float SwivelRateDegPerSec = 240f;
        private bool _hasThrottle;
        private Transform _latchNode, _latchAftNode, _latchFwdNode, _footNodeL, _footNodeR;
        private Vector3 _footOffL, _footOffR;
        private Vector3 _latchLocked;
        private CanopyLatchKind _latchKind;
        private static readonly HashSet<string> Warned = new();

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
            _grab.ReleaseAll();
            _body.SetVisible(false);
            if (_canopyProps != null) _canopyProps.SetActive(false);
        }

        private void OnDestroy() { _body.Destroy(); DestroyBailout(); }

        private void LateUpdate()
        {
            Tick();
            PoseBody();
            PlaceCanopyProps();
            DrawGliderLevers();
            DrawPanel();
        }

        // ---- 1-5: once a frame, from whoever asks first (TouchFlightControls in Update, else LateUpdate) ----

        public void Tick()
        {
            if (_tickFrame == Time.frameCount) return;
            _tickFrame = Time.frameCount;
            bool was = Active;
            Active = Resolve();
            if (!Active)
            {
                if (was) { _grab.ReleaseAll(); (_visual?.Cockpit.HandLatch as PittsCanopyLatch)?.Release(); ReleaseHands(); }
                _maskRight = false;
                TickJumper();
                return;
            }
            Bind();
            ReadHands();
            UpdatePoints();
            bool blocked = XrHeadset.MenuOpen;
            double gdt = HandDt;
            Events(HandSide.Left, _grab.Step(HandSide.Left, _hands[0], blocked, gdt));
            Events(HandSide.Right, _grab.Step(HandSide.Right, _hands[1], blocked, gdt));
            OwnPinches();
            DiagLog();
            Drive();
            DriveGlider();
            DrivePanel();
            DriveBailout();
        }

        /// <summary>In the cockpit the LEFT grip is the left hand's (it no longer grabs the throttle from anywhere:
        /// the lever is where it is), and the RIGHT grip is the right hand's whenever it is on or holding a control,
        /// so closing on the stick never toggles the smoke. A right grip closed in free air is still the smoke.</summary>
        public void MaskGrips(ref QuestControllerFrame f)
        {
            if (!Active) { _maskRight = false; return; }
            f.LeftGrip = 0.0;
            bool onSomething = _grab.Held(HandSide.Right) != Grabbable.None || _grab.Hover(HandSide.Right) != Grabbable.None;
            // Once masked, masked until the grip opens: a fist carried off the stick must not read as a new squeeze.
            _maskRight = onSomething || (_maskRight && f.RightGrip > QuestControlMapper.GripOff);
            if (_maskRight) f.RightGrip = 0.0;
        }

        private bool Resolve()
        {
            if (!QuestCockpit.HandsInCockpit(XrHeadset.Active, ChaseCamera.InCockpit, XrHeadset.FlyByHand)) return false;
            if (_chase == null) _chase = GetComponent<ChaseCamera>();
            if (_rig == null) _rig = GetComponent<QuestRig>();
            if (_chase == null || _rig == null || _chase.Target == null || _chase.Cockpit == null) return false;
            if (_air != _chase.Target)
            {
                _air = _chase.Target;
                _driver = _air.GetComponent<FlightSimDriver>();
                _visual = _air.GetComponent<AirframeVisual>();
                _egress = _air.GetComponent<PilotEgress>();
                _boundKey = null;
            }
            if (_driver == null || _driver.Sim?.Aircraft == null) return false;
            return _egress == null || !_egress.PilotOut;
        }

        // ---- frames ----

        /// <summary>World → cockpit frame (the aeroplane's position and rotation; its scale deliberately ignored, so
        /// metres stay metres — the head rig places the camera the same way).</summary>
        private Vector3 Local(Vector3 world) => Quaternion.Inverse(_air.rotation) * (world - _air.position);
        private Vector3 World(Vector3 local) => _air.position + _air.rotation * local;
        private static Vec3 C(Vector3 v) => new(v.x, v.y, v.z);
        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        // ---- 2: where the controls are ----

        private Transform Node(string name) => _visual != null && !string.IsNullOrEmpty(name) ? _visual.FindInModel(name) : null;

        /// <summary>A CONTRACT node, or null and one log line saying it is missing (CONTRACT.md).</summary>
        private Transform Contract(string name)
        {
            Transform t = Node(name);
            if (t == null && Warned.Add(_driver.AircraftId + "/" + name))
                Debug.Log($"[VrCockpit] missing node {name} ({_driver.AircraftId}) — using the proxy position");
            return t;
        }

        private RigNode RigNodeNamed(string name)
        {
            if (_visual?.Rig == null) return null;
            foreach (AirframeRig.Bound b in _visual.Rig.Nodes) if (b.Node.Node == name) return b.Node;
            return null;
        }

        private static (double Min, double Max) MapEnds(RigNode n) => (n.MapPoint(0).Value, n.MapPoint(n.MapPoints - 1).Value);

        /// <summary>Changes when the aeroplane's model is rebuilt (a new rig instance, a new eye node), so the grab
        /// points are looked up again on the new nodes.</summary>
        private int ModelKey()
        {
            if (_visual == null) return 0;
            Transform eye = _visual.CockpitEye();
            return (_visual.Rig != null ? _visual.Rig.GetHashCode() : 0) ^ (eye != null ? eye.GetInstanceID() : 0);
        }

        private void Bind()
        {
            CockpitView cv = _chase.Cockpit;
            string key = $"{_driver.AircraftId}|{cv.SeatIndex}|{ModelKey()}";
            if (key == _boundKey) return;
            _boundKey = key;
            _grab.ReleaseAll();

            Aircraft ac = _driver.Sim.Aircraft;
            string id = _driver.AircraftId;
            _proxy = CockpitProxy.For(id);
            _eye = Local(_air.TransformPoint(cv.EyeLocal));
            string seatId = SeatChoice.IdAt(cv.Seats, cv.SeatIndex) ?? "";
            // CONTRACT.md names the two tandem seats Rear / Front; the existing rigs use _R / _F.
            string full = seatId == "rear" ? "Rear" : seatId == "front" ? "Front" : null;
            string brief = seatId == "rear" ? "R" : seatId == "front" ? "F" : null;

            // The stick: the rig's own hinges and grip (VrControlBinding: why the old upright rod missed every raked
            // stick in the fleet), else the proxy.
            _stickRig = VrControlBinding.BindStick(_visual?.Rig, Node, _air, seatId);
            Vector3 pivot;
            if (_stickRig != null) { _stick = _stickRig.Gimbal; pivot = U(_stick.Pivot); }
            else
            {
                pivot = _eye + U(_proxy.StickPivot);
                _stick = new StickGimbal(C(pivot), _proxy.StickLengthM, _proxy.StickRollMaxDeg, _proxy.StickAftMaxDeg, _proxy.StickFwdMaxDeg);
            }
            double len = _stick.LengthM, roll = _stick.RollMaxDeg, aft = _stick.AftMaxDeg, fwd = _stick.FwdMaxDeg;
            Vector3 grip0 = U(_stick.GripAt(0, 0));

            // The throttle: the rig's lever through the knob it draws at idle and full, else the proxy's arc.
            _hasThrottle = ac.Config.Propulsion != null;
            _lever = _hasThrottle ? VrControlBinding.BindThrottle(_visual?.Rig, Node, _air, seatId) : null;
            _lever ??= VrControlBinding.ProxyThrottle(C(_eye + U(_proxy.ThrottlePivot)), _proxy.ThrottleArmM, _proxy.ThrottleIdleDeg, _proxy.ThrottleSweepDeg);
            double sweep = _lever.Lever.SweepDeg, arm = _lever.Lever.ArmM;

            // The feet: Foot_* empties at the ball of the foot, else the pedal (its hinge is on the floor: the ball
            // is ~0.12 m up its face, fixed in the pedal's own frame so it swings with it), else the proxy.
            BindFoot(full, brief, "L", out _footNodeL, out _footOffL);
            BindFoot(full, brief, "R", out _footNodeR, out _footOffR);

            // The latch.
            _latchKind = _proxy.Latch;
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            _latchNode = _latchAftNode = _latchFwdNode = null;
            if (_latchKind != CanopyLatchKind.None && cockpit != null)
            {
                if (_latchKind == CanopyLatchKind.SlideBall)
                    _latchNode = Contract("Grip_Canopy_Latch") ?? Contract("Canopy_Latch_Knob");
                else
                {
                    // Extra v2 (CONTRACT "Extra latch v2"): a fixed rear and a sliding front handle on the canopy frame,
                    // one pair per cockpit (the front cockpit's names end _Front); Grip_Canopy_Latch stays between them.
                    string sfx = seatId == "front" ? "_Front" : "";
                    _latchAftNode = Contract("Grip_Canopy_Latch_Aft" + sfx);
                    _latchFwdNode = Contract("Grip_Canopy_Latch_Fwd" + sfx);
                    // A model without the v2 handles is the superseded L/R-paddle one: its Grip_Canopy_Latch marks the old
                    // paddles, not the pair, so the pair is the proxy too (it must stay between the handles it grabs).
                    _latchNode = _latchAftNode != null || _latchFwdNode != null ? Contract("Grip_Canopy_Latch" + sfx) : null;
                }
                // The Pitts' ball is on the REAR panel whichever seat you are in.
                Vector3 anchor = _eye;
                if (_latchKind == CanopyLatchKind.SlideBall)
                    foreach (StationConfig s in cv.Seats)
                        if (s.Id == "rear") anchor = Local(_air.TransformPoint(CoordinateMap.ToUnity(s.EyeVec())));
                _latchLocked = anchor + U(_proxy.LatchLocked);
                bool fits = _latchKind == CanopyLatchKind.SlideBall ? cockpit.HandLatch is PittsCanopyLatch : cockpit.HandLatch is ExtraCanopyLatch;
                if (!fits) cockpit.HandLatch = _latchKind == CanopyLatchKind.SlideBall ? new PittsCanopyLatch() : new ExtraCanopyLatch();
            }
            else if (cockpit != null) cockpit.HandLatch = null;

            Debug.Log($"[VrCockpit] {id} seat '{seatId}': stick {(_stickRig != null ? $"on the rig ({_stickRig.Roll?.Name ?? "no roll node"} / {_stickRig.Pitch.Name}, grip {_stickRig.GripSource}, rig check {_stickRig.SenseResidualM * 1000:F1} mm)" : "proxy")} " +
                      $"(pivot {pivot.y - _eye.y:F2} m below the eye, {len:F2} m, grip {grip0.y - pivot.y:F2} up / {grip0.z - pivot.z:F2} fwd / {grip0.x - pivot.x:F2} right of it, roll ±{roll:F0}°, {aft:F0}° aft / {fwd:F0}° fwd); " +
                      $"throttle {(!_hasThrottle ? "none (no engine)" : _lever.OnRig ? $"on the rig's {_lever.Node.Name} (knob {_lever.GripSource})" : "proxy")} ({sweep:F0}°, arm {arm:F2} m); " +
                      $"feet {(_footNodeL != null ? "on the pedals" : "proxy")}; latch {_latchKind}{(_latchKind != CanopyLatchKind.None ? _latchNode != null ? " on its node" : " proxy" : "")}");
            BindGlider(brief);
            BindPanel(full);
            BindBailout(full);
            // The arms' keep-out: the whole airframe round this seat but the stick and the throttle the hands hold.
            var exclude = new System.Collections.Generic.List<Transform>();
            if (_stickRig?.Pitch?.T != null) exclude.Add(_stickRig.Pitch.T);
            if (_stickRig?.Roll?.T != null) exclude.Add(_stickRig.Roll.T);
            if (_lever?.Node?.T != null) exclude.Add(_lever.Node.T);
            _clearance.Build(_air, _eye, exclude, $"{id} {seatId}");
            _swivel[0] = _swivel[1] = 0f;
        }

        private void BindFoot(string full, string brief, string side, out Transform node, out Vector3 offset)
        {
            offset = Vector3.zero;
            node = full != null ? Node($"Foot_{side}_{full}") : null;
            if (node != null) return;
            node = full != null ? Contract($"Pedal_{side}_{full}") : null;
            if (node == null && brief != null) node = Node($"Pedal_{brief}_{side}");
            if (node != null) offset = node.InverseTransformPoint(node.position + _air.up * 0.12f);
        }

        // ---- 1: the hands ----

        /// <summary>A controller's grip pose, or a tracked hand's joints, per side (VrCockpitHands.Hands.cs).</summary>
        private void ReadHands() => ReadAllHands();

        // ---- 2: where each grab point is this frame ----

        private float _ail, _eleAft, _rud;

        private void UpdatePoints()
        {
            Aircraft ac = _driver.Sim.Aircraft;
            ControlsConfig c = ac.Config.Controls;
            var d = ac.CurrentDeflections;
            static float N(double v, double max) => max > 1e-6 ? Mathf.Clamp((float)(v / max), -1f, 1f) : 0f;
            // Where the AERO MODEL has the controls (the rig draws them from the same numbers).
            _ail = N(d.AileronRad, c.Aileron.MaxDeflRad);
            _eleAft = (float)RigInputs.FromDeflections(0, d.ElevatorRad, 0, 0, 1, c.Elevator.MaxDeflRad, 1, c.Elevator.LimitFor(-1.0)).Elevator;
            _rud = N(d.RudderRad, c.Rudder.MaxDeflRad);
            if (!_grab.IsHeld(Grabbable.Stick)) _stick.SetFromControls(_ail, _eleAft);
            (Vec3 sa, Vec3 sb) = VrControlBinding.StickCapsule(_stick);
            _grab.Set(Grabbable.Stick, sa, sb, true);
            (Vec3 ta, Vec3 tb) = VrControlBinding.ThrottleCapsule(_lever.Lever, _lever.TFor(ac.Throttle01));
            _grab.Set(Grabbable.Throttle, ta, tb, _hasThrottle);
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            bool latch = _latchKind != CanopyLatchKind.None && cockpit != null && cockpit.HandLatch != null && !cockpit.CanopyGone;
            _grab.Set(Grabbable.CanopyLatch, latch ? C(LatchPoint(cockpit)) : Vec3.Zero, latch);
            bool pair = latch && _latchKind == CanopyLatchKind.Squeeze;
            _grab.Set(Grabbable.CanopyLatchAft, pair ? C(LatchHandle(cockpit, fwd: false)) : Vec3.Zero, pair);
            _grab.Set(Grabbable.CanopyLatchFwd, pair ? C(LatchHandle(cockpit, fwd: true)) : Vec3.Zero, pair);
            UpdateGliderPoints();
            UpdatePanelPoints();
            UpdateBailoutPoints();
        }

        /// <summary>The latch's grab point: the model's node (it rides the rig), else the proxy, which is fixed to the
        /// CANOPY (both latches are on it) and so is carried by its slide and swing (<see cref="OnCanopy"/>). The Extra's
        /// pair grip sits midway between the handles, so it moves aft half as far as the front one.</summary>
        private Vector3 LatchPoint(CockpitRigState cockpit)
        {
            if (_latchNode != null) return Local(_latchNode.position);
            if (cockpit.HandLatch is PittsCanopyLatch p)
                return OnCanopy(_latchLocked - Vector3.right * (float)(PittsCanopyLatch.TravelM - p.PositionM));
            if (cockpit.HandLatch is ExtraCanopyLatch x)
                return OnCanopy(_latchLocked - Vector3.forward * (float)(0.5 * x.S * ExtraCanopyLatch.TravelM));
            return OnCanopy(_latchLocked);
        }

        /// <summary>The Extra's rear (fixed) or front (sliding) handle: its node, else 45 mm aft / ahead of the pair's
        /// proxy, the front one carried aft by the squeeze, all on the canopy.</summary>
        private Vector3 LatchHandle(CockpitRigState cockpit, bool fwd)
        {
            Transform n = fwd ? _latchFwdNode : _latchAftNode;
            if (n != null) return Local(n.position);
            float half = (float)(0.5 * ExtraCanopyLatch.HandleSpacingM);
            if (!fwd) return OnCanopy(_latchLocked - Vector3.forward * half);
            double s = cockpit.HandLatch is ExtraCanopyLatch x ? x.S : 0.0;
            return OnCanopy(_latchLocked + Vector3.forward * (half - (float)(s * ExtraCanopyLatch.TravelM)));
        }

        // ---- 3: events and haptics ----

        private void Events(HandSide side, GrabEvents ev)
        {
            if (ev == GrabEvents.None) return;
            bool left = side == HandSide.Left;
            Vec3 h = _hands[(int)side].Position;
            if ((ev & GrabEvents.HoverEnter) != 0) XrHandInput.Tick(left, 0.15f, 0.015f);
            if ((ev & GrabEvents.Grabbed) != 0)
            {
                XrHandInput.Tick(left, 0.45f, 0.035f);
                if (_hands[(int)side].Source == HandSource.TrackedHand) Debug.Log($"[VrHands] {(left ? "left" : "right")} hand took {_grab.Held(side)}");
                switch (_grab.Held(side))
                {
                    case Grabbable.Stick: _stick.Grab(h); break;
                    case Grabbable.Throttle: _lever.Lever.Grab(h, _lever.TFor(_driver.Sim.Aircraft.Throttle01)); break;
                    case Grabbable.CanopyLatch:
                        if (_visual.Cockpit.HandLatch is PittsCanopyLatch p) p.Grab(h.X);
                        break;
                    default:
                        if (!GrabGlider(side, _grab.Held(side), h) && !GrabPanel(side, _grab.Held(side), h))
                            GrabBailout(side, _grab.Held(side), _hands[(int)side]);
                        break;
                }
            }
            if ((ev & GrabEvents.Released) != 0 && !_grab.IsHeld(Grabbable.CanopyLatch))
                (_visual?.Cockpit.HandLatch as PittsCanopyLatch)?.Release();
        }

        // ---- 4: the held controls follow the hands ----

        private void Drive()
        {
            Aircraft ac = _driver.Sim.Aircraft;
            ControlsConfig c = ac.Config.Controls;
            if (_grab.IsHeldBy(Grabbable.Stick, out HandSide sh) && PadOwnsStick)
            {
                // The thumbstick is out of its dead zone: it flies, and the hand rides the stick where the pad puts it
                // (re-gripped every frame, so the hand takes over again from there without a jump).
                _stick.SetFromControls(_ail, _eleAft);
                _stick.Grab(_hands[(int)sh].Position);
            }
            else if (_grab.IsHeldBy(Grabbable.Stick, out sh))
            {
                _stick.Follow(_hands[(int)sh].Position);
                // Stick angle → surface, linear (a cable has no expo): the deflection the drawn stick stands for,
                // turned back into the input that produces it through this type's own dead zone and expo.
                AileronInput = (float)Aircraft.StickForDeflection(_stick.Aileron * c.Aileron.MaxDeflRad, c.Aileron);
                // The rig's elevator is normalised by the trailing-edge-UP stop on the aft side (RigInputs.FromDeflections).
                double aftRad = _stick.ElevatorAft * (_stick.ElevatorAft > 0 ? c.Elevator.LimitFor(-1.0) : c.Elevator.MaxDeflRad);
                ElevatorInput = (float)Aircraft.StickForDeflection(-aftRad, c.Elevator);
            }
            if (_hasThrottle && _grab.IsHeldBy(Grabbable.Throttle, out HandSide th))
                Throttle01 = (float)_lever.ThrottleFor(_lever.Lever.Follow(_hands[(int)th].Position));

            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            if (cockpit?.HandLatch == null) return;
            bool lh = _grab.IsHeldBy(Grabbable.CanopyLatch, out HandSide lside);
            bool click = false;
            if (cockpit.HandLatch is PittsCanopyLatch pitts && lh)
                click = pitts.Follow(_hands[(int)lside].Position.X);
            else if (cockpit.HandLatch is ExtraCanopyLatch extra)
            {
                // The pair in one hand, squeezed by its trigger; or a hand on each handle (or on the front one alone),
                // measured along the canopy frame's AFT axis (−z: the hinge runs fore-aft, so open or shut). Stepped
                // before the canopy (DriveBailout), against its state from the last frame.
                var hands = new ExtraLatchHands();
                if (lh) { hands.PairHeld = true; hands.PairSqueeze = _hands[(int)lside].Pinch; }
                if (_grab.IsHeldBy(Grabbable.CanopyLatchFwd, out HandSide fs)) { hands.FwdHeld = true; hands.FwdAftM = -_hands[(int)fs].Position.Z; if (!lh) lside = fs; }
                if (_grab.IsHeldBy(Grabbable.CanopyLatchAft, out HandSide asd)) { hands.AftHeld = true; hands.AftAftM = -_hands[(int)asd].Position.Z; }
                CanopyDynamics cd = cockpit.HandCanopy;
                bool shut = cd != null ? cd.Shut : cockpit.CanopyOpen01 < 0.01f;
                click = extra.Step(Time.deltaTime, hands, shut, cd?.AngleDeg ?? cockpit.CanopyOpen01 * 100.0);
                lh |= hands.FwdHeld;
            }
            if (click && lh) XrHandInput.Tick(lside == HandSide.Left, 0.8f, 0.03f);
            if (cockpit.HandLatch.Locked != cockpit.CanopyLocked)
            {
                cockpit.SetCanopyLock(cockpit.HandLatch.Locked);
                Debug.Log($"[VrCockpit] canopy latch {(cockpit.HandLatch.Locked ? "LOCKED" : "UNLOCKED")} ({_latchKind})");
            }
        }

        // ---- 6: the body ----

        private VrPilotBody.Pose _pose;

        /// <summary>
        /// Four two-bone chains (Core TwoBoneIk), 22 transforms written, no allocation: well under 0.1 ms of the
        /// 13.9 ms a 72 Hz frame has (estimate; the [QuestPerf] line measures the whole frame on the device).
        /// </summary>
        private void PoseBody()
        {
            if (_jumper) { PoseJumper(); return; }
            if (!Active) { _body.SetVisible(false); return; }
            _body.SetVisible(true);

            Vector3 lift = Vector3.up * _rise;   // climbing out: the body rises off the seat with the hand's push
            Vector3 shL = _eye + lift + U(SeatedPilot.ShoulderL), shR = _eye + lift + U(SeatedPilot.ShoulderR);
            Vector3 hipL = _eye + lift + U(SeatedPilot.HipL), hipR = _eye + lift + U(SeatedPilot.HipR);
            Vector3 throttleGrip = _hasThrottle ? U(_lever.Lever.GripAt(_lever.TFor(_driver.Sim.Aircraft.Throttle01))) : hipL + new Vector3(0f, 0.08f, 0.35f);
            Vector3 stickGrip = U(_stick.Grip);

            HandTarget(HandSide.Left, throttleGrip, Grabbable.Throttle, out Vector3 gripL, out Quaternion rotL, out float curlL, out Vector3 axisL);
            HandTarget(HandSide.Right, stickGrip, Grabbable.Stick, out Vector3 gripR, out Quaternion rotR, out float curlR, out Vector3 axisR);

            // Feet on the pedals: the pedal nodes when the model has them (the rig swings them with the rudder),
            // else the proxy, right rudder = right pedal forward, left pedal aft.
            Vector3 ballL = _footNodeL != null ? Local(_footNodeL.TransformPoint(_footOffL)) : _eye + U(_proxy.FootL) - Vector3.forward * (float)(_rud * _proxy.PedalTravelM);
            Vector3 ballR = _footNodeR != null ? Local(_footNodeR.TransformPoint(_footOffR)) : _eye + U(_proxy.FootR) + Vector3.forward * (float)(_rud * _proxy.PedalTravelM);
            // Ankle from the ball: the body's own foot pose (v3: sole on the pedal face), else the v2 toes-down offset.
            Vector3 heelL = _body.HeelFromBall(true), heelR = _body.HeelFromBall(false);

            // Everything to world now; the IK runs there, so a skinned asset's own joints and lengths can be used.
            Quaternion airRot = _air.rotation;
            _body.PlaceRoot(World(0.5f * (hipL + hipR)), airRot);
            _pose.Up = _air.up;
            // A tracked hand is drawn from its own joints, and the forearm runs to its own wrist.
            _pose.JointsL = TrackedJointsWorld(0);
            _pose.JointsR = TrackedJointsWorld(1);
            Arm(true, World(shL), World(gripL), airRot * rotL, curlL, World(_eye + U(SeatedPilot.ElbowPoleL)), _pose.JointsL, airRot * axisL, _clearance);
            Arm(false, World(shR), World(gripR), airRot * rotR, curlR, World(_eye + U(SeatedPilot.ElbowPoleR)), _pose.JointsR, airRot * axisR, _clearance);
            Leg(true, World(hipL), World(ballL), World(ballL + heelL), World(_eye + U(SeatedPilot.KneePoleL)));
            Leg(false, World(hipR), World(ballR), World(ballR + heelR), World(_eye + U(SeatedPilot.KneePoleR)));
            _body.Apply(_pose);
            PlaceProps();
        }

        /// <summary>Where a hand is drawn: ON the control it holds (the visible hand snaps to the grip), at the
        /// controller otherwise, and resting on its control when the controller is not tracked — the right hand on
        /// the stick, the left on the throttle — so a pilot who looks down always sees himself flying.</summary>
        private void HandTarget(HandSide side, Vector3 restGrip, Grabbable restOn, out Vector3 grip, out Quaternion rot, out float curl, out Vector3 axis)
        {
            HandPose h = _hands[(int)side];
            Grabbable held = _grab.Held(side);
            // The grip axis of the control the hand is closed on (cockpit frame), zero for anything else: a hand ON the
            // stick or the throttle wraps that control's own axis (ArmPose), not the controller's roll in the real hand.
            axis = Vector3.zero;
            rot = h.Tracked ? new Quaternion((float)h.Rotation.X, (float)h.Rotation.Y, (float)h.Rotation.Z, (float)h.Rotation.W) : VrPilotBody.RestGripRot(side == HandSide.Left);
            if (held != Grabbable.None)
            {
                grip = held == Grabbable.Stick ? U(_stick.Grip)
                     : held == Grabbable.Throttle ? U(_lever.Lever.GripAt(_lever.TFor(Throttle01)))
                     : held == Grabbable.CanopyLatch ? LatchPoint(_visual.Cockpit)
                     : held == Grabbable.CanopyLatchAft ? LatchHandle(_visual.Cockpit, fwd: false)
                     : held == Grabbable.CanopyLatchFwd ? LatchHandle(_visual.Cockpit, fwd: true)
                     : held is Grabbable.SpoilerHandle or Grabbable.TrimLever or Grabbable.TowRelease ? HeldGliderPoint(held)
                     : held >= Grabbable.PanelFirst && held <= Grabbable.PanelLast ? HeldPanelPoint(held)
                     : HeldBailoutPoint(held, h);
                curl = held == Grabbable.CanopyLatch && _latchKind == CanopyLatchKind.Squeeze ? 0.6f + 0.4f * (float)h.Pinch : 1f;
                axis = GripAxis(held);
                return;
            }
            if (h.Tracked) { grip = U(h.Position); curl = (float)h.Grip; return; }
            grip = restGrip;
            curl = 1f;
            axis = restOn == Grabbable.Throttle && !_hasThrottle ? Vector3.zero : GripAxis(restOn);
        }

        /// <summary>Up the stick's shaft (pivot → grip), or along the throttle lever (pivot → knob), cockpit frame.</summary>
        private Vector3 GripAxis(Grabbable g)
        {
            if (g == Grabbable.Stick && _stick != null) return U(_stick.Up);
            if (g == Grabbable.Throttle && _lever != null)
            {
                Vec3 knob = _lever.Lever.GripAt(_lever.TFor(_grab.IsHeld(Grabbable.Throttle) ? Throttle01 : _driver.Sim.Aircraft.Throttle01));
                Vec3 d = knob - _lever.Lever.Pivot;
                return d.LengthSquared > 1e-8 ? U(d.Normalized()) : Vector3.zero;
            }
            return Vector3.zero;
        }

        private readonly Vector3[][] _jointsWorld = { new Vector3[HandJoint.Count], new Vector3[HandJoint.Count] };

        private Vector3[] TrackedJointsWorld(int s)
        {
            if (!_hasJoints[s]) return null;
            Vector3[] w = _jointsWorld[s], l = _jointsLocal[s];
            for (int i = 0; i < w.Length; i++) w[i] = World(l[i]);
            return w;
        }

        /// <summary>
        /// One arm (Core <see cref="ArmPose"/>). On a control (<paramref name="gripAxis"/> non-zero) the hand wraps that
        /// control's own axis and the wrist sits behind the fist along the hand, so the wrist stays nearly straight and
        /// the palm faces the grip (Ty 2026-11-04: "the wrists are super unrealistic"). The elbow swings, eased, to keep
        /// the arm out of the structure (<paramref name="clearance"/>). A tracked hand keeps its own wrist; a free
        /// controller hand keeps the old wrist law (a fist's depth toward the shoulder) and only the elbow is chosen.
        /// </summary>
        private void Arm(bool left, Vector3 shoulder, Vector3 grip, Quaternion handRot, float curl, Vector3 pole, Vector3[] joints = null,
                         Vector3 gripAxis = default, IArmClearance clearance = null)
        {
            float upper = (float)SeatedPilot.UpperArmM, lower = (float)SeatedPilot.ForearmM;
            if (_body.TryChain(left ? "L_UpperArm" : "R_UpperArm", left ? "L_LowerArm" : "R_LowerArm", left ? "L_Hand" : "R_Hand",
                               out Vector3 root, out float u, out float l)) { shoulder = root; upper = u; lower = l; }
            int s = left ? 0 : 1;
            if (clearance is VrArmClearance vc && !vc.Ready) clearance = null;
            ArmPose.Result r;
            if (joints == null && gripAxis.sqrMagnitude > 1e-6f)
            {
                r = ArmPose.OnGrip(left, C(shoulder), C(grip), C(gripAxis), upper, lower, C(pole), SeatedPilot.WristToGripM, clearance, _swivel[s]);
                Vector3 fwd = U(r.HandForward), palm = U(r.Palm);
                // Grip-pose convention (VrPilotBody.PalmOf): +Z along the fist, the palm along −X (right) / +X (left).
                Vector3 x = left ? palm : -palm;
                handRot = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, x));
            }
            else
            {
                Vector3 toShoulder = shoulder - grip;
                Vector3 wrist0 = grip + (toShoulder.sqrMagnitude > 1e-8f ? toShoulder.normalized : Vector3.zero) * (float)SeatedPilot.WristToGripM;
                if (joints != null) { wrist0 = joints[HandJoint.Wrist]; grip = joints[HandJoint.Palm]; }
                r = ArmPose.Free(C(shoulder), C(wrist0), upper, lower, C(pole), clearance, _swivel[s]);
                r.Grip = C(r.Reached ? grip : U(r.Wrist) + (grip - wrist0));
            }
            // Ease the elbow's swing toward the one chosen: a wall appearing beside the arm must not flick the elbow.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            _swivel[s] = dt > 0f ? Mathf.MoveTowards(_swivel[s], (float)r.SwivelDeg, SwivelRateDegPerSec * dt) : (float)r.SwivelDeg;
            Vector3 w = U(r.Wrist);
            Vector3 elbow = U(ArmPose.ElbowAt(r.Shoulder, r.Wrist, upper, lower, C(pole), _swivel[s]));
            Vector3 g = U(r.Grip);
            if (left) { _pose.ShoulderL = shoulder; _pose.ElbowL = elbow; _pose.WristL = w; _pose.GripL = g; _pose.HandRotL = handRot; _pose.CurlL = curl; }
            else { _pose.ShoulderR = shoulder; _pose.ElbowR = elbow; _pose.WristR = w; _pose.GripR = g; _pose.HandRotR = handRot; _pose.CurlR = curl; }
        }

        private void Leg(bool left, Vector3 hip, Vector3 ball, Vector3 ankle, Vector3 pole)
        {
            float upper = (float)SeatedPilot.ThighM, lower = (float)SeatedPilot.ShinM;
            if (_body.TryChain(left ? "L_UpperLeg" : "R_UpperLeg", left ? "L_LowerLeg" : "R_LowerLeg", left ? "L_Foot" : "R_Foot",
                               out Vector3 root, out float u, out float l)) { hip = root; upper = u; lower = l; }
            TwoBoneIk.Result r = TwoBoneIk.Solve(C(hip), C(ankle), upper, lower, C(pole));
            Vector3 a = U(r.End);
            Vector3 b = a + (ball - ankle);
            if (left) { _pose.HipL = hip; _pose.KneeL = U(r.Middle); _pose.AnkleL = a; _pose.BallL = b; }
            else { _pose.HipR = hip; _pose.KneeR = U(r.Middle); _pose.AnkleR = a; _pose.BallR = b; }
        }
    }
}

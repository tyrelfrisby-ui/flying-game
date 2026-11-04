using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// PHASE 3: TRACKED HANDS, and the 2-33's own handles (docs/vr-cockpit.md, "Hands" and "The 2-33").
    ///
    /// HANDS. Per side, every frame, <see cref="HandSourceArbiter"/> picks the controller (if its pose is tracked) or
    /// the tracked hand. A tracked hand's 26 joints (XR Hands, through <see cref="XrTrackedHands"/>) are taken into the
    /// cockpit through the SAME recentre as a controller (QuestRig.TrackingToSeat), and <see cref="HandGrabDetector"/>
    /// turns them into the same <see cref="HandPose"/> a controller gives: grab = a pinch or a fist, with hysteresis
    /// and a quarter-second grace through tracking loss. From there the grab, the levers, the latches and the body
    /// are the controllers' own code. The hand's point is the PINCH point (between thumb and index tips) when the
    /// hand is pinching and the middle of the fist when it is not, chosen while open and kept while closed.
    /// Without the XR Hands joints the Hand Interaction Profile's grip pose and pinch value stand in (no fist, and no
    /// fingers drawn).
    ///
    /// Nothing here moves the camera.
    ///
    /// THE 2-33. The dive-brake handle in the seat's own cockpit (DiveBrake_Lever_F / _R), the trim lever
    /// (Trim_Lever) and the tow release (TowRelease_F / _R), each bound from the rig's own node: its pivot, its knob
    /// (the farthest vertex of its own meshes from the pivot — the end a hand takes), and the two ends of its travel
    /// worked out with the rig's OWN rotation (RestRot · AngleAxis(angle, axis)), so no sign is reasoned out here.
    /// The handle and the lever go into TouchFlightControls (SpoilerHandle01, PitchTrimLever), the knob into
    /// TowController.HandPull: no second path into the flight model.
    /// </summary>
    public sealed partial class VrCockpitHands
    {
        public bool SpoilerHandleHeld => Active && _spoilerLever != null && _grab.IsHeld(Grabbable.SpoilerHandle);
        /// <summary>The held dive-brake handle, 0 stowed .. 1 full aft.</summary>
        public float SpoilerHandle01 { get; private set; }
        public bool TrimLeverHeld => Active && _trimLever != null && _grab.IsHeld(Grabbable.TrimLever);
        /// <summary>The held trim lever, −1 forward (nose down) .. +1 aft (nose up).</summary>
        public float TrimLever { get; private set; }

        // ---- the tracked hands ----

        private readonly HandSourceArbiter[] _arb = { new(), new() };
        private readonly HandGrabDetector[] _det = { new(), new() };
        private readonly OneEuroFilter3[] _filt = { new(), new() };
        private readonly Vector3[][] _jp = { new Vector3[HandJoint.Count], new Vector3[HandJoint.Count] };
        private readonly Quaternion[][] _jr = { new Quaternion[HandJoint.Count], new Quaternion[HandJoint.Count] };
        private readonly Vec3[] _jc = new Vec3[HandJoint.Count];
        /// <summary>This frame's joints in the cockpit frame, per side, for drawing; valid where <see cref="_hasJoints"/>.</summary>
        private readonly Vector3[][] _jointsLocal = { new Vector3[HandJoint.Count], new Vector3[HandJoint.Count] };
        private readonly bool[] _hasJoints = new bool[2];
        private readonly bool[] _pinchMode = new bool[2];
        private readonly bool[] _pinchOwned = new bool[2];
        private readonly HandSource[] _logSource = new HandSource[2];
        // For the [VrHands] line: how each hand was read this frame.
        private readonly string[] _diagVia = { "-", "-" };
        private readonly double[] _diagCurl = { double.NaN, double.NaN }, _diagGap = { double.NaN, double.NaN };
        private float _diagNext;

        /// <summary>The input side's gates on the hands (TouchFlightControls), for the [VrHands] line.</summary>
        public static int GateRightFinger = int.MinValue, GateLeftFinger = int.MinValue;

        /// <summary>Every 2 s while a tracked hand is in the cockpit, one line per hand: what it is read from (XR Hands
        /// joints, or the hand-interaction pose with no joints), its curl / pinch gap / grip, pinch or fist mode,
        /// what it is on and holding, and how far it is from the stick and throttle — so a hand that won't take hold
        /// can be diagnosed from logcat (adb logcat -s Unity | grep VrHands).</summary>
        private void DiagLog()
        {
            if (Time.unscaledTime < _diagNext) return;
            if (_hands[0].Source != HandSource.TrackedHand && _hands[1].Source != HandSource.TrackedHand) return;
            _diagNext = Time.unscaledTime + 2f;
            var sb = new System.Text.StringBuilder("[VrHands]");
            for (int s = 0; s < 2; s++)
            {
                HandSide side = (HandSide)s;
                HandPose h = _hands[s];
                sb.Append(s == 0 ? " L " : " | R ").Append(h.Source).Append(' ').Append(_diagVia[s]);
                if (!h.Tracked) continue;
                sb.Append($" curl {_diagCurl[s]:0.00} gap {_diagGap[s] * 100:0.0}cm grip {h.Grip:0.00}{(_det[s].Closed ? " CLOSED" : "")}{(_pinchMode[s] ? " pinchMode" : "")}");
                sb.Append($" on {_grab.Hover(side)} held {_grab.Held(side)}");
                for (int g = (int)Grabbable.Stick; g <= (int)Grabbable.Throttle; g++)
                {
                    CockpitGrab.Point p = _grab.Points[g];
                    if (p.Enabled) sb.Append($" →{(Grabbable)g} {System.Math.Sqrt(p.Distance2(h.Position)) * 100:0.0}cm");
                }
            }
            sb.Append($" | joints {XrTrackedHands.Status} | pad owns stick {PadOwnsStick}");
            sb.Append(" touch R ").Append(GateRightFinger == int.MinValue ? "free" : GateRightFinger.ToString())
              .Append(" L ").Append(GateLeftFinger == int.MinValue ? "free" : GateLeftFinger.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>This frame's index-finger curl and thumb–index gap per tracked hand (the panel's poke reads them).</summary>
        private readonly double[] _indexCurl = { 1.0, 1.0 }, _gapM = { 0.0, 0.0 };

        /// <summary>Pinch mode while open: thumb and index within 4.5 cm and the fingers not wrapped round anything.</summary>
        private const double PinchModeGapM = 0.045, PinchModeMaxCurl = 0.45;
        /// <summary>The middle of a closed fist, from the palm joint toward the palm side.</summary>
        private const float FistOffsetM = 0.025f;

        /// <summary>The frame time the hands are read with, when ≥ 0 (VrHandJointCheck drives frames in edit mode, where
        /// Time does not advance). Never set in the game.</summary>
        public static float DeltaTimeForTesting = -1f;

        /// <summary>This frame's hand time step (real time: the hands do not pause with the sim).</summary>
        private static double HandDt => DeltaTimeForTesting >= 0f ? DeltaTimeForTesting : Time.unscaledDeltaTime;

        private void ReadAllHands()
        {
            QuestControllerFrame f = XrControllers.Read(out bool any);
            double dt = DeltaTimeForTesting >= 0f ? DeltaTimeForTesting : Time.unscaledDeltaTime;
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                _hasJoints[s] = false;
                _diagVia[s] = "-"; _diagCurl[s] = _diagGap[s] = double.NaN;
                bool ctl = any && !f.HandsOnly && XrHandInput.TryGripPose(left, out Vector3 cp, out Quaternion cq);
                bool joints = !ctl && XrTrackedHands.TryRead(left, _jp[s], _jr[s]);
                float aimPinch = 0f, aimGrasp = float.NaN;
                Vector3 hp = default; Quaternion hq = Quaternion.identity;
                bool aim = !ctl && !joints && XrControllers.TryTrackedHandPose(left, out hp, out hq, out aimPinch, out aimGrasp);
                HandSource src = _arb[s].Step(ctl, joints || aim, dt);
                if (src != _logSource[s])
                {
                    _logSource[s] = src;
                    Debug.Log($"[VrCockpit] {(left ? "left" : "right")} hand: {src}" +
                              (src == HandSource.TrackedHand ? joints ? " (XR Hands joints)" : " (hand interaction pose, no joints: " + XrTrackedHands.Status + ")" : ""));
                }

                if (src == HandSource.Controller)
                {
                    _det[s].Reset(); _filt[s].Reset(); _pinchMode[s] = false;
                    XrHandInput.TryGripPose(left, out cp, out cq);
                    _hands[s] = HandPose.None;
                    if (!_rig.TrackingToSeat(cp, cq, out Vector3 sp, out Quaternion sq)) continue;
                    _hands[s] = new HandPose
                    {
                        Source = HandSource.Controller,
                        Position = C(_eye + Vector3.up * _rise + sp),   // climbing out raises the seat the hands are measured from
                        Rotation = new Quat(sq.x, sq.y, sq.z, sq.w),
                        Grip = left ? f.LeftGrip : f.RightGrip,
                        Pinch = left ? f.LeftTrigger : f.RightTrigger,
                    };
                    continue;
                }

                if (src == HandSource.TrackedHand && (joints || aim))
                {
                    Vector3 seat = _eye + Vector3.up * _rise;
                    double gap = double.NaN, curl = double.NaN;
                    Vector3 point; Quaternion rot;
                    if (joints)
                    {
                        bool ok = true;
                        for (int i = 0; i < HandJoint.Count && ok; i++)
                        {
                            ok = _rig.TrackingToSeat(_jp[s][i], _jr[s][i], out Vector3 sp, out Quaternion sq);
                            _jointsLocal[s][i] = seat + sp;
                            _jc[i] = C(seat + sp);
                            if (i == HandJoint.Palm) _jr[s][HandJoint.Palm] = sq;   // reused: the palm's cockpit-frame rotation
                        }
                        if (!ok) { _hands[s] = _det[s].Step(false, Vec3.Zero, Quat.Identity, double.NaN, double.NaN, dt); continue; }
                        _hasJoints[s] = true;
                        gap = (_jc[HandJoint.ThumbTip] - _jc[HandJoint.IndexTip]).Length;
                        curl = HandGrabDetector.MeanCurl(_jc);
                        int im = HandJoint.IndexMetacarpal;
                        _indexCurl[s] = HandGrabDetector.FingerCurl(_jc[im], _jc[im + 1], _jc[im + 2], _jc[im + 3], _jc[im + 4]);
                        _gapM[s] = gap;
                        rot = _jr[s][HandJoint.Palm];
                        // The point a hand takes hold with: chosen while open, kept while closed (a switch mid-pull would
                        // move the lever by the distance between the two).
                        if (!_det[s].Closed) _pinchMode[s] = gap < PinchModeGapM && curl < PinchModeMaxCurl;
                        point = _pinchMode[s]
                            ? 0.5f * (_jointsLocal[s][HandJoint.ThumbTip] + _jointsLocal[s][HandJoint.IndexTip])
                            : _jointsLocal[s][HandJoint.Palm] + rot * Vector3.down * FistOffsetM;
                    }
                    else
                    {
                        if (!_rig.TrackingToSeat(hp, hq, out Vector3 sp, out Quaternion sq)) { _hands[s] = HandPose.None; continue; }
                        point = seat + sp; rot = sq;
                        gap = HandGrabDetector.PinchFullOpenM - aimPinch * 0.05;   // the profile's 0..1 pinch as a gap (1 → 1 cm)
                        // The profile's grasp as the curl, so a FIST closes too (before: NaN, and only a pinch could
                        // take hold — a fist round the stick did nothing).
                        if (!float.IsNaN(aimGrasp)) curl = aimGrasp;
                    }
                    _diagVia[s] = joints ? "joints" : "aim-pose(no joints" + (float.IsNaN(aimGrasp) ? ", no grasp)" : ")");
                    _diagCurl[s] = curl; _diagGap[s] = gap;
                    Vec3 filtered = _filt[s].Step(C(point), dt);
                    _hands[s] = _det[s].Step(true, filtered, new Quat(rot.x, rot.y, rot.z, rot.w), gap, curl, dt);
                    continue;
                }

                // Nothing this frame: a tracked hand coasts through a short loss, anything else is gone.
                _hands[s] = _det[s].Step(false, Vec3.Zero, Quat.Identity, double.NaN, double.NaN, dt);
                if (!_hands[s].Tracked) _filt[s].Reset();
            }
        }

        /// <summary>A tracked hand on or holding a control owns its pinch until the pinch opens, so a second's pinch on the
        /// dive-brake handle never opens the menu or recentres the view.</summary>
        private void OwnPinches()
        {
            for (int s = 0; s < 2; s++)
            {
                HandSide side = (HandSide)s;
                bool on = _hands[s].Source == HandSource.TrackedHand &&
                          (_grab.Held(side) != Grabbable.None || _grab.Hover(side) != Grabbable.None);
                _pinchOwned[s] = on || (_pinchOwned[s] && _det[s].Closed);
            }
            XrControllers.LeftPinchOwned = _pinchOwned[0];
            XrControllers.RightPinchOwned = _pinchOwned[1];
        }

        private void ReleaseHands()
        {
            for (int s = 0; s < 2; s++) { _det[s].Reset(); _filt[s].Reset(); _pinchOwned[s] = false; _hasJoints[s] = false; }
            XrControllers.LeftPinchOwned = XrControllers.RightPinchOwned = false;
        }

        // ---- the 2-33's handles ----

        private RigHandle _spoilerNode, _trimNode, _towNode;
        private HingedLever _spoilerLever, _trimLever;
        private SlideKnob _towKnob;
        private TouchFlightControls _touch;
        private TowController _tow;
        private readonly LeverDetents _spoilerFeel = new(SpoilerHandle.WheelBrakeFrom);
        private LeverDetents _trimFeel = new();

        private RigHandle Handle(string node) => RigHandle.Find(_visual?.Rig, node);

        /// <summary>The knob in the cockpit frame with the node at the rig's value <paramref name="v"/>.</summary>
        private Vector3 KnobAt(RigHandle h, double v) => Local(h.KnobWorld(v));

        private void BindGlider(string brief)
        {
            _touch = _air.GetComponent<TouchFlightControls>();
            _tow = _air.GetComponent<TowController>();
            _spoilerNode = brief != null ? Handle("DiveBrake_Lever_" + brief) : null;
            _trimNode = Handle("Trim_Lever");
            _towNode = brief != null ? Handle("TowRelease_" + brief) : null;
            _spoilerLever = _spoilerNode != null && _touch != null
                ? new HingedLever(C(Local(_spoilerNode.PivotWorld)), C(KnobAt(_spoilerNode, _spoilerNode.V0)), C(KnobAt(_spoilerNode, _spoilerNode.V1)), C(Quaternion.Inverse(_air.rotation) * _spoilerNode.HingeWorld))
                : null;
            _trimLever = _trimNode != null && _touch != null
                ? new HingedLever(C(Local(_trimNode.PivotWorld)), C(KnobAt(_trimNode, _trimNode.V0)), C(KnobAt(_trimNode, _trimNode.V1)), C(Quaternion.Inverse(_air.rotation) * _trimNode.HingeWorld))
                : null;
            _towKnob = _towNode != null && _tow != null ? new SlideKnob(C(KnobAt(_towNode, _towNode.V0)), C(KnobAt(_towNode, _towNode.V1))) : null;
            // The trim quadrant's notches (the rig's detents), as fractions of the lever's travel: felt, not snapped to.
            RigNode tn = _trimNode != null ? RigNodeNamed("Trim_Lever") : null;
            if (tn?.Detents != null)
            {
                var d = new double[tn.Detents.Length];
                for (int i = 0; i < d.Length; i++) d[i] = _trimNode.TFor(tn.Detents[i]);
                _trimFeel = new LeverDetents(d);
            }
            else _trimFeel = new LeverDetents();
            if (_spoilerNode != null || _trimNode != null || _towNode != null)
                Debug.Log($"[VrCockpit] {_driver.AircraftId} glider handles: " +
                          $"dive brake {(_spoilerLever != null ? $"{_spoilerNode.Name} ({_spoilerLever.SweepDeg:F0}° on a {_spoilerLever.ArmM * 100:F0} cm arm, wheel brake past {SpoilerHandle.WheelBrakeFrom:P0})" : "none")}; " +
                          $"trim {(_trimLever != null ? $"{_trimLever.SweepDeg:F0}° on a {_trimLever.ArmM * 100:F0} cm arm, {tn?.Detents?.Length ?? 0} notches felt" : "none")}; " +
                          $"tow release {(_towKnob != null ? $"{_towNode.Name} ({_towKnob.TravelM * 1000:F0} mm pull, fires at {SpringPullKnob.FireAt:P0})" : "none")}");
        }

        private void UpdateGliderPoints()
        {
            Aircraft ac = _driver.Sim.Aircraft;
            // The handle is where the rig draws it: the sim's spoiler. Held, it is the hand's.
            double spoiler = ac.CurrentDeflections.SpoilerFraction;
            if (_spoilerLever != null && !_grab.IsHeld(Grabbable.SpoilerHandle)) _spoilerFeel.Reset(spoiler);
            _grab.Set(Grabbable.SpoilerHandle, _spoilerLever != null ? _spoilerLever.GripAt(_grab.IsHeld(Grabbable.SpoilerHandle) ? SpoilerHandle01 : spoiler) : Vec3.Zero, _spoilerLever != null);
            double trimT = _trimNode != null && _touch != null ? _trimNode.TFor(_grab.IsHeld(Grabbable.TrimLever) ? TrimLever : _touch.PitchTrimLever) : 0.0;
            if (_trimLever != null && !_grab.IsHeld(Grabbable.TrimLever)) _trimFeel.Reset(trimT);
            _grab.Set(Grabbable.TrimLever, _trimLever != null ? _trimLever.GripAt(trimT) : Vec3.Zero, _trimLever != null);
            _grab.Set(Grabbable.TowRelease, _towKnob != null ? _towKnob.GripAt(_tow.KnobPull01) : Vec3.Zero, _towKnob != null);
        }

        private bool GrabGlider(HandSide side, Grabbable g, Vec3 hand)
        {
            switch (g)
            {
                case Grabbable.SpoilerHandle:
                    SpoilerHandle01 = _touch.SpoilerHandle01;
                    _spoilerLever.Grab(hand, SpoilerHandle01);
                    _spoilerFeel.Reset(SpoilerHandle01);
                    Debug.Log($"[VrCockpit] dive-brake handle taken ({side}, {_hands[(int)side].Source}) at {SpoilerHandle01:F2}");
                    return true;
                case Grabbable.TrimLever:
                    TrimLever = _touch.PitchTrimLever;
                    double t = _trimNode.TFor(TrimLever);
                    _trimLever.Grab(hand, t);
                    _trimFeel.Reset(t);
                    return true;
                case Grabbable.TowRelease:
                    _towKnob.Grab(hand, _tow.KnobPull01);
                    return true;
            }
            return false;
        }

        private void DriveGlider()
        {
            float dt = Time.deltaTime;
            if (_spoilerLever != null && _grab.IsHeldBy(Grabbable.SpoilerHandle, out HandSide ss))
            {
                double t = _spoilerLever.Follow(_hands[(int)ss].Position);
                SpoilerHandle01 = (float)_spoilerNode.ParamFor(t);
                Feel(ss, _spoilerFeel.Step(t));
            }
            if (_trimLever != null && _grab.IsHeldBy(Grabbable.TrimLever, out HandSide ts))
            {
                double t = _trimLever.Follow(_hands[(int)ts].Position);
                TrimLever = (float)_trimNode.ParamFor(t);
                Feel(ts, _trimFeel.Step(t));
            }
            if (_towKnob != null)
            {
                bool held = _grab.IsHeldBy(Grabbable.TowRelease, out HandSide ks);
                double pull = held ? _towKnob.Follow(_hands[(int)ks].Position) : 0.0;
                if (_tow.HandPull(held, (float)pull, dt)) XrHandInput.Tick(ks == HandSide.Left, 1f, 0.06f);
            }
        }

        /// <summary>A bump at a stop, a click at a detent (the wheel brake's start, a trim notch). Controllers only:
        /// a bare hand has nothing to buzz.</summary>
        private void Feel(HandSide side, LeverFeel f)
        {
            if (f == LeverFeel.None || _hands[(int)side].Source != HandSource.Controller) return;
            bool left = side == HandSide.Left;
            if ((f & LeverFeel.Stop) != 0) XrHandInput.Tick(left, 0.7f, 0.03f);
            else XrHandInput.Tick(left, 0.35f, 0.02f);
        }

        /// <summary>After the rig has drawn the frame (this runs at execution order 10001): a HELD lever is drawn where the
        /// hand has it — the sim's spoiler follows the handle a frame later and the rig would show that — and the trim
        /// lever is always drawn at the trim itself rather than in the rig's nearest notch, so the lever, the hand and
        /// the A / B buttons agree.</summary>
        private void DrawGliderLevers()
        {
            if (!Active) return;
            if (_spoilerNode?.T != null && _grab.IsHeld(Grabbable.SpoilerHandle))
                _spoilerNode.T.localRotation = _spoilerNode.RestRot * Quaternion.AngleAxis((float)Lerp(_spoilerNode, SpoilerHandle01), _spoilerNode.Axis);
            if (_trimNode?.T != null && _touch != null)
            {
                double lever = _grab.IsHeld(Grabbable.TrimLever) ? TrimLever : _touch.PitchTrimLever;
                _trimNode.T.localRotation = _trimNode.RestRot * Quaternion.AngleAxis((float)Lerp(_trimNode, lever), _trimNode.Axis);
            }
        }

        private static double Lerp(RigHandle h, double param) => h.ValueFor(param);

        private Vector3 HeldGliderPoint(Grabbable g) => g switch
        {
            Grabbable.SpoilerHandle => U(_spoilerLever.GripAt(SpoilerHandle01)),
            Grabbable.TrimLever => U(_trimLever.GripAt(_trimNode.TFor(TrimLever))),
            Grabbable.TowRelease => U(_towKnob.GripAt(_tow.KnobPull01)),
            _ => _eye,
        };
    }
}

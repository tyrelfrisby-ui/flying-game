using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE HANDS-ON BAIL-OUT (docs/vr-cockpit.md phase 2; Ty: "unlatch the canopy and the seatbelts and the headset cables
    /// and jump out … they need to look down and grab and pull the rip chord"). The physics is Core's (CanopyHinge,
    /// Harness, HeadsetCord, Egress, Ripcord); this is the glue:
    ///
    ///  IN THE COCKPIT (Active)
    ///   canopy    grab Grip_Canopy_Rail (or the frame's free edge) once unlatched and push: the Pitts slides aft then
    ///             swings over to the right, the Extra swings right. CanopyDynamics steps it in the airload every frame
    ///             (it flips open by itself above ~38 kt unlatched); its drag goes into the sim (CockpitRigState.Fill).
    ///   belts     grab Grip_Harness_Release_* and lift (lever) or twist (rotary); every belt open = free of the seat.
    ///   cord      grab the plug at Headset_Jack_* and draw it 3 cm out; the cord is a rope drawn head → jack.
    ///   climb     grab a SILL and push down: the seat rises by the push (the view with it — his own hand did it);
    ///             strapped in it will not, against a shut canopy it stops at the head; out over the side, or pulled
    ///             out by the slipstream once the shoulders are in it.
    ///  OUT (the jumper)
    ///   the head rig is placed on the falling body (QuestRig → <see cref="AdjustOrigin"/>): level, the heading he left
    ///   on, no rotation forced on the view ever; his real head looks around. The cord, if still plugged, goes taut and
    ///   pulls (a bounded 4 cm offset and a strong buzz), then lets go. The ripcord is on his LEFT chest strap: look
    ///   down, grab, pull 10 cm DOWN. Tail and wing strikes are detected and reported.
    /// </summary>
    public sealed partial class VrCockpitHands
    {
        // ---- the cockpit half ----
        private Transform _canopyNode, _railNode, _relMainNode, _relLapNode, _jackNode, _jettisonNode;
        private Vector3 _hingeProxy, _sillL, _sillR, _relMainProxy, _relLapProxy, _jackProxy, _jettisonShut;
        /// <summary>Which hand holds the canopy this frame (the rail, or — Extra — its latch handles, which are on the
        /// frame), −1 none.</summary>
        private int _canopyHolder = -1;
        private bool _slamLogged;
        private float _rise;
        private bool _openCockpit, _modelHarness;
        private EgressClearance _clear;
        private Vector3 _prevAirPos;
        private Quaternion _prevAirRot = Quaternion.identity;
        private bool _blockedTicked, _taughtLogged;

        // ---- out of the aeroplane ----
        private bool _jumper;
        private Vector3 _jOrigin, _yank;
        private Quaternion _jRot = Quaternion.identity;
        private float _jT, _shockHapticT;
        private bool _landedFelt;
        private readonly HashSet<string> _struck = new();
        private readonly HandPose[] _jHands = { HandPose.None, HandPose.None };
        private readonly Ripcord _ripcord = Ripcord.BackRig();
        /// <summary>No headset cord, in any aeroplane (Ty 2026-11-03: "remove the floating headset cable in the cockpit",
        /// he finds it annoying). Off here, at the one place a cord is made: no <see cref="HeadsetCord"/> is created, so
        /// there is no rope, no plug, no plug to grab and nothing to tug his head on the way out. The Core model
        /// (HeadsetCord, VerletRope) and the aircraft's static Headset_Jack_* sockets stay; set true to bring it back.</summary>
        internal static readonly bool HeadsetCordOn = false;
        /// <summary>The ripcord handle, relative to the jumper's eye in his own frame: the LEFT main lift web at the chest
        /// strap (Strong Para-Cushion manual), 0.40 below and 0.15 ahead of the eye, 0.10 left (E).</summary>
        private static readonly Vector3 RipcordAtEye = new(-0.10f, -0.40f, 0.15f);

        // ---- drawn ----
        private VerletRope _rope;
        private LineRenderer _ropeLine;
        private Vector3[] _ropeDraw;
        private GameObject _props, _plug, _fitMain, _fitLap, _lapBelt, _strapL, _strapR, _dRing;
        private GameObject _gear;
        private Transform _gearDeployed, _gearRipcord;
        private SkinnedMeshRenderer _gearCanopy;
        private int _inflateIdx = -1;
        private bool _gearTried;
        private Material _cordMat;

        /// <summary>The pilot-gear asset (CONTRACT.md phase 2): container, harness, ripcord, the deployed canopy.</summary>
        public const string GearAssetPath = "Models/pilot-gear/pilot-gear-own";

        /// <summary>Called by <see cref="QuestRig"/> after it has placed the origin: the climb raises the seat, and once he
        /// is out the origin IS him. Ticks first, so this frame's numbers are used (a falling body moves 0.7 m a frame).</summary>
        public void AdjustOrigin(ref Vector3 pos, ref Quaternion rot)
        {
            Tick();
            if (_jumper) { pos = _jOrigin + _yank; rot = _jRot; return; }
            if (Active && _air != null) pos += _air.rotation * (Vector3.up * _rise) + _yank;
        }

        // ---- binding ----

        private void BindBailout(string full)
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            _rise = 0f;
            _yank = Vector3.zero;
            if (cockpit == null) return;
            string id = _driver.AircraftId ?? "";
            Aircraft ac = _driver.Sim.Aircraft;

            // The canopy: Pitts and Extra (the two whose latches are built). Its bubble's size is Core's, measured off
            // Codex's models (CanopySpec.For); the hinge is the Canopy node's origin (the rig turns it about that).
            _canopyNode = Node("Canopy");
            _railNode = Node("Grip_Canopy_Rail");
            _openCockpit = _canopyNode == null && _air.Find("Canopy") == null;
            _modelHarness = Node("Harness_Rear") != null || Node("Harness_Front") != null;
            if (_latchKind != CanopyLatchKind.None)
            {
                CanopySpec spec = CanopySpec.For(id);
                spec = WithModelStopTab(spec);
                if (cockpit.HandCanopy == null || cockpit.HandCanopy.Spec.Motion != spec.Motion || System.Math.Abs(cockpit.HandCanopy.Spec.LengthM - spec.LengthM) > 1e-3
                    || !SameTab(cockpit.HandCanopy.Spec.StopTab, spec.StopTab))
                    cockpit.HandCanopy = new CanopyDynamics(spec);
                if (_railNode == null) Contract("Grip_Canopy_Rail");
            }
            else cockpit.HandCanopy = null;
            // The hinge line on the right sill, 0.29 m below the eye (Codex's Extra: Eye_Rear y 0.55, hinge axis 0.261; E
            // for the Pitts), HingeOutboardM right of the centre-line the eye is on.
            _hingeProxy = _eye + new Vector3((float)(cockpit.HandCanopy?.Spec.HingeOutboardM ?? 0.35), -0.29f, 0f);
            // Body frame = the config's (x fwd, y right, z down, the CG at Mass.Cg); the aeroplane's transform is its CG.
            cockpit.CanopyDragPointBody = ac.Config.Mass.CgVec() + ToBody(_eye + Vector3.up * 0.25f);
            _canopyHolder = -1;

            // The jettison (P2c-3). Pitts: the RED knob on the far right where the bow tubes meet the sill rail, per
            // cockpit (confirmed by Ty 2026-11-02); it is on the canopy frame and pulls the right-hand hinge pins. Its
            // node, else a proxy at that junction: just inboard of the hinge line, on the rail, at the bows' station (the
            // ball latch grip's, which rides between the bows), else 0.30 m ahead of the eye (E). Extra: the model's own
            // right-sill emergency release, CanopyRelease_R / _F, on the airframe; none if the model has none.
            _jettisonNode = null;
            bool pitts = _latchKind == CanopyLatchKind.CableLever;
            if (pitts && full != null)
            {
                _jettisonNode = Contract("Grip_Canopy_Jettison_" + full) ?? Contract("Canopy_Jettison_Knob_" + full);
                Transform bows = Node(full == "Front" ? "Grip_Canopy_Latch_Front" : "Grip_Canopy_Latch");
                Vector3 h = HingeShut();
                _jettisonShut = new Vector3(h.x - 0.015f, h.y + 0.01f, bows != null ? Local(bows.position).z : _eye.z + 0.30f);
            }
            else if (_latchKind == CanopyLatchKind.Squeeze)
            {
                string b = full == "Front" ? "F" : "R";
                _jettisonNode = Node("CanopyRelease_" + b);
            }
            JettisonPull jp = JettisonPull.For(id, _jettisonNode != null);
            if (jp == null) cockpit.HandJettison = null;
            else if (cockpit.HandJettison == null || System.Math.Abs(cockpit.HandJettison.TravelM - jp.TravelM) > 1e-6) cockpit.HandJettison = jp;

            // The belts: the type's set (Core SeatHarness.For), plus a lap belt where Codex models one on a single-release type.
            // No belts (Ty 2026-11-05; Core BeltsAndCords): an empty harness, so nothing holds him in the seat.
            cockpit.Harness ??= SeatHarness.ForCockpit(id);
            _relMainNode = full != null ? Contract("Grip_Harness_Release_" + full) : null;
            _relLapNode = full != null ? Node("Grip_Harness_Release_Lap_" + full) : null;
            if (_relLapNode != null && cockpit.Harness.Releases.Count == 1)
                cockpit.Harness.Releases.Add(new HarnessRelease("lap", HarnessReleaseKind.LiftLever, new Vec3(0, 0.6, 0.8)));
            bool extra = id.StartsWith("extra");
            // Proxies (E): the Extra's two per the 300S card — top on the right, bottom on the left; one rotary centred.
            _relMainProxy = _eye + (extra ? new Vector3(0.09f, -0.45f, 0.16f) : new Vector3(0f, -0.58f, 0.17f));
            _relLapProxy = _eye + new Vector3(-0.11f, -0.62f, 0.15f);

            // The cord: Codex's jack (phase 1 already has the empties), else the right side below the shoulder (E).
            if (HeadsetCordOn) cockpit.Cord ??= new HeadsetCord();
            else cockpit.Cord = null;
            _jackNode = full != null ? Contract("Headset_Jack_" + full) : null;
            _jackProxy = _eye + new Vector3(0.24f, -0.50f, -0.10f);

            // The sills either side, at elbow height beside him (E).
            _sillL = _eye + new Vector3(-0.33f, -0.30f, 0.05f);
            _sillR = _eye + new Vector3(0.33f, -0.30f, 0.05f);

            _clear = EgressClearance.From(ac.Config);
            _prevAirPos = _air.position; _prevAirRot = _air.rotation;
            EnsureProps();
            _rope?.Snap(C(HeadCableRoot()), new Vec3(0, -1, 0));
            CanopyDynamics cd = cockpit.HandCanopy;
            Debug.Log($"[VrEgress] {id}: canopy {(cd == null ? _openCockpit ? "none (open cockpit)" : "not hands-on for this type" : $"{cd.Spec.Motion}, ΔCp {cd.Bubble.NetCp:F3} on {cd.Bubble.PlanAreaM2:F2} m², flings open unlatched above {Kt(cd.FlipOpenQ(0)):F0} KIAS, held shut by a hand to {Kt(cd.FlipOpenQ(CanopyDynamics.HandForceN)):F0}, fittings {cd.Spec.FittingStrengthN:F0} N; jettison {(cockpit.HandJettison == null ? "none" : _jettisonNode != null ? "on its node" : "proxy")}")}; " +
                      $"belts {cockpit.Harness.Releases.Count} ({(_relMainNode != null ? "on the node" : "proxy")}); cord {HeadsetCord.DefaultLengthM:F1} m to the jack ({(_jackNode != null ? "node" : "proxy")})");
        }

        /// <summary>
        /// The Pitts' stop tab where the MODEL has it: Canopy_Stop_Rubber's meshes, as a capsule along their longest
        /// extent (radius half the smaller ones), in the canopy's hinge-relative shut frame (cockpit axes, the hinge at the
        /// Canopy node's origin), replacing Core's copy of the same numbers. Logged either way.
        /// </summary>
        private CanopySpec WithModelStopTab(CanopySpec spec)
        {
            if (spec.StopTab == null) return spec;
            Transform rubber = Node("Canopy_Stop_Rubber");
            var mf = rubber != null ? rubber.GetComponentsInChildren<MeshFilter>() : null;
            if (mf == null || mf.Length == 0 || _canopyNode == null)
            {
                Debug.Log($"[VrEgress] {_driver.AircraftId}: no Canopy_Stop_Rubber mesh{(_canopyNode == null ? " (no Canopy node)" : "")}; the stop tab is Core's copy");
                return spec;
            }
            Vector3 hinge = Local(_canopyNode.position);
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (MeshFilter f in mf)
            {
                if (f.sharedMesh == null) continue;
                foreach (Vector3 v in f.sharedMesh.vertices)
                {
                    Vector3 p = Local(f.transform.TransformPoint(v)) - hinge;
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
            }
            Vector3 size = hi - lo, mid = 0.5f * (lo + hi);
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float len = size[axis];
            float r = 0.25f * (size.x + size.y + size.z - len);   // half the mean of the two small extents
            Vector3 dir = Vector3.zero; dir[axis] = 1f;
            float half = Mathf.Max(0f, 0.5f * len - r);
            Vec3 a = C(mid - dir * half), b = C(mid + dir * half);
            CanopySpec s = spec.WithStopTab(spec.StopTab.WithTab(a, b, r));
            Vec3 c0 = 0.5 * (spec.StopTab.Tab.A + spec.StopTab.Tab.B);
            Debug.Log($"[VrEgress] {_driver.AircraftId}: stop tab from the model's Canopy_Stop_Rubber: {len * 1000:F0} mm × r {r * 1000:F1} mm at " +
                      $"({mid.x:F3}, {mid.y:F3}, {mid.z:F3}) from the hinge (Core's copy {(C(mid) - c0).Length * 1000:F1} mm away); the canopy rests on it at {s.StopTab.RestAngleDeg(s.SlideM):F1}°");
            return s;
        }

        private static bool SameTab(CanopyStopTab x, CanopyStopTab y) =>
            x == null ? y == null : y != null && (x.Tab.A - y.Tab.A).Length < 1e-4 && (x.Tab.B - y.Tab.B).Length < 1e-4;

        private static double Kt(double q) => System.Math.Sqrt(2.0 * q / 1.225) * 1.9438445;

        /// <summary>Cockpit frame (Unity axes of the aeroplane) → the config's body frame (x fwd, y right, z down).</summary>
        private static Vec3 ToBody(Vector3 local) => new(local.z, local.x, -local.y);

        // ---- the grab points ----

        private Vector3 Hinge()
        {
            CockpitRigState cockpit = _visual.Cockpit;
            if (_canopyNode != null) return Local(_canopyNode.position);
            return _hingeProxy - Vector3.forward * (float)(cockpit.HandCanopy?.SlideM ?? 0.0);
        }

        /// <summary>The hinge where it is with the canopy shut (the Pitts' slide taken back out).</summary>
        private Vector3 HingeShut()
        {
            CanopyDynamics c = _visual != null ? _visual.Cockpit.HandCanopy : null;
            return Hinge() + Vector3.forward * (float)(c?.SlideM ?? 0.0);
        }

        /// <summary>A proxy fixed to the CANOPY: where it is now, given where it is with the canopy shut (Core
        /// CanopyDynamics.PointOnCanopy: the slide, then the swing about the hinge line). Model nodes ride the rig.</summary>
        private Vector3 OnCanopy(Vector3 shut)
        {
            CanopyDynamics c = _visual != null ? _visual.Cockpit.HandCanopy : null;
            if (c == null || c.Departed) return shut;
            return U(c.PointOnCanopy(C(shut), C(HingeShut())));
        }

        /// <summary>A point fixed to the canopy where it is NOW, taken back to where it is with the canopy shut (the
        /// inverse of <see cref="OnCanopy"/>): a hand measured in the canopy's own frame, for a lever that rides it.</summary>
        private Vec3 OffCanopy(Vec3 now)
        {
            CanopyDynamics c = _visual != null ? _visual.Cockpit.HandCanopy : null;
            if (c == null || c.Departed) return now;
            Vec3 h = C(HingeShut());
            return CanopyDynamics.PointOnCanopy(now + new Vec3(0, 0, c.SlideM), h, 0.0, -c.AngleDeg);
        }

        /// <summary>The jettison knob / release: its node, else (Pitts) the proxy on the canopy.</summary>
        private Vector3 JettisonPoint() => _jettisonNode != null ? Local(_jettisonNode.position) : OnCanopy(_jettisonShut);

        /// <summary>Where the hand closes on the canopy: Codex's rail empty (it rides the rig), else the free edge
        /// abeam the eye, wherever the canopy is.</summary>
        private Vector3 RailPoint()
        {
            if (_railNode != null) return Local(_railNode.position);
            CanopyDynamics c = _visual.Cockpit.HandCanopy;
            Vector3 h = Hinge();
            double b = c.AngleDeg * System.Math.PI / 180.0;
            float w = (float)c.Spec.FreeEdgeM;
            return new Vector3(h.x - w * (float)System.Math.Cos(b), h.y + w * (float)System.Math.Sin(b), _eye.z + 0.20f);
        }

        private Vector3 ReleasePoint(bool main) =>
            main ? (_relMainNode != null ? Local(_relMainNode.position) : _relMainProxy)
                 : (_relLapNode != null ? Local(_relLapNode.position) : _relLapProxy);

        private Vector3 JackPoint() => _jackNode != null ? Local(_jackNode.position) : _jackProxy;

        private void UpdateBailoutPoints()
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            bool on = cockpit != null && cockpit.Harness != null;
            CanopyDynamics c = cockpit?.HandCanopy;
            bool canopy = c != null && !cockpit.CanopyGone && !cockpit.CanopyLocked;
            _grab.Set(Grabbable.CanopyRail, canopy ? C(RailPoint()) : Vec3.Zero, canopy);
            bool jettison = cockpit?.HandJettison != null && !cockpit.CanopyGone && !cockpit.HandJettison.Fired;
            _grab.Set(Grabbable.CanopyJettison, jettison ? C(JettisonPoint()) : Vec3.Zero, jettison);
            int n = on ? cockpit.Harness.Releases.Count : 0;
            _grab.Set(Grabbable.HarnessMain, n > 0 ? C(ReleasePoint(true)) : Vec3.Zero, n > 0 && !cockpit.Harness.Releases[0].Released);
            _grab.Set(Grabbable.HarnessLap, n > 1 ? C(ReleasePoint(false)) : Vec3.Zero, n > 1 && !cockpit.Harness.Releases[1].Released);
            bool plugged = on && cockpit.Cord != null && cockpit.Cord.Plugged;
            _grab.Set(Grabbable.HeadsetPlug, plugged ? C(JackPoint()) : Vec3.Zero, plugged);
            // The sills hold still while the body rises past them (the hands are measured from the risen seat).
            _grab.Set(Grabbable.SillLeft, on ? C(_sillL) : Vec3.Zero, on);
            _grab.Set(Grabbable.SillRight, on ? C(_sillR) : Vec3.Zero, on);
            _grab.Set(Grabbable.Ripcord, Vec3.Zero, false);
        }

        private Vector3 HeldBailoutPoint(Grabbable g, HandPose h)
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            switch (g)
            {
                case Grabbable.CanopyRail when cockpit?.HandCanopy != null: return RailPoint();
                case Grabbable.CanopyJettison:
                {
                    // Drawn on the knob, pulled out along its axis toward the pilot.
                    Vector3 k = JettisonPoint();
                    double out01 = cockpit?.HandJettison?.PositionM ?? 0.0;
                    return k + (_eye - k).normalized * (float)out01;
                }
                case Grabbable.HarnessMain: return ReleasePoint(true);
                case Grabbable.HarnessLap: return ReleasePoint(false);
                case Grabbable.HeadsetPlug: return JackPoint() + (U(h.Position) - JackPoint()) * 0.5f;
                case Grabbable.SillLeft: return _sillL;
                case Grabbable.SillRight: return _sillR;
                default: return U(h.Position);
            }
        }

        private void GrabBailout(HandSide side, Grabbable g, HandPose h)
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            if (cockpit == null) return;
            switch (g)
            {
                case Grabbable.CanopyRail: break;   // DriveBailout hands the canopy to whichever hand holds it
                case Grabbable.CanopyJettison:
                    // Pulled toward the pilot (the knob's axis points at him: Codex's Extra pulls are +inboard).
                    cockpit.HandJettison?.Grab(h.Position, C((_eye - JettisonPoint()).normalized));
                    break;
                case Grabbable.HarnessMain: cockpit.Harness.Releases[0].Grab(h.Position, h.Rotation); break;
                case Grabbable.HarnessLap: cockpit.Harness.Releases[1].Grab(h.Position, h.Rotation); break;
                case Grabbable.HeadsetPlug:
                {
                    // Out of the jack = away from the panel, toward the pilot (inboard and up).
                    Vector3 axis = _eye - JackPoint();
                    cockpit.Cord.Grab(h.Position, C(axis.normalized));
                    break;
                }
                case Grabbable.SillLeft:
                case Grabbable.SillRight:
                    cockpit.Climb.Grab(Raw(h.Position), g == Grabbable.SillLeft ? ExitSide.Left : ExitSide.Right);
                    _blockedTicked = false;
                    break;
            }
        }

        /// <summary>A hand's position in the seat's own frame, without the climb's rise (the sill is fixed to the seat).</summary>
        private Vec3 Raw(Vec3 p) => p - new Vec3(0, _rise, 0);

        // ---- the cockpit, every frame ----

        private void DriveBailout()
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            if (cockpit == null || cockpit.Harness == null) { _prevAirPos = _air.position; _prevAirRot = _air.rotation; return; }
            float dt = Time.deltaTime;
            Aircraft ac = _driver.Sim.Aircraft;
            double ias = _driver.IasMs;
            double q = 0.5 * 1.225 * ias * ias;   // IAS is equivalent airspeed here: q = ½ρ₀V_e²
            var gApp = new Vec3(0, -ac.LoadFactorZ * 9.80665, 0);

            // The jettison knob / emergency release: pulled through its breakout to its firing point, it pulls the hinge
            // pins (one-way). The canopy then goes the moment its latch is open too.
            CanopyDynamics c = cockpit.HandCanopy;
            JettisonPull jp = cockpit.HandJettison;
            if (jp != null && c != null && !cockpit.CanopyGone)
            {
                if (_grab.IsHeldBy(Grabbable.CanopyJettison, out HandSide js))
                {
                    if (jp.Follow(_hands[(int)js].Position))
                    {
                        c.ReleaseHingePins();
                        XrHandInput.Tick(js == HandSide.Left, 1f, 0.06f);
                        Debug.Log($"[VrEgress] canopy JETTISON pulled ({jp.TravelM * 1000:F0} mm): hinge pins out{(cockpit.CanopyLocked ? " — the latch still holds it: open the latch and it goes" : "")}");
                        _grab.Drop(js);
                    }
                }
                else if (jp.Held) jp.Release();
            }

            // The canopy in the air, held by whichever hand has the rail or (Extra) its latch handles, which are on the
            // frame: squeeze and push up is one movement, and the airload can tear it out of that hand.
            if (c != null && !cockpit.CanopyGone)
            {
                int holder = -1;
                if (_grab.IsHeldBy(Grabbable.CanopyRail, out HandSide cs)) holder = (int)cs;
                else if (_latchKind == CanopyLatchKind.Squeeze &&
                         (_grab.IsHeldBy(Grabbable.CanopyLatch, out cs) || _grab.IsHeldBy(Grabbable.CanopyLatchFwd, out cs) ||
                          _grab.IsHeldBy(Grabbable.CanopyLatchAft, out cs))) holder = (int)cs;
                if (holder != _canopyHolder)
                {
                    if (c.Held) c.Release();
                    _canopyHolder = holder;
                    if (holder >= 0) c.Grab(_hands[holder].Position, C(Hinge()));
                }
                Vec3 hand = holder >= 0 ? _hands[holder].Position : Vec3.Zero;
                CanopyEvents ev = c.Step(dt, q, gApp, cockpit.CanopyLocked, hand, C(Hinge()));
                if ((ev & CanopyEvents.TornFromHand) != 0 && holder >= 0)
                {
                    XrHandInput.Tick(holder == 0, 1f, 0.12f);
                    _grab.Drop((HandSide)holder);
                    _canopyHolder = -1;
                    Debug.Log($"[VrEgress] canopy torn out of the {(HandSide)holder} hand at {ias * 1.9438445:F0} KIAS (q {q:F0} Pa): the airload beat {CanopyDynamics.HandForceN:F0} N at the edge");
                }
                if ((ev & CanopyEvents.FlippedOpen) != 0)
                    Debug.Log($"[VrEgress] canopy flung open by the suction at {ias * 1.9438445:F0} KIAS; drag +{c.DragAreaM2:F2} m² into the sim");
                if ((ev & CanopyEvents.HitStop) != 0)
                {
                    XrHandInput.Tick(true, 0.5f, 0.05f); XrHandInput.Tick(false, 0.5f, 0.05f);
                    if (!_slamLogged) Debug.Log($"[VrEgress] canopy slammed onto its stop: {c.PeakFittingLoadN:F0} N on the fittings (they hold {c.Spec.FittingStrengthN:F0})");
                    _slamLogged = true;
                }
                if (c.AngleDeg < 50.0) _slamLogged = false;   // shut again: the next slam is news
                if ((ev & CanopyEvents.Departed) != 0) DepartCanopy(c, ias);
            }

            // The belts.
            for (int i = 0; i < cockpit.Harness.Releases.Count && i < 2; i++)
            {
                HarnessRelease r = cockpit.Harness.Releases[i];
                Grabbable slot = i == 0 ? Grabbable.HarnessMain : Grabbable.HarnessLap;
                if (_grab.IsHeldBy(slot, out HandSide hs))
                {
                    if (r.Follow(_hands[(int)hs].Position, _hands[(int)hs].Rotation))
                    {
                        XrHandInput.Tick(hs == HandSide.Left, 0.8f, 0.03f);
                        Debug.Log($"[VrEgress] harness {r.Name} RELEASED ({r.Kind}){(cockpit.Harness.Latched ? " — still held by the other belt" : " — free of the seat")}");
                    }
                }
                else if (r.Held) r.Release();
            }

            // The plug, by hand.
            HeadsetCord cord = cockpit.Cord;
            if (cord == null) { }
            else if (_grab.IsHeldBy(Grabbable.HeadsetPlug, out HandSide ps))
            {
                if (cord.Follow(_hands[(int)ps].Position))
                {
                    XrHandInput.Tick(ps == HandSide.Left, 0.6f, 0.03f);
                    Debug.Log("[VrEgress] headset unplugged by hand");
                }
            }
            else if (cord.Held) cord.Release();

            // The climb.
            ClimbOut climb = cockpit.Climb;
            bool clear = cockpit.CanopyGone || _openCockpit || (c != null && c.ClearToLeave);
            bool onSill = _grab.IsHeldBy(Grabbable.SillLeft, out HandSide ls) || _grab.IsHeldBy(Grabbable.SillRight, out ls);
            bool leave = false;
            if (onSill && climb.Held)
            {
                ClimbEvents ev = climb.Step(Raw(_hands[(int)ls].Position), cockpit.Harness.Latched, clear, q);
                if ((ev & ClimbEvents.Blocked) != 0 && !_blockedTicked)
                {
                    _blockedTicked = true;
                    XrHandInput.Tick(ls == HandSide.Left, 0.5f, 0.06f);
                    Debug.Log($"[VrEgress] can't rise: {(cockpit.Harness.Latched ? "still strapped in" : "the canopy is in the way")}");
                }
                if ((ev & ClimbEvents.Exposed) != 0) Debug.Log($"[VrEgress] shoulders in the slipstream ({ClimbOut.SlipstreamN(q, climb.RiseM):F0} N on him)");
                leave = climb.Out;
            }
            else if (climb.Held) leave = climb.Release();
            _rise = (float)climb.RiseM;

            StepCord(cord, dt, inCockpit: true);
            if (leave) Leave(climb);
            _prevAirPos = _air.position; _prevAirRot = _air.rotation;
        }

        /// <summary>
        /// THE CANOPY HAS GONE (torn off its hinges, or jettisoned): it leaves as a body of its own (AirframeVisual.
        /// DetachCanopy → CanopyFlight) with the aeroplane's velocity at its centre (rotation included, finite-differenced
        /// like the climb-out) plus its own swing; the aeroplane loses its mass and its CG moves; the open cockpit's
        /// drag and wind take over (CockpitRigState.Fill); and its path past the tail is predicted and a strike LOGGED
        /// (a warning; nothing is damaged).
        /// </summary>
        private void DepartCanopy(CanopyDynamics c, double ias)
        {
            CockpitRigState cockpit = _visual.Cockpit;
            Aircraft ac = _driver.Sim.Aircraft;
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            Vector3 comLocal = U(c.ComLocal(C(HingeShut())));
            Vector3 vPoint = (World(comLocal) - (_prevAirPos + _prevAirRot * comLocal)) / dt;
            if (vPoint.sqrMagnitude > 4f * _driver.WorldVelocityUnity.sqrMagnitude + 100f) vPoint = _driver.WorldVelocityUnity;   // a reset between frames
            Vector3 rel = U(c.ComVelocityLocal());
            Vector3 vel = vPoint + _air.rotation * rel;
            Vector3 up = _air.rotation * U(CanopyDynamics.Normal(c.AngleDeg));
            // The swing about the hinge line (fore-aft; + β is left edge up, over to the right) and the pitch-over.
            Vector3 spin = -_air.forward * (float)(c.RateDps * Mathf.Deg2Rad) + _air.right * (float)CanopyFreeBody.PitchOverRadS;
            bool torn = c.DepartedBy == CanopyDeparture.HingesFailed;
            Vector3? centre = _visual.DetachCanopy(c, vel, up, spin, torn);
            cockpit.CanopyGone = true;
            if (centre.HasValue) comLocal = Local(centre.Value);
            Vec3 comBody = ac.Config.Mass.CgVec() + ToBody(comLocal);
            ac.ShedMass(c.Spec.MassKg, comBody);
            _air.GetComponent<FlightAudio>()?.CanopyJettison();
            XrHandInput.Tick(true, 1f, 0.2f); XrHandInput.Tick(false, 1f, 0.2f);
            _grab.Set(Grabbable.CanopyRail, Vec3.Zero, false);
            _canopyHolder = -1;

            CanopyTailCheck.Result tail = CanopyTailCheck.Predict(c, comBody, ToBody(rel), ToBody(U(CanopyDynamics.Normal(c.AngleDeg))),
                                                                  ac.State.Velocity.Length, EgressAir.Density(_air.position.y), _clear);
            Debug.Log($"[VrEgress] canopy {(torn ? $"TORN OFF ITS HINGES at {ias * 1.9438445:F0} KIAS ({c.PeakFittingLoadN:F0} N on fittings rated {c.Spec.FittingStrengthN:F0})" : $"JETTISONED at {ias * 1.9438445:F0} KIAS")}: " +
                      $"{c.Spec.MassKg:F0} kg gone, CG moved ({ac.CgShiftBody.X * 1000:F0}, {ac.CgShiftBody.Y * 1000:F0}, {ac.CgShiftBody.Z * 1000:F0}) mm; open-cockpit drag {c.DragAreaM2:F2} m²");
            if (tail.Surface != null)
                Debug.LogWarning($"[VrEgress] the departing canopy would pass through the {tail.Surface} {tail.TimeSec:F2} s after leaving (predicted; no strike is modelled)");
            else
                Debug.Log($"[VrEgress] the canopy clears the tail: {tail.AtTail.Y:F1} m right, {-tail.AtTail.Z:F1} m up at the tail station");
        }

        /// <summary>Over the side: hand the pilot to PilotEgress as a body with the aeroplane's velocity at that point
        /// (finite-differenced from the aeroplane's pose, so its rotation is in it) plus his push.</summary>
        private void Leave(ClimbOut climb)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            // His eye where it is now (risen); his centre 0.30 m below it, so the view does not jump as he goes (the
            // jumper's eye is 0.30 m above his centre in free fall). The push carries him outboard from there.
            Vector3 local = _eye + Vector3.up * _rise;
            Vector3 eyeNow = World(local);
            Vector3 now = eyeNow - Vector3.up * 0.30f;
            Vector3 before = _prevAirPos + _prevAirRot * local;
            Vector3 vPoint = (eyeNow - before) / dt;
            if (vPoint.sqrMagnitude > 4f * _driver.WorldVelocityUnity.sqrMagnitude + 100f) vPoint = _driver.WorldVelocityUnity;   // a reset between frames
            Vec3 v = JumperAero.ExitVelocity(C(vPoint), Vec3.Zero, Vec3.Zero, C(_air.right), C(_air.up), climb.Side);
            float yaw = _air.eulerAngles.y;
            if (_egress == null) return;
            Debug.Log($"[VrEgress] OUT to the {climb.Side}{(climb.PulledOut ? ", pulled off the sill by the slipstream" : "")} at {_driver.IasMs * 1.9438445:F0} KIAS; " +
                      $"exit velocity = aircraft {vPoint.magnitude:F1} m/s + push {JumperAero.PushOutMs:F1} out / {JumperAero.PushUpMs:F1} up");
            _egress.LeaveByHand(now, U(v), yaw);
            BeginJumper(eyeNow, yaw);
        }

        /// <summary>The one-press bail-out (PilotEgress.BailOut, from the menu / glass / keyboard) put him over the side with
        /// a headset on: the head goes with the body, exactly as after a climb-out by hand. <paramref name="body"/> is the
        /// jumper's centre (world).</summary>
        public void FollowJump(Vector3 body, float yawDeg)
        {
            Debug.Log($"[VrEgress] OUT by the bail-out button at {(_driver != null ? _driver.IasMs * 1.9438445 : 0):F0} KIAS: first person, the ripcord on the left chest strap (the chute also opens by itself {PilotEgress.BailChuteSec:F0} s after leaving)");
            BeginJumper(body + Vector3.up * 0.30f, yawDeg);
        }

        private void BeginJumper(Vector3 eyeNow, float yaw)
        {
            // First person from here: level, on the heading he left on (a one-time snap of any bank: see docs, comfort).
            _jumper = true;
            _jRot = Quaternion.Euler(0f, yaw, 0f);
            _jOrigin = eyeNow;
            _jT = 0f; _shockHapticT = 0f; _landedFelt = false;
            _struck.Clear();
            _ripcord.Reset();
            _rise = 0f;
            for (int i = 1; i < CockpitGrab.Slots; i++) _grab.Set((Grabbable)i, Vec3.Zero, false);
            _grab.ReleaseAll();
            XrHandInput.Tick(true, 0.7f, 0.15f); XrHandInput.Tick(false, 0.7f, 0.15f);
        }

        // ---- the cord, in and out of the cockpit ----

        /// <summary>The headset's cord root: Codex's Headset_Cable_Root when the gear asset is worn, else the bottom of the
        /// LEFT ear cup (E: most GA headsets' cords leave the left side), from the tracked head this frame.</summary>
        private Vector3 HeadCableRoot()
        {
            Vector3 head; Quaternion hq;
            if (!HeadWorld(out head, out hq)) { head = transform.position; hq = transform.rotation; }
            return head + hq * new Vector3(-0.085f, -0.07f, -0.02f);
        }

        /// <summary>The tracked head this frame, in the world (not the camera, which is placed after this runs).</summary>
        private bool HeadWorld(out Vector3 pos, out Quaternion rot)
        {
            pos = transform.position; rot = transform.rotation;
            if (_rig == null || !XrControllers.TryHead(out Vector3 p, out Quaternion q)) return false;
            if (!_rig.TrackingToSeat(p, q, out Vector3 sp, out Quaternion sq)) return false;
            if (_jumper) { pos = _jOrigin + _yank + _jRot * sp; rot = _jRot * sq; return true; }
            if (_air == null) return false;
            pos = World(_eye + Vector3.up * _rise + sp);
            rot = _air.rotation * sq;
            return true;
        }

        private Vector3 JackWorld() => _air != null ? World(JackPoint()) : Vector3.zero;

        private void StepCord(HeadsetCord cord, float dt, bool inCockpit)
        {
            if (_ropeLine == null) return;
            if (cord == null) { if (_ropeLine.enabled) { _ropeLine.enabled = false; _plug.SetActive(false); } return; }
            // The view's tug relaxes (translation only).
            _yank *= Mathf.Exp(-dt / (float)HeadsetCord.YankTauSec);
            if (cord.State == CordState.HeadsetOff) { _ropeLine.enabled = false; _plug.SetActive(false); return; }

            Vector3 root = HeadCableRoot(), jack = JackWorld();
            CordEvents ev = cord.Step(Vector3.Distance(root, jack));
            if ((ev & CordEvents.Taut) != 0 && !_taughtLogged) { _taughtLogged = true; Debug.Log($"[VrEgress] headset cord taut ({cord.LengthM:F1} m)"); }
            if ((ev & (CordEvents.PulledOut | CordEvents.HeadsetPulledOff)) != 0)
            {
                // The tug on his head, along the cord toward the aeroplane, bounded; and a strong buzz in both hands.
                Vector3 dir = (jack - root).normalized;
                _yank = dir * (float)HeadsetCord.YankM(cord.PeakTensionN);
                XrHandInput.Tick(true, 1f, 0.15f); XrHandInput.Tick(false, 1f, 0.15f);
                Debug.Log((ev & CordEvents.PulledOut) != 0
                    ? $"[VrEgress] still plugged in: the cord went taut and the aeroplane pulled the plug out at {cord.PeakTensionN:F0} N (head tugged {_yank.magnitude * 100f:F1} cm)"
                    : $"[VrEgress] still plugged in: the cord held and pulled the HEADSET off at {cord.PeakTensionN:F0} N");
                if (cord.State == CordState.HeadsetOff) { _ropeLine.enabled = false; _plug.SetActive(false); return; }
            }

            // The rope, head-relative in world axes: it hangs (cockpit) or streams up and away from the fall (out).
            Vector3 g = inCockpit ? Physics.gravity : StreamDir() * 9.81f;
            Vec3 a = Vec3.Zero, b = C(jack - root);
            _rope.Step(System.Math.Min(dt, 1.0 / 30.0), C(g), a, b, cord.Plugged);
            for (int i = 0; i < _rope.Points.Length; i++) _ropeDraw[i] = root + U(_rope.Points[i]);
            _ropeLine.enabled = true;
            _ropeLine.SetPositions(_ropeDraw);
            Vector3 end = _ropeDraw[_ropeDraw.Length - 1], prev = _ropeDraw[_ropeDraw.Length - 2];
            _plug.SetActive(true);
            _plug.transform.SetPositionAndRotation(end, Quaternion.FromToRotation(Vector3.up, end - prev));
        }

        /// <summary>Out of the aeroplane the cord streams downwind of him: opposite his velocity through the air.</summary>
        private Vector3 StreamDir()
        {
            Vector3 v = _egress != null ? _egress.PilotVelocity : Vector3.zero;
            return v.sqrMagnitude > 1f ? -v.normalized : Vector3.down;
        }

        // ---- the jumper ----

        private void TickJumper()
        {
            if (!_jumper)
            {
                // Outside views, the menu's bail-out, no headset: no cord or belts drawn.
                if (_ropeLine != null && _ropeLine.enabled) { _ropeLine.enabled = false; _plug.SetActive(false); }
                if (_props != null && _props.activeSelf) _props.SetActive(false);
                return;
            }
            if (_egress == null || !_egress.HandsOn || !_egress.PilotOut || _egress.PilotTransform == null || !XrHeadset.Active)
            {
                EndJumper();
                return;
            }
            float dt = Time.deltaTime;
            _jT += dt;
            Vector3 body = _egress.PilotTransform.position;
            float upright = _egress.PilotLanded ? 1f : _egress.ChuteInflate01;
            // The eye above his centre: 0.30 m belly-down (the head is forward of the centre, raised to see), 0.75 m
            // hanging or standing (E).
            _jOrigin = body + Vector3.up * Mathf.Lerp(0.30f, 0.75f, upright);

            // The near plane: ChaseCamera set the outside one; his own hands are centimetres away.
            var cam = GetComponent<Camera>();
            if (cam != null && cam.nearClipPlane > 0.05f) cam.nearClipPlane = 0.05f;

            ReadJumperHands();
            bool blocked = XrHeadset.MenuOpen;
            Vector3 ringLocal = RipcordLocal();
            _grab.Set(Grabbable.Ripcord, C(ringLocal), !_ripcord.Pulled && !_egress.ChuteOut);
            for (int s = 0; s < 2; s++)
            {
                GrabEvents ev = _grab.Step((HandSide)s, _jHands[s], blocked, HandDt);
                if ((ev & GrabEvents.HoverEnter) != 0) XrHandInput.Tick(s == 0, 0.15f, 0.015f);
                if ((ev & GrabEvents.Grabbed) != 0 && _grab.Held((HandSide)s) == Grabbable.Ripcord)
                {
                    XrHandInput.Tick(s == 0, 0.45f, 0.035f);
                    _ripcord.Grab(_jHands[s].Position);
                }
            }
            if (_grab.IsHeldBy(Grabbable.Ripcord, out HandSide rs))
            {
                if (_ripcord.Follow(_jHands[(int)rs].Position))
                {
                    XrHandInput.Tick(rs == HandSide.Left, 0.9f, 0.08f);
                    bool ok = _egress.PullRipcord();
                    Debug.Log($"[VrEgress] RIPCORD pulled ({Ripcord.PullM * 100:F0} cm down){(ok ? $" at {_egress.PilotVelocity.magnitude:F0} m/s, {_jT:F1} s after leaving" : " — nothing happened")}");
                }
            }
            else if (_ripcord.Held) _ripcord.Release();

            // The opening shock and the canopy in the hands' buzz: amplitude with the load, a pulse every 50 ms.
            if (_egress.ChuteOut && !_egress.PilotLanded && (_shockHapticT -= dt) <= 0f)
            {
                float g = _egress.PilotLoadG;
                if (g > 1.3f) { float a = Mathf.Clamp01((g - 1f) / 7f); XrHandInput.Tick(true, a, 0.05f); XrHandInput.Tick(false, a, 0.05f); }
                _shockHapticT = 0.05f;
            }
            if (_egress.PilotLanded && !_landedFelt)
            {
                _landedFelt = true;
                XrHandInput.Tick(true, 1f, 0.2f); XrHandInput.Tick(false, 1f, 0.2f);
            }

            // Did he hit the tail or a wing? Checked in the aeroplane's own frame while he is near it.
            if (_clear != null && _air != null && _jT < 8f)
            {
                Vector3 l = Quaternion.Inverse(_air.rotation) * (body - _air.position);
                string hit = _clear.Hit(_driver.Sim.Aircraft.Config.Mass.CgVec() + ToBody(l));
                if (hit != null && _struck.Add(hit))
                {
                    float rel = (_egress.PilotVelocity - _driver.WorldVelocityUnity).magnitude;
                    XrHandInput.Tick(true, 1f, 0.25f); XrHandInput.Tick(false, 1f, 0.25f);
                    Debug.Log($"[VrEgress] STRUCK the {hit} {_jT:F2} s after leaving, {rel:F0} m/s relative to the aeroplane (reported only: no injury model yet)");
                }
            }

            StepCord(_visual != null ? _visual.Cockpit.Cord : null, dt, inCockpit: false);
            PlaceGear();
        }

        private void EndJumper()
        {
            _jumper = false;
            _yank = Vector3.zero;
            _grab.ReleaseAll();
            _body.SetVisible(false);
            if (_ropeLine != null) _ropeLine.enabled = false;
            if (_props != null) _props.SetActive(false);
            if (_gear != null) _gear.SetActive(false);
        }

        private void ReadJumperHands()
        {
            QuestControllerFrame f = XrControllers.Read(out bool any);
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                _jHands[s] = HandPose.None;
                if (!any || f.HandsOnly) continue;
                if (!XrHandInput.TryGripPose(left, out Vector3 p, out Quaternion q)) continue;
                if (!_rig.TrackingToSeat(p, q, out Vector3 sp, out Quaternion sq)) continue;
                _jHands[s] = new HandPose
                {
                    Source = HandSource.Controller,
                    Position = C(sp),                       // the jumper's frame: from his eye, his heading
                    Rotation = new Quat(sq.x, sq.y, sq.z, sq.w),
                    Grip = left ? f.LeftGrip : f.RightGrip,
                    Pinch = left ? f.LeftTrigger : f.RightTrigger,
                };
            }
        }

        /// <summary>The handle in the jumper's frame: Codex's Grip_Ripcord on the worn gear, else the proxy; pulled, it
        /// comes away in the hand.</summary>
        private Vector3 RipcordLocal()
        {
            if (_gearRipcord != null && _gear != null && _gear.activeSelf)
                return Quaternion.Inverse(_jRot) * (_gearRipcord.position - (_jOrigin + _yank));
            return RipcordAtEye;
        }

        private Vector3 J(Vector3 local) => _jOrigin + _yank + _jRot * local;

        /// <summary>His body, out of the aeroplane: shoulders and hips from his eye as in the seat, the hands at the
        /// controllers (or on the handle), the legs hanging a little forward. Nothing here turns the view.</summary>
        private void PoseJumper()
        {
            // Fly by hand switched off after he climbed out: he stays the jumper (the view is on him), undrawn.
            if (!XrHeadset.FlyByHand) { _body.SetVisible(false); return; }
            _body.SetVisible(true);
            Vector3 shL = U(SeatedPilot.ShoulderL), shR = U(SeatedPilot.ShoulderR);
            Vector3 hipL = U(SeatedPilot.HipL), hipR = U(SeatedPilot.HipR);
            Vector3 ballL = new(-0.13f, -1.48f, 0.12f), ballR = new(0.13f, -1.48f, 0.12f);
            Vector3 heelL = _body.HeelFromBall(true), heelR = _body.HeelFromBall(false);
            _body.PlaceRoot(J(0.5f * (hipL + hipR)), _jRot);
            _pose.Up = _jRot * Vector3.up;
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                HandPose h = _jHands[s];
                bool onRing = _grab.Held((HandSide)s) == Grabbable.Ripcord;
                Vector3 grip = onRing ? U(h.Position) : h.Tracked ? U(h.Position) : (left ? new Vector3(-0.30f, -0.45f, 0.25f) : new Vector3(0.30f, -0.45f, 0.25f));
                Quaternion rot = h.Tracked ? _jRot * new Quaternion((float)h.Rotation.X, (float)h.Rotation.Y, (float)h.Rotation.Z, (float)h.Rotation.W) : _jRot;
                float curl = onRing ? 1f : h.Tracked ? (float)h.Grip : 0.3f;
                Arm(left, J(left ? shL : shR), J(grip), rot, curl, J(U(left ? SeatedPilot.ElbowPoleL : SeatedPilot.ElbowPoleR)));
            }
            Leg(true, J(hipL), J(ballL), J(ballL + heelL), J(new Vector3(-0.15f, -0.9f, 0.6f)));
            Leg(false, J(hipR), J(ballR), J(ballR + heelR), J(new Vector3(0.15f, -0.9f, 0.6f)));
            _body.Apply(_pose);
            PlaceProps();
        }

        // ---- the stand-ins (primitive, until Codex's gear and harness meshes are in the build) ----

        private void EnsureProps()
        {
            if (_props != null) return;
            _props = new GameObject("VR Bail-out Props");
            Material webbing = EgressAir.Unlit(new Color(0.12f, 0.16f, 0.30f));
            Material metal = EgressAir.Unlit(new Color(0.75f, 0.75f, 0.78f));
            Material red = EgressAir.Unlit(new Color(0.80f, 0.08f, 0.06f));
            _cordMat = EgressAir.Unlit(new Color(0.08f, 0.08f, 0.08f));
            _fitMain = EgressAir.Prim(PrimitiveType.Cylinder, "Release (main)", _props.transform, Vector3.zero, new Vector3(0.06f, 0.008f, 0.06f), metal);
            _fitLap = EgressAir.Prim(PrimitiveType.Cube, "Release (lap)", _props.transform, Vector3.zero, new Vector3(0.07f, 0.015f, 0.04f), metal);
            _lapBelt = EgressAir.Prim(PrimitiveType.Cube, "Lap belt", _props.transform, Vector3.zero, Vector3.one, webbing);
            _strapL = EgressAir.Prim(PrimitiveType.Cube, "Shoulder strap L", _props.transform, Vector3.zero, Vector3.one, webbing);
            _strapR = EgressAir.Prim(PrimitiveType.Cube, "Shoulder strap R", _props.transform, Vector3.zero, Vector3.one, webbing);
            _dRing = EgressAir.Prim(PrimitiveType.Cube, "Ripcord handle", _props.transform, Vector3.zero, new Vector3(0.07f, 0.035f, 0.02f), red);
            _plug = EgressAir.Prim(PrimitiveType.Cylinder, "Headset plug", null, Vector3.zero, new Vector3(0.012f, 0.03f, 0.012f), metal);
            _props.SetActive(false);
            var go = new GameObject("Headset cord");
            _ropeLine = go.AddComponent<LineRenderer>();
            _ropeLine.useWorldSpace = true;
            _ropeLine.widthMultiplier = (float)HeadsetCord.DiameterM;
            _ropeLine.sharedMaterial = _cordMat;
            _ropeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ropeLine.receiveShadows = false;
            _rope = new VerletRope(20, HeadsetCord.DefaultLengthM, Vec3.Zero, new Vec3(0, -1, 0));
            _ropeDraw = new Vector3[_rope.Points.Length];
            _ropeLine.positionCount = _ropeDraw.Length;
            _ropeLine.enabled = false;
            _plug.SetActive(false);
        }

        private static void Strap(GameObject s, Vector3 a, Vector3 b, Quaternion frame, float width)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) { s.SetActive(false); return; }
            s.SetActive(true);
            s.transform.SetPositionAndRotation(0.5f * (a + b), Quaternion.LookRotation(d / len, frame * Vector3.forward));
            s.transform.localScale = new Vector3(width, 0.004f, len);
        }

        /// <summary>Called from LateUpdate's body pass: the belts in the cockpit (unless the model has Harness_*), the
        /// D-ring out of it (unless the gear asset has one). Straps that are released are not drawn: they have fallen open.</summary>
        private void PlaceProps()
        {
            if (_props == null) return;
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            if (_jumper)
            {
                _props.SetActive(true);
                _fitMain.SetActive(false); _fitLap.SetActive(false); _lapBelt.SetActive(false); _strapL.SetActive(false); _strapR.SetActive(false);
                bool gearRing = _gearRipcord != null && _gear != null && _gear.activeSelf;
                bool inHand = _grab.IsHeldBy(Grabbable.Ripcord, out HandSide rs);
                _dRing.SetActive(!gearRing && !_ripcord.Pulled);
                Vector3 at = inHand ? U(_jHands[(int)rs].Position) : RipcordAtEye;
                _dRing.transform.SetPositionAndRotation(J(at), _jRot);
                return;
            }
            if (!Active || cockpit?.Harness == null || _modelHarness) { _props.SetActive(false); return; }
            _props.SetActive(true);
            _dRing.SetActive(false);
            Quaternion r = _air.rotation;
            Vector3 lift = Vector3.up * _rise;
            int n = cockpit.Harness.Releases.Count;
            bool mainOn = n > 0 && !cockpit.Harness.Releases[0].Released, lapOn = n > 1 && !cockpit.Harness.Releases[1].Released;
            Vector3 main = ReleasePoint(true), lap = n > 1 ? ReleasePoint(false) : main;
            _fitMain.SetActive(_relMainNode == null && n > 0);
            _fitMain.transform.SetPositionAndRotation(World(main), r * Quaternion.Euler(90f, 0f, 0f));
            _fitLap.SetActive(_relLapNode == null && n > 1);
            _fitLap.transform.SetPositionAndRotation(World(lap), r);
            Vector3 hipL = _eye + lift + U(SeatedPilot.HipL) + new Vector3(-0.10f, 0.08f, 0.12f), hipR = _eye + lift + U(SeatedPilot.HipR) + new Vector3(0.10f, 0.08f, 0.12f);
            Vector3 shL = _eye + lift + U(SeatedPilot.ShoulderL) + new Vector3(0.03f, 0.06f, 0.06f), shR = _eye + lift + U(SeatedPilot.ShoulderR) + new Vector3(-0.03f, 0.06f, 0.06f);
            bool lapHeld = n > 1 ? lapOn : mainOn;
            if (lapHeld) { Strap(_lapBelt, World(hipL), World(hipR), r, 0.045f); } else _lapBelt.SetActive(false);
            if (mainOn) { Strap(_strapL, World(shL), World(main), r, 0.045f); Strap(_strapR, World(shR), World(main), r, 0.045f); }
            else { _strapL.SetActive(false); _strapR.SetActive(false); }
        }

        /// <summary>Codex's pilot-gear asset, worn by the jumper: container on his back, the ripcord, and the deployed
        /// canopy driven by the same opening model ("Inflate" 0..1). Absent: the stand-ins and the primitive canopy.</summary>
        private void PlaceGear()
        {
            if (!_gearTried)
            {
                _gearTried = true;
                var prefab = Resources.Load<GameObject>(GearAssetPath);
                if (prefab != null)
                {
                    _gear = Object.Instantiate(prefab);
                    _gear.name = "VR Pilot Gear";
                    foreach (Transform t in _gear.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Parachute_Deployed") _gearDeployed = t;
                        else if (t.name == "Grip_Ripcord") _gearRipcord = t;
                        else if (t.name == "Headset") t.gameObject.SetActive(false);   // the camera is the head
                        else if (t.name == "Headset_Plug") t.gameObject.SetActive(false);   // no cord (HeadsetCordOn), so no plug either
                        else if (t.name == "Parachute_Canopy") _gearCanopy = t.GetComponent<SkinnedMeshRenderer>();
                    }
                    if (_gearCanopy != null && _gearCanopy.sharedMesh != null) _inflateIdx = _gearCanopy.sharedMesh.GetBlendShapeIndex("Inflate");
                    foreach (var c in _gear.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                    Debug.Log($"[VrEgress] pilot gear {GearAssetPath}: ripcord {(_gearRipcord != null ? "Grip_Ripcord" : "proxy")}, canopy {(_gearDeployed != null ? "Parachute_Deployed" : "primitive")}{(_gearCanopy != null ? _inflateIdx >= 0 ? " with Inflate" : " (no Inflate shape key)" : "")}");
                }
                else Debug.Log($"[VrEgress] pilot gear {GearAssetPath}.glb not in the build — stand-in D-ring and the primitive canopy");
            }
            if (_gear == null) return;
            _gear.SetActive(true);
            // Worn: the asset's root at his hips, facing his heading (it is authored in the pilot body's frame).
            _gear.transform.SetPositionAndRotation(J(0.5f * (U(SeatedPilot.HipL) + U(SeatedPilot.HipR))), _jRot);
            bool deployed = _egress.ChuteOut;
            if (_gearDeployed != null)
            {
                _gearDeployed.gameObject.SetActive(deployed);
                _egress.SetChuteVisualVisible(!deployed);
                if (_gearCanopy != null && _inflateIdx >= 0) _gearCanopy.SetBlendShapeWeight(_inflateIdx, 100f * _egress.ChuteInflate01);
            }
        }

        // ---- the canopy's proxy hardware, drawn only where the model has no node yet ----

        private GameObject _canopyProps, _knobProxy, _handleAftProxy, _handleFwdProxy;

        /// <summary>A red knob for the Pitts' jettison and the Extra's two red handles, where Codex's nodes are missing, so
        /// the pilot can see what he is reaching for (sizes: Codex's 14 × 55 × 27 mm handle grips; a 25 mm knob, E). They
        /// ride the canopy like the grab points they mark. Hidden outside the cockpit and once the canopy has gone.</summary>
        private void PlaceCanopyProps()
        {
            CockpitRigState cockpit = _visual != null ? _visual.Cockpit : null;
            bool on = Active && !_jumper && cockpit != null && !cockpit.CanopyGone && cockpit.HandCanopy != null;
            bool knob = on && cockpit.HandJettison != null && _jettisonNode == null;
            bool handles = on && _latchKind == CanopyLatchKind.Squeeze && cockpit.HandLatch != null;
            if (!knob && !handles) { if (_canopyProps != null) _canopyProps.SetActive(false); return; }
            if (_canopyProps == null)
            {
                _canopyProps = new GameObject("VR Canopy Props");
                Material red = EgressAir.Unlit(new Color(0.80f, 0.08f, 0.06f));
                _knobProxy = EgressAir.Prim(PrimitiveType.Sphere, "Jettison knob (proxy)", _canopyProps.transform, Vector3.zero, Vector3.one * 0.025f, red);
                _handleAftProxy = EgressAir.Prim(PrimitiveType.Cube, "Latch handle aft (proxy)", _canopyProps.transform, Vector3.zero, new Vector3(0.014f, 0.055f, 0.027f), red);
                _handleFwdProxy = EgressAir.Prim(PrimitiveType.Cube, "Latch handle fwd (proxy)", _canopyProps.transform, Vector3.zero, new Vector3(0.014f, 0.055f, 0.027f), red);
            }
            _canopyProps.SetActive(true);
            // The canopy's own turn: about the fore-aft hinge line, the left edge going up and over to the right.
            Quaternion r = _air.rotation * Quaternion.Euler(0f, 0f, -(float)cockpit.HandCanopy.AngleDeg);
            _knobProxy.SetActive(knob);
            if (knob)
            {
                Vector3 k = JettisonPoint();
                double pulled = cockpit.HandJettison.PositionM;
                _knobProxy.transform.SetPositionAndRotation(World(k + (_eye - k).normalized * (float)pulled), r);
            }
            _handleAftProxy.SetActive(handles && _latchAftNode == null);
            _handleFwdProxy.SetActive(handles && _latchFwdNode == null);
            if (handles)
            {
                _handleAftProxy.transform.SetPositionAndRotation(World(LatchHandle(cockpit, fwd: false)), r);
                _handleFwdProxy.transform.SetPositionAndRotation(World(LatchHandle(cockpit, fwd: true)), r);
            }
        }

        private void DestroyBailout()
        {
            if (_canopyProps != null) Object.Destroy(_canopyProps);
            if (_props != null) Object.Destroy(_props);
            if (_plug != null) Object.Destroy(_plug);
            if (_ropeLine != null) Object.Destroy(_ropeLine.gameObject);
            if (_gear != null) Object.Destroy(_gear);
        }
    }
}

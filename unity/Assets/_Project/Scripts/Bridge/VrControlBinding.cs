using System;
using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE STICK AND THE THROTTLE, BOUND TO THE RIG THAT DRAWS THEM (Ty 2026-11-02: "the extra throttle worked but not
    /// the stick", "it did not work on the 2.33", "in the Great Lakes neither the stick nor the throttle").
    ///
    /// Why they failed: the old binding modelled every stick as a rod standing STRAIGHT UP from its pivot, only knew
    /// the Extra's node names (StickRoll_R / Stick_R), and took the stick's length and the throttle's arm from a
    /// Grip_* empty that only the Extra and the Pitts have. Every stick in the fleet is RAKED (the Extra's grip 5.5 cm
    /// aft of its pivot, the Great Lakes' 12 cm, the Cub's 7 cm forward), so the one grab point sat that far from the
    /// grip the pilot sees — beyond the 6 cm reach for a fist closed on the Extra's grip — and on the Great Lakes, with
    /// no Grip_Throttle_*, the throttle's grab point was the proxy's, 20 cm from a pivot at a made-up angle.
    ///
    /// Now: the pitch hinge is the seat's elevator node (Stick_R / Stick_Rear / Stick_F / Stick_Front), the roll hinge
    /// its nearest ancestor on the aileron (StickRoll_*, Stick_*_Roll, the TorqueTube); the grip is the Grip_Stick_*
    /// empty or the top of the stick's own mesh less 4 cm; hinges, axes and the rest grip are read with the rig AT
    /// REST, and each hinge's sense is checked against the rig's own pose at full travel. The throttle is the seat's
    /// throttle node (Throttle_R / Throttle_Rear / Throttle_Lever_Rear ...) as a <see cref="HingedLever"/> through the
    /// knob the rig draws at idle and full. A type with no such nodes keeps the proxy.
    ///
    /// Shared by the headset (VrCockpitHands) and the bench check (EditorTools.VrHandleCheck), so the check measures
    /// the binding that ships.
    /// </summary>
    public static class VrControlBinding
    {
        /// <summary>The fist sits this far below the top of a stick with no Grip_Stick_* empty.</summary>
        public const float GripBelowTopM = 0.04f;
        /// <summary>The stick's grab capsule: from this far above the grip point ...</summary>
        public const double CapsuleUpM = 0.05;
        /// <summary>... to this far down the stick.</summary>
        public const double CapsuleDownM = 0.12;
        /// <summary>The throttle's capsule runs this far from the knob toward the lever's pivot.</summary>
        public const double ThrottleCapsuleM = 0.06;

        public sealed class Stick
        {
            public StickGimbal Gimbal;
            public RigHandle Roll, Pitch;
            public string GripSource = "proxy";
            /// <summary>Rig-pose check at bind: the gimbal's grip vs the rig's at full roll and full aft, metres.</summary>
            public double SenseResidualM;
            public bool OnRig => Pitch != null;
        }

        public sealed class Throttle
        {
            public HingedLever Lever;
            public RigHandle Node;
            public string GripSource = "proxy";
            public bool OnRig => Node != null;
            /// <summary>The lever's t (0 idle .. 1 full along its travel) for a throttle 0..1.</summary>
            public double TFor(double throttle01) => Node != null ? Node.TFor(throttle01) : Math.Clamp(throttle01, 0.0, 1.0);
            /// <summary>The throttle 0..1 for the lever's t.</summary>
            public double ThrottleFor(double t) => Node != null ? Math.Clamp(Node.ParamFor(t), 0.0, 1.0) : Math.Clamp(t, 0.0, 1.0);
        }

        public static string[] Suffixes(string seatId) =>
            seatId == "rear" ? new[] { "_R", "_Rear" } : seatId == "front" ? new[] { "_F", "_Front" } : Array.Empty<string>();

        private static Vector3 Local(Transform air, Vector3 world) => Quaternion.Inverse(air.rotation) * (world - air.position);
        private static Vector3 LocalDir(Transform air, Vector3 dir) => Quaternion.Inverse(air.rotation) * dir;
        private static Vec3 C(Vector3 v) => new(v.x, v.y, v.z);

        private static RigHandle FindParam(AirframeRig rig, string name, string param)
        {
            RigHandle h = RigHandle.Find(rig, name);
            return h != null && h.Node.Param == param ? h : null;
        }

        /// <summary>The seat's stick from the rig, or null (no rig, no elevator node for this seat).</summary>
        public static Stick BindStick(AirframeRig rig, Func<string, Transform> find, Transform air, string seatId)
        {
            if (rig == null) return null;
            string full = seatId == "rear" ? "Rear" : seatId == "front" ? "Front" : null;
            RigHandle pitch = null;
            foreach (string sfx in Suffixes(seatId))
                pitch ??= FindParam(rig, "Stick" + sfx, "elevator");
            if (pitch == null) return null;

            // The roll hinge: the nearest ancestor the rig turns on the aileron.
            RigHandle roll = null;
            for (Transform p = pitch.T.parent; p != null && roll == null; p = p.parent)
                foreach (AirframeRig.Bound b in rig.Nodes)
                    if (b.T == p && b.Node.Param == "aileron") { roll = RigHandle.Find(rig, b.Node.Node); break; }
            if (roll == null)
                foreach (string n in new[] { "StickRoll" + Suffixes(seatId)[0], "StickRoll" + Suffixes(seatId)[1], "Stick" + Suffixes(seatId)[1] + "_Roll", "TorqueTube" })
                    roll ??= FindParam(rig, n, "aileron");

            Transform gripEmpty = full != null && find != null ? find("Grip_Stick_" + full) : null;

            // Everything with both hinges at rest, then put back.
            Quaternion keepP = pitch.T.localRotation, keepR = roll != null ? roll.T.localRotation : Quaternion.identity;
            Vector3 keepPp = pitch.T.localPosition, keepRp = roll != null ? roll.T.localPosition : Vector3.zero;
            var s = new Stick { Pitch = pitch, Roll = roll };
            try
            {
                if (roll != null) { roll.T.localPosition = roll.RestPos; roll.Pose(0.0); }
                pitch.T.localPosition = pitch.RestPos; pitch.Pose(0.0);

                Vector3 gripW;
                if (gripEmpty != null) { gripW = gripEmpty.position; s.GripSource = "Grip_Stick_" + full; }
                else
                {
                    Vector3 top = pitch.T.TransformPoint(RigHandle.KnobOf(pitch.T, false));
                    Vector3 down = (pitch.PivotWorld - top).normalized;
                    gripW = top + down * GripBelowTopM;
                    s.GripSource = $"top of {pitch.Name}'s mesh less {GripBelowTopM * 100f:F0} cm ({RigHandle.VertexCount(pitch.T)} vertices)";
                }
                Vector3 gripInPitch = pitch.T.InverseTransformPoint(gripW);

                Vec3 pp = C(Local(air, pitch.PivotWorld)), pa = C(LocalDir(air, pitch.HingeWorld));
                Vec3 rp = roll != null ? C(Local(air, roll.PivotWorld)) : pp;
                Vec3 ra = roll != null ? C(LocalDir(air, roll.HingeWorld)) : new Vec3(0, 0, -1);
                Vec3 g0 = C(Local(air, gripW));
                RigCurve rMap = roll != null ? Curve(roll.Node) : RigCurve.Symmetric(18, 18);
                RigCurve pMap = Curve(pitch.Node);

                // Each hinge's sense from the rig's own pose (a mirrored scale anywhere up the chain turns it over).
                double rollEnd = rMap.Value(1.0), pitchEnd = pMap.Value(1.0);
                Vec3 rigRoll = g0, rigPitch;
                if (roll != null) { roll.Pose(rollEnd); rigRoll = C(Local(air, pitch.T.TransformPoint(gripInPitch))); roll.Pose(0.0); }
                pitch.Pose(pitchEnd); rigPitch = C(Local(air, pitch.T.TransformPoint(gripInPitch))); pitch.Pose(0.0);
                if (roll != null && Err(new StickGimbal(rp, -ra, pp, pa, g0, rMap, pMap), rollEnd, 0, rigRoll) <
                                    Err(new StickGimbal(rp, ra, pp, pa, g0, rMap, pMap), rollEnd, 0, rigRoll)) ra = -ra;
                if (Err(new StickGimbal(rp, ra, pp, -pa, g0, rMap, pMap), 0, pitchEnd, rigPitch) <
                    Err(new StickGimbal(rp, ra, pp, pa, g0, rMap, pMap), 0, pitchEnd, rigPitch)) pa = -pa;
                s.Gimbal = new StickGimbal(rp, ra, pp, pa, g0, rMap, pMap);

                // And both at once, the way the rig draws them (roll outer, pitch inner).
                if (roll != null) roll.Pose(rollEnd);
                pitch.Pose(pitchEnd);
                Vec3 both = C(Local(air, pitch.T.TransformPoint(gripInPitch)));
                s.SenseResidualM = Math.Max(Math.Max(Err(s.Gimbal, rollEnd, 0, rigRoll), Err(s.Gimbal, 0, pitchEnd, rigPitch)),
                                            Err(s.Gimbal, rollEnd, pitchEnd, both));
            }
            finally
            {
                pitch.T.localRotation = keepP; pitch.T.localPosition = keepPp;
                if (roll != null) { roll.T.localRotation = keepR; roll.T.localPosition = keepRp; }
            }
            return s;
        }

        private static double Err(StickGimbal g, double r, double p, Vec3 rig) => (g.GripAt(r, p) - rig).Length;

        /// <summary>A rig node's map as a curve (its ends and every point between, NOT snapped to detents).</summary>
        public static RigCurve Curve(RigNode n)
        {
            var ps = new List<double>(); var vs = new List<double>();
            for (int i = 0; i < n.MapPoints; i++) { ps.Add(n.MapPoint(i).Param); vs.Add(n.MapPoint(i).Value); }
            return new RigCurve(ps.ToArray(), vs.ToArray());
        }

        /// <summary>The seat's throttle lever from the rig, or null.</summary>
        public static Throttle BindThrottle(AirframeRig rig, Func<string, Transform> find, Transform air, string seatId)
        {
            if (rig == null) return null;
            string full = seatId == "rear" ? "Rear" : seatId == "front" ? "Front" : null;
            Transform gripEmpty = full != null && find != null ? find("Grip_Throttle_" + full) : null;
            RigHandle h = null;
            foreach (string sfx in Suffixes(seatId))
            {
                h ??= FindParam(rig, "Throttle" + sfx, "throttle") is RigHandle a ? RigHandle.Find(rig, a.Name, gripEmpty) : null;
                h ??= FindParam(rig, "Throttle_Lever" + sfx, "throttle") is RigHandle b ? RigHandle.Find(rig, b.Name, gripEmpty) : null;
            }
            if (h == null) return null;
            var lever = new HingedLever(C(Local(air, h.PivotWorld)), C(Local(air, h.KnobWorld(h.V0))), C(Local(air, h.KnobWorld(h.V1))),
                                        C(LocalDir(air, h.HingeWorld)));
            return new Throttle
            {
                Node = h, Lever = lever,
                GripSource = gripEmpty != null ? "Grip_Throttle_" + full : $"farthest vertex of {h.Name} ({RigHandle.VertexCount(h.T)} vertices)",
            };
        }

        /// <summary>The proxy throttle (a type with no lever node): the old arc, as a hinged lever about the lateral axis.</summary>
        public static Throttle ProxyThrottle(Vec3 pivot, double armM, double idleDeg, double sweepDeg)
        {
            var arc = new LeverArc(pivot, armM, idleDeg, sweepDeg);
            return new Throttle { Lever = new HingedLever(pivot, arc.GripAt(0.0), arc.GripAt(1.0), new Vec3(1, 0, 0)) };
        }

        /// <summary>The stick's grab capsule, as it stands: along the stick, from a little above the grip to well down it.</summary>
        public static (Vec3 A, Vec3 B) StickCapsule(StickGimbal g)
        {
            Vec3 up = g.Up, grip = g.Grip;
            return (grip + up * CapsuleUpM, grip - up * CapsuleDownM);
        }

        /// <summary>The throttle's grab capsule: from the knob toward the pivot.</summary>
        public static (Vec3 A, Vec3 B) ThrottleCapsule(HingedLever l, double t)
        {
            Vec3 k = l.GripAt(t);
            Vec3 axisPoint = l.Pivot + l.Axis * l.AxialM;
            Vec3 d = axisPoint - k;
            double len = d.Length;
            return (k, len > 1e-6 ? k + d * (Math.Min(ThrottleCapsuleM, 0.5 * len) / len) : k);
        }
    }
}

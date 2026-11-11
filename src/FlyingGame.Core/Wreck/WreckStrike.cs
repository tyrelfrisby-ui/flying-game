using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>
/// WHEN A DEPARTED PIECE HITS THE AEROPLANE IT CAME OFF. Ty 2026-11-11: "The engine and prop while separating should
/// follow an accurate flight path and if that path contacts the airframe it should damage it appropriately", and
/// "Yes thrown blade also follows physics and causes damage".
///
/// THIS IS THE ONLY THING THAT WAS MISSING, and it is worth saying what was already here because it decided the
/// shape of this file. A departed piece ALREADY flies properly — <see cref="WreckSim"/> integrates it 6-DOF and
/// <see cref="DebrisAero"/> gives it a flat-plate normal force with the centre-of-pressure walk that makes it tumble
/// and flutter — and it already arrives at ground or water through <see cref="IWreckWorld"/>, with no lifetime and
/// no distance cull anywhere, so "falls from the sky until it hits the ground" is what it has always done. What
/// <see cref="IWreckWorld"/> knows nothing about is the AEROPLANE. So this is the missing predicate and nothing else.
///
/// AND IT DOES NOT NEED NEW GEOMETRY, which was the thing worth checking before building anything.
/// <see cref="AirframeHydroModel.Build"/> already returns the intact airframe as a list of boxes in the body frame,
/// each tagged with the <see cref="WreckAtom"/> it belongs to — cabin, nose, tail cone, each wing panel, each
/// aileron, the stabiliser, each elevator, the fin, the rudder, each gear leg. Those are per-component collision
/// volumes, already built, already used by the flooding and break-up models. A departed piece carries the same kind
/// of boxes. So the test is box against box and never a sphere standing in for an aeroplane.
///
/// THE SWEEP MATTERS. A thrown blade leaves at the disc's rim speed — on the Pitts at the red line that is about
/// 130 m/s of tangential velocity before the aeroplane's own is added — and at 200 Hz that is two thirds of a metre
/// a step, which is wider than most of the boxes it is passing. Tested only at the step's end points it would go
/// straight through the cabin and report nothing. So the piece's centre of mass is swept as a SEGMENT through the
/// aeroplane's frame, and the boxes are expanded by the piece's own reach about its CG
/// (<see cref="WreckPiece.ReachM"/>, which for a single-box piece like a blade is exactly half its length).
///
/// EVERYTHING IS DONE IN THE AIRCRAFT'S BODY FRAME, which is what makes the relative motion come out right without a
/// word of special-casing. The aeroplane is still flying forward while the engine leaves the nose ahead of and below
/// the CG; in the aeroplane's own frame that is the engine moving AFT down the fuselage, past the cabin, under the
/// wing root and into the gear if the geometry takes it there. Which component it strikes falls out of the boxes.
/// </summary>
public static class WreckStrike
{
    /// <summary>What a departed piece hit, and how hard.</summary>
    public readonly struct Hit
    {
        /// <summary>The airframe box it reached first along its path this step.</summary>
        public readonly WreckAtom Atom;
        /// <summary>Where, in the aircraft body frame.</summary>
        public readonly Vec3 AtBody;
        /// <summary>The struck face's outward normal, aircraft body frame.</summary>
        public readonly Vec3 NormalBody;
        /// <summary>Closing speed along that normal, m/s — the part of the relative velocity that is arriving.</summary>
        public readonly double ClosingMs;
        /// <summary>Kinetic energy in the closing direction, J: ½·m·v_n². The number that decides whether this is a
        /// scuff or a hole.</summary>
        public readonly double EnergyJ;
        /// <summary>Fraction of the step at which it arrived, 0..1 — so the piece can be put where it actually hit.</summary>
        public readonly double T;
        public bool Any => ClosingMs > 0.0;

        public Hit(WreckAtom atom, Vec3 atBody, Vec3 normalBody, double closingMs, double energyJ, double t)
        { Atom = atom; AtBody = atBody; NormalBody = normalBody; ClosingMs = closingMs; EnergyJ = energyJ; T = t; }
    }

    /// <summary>
    /// Does <paramref name="piece"/> reach the airframe during this step? The piece's CG is swept through the
    /// aircraft's body frame and tested against every box; the earliest crossing wins.
    ///
    /// <paramref name="airframe"/> is <see cref="AirframeHydroModel.Elements"/> and <paramref name="cgBody"/> the
    /// config's reference CG, because the boxes are placed about the body origin and the aeroplane's state is about
    /// its CG — getting that wrong would put the whole aeroplane half a metre out, which on a Pitts is the
    /// difference between the cabin and the air beside it.
    /// </summary>
    public static Hit Test(WreckPiece piece, RigidBodyState aircraft, Vec3 cgBody,
                           IReadOnlyList<HydroElement> airframe, double dt)
        => Test(piece.State, piece.MassKg, piece.ReachM, aircraft, cgBody, airframe, dt);

    /// <summary>The same, as a pure function of the piece's state, mass and reach — which is what it actually needs,
    /// and what lets it be tested without a <see cref="WreckSim"/> to own the piece.</summary>
    public static Hit Test(RigidBodyState piece, double pieceMassKg, double pieceReachM,
                           RigidBodyState aircraft, Vec3 cgBody,
                           IReadOnlyList<HydroElement> airframe, double dt)
    {
        if (airframe == null || airframe.Count == 0 || dt <= 0.0) return default;

        // The piece's CG now and at the end of the step, both in the AIRCRAFT's body frame. The aeroplane's own
        // motion over one step is carried by taking its state at the start and its velocity for the end point: at
        // 200 Hz the attitude turns by a fraction of a degree, and the relative sweep is what this is about.
        Quat qi = aircraft.Attitude.Conjugate();
        Vec3 p0 = qi.Rotate(piece.Position - aircraft.Position) + cgBody;
        Vec3 pieceVelWorld = piece.Attitude.Rotate(piece.Velocity);
        Vec3 acVelWorld = aircraft.Attitude.Rotate(aircraft.Velocity);
        Vec3 relBody = qi.Rotate(pieceVelWorld - acVelWorld);
        Vec3 p1 = p0 + relBody * dt;

        double bestT = double.MaxValue;
        Hit best = default;
        foreach (HydroElement e in airframe)
        {
            // Expanded by the piece's reach: a blade is not a point, and its tip arrives before its middle does.
            Vec3 half = e.Half + new Vec3(pieceReachM, pieceReachM, pieceReachM);
            if (!SegmentBox(p0, p1, e.Centre, half, out double t, out Vec3 normal)) continue;
            if (t >= bestT) continue;
            double closing = -Vec3.Dot(relBody, normal);
            if (closing <= 0.0) continue;                      // leaving, not arriving
            bestT = t;
            best = new Hit(e.Atom, p0 + (p1 - p0) * t, normal, closing, 0.5 * pieceMassKg * closing * closing, t);
        }
        return best;
    }

    /// <summary>
    /// Segment against an axis-aligned box, the standard slab test, returning where along the segment it entered and
    /// which face. Axis-aligned is not an approximation here: <see cref="HydroElement"/> boxes ARE axis-aligned in
    /// the body frame, which is the frame this whole test runs in.
    /// </summary>
    public static bool SegmentBox(Vec3 a, Vec3 b, Vec3 centre, Vec3 half, out double t, out Vec3 normal)
    {
        t = 0.0; normal = Vec3.Zero;
        Vec3 d = b - a;
        double tMin = 0.0, tMax = 1.0;
        int axis = -1; double sign = 0.0;
        for (int k = 0; k < 3; k++)
        {
            double da = k == 0 ? d.X : k == 1 ? d.Y : d.Z;
            double aa = k == 0 ? a.X : k == 1 ? a.Y : a.Z;
            double cc = k == 0 ? centre.X : k == 1 ? centre.Y : centre.Z;
            double hh = k == 0 ? half.X : k == 1 ? half.Y : half.Z;
            double lo = cc - hh, hi = cc + hh;
            if (System.Math.Abs(da) < 1e-12)
            {
                if (aa < lo || aa > hi) return false;           // parallel and outside this slab
                continue;
            }
            double t1 = (lo - aa) / da, t2 = (hi - aa) / da;
            double s = -1.0;
            if (t1 > t2) { (t1, t2) = (t2, t1); s = 1.0; }
            if (t1 > tMin) { tMin = t1; axis = k; sign = s; }
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax) return false;
        }
        if (axis < 0) return false;                            // started inside: no entering face this step
        t = tMin;
        normal = axis == 0 ? new Vec3(sign, 0, 0) : axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign);
        return true;
    }

    /// <summary>
    /// WHAT THE BLOW DOES TO WHAT IT HIT. Ty asked for it to be "appropriate", and the way to make that mean
    /// something is to compare the strike against the SAME structural limit the rest of the model already uses: the
    /// ultimate bending moment of the joint that holds that component on
    /// (<see cref="Joint.UltimateNm"/>, sized from the type's own design load factor). So a glancing blow to a
    /// fabric elevator and a direct hit on a wing spar are different events because the numbers are different, not
    /// because of a table.
    ///
    /// The moment is the strike's impulse on the arm from the joint to where it landed. The impulse is the piece's
    /// own (<see cref="WreckPiece.StopAt"/> computes it through the piece's mass and inertia, inelastically), so a
    /// heavy engine arriving slowly and a light blade arriving fast are told apart by momentum rather than by speed.
    /// </summary>
    public static double StrikeMomentNm(double impulseNs, Vec3 atBody, Joint joint)
        => impulseNs * (atBody - joint.At).Length;

    /// <summary>Does this strike take the component off? Its moment against the joint's ultimate. Nothing is
    /// randomised: the same blow in the same place is the same outcome.</summary>
    public static bool Severs(double impulseNs, Vec3 atBody, Joint joint)
        => joint.UltimateNm > 0.0 && StrikeMomentNm(impulseNs, atBody, joint) > joint.UltimateNm;
}

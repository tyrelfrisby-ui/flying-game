using System;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim.Practice;

namespace FlyingGame.Sim;

/// <summary>The STOL contest's starts (owner 2026-10-07): on final at 1.1 Vs with full flaps (trimmed, the power set for
/// the path), or at rest with the main wheels on the line — both southbound on the grass strip.</summary>
public static class StolSpawn
{
    public const double FinalDistanceM = 600.0, PathDeg = 4.0, ApproachVsFactor = 1.1;

    /// <summary>On final: the state, the trim elevator (rad) and the throttle (0..1) that hold the 4° path at 1.1 Vs.</summary>
    public static (RigidBodyState state, double elevatorRad, double throttle01, double speedMs) Final(AircraftConfig c, StolContest k, double fieldElevM)
    {
        double vso = PracticeScenario.EstimateVso(c, fieldElevM + 30, 1.0);
        var t = GlideTable.Lookup(c.Id, 1.0); if (t != null && Math.Abs(fieldElevM - WorldTerrain.DatumM) < 60) vso = t.VsoKt / 1.943844;
        double v = ApproachVsFactor * vso, gamma = -PathDeg * Math.PI / 180;
        double drop = 0; foreach (GearConfig g in c.Gear) if (!g.IsTailwheel) drop = Math.Max(drop, g.Pos[2] - c.Mass.CgVec().Z);
        double hWheels = FinalDistanceM * Math.Tan(-gamma);
        double x = k.LineX + FinalDistanceM, y = k.StripY;   // north of the line, flying south
        TrimSolver.Result tr = TrimSolver.SolveGliderTrim(c, v, fieldElevM + hWheels, flapFraction: 1.0);
        double alpha = tr.Converged ? tr.AlphaRad : 0.12, pitch = alpha + gamma;
        // Crabbed into the wind so the track runs down the strip from the first frame (a 5 m/s crosswind uncrabbed rolled it).
        Vec3 w = Atmosphere.WindAtPosition(new Vec3(x, y, -(fieldElevM + hWheels)));
        double ax = Math.Cos(StolContest.HeadingRad), ay = Math.Sin(StolContest.HeadingRad);
        double wCross = -w.X * ay + w.Y * ax, wAlong = w.X * ax + w.Y * ay;
        double crab = Math.Asin(Math.Clamp(-wCross / v, -0.5, 0.5)), hdg = StolContest.HeadingRad + crab;
        Quat att = Quat.Multiply(new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2)), new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2)));
        double vAlong = Math.Sqrt(Math.Max(0, v * v - wCross * wCross)) + wAlong;   // ground speed along the strip
        var vWorld = new Vec3(ax * vAlong * Math.Cos(gamma), ay * vAlong * Math.Cos(gamma), -v * Math.Sin(gamma));
        var state = new RigidBodyState(new Vec3(x, y, -(fieldElevM + hWheels + drop)), att, att.Conjugate().Rotate(vWorld), Vec3.Zero);
        // Power for the path: drag (weight / glide ratio) less the weight's component along the descent.
        double weight = c.Mass.MassKg * 9.81, ratio = tr.Converged && tr.GlideRatio > 0 ? tr.GlideRatio : 6.0;
        double thrust = Math.Max(0, weight / ratio + weight * Math.Sin(gamma));
        double thr = 0;
        if (c.Propulsion is { } p)
            thr = p.PropDiameterM <= 0 ? thrust / (p.MaxPowerW * Math.Max(1, c.Engines.Count))
                                       : thrust * v / ((p.Efficiency > 0 ? p.Efficiency : 0.75) * p.MaxPowerW * Math.Max(1, c.Engines.Count));
        return (state, tr.Converged ? tr.ElevatorRad : 0.0, Math.Clamp(thr, 0, 1), v);
    }

    /// <summary>At rest, the main wheels on the line, heading south.</summary>
    public static RigidBodyState OnTheLine(AircraftConfig c, StolContest k, double fieldElevM)
    {
        Vec3 cg = c.Mass.CgVec(); double mainsX = 0; int n = 0;
        foreach (GearConfig g in c.Gear) if (!g.IsTailwheel && g.GearType != "float-keel" && g.Pos[2] > 0 && Math.Abs(g.Pos[1]) > 0.3) { mainsX += g.Pos[0] - cg.X; n++; }
        if (n > 0) mainsX /= n;
        // Southbound: body +x is world −x, so the CG sits mainsX north… of the mains: x_cg = line + mainsX.
        double x = k.LineX + mainsX;
        return LandingGear.RestingState(c, x, k.StripY, fieldElevM + WorldTerrain.GrassSwoopAt(x, k.StripY), StolContest.HeadingRad);
    }

    /// <summary>The main wheels' mean world position (NED) — what the judges measure.</summary>
    public static Vec3 Mains(AircraftConfig c, RigidBodyState s)
    {
        Vec3 cg = c.Mass.CgVec(), sum = Vec3.Zero; int n = 0;
        foreach (GearConfig g in c.Gear) if (!g.IsTailwheel && g.GearType != "float-keel" && g.Pos[2] > 0 && Math.Abs(g.Pos[1]) > 0.3) { sum += s.Position + s.Attitude.Rotate(g.PosVec() - cg); n++; }
        return n > 0 ? sum * (1.0 / n) : s.Position;
    }
}

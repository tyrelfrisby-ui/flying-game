using System;
using System.IO;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE TRACED THRUST JET (PropJet), owner 2026-11-08: "the thrust jet needs to tilt with alpha and beta accurately and
/// it needs to curve with pitch and yaw rates accurately in order to determine the effects of Q and relative wind on the
/// downstream surfaces", on a model that had it "locked to the body ignoring alpha/beta", which "would provide
/// artificial stability".
///
/// What these pin is the geometry, not a calibration: the tube is the path of the air, so in the reference state it goes
/// straight back and nothing changes, at alpha it rides over the tail, at sideslip it goes sideways, in a flat spin it
/// leaves the airframe entirely, and under rate its air arrives from a different direction than it left the hub. The
/// numbers below are measured from the construction (PropJetProbe), and each one is also what the closed form says:
/// the tube's displacement at the tail is arm · V_cross / (V_axial + ū) and the direction's swing is ω · arm / V_slip.
/// </summary>
public class PropJetTests
{
    private readonly ITestOutputHelper _out;
    public PropJetTests(ITestOutputHelper o) { _out = o; }

    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id = "pitts-s2b-like") =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // The Pitts at 30 m/s with the slipstream TailSlipstream gives at full power: ΔU 15 m/s, tube 0.69 m.
    private const double Du = 15.0, R0 = 0.69, Swirl = 3.0;

    private static (Vec3 At, double Radius, Vec3 Dir, double U) AtTail(AircraftConfig c, Vec3 v, Vec3 rates)
        => PropJet.Probe(c, v, rates, c.Mass.CgVec(), Du, R0, Swirl, PropJet.TailArm(c));

    /// <summary>Straight ahead with no rotation the air goes straight back, so the tube does: on the axis, at the full
    /// increment, pointing along it. This is the state every stored rig and trim was solved in.</summary>
    [Fact]
    public void InTheReferenceStateTheTubeGoesStraightBack()
    {
        var c = Load();
        var (at, rad, dir, u) = AtTail(c, new Vec3(30, 0, 0), Vec3.Zero);
        _out.WriteLine($"at ({at.X:F2},{at.Y:F2},{at.Z:F2}) rad {rad:F3} dir ({dir.X:F3},{dir.Y:F3},{dir.Z:F3}) u {u:F2}");
        Assert.Equal(0.0, at.Y, 9);
        Assert.Equal(0.0, at.Z - (c.Mass.CgVec().Z + c.Propulsion!.ThrustLineZ), 9);
        Assert.Equal(1.0, dir.X, 9);
        Assert.Equal(Du, u, 6);
        Assert.Equal(1.0, PropJet.TailShare(c, new Vec3(30, 0, 0), Vec3.Zero, c.Mass.CgVec(), Du, R0, Swirl));
    }

    /// <summary>Every shipping type's reference state is exact, not merely close: the share is 1.0 to the bit, so the
    /// rig solver (which never passes a rate and trims at a small alpha) is not quietly re-aimed by this change.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void TheReferenceStateIsExactForEveryShippingType(string id)
    {
        var c = Load(id);
        foreach (double v in new[] { 12.0, 30.0, 55.0 })
            Assert.Equal(1.0, PropJet.TailShare(c, new Vec3(v, 0, 0), Vec3.Zero, c.Mass.CgVec(), Du, R0, Swirl));
    }

    /// <summary>At alpha the air rises over the airframe and takes the tube with it: at +10° and 30 m/s with ΔU 15 the
    /// Pitts' tube is 0.53 m above the thrust line by the tail, which is why the elevator's blast fades as the nose comes
    /// up. The closed form (arm · V_cross / (V_axial + 0.75 ΔU) = 4.382 · 5.209 / 40.79 = 0.56 m) is 5 % more, because
    /// the traced tube carries only half the increment for the first diameter behind the disc and so is swept a little
    /// less far. Negative alpha mirrors it exactly.</summary>
    [Fact]
    public void AtAlphaTheTubeRidesOffTheTail()
    {
        var c = Load();
        double axisZ = c.Mass.CgVec().Z + c.Propulsion!.ThrustLineZ;
        double r = 10 / D;
        var up = AtTail(c, new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r)), Vec3.Zero);
        var dn = AtTail(c, new Vec3(30 * Math.Cos(r), 0, -30 * Math.Sin(r)), Vec3.Zero);
        _out.WriteLine($"alpha +10 z {up.At.Z - axisZ:F3}   alpha -10 z {dn.At.Z - axisZ:F3}");
        Assert.Equal(-0.53, up.At.Z - axisZ, 2);           // NED: negative z is ABOVE the thrust line
        Assert.Equal(+0.53, dn.At.Z - axisZ, 2);
        Assert.Equal(0.0, up.At.Y, 9);
    }

    /// <summary>At sideslip it goes sideways, by the same construction and symmetrically — the owner's "would bend with
    /// beta and mostly miss the tail".</summary>
    [Fact]
    public void AtSideslipTheTubeGoesSideways()
    {
        var c = Load();
        double r = 15 / D;
        var left = AtTail(c, new Vec3(30 * Math.Cos(r), 30 * Math.Sin(r), 0), Vec3.Zero);
        var right = AtTail(c, new Vec3(30 * Math.Cos(r), -30 * Math.Sin(r), 0), Vec3.Zero);
        _out.WriteLine($"beta +15 y {left.At.Y:F3}   beta -15 y {right.At.Y:F3}");
        Assert.Equal(-0.80, left.At.Y, 2);                 // closed form 0.85, less the increment's ramp
        Assert.Equal(+0.80, right.At.Y, 2);
        Assert.Equal(left.At.Y, -right.At.Y, 9);
    }

    /// <summary>In a flat spin the jet leaves the airframe: at alpha 90 the tube is 3.6 m off the thrust line by the
    /// tail station and the tail sees none of it. NOTHING here is gated on the wing being stalled — separation is not an
    /// input to any of it, which is the artificial stability the owner identified being removed structurally rather than
    /// by a smaller coefficient.</summary>
    [Fact]
    public void InAFlatSpinTheJetLeavesTheAirframe()
    {
        var c = Load();
        Vec3 cg = c.Mass.CgVec();
        Vec3 v = new Vec3(0, 0, 30), rates = new Vec3(0, 0, 5.2);
        var (at, _, _, _) = PropJet.Probe(c, v, rates, cg, Du, R0, Swirl, PropJet.TailArm(c));
        _out.WriteLine($"flat spin at ({at.X:F2},{at.Y:F2},{at.Z:F2}) share {PropJet.TailShare(c, v, rates, cg, Du, R0, Swirl):F3}");
        Assert.True(Math.Abs(at.Z - (cg.Z + c.Propulsion!.ThrustLineZ)) > 3.0);
        Assert.Equal(0.0, PropJet.TailShare(c, v, rates, cg, Du, R0, Swirl), 9);
        // And no tail strip is blown. The stabiliser's own station, which in the reference state is in the tube.
        // The stabiliser's own station (hub 1.2826 less the 4.382 m arm = −3.0995 m), which in the reference state is
        // squarely in the tube.
        Vec3 tailAt = new Vec3(-3.0995, 0.3, cg.Z + c.Propulsion.ThrustLineZ);
        Assert.Equal(Du, PropJet.RelWindAt(c, new Vec3(30, 0, 0), Vec3.Zero, cg, Du, R0, Swirl, tailAt, false).X, 6);
        Assert.Equal(0.0, PropJet.RelWindAt(c, v, rates, cg, Du, R0, Swirl, tailAt, true).Length, 9);
    }

    /// <summary>The increment is MOMENTUM, so its direction is fixed in the air and turns at −ω in body axes: by the
    /// tail the jet's air arrives from ω · arm / V_slip off the nose. At 100 deg/s yaw and 30 m/s that is 0.12 rad ≈ 7°,
    /// to the LEFT of the nose for a nose-right yaw rate, and the pitch axis mirrors it. This is the owner's "curve with
    /// pitch and yaw rates", and with it the fin and stabiliser see a flow angle the body axes never show.</summary>
    [Fact]
    public void TheJetsAirCurvesWithTheRates()
    {
        var c = Load();
        double w = 100 / D;
        var yawR = AtTail(c, new Vec3(30, 0, 0), new Vec3(0, 0, w));
        var yawL = AtTail(c, new Vec3(30, 0, 0), new Vec3(0, 0, -w));
        var pitchUp = AtTail(c, new Vec3(30, 0, 0), new Vec3(0, w, 0));
        _out.WriteLine($"yaw +100 dir ({yawR.Dir.X:F3},{yawR.Dir.Y:F3},{yawR.Dir.Z:F3})  pitch +100 dir ({pitchUp.Dir.X:F3},{pitchUp.Dir.Y:F3},{pitchUp.Dir.Z:F3})");
        double expect = w * PropJet.TailArm(c) / (30.0 + 0.75 * Du);
        Assert.Equal(-expect, yawR.Dir.Y, 1);
        Assert.Equal(+expect, yawL.Dir.Y, 1);
        Assert.Equal(+expect, pitchUp.Dir.Z, 1);
        Assert.True(yawR.Dir.X > 0.98);
    }

    /// <summary>What a surface takes: the full increment along the tube on the axis, nothing at all outside it, and a
    /// shear layer rather than a step across the edge — a strip crossing the boundary must not slam the solver. The
    /// increment is a VECTOR, so the strip's dynamic pressure and its incidence both come from it.</summary>
    [Fact]
    public void AStripTakesTheIncrementOnlyWhereTheTubeIs()
    {
        var c = Load();
        Vec3 cg = c.Mass.CgVec();
        double axisZ = cg.Z + c.Propulsion!.ThrustLineZ;
        Vec3 v = new Vec3(30, 0, 0);
        double arm = PropJet.TailArm(c);
        var (centre, rad, _, _) = PropJet.Probe(c, v, Vec3.Zero, cg, Du, R0, Swirl, arm);
        Vec3 onAxis = new Vec3(centre.X, 0, axisZ);
        Vec3 inc = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, onAxis, false);
        _out.WriteLine($"radius {rad:F3}  on axis ({inc.X:F2},{inc.Y:F2},{inc.Z:F2})");
        Assert.Equal(Du, inc.X, 6);
        Assert.Equal(0.0, inc.Y, 9);
        Vec3 outside = new Vec3(centre.X, 0, axisZ - (rad + 0.5));
        Assert.Equal(0.0, PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, outside, false).Length, 9);
        // The shear layer is the Report 648 shape the tube carries: a uniform core to half the radius, then cos-squared
        // to zero at the edge. So it is HALF the increment at three quarters of the radius (cos 45 squared) and only a
        // few per cent at 0.95 R. Pinned at two radii, because it is the shape that keeps a strip crossing the boundary
        // from slamming the solver, not any one value. (This band was written for the 25 % linear ramp the cos-squared
        // profile replaced, and asked for 0.05 Du at 0.95 R where the real profile gives 0.025.)
        double mid = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, new Vec3(centre.X, 0.75 * rad, axisZ), false).X;
        Vec3 inEdge = new Vec3(centre.X, 0.95 * rad, axisZ);
        double edge = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, inEdge, false).X;
        _out.WriteLine($"at 0.75 R: {mid:F2}, at 0.95 R: {edge:F2}, of {Du:F2}");
        Assert.InRange(mid, 0.40 * Du, 0.60 * Du);
        Assert.InRange(edge, 0.01 * Du, 0.06 * Du);
        Assert.True(edge < mid, "the shear layer must fall outward");
        // Ahead of the disc there is no wash at all.
        Assert.Equal(0.0, PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, new Vec3(centre.X + 12.0, 0, axisZ), false).Length, 9);
    }

    /// <summary>The swirl, unchanged in sense from the model it replaces: a right-hand prop (clockwise from behind)
    /// carries the air UP past the left of the tail and DOWN past the right, so the relative wind's z increment is
    /// negative on the right half (+y) and positive on the left, about the tube's own axis.</summary>
    [Fact]
    public void TheSwirlTurnsTheRightWayAboutTheTubesAxis()
    {
        var c = Load();
        Vec3 cg = c.Mass.CgVec();
        double axisZ = cg.Z + c.Propulsion!.ThrustLineZ;
        Vec3 v = new Vec3(30, 0, 0);
        var (centre, rad, _, _) = PropJet.Probe(c, v, Vec3.Zero, cg, Du, R0, Swirl, PropJet.TailArm(c));
        double off = 0.5 * rad;
        Vec3 right = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, new Vec3(centre.X, +off, axisZ), true);
        Vec3 left = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, new Vec3(centre.X, -off, axisZ), true);
        _out.WriteLine($"right z {right.Z:F3}  left z {left.Z:F3}  (sign {c.Propulsion.RotationSign})");
        Assert.True(right.Z < 0.0);
        Assert.Equal(-right.Z, left.Z, 9);
        // Without the swirl flag the two are identical: the fin's share of the swirl is the propeller model's own yaw.
        Vec3 noSwirl = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, new Vec3(centre.X, +off, axisZ), false);
        Assert.Equal(0.0, noSwirl.Z, 9);
    }

    /// <summary>A twin's jets leave its nacelles, not its nose: behind the left engine there is a blast and on the
    /// fuselage centreline between them there is none. The single centreline tube could not say this.</summary>
    [Fact]
    public void ATwinsJetsLeaveItsNacelles()
    {
        var c = Load("dc3-like");
        Vec3 cg = c.Mass.CgVec();
        Assert.True(c.Engines.Count == 2);
        double y = c.Engines[0].Pos[1];
        Vec3 v = new Vec3(50, 0, 0);
        Vec3 behindNacelle = new Vec3(cg.X - 4.0, y, c.Engines[0].Pos[2]);
        Vec3 onCentreline = new Vec3(cg.X - 4.0, 0, c.Engines[0].Pos[2]);
        double a = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, behindNacelle, false).X;
        double b = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, onCentreline, false).X;
        _out.WriteLine($"nacelle y {y:F2}: behind it {a:F2}, on the centreline {b:F2}");
        Assert.Equal(Du, a, 6);
        Assert.Equal(0.0, b, 9);
    }

    /// <summary>propulsion.jetK is the one scale factor on the whole jet: 0 is no jet at all, for telling the jet's
    /// contribution from the rest of a result.</summary>
    [Fact]
    public void JetKScalesTheWholeJet()
    {
        var c = Load();
        Vec3 cg = c.Mass.CgVec();
        double axisZ = cg.Z + c.Propulsion!.ThrustLineZ;
        Vec3 v = new Vec3(30, 0, 0);
        var (centre, _, _, _) = PropJet.Probe(c, v, Vec3.Zero, cg, Du, R0, Swirl, PropJet.TailArm(c));
        Vec3 at = new Vec3(centre.X, 0, axisZ);
        double full = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, at, false).X;
        c.Propulsion.JetK = 0.5;
        double half = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, at, false).X;
        c.Propulsion.JetK = 0.0;
        double none = PropJet.RelWindAt(c, v, Vec3.Zero, cg, Du, R0, Swirl, at, false).Length;
        _out.WriteLine($"k 1 {full:F2}  k 0.5 {half:F2}  k 0 {none:F2}");
        Assert.Equal(0.5 * full, half, 6);
        Assert.Equal(0.0, none, 9);
    }

    /// <summary>A prop doing nothing to the air (stopped, torn off, or a type with no propeller) traces no tube, so the
    /// jet costs nothing and changes nothing when there is no jet.</summary>
    [Fact]
    public void NoPropellerNoJet()
    {
        var c = Load();
        Vec3 cg = c.Mass.CgVec();
        Assert.Equal(1.0, PropJet.TailShare(c, new Vec3(30, 0, 0), Vec3.Zero, cg, 0.0, R0, 0.0));
        Assert.Equal(0.0, PropJet.RelWindAt(c, new Vec3(30, 0, 0), Vec3.Zero, cg, 0.0, R0, 0.0, new Vec3(-2, 0, 0), true).Length, 9);
        var glider = Load("glider-2-33-like");
        Assert.Equal(1.0, PropJet.TailShare(glider, new Vec3(30, 0, 0), Vec3.Zero, glider.Mass.CgVec(), Du, R0, Swirl));
    }
}

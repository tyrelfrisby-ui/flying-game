using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty's Quest 3S report, 2026-11-04 (build 2026.1104.1656): "no views are available except 'outside' which is actually
/// cockpit. the wrists are super unrealistic on the hands and look really weird. the arms go into the structure on the
/// great lakes like the switch box on the left." The pure halves of the fixes: <see cref="QuestOutsideView"/> (the level,
/// eased, snap-turned origin of an outside view) and <see cref="ArmPose"/> (the hand on the grip's own axis, the wrist
/// behind the fist, the elbow swung clear of the structure). Expected numbers are worked by hand from the stated rules.
/// </summary>
public class QuestViewArmTests
{
    private const double Dt = 1.0 / 72.0;

    // ---- outside views -------------------------------------------------------------------------------------------

    [Fact]
    public void OutsideOriginRidesASteadyAeroplaneExactlyAndNeverTurnsByItself()
    {
        var o = new QuestOutsideView();
        var v = new Vec3(0, 0, 50);           // 50 m/s north, level
        Vec3 tgt = Vec3.Zero, off = new(0, 3, -20);
        for (int i = 0; i < 720; i++)          // ten seconds
        {
            tgt += v * Dt;
            o.Step(1, false, tgt, tgt + off, new Vec3(0, -0.1, 1), Dt);
        }
        Assert.Equal(0.0, (o.Position - (tgt + off)).Length, 9);
        Assert.Equal(0.0, o.YawDeg, 9);
        Assert.Equal(0, o.Snaps);
    }

    [Fact]
    public void OutsideOriginEasesASwingingOffsetAndSnapsTheHeadingOnlyPast45Degrees()
    {
        var o = new QuestOutsideView();
        o.Step(1, false, Vec3.Zero, new Vec3(0, 3, -20), new Vec3(0, 0, 1), Dt);
        // The chase camera swings 30° round the aeroplane in one frame (the flight path turned): the origin does not jump,
        // and 30° is under the snap, so the heading stays.
        double a = 30 * System.Math.PI / 180;
        var swung = new Vec3(-20 * System.Math.Sin(a), 3, -20 * System.Math.Cos(a));
        o.Step(1, false, Vec3.Zero, swung, new Vec3(System.Math.Sin(a), 0, System.Math.Cos(a)), Dt);
        double k = 1 - System.Math.Exp(-Dt / QuestOutsideView.OffsetTauSec);
        Assert.Equal((new Vec3(0, 3, -20) + (swung - new Vec3(0, 3, -20)) * k - o.Position).Length, 0.0, 9);
        Assert.Equal(0.0, o.YawDeg, 9);
        // Past 45° it snaps, once, all the way.
        double b = 50 * System.Math.PI / 180;
        o.Step(1, false, Vec3.Zero, swung, new Vec3(System.Math.Sin(b), 0, System.Math.Cos(b)), Dt);
        Assert.Equal(50.0, o.YawDeg, 9);
        Assert.Equal(1, o.Snaps);
    }

    [Fact]
    public void FlybyStandsStillInTheWorldAndTeleportsWhenItReparks()
    {
        var o = new QuestOutsideView();
        var park = new Vec3(100, 20, 300);
        for (int i = 0; i < 100; i++)
        {
            var tgt = new Vec3(0, 50, i * 1.0);
            o.Step(8, true, tgt, park, tgt - park, Dt);
            Assert.Equal(0.0, (o.Position - park).Length, 12);
        }
        double yaw0 = o.YawDeg;   // set at the park, toward the aeroplane; it does not follow it round
        Assert.Equal(QuestOutsideView.YawOf(new Vec3(0, 50, 0) - park, 0), yaw0, 9);
        var park2 = new Vec3(-120, 25, 600);
        o.Step(8, true, new Vec3(0, 50, 100), park2, new Vec3(0, 50, 100) - park2, Dt);
        Assert.Equal(0.0, (o.Position - park2).Length, 12);
        Assert.Equal(QuestOutsideView.YawOf(new Vec3(0, 50, 100) - park2, 0), o.YawDeg, 9);
    }

    [Fact]
    public void ANewViewStartsAfresh()
    {
        var o = new QuestOutsideView();
        o.Step(1, false, Vec3.Zero, new Vec3(0, 3, -20), new Vec3(0, 0, 1), Dt);
        o.Step(10, false, Vec3.Zero, new Vec3(15, 5, 0), new Vec3(-1, 0, 0), Dt);
        Assert.Equal(0.0, (o.Position - new Vec3(15, 5, 0)).Length, 12);
        Assert.Equal(-90.0, o.YawDeg, 9);
    }

    // ---- the arm on a grip ----------------------------------------------------------------------------------------

    // Cockpit frame relative to the eye (+x right, +y up, +z nose): SeatedPilot's right shoulder, a stick grip 0.50
    // below and 0.42 ahead of the eye on an upright stick (CockpitProxy).
    private static readonly Vec3 ShoulderR = SeatedPilot.ShoulderR, Grip = new(0.0, -0.50, 0.42), Up = new(0, 1, 0);

    [Fact]
    public void TheHandWrapsTheStickAndTheWristSitsBehindTheFist()
    {
        ArmPose.Result r = ArmPose.OnGrip(false, ShoulderR, Grip, Up, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, SeatedPilot.WristToGripM);
        Assert.True(r.Reached);
        Assert.Equal(0.0, (r.Grip - Grip).Length, 9);
        // A fist's depth behind the grip along the hand, the hand within 20° of square to the shaft, thumb up it.
        Assert.Equal(SeatedPilot.WristToGripM, (r.Grip - r.Wrist).Length, 9);
        Assert.True(System.Math.Abs(r.HandForward.Dot(Up)) <= System.Math.Sin(ArmPose.MaxTiltDeg * System.Math.PI / 180) + 1e-9);
        Assert.True(r.Thumb.Dot(Up) > 0.9);
        // The right palm faces the stick from its right: it points left (−x) more than anything else across.
        Assert.True(r.Palm.X < -0.5, $"palm {r.Palm}");
        // And the wrist is nearly straight: the forearm runs into the hand.
        Assert.True(r.WristBendDeg < 15.0, $"wrist bend {r.WristBendDeg:F1}°");
    }

    [Fact]
    public void TheOldWristLawBentTheWristFarMore()
    {
        // Before: the wrist on the line from the grip back to the shoulder, the hand aimed down that line.
        Vec3 toS = (ShoulderR - Grip).Normalized();
        Vec3 wrist = Grip + toS * SeatedPilot.WristToGripM;
        TwoBoneIk.Result old = TwoBoneIk.Solve(ShoulderR, wrist, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR);
        double oldBend = ArmPose.AngleDeg(old.End - old.Middle, Grip - wrist);
        ArmPose.Result now = ArmPose.OnGrip(false, ShoulderR, Grip, Up, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, SeatedPilot.WristToGripM);
        Assert.True(oldBend > 25.0, $"old bend {oldBend:F1}°");
        Assert.True(now.WristBendDeg < oldBend - 15.0, $"{now.WristBendDeg:F1}° vs {oldBend:F1}°");
    }

    /// <summary>A box of structure (a switch box) where the natural elbow would go.</summary>
    private sealed class Box : IArmClearance
    {
        public Vec3 Min, Max;
        public double Distance(Vec3 p)
        {
            double dx = System.Math.Max(Min.X - p.X, p.X - Max.X), dy = System.Math.Max(Min.Y - p.Y, p.Y - Max.Y), dz = System.Math.Max(Min.Z - p.Z, p.Z - Max.Z);
            double outside = new Vec3(System.Math.Max(dx, 0), System.Math.Max(dy, 0), System.Math.Max(dz, 0)).Length;
            return outside > 0 ? outside : System.Math.Max(dx, System.Math.Max(dy, dz));
        }
    }

    [Fact]
    public void TheElbowSwingsOutOfAStructureAndStaysPutInTheClear()
    {
        ArmPose.Result free = ArmPose.OnGrip(false, ShoulderR, Grip, Up, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, SeatedPilot.WristToGripM);
        // A 12 cm box centred on where that elbow went.
        var box = new Box { Min = free.Elbow - new Vec3(0.06, 0.06, 0.06), Max = free.Elbow + new Vec3(0.06, 0.06, 0.06) };
        ArmPose.Result r = ArmPose.OnGrip(false, ShoulderR, Grip, Up, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, SeatedPilot.WristToGripM, box);
        Assert.True(r.NaturalPenetrationM > 0.08, $"natural {r.NaturalPenetrationM:F3} m");
        Assert.True(r.PenetrationM < 0.01, $"swung {r.PenetrationM:F3} m at {r.SwivelDeg}°");
        Assert.NotEqual(0.0, r.SwivelDeg);
        Assert.Equal(0.0, (r.Grip - Grip).Length, 9);   // the hand stays on the stick
        // Far from anything, it does not swing at all.
        var far = new Box { Min = new Vec3(5, 5, 5), Max = new Vec3(6, 6, 6) };
        Assert.Equal(0.0, ArmPose.OnGrip(false, ShoulderR, Grip, Up, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, SeatedPilot.WristToGripM, far).SwivelDeg);
    }

    [Fact]
    public void ElbowAtKeepsBothBoneLengths()
    {
        Vec3 w = new(0.1, -0.5, 0.3);
        foreach (double deg in new[] { -60.0, -20.0, 0.0, 35.0, 80.0 })
        {
            Vec3 e = ArmPose.ElbowAt(ShoulderR, w, SeatedPilot.UpperArmM, SeatedPilot.ForearmM, SeatedPilot.ElbowPoleR, deg);
            Assert.Equal(SeatedPilot.UpperArmM, (e - ShoulderR).Length, 9);
            Assert.Equal(SeatedPilot.ForearmM, (w - e).Length, 9);
        }
    }
}

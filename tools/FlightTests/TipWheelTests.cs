using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Wing-tip wheels (owner request): when the glider drops a wing on the ground the tip must be caught
/// by its tip wheel — it settles on main wheel + tip wheel at the geometric angle, never rolls over or
/// lets the wing pierce the surface.
/// </summary>
public class TipWheelTests
{
    private static readonly AircraftConfig Config = TestAircraftConfig.Load();

    [Fact]
    public void GliderHasTipWheelsAtBothTips()
    {
        int tips = Config.Gear.Count(g => Math.Abs(g.Pos[1]) > 5.0);
        Assert.Equal(2, tips);
        Assert.Contains(Config.Gear, g => g.Pos[1] > 5.0 && g.Pos[2] < 0.0); // above the CG, under the raised tip
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void DroppedWingSettlesOnTipWheel(int side)
    {
        // At rest on the runway, rolled 8 deg toward `side`: the tip drops until its wheel touches
        // (geometry: tan(phi) = (1.0 - (-0.53)) / 7.6 → ~11.4 deg) and holds there.
        double phi0 = side * 8.0 * Math.PI / 180.0;
        var att = new Quat(Math.Sin(phi0 / 2), 0, 0, Math.Cos(phi0 / 2));
        var state = new RigidBodyState(new Vec3(0, 0, -0.98), att, Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(Config, state, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);

        double maxRoll = 0;
        for (int i = 0; i < 60; i++)
        {
            sim.RunFor(0.1, ControlInputs.Neutral);
            double phi = Roll(ac.State) * 180 / Math.PI;
            Assert.False(double.IsNaN(phi), "Sim went NaN on the ground.");
            maxRoll = Math.Max(maxRoll, Math.Abs(phi));
        }

        double final = Roll(ac.State) * 180 / Math.PI;
        Assert.True(Math.Sign(final) == side, $"Wing should stay down on the {side} side (final roll {final:F1} deg).");
        Assert.InRange(Math.Abs(final), 9.0, 14.0);
        Assert.True(maxRoll < 20.0, $"Rolled to {maxRoll:F1} deg — tip wheel did not catch the wing.");
    }

    private static double Roll(RigidBodyState s)
    {
        Quat q = s.Attitude;
        return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
    }
}

/// <summary>2-33 nose skid: clear of the ground at rest, catches the nose under hard braking so the glider
/// cannot tip onto its nose.</summary>
public class NoseSkidTests
{
    private static readonly AircraftConfig Config = TestAircraftConfig.Load();

    [Fact]
    public void HardBrakingRolloutDoesNotTipOver()
    {
        // Rolling out on the runway at 15 m/s, full wheel brake, stick neutral.
        var mains = Config.Gear.FindAll(g => Math.Abs(g.Pos[1]) < 0.1 && g.Pos[2] > 0.9 && g.GearType != "nose-skid");
        var tail = Config.Gear.Find(g => g.Pos[0] < -3);
        double pitch0 = Math.Atan((mains[0].Pos[2] - tail!.Pos[2]) / (mains[0].Pos[0] - tail.Pos[0]));
        var att = new Quat(0, Math.Sin(pitch0 / 2), 0, Math.Cos(pitch0 / 2));
        double maxWz = -999; foreach (var g in Config.Gear) maxWz = Math.Max(maxWz, att.Rotate(g.PosVec() - Config.Mass.CgVec()).Z);
        var state = new RigidBodyState(new Vec3(0, 0, -maxWz + 0.01), att, new Vec3(15, 0, 0), Vec3.Zero);
        var ac = new Aircraft(Config, state, ControlDeflections.Neutral) { BrakeInput = 1.0 };
        var sim = new SimLoop(ac);
        double minPitch = 0;
        for (int i = 0; i < 100; i++)
        {
            sim.RunFor(0.1, new ControlInputs(0, 0, 0, 1.0)); // spoilers out too
            Quat q = ac.State.Attitude;
            double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3;
            minPitch = Math.Min(minPitch, pitch);
            Assert.False(double.IsNaN(pitch));
        }
        Assert.True(minPitch > -12, $"nosed over to {minPitch:F1} deg under braking");
        Assert.True(ac.State.Velocity.Length < 1.0, $"should have stopped (V={ac.State.Velocity.Length:F1})");
    }
}

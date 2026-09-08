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

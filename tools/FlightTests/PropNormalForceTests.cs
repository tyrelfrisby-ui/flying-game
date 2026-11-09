using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE PROPELLER'S IN-PLANE (NORMAL) FORCE — sign conventions and the momentum form (PropModel.NormalForce, 2026-11-08).
/// Every other propeller effect in the model is axial force or a pure moment, so this is the only one that can push the
/// airframe sideways, and a sign error in it would be invisible in level flight and decisive in a spin.
/// </summary>
public class PropNormalForceTests
{
    private static PropulsionConfig Prop(double k) => new()
    {
        MaxPowerW = 194000, PropDiameterM = 1.98, PropBladeCount = 2, IdleRpm = 700, MaxRpm = 2700,
        PropInertia = 3.5, RotationSign = 1, Efficiency = 0.8, NormalForceK = k,
    };

    [Fact]
    public void OffByDefaultSoTheFleetIsUntouched()
    {
        Assert.Equal(0.0, new PropulsionConfig().NormalForceK);
        Assert.Equal(Vec3.Zero, PropModel.NormalForce(Prop(0.0), 2000, new Vec3(40, 5, 10), 1.225, 1.0));
    }

    [Fact]
    public void TheGateSwitchesItOffInAttachedFlow()
        => Assert.Equal(Vec3.Zero, PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 5, 10), 1.225, 0.0));

    /// <summary>At positive alpha the air meets the disc from below, the disc turns it down, and the reaction is UP
    /// (body −z). Ahead of the CG that is the classic nose-up, statically destabilising propeller normal force.</summary>
    [Fact]
    public void PositiveAlphaGivesAnUpwardForce()
    {
        Vec3 f = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 0, 10), 1.225, 1.0);
        Assert.True(f.Z < 0, $"expected body −z (up), got {f.Z:0.0}");
        Assert.Equal(0.0, f.Y, 9);
        Assert.Equal(0.0, f.X, 9);   // in the disc's PLANE: never an axial contribution
    }

    /// <summary>Sideslip from the right (v &gt; 0) pushes the nose to the LEFT, which at a station ahead of the CG is a
    /// yaw moment that grows the sideslip — a tractor propeller is directionally destabilising.</summary>
    [Fact]
    public void SideslipGivesAForceOpposingIt()
    {
        Vec3 f = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 10, 0), 1.225, 1.0);
        Assert.True(f.Y < 0, $"expected body −y, got {f.Y:0.0}");
        Assert.Equal(0.0, f.Z, 9);
    }

    [Fact]
    public void ItReversesWithTheCrossflowSoItDoesNotFavourOneAttitude()
    {
        Vec3 up = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 0, 10), 1.225, 1.0);
        Vec3 dn = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 0, -10), 1.225, 1.0);
        Assert.Equal(-up.Z, dn.Z, 6);
    }

    /// <summary>Edge-on (no axial component at all) the disc still ingests its own induced flow, so the force does not
    /// vanish — which is the whole point at the 90° inflow of a flat spin.</summary>
    [Fact]
    public void EdgeOnTheInducedFlowStillCarriesIt()
    {
        Vec3 f = PropModel.NormalForce(Prop(0.1), 2500, new Vec3(0, 0, -40), 0.91, 1.0);
        double area = System.Math.PI * 0.99 * 0.99;
        double vi = System.Math.Sqrt(2500.0 / (2.0 * 0.91 * area));
        double expect = 0.1 * 0.91 * area * vi * 40.0;
        Assert.Equal(expect, f.Z, 3);
        Assert.True(f.Z > 0, "crossflow from the canopy side pushes the airframe toward the belly (+z)");
    }

    [Fact]
    public void ItScalesLinearlyWithTheCoefficientAndTheGate()
    {
        Vec3 full = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 0, 10), 1.225, 1.0);
        Vec3 half = PropModel.NormalForce(Prop(0.1), 2000, new Vec3(40, 0, 10), 1.225, 0.5);
        Vec3 tenth = PropModel.NormalForce(Prop(0.01), 2000, new Vec3(40, 0, 10), 1.225, 1.0);
        Assert.Equal(full.Z * 0.5, half.Z, 6);
        Assert.Equal(full.Z * 0.1, tenth.Z, 6);
    }
}

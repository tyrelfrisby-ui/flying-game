using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Aerotow rope physics (owner-directed): the rope is an elastic weak-linked tether that only pulls.
/// These test the force model directly by placing the two aircraft at chosen separations and reading
/// the tension/force the rope applies — deterministic, free of free-flight confounds (in-game the
/// glider pilot actively flies formation; the tug is AI speed/altitude-hold).
/// </summary>
public class AeroTowTests
{
    private static Aircraft At(string id, Vec3 pos, Vec3 velWorld)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        // Level, heading +x. Velocity given in world frame == body frame here (identity attitude).
        return new Aircraft(c, new RigidBodyState(pos, Quat.Identity, velWorld, Vec3.Zero),
            new ControlDeflections(0, 0, 0, 0));
    }

    // Tug tail hook at CG+(-3.4); glider nose hook at CG+(+2). Rope natural length 61 m.
    private static AeroTow Setup(double gliderX, double tugX, Vec3 gliderVel = default, Vec3 tugVel = default)
    {
        var tug = At("pa18-cub-like", new Vec3(tugX, 0, -300), tugVel.LengthSquared > 0 ? tugVel : new Vec3(30, 0, 0));
        var glider = At("glider-2-33-like", new Vec3(gliderX, 0, -300), gliderVel.LengthSquared > 0 ? gliderVel : new Vec3(30, 0, 0));
        return new AeroTow(tug, glider);
    }

    [Fact]
    public void TautRopePullsGliderForwardAndTugBack()
    {
        // Hook separation = (tugX-3.4) - (gliderX+2). For 62 m (1 m stretch): tugX=35, gliderX=-30.4.
        var tow = Setup(gliderX: -32.4, tugX: 35);
        tow.Apply(0.01);
        Assert.True(tow.Connected);
        Assert.InRange(tow.Tension, 1500, 2500);                       // ~2000 N/m * 1 m stretch
        Assert.True(tow.Glider.ExternalForceWorld.X > 100, $"Glider must be pulled +x toward tug; got {tow.Glider.ExternalForceWorld.X:F0}");
        Assert.True(tow.Tug.ExternalForceWorld.X < -100, $"Tug must be pulled -x back toward glider; got {tow.Tug.ExternalForceWorld.X:F0}");
        Assert.True(System.Math.Abs(tow.Glider.ExternalForceWorld.X + tow.Tug.ExternalForceWorld.X) < 1, "Equal and opposite (Newton's 3rd).");
    }

    [Fact]
    public void SlackRopeAppliesNoForce()
    {
        // Separation well under 61 m: tugX=20, gliderX=0 -> hooks ~14.6 m apart.
        var tow = Setup(gliderX: 0, tugX: 20);
        tow.Apply(0.01);
        Assert.Equal(0.0, tow.Tension, 3);
        Assert.True(tow.Glider.ExternalForceWorld.LengthSquared < 1e-9);
    }

    [Fact]
    public void ReleaseFreesTheGlider()
    {
        var tow = Setup(gliderX: -32.4, tugX: 35);
        tow.Apply(0.01);
        Assert.True(tow.Tension > 0);
        tow.Release();
        tow.Apply(0.01);
        Assert.False(tow.Connected);
        Assert.Equal("released", tow.SeverReason);
        Assert.True(tow.Glider.ExternalForceWorld.LengthSquared < 1e-9, "Released glider feels no rope force.");
    }

    [Fact]
    public void SustainedOverloadBreaksWeakLink()
    {
        // 62.5 m hook separation = 1.5 m stretch -> ~3000 N sustained, over the 2940 N glider link.
        var tow = Setup(gliderX: -32.9, tugX: 35);
        for (int i = 0; i < 60 && tow.Connected; i++)
        {
            tow.Apply(0.01);   // hold the overload -> filtered tension climbs past the weak link
        }
        Assert.False(tow.Connected, "Sustained overload must break the weak link.");
        Assert.Contains("weaklink", tow.SeverReason);
    }
}

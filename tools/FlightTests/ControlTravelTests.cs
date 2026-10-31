using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>What the surfaces are allowed to move, against the type certificate that says so, and the asymmetric stops
/// that needs (owner 2026-10-30: the J-3's travels were measured against FAA Aircraft Specification A-691 Rev 34 and
/// none of the three matched). An elevator's two stops are rarely the same distance from neutral — more up than down —
/// and the own-model GLBs are rigged to the certificate, so a config that disagrees draws a surface in one place and
/// flies it in another. See docs/CONTROL-SURFACES.md.</summary>
public class ControlTravelTests
{
    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id + ".json"));
    private const double Deg = Math.PI / 180.0;

    /// <summary>A-691 Rev 34, Item 4 "Control Surface Movements": elevator 34 up / 29 down, aileron 18 / 18,
    /// rudder 30 / 30. The same figures cover the landplane and the floatplane (J3C-65 and J3C-65S).</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void TheCubMovesItsSurfacesAsFarAsItsCertificateSays(string id)
    {
        ControlsConfig c = Load(id).Controls;
        Assert.Equal(18.0, c.Aileron.MaxDeflRad / Deg, 1);
        Assert.Equal(18.0, c.Aileron.LimitFor(-1.0) / Deg, 1);      // symmetric: no negative stop of its own
        Assert.Equal(29.0, c.Elevator.MaxDeflRad / Deg, 1);         // positive = trailing edge DOWN
        Assert.Equal(34.0, c.Elevator.LimitFor(-1.0) / Deg, 1);     // negative = UP, and there is more of it
        Assert.Equal(30.0, c.Rudder.MaxDeflRad / Deg, 1);
        Assert.Equal(30.0, c.Rudder.LimitFor(-1.0) / Deg, 1);
    }

    /// <summary>An asymmetric stop limits its own side only, and the stick shaping reaches each stop at full travel —
    /// so full aft gives all 34° of up elevator while full forward gives 29° of down, not 34 of each.</summary>
    [Fact]
    public void AnAsymmetricStopLimitsItsOwnSideAndTheStickReachesBoth()
    {
        ControlAxisConfig ele = Load("j3-cub-like").Controls.Elevator;
        Assert.Equal(-34.0 * Deg, ele.ClampDefl(-1.0), 4);
        Assert.Equal(29.0 * Deg, ele.ClampDefl(1.0), 4);
        Assert.Equal(34.0 * Deg, ele.MaxTravelRad, 4);

        // Full stick each way reaches that side's stop (StickForDeflection is the exact inverse of the shaping).
        Assert.Equal(-1.0, Aircraft.StickForDeflection(-34.0 * Deg, ele), 2);
        Assert.Equal(1.0, Aircraft.StickForDeflection(29.0 * Deg, ele), 2);
        // 29° UP is NOT full aft: if the up side were scaled by the 29° stop it would be.
        Assert.True(Math.Abs(Aircraft.StickForDeflection(-29.0 * Deg, ele)) < 0.95);

        // A symmetric axis is untouched by any of this.
        ControlAxisConfig ail = Load("j3-cub-like").Controls.Aileron;
        Assert.Equal(-18.0 * Deg, ail.ClampDefl(-1.0), 4);
        Assert.Equal(18.0 * Deg, ail.ClampDefl(1.0), 4);
        Assert.Equal(1.0, Aircraft.StickForDeflection(18.0 * Deg, ail), 2);
        Assert.Equal(-1.0, Aircraft.StickForDeflection(-18.0 * Deg, ail), 2);
    }
}

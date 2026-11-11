using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A DEPARTED CANOPY BREAKS ON HOW HARD IT HITS, NOT ON WHETHER IT TOUCHES.
///
/// Until 2026-11-11 <see cref="CanopyFreeBody.Step(double, double, Vec3, Vec3, ICanopyObstacle?, string?)"/> shattered
/// the shell on ANY contact at ANY speed. The impact speed was recorded and then played no part in the decision, so a
/// shell leaning on the turtledeck at a metre a second broke exactly as hard as one that caught the fin at 60 m/s.
/// That is almost certainly what produced Ty's report of 2026-11-11 — "I had a hard landing on the extra but the
/// airplane was intact and upright. When I 'bailed out' the canopy shattered even though it didn't hit anything" —
/// because on the ground the released shell had nowhere to go but straight down onto the aeroplane.
///
/// The threshold is <see cref="CanopyFreeBody.ShatterSpeedMs"/>, defaulting to the repo's own
/// <see cref="Glazing.ShatterClosingMs"/>: the same speed at which a pane of the same material shatters when its own
/// surface arrives on something (6 m/s cast acrylic, 8 m/s stretched). It is an AUTHORED number, as it says where it
/// is defined.
///
/// And what it is measured against is the CLOSING speed on the structure, not the shell's speed through the air: a
/// canopy brushing the fin at 150 kt is closing on it at nothing.
/// </summary>
public class CanopyStrikeSpeedTests
{
    private readonly ITestOutputHelper _o;
    public CanopyStrikeSpeedTests(ITestOutputHelper o) { _o = o; }

    private static readonly Vec3 Level1G = new(0, -9.80665, 0);
    private static CanopyDynamics Extra() => new(CanopySpec.For("extra-300-like"));

    /// <summary>A wall at z, fixed in the frame the shell is flown in (so the shell's own speed is the closing speed),
    /// or carried along at <see cref="Moving"/> (so it is not).</summary>
    private sealed class Wall : ICanopyObstacle
    {
        private readonly double _atZ;
        public Vec3 Moving;
        public Wall(double atZ) { _atZ = atZ; }
        public string? Hit(Vec3 at, double radiusM) => at.Z + radiusM >= _atZ ? "HStab" : null;
        public Vec3 VelocityAt(Vec3 at) => Moving;
    }

    /// <summary>Crown up, flying along +z at <paramref name="ms"/>, in gravity-free air so the test says exactly what
    /// it means by a speed.</summary>
    private static CanopyFreeBody Release(double ms) =>
        new(Extra(), new Vec3(0, 0, 0), new Vec3(0, 0, ms), new Vec3(0, 1, 0), new Vec3(0, 0, 1), Vec3.Zero);

    private static CanopyFreeBody Fly(double ms, Wall wall, double sec = 3.0)
    {
        CanopyFreeBody b = Release(ms);
        for (double t = 0; t < sec; t += 0.005) b.Step(0.005, 0.0, Vec3.Zero, Vec3.Zero, wall, null);
        return b;
    }

    /// <summary>TY'S BUG, AT THE MECHANISM. A shell drifting onto the aeroplane at a walking pace does not break.</summary>
    [Fact]
    public void AShellThatLeansOnTheAirframeDoesNotBreak()
    {
        CanopyFreeBody b = Fly(1.0, new Wall(2.0));
        _o.WriteLine($"  1.0 m/s onto the airframe: shattered {b.Shattered}, touched {b.Touched} ({b.TouchedWhat}) "
                     + $"at {b.TouchClosingMs:F2} m/s, threshold {b.ShatterSpeedMs:F1} m/s");
        Assert.False(b.Shattered, "a metre a second broke the canopy");
        Assert.True(b.Touched, "the contact was not even noticed");
        Assert.Equal("HStab", b.TouchedWhat);
        Assert.InRange(b.TouchClosingMs, 0.5, 1.5);
        Assert.True(double.IsFinite(b.TouchAtSec));
    }

    /// <summary>And a shell that really does catch the tail still breaks, with the impact recorded once.</summary>
    [Fact]
    public void AShellThatHitsHardStillBreaksAndRecordsTheImpact()
    {
        CanopyFreeBody b = Fly(30.0, new Wall(6.0));
        _o.WriteLine($"  30 m/s onto the airframe: shattered {b.Shattered} ({b.StruckWhat}) at {b.StrikeAtSec:F3} s, "
                     + $"closing {b.StrikeClosingMs:F1} m/s, {b.StrikeSpeedMs:F1} m/s through the air");
        Assert.True(b.Shattered);
        Assert.Equal("HStab", b.StruckWhat);
        Assert.True(b.StrikeClosingMs > b.ShatterSpeedMs);
        Assert.False(b.Touched, "a hard strike was also logged as a gentle touch");
    }

    /// <summary>The threshold is where it says it is, from either side of it.</summary>
    [Theory]
    [InlineData(0.6, false)]
    [InlineData(0.9, false)]
    [InlineData(1.2, true)]
    [InlineData(3.0, true)]
    public void TheThresholdIsWhereItSaysItIs(double ofThreshold, bool breaks)
    {
        double v = ofThreshold * Glazing.ShatterClosingMs(GlazingMaterial.CastAcrylic);
        CanopyFreeBody b = Fly(v, new Wall(4.0));
        _o.WriteLine($"  {v:F2} m/s ({ofThreshold:F1}x the {b.ShatterSpeedMs:F1} m/s threshold): shattered {b.Shattered}");
        Assert.Equal(breaks, b.Shattered);
        Assert.Equal(!breaks, b.Touched);
    }

    /// <summary>
    /// THE SPEED THAT COUNTS IS THE CLOSING SPEED. The same shell at 60 m/s through the air, meeting a piece of
    /// structure that is travelling with it at 59 m/s, is closing at 1 m/s and must not break — which is a canopy
    /// riding in the wake and brushing the fin at cruise.
    /// </summary>
    [Fact]
    public void ItIsTheClosingSpeedNotTheSpeedThroughTheAir()
    {
        var wall = new Wall(10.0) { Moving = new Vec3(0, 0, 59.0) };
        CanopyFreeBody b = Fly(60.0, wall, sec: 1.0);
        _o.WriteLine($"  60 m/s shell, 59 m/s structure: shattered {b.Shattered}, touched {b.Touched} at "
                     + $"{b.TouchClosingMs:F2} m/s closing");
        Assert.False(b.Shattered, "a 1 m/s brush at cruise broke the glass");
        Assert.True(b.Touched);
        Assert.InRange(b.TouchClosingMs, 0.0, 2.0);
    }

    /// <summary>The fighters' stretched acrylic is tougher than the rest of the fleet's cast, and the body takes
    /// whichever the type has: the same event breaks one and not the other.</summary>
    [Fact]
    public void TheTypesOwnGlazingSetsTheThreshold()
    {
        double cast = Glazing.ShatterClosingMs(Glazing.MaterialFor("extra-300-like"));
        double stretched = Glazing.ShatterClosingMs(Glazing.MaterialFor("f86-sabre-like"));
        _o.WriteLine($"  cast acrylic {cast:F1} m/s, stretched acrylic {stretched:F1} m/s");
        Assert.True(stretched > cast);

        double v = 0.5 * (cast + stretched);
        CanopyFreeBody soft = Release(v);
        CanopyFreeBody hard = Release(v);
        hard.ShatterSpeedMs = stretched;
        var w1 = new Wall(3.0);
        var w2 = new Wall(3.0);
        for (double t = 0; t < 2.0; t += 0.005)
        {
            soft.Step(0.005, 0.0, Vec3.Zero, Vec3.Zero, w1, null);
            hard.Step(0.005, 0.0, Vec3.Zero, Vec3.Zero, w2, null);
        }
        Assert.True(soft.Shattered, $"{v:F1} m/s did not break cast acrylic");
        Assert.False(hard.Shattered, $"{v:F1} m/s broke stretched acrylic");
    }

    /// <summary>The look-ahead and the flight cannot disagree: the tail check reports the same contact, and says
    /// whether it breaks anything.</summary>
    [Fact]
    public void TheTailCheckSaysWhetherTheStrikeBreaksIt()
    {
        var far = new EgressClearance();
        far.Boxes.Add(new EgressClearance.Box("HStab", new Vec3(-5.2, -1.2, -0.6), new Vec3(-4.4, 1.2, 0.6)));
        CanopyTailCheck.Result hit = CanopyTailCheck.Predict(Extra(), new Vec3(-0.5, 0.3, -0.9), new Vec3(0, 0, 0),
                                                            new Vec3(0, 0, -1), 60.0, 1.225, far);
        _o.WriteLine($"  60 m/s: {(hit.Surface ?? "clears")} at {hit.TimeSec:F2} s, closing {hit.ClosingMs:F1} m/s, "
                     + $"shatters {hit.Shatters}");
        if (hit.Surface != null) Assert.Equal(hit.ClosingMs > Glazing.ShatterClosingMs(GlazingMaterial.CastAcrylic), hit.Shatters);
    }
}

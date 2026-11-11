using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A CONTROL SURFACE IS LOST ONE SURFACE AT A TIME, not one SIDE at a time. Ty 2026-11-11, on what a shed surface
/// costs: "It also looses the lift due to reduced wing area for ailerons", and then "And flaps anything in a pair".
///
/// The Pitts has FOUR ailerons — upper and lower on each side (configs: wing-upper-aileron, wing-lower-aileron; the
/// model's own nodes are Aileron_UL/UR/LL/LR) — and AirframeComponent.AileronLeft used to mask every aileron strip
/// with y &lt; 0, so it could only ever lose both of them at once. On the aeroplane Ty actually flies that made the
/// asymmetry he is describing impossible to produce.
///
/// AND THE ASYMMETRY IS GEOMETRIC, WHICH IS WHY THIS IS WORTH DOING THIS WAY. Aircraft.LoseComponent masks the
/// departed surface's own STRIPS, and AeroModel reads that mask, so the lost lift, the rolling moment, the trim
/// change and the altered spanwise distribution all fall out of the existing aerodynamics. Nothing here writes a
/// rolling moment; it writes which strips stopped lifting.
/// </summary>
[Collection("WorldTerrainActive")]
public class PerSurfaceLossTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public PerSurfaceLossTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() => Calm();

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static Aircraft Flying(AircraftConfig c) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(60, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    /// <summary>How many strips this loss stopped, and which surfaces they belonged to.</summary>
    private static (int Count, List<string> Surfaces) Masked(AircraftConfig c, params AirframeComponent[] lose)
    {
        var ac = Flying(c);
        foreach (AirframeComponent comp in lose) ac.LoseComponent(comp);
        bool[]? mask = ac.StripMask;
        var surfaces = new List<string>();
        int n = 0, idx = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig _ in sf.Strips)
            {
                if (mask != null && !mask[idx]) { n++; if (!surfaces.Contains(sf.Id)) surfaces.Add(sf.Id); }
                idx++;
            }
        return (n, surfaces);
    }

    // ---- the Pitts: four ailerons, addressable one at a time -----------------------------------------------------

    [Fact]
    public void ThePittsLosesOneOfItsFourAileronsAtATime()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        (int upperL, List<string> sU) = Masked(c, AirframeComponent.AileronLeft);
        (int lowerL, List<string> sL) = Masked(c, AirframeComponent.AileronLowerLeft);
        (int bothL, _) = Masked(c, AirframeComponent.AileronLeft, AirframeComponent.AileronLowerLeft);
        _o.WriteLine($"Pitts: AileronLeft masks {upperL} strips of [{string.Join(", ", sU)}]; " +
                     $"AileronLowerLeft masks {lowerL} of [{string.Join(", ", sL)}]; both together {bothL}");

        Assert.True(upperL > 0 && lowerL > 0, "each of the two left ailerons must be losable on its own");
        Assert.Equal(new[] { "wing-upper-aileron" }, sU);     // and ONLY the upper one
        Assert.Equal(new[] { "wing-lower-aileron" }, sL);     // and ONLY the lower one
        Assert.Equal(upperL + lowerL, bothL);                 // they are disjoint sets of strips
    }

    /// <summary>And the right-hand pair is independent of the left: four ailerons, four separate losses.</summary>
    [Fact]
    public void AllFourPittsAileronsAreIndependent()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var all = new[] { AirframeComponent.AileronLeft, AirframeComponent.AileronRight,
                          AirframeComponent.AileronLowerLeft, AirframeComponent.AileronLowerRight };
        int sum = 0;
        foreach (AirframeComponent comp in all) sum += Masked(c, comp).Count;
        (int together, _) = Masked(c, all);
        _o.WriteLine($"Pitts: the four ailerons mask {sum} strips singly and {together} together");
        Assert.Equal(sum, together);                          // no overlap anywhere: four distinct surfaces
        Assert.True(together > 3, "a Pitts has four aileron surfaces to lose");
    }

    // ---- a type with ONE aileron surface is unchanged -------------------------------------------------------------

    /// <summary>
    /// THE GREAT LAKES HAS ITS AILERONS ON THE LOWER WING ONLY, which is the case that stops "upper" being a claim
    /// about which wing a surface is bolted to. It has one aileron SURFACE, so AileronLeft still addresses it and
    /// AileronLowerLeft addresses nothing — otherwise its only ailerons would have become unlosable by their own name.
    /// </summary>
    [Theory]
    [InlineData("great-lakes-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    [InlineData("c172-like")]
    public void ATypeWithOneAileronSurfaceIsUnchanged(string id)
    {
        AircraftConfig c = Load(id);
        (int left, List<string> s) = Masked(c, AirframeComponent.AileronLeft);
        (int lower, _) = Masked(c, AirframeComponent.AileronLowerLeft);
        _o.WriteLine($"{id}: AileronLeft masks {left} strips of [{string.Join(", ", s)}]; AileronLowerLeft masks {lower}");
        Assert.True(left > 0, $"{id}: its left aileron must still be losable as AileronLeft");
        Assert.Equal(0, lower);                               // nothing to address: it has no pair
    }

    // ---- losing a surface costs LIFT, which is the point ----------------------------------------------------------

    /// <summary>
    /// "It also looses the lift due to reduced wing area for ailerons." The strips stop contributing, so one
    /// surface gone leaves the aeroplane unbalanced in roll — and it does so because of the geometry, not because
    /// anything here applies a rolling moment.
    ///
    /// THE TEST IS THE MIRROR, NOT THE SIGN, and that is a correction the measurement forced. I first asserted that
    /// losing a LEFT aileron rolls it left, and it does not: the intact Pitts is already rolling -0.96 deg/s at this
    /// condition (its rigged aileron and fin offsets), and its ailerons carry a rigged deflection, so what a surface
    /// contributes at trim is not simply "lift" and the sign of the change is not something to assume. What IS
    /// safe to assert is the asymmetry itself — losing the left surface and losing its mirror on the right must
    /// move the roll rate in OPPOSITE directions by comparable amounts, which is the whole of what "it loses the
    /// lift on one side" means.
    /// </summary>
    [Fact]
    public void LosingOneAileronUnbalancesTheAeroplaneInRoll()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        double Roll(params AirframeComponent[] lose)
        {
            var ac = Flying(c);
            foreach (AirframeComponent comp in lose) ac.LoseComponent(comp);
            new SimLoop(ac).RunFor(1.0, new ControlInputs(0, 0, 0, -0.5));
            return ac.State.Rates.X * 180.0 / Math.PI;
        }
        double intact = Roll();
        double left = Roll(AirframeComponent.AileronLeft) - intact;
        double right = Roll(AirframeComponent.AileronRight) - intact;
        double lowerLeft = Roll(AirframeComponent.AileronLowerLeft) - intact;
        double lowerRight = Roll(AirframeComponent.AileronLowerRight) - intact;
        _o.WriteLine($"Pitts roll rate change after 1 s, stick centred (intact {intact:F2} deg/s): upper L {left:+0.00;-0.00}, " +
                     $"upper R {right:+0.00;-0.00}, lower L {lowerLeft:+0.00;-0.00}, lower R {lowerRight:+0.00;-0.00}");
        Assert.True(Math.Abs(left) > 1e-3, "losing a surface has to change the roll");
        Assert.True(left * right < 0.0, $"the two sides must unbalance it OPPOSITE ways ({left:F3} and {right:F3})");
        Assert.True(lowerLeft * lowerRight < 0.0, $"and so must the lower pair ({lowerLeft:F3} and {lowerRight:F3})");
        // And each side's two ailerons push the same way as each other: they are both on that side.
        Assert.True(left * lowerLeft > 0.0, "both left ailerons unbalance it the same way");
    }

    // ---- the recording format -------------------------------------------------------------------------------------

    /// <summary>
    /// APPENDED, NEVER INSERTED. TrackRecorder writes a lost part into a flight recording as (int)AirframeComponent
    /// and RecordingCodec puts that ordinal on disk, so inserting a member silently reinterprets every .fgrec ever
    /// saved. This pins the ordinals of everything that existed before the lower ailerons were added.
    /// </summary>
    [Fact]
    public void TheEnumOrdinalsOlderRecordingsDependOnHaveNotMoved()
    {
        Assert.Equal(0, (int)AirframeComponent.Fuselage);
        Assert.Equal(2, (int)AirframeComponent.WingLeft);
        Assert.Equal(8, (int)AirframeComponent.Propeller);
        Assert.Equal(19, (int)AirframeComponent.AileronLeft);
        Assert.Equal(20, (int)AirframeComponent.AileronRight);
        Assert.Equal(23, (int)AirframeComponent.Rudder);
        Assert.Equal(27, (int)AirframeComponent.WingTipLowerRight);
        // The new pair is past the end of everything an existing recording can contain.
        Assert.True((int)AirframeComponent.AileronLowerLeft > (int)AirframeComponent.WingTipLowerRight);
        Assert.Equal((int)AirframeComponent.AileronLowerLeft + 1, (int)AirframeComponent.AileronLowerRight);
    }
}

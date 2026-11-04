using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Roll authority, asserted as PHYSICS rather than against a published number (owner, 2026-11-01: "NO fudge
/// factors tuned to published numbers; published roll rates are VALIDATION ONLY").
///
/// The history matters, because two earlier attempts at this were both wrong in the same way. The Pitts rolled
/// 391.6 deg/s at 160 mph IAS against the owner's 260–300. The first fix shrank the aileron TRAVEL from the
/// certificated 25 deg (FAA TCDS A8SO Rev. 21 p. 9) to 16.5 deg, which is less travel than this same model
/// gives a J-3 Cub. The second (4e5e1a7) introduced <c>controls.aileron.effectiveness = 0.66</c> and called it
/// the flap effectiveness τ — but τ is ALREADY in the model, as the fixed row's camber gain g = 0.85·τ(E)
/// (docs/CONTROL-SURFACES.md), so that was counting it twice. Both were the same mistake: a constant chosen to
/// reach a published number, wearing a physical name.
///
/// What was actually wrong was geometry. A deflection cambers the fixed strip ahead of the control, and that
/// camber was applied across the strip's WHOLE span even where the aileron behind it had already ended —
/// rolling moment with no aileron to produce it, and, because it adds roll POWER without adding roll DAMPING,
/// it lands directly on the steady roll rate. Coverage is 1.000 for 28 of the fleet's 32 aileron and elevator
/// systems and those are bit-for-bit unchanged; it is the four surfaces rebuilt from their GLB models on
/// 2026-10-28 that were wrong (Pitts upper wing 0.681, lower 0.719, Extra 300 0.826, Great Lakes 0.877).
///
/// These tests therefore assert the MECHANISM — coverage is measured from the geometry, the finite-span rescale
/// reaches wings that carry 2-D tables, the spare effectiveness hook is still 1.0 and unused — plus wide bands
/// on the resulting rates that would catch a structural change without pinning a tuned number.
/// </summary>
// FLIES AEROPLANES, so it must not run in PARALLEL with a class that installs a world or sets the
// weather: Atmosphere.SteadyWind / ActiveTurbulence / Field and WorldTerrain.Active are process-wide,
// and a thermal arriving under the aeroplane mid-roll changes the rate being measured. Without this
// every case here passed in isolation and failed in the full suite (2026-11-01), including the three
// types this work does not touch at all — which is exactly how that bug always looks.
[Collection("WorldTerrainActive")]
public sealed class PittsRollRateTests
{
    private readonly ITestOutputHelper _out;
    public PittsRollRateTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ---- the mechanism ----

    /// <summary>The spare per-axis hook stays at its default and unused. If an aircraft ever sets it, that is a
    /// deliberate act that has to be justified here — it is NOT τ, which the fixed-row gain already carries.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]
    [InlineData("j3-cub-like")]
    public void TheSpareEffectivenessHookIsOneAndUnused(string id)
    {
        AircraftConfig c = Load(id);
        Assert.Equal(1.0, c.Controls.Aileron.Effectiveness, 9);
        Assert.Equal(1.0, c.Controls.Elevator.Effectiveness, 9);
        Assert.Equal(1.0, c.Controls.Rudder.Effectiveness, 9);
    }

    /// <summary>The Pitts keeps its CERTIFICATED aileron travel. 25 deg ± 2 deg symmetrical, FAA TCDS A8SO
    /// Rev. 21 (15 February 2005) p. 9, Model S-2B — copied here by hand, not read from the file.</summary>
    [Fact]
    public void ThePittsKeepsItsCertificatedAileronTravel()
    {
        Assert.Equal(0.4363, Load("pitts-s2b-like").Controls.Aileron.MaxDeflRad, 4);   // 25.00 deg
        // And the roll rate is not being held down by a fuselage damping fudge either.
        Assert.Equal(0.0, Load("pitts-s2b-like").Fuselage.Damping.P, 9);
    }

    /// <summary>Coverage is MEASURED from each config's own geometry, so it cannot drift from the strips. The
    /// literals are the aggregate aileron span / cambered span of each surface, computed from the configs on
    /// 2026-11-01 — a rebuild of one of these models has to change them deliberately.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", "wing-upper", 0.681)]
    [InlineData("pitts-s2b-like", "wing-lower", 0.719)]
    [InlineData("extra-300-like", "wing", 0.826)]
    [InlineData("great-lakes-like", "wing-lower", 0.874)]
    [InlineData("c172-like", "wing", 1.000)]
    [InlineData("j3-cub-like", "wing", 1.000)]
    public void TheCamberedSpanIsOnlyTheSpanThatHasAnAileronBehindIt(string id, string surfaceId, double expected)
    {
        AircraftConfig c = Load(id);
        // Span-weighted mean coverage over the fixed strips that carry aileron camber.
        double covered = 0, total = 0, idx = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                int i = (int)idx++;
                if (sf.Id != surfaceId || st.Control is null || st.Control.Surface != "aileron"
                    || Math.Abs(st.Control.Gain) >= 0.9 || st.Chord <= 1e-9) continue;
                double w = st.Area / st.Chord;
                total += w;
                covered += w * AeroModel.ControlSpanCoverage(c, i);
            }
        Assert.True(total > 0, $"{id}/{surfaceId}: no fixed strips carry aileron camber");
        _out.WriteLine($"{id}/{surfaceId}: coverage {covered / total:F3} over {total:F2} m of cambered span");
        Assert.Equal(expected, covered / total, 2);
    }

    /// <summary>
    /// These wings carry 2-D section data and are KNOWN to, and the finite-span rescale that the tails have
    /// had since 2026-10-06 is deliberately NOT applied to them. It was built and measured on 2026-11-01 and
    /// then taken back out: Helmbold stretches the alpha axis, so CLmax survives but it takes more incidence to
    /// reach any given lift, and on a taildragger pinned at its ground attitude that lands straight on the
    /// lift-off speed. The Pitts went from 34.0 m/s (76 mph) to 40.2 m/s (90 mph) and the Extra from 38.7 to
    /// 44.7 (87 to 100 mph), against real rotation speeds nearer 60–70 mph — an 18 % move the wrong way on a
    /// number that was checked against the books, bought for a 7 % change in the roll rate that was the point
    /// of the exercise, and in the wrong direction there too (the Pitts went 291 to 311 deg/s).
    ///
    /// The slope is still wrong; it is the configs around it that were authored against it. Re-deriving those
    /// wings' tables, incidence and CLmax together is its own job, and this test exists so the list does not
    /// get lost. See docs/CONTROL-SURFACES.md and the AGENT-LOG entry.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", "wing-upper", 6.30)]
    [InlineData("pitts-s2b-like", "wing-lower", 6.30)]
    [InlineData("great-lakes-like", "wing-upper", 6.17)]
    [InlineData("great-lakes-like", "wing-lower", 6.17)]
    [InlineData("extra-300-like", "wing", 5.82)]
    [InlineData("cassutt-f1-like", "wing", 5.82)]
    [InlineData("glider-swift-s1-like", "wing", 5.82)]
    [InlineData("target-drone-like", "wing", 5.82)]
    [InlineData("boeing-737-like", "wing", 5.53)]
    public void TheseWingsStillCarryTwoDimensionalSectionTables(string id, string surfaceId, double expectedA0)
    {
        AircraftConfig c = Load(id);
        int i = c.Surfaces.FindIndex(s => s.Id == surfaceId);
        Assert.True(i >= 0, $"{id} has no surface {surfaceId}");
        string raw = c.Surfaces[i].Strips[0].Airfoil;
        double a0 = AeroModel.SectionSlope(c.AirfoilTables[raw]);
        _out.WriteLine($"{id}/{surfaceId}: {raw} at {a0:F2}/rad — 2-D, NOT rescaled (see the summary)");
        Assert.Equal(expectedA0, a0, 1);
        Assert.True(a0 >= 5.5, "this case is stale: the table is no longer 2-D");
        // The rescale must stay off for wings until their configs are re-derived with it. A null key is how
        // this model says "use the strip's own airfoil" — i.e. no derived finite-span table was made.
        Assert.Null(AeroModel.SectionTableKey(c, i));
    }

    /// <summary>A wing already on a finite-span polar is left exactly alone — the rescale must not fire twice.</summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("j3-cub-like")]
    [InlineData("glider-2-33-like")]
    public void AWingAlreadyOnAFiniteSpanPolarIsUntouched(string id)
    {
        AircraftConfig c = Load(id);
        int i = c.Surfaces.FindIndex(s => s.Id == "wing");
        string raw = c.Surfaces[i].Strips[0].Airfoil;
        Assert.True(AeroModel.SectionSlope(c.AirfoilTables[raw]) < 5.5);
        Assert.Null(AeroModel.SectionTableKey(c, i));
    }

    // ---- the rates that fall out of it ----

    /// <summary>
    /// Full-aileron steady roll rate. These are WIDE bands around what the physics produces, not the published
    /// figures: they exist to catch a structural change (a lost coverage scale, a damping change, a rebuilt
    /// wing), not to pin a tune. The owner's own figures are recorded beside them as VALIDATION only, and where
    /// the model disagrees that disagreement is written down rather than tuned away.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", 160.0, 218.2, "owner's target 225 (validation only). Was 291.1 before the aileron-section change of 2026-11-02.")]
    [InlineData("extra-300-like", 160.0, 238.8, "published ~340-400, speed not stated: the model is LOW. 30 deg travel sits deep in the plain-flap fall-off; spades not modelled.")]
    [InlineData("great-lakes-like", 160.0, 66.4, "lower-wing ailerons only in this config; no owner figure yet")]
    [InlineData("c172-like", 110.0, 83.4, "owner's typical 60-90")]
    [InlineData("j3-cub-like", 75.0, 53.3, "owner's typical 40-60")]
    [InlineData("glider-2-33-like", 60.0, 14.5, "slow, adverse-yaw dominated")]
    public void TheSteadyRollRateIsWhatThePhysicsGives(string id, double iasMph, double expectedDps, string note)
    {
        (double steady, double to90, double peak) = PittsRoll.Measure(id, iasMph);
        _out.WriteLine($"{id} at {iasMph:F0} mph: {steady:F1} deg/s (recorded {expectedDps:F1}), peak {peak:F1}, {to90:F2} s to 90 %. {note}");
        // +/- 12 %: looser than any plausible integrator or atmosphere noise, tight enough that losing the
        // coverage scale (Pitts +34 %), the aileron-section treatment (Pitts +38 %) or the finite-span rescale (+7 %)
        // fails it. Recorded 2026-11-02 on lane/claude-pitts-spin and re-measured identical 2026-11-03 on lane/claude-island-nav after the merge (tail wakes in) running this class ALONE: ModelRigTests.FullAirBrakeStopsTheGliderSooner... leaves
        // WorldTerrain.Active set and every aeroplane flown after it in the same process goes NaN (pre-existing).
        Assert.InRange(steady, expectedDps * 0.88, expectedDps * 1.12);
    }

    /// <summary>Crispness: an aerobatic type still reaches most of its roll rate at once. The finite-span
    /// rescale LENGTHENED these (the finite-span rescale was not shipped; see TheseWingsStillCarry...) because it removed damping that was never real,
    /// which is the honest direction for that correction.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", 160.0)]
    [InlineData("extra-300-like", 160.0)]
    public void AnAerobaticTypeStillBitesAtOnce(string id, double iasMph)
    {
        (double steady, double to90, _) = PittsRoll.Measure(id, iasMph);
        Assert.True(to90 < 0.8, $"{id} took {to90:F2} s to reach 90 % of {steady:F0} deg/s — that is mushy for an aerobatic type");
    }
}

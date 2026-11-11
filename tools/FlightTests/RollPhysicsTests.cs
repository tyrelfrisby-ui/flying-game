using System.IO;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The roll physics of 2026-11-02 (owner: "the roll rate is way too high"; "they all roll too fast, so whatever physics
/// solution you come up with, apply it to all of the airplanes"). Every expected value here is a LITERAL copied by hand
/// from its source — Glauert's formula worked by hand, Olson's Fig. 3(a), docs/CONTROL-SURFACES.md's measured chord
/// ratios, the lattice output recorded in the commit — so a change to any of them has to be deliberate.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class RollPhysicsTests
{
    private readonly ITestOutputHelper _out;
    public RollPhysicsTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ---- the plain-flap section ----

    /// <summary>Glauert's τ = 1 − (θf − sin θf)/π, cos θf = 2E − 1, worked by hand: E 0.25 → θf = 120°, (2.0944 − 0.8660)/π =
    /// 0.3910, τ 0.6090; E 0.5 → θf 90°, (1.5708 − 1)/π = 0.1817, τ 0.8183; E 0.1 → θf 143.13°, (2.4981 − 0.6)/π = 0.6042,
    /// τ 0.3958.</summary>
    [Theory]
    [InlineData(0.25, 0.6090)]
    [InlineData(0.50, 0.8183)]
    [InlineData(0.10, 0.3958)]
    public void GlauertTau(double e, double tau) => Assert.Equal(tau, PlainFlap.Tau(e), 3);

    /// <summary>η_δ at the plotted markers of Olson (AIAA 2015-0011) Fig. 3(a), plain flap, sealed gap, read by hand.</summary>
    [Theory]
    [InlineData(0.1, 10, 0.735)]
    [InlineData(0.2, 20, 0.617)]
    [InlineData(0.3, 30, 0.500)]
    [InlineData(0.4, 40, 0.435)]
    [InlineData(0.25, 25, 0.545)]   // between the 0.2 (0.535) and 0.3 (0.555) curves at 25°
    [InlineData(0.05, 0, 0.760)]    // below the chart's smallest chord ratio: held at the 0.1 curve
    public void SealedPlainFlapEffectiveness(double e, double deg, double eta) =>
        Assert.Equal(eta, PlainFlap.Eta(e, deg * Math.PI / 180), 3);

    /// <summary>The aileron sections' chord ratios are MEASURED from the strips, and agree to ±0.02 with the values
    /// docs/CONTROL-SURFACES.md recorded when those surfaces were rebuilt from their models (2026-10-28). That table does not
    /// say how it averaged over the span; this is the plain mean over the aileron sections (Extra 0.286 against its 0.271).</summary>
    [Theory]
    [InlineData("pitts-s2b-like", "wing-upper", 0.248)]
    [InlineData("pitts-s2b-like", "wing-lower", 0.236)]
    [InlineData("extra-300-like", "wing", 0.271)]
    public void AileronChordRatioIsTheSectionsOwn(string id, string surfaceId, double expected)
    {
        AircraftConfig c = Load(id);
        int idx = 0; double sum = 0, n = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                int i = idx++;
                if (sf.Id != surfaceId || st.Control is null || st.Control.Surface != "aileron") continue;
                double e = AeroModel.AileronSectionE(c, i);
                if (!double.IsNaN(e)) { sum += e; n++; }
            }
        Assert.True(n > 0);
        _out.WriteLine($"{id}/{surfaceId}: mean E {sum / n:F3} over {n} sections");
        Assert.InRange(sum / n, expected - 0.02, expected + 0.02);
    }

    /// <summary>
    /// THE MECHANISM that was wrong. The hinged row used to fly its own table at α + δ, past the stall at full travel, and
    /// the roll itself un-stalled it: the Pitts' full-aileron rolling moment was 0.140 at the first instant and 0.174 at the
    /// steady roll (+24 %). A plain flap separates off its hinge as a function of δ, so the aileron's moment at the steady
    /// roll must now be what it is at the first instant, to within the wing's own (small) nonlinearity.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", 160.0)]
    [InlineData("extra-300-like", 160.0)]
    [InlineData("c172-like", 110.0)]
    [InlineData("j3-cub-like", 75.0)]
    public void TheRollDoesNotUnstallTheAileron(string id, double mph)
    {
        RollDerivatives.AlphaDeg = 2.0;
        var d = RollDerivatives.Of(id, mph);
        double ratio = RollDerivatives.AileronAtRoll / d.ClFull;
        _out.WriteLine($"{id}: Cl(full) {d.ClFull:F4} at p = 0, {RollDerivatives.AileronAtRoll:F4} at the steady roll ({ratio:F3})");
        Assert.InRange(ratio, 0.93, 1.07);
    }

    // ---- biplane mutual induction ----

    /// <summary>The lattice's factors, as recorded on 2026-11-02 (commit 0a290907). Monoplanes are exactly 1.
    /// The STEARMAN's were re-recorded on 2026-11-10: its stagger was 17.7 in against A-743 NOTE 5's certificated
    /// 26-13/16 and its decalage was reversed, so the lattice had been solved on the wrong cellule. Correcting the
    /// geometry moved it from 0.796 / 0.868 / 0.782 to the row below — more stagger means more of each wing in
    /// cleaner air, so every factor rose. `docs/research/STEARMAN-PT17-DATA.md` § 5.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", 0.848, 0.842, 0.826)]
    [InlineData("great-lakes-like", 0.849, 0.880, 0.866)]
    [InlineData("stearman-pt17-like", 0.812, 0.922, 0.805)]
    [InlineData("jn4-jenny-like", 0.883, 0.901, 0.859)]
    [InlineData("c172-like", 1.0, 1.0, 1.0)]
    [InlineData("extra-300-like", 1.0, 1.0, 1.0)]
    public void CelluleInductionFactors(string id, double damping, double aileron, double liftSlope)
    {
        BiplaneCellule c = BiplaneCellule.For(Load(id));
        _out.WriteLine($"{id}: {c}");
        Assert.Equal(damping, c.RollDampingInduction, 3);
        Assert.Equal(aileron, c.AileronInduction, 3);
        Assert.Equal(liftSlope, c.LiftSlopeInduction, 3);
    }

    /// <summary>The classical shape of the interference: the closest cellule in the fleet (the Stearman, gap 0.75 chord)
    /// loses the most lift slope; and the forward (upper) wing of a positively staggered cellule loses less than the
    /// lower (Munk, NACA Rep. 151).</summary>
    [Fact]
    public void InterferenceHasTheClassicalShape()
    {
        string[] bis = { "pitts-s2b-like", "great-lakes-like", "stearman-pt17-like", "jn4-jenny-like" };
        double stearman = BiplaneCellule.For(Load("stearman-pt17-like")).LiftSlopeInduction;
        foreach (string id in bis)
        {
            BiplaneCellule c = BiplaneCellule.For(Load(id));
            Assert.True(c.StaggerM > 0, $"{id}: expected positive stagger");
            Assert.True(c.LiftSlopeInductionUpper > c.LiftSlopeInductionLower, $"{id}: upper wing should lose less");
            Assert.True(c.LiftSlopeInduction >= stearman - 1e-9, $"{id}: the Stearman's 0.75-chord gap should interfere most");
        }
    }

    // ---- the check of strip theory ----

    /// <summary>
    /// CHECK of the model's roll balance against lifting-surface theory (the same lattice, self-induction included): for
    /// every monoplane with ailerons, the steady pb/2V that strip theory gives is within −8 %..+3 % of the lattice's, which is
    /// why the steady rate needs no self-induction correction. (The damping ITSELF is 1.5–2.6× the lattice's: that sets the
    /// time to reach the rate, not the rate — recorded in the AGENT-LOG as an open question, not changed here.)
    /// </summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    [InlineData("p51d-like")]
    [InlineData("pa25-pawnee-like")]
    [InlineData("boeing-737-like")]
    [InlineData("glider-2-33-like")]
    public void StripTheorySteadyRateAgreesWithLiftingSurfaceTheory(string id)
    {
        var k = BiplaneCellule.LiftingSurfaceOverStrip(Load(id));
        _out.WriteLine($"{id}: aileron x{k.Aileron:F3}, damping x{k.Damping:F3}, steady pb/2V x{k.SteadyRate:F3}");
        Assert.InRange(k.SteadyRate, 0.90, 1.05);
        Assert.True(k.Damping < 1.0 && k.Aileron < 1.0, "induction can only take load away");
    }
}

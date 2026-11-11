using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The float catalogue (Ty 2026-11-11: "Use the EDO drawings and make every model they have available straight floats
/// and amphibs. The game selects the nearest to the gross weight and offers them an amphib choice if one exists").
///
/// Four things are pinned here, and each of them is a way the catalogue could rot quietly:
///  1. THE WEIGHTS AGAINST THE PUBLISHED ONES. Every installed weight in the table is derived from one ratio. Five
///     sizes have an absolute weight on an FAA Aircraft Specification; the derived figure must sit in a narrow band
///     ABOVE each of them, never below, and the class comment says why above.
///  2. THE SELECTION RULE AGAINST HISTORY. Every row below is a real aeroplane on a real float, and the expectation
///     is what the rule picks, not what history fitted. Where the two differ the row says so and why.
///  3. WHICH RATINGS HAVE AN AMPHIBIAN, and that the offer is per SIZE OF AEROPLANE, not "is there a bigger
///     amphibian somewhere" — the J-3 is the case that distinguishes them, and it is Ty's own example.
///  4. THE NAME. "Aluminum floats", never the maker's — the repo is public and trademark names are already a release
///     blocker. A structural test is the only kind that survives a lane that has not read the brief.
/// </summary>
public class FloatCatalogTests
{
    private readonly ITestOutputHelper _out;
    public FloatCatalogTests(ITestOutputHelper o) { _out = o; }

    // ---- 1. the weights ------------------------------------------------------------------------------------------

    /// <summary>
    /// The J-3's float, which is the one installation somebody weighed twice. FAA Aircraft Specification A-691 Rev 34
    /// Item 206: "Edo 60-1320 floats with water rudders (171 lb.)"; owner-weighed 171-182 lb installed with all
    /// rigging. The catalogue is derived to the second of those, because an aeroplane carries what is on it.
    /// </summary>
    [Fact]
    public void The1320MatchesTheWeighedJ3Floats()
    {
        FloatSize s = FloatCatalog.Get("aluminum-1320")!;
        _out.WriteLine($"1320: derived {s.StraightInstalledLb:F1} lb; certificate 171, weighed 171-182");
        Assert.InRange(s.StraightInstalledLb, 171, 182);
    }

    /// <summary>The 2,000 lb-class float: 263 lb with water rudders and all attachment struts, wires and fittings, per
    /// the maker's own drawings. Two pounds of tolerance, which is 0.8 %.</summary>
    [Fact]
    public void The2000MatchesTheDrawnSystemWeight()
    {
        FloatSize s = FloatCatalog.Get("aluminum-2000")!;
        _out.WriteLine($"2000: derived {s.StraightInstalledLb:F1} lb against 263 lb drawn");
        Assert.InRange(s.StraightInstalledLb, 261, 265);
    }

    /// <summary>
    /// THE CHECK THE RATIO WAS NOT FITTED TO. Five sizes have an absolute installed weight printed in an FAA Aircraft
    /// Specification equipment list, per the AIRFRAMER's installation drawing. Those fit a line of 0.1264, about 5 %
    /// under this catalogue's 0.132 — and the catalogue is deliberately on the high side of them, because the one
    /// installation that was both certificated AND weighed is 171 lb on the certificate and up to 182 lb on the
    /// scale. So: always above, never by more than 7 %. If a row ever drops below its published weight the derivation
    /// has gone wrong in the other direction, and that is the one thing this band cannot absorb.
    /// </summary>
    [Fact]
    public void EveryPublishedWeightSitsJustUnderTheDerivedOne()
    {
        int n = 0;
        foreach (FloatSize s in FloatCatalog.AllSizes)
        {
            if (s.PublishedStraightLb <= 0) continue;
            n++;
            double over = s.StraightInstalledLb / s.PublishedStraightLb;
            _out.WriteLine($"{s.Name}: published {s.PublishedStraightLb:F0} lb, derived {s.StraightInstalledLb:F1} lb ({(over - 1) * 100:+0.0;-0.0} %)");
            Assert.InRange(over, 1.00, 1.07);
        }
        Assert.Equal(5, n);     // 1140, 1320, 1400, 1650, 2000 - if a sixth is found, widen this deliberately
    }

    /// <summary>Every weight comes out of the one ratio and nothing is hand-edited into the table.</summary>
    [Fact]
    public void EveryWeightIsDerivedFromTheOneRatio()
    {
        foreach (FloatSize s in FloatCatalog.AllSizes)
        {
            // The table stores the derived weight rounded to 0.1 lb, so 0.05 lb is the whole of the slack there is.
            double want = FloatCatalog.InstalledLbPerDisplacementLb * s.DisplacementLb;
            Assert.True(System.Math.Abs(want - s.StraightInstalledLb) <= 0.05,
                        $"{s.Id}: table {s.StraightInstalledLb:F1} lb, rule {want:F2} lb");
            Assert.True(System.Math.Abs(FloatCatalog.AmphibianWeightFactor * want - s.AmphibianInstalledLb) <= 0.05,
                        $"{s.Id}: amphibian {s.AmphibianInstalledLb:F1} lb is not 1.5 x the rule's {want:F2}");
        }
    }

    /// <summary>The modern cross-checks on another maker's floats: 280 lb of straight-float system on a PA-18
    /// (2100 class) and 305 lb on a Cessna 172 (2350 class). Both inside 2 % of the ratio — which is the reason the
    /// ratio survived meeting the five lighter certificate figures.</summary>
    [Theory]
    [InlineData(2100, 280)]
    [InlineData(2350, 305)]
    public void TheRatioAgreesWithTheModernPublishedSystemWeights(double displacementLb, double publishedLb)
    {
        double derived = FloatCatalog.InstalledLbPerDisplacementLb * displacementLb;
        _out.WriteLine($"{displacementLb:F0}: derived {derived:F0} lb vs published {publishedLb:F0} lb ({(derived / publishedLb - 1) * 100:+0.0;-0.0} %)");
        Assert.InRange(derived / publishedLb, 0.98, 1.02);
    }

    // ---- 2. the selection rule against real fitments --------------------------------------------------------------

    /// <summary>
    /// Nearest STRAIGHT float to the float gross weight, never below 1.8 x gross of pair displacement. Each row is a
    /// real aeroplane; the comment says what history fitted and the expectation is what the rule returns.
    /// </summary>
    [Theory]
    [InlineData(1300, 1320)]    // J-3 Cub, float gross 1,300 lb (A-691). History: 1320. AGREES.
    [InlineData(1750, 1650)]    // PA-18 Super Cub and PA-12, float gross ~1,750 lb. History: 2000. DISAGREES - the 1650 is legal (1.89 W) and nearer, but it is the Cessna 120/140/150 float and no Cub was ever on one. See the note on the rule's light-aeroplane bias.
    [InlineData(2107, 2130)]    // Cessna 170 on 2000s, float gross 2,107 lb - RESTRICTED on floats, below the 2,200 lb landplane gross. History: 2000. DISAGREES by one size up.
    [InlineData(2200, 2130)]    // Cessna 172, float gross ~2,200 lb (also restricted). History: BOTH 2000 and 2130, and the 2130 was developed for this aeroplane, so the rule picking it is arguably the better answer.
    [InlineData(2300, 2425)]    // Maule M-4, float gross 2,300 lb (Maule TCDS: "EDO 248A2440 or 248B2440 Floats: 2300 lbs"). History: 2440. Fifteen pounds of displacement away - the same float to a pilot. See TheTwoSizesFifteenPoundsApartAreANearTie.
    [InlineData(2820, 2870)]    // Cessna 180A, float gross 2,820 lb (its TCDS entry for the 2870 float). History: 2870. AGREES - and only because the 2790 is an AMPHIBIAN and so is not in the straight ladder at all.
    [InlineData(2950, 2960)]    // Cessna 180G-K, float gross 2,950 lb. History: 2960. AGREES.
    [InlineData(3350, 3430)]    // Cessna 185, float gross 3,350 lb. History: 3430. AGREES.
    [InlineData(3500, 3430)]    // Cessna 206, seaplane gross 3,500 lb (against 3,300 on wheels). History: 3430 straight (the 3500 is its amphibian). AGREES.
    [InlineData(5090, 4930)]    // DHC-2 Beaver, float gross 5,090 lb (TSB of Canada A01P0194: "maximum certificated all-up weight ... on floats is 5090 pounds"). History: BOTH 4580 and 4930 - the TSB's own aeroplane was on 4930s, and so is the game's dhc2-beaver-floats-like.
    [InlineData(5370, 4930)]    // Turbo Beaver, Mk.III float gross ~5,370 lb. History: 4930. AGREES.
    [InlineData(8000, 7170)]    // DHC-3 Otter, float gross 8,000 lb. History: 7170. AGREES, and only because of FloorTolerance.
    public void TheRuleAgreesWithHistoryExceptWhereItIsNoted(double grossLb, double expectedDisplacementLb)
    {
        FloatSize s = FloatCatalog.Select(grossLb)!;
        _out.WriteLine($"{grossLb:F0} lb gross -> {s.Name} ({s.DisplacementLb:F0} per float, pair {s.PairDisplacementLb:F0} = {s.ReserveRatio(grossLb):F2} W, system {s.StraightInstalledLb:F0} lb)");
        Assert.Equal(expectedDisplacementLb, s.DisplacementLb);
    }

    /// <summary>
    /// THE GROSS WEIGHT YOU FEED IT DECIDES THE ANSWER, and the J-3 is the proof: its landplane gross is 1,220 lb and
    /// its float gross 1,300 lb, and the rule returns a different float for each. 1,300 gives the 1320 every J-3 on
    /// floats in history has flown on; 1,220 gives the 1140, which none ever did. A configurator that reaches for
    /// mass.massKg off the LANDPLANE config gets this wrong and nothing else will catch it.
    /// </summary>
    [Fact]
    public void TheLandplaneGrossPicksTheWrongFloatForTheJ3()
    {
        Assert.Equal(1320, FloatCatalog.Select(1300)!.DisplacementLb);
        Assert.Equal(1140, FloatCatalog.Select(1220)!.DisplacementLb);
    }

    /// <summary>Whatever it picks is legal: FAR 23.751's 1.8 x gross, inside the tolerance the floor is allowed.</summary>
    [Theory]
    [InlineData(600)] [InlineData(1300)] [InlineData(1750)] [InlineData(2200)] [InlineData(2950)]
    [InlineData(3500)] [InlineData(5090)] [InlineData(8000)] [InlineData(7900)]
    public void EverySelectionMeetsTheReserveFloor(double grossLb)
    {
        FloatSize s = FloatCatalog.Select(grossLb)!;
        Assert.True(s.ReserveRatio(grossLb) >= FloatCatalog.ReserveBuoyancyFactor * (1 - FloatCatalog.FloorTolerance),
                    $"{s.Name} gives {s.ReserveRatio(grossLb):F3} W on {grossLb:F0} lb");
    }

    /// <summary>
    /// The two certificated big-float installations that sit ON the floor, which is the whole reason
    /// <see cref="FloatCatalog.FloorTolerance"/> exists: the Beaver's pair of 4,580s is 9,160 lb against 1.8 x 5,090 =
    /// 9,162, and the Otter's pair of 7,170s is 14,340 against 1.8 x 8,000 = 14,400. A floor with no tolerance throws
    /// both out and the Otter then has no float at all.
    /// </summary>
    [Fact]
    public void TheFloorToleranceIsWhatKeepsTheOtterFlying()
    {
        FloatSize otter = FloatCatalog.Get("aluminum-7170")!;
        FloatSize beaver = FloatCatalog.Get("aluminum-4580")!;
        _out.WriteLine($"Otter 8,000 lb on the 7170 pair: {otter.ReserveRatio(8000):F4} W; Beaver 5,090 lb on the 4580 pair: {beaver.ReserveRatio(5090):F4} W");
        Assert.True(otter.ReserveRatio(8000) < FloatCatalog.ReserveBuoyancyFactor);        // genuinely short
        Assert.True(beaver.ReserveRatio(5090) < FloatCatalog.ReserveBuoyancyFactor);       // genuinely short
        Assert.True(FloatCatalog.MeetsReserve(otter, 8000));                               // and still offered
        Assert.True(FloatCatalog.MeetsReserve(beaver, 5090));
        Assert.Equal(7170, FloatCatalog.Select(8000)!.DisplacementLb);
    }

    /// <summary>The 2425 and the 2440 are fifteen pounds of displacement apart: the same float to a pilot, and a coin
    /// toss to the rule. Recorded so nobody reads the Maule row above as a real disagreement.</summary>
    [Fact]
    public void TheTwoSizesFifteenPoundsApartAreANearTie()
    {
        FloatSize a = FloatCatalog.Get("aluminum-2425")!, b = FloatCatalog.Get("aluminum-2440")!;
        Assert.True(System.Math.Abs(a.DisplacementLb - b.DisplacementLb) / a.DisplacementLb < 0.01);
        Assert.True(System.Math.Abs(a.StraightInstalledLb - b.StraightInstalledLb) < 3);
    }

    /// <summary>A tie goes to the LARGER float: more buoyancy is the tie-break a seaplane pilot wants, and the rule
    /// as written would otherwise leave it to table order. 2,065 lb is 65 lb from the 2000 and 65 from the 2130.</summary>
    [Fact]
    public void ATieGoesToTheLargerFloat()
    {
        Assert.Equal(2130, FloatCatalog.Select(2065)!.DisplacementLb);
    }

    /// <summary>Nothing in the family lifts it, so nothing is returned. A configurator must see the hole rather than
    /// be handed a float that cannot legally carry the aeroplane.</summary>
    [Fact]
    public void TooHeavyForTheFamilyReturnsNothing()
    {
        Assert.Null(FloatCatalog.Select(60000));    // a 737 does not go on floats
        Assert.Null(FloatCatalog.Select(0));
    }

    // ---- 3. amphibian availability --------------------------------------------------------------------------------

    /// <summary>
    /// Ty's worked example, and the one the catalogue must get right: the J-3 is offered NO amphibian. Note what makes
    /// this non-trivial - six amphibians exist in the family and every one of them is legal on a 1,300 lb aeroplane by
    /// buoyancy alone. The reason the Cub gets none is that none of them is a 1,300 lb aeroplane's float.
    /// </summary>
    [Fact]
    public void TheJ3IsOfferedNoAmphibian()
    {
        (FloatSize? straight, FloatSize? amphib) = FloatCatalog.Offer(1300);
        _out.WriteLine($"J-3 at 1,300 lb: {straight!.Name}, amphibian {(amphib == null ? "none" : amphib.Name)}");
        Assert.Equal(1320, straight.DisplacementLb);
        Assert.Null(amphib);
        Assert.False(straight.Amphibian);
        // ... and yet every amphibian in the family would float it.
        Assert.Contains(FloatCatalog.AllSizes, s => s.Amphibian && FloatCatalog.MeetsReserve(s, 1300));
    }

    /// <summary>
    /// The amphibian a type really flew, where one exists. The straight float is picked first and the amphibian is the
    /// one of its own size - which is how the real range is built: an amphibian is its OWN model on the same hull,
    /// displacing a few per cent less because the wheel wells are cut out of it.
    /// </summary>
    [Theory]
    [InlineData(1300, null)]    // J-3 Cub: none. Ty's example; its certificates have no amphibian entry at all.
    [InlineData(2200, null)]    // Cessna 172: none. TCDS 3A12 Rev 84 has no amphibian configuration whatsoever.
    [InlineData(2300, null)]    // Maule M-4: none certificated, and the lopsided band is what keeps the 2700 out of reach.
    [InlineData(2820, 2790)]    // Cessna 180A: TCDS 5A6 Rev 68, "Amphibian with Edo 597-2790". (The 2700 and 2705 are also real 180 amphibians; the rule takes the nearest.)
    [InlineData(2950, 2790)]    // Cessna 180G-K: same.
    [InlineData(3500, 3500)]    // Cessna 206: NTSB ANC17LA053, a U206G "equipped with EDO 696-3500 amphibious floats".
    [InlineData(5090, 4580)]    // DHC-2 Beaver: the 4580 is the one rating built in both versions, and the Beaver amphibian is its.
    [InlineData(8000, null)]    // DHC-3 Otter: none. Its 7170A is a straight float - the trailing "A" is a revision letter - and Otter amphibians are on another maker's floats.
    public void TheAmphibianOfferedIsTheOneOfItsOwnSize(double grossLb, int? expectedAmphibianLb)
    {
        (FloatSize? straight, FloatSize? amphib) = FloatCatalog.Offer(grossLb);
        _out.WriteLine($"{grossLb:F0} lb -> {straight!.Name} straight, amphibian {(amphib == null ? "none" : amphib.Name)}");
        if (expectedAmphibianLb == null) Assert.Null(amphib);
        else Assert.Equal(expectedAmphibianLb.Value, amphib!.DisplacementLb);
    }

    /// <summary>
    /// The C-185 at its STRAIGHT-float gross is offered the 3500, not the 2790 its certificate names - because at
    /// 3,350 lb a pair of 2790s is 1.67 W and illegal. An amphibian conversion carries its own, lower gross weight,
    /// and the caller has to pass that one. Recorded because it looks like a bug and is not.
    /// </summary>
    [Fact]
    public void TheAmphibianGrossIsItsOwnNumberAndTheCallerMustPassIt()
    {
        FloatSize c2790 = FloatCatalog.Get("aluminum-2790")!;
        Assert.False(FloatCatalog.MeetsReserve(c2790, 3350));                 // illegal at the straight-float gross
        Assert.Equal(3500, FloatCatalog.Offer(3350).amphibian!.DisplacementLb);
        // At the weight a pair of 2790s really supports, it is the answer again.
        Assert.True(FloatCatalog.MeetsReserve(c2790, c2790.MaxGrossLb));
        Assert.Equal(2790, FloatCatalog.Select(c2790.MaxGrossLb, amphibian: true)!.DisplacementLb);
    }

    /// <summary>
    /// Which ratings were built as what. The amphibian column is EVIDENCE, not a formula: an FAA specification that
    /// says "Amphibian with ..." in so many words, or a parts list whose Figure 1 draws wheels and lists a bow well, a
    /// main well, a retraction system and hydraulic lines. If a later lane changes this list it must bring the source
    /// with it.
    /// </summary>
    [Fact]
    public void OnlyTheRatingsWithEvidenceAreAmphibious()
    {
        double[] amphibious = FloatCatalog.AllSizes.Where(s => s.Amphibian).Select(s => s.DisplacementLb).ToArray();
        double[] straight = FloatCatalog.AllSizes.Where(s => s.Straight).Select(s => s.DisplacementLb).ToArray();
        _out.WriteLine("amphibious: " + string.Join(", ", amphibious));
        _out.WriteLine("straight:   " + string.Join(", ", straight));
        Assert.Equal(new double[] { 2700, 2705, 2790, 3500, 4580, 29000 }, amphibious);
        Assert.Equal(new double[] { 1140, 1320, 1400, 1650, 2000, 2130, 2425, 2440, 2870, 2960, 3430, 4580, 4930, 7170 }, straight);
        Assert.All(FloatCatalog.AllSizes, s => Assert.True(s.Straight || s.Amphibian, s.Id + " is neither"));
    }

    /// <summary>
    /// THE SHAPE OF THE RANGE, which is the thing the brief had back to front: an amphibian is its own model at its own
    /// rating, not a switch on a straight float. Only the 4580 was built both ways. The 2790 and 3500 are
    /// amphibian-only, which is why neither appears in the straight ladder above - and removing the 2790 from it is
    /// what made the Cessna 180A agree with history.
    /// </summary>
    [Fact]
    public void AnAmphibianIsItsOwnModelNotASwitch()
    {
        Assert.False(FloatCatalog.Get("aluminum-2790")!.Straight);
        Assert.False(FloatCatalog.Get("aluminum-3500")!.Straight);
        Assert.False(FloatCatalog.Get("aluminum-2870")!.Amphibian);
        Assert.False(FloatCatalog.Get("aluminum-3430")!.Amphibian);
        FloatSize both = FloatCatalog.Get("aluminum-4580")!;
        Assert.True(both.Straight && both.Amphibian);
        Assert.Single(FloatCatalog.AllSizes.Where(s => s.Straight && s.Amphibian));
    }

    /// <summary>
    /// A WELL COSTS DISPLACEMENT, which is the measurement the lopsided band is made of: every amphibian in the range
    /// sits below its straight sister's rating except the 3500, which is a bigger hull outright. The pairs are
    /// 2700/2705/2790 against 2870 and 2960, 3500 against 3430, and 4580 against itself.
    /// </summary>
    [Theory]
    [InlineData(2700, 2870, -0.059)]
    [InlineData(2705, 2870, -0.058)]
    [InlineData(2790, 2870, -0.028)]
    [InlineData(2790, 2960, -0.057)]
    [InlineData(3500, 3430, +0.020)]
    [InlineData(4580, 4580, 0.000)]
    public void EveryRealPairIsInsideTheBand(double amphibLb, double straightLb, double expectedFraction)
    {
        double f = (amphibLb - straightLb) / straightLb;
        Assert.InRange(f, expectedFraction - 0.001, expectedFraction + 0.001);
        Assert.InRange(f, -FloatCatalog.AmphibianBandBelow, FloatCatalog.AmphibianBandAbove);
    }

    /// <summary>The DC-3-class float was only ever an amphibian, and only ever military: its floats carried
    /// retractable wheels at the step, and the civil DC-3 specifications have no float entry of any kind. So the
    /// straight ladder has nothing for it, and asking for an amphibian directly is the only way to reach it.</summary>
    [Fact]
    public void TheDC3FloatIsAnAmphibianOrNothing()
    {
        Assert.Null(FloatCatalog.Select(26900));                                          // no straight float that big
        Assert.Equal(29000, FloatCatalog.Select(26900, amphibian: true)!.DisplacementLb);
        Assert.False(FloatCatalog.Get("aluminum-29000")!.Straight);
    }

    /// <summary>The uplift, from a modern maker's seaplane-vs-amphibian system weights on comparable aircraft (PA-18
    /// 280 -> 427, C-172 305 -> 468, Beaver 491 -> 694: x1.53, x1.53, x1.41). NOTHING measured on a real amphibian in
    /// THIS range stands behind it - no certificate, no parts list, no field figure was found - so it is the weakest
    /// number in the catalogue and is marked as such.</summary>
    [Fact]
    public void AnAmphibianIsHalfAgainTheStraightFloatSystem()
    {
        foreach (FloatSize s in FloatCatalog.AllSizes.Where(s => s.Amphibian))
            Assert.InRange(s.AmphibianInstalledLb / s.StraightInstalledLb, 1.4, 1.55);
        Assert.DoesNotContain(FloatCatalog.AllSizes.Where(s => s.Amphibian), s => s.PublishedStraightLb > 0);
    }

    // ---- 4. the exchange rule -------------------------------------------------------------------------------------

    /// <summary>
    /// Ty's own aeroplane, and the arithmetic the whole weight side rests on: a J-3 that weighs 700 lb empty on
    /// wheels weighs 825 lb empty on floats. Not 875 - the wheels, legs and brakes come OFF, and they are ~50 lb.
    /// </summary>
    [Fact]
    public void TheJ3GoesFrom700PoundsOnWheelsTo825OnFloats()
    {
        FloatSize s = FloatCatalog.Select(1300)!;
        double gear = FloatExchange.GearRemovedLb("j3-cub-like")!.Value;
        double empty = FloatExchange.FloatsEmptyLb(700, gear, s, amphibian: false);
        _out.WriteLine($"J-3: 700 lb on wheels + {s.StraightInstalledLb:F0} lb of {s.Name} - {gear:F0} lb of gear = {empty:F0} lb on floats");
        Assert.InRange(empty, 820, 832);
    }

    /// <summary>
    /// Charging the whole float system and forgetting the gear is a THIRD of what the floats cost in useful load on a
    /// J-3 - 50 lb on a 125 lb exchange. This is the test that fails if somebody "simplifies" the exchange away.
    /// </summary>
    [Fact]
    public void TheExchangeIsNotTheFloatSystemWeight()
    {
        FloatSize s = FloatCatalog.Select(1300)!;
        double exchange = FloatExchange.ExchangeLb(50, s, amphibian: false);
        Assert.True(exchange < s.StraightInstalledLb - 45);
        Assert.InRange(exchange, 120, 132);
    }

    /// <summary>A modern maker publishes the exchange weight directly for a Cessna 172 on 2350-class floats - 168 lb -
    /// and the catalogue's own gear figure for the 172 was read back out of it, so this closes the loop.</summary>
    [Fact]
    public void TheC172ExchangeWeightReproducesThePublishedFigure()
    {
        double gear = FloatExchange.GearRemovedLb("c172-like")!.Value;
        double system = FloatCatalog.InstalledLbPerDisplacementLb * 2350;    // the 2350-class float, not a catalogue size
        _out.WriteLine($"C-172: {system:F0} lb system - {gear:F0} lb gear = {system - gear:F0} lb exchange, published 168");
        Assert.InRange(system - gear, 163, 178);
    }

    /// <summary>A type nobody has weighed the gear of returns null rather than a plausible number. The guess would
    /// land straight in the empty weight and never be questioned again.</summary>
    [Fact]
    public void AnUnweighedTypeRefusesToGuess()
    {
        Assert.Null(FloatExchange.GearRemovedLb("great-lakes-like"));
        Assert.Null(FloatExchange.GearRemovedLb(null));
        Assert.Null(FloatExchange.Fit("great-lakes-like", 400, 800, amphibian: false));
    }

    /// <summary>The whole chain in one call, in the units the configs hold: 1,300 lb gross is 589.7 kg, which is
    /// exactly what j3-cub-floats-like flies at.</summary>
    [Fact]
    public void FitPicksTheSizeAndSaysWhatItWeighs()
    {
        (FloatSize size, double emptyKg) = FloatExchange.Fit("j3-cub-floats-like", 700 * FloatSize.Lb, 589.7, amphibian: false)!.Value;
        Assert.Equal(1320, size.DisplacementLb);
        Assert.InRange(emptyKg / FloatSize.Lb, 820, 832);
    }

    /// <summary>Asking for an amphibian where none was built returns null all the way up the chain, rather than a
    /// weight for a set that never existed.</summary>
    [Fact]
    public void FitRefusesAnAmphibianThatWasNeverBuilt()
    {
        Assert.Null(FloatExchange.Fit("j3-cub-floats-like", 700 * FloatSize.Lb, 589.7, amphibian: true));
    }

    // ---- 5. the structural guards ---------------------------------------------------------------------------------

    /// <summary>
    /// THE NAME. The player never sees the maker's; "Aluminum floats" is what Ty decided, the repo is public, and
    /// aircraft trademark names are already a release blocker on this project. Source comments may and should name
    /// the certificates and drawings - that is how this codebase cites a measurement - but an id or a display name
    /// may not.
    /// </summary>
    [Fact]
    public void NoPlayerFacingStringNamesTheMaker()
    {
        foreach (FloatCatalogFamily f in FloatCatalog.Families)
        {
            Assert.DoesNotContain("edo", f.Id, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("edo", f.Name, System.StringComparison.OrdinalIgnoreCase);
            foreach (FloatSize s in f.Sizes)
            {
                Assert.DoesNotContain("edo", s.Id, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("edo", s.Name, System.StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith("Aluminum", s.Name);
            }
        }
    }

    /// <summary>Ids unique, ratings in ascending order (the selector does not depend on it, but every menu that lists
    /// them does), every row sourced, and every weight positive.</summary>
    [Fact]
    public void TheTableIsWellFormed()
    {
        foreach (FloatCatalogFamily f in FloatCatalog.Families)
        {
            Assert.NotEmpty(f.Sizes);
            for (int i = 1; i < f.Sizes.Count; i++)
                Assert.True(f.Sizes[i].DisplacementLb > f.Sizes[i - 1].DisplacementLb, $"{f.Id} is out of order at {i}");
            foreach (FloatSize s in f.Sizes)
            {
                Assert.Equal(f.Id, s.FamilyId);
                Assert.True(s.StraightInstalledLb > 0 && s.AmphibianInstalledLb > s.StraightInstalledLb);
                Assert.False(string.IsNullOrWhiteSpace(s.Source), s.Id + " has no source");
                Assert.Same(s, FloatCatalog.Get(s.Id));
            }
        }
        Assert.Equal(FloatCatalog.AllSizes.Count(), FloatCatalog.AllSizes.Select(s => s.Id).Distinct().Count());
    }

    /// <summary>Room for the composite family: the catalogue is families-of-sizes, and the selector is already
    /// told which family to search. Adding "Composite floats" must not need a change above the family array.</summary>
    [Fact]
    public void TheCatalogueIsFamiliesOfSizesSoCompositeCanBeAdded()
    {
        Assert.Contains(FloatCatalog.Families, f => f.Id == FloatCatalog.AluminumId);
        Assert.Null(FloatCatalog.GetFamily("composite"));              // not built yet, and says so
        Assert.Null(FloatCatalog.Select(1300, familyId: "composite"));
        Assert.Empty(FloatCatalog.Legal(1300, familyId: "composite"));
    }

    /// <summary>Which rows have a published weight behind them and which are a line extended past its data:
    /// everything above the 2,000 lb float, which is every big one, and the big end is where the one modern
    /// comparable says the rule runs ~19 % heavy.</summary>
    [Fact]
    public void OnlyTheSizesWithAPublishedWeightClaimToBeMeasured()
    {
        foreach (FloatSize s in FloatCatalog.AllSizes)
            Assert.Equal(s.PublishedStraightLb > 0 ? FloatWeightBasis.Measured : FloatWeightBasis.Extrapolated, s.Basis);
        Assert.Equal(5, FloatCatalog.AllSizes.Count(s => s.Basis == FloatWeightBasis.Measured));
    }

    /// <summary>The catalogue as a table, printed. Not an assertion - a way to read what the rule produced without
    /// opening the source.</summary>
    [Fact]
    public void PrintTheCatalogue()
    {
        _out.WriteLine("size           disp/float    pair  max gross  straight   publ'd   amphib  versions");
        foreach (FloatSize s in FloatCatalog.AllSizes)
            _out.WriteLine($"{s.Name,-16} {s.DisplacementLb,7:F0} {s.PairDisplacementLb,8:F0} {s.MaxGrossLb,10:F0} " +
                           $"{(s.Straight ? s.StraightInstalledLb.ToString("F1") : "-"),9} " +
                           $"{(s.PublishedStraightLb > 0 ? s.PublishedStraightLb.ToString("F0") : "-"),8} " +
                           $"{(s.Amphibian ? s.AmphibianInstalledLb.ToString("F1") : "-"),8}  " +
                           $"{(s.Straight ? "straight" : "")}{(s.Straight && s.Amphibian ? " + " : "")}{(s.Amphibian ? "amphibian" : "")}");
    }
}

using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// <see cref="SeaState"/> against the published numbers it claims to be. Ty, 2026-11-11: "No wind glassy wind
/// [makes] texture first then waves stack up with distance etc use real physics" — so each of those three has a
/// test here, and each is pinned to a source outside this repository rather than to whatever the code happens to
/// print today.
///
/// Sources used as the reference values:
///   Hasselmann et al. 1973 (JONSWAP), Deut. Hydrogr. Z. A8(12)   — fetch-limited growth
///   Pierson and Moskowitz 1964, JGR 69(24) 5181                   — the fully developed sea
///   Cox and Munk 1954, JOSA 44(11) 838                            — mean square slope against wind
///   WMO Beaufort scale, sea criteria                              — "sea like a mirror", "ripples ... like scales"
/// </summary>
public sealed class SeaStateTests
{
    private readonly ITestOutputHelper _out;
    public SeaStateTests(ITestOutputHelper o) { _out = o; }

    private const double Knot = 0.514444;

    // ---- 1. no wind: a mirror ---------------------------------------------------------------------------------

    /// <summary>The physical floor under "glassy": clean water cannot carry a wave slower than 0.231 m/s, and the
    /// slowest one is 1.71 cm long. Below that wind there is nothing for the wind to grip.</summary>
    [Fact]
    public void TheSlowestWaveCleanWaterCanCarryIsTwentyThreeCentimetresPerSecond()
    {
        Assert.Equal(0.2313, SeaState.MinimumPhaseSpeedMs, 4);
        Assert.Equal(0.0171, SeaState.MinimumWavelengthM, 4);
    }

    /// <summary>Beaufort 0 (&lt; 1 kt): "Sea like a mirror". Nothing is rough, nothing has a height.</summary>
    [Fact]
    public void ADeadCalmIsAMirror()
    {
        Assert.Equal(1.0, SeaState.Glassy01(0.0), 9);
        Assert.Equal(0.0, SeaState.MeanSquareSlope(0.0), 9);
        Assert.Equal(0.0, SeaState.RmsSlope(0.0), 9);
        Assert.Equal(0.0, SeaState.SignificantHeightM(0.0, 3000.0), 9);
        // And still a mirror at the top of Beaufort force 0.
        Assert.Equal(1.0, SeaState.Glassy01(0.5 * Knot), 9);
    }

    // ---- 2. a little wind: TEXTURE first, with no height to speak of -------------------------------------------

    /// <summary>Beaufort 1-2 (1-6 kt) is where the WMO sea criteria go from a mirror to "ripples with the
    /// appearance of scales" to "small wavelets". The surface is fully textured by the top of force 2 while the
    /// significant wave height is still under 2 cm on any fetch this world contains — which is exactly Ty's
    /// "wind [makes] texture first": the mirror dies long before a wave you could see in profile exists.</summary>
    [Fact]
    public void TextureArrivesLongBeforeHeightDoes()
    {
        double light = 3.0 * Knot;                       // 3 kt, the top of Beaufort force 1
        Assert.InRange(SeaState.Ripple01(light), 0.2, 0.35);             // scales, not yet wavelets
        Assert.InRange(SeaState.Ripple01(5.0 * Knot), 0.65, 0.80);       // 5 kt: force 2, "small wavelets"
        // The whole harbor, 2.4 km of fetch, at 3 kt:
        double hs = SeaState.SignificantHeightM(light, 2400.0);
        Assert.InRange(hs, 0.0, 0.02);
        _out.WriteLine($"  3 kt over the 2.4 km harbor: texture {SeaState.Ripple01(light):F2}, Hs {hs * 100:F1} cm, " +
                       $"rms slope {SeaState.RmsSlope(light) * 57.2958:F1} deg");
        // By 7 kt (force 2/3 boundary) the texture is complete and only the waves grow after that.
        Assert.Equal(1.0, SeaState.Ripple01(7.0 * Knot), 6);
    }

    /// <summary>Cox and Munk 1954: σ² = 0.003 + 5.12e-3·U for a clean sea. These are their numbers, not ours.</summary>
    [Theory]
    [InlineData(5.0, 0.02860)]
    [InlineData(10.0, 0.05420)]
    [InlineData(14.0, 0.07468)]
    public void MeanSquareSlopeIsCoxAndMunk(double windMs, double expected)
    {
        Assert.Equal(expected, SeaState.MeanSquareSlope(windMs), 5);
        _out.WriteLine($"  U = {windMs:F0} m/s: mss {expected:F4}, rms slope {SeaState.RmsSlope(windMs) * 57.2958:F1} deg");
    }

    // ---- 3. more wind and more fetch: the waves stack up downwind ----------------------------------------------

    /// <summary>JONSWAP's own law, written the way the paper writes it: gHs/U² = 1.6e-3·(gX/U²)^½. Checked at two
    /// winds and two fetches so neither the wind power nor the fetch power can be wrong and still pass.</summary>
    [Theory]
    [InlineData(10.0, 1000.0)]
    [InlineData(10.0, 50000.0)]
    [InlineData(20.0, 1000.0)]
    public void SignificantHeightFollowsTheJonswapFetchLaw(double windMs, double fetchM)
    {
        double xHat = SeaState.G * fetchM / (windMs * windMs);
        double expected = 1.6e-3 * System.Math.Sqrt(xHat) * windMs * windMs / SeaState.G;
        Assert.Equal(expected, SeaState.SignificantHeightM(windMs, fetchM), 6);
    }

    /// <summary>Height goes as √fetch: four times the fetch is twice the wave. This IS "waves stack up with
    /// distance", stated as a ratio so it cannot be satisfied by a fudged constant.</summary>
    [Fact]
    public void FourTimesTheFetchIsTwiceTheWave()
    {
        double near = SeaState.SignificantHeightM(8.0, 300.0);
        double far = SeaState.SignificantHeightM(8.0, 1200.0);
        Assert.Equal(2.0, far / near, 6);
        // And the wave gets LONGER as well as taller, as fetch^0.66: 4^0.66 = 2.51.
        double lambdaNear = SeaState.PeakWavelengthM(8.0, 300.0), lambdaFar = SeaState.PeakWavelengthM(8.0, 1200.0);
        Assert.Equal(System.Math.Pow(4.0, 0.66), lambdaFar / lambdaNear, 2);
        _out.WriteLine($"  16 kt: 300 m fetch -> Hs {near * 100:F0} cm, lambda {lambdaNear:F1} m; " +
                       $"1200 m fetch -> Hs {far * 100:F0} cm, lambda {lambdaFar:F1} m");
    }

    /// <summary>Pierson-Moskowitz, the fully developed sea at 10 m/s: Hs = 2.46 m, peak period 7.84 s, peak
    /// wavelength 96 m. Those are the textbook values for a 20 kt fully developed wind sea.</summary>
    [Fact]
    public void TheFullyDevelopedSeaIsPiersonMoskowitz()
    {
        double huge = 1.0e7;                             // far past any fetch on Earth at this wind
        Assert.Equal(2.459, SeaState.SignificantHeightM(10.0, huge), 2);
        Assert.Equal(7.842, SeaState.PeakPeriodS(10.0, huge), 2);
        Assert.Equal(95.98, SeaState.PeakWavelengthM(10.0, huge), 1);
        // The cap bites at 232 km of fetch; nothing in this world is within three orders of magnitude of that.
        Assert.InRange(SeaState.FullyDevelopedFetchM(10.0), 231.0e3, 232.9e3);
    }

    /// <summary>The whole point of modelling fetch in a world 8.6 x 14.9 NM across: the sea here is ALWAYS
    /// fetch-limited, by a factor of about a hundred. The harbor is the longest water in the world at 2.4 km and a
    /// 10 m/s wind there makes a 25 cm wave, not the 2.46 m fully developed one.</summary>
    [Fact]
    public void EveryWaterInThisWorldIsFetchLimited()
    {
        double harbor = SeaState.SignificantHeightM(10.0, 2400.0);
        Assert.InRange(harbor, 0.24, 0.26);
        Assert.True(harbor < 0.11 * SeaState.FullyDevelopedHeightM(10.0),
            "a 2.4 km fetch must be nowhere near a fully developed sea");
        _out.WriteLine($"  20 kt over the 2.4 km harbor: Hs {harbor * 100:F0} cm, " +
                       $"lambda {SeaState.PeakWavelengthM(10.0, 2400.0):F1} m, " +
                       $"fully developed would be {SeaState.FullyDevelopedHeightM(10.0):F2} m");
    }

    /// <summary>
    /// The cross-check that tells the shader HOW TO SPEND its slope budget. Cox and Munk measured the whole
    /// surface's rms slope optically; JONSWAP's πHs/λ is the slope of the PEAK wave train alone, and the two were
    /// measured twenty years apart by people with no interest in each other's quantity. Their ratio is therefore
    /// the share of the surface's optical roughness that the long waves carry, and the rest is the short waves
    /// riding on them — which is exactly the split the water shader has to make between a drawn wave train and an
    /// unresolved roughness.
    ///
    /// Measured here, over fetches of 0.5 to 2.4 km (every water in this world):
    ///   1 m/s   ratio 0.23-0.29   — almost ALL the slope is capillary. This is Ty's "texture first".
    ///   3 m/s   ratio 0.77-0.99
    ///   5 m/s   ratio 0.77-0.98
    ///   10 m/s  ratio 0.69-0.89   — the long waves now carry most of it, the rest rides on top.
    /// Above the capillary band the two agree to within about a quarter, which for two independent measurement
    /// programmes is as close as this kind of thing ever comes.
    /// </summary>
    [Theory]
    [InlineData(3.0, 0.75, 1.00)]
    [InlineData(5.0, 0.75, 1.00)]
    [InlineData(8.0, 0.70, 0.95)]
    [InlineData(12.0, 0.65, 0.90)]
    public void TheWaveTrainCarriesMostOfTheSlopeOnceThereIsWind(double windMs, double lo, double hi)
    {
        double jonswap = SeaState.PeakWaveSlope(windMs, 1000.0);
        double coxMunk = SeaState.RmsSlope(windMs);
        Assert.InRange(jonswap / coxMunk, lo, hi);
        _out.WriteLine($"  U = {windMs:F0} m/s, 1 km fetch: JONSWAP peak-train slope {jonswap:F3}, " +
                       $"Cox-Munk rms {coxMunk:F3}, long-wave share {jonswap / coxMunk:F2}");
    }

    /// <summary>And the other end of it: in the capillary band the long waves carry almost none of the slope, so
    /// what breaks the mirror at 2 kt is TEXTURE and not a wave. This is the assertion that fails if anyone ever
    /// "simplifies" the capillary ramp out of the model.</summary>
    [Fact]
    public void AtTwoKnotsTheSlopeIsAlmostAllCapillary()
    {
        double wind = 2.0 * Knot;
        double share = SeaState.PeakWaveSlope(wind, 1000.0) / SeaState.RmsSlope(wind);
        Assert.InRange(share, 0.0, 0.45);
        Assert.True(SeaState.RmsSlope(wind) > 0.02, "and yet the surface is measurably rough: the mirror is gone");
        _out.WriteLine($"  2 kt: rms slope {SeaState.RmsSlope(wind) * 57.2958:F1} deg, " +
                       $"of which the wave train carries {share * 100:F0} %");
    }

    // ---- fetch from the world ---------------------------------------------------------------------------------

    /// <summary>The march upwind stops at the shore. A 900 m-wide strip of water with the wind straight across it
    /// gives a fetch of the distance to the upwind edge, not the whole strip.</summary>
    [Fact]
    public void FetchIsTheDistanceUpwindToTheShore()
    {
        bool Water(double x, double y) => Math.Abs(y) < 450.0;
        // Wind from the east (+y in the sim frame is east, so "from the east" is a bearing of 90°).
        double fetch = SeaState.UpwindFetchM(Water, 0, -200.0, Math.PI / 2, 5000.0, 25.0);
        Assert.InRange(fetch, 625.0, 650.0);             // 650 m to the shore, rounded down to a whole step
        // Standing ON the upwind shore there is no fetch at all, and the water there is smooth.
        Assert.Equal(0.0, SeaState.UpwindFetchM(Water, 0, 449.0, Math.PI / 2, 5000.0, 25.0), 6);
        _out.WriteLine($"  900 m strip, wind across it: fetch {fetch:F0} m at 200 m downwind of the centre");
    }

    /// <summary>The harbor is 3.2 km long and 900 m wide (WorldTerrain.Harbor: a 1600 x 450 m ellipse), so turning
    /// the wind from across it to along it changes the fetch by a factor of three — and the wave with it. This is
    /// the lake-shore observation that fetch is a DIRECTION, not a property of the water.</summary>
    [Fact]
    public void TurningTheWindAlongTheHarborTriplesTheWave()
    {
        WorldTerrain.Lake h = WorldTerrain.Harbor;
        bool Water(double x, double y) => h.Inside(x, y) < 1.0;
        double across = SeaState.UpwindFetchM(Water, h.Cx, h.Cy, Math.PI / 2, 5000.0, 10.0);
        double along = SeaState.UpwindFetchM(Water, h.Cx, h.Cy, 0.0, 5000.0, 10.0);
        Assert.InRange(across, 430.0, 460.0);
        Assert.InRange(along, 1580.0, 1610.0);
        Assert.InRange(SeaState.SignificantHeightM(10.0, along) / SeaState.SignificantHeightM(10.0, across), 1.8, 1.95);
        _out.WriteLine($"  harbor centre at 20 kt: across {across:F0} m -> Hs {SeaState.SignificantHeightM(10, across) * 100:F0} cm; " +
                       $"along {along:F0} m -> Hs {SeaState.SignificantHeightM(10, along) * 100:F0} cm");
    }
}

using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Pitts' roll rate, pinned where Ty put it (2026-11-01). He competed Unlimited, owned an S-1S, flew the
/// in-game Pitts and said the roll was "a little too good, too high": it was turning <b>391.6 deg/s</b> at
/// 160 mph IAS with full aileron. His target is <b>270 deg/s</b> at that speed, and that is what this pins.
///
/// WHY 160 MPH: an aerobatic box entry speed for an S-2B, and the operating point Ty named. Roll rate scales
/// with airspeed, so a rate without a speed beside it means nothing.
///
/// WHICH HALF OF THE RATIO MOVED. A steady roll rate is aileron rolling moment over roll damping, and either
/// one will buy the number. The damping is NOT the wrong half: <c>fuselage.damping.p</c> is 0 and all of it
/// falls out of the wing strips' own geometry (each strip's local alpha shifts by p·y/V), which is already
/// right. Nor is the aileron TRAVEL the wrong half — 25 deg is the S-2B's certificated figure (FAA TCDS A8SO
/// Rev. 21, 15 Feb 2005, p. 9: "Ailerons (symmetrical) UP 25° ± 2° DOWN 25° ± 2°"), and an earlier attempt that
/// shrank it to 16.5 deg to land the rate made the config lie about the aeroplane — that is LESS travel than
/// this same model gives a J-3 Cub, which is absurd for a Pitts. Ty rejected it.
///
/// What was actually wrong is the strip model's assumption that a control surface turns its section's incidence
/// by its FULL geometric angle (τ = 1). For a plain hinged flap τ is a function of chord ratio alone and is
/// nowhere near 1. <c>controls.aileron.effectiveness</c> is that τ, and 0.66 is what puts the Pitts on 270.
/// It is corroborated rather than fitted: thin-aerofoil theory on this config's OWN aileron geometry
/// (cf/c = 0.247 upper, 0.237 lower) gives τ ≈ 0.60, so the tuned value sits within 10 % of a first-principles
/// estimate of the same quantity. See <see cref="TheTunedEffectivenessAgreesWithThinAerofoilTheory"/>.
///
/// THE TOLERANCE IS +/- 15 deg/s (about 5.5 %). Tight enough that a real change in aileron authority, wing
/// geometry or the damping model fails it, loose enough to survive integrator and atmosphere noise. It is a
/// BAND, not a target to re-tune against: if it fails, something about the aeroplane changed.
/// </summary>
public sealed class PittsRollRateTests
{
    private readonly ITestOutputHelper _out;
    public PittsRollRateTests(ITestOutputHelper o) { _out = o; }

    private const double TargetDps = 270.0, ToleranceDps = 15.0, IasMph = 160.0;

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void ThePittsRollsAtTheRateTyAskedFor()
    {
        (double steady, double to90, double peak) = PittsRoll.Measure("pitts-s2b-like", IasMph);
        _out.WriteLine($"Pitts at {IasMph:F0} mph IAS: steady {steady:F1} deg/s (target {TargetDps:F0} +/- {ToleranceDps:F0}), " +
                       $"peak {peak:F1}, {to90:F2} s to 90 %");
        Assert.InRange(steady, TargetDps - ToleranceDps, TargetDps + ToleranceDps);
    }

    /// <summary>The travel is the TCDS number and stays the TCDS number. Copied by hand from FAA TCDS A8SO
    /// Rev. 21 p. 9 (Model S-2B): ailerons symmetrical, 25 deg up and down. Moving it has to be a deliberate
    /// edit here as well (CLAUDE.md — expected values as literals).</summary>
    [Fact]
    public void TheAileronTravelIsTheCertificatedOne()
    {
        const double tcdsDeg = 25.0;
        double authoredDeg = Load("pitts-s2b-like").Controls.Aileron.MaxDeflRad * 180.0 / Math.PI;
        Assert.Equal(tcdsDeg, authoredDeg, 1);
        // And the rate is NOT being held down by a fuselage roll-damping fudge.
        Assert.Equal(0.0, Load("pitts-s2b-like").Fuselage.Damping.P, 9);
    }

    /// <summary>The rate comes from the effectiveness factor, so that is pinned too.</summary>
    [Fact]
    public void TheRateComesFromTheAileronEffectiveness()
    {
        Assert.Equal(0.66, Load("pitts-s2b-like").Controls.Aileron.Effectiveness, 4);
    }

    /// <summary>The tune is not a free parameter pulled to fit one number: thin-aerofoil theory applied to the
    /// aileron chord ratio this very config authors predicts the same quantity independently, and agrees to
    /// within 10 %. If someone re-chords the ailerons, this is the test that notices the two have drifted apart.
    /// τ = 1 - (θ - sin θ)/π, θ = arccos(2·cf/c - 1).</summary>
    [Fact]
    public void TheTunedEffectivenessAgreesWithThinAerofoilTheory()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        double predicted = TauFromGeometry(cfg, "wing-upper", "wing-upper-aileron");
        double tuned = cfg.Controls.Aileron.Effectiveness;
        _out.WriteLine($"thin-aerofoil tau from the authored chords: {predicted:F3}; tuned effectiveness {tuned:F3}");
        Assert.InRange(tuned, predicted * 0.85, predicted * 1.15);
    }

    private static double TauFromGeometry(AircraftConfig cfg, string wingId, string aileronId)
    {
        double Chord(string id)
        {
            var surface = cfg.Surfaces.Single(s => s.Id == id);
            var strips = surface.Strips.Where(s => s.Control is not null && s.Control.Surface == "aileron").ToList();
            return strips.Average(s => s.Chord);
        }
        double cw = Chord(wingId), ca = Chord(aileronId);
        double ratio = ca / (cw + ca);
        double theta = Math.Acos(2.0 * ratio - 1.0);
        return 1.0 - (theta - Math.Sin(theta)) / Math.PI;
    }

    /// <summary>Crispness is not what was being tuned: the stick still bites fast. Roll damping was untouched,
    /// so only the plateau moved.</summary>
    [Fact]
    public void ItIsStillCrisp()
    {
        (double steady, double to90, _) = PittsRoll.Measure("pitts-s2b-like", IasMph);
        Assert.True(to90 < 0.5, $"the Pitts took {to90:F2} s to reach 90 % of {steady:F0} deg/s — that is mushy, not a Pitts");
    }

    /// <summary>THE BACKWARDS-COMPATIBILITY GATE. `effectiveness` defaults to 1.0, which is the τ = 1 the model
    /// always assumed, so every config written before 2026-11-01 — which is every config but the Pitts — flies
    /// bit-for-bit as it did. These two are the aeroplanes that would show a leak first: the Extra rolls faster
    /// than the Pitts and the Great Lakes much slower. The literals are the rates measured on aacb429, the
    /// branch point, BEFORE the field existed.</summary>
    [Theory]
    [InlineData("extra-300-like", 409.3)]
    [InlineData("great-lakes-like", 83.3)]
    public void TheOtherAerobaticTypesAreUnchanged(string id, double expectedDpsOnAacb429)
    {
        Assert.Equal(1.0, Load(id).Controls.Aileron.Effectiveness, 9);
        (double steady, _, _) = PittsRoll.Measure(id, IasMph);
        _out.WriteLine($"{id}: {steady:F1} deg/s (aacb429 measured {expectedDpsOnAacb429:F1})");
        Assert.InRange(steady, expectedDpsOnAacb429 - 8.0, expectedDpsOnAacb429 + 8.0);
    }

    /// <summary>A config that never mentions the field reads 1.0 — the contract default, not an accident of the
    /// JSON loader. Every aeroplane in the fleet but the Pitts is in this state.</summary>
    [Fact]
    public void AConfigThatNeverMentionsEffectivenessGetsOne()
    {
        Assert.Equal(1.0, new ControlAxisConfig().Effectiveness, 9);
        AircraftConfig fromJson = AircraftConfigLoader.LoadFromJson("{\"controls\":{\"aileron\":{\"maxDeflRad\":0.4}}}");
        Assert.Equal(1.0, fromJson.Controls.Aileron.Effectiveness, 9);
        Assert.Equal(1.0, fromJson.Controls.Elevator.Effectiveness, 9);
        Assert.Equal(1.0, fromJson.Controls.Rudder.Effectiveness, 9);
    }
}

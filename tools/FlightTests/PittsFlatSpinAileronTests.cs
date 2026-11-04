using System.Collections.Concurrent;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Flat-spin aileron, upright and inverted (owner, Ty, 2026-11-03, build 2026.1103.1622: "the inverted flat isn't quite flat
/// enough now and full opposite aileron actually reduces the spin rate and lowers the nose which is not correct").
///
/// Aileron AGAINST the roll — out-spin upright (stick away from the rudder), and in an inverted spin, where roll and yaw
/// have opposite signs in body axes, the stick TOWARD the rudder — is the flat-spin input: it flattens the spin and speeds
/// it up, upright and inverted alike (NACA TR 1329, fuselage-loaded aeroplanes: ailerons against the spin are pro-spin).
/// The cause, measured with SpinBalanceProbe (AGENT-LOG run #4): the deflected aileron's separated-flow load was a drag
/// along the wind, even in δ. It damped the rotation with either stick, and its chordwise part, on a cellule above the CG,
/// pitched the nose body-up — up in an upright spin, DOWN in an inverted one. It is now the plate's normal force, tilted with
/// the surface (AeroModel, FLAP NORMAL).
///
/// Every spin: SpinBalanceProbe.Developed — stalled, then 45 s of pro-spin controls (stick full aft upright, full forward
/// inverted, full rudder), the last 20 s measured. Literals are copied by hand from the run #4 table in AGENT-LOG.
/// </summary>
public class PittsFlatSpinAileronTests
{
    private readonly ITestOutputHelper _out;
    public PittsFlatSpinAileronTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));

    private const double Idle = 1.0, Power40 = 0.2, Full = -1.0;   // throttle lever: +1 idle … −1 full

    private static readonly ConcurrentDictionary<(double rud, double lever, bool inv, double ail), SpinBalanceProbe.SpinStats> Spins = new();

    private SpinBalanceProbe.SpinStats Spin(double rudder, double lever, bool inverted, double aileron)
    {
        var st = Spins.GetOrAdd((rudder, lever, inverted, aileron), k => SpinBalanceProbe.Developed(Load(), k.rud, k.lever, k.inv, k.ail).stats);
        _out.WriteLine($"{(inverted ? "inverted" : "upright")} rudder {rudder} lever {lever} aileron {aileron}: α {st.Alpha:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  θ {st.Pitch:F1}  Ω {st.OmegaVertDeg:F0} deg/s  {st.SecPerTurn:F2} s/turn");
        return st;
    }

    /// <summary>
    /// The developed inverted flat: 40 % power, stick full forward, full right rudder, full right (flat-spin) aileron. The band
    /// is the mirror of the upright flat the owner approves (40 % left, no aileron: α 92.9, θ +3.9, 201 deg/s), widened toward
    /// his "-60..-80, nose near the horizon": |α| 75–95, the nose within 15° of the horizon, under 1.8 s a turn, and it stays
    /// inverted. Before run #4 this spin sat at α −32.9, θ −55.7, 154 deg/s (2.33 s/turn).
    /// </summary>
    [Fact]
    public void InvertedFlatSpinIsFlat()
    {
        var st = Spin(1.0, Power40, true, 1.0);
        Assert.True(st.AlphaMax < 0, $"rolled upright: α reached {st.AlphaMax:F0}");
        Assert.InRange(st.Alpha, -95.0, -75.0);
        Assert.InRange(st.Pitch, -15.0, 15.0);
        Assert.True(st.SecPerTurn < 1.8, $"{st.SecPerTurn:F2} s/turn");
    }

    /// <summary>Full flat-spin aileron in the inverted spin (40 % power, right rudder) flattens it — |α| up, the nose up toward
    /// the horizon — and speeds it up. Before run #4 it did the opposite: α −32.7 → −32.9, θ −56.4 → −55.7, 165 → 154 deg/s.</summary>
    [Fact]
    public void FlatSpinAileronFlattensAndSpeedsUpTheInvertedSpin()
    {
        var n = Spin(1.0, Power40, true, 0.0);
        var a = Spin(1.0, Power40, true, 1.0);
        Assert.True(-a.Alpha > -n.Alpha + 20, $"|α| {-n.Alpha:F1} → {-a.Alpha:F1}");
        Assert.True(a.Pitch > n.Pitch + 20, $"nose {n.Pitch:F1} → {a.Pitch:F1}");
        Assert.True(a.OmegaVertDeg > 1.2 * n.OmegaVertDeg, $"rate {n.OmegaVertDeg:F0} → {a.OmegaVertDeg:F0} deg/s");
    }

    /// <summary>The upright mirror (40 % power, LEFT rudder, full RIGHT aileron): out-spin aileron flattens the flat spin further
    /// and speeds it up. Before run #4 it slowed it (201 → 190 deg/s) and steepened it (α 92.9 → 91.8).</summary>
    [Fact]
    public void OutSpinAileronFlattensAndSpeedsUpTheUprightFlatSpin()
    {
        var n = Spin(-1.0, Power40, false, 0.0);
        var a = Spin(-1.0, Power40, false, 1.0);
        Assert.True(a.Alpha >= n.Alpha, $"α {n.Alpha:F1} → {a.Alpha:F1}");
        Assert.True(a.OmegaVertDeg > 1.1 * n.OmegaVertDeg, $"rate {n.OmegaVertDeg:F0} → {a.OmegaVertDeg:F0} deg/s");
    }

    /// <summary>At idle the inverted right spin does not go flat, but the flat-spin aileron must at least RAISE the nose and
    /// flatten it, not lower it. Before run #4: θ −59.6 → −59.3, α −29.4 → −29.3 (no flattening at all).</summary>
    [Fact]
    public void FlatSpinAileronRaisesTheNoseOfTheIdleInvertedSpin()
    {
        var n = Spin(1.0, Idle, true, 0.0);
        var a = Spin(1.0, Idle, true, 1.0);
        Assert.True(a.Pitch > n.Pitch + 4, $"nose {n.Pitch:F1} → {a.Pitch:F1}");
        Assert.True(-a.Alpha > -n.Alpha + 4, $"|α| {-n.Alpha:F1} → {-a.Alpha:F1}");
    }

    /// <summary>
    /// Regression pin: the UPRIGHT spins with the ailerons neutral, which the owner flew on 2026.1103.1622 and approved
    /// ("upright flat spin feels good"). Run #4 changes only what a deflected aileron does, so these must not move. Values
    /// copied from the run #4 "before" table: α, θ (deg), Ω (deg/s).
    /// </summary>
    [Theory]
    [InlineData(-1.0, Idle, 31.8, -56.9, 183.0)]
    [InlineData(1.0, Idle, 30.9, -57.3, 245.0)]
    [InlineData(-1.0, Power40, 92.9, 3.9, 201.0)]
    [InlineData(1.0, Power40, 30.8, -57.6, 318.0)]
    public void UprightSpinsWithAileronsNeutralAreUnchanged(double rudder, double lever, double alpha, double pitch, double omega)
    {
        var st = Spin(rudder, lever, false, 0.0);
        Assert.InRange(st.Alpha, alpha - 1.5, alpha + 1.5);
        Assert.InRange(st.Pitch, pitch - 1.5, pitch + 1.5);
        Assert.InRange(st.OmegaVertDeg, omega * 0.96, omega * 1.04);
    }
}

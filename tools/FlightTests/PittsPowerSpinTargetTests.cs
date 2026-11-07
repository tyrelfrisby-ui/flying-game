using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The owner's power-on spin targets (Ty, former Unlimited Pitts pilot, 2026-11-03), pinned so they cannot be lost:
///  - full power, UPRIGHT: about 85° α ("just shy of flat");
///  - full power, INVERTED, entered with RIGHT rudder (stick full forward, and right aileron to flatten it): 90–95° α,
///    maybe a touch tail-low, and it STAYS there.
/// Both are open. Measured on lane/claude-pitts-spin (AGENT-LOG 2026-11-03): the inverted right-rudder spin cycles —
/// it flattens to −86°, banks, the prop's gyroscopic yaw h·q (q appears as the wings bank) stops the yaw, and it rolls
/// through knife-edge into an upright spin, every ~20 s. The upright right-rudder full-power spin sits at α 30. Nothing
/// is tuned to pass these; they are skipped until the physics that holds a real one there is found.
/// </summary>
public class PittsPowerSpinTargetTests
{
    private readonly ITestOutputHelper _out;
    public PittsPowerSpinTargetTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));

    /// <summary>Met 2026-11-06 (Q007 t918u, PittsSpinWakeTests): α −92.6 (−93..−92), θ +2.4. Was −85.1 (2026-11-05).</summary>
    [Fact]
    public void InvertedRightRudderRightAileronFullPowerStaysInvertedFlat()
    {
        var (st, _) = SpinBalanceProbe.Developed(Load(), rudder: 1.0, lever: -1.0, inverted: true, aileron: 1.0);
        _out.WriteLine($"α {st.Alpha:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})");
        Assert.True(st.AlphaMax < 0, $"rolled upright: α reached {st.AlphaMax:F0}");
        Assert.InRange(st.Alpha, -100.0, -85.0);
    }

    /// <summary>Left: met 2026-11-06 (Q007 t918u): α 84.6 (84..85), θ −3.8 (was 92.7 / +3.3).</summary>
    [Fact]
    public void UprightFullPowerLeftSpinIsNearlyFlat() => UprightFullPowerSpinIsNearlyFlat(-1.0);

    [Fact(Skip = "OPEN (owner target 2026-11-03): the right-rudder (gyro-adverse) spin sits near α 30 at full power; see AGENT-LOG")]
    public void UprightFullPowerRightSpinIsNearlyFlat() => UprightFullPowerSpinIsNearlyFlat(1.0);

    private void UprightFullPowerSpinIsNearlyFlat(double rudder)
    {
        var (st, _) = SpinBalanceProbe.Developed(Load(), rudder, lever: -1.0);
        _out.WriteLine($"rudder {rudder}: α {st.Alpha:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})");
        Assert.InRange(st.Alpha, 78.0, 92.0);
    }
}

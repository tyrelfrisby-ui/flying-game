using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-09, after flying 2026.1109.0331: "the 2-33 will no longer spin". Does it, and what stopped it?</summary>
public class GliderSpinProbe
{
    private readonly ITestOutputHelper _o;
    public GliderSpinProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Can full aft stick hold the aeroplane PAST its own stall? If not it cannot be made to autorotate.</summary>
    [Fact]
    public void FullAftAuthorityAgainstTheShadow()
    {
        foreach (string id in new[] { "glider-2-33-like", "glider-swift-s1-like", "j3-cub-like", "great-lakes-like", "pitts-s2b-like", "extra-300-like" })
        {
            var baseline = Load(id);
            double stall = StallAuthorityTests.StallDeg(baseline);
            string line = $"{id,-22} stall {stall,5:F1} |";
            foreach (double k in new[] { 0.0, 0.5 })
            {
                var c = Load(id);
                if (c.SpinAero != null) c.SpinAero.ShadowK = k;
                double bal = StallAuthorityTests.FullAftBalanceDeg(c);
                line += $"  shadowK {k:F1}: full-aft {bal,5:F1} ({(bal > stall ? "holds past" : "SHORT")})";
            }
            _o.WriteLine(line);
        }
    }

    [Fact]
    public void Probe()
    {
        foreach (string id in new[] { "glider-2-33-like", "glider-swift-s1-like" })
            foreach (double k in new[] { 0.0, 0.5 })
                foreach (double rudder in new[] { -1.0, 1.0 })
                {
                    var c = Load(id);
                    if (c.SpinAero != null) c.SpinAero.ShadowK = k;
                    var (st, _) = SpinBalanceProbe.Developed(c, rudder, 1.0, false, 0.0);
                    _o.WriteLine($"{id,-22} shadowK {k:F1} rudder {rudder,4:F0}: alpha {st.Alpha,6:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  theta {st.Pitch,6:F1}  omega {st.OmegaVertDeg,5:F0} deg/s  {st.SecPerTurn:F2} s/turn");
                }
    }
}

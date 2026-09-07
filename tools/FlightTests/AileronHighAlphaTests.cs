using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Aileron effectiveness vs alpha must follow the NACA lateral-control picture (owner-directed
/// research 2026-09-07; e.g. NACA TN-2347: reversal in the separated-flow range 18-24 deg):
/// full authority attached, ~half by the stall, REVERSED just past stall in separated flow,
/// weak non-reversed recovery in deep stall. Measured on the glider config.
/// </summary>
public class AileronHighAlphaTests
{
    private static double Authority(AircraftConfig config, Dictionary<string, AirfoilTable> tables,
        StripFlowState fs, int n, double sep, double aDeg)
    {
        for (int i = 0; i < n; i++) fs.Separation[i] = sep;
        double a = aDeg * Math.PI / 180.0;
        Vec3 vel = new(20 * Math.Cos(a), 0, 20 * Math.Sin(a));
        (_, Vec3 m0) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, 1.156,
            new ControlDeflections(0, 0, 0, 0), 0.0, fs);
        for (int i = 0; i < n; i++) fs.Separation[i] = sep;
        (_, Vec3 m1) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, 1.156,
            new ControlDeflections(config.Controls.Aileron.MaxDeflRad, 0, 0, 0), 0.0, fs);
        return m1.X - m0.X;
    }

    [Fact]
    public void FollowsNacaEffectivenessShape()
    {
        AircraftConfig config = AircraftConfigLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));
        var tables = Aircraft.BuildAirfoilTables(config);
        int n = config.Surfaces.Sum(x => x.Strips.Count);
        var fs = new StripFlowState();
        fs.EnsureSize(n);

        double a4 = Authority(config, tables, fs, n, 0, 4);
        Assert.True(a4 > 0, "Positive aileron must roll right at low alpha.");
        Assert.True(Authority(config, tables, fs, n, 0, 8) > 0.85 * a4,
            "Attached authority must stay near-full through climb alpha (pre-stall UNCHANGED).");
        double atStall = Authority(config, tables, fs, n, 0, 16);
        Assert.InRange(atStall / a4, 0.25, 0.75); // NACA: ~30-50% remaining at the stall

        // Reversal must exist in the separated-flow band (TN-2347: 18-24 deg; model: 16-20).
        bool reversed = false;
        for (double d = 14; d <= 26; d += 2)
        {
            if (Authority(config, tables, fs, n, 1.0, d) < -0.1 * a4) reversed = true;
        }
        Assert.True(reversed, "Separated-flow aileron REVERSAL band (NACA TN-2347) must exist near 16-24 deg.");

        // Deep stall: weak, NOT strongly reversed.
        double deep = Authority(config, tables, fs, n, 1.0, 36);
        Assert.InRange(deep / a4, -0.1, 0.6);
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Flap/slat/sweep sign pins (owner-directed high-lift + jet physics, 2026-09-07).</summary>
public class HighLiftTests
{
    private static AircraftConfig Glider() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));

    private static double WingLift(AircraftConfig c, double alphaDeg, ControlDeflections ctl)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double a = alphaDeg * Math.PI / 180;
        Vec3 vel = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
        (Vec3 f, _) = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, 1.225, ctl);
        return -f.Z; // up
    }

    [Fact]
    public void FlapAddsLift()
    {
        var c = Glider();
        foreach (var sf in c.Surfaces)
            if (sf.Id == "wing")
                foreach (var st in sf.Strips)
                    st.Flap = new FlapConfig { MaxDeltaAlphaRad = 0.18, MaxCd = 0.08, MaxClMax = 0.5 };
        double up = WingLift(c, 4, new ControlDeflections(0, 0, 0, 0, 0.0));
        double down = WingLift(c, 4, new ControlDeflections(0, 0, 0, 0, 1.0));
        Assert.True(down > up * 1.15, $"Full flap must add lift at fixed alpha: up={up:F0} down={down:F0}");
    }

    [Fact]
    public void SlatDelaysStall()
    {
        var c = Glider();
        foreach (var sf in c.Surfaces)
            if (sf.Id == "wing")
                foreach (var st in sf.Strips)
                    st.Slat = new SlatConfig { StallExtensionRad = 0.17, ClIncrement = 0.1 };
        // At alpha just past the clean stall (~18 deg), slats out should give MORE lift than slats in.
        double slatsIn = WingLift(c, 20, new ControlDeflections(0, 0, 0, 0, 0, 0.0));
        double slatsOut = WingLift(c, 20, new ControlDeflections(0, 0, 0, 0, 0, 1.0));
        Assert.True(slatsOut > slatsIn * 1.1, $"Slats must delay stall (more lift past clean stall): in={slatsIn:F0} out={slatsOut:F0}");
    }

    [Fact]
    public void SweepReducesLift()
    {
        var c = Glider();
        double straight = WingLift(c, 6, new ControlDeflections(0, 0, 0, 0));
        foreach (var sf in c.Surfaces)
            if (sf.Id == "wing")
                foreach (var st in sf.Strips)
                    st.SweepRad = 35 * Math.PI / 180;
        double swept = WingLift(c, 6, new ControlDeflections(0, 0, 0, 0));
        Assert.True(swept < straight * 0.9, $"35deg sweep must reduce lift: straight={straight:F0} swept={swept:F0}");
    }
}

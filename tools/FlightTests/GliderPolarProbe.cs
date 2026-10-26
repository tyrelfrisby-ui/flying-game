using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (GLIDER_POLAR=1): the CLEAN best glide of every sailplane in the fleet, against the published figure.
///
/// `configs/glide-table.json` cannot answer this. Its glider rows are flown with HALF SPOILER at 1.3 Vso --
/// the approach configuration the lessons start in -- so the EB29's 11.4:1 there is a correct number for a
/// wrong question. Best L/D is spoilers in, at whatever speed the polar peaks.
///
/// Why it exists: the EB29 and the Swift were both a third short of book, and a drag budget found why. At a
/// fixed span, wing AREA is almost no lever at all -- shrinking the chord sheds lift and drag together -- so
/// the whole gap had to be drag area, and it was: both glass ships carried a 2-33's fuselage plate and a
/// Cessna's tail sections. The sweep prints what the published figure costs, in the one number
/// NEW-AIRPLANE-PROTOCOL allows calibrating rather than measuring.
/// </summary>
[Collection("WorldTerrainActive")]
public class GliderPolarProbe
{
    private readonly ITestOutputHelper _out;
    public GliderPolarProbe(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("glider-2-33-like", 22.5)]       // SGS 2-33A: 22.5:1
    [InlineData("glider-swift-s1-like", 37.0)]   // Swift S-1: 37:1
    [InlineData("glider-eb29r-like", 70.0)]      // EB29: ~70:1, open class, 29.3 m
    public void CleanBestGlide(string id, double bookLd)
    {
        if (Environment.GetEnvironmentVariable("GLIDER_POLAR") == null) return;
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

        // span^2 / area, counting every surface hinged off the wing -- where a config splits the planform at
        // the hinge line the `wing` strips stop there and are not the wing.
        double area = 0, tip = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (sf.Id != "wing" && !sf.Id.StartsWith("wing-")) continue;
            foreach (StripConfig st in sf.Strips)
            {
                area += st.Area;
                if (st.Chord > 0) tip = Math.Max(tip, Math.Abs(st.Pos[1]) + st.Area / st.Chord / 2);
            }
        }
        double span = tip * 2;

        double shipped = 0, shippedV = 0, baseCd0 = c.Fuselage.Cd0Area;
        foreach (double f in new[] { 1.0, 0.8, 0.6, 0.5, 0.4, 0.3 })
        {
            c.Fuselage.Cd0Area = baseCd0 * f;
            double ld = 0, vv = 0;
            for (double v = 15; v <= 80; v += 0.5)
            {
                TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 600.0);
                if (t.Converged && t.GlideRatio > ld) { ld = t.GlideRatio; vv = v; }
            }
            _out.WriteLine($"   cd0Area {baseCd0 * f:F4} m2  ->  {ld,5:F1}:1 at {vv * 1.944,4:F0} kt");
            if (f == 1.0) { shipped = ld; shippedV = vv; }
        }
        c.Fuselage.Cd0Area = baseCd0;

        _out.WriteLine($"{id,-22} best L/D {shipped,5:F1}:1 at {shippedV * 1.944,4:F0} kt   " +
                       $"book {bookLd,4:F1}:1   {100 * (shipped - bookLd) / bookLd,+6:F0}%   " +
                       $"span {span:F2} m  area {area:F2} m2  AR {span * span / area:F1}");
    }
}

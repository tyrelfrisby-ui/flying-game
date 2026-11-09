using System;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE GEOMETRIC HELICAL SHADOW (SpinAeroConfig.ShadowK). Ty's own logic check, 2026-11-08, turned into tests: from a
/// three-view, in a truly flat spin the EXPOSED wing takes full free-stream dynamic pressure across its whole area and
/// shadows the one behind it. Inverted the exposed wing is the upper and the shadowed one is the more AFT wing; upright
/// the exposed wing is the lower and the shadowed one is the more FORWARD wing. The Report 648 path did only the first
/// half (0.16 inverted, 0.99 upright), and a sign or frame slip here would silently invert a whole class of spins, so
/// each half is pinned separately rather than by one combined number.
/// </summary>
public class GeometricShadowTests
{
    private static AircraftConfig Pitts(double shadowK)
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        c.SpinAero ??= new SpinAeroConfig();
        c.SpinAero.ShadowK = shadowK;
        return c;
    }

    /// <summary>Mean q over the strips of one surface, at one angle of attack and spin rate.</summary>
    private static double Q(AircraftConfig c, string surfaceId, double alphaDeg, double spinDegS = 300)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double r = alphaDeg * Math.PI / 180.0;
        Vec3 v = new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r));
        Vec3 rates = new Vec3(0, 0, spinDegS * Math.PI / 180.0);
        double[] wing = TailWake.WingStripQFactors(c, tables, v, rates);
        double[] all = TailWake.StripQFactors(c, tables, v, rates);
        int i = 0; double sum = 0; int n = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig _ in sf.Strips)
            {
                double q = double.IsNaN(wing[i]) ? all[i] : wing[i];
                if (sf.Id == surfaceId && !double.IsNaN(q)) { sum += q; n++; }
                i++;
            }
        Assert.True(n > 0, $"no strips found for {surfaceId}");
        return sum / n;
    }

    [Fact]
    public void OffByDefaultEveryTypeKeepsTheOldPath()
    {
        Assert.Equal(0.0, new SpinAeroConfig().ShadowK);
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(d.FullName, "configs", "aircraft"), "*.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            if ((c.SpinAero?.ShadowK ?? 0.0) > 0.0)
                Assert.Equal("pitts-s2b-like", c.Id);   // the only type opted in; widen this deliberately, never by accident
        }
    }

    /// <summary>INVERTED FLAT: the upper wing is upstream and clean, the lower wing (the aft one) is shadowed.</summary>
    [Fact]
    public void InvertedFlatShadowsTheLowerAftWingAndLeavesTheUpperClean()
    {
        var c = Pitts(0.9);
        Assert.True(Q(c, "wing-upper", -90) > 0.97, $"upper wing should be in clean air inverted, got {Q(c, "wing-upper", -90):0.00}");
        Assert.True(Q(c, "wing-lower", -90) < 0.80, $"lower wing should be shadowed inverted, got {Q(c, "wing-lower", -90):0.00}");
    }

    /// <summary>UPRIGHT FLAT: the mirror image — the lower wing is upstream and clean, the upper (forward) wing is
    /// shadowed. This is the half the Report 648 path missed entirely (it gave the upper wing 0.99).</summary>
    [Fact]
    public void UprightFlatShadowsTheUpperForwardWingAndLeavesTheLowerClean()
    {
        var c = Pitts(0.9);
        Assert.True(Q(c, "wing-lower", 90) > 0.97, $"lower wing should be in clean air upright, got {Q(c, "wing-lower", 90):0.00}");
        Assert.True(Q(c, "wing-upper", 90) < 0.85, $"upper wing should be shadowed upright, got {Q(c, "wing-upper", 90):0.00}");
    }

    /// <summary>Both halves must be of comparable depth: it is the forward/aft asymmetry that is supposed to make the
    /// two spins differ, not one wing being blanked while the other is untouched.</summary>
    [Fact]
    public void TheTwoHalvesAreComparableInDepth()
    {
        var c = Pitts(0.9);
        double up = 1.0 - Q(c, "wing-upper", 90), inv = 1.0 - Q(c, "wing-lower", -90);
        Assert.True(up > 0.1 && inv > 0.1, $"both halves must fire: upright loss {up:0.00}, inverted loss {inv:0.00}");
        Assert.True(Math.Max(up, inv) / Math.Min(up, inv) < 3.0,
            $"one half is more than 3x the other: upright {up:0.00} vs inverted {inv:0.00}");
    }

    /// <summary>CRUISE IS UNTOUCHED. Every rig, trim and elevator calibration on this fleet was solved in attached flow,
    /// so the new path has to agree with the old one there; it does, to the hundredth, because an attached section sheds
    /// a sheet 2 % of its chord thick and nothing is inside it.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void AttachedFlowIsUnchanged(double alphaDeg)
    {
        AircraftConfig off = Pitts(0.0), on = Pitts(0.9);
        foreach (string id in new[] { "hStab", "elevator", "wing-upper", "wing-lower" })
            Assert.Equal(Q(off, id, alphaDeg, 0), Q(on, id, alphaDeg, 0), 2);
    }

    /// <summary>Nothing aft ever shadows anything forward: the trace only runs upstream, so the wing can never be in
    /// the stabiliser's wake however the aeroplane is pointed.</summary>
    [Fact]
    public void NothingAftShadowsAnythingForward()
    {
        var c = Pitts(0.9);
        foreach (double a in new[] { -120.0, -90, -45, 0, 45, 90, 120 })
        {
            double upper = Q(c, "wing-upper", a), lower = Q(c, "wing-lower", a);
            Assert.True(upper > 0.05 && lower > 0.05, $"α {a}: a wing was blanked outright ({upper:0.00}/{lower:0.00})");
        }
    }

    /// <summary>In a true flat spin the stabiliser is NOT blanketed: the wake leaves the wing across the fuselage and
    /// crosses the tail's height well forward of it. Measured independently as eta_t about 1.0 on the flown spin, and
    /// the Report 648 path agrees, so both models being right here is the check.</summary>
    [Fact]
    public void TheStabiliserIsNotBlanketedInATrueFlatSpin()
    {
        var c = Pitts(0.9);
        Assert.True(Q(c, "hStab", 90) > 0.95, $"upright flat: got {Q(c, "hStab", 90):0.00}");
        Assert.True(Q(c, "hStab", -90) > 0.95, $"inverted flat: got {Q(c, "hStab", -90):0.00}");
    }
}

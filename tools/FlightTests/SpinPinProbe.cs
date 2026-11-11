using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (SPINPINS=1): every quantity <see cref="PittsSpinWakeTests"/> pins, measured in one pass, so a pin is
/// re-cut from a reading taken on the branch it is being written on rather than hand-copied out of an old probe
/// table. Prints the developed spin stats (<see cref="SpinBalanceProbe.Developed"/>, the same entry the tests use)
/// for every cell those tests assert on, and the biplane cellule's per-surface q factors at the angles the two wake
/// tests check. SPINPINS_ID picks the type.
/// </summary>
[Collection("WorldTerrainActive")]
public class SpinPinProbe
{
    private readonly ITestOutputHelper _out;
    public SpinPinProbe(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;
    private const double Idle = 1.0, Power40 = 0.2, Full = -1.0;

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    [Fact]
    public void ProbeSpinPins()
    {
        if (Environment.GetEnvironmentVariable("SPINPINS") == null) return;
        string id = Environment.GetEnvironmentVariable("SPINPINS_ID") ?? "pitts-s2b-like";
        var cfg = Load(id);
        _out.WriteLine($"{id}: developed spins (SpinBalanceProbe.Developed — stall, then 45 s of pro-spin controls, the last 20 measured)");
        _out.WriteLine("  rudder  lever    attitude  aileron |   alpha (min..max)      pitch (min..max)     Omega    s/turn   kt   beta");
        foreach (var (rud, lever, inv, ail, label) in new[]
        {
            (-1.0, Idle,    false, 0.0, "idle         "),
            ( 1.0, Idle,    true,  0.0, "idle         "),
            (-1.0, Power40, false, 0.0, "40 %         "),
            (-1.0, Power40, false, 1.0, "40 %         "),
            (-1.0, Full,    false, 0.0, "full power   "),
            (-1.0, Full,    false, 1.0, "full power   "),
            ( 1.0, Full,    true,  1.0, "full power   "),
        })
        {
            var st = SpinBalanceProbe.Developed(Load(id), rud, lever, inv, ail).stats;
            _out.WriteLine($"  {rud,+5:+0;-0}  {label} {(inv ? "INVERTED" : "upright ")}  {ail,+5:+0.0;-0.0} | "
                + $"{st.Alpha,8:F1} ({st.AlphaMin,5:F0}..{st.AlphaMax,-5:F0}) {st.Pitch,8:F1} ({st.PitchMin,5:F0}..{st.PitchMax,-5:F0}) {st.OmegaVertDeg,7:F0} {st.SecPerTurn,8:F2} {st.Tas * 1.943844,5:F0} {st.Beta,6:F0}");
        }

        // THE POWER SWEEP, ailerons neutral. The 40 % cell is the one that does not settle (α 44..56 against 1-2 deg
        // of swing everywhere else), and this is the map of where the normal spin and the flat spin trade places.
        _out.WriteLine($"{id}: upright LEFT spin, ailerons neutral, throttle lever swept (−1 full … +1 idle)");
        _out.WriteLine("    lever  %pwr |   alpha (min..max)  swing      pitch     Omega   s/turn    kt");
        foreach (double lv in new[] { 1.0, 0.8, 0.6, 0.4, 0.2, 0.0, -0.2, -0.4, -0.6, -0.8, -1.0 })
        {
            var st = SpinBalanceProbe.Developed(Load(id), -1.0, lv, false, 0.0).stats;
            _out.WriteLine($"    {lv,5:+0.0;-0.0} {(1 - lv) * 50,5:F0} | {st.Alpha,8:F1} ({st.AlphaMin,5:F0}..{st.AlphaMax,-5:F0}) {st.AlphaMax - st.AlphaMin,6:F1} {st.Pitch,9:F1} {st.OmegaVertDeg,7:F0} {st.SecPerTurn,8:F2} {st.Tas * 1.943844,5:F0}");
        }

        _out.WriteLine($"{id}: biplane cellule q factors (TailWake.WingStripQFactors, 30 m/s, no rotation), mean per surface");
        var tables = Aircraft.BuildAirfoilTables(cfg);
        _out.WriteLine("    alpha |  wing-upper  upper-aileron   wing-lower  lower-aileron");
        foreach (double a in new[] { -90.0, -75, -45, -35, -30, -20, -10, 0, 10, 16, 30, 45, 60, 75, 90 })
        {
            var q = TailWake.WingStripQFactors(cfg, tables, new Vec3(30 * Math.Cos(a / D), 0, 30 * Math.Sin(a / D)), Vec3.Zero);
            var means = new Dictionary<string, double>();
            int k = 0;
            foreach (var sf in cfg.Surfaces)
            {
                double s = 0; int n = 0;
                foreach (var _ in sf.Strips) { double v = q[k++]; if (!double.IsNaN(v)) { s += v; n++; } }
                if (n > 0) means[sf.Id] = s / n;
            }
            _out.WriteLine($"    {a,5:F0} | {means.GetValueOrDefault("wing-upper", double.NaN),11:F4} {means.GetValueOrDefault("wing-upper-aileron", double.NaN),14:F4} {means.GetValueOrDefault("wing-lower", double.NaN),12:F4} {means.GetValueOrDefault("wing-lower-aileron", double.NaN),13:F4}");
        }
    }
}

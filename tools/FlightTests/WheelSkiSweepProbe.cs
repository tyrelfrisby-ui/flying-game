using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Opt-in (WHEEL_SKI_SWEEP=1): every wheeled type, from one step below its main tyres' V_p up to its skim speed in
/// 5 kt steps, at three held attitudes (2, 6, 10 deg), 40 % power, a 0.5 m/s touch on deep water, 10 s. Ty's rule
/// (2026-11-04): no skiing below V_p; above it, every aeroplane skis unless the load on the wheels exceeds what the
/// water can carry or the drag's moment flips it. The table goes to build/wheel-ski/sweep.log.
/// </summary>
[Collection("WorldTerrainActive")]
public class WheelSkiSweepProbe
{
    private readonly ITestOutputHelper _out;
    public WheelSkiSweepProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>How far above the lowest tyre bottom the lowest prop-tip station is, at an attitude: the water depth at
    /// the wheels that puts a blade in the water.</summary>
    public static double PropClearanceM(AircraftConfig cfg, double pitchDeg)
    {
        double th = pitchDeg / 57.2958;
        double Z(FlyingGame.Core.MathTypes.Vec3 b) => -b.X * Math.Sin(th) + b.Z * Math.Cos(th);
        double wheel = cfg.Gear.Max(g => Z(g.PosVec()));
        var tips = AirframeContact.BuildPoints(cfg).Where(p => p.PropDisc).ToList();
        if (tips.Count == 0) return double.PositiveInfinity;
        return wheel - tips.Max(p => Z(p.Body));
    }

    [Fact]
    public void Clearances()
    {
        if (System.Environment.GetEnvironmentVariable("WHEEL_SKI_SWEEP") == null) return;
        foreach (object[] row in WheelWaterTests.Wheeled())
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", (string)row[0] + ".json"));
            _out.WriteLine($"{row[0],-22} prop clearance above the lowest tyre: 0 deg {PropClearanceM(cfg, 0):F2} m, 2 deg {PropClearanceM(cfg, 2):F2}, 6 deg {PropClearanceM(cfg, 6):F2}, 10 deg {PropClearanceM(cfg, 10):F2}");
        }
    }

    /// <summary>WHEEL_SKI_TRACE=id,kt,att: one run, step by step.</summary>
    [Fact]
    public void Trace()
    {
        string? t = System.Environment.GetEnvironmentVariable("WHEEL_SKI_TRACE");
        if (t == null) return;
        string[] a = t.Split(',');
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", a[0] + ".json"));
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            var r = WheelWaterTouch.Run(cfg, 0.5, double.Parse(a.Length > 3 ? a[3] : "1.5"), speed: double.Parse(a[1]) * 0.514444, throttle01: 0.4,
                                        holdPitchDeg: double.Parse(a[2]), log: _out.WriteLine, startAtHoldPitch: true);
            _out.WriteLine(r.ToString());
            foreach (string e in r.Events) _out.WriteLine(e);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Fact]
    public void Sweep()
    {
        if (System.Environment.GetEnvironmentVariable("WHEEL_SKI_SWEEP") == null) return;
        string? only = System.Environment.GetEnvironmentVariable("WHEEL_SKI_ONLY");
        var lines = new List<string>();
        void W(string s) { _out.WriteLine(s); lines.Add(s); }
        W("id | V_p main kt | V_p fwd/aft kt | V_skim kt | kt | att | result | t_over s | Vmin kt | max immersion m (tyre r) | wheel g mean/peak | pitch min..max");
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            foreach (object[] row in WheelWaterTests.Wheeled())
            {
                string id = (string)row[0];
                if (only != null && !only.Split(',').Contains(id)) continue;
                AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
                GearConfig? main = cfg.Gear.FirstOrDefault(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3);
                if (main == null) continue;
                double vpKt = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, main)) / 0.514444;
                string other = string.Join("/", cfg.Gear.Where(g => g != main && !(Math.Abs(g.Pos[1]) > 0.3 && !g.IsTailwheel))
                    .Select(g => (WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, g)) / 0.514444).ToString("F0")));
                double skimKt = WheelWaterTouch.SkimSpeed(cfg).v / 0.514444;
                double start = Math.Ceiling(vpKt / 5) * 5 - 5, end = Math.Max(skimKt, start + 5);
                // WHEEL_SKI_BELOW=1: the three steps under that instead, down to 15 kt — how far below V_p it still skis.
                if (System.Environment.GetEnvironmentVariable("WHEEL_SKI_BELOW") != null) { end = start - 5; start = Math.Max(15, start - 15); }
                for (double kt = start; kt <= end + 0.01; kt += 5)
                {
                    foreach (double att in new[] { 2.0, 6.0, 10.0 })
                    {
                        var r = WheelWaterTouch.Run(cfg, 0.5, 10.0, speed: kt * 0.514444, throttle01: 0.4, holdPitchDeg: att, startAtHoldPitch: true);
                        string result = r.NoseOverAt > 0 ? "OVER" : r.TouchedFraction < 0.5 ? "AIRBORNE" : r.MinSpeed < 0.7 * r.V ? "SLOWED" : "HOLD";
                        int deep = 0;
                        for (int i = 1; i < r.PeakImmersionM.Length; i++) if (r.PeakImmersionM[i] > r.PeakImmersionM[deep]) deep = i;
                        double rr = r.PeakImmersionM.Length > 0 ? LandingGear.TireRadiusOf(cfg, cfg.Gear[deep]) : 0;
                        double imm = r.PeakImmersionM.Length > 0 ? r.PeakImmersionM[deep] : 0;
                        W($"{id} | {vpKt:F0} | {other} | {skimKt:F0} | {kt:F0} | {att:F0} | {result} | {(r.NoseOverAt > 0 ? r.NoseOverAt.ToString("F1") : "-")} | {r.MinSpeed / 0.514444:F0} | {imm:F2} g{deep} ({rr:F2}) | {r.MeanWheelG:F2}/{r.PeakWheelG:F2} | {r.MinPitchDeg:F0}..{r.MaxPitchDeg:F0} | touch {r.TouchedFraction:P0}{(r.Lost.Count > 0 ? " lost " + string.Join(",", r.Lost) : "")}");
                    }
                }
            }
        }
        finally { FloatHydro.FlatWaterOverride = null; }
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "build", "wheel-ski");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, System.Environment.GetEnvironmentVariable("WHEEL_SKI_OUT") ?? "sweep.log"), lines);
    }
}

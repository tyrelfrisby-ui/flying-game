using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (SPOILER_SWEEP=1). Ty 2026-11-11: "adjust elevator spoiler until upright flat spin is ~78 degrees
/// which was the sweet spot before." His named lever is SpinAeroConfig.ControlSpoilerOnsetDeg (10.0 on the Pitts and
/// the Great Lakes today). This sweeps it, and ControlSpoilerK beside it, against the settled alpha of the flat spin
/// he means: full power, pro-spin rudder, full OUT-spin aileron, stick full aft - the case PittsSpinPastFlatTests
/// pins at alpha 92-95.
///
/// The Great Lakes is the control experiment: same flat-plate hStab/elevator, same fin-lowAR vStab/rudder, same
/// onset 10.0. A difference between the two rules the tail out.
///
/// SPOILER_SWEEP_IDS=pitts-s2b-like,great-lakes-like   which types (default: those two)
/// SPOILER_SWEEP_ONSET=10,30,50,60,70,80               onset angles, deg
/// SPOILER_SWEEP_K=0.6                                 ControlSpoilerK values swept at the config's own onset
/// SPOILER_SWEEP_INV=1                                 also measure the inverted flat spin at each point
/// </summary>
[Collection("WorldTerrainActive")]
public class SpoilerOnsetSpinSweep
{
    private readonly ITestOutputHelper _out;
    public SpoilerOnsetSpinSweep(ITestOutputHelper o) { _out = o; }

    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }
    internal static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id + ".json"));

    private static double[] Nums(string env, double[] fallback)
    {
        string? s = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return s.Split(',').Select(x => double.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>The flat spin Ty means: full power (lever -1), pro-spin rudder, full out-spin aileron, stick full aft.</summary>
    private static SpinBalanceProbe.SpinStats Flat(AircraftConfig cfg, bool inverted)
        => SpinBalanceProbe.Developed(cfg, rudder: inverted ? 1.0 : -1.0, lever: -1.0, inverted: inverted, aileron: 1.0).stats;


    /// <summary>
    /// WHICH ONSET GOVERNS AT RUN TIME. <see cref="SpinAeroConfig.ControlSpoilerOnsetDeg"/> is 10.0 in twelve configs
    /// and 30.0 as the C# property default, and AeroModel reads it as `config.SpinAero?.ControlSpoilerOnsetDeg ?? 30.0`
    /// - so if the JSON key were mis-spelled, or the spinAero block absent, the code default would win silently and the
    /// `controlSpoilerOnsetDegSource` string tying 10 to the flat plate's 9 deg stall would never have been in effect.
    /// Asked 2026-11-11. MEASURED, not reasoned: the config value must both deserialise AND change the pitching moment.
    /// </summary>
    [Fact]
    public void TheConfigsOnsetGovernsAndNotTheCodeDefault()
    {
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;
        try
        {
            var cfg = Load("pitts-s2b-like");
            Assert.NotNull(cfg.SpinAero);
            Assert.Equal(10.0, cfg.SpinAero!.ControlSpoilerOnsetDeg, 6);        // the JSON key deserialises
            Assert.Equal(30.0, new SpinAeroConfig().ControlSpoilerOnsetDeg, 6); // and is NOT the C# default

            var tables = FlyingGame.Sim.Aircraft.BuildAirfoilTables(cfg);
            double e = cfg.Controls.Elevator.MaxDeflRad;
            double Authority(double onsetDeg, double alphaDeg)
            {
                cfg.SpinAero!.ControlSpoilerOnsetDeg = onsetDeg;
                double a = alphaDeg * Math.PI / 180;
                var v = new FlyingGame.Core.MathTypes.Vec3(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                double m(double de) => AeroModel.Compute(cfg, tables, v, FlyingGame.Core.MathTypes.Vec3.Zero,
                    FlyingGame.Core.MathTypes.Vec3.Zero, 1.0, new ControlDeflections(0, de, 0, 0)).Moment.Y;
                return Math.Abs(m(-e) - m(e));
            }
            // Somewhere in the band the two onsets bracket, they MUST differ, or the field is not being read.
            int differing = 0;
            foreach (double a in new[] { 15.0, 20.0, 25.0, 30.0, 35.0, 40.0 })
            {
                double at10 = Authority(10.0, a), at30 = Authority(30.0, a);
                _out.WriteLine($"alpha {a,4:F0}: elevator authority  onset 10 {at10,10:F1} N.m   onset 30 {at30,10:F1} N.m   {(at10 != at30 ? $"differ by {at10 - at30:+0.0;-0.0}" : "IDENTICAL")}");
                if (at10 != at30) differing++;
            }
            Assert.True(differing > 0, "onset 10 and onset 30 give bit-identical elevator authority at every alpha "
                                       + "tried: AeroModel is not reading ControlSpoilerOnsetDeg from the config");
            cfg.SpinAero!.ControlSpoilerOnsetDeg = 10.0;
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }

    [Fact]
    public void Sweep()
    {
        if (Environment.GetEnvironmentVariable("SPOILER_SWEEP") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("SPOILER_SWEEP_IDS") ?? "pitts-s2b-like,great-lakes-like").Split(',');
        double[] onsets = Nums("SPOILER_SWEEP_ONSET", new[] { 10.0, 30.0, 50.0, 60.0, 70.0, 80.0 });
        double[] ks = Nums("SPOILER_SWEEP_K", new double[0]);
        bool inv = Environment.GetEnvironmentVariable("SPOILER_SWEEP_INV") != null;

        foreach (string id in ids)
        {
            var baseCfg = Load(id.Trim());
            _out.WriteLine($"=== {baseCfg.Id}: onset today {baseCfg.SpinAero?.ControlSpoilerOnsetDeg ?? 30.0:F1} deg, K {baseCfg.SpinAero?.ControlSpoilerK ?? 0.0:F2} ===");
            _out.WriteLine($"{"lever",-16} {"alpha",8} {"(min..max)",12} {"pitch",7} {"s/turn",7} {"Omega",7}" + (inv ? $" | {"inv alpha",9} {"(min..max)",12} {"inv pitch",9}" : ""));
            foreach (double onset in onsets)
            {
                var cfg = Load(id.Trim());
                if (cfg.SpinAero is null) { _out.WriteLine("   (no spinAero block)"); break; }
                cfg.SpinAero.ControlSpoilerOnsetDeg = onset;
                Row($"onset {onset:F0}", cfg, inv);
            }
            foreach (double k in ks)
            {
                var cfg = Load(id.Trim());
                if (cfg.SpinAero is null) break;
                cfg.SpinAero.ControlSpoilerK = k;
                Row($"K {k:F2}", cfg, inv);
            }
            // SPOILER_SWEEP_PAIR="cl:k,cl:k,..." - the two-parameter fit. The targets are COUPLED: aft camber is
            // nose-down in body axes either way up, so it steepens the upright spin while flattening the inverted
            // one, whereas controlSpoilerK raises the elevator's authority and the pro-spin stick is AFT upright and
            // FORWARD inverted, so it flattens both. Two levers, two targets (Ty: upright ~78, inverted ~95).
            foreach (string pair in (Environment.GetEnvironmentVariable("SPOILER_SWEEP_PAIR") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(':');
                double cl = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                double k = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                var cfg = Load(id.Trim());
                cfg.Fuselage.Crossflow.AftCamberCl = cl;
                if (cfg.SpinAero is not null) cfg.SpinAero.ControlSpoilerK = k;
                Row($"cl {cl:F2} K {k:F2}", cfg, inv);
            }
            // SPOILER_SWEEP_BROADSIDE: the broadside share alone (CrossflowConfig.AftCamberBroadsideCl), which is
            // the only form of this effect with any authority in a spin, because a developed spin has beta 2-4 deg.
            foreach (double cl in Nums("SPOILER_SWEEP_BROADSIDE", new double[0]))
            {
                var cfg = Load(id.Trim());
                cfg.Fuselage.Crossflow.AftCamberBroadsideCl = cl;
                Row($"broadside {cl:F2}", cfg, inv);
            }
            // SPOILER_SWEEP_BPAIR="broadsideCl:spoilerK,...": the two-parameter fit for Ty's two targets.
            foreach (string pair in (Environment.GetEnvironmentVariable("SPOILER_SWEEP_BPAIR") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(':');
                var cfg = Load(id.Trim());
                cfg.Fuselage.Crossflow.AftCamberBroadsideCl = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                if (cfg.SpinAero is not null) cfg.SpinAero.ControlSpoilerK = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                Row($"bs {parts[0]} K {parts[1]}", cfg, inv);
            }
            foreach (double cl in Nums("SPOILER_SWEEP_AFTCL", new double[0]))
            {
                var cfg = Load(id.Trim());
                cfg.Fuselage.Crossflow.AftCamberCl = cl;
                double[] aft = FlyingGame.Core.Aero.AeroModel.AftBody(cfg);
                Row($"aftCamberCl {cl:F2}", cfg, inv);
                _out.WriteLine($"{"",16} (aft body {aft[0]:F2} m2 at x {aft[1]:F2}, arm {aft[1] - cfg.Mass.CgVec().X:F2} m)");
            }
        }
    }

    private void Row(string label, AircraftConfig cfg, bool inv)
    {
        string extra = "";
        try
        {
            var up = Flat(cfg, false);
            if (inv) { var dn = Flat(cfg, true); extra = $" | {dn.Alpha,9:F1} {$"({dn.AlphaMin:F0}..{dn.AlphaMax:F0})",12} {dn.Pitch,9:F1}"; }
            _out.WriteLine($"{label,-16} {up.Alpha,8:F1} {$"({up.AlphaMin:F0}..{up.AlphaMax:F0})",12} {up.Pitch,7:F1} {up.SecPerTurn,7:F2} {up.OmegaVertDeg,7:F0}{extra}");
        }
        catch (Exception e) { _out.WriteLine($"{label,-16}  ({e.GetType().Name}: {e.Message})"); }
    }
}

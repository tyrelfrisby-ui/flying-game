using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// MEASUREMENT, not a gate (ROLL_PROBE=1). Splits each type's steady roll rate into its two halves — the aileron's
/// rolling moment and the roll damping — straight off <see cref="AeroModel.Compute"/>, so a change to either can be
/// seen for what it is: steady p ≈ ΔL(δ) / (−∂L/∂p). Prints, per type: the rolling-moment coefficient at full aileron,
/// how much of the small-deflection slope survives at full travel, C_lp, and the pb/2V they imply.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class RollDerivativeProbe
{
    private readonly ITestOutputHelper _out;
    public RollDerivativeProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Derivatives()
    {
        if (Environment.GetEnvironmentVariable("ROLL_PROBE") != "1") return;
        foreach ((string id, double mph) in RollFleet.Types)
        for (RollDerivatives.AlphaDeg = 0; RollDerivatives.AlphaDeg <= 2; RollDerivatives.AlphaDeg += 2)
        {
            var d = RollDerivatives.Of(id, mph);
            _out.WriteLine($"{id,-26} {mph,4:F0} mph  b {d.SpanM,5:F2}  Cl(full) {d.ClFull,7:F4}  K(full) {d.SaturationRatio,5:F3}  " +
                           $"Clp {d.Clp,7:F4}  pb/2V {d.PbOver2V,6:F3}  p {d.PDps,6:F1} deg/s | nonlinear p_eq {RollDerivatives.PEqDps,6:F1}  ail@roll {RollDerivatives.AileronAtRoll,7:F4}  damp@roll {RollDerivatives.DampingAtRoll,7:F4}");
        }
    }
}

/// <summary>Static roll derivatives of a config at a given IAS, from the model's own forces.</summary>
internal static class RollDerivatives
{
    internal readonly record struct Result(double SpanM, double ClFull, double SaturationRatio, double Clp, double PbOver2V, double PDps);

    [ThreadStatic] internal static double AlphaDeg;
    [ThreadStatic] internal static double AileronAtRoll, DampingAtRoll, PEqDps;
    internal static Result Of(string id, double iasMph)
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double v = iasMph * 0.44704, rho = 1.225, alpha = AlphaDeg * Math.PI / 180.0;
        Vec3 vb = new(v * Math.Cos(alpha), 0, v * Math.Sin(alpha));
        double b = AeroModel.WingSpan(cfg), s = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase) && !sf.Id.Contains("aileron", StringComparison.OrdinalIgnoreCase))
                foreach (StripConfig st in sf.Strips) s += st.Area;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            if (sf.Id.EndsWith("-aileron", StringComparison.OrdinalIgnoreCase))
                foreach (StripConfig st in sf.Strips) s += st.Area;
        double qsb = 0.5 * rho * v * v * s * b;
        double L(double ail, double p) =>
            AeroModel.Compute(cfg, tables, vb, new Vec3(p, 0, 0), Vec3.Zero, rho, new ControlDeflections(ail, 0, 0, 0)).Moment.X;
        double full = cfg.Controls.Aileron.MaxDeflRad;
        double l0 = L(0, 0);
        double small = 2.0 * Math.PI / 180.0;
        double slope = (L(small, 0) - L(-small, 0)) / (2 * small);
        double dFull = 0.5 * (L(full, 0) - L(-full, 0));
        double dp = 0.02 * 2 * v / b;   // pb/2V = 0.02
        double lp = (L(0, dp) - L(0, -dp)) / (2 * dp);
        double clFull = dFull / qsb;
        double clp = lp / qsb * 2 * v / b;   // per unit pb/2V
        double pb2v = -clFull / clp;
        // Nonlinear static equilibrium: the p at which full aileron's moment is balanced, L(full, p) = 0.
        double lo = 0, hi = 4 * v / b;   // pb/2V up to 2
        for (int k = 0; k < 60; k++) { double mid = 0.5 * (lo + hi); if (L(full, mid) - l0 > 0) lo = mid; else hi = mid; }
        // Moments at that equilibrium, split: the aileron's (δ at p_eq minus δ=0 at p_eq) and the damping (δ=0).
        double pEq = 0.5 * (lo + hi);
        AileronAtRoll = (L(full, pEq) - L(0, pEq)) / qsb; DampingAtRoll = (L(0, pEq) - l0) / qsb;
        PEqDps = pEq * 180 / Math.PI;
        return new Result(b, clFull, dFull / (slope * full), clp, pb2v, pb2v * 2 * v / b * 180 / Math.PI);
    }
}

/// <summary>The fleet roll-rate table: every type at a stated, typical speed for a full-aileron roll (IAS, mph). The speed is a
/// round figure in each type's normal manoeuvring range — about 1.5–2 Vs for the light aircraft, the aerobatic box speed for the
/// aerobats (the owner's 160 mph for the Pitts), a cruise for the transports — not a book figure.</summary>
internal static class RollFleet
{
    internal static readonly (string Id, double IasMph)[] Types =
    {
        ("pitts-s2b-like", 160), ("extra-300-like", 160), ("great-lakes-like", 160), ("stearman-pt17-like", 100),
        ("jn4-jenny-like", 70), ("decathlon-8kcab-like", 120), ("cassutt-f1-like", 150), ("geebee-r2-like", 200),
        ("c172-like", 110), ("pa28-archer-like", 110), ("cirrus-sr22-like", 150), ("glasair3-like", 180),
        ("j3-cub-like", 75), ("j3-cub-floats-like", 70), ("pa18-cub-like", 90), ("pa18-bush-like", 90), ("pa18-floats-like", 85),
        ("pa25-pawnee-like", 100), ("dhc2-beaver-like", 110), ("dhc2-beaver-floats-like", 100), ("aircam-like", 70),
        ("aircam-amphib-like", 70), ("seminole-like", 130), ("dc3-like", 150), ("p51d-like", 250), ("f86-sabre-like", 350),
        ("boeing-737-like", 288), ("hughes-h4-like", 150), ("target-drone-like", 150), ("glider-2-33-like", 60),
        ("glider-eb29r-like", 80), ("glider-swift-s1-like", 120), ("hang-glider-like", 25), ("paraglider-en-b-like", 22),
    };
}

/// <summary>FLEET_ROLL=1: the time-domain full-aileron roll for every type in <see cref="RollFleet"/>: full aileron, full
/// power, stick and rudder otherwise centred, for 3 s; "steady" is the mean over 2–3 s. Each sample is taken as pb/2V at
/// the INSTANTANEOUS airspeed and quoted back at the stated speed, because the nose drops and the speed drifts during a
/// roll with the elevator left alone (the Pitts gains ~4 % in 3 s, much more over 8).</summary>
[Collection("WorldTerrainActive")]
public sealed class FleetRollProbe
{
    private readonly ITestOutputHelper _out;
    public FleetRollProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Fleet()
    {
        if (Environment.GetEnvironmentVariable("FLEET_ROLL") != "1") return;
        foreach ((string id, double mph) in RollFleet.Types)
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            double v = mph * 0.44704, alpha = 2.0 * Math.PI / 180.0, dt = 1.0 / 400.0;
            var state = new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(v * Math.Cos(alpha), 0, v * Math.Sin(alpha)), Vec3.Zero);
            var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
            var loop = new SimLoop(ac, dt);
            double steady = 0, t = 0, to90 = double.NaN; var trace = new List<double>();
            for (int i = 0; i < (int)(3.0 / dt); i++)
            {
                loop.RunFor(dt, new ControlInputs(1.0, 0, 0, -1.0)); t += dt;
                double dps = Math.Abs(ac.State.Rates.X) * 180.0 / Math.PI * v / Math.Max(1.0, ac.State.Velocity.Length); trace.Add(dps);
                if (t > 2.0) steady += dps * dt / 1.0;
            }
            for (int i = 0; i < trace.Count; i++) if (trace[i] >= 0.9 * steady) { to90 = (i + 1) * dt; break; }
            double b = AeroModel.WingSpan(cfg);
            _out.WriteLine($"FLEET {id,-26} {mph,4:F0} mph  steady {steady,6:F1} deg/s  pb/2V {steady * Math.PI / 180 * b / (2 * v),6:F3}  to90 {to90,5:F2} s");
        }
    }
}

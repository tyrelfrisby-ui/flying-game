using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SMOKE PIPES ARE THE EXHAUST STACKS (Ty, asked on 2026-11-11, said yes), so the jet leaves through the bore he
/// measured and not through the 4 in stack the jet model was fitted against in October. That changes the model's INPUT,
/// not its fit. This prints what moved, side by side, because the number is large and he will see it immediately: the
/// Pitts' two 1.5 in stacks are 22.80 cm² against the default's 81.07, so 3.56x less area and 3.56x the exit velocity.
///
/// It also prints how far the plume punches out before the airstream takes it, which is the thing that is visible: the
/// free-jet travel of the mean jet (ExhaustJet.Start/Step marched in still air), the pulse parcels' penetration
/// (ExhaustPulse.Penetration), and the net push AirframeFlow.Deflection ends up with at cruise.
///
/// WHAT IS NOT RE-TUNED. Every coefficient downstream of the bore stays exactly as it was:
///   ExhaustJet          VE 0.85, AFR 12.5, γ 1.35, r_c, T_e 900 K  - engine and gas properties, measured apart from the pipe
///                       ShearAlpha 0.057                            - the published round-jet entrainment constant, dimensionless
///                       Start: b = d/2, h = d, M = ρ_e π b² h       - pure geometry of the pipe, no coefficient
///   ExhaustPulse        x_c = 6 d                                   - the round jet's potential core, 6 diameters, scales WITH the bore
///                       spentS 0.15 s                               - a TIME, not bore-specific; see the note below
///   SmokeTrail          0.22 × penetration, TubeDiffusion 0.6 m/√s  - widening rates, not bore-specific
///                       PuffD0 / PuffGrowth / TauCore               - fitted to the trail's look, never read the bore at all
/// One is still worth Ty's eye and has NOT been touched:
/// <see cref="ExhaustPulse.Penetration"/>'s <c>spentS</c> = 0.15 s. Not a bore coefficient — it is a time — but the
/// sentence that justifies it, that the jet is "spent within a metre or so" in the crossflow, was written at 70 m/s. At
/// 251 m/s the same 0.15 s carries the gas 4.60 m instead of 2.87. His number to move.
/// The other one, the 4 ms march itself, IS now fixed: <see cref="ExhaustJet.Step"/> substeps and
/// <see cref="AirframeFlow.Deflection"/> advances a marker by the displacement the step integrated instead of by the
/// excess it started with. The net-push rows below used to read 0.54 / 0.82 / 1.35 m at the drawn step against 0.39 /
/// 0.45 / 0.62 converged; they now agree to about 1 %. <see cref="JetStepConvergenceProbe"/> has that story.
/// </summary>
public class SmokeStackJetProbe
{
    private readonly ITestOutputHelper _o;
    public SmokeStackJetProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private const double Rho = 1.225, SoundAt900K = 601.0;

    /// <summary>How far the mean jet's own excess carries it along its axis before it is spent (still air): the JETLAG
    /// element marched with its real entrainment, ∫|w| dt.</summary>
    private static double FreeJetTravelM(double u0, double d)
    {
        if (u0 <= 0) return 0;
        ExhaustJet.Element e = ExhaustJet.Start(new Vec3(1, 0, 0), u0, d, Rho);
        double dt = 0.0005, s = 0;
        for (double t = 0; t < 4.0 && !e.Spent; t += dt)
        {
            s += e.W.Length * dt;
            ExhaustJet.Step(ref e, Vec3.Zero, Rho, dt, Vec3.Zero);
        }
        return s;
    }

    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("pitts-s2b-like")]
    public void Probe(string id)
    {
        AircraftConfig cfg = Load(id);
        SmokeConfig sm = cfg.Smoke!;
        PropulsionConfig pr = cfg.Propulsion!;
        SmokePipesConfig pipes = sm.Pipes!;
        double vd = sm.DisplacementCuIn * 16.387e-6;

        double Exit(double d, int n, double rpm, double thr) =>
            ExhaustJet.ExitVelocity(vd, rpm, thr, Rho, d, sm.ExhaustTempK, compressionRatio: sm.CompressionRatio, pipes: n);

        (double d, int n)[] cases = { (0.1016, 1), (sm.JetPipeDiameterM, sm.JetPipeCount) };
        string[] names = { "WAS: one 4 in stack", $"NOW: {pipes.Count} x {pipes.DiameterIn:0.0} in stacks" };

        _o.WriteLine($"=== {id} — AEIO-540, {sm.DisplacementCuIn:0.#} in3, {pr.MaxRpm:0} rpm max, {pr.IdleRpm:0} idle ===");
        _o.WriteLine("                             area cm2   U0 full   U0 idle   free-jet travel   pulse pen @0.15s   element M g");
        var exits = new double[2];
        var pens = new double[2];
        for (int k = 0; k < 2; k++)
        {
            (double d, int n) = cases[k];
            double area = n * Math.PI * d * d / 4;
            double full = Exit(d, n, pr.MaxRpm, 1.0), idle = Exit(d, n, pr.IdleRpm, 0.0);
            exits[k] = full;
            // The pulse's peak parcel against the stream along the pipe at 120 kt (62 m/s) with the pipe 45 deg down:
            // the stream's component along the pipe axis is 62 cos 45 = 43.8 m/s, and the gas has to beat that.
            double stream = 62.0 * Math.Cos(pipes.DownDeg * Math.PI / 180);
            double peak = ExhaustPulse.Velocity(0.5 / ExhaustPulse.EventsPerRev(sm.Cylinders), full, sm.Cylinders);
            pens[k] = ExhaustPulse.Penetration(peak - stream, 0.15, d);
            ExhaustJet.Element el = ExhaustJet.Start(new Vec3(1, 0, 0), full, d, Rho);
            _o.WriteLine($"{names[k],-28} {area * 1e4,8:0.00} {full,9:0.0} {idle,9:0.00} {FreeJetTravelM(full, d),17:0.00} {pens[k],18:0.00} {el.M * 1000,13:0.000}");
        }
        _o.WriteLine($"  exit velocity x{exits[1] / exits[0]:0.00}   (M {exits[1] / SoundAt900K:0.00} at 900 K, a = {SoundAt900K:0} m/s)");
        _o.WriteLine($"  pulse penetration x{pens[1] / Math.Max(1e-9, pens[0]):0.00} — the drawn tube is PipeDiameterM + 0.22 x this + 0.6 sqrt(age):");
        for (int k = 0; k < 2; k++)
            _o.WriteLine($"    {names[k],-28} tube width at 0.15 s = {cases[k].d + 0.22 * pens[k] + 0.6 * Math.Sqrt(0.15):0.00} m");

        // THE NET PUSH the airframe flow ends up giving a marker released at a pipe mouth at cruise - "how far off the
        // free-stream path does the plume end up", which is what you see. Printed at the step SmokeTrail actually uses
        // (FlowDt 0.004) and at two smaller ones: they agree now. Before the march was fixed the drawn 4 ms figure was
        // about twice the others, and the gap widened with the faster jet.
        CrossflowConfig cf = cfg.Fuselage.Crossflow!;
        var flow = new AirframeFlow(cfg, .4 * cf.LengthM, cf.BodyRadiusM);
        Assert.True(pipes.TryPipes(out Vec3[] mouths, out Vec3[] aim));
        var v = new Vec3(62, 0, 62 * Math.Tan(3 * Math.PI / 180));
        _o.WriteLine("  net push off the free-stream path at cruise, 120 kt full power (Deflection, along the pipe axis):");
        for (int k = 0; k < 2; k++)
        {
            (double d, int n) = cases[k];
            double u0 = Exit(d, n, pr.MaxRpm, 1.0);
            string line = $"    {names[k],-28}";
            foreach (double dt in new[] { 0.004, 0.001, 0.00025 })
            {
                List<Vec3> path = flow.Deflection(mouths[0], v, Vec3.Zero, aim[0], u0, d, Rho, dt, 4.0, seed: 7);
                line += $"  dt {dt:0.#####} -> {path[^1].Length,5:0.00} m";
                if (dt == 0.004) line += " (DRAWN)";
            }
            _o.WriteLine(line);
        }
    }
}

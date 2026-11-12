using System.Diagnostics;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE JET'S MARCH NOW CONVERGES (2026-11-11). <see cref="ExhaustJet.Step"/> substeps, so the answer the game draws is
/// the dt → 0 answer this model always had rather than one Euler step's worth of it. Nothing was retuned; this prints
/// the evidence and the bill.
///
/// Three things are measured.
///   1. CONVERGENCE, on the free jet's travel in still air — the clean test, because it has no diffusion noise in it.
///      One substepped 4 ms march must land on the same number as a very fine unsubstepped march.
///   2. THE FLEET, before and after, as the net push off the free-stream path that SmokeTrail actually draws. Nine
///      piston types move; the four that ship are called out.
///   3. THE COST: substeps per march and wall-clock per march, before and after.
/// </summary>
public class JetStepConvergenceProbe
{
    private readonly ITestOutputHelper _o;
    public JetStepConvergenceProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private const double Rho = 1.225;

    /// <summary>∫|w| dt until the element is spent: how far the mean jet's own excess carries it. <paramref name="sub"/>
    /// 1 = the old single Euler step, 0 = substepped.</summary>
    private static (double travel, long substeps) FreeJetTravel(double u0, double d, double dt, int sub)
    {
        ExhaustJet.Element e = ExhaustJet.Start(new Vec3(1, 0, 0), u0, d, Rho);
        double s = 0; long n = 0;
        for (double t = 0; t < 6.0 && !e.Spent; t += dt)
        {
            n += sub > 0 ? sub : ExhaustJet.Substeps(e, Vec3.Zero, Rho, dt);
            // The displacement the step integrated, which is what a marker is actually moved by (and what the old
            // single-Euler march got wrong): not the excess it started with, held for the whole step.
            ExhaustJet.Step(ref e, Vec3.Zero, Rho, dt, Vec3.Zero, sub, out Vec3 carried);
            s += carried.Length;
        }
        return (s, n);
    }

    // ---------------------------------------------------------------- 1. convergence

    [Fact]
    public void TheSubsteppedFourMillisecondMarchLandsOnTheConvergedAnswer()
    {
        // CONVERGENCE MEANS THE ANSWER NO LONGER DEPENDS ON THE STEP. So the test is the substepped 4 ms march against
        // the substepped 0.04 ms march - a hundredfold finer outer step - and not against some "fine" single-Euler march,
        // which at 20 us still loses 4 % of a 141 m/s jet's excess in its first step and so is not a limit at all.
        _o.WriteLine("FREE-JET TRAVEL in still air (no diffusion, so no noise), integrated to Spent.");
        _o.WriteLine("  bore / u0                      old 4ms (1 Euler)   NEW 4ms (sub)   NEW 0.04ms (sub)   new 4ms vs 0.04ms");
        foreach ((string name, double u0, double d) in new[] {
            ("4 in @ 70.6 (fleet default)", 70.6, 0.1016),
            ("2 x 2.0 in @ 140.8 (Extra)", 140.8, 0.0508),
            ("2 x 1.5 in @ 251.1 (Pitts)", 251.1, 0.0381) })
        {
            double was = FreeJetTravel(u0, d, 0.004, 1).travel;
            double now = FreeJetTravel(u0, d, 0.004, 0).travel;
            double fine = FreeJetTravel(u0, d, 0.00004, 0).travel;
            _o.WriteLine($"  {name,-30} {was,17:0.00} {now,15:0.00} {fine,18:0.00} {100 * (now / fine - 1),19:+0.00;-0.00} %");
            // The drawn march IS the converged march now, to better than 1 %.
            Assert.True(Math.Abs(now / fine - 1) < 0.01,
                $"{name}: substepped 4 ms travel {now:0.000} m is not step-independent ({fine:0.000} m at 0.04 ms)");
            // And it really was off before, so this is not a no-op: the single Euler step under-read the travel.
            Assert.True(was < 0.99 * fine, $"{name}: the old single step was already converged?");
        }
    }

    [Fact]
    public void ASubstepNeverSwallowsMoreThanItsShareOfTheElement()
    {
        // The invariant the substep count exists to hold, walked over the whole life of the fastest jet in the fleet.
        ExhaustJet.Element e = ExhaustJet.Start(new Vec3(1, 0, 0), 251.1, 0.0381, Rho);
        int worst = 0;
        for (double t = 0; t < 4.0 && !e.Spent; t += 0.004)
        {
            int n = ExhaustJet.Substeps(e, Vec3.Zero, Rho, 0.004);
            worst = Math.Max(worst, n);
            double wl = e.W.Length;
            double dm = Rho * 2 * e.B * e.H * (0.004 / n) * Math.PI * ExhaustJet.ShearAlpha * wl;
            Assert.True(dm / e.M <= ExhaustJet.MaxEntrainPerStep * 1.0001,
                $"a substep entrains {100 * dm / e.M:0.0} % of the element at t {t:0.000}");
            ExhaustJet.Step(ref e, Vec3.Zero, Rho, 0.004, Vec3.Zero);
        }
        _o.WriteLine($"worst substep count over the Pitts' jet's life: {worst} (ceiling {ExhaustJet.MaxSubsteps})");
        Assert.InRange(worst, 2, ExhaustJet.MaxSubsteps);
    }

    // ---------------------------------------------------------------- 2. the fleet

    /// <summary>Net push off the free-stream path at cruise, which is what the smoke is drawn along. The diffusion term
    /// is a random walk seeded per marker, so this is the mean over several seeds — comparing one seed at two different
    /// outer dt compares two different realisations.</summary>
    private static (double push, long substeps, double ms, double outer) NetPush(AircraftConfig cfg, AirframeFlow flow, Vec3 at, Vec3 aim,
                                                                   double u0, double d, double dt, int sub, int seeds)
    {
        var v = new Vec3(62, 0, 62 * Math.Tan(3 * Math.PI / 180));
        double sum = 0; long subs = 0, outer = 0;
        var sw = Stopwatch.StartNew();
        for (uint s = 1; s <= seeds; s++)
        {
            List<Vec3> path = flow.Deflection(at, v, Vec3.Zero, aim, u0, d, Rho, dt, 4.0, seed: s, jetSubsteps: sub);
            sum += path[^1].Length; outer += path.Count;
        }
        sw.Stop();
        // The substep bill for one march, counted on the element alone (the same states Deflection walks).
        ExhaustJet.Element e = ExhaustJet.Start(aim * (1.0 / aim.Length), u0, d, Rho);
        for (double t = 0; t < 4.0 && !e.Spent; t += dt)
        {
            subs += sub > 0 ? sub : ExhaustJet.Substeps(e, Vec3.Zero, Rho, dt);
            ExhaustJet.Step(ref e, Vec3.Zero, Rho, dt, Vec3.Zero, sub);
        }
        return (sum / seeds, subs, sw.Elapsed.TotalMilliseconds / seeds, (double)outer / seeds);
    }

    [Theory]
    // the four that ship, then the rest
    [InlineData("pitts-s2b-like", true)]
    [InlineData("extra-300-like", true)]
    [InlineData("great-lakes-like", true)]
    [InlineData("j3-cub-like", true)]
    [InlineData("j3-cub-floats-like", true)]
    [InlineData("pa18-cub-like", false)]
    [InlineData("pa18-bush-like", false)]
    [InlineData("pa18-floats-like", false)]
    [InlineData("c172-like", false)]
    public void FleetNetPushBeforeAndAfter(string id, bool ships)
    {
        AircraftConfig cfg = Load(id);
        SmokeConfig sm = cfg.Smoke!;
        PropulsionConfig pr = cfg.Propulsion!;
        CrossflowConfig cf = cfg.Fuselage.Crossflow!;
        var flow = new AirframeFlow(cfg, .4 * cf.LengthM, cf.BodyRadiusM);

        Vec3 at, aim;
        if (sm.Pipes != null && sm.Pipes.TryPipes(out Vec3[] mouths, out Vec3[] aims)) { at = mouths[0]; aim = aims[0]; }
        else
        {
            Assert.True(sm.TryOutlet(out at), id + ": no outlet to release from");
            double down = sm.DownDeg * Math.PI / 180;
            aim = new Vec3(-Math.Cos(down), 0, Math.Sin(down));
        }
        double d = sm.JetPipeDiameterM;
        double u0 = ExhaustJet.ExitVelocity(sm, pr, pr.MaxRpm, 1, Rho);

        const int Seeds = 24;
        (double was, long subWas, double msWas, double outWas) = NetPush(cfg, flow, at, aim, u0, d, 0.004, 1, Seeds);
        (double now, long subNow, double msNow, double outNow) = NetPush(cfg, flow, at, aim, u0, d, 0.004, 0, Seeds);
        (double fine, _, _, _) = NetPush(cfg, flow, at, aim, u0, d, 0.00005, 0, Seeds);

        _o.WriteLine($"{(ships ? "SHIPS " : "      ")}{id,-20} bore {d * 39.3701:0.0} in x{sm.JetPipeCount}  u0 {u0,6:0.0} m/s");
        _o.WriteLine($"         net push: was {was:0.000} m   NOW {now:0.000} m ({100 * (now / was - 1):+0.0;-0.0} %)   " +
                     $"converged {fine:0.000} m   now vs converged {100 * (now / fine - 1):+0.0;-0.0} %");
        _o.WriteLine($"         cost: jet substeps {subWas} -> {subNow} (x{(double)subNow / subWas:0.0}), " +
                     $"OUTER steps (the airframe-flow evaluations, and the stored path) {outWas:0} -> {outNow:0} (x{outNow / outWas:0.00}), " +
                     $"wall-clock per march {msWas:0.000} -> {msNow:0.000} ms (x{msNow / msWas:0.00})");

        // It lands on the model's own converged answer, within the diffusion noise this average still carries.
        Assert.True(Math.Abs(now / fine - 1) < 0.08,
            $"{id}: drawn push {now:0.000} m is not the converged {fine:0.000} m");
        // And it moved: the old step was over-pushing every one of them.
        Assert.True(now < was, $"{id}: the push did not come down ({now:0.000} vs {was:0.000} m)");
    }

    // ---------------------------------------------------------------- 3. what the fix implies for spentS

    [Fact]
    public void WhatTheConvergedJetSaysAboutTheSpentTimeConstant()
    {
        // ExhaustPulse.Penetration's spentS = 0.15 s is a separate constant and is NOT touched here. It is worth saying
        // what the converged jet now implies about it: the free jet's own excess is down to a given speed at a time the
        // march can finally be trusted about.
        foreach ((string name, double u0, double d) in new[] {
            ("4 in @ 70.6", 70.6, 0.1016), ("2 x 2.0 in @ 140.8", 140.8, 0.0508), ("2 x 1.5 in @ 251.1", 251.1, 0.0381) })
        {
            ExhaustJet.Element e = ExhaustJet.Start(new Vec3(1, 0, 0), u0, d, Rho);
            double s = 0, at15 = -1;
            for (double t = 0; t < 1.0 && !e.Spent; t += 0.004)
            {
                s += e.W.Length * 0.004;
                ExhaustJet.Step(ref e, Vec3.Zero, Rho, 0.004, Vec3.Zero);
                if (at15 < 0 && t >= 0.15) at15 = s;
            }
            _o.WriteLine($"  {name,-22} converged free jet travels {at15:0.00} m in the first 0.15 s; " +
                         $"ExhaustPulse.Penetration says {ExhaustPulse.Penetration(u0 - 43.8, 0.15, d):0.00} m for a parcel at the mean");
        }
        _o.WriteLine("  (spentS 0.15 s unchanged — Ty's number to move.)");
    }
}

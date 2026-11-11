using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// RUD_BUD=1 — THE RUDDER'S YAW BUDGET IN THE DEVELOPED SPIN, measured at the spin the aeroplane ACTUALLY
/// flies rather than at a swept free-stream angle.
///
/// Why this exists when SpinAuthorityProbe already reports a rudder number. That probe enters its spin
/// power-on with out-spin aileron, and on this tip that entry does not converge to a spin at all: it ends
/// at alpha 11, 148 kt, 276 deg/s — a spiral. Every "authority at alpha 90" number it then prints is a
/// quasi-static sweep of the relative wind at a SPIRAL's speed, which is four times the dynamic pressure of
/// a real spin and at an attitude the aeroplane never holds. The standing "0.2 deg/s^2" figure is that
/// probe's alpha-90 cell. The aeroplane's own developed spin sits near alpha 79, not 90, and at about a
/// third of that speed — so the quoted number is neither the right attitude nor the right q.
///
/// This probe uses SpinBalanceProbe.Developed (idle, full pro-spin rudder, 45 s, last 20 averaged), which is
/// the entry the rest of the spin work is measured against, and then asks the one question that matters:
/// held at that exact state, what does REVERSING the rudder to full anti-spin do to the yaw acceleration,
/// and how does that compare with what is needed to stop the rotation in a realistic number of turns?
///
/// It then splits the supply into its factors by switching each one off in the config and re-measuring:
/// the base-pressure spoiler term (ControlSpoilerK), the fuselage crossflow sidewash at the fin
/// (FinSidewashK), and the section's own normal-force change, with the local dynamic pressure and the
/// moment arm reported per strip so a missing moment is attributable to a factor and not to "the rudder".
///
/// Finally it flies the recovery: full opposite rudder from the developed spin, hands otherwise off, and
/// reports the turns taken. The static budget predicts; this one decides.
/// </summary>
public class SpinRudderBudgetProbe
{
    private readonly ITestOutputHelper _o;
    public SpinRudderBudgetProbe(ITestOutputHelper o) { _o = o; }

    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("RUD_BUD") == null) return;
        string id = Environment.GetEnvironmentVariable("RUD_BUD_ID") ?? "pitts-s2b-like";
        double rud = double.Parse(Environment.GetEnvironmentVariable("RUD_BUD_RUD") ?? "-1", CultureInfo.InvariantCulture);
        // THE CONDITION IS THE WHOLE MEASUREMENT. A Pitts' developed FLAT spin is power-on with the aileron held,
        // which is how Ty flies it and how AftCamberSpinProbe measures it: Developed(rudder -1, lever -1, aileron 1)
        // settles near alpha 74. At idle with the ailerons neutral the same aeroplane sits at alpha 28 — a steep
        // spin, a different flight condition, and NOT the one being reported. Defaults here are therefore the FLAT
        // spin's: full power, pro-spin aileron. Set RUD_BUD_LEVER=1 RUD_BUD_AIL=0 for the idle steep spin.
        double lever = double.Parse(Environment.GetEnvironmentVariable("RUD_BUD_LEVER") ?? "-1", CultureInfo.InvariantCulture);
        double ail = double.Parse(Environment.GetEnvironmentVariable("RUD_BUD_AIL") ?? "1", CultureInfo.InvariantCulture);

        var cfg = Load(id);
        var (st, ac) = SpinBalanceProbe.Developed(cfg, rud, lever, aileron: ail);
        var s = ac.State;
        var I = ac.MassProperties;
        var cg = cfg.Mass.CgVec();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        Vec3 v = s.Velocity, om = s.Rates;
        double rho = Atmosphere.DensityAtAltitude(-s.Position.Z);
        double qInf = 0.5 * rho * v.LengthSquared;
        var held = ac.CurrentDeflections;

        _o.WriteLine($"{id}: Izz {I.Izz:F0} kg.m^2, rudder travel +/-{cfg.Controls.Rudder.MaxDeflRad * D:F1} deg,"
            + $" ControlSpoilerK {cfg.SpinAero?.ControlSpoilerK ?? 0:F2}, FinSidewashK {cfg.SpinAero?.FinSidewashK ?? 0:F2},"
            + $" ControlSpoilerOnsetDeg {cfg.SpinAero?.ControlSpoilerOnsetDeg ?? 0:F1}");
        _o.WriteLine($"DEVELOPED SPIN (lever {lever:+0.0;-0.0}, aileron {ail:+0.0;-0.0}, pro-spin rudder {rud:+0;-0}, 45 s, last 20 averaged):");
        _o.WriteLine($"   alpha {st.Alpha:F1} deg (range {st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch:F1}  beta {st.Beta:F1}");
        _o.WriteLine($"   p/q/r {st.P:F0}/{st.Q:F0}/{st.R:F0} deg/s   Omega_vert {st.OmegaVertDeg:F0} deg/s"
            + $"   {st.SecPerTurn:F2} s/turn   {st.FtPerTurn:F0} ft/turn   TAS {st.Tas:F1} m/s ({st.Tas * 1.943844:F0} kt)");
        _o.WriteLine($"   q_inf {qInf:F0} Pa   held deflections: ele {held.ElevatorRad * D:F1} deg, rud {held.RudderRad * D:F1} deg");

        // WHAT IS REQUIRED. To stop a rotation of Omega in N turns takes a mean angular deceleration of
        // Omega^2 / (2 * 2*pi*N) — constant deceleration over the angle swept. The honest spread is 0.5 to 3
        // turns; the certification question ("recovery within 2 turns") sits in the middle of it.
        double omVert = Math.Abs(st.OmegaVertDeg) / D;                 // rad/s about the vertical: the spin's own rate
        _o.WriteLine($"\nREQUIRED, to stop {st.OmegaVertDeg:F0} deg/s in N turns  (alpha_req = Omega^2 / (2 * 2*pi*N)):");
        foreach (double turns in new[] { 0.5, 1.0, 1.5, 2.0, 3.0 })
            _o.WriteLine($"   {turns,4:F1} turns -> {omVert * omVert / (2 * 2 * Math.PI * turns) * D,7:F1} deg/s^2");

        // WHAT IS SUPPLIED, at the spin's own state. Three readings, because they answer different questions:
        //   held -> anti   is what the pilot's foot actually buys (the realistic recovery input)
        //   full L <-> R   is the symmetric travel figure every earlier probe printed, kept for comparison
        //   anti, absolute is the total yawing moment with the rudder over, including everything else
        var antiD = new ControlDeflections(held.AileronRad, held.ElevatorRad, -held.RudderRad, held.SpoilerFraction);
        double rMax = cfg.Controls.Rudder.MaxDeflRad;

        double Nz(AircraftConfig c, ControlDeflections d, Vec3 rates)
            => AeroModel.Compute(c, Aircraft.BuildAirfoilTables(c), v, rates, Vec3.Zero, rho, d).Moment.Z;

        double nHeld = Nz(cfg, held, om), nAnti = Nz(cfg, antiD, om);
        double nFullL = Nz(cfg, new ControlDeflections(held.AileronRad, held.ElevatorRad, -rMax, 0), om);
        double nFullR = Nz(cfg, new ControlDeflections(held.AileronRad, held.ElevatorRad, +rMax, 0), om);
        _o.WriteLine($"\nSUPPLIED, at the developed spin's own velocity and rates:");
        _o.WriteLine($"   N with rudder held pro-spin : {nHeld,9:F0} N.m  ->  {nHeld / I.Izz * D,7:F1} deg/s^2");
        _o.WriteLine($"   N with rudder full ANTI-spin: {nAnti,9:F0} N.m  ->  {nAnti / I.Izz * D,7:F1} deg/s^2");
        _o.WriteLine($"   CHANGE from reversing the foot: {nAnti - nHeld,8:F0} N.m  ->  {(nAnti - nHeld) / I.Izz * D,7:F1} deg/s^2"
            + "   <-- THE NUMBER (what the pilot's input is worth)");
        _o.WriteLine($"   symmetric full L<->R travel   : {Math.Abs(nFullR - nFullL),8:F0} N.m  ->  {Math.Abs(nFullR - nFullL) / I.Izz * D,7:F1} deg/s^2"
            + "   (the figure earlier probes printed)");

        // FACTOR DECOMPOSITION. Each term switched off in a COPY of the config and the reversal re-measured, so
        // every row is the same question with one supplier removed. The share is what that supplier contributes
        // to the reversal moment; they do not sum to 100 % because the terms multiply as well as add.
        _o.WriteLine($"\nWHERE THE REVERSAL MOMENT COMES FROM (each factor removed, the reversal re-measured):");
        double baseline = Math.Abs(nAnti - nHeld);
        foreach (var (what, mutate) in new (string, Action<AircraftConfig>)[]
        {
            ("as authored (baseline)",      _ => { }),
            ("spoiler term OFF (K=0)",      c => c.SpinAero!.ControlSpoilerK = 0),
            ("fin sidewash OFF (K=0)",      c => c.SpinAero!.FinSidewashK = 0),
            ("both OFF",                    c => { c.SpinAero!.ControlSpoilerK = 0; c.SpinAero.FinSidewashK = 0; }),
            ("stabHighAlphaK OFF (=1)",     c => c.SpinAero!.StabHighAlphaK = 1.0),
        })
        {
            var c2 = Load(id); c2.SpinAero ??= new SpinAeroConfig(); mutate(c2);
            var t2 = Aircraft.BuildAirfoilTables(c2);
            double a = AeroModel.Compute(c2, t2, v, om, Vec3.Zero, rho, antiD).Moment.Z;
            double h2 = AeroModel.Compute(c2, t2, v, om, Vec3.Zero, rho, held).Moment.Z;
            double d2 = Math.Abs(a - h2);
            _o.WriteLine($"   {what,-26} reversal {d2,8:F0} N.m  {d2 / I.Izz * D,7:F1} deg/s^2"
                + $"   ({(baseline > 1 ? 100 * d2 / baseline : 0),5:F0} % of baseline"
                + $", so this factor is worth {(baseline > 1 ? 100 * (baseline - d2) / baseline : 0),5:F0} %)");
        }

        // AND THE OTHER TWO FACTORS, which cannot be switched off because they are geometry: the local dynamic
        // pressure each vertical strip actually flies in, and its yaw arm. Per strip, pro-spin against anti-spin.
        _o.WriteLine($"\nPER-STRIP, vertical tail only (pro-spin rudder -> full anti-spin):");
        _o.WriteLine("   x/z (m)        area m^2   dFy N     arm m    dMz N.m   eta_t   q_local Pa");
        var byStrip = new Dictionary<string, double>();
        foreach (var (d, sign) in new[] { (held, -1.0), (antiD, 1.0) })
        {
            var cap = new List<ForceSample>(512);
            ForceDebug.Samples = cap;
            try { AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, rho, d); }
            finally { ForceDebug.Samples = null; }
            foreach (var f in cap)
            {
                if (f.Kind != "lift" && f.Kind != "drag") continue;
                if (Math.Abs(f.PosBody.Y) > 0.03 || f.PosBody.X > -1.0) continue;   // fin + rudder sit on the centreline, aft
                string k = $"{f.PosBody.X:0.00}/{f.PosBody.Z:+0.00;-0.00}";
                byStrip.TryGetValue(k, out double acc);
                byStrip[k] = acc + sign * f.ForceBody.Y;
            }
        }
        var eta = new Dictionary<string, double>();
        {
            var cap = new List<ForceSample>(512);
            ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
            try { AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, rho, held); }
            finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
            foreach (var f in cap)
                if (f.Kind == "tailq" && f.MomentBody.X > 0.5)
                    eta[$"{f.PosBody.X:0.00}/{f.PosBody.Z:+0.00;-0.00}"] = f.ForceBody.Z;
        }
        double areaOf(string k)
        {
            double a = 0;
            foreach (var sf in cfg.Surfaces)
                if (sf.Id.StartsWith("vStab") || sf.Id.StartsWith("rudder"))
                    foreach (var sp in sf.Strips)
                        if ($"{sp.PosVec().X:0.00}/{sp.PosVec().Z:+0.00;-0.00}" == k) a += sp.Area;
            return a;
        }
        double sumFy = 0, sumM = 0, sumArea = 0, sumQ = 0; int nq = 0;
        foreach (var (k, fy) in byStrip.OrderBy(kv => kv.Key))
        {
            double x = double.Parse(k.Split('/')[0], CultureInfo.InvariantCulture);
            double arm = x - cg.X, a = areaOf(k);
            double e = eta.TryGetValue(k, out double ev) ? ev : double.NaN;
            sumFy += Math.Abs(fy); sumM += Math.Abs(fy * arm); sumArea += a;
            if (!double.IsNaN(e)) { sumQ += qInf * e; nq++; }
            _o.WriteLine($"   {k,-14} {a,7:F3}  {fy,8:F0}  {arm,7:F2}  {Math.Abs(fy * arm),9:F0}   {e,6:F2}  {(double.IsNaN(e) ? 0 : qInf * e),9:F0}");
        }
        double qMean = nq > 0 ? sumQ / nq : 0;
        _o.WriteLine($"   TOTAL          {sumArea,7:F3}  {sumFy,8:F0}  {sumM / Math.Max(sumFy, 1e-9),7:F2}  {sumM,9:F0}"
            + $"   mean eta {(qInf > 1 ? qMean / qInf : 0),5:F2}  q_local {qMean,7:F0} (q_inf {qInf:F0})");
        _o.WriteLine($"   implied dCy over the whole vertical tail = dFy / (q_local * area) = {(qMean * sumArea > 1 ? sumFy / (qMean * sumArea) : 0):F3}");

        // AND THE TRUTH: fly it. The static budget above predicts; this decides. It matters because the budget is a
        // CONSTANT-deceleration sum and a recovery is not one: the moment grows as it works, because the nose dropping
        // raises q and unblankets the fin, so an aeroplane with less than the "required" instantaneous moment can still
        // recover inside the turns. That is why the arithmetic requirement and the flown result disagree, and the flown
        // one is the answer.
        //
        // The variants are the ones that decide whether a recovery is real. Both control positions Ty actually uses,
        // then NACA's own robustness cases: the TN 1329 standard is recovery in 2 turns by full rapid rudder reversal,
        // and the TP 1009 "criterion spin" asks for it again with only TWO THIRDS of the rudder and the aileron a third
        // in the most critical direction. An aeroplane that only recovers on the last inch of pedal has not recovered.
        _o.WriteLine($"\nFLOWN: opposite rudder from the developed spin (power as entered; NACA TN 1329 asks for 2 turns) —");
        foreach (var (what, stick, aOut, rScale) in new (string, double, double, double)[]
        {
            ("stick aft, ail neutral",   -1.0, 0.0,  1.0),
            ("stick neutral (Beggs)",     0.0, 0.0,  1.0),
            ("stick aft, AILERON HELD",  -1.0, ail,  1.0),   // Ty does not take the aileron out; the spin was held on it
            ("Beggs, AILERON HELD",       0.0, ail,  1.0),
            ("2/3 rudder, ail 1/3 pro",  -1.0, ail / 3, 2.0 / 3),  // TP 1009 criterion spin
        })
        {
            var (_, ac2) = SpinBalanceProbe.Developed(cfg, rud, lever, aileron: ail);
            double h = SimLoop.DefaultFixedDtSec, net = 0, tEnd = double.NaN, turnsEnd = double.NaN;
            for (double t = 0; t < 20; t += h)
            {
                ac2.Step(new ControlInputs(aOut, stick, -rud * rScale, lever), h);
                var s2 = ac2.State;
                if (-s2.Position.Z < 600)
                    ac2.State = new RigidBodyState(new Vec3(s2.Position.X, s2.Position.Y, s2.Position.Z - 2500), s2.Attitude, s2.Velocity, s2.Rates);
                Vec3 zB = ac2.State.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
                double ov = ac2.State.Rates.X * zB.X + ac2.State.Rates.Y * zB.Y + ac2.State.Rates.Z * zB.Z;
                net += Math.Abs(ov) * h;
                if (t > 0.3 && Math.Abs(ov) * D < 20) { tEnd = t; turnsEnd = net / (2 * Math.PI); break; }
            }
            var sf2 = ac2.State;
            Vec3 air = sf2.Velocity;
            _o.WriteLine(double.IsNaN(tEnd)
                ? $"   {what,-26}: DID NOT RECOVER in 20 s ({net / (2 * Math.PI):F1} turns), final alpha {Math.Atan2(air.Z, air.X) * D:F1}, {sf2.Rates.Length * D:F0} deg/s"
                : $"   {what,-26}: recovered in {turnsEnd:F2} turns, {tEnd:F1} s  (final alpha {Math.Atan2(air.Z, air.X) * D:F1})");
        }
    }
}

namespace FlyingGame.Core;

/// <summary>
/// THE COMPRESSION STROKES OF A DEAD ENGINE, so a propeller LUMPS to a stop instead of decaying smoothly, and once
/// stopped stays stopped until the air can shove it over a compression. Ty, 2026-11-10:
///
///   "Cranking resistance should be calibrated such that at fine pitch the prop stops right at level flight stall
///    speed. Use real 'compression strokes' for the number of cyl of the engine so the prop lumps to a stop instead
///    of stopping smoothly. To get prop windmilling again should take 10% under vne but occasional flips through a
///    compression stroke can occur at speeds below (simulate slow leak down on each cyl to accommodate the tick over
///    but give each cylinder a different leak down rate to keep it from being predictable"
///
/// <see cref="PistonEngine.MotoringTorqueNm"/> is the MEAN of what it takes to turn the engine — pumping and friction,
/// out of the power curve's own friction intercept. This is the RIPPLE on top of it, and the ripple is the whole
/// character: the mean alone gives a propeller that slows smoothly and creeps to a halt, where a real one stops with a
/// thud against a cylinder on compression.
///
/// THE SHAPE IS THE SLIDER-CRANK AND A POLYTROPIC GAS, not a chosen waveform. For one cylinder at crank angle phi from
/// top dead centre the swept volume is
///
///     V(phi) = Vc · (1 + (CR − 1)/2 · (1 − cos phi))
///
/// (Vc at TDC, Vc·CR at BDC), the trapped gas follows p = p_amb · (V_bdc / V)^n with n = 1.3, and the torque the gas
/// makes on the crank is (p − p_amb)·dV/dphi. Compressing, phi runs DOWN to zero against the rotation, so that torque
/// opposes it; expanding it runs up with it, so the gas gives the work back. A sealed cylinder therefore costs nothing
/// net over a cycle — which is correct, and is why the mean comes from the friction intercept rather than from here.
/// What this contributes is the lumpiness, and a hard lump near TDC is what stops a propeller.
///
/// A FOUR-STROKE FIRES EACH CYLINDER ONCE PER TWO REVOLUTIONS, so the compression and expansion together occupy one
/// revolution of every two, and the cylinders are evenly spaced through 720 degrees. Four cylinders therefore give two
/// compressions per revolution with gaps between them; a nine-cylinder radial gives four and a half, overlapping, and
/// it hardly lumps at all. That spread is free — it is the cylinder count and nothing else.
///
/// LEAK-DOWN IS WHAT LETS IT TICK OVER. A cylinder stopped on compression bleeds past its rings, the torque holding
/// the propeller falls, and it creeps over onto the NEXT cylinder's compression, which is fresh and sealed. Each
/// cylinder gets its own leak rate (<see cref="LeakTauSec"/>), so the aeroplane never ticks in a rhythm — Ty's "give
/// each cylinder a different leak down rate to keep it from being predictable".
/// </summary>
public static class PistonCompression
{
    /// <summary>Polytropic exponent for the trapped charge: 1.3 is the standard value for a cylinder with heat loss
    /// (1.4 is the adiabatic ideal, which a real cylinder does not reach).</summary>
    public const double PolytropicN = 1.3;

    /// <summary>
    /// ONE CYLINDER'S GAS TORQUE, as a share of the peak a sealed cylinder makes: negative while it is being
    /// compressed, positive while it expands, zero over the rest of the four-stroke cycle.
    /// <paramref name="cycleRad"/> is this cylinder's own position in its 720-degree cycle (0 .. 4π); the gas spring
    /// occupies the last revolution of it, with top dead centre at 3π.
    /// <paramref name="leak01"/> is 1 for a sealed cylinder and 0 for one that has bled down completely, and it
    /// multiplies the gauge pressure — so a leaked cylinder is both easier to compress and gives less back.
    /// </summary>
    public static double CylinderTorqueFactor(double cycleRad, double compressionRatio, double leak01 = 1.0)
    {
        double cycle = Mod(cycleRad, 4.0 * Math.PI);
        if (cycle < 2.0 * Math.PI) return 0.0;                 // induction and exhaust: the valves are open, no spring
        double cr = Math.Max(1.5, compressionRatio);
        double leak = Math.Clamp(leak01, 0.0, 1.0);

        // phi from TDC, which sits at 3π: compressing on the way up (phi falling to 0), expanding after.
        double phi = cycle - 3.0 * Math.PI;                     // −π .. +π
        bool compressing = phi < 0.0;
        double p = Math.Abs(phi);                               // π at BDC, 0 at TDC
        double vOverVc = 1.0 + (cr - 1.0) * 0.5 * (1.0 - Math.Cos(p));
        double gauge = leak * (Math.Pow(cr / vOverVc, PolytropicN) - 1.0);        // (p − p_amb) / p_amb
        double dVdPhi = (cr - 1.0) * 0.5 * Math.Sin(p);                           // per Vc, always ≥ 0 on 0..π
        double q = gauge * dVdPhi;
        return (compressing ? -q : q) / PeakFor(cr);
    }

    /// <summary>
    /// THE WHOLE ENGINE'S RIPPLE at this crank angle, as a share of the peak one cylinder makes sealed — so −1 is a
    /// single cylinder squarely on compression and a four-cylinder lumps near that while a nine-cylinder radial, whose
    /// strokes overlap, never gets far from zero.
    /// <paramref name="leak"/> is each cylinder's seal, 1 .. 0, in firing order; pass nothing for a sealed engine.
    /// </summary>
    public static double RippleFactor(double crankRad, int cylinders, double compressionRatio, double[]? leak = null)
    {
        int n = Math.Max(1, cylinders);
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            // Evenly spaced through the 720-degree cycle, which is what makes the gaps close up as cylinders are added.
            double phase = crankRad + 4.0 * Math.PI * i / n;
            sum += CylinderTorqueFactor(phase, compressionRatio, leak is { Length: > 0 } ? leak[i % leak.Length] : 1.0);
        }
        return sum;
    }

    /// <summary>The most a whole sealed engine of this many cylinders resists, as a share of one cylinder's peak.
    /// Swept rather than derived: where the cylinders' strokes overlap the peaks add, and how much they add is the
    /// cylinder count's own business.</summary>
    public static double WorstRipple(int cylinders, double compressionRatio)
    {
        double worst = 0.0;
        for (int i = 0; i <= 1440; i++)
        {
            double r = RippleFactor(4.0 * Math.PI * i / 1440.0, cylinders, compressionRatio);
            if (r < worst) worst = r;
        }
        return -worst;
    }

    /// <summary>One cylinder's peak |gas torque| over its cycle, sealed, in the same units as the expression above —
    /// the normaliser, so <see cref="CylinderTorqueFactor"/> comes back as a share of it.</summary>
    private static double PeakFor(double cr)
    {
        if (Math.Abs(cr - _peakCr) < 1e-9) return _peak;
        double best = 1e-9;
        for (int i = 1; i < 720; i++)
        {
            double p = Math.PI * i / 720.0;
            double vOverVc = 1.0 + (cr - 1.0) * 0.5 * (1.0 - Math.Cos(p));
            double q = (Math.Pow(cr / vOverVc, PolytropicN) - 1.0) * (cr - 1.0) * 0.5 * Math.Sin(p);
            if (q > best) best = q;
        }
        _peakCr = cr; _peak = best;
        return best;
    }
    private static double _peakCr = double.NaN, _peak = 1.0;

    /// <summary>The blade angle a dead propeller sits at: the governor's fine stop on a constant-speed type, the fitted
    /// 75 % pitch on a fixed-pitch one, 15 deg for a type whose blade has not been fitted yet. Both of Ty's anchors are
    /// stated "at fine pitch", so every calibration below reads this.</summary>
    public static double FinePitchDeg(DataContracts.PropulsionConfig p)
        => p is { ConstantSpeed: true, FineStopDeg: > 0.0 } ? p.FineStopDeg
         : p.BladePitch75Deg > 0.0 ? p.BladePitch75Deg : 15.0;

    /// <summary>The rpm the calibrations below call "at rest": not zero, because a blade-element solve needs an omega
    /// to divide by.</summary>
    public const double AtRestRpm = 5.0;

    /// <summary>
    /// HOW HARD A CYLINDER ON COMPRESSION PUSHES BACK, N·m, calibrated to Ty's restart anchor, 2026-11-10: "To get
    /// prop windmilling again should take 10% under vne". A STOPPED propeller has no stored energy, so it has to beat
    /// the worst of the ripple from rest, and that condition fixes this one number:
    ///
    ///     Q_air(0.9·Vne, fine stop, at rest)  =  |Q_mean(at rest)|  +  peak · worstRipple(cylinders, CR)
    ///
    /// AT REST ON BOTH SIDES, which is a correction (2026-11-10, second pass). This used to take the motoring mean at
    /// RATED rpm, which was harmless while the boundary friction was a spike below 300 rpm and is not harmless now:
    /// the mean at rest is three to five times the mean at rated, and leaving that out of an equation written at
    /// rest put the restart speed at 1.07 Vne on the Pitts and 1.11 Vne on the Cub instead of the 0.90 Vne he asked
    /// for. With the mean at rest it measures 0.900 Vne on every type. It is still one equation in one unknown.
    ///
    /// Which leaves his OTHER anchor — the propeller stopping at the stall speed — as what
    /// <see cref="PistonEngine.BoundaryExcessFor"/> is solved from, and the two anchors then share one budget: at rest
    /// the air at 0.9 Vne has to pay for the dry friction AND the compression peak out of the same torque. On the Great
    /// Lakes, whose Vne is only 68 m/s, the dry friction alone takes 68 % of that budget and what is left makes a weak
    /// lump. That is a finding about those two anchors on that type, not a knob.
    /// </summary>
    public static double PeakNmFor(DataContracts.AircraftConfig c)
    {
        DataContracts.PropulsionConfig? p = c.Propulsion;
        if (p is null || p.PropDiameterM <= 0.0 || p.MaxPowerW <= 0.0) return 0.0;
        double restartMs = 0.9 * (c.Limits?.VneMs ?? 0.0);
        if (restartMs <= 1.0) return 0.0;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, FinePitchDeg(p), p.ActivityFactor);
        double qAir = -BladeElement.Solve(g, restartMs, 0.0, AtRestRpm, Atmosphere.SeaLevelDensityKgM3).TorqueNm;
        double qMean = -PistonEngine.MotoringTorqueNm(p, AtRestRpm, 0.0, 1.0, 1.0);
        double worst = WorstRipple(c.EngineCylinders, c.EngineCompressionRatio);
        return Math.Max(0.0, (qAir - qMean) / Math.Max(1e-6, worst));
    }

    /// <summary>
    /// WHERE A TURNING PROPELLER GIVES UP, m/s — Ty's other anchor, 2026-11-10: "at fine pitch the prop stops right at
    /// level flight stall speed". Seeded from a windmill at <paramref name="startMs"/> and slowed in 0.25 m/s steps,
    /// each held 0.8 s, this integrates the same shaft equation <c>AircraftEngine</c> runs — blade torque against the
    /// motoring mean plus the compression ripple, with the crank angle and every cylinder's seal as states — and
    /// returns the speed at which the rpm finally falls below 20. NaN if it never stops.
    ///
    /// SEEDED FROM A WINDMILL, because that is the scenario: it was turning and you slowed down. Started from rest it
    /// could never break away at a speed <see cref="PeakNmFor"/> made exactly marginal, which would measure the RESTART
    /// threshold by accident and say nothing about where a turning propeller gives up.
    ///
    /// IT IS NOT A QUASI-STEADY NUMBER and must not be read as one. A heavy blade coasts, so how fast you slow down
    /// matters: at 0.8 s per 0.25 m/s — 0.31 m/s^2, a gentle deceleration — the Pitts quits at 29.4 m/s, and given
    /// 2.4 s a step it hangs on to nearly 31. That is real behaviour rather than a numerical artefact, but it means
    /// anything calibrated against this has the deceleration baked in, so the solve, the probe and the test all use
    /// these same steps.
    /// </summary>
    public static double PropStopSpeedMs(DataContracts.AircraftConfig c, double startMs)
    {
        DataContracts.PropulsionConfig? p = c.Propulsion;
        if (p is null || p.PropDiameterM <= 0.0 || p.MaxPowerW <= 0.0 || startMs <= 2.5) return double.NaN;
        int cyl = Math.Max(1, c.EngineCylinders);
        double cr = c.EngineCompressionRatio, peak = PeakNmFor(c), rho = Atmosphere.SeaLevelDensityKgM3;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, FinePitchDeg(p), p.ActivityFactor);
        double Mean(double r) => PistonEngine.MotoringTorqueNm(p, r, 0.0, 1.0, 1.0);

        double[] tau = LeakTauSec(cyl, SeedFor(c.Id));
        var leak = new double[cyl];
        for (int i = 0; i < cyl; i++) leak[i] = 1.0;
        BladeElement.Windmilling spun = BladeElement.Windmill(g, startMs, rho, p.MaxRpm, Mean);
        double rpm = spun.Turning ? spun.Rpm : 0.0, crank = 0.0;
        const double dt = 0.002;
        const int stepsPerSpeed = 400;                        // 0.8 s at each speed
        double inertia = Math.Max(0.05, p.PropInertia);
        for (double v = startMs; v > 2.0; v -= 0.25)
        {
            for (int step = 0; step < stepsPerSpeed; step++)
            {
                // The order is AircraftEngine.StepShaft's, step for step: bleed on the crank angle the engine is
                // actually at, take the ripple there, integrate, and move the crank last — because the crank only
                // moves if the shaft does, and it is what the next step's lump is measured from.
                double qProp = BladeElement.Solve(g, v, 0.0, rpm, rho).TorqueNm;
                BleedOneStep(leak, tau, crank, dt, rpm * 2.0 * Math.PI / 60.0 * dt);
                double qEng = Mean(rpm) + peak * RippleFactor(crank, cyl, cr, leak);
                double omega = Math.Max(0.0, rpm * 2.0 * Math.PI / 60.0 + (qEng - qProp) / inertia * dt);
                rpm = Math.Min(omega * 60.0 / (2.0 * Math.PI), p.MaxRpm * 1.6);
                crank = (crank + omega * dt) % (4.0 * Math.PI);
            }
            if (rpm < 20.0) return v;
        }
        return double.NaN;
    }

    /// <summary>One step of every cylinder's seal: bleeding past the rings while it is on its compression stroke, and
    /// re-charging in proportion to how far the crank actually moved everywhere else — a cylinder fills on its
    /// induction stroke, so a stopped engine never gains any. Shared so the sweep above and <c>AircraftEngine</c>
    /// cannot drift apart.</summary>
    public static void BleedOneStep(double[] leak, double[] tauSec, double crankRad, double dt, double crankAdvanceRad)
    {
        int n = leak.Length;
        for (int i = 0; i < n; i++)
        {
            double cycle = Mod(crankRad + 4.0 * Math.PI * i / n, 4.0 * Math.PI);
            bool onCompression = cycle >= 2.0 * Math.PI && cycle < 3.0 * Math.PI;
            leak[i] = onCompression
                ? leak[i] * Math.Exp(-dt / Math.Max(0.05, tauSec[i]))
                : Math.Min(1.0, leak[i] + crankAdvanceRad / (2.0 * Math.PI));
        }
    }

    /// <summary>
    /// HOW LONG EACH CYLINDER TAKES TO BLEED DOWN, seconds, deliberately all different. A healthy cylinder holds
    /// compression for a few seconds and a tired one for well under one, which is what a leak-down check measures; the
    /// spread here is that, dealt out from the aeroplane's own id so a given aeroplane is consistent from flight to
    /// flight while no two cylinders and no two types agree. 0.6 s to 4.5 s.
    ///
    /// SEED IT WITH <see cref="SeedFor"/> AND NOT WITH string.GetHashCode, which was the first version and was not
    /// consistent from flight to flight at all: .NET randomizes string hashing per PROCESS, so every launch dealt the
    /// aeroplane a different set of cylinders. It showed up here as 0.5 m/s of scatter in the measured stopping speed
    /// between two runs of the same test on the same code — which is small, but it is the difference between a number
    /// you can calibrate against and one you cannot.
    /// </summary>
    public static double[] LeakTauSec(int cylinders, int seed)
    {
        int n = Math.Max(1, cylinders);
        var taus = new double[n];
        uint s = (uint)seed | 1u;
        for (int i = 0; i < n; i++)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;           // xorshift: deterministic, and no two neighbours alike
            taus[i] = 0.6 + 3.9 * ((s & 0xFFFFu) / 65535.0);
        }
        return taus;
    }

    /// <summary>A STABLE seed from an aeroplane's id — FNV-1a, so the same id gives the same cylinders in every
    /// process, on every platform, for ever. The point of dealing the leak rates from the id is that this aeroplane's
    /// engine is always this engine; a per-process hash gives it a different engine every launch.</summary>
    public static int SeedFor(string? id)
    {
        uint h = 2166136261u;
        if (id is not null)
            foreach (char ch in id) { h ^= ch; h *= 16777619u; }
        return (int)h;
    }

    private static double Mod(double a, double m)
    {
        double r = a % m;
        return r < 0.0 ? r + m : r;
    }
}

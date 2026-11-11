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
    /// 75 % pitch on a fixed-pitch one, 15 deg for a type whose blade has not been fitted yet. Ty's stall-speed anchor
    /// is stated "at fine pitch", so every calibration below reads this.</summary>
    public static double FinePitchDeg(DataContracts.PropulsionConfig p)
        => p is { ConstantSpeed: true, FineStopDeg: > 0.0 } ? p.FineStopDeg
         : p.BladePitch75Deg > 0.0 ? p.BladePitch75Deg : 15.0;

    /// <summary>
    /// A FLEET BMEP, Pa, and the ONLY reason it exists is that most of this fleet's displacement is not authored.
    /// 1.00 MPa = 145 psi is the mean of the SIX distinct engines whose displacement <see cref="SmokeConfig"/> does
    /// carry — Continental C-85 959 kPa, Lycoming O-320 949, the two O-360s 1007 and 1008, the Pitts' O-540 972 and the
    /// Extra's 1125 — a 17 % spread, which is what a brake mean effective pressure is: nearly a constant across
    /// naturally aspirated direct-drive engines, because that is what fixes how much power a given displacement makes.
    ///
    /// AND IT IS AN ESTIMATE, SO HERE IS HOW WRONG IT IS. Inverted against engines whose real displacement is known
    /// from elsewhere it gives the Cassutt's O-200 to 0.7 %, the Decathlon's, Archer's and Seminole's O-360s to 2 %,
    /// the Beaver's R-985 to +10 %, the SR22's IO-550 to +15 %, the Stearman's R-670 to −12 %, the JN-4's OX-5 to
    /// −29 %, the DC-3's R-1830 to +34 %, the Merlin to +65 % (it is supercharged: its BMEP is nothing like this
    /// fleet's) and the AirCam's Rotax to +180 % (it is GEARED, so maxRpm is the propeller's and not the crank's).
    /// So: good to about 15 % on a direct-drive, naturally aspirated four or six, and not to be trusted on a
    /// supercharged engine, a radial or a geared one. No type that falls back to this estimate runs the compression
    /// mechanism today — none of them has a fitted blade or fitted governor stops — and each wants its real
    /// displacement authored on <see cref="SmokeConfig.DisplacementCuIn"/> when its blade is fitted.
    /// </summary>
    public const double FleetBmepPa = 1.00e6;

    /// <summary>
    /// THE ENGINE'S SWEPT VOLUME, m^3: <see cref="SmokeConfig.DisplacementCuIn"/> where it is authored, and otherwise
    /// backed out of rated torque at <see cref="FleetBmepPa"/> — because for a four-stroke BMEP = 4π·Q_rated / V_d, and
    /// that is a definition, not a fit. <paramref name="authored"/> says which you got, so a probe can print it.
    /// </summary>
    public static double DisplacementM3(DataContracts.AircraftConfig c, out bool authored)
    {
        double cuIn = c.Smoke?.DisplacementCuIn ?? 0.0;
        authored = cuIn > 0.0;
        if (authored) return cuIn * CubicInchM3;
        DataContracts.PropulsionConfig? p = c.Propulsion;
        if (p is null || p.MaxPowerW <= 0.0 || p.MaxRpm <= 0.0) return 0.0;
        double qRated = p.MaxPowerW / (p.MaxRpm * 2.0 * Math.PI / 60.0);
        return 4.0 * Math.PI * qRated / FleetBmepPa;
    }

    public const double CubicInchM3 = 16.387064e-6;

    /// <summary>The rpm the calibrations below call "at rest": not zero, because a blade-element solve needs an omega
    /// to divide by.</summary>
    public const double AtRestRpm = 5.0;

    /// <summary>
    /// HOW HARD A CYLINDER ON COMPRESSION PUSHES BACK, N·m at sea-level ambient — AND IT IS THE ENGINE'S OWN GAS LOAD
    /// NOW, computed from its displacement and compression ratio, not solved from anything. Ty 2026-11-10, shown that
    /// the Pitts and the Extra carry the SAME AEIO-540 and were coming out at 99 and 683 N·m purely because their
    /// airframes have different Vne: "that settles it - compute from displacement". Two identical engines cannot
    /// resist differently because they are bolted to different aeroplanes.
    ///
    /// It is the same slider-crank and polytropic charge the ripple's SHAPE comes from, in absolute units instead of
    /// normalised ones. The clearance volume is V_c = V_d / (cylinders · (CR − 1)), the peak of
    /// (p/p_amb − 1)·dV/dφ over the stroke is <see cref="PeakFor"/> in units of V_c per radian, so
    ///
    ///     peak = p_amb · V_c · PeakFor(CR)
    ///
    /// which for a 541.5 cubic inch six at 8.7:1 is 326 N·m — about 20 kN on the piston through a 55 mm crank throw,
    /// and the Pitts and the Extra now agree because they are the same engine. A 361 cubic inch FOUR at the same
    /// compression comes out at 326 as well, which is right: an O-360 and an O-540 share the 5.125 in bore and the
    /// 4.375 in stroke, so they share a cylinder and differ only in how many. The Cub's 188 cubic inch four at 6.3:1
    /// is 125.
    ///
    /// WHAT THIS GAVE UP IS HIS RESTART ANCHOR — "To get prop windmilling again should take 10% under vne" — and he
    /// gave it up on purpose, told that it and a physical compression peak cannot both hold: he would rather the Pitts
    /// thud than restart at an exact number. So the restart speed is now a MEASUREMENT of this model
    /// (<see cref="RestartSpeedMs"/>, printed by CompressionStopProbe) and not an input to it, and the only thing still
    /// solved from an anchor is the dry boundary friction, from the stall speed
    /// (<see cref="PistonEngine.BoundaryExcessFor"/>).
    ///
    /// Sea-level ambient, because the charge is drawn at ambient and a cached per-type number cannot know the altitude.
    /// AircraftEngine scales the ripple by the pressure ratio where it uses it, so a dead engine at 10,000 ft has a
    /// softer lump — which is correct, and is the altitude lapse of a gas spring.
    /// </summary>
    public static double PeakNmFor(DataContracts.AircraftConfig c)
    {
        DataContracts.PropulsionConfig? p = c.Propulsion;
        if (p is null || p.PropDiameterM <= 0.0 || p.MaxPowerW <= 0.0) return 0.0;
        double displacement = DisplacementM3(c, out _);
        double cr = Math.Max(1.5, c.EngineCompressionRatio);
        double vClearance = displacement / (Math.Max(1, c.EngineCylinders) * (cr - 1.0));
        return Atmosphere.SeaLevelPressurePa * vClearance * PeakFor(cr);
    }

    /// <summary>
    /// THE SPEED AT WHICH A STOPPED PROPELLER BREAKS AWAY AND STARTS WINDMILLING AGAIN, m/s at sea level — a
    /// MEASUREMENT of this model since 2026-11-10, not an anchor into it. A stopped propeller has no stored energy, so
    /// the air on the blade at rest has to beat the mean it takes to turn the engine over PLUS the worst of the
    /// compression ripple:
    ///
    ///     Q_air(v, fine stop, at rest)  =  |Q_mean(at rest)|  +  peak · worstRipple(cylinders, CR)
    ///
    /// Both sides at rest: the mean at rest is several times the mean at rated rpm (PistonEngine's boundary friction),
    /// and an equation written at rest that used the rated figure would be wrong by that much.
    ///
    /// <paramref name="leak01"/> is how well the cylinders are still holding: 1 is a freshly stopped engine, every
    /// cylinder sealed, which is the hardest case and the one to quote. Lower is a propeller that has been standing a
    /// few seconds, and it matters enormously — the gas load is proportional to the trapped charge, so a cylinder bled
    /// to a third needs a third of the shove. That is Ty's "occasional flips through a compression stroke can occur at
    /// speeds below": the leak-down is what lets a stopped propeller creep over onto the next cylinder long before it
    /// could break away against a sealed one.
    ///
    /// Ty's original anchor was 0.9 Vne. It is no longer enforced, and what it has become is a finding — see
    /// <see cref="PeakNmFor"/> for why, and CompressionStopProbe for the number each type actually lands on.
    /// </summary>
    public static double RestartSpeedMs(DataContracts.AircraftConfig c, double leak01 = 1.0)
    {
        DataContracts.PropulsionConfig? p = c.Propulsion;
        if (p is null || p.PropDiameterM <= 0.0 || p.MaxPowerW <= 0.0) return double.NaN;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, FinePitchDeg(p), p.ActivityFactor);
        int cyl = Math.Max(1, c.EngineCylinders);
        var leak = new double[cyl];
        for (int i = 0; i < cyl; i++) leak[i] = Math.Clamp(leak01, 0.0, 1.0);
        double worst = 0.0;
        for (int i = 0; i <= 1440; i++)
        {
            double r = RippleFactor(4.0 * Math.PI * i / 1440.0, cyl, c.EngineCompressionRatio, leak);
            if (r < worst) worst = r;
        }
        double need = -PistonEngine.MotoringTorqueNm(p, AtRestRpm, 0.0, 1.0, 1.0) + PeakNmFor(c) * -worst;
        double QAir(double v) => -BladeElement.Solve(g, v, 0.0, AtRestRpm, Atmosphere.SeaLevelDensityKgM3).TorqueNm;
        double lo = 1.0, hi = 400.0;
        if (QAir(hi) < need) return double.NaN;              // no speed in the model's range can shove it over
        for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (QAir(m) >= need) hi = m; else lo = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// WHERE A TURNING PROPELLER GIVES UP, m/s — Ty's other anchor, 2026-11-10: "at fine pitch the prop stops right at
    /// level flight stall speed". Seeded from a windmill at <paramref name="startMs"/> and slowed in 0.25 m/s steps,
    /// each held 0.8 s, this integrates the same shaft equation <c>AircraftEngine</c> runs — blade torque against the
    /// motoring mean plus the compression ripple, with the crank angle and every cylinder's seal as states — and
    /// returns the speed at which the rpm finally falls below Ty's 300 rpm static-friction floor
    /// (Engine.ImpulseCoupling.StaticFrictionStopRpm). NaN if it never stops.
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
                // AND STATIC FRICTION TAKES IT AT 300, exactly as AircraftEngine.StepShaft does (Ty 2026-11-11: "if
                // rpm drops below 300 the friction should be too high for the engine to overcome static and the
                // engine simply winds down and stops"). It has to be the same law in both places or the constant
                // solved against this sweep is solved against an engine the sim does not fly.
                if (rpm < Engine.ImpulseCoupling.StaticFrictionStopRpm && qEng - qProp < 0.0) { rpm = 0.0; omega = 0.0; }
                crank = (crank + omega * dt) % (4.0 * Math.PI);
            }
            if (rpm < Engine.ImpulseCoupling.StaticFrictionStopRpm) return v;
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

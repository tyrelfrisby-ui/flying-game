namespace FlyingGame.Core.Instruments;

/// <summary>
/// Compressible flow through an orifice, the one piece of gas dynamics the air-driven gyros need. Isentropic
/// nozzle from a reservoir at (p0, T0) to a back pressure p: subsonic below the critical pressure ratio, choked
/// above it. An instrument's "effective area" is Cd·A, measured from its rated consumption rather than from
/// drawings, so no discharge coefficient has to be guessed.
/// </summary>
public static class Pneumatics
{
    public const double Gamma = 1.4;
    public const double GasConstant = 287.05;
    public const double Cp = Gamma * GasConstant / (Gamma - 1.0);
    public const double PaPerInHg = 3386.389;
    /// <summary>m³/s per cubic foot per minute.</summary>
    public const double M3sPerCfm = 4.719474e-4;
    /// <summary>"Standard" air for an SCFM rating: 1 atm, 15 °C.</summary>
    public const double StdPressurePa = 101325.0, StdTempK = 288.15;
    public static double StdDensity => StdPressurePa / (GasConstant * StdTempK);

    /// <summary>Critical pressure ratio (2/(γ+1))^(γ/(γ−1)) = 0.528: below it the throat is sonic.</summary>
    public static readonly double CriticalRatio = Math.Pow(2.0 / (Gamma + 1.0), Gamma / (Gamma - 1.0));

    /// <summary>Mass flow, kg/s, through effective area <paramref name="areaM2"/> from (p0, T0) to <paramref name="pBack"/>.</summary>
    public static double MassFlow(double areaM2, double p0, double t0, double pBack)
    {
        if (areaM2 <= 0 || p0 <= 0 || pBack >= p0) return 0.0;
        double pr = Math.Max(pBack / p0, CriticalRatio);
        double term = 2.0 * Gamma / ((Gamma - 1.0) * GasConstant * t0)
                      * (Math.Pow(pr, 2.0 / Gamma) - Math.Pow(pr, (Gamma + 1.0) / Gamma));
        return areaM2 * p0 * Math.Sqrt(Math.Max(0.0, term));
    }

    /// <summary>Jet velocity, m/s, of air expanded isentropically from (p0, T0) to <paramref name="pBack"/> (sonic once choked).</summary>
    public static double JetVelocity(double p0, double t0, double pBack)
    {
        if (p0 <= 0 || pBack >= p0) return 0.0;
        double pr = Math.Max(pBack / p0, CriticalRatio);
        return Math.Sqrt(2.0 * Cp * t0 * (1.0 - Math.Pow(pr, (Gamma - 1.0) / Gamma)));
    }

    /// <summary>The Cd·A that passes <paramref name="scfm"/> of standard air at <paramref name="suctionInHg"/> below a standard atmosphere.</summary>
    public static double AreaForRating(double scfm, double suctionInHg)
    {
        double mdot = scfm * M3sPerCfm * StdDensity;
        double perArea = MassFlow(1.0, StdPressurePa, StdTempK, StdPressurePa - suctionInHg * PaPerInHg);
        return mdot / perArea;
    }
}

/// <summary>
/// The engine-driven vacuum system of a 1970s 172, and the suction gauge that reads it.
///
/// THE CIRCUIT. Cabin air goes in through the central filter, through the attitude indicator and the
/// directional gyro in parallel (each spins its rotor on a jet of that air), into a common manifold, and out
/// through a dry rotary-vane pump on the engine's accessory pad. A relief valve (the regulator) between the
/// manifold and the cabin admits extra air whenever the suction would rise above its setting. The suction
/// gauge reads the manifold below the cabin.
///
/// SO THE SUCTION IS NOT A NUMBER SOMEBODY CHOSE. It is where the pump's flow, which falls as the manifold
/// air thins, meets the instruments' flow, which rises as the pressure difference across their jets grows.
/// At idle the pump cannot keep up and the gauge sits below the green; above a certain rpm the unregulated
/// balance would overshoot and the regulator holds it at its setting. At altitude both flows fall with the
/// air density, but not in step, so a given rpm makes less absolute suction — which is why a pilot sees the
/// needle sag on a high-altitude runup.
///
/// NUMBERS (E = engineering estimate, with why):
///  - Pump drive 1.3 × crank (E: the Lycoming O-320 accessory pad ratio for the vacuum pump).
///  - Regulator 5.0 inHg (the 172 POH's green arc is 4.5–5.4; shops set it at the middle).
///  - Attitude indicator 2.3 SCFM at 5.0 inHg, directional gyro 1.3 SCFM at 5.0 inHg (E: the order of a
///    Sigma-Tek/RC Allen data sheet for 3-1/8 in air gyros).
///  - Pump displacement is NOT taken from a catalogue (no Airborne 211CC sheet to hand). It is DERIVED from
///    one pilot-checkable fact: the regulator starts holding at <see cref="RegulatorCrackRpm"/> = 1,200 engine
///    rpm at sea level, i.e. the needle reaches the green just above a fast idle. Everything else — idle
///    suction, the sag at altitude — then follows from the gas dynamics.
///  - The manifold volume is a litre or so and empties through the instruments in well under a second, so the
///    circuit is solved quasi-statically each step.
/// </summary>
public sealed class VacuumSystem
{
    public const double DriveRatio = 1.3;
    public const double RegulatorInHg = 5.0;
    public const double AiScfm = 2.3, DgScfm = 1.3, RatedInHg = 5.0;
    public const double RegulatorCrackRpm = 1200.0;

    public static readonly double AiAreaM2 = Pneumatics.AreaForRating(AiScfm, RatedInHg);
    public static readonly double DgAreaM2 = Pneumatics.AreaForRating(DgScfm, RatedInHg);

    /// <summary>Pump displacement, m³ per pump revolution, derived from <see cref="RegulatorCrackRpm"/> (see the class comment).</summary>
    public static readonly double DisplacementM3PerRev = DeriveDisplacement();

    /// <summary>Failure hook: the pump's drive coupling has sheared (the classic dry-pump failure). No flow at all.</summary>
    public bool PumpFailed;

    // ---- the solution, this step -------------------------------------------------------------
    /// <summary>Manifold suction below ambient, Pa / inHg. What the gauge reads.</summary>
    public double SuctionPa { get; private set; }
    public double SuctionInHg => SuctionPa / Pneumatics.PaPerInHg;
    /// <summary>True while the relief valve is open and holding the setting.</summary>
    public bool Regulating { get; private set; }
    /// <summary>Air through each instrument, kg/s, and the speed of the jet it makes, m/s.</summary>
    public double AiFlowKgS { get; private set; }
    public double DgFlowKgS { get; private set; }
    public double JetMs { get; private set; }

    public void Step(double engineRpm, double p0, double t0)
    {
        double pumpRevS = PumpFailed ? 0.0 : Math.Max(0.0, engineRpm) * DriveRatio / 60.0;
        double unregulated = Balance(pumpRevS, p0, t0);
        double cap = RegulatorInHg * Pneumatics.PaPerInHg;
        Regulating = unregulated > cap;
        SuctionPa = Math.Min(unregulated, cap);
        double pm = p0 - SuctionPa;
        AiFlowKgS = Pneumatics.MassFlow(AiAreaM2, p0, t0, pm);
        DgFlowKgS = Pneumatics.MassFlow(DgAreaM2, p0, t0, pm);
        JetMs = Pneumatics.JetVelocity(p0, t0, pm);
    }

    /// <summary>
    /// The suction, Pa, at which the pump's intake flow equals the instruments' flow, with the regulator shut.
    /// Pump mass flow is the manifold density × displacement × rev/s (a positive-displacement pump moves a
    /// fixed VOLUME per turn); it falls as suction rises. Instrument flow rises with it. One crossing: bisect.
    /// </summary>
    public static double Balance(double pumpRevS, double p0, double t0)
    {
        if (pumpRevS <= 0 || p0 <= 0) return 0.0;
        double lo = 0.0, hi = p0 * 0.999;
        for (int i = 0; i < 60; i++)
        {
            double x = 0.5 * (lo + hi);
            double pm = p0 - x;
            double pump = pm / (Pneumatics.GasConstant * t0) * DisplacementM3PerRev * pumpRevS;
            double inst = Pneumatics.MassFlow(AiAreaM2 + DgAreaM2, p0, t0, pm);
            if (pump > inst) lo = x; else hi = x;
        }
        return 0.5 * (lo + hi);
    }

    private static double DeriveDisplacement()
    {
        double p0 = Pneumatics.StdPressurePa, t0 = Pneumatics.StdTempK;
        double pm = p0 - RegulatorInHg * Pneumatics.PaPerInHg;
        double inst = Pneumatics.MassFlow(AiAreaM2 + DgAreaM2, p0, t0, pm);
        double rhoM = pm / (Pneumatics.GasConstant * t0);
        return inst / rhoM / (RegulatorCrackRpm * DriveRatio / 60.0);
    }
}

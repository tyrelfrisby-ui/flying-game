using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// The electric turn coordinator and its inclinometer. ELECTRIC on purpose: in a 172 it is the one gyro that
/// survives a vacuum failure, which is the whole point of partial-panel training.
///
/// THE GYRO is a rate gyro: spin axis athwartships, ONE gimbal whose axis lies fore-and-aft but canted 30°
/// nose-up, restrained by a spring and damped by a dashpot. A rotation about the input axis (perpendicular to
/// both) makes a gyroscopic torque H·ω that the spring balances, so the gimbal — and the little aeroplane
/// geared to it — deflects in proportion to the rate. The cant puts the input axis between yaw and roll:
/// ω_in = r·cos 30° + p·sin 30°, which is why the needle moves the instant you roll in, before the turn has
/// started. The deflection is proportional to H, so a rotor that has not spun up, or is running down on a dead
/// bus, under-reads — it does not flag on its own (the flag watches the power, not the rotor).
///
/// CALIBRATION is the one place a number sets a reading, and it is how the real one is set: the spring is
/// such that the little aeroplane's wing is on the "L"/"R" mark in a coordinated standard-rate turn (3 °/s)
/// at 90 KTAS, which is a 13.9° bank. Away from that speed the bank for standard rate changes, so the needle
/// reads a hair differently — as the real one does.
///
/// THE ROTOR is a small DC motor: I·ω̇ = K·i − b·ω with i = (V − K·ω)/R. That is first order, with steady speed
/// proportional to the bus volts and two time constants — up I/(b + K²/R), down I/b. (E) 30 s up, 180 s down.
///
/// THE BALL sits at the bottom of a curved tube, i.e. where the apparent gravity points in the case's
/// athwartships plane, damped by its fluid (E 0.25 s); the tube's arc is ±12° to the stops (E).
///
/// The rotor is rated at 14.2 V, the alternator's regulated bus, because that is where it runs in flight and where
/// the spring was set. On the battery alone (12.4 V) it runs 13 % slow and the needle under-reads by as much.
///
/// (E) gimbal natural period 0.6 s, damping ratio 0.7; gimbal stops at twice standard rate; power flag in view
/// below 9 V.
/// </summary>
public sealed class TurnCoordinator
{
    public const double CantDeg = 30.0;
    public const double StandardRateDegS = 3.0, CalibrationKtas = 90.0;
    public const double RatedVolts = 14.2, FlagBelowVolts = 9.0;
    public const double SpinUpS = 30.0, RunDownS = 180.0;
    public const double GimbalPeriodS = 0.6, GimbalDamping = 0.7, StopAtRates = 2.0;
    public const double BallLagS = 0.25, BallArcDeg = 12.0;

    /// <summary>The input rate, rad/s, that puts the needle on the mark: standard rate at the calibration bank.</summary>
    public static readonly double CalibrationInputRadS = CalibrationInput();

    /// <summary>Rotor speed as a fraction of rated-at-14 V.</summary>
    public double RotorFraction;
    /// <summary>Gimbal deflection in "rates": 1.0 = the little aeroplane's wing on the R mark, −1.0 on L.</summary>
    public double Needle { get; private set; }
    private double _needleRate;
    /// <summary>Ball angle in the tube, degrees, + = right; and normalised to the tube's arc.</summary>
    public double BallDeg { get; private set; }
    public double Ball => Math.Clamp(BallDeg / BallArcDeg, -1.0, 1.0);
    public bool FlagInView { get; private set; }

    /// <summary>Failure hook: the instrument itself has failed (motor open circuit). Power flag shows; rotor runs down.</summary>
    public bool Failed;

    public void Start(in PanelInputs x, double rotorFraction = 1.0)
    {
        RotorFraction = rotorFraction;
        Needle = InputRate(x.RatesBody) / CalibrationInputRadS * rotorFraction;
        _needleRate = 0;
        BallDeg = BallTarget(x.SpecificForceBody);
    }

    public static double InputRate(Vec3 ratesBody) =>
        ratesBody.Z * Math.Cos(CantDeg * V.Deg) + ratesBody.X * Math.Sin(CantDeg * V.Deg);

    public void Step(double dt, in PanelInputs x)
    {
        double volts = Failed ? 0.0 : Math.Max(0.0, x.BusVolts);
        FlagInView = volts < FlagBelowVolts;
        // Motor: toward V/V_rated with the powered time constant, or coasting on the bearings.
        if (volts > 0.5)
            RotorFraction += (volts / RatedVolts - RotorFraction) * (1.0 - Math.Exp(-dt / SpinUpS));
        else
            RotorFraction *= Math.Exp(-dt / RunDownS);

        // Gimbal: θ̈ + 2ζω_n θ̇ + ω_n² θ = ω_n² · (H/H_r)(ω_in/ω_cal) — spring balances H·ω_in.
        double wn = 2.0 * Math.PI / GimbalPeriodS;
        double drive = RotorFraction * InputRate(x.RatesBody) / CalibrationInputRadS;
        _needleRate += (wn * wn * (drive - Needle) - 2.0 * GimbalDamping * wn * _needleRate) * dt;
        Needle += _needleRate * dt;
        if (Math.Abs(Needle) > StopAtRates) { Needle = Math.Sign(Needle) * StopAtRates; _needleRate = 0; }

        BallDeg += (BallTarget(x.SpecificForceBody) - BallDeg) * (1.0 - Math.Exp(-dt / BallLagS));
    }

    /// <summary>Where the ball would rest: the apparent gravity's lean in the case's y–z plane, degrees (+ right), stopped at the tube ends.</summary>
    public static double BallTarget(Vec3 specificForceBody)
    {
        Vec3 a = -specificForceBody;
        double deg = Math.Atan2(a.Y, a.Z) / V.Deg;
        return Math.Clamp(deg, -BallArcDeg, BallArcDeg);
    }

    private static double CalibrationInput()
    {
        double w = StandardRateDegS * V.Deg;
        double v = CalibrationKtas * 1852.0 / 3600.0;
        double bank = Math.Atan(v * w / Earth.G);
        // Coordinated turn: p = 0, q = Ω sin φ, r = Ω cos φ.
        return InputRate(new Vec3(0, w * Math.Sin(bank), w * Math.Cos(bank)));
    }
}

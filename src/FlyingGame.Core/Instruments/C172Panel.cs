using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// The failure hooks an instructor (or a scenario) pulls. Each one is the mechanical fault, not a reading: a
/// failed pump makes no suction and the gyros run down on their own physics; a dead bus stops the turn
/// coordinator's motor and the radios.
/// </summary>
public sealed class InstrumentFailures
{
    /// <summary>Vacuum pump drive sheared — the partial-panel failure.</summary>
    public bool VacuumPump;
    /// <summary>The whole electrical bus is dead (alternator AND battery): TC, radios, DME all go.</summary>
    public bool Electrical;
    public bool TurnCoordinator, Nav1, Nav2, Adf, Dme;

    public void Clear() { VacuumPump = Electrical = TurnCoordinator = Nav1 = Nav2 = Adf = Dme = false; }
}

/// <summary>
/// A 1970s Cessna 172's flight and navigation instruments, each a separate unit, stepped together:
/// vacuum system → attitude indicator and directional gyro; magnetic compass; electric turn coordinator;
/// NAV1 (VOR/LOC + glideslope), NAV2 (VOR/LOC), ADF, DME. Engine-free; the Unity side fills
/// <see cref="PanelInputs"/> and reads <see cref="WriteRig"/>.
///
/// The gyros and the compass are integrated in sub-steps of at most <see cref="MaxSubStepS"/> so a long frame
/// does not change what they do; the radios step once per call (their signal checks are throttled inside).
/// </summary>
public sealed class C172Panel
{
    public const double MaxSubStepS = 0.005;
    /// <summary>The panel's latitude, degrees north. This world has none of its own; 45° N is the middle of the
    /// northern United States, where the dip (63°) and the Earth-rate drift (10.6 °/h) are what a US
    /// pilot's compass and DG actually do.</summary>
    public double LatitudeDeg = 45.0;

    public readonly VacuumSystem Vacuum = new();
    public readonly AttitudeIndicator Ai = new();
    public readonly DirectionalGyro Dg = new();
    public readonly MagneticCompass Compass = new();
    public readonly TurnCoordinator Tc = new();
    public readonly NavReceiver Nav1 = new(hasGlideslope: true, 115.70, 360);
    public readonly NavReceiver Nav2 = new(hasGlideslope: false, 113.40, 360);
    public readonly AdfReceiver Adf = new(353.0);
    public readonly DmeUnit Dme = new();
    public readonly InstrumentFailures Failures = new();

    public double SimTimeS { get; private set; }

    /// <summary>
    /// Begin a flight. <paramref name="gyrosRunning"/> = an air start or a hot restart: rotors at speed and erect,
    /// the DG already set to the compass, needles settled. False = a cold aeroplane: every rotor stopped.
    /// </summary>
    public void Start(in PanelInputs x, bool gyrosRunning, WorldTerrain? terrain = null)
    {
        SimTimeS = 0;
        Vec3 b = Earth.MagneticFieldNed(LatitudeDeg);
        Compass.Settle(x, b);
        Vacuum.PumpFailed = Failures.VacuumPump;
        Vacuum.Step(x.EngineRpm, x.AmbientPressurePa, x.AmbientTempK);
        double frac = gyrosRunning ? 1.0 : 0.0;
        Ai.Erect(x, frac);
        Dg.Erect(x, Compass.ReadingDeg, frac);
        Tc.Start(x, gyrosRunning ? Math.Min(1.0, Volts(x) / TurnCoordinator.RatedVolts) : 0.0);
        double v = Volts(x);
        ApplyFailures();
        Nav1.Settle(x.PositionWorld, v, terrain);
        Nav2.Settle(x.PositionWorld, v, terrain);
        Adf.Settle(x.Attitude, x.PositionWorld, v, terrain);
        Dme.Step(SearchSettleS, Nav1.FreqMhz, Nav2.FreqMhz, x.PositionWorld, v, terrain);
        Dme.Step(SearchSettleS, Nav1.FreqMhz, Nav2.FreqMhz, x.PositionWorld, v, terrain);
    }

    private const double SearchSettleS = DmeUnit.SearchS + 0.1;

    private double Volts(in PanelInputs x) => Failures.Electrical ? 0.0 : x.BusVolts;

    private void ApplyFailures()
    {
        Vacuum.PumpFailed = Failures.VacuumPump;
        Tc.Failed = Failures.TurnCoordinator;
        Nav1.Failed = Failures.Nav1; Nav2.Failed = Failures.Nav2;
        Adf.Failed = Failures.Adf; Dme.Failed = Failures.Dme;
    }

    public void Step(in PanelInputs x, double dt, WorldTerrain? terrain = null)
    {
        if (dt <= 0) return;
        ApplyFailures();
        PanelInputs xs = x;
        xs.BusVolts = Volts(x);
        Vacuum.Step(x.EngineRpm, x.AmbientPressurePa, x.AmbientTempK);
        Vec3 earthRate = Earth.RateNed(LatitudeDeg);
        Vec3 field = Earth.MagneticFieldNed(LatitudeDeg);
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubStepS));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            Ai.Step(h, xs, Vacuum.AiFlowKgS, Vacuum.JetMs, earthRate);
            Dg.Step(h, xs, Vacuum.DgFlowKgS, Vacuum.JetMs, earthRate);
            Compass.Step(h, xs, field);
            Tc.Step(h, xs);
        }
        Nav1.Step(dt, x.PositionWorld, xs.BusVolts, terrain);
        Nav2.Step(dt, x.PositionWorld, xs.BusVolts, terrain);
        Adf.Step(dt, x.Attitude, x.PositionWorld, xs.BusVolts, terrain);
        Dme.Step(dt, Nav1.FreqMhz, Nav2.FreqMhz, x.PositionWorld, xs.BusVolts, terrain);
        SimTimeS += dt;
    }

    /// <summary>Everything the panel shows, into the rig's named inputs (see docs/C172-INSTRUMENTS.md for the list).</summary>
    public void WriteRig(ref RigInputs r)
    {
        r.SuctionInHg = Vacuum.SuctionInHg;
        r.AiPitchDeg = Ai.PitchDeg;
        r.AiBankDeg = Ai.BankDeg;
        r.DgCardDeg = Dg.CardDeg;
        r.DgKnobDeg = Dg.KnobDeg;
        r.CompassCardDeg = Compass.ReadingDeg;
        r.CompassCardPitchDeg = Compass.CardPitchDeg;
        r.CompassCardRollDeg = Compass.CardRollDeg;
        r.TcNeedle = Tc.Needle;
        r.TcFlag = Tc.FlagInView ? 1.0 : 0.0;
        r.SlipBall = Tc.Ball;
        r.Nav1Cdi = Nav1.Cdi; r.Nav1Gs = Nav1.Gs; r.Nav1ToFrom = Nav1.ToFrom;
        r.Nav1Flag = Nav1.Flag ? 1.0 : 0.0; r.Nav1GsFlag = Nav1.GsFlag ? 1.0 : 0.0; r.Nav1ObsDeg = Nav1.ObsDeg;
        r.Nav1FreqMhz = Nav1.FreqMhz;
        r.Nav2Cdi = Nav2.Cdi; r.Nav2ToFrom = Nav2.ToFrom; r.Nav2Flag = Nav2.Flag ? 1.0 : 0.0; r.Nav2ObsDeg = Nav2.ObsDeg;
        r.Nav2FreqMhz = Nav2.FreqMhz;
        r.AdfNeedleDeg = Adf.NeedleDeg; r.AdfCardDeg = Adf.CardDeg; r.AdfFreqKhz = Adf.FreqKhz;
        r.DmeNm = Dme.Valid ? Dme.DistanceNm : 0.0;
        r.DmeKt = Dme.Valid ? Dme.GroundSpeedKt : 0.0;
        r.DmeMin = Dme.Valid && !double.IsNaN(Dme.TimeToStationMin) ? Dme.TimeToStationMin : 0.0;
        r.DmeValid = Dme.Valid ? 1.0 : 0.0;
        r.AiRotor = Ai.Rotor.SpeedFraction; r.DgRotor = Dg.Rotor.SpeedFraction; r.TcRotor = Tc.RotorFraction;
    }
}

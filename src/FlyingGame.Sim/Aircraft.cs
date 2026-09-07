using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Assembles a RigidBody6DOF + AeroModel from an AircraftConfig and owns actuator state (control-surface
/// positions slew toward their commanded target at the configured rate, so a step input doesn't teleport
/// a surface). This is the one place airframe + surfaces + gravity come together each step.
/// </summary>
public sealed class Aircraft
{
    public AircraftConfig Config { get; }
    public MassProperties MassProperties { get; }
    public RigidBodyState State { get; set; }

    private readonly Dictionary<string, AirfoilTable> _airfoilTables;
    private double _aileronRad;
    private double _elevatorRad;
    private double _rudderRad;
    private double _spoilerFraction;
    private double _wakeStalledFrac; // hysteretic separation state (fast to grow, slow to decay)
    private double _throttle01;      // powered aircraft only
    private readonly StripFlowState _flowState = new(); // per-strip two-branch stall memory

    public Aircraft(AircraftConfig config, RigidBodyState initialState, ControlDeflections? initialDeflections = null)
    {
        Config = config;
        MassProperties = new MassProperties(
            config.Mass.MassKg,
            config.Mass.Inertia.Ixx,
            config.Mass.Inertia.Iyy,
            config.Mass.Inertia.Izz,
            config.Mass.Inertia.Ixz);
        State = initialState;
        _airfoilTables = BuildAirfoilTables(config);

        ControlDeflections initial = initialDeflections ?? ControlDeflections.Neutral;
        _aileronRad = initial.AileronRad;
        _elevatorRad = initial.ElevatorRad;
        _rudderRad = initial.RudderRad;
        _spoilerFraction = initial.SpoilerFraction;
    }

    public ControlDeflections CurrentDeflections => new(_aileronRad, _elevatorRad, _rudderRad, _spoilerFraction);

    public static Dictionary<string, AirfoilTable> BuildAirfoilTables(AircraftConfig config)
    {
        var tables = new Dictionary<string, AirfoilTable>();
        foreach (KeyValuePair<string, AirfoilTableData> kv in config.AirfoilTables)
        {
            tables[kv.Key] = new AirfoilTable(kv.Value);
        }

        return tables;
    }

    /// <summary>Advances the aircraft by one fixed timestep: shapes stick input into deflection targets (dead zone/expo/max travel), slews actuators toward them, then integrates the rigid body via RK4.</summary>
    public void Step(ControlInputs inputs, double dt)
    {
        // One lever, two meanings: powered aircraft read it as THROTTLE (full forward = full power),
        // the glider reads aft-of-neutral as speed brake (axisMap "aftOnly") — same thumb geometry.
        double spoilerTarget = Config.Propulsion is null
            ? Math.Clamp(Math.Max(0.0, inputs.ThrottleLever), 0.0, 1.0)
            : 0.0;
        _throttle01 = Config.Propulsion is null ? 0.0 : Math.Clamp((1.0 - inputs.ThrottleLever) * 0.5, 0.0, 1.0);
        ControlDeflections targets = new(
            ShapeAxis(inputs.Aileron, Config.Controls.Aileron),
            ShapeAxis(inputs.Elevator, Config.Controls.Elevator),
            ShapeAxis(inputs.Rudder, Config.Controls.Rudder),
            spoilerTarget);

        StepWithDeflectionTargets(targets, dt);
    }

    /// <summary>
    /// Advances the aircraft by one fixed timestep toward explicit deflection targets, bypassing the RC
    /// stick shaping. Used to spawn/hold a solved trim (e.g. tow-release trim, per VERTICAL-SLICE.md)
    /// without needing to invert the dead-zone/expo curve to find the stick position that reproduces it.
    /// </summary>
    public void StepWithDeflectionTargets(ControlDeflections targets, double dt)
    {
        _aileronRad = SlewTo(_aileronRad, targets.AileronRad, Config.Controls.Aileron.RateRadPerSec, dt);
        _elevatorRad = SlewTo(_elevatorRad, targets.ElevatorRad, Config.Controls.Elevator.RateRadPerSec, dt);
        _rudderRad = SlewTo(_rudderRad, targets.RudderRad, Config.Controls.Rudder.RateRadPerSec, dt);
        _spoilerFraction = Math.Clamp(targets.SpoilerFraction, 0.0, 1.0);

        ControlDeflections controls = CurrentDeflections;

        double altitudeM = -State.Position.Z;
        double airDensity = Atmosphere.DensityAtAltitude(altitudeM);
        Vec3 windBody = Atmosphere.WindAtPosition(State.Position);
        double weightN = MassProperties.MassKg * Atmosphere.GravityMs2;

        // Stall hysteresis: the separated wake develops quickly (~0.25 s) but washes out slowly
        // (~1.0 s). Feeding the LAGGED fraction to the aero model stops the wake band flickering
        // on/off across the stall boundary — the relaxation cycle that made fast spins fall out.
        double instantFrac = AeroModel.InstantStalledFraction(Config, State.Velocity, State.Rates, windBody);
        double tau = instantFrac > _wakeStalledFrac ? Config.StallDynamics.WakeGrowTau : Config.StallDynamics.WakeDecayTau;
        _wakeStalledFrac += (instantFrac - _wakeStalledFrac) * (1.0 - Math.Exp(-dt / tau));

        int stripCount = Config.Surfaces.Sum(s => s.Strips.Count);
        _flowState.EnsureSize(stripCount);

        (Vec3 Force, Vec3 Moment) ForceMoment(RigidBodyState s)
        {
            (Vec3 aeroForce, Vec3 aeroMoment) = AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls, _wakeStalledFrac, _flowState);
            Vec3 gravityWorld = new(0, 0, weightN);
            Vec3 gravityBody = s.Attitude.Conjugate().Rotate(gravityWorld);
            Vec3 totalF = aeroForce + gravityBody;
            Vec3 totalM = aeroMoment;
            if (Config.Propulsion is not null)
            {
                (Vec3 pF, Vec3 pM) = PropModel.Compute(Config.Propulsion, _throttle01, s.Velocity, s.Rates, airDensity);
                totalF += pF;
                totalM += pM;
            }
            return (totalF, totalM);
        }

        State = RigidBody6DOF.IntegrateRK4(State, dt, MassProperties, ForceMoment);

        // Advance per-strip separation memory (two-branch stall hysteresis): a strip SEPARATES fast
        // above 16 deg local alpha, REATTACHES slowly below 11 deg, and holds in the band between —
        // so the inner wing stays committed to the separated branch while the outer wing flies the
        // attached one at the same |alpha| history. LocalAlphaRad was recorded during the last stage.
        const double sepOn = 16.0 * Math.PI / 180.0, sepOff = 11.0 * Math.PI / 180.0;
        for (int i = 0; i < stripCount; i++)
        {
            double a = Math.Abs(_flowState.LocalAlphaRad[i]);
            double s0 = _flowState.Separation[i];
            double target = a > sepOn ? 1.0 : a < sepOff ? 0.0 : s0;
            double tauSec = target > s0 ? Config.StallDynamics.StripSepTau : Config.StallDynamics.StripReattachTau;
            _flowState.Separation[i] = s0 + (target - s0) * (1.0 - Math.Exp(-dt / tauSec));
        }
    }

    private static double ShapeAxis(double input, ControlAxisConfig axis)
    {
        double clamped = Math.Clamp(input, -1.0, 1.0);
        double magnitude = Math.Abs(clamped);
        if (magnitude <= axis.DeadZone)
        {
            return 0.0;
        }

        double rescaled = Math.Clamp((magnitude - axis.DeadZone) / (1.0 - axis.DeadZone), 0.0, 1.0);
        double shaped = axis.Expo * rescaled * rescaled * rescaled + (1.0 - axis.Expo) * rescaled;
        return Math.Sign(clamped) * shaped * axis.MaxDeflRad;
    }

    private static double SlewTo(double current, double target, double ratePerSec, double dt)
    {
        double maxStep = Math.Abs(ratePerSec) * dt;
        double delta = target - current;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }
}

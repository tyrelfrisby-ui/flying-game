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
        // axisMap "aftOnly": neutral (0) and forward (negative) both mean stowed; only aft (positive) deploys.
        double spoilerTarget = Math.Clamp(Math.Max(0.0, inputs.ThrottleLever), 0.0, 1.0);
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

        (Vec3 Force, Vec3 Moment) ForceMoment(RigidBodyState s)
        {
            (Vec3 aeroForce, Vec3 aeroMoment) = AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls);
            Vec3 gravityWorld = new(0, 0, weightN);
            Vec3 gravityBody = s.Attitude.Conjugate().Rotate(gravityWorld);
            return (aeroForce + gravityBody, aeroMoment);
        }

        State = RigidBody6DOF.IntegrateRK4(State, dt, MassProperties, ForceMoment);
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

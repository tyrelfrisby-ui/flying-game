using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Sim;

/// <summary>
/// Assembles a RigidBody6DOF + AeroModel from an AircraftConfig and owns actuator state (control-surface
/// positions slew toward their commanded target at the configured rate, so a step input doesn't teleport
/// a surface). This is the one place airframe + surfaces + gravity come together each step.
/// </summary>
/// <summary>
/// Airframe structural state — REAL g limits (owner request 2026-09-08). Limit loads come from
/// AircraftConfig.Limits (GMax/GMin, the FAR 23/25 limit load factors); ultimate = limit ×
/// Limits.GroanFactor (1.5) and Limits.UltimateFactor (2.0). Three thresholds, not two: inside the LIMIT
/// the structure is rated and silent; past the limit it is an exceedance the syllabus grades but the
/// airframe still says nothing; past the GROAN threshold it creaks and pops (<see cref="OverLimitSeverity"/>
/// 0..1, <see cref="Pops"/>); past ULTIMATE for a short continuous dwell the WINGS SEPARATE
/// (<see cref="WingsFailed"/>) — the sim then flies the fuselage, tail, gear and engine without them.
/// Per-instance state only: a fresh Aircraft (ResetFlight) is intact.
/// </summary>
public sealed class StructuralState
{
    /// <summary>Continuous time past ultimate needed before the wings let go — long enough to reject a
    /// single 5 ms RK4 spike or a gust tick, short enough that a real overstress snaps them.</summary>
    public const double FailureDwellSec = 0.05;

    /// <summary>The rated LIMIT load factors: what the structure carries without permanent deformation. Exceeding
    /// these is an exceedance for scoring (Standards.GLimit) even though nothing is heard yet.</summary>
    public double LimitPosG { get; }
    public double LimitNegG { get; }

    /// <summary>Where the airframe starts to complain: limit × Limits.GroanFactor. Nothing is audible inside this.</summary>
    public double GroanPosG { get; }
    public double GroanNegG { get; }

    /// <summary>Where the wings come off: limit × Limits.UltimateFactor.</summary>
    public double UltimatePosG { get; }
    public double UltimateNegG { get; }

    /// <summary>0 at the GROAN threshold, ramping to 1 at ultimate (by sign of n). Drives the groan's loudness and
    /// the pop rate. Zero everywhere below the groan threshold, including between limit and groan.</summary>
    public double OverLimitSeverity { get; internal set; }

    /// <summary>How many times the structure has POPPED — a rivet letting go, a spar creaking over. Monotonic while
    /// the airframe is intact; the bridge plays one sound per increment. The sim decides WHEN (deterministically, so
    /// a test can count them); the bridge varies the timbre, so nothing random lives in the physics.</summary>
    public int Pops { get; internal set; }

    /// <summary>Raised once per pop, with the severity at that moment (0..1).</summary>
    public event Action<double>? OnPop;

    /// <summary>Pops per second at a given severity: a slow tick just past the groan threshold rising to a rattle at
    /// ultimate. Deliberately audible as separate events rather than a texture.</summary>
    public static double PopsPerSec(double severity) =>
        severity <= 0.0 ? 0.0 : 1.5 + 10.5 * Math.Clamp(severity, 0.0, 1.0);

    private double _popAccum;

    /// <summary>Advance the structural model one step at load factor <paramref name="n"/>. Returns true on the step
    /// the wings have been past ultimate long enough to let go — the caller does the separating, because dropping the
    /// wing surfaces and re-deriving the inertia is the Aircraft's business, not this bookkeeping's.</summary>
    public bool Step(double n, double dt)
    {
        if (WingsFailed) { OverLimitSeverity = 0.0; return false; }   // nothing left to groan
        OverLimitSeverity = SeverityFor(n);
        AccumulatePops(OverLimitSeverity, dt);
        bool beyondUltimate = n > UltimatePosG || n < UltimateNegG;
        OverUltimateDwellSec = beyondUltimate ? OverUltimateDwellSec + dt : 0.0;
        return OverUltimateDwellSec >= FailureDwellSec;
    }

    /// <summary>Advance the pop clock. Raises as many pops as the elapsed time earned, so a long slow frame does not
    /// swallow them.</summary>
    private void AccumulatePops(double severity, double dt)
    {
        if (severity <= 0.0) { _popAccum = 0.0; return; }
        _popAccum += PopsPerSec(severity) * dt;
        while (_popAccum >= 1.0)
        {
            _popAccum -= 1.0;
            Pops++;
            OnPop?.Invoke(severity);
        }
    }

    /// <summary>Continuous time (s) the load has been beyond ultimate; resets when it comes back inside.</summary>
    public double OverUltimateDwellSec { get; internal set; }

    /// <summary>True once the wings have separated (latched until a fresh Aircraft is built).</summary>
    public bool WingsFailed { get; internal set; }

    /// <summary>Sim time (Atmosphere.SimTimeSec) of the failure, for bridges that poll.</summary>
    public double FailureTimeSec { get; internal set; }

    /// <summary>Raised ONCE, on the step the wings separate.</summary>
    public event Action? OnWingsFailed;

    private readonly LimitsConfig _limits;
    private readonly double _massKg;

    /// <summary>The structure of an aeroplane of this total mass. The thresholds are the active category's limits at or
    /// below VA with the airbrakes in, scaled down when the non-lifting parts outweigh what the wings were certificated
    /// to carry (<see cref="LimitsConfig.NonLiftingFactor"/>) -- the wing-root bending that breaks a wing is n times
    /// that weight, whatever the speed. A type with none of the envelope fields gets exactly its GMax/GMin.</summary>
    internal StructuralState(LimitsConfig limits, double massKg = 0.0)
    {
        _limits = limits; _massKg = massKg;
        double ultimate = limits.UltimateFactor > 1.0 ? limits.UltimateFactor : 2.0;
        double groan = limits.GroanFactor > 1.0 ? limits.GroanFactor : 1.5;
        if (groan >= ultimate) groan = 1.0 + (ultimate - 1.0) * 0.5;   // a config that inverts them still gets a band
        (double pos, double neg) = limits.LimitAt(0.0, 0.0, massKg);
        LimitPosG = pos > 0.0 ? pos : 10.0;
        LimitNegG = neg < 0.0 ? neg : -10.0;
        GroanPosG = LimitPosG * groan;
        GroanNegG = LimitNegG * groan;
        UltimatePosG = LimitPosG * ultimate;
        UltimateNegG = LimitNegG * ultimate;
    }

    /// <summary>Severity of load factor n against the groan/ultimate band on its own sign: 0 anywhere below the groan
    /// threshold (the limit included), 1 at or beyond ultimate.</summary>
    public double SeverityFor(double n)
    {
        if (n > GroanPosG)
        {
            return Math.Clamp((n - GroanPosG) / (UltimatePosG - GroanPosG), 0.0, 1.0);
        }

        if (n < GroanNegG)
        {
            return Math.Clamp((GroanNegG - n) / (GroanNegG - UltimateNegG), 0.0, 1.0);
        }

        return 0.0;
    }

    /// <summary>True when the load factor is past the rated limit — an exceedance for scoring, whether or not the
    /// airframe has started to complain yet.</summary>
    public bool OverLimit(double n) => n > LimitPosG || n < LimitNegG;

    /// <summary>The rated limit load factors at this indicated airspeed and airbrake deflection: the manual's envelope
    /// narrows from VA to VNE and again with the brakes out. What an exceedance is graded against in flight; the
    /// groan and the wing failure stay on <see cref="LimitPosG"/>/<see cref="LimitNegG"/>, because bending strength
    /// does not depend on speed.</summary>
    public (double Pos, double Neg) LimitAt(double iasMs, double spoilerFraction) => _limits.LimitAt(iasMs, spoilerFraction, _massKg);

    /// <summary>Past the rated limit at this speed and airbrake deflection.</summary>
    public bool OverLimitAt(double n, double iasMs, double spoilerFraction)
    {
        (double pos, double neg) = LimitAt(iasMs, spoilerFraction);
        return n > pos || n < neg;
    }

    internal void RaiseFailed() => OnWingsFailed?.Invoke();
}

public sealed partial class Aircraft
{
    public AircraftConfig Config { get; }
    public MassProperties MassProperties { get; private set; }
    public RigidBodyState State { get => _state; set { _state = value; _wheelsUnseeded = true; } }
    private RigidBodyState _state;
    /// <summary>The state was put somewhere from outside a step (spawn, teleport, a test): the wheels are given the spin
    /// that state implies before the next step, so an aeroplane placed rolling does not start with a skid.</summary>
    private bool _wheelsUnseeded = true;
    /// <summary>Each leg's wheel: its spin and what it is doing on the ground (WheelSpin), config.Gear order.</summary>
    public IReadOnlyList<WheelState> Wheels => _wheels;
    private WheelState[] _wheels = System.Array.Empty<WheelState>();

    /// <summary>Structural g-limit state (limit/ultimate loads, groan severity, wing failure).</summary>
    public StructuralState Structure { get; }

    /// <summary>Per-surface aero mask (indexed like Config.Surfaces): null while intact; after wing failure
    /// the wing surfaces are false. Config.Surfaces itself is never mutated — other systems index it.</summary>
    private readonly List<AirframeComponent> _pendingBreaks = new();
    private readonly HashSet<AirframeComponent> _lostGear = new();   // legs torn off (LandingGear skips them)
    private bool[] _gearTouching = System.Array.Empty<bool>();       // per-leg contact state, Config.Gear order
    /// <summary>Is any leg on the ground or in the water (the last step's contact state).</summary>
    public bool AnyGearTouching => System.Array.IndexOf(_gearTouching, true) >= 0;
    private double[] _gearStageClear = System.Array.Empty<double>(); // per leg: clearance and margin from a step's first RK4 stage
    private Vec3[] _wheelBodies = System.Array.Empty<Vec3>();         // each leg's contact point (body), for the step's clear test
    private readonly ImpactRecorder _impacts = new();
    /// <summary>Castoring tailwheel yaw (LandingGear.TailwheelState); null for types without a steerable tailwheel.</summary>
    public LandingGear.TailwheelState? Tailwheel { get; }
    /// <summary>The rudder-linked nosewheel's yaw (rad, + = turned RIGHT, the way right pedal turns it), exactly the angle
    /// LandingGear.Compute steers the tyre by; 0 for a type whose nosewheel does not steer. For the visual rig.</summary>
    public double NoseWheelSteerRad
    {
        get
        {
            double cmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
            return cmd * LandingGear.NoseWheelSteerMaxRad(Config);   // = -SteerRad: + is right here, left in Compute's frame
        }
    }
    private bool[]? _surfaceMask;
    private bool[]? _stripMask;                 // per-strip aero mask (broken-off components), AeroModel strip order
    private readonly List<ContactPoint> _contacts;
    private readonly HashSet<AirframeComponent> _lost = new();
    /// <summary>Airframe hard points (wing tips, tail, nose, cabin top …) that cannot pass through the ground.</summary>
    public IReadOnlyList<ContactPoint> ContactPoints => _contacts;
    /// <summary>Components that have broken off (hard ground/solid impact).</summary>
    public IReadOnlyCollection<AirframeComponent> LostComponents => _lost;
    /// <summary>Bullet damage: hits per part, fuel leaks and fires on the inner wing panels (Combat/Damage.cs).</summary>
    public FlyingGame.Core.Combat.DamageState Damage { get; }
    /// <summary>Guns and ammunition (Combat/Gunnery.cs); null for types without armament defined.</summary>
    public FlyingGame.Core.Combat.Armament Guns { get; }
    public bool IsLost(AirframeComponent c) => _lost.Contains(c);
    /// <summary>Raised once per component, on the step it breaks off.</summary>
    public event Action<AirframeComponent>? ComponentLost;
    /// <summary>Raised after a step in which a hard point or wheel first touched the ground/a solid: (closing speed m/s, point name). Drives the crash sound.</summary>
    public event Action<double, string>? HardImpact;

    /// <summary>The hardest first contact of the last step that had one (what HardImpact reported, with the surface it
    /// struck and where): read by CrashRelay to tell land from water. Set before ComponentLost fires for that step.</summary>
    public ImpactRecorder.Hit LastImpact { get; private set; }

    /// <summary>The airframe's own volumes in the water: buoyancy, water drag, added mass and flooding through its
    /// openings (Core/Wreck/AirframeFlooding). Inactive away from water.</summary>
    public AirframeFlooding Hydro => _hydro ??= new AirframeFlooding(Config, Load);
    private AirframeFlooding? _hydro;

    /// <summary>Float compartments (14 CFR 23.751: four per float), null for a type without floats. Sealed unless a
    /// lesson or scenario punctures one (<see cref="PunctureFloat"/>).</summary>
    public FloatFlooding? FloatFlood { get; }

    /// <summary>Put a hole in a float compartment (lessons/scenarios). False if the type has no such float.</summary>
    public bool PunctureFloat(FloatPuncture p) => FloatFlood?.Puncture(p) ?? false;

    /// <summary>Mass fraction each wing takes with it (light/utility/transport types: ~12 % of MTOW per wing).</summary>
    public const double WingMassFractionPerWing = 0.12;

    private readonly Dictionary<string, AirfoilTable> _airfoilTables;
    private double _aileronRad;
    private double _elevatorRad;
    private double _rudderRad;
    private double _spoilerFraction;
    private double _wakeStalledFrac; // hysteretic separation state (fast to grow, slow to decay)
    /// <summary>Lagged separated-wake fraction the aero model is flying with (0 attached … 1 fully stalled).</summary>
    public double StalledFraction => _wakeStalledFrac;
    private double _throttle01;      // powered aircraft only
    public double FlapFraction { get; set; }   // 0..1, set by cockpit/challenge
    /// <summary>When true, every force the physics applies is recorded per step into <see cref="LastForces"/>
    /// (body frame) for the force-vector overlay.</summary>
    public bool CaptureForces { get; set; }
    public List<ForceSample> LastForces { get; private set; } = new();

    /// <summary>Landing gear command (retractable types): true = down. Fixed gear is always down.</summary>
    public bool GearDown { get; private set; } = true;
    /// <summary>0 = up and stowed .. 1 = down and locked; travels over ~4 s.</summary>
    public double GearExtension { get; private set; } = 1.0;
    public const double GearTravelSec = 4.0;
    /// <summary>Full gear travel time (s) for this airframe; the game's default, the widget's 6 s.</summary>
    public double GearTravelTime { get; set; } = GearTravelSec;
    /// <summary>Speed-brake / spoiler lever 0 (retracted) .. 1 (extended), separate from the throttle; used only on types
    /// whose config has spoiler panels (Controls.Spoiler.MaxDeflRad &gt; 0).</summary>
    public double SpoilerCommand { get; set; }
    /// <summary>Slipstream swirl at the tail as a fraction of the axial speed-up (0.2; 0 = no swirl — diagnostics).</summary>
    public static double SwirlFactor = 0.2;
    public void SetGear(bool down, bool immediate = false)
    {
        if (!Config.RetractableGear) { GearDown = true; GearExtension = 1.0; return; }
        GearDown = down;
        if (immediate) GearExtension = down ? 1.0 : 0.0;
    }
    /// <summary>
    /// The water-rudder retract (Ty 2026-11-02, t278u; floatplanes whose config draws a retractable blade). The blade
    /// is spring-loaded DOWN: with the cockpit handle let go of it lies on the floor and the rudder is down, which is
    /// how every aircraft starts (a new Aircraft is a water start with the rudder down). Hanging the handle on its hook
    /// raises it. 0 = down .. 1 = up (on the hook).
    /// </summary>
    public bool HasWaterRudderRetract => Config.Floats is { WaterRudderRetractable: true, WaterRudderChordM: > 0 };
    /// <summary>The handle is on the hook (target: up). False = let go of (target: down, the spring).</summary>
    public bool WaterRudderHooked { get; private set; }
    /// <summary>Where the blade is, 0 = down .. 1 = up.</summary>
    public double WaterRudderUp01 { get; private set; }
    /// <summary>A hand holding the handle (VR) sets the cable pull directly, 0..1 (the fraction of the way from the floor
    /// to the hook); null = nobody is holding it, so the hook or the spring decides.</summary>
    public double? WaterRudderHeldUp01 { get; set; }
    /// <summary>Hand time to lift the handle from the floor onto the hook (or unhook it and let the spring take it):
    /// a judgement of the reach and the hang, not a measured number. The blade follows the cable.</summary>
    public const double WaterRudderTravelSec = 0.8;
    public void SetWaterRudderHooked(bool hooked, bool immediate = false)
    {
        if (!HasWaterRudderRetract) { WaterRudderHooked = false; WaterRudderUp01 = 0; return; }
        WaterRudderHooked = hooked;
        if (immediate) WaterRudderUp01 = hooked ? 1.0 : 0.0;
    }
    /// <summary>The phone / controller toggle: hang the handle on the hook, or take it off.</summary>
    public void ToggleWaterRudder() => SetWaterRudderHooked(!WaterRudderHooked);
    public void SetEngineThrottleScale(int idx, double scale) { if (idx>=0 && idx<Config.Engines.Count) Config.Engines[idx].ThrottleScale = System.Math.Clamp(scale,0,1); }
    public double SlatFraction { get; set; }   // 0..1 (auto or manual)
    /// <summary>The whole-aircraft parachute, on types that carry one (owner 2026-10-16). Always present as
    /// an object; <see cref="BallisticRecovery.Out"/> is false until somebody pulls the handle, and nothing
    /// happens at all unless <see cref="AircraftConfig.Brs"/> is set.</summary>
    public BallisticRecovery Brs { get; } = new();
    /// <summary>
    /// Pull the handle. Returns false if there is no parachute to pull. Vpd is an INDICATED speed, so what
    /// is judged here is the indicated one — a deployment at 140 kt indicated is the same event at any
    /// height, which is the whole reason the limit is written that way.
    /// </summary>
    public bool DeployBrs(double aglM)
    {
        if (Config.Brs is null || Brs.Out) return false;
        Vec3 windWorld = Atmosphere.WindAtPosition(State.Position);
        Vec3 windBody = windWorld.LengthSquared > 1e-9 ? State.Attitude.Conjugate().Rotate(windWorld) : Vec3.Zero;
        double tas = (State.Velocity - windBody).Length;
        double rho = Atmosphere.DensityAt(State.Position);
        double ias = tas * System.Math.Sqrt(rho / 1.225);
        Brs.Deploy(Config.Brs, ias, aglM);
        return true;
    }

    /// <summary>Extra flat-plate drag area (m²) on top of the airframe's own, set from outside the sim each frame —
    /// today an OPEN CANOPY in the headset's hands-on bail-out (Core Vr.CanopyDynamics.DragAreaM2). Applied along the
    /// relative wind at <see cref="ExtraDragPointBody"/> (body axes, the config's frame), so it also yaws and pitches.</summary>
    public double ExtraDragAreaM2 { get; set; }
    public Vec3 ExtraDragPointBody { get; set; }
    public double BrakeInput { get; set; }     // 0..1 wheel braking (on ground), both sides
    /// <summary>Differential braking bias -1 (left only) .. +1 (right only): rudder pedal toe-brake feel.
    /// Left/right brake = BrakeInput × (1 ∓ bias) clamped to 0..1.</summary>
    public double BrakeBias { get; set; }
    private readonly StripFlowState _flowState = new(); // per-strip two-branch stall memory
    /// <summary>The per-strip flow and damage state (tests and probes read the crash damage in it).</summary>
    public StripFlowState FlowStateForTests => _flowState;

    /// <summary>External world-frame force (N) applied at ExternalForcePointBody — the aerotow rope
    /// tension, winch, etc. Set by the coupling each step; zero by default.</summary>
    public Vec3 ExternalForceWorld;
    public Vec3 ExternalForcePointBody;

    /// <summary>New mass and inertia (live weight &amp; balance — the Aero Widget's loading command). The CG itself is
    /// Config.Mass.Cg, which every force and moment is taken about.</summary>
    public void SetMassProperties(double massKg, double ixx, double iyy, double izz, double ixz) =>
        MassProperties = Shed(new MassProperties(massKg, ixx, iyy, izz, ixz));

    /// <summary>Mass that has LEFT the aeroplane in flight (a departed canopy, Core Vr.CanopyDynamics; a component broken
    /// off with its volumes, <see cref="LoseComponent"/>), and the
    /// first moment of it about the reference CG, kg·m (body frame). Zero unless something has gone.</summary>
    public double ShedMassKg { get; private set; }
    private Vec3 _shedMoment;

    /// <summary>The CG of what is left, body frame: Config.Mass.Cg moved by −m·r / (M − m). Forces and moments are still
    /// taken about Config.Mass.Cg (every geometry in the config is placed relative to it); the shift enters as the
    /// remaining weight's moment about it (see the force sum), which is exactly what moving the CG does to the
    /// statics. The dynamics' coupling term m·r×a is neglected (a 14 kg canopy 0.5 m from the CG of an 800 kg aeroplane).</summary>
    public Vec3 CgShiftBody => MassProperties.MassKg > 1.0 ? _shedMoment * (-1.0 / MassProperties.MassKg) : Vec3.Zero;

    /// <summary>A part of <paramref name="kg"/> at <paramref name="pointBody"/> (the config's body frame) has left:
    /// the mass drops, the inertia loses its parallel-axis share, and the CG moves away from where it was.</summary>
    public void ShedMass(double kg, Vec3 pointBody)
    {
        if (kg <= 0.0) return;
        ShedMassKg += kg;
        _shedMoment += (pointBody - Config.Mass.CgVec()) * kg;
        MassProperties = Shed(MassProperties, kg, pointBody - Config.Mass.CgVec());
    }

    private MassProperties Shed(MassProperties p) =>
        ShedMassKg > 0.0 ? Shed(p, ShedMassKg, _shedMoment * (1.0 / ShedMassKg)) : p;

    private static MassProperties Shed(MassProperties p, double kg, Vec3 d) => new(
        Math.Max(1.0, p.MassKg - kg),
        Math.Max(1.0, p.Ixx - kg * (d.Y * d.Y + d.Z * d.Z)),
        Math.Max(1.0, p.Iyy - kg * (d.X * d.X + d.Z * d.Z)),
        Math.Max(1.0, p.Izz - kg * (d.X * d.X + d.Y * d.Y)),
        p.Ixz - kg * d.X * d.Z);

    public Aircraft(AircraftConfig config, RigidBodyState initialState, ControlDeflections? initialDeflections = null)
    {
        Config = config;
        Load = new AircraftLoad(config);
        // At the reference loading — and for every aeroplane with no loading block at all — this is exactly the
        // authored MassConfig, so nothing that is already tuned moves. See AircraftLoad.
        MassProperties = Load.Properties;
        State = initialState;
        _airfoilTables = BuildAirfoilTables(config);
        _contacts = AirframeContact.BuildPoints(config);
        InitContactDamage();
        _gearTouching = new bool[config.Gear.Count];
        _gearStageClear = new double[2 * config.Gear.Count];
        _wheels = WheelState.For(config);
        _wheelBodies = config.Gear.Select(g => g.PosVec()).ToArray();
        Damage = new FlyingGame.Core.Combat.DamageState(LoseComponent, IsLost);
        foreach (GearConfig g in config.Gear) if (g.IsTailwheel && g.IsSteerable) { Tailwheel = new LandingGear.TailwheelState(); break; }
        Guns = FlyingGame.Core.Combat.Armament.For(config);
        foreach (ContactPoint cp in _contacts) if (cp.Name == "nose") _noseBody = cp.Body;
        Structure = new StructuralState(config.Limits, config.Mass.MassKg);
        if (config.Floats is not null) FloatFlood = new FloatFlooding(config);
        InitFloatWaterLoads();
        // The water the floats already carry at the start (an aeroplane spawned afloat): without this the first step
        // took all of it as newly entrained — a slam out of nothing, 420 kN on a Beaver starting with its heels wet.
        if (config.Floats is not null) _floatAddedMass = FloatHydro.AddedMassOf(config, initialState);

        ControlDeflections initial = initialDeflections ?? ControlDeflections.Neutral;
        _aileronRad = initial.AileronRad;
        _elevatorRad = initial.ElevatorRad;
        _rudderRad = initial.RudderRad;
        _spoilerFraction = initial.SpoilerFraction;
        _spawnTrim = initial;
    }

    private readonly ControlDeflections _spawnTrim;   // the deflections the aircraft spawned trimmed at (tab calibration)

    public ControlDeflections CurrentDeflections => new(_aileronRad, _elevatorRad, _rudderRad, _spoilerFraction);

    /// <summary>Replay (display only): put the aircraft in a recorded pose without stepping the physics — state, control
    /// surfaces, power, flaps, gear and g — so every visual, gauge and sound that reads the aircraft shows the recording.
    /// The per-strip stall memory, tailwheel and structure are untouched, so restoring the live pose resumes exactly.</summary>
    public void SetReplayPose(RigidBodyState state, ControlDeflections d, double throttle01, double flapFraction, double gearExtension, double loadFactorZ)
    {
        State = state;
        _aileronRad = d.AileronRad; _elevatorRad = d.ElevatorRad; _rudderRad = d.RudderRad; _spoilerFraction = d.SpoilerFraction;
        _throttle01 = throttle01;
        FlapFraction = flapFraction;
        GearExtension = gearExtension;
        LoadFactorZ = loadFactorZ;
        AeroLoadFactorZ = loadFactorZ;
    }

    // ---- telemetry shared by audio / structure / HUD / net (read-only) ----------------------------

    /// <summary>Commanded power 0..1 (0 for gliders).</summary>
    public double Throttle01 => _throttle01;

    /// <summary>The bus behind the master, alternator, fuel-pump and smoke switches (ElectricalSystem); stepped
    /// with the sim. Every type has one; it starts with the master and the alternator on.</summary>
    public FlyingGame.Core.Engine.ElectricalSystem Electrics { get; } = new();

    /// <summary>What is in the aeroplane: people, fuel, baggage. The loading page sets it before flight; in flight
    /// only the fuel changes, and the mass, CG and inertia follow it every step.</summary>
    public AircraftLoad Load { get; }

    /// <summary>Once anything has broken off or the wings have gone, DAMAGE owns the mass tensor — it scales the mass
    /// and cuts the inertia — and the loading stops re-deriving it, or the next step would quietly put the wings'
    /// inertia back. Fuel still drains; it just no longer moves the CG of an aeroplane that is coming apart.</summary>
    private bool _massOwnedByDamage;

    /// <summary>Re-derive the mass properties from <see cref="Load"/>. The loading page calls this after an edit.
    /// Ignored once damage owns the tensor.</summary>
    public void ApplyLoad()
    {
        if (!_massOwnedByDamage) MassProperties = Shed(Load.Properties);
    }

    /// <summary>Fuel mass burned per second at the current throttle. Shaft power is MaxPowerW x throttle
    /// (PropModel), so this is BSFC x that. For a JET (propDiameterM &lt;= 0) MaxPowerW is thrust in newtons, so the
    /// same coefficient reads as a thrust-specific consumption in kg per newton-second — a turbine wants its own
    /// number authored, the default is a piston's.</summary>
    public double FuelFlowKgPerSec
    {
        get
        {
            if (Config.Propulsion is null || Load.FuelCapacityKg <= 0.0 || _noseLost) return 0.0;   // stopped: burns nothing
            double bsfc = Config.Propulsion.BsfcKgPerWs;
            if (bsfc <= 0.0) return 0.0;
            // A dead engine burns nothing; a running one burns what the mixture meters (full rich: as it always did) at
            // the power the governor lets it make.
            if (!EngineRunning) return 0.0;
            double meter = HasMixture ? Core.Engine.MixtureModel.FuelFraction(MixtureLever) : 1.0;
            double gov = HasPropLever ? GovernorPowerFactor() : 1.0;
            return Config.Propulsion.MaxPowerW * Math.Clamp(_throttle01, 0.0, 1.0) * bsfc * meter * gov;
        }
    }

    /// <summary>
    /// Body-z load factor in g from the NON-gravitational forces (aero + thrust + gear + hydro + rope)
    /// at the last force evaluation of the step: +1 in level flight, +4 in a 4 g pull, negative
    /// pushed over. Sign: body z is DOWN, so lift (−z) gives a positive n.
    /// </summary>
    public double LoadFactorZ { get; private set; } = 1.0;
    /// <summary>The part of <see cref="LoadFactorZ"/> the AIR makes (aero normal force over weight): what loads a wing
    /// panel in bending as lift does. A fuselage thumping onto the ground decelerates the aeroplane at several g without the
    /// wing lifting at all; that load reaches a panel only through its own contact points (AircraftContactDamage).</summary>
    public double AeroLoadFactorZ { get; private set; } = 1.0;

    /// <summary>
    /// Specific force, body axes, m/s²: every non-gravitational force on the airframe over its mass, at the last
    /// force evaluation of the step — what an accelerometer, a compass float or an inclinometer ball feels.
    /// (0, 0, −g) in level flight. Unlike <see cref="LoadFactorZ"/> it includes buoyancy, because a float in
    /// a compass bowl feels the water holding the aeroplane up just as it feels the wing.
    /// </summary>
    public Vec3 SpecificForceBody { get; private set; } = new(0, 0, -9.80665);

    /// <summary>Engine speed (rpm) for piston/prop types from the prop model's throttle→rpm law; 0 for gliders.
    /// Jets (PropDiameterM == 0) report a pseudo-N1 0..100 as MaxRpm-scaled throttle.</summary>
    /// <summary>Engine rpm for the audio/instruments. Constant-speed prop: below ~30 % throttle the prop sits on
    /// its fine-pitch stop and rpm follows power; above it the governor holds GovernedRpm, rising to MaxRpm only
    /// with the throttle firewalled (take-off). Fixed pitch: rpm follows throttle and picks up with airspeed
    /// (a fixed-pitch prop unloads as the aircraft accelerates: ~2300 static, red-line at cruise speed).</summary>
    public double EngineRpm
    {
        get
        {
            PropulsionConfig p = Config.Propulsion!;
            if (p is null || _noseLost) return 0.0;   // engine stopped (prop strike)
            // Not running (cut-off, too lean): the air turns the prop, or nothing does. The throttle has no say.
            if (!EngineRunning) return WindmillRpm;
            double idle = p.IdleRpm, max = p.MaxRpm, thr = _throttle01;
            if (p.ConstantSpeed) return GovernedRpmAt(_propLever);   // the governor holds what the prop lever sets
            // Fixed pitch: rpm follows the power the engine really makes (the mixture's share; full rich = the throttle).
            if (HasMixture) thr *= PowerScale;
            double vRef = Config.SpawnIasMs > 1 ? Config.SpawnIasMs : 30.0;
            double speedFactor = 0.82 + 0.18 * Math.Clamp(State.Velocity.Length / vRef, 0.0, 1.0);
            return idle + (max - idle) * Math.Pow(Math.Max(0.0, thr), 0.8) * speedFactor;
        }
    }

    public static Dictionary<string, AirfoilTable> BuildAirfoilTables(AircraftConfig config)
    {
        var tables = new Dictionary<string, AirfoilTable>();
        foreach (KeyValuePair<string, AirfoilTableData> kv in config.AirfoilTables)
        {
            tables[kv.Key] = new AirfoilTable(kv.Value);
        }
        // The tails' finite-span section tables (AeroModel.SectionTableKey: stab + elevator, fin + rudder as one section).
        foreach (KeyValuePair<string, AirfoilTableData> kv in AeroModel.DerivedSectionTables(config))
            tables[kv.Key] = new AirfoilTable(kv.Value);

        return tables;
    }

    // ---- structure ---------------------------------------------------------------------------------

    /// <summary>
    /// Which surfaces are the MAIN WING (the set that leaves on structural failure), indexed like
    /// config.Surfaces. Rule: every surface whose id contains "wing" — the same tag AeroModel uses for
    /// wake/downwash/spoiler logic, and it already covers the aileron rows ("wing-aileron") and both
    /// biplane planes ("wing-upper"/"wing-lower" + their aileron rows). Fallback for a config with no
    /// "wing" tag at all: the horizontal (non-vertical) surface with the largest strip area, plus any
    /// surface whose id starts with that surface's id (its control rows). Tail surfaces never qualify.
    /// </summary>
    public static bool[] MainWingSurfaces(AircraftConfig config)
    {
        var isWing = new bool[config.Surfaces.Count];
        bool anyTagged = false;
        for (int i = 0; i < config.Surfaces.Count; i++)
        {
            if (config.Surfaces[i].Id.Contains("wing", StringComparison.OrdinalIgnoreCase))
            {
                isWing[i] = true;
                anyTagged = true;
            }
        }

        if (anyTagged)
        {
            return isWing;
        }

        int best = -1;
        double bestArea = 0.0;
        for (int i = 0; i < config.Surfaces.Count; i++)
        {
            SurfaceConfig s = config.Surfaces[i];
            string id = s.Id;
            bool vertical = id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || id.Contains("vertical", StringComparison.OrdinalIgnoreCase)
                            || id.Contains("rudder", StringComparison.OrdinalIgnoreCase) || id.Contains("fin", StringComparison.OrdinalIgnoreCase);
            bool tail = id.Contains("stab", StringComparison.OrdinalIgnoreCase) || id.Contains("elevator", StringComparison.OrdinalIgnoreCase)
                        || id.Contains("tail", StringComparison.OrdinalIgnoreCase);
            if (vertical || tail)
            {
                continue;
            }

            double area = s.Strips.Sum(st => st.Area);
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }

        if (best >= 0)
        {
            string wingId = config.Surfaces[best].Id;
            for (int i = 0; i < config.Surfaces.Count; i++)
            {
                if (i == best || (wingId.Length > 0 && config.Surfaces[i].Id.StartsWith(wingId, StringComparison.OrdinalIgnoreCase)))
                {
                    isWing[i] = true;
                }
            }
        }

        return isWing;
    }

    /// <summary>
    /// Break a component off NOW (a hard ground/solid impact does this itself; exposed for scenarios/tests).
    /// Its strips leave the aero model — a lost wing half flies on as a half-winged aircraft with the
    /// rolling moment that implies, a lost tail loses its pitch/yaw stability — and its contact points go
    /// with it. A lost nose stops the propeller. Idempotent.
    ///
    /// MASS (decision for Ty, AGENT-LOG NEEDS YOU, 2026-11-02 — this replaces the 2026-09 rule that left mass and
    /// inertia alone). Every piece that breaks off is now a body of its own in the wreck (CrashRelay/WreckSim), with
    /// the mass of the elements it took (Core/Wreck/AirframeHydroModel's Raymer buildup). Leaving that mass on the
    /// aeroplane counted it twice, and — worse — left the remainder's weight acting at the WHOLE aeroplane's CG: a
    /// Pitts cockpit with its engine still on and its tail and wings gone sank TAIL first, held up at the back by
    /// the weight of parts it no longer had. So the aeroplane sheds exactly what the wreck piece took
    /// (<see cref="ShedMass"/>).
    /// </summary>
    public void LoseComponent(AirframeComponent comp)
    {
        // From here the mass tensor is damage's: the loading model must stop re-deriving it (see ApplyLoad).
        _massOwnedByDamage = true;
        if (!_lost.Add(comp)) return;
        int total = 0; foreach (SurfaceConfig sf in Config.Surfaces) total += sf.Strips.Count;
        _stripMask ??= Enumerable.Repeat(true, total).ToArray();
        int idx = 0;
        foreach (SurfaceConfig sf in Config.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool wing = id.Contains("wing"), hTail = id == "hstab" || id == "elevator", vTail = id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            bool aileron = id.Contains("aileron"), elevator = id == "elevator", rudder = id.StartsWith("rudder");
            double semi = WingPanels.Semispan(Config);
            foreach (StripConfig st in sf.Strips)
            {
                double y = st.Pos[1];
                AirframeComponent? panel = wing ? WingPanels.PanelOf(y, semi) : null;
                bool gone = comp switch
                {
                    AirframeComponent.WingLeft => wing && y < -0.3,
                    AirframeComponent.WingRight => wing && y > 0.3,
                    AirframeComponent.WingLeftOuter => panel == AirframeComponent.WingLeftOuter,
                    AirframeComponent.WingRightOuter => panel == AirframeComponent.WingRightOuter,
                    AirframeComponent.WingLeftInner => panel == AirframeComponent.WingLeftInner || panel == AirframeComponent.WingLeftOuter,
                    AirframeComponent.WingRightInner => panel == AirframeComponent.WingRightInner || panel == AirframeComponent.WingRightOuter,
                    AirframeComponent.AileronLeft => aileron && y < 0,
                    AirframeComponent.AileronRight => aileron && y > 0,
                    AirframeComponent.ElevatorLeft => elevator && y < 0,
                    AirframeComponent.ElevatorRight => elevator && y > 0,
                    AirframeComponent.Rudder => rudder,
                    AirframeComponent.TailHorizontal => hTail,
                    AirframeComponent.TailVertical => vTail,
                    AirframeComponent.TailBoom => hTail || vTail,
                    _ => false,
                };
                if (gone) _stripMask[idx] = false;
                idx++;
            }
        }
        if (comp != AirframeComponent.Cabin) _contacts.RemoveAll(p => p.Component == comp);   // the cabin's own points stay: it is what is left
        // Panels: the inner panel carries the outer one (and its aileron); the whole-wing failure drops both panels.
        if (comp == AirframeComponent.WingLeftInner) { LoseComponent(AirframeComponent.WingLeftOuter); LoseComponent(AirframeComponent.AileronLeft); }
        if (comp == AirframeComponent.WingRightInner) { LoseComponent(AirframeComponent.WingRightOuter); LoseComponent(AirframeComponent.AileronRight); }
        if (comp == AirframeComponent.WingLeftOuter) LoseComponent(AirframeComponent.AileronLeft);
        if (comp == AirframeComponent.WingRightOuter) LoseComponent(AirframeComponent.AileronRight);
        if (comp == AirframeComponent.WingLeft) { _lost.Add(AirframeComponent.WingLeftInner); _lost.Add(AirframeComponent.WingLeftOuter); _lost.Add(AirframeComponent.AileronLeft); _contacts.RemoveAll(p => p.Component is AirframeComponent.WingLeftInner or AirframeComponent.WingLeftOuter); }
        if (comp == AirframeComponent.WingRight) { _lost.Add(AirframeComponent.WingRightInner); _lost.Add(AirframeComponent.WingRightOuter); _lost.Add(AirframeComponent.AileronRight); _contacts.RemoveAll(p => p.Component is AirframeComponent.WingRightInner or AirframeComponent.WingRightOuter); }
        if (comp == AirframeComponent.TailHorizontal || comp == AirframeComponent.TailBoom) { _lost.Add(AirframeComponent.ElevatorLeft); _lost.Add(AirframeComponent.ElevatorRight); }
        if (comp == AirframeComponent.TailVertical || comp == AirframeComponent.TailBoom) _lost.Add(AirframeComponent.Rudder);
        if (comp is AirframeComponent.GearLeft or AirframeComponent.GearRight or AirframeComponent.GearNose or AirframeComponent.GearTail)
        {
            // The leg is gone: the wheel no longer carries anything; a stub hard point 45 % up the leg does.
            _lostGear.Add(comp);
            Vec3 cgv = Config.Mass.CgVec();
            foreach (GearConfig g in Config.Gear)
                if (AirframeContact.GearComponent(g) == comp)
                    _contacts.Add(new ContactPoint { Body = new Vec3(cgv.X + g.Pos[0], cgv.Y + g.Pos[1], cgv.Z + g.Pos[2] * 0.55), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "gear-stub" });
        }
        if (comp == AirframeComponent.TailBoom)
        {
            // The aft fuselage with the tail surfaces: no tail aero, no tail hard points, the tailwheel goes with it.
            _contacts.RemoveAll(p => p.Component is AirframeComponent.TailHorizontal or AirframeComponent.TailVertical);
            (_, double tailCutX) = AirframeContact.FuselageStations(Config);
            _contacts.Add(new ContactPoint { Body = new Vec3(tailCutX, 0, 0.35), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "boom-stub" });
            LoseComponent(AirframeComponent.GearTail);
        }
        if (comp == AirframeComponent.Cabin)
        {
            // Mid-fuselage slam: the fuselage breaks into its three sections — nose and tail boom off, cabin left.
            LoseComponent(AirframeComponent.Nose);
            LoseComponent(AirframeComponent.TailBoom);
        }
        if (comp == AirframeComponent.Nose || comp == AirframeComponent.Propeller)
        {
            _noseLost = true;   // prop strike / nose gone: the engine stops
            for (int i = 0; i < Config.Engines.Count; i++) SetEngineThrottleScale(i, 0.0);
            if (comp == AirframeComponent.Nose) LoseComponent(AirframeComponent.GearNose);   // the nose leg hangs off the firewall
        }
        if (comp == AirframeComponent.NacelleLeft || comp == AirframeComponent.NacelleRight)
        {
            for (int i = 0; i < Config.Engines.Count; i++)
                if ((Config.Engines[i].Pos[1] < 0) == (comp == AirframeComponent.NacelleLeft)) SetEngineThrottleScale(i, 0.0);
        }
        // Wing panels outboard of their structural station, and what their ailerons and slave struts do (contact damage).
        OnComponentLostStructure(comp);
        // The volumes it takes with it (CrashRelay turns them into a wreck piece), and their mass with them.
        ShedMass(Hydro.Detach(comp));
        ComponentLost?.Invoke(comp);
    }

    /// <summary>
    /// Take the mass of elements that have left the airframe off the rigid body. Every force and moment in this class
    /// is taken about Config.Mass.Cg (the reference point P), so P stays the reference and the remainder's true CG is
    /// carried as an offset c from it:
    ///   m' = m − Σm_e;   c' = (m·c − Σ m_e·r_e) / m'   (r_e from P);
    ///   I_P' = I_P − Σ [box inertia of e + m_e·(parallel axis to P)], products (Ixy, Ixz, Iyz) included, so momentum
    ///   is conserved at the separation;  weight m'·g acts at P + c' (the gravity moment c' × W in ForceMoment).
    /// The rigid-body equations are then written about P rather than the CG, which leaves out the coupling term
    /// m'·c' × a_P: exact at rest and in steady sinking, an approximation while it tumbles (|c'| is tens of cm).
    /// </summary>
    private void ShedMass(List<FlyingGame.Core.Wreck.HydroElement> gone)
    {
        if (gone.Count == 0) return;
        Vec3 cg0 = Config.Mass.CgVec();
        double dm = 0, ix = 0, iy = 0, iz = 0, pxy = 0, pxz = 0, pyz = 0;
        Vec3 first = Vec3.Zero;
        void Box(FlyingGame.Core.Wreck.HydroElement e, ref double x, ref double y, ref double z)
        {
            Vec3 r = e.Centre - cg0, h = e.Half;
            x += e.MassKg * ((h.Y * h.Y + h.Z * h.Z) / 3 + r.Y * r.Y + r.Z * r.Z);
            y += e.MassKg * ((h.X * h.X + h.Z * h.Z) / 3 + r.X * r.X + r.Z * r.Z);
            z += e.MassKg * ((h.X * h.X + h.Y * h.Y) / 3 + r.X * r.X + r.Y * r.Y);
        }
        foreach (var e in gone)
        {
            dm += e.MassKg; first += (e.Centre - cg0) * e.MassKg; Box(e, ref ix, ref iy, ref iz);
            // Products of inertia about P: an axis-aligned box has none of its own, only its parallel-axis share. A
            // piece off ONE side takes Ixy and Iyz with it, and the rest is left asymmetric (2026-11-03: momentum).
            Vec3 r = e.Centre - cg0;
            pxy += e.MassKg * r.X * r.Y; pxz += e.MassKg * r.X * r.Z; pyz += e.MassKg * r.Y * r.Z;
        }
        if (dm <= 0) return;
        MassProperties mp = MassProperties;
        double m1 = mp.MassKg - dm;
        if (m1 <= 1.0) return;   // nothing would be left: a broken config, keep the body as it is
        // One account of what has left, shared with ShedMass(kg, point) (a departed canopy): CgShiftBody = −Σm·r / m'.
        ShedMassKg += dm; _shedMoment += first;
        // Exactly what the piece takes, so linear AND angular momentum are conserved at the separation (the piece leaves
        // with the same ω and with v + ω×r: WreckSim.Add). Until 2026-11-03 the remainder was floored at the inertia of the
        // wreck model's remaining boxes; those boxes carry 1.3–2.3× the config's published inertia (Raymer's 12.2 kg/m² on
        // a fabric wing: PA-18 boxes Ixx 2,733 against 1,450), so the floor acted on every wing loss and handed the
        // aeroplane angular momentum it never had (PA-18 tip panel: L_x 1,072 → 2,047). The only floor left keeps the
        // tensor positive: a tenth of what it was, logged when it acts.
        // The one floor: what is left can never have less inertia about P than its own remaining boxes have. Exact shedding
        // only goes below that when the books are inconsistent (the model's boxes carry more than the config's inertia on
        // that axis); there conservation cannot hold whatever is done, and a remnant lighter in inertia than its own parts
        // is a stiff rigid body the contact springs drive unstable (a C172 cabin left alone went 6 → 387 m/s in a step).
        double bx = 0, by = 0, bz = 0;
        var attached = Hydro.Attached;
        for (int k = 0; k < attached.Count; k++) Box(attached[k], ref bx, ref by, ref bz);
        // And a separation never ADDS inertia: where the boxes already carry more than the aeroplane has (the Extra's pitch
        // axis, its engine far forward of a light config Iyy), the floor is what it had.
        double Floor(double i, double boxes, double had, string axis)
        {
            double min = System.Math.Max(System.Math.Min(boxes, had), 1.0);
            if (i >= min) return i;
            Hydro.DamageLog.Add($"inertia floor on {axis}: {i:F0} kg·m² left, its own boxes carry {boxes:F0} — momentum not conserved on this axis");
            return min;
        }
        var next = new MassProperties(m1,
            Floor(mp.Ixx - ix, bx, mp.Ixx, "Ixx"), Floor(mp.Iyy - iy, by, mp.Iyy, "Iyy"), Floor(mp.Izz - iz, bz, mp.Izz, "Izz"), mp.Ixz - pxz,
            mp.Ixy - pxy, mp.Iyz - pyz);
        if (!PositiveDefinite(next))
        {
            Hydro.DamageLog.Add("inertia products dropped: the tensor left was not positive definite — momentum not conserved");
            next = new MassProperties(m1, next.Ixx, next.Iyy, next.Izz, 0, 0, 0);
        }
        MassProperties = next;
    }
    private static bool PositiveDefinite(MassProperties m)
    {
        double a = m.Ixx, b = -m.Ixy, c = -m.Ixz, d = m.Iyy, e = -m.Iyz, f = m.Izz;
        return a > 0 && a * d - b * b > 0 && a * (d * f - e * e) - b * (b * f - c * e) + c * (b * e - c * d) > 0;
    }
    private bool _noseLost;

    // Water: one query at the CG per step while anywhere near a surface; far above one (or over land), only every
    // half second — 100 m of margin is more than any aeroplane closes in that time.
    private bool _hydroArmed;
    private FloatHydro.AddedMass _floatAddedMass = FloatHydro.AddedMass.None;

    /// <summary>
    /// A float's ultimate water load, N — 14 CFR 23.527 (hull and main float load factors): the limit water reaction
    /// load factor n_w = C1·V_S0² / (tan^(2/3) β · W^(1/3)), C1 = 0.012, V_S0 in knots, W in pounds, β the deadrise at
    /// the step; for twin floats each float is the hull of a fictitious seaplane of half the weight (23.527(b)); times
    /// the 1.5 factor of safety (23.303). V_S0 is the type's own (PracticeScenario.EstimateVso, power off, no flap).
    /// Above it the float's bottom splits (FloatFlooding): the aeroplane does not come apart, it starts to fill on that
    /// side — the damage a float actually takes from a landing far harder than it was designed for. 0 = no floats.
    /// </summary>
    public double FloatUltimateWaterLoadN { get; private set; }
    /// <summary>The limit water load factor n_w that load is built on (23.527), for the log and tests.</summary>
    public double FloatLimitWaterLoadFactor { get; private set; }
    private readonly bool[] _floatSplit = new bool[2];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> VsoCache = new();

    private void InitFloatWaterLoads()
    {
        if (Config.Floats is null || FloatFlood is null) return;
        double vso = VsoCache.GetOrAdd(Config.Id ?? "", _ => Practice.PracticeScenario.EstimateVso(Config, 0.0));
        int floats = Config.Floats.Count == 1 ? 1 : 2;
        double wLb = Config.Mass.MassKg * 2.20462 / floats;
        double vKt = vso * 1.943844;
        double beta = Config.Floats.DeadriseDeg * System.Math.PI / 180;
        FloatLimitWaterLoadFactor = 0.012 * vKt * vKt / (System.Math.Pow(System.Math.Tan(beta), 2.0 / 3.0) * System.Math.Pow(wLb, 1.0 / 3.0));
        FloatUltimateWaterLoadN = 1.5 * FloatLimitWaterLoadFactor * Config.Mass.MassKg * 9.81 / floats;
    }

    /// <summary>Peak water load each main float has carried, N (tests and the log).</summary>
    public readonly double[] FloatPeakWaterLoadN = new double[2];

    /// <summary>After the step: each main float's water reaction — FloatHydro's forces, plus its share of the entrained
    /// water's momentum (the added-mass slam, applied implicitly after the RK4 step, so it is not in those forces) —
    /// against <see cref="FloatUltimateWaterLoadN"/>.</summary>
    private void CheckFloatWaterLoads(RigidBodyState beforeAm, RigidBodyState afterAm, double dt)
    {
        if (FloatUltimateWaterLoadN <= 0 || dt <= 0) return;
        double dvUp = -(afterAm.Attitude.Rotate(afterAm.Velocity) - beforeAm.Attitude.Rotate(beforeAm.Velocity)).Z;
        double slamN = System.Math.Max(0.0, dvUp) * MassProperties.MassKg / dt;
        int n = Config.Floats!.Count == 1 ? 1 : 2;
        double l0 = System.Math.Max(0, FloatHydro.Floats[0].WaterUpN), l1 = n == 2 ? System.Math.Max(0, FloatHydro.Floats[1].WaterUpN) : 0;
        double sum = l0 + l1;
        for (int i = 0; i < n; i++)
        {
            double own = i == 0 ? l0 : l1;
            double load = own + slamN * (sum > 1e-6 ? own / sum : 1.0 / n);
            if (load > FloatPeakWaterLoadN[i]) FloatPeakWaterLoadN[i] = load;
            if (_floatSplit[i] || load <= FloatUltimateWaterLoadN) continue;
            _floatSplit[i] = true;
            string side = n == 1 ? "hull" : i == 0 ? "left float" : "right float";
            string why = $"{side} bottom split at the step: {load / 1000:F1} kN of water load against {FloatUltimateWaterLoadN / 1000:F1} (14 CFR 23.527 ultimate)";
            // A buckled bottom panel at the step: a 300 × 6 mm split, running up the side above the waterline.
            PunctureFloat(new FloatPuncture(i, 2, 0.30 * 0.006, 0.30 * 0.002, why));
            Hydro.DamageLog.Add(why);
        }
    }
    private double _hydroRecheckSec;
    private void PrepareWater(double dt)
    {
        _hydroRecheckSec -= dt;
        bool wet = _hydro is { EverWet: true } || (FloatFlood is not null);
        if (!wet && _hydroRecheckSec > 0) { _hydroArmed = false; return; }
        double? w = FloatHydro.WaterSurfaceAt(State.Position.X, State.Position.Y, -State.Position.Z);
        double above = w.HasValue ? -State.Position.Z - w.Value : double.PositiveInfinity;
        if (!wet && above > 100.0) { _hydroArmed = false; _hydroRecheckSec = 0.5; return; }
        _hydroArmed = true;
        Hydro.Prepare(State, w);
    }
    private Vec3 _noseBody = new(1.5, 0, 0);
    private PropModel.PropDrive _drive = PropModel.PropDrive.Running;
    /// <summary>The wash over the tail the last step flew with (axial m/s, tube radius m, swirl m/s): 0 with no prop turning.</summary>
    public (double DeltaU, double Radius, double Swirl) LastTailSlipstream { get; private set; }
    /// <summary>True once the propeller has hit the ground (or the nose is gone): engine stopped, no thrust. DAMAGE only:
    /// an engine stopped by its mixture is <see cref="EngineStarved"/>; <see cref="EngineRunning"/> covers both.</summary>
    public bool EngineStopped => _noseLost;

    // ---- the fuel shut-off valve (the Great Lakes' and the Extra's red lever, one in each cockpit, linked) ----------

    /// <summary>The fuel shut-off valve: true = ON (the lever down). The cockpit's hands set it; nothing else does, so an
    /// aeroplane without a valve in its cockpit always flies with it open.</summary>
    public bool FuelValveOpen { get; set; } = true;

    /// <summary>Seconds of running on the fuel already past the valve (lines, servo, flow divider) once it is shut: an
    /// injected 540 at cruise empties them in a few seconds. Then the engine stops.</summary>
    public const double FuelLinesSec = 4.0;

    /// <summary>Seconds after the valve is opened again before the fuel reaches the injectors and the windmilling
    /// engine fires (the sim has no starter: a running prop restarts on fuel alone).</summary>
    public const double FuelRelightSec = 2.0;

    private double _fuelLinesSec = FuelLinesSec;
    private bool _fuelStarved;

    /// <summary>True while the engine is dead for want of fuel at the valve (shut long enough). Like a cut-off mixture it
    /// is not damage: <see cref="EngineRunning"/> is false, <see cref="EngineStarved"/> true, the prop windmills.</summary>
    public bool FuelStarved => _fuelStarved;

    private void UpdateFuelValve(double dt)
    {
        if (Config.Propulsion is null) return;
        if (!FuelValveOpen)
        {
            _fuelLinesSec = Math.Max(0.0, _fuelLinesSec - dt);
            if (_fuelLinesSec <= 0.0) _fuelStarved = true;
            return;
        }
        if (_fuelStarved)
        {
            // Refilling: the lines fill over FuelRelightSec; the engine fires once they are full.
            _fuelLinesSec = Math.Min(FuelLinesSec, _fuelLinesSec + dt * FuelLinesSec / FuelRelightSec);
            if (_fuelLinesSec >= FuelLinesSec) _fuelStarved = false;
            return;
        }
        _fuelLinesSec = FuelLinesSec;
    }

    // ---- the cowl flap (the Pitts' lever under the left knee, Ty 2026-11-04 19:31: CLOSED / MID / OPEN) ----------

    /// <summary>The cowl flap, 0 CLOSED .. 1 OPEN. Open: more cooling air (the CHT the cockpit reads falls,
    /// EngineTemperatures) and a little drag, <see cref="CowlFlapDragAreaM2"/> of flat plate at full open.</summary>
    public double CowlFlap01 { get => _cowlFlap01; set => _cowlFlap01 = Math.Clamp(value, 0.0, 1.0); }
    private double _cowlFlap01;
    /// <summary>(E) 0.02 m² (0.22 ft²) of flat plate for a fully open cowl flap on a 200 hp single: a few per cent of the
    /// Pitts' parasite drag, not a measured number.</summary>
    public const double CowlFlapDragAreaM2 = 0.02;
    /// <summary>Strip-level aero mask (AeroModel strip order), null while every component is attached.</summary>
    public bool[]? StripMask => _stripMask;
    /// <summary>Surfaces flying (null = all): false where a whole surface has gone (FailWings).</summary>
    public bool[]? SurfaceMask => _surfaceMask;

    /// <summary>True when surface index i (into Config.Surfaces) still contributes aero.</summary>
    public bool IsSurfaceActive(int surfaceIndex) =>
        _surfaceMask is null || surfaceIndex < 0 || surfaceIndex >= _surfaceMask.Length || _surfaceMask[surfaceIndex];

    /// <summary>
    /// Separate the wings NOW (the sim calls this itself when the ultimate load is exceeded; exposed so a
    /// scenario/test can do it directly). Aero afterwards is fuselage + tail + gear + hydro + propulsion:
    /// wing surfaces (and their aileron/flap/slat strips and spoiler panels) are masked out of the strip
    /// loop; mass drops by ~12 % per wing and the inertias shed the wings' share (most of the roll inertia,
    /// a good part of the yaw inertia, little of the pitch inertia). CG shift is ignored. Idempotent.
    /// </summary>
    public void FailWings()
    {
        if (Structure.WingsFailed)
        {
            return;
        }

        bool[] wings = MainWingSurfaces(Config);
        _surfaceMask = new bool[wings.Length];
        for (int i = 0; i < wings.Length; i++)
        {
            _surfaceMask[i] = !wings[i];
        }

        const double massKeep = 1.0 - 2.0 * WingMassFractionPerWing;   // 0.76
        MassProperties = new MassProperties(
            MassProperties.MassKg * massKeep,
            MassProperties.Ixx * 0.30,   // wings carry ~70 % of roll inertia
            MassProperties.Iyy * 0.90,   // wing mass sits near the CG in x: pitch inertia barely changes
            MassProperties.Izz * 0.60,   // wing span contributes ~40 % of yaw inertia
            MassProperties.Ixz * 0.50);

        _massOwnedByDamage = true;
        Hydro.Detach(AirframeComponent.WingLeft);
        Hydro.Detach(AirframeComponent.WingRight);
        Structure.WingsFailed = true;
        Structure.OverLimitSeverity = 0.0;
        Structure.OverUltimateDwellSec = 0.0;
        Structure.FailureTimeSec = Atmosphere.SimTimeSec;
        Structure.RaiseFailed();
    }

    /// <summary>Per-step structural bookkeeping from the load factor of the step just integrated.</summary>
    private void UpdateStructure(double dt)
    {
        if (Structure.Step(LoadFactorZ, dt)) FailWings();
    }

    /// <summary>Burn the fuel this step used and let the mass, CG and inertia follow it. An aeroplane with no tanks
    /// authored burns nothing and this does nothing at all.</summary>
    private void UpdateLoad(double dt)
    {
        double flow = FuelFlowKgPerSec;
        if (flow <= 0.0) return;
        double drawn = Load.BurnFuel(flow * dt);
        if (drawn > 0.0) ApplyLoad();
    }

    /// <summary>Advances the aircraft by one fixed timestep: shapes stick input into deflection targets (dead zone/expo/max travel), slews actuators toward them, then integrates the rigid body via RK4.</summary>
    public void Step(ControlInputs inputs, double dt)
    {
        _pendingBreaks.Clear();
        _impacts.Reset();
        Brs.Step(dt);
        // One lever, two meanings: powered aircraft read it as THROTTLE (full forward = full power),
        // the glider reads aft-of-neutral as speed brake (axisMap "aftOnly") — same thumb geometry.
        double spoilerTarget = Config.Propulsion is null
            ? Math.Clamp(Math.Max(0.0, inputs.ThrottleLever), 0.0, 1.0)
            : 0.0;
        // A separate speed-brake lever (the Aero Widget's, 2026-10-08) on types that have the panels.
        if (Config.Controls.Spoiler.MaxDeflRad > 0.0) spoilerTarget = Math.Max(spoilerTarget, Math.Clamp(SpoilerCommand, 0.0, 1.0));
        _throttle01 = Config.Propulsion is null ? 0.0 : Math.Clamp((1.0 - inputs.ThrottleLever) * 0.5, 0.0, 1.0);
        UpdateFuelValve(dt);
        ControlDeflections targets = new(
            ShapeAxis(inputs.Aileron, Config.Controls.Aileron),
            ShapeAxis(inputs.Elevator, Config.Controls.Elevator),
            ShapeAxis(inputs.Rudder, Config.Controls.Rudder),
            spoilerTarget);

        // REVERSIBLE controls, released: the surface floats. Its target is where the hinge moment balances; it settles
        // there with the linkage's time constant (the slew below then passes it through unchanged).
        if (inputs.AileronFree || inputs.ElevatorFree || inputs.RudderFree)
        {
            double a = inputs.AileronFree && Config.Controls.Aileron.Reversible
                ? Settle(_aileronRad, FreeFloat("aileron", Config.Controls.Aileron, targets.AileronRad, _aileronRad), Config.Controls.Aileron, dt) : targets.AileronRad;
            double e = inputs.ElevatorFree && Config.Controls.Elevator.Reversible
                ? Settle(_elevatorRad, FreeFloat("elevator", Config.Controls.Elevator, targets.ElevatorRad, _elevatorRad), Config.Controls.Elevator, dt) : targets.ElevatorRad;
            double r = inputs.RudderFree && Config.Controls.Rudder.Reversible
                ? Settle(_rudderRad, FreeFloat("rudder", Config.Controls.Rudder, targets.RudderRad, _rudderRad), Config.Controls.Rudder, dt) : targets.RudderRad;
            targets = new ControlDeflections(a, e, r, spoilerTarget);
        }

        StepWithDeflectionTargets(targets, dt);
        if (!_tabCalibrated)
        {
            // First step after spawn: set the tabs from the real flow so a free surface holds the SPAWN trim (the deflections
            // the aircraft was created with — the trim slider is preset to the same), whatever the stick did this step.
            _tabCalibrated = true;
            CalibrateTabs();
        }
    }

    /// <summary>First-order settling of a free surface toward its float angle (linkage inertia + friction).</summary>
    private static double Settle(double current, double target, ControlAxisConfig axis, double dt)
        => current + (target - current) * (1.0 - Math.Exp(-dt / Math.Max(0.01, axis.FreeTauS)));

    /// <summary>
    /// Where a FREE reversible surface trails (owner 2026-10-02): the deflection δ at which its hinge moment is zero,
    ///   H(δ) = Σ g·qSc·Chα·α_flow + Σ qSc·Chδ·(δ − δ_tab) − K·(δ − δ_springNeutral) = 0,
    /// summed over the surface's HINGED strips (g = the sign of the strip's control gain, so linked ailerons cancel their symmetric float and
    /// only roll-rate / sideslip differences move them). α_flow is the flow over the fixed surface ahead of the hinge, from
    /// the last aero evaluation (downwash, slipstream, sideslip, rates included). The trim tab zeroes the air load at δ_tab
    /// = trim + the calibration offset (set on the first step from the spawn's real flow, so hands-off holds the spawn
    /// trim); away from the trimmed condition the surface floats with the flow — stick-free stability, lighter than
    /// stick-fixed. K = the centering spring (CenteringSpringKt: the airspeed where it equals the air's own restoring
    /// stiffness); its neutral is 0, or the trim for a "spring" (bungee / cartridge) trim. No air and no spring: it stays.
    /// </summary>
    public double FreeFloat(string surface, ControlAxisConfig axis, double trimRad, double currentRad)
    {
        (double a, double b, double k) = HingeTerms(surface, axis);
        double den = k - b;
        if (den <= 0.0 && b > 0.0)
        {
            // Net DESTABILIZING (reversed flow beats the spring): no balance point holds — the surface runs away from
            // the unstable one and slams against the stop on whichever side it is already displaced (control slam).
            double unstable = Math.Abs(den) > 1e-9 ? -a / -den : 0.0;
            double side = currentRad - unstable;
            if (Math.Abs(side) < 1e-6) side = a != 0.0 ? a : 1.0;
            return Math.Sign(side) * axis.LimitFor(Math.Sign(side));
        }
        if (den < 1e-6) return currentRad;
        double d;
        if (axis.TrimType == "spring" && k > 1e-9)
        {
            // SPRING trim (bungee / spring cartridge, no tab): the air acts on the bare surface, the trim sets the spring's
            // neutral — H = a + b·δ − k·(δ − δ_s). Faster, the air wins and the surface trails; slower, the spring wins.
            double neutral = trimRad + TabOffset(surface);
            d = (a + k * neutral) / den;
        }
        else
        {
            // TAB trim: zero air load at δ_tab; any centering spring pulls toward 0.
            double tab = trimRad + TabOffset(surface);
            d = (a - b * tab) / den;
        }
        return axis.ClampDefl(d);
    }

    /// <summary>The hinge-moment terms of <see cref="FreeFloat"/>: a (flow), b (deflection, negative), k (spring).</summary>
    private (double a, double b, double k) HingeTerms(string surface, ControlAxisConfig axis)
    {
        double a = 0.0, b = 0.0, scRef = 0.0;
        int idx = 0;
        foreach (SurfaceConfig sf in Config.Surfaces)
        {
            foreach (StripConfig st in sf.Strips)
            {
                int i = idx++;
                // Only the HINGED rows (gain ±1) carry hinge moment; the fixed surface ahead also lists the control (gain ~0.45:
                // the deflection's camber effect on it) but isn't on the hinge.
                if (st.Control is null || st.Control.Surface != surface || Math.Abs(st.Control.Gain) < 0.9) continue;
                if (_hingeExcluded is not null && i < _hingeExcluded.Length && _hingeExcluded[i]) continue;   // no longer on this circuit (a disconnected aileron)
                double sc = st.Area * st.Chord, g = Math.Sign(st.Control.Gain);
                scRef += sc;
                HingeStrip(i, sc, g, axis, ref a, ref b);
            }
        }
        double vSpring = axis.CenteringSpringKt * 0.514444;
        return (a, b, 0.5 * 1.225 * vSpring * vSpring * scRef * Math.Abs(axis.HingeChDelta));
    }

    /// <summary>One hinged strip's share of the hinge-moment terms (see <see cref="HingeTerms"/>): flow term into
    /// <paramref name="a"/>, deflection stiffness into <paramref name="b"/>. <paramref name="g"/> = the sign of its gain.</summary>
    private void HingeStrip(int i, double sc, double g, ControlAxisConfig axis, ref double a, ref double b)
    {
        if (i >= _flowState.HingeQ.Length) return;
        double qsc = _flowState.HingeQ[i] * sc;
        // Attached flow (to 20°): the linear Chα. Separated (owner 2026-10-12, CFI/Pitts: in a developed spin the air from
        // below holds the free elevator nearly full aft and the rudder pro-spin): a surface hinged at its leading edge in
        // separated crossflow trails WITH the local flow, like a vane — its flow term grows to Chδ·α, so the float
        // goes to −α (against no spring: the stop). Blended from 20° to 40° of local flow.
        double al = _flowState.HingeAlphaRad[i], att = Math.Clamp(al, -0.35, 0.35);
        double sep = Math.Clamp((Math.Abs(al) - 0.35) / 0.35, 0.0, 1.0); sep = sep * sep * (3 - 2 * sep);
        a += g * qsc * ((1 - sep) * axis.HingeChAlpha * att + sep * axis.HingeChDelta * al);
        // Deflection stiffness follows the flow direction: centering in normal flow, gone in crossflow, and
        // REVERSED and ~2x stronger when the air comes from the trailing edge (tailslide: the hinge is now
        // downstream and the load acts near the leading free edge) — the free surface is pushed to its stop.
        double dir = i < _flowState.HingeFlowDir.Length ? _flowState.HingeFlowDir[i] : 1.0;
        b += qsc * axis.HingeChDelta * (dir >= 0 ? dir : 2.0 * dir);
    }

    // Trim-tab calibration: δ_tab − trim, per axis, so the free surface floats exactly at the spawn's trimmed deflection.
    private double _tabOffsetAil, _tabOffsetEle, _tabOffsetRud;
    private bool _tabCalibrated;
    private double TabOffset(string surface) => surface switch { "aileron" => _tabOffsetAil, "elevator" => _tabOffsetEle, _ => _tabOffsetRud };

    /// <summary>Set the tab offsets from the flow just evaluated: with the current trims, a free surface would hold the
    /// deflection it has now. Called once, on the first step after spawn (needs airflow; on the ground the offset stays 0).</summary>
    private void CalibrateTabs()
    {
        ControlDeflections trims = _spawnTrim;
        double Offset(string surface, ControlAxisConfig axis, double current, double trim)
        {
            (double a, double b, double k) = HingeTerms(surface, axis);
            if (axis.TrimType == "spring" && k > 1e-9)
            {
                // a + b·δ − k·(δ − neutral) = 0 at δ = current  →  neutral
                double neutral = current - (a + b * current) / k;
                return axis.ClampDefl(neutral - trim);
            }
            if (Math.Abs(b) < 1e-6) return 0.0;
            // a + b·(δ − tab) − k·δ = 0 at δ = current  →  tab
            double tab = current - (k * current - a) / b;
            return axis.ClampDefl(tab - trim);
        }
        _tabOffsetAil = Offset("aileron", Config.Controls.Aileron, trims.AileronRad, trims.AileronRad);
        _tabOffsetEle = Offset("elevator", Config.Controls.Elevator, trims.ElevatorRad, trims.ElevatorRad);
        _tabOffsetRud = Offset("rudder", Config.Controls.Rudder, trims.RudderRad, trims.RudderRad);
    }

    // Where the airframe samples a regional atmosphere besides its CG: the wing tips and the tail (body axes, from the
    // CG). Set on first use from the config.
    private double _halfSpanM = -1, _tailArmM;

    /// <summary>
    /// The air over the whole airframe from the regional field: density and wind at the CG, and the wind's gradient from
    /// samples at the left and right wing tips and the tail. The difference between the tips is what rolls (and,
    /// through the drag of each wing, yaws) an aircraft whose wings sit in different air — one wing in a thermal, one
    /// tip in the lift off a ridge, a gust front arriving from the side. Four samples a step, once per step (held through
    /// the RK4 stages like the uniform wind always was), a pure function of position and sim time: deterministic, and
    /// independent of the frame rate beyond that sample-and-hold.
    /// </summary>
    private void SampleRegionalAir(out double density, out Vec3 windBody, out Vec3 meanWindBody, out WindGradient gradient)
    {
        if (_halfSpanM < 0)
        {
            _halfSpanM = System.Math.Max(0.5, 0.5 * AeroModel.WingSpan(Config));
            double tail = 0; int n = 0;
            foreach (SurfaceConfig sf in Config.Surfaces)
                if (sf.Id.Contains("Stab", StringComparison.OrdinalIgnoreCase) || sf.Id.Contains("tail", StringComparison.OrdinalIgnoreCase))
                    foreach (StripConfig st in sf.Strips) { tail += st.PosVec().X; n++; }
            _tailArmM = n > 0 ? tail / n - Config.Mass.CgVec().X : 0.0;
        }
        Quat q = State.Attitude, qc = q.Conjugate();
        Vec3 cgPos = State.Position;
        AtmosphereSample cg = Atmosphere.SampleAt(cgPos)!.Value;
        density = cg.DensityKgM3;
        Vec3 wCg = Atmosphere.WindFromSample(cgPos, cg, true);
        windBody = qc.Rotate(wCg);
        meanWindBody = qc.Rotate(Atmosphere.WindFromSample(cgPos, cg, false));

        Vec3 Wind(Vec3 bodyOffset)
        {
            Vec3 p = cgPos + q.Rotate(bodyOffset);
            return qc.Rotate(Atmosphere.WindFromSample(p, Atmosphere.SampleAt(p)!.Value, true));
        }
        Vec3 left = Wind(new Vec3(0, -_halfSpanM, 0)), right = Wind(new Vec3(0, _halfSpanM, 0));
        Vec3 perY = (right - left) / (2 * _halfSpanM);
        Vec3 perX = Vec3.Zero;
        if (System.Math.Abs(_tailArmM) > 0.5) perX = (Wind(new Vec3(_tailArmM, 0, 0)) - windBody) / _tailArmM;
        gradient = perX.LengthSquared + perY.LengthSquared > 0 ? new WindGradient(perX, perY) : default;
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
        UpdateAileronLinkage(dt);

        ControlDeflections controls = new(_aileronRad, _elevatorRad, _rudderRad, _spoilerFraction, FlapFraction, SlatFraction);

        long tStep = SimProfile.Begin(), tAir = tStep;
        double altitudeM = -State.Position.Z;
        double airDensity;
        Vec3 windBody, meanWindBody;
        WindGradient windGradient = default;
        if (Atmosphere.Field is null)
        {
            airDensity = Atmosphere.DensityAtAltitude(altitudeM);
            // Turbulence wind is world-frame; the aero model works in body frame, so rotate it in.
            Vec3 windWorld = Atmosphere.WindAtPosition(State.Position);
            windBody = windWorld.LengthSquared > 1e-9 ? State.Attitude.Conjugate().Rotate(windWorld) : Vec3.Zero;
            Vec3 meanWindWorld = Atmosphere.MeanWindAtPosition(State.Position);
            meanWindBody = meanWindWorld.LengthSquared > 1e-9 ? State.Attitude.Conjugate().Rotate(meanWindWorld) : Vec3.Zero;
        }
        else SampleRegionalAir(out airDensity, out windBody, out meanWindBody, out windGradient);
        SimProfile.End(SimProfile.Scope.Air, tAir);
        double weightN = MassProperties.MassKg * Atmosphere.GravityMs2;

        // Stall hysteresis: the separated wake develops quickly (~0.25 s) but washes out slowly
        // (~1.0 s). Feeding the LAGGED fraction to the aero model stops the wake band flickering
        // on/off across the stall boundary — the relaxation cycle that made fast spins fall out.
        double instantFrac = AeroModel.InstantStalledFraction(Config, State.Velocity, State.Rates, windBody, _surfaceMask, _stripMask, _airfoilTables);
        double tau = instantFrac > _wakeStalledFrac ? Config.StallDynamics.WakeGrowTau : Config.StallDynamics.WakeDecayTau;
        _wakeStalledFrac += (instantFrac - _wakeStalledFrac) * (1.0 - Math.Exp(-dt / tau));

        // A loop, not LINQ's Sum: that boxes the list's enumerator at every step (QuestFrameBudgetProbe).
        int stripCount = 0;
        for (int i = 0; i < Config.Surfaces.Count; i++) stripCount += Config.Surfaces[i].Strips.Count;
        _flowState.EnsureSize(stripCount);

        // The engine's own state (mixture, governor, quit / caught again) before anything reads it.
        UpdateEngine(dt, airDensity);
        _drive = Drive;
        // Slipstream over the tail (momentum theory): V_slip = √(V² + 2T/(ρA)), contracted to ~0.8 R. From what the prop
        // is really doing, not the throttle: a stopped or missing prop blows nothing (Ty 2026-11-04, the Extra's strike).
        (double slipDu, double slipR, double slipSwirl) = TailSlipstream(Config, _throttle01, State.Velocity, State.Rates, airDensity, _drive, _noseLost);
        LastTailSlipstream = (slipDu, slipR, slipSwirl);

        // Ground contact, once a step instead of at each of RK4's four stages (Quest 3S, 2026-11-02: below 60 m the
        // contact queries took the headset from 72 fps to 20, and to 8 on the ground). If the airframe and its wheels are
        // clear of the ground by more than AirframeContact's 60 m plus the distance they can travel in the step, the
        // gear and the hard points are skipped on every stage; otherwise each stage is numbered so that points and
        // wheels found well clear on the first can skip the other three (AirframeContact.StageCullMargin).
        int stage = -1;
        double stepDt = dt;
        double stepTravel = 11.0 * (State.Velocity.Length + State.Rates.Length * 10.0) * dt, stepClearance = double.PositiveInfinity;
        bool groundClear = AirframeContact.StageCullEnabled && WorldTerrain.Active != null && _contacts.Count > 0
            && AirframeContact.ClearOfTheGround(_contacts, _wheelBodies, Config.Mass.CgVec(), State, stepTravel, out stepClearance);
        // Well inside 60 m of the ground (sitting on it, taxiing, landing): no stage can be clear, so the hard points
        // skip that test at each stage too.
        bool stagesNotClear = AirframeContact.StageCullEnabled && !groundClear && AirframeContact.StageCannotBeClear(stepClearance, stepTravel);

        (Vec3 Force, Vec3 Moment) ForceMoment(RigidBodyState s)
        {
            stage++;
            ForceDebug.Samples = CaptureForces ? new List<ForceSample>(160) : null;
            Vec3 waterF = Vec3.Zero;   // buoyancy + floodwater, kept out of the load factor
            // Ground effect: the wing's height over the ground (or the water) under the CG.
            long tGe = SimProfile.Begin();
            double under = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
            double? water = FloatHydro.WaterSurfaceAt(s.Position.X, s.Position.Y, -s.Position.Z);
            if (water.HasValue && water.Value > under) under = water.Value;
            double wingAgl = -s.Position.Z - under + AeroModel.WingHeightAboveCg(Config);
            SimProfile.End(SimProfile.Scope.GroundEffect, tGe);
            long tAero = SimProfile.Begin();
            (Vec3 aeroForce, Vec3 aeroMoment) = AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls, _wakeStalledFrac, _flowState, slipDu, slipR, _surfaceMask, _stripMask, meanWindBody, wingAgl, slipSwirl, windGradient);
            SimProfile.End(SimProfile.Scope.Aero, tAero);
            Vec3 gravityWorld = new(0, 0, weightN);
            Vec3 gravityBody = s.Attitude.Conjugate().Rotate(gravityWorld);
            Vec3 totalF = aeroForce + gravityBody;
            Vec3 totalM = aeroMoment + Vec3.Cross(CgShiftBody, gravityBody);   // weight at the remainder's own CG (ShedMass)
            if (Config.WeightShift is not null)
            {
                // A flex wing has nothing to deflect: the pilot swings his own mass on the hang strap and the whole
                // wing follows, so its control axes carry the swing ANGLE. Same call the trim solver makes.
                totalM += Config.WeightShift.SwingMoment(_elevatorRad, _aileronRad, aeroForce, MassProperties.MassKg);
            }
            if (ExternalForceWorld.LengthSquared > 1e-9)
            {
                Vec3 fBody = s.Attitude.Conjugate().Rotate(ExternalForceWorld);
                totalF += fBody;
                totalM += Vec3.Cross(ExternalForcePointBody - Config.Mass.CgVec(), fBody);
            }
            if (Config.Floats is not null)
            {
                double rudderCmdW = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
                long tFl = SimProfile.Begin();
                (Vec3 hF, Vec3 hM) = FloatHydro.Compute(Config, s, rudderCmdW, WaterRudderUp01);
                SimProfile.End(SimProfile.Scope.Floats, tFl);
                totalF += s.Attitude.Conjugate().Rotate(hF);
                totalM += s.Attitude.Conjugate().Rotate(hM);
                if (FloatFlood is not null && _hydroArmed)
                {
                    // Water that has got into a punctured compartment: weight at the compartment (the hull's own
                    // displacement is FloatHydro's, above).
                    (Vec3 fF, Vec3 fM) = FloatFlood.Forces(s, Hydro.WaterUp);
                    waterF += fF;
                    totalF += fF;
                    totalM += fM;
                }
            }
            if (_hydroArmed && Hydro.Active)
            {
                // The airframe's own volumes in the water (buoyancy, floodwater). Drag and added mass go on after the
                // RK4 step, implicitly (HydroBody.Correct) — they are far too stiff for it.
                (Vec3 wF, Vec3 wM) = Hydro.Forces(s);
                ForceDebug.Add(Config.Mass.CgVec(), wF, wM, "water");
                waterF += wF;
                totalF += wF;
                totalM += wM;
            }
            bool amphibUp = Config.Floats != null && GearExtension <= 0.9;   // amphibian, wheels up: the float keels still carry it
            if (Config.Gear.Count > 0 && (GearExtension > 0.9 || amphibUp) && groundClear)
            {
                LandingGear.AllInTheAir(Config, _gearTouching, _lostGear, amphibUp);
            }
            else if (Config.Gear.Count > 0 && (GearExtension > 0.9 || amphibUp))   // retracted gear carries nothing (belly contact does)
            {
                double rudderCmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
                // The 2-33's wheel brake is ON the dive-brake handle, at the end of its travel: full air
                // brake is also full brake, whatever the pilot's own brake input is doing (SpoilerConfig).
                double wheelBrake = Math.Max(BrakeInput, Config.Controls.Spoiler.BrakeFromHandle(_spoilerFraction));
                long tGear = SimProfile.Begin();
                (Vec3 gForceWorld, Vec3 gMomentWorld) = LandingGear.Compute(Config, s, rudderCmd, wheelBrake, 0.0, BrakeBias, _lostGear, _pendingBreaks, _gearTouching, _impacts, GearExtension > 0.9 ? Tailwheel : null, amphibUp, stage, stepDt, _gearStageClear, _wheels);
                SimProfile.End(SimProfile.Scope.Gear, tGear);
                totalF += s.Attitude.Conjugate().Rotate(gForceWorld);
                totalM += s.Attitude.Conjugate().Rotate(gMomentWorld);
            }
            if (Config.RetractableGear && GearExtension > 0.01 && Config.GearDragAreaM2 > 0)
            {
                // Extended gear: flat-plate drag area along the relative wind (wheels, legs, open doors).
                Vec3 vAir = s.Velocity - windBody;
                double vA = vAir.Length;
                if (vA > 1.0) totalF -= vAir * (0.5 * airDensity * vA * Config.GearDragAreaM2 * GearExtension);
            }
            if (ExtraDragAreaM2 > 0 || _cowlFlap01 > 0)
            {
                Vec3 vAir = s.Velocity - windBody;
                double vA = vAir.Length;
                if (vA > 1.0)
                {
                    // The open canopy's drag, and the cowl flap's (at the same point: both are close to the cockpit).
                    Vec3 xF = vAir * (-0.5 * airDensity * vA * (ExtraDragAreaM2 + _cowlFlap01 * CowlFlapDragAreaM2));
                    ForceDebug.Add(ExtraDragPointBody, xF, Vec3.Zero, "extra-drag");
                    totalF += xF;
                    totalM += Vec3.Cross(ExtraDragPointBody - Config.Mass.CgVec(), xF);
                }
            }
            if (Brs.Out && Config.Brs is not null)
            {
                // The whole-aircraft parachute. Applied at the bridle, which is above the CG — that offset is
                // what makes the aeroplane hang the right way up, and it is the only thing that does.
                (Vec3 bF, Vec3 bM) = Brs.Forces(Config, s, windBody, airDensity);
                ForceDebug.Add(Config.Brs.BridleVec(), bF, Vec3.Zero, "brs");
                totalF += bF;
                totalM += bM;
            }
            if (groundClear)
            {
                // Nothing within reach of the ground or the water this step: no height queries, but the hard points
                // still meet the buildings, the tower and the arch (WorldSolids) at every altitude.
                long tSo = SimProfile.Begin();
                (Vec3 sF, Vec3 sM) = AirframeContact.ComputeSolidsOnly(_contacts, Config.Mass.CgVec(), s, _pendingBreaks, _impacts, record: stage == 0);
                SimProfile.End(SimProfile.Scope.Contact, tSo);
                totalF += s.Attitude.Conjugate().Rotate(sF);
                totalM += s.Attitude.Conjugate().Rotate(sM);
            }
            else
            {
                // The rest of the airframe against the ground/solids (a flipped aircraft rests on fin and tips).
                long tCt = SimProfile.Begin();
                (Vec3 cF, Vec3 cM) = AirframeContact.Compute(_contacts, Config.Mass.CgVec(), s, _pendingBreaks, 0.0, _impacts,
                    Config.Floats is not null, waterByHydro: true, stage, stepDt, stagesNotClear);
                SimProfile.End(SimProfile.Scope.Contact, tCt);
                totalF += s.Attitude.Conjugate().Rotate(cF);
                totalM += s.Attitude.Conjugate().Rotate(cM);
            }
            if (Config.Propulsion is not null)
            {
                (Vec3 pF, Vec3 pM) = PropulsionForces(Config, _throttle01, s.Velocity, s.Rates, airDensity, _noseLost, _noseBody, _propThrustScale, _drive);
                totalF += pF;
                totalM += pM;
            }
            if (ForceDebug.Samples is not null)
            {
                ForceDebug.Add(Config.Mass.CgVec(), gravityBody, Vec3.Zero, "weight");
                if (ExternalForceWorld.LengthSquared > 1e-9) ForceDebug.Add(ExternalForcePointBody, s.Attitude.Conjugate().Rotate(ExternalForceWorld), Vec3.Zero, "rope");
                LastForces = ForceDebug.Samples;
                ForceDebug.Samples = null;
            }
            // Load factor from the non-gravitational resultant (RK4 evaluates this 4×; the last is ≈ end state).
            // Buoyancy and floodwater weight are spread over the whole airframe, not carried through the wing: they are
            // not load factor (a sunk glider is not pulling 20 g).
            Vec3 nonGrav = totalF - gravityBody - waterF;
            LoadFactorZ = weightN > 1e-9 ? -nonGrav.Z / weightN : 1.0;
            AeroLoadFactorZ = weightN > 1e-9 ? -aeroForce.Z / weightN : 1.0;
            if (MassProperties.MassKg > 1e-9) SpecificForceBody = (totalF - gravityBody) / MassProperties.MassKg;
            return (totalF, totalM);
        }

        PrepareWater(dt);
        if (_wheelsUnseeded) WheelSpin.SeedRolling(Config, State, _wheels);
        foreach (WheelState w in _wheels) w.ClearContact();
        RigidBodyState preStep = State;
        long tForces = SimProfile.Begin();
        State = RigidBody6DOF.IntegrateRK4(State, dt, MassProperties, ForceMoment);
        SimProfile.End(SimProfile.Scope.Forces, tForces);
        // A wheel the gear did not turn against the ground this step (in the air, on a ski, in the water, retracted)
        // runs down in its bearings.
        foreach (WheelState w in _wheels) { if (!w.Advanced) w.Coast(dt); w.Advanced = false; }
        if (Config.Floats is not null)
        {
            // The water the floats carry (FloatHydro.AddedMassOf): applied implicitly, like the airframe's below.
            FloatHydro.AddedMass am = FloatHydro.AddedMassOf(Config, State);
            RigidBodyState beforeAm = State;
            State = FloatHydro.CorrectForAddedMass(preStep, State, MassProperties, _floatAddedMass, am, dt);
            _floatAddedMass = am;
            CheckFloatWaterLoads(beforeAm, State, dt);
        }
        if (_hydroArmed)
        {
            State = Hydro.AfterStep(preStep, State, MassProperties, dt);
            RecordPanelWaterLoads(preStep, State, dt);   // the water on each wing panel's station (AircraftContactDamage)
            FloatFlood?.Flood(State, Hydro.WaterUp, dt);
            foreach (AirframeComponent c in Hydro.TakeBreaks())   // the water break-up rule (AirframeFlooding.CheckJoints)
                if (!_lost.Contains(c) && !_pendingBreaks.Contains(c)) _pendingBreaks.Add(c);
        }
        else RecordPanelWaterLoads(preStep, State, dt);   // clears last step's water moments once clear of the water
        long tAfter = SimProfile.Begin();
        Atmosphere.AdvanceTime(dt);
        UpdateStructure(dt);
        UpdateLoad(dt);
        Electrics.Step(dt, EngineRpm);
        StepTrees(preStep, dt);   // stems and crowns: snap, stop, or take the part that hit them (AircraftTreeStrike)
        if (_impacts.MaxClosingMs > 0.0) LastImpact = _impacts.Last;
        FilterFuselageBreaks();   // a fuselage section crushes before it breaks (AircraftFuselageCrush)
        foreach (AirframeComponent c in _pendingBreaks) LoseComponent(c);
        AfterStepContactDamage(dt);
        if (_impacts.MaxClosingMs > 0.0) HardImpact?.Invoke(_impacts.MaxClosingMs, _impacts.Point);
        Damage.Update(dt);
        if (Tailwheel != null && GearExtension > 0.9 && !_lostGear.Contains(AirframeComponent.GearTail))
        {
            double rudderCmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
            LandingGear.UpdateTailwheel(Config, State, Tailwheel, rudderCmd, dt);
        }
        if (HasWaterRudderRetract)
        {
            if (WaterRudderHeldUp01 is double held) WaterRudderUp01 = Math.Clamp(held, 0.0, 1.0);
            else
            {
                double target = WaterRudderHooked ? 1.0 : 0.0, step = dt / WaterRudderTravelSec;
                WaterRudderUp01 = WaterRudderUp01 < target ? Math.Min(target, WaterRudderUp01 + step) : Math.Max(target, WaterRudderUp01 - step);
            }
        }
        if (Config.RetractableGear)
        {
            double target = GearDown ? 1.0 : 0.0;
            double step = dt / GearTravelTime;
            GearExtension = GearExtension < target ? Math.Min(target, GearExtension + step) : Math.Max(target, GearExtension - step);
        }

        // Proposal 1: downwash transport lag (Cm-alphadot) — eps arrives at the tail one
        // transport time (tail-arm / V) late.
        if (Config.StallDynamics.DownwashLagEnabled)
        {
            double vTot = Math.Max(State.Velocity.Length, 6.0);
            double alphaNow = Math.Atan2(State.Velocity.Z, Math.Max(Math.Abs(State.Velocity.X), 0.5) * Math.Sign(State.Velocity.X == 0 ? 1 : State.Velocity.X));
            double target = 0.4 * Math.Clamp(alphaNow, -0.5, 0.5) * (1.0 - _wakeStalledFrac);
            double tauDw = 4.2 / vTot;
            _flowState.DownwashEpsLagged += (target - _flowState.DownwashEpsLagged) * (1.0 - Math.Exp(-dt / tauDw));
        }

        // Proposal 3: unsteady force lag (~3 chords / V per strip).
        if (Config.StallDynamics.UnsteadyLagEnabled)
        {
            double vTot = Math.Max(State.Velocity.Length, 6.0);
            if (!_flowState.LagPrimed)
            {
                Array.Copy(_flowState.InstCl, _flowState.LagCl, _flowState.InstCl.Length);
                Array.Copy(_flowState.InstCd, _flowState.LagCd, _flowState.InstCd.Length);
                Array.Copy(_flowState.InstCm, _flowState.LagCm, _flowState.InstCm.Length);
                _flowState.LagPrimed = true;
            }
            for (int i = 0; i < _flowState.LagCl.Length; i++)
            {
                double tauU = Math.Clamp(3.0 * Math.Max(_flowState.ChordM[i], 0.2) / vTot, 0.02, 0.3);
                double k = 1.0 - Math.Exp(-dt / tauU);
                _flowState.LagCl[i] += (_flowState.InstCl[i] - _flowState.LagCl[i]) * k;
                _flowState.LagCd[i] += (_flowState.InstCd[i] - _flowState.LagCd[i]) * k;
                _flowState.LagCm[i] += (_flowState.InstCm[i] - _flowState.LagCm[i]) * k;
            }
        }

        // Advance per-strip separation memory (two-branch stall hysteresis): a strip SEPARATES fast
        // above 16 deg local alpha, REATTACHES slowly below 11 deg, and holds in the band between —
        // so the inner wing stays committed to the separated branch while the outer wing flies the
        // attached one at the same |alpha| history. LocalAlphaRad was recorded during the last stage.
        // (Spin research 2026-10-13: in a DEVELOPED spin at α 30–60° every wing strip, up-going wing included, sits above
        // sepOn and never dips below sepOff, so both wings ride the separated branch and this memory does not act; nor can
        // it in a steady rotary balance, where each strip's α is fixed. Wing roll damping there is the static post-stall
        // normal-force slope plus the drag tilt of the rotated local wind — as in NASA rotary-balance data for GA wings,
        // where wing roll is propelling only from the stall to ~35° α. Spin rate is then set by the pitch balance (α) and
        // the fuselage/tail yaw; final calibration awaits the owner's measured spin numbers.)
        // These two thresholds are a calibration against the UPRIGHT stall. A cambered strip reaches its
        // INVERTED stall sooner (the Clark Y table: -13.0 deg against +16.5 deg), so for a strip at negative
        // alpha the same calibrated band is measured from its own stall instead: shifted by the section's
        // asymmetry, which is zero for a symmetric section and leaves the aerobatic airplanes bit-identical.
        const double sepOn = 16.0 * Math.PI / 180.0, sepOff = 11.0 * Math.PI / 180.0;
        for (int i = 0; i < stripCount; i++)
        {
            double aSigned = _flowState.LocalAlphaRad[i];
            double a = Math.Abs(aSigned);
            double shift = 0.0;
            if (aSigned < 0.0 && i < _flowState.StallNegRad.Length && _flowState.StallPosRad[i] > 0.0)
            {
                shift = -_flowState.StallNegRad[i] - _flowState.StallPosRad[i];   // <= 0 when cambered
            }
            double on = Math.Max(sepOn + shift, 2.0 * Math.PI / 180.0);
            double off = Math.Max(sepOff + shift, 1.0 * Math.PI / 180.0);
            double s0 = _flowState.Separation[i];
            double target = a > on ? 1.0 : a < off ? 0.0 : s0;
            double tauSec = target > s0 ? Config.StallDynamics.StripSepTau : Config.StallDynamics.StripReattachTau;
            _flowState.Separation[i] = s0 + (target - s0) * (1.0 - Math.Exp(-dt / tauSec));
        }
        _wheelsUnseeded = false;   // the State writes above are the step's own
        SimProfile.End(SimProfile.Scope.AfterStep, tAfter);
        SimProfile.End(SimProfile.Scope.Step, tStep);
    }

    /// <summary>
    /// The propeller's wash over the tail (momentum theory): V_slip = √(V² + 2T/(ρA)), ~90 % developed by the tail
    /// (≈2 R aft) in a tube contracted to ~0.7 R, carrying a swirl of ~<see cref="SwirlFactor"/> × the axial increase,
    /// clockwise from behind for a right-hand prop. One prop's worth, on the centreline, from the type's Propulsion.
    /// Static so RigSolver evaluates exactly what the sim flies.
    /// </summary>
    public static (double DeltaU, double Radius, double Swirl) TailSlipstream(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity)
        => TailSlipstream(config, throttle01, velocity, rates, airDensity, PropModel.PropDrive.Running, propGone: false);

    /// <summary>The same from what the propeller is really doing (<paramref name="drive"/>). A running engine: exactly the
    /// throttle law above (the multi-engine types' failed engines, ThrottleScale 0, no longer count). Gone (prop strike,
    /// nose lost): nothing. Dead and windmilling: the prop takes energy OUT of the air, so the tail sits in a slightly SLOWER
    /// wake (V_slip² = V² + 2T/(ρA) with the windmill's negative T) swirling the other way — small, and never a blast.</summary>
    public static (double DeltaU, double Radius, double Swirl) TailSlipstream(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity,
        PropModel.PropDrive drive, bool propGone)
    {
        if (config.Propulsion is null || config.Propulsion.PropDiameterM <= 0.0 || propGone) return (0.0, 0.0, 0.0);
        double vNow = velocity.Length;
        double thr = throttle01;
        if (config.Engines.Count > 0)
        {
            double scale = 0.0; int n = 0;
            foreach (EngineMount m in config.Engines) { if (!m.Feathered) scale += m.ThrottleScale; n++; }
            thr *= n > 0 ? scale / n : 1.0;
        }
        (Vec3 tF, _) = PropModel.Compute(config.Propulsion, thr, velocity, rates, airDensity, drive);
        double radius = config.Propulsion.PropDiameterM * 0.5;
        double disc = System.Math.PI * radius * radius;
        if (!drive.EngineRunning)
        {
            double t = System.Math.Min(0.0, tF.X);
            double v2 = System.Math.Max(0.0, vNow * vNow + 2.0 * t / (airDensity * disc));
            double deficit = 0.9 * (System.Math.Sqrt(v2) - vNow);   // <= 0
            if (deficit == 0.0) return (0.0, 0.0, 0.0);
            return (deficit, 0.7 * radius, SwirlFactor * deficit * config.Propulsion.RotationSign);
        }
        double thrust = System.Math.Max(0.0, tF.X);
        double vSlip = System.Math.Sqrt(vNow * vNow + 2.0 * thrust / (airDensity * disc));
        double du = 0.9 * System.Math.Max(0.0, vSlip - vNow);
        return (du, 0.7 * radius, SwirlFactor * du * config.Propulsion.RotationSign);
    }

    /// <summary>
    /// Every engine's force and moment about the CG. Single engine: Propulsion as-is at the nose. Multi-engine: each
    /// mount runs the prop model with its own rotation sign and throttle, thrust applied AT the mount (r×F gives the
    /// asymmetric yaw/roll when one is failed); prop torque/P-factor/slipstream/gyro of counter-rotating pairs cancel
    /// in symmetric flight and survive in engine-out. Static so RigSolver evaluates exactly what the sim flies.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) PropulsionForces(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity,
        bool noseLost = false, Vec3 noseBody = default, double[]? thrustScale = null)
        => PropulsionForces(config, throttle01, velocity, rates, airDensity, noseLost, noseBody, thrustScale, PropModel.PropDrive.Running);

    /// <summary>The same with the engine's real state: a dead engine makes no power whatever the throttle says (torque,
    /// P-factor, spiral slipstream and the gyro at the windmill's rpm, not the lever's).</summary>
    public static (Vec3 Force, Vec3 Moment) PropulsionForces(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity,
        bool noseLost, Vec3 noseBody, double[]? thrustScale, PropModel.PropDrive drive)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        if (config.Propulsion is null) return (totalF, totalM);
        if (config.Engines.Count == 0 && !noseLost)   // a prop torn off with the nose makes neither thrust nor drag
        {
            (Vec3 pF, Vec3 pM) = PropModel.Compute(config.Propulsion, throttle01, velocity, rates, airDensity, drive);
            // Blade tips curled by a prop strike (Aircraft.PropStrike): a smaller disc at the same power.
            if (thrustScale is { Length: > 0 } && thrustScale[0] != 1.0) { pF *= thrustScale[0]; pM *= thrustScale[0]; }
            ForceDebug.Add(noseBody, pF, pM, "thrust");
            if (ForceDebug.Samples != null) ForceDebug.Add(noseBody, Vec3.Zero, PropModel.GyroMoment(config.Propulsion, throttle01, rates, drive), "gyro");   // (already in "thrust"'s moment: a breakdown only)
            return (pF, pM);
        }
        var basePropCfg = config.Propulsion;
        for (int ei = 0; ei < config.Engines.Count; ei++)
        {
            EngineMount m = config.Engines[ei];
            if (m.Feathered) continue;   // edge-on blades: no thrust, no windmill drag
            var engCfg = new PropulsionConfig
            {
                MaxPowerW = basePropCfg.MaxPowerW, PropDiameterM = basePropCfg.PropDiameterM,
                IdleRpm = basePropCfg.IdleRpm, MaxRpm = basePropCfg.MaxRpm,
                PropInertia = basePropCfg.PropInertia, RotationSign = m.RotationSign,
                Efficiency = basePropCfg.Efficiency, ThrustLineZ = basePropCfg.ThrustLineZ,
                PFactorK = basePropCfg.PFactorK, SlipstreamK = basePropCfg.SlipstreamK,
                IdleDragCd = basePropCfg.IdleDragCd, DesignSpeedMs = basePropCfg.DesignSpeedMs
            };
            (Vec3 eF, Vec3 eM) = PropModel.Compute(engCfg, throttle01 * m.ThrottleScale, velocity, rates, airDensity, drive);
            if (thrustScale is not null && ei < thrustScale.Length && thrustScale[ei] != 1.0) { eF *= thrustScale[ei]; eM *= thrustScale[ei]; }
            ForceDebug.Add(m.PosVec() + new Vec3(0.8, 0, 0), eF, eM, "thrust");
            if (ForceDebug.Samples != null) ForceDebug.Add(m.PosVec() + new Vec3(0.8, 0, 0), Vec3.Zero, PropModel.GyroMoment(engCfg, throttle01 * m.ThrottleScale, rates, drive), "gyro");
            totalF += eF;
            totalM += eM + Vec3.Cross(m.PosVec() - config.Mass.CgVec(), eF);
        }
        return (totalF, totalM);
    }

    /// <summary>Inverse of the stick shaping: the raw stick fraction (-1..1) that produces `deflRad`
    /// through this axis's dead zone/expo/travel (bisection; exact enough to hold a trim hands-off).</summary>
    public static double StickForDeflection(double deflRad, ControlAxisConfig axis)
    {
        if (axis.MaxDeflRad <= 1e-9 || Math.Abs(deflRad) < 1e-9) return 0.0;
        double sign = Math.Sign(deflRad), target = Math.Min(Math.Abs(deflRad), axis.LimitFor(sign));
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Math.Abs(ShapeAxis(sign * mid, axis)) < target) lo = mid; else hi = mid;
        }
        return sign * 0.5 * (lo + hi);
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
        return Math.Sign(clamped) * shaped * axis.LimitFor(Math.Sign(clamped));
    }

    private static double SlewTo(double current, double target, double ratePerSec, double dt)
    {
        double maxStep = Math.Abs(ratePerSec) * dt;
        double delta = target - current;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }
}

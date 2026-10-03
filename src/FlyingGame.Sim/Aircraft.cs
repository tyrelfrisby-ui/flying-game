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
/// <summary>
/// Airframe structural state — REAL g limits (owner request 2026-09-08). Limit loads come from
/// AircraftConfig.Limits (GMax/GMin, the FAR 23/25 limit load factors); ultimate = limit ×
/// Limits.UltimateFactor (1.5). Between limit and ultimate the structure yields and groans
/// (<see cref="OverLimitSeverity"/> 0..1); past ultimate for a short continuous dwell the WINGS SEPARATE
/// (<see cref="WingsFailed"/>) — the sim then flies the fuselage, tail, gear and engine without them.
/// Per-instance state only: a fresh Aircraft (ResetFlight) is intact.
/// </summary>
public sealed class StructuralState
{
    /// <summary>Continuous time past ultimate needed before the wings let go — long enough to reject a
    /// single 5 ms RK4 spike or a gust tick, short enough that a real overstress snaps them.</summary>
    public const double FailureDwellSec = 0.05;

    public double LimitPosG { get; }
    public double LimitNegG { get; }
    public double UltimatePosG { get; }
    public double UltimateNegG { get; }

    /// <summary>0 under the limit load; ramps to 1 at the ultimate load (by sign of n). Drives the groan.</summary>
    public double OverLimitSeverity { get; internal set; }

    /// <summary>Continuous time (s) the load has been beyond ultimate; resets when it comes back inside.</summary>
    public double OverUltimateDwellSec { get; internal set; }

    /// <summary>True once the wings have separated (latched until a fresh Aircraft is built).</summary>
    public bool WingsFailed { get; internal set; }

    /// <summary>Sim time (Atmosphere.SimTimeSec) of the failure, for bridges that poll.</summary>
    public double FailureTimeSec { get; internal set; }

    /// <summary>Raised ONCE, on the step the wings separate.</summary>
    public event Action? OnWingsFailed;

    internal StructuralState(LimitsConfig limits)
    {
        double factor = limits.UltimateFactor > 1.0 ? limits.UltimateFactor : 1.5;
        LimitPosG = limits.GMax > 0.0 ? limits.GMax : 10.0;
        LimitNegG = limits.GMin < 0.0 ? limits.GMin : -10.0;
        UltimatePosG = LimitPosG * factor;
        UltimateNegG = LimitNegG * factor;
    }

    /// <summary>Severity of load factor n against the limit/ultimate band on its own sign: 0 inside the
    /// limit envelope, 1 at (or beyond) ultimate.</summary>
    public double SeverityFor(double n)
    {
        if (n > LimitPosG)
        {
            return Math.Clamp((n - LimitPosG) / (UltimatePosG - LimitPosG), 0.0, 1.0);
        }

        if (n < LimitNegG)
        {
            return Math.Clamp((LimitNegG - n) / (LimitNegG - UltimateNegG), 0.0, 1.0);
        }

        return 0.0;
    }

    internal void RaiseFailed() => OnWingsFailed?.Invoke();
}

public sealed class Aircraft
{
    public AircraftConfig Config { get; }
    public MassProperties MassProperties { get; private set; }
    public RigidBodyState State { get; set; }

    /// <summary>Structural g-limit state (limit/ultimate loads, groan severity, wing failure).</summary>
    public StructuralState Structure { get; }

    /// <summary>Per-surface aero mask (indexed like Config.Surfaces): null while intact; after wing failure
    /// the wing surfaces are false. Config.Surfaces itself is never mutated — other systems index it.</summary>
    private readonly List<AirframeComponent> _pendingBreaks = new();
    private readonly HashSet<AirframeComponent> _lostGear = new();   // legs torn off (LandingGear skips them)
    private bool[] _gearTouching = System.Array.Empty<bool>();       // per-leg contact state, Config.Gear order
    private readonly ImpactRecorder _impacts = new();
    /// <summary>Castoring tailwheel yaw (LandingGear.TailwheelState); null for types without a steerable tailwheel.</summary>
    public LandingGear.TailwheelState? Tailwheel { get; }
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
    public void SetGear(bool down, bool immediate = false)
    {
        if (!Config.RetractableGear) { GearDown = true; GearExtension = 1.0; return; }
        GearDown = down;
        if (immediate) GearExtension = down ? 1.0 : 0.0;
    }
    public void SetEngineThrottleScale(int idx, double scale) { if (idx>=0 && idx<Config.Engines.Count) Config.Engines[idx].ThrottleScale = System.Math.Clamp(scale,0,1); }
    public double SlatFraction { get; set; }   // 0..1 (auto or manual)
    public double BrakeInput { get; set; }     // 0..1 wheel braking (on ground), both sides
    /// <summary>Differential braking bias -1 (left only) .. +1 (right only): rudder pedal toe-brake feel.
    /// Left/right brake = BrakeInput × (1 ∓ bias) clamped to 0..1.</summary>
    public double BrakeBias { get; set; }
    private readonly StripFlowState _flowState = new(); // per-strip two-branch stall memory

    /// <summary>External world-frame force (N) applied at ExternalForcePointBody — the aerotow rope
    /// tension, winch, etc. Set by the coupling each step; zero by default.</summary>
    public Vec3 ExternalForceWorld;
    public Vec3 ExternalForcePointBody;

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
        _contacts = AirframeContact.BuildPoints(config);
        _gearTouching = new bool[config.Gear.Count];
        Damage = new FlyingGame.Core.Combat.DamageState(LoseComponent, IsLost);
        foreach (GearConfig g in config.Gear) if (g.IsTailwheel && g.IsSteerable) { Tailwheel = new LandingGear.TailwheelState(); break; }
        Guns = FlyingGame.Core.Combat.Armament.For(config);
        foreach (ContactPoint cp in _contacts) if (cp.Name == "nose") _noseBody = cp.Body;
        Structure = new StructuralState(config.Limits);

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
    }

    // ---- telemetry shared by audio / structure / HUD / net (read-only) ----------------------------

    /// <summary>Commanded power 0..1 (0 for gliders).</summary>
    public double Throttle01 => _throttle01;

    /// <summary>
    /// Body-z load factor in g from the NON-gravitational forces (aero + thrust + gear + hydro + rope)
    /// at the last force evaluation of the step: +1 in level flight, +4 in a 4 g pull, negative
    /// pushed over. Sign: body z is DOWN, so lift (−z) gives a positive n.
    /// </summary>
    public double LoadFactorZ { get; private set; } = 1.0;

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
            double idle = p.IdleRpm, max = p.MaxRpm, thr = _throttle01;
            if (p.ConstantSpeed)
            {
                double gov = Math.Clamp(p.GovernedRpm, idle, max);
                double finePitch = idle + (Math.Min(gov, 1800.0) - idle) * Math.Min(1.0, thr / 0.3);
                if (thr < 0.3) return finePitch;
                double t = Math.Clamp((thr - 0.9) / 0.1, 0.0, 1.0);
                return gov + (max - gov) * t * t * (3 - 2 * t);
            }
            double vRef = Config.SpawnIasMs > 1 ? Config.SpawnIasMs : 30.0;
            double speedFactor = 0.82 + 0.18 * Math.Clamp(State.Velocity.Length / vRef, 0.0, 1.0);
            return idle + (max - idle) * Math.Pow(thr, 0.8) * speedFactor;
        }
    }

    public static Dictionary<string, AirfoilTable> BuildAirfoilTables(AircraftConfig config)
    {
        var tables = new Dictionary<string, AirfoilTable>();
        foreach (KeyValuePair<string, AirfoilTableData> kv in config.AirfoilTables)
        {
            tables[kv.Key] = new AirfoilTable(kv.Value);
        }

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
    /// with it. Mass and inertia are deliberately left alone (owner: no dynamic/inertial bookkeeping for
    /// the lost piece). A lost nose stops the propeller. Idempotent.
    /// </summary>
    public void LoseComponent(AirframeComponent comp)
    {
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
        ComponentLost?.Invoke(comp);
    }
    private bool _noseLost;
    private Vec3 _noseBody = new(1.5, 0, 0);
    /// <summary>True once the propeller has hit the ground (or the nose is gone): engine stopped, no thrust.</summary>
    public bool EngineStopped => _noseLost;
    /// <summary>Strip-level aero mask (AeroModel strip order), null while every component is attached.</summary>
    public bool[]? StripMask => _stripMask;

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

        Structure.WingsFailed = true;
        Structure.OverLimitSeverity = 0.0;
        Structure.OverUltimateDwellSec = 0.0;
        Structure.FailureTimeSec = Atmosphere.SimTimeSec;
        Structure.RaiseFailed();
    }

    /// <summary>Per-step structural bookkeeping from the load factor of the step just integrated.</summary>
    private void UpdateStructure(double dt)
    {
        if (Structure.WingsFailed)
        {
            Structure.OverLimitSeverity = 0.0; // nothing left to groan
            return;
        }

        double n = LoadFactorZ;
        Structure.OverLimitSeverity = Structure.SeverityFor(n);
        bool beyondUltimate = n > Structure.UltimatePosG || n < Structure.UltimateNegG;
        Structure.OverUltimateDwellSec = beyondUltimate ? Structure.OverUltimateDwellSec + dt : 0.0;
        if (Structure.OverUltimateDwellSec >= StructuralState.FailureDwellSec)
        {
            FailWings();
        }
    }

    /// <summary>Advances the aircraft by one fixed timestep: shapes stick input into deflection targets (dead zone/expo/max travel), slews actuators toward them, then integrates the rigid body via RK4.</summary>
    public void Step(ControlInputs inputs, double dt)
    {
        _pendingBreaks.Clear();
        _impacts.Reset();
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
            return Math.Sign(side) * axis.MaxDeflRad;
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
        return Math.Clamp(d, -axis.MaxDeflRad, axis.MaxDeflRad);
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
                double sc = st.Area * st.Chord, g = Math.Sign(st.Control.Gain);
                scRef += sc;
                if (i >= _flowState.HingeQ.Length) continue;
                double qsc = _flowState.HingeQ[i] * sc;
                a += g * qsc * axis.HingeChAlpha * _flowState.HingeAlphaRad[i];
                // Deflection stiffness follows the flow direction: centering in normal flow, gone in crossflow, and
                // REVERSED and ~2x stronger when the air comes from the trailing edge (tailslide: the hinge is now
                // downstream and the load acts near the leading free edge) — the free surface is pushed to its stop.
                double dir = i < _flowState.HingeFlowDir.Length ? _flowState.HingeFlowDir[i] : 1.0;
                b += qsc * axis.HingeChDelta * (dir >= 0 ? dir : 2.0 * dir);
            }
        }
        double vSpring = axis.CenteringSpringKt * 0.514444;
        return (a, b, 0.5 * 1.225 * vSpring * vSpring * scRef * Math.Abs(axis.HingeChDelta));
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
                return Math.Clamp(neutral - trim, -axis.MaxDeflRad, axis.MaxDeflRad);
            }
            if (Math.Abs(b) < 1e-6) return 0.0;
            // a + b·(δ − tab) − k·δ = 0 at δ = current  →  tab
            double tab = current - (k * current - a) / b;
            return Math.Clamp(tab - trim, -axis.MaxDeflRad, axis.MaxDeflRad);
        }
        _tabOffsetAil = Offset("aileron", Config.Controls.Aileron, trims.AileronRad, trims.AileronRad);
        _tabOffsetEle = Offset("elevator", Config.Controls.Elevator, trims.ElevatorRad, trims.ElevatorRad);
        _tabOffsetRud = Offset("rudder", Config.Controls.Rudder, trims.RudderRad, trims.RudderRad);
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

        ControlDeflections controls = new(_aileronRad, _elevatorRad, _rudderRad, _spoilerFraction, FlapFraction, SlatFraction);

        double altitudeM = -State.Position.Z;
        double airDensity = Atmosphere.DensityAtAltitude(altitudeM);
        // Turbulence wind is world-frame; the aero model works in body frame, so rotate it in.
        Vec3 windWorld = Atmosphere.WindAtPosition(State.Position);
        Vec3 windBody = windWorld.LengthSquared > 1e-9 ? State.Attitude.Conjugate().Rotate(windWorld) : Vec3.Zero;
        Vec3 meanWindWorld = Atmosphere.MeanWindAtPosition(State.Position);
        Vec3 meanWindBody = meanWindWorld.LengthSquared > 1e-9 ? State.Attitude.Conjugate().Rotate(meanWindWorld) : Vec3.Zero;
        double weightN = MassProperties.MassKg * Atmosphere.GravityMs2;

        // Stall hysteresis: the separated wake develops quickly (~0.25 s) but washes out slowly
        // (~1.0 s). Feeding the LAGGED fraction to the aero model stops the wake band flickering
        // on/off across the stall boundary — the relaxation cycle that made fast spins fall out.
        double instantFrac = AeroModel.InstantStalledFraction(Config, State.Velocity, State.Rates, windBody, _surfaceMask, _stripMask);
        double tau = instantFrac > _wakeStalledFrac ? Config.StallDynamics.WakeGrowTau : Config.StallDynamics.WakeDecayTau;
        _wakeStalledFrac += (instantFrac - _wakeStalledFrac) * (1.0 - Math.Exp(-dt / tau));

        int stripCount = Config.Surfaces.Sum(s => s.Strips.Count);
        _flowState.EnsureSize(stripCount);

        // Slipstream over the tail (momentum theory): V_slip = √(V² + 2T/(ρA)), contracted to ~0.8 R.
        double slipDu = 0.0, slipR = 0.0;
        if (Config.Propulsion is not null && Config.Propulsion.PropDiameterM > 0.0)
        {
            double vNow = State.Velocity.Length;
            (Vec3 tF, _) = PropModel.Compute(Config.Propulsion, _throttle01, State.Velocity, State.Rates, airDensity);
            double thrust = System.Math.Max(0.0, tF.X);
            double radius = Config.Propulsion.PropDiameterM * 0.5;
            double disc = System.Math.PI * radius * radius;
            double vSlip = System.Math.Sqrt(vNow * vNow + 2.0 * thrust / (airDensity * disc));
            // ~90 % developed by the tail (≈2 R aft), in a tube contracted to ~0.7 R.
            slipDu = 0.9 * System.Math.Max(0.0, vSlip - vNow);
            slipR = 0.7 * radius;
        }

        (Vec3 Force, Vec3 Moment) ForceMoment(RigidBodyState s)
        {
            ForceDebug.Samples = CaptureForces ? new List<ForceSample>(160) : null;
            (Vec3 aeroForce, Vec3 aeroMoment) = AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls, _wakeStalledFrac, _flowState, slipDu, slipR, _surfaceMask, _stripMask, meanWindBody);
            Vec3 gravityWorld = new(0, 0, weightN);
            Vec3 gravityBody = s.Attitude.Conjugate().Rotate(gravityWorld);
            Vec3 totalF = aeroForce + gravityBody;
            Vec3 totalM = aeroMoment;
            if (ExternalForceWorld.LengthSquared > 1e-9)
            {
                Vec3 fBody = s.Attitude.Conjugate().Rotate(ExternalForceWorld);
                totalF += fBody;
                totalM += Vec3.Cross(ExternalForcePointBody - Config.Mass.CgVec(), fBody);
            }
            if (Config.Floats is not null)
            {
                double rudderCmdW = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
                (Vec3 hF, Vec3 hM) = FloatHydro.Compute(Config, s, rudderCmdW);
                totalF += s.Attitude.Conjugate().Rotate(hF);
                totalM += s.Attitude.Conjugate().Rotate(hM);
            }
            if (Config.Gear.Count > 0 && GearExtension > 0.9)   // retracted gear carries nothing (belly contact does)
            {
                double rudderCmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
                (Vec3 gForceWorld, Vec3 gMomentWorld) = LandingGear.Compute(Config, s, rudderCmd, BrakeInput, 0.0, BrakeBias, _lostGear, _pendingBreaks, _gearTouching, _impacts, Tailwheel);
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
            {
                // The rest of the airframe against the ground/solids (a flipped aircraft rests on fin and tips).
                (Vec3 cF, Vec3 cM) = AirframeContact.Compute(_contacts, Config.Mass.CgVec(), s, _pendingBreaks, 0.0, _impacts);
                totalF += s.Attitude.Conjugate().Rotate(cF);
                totalM += s.Attitude.Conjugate().Rotate(cM);
            }
            if (Config.Propulsion is not null)
            {
                if (Config.Engines.Count == 0)
                {
                    (Vec3 pF, Vec3 pM) = PropModel.Compute(Config.Propulsion, _noseLost ? 0.0 : _throttle01, s.Velocity, s.Rates, airDensity);
                    ForceDebug.Add(_noseBody, pF, pM, "thrust");
                    totalF += pF;
                    totalM += pM;
                }
                else
                {
                    // Multi-engine: each mount runs the prop model with its own rotation sign and
                    // throttle, thrust applied AT the mount (r×F gives the asymmetric yaw/roll when
                    // one is failed). Prop torque/P-factor/slipstream/gyro of counter-rotating pairs
                    // cancel in symmetric flight and survive in engine-out.
                    var basePropCfg = Config.Propulsion;
                    foreach (EngineMount m in Config.Engines)
                    {
                        var engCfg = new PropulsionConfig
                        {
                            MaxPowerW = basePropCfg.MaxPowerW, PropDiameterM = basePropCfg.PropDiameterM,
                            IdleRpm = basePropCfg.IdleRpm, MaxRpm = basePropCfg.MaxRpm,
                            PropInertia = basePropCfg.PropInertia, RotationSign = m.RotationSign,
                            Efficiency = basePropCfg.Efficiency, ThrustLineZ = basePropCfg.ThrustLineZ,
                            PFactorK = basePropCfg.PFactorK, SlipstreamK = basePropCfg.SlipstreamK
                        };
                        (Vec3 eF, Vec3 eM) = PropModel.Compute(engCfg, _throttle01 * m.ThrottleScale, s.Velocity, s.Rates, airDensity);
                        ForceDebug.Add(m.PosVec() + new Vec3(0.8, 0, 0), eF, eM, "thrust");
                        totalF += eF;
                        totalM += eM + Vec3.Cross(m.PosVec() - Config.Mass.CgVec(), eF);
                    }
                }
            }
            if (ForceDebug.Samples is not null)
            {
                ForceDebug.Add(Config.Mass.CgVec(), gravityBody, Vec3.Zero, "weight");
                if (ExternalForceWorld.LengthSquared > 1e-9) ForceDebug.Add(ExternalForcePointBody, s.Attitude.Conjugate().Rotate(ExternalForceWorld), Vec3.Zero, "rope");
                LastForces = ForceDebug.Samples;
                ForceDebug.Samples = null;
            }
            // Load factor from the non-gravitational resultant (RK4 evaluates this 4×; the last is ≈ end state).
            Vec3 nonGrav = totalF - gravityBody;
            LoadFactorZ = weightN > 1e-9 ? -nonGrav.Z / weightN : 1.0;
            return (totalF, totalM);
        }

        State = RigidBody6DOF.IntegrateRK4(State, dt, MassProperties, ForceMoment);
        Atmosphere.AdvanceTime(dt);
        UpdateStructure(dt);
        foreach (AirframeComponent c in _pendingBreaks) LoseComponent(c);
        if (_impacts.MaxClosingMs > 0.0) HardImpact?.Invoke(_impacts.MaxClosingMs, _impacts.Point);
        Damage.Update(dt);
        if (Tailwheel != null && GearExtension > 0.9 && !_lostGear.Contains(AirframeComponent.GearTail))
        {
            double rudderCmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
            LandingGear.UpdateTailwheel(Config, State, Tailwheel, rudderCmd, dt);
        }
        if (Config.RetractableGear)
        {
            double target = GearDown ? 1.0 : 0.0;
            double step = dt / GearTravelSec;
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

    /// <summary>Inverse of the stick shaping: the raw stick fraction (-1..1) that produces `deflRad`
    /// through this axis's dead zone/expo/travel (bisection; exact enough to hold a trim hands-off).</summary>
    public static double StickForDeflection(double deflRad, ControlAxisConfig axis)
    {
        if (axis.MaxDeflRad <= 1e-9 || Math.Abs(deflRad) < 1e-9) return 0.0;
        double sign = Math.Sign(deflRad), target = Math.Min(Math.Abs(deflRad), axis.MaxDeflRad);
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (ShapeAxis(mid, axis) < target) lo = mid; else hi = mid;
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
        return Math.Sign(clamped) * shaped * axis.MaxDeflRad;
    }

    private static double SlewTo(double current, double target, double ratePerSec, double dt)
    {
        double maxStep = Math.Abs(ratePerSec) * dt;
        double delta = target - current;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }
}

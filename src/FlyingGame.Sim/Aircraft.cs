using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Aero.Wake;
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
/// What broke on a structural step (<see cref="StructuralState.Step(double, double, double, double)"/>).
///
/// The wings have been STAGGERED since 2026-11-10 (<see cref="WingFailureLaw"/>), so what used to be one bit is a bit
/// per side and per stage. <see cref="Wings"/> still means "the wings are gone", which now happens when the SECOND side
/// departs — both on the same step for any symmetric pull, which is how it always read.
/// </summary>
[Flags]
public enum StructuralBreak
{
    None = 0, Wings = 1, HorizontalTail = 2, EngineMount = 4,
    /// <summary>This side's whole wing has left the aeroplane.</summary>
    WingLeftGone = 8, WingRightGone = 16,
    /// <summary>Strut-braced: the sections outboard of this side's station have gone; the braced inner bay flies on.</summary>
    OutboardLeftGone = 32, OutboardRightGone = 64,
    /// <summary>Cantilever: this side has folded up and is still attached, still making force.</summary>
    FoldedLeft = 128, FoldedRight = 256,
    /// <summary>The fin has failed under lateral load (Ty 2026-11-11's rudder-reversal limit).</summary>
    VerticalTail = 512,
}

/// <summary>
/// Airframe structural state — REAL g limits (owner request 2026-09-08). Limit loads come from
/// AircraftConfig.Limits (GMax/GMin, the FAR 23/25 limit load factors). Inside the LIMIT the structure is rated and
/// silent; past it the primary structure follows Ty's OVERSTRESS PATTERN (2026-11-05, <see cref="OverstressMember"/>):
/// it flexes visibly from the limit, 10° at 1.5 × limit and 20° at 2.0 × limit, keeps half of any bending past 1.5 ×
/// limit as a permanent set, is weakened 2 % per 0.1 of overage for the next time, and snaps and departs past
/// 2.0 × limit of what is left of it. Three members, each on its own load:
///  • the WINGS on the load factor the air makes, weight-adjusted (n / n_limit × W / W_gross: a light aeroplane has more
///    g before the same bending; an over-gross one less);
///  • the HORIZONTAL TAIL on the same load divided by <see cref="TailDesignMargin"/>, because what it carries in a
///    symmetric pull is the balancing download and not the wings' lift (Ty 2026-11-09): a pull that bends or breaks the
///    wings does not reach the tail's own limit;
///  • the ENGINE MOUNT on the same load factor with NO weight adjustment — the engine's mass is fixed, so its mount
///    feels n × its own weight whatever the rest weighs.
/// Past 1.5 × limit it also creaks and pops (<see cref="OverLimitSeverity"/> 0..1, <see cref="Pops"/>), from the
/// member nearest its break. <see cref="GroanPosG"/>/<see cref="UltimatePosG"/> are the published 1.5 × and 2.0 × limit
/// (Limits.GroanFactor, Limits.UltimateFactor) at the spawn mass, for display and grading.
/// Per-instance state only: a fresh Aircraft (ResetFlight) is intact.
/// </summary>
public sealed partial class StructuralState
{
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

    /// <summary>The overstress members (Ty 2026-11-05). <see cref="HorizontalTail"/> is null without a horizontal tail,
    /// <see cref="EngineMount"/> without a propeller engine on a mount.</summary>
    public OverstressMember? HorizontalTail { get; }
    public OverstressMember? EngineMount { get; }

    /// <summary>
    /// THE VERTICAL TAIL, on its own LATERAL load (Ty 2026-11-11) — the one member here that is not driven by a load
    /// factor at all, because the fin is not loaded by one. Its load is the side force its own strips make, in
    /// newtons, against <see cref="FinLimitLoadN"/>.
    ///
    /// Null when the type publishes no fin limit (<see cref="LimitsConfig.FinLimitLoadN"/> 0), which is the same
    /// discipline the <see cref="Wings"/> property states: a member nothing could drive, or that is driven against a
    /// guessed limit, is a shadow that quietly disagrees with the members that are real.
    ///
    /// WHAT IS NOT DONE: its <see cref="OverstressMember.DeflectionRad"/> is not DRAWN. The wings' and the tail's
    /// overstress shapes become geometry the aero then flies (AircraftOverstress.ApplyOverstressShape); a bent fin
    /// is felt and heard but not seen, so a fin pulled past its limit and back looks straight while carrying a
    /// permanent set. The set is real and the model flies it; only the mesh is missing.
    /// </summary>
    public OverstressMember? VerticalTail { get; }

    /// <summary>The fin's limit load, N (<see cref="LimitsConfig.FinLimitLoadN"/>); 0 = no fin member.</summary>
    public double FinLimitLoadN { get; }

    /// <summary>Each wing as its own failing structure, on its own load (Ty 2026-11-10,
    /// <see cref="WingFailureLaw"/>): in a rolling pull the two do not carry the same thing, so one can come apart
    /// while the other does not.</summary>
    public WingSideStructure LeftWing { get; } = new(-1);
    public WingSideStructure RightWing { get; } = new(1);
    /// <summary>The wing on side <paramref name="side"/> (−1 left, +1 right).</summary>
    public WingSideStructure Wing(int side) => side < 0 ? LeftWing : RightWing;

    /// <summary>
    /// THE MORE LOADED WING's overstress member — what "the wings" means now that each one is its own
    /// (<see cref="LeftWing"/>, <see cref="RightWing"/>). Under a symmetric pull the two are identical, so every reading
    /// that predates the staggered failure is unchanged; in a rolling pull this is the wing that is closer to coming
    /// apart, which is what a single number about "the wings" is for (the groan, the HUD, the grading).
    ///
    /// Not a member of its own: there is nothing left for a third one to be loaded by, and a shadow member that nothing
    /// drove would quietly disagree with the two that do.
    /// </summary>
    public OverstressMember Wings =>
        RightWing.Member.EffectiveRatio > LeftWing.Member.EffectiveRatio ? RightWing.Member : LeftWing.Member;

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

    /// <summary>
    /// How much more the horizontal tail can carry than the balancing download a pull at the wings' LIMIT puts on it
    /// (Ty 2026-11-09, on the overstressed Extra 300: "the tail should not bend and if it did it would be the other way
    /// due to the direction of the load").
    ///
    /// The tail used to be stepped at the WINGS' load ratio — the two lines were identical — so it passed 1.5 × and 2.0 ×
    /// on the same step they did and kept the same 10° set whenever they did (measured, Extra 300 held at 1.8 × its 10 g
    /// limit: wings 16.00°, tail 16.00°). That is not a load the tail carries. In a symmetric pull the tail's load is the
    /// BALANCING download that trims the wing's nose-down pitching moment — a few per cent of the wing's lift — while the
    /// structure under it is sized by a different and much larger case: 14 CFR 23.423's abrupt elevator manoeuvre and
    /// 23.425's gust, not 23.421's balancing load. So the balancing download at the wings' limit is a fraction of what
    /// the tail can carry, and a manoeuvre that bends the WINGS does not reach the tail's limit.
    ///
    /// 2.5 is that fraction's reciprocal, set against Ty's rule rather than measured per type: the pull that takes the
    /// wings off (2.0 × their limit) leaves the tail at 0.80 × its own — inside its limit, so it does not flex at all,
    /// which is Ty's position. Nothing in the sim yet puts the tail's OWN load on it (there is no per-surface normal
    /// force, and the abrupt-elevator case is not modelled), so this is the only thing that loads it; when a real tail
    /// load arrives, it replaces this ratio rather than scaling it.
    /// </summary>
    public const double TailDesignMargin = 2.5;

    /// <summary>Advance at load factor <paramref name="n"/> for everything, at the spawn mass (tests, scenarios). True on
    /// the step the wings break (both sides gone).</summary>
    public bool Step(double n, double dt) => (Step(n, n, _spawnWeightRatio, dt) & StructuralBreak.Wings) != 0;

    /// <summary>Advance the structure one step: the wings on the air's load factor <paramref name="nAero"/> times
    /// <paramref name="wingWeightRatio"/> (<see cref="LimitsConfig.WingWeightRatio"/>), the tail on the same load through
    /// its own margin (<see cref="TailDesignMargin"/>: it carries the balancing download, not the wings' lift), the engine
    /// mount on the whole aeroplane's <paramref name="nTotal"/>. Returns what broke on this step; the caller does the
    /// separating, because dropping surfaces and re-deriving the inertia is the Aircraft's business.</summary>
    public StructuralBreak Step(double nAero, double nTotal, double wingWeightRatio, double dt) =>
        Step(nAero, nAero, nTotal, wingWeightRatio, dt);

    /// <summary>
    /// The same, with the two wings on their OWN load factors (Ty 2026-11-10: "account for aileron load so one tip could
    /// break off but not the other"). <paramref name="nLeft"/> and <paramref name="nRight"/> are what each wing is
    /// carrying (Aircraft.WingSideLoadFactor: the symmetric part ± half the rolling moment over the side's lift arm);
    /// they are equal for every symmetric pull, which is every caller that predates the stagger. The TAIL is stepped on
    /// the symmetric part alone — a rolling pull does not load one side of the stabiliser more in anything this sim
    /// models, and its own load is a different case entirely (<see cref="TailDesignMargin"/>).
    /// </summary>
    /// <summary>
    /// THE LOAD RATIO A WING MEMBER ACTUALLY SEES at <paramref name="n"/> g — <see cref="Ratio"/> against the
    /// PUBLISHED limit, scaled by the wing's weight share, exactly as <see cref="StepWings"/> computes it. Flutter
    /// needs this because Ty's endpoint is "an ultimate strength of 1g", and the strength that makes a member break
    /// at 1 g can only be worked out from the ratio that member is really driven by: the published limit is not
    /// <see cref="LimitPosG"/> and the weight share is not 1 on every loading. One source of truth rather than a
    /// second copy of the arithmetic that could drift.
    /// </summary>
    public double WingRatioAt(double n, double wingWeightRatio) => Ratio(n) * wingWeightRatio;

    /// <summary>The same for the horizontal tail, which carries the wing's ratio over its own design margin.</summary>
    public double TailRatioAt(double n, double wingWeightRatio) => Ratio(n) * wingWeightRatio / TailDesignMargin;

    /// <param name="finSideForceN">The lateral force the vertical tail's own strips are making, N (Aircraft's
    /// <c>FinSideLoadN</c>). 0 for a held-load-factor scenario, which is right: a symmetric pull puts nothing on the
    /// fin.</param>
    public StructuralBreak Step(double nLeft, double nRight, double nTotal, double wingWeightRatio, double dt, double finSideForceN = 0.0)
    {
        StructuralBreak broke = StepWings(nLeft, nRight, wingWeightRatio, dt);
        double rw = Ratio(0.5 * (nLeft + nRight)) * wingWeightRatio, re = Ratio(nTotal);
        if (HorizontalTail is { Broken: false } t && t.Step(rw / TailDesignMargin, dt)) broke |= StructuralBreak.HorizontalTail;
        if (EngineMount is { Broken: false } e && e.Step(re, dt)) broke |= StructuralBreak.EngineMount;
        // The fin on its own LATERAL load — a force over a force, not a load factor over a load factor.
        if (VerticalTail is { Broken: false } f && f.Step(FinRatioAt(finSideForceN), dt)) broke |= StructuralBreak.VerticalTail;
        double sev = 0.0;
        foreach (WingSideStructure w in Wings2)
            if (!w.Gone && w.Member is { Broken: false }) sev = Math.Max(sev, Severity(w.Member));
        if (HorizontalTail is { Broken: false } t2) sev = Math.Max(sev, Severity(t2));
        if (EngineMount is { Broken: false } e2) sev = Math.Max(sev, Severity(e2));
        if (VerticalTail is { Broken: false } f2) sev = Math.Max(sev, Severity(f2));
        OverLimitSeverity = sev;
        AccumulatePops(sev, dt);
        return broke;
    }

    /// <summary>Load factor as a multiple of the published limit on its own side, signed.</summary>
    /// <remarks>
    /// THE ENGINE MOUNT SHARES THE WING'S NEGATIVE LIMIT HERE AND THE CERTIFICATION STANDARD SAYS IT SHOULD NOT.
    /// <see cref="StructuralState.Step(double, double, double, double, double)"/> steps the mount on Ratio(nTotal), so
    /// on a push it divides by <c>_publishedNeg</c> -- the J-3's -1.5 g. Sourced 2026-11-10, and -1.5 is traceable: it
    /// is CAR 4a Table 4a-1's Condition V (inverted flight, 4a.84) MINIMUM limit load factor, printed page 53 of CAM 4a
    /// "Airplane Airworthiness". Section 4a.84(d) then says, verbatim: "Only the rear (or single) lift truss system of
    /// externally braced wing structures need be investigated for this condition." So by the standard's own words it is
    /// a wing lift-truss number and has nothing to do with a welded steel-tube mount.
    /// <para>NOT CHANGED, because no primary document publishes a mount figure to change it TO. FAA TCDS A-691 Rev 34
    /// (8 Mar 2011) was read in full: it states no load factors at all, only "Certification Basis   Type Certificate
    /// No. 691 (CAR 4a)" (page 10). The commonly cited +3.5/-1.5 for the J-3 is not in it. The two Piper mount
    /// documents, SB-21 "Engine Mounting Bolt Failures" (1946) and SB-36 "Inspection of Motor Mount" (1941), are crack
    /// and bolt inspections with no load data.</para>
    /// <para>WHAT CAR 4a DESIGNS A MOUNT TO, if this is ever given its own limit (CAM 4a page 16, "Special Loading
    /// Conditions"): 4a.189 and 4a.188 -- Condition I POSITIVE loads with engine torque, and no negative flight case
    /// appears anywhere; 4a.190 -- side load, one third of Condition I, floor 1.33 g; 4a.191 -- the only opposite-sign
    /// case, "the limit load in each member shall be arbitrarily assumed as 50 percent of that in the level landing
    /// condition but of opposite sign", i.e. a LANDING number, not an inverted-flight one; 4a.187 -- torque stresses
    /// times 2 for a four-cylinder engine such as the A-65. All with an ultimate factor of safety of 1.5. For a
    /// 1100-1220 lb J-3 that puts 4a.191 near 1.9-2.0 g limit by Figure 4a-10 -- but that is DERIVED arithmetic resting
    /// on a wing area A-691 does not publish, so it is written here as a lead and deliberately not installed as a fact.
    /// Ty's call: it needs a number he can defend, and the honest state today is that CAR 4a specifies a derivation.</para>
    /// </remarks>
    private double Ratio(double n) => n >= 0 ? n / _publishedPos : -n / _publishedNeg;

    private static double Severity(OverstressMember m) =>
        Math.Clamp((m.EffectiveRatio - OverstressMember.UltimateRatio) / (OverstressMember.BreakRatio - OverstressMember.UltimateRatio), 0.0, 1.0);

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

    /// <summary>True once the wings have separated (latched until a fresh Aircraft is built).</summary>
    public bool WingsFailed { get; internal set; }

    /// <summary>Sim time (Atmosphere.SimTimeSec) of the failure, for bridges that poll.</summary>
    public double FailureTimeSec { get; internal set; }

    /// <summary>Raised ONCE, on the step the wings separate.</summary>
    public event Action? OnWingsFailed;

    private readonly LimitsConfig _limits;
    private readonly double _massKg, _publishedPos, _publishedNeg, _spawnWeightRatio;

    /// <summary>The structure of an aeroplane of this total mass. The published thresholds are the active category's limits
    /// at or below VA with the airbrakes in, scaled down when the non-lifting parts outweigh what the wings were
    /// certificated to carry (<see cref="LimitsConfig.NonLiftingFactor"/>). The overstress members measure the load against
    /// the category's own limits and weigh it themselves (<see cref="LimitsConfig.WingWeightRatio"/>).</summary>
    internal StructuralState(LimitsConfig limits, double massKg = 0.0, double grossKg = 0.0, bool horizontalTail = false, bool engineMount = false)
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
        (_publishedPos, _publishedNeg) = limits.PublishedLimits();
        _spawnWeightRatio = massKg > 0 ? limits.WingWeightRatio(massKg, grossKg > 0 ? grossKg : massKg) : 1.0;
        if (horizontalTail) HorizontalTail = new OverstressMember("horizontal tail");
        if (engineMount) EngineMount = new OverstressMember("engine mount");
        FinLimitLoadN = limits.FinLimitLoadN > 0.0 ? limits.FinLimitLoadN : 0.0;
        if (FinLimitLoadN > 0.0) VerticalTail = new OverstressMember("vertical tail");
    }

    /// <summary>The fin's load as a multiple of its limit, signed (+ = the force pushes the tail to the RIGHT), from
    /// the lateral force its strips are making. One source of truth, so the probe, the member and any table agree.</summary>
    public double FinRatioAt(double sideForceN) => FinLimitLoadN > 0.0 ? sideForceN / FinLimitLoadN : 0.0;

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
    /// overstress pattern stays on the published limits, because bending strength does not depend on speed.</summary>
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

    /// <summary>What touched the surface in the last physics step (SurfaceContacts: the tracks' events), refilled in place
    /// every step. <see cref="SurfaceContactsStepped"/> is raised with it after every step, so a reader stepping several
    /// physics steps a frame sees each one.</summary>
    public SurfaceContactBuffer SurfaceContacts { get; private set; } = new(1);
    public event Action<SurfaceContactBuffer>? SurfaceContactsStepped;

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
    private double _slipOverTail = 1.0; // share of the prop's wash that reaches the tail this step (SlipstreamOverTail)
    /// <summary>Share of the propeller's wash that reaches the tail this step (1 in attached flow; <see cref="SlipstreamOverTail"/>).</summary>
    public double SlipOverTail => _slipOverTail;
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

    /// <summary>This aeroplane's slipstream swirl distribution, refilled in place every step (no per-frame allocation).</summary>
    private readonly BladeElement.SwirlProfile _swirl = new();

    /// <summary>
    /// WHEEL-SKIS (Ty 2026-11-05): the cockpit's WHEELS / SKIS selector and the hinged skis it drives, on an aeroplane with
    /// the option fitted (<see cref="AircraftConfig.Skis"/>); null on one without. The gear physics reads where the skis
    /// ARE (<see cref="SkiSelector.Lowered01"/>), every input moves the lever (<see cref="SkiSelector.Select"/>), and the
    /// hydraulics run them there over <see cref="SkiGear.TransitSec"/>. It starts with the skis down over snow and up
    /// over anything else (<see cref="SkiSelector.StartFor"/>).
    /// </summary>
    public SkiState? Skis { get; }

    public void SetGear(bool down, bool immediate = false)
    {
        if (!Config.RetractableGear) { GearDown = true; GearExtension = 1.0; return; }
        GearDown = down;
        if (immediate) GearExtension = down ? 1.0 : 0.0;
    }
    /// <summary>
    /// The water-rudder retract (Ty 2026-11-02, t278u; floatplanes whose config draws a retractable blade). The blade
    /// is spring-loaded DOWN: with the cockpit handle let go of it lies on the floor and the rudder is down, which is
    /// how an aeroplane spawned ON THE WATER starts. One spawned in the air starts with the handle on the hook and the
    /// blade up, because a floatplane cannot be airborne without the pilot having raised it (Ty 2026-11-11; the
    /// constructor reads the floats' own added mass to tell wet from dry). Hanging the handle on its hook raises it.
    /// 0 = down .. 1 = up (on the hook).
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
    /// <summary>Scratch flow state for <see cref="ForcesAtCurrentState"/>, never time-advanced: it keeps the
    /// overlay's own aero evaluation out of the live stall memory and the unsteady lag.</summary>
    private StripFlowState? _overlayFlow;

    /// <summary>External world-frame force (N) applied at ExternalForcePointBody — the aerotow rope
    /// tension, winch, etc. Set by the coupling each step; zero by default.</summary>
    public Vec3 ExternalForceWorld;
    public Vec3 ExternalForcePointBody;

    /// <summary>New mass and inertia (live weight &amp; balance — the Aero Widget's loading command). The CG itself is
    /// Config.Mass.Cg, which every force and moment is taken about.</summary>
    public void SetMassProperties(double massKg, double ixx, double iyy, double izz, double ixz) =>
        MassProperties = Shed(Fit(new MassProperties(massKg, ixx, iyy, izz, ixz)));

    /// <summary>Mass that has LEFT the aeroplane in flight (a departed canopy, Core Vr.CanopyDynamics; a component broken
    /// off with its volumes, <see cref="LoseComponent"/>), and the
    /// first moment of it about the reference CG, kg·m (body frame). Zero unless something has gone.</summary>
    public double ShedMassKg { get; private set; }
    private Vec3 _shedMoment;

    /// <summary>The CG of what is left, body frame: Config.Mass.Cg moved by −m·r / (M − m). Forces and moments are still
    /// taken about Config.Mass.Cg (every geometry in the config is placed relative to it); the shift enters as the
    /// remaining weight's moment about it (see the force sum), which is exactly what moving the CG does to the
    /// statics. The dynamics' coupling term m·r×a is neglected (a 14 kg canopy 0.5 m from the CG of an 800 kg aeroplane).</summary>
    public Vec3 CgShiftBody => MassProperties.MassKg > 1.0 ? (_fittedMoment - _shedMoment) * (1.0 / MassProperties.MassKg) : Vec3.Zero;

    /// <summary>Equipment the session bolts on that the authored mass does not include — the wheel-skis
    /// (<see cref="SkiGear.TotalMassKg"/>) — and its first moment about the reference CG, kg·m. Added exactly the way
    /// <see cref="ShedMass"/> takes a part away: the mass, the parallel-axis inertia, and the CG moved toward it.</summary>
    public double FittedMassKg { get; private set; }
    private Vec3 _fittedMoment;

    private double _fitIxx, _fitIyy, _fitIzz, _fitIxz;   // its parallel-axis inertia about the reference CG, each part at its own place
    private readonly double _skiDragAreaM2;

    private MassProperties Fit(MassProperties p) => FittedMassKg <= 0.0 ? p
        : new MassProperties(p.MassKg + FittedMassKg, p.Ixx + _fitIxx, p.Iyy + _fitIyy, p.Izz + _fitIzz, p.Ixz + _fitIxz);

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

    /// <summary>Takes <paramref name="kg"/> at offset <paramref name="d"/> out of <paramref name="p"/> (a negative mass puts it in).</summary>
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
        State = initialState;
        if (SkiGear.AnyFitted(config))
        {
            WorldTerrain.Surface under = WorldTerrain.SurfaceOverride
                ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(initialState.Position.X, initialState.Position.Y, -initialState.Position.Z) : WorldTerrain.Surface.Paved);
            Skis = SkiState.For(config, SkiSelector.StartFor(under));
            FittedMassKg = SkiGear.TotalMassKg(config);
            _fittedMoment = SkiGear.MassMomentKgM(config);
            _skiDragAreaM2 = SkiGear.TotalDragAreaM2(config);
            Vec3 cg0 = config.Mass.CgVec();
            foreach (SkiGear.Geometry? geo in Skis!.Legs)
            {
                if (geo == null) continue;
                Vec3 d = geo.PinAt(SkiGear.LoweredLinkRad) - cg0;
                _fitIxx += geo.MassKg * (d.Y * d.Y + d.Z * d.Z); _fitIyy += geo.MassKg * (d.X * d.X + d.Z * d.Z);
                _fitIzz += geo.MassKg * (d.X * d.X + d.Y * d.Y); _fitIxz += geo.MassKg * d.X * d.Z;
            }
        }
        MassProperties = Fit(Load.Properties);
        _airfoilTables = BuildAirfoilTables(config);
        _contacts = AirframeContact.BuildPoints(config);
        InitContactDamage();
        _gearTouching = new bool[config.Gear.Count];
        _gearStageClear = new double[2 * config.Gear.Count];
        _wheels = WheelState.For(config);
        SurfaceContacts = new SurfaceContactBuffer(config);
        BuildImprints();
        _wheelBodies = config.Gear.Select(g => g.PosVec()).ToArray();
        Damage = new FlyingGame.Core.Combat.DamageState(LoseComponent, IsLost);
        foreach (GearConfig g in config.Gear) if (g.IsTailwheel && g.IsSteerable) { Tailwheel = new LandingGear.TailwheelState(); break; }
        Guns = FlyingGame.Core.Combat.Armament.For(config);
        _noseBody = NoseIn(_contacts);   // the points this constructor already built; NoseBody(config) is the same answer
        Structure = new StructuralState(config.Limits, config.Mass.MassKg, StructuralGrossKg(config), HasHorizontalTail(config), HasEngineMount(config));
        // Which failure ladder each wing is on, and what would hold a folded one up (WingFailureLaw). InitContactDamage
        // has already built the panels, so their authored Braced flag and construction are to hand.
        InitWingFailure();
        if (config.Floats is not null) FloatFlood = new FloatFlooding(config);
        InitFloatWaterLoads();
        // The water the floats already carry at the start (an aeroplane spawned afloat): without this the first step
        // took all of it as newly entrained — a slam out of nothing, 420 kN on a Beaver starting with its heels wet.
        if (config.Floats is not null) _floatAddedMass = FloatHydro.AddedMassOf(config, initialState);
        // THE WATER RUDDER STARTS WHERE THE SPAWN PUTS IT (Ty 2026-11-11: "Selecting floats shows the airplane in the
        // air currently and in the air water rudders are up not down so make sure that is how they are shown"). The
        // blade is spring-loaded DOWN, so until this line every spawn built it down — and the landing page's preview
        // spawns the floatplane AIRBORNE (LandingFlow's default viewpoint is "In the air", 2,000 ft over the field),
        // so the configuration picture and the card thumbnail cut from it drew a Cub flying along with its water
        // rudders hanging in the breeze. So did every in-air start in the game proper: In the air, On final to the
        // lake, In a thermal, and the race / STOL / combat starts. An aeroplane that is already flying has had its
        // handle hung on the hook — it cannot have got airborne otherwise — so the spawn state follows the water,
        // the same way the skis read the surface they are spawned on twenty lines above (SkiSelector.StartFor).
        //
        // Wet comes off the added mass just computed rather than a second world query: AddedMassOf sums
        // ρ·π·c²/2 over the stations the waterline cuts, so its heave term is > 0 if and only if some float is in the
        // water at this state. No water under the floats (over land, or a world with no terrain loaded) reads dry,
        // which is also right for an amphibian spawned on a runway — water rudders up, clear of the tarmac.
        //
        // After the spawn the handle is the pilot's: taking off from the lake with the blade still down leaves it
        // down, because that is what the real aeroplane does to a pilot who forgets it (the GEAR slot raises it,
        // TouchFlightControls; CockpitWaterRudder is the hand in VR).
        if (HasWaterRudderRetract) SetWaterRudderHooked(_floatAddedMass.Linear.Z <= 0, immediate: true);

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
    /// <summary>While a replay is on screen, the rpm it was flown at (<see cref="Replay.ReplayFrame.EngineRpm"/>); null
    /// when flying for real. The replay poses the aeroplane but does not run its engine, and <see cref="EngineStopped"/>
    /// latches on a lost nose, so without this a replayed prop stands still for the whole flight — including the part
    /// before the crash that stopped it (Ty 2026-11-07). FlightReplay sets it each frame and clears it on exit.</summary>
    public double? ReplayEngineRpm { get; set; }
    /// <summary>Set by a replay: whether the engine was MAKING POWER at the frame being shown, from the recording.
    /// Null while flying. <see cref="AircraftEngine.EngineRunning"/> reads it, so the engine sound, the shaft power and
    /// the replayed thrust all follow the flight as flown instead of the live engine's state (Ty 2026-11-08: "no engine
    /// sound on replay" — the aeroplane had crashed, so the LIVE engine was stopped for the whole playback).</summary>
    public bool? ReplayEngineRunning { get; set; }

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
    /// <summary>The water's own velocity under the aeroplane (world NED, m/s; <see cref="WaterCurrent"/>): zero on a lake,
    /// the river's on a river. Over the ground the aeroplane moves at State's velocity, through the water at that minus
    /// this, through the air at that minus the wind — three different speeds on a river with a breeze.</summary>
    public Vec3 WaterVelocityWorld => WaterCurrent.SurfaceAt(State.Position.X, State.Position.Y);

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
        if (!_massOwnedByDamage) MassProperties = Shed(Fit(Load.Properties));
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
    /// The ROLLING moment the air is making, N·m about body x (+ = right wing down), at the last force evaluation of the
    /// step. It is the whole of what tells the two wings apart: an aero roll moment IS the difference between what the
    /// two of them are lifting, so with the symmetric part known it gives each wing its own load
    /// (<see cref="WingSideLoadFactor"/>, Ty 2026-11-10's "account for aileron load"). Everything the air does shows up
    /// in it — the ailerons, roll damping, the dihedral effect in a slip, and a wing that has shed its tips or folded —
    /// so none of those has to be modelled a second time to load a wing asymmetrically.
    /// </summary>
    public double AeroRollMomentNm { get; private set; }

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
    public double EngineRpm => ReplayEngineRpm
        ?? (_propStriking.Length > 0 && _propStriking[0] ? Math.Min(FreeEngineRpm, StrikeRotorRpm(0)) : FreeEngineRpm);

    /// <summary>The rpm the engine runs at with nothing holding its propeller (<see cref="EngineRpm"/> is lower while a
    /// strike is braking the blades). A prop whose blades a strike has shortened absorbs less torque and runs faster
    /// (<see cref="PropBladeDamage.Performance"/>).</summary>
    public double FreeEngineRpm => EngineRunning && _propDamaged.Length > 0 && _propDamaged[0] ? BaseEngineRpm * PropPerformance(0).rpmScale : BaseEngineRpm;

    /// <summary>The rpm with an undamaged propeller.</summary>
    private double BaseEngineRpm
    {
        get
        {
            PropulsionConfig p = Config.Propulsion!;
            if (p is null || _noseLost) return 0.0;   // engine stopped (prop strike)
            // THE GAUGE READS THE SHAFT. When this type's blade is fitted the rpm is integrated against the torque
            // balance (AircraftEngine.StepShaft), so the tachometer, the audio and the forces are finally the same
            // number — they were two different throttle laws before, and neither was what the propeller was doing.
            if (ShaftRpm > 0.0) return ShaftRpm;
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
    /// <summary>
    /// Does this type carry TWO aileron surfaces, upper and lower? A biplane with ailerons on both wings does (the
    /// Pitts); a biplane with them on one wing does not (the Great Lakes, lower only), and nor does a monoplane.
    /// Derived from the config's own surface ids so a type nobody wrote a rule for still comes out right.
    /// </summary>
    private bool AileronPair()
    {
        bool upper = false, lower = false;
        foreach (SurfaceConfig sf in Config.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            if (!id.Contains("aileron")) continue;
            if (id.Contains("lower")) lower = true; else upper = true;
        }
        return upper && lower;
    }
    private bool? _aileronPair;
    private bool _hasAileronPair => _aileronPair ??= AileronPair();

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
            // WHICH aileron surface this is, so a pair can be told from a stack. A biplane carries two aileron
            // SURFACES (the Pitts' wing-upper-aileron and wing-lower-aileron) and the component used to address only
            // a SIDE, so losing "the left aileron" took both of them. The test is the surface's own id against the
            // wing it hangs on, which is how every other surface here is identified — no table of types.
            bool lowerAileron = aileron && id.Contains("lower");
            bool upperAileron = aileron && !lowerAileron;
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
                    // A type with ONE aileron surface has it addressed by AileronLeft/Right whichever wing it is on
                    // (the Great Lakes' only ailerons are on its LOWER wing), so "upper" here means "the one that is
                    // not the lower of a pair" rather than a claim about which wing it is bolted to.
                    AirframeComponent.AileronLeft => (upperAileron || !_hasAileronPair) && aileron && y < 0,
                    AirframeComponent.AileronRight => (upperAileron || !_hasAileronPair) && aileron && y > 0,
                    AirframeComponent.AileronLowerLeft => lowerAileron && _hasAileronPair && y < 0,
                    AirframeComponent.AileronLowerRight => lowerAileron && _hasAileronPair && y > 0,
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
        if (comp == AirframeComponent.WingLeftInner) { LoseComponent(AirframeComponent.WingLeftOuter); LoseComponent(AirframeComponent.AileronLeft); if (_hasAileronPair) LoseComponent(AirframeComponent.AileronLowerLeft); }
        if (comp == AirframeComponent.WingRightInner) { LoseComponent(AirframeComponent.WingRightOuter); LoseComponent(AirframeComponent.AileronRight); if (_hasAileronPair) LoseComponent(AirframeComponent.AileronLowerRight); }
        // A WING PANEL TAKES ITS OWN AILERONS, and on a type with two surfaces that is both of them: the strip mask
        // cannot tell an upper panel from a lower one (WingPanels works in |y| across the whole wing), so a lost
        // outer panel is a lost outer panel on every wing the type has. Keeping the lower one attached while its
        // wing went would be worse than taking both.
        if (comp == AirframeComponent.WingLeftOuter) { LoseComponent(AirframeComponent.AileronLeft); if (_hasAileronPair) LoseComponent(AirframeComponent.AileronLowerLeft); }
        if (comp == AirframeComponent.WingRightOuter) { LoseComponent(AirframeComponent.AileronRight); if (_hasAileronPair) LoseComponent(AirframeComponent.AileronLowerRight); }
        if (comp == AirframeComponent.WingLeft) { _lost.Add(AirframeComponent.WingLeftInner); _lost.Add(AirframeComponent.WingLeftOuter); _lost.Add(AirframeComponent.AileronLeft); _lost.Add(AirframeComponent.AileronLowerLeft); _contacts.RemoveAll(p => p.Component is AirframeComponent.WingLeftInner or AirframeComponent.WingLeftOuter); }
        if (comp == AirframeComponent.WingRight) { _lost.Add(AirframeComponent.WingRightInner); _lost.Add(AirframeComponent.WingRightOuter); _lost.Add(AirframeComponent.AileronRight); _lost.Add(AirframeComponent.AileronLowerRight); _contacts.RemoveAll(p => p.Component is AirframeComponent.WingRightInner or AirframeComponent.WingRightOuter); }
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
            // AND THE PROPELLER'S WEIGHT GOES WITH IT, from where it was (Ty 2026-11-09: a lost prop must "eliminate
            // the weight of the prop at the proper location/arm"). The thrust and the windmilling drag were already
            // gone with _noseLost; what was missing is that this is the furthest-forward mass on the aeroplane, so
            // losing it throws the CG BACKWARDS — which is most of why a thrown propeller is frightening rather than
            // merely quiet. ShedMass drops the mass, takes the parallel-axis share out of the inertia and moves the
            // CG, exactly as it does for a canopy or a wing panel.
            double propKg = AirframeContact.PropMassKg(Config);
            if (propKg > 0)
                foreach (Vec3 hub in AirframeContact.PropHubsBody(Config)) ShedMass(propKg, hub);
        }
        if (comp == AirframeComponent.NacelleLeft || comp == AirframeComponent.NacelleRight)
        {
            for (int i = 0; i < Config.Engines.Count; i++)
                if ((Config.Engines[i].Pos[1] < 0) == (comp == AirframeComponent.NacelleLeft)) SetEngineThrottleScale(i, 0.0);
        }
        // Wing panels outboard of their structural station, and what their ailerons and slave struts do (contact damage).
        OnComponentLostStructure(comp);
        // WHAT IS LEFT IS A DIFFERENT WING (Ty 2026-11-10, having torn the outer panels off a J-3: "the glide ratio was
        // inexplicably good it should be bad with short stubby wing roots I assume it is counting the missing wing in
        // the aero"). The LIFT of a departed strip does go — the strip mask drops it from the sum — but the induced
        // drag of what remains is set by the trailing-vortex system's ASPECT RATIO, and that was measured once per
        // config from the strips as authored. So the stub went on flying the whole wing's aspect ratio and paying two
        // thirds of the induced drag it owed, which is most of the drag at best glide. Remeasured here, where the mask
        // changes, rather than per step: it loops the strips, and it only moves when something breaks off.
        RemeasureAspectRatios();
        // The volumes it takes with it (CrashRelay turns them into a wreck piece), and their mass with them.
        ShedMass(Hydro.Detach(comp));
        ComponentLost?.Invoke(comp);
    }

    /// <summary>Re-measure every surface's trailing-vortex system aspect ratio over the strips this aeroplane STILL
    /// HAS, and hand it to the aero model through the flow state (<see cref="StripFlowState.SystemAspectRatio"/>).
    /// Null while nothing has broken off, where the config's own cache is right.</summary>
    private void RemeasureAspectRatios() =>
        _flowState.SystemAspectRatio = _stripMask is null ? null : AeroModel.MeasureSystemAspectRatios(Config, _stripMask);

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
    /// the 1.5 factor of safety (23.303). V_S0 is the type's own (PracticeScenario.EstimateVso, power off, no flap),
    /// AT THE DESIGN GROSS — see <see cref="InitFloatWaterLoads"/>.
    /// Above it the float's bottom splits (FloatFlooding): the aeroplane does not come apart, it starts to fill on that
    /// side — the damage a float actually takes from a landing far harder than it was designed for. 0 = no floats.
    /// </summary>
    public double FloatUltimateWaterLoadN { get; private set; }
    /// <summary>The limit water load factor n_w that load is built on (23.527), for the log and tests.</summary>
    public double FloatLimitWaterLoadFactor { get; private set; }
    private readonly bool[] _floatSplit = new bool[2];
    private readonly double[] _floatLoadSum = new double[2];
    /// <summary>The time over which a float's structure takes up its water load, s: about a quarter period of a float
    /// on its struts (~10–15 Hz). An assumption, stated (AGENT-LOG NEEDS YOU).</summary>
    public const double FloatLoadWindowS = 0.02;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> VsoCache = new();

    /// <summary>
    /// THE STALL SPEED 23.527 MEANS: V_S0 AT THE DESIGN GROSS, m/s. 23.527 is one equation evaluated at ONE weight —
    /// both the W in it and the V_S0 in it are the weight the float was certificated at.
    ///
    /// Half of that was fixed on 2026-11-11 ("A float was getting 16% weaker because the aeroplane was authored
    /// lighter"): W moved to <see cref="AircraftOverstress.StructuralGrossKg"/>. V_S0 was left reading
    /// PracticeScenario.EstimateVso, which trims the aeroplane at config.Mass.MassKg — the weight AS FLOWN. The two
    /// then disagreed, and the half that was missed was the bigger one: n_w goes as V_S0², so the float lost the whole
    /// mass ratio and not merely its cube root.
    ///
    /// MEASURED on j3-cub-floats-like, whose massKg went 589.67 → 451.142 kg that same morning: EstimateVso read
    /// 33.24 kt where V_S0 at the certificated 1,300 lb is 38.0, n_w came out 3.139 against the 4.12 FloatLandingTests
    /// derives by hand from the certificate, and the ultimate 13.62 kN against 17.9 — a float 24 % weaker than the one
    /// A-691 approved, which split BOTH bottoms on a 98 fpm touchdown.
    ///
    /// V_S ∝ √W at a fixed CLmax, wing area and density, so the design-gross stall is the trimmed one scaled by
    /// √(design / as-flown): no second trim solve, and the result is a property of the TYPE, which is why the cache
    /// holds this and not the as-flown figure. A type authored at its own gross — the rest of the fleet, where
    /// StructuralGrossKg falls back to Mass.MassKg — scales by exactly 1 and does not move.
    /// </summary>
    private static double DesignGrossVsoMs(AircraftConfig c)
    {
        double designKg = StructuralGrossKg(c), flownKg = System.Math.Max(1.0, c.Mass.MassKg);
        return Practice.PracticeScenario.EstimateVso(c, 0.0) * System.Math.Sqrt(designKg / flownKg);
    }

    private void InitFloatWaterLoads()
    {
        if (Config.Floats is null || FloatFlood is null) return;
        // THE DESIGN GROSS, not the weight it is flying at: 14 CFR 23.527's water load is certificated once, and a
        // float does not get weaker because the aeroplane is light today (StructuralGrossKg, in AircraftOverstress.cs).
        double designKg = StructuralGrossKg(Config);
        // THE CACHE KEY CARRIES THE TWO WEIGHTS, not the type id alone. A production config's masses are read from
        // its file and never move, so this still solves one trim per type; but a TEST that loads the same id twice
        // and authors a different mass on each copy — StructuralGrossTests does exactly that, and so does
        // FloatLandingTests.AFloatsUltimateLoadIsItsCertificatesAndDoesNotFollowTodaysWeight — was being handed back
        // whichever copy happened to reach the cache first. xUnit runs those two classes in parallel in one process
        // over one static dictionary, so which answer they got depended on the scheduler. A float's strength is not
        // allowed to depend on that.
        double vso = VsoCache.GetOrAdd($"{Config.Id}|{designKg:R}|{Config.Mass.MassKg:R}", _ => DesignGrossVsoMs(Config));
        int floats = Config.Floats.Count == 1 ? 1 : 2;
        double wLb = designKg * 2.20462 / floats;
        double vKt = vso * 1.943844;
        double beta = Config.Floats.DeadriseDeg * System.Math.PI / 180;
        FloatLimitWaterLoadFactor = 0.012 * vKt * vKt / (System.Math.Pow(System.Math.Tan(beta), 2.0 / 3.0) * System.Math.Pow(wLb, 1.0 / 3.0));
        FloatUltimateWaterLoadN = 1.5 * FloatLimitWaterLoadFactor * designKg * 9.81 / floats;
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
            double inst = own + slamN * (sum > 1e-6 ? own / sum : 1.0 / n);
            // The float and its struts answer the load over their own response time, not one 5 ms step: a single-step
            // spike of the added-mass impulse (a station entering the water) is averaged over FloatLoadWindowS.
            _floatLoadSum[i] += inst * dt - _floatLoadSum[i] * System.Math.Min(1.0, dt / FloatLoadWindowS);
            double load = _floatLoadSum[i] / FloatLoadWindowS;
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
    private Vec3 _noseBody = DefaultNoseBody;
    private static readonly Vec3 DefaultNoseBody = new(1.5, 0, 0);

    /// <summary>
    /// WHERE THE PROPELLER HUB IS, body frame — the airframe's "nose" contact point, or 1.5 m ahead of the origin for a
    /// config that authors none. It is the arm <see cref="PropulsionForces"/> swings the disc's normal and in-plane
    /// forces on, so a caller that leaves <c>noseBody</c> at its default is not evaluating this aeroplane's propeller:
    /// the arm comes out as −cg instead of nose − cg, wrong in magnitude and in SIGN.
    ///
    /// Static, and shared with <see cref="RigSolver.SteadyForces"/>, because that is exactly how the rig and the sim
    /// came apart. <see cref="PropModel.Parts.InPlane"/> only exists on a SOLVED (blade-element) propeller, so the
    /// error was invisible until the blades were fitted and then appeared only on the five types that have them,
    /// measured at their own rig points as a pitching moment the rig had never seen: Extra +126 N·m, Pitts +42, both
    /// Cubs +10, Great Lakes −5, against 0.000 on every unfitted type.
    /// </summary>
    public static Vec3 NoseBody(AircraftConfig config)
    {
        // CACHED PER CONFIG. AirframeContact.BuildPoints walks every strip and builds the wing panels, and the rig
        // solver asks this inside a Jacobian — and AileronForZeroRoll's secant asks it at every spawn, where the same
        // file already records what a re-solve per spawn costs (3,189 ms against 198). The nose is fixed geometry, so
        // it is built once for the config and never again.
        if (_noseCache.TryGetValue(config, out object? hit)) return (Vec3)hit;
        Vec3 nose = NoseIn(AirframeContact.BuildPoints(config));
        _noseCache.AddOrUpdate(config, nose);
        return nose;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, object> _noseCache = new();

    private static Vec3 NoseIn(List<ContactPoint> points)
    {
        foreach (ContactPoint cp in points) if (cp.Name == "nose") return cp.Body;
        return DefaultNoseBody;
    }
    private PropModel.PropDrive _drive = PropModel.PropDrive.Running;
    /// <summary>The wash over the tail the last step flew with (axial m/s, tube radius m, swirl m/s): 0 with no prop turning.</summary>
    public (double DeltaU, double Radius, double Swirl) LastTailSlipstream { get; private set; }
    /// <summary>True once the propeller has hit the ground (or the nose is gone): engine stopped, no thrust. DAMAGE only:
    /// an engine stopped by its mixture is <see cref="EngineStarved"/>; <see cref="EngineRunning"/> covers both.</summary>
    public bool EngineStopped => ReplayEngineRpm.HasValue ? ReplayEngineRpm.Value <= 1.0 : _noseLost || _engineSeized;

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
        // AND THE WING PANELS GO WITH THE WINGS (Ty 2026-11-10: "I bent the wings then over stressed it to the point of
        // failure in flight and the bent wingtips were still there so that is not fixed"). This masked the wing surfaces
        // and dropped the mass but never told the PANELS, so every tip stayed Bent on an aeroplane that had no wings.
        // The drawn tip is downstream of that, and so is the replay's recorded shape (AirframeShape.Capture), so no
        // amount of repairing the renderer could make the tip go away while the sim still insisted it was there.
        //
        // OnComponentLostStructure's side branch is the EXISTING rule for a whole wing going — the panels on that side
        // Broken, their contact points gone, that side's slave struts disconnected — and its own summary already claims
        // "the tip panels that a combat or g-limit wing loss takes with it". It was simply never reached from here,
        // because a g-limit failure does not LoseComponent the wings: the wreck pieces come from
        // CrashRelay.OnWingsFailed instead. Called directly, so nothing sheds the wings' mass a second time on top of
        // the factor above, and no ComponentLost fires to detach geometry the break-up has already thrown.
        OnComponentLostStructure(AirframeComponent.WingLeft);
        OnComponentLostStructure(AirframeComponent.WingRight);
        // A wing that is not there carries no load: the elastic flex and the flex under g come off, and the PLASTIC set
        // stays, because that is the shape the piece fell in. Left on, the flex kept the drawn tip moving after it had
        // gone — StructuralDamage redraws a panel whose FlightFlexRad is non-zero whatever its state — and
        // ApplyOverstressShape stops updating it the moment WingsFailed is set, so it would have been frozen at its last
        // value for the rest of the flight.
        foreach (WingPanel w in _panels) { w.FlightFlexRad = 0.0; w.ElasticRad = 0.0; }
        Structure.OverLimitSeverity = 0.0;
        Structure.FailureTimeSec = Atmosphere.SimTimeSec;
        Structure.RaiseFailed();
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

        targets = LimitBentStab(targets);
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
    /// <summary>The hinge-moment terms for a free axis, for probes: a (flow), b (deflection), k (spring), and the
    /// per-side flow contributions before the interconnect cancels them. Ty 2026-11-09: "the ailerons are connected
    /// to one another as one trys to weathervane the other is also trying to weather vane and cancels it out".</summary>
    public (double A, double B, double K, double Den, double Left, double Right) HingeDebug(string surface, ControlAxisConfig axis)
    {
        (double a, double b, double k, double _) = HingeTerms(surface, axis);
        double left = 0.0, right = 0.0;
        int idx = 0;
        foreach (SurfaceConfig sf in Config.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                int i = idx++;
                if (st.Control is null || st.Control.Surface != surface || Math.Abs(st.Control.Gain) < 0.9) continue;
                double la = 0.0, lb = 0.0;
                double vn = 0.0, vd = 0.0;
                HingeStrip(i, st.Area * st.Chord, Math.Sign(st.Control.Gain), axis, ref la, ref lb, ref vn, ref vd);
                if (st.Pos[1] < 0) left += la; else right += la;
            }
        return (a, b, k, k - b, left, right);
    }

    public double FreeFloat(string surface, ControlAxisConfig axis, double trimRad, double currentRad)
    {
        (double a, double b, double k, double vane) = HingeTerms(surface, axis);
        double den = k - b;
        if (den <= 0.0 && b > 0.0)
        {
            // Net DESTABILIZING (reversed flow beats the spring): the LINEAR balance point is unstable, so the surface
            // runs away from it — but it stops where it has turned far enough INTO the flow to be a vane, aligned with
            // the air over it. That still reaches the stop when the flow genuinely asks (a tailslide), and does not
            // when it does not.
            return axis.ClampDefl(vane);
        }
        if (den < 1e-6) return axis.ClampDefl(vane);
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
        // THE LINEAR BALANCE IS A SMALL-DEFLECTION EXPANSION. a and b are hinge-moment slopes about the trail
        // position, so a/(k−b) is only the answer while the surface is somewhere near there. In the Pitts' flat
        // spin the aileron's own terms measure a +11.7…+18.6 against b −8.5…−22.9 with NO centering spring at all
        // (k = 0), and the root comes out at 50–79° on a surface that has 25°: it sits on the stop every step.
        // That is the owner's report, twice — "the problem is the ailerons sticking at full opposite", and after
        // three more builds, "The ailerons still peg and stick at full opposite too which is not 'real' behavior".
        //
        // When the root lies outside the travel the expansion has been extrapolated past any validity, so take the
        // VANE instead: the angle at which the surface is aligned with the air over it, which this already computes
        // for the destabilising case and which measures −3° to −10° here.
        //
        // PARTIAL, and the rest is measured rather than guessed. It stops the hard peg — the aileron floats 17-24°
        // instead of sitting at exactly 25.0 every step — but that is still most of the travel, because `a` itself
        // is large: the hinge-moment slope is evaluated on a wing at α 70-85, far outside where a slope about the
        // trail position means anything. Gating on the strips' own separation was tried and does nothing, because
        // `StripFlowState.Separation` reads ~0 for these aileron strips even at α 78 — worth knowing before the
        // next attempt. AileronPegProbe (AIL_PEG=1) prints the whole balance each second.
        if (Math.Abs(d) > axis.MaxDeflRad) return axis.ClampDefl(vane);
        return axis.ClampDefl(d);
    }

    /// <summary>The hinge-moment terms of <see cref="FreeFloat"/>: a (flow), b (deflection, negative), k (spring).</summary>
    private (double a, double b, double k, double vane) HingeTerms(string surface, ControlAxisConfig axis)
    {
        double a = 0.0, b = 0.0, scRef = 0.0;
        double vaneNum = 0.0, vaneDen = 0.0;
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
                HingeStrip(i, sc, g, axis, ref a, ref b, ref vaneNum, ref vaneDen);
            }
        }
        double vSpring = axis.CenteringSpringKt * 0.514444;
        return (a, b, 0.5 * 1.225 * vSpring * vSpring * scRef * Math.Abs(axis.HingeChDelta),
                vaneDen > 1e-9 ? vaneNum / vaneDen : 0.0);
    }

    /// <summary>One hinged strip's share of the hinge-moment terms (see <see cref="HingeTerms"/>): flow term into
    /// <paramref name="a"/>, deflection stiffness into <paramref name="b"/>. <paramref name="g"/> = the sign of its gain.</summary>
    private void HingeStrip(int i, double sc, double g, ControlAxisConfig axis, ref double a, ref double b,
                            ref double vaneNum, ref double vaneDen)
    {
        if (i >= _flowState.HingeQ.Length) return;
        double qsc = _flowState.HingeQ[i] * sc;
        // Attached flow (to 20°): the linear Chα. Separated (owner 2026-10-12, CFI/Pitts: in a developed spin the air from
        // below holds the free elevator nearly full aft and the rudder pro-spin): a surface hinged at its leading edge in
        // separated crossflow trails WITH the local flow, like a vane — its flow term grows to Chδ·α, so the float
        // goes to −α (against no spring: the stop). Blended from 20° to 40° of local flow.
        // WHICH WAY THE SURFACE WANTS TO TRAIL is set by the flow NORMAL to it, and that normal is the same sign on
        // both wings. HingeAlphaRad is reversed in tail-first flow (see StripFlowState), which is right for the
        // ANGLE but wrong for the float's direction: in a flat spin the retreating wing's CHORDWISE component
        // reverses (+/-11 m/s from the rotation) while the 25 m/s coming from below does not, so both ailerons still
        // want to trail the same way in absolute terms. Left reversed, one side's float flipped sign, the two stopped
        // cancelling through the interconnect and ADDED instead - measured L +14, R +21 on the stick where they
        // should have met near zero. Ty 2026-11-09: "the ailerons are connected to one another as one trys to
        // weathervane the other is also trying to weather vane and cancels it out".
        double dirNow = i < _flowState.HingeFlowDir.Length ? _flowState.HingeFlowDir[i] : 1.0;
        // CONTINUOUSLY, not by its SIGN. HingeFlowDir runs +1 (normal flow) through 0 (pure crossflow) to -1 (flow
        // from the trailing edge), and taking sign(dir) puts a step right at zero: wherever the chordwise flow hovers
        // near crossflow the float target snaps between extremes every frame. Ty 2026-11-09: "there's a high
        // frequency twitch especially in aileron post crash that has crept in today... it could be from that aileron
        // centering work" - and he was right, it was this. Post-crash, tumbling and sliding, dir crosses zero
        // constantly.
        //
        // Using dir ITSELF is also the better statement: at pure crossflow a surface has no preferred direction to
        // trail, so its flow term should FADE OUT there rather than flip, and it still un-reverses properly at -1.
        // dir ITSELF is continuous but scales the term down everywhere (|dir| < 1 through a spin), which threw away
        // the interconnect cancellation and put the ailerons back on their stops. So: full magnitude wherever the
        // flow has a definite direction, fading to zero only in a narrow band either side of pure crossflow, where a
        // surface genuinely has no preferred way to trail.
        const double dirBand = 0.15;
        double al = _flowState.HingeAlphaRad[i] * Math.Clamp(dirNow / dirBand, -1.0, 1.0);
        double att = Math.Clamp(al, -0.35, 0.35);
        double sep = Math.Clamp((Math.Abs(al) - 0.35) / 0.35, 0.0, 1.0); sep = sep * sep * (3 - 2 * sep);
        // THE LEADING-EDGE STALL LATCH IS GONE (Ty 2026-11-10: "Delete the leading edge stall latch"). It pinned
        // sep = 1 — full vane behaviour — from the strip's own stall angle until the angle had fallen a band below
        // it, and the RELEASE was the defect: measured through a Beggs-Mueller recovery, elevator strips sat latched
        // at 7-11 degrees of their own local flow one second after the throttle closed, and did not let go for 12 s.
        // While latched the vane angle is -alpha_local, which at 30-50 deg of local flow is outside the elevator's
        // +/-27 deg of travel, so the surface sat on its aft stop for the whole recovery: 7.07 turns to stop
        // rotating against a turn or two in the real aeroplane, and the nose would not drop when the power came off.
        // The separation blend above (20-40 deg of local flow) now decides it on its own.
        a += g * qsc * ((1 - sep) * axis.HingeChAlpha * att + sep * axis.HingeChDelta * al);
        // Deflection stiffness follows the flow direction: centering in normal flow, gone in crossflow, and
        // REVERSED and ~2x stronger when the air comes from the trailing edge (tailslide: the hinge is now
        // downstream and the load acts near the leading free edge) — the free surface is pushed to its stop.
        double dir = dirNow;
        b += qsc * axis.HingeChDelta * (dir >= 0 ? dir : 2.0 * dir);
        // Where a vane would sit on this strip: aligned with its own flow, weighted by what it feels. Summed with the
        // gain, so an interconnected pair's symmetric share cancels here exactly as it does in the flow term above.
        vaneNum += g * qsc * -al; vaneDen += qsc;
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
            (double a, double b, double k, double _) = HingeTerms(surface, axis);
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

    // ---- wake (docs/WAKE-VORTEX.md) ----------------------------------------------------------------

    /// <summary>The wake this aeroplane sheds (<see cref="VortexWake.CreateSource"/>), or null: it sheds nothing. Set by
    /// whoever puts the aeroplane in the world (the player, a tug, a drone, a replayed ship), never by the sim itself, so
    /// probes and trim runs leave no wakes behind.</summary>
    public WakeSource? Wake { get; set; }
    /// <summary>False: other wakes do not reach this aeroplane's strips.</summary>
    public bool FeelsWake { get; set; } = true;
    /// <summary>Its own wake is felt only once older than this (s): the near wake is already its induced drag and
    /// downwash; a tight turn back into its own older wake is felt like anybody else's.</summary>
    public const double OwnWakeMinAgeSec = 5.0;
    /// <summary>The wake's air velocity at the CG (body frame, m/s) on the last step: zero out of every wake.</summary>
    public Vec3 LastWakeAtCgBody { get; private set; }
    private Vec3[]? _stripRelBody, _stripWakeBody, _stripWakeStep;
    private double _wakeReachM, _wakeHalfSpanM, _wakeTailArmM;
    private readonly List<VortexSegment> _wakeNear = new(), _wakeFar = new();
    private readonly List<JetSegment> _jetNear = new();
    private Vec3 _lastAeroForceBody;

    /// <summary>The wake's air at every strip, body frame, once a step (held through RK4's stages like the wind), or
    /// null when no wake is within reach of the airframe. Segments within about a span of the airframe are evaluated at
    /// every strip (that is where a line's field bends across the wing); those further off only at the CG, the tips and
    /// the tail, and spread over the strips as a linear field — what the regional air already does.</summary>
    private Vec3[]? SampleWakeAtStrips()
    {
        LastWakeAtCgBody = Vec3.Zero;
        if (!FeelsWake || !VortexWake.Active) return null;
        if (_stripRelBody is null)
        {
            int n = 0;
            foreach (SurfaceConfig sf in Config.Surfaces) n += sf.Strips.Count;
            _stripRelBody = new Vec3[n];
            _stripWakeBody = new Vec3[n];
            Vec3 cg = Config.Mass.CgVec();
            int i = 0;
            _wakeReachM = 0.5;
            double tail = 0; int nt = 0;
            foreach (SurfaceConfig sf in Config.Surfaces)
                foreach (StripConfig st in sf.Strips)
                {
                    _stripRelBody[i] = st.PosVec() - cg;
                    _wakeReachM = Math.Max(_wakeReachM, _stripRelBody[i].Length);
                    _wakeHalfSpanM = Math.Max(_wakeHalfSpanM, Math.Abs(_stripRelBody[i].Y));
                    if (_stripRelBody[i].X < -0.5 && Math.Abs(_stripRelBody[i].Y) < 0.5 * _wakeReachM) { tail += _stripRelBody[i].X; nt++; }
                    i++;
                }
            _wakeTailArmM = nt > 0 ? tail / nt : 0.0;
        }
        Quat q = State.Attitude, qc = q.Conjugate();
        Vec3 c = State.Position;
        VortexWake.Gather(c, _wakeReachM, Wake, OwnWakeMinAgeSec, _wakeNear, _jetNear);
        if (_wakeNear.Count == 0 && _jetNear.Count == 0) return null;
        _wakeFar.Clear();
        for (int i = _wakeNear.Count - 1; i >= 0; i--)
        {
            VortexSegment sg = _wakeNear[i];
            if (WakeSourceDistance(c, sg) > 2.0 * _wakeReachM + 4.0 * sg.CoreRadius) { _wakeFar.Add(sg); _wakeNear.RemoveAt(i); }
        }
        Vec3 farCg = Vec3.Zero, perY = Vec3.Zero, perX = Vec3.Zero;
        if (_wakeFar.Count > 0)
        {
            farCg = qc.Rotate(VortexWake.VelocityFrom(_wakeFar, null, c));
            if (_wakeHalfSpanM > 0.25)
            {
                Vec3 l = qc.Rotate(VortexWake.VelocityFrom(_wakeFar, null, c + q.Rotate(new Vec3(0, -_wakeHalfSpanM, 0))));
                Vec3 r = qc.Rotate(VortexWake.VelocityFrom(_wakeFar, null, c + q.Rotate(new Vec3(0, _wakeHalfSpanM, 0))));
                perY = (r - l) / (2.0 * _wakeHalfSpanM);
            }
            if (_wakeTailArmM < -0.5)
                perX = (qc.Rotate(VortexWake.VelocityFrom(_wakeFar, null, c + q.Rotate(new Vec3(_wakeTailArmM, 0, 0)))) - farCg) / _wakeTailArmM;
        }
        bool near = _wakeNear.Count > 0 || _jetNear.Count > 0;
        for (int i = 0; i < _stripRelBody.Length; i++)
        {
            Vec3 rel = _stripRelBody[i];
            Vec3 v = farCg + perX * rel.X + perY * rel.Y;
            if (near) v += qc.Rotate(VortexWake.VelocityFrom(_wakeNear, _jetNear, c + q.Rotate(rel), cullByInfluence: false));
            _stripWakeBody![i] = v;
        }
        LastWakeAtCgBody = farCg + (near ? qc.Rotate(VortexWake.VelocityFrom(_wakeNear, _jetNear, c, cullByInfluence: false)) : Vec3.Zero);
        return _stripWakeBody;
    }

    private static double WakeSourceDistance(Vec3 p, in VortexSegment s)
    {
        Vec3 ab = s.B - s.A;
        double l2 = ab.LengthSquared;
        double t = l2 > 1e-12 ? Math.Clamp(Vec3.Dot(p - s.A, ab) / l2, 0.0, 1.0) : 0.0;
        return (p - (s.A + ab * t)).Length;
    }

    /// <summary>After the step: shed into <see cref="Wake"/> from the aerodynamic force the step actually made.</summary>
    private void ShedWake(double dt, Vec3 windBody, double airDensity, double slipDu, double slipR, double slipSwirl)
    {
        if (Wake is null) return;
        Quat q = State.Attitude;
        Wake.Shed(dt, State.Position, q, q.Rotate(State.Velocity - windBody), q.Rotate(_lastAeroForceBody), airDensity,
            slipDu, slipR, slipSwirl);
    }

    /// <summary>A pose set from outside (a recorded ship): shed from the load factor the recording carries.</summary>
    public void ShedPoseWake(double dt)
    {
        if (Wake is null || dt <= 0.0) return;
        Quat q = State.Attitude;
        Vec3 windWorld = Atmosphere.MeanWindAtPosition(State.Position);
        Vec3 airWorld = q.Rotate(State.Velocity) - windWorld;
        Wake.ShedFromLoadFactor(dt, State.Position, q, airWorld, AeroLoadFactorZ, MassProperties.MassKg * Atmosphere.GravityMs2,
            Atmosphere.DensityAt(State.Position));
    }

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
        // THE GUST THIS SPAN ACTUALLY FEELS (Atmosphere.WindForSpan). A wing averages away every eddy smaller than
        // itself, and at 50 ft AGL a third of the field's variance is smaller than a 15 m span — sampling it at one
        // point and applying it to the whole airframe was Ty's jitter, on every type at every speed (2026-11-09).
        Vec3 spanAxisWorld = q.Rotate(new Vec3(0, 1, 0));
        Vec3 wCg = Atmosphere.WindForSpan(cgPos, cg, 2 * _halfSpanM, spanAxisWorld, out Vec3 gustGradWorld);
        windBody = qc.Rotate(wCg);
        meanWindBody = qc.Rotate(Atmosphere.WindFromSample(cgPos, cg, false));

        // THE SPANWISE GRADIENT, which is how a pilot finds lift: the wing in the stronger air rises and you turn
        // toward it. The gust's share of it is the closed-form first moment across the span, from the same modes; the
        // rest — mean-wind shear, the thermal columns, the river — is large-scale and still differenced, which is the
        // right answer for structure far bigger than the aeroplane. Differencing the GUST at +/- half span, as this
        // did, was aliasing a field with metre-scale structure in it, so the roll cue was partly noise.
        Vec3 WindNoGust(Vec3 bodyOffset)
        {
            Vec3 p = cgPos + q.Rotate(bodyOffset);
            return qc.Rotate(Atmosphere.WindFromSample(p, Atmosphere.SampleAt(p)!.Value, false));
        }
        Vec3 left = WindNoGust(new Vec3(0, -_halfSpanM, 0)), right = WindNoGust(new Vec3(0, _halfSpanM, 0));
        Vec3 perY = (right - left) / (2 * _halfSpanM) + qc.Rotate(gustGradWorld);
        // Along the fuselage only the large-scale wind is differenced. The gust's own longitudinal gradient over a
        // 4 m tail arm was aliased noise and is left out rather than left wrong; it is second order beside the
        // uniform gust, and giving it the same closed-form treatment as the span is a job of its own.
        Vec3 perX = Vec3.Zero;
        if (System.Math.Abs(_tailArmM) > 0.5)
            perX = (WindNoGust(new Vec3(_tailArmM, 0, 0)) - qc.Rotate(Atmosphere.WindFromSample(cgPos, cg, false))) / _tailArmM;
        gradient = perX.LengthSquared + perY.LengthSquared > 0 ? new WindGradient(perX, perY) : default;
    }

    /// <summary>
    /// The forces on the aeroplane AT ITS CURRENT STATE, worked out fresh and returned, without stepping
    /// anything. This is what a replay needs. The physics does not run while a recording is played back, so
    /// <see cref="LastForces"/> still holds whatever the last LIVE step computed: drawing that would hang one
    /// instant's forces on another instant's attitude, a picture that is right only on the frame the replay
    /// was entered on.
    ///
    /// Only what is a PURE FUNCTION of the state is recomputed — aerodynamics, weight, thrust, the open
    /// canopy's drag, the parachute, the tow rope. Ground contact, the landing gear and the hull in the water
    /// are deliberately left out: their load is set by how far the integrator has pressed the airframe into
    /// the ground or the water, which is history and not pose, so no recorded frame can recover it.
    ///
    /// Nothing the live sim relies on is touched. The aero model runs against a scratch flow state, sharing
    /// the real separation memory and the real damage (it only reads those) but keeping its own coefficient
    /// arrays, and left UNPRIMED so it returns the quasi-steady coefficients rather than the lagged ones
    /// belonging to some other moment. Neither LastForces, LoadFactorZ nor SpecificForceBody is written.
    /// </summary>
    public List<ForceSample> ForcesAtCurrentState()
    {
        RigidBodyState s = State;
        double airDensity;
        Vec3 windBody, meanWindBody;
        WindGradient windGradient = default;
        if (Atmosphere.Field is null)
        {
            airDensity = Atmosphere.DensityAtAltitude(-s.Position.Z);
            Vec3 windWorld = Atmosphere.WindAtPosition(s.Position);
            windBody = windWorld.LengthSquared > 1e-9 ? s.Attitude.Conjugate().Rotate(windWorld) : Vec3.Zero;
            Vec3 meanWindWorld = Atmosphere.MeanWindAtPosition(s.Position);
            meanWindBody = meanWindWorld.LengthSquared > 1e-9 ? s.Attitude.Conjugate().Rotate(meanWindWorld) : Vec3.Zero;
        }
        else SampleRegionalAir(out airDensity, out windBody, out meanWindBody, out windGradient);

        ControlDeflections controls = new(_aileronRad, _elevatorRad, _rudderRad, _spoilerFraction, FlapFraction, SlatFraction);

        int stripCount = 0;
        for (int i = 0; i < Config.Surfaces.Count; i++) stripCount += Config.Surfaces[i].Strips.Count;
        _overlayFlow ??= new StripFlowState();
        _overlayFlow.EnsureSize(stripCount);
        // Shared because AeroModel only READS them, and the overlay should show the wing the aeroplane
        // actually has: how far each strip has separated, and whatever the trees and the ground have torn off.
        _overlayFlow.Separation = _flowState.Separation;
        _overlayFlow.DeflectionOverride = _flowState.DeflectionOverride;
        _overlayFlow.BendRad = _flowState.BendRad;
        _overlayFlow.BendOffset = _flowState.BendOffset;
        _overlayFlow.IncidenceDeltaRad = _flowState.IncidenceDeltaRad;
        _overlayFlow.LiftFactor = _flowState.LiftFactor;
        _overlayFlow.ExtraCd = _flowState.ExtraCd;
        _overlayFlow.DownwashEpsLagged = _flowState.DownwashEpsLagged;
        _overlayFlow.LagPrimed = false;   // quasi-steady: this state's coefficients, not the last live step's

        double stalled = AeroModel.InstantStalledFraction(Config, s.Velocity, s.Rates, windBody, _surfaceMask, _stripMask, _airfoilTables);
        (double slipDu, double slipR, double slipSwirl) = TailSlipstream(Config, _throttle01, s.Velocity, s.Rates, airDensity, Drive, _noseLost, _swirl);
        // The strips take the jet from PropJet's traced tube inside AeroModel; this scalar is only the bulk spiral-slipstream
        // yaw's share, measured from the same trace.
        double overTail = PropJet.TailShare(Config, s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position)),
                                            s.Rates, Config.Mass.CgVec(), slipDu, slipR, slipSwirl);

        double under = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
        double? water = FloatHydro.WaterSurfaceAt(s.Position.X, s.Position.Y, -s.Position.Z);
        if (water.HasValue && water.Value > under) under = water.Value;
        double wingAgl = -s.Position.Z - under + AeroModel.WingHeightAboveCg(Config);

        List<ForceSample> samples = new(160);
        List<ForceSample>? outer = ForceDebug.Samples;
        ForceDebug.Samples = samples;
        double savedSlipOverTail = _slipOverTail;
        PropModel.PropDrive savedDrive = _drive;
        try
        {
            _slipOverTail = overTail;
            _drive = Drive;   // MountedPropulsion reads the field; both are re-derived at the top of every live step
            AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls, stalled,
                _overlayFlow, slipDu, slipR, _surfaceMask, _stripMask, meanWindBody, wingAgl, slipSwirl, windGradient,
                Config.Propulsion is not null && PropModel.IsSolved(Config.Propulsion, _drive), _swirl);

            Vec3 gravityBody = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, MassProperties.MassKg * Atmosphere.GravityMs2));
            ForceDebug.Add(Config.Mass.CgVec(), gravityBody, Vec3.Zero, "weight");

            if (ExtraDragAreaM2 > 0 || _cowlFlap01 > 0)
            {
                Vec3 vAir = s.Velocity - windBody;
                double vA = vAir.Length;
                if (vA > 1.0)
                    ForceDebug.Add(ExtraDragPointBody, vAir * (-0.5 * airDensity * vA * (ExtraDragAreaM2 + _cowlFlap01 * CowlFlapDragAreaM2)), Vec3.Zero, "extra-drag");
            }
            if (Brs.Out && Config.Brs is not null)
            {
                (Vec3 bF, Vec3 _) = Brs.Forces(Config, s, windBody, airDensity);
                ForceDebug.Add(Config.Brs.BridleVec(), bF, Vec3.Zero, "brs");
            }
            if (Config.Propulsion is not null) MountedPropulsion(s.Velocity, s.Rates, airDensity);
            if (ExternalForceWorld.LengthSquared > 1e-9)
                ForceDebug.Add(ExternalForcePointBody, s.Attitude.Conjugate().Rotate(ExternalForceWorld), Vec3.Zero, "rope");
        }
        finally
        {
            _slipOverTail = savedSlipOverTail;
            _drive = savedDrive;
            ForceDebug.Samples = outer;
        }
        return samples;
    }

    /// <summary>
    /// Advances the aircraft by one fixed timestep toward explicit deflection targets, bypassing the RC
    /// stick shaping. Used to spawn/hold a solved trim (e.g. tow-release trim, per VERTICAL-SLICE.md)
    /// without needing to invert the dead-zone/expo curve to find the stick position that reproduces it.
    /// </summary>
    public void StepWithDeflectionTargets(ControlDeflections targets, double dt)
    {
        BeginOccupantStep();
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
        _stripWakeStep = SampleWakeAtStrips();   // a field, not a local: the RK4 closure would carry it (8 B a step)
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
        (double slipDu, double slipR, double slipSwirl) = TailSlipstream(Config, _throttle01, State.Velocity, State.Rates, airDensity, _drive, _noseLost, _swirl);
        // WHERE that wash goes is PropJet's traced tube, per strip, inside AeroModel: so the blowing follows alpha, beta and
        // the body rates out of the geometry, with no stall gate on it. What is left here is the bulk spiral-slipstream yaw
        // on the fin (PropModel.Parts.SlipYaw), which is a moment and not a strip load, so it takes the tube's share of the
        // tail from the same trace (PropJet.TailShare).
        _slipOverTail = PropJet.TailShare(Config, State.Velocity - State.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(State.Position)),
                                          State.Rates, Config.Mass.CgVec(), slipDu, slipR, slipSwirl);
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
            (Vec3 aeroForce, Vec3 aeroMoment) = AeroModel.Compute(Config, _airfoilTables, s.Velocity, s.Rates, windBody, airDensity, controls, _wakeStalledFrac, _flowState, slipDu, slipR, _surfaceMask, _stripMask, meanWindBody, wingAgl, slipSwirl, windGradient,
                Config.Propulsion is not null && PropModel.IsSolved(Config.Propulsion, _drive), _swirl, _stripWakeStep);
            _lastAeroForceBody = aeroForce;
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
                // The 2-33's wheel brake is ON the dive-brake handle, at the end of its travel, but the HANDLE is the
                // input's (Core.Vr.SpoilerHandle: the strip's and the hand's last 15 %, the knob's band under 20 %),
                // which already writes it into BrakeInput. The spoiler deployment is not the handle: the knob has its
                // spoilers full at 25 % and its brake from 20 %, so reading the brake off the spoiler here as well
                // put full wheel brake on at full spoiler, before any brake was asked for (Ty 2026-11-05 16:06:
                // "the tire screeches with any braking at all on the 2-33").
                double wheelBrake = BrakeInput;
                long tGear = SimProfile.Begin();
                (Vec3 gForceWorld, Vec3 gMomentWorld) = LandingGear.Compute(Config, s, rudderCmd, wheelBrake, 0.0, BrakeBias, _lostGear, _pendingBreaks, _gearTouching, _impacts, GearExtension > 0.9 ? Tailwheel : null, amphibUp, stage, stepDt, _gearStageClear, _wheels, Skis);
                SimProfile.End(SimProfile.Scope.Gear, tGear);
                totalF += s.Attitude.Conjugate().Rotate(gForceWorld);
                totalM += s.Attitude.Conjugate().Rotate(gMomentWorld);
            }
            if (Skis != null && _skiDragAreaM2 > 0)
            {
                // The wheel-skis' own flat-plate drag (SkiGear.DragAreaM2: the blunt ski, its upturned tip, the links), up
                // or down — raised, they still hang in the wind.
                Vec3 vAir = s.Velocity - windBody;
                double vA = vAir.Length;
                if (vA > 1.0) totalF -= vAir * (0.5 * airDensity * vA * _skiDragAreaM2);
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
                (Vec3 pF, Vec3 pM) = MountedPropulsion(s.Velocity, s.Rates, airDensity);
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
            AeroRollMomentNm = aeroMoment.X;
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
        // The step's surface contacts, before anything else can recompute the (process-wide) float state.
        FlyingGame.Core.SurfaceContacts.Collect(Config, State, _wheels, Config.Floats is not null, SurfaceContacts, Atmosphere.SimTimeSec);
        SurfaceContactsStepped?.Invoke(SurfaceContacts);
        // The marks those contacts leave in the ground (AircraftImprints): the airframe contact points still hold what
        // this step's first stage recorded, which is what a scar's depth is read from.
        LayImprints(Atmosphere.SimTimeSec);
        if (Config.Floats is not null)
        {
            // The water the floats carry (FloatHydro.AddedMassOf): applied implicitly, like the airframe's below.
            FloatHydro.AddedMass am = FloatHydro.AddedMassOf(Config, State);
            RigidBodyState beforeAm = State;
            State = FloatHydro.CorrectForAddedMass(preStep, State, MassProperties, _floatAddedMass, am, dt,
                WaterCurrent.SurfaceAt(State.Position.X, State.Position.Y));
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
        Atmosphere.AdvanceTime(this, dt);   // only the clock's owner moves it (Atmosphere.ClockOwner)
        ShedWake(dt, windBody, airDensity, slipDu, slipR, slipSwirl);
        // BEFORE the structure steps, because flutter lowers the ultimate strength the structure is about to be
        // judged against: a wing whose fatigue has just taken it to 1 g has to be weak on THIS step's load, not the
        // next one's.
        StepFlutter(dt);
        UpdateStructure(dt);
        UpdateLoad(dt);
        Electrics.Step(dt, EngineRpm);
        StepTrees(preStep, dt);   // stems and crowns: snap, stop, or take the part that hit them (AircraftTreeStrike)
        StepGlazing(dt);          // the canopy, windshield and windows: shattered only by a direct arrival (AircraftGlazing)
        if (_impacts.MaxClosingMs > 0.0) LastImpact = _impacts.Last;
        FilterFuselageBreaks();   // a fuselage section crushes before it breaks (AircraftFuselageCrush)
        foreach (AirframeComponent c in _pendingBreaks) LoseComponent(c);
        AfterStepContactDamage(dt);
        if (_impacts.MaxClosingMs > 0.0) HardImpact?.Invoke(_impacts.MaxClosingMs, _impacts.Point);
        Damage.Update(dt);
        if (Tailwheel != null && GearExtension > 0.9 && !_lostGear.Contains(AirframeComponent.GearTail))
        {
            double rudderCmd = Config.Controls.Rudder.MaxDeflRad > 1e-6 ? _rudderRad / Config.Controls.Rudder.MaxDeflRad : 0;
            LandingGear.UpdateTailwheel(Config, State, Tailwheel, rudderCmd, dt, Skis);
        }
        Skis?.Selector.Step(dt);
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
        // A type whose wing tables are DERIVED from its section rather than calibrated by feel may instead measure the
        // band from each strip's own static stall: StallDynamicsConfig.SeparationOnPastStallDeg / ReattachBelowStallDeg,
        // null for every type that does not (the full argument is on those two properties). The Extra 300 is the first,
        // 2026-11-10; its stations stall at 21–24°, where a fixed 16° onset would call them separated five degrees early.
        double? onPast = Config.StallDynamics.SeparationOnPastStallDeg, offBelow = Config.StallDynamics.ReattachBelowStallDeg;
        bool ownStall = onPast.HasValue && offBelow.HasValue;
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
            if (ownStall && i < _flowState.StallPosRad.Length && _flowState.StallPosRad[i] > 0.0)
            {
                double stall = aSigned < 0.0 ? -_flowState.StallNegRad[i] : _flowState.StallPosRad[i];
                on = Math.Max(stall + onPast!.Value * Math.PI / 180.0, 2.0 * Math.PI / 180.0);
                off = Math.Max(stall - offBelow!.Value * Math.PI / 180.0, 1.0 * Math.PI / 180.0);
            }
            double s0 = _flowState.Separation[i];
            double target = a > on ? 1.0 : a < off ? 0.0 : s0;
            double tauSec = target > s0 ? Config.StallDynamics.StripSepTau : Config.StallDynamics.StripReattachTau;
            _flowState.Separation[i] = s0 + (target - s0) * (1.0 - Math.Exp(-dt / tauSec));
        }
        _wheelsUnseeded = false;   // the State writes above are the step's own
        EndOccupantStep(dt);       // what the step did to the pilot in his seat (AircraftOccupant)
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
        => TailSlipstream(config, throttle01, velocity, rates, airDensity, drive, propGone, null);

    /// <summary>The same, also filling the slipstream's radial swirl distribution for the jet to interpolate.</summary>
    public static (double DeltaU, double Radius, double Swirl) TailSlipstream(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity,
        PropModel.PropDrive drive, bool propGone, BladeElement.SwirlProfile? profile)
    {
        // SimProfile only (Scope.Slipstream): on a type with a fitted blade this call solves a whole propeller disc
        // once a step, on top of the four the RK4 stages solve, and that had never been separated from the rest of the
        // step. Off, Begin() is one thread-static read.
        long tSlip = SimProfile.Begin();
        (double DeltaU, double Radius, double Swirl) r = TailSlipstreamInner(config, throttle01, velocity, rates, airDensity, drive, propGone, profile);
        SimProfile.End(SimProfile.Scope.Slipstream, tSlip);
        return r;
    }

    private static (double DeltaU, double Radius, double Swirl) TailSlipstreamInner(AircraftConfig config, double throttle01, Vec3 velocity, Vec3 rates, double airDensity,
        PropModel.PropDrive drive, bool propGone, BladeElement.SwirlProfile? profile)
    {
        if (profile is not null) profile.Any = false;
        if (config.Propulsion is null || config.Propulsion.PropDiameterM <= 0.0 || propGone) return (0.0, 0.0, 0.0);
        double vNow = velocity.Length;
        double thr = throttle01;
        if (config.Engines.Count > 0)
        {
            double scale = 0.0; int n = 0;
            foreach (EngineMount m in config.Engines) { if (!m.Feathered) scale += m.ThrottleScale; n++; }
            thr *= n > 0 ? scale / n : 1.0;
        }
        (Vec3 tF, _) = PropModel.Compute(config.Propulsion, thr, velocity, rates, airDensity, drive, out PropModel.Parts parts, profile);
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
        // THE THRUST IS SIGNED, and at idle it goes NEGATIVE. Floored at zero the disc could only ever make a jet,
        // never a wake: at idle the tail got air at freestream speed when it should be getting air the propeller has
        // SLOWED. Ty 2026-11-09: "at idle not only will the prop not produce a jet tube but it will in fact slow the
        // air down." Momentum theory says the same thing with the sign kept - v_slip^2 = V^2 + 2T/(rho A) is below V
        // when T < 0 - and it is the same equation the windmilling branch above already uses; the only reason a
        // RUNNING engine never reached it was the Max(0).
        double thrust = tF.X;
        double vSlip2 = vNow * vNow + 2.0 * thrust / (airDensity * disc);
        double vSlip = System.Math.Sqrt(System.Math.Max(0.0, vSlip2));
        double du = 0.9 * (vSlip - vNow);                       // < 0 when the disc is braking the air
        // A SOLVED PROPELLER MEASURES ITS OWN TUBE, in width and in rotation, instead of taking the two constants
        // below. Both of those constants are the STATIC limit of the real thing wearing a fixed number, which is why
        // they looked harmless: at V = 0 they are very nearly right.
        //
        // THE WIDTH. 0.7 R is momentum theory's contraction with the aeroplane standing still, where the air arrives
        // at the disc at v_i and leaves at 2 v_i, so the area must halve. In flight it arrives at V + v_i and leaves
        // at V + 2 v_i, so it halves only in the limit V -> 0 and the real ratio is
        //     R_s / R = sqrt((V + v_i) / (V + 2 v_i)),
        // which at the Pitts' own rig condition is 0.98, not 0.7. Fixed at 0.7 the tube carried 150 kg/s where the
        // disc had passed 295 — half the air simply missing — and every surface near the wash's edge sat wrongly
        // outside it.
        //
        // THE ROTATION. SwirlFactor = 0.2 says the tangential speed is a fifth of the axial increase, one number for
        // every propeller ever flown here. It need not be guessed: the torque the blades make IS the angular momentum
        // they put into the air, and PropJet turns the tube as a solid body, so the two are the same statement —
        //     Q = rho * V_t * 2 pi * Omega * integral(r^3 dr, 0..R_s) = rho * V_t * pi * Omega * R_s^4 / 2
        // and the edge speed PropJet wants is Omega * R_s = 2 Q / (rho * pi * R_s^3 * V_t). What the blades took out
        // of the crankshaft is then exactly what the fin feels, with nothing in between to tune.
        if (parts.SwirlC != 0.0 && System.Math.Abs(du) > 1e-6)
        {
            double vi = 0.5 * (vSlip - vNow);                       // momentum theory at the disc; far wake is 2 v_i
            // AND A BRAKING DISC EXPANDS ITS WAKE. The ratio is the same sqrt((V+vi)/(V+2vi)), but with vi NEGATIVE
            // the denominator is the smaller of the two and it comes out ABOVE one. Clamped to 1.0 it could only ever
            // contract, which is the thrusting case wearing the general formula's clothes. The upper bound is kept
            // loose rather than absent: the far wake of a disc near zero through-flow is not a tube at all.
            double contraction = System.Math.Sqrt((vNow + vi) / System.Math.Max(1e-6, vNow + 2.0 * vi));
            double rs = radius * System.Math.Clamp(contraction, 0.70, 1.6);
            double vTube = vNow + du;
            double q = System.Math.Abs(parts.TorqueRoll);
            double edge = 2.0 * q / System.Math.Max(1e-6, airDensity * System.Math.PI * rs * rs * rs * vTube);
            return (du, rs, edge * config.Propulsion.RotationSign);
        }
        return (du, 0.7 * radius, SwirlFactor * du * config.Propulsion.RotationSign);
    }

    // <summary>
    // SUPERSEDED, 2026-11-08. The share of the propeller's wash that still passes over the tail used to be this one
    // scalar — Glauert's skewed wake (ARC R&amp;M 1111, 1926; Leishman §2.14) as the overlap of two circles, applied to
    // the whole tail at once and in proportion to the WING's separated share, which is a stall gate and not physics
    // (owner 2026-11-08: the body-locked jet "would provide artificial stability"). It is now
    // <see cref="PropJet"/>: the tube is traced from the disc through the air's own motion, each surface takes what the
    // geometry gives it, and the bulk spiral-slipstream yaw takes <see cref="PropJet.TailShare"/> — the same
    // two-circle overlap, but against the traced path and with no gate.
    // </summary>
    /// <summary>From the propeller disc (AirframeContact's hub: 0.1 m ahead of the nose) to the tail's area-weighted
    /// station (every non-wing strip), m — the reference length the traced jet is measured along
    /// (<see cref="PropJet.TailArm"/>, one copy of the construction). 0 for a type with no single nose propeller.</summary>
    public static double DiscToTailArm(AircraftConfig config)
        => config.Propulsion is null || config.Propulsion.PropDiameterM <= 0.0 || config.Engines.Count > 0
            ? 0.0
            : PropJet.TailArm(config);

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
        bool noseLost, Vec3 noseBody, double[]? thrustScale, PropModel.PropDrive drive, double slipOverTail = 1.0,
        double wingSeparated = 0.0)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        if (config.Propulsion is null) return (totalF, totalM);
        if (config.Engines.Count == 0 && !noseLost)   // a prop torn off with the nose makes neither thrust nor drag
        {
            (Vec3 pF, Vec3 pM) = PropModel.Compute(config.Propulsion, throttle01, velocity, rates, airDensity, drive, out PropModel.Parts parts);
            // The spiral slipstream's yaw is the swirl striking the fin: only the share of the wash that still reaches it.
            if (slipOverTail < 1.0) pM -= new Vec3(0, 0, (1.0 - slipOverTail) * parts.SlipYaw);
            // The disc's IN-PLANE force (PropModel.NormalForce). Gated on the wing's separated share, like the skewed
            // wash above it: the term is real in attached flight too, but every stored rig, trim and elevator
            // calibration was solved without it, so turning it on there is a fleet re-rig and not this change. The
            // gate is a SCOPE limit, not physics. It acts at the disc, which is a long arm ahead of the CG.
            // The inflow AT THE DISC, not at the CG: in a spin at 300 deg/s the prop sits ~1.5 m ahead of the centre of
            // rotation, so omega x r adds several m/s of crossflow that the CG's velocity does not carry. The term is
            // linear in the crossflow, so using the wrong station is a real error and not a scale factor.
            Vec3 propArm = noseBody - config.Mass.CgVec();
            Vec3 nF = PropModel.NormalForce(config.Propulsion, parts.Thrust, velocity + Vec3.Cross(rates, propArm), airDensity, wingSeparated);
            if (nF.X != 0.0 || nF.Y != 0.0 || nF.Z != 0.0)
            {
                pF += nF;
                pM += Vec3.Cross(propArm, nF);
            }
            // AND THE SOLVED DISC'S IN-PLANE FORCE on the same arm. PropModel puts it in the force but leaves its
            // moment here, because only this side knows where the hub is; taken inside on a propeller-radius arm it
            // was about half the moment it should have been.
            if (parts.InPlane.Y != 0.0 || parts.InPlane.Z != 0.0) pM += Vec3.Cross(propArm, parts.InPlane);
            // Blade tips curled by a prop strike (Aircraft.PropStrike): a smaller disc at the same power.
            if (thrustScale is { Length: > 0 } && thrustScale[0] != 1.0) { pF *= thrustScale[0]; pM *= thrustScale[0]; }
            // THE SAMPLE'S MOMENT MUST BE A PURE ONE (ForceSample): every consumer sums Cross(pos - cg, force) +
            // moment, and `pM` is already the WHOLE moment about the CG - it carries the thrust line
            // (thrust x ThrustLineZ), the disc's normal force on its arm and the in-plane force on its arm. Recorded
            // at the hub with `pM` as it stands, each of those was counted a SECOND time out of Cross(propArm, pF),
            // and the prop read about 1400 N.m more nose-down and several hundred N.m more yaw than it makes: the
            // Pitts' inverted-spin pitch budget had the thrust group at -2850 N.m where it is -1400
            // (InvSpinPowerHoldProbe, 2026-11-10). THE FLIGHT MODEL WAS NEVER AFFECTED - the equations of motion take
            // (pF, pM) straight from this method and never go through a sample - but every spin budget taken through
            // LastForces was, which is the same trap as the "tailflow" record that became a phantom +965 ft.lb
            // nose-up term on 2026-11-09. Subtracting the arm moment keeps the arrow drawn at the hub where it
            // belongs and makes Cross(pos - cg, force) + moment reconstruct pM exactly.
            ForceDebug.Add(noseBody, pF, pM - Vec3.Cross(propArm, pF), "thrust");
            if (ForceDebug.Samples != null) ForceDebug.Add(noseBody, nF, Vec3.Zero, "propn");   // (already in "thrust": a breakdown only)
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
        double shaped = FlyingGame.Core.InputExpo.Apply(rescaled, axis.Expo);   // after the dead zone, never before
        return Math.Sign(clamped) * shaped * axis.LimitFor(Math.Sign(clamped));
    }

    private static double SlewTo(double current, double target, double ratePerSec, double dt)
    {
        double maxStep = Math.Abs(ratePerSec) * dt;
        double delta = target - current;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }
}

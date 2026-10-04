namespace FlyingGame.Core.DataContracts;

/// <summary>
/// Plain-data mirror of the AircraftConfig JSON schema (see docs/DATA-CONTRACTS.md). The physics reads
/// ONLY this — no aircraft-specific C#. Every field added after schemaVersion 1 must be optional with a
/// sensible default so old files keep loading.
/// </summary>
public sealed class AircraftConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Airspeed (m/s) the aircraft is spawned/reset at — a comfortable cruise for the type,
    /// well above stall (a P-51 or Sabre dropped in at glider speed stalls instantly). 0 = 22 m/s default.</summary>
    public double SpawnIasMs { get; set; }
    /// <summary>True for types with an ejection seat (the F-86); everyone else can only bail out.</summary>
    public bool EjectionSeat { get; set; }
    /// <summary>Retractable landing gear: the wheels only carry the aircraft when extended, and the extended gear
    /// adds <see cref="GearDragAreaM2"/> of flat-plate drag area (gear up in the air start).</summary>
    public bool RetractableGear { get; set; }
    public double GearDragAreaM2 { get; set; }
    /// <summary>Airframe toughness against impacts: multiplies every break speed (hard points and gear legs).
    /// 1 = light-aircraft default; a wartime fighter is ~1.6.</summary>
    public double ImpactStrength { get; set; } = 1.0;

    public MassConfig Mass { get; set; } = new();
    public List<SurfaceConfig> Surfaces { get; set; } = new();
    public Dictionary<string, AirfoilTableData> AirfoilTables { get; set; } = new();
    public ControlsConfig Controls { get; set; } = new();
    public FuselageConfig Fuselage { get; set; } = new();
    public PropulsionConfig? Propulsion { get; set; }

    /// <summary>Seaplane floats (null = landplane). See FloatHydro.</summary>
    public FloatsConfig? Floats { get; set; }

    /// <summary>Multi-engine mounts. Empty = single centerline engine using Propulsion as-is. Each
    /// mount reuses Propulsion for thrust/prop params but overrides position and rotation sign, so a
    /// twin's counter-rotating props cancel torque/P-factor in symmetric flight and an engine-out
    /// produces real asymmetric yaw at the mount's spanwise arm.</summary>
    public List<EngineMount> Engines { get; set; } = new();

    /// <summary>Time constants of separation dynamics (optional; defaults = tuned values). The
    /// oscillation damping of the deep spin lives here, not in gains/areas.</summary>
    public StallDynamicsConfig StallDynamics { get; set; } = new();

    /// <summary>
    /// Max fraction of dynamic pressure the tail loses when fully inside a fully-stalled wing's wake
    /// (tail blanketing — the mechanism that lets a spin's nose ride high). 0 disables. Optional,
    /// schema-safe default.
    /// </summary>
    public double WakeBlanketMaxLoss { get; set; } = 0.45;

    /// <summary>Fraction of blanket loss applied to VERTICAL surfaces (fin crosses the wake slab
    /// edge-on; tall fins keep ~half their q per CR-3099 yaw-damping linearity; short fins that sit
    /// fully in the wake keep 1.0). Data-scoped per aircraft.</summary>
    public double VerticalBlanketFactor { get; set; } = 0.5;
    public List<GearConfig> Gear { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
}

public sealed class MassConfig
{
    public double MassKg { get; set; }
    public double[] Cg { get; set; } = { 0, 0, 0 };
    public InertiaConfig Inertia { get; set; } = new();

    public MathTypes.Vec3 CgVec() => new(Cg[0], Cg[1], Cg[2]);
}

public sealed class InertiaConfig
{
    public double Ixx { get; set; }
    public double Iyy { get; set; }
    public double Izz { get; set; }
    public double Ixz { get; set; }
}

public sealed class SurfaceConfig
{
    public string Id { get; set; } = "";
    public List<StripConfig> Strips { get; set; } = new();

    /// <summary>
    /// Oswald span-efficiency factor for this surface's induced drag (Cd_i = Cl²/(π·AR·e)).
    /// Airfoil tables carry PROFILE drag only; induced drag is computed per strip from this.
    /// </summary>
    public double OswaldE { get; set; } = 0.85;

    /// <summary>
    /// Height of this surface's ROOT above the fuselage axis (m): positive = high wing (sits on top of
    /// the body), negative = low wing (hangs under it), 0 = mid wing / not modeled. Drives the
    /// wing-body crossflow dihedral effect: in sideslip the flow wraps over/under the fuselage, giving
    /// upwash at the windward root of a high wing (extra dihedral effect) and downwash for a low wing
    /// (anhedral effect). Needs <see cref="CrossflowConfig.BodyRadiusM"/> &gt; 0 to act.
    /// </summary>
    public double HeightAboveBodyAxisM { get; set; }
}

public sealed class StripConfig
{
    public double[] Pos { get; set; } = { 0, 0, 0 };
    public double Chord { get; set; }
    public double Area { get; set; }
    public double IncidenceRad { get; set; }
    public double DihedralRad { get; set; }
    public string Airfoil { get; set; } = "";
    public StripControlConfig? Control { get; set; }

    /// <summary>Quarter-chord SWEEP angle (rad). Swept strips see reduced chordwise velocity
    /// (cos-sweep): lift/stall scale with cos(sweep), and effective AoA uses the normal-flow
    /// component. Zero for straight wings.</summary>
    public double SweepRad { get; set; }

    /// <summary>Trailing-edge FLAP on this strip: deflection shifts zero-lift AoA (camber) and adds
    /// drag; part of the "flap" control group. null = no flap.</summary>
    public FlapConfig? Flap { get; set; }

    /// <summary>Leading-edge SLAT on this strip: when deployed, extends the stall AoA (delays
    /// separation) and adds a lift increment. null = no slat.</summary>
    public SlatConfig? Slat { get; set; }

    public MathTypes.Vec3 PosVec() => new(Pos[0], Pos[1], Pos[2]);
}

public sealed class StripControlConfig
{
    public string Surface { get; set; } = "";
    public double Gain { get; set; }
}

public sealed class FlapConfig
{
    public double MaxDeltaAlphaRad { get; set; } = 0.20;  // zero-lift shift at full flap (camber)
    public double MaxCd { get; set; } = 0.09;             // added drag at full flap
    public double MaxClMax { get; set; } = 0.5;           // stall-alpha-equivalent lift boost
}

public sealed class SlatConfig
{
    public double StallExtensionRad { get; set; } = 0.17; // ~10 deg extra stall AoA when deployed
    public double ClIncrement { get; set; } = 0.15;
}

public sealed class AirfoilTableData
{
    public double[] AlphaRad { get; set; } = System.Array.Empty<double>();
    public double[] Cl { get; set; } = System.Array.Empty<double>();
    public double[] Cd { get; set; } = System.Array.Empty<double>();
    public double[] Cm { get; set; } = System.Array.Empty<double>();
}

public sealed class ControlsConfig
{
    public ControlAxisConfig Aileron { get; set; } = new();
    public ControlAxisConfig Elevator { get; set; } = new();
    public ControlAxisConfig Rudder { get; set; } = new();
    public SpoilerConfig Spoiler { get; set; } = new();
}

public sealed class ControlAxisConfig
{
    /// <summary>Plain-flap saturation of this control's deflection (deg): effective = s·tanh(δ/s). Hinged elevators separate
    /// past ~15–20° (default 18); aerobatic types with big balanced elevators keep authority (larger); an all-moving
    /// stabilator is tuned so full aft holds the wing just past its stall.</summary>
    public double SaturationDeg { get; set; } = 120.0;   // ≈ none unless the stall calibration sets it (StallAuthorityTests)

    public double MaxDeflRad { get; set; }
    public double RateRadPerSec { get; set; } = 1000; // effectively unlimited unless configured
    public double Expo { get; set; }
    public double DeadZone { get; set; }

    /// <summary>REVERSIBLE control (owner 2026-10-02): cables/pushrods, so when the pilot lets go the surface is free and
    /// trails to where its hinge moment balances — air load vs. centering spring vs. trim. false = irreversible
    /// (hydraulic / servo): released, it goes to its trim position like before.</summary>
    public bool Reversible { get; set; }
    /// <summary>Hinge-moment slope with the flow angle over the surface (per rad, negative: the flow pushes the trailing
    /// edge to trail with it). Plain unbalanced surface ≈ -0.25; horn/overhang-balanced ≈ -0.10.</summary>
    public double HingeChAlpha { get; set; } = -0.20;
    /// <summary>Hinge-moment slope with deflection (per rad, negative = restoring). Plain ≈ -0.55; balanced ≈ -0.30;
    /// a stabilator's anti-servo tab gives its restoring moment.</summary>
    public double HingeChDelta { get; set; } = -0.45;
    /// <summary>CENTERING SPRING strength, as the airspeed (kt) at which the air's own restoring moment equals the
    /// spring's: 0 = no spring; 30 = light (air dominates in flight); 80 = strong (the surface stays near the spring's
    /// neutral at normal speeds). Also what centres the surface on the ground and at low speed.</summary>
    public double CenteringSpringKt { get; set; }
    /// <summary>How the trim works when this axis is free: "tab" (trim tab — zero air load at the trimmed deflection)
    /// or "spring" (the trim moves the spring's neutral, e.g. a bungee / spring-cartridge trim).</summary>
    public string TrimType { get; set; } = "tab";
    /// <summary>Time constant (s) of the free surface settling (surface + linkage inertia and friction).</summary>
    public double FreeTauS { get; set; } = 0.12;
}

public sealed class SpoilerConfig
{
    public double MaxDeflRad { get; set; }
    public bool DragOnly { get; set; } = true;
    public string Axis { get; set; } = "throttleLever";
    public string AxisMap { get; set; } = "aftOnly";
    /// <summary>EFFECTIVE flat-plate area of the deployed brakes (m²): panel frontal area plus the induced-drag
    /// penalty of the spoiled lift. 0 = 2 % of wing area. 2-33: 1.2 m² (0.45 m² of panel alone felt like nothing;
    /// whole-wing plate dove vertically at 27 kt). Drag = q · area · Cd 1.2 · fraction.</summary>
    public double PlateAreaM2 { get; set; }
}

public sealed class FuselageConfig
{
    public double Cd0Area { get; set; }
    public double SideForceArea { get; set; }
    public DampingConfig Damping { get; set; } = new();

    /// <summary>
    /// Slender-body crossflow drag (optional; zero areas disable). At high alpha/beta the fuselage is
    /// broadside to the flow: plan-view area produces normal force (arrests spin flattening), side-view
    /// area produces yaw-restoring side force when the fin is stalled/blanketed. Forces act at the
    /// respective area centers (x vs CG), giving the moments a point-model fuselage cannot.
    /// </summary>
    public CrossflowConfig Crossflow { get; set; } = new();
}

public sealed class EngineMount
{
    public double[] Pos { get; set; } = { 0, 0, 0 };  // engine/prop-plane position vs CG (m)
    public int RotationSign { get; set; } = 1;        // +1 right-hand, -1 left-hand (counter-rot)
    public double ThrottleScale { get; set; } = 1.0;  // 1 running, 0 failed/feathered
    /// <summary>Failed AND feathered: the blades edge-on — no thrust and none of the windmilling drag (a failed engine
    /// that isn't feathered windmills, and that drag is the multi-engine pilot's enemy).</summary>
    public bool Feathered { get; set; }

    public MathTypes.Vec3 PosVec() => new(Pos[0], Pos[1], Pos[2]);
}

public sealed class PropulsionConfig
{
    public double MaxPowerW { get; set; }
    public double PropDiameterM { get; set; }
    public double IdleRpm { get; set; } = 700;
    public double MaxRpm { get; set; } = 2700;
    public double PropInertia { get; set; } = 1.7;      // prop + crank about x, kg m^2
    public int RotationSign { get; set; } = 1;          // +1 = right-hand (clockwise from behind)
    public double Efficiency { get; set; } = 0.75;
    public double ThrustLineZ { get; set; } = 0.0;      // + below CG (z down)
    public double PFactorK { get; set; } = 0.35;        // lateral thrust offset fraction of radius per sin(alpha)
    public double SlipstreamK { get; set; } = 0.12;     // spiral-slipstream yaw moment coefficient
    /// <summary>Constant-speed propeller: the governor holds <see cref="GovernedRpm"/> whenever there is enough
    /// power to govern, so the throttle changes manifold pressure (loudness), not rpm (pitch). Fixed-pitch:
    /// rpm follows throttle and airspeed.</summary>
    public bool ConstantSpeed { get; set; }
    public double GovernedRpm { get; set; } = 2400;
    /// <summary>Drag of the idling / windmilling propeller as a coefficient on its DISC area (owner 2026-10-03: the 172
    /// glided 14:1 at idle — the POH says 9:1 — and floated forever). A fine-pitch prop at idle is driven by the air and
    /// is a big drag: C_D ≈ 0.15–0.25 on disc area. Fades out as the throttle comes up (gone by 20 %).
    /// Calibrated at 0.15 (172 → 8.9:1, book 9:1; Cub 8:1; SR22 ≈ 10:1, book 8.8:1). Fast types with big props set their own
    /// (the drag of a windmilling blade falls off at high advance ratio): Extra 0.08 (7:1); P-51 0.05 (8:1 at 120 kt).</summary>
    public double IdleDragCd { get => _idleDragCd ?? 0.15; set => _idleDragCd = value; }
    private double? _idleDragCd;
}

public sealed class StallDynamicsConfig
{
    public double StripSepTau { get; set; } = 0.15;      // s, per-strip separation growth
    public double StripReattachTau { get; set; } = 0.6;  // s, per-strip reattachment
    public double WakeGrowTau { get; set; } = 0.25;      // s, whole-wing wake development
    public double WakeDecayTau { get; set; } = 1.0;      // s, wake washout
    public double WakeSpreadDeg { get; set; } = 6.0;     // wake band edge softness

    /// <summary>Proposal 1: downwash transport lag (Cm-alphadot). Off = instantaneous downwash.</summary>
    public bool DownwashLagEnabled { get; set; }

    /// <summary>Proposal 3: per-strip unsteady force lag (~3 chords/V). Off = quasi-steady.</summary>
    public bool UnsteadyLagEnabled { get; set; }
}

public sealed class CrossflowConfig
{
    public double PlanArea { get; set; }
    public double PlanCenterX { get; set; }
    public double SideArea { get; set; }
    public double SideCenterX { get; set; }
    public double Cd { get; set; } = 1.2; // circular-cylinder crossflow drag coefficient

    /// <summary>Proposal 2: fuselage length for DISTRIBUTED crossflow (5 stations -> real Cm_q/N_r
    /// damping at spin attitudes). 0 = legacy single-point.</summary>
    public double LengthM { get; set; }

    /// <summary>
    /// Fuselage radius at the wing station (m) for the wing-body crossflow dihedral effect (2D cylinder
    /// crossflow around the body: upwash on the windward side above the axis, downwash below). 0 = off.
    /// Pair with <see cref="SurfaceConfig.HeightAboveBodyAxisM"/> on the wing surfaces.
    /// </summary>
    public double BodyRadiusM { get; set; }

    /// <summary>
    /// Fuselage cross-SECTION camber in crossflow (owner/CFI observation on the Extra 300: a rounded
    /// turtle deck over a flat, angular belly is a cambered "airfoil" to sideslip flow, so ANY sideslip
    /// lifts the aft fuselage toward the rounded side and pitches the nose DOWN — the sideslip↔pitch
    /// coupling that lets it tumble). Body-up force = ½ρ·v_local²·SectionCamberArea·SectionCamberCl at
    /// SectionCamberCenterX (v_local = lateral velocity at that station incl. yaw rate). Even in β, so
    /// it works either way and in any attitude. 0 = off.
    /// </summary>
    public double SectionCamberCl { get; set; }
    public double SectionCamberArea { get; set; }
    public double SectionCamberCenterX { get; set; }
}

public sealed class DampingConfig
{
    public double P { get; set; }
    public double Q { get; set; }
    public double R { get; set; }
}

/// <summary>Twin straight floats (EDO 2000 class). Body frame: x forward, z DOWN from the CG.</summary>
public sealed class FloatsConfig
{
    public double LengthM { get; set; } = 4.95;
    public double BeamM { get; set; } = 0.66;
    public double DepthM { get; set; } = 0.55;           // keel to deck at the step
    public double DeadriseDeg { get; set; } = 20;
    public double StepFraction { get; set; } = 0.55;     // step position from the bow, fraction of length
    public double SpreadM { get; set; } = 2.6;           // centre-to-centre
    public double BowX { get; set; } = 2.6;              // bow tip x vs CG
    public double KeelZ { get; set; } = 1.55;            // keel at the step, z down vs CG
    public double ForebodyKeelDeg { get; set; } = 2.5;   // keel rises toward the bow
    public double AfterbodyKeelDeg { get; set; } = 4.0;  // keel rises toward the stern behind the step
    public double WaterRudderAreaM2 { get; set; } = 0.05;
    public double WaterRudderMaxRad { get; set; } = 0.6;
    /// <summary>Shape taper (owner 2026-10-02: displacement must be right). The forebody keeps full beam/depth for this
    /// fraction of its length from the step, then narrows to the bow; the afterbody narrows from the step to the stern.</summary>
    public double ForebodyFullFraction { get; set; } = 0.4;
    public double BowBeamFraction { get; set; } = 0.3;
    public double BowDepthFraction { get; set; } = 0.55;
    public double SternBeamFraction { get; set; } = 0.85;  // afterbody keeps most of its beam (reserve buoyancy aft for the hump)
    public double SternDepthFraction { get; set; } = 0.75;
    /// <summary>2 = twin floats (SpreadM apart); 1 = a flying-boat HULL on the centreline (owner 2026-10-02: H-4).</summary>
    public int Count { get; set; } = 2;
    /// <summary>Flying boats: the small wingtip floats (SpreadM = tip-to-tip spacing). They touch only when the hull heels.</summary>
    public FloatsConfig? TipFloats { get; set; }
}

public sealed class GearConfig
{
    public double[] Pos { get; set; } = { 0, 0, 0 };  // wheel contact point vs CG (m); +z is DOWN (below CG)
    public double SpringN { get; set; }               // oleo strut stiffness (N/m compression)
    public double DampNs { get; set; }                // oil damping (N per m/s compression rate)
    public bool Brake { get; set; }                   // wheel has a brake
    public bool IsTailwheel { get; set; }             // tailwheel (small, low cornering) vs main
    public bool IsSteerable { get; set; }             // steered by rudder (nosewheel / steerable tailwheel)
    public double MaxSteerRad { get; set; } = 0.5;    // steering authority when steerable
    public double TireMu { get; set; } = 0.8;         // dry tarmac friction (limits tire force)
    public double CorneringStiffnessN { get; set; } = 6000; // side force per rad slip (before mu limit)
    public double RollResistN { get; set; } = 40;     // rolling resistance at full load
    public string GearType { get; set; } = "";        // bungee|spring-steel|spring-aluminum|oleo (character/label)
    public double TireRadiusM { get; set; }           // visual tire size (0 = auto from fuselage length); 0.445 = 35" bushwheel
    public double BreakSinkMs { get; set; }           // first-contact sink rate that tears this leg off (0 = LandingGear.DefaultBreakSinkMs)
    // Tailwheel (owner 2026-09-12): the wheel CASTORS about its pivot; springs to the rudder pull it toward the
    // commanded angle but always give; past BreakoutRad it swivels free. 0 = LandingGear defaults.
    public double SteerSpringNmPerRad { get; set; }   // steering-spring stiffness (soft: ~60 N·m/rad on a Cub)
    public double SteerDampNms { get; set; }          // castor damping (N·m per rad/s)
    public double CastorTrailM { get; set; }          // contact patch behind the castor pivot (m)
    public double BreakoutRad { get; set; }           // castor angle at which the steering UNLOCKS (free swivel until re-engaged)

    public MathTypes.Vec3 PosVec() => new(Pos[0], Pos[1], Pos[2]);
}

public sealed class LimitsConfig
{
    public double VneMs { get; set; } = 1000;
    /// <summary>LIMIT load factors (g): the loads the structure must carry without permanent deformation.</summary>
    public double GMax { get; set; } = 10;
    public double GMin { get; set; } = -10;

    /// <summary>ULTIMATE load = limit × this factor (FAR 23.303 / 25.303: 1.5). Between limit and ultimate
    /// the airframe deforms and groans; beyond ultimate the wings separate. Optional, schema-safe default.</summary>
    public double UltimateFactor { get; set; } = 1.5;
}

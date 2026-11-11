using System;

namespace FlyingGame.Core.DataContracts;

/// <summary>
/// Plain-data mirror of the AircraftConfig JSON schema (see docs/DATA-CONTRACTS.md). The physics reads
/// ONLY this — no aircraft-specific C#. Every field added after schemaVersion 1 must be optional with a
/// sensible default so old files keep loading.
/// </summary>
public sealed class AircraftConfig
{

    /// <summary>
    /// THE ENGINE'S CYLINDER COUNT AND COMPRESSION RATIO, from whichever place authored them. They describe the engine,
    /// not the exhaust, but the exhaust asked first (<see cref="SmokeConfig.Cylinders"/> drives the pipe's pulse rate),
    /// so rather than author the same two numbers twice and let them drift, propulsion's own values win when given and
    /// the smoke pipe's are the fallback. One place decides, so the pulse you HEAR and the compression you FEEL are the
    /// same engine. Four and 8.7 when nothing says otherwise — a small four-cylinder Lycoming or Continental, which is
    /// most of this fleet.
    /// </summary>
    public int EngineCylinders =>
        Propulsion is { Cylinders: > 0 } p ? p.Cylinders : Smoke is { Cylinders: > 0 } sm ? sm.Cylinders : 4;

    public double EngineCompressionRatio =>
        Propulsion is { CompressionRatio: > 1.0 } p ? p.CompressionRatio
        : Smoke is { CompressionRatio: > 1.0 } sm ? sm.CompressionRatio : 8.7;
    /// <summary>
    /// BUSHWHEELS (owner 2026-10-16): any aeroplane can be put on big soft tyres, which is what lets it skim water
    /// and land on a gravel bar. Set from the session when the option is chosen; not part of the type's own data.
    ///
    /// Since 2026-11-04 (Ty: "proper tundra tires") the option FITS A REAL TYRE from <see cref="TireCatalog"/> to each
    /// main wheel — the leg's own <see cref="GearConfig.TundraTire"/>, or the smallest Alaskan Bushwheel rated for the
    /// leg's static load — at that tyre's own pressure, on the same axle (so the contact point drops by the
    /// difference in radius). It used to double the radius and the rotating mass and halve the pressure. Setting it
    /// back to false puts the stock tyre back; see <see cref="TireFit"/>.
    /// </summary>
    public bool Bushwheels
    {
        get => _bushwheels;
        set { if (value != _bushwheels) { _bushwheels = value; TireFit.Apply(this, value); } }
    }
    private bool _bushwheels;

    /// <summary>
    /// WHEEL-SKIS (owner 2026-10-16; hinged since Ty 2026-11-05): a ski on every wheeled leg, hung from the axle on links
    /// and raised or lowered by the cockpit's WHEELS / SKIS selector (<see cref="SkiGear"/>, <see cref="SkiSelector"/>).
    /// Lowered, the tyre sits on top of the ski and the ski is what touches the ground — on snow it runs almost freely,
    /// holds sideways only by its edge and the brakes do nothing; on pavement it SCRAPES. Raised, the skis swing forward
    /// and up, the tyres meet the ground and the aeroplane rolls on its wheels. Either way the skis' weight and drag are
    /// on. Session state, not the type's own data; any aeroplane with a wheel can have it.
    /// </summary>
    public bool Skis { get; set; }

    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Airspeed (m/s) the aircraft is spawned/reset at — a comfortable cruise for the type,
    /// well above stall (a P-51 or Sabre dropped in at glider speed stalls instantly). 0 = 22 m/s default.</summary>
    public double SpawnIasMs { get; set; }
    /// <summary>True for types with an ejection seat (the F-86); everyone else can only bail out.</summary>
    public bool EjectionSeat { get; set; }
    /// <summary>
    /// A whole-aircraft BALLISTIC RECOVERY PARACHUTE (owner 2026-10-16), the Cirrus CAPS kind. Null on types
    /// that have not got one — which is most of them — and where it IS fitted the pilot does not bail out at
    /// all: the aeroplane comes down under the canopy with everybody still in it.
    /// </summary>
    public BrsConfig? Brs { get; set; }
    /// <summary>Retractable landing gear: the wheels only carry the aircraft when extended, and the extended gear
    /// adds <see cref="GearDragAreaM2"/> of flat-plate drag area (gear up in the air start).</summary>
    public bool RetractableGear { get; set; }
    public double GearDragAreaM2 { get; set; }
    /// <summary>Airframe toughness against impacts: multiplies every break speed (hard points and gear legs).
    /// 1 = light-aircraft default; a wartime fighter is ~1.6.</summary>
    public double ImpactStrength { get; set; } = 1.0;
    /// <summary>How the FUSELAGE is built, which decides how it fails in a crash (Ty 2026-11-04, <see cref="FuselageStructure"/>):
    /// "steelTube" (welded 4130 truss under fabric or light panels — Pitts, Extra, Cubs, Great Lakes, Decathlon …: it bends
    /// and crushes and never breaks apart, except that the engine can leave at the firewall), "aluminumMonocoque"
    /// (riveted skin and stringers: crushes progressively, then breaks), "composite" (glass/carbon shell: little crush,
    /// then it cracks), "wood" (spruce/plywood truss or shell: splinters after a little crush). Empty = untagged, which
    /// keeps the old break-at-first-overload rule exactly.</summary>
    public string StructureType { get; set; } = "";
    /// <summary>Where <see cref="StructureType"/> came from (type certificate, maintenance manual, builder's notes).</summary>
    public string StructureTypeSource { get; set; } = "";
    /// <summary>How the WING is built, which decides how its panels bend and how far before they let go (Ty 2026-11-05,
    /// <see cref="FlyingGame.Core.MemberMaterials"/>): "wood" (spruce spars, fabric), "aluminumFabric" (aluminium spars,
    /// fabric), "aluminum" (stressed aluminium skin), "composite". Empty = what the fuselage implies (steel tube or wood →
    /// wood, composite → composite, otherwise aluminium skin).</summary>
    public string WingSpar { get; set; } = "";
    public string WingSparSource { get; set; } = "";

    public MassConfig Mass { get; set; } = new();
    /// <summary>Seats, tanks and baggage, and the weight-and-balance envelope. Optional; absent means the authored
    /// <see cref="MassConfig"/> is the only loading there is.</summary>
    public LoadingConfig Loading { get; set; } = new();
    public List<SurfaceConfig> Surfaces { get; set; } = new();
    public Dictionary<string, AirfoilTableData> AirfoilTables { get; set; } = new();
    public ControlsConfig Controls { get; set; } = new();
    /// <summary>How the airframe is RIGGED: the aileron neutral offset and the fin offset that make it fly straight hands
    /// off at the rig condition (owner 2026-11-02: level at 75 % power). Fixed geometry, solved by RigSolver and written
    /// here by RigTool; absent = an unrigged, symmetric airframe.</summary>
    public RigConfig Rig { get; set; } = new();
    public FuselageConfig Fuselage { get; set; } = new();
    /// <summary>How the airframe is built, for what a crash in water does to it (Core/Wreck): skin covering and
    /// cockpit openings. Optional; absent means a riveted aluminium skin and an enclosed cabin with a windshield and
    /// two doors.</summary>
    public ConstructionConfig Construction { get; set; } = new();
    public PropulsionConfig? Propulsion { get; set; }

    /// <summary>Airshow smoke exhaust (optional; owner 2026-10-12, the Extra: "the exhaust location … 4 ft from the nose on
    /// the belly pointing on a 30 degree line downward angle from the longitudinal axis"). Null = the generic stack rule.</summary>
    public SmokeConfig? Smoke { get; set; }

    /// <summary>Separated-flow wing terms the strip model can't produce (spin research 2026-10-13). Null/empty = none.</summary>
    public SpinAeroConfig? SpinAero { get; set; }

    /// <summary>Biplane cellule interference (<see cref="FlyingGame.Core.Aero.BiplaneCellule"/>). Absent = off, which
    /// is the default for every type: switching it on is a per-type decision, because a section table that was fitted
    /// to make the aeroplane stall at the right speed is already carrying this effect implicitly and would count it
    /// twice. The cellule is MEASURED from the strips whether this is set or not, so the numbers are always available
    /// to a probe or a test.</summary>
    public CelluleConfig? Cellule { get; set; }

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

    public List<GearConfig> Gear { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();

    /// <summary>What this aeroplane's own instruments read (see <see cref="InstrumentsConfig"/>). Until 2026-11-10 the
    /// block existed in the JSON but nothing in C# read it: the modelled cockpit faces are drawn from it by
    /// tools/own_models/instruments/build.py and its numbers were copied by hand into
    /// <c>FlyingGame.Core.Instruments.InstrumentLibrary</c>. The fleet-wide ASI UNIT rule (Ty 2026-11-10, "by era and
    /// category") needs it at run time on every type, so it is a loaded contract now.</summary>
    public InstrumentsConfig? Instruments { get; set; }

    /// <summary>Set on a wing flown by weight shift instead of control surfaces (<see cref="WeightShiftConfig"/>);
    /// null on everything with a stick connected to something.</summary>
    public WeightShiftConfig? WeightShift { get; set; }

    /// <summary>Where each wing's outboard panel joins the structure that holds it (<see cref="FlyingGame.Core.WingStructure"/>):
    /// |y| in metres, keyed by surface id ("wing", "wing-upper", "wing-lower"). A biplane's is the interplane strut, a
    /// strut-braced wing's the lift-strut attach, measured from the type's own model. Absent = the aileron's inboard end
    /// (the cantilever rule).</summary>
    public Dictionary<string, double>? WingBreakStationY { get; set; }

    /// <summary>Where <see cref="WingBreakStationY"/> comes from: the document, figure and inch figure (JSON has no comments).</summary>
    public string WingBreakStationSource { get; set; } = "";

    /// <summary>A strut-braced wing's lift-strut FUSELAGE fitting, body frame [x, y, z] m (+x forward, +y right, +z down;
    /// the right-hand fitting — the left is its mirror), converted from the type's station / butt line / height datum
    /// as described in <see cref="LiftStrutFuselageAttachSource"/>. Reference geometry: nothing in the flight model reads it.</summary>
    public double[]? LiftStrutFuselageAttach { get; set; }
    public string LiftStrutFuselageAttachSource { get; set; } = "";

    /// <summary>How the wing is held up outboard of the cabin: "strut" (lift struts) or "" (a cantilever, or a biplane,
    /// which is braced by its cellule anyway). A braced spar runs on through its station at the section its inboard bay
    /// needs (<see cref="FlyingGame.Core.WingStructure.EnsureStrength"/>).</summary>
    public string WingBracing { get; set; } = "";

    /// <summary>The rigid slave strut joining a biplane's upper and lower ailerons on each side (null = none).</summary>
    public SlaveStrutConfig? SlaveStrut { get; set; }

    // (The 2026-10-30 `LoadingConfig? Loading` stood here, from the load-sheet implementation that collided with
    // this one. Only one may own AircraftConfig.Loading. See the note in StartMenu.cs and e4755c9.)
}

/// <summary>
/// One place something heavy sits: a seat, a fuel tank or bag, a baggage locker. Positions are in the airframe's
/// own BODY REFERENCE FRAME — the same frame as <see cref="GearConfig.Pos"/> and <see cref="StripConfig.Pos"/>,
/// +x forward, +y right, +z DOWN — not relative to the CG. <see cref="MassConfig.Cg"/> is the CG's position in that
/// same frame, which is why the loading model can move it.
/// </summary>
public sealed class StationConfig
{
    /// <summary>Stable key the saved loadout refers to. Never renamed once shipped.</summary>
    public string Id { get; set; } = "";
    /// <summary>What the loading page calls it: "Pilot", "Left wing", "Aft baggage".</summary>
    public string Label { get; set; } = "";
    /// <summary>Where the mass acts, body reference frame, metres.</summary>
    public double[] Station { get; set; } = { 0, 0, 0 };
    /// <summary>The most that may be put here, kg. A placard, not a physics limit.</summary>
    public double MaxKg { get; set; }
    /// <summary>How much of this station is ALREADY INCLUDED in <see cref="MassConfig.MassKg"/> and
    /// <see cref="MassConfig.Cg"/>. This is what makes the loading model exact: load the aeroplane to its reference
    /// and it flies with precisely the authored mass, CG and inertia, so every tuned book number is untouched. Only
    /// a change away from the reference moves anything.</summary>
    public double ReferenceKg { get; set; }
    /// <summary>Fuel only: the order tanks drain in, lowest first. Equal numbers drain together.</summary>
    public int BurnOrder { get; set; }

    /// <summary>SEATS only: the pilot's EYE in that seat — the view point (owner 2026-10-31, authored by Codex), body
    /// reference frame, +x forward, +y right, +z DOWN, so an eye above the reference has a NEGATIVE z. It is on the
    /// seat rather than in a separate table because it is the same seat: one authored entry gives the loading page
    /// its mass station and the cockpit view its view point, and they can never drift apart. Null or absent means the
    /// seat has no authored view point yet and the camera falls back to its own estimate.</summary>
    public double[]? Eye { get; set; }

    public bool HasEye => Eye is { Length: 3 };

    /// <summary>SEATS only: where the shared WHEELS / SKIS selector (<see cref="FlyingGame.Core.SkiSelectorPart"/>) is mounted
    /// for this seat when wheel-skis are fitted, body frame like <see cref="Eye"/>: the centre of its placard plate. An
    /// ANCHOR only: the part itself is the one shared part, drawn the same in every aeroplane. Absent on every seat: the
    /// pilot's seat gets it at <see cref="FlyingGame.Core.SkiSelectorPart.DefaultOffsetFromEye"/>.</summary>
    public double[]? SkiSelector { get; set; }

    public MathTypes.Vec3 StationVec() => new(Station[0], Station[1], Station[2]);

    /// <summary>The authored eye point, or the mass station raised to roughly head height when none is authored —
    /// a seated pilot's eye is about half a metre above the seat pan he is weighed at.</summary>
    public MathTypes.Vec3 EyeVec() => HasEye ? new(Eye![0], Eye[1], Eye[2]) : new(Station[0], Station[1], Station[2] - 0.5);
}

/// <summary>
/// What can be put IN the aeroplane, and where that leaves the weight and balance (owner 2026-10-31: "build a
/// configuration on the launch page for loading the airplane with fuel bags and people then model the resulting
/// weight and cg while they fly").
///
/// Entirely optional. An aeroplane with no loading block has no stations, so <see cref="FlyingGame.Core.AircraftLoad"/>
/// reports exactly the authored <see cref="MassConfig"/> and nothing about its handling changes.
///
/// Seats carry the pilot's EYE position as well as the occupant's mass station, because they are the same seat: one
/// authored seat serves both the loading page and the cockpit view.
/// </summary>
public sealed class LoadingConfig
{
    /// <summary>Airframe, oil, everything that never comes out. Optional: when 0 it is derived as
    /// <see cref="MassConfig.MassKg"/> minus every station's <see cref="StationConfig.ReferenceKg"/>.</summary>
    public double EmptyMassKg { get; set; }
    /// <summary>Maximum certificated take-off mass. 0 = no placard, and the page will not complain about weight.</summary>
    public double MaxGrossMassKg { get; set; }
    /// <summary>The same placard as several configs author it. Read by the landing page's weight-and-balance plot only,
    /// when <see cref="MaxGrossMassKg"/> is 0 (<see cref="FlyingGame.Core.AircraftLoad.PlacardGrossKg"/>); nothing in the
    /// sim reads it. (The "envelope" beside it in some configs is the 2026-10-30 lane's percent-MAC copy of the same
    /// certificate limits; it is not read. <see cref="CgLimits"/> is.)</summary>
    public double MaxTakeoffMassKg { get; set; }
    /// <summary>CG envelope in the body reference frame's x, metres. The frame has +x FORWARD, so the forward limit
    /// is the LARGER number and the aft limit the smaller one — a nose-heavy aeroplane has a bigger x than a
    /// tail-heavy one. Equal values, or an aft limit above the forward one, mean no published envelope and the page
    /// will not complain about balance.</summary>
    public double CgForwardLimitM { get; set; }
    public double CgAftLimitM { get; set; }
    /// <summary>A CG envelope whose limits change with weight, as a type certificate writes it: one row per weight the
    /// certificate quotes, body x metres (+forward). Between rows the limits vary in a straight line; below the lightest
    /// row and above the heaviest they hold that row's values (a certificate's "at 1475 lb or less"). Empty = use
    /// <see cref="CgForwardLimitM"/> / <see cref="CgAftLimitM"/>, constant with weight. Only from a published source,
    /// cited in the config beside it: no envelope is better than an invented one (Ty 2026-11-06).</summary>
    public List<CgLimitConfig> CgLimits { get; set; } = new();
    /// <summary>Body x (m) of the type's own weight-and-balance datum, so the plot can label the CG the way the type's
    /// handbook does (inches / mm aft of datum). 0 with <see cref="HasCgDatum"/> false = no datum located, and the plot
    /// measures from the body reference instead and says so.</summary>
    public double CgDatumM { get; set; }
    public bool HasCgDatum { get; set; }

    public List<StationConfig> Seats { get; set; } = new();
    /// <summary>Fuel: wing tanks, a belly tank, or a bladder bag strapped in the cabin — a bag is just a tank with a
    /// cabin station. All of these burn off, in <see cref="StationConfig.BurnOrder"/>.</summary>
    public List<StationConfig> Tanks { get; set; } = new();
    /// <summary>Dead weight that does not burn: baggage, freight, a dog.</summary>
    public List<StationConfig> Baggage { get; set; } = new();
}

/// <summary>One weight's CG limits (<see cref="LoadingConfig.CgLimits"/>). Body x, metres, +forward, so ForwardM &gt; AftM.</summary>
public sealed class CgLimitConfig
{
    public double MassKg { get; set; }
    public double ForwardM { get; set; }
    public double AftM { get; set; }
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

    /// <summary>REYNOLDS FAMILY (optional; Grob G 103, 2026-11-01). Tables that are the same section measured (or run
    /// through XFOIL) at different chord Reynolds numbers share a <see cref="ReynoldsFamily"/> name and each carries its
    /// own <see cref="ReynoldsNumber"/>. A strip whose table belongs to a family is flown on the family, interpolated in
    /// log Re at the strip's own Reynolds number, V c / nu -- so a sailplane's profile drag falls as it flies faster and
    /// its tip strips (short chord) have the drag and CLmax of their own Re, as the real wing does. Empty = a single
    /// table, which is every type before this one.</summary>
    public string ReynoldsFamily { get; set; } = "";
    public double ReynoldsNumber { get; set; }
}

/// <summary>
/// The airframe's RIGGING (owner 2026-11-02: "trim/rig all of the airplanes for hands off level 75% power"). These are
/// angles the aeroplane is built with, like a real one's — not a control input and not a controller:
///  - <see cref="AileronRad"/>: where both ailerons sit, relative to the stick, with the stick centred. Same sign as the
///    aileron deflection (positive = the deflection a positive stick commands). It is what bending the aileron's
///    fixed tab, or re-rigging the aileron neutral, does on the real aeroplane.
///  - <see cref="FinOffsetRad"/>: the fin's (and the rudder hinged to it) incidence to the centreline. Positive =
///    leading edge LEFT, trailing edge RIGHT, which pushes the tail left and yaws the nose RIGHT — the offset a
///    clockwise-from-behind propeller needs.
/// Both are solved together with the elevator trim by RigSolver for steady, straight, level flight at
/// <see cref="ThrottleFraction"/> of rated power at <see cref="AltitudeM"/> ISA, with the stick and rudder centred,
/// zero sideslip and zero rates; RigTool writes the result here. The remaining fields record that condition and the
/// solution so a reader can see what the rig was set for; the sim reads only the two angles.
/// </summary>
public sealed class RigConfig
{
    public double AileronRad { get; set; }
    public double FinOffsetRad { get; set; }

    /// <summary>Shaft power fraction the rig is set for (PropModel: power = MaxPowerW × throttle; a jet's thrust).</summary>
    public double ThrottleFraction { get; set; }
    /// <summary>ISA pressure altitude of the rig condition, m.</summary>
    public double AltitudeM { get; set; }
    /// <summary>The solved condition: true airspeed (m/s), angle of attack, elevator, and the bank that balances the
    /// fin's small side force with zero sideslip (all rad). The elevator here is the hands-off trim at that condition.</summary>
    public double TasMs { get; set; }
    public double AlphaRad { get; set; }
    public double ElevatorTrimRad { get; set; }
    public double BankRad { get; set; }
}

public sealed class ControlsConfig
{
    public ControlAxisConfig Aileron { get; set; } = new();
    public ControlAxisConfig Elevator { get; set; } = new();
    public ControlAxisConfig Rudder { get; set; } = new();
    public SpoilerConfig Spoiler { get; set; } = new();

    /// <summary>The axis a strip's <see cref="StripControlConfig.Surface"/> name belongs to, or null if it names
    /// nothing (a flap or spoiler row, which have their own models). Mirrors
    /// <c>ControlDeflections.GetDeflection</c>'s names.</summary>
    public ControlAxisConfig? AxisFor(string? surface) => surface switch
    {
        "aileron" => Aileron,
        "elevator" => Elevator,
        "rudder" => Rudder,
        _ => null,
    };
}

public sealed class ControlAxisConfig
{
    /// <summary>Plain-flap saturation of this control's deflection (deg): effective = s·tanh(δ/s). Hinged elevators separate
    /// past ~15–20° (default 18); aerobatic types with big balanced elevators keep authority (larger); an all-moving
    /// stabilator is tuned so full aft holds the wing just past its stall.</summary>

    public double MaxDeflRad { get; set; }

    /// <summary>Travel limit in the NEGATIVE-deflection direction (rad) when the surface is not symmetric; 0 = symmetric
    /// (<see cref="MaxDeflRad"/> both ways). Sim sign convention: positive elevator is trailing-edge DOWN, so this is the
    /// UP travel, and almost every certificated elevator has more of it than down travel — the J-3's TCDS (FAA A-691
    /// Item 4) gives 34 deg up / 29 deg down. Ailerons and rudders are normally symmetric and leave this at 0.</summary>
    public double MaxDeflNegRad { get; set; }

    /// <summary>The travel limit on the side `sign` lies on.</summary>
    public double LimitFor(double sign) => sign < 0.0 && MaxDeflNegRad > 1e-9 ? MaxDeflNegRad : MaxDeflRad;

    /// <summary>Clamp a deflection to this axis's stops, which need not be the same distance from neutral.</summary>
    public double ClampDefl(double deflRad) => Math.Clamp(deflRad, -LimitFor(-1.0), LimitFor(1.0));

    /// <summary>The bigger of the two stops: a search bound, not a limit to deflect to.</summary>
    public double MaxTravelRad => Math.Max(MaxDeflRad, MaxDeflNegRad);

    /// <summary>
    /// A spare per-axis multiplier on the INCIDENCE a deflection commands. <b>Default 1.0, and every aircraft in
    /// the fleet leaves it there</b> — it is a hook for a future, physically-derived loss, not a tuning knob.
    ///
    /// <b>It is NOT the flap effectiveness τ.</b> τ is already in the model, and in the right place: a control
    /// splits into a HINGED row that turns to its full geometric angle (gain ±1, because the surface really does
    /// sit at α + δ) and a FIXED row ahead of it that is merely cambered by it (gain g = 0.85·τ(E), E = c_f/c).
    /// The section's response is τ = E + (1 − E)·g by construction — see docs/CONTROL-SURFACES.md. Putting a
    /// chord-ratio factor here as well counts it TWICE, which is exactly the mistake 4e5e1a7 made: it set 0.66 on
    /// the Pitts, called it τ, and claimed thin-aerofoil theory corroborated it. It did not. The Pitts' own
    /// authored gain of 0.356 already carries its chord ratio, and the number was only ever the ratio needed to
    /// reach a published roll rate. It was removed on 2026-11-01.
    ///
    /// If you are about to set this to anything but 1.0, the question to answer first is which PHYSICAL loss it
    /// stands for and what independent calculation predicts that value. A loss that only ever equals the number
    /// you tuned it to belongs in neither this field nor this model.
    /// </summary>
    public double Effectiveness { get; set; } = 1.0;

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

    /// <summary>
    /// Where the hinge line sits in the movable surface, as a fraction of its own chord from its leading edge: 0 = a
    /// plain surface hinged at its nose (the default, and what every config was built with); 0.30 = a set-back hinge
    /// with 30 % of the chord ahead of it as an overhang balance (Ty 2026-11-04 for the Extra: "the aileron hinge point
    /// needs to be 30% … so the front of the ailerons sticks up when deflected"). The hinged strips stay the drawn
    /// surface; this says where on them it turns. See docs/CONTROL-SURFACES.md, "Set-back hinges".
    /// </summary>
    public double HingeSetBackFrac { get; set; }
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

    /// <summary>The spoiler roar's level, 1 = the stock voice (WindSynth: a 650 Hz band of noise growing with the
    /// deployment and the airspeed). The 2-33's is quieter (Ty 2026-11-05 16:06: "a little too loud").</summary>
    public double SoundLevel { get; set; } = 1.0;
    /// <summary>How much low wake rumble sits under the roar, 0 = none (the stock voice); 1 is about half the stock
    /// roar's RMS on its own (tools/SpoilerAudio). The 2-33's dive brakes: "a bit more rumble to it".</summary>
    public double SoundRumble { get; set; }

    /// <summary>Handle position (0..1) at which the WHEEL BRAKE starts to bite, for a glider whose brake is on
    /// the end of the dive-brake handle's travel (SGS 2-33: 0.9, i.e. the last tenth; the AFM has the pilot go
    /// full aft after touchdown). 0 = no such linkage, which is every other type. A RECORD of the rig table's band
    /// (ModelRigTests): since 2026-11-05 the sim does not apply it, because the sim only sees the spoiler, not the
    /// handle. The input owns the handle and its brake band (Core.Vr.SpoilerHandle, 0.85).</summary>
    public double WheelBrakeFromSpoiler { get; set; }
    /// <summary>Handle position at which that brake is full on.</summary>
    public double WheelBrakeFullSpoiler { get; set; } = 1.0;

    /// <summary>PHYSICAL AIRBRAKES (Grob G 103, 2026-11-01). With <see cref="DragOnly"/> false and a plate height and a
    /// span given, the brakes act on the wing strips they actually sit on, instead of as one flat-plate area hung on
    /// the aeroplane: the strips inside [<see cref="BrakeSpanInnerM"/>, <see cref="BrakeSpanOuterM"/>] (|y|, each side)
    /// lose lift and gain the plate's drag. Two physical terms, nothing fitted:
    ///  - LIFT. A plate standing out of the upper surface at x_b = <see cref="BrakeChordFraction"/> of the chord
    ///    separates the flow behind it, and the upper surface aft of it sits at the base pressure of the wake: the
    ///    section's circulation is what the chord AHEAD of the plate can carry, so its lift falls to x_b of the
    ///    clean value (the Kutta point moves forward to the plate). At part deflection the loss is in proportion.
    ///  - DRAG. The plate is a flat plate normal to the flow: CD 1.2 on its own frontal area (Hoerner, Fluid-Dynamic
    ///    Drag ch. 3, a plate of aspect ratio 5-10 -- the brake's span over twice its height, the wing being its
    ///    reflection plane), in the local dynamic pressure over the upper surface at x_b, (Ue/V)^2 q, Ue/V being
    ///    <see cref="BrakeLocalVelocityRatio"/> from the section's own pressure distribution.
    /// Because each strip's lift is lost where it really is, the spanwise loading dips over the brake span, the rest of
    /// the wing flies at a higher angle to make it up, and the induced drag the strips already carry rises with it.
    /// <see cref="PlateAreaM2"/> is not used.</summary>
    public double BrakeSpanInnerM { get; set; }
    public double BrakeSpanOuterM { get; set; }
    /// <summary>Chordwise position of the brake plate, fraction of the local chord from the leading edge.</summary>
    public double BrakeChordFraction { get; set; } = 0.5;
    /// <summary>Plate height at full extension, m.</summary>
    public double BrakeHeightM { get; set; }
    /// <summary>Edge velocity over the upper surface at the plate, as a fraction of the free stream.</summary>
    public double BrakeLocalVelocityRatio { get; set; } = 1.0;
    /// <summary>Flat plate normal to the flow, aspect ratio 5-10 (Hoerner).</summary>
    public const double BrakePlateCd = 1.2;

    /// <summary>True when the brakes are modelled on the wing strips (see <see cref="BrakeSpanInnerM"/>).</summary>
    public bool Physical => !DragOnly && BrakeHeightM > 0.0 && BrakeSpanOuterM > BrakeSpanInnerM;

    /// <summary>The fraction of a strip of this width, centred at |y|, that lies inside the brake span.</summary>
    public double SpanOverlap(double absY, double width)
    {
        if (!Physical || width <= 0.0) return 0.0;
        double lo = Math.Max(absY - 0.5 * width, BrakeSpanInnerM), hi = Math.Min(absY + 0.5 * width, BrakeSpanOuterM);
        return hi > lo ? (hi - lo) / width : 0.0;
    }

    /// <summary>The wheel brake the handle itself is applying at this deflection, 0..1.</summary>
    public double BrakeFromHandle(double spoilerFraction)
    {
        if (WheelBrakeFromSpoiler <= 0.0) return 0.0;
        double full = Math.Max(WheelBrakeFullSpoiler, WheelBrakeFromSpoiler + 1e-6);
        return Math.Clamp((spoilerFraction - WheelBrakeFromSpoiler) / (full - WheelBrakeFromSpoiler), 0.0, 1.0);
    }
}

public sealed class FuselageConfig
{
    public double Cd0Area { get; set; }
    public double SideForceArea { get; set; }
    /// <summary>Scale on the fuselage's destabilising pitch moment (AeroModel.FuselageMunkM3; 1 = Gilruth & White). For bodies
    /// the estimate doesn't fit (owner 2026-10-06: the Gee Bee R-2's barrel was "just barely stable"; no good fuselage
    /// data), set to land the known stability.</summary>
    public double MunkScale { get; set; } = 1.0;
    public DampingConfig Damping { get; set; } = new();

    /// <summary>
    /// Slender-body crossflow drag (optional; zero areas disable). At high alpha/beta the fuselage is
    /// broadside to the flow: plan-view area produces normal force (arrests spin flattening), side-view
    /// area produces yaw-restoring side force when the fin is stalled/blanketed. Forces act at the
    /// respective area centers (x vs CG), giving the moments a point-model fuselage cannot.
    /// </summary>
    public CrossflowConfig Crossflow { get; set; } = new();
}

/// <summary>How the airframe is built (crash-in-water damage, Core/Wreck/AirframeHydroModel).</summary>
public sealed class ConstructionConfig
{
    /// <summary>"metal" (riveted 2024-T3 sheet, the default) or "fabric" (doped fabric over a steel-tube fuselage and
    /// wooden or metal wings).</summary>
    public string Covering { get; set; } = "metal";
    /// <summary>Cockpit openings in the top of the fuselage. Empty = an enclosed cabin (windshield and two doors).</summary>
    public List<CockpitOpeningConfig> Cockpits { get; set; } = new();
    public bool IsFabric => string.Equals(Covering, "fabric", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One cockpit opening in the fuselage top deck, body frame (+x forward), measured from the type's model.</summary>
public sealed class CockpitOpeningConfig
{
    /// <summary>Forward edge of the opening, body x, m.</summary>
    public double FrontX { get; set; }
    /// <summary>Aft edge of the opening, body x, m.</summary>
    public double AftX { get; set; }
    /// <summary>Plan area of the opening, m^2.</summary>
    public double AreaM2 { get; set; }
    /// <summary>Width of the opening, m (the span of a canopy over it).</summary>
    public double WidthM { get; set; }
    /// <summary>"open" (nothing over it: always an opening) or "canopy" (acrylic over it: an opening once it breaks).</summary>
    public string Enclosure { get; set; } = "open";
    public bool IsOpen => string.Equals(Enclosure, "open", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An aileron interconnect: a rigid strut, one each side, pinned to a horn on each aileron. The stick drives the
/// <see cref="DrivenSurface"/> rows; the <see cref="SlavedSurface"/> rows go wherever the strut puts them. The strut runs
/// between the two hinge lines at span |y| <see cref="DrivenY"/> / <see cref="SlavedY"/>. Each horn points from its hinge
/// line to its pin at <see cref="DrivenHornDownDeg"/> / <see cref="SlavedHornDownDeg"/> below body +x (forward) in the
/// x–z plane, <see cref="HornArmM"/> long (or its own <see cref="DrivenHornArmM"/> / <see cref="SlavedHornArmM"/>).
/// The slaved aileron turns with the driven one in the ratio of the two horns' arms measured across the strut, so two
/// parallel equal horns (a parallelogram) move the ailerons 1:1. Without a horn angle both horns stand PERPENDICULAR to
/// the strut, pointing forward (the pre-2026-11-03 stand-in, which also gives 1:1).
/// </summary>
public sealed class SlaveStrutConfig
{
    public string DrivenSurface { get; set; } = "";
    public string SlavedSurface { get; set; } = "";
    public double DrivenY { get; set; }
    public double SlavedY { get; set; }
    public double HornArmM { get; set; } = 0.07;
    /// <summary>Driven (stick) aileron's horn: hinge → pin direction, degrees below body +x (+ = pin below the hinge line).
    /// Null = the perpendicular-to-the-strut stand-in.</summary>
    public double? DrivenHornDownDeg { get; set; }
    /// <summary>Slaved aileron's horn direction, as <see cref="DrivenHornDownDeg"/>.</summary>
    public double? SlavedHornDownDeg { get; set; }
    /// <summary>Each horn's own hinge-to-pin length, m (0 = <see cref="HornArmM"/>).</summary>
    public double DrivenHornArmM { get; set; }
    public double SlavedHornArmM { get; set; }
    /// <summary>Where the horn geometry comes from (JSON has no comments).</summary>
    public string Source { get; set; } = "";
}

/// <summary>Per-type switches for the wing cellule of a multi-wing aeroplane.</summary>
public sealed class CelluleConfig
{
    /// <summary>Apply the measured cellule CLmax factor to this aeroplane's wing strips. Only turn this on for a type
    /// whose section tables are a TRUE 2-D section; see BiplaneCellule for why.</summary>
    public bool LiftInterference { get; set; }
}

public sealed class SpinAeroConfig
{
    /// <summary>
    /// ROLLING MOMENT DUE TO SIDESLIP ABOVE THE STALL (NACA TR-379, Knight &amp; Wenzinger 1932, Figs. 27–30): past maximum
    /// lift the yawed wing's LEADING half carries more load (most at its tip) and the trailing half less — the sweep-forward /
    /// sweep-back effect of yaw on the separated flow — "of the same order of magnitude as those due to rolling", and it is
    /// what extends the monoplane's stable autorotation to α 40° (pb/2V 0.64 at 20° yaw, TR-379 Table V). A straight strip
    /// wing has none of it (each strip sees only its own section α). "monoplane" (Clark Y monoplane, Fig. 28) or
    /// "biplane" (Clark Y unstaggered biplane, Fig. 27); empty = off.
    /// </summary>
    public string RollDueToSideslip { get; set; } = "";

    /// <summary>
    /// DEPTH OF THE WING-ON-WING WAKE in a biplane cellule (<see cref="FlyingGame.Core.Aero.TailWake.WingQFactor"/>),
    /// as a multiplier on the dynamic-pressure LOSS. 1.0 = the Report 648 result as computed (today's behaviour), 0 = the
    /// two wings never shade each other.
    ///
    /// Why it is a knob (Ty, 2026-11-08): the shading is geometrically right — positive stagger puts the upper wing
    /// upstream of the lower at alpha about -66 deg, so INVERTED the lower wing is the one that dies — but its
    /// consequence is backwards from the real aeroplane. The lower wing sits BELOW the CG and the upper 0.81 m ABOVE it,
    /// so killing the lower one leaves the upper wing's drag on the long lever, which is a body nose-up moment and
    /// therefore nose-DOWN when inverted. Upright the wings are barely shaded at all. The model then makes inverted
    /// spins nose-low and upright spins level, and a Pitts does the opposite: "upright spins are more nose low and
    /// inverted flat spins are more nose high".
    ///
    /// The magnitude is the part with no measurement behind it: Report 648's centre-line deficit is a fit taken on
    /// attached and flapped sections (c_d &lt;= 0.1) and at c_d about 1 it returns more than 1 for every x/c &lt; 2.1, so it
    /// is CLAMPED, and the clamp is what asserts near-dead air (q 0.04) a gap away. This scales that assertion.
    /// </summary>
    public double CelluleWakeK { get; set; } = 1.0;

    /// <summary>
    /// HORIZONTAL-TAIL EFFECTIVENESS AT 60-90 DEGREES OF LOCAL ANGLE, as a multiplier on the whole force of each
    /// stabiliser and elevator strip. 1.0 = untouched, so no type changes until it is entered. The band is folded
    /// about 90 deg (so 90-120 is treated as 60-90 reversed) and mirrored for negative alpha, and it ramps in over
    /// 50-60 deg so nothing steps. Below 50 deg of local angle it does nothing at all, which is why it cannot touch
    /// any ordinary flight condition: 60-90 deg at the tail happens in a spin or a tumble and nowhere else.
    ///
    /// Ty, 2026-11-09, after flying 2026.1109.1912: "I tested the pitts same flat spin problems make the stab more
    /// effective 60-90 aoa" - and, asked to confirm the direction, "More effective".
    ///
    /// WHY IT IS A KNOB AND NOT A DERIVATION, stated plainly. Flat-plate theory says the stabiliser is ALREADY at its
    /// limit here: the derived section's Cn is 1.18 at 60 deg and 1.19 at 90, against Cd_max 1.19 from Hoerner's AR 1-5
    /// plates and Viterna &amp; Corrigan's 1.11 + 0.018*AR, and the horizontal tail's own aspect ratio of 2.6 puts it
    /// squarely in that band. The slipstream is already carried separately (eta_t at the stab measures 1.29-1.53 in
    /// the spin, so it is working on MORE than free-stream dynamic pressure, not less). So there is no term left in
    /// plate theory to find, and this multiplier deliberately exceeds it on the owner's flight evidence rather than
    /// on a reference. He flies flat spins in the aeroplane; the model does not.
    ///
    /// What it is NOT: it is not the dead rudder. That has a measured cause - `fin-lowAR` holds its normal force flat
    /// from 25 to 90 deg, so a +-30 deg rudder commands 0.03-0.15 of Cn where it commands 2.17 at small angles - and a
    /// flat Cn is a dead control whatever its magnitude. This knob scales the horizontal tail's force, which also
    /// scales what its elevator can command, but it leaves the fin alone.
    /// </summary>
    public double StabHighAlphaK { get; set; } = 1.0;

    /// <summary>
    /// FIN AND RUDDER IN THE FUSELAGE'S ACCELERATED CROSSFLOW. 0 = off (today's behaviour, so no type changes until
    /// it is entered), 1 = full cylinder theory. Scales each VERTICAL strip's LATERAL flow component by
    ///     amp = 1 + k * (R/r)^2,   r = the strip's distance from the fuselage axis, clamped to R
    /// with R the local body radius. Potential flow past a circular cylinder has surface speed 2*V*sin(theta),
    /// peaking at the two points 90 degrees from the stagnation line - and for a crossflow coming at the side of a
    /// fuselage, those two points are the TOP and the BOTTOM. A fin standing up off the fuselage therefore sits
    /// exactly in the accelerated flow, with up to twice the lateral velocity at the root and so four times the
    /// lateral dynamic pressure, decaying as 1 + (R/r)^2 to free stream a body-diameter out. This is the mechanism
    /// behind DATCOM's fin sidewash factor and it is NOT in a strip model, which hands each strip the free-stream
    /// lateral component however close to the body it sits.
    ///
    /// Ty, 2026-11-09: "The rudder is much more effective in real life during spin recovery", then "Build it so I
    /// can test the rudder crossflow fix".
    ///
    /// WHY THIS AND NOT THE SECTION. Three attempts at reshaping the fin's own curve all failed on an arithmetic
    /// wall (tools/aircraft_builder/fin_section.py): authority at a local angle A is CN(A+30) - CN(A-30), CN cannot
    /// exceed the broadside plate at ~1.19, and `fin-lowAR` is at 97 % of that ceiling by 30 degrees, so authority
    /// there REQUIRES a low CN at A-30 - which is simultaneously the autorotation drive and the rudder power that
    /// enters the spin. This term escapes the wall entirely because it multiplies q, not CN: the rudder's moment
    /// is dCN * q * S, so it scales with the dynamic pressure the surface is actually working in. And it is ZERO
    /// without crossflow, so ordinary flight, the spin entry and the attached rudder are all untouched.
    ///
    /// THE GEOMETRY IS THE WEAK PART, and the knob carries it. With measured fuselage stations R is the local
    /// half-depth; without them it falls back to the MEAN half-depth from the crossflow block's side area and
    /// length, which over-states R at the tail where the fuselage has tapered. So the shape of the law is derived
    /// and its scale is not: 1.0 is cylinder theory on a mean radius, not a measurement.
    /// </summary>
    public double FinSidewashK { get; set; } = 0.0;

    /// <summary>
    /// THE SAME FUSELAGE CROSSFLOW ACCELERATION, AT THE HORIZONTAL TAIL. amp = 1 + k·(R/r)² on each horizontal
    /// tail strip's NORMAL flow, R the body's local half-width and r the strip's distance off the centreline,
    /// ramped in over 20°–40° of the strip's own flow angle so that ordinary flight keeps the trim it has.
    /// Owner-directed, 2026-11-10: "The elevator gets the same dynamic pressure power you just gave the rudder",
    /// after a clip of his own flat spin in which "the elevator literally does nothing in the spin".
    ///
    /// SEPARATE FROM <see cref="FinSidewashK"/> on purpose, although it is one effect and they will usually carry
    /// the same number: the fin's amplified component is the lateral one, which is zero in symmetric flight, so
    /// that knob cannot reach ordinary flying and needs no ramp. This one's is the normal component, which is
    /// never zero, and it is coupled to the spin ATTITUDE through the stabiliser — so the two want to be swept
    /// against each other rather than locked together.
    /// </summary>
    public double StabSidewashK { get; set; } = 0.0;

    /// <summary>
    /// A DEFLECTED CONTROL ON A BROADSIDE SURFACE ACTS AS A SPOILER. Adds a force normal to the FIXED surface's own
    /// plane, k·sin(δ)·|sin α_chord|·q·S per hinged horizontal-tail strip, ramped in over 20° from
    /// <see cref="ControlSpoilerOnsetDeg"/>. 0 = off.
    ///
    /// THIRTEEN CONFIGS CARRY 0.6 since 2026-11-10: the Pitts and the Extra, which it was fitted on, and the eleven
    /// of the fabric fleet whose horizontal tails became flat plates the same day. It is NOT refitted per type —
    /// the argument below is about the SECTION, not the airframe, so when a type's tail became a plate the argument
    /// started applying to it, and the restriction to two types had been staging rather than a finding (Ty, shown
    /// that: "Do it"). On the other eleven it had a second, sharper consequence: the
    /// <see cref="ControlSpoilerOnsetDeg"/> of 10 they were given with the plate was INERT, because the block in
    /// <see cref="FlyingGame.Core.Aero.AeroModel"/> is gated on k &gt; 0 — the onset was moving the start of a term
    /// multiplied by zero. It roughly doubles their post-stall authority floor, costs their pitch damping nothing
    /// (dM/dq comes out bit-identical at k 0 and 0.6, because this term is gated on a DEFLECTED control and dM/dq
    /// is controls-neutral) and leaves every type's Vso and trim untouched, which was measured rather than assumed
    /// (SpoilerFleetProbe, SPOILER_FLEET=1; the per-type numbers are in each config's `controlSpoilerKSource`).
    ///
    /// WHY IT EXISTS: post-stall the section law is CN = Cd_max·sin θ, which is flat at θ ≈ 90°, so a strip model
    /// gives a control on a broadside surface no normal force to command — measured on the Pitts, the elevator's
    /// whole travel moves Cn by 0.387 at α 30 and 0.062 at α 80, while the tail has 116–122 % of free-stream q and
    /// the right area and arm (ElevatorBreakdownProbe). The real surface displaces the separated wake instead of
    /// turning the flow, and the base-pressure difference that leaves acts on the whole tail. One angle per strip
    /// cannot express that, so it is added rather than derived.
    ///
    /// BOTH TAILS. It shipped on the horizontal tail alone for one build so that this and <see cref="FinSidewashK"/>
    /// would not move at once; the rudder needs it for the same reason, and on a type whose fin stands well off the
    /// body — the Extra — the crossflow knob cannot reach the fin at all and this is the only thing that does.
    /// </summary>
    public double ControlSpoilerK { get; set; } = 0.0;

    /// <summary>
    /// WHERE <see cref="ControlSpoilerK"/> STARTS, in degrees of the strip's own flow angle; it is full 20° later.
    /// Default 30, which is the angle it shipped with and the one Ty flew and accepted ("That is it! Now it behaves
    /// like a Pitts really does"), so no type changes until it is entered.
    ///
    /// IT IS A KNOB, AND THE ONSET IS THE PART OF IT WITH PHYSICS IN IT. The spoiler term exists because the section
    /// table stops being able to express a control: the model moves a control by shifting its section's incidence,
    /// and once the section is on the fully separated law CN = Cd_max·sin(θ) that shift changes which way the force
    /// points and not how big it is. So the term should begin where the TABLE'S OWN flap effect dies — at the
    /// section's stall — and 30° was chosen when every horizontal tail in the fleet was a NACA 0012, whose stall is
    /// at 14° and whose separated law has taken over by about 30°.
    ///
    /// A FLAT-PLATE TAIL STALLS AT 9–12°, not 14, which is why this is a field and not a constant: with the plate,
    /// the band from the section's stall up to 30° is one where the table has no control effectiveness left and this
    /// term has not started either, and that shows up as a hole in elevator authority at α 20–34
    /// (FlatPlateSearchProbe, FLAT_PLATE_SEARCH=…). Lowering it is NOT free — it is the term Ty judged the Pitts'
    /// spin on, so moving the onset moves a spin he has already accepted, and it has to be flown again before it
    /// ships on a type he flies.
    /// </summary>
    public double ControlSpoilerOnsetDeg { get; set; } = 30.0;

    /// <summary>
    /// GEOMETRIC HELICAL SHADOW DEPTH, 0 = use the Report 648 wake path instead (the default, so no type changes until
    /// it is entered). Above 0, EVERY surface's dynamic-pressure loss - wings, stabiliser, fin, all of them - is computed
    /// from THIS AIRFRAME'S OWN GEOMETRY by marching the real helical air path upstream from each section and seeing what
    /// it came through. This is the share of dynamic pressure a fully shadowed strip loses.
    ///
    /// Ty, 2026-11-08: "Model the shadowing from our own geometry on each wing section rather than the naca data so we
    /// can model the helical flow exactly and use reduced Q from a k factor in the shadow that we can tweak for fine
    /// tuning... it will break us out of this naca shadow trap and put the controls back in our hands based on solid
    /// physics." The Report 648 path could not answer the question it was being asked: its half-width (~0.45 m) sat
    /// within 3 cm of this cellule's stagger offset (~0.48 m), so whether a wing was shadowed at the flat attitude came
    /// down to incidental differences in how the two sections' chord extents were built. It shadowed 84-96 % of the
    /// downstream wing inverted and ~1 % upright, when the geometry says both should happen. Ty extended it to every
    /// surface the same day: "Use the same dynamic pressure shadow based on actual geometry for all surfaces not just the
    /// wing. This needs to include the tails too. This might be the key to unlocking the Endo in the extra."
    /// </summary>
    public double ShadowK { get; set; } = 0.0;

    /// <summary>Chords of helical path over which a shadow washes out as the wake refills. 0 or less = no washout.
    /// The march itself stops at three metres, which on this fleet is three to four chords, so values far above that
    /// cannot express anything.</summary>
    public double ShadowDecayChords { get; set; } = 3.0;
}

public sealed class SmokeConfig
{
    /// <summary>Measured outlet in body axes (+x forward, +y right, +z down), metres from the CG.
    /// Optional: overrides the prop-hub estimate, whose hub need not coincide with the placed model.
    /// A single smoke-fed pipe may be selected on an airframe with paired exhausts.</summary>
    public double[]? OutletBodyM { get; set; }
    /// <summary>Return a measured outlet only when all three coordinates are finite.</summary>
    public bool TryOutlet(out MathTypes.Vec3 outlet)
    {
        outlet = MathTypes.Vec3.Zero;
        if (OutletBodyM == null || OutletBodyM.Length != 3) return false;
        foreach (double v in OutletBodyM) if (double.IsNaN(v) || double.IsInfinity(v)) return false;
        outlet = new MathTypes.Vec3(OutletBodyM[0], OutletBodyM[1], OutletBodyM[2]);
        return true;
    }
    /// <summary>The exhaust's distance aft of the nose (spinner tip), m.</summary>
    public double AftOfNoseM { get; set; }
    /// <summary>The exhaust's line below the longitudinal axis, pointing aft (°).</summary>
    public double DownDeg { get; set; } = 30;
    /// <summary>The engine's displacement (in³) and the exhaust pipe's diameter (in): the jet's exit velocity comes from
    /// the engine's mass flow (<see cref="Aero.ExhaustJet"/>). 0 = estimated from the power (≈ 0.56 hp/in³).</summary>
    public double DisplacementCuIn { get; set; }
    public double PipeDiameterIn { get; set; } = 4;
    /// <summary>The exhaust gas temperature at the pipe's exit (K).</summary>
    public double ExhaustTempK { get; set; } = 900;
    /// <summary>Cylinders feeding the smoke pipe's engine: a four-stroke's pipe sees cylinders / 2 exhaust pulses per
    /// revolution (<see cref="Aero.ExhaustPulse"/>). 6 when not given (the AEIO-540s).</summary>
    public int Cylinders { get; set; } = 6;
    /// <summary>Compression ratio, for the part-throttle residual gas (<see cref="Aero.ExhaustJet.ExitVelocity(double, double, double, double, double, double, double, double, double)"/>).</summary>
    public double CompressionRatio { get; set; } = 8.7;
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

    /// <summary>
    /// WHAT THE PROPELLER WEIGHS, kg. It matters because the propeller can LEAVE — a crankshaft failure takes it with
    /// it (Ty 2026-11-09) — and it is the furthest-forward mass on the aeroplane, so losing it throws the CG backwards
    /// hard. 0 means "estimate it", see <see cref="PropMassKg"/>'s estimator, and a published figure should replace
    /// the estimate wherever one is known.
    /// </summary>
    public double PropMassKg { get; set; }

    /// <summary>
    /// THE BLADE ANGLE AT THREE-QUARTER RADIUS, degrees, which with a constant geometric pitch sets the twist all
    /// along the blade (<see cref="FlyingGame.Core.BladeElement.Geometry"/>). 0 means this type has not been fitted
    /// yet.
    ///
    /// For a FIXED-PITCH type it is fitted to the one number a pilot reads on every run-up: the rpm at full throttle
    /// standing still (Ty 2026-11-09, "Use full power static rpm (zero airspeed) as the anchor for fixed pitch").
    /// Static rpm is sharply sensitive to pitch — two degrees is 200 rpm on a J-3 — needs no assumption about drag or
    /// cruise, and is a figure he has seen rather than one from a book. See
    /// <see cref="FlyingGame.Core.PistonEngine.FitPitchForStaticRpm"/>.
    ///
    /// For a CONSTANT-SPEED type this is not used in flight: the governor sets the blade, and what matters is the
    /// pair of stops, which are derived from his other two rules (<see cref="FlyingGame.Core.BladeElement.GovernorStops"/>).
    /// </summary>
    public double BladePitch75Deg { get; set; }

    /// <summary>Where <see cref="BladePitch75Deg"/> came from — the anchor it was fitted to and what followed.</summary>
    public string BladePitchSource { get; set; } = "";

    /// <summary>
    /// A CONSTANT-SPEED PROPELLER'S PITCH STOPS, degrees at 0.75 R. Derived, not chosen — Ty 2026-11-09: "the low
    /// pitch stop allows full rpm at 26 inches of manifold pressure at sea level for naturally aspirated engines and
    /// the high pitch stop prevents an overspeed until vd at full power with Vd being 110% of the vne". See
    /// <see cref="FlyingGame.Core.BladeElement.GovernorStops"/>, which computes them from the blade and the type's
    /// own Vne; these fields are that computation CACHED, because deriving them costs two bisections of
    /// blade-element solutions and the governor needs them every step. 0 = not yet derived.
    /// </summary>
    public double FineStopDeg { get; set; }
    public double CoarseStopDeg { get; set; }
    public string PitchStopSource { get; set; } = "";

    /// <summary>Activity factor: the published measure of how much blade there is, which with
    /// <see cref="PropBladeCount"/> sets the chord distribution a blade-element solution needs (see
    /// <see cref="FlyingGame.Core.BladeElement"/>). 100 is a conventional light-aeroplane blade.
    ///
    /// The blade COUNT is not here: it is <see cref="PropBladeCount"/>, which every type already carries alongside
    /// propMaterialSource naming the real propeller. That count is not bookkeeping — it decides whether the
    /// propeller's gyroscopic reaction is steady or PULSATES, because three or more blades are polar symmetric while
    /// two swing between end-on and across twice a revolution, which is what fatigues a crankshaft (see
    /// <see cref="FlyingGame.Core.Engine.CrankshaftFatigue"/>).</summary>
    public double ActivityFactor { get; set; } = 100.0;
    public double IdleRpm { get; set; } = 700;
    public double MaxRpm { get; set; } = 2700;
    public double PropInertia { get; set; } = 1.7;      // prop + crank about x, kg m^2
    public int RotationSign { get; set; } = 1;          // +1 = right-hand (clockwise from behind)
    public double Efficiency { get; set; } = 0.75;
    public double ThrustLineZ { get; set; } = 0.0;      // + below CG (z down)
    public double PFactorK { get; set; } = 0.35;        // lateral thrust offset fraction of radius per sin(alpha)
    public double SlipstreamK { get; set; } = 0.12;     // spiral-slipstream yaw moment coefficient

    /// <summary>Share of the ingested flow's CROSS-stream momentum the disc destroys, which is the propeller's in-plane
    /// (normal/H) force — see <see cref="FlyingGame.Core.PropModel.NormalForce"/>. 0 = off, and it is off for every type
    /// until that type's own number is entered, because the term is real in attached flight and every stored rig and trim
    /// was solved without it. Per-type and opt-in for the same reason spinAero.rollDueToSideslip is.</summary>
    public double NormalForceK { get; set; } = 0.0;

    /// <summary>Depth of the traced thrust jet (<see cref="FlyingGame.Core.Aero.PropJet"/>): 1 = the full momentum-theory
    /// increment the slipstream carries, 0 = no jet at all (diagnostics). The SHAPE of the jet is geometry — where the
    /// tube goes, how it bends, which surfaces it covers — and this is the one scale factor on it.</summary>
    public double JetK { get; set; } = 1.0;

    /// <summary>
    /// HOW FAST THE JET'S TUBE SPREADS, as a fraction of its own radius per PROPELLER DIAMETER of path aft
    /// (Ty 2026-11-09: "build the spread as something tunable with a k factor from none to 25% per diameter of prop in
    /// distance aft ie if the fuselage is 4 prop diameters the spread would be doubled by the back of the tail. that
    /// seems extreme so we have head room"). So R(s) = R0 * (1 + k * s / D), and k = 0.25 four diameters aft doubles
    /// the radius. Clamped to <see cref="JetSpreadKMax"/>.
    ///
    /// It replaces a flat 0.045 metres of radius per metre of path. Two reasons. The rate belongs to the JET's own
    /// size, not to the airframe's: a shear layer spreads in proportion to the width of the jet feeding it, so a
    /// per-metre number silently means different things on a 1.8 m propeller and a 2.6 m one. And the tube's starting
    /// radius is no longer a constant 0.7 R — it is the real contraction, which is 0.71 R standing still and 0.98 R in
    /// cruise, so an ADDITIVE growth added the same absolute width to a narrow static tube as to a wide cruising one.
    /// Proportional growth is the one that means the same thing in both.
    ///
    /// THIS NUMBER IS THE STILL-AIR LIMIT, not the rate in flight. Ty 2026-11-09: "that makes sense to the k factor has
    /// to vary with airspeed." A shear layer grows in proportion to the velocity DIFFERENCE across it, measured against
    /// the mean — the mixing-layer shear parameter
    ///     lambda = (U_jet - U_outside) / (U_jet + U_outside) = du / (2 V + du)
    /// which is 1 standing still, where the slipstream really is a jet into still air, and collapses toward 0 in
    /// cruise, where the wash is only a quarter faster than the air it is moving through. No shear, no mixing, no
    /// spread: the effective rate is <c>JetSpreadK * lambda</c>. That is why a propeller aeroplane's whole tail is in
    /// the blast at full power standing still and its slipstream is barely wider than the disc in cruise, and it is
    /// the same statement as the measurements — a tractor single's slipstream at the tail is about one propeller
    /// diameter across.
    ///
    /// 0.25 is the owner's stated extreme ("25% per diameter of prop in distance aft ... that seems extreme so we have
    /// head room") and it turns out to BE the still-air rate rather than a ceiling over it: with the static
    /// contraction at 0.707 R, k = 0.25 per diameter is dR/ds = 0.0885, a 5.1 deg half-angle, which is the measured
    /// spreading rate of a round jet into still air (Schlichting, <i>Boundary-Layer Theory</i> ch. 24). The flat
    /// 0.045 m/m it replaces was that same still-air rate applied at EVERY airspeed, which is why it read as 0.10
    /// across the fleet — a cruise compromise standing in for both ends.
    /// </summary>
    public double JetSpreadK { get; set; } = 0.25;

    /// <summary>
    /// THE PROPELLER DISC'S STATION, body x, when it has been measured. 0 derives it from the fuselage construction
    /// as before: the furthest-aft trailing edge on the aeroplane, less 0.2, plus the crossflow length, plus 0.1.
    /// That construction anchors a length measured SPINNER TO TAIL POST at the RUDDER'S TRAILING EDGE, which is
    /// behind the tail post, so it lands the nose short by the rudder's overhang. On the S-2B that is 0.317 m - the
    /// derived hub sits at +1.283 where the type's own inertia build-up measures the disc at +1.600 (an earlier
    /// placement from the TCDS arms used +1.68). A fifth of the arm, on the station that sets the in-plane force's
    /// moment, P-factor's moment, where a thrown propeller's mass leaves from, and where the slipstream starts.
    /// Ty 2026-11-09: "Check prop location on the pitts. Make sure it has the correct arm."
    /// </summary>
    public double HubXM { get; set; }

    /// <summary>The ceiling on <see cref="JetSpreadK"/>, the still-air rate; the rate in flight is this times lambda.</summary>
    public const double JetSpreadKMax = 0.25;
    /// <summary>Brake specific fuel consumption, kg of fuel per watt-second of SHAFT power. The default is a normally
    /// aspirated piston's 0.45 lb/hp/hr: a 160 hp four at 75 % cruise burns about 34 litres an hour on it, which is
    /// what the book says. Turbines want their own number.</summary>
    public double BsfcKgPerWs { get; set; } = 7.6e-8;
    /// <summary>Constant-speed propeller: the governor holds <see cref="GovernedRpm"/> whenever there is enough
    /// power to govern, so the throttle changes manifold pressure (loudness), not rpm (pitch). Fixed-pitch:
    /// rpm follows throttle and airspeed.</summary>
    public bool ConstantSpeed { get; set; }
    public double GovernedRpm { get; set; } = 2400;

    /// <summary>Where this type's constant-speed governor BOTTOMS with the blue lever full back, rpm. 0 = use the
    /// fleet's 1,200 rpm (PropModel.FleetLowGovernedRpm). Authored only where a type's own stop is known.</summary>
    public double GovernorLowRpm { get; set; }

    /// <summary>
    /// WHERE THE BLADES GO WHEN THE OIL PRESSURE FAILS. Ty 2026-11-10: "aerobatic aircraft with a constant speed
    /// propeller default to high pitch with loss of oil pressure like twins do (Seminole beech 18 etc) and standard
    /// single engines default to low pitch with no oil pressure".
    ///
    /// A single's governor normally uses oil pressure to hold the blades COARSE against a spring (and the blades'
    /// own counterweights), so losing the oil lets them spring FINE: the engine can still make power, which is what
    /// you want when the oil has gone but the engine has not. An aerobatic installation and a twin are plumbed the
    /// other way — oil holds them FINE, so losing it drives them COARSE, which for a twin is toward feather and for
    /// an aerobatic single means the prop will not overspeed when the aeroplane is pointed down.
    ///
    /// true = coarse (aerobatic, twin), false = fine (the standard single).
    /// </summary>
    public bool CoarseOnOilLoss { get; set; }

    /// <summary>
    /// Cylinders and compression ratio, for the COMPRESSION STROKES of a dead engine
    /// (<see cref="FlyingGame.Core.PistonCompression"/>): what makes a propeller lump to a stop rather than coast to
    /// one, and what it has to be shoved over to windmill again. 0 here means "ask the exhaust", because the same two
    /// numbers were already authored on <see cref="SmokeConfig"/> for the pipe's pulse rate and it is the same engine —
    /// see <see cref="AircraftConfig.EngineCylinders"/>, which is the one place that decides.
    /// </summary>
    public int Cylinders { get; set; }
    public double CompressionRatio { get; set; }
    /// <summary>
    /// HOW MUCH MORE THAN ITS RUNNING FRICTION THIS ENGINE RESISTS WHEN IT IS DEAD AND DRY, at rest — so 3.0 means
    /// four times (see <see cref="FlyingGame.Core.PistonEngine.BoundaryFrictionFactor"/>). 0 = use the fleet number.
    ///
    /// SOLVED, NOT CHOSEN: Ty 2026-11-10, "Cranking resistance should be calibrated such that at fine pitch the prop
    /// stops right at level flight stall speed", and then, shown that one fleet constant could not do it: "fix it per
    /// engine". For each type this is the one value that puts the propeller's stopping speed on the type's own stall
    /// speed (<c>PracticeScenario.EstimateVso</c>, sea level, flaps up) — one equation in one unknown.
    /// <see cref="DryBoundaryExcessSource"/> says what each was solved against, <c>CompressionStopProbe</c>
    /// (COMPSTOP=1) re-measures it, and with SOLVE=1 it re-derives it.
    ///
    /// AND IT IS THE ONLY FITTED NUMBER LEFT IN THE DEAD-ENGINE MODEL. The compression peak beside it is the engine's
    /// own gas load, out of its displacement and compression ratio (<c>PistonCompression.PeakNmFor</c>), and the speed
    /// a stopped propeller breaks away at is a measurement rather than an anchor — Ty gave that anchor up on 2026-11-10
    /// when the same AEIO-540 came out at 99 N·m in the Pitts and 683 in the Extra because their airframes have
    /// different Vne.
    /// </summary>
    public double DryBoundaryExcess { get; set; }
    /// <summary>Where <see cref="DryBoundaryExcess"/> came from — the stall speed it was solved against and the
    /// stopping speed that came back.</summary>
    public string DryBoundaryExcessSource { get; set; } = "";
    /// <summary>Drag of the idling / windmilling propeller as a coefficient on its DISC area (owner 2026-10-03: the 172
    /// glided 14:1 at idle — the POH says 9:1 — and floated forever). A fine-pitch prop at idle is driven by the air and
    /// is a big drag: C_D ≈ 0.15–0.25 on disc area. Fades out as the throttle comes up (gone by 20 %).
    /// Calibrated at 0.15 (172 → 8.9:1, book 9:1; Cub 8:1; SR22 ≈ 10:1, book 8.8:1). EVERY propeller aircraft has it, at the
    /// full value (owner 2026-10-05: "there should not be a single prop plane without it", and the Extra and P-51 must not
    /// override it lower — their constant-speed props go to fine pitch at idle, the most drag of all).</summary>
    public double IdleDragCd { get => _idleDragCd ?? 0.15; set => _idleDragCd = value; }
    /// <summary>Fixed-pitch prop: the speed its efficiency peaks at (m/s; default 45 ≈ 87 kt, a cruise prop). A slow type with
    /// a fine-pitched prop (the AirCam's ground-adjustable Warp Drives) peaks far lower — and so does its best climb speed.</summary>
    public double DesignSpeedMs { get; set; } = 45.0;
    /// <summary>What the blades are made of: "metal" (default), "wood" or "composite". A strike curls a metal blade's
    /// struck span and breaks a wooden or composite one's off (<see cref="PropBladeDamage"/>, Ty 2026-11-05 9:04).</summary>
    public string PropMaterial { get; set; } = "metal";
    public string PropMaterialSource { get; set; } = "";
    /// <summary>Where <see cref="PropInertia"/> comes from. Reference only: nothing in the flight model reads it.</summary>
    public string PropInertiaSource { get; set; } = "";
    /// <summary>How many blades (2 by default): each strikes, and is damaged, on its own.</summary>
    public int PropBladeCount { get; set; } = 2;
    private double? _idleDragCd;
}

public sealed class StallDynamicsConfig
{
    public double StripSepTau { get; set; } = 0.15;      // s, per-strip separation growth
    public double StripReattachTau { get; set; } = 0.6;  // s, per-strip reattachment
    public double WakeGrowTau { get; set; } = 0.25;      // s, whole-wing wake development
    public double WakeDecayTau { get; set; } = 1.0;      // s, wake washout

    /// <summary>Proposal 1: downwash transport lag (Cm-alphadot). Off = instantaneous downwash.</summary>
    public bool DownwashLagEnabled { get; set; }

    /// <summary>Proposal 3: per-strip unsteady force lag (~3 chords/V). Off = quasi-steady.</summary>
    public bool UnsteadyLagEnabled { get; set; }

    /// <summary>
    /// Where each strip's separation memory switches, measured from the strip's OWN static stall (its table's CL-max
    /// angle) instead of the fleet's fixed 16° / 11°. Null (the default) keeps 16° / 11° for every type, so adding
    /// this changed nothing on any aeroplane that does not set it.
    ///
    /// WHY IT HAD TO COME WITH THE SECTION. Those two constants were calibrated against tables that stall near 14°,
    /// which every hand-built "feel" table in the fleet does. A table derived from the real section
    /// (docs/aircraft/extra-300-airfoil-DERIVATION.md) puts the finite wing's static stall where the data says —
    /// 21–24° per station on the Extra — and with a fixed 16° onset every strip would be counted "separated", with
    /// the ailerons cut by 85 % and the induced drag tapered off, FIVE DEGREES BEFORE its flow actually leaves. The
    /// wing then cannot be pulled to its own stall at idle at all: the tail downwash collapses first. So the derived
    /// section is unflyable without this; the two are one change, not two.
    ///
    /// WHY ZERO IS THE RIGHT VALUE FOR A DERIVED TABLE. The static polar already contains the static stall, so onset
    /// is AT it (0° past): anything beyond is only the dynamic delay <see cref="StripSepTau"/> gives. Reattachment
    /// keeps the legacy band's 5° below onset, because no static-hysteresis measurement for these sections at these
    /// Reynolds numbers was found to replace it — that is an inherited number, not a derived one.
    /// </summary>
    public double? SeparationOnPastStallDeg { get; set; }

    /// <summary>See <see cref="SeparationOnPastStallDeg"/>: reattachment, degrees below the strip's own static stall.</summary>
    public double? ReattachBelowStallDeg { get; set; }
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
    /// THE BODY'S MEASURED SHAPE, station by station: <see cref="StationX"/> the body-axis x of each station, with the
    /// midline height (<see cref="StationMidZ"/>, NED so negative is up) and the section's half-width and half-height
    /// there. All four must be the same length, and two stations are the minimum; given none, the body is the constant
    /// ellipse the areas above imply with a straight midline, exactly as before. Measured off the type's three-view
    /// mesh, never a scaled drawing. It exists because an ATTACHED propeller wash needs something with a shape to
    /// follow (see <c>FuselageShape</c>): a Cub's tailcone rises most of a foot on the way to the fin.
    /// </summary>
    public double[] StationX { get; set; } = System.Array.Empty<double>();
    public double[] StationMidZ { get; set; } = System.Array.Empty<double>();
    public double[] StationHalfY { get; set; } = System.Array.Empty<double>();
    public double[] StationHalfZ { get; set; } = System.Array.Empty<double>();

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
    /// <summary>
    /// COWL camber (owner 2026-10-12, the Extra 300: "the flat, sharp-cornered top of the cowling vs the round, smooth lower
    /// cowling"): the forward body's section is cambered the OTHER way from the turtle deck, so sideslip crossflow pushes the
    /// nose station toward the belly (body-DOWN force ahead of the CG) — also nose-down with |β|, adding to the deck's.
    /// Body-down force = ½ρ·v_local²·CowlCamberArea·CowlCamberCl at CowlCamberCenterX. 0 = off.
    /// </summary>
    public double CowlCamberCl { get; set; }
    /// <summary>
    /// SHARP-CORNERED SECTION (2026-10-12, the spin autorotation work): the fraction (0…1) of the side crossflow area whose
    /// cross-section is flat-sided / flat-bottomed with sharp corners rather than round. In a spin the fuselage meets the air
    /// broadside from below (crossflow w) while yaw rotation moves each station sideways (v). A round section only DRAGS
    /// against that sideways motion (damping); a sharp-cornered one "gallops": its transverse force acts ALONG the motion for
    /// small v/w — the quasi-steady galloping of a square prism, Cfy(ψ = v/U) = A1ψ − A3ψ³ + A5ψ⁵ − A7ψ⁷ with A1 2.69, A3 168,
    /// A5 6270, A7 59 900 (Parkinson &amp; Smith 1964, QJMAM 17; Blevins, Flow-Induced Vibration ch. 4), valid to |ψ| ≈ 0.25
    /// and faded back to plain crossflow drag by |ψ| 0.45. NASA spin research (e.g. TM-84377, TN-4176 Polhamus) names the
    /// fuselage cross-section as a pro-spin yaw driver. 0 = all round (the previous model).
    /// </summary>
    public double SharpSectionFraction { get; set; }
    public double CowlCamberArea { get; set; }
    public double CowlCamberCenterX { get; set; }

    /// <summary>
    /// AFT-FUSELAGE SIDESLIP CAMBER IN CROSSFLOW (owner Ty, 2026-11-11: "Fuselage crossflow should trigger the
    /// sideslip camber on the aft fuselage and produce a pronounced negative pitching moment on the pitts and extra
    /// which should result in flatter inverted spins than upright").
    ///
    /// WHAT IT IS. <see cref="SectionCamberCl"/> already models the aft body as a cambered section to SIDESLIP flow,
    /// but its dynamic pressure is the lateral velocity alone. NACA TN 4176 / NASA TR R-29 (Polhamus) gives the
    /// three-dimensional crossflow term for exactly this effect as
    /// (C_Y)_nonpotential = c_y'(S/A)(cos²α·sin²β + sin²α) — and the model carries only the first half. The sin²α
    /// share is the one that matters in a spin, where the body is broadside: at α 80° it is 0.97 against the
    /// cos²α·sin²β half's 0.03. docs/aircraft/CM-BETA.md §6 measures the gap at 2.41× at α −40° / β 45° alone.
    ///
    /// THE LAW. The missing share is carried here, weighted by the SAME sideslip dependence the camber force already
    /// has, so it reduces to the existing lateral-velocity force multiplied by sin² of the station's own flow angle:
    ///     body-up force = ½ρ·AftCamberCl·AftCamberGain·A_aft·v_local²·(w_local² / (u² + w_local²))
    /// at the aft body's own centroid, with v_local and w_local including the rotation terms r·arm and −q·arm. That
    /// is Ty's sentence literally: the crossflow (w_local) TRIGGERS the sideslip camber (v_local²).
    ///
    /// DELIBERATE DEVIATION FROM THE PUBLISHED FORM, recorded rather than hidden: TN 4176 writes the second share as
    /// "+ sin²α", with no sin²β on it. Taken bare it would make a pitching moment at ZERO sideslip at any α, which
    /// this term — a SIDESLIP camber — must not do, and which would move every type's stall break. Carrying the same
    /// sin²β weight keeps both limits the source requires of a sideslip effect (zero at β 0) and that the existing
    /// low-α behaviour requires (zero at α 0), so SectionCamberCl's pinned normal-flight numbers are untouched by
    /// construction: at α 5° / β 15° this term is 0.8 % of the old one, at α 80° / β 40° it is 32× it.
    ///
    /// NOT APPLIED to the φ-coefficient question. The same source's sketch 13 says the flat section's force PEAKS at
    /// φ 60–70° and is ZERO at φ 90° (pure sideslip at α 0, i.e. ordinary knife-edge), where the model today makes
    /// ~4.5 kN. CM-BETA §8 calls that the owner's call and not an agent's, because it is his flight feel on the
    /// aeroplane he flies and 26 types' balance. This term changes nothing at α 0, so it leaves that decision open.
    ///
    /// GEOMETRIC, NOT PER-TYPE. A_aft and its arm are derived from the fuselage's own silhouette
    /// (<see cref="FlyingGame.Core.Aero.AeroModel.AftBody"/>: the part of the side view behind the CG), so every type
    /// gets the term and no area or station is authored anywhere. ONE THING COULD NOT BE MADE GEOMETRIC and is
    /// reported rather than disguised: the camber COEFFICIENT. The only fleet-wide shape fields are lengthM,
    /// sideArea, planArea and their centroids, and side/plan area does not discriminate the aerobatic types at all
    /// (Pitts 1.33, Extra 1.30 — but Great Lakes 1.31, Pawnee 1.32, and the fleet's two highest are the Hughes H-4
    /// flying boat at 1.71 and the 737 at 1.38). The only field that could express "rounded turtledeck over flat
    /// belly" is the station shape (StationX/MidZ/HalfY/HalfZ), which exists on two configs and on NEITHER the Pitts
    /// nor the Extra. So this is one fleet constant, not a per-type fit, and the Pitts and the Extra come out
    /// pronounced because of where they FLY — the term needs high α and high local β at once, which is a spin.
    /// </summary>
    public double AftCamberCl { get; set; } = DefaultAftCamberCl;

    /// <summary>
    /// THE KNOB (Ty 2026-11-11: "give me a knob on the game to adjust it so I can tune it rather that asking you
    /// to"). A plain scalar on the aft-fuselage camber moment. 1.0 = <see cref="AftCamberCl"/> as fitted, 0 = the
    /// term off entirely. Registered for the Developer page as `fuselage.crossflow.aftCamberGain`, range 0…3.
    /// Live without plumbing: AeroModel.ApplyFuselage re-reads Fuselage.Crossflow on every Compute, so writing this
    /// on the running aircraft's config takes effect on the next frame.
    /// </summary>
    public double AftCamberGain { get; set; } = 1.0;

    /// <summary>
    /// THE BROADSIDE SHARE — <b>OFF BY DEFAULT (0.0), AND IT IS A DECISION FOR THE OWNER, NOT AN AGENT.</b>
    ///
    /// WHY IT EXISTS. <see cref="AftCamberCl"/> carries the published crossflow share weighted by sin²β, which keeps
    /// it silent at zero sideslip. MEASURED 2026-11-11, that weighting also makes it almost powerless in the thing
    /// Ty actually asked about: a developed flat spin has β of only 2–4° (Pitts, −4.3° upright and −1.8° inverted),
    /// so the whole camber family contributes 1–2 % of the spin's pitch budget (−11 N·m upright, −25 N·m inverted,
    /// against 940 and −2216 N·m total). Polhamus' own governing variable tan φ = tan β / tan α puts that spin at
    /// φ ≈ 0.85°, and his sketch 13 says the force is near zero there. So the sourced, safely-gated term CANNOT
    /// deliver the "pronounced" moment Ty predicts, and no coefficient of plausible size will make it.
    ///
    /// WHAT THIS IS INSTEAD. The bare form TN 4176 actually publishes is "+ sin²α" with NO sin²β on it: a body that
    /// is broadside to the crossflow and asymmetric top-to-bottom makes a normal force whether or not it is
    /// sideslipping. That is not camber lift — it is the asymmetric separation of a bluff, flat-bellied,
    /// round-topped body at broadside — and it is the share that is large in a spin, where the crossflow q factor
    /// measures 0.96 of free stream against the sideslip share's 0.0002.
    ///     body-up force = ½ρ·AftCamberBroadsideCl·AftCamberGain·A_aft·V_local²·sin²(local α)
    /// Always body-up aft of the CG, hence always nose-DOWN in body axes: it steepens an upright spin and FLATTENS
    /// an inverted one, which is exactly the asymmetry Ty predicts and the acceptance test he set (upright ~78°,
    /// inverted ~95°).
    ///
    /// WHY IT IS NOT ON. It is non-zero at ZERO SIDESLIP, so it changes the pitching moment at every high-α
    /// condition on all 35 types — the stall break, the trim curve and the spin of the whole fleet. docs/aircraft/
    /// CM-BETA.md §8 reserved precisely this to the owner: "that is your flight feel on the aeroplane you fly and it
    /// is 26 types' balance, so it is your call, not mine." Shipped at 0.0 so the model is unchanged, with the
    /// measurement in the report and one knob to turn it on.
    /// </summary>
    public double AftCamberBroadsideCl { get; set; }

    /// <summary>
    /// The fleet value of <see cref="AftCamberCl"/>. 0.6 is the PITTS' own authored turtledeck coefficient
    /// (<see cref="SectionCamberCl"/>), carried unchanged to the fleet rather than refitted per type — the same move
    /// SpinAeroConfig.ControlSpoilerK 0.6 made from the Pitts to eleven types on 2026-11-10, and for the same
    /// reason: the argument is about the SECTION, and nothing in the repo distinguishes one type's aft-body section
    /// from another's (see <see cref="AftCamberCl"/> for the survey that establishes that). It is safe fleet-wide by
    /// construction, not by luck: the term carries sin²α·sin²β, so at a 3°/15° slipping approach — the condition
    /// SideslipCamberTests pins — it is under 1 N·m against that test's 450–800 N·m band.
    /// </summary>
    public const double DefaultAftCamberCl = 0.6;
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

    /// <summary>
    /// The angle the floats are RIGGED to the airframe, degrees, positive leading edge up. It is what decides how
    /// much of their own weight they carry in the air (see <see cref="FlyingGame.Core.Aero.FloatAero"/>): a float
    /// installation sets this by the length of its struts, and it is a choice the installer made, not something the
    /// float's own dimensions imply. Ty 2026-11-09: "they carry their own weight plus some so they don't cause any
    /// trim drag." The only tunable in the float's aerodynamics — everything else is measured geometry.
    /// </summary>
    public double IncidenceDeg { get; set; }
    public double StepFraction { get; set; } = 0.55;     // step position from the bow, fraction of length
    public double SpreadM { get; set; } = 2.6;           // centre-to-centre
    public double BowX { get; set; } = 2.6;              // bow tip x vs CG
    public double KeelZ { get; set; } = 1.55;            // keel at the step, z down vs CG
    public double ForebodyKeelDeg { get; set; } = 2.5;   // keel rises toward the bow
    public double AfterbodyKeelDeg { get; set; } = 4.0;  // keel rises toward the stern behind the step
    public double WaterRudderAreaM2 { get; set; } = 0.05;
    public double WaterRudderMaxRad { get; set; } = 0.6;
    /// <summary>
    /// The DRAWN water-rudder blade (Ty 2026-11-02, t278u: a hydrodynamic surface from first principles, no fudge).
    /// When <see cref="WaterRudderChordM"/> is above zero, FloatHydro models the blade as an elliptic flat plate of this
    /// chord (fore-aft) and span (vertical): its immersed area comes from where the blade really is relative to the
    /// water, its lift-curve slope from its immersed aspect ratio (Helmbold), and its force is ½ρV²·S·a·α. Zero keeps
    /// the older lumped model (WaterRudderAreaM2 with its ×2.5 factor) for the seaplanes that have not been drawn.
    /// </summary>
    public double WaterRudderChordM { get; set; }
    public double WaterRudderSpanM { get; set; }
    /// <summary>The steering post / retract hinge, where the DRAWN one is (the yoke on the drawn float's stern, as the
    /// game places the model): x forward and z down in the config datum frame like <see cref="BowX"/> and
    /// <see cref="KeelZ"/>, and y the lateral offset of its float's centreline (always positive; the left float's rudder
    /// is at −y). Drawn, not on the physics float, because its height above the water is what decides how much of the
    /// blade is wet: on the J-3 the hinge rides ~10 cm above the water at rest and only the blade's lower part dips.</summary>
    public double WaterRudderPivotX { get; set; }
    public double WaterRudderPivotY { get; set; }
    public double WaterRudderPivotZ { get; set; }
    /// <summary>The blade's ellipse centre from the pivot with the blade DOWN and straight: metres aft, metres below.</summary>
    public double WaterRudderBladeAftM { get; set; }
    public double WaterRudderBladeBelowM { get; set; }
    /// <summary>Only the LEFT float carries a water rudder (the J-3's Edo 60-1320s, as drawn: one rudder, on the left
    /// float). Read from <see cref="WaterRudderSides"/>, which is what the config states.</summary>
    public bool WaterRudderLeftOnly => WaterRudderSides == "left";
    /// <summary>The pilot can raise it (a cable to the cockpit, spring-loaded down). The retract angle at full pull,
    /// radians: the blade swings up and aft about its hinge (its bottom goes aft, then up), out of the water.</summary>
    public bool WaterRudderRetractable { get; set; }
    public double WaterRudderRetractRad { get; set; } = 1.5707963267948966;
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
    /// <summary>The DRAWN hull, when there is one (owner 2026-11-02): stations bow first, each [x, y0, z0, y1, z1, …, 0,
    /// zKeel] — x and z (down) in the config datum frame, the half-section points (y ≥ 0 from the float's own centreline)
    /// from the deck edge down to the keel. Null = the block float made from the parameters above (FloatHull.Parametric).
    /// The J-3's are cut out of its own GLB, as placed, by tools/own_models/j3_float_stations.py.</summary>
    public double[][]? Stations { get; set; }
    /// <summary>The step's x (datum frame) for <see cref="Stations"/>; null = BowX − StepFraction·LengthM.</summary>
    public double? StepX { get; set; }
    /// <summary>Water rudder blade without a drawn pivot (<see cref="WaterRudderChordM"/> 0), when down: x of its centre
    /// and the tip's z (down), datum frame, with <see cref="WaterRudderSpanM"/> as its span (top to tip).
    /// 0 span = the old default (a blade at the stern keel, area WaterRudderAreaM2, span from √(2·area)).</summary>
    public double WaterRudderX { get; set; }
    public double WaterRudderTipZ { get; set; }
    /// <summary>Which floats carry a water rudder: "both" (default) or "left" / "right" (the J-3's 60-1320 installation:
    /// the RH float assembly is listed "less water rudder", Kenmore Air Harbor EDO 60-1320 parts list, Figure 1).</summary>
    public string WaterRudderSides { get; set; } = "both";
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

    /// <summary>
    /// TOE-OUT, degrees, per wheel: how far this wheel points AWAY from the centreline (negative is toe-in). 0 on
    /// every type until one is flown with it.
    ///
    /// Ty 2026-11-10, flying the Pitts and the Extra: "How are the main gear tires aligned? It acts like it has toe
    /// in" — and they were aligned perfectly straight, which is the one thing a sprung taildragger's gear never is.
    /// Then: "Try 1 degree toe out both sides", and the reason, which is the whole point of the number: "The loaded
    /// tire tries to unload back to the other".
    ///
    /// WHY IT RESTORES: a toed-out wheel rolls at a standing slip angle, so it makes a side force outboard all the
    /// time, in proportion to the load ON THAT WHEEL. Sitting level the two cancel exactly and nothing happens. Lift
    /// one — a swerve, a gust, a wing down — and the heavier wheel's outboard force is the bigger one, so the pair
    /// push the aeroplane back toward the light side and the load shares out again. A toed-IN pair does the reverse
    /// and digs the swerve in deeper, which is what he was feeling.
    ///
    /// The sign is taken from the wheel's own y, so one number on both mains means out on both: a wheel at +y
    /// (right) points right, a wheel at -y points left.
    /// </summary>
    public double ToeOutDeg { get; set; }

    /// <summary>
    /// WHAT KIND OF SPRING this leg is: "bungee" (rubber shock cord), "spring-steel" / "spring-aluminum" /
    /// "spring-composite" (a leaf or a bow that bends), "oleo" (air over oil). Also, historically, what kind of
    /// CONTACT it is where it is not a sprung wheel at all: "float-keel", "nose-skid", "tail-skid", "tip-skid",
    /// "feet" — those are read all over the sim and must keep their meaning.
    ///
    /// Since 2026-11-10 the spring kinds are not labels: they set the leg's TRAVEL to its stop and the shape of its
    /// force past it (LandingGear.LegTravelM, LandingGear.LegForceN). They do not decide
    /// whether the wheel swings outboard as it compresses — that is the LINKAGE, which is <see cref="LegPivot"/>: a
    /// bungee on a pivoting half-axle (the Great Lakes' oleo too) swings, a telescopic strut does not.
    /// </summary>
    public string GearType { get; set; } = "";
    public string GearTypeSource { get; set; } = "";

    /// <summary>
    /// THE FUSELAGE ATTACHMENT this leg swings about: (x, y, z) in the same body frame as <see cref="Pos"/> (vs the
    /// config datum, +z DOWN). null/empty = no linkage modelled, and the contact point stays put as the leg
    /// compresses, which is what the whole fleet did before 2026-11-10.
    ///
    /// Ty 2026-11-10, on his own J-3 and Pitts: "Pivot at the fuselage attachment and so does the pitts" — "The cub
    /// bungees are in the black leather bags on the gear legs, the pitts bungees are in the belly and the cabana
    /// struts attach to them". The legs are rigid and hinge on the fuselage, so compressing them swings the wheels
    /// along an arc: OUT as well as up, and the track WIDENS. A composite bow (the Extra) bends instead of hinging,
    /// and to first order its tip moves the same way — perpendicular to the leg — so it is authored the same way,
    /// with the pivot at the root.
    ///
    /// Only the y and z matter to the motion (the hinge axis is fore-and-aft); x is authored where the pin is.
    /// Measured off the models, which carry the real linkage: the J-3's and the Great Lakes' GLBs have the hinge
    /// rigged (Gear_Pivot_L/R + Gear_HingeAxis), and the Pitts' and the Extra's leg meshes are measured at the
    /// fuselage. LandingGear.SplayPerM is the rate that falls out, and GearGeometryTests holds each
    /// type to its model.
    /// </summary>
    public double[]? LegPivot { get; set; }
    public string LegPivotSource { get; set; } = "";
    /// <summary>How far this leg can compress before it is on its stop (m at the wheel). 0 = the kind's own
    /// (LandingGear.LegTravelM).</summary>
    public double LegTravelM { get; set; }
    public string LegTravelSource { get; set; } = "";
    // THE TYRE (Ty 2026-11-04, "proper tundra tires"): which tyre is on this leg, by TireCatalog id. Its size, width,
    // masses, inertia, carcass and pressures all come from the catalog — one source of truth — and a config never
    // states a tyre's radius or mass by hand any more (TireCatalogTests holds every config to it).
    public string Tire { get; set; } = "";
    // The tyre the BUSHWHEELS option fits to this leg (a main wheel), by catalog id; "" = the smallest tundra tyre in
    // the catalog rated for the leg's static load (TireFit). Its pressure: TundraPressurePsi, 0 = the tyre's typical.
    public string TundraTire { get; set; } = "";
    public double TundraPressurePsi { get; set; }
    // A SKID's (or a code-built test leg's) radius, where there is no catalog tyre. 0 = auto from fuselage length.
    public double TireRadiusM { get; set; }
    // Overrides of the catalog's rotating mass (tyre, tube, wheel halves and brake disc) and the inertia it derives,
    // for tests and probes only: no config may set them (TireCatalogTests).
    public double WheelMassKg { get; set; }
    public double WheelInertiaKgM2 { get; set; }
    // Operating pressure (psi) of the tyre on this leg. It decides the tyre's spring, its footprint, its ground
    // pressure (sinkage) and when it hydroplanes (V_p kt = 9√p). 0 = the catalog tyre's typical pressure.
    public double TirePressurePsi { get; set; }
    public double BreakSinkMs { get; set; }           // first-contact sink rate that tears this leg off (0 = LandingGear.DefaultBreakSinkMs)
    // Tailwheel (owner 2026-09-12): the wheel CASTORS about its pivot; springs to the rudder pull it toward the
    // commanded angle but always give; past BreakoutRad it swivels free. 0 = LandingGear defaults.
    public double SteerSpringNmPerRad { get; set; }   // steering-spring stiffness (soft: ~60 N·m/rad on a Cub)
    public double SteerDampNms { get; set; }          // castor damping (N·m per rad/s)
    public double CastorTrailM { get; set; }          // contact patch behind the castor pivot (m)
    public double BreakoutRad { get; set; }           // castor angle at which the steering UNLOCKS (free swivel until re-engaged)

    public MathTypes.Vec3 PosVec() => new(Pos[0], Pos[1], Pos[2]);

    // What TireFit took off when the bushwheel option went on, to put back when it comes off. Not data.
    internal bool TundraFitted;
    internal string StockTire = "";
    internal double StockPosZ, StockPressurePsi;
}

/// <summary>A wing with no moving surface at all, flown by swinging the pilot's own mass about the hang point: a hang
/// glider. The control axes carry the SWING ANGLE rather than a deflection, so the offset is h*tan(theta) and the axis
/// limit is the furthest the pilot can actually move, atan(maxShift / hangHeight).
///
/// The moment is the pilot's APPARENT weight at that offset, not his static weight: what he presses the wing with is
/// m_pilot*(g - a), and since gravity is already applied at the cg with no moment, that leaves -(m_pilot/m) times the
/// aerodynamic force. This is why a flex wing's roll authority RISES with load factor -- in a 2 g turn the same body
/// movement gives twice the moment -- which a plain m*g*d model gets wrong.</summary>
public sealed class WeightShiftConfig
{
    public double PilotMassKg { get; set; }
    /// <summary>Hang point above the combined cg (m).</summary>
    public double HangHeightM { get; set; }
    public double MaxLateralM { get; set; }
    public double MaxLongitudinalM { get; set; }

    /// <summary>The moment the pilot makes by swinging, given the swing angles his control axes carry and the
    /// aerodynamic force on the wing. ONE implementation: both the sim's force assembly and the trim solver call
    /// this, because a wing whose pitch control the trim solver did not know about would simply fail to trim --
    /// which is exactly how the first hang glider behaved.
    ///
    /// Signs follow the sim's own conventions: positive roll swing moves the pilot right and rolls right; positive
    /// pitch swing moves him forward and pitches down.</summary>
    public MathTypes.Vec3 SwingMoment(double swingPitchRad, double swingRollRad, MathTypes.Vec3 aeroForceBody, double totalMassKg)
    {
        if (PilotMassKg <= 0.0 || HangHeightM <= 1e-6) return MathTypes.Vec3.Zero;
        MathTypes.Vec3 shift = new(HangHeightM * Math.Tan(swingPitchRad), HangHeightM * Math.Tan(swingRollRad), 0.0);
        // His APPARENT weight, m*(g - a). Gravity is already applied at the cg with no moment, so what is left is
        // -(his share) times the aerodynamic force: nothing in free fall, double in a 2 g turn. That is why a flex
        // wing's roll authority grows with load factor, which a plain m*g*d model gets wrong.
        MathTypes.Vec3 pilotForce = -(PilotMassKg / Math.Max(1e-6, totalMassKg)) * aeroForceBody;
        return MathTypes.Vec3.Cross(shift, pilotForce);
    }
}

/// <summary>
/// WHAT THIS AEROPLANE'S OWN INSTRUMENTS READ. Only the airspeed indicator and the g-meter are in the contract so far;
/// the rest of a modelled panel is still its builder's business (tools/own_models). The faces of the four types with
/// modelled cockpits are drawn from this block by tools/own_models/instruments/build.py, and
/// <c>InstrumentLibraryTests</c> holds the hand-copied numbers in InstrumentLibrary against it, so there is one place.
/// </summary>
public sealed class InstrumentsConfig
{
    /// <summary>The airspeed indicator. Every type in the fleet declares at least its <see
    /// cref="AirspeedInstrumentConfig.Units"/>; only a type with a modelled cockpit declares the whole face.</summary>
    public AirspeedInstrumentConfig? Airspeed { get; set; }
}

/// <summary>
/// THE AIRSPEED INDICATOR'S FACE. <c>units</c> is the fleet-wide decision (Ty 2026-11-10: "vintage, fabric and
/// experimental types read mph; anything certified modern or turbine reads knots") and every type has it. The rest —
/// the scale and the certificated arcs — is published data off a TCDS or a POH, and only the types whose cockpit is
/// modelled have had it authored; <c>FlyingGame.Core.Instruments.AsiDial</c> derives a scale from the spawn speed and
/// <c>limits.vneMs</c> for the others. EVERY NUMBER HERE IS IN <c>units</c>: a face mixing mph arcs with a knots red
/// line is worse than either, which is how the 172's face got to be wrong (its white arc and green floor were a 172M's
/// mph figures, its green top, yellow arc and red line a 172S's knots).
/// </summary>
public sealed class AirspeedInstrumentConfig
{
    /// <summary>"mph" or "kt". Anything else (or absent) reads as knots — see <c>AsiUnits.Parse</c>.</summary>
    public string Units { get; set; } = "kt";

    /// <summary>Bottom and top of the printed scale, in <see cref="Units"/>. 0 = not authored, derive it.</summary>
    public double ScaleMin { get; set; }
    public double ScaleMax { get; set; }

    /// <summary>The certificated range arcs, in <see cref="Units"/>. Null = not authored.</summary>
    public AsiArcsConfig? Arcs { get; set; }

    /// <summary>Vne as it is painted on the glass, in <see cref="Units"/>. 0 = not authored, use
    /// <see cref="LimitsConfig.VneMs"/>. It wins over the converted Vne where both exist, because a TCDS publishes one
    /// number in CAS and that number is what the pilot reads.</summary>
    public double Redline { get; set; }
}

/// <summary>The three range arcs, each <c>[low, high]</c> in the ASI's own units: white = flap operating range,
/// green = normal operating range, yellow = caution range. A missing or short array means that arc is not drawn.</summary>
public sealed class AsiArcsConfig
{
    public double[]? White { get; set; }
    public double[]? Green { get; set; }
    public double[]? Yellow { get; set; }
}

public sealed class LimitsConfig
{
    public double VneMs { get; set; } = 1000;
    /// <summary>LIMIT load factors (g): the loads the structure must carry without permanent deformation.</summary>
    public double GMax { get; set; } = 10;
    public double GMin { get; set; } = -10;
    /// <summary>Where the limit load factors came from (documentation only).</summary>
    public string GMinSource { get; set; } = "";

    /// <summary>Where the airframe starts to COMPLAIN, as a multiple of the limit load (owner 2026-10-31: "make the
    /// wings pop and groan above 1.5 limit load"). Real certification has no such band — a limit load is the load the
    /// structure carries without permanent deformation, full stop — but a game needs to tell the pilot he is pushing
    /// it before it takes the wings off him, and silence up to the limit then an instant failure is no warning at all.
    /// So between the limit and this the airframe is quiet, and from here to <see cref="UltimateFactor"/> it groans
    /// and pops. Exceeding the LIMIT is still an exceedance for scoring: that is what Limits.GMax means and the
    /// syllabus grades against it (Standards.GLimit).</summary>
    public double GroanFactor { get; set; } = 1.5;

    /// <summary>ULTIMATE load = limit × this factor: where the wings actually separate (owner 2026-10-31: "don't have
    /// the wings actually break off until 2.0 limit load"). FAR 23.303 / 25.303 puts real ultimate at 1.5 × limit;
    /// this is deliberately more forgiving than the certification basis, so a pilot who overstresses it gets a band
    /// of noise to react in. Optional, schema-safe default.</summary>
    public double UltimateFactor { get; set; } = 2.0;

    // ---- The rest of a sailplane's flight envelope (Grob G 103, 2026-11-01). All optional: a type that sets none of
    // ---- these has exactly the fixed GMax/GMin it always had.

    /// <summary>VA, the manoeuvring speed (IAS, m/s) up to which <see cref="GMax"/>/<see cref="GMin"/> hold. 0 = they hold
    /// all the way to VNE.</summary>
    public double VaMs { get; set; }
    /// <summary>Limit load factors at VNE; 0 = the same as at VA. Between VA and VNE the limit is the straight line a
    /// flight manual's two quoted points imply (CS 22.333's flight envelope, A to D and G to E).</summary>
    public double GMaxAtVne { get; set; }
    public double GMinAtVne { get; set; }
    /// <summary>VNE (IAS, m/s) against pressure altitude (m), ascending rows [altitude, vne]; empty = <see cref="VneMs"/>
    /// at every height. Sailplane VNE falls with altitude to keep the TRUE airspeed below the flutter speed.</summary>
    public double[][] VneByAltitude { get; set; } = Array.Empty<double[]>();
    /// <summary>The certificated maximum mass of the NON-LIFTING PARTS -- fuselage, tail, occupants, ballast: everything
    /// the wings carry. Wing-root bending is n x that weight, so the limit load factors hold only up to it, and above it
    /// the same bending moment arrives at a proportionally lower n. 0 = not stated.</summary>
    public double MaxNonLiftingMassKg { get; set; }
    /// <summary>The mass of the lifting parts (the wings), to subtract from the total to get the non-lifting mass.</summary>
    public double LiftingMassKg { get; set; }
    /// <summary>Limit load factors with the airbrakes out (CS 22.345: -1.5 to +3.5). 0/0 = no separate limit.</summary>
    public double AirbrakeGMax { get; set; }
    public double AirbrakeGMin { get; set; }
    /// <summary>Other certificated categories this airframe may be flown in (e.g. "utility" beside the "acrobatic"
    /// figures at the top level). <see cref="Category"/> picks one; empty = the top-level figures.</summary>
    public List<LoadCategoryConfig> Categories { get; set; } = new();
    public string Category { get; set; } = "";

    /// <summary>The category being flown: the named one, or the top-level figures.</summary>
    public LoadCategoryConfig Active()
    {
        if (!string.IsNullOrEmpty(Category))
            foreach (LoadCategoryConfig c in Categories)
                if (string.Equals(c.Name, Category, StringComparison.OrdinalIgnoreCase)) return c;
        return new LoadCategoryConfig { Name = "", VaMs = VaMs, GMax = GMax, GMin = GMin, GMaxAtVne = GMaxAtVne, GMinAtVne = GMinAtVne };
    }

    /// <summary>VNE (IAS, m/s) at this pressure altitude.</summary>
    public double VneAt(double altitudeM)
    {
        if (VneByAltitude == null || VneByAltitude.Length == 0) return VneMs;
        if (altitudeM <= VneByAltitude[0][0]) return VneByAltitude[0][1];
        for (int i = 0; i < VneByAltitude.Length - 1; i++)
        {
            double[] a = VneByAltitude[i], b = VneByAltitude[i + 1];
            if (altitudeM <= b[0]) return a[1] + (b[1] - a[1]) * (altitudeM - a[0]) / (b[0] - a[0]);
        }
        return VneByAltitude[^1][1];
    }

    /// <summary>How much of the limit load factor the wings can give at this total mass: 1 up to the certificated
    /// non-lifting mass, MaxNonLifting / (mass - wings) above it (the same root bending moment).</summary>
    public double NonLiftingFactor(double massKg)
    {
        if (MaxNonLiftingMassKg <= 0.0) return 1.0;
        double nonLifting = massKg - LiftingMassKg;
        return nonLifting > MaxNonLiftingMassKg ? MaxNonLiftingMassKg / nonLifting : 1.0;
    }

    /// <summary>
    /// How heavily the WINGS (and the horizontal tail) are loaded against what their limit load factors were rated for,
    /// as a multiple (Ty 2026-11-05 overstress pattern: "this accounts for actual weight so it could be higher than
    /// published if flying light weight"): the wing root bending is n x the weight it carries, so the same g at half the
    /// weight is half the bending. A type that states its certificated non-lifting mass (a sailplane) is measured on that:
    /// (m - wings) / max non-lifting. Otherwise m / gross, the gross being the placard's maximum take-off mass, else the
    /// config's authored mass (which is the gross for every type that has no placard). 1 = exactly at gross.
    /// </summary>
    public double WingWeightRatio(double massKg, double grossKg)
    {
        if (MaxNonLiftingMassKg > 0.0) return Math.Max(0.0, massKg - LiftingMassKg) / MaxNonLiftingMassKg;
        return grossKg > 1e-9 ? massKg / grossKg : 1.0;
    }

    /// <summary>The active category's limit load factors at or below VA, with no airbrake and no weight adjustment: the
    /// published numbers the overstress pattern is measured against.</summary>
    public (double Pos, double Neg) PublishedLimits()
    {
        LoadCategoryConfig c = Active();
        return (c.GMax > 0.0 ? c.GMax : 10.0, c.GMin < 0.0 ? c.GMin : -10.0);
    }

    /// <summary>The limit load factors (positive, negative) at this indicated airspeed, airbrake deflection and total
    /// mass: the active category's envelope, the airbrake envelope when they are out, and the wing-bending mass limit.</summary>
    public (double Pos, double Neg) LimitAt(double iasMs, double spoilerFraction, double massKg)
    {
        LoadCategoryConfig c = Active();
        double pos = c.GMax, neg = c.GMin;
        if (c.VaMs > 0.0 && VneMs > c.VaMs && iasMs > c.VaMs)
        {
            double t = Math.Clamp((iasMs - c.VaMs) / (VneMs - c.VaMs), 0.0, 1.0);
            pos += t * ((c.GMaxAtVne > 0.0 ? c.GMaxAtVne : c.GMax) - c.GMax);
            neg += t * ((c.GMinAtVne < 0.0 ? c.GMinAtVne : c.GMin) - c.GMin);
        }
        if (spoilerFraction > 0.02 && AirbrakeGMax > 0.0)
        {
            pos = Math.Min(pos, AirbrakeGMax);
            neg = Math.Max(neg, AirbrakeGMin);
        }
        double f = NonLiftingFactor(massKg);
        return (pos * f, neg * f);
    }
}

/// <summary>One certificated category's manoeuvring envelope (see <see cref="LimitsConfig.Categories"/>).</summary>
public sealed class LoadCategoryConfig
{
    public string Name { get; set; } = "";
    public double VaMs { get; set; }
    public double GMax { get; set; }
    public double GMin { get; set; }
    public double GMaxAtVne { get; set; }
    public double GMinAtVne { get; set; }
}

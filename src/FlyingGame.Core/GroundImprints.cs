using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// What kind of mark a contact left. Ty 2026-11-10: "if it is penetrating it should leave imprints behind as it slides
/// and rolls displacing the dirt or snow etc" — he named rolling and sliding separately, and they do not look alike.
/// APPEND ONLY: a renderer may index a material or a mesh table by it.
/// </summary>
public enum GroundImprintKind
{
    /// <summary>A ROLLING tyre: a rut of the footprint's width, the tread pressed into it, the ground compacted straight
    /// down. The ordinary mark of a landing roll and a taxi.</summary>
    Rut,
    /// <summary>A tyre that is NOT rolling with the ground: braked to a lock, or scrubbing sideways in a turn or a crabbed
    /// touchdown. The rubber slides over the surface instead of laying its tread into it, so it smears the ground along
    /// the slip rather than compacting it: on soft ground a shallow scrape with a shoulder of thrown material, on
    /// pavement the black streak (<see cref="GroundMaterialRow.SkidMark"/>).</summary>
    Skid,
    /// <summary>A ski or a float bottom: one broad, smooth, flat-bottomed track the width of the running surface. A ski's
    /// whole purpose is flotation, so it is the widest and shallowest mark anything leaves.</summary>
    Track,
    /// <summary>A small hard part of the airframe dragging: a wingtip bow, a tailskid, a fin tip, a gear stub, a
    /// propeller tip. Narrow, deep for its width, and it tears the surface open rather than compacting it.</summary>
    Gouge,
    /// <summary>A large structure sliding on the ground: a belly, a nose, a whole wing panel — a wreck ploughing down a
    /// hillside. Broad and torn. Same mechanism as a <see cref="Gouge"/>; what makes it broad is the load spread over a
    /// structure's breadth instead of a tip's.</summary>
    Scar,
}

/// <summary>
/// One mark in the ground: a straight stretch of rut, skid, track, gouge or scar, between two points on the surface.
/// A curving track is a chain of these. Immutable once closed; the renderer draws each as one quad lying on the
/// surface, <see cref="WidthM"/> across and <see cref="DepthM"/> into it.
///
/// All vectors are world NED (z down), as every other sim position is; Unity converts them as it converts those.
/// </summary>
public readonly struct GroundImprint
{
    /// <summary>Where the stretch begins and ends: the contact point on the SURFACE, not the part's centre.</summary>
    public readonly Vec3 StartWorld, EndWorld;
    /// <summary>Across the direction of travel, m: the trough's width AT THE SURFACE (<see cref="GroundImprints.TroughWidthM"/>),
    /// which is wider than the contact patch because the sides of a hollow slump to the ground's angle of repose.</summary>
    public readonly double WidthM;
    /// <summary>How far below the undisturbed surface the bottom of the mark lies, m: the part of the contact's
    /// penetration that the ground did NOT give back (<see cref="GroundImprints.ScarDepthM"/>).</summary>
    public readonly double DepthM;
    public readonly GroundImprintKind Kind;
    /// <summary>What was marked — the same key the physics and the sound use (<see cref="GroundMaterials"/>), so the
    /// imagery of a mark cannot disagree with the ground the aeroplane was rolling on.</summary>
    public readonly GroundMaterial Material;
    /// <summary>Sim time the stretch was LAST extended (<see cref="Atmosphere.SimTimeSec"/>), s: how old the mark is, for
    /// a renderer that wants to weather it. Nothing in this record fades — see <see cref="GroundImprints"/>.</summary>
    public readonly double TimeSec;
    /// <summary>Which aeroplane laid it (<see cref="GroundImprintPen.Owner"/>), so a renderer can tell one aeroplane's
    /// tracks from another's in a formation; −1 if unattributed.</summary>
    public readonly int Owner;

    public GroundImprint(Vec3 startWorld, Vec3 endWorld, double widthM, double depthM, GroundImprintKind kind,
        GroundMaterial material, double timeSec, int owner)
    {
        StartWorld = startWorld; EndWorld = endWorld; WidthM = widthM; DepthM = depthM;
        Kind = kind; Material = material; TimeSec = timeSec; Owner = owner;
    }

    public double LengthM => (EndWorld - StartWorld).Length;
    /// <summary>Unit vector along the stretch (the lie of the mark); zero for a stretch of no length.</summary>
    public Vec3 AlongWorld { get { Vec3 d = EndWorld - StartWorld; double l = d.Length; return l > 1e-9 ? d / l : Vec3.Zero; } }

    public override string ToString() =>
        $"{Kind} {Material} {LengthM:F2} m x {WidthM:F3} m, {100 * DepthM:F1} cm deep";
}

/// <summary>
/// The marks the ground keeps, and the soil mechanics that decides them.
///
/// Ty 2026-11-10: *"if it is penetrating it should leave imprints behind as it slides and rolls displacing the dirt or
/// snow etc"*, and on how deep: *"I want the penetration to show and stay as ground scars if it is going to be there
/// and I can understand several cm on initial impact but just rolling down the hill it should be a few cm not a dozen
/// or more"*.
///
/// That is a statement about PLASTICITY. The aeroplane presses into soft ground, the ground yields, and when the load
/// moves on the hollow is still there. So a mark is not invented from the load: it is the penetration the contact
/// physics already has, less the part the ground gives back.
///
/// ## Where each number comes from
///
/// **DEPTH** is read from the contact, never recomputed. Two paths, because the two contacts measure different things,
/// and getting this wrong is how two models end up disagreeing about one rut:
/// - A TYRE's depth is <see cref="TireModel.GroundSinkageM"/> — Bekker's pressure-sinkage compaction, inverted out of
///   the rolling resistance the aeroplane is already being charged for. A plate-bearing sinkage is by definition the
///   PERMANENT set of the soil, so it is already the rut: it is taken as it stands, with no retention applied.
/// - An AIRFRAME hard point's depth is <see cref="ContactPoint.DepthM"/>, the penetration of the contact spring. That
///   is an elastic deflection by construction — let the load off and it returns — so only
///   <see cref="PlasticRetention01"/> of it stays as a scar.
///
/// **WIDTH** is not in either contact (a <see cref="ContactPoint"/> has no breadth; a hard point is a point), so it
/// comes from the ground's BEARING CAPACITY, which is the one property that says how much surface a load needs:
/// <see cref="BearingPressurePa"/>. A load N yields a patch of area N/p, taken square, so the patch is √(N/p) across.
/// A tyre, ski or float measures its own footprint and uses it instead. Either way the trough at the SURFACE is wider
/// than the patch at the bottom, because the sides of an open hollow slump back to the ground's angle of repose
/// (<see cref="ReposeAngleDeg"/>): <see cref="TroughWidthM"/>.
///
/// **WHETHER IT MARKS AT ALL** falls out of the same two numbers rather than being a separate switch. Asphalt's
/// retention is 0 and its bearing pressure is megapascals, so a Pitts on its belly leaves nothing; snow's retention is
/// 0.95 at 60 kPa, so a ski track is broad and stays.
///
/// ## What is NOT here
/// - **Fading.** A rut in soft ground does not fade on the timescale of a flight, and Ty asked for marks that "stay".
///   Marks go only when the ring runs out (oldest first). <see cref="GroundImprint.TimeSec"/> is recorded so a
///   renderer CAN weather them, and so the record is complete; nothing here reads it.
/// - **Mud.** <see cref="GroundMaterial"/> has no mud and no world surface produces one — still open (surface-tracks
///   Q145, unanswered since 2026-11-06). When a mud surface exists it is three numbers in the tables below: a mud
///   bearing pressure near 50 kPa, retention ~1.0 and a repose angle near 25°, which would give the deepest, widest,
///   longest-lasting mark of anything on the map. Until then wet grass and dirt are what the island has.
/// - **Particles.** A standing prohibition of Ty's: no float spray, no thrown dirt. A mark is geometry in the ground.
/// </summary>
public static class GroundImprints
{
    /// <summary>
    /// ULTIMATE BEARING PRESSURE, Pa: the pressure the surface carries before it yields and takes a set. It sets how
    /// much area a load needs, hence the width of the mark, and it is why hard surfaces do not mark.
    ///
    /// Presumptive ALLOWABLE bearing values are what building codes tabulate (IBC Table 1806.2: sandy gravel or gravel
    /// 3000 psf = 144 kPa; sand, silty sand, clayey sand, silty or clayey gravel 2000 psf = 96 kPa; clay, sandy clay,
    /// silty clay 1500 psf = 72 kPa). Those carry a factor of safety near 3, so the ULTIMATE values below are about
    /// three times them. They are shallow-footing numbers, which is the right class of measurement: a tyre, a ski and a
    /// wingtip are all shallow footings a few centimetres into the top of the soil.
    /// </summary>
    public static double BearingPressurePa(GroundMaterial m) => m switch
    {
        // Asphalt concrete. An airport flexible pavement is DESIGNED to take tyre pressures of 0.7-1.4 MPa with no
        // permanent deformation; the mix's own unconfined compressive strength at 20 C is 3-5 MPa. 3 MPa: nothing an
        // aeroplane can put on it yields it. Retention is 0 as well, so it never marks either way.
        GroundMaterial.Asphalt => 3.0e6,
        // Steel deck. Not soil at all; here so the table is total.
        GroundMaterial.Deck => 2.0e8,
        // Glacier ice crushes at 5-25 MPa (Petrovic 2003, J. Mater. Sci. 38 — the same source WorldTerrain's rolling
        // coefficient cites for ice being rigid under a tyre). The bottom of that range, for the porous summer
        // weathering crust rather than clean ice.
        GroundMaterial.Ice => 5.0e6,
        // Compacted gravel: IBC 144 kPa allowable x 3. The firmest unpaved ground on the map.
        GroundMaterial.Gravel => 430e3,
        // Dry firm packed earth, as a dirt strip is: IBC's silty/clayey sand class, 96 kPa allowable x ~2.6.
        GroundMaterial.Dirt => 250e3,
        // Hard wet-packed beach sand. Softer than the dirt strips in this model already: WorldTerrain gives sand a
        // rolling coefficient of 0.08 against dirt's 0.06, and the Bekker sinkage that comes out of that puts a Cub at
        // 10 mm in sand against 3.3 mm in dirt (docs/SURFACE-CONTACTS.md). 150 kPa keeps the width in the same order.
        GroundMaterial.Sand => 150e3,
        // Turf: a root mat over topsoil. The mat spreads the load, which is exactly why a grass strip takes an
        // aeroplane that a bare field of the same soil would bog. Plate-bearing and cone-penetrometer surveys of
        // sports turf run 150-400 kPa; 200 kPa for a landing strip's mown grass.
        GroundMaterial.Grass => 200e3,
        // ROUGH is the island's green ground off the strips: pasture, grass over uneven earth (WorldTerrain.Surface.Rough).
        // Less mat than a mown strip and softer underneath.
        GroundMaterial.Rough => 180e3,
        // Settled seasonal snow, density near 300 kg/m3. Snow's bearing strength is all density: ram-hardness and
        // plate-bearing work on settled snow of that density gives 30-100 kPa. 60 kPa. The existing model agrees in
        // spirit: a Cub's tyre sinks 34 mm in it, ten times its rut in dirt.
        GroundMaterial.Snow => 60e3,
        // Wind-packed, graded glacier snow (density 450-550). Much stronger, but the crust is THIN, which is why
        // WorldTerrain makes it draggier for a WHEEL than the soft snow it sits on: the wheel breaks through and
        // ploughs. 200 kPa is the crust; what happens once it is through is in the penetration, not here.
        GroundMaterial.PackedSnow => 200e3,
        // Water keeps no mark (the wake is SprayField's business, and it is not geometry in the ground).
        GroundMaterial.Water => 0.0,
        _ => 180e3,
    };

    /// <summary>
    /// How much of an ELASTIC contact penetration stays behind as a hollow, 0-1. This is plasticity, which is a
    /// different property from strength: asphalt is strong AND springy, snow is weak AND keeps every millimetre.
    ///
    /// Only an airframe hard point's depth is multiplied by this. A tyre's Bekker sinkage is a permanent set already
    /// (see the class remarks) and is taken as it stands.
    /// </summary>
    public static double PlasticRetention01(GroundMaterial m) => m switch
    {
        // Asphalt is viscoelastic: at the loads and the seconds involved it recovers. A light aeroplane does not rut a
        // runway. Zero is the whole reason a Pitts sliding down a paved runway leaves a black streak and no hollow.
        GroundMaterial.Asphalt => 0.0,
        GroundMaterial.Deck => 0.0,
        GroundMaterial.Water => 0.0,
        // Ice is elastic to its crushing stress and then brittle. What a part leaves on it is SCORING and crushed
        // grains — a change in the surface's texture, not a hollow in it — and this record is geometry: a depth and a
        // width. Zero, therefore, rather than a small number that would have the renderer dig a trench in a glacier.
        // Scoring would be a mark with no depth, and nothing draws one yet; if it should, that is a new kind.
        GroundMaterial.Ice => 0.0,
        // Turf: the root mat springs back, the soil under it does not. Divot and tyre-rut work on turf finds most of
        // the instantaneous deflection recovered within seconds and the rest permanent. A third of it stays.
        GroundMaterial.Grass => 0.35,
        // Pasture: less mat to spring back, more soft earth to take the set.
        GroundMaterial.Rough => 0.50,
        // Firm earth, a cohesive soil: it yields plastically with little rebound.
        GroundMaterial.Dirt => 0.80,
        // Granular: the stones rearrange and stay rearranged.
        GroundMaterial.Gravel => 0.85,
        // Damp sand holds the shape of a footprint essentially exactly. This is the surface Ty would see his own
        // tracks in most plainly.
        GroundMaterial.Sand => 0.90,
        // Snow compaction is irreversible at the timescale of a flight. A ski track is as deep as the ski went.
        GroundMaterial.Snow => 0.95,
        // Packed snow: harder, so it penetrates less in the first place, but the crust that breaks stays broken.
        GroundMaterial.PackedSnow => 0.90,
        _ => 0.50,
    };

    /// <summary>
    /// ANGLE OF REPOSE of the surface material, degrees: the steepest the side of an open hollow stands before it
    /// slumps. It is what makes a deep mark wider at the top than at the bottom (<see cref="TroughWidthM"/>).
    ///
    /// Published values: dry sand 30-35, gravel and crushed stone 35-40, firm earth and loam 35-40, settled snow 35-40
    /// (snow's is high and rises as it sinters, which is why a ski track keeps a clean edge). A hard surface never
    /// opens a hollow, so its value is only there to keep the table total; 90 means no slumping.
    /// </summary>
    public static double ReposeAngleDeg(GroundMaterial m) => m switch
    {
        GroundMaterial.Asphalt or GroundMaterial.Deck or GroundMaterial.Ice => 90.0,
        GroundMaterial.Sand => 32.0,
        GroundMaterial.Gravel => 38.0,
        GroundMaterial.Dirt => 37.0,
        // Turf's root mat holds a near-vertical edge: a tyre rut in grass has a sharp shoulder, it does not slump open.
        GroundMaterial.Grass => 60.0,
        GroundMaterial.Rough => 45.0,
        GroundMaterial.Snow or GroundMaterial.PackedSnow => 38.0,
        GroundMaterial.Water => 90.0,
        _ => 40.0,
    };

    /// <summary>Does this ground keep a mark at all? Asphalt, a deck and water do not; everything else does.</summary>
    public static bool Marks(GroundMaterial m) => PlasticRetention01(m) > 0.0 && BearingPressurePa(m) > 0.0;

    /// <summary>
    /// The shallowest mark worth recording, m. 1 mm.
    ///
    /// REJECTED: 5 mm, on the argument that a shallower hollow is inside the ground's own micro-relief (the finest the
    /// terrain carries is gravel's +-12 mm, <see cref="WorldTerrain.MicroBumpAt"/>). It is the wrong gate, and it was
    /// wrong by a visible amount: the Bekker rut a Cub's 8.00-6 presses into a dirt strip is 3.3 mm and into mown grass
    /// 5.6 mm (docs/SURFACE-CONTACTS.md), so a 5 mm floor erased a Cub's tracks on a dirt strip entirely and left its
    /// tracks on grass hanging on a tenth of a millimetre. And a tyre track in grass plainly shows: what you see is
    /// flattened, bruised turf, not a trench. VISIBILITY IS THE RENDERER'S BUSINESS — the record's job is to say
    /// honestly that the rut is 3 mm deep and let the drawing make that flattened grass rather than a ditch.
    ///
    /// 1 mm is where the ground stops being displaced in any sense the terrain can carry: it is under a blade of grass,
    /// under a grain of sand, and under one part in ten of the finest micro-relief in the world model.
    /// </summary>
    public const double MinDepthM = 0.001;

    /// <summary>The narrowest mark worth recording, m. A mark thinner than this is a scratch, and the renderer cannot
    /// draw a quad a few millimetres across on a 25 m mesh without it vanishing into one texel. 2 cm.</summary>
    public const double MinWidthM = 0.02;

    /// <summary>
    /// The part of a contact penetration that stays as a scar, m. <paramref name="contactDepthM"/> is the ELASTIC
    /// penetration of a contact spring (<see cref="ContactPoint.DepthM"/>); a tyre's or a ski's model sinkage is a
    /// permanent set already and must NOT be passed through here.
    /// </summary>
    public static double ScarDepthM(GroundMaterial m, double contactDepthM) =>
        double.IsFinite(contactDepthM) && contactDepthM > 0 ? PlasticRetention01(m) * contactDepthM : 0.0;

    /// <summary>
    /// The contact patch a load of <paramref name="loadN"/> needs on this ground, m across: the ground yields at
    /// <see cref="BearingPressurePa"/>, so the patch is N/p in area, taken SQUARE (a hard point has no shape of its
    /// own in the contact model, so a square is the one assumption that adds no invented geometry). 0 on ground that
    /// does not yield.
    /// </summary>
    public static double BearingPatchWidthM(GroundMaterial m, double loadN)
    {
        double p = BearingPressurePa(m);
        if (!(p > 0) || !double.IsFinite(loadN) || loadN <= 0) return 0.0;
        return System.Math.Sqrt(loadN / p);
    }

    /// <summary>
    /// The width of the mark AT THE SURFACE, m: the contact patch at the bottom plus the slump of the two sides back to
    /// the angle of repose, w = patch + 2 d / tan(repose). A 3 cm gouge in dirt (repose 37 deg) opens 8 cm wider than
    /// the part that made it; the same gouge in grass (60 deg, held by the root mat) only 3.5 cm wider.
    /// </summary>
    public static double TroughWidthM(GroundMaterial m, double patchWidthM, double depthM)
    {
        if (!(depthM > 0)) return System.Math.Max(0, patchWidthM);
        double t = System.Math.Tan(ReposeAngleDeg(m) * System.Math.PI / 180.0);
        return System.Math.Max(0, patchWidthM) + (t > 1e-6 ? 2.0 * depthM / t : 0.0);
    }

    /// <summary>
    /// Which mark a surface contact leaves. A tyre whose rubber is sliding over the ground faster than
    /// <see cref="SkidSlipMs"/> smears instead of compacting, so it is a <see cref="GroundImprintKind.Skid"/>; a locked
    /// wheel always is.
    /// </summary>
    public static GroundImprintKind KindOf(SurfaceContactType type, double slipMs, double groundSpeedMs, bool locked) => type switch
    {
        SurfaceContactType.Tire => IsSkidding(slipMs, groundSpeedMs, locked) ? GroundImprintKind.Skid : GroundImprintKind.Rut,
        SurfaceContactType.Ski or SurfaceContactType.Float => GroundImprintKind.Track,
        _ => GroundImprintKind.Gouge,
    };

    /// <summary>
    /// Is this tyre's rubber SLIDING over the ground rather than rolling on it? Locked, or slipping past the ratio at
    /// which the tyre has passed its friction peak and begun to slide (<see cref="WheelSpin.FallStart"/>, 0.15 — the
    /// tyre model's own number for where the coefficient starts falling from the peak toward the sliding value;
    /// aircraft tyres peak at 10-20 % slip, NASA TP-2917 and ESDU 71026).
    ///
    /// A FRACTION of the ground speed, not an absolute slip, and that was measured rather than argued: a J-3's mains
    /// are splayed, so rolling straight down a grass strip at 14 m/s each main scrubs sideways at 0.65 m/s — a 2.7 deg
    /// slip angle, an ordinary rolling tyre. Against a flat threshold of 0.3 m/s every Cub main read as a skid for the
    /// whole landing roll, which is both wrong and expensive: the kind flipping between Rut and Skid closes a stretch
    /// and opens another.
    ///
    /// <see cref="SkidSlipMs"/> is the floor, for the standstill case where a ratio means nothing.
    /// </summary>
    public static bool IsSkidding(double slipMs, double groundSpeedMs, bool locked) =>
        locked || slipMs > System.Math.Max(SkidSlipMs, WheelSpin.FallStart * System.Math.Abs(groundSpeedMs));

    /// <summary>
    /// The floor under <see cref="IsSkidding"/>, m/s: below it the contact model is regularised static friction rather
    /// than sliding, so there is nothing to smear the ground with. The same threshold the scrape sound and the dust
    /// already use (<see cref="ScrapeState.SlipThresholdMs"/>).
    /// </summary>
    public const double SkidSlipMs = ScrapeState.SlipThresholdMs;

    /// <summary>
    /// A large structure sliding leaves a broad <see cref="GroundImprintKind.Scar"/>; a small hard part leaves a narrow
    /// <see cref="GroundImprintKind.Gouge"/>. The difference in the mark's WIDTH comes from the load through
    /// <see cref="BearingPatchWidthM"/> and is not decided here — this only says which of the two a renderer should
    /// draw, and the line is whether the part has a structure's breadth or a tip's.
    /// </summary>
    public static GroundImprintKind KindOf(AirframeComponent c) => c switch
    {
        AirframeComponent.Fuselage or AirframeComponent.Nose or AirframeComponent.Cabin or AirframeComponent.TailBoom
            or AirframeComponent.WingLeft or AirframeComponent.WingRight
            or AirframeComponent.WingLeftInner or AirframeComponent.WingRightInner
            or AirframeComponent.WingLeftOuter or AirframeComponent.WingRightOuter => GroundImprintKind.Scar,
        _ => GroundImprintKind.Gouge,
    };

    /// <summary>
    /// The marks of the world the aeroplanes are flying in. Process-wide, like <see cref="StrippedTrees"/> and
    /// <see cref="Forest"/>'s snapped tops, and cleared with them for a new flight (<see cref="Clear"/>): every
    /// aeroplane, player and AI, writes into one field, so you can see where the other one landed.
    /// </summary>
    public static readonly GroundImprintField World = new(GroundImprintField.WorldBudget);

    /// <summary>Forget every mark (a new flight, a new world, a test).</summary>
    public static void Clear() => World.Clear();
}

/// <summary>
/// A fixed ring of <see cref="GroundImprint"/>s: the marks a world keeps. Nothing is allocated after construction, and
/// when it is full the OLDEST mark goes (a landing roll that fills the ring erases the taxi out, which is the order a
/// pilot cares about least to most).
///
/// ## The budget, and what it costs
/// <see cref="WorldBudget"/> is 1,024 stretches. That is the whole world, every aeroplane together — the precedent is
/// <see cref="SprayField.MaxParcels"/> at 600, set as an "iPhone / Quest budget".
///
/// - **Memory:** a <see cref="GroundImprint"/> is 2 Vec3 (48 B) + 3 doubles (24 B) + 2 enums + an int (16 B, padded):
///   88 B. 1,024 of them is 88 kB, allocated once.
/// - **Drawing:** one quad each, so 2,048 triangles for every mark in the world. A single detail tree in ForestBuilder
///   costs more than that.
/// - **Per step:** one append-or-extend per contact in contact, which is at most a dozen per aeroplane (every gear leg
///   plus whatever airframe points are touching). Each is a handful of dot products, no allocation, no dictionary and
///   no terrain query — deliberately, after the J-3 was found 8.8x too expensive on 2026-11-09 with a
///   ConditionalWeakTable in the hot path.
///
/// ## How 1,024 lasts a circuit's worth of landings
/// A stretch is EXTENDED while the contact keeps going the same way at much the same width and depth
/// (<see cref="StraightTolerance"/>, <see cref="SizeTolerance"/>), so a straight rut costs ONE stretch however long it
/// is, and only a wander, a turn, a change of surface or a change of depth starts a new one.
///
/// MEASURED (GroundImprintTests): a J-3 put down at 14 m/s on grass and left entirely alone — no rudder, tailwheel
/// castoring — rolls 58 m to a stop and lays 135 stretches on three wheels. Almost none of that is the LENGTH of the
/// roll: while it is rolling the stretches run 2-4 m each, set by the arc the aeroplane is describing because nothing
/// is steering it, and the rest is the slow weave as it comes to rest. A roll held STRAIGHT is one stretch per wheel
/// however long it is. So 1,024 holds about seven landing rolls of that kind, and effectively unlimited straight
/// rolling — a circuit's worth of landings and the taxiing between them, which is what Ty asked to be able to see.
///
/// A GROUND LOOP is the expensive case, because it is all curve; that is the right place for the budget to be spent,
/// and the oldest marks are the ones that go.
/// </summary>
public sealed class GroundImprintField
{
    /// <summary>The whole world's budget. See the class remarks for what it costs and why it is enough.</summary>
    public const int WorldBudget = 1024;

    /// <summary>
    /// How far a contact may stray from the open stretch's line before the stretch is closed and a new one begun, as a
    /// fraction of the mark's own WIDTH, with <see cref="StraightFloorM"/> as a floor. It is this tolerance, not a
    /// length cap, that breaks a stretch — and the test is in 3D, so the ground's undulation breaks one as surely as a
    /// turn does, and a flat quad never stands off the surface by more than this.
    ///
    /// Relative to the width, because that is what the eye judges: a 20 cm rut cutting 6 cm off a corner is a third of
    /// its own width out of place and reads as wrong, while the same 6 cm on a metre-wide wreck scar is nothing.
    ///
    /// 0.3 was MEASURED, not chosen: a J-3 rolling out 68 m on grass with the tailwheel castoring wanders about 5 cm
    /// laterally every metre and a half, and at a flat 5 cm tolerance that cost 0.7 stretches per metre — one 200 m
    /// landing roll on three wheels would have been 420 of the 1,024. At 0.3 of the width it is well under half that,
    /// and the chord error stays inside the rut.
    /// </summary>
    public const double StraightTolerance = 0.3;

    /// <summary>The floor under <see cref="StraightTolerance"/>, m: a narrow gouge still gets 4 cm of latitude, which is
    /// under the 25 m terrain mesh's own resolution and so cannot be seen as a kink.</summary>
    public const double StraightFloorM = 0.04;

    /// <summary>
    /// How much the width or the depth may change within one stretch, as a fraction. Beyond it the stretch is closed, so
    /// a wheel that sinks deeper as the strip softens lays a chain of progressively deeper ruts rather than one rut of
    /// an averaged depth. 25 %: a quarter is about the smallest change in a mark's size that reads on screen.
    ///
    /// A fraction ALONE was measured to be wrong, and expensively so: a braked Cub on a dirt strip laid dozens of
    /// zero-length stretches because its 3 mm rut deepened 3 mm -> 4 mm between two steps, which is 33 %, so the
    /// stretch was closed and reopened every step. A quarter of nothing is nothing. The tolerance therefore has an
    /// ABSOLUTE floor as well (<see cref="GroundImprints.MinDepthM"/> for depth, half
    /// <see cref="GroundImprints.MinWidthM"/> for width — a millimetre and a centimetre, the smallest differences this
    /// record claims to resolve at all), and a change under that floor never breaks a stretch.
    /// </summary>
    public const double SizeTolerance = 0.25;

    private readonly GroundImprint[] _items;
    private int _next;          // where the next new stretch goes (the ring's write head)
    private int _version;

    public GroundImprintField(int budget) => _items = new GroundImprint[System.Math.Max(1, budget)];

    public int Count { get; private set; }
    public int Capacity => _items.Length;
    /// <summary>How many stretches have been pushed out of the ring since <see cref="Clear"/>: the budget's own report.</summary>
    public long Evicted { get; private set; }

    /// <summary>Changes whenever any mark is added or extended: a renderer rebuilds then, and only then.</summary>
    public int Version => System.Threading.Volatile.Read(ref _version);

    /// <summary>The marks, oldest-to-newest order NOT guaranteed (the ring overwrites in place).</summary>
    public ref readonly GroundImprint this[int i]
    {
        get
        {
            if ((uint)i >= (uint)Count) throw new System.ArgumentOutOfRangeException(nameof(i));
            return ref _items[i];
        }
    }

    public void Clear()
    {
        if (Count == 0 && Evicted == 0) return;
        System.Array.Clear(_items, 0, _items.Length);
        Count = 0; _next = 0; Evicted = 0;
        System.Threading.Interlocked.Increment(ref _version);
    }

    /// <summary>Add a stretch, evicting the oldest if the ring is full. Returns its index, so a pen can extend it.</summary>
    internal int Add(in GroundImprint k)
    {
        int at = _next;
        if (Count < _items.Length) Count++; else Evicted++;
        _items[at] = k;
        _next = (at + 1) % _items.Length;
        System.Threading.Interlocked.Increment(ref _version);
        return at;
    }

    /// <summary>Replace a stretch in place (a pen extending its open one).</summary>
    internal void Set(int at, in GroundImprint k)
    {
        _items[at] = k;
        System.Threading.Interlocked.Increment(ref _version);
    }

    /// <summary>
    /// Is the stretch at <paramref name="at"/> still the one a pen put there? False once the ring has come round and
    /// overwritten it, which is how a pen notices its open stretch has been evicted and starts a fresh one.
    ///
    /// Owner, last-touched time AND start point, all three. Owner alone is not enough: one aeroplane has a pen with
    /// many tracks, and at ring saturation one of its own tracks can overwrite another's slot. Owner and time are not
    /// enough either — two of its tracks laid on the same step share a time — so the start point settles it, and two
    /// parts of one aeroplane are never at the same point.
    /// </summary>
    internal bool StillHolds(int at, double timeSec, int owner, Vec3 startWorld) =>
        (uint)at < (uint)Count && _items[at].Owner == owner && _items[at].TimeSec == timeSec
        && _items[at].StartWorld.X == startWorld.X && _items[at].StartWorld.Y == startWorld.Y;
}

/// <summary>
/// One aeroplane's pen: it holds the stretch each of its parts is currently laying and extends it while the part keeps
/// going the same way, so a 200 m straight rut costs one stretch in the field instead of four hundred.
///
/// One per aeroplane, sized once from the config (a slot per gear leg and a slot per airframe contact point), so there
/// is no allocation and no hashing on the step path.
/// </summary>
public sealed class GroundImprintPen
{
    private struct Track
    {
        public bool Open;
        public int At;                  // index in the field
        public Vec3 Start, End;
        public Vec3 Along;              // unit, the stretch's direction, fixed at the second point
        public double WidthM, DepthM, TimeSec;
        public GroundImprintKind Kind;
        public GroundMaterial Material;
    }

    private readonly GroundImprintField _into;
    private readonly Track[] _tracks;
    private readonly bool[] _seen;

    /// <summary>Which aeroplane this is, recorded on every mark it lays (<see cref="GroundImprint.Owner"/>).</summary>
    public int Owner { get; }

    /// <summary>How many stretches this pen has opened (not extensions): what a test counts to check the budget.</summary>
    public long Opened { get; private set; }

    public GroundImprintPen(GroundImprintField into, int trackCount, int owner)
    {
        _into = into;
        _tracks = new Track[System.Math.Max(1, trackCount)];
        _seen = new bool[_tracks.Length];
        Owner = owner;
    }

    public int TrackCount => _tracks.Length;

    /// <summary>Begin a step: nothing has been seen in contact yet.</summary>
    public void BeginStep() => System.Array.Clear(_seen, 0, _seen.Length);

    /// <summary>
    /// End a step: every track that was NOT laid this step had its part leave the ground, so its open stretch is
    /// finished. A finished stretch stays in the field exactly as it is; closing only means this pen stops extending it.
    /// </summary>
    public void EndStep()
    {
        for (int i = 0; i < _tracks.Length; i++) if (!_seen[i] && _tracks[i].Open) _tracks[i].Open = false;
    }

    /// <summary>Forget every open stretch (a new flight; the marks themselves are the field's business).</summary>
    public void Reset()
    {
        System.Array.Clear(_tracks, 0, _tracks.Length);
        System.Array.Clear(_seen, 0, _seen.Length);
        Opened = 0;
    }

    /// <summary>
    /// Lay a mark for one part this step, at <paramref name="atWorld"/> on the surface.
    /// <paramref name="patchWidthM"/> is the part's own contact patch across the direction of travel (a tyre's
    /// footprint, a ski's running surface) or 0 to let the ground's bearing capacity size it from
    /// <paramref name="loadN"/>; <paramref name="scarDepthM"/> is the depth that STAYS (see
    /// <see cref="GroundImprints.ScarDepthM"/> — the caller decides, because a tyre's sinkage and a hard point's
    /// penetration are different measurements).
    ///
    /// Returns true if anything was recorded. Nothing is recorded on ground that does not keep a mark, or for a mark
    /// too shallow or too narrow to see.
    /// </summary>
    public bool Lay(int track, GroundImprintKind kind, GroundMaterial material, Vec3 atWorld,
        double patchWidthM, double scarDepthM, double loadN, double timeSec)
    {
        if ((uint)track >= (uint)_tracks.Length) return false;
        _seen[track] = true;
        if (!GroundImprints.Marks(material)) { _tracks[track].Open = false; return false; }
        if (!(scarDepthM >= GroundImprints.MinDepthM) || !double.IsFinite(scarDepthM)) { _tracks[track].Open = false; return false; }
        double patch = patchWidthM > 0 ? patchWidthM : GroundImprints.BearingPatchWidthM(material, loadN);
        double width = GroundImprints.TroughWidthM(material, patch, scarDepthM);
        if (!(width >= GroundImprints.MinWidthM) || !double.IsFinite(width)) { _tracks[track].Open = false; return false; }
        if (!double.IsFinite(atWorld.X) || !double.IsFinite(atWorld.Y) || !double.IsFinite(atWorld.Z))
        { _tracks[track].Open = false; return false; }

        ref Track t = ref _tracks[track];
        // An open stretch this pen laid may have been pushed out of the ring by newer marks; it is not ours to extend
        // any more (StillHolds), and the aeroplane simply starts a fresh one where it is.
        bool continuing = t.Open && t.Kind == kind && t.Material == material
                       && _into.StillHolds(t.At, t.TimeSec, Owner, t.Start)
                       && WithinSize(t.WidthM, width, 0.5 * GroundImprints.MinWidthM)
                       && WithinSize(t.DepthM, scarDepthM, GroundImprints.MinDepthM)
                       && StraightEnough(in t, atWorld);
        if (continuing)
        {
            t.End = atWorld;
            // The deepest and widest the stretch got is what it reads as: a rut you rolled into and out of is as deep
            // as the deepest part of it, not as deep as where it happened to end.
            t.WidthM = System.Math.Max(t.WidthM, width);
            t.DepthM = System.Math.Max(t.DepthM, scarDepthM);
            t.TimeSec = timeSec;
            if (t.Along.LengthSquared < 0.5)
            {
                Vec3 d = t.End - t.Start;
                double l = d.Length;
                if (l > 1e-6) t.Along = d / l;
            }
            _into.Set(t.At, new GroundImprint(t.Start, t.End, t.WidthM, t.DepthM, kind, material, timeSec, Owner));
            return true;
        }

        t.Open = true;
        t.Start = t.End = atWorld;
        t.Along = Vec3.Zero;          // no direction until the stretch has two distinct points
        t.WidthM = width; t.DepthM = scarDepthM; t.TimeSec = timeSec;
        t.Kind = kind; t.Material = material;
        t.At = _into.Add(new GroundImprint(atWorld, atWorld, width, scarDepthM, kind, material, timeSec, Owner));
        Opened++;
        return true;
    }

    /// <summary>The part left the ground (or its leg broke off): stop extending its stretch.</summary>
    public void Lift(int track)
    {
        if ((uint)track < (uint)_tracks.Length) { _seen[track] = true; _tracks[track].Open = false; }
    }

    private static bool WithinSize(double was, double now, double absoluteSlack) =>
        System.Math.Abs(now - was) <= System.Math.Max(absoluteSlack, GroundImprintField.SizeTolerance * System.Math.Max(0, was));

    /// <summary>Is <paramref name="at"/> still on the open stretch's line, within
    /// <see cref="GroundImprintField.StraightTolerance"/> of the mark's own width (floor
    /// <see cref="GroundImprintField.StraightFloorM"/>)? In 3D, so the ground's own undulation breaks a stretch as
    /// surely as a turn does, and a flat quad never stands off the surface by more than that.</summary>
    private static bool StraightEnough(in Track t, Vec3 at)
    {
        Vec3 d = at - t.Start;
        if (t.Along.LengthSquared < 0.5) return d.Length < 1e4;      // one point so far: any second point is collinear
        double along = Vec3.Dot(d, t.Along);
        if (along < 0) return false;                                  // reversed: that is a new mark, not this one
        double tol = System.Math.Max(GroundImprintField.StraightFloorM, GroundImprintField.StraightTolerance * t.WidthM);
        return (d - t.Along * along).Length <= tol;
    }
}

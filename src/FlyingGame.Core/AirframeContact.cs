using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>The airframe parts a contact point belongs to — what breaks off when that point hits hard.</summary>
/// <summary>Things that can break off. Fuselage = the cabin's own hard points (never break). The fuselage is three
/// sections (owner 2026-09-09): Nose (ahead of the cabin, with the engine), the cabin, and the TailBoom (aft of the
/// cabin, carrying the tail surfaces and tailwheel); Cabin = a mid-fuselage slam that breaks BOTH ends off. Each gear
/// leg is its own component (GearLeft/GearRight/GearNose/GearTail) and tears off on its own hard touchdown.</summary>
public enum AirframeComponent
{
    Fuselage, Nose, WingLeft, WingRight, TailHorizontal, TailVertical, NacelleLeft, NacelleRight, Propeller, GearLeft, GearRight, GearNose, GearTail, TailBoom, Cabin,
    // Combat damage model (owner 2026-09-10): each wing is two panels — the OUTER panel (tip) and the INNER panel, which
    // carries the fuel — and every control surface is its own part. WingLeft/WingRight above still mean the whole wing
    // (the g-limit failure); losing an inner panel takes its outer panel with it.
    WingLeftOuter, WingRightOuter, WingLeftInner, WingRightInner, AileronLeft, AileronRight, ElevatorLeft, ElevatorRight, Rudder,
    // Contact damage (Ty 2026-11-03, appended, never inserted): the panel outboard of each wing's STRUCTURAL station
    // (WingStructure) — a biplane's interplane strut, a strut-braced wing's lift-strut attach, a cantilever wing's aileron
    // inboard end. WingTip* is a monoplane's wing or a biplane's upper wing; WingTipLower* a biplane's lower wing.
    WingTipLeft, WingTipRight, WingTipLowerLeft, WingTipLowerRight,
}

/// <summary>Where the wing splits into its two panels: |y| beyond this fraction of the semispan is the outer panel.</summary>
public static class WingPanels
{
    public const double OuterFraction = 0.55;
    public static double Semispan(AircraftConfig c)
    {
        double s = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            if (!id.Contains("wing")) continue;
            foreach (StripConfig st in sf.Strips) s = System.Math.Max(s, System.Math.Abs(st.Pos[1]));
        }
        return s;
    }
    /// <summary>The panel a wing strip at spanwise y belongs to (null for the centre section within ±0.3 m).</summary>
    public static AirframeComponent? PanelOf(double y, double semispan)
    {
        if (System.Math.Abs(y) <= 0.3) return null;
        bool outer = System.Math.Abs(y) > OuterFraction * semispan;
        return y < 0 ? (outer ? AirframeComponent.WingLeftOuter : AirframeComponent.WingLeftInner) : (outer ? AirframeComponent.WingRightOuter : AirframeComponent.WingRightInner);
    }
}

/// <summary>A hard point on the airframe (body frame, same origin as the config) that must not pass through
/// the ground or a solid: wing tips, stab tips, fin top, nose, tail cone, cabin top, nacelles.</summary>
public sealed class ContactPoint
{
    public Vec3 Body;
    public AirframeComponent Component;
    public double BreakSpeedMs;           // normal impact speed that snaps the component off (0 = unbreakable)
    public bool Touching;                 // contact state last step (for impact detection)
    public string Name = "";
    /// <summary>Set on a step's first RK4 stage: how far the point is above the ground or water under it, and how far
    /// it could close on it before the step ends (<see cref="AirframeContact.StageCullMargin"/>). The later stages skip
    /// the ground query while the first exceeds the second.</summary>
    public double StageClearM = double.NegativeInfinity, StageMarginM;

    /// <summary>Points with the same patch number (&gt;= 0) are one contact PATCH: close together on one part (a wingtip's
    /// leading edge, trailing edge and skins; the propeller disc) and listed together, they share one ground and water
    /// sample per evaluation. -1 = a point on its own.</summary>
    public int Patch = -1;
    /// <summary>The <see cref="WingStructure"/> panel this point is on (-1 = none). Such a point breaks nothing by its
    /// closing speed: the aeroplane checks the bending moment its force puts on the panel's station.</summary>
    public int Panel = -1;
    /// <summary>That panel itself (set by the aeroplane once its strengths are known): the panel's points push no harder
    /// than its plastic hinges can carry (<see cref="WingPanel.CapUpNm"/>); null = an ordinary hard point.</summary>
    public WingPanel? PanelRef;
    // A panel point's force this evaluation, held until its whole panel has been capped (ComputeEveryPoint).
    internal Vec3 PendingF, PendingR;
    internal bool Pending;
    internal double PendingK, PendingC;
    /// <summary>A blade-tip station on the propeller disc. The disc is not a hard point — a turning blade cuts or curls,
    /// it cannot hold the aeroplane up — so it pushes nothing; its penetration is the prop strike (Aircraft).</summary>
    public bool PropDisc;
    /// <summary>Contact spring and damper multiplier for a point on a flexible part. A wingtip is a cantilever of tip
    /// stiffness k_w in series with the contact's k_c: it presses with k_w/(k_c + k_w) of a hard point's force for the
    /// same approach, and the rest of that approach is the wing bending UP (WingPanel.ElasticRad) — not the ground
    /// coming through it.</summary>
    public double StiffnessScale = 1.0;

    // What this point felt at the last RECORDED evaluation (a step's first RK4 stage), body frame. Zero when clear.
    public Vec3 ForceBody;
    public double DepthM, NormalN, SlipMs, FrictionW;
    /// <summary>What it touched: <see cref="SurfaceNone"/>, <see cref="SurfaceSolid"/>, <see cref="SurfaceWater"/> or a
    /// <see cref="WorldTerrain.Surface"/> value.</summary>
    public int SurfaceCode = SurfaceNone;
    public const int SurfaceNone = -1, SurfaceSolid = -2, SurfaceWater = -3;
    /// <summary>World position and velocity at that evaluation.</summary>
    public Vec3 WorldPos, WorldVel;
    /// <summary>Set when the point ARRIVED faster than its break speed (the aeroplane reads and clears it: a fuselage
    /// section crushes before it breaks, <see cref="FuselageStructure"/>): the closing speed and the surface's outward
    /// normal in the body frame.</summary>
    public bool ArrivalPending;
    public double ArrivalMs;
    public Vec3 ArrivalNormalBody;
}

/// <summary>Solid boxes in the world (buildings, bridge decks, landmark legs) that the airframe collides with.
/// Axis-aligned in the sim frame: centre (x, y), half-sizes, bottom/top heights (m, up).</summary>
public static class WorldSolids
{
    public readonly struct Box
    {
        public readonly double Cx, Cy, Hx, Hy, Bottom, Top;
        public Box(double cx, double cy, double hx, double hy, double bottom, double top) { Cx = cx; Cy = cy; Hx = hx; Hy = hy; Bottom = bottom; Top = top; }
    }
    public static readonly System.Collections.Generic.List<Box> Boxes = new();

    /// <summary>A solid that is not a box (the sea cave's rock roof): reports penetration at a point.</summary>
    public interface IShape { bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth); }
    public static readonly TrackedList<IShape> Shapes = new();

    /// <summary>
    /// A solid or deck that never moves and answers "not here" everywhere outside this rectangle (sim x, y, metres):
    /// <see cref="IShape.Penetrate"/> false, <see cref="WorldDecks.IDeck.TopAt"/> false. It is then asked only by points
    /// inside it (<see cref="FootprintIndex{T}"/>). Anything that moves, or whose extent is not simple to state, does
    /// not implement this and is asked everywhere, as before. A footprint that is too small is a solid that cannot be
    /// hit, so it errs large: <c>WorldIndexTests</c> walks every footprint in the island against its own Penetrate.
    /// </summary>
    public interface IFootprint { void Footprint(out double minX, out double minY, out double maxX, out double maxY); }

    /// <summary>Which shapes can be hit at a point, in list order.</summary>
    public static readonly FootprintIndex<IShape> ShapeIndex = new(Shapes);

    /// <summary>Off only to prove the index changes nothing: with it off every shape and deck is asked, as before
    /// 2026-11-02 (<c>WorldIndexTests</c> flies the same flights both ways).</summary>
    public static bool IndexEnabled = true;

    /// <summary>If (x, y, up) is inside a box, the outward push (world, z DOWN) along the least-penetration axis and
    /// its depth; null outside every box.</summary>
    public static (Vec3 normalNed, double depth)? Penetration(double x, double y, double up)
    {
        foreach (Box b in Boxes)
        {
            double dx = x - b.Cx, dy = y - b.Cy;
            if (System.Math.Abs(dx) > b.Hx || System.Math.Abs(dy) > b.Hy || up < b.Bottom || up > b.Top) continue;
            double px = b.Hx - System.Math.Abs(dx), py = b.Hy - System.Math.Abs(dy), pTop = b.Top - up, pBot = up - b.Bottom;
            double m = System.Math.Min(System.Math.Min(px, py), System.Math.Min(pTop, pBot));
            if (m == pTop) return (new Vec3(0, 0, -1), pTop);
            if (m == px) return (new Vec3(System.Math.Sign(dx), 0, 0), px);
            if (m == py) return (new Vec3(0, System.Math.Sign(dy), 0), py);
            return (new Vec3(0, 0, 1), pBot);
        }
        if (!IndexEnabled)
        {
            SimProfile.CountSolidTests(Shapes.Count);
            foreach (IShape sh in Shapes)
                if (sh.Penetrate(x, y, up, out Vec3 n0, out double d0)) return (n0, d0);
            return null;
        }
        // Only the shapes whose footprint holds (x, y), in list order: the first that answers is the one the walk over
        // the whole list found (FootprintIndex). It was 1,475 shapes per hard point per RK4 stage.
        IShape[] shapes = ShapeIndex.Candidates(x, y, out int[] at);
        SimProfile.CountSolidTests(at.Length);
        for (int k = 0; k < at.Length; k++)
            if (shapes[at[k]].Penetrate(x, y, up, out Vec3 n, out double d)) return (n, d);
        return null;
    }

    /// <summary>A shape that never moves and can say where it is: the broadphase (<see cref="Nearby"/>) skips it unless its
    /// box overlaps the airframe's. Shapes without bounds (moving ones: the carrier, a tunnel's rocks) are always tested.</summary>
    public interface IBounded { void Bounds(out double minX, out double minY, out double minUp, out double maxX, out double maxY, out double maxUp); }

    /// <summary>A shape too big to bound (the forest covers the island) that can hand the broadphase a cheaper stand-in
    /// for one region: same answers for any point inside it.</summary>
    public interface IRegional { IShape Within(double minX, double minY, double minUp, double maxX, double maxY, double maxUp); }

    /// <summary>A solid that breaks rather than stops (the forest): an airframe does not meet it through the hard-point
    /// spring (<see cref="Nearby"/> leaves it out) but through its own strike test (<see cref="TreeStructure"/>), which
    /// weighs the wood's strength against the part that hits it. <see cref="Penetration"/> still reports it.</summary>
    public interface IFrangible { }

    /// <summary>Bump after rebuilding the solids in place (same counts), so every <see cref="Nearby"/> re-gathers.</summary>
    public static int Version;

    /// <summary>
    /// The broadphase for one airframe (Ty, iOS 2026-11-02, "it still slows down when it gets close to the ground"):
    /// the island registers ~1,500 solids (1,144 blocks, 297 cables) and <see cref="Penetration"/> tested every one of
    /// them for every hard point at every RK4 stage — about 89 µs a point on a Mac, the bulk of the sim's time on an
    /// iPad. This keeps the solids whose bounds come within <see cref="RegionMarginM"/> of the airframe and re-gathers
    /// only when the airframe leaves that region or the solids change. Same answers as <see cref="Penetration"/>, in the
    /// same order: a solid whose bounds miss the region cannot contain a point of the airframe.
    /// </summary>
    public sealed class Nearby
    {
        public const double RegionMarginM = 80.0;
        private readonly System.Collections.Generic.List<Box> _boxes = new();
        private readonly System.Collections.Generic.List<IShape> _shapes = new();
        private double _x0 = 1, _y0 = 1, _u0 = 1, _x1 = 0, _y1 = 0, _u1 = 0;   // empty: the first Update gathers
        private int _boxCount = -1, _shapeCount = -1, _version = -1;
        private IShape? _lastShape;
        /// <summary>How many times the solids were gathered (tests).</summary>
        public int Gathers { get; private set; }

        public void Update(double minX, double minY, double minUp, double maxX, double maxY, double maxUp)
        {
            IShape? last = Shapes.Count > 0 ? Shapes[Shapes.Count - 1] : null;
            bool inside = minX >= _x0 && maxX <= _x1 && minY >= _y0 && maxY <= _y1 && minUp >= _u0 && maxUp <= _u1;
            if (inside && Boxes.Count == _boxCount && Shapes.Count == _shapeCount && Version == _version && ReferenceEquals(last, _lastShape)) return;
            _x0 = minX - RegionMarginM; _y0 = minY - RegionMarginM; _u0 = minUp - RegionMarginM;
            _x1 = maxX + RegionMarginM; _y1 = maxY + RegionMarginM; _u1 = maxUp + RegionMarginM;
            _boxCount = Boxes.Count; _shapeCount = Shapes.Count; _version = Version; _lastShape = last;
            _boxes.Clear(); _shapes.Clear(); Gathers++;
            foreach (Box b in Boxes)
                if (b.Cx + b.Hx >= _x0 && b.Cx - b.Hx <= _x1 && b.Cy + b.Hy >= _y0 && b.Cy - b.Hy <= _y1 && b.Top >= _u0 && b.Bottom <= _u1) _boxes.Add(b);
            foreach (IShape sh in Shapes)
            {
                if (sh is IFrangible) continue;   // trees: the airframe's tree-strike test, not a spring
                if (sh is IRegional rg) { _shapes.Add(rg.Within(_x0, _y0, _u0, _x1, _y1, _u1)); continue; }
                if (sh is IBounded bd)
                {
                    bd.Bounds(out double ax, out double ay, out double au, out double bx, out double by, out double bu);
                    if (bx < _x0 || ax > _x1 || by < _y0 || ay > _y1 || bu < _u0 || au > _u1) continue;
                }
                _shapes.Add(sh);
            }
        }

        /// <summary>Nothing solid near the region last gathered (the glazing's sensors skip their solid queries).</summary>
        public bool Empty => _boxes.Count == 0 && _shapes.Count == 0;

        /// <summary><see cref="WorldSolids.Penetration"/> over the gathered solids; the point must lie inside the box last
        /// passed to <see cref="Update"/>.</summary>
        public (Vec3 normalNed, double depth)? Penetration(double x, double y, double up)
        {
            foreach (Box b in _boxes)
            {
                double dx = x - b.Cx, dy = y - b.Cy;
                if (System.Math.Abs(dx) > b.Hx || System.Math.Abs(dy) > b.Hy || up < b.Bottom || up > b.Top) continue;
                double px = b.Hx - System.Math.Abs(dx), py = b.Hy - System.Math.Abs(dy), pTop = b.Top - up, pBot = up - b.Bottom;
                double m = System.Math.Min(System.Math.Min(px, py), System.Math.Min(pTop, pBot));
                if (m == pTop) return (new Vec3(0, 0, -1), pTop);
                if (m == px) return (new Vec3(System.Math.Sign(dx), 0, 0), px);
                if (m == py) return (new Vec3(0, System.Math.Sign(dy), 0), py);
                return (new Vec3(0, 0, 1), pBot);
            }
            foreach (IShape sh in _shapes)
                if (sh.Penetrate(x, y, up, out Vec3 n, out double d)) return (n, d);
            return null;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Nearby> NearbyByAirframe = new();
    /// <summary>The broadphase belonging to one airframe (keyed by its contact-point list).</summary>
    public static Nearby NearbyFor(object airframe) => NearbyByAirframe.GetValue(airframe, _ => new Nearby());
}

/// <summary>
/// Airframe-to-ground (and airframe-to-solid) contact: every hard point is a stiff spring-damper against the
/// surface with Coulomb friction, so an aircraft on its back rests on its fin and wing tips instead of
/// hanging from its tyres. A point that ARRIVES fast enough snaps its component off: the caller drops the
/// component's aero (no mass/inertia bookkeeping — owner's call) and removes its contact points.
/// </summary>
/// <summary>Hardest first-contact closing speed seen during a step (any hard point or wheel) — for the crash sound.</summary>
public sealed class ImpactRecorder
{
    public double MaxClosingMs;
    public string Point = "";
    /// <summary>The hardest contact's surface, world point and world velocity (CrashRelay tells land from water by it).</summary>
    public bool OnWater;
    public Vec3 PositionWorld, VelocityWorld;
    public void Reset() { MaxClosingMs = 0; Point = ""; OnWater = false; PositionWorld = Vec3.Zero; VelocityWorld = Vec3.Zero; }
    public void Record(double closing, string point) { if (closing > MaxClosingMs) { MaxClosingMs = closing; Point = point; OnWater = false; } }
    public void Record(double closing, string point, bool onWater, Vec3 positionWorld, Vec3 velocityWorld)
    {
        if (closing <= MaxClosingMs) return;
        MaxClosingMs = closing; Point = point; OnWater = onWater; PositionWorld = positionWorld; VelocityWorld = velocityWorld;
    }

    /// <summary>A snapshot of the hardest contact.</summary>
    public readonly struct Hit
    {
        public readonly double ClosingMs; public readonly string Point; public readonly bool OnWater;
        public readonly Vec3 PositionWorld, VelocityWorld;
        public Hit(double closing, string point, bool onWater, Vec3 pos, Vec3 vel) { ClosingMs = closing; Point = point; OnWater = onWater; PositionWorld = pos; VelocityWorld = vel; }
    }
    public Hit Last => new(MaxClosingMs, Point, OnWater, PositionWorld, VelocityWorld);
}

public static class AirframeContact
{
    public const double SpringNPerM = 150000.0, DampNsPerM = 9000.0, FrictionMu = 0.7;
    /// <summary>What an airframe that cannot float meets when it ditches: a tenth of the ground's stiffness, so it
    /// goes in rather than bouncing, and three times its damping, so it stops in a wingspan or two instead of
    /// continuing to the bottom of the sea.</summary>
    public const double WaterSpringNPerM = 15000.0, WaterDampNsPerM = 27000.0;
    /// <summary>A crushed airframe does not spring back: while a point is SEPARATING only this fraction of the stored
    /// spring force is returned (owner 2026-09-09: "it shouldn't bounce 100 ft in the air"). Restitution ≈ √0.08 ≈ 0.28
    /// before damping; the rest of the impact energy goes into the structure.</summary>
    public const double ReboundFraction = 0.08;
    /// <summary>Impact speeds that break the fuselage: tail cone strike → tail boom; mid-fuselage slam → both ends.</summary>
    public const double TailBoomBreakMs = 5.5, CabinBreakMs = 8.0, NoseBreakMs = 6.0;

    /// <summary>Which component a landing-gear leg belongs to: tailwheel, nose (centreline), left or right main.</summary>
    public static AirframeComponent GearComponent(GearConfig g) =>
        g.IsTailwheel ? AirframeComponent.GearTail : g.Pos[1] < -0.05 ? AirframeComponent.GearLeft : g.Pos[1] > 0.05 ? AirframeComponent.GearRight : AirframeComponent.GearNose;

    /// <summary>Fuselage section cuts (body x): the nose section is ahead of <c>noseCutX</c>, the tail boom aft of
    /// <c>tailCutX</c>, the cabin between — set off the wing root (0.6 m ahead of the LE, 0.3 m behind the TE).</summary>
    public static (double noseCutX, double tailCutX) FuselageStations(AircraftConfig c)
    {
        double rootX = 0.3, chord = 1.4;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            if (!id.Contains("wing") || id.Contains("aileron") || sf.Strips.Count == 0) continue;
            StripConfig root = sf.Strips[0];
            foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1]) < System.Math.Abs(root.Pos[1])) root = st;
            rootX = root.Pos[0]; chord = root.Chord;
            break;
        }
        return (rootX + 0.25 * chord + 0.6, rootX - 0.75 * chord - 0.3);
    }

    /// <summary>
    /// EFFECTIVE friction of a WRECKED airframe sliding on the surface: the fuselage, wing stubs and every wreck piece
    /// (WreckSim uses it too). Ty 2026-11-04 6:31 PM PT: "after a crash the fuselage/cockpit wreck slides an
    /// unrealistically long way."
    ///
    /// Until then this was the float-keel table (<see cref="WorldTerrain.KeelSlidingCoefficient"/>): a smooth keel strip
    /// sliding along its length, grass 0.35, snow 0.10, glacier ice 0.03, which put a 30 m/s wreck 131 m down a grass
    /// field, 459 m along a snowfield and 1.5 km across ice. A wreck is not a keel: torn skin, bent stringers, a broken
    /// strut or a gear stub gouge pavement and PLOUGH soft ground, so the force is abrasion plus the soil pushed ahead.
    /// Reference values: accident reconstruction tabulates a vehicle body sliding on its side or roof at 0.3-0.5 on dry
    /// asphalt and 0.5-0.8 on grass and dirt, where the body digs in (e.g. Fricke, Traffic Accident Reconstruction,
    /// Northwestern University Traffic Institute, drag-factor tables; the same order as the 0.4-0.6 metal-on-asphalt of
    /// the Engineering ToolBox), and a gear-up light aircraft on grass typically stops in 40-60 m from 55 kt
    /// (μ_eff ≈ 0.7). A crumpled airframe has more to dig with than a car's smooth flank, so it takes the upper half of
    /// those bands; on snow it ploughs (vehicles on packed snow 0.2-0.3, loose snow more), on ice it barely can (vehicles
    /// on ice 0.1-0.2). These are ESTIMATES inside published ranges, not measurements of an aircraft (AGENT-LOG,
    /// Q for Ty). Coulomb stopping distance v²/(2μg) from 30 m/s: paved 76 m, gravel 61, dirt/sand 57, grass 71,
    /// pasture 61, snow 115, packed snow 153, ice 229 — before drag, tumbling and parts digging in shorten it.
    /// Floats keep the keel table (LandingGear), and buildings <see cref="FrictionMu"/>.
    /// </summary>
    public static double SlidingMu(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Paved => 0.60,
        WorldTerrain.Surface.Gravel => 0.75,
        WorldTerrain.Surface.Dirt or WorldTerrain.Surface.Sand => 0.80,
        WorldTerrain.Surface.Grass => 0.65,
        WorldTerrain.Surface.Rough => 0.75,
        WorldTerrain.Surface.Snow => 0.40,
        WorldTerrain.Surface.PackedGlacierSnow => 0.30,
        WorldTerrain.Surface.GlacierIce => 0.20,
        _ => 0.75,
    };

    /// <summary>
    /// Derive the hard points from the config geometry (Ty 2026-11-03: "the tip of every wing, tail and the prop").
    /// Strip positions are quarter-chord points. On the own-model biplanes' lower wings their heights ALREADY contain the
    /// dihedral (a Pitts lower wing runs z 0.528 at the root to 0.408 at the tip); everywhere else they sit at one height
    /// and the dihedral is only in dihedralRad (<see cref="WingStructure.ChordZ"/> tells them apart). The tip is the outer
    /// strip's outer edge. Before 2026-11-03 the tip point always subtracted |y|·tan Γ, which on the Pitts counted the
    /// dihedral twice and put the point 0.15 m INSIDE the lower wing, at its quarter chord only — so the wing went
    /// through the ground before anything touched.
    /// Each wingtip is a PATCH of five points sharing one ground sample: leading edge, trailing edge, the aileron's outer
    /// trailing corner, and the lower and upper skin at 30 % chord (thickness from the airfoil). Stab tips, the fin top and
    /// the rudder heel are patches too, and the propeller is its whole disc (twelve blade-tip stations at full radius).
    /// </summary>
    public static List<ContactPoint> BuildPoints(AircraftConfig c)
    {
        var pts = new List<ContactPoint>();
        int patch = 0;
        double rBody = c.Fuselage.Crossflow?.BodyRadiusM > 0 ? c.Fuselage.Crossflow.BodyRadiusM : 0.5;
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double minTe = double.MaxValue;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips) minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
        List<WingPanel> panels = WingStructure.Build(c);

        StripConfig? Outer(SurfaceConfig sf, int side)
        {
            StripConfig? o = null;
            foreach (StripConfig st in sf.Strips) if (System.Math.Sign(st.Pos[1]) == side && (o is null || System.Math.Abs(st.Pos[1]) > System.Math.Abs(o.Pos[1]))) o = st;
            return o;
        }
        (double y, double z) Edge(SurfaceConfig sf, StripConfig st, int side)
        {
            double y = st.Pos[1] + side * 0.5 * WingStructure.Width(st);
            return (y, WingStructure.ChordZ(sf, st, System.Math.Abs(y)));
        }
        void TipPatch(SurfaceConfig baseSf, IEnumerable<SurfaceConfig> controlRows, int side, AirframeComponent comp, int panel, double breakMs, string tag, bool skins)
        {
            StripConfig? tip = Outer(baseSf, side);
            if (tip is null) return;
            (double ye, double ze) = Edge(baseSf, tip, side);
            double le = tip.Pos[0] + 0.25 * tip.Chord, te = tip.Pos[0] - 0.75 * tip.Chord;
            var group = new List<ContactPoint>();
            void Add(string what, double x, double y, double z) =>
                group.Add(new ContactPoint { Body = new Vec3(x, y, z), Component = comp, BreakSpeedMs = breakMs, Name = $"{tag}-{what}-{(side < 0 ? "L" : "R")}", Patch = patch, Panel = panel });
            Add("LE", le, ye, ze);
            Add("TE", te, ye, ze);
            foreach (SurfaceConfig row in controlRows)
            {
                StripConfig? o = Outer(row, side);
                if (o is null) continue;
                (double yr, double zr) = Edge(row, o, side);
                double ter = o.Pos[0] - 0.75 * o.Chord;
                if (ter < te - 0.01) Add("TEcorner", ter, yr, zr);
            }
            if (skins)
            {
                double chord = le - te, half = 0.5 * Wreck.AirframeHydroModel.ThicknessRatio(tip.Airfoil) * chord, xs = le - 0.3 * chord;
                Add("skinLower", xs, ye, ze + half);
                Add("skinUpper", xs, ye, ze - half);
            }
            pts.AddRange(group);
            patch++;
        }

        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            if (sf.Strips.Count == 0) continue;
            if (WingStructure.IsWingBase(sf.Id))
            {
                // Wing root skin (the top of a high wing is what a flipped aircraft rests on; the bottom of a low
                // wing is what a belly landing rides on): one point each side of the centreline.
                StripConfig root = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1]) < System.Math.Abs(root.Pos[1])) root = st;
                double skin = root.Pos[2] < 0 ? root.Pos[2] - 0.07 * root.Chord : root.Pos[2] + 0.07 * root.Chord;
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], -0.8, skin), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "wing-root-L" });
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], 0.8, skin), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "wing-root-R" });
                var rows = c.Surfaces.Where(r => r != sf && WingStructure.BelongsTo(r.Id, sf.Id, c)).ToList();
                foreach (int side in new[] { -1, 1 })
                {
                    int pi = panels.FindIndex(p => p.SurfaceId == sf.Id && p.Side == side);
                    if (pi >= 0) TipPatch(sf, rows, side, panels[pi].Component, pi, 0.0, "tip:" + sf.Id, true);
                    else TipPatch(sf, rows, side, side < 0 ? AirframeComponent.WingLeftOuter : AirframeComponent.WingRightOuter, -1, 4.5, "tip:" + sf.Id, true);
                }
                // A mid-span strike (0.45 semispan) takes the inner panel and the outer with it (owner 2026-09-10).
                StripConfig? lt = Outer(sf, -1), rt = Outer(sf, 1);
                double semi = System.Math.Max(lt is null ? 0 : System.Math.Abs(lt.Pos[1]), rt is null ? 0 : System.Math.Abs(rt.Pos[1]));
                foreach ((AirframeComponent comp, double sign) in new[] { (AirframeComponent.WingLeftInner, -1.0), (AirframeComponent.WingRightInner, 1.0) })
                {
                    double y = sign * semi * 0.45;
                    StripConfig near = root; foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1] - y) < System.Math.Abs(near.Pos[1] - y)) near = st;
                    double z = WingStructure.ChordZ(sf, near, System.Math.Abs(y)) + (near.Pos[2] < 0 ? -0.06 * near.Chord : 0.06 * near.Chord);
                    pts.Add(new ContactPoint { Body = new Vec3(near.Pos[0], y, z), Component = comp, BreakSpeedMs = 7.0, Name = comp + ":" + sf.Id });
                }
            }
            if (id == "hstab")
            {
                var elev = c.Surfaces.Where(r => r.Id.ToLowerInvariant() == "elevator").ToList();
                foreach (int side in new[] { -1, 1 })
                {
                    int before = pts.Count;
                    TipPatch(sf, elev, side, AirframeComponent.TailHorizontal, -1, 5.0, "stab", false);
                    if (pts.Count > before) pts[before].Name = side < 0 ? "stab-L" : "stab-R";
                }
            }
            if (id == "vstab")
            {
                StripConfig top = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (st.Pos[2] < top.Pos[2]) top = st;
                double topZ = top.Pos[2] - 0.5 * WingStructure.Width(top);
                if (topZ > -0.2) topZ = -0.2; // a fin that sits below the axis (low rudder): still give it a top
                pts.Add(new ContactPoint { Body = new Vec3(top.Pos[0] + 0.25 * top.Chord, 0, topZ), Component = AirframeComponent.TailVertical, BreakSpeedMs = 5.0, Name = "fin-top", Patch = patch });
                SurfaceConfig? rud = c.Surfaces.FirstOrDefault(r => r.Id.ToLowerInvariant().StartsWith("rudder") && r.Strips.Count > 0);
                double teTop = top.Pos[0] - 0.75 * top.Chord;
                if (rud is not null)
                {
                    StripConfig rt = rud.Strips[0], rb = rud.Strips[0];
                    foreach (StripConfig st in rud.Strips) { if (st.Pos[2] < rt.Pos[2]) rt = st; if (st.Pos[2] > rb.Pos[2]) rb = st; }
                    teTop = System.Math.Min(teTop, rt.Pos[0] - 0.75 * rt.Chord);
                    pts.Add(new ContactPoint { Body = new Vec3(teTop, 0, topZ), Component = AirframeComponent.TailVertical, BreakSpeedMs = 5.0, Name = "fin-top-TE", Patch = patch });
                    patch++;
                    // The rudder's heel: its lowest trailing corner, what a tail-low arrival drags first.
                    pts.Add(new ContactPoint { Body = new Vec3(rb.Pos[0] - 0.75 * rb.Chord, 0, rb.Pos[2] + 0.5 * WingStructure.Width(rb)), Component = AirframeComponent.Rudder, BreakSpeedMs = 5.0, Name = "rudder-heel" });
                }
                else
                {
                    pts.Add(new ContactPoint { Body = new Vec3(teTop, 0, topZ), Component = AirframeComponent.TailVertical, BreakSpeedMs = 5.0, Name = "fin-top-TE", Patch = patch });
                    patch++;
                }
            }
        }
        double tailX = minTe - 0.2, noseX = tailX + len;
        // Tail cone bottom sits ABOVE the tailwheel/skid (which hangs below it) — never lower than that.
        double tailZ = 0.0;
        foreach (GearConfig g in c.Gear) if (g.IsTailwheel || g.Pos[0] < tailX + 1.5) tailZ = System.Math.Min(tailZ, g.Pos[2] - 0.45);
        // The nose and the propeller hub sit on the THRUST LINE (propulsion.thrustLineZ, + below the CG), not on the CG
        // line: a P-51's fuselage axis is 0.75 m above its CG, and putting the prop on the CG line struck it 0.75 m early.
        double axisZ = c.Propulsion?.ThrustLineZ ?? 0.0;
        pts.Add(new ContactPoint { Body = new Vec3(noseX, 0, axisZ), Component = AirframeComponent.Nose, BreakSpeedMs = NoseBreakMs, Name = "nose" });
        // Propeller disc: twelve blade-tip stations at full radius, one patch per propeller.
        if (c.Propulsion is not null && c.Propulsion.PropDiameterM > 0)
        {
            double r = c.Propulsion.PropDiameterM * 0.5;
            var hubs = c.Engines.Count == 0 ? new List<Vec3> { new(noseX + 0.1, 0, axisZ) } : c.Engines.Select(e => new Vec3(e.Pos[0] + 1.0, e.Pos[1], e.Pos[2])).ToList();
            foreach (Vec3 hub in hubs)
            {
                for (int k = 0; k < PropDiscStations; k++)
                {
                    double a = 2 * System.Math.PI * k / PropDiscStations;
                    pts.Add(new ContactPoint { Body = new Vec3(hub.X, hub.Y + r * System.Math.Sin(a), hub.Z + r * System.Math.Cos(a)), Component = AirframeComponent.Propeller, BreakSpeedMs = 0, Name = "prop-tip", Patch = patch, PropDisc = true });
                }
                patch++;
            }
        }
        // Fuselage in three sections: a hard tail-cone strike snaps the TAIL BOOM (stab, fin, tailwheel go with it);
        // a hard cowl strike takes the NOSE; a mid-fuselage slam (belly / cabin top / wing roots) breaks up the
        // fuselage — both ends off, the cabin survives.
        pts.Add(new ContactPoint { Body = new Vec3(tailX, 0, tailZ), Component = AirframeComponent.TailBoom, BreakSpeedMs = TailBoomBreakMs, Name = "tail-cone" });
        pts.Add(new ContactPoint { Body = new Vec3(0.3, 0, -rBody * 1.1), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "cabin-top" });
        pts.Add(new ContactPoint { Body = new Vec3(noseX * 0.6, 0, axisZ - rBody * 0.9), Component = AirframeComponent.Nose, BreakSpeedMs = NoseBreakMs, Name = "cowl-top" });
        pts.Add(new ContactPoint { Body = new Vec3(0.3, 0, rBody * 1.0), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "belly" });
        foreach (EngineMount e in c.Engines)
        {
            pts.Add(new ContactPoint { Body = new Vec3(e.Pos[0] + 0.8, e.Pos[1], e.Pos[2] + 0.4), Component = e.Pos[1] < 0 ? AirframeComponent.NacelleLeft : AirframeComponent.NacelleRight, BreakSpeedMs = 6.0, Name = "nacelle" });
        }
        // Tougher airframes (config impactStrength) take proportionally harder hits before anything breaks (the wing
        // panels' strengths scale the same way, WingStructure.EnsureStrength).
        if (c.ImpactStrength > 0 && System.Math.Abs(c.ImpactStrength - 1.0) > 1e-9)
            foreach (ContactPoint p in pts) if (p.Component != AirframeComponent.Propeller && p.BreakSpeedMs > 0) p.BreakSpeedMs *= c.ImpactStrength;
        return pts;
    }

    /// <summary>Blade-tip stations on each propeller disc.</summary>
    /// <summary>
    /// The propeller hub(s), body frame — the same construction the prop-disc contact points use, so a propeller that
    /// departs leaves from exactly where it was being struck.
    /// </summary>
    public static System.Collections.Generic.List<Vec3> PropHubsBody(AircraftConfig c)
    {
        var hubs = new System.Collections.Generic.List<Vec3>();
        if (c.Propulsion is null || c.Propulsion.PropDiameterM <= 0) return hubs;
        double axisZ = c.Propulsion.ThrustLineZ;
        if (c.Engines.Count == 0)
        {
            double minTe = double.MaxValue;
            foreach (SurfaceConfig sf in c.Surfaces)
                foreach (StripConfig st in sf.Strips) minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
            double len = c.Fuselage?.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
            double noseX = minTe == double.MaxValue ? 0.0 : minTe - 0.2 + len;
            hubs.Add(new Vec3(noseX + 0.1, 0, axisZ));
        }
        else foreach (EngineMount e in c.Engines) hubs.Add(new Vec3(e.Pos[0] + 1.0, e.Pos[1], e.Pos[2]));
        return hubs;
    }

    /// <summary>
    /// What one propeller weighs, kg: the config's figure when it has one, else an estimate from the disc and what
    /// the blades are made of. A propeller's weight scales with its swept area for a given construction, and the
    /// anchor is the Hartzell HC-C2YR on the Pitts — 82 in of aluminium, about 25 kg complete — which gives
    /// 5.8 kg per square metre of diameter squared. Wood and composite blades run near half that.
    /// </summary>
    public static double PropMassKg(AircraftConfig c)
    {
        PropulsionConfig? p = c.Propulsion;
        if (p is null || p.PropDiameterM <= 0) return 0.0;
        if (p.PropMassKg > 0) return p.PropMassKg;
        double perD2 = string.Equals(p.PropMaterial, "metal", System.StringComparison.OrdinalIgnoreCase) ? 5.8 : 3.0;
        double blades = System.Math.Clamp(p.PropBladeCount, 2, 6);
        return perD2 * p.PropDiameterM * p.PropDiameterM * (0.75 + 0.125 * blades);
    }

    public const int PropDiscStations = 12;

    /// <summary>
    /// How far above the ground the whole airframe is unambiguously clear of it. Terrain cannot rise by this much
    /// inside the few metres an airframe spans — towers, buildings and bridge decks are <see cref="WorldSolids"/>,
    /// which is a bounded list test and is still checked at every altitude: when the airframe is clear of the ground
    /// only the ground and water queries are skipped, every hard point is still tested against the solids
    /// (<see cref="ComputeSolidsOnly"/>; before 2026-11-02 the shortcut returned nothing at all, so above 60 m agl a
    /// tower, the arch or a hangar roof could be flown through).
    /// </summary>
    private const double ClearAboveM = 60.0;
    /// <summary><see cref="ClearAboveM"/> for other airframe sensors (the glazing, Sim's AircraftGlazing).</summary>
    public const double ClearOfGroundM = ClearAboveM;

    /// <summary>
    /// Five height samples that say whether the airframe is clear of the ground altogether: under the CG and at
    /// four points on its own radius. Returns false whenever anything could be close, so the caller falls back to
    /// the exact per-point test.
    /// </summary>
    private static bool ClearOfTheGround(List<ContactPoint> points, Vec3 cg, RigidBodyState s) =>
        ClearOfTheGround(points, null, cg, s, 0.0);

    /// <summary>
    /// The same five-sample test for a whole step, with the wheels counted (<paramref name="wheels"/>: body positions of
    /// each wheel's contact point, null for none) and <paramref name="extraM"/> more clearance demanded — the distance the
    /// airframe can travel before the step ends. True means neither the hard points nor the wheels can reach the ground
    /// on ANY of the step's four RK4 stages, so the caller skips gear and contact for the whole step
    /// (<c>Aircraft.StepWithDeflectionTargets</c>). The test itself costs five height samples once a step instead of
    /// once a stage, and the gear, which had no such test at all, stops asking for the ground under every wheel at
    /// every stage in cruise.
    /// </summary>
    public static bool ClearOfTheGround(List<ContactPoint> points, IReadOnlyList<Vec3>? wheels, Vec3 cg, RigidBodyState s, double extraM)
        => ClearOfTheGround(points, wheels, cg, s, extraM, out _);

    /// <summary>The four samples round the CG, as unit offsets (scaled by the reach: the same sums, no array a call).</summary>
    private static readonly (double dx, double dy)[] Around = { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) };

    /// <summary>
    /// <see cref="ClearOfTheGround(List{ContactPoint}, IReadOnlyList{Vec3}?, Vec3, RigidBodyState, double)"/>, also
    /// saying how far clear the lowest point is (<paramref name="clearanceM"/>, the number compared with 60 m + extra;
    /// −∞ with nothing to test). A step whose clearance is under 60 m − its travel cannot be clear of the ground at any of
    /// its four stages, by the same bound that lets a clear step skip them, so the caller can skip the per-stage test
    /// (<see cref="StageCannotBeClear"/>).
    /// </summary>
    public static bool ClearOfTheGround(List<ContactPoint> points, IReadOnlyList<Vec3>? wheels, Vec3 cg, RigidBodyState s, double extraM, out double clearanceM)
    {
        clearanceM = double.NegativeInfinity;
        double reach = 0, lowestZ = double.MinValue;
        foreach (ContactPoint p in points)
        {
            Vec3 rWorld = s.Attitude.Rotate(p.Body - cg);
            Vec3 w = s.Position + rWorld;
            double horiz = System.Math.Sqrt(rWorld.X * rWorld.X + rWorld.Y * rWorld.Y);
            if (horiz > reach) reach = horiz;
            if (w.Z > lowestZ) lowestZ = w.Z;        // z is + DOWN, so the largest z is the lowest point
        }
        if (wheels != null)
            for (int i = 0; i < wheels.Count; i++)   // (an index loop: foreach over the IReadOnlyList allocates, once a step)
            {
                Vec3 b = wheels[i];
                Vec3 rWorld = s.Attitude.Rotate(b - cg);
                double horiz = System.Math.Sqrt(rWorld.X * rWorld.X + rWorld.Y * rWorld.Y);
                if (horiz > reach) reach = horiz;
                double z = s.Position.Z + rWorld.Z;
                if (z > lowestZ) lowestZ = z;
            }
        if (lowestZ == double.MinValue) return false;
        double highestGround = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
        foreach ((double ux, double uy) in Around)
        {
            double g = WorldTerrain.GroundHeightAt(s.Position.X + ux * reach, s.Position.Y + uy * reach);
            if (g > highestGround) highestGround = g;
        }
        // Water counts as ground here: a float or a hull touches it.
        double? water = FloatHydro.WaterSurfaceAt(s.Position.X, s.Position.Y, -s.Position.Z);
        if (water.HasValue && water.Value > highestGround) highestGround = water.Value;
        clearanceM = -lowestZ - highestGround;
        return clearanceM > ClearAboveM + extraM;
    }

    /// <summary>Off only to prove the per-step ground test and the stage cull change nothing: with it off the sim takes
    /// the paths it took before 2026-11-02, and <c>GroundContactCullTests</c> flies the same flights both ways.</summary>
    public static bool StageCullEnabled = true;

    /// <summary>
    /// How much clearance a point must have on a step's first stage for the other three to skip its ground query:
    /// 0.1 m, plus eleven times the distance it moves in the step (<paramref name="speedMs"/> × <paramref name="dt"/>) —
    /// once for the point coming down, ten times for the ground coming up under it as it moves sideways, which is any
    /// slope up to 84°. A point that clear on stage 1 cannot touch the ground on stages 2–4, so skipping the query
    /// returns the zero it would have returned. Against a wall steeper than that (a cliff face, a cave mouth, a deck
    /// edge) a contact can begin one step — 5 ms — later than it did; solids are still tested at every stage.
    /// </summary>
    public static double StageCullMargin(double speedMs, double dt) => 0.1 + 11.0 * speedMs * dt;

    /// <summary>
    /// True when a step whose airframe is <paramref name="stepClearanceM"/> clear of the ground, and can travel
    /// <paramref name="extraM"/> in the step (the margin <see cref="ClearOfTheGround(List{ContactPoint}, IReadOnlyList{Vec3}?, Vec3, RigidBodyState, double)"/>
    /// was given), cannot be more than 60 m clear at any of its RK4 stages — so <see cref="Compute(List{ContactPoint}, Vec3, RigidBodyState, List{AirframeComponent}?, double, ImpactRecorder?, bool, bool, int, double, bool)"/>
    /// need not ask, at five height evaluations a stage. Quest, 2026-11-02: on the ground those were 20 of the 48
    /// evaluations a 2-33 made each step, every one of them answering "not clear".
    /// </summary>
    public static bool StageCannotBeClear(double stepClearanceM, double extraM) => stepClearanceM + extraM <= ClearAboveM;

    /// <summary>
    /// Contact forces (world) and the components whose points just hit hard enough to break.
    ///
    /// PERFORMANCE, and it is the difference between flying and not (2026-10-30). One island height query costs
    /// about 99 us, because the height field evaluates the whole world description per sample. This loop asks for
    /// one per contact point, and the RK4 integrator evaluates forces four times per step at 200 Hz — fifteen
    /// points therefore cost 12,000 height queries per second of flight, about 1.2 s of CPU per second on a Mac
    /// Studio. The iPad ran the simulation at 5 fps: the aeroplane flew, far too slowly to fly.
    ///
    /// So the footprint is tested first, with five samples instead of fifteen-per-stage: the ground under the CG
    /// and under four points on the airframe's own radius. If the lowest part of the aeroplane is more than
    /// <see cref="ClearAboveM"/> above the highest of those, nothing can be touching and the loop is skipped.
    /// Within that height — which is where landing, taxiing and every contact actually happen — every point is
    /// sampled exactly as before, so no contact behaviour changes.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ = 0.0, ImpactRecorder? impacts = null, bool floatsCarryWater = false, bool waterByHydro = false)
        => Compute(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, -1, 0.0);

    /// <summary>
    /// <see cref="Compute(List{ContactPoint}, Vec3, RigidBodyState, List{AirframeComponent}?, double, ImpactRecorder?, bool, bool)"/>
    /// told which RK4 stage this is (0..3; -1 = not part of a step, test every point every time) and the step's
    /// <paramref name="dt"/>. On stage 0 every point within reach is tested and remembers its clearance; on stages 1–3 a
    /// point that was clear by more than <see cref="StageCullMargin"/> skips the ground and water queries (solids are
    /// still tested). Quest 3S, 2026-11-02: these queries, 21 points × 4 stages × 200 Hz, were what took the headset
    /// from 72 fps to 20 below 60 m and to 8 on the ground (docs/quest-performance.md, *Slow near the ground*).
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ, ImpactRecorder? impacts, bool floatsCarryWater, bool waterByHydro, int stage, double dt)
        => Compute(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, stage, dt, false);

    /// <summary>The same, told by the step (<paramref name="knownNotClear"/>, <see cref="StageCannotBeClear"/>) that the
    /// clear-of-the-ground test would say no, so it is not asked.</summary>
    public static (Vec3 Force, Vec3 Moment) Compute(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ, ImpactRecorder? impacts, bool floatsCarryWater, bool waterByHydro, int stage, double dt, bool knownNotClear)
    {
        // A step's first stage (or a call outside any step) is the one recorded on the points (ContactPoint.ForceBody …).
        bool record = stage <= 0;
        long tClear = SimProfile.Begin();
        bool clear = !knownNotClear && points.Count > 0 && WorldTerrain.Active != null && ClearOfTheGround(points, cg, s);
        SimProfile.End(SimProfile.Scope.ContactClear, tClear);
        if (clear)
        {
            // No clearance remembered for the later stages: if one of them is not clear, it tests every point.
            if (stage == 0) foreach (ContactPoint p in points) p.StageClearM = double.NegativeInfinity;
            long tSolids = SimProfile.Begin();
            var solidsOnly = ComputeEveryPoint(points, cg, s, broken, 0.0, impacts, false, false, -1, 0.0, groundClear: true, record);
            SimProfile.End(SimProfile.Scope.ContactPoints, tSolids);
            return solidsOnly;
        }
        long tPoints = SimProfile.Begin();
        var result = ComputeEveryPoint(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, stage, dt, groundClear: false, record);
        SimProfile.End(SimProfile.Scope.ContactPoints, tPoints);
        return result;
    }

    /// <summary>
    /// The exact test: every contact point against the ground, with no footprint shortcut. This is what
    /// <see cref="Compute"/> did before the shortcut existed, and the regression test holds the two to the same
    /// answer at every altitude — a shortcut that ever skips a real contact is a worse bug than a slow frame.
    /// </summary>
    /// <param name="waterByHydro">The airframe's water forces come from its own volumes (Wreck/AirframeFlooding): a hard
    /// point arriving at the water is still an impact and can still break its component — water at 25 m/s is hard —
    /// but the old ditching spring-damper is not applied.</param>
    public static (Vec3 Force, Vec3 Moment) ComputeEveryPoint(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ = 0.0, ImpactRecorder? impacts = null, bool floatsCarryWater = false, bool waterByHydro = false)
        => ComputeEveryPoint(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, -1, 0.0, groundClear: false, record: true);

    /// <summary>
    /// The hard points against <see cref="WorldSolids"/> only, for an airframe already known to be clear of the ground
    /// and the water (<see cref="ClearOfTheGround(List{ContactPoint}, IReadOnlyList{Vec3}?, Vec3, RigidBodyState, double)"/>):
    /// no height queries, one box-list test per point. Buildings, the tower, the arch legs and the sea-cave roof stand
    /// higher than <see cref="ClearAboveM"/> above the ground under them, so this runs at every altitude.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) ComputeSolidsOnly(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, ImpactRecorder? impacts, bool record = false)
        => ComputeEveryPoint(points, cg, s, broken, 0.0, impacts, false, false, -1, 0.0, groundClear: true, record);

    private static (Vec3 Force, Vec3 Moment) ComputeEveryPoint(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ, ImpactRecorder? impacts, bool floatsCarryWater, bool waterByHydro, int stage, double dt, bool groundClear, bool record)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        bool cull = StageCullEnabled && stage >= 0;
        // Broadphase: the solids near the airframe's box, gathered once and kept while the airframe stays in their region.
        double reach = 0;
        foreach (ContactPoint p in points) { double r = (p.Body - cg).Length; if (r > reach) reach = r; }
        reach += 0.5;
        var nearby = WorldSolids.NearbyFor(points);
        nearby.Update(s.Position.X - reach, s.Position.Y - reach, -s.Position.Z - reach, s.Position.X + reach, s.Position.Y + reach, -s.Position.Z + reach);
        // One ground/water sample per patch (its points are listed together): taken where the first point that needs it is.
        int samplePatch = int.MinValue; double patchGround = 0; double? patchWater = null;
        // A patch is ONE contact: its touching points share one contact stiffness (each takes k/m, c/m of m touching),
        // or a wingtip lying flat would press five times as hard as a point.
        int sharePatch = int.MinValue, shareCount = 1, head = 0;
        bool anyPanel = false;
        for (int idx = 0; idx < points.Count; idx++)
        {
            ContactPoint p = points[idx];
            if (idx == 0 || p.Patch != points[idx - 1].Patch) head = idx;
            Vec3 rBody = p.Body - cg;
            Vec3 rWorld = s.Attitude.Rotate(rBody);
            Vec3 w = s.Position + rWorld;
            Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));   // world velocity of the point
            if (record) { p.ForceBody = Vec3.Zero; p.DepthM = 0; p.NormalN = 0; p.SlipMs = 0; p.FrictionW = 0; p.SurfaceCode = ContactPoint.SurfaceNone; p.WorldPos = w; p.WorldVel = vel; }

            Vec3 normal = new(0, 0, -1);   // outward (NED up)
            double depth;
            bool onWater = false;
            // The floats carry the airframe on the water (FloatHydro), not its propeller: a blade-tip station reads the
            // water it is in. Skipping it with the rest of a floatplane left every floatplane's prop unable to touch the
            // water at all, so no water strike could slow or stop it (Ty 2026-11-05, the J-3 on floats).
            bool carried = floatsCarryWater && !p.PropDisc;
            double groundH = double.NegativeInfinity;   // the ditching bed below needs it; unset only where onWater stays false
            if (groundClear || (cull && stage > 0 && p.StageClearM > p.StageMarginM))
            {
                depth = double.NegativeInfinity;   // clear of the ground and the water all step (see StageCullMargin)
            }
            else
            {
            // Ground (with the wheel-scale bumps when the point is low enough to meet them) — normal is world up.
            double? waterH;
            if (p.Patch >= 0)
            {
                // Always where the patch's FIRST point is, whichever point asks first (culled or not, same answer).
                if (p.Patch != samplePatch) SamplePatch(points, head, cg, s, ref samplePatch, ref patchGround, ref patchWater);
                groundH = patchGround; waterH = patchWater;
            }
            else
            {
                groundH = WorldTerrain.HardPointGroundAt(w.X, w.Y, -w.Z);
                waterH = FloatHydro.WaterSurfaceAt(w.X, w.Y, -w.Z);
            }
            onWater = waterH.HasValue && waterH.Value >= groundH - 0.01;
            // On water, a SEAPLANE's hull is FloatHydro's business and this must keep its hands off it. A wheeled
            // aeroplane's is nobody's: every point over water used to return no force at all, so a landplane that
            // ditched sank through the sea, through the bed under it, and went on falling for ever — which is what
            // Ty saw as the Pitts "freezing" about thirty seconds into a flight out of the aerobatic box, the box
            // being over the sea. Water now pushes back on an airframe that cannot float, at a tenth of the ground's
            // stiffness and three times its damping: an arrival, not a trampoline, and not a runway either.
            depth = onWater
                ? (carried ? -1.0 : (w.Z + surfaceOffsetZ) - (-waterH!.Value))
                : (w.Z + surfaceOffsetZ) - (-groundH);
            if (cull && stage == 0)
            {
                // Clearance over whichever is higher, ground or water: either branch above gives a depth no deeper.
                double top = waterH.HasValue && waterH.Value > groundH ? waterH.Value : groundH;
                p.StageClearM = -(w.Z + surfaceOffsetZ) - top;
                p.StageMarginM = StageCullMargin(vel.Length, dt);
            }
            // Into the open ground, the ground pushes back along ITS normal, by how far the point is inside it
            // measured along that normal. With world-up and the vertical depth, a wall was a floor: a point 10 cm
            // into a 73 deg crevasse wall read as 34 cm deep and was pushed straight UP with 3.4 times the force,
            // instead of being stopped. Measured along the normal, the depth is the 17 cm it actually is and
            // the push is horizontal; on level ground the normal is straight up and nothing changes. Decks and
            // cave floors are level slabs, so they keep world-up.
            if (!onWater && depth > 0.0)
            {
                normal = WorldTerrain.ContactNormalAt(w.X, w.Y, groundH);
                depth *= -normal.Z;
            }
            }
            // Solids (buildings etc.): whichever is deeper.
            var solid = nearby.Penetration(w.X, w.Y, -w.Z);
            bool solidWins = solid.HasValue && solid.Value.depth > depth;
            if (solidWins) { normal = solid!.Value.normalNed; depth = solid.Value.depth; }

            if (depth <= 0.0) { p.Touching = false; continue; }
            if (p.Patch >= 0 && p.Patch != sharePatch && !groundClear)
            {
                // How many of this patch's points are into the ground or the water (the shared sample; solids aside).
                sharePatch = p.Patch; shareCount = 0;
                if (samplePatch != p.Patch) SamplePatch(points, head, cg, s, ref samplePatch, ref patchGround, ref patchWater);
                for (int j = idx; j < points.Count && points[j].Patch == p.Patch; j++)
                {
                    Vec3 wj = s.Position + s.Attitude.Rotate(points[j].Body - cg);
                    double topJ = patchWater.HasValue && patchWater.Value > patchGround && !carried ? patchWater.Value : patchGround;
                    if ((wj.Z + surfaceOffsetZ) + topJ > 0.0) shareCount++;
                }
                if (shareCount < 1) shareCount = 1;
            }
            double share = p.Patch >= 0 && p.Patch == sharePatch ? 1.0 / shareCount : 1.0;

            double closing = -Vec3.Dot(vel, normal);          // + = moving INTO the surface
            bool ditching = onWater && !carried && !solidWins;
            if (!p.Touching)
            {
                p.Touching = true;
                impacts?.Record(closing, p.Name, ditching, w, vel);
                // On water whose forces are the airframe's own (waterByHydro) the break-up is load-based
                // (Wreck/AirframeFlooding.CheckJoints): a closing speed at first touch says nothing about the load once
                // the water's added mass has taken it up. A propeller blade in the water still bends.
                bool loadRuled = onWater && !carried && !solidWins && waterByHydro && p.Component != AirframeComponent.Propeller;
                if (broken is not null && p.BreakSpeedMs > 0 && closing > p.BreakSpeedMs && !loadRuled)
                {
                    if (!p.ArrivalPending || closing > p.ArrivalMs)
                    {
                        p.ArrivalPending = true; p.ArrivalMs = closing; p.ArrivalNormalBody = s.Attitude.Conjugate().Rotate(normal);
                    }
                    if (!broken.Contains(p.Component)) broken.Add(p.Component);
                }
            }
            if (ditching && waterByHydro)
            {
                // Buoyancy, drag and added mass are AirframeFlooding's. What is left for this point is the BED: a
                // sunk aeroplane comes to rest on the bottom like on any ground.
                double bed = (w.Z + surfaceOffsetZ) - (-groundH);
                if (bed <= 0.0) { if (record) { p.DepthM = depth; p.SurfaceCode = ContactPoint.SurfaceWater; } continue; }
                depth = bed;
                ditching = false;
            }
            double spring = (ditching ? WaterSpringNPerM : SpringNPerM) * depth * share * p.StiffnessScale;
            if (closing < 0.0) spring *= ReboundFraction;                        // crumpling, not a trampoline
            double n = spring + (ditching ? WaterDampNsPerM : DampNsPerM) * share * p.StiffnessScale * System.Math.Max(0.0, closing);
            // Coulomb friction against the tangential velocity (regularised at low speed): the surface's own coefficient
            // on the ground, FrictionMu on a building or in the water.
            WorldTerrain.Surface kind = WorldTerrain.Surface.Paved;
            double muSurface = FrictionMu;
            if (!solidWins && !ditching)
            {
                kind = WorldTerrain.SurfaceOverride ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(w.X, w.Y, -w.Z) : WorldTerrain.Surface.Paved);
                muSurface = SlidingMu(kind);
            }
            // Ditched, the point slides against the WATER, which may itself be moving (a river, the tide: WaterCurrent).
            Vec3 vSlide = ditching ? vel - WaterCurrent.At(w.X, w.Y, System.Math.Max(0.0, depth)) : vel;
            Vec3 vt = vSlide - normal * Vec3.Dot(vSlide, normal);
            double vtLen = vt.Length;
            if (record)
            {
                p.DepthM = depth; p.NormalN = n; p.SlipMs = vtLen; p.FrictionW = muSurface * n * vtLen;
                p.SurfaceCode = solidWins ? ContactPoint.SurfaceSolid : ditching ? ContactPoint.SurfaceWater : (int)kind;
            }
            // A propeller blade cuts or curls; it does not hold the aeroplane up (Aircraft reads its depth: the strike).
            if (p.PropDisc) continue;
            Vec3 f = normal * n;
            if (vtLen > 1e-4)
            {
                double mu = muSurface * System.Math.Min(1.0, vtLen / 0.3);
                f -= vt / vtLen * (mu * n);
            }
            if (p.PanelRef is { Bending: not null })
            {
                // A wing panel's point: held until the panel's whole push is known, then capped at what its hinges carry.
                p.Pending = true; p.PendingF = f; p.PendingR = rWorld;
                p.PendingK = (ditching ? WaterSpringNPerM : SpringNPerM) * share * p.StiffnessScale;
                p.PendingC = (ditching ? WaterDampNsPerM : DampNsPerM) * share * p.StiffnessScale;
                anyPanel = true;
                continue;
            }
            totalF += f;
            totalM += Vec3.Cross(rWorld, f);
            if (record) p.ForceBody = s.Attitude.Conjugate().Rotate(f);
            Aero.ForceDebug.Add(p.Body, s.Attitude.Conjugate().Rotate(f), Vec3.Zero, "contact");
        }
        if (anyPanel) CapPanels(points, s, record, ref totalF, ref totalM);
        return (totalF, totalM);
    }

    /// <summary>
    /// A wing panel is a cantilever on two plastic hinges at its station (<see cref="WingStructure"/>): the ground cannot
    /// push its tip harder than the hinges carry. The panel's points' forces are scaled together until their bending moment
    /// about the station and their torque about the flexural axis are within the hinges' caps; what they asked for, and
    /// the stiffness of that push about each axis, is recorded on the panel for the aeroplane's plastic step.
    /// </summary>
    private static void CapPanels(List<ContactPoint> points, RigidBodyState s, bool record, ref Vec3 totalF, ref Vec3 totalM)
    {
        Quat toBody = s.Attitude.Conjugate();
        for (int i = 0; i < points.Count; i++)
        {
            ContactPoint head = points[i];
            if (!head.Pending) continue;
            WingPanel w = head.PanelRef!;
            double up = 0, tw = 0, kU = 0, cU = 0, kT = 0, cT = 0;
            for (int j = i; j < points.Count; j++)
            {
                ContactPoint q = points[j];
                if (!q.Pending || q.PanelRef != w) continue;
                Vec3 fb = toBody.Rotate(q.PendingF);
                up += w.MomentUp(q.Body, fb);
                tw += w.TorqueNoseUp(q.Body, fb);
                (double lu, double lt) = w.Levers(q.Body);
                kU += q.PendingK * lu * lu; cU += q.PendingC * lu * lu;
                kT += q.PendingK * lt * lt; cT += q.PendingC * lt * lt;
            }
            double scale = 1.0;
            double capUp = up >= 0 ? w.CapUpNm : w.CapDownNm;
            if (System.Math.Abs(up) > capUp) scale = System.Math.Min(scale, capUp / System.Math.Abs(up));
            if (System.Math.Abs(tw) > w.CapTwistNm) scale = System.Math.Min(scale, w.CapTwistNm / System.Math.Abs(tw));
            for (int j = i; j < points.Count; j++)
            {
                ContactPoint q = points[j];
                if (!q.Pending || q.PanelRef != w) continue;
                q.Pending = false;
                Vec3 f = q.PendingF * scale;
                totalF += f;
                totalM += Vec3.Cross(q.PendingR, f);
                if (record) q.ForceBody = toBody.Rotate(f);
                Aero.ForceDebug.Add(q.Body, toBody.Rotate(f), Vec3.Zero, "contact");
            }
            if (record)
            {
                w.RawUpNm = up; w.RawTwistNm = tw; w.ContactScale = scale;
                w.KUpSpring = kU; w.KUpDamp = cU; w.KTwSpring = kT; w.KTwDamp = cT;
                w.ContactRecorded = true;
            }
        }
    }

    /// <summary>The patch's ground and water, sampled under its first point at the height of its LOWEST point: the
    /// ground query adds the wheel-scale bumps only for a point near the surface (WorldTerrain.HardPointGroundAt), and a
    /// patch must see them if any of its points could.</summary>
    private static void SamplePatch(List<ContactPoint> points, int head, Vec3 cg, RigidBodyState s, ref int samplePatch, ref double ground, ref double? water)
    {
        ContactPoint first = points[head];
        Vec3 w = s.Position + s.Attitude.Rotate(first.Body - cg);
        double up = -w.Z;
        for (int j = head + 1; j < points.Count && points[j].Patch == first.Patch; j++)
            up = System.Math.Min(up, -(s.Position.Z + s.Attitude.Rotate(points[j].Body - cg).Z));
        ground = WorldTerrain.HardPointGroundAt(w.X, w.Y, up);
        water = FloatHydro.WaterSurfaceAt(w.X, w.Y, up);
        samplePatch = first.Patch;
    }
}

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
    public static readonly System.Collections.Generic.List<IShape> Shapes = new();

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
        foreach (IShape sh in Shapes)
            if (sh.Penetrate(x, y, up, out Vec3 n, out double d)) return (n, d);
        return null;
    }

    /// <summary>A shape that never moves and can say where it is: the broadphase (<see cref="Nearby"/>) skips it unless its
    /// box overlaps the airframe's. Shapes without bounds (moving ones: the carrier, a tunnel's rocks) are always tested.</summary>
    public interface IBounded { void Bounds(out double minX, out double minY, out double minUp, out double maxX, out double maxY, out double maxUp); }

    /// <summary>A shape too big to bound (the forest covers the island) that can hand the broadphase a cheaper stand-in
    /// for one region: same answers for any point inside it.</summary>
    public interface IRegional { IShape Within(double minX, double minY, double minUp, double maxX, double maxY, double maxUp); }

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
                if (sh is IRegional rg) { _shapes.Add(rg.Within(_x0, _y0, _u0, _x1, _y1, _u1)); continue; }
                if (sh is IBounded bd)
                {
                    bd.Bounds(out double ax, out double ay, out double au, out double bx, out double by, out double bu);
                    if (bx < _x0 || ax > _x1 || by < _y0 || ay > _y1 || bu < _u0 || au > _u1) continue;
                }
                _shapes.Add(sh);
            }
        }

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

    /// <summary>Derive the hard points from the config geometry.</summary>
    public static List<ContactPoint> BuildPoints(AircraftConfig c)
    {
        var pts = new List<ContactPoint>();
        double rBody = c.Fuselage.Crossflow?.BodyRadiusM > 0 ? c.Fuselage.Crossflow.BodyRadiusM : 0.5;
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double minTe = double.MaxValue, maxLe = double.MinValue;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool vertical = id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            bool isWing = id.Contains("wing") && !id.Contains("aileron");
            bool isStab = id == "hstab";
            bool isFin = id == "vstab";
            foreach (StripConfig st in sf.Strips)
            {
                minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
                maxLe = System.Math.Max(maxLe, st.Pos[0] + 0.25 * st.Chord);
            }
            if (isWing)
            {
                StripConfig l = sf.Strips[0], r = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) { if (st.Pos[1] < l.Pos[1]) l = st; if (st.Pos[1] > r.Pos[1]) r = st; }
                // Wing root skin (the top of a high wing is what a flipped aircraft rests on; the bottom of a low
                // wing is what a belly landing rides on): one point each side of the centreline.
                StripConfig root = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1]) < System.Math.Abs(root.Pos[1])) root = st;
                double skin = root.Pos[2] < 0 ? root.Pos[2] - 0.07 * root.Chord : root.Pos[2] + 0.07 * root.Chord;
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], -0.8, skin), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "wing-root-L" });
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], 0.8, skin), Component = AirframeComponent.Cabin, BreakSpeedMs = CabinBreakMs, Name = "wing-root-R" });
                // Wing tips: a hard tip strike takes only the OUTER panel; a mid-span strike (0.45 semispan) takes the
                // inner panel and the outer with it (owner 2026-09-10).
                foreach ((StripConfig st, AirframeComponent comp, double sign) in new[] { (l, AirframeComponent.WingLeftOuter, -1.0), (r, AirframeComponent.WingRightOuter, 1.0) })
                {
                    double w = st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;
                    double y = st.Pos[1] + sign * w * 0.5;
                    double z = st.Pos[2] - System.Math.Abs(y) * System.Math.Tan(st.DihedralRad);
                    pts.Add(new ContactPoint { Body = new Vec3(st.Pos[0], y, z), Component = comp, BreakSpeedMs = 4.5, Name = comp + ":" + sf.Id });
                }
                double semi = System.Math.Max(System.Math.Abs(l.Pos[1]), System.Math.Abs(r.Pos[1]));
                foreach ((AirframeComponent comp, double sign) in new[] { (AirframeComponent.WingLeftInner, -1.0), (AirframeComponent.WingRightInner, 1.0) })
                {
                    double y = sign * semi * 0.45;
                    StripConfig near = root; foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1] - y) < System.Math.Abs(near.Pos[1] - y)) near = st;
                    double z = near.Pos[2] - System.Math.Abs(y) * System.Math.Tan(near.DihedralRad) + (near.Pos[2] < 0 ? -0.06 * near.Chord : 0.06 * near.Chord);
                    pts.Add(new ContactPoint { Body = new Vec3(near.Pos[0], y, z), Component = comp, BreakSpeedMs = 7.0, Name = comp + ":" + sf.Id });
                }
            }
            if (isStab)
            {
                StripConfig l = sf.Strips[0], r = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) { if (st.Pos[1] < l.Pos[1]) l = st; if (st.Pos[1] > r.Pos[1]) r = st; }
                pts.Add(new ContactPoint { Body = new Vec3(l.Pos[0], l.Pos[1] - 0.2, l.Pos[2]), Component = AirframeComponent.TailHorizontal, BreakSpeedMs = 5.0, Name = "stab-L" });
                pts.Add(new ContactPoint { Body = new Vec3(r.Pos[0], r.Pos[1] + 0.2, r.Pos[2]), Component = AirframeComponent.TailHorizontal, BreakSpeedMs = 5.0, Name = "stab-R" });
            }
            if (isFin && sf.Strips.Count > 0)
            {
                StripConfig top = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (st.Pos[2] < top.Pos[2]) top = st;
                double w = top.Chord > 1e-6 ? top.Area / top.Chord : 0.3;
                double topZ = top.Pos[2] - w * 0.5;
                if (topZ > -0.2) topZ = -0.2; // a fin that sits below the axis (low rudder): still give it a top
                pts.Add(new ContactPoint { Body = new Vec3(top.Pos[0], 0, topZ), Component = AirframeComponent.TailVertical, BreakSpeedMs = 5.0, Name = "fin-top" });
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
        // Propeller disc: the lowest blade tip. ANY ground contact is a prop strike (engine stops, blades bend).
        if (c.Propulsion is not null && c.Propulsion.PropDiameterM > 0)
        {
            double r = c.Propulsion.PropDiameterM * 0.5 * 0.85;
            if (c.Engines.Count == 0)
            {
                pts.Add(new ContactPoint { Body = new Vec3(noseX + 0.1, 0, axisZ + r), Component = AirframeComponent.Propeller, BreakSpeedMs = 0.15, Name = "prop-tip" });
            }
            else
            {
                foreach (EngineMount e in c.Engines)
                    pts.Add(new ContactPoint { Body = new Vec3(e.Pos[0] + 1.0, e.Pos[1], e.Pos[2] + r), Component = AirframeComponent.Propeller, BreakSpeedMs = 0.15, Name = "prop-tip" });
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
        // Tougher airframes (config impactStrength) take proportionally harder hits before anything breaks — except the
        // prop strike, which is a ground touch at any speed.
        if (c.ImpactStrength > 0 && System.Math.Abs(c.ImpactStrength - 1.0) > 1e-9)
            foreach (ContactPoint p in pts) if (p.Component != AirframeComponent.Propeller && p.BreakSpeedMs > 0) p.BreakSpeedMs *= c.ImpactStrength;
        return pts;
    }

    /// <summary>
    /// How far above the ground the whole airframe is unambiguously clear of it. Terrain cannot rise by this much
    /// inside the few metres an airframe spans — towers, buildings and bridge decks are <see cref="WorldSolids"/>,
    /// which is a bounded list test and is still checked at every altitude: when the airframe is clear of the ground
    /// only the ground and water queries are skipped, every hard point is still tested against the solids
    /// (<see cref="ComputeSolidsOnly"/>; before 2026-11-02 the shortcut returned nothing at all, so above 60 m agl a
    /// tower, the arch or a hangar roof could be flown through).
    /// </summary>
    private const double ClearAboveM = 60.0;

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
    {
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
            foreach (Vec3 b in wheels)
            {
                Vec3 rWorld = s.Attitude.Rotate(b - cg);
                double horiz = System.Math.Sqrt(rWorld.X * rWorld.X + rWorld.Y * rWorld.Y);
                if (horiz > reach) reach = horiz;
                double z = s.Position.Z + rWorld.Z;
                if (z > lowestZ) lowestZ = z;
            }
        if (lowestZ == double.MinValue) return false;
        double highestGround = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
        foreach ((double dx, double dy) in new[] { (reach, 0.0), (-reach, 0.0), (0.0, reach), (0.0, -reach) })
        {
            double g = WorldTerrain.GroundHeightAt(s.Position.X + dx, s.Position.Y + dy);
            if (g > highestGround) highestGround = g;
        }
        // Water counts as ground here: a float or a hull touches it.
        double? water = FloatHydro.WaterSurfaceAt(s.Position.X, s.Position.Y, -s.Position.Z);
        if (water.HasValue && water.Value > highestGround) highestGround = water.Value;
        return -lowestZ - highestGround > ClearAboveM + extraM;
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
    {
        long tClear = SimProfile.Begin();
        bool clear = points.Count > 0 && WorldTerrain.Active != null && ClearOfTheGround(points, cg, s);
        SimProfile.End(SimProfile.Scope.ContactClear, tClear);
        if (clear)
        {
            // No clearance remembered for the later stages: if one of them is not clear, it tests every point.
            if (stage == 0) foreach (ContactPoint p in points) p.StageClearM = double.NegativeInfinity;
            long tSolids = SimProfile.Begin();
            var solidsOnly = ComputeSolidsOnly(points, cg, s, broken, impacts);
            SimProfile.End(SimProfile.Scope.ContactPoints, tSolids);
            return solidsOnly;
        }
        long tPoints = SimProfile.Begin();
        var result = ComputeEveryPoint(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, stage, dt, groundClear: false);
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
        => ComputeEveryPoint(points, cg, s, broken, surfaceOffsetZ, impacts, floatsCarryWater, waterByHydro, -1, 0.0, groundClear: false);

    /// <summary>
    /// The hard points against <see cref="WorldSolids"/> only, for an airframe already known to be clear of the ground
    /// and the water (<see cref="ClearOfTheGround(List{ContactPoint}, IReadOnlyList{Vec3}?, Vec3, RigidBodyState, double)"/>):
    /// no height queries, one box-list test per point. Buildings, the tower, the arch legs and the sea-cave roof stand
    /// higher than <see cref="ClearAboveM"/> above the ground under them, so this runs at every altitude.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) ComputeSolidsOnly(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, ImpactRecorder? impacts)
        => ComputeEveryPoint(points, cg, s, broken, 0.0, impacts, false, false, -1, 0.0, groundClear: true);

    private static (Vec3 Force, Vec3 Moment) ComputeEveryPoint(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ, ImpactRecorder? impacts, bool floatsCarryWater, bool waterByHydro, int stage, double dt, bool groundClear)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        bool cull = StageCullEnabled && stage >= 0;
        // Broadphase: the solids near the airframe's box, gathered once and kept while the airframe stays in their region.
        double reach = 0;
        foreach (ContactPoint p in points) { double r = (p.Body - cg).Length; if (r > reach) reach = r; }
        reach += 0.5;
        var nearby = WorldSolids.NearbyFor(points);
        nearby.Update(s.Position.X - reach, s.Position.Y - reach, -s.Position.Z - reach, s.Position.X + reach, s.Position.Y + reach, -s.Position.Z + reach);
        foreach (ContactPoint p in points)
        {
            Vec3 rBody = p.Body - cg;
            Vec3 rWorld = s.Attitude.Rotate(rBody);
            Vec3 w = s.Position + rWorld;
            Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));   // world velocity of the point

            Vec3 normal = new(0, 0, -1);   // outward (NED up)
            double depth;
            bool onWater = false;
            double groundH = double.NegativeInfinity;   // the ditching bed below needs it; unset only where onWater stays false
            if (groundClear || (cull && stage > 0 && p.StageClearM > p.StageMarginM))
            {
                depth = double.NegativeInfinity;   // clear of the ground and the water all step (see StageCullMargin)
            }
            else
            {
            // Ground (with the wheel-scale bumps when the point is low enough to meet them) — normal is world up.
            groundH = WorldTerrain.HardPointGroundAt(w.X, w.Y, -w.Z);
            double? waterH = FloatHydro.WaterSurfaceAt(w.X, w.Y, -w.Z);
            onWater = waterH.HasValue && waterH.Value >= groundH - 0.01;
            // On water, a SEAPLANE's hull is FloatHydro's business and this must keep its hands off it. A wheeled
            // aeroplane's is nobody's: every point over water used to return no force at all, so a landplane that
            // ditched sank through the sea, through the bed under it, and went on falling for ever — which is what
            // Ty saw as the Pitts "freezing" about thirty seconds into a flight out of the aerobatic box, the box
            // being over the sea. Water now pushes back on an airframe that cannot float, at a tenth of the ground's
            // stiffness and three times its damping: an arrival, not a trampoline, and not a runway either.
            depth = onWater
                ? (floatsCarryWater ? -1.0 : (w.Z + surfaceOffsetZ) - (-waterH!.Value))
                : (w.Z + surfaceOffsetZ) - (-groundH);
            if (cull && stage == 0)
            {
                // Clearance over whichever is higher, ground or water: either branch above gives a depth no deeper.
                double top = waterH.HasValue && waterH.Value > groundH ? waterH.Value : groundH;
                p.StageClearM = -(w.Z + surfaceOffsetZ) - top;
                p.StageMarginM = StageCullMargin(vel.Length, dt);
            }
            }
            // Solids (buildings etc.): whichever is deeper.
            var solid = nearby.Penetration(w.X, w.Y, -w.Z);
            bool solidWins = solid.HasValue && solid.Value.depth > depth;
            if (solidWins) { normal = solid!.Value.normalNed; depth = solid.Value.depth; }

            if (depth <= 0.0) { p.Touching = false; continue; }

            double closing = -Vec3.Dot(vel, normal);          // + = moving INTO the surface
            bool ditching = onWater && !floatsCarryWater && !solidWins;
            if (!p.Touching)
            {
                p.Touching = true;
                impacts?.Record(closing, p.Name, ditching, w, vel);
                // On water whose forces are the airframe's own (waterByHydro) the break-up is load-based
                // (Wreck/AirframeFlooding.CheckJoints): a closing speed at first touch says nothing about the load once
                // the water's added mass has taken it up. A propeller blade in the water still bends.
                bool loadRuled = onWater && !floatsCarryWater && !solidWins && waterByHydro && p.Component != AirframeComponent.Propeller;
                if (broken is not null && p.BreakSpeedMs > 0 && closing > p.BreakSpeedMs && !broken.Contains(p.Component) && !loadRuled)
                {
                    broken.Add(p.Component);
                }
            }
            if (ditching && waterByHydro)
            {
                // Buoyancy, drag and added mass are AirframeFlooding's. What is left for this point is the BED: a
                // sunk aeroplane comes to rest on the bottom like on any ground.
                double bed = (w.Z + surfaceOffsetZ) - (-groundH);
                if (bed <= 0.0) continue;
                depth = bed;
                ditching = false;
            }
            double spring = (ditching ? WaterSpringNPerM : SpringNPerM) * depth;
            if (closing < 0.0) spring *= ReboundFraction;                        // crumpling, not a trampoline
            double n = spring + (ditching ? WaterDampNsPerM : DampNsPerM) * System.Math.Max(0.0, closing);
            Vec3 f = normal * n;
            // Coulomb friction against the tangential velocity (regularised at low speed).
            Vec3 vt = vel - normal * Vec3.Dot(vel, normal);
            double vtLen = vt.Length;
            if (vtLen > 1e-4)
            {
                double mu = FrictionMu * System.Math.Min(1.0, vtLen / 0.3);
                f -= vt / vtLen * (mu * n);
            }
            totalF += f;
            totalM += Vec3.Cross(rWorld, f);
            Aero.ForceDebug.Add(p.Body, s.Attitude.Conjugate().Rotate(f), Vec3.Zero, "contact");
        }
        return (totalF, totalM);
    }
}

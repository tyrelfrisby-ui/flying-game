using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// HINGED WHEEL-SKIS (Ty 2026-11-05 07:58–07:59): "wheel skis as an option on every airplane. when chosen a selector for
/// 'wheels/skis' gets put in the cockpit someplace … that raises or lowers the skis they rotate forward and up when raised
/// and rotate down and aft when lowered and the tire sits on top of the ski when lowered instead of poking down below".
///
/// THE MECHANISM. Each wheeled leg carries a ski hung from its AXLE on a pair of parallel links (one each side of the
/// tyre), so the ski swings on a circle about the axle and stays parallel to the airframe while it does. The link angle
/// θ is measured from straight down, positive with the link's lower end FORWARD:
/// <list type="bullet">
/// <item><b>SKIS (lowered)</b>, θ = <see cref="LoweredLinkRad"/> (12° aft of straight down, i.e. just over centre, so a
///   load on the ski pushes the links into their stop instead of folding them): the ski's top lies on the tyre's bottom —
///   the tyre SITS ON the ski — and the ski's bottom is the lowest thing on the leg by the ski's thickness.</item>
/// <item><b>WHEELS (raised)</b>, θ = <see cref="RaisedLinkRad"/> (80° forward): the links have swung the ski forward and up,
///   the tyre now comes down through the ski's slot and stands below the ski's bottom, and the tyre meets the ground.</item>
/// </list>
/// The link radius is the axle-to-ski-top distance (the tyre's radius plus the seating gap), so the links pivot exactly on
/// the axle — unless the tyre is too small for such short links to lift the ski clear, when they are lengthened and
/// pivot higher up the leg (<see cref="RaisedClearanceM"/>). Nothing else is chosen per aeroplane: the ski's length and width follow the fuselage (the rule the old
/// procedural ski already used: a Super Cub's main ski is about 1.7 × 0.3 m, a Beaver's 2.4 × 0.4 m), its thickness the
/// length, its links the tyre, and everything the physics and the drawing need — where the ski is, where its bottom is,
/// how long the slot must be for the tyre to pass — is computed from those by the kinematics below. The slot is the set of
/// points of the ski the tyre would otherwise pass through on the way up (<see cref="Geometry.SlotAftM"/>): measured, not
/// drawn, and it lies AFT of where the tyre rests when the skis are down, so a lowered ski has solid ski under the tyre.
///
/// THE CONTACT. The ski pitches freely on its pedestal pin (it does on every real ski: a bungee holds the tip up in the air
/// and a check cable stops it dropping too far), so on the ground it lies on the ground and its load goes in under the pin;
/// past its pitch stops (<see cref="TipDownLimitRad"/>, <see cref="TipUpLimitRad"/>) it is held at the stop and its heel or
/// the front of its flat bears instead. Whichever of the ski's lowest point and the tyre's bottom is deeper in the ground
/// carries the leg (<see cref="Probe"/>): lowered that is always the ski, raised always the tyre, and half way it is
/// whatever the geometry says — a ski swinging down under an aeroplane standing on its wheels lifts it, as it does in life.
/// </summary>
public static class SkiGear
{
    /// <summary>Link angle with the skis DOWN (rad from straight down, + = lower end forward): 12° aft, over centre. The
    /// tyre then rests r·sin 12° = 0.21 r AHEAD of the pin, on solid ski: the slot the tyre comes down through when the
    /// skis are up always reaches to about the pin (in the ski's frame the tyre's centre runs on a circle of radius r
    /// about it), so the over-centre angle is what puts ski under the tyre.</summary>
    public const double LoweredLinkRad = -12.0 * System.Math.PI / 180.0;
    /// <summary>Link angle with the skis UP: 80° forward. The ski has risen r(cos 12° − cos 80°) = 0.80 r.</summary>
    public const double RaisedLinkRad = 80.0 * System.Math.PI / 180.0;
    /// <summary>Gap between the tyre's bottom and the ski's top with the skis down: the tyre sits on the ski (2 mm so the
    /// two surfaces are not drawn into each other).</summary>
    public const double SeatGapM = 0.002;
    /// <summary>How far the ski may pitch on its pin, relative to the airframe (ESTIMATE, Ty to check; the real stops are
    /// a check cable and a bungee). Tip-down 20° lets a taildragger's ski lie flat in the three-point attitude (a Cub sits
    /// about 11° nose-up); tip-up 15°.</summary>
    public const double TipDownLimitRad = 20.0 * System.Math.PI / 180.0, TipUpLimitRad = 15.0 * System.Math.PI / 180.0;
    /// <summary>The least the raised ski's bottom stands above the tyre's bottom (so a tyre's static squash cannot bring it
    /// down onto the ground).</summary>
    public const double RaisedClearanceM = 0.04;
    /// <summary>The selector's full travel, s (ESTIMATE, Ty to check: an electric hydraulic pump on Wipline-type gear
    /// takes several seconds). Hydraulic, so the skis keep moving at this rate whatever the airspeed.</summary>
    public const double TransitSec = 8.0;

    /// <summary>
    /// Mass of the installation per square metre of ski (ESTIMATE): ski, pedestal, links, cables, the actuator's share.
    /// 22 kg/m² gives a Super Cub (two 1.73 × 0.30 m mains and a small tail ski) about 26 kg, a Beaver about 50 kg —
    /// the published weight increase of a wheel-ski kit runs 50–80 lb on a Cub and 150–200 lb on a Beaver.
    /// </summary>
    public const double ArealMassKgPerM2 = 22.0;
    /// <summary>Drag coefficient on the ski's frontal area (thickness + the upturned tip's rise) × width, plus the links
    /// and pedestal (ESTIMATE): a blunt plate in the free stream, 0.8.</summary>
    public const double FrontalCd = 0.8;
    /// <summary>The upturned tip: a share of the ski's length, at this angle (the old procedural ski's proportions).</summary>
    public const double TipLengthShare = 0.3, TipAngleRad = 32.0 * System.Math.PI / 180.0;

    /// <summary>One leg's ski, in the config's body frame (+x forward, +y right, +z down), all derived from the leg.</summary>
    public sealed class Geometry
    {
        /// <summary>The axle (the tyre's centre), the links' upper pivot, and the leg's tyre bottom (GearConfig.Pos). The
        /// pivot is the axle unless the tyre is too small for links that short to lift the ski clear; then it is higher
        /// up the leg, straight above the ski's pin with the skis down.</summary>
        public Vec3 Axle, Pivot, TyreBottom;
        public double TyreRadiusM, LinkM;
        /// <summary>The ski's flat running length, its width, its thickness (top to bottom).</summary>
        public double LengthM, WidthM, ThicknessM;
        /// <summary>The flat's ends relative to the pin, along the ski (+ forward): front of the flat, and the heel.</summary>
        public double FlatFwdM, FlatAftM;
        /// <summary>The slot the tyre passes through, relative to the pin along the ski (both negative: aft of the pin,
        /// i.e. aft of where the tyre rests on a lowered ski), and its width (the tyre's section plus a margin).</summary>
        public double SlotFwdM, SlotAftM, SlotWidthM;
        public double MassKg, DragAreaM2;
        /// <summary>The pin, the ski's top under the pedestal: the link's length from the pivot, at the link's angle.</summary>
        public Vec3 PinAt(double linkRad) => Pivot + new Vec3(LinkM * System.Math.Sin(linkRad), 0, LinkM * System.Math.Cos(linkRad));
        /// <summary>The pin's point on the ski's BOTTOM with the ski parallel to the airframe.</summary>
        public Vec3 BottomUnderPinAt(double linkRad) => PinAt(linkRad) + new Vec3(0, 0, ThicknessM);
    }

    /// <summary>Does this leg take a ski? Every wheel does; a float keel, a skid or a pair of feet is not a wheel.</summary>
    public static bool LegTakesSki(GearConfig g) => WheelSpin.IsWheel(g);

    /// <summary>Is a ski fitted to this leg of this aeroplane (the session option is on and the leg is a wheel)?</summary>
    public static bool Fitted(AircraftConfig c, GearConfig g) => c.Skis && LegTakesSki(g);

    /// <summary>Is the option fitted to anything on this aeroplane?</summary>
    public static bool AnyFitted(AircraftConfig c)
    {
        if (!c.Skis || c.Gear == null) return false;
        foreach (GearConfig g in c.Gear) if (LegTakesSki(g)) return true;
        return false;
    }

    /// <summary>Link angle at selector position <paramref name="lowered01"/> (0 = WHEELS, raised; 1 = SKIS, lowered).</summary>
    public static double LinkRad(double lowered01)
    {
        double t = System.Math.Clamp(double.IsFinite(lowered01) ? lowered01 : 1.0, 0.0, 1.0);
        return RaisedLinkRad + (LoweredLinkRad - RaisedLinkRad) * t;
    }

    /// <summary>The leg's ski geometry.</summary>
    public static Geometry For(AircraftConfig c, GearConfig g)
    {
        double r = LandingGear.TireRadiusOf(c, g);
        double fusLen = c.Fuselage?.Crossflow != null && c.Fuselage.Crossflow.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        bool tail = g.IsTailwheel;
        bool nose = !tail && g.IsSteerable;
        // A quarter of the fuselage for a main, a twelfth for the tail ski, and a nose ski in between (the old rule had no
        // nose case and would have hung a 2 m ski on a 172's nosewheel).
        double length = System.Math.Max(fusLen * (tail ? 0.08 : nose ? 0.16 : 0.25), r * 2.4);
        double width = System.Math.Max(fusLen * 0.042 * (tail ? 0.6 : nose ? 0.8 : 1.0), r * 0.8);
        double thick = System.Math.Clamp(length * 0.03, 0.035, 0.08);
        // Links on the axle are (r + gap)/cos θ_L long and lift the ski (cos θ_L − cos θ_R) of that. A small tyre under a
        // thick ski needs longer links to clear it by RaisedClearanceM; their pivot then moves up the leg.
        double onAxle = (r + SeatGapM) / System.Math.Cos(LoweredLinkRad);
        double link = System.Math.Max(onAxle, (thick + RaisedClearanceM) / (System.Math.Cos(LoweredLinkRad) - System.Math.Cos(RaisedLinkRad)));
        // The pivot straight above the axle, at the height that puts the lowered ski's top on the tyre's bottom.
        Vec3 axle = g.PosVec() + new Vec3(0, 0, -r);
        var geo = new Geometry
        {
            TyreBottom = g.PosVec(),
            Axle = axle,
            Pivot = new Vec3(axle.X, axle.Y, g.Pos[2] + SeatGapM - link * System.Math.Cos(LoweredLinkRad)),
            TyreRadiusM = r,
            LinkM = link,
            LengthM = length, WidthM = width, ThicknessM = thick,
        };
        // The axle a little aft of the ski's middle (8 % of its length), so most of the ski is ahead of the wheel and it
        // rides up over the snow instead of digging in.
        geo.FlatFwdM = 0.5 * length + 0.08 * length;
        geo.FlatAftM = -(0.5 * length - 0.08 * length);
        TireSpec? t = TireCatalog.Physical(g);
        double tyreWidth = t != null ? t.SectionWidthM * System.Math.Max(1, t.Count) : r * 0.6;
        geo.SlotWidthM = System.Math.Min(width * 0.85, tyreWidth + 0.03);
        SweepSlot(geo);
        double tipRise = TipLengthShare * length * System.Math.Sin(TipAngleRad);
        double area = width * length * (1 + TipLengthShare);
        geo.MassKg = ArealMassKgPerM2 * area;
        geo.DragAreaM2 = FrontalCd * width * (thick + tipRise);
        return geo;
    }

    /// <summary>
    /// The slot, from the kinematics: step the links from lowered to raised and, at each angle, find where the tyre's
    /// circle comes above the ski's top plane; in the ski's frame that chord is a part of the ski the tyre must pass
    /// through. The union is the slot (5 mm more at its front, 1 cm at its back). Lowered, the tyre is tangent to the
    /// ski's top 0.21 r ahead of the pin.
    /// </summary>
    private static void SweepSlot(Geometry geo)
    {
        double fwd = double.NegativeInfinity, aft = double.PositiveInfinity;
        const int n = 400;
        for (int i = 0; i <= n; i++)
        {
            double th = LoweredLinkRad + (RaisedLinkRad - LoweredLinkRad) * i / n;
            Vec3 pin = geo.PinAt(th);
            // Height of the ski's top above the tyre's bottom (body z is down).
            double rise = geo.TyreBottom.Z - pin.Z;
            if (rise <= 0) continue;
            double half = System.Math.Sqrt(System.Math.Max(0, 2 * geo.TyreRadiusM * rise - rise * rise));
            // The tyre's centre is at the axle's x; in the ski's frame (relative to the pin) that is axle.x - pin.x.
            double cx = geo.Axle.X - pin.X;
            fwd = System.Math.Max(fwd, cx + half);
            aft = System.Math.Min(aft, cx - half);
        }
        if (double.IsInfinity(fwd)) { geo.SlotFwdM = geo.SlotAftM = 0; return; }
        geo.SlotFwdM = fwd + 0.005;
        geo.SlotAftM = aft - 0.01;
    }

    /// <summary>Total installed mass of the option on this aeroplane.</summary>
    public static double TotalMassKg(AircraftConfig c)
    {
        double m = 0;
        if (!c.Skis) return 0;
        foreach (GearConfig g in c.Gear) if (LegTakesSki(g)) m += For(c, g).MassKg;
        return m;
    }

    /// <summary>First moment of the installed mass about the config's CG, kg·m (body frame), taken at each ski's pin with
    /// the skis down; the few centimetres the links move it are left out.</summary>
    public static Vec3 MassMomentKgM(AircraftConfig c)
    {
        Vec3 sum = Vec3.Zero, cg = c.Mass.CgVec();
        if (!c.Skis) return sum;
        foreach (GearConfig g in c.Gear)
        {
            if (!LegTakesSki(g)) continue;
            Geometry geo = For(c, g);
            sum += (geo.PinAt(LoweredLinkRad) - cg) * geo.MassKg;
        }
        return sum;
    }

    /// <summary>Flat-plate drag area of every ski, m² (applied at the CG with the rest of the parasite drag).</summary>
    public static double TotalDragAreaM2(AircraftConfig c)
    {
        double a = 0;
        if (!c.Skis) return 0;
        foreach (GearConfig g in c.Gear) if (LegTakesSki(g)) a += For(c, g).DragAreaM2;
        return a;
    }

    /// <summary>The geometry of every leg (null where the leg takes no ski), built once per aeroplane.</summary>
    public static Geometry?[] ForAll(AircraftConfig c)
    {
        var all = new Geometry?[c.Gear.Count];
        for (int i = 0; i < all.Length; i++) all[i] = Fitted(c, c.Gear[i]) ? For(c, c.Gear[i]) : null;
        return all;
    }

    /// <summary>What the ski of one leg is doing against the ground.</summary>
    public struct Contact
    {
        /// <summary>The ski's lowest point, world (NED), and body (relative to the config's origin).</summary>
        public Vec3 PointWorld, PointBody;
        /// <summary>Depth of that point below the ground under it along the vertical (+ = in the ground).</summary>
        public double PenetrationM;
        /// <summary>The ski's pitch on its pin relative to the airframe (+ = tip up), as it lies.</summary>
        public double PitchRad;
    }

    /// <summary>
    /// Where the ski of this leg bears, for the aeroplane in state <paramref name="s"/> (its CG at s.Position) over the
    /// world's ground (<see cref="WorldTerrain.WheelGroundHeightAt(double, double, double)"/>, its normal
    /// <see cref="WorldTerrain.ContactNormalAt"/>), the contact datum at NED z = <paramref name="groundZ"/> as in
    /// LandingGear.Compute.
    /// The ski lies on the ground if its stops allow it, so its lowest point is under the pin; held at a stop, it is the
    /// lower end of its flat.
    /// </summary>
    public static Contact Probe(Geometry geo, Vec3 cg, RigidBodyState s, double lowered01, double groundZ)
    {
        double th = LinkRad(lowered01);
        Vec3 pin = geo.PinAt(th);
        Vec3 pinW = s.Position + s.Attitude.Rotate(pin - cg);
        double gH = WorldTerrain.WheelGroundHeightAt(pinW.X, pinW.Y, -pinW.Z);
        Vec3 up = WorldTerrain.ContactNormalAt(pinW.X, pinW.Y, gH);     // world NED, pointing up (z < 0)
        // The ski's pitch that lays its bottom flat on the ground: the angle between the airframe's x axis and the
        // ground plane, measured in the airframe's own x–z plane.
        Vec3 bx = s.Attitude.Rotate(new Vec3(1, 0, 0)), bz = s.Attitude.Rotate(new Vec3(0, 0, 1));
        // Ski forward axis at pitch p (+ tip up) is bx·cos p − bz·sin p; flat when it is perpendicular to 'up':
        // (bx·up) cos p − (bz·up) sin p = 0  →  tan p = (bx·up)/(bz·up), on the branch where the ski's bottom faces down
        // (bz·up < 0 for an aeroplane the right way up): nose up by θ gives p = −θ, the tip down onto the ground.
        double want = System.Math.Atan2(-Vec3.Dot(bx, up), -Vec3.Dot(bz, up));
        double pitch = System.Math.Clamp(want, -TipDownLimitRad, TipUpLimitRad);
        Vec3 skiFwd = bx * System.Math.Cos(pitch) - bz * System.Math.Sin(pitch);
        Vec3 skiDown = bz * System.Math.Cos(pitch) + bx * System.Math.Sin(pitch);
        Vec3 best = pinW + skiDown * geo.ThicknessM;
        double gB = gH;
        if (System.Math.Abs(want - pitch) > 1e-9)
        {
            // Held at a stop: one end of the flat is lower than the middle.
            Vec3 front = best + skiFwd * geo.FlatFwdM, heel = best + skiFwd * geo.FlatAftM;
            best = Vec3.Dot(front, up) < Vec3.Dot(heel, up) ? front : heel;   // NED: lower = less 'up'
            gB = WorldTerrain.WheelGroundHeightAt(best.X, best.Y, -best.Z);
        }
        Vec3 body = s.Attitude.Conjugate().Rotate(best - s.Position) + cg;
        return new Contact { PointWorld = best, PointBody = body, PenetrationM = best.Z - (groundZ - gB), PitchRad = pitch };
    }

    /// <summary>The same in the aeroplane's own frame, standing level with nothing under it (for drawing and resting):
    /// the pin and the ski parallel to the airframe.</summary>
    public static Vec3 LowestBody(Geometry geo, double lowered01) => geo.BottomUnderPinAt(LinkRad(lowered01));

    // ---- the ski base's friction on what it is on ------------------------------------------------------------------

    /// <summary>
    /// A ski base's SLIDING coefficient on ground that is not snow (ESTIMATES, Ty to check): a UHMW / aluminium running
    /// surface dragged over dry asphalt or concrete is in the plastic-on-rough-stone band, 0.3–0.4; over gravel it also
    /// ploughs the stones; on grass it slides (ski aeroplanes do use grass); on wet glacier ice it is metal-on-ice, 0.03
    /// (the keel table). Snow and the packed zone keep the ski's own anisotropic law (<see cref="LandingGear.SkiRolling(WorldTerrain.Surface, double)"/>).
    /// </summary>
    public static double HardSlidingCoefficient(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Paved => 0.35,
        WorldTerrain.Surface.Gravel => 0.45,
        WorldTerrain.Surface.Dirt => 0.40,
        WorldTerrain.Surface.Sand => 0.40,
        WorldTerrain.Surface.Grass => 0.20,
        WorldTerrain.Surface.Rough => 0.30,
        WorldTerrain.Surface.GlacierIce => 0.03,
        _ => 0.40,
    };

    /// <summary>Snow the ski RUNS on (its anisotropic law: free fore-and-aft, held sideways only by its edge).</summary>
    public static bool IsSnow(WorldTerrain.Surface s) => s == WorldTerrain.Surface.Snow || s == WorldTerrain.Surface.PackedGlacierSnow;

    /// <summary>Static over kinetic on hard ground: a block of plastic at rest on stone holds about 1.3× its sliding value.</summary>
    public const double HardStaticFactor = 1.3;

    /// <summary>A ski base's resistance to sliding forward, μ, at this speed on this surface.</summary>
    public static double ForwardCoefficient(WorldTerrain.Surface s, double speedMs)
    {
        if (IsSnow(s)) return LandingGear.SkiRolling(s, speedMs);
        double film = System.Math.Clamp(System.Math.Abs(speedMs) / LandingGear.SkiFilmSpeedMs, 0, 1);
        return HardSlidingCoefficient(s) * (HardStaticFactor - (HardStaticFactor - 1) * film);
    }

    /// <summary>A ski base's sideways hold, μ, at this speed: the edge in snow, plain sliding on anything hard.</summary>
    public static double LateralCoefficient(WorldTerrain.Surface s, double speedMs)
    {
        if (IsSnow(s)) return LandingGear.SkiLateralCoefficient(s) + LandingGear.SkiStaticExcess(s, speedMs);
        return ForwardCoefficient(s, speedMs);
    }
}

/// <summary>
/// The cockpit's WHEELS / SKIS selector and the hydraulics it commands (Ty 2026-11-05). One per aeroplane; the physics
/// reads <see cref="Lowered01"/>, the lever and its annunciator read <see cref="Commanded"/> and <see cref="Annunciation"/>,
/// and every input (touch, click, key, the Quest's hand) calls <see cref="Select"/> / <see cref="Toggle"/>.
/// </summary>
public sealed class SkiSelector
{
    public enum Position { Wheels, Skis }
    public enum Light { Wheels, Skis, Transit }

    /// <summary>Where the lever is.</summary>
    public Position Commanded { get; private set; }
    /// <summary>Where the skis are: 0 = raised (WHEELS), 1 = lowered (SKIS).</summary>
    public double Lowered01 { get; private set; }
    public double TransitSec { get; }

    public SkiSelector(Position start, double transitSec = SkiGear.TransitSec)
    {
        Commanded = start;
        Lowered01 = start == Position.Skis ? 1.0 : 0.0;
        TransitSec = transitSec > 0 ? transitSec : SkiGear.TransitSec;
    }

    public void Select(Position p) => Commanded = p;
    public void Toggle() => Commanded = Commanded == Position.Skis ? Position.Wheels : Position.Skis;

    /// <summary>The hydraulics run the skis toward the lever at a constant rate.</summary>
    public void Step(double dt)
    {
        if (dt <= 0) return;
        double target = Commanded == Position.Skis ? 1.0 : 0.0;
        double step = dt / TransitSec;
        Lowered01 = Lowered01 < target ? System.Math.Min(target, Lowered01 + step) : System.Math.Max(target, Lowered01 - step);
    }

    /// <summary>What the annunciator shows: the position once the skis are in it, TRANSIT while they move (or stop short).</summary>
    public Light Annunciation => Lowered01 >= 1.0 - 1e-9 ? Light.Skis : Lowered01 <= 1e-9 ? Light.Wheels : Light.Transit;

    /// <summary>
    /// Where the lever starts for a flight: SKIS over snow (the reason the option is fitted), WHEELS over anything else —
    /// a pilot taxiing out on pavement has the skis up. A start choice, nothing more; the pilot moves it from there.
    /// </summary>
    public static Position StartFor(WorldTerrain.Surface underneath) =>
        SkiGear.IsSnow(underneath) ? Position.Skis : Position.Wheels;
}

/// <summary>
/// One aeroplane's wheel-skis while it flies: the selector, each leg's geometry (built once), and what each ski did last
/// step (for the drawing: its pitch on the pin, and whether it was the part on the ground).
/// </summary>
public sealed class SkiState
{
    public readonly SkiSelector Selector;
    public readonly SkiGear.Geometry?[] Legs;
    public readonly double[] PitchRad;
    public readonly bool[] Bearing;

    public SkiState(AircraftConfig c, SkiSelector.Position start)
    {
        Selector = new SkiSelector(start);
        Legs = SkiGear.ForAll(c);
        PitchRad = new double[Legs.Length];
        Bearing = new bool[Legs.Length];
    }

    /// <summary>For an aeroplane with the option fitted; null for one without.</summary>
    public static SkiState? For(AircraftConfig c, SkiSelector.Position start) => SkiGear.AnyFitted(c) ? new SkiState(c, start) : null;

    public double Lowered01 => Selector.Lowered01;
}

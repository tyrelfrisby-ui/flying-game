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
    // ---- THE SKI'S BOTTOM: ONE LONG SHALLOW SWEEP, NOT A PLATE WITH A BENT END --------------------------------------
    //
    // Ty, 2026-11-10, on the 2026-11-05 renders: "The front of the skis should curve up not a sharp angle", and then
    // reference photographs of his own choosing: an Aero Ski pair, a Federal A-1500 pair, and a page of Federal skis
    // from several angles. Those photographs are the standard this is held to (his standing rule of 2026-10-25: the
    // photographs are the accuracy check, not a scaled drawing and not anybody's recollection).
    //
    // WHAT WAS HERE, and what is wrong with it. TipLengthShare 0.3 / TipAngleRad 32 deg: a flat plank with a second
    // flat plank hinged off its front at 32 degrees. Two things wrong. It has a CREASE, which is what Ty objected to
    // and which no ski has — a crease digs into snow and is a stress riser in the sheet. And it is the wrong SHAPE
    // even without the crease: a real ski's bottom is one long shallow sweep that is dead flat under the pedestal and
    // is already rising well before you can point at where it started. It is not a plate with a curved end.
    //
    // THE MEASUREMENT. Of the photographs Ty sent, one is a near side-on shot of an Aero Ski hung under an aeroplane,
    // white composite against dark grass, which separates cleanly. Its bottom edge was traced by threshold (every
    // column's lowest bright pixel), not read off by eye, over the ski's whole 561-pixel length:
    //
    //     u (from the nose)  0.01  0.05  0.12  0.19  0.26  0.33  0.40  0.49 .. 0.88  0.94  0.97  1.00
    //     rise above flat    .119  .094  .053  .030  .014  .007  .005  .000 .. .000  .004  .016  .032   (x length)
    //
    // So: the nose stands 0.127 of the ski's length above the running surface; the bottom is flat from 0.45 to 0.88 of
    // the length back from the nose — which is where the pedestal sits, on both installations, and is why a ski does
    // not rock; and the TAIL sweeps up too, 0.032 of the length, over the aft eighth. The forward sweep spans 0.45 of
    // the length, and it leaves the flat with zero slope AND zero curvature, which is why there is no point in the
    // photograph you could call the start of it.
    //
    // THE CURVE. A power law in the distance from where the sweep begins fits that trace and has the right end
    // behaviour for free (flat and curvature-free at the join, steepest at the nose):
    //
    //     rise(u) = NoseRiseShare * L * ((uF - u) / uF)^n        for u < uF
    //
    // Fitting n to the ten traced points forward of the flat gives 2.49 at uF = 0.45 (2.33 at 0.42, 2.64 at 0.48);
    // 2.5 is used. The nose's own slope then comes out at atan(n * 0.127 / 0.45) = 35.2 deg, which is also what the
    // photograph shows the last inch or two of a ski doing. The tail takes the same law over its own eighth.
    //
    // CAVEAT, stated so nobody takes this for a drawing: it is a photograph, not an orthographic view. The camera is
    // a little below the ski and a little off its beam, which foreshortens the LENGTH and so overstates both rises;
    // the Federal photographs show the same character but are too oblique to trace. Against this, the only numbers
    // the project had were lane/claude-j3-skis' Federal A-1500A estimate (docs/SKIS.md 3: 1.75 m overall, "upturn
    // 0.30 x 0.10 m", marked ESTIMATE, no Federal drawing found) — reach 0.171 L and rise 0.057 L, about half of
    // what the trace says in rise and a third in reach. The photograph was preferred because the estimate was a guess
    // and the photograph is a ski; ONE published dimension from a manufacturer would settle it and none exists in
    // public (Airglas's site, Aircraft Spruce's TrickAir pages and the Federal listings all publish weight and static
    // load only; AC 43.13-2B 504 gives the rigging angles and no ski dimensions).
    //
    // REJECTED: a single circular arc over the whole bottom (it rises 0.037 L at a quarter of the way back where the
    // trace says 0.018 — a ski is flatter in the middle and sharper at the ends than a circle); an arc over the tip
    // alone (tried first, 0.50 m radius turning 36.9 deg — it has no crease but it is far too short a sweep against
    // the photographs); and changing the ski's LENGTH rule, which is pre-existing and not what Ty asked about.
    /// <summary>How high the nose stands above the running surface, as a share of the ski's whole length: traced off
    /// the Aero Ski photograph Ty sent (see the note above).</summary>
    public const double NoseRiseShare = 0.127;
    /// <summary>How far back from the nose the forward sweep reaches, as a share of the length. Ahead of this the
    /// bottom is rising; behind it, to <see cref="TailSweepShare"/> of the length from the heel, it is flat.</summary>
    public const double NoseSweepShare = 0.45;
    /// <summary>The tail's own upturn: how high the heel stands, as a share of the length.</summary>
    public const double TailRiseShare = 0.032;
    /// <summary>How far forward of the heel the tail's sweep reaches, as a share of the length.</summary>
    public const double TailSweepShare = 0.12;
    /// <summary>The power the sweep follows, fitted to the traced bottom edge (2.49 at the chosen sweep length). Any
    /// value above 2 leaves the flat with no slope and no curvature, which is why the sweep has no visible start.</summary>
    public const double SweepPower = 2.5;

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
        /// <summary>The ski's ENDS relative to the pin, along the ski (+ forward): the nose and the heel. The bottom
        /// between them is flat from <see cref="NoseSweepM"/> back to <see cref="TailSweepM"/> and swept up outside
        /// that — see <see cref="BottomRiseAt"/> and the long note on <see cref="NoseRiseShare"/>. Named "Flat" from
        /// when the whole of it was; the pin itself always lies in the flat part, which is what stops a ski rocking.</summary>
        public double FlatFwdM, FlatAftM;
        /// <summary>Where the two sweeps begin, relative to the pin (+ forward): forward of <see cref="NoseSweepM"/>
        /// and aft of <see cref="TailSweepM"/> the bottom is rising. How high each end stands above the flat.</summary>
        public double NoseSweepM, TailSweepM, NoseRiseM, TailRiseM;
        /// <summary>
        /// How high the BOTTOM surface stands above the flat running surface at <paramref name="x"/> metres forward of
        /// the pin (negative = aft), by the measured power law. Zero over the flat. This is the one place the ski's
        /// bottom profile is written down: the drawing walks it for its stations and the contact
        /// (<see cref="Probe"/>) asks it where the ski touches when it is held off the flat at a pitch stop.
        /// </summary>
        public double BottomRiseAt(double x)
        {
            if (x > NoseSweepM)
            {
                double span = FlatFwdM - NoseSweepM;
                return span <= 1e-9 ? 0 : NoseRiseM * System.Math.Pow(System.Math.Min(1.0, (x - NoseSweepM) / span), SweepPower);
            }
            if (x < TailSweepM)
            {
                double span = TailSweepM - FlatAftM;
                return span <= 1e-9 ? 0 : TailRiseM * System.Math.Pow(System.Math.Min(1.0, (TailSweepM - x) / span), SweepPower);
            }
            return 0;
        }
        /// <summary>A point on the ski's BOTTOM surface, in the ski's own frame relative to the pin: X along the ski
        /// (+ forward), Z down, so the flat's bottom is at Z = <see cref="ThicknessM"/> and the swept ends are above it.</summary>
        public (double X, double Z) BottomAt(double x) => (x, ThicknessM - BottomRiseAt(x));
        /// <summary>
        /// The point of the FORWARD sweep whose own slope is <paramref name="slopeRad"/> above the flat — the point
        /// that touches level ground when the ski is held that much nose-down, and the point that meets the forward
        /// wall of a rut. Inverting the power law: rise = R (x'/span)^n, so slope = nR/span (x'/span)^(n-1).
        /// Past the nose's own slope it is the nose itself.
        /// </summary>
        public (double X, double Z) NoseBottomAtSlope(double slopeRad)
        {
            double span = FlatFwdM - NoseSweepM;
            if (span <= 1e-9 || NoseRiseM <= 1e-9 || slopeRad <= 0) return BottomAt(NoseSweepM);
            double full = SweepPower * NoseRiseM / span;                       // the slope at the nose itself
            double t = System.Math.Clamp(System.Math.Pow(slopeRad / full, 1.0 / (SweepPower - 1.0)), 0.0, 1.0);
            return BottomAt(NoseSweepM + t * span);
        }
        /// <summary>The same for the TAIL's sweep, when the ski is held nose-UP and its heel carries.</summary>
        public (double X, double Z) TailBottomAtSlope(double slopeRad)
        {
            double span = TailSweepM - FlatAftM;
            if (span <= 1e-9 || TailRiseM <= 1e-9 || slopeRad <= 0) return BottomAt(TailSweepM);
            double full = SweepPower * TailRiseM / span;
            double t = System.Math.Clamp(System.Math.Pow(slopeRad / full, 1.0 / (SweepPower - 1.0)), 0.0, 1.0);
            return BottomAt(TailSweepM - t * span);
        }
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
        // The pin a little aft of the ski's middle (8 % of its length), so most of the ski is ahead of the wheel and it
        // rides up over the snow instead of digging in. That also puts the pin 0.58 of the length back from the nose,
        // and the measured flat runs from 0.45 to 0.88 back from the nose: the pin lands in the flat with room either
        // side, which is where every ski in Ty's photographs carries its pedestal and is why a ski does not rock on
        // its own curve. Nothing was moved to make that true — it already was.
        geo.FlatFwdM = 0.5 * length + 0.08 * length;
        geo.FlatAftM = -(0.5 * length - 0.08 * length);
        geo.NoseSweepM = geo.FlatFwdM - NoseSweepShare * length;
        geo.TailSweepM = geo.FlatAftM + TailSweepShare * length;
        geo.NoseRiseM = NoseRiseShare * length;
        geo.TailRiseM = TailRiseShare * length;
        TireSpec? t = TireCatalog.Physical(g);
        double tyreWidth = t != null ? t.SectionWidthM * System.Math.Max(1, t.Count) : r * 0.6;
        geo.SlotWidthM = System.Math.Min(width * 0.85, tyreWidth + 0.03);
        SweepSlot(geo);
        // The ski is now exactly 'length' long from heel to nose. It used to be 'length' of flat plus another 0.3 of it
        // hinged on the front, i.e. 1.3 length overall, which made a J-3's main ski 2.21 m against the A-1500A's 1.75.
        // At 'length' a J-3's comes out 1.71 m, within 3 % of the real ski, with no change to the sizing rule itself.
        // The plan area therefore falls by 23 %, and with it the installed mass: a J-3's two mains and tail ski go from
        // about 31 kg to about 24 kg (53 lb), still inside the 50–80 lb a Cub ski kit is published at, so
        // ArealMassKgPerM2 is left alone rather than re-fitted to put the answer back where it was.
        double area = width * length;
        geo.MassKg = ArealMassKgPerM2 * area;
        // Frontal area: the ski's own thickness plus how high the NOSE stands, across its width. This is the one place
        // the bottom's shape reaches the forces. For a Super Cub's three skis: the 32° crease stood 0.159 L up and gave
        // 0.167 m² of flat plate; the measured sweep stands 0.127 L up and gives 0.137 m², a fifth less drag.
        geo.DragAreaM2 = FrontalCd * width * (thick + geo.NoseRiseM);
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
    /// heel, or — nose-down against the ground — a point ON THE TIP'S ARC, not the corner at the front of the flat.
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
        double excess = want - pitch;
        if (System.Math.Abs(excess) > 1e-9)
        {
            // HELD AT A STOP, so the flat is not on the ground and one end of the ski carries it.
            //
            // THIS IS WHERE THE BOTTOM'S SHAPE REACHES THE PHYSICS. With the old creased tip the ski stood on the
            // corner at the front of the flat at every angle — a line contact on a 32° crease, which is the thing
            // that digs in and the thing a sweep exists to prevent. With the measured sweep the point that touches is
            // the point whose OWN slope is 'excess' above the flat, so as the ski is pressed further off level the
            // contact ROLLS along the curve instead of staying on one corner, exactly as a ski rides up the forward
            // wall of a rut rather than catching on it. On a Super Cub main ski (1.70 m) the forward contact runs
            // from the pin's flat out to 0.986 m ahead of the pin and 0.216 m up at the nose, over 35.2° of slope.
            //
            // excess > 0: nose-DOWN against the ground (the check cable will not let it pitch any further tip-up), so
            // the forward sweep carries. excess < 0: nose-UP (the safety cable has it), so the TAIL's own sweep does —
            // and the tail is swept too, 0.032 of the length, which is what the photographs show and what keeps a
            // heel from digging when a ski is dragged backwards or settles tail-first.
            Vec3 bear;
            {
                (double X, double Z) t = excess > 0 ? geo.NoseBottomAtSlope(excess) : geo.TailBottomAtSlope(-excess);
                bear = pinW + skiFwd * t.X + skiDown * t.Z;
            }
            // Whichever of that end and the point under the pin is actually lower on this ground (NED: lower = less 'up').
            best = Vec3.Dot(bear, up) < Vec3.Dot(best, up) ? bear : best;
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

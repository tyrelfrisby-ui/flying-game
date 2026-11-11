using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>The six things an aeroplane can stand on (Ty 2026-11-11). A tundra tyre's SIZE is part of the option,
/// not a separate axis — <see cref="GearCatalog.Options"/> lists one row per size.</summary>
public enum UndercarriageKind
{
    /// <summary>The type's own wheels and tyres, with fairings where that type wears them.</summary>
    Wheels,
    /// <summary>Alaskan Bushwheels. Never fairings: a tundra tyre will not fit inside a pant.</summary>
    TundraTires,
    /// <summary>Straight skis on the MAINS — the wheel comes off the axle — and a wheel ski at the tail, always
    /// (Ty's rule; see <see cref="GearCatalog.TailIsAlwaysAWheelSki"/>).</summary>
    StraightSkis,
    /// <summary>Wheel-penetration skis everywhere: the tyre stays and protrudes through the ski.</summary>
    WheelSkis,
    StraightFloats,
    AmphibianFloats,
}

/// <summary>One row of the undercarriage menu.</summary>
public sealed class UndercarriageOption
{
    public string Id = "", Name = "", Source = "";
    public UndercarriageKind Kind;

    /// <summary>TUNDRA ONLY: the <see cref="TireCatalog"/> id this size fits. The size and its weight therefore have
    /// one home, and it is the tyre catalogue's.</summary>
    public string? TireId;

    /// <summary>The nominal diameter a player sees for a tundra size, inches: 26, 29, 31, 35 — and 35 again for the
    /// heavy-duty one, because it IS the same size (Ty: "35HD which is the same size but heavier").</summary>
    public double TundraNominalIn;

    public bool IsFloat => Kind is UndercarriageKind.StraightFloats or UndercarriageKind.AmphibianFloats;
    public bool IsSki => Kind is UndercarriageKind.StraightSkis or UndercarriageKind.WheelSkis;
    public bool IsWheeled => !IsFloat;
}

/// <summary>
/// THE UNDERCARRIAGE CATALOGUE (Ty 2026-11-11): "the configurator should have wheels when applicable (not flying
/// boats helicopters etc) that will be standard size with wheel pants when applicable, tundra tires which will not
/// have wheel pants and will come in 26 29 31 35 and 35HD which is the same size but heavier, straight skis, wheel
/// skis, straight floats, amphib floats. Available on every airplane."
///
/// IT OWNS NO NUMBERS THAT ALREADY HAVE A HOME. A tyre's size, mass and part-by-part inertia are
/// <see cref="TireCatalog"/>'s and all five tundra sizes were already in it; a ski's size, mass, centre and drag area
/// are <see cref="SkiGear"/>'s, derived from the leg's own geometry; a float's displacement and installed weight are
/// <see cref="FloatCatalog"/>'s. What is new here is the MENU, the ELIGIBILITY rule, and turning any chosen option
/// into a set of <see cref="GearComponent"/>s that the mass build-up and <see cref="GearAero"/> both read.
///
/// A FITMENT IS THE COMPLETE UNDERCARRIAGE, AND FITTING ONE REPLACES THE LAST. Floats do not join the wheels, they
/// take their place. So <see cref="ComponentsFor"/> always returns everything the aeroplane is standing on, and the
/// arithmetic is set against set (<see cref="GearMass.Exchange"/>) — never a sum of two fitments. That is
/// <see cref="FloatExchange"/>'s rule, which Ty taught this project on his own aeroplane (700 lb on wheels to 825 on
/// floats: 175 lb of float LESS 50 lb of gear), now carrying a whole inertia tensor instead of one number.
///
/// THE TAIL IS ALWAYS A WHEEL SKI. Ty: "Make all tail skis wheel skis so the tire sticks through the ski for normal
/// handling on the ground but offers floatation on snow." So "straight skis" is a statement about the MAINS, and the
/// tail keeps its tyre under every ski option. It is a rule here and not a per-type exception, which is the only way
/// it stays true for a type nobody has thought about yet.
/// </summary>
public static class GearCatalog
{
    /// <summary>Ty's rule, named so a reader can find it from either end.</summary>
    public const bool TailIsAlwaysAWheelSki = true;

    // ---- the menu ---------------------------------------------------------------------------------------------
    private static readonly UndercarriageOption[] Menu =
    {
        new() { Id = "wheels", Name = "Wheels", Kind = UndercarriageKind.Wheels,
                Source = "The type's own tyres, from TireCatalog, with fairings where that type wears them (GearCatalog.WearsFairings)." },

        // The five tundra sizes. All five were already in TireCatalog with published tyre weights; this menu only
        // names them. 35 and 35HD are the same 35x15x10 size and differ in carcass and tread -- 44 lb against 60 --
        // which is exactly the distinction Ty drew, and it is a separate row for the same reason an amphibian float
        // is a separate row from its straight sister: same envelope, different weight.
        new() { Id = "tundra-26", Name = "26 in tundra tyres", Kind = UndercarriageKind.TundraTires, TireId = "26126.R", TundraNominalIn = 26,
                Source = "Alaskan Bushwheel 26x12x6, tyre 26 lb [MCF] -- TireCatalog" },
        new() { Id = "tundra-29", Name = "29 in tundra tyres", Kind = UndercarriageKind.TundraTires, TireId = "29136.R", TundraNominalIn = 29,
                Source = "Alaskan Bushwheel 29x13x6, tyre 31 lb [MCF] -- TireCatalog" },
        new() { Id = "tundra-31", Name = "31 in tundra tyres", Kind = UndercarriageKind.TundraTires, TireId = "31136.R", TundraNominalIn = 31,
                Source = "Alaskan Bushwheel 31x13x6, tyre 35 lb [MCF] -- TireCatalog" },
        new() { Id = "tundra-35", Name = "35 in tundra tyres", Kind = UndercarriageKind.TundraTires, TireId = "351510.R1", TundraNominalIn = 35,
                Source = "Alaskan Bushwheel 35x15x10 lightweight, tyre 44 lb [MCF], 3/16 in tread -- TireCatalog. Previously excluded from the automatic bushwheel fit as 'that aeroplane's tyre'; Ty 2026-11-11 makes it a size anyone may choose." },
        new() { Id = "tundra-35hd", Name = "35 in tundra tyres, heavy duty", Kind = UndercarriageKind.TundraTires, TireId = "351510.R", TundraNominalIn = 35,
                Source = "Alaskan Bushwheel 35x15x10 heavy duty, tyre 60 lb [MCF], 1/4 in tread, on the 10 in wheel with HD brakes -- TireCatalog. Same size as the 35, 16 lb a tyre heavier." },

        new() { Id = "skis-straight", Name = "Straight skis", Kind = UndercarriageKind.StraightSkis,
                Source = "Mains: the wheel comes off the axle and the ski bolts on. Tail: a WHEEL ski, always (Ty 2026-11-11)." },
        new() { Id = "skis-wheel", Name = "Wheel skis", Kind = UndercarriageKind.WheelSkis,
                Source = "The tyre stays and protrudes through the ski. Geometry, size, mass and drag area are SkiGear's." },

        new() { Id = "floats-straight", Name = "Straight floats", Kind = UndercarriageKind.StraightFloats,
                Source = "FloatCatalog picks the size by gross weight; FloatAero owns their aerodynamics." },
        new() { Id = "floats-amphib", Name = "Amphibian floats", Kind = UndercarriageKind.AmphibianFloats,
                Source = "FloatCatalog, where an amphibian of the aeroplane's own size was built. The J-3's was not." },
    };

    public static IReadOnlyList<UndercarriageOption> Options => Menu;

    public static UndercarriageOption? Get(string? id) => id == null ? null : System.Array.Find(Menu, o => o.Id == id);

    // ---- eligibility ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE ELIGIBILITY RULE, read off the config rather than a hand list. Ty: "wheels when applicable (not flying
    /// boats helicopters etc) … Available on every airplane."
    ///
    /// An aeroplane can be put on something else exactly when it has a WHEEL to take off. <see cref="WheelSpin.IsWheel"/>
    /// already answers that, and it is the predicate the whole sim uses, so this adds no second opinion: a float keel,
    /// a nose/tail/tip skid and a hang glider's feet are not wheels. That one test excludes everything Ty named and
    /// nothing he did not:
    ///  - the FLYING BOAT (the H-4) stands on a hull and tip floats, so every leg is a float-keel or a tip-skid;
    ///  - the HANG GLIDER and the PARAGLIDER stand on feet;
    ///  - the GLIDERS keep their one wheel, and so keep every option, which is right — a 2-33 on skis is a real
    ///    aeroplane and Ty asked for "every airplane".
    /// A helicopter would be excluded by the same test if it had skids, and there is none in the fleet to check that
    /// against, so it is an argument rather than a measurement.
    /// </summary>
    public static bool HasWheel(AircraftConfig c)
    {
        if (c.Gear == null) return false;
        foreach (GearConfig g in c.Gear) if (WheelSpin.IsWheel(g)) return true;
        return false;
    }

    /// <summary>A hull on the centreline, not a pair of floats under an aeroplane — the one type that is a boat.</summary>
    public static bool IsFlyingBoat(AircraftConfig c) => c.Floats is { Count: 1 };

    /// <summary>
    /// Is this option offered on this aeroplane? Wheeled options need a wheel. Float options need a wheel to take off
    /// AND a float in the catalogue that will legally lift the aeroplane — so the 737 and the DC-3 are refused
    /// straight floats by arithmetic rather than by opinion, and the amphibian is refused wherever the real range
    /// never built one of that size, which is Ty's J-3 example.
    /// </summary>
    public static bool Eligible(AircraftConfig c, UndercarriageOption o)
    {
        if (IsFlyingBoat(c) || !HasWheel(c)) return false;
        if (!o.IsFloat) return true;
        double grossLb = (c.Mass?.MassKg ?? 0) / FloatSize.Lb;
        FloatSize? straight = FloatCatalog.Select(grossLb);
        if (straight == null) return false;
        return o.Kind == UndercarriageKind.StraightFloats || FloatCatalog.AmphibianFor(straight, grossLb) != null;
    }

    /// <summary>The menu for one aeroplane, in menu order.</summary>
    public static IReadOnlyList<UndercarriageOption> OfferedFor(AircraftConfig c)
    {
        List<UndercarriageOption> list = new();
        foreach (UndercarriageOption o in Menu) if (Eligible(c, o)) list.Add(o);
        return list;
    }

    // ---- what each type wears as standard -----------------------------------------------------------------------

    /// <summary>
    /// Does this type wear WHEEL FAIRINGS as standard? THIS IS A HAND LIST AND IT SHOULD NOT BE. Nothing in an
    /// AircraftConfig implies it: pants are a marketing and a mission decision, not a geometric one, and two
    /// aeroplanes with identical legs differ on it. Ty's words were "with wheel pants when applicable", and what is
    /// applicable is what that aeroplane really has — so this is the list, each entry naming why, and anything not
    /// on it has none.
    ///
    /// A TUNDRA TYRE NEVER HAS THEM, at any size: a 26 in bushwheel will not go inside a pant built for a 6.00-6, and
    /// a bush aeroplane would not want the mud trap if it would. That is a rule, not a list.
    /// </summary>
    public static bool WearsFairings(AircraftConfig c) => c.Id switch
    {
        "c172-like" => true,        // Cessna 172: fairings are standard equipment on the Skyhawk
        "pa28-archer-like" => true, // PA-28 Archer: standard
        "cirrus-sr22-like" => true, // SR22: composite fairings, part of the airframe's shape
        "glasair3-like" => true,    // Glasair III: a retractable, faired
        "cassutt-f1-like" => true,  // Formula One racer: pants are the point
        "geebee-r2-like" => true,   // Gee Bee R-2: large teardrop spats, a defining feature of the airframe
        "p51d-like" => false,       // retracts; the wheel goes inside the wing
        _ => false,                 // Cubs, the Beaver, the aerobatic types, the gliders: none
    };

    // ---- building the components --------------------------------------------------------------------------------

    /// <summary>Does this leg take a ski — and is it a MAIN (a straight ski may replace its wheel) or not?</summary>
    public static bool IsMainLeg(GearConfig g) => WheelSpin.IsWheel(g) && !g.IsTailwheel && !g.IsSteerable;

    /// <summary>A readable, stable id for a leg: its role and which side it is on. GearConfig carries no id of its
    /// own, and the index is kept as a suffix so two legs in the same role can never collide.</summary>
    public static string LegId(GearConfig g, int index)
    {
        string role = g.IsTailwheel ? "tail" : g.IsSteerable ? "nose" : "main";
        string side = System.Math.Abs(g.Pos[1]) < 0.05 ? "" : g.Pos[1] < 0 ? "-l" : "-r";
        return role + side + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The COMPLETE undercarriage for one option, as components. Everything the aeroplane stands on, so swapping is
    /// set against set and nothing is ever double counted.
    ///
    /// WHERE EACH NUMBER COMES FROM, and none of them from here:
    ///  - a wheel's mass, radius, width and own part-by-part polar inertia: <see cref="TireSpec"/>;
    ///  - a ski's length, width, thickness, centre, mass and frontal drag area: <see cref="SkiGear"/>, from the leg's
    ///    own geometry;
    ///  - a float's installed weight: <see cref="FloatCatalog"/>, and its AERODYNAMICS are
    ///    <see cref="Aero.FloatAero"/>'s, so its component carries a drag area of ZERO.
    /// </summary>
    public static IReadOnlyList<GearComponent> ComponentsFor(AircraftConfig c, UndercarriageOption o)
    {
        List<GearComponent> set = new();
        if (c.Gear == null) return set;

        if (o.IsFloat) { AddFloats(c, o, set); return set; }

        for (int i = 0; i < c.Gear.Count; i++)
        {
            GearConfig g = c.Gear[i];
            if (!WheelSpin.IsWheel(g)) continue;
            string leg = LegId(g, i);
            bool main = IsMainLeg(g);
            // THE TAIL-SKI RULE: a straight-ski aeroplane is straight on the mains only; every other leg keeps its
            // wheel under the ski. Encoded once, here, so it holds for a type nobody has thought about yet.
            bool skiHere = o.IsSki;
            bool keepsWheel = !skiHere || o.Kind == UndercarriageKind.WheelSkis || !main;

            if (keepsWheel) AddWheel(c, g, leg, o, set);
            if (skiHere) AddSki(c, g, leg, o, set);
        }
        return set;
    }

    private static void AddWheel(AircraftConfig c, GearConfig g, string leg, UndercarriageOption o, List<GearComponent> set)
    {
        // The tyre this option puts on this leg. Tundra goes on the MAINS only; a tailwheel keeps its own tyre,
        // which is what a real bushwheel installation does (the tailwheel gets its own small tundra tyre at best,
        // and TireCatalog has no such size, so it is left alone rather than invented).
        TireSpec? stock = TireCatalog.Of(g);
        TireSpec? fitted = o.Kind == UndercarriageKind.TundraTires && IsMainLeg(g) && o.TireId != null
                         ? TireCatalog.Get(o.TireId) ?? stock : stock;
        if (fitted == null) return;

        // THE CENTRE OF MASS IS THE AXLE, AND IT MOVES WITH THE TYRE. GearConfig.Pos is the CONTACT POINT; the axle
        // is one tyre radius above it. Fitting a 35 in tundra tyre on the same leg raises the aeroplane and puts the
        // wheel's mass 11 cm further from the airframe than the stock 8.00-6 did -- which is the whole reason this
        // is derived from the fitted tyre and not stored.
        double r = fitted.RadiusM;
        Vec3 contact = g.PosVec();
        Vec3 axle = new(contact.X, contact.Y, contact.Z - r);

        bool braked = g.Brake;
        double massKg = fitted.RotatingMassKg(braked);
        // Frontal area of the tyre itself: diameter x section width, times the number of tyres on the leg.
        double frontal = fitted.OdM * fitted.SectionWidthM * System.Math.Max(1, fitted.Count);
        // A fairing goes on only where the type wears them, the option is the stock wheels, and the tyre is small
        // enough — which on the types that print a limit is a CERTIFICATION one, not just a question of clearance.
        bool pants = o.Kind == UndercarriageKind.Wheels && WearsFairings(c) && !fitted.Tundra
                  && fitted.OdIn <= MaxFairedTyreOdIn;
        double cd = ExposedWheelCd(fitted) * (pants ? FairingCdFactor : 1.0);
        double install = InstallationFactor(g);

        set.Add(new GearComponent
        {
            Id = leg + "-wheel", Name = fitted.Name, AttachTo = "fuselage",
            MassKg = massKg,
            MassTier = fitted.Legacy ? MassTier.Estimated : MassTier.Known,
            MassSource = fitted.Source,
            CmBody = axle, AeroRefPointBody = axle,
            Shape = InertiaShape.Disc, DimX = r, DimY = fitted.SectionWidthM,
            DiscPolarKgM2 = fitted.InertiaKgM2(braked),
            Spins = true,
            AeroOwnedBy = AeroOwner.Components,
            DragAreaM2 = frontal * cd * install,
            InterferenceFactor = install,
            AeroSource = (pants ? "faired" : "exposed") + " wheel, Cd " + cd.ToString("0.000") +
                         " on width x diameter (Hoerner p. 13-14 convention; 0.155 (b/d)^-0.45 fitted to NACA TR 485 " +
                         "Table I" + (pants ? ", x 0.57 for the fairing, TR 485 Table II" : "") +
                         "), x " + install.ToString("0.00") + " for struts, fittings and interference (TR 485 Table VII-A)",
        });

        if (pants) AddFairing(leg, g.IsSteerable && !g.IsTailwheel, fitted, axle, set);
    }

    private static void AddFairing(string leg, bool nose, TireSpec t, Vec3 axle, List<GearComponent> set)
    {
        // A FAIRING'S ENVELOPE, from the tyre it has to cover: a little longer than the tyre's diameter, a little
        // taller than its radius above the axle, and wider than its section. Those proportions are what a fairing
        // IS -- it is drawn around the wheel -- and they give the box the inertia is taken from.
        double lx = 1.6 * t.OdM, lz = 1.15 * t.OdM, ly = 1.6 * t.SectionWidthM;
        set.Add(new GearComponent
        {
            Id = leg + "-fairing", Name = (nose ? "nose" : "main") + " wheel fairing", AttachTo = "fuselage",
            MassKg = nose ? NoseFairingMassKg : MainFairingMassKg,
            MassTier = MassTier.Known, MassSource = FairingMassSource,
            CmBody = axle, AeroRefPointBody = axle,
            Shape = InertiaShape.Box, DimX = lx, DimY = ly, DimZ = lz,
            // The fairing's own drag is NOT added: it REPLACES the exposed wheel's, and the wheel component above
            // already carries the faired coefficient. Counting a pant as extra drag is the double-count this whole
            // ownership scheme exists to stop.
            AeroOwnedBy = AeroOwner.None,
            AeroSource = "none of its own: the wheel it covers carries the faired coefficient instead",
        });
    }

    private static void AddSki(AircraftConfig c, GearConfig g, string leg, UndercarriageOption o, List<GearComponent> set)
    {
        SkiGear.Geometry geo = SkiGear.For(c, g);
        // Where the ski's mass really is: under its pin, with the links down. SkiGear computes that point; this does
        // not re-derive it.
        Vec3 at = geo.PinAt(SkiGear.LoweredLinkRad);
        bool straight = o.Kind == UndercarriageKind.StraightSkis && IsMainLeg(g);
        // A STRAIGHT SKI HAS NO SLOT, so it is lighter than a wheel ski of the same plan: no slot reinforcement, no
        // links, no selector. The fraction is an ESTIMATE and is flagged as one.
        double massKg = geo.MassKg * (straight ? StraightSkiMassFraction : 1.0);
        set.Add(new GearComponent
        {
            // The id never ends in "-wheel": a wheel ski is a ski, and an id that matched both would quietly be
            // counted as a wheel by anything filtering on the suffix.
            Id = leg + (straight ? "-ski-straight" : "-ski-penetration"),
            Name = straight ? "straight ski" : "wheel ski", AttachTo = "fuselage",
            MassKg = massKg, MassTier = MassTier.Estimated,
            MassSource = "SkiGear.ArealMassKgPerM2 (22 kg/m^2, ESTIMATE fitted to published Cub and Beaver ski kits) " +
                         "on the ski's own plan area" +
                         (straight ? "; x " + StraightSkiMassFraction.ToString("0.00") +
                                     " for a straight ski, MEASURED from FAA Aircraft Specification pairs " +
                                     "(32/76 lb at the 1500 rating, A-768 items 204(a)/(j); 53/102 lb at 2500, A-799 items 208(a)/(g))"
                                   : ""),
            CmBody = at, AeroRefPointBody = at,
            Shape = InertiaShape.Slab, DimX = geo.LengthM, DimY = geo.WidthM, DimZ = geo.ThicknessM,
            IncidenceRad = 0,
            AeroOwnedBy = AeroOwner.Components,
            // A SKI'S DRAG IS MEASURED, ON ITS PLAN AREA. Hoerner, Fluid-Dynamic Drag p. 13-16, Fig 40 (after
            // Koehler, Luftwissen 1937) gives a real aeroplane ski at aspect ratio 0.19: Cd 0.03 minimum on PLAN
            // area, +0.07 for the upturned heel, -0.02 for a fairing over the deck. Ours has the upturned heel and
            // no deck fairing, so 0.10.
            //
            // SkiGear's own DragAreaM2 is an independent estimate from the other direction -- a blunt coefficient of
            // 0.8 on the ski's frontal area (thickness plus nose rise) -- and on a Super Cub the two agree to within
            // 25 %, 0.56 ft^2 against 0.70. That is a genuine cross-check and the measured one is used.
            //
            // WHAT NEITHER OF THEM EVER HAD IS A POINT TO ACT AT: the sim applies SkiGear.TotalDragAreaM2 at the CG
            // with no moment at all. Here it acts at the ski's own pin, and the plan area carries lift as well.
            DragAreaM2 = SkiPlanCd * geo.LengthM * geo.WidthM,
            PlanAreaM2 = geo.LengthM * geo.WidthM,
            AspectRatio = geo.WidthM / System.Math.Max(1e-6, geo.LengthM),
            AeroSource = "Cd " + SkiPlanCd.ToString("0.00") + " on PLAN area (Hoerner Fluid-Dynamic Drag p. 13-16 " +
                         "Fig 40: 0.03 clean + 0.07 for the upturned heel, at aspect ratio 0.19), applied at the " +
                         "ski's pin; lift from the low-aspect-ratio plate law (FloatAero.Coefficients with " +
                         "PlateVortexK 1.8, Hoerner Fluid-Dynamic Lift ch. VII pp. 7-17/7-19)",
        });
    }

    private static void AddFloats(AircraftConfig c, UndercarriageOption o, List<GearComponent> set)
    {
        bool amphib = o.Kind == UndercarriageKind.AmphibianFloats;
        double grossLb = (c.Mass?.MassKg ?? 0) / FloatSize.Lb;
        FloatSize? straight = FloatCatalog.Select(grossLb);
        FloatSize? size = amphib && straight != null ? FloatCatalog.AmphibianFor(straight, grossLb) : straight;
        if (size == null) return;

        // Geometry: the type's own float block where it has one (the J-3's is drawn), else the size's proportions
        // are not known here and the installed float block has to come from the config. A type with no floats block
        // gets the mass at the keel under the wing, which is honest but coarse, and is flagged on the component.
        FloatsConfig? f = c.Floats;
        double lengthM = f?.LengthM ?? 0, beamM = f?.BeamM ?? 0, depthM = f?.DepthM ?? 0;
        bool drawn = lengthM > 0.1 && beamM > 0.01;
        Vec3 at = drawn ? new Vec3(f!.BowX - 0.5 * f.LengthM, 0, f.KeelZ) : new Vec3(0, 0, 1.2);

        set.Add(new GearComponent
        {
            Id = "floats", Name = size.Name + (amphib ? " (amphibian)" : ""), AttachTo = "fuselage",
            MassKg = size.InstalledKg(amphib),
            // ESTIMATED EVEN AT THE ANCHORED SIZES. FloatCatalog's weights are all derived from one ratio; at the
            // 1320 and the 2000 there is a published figure to CHECK it against, which is not the same as the
            // catalogue carrying the published figure. The Source names it either way.
            MassTier = MassTier.Estimated,
            MassSource = size.Source,
            CmBody = at, AeroRefPointBody = at,
            Shape = drawn ? InertiaShape.Box : InertiaShape.Point,
            DimX = lengthM, DimY = beamM, DimZ = depthM,
            // FLOATS' AERODYNAMICS ARE FloatAero'S, FULL STOP. It already computes lift, drag, the sideslip side
            // force and the moments at the floats' own position. A drag area here would be counted twice and
            // nothing would fail loudly, which is the exact failure AeroOwner exists to prevent.
            AeroOwnedBy = AeroOwner.FloatAero, DragAreaM2 = 0,
            AeroSource = "FloatAero.Compute: Polhamus slender-wing + vortex lift in both planes, at the floats' own centroid",
        });
    }

    // ---- the coefficients and the figures this catalogue does own -----------------------------------------------

    /// <summary>
    /// DRAG COEFFICIENT OF AN EXPOSED AIRCRAFT WHEEL, on the reference area Hoerner insists on and this catalogue
    /// uses everywhere: SECTION WIDTH × OUTSIDE DIAMETER (Fluid-Dynamic Drag 1965, p. 13-14, "The drag coefficient
    /// of airplane wheels and landing gears may conveniently be based upon the area S_D, equal to tire width times
    /// outer diameter").
    ///
    /// IT IS NOT A CONSTANT — it depends on how FAT the tyre is, and strongly. Fitted to the five full-size wheels
    /// of NACA TR 485 (Herrnstein and Biermann 1934, 20-ft tunnel, 80 mph, Table I), each converted to a coefficient
    /// on its own width × diameter:
    ///
    ///   30x5 high-pressure   b/d 0.168 -> 0.349      25x11-4 extra-low-pressure  b/d 0.459 -> 0.227
    ///   32x6 high-pressure   b/d 0.198 -> 0.308      8.50-10 low-pressure        b/d 0.336 -> 0.249
    ///
    ///   Cd = 0.155 (b/d)^-0.45,  which reproduces all four inside 3 %.
    ///
    /// Narrow, tall tyres are worse per unit frontal area; fat ones are better. THE CONSEQUENCE FOR A TUNDRA TYRE IS
    /// THE WHOLE POINT: a 31 in bushwheel's coefficient is 0.228, almost exactly a stock 8.50-6's 0.233. A tundra
    /// tyre is not draggier per square foot — it is 2.2 times bigger frontally, and that is all. Model it as area,
    /// never as a special coefficient.
    /// </summary>
    public static double ExposedWheelCd(TireSpec t) =>
        0.155 * System.Math.Pow(System.Math.Max(0.05, t.WidthIn / System.Math.Max(1e-6, t.OdIn)), -0.45);

    /// <summary>
    /// What a FAIRING does to the wheel's own coefficient: × 0.57. MEASURED — NACA TR 485 Table II put six fairings
    /// on an isolated 8.50-10 wheel and the ordinary good one (their "fairing A") took its drag from 6.1 lb to
    /// 3.5 lb, −42.7 %, i.e. Cd 0.249 → 0.143. Hoerner quotes that same 0.14 on p. 13-14 for "a typical shape", so
    /// the two sources close exactly.
    ///
    /// THE OFTEN-REPEATED "PANTS HALVE IT" IS RIGHT ABOUT THE WHEEL AND WRONG ABOUT THE AEROPLANE. The same report
    /// measured complete installed gears before and after and got −23 %, −25.5 % and −33 %, because the leg and its
    /// fittings are untouched. This factor is applied to the WHEEL only, and the installation factor below carries
    /// the rest, so the aeroplane-level answer comes out in that band on its own rather than being asserted.
    ///
    /// A fully encasing fairing reaches × 0.28 (−72 %), and a badly shaped one with an open side cut-out MAKES IT
    /// WORSE (+26 %) — neither is modelled, because no type in this fleet has either.
    /// </summary>
    public const double FairingCdFactor = 0.57;

    /// <summary>
    /// EVERYTHING ON THE LEG THAT IS NOT THE WHEEL, as a multiple of the bare wheels' own drag: the struts, the
    /// axles, the brake lines, the fittings, and the interference between all of them — which NACA TR 485 found to
    /// be 44 % of a conventional gear's whole drag, more than the wheels themselves.
    ///
    /// MEASURED, from TR 485 Table VII-A (3000 lb aeroplane, 100 mph), as complete-gear drag over bare-wheel drag:
    ///
    ///   tripod / vee gear with exposed fittings   45.6 / 19.4 = 2.35
    ///   horizontal-axle gear                      38.3 / 19.4 = 1.97
    ///   single cantilever strut                   22.8 / 15.6 = 1.46
    ///
    /// The gear type is read off the config rather than guessed: a Cub's bungee vee gear with its exposed fittings
    /// is the 2.35 case, a Cessna's or a Pitts' cantilever spring leg is the 1.46 case, and an oleo strut gear sits
    /// between. THE LEG ITSELF IS NOT A SEPARATE COMPONENT — the config carries a contact point and a spring rate,
    /// not a strut length or a section — so its drag rides on the wheel's here, and the catalogue says so rather
    /// than pretending the leg is free.
    ///
    /// IT IS CHECKED, NOT ASSUMED: TR 485 puts a 3000 lb taildragger's complete fixed gear at f = 1.0-1.8 ft² and
    /// its bare wheels at 0.76 ft², and 0.76 × 2.35 = 1.79 lands at the top of that band. A test pins it.
    /// </summary>
    public static double InstallationFactor(GearConfig g) => g.GearType switch
    {
        "spring-steel" or "spring-composite" => 1.46,   // a cantilever bow or spring leg: nothing hanging in the wind
        "oleo" => 1.97,                                 // a strut gear with an axle across it
        _ => 2.35,                                      // bungee vee gear and anything unstated: exposed fittings
    };

    /// <summary>
    /// ONE WHEEL FAIRING, kg — PUBLISHED, and from two eras that disagree by a factor of nearly two, so which one a
    /// type gets matters.
    ///
    ///  - A PRE-WAR FABRIC TAILDRAGGER on a 6.00-6 or smaller: 6 lb for the PAIR of mains, i.e. 3 lb each. Ten
    ///    independent FAA Aircraft Specifications print exactly 6 lb — A-691 item 307 and A-698 item 303 (the J-3),
    ///    1A2 item 211 (the PA-18), A-768 item 207 and 5A2 item 211 (the Cessna 120/140/140A), A-799 item 210 (the
    ///    170), A-692, A-703, A-725, A-696, A-767. The one outlier is a Luscombe at 10 lb.
    ///  - A MODERN LIGHT SINGLE: 16-21 lb for the FULL SET, from the POH equipment lists — Cessna 172S 16.5 lb
    ///    (nose 3.5, mains 10.1, brake fairings 1.1), 182T 16.4, T206H 19.4, Archer III 20.8 (nose 3.8, 8.5 each
    ///    main), PA-22 Tri-Pacer 20.5 (nose 5.5, mains 15.0). So about 5 lb a main and 3.5 a nose.
    ///
    /// Every type in this fleet that wears fairings is in the second group, so those are the figures used. The
    /// taildragger figure is written down because the moment somebody gives a Cub pants it is the right one.
    /// </summary>
    public const double MainFairingMassKg = 5.0 * FloatSize.Lb, NoseFairingMassKg = 3.5 * FloatSize.Lb;

    public const string FairingMassSource =
        "PUBLISHED: 5 lb per main fairing and 3.5 lb per nose fairing, from POH equipment lists (Cessna 172S " +
        "32-03-A, full set 16.5 lb: nose 3.5, mains 10.1 the pair; 182T 16.4; T206H 19.4; Piper Archer III item 31, " +
        "20.8 lb: nose 3.80, 8.50 each main; PA-22 FAA spec 1A6 item 210, nose 5.5 + mains 15.0). A pre-war fabric " +
        "taildragger's are LIGHTER -- 6 lb the pair, i.e. 3 lb each, in ten FAA Aircraft Specifications -- and no " +
        "type in this fleet that wears fairings is one.";

    /// <summary>
    /// The tyre outside diameter above which no fairing is fitted, inches. NOT just geometry — it is a CERTIFICATION
    /// limit on the types that print one: A-768 Note 4 says the Cessna 120/140's wheel streamlines are "not eligible"
    /// with 7.00-6 or 8.00-6 tyres, and A-799 item 210 and 5A2 item 211 say the same for the 170 and 140A. A 6.00-6
    /// is 17.5 in and a 7.00-6 is about 19, so the line falls between them. Oversize fairings do exist up to the
    /// 8.00-6 class (a modern Cessna sells one, about 1.3x the standard set's weight), and above that nothing does:
    /// no maker anywhere lists a fairing for a 26 in bushwheel, whose TYRE alone weighs 26 lb. So the rule this
    /// catalogue applies — never a fairing on a tundra tyre, at any size — is conservative at the 8.00-6 end and
    /// right everywhere it is actually used.
    /// </summary>
    public const double MaxFairedTyreOdIn = 18.0;

    /// <summary>
    /// A SKI'S DRAG COEFFICIENT ON ITS PLAN AREA. MEASURED: Hoerner, Fluid-Dynamic Drag p. 13-16, Fig 40 (after
    /// Koehler, Luftwissen 1937), a real aeroplane ski at aspect ratio 0.19 — Cd 0.03 at minimum, +0.07 for an
    /// upturned heel, −0.02 for a streamline fairing over the deck. Ours has the heel and no deck fairing, so 0.10.
    /// The upturned heel more than TRIPLES the clean ski's drag, which is worth knowing before anyone draws one.
    /// </summary>
    public const double SkiPlanCd = 0.03 + 0.07;

    /// <summary>
    /// A STRAIGHT SKI'S MASS AS A FRACTION OF THE WHEEL SKI'S of the same rating. MEASURED, from two pairs of FAA
    /// Aircraft Specification equipment-list weights for the SAME maker's straight and hydraulic wheel skis:
    ///
    ///   rating 1500 lb: straight 32 lb/pair, wheel ski 76 lb/pair (A-768 Rev 35 items 204(a) and 204(j))  = 0.42
    ///   rating 2500 lb: straight 53 lb/pair, wheel ski 102 lb/pair (A-799 Rev 55 items 208(a) and 208(g))  = 0.52
    ///
    /// 0.47 is the middle. The gap is the slot and its reinforcement, the links, the selector, the actuator and the
    /// wheel and brake that stay on the aeroplane — which is exactly what <see cref="SkiGear"/> models and a straight
    /// ski does not have. It replaced a flat 0.75 guess made before these figures were in hand.
    ///
    /// NOTE WHICH WHEEL SKI. <see cref="SkiGear"/> models the RAISE-AND-LOWER kind (it has a selector and a transit
    /// time), so the fraction is taken against the hydraulic AWB figures and not against the ground-convertible AWA
    /// ones, which are lighter (78 lb/pair at the 2500 rating, so straight/convertible would be 0.68). Using the
    /// wrong one of those two would make a straight ski 45 % heavier than it is.
    /// </summary>
    public const double StraightSkiMassFraction = 0.47;

    /// <summary>
    /// What a STRAIGHT ski pair weighs as a fraction of the aeroplane's gross — the independent check on the line
    /// above, and the one to use if the geometry ever stops being trustworthy. Eight equipment-list points from two
    /// makers over 1450–2400 lb give 2.1–3.2 % of gross, the two cleanest (one maker, two ratings) 2.1 % of the
    /// SKI's rating exactly. Not used to compute anything — <see cref="SkiGear"/>'s geometry does that — and pinned
    /// in a test so the geometry cannot wander away from the certificates without somebody hearing about it.
    /// </summary>
    public const double StraightSkiPairFractionOfGross = 0.024;
    public const double StraightSkiPairFractionLo = 0.021, StraightSkiPairFractionHi = 0.032;

    /// <summary>
    /// A TAIL SKI IS TINY AND THE CERTIFICATES SAY SO: 3 lb for a 1500 lb-class straight tail ski (A-768 Rev 35 item
    /// 205), 6 lb for a 2500 lb-class one (A-799 Rev 55 item 211) — against the 5–8 lb tailwheel it sits with. So
    /// Ty's rule that every tail ski is a wheel ski costs almost nothing: the certificated retractable tail ski is
    /// quoted as +5.5 lb ON TOP of the tailwheel (1A2 item 208(f)), because the tailwheel stays, and that is the
    /// configuration his rule describes. The band here is what a tail ski of any kind in this fleet must land in.
    /// </summary>
    public const double TailSkiPairLb = 3.0, TailSkiPairHiLb = 12.0;
}

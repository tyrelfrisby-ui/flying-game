using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Landing-gear ground reactions (NASA TM-1999-209143 / shimmy-analysis style, at low order): each
/// wheel is an oleo strut (spring + oil damping, vertical) plus a tire that makes longitudinal
/// (rolling/braking) and LATERAL (cornering) force. Cornering force is proportional to the tire's
/// slip angle up to the friction-circle limit μ·N — so a skidding tire saturates. Forces are applied
/// at each wheel's contact point, so the emergent behaviours the owner asked for FALL OUT of the
/// rigid-body dynamics with no special cases:
///  - a side load rolls the aircraft, compressing the outer strut more → weight shifts to that wheel;
///  - a TAILDRAGGER (CG behind the mains) is directionally UNSTABLE — a yaw disturbance puts a side
///    load at the CG behind the main pivot, which grows the yaw (ground-loop tendency), controllable
///    with rudder + steerable tailwheel; a tricycle (CG ahead of the mains) is self-stable.
/// Ground is the plane world z = groundZ (z is DOWN). Runway at sea level → groundZ = 0.
/// </summary>
public static class LandingGear
{
    /// <summary>
    /// The tyre's free radius — the SAME rule the airframe visual uses, so the physics and the picture agree about
    /// how big the wheel is: the catalog tyre's (TireCatalog, by the leg's id; the bushwheel option fits its tundra
    /// tyre there, TireFit), else a skid's or a code-built leg's stated radius, else from the fuselage length (a
    /// tailwheel a bit under half the size of a main).
    /// </summary>
    public static double TireRadiusOf(AircraftConfig c, GearConfig g)
    {
        TireSpec? t = TireCatalog.Of(g);
        if (t != null) return t.RadiusM;
        if (g.TireRadiusM > 0) return g.TireRadiusM;
        double length = c.Fuselage?.Crossflow != null && c.Fuselage.Crossflow.LengthM > 0
                        ? c.Fuselage.Crossflow.LengthM : 7.0;
        double main = System.Math.Clamp(length * 0.045, 0.15, 0.65);
        return g.IsTailwheel ? main * 0.45 : main;
    }

    /// <summary>True if any main (non-tail) wheel is at or below the local ground.</summary>

    public static bool AnyMainWheelOnGround(AircraftConfig config, RigidBodyState s)
    {
        Vec3 cg = config.Mass.CgVec();
        foreach (GearConfig g in config.Gear)
        {
            if (g.IsTailwheel) continue;
            Vec3 w = s.Position + s.Attitude.Rotate(g.PosVec() - cg);
            double? waterH = FloatHydro.WaterSurfaceAt(w.X, w.Y, -w.Z);
            if (waterH.HasValue && waterH.Value >= WorldTerrain.GroundHeightAt(w.X, w.Y) - 0.01) continue;
            if (w.Z >= -WorldTerrain.WheelGroundHeightAt(w.X, w.Y, -w.Z) - 0.02) return true;
        }
        return false;
    }

    /// <summary>
    /// The aircraft at rest on ALL its wheels (owner 2026-09-09: a taildragger spawned level dropped its tail from a
    /// metre up and broke the tail boom): the three-point pitch that puts the mains and the tail/nose wheel on the
    /// ground together, CG height so the lowest wheel sits 2 cm into the surface (struts settle), heading given.
    /// </summary>
    /// <remarks>With wheel-skis fitted the aeroplane stands on whatever is lowest on each leg at
    /// <paramref name="skiLowered01"/> (NaN = where the selector starts over this ground, <see cref="SkiSelector.StartFor"/>):
    /// on its skis when they are down, lying flat under each pin.</remarks>
    public static RigidBodyState RestingState(AircraftConfig c, double x, double y, double groundElevM, double headingRad = 0.0,
                                              double skiLowered01 = double.NaN)
    {
        Vec3 cg = c.Mass.CgVec();
        var mains = c.Gear.FindAll(g => !g.IsTailwheel && g.GearType != "nose-skid" && g.GearType != "float-keel" && System.Math.Abs(g.Pos[1]) < 2.5 && g.Pos[2] > 0);
        double pitch = 0.0;
        if (mains.Count > 0)
        {
            // Main wheel reference: the lowest-hanging main. Third wheel: the tailwheel (aft) or the nosewheel (ahead).
            GearConfig m = mains[0]; foreach (GearConfig g in mains) if (g.Pos[2] > m.Pos[2]) m = g;
            GearConfig? third = null;
            foreach (GearConfig g in c.Gear)
            {
                if (g == m || g.Pos[2] <= 0 || g.GearType == "nose-skid" || g.GearType == "float-keel") continue;
                if (g.IsTailwheel || (System.Math.Abs(g.Pos[1]) < 0.3 && System.Math.Abs(g.Pos[0] - m.Pos[0]) > 1.0))
                    if (third == null || System.Math.Abs(g.Pos[0] - m.Pos[0]) > System.Math.Abs(third.Pos[0] - m.Pos[0])) third = g;
            }
            if (third != null && System.Math.Abs(m.Pos[0] - third.Pos[0]) > 0.5)
                pitch = System.Math.Atan((m.Pos[2] - third.Pos[2]) / (m.Pos[0] - third.Pos[0]));   // nose up when the tailwheel hangs higher
        }
        var qPitch = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        var qYaw = new Quat(0, 0, System.Math.Sin(headingRad / 2), System.Math.Cos(headingRad / 2));
        Quat att = Quat.Multiply(qYaw, qPitch);
        double lowest = double.MinValue;   // NED z (down) of the lowest wheel relative to the CG
        if (c.Skis && double.IsNaN(skiLowered01))
        {
            WorldTerrain.Surface under = WorldTerrain.SurfaceOverride ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(x, y) : WorldTerrain.Surface.Paved);
            skiLowered01 = SkiSelector.StartFor(under) == SkiSelector.Position.Skis ? 1.0 : 0.0;
        }
        foreach (GearConfig g in c.Gear)
        {
            if (g.GearType == "float-keel" || g.Pos[2] <= 0) continue;
            lowest = System.Math.Max(lowest, att.Rotate(g.PosVec() - cg).Z);
            if (SkiGear.Fitted(c, g))
            {
                // The ski lies flat on level ground: its bottom is its thickness straight below the pin.
                SkiGear.Geometry geo = SkiGear.For(c, g);
                lowest = System.Math.Max(lowest, att.Rotate(geo.PinAt(SkiGear.LinkRad(skiLowered01)) - cg).Z + geo.ThicknessM);
            }
        }
        if (lowest == double.MinValue) lowest = 0.0;
        return new RigidBodyState(new Vec3(x, y, -(groundElevM + lowest - 0.02)), att, Vec3.Zero, Vec3.Zero);
    }

    /// <summary>First-contact sink rate (m/s) that tears a leg off when the config gives none: FAR 23 designs the
    /// gear for 10 ft/s (3 m/s); ~4 m/s is past its ultimate. A tailwheel takes the tail dropping through onto it at
    /// the end of every wheel landing (5–6 m/s at the wheel from the pitch rate), so it is only lost to a real slam.</summary>
    public const double DefaultBreakSinkMs = 4.0, DefaultTailwheelBreakSinkMs = 8.0;
    /// <summary>
    /// TYRE CORNERING STIFFNESS per newton of wheel load (N per rad per N). 7.2, which is 0.125 of the wheel's load
    /// per DEGREE of slip — the middle of the 0.10–0.15 per degree that published cornering power for aircraft tyres
    /// sits in.
    ///
    /// It was 12.0, described only as "~12 for an aircraft tyre" with nothing behind it. 12 per rad is 0.21 per
    /// degree, which is the AUTOMOTIVE end: a car tyre on a road, not a high-pressure aircraft tyre with a small stiff
    /// contact patch. Ty, 2026-11-10, shown this beside two other candidates for why a Pitts ground-loops 180 degrees
    /// off every landing, picked this one — and his own words from the day before name the symptom exactly: "all
    /// airplanes are overly difficult to steer on asphalt so I think the friction is too high". The friction CAP (mu
    /// 0.8) was never the problem; this is. A tyre 40-70 % too stiff in slip turns a small yaw angle into a large side
    /// force, and that is a swerve that feeds itself.
    ///
    /// It is also most of why 2 degrees of toe-out cost so much: at 12 per rad a toed wheel spends about 0.42 of its
    /// own load on a side force that has to be cancelled by the other wheel, and at 7.2 it is 0.25.
    ///
    /// NOT APPLIED TO THE TAILWHEEL, deliberately. <see cref="TailwheelCorneringPerLoad"/> is 4.0 and the comment
    /// below calls it "a third of a main's", which this breaks — but Ty tuned that number by feel over three passes
    /// and the relationship was a description, not the reason. Changing both at once would make a flight report
    /// unattributable. Flagged, not silently followed.
    /// </summary>
    public const double CorneringPerLoad = 7.2;
    /// <summary>A tailwheel's small contact patch: about half the cornering per newton, and a lower friction cap.</summary>
    // Owner 2026-09-12 (second pass): the tailwheel must SKID in a swerve rather than hold the tail — a small hard tyre:
    // a third of a main's cornering per newton and 0.6 of its friction; and weaker steering springs still.
    // Third pass (owner: "a tailwheel would never reverse a swerve"): the castor answers within a few hundredths of a
    // second (damping 0.4), so the skidding-tail moment that was cancelling the mains' swerve moment for the first 0.2 s
    // is gone; the small hard tyre's friction is half a main's.
    public const double TailwheelCorneringPerLoad = 4.0, TailwheelMuScale = 0.8;
    // Steering unit (owner 2026-09-13): springs to the rudder (no slack) at half the earlier
    // stiffness; swivelling past 45° UNLOCKS the steering (free castor — 45° is beyond rudder travel, so it takes rudder
    // plus side load or brake stretching the springs); it re-engages only once the wheel is back inside rudder travel
    // AND the rudder has been brought to match it. Trail 7.5 cm ≈ a Scott 3200's 3 in (positive castor).
    public const double DefaultSteerSpringNmPerRad = 4.0, DefaultSteerDampNms = 0.5, DefaultCastorTrailM = 0.075;
    public const double DefaultBreakoutRad = 0.785;      // 45°: steering unlocks
    public const double SteerDeadZoneRad = 0.0;          // owner 2026-09-13: no slack (the ±1° dead zone made it too hard)
    public const double RelockToleranceRad = 0.035;      // rudder within 2° of the wheel re-engages the steering

    // ---- THE LEG: WHAT KIND OF SPRING IT IS, AND WHERE IT HINGES (Ty 2026-11-10) -----------------------------------
    // "They go up on one wheel and strike wingtips rather than using their suspension. The pitts has bungee like the
    //  cub no rebound but plenty of travel. The extra has springy fiberglass that flexes nicely."
    // "The cub has bungee too. Pivot at the fuselage attachment and so does the pitts."
    // "The cub bungees are in the black leather bags on the gear legs the pitts bungees are in the belly and the
    //  cabana struts attach to them so cabana pulls on bungee inside."
    //
    // Two separate things come out of that, and they are kept separate here:
    //  - WHAT SPRINGS IT (GearConfig.GearType): a rubber shock cord, a leaf or bow that bends, or air over oil. It
    //    sets how far the leg travels before it is on its stop, and what it does past it. The rebound is UNDAMPED on
    //    every leg here and always was (the damper only ever sees max(0, closing rate)) — which is exactly a bungee,
    //    and is why these aeroplanes bounce. Nothing was needed for "no rebound"; it is stated so nobody "fixes" it.
    //  - WHAT CARRIES IT (GearConfig.LegPivot): the linkage. A Cub's and a Pitts' legs are rigid and hinge on the
    //    fuselage, so the wheel runs on an ARC — up AND outboard — and the track widens as the gear compresses. A
    //    composite bow bends rather than hinges and its tip moves the same way to first order. A telescopic strut
    //    does not: no pivot authored, no splay, exactly as before.
    //
    // WHAT IS NOT MODELLED, and it matters (GearGeometryTests.TheJackingThatComesWithTheSameLinkageIsNotModelled): a swing arm also
    // turns a SIDE force at the contact into leg EXTENSION — jacking. The leg's own balance is
    //     N = k·stroke − Y_outboard·(drop/run),
    // so a skid's inboard force pushes the body UP. These legs' drop/run is 0.63 (the Great Lakes' half-axle, hinged
    // on the centreline) to 1.82 (the Extra's bow), which puts their roll centres at 55 % to 114 % of the CG height —
    // so at the tyre's μ = 0.8 the factor μ·(drop/run) reaches 1 on the Cub, the Pitts and the Extra, and a rigid-link
    // treatment is singular there: the load would run away inside a step. A faithful load path
    // needs what actually reacts it — the shock strut's own geometry, the leg's lateral bending and the tyre's
    // lateral give — which is a bigger piece of work and Ty's call. The splay below is the kinematics only, so this
    // model is optimistic about tipping by roughly as much as it is pessimistic about jacking.
    public enum LegKind { Rigid, Bungee, SpringSteel, SpringAluminum, SpringComposite, Oleo }

    /// <summary>What kind of spring this leg is. Anything that is not a sprung wheel (a keel, a skid, feet) or is
    /// unauthored is <see cref="LegKind.Rigid"/>: a plain linear spring with no stop, which is the whole fleet's
    /// behaviour before 2026-11-10.</summary>
    public static LegKind KindOf(GearConfig g) => g.GearType switch
    {
        "bungee" => LegKind.Bungee,
        "spring-steel" => LegKind.SpringSteel,
        "spring-aluminum" or "spring-aluminium" => LegKind.SpringAluminum,
        "spring-composite" or "spring-fibreglass" => LegKind.SpringComposite,
        "oleo" => LegKind.Oleo,
        _ => LegKind.Rigid,
    };

    /// <summary>
    /// How far the leg compresses at the wheel before it is on its stop (m). A leg may state its own
    /// (GearConfig.LegTravelM); otherwise it is the kind's:
    ///  - BUNGEE 0.18 m. A J-3's shock-cord gear is the long-travel one — Ty: "plenty of travel" — and 7 in at the
    ///    wheel is what the Univair gear geometry gives between the leg's droop and its stop (ESTIMATE from the
    ///    linkage; nobody has measured his aeroplane). It is ~4 times the static squash, which is what a gear
    ///    designed for FAR 23's 10 ft/s arrival needs.
    ///  - a BENDING LEG less: a steel or aluminium leaf 0.12 m, a composite bow 0.14 m (it is the springier one —
    ///    Ty: "springy fiberglass that flexes nicely").
    ///  - an OLEO: none. A real oleo's stroke is published per type and nobody has measured the fleet's, so its legs
    ///    keep the old no-stop spring rather than inherit a guess that could fire on a hard arrival.
    /// </summary>
    public const double BungeeTravelM = 0.18, SpringLeafTravelM = 0.12, SpringCompositeTravelM = 0.14;
    public static double LegTravelM(GearConfig g)
    {
        if (g.LegTravelM > 0) return g.LegTravelM;
        return KindOf(g) switch
        {
            LegKind.Bungee => BungeeTravelM,
            LegKind.SpringSteel or LegKind.SpringAluminum => SpringLeafTravelM,
            LegKind.SpringComposite => SpringCompositeTravelM,
            _ => double.PositiveInfinity,
        };
    }

    /// <summary>How much stiffer the leg is once it is on its stop (ESTIMATE: rubber blocks and metal-to-metal).</summary>
    public const double LegStopStiffnessFactor = 8.0;

    /// <summary>
    /// The leg's own spring force at a compression (N, no damping): the authored rate, plus the stop once the travel
    /// is used up. Linear in the working range on every kind — a shock cord's stretch and a leaf's bending both are,
    /// to the accuracy anything here is authored to — so the static stance, and every number tuned against it, is
    /// unchanged by this function existing.
    /// </summary>
    public static double LegForceN(GearConfig g, double stroke)
    {
        if (stroke <= 0) return 0;
        double f = g.SpringN * stroke;
        double travel = LegTravelM(g);
        if (stroke > travel) f += (LegStopStiffnessFactor - 1.0) * g.SpringN * (stroke - travel);
        return f;
    }

    /// <summary>The leg's secant rate at a compression (N/m): what a linear spring in series with the tyre
    /// (TireModel.Junction) has to be given so the junction sees the same force at this stroke.</summary>
    public static double LegSecantNPerM(GearConfig g, double stroke)
        => stroke > 1e-6 ? LegForceN(g, stroke) / stroke : g.SpringN;

    /// <summary>
    /// How far outboard the contact point travels per metre the leg compresses: drop/run of the line from the
    /// fuselage pivot to the contact point. 0 with no pivot authored (a telescopic strut, or a leg nobody has
    /// measured) — the contact then stays where it is, as it did before.
    ///
    /// It is the pivot-to-CONTACT drop, not the pivot-to-axle drop: the wheel cambers with the leg, so the lowest
    /// point of the tyre moves outboard by the tyre's radius times the leg's rotation on top of the axle's own
    /// travel. Taking the axle alone halves the answer.
    /// </summary>
    public static double SplayPerM(GearConfig g)
    {
        if (g.LegPivot == null || g.LegPivot.Length < 3 || g.Pos.Length < 3) return 0.0;
        double run = System.Math.Abs(g.Pos[1] - g.LegPivot[1]), drop = g.Pos[2] - g.LegPivot[2];
        if (run < 0.05 || drop <= 0.0) return 0.0;      // a pivot on top of the wheel, or above it: no arc to speak of
        return drop / run;
    }

    /// <summary>
    /// WHERE THE WHEEL ACTUALLY TOUCHES once the leg has compressed <paramref name="stroke"/> metres: on the arc
    /// about the fuselage pivot, so it is that much higher and further OUTBOARD than the authored point. The
    /// authored point is the leg at rest on its own (<see cref="GearConfig.Pos"/>, measured off the model), so the
    /// aeroplane's own weight already widens the track by the static squash times <see cref="SplayPerM"/> — which is
    /// why a Cub's wheels stand further apart with weight on them than on jacks.
    ///
    /// Exact for a hinged leg. A bending bow follows the same tangent and differs only in the second order (its
    /// chord shortens as it curves: under 3 mm at 100 mm of stroke on the Extra's), which is left out.
    /// </summary>
    public static MathTypes.Vec3 ContactPointM(GearConfig g, double stroke)
    {
        MathTypes.Vec3 p = g.PosVec();
        if (stroke <= 0 || SplayPerM(g) <= 0) return p;
        double qy = g.LegPivot![1], qz = g.LegPivot[2];
        double run = System.Math.Abs(g.Pos[1] - qy), drop = g.Pos[2] - qz;
        double len = System.Math.Sqrt(run * run + drop * drop);
        double dz = drop - stroke;                                    // the contact rises by exactly the stroke
        if (!(dz > -len)) return p;                                   // swung past the horizontal: not a real stroke
        double runNew = System.Math.Sqrt(System.Math.Max(0.0, len * len - dz * dz));
        double side = g.Pos[1] >= qy ? 1.0 : -1.0;
        return new MathTypes.Vec3(p.X, qy + side * runNew, qz + dz);
    }

    /// <summary>
    /// How far a leg has TURNED on its hinge (rad, + = swung UP toward the pivot) when a point of it sitting
    /// <paramref name="run"/> out and <paramref name="drop"/> down from the pivot has risen <paramref name="rise"/>
    /// metres. A rigid leg turns as one piece, so this one angle is the whole motion: it is the same for the axle, the
    /// axle fitting, the tyre's contact patch and the leg tubes, and each of them rises by its OWN amount at it.
    ///
    /// 0 where the geometry cannot deliver that rise (no arm, or a rise that would swing the point past the pivot's own
    /// height) — a visual asking for an impossible pose leaves the gear where the model drew it rather than invert.
    /// </summary>
    public static double HingeRadForRise(double run, double drop, double rise)
    {
        double len2 = run * run + drop * drop;
        if (len2 < 1e-9 || run < 1e-6) return 0.0;
        double dz = drop - rise;
        if (dz * dz >= len2) return 0.0;                               // swung past the horizontal: not a real stroke
        double runNew = System.Math.Sqrt(len2 - dz * dz);
        return System.Math.Atan2(drop, run) - System.Math.Atan2(dz, runNew);
    }

    /// <summary>
    /// The angle this leg's hinge stands at when it has compressed <paramref name="stroke"/> metres — the rotation
    /// <see cref="ContactPointM"/> is the result of, stated as an angle instead of a point, because that is what a
    /// DRAWN leg needs: a model's gear is one rigid piece hung on the fuselage (Gear_Pivot_L/R about Gear_HingeAxis_L/R
    /// in our own J-3 and Great Lakes GLBs), and the only thing you can do to it that keeps the legs on their fittings
    /// is turn it about that line.
    ///
    /// It is measured on the CONTACT point (the same run and drop <see cref="SplayPerM"/> and
    /// <see cref="ContactPointM"/> use), so the contact rises by exactly <paramref name="stroke"/> at it. The axle
    /// rises by LESS, because the tyre's radius vector is not along the arc: measured off our J-3's own GLB, the axle
    /// sits 0.485 m below and 0.600 m out from the pivot and the contact 0.7085 m below it, so 0.100 m of axle rise is
    /// 9.01 deg of hinge and 0.103 m of contact rise. 9.019 deg is what tools/own_models/build_j3_cub.py published on
    /// the node as `hinge_deg_full` for `wheel_rise_full_m` 0.10, and this gives 9.014 — the model's full 3-D arc and
    /// this planar one agree to 0.005 deg (GearVisualTests.TheHingeAngleIsTheOneTheModelPublishes).
    ///
    /// A DRAWN leg must not be turned by this angle about its own hinge line unless that line is square to the arc.
    /// The J-3's is, to 1.97 deg (measured: Gear_Pivot_L -> Gear_HingeAxis_L is (-0.9994, 0.0345, 0) in the GLB), and
    /// turning the drawn leg by this angle about THAT line lifts its contact 99.9 % of the asked stroke — nothing to
    /// correct. The Great Lakes' axle hinges at the fuselage CENTRELINE with its compression strut running aft, and its
    /// line is canted 35.8 deg in plan: the same angle lifts its wheel only 86-87 % as far (measured over 20-200 mm of
    /// stroke), so its drawn leg needs 1.14-1.16 times this angle. The drawn side therefore solves its OWN angle
    /// against its OWN measured axis for the rise this asks for; this function is the rise it has to hit, and the sim's
    /// statement of the kinematics.
    /// </summary>
    public static double LegHingeRad(GearConfig g, double stroke)
    {
        if (stroke <= 0 || SplayPerM(g) <= 0) return 0.0;
        return HingeRadForRise(System.Math.Abs(g.Pos[1] - g.LegPivot![1]), g.Pos[2] - g.LegPivot[2], stroke);
    }

    /// <summary>Tyre grip by surface, relative to the config's TireMu (dry pavement): gravel and grass hold less, the
    /// rough infield less again — a swerve there slides.</summary>
    public static double SurfaceGrip(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Paved => 1.0, WorldTerrain.Surface.Gravel => 0.75, WorldTerrain.Surface.Grass => 0.65,
        // Snow: a tyre has very little to hold on to, which is why a glacier landing is flown straight and why
        // a ground loop there is so hard to stop (μ ≈ 0.2 of dry tarmac).
        WorldTerrain.Surface.Snow => 0.20,
        // A packed, graded zone is the one snow surface a TYRE can do something with — compacted-snow
        // braking coefficients run 0.2–0.3 absolute against 0.6–0.8 dry, so about 0.4 of dry pavement.
        WorldTerrain.Surface.PackedGlacierSnow => 0.40,
        // Bare glacier ice in summer is melting, which is the FAA's "wet ice" (AC 25-32 6.3.8: "ice that is melting"):
        // Nil braking, below Ice/Poor's 0.083. The matrix publishes no number for Nil, because nobody operates on it;
        // what makes wet ice Nil is a water film between tread and ice, the same lubrication the matrix prices at
        // 0.053 for water above hydroplaning speed — its lowest coefficient. NASA TP-2917 measured the same 737s at
        // ~0.5 dry, the reference this table is a fraction of.
        WorldTerrain.Surface.GlacierIce => WetIceWheelBrakingCoefficient / DryWheelBrakingCoefficient,
        // Dry packed dirt holds about as well as firm gravel (μ 0.55-0.65 against 0.8 dry tarmac); hard, wet-packed
        // beach sand a little less (≈ 0.55 absolute).
        WorldTerrain.Surface.Dirt => 0.75,
        WorldTerrain.Surface.Sand => 0.55 / 0.8,
        _ => 0.55,
    };

    /// <summary>FAA AC 25-32 Table 2: the matrix's water-film wheel braking coefficient, taken for wet (melting) ice.</summary>
    public const double WetIceWheelBrakingCoefficient = 0.053;
    /// <summary>NASA TP-2917 (Yager and others 1990): effective braking friction of the test aircraft on dry runways.</summary>
    public const double DryWheelBrakingCoefficient = 0.5;

    // ---- WHEEL-SKIS (owner 2026-10-16) -----------------------------------------------------------------
    /// <summary>Rolling resistance of a ski on snow. Published figures for waxed ski bases on cold snow run
    /// 0.03–0.06; a loaded aircraft ski on a glacier is at the top of that. Against 0.14 for a wheel, which
    /// is why a ski-equipped aeroplane rolls out for ever and why you land uphill.</summary>
    public const double SkiRollingCoefficient = 0.055;
    /// <summary>And on a wind-packed, graded landing zone, where the base is running on polished crust
    /// rather than ploughing through settled snow: 0.02 (owner's handoff 2026-10-29).</summary>
    public const double SkiRollingCoefficientPacked = 0.02;
    /// <summary>Rolling resistance of a ski RUNNING on the surface it is actually on.</summary>
    public static double SkiRolling(WorldTerrain.Surface s)
        => s == WorldTerrain.Surface.PackedGlacierSnow ? SkiRollingCoefficientPacked : SkiRollingCoefficient;

    /// <summary>
    /// How much more a ski drags at a STANDSTILL than when it is running. A ski base rides on a microscopic
    /// film of water it melts itself as it goes, and at zero speed there is no film: published STATIC
    /// coefficients for a ski base on snow run 0.1–0.4 against 0.02–0.08 kinetic, so a factor of four is
    /// the conservative end of it.
    ///
    /// It matters for one concrete reason. The Knik's graded tongue falls at 2.8°, tan 2.8° = 0.049, and a
    /// ski's RUNNING coefficient on packed snow is 0.02. With no static term an aeroplane that had come to
    /// a stop on the zone crept back down it for ever and the Knik landing could never report a stop —
    /// which is not a quirk of the test: it is the model saying you cannot park on a glacier, and a ski
    /// pilot does park on one.
    /// </summary>
    public const double SkiStaticFactor = 4.0;
    /// <summary>The speed by which the film is established and the ski is down to its running value.</summary>
    public const double SkiFilmSpeedMs = 1.0;

    /// <summary>A ski's rolling resistance at the speed it is actually doing: static near a standstill,
    /// falling to its running value once the base has its film.</summary>
    public static double SkiRolling(WorldTerrain.Surface s, double speedMs)
    {
        double film = System.Math.Clamp(System.Math.Abs(speedMs) / SkiFilmSpeedMs, 0, 1);
        return SkiRolling(s) * (SkiStaticFactor - (SkiStaticFactor - 1) * film);
    }

    /// <summary>
    /// The static friction a ski base has before its melt film forms, OVER its running value — in whatever direction
    /// the base is being moved, since the film does not care which way it is dragged. Fore and aft it is already in
    /// <see cref="SkiRolling(WorldTerrain.Surface, double)"/>; across, it adds to the edge's hold, so an aeroplane
    /// parked on a cross-sloping snow patch stays put (3 deg of cross slope wants 0.052, more than the edge's 0.05
    /// alone). Zero once the base is running, so nothing changes at speed.
    /// </summary>
    public static double SkiStaticExcess(WorldTerrain.Surface s, double speedMs)
        => SkiRolling(s, speedMs) - SkiRolling(s);

    /// <summary>What a ski has instead of grip. A ski has no tread and no contact patch to speak of — it
    /// holds direction by its length in the snow, and it is a third of a tyre's answer on the same surface.</summary>
    public const double SkiGripScale = 0.33;

    /// <summary>
    /// The ski's own LATERAL hold, as an absolute μ against its vertical load — not a scale on the tyre's,
    /// because what resists a sideways slide is the ski's edge in the snow and the tyre inside it has nothing
    /// to do with it. On settled snow this is 0.05, which is what TireMu × SurfaceGrip(Snow) × SkiGripScale
    /// came to for the whole ski fleet and so leaves that behaviour where it was; on a packed, graded zone a
    /// ski bites more than twice as well (0.12, owner's handoff) — which is what makes a crosswind landing on
    /// the LZ a different proposition from one in soft snow, and why a ski pilot wants the packed strip.
    /// </summary>
    public static double SkiLateralCoefficient(WorldTerrain.Surface s)
        => s == WorldTerrain.Surface.PackedGlacierSnow ? 0.12 : 0.05;
    /// <summary>Cornering stiffness of a ski, per newton of load: a fraction of a tyre's, so it skids rather
    /// than bites and the aeroplane has to be flown straight.</summary>
    public const double SkiCorneringPerLoad = 1.6;
    // Since 2026-11-05 (Ty: hinged wheel-skis, the tyre sits ON the lowered ski) a ski is on the ground because the
    // selector has it down and its bottom is the lowest thing on the leg — not because the surface is snow. Which part
    // bears is SkiGear.Probe's geometry, in Compute below; with the ski down the wheel is off the ground and its brake
    // has nothing to act on (the old wheel-penetration ski's 15 % wheel load share is gone with the slot under the tyre).

    /// <summary>
    /// A WHEEL's rolling resistance on a packed glacier zone, which is not a property of the snow alone but of
    /// how far the tyre cuts into it. The owner's handoff asks for 0.18 rising toward 0.35 with sinkage, and
    /// the thing that decides sinkage is bearing pressure: a big soft bushwheel at a few psi floats on the
    /// crust, a small hard tyre under the same aeroplane breaks through and ploughs. So the sinkage term is
    /// driven by the static load this wheel carries, against what its own tyre can spread.
    /// </summary>
    public static double SnowSinkageRolling(AircraftConfig c, WorldTerrain.Surface surface, double staticLoadN)
    {
        // The aeroplane's main tyre (since 2026-11-04 a catalog tyre; the bushwheel option's is its tundra tyre).
        foreach (GearConfig g in c.Gear)
        {
            if (!TireFit.IsMainWheel(g)) continue;
            TireSpec? t = TireCatalog.Physical(g);
            if (t != null) return SnowSinkageRolling(t, TirePressure.Psi(c, g), surface, staticLoadN);
        }
        double floorMu = WorldTerrain.RollingCoefficient(surface);
        if (surface != WorldTerrain.Surface.PackedGlacierSnow) return floorMu;
        return floorMu + (0.35 - floorMu) * System.Math.Clamp(staticLoadN / 2500.0, 0, 1);
    }

    /// <summary>
    /// The crust's bearing pressure: a tyre pressing harder than this is through it and ploughing (ESTIMATE, Ty to
    /// check: wind-packed and graded snow carries of the order of 100–300 kPa; 125 kPa ≈ 18 psi puts a Cub's 8.00-6
    /// at its POH 18 psi through it and a tundra tyre at 8 psi half way, as the 2026-10-29 handoff described: "a big
    /// soft bushwheel floats on the crust, a small hard tyre breaks through").
    /// </summary>
    public const double PackedSnowCrustPa = 125e3;

    /// <summary>The same for one tyre, from its own ground pressure at that load (TireModel): sinkage = p_g / crust.</summary>
    public static double SnowSinkageRolling(TireSpec t, double psi, WorldTerrain.Surface surface, double loadN)
    {
        double floorMu = WorldTerrain.RollingCoefficient(surface);
        if (surface != WorldTerrain.Surface.PackedGlacierSnow) return floorMu;
        double defl = TireModel.StaticDeflectionM(t, psi, loadN);
        double sinkage = System.Math.Clamp(TireModel.GroundPressurePa(t, loadN, defl) / PackedSnowCrustPa, 0, 1);
        return floorMu + (0.35 - floorMu) * sinkage;
    }



    /// <summary>
    /// The castoring tailwheel's own yaw (owner 2026-09-12): the wheel is not bolted to the rudder — steering springs
    /// pull it toward the rudder command, the ground force on its contact patch (a trail behind the pivot) swings it
    /// toward the direction it is actually rolling, and past the breakout angle it swivels free. Its load is whatever
    /// the tail spring carries — tail weight less the aerodynamic tail load, so a tail-up wheel landing carries none.
    /// </summary>
    public sealed class TailwheelState
    {
        public double AngleRad;        // wheel yaw relative to the fuselage, + = wheel turned LEFT (Compute's steer frame)
        public double LastLateralN;    // side force on the contact patch last step (diagnostics)
        public double LastLoadN;       // vertical load last step
        public bool FreeSwivel;        // steering UNLOCKED: swivelled past UnlockRad, springs disengaged until re-engaged
    }

    /// <summary>Advance the tailwheel castor angle by dt from the current state and rudder command.</summary>
    /// <summary>Steering lock: unlock past `unlockRad` of castor; re-engage only inside rudder travel with the rudder matching.</summary>
    private static void UpdateLock(TailwheelState tw, double cmd, double maxSteerRad, double unlockRad)
    {
        double a = System.Math.Abs(tw.AngleRad);
        if (!tw.FreeSwivel) { if (a > unlockRad) tw.FreeSwivel = true; }
        else if (a <= maxSteerRad + 1e-9 && System.Math.Abs(tw.AngleRad - cmd) <= RelockToleranceRad) tw.FreeSwivel = false;
    }

    public static void UpdateTailwheel(AircraftConfig config, RigidBodyState s, TailwheelState tw, double rudderCmd, double dt,
                                       SkiState? skis = null)
    {
        GearConfig? g = null;
        int gIndex = -1;
        for (int i = 0; i < config.Gear.Count; i++) if (config.Gear[i].IsTailwheel && config.Gear[i].IsSteerable) { g = config.Gear[i]; gIndex = i; break; }
        if (g == null || tw == null) return;
        double k = g.SteerSpringNmPerRad > 0 ? g.SteerSpringNmPerRad : DefaultSteerSpringNmPerRad;
        double cD = g.SteerDampNms > 0 ? g.SteerDampNms : DefaultSteerDampNms;
        double trail = g.CastorTrailM > 0 ? g.CastorTrailM : DefaultCastorTrailM;
        double breakout = g.BreakoutRad > 0 ? g.BreakoutRad : DefaultBreakoutRad;
        double cmd = rudderCmd * g.MaxSteerRad;

        // Where the patch is being dragged: the tyre-aligned angle (slip-free) in the same steer frame Compute uses
        // (up × fwd, so a positive angle points the tyre to the aircraft's LEFT, where right rudder puts a trailing
        // tailwheel; this used to be mirrored, so the angle Compute applied pointed the tyre AWAY from its velocity,
        // doubled the slip and made the tailwheel fight every swerve — owner: "tailwheels still too stable").
        Vec3 cg = config.Mass.CgVec();
        Vec3 rBody = g.PosVec() - cg;
        Vec3 wheelWorld = s.Position + s.Attitude.Rotate(rBody);
        double groundH = WorldTerrain.WheelGroundHeightAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
        double penetration = wheelWorld.Z + groundH;
        // A tail ski down under the tailwheel: the ski, not the tyre, is what the castor's ground force acts on.
        bool skiBears = false;
        skis ??= config.Skis ? SkiState.For(config, SkiSelector.Position.Skis) : null;
        SkiGear.Geometry? skiGeo = skis != null && gIndex < skis.Legs.Length ? skis.Legs[gIndex] : null;
        if (skiGeo != null)
        {
            SkiGear.Contact sc = SkiGear.Probe(skiGeo, cg, s, skis!.Lowered01, 0.0);
            if (sc.PenetrationM > 0 && sc.PenetrationM > penetration)
            {
                skiBears = true; penetration = sc.PenetrationM; wheelWorld = sc.PointWorld; rBody = sc.PointBody - cg;
            }
        }
        double loadN = 0, cornering = 0, grip = 0, velAngle = 0; bool onGround = false;
        if (penetration > 0)
        {
            Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
            if (WorldDecks.All.Count > 0) vel -= WorldDecks.SurfaceVelocity(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
            TireSpec? twTire = skiBears ? null : TireCatalog.Physical(g);
            loadN = twTire != null
                  ? TireModel.QuasiStatic(twTire, TirePressure.Psi(config, g), penetration, vel.Z, LegSecantNPerM(g, penetration), g.DampNs, out _)
                  : System.Math.Max(0, LegForceN(g, penetration) + g.DampNs * System.Math.Max(0, vel.Z));
            Vec3 fwdWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
            Vec3 fwdGround = new Vec3(fwdWorld.X, fwdWorld.Y, 0);
            if (fwdGround.Length > 1e-6 && loadN > 0)
            {
                fwdGround = fwdGround / fwdGround.Length;
                Vec3 rightGround = new Vec3(fwdGround.Y, -fwdGround.X, 0);
                double vFwd = Vec3.Dot(vel, fwdGround), vSide = Vec3.Dot(vel, rightGround);
                velAngle = System.Math.Atan2(vSide, System.Math.Abs(vFwd) + 0.5);
                // A patch that is not rolling does not castor (static friction holds it): fade the tyre term in over
                // the first 0.5 m/s of patch speed.
                double rolling = System.Math.Clamp(System.Math.Sqrt(vFwd * vFwd + vSide * vSide) / 0.5, 0.0, 1.0);
                cornering = System.Math.Max(g.CorneringStiffnessN, TailwheelCorneringPerLoad * loadN) * rolling;
                WorldTerrain.Surface twSurface = WorldTerrain.SurfaceOverride
                    ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z) : WorldTerrain.Surface.Paved);
                grip = skiBears
                     ? SkiGear.LateralCoefficient(twSurface, System.Math.Sqrt(vFwd * vFwd + vSide * vSide)) * loadN
                     : g.TireMu * TailwheelMuScale * SurfaceGrip(twSurface) * loadN;
                onGround = true;
            }
        }
        tw.LastLoadN = loadN;

        // Castor: the ground force on a patch behind the pivot turns the wheel toward its velocity; the springs pull it
        // toward the rudder (unless broken out). Overdamped first order, so step it EXACTLY toward the balance point
        // (an explicit step with a rate clamp chattered around the balance whenever the tyre was friction-saturated):
        //   cD·δ' = C·trail·(δv − δ) − k·(δ − cmd)  →  δ_eq = (C·trail·δv + k·cmd)/(C·trail + k), τ = cD/(C·trail + k)
        // with the swing rate capped at what the friction-limited patch force can actually deliver.
        int sub = 4; double h = dt / sub;
        for (int i = 0; i < sub; i++)
        {
            UpdateLock(tw, cmd, g.MaxSteerRad, breakout);
            double err = tw.AngleRad - cmd;
            // Springs: none while unlocked, none inside the dead zone; outside it they pull toward the edge of the slack.
            double kEff = tw.FreeSwivel || System.Math.Abs(err) <= SteerDeadZoneRad ? 0.0 : k;
            double cmdEff = cmd + System.Math.Clamp(err, -SteerDeadZoneRad, SteerDeadZoneRad);
            double ct = onGround ? cornering * trail : 0.0;
            double denom = ct + kEff;
            if (denom < 1e-9) continue;
            double eq = (ct * velAngle + kEff * cmdEff) / denom;
            double tau = cD / denom;
            double move = (eq - tw.AngleRad) * (1 - System.Math.Exp(-h / tau));
            double maxRate = (grip * trail + kEff * System.Math.Abs(err)) / cD + 1e-6;
            move = System.Math.Clamp(move, -maxRate * h, maxRate * h);
            tw.AngleRad += move;
        }
        UpdateLock(tw, cmd, g.MaxSteerRad, breakout);
        tw.LastLateralN = onGround ? System.Math.Clamp(-cornering * (velAngle - tw.AngleRad), -grip, grip) : 0.0;
        // Full swivel: keep the angle wrapped so a 180° spin reads as a wheel rolling backwards, not 540°.
        while (tw.AngleRad > System.Math.PI) tw.AngleRad -= 2 * System.Math.PI;
        while (tw.AngleRad < -System.Math.PI) tw.AngleRad += 2 * System.Math.PI;
    }

    /// <param name="lostLegs">legs already torn off (skipped)</param>
    /// <param name="broken">receives the leg that just hit harder than its break sink rate</param>
    /// <param name="touching">per-leg contact state (first-contact detection), config.Gear order</param>
    /// <param name="impacts">records the hardest first contact (crash sound)</param>
    /// <summary>What <see cref="Compute"/> records for gear that is nowhere near the ground: every leg it would have tested
    /// is not touching. For a step the caller has already found clear of the ground (<see cref="AirframeContact.ClearOfTheGround(List{ContactPoint}, IReadOnlyList{Vec3}?, Vec3, RigidBodyState, double)"/>).</summary>
    public static void AllInTheAir(AircraftConfig config, bool[]? touching, IReadOnlyCollection<AirframeComponent>? lostLegs, bool wheelsUp)
    {
        if (touching == null) return;
        for (int gi = 0; gi < config.Gear.Count && gi < touching.Length; gi++)
        {
            GearConfig g = config.Gear[gi];
            if (wheelsUp && g.GearType != "float-keel") continue;
            if (lostLegs != null && lostLegs.Contains(AirframeContact.GearComponent(g))) continue;
            touching[gi] = false;
        }
    }

    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config, RigidBodyState s, double rudderCmd, double brakeCmd, double groundZ = 0.0, double brakeBias = 0.0,
        IReadOnlyCollection<AirframeComponent>? lostLegs = null, List<AirframeComponent>? broken = null, bool[]? touching = null, ImpactRecorder? impacts = null,
        TailwheelState? tailwheel = null, bool wheelsUp = false, SkiState? skis = null)
        => Compute(config, s, rudderCmd, brakeCmd, groundZ, brakeBias, lostLegs, broken, touching, impacts, tailwheel, wheelsUp, -1, 0.0, null, null, skis);

    /// <summary>
    /// The same, told which RK4 stage of a step this is (0..3; -1 = test every wheel every time) and the step's
    /// <paramref name="dt"/>, with one slot per wheel in <paramref name="stageClear"/>. On stage 0 each wheel remembers how
    /// far it is above the ground or water; on stages 1–3 a wheel clear by more than
    /// <see cref="AirframeContact.StageCullMargin"/> skips its ground queries, which would have found it in the air.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config, RigidBodyState s, double rudderCmd, double brakeCmd, double groundZ, double brakeBias,
        IReadOnlyCollection<AirframeComponent>? lostLegs, List<AirframeComponent>? broken, bool[]? touching, ImpactRecorder? impacts,
        TailwheelState? tailwheel, bool wheelsUp, int stage, double dt, double[]? stageClear, WheelState[]? wheels = null,
        SkiState? skis = null)
    {
        Vec3 totalForce = Vec3.Zero, totalMoment = Vec3.Zero;
        // Wheel-skis with no state handed in (a direct call): the skis are down, which is why they were fitted.
        skis ??= config.Skis ? SkiState.For(config, SkiSelector.Position.Skis) : null;
        bool cull = AirframeContact.StageCullEnabled && stage >= 0 && stageClear != null && stageClear.Length >= 2 * config.Gear.Count;
        if (config.Gear.Count == 0)
        {
            return (Vec3.Zero, Vec3.Zero);
        }

        Vec3 cg = config.Mass.CgVec();
        // Brake capacity per braked wheel (owner 2026-10-07: "make sure brakes are accurately modeled and traction is
        // accurately modeled"): the pedal sets a brake TORQUE on the wheel, sized so full pedal at the static load on dry
        // pavement uses 0.675 of the grip (the owner's 2026-09-13 tune). The tyre gives at most μ·N of its ACTUAL load:
        // lightly loaded (three-point with the wing still lifting) or on grass, the same torque stops the wheel and it
        // skids at μ_k ≈ 0.8 μ — braking falls off and the locked tyre can no longer corner. Since 2026-11-04 that is
        // the wheel's own equation (WheelSpin), not a branch.
        int braked = 0; foreach (GearConfig gb in config.Gear) if (gb.Brake) braked++;
        double staticLoadN = config.Mass.MassKg * 9.81 * 0.88 / System.Math.Max(1, braked);

        for (int gi = 0; gi < config.Gear.Count; gi++)
        {
            GearConfig g = config.Gear[gi];
            // An amphibian with its wheels up (2026-10-06, AirCam on amphib floats): the float keels still meet the ground.
            if (wheelsUp && g.GearType != "float-keel") continue;
            AirframeComponent leg = AirframeContact.GearComponent(g);
            if (lostLegs != null && lostLegs.Contains(leg)) continue;   // torn off: the stub/belly hard points carry the load
            // Differential braking: wheels left of centre get wheelBrake·(1+bias·-1)... i.e. bias<0 favours LEFT.
            double wheelBrake = g.Pos[1] < -0.05 ? brakeCmd * System.Math.Clamp(1.0 - brakeBias, 0.0, 1.0)
                              : g.Pos[1] > 0.05 ? brakeCmd * System.Math.Clamp(1.0 + brakeBias, 0.0, 1.0)
                              : brakeCmd;
            Vec3 rBody = g.PosVec() - cg;
            Vec3 wheelWorld = s.Position + s.Attitude.Rotate(rBody);
            // The wheel's spin (WheelSpin): advanced once a step, on its first stage, which also records what this
            // leg is doing for the sound. No state (a direct call), or not a wheel: its steady state instead.
            WheelState? ws = wheels != null && gi < wheels.Length ? wheels[gi] : null;
            bool firstStage = stage <= 0;
            if (ws != null && firstStage)
            {
                ws.ClearContact();
                // The tyre's own shape this step (TyreWear): a worn strip or a flat under the axle stands it lower. The
                // work the axle does dropping into it or climbing out is the thump you hear. Averaged over the arc the
                // wheel will turn this step, so a flat spot is never stepped over: one bump a revolution, at V/(2πr).
                double drop = ws.Tyre != null ? ws.Tyre.Drop(-ws.AngleRad, -(ws.AngleRad + ws.OmegaRadS * dt)) : 0.0;
                if (ws.TyreLoadN > 0) ws.ThumpJ += ws.TyreLoadN * System.Math.Abs(drop - ws.DropM);
                // The profile moves under the axle at that rate: the tyre's and the leg's dampers see it, as they see
                // any bump (a spot's edge at speed is a step the carcass must follow in a few milliseconds).
                ws.DropRateMs = ws.TyreLoadN > 0 && dt > 0 ? (drop - ws.DropM) / dt : 0.0;
                ws.DropM = drop;
            }
            double tyreDrop = ws?.DropM ?? 0.0;
            if (cull && stage > 0 && stageClear![2 * gi] > stageClear[2 * gi + 1])
            {
                // In the air all step: what the queries below would have found (penetration <= 0, no water to touch).
                if (touching != null && gi < touching.Length) touching[gi] = false;
                continue;
            }
            // Ground under THIS wheel: the world height field (plateau airports) — NED z = -height.
            double groundH = WorldTerrain.WheelGroundHeightAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
            double? waterH = FloatHydro.WaterSurfaceAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
            bool overWater = waterH.HasValue && waterH.Value >= groundH - 0.01;
            if (cull && stage == 0)
            {
                // Clearance to the ground (penetration's negative) and to the water (from the tyre's bottom, as below).
                double clearGround = (groundZ - groundH) - wheelWorld.Z;
                // A ski hangs below the tyre when it is down, and a ski at its pitch stop reaches further still.
                SkiGear.Geometry? sk = skis != null && gi < skis.Legs.Length ? skis.Legs[gi] : null;
                if (sk != null) clearGround -= sk.ThicknessM + SkiGear.SeatGapM + 0.5 * sk.LengthM;
                double clearWater = waterH.HasValue ? -wheelWorld.Z - waterH.Value : double.PositiveInfinity;
                stageClear![2 * gi] = System.Math.Min(clearGround, clearWater);
                stageClear[2 * gi + 1] = AirframeContact.StageCullMargin(s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody)).Length, dt);
            }
            if (overWater && g.GearType == "float-keel") continue;          // a float's hull does the floating
            if (overWater)
            {
                // A WHEEL on water: it planes while it is fast enough and ploughs when it is not. The force
                // goes on at its centre of pressure on the tyre, so the nose-over when the speed decays is the
                // moment arm's doing and not a special case. (WheelHydro has the physics and the references.)
                // The gear point is the BOTTOM of the tyre, as it is on the ground below (penetration is measured
                // from it): the water branch used to take it for the axle and put the tyre a radius lower.
                double tyreR = TireRadiusOf(config, g);
                double depth = waterH!.Value - (-wheelWorld.Z);
                if (depth > 0)
                {
                    Vec3 vWater = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
                    if (WorldDecks.All.Count > 0) vWater -= WorldDecks.SurfaceVelocity(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
                    // ... and through the WATER, which may itself be moving (a river, the tide: WaterCurrent).
                    vWater -= WaterCurrent.At(wheelWorld.X, wheelWorld.Y, 0.5 * depth);
                    // The water pushes on the TYRE, and the tyre is on the leg: the leg's spring is in series with
                    // the water, as it is with the ground. The tyre sits as deep as the leg lets it — where the
                    // water's push equals the spring's (quasi-static) — not at the gear point's geometric depth;
                    // without it every touch went straight into the airframe as if the leg were solid.
                    double tyreDepth = depth;
                    if (g.SpringN > 0)
                    {
                        double lo = 0, hi = depth;
                        for (int it = 0; it < 24; it++)
                        {
                            double mid = 0.5 * (lo + hi);
                            double up = -WheelHydro.Force(config, g, mid, vWater, out _, out _, out _).Z;
                            if (up > g.SpringN * (depth - mid)) hi = mid; else lo = mid;
                        }
                        tyreDepth = 0.5 * (lo + hi);
                    }
                    Vec3 hydro = WheelHydro.Force(config, g, tyreDepth, vWater, out _, out Vec3 cpOffset, out _);
                    if (ws != null && firstStage)
                    {
                        ws.InContact = true; ws.OverWater = true; ws.LoadN = -hydro.Z;
                        ws.VFwdMs = System.Math.Sqrt(vWater.X * vWater.X + vWater.Y * vWater.Y);
                        ws.Material = GroundMaterial.Water; ws.Brake01 = wheelBrake;
                    }
                    totalForce += hydro;
                    totalMoment += Vec3.Cross(s.Attitude.Rotate(rBody) + cpOffset, hydro);
                    if (touching != null && gi < touching.Length) touching[gi] = true;
                }
                // Deep water: the tyre never reaches the bed, so there is no ground contact to add.
                if (waterH.Value - groundH > tyreR * 2) continue;
            }
            double localGroundZ = groundZ - groundH;
            // >0 = wheel below ground surface (compressed). The gear point is a sound tyre's bottom; a worn or flat one's
            // is higher by its drop.
            double penetration = wheelWorld.Z - localGroundZ - tyreDrop;
            // WHEEL-SKIS: whichever of the ski's lowest point and the tyre's bottom is deeper in the ground carries the leg
            // (SkiGear: lowered, the tyre sits on the ski and it is always the ski; raised, always the tyre; in between,
            // whatever the links put lowest). A bearing ski moves the contact to its own point and takes the tyre out of the
            // load path: the load goes up through the pedestal and the links to the axle.
            bool skiBears = false;
            SkiGear.Geometry? skiGeo = skis != null && gi < skis.Legs.Length ? skis.Legs[gi] : null;
            if (skiGeo != null)
            {
                SkiGear.Contact sc = SkiGear.Probe(skiGeo, cg, s, skis!.Lowered01, groundZ);
                if (firstStage) skis.PitchRad[gi] = sc.PitchRad;
                if (sc.PenetrationM > 0 && sc.PenetrationM > penetration)
                {
                    skiBears = true;
                    penetration = sc.PenetrationM;
                    wheelWorld = sc.PointWorld;
                    rBody = sc.PointBody - cg;
                    groundH = WorldTerrain.WheelGroundHeightAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
                }
                if (firstStage) skis.Bearing[gi] = skiBears;
            }
            if (penetration <= 0.0)
            {
                if (touching != null && gi < touching.Length) touching[gi] = false;
                if (ws != null && firstStage) ws.TireDeflM = ws.TireDeflNextM = 0;   // a tyre in the air springs back
                continue; // wheel in the air
            }
            // Only for a wheel on the ground: it is three dozen region tests, and every wheel in the air used to pay them.
            WorldTerrain.Surface surface = WorldTerrain.SurfaceOverride
                ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z) : WorldTerrain.Surface.Paved);
            // The ground pushes along ITS normal, and the tyre rolls and slides in ITS plane. Taking world-up for both
            // made a slope a staircase: an aeroplane landing up an 8 deg snow patch was lifted, never slowed (the
            // force had no component along the slope), and one rolling into a crevasse wall was thrown upward by
            // the wall's whole vertical depth rather than stopped. On level ground the normal is world-up and
            // nothing changes; a deck or a cave floor is a level slab and keeps it.
            Vec3 groundUp = WorldTerrain.ContactNormalAt(wheelWorld.X, wheelWorld.Y, groundH);
            penetration *= -groundUp.Z;                       // vertical depth -> depth along the normal

            // Contact-point velocity (world) = body vel + ω×r, rotated to world.
            Vec3 contactVelWorld = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
            // ... relative to the surface: a moving deck (the carrier) carries the wheels with it.
            if (WorldDecks.All.Count > 0) contactVelWorld -= WorldDecks.SurfaceVelocity(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z);
            double compressionRate = -Vec3.Dot(contactVelWorld, groundUp); // closing rate along the normal (+ = compressing)
            if (ws != null) compressionRate -= ws.DropRateMs;               // a worn strip coming under the axle opens it
            if (touching != null && gi < touching.Length && !touching[gi])
            {
                // Touchdown on this leg: a hard enough one tears it off (individually — the other legs stay).
                touching[gi] = true;
                impacts?.Record(compressionRate, leg.ToString());
                double breakSink = (g.BreakSinkMs > 0 ? g.BreakSinkMs : g.IsTailwheel ? DefaultTailwheelBreakSinkMs : DefaultBreakSinkMs) * config.ImpactStrength;
                if (broken != null && compressionRate > breakSink && !broken.Contains(leg))
                {
                    broken.Add(leg);
                    if (ws != null && compressionRate > ws.ArrivalMs) { ws.ArrivalMs = compressionRate; ws.ArrivalBreakMs = breakSink; }
                }
            }

            // The tyre's reaction is along the GROUND NORMAL whatever the aircraft's pitch (the body axis here gives a
            // spurious fore/aft force). Never pulls.
            // Since 2026-11-04 a leg with a catalog tyre is the leg and the TYRE in series (TireModel.Junction): the tyre
            // deflects first, the load builds at the series rate, and the deflection sets the rolling radius, the
            // footprint and the slip stiffness. Skids, keels, feet and code-built legs with no tyre are the leg alone.
            TireSpec? tire = g.GearType == "float-keel" || skiBears ? null : ws != null ? ws.Tire : WheelSpin.IsWheel(g) ? TireCatalog.Physical(g) : null;
            double tyrePsi = tire == null ? 0 : ws != null ? ws.PressurePsi : TirePressure.Psi(config, g);
            // The LEG's own rate at the compression it is actually at: linear at the authored rate until its travel is
            // used up, then its stop (LegForceN). The junction below wants a linear spring, so it is given the secant
            // rate at this stroke; the leg's share of the total compression is what the tyre has not taken, which the
            // step starts from (the tyre's deflection a step ago — one step of lag on a few-hertz spring).
            double legStroke = System.Math.Max(0.0, penetration - (ws?.TireDeflM ?? 0.0));
            double kLeg = LegSecantNPerM(g, legStroke);
            double tyreDefl = 0, normalN;
            if (tire == null)
                normalN = System.Math.Max(0.0, LegForceN(g, penetration) + g.DampNs * System.Math.Max(0, compressionRate));
            else if (ws != null && stage >= 0 && dt > 0)
            {
                // The step starts from where the last one ended; stages 1–2 look half a step on, stage 3 a whole one.
                if (firstStage) ws.TireDeflM = ws.TireDeflNextM;
                double tau = stage == 0 ? 0 : stage == 3 ? dt : 0.5 * dt;
                tyreDefl = TireModel.Junction(tire, tyrePsi, ws.TireDeflM, penetration, compressionRate, tau, kLeg, g.DampNs, ws.TireDampingNsPerM, out normalN);
                if (firstStage)
                    ws.TireDeflNextM = TireModel.Junction(tire, tyrePsi, ws.TireDeflM, penetration + compressionRate * dt, compressionRate, dt,
                                                          kLeg, g.DampNs, ws.TireDampingNsPerM, out _);
                else if (stage == 3) ws.TireDeflNextM = tyreDefl;
            }
            else normalN = TireModel.QuasiStatic(tire, tyrePsi, penetration, compressionRate, LegSecantNPerM(g, penetration), g.DampNs, out tyreDefl);
            Vec3 normalForce = groundUp * normalN;
            // WHERE THE WHEEL IS NOW. A hinged or bending leg swings its contact point outboard as it compresses
            // (ContactPointM), so the force goes on at the WIDENED track and the arm that holds the aeroplane down on
            // its wheels grows with the load on them. With no pivot authored this is the authored point, as before.
            // (The contact's VELOCITY is still taken at the authored point: the splay changes it by the splay times the
            // body rates, millimetres per second, and taking it here would need the stroke before the ground query.)
            double legStrokeNow = System.Math.Max(0.0, penetration - tyreDefl);
            Vec3 rContact = SplayPerM(g) > 0 ? ContactPointM(g, legStrokeNow) - cg : rBody;
            if (ws != null && firstStage)
            {
                // A wheel standing in shallow water on the bed is still in the water to the ear.
                bool onDeck = WorldDecks.All.Count > 0 && WorldDecks.DeckUnder(wheelWorld.X, wheelWorld.Y, -wheelWorld.Z, out double deckTop)
                              && deckTop > WorldTerrain.GroundHeightAt(wheelWorld.X, wheelWorld.Y) + 1;
                // The stroke the line above swung the contact point by, kept for the DRAWN wheel (WheelState.LegStrokeM).
                // It is the same number the force went on at, not a second estimate of it.
                ws.LegStrokeM = legStrokeNow;
                ws.InContact = true; ws.LoadN = normalN; ws.Surface = surface; ws.Brake01 = wheelBrake;
                ws.TyreLoadN = normalN; ws.PatchWorld = wheelWorld;
                ws.OverWater = overWater;
                if (tire != null)
                {
                    ws.RollingRadiusM = TireModel.RollingRadiusM(tire, tyreDefl);
                    ws.FootprintLengthM = TireModel.FootprintLengthM(tire, tyreDefl);
                    ws.FootprintWidthM = TireModel.FootprintWidthM(tire, tyreDefl);
                    ws.GroundPressurePa = TireModel.GroundPressurePa(tire, normalN, tyreDefl);
                }
                ws.Material = GroundMaterials.Of(surface, onDeck, overWater);
            }

            if (g.GearType == "float-keel")
            {
                // FLOAT ON LAND (owner 2026-10-03: the Beaver landed on land never slowed down): a float keel doesn't roll —
                // its skid strip SLIDES. Kinetic (Coulomb) friction μ_k·N against the contact's ground-plane velocity, in
                // whatever direction it moves (no tyre cornering), μ_k by surface (KeelSlidingCoefficient). Smoothed below
                // ~0.1 m/s so it holds still at idle instead of chattering.
                Vec3 vG = contactVelWorld - groundUp * Vec3.Dot(contactVelWorld, groundUp);
                double vMag = vG.Length;
                Vec3 slideF = vMag > 1e-6 ? vG * (-WorldTerrain.KeelSlidingCoefficient(surface) * normalN / (vMag + 0.1)) : Vec3.Zero;
                Vec3 keelForce = normalForce + slideF;
                totalForce += keelForce;
                totalMoment += Vec3.Cross(s.Attitude.Rotate(rContact), keelForce);
                continue;
            }

            // Ground-plane tire frame: forward = aircraft heading projected on ground, right = ×down.
            Vec3 fwdWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
            Vec3 fwdGround = (fwdWorld - groundUp * Vec3.Dot(fwdWorld, groundUp));
            double fwdLen = fwdGround.Length;
            if (fwdLen < 1e-6)
            {
                totalForce += normalForce;
                totalMoment += Vec3.Cross(s.Attitude.Rotate(rContact), normalForce);
                continue;
            }

            fwdGround /= fwdLen;
            Vec3 rightGround = Vec3.Cross(groundUp, fwdGround); // right-hand: up × fwd = right

            // Steered wheels: a nosewheel points where the rudder says; a steerable TAILWHEEL points where its castor
            // state says (springs toward the rudder, ground force toward its velocity — UpdateTailwheel).
            bool castoring = g.IsTailwheel && g.IsSteerable && tailwheel != null;
            // Steer frame: + points the tyre LEFT. A trailing TAILWHEEL steered by right rudder points left (pushes the
            // tail left, nose right); a NOSEWHEEL steered by right rudder must point RIGHT — it was applied mirrored
            // (right rudder turned the 172 and Seminole left on the ground).
            // TOE (GearConfig.ToeOutDeg) rides on top of whatever steering this wheel has: a standing slip angle the
            // wheel keeps for ever, outboard on both sides, so the pair cancel level and restore when one is loaded.
            double steer = (castoring ? tailwheel!.AngleRad : SteerRad(g, rudderCmd)) + ToeRad(g);
            Vec3 tireFwd = fwdGround * System.Math.Cos(steer) + rightGround * System.Math.Sin(steer);
            Vec3 tireRight = Vec3.Cross(groundUp, tireFwd);

            // Contact velocity in the ground plane, decomposed on the tire frame.
            Vec3 velGround = contactVelWorld - groundUp * Vec3.Dot(contactVelWorld, groundUp);
            double vFwd = Vec3.Dot(velGround, tireFwd);
            double vSide = Vec3.Dot(velGround, tireRight);
            double speed = velGround.Length;

            // Lateral: cornering force opposes slip, ∝ slip angle, capped by the friction circle. Cornering stiffness
            // grows with the wheel load (a tyre's C ≈ 10–15 × its vertical load per radian): a fixed per-type constant
            // left the heavier types with a fifth of their friction budget and they skated sideways (owner).
            double slip = System.Math.Atan2(vSide, System.Math.Abs(vFwd) + 0.5);
            bool onSki = skiBears;
            if (ws != null && firstStage) { ws.OnSki = onSki; ws.VFwdMs = vFwd; }
            double cornerPerLoad = onSki ? SkiCorneringPerLoad : (g.IsTailwheel ? TailwheelCorneringPerLoad : CorneringPerLoad);
            double cornering = onSki ? cornerPerLoad * normalN
                                     : System.Math.Max(g.CorneringStiffnessN, cornerPerLoad * normalN);
            double lateralN = -cornering * slip;
            double muLimit = onSki
                           ? SkiGear.LateralCoefficient(surface, speed) * normalN
                           : g.TireMu * (g.IsTailwheel ? TailwheelMuScale : 1.0) * SurfaceGrip(surface) * normalN;

            // Longitudinal. On a ski: its own rolling drag and a load-limited brake, as before (no wheel turns against
            // the snow). On a tyre: the WHEEL's force (WheelSpin) — spin-up, braking, lock-up all from its slip — plus
            // the rolling resistance, a direct force at the patch (most of it is the tyre ploughing the ground).
            // A wheel's rolling resistance is its own tyre's on this ground (TireModel.RollingCoefficient: the tyre's
            // hysteresis, and the ground's compaction from its footprint pressure); on a packed snow zone, how far its
            // ground pressure takes it through the crust.
            double rollMu = onSki ? SkiGear.ForwardCoefficient(surface, vFwd)
                          : surface == WorldTerrain.Surface.PackedGlacierSnow
                            ? (tire != null ? SnowSinkageRolling(tire, tyrePsi, surface, staticLoadN) : SnowSinkageRolling(config, surface, staticLoadN))
                            : TireModel.RollingCoefficient(tire, tyrePsi, surface, normalN, tyreDefl);
            double rollN = rollMu * normalN;
            // A soft or flat tyre is squashed further and loses more to its own hysteresis (TyreWear).
            if (!onSki && ws?.Tyre != null) rollN += ws.Tyre.ExtraRollingCoefficient(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Paved)) * normalN;
            // Math.Sign THROWS on NaN, and this is reached four times a step. An aeroplane deep inside geometry
            // used to arrive here with a non-finite forward speed, and the throw left FlightSimDriver.Update
            // without its ApplyStateToTransform — a frozen aeroplane with dead controls, which is what the owner
            // saw. A sign is a sign: it cannot be the thing that stops the game.
            double fwdSign = System.Math.Sign(double.IsFinite(vFwd) && vFwd != 0 ? vFwd : 1);
            Vec3 tireForce;
            if (onSki)
            {
                lateralN = System.Math.Clamp(lateralN, -muLimit, muLimit);
                // No brake: the wheel sits on top of the ski, off the ground, and a brake on it stops only the wheel.
                // A ski's friction on snow is ANISOTROPIC: it slides forwards on its own base (SkiRollingCoefficient) and
                // holds sideways only by its length in the snow (SkiGripScale). So the forward drag is not capped by
                // the lateral budget — on snow that budget (0.8 × 0.20 × 0.33 = 0.053) is actually SMALLER than the
                // ski's own forward drag (0.055), and clamping to it quietly threw the ski's drag away. On hard ground
                // both are the base's plain sliding coefficient (SkiGear.HardSlidingCoefficient).
                double longN = -rollN * fwdSign;
                tireForce = tireRight * lateralN + tireFwd * longN;
            }
            else
            {
                // The lateral demand in units of the grip: a tyre already cornering hard has less to give fore and aft.
                double sigmaY = muLimit > 1e-9 ? System.Math.Abs(lateralN) / muLimit : 0.0;
                double fx, fall;
                // Full pedal at the static load on dry pavement asks for 0.675 of the grip (the owner's 2026-09-13 tune):
                // that is the brake's TORQUE, T = 0.675·μ·W_static·r, the same at every load. Lightly loaded (three-point
                // with the wing still lifting) or on grass, the same pedal asks for more than the tyre can give and the
                // wheel stops: lock-up, from the wheel's own equation.
                double brakeForceN = g.Brake ? wheelBrake * g.TireMu * staticLoadN * 0.675 : 0.0;
                // The slip stiffness the tyre has at this load and deflection (TR R-64), as the scale of its slip curve.
                double kappa0 = tire != null ? TireModel.SlipRatioScale(tire, tyrePsi, muLimit, tyreDefl) : WheelSpin.SlipRatioScale;
                if (ws != null && ws.IsWheel && firstStage && dt > 0)
                {
                    ws.SlipScale = kappa0;
                    double omega0 = ws.OmegaRadS, rRoll = ws.RollingRadiusM;
                    // The brake's torque is the brake's: a flat does not change it (SoundRollingRadiusM, not rRoll).
                    WheelSpin.StepResult res = WheelSpin.Advance(ref ws.OmegaRadS, ws.InertiaKgM2, rRoll, muLimit, vFwd, sigmaY,
                                                                 brakeForceN * ws.SoundRollingRadiusM, ws.BearingTorqueNm, dt, kappa0);
                    // The friction work went into the tread under the patch (TyreWear: wear, heat, a burst).
                    ws.Wear(res.SlipEnergyJ, System.Math.Abs(vFwd - ws.OmegaRadS * rRoll), normalN,
                            ws.AngleRad, ws.AngleRad + 0.5 * (omega0 + ws.OmegaRadS) * dt, dt);
                    ws.Advanced = true;
                    ws.ForcePerLoad = normalN > 1e-9 && double.IsFinite(res.ForceN / normalN) ? res.ForceN / normalN : 0.0;
                    ws.FallFactor = res.FallFactor;
                    ws.SlipRatio = res.SlipRatio; ws.SlipPowerW = res.SlipPowerW; ws.Locked = res.Locked;
                    ws.BrakeTorqueNm = brakeForceN * ws.RollingRadiusM;
                    ws.SlipEnergyJ += res.SlipEnergyJ; ws.SlipTimeS += res.SlipTimeS; ws.SlipImpulseNs += res.SlipImpulseNs;
                    ws.SlipUnderSquealJ += res.SlipUnderSquealJ; ws.SlipUnderScrubJ += res.SlipUnderScrubJ;
                }
                if (ws != null && ws.IsWheel && dt > 0)
                {
                    // Each stage applies the step's force per newton to its own load: zero if the wheel was in the air
                    // at the step's start (it is advanced from the next one).
                    fx = ws.Advanced ? ws.ForcePerLoad * normalN : 0.0;
                    fall = ws.Advanced ? ws.FallFactor : 1.0;
                }
                else fx = WheelSpin.QuasiStatic(vFwd, sigmaY, muLimit, brakeForceN, out _, out fall, kappa0);
                // The friction circle, with the wheel's own slip first (it is the wheel's equation that decides it) and
                // what is left for cornering; a locked tyre corners with only what its sliding slip points sideways.
                double latCap = System.Math.Sqrt(System.Math.Max(0.0, muLimit * fall * muLimit * fall - fx * fx));
                // How hard the slip angle asks against what the tyre has left sideways: 1 at the friction peak (the
                // slip angle where the linear cornering force meets the cap), more once the patch is sliding. The
                // sound only: a tyre still under its peak grips and is quiet (GroundSoundMix.SideSquealOnset).
                double latDemand = latCap > 1e-9 ? System.Math.Abs(lateralN) / latCap : (System.Math.Abs(lateralN) > 1e-9 ? 1e3 : 0.0);
                lateralN = System.Math.Clamp(lateralN, -latCap, latCap);
                double longN = fx - rollN * fwdSign;
                double longBudget = System.Math.Sqrt(System.Math.Max(0.0, muLimit * muLimit - lateralN * lateralN));
                longN = System.Math.Clamp(longN, -longBudget, longBudget);
                tireForce = tireRight * lateralN + tireFwd * longN;
                // For the sound only (nothing reads it back): the side force's work on the patch's sideways slip, the
                // scrub of a crabbed touchdown or a tight turn.
                if (ws != null && ws.IsWheel && firstStage && dt > 0 && double.IsFinite(lateralN) && double.IsFinite(vSide))
                {
                    double pSide = System.Math.Abs(lateralN * vSide);
                    ws.SideSlipEnergyJ += pSide * dt;
                    ws.SideSlipImpulseNs += System.Math.Abs(lateralN) * dt;
                    if (double.IsFinite(latDemand))
                    {
                        ws.SideSlipUnderSquealJ += pSide * dt * (1 - GroundSoundMix.SideSquealOnset(latDemand));
                        ws.SideSlipUnderScrubJ += pSide * dt * (1 - GroundSoundMix.SideScrubOnset(latDemand));
                    }
                    if (pSide > WheelSpin.AudibleSlipPowerW) ws.SideSlipTimeS += dt;
                }
            }
            Vec3 wheelForce = normalForce + tireForce;
            Aero.ForceDebug.Add(rContact + cg, s.Attitude.Conjugate().Rotate(wheelForce), Vec3.Zero, "gear");

            totalForce += wheelForce;
            totalMoment += Vec3.Cross(s.Attitude.Rotate(rContact), wheelForce);
        }

        return (totalForce, totalMoment);
    }

    /// <summary>A rudder-linked (not castoring) wheel's yaw in Compute's steer frame, + = tyre pointed LEFT: a nosewheel
    /// turns with the pedal (right rudder, wheel right), a trailing tailwheel against it. 0 for a wheel that does not
    /// steer. The linkage turns the wheel whether or not it is on the ground; it only makes a force while loaded.</summary>
    /// <summary>This wheel's standing toe angle in the steer sense (positive turns the wheel to the aeroplane's
    /// right): <see cref="GearConfig.ToeOutDeg"/> taken outboard, from the sign of the wheel's own y. A wheel on the
    /// centreline has none, whatever is authored, because out has no meaning there.</summary>
    /// <summary>
    /// A LIVE OVERRIDE ON EVERY MAIN WHEEL'S TOE, degrees out, or NaN to fly what each type authors. Ty 2026-11-10:
    /// "make the main wheel toe in/out an easy knob to turn for all planes" — he is tuning this by feel, and a
    /// number you can only change by rebuilding is not a knob. Negative is toe-IN.
    /// </summary>
    public static double ToeOutOverrideDeg = double.NaN;

    public static double ToeRad(GearConfig g)
    {
        double deg = double.IsNaN(ToeOutOverrideDeg) ? g.ToeOutDeg : ToeOutOverrideDeg;
        if (deg == 0.0 || g.Pos.Length < 2) return 0.0;
        double side = g.Pos[1] > 1e-6 ? 1.0 : g.Pos[1] < -1e-6 ? -1.0 : 0.0;
        return side * deg * System.Math.PI / 180.0;
    }

    public static double SteerRad(GearConfig g, double rudderCmd) =>
        g.IsSteerable ? (g.IsTailwheel ? 1.0 : -1.0) * rudderCmd * g.MaxSteerRad : 0.0;

    /// <summary>The steering lock of a type's rudder-linked NOSE wheel (a steerable non-tailwheel ahead of the CG), rad;
    /// 0 when it has none. RigInputs.NoseWheelSteerDeg is the rudder fraction times this.</summary>
    public static double NoseWheelSteerMaxRad(AircraftConfig config)
    {
        foreach (GearConfig g in config.Gear)
            if (g.IsSteerable && !g.IsTailwheel && g.Pos[0] > config.Mass.Cg[0]) return g.MaxSteerRad;
        return 0.0;
    }

    /// <summary>True if any wheel is in contact with the ground (for on-ground state / challenge logic).</summary>
    public static bool OnGround(AircraftConfig config, RigidBodyState s, double groundZ = 0.0)
    {
        Vec3 cg = config.Mass.CgVec();
        foreach (GearConfig g in config.Gear)
        {
            Vec3 wheelWorld = s.Position + s.Attitude.Rotate(g.PosVec() - cg);
            if (wheelWorld.Z - groundZ > 0.0)
            {
                return true;
            }
        }

        return false;
    }
}

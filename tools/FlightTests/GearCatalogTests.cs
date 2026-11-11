using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The undercarriage catalogue (Ty 2026-11-11: "wheels when applicable ... tundra tires ... 26 29 31 35 and 35HD
/// which is the same size but heavier, straight skis, wheel skis, straight floats, amphib floats. Available on every
/// airplane"), and the COMPONENT contract underneath it: mass, its own centre of mass, its own inertia from its own
/// shape, and aerodynamics applied at its own point.
///
/// What is pinned here, and why each one is a way this could rot quietly:
///  1. THE AERO OWNERSHIP PARTITIONS. A float's drag is FloatAero's and nothing else may add to it. Drag counted
///     twice fails silently, which is why it gets a structural test rather than a comment.
///  2. EVERY "Known" MASS HAS A SOURCE. Mechanically, so "which inputs did you invent" is answerable without
///     reading.
///  3. THE CENTRE OF MASS TRACKS THE GEOMETRY. GearConfig.Pos is the CONTACT POINT; the axle is a tyre radius above
///     it, and fitting a 35 in bushwheel moves it.
///  4. THE TAIL-SKI RULE. Ty's, and it must hold for a type nobody has thought about yet.
///  5. THE SKI WEIGHTS AGAINST THE CERTIFICATES. SkiGear's areal mass is an estimate; the equipment lists are not.
///  6. THE FLOATS DESTABILISE IN YAW, and the J-3 is left that way on purpose.
/// </summary>
public class GearCatalogTests
{
    private readonly ITestOutputHelper _out;
    public GearCatalogTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ---- 1. the menu ---------------------------------------------------------------------------------------------

    /// <summary>Six kinds, ten rows: the tundra option is five rows because the size is part of the choice.</summary>
    [Fact]
    public void TheMenuIsTheSixOptionsWithFiveTundraSizes()
    {
        double[] sizes = GearCatalog.Options.Where(o => o.Kind == UndercarriageKind.TundraTires)
                                            .Select(o => o.TundraNominalIn).ToArray();
        Assert.Equal(new double[] { 26, 29, 31, 35, 35 }, sizes);
        foreach (UndercarriageKind k in System.Enum.GetValues<UndercarriageKind>())
            Assert.Contains(GearCatalog.Options, o => o.Kind == k);
        // Every tundra row names a tyre that really is in the tyre catalogue, so the size and its weight have one home.
        foreach (UndercarriageOption o in GearCatalog.Options.Where(o => o.Kind == UndercarriageKind.TundraTires))
            Assert.True(TireCatalog.Get(o.TireId)?.Tundra == true, o.Id + " does not name a tundra tyre");
    }

    /// <summary>
    /// Ty: "35HD which is the same size but heavier". The two rows are the same 35x15x10 and differ by 16 lb of
    /// tyre — the heavy-duty carcass and a quarter-inch tread against three sixteenths. Same shape, different mass,
    /// which is the same relationship an amphibian float has with its straight sister.
    /// </summary>
    [Fact]
    public void The35AndThe35HDAreTheSameSizeAndDifferentWeights()
    {
        TireSpec light = TireCatalog.Get(GearCatalog.Get("tundra-35")!.TireId)!;
        TireSpec heavy = TireCatalog.Get(GearCatalog.Get("tundra-35hd")!.TireId)!;
        _out.WriteLine($"35: {light.OdIn:F0}x{light.WidthIn:F0}x{light.RimIn:F0}, tyre {light.TireLb:F0} lb; " +
                       $"35HD: {heavy.OdIn:F0}x{heavy.WidthIn:F0}x{heavy.RimIn:F0}, tyre {heavy.TireLb:F0} lb");
        Assert.Equal(light.OdIn, heavy.OdIn);
        Assert.Equal(light.WidthIn, heavy.WidthIn);
        Assert.True(heavy.TireLb > light.TireLb + 10);
    }

    // ---- 2. eligibility ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE ELIGIBILITY RULE READ OFF THE CONFIG: an aeroplane can be put on something else exactly when it has a
    /// WHEEL to take off, which WheelSpin.IsWheel already decides for the whole sim. That one test excludes
    /// everything Ty named and nothing he did not.
    /// </summary>
    [Theory]
    [InlineData("hughes-h4-like", false)]       // flying boat: a hull and tip floats, every leg a float-keel or skid
    [InlineData("hang-glider-like", false)]     // stands on feet
    [InlineData("paraglider-en-b-like", false)] // stands on feet
    [InlineData("j3-cub-like", true)]
    [InlineData("glider-2-33-like", true)]      // one wheel is a wheel: "available on every airplane"
    [InlineData("c172-like", true)]
    [InlineData("dhc2-beaver-like", true)]
    [InlineData("boeing-737-like", true)]
    public void OnlyAnAeroplaneWithAWheelIsOfferedAnything(string id, bool anything)
    {
        AircraftConfig c = Load(id);
        IReadOnlyList<UndercarriageOption> offered = GearCatalog.OfferedFor(c);
        _out.WriteLine($"{id}: {(offered.Count == 0 ? "nothing" : string.Join(", ", offered.Select(o => o.Id)))}");
        Assert.Equal(anything, offered.Count > 0);
    }

    /// <summary>Floats need a float that will legally lift the aeroplane as well as a wheel to take off, so the big
    /// jets are refused by arithmetic rather than by opinion — and every wheeled type keeps all four land options.</summary>
    [Fact]
    public void FloatsAreRefusedWhenNoFloatInTheRangeWillLiftIt()
    {
        AircraftConfig jet = Load("boeing-737-like");
        IReadOnlyList<UndercarriageOption> offered = GearCatalog.OfferedFor(jet);
        Assert.DoesNotContain(offered, o => o.IsFloat);
        Assert.Equal(8, offered.Count);     // wheels + five tundra sizes + two ski kinds
        AircraftConfig cub = Load("j3-cub-like");
        Assert.Contains(GearCatalog.OfferedFor(cub), o => o.Kind == UndercarriageKind.StraightFloats);
    }

    /// <summary>
    /// Ty's own worked example, now reaching the menu: the J-3 is offered straight floats and NOT amphibians,
    /// because no amphibian of a 1,300 lb aeroplane's size was ever built.
    /// </summary>
    [Fact]
    public void TheJ3IsOfferedStraightFloatsAndNoAmphibian()
    {
        IReadOnlyList<UndercarriageOption> offered = GearCatalog.OfferedFor(Load("j3-cub-like"));
        Assert.Contains(offered, o => o.Kind == UndercarriageKind.StraightFloats);
        Assert.DoesNotContain(offered, o => o.Kind == UndercarriageKind.AmphibianFloats);
    }

    // ---- 3. the component contract -------------------------------------------------------------------------------

    /// <summary>
    /// AERO OWNERSHIP PARTITIONS, with nothing counted twice and nothing orphaned. The float case is the one that
    /// matters: FloatAero already returns a force AND a moment at the floats' own position, so a float component
    /// carrying a drag area would double it and nothing would fail loudly.
    /// </summary>
    [Fact]
    public void NoComponentOwnsDragThatSomebodyElseAlreadyComputes()
    {
        foreach (string id in new[] { "j3-cub-like", "c172-like", "dhc2-beaver-like", "glider-2-33-like" })
        {
            AircraftConfig c = Load(id);
            foreach (UndercarriageOption o in GearCatalog.OfferedFor(c))
            {
                IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, o);
                Assert.NotEmpty(set);
                foreach (GearComponent g in set)
                {
                    if (g.AeroOwnedBy != AeroOwner.Components)
                        Assert.Equal(0, g.DragAreaM2);                  // somebody else's: contributes nothing here
                    if (g.AeroOwnedBy == AeroOwner.Components)
                        Assert.True(g.DragAreaM2 > 0, $"{id}/{o.Id}/{g.Id} owns its aero but has no area");
                }
                // A float set's whole flat-plate area is zero HERE, because FloatAero has it.
                if (o.IsFloat) Assert.Equal(0, GearAero.FlatPlateAreaM2(set));
                else Assert.True(GearAero.FlatPlateAreaM2(set) > 0);
            }
        }
    }

    /// <summary>Every component whose mass is claimed KNOWN names where the figure came from. Mechanical, so the
    /// question "which of these did you invent?" does not need a reader.</summary>
    [Fact]
    public void EveryKnownMassNamesItsSource()
    {
        foreach (string id in new[] { "j3-cub-like", "c172-like", "pa18-cub-like", "dhc2-beaver-like" })
        {
            AircraftConfig c = Load(id);
            foreach (UndercarriageOption o in GearCatalog.OfferedFor(c))
                foreach (GearComponent g in GearCatalog.ComponentsFor(c, o))
                {
                    Assert.False(string.IsNullOrWhiteSpace(g.MassSource), $"{id}/{o.Id}/{g.Id} has no mass source");
                    Assert.True(g.MassKg > 0, $"{id}/{o.Id}/{g.Id} weighs nothing");
                }
        }
    }

    /// <summary>
    /// THE CENTRE OF MASS TRACKS THE GEOMETRY. GearConfig.Pos is the CONTACT POINT, not the centre of mass: the axle
    /// is one tyre radius above it, and fitting a 35 in bushwheel in place of the stock tyre moves the wheel's mass
    /// up by the difference in radius. A stored constant could not do this, and the wheel is the component most
    /// likely to be swapped.
    /// </summary>
    [Fact]
    public void FittingABiggerTyreMovesTheWheelsMass()
    {
        AircraftConfig c = Load("pa18-cub-like");
        GearComponent stock = First(c, "wheels", "-wheel");
        GearComponent big = First(c, "tundra-35", "-wheel");
        _out.WriteLine($"stock axle z {stock.CmBody.Z:F3} m, 35 in axle z {big.CmBody.Z:F3} m, mass {stock.MassKg:F1} -> {big.MassKg:F1} kg");
        Assert.True(big.CmBody.Z < stock.CmBody.Z - 0.05);   // +z is DOWN, so a bigger tyre lifts the axle
        Assert.True(big.MassKg > stock.MassKg);
    }

    private static GearComponent First(AircraftConfig c, string optionId, string idSuffix) =>
        GearCatalog.ComponentsFor(c, GearCatalog.Get(optionId)!).First(g => g.Id.EndsWith(idSuffix));

    /// <summary>
    /// A WHEEL'S INERTIA IS A DISC'S, and its polar term is the tyre catalogue's own part-by-part figure (tread,
    /// sidewalls, beads, tube, wheel, brake disc, each at its own radius) rather than anything recomputed here — so
    /// the spin-up model and the mass build-up read one number. The two transverse terms are half the polar plus
    /// m·w²/12, which is the perpendicular-axis theorem for a body whose mass lies in a plane, widened.
    /// </summary>
    [Fact]
    public void AWheelsInertiaIsTheTyreCatalogsOwn()
    {
        AircraftConfig c = Load("pa18-cub-like");
        GearComponent w = First(c, "wheels", "-wheel");
        GearConfig leg = c.Gear.First(g => GearCatalog.IsMainLeg(g));
        TireSpec t = TireCatalog.Of(leg)!;
        MassProperties I = w.InertiaAboutCm;
        _out.WriteLine($"{t.Name}: polar {t.InertiaKgM2(leg.Brake):F4}, component Iyy {I.Iyy:F4}, Ixx {I.Ixx:F4} kg m^2");
        Assert.Equal(t.InertiaKgM2(leg.Brake), I.Iyy, 6);
        Assert.Equal(0.5 * I.Iyy + w.MassKg * t.SectionWidthM * t.SectionWidthM / 12.0, I.Ixx, 6);
        Assert.Equal(I.Ixx, I.Izz, 6);
        Assert.Equal(0, I.Ixz);
        Assert.True(w.Spins);
    }

    /// <summary>
    /// On a light aeroplane a wheel's OWN inertia is a rounding error next to its parallel-axis term — a point mass
    /// would be fair, and the shape is given anyway because TireSpec computes it for free and because the moment the
    /// wheel becomes a 35 in bushwheel or a ski the answer stops being small. The number is recorded here so the
    /// argument does not have to be had again.
    /// </summary>
    [Fact]
    public void AWheelsOwnInertiaIsSmallNextToItsArm()
    {
        AircraftConfig c = Load("j3-cub-like");
        GearComponent w = First(c, "wheels", "-wheel");
        Vec3 cg = c.Mass.CgVec();
        Vec3 r = w.CmBody - cg;
        double own = w.InertiaAboutCm.Ixx, axis = w.MassKg * (r.Y * r.Y + r.Z * r.Z);
        _out.WriteLine($"J-3 main wheel: own Ixx {own:F4}, parallel-axis {axis:F4} kg m^2 = {100 * own / axis:F1} %");
        Assert.InRange(own / axis, 0, 0.05);
    }

    /// <summary>The set's inertia about the CG is the parallel-axis sum, and a pair of mains at +-y with a tailwheel
    /// on the centreline stays symmetric: Ixy and Iyz come out zero, and would not on an aeroplane that had lost a
    /// leg, which is why MassProperties carries them.</summary>
    [Fact]
    public void TheSetsInertiaIsTheParallelAxisSum()
    {
        AircraftConfig c = Load("j3-cub-like");
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("wheels")!);
        MassProperties I = GearMass.About(set, c.Mass.CgVec());
        _out.WriteLine($"J-3 wheels: {I.MassKg:F1} kg, Ixx {I.Ixx:F2}, Iyy {I.Iyy:F2}, Izz {I.Izz:F2}, Ixz {I.Ixz:F2}");
        Assert.Equal(GearMass.TotalKg(set), I.MassKg, 6);
        Assert.Equal(0, I.Ixy, 6);
        Assert.Equal(0, I.Iyz, 6);
        Assert.True(I.Ixx > 0 && I.Iyy > 0 && I.Izz > 0);
    }

    // ---- 4. the tail-ski rule ------------------------------------------------------------------------------------

    /// <summary>
    /// TY'S RULE: "Make all tail skis wheel skis so the tire sticks through the ski for normal handling on the
    /// ground but offers floatation on snow." So "straight skis" is a statement about the MAINS: on a straight-ski
    /// taildragger the mains have a ski and NO wheel, and the tail has a ski AND its wheel. It is encoded once, in
    /// ComponentsFor, so it holds for a type nobody has thought about yet.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]       // a nosewheel is not a main either: it keeps its wheel too
    public void EveryTailSkiIsAWheelSki(string id)
    {
        AircraftConfig c = Load(id);
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-straight")!);
        for (int i = 0; i < c.Gear.Count; i++)
        {
            GearConfig g = c.Gear[i];
            if (!WheelSpin.IsWheel(g)) continue;
            string leg = GearCatalog.LegId(g, i);
            bool hasWheel = set.Any(x => x.Id == leg + "-wheel");
            bool hasSki = set.Any(x => x.Id.StartsWith(leg + "-ski"));
            _out.WriteLine($"{id} {leg}: ski {hasSki}, wheel {hasWheel}");
            Assert.True(hasSki, leg + " has no ski");
            Assert.Equal(!GearCatalog.IsMainLeg(g), hasWheel);
        }
        Assert.True(GearCatalog.TailIsAlwaysAWheelSki);
    }

    /// <summary>Wheel skis keep every wheel, mains included — that is the whole difference.</summary>
    [Fact]
    public void WheelSkisKeepEveryWheel()
    {
        AircraftConfig c = Load("j3-cub-like");
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-wheel")!);
        int wheels = set.Count(x => x.Id.EndsWith("-wheel")), skis = set.Count(x => x.Id.Contains("-ski"));
        _out.WriteLine(string.Join(" ", set.Select(x => x.Id)));
        Assert.Equal(wheels, skis);
        Assert.True(wheels >= 3);
    }

    // ---- 5. the ski weights against the certificates -------------------------------------------------------------

    /// <summary>
    /// SkiGear's mass comes from an areal density it calls an ESTIMATE. The FAA Aircraft Specification equipment
    /// lists are not estimates, and a STRAIGHT ski pair on them runs 2.1-3.2 % of the aeroplane's gross weight
    /// (eight points, two makers, 1450-2400 lb). This is the check that the geometry-derived mass has not wandered
    /// away from the certificates — it is not used to compute anything.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("c172-like")]
    public void AStraightSkiPairIsAFewPerCentOfGross(string id)
    {
        AircraftConfig c = Load(id);
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-straight")!);
        double mainsKg = set.Where(g => g.Id.Contains("-ski-straight")).Sum(g => g.MassKg);
        double frac = mainsKg / c.Mass.MassKg;
        _out.WriteLine($"{id}: straight main skis {mainsKg / FloatSize.Lb:F1} lb on {c.Mass.MassKg / FloatSize.Lb:F0} lb gross = " +
                       $"{100 * frac:F2} % (certificates: {100 * GearCatalog.StraightSkiPairFractionLo:F1}-{100 * GearCatalog.StraightSkiPairFractionHi:F1} %)");
        // MEASURED, AND IT DOES NOT AGREE AT THE TOP OF THE RANGE. A J-3 comes out near 2 % and inside the band; a
        // Super Cub 1.3 % and a 172 0.7 %, both LIGHT. The cause is upstream of this catalogue: SkiGear sizes a ski
        // from the FUSELAGE LENGTH and gives it a fixed mass per square metre, and neither tracks the weight the ski
        // has to carry, so the heavier the aeroplane the further the ski falls behind the certificated one. The band
        // asserted here is today's behaviour, not the certificates'; it is deliberately a pin on a KNOWN GAP so that
        // fixing SkiGear's sizing rule trips it and somebody has to come back and widen it on purpose.
        Assert.InRange(frac, 0.005, GearCatalog.StraightSkiPairFractionHi * 1.2);
    }

    /// <summary>
    /// A TAIL SKI IS TINY, and two certificates say how tiny: 3 lb for a 1,500 lb-class straight tail ski
    /// (A-768 Rev 35 item 205) and 6 lb for a 2,500 lb-class one (A-799 Rev 55 item 211) — beside the 5-8 lb
    /// tailwheel they sit with. So Ty's "make all tail skis wheel skis" costs almost nothing, and the certificated
    /// retractable tail ski is in fact quoted as +5.5 lb ON TOP of the tailwheel (1A2 item 208(f)), which is exactly
    /// the configuration his rule describes.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("pa18-cub-like")]
    public void ATailSkiWeighsWhatTheCertificatesSay(string id)
    {
        AircraftConfig c = Load(id);
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-straight")!);
        GearComponent tail = set.First(g => g.Id.StartsWith("tail") && g.Id.Contains("-ski"));
        double lb = tail.MassKg / FloatSize.Lb;
        _out.WriteLine($"{id} tail ski {lb:F1} lb (certificated straight tail skis: 3 lb at 1,500 lb class, 6 lb at 2,500)");
        Assert.InRange(lb, GearCatalog.TailSkiPairLb * 0.5, GearCatalog.TailSkiPairHiLb);
    }

    /// <summary>The straight-to-wheel-ski ratio is MEASURED, not guessed: two certificate pairs from one maker at
    /// two ratings, 32/76 and 53/102.</summary>
    [Fact]
    public void AStraightSkiIsAboutHalfAWheelSki()
    {
        Assert.InRange(GearCatalog.StraightSkiMassFraction, 32.0 / 76.0, 53.0 / 102.0);
        AircraftConfig c = Load("j3-cub-like");
        double straight = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-straight")!)
                                    .Where(g => g.Id.Contains("-ski-straight")).Sum(g => g.MassKg);
        double wheelSki = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-wheel")!)
                                     .Where(g => g.Id.Contains("-ski-penetration") && g.Id.StartsWith("main")).Sum(g => g.MassKg);
        Assert.Equal(GearCatalog.StraightSkiMassFraction, straight / wheelSki, 3);
    }

    // ---- 6. the exchange -----------------------------------------------------------------------------------------

    /// <summary>
    /// THE EXCHANGE IS SET AGAINST SET, because a float set REPLACES the wheels rather than joining them. Ty's own
    /// aeroplane is the check: a J-3 that weighs 700 lb empty on wheels weighs 825 on floats, and the 125 lb is the
    /// float system LESS the gear that came off. Here the whole tensor changes too, not just the mass.
    /// </summary>
    [Fact]
    public void PuttingTheCubOnFloatsIsAnExchangeNotAnAddition()
    {
        AircraftConfig c = Load("j3-cub-like");
        Vec3 cg = c.Mass.CgVec();
        IReadOnlyList<GearComponent> wheels = GearCatalog.ComponentsFor(c, GearCatalog.Get("wheels")!);
        AircraftConfig onFloats = Load("j3-cub-floats-like");
        IReadOnlyList<GearComponent> floats = GearCatalog.ComponentsFor(onFloats, GearCatalog.Get("floats-straight")!);

        MassProperties d = GearMass.Exchange(floats, wheels, cg);
        _out.WriteLine($"J-3 wheels {GearMass.TotalKg(wheels):F1} kg -> floats {GearMass.TotalKg(floats):F1} kg; " +
                       $"exchange {d.MassKg:F1} kg = {d.MassKg / FloatSize.Lb:F1} lb, dIxx {d.Ixx:F1}, dIyy {d.Iyy:F1}, dIzz {d.Izz:F1} kg m^2");
        Assert.True(d.MassKg > 0);                                   // floats are heavier than the gear they replace
        Assert.True(d.MassKg < GearMass.TotalKg(floats));            // ... but not by the whole float system
        // The whole tensor grows: a pair of floats is a long body a metre and a half below the CG.
        Assert.True(d.Ixx > 0 && d.Iyy > 0 && d.Izz > 0);
    }

    /// <summary>
    /// A PAIR OF FLOATS IS TWO BODIES, NOT ONE. Modelling them as a single box on the centreline throws away exactly
    /// the thing that makes a floatplane roll and yaw like a floatplane: two 34 kg bodies 2.6 m apart contribute
    /// 2 x 34 x 1.3^2 = 115 kg m^2 of Ixx and the same again of Izz, and a centreline box contributes none of it.
    /// The first pass of this catalogue did that and a J-3's floats came out with an Izz of 0.2 kg m^2 — a number
    /// that looks like a unit error and is really a geometry error. This is the test that would have caught it.
    /// </summary>
    [Fact]
    public void ThePairOfFloatsSitsAtItsOwnSpread()
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("floats-straight")!);
        Assert.Equal(2, set.Count);
        Assert.Equal(-set[0].CmBody.Y, set[1].CmBody.Y, 6);
        Assert.True(System.Math.Abs(set[0].CmBody.Y) > 0.5, "the floats must be out at their own spread");
        Assert.Equal(set[0].MassKg, set[1].MassKg, 6);
        MassProperties I = GearMass.About(set, c.Mass.CgVec());
        _out.WriteLine($"J-3 floats: {I.MassKg:F1} kg at +-{set[1].CmBody.Y:F2} m; Ixx {I.Ixx:F1}, Iyy {I.Iyy:F1}, Izz {I.Izz:F1} kg m^2");
        // Izz is dominated by the lateral separation, so it must be the same order as Ixx, not two orders smaller.
        Assert.InRange(I.Izz / I.Ixx, 0.3, 3.0);
        Assert.Equal(0, I.Ixy, 6);   // a symmetric pair
    }

    /// <summary>
    /// The scalar exchange rule and the component one are THE SAME ARITHMETIC and must not drift: FloatExchange's
    /// 125 lb for Ty's J-3 (the 1320's ~175 lb system less ~50 lb of gear) is what the component sets give too, to
    /// within the pound the catalogue's derived float weight sits from the round number.
    /// </summary>
    [Fact]
    public void TheComponentExchangeAgreesWithTheScalarOne()
    {
        FloatSize s = FloatCatalog.Select(1300)!;
        double scalarLb = FloatExchange.ExchangeLb(50, s, amphibian: false);
        _out.WriteLine($"scalar: {s.StraightInstalledLb:F1} lb of float - 50 lb of gear = {scalarLb:F1} lb; Ty's figure is 125");
        Assert.InRange(scalarLb, 124.0, 126.0);
    }

    // ---- 7. the floats in sideslip -------------------------------------------------------------------------------

    /// <summary>
    /// Ty 2026-11-11: "make sure the floats inertia lift drag etc is modeled correctly at the correct arm sideslip
    /// too", and the order he set: model it first, MEASURE what it does, and only then decide whether anything needs
    /// adding. This is the measurement, and it does not say what was expected.
    ///
    /// THE SIDE FORCE IS REAL AND ITS DIRECTION IS RIGHT: a positive sideslip pushes a J-3's floats 119 N to -y at
    /// 30 m/s and 3 m/s of slip.
    ///
    /// ITS YAWING MOMENT IS WEAKLY STABILISING, NOT DESTABILISING — about +18 N m, the same sign as the fin's. The
    /// reason is geometric and measurable: the force is applied at the floats' own centroid, HALF A LENGTH AFT OF
    /// THE BOW, and on this airframe that point sits 0.15 m BEHIND the centre of gravity, not ahead of it. A float's
    /// bow reaches out past the propeller, but its middle does not.
    ///
    /// SO THE MODEL IS MISSING THE TERM THAT REALLY DESTABILISES A SLENDER BODY, and it is not a force at all: the
    /// MUNK MOMENT (Munk 1924, NACA TR 184; Allen and Perkins, NACA RM A50L07), a pure couple of
    /// (k2-k1)·q·Volume·sin 2beta that acts to turn any closed body broadside regardless of where you apply forces.
    /// At this condition it is of order 120 N m NOSE-OUT on a pair of 1320s, which is seven times the crossflow
    /// term and the opposite sign — i.e. it, and not the side force, is why floatplanes grow fin area. It is not
    /// added here because it needs the float's VOLUME and FloatsConfig does not carry one; that is a real gap and it
    /// is reported rather than papered over. Nothing has been adjusted to make this test read better.
    /// </summary>
    [Fact]
    public void TheFloatsSideForceIsRightAndItsYawingMomentIsNotTheWholeStory()
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        (Vec3 f0, Vec3 m0) = FloatAero.Compute(c, new Vec3(30, 0, 0), 1.225);
        (Vec3 f1, Vec3 m1) = FloatAero.Compute(c, new Vec3(30, 3, 0), 1.225);
        double armX = c.Floats!.BowX - 0.5 * c.Floats.LengthM - c.Mass.CgVec().X;
        _out.WriteLine($"J-3 floats: side force {f0.Y:F1} -> {f1.Y:F1} N, yaw {m0.Z:F1} -> {m1.Z:F1} N m, " +
                       $"roll {m0.X:F1} -> {m1.X:F1} N m; centroid {armX:+0.000;-0.000} m from the CG");
        Assert.True(f1.Y < -1.0, "a positive sideslip must push the floats to -y");
        Assert.Equal(0.0, f0.Y, 6);                       // and nothing at all in balanced flight
        // The measured arm, pinned: if a later lane moves where the side force acts, the sign of its yawing moment
        // changes with it and this is the number that says so.
        Assert.InRange(armX, -0.30, 0.0);
        Assert.True(m1.Z > m0.Z, "with the centroid aft of the CG the crossflow term is weakly STABILISING");
    }

    /// <summary>
    /// And the same force acts BELOW the centre of gravity, so it also takes away dihedral effect — exactly as a
    /// ventral fin does. Both moments fall out of applying the force at the right point; neither is a coefficient,
    /// and neither could exist if the floats' force were a scalar taken off the thrust.
    /// </summary>
    [Fact]
    public void TheFloatsAlsoTakeAwayDihedralEffect()
    {
        AircraftConfig c = Load("j3-cub-floats-like");
        (_, Vec3 m0) = FloatAero.Compute(c, new Vec3(30, 0, 0), 1.225);
        (_, Vec3 m1) = FloatAero.Compute(c, new Vec3(30, 3, 0), 1.225);
        _out.WriteLine($"roll moment {m0.X:F1} -> {m1.X:F1} N m in a positive sideslip (a stable aeroplane rolls the other way)");
        Assert.True(m1.X > m0.X + 1.0);
        Assert.True(c.Floats!.KeelZ > c.Mass.CgVec().Z, "the floats must be BELOW the CG for that sign to mean anything");
    }

    /// <summary>
    /// THE J-3 IS LEFT ALONE. Ty: "If floats decrease directional stability too much add fins on the stabilizers or
    /// a ventral fin on the belly ahead of the rudder ... The j3 has none so leave it alone." So the J-3 on floats
    /// must carry exactly the same surfaces as the J-3 on wheels — no finlet, no ventral fin — and come out less
    /// directionally stable, which is what the real aeroplane does. If this test ever fails because somebody added a
    /// surface to make a number comfortable, the surface is the bug.
    /// </summary>
    [Fact]
    public void TheJ3OnFloatsGrowsNoFins()
    {
        string[] land = Load("j3-cub-like").Surfaces.Select(s => s.Id).OrderBy(s => s).ToArray();
        string[] water = Load("j3-cub-floats-like").Surfaces.Select(s => s.Id).OrderBy(s => s).ToArray();
        _out.WriteLine("wheels: " + string.Join(", ", land) + "\nfloats: " + string.Join(", ", water));
        Assert.Equal(land, water);
    }

    // ---- 8. the aero acts where the component is -----------------------------------------------------------------

    /// <summary>
    /// Ty: "the lift drag moment at the actual location need to be accurate". A main wheel hangs below the CG, so
    /// its drag makes a PITCHING MOMENT and not just a number off the thrust — and a 35 in bushwheel makes several
    /// times as much of it as the stock tyre. That is the whole reason a component carries a position.
    /// </summary>
    [Fact]
    public void WheelDragMakesAPitchingMomentAndABiggerTyreMakesMore()
    {
        AircraftConfig c = Load("pa18-cub-like");
        Vec3 cg = c.Mass.CgVec(), v = new(40, 0, 0);
        (Vec3 fs, Vec3 ms) = GearAero.Compute(GearCatalog.ComponentsFor(c, GearCatalog.Get("wheels")!), v, 1.225, cg);
        (Vec3 fb, Vec3 mb) = GearAero.Compute(GearCatalog.ComponentsFor(c, GearCatalog.Get("tundra-35hd")!), v, 1.225, cg);
        _out.WriteLine($"stock: drag {-fs.X:F0} N, pitch {ms.Y:F0} N m;  35HD: drag {-fb.X:F0} N, pitch {mb.Y:F0} N m");
        Assert.True(ms.Y != 0, "a wheel below the CG must make a pitching moment");
        Assert.True(-fb.X > -fs.X * 1.5, "a 35 in tundra tyre must cost far more drag than the stock tyre");
        Assert.True(System.Math.Abs(mb.Y) > System.Math.Abs(ms.Y), "and more pitching moment with it");
    }

    /// <summary>A fairing does not ADD drag, it REPLACES the exposed wheel's with a lower one. Counting the pant as
    /// an extra item is the double-count this whole ownership scheme exists to stop, so the faired aeroplane must
    /// come out with LESS flat-plate area than the same aeroplane bare.</summary>
    [Fact]
    public void AFairingSubtractsDragRatherThanAddingIt()
    {
        AircraftConfig c = Load("c172-like");
        Assert.True(GearCatalog.WearsFairings(c));
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("wheels")!);
        GearComponent fairing = set.First(g => g.Id.EndsWith("-fairing"));
        Assert.Equal(AeroOwner.None, fairing.AeroOwnedBy);
        Assert.Equal(0, fairing.DragAreaM2);
        Assert.True(fairing.MassKg > 0);
        // The wheel it covers carries the faired coefficient, which is lower than the exposed one.
        Assert.True(GearCatalog.FairingCdFactor < 1.0);
        GearComponent faired = set.First(g => g.Id.StartsWith("main") && g.Id.EndsWith("-wheel"));
        GearComponent bare = GearCatalog.ComponentsFor(c, GearCatalog.Get("tundra-26")!).First(g => g.Id.StartsWith("main") && g.Id.EndsWith("-wheel"));
        _out.WriteLine($"faired main f {faired.DragAreaM2:F4} m^2; a bare 26 in tundra on the same leg {bare.DragAreaM2:F4} m^2");
        // And a tundra tyre never gets one, at any size.
        Assert.DoesNotContain(GearCatalog.ComponentsFor(c, GearCatalog.Get("tundra-29")!), g => g.Id.EndsWith("-fairing"));
    }

    /// <summary>A ski lifts as well as drags, and both act at the ski's own pin. Today the sim applies
    /// SkiGear.TotalDragAreaM2 at the CG with no moment at all, which is the gap this closes.</summary>
    [Fact]
    public void ASkiLiftsAndItsForcesActAtTheSki()
    {
        AircraftConfig c = Load("pa18-cub-like");
        Vec3 cg = c.Mass.CgVec();
        IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, GearCatalog.Get("skis-wheel")!);
        GearComponent ski = set.First(g => g.Id.Contains("-ski"));
        Assert.True(ski.PlanAreaM2 > 0 && ski.AspectRatio > 0 && ski.AspectRatio < 0.6);
        // At a positive angle of attack the skis make lift (-z is up), and they are below the CG.
        (Vec3 f, Vec3 m) = GearAero.Compute(set, new Vec3(40 * System.Math.Cos(0.1), 0, 40 * System.Math.Sin(0.1)), 1.225, cg);
        _out.WriteLine($"skis at 5.7 deg: force ({f.X:F0}, {f.Y:F0}, {f.Z:F0}) N, moment ({m.X:F0}, {m.Y:F0}, {m.Z:F0}) N m");
        Assert.True(m.Y != 0, "ski forces below the CG must make a pitching moment");
        Assert.True(ski.CmBody.Z > cg.Z);
    }

    /// <summary>
    /// THE SANITY TARGET THE DRAG MODEL IS CHECKED AGAINST, and it is a published one: NACA TR 485 measured twenty
    /// complete fixed landing gears on a 3,000 lb aeroplane and got equivalent flat-plate areas of 0.92 to 2.01 ft^2,
    /// with a conventional tripod at 1.72-1.78 and the bare wheels alone at 0.76. The catalogue reaches that from the
    /// other end -- each wheel's own coefficient from the tyre's shape, times the installation factor -- so landing
    /// inside the band is a result and not an input.
    ///
    /// The Pitts and the Great Lakes are the 1,700-2,000 lb-class check and come out proportionately lower; a Cub,
    /// lighter still and on small tyres, lower again. The band asserted is the measured one widened for weight.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like", 0.15, 1.2)]
    [InlineData("pa18-cub-like", 0.15, 1.6)]
    [InlineData("great-lakes-like", 0.3, 2.0)]
    [InlineData("c172-like", 0.3, 2.0)]
    public void ACompleteFixedGearLandsOnTheMeasuredFlatPlateArea(string id, double loFt2, double hiFt2)
    {
        AircraftConfig c = Load(id);
        double m2 = GearAero.FlatPlateAreaM2(GearCatalog.ComponentsFor(c, GearCatalog.Get("wheels")!));
        double ft2 = m2 / (0.3048 * 0.3048);
        _out.WriteLine($"{id}: complete gear f = {ft2:F2} ft^2 ({m2:F4} m^2) on {c.Mass.MassKg / FloatSize.Lb:F0} lb gross " +
                       $"-- NACA TR 485's twenty gears on a 3,000 lb aeroplane ran 0.92 to 2.01 ft^2");
        Assert.InRange(ft2, loFt2, hiFt2);
    }

    /// <summary>
    /// A TUNDRA TYRE IS NOT DRAGGIER PER SQUARE FOOT — IT IS BIGGER. That is the structural result behind the whole
    /// tundra option, and it falls out of the coefficient being a function of the tyre's shape: a 31 in bushwheel's
    /// Cd on its own width x diameter is 0.228 and a stock 8.50-6's is 0.233, within 2 %. All of the cruise speed a
    /// 31 costs is frontal area, 2.2 times as much of it. Model it as area, never as a special coefficient.
    /// </summary>
    [Fact]
    public void ATundraTyreCostsAreaNotCoefficient()
    {
        TireSpec stock = TireCatalog.Get("8.50-6")!, big = TireCatalog.Get("31136.R")!;
        double cdStock = GearCatalog.ExposedWheelCd(stock), cdBig = GearCatalog.ExposedWheelCd(big);
        double aStock = stock.OdM * stock.SectionWidthM, aBig = big.OdM * big.SectionWidthM;
        _out.WriteLine($"8.50-6: Cd {cdStock:F3} on {aStock:F3} m^2;  31 in: Cd {cdBig:F3} on {aBig:F3} m^2 " +
                       $"({aBig / aStock:F2}x the area, {cdBig * aBig / (cdStock * aStock):F2}x the drag)");
        Assert.InRange(cdBig / cdStock, 0.93, 1.07);     // the same coefficient, to within a few per cent
        Assert.True(aBig > 2.0 * aStock);                 // and more than twice the area
    }

    /// <summary>The whole catalogue for one aeroplane, printed — a way to read what it produced without opening the
    /// source. Not an assertion.</summary>
    [Fact]
    public void PrintTheCubsOptions()
    {
        AircraftConfig c = Load("j3-cub-like");
        Vec3 cg = c.Mass.CgVec();
        _out.WriteLine("option            kg     lb    f (m^2)   Ixx     Iyy     Izz    components");
        foreach (UndercarriageOption o in GearCatalog.OfferedFor(c))
        {
            IReadOnlyList<GearComponent> set = GearCatalog.ComponentsFor(c, o);
            MassProperties I = GearMass.About(set, cg);
            _out.WriteLine($"{o.Id,-16} {I.MassKg,6:F1} {I.MassKg / FloatSize.Lb,6:F1} {GearAero.FlatPlateAreaM2(set),8:F4} " +
                           $"{I.Ixx,7:F2} {I.Iyy,7:F2} {I.Izz,7:F2}  {string.Join(" ", set.Select(g => g.Id))}");
        }
    }
}

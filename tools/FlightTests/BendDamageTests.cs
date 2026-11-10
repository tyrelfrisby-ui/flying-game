using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05: "The wing hit relatively slowly and immediately snapped off … the airplanes are all like porcelain dolls
/// … make them more resilient in the sense that they stay together but still use realistic damage thresholds for bending
/// surfaces and model the bent surfaces reduced strength and aerodynamic impacts if it continues flying."
///
/// What it was: a J-3 wing panel came off the first step its tip's contact moment passed the ultimate (a damped spring
/// rings far past it for one step), and a bent one snapped at 2 × its 1 g moment, a fifth of its ultimate. Now the panel's
/// station is two plastic hinges (<see cref="PlasticHinge"/>, <see cref="WingStructure"/>) that the ground cannot push past,
/// and a panel comes off only when a hinge has turned through its construction's rupture rotation.
/// Expected values are literals worked by hand from <see cref="MemberMaterials"/>' derivation.
/// </summary>
[Collection("WorldTerrainActive")]
public class BendDamageTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public BendDamageTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }
    public void Dispose() { WorldTerrain.Active = null; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static Quat Roll(double rad) => new(Math.Sin(rad / 2), 0, 0, Math.Cos(rad / 2));
    private const double Deg = Math.PI / 180.0;

    private static WingPanel Panel(Aircraft ac, AirframeComponent c) => ac.Panels.First(p => p.Component == c);

    /// <summary>The J-3 in its three-point attitude rolled right until its right wingtip is the lowest point, 3 cm off
    /// the ground (17.5°, the gear on the ground nearly), at least <paramref name="minBankDeg"/>; moving at
    /// <paramref name="fwdMs"/> and sinking at <paramref name="sinkMs"/> (world), rolling so the tip comes down at
    /// <paramref name="tipMs"/>. Flown for 4 s hands off at idle.</summary>
    private (Aircraft ac, List<ContactEvent> ev, double maxCarriedNm) RightTipStrike(double fwdMs, double tipMs, double minBankDeg = 0, double sinkMs = 0, Aircraft? again = null)
    {
        AircraftConfig c = again?.Config ?? Load("j3-cub-like");
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        Quat att = rest.Attitude; double low = 0;
        List<ContactPoint> pts = (again?.ContactPoints ?? AirframeContact.BuildPoints(c)).Where(p => !p.PropDisc).ToList();
        for (double ph = 0; ph < 45; ph += 0.25)
        {
            att = Quat.Multiply(Roll(ph * Deg), rest.Attitude);
            ContactPoint lowest = pts.OrderBy(p => att.Rotate(p.Body - c.Mass.CgVec()).Z).Last();
            low = -att.Rotate(lowest.Body - c.Mass.CgVec()).Z;
            if (lowest.Name.StartsWith("tip:wing") && ph >= minBankDeg) break;
        }
        var state = new RigidBodyState(new Vec3(0, 0, low - 0.03), att, att.Conjugate().Rotate(new Vec3(fwdMs, 0, sinkMs)), att.Conjugate().Rotate(new Vec3(tipMs / 5.4, 0, 0)));
        Aircraft ac = again ?? new Aircraft(c, state, ControlDeflections.Neutral);
        if (again is not null) ac.State = state;
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        double carried = 0;
        WingPanel w = Panel(ac, AirframeComponent.WingTipRight);
        for (double t = 0; t < 4; t += 0.005)
        {
            sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0.0));
            double m = 0;
            foreach (ContactPoint p in ac.ContactPoints) if (p.Panel >= 0 && p.PanelRef == w) m += w.MomentUp(p.Body, p.ForceBody);
            carried = Math.Max(carried, Math.Abs(m));
        }
        ac.Contact -= ev.Add;
        _o.WriteLine($"strike fwd {fwdMs} tip {tipMs} sink {sinkMs}: {w.State}, bend {w.BendRad / Deg:F2}°, twist {w.TwistRad / Deg:F2}°, plastic path {w.Bending!.PathRad:F3} / {w.Twisting!.PathRad:F3} rad, " +
                     $"work {w.Bending.WorkJ:F0} + {w.Twisting.WorkJ:F0} J, capacity {w.Bending.Capacity:F0} / {w.Twisting.Capacity:F0} N·m; " +
                     $"demanded {w.PeakContactNm:F0}, carried {carried:F0} (yield {w.LimitNm:F0}, ultimate {w.UltimateNm:F0}); lost {string.Join(",", ac.LostComponents)}");
        foreach (ContactEvent e in ev.Where(e => e.Kind is ContactEventKind.PanelBent or ContactEventKind.PanelBroken or ContactEventKind.MemberBent)) _o.WriteLine("  " + e);
        return (ac, ev, carried);
    }

    // ---- the law ------------------------------------------------------------------------------------------------------

    [Fact]
    public void HingeBackbonesAreTheMaterialsOwn()
    {
        // Wood/fabric (spruce): peak 2 × (0.83 − 0.48) % = 0.007 rad, 35 % by 0.05, fabric tears at 2 × 20 % = 0.40.
        var wood = new PlasticHinge(3000, 6000, MemberMaterials.Law(WingConstruction.WoodFabric));
        Assert.Equal(3000, wood.CapacityAt(0), 6);
        Assert.Equal(4500, wood.CapacityAt(0.0035), 6);
        Assert.Equal(6000, wood.CapacityAt(0.007), 6);
        Assert.Equal(2100, wood.CapacityAt(0.05), 6);
        Assert.Equal(2100, wood.CapacityAt(0.30), 6);
        Assert.Equal(0, wood.CapacityAt(0.40), 6);
        // Aluminium: peak at 0.02 (local buckling at 1.5 %), folds to half by 0.10, the tension flange tears at 0.36.
        var al = new PlasticHinge(4000, 6000, MemberMaterials.Law(WingConstruction.AluminumSkin));
        Assert.Equal(6000, al.CapacityAt(0.02), 6);
        Assert.Equal(4500, al.CapacityAt(0.06), 6);
        Assert.Equal(3000, al.CapacityAt(0.10), 6);
        Assert.Equal(0, al.CapacityAt(0.36), 6);
        // Composite: brittle — 15 % by 0.02, gone at 0.10.
        var comp = new PlasticHinge(4000, 6000, MemberMaterials.Law(WingConstruction.Composite));
        Assert.Equal(900, comp.CapacityAt(0.02), 6);
        Assert.Equal(0, comp.CapacityAt(0.10), 6);
        // The fleet: the J-3's spruce, the Super Cub's aluminium spars, the 2-33's metal wing, the Extra's carbon.
        Assert.Equal(WingConstruction.WoodFabric, MemberMaterials.Of(Load("j3-cub-like")));
        Assert.Equal(WingConstruction.AluminumFabric, MemberMaterials.Of(Load("pa18-cub-like")));
        Assert.Equal(WingConstruction.AluminumSkin, MemberMaterials.Of(Load("glider-2-33-like")));
        Assert.Equal(WingConstruction.Composite, MemberMaterials.Of(Load("extra-300-like")));
        Assert.Equal(WingConstruction.WoodFabric, MemberMaterials.Of(Load("pitts-s2b-like")));    // steel-tube fuselage → wood wing
        Assert.Equal(WingConstruction.AluminumSkin, MemberMaterials.Of(Load("c172-like")));
        // Torsion: a fabric wing twists through its two spars, T_u = M_u·b/(2L); a stressed skin carries 1.28 M_u.
        Assert.Equal(6000 * 0.45 * 1.6 / (2 * 2.0), MemberMaterials.UltimateTorqueNm(WingConstruction.WoodFabric, 6000, 1.6, 2.0), 9);
        Assert.Equal(1.28 * 6000, MemberMaterials.UltimateTorqueNm(WingConstruction.AluminumSkin, 6000, 1.6, 2.0), 9);
    }

    [Fact]
    public void AHingeComesOffOnTheWorkItTakesNotOnAPeakOfLoad()
    {
        var law = MemberMaterials.Law(WingConstruction.WoodFabric);
        // Three times its ultimate, against a stiff push that lets go as it turns (1 MN·m/rad): it turns until the push
        // is down to the backbone — up the hardening to its peak (0.007 rad), then 5000/(1e6 − 90,698) = 0.0055 rad more
        // down the softening, 0.0125 rad in all — and holds.
        var stiff = new PlasticHinge(3000, 6000, law);
        double d = stiff.Load(18000, 1.0e6);
        Assert.False(stiff.Ruptured);
        Assert.InRange(d, 0.007, 0.02);
        // The same demand from a load that does not let go (the air): it runs down the backbone, a step at a time, and off.
        var air = new PlasticHinge(3000, 6000, law);
        int calls = 0;
        while (!air.Ruptured && calls < 100) { air.Load(18000, 0.0, 0.1); calls++; }
        Assert.True(air.Ruptured);
        Assert.InRange(calls, 4, 6);
        // Its work is the area under the backbone: (3000+6000)/2·0.007 + (6000+2100)/2·0.043 + 2100·0.35 = 940.65 J.
        Assert.Equal(940.65, air.WorkJ, 6);
    }

    [Fact]
    public void RepeatedLoadsWeakenABentMember()
    {
        // A cracked spar loaded again and again in both senses, each time a little past what it has left: every yield walks
        // it further down its backbone, so each load finds it weaker than the one before, and in the end it lets go.
        var h = new PlasticHinge(3000, 6000, MemberMaterials.Law(WingConstruction.AluminumSkin));
        h.Impose(0.03);   // bent past its peak
        double before = h.Capacity;
        var caps = new List<double> { before };
        int n = 0;
        while (!h.Ruptured && n < 2000)
        {
            h.Load((n % 2 == 0 ? 1 : -1) * (h.Capacity + 300.0), 2.0e5);
            caps.Add(h.Capacity); n++;
        }
        _o.WriteLine($"capacity {string.Join(" → ", caps.Take(12).Select(c => c.ToString("F0")))} …; ruptured after {n} loads");
        Assert.True(h.Ruptured);
        for (int i = 1; i < caps.Count; i++) Assert.True(caps[i] <= caps[i - 1] + 1e-9, $"load {i} made it stronger");
        Assert.True(caps[1] < before);
        Assert.True(n > 20, "a load just past what is left takes a little each time; it should take many of them");
    }

    // ---- the J-3 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void J3SlowWingtipStrikeBendsAndStaysAttached()
    {
        // Rolling at 20 m/s, the right tip coming down at 5 m/s: Ty's crosswind wing-strike.
        (Aircraft ac, List<ContactEvent> ev, double carried) = RightTipStrike(20, 5);
        WingPanel w = Panel(ac, AirframeComponent.WingTipRight);
        Assert.False(ac.IsLost(AirframeComponent.WingTipRight));
        Assert.False(ac.IsLost(AirframeComponent.AileronRight));
        Assert.DoesNotContain(ev, e => e.Kind == ContactEventKind.PanelBroken);
        Assert.Equal(PanelState.Bent, w.State);
        Assert.Contains(ev, e => e.Kind == ContactEventKind.PanelBent && e.Component == AirframeComponent.WingTipRight);
        Assert.True(Math.Abs(w.TwistRad) > 0.5 * Deg || Math.Abs(w.BendRad) > 0.5 * Deg, "the strike left a set");
        // The old rule broke it: the ground asked the station for more than its ultimate ...
        Assert.True(w.PeakContactNm > w.UltimateNm, "the strike was past the old snap threshold");
        // ... and could only push as hard as the hinge carried.
        Assert.True(carried <= 1.02 * w.UltimateNm, $"the tip was pushed with {carried:F0} N·m against an ultimate of {w.UltimateNm:F0}");
        Assert.False(w.Bending!.Ruptured || w.Twisting!.Ruptured);
    }

    [Fact]
    public void HighEnergyImpactStillSeparatesTheWing()
    {
        // A cartwheel: 30 m/s, sinking at 6 m/s, the tip coming down at 15 m/s.
        (Aircraft ac, List<ContactEvent> ev, _) = RightTipStrike(30, 15, 0, 6);
        WingPanel w = Panel(ac, AirframeComponent.WingTipRight);
        Assert.True(ac.IsLost(AirframeComponent.WingTipRight));
        Assert.Equal(PanelState.Broken, w.State);
        Assert.Contains(ev, e => e.Kind == ContactEventKind.PanelBroken && e.Component == AirframeComponent.WingTipRight);
        Assert.True(w.Bending!.Ruptured || w.Twisting!.Ruptured);
        Assert.True(w.Bending.WorkJ + w.Twisting.WorkJ > 300, "it went only after absorbing a good deal of work");
        Assert.False(ac.IsLost(AirframeComponent.WingTipLeft));
        // Dropped hard onto the tip at speed (35 m/s, 25° of bank, sinking 6 m/s): the panel folds up, then goes.
        (Aircraft ac2, _, _) = RightTipStrike(35, 5, 25, 6);
        Assert.True(ac2.IsLost(AirframeComponent.WingTipRight));
        Assert.True(Panel(ac2, AirframeComponent.WingTipRight).Bending!.PathRad > 0.2, "it bent a long way before it went");
    }

    [Fact]
    public void ABentWingRollsAndDragsInFlight()
    {
        // The panel as a 10 m/s tip strike leaves it (bent ~9°, twisted, cracked), flown at 30 m/s.
        (Aircraft ac, _, _) = RightTipStrike(25, 10, 0, 3);
        WingPanel w = Panel(ac, AirframeComponent.WingTipRight);
        Assert.False(ac.IsLost(AirframeComponent.WingTipRight));
        AircraftConfig c = ac.Config;
        var tables = Aircraft.BuildAirfoilTables(c);
        int n = c.Surfaces.Sum(s => s.Strips.Count);
        StripFlowState damaged = ac.FlowStateForTests;
        (Vec3 Force, Vec3 Moment) Aero(bool bent, double aileron)
        {
            var fs = new StripFlowState(); fs.EnsureSize(n);
            if (bent) { fs.BendRad = damaged.BendRad; fs.BendOffset = damaged.BendOffset; fs.IncidenceDeltaRad = damaged.IncidenceDeltaRad; fs.LiftFactor = damaged.LiftFactor; fs.ExtraCd = damaged.ExtraCd; }
            return AeroModel.Compute(c, tables, new Vec3(30, 0, 1.5), Vec3.Zero, Vec3.Zero, 1.225, new ControlDeflections(aileron, 0, 0, 0), -1.0, fs, 0, 0, null, null);
        }
        var intact = Aero(false, 0); var bentA = Aero(true, 0);
        double dRoll = bentA.Moment.X - intact.Moment.X, dDrag = -(bentA.Force.X - intact.Force.X);
        double rollPerRad = (Aero(false, 0.1).Moment.X - Aero(false, -0.1).Moment.X) / 0.2;
        double trimDeg = -dRoll / rollPerRad / Deg;
        _o.WriteLine($"bend {w.BendRad / Deg:F1}°, twist {w.TwistRad / Deg:F1}°: rolling moment {dRoll:F0} N·m ({trimDeg:F1}° of aileron to hold), drag +{dDrag:F1} N on {-intact.Force.X:F1}");
        Assert.True(Math.Abs(trimDeg) > 0.5, "a bent, twisted panel needs aileron to hold the wings level");
        Assert.True(dDrag > 0, "and it drags");
    }

    [Fact]
    public void ASecondStrikeFindsTheBentPanelWeaker()
    {
        (Aircraft ac, _, double carried1) = RightTipStrike(20, 5);
        WingPanel w = Panel(ac, AirframeComponent.WingTipRight);
        double path1 = w.Bending!.PathRad + w.Twisting!.PathRad, cap1 = w.Bending.Capacity + w.Twisting.Capacity;
        (_, _, double carried2) = RightTipStrike(20, 5, 0, 0, ac);
        double path2 = w.Bending.PathRad + w.Twisting.PathRad, cap2 = w.Bending.Capacity + w.Twisting.Capacity;
        _o.WriteLine($"after one strike path {path1:F3} rad, capacity {cap1:F0}; after two {path2:F3}, {cap2:F0}; carried {carried1:F0} then {carried2:F0} N·m");
        Assert.True(path2 > path1, "the second strike bent it further");
        Assert.True(cap2 < cap1, "and left it weaker");
    }

    // ---- the other members --------------------------------------------------------------------------------------------

    [Fact]
    public void TailSurfacesMidSpanNacellesAndGearLegsCrushBeforeTheyBreak()
    {
        var j3 = Load("j3-cub-like");
        // Steel-tube tail: 0.20 m of crush before it parts, i.e. one arrival over 5·√(1 + 0.20/0.15) = 7.64 m/s.
        Assert.Equal(0.20, MemberMaterials.CrushCapacityM(AirframeComponent.TailVertical, j3), 9);
        Assert.Equal(0.08, MemberMaterials.CrushCapacityM(AirframeComponent.WingRightInner, j3), 9);   // spruce
        Assert.Equal(0.15, MemberMaterials.CrushCapacityM(AirframeComponent.GearLeft, j3), 9);
        Assert.Equal(0.15, MemberMaterials.CrushCapacityM(AirframeComponent.WingRightInner, Load("c172-like")), 9);
        Assert.Equal(0.0, MemberMaterials.CrushCapacityM(AirframeComponent.Propeller, j3), 9);
        // Dropped flat onto its gear at 5 m/s, past the 4 m/s break sink of its legs: they bend and stay on; at 7 m/s
        // (past 4·√2 = 5.66 in one arrival) they go.
        foreach ((double sink, bool kept) in new[] { (5.0, true), (7.0, false) })
        {
            RigidBodyState rest = LandingGear.RestingState(j3, 0, 0, 0);
            var ac = new Aircraft(j3, new RigidBodyState(rest.Position + new Vec3(0, 0, -0.05), rest.Attitude, rest.Attitude.Conjugate().Rotate(new Vec3(15, 0, sink)), Vec3.Zero), ControlDeflections.Neutral);
            var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
            var sim = new SimLoop(ac);
            for (double t = 0; t < 1.0; t += 0.005) sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0.0));
            _o.WriteLine($"gear drop {sink} m/s: lost {string.Join(",", ac.LostComponents)}; crush L {ac.MemberCrushM(AirframeComponent.GearLeft):F3} R {ac.MemberCrushM(AirframeComponent.GearRight):F3} m");
            bool legsOn = !ac.IsLost(AirframeComponent.GearLeft) && !ac.IsLost(AirframeComponent.GearRight);
            Assert.Equal(kept, legsOn);
            if (kept) Assert.Contains(ev, e => e.Kind == ContactEventKind.MemberBent && e.Component is AirframeComponent.GearLeft or AirframeComponent.GearRight);
        }
    }

    /// <summary>
    /// A BEND IS A KINK AT THE STATION, NOT A CURVE ALONG THE WING (Ty 2026-11-10: "The aileron fix on the bent wing
    /// fixed the outboard hinge but not the inboard hinge keep both hinges 'attached'").
    ///
    /// Both halves of WingPanel.Deform return a point UNCHANGED inboard of BreakY and move it RIGIDLY outboard, so the
    /// drawn hinge line is straight inboard of the station, kinks there, and is a straight rotated line beyond it. That
    /// is why ONE rigid aileron carrier cannot hold both ends of an aileron that spans the station: the motion is rigid
    /// and the line it must follow is not. Anything drawing a surface across the station has to split it there — pinned
    /// here so a later repair cannot "fix" it by smearing the bend along the span instead, which would un-fix the wing.
    ///
    /// STILL TRUE OF A STRIKE, AND ONLY OF A STRIKE (Ty 2026-11-10, later the same day: "Make bent wings from
    /// symmetrical aerodynamic load curve from the root instead of pivot at a strut or root. Wing strike bends are
    /// still sharp mid span."). The two shapes now live apart: <see cref="WingPanel.Deform"/> is the strike's kink and
    /// is what this pins, and the AERODYNAMIC bend is a curve from the root that deliberately DOES move the wing
    /// inboard of the station (<see cref="WingPanel.Curve"/>; StaggeredWingFailureTests pins that half). The driver
    /// here is the kink angle passed to Deform rather than WingPanel.BendRad, because BendRad is now the angle a panel
    /// REPORTS — the hinge's set plus its wing's aerodynamic set, which the aileron limit reads — and not the input to
    /// any one shape.
    /// </summary>
    [Fact]
    public void ABendMovesNothingInboardOfTheStationAndMovesEverythingOutboardRigidly()
    {
        AircraftConfig j3 = Load("j3-cub-like");
        WingPanel p = WingStructure.Build(j3).First(q => q.Component == AirframeComponent.WingTipRight);
        double kink = 10.0 * Math.PI / 180.0;
        p.TwistRad = 0.0;                                  // the bend alone
        double y = p.BreakY;
        _o.WriteLine($"station {y:F3} m, strike bend {kink * 180 / Math.PI:F1}°");

        // Inboard of the station: identity, to the last bit. This is the wing the INBOARD hinge is attached to, and it
        // does not move — so a carrier that moves the whole aileron lifts that hinge off it.
        foreach (double ay in new[] { 0.0, 0.25 * y, 0.9 * y, y })
        {
            var q = new Vec3(0.3, ay, -0.2);
            Vec3 d = p.Deform(q, kink);
            Assert.Equal(q.X, d.X, 12);
            Assert.Equal(q.Y, d.Y, 12);
            Assert.Equal(q.Z, d.Z, 12);
        }

        // Outboard: moved, and RIGIDLY — two points keep the distance between them, so one carrier transform is exact
        // there and the OUTBOARD hinge really is attachable by one.
        var a = new Vec3(0.3, y + 0.10, -0.2);
        var b = new Vec3(0.5, y + 0.90, -0.1);
        Vec3 da = p.Deform(a, kink), db = p.Deform(b, kink);
        Assert.True((da - a).Length > 1e-4, "a point just outboard of the station must move");
        Assert.Equal((b - a).Length, (db - da).Length, 9);

        // And the kink is real: a millimetre inboard of the station stays put, a millimetre outboard does not.
        Vec3 just = p.Deform(new Vec3(0.3, y - 0.001, -0.2), kink);
        Vec3 past = p.Deform(new Vec3(0.3, y + 0.001, -0.2), kink);
        Assert.Equal(-0.2, just.Z, 12);
        Assert.True(Math.Abs(past.Z - (-0.2)) > 1e-5, $"just outboard should have lifted, z {past.Z:F6}");
    }
}

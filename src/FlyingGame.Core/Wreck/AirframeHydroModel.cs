using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>Which atoms a broken-off component takes with it. Children break first (Aircraft.LoseComponent raises
/// an inner panel's outer panel and aileron before the panel itself), so a component claims only what is still on.</summary>
public static class WreckAtoms
{
    public static WreckAtom[] Of(AirframeComponent c) => c switch
    {
        AirframeComponent.WingLeft or AirframeComponent.WingLeftInner => new[] { WreckAtom.WingLInner, WreckAtom.WingLOuter, WreckAtom.AileronL },
        AirframeComponent.WingRight or AirframeComponent.WingRightInner => new[] { WreckAtom.WingRInner, WreckAtom.WingROuter, WreckAtom.AileronR },
        AirframeComponent.WingLeftOuter => new[] { WreckAtom.WingLOuter, WreckAtom.AileronL },
        AirframeComponent.WingRightOuter => new[] { WreckAtom.WingROuter, WreckAtom.AileronR },
        // A lower aileron shares its side's atom: the wreck model cuts a biplane's ailerons as one box per side, so
        // both surfaces on a side float and flood as that box. Telling them apart in the WRECK would need the model
        // to carry two boxes a side, which is its own change and is not what the per-surface identity is for.
        AirframeComponent.AileronLeft or AirframeComponent.AileronLowerLeft => new[] { WreckAtom.AileronL },
        AirframeComponent.AileronRight or AirframeComponent.AileronLowerRight => new[] { WreckAtom.AileronR },
        AirframeComponent.TailHorizontal => new[] { WreckAtom.HStab, WreckAtom.ElevatorL, WreckAtom.ElevatorR },
        AirframeComponent.TailVertical => new[] { WreckAtom.VStab, WreckAtom.Rudder },
        AirframeComponent.TailBoom => new[] { WreckAtom.TailCone, WreckAtom.HStab, WreckAtom.ElevatorL, WreckAtom.ElevatorR, WreckAtom.VStab, WreckAtom.Rudder, WreckAtom.GearTail },
        AirframeComponent.ElevatorLeft => new[] { WreckAtom.ElevatorL },
        AirframeComponent.ElevatorRight => new[] { WreckAtom.ElevatorR },
        AirframeComponent.Rudder => new[] { WreckAtom.Rudder },
        AirframeComponent.Nose => new[] { WreckAtom.Nose, WreckAtom.GearNose },
        AirframeComponent.GearLeft => new[] { WreckAtom.GearLeft },
        AirframeComponent.GearRight => new[] { WreckAtom.GearRight },
        AirframeComponent.GearNose => new[] { WreckAtom.GearNose },
        AirframeComponent.GearTail => new[] { WreckAtom.GearTail },
        AirframeComponent.NacelleLeft => new[] { WreckAtom.NacelleL },
        AirframeComponent.NacelleRight => new[] { WreckAtom.NacelleR },
        _ => System.Array.Empty<WreckAtom>(),   // Fuselage, Cabin (it is what stays), Propeller (bent, stays on)
    };

    public static bool IsGear(WreckAtom a) => a is WreckAtom.GearLeft or WreckAtom.GearRight or WreckAtom.GearNose or WreckAtom.GearTail;
}

/// <summary>Where two atoms join. When one side leaves, the joint is a torn section open on both sides (an opening
/// of <see cref="AreaM2"/> spanning <see cref="HalfHeight"/> above and below <see cref="At"/>). A one-sided cut
/// opens only the named child element (a fuel feed line pulled out of a tank).</summary>
public sealed class Cut
{
    public WreckAtom Child, Parent;
    public Vec3 At;
    public double AreaM2, HalfHeight;
    public string Why = "";
    public string? ChildElement;
    public bool OneSided;
}

/// <summary>
/// A structural joint that a water impact can break (AirframeFlooding): the component it holds on, the atoms beyond
/// it, where it is, its long axis (x for the fuselage, y for a wing), and the bending moment it was built to carry.
///
/// STRENGTH. Each joint is taken to be exactly as strong as its design case requires at the type's ultimate load
/// factor n_ult = limits.gMax × limits.ultimateFactor (the factor the game breaks the wings at; 14 CFR 23.303 is
/// 1.5): a fuselage joint carries the inertia of everything beyond it, n_ult·g·Σ m·|Δx| (the tail load that also sizes
/// a real aft fuselage is left out, so the aft fuselage is if anything weaker than a real one); a wing joint carries
/// the lift outboard of it at n_ult less the inertia relief of the wing's own mass, the lift spread spanwise by
/// Schrenk's approximation (NACA TM 948, 1940: the mean of the planform and the elliptic distributions). Along the
/// fuselage a joint holds everything beyond it at the emergency-landing forward ultimate inertia factor, 9.0 g
/// (14 CFR 23.561(b)(3), pre-amendment 23-64) — what a nose-first arrival loads it with. Empennage and control-surface
/// joints, and a wing's chordwise bending, are not checked.
/// </summary>
public sealed class Joint
{
    public AirframeComponent Component;
    public WreckAtom[] Atoms = System.Array.Empty<WreckAtom>();
    public Vec3 At, Axis;
    public double UltimateNm;
    /// <summary>Ultimate load along the joint's axis (fuselage joints), N; 0 = not checked.</summary>
    public double UltimateAxialN;
    public string Why = "";
    /// <summary>Largest bending moment it has carried in the water, N·m (probe / tests).</summary>
    public double PeakNm;
    /// <summary>Largest force along its axis, N.</summary>
    public double PeakAxialN;
}

/// <summary>
/// The airframe as closed volumes, from the aircraft config alone. Written down so it can be checked:
///
/// GEOMETRY. Fuselage: the config's body radius r and length L (fuselage.crossflow), laid out exactly as
/// AirframeContact.BuildPoints lays out the hard points (tail at the aftmost trailing edge − 0.2 m, nose L ahead of
/// it), and cut into nose / cabin / tail cone at AirframeContact.FuselageStations — the same stations the break-up
/// and the visual mesh cuts use. Nose and cabin are cylinders of radius r; the tail cone a cone of base r (as a box,
/// the equal-volume cylinder of radius r/√3). Lifting surfaces: every strip's chord, width and position from the
/// config, grouped by the component it belongs to (wing panels split at WingPanels.OuterFraction, ailerons,
/// stabiliser, elevators, fin, rudder); thickness from the airfoil name (NACA digits; Clark Y 11.7 %; otherwise
/// 12 %, the NACA 0012 tail section); volume 0.685·t·c per unit span (<see cref="WaterPhysics.AirfoilAreaFactor"/>).
///
/// MASS. Raymer's general-aviation empty-weight buildup (<see cref="WaterPhysics"/>, Table 15.2): wing 12.2 kg/m^2
/// of exposed planform, tails 9.76 kg/m^2, fuselage 6.84 kg/m^2 of wetted area, gear 5.7 % and "all else" 10 % of
/// design gross (the config mass), installed engine 1.4 × dry (dry from power, <see cref="WaterPhysics.PistonEngineDryKg"/>).
/// The airframe items are then scaled together so the sum is the config's empty mass (AircraftLoad.EmptyMassKg) —
/// the config is authoritative on how heavy the aeroplane is; Raymer only says how it is shared out. Payload comes
/// from the loading model at its current values: occupants (ρ 1010) and baggage (neutral) in the cabin, fuel in
/// each tank (ρ 720), tanks sealed but for their vent line.
///
/// OPENINGS. Intact: door-seal gaps on the cabin, drain holes in every closed surface, cooling inlets and exit on a
/// cowling (dimensions in <see cref="WaterPhysics"/>, all marked ASSUMED). Broken: every joint that tears opens its
/// full section on both pieces (<see cref="Cuts"/>).
/// </summary>
public sealed class AirframeHydroModel
{
    public readonly List<HydroElement> Elements = new();
    public readonly List<Cut> Cuts = new();
    public readonly List<Joint> Joints = new();
    /// <summary>Ultimate load factor the joints are sized for (limits.gMax × limits.ultimateFactor).</summary>
    public double UltimateLoadFactor { get; private set; }
    public double EmptyMassKg { get; private set; }
    public double DesignGrossKg { get; private set; }
    public double EngineDryKg { get; private set; }
    /// <summary>Factor applied to Raymer's airframe items to land on the config's empty mass.</summary>
    public double StructureScale { get; private set; }
    public readonly List<string> Derivation = new();

    public double TotalMassKg { get { double m = 0; foreach (HydroElement e in Elements) m += e.MassKg; return m; } }

    /// <summary>What the wing structure's Raymer mass was multiplied by so the model's roll inertia fits inside the
    /// config's (1 = untouched). See <see cref="CalibrateWingMass"/>.</summary>
    public double WingMassFactor { get; private set; } = 1.0;

    /// <summary>
    /// The model's boxes must fit inside the config's published inertia (2026-11-03, momentum at separation): a piece that
    /// breaks off takes exactly its own share of mass and inertia, so a model that carries more inertia than the aeroplane
    /// has would leave the rest with less than nothing. Raymer's per-area weights are a metal GA aeroplane's; on a fabric
    /// wing they are about twice the real thing (a PA-18 wing is ~45 kg; the buildup gave its outer panel and aileron alone
    /// 57 kg), and its boxes carried 1.9× the config's roll inertia (PA-18 Ixx 2,733 against 1,450; C172 2,979 against
    /// 1,285; J-3 1,685 against 1,003). Fore and aft, the tail's 9.8 kg/m² and the fuselage's wetted-area weight put the
    /// Extra's and the Pitts's pitch inertia past theirs too.
    ///
    /// So, axis by axis, the STRUCTURE (never an engine, fuel, a tank, the payload or the cabin) is scaled down until the
    /// boxes' inertia about the CG is no more than 90 % of the config's — the rest is the spread of mass inside each box
    /// that boxes cannot carry: the wing structure for roll; the tail and tail-cone structure for pitch; both
    /// together for yaw, if it is still over. What is shed goes into the cabin structure at the CG. Mass and CG are
    /// unchanged; the volumes, and so the floating, are too. Never below half of Raymer (a quarter left the 172 an
    /// empennage of 15 kg; a real one is ~60): what cannot be met then is written to the derivation, and a break that
    /// would take more than the aeroplane has falls back to Aircraft.ShedMass's logged floor.
    /// </summary>
    private static void CalibrateWingMass(AirframeHydroModel m, AircraftConfig c)
    {
        Vec3 cg = c.Mass.CgVec();
        HydroElement? cabin = m.Elements.FirstOrDefault(e => e.Atom == WreckAtom.Cabin && Structure(e));
        if (cabin is null) return;
        static bool Structure(HydroElement e) => !e.Name.StartsWith("tank-") && !e.Name.StartsWith("engine") && !e.Name.StartsWith("fuel");
        static bool Wing(HydroElement e) => Structure(e) && e.Atom is WreckAtom.WingLInner or WreckAtom.WingLOuter or WreckAtom.WingRInner or WreckAtom.WingROuter
            or WreckAtom.AileronL or WreckAtom.AileronR;
        // Not the nose: its structure carries the installed engine's accessories (RaymerInstalledEngineFactor), real mass.
        static bool ForeAft(HydroElement e) => Structure(e) && e.Atom is WreckAtom.TailCone or WreckAtom.HStab or WreckAtom.VStab
            or WreckAtom.ElevatorL or WreckAtom.ElevatorR or WreckAtom.Rudder or WreckAtom.GearTail;
        double I(HydroElement e, int axis)
        {
            Vec3 r = e.Centre - cg, h = e.Half;
            return e.MassKg * axis switch
            {
                0 => (h.Y * h.Y + h.Z * h.Z) / 3 + r.Y * r.Y + r.Z * r.Z,
                1 => (h.X * h.X + h.Z * h.Z) / 3 + r.X * r.X + r.Z * r.Z,
                _ => (h.X * h.X + h.Y * h.Y) / 3 + r.X * r.X + r.Y * r.Y,
            };
        }
        double[] cfgI = { c.Mass.Inertia.Ixx, c.Mass.Inertia.Iyy, c.Mass.Inertia.Izz };
        string[] axisName = { "Ixx", "Iyy", "Izz" };
        void Fit(int axis, System.Func<HydroElement, bool> group, System.Action<double> record)
        {
            double target = 0.9 * cfgI[axis];
            if (target <= 0) return;
            double ig = 0, io = 0;
            foreach (HydroElement e in m.Elements) { if (group(e)) ig += I(e, axis); else io += I(e, axis); }
            if (ig <= 0 || io + ig <= target) return;
            double f = System.Math.Clamp((target - io) / ig, 0.5, 1.0);
            double moved = 0;
            foreach (HydroElement e in m.Elements) if (group(e)) { moved += e.MassKg * (1 - f); e.MassKg *= f; }
            cabin.MassKg += moved;
            record(f);
            m.Derivation.Add($"{axisName[axis]}: structure x{f:F3} ({moved:F1} kg to the cabin): the boxes' {io + ig:F0} kg·m² against the config's {cfgI[axis]:F0}");
        }
        Fit(0, Wing, f => m.WingMassFactor *= f);
        Fit(1, ForeAft, _ => { });
        Fit(2, e => Wing(e) || ForeAft(e), f => m.WingMassFactor *= f);
        for (int axis = 0; axis < 3; axis++)
        {
            double sum = 0; foreach (HydroElement e in m.Elements) sum += I(e, axis);
            if (sum > cfgI[axis]) m.Derivation.Add($"{axisName[axis]}: the boxes still carry {sum:F0} kg·m² against the config's {cfgI[axis]:F0} (engine, fuel or payload alone)");
        }
    }

    public static double ThicknessRatio(string? airfoil)
    {
        string a = (airfoil ?? "").ToLowerInvariant();
        if (a.Contains("clarky") || a.Contains("clark-y") || a.Contains("clark y")) return 0.117;
        int i = a.IndexOf("naca", System.StringComparison.Ordinal);
        if (i >= 0)
        {
            string digits = new string(a.Substring(i + 4).TakeWhile(ch => char.IsDigit(ch)).ToArray());
            if (digits.Length >= 4 && int.TryParse(digits.Substring(digits.Length - 2), out int tt) && tt > 0) return tt / 100.0;
        }
        return 0.12;
    }

    private sealed class Group
    {
        public WreckAtom Atom; public string Name = "";
        public double Area, AreaX, AreaZ, Vol, YMin = double.MaxValue, YMax = double.MinValue, ChordArea, TcArea;
        public bool Vertical, Exposed = true;
        public double RootChord, RootY = double.MaxValue, RootTc, RootX, RootZ;
        public double TipY, TipChord, TipTc, TipX, TipZ;
    }

    public static AirframeHydroModel Build(AircraftConfig c, AircraftLoad? load = null)
    {
        var m = new AirframeHydroModel();
        load ??= new AircraftLoad(c);
        double rBody = c.Fuselage.Crossflow?.BodyRadiusM > 0 ? c.Fuselage.Crossflow.BodyRadiusM : 0.5;
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double minTe = double.MaxValue;
        foreach (SurfaceConfig sf in c.Surfaces) foreach (StripConfig st in sf.Strips) minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
        if (minTe == double.MaxValue) minTe = -len * 0.6;
        double tailX = minTe - 0.2, noseX = tailX + len;
        // Where the nose really is. fuselage.crossflow.lengthM is the aerodynamic crossflow length and on several types
        // falls short of the airframe: laid aft-to-fore from the tail it put the 172's nose at x = -0.43 m, behind its
        // own wing leading edge (a real 172 is 8.3 m long). Two config facts locate it better, whichever reaches further:
        // the smoke system's measured exhaust outlet plus its aftOfNoseM (172: 1.282 + 1.01 = 2.29 m), and the nosewheel,
        // which sits under the cowling (its contact point + 0.3 m of cowling ahead of it).
        if (c.Smoke is { } sm && sm.AftOfNoseM > 0 && sm.TryOutlet(out Vec3 outlet)) noseX = System.Math.Max(noseX, outlet.X + sm.AftOfNoseM);
        foreach (GearConfig g in c.Gear)
            if (!g.IsTailwheel && System.Math.Abs(g.Pos[1]) < 0.05 && g.GearType != "float-keel") noseX = System.Math.Max(noseX, g.Pos[0] + 0.3);
        (double noseCut, double tailCut) = AirframeContact.FuselageStations(c);
        noseCut = System.Math.Clamp(noseCut, tailX + 0.6, noseX - 0.2);
        tailCut = System.Math.Clamp(tailCut, tailX + 0.2, noseCut - 0.4);
        double axisZ = c.Propulsion?.ThrustLineZ ?? 0.0;
        double semi = WingPanels.Semispan(c);
        double wdg = c.Mass.MassKg;

        // ---- lifting surfaces, grouped by atom ----
        var groups = new Dictionary<string, Group>();
        double centreWingVol = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool aileron = id.Contains("aileron");
            bool wing = id.Contains("wing") && !aileron;
            bool hstab = id == "hstab", elevator = id == "elevator";
            bool fin = id.Contains("vstab") || id.Contains("fin"), rudder = id.StartsWith("rudder");
            double tc = ThicknessRatio(sf.Strips.Count > 0 ? sf.Strips[0].Airfoil : null);
            foreach (StripConfig st in sf.Strips)
            {
                double y = st.Pos[1];
                double vol = WaterPhysics.AirfoilAreaFactor * tc * st.Chord * st.Area;
                WreckAtom atom;
                if (wing)
                {
                    AirframeComponent? panel = WingPanels.PanelOf(y, semi);
                    if (panel is null) { centreWingVol += vol; continue; }   // the centre section is inside the cabin
                    atom = panel switch
                    {
                        AirframeComponent.WingLeftOuter => WreckAtom.WingLOuter, AirframeComponent.WingRightOuter => WreckAtom.WingROuter,
                        AirframeComponent.WingLeftInner => WreckAtom.WingLInner, _ => WreckAtom.WingRInner,
                    };
                }
                else if (aileron) atom = y < 0 ? WreckAtom.AileronL : WreckAtom.AileronR;
                else if (hstab) atom = WreckAtom.HStab;
                else if (elevator) atom = y < 0 ? WreckAtom.ElevatorL : WreckAtom.ElevatorR;
                else if (rudder) atom = WreckAtom.Rudder;
                else if (fin) atom = WreckAtom.VStab;
                else { centreWingVol += vol; continue; }
                string key = atom + "|" + id + "|" + (atom == WreckAtom.HStab ? (y < 0 ? "L" : "R") : "");
                if (!groups.TryGetValue(key, out Group? gr))
                {
                    gr = new Group { Atom = atom, Name = sf.Id + (atom == WreckAtom.HStab ? (y < 0 ? "-L" : "-R") : ""), Vertical = fin || rudder };
                    groups[key] = gr;
                }
                double w = st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;
                double xMid = st.Pos[0] - 0.25 * st.Chord;   // strip pos is the quarter chord
                gr.Area += st.Area; gr.AreaX += st.Area * xMid; gr.AreaZ += st.Area * st.Pos[2]; gr.Vol += vol;
                gr.ChordArea += st.Area * st.Chord; gr.TcArea += st.Area * tc;
                double span = gr.Vertical ? -st.Pos[2] : y;   // fins run "up" (−z)
                gr.YMin = System.Math.Min(gr.YMin, (gr.Vertical ? st.Pos[2] : y) - w * 0.5);
                gr.YMax = System.Math.Max(gr.YMax, (gr.Vertical ? st.Pos[2] : y) + w * 0.5);
                double inboard = gr.Vertical ? System.Math.Abs(st.Pos[2]) : System.Math.Abs(y);
                if (inboard < gr.RootY) { gr.RootY = inboard; gr.RootChord = st.Chord; gr.RootTc = tc; gr.RootX = xMid; gr.RootZ = st.Pos[2]; }
                if (inboard >= gr.TipY) { gr.TipY = inboard; gr.TipChord = st.Chord; gr.TipTc = tc; gr.TipX = xMid; gr.TipZ = st.Pos[2]; }
                _ = span;
            }
        }

        // ---- mass buildup (Raymer Table 15.2), then scaled to the config's empty mass ----
        double nEng = 0, engDry = 0, propKg = 0;
        bool singleProp = c.Propulsion is { PropDiameterM: > 0 } && c.Engines.Count == 0;
        if (c.Propulsion is { PropDiameterM: > 0 } p)
        {
            nEng = System.Math.Max(1, c.Engines.Count);
            engDry = WaterPhysics.PistonEngineDryKg(p.MaxPowerW);
            // ONE PROPELLER WEIGHT FOR THE WHOLE MODEL: AirframeContact.PropMassKg, which prefers the config's own
            // published figure and otherwise inverts Ty's blade rule out of the authored propInertia.
            // This used the slender-blade inversion m = 3·I/R² until 2026-11-11, and that assumes the weight is
            // spread evenly along the blade. Ty's rule deliberately concentrates most of it in a hub that
            // contributes almost nothing to the polar moment, so the slender-blade figure came out about four times
            // light on every two-blade type in the fleet — 9.4 kg for the Pitts' 36.3 kg propeller, 1.7 kg for a
            // wooden Cub blade pair that ought to be near 7 — and the engine element it feeds was that much short.
            propKg = AirframeContact.PropMassKg(c);
        }
        double fusRaw(double wet) => WaterPhysics.RaymerFuselageKgPerM2 * wet;
        double lNose = noseX - noseCut, lCab = noseCut - tailCut, lTail = tailCut - tailX;
        double wetNose = 2 * System.Math.PI * rBody * lNose, wetCab = 2 * System.Math.PI * rBody * lCab;
        double wetTail = System.Math.PI * rBody * System.Math.Sqrt(rBody * rBody + lTail * lTail);
        var gearLegs = c.Gear.Where(g => g.GearType != "float-keel").ToList();
        int floatKeels = c.Gear.Count - gearLegs.Count;
        double raw = fusRaw(wetNose + wetCab + wetTail) + WaterPhysics.RaymerGearFraction * wdg + WaterPhysics.RaymerAllElseFraction * wdg;
        foreach (Group gr in groups.Values)
            raw += (gr.Atom is WreckAtom.HStab or WreckAtom.ElevatorL or WreckAtom.ElevatorR or WreckAtom.VStab or WreckAtom.Rudder
                ? WaterPhysics.RaymerTailKgPerM2 : WaterPhysics.RaymerWingKgPerM2) * gr.Area;
        double installed = nEng * (engDry * WaterPhysics.RaymerInstalledEngineFactor + propKg);
        double empty = load.EmptyMassKg > 0 ? load.EmptyMassKg : wdg;
        double k = raw > 0 ? (empty - installed) / raw : 1.0;
        if (k < 0.1) k = 0.1;   // an engine heavier than the whole empty aeroplane would be a broken config: keep some structure
        m.EmptyMassKg = empty; m.DesignGrossKg = wdg; m.EngineDryKg = engDry; m.StructureScale = k;
        m.Derivation.Add($"design gross {wdg:F0} kg (config), empty {empty:F0} kg (loading model), engines {nEng:F0} x {engDry:F1} kg dry + prop {propKg:F1} kg");
        m.Derivation.Add($"Raymer airframe items {raw:F0} kg raw, scaled x{k:F3} to land on the empty mass");
        m.Derivation.Add($"fuselage r {rBody:F2} m, nose {lNose:F2} m / cabin {lCab:F2} m / tail cone {lTail:F2} m (cuts at x {noseCut:F2}, {tailCut:F2})");

        double drain = WaterPhysics.CircleArea(WaterPhysics.DrainHoleDiaM);
        HydroElement Add(string name, WreckAtom atom, Vec3 centre, Vec3 half, double env, double massKg, double solidM3)
        {
            var e = new HydroElement(name, atom, centre, half, env, massKg, solidM3);
            m.Elements.Add(e);
            return e;
        }

        // ---- engine(s) ----
        double engVol = 0;
        if (singleProp)
        {
            double mE = engDry + propKg;
            engVol = engDry / WaterPhysics.RhoEngine + propKg / WaterPhysics.RhoAluminium;
            double side = System.Math.Cbrt(engVol);
            Add("engine", WreckAtom.Nose, new Vec3(noseCut + lNose * 0.5, 0, axisZ), new Vec3(side / 2, side / 2, side / 2), engVol, mE, engVol);
        }
        else if (nEng > 0)
        {
            foreach (EngineMount em in c.Engines)
            {
                double mE = engDry * WaterPhysics.RaymerInstalledEngineFactor + propKg;
                double v = engDry / WaterPhysics.RhoEngine + (mE - engDry) / WaterPhysics.RhoAluminium;
                double side = System.Math.Cbrt(v);
                Add("engine" + (em.Pos[1] < 0 ? "-L" : "-R"), em.Pos[1] < 0 ? WreckAtom.NacelleL : WreckAtom.NacelleR, em.PosVec(), new Vec3(side / 2, side / 2, side / 2), v, mE, v);
            }
        }

        // ---- fuselage sections ----
        double cyl(double l) => System.Math.PI * rBody * rBody * l;
        double extras = singleProp ? engDry * (WaterPhysics.RaymerInstalledEngineFactor - 1) : 0;
        double noseStruct = k * fusRaw(wetNose) + extras;
        HydroElement nose = Add("nose", WreckAtom.Nose, new Vec3(noseCut + lNose * 0.5, 0, axisZ), new Vec3(lNose * 0.5, rBody, rBody),
            System.Math.Max(0.01, cyl(lNose) - engVol), noseStruct, noseStruct / WaterPhysics.RhoAluminium);
        if (singleProp)
        {
            nose.Openings.Add(new Opening(new Vec3(noseX, 0, axisZ - 0.5 * rBody), WaterPhysics.CowlInletM2, "cowl cooling inlets"));
            nose.Openings.Add(new Opening(new Vec3(noseCut + 0.1, 0, axisZ + rBody), WaterPhysics.CowlExitM2, "cowl exit"));
        }
        else nose.AddDistributedOpening(2 * drain, "drain holes");

        double gearKgFloat = floatKeels > 0 ? k * WaterPhysics.RaymerGearFraction * wdg * floatKeels / System.Math.Max(1, c.Gear.Count) : 0;
        double cabStruct = k * (fusRaw(wetCab) + WaterPhysics.RaymerAllElseFraction * wdg) + gearKgFloat;
        double cabMass = cabStruct, cabSolid = cabStruct / WaterPhysics.RhoAluminium;
        double payload = 0;
        foreach (AircraftLoad.Slot s in load.Slots)
        {
            if (s.Kind == AircraftLoad.StationKind.Fuel || s.Kg <= 0) continue;
            double rho = s.Kind == AircraftLoad.StationKind.Seat ? WaterPhysics.RhoHuman : WaterPhysics.RhoBaggage;
            cabMass += s.Kg; cabSolid += s.Kg / rho; payload += s.Kg;
        }
        HydroElement cabin = Add("cabin", WreckAtom.Cabin, new Vec3((tailCut + noseCut) * 0.5, 0, 0), new Vec3(lCab * 0.5, rBody, rBody),
            cyl(lCab) + centreWingVol, cabMass, cabSolid);
        ConstructionConfig cons = c.Construction ?? new ConstructionConfig();
        bool fabric = cons.IsFabric;
        double fusSkinPa = fabric ? WaterPhysics.FabricSkinBurstPa(WaterPhysics.FabricPanelSpanM) : WaterPhysics.MetalSkinBurstPa(WaterPhysics.FuselageStringerPitchM);
        double doorSide = 1.6 * rBody, doorGap = WaterPhysics.DoorSealGapM * 4 * doorSide;
        if (cons.Cockpits.Count == 0)
        {
            // An enclosed cabin. Two doors, each a square 1.6·r on a side (a 172's door is about 0.95 m with r = 0.6 m),
            // sealed by a gap DoorSealGapM wide all round; each door edge spans the cabin side from sill to top. A crash
            // can burst a door off its latch (DoorLatchN over the door's area) or the windshield (acrylic across the
            // cabin, 2r): the windshield is the top half of the cabin's front face, the step between the cowling and
            // the roof, πr²/2 seen from ahead.
            cabin.AddSpanningOpening(new Vec3(cabin.Centre.X, -rBody, 0), doorGap, "left door seal");
            cabin.AddSpanningOpening(new Vec3(cabin.Centre.X, rBody, 0), doorGap, "right door seal");
            double doorPa = WaterPhysics.DoorLatchN / (doorSide * doorSide);
            foreach (int sg in new[] { -1, 1 })
            {
                var door = new Panel
                {
                    Why = (sg < 0 ? "left" : "right") + " door burst off its latch", At = new Vec3(cabin.Centre.X, sg * rBody, 0), Normal = new Vec3(0, sg, 0),
                    U = new Vec3(0.5 * doorSide, 0, 0), V = new Vec3(0, 0, 0.5 * doorSide), AreaM2 = doorSide * doorSide, FailPa = doorPa,
                };
                door.HoleAt.Add(new Vec3(cabin.Centre.X, sg * rBody, -0.5 * doorSide));
                door.HoleAt.Add(new Vec3(cabin.Centre.X, sg * rBody, 0.5 * doorSide));
                cabin.AddPanel(door);
            }
            var ws = new Panel
            {
                Why = "windshield broken", At = new Vec3(noseCut, 0, -0.5 * rBody), Normal = new Vec3(1, 0, 0),
                U = new Vec3(0, rBody, 0), V = new Vec3(0, 0, 0.5 * rBody), AreaM2 = 0.5 * System.Math.PI * rBody * rBody,
                FailPa = WaterPhysics.GlazingBurstPa(2 * rBody),
            };
            ws.HoleAt.Add(new Vec3(noseCut, 0, 0)); ws.HoleAt.Add(new Vec3(noseCut, 0, -rBody));
            cabin.AddPanel(ws);
            m.Derivation.Add($"cabin payload {payload:F0} kg (seats + baggage), door seal area {2 * doorGap * 1e4:F0} cm^2; doors burst at {doorPa / 1000:F1} kPa, windshield {ws.AreaM2:F2} m^2 at {ws.FailPa / 1000:F0} kPa");
        }
        else m.Derivation.Add($"cabin payload {payload:F0} kg (seats + baggage), {cons.Cockpits.Count} cockpit opening(s) from the config, no doors");

        double tailStruct = k * fusRaw(wetTail);
        double rEq = rBody / System.Math.Sqrt(3);
        HydroElement tail = Add("tailcone", WreckAtom.TailCone, new Vec3(tailCut - lTail * 0.25, 0, 0), new Vec3(lTail * 0.5, rEq, rEq),
            cyl(lTail) / 3, tailStruct, tailStruct / WaterPhysics.RhoAluminium);
        tail.AddDistributedOpening(2 * drain, "drain holes");

        // ---- along its own axis the fuselage is one slender body: Lamb's k1 for its whole fineness, shared by section ----
        {
            double k1 = WaterPhysics.LambAxialCoefficient((noseX - tailX) / (2 * rBody));
            foreach (HydroElement fe in new[] { nose, cabin, tail }) fe.AxialAddedMassKg = k1 * WaterPhysics.RhoWater * fe.EnvelopeM3;
            m.Derivation.Add($"fuselage fineness {(noseX - tailX) / (2 * rBody):F1}: axial added mass k1 = {k1:F3} of the water displaced (Lamb)");
        }

        // ---- cockpit openings (config construction.cockpits): split between the fuselage sections they lie over ----
        var fusEls = new[] { nose, cabin, tail };
        foreach (CockpitOpeningConfig ck in cons.Cockpits)
        {
            double xf = System.Math.Max(ck.FrontX, ck.AftX), xa = System.Math.Min(ck.FrontX, ck.AftX), ckLen = xf - xa;
            if (ckLen <= 1e-6 || ck.AreaM2 <= 0) continue;
            var canopy = new List<Panel>();
            foreach (HydroElement fe in fusEls)
            {
                // The section's own stations (the tail cone's box sits on its centroid and reaches past the cut).
                double ex0 = fe == nose ? noseCut : fe == cabin ? tailCut : tailX, ex1 = fe == nose ? noseX : fe == cabin ? noseCut : tailCut;
                double o0 = System.Math.Max(xa, ex0), o1 = System.Math.Min(xf, ex1);
                if (o1 - o0 <= 1e-6) continue;
                double area = ck.AreaM2 * (o1 - o0) / ckLen, top = fe.Centre.Z - fe.Half.Z;
                Vec3 front = new(o1, 0, top), aft = new(o0, 0, top);
                if (ck.IsOpen)
                {
                    fe.Openings.Add(new Opening(front, 0.5 * area, "open cockpit"));
                    fe.Openings.Add(new Opening(aft, 0.5 * area, "open cockpit"));
                    continue;
                }
                double w = ck.WidthM > 0 ? ck.WidthM : area / (o1 - o0);
                double fail = WaterPhysics.GlazingBurstPa(w);
                var plan = new Panel { Why = "canopy broken", At = new Vec3(0.5 * (o0 + o1), 0, top), Normal = new Vec3(0, 0, -1), U = new Vec3(0.5 * (o1 - o0), 0, 0), V = new Vec3(0, 0.5 * w, 0), AreaM2 = area, FailPa = fail };
                plan.HoleAt.Add(front); plan.HoleAt.Add(aft);
                canopy.Add(fe.AddPanel(plan));
                if (o1 >= xf - 1e-6)
                {
                    // The windscreen: the canopy's front, seen from ahead a half-disc as wide as the canopy (it is the same
                    // acrylic: when either breaks, all of it goes). It adds no area of its own: the hole is the canopy's.
                    var screen = new Panel { Why = "canopy broken (windscreen)", At = new Vec3(xf, 0, top), Normal = new Vec3(1, 0, 0), U = new Vec3(0, 0.5 * w, 0), V = new Vec3(0, 0, 0.25 * w), AreaM2 = 0, FailPa = fail };
                    canopy.Add(fe.AddPanel(screen));
                }
            }
            foreach (Panel pc in canopy) foreach (Panel q in canopy) if (q != pc) pc.Linked.Add(q);
            m.Derivation.Add($"cockpit x {xa:F2}..{xf:F2}: {ck.AreaM2:F3} m^2 {(ck.IsOpen ? "OPEN" : $"under a canopy bursting at {WaterPhysics.GlazingBurstPa(ck.WidthM) / 1000:F0} kPa")}");
        }
        // ---- skin: belly, top and both sides of each fuselage section can burst in a slam (the cowling is metal) ----
        foreach (HydroElement fe in fusEls)
        {
            double pa = fe == nose ? WaterPhysics.MetalSkinBurstPa(WaterPhysics.FuselageStringerPitchM) : fusSkinPa;
            fe.AddSkinFace(2, 1, pa, fe.Name + " belly burst");
            fe.AddSkinFace(2, -1, pa, fe.Name + " top burst");
            fe.AddSkinFace(1, -1, pa, fe.Name + " left side burst");
            fe.AddSkinFace(1, 1, pa, fe.Name + " right side burst");
        }
        m.Derivation.Add($"skin {(fabric ? "doped fabric" : "riveted 2024-T3")}: fuselage panels burst at {fusSkinPa / 1000:F0} kPa (slam speed {System.Math.Sqrt(2 * fusSkinPa / (WaterPhysics.RhoWater * WaterPhysics.SlamCoefficient)):F1} m/s)");

        m.Cuts.Add(new Cut { Child = WreckAtom.Nose, Parent = WreckAtom.Cabin, At = new Vec3(noseCut, 0, axisZ), AreaM2 = System.Math.PI * rBody * rBody, HalfHeight = rBody, Why = "fuselage torn at the firewall" });
        m.Cuts.Add(new Cut { Child = WreckAtom.TailCone, Parent = WreckAtom.Cabin, At = new Vec3(tailCut, 0, 0), AreaM2 = System.Math.PI * rBody * rBody, HalfHeight = rBody, Why = "fuselage torn aft of the cabin" });

        // ---- lifting surfaces ----
        foreach (Group gr in groups.Values)
        {
            bool tailSurf = gr.Atom is WreckAtom.HStab or WreckAtom.ElevatorL or WreckAtom.ElevatorR or WreckAtom.VStab or WreckAtom.Rudder;
            double structKg = k * (tailSurf ? WaterPhysics.RaymerTailKgPerM2 : WaterPhysics.RaymerWingKgPerM2) * gr.Area;
            double chord = gr.ChordArea / gr.Area, tc = gr.TcArea / gr.Area;
            double spanLen = gr.YMax - gr.YMin, spanMid = 0.5 * (gr.YMax + gr.YMin);
            double x = gr.AreaX / gr.Area, z = gr.AreaZ / gr.Area;
            Vec3 centre = gr.Vertical ? new Vec3(x, 0, spanMid) : new Vec3(x, spanMid, z);
            Vec3 half = gr.Vertical ? new Vec3(chord * 0.5, tc * chord * 0.5, spanLen * 0.5) : new Vec3(chord * 0.5, spanLen * 0.5, tc * chord * 0.5);
            HydroElement e = Add(gr.Name, gr.Atom, centre, half, gr.Vol, structKg, structKg / WaterPhysics.RhoAluminium);
            int bays = System.Math.Max(1, (int)System.Math.Round(spanLen / WaterPhysics.RibPitchM));
            e.AddDistributedOpening(2 * bays * drain, $"drain holes and seams ({bays} bays)");
            {
                // The two broad faces (normal to the thinnest axis): a slam bursts skin between the ribs.
                double skinPa = fabric ? WaterPhysics.FabricSkinBurstPa(WaterPhysics.FabricPanelSpanM) : WaterPhysics.MetalSkinBurstPa(WaterPhysics.RibPitchM);
                int thin = gr.Vertical ? 1 : 2;
                e.AddSkinFace(thin, 1, skinPa, e.Name + " skin burst");
                e.AddSkinFace(thin, -1, skinPa, e.Name + " skin burst");
            }
            // The joint to whatever it hangs on: the full section at its inboard end.
            WreckAtom? parent = gr.Atom switch
            {
                WreckAtom.WingLInner or WreckAtom.WingRInner => WreckAtom.Cabin,
                WreckAtom.WingLOuter => WreckAtom.WingLInner, WreckAtom.WingROuter => WreckAtom.WingRInner,
                WreckAtom.HStab or WreckAtom.VStab => WreckAtom.TailCone,
                _ => null,   // control surfaces hang on hinges: no section to tear open
            };
            if (parent is WreckAtom pa)
            {
                double secArea = WaterPhysics.AirfoilAreaFactor * gr.RootTc * gr.RootChord * gr.RootChord;
                double side = gr.Vertical ? 0 : System.Math.Sign(spanMid);
                Vec3 at = gr.Vertical ? new Vec3(gr.RootX, 0, gr.RootZ) : new Vec3(gr.RootX, side * gr.RootY, gr.RootZ);
                m.Cuts.Add(new Cut { Child = gr.Atom, Parent = pa, At = at, AreaM2 = secArea, HalfHeight = gr.RootTc * gr.RootChord * 0.5, Why = gr.Name + " torn at its root" });
            }
        }

        // ---- fuel tanks: sealed but for a vent line at the top ----
        foreach (AircraftLoad.Slot s in load.Slots)
        {
            if (s.Kind != AircraftLoad.StationKind.Fuel || s.MaxKg <= 0) continue;
            Vec3 st = s.Station;
            double vTank = s.MaxKg / WaterPhysics.RhoAvgas;
            // The element the tank sits in: the nearest wing panel/cabin box containing the station.
            HydroElement host = cabin;
            double best = double.MaxValue;
            foreach (HydroElement e in m.Elements)
            {
                if (e.Atom is not (WreckAtom.Cabin or WreckAtom.Nose or WreckAtom.WingLInner or WreckAtom.WingRInner or WreckAtom.WingLOuter or WreckAtom.WingROuter)) continue;
                double d = (e.Centre - st).Length - e.Half.Length;
                if (d < best) { best = d; host = e; }
            }
            double hx = System.Math.Min(host.Half.X, 0.5 * host.Half.X + 0.05), hz = 0.9 * host.Half.Z;
            double hy = vTank / (8 * hx * hz);
            var tank = Add("tank-" + s.Id, host.Atom, st, new Vec3(hx, hy, hz), vTank, s.Kg, s.Kg / WaterPhysics.RhoAvgas);
            tank.Openings.Add(new Opening(new Vec3(st.X, st.Y, st.Z - hz), WaterPhysics.CircleArea(WaterPhysics.FuelLineDiaM), "tank vent line"));
            // The tank is inside its host: take its volume out of the host so it is not displaced twice.
            double take = System.Math.Min(vTank, host.EnvelopeM3 * 0.9);
            host.EnvelopeM3 -= take; host.VoidM3 = System.Math.Max(0.0, host.VoidM3 - take); host.AirFreeM3 = host.VoidM3;
            if (host.Atom != WreckAtom.Cabin && host.Atom != WreckAtom.Nose)
            {
                WreckAtom parent = host.Atom is WreckAtom.WingLOuter ? WreckAtom.WingLInner : host.Atom is WreckAtom.WingROuter ? WreckAtom.WingRInner : WreckAtom.Cabin;
                m.Cuts.Add(new Cut { Child = host.Atom, Parent = parent, ChildElement = tank.Name, OneSided = true, At = new Vec3(st.X, st.Y, st.Z + hz), AreaM2 = WaterPhysics.CircleArea(WaterPhysics.FuelLineDiaM), HalfHeight = 0, Why = "fuel feed line pulled out of the tank" });
            }
            m.Derivation.Add($"tank {s.Id}: {vTank * 1000:F0} L holding {s.Kg:F0} kg of fuel, in {host.Name}");
        }

        // ---- gear legs (steel; the wheels' water forces are WheelHydro's while attached) ----
        double legKg = gearLegs.Count > 0 ? k * WaterPhysics.RaymerGearFraction * wdg * gearLegs.Count / System.Math.Max(1, c.Gear.Count) / gearLegs.Count : 0;
        foreach (GearConfig g in gearLegs)
        {
            AirframeComponent comp = AirframeContact.GearComponent(g);
            WreckAtom atom = comp switch { AirframeComponent.GearLeft => WreckAtom.GearLeft, AirframeComponent.GearRight => WreckAtom.GearRight, AirframeComponent.GearTail => WreckAtom.GearTail, _ => WreckAtom.GearNose };
            double v = legKg / WaterPhysics.RhoSteel;
            double hzLeg = System.Math.Max(0.05, System.Math.Abs(g.Pos[2]) * 0.25);
            double hxy = System.Math.Sqrt(v / (8 * hzLeg));
            Add("gear-" + atom, atom, new Vec3(g.Pos[0], g.Pos[1], g.Pos[2] * 0.6), new Vec3(hxy, hxy, hzLeg), v, legKg, v);
        }
        m.Derivation.Add($"total element mass {m.TotalMassKg:F0} kg vs config {wdg:F0} kg at this loading");
        BuildJoints(m, c, noseCut, tailCut, axisZ, wdg);
        CalibrateWingMass(m, c);
        return m;
    }

    /// <summary>
    /// Ultimate bending moment the elliptic part of Schrenk's lift carries about a station y_j of a half-wing of
    /// semispan s, per newton of half-wing lift: ∫ l(y)·(y − y_j) dy over y_j..s with l(y) = (4/(π s))·√(1 − (y/s)²):
    /// (4/π)·[(s/3)(1 − η²)^{3/2} − y_j·(π/4 − ½(η√(1 − η²) + asin η))], η = y_j/s. At the root, 4s/(3π).
    /// </summary>
    public static double EllipticArm(double yj, double s)
    {
        if (s <= 0) return 0;
        double eta = System.Math.Clamp(yj / s, 0.0, 1.0), q = System.Math.Sqrt(1 - eta * eta);
        return 4 / System.Math.PI * (s / 3 * q * q * q - yj * (System.Math.PI / 4 - 0.5 * (eta * q + System.Math.Asin(eta))));
    }

    private static void BuildJoints(AirframeHydroModel m, AircraftConfig c, double noseCut, double tailCut, double axisZ, double wdg)
    {
        // 3.8: 14 CFR 23.337 normal category, for a config that gives none. Every config in the fleet sets gMax, and until
        // 2026-11-03 a value of 10 was read as "unset" and replaced by 3.8 — which sized the Extra 300's (+10 g, aerobatic)
        // water joints for a normal-category aeroplane: its wing root at 34.5 kN·m against 64.5 at its own outboard panel
        // station (WingStructure, which always used the config's gMax), so a wingtip in the water took the whole wing.
        double gMax = c.Limits.GMax > 0 ? c.Limits.GMax : 3.8;
        double uf = c.Limits.UltimateFactor > 0 ? c.Limits.UltimateFactor : 1.5;
        double n = gMax * uf, g = WaterPhysics.G;
        m.UltimateLoadFactor = n;
        List<HydroElement> Beyond(WreckAtom[] atoms) => m.Elements.Where(e => atoms.Contains(e.Atom)).ToList();

        void Fuselage(AirframeComponent comp, Vec3 at, string why)
        {
            WreckAtom[] atoms = WreckAtoms.Of(comp);
            List<HydroElement> els = Beyond(atoms);
            if (els.Count == 0) return;
            double mom = 0, mass = 0; foreach (HydroElement e in els) { mom += e.MassKg * System.Math.Abs(e.Centre.X - at.X); mass += e.MassKg; }
            m.Joints.Add(new Joint { Component = comp, Atoms = atoms, At = at, Axis = new Vec3(1, 0, 0), UltimateNm = n * g * mom, UltimateAxialN = WaterPhysics.EmergencyForwardG * g * mass, Why = why });
        }
        Fuselage(AirframeComponent.Nose, new Vec3(noseCut, 0, axisZ), "firewall / engine mount");
        Fuselage(AirframeComponent.TailBoom, new Vec3(tailCut, 0, 0), "aft fuselage");

        // Wing lift by Schrenk: per side, half the design weight at n_ult, spread as the mean of planform and elliptic.
        var strips = new List<(double y, double area)>();
        double semi = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (!sf.Id.ToLowerInvariant().Contains("wing")) continue;
            foreach (StripConfig st in sf.Strips)
            {
                double w = st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;
                strips.Add((st.Pos[1], st.Area));
                semi = System.Math.Max(semi, System.Math.Abs(st.Pos[1]) + 0.5 * w);
            }
        }
        void Wing(AirframeComponent comp, WreckAtom child, int side)
        {
            WreckAtom[] atoms = WreckAtoms.Of(comp);
            List<HydroElement> els = Beyond(atoms);
            List<Cut> cuts = m.Cuts.Where(k => k.Child == child && !k.OneSided).ToList();
            if (els.Count == 0 || cuts.Count == 0 || semi <= 0) return;
            Vec3 at = new(cuts.Average(k => k.At.X), side * cuts.Average(k => System.Math.Abs(k.At.Y)), cuts.Average(k => k.At.Z));
            double yj = System.Math.Abs(at.Y);
            double sideArea = 0, planArm = 0;
            foreach ((double y, double a) in strips)
            {
                if (System.Math.Sign(y) != side) continue;
                sideArea += a;
                if (System.Math.Abs(y) > yj) planArm += a * (System.Math.Abs(y) - yj);
            }
            if (sideArea <= 0) return;
            double arm = 0.5 * (planArm / sideArea + EllipticArm(yj, semi));
            double relief = 0; foreach (HydroElement e in els) relief += e.MassKg * System.Math.Max(0.0, System.Math.Abs(e.Centre.Y) - yj);
            double ult = n * g * (0.5 * wdg * arm - relief);
            if (ult <= 0) return;
            m.Joints.Add(new Joint { Component = comp, Atoms = atoms, At = at, Axis = new Vec3(0, 1, 0), UltimateNm = ult, Why = $"{comp} root (Schrenk arm {arm:F2} m)" });
        }
        Wing(AirframeComponent.WingLeft, WreckAtom.WingLInner, -1);
        Wing(AirframeComponent.WingRight, WreckAtom.WingRInner, 1);
        Wing(AirframeComponent.WingLeftOuter, WreckAtom.WingLOuter, -1);
        Wing(AirframeComponent.WingRightOuter, WreckAtom.WingROuter, 1);
        foreach (Joint j in m.Joints) m.Derivation.Add($"joint {j.Component,-15} at x {j.At.X:F2} y {j.At.Y:F2}: ultimate {j.UltimateNm / 1000:F1} kN·m{(j.UltimateAxialN > 0 ? $", {j.UltimateAxialN / 1000:F1} kN along" : "")} ({j.Why}, n_ult {n:F1})");
    }

    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (string d in Derivation) sb.AppendLine("  " + d);
        sb.AppendLine("  element            atom        mass kg  envelope m3  void m3  density kg/m3  openings cm2");
        foreach (HydroElement e in Elements)
            sb.AppendLine($"  {e.Name,-18} {e.Atom,-11} {e.MassKg,7:F1} {e.EnvelopeM3,11:F3} {e.VoidM3,8:F3} {(e.EnvelopeM3 > 0 ? e.MassKg / e.EnvelopeM3 : 0),13:F0} {e.TotalOpeningM2 * 1e4,12:F1}");
        return sb.ToString();
    }
}

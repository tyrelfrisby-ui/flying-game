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
        AirframeComponent.AileronLeft => new[] { WreckAtom.AileronL },
        AirframeComponent.AileronRight => new[] { WreckAtom.AileronR },
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
    public double EmptyMassKg { get; private set; }
    public double DesignGrossKg { get; private set; }
    public double EngineDryKg { get; private set; }
    /// <summary>Factor applied to Raymer's airframe items to land on the config's empty mass.</summary>
    public double StructureScale { get; private set; }
    public readonly List<string> Derivation = new();

    public double TotalMassKg { get { double m = 0; foreach (HydroElement e in Elements) m += e.MassKg; return m; } }

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
            // Slender-blade propeller about its hub: I = m·R²/3, so m = 3·I/R² (hub and spinner not included).
            double r = p.PropDiameterM * 0.5;
            propKg = p.PropInertia > 0 ? 3 * p.PropInertia / (r * r) : 0;
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
        // Two doors, each a square 1.6·r on a side (a 172's door is about 0.95 m with r = 0.6 m), sealed by a gap
        // DoorSealGapM wide all round; each door edge spans the cabin side from sill to top.
        double doorSide = 1.6 * rBody, doorGap = WaterPhysics.DoorSealGapM * 4 * doorSide;
        cabin.AddSpanningOpening(new Vec3(cabin.Centre.X, -rBody, 0), doorGap, "left door seal");
        cabin.AddSpanningOpening(new Vec3(cabin.Centre.X, rBody, 0), doorGap, "right door seal");
        m.Derivation.Add($"cabin payload {payload:F0} kg (seats + baggage), door seal area {2 * doorGap * 1e4:F0} cm^2");

        double tailStruct = k * fusRaw(wetTail);
        double rEq = rBody / System.Math.Sqrt(3);
        HydroElement tail = Add("tailcone", WreckAtom.TailCone, new Vec3(tailCut - lTail * 0.25, 0, 0), new Vec3(lTail * 0.5, rEq, rEq),
            cyl(lTail) / 3, tailStruct, tailStruct / WaterPhysics.RhoAluminium);
        tail.AddDistributedOpening(2 * drain, "drain holes");

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
        return m;
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

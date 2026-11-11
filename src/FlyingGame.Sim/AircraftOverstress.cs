using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Ty's overstress pattern (2026-11-05, <see cref="OverstressMember"/>, <see cref="StructuralState"/>) made into the
/// aeroplane's geometry. Each step the three members' shapes become what flies and what is drawn:
///  • WINGS: every wing panel outboard of its structural station (<see cref="WingPanel"/>) turns tip-up by the wings'
///    set plus their flex under load — dihedral, load points and hard points move with it (the same geometry a ground
///    strike bends, <see cref="WingPanel.Deform"/>), and the panels' own hinges lose the strength the overstress took.
///  • HORIZONTAL TAIL: each half of the stabiliser and elevator turns about the centreline, tips DOWN under positive g
///    (a pull is a download on the tail and the tail's own mass hangs the same way). It is loaded by the balancing
///    download, not by the wings' lift (<see cref="StructuralState.TailDesignMargin"/>, Ty 2026-11-09), so a manoeuvre
///    that bends the wings leaves it straight.
///  • ENGINE MOUNT: the engine and propeller droop about the firewall, nose DOWN under positive g: the thrust line, the
///    disc's angle to the air and the hub's place all follow (<see cref="EngineSagRad"/>).
/// Whatever passes its break departs through the same path as any other lost part (FailWings, LoseComponent).
/// </summary>
public sealed partial class Aircraft
{
    private double _structGrossKg = -1;
    private double _tailShownRad;

    /// <summary>The gross mass the wings' limit load factors were rated at: the placard's maximum take-off mass, else the
    /// config's authored mass.</summary>
    /// <summary>
    /// THE DESIGN GROSS WEIGHT, which is what a structural limit is certificated against — not the weight the
    /// aeroplane happens to be flying at. A float's ultimate water load, a tyre's rating, a spar's limit load: all of
    /// them are fixed properties of the structure, set once at the design weight. They do not get weaker because the
    /// aeroplane is light today.
    ///
    /// THE FALLBACK TO Mass.MassKg IS A LAST RESORT AND IT USED TO BE HARMLESS. While every config authored
    /// mass.massKg AS its max gross the two were the same number. They stopped being the same on 2026-11-11, when the
    /// fleet's empty weights were corrected and massKg became the aeroplane AS FLOWN — the J-3 on floats went 589.7
    /// to 451.1 kg. Anything structural still reading massKg therefore shrank with it. Measured on that type: the
    /// float's modelled strength fell 36.3 %.
    ///
    /// (THAT FIGURE WAS 16.4 % HERE UNTIL 2026-11-11, and the 16.4 was an understatement with an instructive cause.
    /// It takes the 23.527 ultimate as ∝ W^(2/3), which is only true with V_S0 held still — and V_S0 does not hold
    /// still when the whole aeroplane is lighter. The ultimate is 1.5·n_w·W with n_w ∝ V_S0²/W^(1/3), so it goes as
    /// V_S0²·W^(2/3), and V_S ∝ √W carries another full factor of W: W^(5/3), measured 0.637 against the predicted
    /// 0.640. The reason W^(2/3) looked right in StructuralGrossTests is that Aircraft's V_S0 cache was keyed by type
    /// id alone, so its two copies of one type were handed each other's stall speed and the V_S0² cancelled. Both are
    /// fixed in Aircraft.InitFloatWaterLoads: the stall speed in 23.527 is the DESIGN GROSS one too, and the cache
    /// key carries the weights.)
    ///
    /// So this now consults the PLACARD first (loading.maxTakeoffMassKg, which the certificate work authors) before
    /// falling back. A type with neither is authored at its gross the old way and is unchanged.
    /// </summary>
    internal static double StructuralGrossKg(AircraftConfig c) =>
        c.Loading is { MaxGrossMassKg: > 0 } g ? g.MaxGrossMassKg
        : c.Loading is { MaxTakeoffMassKg: > 0 } t ? t.MaxTakeoffMassKg
        : c.Mass.MassKg;

    /// <summary>The horizontal tail's rows: the stabiliser and the elevator (LoseComponent's TailHorizontal).</summary>
    internal static bool IsHorizontalTail(string id) =>
        string.Equals(id, "hstab", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "elevator", StringComparison.OrdinalIgnoreCase);

    internal static bool HasHorizontalTail(AircraftConfig c) => c.Surfaces.Any(s => IsHorizontalTail(s.Id) && s.Strips.Count > 0);

    /// <summary>A propeller engine on a mount: the nose engine of a single, or the nacelles of a multi.</summary>
    internal static bool HasEngineMount(AircraftConfig c) => c.Propulsion is { PropDiameterM: > 0, MaxPowerW: > 0 };

    /// <summary>The wings' (and tail's) weight against the gross they were rated at, now (<see cref="LimitsConfig.WingWeightRatio"/>).</summary>
    public double WingWeightRatio
    {
        get
        {
            if (_structGrossKg < 0) _structGrossKg = StructuralGrossKg(Config);
            return Config.Limits.WingWeightRatio(MassProperties.MassKg, _structGrossKg);
        }
    }

    /// <summary>The engine's droop about the firewall, rad, + = nose DOWN: the mount's overstress shape plus any bend a crash
    /// crushed into it (<see cref="FuselageCrush.NoseBendRad"/>).</summary>
    public double EngineSagRad => (Structure.EngineMount?.DeflectionRad ?? 0.0) + (_crush?.NoseBendRad ?? 0.0);

    /// <summary>
    /// The horizontal tail's bend at its tips, rad, + = tips UP.
    ///
    /// The member's own sign is the other way round, because <see cref="OverstressMember"/>'s + is "the way positive g
    /// bends it" and for a tail that is tips DOWN: a pull is a DOWNLOAD on the tail (it balances the wing's nose-down
    /// pitching moment) and the tail's own mass hangs the same way. So this negates it, and a positive-g overstress comes
    /// out NEGATIVE here — tips down. Checked end to end against Ty's 2026-11-09 report that the stabiliser was drawn
    /// bent the wrong way (OverstressPatternTests: the Extra 300 held at 1.8 × its limit gives tail
    /// DeflectionRad +16.00° and TailBendRad −16.00°, and both <see cref="ApplyTailBend"/> and the drawn mesh take + as
    /// tips up): the sign is right in the sim, the whole way through. What was drawn wrong was the ELEVATOR, which the
    /// bend pinned at the deflection it had when the bend started — stick back, trailing edge up — see
    /// AirframeVisual.ShowTailBend.
    /// </summary>
    public double TailBendRad => -(Structure.HorizontalTail?.DeflectionRad ?? 0.0);

    /// <summary>Which way the elevator was deflected when the tail FIRST took a permanent set: +1, -1, or 0 while
    /// the tail is straight. The structure remembers the load that bent it, not where the stick is now — the same
    /// device as the bent wing's aileron centre (BentAileronTravel), kept once and never moved.</summary>
    private double _stabBentToward;

    /// <summary>
    /// A BENT STABILISER LEAVES THE ELEVATOR LESS, AND NOT EVENLY (Ty 2026-11-10): "apply the same limited travel
    /// rule as the ailerons but be more generous on available travel so the always have some of the other direction
    /// like if they push to max bending before failure the elevator will move to full down still but only 10
    /// percent up and vise versa". The rules are in <see cref="BentStabTravel"/>; this captures the sense and
    /// applies it to the step's target, before the actuator slews toward it.
    /// </summary>
    private FlyingGame.Core.Aero.ControlDeflections LimitBentStab(FlyingGame.Core.Aero.ControlDeflections t)
    {
        if (Structure.HorizontalTail is not { } tail) return t;
        double bend = Math.Abs(tail.SetRad);
        if (bend <= 1e-6) return t;
        if (_stabBentToward == 0.0)
        {
            // The first set: the elevator's own deflection is the sense that bent it. A tail bent with the stick
            // central (a gust, a strike) leaves no preferred way, so the band is symmetric from here.
            _stabBentToward = Math.Sign(t.ElevatorRad);
            if (_stabBentToward == 0.0) _stabBentToward = Math.Sign(_elevatorRad);
            if (_stabBentToward == 0.0) return t;
        }
        double rupture = OverstressMember.BreakRatio * OverstressMember.FlexPerLimitRad;
        double held = BentStabTravel.Clamp(t.ElevatorRad, _stabBentToward, bend, rupture, Config.Controls.Elevator.MaxDeflRad);
        return held == t.ElevatorRad ? t : new FlyingGame.Core.Aero.ControlDeflections(t.AileronRad, held, t.RudderRad, t.SpoilerFraction);
    }

    /// <summary>Per-step structural bookkeeping from the load factor of the step just integrated.</summary>
    /// <remarks>All three members are loaded by the load factor the AIR makes. In flight that is the whole aeroplane's (less a
    /// few per cent of thrust); on the ground, in the water or against a solid the push of the contact is not a load
    /// factor on any of them — a strike on a wingtip, the nose or the tail is the contact model's (the panels' plastic
    /// hinges, FuselageCrush), and a contact spring's step-long spike must not read as a 20 g pull on the engine mount.</remarks>
    /// <remarks>The two wings go in on their OWN load factors (<see cref="WingSideLoadFactor"/>): the air's rolling
    /// moment tells them apart, so a rolling pull loads the down-aileron wing harder and the stagger can be
    /// asymmetric (Ty 2026-11-10).</remarks>
    private void UpdateStructure(double dt) =>
        StepStructure(WingSideLoadFactor(-1), WingSideLoadFactor(1), AeroLoadFactorZ, dt);

    /// <summary>Put the structure through one step at held load factors (scenarios, lessons, tests): exactly what a step
    /// that flew them does to it — the overstress pattern, any part that departs, and the shapes the aero and the visual
    /// then take. <paramref name="nAero"/> loads the wings and tail (weight-adjusted), <paramref name="nTotal"/> the engine.</summary>
    public void HoldLoadFactor(double nAero, double nTotal, double dt) => StepStructure(nAero, nAero, nTotal, dt);

    /// <summary>The same with the two wings held at DIFFERENT load factors (tests, probes): a rolling pull without
    /// having to fly one, so one wing can be taken past its break while the other is not.</summary>
    public void HoldWingLoadFactors(double nLeft, double nRight, double nTotal, double dt) =>
        StepStructure(nLeft, nRight, nTotal, dt);

    private void StepStructure(double nLeft, double nRight, double nTotal, double dt)
    {
        StructuralBreak broke = Structure.Step(nLeft, nRight, nTotal, WingWeightRatio, dt);
        ApplyWingBreaks(broke);
        if ((broke & StructuralBreak.HorizontalTail) != 0 && !IsLost(AirframeComponent.TailHorizontal) && !IsLost(AirframeComponent.TailBoom))
            LoseComponent(AirframeComponent.TailHorizontal);
        if ((broke & StructuralBreak.EngineMount) != 0) LoseEngineMount();
        ApplyOverstressShape();
    }

    /// <summary>The engine mount has let go: the nose engine leaves with the nose section (its leg with it), a wing engine
    /// with its nacelle.</summary>
    private void LoseEngineMount()
    {
        if (Config.Engines.Count == 0) { LoseComponent(AirframeComponent.Nose); return; }
        bool left = false, right = false, centre = false;
        foreach (EngineMount e in Config.Engines)
        {
            if (e.Pos[1] < -0.3) left = true; else if (e.Pos[1] > 0.3) right = true; else centre = true;
        }
        if (left) LoseComponent(AirframeComponent.NacelleLeft);
        if (right) LoseComponent(AirframeComponent.NacelleRight);
        if (centre) LoseComponent(AirframeComponent.Nose);
    }

    /// <remarks>PER SIDE since 2026-11-10 (<see cref="WingFailureLaw"/>): each panel takes the shape, the set and the
    /// weakening of ITS OWN wing's member, so a rolling pull leaves one wing bent more than the other — "asymmetric
    /// aerodynamic bending is shown as bent more on one side than the other" (Ty, same day). Before this every panel
    /// read one member, so the two wings could only ever be bent the same.</remarks>
    private void ApplyOverstressShape()
    {
        if (_panels.Count > 0 && !Structure.WingsFailed)
        {
            // What each side's overstresses have taken off it since the last step, booked BEFORE the panels are walked:
            // a biplane has two panels on a side and they must both lose it, so the running total cannot be advanced
            // panel by panel.
            double weakenL = Structure.LeftWing.Member.Strength / _wingStrengthPerSide[0];
            double weakenR = Structure.RightWing.Member.Strength / _wingStrengthPerSide[1];
            _wingStrengthPerSide[0] = Structure.LeftWing.Member.Strength;
            _wingStrengthPerSide[1] = Structure.RightWing.Member.Strength;
            for (int k = 0; k < _panels.Count; k++)
            {
                WingPanel w = _panels[k];
                if (w.State == PanelState.Broken) continue;
                OverstressMember m = Structure.Wing(w.Side).Member;
                double weaken = w.Side < 0 ? weakenL : weakenR;
                if (Math.Abs(weaken - 1.0) > 1e-12) Weaken(w, weaken);
                double set = m.SetRad, flex = m.FlexRad;
                bool setChanged = w.OverstressSetRad != set;
                bool flexChanged = Math.Abs(w.FlightFlexRad - flex) > 5e-4 || (flex == 0.0 && w.FlightFlexRad != 0.0);
                if (!setChanged && !flexChanged) continue;
                w.OverstressSetRad = set;
                w.FlightFlexRad = flex;
                if (setChanged)
                {
                    if (w.State == PanelState.Intact && Math.Abs(set) >= WingStructure.VisibleSetRad) w.State = PanelState.Bent;
                    SetBend(k);
                }
                else ApplyPanelFlex(k);
            }
            // A fold deepens with the load after the step it first appeared on, so it is refreshed here rather than on
            // the one step the structure reported it (ApplyWingFold leaves a panel alone when nothing has moved).
            ApplyWingFold();
        }
        if (Structure.HorizontalTail is { } tail && !IsLost(AirframeComponent.TailHorizontal) && !IsLost(AirframeComponent.TailBoom))
        {
            double bend = TailBendRad;
            if (Math.Abs(bend - _tailShownRad) > 5e-4 || (bend == 0.0 && _tailShownRad != 0.0)) { _tailShownRad = bend; ApplyTailBend(bend); }
        }
    }

    /// <summary>What an overstress took from the wings, taken from each panel's hinges too: a weakened wing is weaker on the
    /// ground as well.</summary>
    private static void Weaken(WingPanel w, double factor)
    {
        w.LimitNm *= factor; w.UltimateNm *= factor;
        foreach (PlasticHinge? h in new[] { w.Bending, w.Twisting })
            if (h is not null) { h.YieldNm *= factor; h.UltimateNm *= factor; }
        SetCaps(w, 0.0);
    }

    /// <summary>The whole wing side flies the shape the panel is in: the aerodynamic CURVE from the root, the strike's
    /// kink at the station, and any fold. Always the whole side (<see cref="ShapeWingSide"/>) — a curve moves the rows
    /// inboard of the station too, and writing only the panel's own left the inner wing flat under a set.</summary>
    private void ApplyPanelFlex(int k) => ShapeWingSide(_panels[k]);

    /// <summary>Each half of the horizontal tail turned about the centreline by <paramref name="rad"/> (+ = tips up): its
    /// rows' dihedral grows by it and their load points move with it.</summary>
    private void ApplyTailBend(double rad)
    {
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        double c = Math.Cos(rad), sn = Math.Sin(rad);
        for (int i = 0; i < _stripTotal; i++)
        {
            if (_stripSurface[i] is not { } sf || !IsHorizontalTail(sf.Id)) continue;
            Vec3 pos = _stripAt[i]!.PosVec();
            double ay = Math.Abs(pos.Y), side = Math.Sign(pos.Y);
            _flowState.BendRad[i] = rad;
            _flowState.BendOffset[i] = new Vec3(0, side * ay * (c - 1.0), -ay * sn);
        }
    }

    // ---- the engine on its mount ---------------------------------------------------------------------------------

    /// <summary>
    /// The propeller's forces with the engine drooped by <see cref="EngineSagRad"/>: the prop is run in its own tilted frame
    /// (the air meets the disc at the new angle, so the P-factor and the normal force follow), its force and moment turned
    /// back into the body, and — for a nose engine — the hub moved with the mount about the firewall, so the thrust acts
    /// on its new line.
    /// </summary>
    private (Vec3 Force, Vec3 Moment) MountedPropulsion(Vec3 velocity, Vec3 rates, double airDensity)
    {
        double sag = EngineSagRad;
        if (Math.Abs(sag) < 1e-6 || Config.Propulsion is null)
            return PropulsionForces(Config, _throttle01, velocity, rates, airDensity, _noseLost, _noseBody, PropThrustNow, _drive, _slipOverTail, _wakeStalledFrac);
        (Vec3 f, Vec3 m) = PropulsionForces(Config, _throttle01, ToMount(velocity, sag), ToMount(rates, sag), airDensity, _noseLost, _noseBody, PropThrustNow, _drive, _slipOverTail, _wakeStalledFrac);
        f = FromMount(f, sag);
        m = FromMount(m, sag);
        if (Config.Engines.Count == 0)
        {
            // Turning the moment turned the thrust's arm about the CG; the hub really turns about the firewall. The
            // difference is (R − I)(cg − pivot) across the new force.
            (double noseCut, _) = AirframeContact.FuselageStations(Config);
            Vec3 cg = Config.Mass.CgVec();
            Vec3 pivot = new(noseCut, 0.0, cg.Z + Config.Propulsion.ThrustLineZ);
            Vec3 d = cg - pivot;
            m += Vec3.Cross(FromMount(d, sag) - d, f);
        }
        return (f, m);
    }

    /// <summary>A body vector in the drooped engine's frame (its x along the new thrust line), and back. Droop is a
    /// nose-down pitch: the engine's x is (cos θ, 0, sin θ) in the body.</summary>
    internal static Vec3 ToMount(Vec3 v, double sag)
    {
        double c = Math.Cos(sag), s = Math.Sin(sag);
        return new Vec3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    internal static Vec3 FromMount(Vec3 v, double sag)
    {
        double c = Math.Cos(sag), s = Math.Sin(sag);
        return new Vec3(v.X * c - v.Z * s, v.Y, v.X * s + v.Z * c);
    }
}

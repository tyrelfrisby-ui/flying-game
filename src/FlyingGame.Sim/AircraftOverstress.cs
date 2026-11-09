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
///    (a pull is a download on the tail and the tail's own mass hangs the same way).
///  • ENGINE MOUNT: the engine and propeller droop about the firewall, nose DOWN under positive g: the thrust line, the
///    disc's angle to the air and the hub's place all follow (<see cref="EngineSagRad"/>).
/// Whatever passes its break departs through the same path as any other lost part (FailWings, LoseComponent).
/// </summary>
public sealed partial class Aircraft
{
    private double _structGrossKg = -1;
    private double _wingStrengthApplied = 1.0;
    private double _tailShownRad;

    /// <summary>The gross mass the wings' limit load factors were rated at: the placard's maximum take-off mass, else the
    /// config's authored mass.</summary>
    internal static double StructuralGrossKg(AircraftConfig c) => c.Loading is { MaxGrossMassKg: > 0 } l ? l.MaxGrossMassKg : c.Mass.MassKg;

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

    /// <summary>The horizontal tail's bend at its tips, rad, + = tips UP (the overstress member's sign is the other way).</summary>
    public double TailBendRad => -(Structure.HorizontalTail?.DeflectionRad ?? 0.0);

    /// <summary>Per-step structural bookkeeping from the load factor of the step just integrated.</summary>
    /// <remarks>All three members are loaded by the load factor the AIR makes. In flight that is the whole aeroplane's (less a
    /// few per cent of thrust); on the ground, in the water or against a solid the push of the contact is not a load
    /// factor on any of them — a strike on a wingtip, the nose or the tail is the contact model's (the panels' plastic
    /// hinges, FuselageCrush), and a contact spring's step-long spike must not read as a 20 g pull on the engine mount.</remarks>
    private void UpdateStructure(double dt) => StepStructure(AeroLoadFactorZ, AeroLoadFactorZ, dt);

    /// <summary>Put the structure through one step at held load factors (scenarios, lessons, tests): exactly what a step
    /// that flew them does to it — the overstress pattern, any part that departs, and the shapes the aero and the visual
    /// then take. <paramref name="nAero"/> loads the wings and tail (weight-adjusted), <paramref name="nTotal"/> the engine.</summary>
    public void HoldLoadFactor(double nAero, double nTotal, double dt) => StepStructure(nAero, nTotal, dt);

    private void StepStructure(double nAero, double nTotal, double dt)
    {
        StructuralBreak broke = Structure.Step(nAero, nTotal, WingWeightRatio, dt);
        if ((broke & StructuralBreak.Wings) != 0) FailWings();
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

    private void ApplyOverstressShape()
    {
        OverstressMember wings = Structure.Wings;
        if (_panels.Count > 0 && !Structure.WingsFailed)
        {
            double weaken = wings.Strength / _wingStrengthApplied;
            bool weakened = Math.Abs(weaken - 1.0) > 1e-12;
            if (weakened) _wingStrengthApplied = wings.Strength;
            double set = wings.SetRad, flex = wings.FlexRad;
            for (int k = 0; k < _panels.Count; k++)
            {
                WingPanel w = _panels[k];
                if (w.State == PanelState.Broken) continue;
                if (weakened) Weaken(w, weaken);
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

    /// <summary>The panel's rows fly at its shape under load: its set (in <see cref="WingPanel.BendRad"/>) plus the flex.</summary>
    private void ApplyPanelFlex(int k)
    {
        WingPanel w = _panels[k];
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        foreach (int i in w.Strips)
        {
            Vec3 pos = _stripAt[i]!.PosVec();
            _flowState.BendRad[i] = w.BendRad + w.FlightFlexRad;
            _flowState.BendOffset[i] = w.Deform(pos, w.FlightFlexRad) - pos;
        }
    }

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

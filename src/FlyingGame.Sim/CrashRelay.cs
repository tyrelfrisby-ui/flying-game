using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Sim;

/// <summary>
/// Turns one aeroplane's crash into CrashEvents (the contract with the crash-audio lane) and runs its wreck.
///
///  - A hard contact of the airframe (Aircraft.HardImpact) becomes a <see cref="CrashImpact"/> for piece −1 with the
///    surface it actually struck — the hard point's own test of water against ground (AirframeContact), so a ditching
///    is Water and a runway or a hillside is Land. Wheels on land are not a crash (the bridge's tyre chirp).
///  - A component that breaks off (Aircraft.ComponentLost, or the wings at ultimate load) becomes a
///    <see cref="WreckPiece"/> carrying the volumes it took with it (AirframeFlooding.Detach); the WreckSim raises the
///    piece's own impact at its first touch of water or land and its FloodState while it is in the water.
///  - The intact (or remaining) airframe in the water reports its FloodState as piece −1.
///
/// Severity01 for the airframe is the same as for a piece (WreckSim.Severity): (v_n / v_break)², v_break the cabin
/// break speed × the config's impact strength. The thresholds below which nothing is raised are the ones the crash
/// sound already had: a hard point arriving on land at under 1.2 m/s is a scrape, not a crunch (StructuralDamage,
/// 2026-09); on water anything over 0.5 m/s makes a splash.
///
/// Main/sim thread only (the contract's rule). Unity's StructuralDamage owns one of these for the player's aeroplane.
/// </summary>
public sealed class CrashRelay
{
    public const double LandCrunchMinMs = 1.2, WaterSplashMinMs = 0.5;
    public Aircraft Aircraft { get; }
    public WreckSim Wreck { get; }
    /// <summary>A piece has left the airframe (the bridge gives it a visual).</summary>
    public event Action<WreckPiece>? PieceSpawned;
    /// <summary>One slick per crash; replacing this relay on respawn clears it with the wreck.</summary>
    public FuelSlick? Slick { get; private set; }
    public double SlickCreatedSimS { get; private set; }
    private readonly bool _subscribed;
    private bool _wingsHandled;
    private FuelSlick? _pendingSlick;
    private double _pendingSlickSimS, _lastWaterImpactSimS = double.NegativeInfinity;
    private double _floodClock, _floodEscaped;
    public double ReportIntervalS { get; set; } = 0.1;
    /// <summary>The last FloodState raised for the airframe (−1), for probes.</summary>
    public FloodState? LastAirframeFlood { get; private set; }

    public CrashRelay(Aircraft aircraft, IWreckWorld? world = null, bool subscribe = true)
    {
        Aircraft = aircraft;
        Wreck = new WreckSim(world)
        {
            BreakSpeedMs = AirframeContact.CabinBreakMs * (aircraft.Config.ImpactStrength > 0 ? aircraft.Config.ImpactStrength : 1.0),
        };
        _subscribed = subscribe;
        if (subscribe)
        {
            aircraft.HardImpact += OnHardImpactEvent;
            aircraft.ComponentLost += OnComponentLostEvent;
        }
        _wingsHandled = aircraft.Structure.WingsFailed;
    }

    public void Detach()
    {
        if (!_subscribed) return;
        Aircraft.HardImpact -= OnHardImpactEvent;
        Aircraft.ComponentLost -= OnComponentLostEvent;
    }

    private void OnHardImpactEvent(double closing, string point) => OnHardImpact(closing, point);
    private void OnComponentLostEvent(AirframeComponent c) => OnComponentLost(c);

    private double Severity(double vn) => WreckSim.Severity(vn, Wreck.BreakSpeedMs);

    /// <summary>The airframe struck something this step. Returns true if a CrashImpact was raised.</summary>
    public bool OnHardImpact(double closingMs, string point)
    {
        ImpactRecorder.Hit hit = Aircraft.LastImpact;
        bool water = hit.OnWater;
        if (water)
        {
            _lastWaterImpactSimS = Atmosphere.SimTimeSec;
            if (Slick == null && _pendingSlick != null && _pendingSlickSimS == Atmosphere.SimTimeSec)
            { Slick = _pendingSlick; SlickCreatedSimS = _pendingSlickSimS; }
        }
        _pendingSlick = null;
        if (!water && point.StartsWith("Gear")) return false;                       // a wheel on land: the tyre's business
        if (closingMs < (water ? WaterSplashMinMs : LandCrunchMinMs)) return false;
        RaiseAirframe(water ? ImpactSurface.Water : ImpactSurface.Land, hit, closingMs);
        return true;
    }

    private void RaiseAirframe(ImpactSurface surface, ImpactRecorder.Hit hit, double closingMs)
    {
        RigidBodyState s = Aircraft.State;
        Vec3 at = hit.PositionWorld.LengthSquared > 0 ? hit.PositionWorld : s.Position;
        Vec3 vel = hit.VelocityWorld.LengthSquared > 0 ? hit.VelocityWorld : s.Attitude.Rotate(s.Velocity);
        CrashEvents.RaiseImpact(new CrashImpact(surface, at, vel, closingMs, Aircraft.MassProperties.MassKg, Severity(closingMs), -1));
    }

    /// <summary>A component broke off. Returns its wreck piece, or null when nothing leaves (the cabin break-up
    /// arrives as its nose and tail; a prop strike bends the blades in place — that one is raised as an airframe
    /// impact, at the speed of the contact that struck it).</summary>
    public WreckPiece? OnComponentLost(AirframeComponent c)
    {
        if (c == AirframeComponent.Propeller)
        {
            ImpactRecorder.Hit hit = Aircraft.LastImpact;
            RaiseAirframe(hit.OnWater ? ImpactSurface.Water : ImpactSurface.Land, hit, hit.ClosingMs);
            return null;
        }
        List<HydroElement>? els = Aircraft.Hydro.TakeDetached(c);
        if (els == null || els.Count == 0) return null;
        return Spawn(c.ToString(), c, els);
    }

    /// <summary>The wings failed at ultimate load: two pieces (left, right), or none if already handled.</summary>
    public IReadOnlyList<WreckPiece> OnWingsFailed()
    {
        var list = new List<WreckPiece>(2);
        if (_wingsHandled) return list;
        _wingsHandled = true;
        foreach (AirframeComponent c in new[] { AirframeComponent.WingLeft, AirframeComponent.WingRight })
        {
            List<HydroElement>? els = Aircraft.Hydro.TakeDetached(c);
            if (els != null && els.Count > 0) list.Add(Spawn(c.ToString(), c, els));
        }
        return list;
    }

    private WreckPiece Spawn(string name, AirframeComponent c, List<HydroElement> els)
    {
        WreckPiece p = Wreck.Add(name, c, els, Aircraft.Config.Mass.CgVec(), Aircraft.State);
        // What broke it off may hold it: a trunk the piece struck stops it there (after it has left with the airframe's own
        // velocity field, so momentum is conserved at the separation and the stop is the piece's alone).
        if (Aircraft.TakeSeparationStop(c, out Vec3 stopAt, out Vec3 stopN)) p.StopAt(stopAt, stopN);
        // A prop/gear loss alone is an intact ditching, not an airframe break-up. LastImpact is
        // populated before ComponentLost in Aircraft.Step, so this snapshots the actual fuel now.
        bool airframeBreak = c is not (AirframeComponent.GearLeft or AirframeComponent.GearRight
            or AirframeComponent.GearNose or AirframeComponent.GearTail or AirframeComponent.Propeller);
        ImpactRecorder.Hit hit = Aircraft.LastImpact;
        bool waterJointFailed = Aircraft.Hydro.Active && Aircraft.Hydro.Model.Joints.Any(j =>
            j.Component == c && (j.PeakNm > j.UltimateNm ||
            (j.UltimateAxialN > 0 && j.PeakAxialN > j.UltimateAxialN)));
        if (_pendingSlick != null && _pendingSlickSimS != Atmosphere.SimTimeSec) _pendingSlick = null;
        if (Slick == null && _pendingSlick == null && airframeBreak && (hit.OnWater || waterJointFailed))
        {
            // A water-loaded joint may fail before any hard point reports contact. In that case
            // the departing piece's lowest point supplies the water contact, projected to the surface.
            Vec3 at = hit.OnWater ? hit.PositionWorld : p.LowestPoint();
            double? surface = Wreck.World.WaterUpAt(at.X, at.Y, -at.Z);
            if (surface.HasValue)
            {
                at = new Vec3(at.X, at.Y, -surface.Value);
                var candidate = FuelSlick.TryCreate(Aircraft.Config, Aircraft.Load, true, true, at,
                    FuelSlick.MeanWind10At(at));
                // HardImpact follows ComponentLost in Aircraft.Step. Confirm the water contact in
                // this same sim step; LastImpact alone may be stale after an earlier intact ditching.
                // Hydro joints can fail on the next slam-loaded step after the first hard-point
                // splash, with no new HardImpact. The joint's measured overload confirms that path.
                if (waterJointFailed || _lastWaterImpactSimS == Atmosphere.SimTimeSec)
                { Slick = candidate; SlickCreatedSimS = Atmosphere.SimTimeSec; }
                else { _pendingSlick = candidate; _pendingSlickSimS = Atmosphere.SimTimeSec; }
            }
        }
        PieceSpawned?.Invoke(p);
        return p;
    }

    /// <summary>
    /// DOES ANYTHING THAT HAS LEFT THE AEROPLANE HIT IT ON THE WAY PAST? Ty 2026-11-11: "The engine and prop while
    /// separating should follow an accurate flight path and if that path contacts the airframe it should damage it
    /// appropriately", and "Yes thrown blade also follows physics and causes damage".
    ///
    /// It is general on purpose. The blade and the engine are what prompted it, but a departed wing panel, a canopy
    /// and a gear leg all leave the same way and all fly down the same fuselage, so the test belongs to every piece
    /// in the air rather than to the two that asked for it. <see cref="WreckStrike"/> does the geometry against the
    /// intact airframe's own boxes (<see cref="AirframeHydroModel"/>), and the only thing decided here is what a
    /// strike COSTS.
    ///
    /// AND THE STRIKE DOES NOT END THE PIECE'S FLIGHT. Ty: "And falls from the sky [unt]il it hits the ground". It
    /// is deflected by the impulse it just delivered and carries on, to the ground or the water, exactly as it would
    /// have done — <see cref="WreckSim"/> has no lifetime and no distance cull, so the two are the same simulation.
    /// </summary>
    private void StepStrikes(double dt)
    {
        if (Wreck.Pieces.Count == 0 || Aircraft.Structure.WingsFailed) return;
        bool anyThrown = false;
        foreach (WreckPiece tp in Wreck.Pieces) if (tp.Thrown && tp.Phase == WreckPhase.InAir && !tp.Asleep) { anyThrown = true; break; }
        if (!anyThrown) return;                            // the common case: nothing was flung, nothing to test
        IReadOnlyList<HydroElement> airframe = AirframeBoxes();
        if (airframe.Count == 0) return;
        if (_airframeReachM <= 0.0)
            foreach (HydroElement e in airframe)
                _airframeReachM = System.Math.Max(_airframeReachM, (e.Centre - Aircraft.Config.Mass.CgVec()).Length + e.Half.Length);
        RigidBodyState s = Aircraft.State;
        Vec3 cg = Aircraft.Config.Mass.CgVec();
        foreach (WreckPiece p in Wreck.Pieces)
        {
            if (!p.Thrown || p.Phase != WreckPhase.InAir || p.Asleep) continue;
            // BROAD PHASE, and it is not an optimisation for its own sake: a departed piece spends 47 seconds
            // falling and all but the first fraction of one of them a long way from the aeroplane, so without this
            // the slab test runs over every airframe box for the whole fall. One distance check retires it — the
            // piece cannot reach the aeroplane this step if it is further off than both their reaches plus how far
            // the two can close in dt. MEASURED: a blade and the engine that followed it off the mount, falling from
            // 1,000 m, cost 4,278 ms of CPU before this and 1,576 ms after — for the same 47.4 s trajectory to the
            // same landing point, 247 m downrange. What is left is the two pieces' own 6-DOF aero, which is the
            // thing actually being asked for; the strike test is no longer most of the bill.
            Vec3 rel = p.State.Position - s.Position;
            double close = p.ReachM + _airframeReachM + (p.State.Attitude.Rotate(p.State.Velocity) - s.Attitude.Rotate(s.Velocity)).Length * dt;
            if (rel.LengthSquared > close * close) { _clear.Add(p.Id); continue; }
            // A PIECE THAT HAS JUST BROKEN OFF IS TOUCHING THE AEROPLANE BY DEFINITION, and must not be read as
            // striking it: it was part of the airframe a moment ago and it spawns exactly where its parts were
            // (WreckSim.Add). Without this every break-up tore further components off on its first step — measured,
            // ten crash and break-up tests went red at once, which is the model telling the truth about a rule it
            // had not been given. So a piece has to get CLEAR of the airframe before it can come back and hit it.
            if (!_clear.Contains(p.Id))
            {
                if (Clear(p, s, cg, airframe)) _clear.Add(p.Id);
                continue;
            }
            WreckStrike.Hit hit = WreckStrike.Test(p, s, cg, airframe, dt);
            if (!hit.Any) continue;
            // The impulse the piece delivers, through its own mass and inertia (inelastic). The piece is deflected
            // by it; the aeroplane is judged by it.
            Vec3 atWorld = s.Position + s.Attitude.Rotate(hit.AtBody - cg);
            double j = p.StopAt(atWorld, s.Attitude.Rotate(hit.NormalBody));
            if (j <= 0.0) continue;
            OnStruckBy(p, hit, j);
        }
    }

    /// <summary>What a strike costs the aeroplane: the moment it puts on the joint that holds the struck component
    /// on, against that joint's own ultimate (<see cref="WreckStrike.Severs"/>). Over it, the component goes — which
    /// spawns a piece of its own, so a thrown blade taking an elevator off is two things in the air, both falling.
    /// Under it, the blow is reported and nothing is removed: there is no partial-damage channel for a strike yet,
    /// and inventing one would be a table of outcomes rather than the numbers Ty asked for.</summary>
    private void OnStruckBy(WreckPiece piece, WreckStrike.Hit hit, double impulseNs)
    {
        Joint? joint = null;
        foreach (Joint jt in HydroModel().Joints)
            foreach (WreckAtom a in jt.Atoms)
                if (a == hit.Atom) { joint = jt; break; }
        LastStrike = (piece.Name, hit.Atom, hit.EnergyJ, impulseNs, joint?.UltimateNm ?? 0.0);
        if (joint is null || !WreckStrike.Severs(impulseNs, hit.AtBody, joint)) return;
        Aircraft.LoseComponent(joint.Component);
    }

    /// <summary>The last strike on the airframe by one of its own departed pieces (name, what it hit, the energy and
    /// impulse it arrived with, and the ultimate it was judged against). Probes and tests read it.</summary>
    public (string Piece, WreckAtom Atom, double EnergyJ, double ImpulseNs, double UltimateNm)? LastStrike { get; private set; }

    /// <summary>
    /// THROW A PROPELLER BLADE. Ty 2026-11-11: a 25 % over-rev held three cumulative seconds "throws a single prop
    /// blade on a cs prop and 50% of a single prop blade on a fixed pitch prop", and "Yes thrown blade also follows
    /// physics and causes damage".
    ///
    /// Everything about it is derived and nothing is chosen. The MASS is Ty's blade rule inverted out of the
    /// authored propeller inertia; WHERE it was is the shaft angle the sim has been integrating all along; HOW FAST
    /// it leaves is the tangential speed that centre of mass already had; and WHICH WAY is the tangent at that
    /// azimuth. From there it is an ordinary wreck piece: it tumbles on <see cref="DebrisAero"/>'s flat-plate
    /// forces, it is tested against the airframe every step by <see cref="StepStrikes"/>, and it falls until it
    /// reaches ground or water. The same over-rev at a different crank angle throws it somewhere else.
    ///
    /// It does NOT take the engine with it. The unbalance it leaves is enormous and computable
    /// (<see cref="FlyingGame.Core.Engine.EngineOverspeed.ThrownBladeUnbalanceN"/> — 225 kN on the Pitts), but what
    /// it has to be compared against is the engine MOUNT's limit, and that is an open question this project has
    /// deliberately not answered. Separating the engine on an invented limit would be the assumed baseline all over
    /// again, so the load is computed and left for Ty.
    /// </summary>
    public WreckPiece? ThrowPropellerBlade(double shaftRpm, double shaftAngleRad)
    {
        if (Aircraft.Config.Propulsion is not { PropDiameterM: > 0, PropInertia: > 0 } p) return null;
        bool cs = p.ConstantSpeed;
        double bladeKg = FlyingGame.Core.Engine.EngineOverspeed.BladeMassKg(p.PropInertia, p.PropDiameterM, p.PropBladeCount);
        double massKg = cs ? bladeKg : 0.5 * bladeKg;
        if (massKg <= 0.0) return null;

        Vec3 hub = new(p.HubXM, 0.0, p.ThrustLineZ);
        Vec3 radius = FlyingGame.Core.Engine.EngineOverspeed.ThrowRadiusBody(shaftAngleRad, p.PropDiameterM);
        if (!cs) radius *= 1.5;                               // the OUTER half's centre of mass sits further out
        double speed = FlyingGame.Core.Engine.EngineOverspeed.ThrowSpeedMs(p.PropDiameterM, shaftRpm, cs);
        Vec3 velocity = FlyingGame.Core.Engine.EngineOverspeed.ThrowDirectionBody(shaftAngleRad, p.RotationSign) * speed;

        // One box, the size of the piece that went: a blade's span along its own radius, a chord and a thickness.
        double span = cs ? 0.5 * p.PropDiameterM : 0.25 * p.PropDiameterM;
        var e = new HydroElement("prop-blade", WreckAtom.Nose, hub + radius,
                                 new Vec3(0.02, 0.5 * span, 0.5 * span * 0.18), 0.5 * span * 0.18 * 0.04, massKg, 0.0);
        WreckPiece piece = Wreck.Add("prop-blade", AirframeComponent.Propeller, new List<HydroElement> { e },
                                     Aircraft.Config.Mass.CgVec(), Aircraft.State, velocity);
        Aircraft.ShedMass(massKg, hub + radius);              // the aeroplane is that much lighter, at that station
        LastThrownBlade = (massKg, speed, shaftAngleRad % (2.0 * System.Math.PI));
        PieceSpawned?.Invoke(piece);

        // AND THEN THE MOUNT. The blade leaves a rotating unbalance of m·r·omega² and the mount is asked to carry it
        // (FlyingGame.Core.Engine.EngineOverspeed.MountFails). It cannot, by one to two orders of magnitude — which
        // is why the limit's exact value does not matter and why this can ship on a DERIVED one; the reasoning is
        // written out on MountLimitG. Ty 2026-11-11: "with accurate physics that will almost always result in engine
        // separation from the airplane but should be based on the loads and structural limits".
        double unbalance = FlyingGame.Core.Engine.EngineOverspeed.ThrownBladeUnbalanceN(
            p.PropInertia, p.PropDiameterM, p.PropBladeCount, shaftRpm, cs);
        LastUnbalanceN = unbalance;
        if (FlyingGame.Core.Engine.EngineOverspeed.MountFails(unbalance, InstalledEngineKg())) SeparateEngine();
        return piece;
    }

    /// <summary>The rotating unbalance the last thrown blade left, N — what the mount was asked to carry.</summary>
    public double LastUnbalanceN { get; private set; }

    /// <summary>The installed engine's mass, kg: the wreck model's own engine box, which is the same mass the
    /// flooding and break-up models carry for it rather than a second estimate.</summary>
    private double InstalledEngineKg()
    {
        foreach (HydroElement e in HydroModel().Elements) if (e.Name == "engine") return e.MassKg;
        return HydroModel().EngineDryKg * 1.4;
    }

    /// <summary>
    /// THE ENGINE LEAVES THE AEROPLANE. Ty 2026-11-11: "The engine and prop while separating should follow an
    /// accurate flight path and if that path contacts the airframe it should damage it appropriately."
    ///
    /// It gets no kick of its own. It leaves at the velocity it already had (v + ω × r at its own station), and what
    /// carries it AFT down the fuselage is the aeroplane continuing to fly forward — which is the whole of why it
    /// does not fall clear, and which is measured in WreckStrikeTests: a J-3's engine released at the nose strikes
    /// the Cabin. It is marked thrown so it is strike-tested; from there it is an ordinary wreck piece and falls
    /// until it reaches ground or water.
    ///
    /// AND THE AEROPLANE IS NOW A DIFFERENT AEROPLANE. Taking the engine off the nose of a light single is an
    /// enormous AFT CG shift, not merely a mass change, and ShedMass carries both: the mass, the parallel-axis share
    /// of the inertia, and the CG moved away from where it was (Aircraft.CgShiftBody enters the force sum as the
    /// remaining weight's moment). That shift, not the separation, is what happens to the pilot next — it is
    /// measured in ThrownBladeTests.
    /// </summary>
    public WreckPiece? SeparateEngine()
    {
        if (EngineGone) return null;
        HydroElement? src = null;
        foreach (HydroElement e in HydroModel().Elements) if (e.Name == "engine") { src = e; break; }
        if (src is null) return null;
        EngineGone = true;
        WreckPiece piece = Wreck.Add("engine", AirframeComponent.Propeller, new List<HydroElement> { src.Clone() },
                                     Aircraft.Config.Mass.CgVec(), Aircraft.State, Vec3.Zero, thrown: true);
        Aircraft.ShedMass(src.MassKg, src.Centre);
        PieceSpawned?.Invoke(piece);
        return piece;
    }

    /// <summary>The engine has left the airframe (an over-rev threw a blade and the mount could not carry it).</summary>
    public bool EngineGone { get; private set; }

    /// <summary>The last blade this aeroplane threw: its mass, the speed it left at and the azimuth it left from.</summary>
    public (double MassKg, double SpeedMs, double AzimuthRad)? LastThrownBlade { get; private set; }

    /// <summary>Is this piece outside every airframe box (expanded by its own reach)? Once it is, it has separated,
    /// and from then on it can strike the aeroplane like anything else in the air.</summary>
    private static bool Clear(WreckPiece p, RigidBodyState s, Vec3 cg, IReadOnlyList<HydroElement> airframe)
    {
        Vec3 pb = s.Attitude.Conjugate().Rotate(p.State.Position - s.Position) + cg;
        foreach (HydroElement e in airframe)
        {
            Vec3 d = pb - e.Centre;
            if (System.Math.Abs(d.X) <= e.Half.X + p.ReachM &&
                System.Math.Abs(d.Y) <= e.Half.Y + p.ReachM &&
                System.Math.Abs(d.Z) <= e.Half.Z + p.ReachM) return false;
        }
        return true;
    }

    private readonly HashSet<int> _clear = new();
    /// <summary>How far the airframe reaches from its CG, m — the broad phase's radius, built once.</summary>
    private double _airframeReachM;
    private AirframeHydroModel? _hydroModel;
    private AirframeHydroModel HydroModel() => _hydroModel ??= AirframeHydroModel.Build(Aircraft.Config, Aircraft.Load);
    private IReadOnlyList<HydroElement> AirframeBoxes() => HydroModel().Elements;

    /// <summary>Advance the wreck and report the airframe's flooding. Call once per frame/step with the elapsed time.</summary>
    public void Step(double dt)
    {
        if (_subscribed && !_wingsHandled && Aircraft.Structure.WingsFailed) OnWingsFailed();
        StepStrikes(dt);
        Wreck.Advance(dt);
        AirframeFlooding h = Aircraft.Hydro;
        if (!h.EverWet) return;
        _floodEscaped += h.TakeEscapedFreeM3() + (Aircraft.FloatFlood?.TakeEscapedFreeM3() ?? 0);
        _floodClock += dt;
        if (_floodClock + 1e-9 < ReportIntervalS) return;
        double waterInside = h.WaterInsideM3 + (Aircraft.FloatFlood?.WaterKg ?? 0) / WaterPhysics.RhoWater;
        if (!double.IsNaN(h.WaterUp) && (waterInside > 1e-4 || _floodEscaped > 0))   // in the water AND flooding
        {
            RigidBodyState s = Aircraft.State;
            double reserveKg = WaterPhysics.RhoWater * (h.ReserveVolumeM3 + (Aircraft.FloatFlood is { } ff ? ff.EnvelopeM3 - ff.WaterKg / WaterPhysics.RhoWater : 0))
                               - Aircraft.MassProperties.MassKg;
            var fs = new FloodState(-1, s.Position, s.Position.Z + h.WaterUp, _floodEscaped / _floodClock,
                h.TrappedAirFreeM3 + (Aircraft.FloatFlood?.TrappedAirM3 ?? 0), reserveKg < 0);
            LastAirframeFlood = fs;
            CrashEvents.RaiseFlood(fs);
        }
        _floodClock = 0; _floodEscaped = 0;
    }
}

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
    private readonly bool _subscribed;
    private bool _wingsHandled;
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
        PieceSpawned?.Invoke(p);
        return p;
    }

    /// <summary>Advance the wreck and report the airframe's flooding. Call once per frame/step with the elapsed time.</summary>
    public void Step(double dt)
    {
        if (_subscribed && !_wingsHandled && Aircraft.Structure.WingsFailed) OnWingsFailed();
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

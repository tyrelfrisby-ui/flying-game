using System;
using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Sim;

/// <summary>
/// FLUTTER, as a state the aeroplane carries. <see cref="FlyingGame.Core.Aero.Flutter"/> holds the laws and the two
/// shapes that are mine rather than Ty's; this holds the arming, the excitation, the accumulator and what it costs.
///
/// ARMED AND INITIATED ARE TWO STATES, and that is the physics rather than bookkeeping. Ty 2026-11-11: "'arm
/// flutter' at 1.2 vd but don't initiate it until either 1.3 vd [later: "Make it a 1.35 fallback"] or a control
/// input of over 1 degree per second rate change and then make the control that exceeded the rate change the control
/// that flutters so it tends to be the control moved unless they are held very still and then it is random". A
/// flutter mode above its boundary is unstable but still needs a disturbance to grow from; moving a control IS that
/// disturbance, and holding everything still leaves only ambient excitation, which is what the 1.35 Vd fallback
/// stands for.
///
/// THE RATE IS READ OFF THE SURFACE, NOT THE STICK. This model has free-elevator behaviour and hinge moments, so the
/// commanded input and the actual deflection are different signals, and the physical excitation is the surface's own
/// motion. <see cref="Aircraft.CurrentDeflections"/> is the surface.
///
/// SLOWING DOWN STOPS THE VIBRATION AND KEEPS THE DAMAGE. Ty did not say what happens on the way back down, so this
/// is my reading of his spec and is flagged as such: below the boundary the mode is damped again so the shaking
/// stops, but the accumulated fatigue and the lowered ultimate strength remain, because he has already said the
/// structure is compromised. It RE-ARMS on the next excursion past 1.2 Vd and needs a fresh excitation. Both are
/// cheap for him to overturn.
/// </summary>
public sealed partial class Aircraft
{
    private bool _flutterArmed, _flutterInitiated;
    private AirframeComponent _flutterSurface;
    private double _flutterIndex;                 // 0 .. the structure's own life; fatigue accumulated this flight
    private double _surfaceLife = double.NaN, _structureLife = double.NaN;
    private Random? _flutterRng;
    private ControlDeflections _lastSurfaces;
    private bool _lastSurfacesValid;
    private readonly List<(AirframeComponent Comp, double Due)> _pendingSheds = new();
    private double _flutterClock;

    /// <summary>The seed for every flutter draw, so a failure can be flown twice and a test can pin it. Set before
    /// the aeroplane first exceeds 1.2 Vd; it is read once, when the draws are made.</summary>
    public int FlutterSeed { get; set; } = 20261111;

    /// <summary>Past 1.2 Vd: the mode is unstable but nothing has excited it yet.</summary>
    public bool FlutterArmed => _flutterArmed;

    /// <summary>Something excited it and it is now growing. The surface it is growing on is
    /// <see cref="FlutterSurface"/>.</summary>
    public bool FlutterInitiated => _flutterInitiated;

    /// <summary>Which surface is fluttering (<see cref="AirframeComponent.Fuselage"/> when none is).</summary>
    public AirframeComponent FlutterSurface => _flutterSurface;

    /// <summary>Fatigue accumulated this flight, in lives of the structure that carries the fluttering surface:
    /// 1 is the point Ty names, where its ultimate strength is down to 1 g.</summary>
    public double FlutterIndex => _flutterIndex;

    /// <summary>What the pilot hears and feels right now: 0 Hz and 0 g when nothing is fluttering, otherwise the
    /// frequency falling and the amplitude rising as it diverges. The ONLY warning he gets.</summary>
    public double FlutterHz => _flutterInitiated && _flutterShaking ? Flutter.FrequencyHz(Math.Min(1.0, _flutterIndex)) : 0.0;
    public double FlutterG => _flutterInitiated && _flutterShaking ? Flutter.AmplitudeG(Math.Min(1.0, _flutterIndex)) : 0.0;
    private bool _flutterShaking;

    /// <summary>Equivalent airspeed over Vd, which is what every flutter speed is a multiple of. Vd is 1.1 x Vne
    /// (CAS) — Ty's own 2026-11-09 convention; nothing in this project authors a Vd.</summary>
    public double SpeedOverVd
    {
        get
        {
            double vne = Config.Limits?.VneMs ?? 0.0;
            if (vne <= 0.0) return 0.0;
            double rho = Atmosphere.DensityAtAltitude(-State.Position.Z);
            double eas = State.Velocity.Length * Math.Sqrt(Math.Max(1e-6, rho) / Atmosphere.SeaLevelDensityKgM3);
            return eas / (Flutter.VdOverVne * vne);
        }
    }

    /// <summary>A surface this flight's flutter has shed, so <see cref="CrashRelay"/> can fly it as a THROWN piece:
    /// a surface that lets go at 250 kt does not drift away from the aeroplane, it goes down the fuselage.</summary>
    public bool WasFlutterShed(AirframeComponent c) => _flutterShed.Contains(c);
    private readonly HashSet<AirframeComponent> _flutterShed = new();

    /// <summary>
    /// Once a step. Order matters: the ceiling first (it is absolute), then arming, then excitation, then the
    /// accumulator, then what the accumulated damage has done.
    /// </summary>
    private void StepFlutter(double dt)
    {
        if (dt <= 0.0 || Config.Limits is null) return;
        _flutterClock += dt;
        ReleasePendingSheds();

        double v = SpeedOverVd;
        _flutterShaking = false;

        // THE 1.5 Vd CEILING IS ITS OWN MECHANISM. Ty: "At either 1.5 vd the absolute limit where the wing or tail
        // structure fails regardless of the duration". Not the fatigue law reaching infinity — a separate hard
        // limit, and asking one law to do both would have made the exponent carry a job it cannot.
        if (v >= Flutter.CeilingVd) { FlutterStructuralFailure(); return; }

        if (v < Flutter.ArmVd)
        {
            // Below the boundary the mode is damped: the shaking stops, the damage stays, and it re-arms if he goes
            // back up. MY READING of his spec, flagged on the class.
            _flutterArmed = false;
            _flutterInitiated = false;
            return;
        }
        _flutterArmed = true;

        if (!_flutterInitiated)
        {
            AirframeComponent excited = SurfaceExceedingRate(dt);
            if (excited != AirframeComponent.Fuselage) { _flutterInitiated = true; _flutterSurface = excited; }
            else if (v >= Flutter.FallbackInitiateVd) { _flutterInitiated = true; _flutterSurface = RandomFlutterSurface(); }
            if (!_flutterInitiated) return;
            DrawLives();
        }
        else TrackSurfaces();

        _flutterShaking = true;
        _flutterIndex += Flutter.DamagePerSecond(v) * dt;
        ApplyFlutterDamage();
    }

    /// <summary>
    /// THE TWO LIVES, DRAWN INDEPENDENTLY. Ty: "either the control surface separates or the structure fails. This
    /// should be random ie the fatigue life of one is better than the other at random but the idea is that the
    /// equivalent exposure at 1.35 vd at 1 minute equals failure on the weaker of the two."
    ///
    /// So the calibration is the WEAKER of two draws, which decides where they are centred rather than leaving it to
    /// taste: for two independent uniform draws of half-width s, the expected minimum sits s/3 below their centre.
    /// Centring each at 1 + s/3 therefore makes the expected weaker life exactly 1 — Ty's minute at 1.35 Vd — with
    /// no fitting. s = 0.3 gives lives on [0.8, 1.4] and a weaker life averaging 1.00.
    /// </summary>
    private void DrawLives()
    {
        _flutterRng ??= new Random(FlutterSeed);
        const double s = LifeSpread;
        double centre = 1.0 + s / 3.0;
        _surfaceLife = centre + (2.0 * _flutterRng.NextDouble() - 1.0) * s;
        _structureLife = centre + (2.0 * _flutterRng.NextDouble() - 1.0) * s;
    }

    /// <summary>Half-width of each life's draw about its centre. The centring is forced by Ty's calibration (see
    /// <see cref="DrawLives"/>); only the WIDTH is a choice, and 0.3 makes the better of the two last up to 75 %
    /// longer than the worse, which is "the fatigue life of one is better than the other" without being a lottery.</summary>
    public const double LifeSpread = 0.3;

    /// <summary>The surface whose own deflection has moved faster than 1 deg/s, or Fuselage for none. The rate is
    /// the SURFACE's, not the stick's.</summary>
    private AirframeComponent SurfaceExceedingRate(double dt)
    {
        ControlDeflections now = CurrentDeflections;
        if (!_lastSurfacesValid) { _lastSurfaces = now; _lastSurfacesValid = true; return AirframeComponent.Fuselage; }
        double limit = Flutter.InitiateRateDegPerSec * Math.PI / 180.0 * dt;
        double dA = Math.Abs(now.AileronRad - _lastSurfaces.AileronRad);
        double dE = Math.Abs(now.ElevatorRad - _lastSurfaces.ElevatorRad);
        double dR = Math.Abs(now.RudderRad - _lastSurfaces.RudderRad);
        _lastSurfaces = now;
        // Whichever moved MOST, if any moved enough: the control he moved is the control that goes.
        if (dA < limit && dE < limit && dR < limit) return AirframeComponent.Fuselage;
        if (dA >= dE && dA >= dR) return AileronSide();
        return dE >= dR ? AirframeComponent.ElevatorLeft : AirframeComponent.Rudder;
    }

    private void TrackSurfaces() { _lastSurfaces = CurrentDeflections; _lastSurfacesValid = true; }

    /// <summary>Which aileron flutters: the side is drawn, because the stick moves both and only one can be first.</summary>
    private AirframeComponent AileronSide()
    {
        _flutterRng ??= new Random(FlutterSeed);
        return _flutterRng.NextDouble() < 0.5 ? AirframeComponent.AileronLeft : AirframeComponent.AileronRight;
    }

    /// <summary>"one of 3 control surfaces (random not always the same one) or the flaps if equipped" — and only the
    /// 172 has flaps in this fleet, so for the shipping types this is the three.</summary>
    private AirframeComponent RandomFlutterSurface()
    {
        _flutterRng ??= new Random(FlutterSeed);
        return _flutterRng.Next(3) switch
        {
            0 => AileronSide(),
            1 => AirframeComponent.ElevatorLeft,
            _ => AirframeComponent.Rudder,
        };
    }

    /// <summary>
    /// WHICH STRUCTURE CARRIES THE FLUTTERING SURFACE: aileron or flap -> the wing it is on, elevator -> the
    /// horizontal tail, rudder -> the fin.
    ///
    /// AND THE FIN HAS NO POOL, which is stated rather than worked around. StructuralState carries the two wings,
    /// the horizontal tail and the engine mount, and every one of them is loaded by the NORMAL load factor — there
    /// is no lateral load factor anywhere in this model, and no config publishes a fin limit load. A fin member
    /// would be a shadow nothing drove, which is the mistake the Wings property's own comment warns against. So
    /// rudder flutter sheds the rudder and does NOT weaken anything: half of what Ty specified, and the half that is
    /// missing needs a fin load signal and a fin limit, neither of which exists.
    /// </summary>
    private OverstressMember? CarryingStructure() => _flutterSurface switch
    {
        AirframeComponent.AileronLeft or AirframeComponent.AileronLowerLeft => Structure.LeftWing.Member,
        AirframeComponent.AileronRight or AirframeComponent.AileronLowerRight => Structure.RightWing.Member,
        AirframeComponent.ElevatorLeft or AirframeComponent.ElevatorRight => Structure.HorizontalTail,
        _ => null,                                   // the rudder: no fin pool to degrade
    };

    /// <summary>The <see cref="OverstressMember.Strength"/> at which the carrying structure breaks at 1 g — Ty's
    /// endpoint, taken from the ratio that member is really driven by rather than from a published limit.</summary>
    private double CarryingStrengthAtOneG()
    {
        bool tail = _flutterSurface is AirframeComponent.ElevatorLeft or AirframeComponent.ElevatorRight;
        double ratio = tail ? Structure.TailRatioAt(1.0, WingWeightRatio) : Structure.WingRatioAt(1.0, WingWeightRatio);
        return Flutter.StrengthAtOneG(ratio);
    }

    /// <summary>
    /// What the fatigue has done. TWO OUTCOMES RACING, and they are different KINDS of outcome since Ty's second
    /// message: the control surface separating is a discrete event with its own drawn life, while the structure's
    /// branch is not an event at all — it is the degradation, and the aeroplane breaks when the load it is actually
    /// pulling exceeds what is left. That is why a partly fluttered wing is a flyable aeroplane that has to be flown
    /// gently, and why the failure speed depends on the pilot rather than on the flutter alone.
    /// </summary>
    private void ApplyFlutterDamage()
    {
        if (double.IsNaN(_structureLife)) return;
        OverstressMember? m = CarryingStructure();
        if (m is not null)
            m.WeakenTo(Flutter.StrengthForIndex(_flutterIndex / Math.Max(1e-6, _structureLife), CarryingStrengthAtOneG()));
        if (_flutterIndex >= _surfaceLife && !_flutterShed.Contains(_flutterSurface) && !IsLost(_flutterSurface))
            ScheduleShed(_flutterSurface);
    }

    /// <summary>The structure fails outright: the 1.5 Vd ceiling, where duration does not come into it.</summary>
    private void FlutterStructuralFailure()
    {
        _flutterArmed = true; _flutterInitiated = true; _flutterShaking = true;
        if (_flutterSurface == AirframeComponent.Fuselage) { DrawLives(); _flutterSurface = RandomFlutterSurface(); }
        _flutterIndex = Math.Max(_flutterIndex, 1.0);
        OverstressMember? m = CarryingStructure();
        // Straight to the 1 g ultimate: level flight alone is now past it, so the existing break rules take it on
        // the next step without this having to break anything itself.
        m?.WeakenTo(Flutter.StrengthForIndex(1.0, CarryingStrengthAtOneG()));
        if (!_flutterShed.Contains(_flutterSurface) && !IsLost(_flutterSurface)) ScheduleShed(_flutterSurface);
    }

    /// <summary>
    /// STAGGERED BY 1 TO 5 SECONDS. Ty: "Stagger the aileron flap and elevator shedding by a random time ranging
    /// from one second to 5 seconds so it is less predictable". It is not a cosmetic queue: between one surface
    /// going and the next the aeroplane is genuinely flying with the first one gone — rolling, out of trim and being
    /// fought — so each shed takes effect when its own timer fires, against whatever state the aeroplane has got
    /// itself into by then.
    /// </summary>
    private void ScheduleShed(AirframeComponent c)
    {
        _flutterRng ??= new Random(FlutterSeed);
        foreach ((AirframeComponent q, double _) in _pendingSheds) if (q == c) return;
        _flutterShed.Add(c);
        _pendingSheds.Add((c, _flutterClock + 1.0 + 4.0 * _flutterRng.NextDouble()));
    }

    private void ReleasePendingSheds()
    {
        for (int i = _pendingSheds.Count - 1; i >= 0; i--)
        {
            if (_pendingSheds[i].Due > _flutterClock) continue;
            AirframeComponent c = _pendingSheds[i].Comp;
            _pendingSheds.RemoveAt(i);
            if (!IsLost(c)) LoseComponent(c);
        }
    }

    /// <summary>The structure's own drawn fatigue life, so a test can place the aeroplane at a KNOWN fraction of it
    /// rather than at a known index — the two differ by the draw, which is the point of the draw.</summary>
    public double FlutterStructureLifeForTesting
    {
        get { if (double.IsNaN(_structureLife)) DrawLives(); return _structureLife; }
    }

    /// <summary>The control surface's own drawn life, the other half of the race.</summary>
    public double FlutterSurfaceLifeForTesting
    {
        get { if (double.IsNaN(_surfaceLife)) DrawLives(); return _surfaceLife; }
    }

    /// <summary>Seconds until the next scheduled shed fires, or NaN when nothing is pending — so a test can see the
    /// stagger as a DELAY rather than inferring it from when the surface finally goes.</summary>
    public double NextFlutterShedInSec
    {
        get
        {
            double best = double.NaN;
            foreach ((AirframeComponent _, double due) in _pendingSheds)
                if (double.IsNaN(best) || due - _flutterClock < best) best = due - _flutterClock;
            return best;
        }
    }

    /// <summary>Test seam: put the aeroplane straight onto a rung without flying the dive that earns it.</summary>
    public void SetFlutterForTesting(AirframeComponent surface, double index)
    {
        _flutterArmed = true; _flutterInitiated = true; _flutterSurface = surface;
        if (double.IsNaN(_structureLife)) DrawLives();
        _flutterIndex = Math.Max(0.0, index);
        ApplyFlutterDamage();
    }
}

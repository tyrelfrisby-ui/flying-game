using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;

namespace FlyingGame.Sim;

/// <summary>
/// The engine's own state, apart from the throttle (Ty 2026-11-04, Quest: a stopped prop still yawed the Extra with the
/// throttle up; the Great Lakes' prop lever "moves but doesn't change rpm"; "the mixture is stuck at full rich").
///
/// Before this the sim had a throttle and nothing else: every prop effect came from the lever, and the only way to stop
/// the engine was to lose the propeller. Now:
///  - <see cref="MixtureLever"/> meters the fuel (<see cref="MixtureModel"/>): leaning changes power, rpm (fixed pitch)
///    and EGT, too lean runs rough and then quits, idle cut-off stops it. Pushed back in, it fires again
///    (<see cref="RelightSec"/>; a starter is assumed on the ground, a windmilling prop in the air).
///  - <see cref="PropLever"/> sets the governor of a constant-speed prop (full forward = the governed rpm it always had);
///    on a fixed-pitch type it does nothing.
///  - <see cref="Drive"/> is what the propeller model gets: power scale when running, windmill rpm (or a stopped prop)
///    when not. The tail's slipstream, torque, P-factor, spiral slipstream, gyro and rpm all come from it.
/// Both levers default to where they always sat (full rich, high rpm), so nothing flies differently until one moves.
/// </summary>
public sealed partial class Aircraft
{
    /// <summary>Mixture 0 (idle cut-off) … 1 (full rich). Set by the cockpit (the VR lever) or any other input.</summary>
    public double MixtureLever { get => _mixtureLever; set => _mixtureLever = System.Math.Clamp(value, 0.0, 1.0); }
    private double _mixtureLever = 1.0;

    /// <summary>Propeller (governor) lever 0 (low rpm, coarse) … 1 (high rpm, fine). Constant-speed props only.</summary>
    public double PropLever { get => _propLever; set => _propLever = System.Math.Clamp(value, 0.0, 1.0); }
    private double _propLever = 1.0;

    /// <summary>The left and right magneto switches (the Great Lakes' MAG L / MAG R toggles, Ty 2026-11-05): both on by
    /// default, as every type has always flown. One off: the mag drop (<see cref="Magnetos.PowerFactor"/>). Both off: no
    /// spark, the engine stops at once and the prop windmills; either back on with the prop turning and it fires again.</summary>
    public bool MagLeft { get; set; } = true;
    public bool MagRight { get; set; } = true;

    /// <summary>
    /// THE OIL SYSTEM HAS FAILED, and the governor with it. Ty 2026-11-10, after the oil-loss pitch rule went in:
    /// "Is the question a prop governor failure but working/running engine?" — which it was not, and that is the case
    /// that matters. An engine that has quit has no oil pressure for the obvious reason; a governor that loses its oil
    /// with the engine still making power is a FAILURE, and the two defaults (coarse for an aerobatic installation and
    /// every twin, fine for a standard single) exist precisely to decide what you are left flying when it happens:
    ///
    ///   aerobatic / twin, COARSE: the rpm sags, you cannot make rated power at low speed, the climb is poor — and the
    ///                             propeller can never overspeed, however far down you point it;
    ///   standard single, FINE:    full power is there at low speed, and the propeller overspeeds the moment you are
    ///                             fast or diving, with the engine itself driving it.
    ///
    /// Set it and the blades drift to <see cref="PropModel.OilLossPitchDeg"/> and STAY there: a fixed-pitch propeller
    /// at that angle, with the rpm solved from the torque balance like any other fixed blade. Nothing sets it on its own
    /// yet — Ty's answer on the cause was "Nothing — only my switch", with random failures queued separately — so this
    /// is the switch, and the CFI failure switchboard is what will pull it.
    /// </summary>
    public bool OilSystemFailed { get; set; }

    /// <summary>
    /// Oil pressure, psi: zero with the engine stopped or <see cref="OilSystemFailed"/>, else rising with rpm (about 35
    /// at idle, 80 in the cruise). It lives HERE rather than in the cockpit bridge, which used to compute it for the
    /// gauge alone, because the governor now reads it: a number drawn on an instrument and a number the aeroplane flies
    /// on must be one number or they will disagree exactly when it matters.
    /// </summary>
    public double OilPressPsi
    {
        get
        {
            if (OilSystemFailed || !EngineRunning) return 0.0;
            PropulsionConfig p = Config.Propulsion!;
            double max = p.MaxRpm > 100.0 ? p.MaxRpm : 2700.0;
            double rpm = _shaftRpm > 0.0 ? _shaftRpm : PropModel.RunningRpm(p, _throttle01, _propLever);
            return System.Math.Clamp(20.0 + 65.0 * System.Math.Min(1.0, rpm / (0.85 * max)), 0.0, 115.0);
        }
    }

    /// <summary>The governor can only move the blades while it has oil to move them with. Below this the blades go to
    /// the installation's default stop (<see cref="OilSystemFailed"/>); a real governor has lost control well before
    /// the gauge reads zero.</summary>
    public const double GovernorMinOilPsi = 12.0;

    /// <summary>True while the governor is in control of the blade angle.</summary>
    public bool GovernorHasOil => EngineRunning && !OilSystemFailed && OilPressPsi >= GovernorMinOilPsi;

    /// <summary>True when this type has a governor for <see cref="PropLever"/> to set.</summary>
    public bool HasPropLever => Config.Propulsion is { ConstantSpeed: true, PropDiameterM: > 0 };
    /// <summary>True when this type has a mixture for <see cref="MixtureLever"/> to meter (a piston; not a jet).</summary>
    public bool HasMixture => Config.Propulsion is { PropDiameterM: > 0, MaxPowerW: > 0 };

    /// <summary>Lowest governed rpm, rpm, with the blue lever full back: the fleet's 1,200 (Ty 2026-11-10). Was a
    /// fraction of MaxRpm until then; a governor's low stop belongs to the governor, not to the engine's red line.</summary>
    public const double FleetLowGovernedRpm = PropModel.FleetLowGovernedRpm;
    /// <summary>How long a re-fuelled engine takes to catch.</summary>
    public const double RelightSec = 1.0;

    private bool _mixtureDead;        // starved by the mixture (cut-off or too lean): not damage, it can fire again
    private double _relightT;
    private double _sigma = 1.0;      // density ratio at the last step
    private double _roughPhase;

    /// <summary>The engine is making power: it has a propeller, fuel, a mixture that fires and a spark (a magneto on).</summary>
    /// Whether it is making power comes from the RECORDING during a replay (<see cref="Aircraft.ReplayEngineRunning"/>):
    /// the physics does not run, so the live magnetos and fuel state belong to some other moment — after a crash, to a
    /// stopped engine, which silenced the whole playback. Having an engine at all is still the airframe's own question.
    public bool EngineRunning => Config.Propulsion is not null && Config.Propulsion.MaxPowerW > 0
                                 && (ReplayEngineRunning
                                     ?? (!_noseLost && !_mixtureDead && !_fuelStarved && Magnetos.Fires(MagLeft, MagRight)));

    /// <summary>The engine has quit for want of fuel (idle cut-off, a mixture too lean to fire, or the fuel valve shut:
    /// <see cref="FuelStarved"/>). Not damage.</summary>
    public bool EngineStarved => (_mixtureDead || _fuelStarved) && !_noseLost;

    /// <summary>The fuel/air equivalence ratio the lever gives here (1 = chemically correct).</summary>
    public double MixturePhi => MixtureModel.Phi(_mixtureLever, _sigma);

    /// <summary>0 smooth … 1 about to quit (lean).</summary>
    public double EngineRoughness => EngineRunning && HasMixture ? MixtureModel.Roughness(MixturePhi) : 0.0;

    /// <summary>Steady exhaust-gas temperature, °F (ambient-ish when not running).</summary>
    public double EgtF => EngineRunning && HasMixture ? MixtureModel.EgtF(MixturePhi, ShaftPower01) : 100.0;

    /// <summary>Shaft power the engine is really making, 0..~1 of MaxPowerW: the throttle × mixture × governor when it
    /// runs, 0 when it does not. What sound, smoke and anything else that means "how hard is it working" should read.</summary>
    public double ShaftPower01 => EngineRunning ? System.Math.Clamp(_throttle01, 0.0, 1.0) * PowerScale : 0.0;

    /// <summary>Running: shaft power relative to the throttle alone (mixture × governor × a lean engine's misfires).</summary>
    public double PowerScale
    {
        get
        {
            if (!EngineRunning) return 0.0;
            double s = HasMixture ? MixtureModel.PowerFactor(_mixtureLever, _sigma) : 1.0;
            if (HasPropLever) s *= GovernorPowerFactor();
            s *= Magnetos.PowerFactor(MagLeft, MagRight);
            double rough = EngineRoughness;
            if (rough > 0.0) s *= 1.0 - 0.15 * rough * (0.5 + 0.5 * System.Math.Sin(_roughPhase));
            return s;
        }
    }

    /// <summary>What the propeller model is given this step, carrying the shaft rpm when it is being integrated.</summary>
    public PropModel.PropDrive Drive
    {
        get
        {
            PropModel.PropDrive d = EngineRunning
                ? (PowerScale == 1.0 && _propLever == 1.0 ? PropModel.PropDrive.Running : PropModel.PropDrive.RunningAt(PowerScale, _propLever))
                : PropModel.PropDrive.Dead(_noseLost ? 0.0 : WindmillRpm);
            return _shaftRpm > 0.0 ? d.WithShaft(_shaftRpm, _bladePitchDeg) : d;
        }
    }

    // ---- the shaft, as a state ------------------------------------------------------------------------------------
    //
    // A PROPELLER'S RPM IS NOT A FUNCTION OF THE THROTTLE (Phase 1, Ty 2026-11-09). It is where the engine's torque
    // and the blade's balance, and it accelerates against the propeller's own inertia:
    //
    //     I * dw/dt = Q_engine(rpm, throttle, density) - Q_prop(blade, V, rpm, density)
    //
    // That one line is why a fixed-pitch aeroplane turns 2150 standing still and red-lines in a dive on the same
    // throttle, why the rpm sags when you raise the nose, and why it takes a moment to come back. Before this the sim
    // had TWO disagreeing throttle laws — PropModel.RunningRpm for the forces and the gyro, BaseEngineRpm for the
    // tachometer and the audio — so the number on the gauge was never the number the aeroplane flew on.
    //
    // Only types whose blade has been fitted (propulsion.bladePitch75Deg, from their static rpm) are integrated; the
    // rest keep the old law untouched, so the fleet stays flyable while the blades are fitted one at a time.
    private double _shaftRpm, _bladePitchDeg;
    /// <summary>Whether the shaft has ever been integrated for this aeroplane — so the spawn's initial condition and an
    /// in-flight restart can be told apart (see the seed in <see cref="StepShaft"/>).</summary>
    private bool _shaftPrimed;

    // ---- the compression strokes of a dead engine (Ty 2026-11-10) -------------------------------------------------
    //
    // "Use real compression strokes for the number of cyl of the engine so the prop lumps to a stop instead of
    //  stopping smoothly. To get prop windmilling again should take 10% under vne but occasional flips through a
    //  compression stroke can occur at speeds below (simulate slow leak down on each cyl ...)"
    //
    // The MEAN of turning the engine is PistonEngine.MotoringTorqueNm. These three hold the RIPPLE: where the crank
    // is, how well each cylinder is still holding its charge, and how hard a cylinder squarely on compression pushes
    // back. The crank angle has to be a state — a lump you cannot see coming is a lump that cannot stop anything.
    private double _crankRad;
    private double[]? _leak;              // 1 sealed .. 0 bled down, per cylinder
    private double[]? _leakTau;           // each cylinder's own bleed time, deliberately all different
    private double _compPeakNm = double.NaN;
    private bool _wasRunning;

    /// <summary>Where the crank is, rad, through the four-stroke's 720 degrees. A state because the lumps are.</summary>
    public double CrankAngleRad => _crankRad;

    /// <summary>Each cylinder's remaining seal, 1 .. 0 (empty before the first dead-engine step).</summary>
    public System.Collections.Generic.IReadOnlyList<double> CylinderSeal => _leak ?? System.Array.Empty<double>();

    /// <summary>
    /// HOW HARD A CYLINDER ON COMPRESSION PUSHES BACK, N·m at sea level — the engine's OWN gas load, out of its
    /// displacement and compression ratio and nothing else. See <see cref="PistonCompression.PeakNmFor"/>, which holds
    /// the expression and why Ty gave up the restart anchor this used to be solved from: the Pitts and the Extra carry
    /// the same AEIO-540 and were coming out at 99 and 683 N·m because their airframes have different Vne.
    ///
    /// The only thing still solved from an anchor is the engine's dry boundary friction
    /// (<see cref="PistonEngine.BoundaryExcessFor"/>), from his stall-speed anchor. The restart speed is now a
    /// measurement of the model (<see cref="PistonCompression.RestartSpeedMs"/>) rather than an input to it.
    /// Cached because the shaft integration asks every step.
    /// </summary>
    public double CompressionPeakNm
        => double.IsNaN(_compPeakNm) ? _compPeakNm = PistonCompression.PeakNmFor(Config) : _compPeakNm;

    /// <summary>The engine's resisting torque at the crank angle it is actually at, N·m (negative): the mean it takes
    /// to turn it over plus the compression ripple. This is what makes it lump.
    ///
    /// THE RIPPLE SCALES WITH AMBIENT PRESSURE, because a cylinder compresses the charge it DREW: at 10,000 ft it draws
    /// 69 % of the sea-level mass and its gas spring is 69 % as stiff. <see cref="CompressionPeakNm"/> is the sea-level
    /// figure — it is a cached per-type number and cannot know the altitude — so the lapse belongs here. The mean
    /// carries its own altitude term already, through PistonEngine's power curve.</summary>
    private double CrankingTorqueNm(double rpm, double pressureRatio)
    {
        PropulsionConfig p = Config.Propulsion!;
        double mean = PistonEngine.MotoringTorqueNm(p, rpm, _throttle01, pressureRatio);
        double ripple = PistonCompression.RippleFactor(_crankRad, Config.EngineCylinders, Config.EngineCompressionRatio, _leak);
        return mean + CompressionPeakNm * System.Math.Clamp(pressureRatio, 0.0, 1.3) * ripple;
    }

    /// <summary>A cylinder stopped on compression bleeds past its rings, each at its own rate, which is what lets a
    /// stopped propeller tick over one compression at a time instead of sitting still for ever. A cylinder away from
    /// its compression stroke re-seals, because it has drawn a fresh charge.</summary>
    private void BleedCylinders(double dt, double crankAdvanceRad)
    {
        int n = System.Math.Max(1, Config.EngineCylinders);
        if (_leak is null || _leak.Length != n)
        {
            _leakTau = PistonCompression.LeakTauSec(n, PistonCompression.SeedFor(Config.Id));
            _leak = new double[n];
            // EMPTY, not sealed. A cylinder's charge is something it DREW, and an engine that has been standing has
            // bled down everywhere - which is also the only initial condition that does not invent energy. Started
            // sealed, whichever cylinder happened to be mid-EXPANSION was holding a charge it had never compressed,
            // and its gas spring drove the propeller round from a standstill with no air on it at all. That is how
            // this was caught: an Extra parked on the ground with the mixture cut read 1.26 rpm.
        }
        PistonCompression.BleedOneStep(_leak, _leakTau!, _crankRad, dt, crankAdvanceRad);
    }

    /// <summary>The shaft rpm being integrated, or 0 when this type is still on the old throttle law.</summary>
    public double ShaftRpm => _shaftRpm;

    /// <summary>True when this aeroplane's propeller is solved rather than estimated.</summary>
    public bool HasFittedBlade => Config.Propulsion is { PropDiameterM: > 0, BladePitch75Deg: > 0 };

    /// <summary>Whether the sim integrates this aeroplane's shaft — <see cref="PropModel.IntegratesShaft"/>, which is
    /// the ONE copy of that predicate, because the rig has to ask the same question and a second copy is how the two
    /// came apart (see <see cref="PropModel.SteadyFlownDrive"/>).</summary>
    private bool SolvesShaft => PropModel.IntegratesShaft(Config.Propulsion);

    /// <summary>Advance the shaft one step against the torque the engine makes and the torque the blade takes.</summary>
    private void StepShaft(double dt, double airDensity)
    {
        PropulsionConfig? p = Config.Propulsion;
        if (p is null || !SolvesShaft || dt <= 0.0) { _shaftRpm = 0.0; return; }
        if (_noseLost) { _shaftRpm = 0.0; return; }               // the propeller has gone: nothing to turn

        double radius = 0.5 * p.PropDiameterM;
        double pressureRatio = Atmosphere.PressureAtAltitude(-State.Position.Z) / Atmosphere.SeaLevelPressurePa;
        // A RUNNING engine is always turning. A DEAD one may be stopped, and must be allowed to STAY stopped: seeding
        // it at 1 rpm every step was what made a stopped propeller read 1 rpm for ever instead of nothing.
        //
        // AND IT IS TURNING AT THE RPM THE TORQUE BALANCE GIVES, not at idle. The seed used to be
        // max(IdleRpm, 500) — an assertion that every flight begins with the engine at idle, which is false of every
        // in-air start: a spawn at 75 % in the cruise has its propeller at the cruise rpm already. That spool-up was
        // half of the J-3's hands-off divergence — the rig solved 2,128.8 rpm against a sim that started at 724 and
        // climbed — because for the second or two it lasts the torque reaction, the P-factor and the slipstream swirl
        // are all a different propeller's, and a spiral mode only needs a kick.
        // PropModel.SteadyDrive is both that torque balance and the one RigSolver.SteadyForces solves the rig
        // against, so taking the seed from it is what makes the spawn and the rig the same aeroplane by construction
        // rather than by agreement.
        //
        // A SPAWN IS AN INITIAL CONDITION; A RESTART IS A TRANSIENT. Only the first step this shaft is INTEGRATED on
        // takes the balance: an engine that has stopped in flight and caught again has to spool up from where it
        // was, which is what the idle seed is right for and what the dead-engine work measured. (StepShaft returns
        // before this whenever SolvesShaft is false, so a constant-speed type that LOSES its oil in the cruise also
        // arrives here unprimed and starts integrating from the rpm it was holding rather than dropping to idle.)
        if (_shaftRpm <= 0.0 && EngineRunning && !_shaftPrimed)
        {
            PropModel.PropDrive steady = PropModel.SteadyDrive(p, System.Math.Clamp(_throttle01, 0.0, 1.0), State.Velocity.X, airDensity);
            _shaftRpm = steady.ShaftRpm > 0.0 ? steady.ShaftRpm : System.Math.Max(p.IdleRpm, 500.0);
            if (steady.BladePitchDeg > 0.0 && _bladePitchDeg <= 0.0) _bladePitchDeg = steady.BladePitchDeg;
        }
        else if (_shaftRpm <= 0.0 && EngineRunning) _shaftRpm = System.Math.Max(p.IdleRpm, 500.0);
        // THE MOMENT IT QUITS, the propeller is still turning at whatever the engine was holding it at, and it spins
        // DOWN from there. Without this the integration began at zero the first step after the engine died and could
        // not break away past the compression peak, so cutting the mixture in the cruise stopped the propeller dead
        // instead of leaving it windmilling - and it only showed up on a constant-speed type, because a fixed-pitch
        // one was already integrating before the engine quit.
        else if (_shaftRpm <= 0.0 && !EngineRunning && _wasRunning)
            _shaftRpm = PropModel.RunningRpm(p, _throttle01, _propLever);
        _wasRunning = EngineRunning;
        _shaftPrimed = true;

        // A GOVERNED PROPELLER SENSES RPM, NOT TORQUE. A real governor is a flyweight head against a spring: rpm
        // above the setting opens a valve that drives the blade coarser, below it drives it finer, and it runs out
        // at the stops. So this is a rate controller on the rpm ERROR, clamped to the stops — which is both what the
        // hardware does and far cheaper than solving for the angle that balances the torque, which would cost forty
        // blade-element solutions every step.
        double pitch = p.BladePitch75Deg > 0 ? p.BladePitch75Deg
                     : p.ConstantSpeed ? System.Math.Max(1.0, PropModel.OilLossPitchDeg(p))
                     : 15.0;
        if (p.ConstantSpeed)
        {
            double fine = p.FineStopDeg > 0 ? p.FineStopDeg : 10.0;
            double coarse = p.CoarseStopDeg > fine ? p.CoarseStopDeg : fine + 15.0;
            if (_bladePitchDeg <= 0.0) _bladePitchDeg = System.Math.Clamp(0.5 * (fine + coarse), fine, coarse);
            if (GovernorHasOil)
            {
                double commanded = GovernedRpmAt(_propLever);
                double error = (_shaftRpm - commanded) / System.Math.Max(1.0, commanded);
                _bladePitchDeg += System.Math.Clamp(error * GovernorGainDegPerSec, -GovernorRateDegPerSec, GovernorRateDegPerSec) * dt;
            }
            else
            {
                // NO OIL PRESSURE: the blades go to whichever stop this installation's spring takes them to, in about
                // two seconds. Ty 2026-11-10 — an aerobatic single and every twin go COARSE, a standard single FINE;
                // see PropModel.OilLossPitchDeg for which way each type is plumbed and why.
                double stop = PropModel.OilLossPitchDeg(p);
                if (stop <= 0.0) stop = fine;
                _bladePitchDeg += (stop - _bladePitchDeg) * (1.0 - System.Math.Exp(-dt / 2.0));
            }
            _bladePitchDeg = System.Math.Clamp(_bladePitchDeg, fine, coarse);
            pitch = _bladePitchDeg;
        }

        var g = new BladeElement.Geometry(p.PropBladeCount, radius, pitch, p.ActivityFactor);
        double qProp = BladeElement.Solve(g, State.Velocity.X, 0.0, _shaftRpm, airDensity).TorqueNm;
        // A DEAD ENGINE IS NOT A FREE SHAFT. Ty 2026-11-10: "You are not accounting for the cranking resistance of the
        // engine". It takes real torque to turn a piston engine over, the power curve's own friction intercept already
        // says how much, and without it a windmilling propeller accelerated until only the blade's own drag stopped it.
        double qEngine;
        // THE IDLE STOP IS NOT A CLOSED THROTTLE (PistonEngine.IdleBleedThrottle): a solved propeller asks the engine
        // for torque, and a lever making literally zero power let the shaft decay to friction — the J-3 idled at 89.8
        // rpm against its published 700. The floor is the throttle that holds IdleRpm on THIS blade, so it costs
        // nothing above idle and makes IdleRpm mean something again.
        if (EngineRunning) qEngine = PistonEngine.TorqueNm(p.MaxPowerW, p.MaxRpm, _shaftRpm,
                                                          System.Math.Max(_throttle01 * PowerScale, IdleBleed(p, g, pressureRatio, airDensity)),
                                                          pressureRatio);
        else
        {
            // A DEAD ENGINE IS NOT A FREE SHAFT, and it is not a smooth one either. Ty 2026-11-10: "You are not
            // accounting for the cranking resistance of the engine", then "Use real compression strokes for the number
            // of cyl of the engine so the prop lumps to a stop instead of stopping smoothly".
            BleedCylinders(dt, _shaftRpm * 2.0 * System.Math.PI / 60.0 * dt);
            qEngine = CrankingTorqueNm(_shaftRpm, pressureRatio);
        }
        double inertia = System.Math.Max(0.05, p.PropInertia);
        double omega = _shaftRpm * 2.0 * System.Math.PI / 60.0;
        omega += (qEngine - qProp) / inertia * dt;
        // IT CAN STOP, and when it is stopped the air cannot pull it BACKWARDS through the engine: clamped at zero, so
        // a propeller held against a compression simply sits there until that cylinder bleeds down enough to let it by.
        omega = System.Math.Max(0.0, omega);
        _shaftRpm = System.Math.Min(omega * 60.0 / (2.0 * System.Math.PI), p.MaxRpm * 1.6);
        // The crank only moves if the shaft does, and it is what the next step's lump is measured from.
        _crankRad = (_crankRad + omega * dt) % (4.0 * System.Math.PI);
    }

    /// <summary>The idle-stop throttle for the blade as it currently stands, re-solved only when the blade angle or
    /// the ambient pressure has moved enough to matter (the bisection costs ~28 blade solves, and this is a hot
    /// path). See <see cref="PistonEngine.IdleBleedThrottle"/>.</summary>
    private double _idleBleed = -1.0, _idleBleedPitch = double.NaN, _idleBleedDelta = double.NaN;
    private double IdleBleed(PropulsionConfig p, in BladeElement.Geometry g, double pressureRatio, double airDensity)
    {
        if (_idleBleed >= 0.0
            && System.Math.Abs(g.PitchAt75Deg - _idleBleedPitch) < 0.25
            && System.Math.Abs(pressureRatio - _idleBleedDelta) < 0.01) return _idleBleed;
        _idleBleed = PistonEngine.IdleBleedThrottle(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, pressureRatio, airDensity);
        _idleBleedPitch = g.PitchAt75Deg; _idleBleedDelta = pressureRatio;
        return _idleBleed;
    }

    /// <summary>Governor response: degrees of blade per second per unit of fractional rpm error, and the rate the
    /// head can actually drive the blade at. A real governor catches a throttle movement in a second or two, and the
    /// lag is what lets the rpm overshoot briefly instead of being pinned to the setting.</summary>
    private const double GovernorGainDegPerSec = 220.0, GovernorRateDegPerSec = 12.0;

    /// <summary>
    /// How fast the air turns a dead engine's prop (0 when it is too slow to, or there is no prop).
    ///
    /// A constant-speed propeller whose stops are fitted is SOLVED instead of assumed: its blades are on the stop the
    /// oil loss left them on, and the rpm is where that blade's own torque comes to zero. The flat J = 1 assumption
    /// behind MixtureModel.WindmillRpm cannot tell a fine stop from a coarse one, and that difference is the whole
    /// point of the oil-loss rule — a fine blade is driven hard and spins up, a coarse one barely turns. Everything
    /// else keeps the old assumption, which is all it has.
    ///
    /// AND ON A FITTED TYPE, NaN MEANS STOPPED — not "no answer" (2026-11-10, second pass). PropModel.Windmilling
    /// returns NaN both for a type it does not apply to and for a blade the air cannot drive at all, and this fell
    /// through to the flat J = 1 assumption in BOTH cases. With the dry boundary friction solved against Ty's stall
    /// anchor the second case now happens in ordinary flight — the Extra's coarse stop cannot be driven at 50 m/s at
    /// 1,500 m — and a propeller that is standing still was reading 1,498 rpm on the tachometer. A fitted type that
    /// gets NaN has a stopped propeller; only an unfitted one still has to guess.
    /// </summary>
    public double WindmillRpm
    {
        get
        {
            if (Config.Propulsion is not { PropDiameterM: > 0 } p || _noseLost) return 0.0;
            double v = State.Velocity.Length;
            double rho = Atmosphere.DensityAtAltitude(-State.Position.Z);
            double solved = PropModel.WindmillRpm(p, v, rho);
            if (!double.IsNaN(solved)) return solved;
            return PropModel.OilLossPitchDeg(p) > 0.0 ? 0.0 : MixtureModel.WindmillRpm(v, p.PropDiameterM, p.MaxRpm);
        }
    }

    /// <summary>Constant-speed rpm for a governor lever position at the current throttle (the old law at lever 1).</summary>
    private double GovernedRpmAt(double lever) => PropModel.RunningRpm(Config.Propulsion!, _throttle01, lever);   // one law: the gyro uses it too

    /// <summary>At the same manifold pressure a piston's power goes with its rpm: the governor lever's share.</summary>
    private double GovernorPowerFactor()
    {
        double full = GovernedRpmAt(1.0);
        return full > 1.0 ? GovernedRpmAt(_propLever) / full : 1.0;
    }

    /// <summary>Once a step, before the forces: the density the mixture meets, the misfire phase, and whether the
    /// engine quits or catches again.</summary>
    private void UpdateEngine(double dt, double airDensity)
    {
        // SimProfile only: the engine's own state, and inside it the shaft integration, which on a type with a fitted
        // blade solves a propeller disc every step (Scope.Shaft). Off, Begin() is one thread-static read.
        long tEngine = SimProfile.Begin();
        UpdateEngineInner(dt, airDensity);
        SimProfile.End(SimProfile.Scope.Engine, tEngine);
    }

    private void UpdateEngineInner(double dt, double airDensity)
    {
        _sigma = airDensity / Atmosphere.SeaLevelDensityKgM3;
        long tShaft = SimProfile.Begin();
        StepShaft(dt, airDensity);          // the propeller's rpm is a state, not a throttle position
        SimProfile.End(SimProfile.Scope.Shaft, tShaft);
        if (!HasMixture || _noseLost) return;
        _roughPhase += dt * 2.0 * System.Math.PI * 3.1;   // a lean misfire's beat, a few times a second
        if (_roughPhase > 1e4) _roughPhase -= 2.0 * System.Math.PI * 1000.0;
        double phi = MixturePhi;
        if (!_mixtureDead)
        {
            if (!MixtureModel.Fires(_mixtureLever, _sigma)) { _mixtureDead = true; _relightT = 0.0; }
            return;
        }
        bool fuel = _mixtureLever > MixtureModel.CutoffLever && phi >= MixtureModel.PhiRelight;
        _relightT = fuel ? _relightT + dt : 0.0;
        if (_relightT >= RelightSec) { _mixtureDead = false; _relightT = 0.0; }
    }
}

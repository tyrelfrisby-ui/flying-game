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

    /// <summary>The shaft rpm being integrated, or 0 when this type is still on the old throttle law.</summary>
    public double ShaftRpm => _shaftRpm;

    /// <summary>True when this aeroplane's propeller is solved rather than estimated.</summary>
    public bool HasFittedBlade => Config.Propulsion is { PropDiameterM: > 0, BladePitch75Deg: > 0 };

    /// <summary>Advance the shaft one step against the torque the engine makes and the torque the blade takes.</summary>
    private void StepShaft(double dt, double airDensity)
    {
        PropulsionConfig? p = Config.Propulsion;
        if (p is null || !HasFittedBlade || dt <= 0.0) { _shaftRpm = 0.0; return; }
        if (_noseLost) { _shaftRpm = 0.0; return; }               // the propeller has gone: nothing to turn

        double radius = 0.5 * p.PropDiameterM;
        double pressureRatio = Atmosphere.PressureAtAltitude(-State.Position.Z) / Atmosphere.SeaLevelPressurePa;
        if (_shaftRpm <= 0.0) _shaftRpm = EngineRunning ? System.Math.Max(p.IdleRpm, 500.0) : System.Math.Max(1.0, WindmillRpm);

        // A GOVERNED PROPELLER SENSES RPM, NOT TORQUE. A real governor is a flyweight head against a spring: rpm
        // above the setting opens a valve that drives the blade coarser, below it drives it finer, and it runs out
        // at the stops. So this is a rate controller on the rpm ERROR, clamped to the stops — which is both what the
        // hardware does and far cheaper than solving for the angle that balances the torque, which would cost forty
        // blade-element solutions every step.
        double pitch = p.BladePitch75Deg > 0 ? p.BladePitch75Deg : 15.0;
        if (p.ConstantSpeed)
        {
            double fine = p.FineStopDeg > 0 ? p.FineStopDeg : 10.0;
            double coarse = p.CoarseStopDeg > fine ? p.CoarseStopDeg : fine + 15.0;
            if (_bladePitchDeg <= 0.0) _bladePitchDeg = System.Math.Clamp(0.5 * (fine + coarse), fine, coarse);
            if (EngineRunning)
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
        double qEngine = EngineRunning
            ? PistonEngine.TorqueNm(p.MaxPowerW, p.MaxRpm, _shaftRpm, _throttle01 * PowerScale, pressureRatio)
            : 0.0;
        double inertia = System.Math.Max(0.05, p.PropInertia);
        double omega = _shaftRpm * 2.0 * System.Math.PI / 60.0;
        omega += (qEngine - qProp) / inertia * dt;
        _shaftRpm = System.Math.Clamp(omega * 60.0 / (2.0 * System.Math.PI), 0.0, p.MaxRpm * 1.6);
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
    /// </summary>
    public double WindmillRpm
    {
        get
        {
            if (Config.Propulsion is not { PropDiameterM: > 0 } p || _noseLost) return 0.0;
            double v = State.Velocity.Length;
            double rho = Atmosphere.DensityAtAltitude(-State.Position.Z);
            double solved = PropModel.WindmillRpm(p, v, rho);
            return double.IsNaN(solved) ? MixtureModel.WindmillRpm(v, p.PropDiameterM, p.MaxRpm) : solved;
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
        _sigma = airDensity / Atmosphere.SeaLevelDensityKgM3;
        StepShaft(dt, airDensity);          // the propeller's rpm is a state, not a throttle position
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

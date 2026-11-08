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

    /// <summary>Lowest governed rpm, as a fraction of <c>MaxRpm</c> (a 2,700 rpm Lycoming's governor bottoms near 1,900).</summary>
    public const double LowGovernorFraction = PropModel.LowGovernorFraction;
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

    /// <summary>What the propeller model is given this step.</summary>
    public PropModel.PropDrive Drive =>
        EngineRunning ? (PowerScale == 1.0 && _propLever == 1.0 ? PropModel.PropDrive.Running : PropModel.PropDrive.RunningAt(PowerScale, _propLever))
                      : PropModel.PropDrive.Dead(_noseLost ? 0.0 : WindmillRpm);

    /// <summary>How fast the air turns a dead engine's prop (0 when it is too slow to, or there is no prop).</summary>
    public double WindmillRpm => Config.Propulsion is { PropDiameterM: > 0 } p && !_noseLost
        ? MixtureModel.WindmillRpm(State.Velocity.Length, p.PropDiameterM, p.MaxRpm) : 0.0;

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

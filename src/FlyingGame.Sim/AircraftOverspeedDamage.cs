using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;

namespace FlyingGame.Sim;

/// <summary>
/// WHAT THE OVER-REV DID TO THE ENGINE, as a state the aeroplane carries for the rest of the flight.
/// <see cref="EngineOverspeed"/> holds the ladder and the laws; this holds the memory and does the wiring.
///
/// IT ONLY MEANS ANYTHING BECAUSE THE OVER-REV NOW HAPPENS. Before 2026-11-11 the governor never ran out of blade
/// inside the envelope on any of the three constant-speed types — the Pitts needed 284 kt CAS at sea level against a
/// 184 kt Vne — so a damage model keyed on over-revving would have been dead code dressed as physics. The coarse
/// stops are now derived at Vd at 10,000 ft density altitude (BladeElement.GovernorStops, Ty's rule), the governor
/// runs out of blade exactly there, and past it the rpm goes. THAT is what this is attached to.
///
/// THE RPM IT WATCHES IS THE SHAFT'S, running or not. A propeller driving a dead engine past the red line in a dive
/// is over-revving it: the rings and the bearings do not know whether the cylinders are firing. The one thing that
/// stops the watch is seizure, because a seized engine has no rpm left to abuse.
/// </summary>
public sealed partial class Aircraft
{
    private double _overspeedPeakExcess;     // the worst over-rev this flight, as a fraction over the red line
    private double _bearingDamage01;         // 0 fresh .. 1 seized, once a bearing has been spun
    private bool _engineSeized;
    // CUMULATIVE seconds spent over each of the three mechanical rungs (Ty 2026-11-11 revised the ladder to gate on
    // time). They only count while the rpm is above their OWN threshold, so they nest: every second over 25 % is
    // also a second over 15 % and over 12 %. Four separate two-second excursions past 12 % add up to eight.
    private double _secondsOver12, _secondsOver15, _secondsOver25;

    /// <summary>The worst over-rev this flight, as a fraction over the red line (0.12 = 12 % over). It is a PEAK and
    /// it never comes back down: the engine has been hurt, and backing off does not unhurt it.</summary>
    public double OverspeedPeakExcess => _overspeedPeakExcess;

    /// <summary>The multiplier this flight's worst over-rev puts on every random engine failure's hazard rate
    /// (<see cref="EngineReliability"/>): 1, or 50 past 10 % over. Ty 2026-11-11.
    ///
    /// NOTHING CONSUMES IT YET, AND THAT IS NOT THIS FILE'S DOING. <see cref="EngineReliability"/> is a complete model
    /// — the table, the hazard, the draw — that has never been wired to a flying aeroplane; SessionSettings says so
    /// where it stores the player's reliability setting ("STORED ONLY: EngineReliability is not wired to the flying
    /// aeroplane yet"). When somebody draws failures in the step, this is the number to multiply the rate by, and the
    /// test beside it pins that it is 50 and that it persists.</summary>
    public double EngineUnreliabilityMultiplier => EngineOverspeed.UnreliabilityMultiplier(_overspeedPeakExcess);

    /// <summary>Cylinders with a ring broken by an over-rev: one, past 12 % over. They make no power, and the exhaust
    /// note is missing their pulse (EngineSynth renders a per-cylinder pulse train; <see cref="DeadCylinders"/> is
    /// what the bridge hands it).</summary>
    public int DeadCylinders => EngineOverspeed.BrokenRings(_secondsOver12);

    /// <summary>Cumulative seconds this flight spent over each mechanical rung's threshold — 12 %, 15 % and 25 % over
    /// the red line. They are what the rungs gate on, and they never come back down.</summary>
    public (double Over12, double Over15, double Over25) OverspeedSeconds => (_secondsOver12, _secondsOver15, _secondsOver25);

    /// <summary>
    /// A BLADE HAS GONE: three cumulative seconds over 25 % (Ty 2026-11-11). The whole blade on a constant-speed
    /// propeller, the outer half on a fixed-pitch one.
    ///
    /// WHAT IT DOES NOT YET DO IS LEAVE THE AEROPLANE, and that is deliberate rather than unfinished. Ty asked for
    /// the separation to follow "the loads and structural limits" and then for the departing engine to fly "an
    /// accurate flight path" and damage what it hits. Two of those three exist: <see cref="ThrownBladeUnbalanceN"/>
    /// computes the load from his own blade rule, and FlyingGame.Core.Wreck's WreckSim already flies a departed
    /// piece properly (6-DOF, flat-plate normal force with the centre-of-pressure walk that makes it tumble). The
    /// two that do NOT exist are the engine MOUNT's load limit - an open question, deliberately not installed - and
    /// any collision at all between a departed piece and the airframe it came off: IWreckWorld knows about water and
    /// ground and nothing else. Firing a separation on an invented mount limit would be the kind of assumed baseline
    /// this project keeps finding was wrong.
    /// </summary>
    public bool BladeThrown => EngineOverspeed.BladeThrown(_secondsOver25);

    /// <summary>The rotating unbalance a thrown blade leaves at the rpm the shaft is turning now, N. It is what a
    /// mount limit would have to be compared against; see <see cref="BladeThrown"/> for why nothing compares it yet.</summary>
    public double ThrownBladeUnbalanceN => BladeThrown && Config.Propulsion is { PropDiameterM: > 0 } p
        ? EngineOverspeed.ThrownBladeUnbalanceN(p.PropInertia, p.PropDiameterM, p.PropBladeCount, BaseEngineRpm, p.ConstantSpeed)
        : 0.0;

    /// <summary>A bearing has been spun by an over-rev past 15 %: from here the engine is on its way out and how long
    /// it takes is up to the pilot's right hand.</summary>
    public bool BearingSpun => EngineOverspeed.BearingSpun(_secondsOver15);

    /// <summary>How far a spun bearing has got, 0 fresh to 1 seized. Vibration, oil temperature and the power loss
    /// all read off this, which is why it is one number and not three.</summary>
    public double BearingDamage01 => _bearingDamage01;

    /// <summary>The engine has seized. Sudden stoppage: it is not running, it will not run again, and the propeller
    /// stops dead rather than windmilling (<c>StepShaft</c> returns at once).</summary>
    public bool EngineSeized => _engineSeized;

    /// <summary>Degrees F the destroyed bearing is adding to the oil temperature. The cockpit bridge owns the oil
    /// TEMPERATURE (it is a first-order state there); the sim owns what is heating it.</summary>
    public double OilTempRiseF => EngineOverspeed.OilTemperatureRiseF(_bearingDamage01);

    /// <summary>The shake at the pilot from a bearing on its way out, g — on top of whatever the propeller's own
    /// imbalance is doing (<see cref="PropVibrationG"/>).</summary>
    public double BearingVibrationG => EngineOverspeed.VibrationG(_bearingDamage01);

    /// <summary>What the damage costs in shaft power: the broken ring's cylinder, and the friction of the bearing
    /// destroying itself. A multiplier on <see cref="PowerScale"/>.</summary>
    private double DamagePowerFactor
    {
        get
        {
            double f = EngineOverspeed.PowerFactorForBrokenRings(DeadCylinders, Config.EngineCylinders);
            if (_bearingDamage01 > 0.0) f *= EngineOverspeed.PowerFactorForBearing(_bearingDamage01);
            return f;
        }
    }

    /// <summary>
    /// Once a step, after the shaft has been integrated: watch the rpm for an over-rev, remember the worst of it, and
    /// let a spun bearing get on with destroying itself at the rate the pilot's rpm and power set.
    /// </summary>
    private void StepOverspeedDamage(double dt)
    {
        if (_engineSeized || dt <= 0.0) return;
        if (Config.Propulsion is not { MaxRpm: > 0.0 } p) return;

        double excess = EngineOverspeed.Excess(BaseEngineRpm, p.MaxRpm);
        if (excess > _overspeedPeakExcess) _overspeedPeakExcess = excess;
        // The three mechanical rungs count TIME, cumulatively, each above its own threshold.
        if (excess > EngineOverspeed.BrokenRingExcess) _secondsOver12 += dt;
        if (excess > EngineOverspeed.SpunBearingExcess) _secondsOver15 += dt;
        if (excess > EngineOverspeed.ThrownBladeExcess) _secondsOver25 += dt;

        if (!BearingSpun) return;
        // THE LOAD IS WHAT THE ENGINE IS REALLY MAKING, not the lever: a dead or idling engine wears the bearing far
        // more slowly, and that is the whole of "a pilot that nurses it back has more time".
        _bearingDamage01 += EngineOverspeed.BearingDamagePerSecond(BaseEngineRpm, p.MaxRpm, ShaftPower01) * dt;
        if (_bearingDamage01 >= 1.0) { _bearingDamage01 = 1.0; _engineSeized = true; }
    }

    /// <summary>Test and scenario seam: put an engine straight onto a rung of the ladder without flying the dive that
    /// earns it — the peak over-rev and, since the rungs gate on time, the seconds it spent there. Used by
    /// OverspeedDamageTests and available to the CFI failure switchboard.</summary>
    public void SetOverspeedForTesting(double excess, double seconds = 0.0)
    {
        _overspeedPeakExcess = System.Math.Max(0.0, excess);
        double s = System.Math.Max(0.0, seconds);
        if (excess > EngineOverspeed.BrokenRingExcess) _secondsOver12 = s;
        if (excess > EngineOverspeed.SpunBearingExcess) _secondsOver15 = s;
        if (excess > EngineOverspeed.ThrownBladeExcess) _secondsOver25 = s;
    }
}

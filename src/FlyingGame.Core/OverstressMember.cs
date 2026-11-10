namespace FlyingGame.Core;

/// <summary>
/// One primary structure under the OVERSTRESS PATTERN (Ty 2026-11-05 06:28, verbatim in
/// ~/Developer/agent-lanes/prompts/ty-overstress-pattern-1105.md; docs/BEND-DAMAGE.md): the wings, the horizontal tail or
/// the engine mount, loaded by the load factor as a multiple r of its limit load.
///
///  • Flex. Nothing below the limit (r = 1); from there it flexes 20° per limit load: 10° at 1.5 (the ultimate load) and
///    20° at 2.0, where it snaps and departs. Visible, and it is the geometry the aero flies.
///  • Permanent set. Past 1.5 the structure yields: when the load comes off, 50 % of the bending at the peak stays
///    (a single pull to 2.0 leaves 10°). Below 1.5 it is fully elastic. Reloading the same way runs back up a straight line
///    from the set to the peak it came from, then on along the curve; a pull the other way bends it back by half of that
///    pull's bending. The set never exceeds half the bending at the break (10°).
///  • Weakening. Every occurrence past 1.5 takes 2 % per 0.1 of overage off the strength for the next one (1.6 → 2 %,
///    1.7 → 4 %, ... 2.0 → 10 %), compounding, and the whole curve shrinks with it: the limit, the 1.5 and the 2.0 are all
///    multiplied by <see cref="Strength"/>. So several pulls to 1.6 walk it down until it breaks below 1.5.
///
/// An occurrence starts when the effective load passes 1.5 and ends when it is back inside the limit (≤ 1.0), which is
/// when its set and its weakening are booked. The member sees the load through its own response
/// (<see cref="ResponseTauSec"/>), so a single integration spike or a gust tick bends nothing and breaks nothing.
/// Signs: + = the way positive g bends it. The caller decides what that is (wing tips up, tail tips down, engine nose down).
/// </summary>
public sealed class OverstressMember
{
    /// <summary>Flex per limit load past the limit, rad: 10° at 1.5, 20° at 2.0 (Ty).</summary>
    public const double FlexPerLimitRad = 20.0 * System.Math.PI / 180.0;
    /// <summary>Ty's "ultimate load": permanent deformation and weakening start past this multiple of the limit.</summary>
    public const double UltimateRatio = 1.5;
    /// <summary>Ty's breaking point: the structure snaps and departs past this multiple of the limit.</summary>
    public const double BreakRatio = 2.0;
    /// <summary>The share of the bending at the peak of an overstress that stays (Ty: 50 %).</summary>
    public const double PermanentShare = 0.5;
    /// <summary>Strength lost per 0.1 of peak overage past 1.5 (Ty: 2 % per 0.1).</summary>
    public const double WeakeningPerTenth = 0.02;
    /// <summary>What an overstress that UNDOES the member's set costs in strength, as a multiple of what the same
    /// overstress would cost bending it further the same way. 4 is the owner's number (2026-11-10).</summary>
    public const double ReversalFatigueFactor = 4.0;

    /// <summary>Is an overstress of this sign taking an existing set OUT? A member with no set cannot be
    /// straightened, so its first bend costs what it costs.</summary>
    public static bool Straightening(double setBefore, int sign) =>
        setBefore != 0.0 && sign != 0 && System.Math.Sign(setBefore) != sign;
    /// <summary>
    /// The structure's response time to load, s: it follows the load factor through a first-order lag of this time
    /// constant. A light aeroplane's wing bends in its first mode at roughly 3–8 Hz (τ = 1/2πf ≈ 0.02–0.05 s); the slow
    /// end is taken, the same 50 ms the wing failure used to wait out (the old FailureDwellSec) — long enough that one
    /// 5 ms RK4 stage or a gust tick does not snap a wing, short enough that a real pull does.
    /// </summary>
    public const double ResponseTauSec = 0.05;

    public string Name { get; }
    /// <summary>What is left of the original strength, 0..1: the whole curve is scaled by it.</summary>
    public double Strength { get; private set; } = 1.0;
    /// <summary>Permanent set, rad (signed).</summary>
    public double SetRad { get; private set; }
    /// <summary>Its shape now, rad from straight: the set plus the flex under the load.</summary>
    public double DeflectionRad { get; private set; }
    /// <summary>The flex under load on top of the set, rad.</summary>
    public double FlexRad => DeflectionRad - SetRad;
    /// <summary>The load the structure is feeling, multiples of the limit (signed), after its response lag.</summary>
    public double LoadRatio { get; private set; }
    /// <summary>The same against what is left of it: |LoadRatio| / Strength.</summary>
    public double EffectiveRatio => System.Math.Abs(LoadRatio) / Strength;
    /// <summary>Overstresses booked (excursions past 1.5 that came back).</summary>
    public int Overstresses { get; private set; }
    /// <summary>Largest effective ratio this member has felt (either sense).</summary>
    public double PeakEffectiveRatio { get; private set; }
    public bool Broken { get; private set; }
    /// <summary>Inside an overstress right now (past 1.5 and not yet back inside the limit).</summary>
    public bool Overstressed => _inEvent;

    private bool _inEvent;
    private int _eventSign;
    private double _eventPeak, _setBefore, _peakDeflection;

    public OverstressMember(string name) { Name = name; }

    /// <summary>The bending along the curve at effective ratio <paramref name="x"/> (≥ 0) of a straight member, rad.</summary>
    public static double CurveRad(double x) => x <= 1.0 ? 0.0 : FlexPerLimitRad * (x - 1.0);

    /// <summary>The weakening an overstress that peaked at effective ratio <paramref name="peak"/> leaves: 0.02 per 0.1 past 1.5.</summary>
    public static double WeakeningFor(double peak) => System.Math.Clamp(WeakeningPerTenth * (peak - UltimateRatio) / 0.1, 0.0, 0.5);

    /// <summary>
    /// Advance one step at load ratio <paramref name="ratio"/> (multiples of the limit on its own side, signed: positive
    /// g over the positive limit, negative g over the negative limit, negated). True on the step the member breaks.
    /// </summary>
    public bool Step(double ratio, double dt)
    {
        if (Broken) return false;
        double a = dt > 0 ? 1.0 - System.Math.Exp(-dt / ResponseTauSec) : 1.0;
        if (double.IsFinite(ratio)) LoadRatio += (ratio - LoadRatio) * a;
        double x = EffectiveRatio;
        int sign = LoadRatio >= 0 ? 1 : -1;
        PeakEffectiveRatio = System.Math.Max(PeakEffectiveRatio, x);
        if (_inEvent && sign != _eventSign) EndEvent();   // through zero inside one step: book the first, start afresh
        if (x > BreakRatio)
        {
            Broken = true;
            DeflectionRad = Loading(_inEvent ? _setBefore : SetRad, BreakRatio, sign);
            return true;
        }
        if (!_inEvent && x > UltimateRatio)
        {
            _inEvent = true; _eventSign = sign; _eventPeak = x; _setBefore = SetRad;
        }
        if (_inEvent)
        {
            if (x >= _eventPeak)
            {
                _eventPeak = x;
                _peakDeflection = Loading(_setBefore, x, sign);
                DeflectionRad = _peakDeflection;
            }
            else
            {
                // Coming off the peak: straight back down to where the set will be, reached at the limit.
                double setAfter = SetAfter(_setBefore, _eventPeak, _eventSign);
                double u = _eventPeak > 1.0 ? System.Math.Clamp((x - 1.0) / (_eventPeak - 1.0), 0.0, 1.0) : 0.0;
                DeflectionRad = setAfter + (_peakDeflection - setAfter) * u;
            }
            if (x <= 1.0) EndEvent();
            return false;
        }
        DeflectionRad = Loading(SetRad, x, sign);
        return false;
    }

    private void EndEvent()
    {
        SetRad = SetAfter(_setBefore, _eventPeak, _eventSign);
        // STRAIGHTENING YOUR WINGS COSTS FOUR TIMES THE LIFE (Ty 2026-11-10, with the bent-wing aileron limit):
        // "a bend that reduces bend angle like a hard push after bending wings up releases the travel limit but is
        // accounted for in fatigue and has a quadruple reduction in fatigue life so you can straighten your wings
        // out once but it will cost you significantly for the next stress".
        //
        // An overstress the other way takes the bend out (SetAfter, just above, already bends it back) and with it
        // the aileron's travel limit, which follows the ANGLE. The structure does not get that back: pulling a
        // bent spar straight works it far harder than bending it further the same way, so the strength this event
        // costs is multiplied.
        //
        // HERE, not in PlasticHinge: that hinge is also the one a ground strike turns, and a strike reverses many
        // times as it loads and recoils. Charging those four times each turned a tip strike that used to BEND a
        // J-3's wing into one that broke it (BendDamageTests.ABentWingRollsAndDragsInFlight) — and bend-before-
        // break is the behaviour he flew and confirmed on 2026-11-05. His sentence is about a flight overstress
        // undoing a flight overstress, which is exactly what this class is.
        Strength *= 1.0 - WeakeningFor(_eventPeak) * (Straightening(_setBefore, _eventSign) ? ReversalFatigueFactor : 1.0);
        Overstresses++;
        _inEvent = false;
        DeflectionRad = SetRad;
    }

    /// <summary>The set left by an overstress of sign <paramref name="sign"/> that peaked at <paramref name="peak"/>, from
    /// a member already set <paramref name="set0"/>: half the bending at the peak; a set the same way that is already as
    /// large stays; a pull the other way bends it back by half that pull's bending.</summary>
    public static double SetAfter(double set0, double peak, int sign)
    {
        double half = PermanentShare * CurveRad(peak);
        double s = set0 == 0.0 || System.Math.Sign(set0) == sign ? sign * System.Math.Max(System.Math.Abs(set0), half) : set0 + sign * half;
        double cap = PermanentShare * CurveRad(BreakRatio);
        return System.Math.Clamp(s, -cap, cap);
    }

    /// <summary>The shape of a member set <paramref name="set"/> loaded to effective ratio <paramref name="x"/> one way:
    /// the same way as its set, straight from the set (at the limit) up to the peak that left it, then along the curve;
    /// the other way (or straight), the set plus the curve's flex.</summary>
    public static double Loading(double set, double x, int sign)
    {
        if (set != 0.0 && System.Math.Sign(set) == sign)
        {
            double q = 1.0 + System.Math.Abs(set) / (PermanentShare * FlexPerLimitRad);   // the peak whose half-bending is |set|
            if (x >= q) return sign * CurveRad(x);
            double u = System.Math.Clamp((x - 1.0) / (q - 1.0), 0.0, 1.0);
            return set + sign * (CurveRad(q) - System.Math.Abs(set)) * u;
        }
        return set + sign * CurveRad(x);
    }

    /// <summary>Put a set in directly (scenarios, tests), as if left by an overstress the same way.</summary>
    public void Impose(double setRad) { SetRad = setRad; DeflectionRad = setRad; }
}

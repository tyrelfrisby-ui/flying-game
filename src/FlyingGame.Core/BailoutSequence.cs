namespace FlyingGame.Core;

/// <summary>Where a pilot gets out, which decides what NEGATIVE g does to him.</summary>
public enum EgressOpening
{
    /// <summary>No canopy or door on the model at all: an open cockpit. The way out is up.</summary>
    OpenTop,
    /// <summary>A bubble over the cockpit (a Pitts, an Extra, a glider). The way out is up.</summary>
    TopCanopy,
    /// <summary>A door in the side (a J-3, a 172). The way out is sideways, which is NOT where the g throws him.</summary>
    SideDoor,
}

/// <summary>
/// GETTING OUT IS PHYSICS, NOT AN ANIMATION. Ty 2026-11-10:
///
///   "For the bailed out pilot have the canopy/door pop off in one second the have the pilot release his seat belt at
///    1.5 seconds and try exiting. The g's will slow down his exit more positive/negative slower exit if it is a
///    negative g in a plane that has an open top accurately show him being flung out by the g as soon as he unbuckles,
///    make one g to zero g take 2 seconds with a 1 second penalty per g +- beyond +1/0"
///
///   "If it has a side door negative g is also slowing but if it has a top canopy the pilot drops or is 'ejected'"
///
/// So: <see cref="CanopyOffSec"/> the canopy or door goes; <see cref="HarnessOffSec"/> the harness comes off; then
/// he climbs out, and how long that takes is the load factor's business —
/// <see cref="ClimbOutSec"/> = 2 s anywhere between 0 and +1 g, plus a second for every g outside that band in
/// EITHER direction (so +2 g → 3 s, +3 g → 4 s, −1 g → 3 s, −2 g → 4 s). That is how his sentence is read here, and
/// it is read literally: the penalty is on the DISTANCE outside the 0..+1 band, not on |nz|.
///
/// THE EXCEPTION IS THE AEROPLANE'S OWN SHAPE. Under negative g an unbuckled man is not held in his seat — the
/// apparent gravity in the cockpit is pushing him toward the canopy rails at |nz| g. If the way out is UP (an open
/// cockpit or a bubble) that is the g doing the whole job, and he does not climb out at all: he is thrown, and
/// <see cref="Bailout"/> integrates him out on nothing but that acceleration, so WHEN he is clear and HOW FAST he
/// leaves are both consequences of the g and the <see cref="SillClearanceM"/> he has to clear. There is no timer in
/// that branch. If the way out is a SIDE DOOR the g is throwing him at the roof instead, so negative g only slows
/// him, exactly like positive g.
///
/// WHICH OPENING IS ALREADY KNOWN PER TYPE, with nothing added to any config: <see cref="OpeningFor"/> asks the one
/// geometric question <see cref="Vr.CanopySpec.IsDoor"/> already asks of the canopy part's own bounds — "is it
/// thinner across the aeroplane than half its height?" — and a model with no canopy part at all is an open cockpit.
/// The Pitts (0.716 m across, 0.406 m tall) and the Extra (0.78 × 0.48) come out as bubbles; a 172's or a J-3's door
/// comes out as a door; a glider's canopy is a bubble, which it is.
///
/// NOT HERE: where he ends up once he is clear. The velocity this hands over is his velocity relative to the
/// aeroplane along its own up axis, and from there he is <see cref="JumperTumble"/>'s and the air's.
/// </summary>
public static class BailoutSequence
{
    /// <summary>The canopy or door pops off, s from the button.</summary>
    public const double CanopyOffSec = 1.0;
    /// <summary>He releases the harness and starts trying to get out, s from the button.</summary>
    public const double HarnessOffSec = 1.5;
    /// <summary>How long the climb out takes anywhere between <see cref="EasyGLow"/> and <see cref="EasyGHigh"/>, s.</summary>
    public const double ExitBaseSec = 2.0;
    /// <summary>Added to that for every g outside the band, either way, s.</summary>
    public const double PenaltySecPerG = 1.0;
    public const double EasyGLow = 0.0, EasyGHigh = 1.0;
    /// <summary>How far he has to move to be clear of the cockpit, m: out of the seat and over the sill. E.</summary>
    public const double SillClearanceM = 0.60;
    /// <summary>What his own arms and legs are worth when he pushes off, m/s. The 3 m/s sideways and 1.5 m/s up
    /// PilotEgress has always used, as a speed: his muscles, not the g.</summary>
    public const double MuscleMs = 3.35;

    /// <summary>
    /// The opening this aeroplane has, from the canopy part's own bounds in aeroplane axes — the same numbers
    /// <see cref="Vr.CanopySpec.ForPart"/> is given, and the same door test. No list of type names anywhere.
    /// </summary>
    /// <param name="hasCanopyPart">False when the model has no canopy or door at all (AirframeVisual.CanopyParts empty).</param>
    /// <param name="spanM">The part's extent ACROSS the aeroplane, m.</param>
    /// <param name="heightM">The part's extent UP, m.</param>
    public static EgressOpening OpeningFor(bool hasCanopyPart, double spanM, double heightM) =>
        !hasCanopyPart ? EgressOpening.OpenTop
        : Vr.CanopySpec.IsDoor(spanM, heightM) ? EgressOpening.SideDoor
        : EgressOpening.TopCanopy;

    /// <summary>How long the climb out takes at this load factor, s (see the class summary for the reading).</summary>
    public static double ClimbOutSec(double loadFactorZ)
    {
        double over = loadFactorZ > EasyGHigh ? loadFactorZ - EasyGHigh
                    : loadFactorZ < EasyGLow ? EasyGLow - loadFactorZ
                    : 0.0;
        return ExitBaseSec + PenaltySecPerG * over;
    }

    /// <summary>Is the g throwing him out rather than making him climb? Negative g, and the way out is up.</summary>
    public static bool FlungOut(EgressOpening opening, double loadFactorZ) =>
        loadFactorZ < 0.0 && opening != EgressOpening.SideDoor;

    /// <summary>The apparent acceleration on an UNBUCKLED occupant toward the canopy rails, m/s² — what is left of
    /// gravity and the aeroplane's own acceleration once the belts have stopped holding him. At −1 g that is one g
    /// upward, which is why he goes; at +1 g it is one g into the seat, which is why he has to climb.</summary>
    public static double TowardTheRailsMs2(double loadFactorZ) => -loadFactorZ * Atmosphere.GravityMs2;
}

/// <summary>
/// One bail-out, stepped. Hold one per aeroplane and call <see cref="Step"/> with the live load factor: the g is read
/// EVERY step, not frozen at the moment the harness came off, so pulling out of the dive while he is half out slows
/// him down and pushing over throws him the rest of the way. See <see cref="BailoutSequence"/> for the rules and
/// whose they are.
/// </summary>
public sealed class Bailout
{
    public double TimeSec { get; private set; }
    public bool CanopyGone { get; private set; }
    public bool HarnessReleased { get; private set; }
    /// <summary>He is clear of the cockpit.</summary>
    public bool Out { get; private set; }
    /// <summary>How far out of the cockpit he is, 0..1 of <see cref="BailoutSequence.SillClearanceM"/>.</summary>
    public double Exit01 { get; private set; }
    /// <summary>His speed along the aeroplane's UP axis relative to it when he came clear, m/s. The g's where he was
    /// thrown, his own muscles where he climbed.</summary>
    public double ExitSpeedMs { get; private set; }
    /// <summary>He was thrown out by the g rather than climbing out.</summary>
    public bool WasFlung { get; private set; }
    /// <summary>The moment he came clear, s from the button (NaN until he is).</summary>
    public double OutAtSec { get; private set; } = double.NaN;

    private double _flingVel;

    public void Step(double dt, double loadFactorZ, EgressOpening opening)
    {
        if (dt <= 0.0 || Out) return;
        TimeSec += dt;
        if (!CanopyGone && TimeSec >= BailoutSequence.CanopyOffSec) CanopyGone = true;
        if (!HarnessReleased && TimeSec >= BailoutSequence.HarnessOffSec) HarnessReleased = true;
        if (!HarnessReleased) return;

        if (BailoutSequence.FlungOut(opening, loadFactorZ))
        {
            // THROWN. Nothing is on a clock here: he is a free body in the cockpit's apparent gravity, starting from
            // however far out he had already climbed, and he is clear when he has covered the sill.
            WasFlung = true;
            double a = BailoutSequence.TowardTheRailsMs2(loadFactorZ);
            _flingVel += a * dt;
            double d = Exit01 * BailoutSequence.SillClearanceM + _flingVel * dt;
            Exit01 = System.Math.Clamp(d / BailoutSequence.SillClearanceM, 0.0, 1.0);
            if (Exit01 >= 1.0) { Out = true; ExitSpeedMs = _flingVel; OutAtSec = TimeSec; }
            return;
        }
        // CLIMBING. He makes progress at 1/ClimbOutSec per second, so a g that changes while he is half way out
        // changes how fast the rest of it goes. The push-off he leaves with is his own.
        _flingVel = 0.0;
        Exit01 = System.Math.Min(1.0, Exit01 + dt / BailoutSequence.ClimbOutSec(loadFactorZ));
        if (Exit01 >= 1.0) { Out = true; ExitSpeedMs = BailoutSequence.MuscleMs; OutAtSec = TimeSec; WasFlung = false; }
    }
}

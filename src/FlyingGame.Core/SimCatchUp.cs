using System;

namespace FlyingGame.Core;

/// <summary>
/// What the fixed-step loop's catch-up cap actually does, in one place, so the graphics policy and the sim loop
/// cannot disagree about it.
///
/// The loop keeps an accumulator of real time owed to the physics. Each frame it adds the frame's duration and then
/// does <c>accumulator = Math.Min(accumulator + realDt, maxCatchUp)</c>. The surplus is not deferred to the next
/// frame — there is nowhere for it to go — it is <b>DISCARDED</b>. The sim therefore runs SLOWER THAN REAL TIME for
/// that frame: the aeroplane advances through less simulated time than the pilot lived through, every control input
/// held across that frame is integrated for less time than it was held, and the trajectory on the screen is not the
/// trajectory physics would have produced for the elapsed wall clock.
///
/// That is deliberate and it is the right trade — without it one slow frame asks for more steps next frame, which is
/// slower still, and the iPad mini sat at 3 fps for twenty seconds at a time (owner 2026-10-04). But it is also the
/// exact harm the graphics policy exists to avoid, and it is already counted
/// (<c>FlightSimDriver.CatchUpCappedFrames</c>; the Quest's <c>[QuestPerf]</c> line prints it as <c>sim capped</c>,
/// and it read 138 in a five-second window the one time the device was measured in trouble).
/// </summary>
public static class SimCatchUp
{
    /// <summary>Simulated seconds thrown away this frame: the owed time over the cap, or zero.</summary>
    public static double Discarded(double accumulatorSec, double realDtSec, double maxCatchUpSec)
    {
        double wanted = accumulatorSec + realDtSec;
        double over = wanted - maxCatchUpSec;
        return over > 0.0 ? over : 0.0;
    }
}

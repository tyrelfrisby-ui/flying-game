using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE CHRISTEN EAGLE MANUAL ON THE INVERTED FLAT SPIN, as the reality check it is: full power HOLDS it, and
/// power off with the controls NEUTRAL recovers it. Both cells pass; they are pinned here because they were
/// reported as broken twice (Ty 2026-11-06 "too easy to recover from the inverted spin", 2026-11-10 "the
/// inverted spin with right rudder rolls to knife edge then upright") and because nothing else in the suite
/// states them.
///
/// WHAT THE ENTRY MEANS, and why it is written this way. The spin is BISTABLE, and the entry decides which
/// attractor it falls into — a single number means nothing without stating the entry. These two fly the
/// aeroplane the way the manoeuvre is flown: push to the break at idle, rudder in, let the spin DEVELOP, and
/// only then do the thing under test. Hold full pro-spin aileron from the break instead, at full power, and the
/// Pitts leaves the spin at t = 3.5 s and ends in an upright one, which is the open defect
/// (InvSpinPowerHoldProbe / InvSpinRollOutProbe). That is a PITCH budget, not the fin: the propeller's three
/// nose-down terms at full power — the gyroscopic couple (−r·h ≈ −1050 N·m), the thrust line 0.20 m above the
/// CG (−760) and the legacy P-factor's offset from sideslip (−760) — total about −2600 N·m against a horizontal
/// tail making +2000, and at the entry's rotation rate the tail has not yet got the dynamic pressure to answer
/// them. Ablate the gyro, OR the P-factor and the thrust line together, and the entry holds; either one alone
/// does not. So do not relax these two tests to make that one pass: they are the part that is already right.
/// </summary>
[Collection("WorldTerrainActive")]
public class InvertedSpinPowerTests
{
    private const double Full = -1.0, Idle = 1.0;   // ControlInputs.ThrottleLever: −1 is full power, +1 is idle

    private static AircraftConfig Load(string id)
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d!.FullName, "configs", "aircraft", id + ".json"));
    }

    /// <summary>Push to the break at idle, right rudder in, then 12 s of full power to develop the spin.</summary>
    private static (Aircraft, SimLoop) DevelopedInvertedSpin(string id)
    {
        var ac = new Aircraft(Load(id), new RigidBodyState(new Vec3(0, 0, -3000), new Quat(1, 0, 0, 0), new Vec3(60, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
            sim.RunFor(0.1, new ControlInputs(0, Math.Min(1, 0.2 + 0.02 * i), 0, Idle));
        for (int i = 0; i < 48; i++) { sim.RunFor(0.25, new ControlInputs(0, 1, 1, Full)); Wrap(ac); }
        return (ac, sim);
    }

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("great-lakes-like")]
    public void FullPowerAndProSpinAileronHoldTheInvertedFlatSpin(string id)
    {
        var (ac, sim) = DevelopedInvertedSpin(id);
        double invertedSec = 0, aSum = 0, wSum = 0;
        int n = 0;
        for (int i = 0; i < 80; i++)
        {
            sim.RunFor(0.25, new ControlInputs(1, 1, 1, Full));   // pro-spin aileron, push, right rudder, full power
            Wrap(ac);
            var st = ac.State;
            var yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
            if (Math.Abs(Math.Atan2(yb.Z, st.Attitude.Rotate(new Vec3(0, 0, 1)).Z)) * 57.2958 > 120) invertedSec += 0.25;
            aSum += Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
            wSum += st.Rates.Length * 57.2958;
            n++;
        }
        // Inverted for all but a second of 20, still turning, and still at a spin's angle of attack.
        Assert.True(invertedSec > 18.0, $"{id}: held inverted only {invertedSec:0.0}s of 20 under full power");
        Assert.True(aSum / n < -55.0, $"{id}: mean alpha {aSum / n:0.0} — left the spin's angle of attack");
        Assert.True(wSum / n > 120.0, $"{id}: mean rotation {wSum / n:0.0} deg/s — the spin stopped under power");
    }

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("great-lakes-like")]
    public void PowerOffWithTheControlsNeutralRecoversTheInvertedFlatSpin(string id)
    {
        var (ac, sim) = DevelopedInvertedSpin(id);
        double stoppedSec = 0, stoppedAt = -1;
        for (int i = 0; i < 80; i++)
        {
            // Stick and rudder centred AND the lever at idle. Note ControlInputs.Neutral is NOT this: its
            // ThrottleLever 0 is the middle of the −1..1 lever, which Aircraft reads as half power.
            sim.RunFor(0.25, new ControlInputs(0, 0, 0, Idle));
            Wrap(ac);
            var st = ac.State;
            double alpha = Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
            if (st.Rates.Length * 57.2958 < 40 && Math.Abs(alpha) < 20)
            { stoppedSec += 0.25; if (stoppedAt < 0 && stoppedSec > 1.5) stoppedAt = i * 0.25; }
            else stoppedSec = 0;
        }
        Assert.True(stoppedAt > 0 && stoppedAt < 12.0,
            $"{id}: power off with the controls neutral did not stop the inverted spin within 12 s (got {stoppedAt:0.0})");
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 800)
            ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2200),
                                          ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}

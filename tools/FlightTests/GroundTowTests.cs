using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Aerotow from the runway with the TugPilot: launch, climb, pattern; glider releases; tug returns,
/// lands with the rope and stops. Plus the rope-angle auto-release when the glider climbs far too high.</summary>
public class GroundTowTests
{
    private readonly ITestOutputHelper _out;
    public GroundTowTests(ITestOutputHelper o) { _out = o; }

    private static double Pitch(Aircraft ac) { var q = ac.State.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static Aircraft OnGround(AircraftConfig c, double x) => new Aircraft(c, LandingGear.RestingState(c, x, 0, 0), ControlDeflections.Neutral);

    private static (double roll, double pitch, double psi) Euler(RigidBodyState s)
    {
        Quat q = s.Attitude;
        return (Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)),
                Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)),
                Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)));
    }

    /// <summary>Glider pilot on tow: match the tug's bank; hold station a few metres above the tug's height
    /// (never kite above it — that lifts the tug's tail), with an optional deliberate height bias.</summary>
    private static ControlInputs FollowTug(Aircraft glider, Aircraft tug, double heightBiasM)
    {
        var (gr, _, _) = Euler(glider.State); var (tr, _, _) = Euler(tug.State);
        double ail = Math.Clamp((tr - gr) * 2.0 - glider.State.Rates.X * 0.6, -1, 1);
        double gAlt = -glider.State.Position.Z, tAlt = -tug.State.Position.Z;
        double target = tAlt + 2.5 + heightBiasM;
        double vz = glider.State.Attitude.Rotate(glider.State.Velocity).Z; // + down
        // Altitude hold: climb rate command from height error, elevator from climb-rate error.
        double vzCmd = Math.Clamp((gAlt - target) * 0.4, -3.0, 3.0);          // + = want to sink
        double elev = Math.Clamp((vzCmd - vz) * 0.25 + glider.State.Rates.Y * 0.5, -0.6, 0.6);
        bool ground = LandingGear.AnyMainWheelOnGround(glider.Config, glider.State) && glider.State.Velocity.Length < 18;
        return new ControlInputs(ground ? 0 : ail, ground ? 0 : elev, 0, 0.0);
    }

    private static Vec3? RopeDirBody(AeroTow tow)
    {
        Vec3 r = tow.GliderHookWorld - tow.TugHookWorld;
        if (r.Length < 1e-6) return null;
        return tow.Tug.State.Attitude.Conjugate().Rotate(r / r.Length);
    }

    [Theory]
    [InlineData("pa18-cub-like")]
    [InlineData("pa25-pawnee-like")]
    public void LaunchPatternReleaseReturnAndLand(string tugId)
    {
        var gcfg = TestAircraftConfig.Load();
        var tcfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", tugId + ".json"));
        var glider = OnGround(gcfg, 0);
        var tug = OnGround(tcfg, 61.0 + 2.0 + 3.4 - 0.2);
        tug.ComponentLost += c => { var vw = tug.State.Attitude.Rotate(tug.State.Velocity); var (rr, pp, _) = Euler(tug.State); _out.WriteLine($"LOST {c}: V={tug.State.Velocity.Length:F1} sink={vw.Z:F2} m/s pitch={pp * 57.3:F1}° roll={rr * 57.3:F1}° x={tug.State.Position.X:F0} agl={-tug.State.Position.Z:F1}"); };
        var tow = new AeroTow(tug, glider, 61.0);
        var pilot = new TugPilot { ThresholdX = -100, RunwayY = 0, RunwayElevM = 0 };
        double dt = SimLoop.DefaultFixedDtSec, maxTension = 0, nextLog = 0; bool released = false; string lastPhase = "";
        for (double t = 0; t < 600; t += dt)
        {
            if (tow.Connected) tow.Apply(dt);
            pilot.PowerLimit01 = Math.Min(1.0, t / 2.0);
            if (!released && t > 90 && pilot.Phase == TugPilot.Phases.Pattern) { tow.Release("glider"); released = true; pilot.GliderReleased = true; }
            glider.Step(tow.Connected ? FollowTug(glider, tug, 0.0) : new ControlInputs(0, 0, 0, 0.3), dt);
            ControlInputs ci = pilot.Update(tug, dt, tow.Tension, tow.Connected ? RopeDirBody(tow) : null);
            tug.Step(ci, dt);
            maxTension = Math.Max(maxTension, tow.Tension);
            string ph = pilot.Phase.ToString();
            if (System.Environment.GetEnvironmentVariable("TOWTRACE") != null && t < 120 && Math.Abs(t / 1.0 - Math.Round(t / 1.0)) < dt / 2) { var (rr, pp, yy) = Euler(tug.State); _out.WriteLine($"   START scale {pilot.ElevatorScale:F2} t={t:F1} tension {tow.Tension:F0} tugV {tug.State.Velocity.Length:F1} gliderV {glider.State.Velocity.Length:F1} tug pitch {pp * 57.3:F1} agl {-tug.State.Position.Z:F2} x {tug.State.Position.X:F1} glider x {glider.State.Position.X:F1}"); }
            if (System.Environment.GetEnvironmentVariable("TOWTRACE") != null && pilot.Phase is TugPilot.Phases.Flare or TugPilot.Phases.Rollout && Math.Abs(t / 0.5 - Math.Round(t / 0.5)) < dt / 2) { var vw = tug.State.Attitude.Rotate(tug.State.Velocity); _out.WriteLine($"   FLARE t={t:F1} agl {-tug.State.Position.Z:F2} V {tug.State.Velocity.Length:F1} sink {vw.Z:F2} pitch {Pitch(tug):F1}"); }
            if (ph != lastPhase || t >= nextLog || pilot.Phase == TugPilot.Phases.Done)
            {
                nextLog = Math.Floor(t / 10) * 10 + 10;
                var (r, p, psi) = Euler(tug.State);
                _out.WriteLine($"t={t,5:F1} {ph,-10} tug V={tug.State.Velocity.Length:F1} agl={-tug.State.Position.Z:F0} x={tug.State.Position.X:F0} y={tug.State.Position.Y:F0} ψ={psi * 57.3:F0}° bank={r * 57.3:F0}° | glider V={glider.State.Velocity.Length:F1} agl={-glider.State.Position.Z:F0} | tension={tow.Tension:F0} {tow.SeverReason}");
                lastPhase = ph;
            }
            Assert.False(double.IsNaN(tug.State.Position.Z), "tug NaN");
            if (pilot.Phase == TugPilot.Phases.Done) break;
            if (tow.Connected == false && !released) Assert.Fail($"rope severed unexpectedly: {tow.SeverReason} (max {maxTension:F0} N)");
        }
        Assert.True(released, "glider never got to release (tug never reached the pattern)");
        Assert.Equal(TugPilot.Phases.Done, pilot.Phase);
        Assert.InRange(tug.State.Position.X, -150, 1500);
        Assert.InRange(Math.Abs(tug.State.Position.Y), 0, 25);   // on the 30 m runway
        Assert.True(-tug.State.Position.Z < 2.5, "tug should be on the runway");
        var (rollEnd, pitchEnd, _) = Euler(tug.State);
        Assert.True(Math.Abs(rollEnd) < 10 * Math.PI / 180 && pitchEnd > -5 * Math.PI / 180, $"tug should be upright (roll {rollEnd * 57.3:F0}°, pitch {pitchEnd * 57.3:F0}°, lost: {string.Join(", ", tug.LostComponents)})");
    }

    [Fact]
    public void TugReleasesWhenGliderClimbsTooHigh()
    {
        var gcfg = TestAircraftConfig.Load();
        var tcfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        var glider = OnGround(gcfg, 0);
        var tug = OnGround(tcfg, 61.0 + 2.0 + 3.4 - 0.2);
        var tow = new AeroTow(tug, glider, 61.0);
        var pilot = new TugPilot { ThresholdX = -100, RunwayY = 0, RunwayElevM = 0 };
        double dt = SimLoop.DefaultFixedDtSec; double tRelease = -1;
        for (double t = 0; t < 120; t += dt)
        {
            if (tow.Connected) tow.Apply(dt);
            pilot.PowerLimit01 = Math.Min(1.0, t / 2.0);
            // After 30 s airborne the glider pilot pulls up hard and stays high above the tug.
            double bias = (pilot.Phase != TugPilot.Phases.GroundRoll && t > 30) ? 40.0 : 0.0; // climb 40 m above the tug
            glider.Step(tow.Connected ? FollowTug(glider, tug, bias) : new ControlInputs(0, 0, 0, 0.3), dt);
            ControlInputs ci = pilot.Update(tug, dt, tow.Tension, tow.Connected ? RopeDirBody(tow) : null);
            if (System.Environment.GetEnvironmentVariable("TOWTRACE") != null && Math.Abs(t / 2.0 - Math.Round(t / 2.0)) < dt / 2) _out.WriteLine($"   t={t:F0} phase {pilot.Phase} tug agl {-tug.State.Position.Z:F1} V {tug.State.Velocity.Length:F1} glider agl {-glider.State.Position.Z:F1} tension {tow.Tension:F0} pitch {Pitch(tug):F1} ele {ci.Elevator:F2} thr {ci.ThrottleLever:F2}");
            if (pilot.WantsRelease && tow.Connected) { tow.Release("tug"); tRelease = t; }
            tug.Step(ci, dt);
            if (!tow.Connected) break;
        }
        _out.WriteLine($"tug released at t={tRelease:F1}");
        Assert.True(tRelease > 30 && tRelease < 90, $"tug should release once the glider is far above (got {tRelease})");
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// The free-flight IN-AIR START, engine-free (lifted out of <c>FlightSimDriver.Spawn</c>, 2026-11-02, so the launch the
/// game makes is the launch the tests make). Owner 2026-11-02: start HANDS OFF at the type's rig condition — a powered
/// aeroplane level at 75 % power (<see cref="RigSolver.RigThrottle"/>) at the speed that power holds at this height, with
/// the trim on the elevator that holds it; a glider at its best-glide speed (a hang glider at its hands-off speed).
/// RigSolver uses the airframe's fixed rig, so with the stick and rudder centred it flies straight. If the solve fails,
/// the power-off trim at the type's spawn speed stands in.
///
/// Everything here is a pure function of the config, the start and the air at the start point: two launches from the
/// same start in the same air are the same launch, to the bit (<c>LaunchReplicationTests</c>).
/// </summary>
public static class AirStart
{
    public sealed class Plan
    {
        public RigidBodyState State { get; set; }
        /// <summary>The deflections the aircraft is built with (elevator at the trim, aileron and rudder centred).</summary>
        public ControlDeflections Deflections { get; set; }
        /// <summary>The raw elevator stick (−1…1) that reproduces the trim through the config's shaping: the trim slider's
        /// preset.</summary>
        public double TrimStick { get; set; }
        /// <summary>The <see cref="ControlInputs.ThrottleLever"/> the start is trimmed at: 75 % power on a powered type
        /// (lever = 1 − 2 × throttle), spoilers stowed (0) on a glider.</summary>
        public double Lever { get; set; }
        public RigSolver.Result Rig { get; set; } = new();
        /// <summary>Null when the rig solve converged; otherwise what stood in.</summary>
        public string? Warning { get; set; }
    }

    /// <summary>The start at (x, y, altitude MSL) heading `headingRad` (true, from +x north toward +y east).
    /// `fallbackTasMs` is the type's spawn speed, used only if the rig solve fails.</summary>
    public static Plan Compute(AircraftConfig config, double x, double y, double altitudeM, double headingRad, double fallbackTasMs)
    {
        var plan = new Plan();
        double startTas = fallbackTasMs, startAlpha, startTheta, startBank = 0.0, startElevator;
        RigSolver.Result rig = config.Propulsion != null
            ? RigSolver.SolveLevel(config, RigSolver.RigThrottle, altitudeM)
            : RigSolver.SolveGlide(config, RigSolver.RigGlideTasAt(config, altitudeM), altitudeM);
        plan.Rig = rig;
        if (rig.Converged)
        {
            startTas = rig.TasMs; startAlpha = rig.AlphaRad; startTheta = rig.ThetaRad; startBank = rig.BankRad; startElevator = rig.ElevatorRad;
        }
        else
        {
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, fallbackTasMs, altitudeM);
            plan.Warning = $"Rig trim did not converge for {config.Id} (residual {rig.ResidualNorm:E2}); using the power-off trim"
                + (trim.Converged ? "." : ", which also failed: level attitude.");
            startAlpha = trim.AlphaRad; startTheta = trim.ThetaRad; startElevator = trim.Converged ? trim.ElevatorRad : 0.0;
        }
        plan.TrimStick = Aircraft.StickForDeflection(startElevator, config.Controls.Elevator);
        plan.Lever = config.Propulsion != null ? 1.0 - 2.0 * RigSolver.RigThrottle : 0.0;

        // (the bank is the fraction of a degree that carries the fin's side force with the ball centred)
        double half = startTheta / 2.0, halfBank = startBank / 2.0;
        var pitchQ = new Quat(0, Math.Sin(half), 0, Math.Cos(half));
        var rollQ = new Quat(Math.Sin(halfBank), 0, 0, Math.Cos(halfBank));
        var yawQ = new Quat(0, 0, Math.Sin(headingRad / 2), Math.Cos(headingRad / 2));
        Quat attitude = Quat.Multiply(yawQ, Quat.Multiply(pitchQ, rollQ));
        // Spawn with the trimmed speed relative to the AIR: ground velocity = air velocity + local wind, so in a crosswind
        // the aircraft starts in a coordinated crab (zero sideslip, camera on the tail) instead of a 12° sideslip that
        // takes seconds to weathervane out.
        var spawnPos = new Vec3(x, y, -altitudeM);
        Vec3 velocityBody = new Vec3(startTas * Math.Cos(startAlpha), 0, startTas * Math.Sin(startAlpha))
            + attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(spawnPos));
        plan.State = new RigidBodyState(spawnPos, attitude, velocityBody, Vec3.Zero);
        plan.Deflections = new ControlDeflections(0, startElevator, 0, 0);
        return plan;
    }

    /// <summary>The aeroplane the start builds: the config at the plan's state and deflections, wheels up.</summary>
    public static Aircraft Build(AircraftConfig config, Plan plan)
    {
        var ac = new Aircraft(config, plan.State, plan.Deflections);
        if (config.RetractableGear) ac.SetGear(false, immediate: true);   // airborne start: wheels up
        return ac;
    }

    /// <summary>Hands off at the start's trim: stick and rudder centred, the elevator on the trim, the power at the rig
    /// lever. HELD (stick-fixed) — what the aircraft flies until the pilot's controls publish their first frame, so the
    /// first steps of a new flight never fly the previous flight's stick and throttle.</summary>
    public static ControlInputs HeldInputs(Plan plan) => new(0, plan.TrimStick, 0, plan.Lever);
}

using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// The pilot in his seat through the crash (<see cref="OccupantInjury"/>, Ty 2026-11-05). Each step the seat point's
/// velocity is taken before and after the WHOLE step — the RK4 forces and everything applied after them (the water's
/// implicit drag, a tree stopping the aeroplane, a part's mass leaving) — so the acceleration it reads is what moved the
/// seat, whatever moved it. Nothing here is tuned to a crash: in level flight it reads 1 g, in a 6 g pull 6 g.
/// </summary>
public sealed partial class Aircraft
{
    private OccupantInjury? _occupant;
    /// <summary>What the flight has done to the pilot so far (a new Aircraft is a new pilot: Restart, relocate, new type).</summary>
    public OccupantInjury Occupant => _occupant ??= new OccupantInjury();

    private Vec3? _occupantSeat;
    /// <summary>The pilot's seat, body frame (x fwd, y right, z down). Default: the first seat's mass station, else the CG.
    /// The Bridge may set the seat the pilot is in (front or rear).</summary>
    public Vec3 OccupantSeatBody
    {
        get => _occupantSeat ??= Config.Loading is { Seats.Count: > 0 } l ? l.Seats[0].StationVec() : Config.Mass.CgVec();
        set => _occupantSeat = value;
    }

    private Vec3 _seatVelWorld;
    private bool _seatVelValid;

    /// <summary>The seat point's world velocity, m/s (CG velocity + ω × r).</summary>
    private Vec3 SeatVelocityWorld(in RigidBodyState s)
    {
        Vec3 r = OccupantSeatBody - Config.Mass.CgVec();   // the state's origin is the authored CG (CgShiftBody moves only the weight)
        return s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, r));
    }

    /// <summary>Called at the start of the step: the seat's velocity as the step finds it (after any teleport, reset or
    /// replay pose written between steps, so those are never read as an acceleration).</summary>
    private void BeginOccupantStep()
    {
        _seatVelWorld = SeatVelocityWorld(State);
        _seatVelValid = true;
    }

    /// <summary>Called at the end of the step: the seat's acceleration over it, less gravity, into the occupant.</summary>
    private void EndOccupantStep(double dt)
    {
        if (!_seatVelValid || !(dt > 0)) return;
        _seatVelValid = false;
        Vec3 a = (SeatVelocityWorld(State) - _seatVelWorld) / dt;
        Vec3 fWorld = a - new Vec3(0, 0, Atmosphere.GravityMs2);   // world z is down: gravity is +z
        Occupant.Step(State.Attitude.Conjugate().Rotate(fWorld), dt);
        // The cabin is the occupant's container in the crush model (FuselageStructure): its fold is the space he loses.
        Occupant.ObserveCrush(Crush.CabinCrushM);
    }
}

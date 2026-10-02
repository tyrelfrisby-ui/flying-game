namespace FlyingGame.Sim;

/// <summary>
/// Raw stick/lever positions for one sim step, before dead zone/expo/rate shaping (that shaping lives in
/// AircraftConfig, applied by <see cref="Aircraft"/>). Aileron/elevator/rudder are -1..1. ThrottleLever is
/// -1 (full forward) .. 0 (neutral) .. +1 (full aft) — on the glider this same lever is the speed brake
/// (axisMap "aftOnly": neutral/forward = stowed, aft = deployed) per DATA-CONTRACTS.md.
/// </summary>
public readonly struct ControlInputs
{
    public readonly double Aileron;
    public readonly double Elevator;
    public readonly double Rudder;
    public readonly double ThrottleLever;
    /// <summary>Pilot's hand/feet OFF this control (owner 2026-10-02): on a reversible axis the surface is then free and
    /// trails with the air loads; the axis value is read as the TRIM position only. Default false = held.</summary>
    public readonly bool AileronFree, ElevatorFree, RudderFree;

    public ControlInputs(double aileron, double elevator, double rudder, double throttleLever)
    {
        Aileron = aileron;
        Elevator = elevator;
        Rudder = rudder;
        ThrottleLever = throttleLever;
        AileronFree = ElevatorFree = RudderFree = false;
    }

    public ControlInputs(double aileron, double elevator, double rudder, double throttleLever, bool aileronFree, bool elevatorFree, bool rudderFree)
        : this(aileron, elevator, rudder, throttleLever)
    {
        AileronFree = aileronFree;
        ElevatorFree = elevatorFree;
        RudderFree = rudderFree;
    }

    public static readonly ControlInputs Neutral = new(0, 0, 0, 0);
}

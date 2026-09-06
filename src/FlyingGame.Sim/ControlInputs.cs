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

    public ControlInputs(double aileron, double elevator, double rudder, double throttleLever)
    {
        Aileron = aileron;
        Elevator = elevator;
        Rudder = rudder;
        ThrottleLever = throttleLever;
    }

    public static readonly ControlInputs Neutral = new(0, 0, 0, 0);
}

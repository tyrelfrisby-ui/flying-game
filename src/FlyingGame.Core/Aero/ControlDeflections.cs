namespace FlyingGame.Core.Aero;

/// <summary>
/// Resolved control-surface deflections (radians) for one sim step. Aileron/elevator/rudder are the
/// deflection commanded at the stick (each strip's own gain, from AircraftConfig, scales this into that
/// strip's local incidence change). Spoiler is the glider speed-brake, drag-only.
/// </summary>
public readonly struct ControlDeflections
{
    public readonly double AileronRad;
    public readonly double ElevatorRad;
    public readonly double RudderRad;
    public readonly double SpoilerFraction; // 0 (stowed) .. 1 (fully deployed)

    public ControlDeflections(double aileronRad, double elevatorRad, double rudderRad, double spoilerFraction)
    {
        AileronRad = aileronRad;
        ElevatorRad = elevatorRad;
        RudderRad = rudderRad;
        SpoilerFraction = spoilerFraction;
    }

    public static readonly ControlDeflections Neutral = new(0, 0, 0, 0);

    /// <summary>Raw stick deflection (radians) for the named control surface, 0 for anything unrecognized.</summary>
    public double GetDeflection(string? surfaceName) => surfaceName switch
    {
        "aileron" => AileronRad,
        "elevator" => ElevatorRad,
        "rudder" => RudderRad,
        _ => 0.0,
    };
}

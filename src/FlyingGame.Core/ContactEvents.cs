using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>What happened at a contact patch (Ty 2026-11-03: "scrapes the surface with sound and dust").</summary>
public enum ContactEventKind
{
    /// <summary>A patch began sliding on the ground or a solid (slip faster than <see cref="ScrapeState.SlipThresholdMs"/>).</summary>
    ScrapeStart,
    /// <summary>It stopped: lifted clear, came to rest, or the part left.</summary>
    ScrapeStop,
    /// <summary>A strike between the limit and ultimate moments bent a wing panel (the crunch). Value = bend, degrees.</summary>
    PanelBent,
    /// <summary>A wing panel broke off (a hard strike, or a bent panel past 15°). Value = moment / ultimate, or the bend.</summary>
    PanelBroken,
    /// <summary>A propeller blade touched the ground. Value = penetration, m.</summary>
    PropStrike,
    /// <summary>The propeller strike stopped the engine (sudden stoppage). Value = rpm when it began.</summary>
    PropStoppage,
    /// <summary>A tree stem snapped where the airframe struck it (TreeStructure). Intensity = the stem's strength, N;
    /// value = its diameter there, m.</summary>
    TreeSnapped,
}

/// <summary>One contact event, raised by <c>Aircraft.ContactEvent</c> after the step in which it happened.</summary>
public readonly struct ContactEvent
{
    public readonly ContactEventKind Kind;
    /// <summary>The patch or part: "tip:wing-lower-LE-L", "fin-top", "prop-tip", …</summary>
    public readonly string Part;
    public readonly AirframeComponent Component;
    /// <summary><see cref="ContactPoint.SurfaceCode"/>: a <see cref="WorldTerrain.Surface"/> value, or solid / water / none.</summary>
    public readonly int SurfaceCode;
    /// <summary>Friction power at the patch, W (scrapes); the bending moment as a fraction of ultimate (panel events).</summary>
    public readonly double Intensity;
    public readonly double Value;
    public readonly Vec3 PositionWorld, VelocityWorld;
    public ContactEvent(ContactEventKind kind, string part, AirframeComponent component, int surfaceCode, double intensity, double value, Vec3 positionWorld, Vec3 velocityWorld)
    {
        Kind = kind; Part = part; Component = component; SurfaceCode = surfaceCode; Intensity = intensity; Value = value; PositionWorld = positionWorld; VelocityWorld = velocityWorld;
    }
    public override string ToString() => $"{Kind} {Part} ({Component}) surface {SurfaceCode} intensity {Intensity:F0} value {Value:F2}";
}

/// <summary>A patch that is sliding now: what the scrape sound and the dust follow every frame.</summary>
public sealed class ScrapeState
{
    /// <summary>Slower than this the contact is regularised static friction (AirframeContact), not a scrape.</summary>
    public const double SlipThresholdMs = 0.3;
    public string Part = "";
    public AirframeComponent Component;
    public int SurfaceCode;
    /// <summary>μ·N·v summed over the patch, W: the energy the scrape turns into heat, noise and thrown dirt.</summary>
    public double PowerW;
    public double NormalN, SlipMs;
    public Vec3 PositionWorld, VelocityWorld;
}

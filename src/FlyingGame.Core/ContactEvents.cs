using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>What happened at a contact patch (Ty 2026-11-03: "scrapes the surface with sound and dust").</summary>
public enum ContactEventKind
{
    /// <summary>A patch began sliding on the ground or a solid (slip faster than <see cref="ScrapeState.SlipThresholdMs"/>).</summary>
    ScrapeStart,
    /// <summary>It stopped: lifted clear, came to rest, or the part left.</summary>
    ScrapeStop,
    /// <summary>A wing panel yielded (the crunch): raised when it first takes a set and again every further 5° of bend or
    /// twist (<see cref="WingStructure"/>). Intensity = the demand over its ultimate; value = its bend, degrees.</summary>
    PanelBent,
    /// <summary>A wing panel broke off: one of its hinges turned through its rupture rotation (<see cref="PlasticHinge"/>).
    /// Intensity and value = the last demand over its ultimate.</summary>
    PanelBroken,
    /// <summary>A propeller blade touched the ground. Value = penetration, m.</summary>
    PropStrike,
    /// <summary>The propeller strike stopped the engine (sudden stoppage). Value = rpm when it began.</summary>
    PropStoppage,
    /// <summary>A tree stem snapped where the airframe struck it (TreeStructure). Intensity = the stem's strength, N;
    /// value = its diameter there, m.</summary>
    TreeSnapped,
    /// <summary>A fuselage section crushed instead of breaking (<see cref="FuselageStructure"/>): Component = Nose, Cabin or
    /// TailBoom. Intensity = the energy this arrival put into the crush, J; value = the crush, m.</summary>
    FuselageCrushed,
    /// <summary>The airframe went through branches (<see cref="BranchStrike"/>) and they did harm: a dented leading edge, torn
    /// fabric, a struck propeller or a damaged control surface. Intensity = the energy the wood took out of the airframe, J;
    /// value = the branch diameter met, m.</summary>
    BranchDamage,
    /// <summary>A tail surface, rudder, wing mid-span, nacelle or gear leg arrived over its break speed and bent instead of
    /// breaking (<see cref="MemberMaterials.CrushCapacityM"/>). Intensity = arrival over break speed; value = its crush so far, m.</summary>
    MemberBent,
    /// <summary>One blade met the ground or the water on one pass (Ty 2026-11-05 9:04: "accurate sounds on both the ground
    /// and water too per blade strike"): one per blade per pass, at blades × rpm. Intensity = the tip's speed, m/s;
    /// value = how deep that blade went, m; <see cref="ContactEvent.Blade"/>, <see cref="ContactEvent.TimeSec"/> (the
    /// instant within the step, for the sound's timing) and <see cref="ContactEvent.Material"/> say which and what.</summary>
    PropBladeStrike,
    /// <summary>A metal blade's struck span curled. Intensity = +1 forward / −1 aft (the sign of that blade's thrust);
    /// value = the curled span, m.</summary>
    PropBladeBent,
    /// <summary>A wooden or composite blade's struck span broke off and left. Intensity = the piece's mass, kg; value =
    /// its length, m; PositionWorld / VelocityWorld = where the piece starts and how fast it goes.</summary>
    PropBladeBroken,
    /// <summary>Wood the airframe threw off going through a tree (Ty 2026-11-05 13:52: "throw bits of tree flying off"): a
    /// snapped top, or the branches and needles one third of an edge has cut since the last report
    /// (<c>Aircraft.WoodEventKg</c>). Intensity = the mass, kg; value = the wood's diameter there, m; PositionWorld /
    /// VelocityWorld = where, and the struck part's own velocity. Part = the edge ("wing-panel-R", "gear-left", "float-L").</summary>
    WoodThrown,
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
    /// <summary>Propeller blade events: which blade (0..n−1; −1 otherwise), which propeller, and the blade's material.</summary>
    public readonly int Blade, Prop;
    public readonly PropMaterial Material;
    /// <summary>Sim time of the event (<see cref="Atmosphere.SimTimeSec"/>), s: within its step for a blade strike.</summary>
    public readonly double TimeSec;
    public ContactEvent(ContactEventKind kind, string part, AirframeComponent component, int surfaceCode, double intensity, double value, Vec3 positionWorld, Vec3 velocityWorld)
        : this(kind, part, component, surfaceCode, intensity, value, positionWorld, velocityWorld, -1, 0, PropMaterial.Metal, Atmosphere.SimTimeSec) { }
    public ContactEvent(ContactEventKind kind, string part, AirframeComponent component, int surfaceCode, double intensity, double value, Vec3 positionWorld, Vec3 velocityWorld,
        int blade, int prop, PropMaterial material, double timeSec)
    {
        Kind = kind; Part = part; Component = component; SurfaceCode = surfaceCode; Intensity = intensity; Value = value; PositionWorld = positionWorld; VelocityWorld = velocityWorld;
        Blade = blade; Prop = prop; Material = material; TimeSec = timeSec;
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

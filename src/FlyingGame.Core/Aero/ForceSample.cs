using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>One force (or moment) the physics model applied this step, for the force-vector overlay: where it
/// acts (body frame, config origin), the force (body frame, N), a pure moment (body frame, N·m) and what it is.</summary>
public readonly struct ForceSample
{
    public readonly Vec3 PosBody, ForceBody, MomentBody;
    public readonly string Kind;   // lift | drag | moment | fuselage | spoiler | thrust | gear | contact | rope | weight
    public ForceSample(Vec3 pos, Vec3 force, Vec3 moment, string kind) { PosBody = pos; ForceBody = force; MomentBody = moment; Kind = kind; }
}

/// <summary>Capture sink for the overlay: null = off; when set, every model adds its samples for the current
/// force evaluation (the caller resets it per evaluation so the last RK4 stage's list survives).</summary>
public static class ForceDebug
{
    public static List<ForceSample>? Samples;
    public static void Add(Vec3 pos, Vec3 force, Vec3 moment, string kind) { Samples?.Add(new ForceSample(pos, force, moment, kind)); }
}

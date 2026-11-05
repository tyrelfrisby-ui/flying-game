using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

// CONTRACT between lane/claude-crash-water (crash physics: detects the surface, raises these events) and
// lane/codex-crash-audio (crash audio + splash/bubble visuals: subscribes to them). Both lanes add this file
// byte-identical so it merges cleanly. NEITHER lane edits it; a needed change goes in AGENT-LOG.md NEEDS YOU.
// Positions/velocities are in the same sim world frame as the aircraft state position (convert for Unity with
// FlyingGame.Bridge.CoordinateMap.ToUnity). Raise on the main/sim thread only.

/// <summary>What a crash impact struck.</summary>
public enum ImpactSurface { Land = 0, Water = 1 }

/// <summary>One impact of the whole airframe or of one wreck piece against land or water.</summary>
public readonly struct CrashImpact
{
    public readonly ImpactSurface Surface;
    /// <summary>Contact point, sim world frame, m.</summary>
    public readonly Vec3 Position;
    /// <summary>Velocity of the impacting body at contact, sim world frame, m/s.</summary>
    public readonly Vec3 Velocity;
    /// <summary>Closing speed along the surface normal at contact, m/s (&gt;= 0).</summary>
    public readonly double NormalSpeedMs;
    /// <summary>Mass of the impacting body, kg.</summary>
    public readonly double MassKg;
    /// <summary>0..1 weight for loudness/size shaping; the physics lane documents how it is derived.</summary>
    public readonly double Severity01;
    /// <summary>-1 = the whole (intact) airframe; otherwise the wreck piece id.</summary>
    public readonly int PieceId;

    public CrashImpact(ImpactSurface surface, Vec3 position, Vec3 velocity, double normalSpeedMs, double massKg,
        double severity01, int pieceId)
    {
        Surface = surface; Position = position; Velocity = velocity; NormalSpeedMs = normalSpeedMs;
        MassKg = massKg; Severity01 = severity01; PieceId = pieceId;
    }
}

/// <summary>Per-step flooding state of one floating/sinking body (wreck piece, or -1 = intact airframe).</summary>
public readonly struct FloodState
{
    public readonly int PieceId;
    /// <summary>Body centre, sim world frame, m.</summary>
    public readonly Vec3 Position;
    /// <summary>Depth of the body centre below the local water surface, m (positive = under water).</summary>
    public readonly double DepthM;
    /// <summary>Volume rate of trapped air escaping (= water flooding in), m^3/s (&gt;= 0). Drives gurgle/bubbles.</summary>
    public readonly double AirEscapeM3s;
    /// <summary>Trapped air still inside, m^3.</summary>
    public readonly double TrappedAirM3;
    /// <summary>True once the body has lost all buoyancy margin and is going (or gone) to the bottom.</summary>
    public readonly bool Sinking;

    public FloodState(int pieceId, Vec3 position, double depthM, double airEscapeM3s, double trappedAirM3, bool sinking)
    {
        PieceId = pieceId; Position = position; DepthM = depthM; AirEscapeM3s = airEscapeM3s;
        TrappedAirM3 = trappedAirM3; Sinking = sinking;
    }
}

/// <summary>Process-wide hub. Physics raises; audio/visuals subscribe. Tests must call <see cref="ClearForTests"/>.</summary>
public static class CrashEvents
{
    public static event Action<CrashImpact>? Impact;
    public static event Action<FloodState>? Flood;

    public static void RaiseImpact(CrashImpact e) => Impact?.Invoke(e);
    public static void RaiseFlood(FloodState e) => Flood?.Invoke(e);
    public static void ClearForTests() { Impact = null; Flood = null; }

    /// <summary>
    /// Does this impact put the chase camera into the water-crash view (framing the wreck)? Only a PIECE that broke off
    /// arriving in the water (<see cref="CrashImpact.PieceId"/> ≥ 0). Ty 2026-11-04 20:24 PT: "no predetermined crash on
    /// water touch; emergent forces, damage only by load." Until then ANY water impact of the airframe above 0.5 m/s
    /// (CrashRelay.WaterSplashMinMs) — a skim, a float landing, a wheel touch — latched the crash view for the rest of the
    /// flight. The airframe touching water is spray and sound; a crash is something breaking, and parts break only by load
    /// (AirframeFlooding.CheckJoints, WaterSlam, the wing panels' bending), which is what makes a piece.
    /// </summary>
    public static bool OpensWaterCrashView(in CrashImpact e) => e.Surface == ImpactSurface.Water && e.PieceId >= 0;
}

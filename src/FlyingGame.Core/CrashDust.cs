namespace FlyingGame.Core;

/// <summary>
/// What a crash throws up, by the surface it happened on (Ty 2026-11-04: "Crashing on snow kicks up BROWN dust; it must be
/// white"). The surface is the one the contact model already decided at the contact point (<see cref="ContactPoint.SurfaceCode"/>:
/// a <see cref="WorldTerrain.Surface"/>, or <see cref="ContactPoint.SurfaceSolid"/> / <see cref="ContactPoint.SurfaceWater"/>),
/// so the dust always agrees with the friction the slide was given. Shared by every platform's emitter (ContactEffects).
///
/// The brown fallback is the colour every puff used to be: the dust material's shader drew its own tint and ignored the
/// particle's colour, so the per-surface table that was already there never reached the screen.
/// </summary>
public static class CrashDust
{
    public enum Kind { Dust, Powder, Chips, Spray }

    public readonly struct Look
    {
        public readonly float R, G, B, A;
        public readonly Kind Kind;
        /// <summary>Pitch of the scrape's grit noise on this surface.</summary>
        public readonly float Pitch;
        public Look(float r, float g, float b, float a, Kind kind, float pitch) { R = r; G = g; B = b; A = a; Kind = kind; Pitch = pitch; }
    }

    /// <summary>Unknown ground (and anything not listed): the brown it always was.</summary>
    public static readonly Look Fallback = new(0.50f, 0.42f, 0.30f, 0.50f, Kind.Dust, 0.8f);

    public static Look For(int surfaceCode) => surfaceCode switch
    {
        (int)WorldTerrain.Surface.Snow or (int)WorldTerrain.Surface.PackedGlacierSnow => new(0.95f, 0.97f, 1.00f, 0.60f, Kind.Powder, 0.5f),
        (int)WorldTerrain.Surface.GlacierIce => new(0.84f, 0.88f, 0.92f, 0.55f, Kind.Chips, 1.25f),
        ContactPoint.SurfaceWater => new(0.88f, 0.94f, 1.00f, 0.60f, Kind.Spray, 0.6f),
        (int)WorldTerrain.Surface.Paved => new(0.62f, 0.60f, 0.58f, 0.35f, Kind.Dust, 1.15f),
        ContactPoint.SurfaceSolid => new(0.60f, 0.60f, 0.60f, 0.30f, Kind.Dust, 1.35f),
        // Dirt and hard sand (lane/claude-tire-spinup) are their own wheel surfaces now; their dust is gravel's.
        (int)WorldTerrain.Surface.Gravel or (int)WorldTerrain.Surface.Dirt or (int)WorldTerrain.Surface.Sand => new(0.55f, 0.46f, 0.34f, 0.45f, Kind.Dust, 0.9f),
        // Grass, and the island's green pasture off the strips (Rough): torn turf over earth.
        (int)WorldTerrain.Surface.Grass or (int)WorldTerrain.Surface.Rough => new(0.42f, 0.44f, 0.26f, 0.40f, Kind.Dust, 0.65f),
        _ => Fallback,
    };
}

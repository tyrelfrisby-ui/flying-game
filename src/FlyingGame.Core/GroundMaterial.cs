namespace FlyingGame.Core;

/// <summary>
/// The ground under a wheel, as ONE key shared by the physics, the sound and (when Codex adopts it) the imagery
/// (docs/TIRE-SPINUP-AND-GROUND-SOUND.md §5a). The physics has its coarse <see cref="WorldTerrain.Surface"/>; the
/// renderer paints finer classes (StripFinish.Kind, FrankChurch.Ground, the island mesh's uv1 / colour channels). This
/// key is what both mean, and <see cref="GroundMaterials.Row"/> is where its numbers live.
///
/// APPEND ONLY: the sound's per-key voice arrays are indexed by it.
/// </summary>
public enum GroundMaterial
{
    Asphalt, Grass, Gravel, Dirt, Sand, Rough, Snow, PackedSnow, Ice, Water, Deck,
}

/// <summary>A rolling voice: what a tyre sounds like rolling over this ground (Bridge/Audio TyreBrakeSynth renders it).</summary>
public enum RollingVoice { None, TarmacHum, GrassSwish, GravelCrunch, DirtRumble, SandHiss, RoughThud, SnowCrunch, IceHiss, DeckDrum }

/// <summary>
/// One row per <see cref="GroundMaterial"/>. Grip and rolling resistance are NOT copied here: they are read from the
/// physics (<see cref="LandingGear.SurfaceGrip"/>, <see cref="WorldTerrain.RollingCoefficient"/>), so the table cannot
/// disagree with what the tyre actually does.
/// </summary>
public sealed class GroundMaterialRow
{
    public GroundMaterial Key;
    /// <summary>What the tyre physics calls it (grip and rolling resistance come from there).</summary>
    public WorldTerrain.Surface Physics;
    /// <summary>Which rendered classes show this ground (read-only documentation of the link; the visual code is Codex's
    /// and does not read this yet — AGENT-LOG NEEDS-Codex).</summary>
    public string Visual = "";
    /// <summary>How much of a full asphalt spin-up chirp this ground gives (1 asphalt/deck; a scuff on dirt; none on
    /// grass, snow and water, where the slip is in the ground rather than in the rubber).</summary>
    public double Chirp;
    /// <summary>Is the wheel's bearing whir heard here? Not on snow or water (Ty).</summary>
    public bool BearingWhir;
    public bool BrakeScrape, BrakeSqueal, SkidMark;
    public RollingVoice Voice;
    /// <summary>Rolling voice gain against wheel ground speed (m/s): piecewise linear (speed, gain).</summary>
    public double[] GainVsSpeed = { 0, 0 };
    /// <summary>Rolling voice pitch multiplier against ground speed.</summary>
    public double[] PitchVsSpeed = { 0, 1 };
    /// <summary>Time constant of the crossfade onto / off this ground, s (0.15–0.3).</summary>
    public double CrossfadeS = 0.2;

    public double Grip => LandingGear.SurfaceGrip(Physics);
    public double RollingResistance => WorldTerrain.RollingCoefficient(Physics);
}

public static class GroundMaterials
{
    public const int Count = (int)GroundMaterial.Deck + 1;

    private static readonly GroundMaterialRow[] Rows =
    {
        new() { Key = GroundMaterial.Asphalt, Physics = WorldTerrain.Surface.Paved, Visual = "StripFinish Paved; WorldTerrain apron/strips",
                Chirp = 1.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = true, SkidMark = true, Voice = RollingVoice.TarmacHum,
                GainVsSpeed = new double[] { 0, 0, 2, 0.15, 15, 0.5, 30, 0.7 }, PitchVsSpeed = new double[] { 0, 0.8, 30, 1.3 }, CrossfadeS = 0.20 },
        new() { Key = GroundMaterial.Grass, Physics = WorldTerrain.Surface.Grass, Visual = "StripFinish Turf; FrankChurch Turf; island uv1.x → turf",
                Chirp = 0.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.GrassSwish,
                // Ty 2026-11-08: "Grass tire sound is way too loud bring it down 7db and make it speed dependent it
                // seems constant from touchdown to being stopped". It was never constant — it reached silence at a dead
                // stop — but the old curve {0,0, 2,0.2, 15,0.55, 30,0.7} spent only about 7 dB between a touchdown and a
                // taxi, which against a sound already too loud is the top of the range the whole way down the strip.
                // Top end is exactly -7 dB (0.313 / 0.70 = 0.447), and the shape below it is steepened so touchdown to
                // taxi now spans about 12 dB. If the taxi end comes out too quiet, the 8 m/s point is the one to raise:
                // it moves the middle without touching the -7 dB at the top.
                GainVsSpeed = new double[] { 0, 0, 2, 0.025, 8, 0.12, 15, 0.21, 30, 0.313 }, PitchVsSpeed = new double[] { 0, 0.8, 30, 1.2 }, CrossfadeS = 0.25 },
        new() { Key = GroundMaterial.Gravel, Physics = WorldTerrain.Surface.Gravel, Visual = "StripFinish Gravel; FrankChurch Gravel; island uv1.x → gravel",
                Chirp = 0.25, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.GravelCrunch,
                GainVsSpeed = new double[] { 0, 0, 0.4, 0.05, 14, 0.55, 30, 0.65 }, PitchVsSpeed = new double[] { 0, 0.9, 30, 1.25 }, CrossfadeS = 0.20 },
        new() { Key = GroundMaterial.Dirt, Physics = WorldTerrain.Surface.Dirt, Visual = "StripFinish Dirt; FrankChurch Dirt",
                Chirp = 0.15, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.DirtRumble,
                GainVsSpeed = new double[] { 0, 0, 1, 0.1, 15, 0.5, 30, 0.6 }, PitchVsSpeed = new double[] { 0, 0.85, 30, 1.2 }, CrossfadeS = 0.20 },
        new() { Key = GroundMaterial.Sand, Physics = WorldTerrain.Surface.Sand, Visual = "StripFinish Sand (\"hard sand\")",
                Chirp = 0.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.SandHiss,
                GainVsSpeed = new double[] { 0, 0, 1, 0.12, 15, 0.5, 30, 0.6 }, PitchVsSpeed = new double[] { 0, 0.9, 30, 1.15 }, CrossfadeS = 0.25 },
        new() { Key = GroundMaterial.Rough, Physics = WorldTerrain.Surface.Rough, Visual = "default island class (pasture)",
                Chirp = 0.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.RoughThud,
                GainVsSpeed = new double[] { 0, 0, 0.5, 0.05, 10, 0.7, 30, 0.8 }, PitchVsSpeed = new double[] { 0, 1, 30, 1 }, CrossfadeS = 0.25 },
        new() { Key = GroundMaterial.Snow, Physics = WorldTerrain.Surface.Snow, Visual = "island uv1.y snow; AlaskaRange ice; ski zones",
                Chirp = 0.0, BearingWhir = false, BrakeScrape = false, BrakeSqueal = false, Voice = RollingVoice.SnowCrunch,
                GainVsSpeed = new double[] { 0, 0, 1, 0.15, 12, 0.55, 30, 0.6 }, PitchVsSpeed = new double[] { 0, 0.9, 30, 1.2 }, CrossfadeS = 0.30 },
        new() { Key = GroundMaterial.PackedSnow, Physics = WorldTerrain.Surface.PackedGlacierSnow, Visual = "AlaskaRange graded landing zones",
                Chirp = 0.0, BearingWhir = false, BrakeScrape = false, BrakeSqueal = false, Voice = RollingVoice.SnowCrunch,
                GainVsSpeed = new double[] { 0, 0, 1, 0.12, 12, 0.45, 30, 0.55 }, PitchVsSpeed = new double[] { 0, 1.0, 30, 1.3 }, CrossfadeS = 0.30 },
        new() { Key = GroundMaterial.Ice, Physics = WorldTerrain.Surface.GlacierIce, Visual = "island vertex colour ice",
                Chirp = 0.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = false, Voice = RollingVoice.IceHiss,
                GainVsSpeed = new double[] { 0, 0, 2, 0.1, 15, 0.4, 30, 0.5 }, PitchVsSpeed = new double[] { 0, 0.9, 30, 1.3 }, CrossfadeS = 0.25 },
        new() { Key = GroundMaterial.Water, Physics = WorldTerrain.Surface.Gravel, Visual = "water plane; StripFinish Water",
                Chirp = 0.0, BearingWhir = false, BrakeScrape = false, BrakeSqueal = false, Voice = RollingVoice.None, CrossfadeS = 0.15 },
        new() { Key = GroundMaterial.Deck, Physics = WorldTerrain.Surface.Paved, Visual = "carrier / ship decks (WorldDecks)",
                Chirp = 1.0, BearingWhir = true, BrakeScrape = true, BrakeSqueal = true, SkidMark = true, Voice = RollingVoice.DeckDrum,
                GainVsSpeed = new double[] { 0, 0, 2, 0.2, 15, 0.55, 30, 0.7 }, PitchVsSpeed = new double[] { 0, 0.9, 30, 1.2 }, CrossfadeS = 0.15 },
    };

    public static GroundMaterialRow Row(GroundMaterial k) => Rows[(int)k];

    /// <summary>The key for what the tyre physics found. A deck is paved to the tyre and a drum to the ear.</summary>
    public static GroundMaterial Of(WorldTerrain.Surface s, bool onDeck = false, bool overWater = false)
    {
        if (overWater) return GroundMaterial.Water;
        if (onDeck) return GroundMaterial.Deck;
        return s switch
        {
            WorldTerrain.Surface.Paved => GroundMaterial.Asphalt,
            WorldTerrain.Surface.Grass => GroundMaterial.Grass,
            WorldTerrain.Surface.Gravel => GroundMaterial.Gravel,
            WorldTerrain.Surface.Dirt => GroundMaterial.Dirt,
            WorldTerrain.Surface.Sand => GroundMaterial.Sand,
            WorldTerrain.Surface.Snow => GroundMaterial.Snow,
            WorldTerrain.Surface.PackedGlacierSnow => GroundMaterial.PackedSnow,
            WorldTerrain.Surface.GlacierIce => GroundMaterial.Ice,
            _ => GroundMaterial.Rough,
        };
    }

    /// <summary>Piecewise-linear lookup in a flat (x0, y0, x1, y1, …) table, held at the ends.</summary>
    public static double Curve(double[] xy, double x)
    {
        int n = xy.Length / 2;
        if (n == 0) return 0;
        if (x <= xy[0]) return xy[1];
        for (int i = 1; i < n; i++)
        {
            double x1 = xy[2 * i];
            if (x <= x1)
            {
                double x0 = xy[2 * i - 2], y0 = xy[2 * i - 1], y1 = xy[2 * i + 1];
                return x1 > x0 ? y0 + (y1 - y0) * (x - x0) / (x1 - x0) : y1;
            }
        }
        return xy[2 * n - 1];
    }
}

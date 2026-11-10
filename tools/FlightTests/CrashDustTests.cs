using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 5:23 PM PT: "Crashing on snow kicks up BROWN dust; it must be white." The crash dust takes its colour from
/// the surface the contact model decided at the contact point (ContactPoint.SurfaceCode). Expected colours are literals.
/// </summary>
public class CrashDustTests
{
    private static void Is(CrashDust.Look l, float r, float g, float b, float a, CrashDust.Kind kind)
    {
        Assert.Equal(r, l.R); Assert.Equal(g, l.G); Assert.Equal(b, l.B); Assert.Equal(a, l.A); Assert.Equal(kind, l.Kind);
    }

    [Fact]
    public void SnowThrowsUpWhitePowder()
    {
        Is(CrashDust.For((int)WorldTerrain.Surface.Snow), 0.95f, 0.97f, 1.00f, 0.60f, CrashDust.Kind.Powder);
        Is(CrashDust.For((int)WorldTerrain.Surface.PackedGlacierSnow), 0.95f, 0.97f, 1.00f, 0.60f, CrashDust.Kind.Powder);
        CrashDust.Look l = CrashDust.For((int)WorldTerrain.Surface.Snow);
        Assert.True(l.R > 0.9f && l.G > 0.9f && l.B > 0.9f, "snow dust must be white");
    }

    [Fact] public void IceThrowsUpLightGreyChips() => Is(CrashDust.For((int)WorldTerrain.Surface.GlacierIce), 0.84f, 0.88f, 0.92f, 0.55f, CrashDust.Kind.Chips);
    [Fact] public void WaterThrowsUpBlueWhiteSpray() => Is(CrashDust.For(ContactPoint.SurfaceWater), 0.88f, 0.94f, 1.00f, 0.60f, CrashDust.Kind.Spray);
    [Fact] public void PavementIsGrey() => Is(CrashDust.For((int)WorldTerrain.Surface.Paved), 0.62f, 0.60f, 0.58f, 0.35f, CrashDust.Kind.Dust);
    [Fact] public void GravelIsBrown() => Is(CrashDust.For((int)WorldTerrain.Surface.Gravel), 0.55f, 0.46f, 0.34f, 0.45f, CrashDust.Kind.Dust);

    [Fact]
    public void GrassAndPastureAreGreenBrown()
    {
        Is(CrashDust.For((int)WorldTerrain.Surface.Grass), 0.42f, 0.44f, 0.26f, 0.40f, CrashDust.Kind.Dust);
        Is(CrashDust.For((int)WorldTerrain.Surface.Rough), 0.42f, 0.44f, 0.26f, 0.40f, CrashDust.Kind.Dust);
    }

    [Fact]
    public void UnknownGroundKeepsTheOldBrown()
    {
        Is(CrashDust.For(ContactPoint.SurfaceNone), 0.50f, 0.42f, 0.30f, 0.50f, CrashDust.Kind.Dust);
        Is(CrashDust.For(99), 0.50f, 0.42f, 0.30f, 0.50f, CrashDust.Kind.Dust);
    }

    /// <summary>The mechanism, not the table: the snow dust came out brown because the emitter's material drew its own brown
    /// tint and ignored the particle's colour. The emitter must take its colour from this table and draw it with a shader
    /// that multiplies the particle colour in (FlyingGame/Spray: <c>o.color = v.color * _Color</c>).</summary>
    [Fact]
    public void TheEmitterDrawsTheTablesColour()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        string unity = Path.Combine(d!.FullName, "unity", "Assets", "_Project");
        string fx = File.ReadAllText(Path.Combine(unity, "Scripts", "Bridge", "ContactEffects.cs"));
        Assert.Contains("CrashDust.For(", fx);
        Assert.Contains("Shader.Find(\"FlyingGame/Spray\")", fx);
        Assert.DoesNotContain("new Color(0.55f, 0.47f, 0.36f", fx);   // the old brown material tint
        string spray = File.ReadAllText(Path.Combine(unity, "Shaders", "Spray.shader"));
        Assert.Contains("o.color = v.color * _Color", spray);
    }
}

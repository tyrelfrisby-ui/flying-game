using System;
using System.Collections.Generic;
using FlyingGame.Core;
using Xunit;

/// <summary>Where the low weather goes (Ty 2026-11-05 08:39; CloudSites). Literals copied by hand from the first
/// placement run (2026-11-05), so moving a site is a deliberate edit here too.</summary>
public class CloudSitesTests
{
    private static T WithIsland<T>(Func<T> f)
    {
        try { WorldTerrain.Active = new WorldTerrain(); CloudSites.Reset(); return f(); }
        finally { WorldTerrain.Active = null; CloudSites.Reset(); }
    }

    [Fact]
    public void FogLiesOnSomeGrassStripsAndSomeLakesNotAll()
    {
        var ids = WithIsland(() => { var l = new List<string>(); foreach (var f in CloudSites.Fog) l.Add((f.Lake ? "lake:" : "strip:") + f.Id); return l; });
        Assert.Equal(new[] {
            "strip:AP4", "strip:AP5", "strip:AP14", "strip:Thomas Creek",
            "lake:Payette Lake", "lake:Lake Powell", "lake:Bighorn Larch", "lake:Bighorn Cirque", "lake:Bighorn Crown" }, ids);
        // "On occasion": 14 turf/grass strips on the island (13 without Drake's cave strip), 10 lakes.
        int turf = 0; foreach (var f in IslandTerrain.Fields) if (StripFinish.LiesNaturally(f.Surface)) turf++;
        Assert.Equal(14, turf);
    }

    [Fact]
    public void FogSitsOnTheSurfaceItCovers()
    {
        var (ap4, payette) = WithIsland(() =>
        {
            CloudSites.FogSite a = default, p = default;
            foreach (var f in CloudSites.Fog) { if (f.Id == "AP4") a = f; if (f.Id == "Payette Lake") p = f; }
            return (a, p);
        });
        Assert.Equal(2300, ap4.X, 0); Assert.Equal(6700, ap4.Z, 0); Assert.Equal(144, ap4.HeadingDeg, 0);
        Assert.Equal(275.6, ap4.SurfaceM, 1);
        Assert.InRange(ap4.DepthM, 5, 9);
        // Payette Lake's water is 4900 ft.
        Assert.Equal(4900 * 0.3048, payette.SurfaceM, 3);
        Assert.True(payette.Lake);
        Assert.InRange(payette.DepthM, 6, 11);
    }

    [Fact]
    public void LowPatchesAreSparseAndBetween150And600mAboveTheGround()
    {
        var low = WithIsland(() => new List<CloudSites.LowPatch>(CloudSites.Low));
        int puffy = 0, wisp = 0;
        foreach (var p in low)
        {
            if (p.Puffy) puffy++; else wisp++;
            Assert.InRange(p.AglM, 150, 600);
            // Clear of the highest ground under the whole patch, by the AGL.
            double top = WithIsland(() => CloudSites.GroundTop(p.X, p.Z, p.RadiusM));
            Assert.Equal(top + p.AglM, p.BaseM, 6);
        }
        Assert.Equal(13, puffy);
        Assert.Equal(13, wisp);
    }

    [Fact]
    public void PlacementIsTheSameEveryTime()
    {
        string Fingerprint() => WithIsland(() =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in CloudSites.Low) sb.Append($"{p.X:F1},{p.Z:F1},{p.BaseM:F1};");
            foreach (var f in CloudSites.Fog) sb.Append(f.Id).Append(';');
            return sb.ToString();
        });
        Assert.Equal(Fingerprint(), Fingerprint());
        Assert.Equal(0.0, CloudSites.Hash01("AP4", 1) - CloudSites.Hash01("AP4", 1));
    }
}

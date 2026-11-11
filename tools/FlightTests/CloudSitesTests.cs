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
    public void FogCanLieOnEveryGrassStripEveryLakeAndTheValleyPools()
    {
        // Where fog CAN lie; whether it does is the island weather's call (Weather.FogModel; IslandWeatherTests).
        var ids = WithIsland(() => { var l = new List<string>(); foreach (var f in CloudSites.Fog) l.Add((f.Lake ? "pool:" : "strip:") + f.Id); return l; });
        Assert.Equal(new[] {
            "strip:3U2 19/1", "strip:KMEV 13G/31G", "strip:AP1", "strip:AP3", "strip:AP4", "strip:AP5", "strip:AP6", "strip:AP8",
            "strip:AP14", "strip:Chamberlain", "strip:Big Creek", "strip:Thomas Creek", "strip:Sulphur Creek",
            "pool:Payette Lake", "pool:Lake Hood", "pool:Knik ice lake", "pool:Lake Powell", "pool:Bighorn Outlet", "pool:Bighorn Shelf",
            "pool:Bighorn Larch", "pool:Bighorn Hidden", "pool:Bighorn Cirque", "pool:Bighorn Crown",
            "pool:Valley 1", "pool:Valley 2", "pool:Valley 3", "pool:Valley 4", "pool:Valley 5", "pool:Valley 6", "pool:Valley 7" }, ids);
        // 14 turf/grass strips on the island (13 without Drake's cave strip), 10 lakes.
        int turf = 0; foreach (var f in IslandTerrain.Fields) if (StripFinish.LiesNaturally(f.Surface)) turf++;
        Assert.Equal(14, turf);
    }

    [Fact]
    public void ValleyPoolsSitOnTheDeepestValleyFloors()
    {
        var v = WithIsland(() => new List<CloudSites.FogSite>(CloudSites.Fog).FindAll(f => f.Id.StartsWith("Valley ")));
        // (Unity x east, z north, floor m MSL), deepest first: 701, 510, 329, 220, 174, 156, 80 m below their surroundings.
        var want = new (double x, double z, double floor)[] { (-5000, -1000, 30.5), (2000, -6000, 15.6), (3000, 4000, 231.1),
            (-4000, 2000, 346.5), (-7000, -5000, 147.9), (-6000, 5000, 34.8), (7000, 1000, 85.3) };
        Assert.Equal(want.Length, v.Count);
        for (int i = 0; i < want.Length; i++)
        {
            Assert.Equal(want[i].x, v[i].X, 6); Assert.Equal(want[i].z, v[i].Z, 6); Assert.Equal(want[i].floor, v[i].SurfaceM, 1);
            Assert.Equal(CloudSites.ValleyRadiusM, v[i].HalfLength, 6);
        }
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Airport lighting (Ty 2026-11-05 08:44: "only airports with instrument approaches have vasi or papi make sure
/// both are represented at one airport or more the rest of the airports have no visual aids and roughly half have
/// runway lights for night flying"): who has what (<see cref="AirportLighting.Table"/>) and what colour a pilot sees
/// from where (<see cref="PapiPart"/>, <see cref="VasiPart"/>, <see cref="EdgeLightPart"/>).</summary>
public class AirportLightingTests
{
    private readonly ITestOutputHelper _out;
    public AirportLightingTests(ITestOutputHelper o) => _out = o;

    // Copied by hand from docs/pending/island-spec.json ifr.approaches (12 procedures at these 8 airports).
    private static readonly string[] InstrumentAirports = { "PANC", "KSNA", "PHDH", "KMYL", "KMEV", "KPGA", "KAVX", "PATK" };
    // ifr.vfr_only: "3U2", "PALH", "AP1-AP14".
    private static readonly string[] VfrOnly = { "3U2", "PALH", "AP1", "AP2", "AP3", "AP4", "AP5", "AP6", "AP7", "AP8", "AP9", "AP10", "AP11", "AP12", "AP13", "AP14" };

    private static string[] Airports() => IslandTerrain.Fields.Where(WindsockPart.IsAirport).Select(f => f.Field).Distinct().ToArray();

    // ---- who has what -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryAirportHasExactlyOnePlan()
    {
        var airports = Airports();
        Assert.Equal(39, airports.Length);
        Assert.Equal(airports.OrderBy(a => a), AirportLighting.Table.Select(p => p.Airport).OrderBy(a => a));
    }

    [Fact]
    public void OnlyInstrumentApproachAirportsHaveAGlidePathAid()
    {
        Assert.Equal(InstrumentAirports.OrderBy(a => a), AirportLighting.Table.Where(p => p.InstrumentApproach).Select(p => p.Airport).OrderBy(a => a));
        foreach (string v in VfrOnly) Assert.False(AirportLighting.HasInstrumentApproach(v), v);
        foreach (var p in AirportLighting.Table)
        {
            if (p.InstrumentApproach) { Assert.NotEqual(GlidePathAid.None, p.Aid); Assert.NotEmpty(p.AidEnds); }
            else { Assert.Equal(GlidePathAid.None, p.Aid); Assert.Empty(p.AidEnds); }
        }
        var units = AirportLighting.GlidePathUnits;
        Assert.All(units, u => Assert.True(AirportLighting.HasInstrumentApproach(IslandTerrain.Fields.Single(f => f.Id == u.FieldId).Field), u.Id));
        // WorldTerrain's airport list carries the same flag.
        foreach (var a in WorldTerrain.Airports) Assert.Equal(InstrumentAirports.Contains(a.Name), a.InstrumentApproach);
    }

    [Fact]
    public void BothPapiAndVasiAreRepresented()
    {
        var units = AirportLighting.GlidePathUnits;
        Assert.Equal(new[] { "KAVX", "PATK" }, AirportLighting.Table.Where(p => p.Aid == GlidePathAid.Vasi).Select(p => p.Airport).OrderBy(a => a));
        Assert.Equal(6, AirportLighting.Table.Count(p => p.Aid == GlidePathAid.Papi));
        // Eight PAPI rows of 4 (PANC and KPGA both ends) and three VASIs of 4 boxes (KAVX both ends).
        Assert.Equal(8 * 4, units.Count(u => u.Kind == GlidePathAid.Papi));
        Assert.Equal(3 * 4, units.Count(u => u.Kind == GlidePathAid.Vasi));
    }

    [Fact]
    public void RoughlyHalfTheAirportsHaveRunwayLights()
    {
        int lit = AirportLighting.Table.Count(p => p.RunwayLights);
        Assert.Equal(19, lit);
        Assert.InRange(lit / (double)AirportLighting.Table.Length, .4, .6);
        Assert.All(AirportLighting.Table.Where(p => p.InstrumentApproach), p => Assert.True(p.RunwayLights, p.Airport));
        var lamps = AirportLighting.EdgeLamps;
        var litFields = lamps.Select(l => l.FieldId).Distinct().ToList();
        foreach (string f in litFields) Assert.True(AirportLighting.For(IslandTerrain.Fields.Single(q => q.Id == f).Field).RunwayLights, f);
        // Every lit airport's hard runways are lit; KMEV's grass 13G/31G is not.
        Assert.DoesNotContain("KMEV 13G/31G", litFields);
        Assert.Contains("KMEV 13/31", litFields);
        Assert.Equal(19, litFields.Select(f => IslandTerrain.Fields.Single(q => q.Id == f).Field).Distinct().Count());
        _out.WriteLine($"{lamps.Count} lamps on {litFields.Count} runways: {string.Join(", ", litFields)}");
    }

    // ---- PAPI: colour from the eye's angle ------------------------------------------------------------------------

    private static Vec3 EyeOnLine(IReadOnlyList<GlidePathUnit> row, double deg, double rangeM, double offAxisDeg = 0)
    {
        double x = row.Average(u => u.Point.X), y = row.Average(u => u.Point.Y), lens = row[0].LensM;
        double h = (row[0].FacingDeg + offAxisDeg) * Math.PI / 180;
        return new Vec3(x + Math.Cos(h) * rangeM, y + Math.Sin(h) * rangeM, -(lens + rangeM * Math.Tan(deg * Math.PI / 180)));
    }
    private static IEnumerable<IReadOnlyList<GlidePathUnit>> Rows(GlidePathAid kind)
        => AirportLighting.GlidePathUnits.Where(u => u.Kind == kind).GroupBy(u => u.FieldId + ":" + u.Runway).Select(g => (IReadOnlyList<GlidePathUnit>)g.OrderBy(u => u.Index).ToList());

    [Theory]
    [InlineData(4.0, 4)]    // high
    [InlineData(3.35, 3)]   // slightly high
    [InlineData(3.0, 2)]    // on the path
    [InlineData(2.65, 1)]   // slightly low
    [InlineData(2.0, 0)]    // low
    public void PapiShowsWhiteBoxesByTheEyesAngle(double deg, int white)
    {
        foreach (var row in Rows(GlidePathAid.Papi))
        foreach (double range in new[] { 800.0, 1852, 5000 })
        {
            var eye = EyeOnLine(row, deg, range);
            Assert.Equal(white, PapiPart.WhiteCount(row, eye));
            // The red boxes are always the ones nearest the runway.
            for (int i = 0; i < 4; i++) Assert.Equal(i >= 4 - white, row[i].WhiteFraction(eye) > .5);
            Assert.All(row, u => Assert.Equal(1, u.Intensity(eye)));
        }
    }

    [Fact]
    public void PapiChangesAtItsSettingAnglesAndNowhereElse()
    {
        var row = Rows(GlidePathAid.Papi).First();
        int last = 0; var changes = new List<double>();
        for (double deg = 1.5; deg <= 4.5; deg += .005)
        {
            int w = PapiPart.WhiteCount(row, EyeOnLine(row, deg, 3000));
            Assert.True(w >= last, "climbing never turns a box red");
            if (w != last) changes.Add(deg);
            last = w;
        }
        Assert.Equal(new[] { 2.5, 2 + 50 / 60.0, 3 + 10 / 60.0, 3.5 }, changes.ToArray(), new Tol(.01));
        // Inside the pink band a box is between red and white: the change is a gradient across 3 arc-minutes.
        var mid = row[3]; var eye = EyeOnLine(row, 2.5, 3000);
        Assert.InRange(mid.WhiteFraction(eye), .2, .8);
        Assert.Equal(0, mid.WhiteFraction(EyeOnLine(row, 2.5 - 2.0 / 60, 3000)));
        Assert.Equal(1, mid.WhiteFraction(EyeOnLine(row, 2.5 + 2.0 / 60, 3000)));
    }

    [Fact]
    public void PapiPathCrossesTheThresholdAtFortyFeet()
    {
        foreach (var row in Rows(GlidePathAid.Papi))
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == row[0].FieldId);
            int s = AirportLighting.EndSide(f, row[0].Runway);
            var thr = f.Centre + f.U * (s * f.Length / 2000);
            double d = Math.Sqrt(Math.Pow(thr.X - row[0].Point.X, 2) + Math.Pow(thr.Y - row[0].Point.Y, 2));
            double along = Math.Abs((thr - row[0].Point).Dot(f.U)) * 1000;
            double tch = row[0].LensM + along * Math.Tan(3 * Math.PI / 180) - f.Plane(thr);
            Assert.InRange(tch, 11.5, 13.0);
            _out.WriteLine($"{row[0].FieldId}:{row[0].Runway} row {along:F0} m in, TCH {tch / .3048:F1} ft, lens {row[0].LensM - row[0].GroundM:F2} m above its ground");
        }
    }

    [Fact]
    public void AGlidePathAidIsDarkFromBehindAndFarOffTheApproach()
    {
        var row = Rows(GlidePathAid.Papi).First();
        Assert.Equal(1, row[0].Intensity(EyeOnLine(row, 3, 2000, 9)));
        Assert.Equal(0, row[0].Intensity(EyeOnLine(row, 3, 2000, 16)));
        Assert.Equal(0, row[0].Intensity(EyeOnLine(row, 3, 2000, 180)));
    }

    // ---- VASI -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(3.0, VasiPart.Picture.RedOverWhite)]
    [InlineData(2.4, VasiPart.Picture.AllRed)]
    [InlineData(3.7, VasiPart.Picture.AllWhite)]
    public void VasiIsRedOverWhiteOnThePath(double deg, VasiPart.Picture picture)
    {
        // Ranges from 1 NM out: closer in, the band between the near bar's 2.75° line and the far bar's 3.25° line
        // widens (they are 213 m apart), as on a real VASI; VasiStaysRedOverWhite... flies the path itself to the threshold.
        foreach (var bars in Rows(GlidePathAid.Vasi))
        foreach (double range in new[] { 1852.0, 3700, 7400 })
            Assert.Equal(picture, VasiPart.See(bars, EyeOnLine(bars, deg, range)));
    }

    [Fact]
    public void VasiStaysRedOverWhiteDownTheLineToTheThreshold()
    {
        foreach (var bars in Rows(GlidePathAid.Vasi))
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == bars[0].FieldId);
            int s = AirportLighting.EndSide(f, bars[0].Runway);
            var thr = f.Centre + f.U * (s * f.Length / 2000);
            double mid = (VasiPart.DownwindFromThresholdM + VasiPart.BarSeparationM / 2);
            // From the threshold out to 4 NM, on the 3° line through the point between the bars.
            for (double back = 0; back <= 7400; back += 100)
            {
                double r = mid + back;
                Assert.Equal(VasiPart.Picture.RedOverWhite, VasiPart.See(bars, EyeOnLine(bars, 3, r)));
            }
            _out.WriteLine($"{bars[0].FieldId}:{bars[0].Runway} bars {f.Length:F0} m runway, TCH {bars[0].LensM + mid * Math.Tan(3 * Math.PI / 180) - f.Plane(thr):F1} m");
        }
    }

    // ---- edge lights ------------------------------------------------------------------------------------------------

    [Fact]
    public void EdgeLightsAreWhiteWithYellowCautionZonesOnlyOnInstrumentRunways()
    {
        foreach (var g in AirportLighting.EdgeLamps.GroupBy(l => l.FieldId))
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == g.Key);
            bool ins = AirportLighting.IsInstrumentRunway(f);
            double caution = Math.Min(EdgeLightPart.CautionZoneM, f.Length / 2);
            var edges = g.Where(l => !l.Threshold).ToList();
            Assert.NotEmpty(edges);
            foreach (var l in edges)
            {
                double along = Math.Round((l.Point - f.Centre).Dot(f.U) * 1000, 6);
                Vec3 fromMinus = Eye(l, f, -3000), fromPlus = Eye(l, f, 3000);
                if (ins && along > f.Length / 2 - caution)
                {   // heading for the +U end: the pilot is on the -U side
                    Assert.Equal(LampColour.Yellow, l.SeenFrom(fromMinus)); Assert.Equal(LampColour.White, l.SeenFrom(fromPlus));
                }
                else if (ins && along < -f.Length / 2 + caution)
                {
                    Assert.Equal(LampColour.White, l.SeenFrom(fromMinus)); Assert.Equal(LampColour.Yellow, l.SeenFrom(fromPlus));
                }
                else { Assert.Equal(LampColour.White, l.SeenFrom(fromMinus)); Assert.Equal(LampColour.White, l.SeenFrom(fromPlus)); }
            }
            // Spacing no more than 200 ft.
            var side = edges.Where(l => (l.Point - f.Centre).Dot(f.V) > 0).Select(l => (l.Point - f.Centre).Dot(f.U) * 1000).OrderBy(a => a).ToList();
            for (int i = 1; i < side.Count; i++) Assert.True(side[i] - side[i - 1] <= EdgeLightPart.MaxSpacingM + 1e-6, $"{f.Id} gap {side[i] - side[i - 1]:F1} m");
            _out.WriteLine($"{f.Id,-14} {(ins ? "instrument" : "visual"),-10} {edges.Count,3} edge, {g.Count(l => l.Threshold),2} threshold/end, yellow {edges.Count(l => !l.Omni)}");
        }
    }

    [Fact]
    public void ThresholdLightsAreGreenFromTheApproachAndRedFromTheRunway()
    {
        foreach (var l in AirportLighting.EdgeLamps.Where(l => l.Threshold))
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == l.FieldId);
            double along = (l.Point - f.Centre).Dot(f.U) * 1000;
            Assert.True(Math.Abs(along) > f.Length / 2, "outside the runway end");
            Vec3 approach = Eye(l, f, Math.Sign(along) * 2000), runway = Eye(l, f, -Math.Sign(along) * 500);
            Assert.Equal(LampColour.Green, l.SeenFrom(approach));
            Assert.Equal(LampColour.Red, l.SeenFrom(runway));
        }
    }

    private static Vec3 Eye(EdgeLamp l, IslandField f, double alongM)
    {
        var p = l.Point + f.U * (alongM / 1000);
        return new Vec3(p.X, p.Y, -(l.GroundM + 50));
    }

    // ---- siting -----------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryBoxStandsOnDryGroundClearOfSocksAndBuildings()
    {
        IslandGeometry.Build();
        foreach (var u in AirportLighting.GlidePathUnits)
        {
            // WaterAt reports a lake's level wherever its basin reaches; the box is dry if its ground stands above it.
            double? water = IslandTerrain.WaterAt(u.Point.X, u.Point.Y);
            Assert.True(water is null || u.GroundM > water + 1, $"{u.Id} ground {u.GroundM:F1} m, water {water:F1} m");
            double pier = u.LensM - u.GroundM;
            Assert.True(pier > .3 && pier < 3, $"{u.Id}: lens {pier:F2} m above its ground");
            foreach (var s in IslandGeometry.Windsocks)
                Assert.True(Math.Sqrt(Math.Pow(s.X - u.Point.X, 2) + Math.Pow(s.Y - u.Point.Y, 2)) > 10, $"{u.Id} stands on {s.Id}");
            foreach (var b in IslandGeometry.Blocks)
                Assert.True(WindsockPart.DistanceToBlock(b, u.Point.X, u.Point.Y) > 3 || b.Top < u.GroundM - 1, $"{u.Id} inside block {b.Id}");
            _out.WriteLine($"{u.Id,-26} lens {pier:F2} m up");
        }
        foreach (var l in AirportLighting.EdgeLamps) Assert.True(double.IsFinite(l.GroundM));
    }

    // ---- one shared part each ---------------------------------------------------------------------------------------

    [Fact]
    public void EachAidIsOneSharedPart()
    {
        string root = FindRoot();
        var scripts = Directory.GetFiles(Path.Combine(root, "unity/Assets/_Project/Scripts/Bridge"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "src/FlyingGame.Core"), "*.cs", SearchOption.AllDirectories));
        foreach (string file in scripts)
        {
            string name = Path.GetFileName(file);
            if (name is "AirportLights.cs" or "AirportLighting.cs" || name.EndsWith(".Generated.cs")) continue;
            foreach (string line in File.ReadAllLines(file))
            {
                string code = line.Split("//")[0];
                Assert.False(Regex.IsMatch(code, @"(?i)\b(papi|vasi)"), $"{name} builds a glide path aid: {line.Trim()}");
                Assert.DoesNotContain("EdgeLightPart.", code);
                Assert.DoesNotContain("AirportLighting.GlidePathUnits", code);
            }
        }
        var glbs = Directory.GetFiles(Path.Combine(root, "unity/Assets"), "*.glb", SearchOption.AllDirectories);
        Assert.DoesNotContain(glbs, g => Regex.IsMatch(Path.GetFileName(g), "(?i)papi|vasi|runway-light"));
        string bridge = File.ReadAllText(Path.Combine(root, "unity/Assets/_Project/Scripts/Bridge/AirportLights.cs"));
        Assert.Contains("WhiteFraction(", bridge);   // colour from the eye, every frame
        Assert.Contains("AirportLights.Build(", File.ReadAllText(Path.Combine(root, "unity/Assets/_Project/Scripts/Bridge/IslandWorldBuilder.cs")));
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "unity/Assets"))) d = d.Parent;
        return d!.FullName;
    }

    private sealed class Tol : IEqualityComparer<double>
    {
        private readonly double _t; public Tol(double t) => _t = t;
        public bool Equals(double a, double b) => Math.Abs(a - b) <= _t;
        public int GetHashCode(double v) => 0;
    }
}

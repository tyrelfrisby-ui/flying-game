using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Windsocks (Ty 2026-11-05: "every airport has a wind sock off to the side of the runway at each end and in
/// the middle"): where they stand (<see cref="WindsockPart"/>) and how they move (<see cref="WindsockDynamics"/>).</summary>
public class WindsockTests
{
    private readonly ITestOutputHelper _out;
    public WindsockTests(ITestOutputHelper o) => _out = o;
    private const double Kt = 0.514444;

    private static List<WindsockPart.Site> Built()
    {
        IslandGeometry.Build();
        return IslandGeometry.Windsocks.ToList();
    }

    // ---- placement --------------------------------------------------------------------------------------------

    [Fact]
    public void EveryAirportRunwayHasThreeSocksAtBothEndsAndTheMiddle()
    {
        var socks = Built();
        var runways = IslandTerrain.Fields.Where(WindsockPart.IsAirport).ToList();
        Assert.Equal(43, runways.Count);   // 52 fields less 2 water lanes, Drake's cave strip and the six beaches
        Assert.Empty(WindsockPart.Unplaced(socks));
        Assert.Equal(3 * runways.Count, socks.Count);
        foreach (var f in runways)
        {
            var mine = socks.Where(s => s.FieldId == f.Id).ToList();
            Assert.Equal(3, mine.Count);
            var a = mine.Single(s => s.Station == WindsockPart.Station.ApproachEnd);
            var m = mine.Single(s => s.Station == WindsockPart.Station.Midfield);
            var b = mine.Single(s => s.Station == WindsockPart.Station.FarEnd);
            // Ends: abeam the last 30 % of each end; middle: abeam the middle fifth.
            Assert.True(a.AlongM < -.2 * f.Length && a.AlongM >= -f.Length / 2, $"{f.Id} approach-end sock at {a.AlongM:F0} m");
            Assert.True(b.AlongM > .2 * f.Length && b.AlongM <= f.Length / 2, $"{f.Id} far-end sock at {b.AlongM:F0} m");
            Assert.True(Math.Abs(m.AlongM) <= .1 * f.Length + 1, $"{f.Id} midfield sock at {m.AlongM:F0} m");
            _out.WriteLine($"{f.Id,-14} RSA ±{WindsockPart.RsaHalfWidthM(f),5:F1}  ends {a.AlongM,6:F0} {a.AcrossM,6:F1} | mid {m.AlongM,5:F0} {m.AcrossM,6:F1} | {b.AlongM,6:F0} {b.AcrossM,6:F1}");
        }
    }

    [Fact]
    public void EverySockIsOutsideEveryRunwaySafetyArea()
    {
        var socks = Built();
        foreach (var s in socks)
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == s.FieldId);
            Assert.True(Math.Abs(s.AcrossM) >= WindsockPart.RsaHalfWidthM(f) + WindsockPart.RsaSetbackM - 1e-9, $"{s.Id} {s.AcrossM:F1} m off the centreline");
            foreach (var other in IslandTerrain.Fields) Assert.False(WindsockPart.InsideRsa(other, s.Point), $"{s.Id} stands in {other.Id}'s safety area");
        }
    }

    [Fact]
    public void RunwaySafetyAreasAreTheFaaWidths()
    {
        // Hand-copied from AC 150/5300-13B: RSA 120 ft (A/B-I small), 150 ft (B-II), 500 ft (C/D).
        IslandField F(string id) => IslandTerrain.Fields.Single(f => f.Id == id);
        Assert.Equal(60 * 0.3048, WindsockPart.RsaHalfWidthM(F("AP1")), 6);          // 18 m grass
        Assert.Equal(60 * 0.3048, WindsockPart.RsaHalfWidthM(F("PHDH 8/26")), 6);    // 75 ft paved
        Assert.Equal(75 * 0.3048, WindsockPart.RsaHalfWidthM(F("KMEV 17/35")), 6);   // 100 ft paved
        Assert.Equal(75 * 0.3048, WindsockPart.RsaHalfWidthM(F("3U2 19/1")), 6);     // 150 ft turf: still small aeroplanes
        Assert.Equal(250 * 0.3048, WindsockPart.RsaHalfWidthM(F("PANC 16/34")), 6);  // 200 ft paved
        Assert.Equal(240 * 0.3048, WindsockPart.RsaBeyondEndM(F("AP1")), 6);
        Assert.Equal(1000 * 0.3048, WindsockPart.RsaBeyondEndM(F("PANC 16/34")), 6);
    }

    [Fact]
    public void SocksStandOnTheGroundClearOfBuildingsWaterAndEachOther()
    {
        var socks = Built();
        foreach (var s in socks)
        {
            Assert.Equal(IslandTerrain.HeightKm(s.Point), s.GroundM);
            Assert.Null(IslandTerrain.WaterAt(s.X, s.Y));
            foreach (var b in IslandGeometry.Blocks)
                Assert.True(WindsockPart.DistanceToBlock(b, s.X, s.Y) >= WindsockPart.ObstacleClearM, $"{s.Id} is {WindsockPart.DistanceToBlock(b, s.X, s.Y):F1} m from {b.Id}");
            foreach (var t in socks)
                if (!ReferenceEquals(s.Id, t.Id) && s.Id != t.Id)
                    Assert.True(Math.Sqrt(Math.Pow(s.X - t.X, 2) + Math.Pow(s.Y - t.Y, 2)) > 20, $"{s.Id} and {t.Id} overlap");
        }
    }

    [Fact]
    public void EachMastIsASolidAndTheOldStaticSocksAreGone()
    {
        var socks = Built();
        Assert.DoesNotContain(IslandGeometry.Blocks, b => b.Id.Contains("windsock"));
        Assert.DoesNotContain(IslandGeometry.Cables, c => c.Id.Contains("windsock"));
        var s = socks[0];
        Assert.True(WorldSolids.Penetration(s.X, s.Y, s.GroundM + 3).HasValue, "an aeroplane that hits a mast hits it");
    }

    [Fact]
    public void TheSockIsOneSharedPart()
    {
        string root = FindRoot();
        // Only Bridge/Windsock.cs draws a sock; nothing else builds one or instantiates the component.
        var scripts = Directory.GetFiles(Path.Combine(root, "unity/Assets/_Project/Scripts"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories));
        foreach (string file in scripts)
        {
            string name = Path.GetFileName(file), text = File.ReadAllText(file);
            if (name is "Windsock.cs" or "WindsockPart.cs" or "WindsockShots.cs") continue;
            Assert.False(text.Contains("ConeMesh(") && text.Contains("indsock"), $"{name} builds its own windsock");
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"\bWindsock\s*\("), $"{name} calls a windsock builder");
            Assert.DoesNotContain("AddComponent<Windsock>", text);
        }
        var glbs = Directory.GetFiles(Path.Combine(root, "unity/Assets"), "*.glb", SearchOption.AllDirectories);
        Assert.DoesNotContain(glbs, g => Path.GetFileName(g).Contains("sock", StringComparison.OrdinalIgnoreCase));
        // The sock reads the air through the one query aeroplanes fly in.
        string bridge = File.ReadAllText(Path.Combine(root, "unity/Assets/_Project/Scripts/Bridge/Windsock.cs"));
        Assert.Contains("WindsockPart.AirAt(", bridge);
        Assert.Contains("Atmosphere.WindAtPosition(", File.ReadAllText(Path.Combine(root, "src/FlyingGame.Core/WindsockPart.cs")));
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "unity/Assets"))) d = d.Parent;
        return d!.FullName;
    }

    // ---- motion -----------------------------------------------------------------------------------------------

    /// <summary>Wind blowing FROM <paramref name="fromDeg"/> at <paramref name="kt"/> (NED, m/s, the way the air moves).</summary>
    private static Vec3 From(double fromDeg, double kt)
    {
        double to = (fromDeg + 180) * Math.PI / 180;
        return new Vec3(Math.Cos(to) * kt * Kt, Math.Sin(to) * kt * Kt, 0);
    }
    private static WindsockDynamics Settled(Vec3 w, double seconds = 30, double azimuth = 0)
    {
        var s = new WindsockDynamics(azimuth);
        for (int i = 0; i < seconds * 60; i++) s.Step(w, 1.225, 1 / 60.0);
        return s;
    }
    private static double AngleOff(double a, double b) { double d = (a - b + 540) % 360 - 180; return Math.Abs(d); }

    [Fact]
    public void InACalmItHangs()
    {
        var s = Settled(Vec3.Zero);
        Assert.Equal(0, s.Fill, 9);
        Assert.Equal(80, s.ChordDroopDeg(), 6);
    }

    [Fact]
    public void AtFifteenKnotsItIsFullyExtended()
    {
        var s = Settled(From(270, 15), azimuth: 90);
        Assert.True(s.Fill > .999, $"fill {s.Fill:F4}");
        Assert.True(s.ChordDroopDeg() < .1, $"droop {s.ChordDroopDeg():F2}°");
        Assert.True(AngleOff(s.AzimuthDeg, 90) < 1, $"points {s.AzimuthDeg:F1}° in a west wind");
        var t = Settled(From(270, 25), azimuth: 90);
        Assert.True(t.ChordDroopDeg() < .1, "and stays out above 15 kt");
    }

    [Fact]
    public void DroopFallsMonotonicallyWithWindSpeed()
    {
        double last = double.MaxValue;
        for (int kt = 0; kt <= 20; kt++)
        {
            double d = Settled(From(0, kt), azimuth: 180).ChordDroopDeg();
            _out.WriteLine($"{kt,3} kt  droop {d,5:F1}°");
            if (kt <= 15) Assert.True(d < last || kt == 0, $"{kt} kt droop {d:F2}° not below {last:F2}°");
            else Assert.True(d <= last + 1e-9);
            last = d;
        }
        // Each band is 3 kt: at 9 kt three bands stand out and two hang.
        var nine = Settled(From(0, 9), azimuth: 180);
        Assert.Equal(.6, nine.Fill, 3);
    }

    [Theory]
    [InlineData(0)] [InlineData(45)] [InlineData(135)] [InlineData(200)] [InlineData(290)]
    public void ItPointsDownwind(double fromDeg)
    {
        double downwind = (fromDeg + 180) % 360;
        var s = Settled(From(fromDeg, 10), 30, azimuth: downwind + 100);
        Assert.True(AngleOff(s.AzimuthDeg, downwind) < 2, $"wind from {fromDeg}: points {s.AzimuthDeg:F1}°, downwind is {downwind}°");
    }

    [Fact]
    public void ItSwingsToTheWindAtThreeKnots()
    {
        var s = Settled(From(0, 3), 60, azimuth: 60);
        Assert.True(AngleOff(s.AzimuthDeg, 180) < 20, $"3 kt: points {s.AzimuthDeg:F1}°");
        var t = Settled(From(0, 1), 60, azimuth: 60);
        Assert.True(AngleOff(t.AzimuthDeg, 60) < 1, "1 kt does not turn the swivel");
    }

    [Fact]
    public void AGustFillsItAndAShiftSwingsIt()
    {
        var s = Settled(From(0, 6), 30, azimuth: 180);
        double before = s.ChordDroopDeg();
        // A 15 kt gust: filled within about a second and a half.
        double t = 0; while (s.Fill < .9 && t < 5) { s.Step(From(0, 15), 1.225, 1 / 60.0); t += 1 / 60.0; }
        _out.WriteLine($"6→15 kt: 90 % full in {t:F2} s (droop {before:F1}° → {s.ChordDroopDeg():F1}°)");
        Assert.True(t < 1.5, $"took {t:F2} s");
        // It empties again when the gust passes.
        for (int i = 0; i < 300; i++) s.Step(From(0, 6), 1.225, 1 / 60.0);
        Assert.Equal(before, s.ChordDroopDeg(), 1);
        // A 60° wind shift at 10 kt: round within two seconds, overshooting a little, never wildly.
        var v = Settled(From(0, 10), 30, azimuth: 180);
        double peak = 0, settledAt = double.NaN;
        for (int i = 0; i < 600; i++)
        {
            v.Step(From(60, 10), 1.225, 1 / 60.0);
            double off = (v.AzimuthDeg - 240 + 540) % 360 - 180;
            peak = Math.Max(peak, off);
            if (double.IsNaN(settledAt) && Math.Abs(off) < 10) settledAt = i / 60.0;
        }
        _out.WriteLine($"60° shift: within 10° at {settledAt:F2} s, overshoot {peak:F1}°");
        Assert.True(settledAt < 2, $"{settledAt:F2} s");
        Assert.True(peak > 0 && peak < 25, $"overshoot {peak:F1}°");
        Assert.True(AngleOff(v.AzimuthDeg, 240) < 2);
    }

    [Fact]
    public void ADownGustLeansTheFilledSockDown()
    {
        var w = From(0, 15); w = new Vec3(w.X, w.Y, 2.0);   // 2 m/s down
        var s = Settled(w, azimuth: 170);
        Assert.Equal(Math.Atan2(2.0, 15 * Kt) * 180 / Math.PI, s.ChordDroopDeg(), 1);
    }

    [Fact]
    public void ItMovesWithTheFieldsOwnGusts()
    {
        // Moderate turbulence on a 10 kt west wind, read at Hilo's midfield sock through the same query the aeroplanes
        // fly in: the sock wanders with the gusts. The eddies are carried by the wind (Turbulence.MeanWind, Taylor's
        // frozen field) -- the game's own uniform air leaves MeanWind at zero, so its eddies stand still over the
        // ground and a still sock would see one constant gust (reported in AGENT-LOG, not changed here).
        var field = Atmosphere.Field; var steady = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence; double clock = Atmosphere.SimTimeSec;
        try
        {
            Atmosphere.Field = null; Atmosphere.SimTimeSec = 0; Atmosphere.SteadyWind = Vec3.Zero;
            Atmosphere.ActiveTurbulence = new Turbulence(4.0, 300.0, 3) { MeanWind = From(270, 10) };
            var site = Built().First(q => q.FieldId == "PHDH 8/26" && q.Station == WindsockPart.Station.Midfield);
            Assert.Equal(Atmosphere.WindAtPosition(site.Pivot), WindsockPart.AirAt(site.Pivot));
            var s = Settled(WindsockPart.AirAt(site.Pivot), 10, 90);
            var az = new List<double>(); var fill = new List<double>(); var speed = new List<double>();
            for (int i = 0; i < 120 * 60; i++)
            {
                Atmosphere.AdvanceTime(1 / 60.0);
                var w = WindsockPart.AirAt(site.Pivot);
                s.Step(w, 1.225, 1 / 60.0);
                az.Add((s.AzimuthDeg - 90 + 540) % 360 - 180); fill.Add(s.Fill); speed.Add(Math.Sqrt(w.X * w.X + w.Y * w.Y));
            }
            double sdAz = Sd(az), sdFill = Sd(fill);
            _out.WriteLine($"moderate turbulence, 10 kt W: wind {speed.Average() / Kt:F1} kt σ {Sd(speed) / Kt:F2} kt; sock azimuth σ {sdAz:F1}°, fill σ {sdFill:F3}, mean fill {fill.Average():F2}");
            Assert.True(sdAz > 3, $"azimuth σ {sdAz:F2}°");
            Assert.True(sdFill > .05, $"fill σ {sdFill:F3}");
            Assert.True(Math.Abs(az.Average()) < 20, $"still points downwind on average ({az.Average():F1}° off)");
        }
        finally
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = steady; Atmosphere.ActiveTurbulence = turb; Atmosphere.SimTimeSec = clock;
        }
    }
    private static double Sd(List<double> x) { double m = x.Average(); return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / x.Count); }
}

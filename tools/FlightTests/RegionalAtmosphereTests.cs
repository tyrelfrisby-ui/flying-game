using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The regional atmosphere (owner 2026-10-27). The calm start airport is EXACTLY calm ISA; the zone blends out smoothly
/// and monotonically; full regional air applies outside it and no thermal stands in it. Airport 2's light-and-variable
/// wind goes all the way round at about 5 kt without jumping; airport 3 is 5 kt gusting 15 from a steady direction. The
/// wind strengthens up the face of a bluff and the slope lift grows toward it; the lee has sink, rotor and (in stable
/// air) waves; turbulence grows toward the ground and dies with height; the spectral turbulence has the right
/// statistics; the aircraft feels a gradient across its span; and the whole thing is deterministic.
/// </summary>
[Collection("WorldTerrainActive")]   // swaps the global terrain and atmosphere
public class RegionalAtmosphereTests
{
    private readonly ITestOutputHelper _out;
    public RegionalAtmosphereTests(ITestOutputHelper output) => _out = output;

    private const double Kt = AtmosphereField.KtToMs;
    private const double PatternAfeM = 304.8;   // 1,000 ft above the field

    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }
    private static AtmosphereFieldConfig Cfg() =>
        AtmosphereFieldConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "atmosphere", "regional-atmosphere.json"));

    /// <summary>One global region over flat or synthetic ground: the physics without the map.</summary>
    private static AtmosphereField Simple(double fromDeg, double kt, Func<double, double, double>? terrain = null, double lapseCPerKm = 6.5, double turbulence = 1.0)
    {
        var c = new AtmosphereFieldConfig
        {
            Synoptic = new SynopticConfig { WindFromDeg = fromDeg, WindSpeedKt = kt },
            CalmZone = new CalmZoneConfig { Enabled = false },
            Regions = { new RegionConfig { Id = "test", Shape = "global", LapseRateCPerKm = lapseCPerKm, RoughnessM = 0.03 } },
            Convection = new ConvectionConfig { BackgroundUpdraftMs = 0 },
        };
        c.Turbulence.Scale = turbulence;
        var f = new AtmosphereField(c) { Heating = (_, _) => 0.0 };
        if (terrain != null) f.Terrain = terrain;
        return f;
    }

    private static T WithWorld<T>(Func<WorldTerrain, T> body)
    {
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        try { return body(t); }
        finally { WorldTerrain.Active = null; Atmosphere.Field = null; Atmosphere.Thermals.Clear(); Atmosphere.SimTimeSec = 0; }
    }

    // =================================================================================================================
    // The calm start airport
    // =================================================================================================================

    [Fact]
    public void CalmZoneIsTheDefaultStartAirportsAndCoversThePattern()
    {
        var f = new AtmosphereField(Cfg());
        AtmosphereField.CalmZone z = f.Calm!;
        WorldTerrain.Airport home = WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex];
        Assert.Equal("home-airport-calm", z.Id);
        Assert.Equal(home.Name, z.AirportName);
        Assert.Equal(home.ElevationM, z.FieldElevationM);
        Assert.Equal(home.X + WorldTerrain.AirportStrips[0].Dx, z.CentreX);   // the main runway's centre, not a typed number
        Assert.Equal(home.Y + WorldTerrain.AirportStrips[0].Dy, z.CentreY);
        Assert.True(z.CeilingAfeM >= PatternAfeM + 150, $"ceiling {z.CeilingAfeM} m AFE must clear the 1,000 ft pattern with margin");
        // The approach and departure paths: 1.5 km of final / climb-out beyond either threshold, and a downwind leg 1 km abeam.
        double halfRunway = WorldTerrain.RunwayLengthM / 2;
        Assert.True(z.HalfLengthM >= halfRunway + 1500, "the stadium must reach 1.5 km past each threshold");
        Assert.True(z.RadiusM >= 1000, "the stadium must hold a downwind leg 1 km abeam");
        _out.WriteLine($"calm zone: {z.Id} at {z.AirportName} centre ({z.CentreX}, {z.CentreY}) half-length {z.HalfLengthM} radius {z.RadiusM} ceiling {z.CeilingAfeM} AFE = {z.CeilingMslM} MSL blend {z.BlendWidthM}/{z.CeilingBlendM}");
    }

    private static IEnumerable<(string name, double x, double y, double afe)> PatternPoints(AtmosphereField.CalmZone z)
    {
        double c = Math.Cos(z.HeadingRad), s = Math.Sin(z.HeadingRad), half = WorldTerrain.RunwayLengthM / 2;
        (double, double) At(double along, double across) => (z.CentreX + along * c - across * s, z.CentreY + along * s + across * c);
        foreach (double afe in new[] { 0.5, 2, 10, 50, 150, PatternAfeM, PatternAfeM + 100 })
        {
            var p = At(0, 0); yield return ("runway centre", p.Item1, p.Item2, afe);
            p = At(-half, 0); yield return ("threshold S", p.Item1, p.Item2, afe);
            p = At(half, 0); yield return ("threshold N", p.Item1, p.Item2, afe);
        }
        foreach (double along in new[] { -2000.0, 0, 2000 })
            foreach (double side in new[] { -1000.0, 1000 })
            { var p = At(along, side); yield return ("downwind", p.Item1, p.Item2, PatternAfeM); }
        { var p = At(-half - 1500, 0); yield return ("final S", p.Item1, p.Item2, 150); }
        { var p = At(half + 1500, 0); yield return ("final N", p.Item1, p.Item2, 150); }
        { var p = At(half + 1500, 0); yield return ("climb-out N", p.Item1, p.Item2, 250); }
        WorldTerrain.Airport a = WorldTerrain.DefaultAirport;
        yield return ("cross runway E", a.X + WorldTerrain.XwindRunwayDx, a.Y + 560, 1);
        yield return ("cross runway W", a.X + WorldTerrain.XwindRunwayDx, a.Y - 560, 1);
    }

    [Fact]
    public void StartAirportIsExactlyCalmIsaFromTheSurfaceThroughThePattern()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            Atmosphere.Field = f;
            Atmosphere.IsaDeviationK = 0;
            // The Valley's own authored thermals, wherever they are, and a strong one just outside the zone whose sink ring
            // would reach inside: none may stir the calm air.
            Atmosphere.Thermals.Add(new Thermal(new Vec3(f.Calm!.CentreX, f.Calm.CentreY + f.Calm.RadiusM + f.Calm.BlendWidthM + 300, -f.Calm.FieldElevationM), 400, 12, f.Calm.FieldElevationM + 2000));
            int n = 0;
            foreach (double time in new[] { 0.0, 37.3, 1234.5, 7200 })
            {
                Atmosphere.SimTimeSec = time;
                foreach (var (name, x, y, afe) in PatternPoints(f.Calm))
                {
                    double alt = afe <= 50 ? t.HeightAt(x, y) + afe : f.Calm.FieldElevationM + afe;
                    var p = new Vec3(x, y, -alt);
                    AtmosphereSample s = f.Sample(p, time);
                    string at = $"{name} {afe} m at t={time}";
                    Assert.True(s.CalmWeight == 1.0, at);
                    Assert.True(s.MeanWind.X == 0 && s.MeanWind.Y == 0 && s.MeanWind.Z == 0, $"mean wind must be exactly zero: {at}");
                    Assert.True(s.Gust.X == 0 && s.Gust.Y == 0 && s.Gust.Z == 0, $"gust must be exactly zero: {at}");
                    Assert.True(s.SigmaHorizMs == 0 && s.SigmaVertMs == 0 && s.GustIncrementMs == 0, $"turbulence must be exactly zero: {at}");
                    Assert.True(s.ThermalFactor == 0 && s.ConvectiveMs == 0 && s.SlopeLiftMs == 0 && s.LeeMs == 0, $"no thermal, sink or slope air: {at}");
                    // What the wings actually read, thermals and glacier wind included.
                    Vec3 w = Atmosphere.WindAtPosition(p), m = Atmosphere.MeanWindAtPosition(p);
                    Assert.True(w.X == 0 && w.Y == 0 && w.Z == 0 && m.X == 0 && m.Y == 0 && m.Z == 0, $"Atmosphere wind must be exactly zero: {at} ({w.X}, {w.Y}, {w.Z})");
                    // Standard atmosphere, exactly.
                    Assert.Equal(Atmosphere.PressureAtAltitude(alt), s.PressurePa);
                    Assert.Equal(Atmosphere.SeaLevelTempK - Atmosphere.LapseRateKPerM * alt, s.TemperatureK);
                    Assert.Equal(Atmosphere.DensityAtAltitude(alt), s.DensityKgM3);
                    Assert.Equal(Atmosphere.DensityAtAltitude(alt), Atmosphere.DensityAt(p));
                    Assert.Equal(101325.0, s.QnhPa);
                    n++;
                }
            }
            _out.WriteLine($"{n} samples exactly calm ISA");
            return 0;
        });
    }

    [Fact]
    public void CalmZoneBlendsOutSmoothlyAndMonotonically()
    {
        // Flat ground at field elevation, the real config (so the real background air): walk out of the zone sideways.
        var f = new AtmosphereField(Cfg()) { Heating = (_, _) => 0.0 };
        AtmosphereField.CalmZone z = f.Calm!;
        f.Terrain = (_, _) => z.FieldElevationM;
        AtmosphereFieldConfig noCalm = Cfg(); noCalm.CalmZone.Enabled = false;
        var reference = new AtmosphereField(noCalm) { Heating = (_, _) => 0.0, Terrain = (_, _) => z.FieldElevationM };
        double alt = z.FieldElevationM + 100;
        double prevWeight = 1, prevSpeed = 0, prevSigma = 0, prevDev = 0, maxStepW = 0;
        for (double d = -50; d <= z.BlendWidthM + 50; d += 1.0)
        {
            var p = new Vec3(z.CentreX, z.CentreY + (z.RadiusM + d), -alt);   // east, toward the coast
            AtmosphereSample s = f.Sample(p, 0);
            Vec3 h = new(s.MeanWind.X, s.MeanWind.Y, 0);
            double speed = h.Length;
            Assert.True(s.CalmWeight <= prevWeight + 1e-12, $"calm weight must fall monotonically (d={d})");
            if (d < z.BlendWidthM) Assert.True(speed >= prevSpeed - 1e-9, $"wind must grow monotonically out of the zone (d={d}: {speed} < {prevSpeed})");
            if (d < z.BlendWidthM)   // (beyond the band the regional air varies on its own account)
            {
                Assert.True(s.SigmaHorizMs >= prevSigma - 1e-9, $"turbulence must grow monotonically (d={d})");
                Assert.True(Math.Abs(s.IsaDeviationK) >= prevDev - 1e-12, $"the air's deviation from ISA must grow monotonically (d={d})");
            }
            maxStepW = Math.Max(maxStepW, prevWeight - s.CalmWeight);
            prevWeight = s.CalmWeight; prevSpeed = speed; prevSigma = s.SigmaHorizMs; prevDev = Math.Abs(s.IsaDeviationK);
            if (d <= 0) Assert.Equal(1.0, s.CalmWeight);
            if (d >= z.BlendWidthM)
            {
                // Clear of the band: the regional air exactly as if there were no calm zone at all.
                AtmosphereSample r = reference.Sample(p, 0);
                Assert.Equal(0.0, s.CalmWeight);
                Assert.Equal(r.MeanWind.X, s.MeanWind.X); Assert.Equal(r.MeanWind.Y, s.MeanWind.Y); Assert.Equal(r.MeanWind.Z, s.MeanWind.Z);
                Assert.Equal(r.Gust.X, s.Gust.X); Assert.Equal(r.SigmaVertMs, s.SigmaVertMs); Assert.Equal(r.DensityKgM3, s.DensityKgM3);
            }
        }
        // Smootherstep across 600 m: the steepest 1 m step is 1.875/600 of the whole change — no step change anywhere.
        Assert.True(maxStepW < 1.9 / z.BlendWidthM + 1e-6, $"steepest change in calm weight per metre {maxStepW:E2}");
        Assert.True(prevSpeed > 3, $"full regional wind outside ({prevSpeed:F1} m/s)");

        // And upward through the ceiling over the runway.
        double prevV = 1;
        for (double up = -20; up <= z.CeilingBlendM + 20; up += 1)
        {
            double wv = f.CalmWeightAt(new Vec3(z.CentreX, z.CentreY, -(z.CeilingMslM + up)));
            Assert.True(wv <= prevV + 1e-12, "calm weight must fall monotonically through the ceiling");
            if (up <= 0) Assert.Equal(1.0, wv);
            if (up >= z.CeilingBlendM) Assert.Equal(0.0, wv);
            prevV = wv;
        }
    }

    [Fact]
    public void NoThermalStandsInTheCalmZone()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            Atmosphere.Field = f;
            AtmosphereField.CalmZone z = f.Calm!;
            double elev = z.FieldElevationM;
            // The door refuses a thermal anywhere in the zone or its blend band...
            Assert.False(Atmosphere.TryAddThermal(new Thermal(new Vec3(z.CentreX, z.CentreY, -elev), 300, 10, elev + 2000)));
            Assert.False(Atmosphere.TryAddThermal(new Thermal(new Vec3(z.CentreX + 1000, z.CentreY + z.RadiusM + 0.5 * z.BlendWidthM, -elev), 300, 10, elev + 2000)));
            Assert.Empty(Atmosphere.Thermals);
            // ...and the Valley's hand-placed thermals (SoaringScenery) are moved out to where the regional air begins.
            (double x, double y)[] authored = { (-1000, 1300), (-1100, -1250), (1900, -1500), (2000, 1500) };
            foreach (var (x0, y0) in authored)
            {
                WorldTerrain.Airport a = WorldTerrain.DefaultAirport;
                var (x, y) = f.NearestThermalSite(a.X + x0, a.Y + y0);
                Assert.True(f.AllowsThermalAt(x, y));
                Assert.True(Atmosphere.TryAddThermal(new Thermal(new Vec3(x, y, -elev), 360, 12, elev + 2000)));
            }
            // Nothing rises or sinks anywhere over the zone below its ceiling, at any time: no columns, no convection.
            for (double along = -z.HalfLengthM; along <= z.HalfLengthM; along += 250)
                for (double across = -z.RadiusM; across <= z.RadiusM; across += 200)
                    foreach (double afe in new[] { 20.0, 200, 450 })
                    {
                        var p = new Vec3(z.CentreX + along, z.CentreY + across, -(elev + afe));
                        Assert.Equal(0.0, Atmosphere.WindAtPosition(p).Z);
                        Assert.Equal(0.0, f.Sample(p, 60).ConvectiveMs);
                    }
            return 0;
        });
    }

    [Fact]
    public void ConfigIsMirroredToStreamingAssets()
    {
        string src = Path.Combine(Repo, "configs", "atmosphere", "regional-atmosphere.json");
        string dst = Path.Combine(Repo, "unity", "Assets", "StreamingAssets", "atmosphere", "regional-atmosphere.json");
        Assert.True(File.Exists(dst), "StreamingAssets/atmosphere/regional-atmosphere.json missing");
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dst));
        var c = Cfg();
        Assert.Equal("defaultStartAirport", c.CalmZone.Anchor);   // tied to the default start, never to coordinates
        Assert.Contains(c.Regions, r => r.Anchor == "airport:Bench" && r.Wind.Mode == "variable");
        Assert.Contains(c.Regions, r => r.Anchor == "airport:Mesa" && r.Wind.Mode == "gusty");
    }

    [Fact]
    public void FullRegionalConditionsApplyAwayFromHome()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            // Distinct air in distinct places: several condition types coexist on the one small map.
            AtmosphereSample S(double x, double y, double agl) => f.Sample(new Vec3(x, y, -(t.HeightAt(x, y) + agl)), 300);
            var desert = S(-5250, -6000, 300); var alaska = S(5700, -5950, 300); var coast = S(1000, 6500, 300);
            var bench = S(WorldTerrain.Airports[1].X, WorldTerrain.Airports[1].Y, 10);
            var mesa = S(WorldTerrain.Airports[2].X, WorldTerrain.Airports[2].Y, 10);
            Assert.Equal("high-desert", desert.RegionId); Assert.Equal("alaska-range", alaska.RegionId); Assert.Equal("coast-marine", coast.RegionId);
            Assert.Equal("airport2-variable", bench.RegionId); Assert.Equal("airport3-gusty", mesa.RegionId);
            Assert.True(desert.TemperatureK - alaska.TemperatureK > 15, "the desert must be far warmer than the ice");
            Assert.True(desert.DensityAltitudeM - (t.HeightAt(-5250, -6000) + 300) > 300, "a hot desert's density altitude stands well above its height");
            Assert.True(coast.QnhPa > desert.QnhPa + 300, "marine high, desert low");
            Assert.True(alaska.SigmaHorizMs > coast.SigmaHorizMs, "mountain air rougher than marine air");
            foreach (var s in new[] { desert, alaska, coast, bench, mesa }) Assert.Equal(0.0, s.CalmWeight);
            return 0;
        });
    }

    // =================================================================================================================
    // Airport 2: light and variable
    // =================================================================================================================

    [Fact]
    public void Airport2WindWandersAllTheWayRoundAtAboutFiveKnots()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            WorldTerrain.Airport a = WorldTerrain.Airports[1];
            var p = new Vec3(a.X, a.Y, -(t.HeightAt(a.X, a.Y) + 10));
            AtmosphereField.Region r = f.Regions.First(x => x.Id == "airport2-variable");
            double maxRateCap = r.Cfg.Wind.TurnRateMaxDegS;
            var bins = new bool[36];
            double sumSpeed = 0, maxRate = 0, prevDir = double.NaN; int n = 0;
            const double dt = 0.5;
            for (double time = 0; time <= 3600; time += dt)
            {
                Vec3 w = f.Sample(p, time).MeanWind;
                double speed = Math.Sqrt(w.X * w.X + w.Y * w.Y);
                double from = Math.Atan2(-w.Y, -w.X) * 180 / Math.PI;   // direction the wind blows FROM
                bins[(int)(((from % 360) + 360) % 360 / 10) % 36] = true;
                if (!double.IsNaN(prevDir))
                {
                    double d = from - prevDir; d -= 360 * Math.Round(d / 360);
                    maxRate = Math.Max(maxRate, Math.Abs(d) / dt);
                }
                prevDir = from; sumSpeed += speed; n++;
            }
            double meanKt = sumSpeed / n / Kt;
            _out.WriteLine($"airport 2: mean {meanKt:F2} kt, max turn {maxRate:F2}°/s, {bins.Count(b => b)}/36 sectors");
            Assert.True(bins.All(b => b), $"the direction must visit every 10° sector within the hour ({bins.Count(b => b)}/36)");
            Assert.InRange(meanKt, 4.25, 5.75);
            Assert.True(maxRate <= maxRateCap * 1.25 + 0.5, $"direction may not jump: max {maxRate:F1}°/s (cap {maxRateCap})");
            return 0;
        });
    }

    [Fact]
    public void WindProcessesArePureFunctionsOfTime()
    {
        // Ask in different orders and at different rates: the same answer at the same time, to the bit.
        var a = new VariableWind(2.6, 0.4, 2.5, 6, 40, 90, 2);
        var b = new VariableWind(2.6, 0.4, 2.5, 6, 40, 90, 2);
        Vec3 late = a.VectorAt(1234.56);
        for (double time = 0; time < 1300; time += 1.0 / 60) b.VectorAt(time);
        Assert.Equal(late.X, b.VectorAt(1234.56).X); Assert.Equal(late.Y, b.VectorAt(1234.56).Y);
        var g1 = new GustyWind(2.57, 7.7, 60, 18, 4, 10, 3);
        var g2 = new GustyWind(2.57, 7.7, 60, 18, 4, 10, 3);
        double gl = g1.GustAt(999.9);
        for (double time = 0; time < 1000; time += 0.02) g2.GustAt(time);
        Assert.Equal(gl, g2.GustAt(999.9));
    }

    // =================================================================================================================
    // Airport 3: 5 kt gusting 15
    // =================================================================================================================

    [Fact]
    public void Airport3IsFiveGustingFifteenFromASteadyDirection()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg()) { TurbulenceScale = 0 };   // the gust train alone (turbulence is tested below)
            WorldTerrain.Airport a = WorldTerrain.Airports[2];
            var p = new Vec3(a.X, a.Y, -(t.HeightAt(a.X, a.Y) + 10));
            AtmosphereField.Region r = f.Regions.First(x => x.Id == "airport3-gusty");
            double baseFrom = r.Cfg.Wind.FromDeg;
            double sum = 0, peak = 0, maxDev = 0, maxAccel = 0, prev = double.NaN; int n = 0, gusts = 0; bool inGust = false;
            const double dt = 0.25;
            for (double time = 0; time <= 3600; time += dt)
            {
                AtmosphereSample s = f.Sample(p, time);
                Vec3 w = s.Wind;
                double speed = Math.Sqrt(w.X * w.X + w.Y * w.Y);
                double from = Math.Atan2(-w.Y, -w.X) * 180 / Math.PI;
                double dev = from - baseFrom; dev -= 360 * Math.Round(dev / 360);
                maxDev = Math.Max(maxDev, Math.Abs(dev));
                if (!double.IsNaN(prev)) maxAccel = Math.Max(maxAccel, Math.Abs(speed - prev) / dt);
                bool g = speed > 9 * Kt; if (g && !inGust) gusts++; inGust = g;
                sum += speed; peak = Math.Max(peak, speed); prev = speed; n++;
            }
            double meanKt = sum / n / Kt, peakKt = peak / Kt;
            _out.WriteLine($"airport 3: mean {meanKt:F2} kt, peak {peakKt:F2} kt, {gusts} gusts/h, direction within ±{maxDev:F2}°, max dU/dt {maxAccel:F2} m/s²");
            Assert.InRange(meanKt, 4.4, 5.6);
            Assert.InRange(peakKt, 13.5, 15.5);
            Assert.True(gusts >= 60, $"gusts must keep coming ({gusts} an hour)");
            Assert.True(maxDev < 2.0, $"the direction must stay steady (±{maxDev:F2}°)");
            // A 1 − cos gust of 5 m/s over 4 s peaks at Aπ/T ≈ 4 m/s²; a step would read A/dt ≈ 20 m/s² here.
            Assert.True(maxAccel < 6.0, $"gusts must build and decay, not step ({maxAccel:F2} m/s²)");
            return 0;
        });
    }

    // =================================================================================================================
    // Bluff and ridge
    // =================================================================================================================

    // A 600 m escarpment, plain at 300 m to the east, plateau at 900 m to the west, its face centred on y = −1100.
    private static double Bluff(double x, double y) => 300 + 600 * 0.5 * (1 + Math.Tanh((-y - 1100) / 150.0));

    [Fact]
    public void WindStrengthensAndSlopeLiftGrowsTowardTheBluff()
    {
        var f = Simple(90, 15, Bluff, turbulence: 0);   // from the east, onto the bluff
        double prevW = -1, prevU = 0;
        var log = new System.Text.StringBuilder();
        for (double y = 1500; y >= -1150; y -= 25)
        {
            double h = Bluff(0, y);
            AtmosphereSample s = f.Sample(new Vec3(0, y, -(h + 100)), 0);
            double u = Math.Sqrt(s.MeanWind.X * s.MeanWind.X + s.MeanWind.Y * s.MeanWind.Y), w = -s.MeanWind.Z;
            log.AppendLine($"y {y,6} ground {h,5:F0}  U {u:F2}  w {w:F2}  speed-up {s.SpeedUp:F3}");
            Assert.True(w >= prevW - 1e-6, $"slope lift must grow toward the bluff (y={y}: {w:F3} < {prevW:F3})");
            Assert.True(u >= prevU - 1e-6, $"the wind must strengthen toward the bluff (y={y}: {u:F3} < {prevU:F3})");
            if (y <= -500) Assert.True(w > 0.2, $"lift in front of the bluff (y={y}: {w:F2})");
            prevW = w; prevU = u;
        }
        _out.WriteLine(log.ToString());
        // Over the crest: faster than the free stream at the same height above the ground (Jackson–Hunt speed-up).
        double free = f.Sample(new Vec3(0, 3000, -(300 + 100)), 0).MeanWind.Y;
        double crest = f.Sample(new Vec3(0, -1350, -(Bluff(0, -1350) + 100)), 0).MeanWind.Y;
        Assert.True(Math.Abs(crest) > 1.2 * Math.Abs(free), $"speed-up over the crest: {Math.Abs(crest):F2} vs free {Math.Abs(free):F2}");
    }

    [Fact]
    public void RealBluffHasLiftInFrontOfIt()
    {
        WithWorld(t =>
        {
            var c = Cfg(); c.Synoptic.WindFromDeg = 90; c.Synoptic.WindSpeedKt = 15;
            var f = new AtmosphereField(c);
            double W(double y) { double h = t.HeightAt(-5000, y); return -f.Sample(new Vec3(-5000, y, -(h + 100)), 0).MeanWind.Z; }
            double U(double y) { double h = t.HeightAt(-5000, y); var m = f.Sample(new Vec3(-5000, y, -(h + 100)), 0).MeanWind; return Math.Sqrt(m.X * m.X + m.Y * m.Y); }
            // South of the calm zone on the Valley's first wall (y ≈ −1,100 … −1,300, wind from the east).
            Assert.True(W(-700) < W(-800) && W(-800) < W(-900), $"lift builds toward the wall: {W(-700):F2} {W(-800):F2} {W(-900):F2}");
            Assert.True(W(-1300) > W(-900) && W(-900) > 1.5, $"strongest at the face and crest: {W(-900):F2} {W(-1300):F2}");
            Assert.True(U(-1300) > U(-700) + 2, $"the wind is faster over the rim: {U(-700):F2} → {U(-1300):F2}");
            return 0;
        });
    }

    [Fact]
    public void LeeHasSinkRotorAndWaves()
    {
        // The wind from the WEST, off the plateau and down the face: the plain is the lee.
        var stable = Simple(270, 25, Bluff, lapseCPerKm: 4.0, turbulence: 0);
        double minLee = 0, minAlong = double.MaxValue;
        for (double y = -1000; y <= 600; y += 20)
            for (double agl = 10; agl <= 600; agl += 10)
            {
                AtmosphereSample s = stable.Sample(new Vec3(0, y, -(Bluff(0, y) + agl)), 0);
                minLee = Math.Min(minLee, s.LeeMs);
                if (agl <= 30) minAlong = Math.Min(minAlong, s.MeanWind.Y);   // the wind blows +y here
            }
        double freeNearGround = Simple(270, 25, (_, _) => 300, turbulence: 0).Sample(new Vec3(0, 0, -(300 + 20)), 0).MeanWind.Y;
        _out.WriteLine($"lee: strongest sink {minLee:F2} m/s; near-ground along-wind min {minAlong:F2} vs free {freeNearGround:F2}");
        Assert.True(minLee < -2.0, $"lee downdraught ({minLee:F2} m/s)");
        Assert.True(minAlong < 0.25 * freeNearGround, $"the rotor slows or reverses the surface wind ({minAlong:F2} vs {freeNearGround:F2})");

        // Standing waves above crest height in stable air: alternating down and up downstream of the crest.
        double up = 0, down = 0;
        for (double y = -1000; y <= 5000; y += 25)
        {
            AtmosphereSample s = stable.Sample(new Vec3(0, y, -(900 + 300)), 0);
            up = Math.Max(up, s.WaveMs); down = Math.Min(down, s.WaveMs);
        }
        _out.WriteLine($"lee waves: up {up:F2}, down {down:F2} m/s");
        Assert.True(up > 0.5 && down < -0.5, $"lee waves in stable air (up {up:F2}, down {down:F2})");
        var unstable = Simple(270, 25, Bluff, lapseCPerKm: 9.9, turbulence: 0);
        for (double y = -1000; y <= 5000; y += 100) Assert.Equal(0.0, unstable.Sample(new Vec3(0, y, -1200), 0).WaveMs);

        // A 5 kt breeze over the same wall makes no rotor: rotors need a real wind.
        var light = Simple(270, 5, Bluff, turbulence: 0);
        for (double y = -800; y <= 600; y += 50)
        {
            AtmosphereSample s = light.Sample(new Vec3(0, y, -(Bluff(0, y) + 20)), 0);
            Assert.True(s.MeanWind.Y > 0, $"no reversed flow at 5 kt (y={y}: {s.MeanWind.Y:F2})");
        }
    }

    // =================================================================================================================
    // Turbulence
    // =================================================================================================================

    [Fact]
    public void TurbulenceGrowsTowardTheTerrainAndDecaysWithHeight()
    {
        var f = Simple(270, 25, Bluff);
        // Over the lee of the bluff, rising: rougher near the ground, dying away above the crest.
        double[] heights = { 20, 100, 300, 700, 1200, 2000 };
        double prev = double.MaxValue;
        foreach (double agl in heights)
        {
            double s = f.Sample(new Vec3(0, -600, -(Bluff(0, -600) + agl)), 0).SigmaVertMs;
            _out.WriteLine($"lee σw at {agl} m AGL: {s:F2}");
            Assert.True(s < prev, $"σ must decay with height ({agl} m: {s:F2})");
            prev = s;
        }
        // The terrain adds its own: the lee of the bluff is rougher than open flat ground in the same wind.
        var flat = Simple(270, 25, (_, _) => 300);
        double lee = f.Sample(new Vec3(0, -600, -(Bluff(0, -600) + 50)), 0).SigmaHorizMs;
        double open = flat.Sample(new Vec3(0, -600, -(300 + 50)), 0).SigmaHorizMs;
        Assert.True(lee > 1.3 * open, $"orographic turbulence: lee {lee:F2} vs open {open:F2}");
    }

    [Theory]
    [InlineData(TurbulenceSpectrum.VonKarman)]
    [InlineData(TurbulenceSpectrum.Dryden)]
    public void SpectralTurbulenceStatisticsAreSane(TurbulenceSpectrum spectrum)
    {
        const double L = 150;
        double rmsSum = 0, meanSum = 0, lintSum = 0, d1Sum = 0, d2Sum = 0; int lines = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            var t = new Turbulence(spectrum, 48, seed, 15, 762);
            const double dx = 7.5; const int n = 16000;   // 120 km of line, 800 L
            var u = new double[n]; var v = new double[n]; var w = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vec3 g = t.UnitGustAt(new Vec3(i * dx, 37.0 * seed, -500 - 13.0 * seed), 0, L, L, Vec3.Zero);
                u[i] = g.X; v[i] = g.Y; w[i] = g.Z;
            }
            foreach (double[] c in new[] { u, v, w }) { meanSum += c.Average(); rmsSum += Math.Sqrt(c.Select(x => x * x).Average()); lines++; }
            // Longitudinal integral scale: ∫ R_uu(r) dr / R_uu(0), to the first zero crossing.
            double var0 = u.Select(x => x * x).Average(), integral = 0;
            for (int lag = 0; lag < n / 4; lag++)
            {
                double r = 0; for (int i = 0; i + lag < n; i++) r += u[i] * u[i + lag]; r /= n - lag;
                if (r <= 0) break;
                integral += r / var0 * dx;
            }
            lintSum += integral;
            // Second-order structure function at two small separations: D(r) ∝ r^(2/3) (von Kármán) or ∝ r (Dryden).
            double D(int lag) { double s = 0; for (int i = 0; i + lag < n; i++) s += (u[i + lag] - u[i]) * (u[i + lag] - u[i]); return s / (n - lag); }
            d1Sum += D(2); d2Sum += D(4);
        }
        double rms = rmsSum / lines, mean = meanSum / lines, lInt = lintSum / 6, ratio = d2Sum / d1Sum;
        _out.WriteLine($"{spectrum}: rms {rms:F3}, mean {mean:F3}, integral scale {lInt:F0} m (L = {L}), D(2r)/D(r) = {ratio:F2}");
        Assert.InRange(rms, 0.85, 1.15);
        Assert.InRange(Math.Abs(mean), 0, 0.12);
        Assert.InRange(lInt, 0.6 * L, 1.6 * L);
        // Theory, with this field's ~1.6 m smallest eddy: 1.62 (von Kármán, the r^(2/3) inertial range: 2^(2/3) = 1.59 with
        // no cut-off) and 1.93 (Dryden, D ∝ r at small r: 2).
        if (spectrum == TurbulenceSpectrum.VonKarman) Assert.InRange(ratio, 1.45, 1.78);
        else Assert.InRange(ratio, 1.78, 2.10);
    }

    [Fact]
    public void MilHandbookScalesAreTheHandbooks()
    {
        // At 1,000 ft the low-altitude model is isotropic: σu = σw and Lu = Lw = 1,000 ft.
        var s1000 = TurbulenceScales.LowAltitude(1000 / TurbulenceScales.FtPerM, 15, TurbulenceSpectrum.Dryden);
        Assert.Equal(s1000.SigmaU, s1000.SigmaW, 6);
        Assert.Equal(1000 / TurbulenceScales.FtPerM, s1000.LengthU, 3);
        Assert.Equal(1.5, s1000.SigmaW, 6);   // σw = 0.1 W20
        // At 100 ft: Lw = h, σu/σw = 1/(0.177 + 0.0823)^0.4, Lu = h/(0.177 + 0.0823)^1.2.
        var s100 = TurbulenceScales.LowAltitude(100 / TurbulenceScales.FtPerM, 15, TurbulenceSpectrum.Dryden);
        Assert.Equal(100 / TurbulenceScales.FtPerM, s100.LengthW, 6);
        Assert.Equal(1 / Math.Pow(0.2593, 0.4), s100.SigmaU / s100.SigmaW, 4);
        Assert.Equal(100 / Math.Pow(0.2593, 1.2) / TurbulenceScales.FtPerM, s100.LengthU, 3);
        // Aloft, the high-altitude scales: 1,750 ft Dryden, 2,500 ft von Kármán.
        Assert.Equal(1750 / TurbulenceScales.FtPerM, TurbulenceScales.LowAltitude(3000, 15, TurbulenceSpectrum.Dryden).LengthU, 3);
        Assert.Equal(2500 / TurbulenceScales.FtPerM, TurbulenceScales.LowAltitude(3000, 15, TurbulenceSpectrum.VonKarman).LengthW, 3);
    }

    // =================================================================================================================
    // The flight model
    // =================================================================================================================

    private static AircraftConfig Glider() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));

    private static Aircraft Trimmed(AircraftConfig c, double v, Vec3 at)
    {
        var t = TrimSolver.SolveGliderTrim(c, v, -at.Z);
        double half = t.ThetaRad / 2;
        return new Aircraft(c, new RigidBodyState(at, new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(v * Math.Cos(t.AlphaRad), 0, v * Math.Sin(t.AlphaRad)), Vec3.Zero), new ControlDeflections(0, t.ElevatorRad, 0, 0));
    }

    [Fact]
    public void ALiftedWingRollsTheAircraft()
    {
        // A narrow thermal under the RIGHT wing: sampled per panel, the right wing climbs — the aircraft rolls left.
        double Roll(bool regional)
        {
            try
            {
                Atmosphere.SimTimeSec = 0;
                Atmosphere.Thermals.Clear();
                Atmosphere.Thermals.Add(new Thermal(new Vec3(0, 9, 0), 18, 5, 3000));
                Atmosphere.Field = regional ? Simple(0, 0, turbulence: 0) : null;
                var c = Glider();
                var ac = Trimmed(c, 24, new Vec3(-12, 0, -600));
                var sim = new SimLoop(ac);
                var hold = new ControlDeflections(0, ac.CurrentDeflections.ElevatorRad, 0, 0);
                for (int i = 0; i < 20; i++) sim.RunFor(0.02, hold);
                return ac.State.Rates.X;
            }
            finally { Atmosphere.Field = null; Atmosphere.Thermals.Clear(); Atmosphere.SimTimeSec = 0; }
        }
        double pRegional = Roll(true), pUniform = Roll(false);
        _out.WriteLine($"roll rate after 0.4 s: per-panel {pRegional * 57.3:F2}°/s, CG-only {pUniform * 57.3:F2}°/s");
        Assert.True(pRegional < -0.03, $"the lifted right wing must roll the aircraft left ({pRegional * 57.3:F2}°/s)");
        Assert.True(pRegional < pUniform - 0.02, "per-panel sampling must feel what CG-only sampling cannot");
    }

    private static RigidBodyState FlyRough(double fixedDt, double chunk, double seconds)
    {
        try
        {
            Atmosphere.SimTimeSec = 0;
            Atmosphere.Field = Simple(60, 20, turbulence: 1.5);
            var c = Glider();
            var ac = Trimmed(c, 26, new Vec3(0, 0, -150));
            var sim = new SimLoop(ac, fixedDt);
            var hold = new ControlDeflections(0, ac.CurrentDeflections.ElevatorRad, 0, 0);
            int chunks = (int)Math.Round(seconds / chunk);
            for (int i = 0; i < chunks; i++) sim.RunFor(chunk, hold);
            return ac.State;
        }
        finally { Atmosphere.Field = null; Atmosphere.SimTimeSec = 0; }
    }

    [Fact]
    public void RegionalFlightIsDeterministicAndFrameRateIndependent()
    {
        // The same flight twice: identical to the bit (no hidden state, no wall clock, no unseeded randomness).
        RigidBodyState a = FlyRough(1.0 / 200, 0.01, 6), b = FlyRough(1.0 / 200, 0.01, 6);
        Assert.Equal(a.Position.X, b.Position.X); Assert.Equal(a.Position.Y, b.Position.Y); Assert.Equal(a.Position.Z, b.Position.Z);
        Assert.Equal(a.Attitude.W, b.Attitude.W);
        // Rendered at 100 fps or 20 fps, the fixed 200 Hz physics reads the same air at the same instants: identical.
        RigidBodyState slowFrames = FlyRough(1.0 / 200, 0.05, 6);
        Assert.Equal(a.Position.X, slowFrames.Position.X); Assert.Equal(a.Position.Z, slowFrames.Position.Z);
        // And the physics step itself: 200 Hz against 400 Hz through the same turbulence converges.
        RigidBodyState h = FlyRough(1.0 / 400, 0.01, 6);
        double dp = (a.Position - h.Position).Length;
        double dq = Math.Abs(a.Attitude.W * h.Attitude.W + a.Attitude.X * h.Attitude.X + a.Attitude.Y * h.Attitude.Y + a.Attitude.Z * h.Attitude.Z);
        double dAngle = 2 * Math.Acos(Math.Min(1, dq)) * 180 / Math.PI;
        _out.WriteLine($"200 Hz vs 400 Hz physics after 6 s in turbulence: {dp:F3} m, {dAngle:F3}°");
        Assert.True(dp < 1.0 && dAngle < 2.0, $"the step size must not change the flight ({dp:F2} m, {dAngle:F2}°)");
    }

    [Fact]
    public void SamplingIsCheap()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            // A 50 m/s flight along the first wall and out over the Bench, sampled at 200 Hz from a cold start: the
            // terrain and heating caches fill as it goes, as they would in the game.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int n = 40000;
            for (int i = 0; i < n; i++)
            {
                double s = i * 0.25;
                f.Sample(new Vec3(-6000 + s, -800 - 0.3 * s, -(900 + 0.02 * s)), i * 0.005);
            }
            double us = sw.Elapsed.TotalMilliseconds * 1000 / n;
            _out.WriteLine($"{us:F1} µs per sample including cache fills (×4 per step: CG, two tips, tail)");
            Assert.True(us < 60, $"a sample costs {us:F1} µs");
            return 0;
        });
    }
}

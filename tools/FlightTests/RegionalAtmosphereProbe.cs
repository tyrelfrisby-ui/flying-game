using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

[Collection("WorldTerrainActive")]
public class RegionalAtmosphereProbe
{
    private readonly ITestOutputHelper _o;
    public RegionalAtmosphereProbe(ITestOutputHelper o) => _o = o;

    [Fact]
    public void SpectrumProbe()
    {
        if (Environment.GetEnvironmentVariable("REGATM_PROBE") != "2") return;
        foreach (int modes in new[] { 48, 96, 200 })
        foreach (double L in new[] { 150.0, 400.0 })
        {
            double d1 = 0, d2 = 0, li = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var t = new Turbulence(TurbulenceSpectrum.VonKarman, modes, seed, 15, 762);
                const double dx = 7.5; const int n = 16000;
                var u = new double[n];
                for (int i = 0; i < n; i++) u[i] = t.UnitGustAt(new Vec3(i * dx, 37.0 * seed, -500), 0, L, L, Vec3.Zero).X;
                double D(int lag) { double s = 0; for (int i = 0; i + lag < n; i++) s += (u[i + lag] - u[i]) * (u[i + lag] - u[i]); return s / (n - lag); }
                d1 += D(2); d2 += D(4);
            }
            _o.WriteLine($"modes {modes} L {L}: D(30)/D(15) = {d2 / d1:F3}");
        }
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("REGATM_PROBE") != "1") return;
        var cfg = AtmosphereFieldConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "atmosphere", "regional-atmosphere.json"));
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        try
        {
            var f = new AtmosphereField(cfg);
            void P(string name, double x, double y, double agl)
            {
                double g = t.HeightAt(x, y);
                var s = f.Sample(new Vec3(x, y, -(g + agl)), 100);
                _o.WriteLine($"{name,-22} g={g,6:F0} calm={s.CalmWeight:F2} reg={s.RegionId,-18} wind=({s.MeanWind.X,5:F1},{s.MeanWind.Y,5:F1},{s.MeanWind.Z,5:F2}) gust={s.Gust.Length,4:F1} sig={s.SigmaHorizMs:F2}/{s.SigmaVertMs:F2} L={s.LengthHorizM:F0}/{s.LengthVertM:F0} T={s.TemperatureK - 273.15,5:F1}C DA={s.DensityAltitudeM,6:F0} su={s.SpeedUp:F2} sl={s.SlopeLiftMs:F2} lee={s.LeeMs:F2} wave={s.WaveMs:F2} conv={s.ConvectiveMs:F2} heat={s.HeatingIndex:F2}");
            }
            foreach (var a in WorldTerrain.Airports) { P(a.Name + " 10m", a.X, a.Y, 10); P(a.Name + " 300m", a.X, a.Y, 300); }
            for (double y = 0; y >= -1600; y -= 100) P($"bluff x=-5000 y={y}", -5000, y, 100);
            for (double y = -2000; y <= 0; y += 200) P($"lee? x=-5000 y={y}", -5000, y, 50);
            P("desert", -5250, -6000, 300); P("alaska", 5700, -5950, 300); P("alpine", 5500, -11300, 300); P("frank", -2200, -12700, 300); P("coast", 1000, 6500, 300);
            var sw = System.Diagnostics.Stopwatch.StartNew(); int n = 0;
            for (double x = -6000; x < 8000; x += 97) for (double y = -14000; y < 9000; y += 391) { f.Sample(new Vec3(x, y, -1200), n * 0.01); n++; }
            _o.WriteLine($"{n} samples, {sw.Elapsed.TotalMilliseconds * 1000 / n:F1} us each (cold heat cache)");
            sw.Restart(); n = 0;
            for (double x = -6000; x < 8000; x += 97) for (double y = -14000; y < 9000; y += 391) { f.Sample(new Vec3(x, y, -1200), n * 0.01); n++; }
            _o.WriteLine($"{n} samples, {sw.Elapsed.TotalMilliseconds * 1000 / n:F1} us each (warm)");
            sw.Restart(); for (int i = 0; i < 20000; i++) t.HeightAt(i * 0.37, -i * 0.5); _o.WriteLine($"HeightAt {sw.Elapsed.TotalMilliseconds * 1000 / 20000:F2} us");
        }
        finally { WorldTerrain.Active = null; }
    }
}

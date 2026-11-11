using System;
using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Weather;
using NUnit.Framework;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// The island weather (docs/weather-system.md; Ty 2026-11-05 08:41 and 08:42): where fog lies and when it goes,
    /// the loose real-weather bias, and the airport weather stations. Literals copied by hand from the first run of the
    /// model (2026-11-05), so moving a number is a deliberate edit here too. Run: Unity -batchmode -runTests
    /// -testPlatform EditMode.
    /// </summary>
    public sealed class IslandWeatherTests
    {
        private static readonly DateTime Nov5 = new DateTime(2026, 11, 5);
        /// <summary>Seed 1103 on 5 November: a calm (0.67 m/s), mostly clear (0.30), humid (1.08 K at dawn) morning.</summary>
        private const int CalmClearHumid = 1103;

        // A 400 m bowl and a 400 m hill, both 2 km wide, on dry land: the same air over both.
        private static double Bowl(double n, double e) => 400 * (1 - Math.Exp(-(n * n + e * e) / (2 * 2000.0 * 2000)));
        private static double Hill(double n, double e) => 400 * Math.Exp(-(n * n + e * e) / (2 * 2000.0 * 2000));
        private static IslandWeather Over(Func<double, double, double> ground, int seed = CalmClearHumid) =>
            new IslandWeather(new WeatherSeed(seed, Nov5)) { Ground = ground, Water = (_, _) => null };

        [OneTimeSetUp] public void Island() { WorldTerrain.Active = new WorldTerrain(); CloudSites.Reset(); WeatherStations.Reset(); }
        [OneTimeTearDown] public void Clean() { WorldTerrain.Active = null; CloudSites.Reset(); WeatherStations.Reset(); }

        // ---- fog: valley against ridge ----------------------------------------------------------------------------
        [Test]
        public void ConditionsOfTheTestMorningAreCalmClearAndHumid()
        {
            IslandWeather.AirMass m = Over(Bowl).AirMassAt(0);
            Assert.AreEqual(0.671, m.WindMs, 0.001);
            Assert.AreEqual(0.301, m.Cover, 0.001);
            Assert.AreEqual(1.075, m.DawnSpreadK, 0.001);
        }

        [Test]
        public void FogPoolsInTheValleyAndTheRidgeStaysClear()
        {
            Assert.AreEqual(184.1, FogModel.ValleyDepth(Bowl, 0, 0), 0.1);
            Assert.AreEqual(-184.1, FogModel.ValleyDepth(Hill, 0, 0), 0.1);
            WeatherPoint valley = Over(Bowl).SampleSurface(0, 0, 0), ridge = Over(Hill).SampleSurface(0, 0, 0);
            // 07:00, before sunrise: the bowl's cold pool has closed the spread; the hill's air drained off it.
            Assert.AreEqual(1.0, valley.FogDensity, 1e-9);
            Assert.AreEqual(0.0, valley.SpreadK, 1e-9);
            Assert.AreEqual(0.0, ridge.FogDensity, 1e-9);
            Assert.AreEqual(4.27, ridge.SpreadK, 0.01);
            Assert.Less(valley.VisibilityM, 200);
            Assert.Greater(ridge.VisibilityM, 15000);
        }

        [Test]
        public void OnTheIslandTheCanyonStripsAndValleyPoolsFogAndTheHighCirquesDoNot()
        {
            var w = new IslandWeather(new WeatherSeed(CalmClearHumid, Nov5));
            double Fog(string id) { CloudSites.FogSite f = CloudSites.Fog.First(s => s.Id == id); return w.FogAt(f.Z, f.X, 0).density; }
            Assert.AreEqual(1.0, Fog("Thomas Creek"), 1e-6);
            Assert.AreEqual(1.0, Fog("Valley 1"), 1e-6);
            Assert.AreEqual(0.0, Fog("AP1"), 1e-6);              // a 1500 m shelf strip standing 475 m above its surroundings
            Assert.AreEqual(0.0, Fog("Bighorn Crown"), 0.01);   // 2390 m cirque lake
        }

        // ---- fog: the dewpoint spread --------------------------------------------------------------------------------
        [Test]
        public void RadiationFogNeedsTheSpreadUnderTwoAndAHalfKelvin()
        {
            // Calm, clear, the night's cooling complete.
            Assert.AreEqual(1.0, FogModel.RadiationDensity(0.5, 0.5, 0, 1), 1e-9);
            Assert.AreEqual(0.5, FogModel.RadiationDensity(1.5, 0.5, 0, 1), 1e-9);
            Assert.AreEqual(0.0072, FogModel.RadiationDensity(2.4, 0.5, 0, 1), 1e-4);
            Assert.AreEqual(0.0, FogModel.RadiationDensity(2.5, 0.5, 0, 1), 1e-12);
            Assert.AreEqual(0.0, FogModel.RadiationDensity(2.6, 0.5, 0, 1), 1e-12);
            // Cloud sends the heat back: no radiation fog under a broken sky, at any spread.
            Assert.AreEqual(0.0, FogModel.RadiationDensity(0.5, 0.5, 0.9, 1), 1e-12);
            // A deep valley pools 6 K, a 110 m one half of it, on the same night.
            Assert.AreEqual(6.0, FogModel.ColdPoolK(300, 0.5, 0, 1), 1e-9);
            Assert.AreEqual(3.0, FogModel.ColdPoolK(110, 0.5, 0, 1), 1e-9);
        }

        // ---- fog: wind and sun ---------------------------------------------------------------------------------------
        [Test]
        public void WindMixesTheFogOutAndTheSunBurnsItOff()
        {
            IslandWeather w = Over(Bowl);
            // Sunrise 07:09 local solar at 45 N on 5 November; a weak sun (28.5 deg at noon) burns off slowly.
            var (rise, set) = w.SunTimes(0);
            Assert.AreEqual(7.152, rise, 0.001);
            Assert.AreEqual(16.848, set, 0.001);
            Assert.AreEqual(4.543, FogModel.BurnOffHours(w.NoonElevationDeg(0)), 0.001);
            Assert.AreEqual(1.0, w.FogAt(0, 0, 0).density, 1e-9);            // 07:00
            Assert.AreEqual(0.848, w.FogAt(0, 0, 2 * 3600).density, 0.001);  // 09:00, thinning
            Assert.AreEqual(0.0, w.FogAt(0, 0, 3 * 3600).density, 1e-9);     // 10:00, gone
            // The same morning with a 6 m/s wind: no fog at all.
            IslandWeather windy = Over(Bowl); windy.WindOverride = (270, 6);
            Assert.AreEqual(0.0, windy.FogAt(0, 0, 0).density, 1e-12);
            // Afternoon: no night's cooling, whatever the spread.
            Assert.AreEqual(0.0, FogModel.NightCooling(13, rise, set), 1e-12);
            Assert.AreEqual(0.0, FogModel.RadiationDensity(0.0, 0.5, 0, FogModel.NightCooling(13, rise, set)), 1e-12);
        }

        [Test]
        public void AdvectionFogNeedsMoistAirOverColderWaterAndABreeze()
        {
            Assert.AreEqual(1.0, FogModel.AdvectionDensity(286, 285, 5), 1e-9);
            Assert.AreEqual(0.0, FogModel.AdvectionDensity(283, 285, 5), 1e-12);   // dewpoint under the water: none
            Assert.AreEqual(0.0, FogModel.AdvectionDensity(286, 285, 0.5), 1e-12); // nothing brings it in
            Assert.AreEqual(0.0, FogModel.AdvectionDensity(286, 285, 15), 1e-12);  // lifted into stratus
        }

        // ---- determinism ---------------------------------------------------------------------------------------------
        [Test]
        public void TheSameSeedGivesTheSameWeatherAndAnotherSeedDoesNot()
        {
            string Print(int seed)
            {
                var w = new IslandWeather(new WeatherSeed(seed, Nov5));
                var sb = new System.Text.StringBuilder();
                foreach (CloudSites.FogSite f in CloudSites.Fog)
                    foreach (double t in new[] { 0.0, 5400, 14400 })
                    {
                        WeatherPoint p = w.SampleSurface(f.Z, f.X, t);
                        sb.Append($"{p.TemperatureK:R},{p.DewpointK:R},{p.QnhPa:R},{p.MeanWind.X:R},{p.FogDensity:R},{p.VisibilityM:R};");
                    }
                return sb.ToString();
            }
            Assert.AreEqual(Print(CalmClearHumid), Print(CalmClearHumid));
            Assert.AreNotEqual(Print(CalmClearHumid), Print(CalmClearHumid + 1));
            // The seed survives the wire unchanged.
            var seed = new WeatherSeed(77, Nov5, 6.5);
            Assert.AreEqual("77;20261105;6.5", seed.Encode());
            Assert.AreEqual(seed.Encode(), WeatherSeed.Decode(seed.Encode()).Encode());
            // Stable draws, never System.Random.
            Assert.AreEqual(IslandWeather.Draw(1, 2, 3), IslandWeather.Draw(1, 2, 3));
        }

        [Test]
        public void FogSitesAreEveryGrassStripEveryLakeAndTheValleyPools()
        {
            var ids = CloudSites.Fog.Select(f => f.Id).ToList();
            Assert.AreEqual(30, ids.Count);
            CollectionAssert.IsSubsetOf(new[] { "Thomas Creek", "AP4", "Payette Lake", "Lake Hood", "Bighorn Crown" }, ids);
            CloudSites.FogSite v1 = CloudSites.Fog.First(f => f.Id == "Valley 1");
            Assert.AreEqual(-5000, v1.X, 1e-6); Assert.AreEqual(-1000, v1.Z, 1e-6); Assert.AreEqual(30.5, v1.SurfaceM, 0.1);
            Assert.AreEqual(7, ids.Count(i => i.StartsWith("Valley ")));
        }

        // ---- real-weather bias ---------------------------------------------------------------------------------------
        [Test]
        public void TheRealWeatherBiasIsLooseClampedAndNeverIncoherent()
        {
            // A wild feed: a 950 hPa, 45 C, 40 m/s hour with the dewpoint ABOVE the temperature, then a 1050 hPa, -30 C hour.
            var bias = new RealWeatherBias(new[] {
                new RealWeatherObservation { Hour = 0, QnhHpa = 950, TempC = 45, DewpointC = 50, WindMs = 40, WindFromDeg = 90, Cover = 1 },
                new RealWeatherObservation { Hour = 12, QnhHpa = 1050, TempC = -30, DewpointC = -20, WindMs = 0, WindFromDeg = 270, Cover = 0 } });
            // Rate-limited hour to hour before use (2 hPa/h, 3 K/h, 3 m/s/h), dewpoint never above the temperature.
            RealWeatherObservation late = bias.Observations[1];
            Assert.AreEqual(974, late.QnhHpa.Value, 1e-9);
            Assert.AreEqual(9, late.TempC.Value, 1e-9);
            Assert.AreEqual(4, late.WindMs.Value, 1e-9);
            Assert.AreEqual(45, bias.Observations[0].DewpointC.Value, 1e-9);
            IslandWeather.AirMass own = Over(Bowl).KeyDraw(0), pulled = bias.Apply(own, 6);
            Assert.AreEqual(own.QnhHpa - RealWeatherBias.MaxQnhHpa, pulled.QnhHpa, 1e-9);
            Assert.AreEqual(own.MeanSeaLevelTempK + RealWeatherBias.MaxTempK, pulled.MeanSeaLevelTempK, 1e-9);
            Assert.AreEqual(own.WindMs + RealWeatherBias.MaxWindMs, pulled.WindMs, 1e-9);
            Assert.GreaterOrEqual(pulled.DawnSpreadK, 0);
            Assert.LessOrEqual(pulled.WindMs, RealWeatherBias.MaxBiasWindMs);
            // Every sample of a biased island is still self-consistent.
            var w = new IslandWeather(new WeatherSeed(CalmClearHumid, Nov5, 7, bias)) { Ground = Bowl, Water = (_, _) => null };
            for (double t = 0; t <= 24 * 3600; t += 1800)
            {
                WeatherPoint p = w.SampleSurface(0, 0, t);
                Assert.LessOrEqual(p.DewpointK, p.TemperatureK);
                Assert.That(p.QnhPa, Is.InRange(97500, 104500));
            }
            // The wire form carries the bias exactly.
            var seed = new WeatherSeed(77, Nov5, 6.5, bias);
            Assert.AreEqual("77;20261105;6.5;0,950,90,40,45,45,1|12,974,270,4,9,9,0", seed.Encode());
        }

        [Test]
        public void OfflineTheIslandStandsOnItsOwnClimatology()
        {
            RealWeatherBias bias = RealWeatherBias.From(new OfflineWeatherProvider(), new RealWeatherRequest(45, -116, Nov5));
            Assert.IsNull(bias);
            var a = new IslandWeather(new WeatherSeed(CalmClearHumid, Nov5, 7, bias));
            var b = new IslandWeather(new WeatherSeed(CalmClearHumid, Nov5));
            Assert.AreEqual(b.AirMassAt(3600).QnhHpa, a.AirMassAt(3600).QnhHpa);
            Assert.IsNull(RealWeatherBias.From(new ThrowingProvider(), new RealWeatherRequest(0, 0, Nov5)));
        }

        private sealed class ThrowingProvider : IRealWeatherProvider
        {
            public bool TryGet(RealWeatherRequest request, out IReadOnlyList<RealWeatherObservation> hourly) => throw new InvalidOperationException("no network");
        }

        // ---- airport weather stations --------------------------------------------------------------------------------
        [Test]
        public void EveryAirportHasAStationOnItsOwnFrequency()
        {
            IReadOnlyList<WeatherStation> all = WeatherStations.All;
            Assert.AreEqual(IslandTerrain.World["airports"].Items.Length, all.Count);
            Assert.AreEqual(16, all.Count);
            Assert.AreEqual(all.Count, all.Select(s => Math.Round(s.FreqMhz * 1000)).Distinct().Count(), "two stations share a frequency");
            foreach (WeatherStation s in all)
            {
                int khz = (int)Math.Round(s.FreqMhz * 1000);
                Assert.That(khz, Is.InRange(WeatherStations.FirstChannelKhz, WeatherStations.LastChannelKhz), s.Id);
                Assert.AreEqual(0, khz % 25, s.Id);
                Assert.IsFalse(WeatherStations.Reserved(khz), s.Id + " on a reserved channel");
                Assert.AreSame(s, WeatherStations.ByFrequency(s.FreqMhz));
            }
            // Nothing transmits on guard or on the default CTAF.
            Assert.IsNull(WeatherStations.ByFrequency(121.5));
            Assert.IsNull(WeatherStations.ByFrequency(122.8));
            // The assignment depends only on the set of ids.
            Assert.AreEqual(123.125, WeatherStations.ById("KMYL").FreqMhz, 1e-9);
            Assert.AreEqual(120.900, WeatherStations.ById("PHDH").FreqMhz, 1e-9);
            var ids = new[] { "B", "A", "C", "KMYL" };
            CollectionAssert.AreEquivalent(WeatherStations.AssignChannels(ids), WeatherStations.AssignChannels(ids.Reverse()));
            Assert.AreEqual(123125, WeatherStations.AssignChannels(ids)["KMYL"]);
        }

        [Test]
        public void AReportSaysWhatTheWeatherIsAtTheStation()
        {
            foreach (int seed in new[] { CalmClearHumid, 1105, 1106, 1109 })
            {
                var w = new IslandWeather(new WeatherSeed(seed, Nov5));
                foreach (WeatherStation s in WeatherStations.All)
                    foreach (double t in new[] { 0.0, 3 * 3600.0, 8 * 3600.0 })
                    {
                        WeatherPoint p = w.SampleSurface(s.North, s.East, t);
                        MetarReport r = WeatherStations.Observe(s, w, t);
                        string at = $"{s.Id} seed {seed} +{t / 3600}h: {r.Raw}";
                        int kt = (int)Math.Round(p.WindSpeedMs * WeatherStations.KtPerMs, MidpointRounding.AwayFromZero);
                        if (kt < WeatherStations.CalmKt) Assert.AreEqual(0, r.WindKt, at);
                        else
                        {
                            Assert.AreEqual(kt, r.WindKt, at);
                            double d = Math.Abs(((r.WindDirDeg - p.WindFromDeg) % 360 + 540) % 360 - 180);
                            Assert.LessOrEqual(d, 5.0 + 1e-9, at);
                        }
                        Assert.AreEqual(Math.Round(p.TemperatureK - 273.15, MidpointRounding.AwayFromZero), r.TempC, at);
                        Assert.AreEqual(Math.Round(p.DewpointK - 273.15, MidpointRounding.AwayFromZero), r.DewpointC, at);
                        Assert.LessOrEqual(r.DewpointC, r.TempC, at);
                        double inHg = p.QnhPa / 100 * WeatherStations.InHgPerHpa;
                        Assert.That(r.AltimeterHundredths / 100.0, Is.InRange(inHg - 0.01, inHg + 1e-9), at);
                        Assert.LessOrEqual(r.VisibilitySm * WeatherStations.MPerSm, p.VisibilityM + 1e-6, at);
                        if (p.VisibilityM < 0.625 * WeatherStations.MPerSm && p.FogDensity > 0) CollectionAssert.Contains(r.Weather, "FG", at);
                        Assert.AreEqual(p.PrecipMmH, r.PrecipMmH, at);
                        // The time group is the island's local time made UTC (UTC-8 by default).
                        Assert.AreEqual(w.UtcAt(t).Hour, r.TimeUtc.Hour, at);
                    }
            }
        }

        [Test]
        public void ReportsAreDeterministic()
        {
            string All(int seed, double t) => string.Join("\n", WeatherStations.All.Select(s => WeatherStations.Observe(s, new IslandWeather(new WeatherSeed(seed, Nov5)), t).Raw));
            Assert.AreEqual(All(CalmClearHumid, 1800), All(CalmClearHumid, 1800));
            Assert.AreNotEqual(All(CalmClearHumid, 1800), All(1106, 1800));
            // One literal, so a change to the model is a deliberate edit: KMYL at 07:00 on the default seed.
            MetarReport kmyl = WeatherStations.Observe(WeatherStations.ById("KMYL"), new IslandWeather(new WeatherSeed(WeatherSeed.DefaultSeed, Nov5)), 0);
            Assert.AreEqual("METAR KMYL 051500Z AUTO 18007KT 10SM FEW021 00/M05 A2999 RMK AO2", kmyl.Raw);
        }

        [Test]
        public void MetarGroupsAreFormattedTheStandardWay()
        {
            var r = new MetarReport
            {
                Station = new WeatherStation("KXYZ", "Test Field", 0, 0, 100, StationKind.Asos, 118.325),
                TimeUtc = new DateTime(2026, 11, 5, 14, 53, 0),
                WindDirDeg = 270, WindKt = 8, GustKt = 18, VisibilitySm = 1.5,
                TempC = -3, DewpointC = -5, AltimeterHundredths = 2992,
            };
            r.Weather.Add("-RA"); r.Weather.Add("BR");
            r.Sky.Add("FEW008"); r.Sky.Add("BKN015"); r.Sky.Add("OVC040");
            Assert.AreEqual("METAR KXYZ 051453Z AUTO 27008G18KT 1 1/2SM -RA BR FEW008 BKN015 OVC040 M03/M05 A2992 RMK AO2", r.Raw);
            Assert.AreEqual("Test Field automated weather observation, one four five three zulu. Wind two seven zero at eight, gusts one eight. "
                + "Visibility one and one half. Light rain. Mist. Sky condition few clouds at eight hundred, ceiling one thousand five hundred broken, "
                + "ceiling four thousand overcast. Temperature minus three, dewpoint minus five. Altimeter two niner niner two.", r.Spoken);

            Assert.AreEqual("00000KT", WeatherStations.WindGroup(0, 0, 0));
            Assert.AreEqual("36005KT", WeatherStations.WindGroup(360, 5, 0));
            Assert.AreEqual("M1/4SM", WeatherStations.VisibilityGroup(0));
            Assert.AreEqual("1/4SM", WeatherStations.VisibilityGroup(0.25));
            Assert.AreEqual("3/4SM", WeatherStations.VisibilityGroup(0.75));
            Assert.AreEqual("2 1/2SM", WeatherStations.VisibilityGroup(2.5));
            Assert.AreEqual("10SM", WeatherStations.VisibilityGroup(10));
            Assert.AreEqual(2.5, WeatherStations.ReportableVisibility(2.9));   // never claims more than it saw
            Assert.AreEqual(10, WeatherStations.ReportableVisibility(37));
            Assert.AreEqual("M03", WeatherStations.TempGroup(-3));
            Assert.AreEqual("00", WeatherStations.TempGroup(0));
            Assert.AreEqual("12", WeatherStations.TempGroup(12));
            // Heights: 100 ft steps to 5,000, 500 ft to 10,000, 1,000 ft above.
            Assert.AreEqual(35, WeatherStations.Hundreds(3480 * 0.3048));
            Assert.AreEqual(75, WeatherStations.Hundreds(7600 * 0.3048));
            Assert.AreEqual(110, WeatherStations.Hundreds(11400 * 0.3048));
            Assert.AreEqual("FEW", WeatherStations.CoverCode(2));
            Assert.AreEqual("SCT", WeatherStations.CoverCode(4));
            Assert.AreEqual("BKN", WeatherStations.CoverCode(7));
            Assert.AreEqual("OVC", WeatherStations.CoverCode(8));
        }
    }
}

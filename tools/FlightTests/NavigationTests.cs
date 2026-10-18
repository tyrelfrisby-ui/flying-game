using FlyingGame.Core;
using FlyingGame.Core.Nav;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The radio navigation system: where the stations are, what can hear them, and whether the altitudes on the
/// charts are still the ones the ground forces.
///
/// Owner, 2026-10-18: a map you can call up any time, *no* own-ship position on it ever, navigation by dead
/// reckoning or VOR, **"make all of the vor's line of sight in function and analize the shaddows"**, and
/// approaches and airways built on TERPS. The analysis he asked for is <see cref="TheShadows"/>, and it is a
/// test rather than a document so that moving a mountain breaks it.
/// </summary>
[Collection("WorldTerrainActive")]
public class NavigationTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public NavigationTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = _t; }

    /// <summary>
    /// Hand the world back. A class that sets <see cref="WorldTerrain.Active"/> and walks away leaves every
    /// later test flying over this world's mountains instead of the flat ground at sea level they were
    /// written for — which is exactly what happened: WindTests' crosswind touchdown started failing, and only
    /// when the whole suite ran, because on its own nobody had set the terrain before it.
    /// </summary>
    public void Dispose() => WorldTerrain.Active = null;

    // The land worth covering: the four plateaus and the country around them.
    private const double XMin = -6800, XMax = 8800, YMin = -14800, YMax = 2000, Grid = 600;

    private double Ground(double x, double y) => _t.HeightAt(x, y, false, false);

    private bool Usable(Vor v, double x, double y, double altM)
        => NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, x, y, altM)
           || NavReception.PathSignal(_t, v.X, v.Y, v.AntennaM, x, y, altM, v.FreqMhz) >= Airways.UsableSignal;

    // ---------------------------------------------------------------- the shadows

    /// <summary>
    /// THE SHADOW ANALYSIS. For each station, and for the network as a whole, what fraction of the world can
    /// actually hear it at a given height above the local ground — and, because a cross fix is how you find
    /// out where you are with no GPS, what fraction can hear TWO.
    ///
    /// The thresholds asserted below are the measured values of 2026-10-18 less a margin. They are here so
    /// that a change to the terrain, or to a station's site, cannot quietly make this world unnavigable.
    /// </summary>
    [Fact]
    public void TheShadows()
    {
        _o.WriteLine("Per station — the fraction of the world that can hear it:");
        foreach (Vor v in Navaids.Vors)
        {
            var line = new System.Text.StringBuilder($"  {v.Id} {v.Name,-10} antenna {v.AntennaM * Terps.FtPerM,6:F0} ft  ");
            foreach (double aglFt in new[] { 1000.0, 3000.0 })
            {
                int n = 0, seen = 0;
                for (double x = XMin; x <= XMax; x += Grid)
                    for (double y = YMin; y <= YMax; y += Grid)
                    {
                        n++;
                        if (Usable(v, x, y, Ground(x, y) + aglFt * Terps.MPerFt)) seen++;
                    }
                line.Append($"{aglFt,6:F0} ft AGL {100.0 * seen / n,5:F1} %   ");
            }
            _o.WriteLine(line.ToString());
        }

        _o.WriteLine("");
        _o.WriteLine("The network — at least one station, and two (a cross fix):");
        var any = new Dictionary<double, double>();
        var two = new Dictionary<double, double>();
        foreach (double aglFt in new[] { 1000.0, 2000.0, 3000.0, 5000.0 })
        {
            int n = 0, a = 0, b = 0;
            for (double x = XMin; x <= XMax; x += Grid)
                for (double y = YMin; y <= YMax; y += Grid)
                {
                    double alt = Ground(x, y) + aglFt * Terps.MPerFt;
                    n++;
                    int k = 0;
                    foreach (Vor v in Navaids.Vors) if (Usable(v, x, y, alt)) k++;
                    if (k >= 1) a++;
                    if (k >= 2) b++;
                }
            any[aglFt] = 100.0 * a / n;
            two[aglFt] = 100.0 * b / n;
            _o.WriteLine($"  {aglFt,6:F0} ft AGL   one or more {any[aglFt],5:F1} %   two or more {two[aglFt],5:F1} %");
        }

        // Measured 2026-10-18: 78.9 / 86.2 / 89.8 / 94.9 one-or-more, 58.6 / 72.9 / 81.1 / 89.8 two-or-more.
        Assert.True(any[1000] > 74, $"one station at 1,000 ft AGL fell to {any[1000]:F1} %");
        Assert.True(any[2000] > 82, $"one station at 2,000 ft AGL fell to {any[2000]:F1} %");
        Assert.True(any[3000] > 86, $"one station at 3,000 ft AGL fell to {any[3000]:F1} %");
        Assert.True(two[1000] > 54, $"a cross fix at 1,000 ft AGL fell to {two[1000]:F1} %");
        Assert.True(two[2000] > 68, $"a cross fix at 2,000 ft AGL fell to {two[2000]:F1} %");
    }

    /// <summary>
    /// The one promise the network has to keep: along the corridor the four airports sit in, you can ALWAYS
    /// fix your position by crossing two radials. Without this the world is not navigable without GPS, which
    /// is the only kind of navigation it has.
    /// </summary>
    [Fact]
    public void TwoStationsAllTheWayDownTheCorridor()
    {
        var thin = new List<string>();
        for (double y = 1000; y >= -11000; y -= 250)
        {
            double alt = Ground(400, y) + 2000 * Terps.MPerFt;
            var heard = new List<string>();
            foreach (Vor v in Navaids.Vors) if (Usable(v, 400, y, alt)) heard.Add(v.Id);
            if (heard.Count < 2) thin.Add($"y {y:F0}: only {(heard.Count == 0 ? "nothing" : string.Join("+", heard))}");
        }
        foreach (string s in thin) _o.WriteLine(s);
        Assert.True(thin.Count == 0,
                    $"the corridor loses its cross fix in {thin.Count} place(s) at 2,000 ft AGL: "
                    + string.Join("; ", thin.Take(4)));
    }

    /// <summary>
    /// The hills earn their place. A station on the Mesa field hears a seventh of the world; the two hill
    /// sites hear half of it. If somebody ever "tidies" the stations onto the airports, this fails.
    /// </summary>
    [Fact]
    public void TheHillSitesBeatTheAirportSites()
    {
        double Coverage(double x, double y)
        {
            double antenna = Ground(x, y) + 5;
            int n = 0, seen = 0;
            for (double px = XMin; px <= XMax; px += Grid)
                for (double py = YMin; py <= YMax; py += Grid)
                {
                    n++;
                    double alt = Ground(px, py) + 1000 * Terps.MPerFt;
                    if (NavReception.InConeOfConfusion(x, y, antenna, px, py, alt)
                        || NavReception.PathSignal(_t, x, y, antenna, px, py, alt, 113.0) >= Airways.UsableSignal) seen++;
                }
            return 100.0 * seen / n;
        }

        double rim = Coverage(Navaids.ById("RIM").X, Navaids.ById("RIM").Y);
        double crs = Coverage(Navaids.ById("CRS").X, Navaids.ById("CRS").Y);
        double mesa = Coverage(400, -6750);     // the Mesa field, where a lazier siting would have put one
        _o.WriteLine($"RIM (the first crest) {rim:F1} %   CRS (the third crest) {crs:F1} %   the Mesa field {mesa:F1} %");
        Assert.True(rim > mesa * 2, $"the Rimrock crest ({rim:F1} %) should hear more than twice what the Mesa field does ({mesa:F1} %)");
        Assert.True(crs > mesa * 2, $"the Crestline crest ({crs:F1} %) should hear more than twice what the Mesa field does ({mesa:F1} %)");
    }

    // ---------------------------------------------------------------- the reception model

    [Fact]
    public void AStationIsUnusableOverheadAndUsableBesideIt()
    {
        Vor v = Navaids.ById("VLY");
        // 3,000 ft above the antenna, a quarter mile to one side: outside the cone, which at that height is
        // about 0.7 NM across.
        double alt = v.AntennaM + 3000 * Terps.MPerFt;
        Assert.True(NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, v.X + 100, v.Y, alt));
        Assert.False(NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, v.X + 1500, v.Y, alt));
        // On the ground beside it there is no cone at all.
        Assert.False(NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, v.X + 100, v.Y, v.GroundM));
    }

    [Fact]
    public void TheGroundInTheWayTakesTheSignalAway()
    {
        // The Valley VOR, read from the Bench plateau just the other side of the escarpment: on the deck
        // behind the wall there is nothing, and from high enough above it there is.
        Vor v = Navaids.ById("VLY");
        double x = 400, y = -2500;
        double low = Ground(x, y) + 100 * Terps.MPerFt;
        double high = Ground(x, y) + 6000 * Terps.MPerFt;
        double sLow = NavReception.PathSignal(_t, v.X, v.Y, v.AntennaM, x, y, low, v.FreqMhz);
        double sHigh = NavReception.PathSignal(_t, v.X, v.Y, v.AntennaM, x, y, high, v.FreqMhz);
        _o.WriteLine($"VLY from (400,-2500): 100 ft AGL {sLow:F2}, 6,000 ft AGL {sHigh:F2}");
        Assert.True(sLow < Airways.UsableSignal, $"behind a 2,000 ft wall on the deck the needle should flag, got {sLow:F2}");
        Assert.True(sHigh > sLow, "climbing must help");
    }

    [Fact]
    public void TheFresnelFadeIsSoftRatherThanABinary()
    {
        // Climbing through the shadow edge, the signal must pass through intermediate values rather than
        // snapping 0 to 1 — a hard edge makes the needle chatter as you cross a ridge.
        //
        // The band is NARROW, and that is the physics rather than a shortcoming: behind a sheer 2,000 ft
        // escarpment a kilometre away, a hundred feet of climb buys a lot of clearance, so the whole fade
        // happens inside a few hundred feet. Sampled every 10 ft it is plainly a ramp; sampled every 100 ft
        // you would see two values and could mistake it for a switch.
        Vor v = Navaids.ById("VLY");
        int partial = 0;
        double first = -1, last = -1;
        for (double aglFt = 100; aglFt <= 8000; aglFt += 10)
        {
            double s = NavReception.PathSignal(_t, v.X, v.Y, v.AntennaM, 400, -2500,
                                               Ground(400, -2500) + aglFt * Terps.MPerFt, v.FreqMhz);
            if (s > 0.02 && s < 0.98) { partial++; if (first < 0) first = aglFt; last = aglFt; }
        }
        _o.WriteLine($"{partial} altitudes gave a partial signal, from {first:F0} to {last:F0} ft AGL");
        Assert.True(partial >= 5, $"the fade should be a ramp, not a switch: only {partial} partial values");
        Assert.True(last - first >= 50, $"the fade spans only {last - first:F0} ft");
    }

    [Fact]
    public void RadialsAndDistancesAreWhatAPilotWouldRead()
    {
        Vor v = Navaids.ById("VLY");
        Assert.Equal(0, NavReception.RadialDeg(v.X, v.Y, v.X + 1000, v.Y), 3);        // north
        Assert.Equal(90, NavReception.RadialDeg(v.X, v.Y, v.X, v.Y + 1000), 3);       // east
        Assert.Equal(180, NavReception.RadialDeg(v.X, v.Y, v.X - 1000, v.Y), 3);      // south
        Assert.Equal(270, NavReception.RadialDeg(v.X, v.Y, v.X, v.Y - 1000), 3);      // west
        // DME is slant range, so it over-reads overhead: 6,000 ft directly above reads about a mile.
        double d = NavReception.SlantRangeNm(v.X, v.Y, v.AntennaM, v.X, v.Y, v.AntennaM + 6000 * Terps.MPerFt);
        Assert.Equal(0.988, d, 2);
        Assert.Equal(-10, NavReception.AngleDiff(10, 0), 6);
        Assert.Equal(20, NavReception.AngleDiff(350, 10), 6);
    }

    [Fact]
    public void EveryFixCanBeIdentifiedByTheRadialAndDistanceTheChartPrints()
    {
        // A fix a pilot cannot find is not a fix. Each one's charted radial and DME must actually put it
        // where the procedure says it is, within the precision the chart is printed to.
        var bad = new List<string>();
        foreach (ProcedureFix f in Airways.EnRouteFixes)
        {
            // "VLY R-087 at 4.3 DME"
            string[] w = f.Identified.Split(' ');
            if (w.Length < 4 || !w[1].StartsWith("R-")) continue;
            Vor v = Navaids.ById(w[0]);
            double radial = double.Parse(w[1].Substring(2));
            double dme = double.Parse(w[3]);
            double actualRadial = NavReception.RadialDeg(v.X, v.Y, f.X, f.Y);
            // Judged at a typical altitude for the fix, because DME is slant range.
            double alt = (Ground(f.X, f.Y) + 3000 * Terps.MPerFt);
            double actualDme = NavReception.SlantRangeNm(v.X, v.Y, v.AntennaM, f.X, f.Y, alt);
            double dr = System.Math.Abs(NavReception.AngleDiff(radial, actualRadial));
            double dd = System.Math.Abs(dme - actualDme);
            _o.WriteLine($"{f.Name,-6} charted {f.Identified,-22} actual R-{actualRadial:F1} at {actualDme:F2} DME");
            if (dr > 1.0) bad.Add($"{f.Name}: charted R-{radial:F0}, actually R-{actualRadial:F1}");
            if (dd > 0.25) bad.Add($"{f.Name}: charted {dme:F1} DME, actually {actualDme:F2}");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // ---------------------------------------------------------------- TERPS

    /// <summary>
    /// EVERY POINT IN THIS WORLD IS MOUNTAINOUS, including four miles out to sea — and that is worth a test
    /// of its own because it is not what anybody would guess from flying the coast.
    ///
    /// ICAO's test is relief of more than 900 m within 10 NM. Ten nautical miles is 18.5 km, and this world
    /// is 16 km by 27 km, so a 10 NM circle anywhere in it reaches from the beach into country that is
    /// kilometres higher: the relief at the Valley field is 3,170 m and at a fix four miles offshore it is
    /// still 2,676 m. The consequence is real and shows on the chart — the en-route obstacle clearance here
    /// is **2,000 ft everywhere**, never 1,000, which is most of why the airway MEAs look high for such
    /// short legs.
    /// </summary>
    [Fact]
    public void EveryPointInThisWorldIsMountainousIncludingTheSea()
    {
        var notMountainous = new List<string>();
        foreach (var p in new[] { (400.0, 0.0, "the Valley field"), (400.0, -6750.0, "the Mesa field"),
                                  (4200.0, -6000.0, "the big Alaska peak"), (850.0, 7970.0, "SEALS, 4 NM out to sea"),
                                  (-6200.0, -1180.0, "SCREE, the south end of V2") })
        {
            double relief = Terps.Relief10NmM(_t, p.Item1, p.Item2);
            bool mtn = relief > Terps.MountainousReliefM;
            _o.WriteLine($"{p.Item3,-26} relief within 10 NM {relief,7:F0} m  -> {(mtn ? "mountainous" : "NOT mountainous")}");
            if (!mtn) notMountainous.Add(p.Item3);
        }
        Assert.True(notMountainous.Count == 0,
                    "somewhere stopped being mountainous, so an MEA somewhere just dropped 1,000 ft: "
                    + string.Join(", ", notMountainous));
        // And the en-route clearance that follows from it.
        Assert.Equal(Terps.EnRouteRocMountainousFt, Terps.RocFt(NavSegment.EnRoute, Terps.IsMountainous(_t, 400, 0)));
    }

    [Fact]
    public void TheOcsAndTheGlidepathAreTheRealGeometry()
    {
        // 34:1 is 102 ÷ 3, so at one nautical mile the surface is 1 NM / 34 and the 3° path is a shade
        // under three times as high — which is the whole margin a precision approach is built on.
        Assert.Equal(6076.115 / 34.0, Terps.PrecisionOcsHeightFt(1.0), 1);
        Assert.Equal(318.4, Terps.GlidepathHeightFt(1.0), 1);
        Assert.True(Terps.GlidepathHeightFt(1.0) > Terps.PrecisionOcsHeightFt(1.0) * 1.7);
        // Rounding: a minimum altitude never rounds DOWN.
        Assert.Equal(4100, Terps.RoundUpFt(4001, 100));
        Assert.Equal(4100, Terps.RoundUpFt(4100, 100));
        Assert.Equal(7260, Terps.RoundUpFt(7241, 20));
    }

    [Fact]
    public void TheLateralScaleIsDeclaredAndTheVerticalCriteriaAreNot()
    {
        // The one place this world is not full size. If somebody puts the real 4 NM back, the airway between
        // the hill stations goes to 19,800 ft, so the constant is here and the reason is in Terps.
        Assert.Equal(1.0, Terps.EnRouteHalfWidthNm, 6);
        Assert.Equal(4.0, Terps.EnRouteHalfWidthNmReal, 6);
        Assert.Equal(1000.0, Terps.EnRouteRocFt, 6);
        Assert.Equal(2000.0, Terps.EnRouteRocMountainousFt, 6);
        Assert.Equal(250.0, Terps.FinalNonPrecisionRocFt, 6);
        Assert.Equal(200.0, Terps.CategoryOneHatFt, 6);
    }

    /// <summary>
    /// THE NEEDLE POINTS THE WAY YOU HAVE TO STEER, both tracking away from a station and tracking toward
    /// one — and the two are not symmetrical. The first version of this had the FROM case inverted, which
    /// makes the instrument not merely wrong but actively dangerous: you would chase the needle away from
    /// the course. It is asserted both ways round here because reading the arithmetic is not enough.
    /// </summary>
    [Theory]
    // obs, radial you are on, expected needle sign (+1 the course is right, -1 left, 0 on course), TO?
    [InlineData(360, 0, 0, false)]        // tracking out on 360, on the course
    [InlineData(360, 10, -1, false)]      // east of a northbound course: the course is to the WEST, needle left
    [InlineData(360, 350, +1, false)]     // west of it: needle right
    [InlineData(90, 100, -1, false)]      // tracking out on 090, south of the course... needle left
    [InlineData(360, 180, 0, true)]       // inbound on 360 from due south, on the course
    [InlineData(360, 175, -1, true)]      // inbound, drifted east: needle left
    [InlineData(360, 185, +1, true)]      // inbound, drifted west: needle right
    [InlineData(270, 90, 0, true)]        // inbound on 270 from due east
    public void TheNeedleShowsWhichWayToSteer(double obs, double radial, int expectSign, bool expectTo)
    {
        double dev = NavReception.CourseDeviationDeg(obs, radial, out bool to);
        _o.WriteLine($"OBS {obs:000}, on R-{radial:000}: needle {dev:+0.0;-0.0;0.0} {(to ? "TO" : "FROM")}");
        Assert.Equal(expectTo, to);
        Assert.Equal(expectSign, System.Math.Sign(System.Math.Round(dev, 6)));
    }

    /// <summary>
    /// The localizer, which is the inbound case again. Valley runway 27: the course is 270 and the antenna
    /// stands beyond the stop end, so an aeroplane on the centreline bears 090 from it.
    ///
    /// The picture, settled once so nobody has to settle it again: **facing west, north is on your right.**
    /// So drifting north on a westbound final is drifting RIGHT of course, the course is then to your LEFT,
    /// and the needle must go left.
    /// </summary>
    [Fact]
    public void ALocalizerNeedleAlsoPointsTheWayToSteer()
    {
        Assert.Equal(0, NavReception.LocalizerDeviationDeg(270, 90), 6);
        // Drifted north: the bearing from the antenna swings from 090 toward 088.
        double driftedNorth = NavReception.LocalizerDeviationDeg(270, 88);
        double driftedSouth = NavReception.LocalizerDeviationDeg(270, 92);
        _o.WriteLine($"localizer 270 — drifted north: {driftedNorth:+0.0;-0.0}, drifted south: {driftedSouth:+0.0;-0.0}");
        Assert.Equal(-2.0, driftedNorth, 6);      // course is to the left
        Assert.Equal(+2.0, driftedSouth, 6);      // course is to the right
        // Two degrees is most of a localizer's 2.5 degree full scale and a fifth of a VOR's ten, which is
        // the whole reason a localizer feels tight: the same angle, four times the needle.
        Assert.True(System.Math.Abs(driftedNorth) / 2.5 > 0.75);
        Assert.True(System.Math.Abs(driftedNorth) / 10.0 < 0.25);
    }

    // ---------------------------------------------------------------- the beacons

    /// <summary>
    /// THE WHOLE POINT OF HAVING BEACONS: a low-frequency signal follows the ground, so a ridge that puts a
    /// VOR into shadow does not shadow an NDB at all. Asserted at the exact spot that proves it — on the
    /// deck behind the first escarpment, where the Valley VOR is unreadable.
    /// </summary>
    [Fact]
    public void ABeaconIsHeardWhereAVorIsNot()
    {
        Vor vly = Navaids.ById("VLY");
        Ndb bn = Navaids.NdbById("BN");
        double x = 400, y = -2500;
        double low = Ground(x, y) + 100 * Terps.MPerFt;

        double vor = NavReception.PathSignal(_t, vly.X, vly.Y, vly.AntennaM, x, y, low, vly.FreqMhz);
        double ndb = NavReception.NdbSignal(bn, x, y);
        _o.WriteLine($"at (400,-2500), 100 ft AGL: the Valley VOR reads {vor:F2}, the Bench beacon reads {ndb:F2}");
        Assert.True(vor < Airways.UsableSignal, "the VOR should be in shadow there");
        Assert.True(ndb > 0.9, "the beacon should not care about the wall");
    }

    [Fact]
    public void ABeaconFadesWithDistanceAndNothingElse()
    {
        Ndb bn = Navaids.NdbById("BN");
        // Inside its rated range it is full strength wherever you are and whatever is in between.
        Assert.Equal(1.0, NavReception.NdbSignal(bn, bn.X, bn.Y + 10.0 * NavReception.MetresPerNm), 6);
        // Beyond it, it fades, and half again as far it is gone.
        double at17 = NavReception.NdbSignal(bn, bn.X, bn.Y + 17.0 * NavReception.MetresPerNm);
        double at24 = NavReception.NdbSignal(bn, bn.X, bn.Y + 24.0 * NavReception.MetresPerNm);
        _o.WriteLine($"BN at 10 NM 1.00, at 17 NM {at17:F2}, at 24 NM {at24:F2}");
        Assert.InRange(at17, 0.05, 0.95);
        Assert.Equal(0.0, at24, 6);
        // The world is 15 NM across, so from anywhere in it at least one beacon is readable.
        int tot = 0, heard = 0;
        for (double x = XMin; x <= XMax; x += Grid)
            for (double y = YMin; y <= YMax; y += Grid)
            {
                tot++;
                foreach (Ndb n in Navaids.Ndbs)
                    if (NavReception.NdbSignal(n, x, y) > 0.2) { heard++; break; }
            }
        _o.WriteLine($"a beacon is readable over {100.0 * heard / tot:F1} % of the world");
        Assert.Equal(tot, heard);
    }

    /// <summary>
    /// And the price of that reach. The needle leans toward high ground beside the path, which is what makes
    /// an ADF approach in mountains a different thing from a VOR one — so the error has to be real, bounded,
    /// and ZERO where there is nothing in the way.
    /// </summary>
    [Fact]
    public void TheTerrainBendsTheNeedleButOnlyWhereThereIsTerrain()
    {
        Ndb bn = Navaids.NdbById("BN");
        // Straight down the Bench 27 final, over the flat Valley plateau toward the beacon: nothing stands
        // above the path, so the needle must be honest. This is the bit of sky the approach is flown in.
        double onFinal = NavReception.NdbBearingErrorDeg(_t, bn, 850, 4000, Ground(850, 4000) + 4000 * Terps.MPerFt);
        _o.WriteLine($"on the Bench 27 final at 4,000 ft: error {onFinal:+0.0;-0.0;0.0} deg");
        Assert.True(System.Math.Abs(onFinal) < 0.5, $"the needle lies by {onFinal:F1} deg where the ground is flat");

        // Low down with a range between you and it, it leans — and never further than the cap.
        double worst = 0;
        for (double x = XMin; x <= XMax; x += Grid)
            for (double y = YMin; y <= YMax; y += Grid)
                foreach (Ndb n in Navaids.Ndbs)
                {
                    double e = NavReception.NdbBearingErrorDeg(_t, n, x, y, Ground(x, y) + 1000 * Terps.MPerFt);
                    worst = System.Math.Max(worst, System.Math.Abs(e));
                }
        _o.WriteLine($"worst bearing error anywhere at 1,000 ft AGL: {worst:F1} deg");
        Assert.True(worst > 2.0, "mountain effect should be worth something somewhere");
        Assert.True(worst <= NavReception.NdbBearingErrorMaxDeg + 1e-9, $"the error is uncapped: {worst:F1} deg");

        // The same error, same place, every time: a pilot can learn this world's needles.
        double a = NavReception.NdbBearingErrorDeg(_t, bn, -3000, -8000, 2500);
        double b = NavReception.NdbBearingErrorDeg(_t, bn, -3000, -8000, 2500);
        Assert.Equal(a, b, 9);
    }

    [Fact]
    public void AnAdfNeedlePointsAtTheBeaconRelativeToTheNose()
    {
        // Relative bearing is what the instrument shows, and it is useless without the heading — which is
        // exactly why the heading card sits next to it.
        Assert.Equal(0, NavReception.RelativeBearingDeg(90, 90), 6);        // dead ahead
        Assert.Equal(90, NavReception.RelativeBearingDeg(180, 90), 6);      // off the right wing
        Assert.Equal(270, NavReception.RelativeBearingDeg(0, 90), 6);       // off the left wing
        Assert.Equal(180, NavReception.RelativeBearingDeg(270, 90), 6);     // behind
        // Homing to the Bench beacon from down the final: it should be on the nose while tracking 270.
        Ndb bn = Navaids.NdbById("BN");
        double brg = NavReception.BearingToDeg(850, 5000, bn.X, bn.Y);
        _o.WriteLine($"from 1.8 NM east of BN on the centreline, the beacon bears {brg:F1}");
        Assert.Equal(270, brg, 1);
        Assert.Equal(0, NavReception.RelativeBearingDeg(brg, 270), 1);
    }

    [Fact]
    public void TheBeaconsSitOnTheGroundTheTableSaysTheyDo()
    {
        // A navaid's elevation is a surveyed fact on a chart. If the terrain moves under one, the chart is
        // wrong — and the first version of this had the Bench beacon declared 522 ft below the hillside it
        // was actually standing on.
        foreach (Ndb n in Navaids.Ndbs)
        {
            double ground = Ground(n.X, n.Y);
            _o.WriteLine($"{n.Id}: declared {n.GroundM:F1} m, the ground is {ground:F1} m");
            Assert.Equal(n.GroundM, ground, 1);
        }
        foreach (Vor v in Navaids.Vors)
        {
            double ground = Ground(v.X, v.Y);
            _o.WriteLine($"{v.Id}: declared {v.GroundM:F1} m, the ground is {ground:F1} m");
            Assert.True(System.Math.Abs(v.GroundM - ground) < 1.0,
                        $"{v.Id} is charted at {v.GroundM:F1} m but stands on {ground:F1} m");
        }
    }
}

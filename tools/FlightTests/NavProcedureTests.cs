using FlyingGame.Core;
using FlyingGame.Core.Nav;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The airways and the approach plates, re-derived from the terrain on every run.
///
/// A published altitude is a survey, and a survey goes stale the moment somebody moves a mountain. The
/// numbers in <see cref="Approaches.All"/> and the MEAs the charts print are all computed here from
/// <see cref="Terps"/> and <see cref="ApproachGeometry"/> and compared with what is published. If this test
/// fails, the chart is wrong, not the test.
/// </summary>
[Collection("WorldTerrainActive")]
public class NavProcedureTests
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public NavProcedureTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = _t; }

    private ApproachGeometry.RunwayEndGeom End(string airport, string rwy)
    {
        foreach (var r in ApproachGeometry.RunwayEnds()) if (r.Airport == airport && r.Name == rwy) return r;
        throw new System.ArgumentException($"{airport} {rwy}");
    }

    [Fact]
    public void EveryPublishedMinimumIsStillWhatTheGroundForces()
    {
        var wrong = new List<string>();
        foreach (Approach a in Approaches.All)
        {
            var r = End(a.Airport, a.Runway);
            double derived;
            if (a.Kind == ApproachKind.Ils)
                derived = ApproachGeometry.IlsDecisionAltitudeFt(_t, r);
            else
            {
                var faf = a.Fix(FixRole.Faf);
                double nm = System.Math.Sqrt((faf.X - r.ThresholdX) * (faf.X - r.ThresholdX)
                                           + (faf.Y - r.ThresholdY) * (faf.Y - r.ThresholdY)) / Terps.MetresPerNm;
                derived = ApproachGeometry.NonPrecisionMdaFt(_t, r, nm);
            }
            _o.WriteLine($"{a.Airport,-8} {a.Title,-12} published {a.MinimumsFt,7:F0} ft   derived {derived,7:F0} ft"
                         + $"   HAT {a.HatFt,5:F0}");
            if (System.Math.Abs(derived - a.MinimumsFt) > 0.5)
                wrong.Add($"{a.Airport} {a.Title}: published {a.MinimumsFt:F0}, the ground now says {derived:F0}");
            if (System.Math.Abs((a.MinimumsFt - a.TdzeFt) - a.HatFt) > 0.5)
                wrong.Add($"{a.Airport} {a.Title}: HAT {a.HatFt:F0} does not match {a.MinimumsFt:F0} - {a.TdzeFt:F0}");
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public void AnIlsIsAlignedWithItsRunwayAndAVorFinalIsCloseEnough()
    {
        foreach (Approach a in Approaches.All)
        {
            var r = End(a.Airport, a.Runway);
            double off = System.Math.Abs(NavReception.AngleDiff(r.CourseDeg, a.FinalCourseDeg));
            _o.WriteLine($"{a.Airport,-8} {a.Title,-12} runway {r.CourseDeg,3:F0}, final approach course {a.FinalCourseDeg,3:F0}  off by {off:F1} deg");
            // A localizer is ON the centreline; a VOR final may be up to 30 degrees off it.
            Assert.True(off <= (a.Kind == ApproachKind.Ils ? 0.5 : 30.0),
                        $"{a.Airport} {a.Title} final approach course is {off:F1} deg off the runway");
            if (a.Localizer is Ils ils)
            {
                Assert.Equal(r.ThresholdX, ils.ThresholdX, 1);
                Assert.Equal(r.ThresholdY, ils.ThresholdY, 1);
                Assert.Equal(a.MinimumsFt, ils.DecisionAltitudeFt, 1);
                // The frequency band: a localizer lives on an odd tenth between 108.10 and 111.95.
                Assert.InRange(ils.FreqMhz, 108.10, 111.95);
                Assert.Equal(1, (int)System.Math.Round(ils.FreqMhz * 10) % 2);
            }
        }
    }

    [Fact]
    public void EveryFinalApproachFixIsOnItsOwnFinalApproachCourse()
    {
        foreach (Approach a in Approaches.All)
        {
            var r = End(a.Airport, a.Runway);
            var faf = a.Fix(FixRole.Faf);
            // How far the fix lies off the extended centreline.
            double back = (r.CourseDeg + 180) * System.Math.PI / 180;
            double ux = System.Math.Cos(back), uy = System.Math.Sin(back);
            double dx = faf.X - r.ThresholdX, dy = faf.Y - r.ThresholdY;
            double along = dx * ux + dy * uy, across = System.Math.Abs(-dx * uy + dy * ux);
            _o.WriteLine($"{a.Airport,-8} {a.Title,-12} FAF {faf.Name,-6} {along / Terps.MetresPerNm,5:F2} NM out, {across,5:F0} m off the centreline");
            Assert.True(along > 0, $"{a.Airport} {a.Title}: the FAF is behind the threshold");
            Assert.True(across < 60, $"{a.Airport} {a.Title}: the FAF is {across:F0} m off the final approach course");
            // A final approach segment of 2 to 6 NM: shorter and there is no room to get stable, longer and
            // this world runs out of room.
            Assert.InRange(along / Terps.MetresPerNm, 2.0, 6.0);
        }
    }

    [Fact]
    public void TheDescentFromEachFinalApproachFixIsFlyable()
    {
        // TERPS wants a non-precision final at about 318 ft/NM and no more than 400. A precision final IS
        // the glidepath, so the thing to check there is that the published crossing altitude is at or just
        // above where the glideslope actually is at that fix.
        foreach (Approach a in Approaches.All)
        {
            var r = End(a.Airport, a.Runway);
            var faf = a.Fix(FixRole.Faf);
            double nm = System.Math.Sqrt((faf.X - r.ThresholdX) * (faf.X - r.ThresholdX)
                                       + (faf.Y - r.ThresholdY) * (faf.Y - r.ThresholdY)) / Terps.MetresPerNm;
            if (a.Kind == ApproachKind.Ils)
            {
                double gs = a.TdzeFt + Terps.GlidepathHeightFt(nm);
                _o.WriteLine($"{a.Airport,-8} {a.Title,-12} cross {faf.Name} at {faf.MinAltitudeFt,7:F0} ft; the glideslope is at {gs,7:F0} ft");
                Assert.True(faf.MinAltitudeFt >= gs - 1,
                            $"{a.Airport} {a.Title}: crossing {faf.Name} at {faf.MinAltitudeFt:F0} puts you BELOW the glideslope ({gs:F0})");
                Assert.True(faf.MinAltitudeFt - gs < 600,
                            $"{a.Airport} {a.Title}: crossing {faf.Name} at {faf.MinAltitudeFt:F0} is {faf.MinAltitudeFt - gs:F0} ft above the glideslope");
            }
            else
            {
                double gradient = (faf.MinAltitudeFt - a.MinimumsFt) / nm;
                _o.WriteLine($"{a.Airport,-8} {a.Title,-12} {faf.MinAltitudeFt,7:F0} ft at {faf.Name} down to {a.MinimumsFt,7:F0} ft in {nm:F2} NM = {gradient:F0} ft/NM");
                Assert.InRange(gradient, 0, 400);
            }
        }
    }

    [Fact]
    public void OneApproachHasACourseReversalAndItIsFlownWhereThereIsRoomForIt()
    {
        var withPt = Approaches.All.Where(a => a.ProcedureTurn != TurnSide.None).ToList();
        Assert.True(withPt.Count >= 1, "the owner asked for at least one procedure turn");
        foreach (Approach a in withPt)
        {
            var fix = a.Fixes.First(f => f.Name == a.ProcedureTurnFix);
            double hi = Terps.HighestInCircleM(_t, fix.X, fix.Y, a.ProcedureTurnRemainWithinNm, 150) * Terps.FtPerM;
            double need = Terps.RoundUpFt(hi + Terps.ProcedureTurnRocFt, 100);
            _o.WriteLine($"{a.Airport} {a.Title}: PT within {a.ProcedureTurnRemainWithinNm:F1} NM of {fix.Name}, "
                         + $"published {a.ProcedureTurnAltitudeFt:F0} ft, the ground needs {need:F0} ft");
            Assert.True(a.ProcedureTurnAltitudeFt >= need,
                        $"{a.Airport} {a.Title}: the procedure turn is charted at {a.ProcedureTurnAltitudeFt:F0} ft "
                        + $"but 1,000 ft over the highest thing within {a.ProcedureTurnRemainWithinNm:F1} NM is {need:F0} ft");
            // And it must be possible to get down from it to the final approach fix.
            var faf = a.Fix(FixRole.Faf);
            Assert.True(a.ProcedureTurnAltitudeFt - faf.MinAltitudeFt < 1500,
                        $"{a.Airport} {a.Title}: {a.ProcedureTurnAltitudeFt - faf.MinAltitudeFt:F0} ft to lose between the "
                        + "procedure turn and the final approach fix");
        }
    }

    [Fact]
    public void NoApproachClaimsAProcedureTurnTheTerrainWouldNotAllow()
    {
        // Summit's plate says no course reversal is published and gives the reason. That reason is checked
        // here, because a note on a chart that nobody verifies is just a note.
        Approach sum = Approaches.All.First(a => a.Airport == "Summit");
        Assert.Equal(TurnSide.None, sum.ProcedureTurn);
        Vor v = Navaids.ById("SUM");
        foreach (double nm in new[] { 2.0, 2.5, 3.0 })
        {
            double need = Terps.RoundUpFt(Terps.HighestInCircleM(_t, v.X, v.Y, nm, 150) * Terps.FtPerM + 1000, 100);
            _o.WriteLine($"a procedure turn within {nm:F1} NM of SUM would need {need:F0} ft");
            // The field is at 7,000 ft; anything over 11,000 is not a light-aeroplane procedure.
            Assert.True(need > 11000, $"a {nm:F1} NM procedure turn at Summit now needs only {need:F0} ft — "
                                      + "the terrain has changed and Summit could have a course reversal after all");
        }
    }

    [Fact]
    public void EveryAirwayLegHasAMinimumAltitudeYouCouldActuallyClimbTo()
    {
        var legs = Airways.Legs(_t);
        Assert.NotEmpty(legs);
        foreach (AirwayLeg leg in legs)
        {
            _o.WriteLine($"{leg.FromId,-6} -> {leg.ToId,-6} crs {leg.CourseDeg,3:F0}  {leg.DistanceNm,5:F2} NM"
                         + $"   MEA {leg.MeaFt,7:F0} ft ({leg.MeaDrivenBy})   MOCA {leg.MocaFt,7:F0} ft");
            Assert.True(leg.MeaFt >= leg.MocaFt, $"{leg.FromId}-{leg.ToId}: the MEA is below the MOCA");
            Assert.True(leg.MeaFt < 16000,
                        $"{leg.FromId}-{leg.ToId}: MEA {leg.MeaFt:F0} ft is above anything in this fleet");
            // An airway leg shorter than a quarter of a mile is a bend nobody can fly.
            Assert.True(leg.DistanceNm > 0.25, $"{leg.FromId}-{leg.ToId} is only {leg.DistanceNm:F2} NM long");
        }
    }

    [Fact]
    public void TheHillStationsAreWhyNoAirwayMeaIsSetByTheRadio()
    {
        // Every MEA here is set by terrain, not signal — which is the payoff of putting the two extra
        // stations on the crests. Worth asserting, because if a station moved down off its hill some leg
        // would start being limited by reception and the chart would have to say so.
        foreach (AirwayLeg leg in Airways.Legs(_t))
            Assert.Equal("terrain", leg.MeaDrivenBy);
    }

    [Fact]
    public void EveryProcedureHasTheFixesAPilotNeedsToFlyIt()
    {
        foreach (Approach a in Approaches.All)
        {
            Assert.True(a.Has(FixRole.Iaf), $"{a.Airport} {a.Title} has no initial approach fix");
            Assert.True(a.Has(FixRole.Faf), $"{a.Airport} {a.Title} has no final approach fix");
            Assert.True(a.Has(FixRole.MissedApproachPoint), $"{a.Airport} {a.Title} has no missed approach point");
            Assert.True(a.Has(FixRole.MissedApproachHold), $"{a.Airport} {a.Title} has nowhere to go when it does not work out");
            Assert.False(string.IsNullOrEmpty(a.MissedApproachText), $"{a.Airport} {a.Title} has no missed approach");
            Assert.False(string.IsNullOrEmpty(a.Notes), $"{a.Airport} {a.Title} does not say why it looks the way it does");
            // Every fix must say how it is identified, because this world has no GPS and never will.
            foreach (ProcedureFix f in a.Fixes)
                Assert.False(string.IsNullOrEmpty(f.Identified), $"{a.Airport} {a.Title}: {f.Name} cannot be identified");
        }
    }

    [Fact]
    public void NoTwoStationsShareAFrequency()
    {
        double[] f = Navaids.Frequencies();
        for (int i = 1; i < f.Length; i++) Assert.True(f[i] > f[i - 1], $"two stations on {f[i]:F2}");
        foreach (Vor v in Navaids.Vors)
        {
            Assert.InRange(v.FreqMhz, 108.00, 117.95);
            // A VOR is on an even tenth below 112, or anywhere above it.
            if (v.FreqMhz < 112.0) Assert.Equal(0, (int)System.Math.Round(v.FreqMhz * 10) % 2);
        }
    }
}

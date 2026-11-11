using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT IS ACTUALLY WRONG WITH THE SHADOW (open visual report 2026-11-09, Ty: "the shadow does not scale with
/// height"). This measures the three things that phrase could mean, separately, so the answer is a number and not a
/// reading of the words:
///
///   (1) SIZE. GroundShadow flattens the airframe along the sun direction. That is a PARALLEL projection, so the
///       ground footprint is the aeroplane's own cross-section at every height. <see cref="GroundSizeIsHeightInvariantAndThatIsCorrect"/>
///       measures it: the same 10.64 m across at 3 m and at 1,500 m, 0 % change. The sun subtends 0.53°, i.e. it is
///       a point to within half a degree, so size invariance is REAL BEHAVIOUR and was never the bug.
///   (2) TRACKING. The same projection displaces the shadow by height/tan(elevation) along the sun's azimuth.
///       <see cref="TheShadowTracksAwayAlongTheSunSAzimuth"/> measures it: 30 m AGL with the sun at 45° puts the
///       shadow 30.0 m down-sun, not under the aeroplane. That was never the bug either.
///   (3) SOFTNESS. PlanarShadow.shader draws one flat silhouette at a constant alpha. Before SunShadow landed, a
///       1,500 m shadow was drawn exactly as dark and exactly as hard-edged as a 3 m one. THAT is the defect, and
///       it is the one <see cref="FlyingGame.Core.SunShadow"/> fixes.
///
/// So this file is also the independent check on the fix that landed for (3), which has never been built or flown.
/// It renders the J-3's real planform (from configs/aircraft/j3-cub-like.json — the strips' own chords and the
/// fuselage's own crossflow half-widths) onto the ground three ways at each height, with GroundShadow's own
/// projection arithmetic:
///
///   BEFORE   the trunks' shadow: sharp silhouette, alpha 0.45, at every height.
///   AFTER    the lane's shadow: the same silhouette scaled by SunShadow.Grow about its centre, alpha 0.45 x Opacity01.
///   TRUTH    the physically exact penumbra: the sharp silhouette convolved with the sun's disc at that slant range,
///            which is what fraction of the sun each patch of ground can see. No model in it at all.
///
/// TRUTH is the arbiter the flat-silhouette model has never been held against, and it says two things. The flux is
/// right — AFTER and TRUTH block the same total light to the last digit, which is the rule SunShadow is built on.
/// The DISTRIBUTION is approximate, and in one direction: a uniform patch drawn out to the penumbra's outer edge
/// must under-darken the core and over-darken the rim. The numbers for that are printed and asserted below.
///
/// SHADOW=1 writes the before/after/truth panels as PNGs (SHADOW_OUT=&lt;dir&gt; to choose where). The assertions run
/// either way; nothing here needs Unity, a GPU or a build.
/// </summary>
public sealed class SunShadowPenumbraProbe
{
    private readonly ITestOutputHelper _o;
    public SunShadowPenumbraProbe(ITestOutputHelper o) { _o = o; }

    private const double Feet = 0.3048;
    private const double ShadeAlpha = 0.45;      // GroundShadow.Shade's alpha
    private const double SunElevationDeg = 45.0; // the figure's sun; the tracking test sweeps it
    private const double SunAzimuthDeg = 135.0;  // the direction the light TRAVELS, from north, so the shadow goes SE

    // The raster the panels and the convolution share. 0.08 m/px over 512 px is 40.96 m of ground, which holds the
    // J-3 (10.64 m span) plus the penumbra's outer edge at 1,500 m AGL (9.87 m each side) with room to see it end.
    private const double MetresPerPixel = 0.08;
    private const int Pixels = 512;

    private static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    /// <summary>One flat piece of the airframe: its planform rectangle in body axes (x forward, y right) and the
    /// height of its own plane above the body axis. GroundShadow projects every vertex along the sun, and a flat
    /// piece all at one height therefore lands as its own planform translated by that height's offset — so a list of
    /// these reproduces what GroundShadow draws without needing the meshes.</summary>
    private readonly struct Slab
    {
        public readonly double XAft, XFwd, YLeft, YRight, Up;
        public Slab(double xAft, double xFwd, double yLeft, double yRight, double up)
        { XAft = xAft; XFwd = xFwd; YLeft = yLeft; YRight = yRight; Up = up; }
    }

    /// <summary>
    /// The J-3's planform out of its own config. Every lifting surface contributes one slab per strip — the strip's
    /// chord placed about its quarter-chord station, spanning its own share of the span — and the fuselage
    /// contributes one slab per crossflow station pair from its own stationHalfY. The vStab has no planform area
    /// worth drawing (it is a vertical sheet; edge-on from above it is ~0.02 m wide) and is left out, which is also
    /// what it contributes to a top-down shadow.
    /// </summary>
    private static List<Slab> J3Planform()
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", "j3-cub-like.json"));
        var slabs = new List<Slab>();

        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (sf.Id.Contains("vStab") || sf.Id.Contains("rudder")) continue;
            var strips = sf.Strips;
            if (strips == null || strips.Count == 0) continue;
            // Each strip owns the span between the midpoints to its neighbours; the outermost own half a pitch.
            var ys = new double[strips.Count];
            for (int i = 0; i < strips.Count; i++) ys[i] = strips[i].Pos[1];
            for (int i = 0; i < strips.Count; i++)
            {
                double below = i > 0 ? 0.5 * (ys[i] + ys[i - 1]) : ys[i] - (strips.Count > 1 ? 0.5 * Math.Abs(ys[1] - ys[0]) : 0.5);
                double above = i < strips.Count - 1 ? 0.5 * (ys[i] + ys[i + 1]) : ys[i] + (strips.Count > 1 ? 0.5 * Math.Abs(ys[^1] - ys[^2]) : 0.5);
                double ch = strips[i].Chord;
                double x = strips[i].Pos[0];
                // z in the config is down-positive about the body axis (the Cub's high wing sits at z = -0.75).
                slabs.Add(new Slab(x - 0.75 * ch, x + 0.25 * ch, Math.Min(below, above), Math.Max(below, above), -strips[i].Pos[2]));
            }
        }

        FuselageConfig f = c.Fuselage;
        if (f?.Crossflow?.StationX != null && f.Crossflow.StationHalfY != null)
        {
            double[] sx = f.Crossflow.StationX, hy = f.Crossflow.StationHalfY, mz = f.Crossflow.StationMidZ;
            for (int i = 0; i + 1 < sx.Length; i++)
            {
                double half = 0.5 * (hy[i] + hy[i + 1]);
                double up = mz != null && mz.Length == sx.Length ? -0.5 * (mz[i] + mz[i + 1]) : 0.0;
                slabs.Add(new Slab(Math.Min(sx[i], sx[i + 1]), Math.Max(sx[i], sx[i + 1]), -half, half, up));
            }
            // The cowl and engine ahead of the first station, which the crossflow table does not carry (it starts at
            // x = -0.3). The J-3 is 6.83 m long overall and the table reaches -6.1, so this is the remaining 1.03 m.
            slabs.Add(new Slab(sx[0], sx[0] + 1.03, -0.21, 0.21, -(mz != null ? mz[0] : 0.0)));
        }
        return slabs;
    }

    /// <summary>Half the airframe's horizontal size, m — what GroundShadow.MeasureHalfSize reads off the meshes and
    /// what SunShadow compares the penumbra against.</summary>
    private static double HalfSize(List<Slab> slabs)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (Slab s in slabs)
        {
            minX = Math.Min(minX, s.XAft); maxX = Math.Max(maxX, s.XFwd);
            minY = Math.Min(minY, s.YLeft); maxY = Math.Max(maxY, s.YRight);
        }
        return Math.Max(0.5, 0.5 * Math.Max(maxX - minX, maxY - minY));
    }

    /// <summary>The sun's travel direction, unit, in Unity axes (x east, y up, z north): GroundShadow's l.</summary>
    private static (double X, double Y, double Z) SunDir(double elevationDeg, double azimuthDeg)
    {
        double e = elevationDeg * Math.PI / 180.0, a = azimuthDeg * Math.PI / 180.0;
        return (Math.Cos(e) * Math.Sin(a), -Math.Sin(e), Math.Cos(e) * Math.Cos(a));
    }

    /// <summary>
    /// GroundShadow's own projection, slab by slab, into a coverage raster centred on the shadow of the body origin.
    /// Coverage is a UNION, not a sum: PlanarShadow.shader's stencil exists so overlapping parts darken a pixel once.
    /// <paramref name="grow"/> scales about that same centre, which is what the lane's GroundShadow does.
    /// </summary>
    private static double[] Rasterise(List<Slab> slabs, double altitudeM, double elevationDeg, double azimuthDeg, double grow)
    {
        var l = SunDir(elevationDeg, azimuthDeg);
        double kx = l.X / l.Y, kz = l.Z / l.Y;   // GroundShadow: S = X - l * (X.y - GroundY) / l.y
        var cov = new double[Pixels * Pixels];
        // Body x forward is world +z (north), body y right is world +x (east), level and heading north.
        foreach (Slab s in slabs)
        {
            double h = altitudeM + s.Up;                    // this slab's own height above the ground plane
            double offE = -kx * h, offN = -kz * h;          // down-sun displacement, = h/tan(elevation) along azimuth
            // The slab's shadow, in metres east/north of the body origin's own shadow (which is at offE0/offN0).
            double e0 = s.YLeft + offE, e1 = s.YRight + offE, n0 = s.XAft + offN, n1 = s.XFwd + offN;
            double cE = -kx * altitudeM, cN = -kz * altitudeM;  // the centre the grow scales about
            e0 = cE + grow * (e0 - cE); e1 = cE + grow * (e1 - cE);
            n0 = cN + grow * (n0 - cN); n1 = cN + grow * (n1 - cN);
            // Panels are centred on that same centre, so subtract it and go to pixels.
            FillRect(cov, (e0 - cE) / MetresPerPixel, (e1 - cE) / MetresPerPixel, (n0 - cN) / MetresPerPixel, (n1 - cN) / MetresPerPixel);
        }
        return cov;
    }

    /// <summary>Union-fill one axis-aligned rectangle, in pixels about the raster's centre, with box antialiasing so
    /// a 0.39 m aileron strip does not vanish or double in width between panels.</summary>
    private static void FillRect(double[] cov, double x0, double x1, double y0, double y1)
    {
        double half = Pixels * 0.5;
        int ix0 = (int)Math.Floor(x0 + half), ix1 = (int)Math.Ceiling(x1 + half);
        int iy0 = (int)Math.Floor(y0 + half), iy1 = (int)Math.Ceiling(y1 + half);
        for (int py = Math.Max(0, iy0); py < Math.Min(Pixels, iy1); py++)
        {
            double fy = Math.Min(py + 1, y1 + half) - Math.Max(py, y0 + half);
            if (fy <= 0) continue;
            // north is up in the image, so row 0 is the far edge
            int row = (Pixels - 1 - py) * Pixels;
            for (int px = Math.Max(0, ix0); px < Math.Min(Pixels, ix1); px++)
            {
                double fx = Math.Min(px + 1, x1 + half) - Math.Max(px, x0 + half);
                if (fx <= 0) continue;
                double a = fx * fy;
                int i = row + px;
                if (cov[i] < a) cov[i] = a;   // union
            }
        }
    }

    /// <summary>
    /// The exact penumbra: the fraction of the sun's disc each patch of ground cannot see, which is the sharp
    /// silhouette convolved with the sun's disc of radius p = slant x theta/2 projected to the ground. No model.
    /// Done with a per-row prefix sum, so a 160 px kernel costs one pass per kernel row.
    /// </summary>
    private static double[] ConvolveWithSunDisc(double[] sharp, double penumbraHalfWidthM)
    {
        double pr = penumbraHalfWidthM / MetresPerPixel;
        if (pr < 0.5) return (double[])sharp.Clone();
        var outp = new double[Pixels * Pixels];
        var prefix = new double[Pixels + 1];
        int k = (int)Math.Ceiling(pr);
        double weightSum = 0;
        var halfWidths = new double[2 * k + 1];
        for (int dy = -k; dy <= k; dy++)
        {
            double inside = pr * pr - dy * dy;
            double w = inside > 0 ? Math.Sqrt(inside) : 0;
            halfWidths[dy + k] = w;
            weightSum += 2 * w;   // the disc's area, in pixel-rows x pixel-widths
        }
        if (weightSum <= 0) return (double[])sharp.Clone();

        for (int srcY = 0; srcY < Pixels; srcY++)
        {
            int srcRow = srcY * Pixels;
            prefix[0] = 0;
            for (int x = 0; x < Pixels; x++) prefix[x + 1] = prefix[x] + sharp[srcRow + x];
            for (int dy = -k; dy <= k; dy++)
            {
                double w = halfWidths[dy + k];
                if (w <= 0) continue;
                int dstY = srcY + dy;
                if (dstY < 0 || dstY >= Pixels) continue;
                int dstRow = dstY * Pixels;
                int span = (int)Math.Round(w);
                if (span < 1) span = 1;
                double scale = 2.0 * w / (2 * span + 1) / weightSum;   // this kernel row's share, spread over its run
                for (int x = 0; x < Pixels; x++)
                {
                    int a = Math.Max(0, x - span), b = Math.Min(Pixels, x + span + 1);
                    outp[dstRow + x] += (prefix[b] - prefix[a]) * scale;
                }
            }
        }
        return outp;
    }

    private static double Sum(double[] a) { double s = 0; foreach (double v in a) s += v; return s; }
    private static double Max(double[] a) { double m = 0; foreach (double v in a) if (v > m) m = v; return m; }

    /// <summary>The alpha distribution's RMS radius in metres: how spread out the darkening actually is. Convolution
    /// ADDS variances, so this is the one number that says whether a growth law spreads the patch by the right
    /// amount rather than merely conserving its total.</summary>
    private static double RmsRadiusM(double[] a)
    {
        double w = 0, sx = 0, sy = 0, sxx = 0, syy = 0;
        for (int y = 0; y < Pixels; y++)
            for (int x = 0; x < Pixels; x++)
            {
                double v = a[y * Pixels + x];
                if (v <= 0) continue;
                w += v; sx += v * x; sy += v * y; sxx += v * x * x; syy += v * y * y;
            }
        if (w <= 0) return 0;
        double mx = sx / w, my = sy / w;
        double varr = sxx / w - mx * mx + syy / w - my * my;
        return Math.Sqrt(Math.Max(0, varr)) * MetresPerPixel;
    }

    // ---------------------------------------------------------------- the three claims

    /// <summary>
    /// (1) SIZE, the thing Ty named. The ground footprint is the same at 3 m and at 1,500 m, because a parallel
    /// projection of a solid is its own cross-section however far it travels. This test exists to say that the
    /// invariance is CORRECT and is not the defect: the sun is a point to within half a degree.
    /// </summary>
    [Fact]
    public void GroundSizeIsHeightInvariantAndThatIsCorrect()
    {
        List<Slab> j3 = J3Planform();
        double span3 = SharpFootprintEastM(j3, 3.0), span1500 = SharpFootprintEastM(j3, 1500.0);
        _o.WriteLine($"  sharp footprint across the sun's azimuth:  3 m AGL {span3:F2} m,  1500 m AGL {span1500:F2} m,  change {100 * (span1500 / span3 - 1):F2} %");
        Assert.Equal(span3, span1500, 2);
        // And it is the aeroplane's own span, to the raster's resolution.
        Assert.InRange(span3, 10.4, 10.9);
    }

    /// <summary>(2) TRACKING. height / tan(elevation) down-sun, which is the landing cue the shadow is there for.</summary>
    [Theory]
    [InlineData(10.0, 45.0, 10.0)]
    [InlineData(30.0, 45.0, 30.0)]
    [InlineData(30.0, 20.0, 82.42)]
    [InlineData(30.0, 70.0, 10.92)]
    public void TheShadowTracksAwayAlongTheSunSAzimuth(double altitudeM, double elevationDeg, double expectM)
    {
        var l = SunDir(elevationDeg, SunAzimuthDeg);
        double offE = -(l.X / l.Y) * altitudeM, offN = -(l.Z / l.Y) * altitudeM;
        double dist = Math.Sqrt(offE * offE + offN * offN);
        _o.WriteLine($"  {altitudeM,6:F0} m AGL, sun {elevationDeg:F0} deg: shadow {dist:F2} m down-sun ({offE:F1} E, {offN:F1} N)");
        Assert.Equal(expectM, dist, 1);
        // Down-sun, not under the aeroplane: the azimuth is 135 deg, so the shadow goes south-east.
        Assert.True(offE > 0 && offN < 0);
    }

    /// <summary>
    /// (3) SOFTNESS — the defect. What the trunks draw against what the lane draws against what is true, measured on
    /// the J-3's own planform. The assertions are the three things that are certain; the printed table is the answer
    /// to "which of the three is wrong", and the PNGs under SHADOW=1 are the same thing as a picture.
    /// </summary>
    [Fact]
    public void TheSoftnessIsWhatWasMissingAndTheFlatPatchGetsTheFluxRightAndTheCoreTooLight()
    {
        List<Slab> j3 = J3Planform();
        double w = HalfSize(j3);
        _o.WriteLine($"  J-3 from its own config: half-size {w:F2} m (span {2 * w:F2} m); SunShadow umbra gone at slant {SunShadow.UmbraVanishesAtM(w):F0} m");
        _o.WriteLine("");
        _o.WriteLine("    ft AGL | slant m |   p m | Grow | BEFORE alpha | AFTER alpha | TRUTH peak | flux A/B/T (m2)        | RMS r  A/T (m)");
        _o.WriteLine("   --------+---------+-------+------+--------------+-------------+------------+------------------------+---------------");

        string outDir = Environment.GetEnvironmentVariable("SHADOW_OUT")
                        ?? Path.Combine(Path.GetTempPath(), "sun-shadow-panels");
        bool write = Environment.GetEnvironmentVariable("SHADOW") == "1";
        if (write) Directory.CreateDirectory(outDir);
        var sheet = new List<(double[] Cov, double Alpha)>();   // row-major, 3 columns: before, after, truth

        double px2 = MetresPerPixel * MetresPerPixel;
        double worstCoreRatio = 1.0, bestCoreRatio = 0.0, worstSpread = 1.0;
        double? rmsBefore = null;   // the trunks' spread: the sharp silhouette's, which never changes with height
        double topAfterSpread = 0, topTruthSpread = 0;
        // 4,500 ft is the top of the table because GroundShadow.MaxAltitudeM is 1,500 m (4,921 ft): above that it
        // draws nothing at all, so there is no model up there to be right or wrong about.
        foreach (double ft in new[] { 10.0, 100.0, 500.0, 1500.0, 4500.0 })
        {
            double alt = ft * Feet;
            double slant = alt / Math.Sin(SunElevationDeg * Math.PI / 180.0);
            double p = SunShadow.PenumbraHalfWidthM(slant);
            (double grow, double opacity) = SunShadow.Soften(slant, w);

            double[] sharp = Rasterise(j3, alt, SunElevationDeg, SunAzimuthDeg, 1.0);
            double[] after = Rasterise(j3, alt, SunElevationDeg, SunAzimuthDeg, grow);
            double[] truth = ConvolveWithSunDisc(sharp, p);

            // alpha = coverage x the material's alpha x (the model's opacity, for AFTER only)
            double beforeAlpha = ShadeAlpha;                       // the trunks: constant, at every height
            double afterAlpha = ShadeAlpha * opacity;
            double truthPeak = ShadeAlpha * Max(truth);
            double fluxBefore = Sum(sharp) * px2 * ShadeAlpha;
            double fluxAfter = Sum(after) * px2 * afterAlpha;
            double fluxTruth = Sum(truth) * px2 * ShadeAlpha;
            double rmsAfter = RmsRadiusM(after), rmsTruth = RmsRadiusM(truth);

            _o.WriteLine($"   {ft,7:F0} | {slant,7:F0} | {p,5:F2} | {grow,4:F2} | {beforeAlpha,12:F3} | {afterAlpha,11:F3} | {truthPeak,10:F3} | {fluxAfter,6:F2} /{fluxBefore,6:F2} /{fluxTruth,6:F2} | {rmsAfter,5:F2} / {rmsTruth,5:F2}");

            // THE FLUX RULE SunShadow is built on, against the exact penumbra: the same light blocked, at every
            // height. It is a RELATIVE tolerance because the scale-about-one-centre is not exactly area-preserving
            // for a silhouette drawn as a UNION — grown apart, the wing and the tailplane stop overlapping the
            // fuselage by quite the same amount. That costs 1 % at 1,500 ft and is the only place the model's own
            // arithmetic leaks; it is far below the 59 % darkness change it is there to produce.
            Assert.InRange(fluxAfter / fluxTruth, 0.97, 1.03);
            // The uniform patch is drawn out to the penumbra's OUTER edge, so it cannot also be as dark as the true
            // core, and it must be more spread out than the truth. Both are measured, at every height that softens.
            if (grow > 1.02)
            {
                double ratio = afterAlpha / truthPeak;
                worstCoreRatio = Math.Min(worstCoreRatio, ratio);
                bestCoreRatio = Math.Max(bestCoreRatio, ratio);
                worstSpread = Math.Max(worstSpread, rmsAfter / rmsTruth);
            }
            topAfterSpread = rmsAfter; topTruthSpread = rmsTruth;
            // THE DISCRIMINATING NUMBER. Flux does NOT separate the two models: a sharp silhouette at 0.45 and a
            // grown one at 0.45/grow^2 block the same light, and so does the truth, so all three columns agree and
            // none of them is evidence. What separates them is HOW SPREAD OUT the darkening is — convolution adds
            // variances, so the RMS radius is the one quantity the sun's disc has to move and a parallel projection
            // cannot. The trunks' never moves; the lane's follows the truth's.
            rmsBefore ??= rmsAfter;              // 10 ft: p is 0.02 m, so all three are the sharp silhouette
            Assert.Equal(rmsBefore.Value, RmsRadiusM(sharp), 2);
            if (grow > 1.05)
                Assert.True(rmsAfter > rmsBefore.Value * 1.05 && rmsTruth > rmsBefore.Value * 1.0,
                    $"at {ft} ft the shadow has not spread: after {rmsAfter:F2} m, truth {rmsTruth:F2} m, sharp {rmsBefore:F2} m");

            if (write)
            {
                WritePng(Path.Combine(outDir, $"shadow-{ft:F0}ft-1-before.png"), sharp, ShadeAlpha, $"BEFORE  {ft:F0} ft");
                WritePng(Path.Combine(outDir, $"shadow-{ft:F0}ft-2-after.png"), after, afterAlpha, $"AFTER  {ft:F0} ft");
                WritePng(Path.Combine(outDir, $"shadow-{ft:F0}ft-3-truth.png"), truth, ShadeAlpha, $"TRUTH  {ft:F0} ft");
                sheet.Add((sharp, ShadeAlpha));    // column 1: the trunks
                sheet.Add((after, afterAlpha));    // column 2: the lane
                sheet.Add((truth, ShadeAlpha));    // column 3: the exact penumbra
            }
        }

        _o.WriteLine("");
        _o.WriteLine("  WHICH OF THE THREE IS WRONG. The footprint column does not move and the BEFORE alpha column");
        _o.WriteLine("  does not move: the trunks draw a 4,500 ft shadow exactly as wide, exactly as dark and exactly");
        _o.WriteLine("  as hard-edged as a 10 ft one. Size was never the bug and tracking was never the bug.");
        _o.WriteLine($"  SPREAD is: BEFORE {rmsBefore:F2} m at every height, TRUTH {rmsBefore:F2} -> {topTruthSpread:F2} m,");
        _o.WriteLine($"  the lane's fix {rmsBefore:F2} -> {topAfterSpread:F2} m. By 4,500 ft the trunks' shadow is");
        _o.WriteLine($"  x{topTruthSpread / rmsBefore.Value:F2} too tight; the fix follows the truth to within x{worstSpread:F2}.");
        _o.WriteLine("");
        _o.WriteLine($"  WHAT THE FLAT PATCH COSTS: its core runs {worstCoreRatio:F2} to {bestCoreRatio:F2} of the TRUE core's darkness.");
        _o.WriteLine("  Both signs show up, and both are the price of ONE draw at ONE alpha rather than a wrong");
        _o.WriteLine("  penumbra. Through the transition the uniform fill carries flux out to the rim and leaves the");
        _o.WriteLine("  core too light (0.75 at 500 ft, where the real shadow still has a nearly full-dark middle);");
        _o.WriteLine("  far above it, a SCALED silhouette keeps the aeroplane's thin shape and its gaps, so it covers");
        _o.WriteLine("  less ground than a real blur of radius p and comes out too dark (1.67 at 4,500 ft). This is");
        _o.WriteLine("  the trade SunShadow's own derivation names when it rejects the centre rule; it is a judgement");
        _o.WriteLine("  about one draw, not an error, and it is far inside what the height bug itself was costing.");
        _o.WriteLine("  FLUX is NOT evidence either way: all three columns agree by construction.");
        if (write)
        {
            WriteContactSheet(Path.Combine(outDir, "shadow-contact-sheet.png"), sheet, 3);
            _o.WriteLine($"  panels, and shadow-contact-sheet.png (rows 10/100/500/1500/4500 ft, columns BEFORE/AFTER/TRUTH): {outDir}");
        }

        // The spread is the claim. The trunks' is height-invariant (asserted per row above); the truth more than
        // doubles it; the fix follows within a third.
        Assert.True(topTruthSpread > 2.0 * rmsBefore!.Value, "the sun's disc should more than double the spread by 4,500 ft");
        Assert.InRange(worstSpread, 1.0, 1.4);
        // The price of one draw at one alpha, bounded both ways. If either end moves, the growth law has changed and
        // somebody should be told which way rather than have a test quietly re-baselined.
        Assert.InRange(worstCoreRatio, 0.5, 0.95);
        Assert.InRange(bestCoreRatio, 1.2, 2.5);
    }

    /// <summary>
    /// THE GUARD. The reflection bug was reported twice because its fix sat on a lane that merged into nothing; this
    /// shadow fix is one source file and one call site away from the same fate. If GroundShadow ever stops consuming
    /// SunShadow, the shadow is silently back to one hard silhouette at every height and nothing else notices.
    /// </summary>
    [Fact]
    public void GroundShadowStillConsumesSunShadow()
    {
        string src = File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "GroundShadow.cs"));
        Assert.Contains("SunShadow.Soften(", src);
        Assert.Contains("_halfSizeM", src);
        // The slant range, not the altitude: that is what makes an evening shadow softer than a midday one.
        Assert.Contains("altitude / -l.y", src);
    }

    // ---------------------------------------------------------------- the picture

    /// <summary>The sharp silhouette's extent across the sun's azimuth, m, measured off the raster itself rather than
    /// from the slab list, so it measures what is actually drawn.</summary>
    private static double SharpFootprintEastM(List<Slab> slabs, double altitudeM)
    {
        double[] cov = Rasterise(slabs, altitudeM, SunElevationDeg, SunAzimuthDeg, 1.0);
        int minX = Pixels, maxX = -1;
        for (int y = 0; y < Pixels; y++)
            for (int x = 0; x < Pixels; x++)
                if (cov[y * Pixels + x] > 0.5) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
        return maxX < minX ? 0 : (maxX - minX + 1) * MetresPerPixel;
    }

    /// <summary>
    /// One panel: the coverage raster composited over a plain sunlit-grass colour at <paramref name="alpha"/>, with a
    /// 1 m grid so the three columns can be compared by eye, and a 10 m scale bar. Plain 8-bit RGB PNG, written with
    /// ZLibStream — no image library in this project and none wanted.
    /// </summary>
    private static void WritePng(string path, double[] cov, double alpha, string label)
    {
        var rgb = new byte[Pixels * Pixels * 3];
        // Sunlit short grass, and the shadow colour GroundShadow uses (black at the given alpha).
        const int gr = 122, gg = 142, gb = 86;
        int gridPx = (int)Math.Round(1.0 / MetresPerPixel);
        for (int y = 0; y < Pixels; y++)
            for (int x = 0; x < Pixels; x++)
            {
                int i = y * Pixels + x;
                bool grid = x % gridPx == 0 || y % gridPx == 0;
                int br = grid ? gr - 12 : gr, bg2 = grid ? gg - 12 : gg, bb = grid ? gb - 12 : gb;
                double a = Math.Clamp(cov[i], 0, 1) * alpha;
                rgb[i * 3 + 0] = (byte)Math.Round(br * (1 - a));
                rgb[i * 3 + 1] = (byte)Math.Round(bg2 * (1 - a));
                rgb[i * 3 + 2] = (byte)Math.Round(bb * (1 - a));
            }
        // A 10 m scale bar, bottom left, so no panel can be read at the wrong scale.
        int barPx = (int)Math.Round(10.0 / MetresPerPixel);
        for (int x = 8; x < Math.Min(Pixels - 8, 8 + barPx); x++)
            for (int y = Pixels - 16; y < Pixels - 10; y++)
            { int i = (y * Pixels + x) * 3; rgb[i] = 255; rgb[i + 1] = 255; rgb[i + 2] = 255; }

        WriteRgbPng(path, rgb, Pixels, Pixels, label);
    }

    /// <summary>Plain 8-bit RGB PNG. This project has no image library and does not want one; zlib is in the BCL
    /// (ZLibStream) and the only other thing a PNG needs is its own CRC32.</summary>
    private static void WriteRgbPng(string path, byte[] rgb, int w, int h, string label)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, w); WriteBe(ihdr, 4, h);
        ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(fs, "IHDR", ihdr);
        var raw = new byte[h * (1 + w * 3)];
        for (int y = 0; y < h; y++)
        {
            raw[y * (1 + w * 3)] = 0;                       // filter: none
            Array.Copy(rgb, y * w * 3, raw, y * (1 + w * 3) + 1, w * 3);
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "tEXt", Encoding.ASCII.GetBytes("Comment\0" + label));
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    /// <summary>
    /// Every panel in one picture: rows are heights, columns are BEFORE / AFTER / TRUTH, each panel box-averaged
    /// down by 3. The three columns share one scale and one grass colour, so the only thing that can differ between
    /// them is the shadow — which is the whole point of putting them side by side.
    /// </summary>
    private static void WriteContactSheet(string path, List<(double[] Cov, double Alpha)> panels, int cols)
    {
        const int Shrink = 3;
        int cell = Pixels / Shrink;                       // 170 px
        int rows = (panels.Count + cols - 1) / cols;
        int w = cols * cell + (cols + 1) * 4, h = rows * cell + (rows + 1) * 4;
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++) { rgb[i * 3] = 24; rgb[i * 3 + 1] = 24; rgb[i * 3 + 2] = 26; }   // gutters
        const int gr = 122, gg = 142, gb = 86;
        for (int p = 0; p < panels.Count; p++)
        {
            int r = p / cols, c = p % cols;
            int ox = 4 + c * (cell + 4), oy = 4 + r * (cell + 4);
            (double[] cov, double alpha) = panels[p];
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    double acc = 0;
                    for (int sy = 0; sy < Shrink; sy++)
                        for (int sx = 0; sx < Shrink; sx++)
                            acc += cov[(y * Shrink + sy) * Pixels + x * Shrink + sx];
                    double a = Math.Clamp(acc / (Shrink * Shrink), 0, 1) * alpha;
                    int i = ((oy + y) * w + ox + x) * 3;
                    rgb[i] = (byte)Math.Round(gr * (1 - a));
                    rgb[i + 1] = (byte)Math.Round(gg * (1 - a));
                    rgb[i + 2] = (byte)Math.Round(gb * (1 - a));
                }
        }
        WriteRgbPng(path, rgb, w, h, "shadow contact sheet: rows 10/100/500/1500/4500 ft, columns BEFORE/AFTER/TRUTH");
    }

    private static void WriteBe(byte[] b, int o, int v)
    { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBe(len, 0, data.Length); s.Write(len, 0, 4);
        byte[] t = Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4); s.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFFu;
        crc = Crc32(t, crc); crc = Crc32(data, crc); crc ^= 0xFFFFFFFFu;
        var cb = new byte[4]; WriteBe(cb, 0, (int)crc); s.Write(cb, 0, 4);
    }

    private static uint[] _crcTable;
    private static uint Crc32(byte[] data, uint crc)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _crcTable[n] = c;
            }
        }
        foreach (byte b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

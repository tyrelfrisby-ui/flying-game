using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// TDPF=1 — THE NACA TAIL-VOLUME SPIN-RECOVERY CRITERION, computed from the AUTHORED strip geometry so it is
/// reproducible instead of read off a drawing. TDPF = TDR x URVC, plotted in NACA TN 1329 against the inertia
/// yawing-moment parameter; above the boundary the aeroplane is predicted to recover from a developed spin by
/// full rapid rudder reversal alone within two turns.
///
/// Definitions used, stated because the construction has several conventions in circulation:
///   URVC = (S_R1*L1 + S_R2*L2) / (S * b/2)   unshielded RUDDER volume coefficient. S_R1 is movable rudder area
///          ABOVE the horizontal tail's shielded wedge, S_R2 is movable rudder area BELOW the horizontal tail
///          plane (all of which is unshielded); L1, L2 are their centroids' distance aft of the CG.
///   The shielded wedge is bounded above by a line from the horizontal tail's LEADING edge at 60 deg to the
///   fuselage reference line, running aft and up — the sense that matters, because at spin attitude the flow
///   comes from below and ahead, so the stabiliser's wake convects aft and UP over the fin. That is why the
///   fin is blanketed in a flat spin while a rudder area below the tail plane is not: eta_t measures 0.26 on
///   the Pitts' fin against 1.16-1.22 on its stabiliser (PittsSpinRudderAuthorityTests).
///   TDR  = S_F * L^2 / (S * (b/2)^2)         tail damping ratio. S_F is the fuselage SIDE area below the
///          horizontal tail plane and aft of the CG, L the distance from the CG to its centroid.
///
/// HONESTY ABOUT S_F. The configs carry no fuselage side PROFILE — only a total `crossflow.sideArea` and a
/// body radius — so S_F here is an approximation: a body half-depth tapering linearly from `bodyRadiusM` at
/// the CG station to a quarter of it at the horizontal tail's trailing edge, integrated below the tail plane.
/// The Pitts' TDR from the authored three-view (docs/pending/PITTS-FLAT-SPIN-RUDDER-2026-11-11.md) is 0.0251;
/// whatever this method gives for the Pitts is printed beside it so every other type's figure can be read as a
/// RATIO against a known one rather than as an absolute. URVC needs no such apology: it comes entirely from
/// authored strip geometry.
///
/// Caveats that must travel with any TDPF number, from the same pending note: TN 1329's population was
/// conventional monoplanes 1935-45; NASA TP 1009 (1977) concluded the criterion "cannot be used as a criterion
/// for prediction of spin recovery; at best it should be considered only as a guideline for tail design"; it is
/// optimistic for short-tailed aeroplanes; and TN 1329 computed every boundary with the AILERONS NEUTRAL.
/// </summary>
public class TailVolumeCriterionProbe
{
    private readonly ITestOutputHelper _o;
    public TailVolumeCriterionProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id)
    {
        // TDPF_REPO lets this run from a build output outside the repo tree (the walk-up finds nothing there).
        string? root = Environment.GetEnvironmentVariable("TDPF_REPO");
        if (root == null)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            root = d?.FullName ?? ".";
        }
        return AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", id + ".json"));
    }

    internal readonly record struct TailVolume(double S, double B, double Zht, double XhtLe, double XhtTe,
                                               double Sr1, double L1, double Sr2, double L2, double Urvc,
                                               double Sf, double Lf, double Tdr, double Tdpf, double Mu, double Iymp,
                                               double RudArea, double CrossingStrips);

    /// <summary>Everything the criterion needs, from the authored geometry alone.</summary>
    internal static TailVolume Compute(AircraftConfig cfg)
    {
        double cgX = cfg.Mass.CgVec().X;

        // Wing reference: total authored lifting area (a biplane counts both wings; see the pending note on why
        // total rather than upper-only) and b = the widest span authored, which on a biplane is the upper wing.
        double S = 0, halfSpan = 0;
        foreach (var sf in cfg.Surfaces)
            if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase))
                foreach (var st in sf.Strips) { S += st.Area; halfSpan = Math.Max(halfSpan, Math.Abs(st.PosVec().Y)); }
        double b = 2 * halfSpan;

        // Horizontal tail plane and chordwise extent. Strip pos is the quarter chord, so LE = x + c/4 and
        // TE = x - 3c/4 (x is negative aft in this frame).
        double zSum = 0, aSum = 0, xLe = double.NegativeInfinity, xTe = double.PositiveInfinity;
        foreach (var sf in cfg.Surfaces)
            if (sf.Id is "hStab" or "elevator")
                foreach (var st in sf.Strips)
                {
                    var p = st.PosVec();
                    zSum += st.Area * p.Z; aSum += st.Area;
                    xLe = Math.Max(xLe, p.X + st.Chord / 4);
                    xTe = Math.Min(xTe, p.X - 3 * st.Chord / 4);
                }
        double zHt = aSum > 0 ? zSum / aSum : 0;

        // The movable rudder, sliced in z so a strip that straddles the tail plane is split rather than binned.
        double sr1 = 0, sr1x = 0, sr2 = 0, sr2x = 0, rudArea = 0, crossing = 0;
        const double Tan60 = 1.7320508;
        foreach (var sf in cfg.Surfaces)
            if (sf.Id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase))
                foreach (var st in sf.Strips)
                {
                    var p = st.PosVec();
                    double height = st.Chord > 1e-9 ? st.Area / st.Chord : 0;    // the strip's own z extent
                    rudArea += st.Area;
                    if ((p.Z - height / 2) < zHt && (p.Z + height / 2) > zHt) crossing++;
                    const int N = 64;
                    double dA = st.Area / N;
                    for (int i = 0; i < N; i++)
                    {
                        double z = p.Z - height / 2 + height * (i + 0.5) / N;
                        if (z > zHt) { sr2 += dA; sr2x += dA * p.X; }            // below the tail plane: unshielded
                        else
                        {
                            double hAbove = zHt - z, dLe = xLe - p.X;            // aft of the LE is positive
                            if (hAbove > Tan60 * Math.Max(0, dLe)) { sr1 += dA; sr1x += dA * p.X; }
                        }
                    }
                }
        double l1 = sr1 > 1e-9 ? Math.Abs(sr1x / sr1 - cgX) : 0;
        double l2 = sr2 > 1e-9 ? Math.Abs(sr2x / sr2 - cgX) : 0;
        double urvc = S * b / 2 > 1e-9 ? (sr1 * l1 + sr2 * l2) / (S * b / 2) : 0;

        // S_F, approximate (see the class comment): half-depth tapering from bodyRadiusM at the CG station to a
        // quarter of it at the tail plane's trailing edge, integrated as side area below the tail plane.
        double r0 = cfg.Fuselage?.Crossflow?.BodyRadiusM ?? 0.5;
        double sf2 = 0, sfx = 0;
        {
            const int N = 200;
            double x0 = cgX, x1 = xTe, dx = (x0 - x1) / N;                        // x1 is aft of x0, so x0 - x1 > 0
            for (int i = 0; i < N; i++)
            {
                double f = (i + 0.5) / N, x = x0 - (x0 - x1) * f;
                double depth = Math.Max(0, r0 * (1 - 0.75 * f) - zHt);            // zHt negative = tail above datum
                sf2 += depth * dx; sfx += depth * dx * x;
            }
        }
        double lf = sf2 > 1e-9 ? Math.Abs(sfx / sf2 - cgX) : 0;
        double tdr = S * (b / 2) * (b / 2) > 1e-9 ? sf2 * lf * lf / (S * (b / 2) * (b / 2)) : 0;

        // The chart's abscissa: the inertia yawing-moment parameter (Ix - Iy) / (m b^2), and the relative density
        // parameter mu = m / (rho S b) at the test altitude the criterion was drawn for (sea level, rho 1.225).
        var I = cfg.Mass.Inertia;
        double m = cfg.Mass.MassKg;
        double iymp = m * b * b > 1e-9 ? (I.Ixx - I.Iyy) / (m * b * b) : 0;
        double mu = 1.225 * S * b > 1e-9 ? m / (1.225 * S * b) : 0;
        return new TailVolume(S, b, zHt, xLe, xTe, sr1, l1, sr2, l2, urvc, sf2, lf, tdr, tdr * urvc, mu, iymp, rudArea, crossing);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("TDPF") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("TDPF_IDS")
            ?? "pitts-s2b-like,decathlon-8kcab-like,extra-300-like,great-lakes-like,stearman-pt17-like,cassutt-f1-like,c172-like,j3-cub-like")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);

        _o.WriteLine("type                   S m^2    b m   z_ht   rudder  S_R1   L1    S_R2   L2    URVC    S_F*  L_F*  TDR*   TDPF*   mu    IYMP      cross");
        foreach (string raw in ids)
        {
            string id = raw.Trim();
            var cfg = Load(id);
            var t = Compute(cfg);
            _o.WriteLine($"{id,-22}{t.S,7:F2}{t.B,7:F2}{t.Zht,7:F3}{t.RudArea,8:F3}{t.Sr1,7:F3}{t.L1,6:F2}{t.Sr2,8:F3}{t.L2,6:F2}"
                + $"{t.Urvc,8:F4}{t.Sf,7:F2}{t.Lf,6:F2}{t.Tdr,7:F4}{t.Tdpf * 1e6,8:F0}{t.Mu,6:F1}{t.Iymp * 1e4,9:F0}{t.CrossingStrips,7:F0}");
        }
        _o.WriteLine("\n  * S_F, L_F, TDR and therefore TDPF are APPROXIMATE (the configs carry no fuselage side profile — see the");
        _o.WriteLine("    class comment). URVC, S_R1 and S_R2 are exact from the authored strips. TDPF is in units of 1e-6.");
        _o.WriteLine("    IYMP = (Ixx-Iyy)/(m b^2) x 1e4, the TN 1329 chart's abscissa. 'cross' counts rudder strips whose own z");
        _o.WriteLine("    extent STRADDLES the tail plane, i.e. that are split here rather than binned wholly above or below.");
        _o.WriteLine("    Calibration: the Pitts' three-view-derived figures in docs/pending/PITTS-FLAT-SPIN-RUDDER-2026-11-11.md");
        _o.WriteLine("    are TDR 0.0251, URVC 0.0151, TDPF 380e-6 against a boundary near 195e-6 for its mu.");

        // THE BINNING TRAP, flagged on 2026-11-11: the Great Lakes' rudder is authored with NO strip below its
        // horizontal tail although the real aeroplane has 0.090 m^2 down there, so any shielding keyed to a
        // strip's z shields area that should be unshielded. Before any conclusion is drawn from a type's TDPF,
        // this says whether that type has the same defect.
        _o.WriteLine("\nBINNING CHECK — movable rudder area below the horizontal tail plane, as authored:");
        foreach (string raw in ids)
        {
            string id = raw.Trim();
            var cfg = Load(id);
            var t = Compute(cfg);
            double frac = t.RudArea > 1e-9 ? t.Sr2 / t.RudArea : 0;
            _o.WriteLine($"   {id,-22} tail plane z {t.Zht,7:F3}   rudder {t.RudArea,6:F3} m^2   below the plane {t.Sr2,6:F3} m^2"
                + $" ({frac * 100,5:F1} %)   {(t.Sr2 < 1e-6 ? "<-- NO ventral rudder authored" : "")}");
            var zs = new List<string>();
            foreach (var sf in cfg.Surfaces)
                if (sf.Id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase))
                    foreach (var st in sf.Strips)
                    {
                        var p = st.PosVec();
                        double h = st.Chord > 1e-9 ? st.Area / st.Chord : 0;
                        zs.Add($"{p.Z:+0.00;-0.00}[{p.Z - h / 2:+0.00;-0.00}..{p.Z + h / 2:+0.00;-0.00}]");
                    }
            _o.WriteLine($"      strip z centres and extents (body z, POSITIVE IS DOWN): {string.Join(" ", zs)}");
        }
    }
}

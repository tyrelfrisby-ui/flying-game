using System;
using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// The wing CELLULE of a multi-wing aeroplane, measured from its own config, and what the two wings do to each
/// other (owner 2026-10-28: "account for cellule clmax of a biplane configuration … build it as a tool/table that
/// applies to all biplanes current and future").
///
/// Two wings one above the other each fly in the other's field, so the pair never makes what two isolated wings of
/// the same total area would. The loss is governed by the GAP over the CHORD (how far apart they are in chords) and
/// eased by STAGGER (offsetting them fore and aft lets each see cleaner air). This class measures those from the
/// strips — nothing is hand-entered per type — and turns them into two numbers:
///
///   <see cref="SigmaPrandtl"/>   the classical induced-drag interference factor, sigma(gap/span). Prandtl's fit,
///                                 sigma = (1 - 0.66 g/b) / (1.055 + 3.7 g/b). Reported for reference: induced drag
///                                 is currently carried by each surface's own oswaldE, which is hand-set to 0.75 on
///                                 the fleet's biplanes against a monoplane's 0.80-0.82.
///   <see cref="ClMaxFactor"/>    the cellule's CLmax as a fraction of the isolated section's, from
///                                 <see cref="ClMaxByGapOverChord"/> below.
///
/// APPLYING IT IS OPT-IN, PER TYPE (<c>cellule.liftInterference</c>), and defaults OFF. That is deliberate. On
/// 2026-10-28 the fleet's four biplanes were measured against their book stalls and the evidence did not support
/// switching it on everywhere:
///
///   - The STEARMAN has the tightest cellule we have (gap/chord 0.75, against the Great Lakes' 1.24 and the Pitts'
///     1.23) and so should suffer the MOST interference of any of them. It already matches its book stall to +1.3 %
///     with no interference term at all.
///   - The PITTS needs 44 % MORE lift than it makes, not less (it realises CLmax 1.13 where its book stall wants
///     1.63; its section tables are 1.22 upper and 1.05 lower).
///
/// The reading is that the fleet's section tables are already doing this work implicitly — an "effective cellule"
/// value rather than a true 2-D section. So a type may only switch this on once its tables are the real section,
/// or it would be counted twice. The measurements and the factor are available to every type regardless, which is
/// the point of having them here rather than in one aeroplane's numbers.
/// </summary>
public sealed class BiplaneCellule
{
    // `private set`, not `private init`: Unity's compiler has no System.Runtime.CompilerServices.IsExternalInit,
    // so every `init` accessor here is a CS0518 that takes the whole iOS player build down -- and the .NET test
    // build compiles it happily, so the suite stays green while the game will not build. Found 2026-10-28.

    /// <summary>Did this config turn out to have a cellule at all? False for every monoplane.</summary>
    public bool IsCellule { get; private set; }

    public double GapM { get; private set; }
    /// <summary>Positive = the UPPER wing is forward of the lower, the usual arrangement.</summary>
    public double StaggerM { get; private set; }
    public double MeanChordM { get; private set; }
    public double SpanM { get; private set; }
    /// <summary>Difference in rigging incidence, upper minus lower (rad).</summary>
    public double DecalageRad { get; private set; }
    /// <summary>Upper wing area over lower wing area. 1 = an orthogonal cellule.</summary>
    public double AreaRatio { get; private set; }

    public double GapOverChord => MeanChordM > 1e-6 ? GapM / MeanChordM : 0.0;
    public double StaggerOverChord => MeanChordM > 1e-6 ? StaggerM / MeanChordM : 0.0;

    /// <summary>Prandtl's induced-drag interference factor for the gap over the SPAN.</summary>
    public double SigmaPrandtl { get; private set; }

    /// <summary>Cellule CLmax as a fraction of the isolated section's CLmax. 1 for a monoplane.</summary>
    public double ClMaxFactor { get; private set; }

    /// <summary>
    /// MUTUAL INDUCTION ON THE ROLLING LOADS (owner 2026-11-02, the roll rate). Each wing of a biplane flies in the
    /// other's induced field, and that is as true of the antisymmetric load a roll makes as of the lift (Prandtl,
    /// NACA Rep. 116, 1921, the biplane section; Munk, NACA Rep. 151, 1923, "General Biplane Theory"). These are the
    /// ratios of the cellule's rolling moment to the sum of what its two wings make ALONE, for the two antisymmetric
    /// loadings a full-aileron roll is the balance of:
    ///   <see cref="RollDampingInduction"/>  the loading of a steady roll rate (local incidence p·y/V, linear in span);
    ///   <see cref="AileronInduction"/>      the loading of the ailerons (τδ over the aileron span, opposite each side).
    /// <see cref="LiftSlopeInduction"/> is the same ratio for a uniform incidence change — the lift-slope interference —
    /// reported, and deliberately NOT applied (see AeroModel, where these are used). All three are 1 for a monoplane.
    /// Computed by <see cref="Induction"/>: a horseshoe-vortex lattice on this config's own wing strips.
    /// </summary>
    public double RollDampingInduction { get; private set; } = 1.0;
    /// <inheritdoc cref="RollDampingInduction"/>
    public double AileronInduction { get; private set; } = 1.0;
    /// <inheritdoc cref="RollDampingInduction"/>
    public double LiftSlopeInduction { get; private set; } = 1.0;
    /// <summary>The upper and lower wings' own lift-slope interference factors (each wing's lift in the cellule over its
    /// lift alone, uniform incidence): Munk's result that the two wings of a staggered cellule are not affected alike.</summary>
    public double LiftSlopeInductionUpper { get; private set; } = 1.0;
    /// <inheritdoc cref="LiftSlopeInductionUpper"/>
    public double LiftSlopeInductionLower { get; private set; } = 1.0;

    /// <summary>The surface ids that make up the cellule (the wings, not their hinged rows).</summary>
    public IReadOnlyList<string> WingIds { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// CELLULE CLmax AGAINST GAP / CHORD. The classical result: the wings interfere less the further apart they are,
    /// and an infinite gap is two monoplanes. Tabulated so it can be read, argued with and replaced — if someone has
    /// Diehl's <i>Engineering Aerodynamics</i> or NACA TR 256 / TR 269 to hand, overwrite these with the measured
    /// curve and nothing else needs to change.
    ///
    /// THESE ARE A STANDARD-FORM FIT, NOT A TRANSCRIBED TABLE. They carry the shape the classical data has — about
    /// three quarters at half a chord, nine tenths at a chord and a quarter, asymptotic to one — and they should be
    /// treated as that until they are checked against a source.
    /// </summary>
    public static readonly (double GapOverChord, double Factor)[] ClMaxByGapOverChord =
    {
        (0.00, 0.62), (0.50, 0.76), (0.75, 0.82), (1.00, 0.87),
        (1.25, 0.90), (1.50, 0.92), (2.00, 0.95), (3.00, 0.98), (6.00, 1.00),
    };

    /// <summary>Stagger eases the interference: offsetting the wings fore and aft lets each meet cleaner air. Worth
    /// about a point of CLmax per quarter-chord of stagger, and capped — a staggered biplane is still a biplane.</summary>
    public const double StaggerGainPerChord = 0.04, MaxStaggerGain = 0.05;

    private static double Interp((double X, double Y)[] t, double x)
    {
        if (x <= t[0].X) return t[0].Y;
        if (x >= t[^1].X) return t[^1].Y;
        for (int i = 1; i < t.Length; i++)
            if (x <= t[i].X)
            {
                double f = (x - t[i - 1].X) / (t[i].X - t[i - 1].X);
                return t[i - 1].Y + f * (t[i].Y - t[i - 1].Y);
            }
        return t[^1].Y;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, BiplaneCellule> _cache = new();

    /// <summary>Measure this aeroplane's cellule. Cached per config; a monoplane gets <see cref="IsCellule"/> false
    /// and a <see cref="ClMaxFactor"/> of 1, so callers need no special case.</summary>
    public static BiplaneCellule For(AircraftConfig cfg) => _cache.GetValue(cfg, Measure);

    private static BiplaneCellule Measure(AircraftConfig cfg)
    {
        // A "wing" is a fixed lifting surface whose id says wing; its hinged row (…-aileron) belongs to it and is
        // counted into its area, not treated as a wing of its own.
        var wings = new List<(string Id, double Area, double Span, double Z, double Qc, double Chord, double Inc)>();
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            if (!id.Contains("wing") || id.Contains("aileron") || id.Contains("flap") || sf.Strips.Count == 0) continue;
            double area = 0, sp = 0, wz = 0, wq = 0, wc = 0, wi = 0;
            foreach (StripConfig st in sf.Strips)
            {
                area += st.Area; sp = Math.Max(sp, 2 * Math.Abs(st.Pos[1]));
                wz += st.Pos[2] * st.Area; wq += st.Pos[0] * st.Area; wc += st.Chord * st.Area; wi += st.IncidenceRad * st.Area;
            }
            double own = area;
            foreach (SurfaceConfig o in cfg.Surfaces)
                if (!ReferenceEquals(o, sf) && o.Id.StartsWith(sf.Id, StringComparison.OrdinalIgnoreCase))
                    foreach (StripConfig st in o.Strips) area += st.Area;
            if (own > 1e-6) wings.Add((sf.Id, area, sp, wz / own, wq / own, wc / own, wi / own));
        }
        if (wings.Count < 2) return new BiplaneCellule { IsCellule = false, ClMaxFactor = 1.0 };

        // config z is DOWN, so the smallest z is the HIGHEST wing.
        wings.Sort((a, b) => a.Z.CompareTo(b.Z));
        var up = wings[0]; var lo = wings[^1];
        double gap = lo.Z - up.Z;
        double chord = 0.5 * (up.Chord + lo.Chord);
        if (gap <= 0.05 || chord <= 1e-6) return new BiplaneCellule { IsCellule = false, ClMaxFactor = 1.0 };

        double span = Math.Max(up.Span, lo.Span);
        double gb = span > 1e-6 ? gap / span : 0.0;
        double sigma = (1.0 - 0.66 * gb) / (1.055 + 3.7 * gb);
        double stagger = up.Qc - lo.Qc;                       // +x is forward: upper ahead = positive stagger
        double k = Interp(ClMaxByGapOverChord, gap / chord)
                 + Math.Min(MaxStaggerGain, Math.Abs(stagger) / chord * StaggerGainPerChord);

        var cell = new BiplaneCellule
        {
            IsCellule = true,
            GapM = gap, StaggerM = stagger, MeanChordM = chord, SpanM = span,
            DecalageRad = up.Inc - lo.Inc,
            AreaRatio = lo.Area > 1e-9 ? up.Area / lo.Area : 1.0,
            SigmaPrandtl = sigma,
            ClMaxFactor = Math.Clamp(k, 0.5, 1.0),
            WingIds = new[] { up.Id, lo.Id },
        };
        Induction(cfg, cell);
        return cell;
    }

    /// <summary>One spanwise panel of the lattice: a wing SECTION (its fixed strip and the hinged strip behind it are one
    /// section, loaded at the section quarter-chord as AeroModel.LoadPoint has it).</summary>
    // A plain struct, not a record struct: Unity's compiler is C# 9 (see the note on `private set` above).
    private readonly struct Panel
    {
        public readonly double X, Y, Z, Width, Chord, A0, Aileron; public readonly bool Upper;
        public Panel(double x, double y, double z, double width, double chord, double a0, double aileron, bool upper)
        { X = x; Y = y; Z = z; Width = width; Chord = chord; A0 = a0; Aileron = aileron; Upper = upper; }
    }

    /// <summary>
    /// The lattice. Each section is a horseshoe vortex — bound along its quarter-chord across its own width, trailing
    /// legs to infinity aft — and Biot–Savart gives every panel's normal velocity at every other panel's control point:
    /// the classical Weissinger/Falkner lifting surface reduced to one chordwise panel, which is Prandtl's lifting line
    /// with the bound vortices of the OTHER wing kept in (they are what stagger acts through). Flow tangency at the
    /// control points gives the circulations. The control point sits a0·c/4π behind the bound vortex, which makes an
    /// isolated two-dimensional section reproduce its own section slope a0 (2π at the classical three-quarter chord).
    /// a0 is the wing's table slope where that is a 2-D section (>= 5.5/rad) and, where the table is already a
    /// finite-span polar, the section slope that Helmbold's formula maps onto it at the wing's own aspect ratio — so the
    /// lattice does not count the wing's own induction twice. Decalage changes each wing's incidence, not the
    /// perturbation loads, so it does not enter these linear ratios.
    /// </summary>
    private static void Induction(AircraftConfig cfg, BiplaneCellule cell)
    {
        var panels = Panels(cfg, id => cell.Covers(id), id => id.StartsWith(cell.WingIds[0], StringComparison.OrdinalIgnoreCase));
        if (panels.Count < 4) return;
        Func<Panel, double> roll = p => p.Y, aileron = p => Math.Sign(p.Y) * p.Aileron, uniform = p => 1.0;
        double Ratio(Func<Panel, double> f, bool lift)
        {
            double alone = Load(panels, true, false, f, lift) + Load(panels, false, true, f, lift);
            return Math.Abs(alone) > 1e-12 ? Load(panels, true, true, f, lift) / alone : 1.0;
        }
        cell.RollDampingInduction = Ratio(roll, false);
        cell.AileronInduction = Ratio(aileron, false);
        cell.LiftSlopeInduction = Ratio(uniform, true);
        cell.LiftSlopeInductionUpper = Load(panels, true, true, uniform, true, true) / Load(panels, true, false, uniform, true);
        cell.LiftSlopeInductionLower = Load(panels, true, true, uniform, true, false) / Load(panels, false, true, uniform, true);
    }

    /// <summary>
    /// CHECK, not used by the flight model: what lifting-surface theory says about strip theory's roll balance, for ANY
    /// config (monoplane or cellule). Strip theory — what <see cref="AeroModel.Compute"/> flies — gives each section only
    /// its own incidence; the lattice adds every wing's induced field, self and mutual. Returns, for full-span unit loads,
    /// the lattice-over-strip ratio of the aileron moment, of the roll damping, and of the steady pb/2V they balance to
    /// (= the first over the second). Strip theory here uses the same section slopes the lattice does.
    /// </summary>
    public static (double Aileron, double Damping, double SteadyRate) LiftingSurfaceOverStrip(AircraftConfig cfg)
    {
        BiplaneCellule cell = For(cfg);
        var panels = Panels(cfg, id => id.Contains("wing", StringComparison.OrdinalIgnoreCase),
                            id => !cell.IsCellule || id.StartsWith(cell.WingIds[0], StringComparison.OrdinalIgnoreCase));
        if (panels.Count < 4) return (1, 1, 1);
        Func<Panel, double> roll = p => p.Y, aileron = p => Math.Sign(p.Y) * p.Aileron;
        double Strip(Func<Panel, double> f) { double m = 0; foreach (Panel p in panels) m += p.A0 * f(p) * p.Chord * p.Width * p.Y; return m; }
        // The lattice's moment is in Γ per unit V; Γ = ½·c·Cl, so the strip moment ½·a0·α·c per unit V compares like for like.
        double ka = Load(panels, true, true, aileron, false) / (0.5 * Strip(aileron));
        double kp = Load(panels, true, true, roll, false) / (0.5 * Strip(roll));
        return (ka, kp, ka / kp);
    }

    private static List<Panel> Panels(AircraftConfig cfg, Func<string, bool> include, Func<string, bool> isUpper)
    {
        var panels = new List<Panel>();
        int idx = 0;
        for (int si = 0; si < cfg.Surfaces.Count; si++)
        {
            SurfaceConfig sf = cfg.Surfaces[si];
            bool inCellule = include(sf.Id);
            foreach (StripConfig st in sf.Strips)
            {
                int i = idx++;
                if (!inCellule || (st.Control is not null && Math.Abs(st.Control.Gain) >= 0.9) || st.Chord <= 1e-9) continue;
                double width = st.Area / st.Chord;
                // The hinged strip behind this one, if any (as AeroModel pairs them: same system, nearest station).
                double hingedChord = 0;
                string key = AeroModel.SystemKey(sf.Id);
                double bestD = double.MaxValue;
                foreach (SurfaceConfig o in cfg.Surfaces)
                    if (AeroModel.SystemKey(o.Id) == key)
                        foreach (StripConfig h in o.Strips)
                        {
                            if (h.Control is null || Math.Abs(h.Control.Gain) < 0.9 || h.Chord <= 1e-9) continue;
                            double d = Math.Abs(h.Pos[1] - st.Pos[1]);
                            if (d < 0.6 * Math.Max(width, h.Area / h.Chord) && d < bestD) { bestD = d; hingedChord = h.Chord; }
                        }
                double cover = st.Control is not null && st.Control.Surface == "aileron" ? AeroModel.ControlSpanCoverage(cfg, i) : 0.0;
                double a0 = SectionSlopeOf(cfg, sf);
                Vec3 at = AeroModel.LoadPoint(cfg, i, st);
                panels.Add(new Panel(at.X, at.Y, at.Z, width, st.Chord + hingedChord, a0, cover, isUpper(sf.Id)));
            }
        }
        return Tile(panels);
    }

    /// <summary>
    /// The strips' own widths (area/chord) come from a rasterised silhouette and on some configs overlap their neighbours
    /// by ~10 % (the Seminole's sum to 13.1 m on an 11.8 m span), and overlapping horseshoes are not a planform. So each
    /// wing's panels are re-tiled: neighbours meet at a single edge, split in proportion to their own widths, the tips
    /// keep their own outer half-width, and a gap wider than the two half-widths (the fuselage) stays a gap. The chord
    /// becomes area/width, so every panel keeps the area the strip model flies.
    /// </summary>
    private static List<Panel> Tile(List<Panel> panels)
    {
        var outList = new List<Panel>();
        foreach (bool up in new[] { true, false })
        {
            var ps = panels.FindAll(p => p.Upper == up);
            ps.Sort((a, b) => a.Y.CompareTo(b.Y));
            int n = ps.Count;
            var lo = new double[n]; var hi = new double[n];
            for (int i = 0; i < n; i++) { lo[i] = ps[i].Y - 0.5 * ps[i].Width; hi[i] = ps[i].Y + 0.5 * ps[i].Width; }
            for (int i = 0; i + 1 < n; i++)
            {
                double gap = ps[i + 1].Y - ps[i].Y, half = 0.5 * (ps[i].Width + ps[i + 1].Width);
                if (gap > 1.2 * half) continue;   // the fuselage: leave both own edges
                double e = ps[i].Y + gap * ps[i].Width / (ps[i].Width + ps[i + 1].Width);
                hi[i] = e; lo[i + 1] = e;
            }
            for (int i = 0; i < n; i++)
            {
                Panel p = ps[i]; double w = hi[i] - lo[i];
                if (w <= 1e-6) continue;
                outList.Add(new Panel(p.X, 0.5 * (lo[i] + hi[i]), p.Z, w, p.Chord * p.Width / w, p.A0, p.Aileron, p.Upper));
            }
        }
        return outList;
    }

    /// <summary>Solve the lattice on the chosen wings for incidence α(panel) per unit V and return the summed lift (Σ Γ·width)
    /// or rolling moment (Σ Γ·width·y), optionally only that carried by the upper (true) or lower (false) wing.</summary>
    private static double Load(List<Panel> panels, bool useUpper, bool useLower, Func<Panel, double> alpha, bool lift, bool? onlyUpper = null)
    {
        var ps = panels.FindAll(p => p.Upper ? useUpper : useLower);
        int n = ps.Count;
        var a = new double[n, n]; var rhs = new double[n];
        for (int i = 0; i < n; i++)
        {
            Panel pi = ps[i];
            // Lattice frame: x DOWNSTREAM, y right, z UP (config is x forward, z down).
            var cp = new Vec3(-pi.X + pi.A0 * pi.Chord / (4 * Math.PI), pi.Y, -pi.Z);
            for (int j = 0; j < n; j++) a[i, j] = HorseshoeW(cp, ps[j]);
            rhs[i] = -alpha(pi);   // tangency: induced w + V·α = 0, per unit V
        }
        double[] g = Solve(a, rhs);
        double m = 0;
        for (int j = 0; j < n; j++)
        {
            if (onlyUpper.HasValue && ps[j].Upper != onlyUpper.Value) continue;
            m += g[j] * ps[j].Width * (lift ? 1.0 : ps[j].Y);   // ρVΓ per unit span: lift, or rolling moment about x
        }
        return m;
    }

    /// <summary>The 2-D section slope a wing's strips fly: the table's own where it is a 2-D section, otherwise the section
    /// slope that Helmbold's a = 2πA / (2 + √(A²/κ² + 4)), κ = a0/2π, maps onto the table's at the wing's own AR.</summary>
    private static double SectionSlopeOf(AircraftConfig cfg, SurfaceConfig sf)
    {
        if (sf.Strips.Count == 0 || !cfg.AirfoilTables.TryGetValue(sf.Strips[0].Airfoil, out AirfoilTableData? t)) return 2 * Math.PI;
        double a = AeroModel.SectionSlope(t);
        if (a >= 5.5) return a;
        int si = cfg.Surfaces.IndexOf(sf);
        double ar = AeroModel.SystemAspectRatio(cfg, si, false);
        double k = 2 * Math.PI * ar / a - 2, d = k * k - 4;
        if (ar <= 0 || d <= 1e-9) return 2 * Math.PI;
        double kappa = Math.Clamp(ar / Math.Sqrt(d), 0.6, 1.1);
        return 2 * Math.PI * kappa;
    }

    /// <summary>Upward (lattice z) velocity at <paramref name="p"/> per unit circulation of panel <paramref name="h"/>'s horseshoe:
    /// trailing leg in from far downstream to the left end, bound vortex left to right, trailing leg out to far downstream.</summary>
    private static double HorseshoeW(Vec3 p, Panel h)
    {
        double far = 1e4;
        var a = new Vec3(-h.X, h.Y - 0.5 * h.Width, -h.Z);
        var b = new Vec3(-h.X, h.Y + 0.5 * h.Width, -h.Z);
        var aInf = new Vec3(a.X + far, a.Y, a.Z);
        var bInf = new Vec3(b.X + far, b.Y, b.Z);
        return (Segment(p, aInf, a) + Segment(p, a, b) + Segment(p, b, bInf)).Z;
    }

    /// <summary>Biot–Savart, straight segment p1→p2 of unit strength (Katz &amp; Plotkin, Low-Speed Aerodynamics, eq. 10.115).</summary>
    private static Vec3 Segment(Vec3 p, Vec3 p1, Vec3 p2)
    {
        Vec3 r1 = p - p1, r2 = p - p2, r0 = p2 - p1;
        Vec3 c = Vec3.Cross(r1, r2);
        double c2 = c.X * c.X + c.Y * c.Y + c.Z * c.Z;
        double l1 = r1.Length, l2 = r2.Length;
        if (c2 < 1e-12 || l1 < 1e-9 || l2 < 1e-9) return Vec3.Zero;
        double k = (Vec3.Dot(r0, r1) / l1 - Vec3.Dot(r0, r2) / l2) / (4 * Math.PI * c2);
        return c * k;
    }

    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length; var m = (double[,])a.Clone(); var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
            if (piv != c) { for (int k = 0; k < n; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]); (x[c], x[piv]) = (x[piv], x[c]); }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int r = n - 1; r >= 0; r--) { double sum = x[r]; for (int k = r + 1; k < n; k++) sum -= m[r, k] * x[k]; x[r] = sum / m[r, r]; }
        return x;
    }

    /// <summary>Is this surface one of the cellule's wings (so the factor applies to its strips)?</summary>
    public bool Covers(string surfaceId)
    {
        if (!IsCellule || surfaceId is null) return false;
        // An index loop: foreach over the IReadOnlyList allocates an enumerator, and this runs for every surface at every
        // aerodynamic evaluation (on the Quest, the largest source of the sim's garbage in a biplane: QuestFrameBudgetProbe).
        for (int i = 0; i < WingIds.Count; i++)
            if (surfaceId.StartsWith(WingIds[i], StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public override string ToString() => IsCellule
        ? $"gap {GapM:F2} m ({GapOverChord:F2} c), stagger {StaggerM:+0.00;-0.00} m ({StaggerOverChord:+0.00;-0.00} c), "
          + $"span {SpanM:F2} m, Su/Sl {AreaRatio:F2}, decalage {DecalageRad * 57.2958:+0.0;-0.0}°, "
          + $"sigma {SigmaPrandtl:F3}, CLmax x{ClMaxFactor:F3}, induction: roll damping x{RollDampingInduction:F3}, "
          + $"aileron x{AileronInduction:F3}, lift slope x{LiftSlopeInduction:F3} (upper {LiftSlopeInductionUpper:F3}, lower {LiftSlopeInductionLower:F3})"
        : "monoplane";
}

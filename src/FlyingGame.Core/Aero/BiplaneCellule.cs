using System;
using System.Collections.Generic;
using FlyingGame.Core.DataContracts;

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

        return new BiplaneCellule
        {
            IsCellule = true,
            GapM = gap, StaggerM = stagger, MeanChordM = chord, SpanM = span,
            DecalageRad = up.Inc - lo.Inc,
            AreaRatio = lo.Area > 1e-9 ? up.Area / lo.Area : 1.0,
            SigmaPrandtl = sigma,
            ClMaxFactor = Math.Clamp(k, 0.5, 1.0),
            WingIds = new[] { up.Id, lo.Id },
        };
    }

    /// <summary>Is this surface one of the cellule's wings (so the factor applies to its strips)?</summary>
    public bool Covers(string surfaceId)
    {
        if (!IsCellule || surfaceId is null) return false;
        foreach (string w in WingIds)
            if (surfaceId.StartsWith(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public override string ToString() => IsCellule
        ? $"gap {GapM:F2} m ({GapOverChord:F2} c), stagger {StaggerM:+0.00;-0.00} m ({StaggerOverChord:+0.00;-0.00} c), "
          + $"span {SpanM:F2} m, Su/Sl {AreaRatio:F2}, decalage {DecalageRad * 57.2958:+0.0;-0.0}°, "
          + $"sigma {SigmaPrandtl:F3}, CLmax x{ClMaxFactor:F3}"
        : "monoplane";
}

using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// WAKES OVER THE TAIL (owner 2026-11-02: "the pitts still spins too flat. how much is the tail blanketing at very high
/// aoa past 45 degrees for example? it seems like too much"). Every horizontal lifting section — each wing strip with its
/// aileron behind it, each stabiliser strip with its elevator — sheds its own two-dimensional wake, and a tail strip loses
/// dynamic pressure only by being inside one.
///
/// WHERE the wake is. It leaves the section along the local flow (the receiving strip's own relative wind, rotation
/// included, turned by the same downwash the tail flies in). Attached, it leaves the trailing edge. Separated, the two
/// shear layers leave the separation point and the trailing edge and the wake is centred between them (Hoerner,
/// Fluid-Dynamic Drag, 1965, ch. 3; Roshko, NACA TN 3169, 1954, for the normal plate): with separation fraction s the
/// separation point sits s of the chord forward of the trailing edge, so the centre line starts ½·s·c ahead of it and the
/// separated region is at least ½·s·c·|sin α| wide either side of it. Nothing is a single "wing" at the area centroid:
/// a biplane has two wakes, from two places.
///
/// HOW WIDE and HOW DEEP. Silverstein, Katzoff &amp; Bullivant, NACA Report 648 (1939), as used by USAF DATCOM §4.4.1:
///   half-width        ζ_w / c = 0.68 √(c_d (x/c + 0.15))
///   centre-line loss  (Δq/q)₀ = 2.42 √c_d / (x/c + 0.3)
///   profile           Δq/q = (Δq/q)₀ · cos²(π ζ / 2ζ_w),  |ζ| &lt; ζ_w
/// x is distance along the wake past the section's downstream edge and c_d the section's own profile drag at its own
/// angle of attack (the airfoil table: ~0.01 attached, ~1 normal to the flow), so a stalled section's wake is as wide and as
/// deep as its drag says. Report 648 was measured on attached and flapped sections; using it at c_d ≈ 1 is an extrapolation
/// in c_d whose √c_d width scaling is the momentum one. The loss is capped at 1 (dead air in the near wake).
///
/// A strip in more than one wake takes the deepest (the biplane's two root wakes, or the left and right root strips at the
/// fin, would otherwise be counted twice). Wings shed onto every tail strip; the stabiliser sheds onto the fin and rudder —
/// which is the NACA spin-tunnel stab-shielding of the rudder (NACA TR 1329 / NASA TP 1009 wedge) arrived at from the same
/// wake rather than a separate construction. The fin's own wake on a T-tail's stab in sideslip is not modelled.
/// </summary>
public static class TailWake
{
    internal sealed class Source
    {
        public int FixedIdx;                 // flat strip index of the fixed strip (its separation memory and airfoil)
        public bool FromWing;                // true: sheds on every tail strip; false (stabiliser): on vertical strips only
        public int SysId;                    // its lifting system (AeroModel.SystemKey), numbered per config
        public double LeX, TeX, Z, Y, HalfSpan;
        public bool InnermostOfSide;         // its wake reaches in to the centre line (over the fuselage)
        public int[] HingedIdx = System.Array.Empty<int>();   // the aileron / elevator strips behind it (masked with it)
        public string Airfoil = "";
        public double Incidence, CosSweep;
    }

    private sealed class Geometry { public Source[] Sources = System.Array.Empty<Source>(); public bool TwoWings; public int[] SysOfSurface = System.Array.Empty<int>(); }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, Geometry> _geom = new();

    internal static bool IsVerticalId(string id) =>
        id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

    private static Geometry Build(AircraftConfig cfg)
    {
        var flat = new List<(StripConfig st, string sys, bool hinged, bool vertical, bool wing, string table)>();
        var sysIds = new List<string>(); var sysOfSurface = new int[cfg.Surfaces.Count];
        for (int si = 0; si < cfg.Surfaces.Count; si++)
        {
            SurfaceConfig sf = cfg.Surfaces[si];
            string sys = AeroModel.SystemKey(sf.Id);
            bool hingedSurface = !string.Equals(sys, sf.Id, StringComparison.Ordinal);   // "X-aileron" → X, "elevator" → hStab
            string? section = AeroModel.SectionTableKey(cfg, si);                         // the table the strip actually flies
            if (!sysIds.Contains(sys)) sysIds.Add(sys);
            sysOfSurface[si] = sysIds.IndexOf(sys);
            foreach (StripConfig st in sf.Strips)
                flat.Add((st, sys, hingedSurface, IsVerticalId(sf.Id) || sys == "vStab", sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase), section ?? st.Airfoil));
        }
        var sources = new List<Source>();
        for (int i = 0; i < flat.Count; i++)
        {
            var (st, sys, hinged, vertical, wing, tableKey) = flat[i];
            if (hinged || vertical) continue;
            bool isStab = sys == "hStab";
            if (!wing && !isStab) continue;
            Vec3 p = st.PosVec();
            double width = st.Chord > 1e-9 ? st.Area / st.Chord : 0.0;
            var src = new Source
            {
                FixedIdx = i, FromWing = wing, SysId = sysIds.IndexOf(sys),
                LeX = p.X + 0.25 * st.Chord, TeX = p.X - 0.75 * st.Chord, Z = p.Z, Y = p.Y, HalfSpan = 0.5 * width,
                Airfoil = tableKey, Incidence = st.IncidenceRad, CosSweep = st.SweepRad != 0.0 ? Math.Cos(st.SweepRad) : 1.0,
            };
            // The hinged strips at this span station (same lifting system, same side, within the strip's width) are part of
            // this SECTION: the wake leaves the aileron's / elevator's trailing edge, not the fixed strip's.
            var hingedHere = new List<int>();
            for (int j = 0; j < flat.Count; j++)
            {
                var o = flat[j];
                if (!o.hinged || o.sys != sys || Math.Sign(o.st.Pos[1]) != Math.Sign(p.Y)) continue;
                double ow = o.st.Chord > 1e-9 ? o.st.Area / o.st.Chord : 0.0;
                if (Math.Abs(o.st.Pos[1] - p.Y) >= 0.6 * Math.Max(width, ow)) continue;
                src.LeX = Math.Max(src.LeX, o.st.Pos[0] + 0.25 * o.st.Chord);
                src.TeX = Math.Min(src.TeX, o.st.Pos[0] - 0.75 * o.st.Chord);
                hingedHere.Add(j);
            }
            src.HingedIdx = hingedHere.ToArray();
            sources.Add(src);
        }
        // The innermost strip of each side and system: its wake covers the fuselage too, out to the centre line.
        foreach (Source s in sources)
        {
            string sys = flat[s.FixedIdx].sys;
            bool inner = true;
            foreach (Source o in sources)
                if (!ReferenceEquals(o, s) && flat[o.FixedIdx].sys == sys && Math.Sign(o.Y) == Math.Sign(s.Y) && Math.Abs(o.Y) < Math.Abs(s.Y)) { inner = false; break; }
            s.InnermostOfSide = inner;
        }
        int firstWing = -1; bool two = false;
        foreach (Source s in sources) if (s.FromWing) { if (firstWing < 0) firstWing = s.SysId; else if (s.SysId != firstWing) two = true; }
        return new Geometry { Sources = sources.ToArray(), TwoWings = two, SysOfSurface = sysOfSurface };
    }

    /// <summary>The wake state of every section for one aerodynamic evaluation. Reused per thread, no allocation per call.</summary>
    internal sealed class Field
    {
        internal Source[] Src = System.Array.Empty<Source>();
        internal bool[] Live = System.Array.Empty<bool>();
        internal double[] Cd = System.Array.Empty<double>(), Sep = System.Array.Empty<double>();
        internal int Count;
        internal double EpsRad;
        internal bool TwoWings;
        internal int[] SysOfSurface = System.Array.Empty<int>();
    }

    [ThreadStatic] private static Field? _scratch;

    /// <summary>
    /// Sets up the wake of every section for this evaluation: which sections are still on the airframe, each one's local
    /// angle of attack, its separation (the strip's own lagged memory when there is one, else the table's stall angle) and
    /// its profile drag. <paramref name="epsRad"/> is the downwash the tail flies in; the wake follows that flow.
    /// </summary>
    internal static Field Prepare(AircraftConfig config, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 freestream,
                                  Vec3 bodyRates, Vec3 cg, StripFlowState? flowState, bool[]? surfaceMask, bool[]? stripMask, double epsRad)
    {
        Geometry g = _geom.GetValue(config, Build);
        Field f = _scratch ??= new Field();
        int n = g.Sources.Length;
        if (f.Live.Length < n) { f.Live = new bool[n]; f.Cd = new double[n]; f.Sep = new double[n]; }
        f.Src = g.Sources; f.Count = n; f.EpsRad = epsRad; f.TwoWings = g.TwoWings; f.SysOfSurface = g.SysOfSurface;
        int[] surfaceOfStrip = SurfaceOfStrip(config);
        for (int k = 0; k < n; k++)
        {
            Source s = g.Sources[k];
            bool live = StripLive(s.FixedIdx, surfaceOfStrip, surfaceMask, stripMask);
            f.Live[k] = live;
            if (!live || !tables.TryGetValue(s.Airfoil, out AirfoilTable? table)) { f.Live[k] = false; continue; }
            Vec3 at = new Vec3(0.5 * (s.LeX + s.TeX), s.Y, s.Z);
            Vec3 v = freestream + Vec3.Cross(bodyRates, at - cg);
            double alpha = (Math.Atan2(v.Z, v.X) + s.Incidence) * s.CosSweep;
            double a = Math.Abs(alpha), aStall = alpha >= 0 ? table.AlphaClMaxRad : -table.AlphaClMinRad;
            double sep = flowState is not null && s.FixedIdx < flowState.Separation.Length
                ? flowState.Separation[s.FixedIdx]
                : (a > aStall ? 1.0 : 0.0);
            double cd = table.Sample(alpha).Cd;
            // The strip itself flies the attached branch in the band just past the stall until its separation has grown
            // (AeroModel's two-branch stall); its wake carries the same blend of the two drags.
            if (a > aStall && a < aStall + 20.0 * Math.PI / 180.0)
                cd = (1.0 - sep) * table.Sample(Math.Sign(alpha) * aStall).Cd + sep * cd;
            else if (a >= aStall + 20.0 * Math.PI / 180.0) sep = 1.0;   // past the band the strip flies the separated table
            f.Cd[k] = Math.Max(0.0, cd);
            f.Sep[k] = Math.Clamp(sep, 0.0, 1.0);
        }
        return f;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, int[]> _surfOf = new();
    private static int[] SurfaceOfStrip(AircraftConfig cfg) => _surfOf.GetValue(cfg, c =>
    {
        var l = new List<int>();
        for (int i = 0; i < c.Surfaces.Count; i++) foreach (StripConfig _ in c.Surfaces[i].Strips) l.Add(i);
        return l.ToArray();
    });

    private static bool StripLive(int idx, int[] surfaceOf, bool[]? surfaceMask, bool[]? stripMask)
    {
        if (stripMask is not null && idx < stripMask.Length && !stripMask[idx]) return false;
        int si = idx < surfaceOf.Length ? surfaceOf[idx] : -1;
        return surfaceMask is null || si < 0 || si >= surfaceMask.Length || surfaceMask[si];
    }

    /// <summary>
    /// q_local / q_freestream for a tail strip at <paramref name="p"/> (body axes) flying in relative wind
    /// <paramref name="vLocal"/> (aircraft relative to the air, at the strip). 1 = clean air.
    /// </summary>
    internal static double QFactor(Field f, Vec3 p, Vec3 vLocal, bool receiverIsVertical)
        => Q(f, p, vLocal, receiverIsVertical, -1, f.EpsRad);

    /// <summary>
    /// q_local / q_freestream for a WING strip of a biplane (Q007, Ty t918u, 2026-11-06): the other wing's wake, by the same
    /// construction. In attached flow and through the ordinary stall each wing's wake runs aft, clear of the other (the
    /// lower wing's leaves behind the upper; the upper's passes a gap above the lower), so nothing changes there. As |α|
    /// nears 90° the air crosses the cellule from below (upright) or from above (inverted), and the windward wing sits in
    /// front of the other one, a gap of 1.2 chords away: two plates in tandem, the rear one in the front one's dead air
    /// (Hoerner, <i>Fluid-Dynamic Drag</i>, 1965, ch. 8: the downstream plate of a tandem pair keeps a small fraction of its
    /// drag). Upright the lower wing, aft of the CG, shields the upper; inverted the upper, forward, shields the lower:
    /// the only aerodynamic asymmetry of a flat spin that a staggered cellule has. Monoplanes: 1, always. The wing's own
    /// system never shades itself, and no downwash is applied (the receiver is not behind the wing).
    /// </summary>
    internal static double WingQFactor(Field f, Vec3 p, Vec3 vLocal, int surfaceIndex)
        => f.TwoWings && surfaceIndex < f.SysOfSurface.Length ? Q(f, p, vLocal, false, f.SysOfSurface[surfaceIndex], 0.0) : 1.0;

    private static double Q(Field f, Vec3 p, Vec3 vLocal, bool receiverIsVertical, int receiverWingSys, double epsRad)
    {
        double speed = vLocal.Length;
        if (speed < 1e-6 || f.Count == 0) return 1.0;
        // The air moves opposite to the aircraft; the downwash turns it down (+z) by ε in the x–z plane.
        double m = Math.Sqrt(vLocal.X * vLocal.X + vLocal.Z * vLocal.Z);
        double el = Math.Atan2(vLocal.Z, vLocal.X) - epsRad;
        Vec3 d = new Vec3(-m * Math.Cos(el), -vLocal.Y, -m * Math.Sin(el)) / speed;
        // The wake sheet of a section is extruded along the span (ŷ) and convected along d; its normal is n = d × ŷ.
        Vec3 nrm = new Vec3(-d.Z, 0.0, d.X);
        double nLen = nrm.Length;
        if (nLen < 1e-6) return 1.0;            // flow straight along the span: no sheet to be inside
        nrm /= nLen;
        double dxz2 = d.X * d.X + d.Z * d.Z;
        double worst = 0.0;
        for (int k = 0; k < f.Count; k++)
        {
            if (!f.Live[k]) continue;
            Source s = f.Src[k];
            if (receiverWingSys >= 0 ? !s.FromWing || s.SysId == receiverWingSys : !s.FromWing && !receiverIsVertical) continue;
            double cd = f.Cd[k];
            if (cd <= 0.0) continue;
            double sep = f.Sep[k], c = s.LeX - s.TeX;
            // Wake origin: the centre between the shear layers from the separation point and the trailing edge.
            double ox = s.TeX + 0.5 * sep * c;
            Vec3 delta = new Vec3(p.X - ox, p.Y - s.Y, p.Z - s.Z);
            double h = Vec3.Dot(delta, nrm);
            Vec3 inPlane = delta - nrm * h;
            double along = (inPlane.X * d.X + inPlane.Z * d.Z) / dxz2;           // distance along the wake (in |d| units)
            double eta = inPlane.Y - along * d.Y;                                // spanwise station the wake came from
            if (s.InnermostOfSide ? (eta * Math.Sign(s.Y) > s.HalfSpan || eta * Math.Sign(s.Y) < -Math.Abs(s.Y)) : Math.Abs(eta) > s.HalfSpan) continue;
            // The section's downstream edge in this flow, and the distance past it.
            double sLe = (s.LeX - ox) * d.X / dxz2, sTe = (s.TeX - ox) * d.X / dxz2;
            double edge = Math.Max(sLe, sTe);
            double x = along - edge;
            if (x < 0.0)
            {
                // Beside the section itself: only on its leeward side (the side the air leaves to) is it in the wake.
                if (along <= 0.0 || (p.Z - s.Z) * d.Z <= 0.0) continue;
                x = 0.0;
            }
            double xc = x / c;
            double zetaW = Math.Max(0.68 * Math.Sqrt(cd * (xc + 0.15)) * c, 0.5 * sep * c * Math.Abs(d.Z) / Math.Sqrt(dxz2));
            double ah = Math.Abs(h);
            if (ah >= zetaW) continue;
            double loss0 = Math.Min(1.0, 2.42 * Math.Sqrt(cd) / (xc + 0.3));
            double cs = Math.Cos(0.5 * Math.PI * ah / zetaW);
            double loss = loss0 * cs * cs;
            if (loss > worst) worst = loss;
        }
        return 1.0 - worst;
    }

    /// <summary>
    /// Diagnostic and test entry: q_local / q_freestream of every strip of the configuration for one flight condition
    /// (NaN for wing strips, which no wake reaches). Uses exactly the field <see cref="AeroModel.Compute"/> flies in.
    /// </summary>
    public static double[] StripQFactors(AircraftConfig config, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 bodyVelocity,
                                         Vec3 bodyRates, StripFlowState? flowState = null, double epsRad = 0.0)
    {
        Vec3 cg = config.Mass.CgVec();
        Field f = Prepare(config, tables, bodyVelocity, bodyRates, cg, flowState, null, null, epsRad);
        var q = new List<double>();
        foreach (SurfaceConfig sf in config.Surfaces)
        {
            bool wing = sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            bool vertical = IsVerticalId(sf.Id);
            foreach (StripConfig st in sf.Strips)
            {
                if (wing) { q.Add(double.NaN); continue; }
                Vec3 r = st.PosVec() - cg;
                q.Add(QFactor(f, st.PosVec(), bodyVelocity + Vec3.Cross(bodyRates, r), vertical));
            }
        }
        return q.ToArray();
    }

    /// <summary>
    /// Diagnostic and test entry: q_local / q_freestream of every WING strip from the other wing's wake (<see cref="WingQFactor"/>;
    /// NaN for every other strip), for one flight condition.
    /// </summary>
    public static double[] WingStripQFactors(AircraftConfig config, IReadOnlyDictionary<string, AirfoilTable> tables, Vec3 bodyVelocity,
                                             Vec3 bodyRates, StripFlowState? flowState = null)
    {
        Vec3 cg = config.Mass.CgVec();
        Field f = Prepare(config, tables, bodyVelocity, bodyRates, cg, flowState, null, null, 0.0);
        var q = new List<double>();
        for (int si = 0; si < config.Surfaces.Count; si++)
        {
            SurfaceConfig sf = config.Surfaces[si];
            bool wing = sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            foreach (StripConfig st in sf.Strips)
            {
                if (!wing) { q.Add(double.NaN); continue; }
                Vec3 r = st.PosVec() - cg;
                q.Add(WingQFactor(f, st.PosVec(), bodyVelocity + Vec3.Cross(bodyRates, r), si));
            }
        }
        return q.ToArray();
    }
}

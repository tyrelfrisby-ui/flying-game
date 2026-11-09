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

    private sealed class Geometry
    {
        public Source[] Sources = System.Array.Empty<Source>();
        public bool TwoWings;
        public int[] SysOfSurface = System.Array.Empty<int>();
        public Panel[] Panels = System.Array.Empty<Panel>();
        public BodyStation[] Body = System.Array.Empty<BodyStation>();
    }

    /// <summary>
    /// A FUSELAGE STATION as a shedder (owner 2026-11-08: "include the shadow effects of the fuselage on the tail and
    /// wing's too"). The body is the biggest bluff shape on the aeroplane and in a spin it is the one lying across the
    /// flow: in an upright flat spin the air comes from below, so the fin — which stands above the body — is directly
    /// downstream of the fuselage and blanketed, while inverted the same fin is the first thing the air meets and the
    /// rudder works. That is the NACA spin-tunnel shielding of the rudder (TR 1329 / NASA TP 1009) arriving out of the
    /// geometry instead of out of a wedge diagram, and it is an asymmetry no symmetric coefficient can produce.
    ///
    /// The section is the ellipse the crossflow model already implies: half-width PlanArea/(2·LengthM) and half-height
    /// SideArea/(2·LengthM), on the body axis (z = 0, where the crossflow stations act), in the same five stations the
    /// crossflow force uses. The march simply asks whether the air path passed THROUGH that volume on its way here.
    /// </summary>
    internal sealed class BodyStation
    {
        public double X, HalfLenX, HalfY, HalfZ;
    }

    /// <summary>
    /// A SHADOW PANEL: one spanwise band of a lifting system, built once per configuration. The march tests panels, not
    /// strips — a Pitts has 24 wing sources and the shadow is a coarse geometric effect, so testing every one of them
    /// per step cost 8x the whole sim's budget. Each band keeps the extreme leading and trailing edge of its own
    /// members, so SWEEP survives at band resolution (the Pitts' upper wing moves 0.38 m in x across its span, which is
    /// four bands' worth), and carries its members so the shedder's live separation is averaged at runtime.
    /// </summary>
    internal sealed class Panel
    {
        public int SysId;
        public double LeX, TeX, Z, Y, HalfSpan;
        public bool InnermostOfSide;
        public int[] Members = System.Array.Empty<int>();
        public double Chord => LeX - TeX;
    }

    private const int ShadowBandsPerSide = 3;

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
        return new Geometry
        {
            Sources = sources.ToArray(), TwoWings = two, SysOfSurface = sysOfSurface,
            Panels = BuildPanels(sources),
            Body = BuildBody(cfg),
        };
    }

    /// <summary>The fuselage's five crossflow stations as shed volumes (see <see cref="BodyStation"/>). Empty for a type
    /// with no distributed crossflow, which leaves the body out of the shadow entirely.</summary>
    private static BodyStation[] BuildBody(AircraftConfig cfg)
    {
        CrossflowConfig? cf = cfg.Fuselage?.Crossflow;
        if (cf is null || cf.LengthM <= 0.1 || cf.PlanArea <= 0.0 || cf.SideArea <= 0.0) return System.Array.Empty<BodyStation>();
        const int n = 5;
        double halfY = 0.5 * cf.PlanArea / cf.LengthM, halfZ = 0.5 * cf.SideArea / cf.LengthM;
        double centre = 0.5 * (cf.PlanCenterX + cf.SideCenterX);
        var body = new BodyStation[n];
        for (int i = 0; i < n; i++)
        {
            double frac = (i - (n - 1) * 0.5) / (n - 1);     // -0.5 .. +0.5, the crossflow model's own stations
            body[i] = new BodyStation
            {
                X = centre + frac * cf.LengthM,
                HalfLenX = 0.5 * cf.LengthM / n,
                HalfY = halfY, HalfZ = halfZ,
            };
        }
        return body;
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
        internal double WingWakeK = 1.0;      // SpinAeroConfig.CelluleWakeK: depth of the wing-on-wing shading
        internal double ShadowK, ShadowDecayChords;        // SpinAeroConfig.CelluleShadowK / ...DecayChords
        internal Vec3 Freestream, Rates, Cg;               // the velocity field the helix is traced through
        internal double[] SysMeanZ = System.Array.Empty<double>();
        internal int SysCount;
        // Shedding sources grouped by lifting system, with that system's height band, so one bound test per system per
        // step replaces a loop over every source. In attached flow no source sheds at all and ShadAny is false, which is
        // what keeps this off the cost of ordinary flight entirely.
        internal Panel[] Panels = System.Array.Empty<Panel>();
        internal double[] PanelSep = System.Array.Empty<double>();     // this evaluation's mean separation per panel
        internal int[] ShadFirst = System.Array.Empty<int>(), ShadCount = System.Array.Empty<int>();
        internal int[] ShadIdx = System.Array.Empty<int>();            // panel indices, grouped by system
        internal double[] ShadZMin = System.Array.Empty<double>(), ShadZMax = System.Array.Empty<double>(), ShadHalfMax = System.Array.Empty<double>();
        internal bool ShadAny;
        // The fuselage as a shedder: its stations, each one's crossflow ratio this evaluation (which IS its separated
        // share: a body sheds in proportion to how much of the flow is across it), and the band they occupy.
        internal BodyStation[] Body = System.Array.Empty<BodyStation>();
        internal double[] BodySep = System.Array.Empty<double>();
        internal bool BodyAny;
        internal double BodyXMin, BodyXMax, BodyReachY, BodyReachZ;
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
        f.WingWakeK = config.SpinAero?.CelluleWakeK ?? 1.0;
        f.ShadowK = config.SpinAero?.ShadowK ?? 0.0;
        f.ShadowDecayChords = config.SpinAero?.ShadowDecayChords ?? 4.0;
        f.Freestream = freestream; f.Rates = bodyRates; f.Cg = cg;
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
        // Each lifting system's mean height, which is the plane the helix is traced to. A wing's own dihedral and sweep
        // move its stations a few cm in Z (the Pitts' upper wing spans -0.62..-0.60), far less than the gap, so one plane
        // per system is enough; the station's own LeX/TeX - which is where the SWEEP lives - is honoured at the hit.
        f.SysCount = 0;
        for (int i = 0; i < g.SysOfSurface.Length; i++) if (g.SysOfSurface[i] + 1 > f.SysCount) f.SysCount = g.SysOfSurface[i] + 1;
        if (f.SysMeanZ.Length < f.SysCount) f.SysMeanZ = new double[f.SysCount];
        for (int sys = 0; sys < f.SysCount; sys++)
        {
            double sum = 0.0; int cnt = 0;
            for (int k = 0; k < n; k++) if (f.Live[k] && g.Sources[k].FromWing && g.Sources[k].SysId == sys) { sum += g.Sources[k].Z; cnt++; }
            f.SysMeanZ[sys] = cnt > 0 ? sum / cnt : double.NaN;
        }
        f.Panels = g.Panels;
        f.Body = g.Body;
        if (f.ShadowK > 0.0) { PrepareShadowGroups(f); PrepareBody(f); }
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
    internal static double QFactor(Field f, Vec3 p, Vec3 vLocal, bool receiverIsVertical, int surfaceIndex = -1, double chord = 0.0)
        => f.ShadowK > 0.0
            ? GeometricShadow(f, p, chord, surfaceIndex >= 0 && surfaceIndex < f.SysOfSurface.Length ? f.SysOfSurface[surfaceIndex] : -1)
            : Q(f, p, vLocal, receiverIsVertical, -1, f.EpsRad);

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
    internal static double WingQFactor(Field f, Vec3 p, Vec3 vLocal, int surfaceIndex, double chord = 0.0)
    {
        if (surfaceIndex >= f.SysOfSurface.Length) return 1.0;
        if (f.ShadowK > 0.0) return GeometricShadow(f, p, chord, f.SysOfSurface[surfaceIndex]);
        if (!f.TwoWings) return 1.0;
        double q = Q(f, p, vLocal, false, f.SysOfSurface[surfaceIndex], 0.0);
        // SpinAeroConfig.CelluleWakeK scales the LOSS, not the retained share, so 1 is exactly the computed wake and 0
        // is no shading at all. Only the wing-on-wing path: the tail's wake (QFactor) is untouched.
        return f.WingWakeK == 1.0 ? q : 1.0 - (1.0 - q) * System.Math.Clamp(f.WingWakeK, 0.0, 1.0);
    }

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
        for (int si = 0; si < config.Surfaces.Count; si++)
        {
            SurfaceConfig sf = config.Surfaces[si];
            bool wing = sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            bool vertical = IsVerticalId(sf.Id);
            foreach (StripConfig st in sf.Strips)
            {
                if (wing) { q.Add(double.NaN); continue; }
                Vec3 r = st.PosVec() - cg;
                q.Add(QFactor(f, st.PosVec(), bodyVelocity + Vec3.Cross(bodyRates, r), vertical, si, st.Chord));
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
                q.Add(WingQFactor(f, st.PosVec(), bodyVelocity + Vec3.Cross(bodyRates, r), si, st.Chord));
            }
        }
        return q.ToArray();
    }

    // ======================================================================================================
    // GEOMETRIC HELICAL SHADOW (Ty, 2026-11-08). "Model the shadowing from our own geometry on each wing section
    // rather than the naca data so we can model the helical flow exactly and use reduced Q from a k factor in the
    // shadow that we can tweak for fine tuning... it will break us out of this naca shadow trap and put the controls
    // back in our hands based on solid physics." Then: "Use the same dynamic pressure shadow based on actual geometry
    // for all surfaces not just the wing. This needs to include the tails too."
    //
    // WHAT IT DOES. For each receiving section, three points along its chord, it marches the AIR'S OWN PATH upstream
    // and asks what that air came through. In the body frame an air particle drifts at -(V_cg + omega x r), so going
    // upstream is integrating r' = +(V_cg + omega x r) - a helix whenever the aircraft is rotating, which in a spin it
    // is. That is the "helical inflow" exactly, not a straight-line approximation of it: at 300 deg/s the flow turns
    // about 8 deg crossing a Pitts cellule, and it is that drift which decides whether a 0.48 m stagger offset puts the
    // downstream wing in shadow or clear of it. The old path could not answer that: its half-width (~0.45 m) sat within
    // 3 cm of the offset, so the answer came from how the chord extents happened to be built.
    //
    // HOW DEEP. The wake's half-thickness is the shedder's OWN PROJECTED GEOMETRY normal to the local flow - a plate of
    // chord c at flow angle theta blocks c*|sin theta|, which for a chordwise section is c*|d_z|/|d_xz| - scaled by how
    // bluff that section actually is (its separation), with a thin attached sheet underneath it and spreading with the
    // distance travelled. No empirical wake correlation anywhere in it. ShadowK is then the one tunable: the share of
    // dynamic pressure a fully shadowed strip loses.
    //
    // SWEEP is honoured because the shed patch tested is the station's OWN LeX/TeX at the span station the helix
    // actually lands on (Ty: "Make sure the actual sweep of the top wing is accounted for too" - the Pitts' upper wing
    // moves 0.38 m in x across its span). Dihedral is not: one mean height per lifting system, which on this fleet is a
    // couple of centimetres against gaps of a metre.
    //
    // A section never shadows its own lifting system (a wing does not shade itself, nor the stabiliser the elevator
    // behind it - they are one section). Everything else falls out of the geometry: wings shade tails, the stabiliser
    // shades the fin, the windward wing of a cellule shades the leeward one, and nothing aft ever shades anything
    // forward, because the trace only ever runs upstream.
    // ======================================================================================================

    // A wake three chords downstream of the section that shed it has largely refilled, and on this fleet three metres
    // is three to four chords, so the march stops there: it is the single biggest term in the cost of this model.
    private const double ShadowMaxPathM = 3.0;
    private const int ShadowMaxSteps = 8;
    private const double ShadowStepM = 0.45;         // march step, metres of path
    private const double ShadowThinSheet = 0.02;     // attached wake half-thickness, fraction of the shedder's chord
    private const double ShadowSpread = 0.08;        // half-thickness added per chord of path travelled

    /// <summary>q_local / q_freestream for any strip, from the geometric helical shadow. 1 = clean air.</summary>
    private static double GeometricShadow(Field f, Vec3 p, double chord, int receiverSys)
    {
        if (f.ShadowK <= 0.0) return 1.0;
        double c = chord > 1e-6 ? chord : 0.5;
        double le = p.X + 0.25 * c;
        // ONE march, from the receiver's own load point, with its half-chord handed to the patch test as extra
        // tolerance. Marching each end of the chord separately cost twice as much and said almost the same thing,
        // because the two paths start half a chord apart and run side by side: the edge softening already expresses
        // "half of this section is in shadow", which is what the loss has to carry.
        return 1.0 - Math.Clamp(TraceShadow(f, p, receiverSys, 0.5 * c), 0.0, 1.0);
    }

    /// <summary>Averages each panel's live separation and groups the shedding panels by system, with the system's
    /// height band, so one bound test per system per step replaces a loop over every panel.</summary>
    private static void PrepareShadowGroups(Field f)
    {
        int pn = f.Panels.Length, sysN = f.SysCount;
        if (f.PanelSep.Length < pn) f.PanelSep = new double[pn];
        if (f.ShadFirst.Length < sysN) { f.ShadFirst = new int[sysN]; f.ShadCount = new int[sysN]; f.ShadZMin = new double[sysN]; f.ShadZMax = new double[sysN]; f.ShadHalfMax = new double[sysN]; }
        if (f.ShadIdx.Length < pn) f.ShadIdx = new int[pn];
        f.ShadAny = false;
        for (int i = 0; i < pn; i++)
        {
            double sum = 0.0; int cnt = 0;
            foreach (int k in f.Panels[i].Members) if (k >= 0 && k < f.Count && f.Live[k]) { sum += f.Sep[k]; cnt++; }
            f.PanelSep[i] = cnt > 0 ? sum / cnt : 0.0;
        }
        int w = 0;
        for (int sys = 0; sys < sysN; sys++)
        {
            f.ShadFirst[sys] = w;
            double zmin = double.MaxValue, zmax = double.MinValue, halfMax = 0.0;
            for (int i = 0; i < pn; i++)
            {
                Panel pa = f.Panels[i];
                if (pa.SysId != sys || f.PanelSep[i] <= 0.0 || pa.Chord <= 1e-6) continue;
                f.ShadIdx[w++] = i;
                if (pa.Z < zmin) zmin = pa.Z;
                if (pa.Z > zmax) zmax = pa.Z;
                double h = pa.Chord * (ShadowThinSheet + 0.5 * f.PanelSep[i]);
                if (h > halfMax) halfMax = h;
            }
            f.ShadCount[sys] = w - f.ShadFirst[sys];
            f.ShadZMin[sys] = zmin; f.ShadZMax[sys] = zmax; f.ShadHalfMax[sys] = halfMax;
            if (f.ShadCount[sys] > 0) f.ShadAny = true;
        }
    }

    /// <summary>Each fuselage station's shed strength for this evaluation: the share of the flow that is ACROSS the body
    /// there, |v_cross| / |v| at that station with the rotation included. 0 in cruise, which is what keeps a type's
    /// cruise, rig and trim where they were; 1 broadside, which is the flat spin. A body in crossflow separates at once
    /// (Hoerner, Fluid-Dynamic Drag ch. 3), so there is no stall angle in this: the wake grows with the crossflow.</summary>
    private static void PrepareBody(Field f)
    {
        int n = f.Body.Length;
        f.BodyAny = false;
        if (n == 0) return;
        if (f.BodySep.Length < n) f.BodySep = new double[n];
        double xmin = double.MaxValue, xmax = double.MinValue, ry = 0.0, rz = 0.0;
        for (int i = 0; i < n; i++)
        {
            BodyStation b = f.Body[i];
            Vec3 v = f.Freestream + Vec3.Cross(f.Rates, new Vec3(b.X, 0.0, 0.0) - f.Cg);
            double sp = v.Length;
            double sep = sp > 1e-6 ? Math.Clamp(Math.Sqrt(v.Y * v.Y + v.Z * v.Z) / sp, 0.0, 1.0) : 0.0;
            f.BodySep[i] = sep;
            if (sep <= 1e-6) continue;
            f.BodyAny = true;
            xmin = Math.Min(xmin, b.X - b.HalfLenX); xmax = Math.Max(xmax, b.X + b.HalfLenX);
            ry = Math.Max(ry, b.HalfY); rz = Math.Max(rz, b.HalfZ);
        }
        f.BodyXMin = xmin; f.BodyXMax = xmax; f.BodyReachY = ry; f.BodyReachZ = rz;
    }

    /// <summary>The deepest shadow this segment takes from the fuselage: did the air path pass through the body's own
    /// volume? The offset is measured in the section's ellipse, grown with the path like every other wake here.</summary>
    private static double BodyShadow(Field f, Vec3 r0, Vec3 r1, double path, double worst)
    {
        double grow = ShadowSpread * path;
        if (Math.Min(r0.X, r1.X) > f.BodyXMax + grow || Math.Max(r0.X, r1.X) < f.BodyXMin - grow) return worst;
        // A conservative lower bound on how close the segment gets to the axis: zero if it crosses the plane, so a
        // genuine hit is never skipped.
        double nearY = r0.Y * r1.Y <= 0.0 ? 0.0 : Math.Min(Math.Abs(r0.Y), Math.Abs(r1.Y));
        double nearZ = r0.Z * r1.Z <= 0.0 ? 0.0 : Math.Min(Math.Abs(r0.Z), Math.Abs(r1.Z));
        if (nearY > f.BodyReachY + grow || nearZ > f.BodyReachZ + grow) return worst;
        Vec3 d = r1 - r0;
        for (int i = 0; i < f.Body.Length; i++)
        {
            double sep = f.BodySep[i];
            if (sep <= 1e-6) continue;
            BodyStation b = f.Body[i];
            double ax = b.HalfLenX + grow, ay = b.HalfY + grow, az = b.HalfZ + grow;
            if (ay <= 1e-9 || az <= 1e-9) continue;
            // The part of the segment alongside this station.
            double t0 = 0.0, t1 = 1.0;
            if (Math.Abs(d.X) > 1e-12)
            {
                double ta = (b.X - ax - r0.X) / d.X, tb = (b.X + ax - r0.X) / d.X;
                t0 = Math.Max(0.0, Math.Min(ta, tb)); t1 = Math.Min(1.0, Math.Max(ta, tb));
                if (t1 < t0) continue;
            }
            else if (Math.Abs(r0.X - b.X) > ax) continue;
            // Closest approach to the axis in the scaled section, over that part.
            double py0 = r0.Y / ay, pz0 = r0.Z / az, dy = d.Y / ay, dz = d.Z / az;
            double den = dy * dy + dz * dz;
            double t = den > 1e-18 ? Math.Clamp(-(py0 * dy + pz0 * dz) / den, t0, t1) : t0;
            double qy = py0 + dy * t, qz = pz0 + dz * t;
            double dist = Math.Sqrt(qy * qy + qz * qz);
            if (dist >= 1.0) continue;
            double profile = Math.Cos(0.5 * Math.PI * dist);
            double scale = 2.0 * Math.Max(b.HalfY, b.HalfZ);
            double decay = f.ShadowDecayChords > 0.0 && scale > 1e-6
                ? Math.Clamp(1.0 - path / (f.ShadowDecayChords * scale), 0.0, 1.0) : 1.0;
            double depth = f.ShadowK * sep * profile * profile * decay;
            if (depth > worst) worst = depth;
        }
        return worst;
    }

    /// <summary>Marches one air path upstream and returns the deepest shadow it came through (0 = clean).</summary>
    private static double TraceShadow(Field f, Vec3 start, int receiverSys, double receiverHalfChord = 0.0)
    {
        if (!f.ShadAny && !f.BodyAny) return 0.0;
        Vec3 r = start;
        double path = 0.0, worst = 0.0;
        double om = f.Rates.Length;
        for (int step = 0; step < ShadowMaxSteps && path < ShadowMaxPathM; step++)
        {
            Vec3 v = f.Freestream + Vec3.Cross(f.Rates, r - f.Cg);
            double speed = v.Length;
            if (speed < 1e-6) break;
            // Short enough that the helix barely turns, and that a surface cannot be stepped over.
            double dt = Math.Min(ShadowStepM / speed, om > 1e-6 ? 0.06 / om : double.MaxValue);
            Vec3 mid = r + v * (0.5 * dt);
            Vec3 vm = f.Freestream + Vec3.Cross(f.Rates, mid - f.Cg);
            double vmLen = vm.Length;
            if (vmLen < 1e-9) break;
            Vec3 next = r + vm * dt;
            Vec3 dHat = vm / vmLen;
            double dxz = Math.Sqrt(dHat.X * dHat.X + dHat.Z * dHat.Z);
            double project = dxz > 1e-6 ? Math.Abs(dHat.Z) / dxz : 1.0;   // chord's projection normal to the flow
            double segLoZ = Math.Min(r.Z, next.Z), segHiZ = Math.Max(r.Z, next.Z);
            for (int sys = 0; sys < f.SysCount; sys++)
            {
                int cnt = f.ShadCount[sys];
                if (cnt == 0 || sys == receiverSys) continue;
                double reach = f.ShadHalfMax[sys] + ShadowSpread * path;
                if (segLoZ - reach > f.ShadZMax[sys] || segHiZ + reach < f.ShadZMin[sys]) continue;   // whole system out of range
                int first = f.ShadFirst[sys];
                for (int j = 0; j < cnt; j++)
                {
                    int i = f.ShadIdx[first + j];
                    Panel pa = f.Panels[i];
                    double sep = f.PanelSep[i], sc = pa.Chord;
                    double half = sc * (ShadowThinSheet + 0.5 * sep * project) + ShadowSpread * path;
                    if (half <= 1e-9) continue;
                    double d = SegmentToPatch(r, next, pa, half, receiverHalfChord);
                    if (double.IsNaN(d) || d >= half) continue;
                    double profile = Math.Cos(0.5 * Math.PI * d / half);
                    double decay = f.ShadowDecayChords > 0.0
                        ? Math.Clamp(1.0 - path / (f.ShadowDecayChords * sc), 0.0, 1.0) : 1.0;
                    double depth = f.ShadowK * sep * profile * profile * decay;
                    if (depth > worst) worst = depth;
                }
            }
            if (f.BodyAny) worst = BodyShadow(f, r, next, path, worst);
            path += (next - r).Length;
            r = next;
            if (worst >= f.ShadowK - 1e-6) break;        // already as deep as it can get
        }
        return worst;
    }

    /// <summary>
    /// Distance from the segment r0..r1 to a shed patch: the plane of the shedding system at z = s.Z, bounded by this
    /// station's own leading and trailing edge (so a swept wing is tested where it actually is) and its span window,
    /// the innermost station reaching in over the fuselage. NaN = the segment is not over the patch at all.
    /// </summary>
    private static double SegmentToPatch(Vec3 r0, Vec3 r1, Panel s, double half, double receiverHalfChord)
    {
        double z0 = r0.Z - s.Z, z1 = r1.Z - s.Z;
        double t = z0 == z1 ? 0.0 : Math.Clamp(-z0 / (z1 - z0), 0.0, 1.0);   // closest approach in height
        if (z0 * z1 <= 0.0 && z0 != z1) t = -z0 / (z1 - z0);                 // it crosses: distance 0 there
        Vec3 at = r0 + (r1 - r0) * t;
        double dz = Math.Abs(at.Z - s.Z);
        if (dz >= half) return double.NaN;
        double sc = s.Chord, soft = 0.15 * sc + receiverHalfChord;
        if (at.X > s.LeX + soft || at.X < s.TeX - soft) return double.NaN;
        double sgn = s.Y >= 0.0 ? 1.0 : -1.0, span = (at.Y - s.Y) * sgn;
        double lo = s.InnermostOfSide ? -Math.Abs(s.Y) : -s.HalfSpan;
        double softY = 0.3 * Math.Max(s.HalfSpan, 1e-3);
        if (span > s.HalfSpan + softY || span < lo - softY) return double.NaN;
        // Edge softening counts as extra distance rather than a hard cut, so a strip half out of the shadow loses half.
        double outX = at.X > s.LeX ? at.X - s.LeX : at.X < s.TeX ? s.TeX - at.X : 0.0;
        double outY = span > s.HalfSpan ? span - s.HalfSpan : span < lo ? lo - span : 0.0;
        double edge = (outX / Math.Max(soft, 1e-9) + outY / Math.Max(softY, 1e-9)) * half;
        return dz + edge;
    }

    /// <summary>Groups the shed sources into a few spanwise bands per system and side (see <see cref="Panel"/>).</summary>
    private static Panel[] BuildPanels(List<Source> sources)
    {
        var panels = new List<Panel>();
        var systems = new List<int>();
        foreach (Source s in sources) if (!systems.Contains(s.SysId)) systems.Add(s.SysId);
        foreach (int sys in systems)
            foreach (int side in new[] { -1, 1 })
            {
                var mine = sources.Where(s => s.SysId == sys && (s.Y >= 0 ? 1 : -1) == side).OrderBy(s => Math.Abs(s.Y)).ToList();
                if (mine.Count == 0) continue;
                int bands = Math.Min(ShadowBandsPerSide, mine.Count);
                for (int b = 0; b < bands; b++)
                {
                    int lo = b * mine.Count / bands, hi = (b + 1) * mine.Count / bands;
                    if (hi <= lo) continue;
                    var band = mine.GetRange(lo, hi - lo);
                    double yMin = band.Min(x => x.Y - x.HalfSpan), yMax = band.Max(x => x.Y + x.HalfSpan);
                    panels.Add(new Panel
                    {
                        SysId = sys,
                        LeX = band.Max(x => x.LeX), TeX = band.Min(x => x.TeX),
                        Z = band.Average(x => x.Z),
                        Y = 0.5 * (yMin + yMax), HalfSpan = 0.5 * (yMax - yMin),
                        InnermostOfSide = band.Any(x => x.InnermostOfSide),
                        Members = band.Select(x => sources.IndexOf(x)).ToArray(),
                    });
                }
            }
        return panels.ToArray();
    }
}

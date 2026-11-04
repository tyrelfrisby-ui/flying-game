using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Sim;

/// <summary>
/// The airframe against the trees (Ty 2026-11-03: "i flew into the tree tops at high speed and it just broke apart and
/// stopped … if a wing hits just that wing breaks off and everything should retain its momentum"). See
/// <see cref="TreeStructure"/> for the tree; this is the airframe's half.
///
/// The airframe meets a stem along its EDGES (<see cref="StrikeEdge"/>: each wing's leading edge inboard and outboard of
/// its structural station, the three fuselage sections, the tailplane and fin leading edges), swept over the step so a
/// 5 cm stem cannot slip between two samples at 60 m/s. Where an edge comes within the stem's radius plus its own
/// half-thickness, one of three things happens, decided by forces (never by "it was a crash"):
///
///  1. The demand is small (a slow taxi into a trunk): the force that would stop that point, v_n·√(k·m_eff), is less than
///     both the stem's and the part's strength — the point is stopped, an inelastic impulse m_eff·v_n along the normal.
///  2. The STEM is the weaker (a treetop, a branch, a sapling): it snaps where it was struck. The airframe pays the momentum
///     of the top it throws (m_top/4 · v_n) and the work of cutting through it (F_s·d / v_n) — never more than stopping
///     the point would cost. A panel loaded past its limit by the snapping force takes the 5° set; nothing comes off.
///  3. The PART is the weaker: it comes off — through <see cref="Aircraft.LoseComponent"/> like any break, so the piece
///     leaves with the rigid velocity field at that instant, v + ω × r, and the mass, CG and inertia it took (momentum is
///     conserved at separation). THEN the trunk stops the piece at the point it struck (<see cref="WreckPiece.StopAt"/>),
///     and the rest of the airframe is charged only what the joint carried while it failed: F_c over the time the edge
///     takes to crush <see cref="FailureTravelM"/>, F_c·δ / v_n. The aeroplane flies on without that part.
///
/// The part's strength: a wing edge is a cantilever about its station (the panel's, or the root's for the inner wing), and
/// a leading-edge blow loads it mostly IN PLANE, where the wing's two spars a half chord apart are a far deeper beam than
/// either spar alone: the in-plane ultimate is the vertical one × (0.5 / t·c⁻¹) — 4 for a 12 % section. The two combine as
/// (M_v/M_v,ult)² + (M_i/M_i,ult)² = 1. A fuselage or tail section crushes: F_c = ½·m_eff·v_break²/δ_crush, with
/// v_break the closing speed that breaks it on the ground (AirframeContact) and δ_crush = <see cref="CrushTravelM"/>:
/// the same energy the ground rule already says it absorbs.
///
/// Inside a crown the branches push back along the relative wind, F = ρ_c·A·v² on each third of each edge (A = a third
/// of its length × <see cref="CrownSweepHeightM"/>).
/// </summary>
public sealed partial class Aircraft
{
    /// <summary>The trees this aeroplane can hit: null = the forest of <see cref="WorldTerrain.Active"/>.</summary>
    public ITreeSource? Trees { get; set; }
    /// <summary>The strike edges (built on first use).</summary>
    public IReadOnlyList<StrikeEdge> StrikeEdges => _edges ??= BuildStrikeEdges();
    /// <summary>Tree strikes so far (tests, probe): edge, tree, outcome, closing speed, impulse on the airframe.</summary>
    public readonly List<TreeStrikeRecord> TreeStrikeLog = new();

    public const double FailureTravelM = 0.05, CrushTravelM = 0.15, CrownSweepHeightM = 0.3;
    private const int SweepSamples = 8;

    private List<StrikeEdge>? _edges;
    private readonly List<Forest.Tree> _treesNear = new();
    private readonly Dictionary<AirframeComponent, (Vec3 at, Vec3 n)> _separationStops = new();

    /// <summary>For CrashRelay: where the thing that broke <paramref name="c"/> off stops the piece (world point and the
    /// normal it pushes along), once.</summary>
    public bool TakeSeparationStop(AirframeComponent c, out Vec3 atWorld, out Vec3 normalWorld)
    {
        if (_separationStops.Remove(c, out var s)) { atWorld = s.at; normalWorld = s.n; return true; }
        atWorld = normalWorld = Vec3.Zero; return false;
    }

    private List<StrikeEdge> BuildStrikeEdges()
    {
        var edges = new List<StrikeEdge>();
        AircraftConfig c = Config;
        double g = Atmosphere.GravityMs2, w = c.Mass.MassKg;
        double gMax = c.Limits.GMax > 0 ? c.Limits.GMax : 3.8, uf = c.Limits.UltimateFactor > 0 ? c.Limits.UltimateFactor : 1.5;
        double strength = c.ImpactStrength > 0 ? c.ImpactStrength : 1.0;
        double wingArea = 0;
        foreach (SurfaceConfig sf in c.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (StripConfig st in sf.Strips) wingArea += st.Area;
        double semi = WingPanels.Semispan(c);

        // Wings: the leading edge, root to station (the inner wing) and station to tip (the panel).
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (!WingStructure.IsWingBase(sf.Id) || sf.Strips.Count == 0) continue;
            double planeArea = 0;
            foreach (SurfaceConfig o in c.Surfaces) if (WingStructure.BelongsTo(o.Id, sf.Id, c)) foreach (StripConfig st in o.Strips) planeArea += st.Area;
            foreach (int side in new[] { -1, 1 })
            {
                var strips = sf.Strips.Where(st => Math.Sign(st.Pos[1]) == side).OrderBy(st => Math.Abs(st.Pos[1])).ToList();
                if (strips.Count == 0) continue;
                StripConfig root = strips[0], tip = strips[^1];
                double rootY = Math.Max(0.0, Math.Abs(root.Pos[1]) - 0.5 * WingStructure.Width(root)), tipY = Math.Abs(tip.Pos[1]) + 0.5 * WingStructure.Width(tip);
                Vec3 Le(StripConfig st, double y) => new(st.Pos[0] + 0.25 * st.Chord, side * y, WingStructure.ChordZ(sf, st, y));
                Vec3 a = Le(root, rootY), b = Le(tip, tipY);
                double tcRoot = AirframeHydroModel.ThicknessRatio(root.Airfoil), tcTip = AirframeHydroModel.ThicknessRatio(tip.Airfoil);
                double ultRoot = gMax * uf * strength * g * 0.5 * w * (wingArea > 0 ? planeArea / wingArea : 1.0) * AirframeHydroModel.EllipticArm(rootY, semi);
                AirframeComponent wingComp = side < 0 ? AirframeComponent.WingLeft : AirframeComponent.WingRight;
                int k = _panels.FindIndex(p => p.SurfaceId == sf.Id && p.Side == side);
                Vec3 station = b;
                if (k >= 0 && tipY > rootY)
                {
                    WingPanel p = _panels[k];
                    station = a + (b - a) * Math.Clamp((p.BreakY - rootY) / (tipY - rootY), 0.0, 1.0);
                    StripConfig atStation = strips.OrderBy(st => Math.Abs(Math.Abs(st.Pos[1]) - p.BreakY)).First();
                    edges.Add(MapThirds(new StrikeEdge
                    {
                        A = station, B = b, Component = p.Component, Panel = k, Side = side, Name = $"{sf.Id}-panel-{(side < 0 ? "L" : "R")}",
                        RadiusM = 0.5 * tcTip * tip.Chord, StationY = p.BreakY, UltimateNm = p.UltimateNm,
                        InPlaneUltimateNm = p.UltimateNm * 0.5 / AirframeHydroModel.ThicknessRatio(atStation.Airfoil),
                        GoneWith = new[] { wingComp },
                    }, sf.Id, 0));
                }
                edges.Add(MapThirds(new StrikeEdge
                {
                    A = a, B = station, Component = wingComp, Side = side, Name = $"{sf.Id}-inner-{(side < 0 ? "L" : "R")}",
                    RadiusM = 0.5 * tcRoot * root.Chord, StationY = rootY, UltimateNm = ultRoot, InPlaneUltimateNm = ultRoot * 0.5 / tcRoot,
                }, sf.Id, 0));
            }
        }

        // Fuselage: nose, cabin and tail boom along the line from the nose to the tail cone.
        ContactPoint? nose = _contacts.FirstOrDefault(p => p.Name == "nose"), tail = _contacts.FirstOrDefault(p => p.Name == "tail-cone");
        ContactPoint? belly = _contacts.FirstOrDefault(p => p.Name == "belly");
        if (nose is not null && tail is not null)
        {
            (double noseCut, double tailCut) = AirframeContact.FuselageStations(c);
            Vec3 At(double x) => nose.Body + (tail.Body - nose.Body) * ((nose.Body.X - x) / Math.Max(1e-6, nose.Body.X - tail.Body.X));
            double r = belly is not null ? Math.Max(0.2, belly.Body.Z) : 0.5;
            edges.Add(new StrikeEdge { A = nose.Body, B = At(noseCut), Component = AirframeComponent.Nose, RadiusM = r, CrushVBreakMs = AirframeContact.NoseBreakMs * strength, Name = "fuselage-nose" });
            edges.Add(new StrikeEdge { A = At(noseCut), B = At(tailCut), Component = AirframeComponent.Cabin, RadiusM = r, CrushVBreakMs = AirframeContact.CabinBreakMs * strength, Name = "fuselage-cabin" });
            edges.Add(new StrikeEdge { A = At(tailCut), B = tail.Body, Component = AirframeComponent.TailBoom, RadiusM = 0.6 * r, CrushVBreakMs = AirframeContact.TailBoomBreakMs * strength, Name = "fuselage-tail" });
        }

        // Tail surfaces: leading edges.
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool hTail = id == "hstab", vTail = id.Contains("vstab") || id.Contains("fin");
            if ((!hTail && !vTail) || sf.Strips.Count == 0) continue;
            double tc = AirframeHydroModel.ThicknessRatio(sf.Strips[0].Airfoil);
            Vec3 Le(StripConfig st) => new(st.Pos[0] + 0.25 * st.Chord, st.Pos[1], st.Pos[2]);
            if (hTail)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    var s = sf.Strips.Where(st => Math.Sign(st.Pos[1]) == side).OrderBy(st => Math.Abs(st.Pos[1])).ToList();
                    if (s.Count == 0) continue;
                    Vec3 tipLe = Le(s[^1]) + new Vec3(0, side * 0.5 * WingStructure.Width(s[^1]), 0);
                    edges.Add(MapThirds(new StrikeEdge { A = new Vec3(Le(s[0]).X, 0, Le(s[0]).Z), B = tipLe, Component = AirframeComponent.TailHorizontal, Side = side, RadiusM = 0.5 * tc * s[0].Chord,
                        CrushVBreakMs = 5.0 * strength, Name = $"hstab-{(side < 0 ? "L" : "R")}", GoneWith = new[] { AirframeComponent.TailBoom } }, sf.Id, 1));
                }
            }
            else
            {
                var s = sf.Strips.OrderBy(st => -st.Pos[2]).ToList();
                Vec3 top = Le(s[^1]) + new Vec3(0, 0, -0.5 * WingStructure.Width(s[^1]));
                edges.Add(MapThirds(new StrikeEdge { A = Le(s[0]), B = top, Component = AirframeComponent.TailVertical, RadiusM = 0.5 * tc * s[0].Chord,
                    CrushVBreakMs = 5.0 * strength, Name = "fin", GoneWith = new[] { AirframeComponent.TailBoom } }, sf.Id, 2));
            }
        }
        return edges;
    }

    /// <summary>Which aero strips stand behind each third of <paramref name="e"/> (kind 0 a wing and its rows, 1 the
    /// tailplane and elevator, 2 the fin and rudder), and the control surface among them.</summary>
    private StrikeEdge MapThirds(StrikeEdge e, string baseId, int kind)
    {
        var lists = new[] { new List<int>(), new List<int>(), new List<int>() };
        int idx = 0;
        foreach (SurfaceConfig sf in Config.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool member = kind == 0 ? WingStructure.BelongsTo(sf.Id, baseId, Config)
                : kind == 1 ? id == "hstab" || id == "elevator"
                : id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            foreach (StripConfig st in sf.Strips)
            {
                int i = idx++;
                if (!member || (kind != 2 && Math.Sign(st.Pos[1]) != e.Side)) continue;
                double a = kind == 2 ? -e.A.Z : Math.Abs(e.A.Y), b = kind == 2 ? -e.B.Z : Math.Abs(e.B.Y), x = kind == 2 ? -st.Pos[2] : Math.Abs(st.Pos[1]);
                if (Math.Abs(b - a) < 1e-6) continue;
                double f = (x - a) / (b - a);
                if (f < -0.02 || f > 1.02) continue;
                int k = Math.Clamp((int)(f * 3), 0, 2);
                lists[k].Add(i);
                if (st.Control is { } ctl)
                {
                    string c = ctl.Surface.ToLowerInvariant();
                    AirframeComponent? comp = c == "aileron" ? (e.Side < 0 ? AirframeComponent.AileronLeft : AirframeComponent.AileronRight)
                        : c == "elevator" ? (e.Side < 0 ? AirframeComponent.ElevatorLeft : AirframeComponent.ElevatorRight)
                        : c == "rudder" ? AirframeComponent.Rudder : null;
                    if (comp is not null) e.ThirdControl[k] = comp;
                }
            }
        }
        for (int k = 0; k < 3; k++) e.ThirdStrips[k] = lists[k].ToArray();
        return e;
    }

    private bool EdgeActive(StrikeEdge e)
    {
        if (e.Component != AirframeComponent.Cabin && _lost.Contains(e.Component)) return false;
        for (int i = 0; i < e.GoneWith.Length; i++) if (_lost.Contains(e.GoneWith[i])) return false;
        if (e.Panel >= 0 && _panels[e.Panel].State == PanelState.Broken) return false;
        return true;
    }

    /// <summary>After the step: the edges swept from <paramref name="pre"/> to the current state, against the stems and
    /// crowns near the airframe.</summary>
    private void StepTrees(RigidBodyState pre, double dt)
    {
        ITreeSource? src = Trees ?? (WorldTerrain.Active is not null ? Forest.Live : null);
        if (src is null) return;
        _edges ??= BuildStrikeEdges();
        if (_edges.Count == 0) return;
        // A state that is not finite (or a step that moved it absurdly far) has nothing to sweep: the cell loop below would
        // run from CellOf(NaN) = int.MinValue.
        Vec3 sp = State.Position, d = State.Position - pre.Position;
        if (!double.IsFinite(sp.X) || !double.IsFinite(sp.Y) || !double.IsFinite(sp.Z) || !double.IsFinite(d.Length) || d.Length > 50.0) return;
        Vec3 cg = Config.Mass.CgVec();
        double reach = 0;
        for (int i = 0; i < _edges.Count; i++) { StrikeEdge e = _edges[i]; reach = Math.Max(reach, Math.Max((e.A - cg).Length, (e.B - cg).Length) + e.RadiusM); }
        if (Trees is null)
        {
            // The whole sky in one height query, as the forest's own solid does.
            WorldTerrain t = WorldTerrain.Active!;
            if (-State.Position.Z - reach > t.HeightAt(State.Position.X, State.Position.Y) + Forest.SkyClearM) return;
        }
        double m = Math.Max(reach, 1.0) + 5.0 + (State.Position - pre.Position).Length;   // + the widest crown, + the step
        _treesNear.Clear();
        src.Near(State.Position.X - m, State.Position.Y - m, State.Position.X + m, State.Position.Y + m, _treesNear);
        if (_treesNear.Count == 0) return;

        for (int ti = 0; ti < _treesNear.Count; ti++)
        {
            Forest.Tree tree = _treesNear[ti];
            for (int ei = 0; ei < _edges.Count; ei++)
            {
                StrikeEdge e = _edges[ei];
                if (!EdgeActive(e)) continue;
                StemTest(src, tree, e, pre, cg);
            }
        }
        CrownDrag(src, cg, dt);
    }

    private Vec3 World(RigidBodyState s, Vec3 body, Vec3 cg) => s.Position + s.Attitude.Rotate(body - cg);

    private void StemTest(ITreeSource src, in Forest.Tree tree, StrikeEdge e, RigidBodyState pre, Vec3 cg)
    {
        Vec3 a0 = World(pre, e.A, cg), b0 = World(pre, e.B, cg), a1 = World(State, e.A, cg), b1 = World(State, e.B, cg);
        double top = src.TopOf(tree);
        // Quick reject: the edge's height band over the step against the standing stem.
        double hiUp = -Math.Min(Math.Min(a0.Z, b0.Z), Math.Min(a1.Z, b1.Z)) + e.RadiusM, loUp = -Math.Max(Math.Max(a0.Z, b0.Z), Math.Max(a1.Z, b1.Z)) - e.RadiusM;
        if (loUp > top || hiUp < tree.GroundM) return;
        for (int k = 0; k <= SweepSamples; k++)
        {
            double f = (double)k / SweepSamples;
            Vec3 a = a0 + (a1 - a0) * f, b = b0 + (b1 - b0) * f;
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double u = l2 > 1e-8 ? Math.Clamp(((tree.X - a.X) * dx + (tree.Y - a.Y) * dy) / l2, 0.0, 1.0) : 0.0;
            Vec3 q = a + (b - a) * u;
            double up = -q.Z;
            if (l2 < 0.0025)
            {
                // A near-vertical edge (the fin): where it overlaps the stem, at its lowest (the stem is thickest there).
                double eLo = -Math.Max(a.Z, b.Z), eHi = -Math.Min(a.Z, b.Z);
                up = Math.Max(eLo, tree.GroundM);
                if (up > Math.Min(eHi, top)) continue;
                u = Math.Abs(b.Z - a.Z) > 1e-6 ? Math.Clamp((-up - a.Z) / (b.Z - a.Z), 0, 1) : 0;
                q = a + (b - a) * u;
            }
            if (up < tree.GroundM || up > top) continue;
            double d = TreeStructure.DiameterAt(tree, up);
            double hx = q.X - tree.X, hy = q.Y - tree.Y, dist = Math.Sqrt(hx * hx + hy * hy);
            if (dist >= 0.5 * d + e.RadiusM) continue;
            Resolve(src, tree, e, e.A + (e.B - e.A) * u, new Vec3(q.X, q.Y, -up), hx, hy, dist, d, cg);
            return;
        }
    }

    private void Resolve(ITreeSource src, in Forest.Tree tree, StrikeEdge e, Vec3 bodyPt, Vec3 qWorld, double hx, double hy, double dist, double d, Vec3 cg)
    {
        RigidBodyState s = State;
        Vec3 r = bodyPt - cg;
        Vec3 vQ = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, r));
        Vec3 n = dist > 1e-4 ? new Vec3(hx / dist, hy / dist, 0) : new Vec3(-vQ.X, -vQ.Y, 0).Normalized();
        double closing = -Vec3.Dot(vQ, n);
        if (closing <= 0.01 || double.IsNaN(closing)) return;
        Vec3 nb = s.Attitude.Conjugate().Rotate(n);
        MassProperties mp = MassProperties;
        double mEff = 1.0 / (1.0 / mp.MassKg + Vec3.Dot(Vec3.Cross(mp.Solve(Vec3.Cross(r, nb)), r), nb));

        double fStem = TreeStructure.BreakForceN(d);
        double fDemand = closing * Math.Sqrt(AirframeContact.SpringNPerM * mEff);
        // The part's strength at this point, along this normal.
        double fPart, perNVert = 0;
        bool unbreakable = e.Component == AirframeComponent.Cabin && _lost.Contains(AirframeComponent.Cabin);
        if (e.UltimateNm > 0)
        {
            double arm = Math.Abs(bodyPt.Y) - e.StationY;
            double mv = Math.Abs(arm * nb.Z), mi = Math.Abs(arm * nb.X);
            perNVert = mv / e.UltimateNm;
            double rho = Math.Sqrt(perNVert * perNVert + Math.Pow(mi / e.InPlaneUltimateNm, 2));
            fPart = rho > 1e-9 ? 1.0 / rho : double.PositiveInfinity;
        }
        else fPart = 0.5 * mEff * e.CrushVBreakMs * e.CrushVBreakMs / CrushTravelM;
        if (unbreakable) fPart = double.PositiveInfinity;
        // A fuselage section crushes before it breaks (FuselageStructure): struck harder than it crushes, and by a stem that
        // does not snap first, it takes the crush, and only a section whose crush has passed its capacity comes off. A
        // steel-tube cabin or tail boom never does: it is stopped, or the stem snaps.
        if (Crush.Type != StructureType.Untagged && e.UltimateNm <= 0 && FuselageStructure.IsSection(e.Component) && !unbreakable && fStem > fPart && fDemand > fPart)
        {
            if (!Crush.Absorb(e.Component, closing, e.CrushVBreakMs, mEff, out double crush))
            {
                CrushFromTree(e.Component, nb, crush, mEff, closing, e.CrushVBreakMs);
                fPart = double.PositiveInfinity;
            }
        }

        double j; TreeStrikeOutcome outcome;
        double up = -qWorld.Z;
        if (fDemand <= Math.Min(fStem, fPart))
        {
            j = mEff * closing;
            outcome = TreeStrikeOutcome.Stopped;
            BendFromStrike(e, perNVert * fDemand);
        }
        else if (fStem <= fPart)
        {
            double mTop = TreeStructure.MassAboveKg(tree, up);
            j = Math.Min(mEff * closing, 0.25 * mTop * closing + fStem * d / closing);
            outcome = TreeStrikeOutcome.StemSnapped;
            // The edge cuts its own thickness out of the stem: what is left stands below the band it swept.
            src.BreakAt(tree, up - e.RadiusM - 0.05);
            BendFromStrike(e, perNVert * fStem);
            Contact?.Invoke(new ContactEvent(ContactEventKind.TreeSnapped, e.Name, e.Component, ContactPoint.SurfaceSolid, fStem, d, qWorld, vQ));
        }
        else
        {
            // The part goes: separation first (LoseComponent hands the piece the rigid velocity field as it is now), then the
            // trunk stops the piece where it struck, then the rest pays what the joint carried while it failed.
            AirframeComponent comp = e.Component;
            _separationStops[comp] = (qWorld, n);
            if (e.Panel >= 0) BreakPanel(e.Panel, double.IsInfinity(fPart) ? 1.0 : fStem / fPart, null);
            else LoseComponent(comp);
            _separationStops.Remove(comp);
            j = Math.Min(mEff * closing, fPart * FailureTravelM / closing);
            outcome = TreeStrikeOutcome.PartBroken;
        }
        ApplyImpulse(r, n * j);
        _impacts.Record(closing, "tree:" + e.Name, false, qWorld, vQ);
        TreeStrikeLog.Add(new TreeStrikeRecord(e.Name, e.Component, outcome, closing, j, d, up, fStem, fPart));
    }

    /// <summary>The vertical part of a strike moment past the limit (and short of ultimate) bends an intact panel.</summary>
    private void BendFromStrike(StrikeEdge e, double ratioToUltimate)
    {
        if (e.Panel < 0) return;
        WingPanel w = _panels[e.Panel];
        if (w.State != PanelState.Intact || ratioToUltimate * w.UltimateNm <= w.LimitNm) return;
        w.State = PanelState.Bent;
        SetBend(e.Panel, WingStructure.BentSetRad);
        Raise(ContactEventKind.PanelBent, e.Name, w.Component, null, ratioToUltimate, WingStructure.BentSetRad * 180 / Math.PI);
    }

    /// <summary>
    /// Inside a crown, each third of each edge (1) pushes the needles and fine wood out of its way at its own speed — the
    /// foliage drag ρ_c·A·v² — and (2) crosses branches (<see cref="TreeStructure.BranchDiameterAt"/>): each costs the work of
    /// breaking it, F·d, and does the damage <see cref="BranchStrike"/> describes to the leading edge, the strips behind it,
    /// its control surface; a limb is struck once, as a stem would be. Then the propellers in a crown chew branches.
    /// </summary>
    private void CrownDrag(ITreeSource src, Vec3 cg, double dt)
    {
        Vec3 jSum = Vec3.Zero, hSum = Vec3.Zero;   // impulse (body) and its moment about P (body)
        int n3 = _edges!.Count * 3;
        if (_thirdDamage.Length != n3) { _thirdDamage = new double[n3]; _thirdCtrlDamage = new double[n3]; _thirdReported = new int[n3]; }
        _ctrlToLose.Clear();
        for (int ei = 0; ei < _edges.Count; ei++)
        {
            StrikeEdge e = _edges[ei];
            if (!EdgeActive(e)) continue;
            double len3 = (e.B - e.A).Length / 3.0;
            double area = len3 * CrownSweepHeightM;
            Vec3 u = (e.B - e.A).Normalized();
            for (int k = 0; k < 3; k++)
            {
                Vec3 b = e.A + (e.B - e.A) * ((k + 0.5) / 3.0);
                Vec3 wPt = World(State, b, cg);
                double up = -wPt.Z;
                for (int ti = 0; ti < _treesNear.Count; ti++)
                {
                    Forest.Tree t = _treesNear[ti];
                    if (up > src.TopOf(t)) continue;
                    double rc = TreeStructure.CrownRadiusAt(t, up);
                    if (rc <= 0) continue;
                    double dx = wPt.X - t.X, dy = wPt.Y - t.Y, rho2 = dx * dx + dy * dy;
                    if (rho2 > rc * rc) continue;
                    Vec3 rb = b - cg;
                    Vec3 v = State.Velocity + Vec3.Cross(State.Rates, rb);
                    double speed = v.Length;
                    Vec3 jb = v * (-TreeStructure.CrownBulkDensityKgM3 * area * speed * dt);
                    // Branches: how many this third crosses this step, how thick, and how hard each pushes.
                    double rho = Math.Sqrt(rho2);
                    double d = TreeStructure.BranchDiameterAt(t, up, rho);
                    Vec3 vAcross = v - u * Vec3.Dot(v, u);
                    double va = vAcross.Length;
                    if (d > 0 && va > 0.5)
                    {
                        double n = TreeStructure.CrossingsPerS(TreeStructure.BranchLengthDensity(t, up), len3, va) * dt;
                        double f = TreeStructure.BranchStrikeForceN(d, va);
                        jb -= vAcross / va * (n * f * d / va);   // the work F·d of each, as an impulse along the motion
                        int slot = ei * 3 + k;
                        double dmg = n * BranchStrike.DamageFrom(f, BranchStrike.LeadingEdgeDentN, len3);
                        if (dmg > 0) DamageThird(e, k, slot, dmg, f, d, wPt, v);
                        if (e.ThirdControl[k] is { } ctl && !_lost.Contains(ctl))
                        {
                            _thirdCtrlDamage[slot] += n * BranchStrike.DamageFrom(f, BranchStrike.ControlSurfaceN, len3);
                            if (_thirdCtrlDamage[slot] >= 1.0 && !_ctrlToLose.Contains(ctl)) _ctrlToLose.Add(ctl);
                        }
                        // A limb is struck when the edge first meets one, and again each time the limbs it meets are a
                        // third thicker than the last it struck (going deeper toward the stem).
                        if (d >= BranchStrike.LimbM && (!_limbsStruck.TryGetValue((TreeKey(t), slot), out double last) || d >= BranchStrike.LimbStepRatio * last))
                        {
                            _limbsStruck[(TreeKey(t), slot)] = d;
                            LimbStrike(t, e, b, d, vAcross, wPt, cg);
                        }
                    }
                    jSum += jb; hSum += Vec3.Cross(rb, jb);
                    break;
                }
            }
        }
        PropBranches(src, cg, dt);
        if (jSum.LengthSquared > 0)
            State = new RigidBodyState(State.Position, State.Attitude, State.Velocity + jSum / MassProperties.MassKg, State.Rates + MassProperties.Solve(hSum));
        for (int i = 0; i < _ctrlToLose.Count; i++)
        {
            Raise(ContactEventKind.BranchDamage, _ctrlToLose[i].ToString(), _ctrlToLose[i], null, 0, 0);
            LoseComponent(_ctrlToLose[i]);
        }
    }

    private double[] _thirdDamage = Array.Empty<double>(), _thirdCtrlDamage = Array.Empty<double>(), _stripDamage = Array.Empty<double>();
    private int[] _thirdReported = Array.Empty<int>();
    private readonly List<AirframeComponent> _ctrlToLose = new();
    private readonly Dictionary<(long, int), double> _limbsStruck = new();
    /// <summary>Branch damage 0..1 of each strip (AeroModel order), empty while none.</summary>
    public IReadOnlyList<double> StripBranchDamage => _stripDamage;
    /// <summary>Highest branch damage on any strip, 0..1.</summary>
    public double MaxBranchDamage { get { double m = 0; for (int i = 0; i < _stripDamage.Length; i++) m = Math.Max(m, _stripDamage[i]); return m; } }

    private static long TreeKey(in Forest.Tree t) => (long)Math.Round(t.X * 100.0) * 1_000_003L ^ (long)Math.Round(t.Y * 100.0);

    /// <summary>The leading edge's third <paramref name="k"/> took <paramref name="add"/> damage: the strips behind it lose
    /// lift and gain drag (<see cref="BranchStrike"/>).</summary>
    private void DamageThird(StrikeEdge e, int k, int slot, double add, double forceN, double d, Vec3 wPt, Vec3 vBody)
    {
        double dmg = Math.Min(1.0, _thirdDamage[slot] + add);
        _thirdDamage[slot] = dmg;
        int[] strips = e.ThirdStrips[k];
        if (strips.Length > 0)
        {
            if (_stripDamage.Length != _stripTotal)
            {
                _stripDamage = new double[_stripTotal];
                _flowState.LiftFactor = Enumerable.Repeat(1.0, _stripTotal).ToArray();
                _flowState.ExtraCd = new double[_stripTotal];
            }
            for (int i = 0; i < strips.Length; i++)
            {
                int s = strips[i];
                if (dmg <= _stripDamage[s]) continue;
                _stripDamage[s] = dmg;
                _flowState.LiftFactor![s] = 1.0 - BranchStrike.LiftLossAtFull * dmg;
                _flowState.ExtraCd![s] = BranchStrike.ExtraCdAtFull * dmg;
            }
        }
        int tenth = (int)(dmg * 10.0);
        if (tenth > _thirdReported[slot])
        {
            _thirdReported[slot] = tenth;
            Contact?.Invoke(new ContactEvent(ContactEventKind.BranchDamage, e.Name, e.Component, ContactPoint.SurfaceSolid, forceN * d, d, wPt, State.Attitude.Rotate(vBody)));
        }
    }

    /// <summary>A limb (<see cref="BranchStrike.LimbM"/> or thicker) met by an edge: it snaps, or the part it met bends or
    /// breaks — the stem rule (<see cref="Resolve"/>) with the limb's own strength, F = F_s(d) + ρ_w·d²·v².</summary>
    private void LimbStrike(in Forest.Tree t, StrikeEdge e, Vec3 bodyPt, double d, Vec3 vAcrossBody, Vec3 wPt, Vec3 cg)
    {
        double va = vAcrossBody.Length;
        if (va < 0.5) return;
        Vec3 nb = vAcrossBody / -va;   // the limb pushes back against the motion
        Vec3 r = bodyPt - cg;
        double mEff = EffectiveMass(r, nb);
        double fLimb = TreeStructure.BranchStrikeForceN(d, va);
        double fPart, perNVert = 0;
        if (e.UltimateNm > 0)
        {
            double arm = Math.Abs(bodyPt.Y) - e.StationY;
            double mv = Math.Abs(arm * nb.Z), mi = Math.Abs(arm * nb.X);
            perNVert = mv / e.UltimateNm;
            double rhoN = Math.Sqrt(perNVert * perNVert + Math.Pow(mi / e.InPlaneUltimateNm, 2));
            fPart = rhoN > 1e-9 ? 1.0 / rhoN : double.PositiveInfinity;
        }
        else fPart = 0.5 * mEff * e.CrushVBreakMs * e.CrushVBreakMs / CrushTravelM;
        bool fuselage = e.UltimateNm <= 0 && FuselageStructure.IsSection(e.Component);
        // A limb is not a trunk: it does not stop the airframe, it does the work F·d of being broken through. On a fuselage
        // section past its crush force that work crushes it by F·d / F_c (not by the closing speed's energy).
        if (fuselage && fLimb > fPart && Crush.Type != StructureType.Untagged)
        {
            double crush = fLimb * d / fPart;
            if (!Crush.AbsorbCrush(e.Component, crush, fLimb * d))
            {
                CrushAt(e.Component, null, nb, crush, mEff, 0.0, 0.0);
                fPart = double.PositiveInfinity;
            }
        }
        if (e.Component == AirframeComponent.Cabin && _lost.Contains(AirframeComponent.Cabin)) fPart = double.PositiveInfinity;
        Vec3 nWorld = State.Attitude.Rotate(nb);
        double j; TreeStrikeOutcome outcome;
        if (fLimb <= fPart)
        {
            double rc = TreeStructure.CrownRadiusAt(t, -wPt.Z), rho = Math.Sqrt((wPt.X - t.X) * (wPt.X - t.X) + (wPt.Y - t.Y) * (wPt.Y - t.Y));
            double mLimb = TreeStructure.WoodDensityKgM3 * Math.PI / 4 * d * d * Math.Max(0.0, rc - rho) * 0.5;
            j = Math.Min(mEff * va, 0.25 * mLimb * va + TreeStructure.BreakForceN(d) * d / va);
            outcome = TreeStrikeOutcome.StemSnapped;
            BendFromStrike(e, perNVert * fLimb);
        }
        else
        {
            if (e.Panel >= 0) BreakPanel(e.Panel, double.IsInfinity(fPart) ? 1.0 : fLimb / fPart, null);
            else LoseComponent(e.Component);
            j = Math.Min(mEff * va, fPart * FailureTravelM / va);
            outcome = TreeStrikeOutcome.PartBroken;
        }
        ApplyImpulse(r, nWorld * j);
        _impacts.Record(va, "limb:" + e.Name, false, wPt, State.Attitude.Rotate(State.Velocity + Vec3.Cross(State.Rates, r)));
        TreeStrikeLog.Add(new TreeStrikeRecord("limb:" + e.Name, e.Component, outcome, va, j, d, -wPt.Z, TreeStructure.BreakForceN(d), fPart));
        Contact?.Invoke(new ContactEvent(ContactEventKind.BranchDamage, "limb:" + e.Name, e.Component, ContactPoint.SurfaceSolid, fLimb * d, d, wPt, State.Attitude.Rotate(-nb * va)));
    }

    /// <summary>A turning propeller in a crown: its blades cross the branches its disc meets (the disc's diameter as the
    /// edge, at the aeroplane's speed). Each crossing of wood thicker than <see cref="BranchStrike.PropNickM"/> chews
    /// <see cref="BranchStrike.PropChewFraction"/> of its diameter off the tips; a limb of <see cref="BranchStrike.PropStopM"/>
    /// stops the engine.</summary>
    private void PropBranches(ITreeSource src, Vec3 cg, double dt)
    {
        for (int e = 0; e < _propPatch.Length; e++)
        {
            if (_propStopped[e] || _noseLost || _propRadius[e] <= 0) continue;
            Vec3 hub = Vec3.Zero; int nd = 0;
            for (int i = 0; i < _contacts.Count; i++) if (_contacts[i].PropDisc && _contacts[i].Patch == _propPatch[e]) { hub += _contacts[i].Body; nd++; }
            if (nd == 0) continue;
            hub /= nd;
            Vec3 w = World(State, hub, cg);
            double up = -w.Z;
            for (int ti = 0; ti < _treesNear.Count; ti++)
            {
                Forest.Tree t = _treesNear[ti];
                if (up > src.TopOf(t)) continue;
                double rc = TreeStructure.CrownRadiusAt(t, up);
                double dx = w.X - t.X, dy = w.Y - t.Y;
                if (rc <= 0 || dx * dx + dy * dy > rc * rc) continue;
                double d = TreeStructure.BranchDiameterAt(t, up, Math.Sqrt(dx * dx + dy * dy));
                double v = (State.Velocity + Vec3.Cross(State.Rates, hub - cg)).Length;
                if (d >= BranchStrike.PropStopM)
                {
                    _propStopped[e] = true;
                    Raise(ContactEventKind.PropStoppage, "prop:branch", AirframeComponent.Propeller, null, d, EngineRpm);
                    if (Config.Engines.Count == 0) LoseComponent(AirframeComponent.Propeller);
                    else { SetEngineThrottleScale(e, 0.0); int patch = _propPatch[e]; _contacts.RemoveAll(p => p.PropDisc && p.Patch == patch); }
                }
                else if (d > BranchStrike.PropNickM)
                {
                    double n = TreeStructure.CrossingsPerS(TreeStructure.BranchLengthDensity(t, up), 2 * _propRadius[e], v) * dt;
                    double r1 = Math.Max(0.3 * _propRadius0[e], _propRadius[e] - n * BranchStrike.PropChewFraction * d);
                    if (r1 < _propRadius[e])
                    {
                        if ((int)(_propRadius[e] / _propRadius0[e] * 50) != (int)(r1 / _propRadius0[e] * 50))
                            Raise(ContactEventKind.BranchDamage, "prop:branch", AirframeComponent.Propeller, null, n, d);
                        ShrinkDisc(e, r1);
                    }
                }
                break;
            }
        }
    }

    /// <summary>An impulse <paramref name="jWorld"/> (N·s) at body point <paramref name="rBody"/> (from P) on the rigid body.</summary>
    private void ApplyImpulse(Vec3 rBody, Vec3 jWorld)
    {
        Vec3 jb = State.Attitude.Conjugate().Rotate(jWorld);
        MassProperties mp = MassProperties;
        State = new RigidBodyState(State.Position, State.Attitude, State.Velocity + jb / mp.MassKg, State.Rates + mp.Solve(Vec3.Cross(rBody, jb)));
    }
}

public enum TreeStrikeOutcome { Stopped, StemSnapped, PartBroken }

/// <summary>One stem strike: which edge, what happened, the closing speed (m/s), the impulse on the airframe (N·s), the
/// stem diameter and height where struck, and the two strengths compared (N).</summary>
public readonly struct TreeStrikeRecord
{
    public readonly string Edge; public readonly AirframeComponent Component; public readonly TreeStrikeOutcome Outcome;
    public readonly double ClosingMs, ImpulseNs, StemDiameterM, HeightM, StemStrengthN, PartStrengthN;
    public TreeStrikeRecord(string edge, AirframeComponent component, TreeStrikeOutcome outcome, double closingMs,
        double impulseNs, double stemDiameterM, double heightM, double stemStrengthN, double partStrengthN)
    {
        Edge = edge; Component = component; Outcome = outcome; ClosingMs = closingMs; ImpulseNs = impulseNs;
        StemDiameterM = stemDiameterM; HeightM = heightM; StemStrengthN = stemStrengthN; PartStrengthN = partStrengthN;
    }
    public override string ToString() =>
        $"{Edge} ({Component}): {Outcome} at {ClosingMs:F1} m/s, {ImpulseNs:F1} N·s; stem {StemDiameterM * 100:F1} cm at {HeightM:F1} m, " +
        $"{StemStrengthN / 1000:F1} kN against the part's {PartStrengthN / 1000:F1} kN";
}

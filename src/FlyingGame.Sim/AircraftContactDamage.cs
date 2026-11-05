using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Sim;

/// <summary>
/// Contact damage (Ty 2026-11-03): wing panels that bend or break on a ground strike, biplane slave struts, prop strikes,
/// and the scrape / part events that the sound and dust follow. See <see cref="WingStructure"/> for the structural law.
/// </summary>
public sealed partial class Aircraft
{
    private List<WingPanel> _panels = new();
    private List<SlaveLink> _links = new();
    private double[] _linkLocal = Array.Empty<double>();          // each link's slaved aileron, LOCAL deflection (rad, + = TE down)
    private bool[]? _hingeExcluded;                                // rows no longer on the stick's circuit (HingeTerms)
    private readonly Dictionary<ContactPoint, Vec3> _straightBody = new();
    private readonly HashSet<int> _deadAileronRows = new();       // fixed rows whose aileron has gone: their camber effect is 0
    private int[] _panelReported = Array.Empty<int>();             // how many 5° steps of set each panel has reported
    private double[] _bendSkinDamage = Array.Empty<double>();     // per strip: a bent panel's buckled skin / loose fabric, 0..1
    private int _stripTotal;
    private SurfaceConfig?[] _stripSurface = Array.Empty<SurfaceConfig?>();
    private StripConfig?[] _stripAt = Array.Empty<StripConfig?>();

    // Propellers (one per engine; one for a single-engine type).
    private int[] _propPatch = Array.Empty<int>();
    private double[] _propRadius0 = Array.Empty<double>(), _propRadius = Array.Empty<double>(), _propOmega = Array.Empty<double>(), _propStrikeDepth = Array.Empty<double>();
    private double[] _propThrustScale = Array.Empty<double>();
    private bool[] _propStriking = Array.Empty<bool>(), _propStopped = Array.Empty<bool>();
    // Per blade (WaterBladeCount per prop, flattened [prop * n + blade]): tip radius now, and how far this strike has
    // pushed it back so far; the rotor's angle (blade 0 from the bottom of the disc) while a strike lasts.
    private double[] _bladeRadius = Array.Empty<double>(), _bladeDemand = Array.Empty<double>(), _propPhase = Array.Empty<double>();
    private double[] _propVibG = Array.Empty<double>();
    // What the thrust model flies with: the blades' own scale × (ω/ω_run)² while a strike is slowing the rotor.
    private double[] _propThrustNow = Array.Empty<double>();

    private readonly Dictionary<string, ScrapeState> _scrapes = new();
    private readonly List<ScrapeState> _scrapeList = new();
    private readonly HashSet<string> _scrapeSeen = new();
    private readonly List<string> _scrapeGone = new();

    /// <summary>Wing panels outboard of their structural stations (state, bend, strength).</summary>
    public IReadOnlyList<WingPanel> Panels => _panels;
    /// <summary>The aileron slave struts (biplanes that have them).</summary>
    public IReadOnlyList<SlaveLink> SlaveLinks => _links;
    /// <summary>Patches sliding on the ground or a solid right now.</summary>
    public IReadOnlyList<ScrapeState> Scrapes => _scrapeList;
    /// <summary>Scrape start/stop, panel bent/broken, prop strike/stoppage — raised after the step they happen in.</summary>
    public event Action<ContactEvent>? Contact;
    /// <summary>Thrust left on propeller <paramref name="i"/> after curled tips (1 = undamaged).</summary>
    public double PropThrustScale(int i = 0) => i < _propThrustScale.Length ? _propThrustScale[i] : 1.0;
    /// <summary>Blade-tip radius of propeller <paramref name="i"/> now, m (shortened by a strike): its LONGEST blade, the
    /// disc it still sweeps.</summary>
    public double PropRadiusM(int i = 0) => i < _propRadius.Length ? _propRadius[i] : 0.0;
    /// <summary>Tip radius of blade <paramref name="blade"/> of propeller <paramref name="i"/>, m (each bends on its own).</summary>
    public double PropBladeRadiusM(int i = 0, int blade = 0)
        => i * WaterBladeCount + blade < _bladeRadius.Length ? _bladeRadius[i * WaterBladeCount + blade] : PropRadiusM(i);
    /// <summary>How bent propeller <paramref name="i"/> is, 0 (straight) .. 1: the mean tip shortening over the blades,
    /// against the 70 % a strike can take off (the 0.3 R floor). For the visual.</summary>
    public double PropBend01(int i = 0)
    {
        if (i >= _propRadius0.Length || _propRadius0[i] <= 0) return 0;
        double sum = 0;
        for (int k = 0; k < WaterBladeCount; k++) sum += _propRadius0[i] - PropBladeRadiusM(i, k);
        return Math.Clamp(sum / WaterBladeCount / (0.7 * _propRadius0[i]), 0, 1);
    }
    /// <summary>The airframe's shake from propeller <paramref name="i"/>'s imbalance, g, once per revolution
    /// (<see cref="PropVibrationHz"/>): zero for a straight prop or a stopped one.</summary>
    public double PropVibrationG(int i = 0) => i < _propVibG.Length && !_propStopped[i] && !_noseLost && EngineRunning ? _propVibG[i] : 0.0;
    /// <summary>Once per revolution of the running propeller, Hz.</summary>
    public double PropVibrationHz => EngineRpm / 60.0;
    /// <summary>The thrust factor the prop model flies with now, per propeller.</summary>
    private double[] PropThrustNow
    {
        get
        {
            for (int e = 0; e < _propThrustNow.Length; e++)
            {
                double f = _propThrustScale[e];
                if (_propStriking[e])
                {
                    double wRun = Math.Max(1e-6, FreeEngineRpm * Math.PI / 30.0), r = Math.Clamp(_propOmega[e] / wRun, 0, 1);
                    f *= r * r;   // a fixed blade's thrust at the same advance ∝ ω²
                }
                _propThrustNow[e] = f;
            }
            return _propThrustNow;
        }
    }
    /// <summary>A slaved (or disconnected) aileron's LOCAL deflection on a side, rad (+ = trailing edge down); NaN if none.</summary>
    public double SlavedAileronLocalRad(int side)
    {
        for (int i = 0; i < _links.Count; i++) if (_links[i].Side == side) return _linkLocal[i];
        return double.NaN;
    }

    private void InitContactDamage()
    {
        _panels = WingStructure.Build(Config);
        _links = SlaveLink.Build(Config);
        _linkLocal = new double[_links.Count];
        _panelReported = new int[_panels.Count];
        foreach (ContactPoint p in _contacts) if (p.Panel >= 0) _straightBody[p] = p.Body;
        if (_panels.Count > 0)
        {
            // The structure scale from a model of its own: building the aeroplane's Hydro here would arm the water
            // model on every aeroplane from its first step (it is created on first use, near water or on a break).
            AirframeHydroModel hm = AirframeHydroModel.Build(Config, Load);
            WingStructure.EnsureStrength(_panels, Config, hm.StructureScale * hm.WingMassFactor);   // the wing weight the wreck model carries
            foreach (ContactPoint p in _contacts)
            {
                if (p.Panel < 0) continue;
                p.PanelRef = _panels[p.Panel];
                if (_panels[p.Panel].TipStiffnessNPerM > 0)
                    p.StiffnessScale = _panels[p.Panel].TipStiffnessNPerM / (AirframeContact.SpringNPerM + _panels[p.Panel].TipStiffnessNPerM);
            }
            foreach (WingPanel w in _panels) SetCaps(w, 0.0);
        }
        _stripTotal = 0;
        foreach (SurfaceConfig sf in Config.Surfaces) _stripTotal += sf.Strips.Count;
        _stripSurface = new SurfaceConfig?[_stripTotal]; _stripAt = new StripConfig?[_stripTotal];
        int n = 0;
        foreach (SurfaceConfig sf in Config.Surfaces) foreach (StripConfig st in sf.Strips) { _stripSurface[n] = sf; _stripAt[n] = st; n++; }

        var patches = new List<int>();
        foreach (ContactPoint p in _contacts) if (p.PropDisc && !patches.Contains(p.Patch)) patches.Add(p.Patch);
        _propPatch = patches.ToArray();
        int np = _propPatch.Length;
        double r0 = Config.Propulsion is { PropDiameterM: > 0 } pc ? 0.5 * pc.PropDiameterM : 0;
        _propRadius0 = Enumerable.Repeat(r0, np).ToArray();
        _propRadius = Enumerable.Repeat(r0, np).ToArray();
        _propOmega = new double[np]; _propStrikeDepth = new double[np];
        _propThrustScale = Enumerable.Repeat(1.0, np).ToArray();
        _propStriking = new bool[np]; _propStopped = new bool[np];
        _bladeRadius = Enumerable.Repeat(r0, np * WaterBladeCount).ToArray();
        _bladeDemand = new double[np * WaterBladeCount];
        _propPhase = new double[np]; _propVibG = new double[np];
        _propThrustNow = Enumerable.Repeat(1.0, np).ToArray();
    }

    private string? BaseWingOf(string surfaceId)
    {
        foreach (SurfaceConfig b in Config.Surfaces)
            if (WingStructure.IsWingBase(b.Id) && WingStructure.BelongsTo(surfaceId, b.Id, Config)) return b.Id;
        return null;
    }

    private WingPanel? PanelFor(string surfaceId, int side)
    {
        string? b = BaseWingOf(surfaceId);
        foreach (WingPanel p in _panels) if (p.SurfaceId == b && p.Side == side) return p;
        return null;
    }

    private bool StripGone(int i) => _stripMask is not null && i < _stripMask.Length && !_stripMask[i];

    // ---- aileron slave struts and disconnected ailerons --------------------------------------------------------------

    /// <summary>
    /// After the stick's actuators have moved: put every slaved aileron where its strut puts it, let a disconnected one
    /// float to its own hinge-moment balance, and park the camber effect of rows whose aileron has gone. The ailerons are
    /// never frozen — through an intact strut the slaved one follows the driven one at every step, bent panel or not.
    /// </summary>
    private void UpdateAileronLinkage(double dt)
    {
        if (_links.Count == 0 && _deadAileronRows.Count == 0) return;
        double[] ovr = _flowState.DeflectionOverride is { } o && o.Length == _stripTotal ? o : (_flowState.DeflectionOverride = new double[_stripTotal]);
        Array.Fill(ovr, double.NaN);
        foreach (int r in _deadAileronRows) ovr[r] = 0.0;
        ControlAxisConfig ax = Config.Controls.Aileron;
        for (int i = 0; i < _links.Count; i++)
        {
            SlaveLink l = _links[i];
            if (AllGone(l.SlavedRows)) continue;     // the slaved aileron itself has gone
            if (l.Connected)
            {
                double driven = l.DrivenSign * (_aileronRad + Config.Rig.AileronRad);
                WingPanel? pd = PanelFor(l.DrivenSurface, l.Side), ps = PanelFor(l.SlavedSurface, l.Side);
                // Built only for a bent panel: a lambda over pd/ps here would allocate its closure every step.
                Func<Vec3, Vec3>? bd = pd is { State: PanelState.Bent } ? BendOf(pd) : null;
                Func<Vec3, Vec3>? bs = ps is { State: PanelState.Bent } ? BendOf(ps) : null;
                if (bd is null && bs is null)
                {
                    // Both wings straight: a working slave strut moves the two ailerons together, and that is what the
                    // aeroplane was rigged with (RigSolver) — the stick's deflection, exactly, as before.
                    _linkLocal[i] = driven;
                    continue;
                }
                // A bent wing: the deflection the strut now gives, less what it gives straight (the reconstructed horns are
                // not exactly 1:1 — the upper hinge is swept, the lower is not — and that residue is not the bend's).
                double guess = _linkLocal[i] - driven;
                _linkLocal[i] = driven + l.Solve(driven, bd, bs, driven + guess, 0.7) - l.Solve(driven, null, null, driven, 0.7);
            }
            else
            {
                // FREE: no strut, no cable, no spring, no tab — it trails where its own hinge moment is zero,
                // H = a + b·δ = 0 (the same strip terms as the stick-free circuit), settling with the linkage's lag.
                double a = 0, b = 0;
                for (int j = 0; j < l.SlavedRows.Length; j++)
                {
                    int r = l.SlavedRows[j];
                    StripConfig st = _stripAt[r]!;
                    HingeStrip(r, st.Area * st.Chord, Math.Sign(st.Control!.Gain), ax, ref a, ref b);
                }
                double cur = _linkLocal[i] * l.SlavedSign, target;
                if (b > 0) target = Math.Sign(cur != 0 ? cur : a) * ax.LimitFor(Math.Sign(cur != 0 ? cur : a));   // reversed flow: to the stop
                else if (-b < 1e-6) target = cur;                                                                // no air: it stays
                else target = ax.ClampDefl(a / -b);
                _linkLocal[i] = Settle(cur, target, ax, dt) * l.SlavedSign;
            }
            double axisVal = _linkLocal[i] * l.SlavedSign;
            for (int j = 0; j < l.SlavedRows.Length; j++) ovr[l.SlavedRows[j]] = axisVal;
            WingPanel? sp = PanelFor(l.SlavedSurface, l.Side);
            if (sp is not null)
                for (int j = 0; j < sp.AileronFixed.Length; j++) if (!_deadAileronRows.Contains(sp.AileronFixed[j])) ovr[sp.AileronFixed[j]] = axisVal;
        }
    }

    private bool AllGone(int[] rows)
    {
        for (int j = 0; j < rows.Length; j++) if (!StripGone(rows[j])) return false;
        return true;
    }

    private static Func<Vec3, Vec3> BendOf(WingPanel p) => v => p.Deform(v);

    // ---- wing panels -------------------------------------------------------------------------------------------------

    // ---- water on the panels -----------------------------------------------------------------------------------------

    /// <summary>The water's bending moment on each panel's station this step, N·m, signed as CheckPanels' "up" (+ = tip up).</summary>
    private double[] _panelWaterUp = Array.Empty<double>();

    /// <summary>
    /// On water a wingtip's hard points push nothing (the airframe's own volumes are AirframeFlooding's), so a panel would
    /// never feel the water at all. Its load is the water's force on the wing elements outboard of its station — buoyancy,
    /// the momentum handed to the water it sets moving (the slam) and drag, <see cref="WaterSlam.ElementForce"/> — taken
    /// about the station like a contact force: a share of each element by the part of its span beyond the station, at the
    /// middle of that part. It enters the same law as the ground (Ty 2026-11-03: the same localized break-up on water,
    /// static load included).
    /// </summary>
    private void RecordPanelWaterLoads(RigidBodyState s0, RigidBodyState s1, double dt)
    {
        if (_panels.Count == 0) return;
        if (_panelWaterUp.Length != _panels.Count) _panelWaterUp = new double[_panels.Count];
        Array.Clear(_panelWaterUp, 0, _panelWaterUp.Length);
        if (!_hydroArmed || !Hydro.Active || dt <= 0) return;
        Vec3 cg = Config.Mass.CgVec();
        for (int k = 0; k < _panels.Count; k++)
        {
            WingPanel w = _panels[k];
            if (w.State == PanelState.Broken) continue;
            IReadOnlyList<HydroElement> attached = Hydro.Attached;
            for (int j = 0; j < attached.Count; j++)
            {
                HydroElement e = attached[j];
                if (Math.Sign(e.Centre.Y) != w.Side || !(e.Name == w.SurfaceId || WingStructure.BelongsTo(e.Name, w.SurfaceId, Config))) continue;
                if (e.Atom is not (WreckAtom.WingLInner or WreckAtom.WingLOuter or WreckAtom.WingRInner or WreckAtom.WingROuter or WreckAtom.AileronL or WreckAtom.AileronR)) continue;
                double y0 = Math.Abs(e.Centre.Y) - e.Half.Y, y1 = Math.Abs(e.Centre.Y) + e.Half.Y;
                if (y1 <= w.BreakY) continue;
                double inner = Math.Max(y0, w.BreakY), frac = y1 > y0 ? (y1 - inner) / (y1 - y0) : 1.0;
                Vec3 f = WaterSlam.ElementForce(e, cg, s0, s1, Hydro.WaterUp, dt, Hydro.Flow) * frac;
                if (f.LengthSquared == 0) continue;
                double ry = w.Side * (0.5 * (y1 + inner) - w.BreakY), rz = e.Centre.Z - w.BreakZ;
                _panelWaterUp[k] += -w.Side * (ry * f.Z - rz * f.Y);
            }
        }
    }

    /// <summary>
    /// The panels' plastic hinges after a step (<see cref="WingStructure"/>, <see cref="PlasticHinge"/>). The demand on
    /// each is what its tip's hard points asked for on the step's first stage (before AirframeContact capped them at the
    /// hinges), plus the water's moment, plus — once the panel is touching or already damaged — the air's: the LIFT's
    /// load factor (AeroLoadFactorZ) times its 1 g moment M1. (The whole-aeroplane load factor also counts the ground
    /// decelerating the fuselage, which bends no wing.) A demand above the backbone turns the hinge until the push, relieved
    /// by the tip rising out of the ground (Σ(k + c/dt)·r² of the touching points), meets it; the permanent bend and twist
    /// are the panel's geometry from then on, and a hinge that has turned through its rupture rotation lets the panel go.
    /// An intact panel in clear air is left to the airframe's g-limits (StructuralState), as before.
    /// </summary>
    private void CheckPanels(double dt)
    {
        if (_panels.Count == 0) return;
        bool any = false;
        for (int i = 0; i < _panels.Count && !any; i++)
            if (_panels[i].ContactRecorded || _panels[i].State == PanelState.Bent || (i < _panelWaterUp.Length && _panelWaterUp[i] != 0)) any = true;
        if (!any)
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].ElasticRad = 0.0;
            return;
        }
        WingStructure.EnsureStrength(_panels, Config, Hydro.Model.StructureScale * Hydro.Model.WingMassFactor);
        for (int k = 0; k < _panels.Count; k++)
        {
            WingPanel w = _panels[k];
            if (w.State == PanelState.Broken || w.Bending is null || w.Twisting is null) { ClearRecorded(w); continue; }
            double water = k < _panelWaterUp.Length ? _panelWaterUp[k] : 0.0;
            bool contact = w.ContactRecorded;
            bool touched = contact || water != 0;
            bool damaged = w.State == PanelState.Bent;
            double air = touched || damaged ? AeroLoadFactorZ * w.M1Nm : 0.0;
            double up = contact ? w.RawUpNm : 0.0, tw = contact ? w.RawTwistNm : 0.0;
            double kUp = contact ? w.KUpSpring + (dt > 0 ? w.KUpDamp / dt : 0.0) : 0.0;
            double kTw = contact ? w.KTwSpring + (dt > 0 ? w.KTwDamp / dt : 0.0) : 0.0;
            ContactPoint? at = null; double fMax = 0;
            if (contact)
            {
                for (int i = 0; i < _contacts.Count; i++)
                {
                    ContactPoint p = _contacts[i];
                    if (p.Panel == k && p.ForceBody.LengthSquared > fMax * fMax) { fMax = p.ForceBody.Length; at = p; }
                }
                w.PeakContactNm = Math.Max(w.PeakContactNm, Math.Abs(up));
            }
            // The flex the (capped) contact force puts in it: F/k_w at the tip, as the tip slope of a cantilever.
            double len = w.TipY - w.BreakY, carried = up * (contact ? w.ContactScale : 0.0) + water;
            w.ElasticRad = touched && w.TipStiffnessNPerM > 0 && len > 0 ? 1.5 * (carried / len) / w.TipStiffnessNPerM / len : 0.0;
            double demand = up + water + air;
            double dB = w.Bending.Load(demand, kUp);
            double dT = w.Twisting.Load(tw, kTw);
            ClearRecorded(w);
            if (w.Bending.Ruptured || w.Twisting.Ruptured)
            {
                BreakPanel(k, Math.Max(Math.Abs(demand) / w.UltimateNm, w.Twisting.Ruptured ? 1.0 : 0.0), at);
                continue;
            }
            if (dB != 0 || dT != 0)
            {
                bool first = w.State == PanelState.Intact;
                w.State = PanelState.Bent;
                SetBend(k);
                int steps = (int)(Math.Max(Math.Abs(w.BendRad), Math.Abs(w.TwistRad)) / (5.0 * Math.PI / 180.0));
                if (first || steps > _panelReported[k])
                {
                    _panelReported[k] = Math.Max(_panelReported[k], steps);
                    Raise(ContactEventKind.PanelBent, at?.Name ?? w.Component.ToString(), w.Component, at, Math.Abs(demand) / w.UltimateNm, w.BendRad * 180 / Math.PI);
                }
            }
            SetCaps(w, water + air);
        }
    }

    private static void ClearRecorded(WingPanel w)
    {
        w.ContactRecorded = false; w.RawUpNm = 0; w.RawTwistNm = 0; w.ContactScale = 1.0;
        w.KUpSpring = w.KUpDamp = w.KTwSpring = w.KTwDamp = 0;
    }

    /// <summary>What the tip's hard points may push on the next step before the hinges yield: the bending backbone less
    /// what the air and the water already put on it (each way), and the torsion backbone.</summary>
    private static void SetCaps(WingPanel w, double otherUpNm)
    {
        if (w.Bending is null || w.Twisting is null) return;
        double c = w.Bending.Capacity;
        w.CapUpNm = Math.Max(0.0, c - otherUpNm);
        w.CapDownNm = Math.Max(0.0, c + otherUpNm);
        w.CapTwistNm = w.Twisting.Capacity;
    }

    /// <summary>Bend a wing panel NOW, as a strike would (scenarios, lessons, tests): <paramref name="rad"/> + = tip up,
    /// the 5° set by default, put in through its bending hinge (so it is as weakened as a panel bent that far). False if
    /// the type has no such panel or it has already gone.</summary>
    public bool BendPanel(AirframeComponent comp, double rad = WingStructure.BentSetRad, double twistRad = 0.0)
    {
        int k = _panels.FindIndex(p => p.Component == comp);
        if (k < 0 || _panels[k].State == PanelState.Broken) return false;
        WingPanel w = _panels[k];
        WingStructure.EnsureStrength(_panels, Config, Hydro.Model.StructureScale * Hydro.Model.WingMassFactor);
        w.Bending!.Impose(rad);
        if (twistRad != 0) w.Twisting!.Impose(twistRad);
        w.State = PanelState.Bent;
        SetBend(k);
        SetCaps(w, 0.0);
        return true;
    }

    /// <summary>Panel <paramref name="k"/> takes the shape of its hinges' sets: its hard points, its rows' load points,
    /// dihedral and incidence follow, and its skin is as buckled as its hinges are past their peak.</summary>
    private void SetBend(int k)
    {
        WingPanel w = _panels[k];
        w.BendRad = w.Bending?.SetRad ?? w.BendRad;
        w.TwistRad = w.Twisting?.SetRad ?? w.TwistRad;
        foreach (ContactPoint p in _contacts) if (p.Panel == k && _straightBody.TryGetValue(p, out Vec3 b0)) p.Body = w.Deform(b0);
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        double[] inc = _flowState.IncidenceDeltaRad is { } a && a.Length == _stripTotal ? a : (_flowState.IncidenceDeltaRad = new double[_stripTotal]);
        double skin = 0;
        foreach (PlasticHinge? h in new[] { w.Bending, w.Twisting })
            if (h is not null && h.PathRad > h.Law.PeakRad) skin = Math.Max(skin, Math.Min(1.0, (h.PathRad - h.Law.PeakRad) / (h.Law.RuptureRad - h.Law.PeakRad)));
        foreach (int i in w.Strips)
        {
            StripConfig st = _stripAt[i]!;
            Vec3 pos = st.PosVec();
            _flowState.BendRad[i] = w.BendRad;
            _flowState.BendOffset[i] = w.Deform(pos) - pos;
            inc[i] = w.TwistRad * w.SpanFraction(Math.Abs(pos.Y));
            SetSkinDamage(i, skin);
        }
    }

    /// <summary>A bent panel's skin: buckled metal or slack, torn fabric over crushed ribs. It takes the same damaged-surface
    /// law as a branch-dented leading edge (<see cref="BranchStrike"/>: 1 − 0.4·D of the lift, 0.06·D more profile drag),
    /// with D the share of the way from the hinge's peak to its rupture; the worse of the two damages counts.</summary>
    private void SetSkinDamage(int strip, double d)
    {
        if (_bendSkinDamage.Length != _stripTotal) _bendSkinDamage = new double[_stripTotal];
        _bendSkinDamage[strip] = Math.Max(_bendSkinDamage[strip], d);
        ApplySurfaceDamage(strip);
    }

    private void ApplySurfaceDamage(int s)
    {
        double d = Math.Max(s < _bendSkinDamage.Length ? _bendSkinDamage[s] : 0.0, s < _stripDamage.Length ? _stripDamage[s] : 0.0);
        if (d <= 0 && _flowState.LiftFactor is null) return;
        _flowState.LiftFactor ??= Enumerable.Repeat(1.0, _stripTotal).ToArray();
        _flowState.ExtraCd ??= new double[_stripTotal];
        _flowState.LiftFactor[s] = 1.0 - BranchStrike.LiftLossAtFull * d;
        _flowState.ExtraCd[s] = BranchStrike.ExtraCdAtFull * d;
    }

    private void BreakPanel(int k, double value, ContactPoint? at)
    {
        WingPanel w = _panels[k];
        string part = at?.Name ?? w.Component.ToString();
        LoseComponent(w.Component);
        Raise(ContactEventKind.PanelBroken, part, w.Component, at, value, value);
    }

    /// <summary>LoseComponent's share for the contact-damage parts: a panel's rows, its aileron, its slave strut and its
    /// mass; and the tip panels that a combat or g-limit wing loss takes with it.</summary>
    private void OnComponentLostStructure(AirframeComponent comp)
    {
        int k = _panels.FindIndex(p => p.Component == comp);
        if (k >= 0)
        {
            WingPanel w = _panels[k];
            w.State = PanelState.Broken;
            foreach (int i in w.Strips) _stripMask![i] = false;
            foreach (int i in w.AileronHinged) _stripMask![i] = false;   // a hinge line cannot cross the missing panel
            foreach (int i in w.AileronFixed) if (!StripGone(i)) _deadAileronRows.Add(i);
            if (_flowState.BendRad is { } br) foreach (int i in w.Strips) { br[i] = 0; _flowState.BendOffset![i] = Vec3.Zero; }
            if (_flowState.IncidenceDeltaRad is { } incd) foreach (int i in w.Strips) incd[i] = 0;
            // The slave strut went with the aileron: the other aileron on this side is on its own now.
            foreach (SlaveLink l in _links)
            {
                if (l.Side != w.Side || !l.Connected) continue;
                if (l.DrivenRows.Any(w.AileronHinged.Contains) || l.SlavedRows.Any(w.AileronHinged.Contains))
                {
                    l.Connected = false;
                    _hingeExcluded ??= new bool[_stripTotal];
                    foreach (int r in l.SlavedRows) _hingeExcluded[r] = true;
                }
            }
            // A side whose only aileron was on this wing loses its aileron (the component, so the visual and the wreck
            // take it); on a four-aileron biplane only this wing's rows go, and their mass is shed here.
            bool onlyAileron = w.AileronHinged.Length > 0 && !_panels.Any(q => q != w && q.Side == w.Side && q.AileronHinged.Length > 0);
            var shed = new List<int>(w.Strips);
            if (!onlyAileron) shed.AddRange(w.AileronHinged.Where(i => !w.Strips.Contains(i)));
            else shed.RemoveAll(i => w.AileronHinged.Contains(i));
            ShedStrips(shed, w.Component);
            if (onlyAileron) LoseComponent(w.Side < 0 ? AirframeComponent.AileronLeft : AirframeComponent.AileronRight);
            return;
        }
        int side = comp is AirframeComponent.WingLeft or AirframeComponent.WingLeftInner or AirframeComponent.WingLeftOuter ? -1
                 : comp is AirframeComponent.WingRight or AirframeComponent.WingRightInner or AirframeComponent.WingRightOuter ? 1 : 0;
        if (side == 0) return;
        foreach (WingPanel w in _panels)
        {
            if (w.Side != side || w.State == PanelState.Broken) continue;
            w.State = PanelState.Broken;
            _contacts.RemoveAll(p => p.Component == w.Component);
        }
        foreach (SlaveLink l in _links) if (l.Side == side) l.Connected = false;
    }

    /// <summary>Take the structure of these strips off the rigid body: Raymer's wing mass per area, scaled like the rest
    /// of the airframe, moved from the element that carried it to a box of its own that leaves (<see cref="ShedMass"/>).</summary>
    private void ShedStrips(IEnumerable<int> strips, AirframeComponent comp)
    {
        double kg = Hydro.Model.StructureScale * Hydro.Model.WingMassFactor * WaterPhysics.RaymerWingKgPerM2, semi = WingPanels.Semispan(Config);
        var gone = new List<HydroElement>();
        foreach (int i in strips)
        {
            SurfaceConfig sf = _stripSurface[i]!; StripConfig st = _stripAt[i]!;
            double y = st.Pos[1];
            WreckAtom atom;
            if (sf.Id.ToLowerInvariant().Contains("aileron")) atom = y < 0 ? WreckAtom.AileronL : WreckAtom.AileronR;
            else
            {
                AirframeComponent? panel = WingPanels.PanelOf(y, semi);
                if (panel is null) continue;
                atom = panel switch
                {
                    AirframeComponent.WingLeftOuter => WreckAtom.WingLOuter, AirframeComponent.WingRightOuter => WreckAtom.WingROuter,
                    AirframeComponent.WingLeftInner => WreckAtom.WingLInner, _ => WreckAtom.WingRInner,
                };
            }
            HydroElement? host = Hydro.Attached.FirstOrDefault(e => e.Name == sf.Id && e.Atom == atom);
            if (host is null) continue;   // already gone with something else: nothing to shed twice
            double take = Math.Min(kg * st.Area, host.MassKg);
            if (take <= 0) continue;
            host.MassKg -= take;
            double tc = AirframeHydroModel.ThicknessRatio(st.Airfoil);
            gone.Add(new HydroElement(sf.Id + "-panel", atom, new Vec3(st.Pos[0] - 0.25 * st.Chord, y, st.Pos[2]),
                new Vec3(0.5 * st.Chord, 0.5 * WingStructure.Width(st), 0.5 * tc * st.Chord), 0, take, 0));
        }
        Hydro.AddDetached(comp, gone);   // the panel is a wreck piece of its own (CrashRelay), with exactly this mass
        ShedMass(gone);
    }

    // ---- propeller strike --------------------------------------------------------------------------------------------

    /// <summary>
    /// A blade in the ground or the water. The disc stations do not push (a blade cuts or curls); what they say is how
    /// deep the tips went and how hard the ground pressed on them (the contact model's own normal force).
    ///
    /// THE ROTOR. I·dω/dt = Q_engine − Q_air(ω) − Q_contact. A piston engine's torque is roughly flat with rpm (its mean
    /// effective pressure is), so Q_engine = Q_rated·(throttle, or the share that just holds idle) with Q_rated =
    /// P_max/ω_max; it used to be P/ω, which grew without bound as the prop slowed and let a running engine turn any prop
    /// through any water (Ty 2026-11-05: "a water strike does not stop the prop it should"). Q_air is the prop's own
    /// load, ∝ ω², which the engine balances at its running rpm. The ground drags each tip round with the Coulomb force
    /// of its surface, μ·N·R; the water with ½ρ_w(ωR)² on the dipped blade (<see cref="WaterBladeTorque"/>). Below
    /// <see cref="EngineStallFractionOfIdle"/> of idle the engine cannot keep firing: sudden stoppage, the engine stops,
    /// the prop is bent. Nothing here asks what the surface was, only what it did to the rotor (Ty 2026-11-04 / 11-05 9:00:
    /// "they don't have to stop the engine but they can").
    ///
    /// THE BLADES. Each blade is struck only when it sweeps through the part of the disc that is in the ground or the
    /// water (the arc acos(1 − d/R) either side of the lowest point, against the rotor's own angle), so a brush that lasts
    /// less than half a turn bends one blade and leaves the other straight. A solid surface curls the tip back by the
    /// depth it went in. Water bends a tip only where its load exceeds the blade: the dipped tip's normal force at the
    /// bend station (<see cref="BladeBendStationOverR"/>) against the section's yield moment
    /// (<see cref="BladeYieldMomentNm"/>), so the tip folds back until what is left in the water no longer overloads it
    /// (<see cref="WaterBladeYieldDepth"/>: about a millimetre at full power, a centimetre at idle). When the strike ends
    /// each blade keeps its shorter radius: the disc reaches as far as its longest blade, the thrust at the same power
    /// is the mean of the blades' (R_k/R₀)^(2/3) (momentum theory, as before), and blades of unequal length are an
    /// imbalance that shakes the airframe once a revolution (<see cref="BladeImbalanceG"/>).
    /// </summary>
    private void PropStrike(double dt)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc) return;
        int nb = WaterBladeCount;
        for (int e = 0; e < _propPatch.Length; e++)
        {
            if (_propStopped[e] || _noseLost) continue;
            double dMax = 0, dSolid = 0, dWater = 0, muN = 0; ContactPoint? at = null;
            for (int i = 0; i < _contacts.Count; i++)
            {
                ContactPoint p = _contacts[i];
                if (!p.PropDisc || p.Patch != _propPatch[e] || p.DepthM <= 0) continue;
                if (p.DepthM > dMax) { dMax = p.DepthM; at = p; }
                if (p.SurfaceCode == ContactPoint.SurfaceWater) dWater = Math.Max(dWater, p.DepthM);
                else
                {
                    dSolid = Math.Max(dSolid, p.DepthM);
                    muN += (p.SurfaceCode >= 0 ? WorldTerrain.KeelSlidingCoefficient((WorldTerrain.Surface)p.SurfaceCode) : AirframeContact.FrictionMu) * p.NormalN;   // a blade tip is smooth metal, not a ploughing wreck (AirframeContact.SlidingMu)
                }
            }
            if (dMax <= 0)
            {
                if (_propStriking[e])
                {
                    // The strike is over: each blade keeps what it lost.
                    _propStriking[e] = false;
                    KeepBends(e);
                }
                continue;
            }
            double rpm = FreeEngineRpm;
            double wRun = rpm * Math.PI / 30.0;
            if (!_propStriking[e])
            {
                _propStriking[e] = true;
                _propStrikeDepth[e] = 0;
                _propOmega[e] = wRun;
                // Where the blades are: as if the prop had turned at this rpm since the sim began (deterministic).
                _propPhase[e] = (Atmosphere.SimTimeSec * wRun + 1.3 * e) % (2 * Math.PI);
                Raise(ContactEventKind.PropStrike, "prop-tip", AirframeComponent.Propeller, at, muN * _propRadius[e], dMax);
            }
            _propStrikeDepth[e] = Math.Max(_propStrikeDepth[e], dSolid);
            double inertia = pc.PropInertia > 0 ? pc.PropInertia : 1.0;
            double wIdle = Math.Max(1.0, pc.IdleRpm * Math.PI / 30.0);
            double wMax = Math.Max(wIdle, pc.MaxRpm * Math.PI / 30.0);
            double throttle = ShaftPower01 * (Config.Engines.Count > e ? Config.Engines[e].ThrottleScale : 1.0);   // a dead engine drives nothing
            double qRated = pc.MaxPowerW / wMax;
            // A running engine at closed throttle still makes the torque that holds its idle against its own prop.
            bool driving = EngineRunning && (Config.Engines.Count <= e || Config.Engines[e].ThrottleScale > 0);
            double share = driving ? Math.Max(throttle, (wIdle / wMax) * (wIdle / wMax)) : 0;
            double qEngine = qRated * share;
            double wFree = Math.Max(wIdle, wRun);
            double qAir = qEngine * (_propOmega[e] / wFree) * (_propOmega[e] / wFree);
            double qGround = muN * _propRadius[e] + WaterBladeTorque(_propRadius[e], dWater, _propOmega[e]);

            // Which blades swept through the struck arc this step, and what each took.
            double alpha = Math.Acos(Math.Clamp(1.0 - dMax / Math.Max(_propRadius[e], 1e-6), -1.0, 1.0));
            double sweep = _propOmega[e] * dt;
            double dYield = dWater > 0 ? WaterBladeYieldDepth(_propRadius[e], _propOmega[e]) : 0;
            double demand = Math.Max(dSolid, dWater - dYield);
            for (int k = 0; k < nb; k++)
            {
                double start = (_propPhase[e] + k * 2 * Math.PI / nb + alpha) % (2 * Math.PI);
                if (start < 0) start += 2 * Math.PI;
                bool swept = sweep >= 2 * Math.PI || start <= 2 * alpha || start + sweep >= 2 * Math.PI;
                int b = e * nb + k;
                if (swept && demand > _bladeDemand[b]) _bladeDemand[b] = demand;
            }
            _propPhase[e] = (_propPhase[e] + sweep) % (2 * Math.PI);

            _propOmega[e] = Math.Min(wRun, _propOmega[e] + (qEngine - qAir - qGround) / inertia * dt);
            if (_propOmega[e] < EngineStallFractionOfIdle * wIdle)
            {
                _propStopped[e] = true;
                _propStriking[e] = false;
                KeepBends(e);   // the stopped prop shows what its blades took
                Raise(ContactEventKind.PropStoppage, "prop-tip", AirframeComponent.Propeller, at, qGround, rpm);
                if (Config.Engines.Count == 0) LoseComponent(AirframeComponent.Propeller);
                else
                {
                    SetEngineThrottleScale(e, 0.0);
                    int patch = _propPatch[e];
                    _contacts.RemoveAll(p => p.PropDisc && p.Patch == patch);
                }
            }
        }
    }

    /// <summary>Each blade keeps the shortening this strike gave it; the disc reaches as far as the longest, the thrust is
    /// the blades' mean (R_k/R₀)^(2/3), and unequal blades shake.</summary>
    private void KeepBends(int e)
    {
        int nb = WaterBladeCount;
        double rMax = 0, sMean = 0;
        for (int k = 0; k < nb; k++)
        {
            int b = e * nb + k;
            _bladeRadius[b] = Math.Max(0.3 * _propRadius0[e], _bladeRadius[b] - _bladeDemand[b]);
            _bladeDemand[b] = 0;
            rMax = Math.Max(rMax, _bladeRadius[b]);
            sMean += Math.Pow(_bladeRadius[b] / _propRadius0[e], 2.0 / 3.0) / nb;
        }
        MoveDisc(e, rMax);
        _propThrustScale[e] = sMean;
        _propVibG[e] = BladeImbalanceG(e);
    }

    /// <summary>The rotor of a struck prop while the strike lasts, rpm (the engine's own rpm otherwise).</summary>
    public double StrikeRotorRpm(int i = 0) => i < _propOmega.Length && _propStriking[i] ? _propOmega[i] * 30.0 / Math.PI : FreeEngineRpm;

    /// <summary>A piston engine slowed below this share of its idle rpm cannot keep firing: the starter cranks one at
    /// 150–300 rpm and none runs steadily much below idle (the fleet's idles are 550–800 rpm), so half of idle.</summary>
    public const double EngineStallFractionOfIdle = 0.5;

    /// <summary>Blade geometry the configs do not carry, typical of the fleet's metal props: two blades, a tip chord of
    /// 0.12 R (11-12 cm on a 76-80 in prop), and an in-plane force coefficient of 0.2 — a thin blade at the tip's ~6°
    /// incidence in water (normal force 2π·sin 6° ≈ 0.7, <see cref="WaterBladeNormalCoeff"/>) resolved into the plane of
    /// rotation through its ~18° blade angle.</summary>
    public const int WaterBladeCount = 2;
    public const double WaterBladeTipChordOverR = 0.12, WaterBladeInPlaneCoeff = 0.2, WaterBladeNormalCoeff = 0.7;

    /// <summary>The section a blade bends at, as a fraction of R: a strike folds the outer quarter (the tip curls that a
    /// ground strike leaves). The section there: chord <see cref="WaterBladeTipChordOverR"/>·R, thickness
    /// <see cref="BladeThicknessRatio"/> of it, 2025-T6 forging (the McCauley / Sensenich blade alloy) yielding at
    /// <see cref="BladeYieldPa"/>; an aerofoil's section modulus about its chord is ≈ 0.075·c·t².</summary>
    public const double BladeBendStationOverR = 0.75, BladeThicknessRatio = 0.08, BladeYieldPa = 255e6;

    /// <summary>The flatwise bending moment the blade section at <see cref="BladeBendStationOverR"/> yields at, N·m
    /// (162 N·m on the J-3's 0.915 m blade, 205 on an SR22's 0.99 m).</summary>
    public static double BladeYieldMomentNm(double radius)
    {
        double c = WaterBladeTipChordOverR * radius, t = BladeThicknessRatio * c;
        return BladeYieldPa * 0.075 * c * t * t;
    }

    /// <summary>The normal (bending) force on a blade tip dipped <paramref name="depth"/> into water at ω, N: the wetted
    /// tip ⅔·d by its chord at ½ρ_w(ωR)², normal coefficient <see cref="WaterBladeNormalCoeff"/>.</summary>
    public static double WaterBladeNormalForce(double radius, double depth, double omega)
    {
        if (depth <= 0 || omega <= 0 || radius <= 0) return 0;
        double u = omega * radius, wet = 2.0 / 3.0 * Math.Min(depth, radius);
        return 0.5 * FloatHydro.WaterDensity * u * u * WaterBladeTipChordOverR * radius * wet * WaterBladeNormalCoeff;
    }

    /// <summary>How deep a blade tip can go into water at ω before the load bends it: the depth whose tip force, at its
    /// arm from the bend station, is the section's yield moment. Deeper than this the tip folds back by the
    /// difference. Bisection (the moment is monotonic in depth over the outer quarter).</summary>
    public static double WaterBladeYieldDepth(double radius, double omega)
    {
        double my = BladeYieldMomentNm(radius), rb = BladeBendStationOverR * radius, lo = 0, hi = radius - rb;
        double Moment(double d) => WaterBladeNormalForce(radius, d, omega) * Math.Max(0.0, radius - d / 3.0 - rb);
        if (Moment(hi) <= my) return hi;
        for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (Moment(m) > my) hi = m; else lo = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// The airframe's once-a-revolution shake from blades of unequal length, g at the pilot. Two parts:
    ///  - MASS: a curled tip carries its outer segment's mass inward by about half the shortening; the blades' masses come
    ///    from the config's prop inertia (two rods, I = 2·⅓·m·R²), the outer quarter of a tapered blade ≈ 15 % of its
    ///    mass. The unbalance m·Δr whirls at ω²: F = m·|Δr₀ − Δr₁|·ω², on the aeroplane's mass.
    ///  - THRUST: each blade's share ∝ (R_k/R₀)^(2/3); the difference, ½·T·|s₀ − s₁|, acts at 0.7 R, a rotating
    ///    moment that rocks the airframe about its pitch/yaw axes, felt at the pilot's arm from the prop.
    /// Zero for straight or equally bent blades.
    /// </summary>
    private double BladeImbalanceG(int e)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc || WaterBladeCount != 2) return 0;
        double r0 = _propRadius0[e];
        double mBlade = 1.5 * (pc.PropInertia > 0 ? pc.PropInertia : 1.0) / (r0 * r0), mSeg = 0.15 * mBlade;
        double dr0 = r0 - _bladeRadius[e * 2], dr1 = r0 - _bladeRadius[e * 2 + 1];
        double w = FreeEngineRpm * Math.PI / 30.0;
        double m = Math.Max(1.0, MassProperties.MassKg);
        double aMass = mSeg * 0.5 * Math.Abs(dr0 - dr1) * w * w / m;
        double s0 = Math.Pow(_bladeRadius[e * 2] / r0, 2.0 / 3.0), s1 = Math.Pow(_bladeRadius[e * 2 + 1] / r0, 2.0 / 3.0);
        double thrust = PropModel.StaticThrustN(pc, ShaftPower01);
        double arm = 2.0;   // prop to the pilot's seat, m (the fleet's single-engine types: 1.5–2.5)
        double iyy = Math.Max(1.0, MassProperties.Iyy);
        double aThrust = 0.5 * thrust * Math.Abs(s0 - s1) * 0.7 * r0 * arm / iyy;
        return (aMass + aThrust) / Atmosphere.GravityMs2;
    }

    /// <summary>The mean torque the water puts on a disc of radius R whose lowest tip is <paramref name="depth"/> under the
    /// surface, turning at ω. Each blade spends acos(1 − d/R)/π of a turn in the wet arc, with on average ⅔·d of its tip
    /// under (a parabolic segment), moving at ωR through water of ρ_w: Q = n·(arc/π)·½ρ_w(ωR)²·c·(⅔d)·C·(R − ⅓d).
    /// On an SR22's 0.99 m blades at 2700 rpm (its engine makes ~820 N·m there) a 5 mm brush costs ~200 N·m and a 2 cm dip
    /// ~1,560; at idle the same 2 cm costs ~105 against an idling engine's next to nothing.</summary>
    public static double WaterBladeTorque(double radius, double depth, double omega)
    {
        if (depth <= 0 || omega <= 0 || radius <= 0) return 0;
        double d = Math.Min(depth, radius);
        double duty = Math.Acos(1.0 - d / radius) / Math.PI;
        double wet = 2.0 / 3.0 * d, u = omega * radius;
        double force = 0.5 * FloatHydro.WaterDensity * u * u * WaterBladeTipChordOverR * radius * wet * WaterBladeInPlaneCoeff;
        return WaterBladeCount * duty * force * (radius - 0.5 * wet);
    }

    /// <summary>Every blade no longer than <paramref name="r1"/> (a branch chews them all alike), then the disc and the
    /// thrust follow the blades.</summary>
    private void ShrinkDisc(int e, double r1)
    {
        int nb = WaterBladeCount;
        double rMax = 0, sMean = 0;
        for (int k = 0; k < nb; k++)
        {
            int b = e * nb + k;
            if (b < _bladeRadius.Length) { _bladeRadius[b] = Math.Min(_bladeRadius[b], r1); rMax = Math.Max(rMax, _bladeRadius[b]); sMean += Math.Pow(_bladeRadius[b] / _propRadius0[e], 2.0 / 3.0) / nb; }
        }
        if (rMax <= 0) { rMax = r1; sMean = Math.Pow(r1 / _propRadius0[e], 2.0 / 3.0); }
        MoveDisc(e, rMax);
        _propThrustScale[e] = sMean;
        if (e < _propVibG.Length) _propVibG[e] = BladeImbalanceG(e);
    }

    /// <summary>The disc stations at radius <paramref name="r1"/> from the hub: the reach of the longest blade.</summary>
    private void MoveDisc(int e, double r1)
    {
        int patch = _propPatch[e];
        var disc = _contacts.Where(p => p.PropDisc && p.Patch == patch).ToList();
        _propRadius[e] = r1;
        if (disc.Count == 0) return;
        Vec3 hub = Vec3.Zero; foreach (ContactPoint p in disc) hub += p.Body; hub /= disc.Count;
        foreach (ContactPoint p in disc) { Vec3 d = p.Body - hub; double l = d.Length; if (l > 1e-9) p.Body = hub + d * (r1 / l); }
    }

    // ---- scrapes and events ------------------------------------------------------------------------------------------

    private void AfterStepContactDamage(double dt)
    {
        CheckPanels(dt);
        PropStrike(dt);
        UpdateScrapes();
    }

    /// <summary>Which patches are sliding (on the ground or a solid, faster than <see cref="ScrapeState.SlipThresholdMs"/>),
    /// with their friction power; ScrapeStart / ScrapeStop for the ones that began or ended this step.</summary>
    private void UpdateScrapes()
    {
        _scrapeSeen.Clear();
        int patch = int.MinValue; ScrapeState? cur = null;
        for (int i = 0; i < _contacts.Count; i++)
        {
            ContactPoint p = _contacts[i];
            if (p.PropDisc) continue;
            bool sliding = p.NormalN > 0 && p.SurfaceCode != ContactPoint.SurfaceWater && p.SurfaceCode != ContactPoint.SurfaceNone;
            if (!sliding) continue;
            bool samePatch = p.Patch >= 0 && p.Patch == patch && cur is not null;
            if (!samePatch)
            {
                patch = p.Patch;
                if (!_scrapes.TryGetValue(p.Name, out cur)) { cur = new ScrapeState { Part = p.Name, Component = p.Component }; _scrapes[p.Name] = cur; }
                if (_scrapeSeen.Add(p.Name)) { cur.PowerW = 0; cur.NormalN = 0; cur.SlipMs = 0; }
                cur.SurfaceCode = p.SurfaceCode; cur.PositionWorld = p.WorldPos; cur.VelocityWorld = p.WorldVel;
            }
            cur!.PowerW += p.FrictionW;
            if (p.NormalN > cur.NormalN) { cur.PositionWorld = p.WorldPos; cur.VelocityWorld = p.WorldVel; cur.SurfaceCode = p.SurfaceCode; }
            cur.NormalN += p.NormalN;
            cur.SlipMs = Math.Max(cur.SlipMs, p.SlipMs);
        }
        _scrapeGone.Clear();
        foreach (KeyValuePair<string, ScrapeState> kv in _scrapes)
        {
            ScrapeState sc = kv.Value;
            bool active = _scrapeSeen.Contains(kv.Key) && sc.SlipMs > ScrapeState.SlipThresholdMs;
            bool listed = _scrapeList.Contains(sc);
            if (active && !listed)
            {
                _scrapeList.Add(sc);
                Contact?.Invoke(new ContactEvent(ContactEventKind.ScrapeStart, sc.Part, sc.Component, sc.SurfaceCode, sc.PowerW, sc.SlipMs, sc.PositionWorld, sc.VelocityWorld));
            }
            else if (!active && listed)
            {
                _scrapeList.Remove(sc);
                Contact?.Invoke(new ContactEvent(ContactEventKind.ScrapeStop, sc.Part, sc.Component, sc.SurfaceCode, 0, 0, sc.PositionWorld, sc.VelocityWorld));
            }
            if (!_scrapeSeen.Contains(kv.Key)) _scrapeGone.Add(kv.Key);
        }
        for (int i = 0; i < _scrapeGone.Count; i++) _scrapes.Remove(_scrapeGone[i]);
    }

    private void Raise(ContactEventKind kind, string part, AirframeComponent comp, ContactPoint? at, double intensity, double value)
    {
        Vec3 pos = at?.WorldPos ?? State.Position, vel = at?.WorldVel ?? State.Attitude.Rotate(State.Velocity);
        Contact?.Invoke(new ContactEvent(kind, part, comp, at?.SurfaceCode ?? ContactPoint.SurfaceNone, intensity, value, pos, vel));
    }
}

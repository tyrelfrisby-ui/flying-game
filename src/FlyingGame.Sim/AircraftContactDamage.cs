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
    private bool[] _propStriking = Array.Empty<bool>(), _propStopped = Array.Empty<bool>(), _propDamaged = Array.Empty<bool>();
    // Per blade (_bladeN per prop, flattened [prop * n + blade]): the straight span the air works on, the span the strike
    // affected (a metal curl, or the piece a wooden / composite blade lost), which way a curl went (+1 forward, −1 aft),
    // and whether it broke. The rotor's angle (blade 0 from the bottom of the disc) while a strike lasts.
    private int _bladeN = 2;
    private PropMaterial _propMaterial = PropMaterial.Metal;
    private double[] _bladeAero = Array.Empty<double>(), _bladeBent = Array.Empty<double>(), _propPhase = Array.Empty<double>();
    private int[] _bladeCurl = Array.Empty<int>(), _bladeStrikes = Array.Empty<int>();
    private bool[] _bladeBroken = Array.Empty<bool>();
    private readonly List<(double t, int k, ContactPoint? where, double tip, double depth)> _strikeBuf = new();
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
    /// <summary>Thrust left on propeller <paramref name="i"/> after a strike (1 = undamaged), at the throttle now: the blades'
    /// aero radii through <see cref="PropBladeDamage.Performance"/>.</summary>
    public double PropThrustScale(int i = 0) => PropPerformance(i).thrustScale;
    /// <summary>Blade-tip radius of propeller <paramref name="i"/> now, m (shortened by a strike): its LONGEST blade's
    /// reach, the disc it still sweeps.</summary>
    public double PropRadiusM(int i = 0) => i < _propRadius.Length ? _propRadius[i] : 0.0;
    /// <summary>How many blades each propeller has (config propulsion.propBladeCount).</summary>
    public int PropBladeCount => _bladeN;
    /// <summary>What the blades are made of (config propulsion.propMaterial).</summary>
    public PropMaterial PropBladeMaterial => _propMaterial;
    /// <summary>The straight span of blade <paramref name="blade"/> of propeller <paramref name="i"/>, m: what the air works
    /// on (a metal blade's curl is cosmetic and not in it; a broken blade ends here).</summary>
    public double PropBladeRadiusM(int i = 0, int blade = 0)
        => i * _bladeN + blade < _bladeAero.Length && blade < _bladeN ? _bladeAero[i * _bladeN + blade] : PropRadiusM(i);
    /// <summary>The span of that blade a strike affected, m: its curl (metal) or the piece it lost (wood, composite).</summary>
    public double PropBladeAffectedM(int i = 0, int blade = 0) => Blade(i, blade) is int b and >= 0 ? _bladeBent[b] : 0.0;
    /// <summary>Which way that blade's struck span curled: +1 forward, −1 aft, 0 straight or broken.</summary>
    public int PropBladeCurl(int i = 0, int blade = 0) => Blade(i, blade) is int b and >= 0 ? _bladeCurl[b] : 0;
    /// <summary>Whether that blade's struck span broke off (wood, composite).</summary>
    public bool PropBladeBroken(int i = 0, int blade = 0) => Blade(i, blade) is int b and >= 0 && _bladeBroken[b];
    /// <summary>How many times that blade has met the ground or the water (one <see cref="ContactEventKind.PropBladeStrike"/>
    /// each).</summary>
    public int PropBladeStrikes(int i = 0, int blade = 0) => Blade(i, blade) is int b and >= 0 ? _bladeStrikes[b] : 0;
    /// <summary>How far that blade reaches from the hub, m: its straight span, plus what a curl still reaches past the bend.</summary>
    public double PropBladeReachM(int i = 0, int blade = 0) => Blade(i, blade) is int b and >= 0 ? Reach(b) : PropRadiusM(i);
    private int Blade(int i, int blade) => blade >= 0 && blade < _bladeN && i * _bladeN + blade < _bladeAero.Length ? i * _bladeN + blade : -1;
    private double Reach(int b) => _bladeBroken[b] ? _bladeAero[b] : _bladeAero[b] + PropBladeDamage.CurledReach(_bladeBent[b]);
    /// <summary>How damaged propeller <paramref name="i"/> is, 0 (straight) .. 1: the mean affected span over the blades,
    /// against the 70 % a strike can take (the 0.3 R floor). For the visual.</summary>
    public double PropBend01(int i = 0)
    {
        if (i >= _propRadius0.Length || _propRadius0[i] <= 0) return 0;
        double sum = 0;
        for (int k = 0; k < _bladeN; k++) sum += PropBladeAffectedM(i, k);
        return Math.Clamp(sum / _bladeN / (0.7 * _propRadius0[i]), 0, 1);
    }
    /// <summary>The airframe's shake from propeller <paramref name="i"/>'s imbalance, g at the pilot, once per revolution
    /// (<see cref="PropVibrationHz"/>): zero for a straight prop or a stopped one.</summary>
    public double PropVibrationG(int i = 0)
    {
        if (i >= _propDamaged.Length || !_propDamaged[i] || _propStopped[i] || _noseLost || !EngineRunning) return 0.0;
        PropBladeDamage.Shake s = PropShake(i);
        double arm = 2.0;   // prop to the pilot's seat, m (the fleet's single-engine types: 1.5–2.5)
        return (s.ForceN / Math.Max(1.0, MassProperties.MassKg) + s.MomentNm * arm / Math.Max(1.0, MassProperties.Iyy)) / Atmosphere.GravityMs2;
    }
    /// <summary>The rotating load propeller <paramref name="i"/>'s imbalance puts on its engine mount, N, once per
    /// revolution (<see cref="PropBladeDamage.Shake.MountLoadN"/>).</summary>
    public double PropMountLoadN(int i = 0)
        => i < _propDamaged.Length && _propDamaged[i] && !_propStopped[i] && !_noseLost && EngineRunning ? PropShake(i).MountLoadN : 0.0;
    /// <summary>Once per revolution of the running propeller, Hz.</summary>
    public double PropVibrationHz => EngineRpm / 60.0;

    private PropBladeDamage.Shake PropShake(int e)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc) return default;
        int n = _bladeN;
        Span<double> aero = stackalloc double[n], bent = stackalloc double[n];
        Span<int> curl = stackalloc int[n];
        Span<bool> broken = stackalloc bool[n];
        for (int k = 0; k < n; k++) { int b = e * n + k; aero[k] = _bladeAero[b]; bent[k] = _bladeBent[b]; curl[k] = _bladeCurl[b]; broken[k] = _bladeBroken[b]; }
        double w = FreeEngineRpm * Math.PI / 30.0;
        double thrust = PropModel.StaticThrustN(pc, ShaftPower01) * PropPerformance(e).thrustScale;
        return PropBladeDamage.Imbalance(aero, bent, curl, broken, _propRadius0[e], PropBladeDamage.BladeMassKg(_propRadius0[e], _propMaterial), w, thrust);
    }

    /// <summary>The disc's thrust and rpm against an undamaged one's at the rpm the engine would run at now.</summary>
    private (double thrustScale, double rpmScale) PropPerformance(int e)
    {
        if (e >= _propDamaged.Length || !_propDamaged[e] || Config.Propulsion is not { PropDiameterM: > 0 } pc) return (1, 1);
        int n = _bladeN;
        Span<double> aero = stackalloc double[n];
        for (int k = 0; k < n; k++) aero[k] = _bladeAero[e * n + k];
        double cap = EngineRunning ? PropBladeDamage.EngineOverspeedLimit * pc.MaxRpm / Math.Max(1.0, BaseEngineRpm) : 1.0;
        return PropBladeDamage.Performance(aero, _propRadius0[e], pc.ConstantSpeed, cap);
    }

    /// <summary>The thrust factor the prop model flies with now, per propeller.</summary>
    private double[] PropThrustNow
    {
        get
        {
            for (int e = 0; e < _propThrustNow.Length; e++)
            {
                double f = PropPerformance(e).thrustScale;
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
        _bladeN = PropBladeDamage.BladeCountOf(Config.Propulsion);
        _propMaterial = PropBladeDamage.MaterialOf(Config.Propulsion);
        _propRadius0 = Enumerable.Repeat(r0, np).ToArray();
        _propRadius = Enumerable.Repeat(r0, np).ToArray();
        _propOmega = new double[np]; _propStrikeDepth = new double[np];
        _propStriking = new bool[np]; _propStopped = new bool[np]; _propDamaged = new bool[np];
        _bladeAero = Enumerable.Repeat(r0, np * _bladeN).ToArray();
        _bladeBent = new double[np * _bladeN];
        _bladeCurl = new int[np * _bladeN]; _bladeStrikes = new int[np * _bladeN];
        _bladeBroken = new bool[np * _bladeN];
        _propPhase = new double[np];
        for (int e = 0; e < np; e++) _propPhase[e] = 1.3 * e;   // the engines' props are not in step
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
        // NOT an early return when there are no links and nothing dead: a wing with a plain aileron and no slave
        // strut still has to be limited once it is bent (LimitBentAilerons below).
        if (_links.Count == 0 && _deadAileronRows.Count == 0 && !AnyPanelBent() && !AnyPanelFolded()) return;
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
                    double vN = 0.0, vD = 0.0;   // hinge terms only here, not the vane angle
                    HingeStrip(r, st.Area * st.Chord, Math.Sign(st.Control!.Gain), ax, ref a, ref b, ref vN, ref vD);
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
        LimitBentAilerons(ovr, ax);
    }

    /// <summary>
    /// A BENT WING'S AILERON HAS LESS TRAVEL, AND IT IS NOT CENTRED WHERE THE STICK IS (Ty 2026-11-10; the rules
    /// and his words are in <see cref="BentAileronTravel"/>). The band narrows linearly with the bend, from full at
    /// straight to nothing at the bend that fails the wing, and it is centred on the deflection the aileron was
    /// holding when that wing FIRST bent — so a wing bent with roll held leaves an aileron that still works one way
    /// and not the other. The centre is captured once; further bending only narrows the band about it.
    ///
    /// Run after the linkage above, so a slaved aileron is limited by its own panel's bend and not by its driver's.
    /// Per PANEL, which is per side and per wing: a biplane's upper and lower wings bend separately.
    /// </summary>
    private void LimitBentAilerons(double[] ovr, ControlAxisConfig ax)
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            WingPanel w = _panels[i];
            if (w.AileronHinged.Length == 0) continue;
            if (w.Bending is null && WorstFoldOfPair(w) <= 1e-6) continue;   // a fold still limits a panel with no hinge
            // THE WHOLE BEND, from either cause. Ty 2026-11-10: "Ground strikes leading to a bend should be
            // treated the same limiting aileron following the same rule". A panel's permanent bend is its contact
            // hinge's set PLUS the flight overstress's (WingStructure: BendRad is the sum), and reading only the
            // hinge covered the ground strike and silently missed the g-pull — the opposite way round from the
            // feature as it was first described. Summed here rather than read from BendRad so it cannot depend on
            // whether the panel's shape has been refreshed yet this step.
            // THE MOST BENT SIDE SETS THE LIMIT. Ty 2026-11-10: "Asymmetric aerodynamic bending is shown as bent
            // more on one side than the other with the most bent side determining the aileron limit". The two
            // ailerons are one control — a roll is what the PAIR does — so a band that differed between them would
            // not describe anything the stick can ask for. Each wing is drawn at its own bend; the limit is the
            // worst of them, which is the one that binds.
            double bend = WorstBendOfPair(w);
            // AND THE FOLD, which is a bend at the root. Ty, asked what a folded wing should do: "Draw it folded,
            // limit the aileron". It is measured on its own 90-degree scale (BentAileronTravel.HalfRangeFromFoldRad)
            // rather than the station hinge's rupture angle, which it would overshoot many times over. Worst side
            // again, for the same reason: the pair is one control.
            double fold = WorstFoldOfPair(w);
            if (bend <= 1e-6 && fold <= 1e-6)
            {
                // Straight (or straightened past measuring): no limit — and the centre STAYS captured, because the
                // wing has been bent once and the next bend must not move it.
                continue;
            }
            // The deflection this side is actually flying, which is the override where the linkage set one and the
            // stick's own otherwise — the same value the band has to be measured against.
            double commanded = ovr[w.AileronHinged[0]];
            if (double.IsNaN(commanded)) commanded = _aileronRad + Config.Rig.AileronRad;
            // FIRST bend on this panel: this deflection is the centre, for good.
            if (double.IsNaN(w.AileronCentreRad)) w.AileronCentreRad = commanded;
            double half = w.Bending is { } hinge
                ? BentAileronTravel.HalfRangeRad(bend, hinge.Law.RuptureRad, ax.MaxDeflRad)
                : ax.MaxDeflRad;
            // The narrower of the two binds: a wing both bent and folded keeps only what both allow.
            if (fold > 1e-6) half = System.Math.Min(half, BentAileronTravel.HalfRangeFromFoldRad(fold, ax.MaxDeflRad));
            double held = BentAileronTravel.Clamp(commanded, w.AileronCentreRad, half, ax.MaxDeflRad);
            for (int j = 0; j < w.AileronHinged.Length; j++) ovr[w.AileronHinged[j]] = held;
            for (int j = 0; j < w.AileronFixed.Length; j++)
                if (!_deadAileronRows.Contains(w.AileronFixed[j])) ovr[w.AileronFixed[j]] = held;
        }
    }

    /// <summary>The larger permanent bend of this panel and its opposite number — the same wing on the other side
    /// (a biplane's upper and lower stay separate, because they are separate wings).</summary>
    private double WorstBendOfPair(WingPanel w)
    {
        double worst = System.Math.Abs((w.Bending?.SetRad ?? 0.0) + w.OverstressSetRad);
        for (int i = 0; i < _panels.Count; i++)
        {
            WingPanel o = _panels[i];
            if (ReferenceEquals(o, w) || o.Lower != w.Lower || o.SurfaceId != w.SurfaceId || o.Side == w.Side) continue;
            worst = System.Math.Max(worst, System.Math.Abs((o.Bending?.SetRad ?? 0.0) + o.OverstressSetRad));
        }
        return worst;
    }

    /// <summary>The larger FOLD of this panel and its opposite number, rad. A cantilever wing folds at its root
    /// (<see cref="WingPanel.FoldRad"/>); one wing can fold while the other does not, and the folded one binds.</summary>
    private double WorstFoldOfPair(WingPanel w)
    {
        double worst = System.Math.Abs(w.FoldRad);
        for (int i = 0; i < _panels.Count; i++)
        {
            WingPanel o = _panels[i];
            if (ReferenceEquals(o, w) || o.Lower != w.Lower || o.SurfaceId != w.SurfaceId || o.Side == w.Side) continue;
            worst = System.Math.Max(worst, System.Math.Abs(o.FoldRad));
        }
        return worst;
    }

    private bool AnyPanelFolded()
    {
        for (int i = 0; i < _panels.Count; i++)
            if (System.Math.Abs(_panels[i].FoldRad) > 1e-6 && _panels[i].AileronHinged.Length > 0) return true;
        return false;
    }

    private bool AnyPanelBent()
    {
        for (int i = 0; i < _panels.Count; i++)
            if (_panels[i].Bending is { } b && System.Math.Abs(b.SetRad + _panels[i].OverstressSetRad) > 1e-6
                && _panels[i].AileronHinged.Length > 0) return true;
        return false;
    }

    private bool AllGone(int[] rows)
    {
        for (int j = 0; j < rows.Length; j++) if (!StripGone(rows[j])) return false;
        return true;
    }

    /// <summary>The shape under a bent wing's ailerons, for the slave strut's geometry: the WHOLE shape the wing is
    /// flying (WingPanel.Shape — the strike's kink, the aerodynamic curve from the root, and any fold), because the
    /// strut's two pins sit on whatever the wing is actually doing.</summary>
    private static Func<Vec3, Vec3> BendOf(WingPanel p) => v => p.Shape(v);

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
            if (_panels[i].ContactRecorded || StruckDamaged(_panels[i]) || (i < _panelWaterUp.Length && _panelWaterUp[i] != 0)) any = true;
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
            // Damaged by a STRIKE (its hinges have yielded): the air's load then works on what the strike left. A panel
            // only bent by an overstress is the wings' business (StructuralState.Wings), not its hinges' twice over.
            bool damaged = StruckDamaged(w);
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

    private static bool StruckDamaged(WingPanel w) => (w.Bending?.PathRad ?? 0.0) > 0.0 || (w.Twisting?.PathRad ?? 0.0) > 0.0;

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
        // The set is the hinges' (a strike) plus this wing's own overstress set (StructuralState.Wing(side),
        // AircraftOverstress — per side since 2026-11-10, so the two wings can be bent differently).
        w.BendRad = (w.Bending?.SetRad ?? 0.0) + w.OverstressSetRad;
        w.TwistRad = w.Twisting?.SetRad ?? w.TwistRad;
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        double[] inc = _flowState.IncidenceDeltaRad is { } a && a.Length == _stripTotal ? a : (_flowState.IncidenceDeltaRad = new double[_stripTotal]);
        double skin = 0;
        foreach (PlasticHinge? h in new[] { w.Bending, w.Twisting })
            if (h is not null && h.PathRad > h.Law.PeakRad) skin = Math.Max(skin, Math.Min(1.0, (h.PathRad - h.Law.PeakRad) / (h.Law.RuptureRad - h.Law.PeakRad)));
        // Where every row and hard point of this WING SIDE has moved to, and the rotation each flies at. The whole
        // side, not the panel's own rows: the aerodynamic bend is a curve from the root and a fold is a kink there, so
        // both move the rows inboard of the station (ShapeWingSide). The twist's incidence and the buckled skin below
        // stay on the panel's own rows, because those are the panel's.
        ShapeWingSide(w);
        foreach (int i in w.Strips)
        {
            Vec3 pos = _stripAt[i]!.PosVec();
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
    /// P_max/ω_max. Q_air is the prop's own load, ∝ ω², which the engine balances at its running rpm. The ground drags
    /// each tip round with the Coulomb force of its surface, μ·N·R; the water with ½ρ_w(ωR)² on each dipped blade
    /// (<see cref="WaterBladeTorque"/>, blade by blade at its own reach). Below <see cref="EngineStallFractionOfIdle"/> of
    /// idle the engine cannot keep firing: sudden stoppage. Nothing here asks what the surface was, only what it did to
    /// the rotor (Ty 2026-11-05 9:00: "they don't have to stop the engine but they can").
    ///
    /// THE BLADES (Ty 2026-11-05 9:04). The disc stations sit at the longest blade's reach; blade k reaches R_k and goes
    /// d − (R_disc − R_k) in, so a blade already shortened can pass over ground its neighbour hits. A blade meets the
    /// surface each time its angle enters the struck arc acos(1 − d_k/R_k) either side of the bottom of the disc: one
    /// <see cref="ContactEventKind.PropBladeStrike"/> per blade per pass (blades × rpm), timed within the step, which the
    /// sound plays one by one. What the pass does depends on the material (<see cref="StrikeBlade"/>):
    ///  - the ground always takes the span that went in; the water only past the depth whose tip load fails the section
    ///    (<see cref="WaterBladeFailDepth"/>: about a millimetre at full power, a centimetre at idle);
    ///  - METAL: that span curls, forward if the blade's thrust at that instant was positive and aft if negative
    ///    (<see cref="PropBladeDamage.BladeAlphaRad"/>), and stays on as a cosmetic curl the air does not see;
    ///  - WOOD / COMPOSITE: that span breaks off at the contact radius and leaves (<see cref="ContactEventKind.PropBladeBroken"/>);
    ///    the blade flies on shorter. In the water a brittle blade loses its whole wetted span once the load breaks it.
    /// The disc's thrust and rpm then follow the blades' aero radii (<see cref="PropBladeDamage.Performance"/>), and
    /// unequal or damaged blades shake once a revolution (<see cref="PropVibrationG"/>, <see cref="PropMountLoadN"/>).
    /// </summary>
    private void PropStrike(double dt)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc) return;
        int nb = _bladeN;
        double t0 = Atmosphere.SimTimeSec - dt;   // the step began here (the clock has already moved on)
        double wFreeNow = FreeEngineRpm * Math.PI / 30.0;
        for (int e = 0; e < _propPatch.Length; e++)
        {
            if (_propStopped[e] || _noseLost) continue;
            // The rotor's own angle, from its own rpm: never from the sim clock, which other flights have moved (two flights
            // that are the same must strike the same blade).
            if (!_propStriking[e]) _propPhase[e] = (_propPhase[e] + wFreeNow * dt) % (2 * Math.PI);
            double dMax = 0, dSolid = 0, dWater = 0, muN = 0; ContactPoint? at = null, solidAt = null, waterAt = null;
            for (int i = 0; i < _contacts.Count; i++)
            {
                ContactPoint p = _contacts[i];
                if (!p.PropDisc || p.Patch != _propPatch[e] || p.DepthM <= 0) continue;
                if (p.DepthM > dMax) { dMax = p.DepthM; at = p; }
                if (p.SurfaceCode == ContactPoint.SurfaceWater) { if (p.DepthM > dWater) { dWater = p.DepthM; waterAt = p; } }
                else
                {
                    if (p.DepthM > dSolid) { dSolid = p.DepthM; solidAt = p; }
                    muN += (p.SurfaceCode >= 0 ? WorldTerrain.KeelSlidingCoefficient((WorldTerrain.Surface)p.SurfaceCode) : AirframeContact.FrictionMu) * p.NormalN;   // a blade tip is smooth metal, not a ploughing wreck (AirframeContact.SlidingMu)
                }
            }
            if (dMax <= 0)
            {
                if (_propStriking[e])
                {
                    // The strike is over: the disc stations go to the longest blade's reach.
                    _propStriking[e] = false;
                    SettleDisc(e);
                }
                continue;
            }
            double rpm = FreeEngineRpm;
            double wRun = rpm * Math.PI / 30.0;
            bool onset = !_propStriking[e];
            if (onset)
            {
                _propStriking[e] = true;
                _propStrikeDepth[e] = 0;
                _propOmega[e] = wRun;
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
            double idleShare = (wIdle / wMax) * (wIdle / wMax);
            double share = driving ? Math.Max(throttle, idleShare) : 0;
            double qEngine = qRated * share;
            double wFree = Math.Max(wIdle, wRun);
            double qAir = qEngine * (_propOmega[e] / wFree) * (_propOmega[e] / wFree);
            double omega = _propOmega[e];
            double qContact = muN * _propRadius[e];
            double sweep = omega * dt;
            bool governing = driving && throttle > 0.3;   // a constant-speed prop's governor holds it above ~30 % power

            for (int k = 0; k < nb; k++)
            {
                int b = e * nb + k;
                double reach = Reach(b), gap = _propRadius[e] - reach;
                double dk = dMax - gap, ds = dSolid - gap, dw = dWater - gap;
                if (dw > 0) qContact += WaterBladeTorque(reach, dw, omega, 1, WetSpan(b, dw));
                if (dk <= 0) continue;   // this blade passes over what its longer neighbour hits
                double arc = Math.Acos(Math.Clamp(1.0 - dk / Math.Max(reach, 1e-6), -1.0, 1.0));
                double start = (_propPhase[e] + k * 2 * Math.PI / nb + arc) % (2 * Math.PI);
                if (start < 0) start += 2 * Math.PI;
                bool inside = start <= 2 * arc;
                int entries = (int)Math.Floor((start + sweep) / (2 * Math.PI));
                if (onset && inside) entries++;
                if (!inside && entries == 0) continue;
                ContactPoint? where = ds > 0 ? solidAt : waterAt;
                for (int c = 0; c < entries; c++)
                {
                    // The c-th time this step the blade's angle comes round to the edge of the struck arc (or now, if the
                    // surface came up to a blade already inside it).
                    double tIn = onset && inside && c == entries - 1 ? 0.0 : (2 * Math.PI * (c + 1) - start) / Math.Max(omega, 1e-6);
                    _bladeStrikes[b]++;
                    _strikeBuf.Add((t0 + Math.Min(tIn, dt), k, where, omega * reach, dk));
                }
                StrikeBlade(e, k, ds, dw, omega, governing, where);
            }
            _propPhase[e] = (_propPhase[e] + sweep) % (2 * Math.PI);
            // The passes in the order they happened (blade by blade above), so whatever plays them can time each from the first.
            if (_strikeBuf.Count > 1) _strikeBuf.Sort((x, y) => x.t.CompareTo(y.t));
            foreach ((double t, int k, ContactPoint? where, double tip, double depth) in _strikeBuf)
                Raise(ContactEventKind.PropBladeStrike, "prop-blade", AirframeComponent.Propeller, where, tip, depth, k, e, t);
            _strikeBuf.Clear();

            _propOmega[e] = Math.Min(wRun, _propOmega[e] + (qEngine - qAir - qContact) / inertia * dt);
            if (_propOmega[e] < EngineStallFractionOfIdle * wIdle)
            {
                _propStopped[e] = true;
                _propStriking[e] = false;
                SettleDisc(e);   // the stopped prop shows what its blades took
                Raise(ContactEventKind.PropStoppage, "prop-tip", AirframeComponent.Propeller, at, qContact, rpm);
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

    /// <summary>One pass of blade <paramref name="k"/> through ground <paramref name="solidDepth"/> or water
    /// <paramref name="waterDepth"/> deep at its own reach: the span that fails, and what the material does with it.</summary>
    private void StrikeBlade(int e, int k, double solidDepth, double waterDepth, double omega, bool governing, ContactPoint? where)
    {
        int b = e * _bladeN + k;
        double reach = Reach(b);
        bool brittle = _propMaterial != PropMaterial.Metal;
        double lose = Math.Max(0, solidDepth);
        if (waterDepth > 0)
        {
            double fail = WaterBladeFailDepth(reach, omega, _propMaterial);
            // Metal yields only as far as the load overloads it; a brittle blade, once broken, loses the whole wetted span.
            if (waterDepth > fail) lose = Math.Max(lose, brittle ? waterDepth : waterDepth - fail);
        }
        if (lose > 0) DamageBlade(e, k, reach - lose, omega, governing, where, raise: true);
    }

    /// <summary>Blade <paramref name="k"/>'s straight span cut back to <paramref name="aero"/> (no further than 0.3 R): a metal
    /// blade curls the rest by the sign of its thrust now, a wooden or composite one loses it.</summary>
    private void DamageBlade(int e, int k, double aero, double omega, bool governing, ContactPoint? where, bool raise)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc) return;
        int b = e * _bladeN + k;
        double r0 = _propRadius0[e];
        aero = Math.Max(0.3 * r0, aero);
        if (aero >= _bladeAero[b] - 1e-6) return;
        double before = _bladeAero[b];
        _bladeAero[b] = aero;
        _bladeBent[b] = r0 - aero;
        _propDamaged[e] = true;
        if (_propMaterial == PropMaterial.Metal)
        {
            double vAxial = State.Velocity.X;
            _bladeCurl[b] = PropBladeDamage.BladeAlphaRad(pc, aero, omega, vAxial, governing) >= 0 ? 1 : -1;
            if (raise) Raise(ContactEventKind.PropBladeBent, "prop-blade", AirframeComponent.Propeller, where, _bladeCurl[b], _bladeBent[b], k, e, Atmosphere.SimTimeSec);
        }
        else
        {
            _bladeBroken[b] = true;
            _bladeCurl[b] = 0;
            if (!raise) return;
            double mBlade = PropBladeDamage.BladeMassKg(r0, _propMaterial);
            double piece = mBlade * (PropBladeDamage.Outboard(aero / r0).fraction - PropBladeDamage.Outboard(before / r0).fraction);
            // The piece leaves with its tip's speed: across the bottom of the disc, opposite to the rotation (a right-hand prop
            // seen from behind turns clockwise, so its lowest blade moves to the left), on top of the aeroplane's own.
            Vec3 tipBody = new(0, -(pc.RotationSign >= 0 ? 1 : -1) * omega * 0.5 * (aero + before), 0);
            Vec3 vel = State.Attitude.Rotate(State.Velocity + tipBody);
            Vec3 pos = where?.WorldPos ?? State.Position;
            Contact?.Invoke(new ContactEvent(ContactEventKind.PropBladeBroken, "prop-blade", AirframeComponent.Propeller,
                where?.SurfaceCode ?? ContactPoint.SurfaceNone, piece, before - aero, pos, vel, k, e, _propMaterial, Atmosphere.SimTimeSec));
        }
    }

    /// <summary>How much of blade <paramref name="b"/> is under water when its tip is <paramref name="depth"/> in, m along the
    /// blade: a curl is an arc (<see cref="PropBladeDamage.MetalCurlRad"/>) and all of it below the waterline drags, so a
    /// curled tip drags about as much as the straight one did.</summary>
    private double WetSpan(int b, double depth)
    {
        double l = _bladeBent[b];
        if (_bladeBroken[b] || l <= 0) return depth;
        double aero = _bladeAero[b], rw = Reach(b) - depth, rho = l / PropBladeDamage.MetalCurlRad;
        double sw = rw > aero ? rho * Math.Asin(Math.Clamp((rw - aero) / rho, 0, 1)) : 0;
        return Math.Max(0, aero - rw) + Math.Max(0, l - sw);
    }

    /// <summary>The disc stations go to the longest blade's reach (between strikes: moving them mid-strike would move the
    /// contact it is measuring).</summary>
    private void SettleDisc(int e)
    {
        double rMax = 0;
        for (int k = 0; k < _bladeN; k++) rMax = Math.Max(rMax, Reach(e * _bladeN + k));
        if (rMax > 0 && Math.Abs(rMax - _propRadius[e]) > 1e-9) MoveDisc(e, rMax);
    }

    /// <summary>The rotor of a struck prop while the strike lasts, rpm (the engine's own rpm otherwise).</summary>
    public double StrikeRotorRpm(int i = 0) => i < _propOmega.Length && _propStriking[i] ? _propOmega[i] * 30.0 / Math.PI : FreeEngineRpm;

    /// <summary>A piston engine slowed below this share of its idle rpm cannot keep firing: the starter cranks one at
    /// 150–300 rpm and none runs steadily much below idle (the fleet's idles are 550–800 rpm), so half of idle.</summary>
    public const double EngineStallFractionOfIdle = 0.5;

    /// <summary>Blade geometry the configs do not carry, typical of the fleet's props: two blades by default (the configs
    /// say how many), a tip chord of 0.12 R (11-12 cm on a 76-80 in prop), and an in-plane force coefficient of 0.2 — a thin
    /// blade at the tip's ~6° incidence in water (normal force 2π·sin 6° ≈ 0.7, <see cref="WaterBladeNormalCoeff"/>)
    /// resolved into the plane of rotation through its ~18° blade angle.</summary>
    public const int WaterBladeCount = 2;
    public const double WaterBladeTipChordOverR = PropBladeDamage.TipChordOverR, WaterBladeInPlaneCoeff = 0.2, WaterBladeNormalCoeff = 0.7;

    /// <summary>The section a blade fails at, as a fraction of R (see <see cref="PropBladeDamage.Section"/>).</summary>
    public const double BladeBendStationOverR = PropBladeDamage.BendStationOverR;

    /// <summary>The flatwise bending moment a metal blade's section at <see cref="BladeBendStationOverR"/> yields at, N·m
    /// (162 N·m on the J-3's 0.915 m blade, 205 on an SR22's 0.99 m).</summary>
    public static double BladeYieldMomentNm(double radius) => PropBladeDamage.FailMomentNm(radius, PropMaterial.Metal);

    /// <summary>The normal (bending) force on a blade tip dipped <paramref name="depth"/> into water at ω, N: the wetted
    /// tip ⅔·d by its chord at ½ρ_w(ωR)², normal coefficient <see cref="WaterBladeNormalCoeff"/>.</summary>
    public static double WaterBladeNormalForce(double radius, double depth, double omega)
    {
        if (depth <= 0 || omega <= 0 || radius <= 0) return 0;
        double u = omega * radius, wet = 2.0 / 3.0 * Math.Min(depth, radius);
        return 0.5 * FloatHydro.WaterDensity * u * u * WaterBladeTipChordOverR * radius * wet * WaterBladeNormalCoeff;
    }

    /// <summary>How deep a metal blade tip can go into water at ω before the load bends it (<see cref="WaterBladeFailDepth"/>).</summary>
    public static double WaterBladeYieldDepth(double radius, double omega) => WaterBladeFailDepth(radius, omega, PropMaterial.Metal);

    /// <summary>How deep a blade tip can go into water at ω before the load fails its section (yields metal, breaks wood or
    /// composite): the depth whose tip force, at its arm from the bend station, is the section's failing moment
    /// (<see cref="PropBladeDamage.FailMomentNm"/>). Bisection (the moment is monotonic in depth over the outer quarter).</summary>
    public static double WaterBladeFailDepth(double radius, double omega, PropMaterial material)
    {
        double my = PropBladeDamage.FailMomentNm(radius, material), rb = BladeBendStationOverR * radius, lo = 0, hi = radius - rb;
        double Moment(double d) => WaterBladeNormalForce(radius, d, omega) * Math.Max(0.0, radius - d / 3.0 - rb);
        if (Moment(hi) <= my) return hi;
        for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (Moment(m) > my) hi = m; else lo = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>The mean torque the water puts on <paramref name="blades"/> blades of radius R whose lowest tip is
    /// <paramref name="depth"/> under the surface, turning at ω. Each blade spends acos(1 − d/R)/π of a turn in the wet arc,
    /// with on average ⅔·d of its tip under (a parabolic segment), moving at ωR through water of ρ_w:
    /// Q = n·(arc/π)·½ρ_w(ωR)²·c·(⅔d)·C·(R − ⅓d). On an SR22's 0.99 m blades at 2700 rpm (its engine makes ~820 N·m
    /// there) a 5 mm brush costs ~200 N·m for two blades and a 2 cm dip ~1,560; at idle the same 2 cm costs ~105.</summary>
    public static double WaterBladeTorque(double radius, double depth, double omega, int blades = WaterBladeCount) => WaterBladeTorque(radius, depth, omega, blades, depth);

    /// <summary>The same with the blade's own wetted span along it, m, which a curled tip makes longer than the depth.</summary>
    public static double WaterBladeTorque(double radius, double depth, double omega, int blades, double span)
    {
        if (depth <= 0 || omega <= 0 || radius <= 0) return 0;
        double d = Math.Min(depth, radius);
        double duty = Math.Acos(1.0 - d / radius) / Math.PI;
        double wet = 2.0 / 3.0 * Math.Min(Math.Max(span, d), radius), u = omega * radius;
        double force = 0.5 * FloatHydro.WaterDensity * u * u * WaterBladeTipChordOverR * radius * wet * WaterBladeInPlaneCoeff;
        return blades * duty * force * (radius - 0.5 * wet);
    }

    /// <summary>Every blade cut back to no more than <paramref name="r1"/> (a branch chews them all alike: metal curls,
    /// wood and composite splinter), then the disc follows the blades.</summary>
    private void ShrinkDisc(int e, double r1)
    {
        for (int k = 0; k < _bladeN; k++)
        {
            int b = e * _bladeN + k;
            if (b < _bladeAero.Length && Reach(b) > r1) DamageBlade(e, k, _bladeAero[b] - (Reach(b) - r1), FreeEngineRpm * Math.PI / 30.0, ShaftPower01 > 0.3, null, raise: false);
        }
        if (!_propStriking[e]) SettleDisc(e);
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

    private void Raise(ContactEventKind kind, string part, AirframeComponent comp, ContactPoint? at, double intensity, double value, int blade, int prop, double timeSec)
    {
        Vec3 pos = at?.WorldPos ?? State.Position, vel = at?.WorldVel ?? State.Attitude.Rotate(State.Velocity);
        Contact?.Invoke(new ContactEvent(kind, part, comp, at?.SurfaceCode ?? ContactPoint.SurfaceNone, intensity, value, pos, vel, blade, prop, _propMaterial, timeSec));
    }
}

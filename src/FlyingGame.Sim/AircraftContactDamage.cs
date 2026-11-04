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
    private bool[] _panelTouching = Array.Empty<bool>(), _bentThisStrike = Array.Empty<bool>();
    private int _stripTotal;
    private SurfaceConfig?[] _stripSurface = Array.Empty<SurfaceConfig?>();
    private StripConfig?[] _stripAt = Array.Empty<StripConfig?>();

    // Propellers (one per engine; one for a single-engine type).
    private int[] _propPatch = Array.Empty<int>();
    private double[] _propRadius0 = Array.Empty<double>(), _propRadius = Array.Empty<double>(), _propOmega = Array.Empty<double>(), _propStrikeDepth = Array.Empty<double>();
    private double[] _propThrustScale = Array.Empty<double>();
    private bool[] _propStriking = Array.Empty<bool>(), _propStopped = Array.Empty<bool>();

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
    /// <summary>Blade-tip radius of propeller <paramref name="i"/> now, m (shortened by a strike).</summary>
    public double PropRadiusM(int i = 0) => i < _propRadius.Length ? _propRadius[i] : 0.0;
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
        _panelTouching = new bool[_panels.Count];
        _bentThisStrike = new bool[_panels.Count];
        foreach (ContactPoint p in _contacts) if (p.Panel >= 0) _straightBody[p] = p.Body;
        if (_panels.Count > 0)
        {
            // The structure scale from a model of its own: building the aeroplane's Hydro here would arm the water
            // model on every aeroplane from its first step (it is created on first use, near water or on a break).
            WingStructure.EnsureStrength(_panels, Config, AirframeHydroModel.Build(Config, Load).StructureScale);
            foreach (ContactPoint p in _contacts)
                if (p.Panel >= 0 && _panels[p.Panel].TipStiffnessNPerM > 0)
                    p.StiffnessScale = _panels[p.Panel].TipStiffnessNPerM / (AirframeContact.SpringNPerM + _panels[p.Panel].TipStiffnessNPerM);
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
            if (l.SlavedRows.All(StripGone)) continue;     // the slaved aileron itself has gone
            if (l.Connected)
            {
                double driven = l.DrivenSign * (_aileronRad + Config.Rig.AileronRad);
                WingPanel? pd = PanelFor(l.DrivenSurface, l.Side), ps = PanelFor(l.SlavedSurface, l.Side);
                Func<Vec3, Vec3>? bd = pd is { State: PanelState.Bent } ? v => pd.Bend(v, pd.BendRad) : null;
                Func<Vec3, Vec3>? bs = ps is { State: PanelState.Bent } ? v => ps.Bend(v, ps.BendRad) : null;
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
                foreach (int r in l.SlavedRows)
                {
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
            foreach (int r in l.SlavedRows) ovr[r] = axisVal;
            WingPanel? sp = PanelFor(l.SlavedSurface, l.Side);
            if (sp is not null) foreach (int r in sp.AileronFixed) if (!_deadAileronRows.Contains(r)) ovr[r] = axisVal;
        }
    }

    // ---- wing panels -------------------------------------------------------------------------------------------------

    /// <summary>Strike loads on each panel's station, from the contact forces recorded on the step's first stage, and the
    /// bent-panel law in flight.</summary>
    private void CheckPanels()
    {
        if (_panels.Count == 0) return;
        bool any = false;
        foreach (ContactPoint p in _contacts) if (p.Panel >= 0 && p.ForceBody.LengthSquared > 0) { any = true; break; }
        if (!any && !_panels.Any(q => q.State == PanelState.Bent))
        {
            Array.Clear(_panelTouching, 0, _panelTouching.Length); Array.Clear(_bentThisStrike, 0, _bentThisStrike.Length);
            foreach (WingPanel q in _panels) q.ElasticRad = 0.0;
            return;
        }
        WingStructure.EnsureStrength(_panels, Config, Hydro.Model.StructureScale);
        for (int k = 0; k < _panels.Count; k++)
        {
            WingPanel w = _panels[k];
            if (w.State == PanelState.Broken) continue;
            double mx = 0, fMax = 0; bool touched = false;
            ContactPoint? at = null;
            foreach (ContactPoint p in _contacts)
            {
                if (p.Panel != k || p.ForceBody.LengthSquared <= 0) continue;
                // Moment about the station's axis (along x through y = ±BreakY, z = BreakZ).
                Vec3 r = p.Body - new Vec3(p.Body.X, w.Side * w.BreakY, w.BreakZ);
                mx += r.Y * p.ForceBody.Z - r.Z * p.ForceBody.Y;
                touched = true;
                if (p.ForceBody.Length > fMax) { fMax = p.ForceBody.Length; at = p; }
            }
            double up = -w.Side * mx;   // + = bending the tip UP
            // The flex the contact force puts in it: F/k_w at the tip, as the tip slope of a cantilever.
            double len = w.TipY - w.BreakY;
            w.ElasticRad = touched && w.TipStiffnessNPerM > 0 && len > 0 ? 1.5 * (up / len) / w.TipStiffnessNPerM / len : 0.0;
            if (!touched) { _panelTouching[k] = false; _bentThisStrike[k] = false; }
            else
            {
                _panelTouching[k] = true;
                w.PeakContactNm = Math.Max(w.PeakContactNm, Math.Abs(up));
            }
            if (w.State == PanelState.Intact || _bentThisStrike[k])
            {
                if (!touched) continue;
                if (Math.Abs(up) > w.UltimateNm) { BreakPanel(k, Math.Abs(up) / w.UltimateNm, at); continue; }
                if (w.State == PanelState.Intact && Math.Abs(up) > w.LimitNm)
                {
                    // Past the limit, short of ultimate: a permanent set (the crunch). For the rest of THIS strike the spar
                    // is still the one that was struck; the weakened law starts with the next.
                    w.State = PanelState.Bent;
                    _bentThisStrike[k] = true;
                    SetBend(k, Math.Sign(up) * WingStructure.BentSetRad);
                    Raise(ContactEventKind.PanelBent, at?.Name ?? w.Component.ToString(), w.Component, at, Math.Abs(up) / w.UltimateNm, WingStructure.BentSetRad * 180 / Math.PI);
                }
                continue;
            }
            // BENT: the flight load (and a new strike in the sense that opens the bend) takes it along 5° at 1 g → 15° at 2 g.
            // The flight load is the LIFT's (AeroLoadFactorZ): the whole-aeroplane load factor also counts the ground
            // decelerating the fuselage, which bends no wing — a −3 g thump of the cabin onto the ground snapped bent panels
            // that had not touched anything. Contact on the panel itself is nContact, static weight included (Ty
            // 2026-11-03, decision 1: static load counts).
            double sgn = Math.Sign(w.BendRad);
            double nFlight = AeroLoadFactorZ * sgn;
            double nContact = touched ? up * sgn / w.M1Nm : 0.0;
            if (touched && -up * sgn > WingStructure.SnapLoadFactor * w.M1Nm) { BreakPanel(k, -up * sgn / w.UltimateNm, at); continue; }   // a cracked spar knocked back the other way
            double th = WingStructure.BentDeflection(Math.Max(nFlight, nContact), w.BendRad);
            if (th > WingStructure.SnapRad) { BreakPanel(k, th * 180 / Math.PI, at); continue; }
            if (th > Math.Abs(w.BendRad) + 1e-9) SetBend(k, sgn * th);
        }
    }

    /// <summary>Bend a wing panel NOW, as a medium strike would (scenarios, lessons, tests): <paramref name="rad"/> + = tip up,
    /// the 5° set by default. False if the type has no such panel or it has already gone.</summary>
    public bool BendPanel(AirframeComponent comp, double rad = WingStructure.BentSetRad)
    {
        int k = _panels.FindIndex(p => p.Component == comp);
        if (k < 0 || _panels[k].State == PanelState.Broken) return false;
        _panels[k].State = PanelState.Bent;
        SetBend(k, rad);
        return true;
    }

    /// <summary>Bend panel <paramref name="k"/> to <paramref name="rad"/>: its hard points and its rows' aero follow.</summary>
    private void SetBend(int k, double rad)
    {
        WingPanel w = _panels[k];
        w.BendRad = rad;
        foreach (ContactPoint p in _contacts) if (p.Panel == k && _straightBody.TryGetValue(p, out Vec3 b0)) p.Body = w.Bend(b0, rad);
        _flowState.BendRad ??= new double[_stripTotal];
        _flowState.BendOffset ??= new Vec3[_stripTotal];
        foreach (int i in w.Strips)
        {
            Vec3 pos = _stripAt[i]!.PosVec();
            _flowState.BendRad[i] = rad;
            _flowState.BendOffset[i] = w.Bend(pos, rad) - pos;
        }
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
    /// A blade in the ground. The disc stations do not push (a blade cuts or curls); what they say is how deep the tips
    /// went and how hard the ground pressed on them (the contact model's own normal force). The ground drags each tip
    /// round with the Coulomb force of its surface, a torque μ·N·R against the engine's P/ω and the rotor's inertia
    /// (propulsion.propInertia): I·dω/dt = P/ω − μ·N·R. A brush with the ground takes a few milliseconds and a few
    /// rad/s; the tips curl back by the depth they went in (the disc shrinks to R − d, thrust at the same power by momentum
    /// theory ∝ R^(2/3)) and the engine runs on. A blade that keeps digging — a nose-over, a collapsed leg — brakes the
    /// rotor below a quarter of idle in a fraction of a second: sudden stoppage, the engine stops and the prop is bent.
    /// A blade that meets the WATER while turning stops the engine outright (a ditching).
    /// </summary>
    private void PropStrike(double dt)
    {
        if (Config.Propulsion is not { PropDiameterM: > 0 } pc) return;
        for (int e = 0; e < _propPatch.Length; e++)
        {
            if (_propStopped[e] || _noseLost) continue;
            double dMax = 0, muN = 0; bool water = false; ContactPoint? at = null;
            foreach (ContactPoint p in _contacts)
            {
                if (!p.PropDisc || p.Patch != _propPatch[e] || p.DepthM <= 0) continue;
                if (p.DepthM > dMax) { dMax = p.DepthM; at = p; }
                if (p.SurfaceCode == ContactPoint.SurfaceWater) water = true;
                else muN += (p.SurfaceCode >= 0 ? AirframeContact.SlidingMu((WorldTerrain.Surface)p.SurfaceCode) : AirframeContact.FrictionMu) * p.NormalN;
            }
            if (dMax <= 0)
            {
                if (_propStriking[e])
                {
                    // The strike is over: the tips curled back by the depth they went in.
                    _propStriking[e] = false;
                    double r1 = Math.Max(0.3 * _propRadius0[e], _propRadius[e] - _propStrikeDepth[e]);
                    ShrinkDisc(e, r1);
                }
                continue;
            }
            double rpm = EngineRpm;
            if (!_propStriking[e])
            {
                _propStriking[e] = true;
                _propStrikeDepth[e] = 0;
                _propOmega[e] = rpm * Math.PI / 30.0;
                Raise(ContactEventKind.PropStrike, "prop-tip", AirframeComponent.Propeller, at, muN * _propRadius[e], dMax);
            }
            _propStrikeDepth[e] = Math.Max(_propStrikeDepth[e], dMax);
            double inertia = pc.PropInertia > 0 ? pc.PropInertia : 1.0;
            double wIdle = Math.Max(1.0, pc.IdleRpm * Math.PI / 30.0);
            double throttle = _throttle01 * (Config.Engines.Count > e ? Config.Engines[e].ThrottleScale : 1.0);
            double qEngine = pc.MaxPowerW * throttle / Math.Max(_propOmega[e], wIdle);
            double qGround = muN * _propRadius[e];
            _propOmega[e] = Math.Min(rpm * Math.PI / 30.0, _propOmega[e] + (qEngine - qGround) / inertia * dt);
            if (water || _propOmega[e] < 0.25 * wIdle)
            {
                _propStopped[e] = true;
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

    private void ShrinkDisc(int e, double r1)
    {
        int patch = _propPatch[e];
        var disc = _contacts.Where(p => p.PropDisc && p.Patch == patch).ToList();
        if (disc.Count == 0) return;
        Vec3 hub = Vec3.Zero; foreach (ContactPoint p in disc) hub += p.Body; hub /= disc.Count;
        foreach (ContactPoint p in disc) { Vec3 d = p.Body - hub; double l = d.Length; if (l > 1e-9) p.Body = hub + d * (r1 / l); }
        _propRadius[e] = r1;
        _propThrustScale[e] = Math.Pow(r1 / _propRadius0[e], 2.0 / 3.0);
    }

    // ---- scrapes and events ------------------------------------------------------------------------------------------

    private void AfterStepContactDamage(double dt)
    {
        CheckPanels();
        PropStrike(dt);
        UpdateScrapes();
    }

    /// <summary>Which patches are sliding (on the ground or a solid, faster than <see cref="ScrapeState.SlipThresholdMs"/>),
    /// with their friction power; ScrapeStart / ScrapeStop for the ones that began or ended this step.</summary>
    private void UpdateScrapes()
    {
        _scrapeSeen.Clear();
        int patch = int.MinValue; ScrapeState? cur = null;
        foreach (ContactPoint p in _contacts)
        {
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
        foreach (string g in _scrapeGone) _scrapes.Remove(g);
    }

    private void Raise(ContactEventKind kind, string part, AirframeComponent comp, ContactPoint? at, double intensity, double value)
    {
        Vec3 pos = at?.WorldPos ?? State.Position, vel = at?.WorldVel ?? State.Attitude.Rotate(State.Velocity);
        Contact?.Invoke(new ContactEvent(kind, part, comp, at?.SurfaceCode ?? ContactPoint.SurfaceNone, intensity, value, pos, vel));
    }
}

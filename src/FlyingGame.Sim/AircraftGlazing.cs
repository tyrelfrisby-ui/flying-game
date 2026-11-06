using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Sim;

/// <summary>
/// The canopy, windshield and windows in a crash (<see cref="Glazing"/>): each pane's sensor points, after every step,
/// against the ground, the solids, the trees and the water's hatches. A pane shatters only on a DIRECT arrival of its own
/// surface; it pushes nothing, so the crash itself is exactly what it was. A shattered pane opens its water hatch (the
/// cabin floods through it), and raises <see cref="ContactEventKind.GlazingShattered"/> for the jagged remnant and the
/// shards (Bridge GlassShatter).
/// </summary>
public sealed partial class Aircraft
{
    private List<GlazingPane>? _glazing;
    private GlazingMaterial? _glazingMaterial;
    private int _hydroLogSeen;
    /// <summary>What this type's glazing is made of.</summary>
    public GlazingMaterial GlazingMaterial => _glazingMaterial ??= Glazing.MaterialFor(Config.Id);
    /// <summary>The panes (built from the config on first use unless <see cref="SetGlazing"/> gave measured ones).</summary>
    public IReadOnlyList<GlazingPane> GlazingPanes => _glazing ??= Glazing.DefaultPanes(Config, _contacts);
    /// <summary>Panes shattered so far, in order (tests, the log).</summary>
    public readonly List<GlazingPane> GlazingLog = new();

    /// <summary>Replace the estimated panes with ones measured on the drawn glass (body frame, config origin). Ignored for a
    /// type with no glazing; panes already shattered stay shattered by id.</summary>
    public void SetGlazing(IEnumerable<GlazingPane> panes)
    {
        if (GlazingMaterial == GlazingMaterial.None) return;
        var gone = new HashSet<string>();
        if (_glazing is not null) foreach (GlazingPane p in _glazing) if (p.Shattered) gone.Add(p.Id);
        _glazing = new List<GlazingPane>();
        foreach (GlazingPane p in panes)
        {
            if (p.Points.Length == 0) continue;
            if (p.Inside.Length != p.Points.Length) p.Inside = new bool[p.Points.Length];
            p.Shattered |= gone.Contains(p.Id);
            _glazing.Add(p);
        }
        _glazingReach = -1;
    }

    /// <summary>The panes against the ground and the solids after a step (the trees are <see cref="GlazingStems"/>).
    /// Cheap by construction (Quest: CLAUDE.md "a shortcut with a threshold is a cliff"): one ground and water sample per
    /// pane, under its lowest point, and the per-point queries only for a pane within <see cref="PaneMarginM"/> of it; the
    /// solids only when the broadphase has any near the airframe. Once a step, not per RK4 stage: these points push nothing.</summary>
    private void StepGlazing(double dt)
    {
        var panes = GlazingPanes;
        if (panes.Count == 0) return;
        if (_hydro is not null && _hydroLogSeen < _hydro.DamageLog.Count) GlazingFromWater();
        bool any = false;
        for (int i = 0; i < panes.Count; i++) if (!panes[i].Shattered) { any = true; break; }
        if (!any) return;
        RigidBodyState s = State;
        Vec3 sp = s.Position;
        if (!double.IsFinite(sp.X) || !double.IsFinite(sp.Y) || !double.IsFinite(sp.Z)) return;
        Vec3 cg = Config.Mass.CgVec();
        if (_glazingReach < 0) { _glazingReach = 0; foreach (GlazingPane p in panes) foreach (Vec3 b in p.Points) _glazingReach = Math.Max(_glazingReach, (b - cg).Length); }
        double reach = _glazingReach;
        // The same shortcut as the hard points': well clear of the ground, only the solids can be met.
        bool groundClear = -sp.Z - reach > WorldTerrain.GroundHeightAt(sp.X, sp.Y) + AirframeContact.ClearOfGroundM;
        var nearby = WorldSolids.NearbyFor(_contacts);
        nearby.Update(sp.X - reach, sp.Y - reach, -sp.Z - reach, sp.X + reach, sp.Y + reach, -sp.Z + reach);
        bool solids = !nearby.Empty;
        if (groundClear && !solids) { foreach (GlazingPane p in panes) System.Array.Clear(p.Inside, 0, p.Inside.Length); return; }
        GlazingMaterial mat = GlazingMaterial;
        double margin = PaneMarginM + 2.0 * s.Velocity.Length * dt;
        foreach (GlazingPane p in panes)
        {
            if (p.Shattered) continue;
            int n = p.Points.Length;
            if (_glazingW.Length < n) _glazingW = new Vec3[n];
            int lowest = 0;
            for (int k = 0; k < n; k++) { _glazingW[k] = sp + s.Attitude.Rotate(p.Points[k] - cg); if (_glazingW[k].Z > _glazingW[lowest].Z) lowest = k; }
            bool nearGround = false, onWater = false;
            if (!groundClear)
            {
                Vec3 lo = _glazingW[lowest];
                double g0 = WorldTerrain.HardPointGroundAt(lo.X, lo.Y, -lo.Z);
                double? w0 = FloatHydro.WaterSurfaceAt(lo.X, lo.Y, -lo.Z);
                // On the water the hatches decide (GlazingFromWater): the water gives, the ground does not.
                onWater = w0.HasValue && w0.Value >= g0 - 0.01;
                nearGround = !onWater && -lo.Z - g0 < margin;
            }
            for (int k = 0; k < n; k++)
            {
                Vec3 w = _glazingW[k];
                double depth = double.NegativeInfinity;
                Vec3 normal = new(0, 0, -1);
                string by = "ground";
                int surface = ContactPoint.SurfaceNone;
                if (nearGround)
                {
                    double g = WorldTerrain.HardPointGroundAt(w.X, w.Y, -w.Z);
                    if (-w.Z < g)
                    {
                        normal = WorldTerrain.ContactNormalAt(w.X, w.Y, g);
                        depth = (g + w.Z) * -normal.Z;
                        surface = (int)(WorldTerrain.SurfaceOverride ?? (WorldTerrain.Active != null ? WorldTerrain.SurfaceAt(w.X, w.Y, -w.Z) : WorldTerrain.Surface.Paved));
                    }
                }
                if (solids && nearby.Penetration(w.X, w.Y, -w.Z) is { } solid && solid.depth > depth)
                { depth = solid.depth; normal = solid.normalNed; by = "solid"; surface = ContactPoint.SurfaceSolid; }
                bool inside = depth > 0;
                bool arrived = inside && !p.Inside[k];
                p.Inside[k] = inside;
                if (!arrived) continue;
                Vec3 r = p.Points[k] - cg;
                Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, r));
                double closing = -Vec3.Dot(vel, normal);
                if (Glazing.ShattersOnSurface(mat, closing)) { Shatter(p, closing, by, surface, p.Points[k], w, vel); break; }
            }
        }
    }

    /// <summary>A pane whose lowest point is further than this above the ground under it (plus twice the step's travel) is
    /// not asked point by point: the ground cannot rise that much across a pane, m.</summary>
    public const double PaneMarginM = 1.5;
    private double _glazingReach = -1;
    private Vec3[] _glazingW = new Vec3[8];

    /// <summary>The panes against the stems near the airframe this step (<see cref="StepTrees"/> has gathered them): the
    /// pane's points swept from <paramref name="pre"/> to now against each stem's radius at that height.</summary>
    private void GlazingStems(ITreeSource src, RigidBodyState pre, Vec3 cg)
    {
        var panes = GlazingPanes;
        if (panes.Count == 0 || _treesNear.Count == 0) return;
        GlazingMaterial mat = GlazingMaterial;
        foreach (GlazingPane p in panes)
        {
            if (p.Shattered) continue;
            for (int k = 0; k < p.Points.Length && !p.Shattered; k++)
            {
                Vec3 a = World(pre, p.Points[k], cg), b = World(State, p.Points[k], cg);
                for (int ti = 0; ti < _treesNear.Count && !p.Shattered; ti++)
                {
                    Forest.Tree tree = _treesNear[ti];
                    double top = src.TopOf(tree);
                    if (Math.Min(-a.Z, -b.Z) > top || Math.Max(-a.Z, -b.Z) < tree.GroundM) continue;
                    // Closest approach of the swept point to the stem's axis (in plan).
                    double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                    double u = l2 > 1e-8 ? Math.Clamp(((tree.X - a.X) * dx + (tree.Y - a.Y) * dy) / l2, 0.0, 1.0) : 1.0;
                    Vec3 q = a + (b - a) * u;
                    double up = -q.Z;
                    if (up < tree.GroundM || up > top) continue;
                    double d = TreeStructure.DiameterAt(tree, up);
                    double hx = q.X - tree.X, hy = q.Y - tree.Y, dist = Math.Sqrt(hx * hx + hy * hy);
                    if (d <= 0 || dist >= 0.5 * d) continue;
                    Vec3 r = p.Points[k] - cg;
                    Vec3 vel = State.Attitude.Rotate(State.Velocity + Vec3.Cross(State.Rates, r));
                    // The side it came from (where the point was at the step's start): at the closest approach the point may
                    // already be past the axis, and the normal from there would point the wrong way.
                    double ax = a.X - tree.X, ay = a.Y - tree.Y, la = Math.Sqrt(ax * ax + ay * ay);
                    Vec3 n = la > 1e-4 ? new Vec3(ax / la, ay / la, 0) : new Vec3(-vel.X, -vel.Y, 0).Normalized();
                    double closing = -Vec3.Dot(vel, n);
                    if (Glazing.ShattersOnStem(mat, d, closing)) Shatter(p, closing, "tree", ContactPoint.SurfaceSolid, p.Points[k], q, vel);
                }
            }
        }
    }

    /// <summary>The water's hatches that burst this step (<see cref="WaterSlam"/>: "canopy broken", "windshield broken")
    /// shatter the panes they are.</summary>
    private void GlazingFromWater()
    {
        List<string> log = Hydro.DamageLog;
        for (; _hydroLogSeen < log.Count; _hydroLogSeen++)
        {
            string why = log[_hydroLogSeen];
            foreach (GlazingPane p in GlazingPanes)
            {
                if (p.Shattered || !Glazing.HatchCovers(why, p.Kind)) continue;
                Vec3 at = p.Points.Length > 0 ? p.Points[0] : Config.Mass.CgVec();
                Vec3 r = at - Config.Mass.CgVec();
                Vec3 w = State.Position + State.Attitude.Rotate(r);
                Vec3 vel = State.Attitude.Rotate(State.Velocity + Vec3.Cross(State.Rates, r));
                Shatter(p, vel.Length, "water", ContactPoint.SurfaceWater, at, w, vel, openHatch: false);
            }
        }
    }

    private void Shatter(GlazingPane p, double closingMs, string by, int surface, Vec3 atBody, Vec3 atWorld, Vec3 velWorld, bool openHatch = true)
    {
        p.Shattered = true; p.ShatterClosingMs = closingMs; p.ShatterBy = by; p.ShatterAtBody = atBody;
        GlazingLog.Add(p);
        if (openHatch && _hydro is not null)
        {
            // The hole the water will find: the matching canopy / windshield hatch, opened as the slam would open it.
            WaterSlam.BreakHatches(_hydro.Attached, why => Glazing.OpensHatch(p.Kind, why) && Glazing.HatchCovers(why, p.Kind));
            _hydroLogSeen = _hydro.DamageLog.Count;
        }
        Contact?.Invoke(new ContactEvent(ContactEventKind.GlazingShattered, p.Id, AirframeComponent.Cabin, surface, closingMs,
            by == "tree" ? 0 : Glazing.ShatterClosingMs(GlazingMaterial), atWorld, velWorld));
    }
}

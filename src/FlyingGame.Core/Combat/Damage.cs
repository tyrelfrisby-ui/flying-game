using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Combat;

public enum DamageEvent { PartLost, FuelLeak, Fire }

/// <summary>
/// Bullet damage (owner 2026-09-10): three hits on any part and it falls off; ONE hit on a control surface and
/// that surface falls off; the INNER wing panels carry fuel — one hit streams fuel, a second sets it on fire, and a
/// burning panel fails after <see cref="FireBurnSec"/>. Ground/mid-air contact goes through AirframeContact (outer
/// panel on a tip strike, inner panel on a mid-span strike). The part loss itself is the aircraft's LoseComponent,
/// so the aero, hard points and visuals follow exactly as for a ground strike.
/// </summary>
public sealed class DamageState
{
    public const int HitsToLose = 3;
    public const double FireBurnSec = 10.0;

    private readonly Dictionary<AirframeComponent, int> _hits = new();
    private readonly Action<AirframeComponent> _lose;
    private readonly Func<AirframeComponent, bool> _isLost;

    public bool FuelLeakLeft { get; private set; }
    public bool FuelLeakRight { get; private set; }
    public bool FireLeft { get; private set; }
    public bool FireRight { get; private set; }
    public double FireLeftSec { get; private set; }
    public double FireRightSec { get; private set; }
    public int TotalHits { get; private set; }
    public event Action<AirframeComponent, DamageEvent>? Changed;

    public DamageState(Action<AirframeComponent> lose, Func<AirframeComponent, bool> isLost) { _lose = lose; _isLost = isLost; }

    public int Hits(AirframeComponent c) => _hits.TryGetValue(c, out int n) ? n : 0;

    public static bool IsControlSurface(AirframeComponent c) =>
        c is AirframeComponent.AileronLeft or AirframeComponent.AileronRight or AirframeComponent.ElevatorLeft or AirframeComponent.ElevatorRight or AirframeComponent.Rudder;
    public static bool IsInnerPanel(AirframeComponent c) => c is AirframeComponent.WingLeftInner or AirframeComponent.WingRightInner;

    /// <summary>One bullet hit on a part. Returns what it caused (None = just a hit).</summary>
    public DamageEvent? RegisterHit(AirframeComponent part)
    {
        if (_isLost(part)) return null;
        TotalHits++;
        int n = Hits(part) + 1;
        _hits[part] = n;
        if (IsControlSurface(part))
        {
            _lose(part);
            Changed?.Invoke(part, DamageEvent.PartLost);
            return DamageEvent.PartLost;
        }
        if (IsInnerPanel(part))
        {
            bool left = part == AirframeComponent.WingLeftInner;
            if (n == 1)
            {
                if (left) FuelLeakLeft = true; else FuelLeakRight = true;
                Changed?.Invoke(part, DamageEvent.FuelLeak);
                return DamageEvent.FuelLeak;
            }
            if (n == 2)
            {
                if (left) { FireLeft = true; FireLeftSec = 0; } else { FireRight = true; FireRightSec = 0; }
                Changed?.Invoke(part, DamageEvent.Fire);
                return DamageEvent.Fire;
            }
        }
        if (n >= HitsToLose)
        {
            Lose(part);
            return DamageEvent.PartLost;
        }
        return null;
    }

    private void Lose(AirframeComponent part)
    {
        if (part == AirframeComponent.WingLeftInner) { FireLeft = false; FuelLeakLeft = false; }
        if (part == AirframeComponent.WingRightInner) { FireRight = false; FuelLeakRight = false; }
        _lose(part);
        Changed?.Invoke(part, DamageEvent.PartLost);
    }

    /// <summary>A burning inner panel fails after FireBurnSec.</summary>
    public void Update(double dt)
    {
        if (FireLeft) { FireLeftSec += dt; if (FireLeftSec >= FireBurnSec && !_isLost(AirframeComponent.WingLeftInner)) Lose(AirframeComponent.WingLeftInner); }
        if (FireRight) { FireRightSec += dt; if (FireRightSec >= FireBurnSec && !_isLost(AirframeComponent.WingRightInner)) Lose(AirframeComponent.WingRightInner); }
    }

    public void Reset()
    {
        _hits.Clear(); TotalHits = 0;
        FuelLeakLeft = FuelLeakRight = FireLeft = FireRight = false; FireLeftSec = FireRightSec = 0;
    }
}

/// <summary>A body-frame axis-aligned box that bullets can hit, tagged with the part it belongs to.</summary>
public readonly struct HitVolume
{
    public readonly AirframeComponent Part; public readonly Vec3 Centre, Half;
    public HitVolume(AirframeComponent part, Vec3 centre, Vec3 half) { Part = part; Centre = centre; Half = half; }
}

/// <summary>Hit volumes from the config geometry, and a segment test against them in world space.</summary>
public static class HitVolumes
{
    public static List<HitVolume> Build(AircraftConfig c)
    {
        var v = new List<HitVolume>();
        double rBody = c.Fuselage.Crossflow?.BodyRadiusM > 0 ? c.Fuselage.Crossflow.BodyRadiusM : 0.5;
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double minTe = double.MaxValue;
        foreach (SurfaceConfig sf in c.Surfaces) foreach (StripConfig st in sf.Strips) minTe = Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
        double tailX = minTe - 0.2, noseX = tailX + len;
        (double noseCut, double tailCut) = AirframeContact.FuselageStations(c);
        double axisZ = c.Propulsion?.ThrustLineZ ?? 0.0;
        v.Add(new HitVolume(AirframeComponent.Nose, new Vec3((noseX + noseCut) / 2, 0, axisZ), new Vec3((noseX - noseCut) / 2, rBody, rBody)));
        v.Add(new HitVolume(AirframeComponent.Cabin, new Vec3((noseCut + tailCut) / 2, 0, axisZ * 0.5), new Vec3((noseCut - tailCut) / 2, rBody, rBody * 1.1)));
        v.Add(new HitVolume(AirframeComponent.TailBoom, new Vec3((tailCut + tailX) / 2, 0, 0), new Vec3((tailCut - tailX) / 2, rBody * 0.6, rBody * 0.6)));
        double semi = WingPanels.Semispan(c);
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool wing = id.Contains("wing") && !id.Contains("aileron"), aileron = id.Contains("aileron"), hstab = id == "hstab", elevator = id == "elevator";
            bool vstab = id.Contains("vstab") && !id.StartsWith("rudder"), rudder = id.StartsWith("rudder");
            if (!(wing || aileron || hstab || elevator || vstab || rudder)) continue;
            // Group strips by part and box each group (x/z extents from chord and thickness, y from the strip span).
            var groups = new Dictionary<AirframeComponent, (double x0, double x1, double y0, double y1, double z0, double z1)>();
            foreach (StripConfig st in sf.Strips)
            {
                double y = st.Pos[1];
                double w = st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;
                AirframeComponent part;
                if (wing) { var p = WingPanels.PanelOf(y, semi); part = p ?? AirframeComponent.Cabin; if (p == null) continue; }
                else if (aileron) part = y < 0 ? AirframeComponent.AileronLeft : AirframeComponent.AileronRight;
                else if (hstab) part = AirframeComponent.TailHorizontal;
                else if (elevator) part = y < 0 ? AirframeComponent.ElevatorLeft : AirframeComponent.ElevatorRight;
                else if (vstab) part = AirframeComponent.TailVertical;
                else part = AirframeComponent.Rudder;
                bool vertical = vstab || rudder;
                double thick = Math.Max(0.06, 0.12 * st.Chord);
                double x0 = st.Pos[0] - 0.75 * st.Chord, x1 = st.Pos[0] + 0.25 * st.Chord;
                double y0 = vertical ? -thick : y - w / 2, y1 = vertical ? thick : y + w / 2;
                double zc = st.Pos[2] - (vertical ? 0 : Math.Abs(y) * Math.Tan(st.DihedralRad));
                double z0 = vertical ? zc - w / 2 : zc - thick, z1 = vertical ? zc + w / 2 : zc + thick;
                if (groups.TryGetValue(part, out var g)) groups[part] = (Math.Min(g.x0, x0), Math.Max(g.x1, x1), Math.Min(g.y0, y0), Math.Max(g.y1, y1), Math.Min(g.z0, z0), Math.Max(g.z1, z1));
                else groups[part] = (x0, x1, y0, y1, z0, z1);
            }
            foreach (var kv in groups)
            {
                var g = kv.Value;
                v.Add(new HitVolume(kv.Key, new Vec3((g.x0 + g.x1) / 2, (g.y0 + g.y1) / 2, (g.z0 + g.z1) / 2), new Vec3((g.x1 - g.x0) / 2, (g.y1 - g.y0) / 2, (g.z1 - g.z0) / 2)));
            }
        }
        return v;
    }

    /// <summary>The first part a world-space segment p0→p1 passes through, or null. Volumes are in the body frame about
    /// the config origin; the state's position is the CG.</summary>
    public static AirframeComponent? Test(IReadOnlyList<HitVolume> volumes, AircraftConfig c, RigidBodyState s, Vec3 p0, Vec3 p1, Func<AirframeComponent, bool>? lost = null)
    {
        Vec3 cg = c.Mass.CgVec();
        Quat inv = s.Attitude.Conjugate();
        Vec3 a = inv.Rotate(p0 - s.Position) + cg, b = inv.Rotate(p1 - s.Position) + cg;
        Vec3 d = b - a;
        double bestT = double.MaxValue; AirframeComponent? best = null;
        foreach (HitVolume hv in volumes)
        {
            if (lost != null && lost(hv.Part)) continue;
            // Slab test on the axis-aligned box.
            double t0 = 0, t1 = 1; bool miss = false;
            for (int axis = 0; axis < 3 && !miss; axis++)
            {
                double ao = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z, dd = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                double lo = (axis == 0 ? hv.Centre.X - hv.Half.X : axis == 1 ? hv.Centre.Y - hv.Half.Y : hv.Centre.Z - hv.Half.Z);
                double hi = (axis == 0 ? hv.Centre.X + hv.Half.X : axis == 1 ? hv.Centre.Y + hv.Half.Y : hv.Centre.Z + hv.Half.Z);
                if (Math.Abs(dd) < 1e-9) { if (ao < lo || ao > hi) miss = true; continue; }
                double ta = (lo - ao) / dd, tb = (hi - ao) / dd;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
                if (t0 > t1) miss = true;
            }
            if (miss) continue;
            if (t0 < bestT) { bestT = t0; best = hv.Part; }
        }
        return best;
    }
}

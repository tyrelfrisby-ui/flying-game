using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>Where the wreck is: the water surface and the ground (or the bed under the water).</summary>
public interface IWreckWorld
{
    /// <summary>Water surface height (m, up) over (x, y) for a body at height <paramref name="up"/>, or null.</summary>
    double? WaterUpAt(double x, double y, double up);
    /// <summary>Ground or bed height (m, up).</summary>
    double GroundUpAt(double x, double y);
}

/// <summary>The game world: FloatHydro's water (which honours its test override) over WorldTerrain's ground.</summary>
public sealed class LiveWreckWorld : IWreckWorld
{
    public static readonly LiveWreckWorld Instance = new();
    public double? WaterUpAt(double x, double y, double up) => FloatHydro.WaterSurfaceAt(x, y, up);
    public double GroundUpAt(double x, double y) => WorldTerrain.GroundHeightAt(x, y);
}

/// <summary>Flat test world: water at a fixed height (or none) over flat ground.</summary>
public sealed class FlatWreckWorld : IWreckWorld
{
    public double? Water; public double Ground;
    public FlatWreckWorld(double? waterUp, double groundUp) { Water = waterUp; Ground = groundUp; }
    public double? WaterUpAt(double x, double y, double up) => Water.HasValue && Water.Value > Ground ? Water : null;
    public double GroundUpAt(double x, double y) => Ground;
}

public enum WreckPhase { InAir, Plunging, Floating, Sinking, OnBottom, OnLand }

/// <summary>A broken-off piece of airframe: its elements (with their flood state), mass, inertia and a 6-DOF state.</summary>
public sealed class WreckPiece
{
    public int Id { get; internal set; }
    public string Name { get; }
    public AirframeComponent Component { get; }
    public readonly List<HydroElement> Elements;
    public double MassKg { get; }
    /// <summary>Piece CG in the airframe body (config) frame.</summary>
    public Vec3 CgBody { get; }
    /// <summary>Principal moments about the CG, body axes (products of inertia neglected).</summary>
    public Vec3 Inertia { get; }
    public RigidBodyState State { get; internal set; }
    public WreckPhase Phase { get; internal set; } = WreckPhase.InAir;
    public bool HitWater { get; internal set; }
    public bool HitLand { get; internal set; }
    /// <summary>Depth of the CG below the water surface, m (+ under); NaN with no water under it.</summary>
    public double DepthM { get; internal set; } = double.NaN;
    /// <summary>Height of the lowest point above the ground (land) or the bed, m (− = pressed in).</summary>
    public double ClearanceM { get; internal set; }
    public double BuoyancyN { get; internal set; }
    public double AirEscapeM3s { get; internal set; }
    public bool Sinking { get; internal set; }
    public bool Asleep { get; internal set; }
    public double TrappedAirM3 { get { double s = 0; foreach (HydroElement e in Elements) s += e.AirM3; return s; } }
    public double TrappedAirFreeM3 { get { double s = 0; foreach (HydroElement e in Elements) s += e.AirFreeM3; return s; } }
    /// <summary>Free air let back in since the piece was born (draining while out of the water), m^3.</summary>
    public double AdmittedFreeM3 { get { double s = 0; foreach (HydroElement e in Elements) s += e.AdmittedFreeM3; return s; } }
    public double ReserveKg => HydroBody.ReserveKg(Elements, MassKg);
    internal double EscapedForReport, ReportClock, RestClock;
    internal bool ReportedAsleep;

    public WreckPiece(string name, AirframeComponent component, List<HydroElement> elements)
    {
        Name = name; Component = component; Elements = elements;
        double mass = 0; Vec3 mx = Vec3.Zero;
        foreach (HydroElement e in elements) { mass += e.MassKg; mx += e.Centre * e.MassKg; }
        if (mass <= 1e-6)
        {
            // A piece of nothing but void (should not happen with Raymer masses): give it its envelope's worth of skin.
            foreach (HydroElement e in elements) { e.MassKg = System.Math.Max(e.MassKg, 0.01 * e.EnvelopeM3 * WaterPhysics.RhoAluminium); mass += e.MassKg; mx += e.Centre * e.MassKg; }
        }
        MassKg = mass;
        CgBody = mx / mass;
        double ixx = 0, iyy = 0, izz = 0;
        foreach (HydroElement e in elements)
        {
            Vec3 r = e.Centre - CgBody, h = e.Half;
            ixx += e.MassKg * ((h.Y * h.Y + h.Z * h.Z) / 3 + r.Y * r.Y + r.Z * r.Z);
            iyy += e.MassKg * ((h.X * h.X + h.Z * h.Z) / 3 + r.X * r.X + r.Z * r.Z);
            izz += e.MassKg * ((h.X * h.X + h.Y * h.Y) / 3 + r.X * r.X + r.Y * r.Y);
        }
        Inertia = new Vec3(System.Math.Max(ixx, 1e-3), System.Math.Max(iyy, 1e-3), System.Math.Max(izz, 1e-3));
    }

    /// <summary>Lowest point of the piece's boxes, world (z down: the largest z).</summary>
    public Vec3 LowestPoint()
    {
        Vec3 best = State.Position; double bz = double.MinValue;
        foreach (HydroElement e in Elements)
            for (int i = 0; i < 8; i++)
            {
                Vec3 corner = e.Centre - CgBody + new Vec3((i & 1) == 0 ? -e.Half.X : e.Half.X, (i & 2) == 0 ? -e.Half.Y : e.Half.Y, (i & 4) == 0 ? -e.Half.Z : e.Half.Z);
                Vec3 w = State.Position + State.Attitude.Rotate(corner);
                if (w.Z > bz) { bz = w.Z; best = w; }
            }
        return best;
    }

    public Vec3 WorldVelocity => State.Attitude.Rotate(State.Velocity);
}

/// <summary>
/// The broken-off pieces of an airframe, after they leave it: falling through the air, striking land or water,
/// plunging, floating on their trapped air, flooding and sinking — all from <see cref="HydroBody"/> and
/// <see cref="HydroElement.Flood"/>, so which piece sinks at once and which floats for a while falls out of its
/// mass, its volume and its openings, never out of its name.
///
/// Integration: fixed <see cref="Dt"/> steps, RK4 for gravity and hydrostatics, then the implicit drag/added-mass
/// update, then ground/bed contact as inelastic impulses at the box corners with Coulomb friction
/// (μ = <see cref="AirframeContact.FrictionMu"/>, the airframe's own), then flooding.
///
/// Events (raised on the caller's thread, from <see cref="Advance"/>): a <see cref="CrashImpact"/> at each piece's
/// first contact with water and its first with land (a bed under water is not an impact anyone hears); a
/// <see cref="FloodState"/> every <see cref="ReportIntervalS"/> for each piece in the water, whose air escape rate is
/// the escaped free-air volume over that interval divided by it — so the reported rates integrate exactly to the
/// trapped air lost.
///
/// Severity01 (the CrashImpact contract asks for its derivation): the normal kinetic energy of the impact as a
/// fraction of the energy that breaks the airframe, (v_n / v_break)², clamped to 0..1, with v_break =
/// <see cref="BreakSpeedMs"/> — the cabin break speed AirframeContact uses (8 m/s × the config's impact strength).
/// Mass cancels: a heavy and a light piece arriving at the same speed hit equally hard for their own structure;
/// the audio lane has MassKg to scale size by.
/// </summary>
public sealed class WreckSim
{
    public IWreckWorld World { get; set; }
    public double Dt { get; set; } = 0.005;
    public double ReportIntervalS { get; set; } = 0.1;
    /// <summary>Normal impact speed that counts as full severity, m/s.</summary>
    public double BreakSpeedMs { get; set; } = AirframeContact.CabinBreakMs;
    public double Time { get; private set; }
    public readonly List<WreckPiece> Pieces = new();
    private double _acc;
    private int _nextId;

    public WreckSim(IWreckWorld? world = null) { World = world ?? LiveWreckWorld.Instance; }

    public static double Severity(double normalSpeedMs, double breakSpeedMs) =>
        breakSpeedMs > 0 ? System.Math.Clamp(normalSpeedMs * normalSpeedMs / (breakSpeedMs * breakSpeedMs), 0.0, 1.0) : 0.0;

    /// <summary>
    /// A piece leaves the airframe. It starts exactly where its parts were, moving as they were moving
    /// (v_cg + ω × r): a break-up does not add a kick of its own.
    /// </summary>
    public WreckPiece Add(string name, AirframeComponent comp, List<HydroElement> elements, Vec3 aircraftCgBody, RigidBodyState aircraft)
    {
        var p = new WreckPiece(name, comp, elements) { Id = _nextId++ };
        Vec3 r = p.CgBody - aircraftCgBody;
        Vec3 pos = aircraft.Position + aircraft.Attitude.Rotate(r);
        Vec3 vBody = aircraft.Velocity + Vec3.Cross(aircraft.Rates, r);
        p.State = new RigidBodyState(pos, aircraft.Attitude, vBody, aircraft.Rates);
        Pieces.Add(p);
        return p;
    }

    /// <summary>Advance real time: as many fixed steps as fit, capped at 0.25 s per call so a stalled frame cannot
    /// make the wreck run away.</summary>
    public void Advance(double dt)
    {
        _acc += System.Math.Min(dt, 0.25);
        while (_acc >= Dt) { _acc -= Dt; StepOnce(); }
    }

    public void StepOnce()
    {
        Time += Dt;
        foreach (WreckPiece p in Pieces) Step(p, Dt);
    }

    private void Step(WreckPiece p, double dt)
    {
        RigidBodyState s = p.State;
        double groundUp = World.GroundUpAt(s.Position.X, s.Position.Y);
        double? wq = World.WaterUpAt(s.Position.X, s.Position.Y, -s.Position.Z);
        double waterUp = wq.HasValue && wq.Value > groundUp ? wq.Value : double.NaN;
        bool water = !double.IsNaN(waterUp);
        if (p.Asleep)
        {
            Report(p, water, waterUp, dt);
            return;
        }

        var mp = new MassProperties(p.MassKg, p.Inertia.X, p.Inertia.Y, p.Inertia.Z, 0);
        WaterCoeffs before = HydroBody.Coefficients(p.Elements, p.CgBody, s, waterUp, airDrag: true);
        Vec3 gWorld = new(0, 0, p.MassKg * WaterPhysics.G);
        (Vec3, Vec3) Forces(RigidBodyState st)
        {
            (Vec3 f, Vec3 m) = HydroBody.Hydrostatics(p.Elements, p.CgBody, st, waterUp);
            return (f + st.Attitude.Conjugate().Rotate(gWorld), m);
        }
        RigidBodyState after = RigidBody6DOF.IntegrateRK4(s, dt, mp, Forces);
        WaterCoeffs now = HydroBody.Coefficients(p.Elements, p.CgBody, after, waterUp, airDrag: true);
        s = HydroBody.Correct(s, after, p.MassKg, p.Inertia, before, now, dt);

        // First touch of the water: the lowest point has gone below the surface.
        Vec3 low = LowestPointOf(p, s);
        if (water && !p.HitWater && low.Z + waterUp > 0)
        {
            p.HitWater = true;
            Vec3 vLow = PointVelocity(p, s, low);
            Raise(p, ImpactSurface.Water, new Vec3(low.X, low.Y, -waterUp), s, System.Math.Max(0.0, vLow.Z));
        }

        // Ground / bed.
        bool onGround = Contact(p, ref s, groundUp, out double closing, out Vec3 contactAt);
        // Resting on the ground the contact comes and goes step to step by a hair; within 2 cm counts as on it.
        bool resting = onGround || (-LowestPointOf(p, s).Z) - groundUp < 0.02;
        if (onGround && !p.HitLand && !water)
        {
            p.HitLand = true;
            Raise(p, ImpactSurface.Land, contactAt, s, closing);
        }
        p.State = s;

        // The piece's own arrival can burst its skin too (the same pressure rule as the airframe's).
        if (water) WaterSlam.Damage(p.Elements, p.CgBody, s, waterUp);
        foreach (HydroElement e in p.Elements) e.Flood(p.CgBody, s, waterUp, dt);

        // Bookkeeping.
        p.DepthM = water ? s.Position.Z + waterUp : double.NaN;
        Vec3 lowNow = LowestPointOf(p, s);
        p.ClearanceM = (-lowNow.Z) - groundUp;
        p.BuoyancyN = BuoyancyN(p, s, waterUp);
        p.Sinking = water && p.HitWater && p.ReserveKg < 0;
        double speed = s.Attitude.Rotate(s.Velocity).Length;
        bool wet = water && p.HitWater;
        p.Phase = resting ? (wet ? WreckPhase.OnBottom : WreckPhase.OnLand)
            : !wet ? WreckPhase.InAir
            : p.Sinking ? WreckPhase.Sinking
            : p.BuoyancyN >= p.MassKg * WaterPhysics.G * 0.999 && System.Math.Abs(p.WorldVelocity.Z) < 0.2 ? WreckPhase.Floating
            : WreckPhase.Plunging;
        // Sleep once at rest on land, or on the bed with nothing left to flood.
        bool settled = resting && speed < 0.05 && s.Rates.Length < 0.05;
        p.RestClock = settled ? p.RestClock + dt : 0;
        if (p.RestClock > 1.0 && (!wet || p.TrappedAirFreeM3 < 1e-6 || p.Elements.All(e => e.LastFlowM3s == 0))) p.Asleep = true;
        Report(p, water, waterUp, dt);
    }

    private void Report(WreckPiece p, bool water, double waterUp, double dt)
    {
        foreach (HydroElement e in p.Elements) p.EscapedForReport += e.TakeEscaped();
        p.ReportClock += dt;
        if (p.ReportClock + 1e-9 < ReportIntervalS) return;
        double interval = p.ReportClock;
        p.AirEscapeM3s = p.EscapedForReport / interval;
        if (water && p.HitWater && !(p.Asleep && p.EscapedForReport == 0 && p.ReportedAsleep))
        {
            CrashEvents.RaiseFlood(new FloodState(p.Id, p.State.Position, p.State.Position.Z + waterUp, p.AirEscapeM3s, p.TrappedAirFreeM3, p.Sinking));
            if (p.Asleep) p.ReportedAsleep = true;
        }
        p.EscapedForReport = 0;
        p.ReportClock = 0;
    }

    private void Raise(WreckPiece p, ImpactSurface surface, Vec3 at, RigidBodyState s, double normalSpeed)
    {
        CrashEvents.RaiseImpact(new CrashImpact(surface, at, s.Attitude.Rotate(s.Velocity), normalSpeed, p.MassKg,
            Severity(normalSpeed, BreakSpeedMs), p.Id));
    }

    private static Vec3 LowestPointOf(WreckPiece p, RigidBodyState s)
    {
        RigidBodyState keep = p.State; p.State = s;
        Vec3 low = p.LowestPoint();
        p.State = keep;
        return low;
    }

    private static Vec3 PointVelocity(WreckPiece p, RigidBodyState s, Vec3 world)
    {
        Vec3 rBody = s.Attitude.Conjugate().Rotate(world - s.Position);
        return s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
    }

    private static double BuoyancyN(WreckPiece p, RigidBodyState s, double waterUp)
    {
        if (double.IsNaN(waterUp)) return 0;
        double b = 0;
        foreach (HydroElement e in p.Elements)
        {
            if (e.BuoyancyElsewhere) continue;
            double depth = s.Position.Z + s.Attitude.Rotate(e.Centre - p.CgBody).Z + waterUp;
            b += WaterPhysics.RhoWater * WaterPhysics.G * e.EnvelopeM3 * HydroElement.SubmergedFraction(depth, e.VerticalHalf(s.Attitude));
        }
        return b;
    }

    /// <summary>
    /// Inelastic contact of the box corners with the ground: each corner below the surface and moving into it gets
    /// the impulse that stops its normal motion (through the piece's mass and inertia), and a Coulomb friction impulse
    /// up to μ times that; then the piece is lifted out by the deepest penetration. Four passes settle a resting box.
    /// </summary>
    private static bool Contact(WreckPiece p, ref RigidBodyState s, double groundUp, out double maxClosing, out Vec3 contactAt)
    {
        maxClosing = 0; contactAt = s.Position;
        double deepest = 0;
        Quat q = s.Attitude, qi = q.Conjugate();
        Vec3 vW = q.Rotate(s.Velocity), wW = q.Rotate(s.Rates);
        double mu = AirframeContact.FrictionMu;
        bool any = false;
        var corners = new List<Vec3>(8 * p.Elements.Count);
        foreach (HydroElement e in p.Elements)
            for (int i = 0; i < 8; i++)
                corners.Add(q.Rotate(e.Centre - p.CgBody + new Vec3((i & 1) == 0 ? -e.Half.X : e.Half.X, (i & 2) == 0 ? -e.Half.Y : e.Half.Y, (i & 4) == 0 ? -e.Half.Z : e.Half.Z)));
        Vec3 InvI(Vec3 tW) { Vec3 tb = qi.Rotate(tW); return q.Rotate(new Vec3(tb.X / p.Inertia.X, tb.Y / p.Inertia.Y, tb.Z / p.Inertia.Z)); }
        for (int pass = 0; pass < 4; pass++)
        {
            foreach (Vec3 r in corners)
            {
                double pen = (s.Position.Z + r.Z) + groundUp;   // + = below the ground
                if (pen <= 0) continue;
                any = true;
                if (pass == 0 && pen > deepest) { deepest = pen; contactAt = s.Position + r; }
                Vec3 vp = vW + Vec3.Cross(wW, r);
                double vn = vp.Z;                                   // + = into the ground
                if (vn <= 0) continue;
                if (pass == 0) maxClosing = System.Math.Max(maxClosing, vn);
                Vec3 n = new(0, 0, -1);
                Vec3 rn = Vec3.Cross(r, n);
                double kN = 1.0 / p.MassKg + Vec3.Dot(Vec3.Cross(InvI(rn), r), n);
                double jn = vn / kN;
                Vec3 J = n * jn;
                Vec3 vt = new(vp.X, vp.Y, 0);
                double vtl = vt.Length;
                if (vtl > 1e-6)
                {
                    Vec3 t = vt / vtl;
                    Vec3 rt = Vec3.Cross(r, t);
                    double kT = 1.0 / p.MassKg + Vec3.Dot(Vec3.Cross(InvI(rt), r), t);
                    double jt = System.Math.Min(vtl / kT, mu * jn);
                    J -= t * jt;
                }
                vW += J / p.MassKg;
                wW += InvI(Vec3.Cross(r, J));
            }
        }
        if (deepest > 0) s = new RigidBodyState(s.Position + new Vec3(0, 0, -deepest), q, qi.Rotate(vW), qi.Rotate(wW));
        else if (any) s = new RigidBodyState(s.Position, q, qi.Rotate(vW), qi.Rotate(wW));
        return any;
    }
}

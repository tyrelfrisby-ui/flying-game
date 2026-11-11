using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero.Wake;

/// <summary>
/// One shedding station of a wake: where the two trailing lines were when the aeroplane passed, what they carried, and
/// what has happened to them since. R and L are named for the sense of the line, not for a wing: R is the line whose
/// circulation points AFT (away from the aeroplane) along newer → older, which is the right wing's when the lift is up
/// and the left wing's inverted. World frame (NED), metres.
/// </summary>
public struct WakeStation
{
    public Vec3 R, L;
    /// <summary>Circulation at shedding and now (m²/s, magnitude; the sign is in which line is R).</summary>
    public double Gamma0, Gamma;
    public double Rc0, Rc;
    public double AgeSec;
    /// <summary>t₀ = b₀/w₀ of this pair (s; infinite when it carried no lift) and the age at which it links.</summary>
    public double T0Sec, DemiseAgeSec;
    public double B0;
    /// <summary>The prop slipstream carried between the lines: direction the excess flow blows (unit, aft), excess
    /// speed at the axis, Gaussian half-width, swirl at that half-width (+ = the prop turns clockwise seen from behind).</summary>
    public Vec3 JetDir;
    public double JetDu0, JetR0, JetSwirl0, JetDu, JetR, JetSwirl;
    /// <summary>The ground (or water) height under the station at its last advection (m, up).</summary>
    public double GroundM;

    public readonly Vec3 Mid => (R + L) * 0.5;
}

/// <summary>
/// The wake of one aeroplane: a live head at its wing tips and a ring of stations it has left behind, each pair
/// descending on its own induction, drifting with the wind, its cores spreading and, once the air has linked it, dying.
/// Fed every sim step with the aeroplane's own aerodynamic force (<see cref="Shed"/>) or, for an aeroplane that is only
/// a recorded or received pose, with the load that pose implies (<see cref="ShedFromLoadFactor"/>,
/// <see cref="ShedKinematic"/>). Created by <see cref="VortexWake.CreateSource"/>.
/// </summary>
public sealed class WakeSource
{
    public const int Capacity = 320;
    /// <summary>Seconds between stations: 0.4 s × 320 = 128 s of wake, longer than any pair here lives.</summary>
    public const double EmitIntervalSec = 0.4;
    /// <summary>Not shed for this long, the aeroplane has gone (destroyed, respawned, out of range): what it left goes on
    /// decaying with no head.</summary>
    public const double StaleSec = 1.0;
    /// <summary>Advection rate: the lines move a few centimetres in 0.1 s (w₀ is under 1 m/s for anything here).</summary>
    public const double AdvectIntervalSec = 0.1;

    public string Label { get; }
    public double SpanM { get; }
    public double B0 => WakeVortex.SpanFactor * SpanM;
    public bool Attached { get; private set; } = true;

    private readonly WakeStation[] _st = new WakeStation[Capacity];
    private int _count, _newest = -1;
    private WakeStation _head;
    private bool _hasHead;
    private double _sinceEmit = double.PositiveInfinity, _sinceShed, _advectAccum, _spacingM = 10.0;
    private Vec3 _kinPrevVel, _kinAccel;
    private bool _kinHas;
    private Vec3[] _vR = new Vec3[Capacity], _vL = new Vec3[Capacity];
    internal Vec3 BoxMin, BoxMax;

    internal WakeSource(string label, double spanM)
    {
        Label = label;
        SpanM = Math.Max(0.5, spanM);
        BoxMin = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        BoxMax = new Vec3(double.MinValue, double.MinValue, double.MinValue);
    }

    /// <summary>Stations left behind (not counting the live head).</summary>
    public int StationCount => _count;
    public bool HasHead => _hasHead;
    public WakeStation Head => _head;
    /// <summary>The k-th newest station (0 = the most recent one left behind).</summary>
    public WakeStation Station(int k) => _st[Index(k)];
    private int Index(int k) => (_newest - k + Capacity * 2) % Capacity;

    /// <summary>Node k along the wake counting the head as −1 when there is one.</summary>
    private ref WakeStation Node(int k) => ref (k < 0 ? ref _head : ref _st[Index(k)]);
    private int FirstNode => _hasHead ? -1 : 0;

    // ---------------------------------------------------------------- shedding

    /// <summary>
    /// The aeroplane's step: <paramref name="aeroForceWorld"/> is its whole aerodynamic force (N, world). The lift is the
    /// part square to the airflow AND to the span (a fuselage's side force in a knife-edge is not wing circulation);
    /// <paramref name="airVelocityWorld"/> is the aeroplane's velocity through the air.
    /// </summary>
    public void Shed(double dt, Vec3 cgWorld, Quat attitude, Vec3 airVelocityWorld, Vec3 aeroForceWorld, double rho,
        double slipDu = 0.0, double slipR = 0.0, double slipSwirl = 0.0)
    {
        _sinceShed = 0.0;
        Attached = true;
        double v = airVelocityWorld.Length;
        Vec3 vHat = v > 1e-6 ? airVelocityWorld / v : attitude.Rotate(new Vec3(1, 0, 0));
        Vec3 fPerp = aeroForceWorld - vHat * Vec3.Dot(aeroForceWorld, vHat);
        Vec3 spanAxis = attitude.Rotate(new Vec3(0, 1, 0));
        spanAxis -= vHat * Vec3.Dot(spanAxis, vHat);
        double sl = spanAxis.Length;
        spanAxis = sl > 1e-6 ? spanAxis / sl : new Vec3(0, 1, 0);
        Vec3 lift = fPerp - spanAxis * Vec3.Dot(fPerp, spanAxis);
        double liftN = lift.Length;

        double gamma = v < 3.0 ? 0.0 : WakeVortex.Circulation(liftN, rho, v, SpanM);
        // The bound vortex points along g = V̂ × L̂ (Kutta–Joukowski, F = ρ V∞ × Γ with V∞ = −V): its end at +g/2 turns
        // aft as the R line. With no lift there is no direction, and nothing is carried, so the body's span stands in.
        Vec3 g = liftN > 1e-6 ? Vec3.Cross(vHat, lift / liftN) : spanAxis;
        double b0 = B0;

        _head.R = cgWorld + g * (0.5 * b0);
        _head.L = cgWorld - g * (0.5 * b0);
        _head.Gamma0 = _head.Gamma = gamma;
        _head.Rc0 = _head.Rc = WakeVortex.CoreRadiusPerSpan * SpanM;
        _head.AgeSec = 0.0;
        _head.B0 = b0;
        double w0 = gamma / (2.0 * Math.PI * b0);
        _head.T0Sec = w0 > 1e-9 ? b0 / w0 : double.PositiveInfinity;
        double eta = w0 > 1e-9 ? Math.Cbrt(VortexWake.EddyDissipation * b0) / w0 : 0.0;
        _head.DemiseAgeSec = WakeVortex.DemiseTStar(eta) * _head.T0Sec;
        _head.JetDir = -vHat;
        _head.JetDu0 = _head.JetDu = slipDu > 0 ? slipDu : 0.0;
        _head.JetR0 = _head.JetR = slipR > 0 ? slipR : 0.0;
        _head.JetSwirl0 = _head.JetSwirl = _head.JetDu0 > 0 ? slipSwirl : 0.0;
        _head.GroundM = VortexWake.GroundUnder(cgWorld);
        _hasHead = true;
        _spacingM = Math.Max(1.0, v * EmitIntervalSec);
        Expand(_head);

        _sinceEmit += dt;
        if (_sinceEmit >= EmitIntervalSec || _count == 0)
        {
            Push(_head);
            _sinceEmit = 0.0;
        }
    }

    /// <summary>A pose with a known normal load factor (a recorded track carries it): the aero force is n·W along the
    /// body's −z, as the accelerometer measured it.</summary>
    public void ShedFromLoadFactor(double dt, Vec3 cgWorld, Quat attitude, Vec3 airVelocityWorld, double loadFactorZ,
        double weightN, double rho)
    {
        Vec3 f = attitude.Rotate(new Vec3(0, 0, -loadFactorZ * weightN));
        Shed(dt, cgWorld, attitude, airVelocityWorld, f, rho);
    }

    /// <summary>A pose and nothing else (a peer over the network): the aerodynamic force is what its acceleration says,
    /// m (a − g), the acceleration differenced from successive velocities and smoothed over
    /// <paramref name="smoothSec"/> (received states are lumpy). Thrust and drag lie along the flight path and drop out
    /// of the lift.</summary>
    public void ShedKinematic(double dt, Vec3 cgWorld, Quat attitude, Vec3 velocityWorld, Vec3 windWorld, double massKg,
        double rho, double smoothSec = 0.3)
    {
        if (dt <= 0.0) return;
        if (_kinHas)
        {
            Vec3 a = (velocityWorld - _kinPrevVel) / dt;
            double k = 1.0 - Math.Exp(-dt / Math.Max(1e-3, smoothSec));
            _kinAccel += (a - _kinAccel) * k;
        }
        _kinPrevVel = velocityWorld;
        _kinHas = true;
        Vec3 f = (_kinAccel - new Vec3(0, 0, Atmosphere.GravityMs2)) * massKg;
        Shed(dt, cgWorld, attitude, velocityWorld - windWorld, f, rho);
    }

    /// <summary>The aeroplane has gone: the wake stays, with its end where the aeroplane last was.</summary>
    public void Detach()
    {
        if (_hasHead) Push(_head);
        _hasHead = false;
        Attached = false;
        _kinHas = false;
    }

    private void Push(in WakeStation s)
    {
        _newest = (_newest + 1) % Capacity;
        _st[_newest] = s;
        if (_count < Capacity) _count++;
    }

    // ---------------------------------------------------------------- ageing and motion

    /// <summary>Called by <see cref="VortexWake.Advance"/>. Returns false when nothing is left.</summary>
    internal bool Advance(double dt)
    {
        _sinceShed += dt;
        if (Attached && _sinceShed > StaleSec) Detach();
        for (int k = 0; k < _count; k++)
        {
            ref WakeStation s = ref _st[Index(k)];
            s.AgeSec += dt;
            s.Gamma = s.Gamma0 * (double.IsInfinity(s.T0Sec) ? 0.0 : WakeVortex.CirculationFraction(s.AgeSec / s.T0Sec, s.DemiseAgeSec / s.T0Sec));
            s.Rc = WakeVortex.CoreRadius(s.Rc0, s.Gamma0, s.AgeSec);
            if (s.JetR0 > 0.0)
            {
                s.JetR = WakeVortex.JetRadius(s.JetR0, s.JetDu0, s.AgeSec);
                double q = s.JetR0 / s.JetR;
                s.JetDu = s.JetDu0 * q * q;
                s.JetSwirl = s.JetSwirl0 * q * q * q;
            }
        }
        // Drop the dead from the old end.
        while (_count > 0)
        {
            WakeStation o = _st[Index(_count - 1)];
            bool dead = o.Gamma <= WakeVortex.DeadFraction * Math.Max(o.Gamma0, 1e-9) && o.JetDu < 0.05;
            if (!dead) break;
            _count--;
        }
        _advectAccum += dt;
        if (_advectAccum >= AdvectIntervalSec)
        {
            Advect(_advectAccum);
            _advectAccum = 0.0;
        }
        return _hasHead || _count > 0;
    }

    /// <summary>
    /// Every node of each line moves with the wind and with what the OTHER line induces on it, plus, within three b₀
    /// of the ground, both lines' mirror images under it. A straight line induces nothing on itself, so its own
    /// segments are left out. The partner's segments are taken within about 6 b₀ along the wake, which is 98.6 % of an
    /// infinite line's induction.
    /// </summary>
    private void Advect(double h)
    {
        if (_count == 0) return;
        int w = Math.Clamp((int)Math.Ceiling(6.0 * B0 / _spacingM), 2, 16);
        int first = FirstNode, lastSeg = _count - 2;   // segment j joins node j (newer) and node j+1 (older)
        for (int k = 0; k < _count; k++)
        {
            ref WakeStation s = ref _st[Index(k)];
            Vec3 wind = VortexWake.WindAt(s.Mid);
            s.GroundM = VortexWake.GroundUnder(s.Mid);
            Vec3 vr = wind, vl = wind;
            if (s.Gamma > 1e-6)
            {
                bool nearGround = -s.Mid.Z - s.GroundM < 3.0 * s.B0;
                double zg = -s.GroundM;   // the ground plane in NED z
                int j0 = Math.Max(first, k - w - 1), j1 = Math.Min(lastSeg, k + w);
                for (int j = j0; j <= j1; j++)
                {
                    ref WakeStation a = ref Node(j);
                    ref WakeStation b = ref Node(j + 1);
                    double gam = 0.5 * (a.Gamma + b.Gamma), rc = 0.5 * (a.Rc + b.Rc);
                    if (gam < 1e-9) continue;
                    // R line: +Γ along newer → older; L line: −Γ.
                    vr += WakeVortex.SegmentVelocity(s.R, a.L, b.L, -gam, rc);
                    vl += WakeVortex.SegmentVelocity(s.L, a.R, b.R, gam, rc);
                    if (nearGround)
                    {
                        // Images: mirrored geometry, reversed circulation.
                        Vec3 aR = Mirror(a.R, zg), bR = Mirror(b.R, zg), aL = Mirror(a.L, zg), bL = Mirror(b.L, zg);
                        vr += WakeVortex.SegmentVelocity(s.R, aL, bL, gam, rc) + WakeVortex.SegmentVelocity(s.R, aR, bR, -gam, rc);
                        vl += WakeVortex.SegmentVelocity(s.L, aR, bR, -gam, rc) + WakeVortex.SegmentVelocity(s.L, aL, bL, gam, rc);
                    }
                }
            }
            _vR[k] = vr; _vL[k] = vl;
        }
        BoxMin = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        BoxMax = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        for (int k = 0; k < _count; k++)
        {
            ref WakeStation s = ref _st[Index(k)];
            s.R += _vR[k] * h;
            s.L += _vL[k] * h;
            KeepAbove(ref s.R, s.GroundM, s.Rc);
            KeepAbove(ref s.L, s.GroundM, s.Rc);
            Expand(s);
        }
        if (_hasHead) Expand(_head);
    }

    private static Vec3 Mirror(Vec3 p, double zg) => new(p.X, p.Y, 2.0 * zg - p.Z);

    private static void KeepAbove(ref Vec3 p, double groundM, double rc)
    {
        double floorZ = -(groundM + rc);
        if (p.Z > floorZ) p = new Vec3(p.X, p.Y, floorZ);
    }

    private void Expand(in WakeStation s)
    {
        BoxMin = new Vec3(Math.Min(BoxMin.X, Math.Min(s.R.X, s.L.X)), Math.Min(BoxMin.Y, Math.Min(s.R.Y, s.L.Y)), Math.Min(BoxMin.Z, Math.Min(s.R.Z, s.L.Z)));
        BoxMax = new Vec3(Math.Max(BoxMax.X, Math.Max(s.R.X, s.L.X)), Math.Max(BoxMax.Y, Math.Max(s.R.Y, s.L.Y)), Math.Max(BoxMax.Z, Math.Max(s.R.Z, s.L.Z)));
    }

    // ---------------------------------------------------------------- what it does to the air

    /// <summary>Adds this wake's segments within reach of a sphere (centre, radius) to the lists. Segments whose both
    /// ends are younger than <paramref name="minAgeSec"/> are left out (an aeroplane's own near wake is already in its
    /// induced drag and downwash).</summary>
    internal void Gather(Vec3 centre, double radius, double minAgeSec, List<VortexSegment>? vort, List<JetSegment>? jets)
    {
        int nodes = _count + (_hasHead ? 1 : 0);
        if (nodes < 2) return;
        double reach = radius + WakeVortex.InfluenceSpans * B0;
        if (centre.X < BoxMin.X - reach || centre.Y < BoxMin.Y - reach || centre.Z < BoxMin.Z - reach
            || centre.X > BoxMax.X + reach || centre.Y > BoxMax.Y + reach || centre.Z > BoxMax.Z + reach) return;
        int first = FirstNode;
        for (int j = first; j <= _count - 2; j++)
        {
            ref WakeStation a = ref Node(j);
            ref WakeStation b = ref Node(j + 1);
            if (b.AgeSec < minAgeSec) continue;   // b is the older end
            double gam = 0.5 * (a.Gamma + b.Gamma);
            if (vort is not null && gam > 1e-6)
            {
                double rc = 0.5 * (a.Rc + b.Rc), age = 0.5 * (a.AgeSec + b.AgeSec), infl = WakeVortex.InfluenceSpans * 0.5 * (a.B0 + b.B0);
                if (DistanceToSegment(centre, a.R, b.R) < radius + infl) vort.Add(new VortexSegment(a.R, b.R, gam, rc, age, infl));
                if (DistanceToSegment(centre, a.L, b.L) < radius + infl) vort.Add(new VortexSegment(a.L, b.L, -gam, rc, age, infl));
            }
            if (jets is not null && (a.JetDu > 0.05 || b.JetDu > 0.05))
            {
                Vec3 ma = a.Mid, mb = b.Mid;
                double r = Math.Max(a.JetR, b.JetR);
                if (DistanceToSegment(centre, ma, mb) < radius + 3.0 * r)
                    jets.Add(new JetSegment(ma, mb, a.JetDir, a.JetDu, b.JetDu, a.JetR, b.JetR, a.JetSwirl, b.JetSwirl));
            }
        }
    }

    internal static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
    {
        Vec3 ab = b - a;
        double l2 = ab.LengthSquared;
        double t = l2 > 1e-12 ? Math.Clamp(Vec3.Dot(p - a, ab) / l2, 0.0, 1.0) : 0.0;
        return (p - (a + ab * t)).Length;
    }
}

/// <summary>A straight piece of one trailing line: from A (newer) to B (older), circulation signed along A→B.</summary>
public readonly struct VortexSegment
{
    public readonly Vec3 A, B;
    public readonly double Gamma, CoreRadius, AgeSec;
    /// <summary>Beyond this distance the segment is not evaluated (m).</summary>
    public readonly double InfluenceM;
    public VortexSegment(Vec3 a, Vec3 b, double gamma, double coreRadius, double ageSec, double influenceM)
    { A = a; B = b; Gamma = gamma; CoreRadius = coreRadius; AgeSec = ageSec; InfluenceM = influenceM; }
}

/// <summary>A piece of a slipstream's axis between two stations, each end with its own excess speed, width and swirl.</summary>
public readonly struct JetSegment
{
    public readonly Vec3 A, B, Dir;
    public readonly double DuA, DuB, RA, RB, SwirlA, SwirlB;
    public JetSegment(Vec3 a, Vec3 b, Vec3 dir, double duA, double duB, double rA, double rB, double swirlA, double swirlB)
    { A = a; B = b; Dir = dir; DuA = duA; DuB = duB; RA = rA; RB = rB; SwirlA = swirlA; SwirlB = swirlB; }

    /// <summary>The slipstream's excess air velocity at <paramref name="p"/>: Gaussian axial excess along
    /// <see cref="Dir"/>, swirl peaking at the half-width, both from the nearer stations by interpolation. A point that
    /// projects outside the piece gets nothing from it (its neighbour carries it).</summary>
    public Vec3 VelocityAt(Vec3 p)
    {
        Vec3 ab = B - A;
        double l2 = ab.LengthSquared;
        if (l2 < 1e-12) return Vec3.Zero;
        double t = Vec3.Dot(p - A, ab) / l2;
        if (t < 0.0 || t >= 1.0) return Vec3.Zero;
        Vec3 off = p - (A + ab * t);
        double r = RA + (RB - RA) * t;
        if (r < 1e-3) return Vec3.Zero;
        double rho2 = off.LengthSquared / (r * r);
        if (rho2 > 9.0) return Vec3.Zero;
        double du = DuA + (DuB - DuA) * t, sw = SwirlA + (SwirlB - SwirlA) * t;
        Vec3 v = Dir * (du * Math.Exp(-rho2));
        double rho = Math.Sqrt(rho2);
        if (rho > 1e-6 && sw != 0.0)
        {
            // The prop turns about the flight direction (−Dir); + swirl is clockwise seen from behind.
            Vec3 axis = -Dir;
            Vec3 tangential = Vec3.Cross(axis, off) / (rho * r);
            v += tangential * (sw * rho * Math.Exp(0.5 * (1.0 - rho2)));
        }
        return v;
    }
}

using System;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// THE THRUST JET, TRACED (owner 2026-11-08: "the thrust jet needs to tilt with alpha and beta accurately and it needs
/// to curve with pitch and yaw rates accurately in order to determine the effects of Q and relative wind on the
/// downstream surfaces"; and, on the model it replaces, "in reality the thrust jet would bend with beta and mostly miss
/// the tail but our model probably has it locked to the body ignoring alpha/beta ... which would provide artificial
/// stability").
///
/// The propeller leaves a tube of faster air behind it. Where that tube GOES is not a property of the airframe: it is a
/// property of the air, which is why the old model — a cylinder bolted to the body x axis, tested only as
/// |y| &lt; r, with a single scalar for "how much of it still reaches the tail" — gave every tail surface the full blast
/// at any attitude and any rate. This traces the tube instead, by the same marching the wing shadow uses
/// (<see cref="TailWake.GeometricShadow"/>), so the jet's tilt with alpha and beta and its curve under pitch and yaw
/// rate come out of the geometry rather than a correlation.
///
/// WHERE IT GOES. In body axes the air at a station moves at −(V + ω×r): everything the airframe is doing, backwards.
/// Inside the jet the air carries in addition the increment the disc gave it, Δu along the prop axis, so the tube's
/// centre line is the path
///     dp/dτ = −(V + ω×(p − cg)) − Δu(s)·d
/// marched from the disc. That single term is the whole effect: at alpha or beta the first part sweeps the tube off the
/// body axis (a flat spin, V_axial ≈ 0, blows it sideways and the tail sees nothing); under pitch or yaw rate the ω×r
/// grows along the tube, which bends it.
///
/// WHICH WAY ITS AIR BLOWS. The increment is momentum, so its DIRECTION is fixed in the air, not in the airframe: a
/// vector fixed in inertial space turns at −ω in body axes, so d obeys ḋ = −ω×d along the march. At 300 deg/s and 4 m
/// of fuselage that is ~8 deg of swing between the disc and the fin — the jet strikes the tail from a different angle
/// than it left the hub, which is the "curve with pitch and yaw rates" of the quote above. The magnitude follows
/// momentum theory: ≈Δu/1.8 at the disc and the full Δu about a diameter behind it (Glauert, ARC R&amp;M 1111, 1926;
/// Leishman, <i>Principles of Helicopter Aerodynamics</i> 2nd ed. §2.14), where Δu is the sim's own
/// TailSlipstream value, unchanged.
///
/// WHAT A SURFACE GETS. Both halves of what Ty asked for, from one vector: the increment is added to the strip's own
/// relative wind, so the dynamic pressure AND the flow direction there follow from it (q rises as |v|², the incidence
/// turns by the cross component). No separate q factor, and nothing is gated on a stall: the geometry decides.
/// Every surface is eligible, wings included — the root of a Pitts' lower wing sits directly behind the disc.
///
/// The tube is one per propeller (its own hub, the same Δu: a per-engine throttle is not carried into the tube, which
/// is the fidelity the single-tube model had). Where two overlap, a station takes the stronger and not the sum.
/// </summary>
public static class PropJet
{
    private const double StepM = 0.5;          // metres of path per march step
    private const double MaxPathM = 7.0;       // past the longest tail arm in the fleet (the Extra's is 5.88 m)
    private const int MaxNodes = 16;           // enough nodes to reach MaxPathM at StepM, so the path cap is what stops it
    private const double CoreFraction = 0.5;   // the tube's uniform core, as a share of its radius
    private const double MinSpeedMs = 0.5;     // below this the tube is not going anywhere this step

    internal sealed class Tube
    {
        internal int Count;
        internal readonly Vec3[] P = new Vec3[MaxNodes];   // centre line nodes, body axes
        internal readonly Vec3[] D = new Vec3[MaxNodes];   // the increment's direction there (unit, body axes)
        internal readonly double[] R = new double[MaxNodes];
        internal readonly double[] U = new double[MaxNodes];
        internal readonly double[] S = new double[MaxNodes];   // path length from the disc
        internal Vec3 Hub, Axis;
        internal double Swirl;        // m/s at the tube edge, signed by the prop's rotation
        internal bool Any;
    }

    internal sealed class Jet
    {
        internal Tube[] Tubes = Array.Empty<Tube>();
        internal int Count;
        internal bool Any;
        internal double K = 1.0;
    }

    [ThreadStatic] private static Jet? _scratch;
    [ThreadStatic] private static Tube? _refTube;

    private sealed class Geom
    {
        internal Vec3[] Hubs = Array.Empty<Vec3>();
        internal double DiameterM;
        internal double TailArmM;          // disc to the area-weighted tail station, straight back
        internal double[] VertArea = Array.Empty<double>();
        internal Vec3[] VertAt = Array.Empty<Vec3>();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, Geom> _geom = new();

    /// <summary>The propeller hubs and the tail station, from the same fuselage construction AirframeContact uses for
    /// the nose and the prop disc (tail end = the furthest-forward trailing edge less 0.2 m, nose = that plus the
    /// fuselage length, hub 0.1 m ahead of it). The hub sits on the THRUST LINE: propulsion.thrustLineZ is an offset
    /// from the CG (+ below it), so the axis is at cg.z + thrustLineZ. (AirframeContact reads the same field as an
    /// absolute body z; the two agree on every type whose cg.z is 0, which until the Pitts' 2026-11-08 cg was the whole
    /// fleet. Noted, not changed here: moving a type's nose and prop disc is a ground-contact change.)</summary>
    private static Geom Build(AircraftConfig c)
    {
        var g = new Geom();
        if (c.Propulsion is null || c.Propulsion.PropDiameterM <= 0.0) return g;
        g.DiameterM = c.Propulsion.PropDiameterM;
        double minTe = double.MaxValue, ax = 0.0, a = 0.0;
        var vArea = new System.Collections.Generic.List<double>();
        var vAt = new System.Collections.Generic.List<Vec3>();
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            bool vertical = TailWake.IsVerticalId(sf.Id);
            bool wing = sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            foreach (StripConfig st in sf.Strips)
            {
                minTe = Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
                if (!wing) { ax += st.Area * st.Pos[0]; a += st.Area; }
                if (vertical) { vArea.Add(st.Area); vAt.Add(st.PosVec()); }
            }
        }
        double axisZ = c.Mass.CgVec().Z + c.Propulsion.ThrustLineZ;
        if (c.Engines.Count == 0)
        {
            double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
            double hubX = minTe == double.MaxValue ? 0.0 : minTe - 0.2 + len + 0.1;
            g.Hubs = new[] { new Vec3(hubX, 0.0, axisZ) };
        }
        else
        {
            var hubs = new Vec3[c.Engines.Count];
            for (int i = 0; i < c.Engines.Count; i++)
            {
                EngineMount m = c.Engines[i];
                hubs[i] = new Vec3(m.Pos[0] + 1.0, m.Pos[1], m.Pos[2]);
            }
            g.Hubs = hubs;
        }
        double hubMeanX = 0.0;
        foreach (Vec3 h in g.Hubs) hubMeanX += h.X;
        hubMeanX /= Math.Max(1, g.Hubs.Length);
        g.TailArmM = a > 0.0 ? Math.Max(0.0, hubMeanX - ax / a) : 0.0;
        g.VertArea = vArea.ToArray();
        g.VertAt = vAt.ToArray();
        return g;
    }

    /// <summary>Traces this evaluation's jet: one tube per propeller, from the disc through the air's own motion.
    /// <paramref name="deltaU"/>, <paramref name="radius"/> and <paramref name="swirl"/> are TailSlipstream's, raw —
    /// the share that reaches a surface is now the geometry's answer, not a factor applied before the trace.</summary>
    internal static Jet Prepare(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                               double deltaU, double radius, double swirl)
    {
        Jet j = _scratch ??= new Jet();
        j.Count = 0; j.Any = false;
        j.K = config.Propulsion?.JetK ?? 1.0;
        if (deltaU == 0.0 || radius <= 0.0 || j.K <= 0.0) return j;
        Geom g = _geom.GetValue(config, Build);
        if (g.Hubs.Length == 0) return j;
        if (j.Tubes.Length < g.Hubs.Length)
        {
            var grown = new Tube[g.Hubs.Length];
            for (int i = 0; i < grown.Length; i++) grown[i] = i < j.Tubes.Length ? j.Tubes[i] : new Tube();
            j.Tubes = grown;
        }
        double spread = config.Propulsion?.JetSpreadPerM ?? 0.045;
        for (int i = 0; i < g.Hubs.Length; i++)
        {
            Trace(j.Tubes[i], g.Hubs[i], new Vec3(1, 0, 0), freestream, rates, cg, deltaU, radius, swirl, g.DiameterM, spread);
            if (j.Tubes[i].Any) j.Any = true;
        }
        j.Count = g.Hubs.Length;
        return j;
    }

    private static void Trace(Tube t, Vec3 hub, Vec3 axis, Vec3 freestream, Vec3 rates, Vec3 cg,
                              double deltaU, double radius, double swirl, double diameter, double spread)
    {
        t.Count = 0; t.Any = false; t.Hub = hub; t.Axis = axis; t.Swirl = swirl;
        Vec3 p = hub, d = axis;
        double s = 0.0;
        for (int i = 0; i < MaxNodes; i++)
        {
            // Momentum theory's growth: about half the increment at the disc, all of it a diameter behind.
            double u = deltaU * (diameter > 0.0 ? 0.55 + 0.45 * Math.Min(1.0, s / diameter) : 1.0);
            t.P[i] = p; t.D[i] = d; t.R[i] = radius + spread * s; t.U[i] = u; t.S[i] = s;
            t.Count = i + 1;
            if (s >= MaxPathM) break;
            Vec3 vAir = -(freestream + Vec3.Cross(rates, p - cg)) - u * d;
            double sp = vAir.Length;
            if (sp < MinSpeedMs) break;
            double dt = StepM / sp;
            p += vAir * dt;
            Vec3 dn = d - Vec3.Cross(rates, d) * dt;    // the increment is fixed in the AIR: in body axes it turns at -omega
            double dl = dn.Length;
            if (dl > 1e-9) d = dn / dl;
            s += StepM;
        }
        t.Any = t.Count > 1;
    }

    /// <summary>The increment this station's relative wind takes from the jet, body axes: the axial part always, the
    /// swirl only for a surface that feels it as a change of incidence (the fin's share of the swirl is the propeller
    /// model's own yaw term, <c>Parts.SlipYaw</c>, which would otherwise be counted twice).</summary>
    internal static Vec3 RelWind(Jet j, Vec3 at, bool withSwirl)
    {
        if (!j.Any) return Vec3.Zero;
        Vec3 best = Vec3.Zero;
        double bestMag = 0.0;
        for (int i = 0; i < j.Count; i++)
        {
            Vec3 v = FromTube(j.Tubes[i], at, withSwirl, j.K);
            double m = v.LengthSquared;
            if (m > bestMag) { bestMag = m; best = v; }
        }
        return best;
    }

    private static Vec3 FromTube(Tube t, Vec3 at, bool withSwirl, double k)
    {
        if (!t.Any) return Vec3.Zero;
        // Nothing ahead of the disc is in the wash.
        if ((at - t.Hub).Dot(t.Axis) > 0.0) return Vec3.Zero;
        double bestDist = double.MaxValue;
        double u = 0.0, rr = 0.0;
        Vec3 d = Vec3.Zero, off = Vec3.Zero;
        for (int i = 0; i + 1 < t.Count; i++)
        {
            Vec3 a = t.P[i], ab = t.P[i + 1] - t.P[i];
            double l2 = ab.LengthSquared;
            if (l2 <= 1e-12) continue;
            double f = Math.Clamp((at - a).Dot(ab) / l2, 0.0, 1.0);
            Vec3 c = a + ab * f;
            Vec3 o = at - c;
            double dist = o.Length;
            double r = t.R[i] + (t.R[i + 1] - t.R[i]) * f;
            if (dist >= r || dist >= bestDist) continue;
            bestDist = dist; rr = r; off = o;
            u = t.U[i] + (t.U[i + 1] - t.U[i]) * f;
            d = t.D[i] + (t.D[i + 1] - t.D[i]) * f;
        }
        if (bestDist == double.MaxValue) return Vec3.Zero;
        double dl = d.Length;
        if (dl < 1e-9) return Vec3.Zero;
        d /= dl;
        // THE PROFILE ACROSS THE TUBE. Momentum theory gives a top-hat, and mixing turns its edge into a shear layer
        // within a diameter or so of the disc; the same cos-squared taper the wake correlation uses (Silverstein,
        // Katzoff & Bullivant, NACA Report 648) carries it. A uniform core out to half the radius, then a smooth taper
        // to nothing at the edge.
        //
        // This is not cosmetic. A linear 25 % edge put a knife edge where the tube's boundary crossed a surface: a
        // Cub's wing root sits right on it, and its rolling moment then swung 4.6 N.m and CHANGED SIGN inside one degree
        // of alpha (measured, JetTrimSensitivityProbe), which no aileron rig can hold. The real profile is smooth, so
        // the loads are smooth.
        double norm = bestDist / Math.Max(1e-6, rr);
        double taper = (norm - CoreFraction) / (1.0 - CoreFraction);
        double cover = norm <= CoreFraction ? 1.0 : Math.Cos(0.5 * Math.PI * Math.Clamp(taper, 0.0, 1.0));
        cover *= cover;
        Vec3 inc = d * (k * cover * u);
        if (withSwirl && t.Swirl != 0.0 && rr > 0.0)
        {
            // Solid-body rotation about the tube's own axis. The air's tangential motion is (swirl/r)·(d × ρ); the
            // relative wind takes the opposite. A right-hand prop (clockwise from behind) carries the air UP past the
            // left half of the tail and DOWN past the right, which is what the signed swirl says.
            Vec3 perp = off - d * off.Dot(d);
            inc -= Vec3.Cross(d, perp) * (k * cover * t.Swirl / rr);
        }
        return inc;
    }

    /// <summary>
    /// How much of the wash still reaches the tail, 0..1, for the propeller model's own spiral-slipstream yaw (the
    /// swirl striking the fin, <c>PropModel.Parts.SlipYaw</c>, which is a bulk moment and not a strip load). The
    /// construction is the one the old scalar used — the overlap of two circles of radius r whose centres are Δ apart,
    /// (2/π)(acos d − d√(1−d²)) with d = Δ/2r — but Δ is now measured between the TRACED tube and the same tube traced
    /// in the reference state (this speed straight ahead, no rotation), at the same distance along the path. So it is
    /// exactly 1 in the reference state whatever the airframe looks like, which leaves every stored rig and trim alone,
    /// and it falls to 0 when the jet has genuinely gone somewhere else. There is no stall gate on it: a flat spin
    /// takes the wash off the fin because of where the air goes, not because the wing is stalled.
    /// </summary>
    public static double TailShare(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                                  double deltaU, double radius, double swirl)
    {
        if (deltaU == 0.0 || radius <= 0.0) return 1.0;
        Geom g = _geom.GetValue(config, Build);
        if (g.Hubs.Length == 0 || g.TailArmM <= 0.0) return 1.0;
        double spread = config.Propulsion?.JetSpreadPerM ?? 0.045;
        Jet j = Prepare(config, freestream, rates, cg, deltaU, radius, swirl);
        if (!j.Any) return 1.0;
        Tube r0 = _refTube ??= new Tube();
        Trace(r0, g.Hubs[0], new Vec3(1, 0, 0), new Vec3(Math.Max(0.0, freestream.Length), 0, 0), Vec3.Zero, cg,
              deltaU, radius, swirl, g.DiameterM, spread);
        if (!r0.Any) return 1.0;
        Vec3 pReal = AtPathLength(j.Tubes[0], g.TailArmM);
        Vec3 pRef = AtPathLength(r0, g.TailArmM);
        double rAt = radius + spread * g.TailArmM;
        double dd = (pReal - pRef).Length / (2.0 * rAt);
        if (dd >= 1.0) return 0.0;
        return Math.Clamp(2.0 / Math.PI * (Math.Acos(dd) - dd * Math.Sqrt(1.0 - dd * dd)), 0.0, 1.0);
    }

    /// <summary>From the propeller disc to the tail's area-weighted station (every non-wing strip), m, straight back.
    /// The reference length the jet is measured along.</summary>
    public static double TailArm(AircraftConfig config) => _geom.GetValue(config, Build).TailArmM;

    private static Vec3 AtPathLength(Tube t, double s)
    {
        if (t.Count == 0) return Vec3.Zero;
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (s <= t.S[i + 1])
            {
                double span = t.S[i + 1] - t.S[i];
                double f = span > 1e-9 ? Math.Clamp((s - t.S[i]) / span, 0.0, 1.0) : 0.0;
                return t.P[i] + (t.P[i + 1] - t.P[i]) * f;
            }
        }
        return t.P[t.Count - 1];
    }

    /// <summary>The traced tube's centre, and its radius, a distance <paramref name="pathM"/> along the jet — for the
    /// tests and the diagnostics, which is the only way to see where the jet actually went.</summary>
    public static (Vec3 At, double Radius, Vec3 Direction, double DeltaU) Probe(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                                                                               double deltaU, double radius, double swirl, double pathM)
    {
        Jet j = Prepare(config, freestream, rates, cg, deltaU, radius, swirl);
        if (!j.Any) return (Vec3.Zero, 0.0, Vec3.Zero, 0.0);
        Tube t = j.Tubes[0];
        double spread = config.Propulsion?.JetSpreadPerM ?? 0.045;
        Vec3 at = AtPathLength(t, pathM);
        int n = t.Count - 1;
        for (int i = 0; i + 1 < t.Count; i++) if (pathM <= t.S[i + 1]) { n = i; break; }
        return (at, radius + spread * pathM, t.D[n], t.U[n]);
    }

    /// <summary>The increment at a station, for the tests and the force overlay.</summary>
    public static Vec3 RelWindAt(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                                 double deltaU, double radius, double swirl, Vec3 at, bool withSwirl)
        => RelWind(Prepare(config, freestream, rates, cg, deltaU, radius, swirl), at, withSwirl);
}

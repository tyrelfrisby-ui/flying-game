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

    /// <summary>
    /// HOW MUCH OF THE WASH FOLLOWS THE BODY. Ty 2026-11-09: "i think there is a 'tendency' for the vortex to follow
    /// the fuselage not a 'rule' that it must. for example in a spin the relative wind is from below and the tail
    /// makes an arcing helix... the likely hood that the entire vortex remains attached to the fuselage all the way to
    /// the tail is low. I'd say we should model fully attached to the fuselage ... and then model no attachment at all
    /// just the column of thrust affected by the relative wind and angular movement and then average the two together
    /// starting at 50/50 bias and then tweaking it as necessary to match emperical real world experience."
    ///
    /// So both limits are traced and blended. It is an EMPIRICAL bias and it is meant to be tuned; it is not derived.
    /// The blend is also why the model degrades the right way: in a spin the free column swings off the tail while the
    /// attached one stays on the body, so the fin keeps some of the wash instead of all of it or none.
    /// </summary>
    public static double AttachedBias = 0.5;

    internal sealed class Tube
    {
        internal int Count;
        internal readonly Vec3[] P = new Vec3[MaxNodes];   // centre line nodes, body axes
        internal readonly Vec3[] D = new Vec3[MaxNodes];   // the increment's direction there (unit, body axes)
        internal readonly double[] R = new double[MaxNodes];
        internal readonly double[] Inner = new double[MaxNodes];   // the body's equivalent radius there: the wash's hole
        internal readonly double[] U = new double[MaxNodes];
        internal readonly double[] S = new double[MaxNodes];   // path length from the disc
        internal Vec3 Hub, Axis;
        internal double Swirl;        // m/s at the tube edge, signed by the prop's rotation (the fallback shape)
        internal BladeElement.SwirlProfile? Profile;   // the blade's own radial distribution, when it was solved
        internal bool Any;
    }

    internal sealed class Jet
    {
        internal Tube[] Tubes = Array.Empty<Tube>();        // the FREE column: advected by the air alone
        internal Tube[] Attached = Array.Empty<Tube>();     // the same wash held onto the body's own midline
        internal int Count;
        internal bool Any;
        internal double K = 1.0;
        internal bool HasAttached;
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
        => Prepare(config, freestream, rates, cg, deltaU, radius, swirl, null);

    internal static Jet Prepare(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                               double deltaU, double radius, double swirl, BladeElement.SwirlProfile? profile)
    {
        Jet j = _scratch ??= new Jet();
        j.Count = 0; j.Any = false; j.HasAttached = false;   // a [ThreadStatic] scratch: every flag resets per evaluation
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
        if (j.Attached.Length < g.Hubs.Length)
        {
            var grown = new Tube[g.Hubs.Length];
            for (int i = 0; i < grown.Length; i++) grown[i] = i < j.Attached.Length ? j.Attached[i] : new Tube();
            j.Attached = grown;
        }
        double spread = ShearSpread(config, freestream, deltaU);
        // ONLY A SINGLE NOSE PROPELLER CAN BE ATTACHED TO THE FUSELAGE. A nacelle-mounted one washes its own nacelle,
        // which is a different body and not described here; it keeps the free column alone.
        bool bodyAttach = g.Hubs.Length == 1 && config.Engines.Count == 0 && config.Fuselage?.Crossflow is { LengthM: > 0.1 };
        for (int i = 0; i < g.Hubs.Length; i++)
        {
            Trace(j.Tubes[i], g.Hubs[i], new Vec3(1, 0, 0), freestream, rates, cg, deltaU, radius, swirl, g.DiameterM, spread, config.Fuselage?.Crossflow);
            j.Tubes[i].Profile = profile is { Any: true } ? profile : null;
            if (j.Tubes[i].Any) j.Any = true;
            if (bodyAttach)
            {
                TraceAttached(j.Attached[i], g.Hubs[i], config.Fuselage!.Crossflow, deltaU, radius, swirl, g.DiameterM, spread);
                j.Attached[i].Profile = profile is { Any: true } ? profile : null;
                if (j.Attached[i].Any) j.HasAttached = true;
            }
            else j.Attached[i].Any = false;
        }
        j.Count = g.Hubs.Length;
        return j;
    }

    /// <summary>
    /// THE SPREADING RATE AT THIS AIRSPEED: the still-air rate times the mixing layer's shear parameter,
    /// lambda = du / (2 V + du). One standing still, near zero in cruise. See <see cref="PropulsionConfig.JetSpreadK"/>.
    /// </summary>
    /// <summary>A test seam: >= 0 forces the spreading rate, so a probe can hold the OLD flat law beside the new one
    /// and compare them at the same condition. Negative (the default) leaves the physics alone.</summary>
    public static double SpreadOverride = -1.0;

    private static double ShearSpread(AircraftConfig config, Vec3 freestream, double deltaU)
    {
        if (SpreadOverride >= 0.0) return SpreadOverride;
        double still = Math.Clamp(config.Propulsion?.JetSpreadK ?? PropulsionConfig.JetSpreadKMax,
                                  0.0, PropulsionConfig.JetSpreadKMax);
        double du = Math.Abs(deltaU);
        if (du <= 1e-9) return 0.0;
        double lambda = du / (2.0 * freestream.Length + du);
        return still * Math.Clamp(lambda, 0.0, 1.0);
    }

    private static void Trace(Tube t, Vec3 hub, Vec3 axis, Vec3 freestream, Vec3 rates, Vec3 cg,
                              double deltaU, double radius, double swirl, double diameter, double spread,
                              CrossflowConfig? body = null)
    {
        t.Count = 0; t.Any = false; t.Hub = hub; t.Axis = axis; t.Swirl = swirl;
        Vec3 p = hub, d = axis;
        double s = 0.0;
        for (int i = 0; i < MaxNodes; i++)
        {
            // Momentum theory's growth: about half the increment at the disc, all of it a diameter behind.
            double u = deltaU * (diameter > 0.0 ? 0.55 + 0.45 * Math.Min(1.0, s / diameter) : 1.0);
            t.P[i] = p; t.D[i] = d; t.U[i] = u; t.S[i] = s;
            t.R[i] = radius * (1.0 + spread * (diameter > 0.0 ? s / diameter : 0.0));
            t.Inner[i] = body is null ? 0.0 : FuselageShape.At(body, p.X).EquivalentRadiusM;
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

    /// <summary>
    /// THE ATTACHED LIMIT: the same wash, held onto the fuselage all the way aft. Its centre line is the body's own
    /// midline rather than a free trajectory, so where the tailcone rises the wash rises with it, and its direction is
    /// the midline's tangent. Nothing here depends on the relative wind or the rotation — that is the whole point of
    /// the pair, and it is why this limit keeps the wash on the tail in a spin where the free column has left.
    ///
    /// Its radius carries the SAME mass through an annulus that must now fit outside the body: pi*R_free^2 of air
    /// through pi*(R^2 - a^2) gives R = sqrt(R_free^2 + a^2) with a the section's equivalent radius. The wash is
    /// pushed outward by exactly the body it is wrapping, which is the mechanism that puts more of a fin in it than a
    /// straight column ever does.
    /// </summary>
    private static void TraceAttached(Tube t, Vec3 hub, CrossflowConfig cf,
                                      double deltaU, double radius, double swirl, double diameter, double spread)
    {
        t.Count = 0; t.Any = false; t.Hub = hub; t.Axis = new Vec3(1, 0, 0); t.Swirl = swirl;
        double s = 0.0;
        for (int i = 0; i < MaxNodes; i++)
        {
            double x = hub.X - s;                                  // straight aft along the body, station by station
            FuselageShape.Envelope e = FuselageShape.At(cf, x);
            double slope = FuselageShape.MidlineSlope(cf, x);
            double u = deltaU * (diameter > 0.0 ? 0.55 + 0.45 * Math.Min(1.0, s / diameter) : 1.0);
            double rFree = radius * (1.0 + spread * (diameter > 0.0 ? s / diameter : 0.0));
            double a = e.EquivalentRadiusM;
            t.P[i] = new Vec3(x, hub.Y, e.MidZ);
            // Aft is -x, so the increment's direction is -(-1, 0, slope) normalised: a midline that RISES going aft
            // (dz/dx > 0 in NED means falling, so a rising tailcone has dz/dx < 0) turns the wash upward with it.
            Vec3 d = new Vec3(1.0, 0.0, -slope);
            double dl = d.Length;
            t.D[i] = dl > 1e-9 ? d / dl : new Vec3(1, 0, 0);
            t.R[i] = Math.Sqrt(rFree * rFree + a * a);
            t.Inner[i] = a;
            t.U[i] = u; t.S[i] = s;
            t.Count = i + 1;
            if (s >= MaxPathM) break;
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
        Vec3 best = Strongest(j.Tubes, j.Count, at, withSwirl, j.K);
        if (!j.HasAttached) return best;
        // THE AVERAGE OF THE TWO LIMITS, at the strip rather than at the path. Where both reach a station the result
        // is the blend; where only one does, the station takes that one's share of the bias, which is what makes the
        // wash's edge soft in the right way instead of a surface being wholly in or wholly out of it.
        Vec3 attached = Strongest(j.Attached, j.Count, at, withSwirl, j.K);
        double b = Math.Clamp(AttachedBias, 0.0, 1.0);
        return best * (1.0 - b) + attached * b;
    }

    private static Vec3 Strongest(Tube[] tubes, int count, Vec3 at, bool withSwirl, double k)
    {
        Vec3 best = Vec3.Zero;
        double bestMag = 0.0;
        for (int i = 0; i < count && i < tubes.Length; i++)
        {
            if (!tubes[i].Any) continue;
            Vec3 v = FromTube(tubes[i], at, withSwirl, k);
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
        double u = 0.0, rr = 0.0, inner = 0.0;
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
            inner = t.Inner[i] + (t.Inner[i + 1] - t.Inner[i]) * f;
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
            // The air's tangential motion is around the tube's own axis; the relative wind takes the opposite. A
            // right-hand prop (clockwise from behind) carries the air UP past the left half of the tail and DOWN past
            // the right, which is what the signed swirl says.
            Vec3 perp = off - d * off.Dot(d);
            double pl = perp.Length;
            if (pl > 1e-9)
            {
                // MEASURED where the blade solved it, assumed only where it did not. The profile already conserves
                // r*v_t through the mapping onto this tube's radius, so the spreading and the body displacement slow
                // the swirl by the ratio they widened it. The fallback is solid-body rotation, which is what every
                // unsolved type still gets, and which is wrong at the hub by 17x on a J-3.
                // THE BODY IS IN THE CORE, and the swirl there never reaches a fin. Measured, the slipstream's
                // angular momentum is concentrated at small radius - 10.6 m/s at r 0.145 on a J-3 - and that is
                // precisely where the FUSELAGE is. Air cannot occupy it, so each streamtube is displaced outward
                // around the body, from r0 to sqrt(r0^2 + a^2), and past the disc nothing torques it, so r*v_t is
                // conserved and the displaced tube SLOWS by r0/r. Left out, the model hands the fin root a core it
                // could never feel: the J-3's rigged fin came out at 6.63 deg against the 1.76 the aeroplane shows.
                double r0 = inner > 0.0 ? Math.Sqrt(Math.Max(0.0, pl * pl - inner * inner)) : pl;
                double rFree = inner > 0.0 ? Math.Sqrt(Math.Max(1e-12, rr * rr - inner * inner)) : rr;
                double vt = t.Profile is not null
                    ? Math.Sign(t.Swirl) * t.Profile.At(r0, rFree) * (r0 / Math.Max(1e-6, pl))
                    : t.Swirl * (r0 / rr);
                inc -= Vec3.Cross(d, perp) * (k * cover * vt / pl);
            }
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
        double spread = ShearSpread(config, freestream, deltaU);
        Jet j = Prepare(config, freestream, rates, cg, deltaU, radius, swirl);
        if (!j.Any) return 1.0;
        Tube r0 = _refTube ??= new Tube();
        Trace(r0, g.Hubs[0], new Vec3(1, 0, 0), new Vec3(Math.Max(0.0, freestream.Length), 0, 0), Vec3.Zero, cg,
              deltaU, radius, swirl, g.DiameterM, spread);
        if (!r0.Any) return 1.0;
        Vec3 pReal = AtPathLength(j.Tubes[0], g.TailArmM);
        Vec3 pRef = AtPathLength(r0, g.TailArmM);
        double rAt = radius * (1.0 + spread * (g.DiameterM > 0.0 ? g.TailArmM / g.DiameterM : 0.0));
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
        double spread = ShearSpread(config, freestream, deltaU);
        Vec3 at = AtPathLength(t, pathM);
        int n = t.Count - 1;
        for (int i = 0; i + 1 < t.Count; i++) if (pathM <= t.S[i + 1]) { n = i; break; }
        double dia = config.Propulsion?.PropDiameterM ?? 0.0;
        return (at, radius * (1.0 + spread * (dia > 0.0 ? pathM / dia : 0.0)), t.D[n], t.U[n]);
    }

    /// <summary>The increment at a station, for the tests and the force overlay.</summary>
    public static Vec3 RelWindAt(AircraftConfig config, Vec3 freestream, Vec3 rates, Vec3 cg,
                                 double deltaU, double radius, double swirl, Vec3 at, bool withSwirl)
        => RelWind(Prepare(config, freestream, rates, cg, deltaU, radius, swirl), at, withSwirl);
}

using System;

namespace FlyingGame.Core;

/// <summary>
/// A PROPELLER, INSTEAD OF A THRUST SCALAR WITH CORRECTIONS BOLTED ON (Ty 2026-11-09: "I'd like to make each of these
/// real science not lump sums if possible"). Blade-element momentum theory: the blade is cut into annular elements,
/// each element's own inflow is solved against the momentum it must impart to its annulus, and thrust and torque are
/// what the elements add up to. Glauert's method; Leishman, <i>Principles of Helicopter Aerodynamics</i> ch. 2-3, with
/// Prandtl's tip loss.
///
/// WHAT THIS REPLACES, and why it is one job and not five. The old model was thrust = eta*P/V capped by a static
/// figure of merit, with P-factor, the in-plane force, the spiral slipstream's yaw and the slipstream's swirl each
/// added afterwards as its own tuned coefficient. They are not separate effects: they are all consequences of a disc
/// of blades meeting the air at an angle, and they all fall out of the same integral.
///
///   thrust, torque    the axial and tangential sums over the elements — no efficiency constant, and no static
///                     figure of merit, because at V = 0 the solution is simply the one with no forward speed
///   efficiency        TV/(Q*Omega), a CURVE against advance ratio instead of the flat 0.75 every type carried
///   swirl             the torque IS the angular momentum in the slipstream, which is what the traced jet wants
///   P-factor          integrate around the azimuth as well as the radius: at alpha the down-going blade meets the
///                     air faster and at a higher angle, and the thrust centre moves off the hub by itself
///   in-plane force    the same azimuthal integral's tangential component — NormalForceK with the K removed
///   windmilling       negative torque, not a separate branch
///
/// The one thing it needs that a thrust scalar did not is BLADE GEOMETRY, and no config has it. Rather than invent
/// chord and twist per type, this takes a standard blade family: an activity-factor chord distribution and a constant
/// geometric pitch set at three-quarter radius. Two numbers per propeller, both of which have published values for
/// real blades, and both of which can be fitted to a type's own book figures (static thrust, climb, cruise at power).
/// </summary>
public static class BladeElement
{
    /// <summary>One propeller's geometry. <see cref="PitchAt75Deg"/> is the blade angle at 0.75 R, which with a
    /// constant geometric pitch sets the twist everywhere else.</summary>
    public readonly struct Geometry
    {
        public readonly int Blades;
        public readonly double RadiusM, HubFraction, ActivityFactor, PitchAt75Deg;
        public Geometry(int blades, double radiusM, double pitchAt75Deg, double activityFactor = 100.0, double hubFraction = 0.15)
        {
            Blades = Math.Max(1, blades); RadiusM = radiusM; PitchAt75Deg = pitchAt75Deg;
            ActivityFactor = activityFactor; HubFraction = Math.Clamp(hubFraction, 0.05, 0.5);
        }

        /// <summary>
        /// Chord at radius fraction x, from the activity factor. AF = (100000/16) * integral of (c/D) x^3 dx over the
        /// blade, so a shape function fixes the distribution and the AF fixes its scale: one published number per
        /// blade instead of a table nobody has. The shape is a conventional round-tipped blade — widest a little
        /// inboard of mid-span, tapering to the tip.
        /// </summary>
        public double ChordM(double x)
        {
            double s = ShapeAt(x);
            return ActivityFactor / (100000.0 / 16.0) / ShapeIntegral * s * (2.0 * RadiusM);
        }

        private static double ShapeAt(double x) => x <= 0.0 || x >= 1.0 ? 0.0 : Math.Pow(Math.Sin(Math.PI * Math.Pow(x, 0.75)), 0.85);

        /// <summary>The shape's own integral of s(x)*x^3, so ChordM scales to the requested activity factor.</summary>
        private static double ShapeIntegral
        {
            get
            {
                if (_shapeIntegral > 0) return _shapeIntegral;
                double sum = 0; const int N = 2000;
                for (int i = 0; i < N; i++) { double x = (i + 0.5) / N; sum += ShapeAt(x) * x * x * x / N; }
                return _shapeIntegral = sum;
            }
        }
        private static double _shapeIntegral;

        /// <summary>Blade angle at x, from a constant geometric pitch through 0.75 R.</summary>
        public double BladeAngleRad(double x)
        {
            double p = 2.0 * Math.PI * 0.75 * RadiusM * Math.Tan(PitchAt75Deg * Math.PI / 180.0);   // advance per turn, m
            return Math.Atan2(p, 2.0 * Math.PI * Math.Max(1e-4, x) * RadiusM);
        }
    }

    /// <summary>
    /// THE SWIRL'S RADIAL SHAPE, carried from the blade to the jet. Preallocated and refilled in place: the sim solves
    /// this every frame and must not allocate to do it.
    ///
    /// It exists because the shape was being ASSUMED and the fin stands where the candidate shapes disagree most.
    /// Solid-body rotation says 0.60 m/s where the J-3's blade says 10.60 at the same station - wrong by 17x at the
    /// hub, and wrong the other way at the tip. A free vortex matches the inner half and then overshoots, having no
    /// tip loss. Measured, the swirl falls monotonically outward in cruise and goes nearly flat inside 0.45 R when
    /// standing still, where the root loss cuts the hub peak down; no one law fits both, and the blade fits both
    /// because it is where both come from.
    /// </summary>
    public sealed class SwirlProfile
    {
        public readonly double[] WakeRadiusM = new double[ProfileCount];
        public readonly double[] SwirlMs = new double[ProfileCount];
        public int Count;
        public bool Any;

        /// <summary>The tangential speed at <paramref name="r"/>, with the tube's own radius mapped onto the profile's.
        /// Past the disc nothing torques the air, so r*v_t is conserved: a streamtube displaced outward - by the jet's
        /// spreading, or by a fuselage in its core - slows by exactly the ratio its radius grew.</summary>
        public double At(double r, double tubeRadiusM)
        {
            if (!Any || Count < 2 || tubeRadiusM <= 1e-6 || r < 0.0) return 0.0;
            double rs = WakeRadiusM[Count - 1];
            if (rs <= 1e-6) return 0.0;
            double scale = rs / tubeRadiusM;                 // the profile's own radius per metre of this tube
            double rp = r * scale;                           // where on the profile this station sits
            double v;
            if (rp <= WakeRadiusM[0]) v = SwirlMs[0] * (rp / Math.Max(1e-6, WakeRadiusM[0]));   // solid-body inside the first annulus
            else if (rp >= rs) return 0.0;                   // outside the wash
            else
            {
                v = 0.0;
                for (int i = 0; i + 1 < Count; i++)
                {
                    double a = WakeRadiusM[i], b = WakeRadiusM[i + 1];
                    if (rp < a || rp > b) continue;
                    double f = b - a < 1e-9 ? 0.0 : (rp - a) / (b - a);
                    v = SwirlMs[i] + f * (SwirlMs[i + 1] - SwirlMs[i]);
                    break;
                }
            }
            return v * scale;                                // r * v_t conserved through the mapping
        }
    }

    /// <summary>What the disc did to the air, and what the air did back.</summary>
    public readonly struct Result
    {
        public readonly double ThrustN, TorqueNm, PowerW, Efficiency, SwirlC, NormalN, SideN;
        /// <summary>
        /// THE FIRST MOMENT OF THRUST across the disc, N.m: sum of dT * r * cos(psi). Divided by the thrust it is the
        /// distance the thrust centre has moved toward the down-going blade — P-factor's moment, measured instead of
        /// assumed. It used to be stood in for by (in-plane force) x (propeller radius), which is a plausible scale
        /// and nothing more; this is the same azimuthal integral that already produces the thrust, merely weighted by
        /// where on the disc each element made it.
        /// </summary>
        public readonly double ThrustMomentNm;
        public Result(double t, double q, double p, double eta, double swirlC, double normal, double side, double thrustMoment = 0.0)
        { ThrustN = t; TorqueNm = q; PowerW = p; Efficiency = eta; SwirlC = swirlC; NormalN = normal; SideN = side; ThrustMomentNm = thrustMoment; }
    }

    /// <summary>How many annuli a profile request comes back with (one per blade element).</summary>
    public const int ProfileCount = 24;

    private const int Elements = 24;
    private const int Azimuths = 16;

    /// <summary>
    /// HOW FINELY THE DISC IS CUT AZIMUTHALLY, and why it is no longer one number. Measured 2026-11-10 (tools/SimCost):
    /// the azimuthal loop is the whole cost of this model. One solve of the J-3's blade took 70 us axial and 1,220 us
    /// the moment there was ANY crossflow — 17x, which is the 16 azimuths — and the sim reaches this solve SIX times a
    /// step (the four RK4 stages through Aircraft.PropulsionForces, Aircraft.TailSlipstream, and
    /// AircraftEngine.StepShaft). So the J-3 cost 8,372 us a step at 200 Hz — 1.67 seconds of CPU per second of
    /// flight — against 120 to 205 us for every other type in the fleet. That is what Ty felt on 2026-11-09: "the j3
    /// is noticibly slower to run/process so there might be a leak on it?" It was not a leak; it was this.
    ///
    /// The old gate was <c>Math.Abs(vCross) &lt; 1e-6</c>, which in flight is never true: a tenth of a degree of alpha
    /// is 0.06 m/s of crossflow and bought the full sixteen azimuths. But what the azimuthal integral is FOR is the
    /// first harmonic — the thrust centre's offset toward the down-going blade (P-factor) and the disc's in-plane
    /// force, both of which are the cos(psi)-weighted part of the load. A midpoint rule with nAz points integrates a
    /// first harmonic EXACTLY for any nAz >= 3, so four azimuths carry the whole of the linear-in-crossflow answer;
    /// only the higher harmonics, which grow with the inflow skew, need more.
    ///
    /// So the count follows the skew mu = |vCross| / (omega R) — the azimuthal variation of the blade's own tangential
    /// speed as a fraction of its tip speed. The thresholds are MEASURED against nAz = 16 over the fleet's fitted
    /// blades, not chosen: tools/SimCost --azimuth prints the sweep banded by skew, which is where these two numbers
    /// were read off (nAz 4 holds thrust and torque to 4e-4 of themselves below a skew of 0.02; nAz 8 holds them to
    /// 9e-4 below 0.06), and HotPathCostTests holds the bound.
    /// </summary>
    private const double Skew4 = 0.02, Skew8 = 0.06;

    /// <summary>A probe's seam: 0 is the adaptive count above, a positive value forces it. Only
    /// tools/SimCost and HotPathCostTests set it, and they set it back.</summary>
    public static int AzimuthOverride;

    /// <summary>
    /// Every inner momentum-balance iteration THIS THREAD has run. It is the probes' and the guard's measure of what
    /// this model costs, and it is a count rather than a clock on purpose: the machine this is measured on runs several
    /// agents at once and one run of the fleet came out four times slower than the one before it on identical code, so
    /// a microsecond bound would be a coin toss. The iteration count is deterministic. Thread-static because the test
    /// suite runs collections in parallel and a shared counter would be measuring other people's propellers.
    /// </summary>
    [System.ThreadStatic] public static long Iterations;

    /// <summary>Azimuths and solves this thread has run, so the probes can report the MEAN azimuth count a flight
    /// actually asks for. Which band real flight sits in is the whole question the thresholds answer, and it is not
    /// something to assume: the J-3 at its spawn condition turns out to ask for eight and sixteen, not four.</summary>
    [System.ThreadStatic] public static long AzimuthsUsed;
    [System.ThreadStatic] public static long Solves;

    /// <summary>
    /// WHERE THE MOMENTUM BALANCE STARTS, AND WHEN IT STOPS. Measured 2026-11-10: with the old cold start it took 26
    /// iterations per element and azimuth — 2,500 for a four-azimuth solve — and the 60-iteration cap was never
    /// reached, so the cost was the convergence rate and not the cap.
    ///
    /// Both halves of that are free to fix. The START: vi and wi vary smoothly across the disc, and the solve walks
    /// the elements outward and the azimuths round, so the neighbour it has just converged is a far better guess than
    /// a formula in the local tangential speed with no vAxial in it at all. The STOP: the test was 1e-6 m/s on an
    /// induced velocity of tens of m/s, which is 1e-7 of the answer — a tolerance on the twelfth significant figure of
    /// a model calibrated against figures good to a per cent.
    ///
    /// The relaxation factor 0.4 is NOT touched. It is what keeps the fixed point stable at a stalled root and in
    /// reversed flow, and this is not the change to find that out on.
    /// </summary>
    private const double ConvergeTol = 1e-4;

    /// <summary>A probe's seam: true restores the 2026-11-09 inner loop exactly — a cold start at every element and
    /// azimuth and the 1e-6 tolerance — so what the warm start and the tolerance cost can be MEASURED against it
    /// (tools/SimCost --blade, --azimuth; HotPathCostTests). False in the sim.</summary>
    public static bool LegacyInnerLoop;

    /// <summary>The previous element's converged induced velocities, one pair per azimuth, as the warm start for the
    /// next element out. Thread-static and allocated once: the sim must not allocate to solve a propeller.</summary>
    [System.ThreadStatic] private static double[]? _viPrev;
    [System.ThreadStatic] private static double[]? _wiPrev;

    /// <summary>
    /// Solve the disc at this airspeed, angle of attack and rpm. <paramref name="alphaRad"/> is the angle between the
    /// free stream and the shaft; at zero the azimuthal loop collapses to the axial case and the in-plane forces are
    /// zero by symmetry.
    /// </summary>
    public static Result Solve(in Geometry g, double vAxialMs, double vCrossMs, double rpm, double rhoKgM3)
        => Solve(g, vAxialMs, vCrossMs, rpm, rhoKgM3, null, null);

    /// <summary>
    /// The same solve, also reporting WHERE THE SWIRL IS. <paramref name="wakeRadii"/> and <paramref name="wakeSwirl"/>,
    /// if given, must hold at least <see cref="ProfileCount"/> entries and come back as the far-wake radius of each
    /// annulus and the tangential speed the air there carries.
    ///
    /// This exists because the swirl's radial SHAPE was being assumed rather than read, and the fin stands exactly
    /// where the candidate shapes disagree. Solid-body rotation (zero on the axis, most at the edge) and a free vortex
    /// (most at the hub, falling as 1/r) are opposite trends, they cross at about 0.7 of the tube radius, and a fin
    /// sits inboard of that crossing — so the choice between them moved the J-3's rigged fin by 50 %. There is no need
    /// to choose: the annulus momentum balance below already solves for wi at every radius, with Prandtl's tip and
    /// root loss on it, so the true distribution is in hand and was simply being discarded. The far-wake tangential
    /// speed is 2 wi, which falls out of the balance itself: multiplying 0.5 W^2 sigma Ct = 2 F (V + vi) wi through by
    /// rho 2 pi r dr r turns the left side into the blade's own torque and the right into mdot (2 wi) r.
    /// </summary>
    public static Result Solve(in Geometry g, double vAxialMs, double vCrossMs, double rpm, double rhoKgM3,
                               double[]? wakeRadii, double[]? wakeSwirl)
    {
        long tBlade = SimProfile.Begin();
        double omega = rpm * 2.0 * Math.PI / 60.0;
        if (omega <= 1e-3 || g.RadiusM <= 0.0) return new Result(0, 0, 0, 0, 0, 0, 0);
        double thrust = 0, torque = 0, normal = 0, angMom = 0, thrustMom = 0;
        double dx = (1.0 - g.HubFraction) / Elements;
        bool axial = Math.Abs(vCrossMs) < 1e-6;
        // THE AZIMUTH COUNT, FROM THE SKEW (see Skew4/Skew8 above for what was measured and why four is enough).
        int nAz;
        if (AzimuthOverride > 0) nAz = axial ? 1 : AzimuthOverride;
        else if (axial) nAz = 1;
        else
        {
            double skew = Math.Abs(vCrossMs) / Math.Max(1e-6, omega * g.RadiusM);
            nAz = skew < Skew4 ? 4 : skew < Skew8 ? 8 : Azimuths;
        }
        long iterations = 0;
        AzimuthsUsed += nAz; Solves++;
        double[] viPrev = _viPrev ??= new double[Azimuths];
        double[] wiPrev = _wiPrev ??= new double[Azimuths];
        bool warm = false;                                 // nothing converged yet: the first element starts cold

        for (int i = 0; i < Elements; i++)
        {
            double x = g.HubFraction + (i + 0.5) * dx;
            double r = x * g.RadiusM, chord = g.ChordM(x), theta = g.BladeAngleRad(x);
            double sigma = g.Blades * chord / (2.0 * Math.PI * r);
            // Prandtl's tip and root loss numerators: constant over the azimuth and over the iteration, and they were
            // being rebuilt inside both (Blades * 0.5 * (1 - x) and * (x - hub)).
            double tipNum = g.Blades * 0.5 * (1.0 - x), rootNum = g.Blades * 0.5 * (x - g.HubFraction);
            double wiSum = 0.0, viSum = 0.0;
            for (int k = 0; k < nAz; k++)
            {
                // Azimuth 0 is the blade going DOWN on the right of a right-hand prop, which is where a positive
                // angle of attack adds velocity and incidence — the whole of P-factor, from geometry.
                double psi = 2.0 * Math.PI * (k + 0.5) / nAz;
                double cosPsi = Math.Cos(psi);                 // wanted three times below; it was taken three times
                double vTanLocal = omega * r + vCrossMs * cosPsi;
                // INDUCED VELOCITY, ABSOLUTE — not a fraction of the forward speed. Writing it as V(1+a), which is
                // the aeroplane-propeller convention, is degenerate at V = 0: the flow through the disc is zero
                // whatever a does, so the static case collapses (it gave 24 N where this propeller makes over a
                // thousand). Rotor analysis never writes it that way because hover IS V = 0. With vi and wi as
                // velocities, the annulus momentum balance is
                //     0.5 W^2 sigma Cn = 2 F (V + vi) vi          ->  vi = ( -V + sqrt(V^2 + W^2 sigma Cn / F) ) / 2
                //     0.5 W^2 sigma Ct = 2 F (V + vi) wi          ->  wi = W^2 sigma Ct / (4 F (V + vi))
                // and V = 0 is just another solution.
                // The guess: the same annulus one step inboard, which has just been solved, or the old formula for
                // the first element (and whenever the probe asks for the old loop back).
                double vi, wi;
                if (warm && !LegacyInnerLoop) { vi = viPrev[k]; wi = wiPrev[k]; }
                else { vi = 0.5 * Math.Sqrt(Math.Max(1.0, vTanLocal * 0.05)); wi = 0.0; }
                double tol = LegacyInnerLoop ? 1e-6 : ConvergeTol;
                double w2 = 0, cn = 0, ct = 0, phi = 0;
                for (int it = 0; it < 60; it++)
                {
                    iterations++;
                    double va = vAxialMs + vi, vt = Math.Max(1e-6, vTanLocal - wi);
                    phi = Math.Atan2(va, vt);
                    double alpha = theta - phi;
                    (double cl, double cd) = Section(alpha);
                    // sin(phi) AND cos(phi) FOR FREE. phi is Atan2(va, vt) with vt forced positive, so it lies in
                    // (-pi/2, pi/2) and the triangle it came from is already here: sin = va/W, cos = vt/W with
                    // W = sqrt(va^2 + vt^2), which the balance needs anyway as w2. Identical to the last bit, and it
                    // retires a Sin and a Cos from an inner loop that runs about 7,000 times per solve.
                    w2 = va * va + vt * vt;
                    double wMag = Math.Sqrt(w2);
                    double sp = va / wMag, cp = vt / wMag;
                    cn = cl * cp - cd * sp;          // along the shaft
                    ct = cl * sp + cd * cp;          // against the rotation
                    // Prandtl tip (and root) loss. sin|phi| IS |sin phi| over (-pi/2, pi/2), so it is |sp| and not two
                    // more Sin calls; and exp(-f) with f >= 0 is in (0, 1], so the clamp to [-1, 1] never bit.
                    double sinAbs = Math.Abs(sp);
                    double denom = Math.Max(1e-3, x * sinAbs);
                    double ftip = AcosExpNeg(tipNum / denom);
                    double froot = AcosExpNeg(rootNum / denom);
                    double F = Math.Max(0.05, ftip * froot);
                    double viNew, wiNew;
                    double load = w2 * sigma * cn / F;
                    if (load >= 0.0)
                        viNew = 0.5 * (-vAxialMs + Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + load)));
                    else
                        viNew = 0.5 * (-vAxialMs + Math.Sign(vAxialMs) * Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + load)));
                    viNew = Math.Clamp(viNew, -0.9 * Math.Abs(vAxialMs) - 30.0, 60.0);
                    wiNew = w2 * sigma * ct / Math.Max(1e-6, 4.0 * F * Math.Max(1e-3, vAxialMs + viNew));
                    // CLAMP BY MAGNITUDE, because vTanLocal CAN GO NEGATIVE. It is omega*r + vCross*cos(psi): at
                    // idle the rpm is low and in a spin the crossflow is large, so on the retreating side of the disc
                    // the blade's own tangential speed reverses. Written as (-0.5*vTan, +0.5*vTan) the bounds then
                    // swap and Math.Clamp THROWS - "'3.88' cannot be greater than '-3.88'". That is exactly the case
                    // Ty asked about on 2026-11-09: pull the power in a flat spin, and the blade-element solve throws
                    // rather than returning a weaker propeller.
                    double wiLim = 0.5 * Math.Abs(vTanLocal);
                    wiNew = Math.Clamp(wiNew, -wiLim, wiLim);
                    double nvi = vi + 0.4 * (viNew - vi), nwi = wi + 0.4 * (wiNew - wi);
                    if (Math.Abs(nvi - vi) < tol && Math.Abs(nwi - wi) < tol) { vi = nvi; wi = nwi; break; }
                    vi = nvi; wi = nwi;
                }
                viPrev[k] = vi; wiPrev[k] = wi;
                double q = 0.5 * rhoKgM3 * w2 * g.Blades * chord * dx * g.RadiusM / nAz;
                double dT = q * cn, dQ = q * ct * r;
                thrust += dT; torque += dQ;
                angMom += dQ;
                wiSum += wi; viSum += vi;
                if (!axial)
                {
                    normal += ct * q * cosPsi;                 // the in-plane force, from the same elements
                    thrustMom += dT * r * cosPsi;              // and WHERE the thrust was made: P-factor's own moment
                }
            }
            warm = true;
            if (wakeRadii is not null && wakeSwirl is not null && i < wakeRadii.Length && i < wakeSwirl.Length)
            {
                // Where this annulus ENDS UP. Mass through it is conserved as the tube contracts, so the annulus at
                // disc radius r arrives in the far wake at r*sqrt((V + vi)/(V + 2 vi)) — the whole contraction, taken
                // annulus by annulus instead of as one number for the disc.
                double viBar = viSum / nAz, wiBar = wiSum / nAz;
                double ratio = Math.Clamp(Math.Sqrt((vAxialMs + viBar) / Math.Max(1e-6, vAxialMs + 2.0 * viBar)), 0.5, 1.5);
                wakeRadii[i] = r * ratio;
                // AND THE SWIRL FOLLOWS IT INWARD. Past the disc nothing applies a torque to the air, so r * v_t is
                // conserved along the streamtube: as the tube contracts the swirl SPEEDS UP, by exactly the ratio the
                // radius shrank. Reporting 2 wi at the contracted radius instead understates it by 6 % in cruise and
                // 41 % standing still, where the contraction is the full 1/sqrt(2).
                wakeSwirl[i] = 2.0 * wiBar / Math.Max(1e-6, ratio);
            }
        }
        double power = torque * omega;
        double eta = power > 1e-6 ? thrust * vAxialMs / power : 0.0;
        // The swirl the slipstream carries: C = Q / (rho * A * V_disc), the vortex strength the traced jet wants.
        double disc = Math.PI * g.RadiusM * g.RadiusM;
        double vDisc = Math.Max(1.0, vAxialMs + 0.5 * Math.Max(0.0, Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + 2.0 * thrust / Math.Max(1e-6, rhoKgM3 * disc))) - vAxialMs));
        double swirlC = angMom / Math.Max(1e-6, rhoKgM3 * disc * vDisc);
        Iterations += iterations;
        SimProfile.End(SimProfile.Scope.Blade, tBlade);
        return new Result(thrust, torque, power, eta, swirlC, normal, 0.0, thrustMom);
    }

    /// <summary>
    /// THE BLADE ANGLE THAT ABSORBS A GIVEN TORQUE, at this rpm and airspeed. A constant-speed propeller's governor
    /// does exactly this: it coarsens the blade until the propeller's torque matches what the engine is making, and
    /// the rpm then stays where it was put. Monotone in pitch over the working range (coarser absorbs more), so a
    /// bisection is enough and cannot pick the wrong root.
    /// </summary>
    public static double PitchForTorqueDeg(int blades, double radiusM, double activityFactor, double hubFraction,
                                           double targetTorqueNm, double rpm, double vAxialMs, double rhoKgM3,
                                           double loDeg = 5.0, double hiDeg = 65.0)
    {
        double Q(double pitch) => Solve(new Geometry(blades, radiusM, pitch, activityFactor, hubFraction), vAxialMs, 0.0, rpm, rhoKgM3).TorqueNm;
        double qLo = Q(loDeg), qHi = Q(hiDeg);
        if (targetTorqueNm <= qLo) return loDeg;
        if (targetTorqueNm >= qHi) return hiDeg;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (loDeg + hiDeg);
            if (Q(mid) < targetTorqueNm) loDeg = mid; else hiDeg = mid;
        }
        return 0.5 * (loDeg + hiDeg);
    }

    /// <summary>
    /// WHERE A DEAD ENGINE'S PROPELLER TURNS, AND WHAT IT COSTS. With no engine torque the propeller settles at the rpm
    /// where its OWN torque is zero: below it the air is driving the blade round, above it the blade is absorbing, and
    /// the crossing is single because torque climbs monotonically with rpm at a fixed blade angle. The thrust at that
    /// rpm is negative, and that is the windmilling drag — which is the whole reason the blade angle on oil loss
    /// matters. A fine blade meets the flow at a large angle, is driven hard, spins fast and drags like a disc; a
    /// coarse one sits closer to the flow, turns slowly and drags far less. That spread is the glide, and it is not
    /// something a single authored Cd can carry.
    ///
    /// The engine is treated as turning FREELY: <see cref="PistonEngine.PowerW"/> clamps its open fraction at zero, so
    /// there is no motoring friction in the model to subtract. A seized engine is the other extreme and is not this —
    /// it would stop the prop, which <see cref="Windmilling.Turning"/> already reports for a blade too coarse to be
    /// driven at all.
    /// </summary>
    public static Windmilling Windmill(in Geometry g, double vAxialMs, double rhoKgM3, double maxRpm,
                                       Func<double, double>? engineTorqueAt = null)
    {
        if (g.RadiusM <= 0.0 || vAxialMs <= 1.0 || maxRpm <= 1.0) return default;
        Geometry blade = g;                                  // a lambda cannot close over an 'in' parameter
        // THE BALANCE IS AGAINST THE ENGINE, NOT AGAINST NOTHING. Ty 2026-11-10: "You are not accounting for the
        // cranking resistance of the engine". Steady state is I·dw/dt = Q_engine − Q_prop = 0, so the air must drive
        // the blade hard enough to turn the engine over: Q_prop = Q_engine, a NEGATIVE number
        // (PistonEngine.MotoringTorqueNm). Blade torque rises monotonically with rpm — the air drives it at low rpm
        // and it absorbs at high rpm — so the crossing is single, and it sits BELOW the zero-torque rpm by however
        // much the engine resists. Pass no delegate and the engine is treated as free, which is only right for a
        // propeller with nothing behind it.
        double Excess(double rpm) => Solve(blade, vAxialMs, 0.0, rpm, rhoKgM3).TorqueNm - (engineTorqueAt?.Invoke(rpm) ?? 0.0);

        // THE HIGHEST CROSSING, NOT THE FIRST — and that distinction only appeared once the engine stopped being a
        // constant. With PistonEngine's boundary friction the resistance FALLS steeply with rpm while the blade's
        // torque RISES, so Excess can cross zero twice: the lower crossing is the STALL point, an unstable balance the
        // propeller falls away from in either direction, and the upper one is where a windmilling propeller actually
        // runs. A plain bisection from the bottom finds the unstable one and reports a propeller turning 31 rpm at
        // 112 mph, which is how this was caught.
        double top = maxRpm;
        while (Excess(top) < 0.0 && top < 8.0 * maxRpm) top *= 2.0;   // a dead engine imposes no red line
        if (Excess(top) < 0.0) return At(top);

        // Walk down from the top for the last speed the air is still driving it, which brackets the stable crossing.
        const int steps = 48;
        double lo = double.NaN;
        for (int i = steps - 1; i >= 1; i--)
        {
            double r = top * i / steps;
            if (Excess(r) < 0.0) { lo = r; break; }
        }
        if (double.IsNaN(lo)) return default;                // resisting at every speed: the propeller stands still

        double hi = top;
        for (int i = 0; i < 24; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Excess(mid) < 0.0) lo = mid; else hi = mid;
        }
        return At(0.5 * (lo + hi));

        Windmilling At(double rpm) => new(rpm, -Solve(blade, vAxialMs, 0.0, rpm, rhoKgM3).ThrustN);
    }

    /// <summary>A freely windmilling propeller: the rpm the air turns it at and the drag (N, positive) it makes there.</summary>
    public readonly struct Windmilling
    {
        public readonly double Rpm, DragN;
        public Windmilling(double rpm, double dragN) { Rpm = rpm; DragN = dragN; }
        /// <summary>False when the blade is too coarse for the air to drive it — the propeller is stopped.</summary>
        public bool Turning => Rpm > 0.0;
    }

    /// <summary>
    /// THE GOVERNOR'S PITCH STOPS, DERIVED (Ty 2026-11-09): "The prop pitch limits should be set so that the low pitch
    /// stop allows full rpm at 26 inches of manifold pressure at sea level for naturally aspirated engines and the
    /// high pitch stop prevents an overspeed until vd at full power with Vd being 110% of the vne."
    ///
    /// Both stops are therefore MEASUREMENTS of the blade, not numbers to pick:
    ///
    ///   FINE  the blade angle that lets the engine reach rated rpm standing still on 26 inHg. That is the run-up
    ///         check a pilot actually does, and it is what makes the fine stop fine enough to give full rpm for
    ///         take-off without being so fine the propeller runs away.
    ///   COARSE the blade angle needed to hold rated rpm at Vd at full power. Any coarser is unnecessary; any finer
    ///         and the governor runs out of blade before Vd and the engine overspeeds in the dive.
    ///
    /// Power at 26 inHg comes from the naturally aspirated engine's near-linear fall with manifold pressure, taken
    /// through the friction intercept near 10 inHg: (26 - 10)/(29.92 - 10) = 0.80 of rated. No tuning constant: full
    /// throttle at sea level IS 29.92 inHg, and the intercept is a property of a piston engine.
    /// </summary>
    public readonly struct Stops
    {
        public readonly double FineDeg, CoarseDeg;
        public Stops(double fine, double coarse) { FineDeg = fine; CoarseDeg = coarse; }
    }

    public const double RunUpManifoldInHg = 26.0, SeaLevelManifoldInHg = 29.92, FrictionManifoldInHg = 10.0;
    public static double PowerFractionAtManifold(double inHg)
        => Math.Clamp((inHg - FrictionManifoldInHg) / (SeaLevelManifoldInHg - FrictionManifoldInHg), 0.0, 1.0);

    public static Stops GovernorStops(int blades, double radiusM, double activityFactor, double hubFraction,
                                      double ratedPowerW, double ratedRpm, double vneMs, double rhoKgM3 = 1.225)
    {
        double omega = ratedRpm * 2.0 * Math.PI / 60.0;
        double qRated = ratedPowerW / Math.Max(1e-6, omega);
        double qRunUp = qRated * PowerFractionAtManifold(RunUpManifoldInHg);
        double fine = PitchForTorqueDeg(blades, radiusM, activityFactor, hubFraction, qRunUp, ratedRpm, 0.0, rhoKgM3);
        double coarse = PitchForTorqueDeg(blades, radiusM, activityFactor, hubFraction, qRated, ratedRpm, 1.1 * vneMs, rhoKgM3);
        return new Stops(fine, Math.Max(coarse, fine + 1.0));
    }

    /// <summary>
    /// PRANDTL'S LOSS FACTOR, (2/pi) acos(exp(-f)), tabulated. It is wanted twice per inner iteration — the tip and the
    /// root — and an Exp plus an Acos each was a quarter of this model's whole cost. The function is smooth, monotone
    /// and bounded (0 at f = 0, 1 as f grows), so a table with linear interpolation is as exact as the step it is read
    /// at.
    ///
    /// TABULATED IN sqrt(f), NOT IN f, and that is not a detail. Near f = 0 the factor goes as sqrt(2f) — its second
    /// derivative is infinite there — so a uniform table in f is wrong by order sqrt(h) in its first interval however
    /// fine it is, and f IS small at the tip element whenever the inflow angle is large (standing still, the whole
    /// static thrust case). In u = sqrt(f) the same function starts off linear, with a bounded second derivative, and
    /// linear interpolation is then as exact as the step.
    ///
    /// Measured (tools/SimCost --azimuth, the "nAz 16, table loss" row — the azimuth count held at the old 16 so the
    /// table is the only difference): over the fleet's five fitted blade settings and 540 conditions each, thrust moves
    /// by at most 5.9e-5 of itself, torque by 2.7e-4, and the thrust centre by 1.5 mm on a 1.0 m propeller radius. Four
    /// of the five blades are at the 1e-6 level; the Extra's 1.5 mm is not interpolation but conditioning — the thrust
    /// centre is a moment DIVIDED by a thrust, and its 3-blade disc passes through zero thrust inside this grid. The
    /// published figures this model is calibrated against are good to a per cent at best.
    ///
    /// Above f = <see cref="LossFMax"/> = 16 the factor is 1 to better than 8e-8, so it is returned flat; the old cut
    /// at 8 would have left a step of 2.1e-4 there, which is the same size as the whole error budget.
    /// </summary>
    private const double LossFMax = 16.0;
    private const int LossN = 2048;
    private static readonly double LossUMax = Math.Sqrt(LossFMax);
    private static readonly double LossScale = LossN / Math.Sqrt(LossFMax);
    private static readonly double[] LossTable = BuildLossTable();

    private static double[] BuildLossTable()
    {
        var t = new double[LossN + 2];
        for (int i = 0; i <= LossN + 1; i++)
        {
            double u = LossUMax * i / LossN;
            t[i] = 2.0 / Math.PI * Math.Acos(Math.Exp(-u * u));
        }
        return t;
    }

    /// <summary>A probe's seam: true takes the exact Exp/Acos instead of the table, so the table's error can be
    /// MEASURED rather than argued (tools/SimCost --azimuth, HotPathCostTests). False in the sim.</summary>
    public static bool ExactPrandtlLoss;

    private static double AcosExpNeg(double f)
    {
        if (ExactPrandtlLoss) return 2.0 / Math.PI * Math.Acos(Math.Exp(-Math.Max(0.0, f)));
        if (f >= LossFMax) return 1.0;
        if (f <= 0.0) return 0.0;
        double u = Math.Sqrt(f) * LossScale;
        int i = (int)u;
        double frac = u - i;
        double[] t = LossTable;
        return t[i] + frac * (t[i + 1] - t[i]);
    }

    /// <summary>
    /// A propeller blade section: Clark-Y-like, which is what most of this fleet's blades are. Linear to the stall,
    /// then a flat-plate post-stall so a stalled root (which every propeller has at low speed) behaves instead of
    /// running away. Not a table because a propeller blade spends its life between 0 and 10 degrees.
    /// </summary>
    private static (double cl, double cd) Section(double alphaRad)
    {
        const double A0 = -2.0 * Math.PI / 180.0, Slope = 5.9, ClMax = 1.25;
        double a = alphaRad - A0;
        double clLin = Slope * a;
        double cl, cd;
        if (Math.Abs(clLin) <= ClMax)
        {
            cl = clLin;
            cd = 0.008 + 0.020 * cl * cl;
        }
        else
        {
            double sign = Math.Sign(clLin);
            double over = Math.Abs(a) - ClMax / Slope;
            cl = sign * Math.Max(0.6, ClMax - 0.7 * over);              // post-stall decay toward a plate
            cd = 0.030 + 1.1 * Math.Sin(Math.Min(Math.PI / 2, Math.Abs(over))) * Math.Sin(Math.Min(Math.PI / 2, Math.Abs(over)));
        }
        return (cl, cd);
    }
}

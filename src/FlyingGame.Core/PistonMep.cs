using System;

namespace FlyingGame.Core;

/// <summary>
/// WHAT A PISTON ENGINE SPENDS ON ITSELF: friction and pumping, as mean effective pressures, so that engine braking
/// RISES WITH RPM AND WITH THROTTLING the way a real engine's does. Ty asked for idle rpm to be "a function of idle
/// throttle and prop inflow driving the propeller against the pumping loads and friction"; the inflow half already
/// worked, and this is the pumping-and-friction half, which did not exist.
///
/// THE DEFECT THIS REPLACES. <see cref="PistonEngine.PowerW"/>'s law was
/// <c>P = P_rated · (MP − 10)/(MP_sea − 10) · (rpm/rpm_rated)</c>, so the TORQUE at a fixed throttle was
/// <c>Q_rated · (MP − 10)/(MP_sea − 10)</c> — a number with no rpm in it at all. Measured on the Pitts before this:
/// +22.6 N·m at 700, 1,200, 1,800 and 2,500 rpm alike, where a real engine at a cracked throttle is braking hard by
/// 2,500. And the 10 inHg intercept could not simply be split into its parts: taken literally it is 345 N·m of
/// friction on a 686 N·m engine, which is half the engine. The model was missing a term, not mis-apportioning one.
///
/// THE DECOMPOSITION IS HEYWOOD'S (Internal Combustion Engine Fundamentals, ch. 13). For a four-stroke,
///
///     BMEP = IMEP_gross − FMEP − PMEP,        Q = BMEP · V_d / (4π)
///
/// gross indicated work less the rubbing-and-auxiliary friction and less the pumping loop. Each of the three is below.
///
/// ---------------------------------------------------------------------------------------------------------------
/// 1. THE MANIFOLD HAD NO RPM IN IT EITHER, AND THAT IS THE BIGGER HALF.
///
/// This is the finding that decided the shape of the fix. Adding a published friction rise to the OLD manifold law
/// moves the Pitts' 2,500 rpm part-throttle torque from +22.6 N·m only to +15.7: still pulling, nowhere near braking.
/// Friction alone cannot do it and nothing in Chen-Flynn says it should.
///
/// What brakes a real engine at a shut throttle is that the throttle plate is a FLOW RESTRICTION and the engine is a
/// PUMP whose demand goes as its speed. At 700 rpm a nearly shut plate still keeps a respectable manifold pressure
/// behind it; at 2,500 rpm the same plate cannot pass the air, the manifold falls away to a few inches, the indicated
/// work collapses AND the pumping loop deepens. Both terms move the same way at once. That is why a closed throttle
/// brakes, and it is what "PMEP from the throttling pressure drop" means.
///
/// So the manifold is solved rather than assumed. Orifice flow through the plate equals the engine's displacement
/// flow at manifold density:
///
///     C_d · A · sqrt(2 ρ_amb (p_amb − MP))  =  (MP/RT) · η_v · V_d · N/2
///
/// Square both sides, write u = MP/p_amb, and everything that is not the throttle or the speed collapses into one
/// group:
///
///     β u² + u − 1 = 0,   β = β_wot · (N/N_rated)² / x²,   u = (−1 + sqrt(1 + 4β)) / (2β)
///
/// with x the open AREA fraction. One constant, <see cref="WideOpenManifoldInHg"/>, fixes β_wot, and it is a published
/// number rather than a fitted one: what a naturally aspirated engine's manifold actually reads at full throttle at
/// rated rpm at sea level, which is a couple of inches under ambient because the induction system is not free.
/// Everything else — discharge coefficient, valve area, volumetric efficiency, charge temperature — is inside β_wot
/// and none of it is separately tunable, which is the point.
///
/// THE LEVER IS NOT THE AREA. A throttle lever drives a butterfly plate through about a right angle, and a butterfly's
/// projected open area goes as (1 − cos θ), not as θ. That is geometry, not a curve: it is why the first third of the
/// lever does most of the work on a real aeroplane and why half throttle is nowhere near half manifold pressure. See
/// <see cref="ThrottleAreaFraction"/>.
///
/// ---------------------------------------------------------------------------------------------------------------
/// 2. GROSS INDICATED WORK, AND WHY IT IS NOT SIMPLY PROPORTIONAL TO MANIFOLD PRESSURE.
///
/// The fuel is metered to the air, so indicated work follows the FRESH charge. At a shut throttle the clearance volume
/// is full of exhaust at something near ambient, and it re-expands into the cylinder before any fresh charge can
/// follow it, so a large part of what the engine swallows is its own exhaust. Heywood's residual fraction,
///
///     x_r = (1/CR) · (p_exhaust / p_intake)^(1/γ)
///
/// is 12 % at full throttle on a 8.7:1 engine and 38 % at the idle stop, and the fresh charge is scaled by
/// (1 − x_r) against its value at rated. No constant: the compression ratio is the engine's own and γ is a gas
/// property. See <see cref="ResidualGasFraction"/> and <see cref="FreshChargeFactor"/>.
///
/// ---------------------------------------------------------------------------------------------------------------
/// 3. FRICTION: CHEN-FLYNN FOR THE SHAPE, GAGG AND FERRAR FOR THE LEVEL.
///
/// The engineering form is Chen-Flynn,
///
///     FMEP = A + B·P_max + C·S_p + D·S_p²
///
/// in mean piston speed S_p = 2·S·N/60 — a constant term, a term in the gas load the rings and bearings carry, and
/// the two speed terms that give the rpm dependence this whole file is about. See <see cref="FrictionShape"/> for the
/// constants, the engine class they were fitted to, and THE PISTON-SPEED RANGE THEY ARE VALID OVER, which matters
/// here more than usual: these are 2,700 rpm aircraft engines, so S_p tops out near 10 m/s where an automotive engine
/// at 5,000 rpm is at 17, and a correlation used outside its range is a guess with references attached.
///
/// ITS LEVEL IS NOT A NEW CONSTANT. It is already in this model, in the altitude lapse, and has been since Ty
/// corrected the altitude behaviour on 2026-11-09. Gagg and Ferrar's relation
/// (<see cref="PistonEngine.AltitudePowerFraction"/>),
///
///     P / P_rated = (σ − 0.117) / 0.883
///
/// is a FRICTION STATEMENT: indicated power falls with density, friction does not, and the engine quits making power
/// at σ = 0.117 precisely because friction is 11.7 % of sea-level gross indicated power. So
/// <c>FMEP_rated = 0.117 · IMEP_rated − PMEP_wot</c>, and Chen-Flynn is used only as a SHAPE, normalised to agree
/// with it at rated rpm.
///
/// THE PAYOFF IS THAT THE ALTITUDE BEHAVIOUR IS NOW DERIVED RATHER THAN APPLIED. At wide-open throttle the three
/// terms above reduce, exactly, to (σ − 0.117)/0.883 — see PistonEngineTests, which asserts it. The lapse Ty
/// validated is not re-litigated by this change; it falls out of it, and it is now right at PART throttle too, where
/// a separate multiplying factor was never right.
/// </summary>
public static class PistonMep
{
    // ---- 1. the manifold -------------------------------------------------------------------------------------

    /// <summary>
    /// WHAT THE MANIFOLD READS AT FULL THROTTLE AT RATED RPM AT SEA LEVEL, inHg — the one constant that sets the
    /// throttle-orifice group β_wot, and the only place the induction system's size enters.
    ///
    /// It is not 29.92. A naturally aspirated engine at full throttle still pulls its air through an air filter, an
    /// induction elbow, a carburettor or injector throat and a valve, and the manifold sits a couple of inches below
    /// ambient at rated speed. Lycoming's and Continental's sea-level performance curves are drawn against the
    /// manifold pressure the engine actually achieves, and the full-throttle line at rated rpm sits near 28.5 inHg
    /// against a 29.92 inHg day.
    ///
    /// SENSITIVITY: this sets how fast the manifold falls away as the throttle shuts, so it is the single number the
    /// part-throttle re-pricing is most sensitive to. β_wot = (1 − u)/u² with u = this / 29.92, which is 0.052 here
    /// and would be 0.023 at 29.2 inHg or 0.094 at 27.7 — a factor of four across a range of 1.5 inHg. It wants a
    /// per-type published figure more than anything else in this file.
    /// </summary>
    /// <summary>A probe's seam, the way <see cref="BladeElement.ExactPrandtlLoss"/> is one: a field rather than a
    /// constant so EngineBrakingProbe can SWEEP it and measure what the fleet is sensitive to, instead of the
    /// sensitivity being argued. The sim never writes it.</summary>
    public static double WideOpenManifoldInHg = 28.5;

    /// <summary>
    /// HOW MUCH A SHUT THROTTLE STILL PASSES, as a fraction of the wide-open area. A butterfly plate cannot seal — it
    /// is a disc in a bore — and on most installations there is a drilled idle air bleed beside it as well.
    ///
    /// THIS IS THE ONE CONSTANT IN THIS FILE I COULD NOT SOURCE, and it is named rather than buried for that reason.
    /// It does NOT set the idle, which is solved per type by <see cref="PistonEngine.IdleBleedThrottle"/> against each
    /// type's own published idle rpm; the idle stop comes out above this, around 1.5 % of area on the Pitts. What it
    /// does set is how hard a DEAD engine with the lever closed pumps, which is a term in the windmilling drag and in
    /// where a dead propeller stops — so it is one of the things the per-type dry boundary excess absorbs, and moving
    /// it moves those five solved constants rather than anything a pilot reads.
    /// </summary>
    public const double ShutThrottleAreaFraction = 0.004;

    /// <summary>
    /// THE OPEN AREA A LEVER POSITION COMMANDS, as a fraction of wide open. A throttle lever drives a butterfly plate
    /// through about a right angle, and the plate's projected open area goes as (1 − cos θ): nothing near shut, then
    /// most of the area in the last half of the travel. Geometry, not a tuning curve.
    ///
    /// It is why half throttle is not half manifold pressure — on the Pitts at 2,400 rpm half lever is 0.31 of the
    /// area and about 22.7 inHg — and it is the shape every pilot knows, where the bottom of the quadrant is where
    /// all the control is.
    /// </summary>
    public static double ThrottleAreaFraction(double throttle01)
    {
        double t = Math.Clamp(throttle01, 0.0, 1.0);
        double open = 1.0 - Math.Cos(0.5 * Math.PI * t);        // the butterfly's projected area, 0 shut to 1 wide open
        return ShutThrottleAreaFraction + (1.0 - ShutThrottleAreaFraction) * open;
    }

    /// <summary>The throttle-orifice group at wide-open throttle and rated rpm: β_wot = (1 − u)/u², u = MP/p_amb.</summary>
    public static double WideOpenOrificeGroup
    {
        get
        {
            double u = WideOpenManifoldInHg / BladeElement.SeaLevelManifoldInHg;
            return (1.0 - u) / (u * u);
        }
    }

    /// <summary>
    /// MANIFOLD PRESSURE, Pa, as the balance between what the throttle can pass and what the engine is swallowing —
    /// so it falls with rpm at a fixed lever, which is the term the old law was missing. See the class remarks for
    /// the derivation; <paramref name="rpm"/> at or below zero returns ambient, which is what a stopped engine's
    /// manifold does.
    /// </summary>
    public static double ManifoldPa(double throttle01, double rpm, double ratedRpm, double ambientPa)
    {
        if (rpm <= 0.0 || ratedRpm <= 0.0) return ambientPa;
        double x = ThrottleAreaFraction(throttle01);
        double speed = rpm / ratedRpm;
        double beta = WideOpenOrificeGroup * speed * speed / (x * x);
        if (beta <= 1e-12) return ambientPa;
        double u = (-1.0 + Math.Sqrt(1.0 + 4.0 * beta)) / (2.0 * beta);
        return ambientPa * Math.Clamp(u, 0.0, 1.0);
    }

    // ---- 2. the fresh charge ---------------------------------------------------------------------------------

    /// <summary>The ratio of specific heats used for the residuals' re-expansion. Burnt gas, so 1.3 rather than air's
    /// 1.4 — the same figure <see cref="PistonCompression"/> uses for its polytropic charge.</summary>
    public const double ResidualGamma = 1.3;

    /// <summary>
    /// HOW MUCH OF THE THROTTLING DEPRESSION IS ACTUALLY LOST, measured. Ideal pumping says the loop costs the full
    /// difference between exhaust back pressure and manifold pressure, so one inHg of manifold vacuum is 0.491 psi
    /// of PMEP. NACA Report 262 (Sparrow and Thorne, <i>Friction of Aviation Engines</i>, 1927) motored seven
    /// aviation engines against the throttle and found the loss rises almost linearly with manifold suction but at
    /// only PART of that rate: Engine A 0.26 psi per inHg at 1,600 rpm and 0.29 at 1,400; Engine C 0.39 at 1,800 and
    /// 0.38 at 1,600. Against the ideal 0.491 that is 0.53 to 0.79 of the theoretical pumping work, and 0.66 is the
    /// middle of those four measurements.
    ///
    /// Some of the depression comes back — the charge is re-expanded on the exhaust stroke, blowdown does not happen
    /// at constant pressure, and the cylinder never reaches the full manifold depression in the time available. The
    /// mechanism is not separated here; what is used is the number they measured.
    ///
    /// IT MATTERS MOST WHERE THERE IS NO COMBUSTION TO SWAMP IT. On a dead engine with the lever closed the pumping
    /// loop is nearly the whole of the resistance, and taking it at the ideal rate made a windmilling propeller far
    /// too draggy: both J-3s stopped at 22-23 m/s against a 16-17 m/s stall speed, and could not be brought back to
    /// Ty's "the prop stops right at level flight stall speed" anchor even with their dry boundary friction taken to
    /// zero. That is what sent me looking for a measurement, and NACA 262 had one.
    /// </summary>
    public const double MeasuredPumpingFraction = 0.66;

    /// <summary>The pumping MEP, Pa: the throttling depression at the rate NACA 262 measured rather than the ideal
    /// one. See <see cref="MeasuredPumpingFraction"/>.</summary>
    public static double PumpingMepPa(double manifoldPa, double exhaustPa)
        => MeasuredPumpingFraction * Math.Max(0.0, exhaustPa - manifoldPa);

    /// <summary>Heywood's residual gas fraction: the clearance volume full of exhaust at back pressure, re-expanded
    /// into the cylinder before any fresh charge can follow. 12 % at full throttle on an 8.7:1 engine, near 40 % at
    /// the idle stop.</summary>
    public static double ResidualGasFraction(double manifoldPa, double exhaustPa, double compressionRatio)
    {
        double cr = Math.Max(1.5, compressionRatio);
        double mp = Math.Max(1.0, manifoldPa);
        return Math.Clamp(Math.Pow(exhaustPa / mp, 1.0 / ResidualGamma) / cr, 0.0, 0.95);
    }

    /// <summary>How much of the charge is FRESH at this manifold pressure, against how much is fresh at rated: the
    /// factor gross indicated work carries on top of its proportionality to manifold pressure.</summary>
    public static double FreshChargeFactor(double manifoldPa, double exhaustPa, double ratedManifoldPa,
                                           double ratedExhaustPa, double compressionRatio)
    {
        double here = 1.0 - ResidualGasFraction(manifoldPa, exhaustPa, compressionRatio);
        double rated = 1.0 - ResidualGasFraction(ratedManifoldPa, ratedExhaustPa, compressionRatio);
        return rated <= 1e-6 ? 1.0 : Math.Max(0.0, here / rated);
    }

    // ---- 3. friction -----------------------------------------------------------------------------------------

    /// <summary>
    /// THE STROKE, m, from the displacement the type already authors. Mean piston speed is the variable Chen-Flynn
    /// lives in and the variable a Stribeck transition lives in (<see cref="PistonEngine.BoundaryRpmScale"/> already
    /// argues the same point), so the stroke has to come from somewhere, and no config authors a bore and a stroke.
    ///
    /// V_d = n · (π/4) · B² · S with B = r·S gives S = (4 V_d / (π n r²))^(1/3), and
    /// <see cref="FleetBoreStrokeRatio"/> is r. CHECKED AGAINST THE TWO ENGINES THIS FLEET ACTUALLY FLIES: the
    /// Lycoming 540 (5.125 in bore, 4.375 in stroke, six cylinders, 541.5 in³) comes out at 0.1124 m against a real
    /// 0.1111, +1.2 %; the Continental C-85 (4.0625 × 3.875, four, 188 in³) at 0.0905 against a real 0.0984, −8 %.
    /// An 8 % error in stroke is an 8 % error in piston speed, which moves the speed-dependent part of FMEP by about
    /// the same and total FMEP by rather less. A type that wants better than that should author its stroke.
    /// </summary>
    public static double StrokeM(double displacementM3, int cylinders)
    {
        int n = Math.Max(1, cylinders);
        double r = FleetBoreStrokeRatio;
        return Math.Pow(4.0 * Math.Max(1e-9, displacementM3) / (Math.PI * n * r * r), 1.0 / 3.0);
    }

    /// <summary>
    /// BORE OVER STROKE for this fleet. The Lycoming parallel-valve four and six are 5.125/4.375 = 1.171 (O-320 is
    /// 5.125/3.875 = 1.323 on the same bore), the Continental C-85 and O-200 are 4.0625/3.875 = 1.048. 1.15 is the
    /// middle of that and within 12 % of every one of them, which is the same spread
    /// <see cref="PistonEngine.BoundaryRpmScale"/> already accepts for the same reason.
    /// </summary>
    public const double FleetBoreStrokeRatio = 1.15;

    /// <summary>Mean piston speed, m/s: S_p = 2·S·N/60. About 10 m/s at 2,700 rpm on a Lycoming 540 and 2.6 at a
    /// 700 rpm idle.</summary>
    public static double MeanPistonSpeedMs(double strokeM, double rpm)
        => 2.0 * Math.Max(0.0, strokeM) * Math.Max(0.0, rpm) / 60.0;

    /// <summary>
    /// WHERE CHEN-FLYNN STOPS BEING TRUE AT THE BOTTOM, m/s of mean piston speed. The correlation is a quadratic in
    /// piston speed, so it keeps falling all the way to zero; real aviation engines do not.
    ///
    /// NACA Report 262, Sparrow and Thorne, <i>Friction of Aviation Engines</i> (1927) — motoring tests on engines of
    /// very nearly this fleet's geometry, one of them 5.51 x 5.91 in — says it twice: "at very low speeds the F.M.E.P.
    /// changes to a much less extent with change of speed and in some instances remains almost constant". Their data
    /// runs over S_p 5 to 11 m/s and sits at 10 to 20 psi, which is 0.7 to 1.4 bar — well under the 2 to 3 bar an
    /// automotive correlation gives at the same piston speeds, which is the other half of why Chen-Flynn is used here
    /// only for its SHAPE and never for its level.
    ///
    /// This fleet idles at S_p 1.5 to 2.6 m/s, FAR below where that data begins, so the quadratic is being read
    /// outside its range in exactly the regime this lane is about. Holding the two speed terms at their value at the
    /// bottom of NACA 262's range is what that report describes, and it is the conservative reading: it cannot make
    /// friction fall below a measured floor, which is the error that was actually being made.
    ///
    /// It also fixes something that was visibly wrong without it: with the quadratic running free to zero the Pitts
    /// made 12 % MORE torque at 700 rpm than at its rating, where a real Lycoming peaks about 4 % up and does it
    /// nearer 2,000 rpm.
    /// </summary>
    public const double LowSpeedPistonSpeedFloorMs = 5.0;

    /// <summary>
    /// WHAT A PISTON ENGINE AT IDLE DOES NOT GET BACK OUT OF ITS CHARGE. Gross indicated work is taken above as
    /// proportional to the fresh charge, and between about 1,800 rpm and the red line that is right — it puts this
    /// model's zero-torque manifold pressure at 6.6 inHg where Lycoming's own sea-level charts extrapolate to 6.4,
    /// 6.9 and 7.2 (O-360-A at 2,700, O-540-A at 2,575, IO-540-K at 2,700). Down at idle it is badly wrong, and
    /// there is a published measurement that says so.
    ///
    /// THE ANCHOR, AND IT IS THE J-3'S OWN ENGINE. Continental Operator's Manual X30012, Table XXI "Test Operating
    /// Limits", publishes for the C-85 an idling speed of 475-525 rpm at 17 inHg of manifold VACUUM — 12.9 inHg
    /// absolute against a 29.92 inHg cell — and, in the same table, a full-throttle speed on the test club of
    /// 2,575-2,625 rpm. A club is a fixed-pitch propeller, so its torque goes as the square of the speed, and those
    /// two lines together say what the engine makes at idle:
    ///
    ///     Q(500 rpm, 12.9 inHg) = Q_rated * (500 / 2600)^2 = 235.1 * 0.03698 = 8.7 N.m,  i.e. BMEP 0.354 bar
    ///
    /// A SOURCED PART-THROTTLE POINT AT IDLE, which is the thing no Lycoming chart carries — their lines stop at
    /// 18 inHg and no Lycoming manual publishes an idle manifold pressure at all. Against it, the charge-proportional
    /// law over-predicts idle BMEP by about 2.3 bar: at idle an engine is nowhere near getting indicated work in
    /// proportion to what it swallows. Heat loss per cycle goes as 1/N, the charge is cold, slow-burning, heavily
    /// diluted by residuals and on a carburettor badly mixed, and the magneto timing is fixed for a speed four times
    /// higher.
    ///
    /// SO IT IS CARRIED AS A LOSS OF INDICATED EFFICIENCY, 1 at the rating and collapsing at low speed:
    ///
    ///     eta_c(N) = 1 / (1 + b * (N_rated / N - 1)^2)
    ///
    /// TWO PUBLISHED CONSTRAINTS FIX ITS TWO PARAMETERS, and neither of them is a test in this repository.
    ///
    /// The COEFFICIENT b is solved from Continental's idle above — one equation, one unknown.
    ///
    /// The EXPONENT is fixed by a second, independent published fact, and getting it wrong was visible: Lycoming's
    /// IO-540-K sheet gives bhp@24 inHg / rpm as 0.0856 to 0.0859 across all ten lines from 1,800 to 2,700 rpm, and
    /// power over speed is torque, so AT A FIXED MANIFOLD PRESSURE THAT ENGINE'S TORQUE IS FLAT TO 0.15 % over its
    /// whole working range. A first-power 1/N loss cannot do both: anchored at Continental's 500 rpm idle it still
    /// costs 15 % at 1,800 rpm and put a 16.5 % tilt across a span Lycoming say is flat. Squaring it concentrates
    /// the collapse where the measurement says it lives — 2 % at 1,800, a factor of two and a half by 500 — and both
    /// published numbers are then satisfied at once.
    ///
    /// AND IT IS HARMONIC RATHER THAN SUBTRACTIVE so it approaches zero without reaching it. Written as
    /// 1 - b(N_rated/N - 1) the factor hits ZERO at 322 rpm on the C-85 and goes negative below — an engine that
    /// cannot be cranked, far outside the 500 rpm it is anchored at. That broke the blade fitting outright: the
    /// static balance could not spin a propeller up at all and every fixed-pitch blade in the fleet pinned at its
    /// finest bound.
    ///
    /// one constant, solved from that one published point — one equation in one unknown, the way
    /// <see cref="PistonEngine.BoundaryExcessFor"/>'s excess and <see cref="PistonCompression"/>'s peak are — and NOT
    /// fitted to any test in this repository. <see cref="PistonMepTests"/> asserts the C-85 lands back on Continental's
    /// published idle, so the anchor cannot quietly drift.
    ///
    /// THE 1/N IS THE HEAT-LOSS SCALING AND NOT A CHOSEN CURVE: the time a charge spends in the cylinder goes as 1/N,
    /// and the heat it loses to the walls goes with that time, so the fraction of the fuel's energy that reaches the
    /// piston falls as 1/N. It is the standard reason an engine's indicated efficiency collapses at cranking speeds.
    ///
    /// WHAT IT IS NOT. It is not a decomposition — it cannot be, from one point. The brief this lane was written
    /// from already said the old 10 inHg intercept "cannot simply be decomposed", and at the bottom of the rpm range
    /// that is still true: what is measured is the SUM of poor combustion, poor breathing and friction, and all this
    /// claims is that the sum is far bigger at idle than a friction correlation alone allows, with a published number
    /// saying how much bigger.
    /// </summary>
    /// <summary>A probe's seam, like <see cref="WideOpenManifoldInHg"/>: a field so EngineBrakingProbe can re-solve
    /// it against Continental's published idle whenever something else in the decomposition moves. The sim never
    /// writes it.</summary>
    public static double IdleIndicatedLossB = 0.10101;

    /// <summary>Continental X30012 Table XXI, C-85: idling speed 475-525 rpm, 17 inHg of manifold vacuum, and
    /// 2,575-2,625 rpm full throttle on the test club. The three numbers <see cref="IdleIndicatedLossB"/> is solved
    /// from, kept here so the test that re-checks it reads the same figures.</summary>
    public const double C85IdleRpm = 500.0, C85IdleManifoldInHg = 12.9, C85ClubStaticRpm = 2600.0;

    /// <summary>How much of its charge's work the engine actually gets at this speed, 1 at the rating. See
    /// <see cref="IdleIndicatedLossB"/>; clamped at 1 because nothing above the rating makes it MORE efficient here,
    /// and at a small floor because a cranking engine still fires, just badly.</summary>
    public static double IndicatedEfficiencyFactor(double rpm, double ratedRpm)
    {
        if (rpm <= 0.0 || ratedRpm <= 0.0) return 1.0;
        double d = ratedRpm / rpm - 1.0;
        return d <= 0.0 ? 1.0 : Math.Clamp(1.0 / (1.0 + IdleIndicatedLossB * d * d), 0.0, 1.0);
    }

    /// <summary>
    /// CHEN-FLYNN'S CONSTANTS, bar: FMEP = A + B·P_max + C·S_p + D·S_p², with P_max in bar and S_p in m/s.
    ///
    /// ONLY THEIR RATIOS ARE USED. <see cref="FrictionShape"/> is normalised at rated rpm and full load, and the LEVEL
    /// comes from Gagg and Ferrar (see the class remarks), so an overall scale error in these cancels exactly. What
    /// they decide is how steeply friction climbs with rpm between idle and the red line, which is the whole
    /// behaviour being added.
    ///
    /// THE RANGE THEY ARE VALID OVER, which is the thing to check before trusting them here: the correlation was
    /// fitted to automotive engines, where a 5,000 rpm four is at 17 m/s of mean piston speed. These aircraft engines
    /// run 2.6 m/s at idle to 10 m/s at the red line — inside that range at the top and well below it at the bottom,
    /// which is the safe direction to be wrong in, because the S_p and S_p² terms are small there and the constant
    /// term dominates. The two speed terms carry 55 % of FMEP at 2,700 rpm and 23 % at 700.
    /// </summary>
    public const double ChenFlynnABar = 0.40, ChenFlynnB = 0.006, ChenFlynnCBarPerMs = 0.08, ChenFlynnDBarPerMs2 = 0.001;

    /// <summary>
    /// PEAK CYLINDER PRESSURE AT RATED POWER, bar, for Chen-Flynn's load term. It enters only through the ratio
    /// B·P_max / FMEP_rated, so what matters is that the load-dependent share of friction is about a fifth, not the
    /// absolute figure.
    /// </summary>
    public const double RatedPeakPressureBar = 55.0;

    /// <summary>What a MOTORED cylinder peaks at against a fired one. No combustion, so the peak is just the
    /// polytropic compression of whatever was trapped — about a third of the fired peak on an 8.7:1 engine. Used by
    /// <see cref="PistonEngine.MotoringTorqueNm"/>, where the rings are carrying a much lighter gas load.</summary>
    public const double MotoredPeakFraction = 1.0 / 3.0;

    /// <summary>
    /// CHEN-FLYNN AS A SHAPE: its value here over its value at rated rpm and full load. <paramref name="peakRatio"/>
    /// is P_max here over P_max at rated, for which manifold pressure is the surrogate (peak cylinder pressure
    /// follows the trapped charge pressure).
    /// </summary>
    public static double FrictionShape(double strokeM, double rpm, double ratedRpm, double peakRatio)
    {
        double here = ChenFlynnAt(strokeM, rpm, Math.Max(0.0, peakRatio));
        double rated = ChenFlynnAt(strokeM, ratedRpm, 1.0);
        return rated <= 1e-9 ? 1.0 : here / rated;
    }

    private static double ChenFlynnAt(double strokeM, double rpm, double peakRatio)
    {
        // The two speed terms are held at NACA 262's floor below it; the constant and load terms are not, because
        // they do not depend on speed at all.
        double sp = Math.Max(LowSpeedPistonSpeedFloorMs, MeanPistonSpeedMs(strokeM, rpm));
        return ChenFlynnABar + ChenFlynnB * RatedPeakPressureBar * peakRatio
             + ChenFlynnCBarPerMs * sp + ChenFlynnDBarPerMs2 * sp * sp;
    }

    /// <summary>
    /// GAGG AND FERRAR'S FRICTION FRACTION: friction is 11.7 % of sea-level GROSS INDICATED power, which is the whole
    /// content of (σ − 0.117)/0.883 and is where <see cref="PistonEngine.AltitudePowerFraction"/>'s 0.117 comes from.
    /// Stated here as well because this file is where it is used as a friction level rather than as a lapse, and the
    /// two must stay the same number — PistonEngineTests asserts that the wide-open-throttle lapse this model
    /// produces IS Gagg and Ferrar's, to the digit.
    /// </summary>
    public const double GaggFerrarFrictionFraction = 0.117;
}

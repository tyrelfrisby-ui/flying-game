using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Nav;

namespace FlyingGame.Core.Multiplayer;

/// <summary>
/// RANDOM AIR START with guaranteed separation. Owner 2026-11-03, verbatim: <i>"spawns need a randomizer for start
/// location and altitude so multiplayer players don't start at the same spot and collide"</i> and <i>"a random air
/// start with guaranteed separation is the alternative for players who don't want to wait"</i>; and on the shape of
/// the job, <i>"a random air start that picks location, altitude and heading inside sensible bounds for the type and
/// terrain and guarantees separation from other occupied starts and airborne traffic (state the separation distances
/// you choose and why), trimmed using the same air-start trim path as the altitude picker"</i>.
///
/// This class only PICKS the point, the height and the heading. <c>FlyingGame.Sim.AirStart.ComputeRandom</c> trims
/// the aeroplane there through <c>AirStart.Compute</c> — byte for byte the path <c>FlyingGame.Sim.StartAltitude</c>'s
/// picker uses, which was the point of asking for it that way.
///
/// ======================================================================================================
/// THE SEPARATION DISTANCES, AND WHY THESE AND NOT THE OBVIOUS ONES
/// ======================================================================================================
///
/// WHERE THESE NUMBERS CAME FROM — read this before looking for the regulation, because THERE ISN'T ONE.
/// THE OWNER CHOSE THEM, on 2026-11-10. The proposal put to him was the obvious one, terminal radar separation:
/// 3 NM laterally or 1,000 ft vertically, with a 1 NM / 500 ft floor over the first minute. His answer, verbatim,
/// was <i>"Tighter - the world is small"</i>, without naming figures; the figures below were then picked to fit
/// that instruction and he can overrule any of them in a word. They are NOT airway numbers, they are NOT scaled
/// TERPS criteria, and a future reader should not go hunting for the standard they came from.
///
/// WHY HE SAID TIGHTER, and the arithmetic behind it. THIS WORLD IS 8.6 NM NORTH-SOUTH BY 14.9 NM EAST-WEST. Three
/// nautical miles is a THIRD of its narrow dimension, so three or four starts fill it and the guarantee begins
/// failing by EXHAUSTION rather than by geometry — "no start found" while the sky looks empty. `docs/NAVIGATION.md`
/// records this repo meeting the same wall twice over airways and approaches: a Victor airway's 8 NM primary area
/// is nearly the width of the whole world, and taken at face value it produced a minimum en-route altitude of
/// 19,800 ft — "correct arithmetic, useless airway". Separation is the third instance of the same lesson.
///
/// THE START VOLUME (rule 1), and what each figure buys:
///   • HORIZONTAL <see cref="SepHorizontalM"/> = 1.5 NM = 2,778 m. At 150 kt of head-on closure — a realistic
///     worst case for two of the light types in this fleet — 1.5 NM is THIRTY-SIX SECONDS of warning, which is
///     more than a real traffic pattern gives a real pilot. Checked against the only standard that is actually
///     about collisions rather than about controller workload: TCAS II issues a resolution advisory inside roughly
///     0.2–0.48 NM horizontally, so 1.5 NM is three to seven times the volume in which a real collision-avoidance
///     system would even speak up.
///   • VERTICAL <see cref="SepVerticalM"/> = 1,000 ft = 305 m, left at the full radar figure. Vertical is the
///     UNCROWDED dimension in this world and it costs nothing to be generous in it — confirmed before relying on
///     it that there is no world ceiling to run out of: the 3,500 ft in `docs/INVISIBLE-WALLS.md` is an aerobatic
///     box strip, not a lid.
///
/// THE FIRST-MINUTE FLOOR (rule 2) is deliberately TIGHTER than the start volume: 0.5 NM = 926 m and 500 ft =
/// 152 m. The two differ because the pilot's situation differs. At the instant of the start they have just come
/// out of a menu with no picture of the sky at all, which is worth a generous bubble; a minute later they are
/// flying, looking out, and 500 ft is the separation the regulator itself considers adequate between
/// opposite-direction VFR traffic (the VFR cruising-altitude rule). Holding the full start volume for the whole
/// minute would have cost availability for no gain in safety.
///
/// MEASURED, so the choice can be argued with rather than believed — the fraction of the candidate ring and band
/// still available for a start as the sky fills, from <c>MultiplayerStartTests.SeparationStandardsAreMeasured</c>,
/// which prints the table on every run. This is the number the coordinating agent asked for and it says the chosen
/// standard is comfortable: 1.5 NM does NOT exhaust the space at the densities one field will see.
///
/// Separated means EITHER distance is outside its limit — the same OR that radar separation and TCAS both use. A
/// pair is in conflict only when they are inside both at once.
///
/// WHAT IS GUARANTEED. Every candidate is tested and one that fails any rule is never returned. If nothing passes
/// in <see cref="MaxTries"/> the answer is NULL and the caller says so out loud: it never relaxes a rule to produce
/// an answer, because a randomizer that quietly drops the guarantee when the sky is busy fails exactly when it was
/// needed.
///  1. AT THE START, the protected volume is clear of every other aeroplane — tested at t = 0 with no reliance on
///     anybody's reported VELOCITY, so it still holds if every velocity in the roster is wrong or zero. This is the
///     "do not materialise inside somebody" rule.
///  2. FOR THE FIRST <see cref="LookaheadS"/> SECONDS, flying both aircraft straight ahead — you on your heading at
///     your trimmed speed, them on their reported velocity — the pair never comes inside the FIRST-MINUTE FLOOR
///     (<see cref="CpaHorizontalM"/> / <see cref="CpaVerticalM"/>, above). Rule 1 alone is nowhere near enough:
///     two of the fast types 1.5 NM apart head-on close it in under ten seconds, and rule 1 looks only at t = 0.
///     THE HORIZON IS 60 s, a full minute of straight flight in which neither pilot has to do anything. Worth
///     knowing that a shorter horizon is arguable and was argued: FAA AC 90-48's see-and-avoid sequence is 12.5 s
///     from first sighting to the aeroplane responding, so 30 s is already double what a pilot with no picture of
///     the sky needs, and at the fleet's top speeds a 60 s straight line is 15 km — more than half this world's
///     long axis, so part of the minute is extrapolating flight that would have left the map. 60 s is kept because
///     it is strictly the more conservative of the two and, with the floor tightened to 0.5 NM, it costs very
///     little availability (measured: see the table above). <see cref="LookaheadS"/> is the one line to change.
///  3. A STALE REPORT IS WIDENED. A pilot last heard of <see cref="Traffic.AgeS"/> ago could be speed × age from
///     where the report put them, so both limits grow by that much — horizontally by their ground speed, vertically
///     by their vertical speed. The relay runs at 10 Hz, so this is normally centimetres; it earns its keep when a
///     roster snapshot is seconds old, which is exactly the case on the start page.
///  4. OCCUPIED FIXED STARTS are kept clear the same way, plus the start's own zone: the protected volume beyond
///     the zone's edge horizontally, or beyond its vertical band. A pilot queued for a fixed start is NOWHERE (see
///     <see cref="Traffic"/>), so their start point is what has to be avoided, not their meaningless position.
///  5. ANOTHER PILOT'S RANDOM CLAIM counts as traffic when that claim came first (<see cref="StartQueue.Before"/>).
///     This is rule 1 and 2 applied to a pilot who has chosen a random start but is still holding — see
///     <see cref="StartQueue.RandomClaim"/> for why, and note that it is the one hole the earlier attempt at this
///     feature left open and flagged ("their picks are independent … it is not guaranteed out").
///  6. TERRAIN: <see cref="TerrainClearanceM"/> = 1,000 ft above the highest ground within
///     <see cref="TerrainRadiusM"/> = 4 NM / 4 = 1 NM of the start, and above the ground under the first minute's
///     track. 14 CFR 91.177 is 1,000 ft above the highest obstacle within 4 NM — the lateral radius is scaled, the
///     1,000 ft is not. REJECTED the 2,000 ft mountainous figure, which this island's relief would otherwise
///     qualify for: it exists for an IFR aeroplane in cloud with no visual reference, this is a VFR start in sight
///     of the ground, and 2,000 ft above the island's highest terrain would squeeze every start on every plateau
///     into one thin layer — which defeats the randomizer that is the entire point of the feature.
///
/// ======================================================================================================
/// HOW STRONG THE GUARANTEE ACTUALLY IS — said plainly, because "guaranteed separation" is the owner's phrase
/// ======================================================================================================
///
/// Against the roster a client holds, the guarantee is EXACT, not probabilistic: every rule is re-tested on the
/// returned start, and no start is returned that fails one. The residual risk is not in the geometry, it is in the
/// roster being out of date, and there are exactly two ways that happens:
///  • A REPORT IS OLD. Covered, and covered arithmetically: rule 3 widens both limits by the pilot's own speed
///    times the age of their report, so an old report makes the rule STRICTER by exactly as much as the pilot
///    could have moved. The relay ticks at 10 Hz, so this is normally centimetres.
///  • A CLAIM HAS NOT ARRIVED YET. Two pilots pressing FLY within one network round trip of each other can each
///    choose before the other's claim lands. This is the hole the earlier attempt at this feature left open and
///    flagged, and rule 5 plus <see cref="StartQueue.RandomClaim"/> is the answer: the claim is published in the
///    state packet the client already sends, so the window is one relay round trip (a tick plus the RTT, tens of
///    milliseconds on a LAN, under ~200 ms through the relay) rather than the several seconds a hold lasts. Inside
///    that window the two picks are independent and the protection is the ring's size alone — about a
///    one-in-a-thousand chance of landing inside one protected volume, falling to nothing as soon as either claim
///    arrives, and the later chooser re-picks the moment it does. It is NOT nil, and it should not be described to
///    a pilot as nil.
/// WHAT WOULD CLOSE IT COMPLETELY: an arbiter that hands out starts, which means a relay change — a deploy, and a
/// build that no longer works against the shipped relay. That trade is the owner's to make, not mine, and the
/// mechanism here was chosen because it needs no server change at all.
/// </summary>
public static class RandomAirStart
{
    public const double Nm = Terps.MetresPerNm, Ft = Terps.MPerFt;

    // ==================================================================================================
    // THE SEPARATION STANDARD — one block, five constants, and they are all one decision.
    //
    // SOURCE: THE OWNER, 2026-11-10. Not an aviation standard, not scaled TERPS criteria. A 3 NM / 1,000 ft radar
    // standard with a 1 NM / 500 ft first-minute floor was proposed to Ty; his answer was "Tighter - the world is
    // small", without figures, and these are the figures picked to carry out that instruction. He overrules any of
    // them in a word. Nothing else in this file hard-codes a distance — change a constant here and the rules, the
    // ring and the wait text all follow.
    //
    // WHY (full reasoning in the class summary above): the world is 8.6 × 14.9 NM, so 3 NM is a third of its narrow
    // dimension and the guarantee would start failing by exhaustion rather than geometry. 1.5 NM is 36 s of warning
    // at 150 kt of head-on closure, more than a real traffic pattern gives a real pilot. Vertical stays at the full
    // 1,000 ft because height is the uncrowded dimension here and there is no world ceiling to run out of.
    //
    // MEASURED COST of each candidate standard: MultiplayerStartTests.SeparationStandardsAreMeasured.
    // ==================================================================================================

    /// <summary>Rule 1, horizontal: 1.5 NM, 2,778 m. Owner's call (see the block above), not a regulation.</summary>
    public const double SepHorizontalNm = 1.5;
    public const double SepHorizontalM = SepHorizontalNm * Nm;
    /// <summary>Rule 1, vertical: 1,000 ft. Left at the full radar figure — height is the cheap dimension here.</summary>
    public const double SepVerticalM = 1000 * Ft;

    /// <summary>Rule 2's floor, horizontal: 0.5 NM, 926 m. Tighter than rule 1 on purpose — by then the pilot is
    /// flying and looking out, rather than one frame out of a menu.</summary>
    public const double CpaHorizontalNm = 0.5;
    public const double CpaHorizontalM = CpaHorizontalNm * Nm;
    /// <summary>Rule 2's floor, vertical: 500 ft. The separation the regulator itself considers adequate between
    /// opposite-direction VFR traffic (the VFR cruising-altitude rule).</summary>
    public const double CpaVerticalM = 500 * Ft;

    /// <summary>The conflict-free window after the start, seconds. See rule 2 for why 60 and not 30.</summary>
    public const double LookaheadS = 60.0;

    /// <summary>14 CFR 91.177 obstacle clearance, 1,000 ft, unscaled.</summary>
    public const double TerrainClearanceM = 1000 * Ft;
    /// <summary>91.177's 4 NM radius at this world's scale: 1 NM.</summary>
    public const double TerrainRadiusNmReal = 4.0;
    public const double TerrainRadiusM = TerrainRadiusNmReal / Terps.LateralScale * Nm;
    /// <summary>
    /// The grid the disc round the start is sampled on. MEASURED, not chosen: at 250 m (what the first attempt at
    /// this used) the test that re-measures a returned start on a 100 m grid found real violations round PATK — a
    /// ridge rising between two samples of the coarse grid, which is exactly the failure a sampled clearance check
    /// is prone to. The picker and the check now sample the SAME grid, so the margin below is true headroom rather
    /// than a patch over a disagreement.
    /// </summary>
    public const double TerrainGridM = 100.0;
    /// <summary>Added to the terrain clearance because the ground is SAMPLED (<see cref="TerrainGridM"/> round the
    /// start, the track every second): 50 m of headroom between samples. A margin, not a standard — it exists to
    /// make the sampled test conservative against the continuous ground it stands in for.</summary>
    public const double TerrainSampleMarginM = 50.0;

    /// <summary>Candidates are drawn from a ring round the field. The INNER edge is the air-start zone's own radius
    /// (1,500 m, <see cref="StartPoint.Air"/>) plus one start volume — i.e. the closest a random start can be and
    /// still be clear of the field's own air start if somebody is using it — rounded to 4,500 m. Inside that the
    /// pick would be fighting the field's own fixed starts for the same air and rule 4 would reject most of it
    /// anyway, so sampling there is wasted tries rather than a different answer.</summary>
    public const double MinRingM = 4500.0;
    /// <summary>The ring never reaches beyond this, whatever the type's speed says, because the world is only
    /// 15.9 km across its short axis and a wider ring puts most candidates in the sea or off the map. See
    /// <see cref="OuterRingFor"/>.</summary>
    public const double MaxRingM = 10000.0;
    /// <summary>How long a random start may be from the field it was launched near, seconds. The ring's outer edge
    /// is the distance the TYPE covers in this time, so a paraglider is not dropped 10 km out with a 17-minute
    /// crawl home while an F-86 is not boxed into a ring it crosses in forty seconds.</summary>
    public const double RingMinutesHome = 180.0;

    public const int MaxTries = 600;

    /// <summary>An unpowered wing that starts slower than this (29 kt) is a hang glider or a paraglider (10–11 m/s);
    /// the slowest sailplane in the fleet starts at 22 m/s.</summary>
    public const double SlowWingMs = 15.0;
    /// <summary>Above this trimmed speed (195 kt) a type is in the fleet's fast group — the F-86, the P-51, the
    /// Gee Bee, the Cassutt, the Glasair — and gets the high band. See <see cref="Band"/>.</summary>
    public const double FastMs = 100.0;

    public readonly struct Pick
    {
        public readonly double X, Y, AltitudeM, HeadingRad;
        public readonly int Tries;
        public Pick(double x, double y, double altitudeM, double headingRad, int tries) { X = x; Y = y; AltitudeM = altitudeM; HeadingRad = headingRad; Tries = tries; }
        public Vec3 Position => new(X, Y, -AltitudeM);
    }

    public sealed class Request
    {
        /// <summary>The field the ring is drawn round.</summary>
        public double CentreX { get; set; }
        public double CentreY { get; set; }
        public double MinRingM { get; set; } = RandomAirStart.MinRingM;
        public double MaxRingM { get; set; } = RandomAirStart.MaxRingM;
        /// <summary>Height band above the ground under the start, m. <see cref="Band"/> fills it.</summary>
        public double MinAglM { get; set; }
        public double MaxAglM { get; set; }
        /// <summary>Your trimmed true airspeed, for the first minute's track (rules 2 and 6).</summary>
        public double OwnSpeedMs { get; set; }
        public Func<double, double, double> GroundAt { get; set; } = (_, _) => 0.0;
        /// <summary>Optional: where a start may be at all (over land the world has actually built, say). Null = anywhere.</summary>
        public Func<double, double, bool>? Allowed { get; set; }
        public IReadOnlyList<Traffic> Traffic { get; set; } = Array.Empty<Traffic>();
        /// <summary>Fixed starts somebody is using or queued for (rule 4).</summary>
        public IReadOnlyList<StartPoint> OccupiedStarts { get; set; } = Array.Empty<StartPoint>();

        /// <summary>Me, for rule 5: another pilot's random claim binds only if it came before mine. Null `SelfId`
        /// (the default) is "I have not claimed anything", which loses every tie — so a chooser with no claim of
        /// its own yields to every claim on the roster. That is the safe default and the one the start page wants.</summary>
        public string? SelfId { get; set; }
        public double SelfClaimSinceS { get; set; } = double.PositiveInfinity;
    }

    /// <summary>
    /// The height band for the type, m AGL — "sensible bounds for the type", which was the owner's phrase and the
    /// reason a 2-33 and an F-86 do not share one. With the altitude picker set (`pickerAglFt` &gt; 0) the pilot has
    /// said what they want and the band is that height ±1,000 ft (never below 1,500 ft AGL), so the randomizer
    /// scatters them around their chosen height instead of overruling it. Otherwise, by type:
    ///  • HANG GLIDER / PARAGLIDER 1,500–3,000 ft. They launch off a hill and sink at 1–2 m/s; from 3,000 ft a
    ///    paraglider has about 20 minutes and that is a long flight for one.
    ///  • SAILPLANE 2,000–5,000 ft. A tow release is 2,000–3,000 ft and the band wants room to find lift and still
    ///    reach a field: a 2-33 at 23:1 glides 21 km from 3,000 ft, which crosses this world.
    ///  • POWERED 2,000–5,000 ft. OWNER'S CALL, 2026-11-10, same decision as the separation distances: this was
    ///    3,000–7,000 ft, and the reason it came down is the ISLAND'S ELEVATION. The inland plateaus sit near
    ///    4,700 ft MSL, so a 7,000 ft AGL start put a Cub at 11,700 ft MSL — thin air, a long climb back to
    ///    anything, and nobody asked to start there. 2,000–5,000 ft AGL still clears the spin-recovery case the
    ///    altitude picker exists for (a Pitts loses 300–600 ft a turn, so 2,000 ft is four turns and a recovery),
    ///    and a pilot who wants more says so with the picker, which overrules this.
    ///  • FAST (above <see cref="FastMs"/>) 4,000–8,000 ft. The F-86 and the P-51 cross this world's short axis in
    ///    80–160 s, so a low start gives them nowhere to go, and a departure in one of them costs thousands of feet
    ///    to recover. Kept above the powered band for that reason, but brought down from 5,000–12,000 ft by the same
    ///    plateau-elevation argument: over KMYL, 8,000 ft AGL is already 13,000 ft MSL.
    /// These are AGL, deliberately: the same choice gives the same HEIGHT over the ground at sea level and on a
    /// 5,000 ft plateau, which is what a pilot means by "start me a few thousand feet up".
    /// </summary>
    public static (double MinM, double MaxM) Band(AircraftConfig c, int pickerAglFt, double trimmedSpeedMs = 0)
    {
        if (pickerAglFt > 0) return (Math.Max(1500, pickerAglFt - 1000) * Ft, Math.Max(2500, pickerAglFt + 1000) * Ft);
        if (c.WeightShift != null || (c.Propulsion == null && c.SpawnIasMs > 0 && c.SpawnIasMs < SlowWingMs)) return (1500 * Ft, 3000 * Ft);
        if (c.Propulsion == null) return (2000 * Ft, 5000 * Ft);
        if (trimmedSpeedMs >= FastMs) return (4000 * Ft, 8000 * Ft);
        return (2000 * Ft, 5000 * Ft);
    }

    /// <summary>The ring's outer edge for a type that cruises at `speedMs`: what it covers in
    /// <see cref="RingMinutesHome"/>, never inside one protected volume beyond the inner edge (or there would be
    /// almost no ring to sample) and never beyond <see cref="MaxRingM"/> (or the sample leaves the world).</summary>
    public static double OuterRingFor(double speedMs, double minRingM = MinRingM) =>
        Math.Clamp(speedMs * RingMinutesHome, minRingM + SepHorizontalM, MaxRingM);

    /// <summary>A start that passes every rule, or null if none was found in <see cref="MaxTries"/>.</summary>
    public static Pick? Choose(Request r, Random rng)
    {
        for (int i = 1; i <= MaxTries; i++)
        {
            // Uniform over the ring's AREA (radius from the square root), not over the radius — otherwise the thin
            // inner annulus gets as many candidates as the broad outer one and starts cluster near the field, which
            // is the opposite of what a randomizer is for.
            double a = rng.NextDouble() * 2 * Math.PI;
            double rad = Math.Sqrt(r.MinRingM * r.MinRingM + rng.NextDouble() * (r.MaxRingM * r.MaxRingM - r.MinRingM * r.MinRingM));
            double x = r.CentreX + rad * Math.Cos(a), y = r.CentreY + rad * Math.Sin(a);
            double agl = r.MinAglM + rng.NextDouble() * (r.MaxAglM - r.MinAglM);
            double hdg = rng.NextDouble() * 2 * Math.PI;
            if (r.Allowed != null && !r.Allowed(x, y)) continue;
            var p = new Pick(x, y, r.GroundAt(x, y) + agl, hdg, i);
            if (Why(p, r) == null) return p;
        }
        return null;
    }

    /// <summary>Null if the start keeps every rule; otherwise the first rule it breaks, in words — which is what the
    /// tests assert on and what the log prints when a busy sky gives no answer at all.</summary>
    public static string? Why(Pick p, Request r)
    {
        Vec3 pos = p.Position, vel = new Vec3(Math.Cos(p.HeadingRad), Math.Sin(p.HeadingRad), 0) * r.OwnSpeedMs;

        foreach (Traffic t in r.Traffic)
        {
            if (t.Id == r.SelfId) continue;
            // Rule 5: a pilot queued for a FIXED start is nowhere (rule 4 handles their start point); a pilot with a
            // random claim is traffic, but only if their claim came before mine.
            if (!t.OccupiesSpace) continue;
            if (t.HasRandomClaim && !StartQueue.Before(t.HoldingSinceS, t.Id, r.SelfClaimSinceS, r.SelfId)) continue;

            double widenH = Math.Sqrt(t.Velocity.X * t.Velocity.X + t.Velocity.Y * t.Velocity.Y) * t.AgeS;   // 3.
            double widenV = Math.Abs(t.Velocity.Z) * t.AgeS;
            // Rule 1 uses the START volume; rule 2 the tighter FIRST-MINUTE floor. Both widen by the same amount
            // for a stale report, because the uncertainty is the pilot's, not the rule's.
            if (!Separated(pos, t.Position, SepHorizontalM + widenH, SepVerticalM + widenV)) return $"too close to {t.Id}";   // 1.
            double cpaH = CpaHorizontalM + widenH, cpaV = CpaVerticalM + widenV;
            (double h, double v) = ClosestApproach(pos, vel, t.Position, t.Velocity, LookaheadS, cpaH, cpaV);
            if (h < cpaH && v < cpaV) return $"converging with {t.Id}";                                                      // 2.
        }
        foreach (StartPoint s in r.OccupiedStarts)                                                                     // 4.
            if (!Separated(pos, new Vec3(s.X, s.Y, -s.AltitudeM), SepHorizontalM + s.RadiusM, SepVerticalM + s.HalfHeightM)) return $"too close to {s.Name}";
        // 6. Terrain: round the start, then along the first window's track, each with the sampling margin on top.
        double need = TerrainClearanceM + TerrainSampleMarginM;
        if (p.AltitudeM - MaxGroundNear(p.X, p.Y, TerrainRadiusM, r.GroundAt) < need) return "terrain near the start";
        for (int k = 1; k <= (int)LookaheadS; k++)
            if (p.AltitudeM - r.GroundAt(p.X + vel.X * k, p.Y + vel.Y * k) < need) return "terrain on the track";

        return null;
    }

    /// <summary>Separated if EITHER the horizontal distance OR the vertical distance is at least its minimum — the
    /// OR that radar separation and TCAS both use. A pair is in conflict only inside both at once.</summary>
    public static bool Separated(Vec3 a, Vec3 b, double minH, double minV)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy >= minH * minH || Math.Abs(a.Z - b.Z) >= minV;
    }

    /// <summary>The closest the two straight tracks come in [0, T], reported as the (horizontal, vertical) pair at
    /// the moment nearest to breaking BOTH limits — the least of max(h / limitH, v / limitV). Scored that way
    /// because the rule is an OR: the dangerous moment is not the one with the smallest distance but the one where
    /// neither distance is enough.</summary>
    public static (double H, double V) ClosestApproach(Vec3 p1, Vec3 v1, Vec3 p2, Vec3 v2, double horizonS,
        double limitH = CpaHorizontalM, double limitV = CpaVerticalM)
    {
        Vec3 dp = p1 - p2, dv = v1 - v2;
        (double h, double v, double s) At(double t)
        {
            double hx = dp.X + dv.X * t, hy = dp.Y + dv.Y * t, vz = dp.Z + dv.Z * t;
            double hh = Math.Sqrt(hx * hx + hy * hy), vv = Math.Abs(vz);
            return (hh, vv, Math.Max(hh / limitH, vv / limitV));
        }
        // Both distances are norms of straight-line motion, so the score is convex in t and a ternary search finds
        // its minimum to the millisecond. A fixed time step was tried first and REJECTED: at the fleet's closing
        // speeds a 1 s step walks straight over a conflict that opens and shuts inside one step.
        double lo = 0, hi = horizonS;
        for (int i = 0; i < 80; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (At(m1).s <= At(m2).s) hi = m2; else lo = m1;
        }
        var best = At(0.5 * (lo + hi));
        return (best.h, best.v);
    }

    /// <summary>The highest ground on the <see cref="TerrainGridM"/> grid over the disc.</summary>
    private static double MaxGroundNear(double x, double y, double radius, Func<double, double, double> groundAt)
    {
        const double step = TerrainGridM;
        double m = groundAt(x, y);
        int n = (int)(radius / step);
        for (int i = -n; i <= n; i++)
            for (int j = -n; j <= n; j++)
                if (i * i + j * j <= n * n) m = Math.Max(m, groundAt(x + i * step, y + j * step));
        return m;
    }
}

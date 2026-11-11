using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// STEARMAN_ELEV=1: WHY THE STEARMAN RUNS OUT OF ELEVATOR BEFORE ITS WING STALLS, and which of the four possible
/// causes it is.
///
/// THE MEASUREMENT THAT STARTED IT (2026-11-10, after the twelve fabric types got flat-plate horizontal tails):
/// at its own reported Vso the Stearman needs 27.4° of elevator against a 29.8° stop — 92 % of travel — so what
/// `PracticeScenario.EstimateVso` reports is an ELEVATOR-LIMITED MINIMUM TRIM SPEED and not an aerodynamic stall.
/// It sits at 24.50 m/s (54.8 mph) against a book 52, and it is the only type in the fleet whose stall speed moved
/// when the tails were converted. That number drives the approach speeds, the STOL spawn and the glide tables.
///
/// Ty's instruction: <b>"Investigate it properly"</b>, explicitly over "just give it more travel", with the
/// reasoning he gave and accepted: <i>something is wrong — either the elevator is too small, the travel too
/// limited, the CG too far forward, or the tail volume is off — and raising the stop would hide whichever of the
/// four it is.</i> He also said in the same breath that it is legitimate for the answer to be that the aeroplane is
/// right and the test is measuring an elevator limit, because a real aeroplane at a forward CG genuinely does run
/// out of elevator before the wing stalls — that is what a forward CG limit is FOR.
///
/// So this probe measures the four, one at a time, on the SAME airframe, each against the question "how much of it
/// would it take to reach the book 52 mph":
///
///  1. THE TRIM LADDER — speed by speed down to the limit, with the elevator used, the fraction of travel, the body
///     alpha reached, and the alpha the WING needs for CLmax. The gap between the last two is the whole question:
///     if the aeroplane stops trimming at an alpha well below its wing's CLmax alpha, the stop is what stopped it.
///  2. THE CG SWEEP — Vso, travel used, and the static margin as the CG moves aft, which is the one of the four
///     that is REALISTIC: a forward CG running you out of elevator at the stall is correct behaviour, so if the
///     model is loaded forward of the real aeroplane's range the answer is the loading and not the aeroplane.
///     Printed in metres from the config datum AND in % of the 1.52 m wing chord aft of the wing system's own
///     quarter-chord, so it can be compared with a TCDS CG range.
///  3. THE ELEVATOR'S SHARE OF THE TAIL — the elevator is 32.0 % of the horizontal tail's area on this config,
///     where its contemporaries in this fleet are at 43 % (Great Lakes, Jenny) and the Cubs at 38.6 %. Swept by
///     moving chord between the hStab row and the elevator row at CONSTANT total tail area and constant leading
///     edge, which is exactly what a bigger elevator on the same tailplane is.
///  4. THE TAIL VOLUME — Sh·l/(S·c), swept by scaling the horizontal tail's area at a fixed arm.
///  and 5. THE TRAVEL ITSELF, swept last, because it is the one Ty ruled out as an answer: it is here only to say
///     what it would take, so that the size of the other three can be read against it.
///
/// It also prints the tail's own flow at the limiting condition — local alpha, downwash ε and η_t = q_tail/q_∞ —
/// because "the downwash or the tail's dynamic pressure ratio at high alpha is costing it authority it should
/// have" was the fifth thing Ty asked to be checked, and it is not one of his four.
///
/// Every variant RELOADS the config from disk rather than mutating one instance, because the finite-span section
/// table, the system aspect ratio and the Vso alpha cap are all cached per `AircraftConfig` instance
/// (`ConditionalWeakTable`), so a mutated instance would be measured against a table derived from its old shape.
///
/// WHAT IT FOUND, 2026-11-10, so a reader does not have to re-run it to know the answer. Sourced verbatim in
/// `docs/research/STEARMAN-PT17-DATA.md`; the document is FAA TCDS A-743 Rev 16.
///  * IT IS THE CG, Ty's third. A-743's datum is the lower wing's leading edge and its flight CG range is
///    (-1.0) to (+7.1) in. The config's CG sits at +0.24 in — 15 % of the way into an 8.1 inch envelope — while
///    A-743's own loading arms put THIS MASS (2649 lb, two seats and full fuel) at +3.53 in. 3.3 inches forward.
///  * UNDER IT, AND NOT ONE OF THE FOUR: the stagger is 17.7 in against a certificated 26-13/16, so the upper
///    wing is 0.231 m too far aft and the cellule's lift 0.117 m aft of where it belongs. Between them the
///    static margin is 25.4 % of chord, the highest in the fleet and outside the 5-20 % band `AeroModel`'s own
///    hinged-section note calls real. `git log`: 630ba1d4, 2026-09-08, "wings re-centred on the CG" moved both
///    wings aft 0.274 m and left the CG where it was, overshooting by about 0.07 m.
///  * THE TRAVEL IS RULED OUT AND BACKWARDS: A-743 says up 24 deg, down 22, and the config has 29.8 symmetric,
///    which is this repo's generic default (0.52 rad on 21 of 35 types). The model has MORE travel than the real
///    aeroplane, and giving it the certificate's stops makes the speed worse.
///  * THE OTHER TWO CANNOT BE SETTLED and are not guessed: the real stabiliser area, elevator area, total tail
///    area and moment arm are unpublished in everything reachable (not A-743, not the 1941 Navy Pilot's Handbook,
///    not Jane's, not the parts catalog, and there is no NACA report on the Model 75). The elevator's 32 % share
///    changes the stick needed and not the speed; Vh 0.383 is the fleet's lowest but the 2-33 flies at 0.316 on
///    28 % of travel.
///  * DOWNWASH AND TAIL q ARE INNOCENT: local alpha 2.74 deg, eps 3.49 deg, eta_t 0.823 at the limit.
///  * AND THE BOOK FIGURE WAS WRONG. The "52 mph" is Jane's LANDING speed; the primary stall is the 1941
///    handbook's 53 mph at 50 % fuel and oil (about 2529 lb), which at this model's 2646 lb is 54.2 mph. The
///    model reads 54.4. So the SPEED was right and the MECHANISM was wrong - and correcting the mechanism takes
///    it to 49.0 mph, 9.6 % low, on a trimmed CLmax of 1.47 where the book needs 1.20. The forward CG has been
///    masking a cellule CLmax 22 % high, which is the missing biplane interference term that
///    `docs/research/JN4-JENNY-DATA.md` already has as an open item.
///
/// ALL OF IT IS NOW CORRECTED, 2026-11-10, `lane/claude-stearman-cellule`, both halves together: the certificate's
/// CG, stagger, decalage, elevator stops and load factors, AND the cellule interference derived from
/// `BiplaneCellule`'s own Biot-Savart lattice (Prandtl NACA Rep. 116, Munk NACA Rep. 151) rather than fitted. The
/// derived factor is 0.805 where exact agreement with the book would have wanted 0.816 -- 1.3 % apart, with nothing
/// fitted -- and the aeroplane now stalls at 54.8 mph against a book 54.2, on 62 % of its travel, with its WING
/// stalling at alpha 12.51 deg where the wing reaches CLmax at 12.50, at a static margin of 13.1 %.
///
/// SO THE SECTIONS ABOVE DESCRIBE AN AEROPLANE THAT NO LONGER EXISTS, and are kept because they are the diagnosis.
/// Section 7 is inverted: it now puts each old wrong value BACK, one at a time, which is the only way left to
/// measure what each correction was worth. `docs/research/STEARMAN-PT17-DATA.md` § 5 is the record.
/// </summary>
[Collection("WorldTerrainActive")]
public class StearmanElevatorLimitProbe
{
    private readonly ITestOutputHelper _o;
    public StearmanElevatorLimitProbe(ITestOutputHelper o) { _o = o; }

    private static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return d.FullName;
    }
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(Root(), "configs", "aircraft", id + ".json"));

    private static (double area, double xbar) Centroid(AircraftConfig c, Func<string, bool> pick)
    {
        double a = 0, ax = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (!pick(sf.Id)) continue;
            foreach (StripConfig st in sf.Strips) { a += st.Area; ax += st.Area * st.Pos[0]; }
        }
        return (a, a > 0 ? ax / a : 0);
    }
    private static bool IsWing(string id) => id.Contains("wing", StringComparison.OrdinalIgnoreCase);
    private static bool IsHtail(string id) => id == "hStab" || id == "elevator";

    /// <summary>Body alpha at which the main wing's own section reaches CLmax: the section's α at CLmax less the
    /// wing's rigging incidence. This is <see cref="PracticeScenario.AlphaCapRad"/>'s ingredient, without its +3°
    /// of slack — i.e. where the aeroplane actually stalls if the elevator can get it there.</summary>
    private static double WingStallBodyAlphaRad(AircraftConfig c)
    {
        SurfaceConfig? main = null; double best = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (!sf.Id.StartsWith("wing", StringComparison.OrdinalIgnoreCase)
                || sf.Id.EndsWith("aileron", StringComparison.OrdinalIgnoreCase) || sf.Strips.Count == 0) continue;
            double area = 0; foreach (StripConfig st in sf.Strips) area += st.Area;
            if (area > best) { best = area; main = sf; }
        }
        if (main is null) return double.NaN;
        var tables = Aircraft.BuildAirfoilTables(c);
        if (!tables.TryGetValue(main.Strips[0].Airfoil, out AirfoilTable? tab)) return double.NaN;
        double inc = 0; foreach (StripConfig st in main.Strips) inc += st.IncidenceRad;
        return tab.AlphaClMaxRad - inc / main.Strips.Count;
    }

    /// <summary>The lowest speed the trim solver holds (the same scan <see cref="PracticeScenario.EstimateVso"/>
    /// runs), and what the elevator was doing there.
    ///
    /// `fine` re-runs the same scan at 0.1 m/s instead of the shipped 0.5 m/s. The shipped step is 1.1 mph on this
    /// aeroplane, which is coarser than the whole discrepancy being investigated and is why a CG sweep at the
    /// shipped step comes out in steps and looks non-monotonic. The 0.5 m/s number is the one the GAME uses, so
    /// both are printed where it matters.</summary>
    private static (double vso, double elevDeg, double frac, double alphaDeg, bool atStop) Limit(AircraftConfig c, bool fine = false)
    {
        double vso = fine ? FineVso(c) : PracticeScenario.EstimateVso(c, 0);
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, vso, 0);
        double lim = c.Controls.Elevator.LimitFor(Math.Sign(t.ElevatorRad) == 0 ? 1 : Math.Sign(t.ElevatorRad));
        double frac = Math.Abs(t.ElevatorRad) / lim;
        return (vso, t.ElevatorRad * 180 / Math.PI, frac, t.AlphaRad * 180 / Math.PI, frac > 0.98);
    }

    /// <summary><see cref="PracticeScenario.EstimateVso"/>'s own scan — down from 1.6 vRef, stopping at the first
    /// speed that no longer trims below the wing's own stalling alpha — at a 0.1 m/s step.</summary>
    private static double FineVso(AircraftConfig c)
    {
        double vRef = c.SpawnIasMs > 1 ? c.SpawnIasMs : 25.0;
        double cap = PracticeScenario.AlphaCapRad(c);
        double last = vRef; bool found = false;
        for (double v = vRef * 1.6; v >= vRef * 0.35; v -= 0.1)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0);
            bool ok = t.Converged && !double.IsNaN(t.AlphaRad) && t.AlphaRad < cap && t.GlideRatio > 0;
            if (!ok) { if (found) return last; continue; }
            found = true; last = v;
        }
        return last;
    }

    /// <summary>The station of the LOWER wing's leading edge, which is TCDS A-743's datum ("Datum — Leading edge of
    /// lower wing"), so the model's CG can be stated in the same inches the certificated CG range is in. Taken from
    /// the inboard (root-chord) strip: pos[0] is its quarter-chord and x is positive FORWARD.</summary>
    private static double LowerWingLeX(AircraftConfig c)
    {
        SurfaceConfig lw = c.Surfaces.Find(s => s.Id == "wing-lower")!;
        StripConfig root = lw.Strips[0];
        foreach (StripConfig st in lw.Strips) if (Math.Abs(st.Pos[1]) < Math.Abs(root.Pos[1])) root = st;
        return root.Pos[0] + 0.25 * root.Chord;
    }

    /// <summary>Stick-fixed static margin, measured rather than assumed: the aerodynamic centre's offset from the
    /// CG from dMy/dα and dL/dα at a small alpha, controls neutral. x is positive forward and My positive nose-up,
    /// so an upward force Δx ahead of the CG gives My = +L·Δx, hence x_ac − x_cg = (dMy/dα)/(dL/dα), and the static
    /// margin (positive = stable) is (x_cg − x_ac)/c.</summary>
    private static (double npX, double smFraction) NeutralPoint(AircraftConfig c, double vMs, double alphaDeg)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double mac = c.Surfaces[0].Strips[0].Chord;
        double h = 0.25 * Math.PI / 180;
        double a = alphaDeg * Math.PI / 180;
        (double m, double l) At(double aa)
        {
            Vec3 v = new(vMs * Math.Cos(aa), 0, vMs * Math.Sin(aa));
            var r = AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, 1.225, new ControlDeflections(0, 0, 0, 0));
            return (r.Moment.Y, -r.Force.Z);
        }
        var lo = At(a - h); var hi = At(a + h);
        double dM = (hi.m - lo.m) / (2 * h), dL = (hi.l - lo.l) / (2 * h);
        double cg = c.Mass.CgVec().X;
        double ac = cg + (Math.Abs(dL) > 1e-6 ? dM / dL : double.NaN);
        return (ac, (cg - ac) / mac);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("STEARMAN_ELEV") == null) return;
        const string Id = "stearman-pt17-like";
        // THE PRIMARY stall figure, not the 52 mph this probe was first written against -- that is Jane's 1945/6
        // LANDING speed. Pilot's Handbook for Stearman Airplanes N2S-1/-2/-3 (Report A75NI-9001, 20 Jan 1941),
        // Table of Characteristics: 53 mph "with 50% Fuel and Oil Consumed" at a 2682.7 lb normal gross, so at about
        // 2529 lb; this model flies at 2646 lb, so the comparable stall is 53*sqrt(2646/2529) = 54.2 mph.
        const double BookVsoMph = 54.2;          // the figure the model is being measured against
        double bookVso = BookVsoMph * 0.44704;

        // ---- 0. the airframe as configured -------------------------------------------------------------------
        {
            var c = Load(Id);
            var (sw, xw) = Centroid(c, IsWing);
            var (sh, xh) = Centroid(c, IsHtail);
            var (se, _) = Centroid(c, s => s == "elevator");
            double cg = c.Mass.CgVec().X, mac = c.Surfaces[0].Strips[0].Chord;
            double arm = cg - xh, le = LowerWingLeX(c);
            var (npX, sm) = NeutralPoint(c, 30.0, 3.0);
            _o.WriteLine($"{Id}: mass {c.Mass.MassKg:F0} kg ({c.Mass.MassKg / 0.45359:F0} lb), wing {sw:F2} m2 " +
                         $"({sw * 10.7639:F1} sqft), chord {mac:F3} m ({mac / 0.0254:F1} in)");
            _o.WriteLine($"  horizontal tail {sh:F3} m2 ({sh * 10.7639:F2} sqft) at x {xh:F3}, elevator {se:F3} m2 " +
                         $"= {se / sh * 100:F1} % of it; span 3.60 m (11 ft 10 in), AR {3.6 * 3.6 / sh:F2}");
            _o.WriteLine($"  tail arm (CG to tail area centroid) {arm:F3} m = {arm / 0.0254:F1} in = {arm / mac:F2} chords");
            _o.WriteLine($"  Sh/Sw {sh / sw:F4}, tail volume Vh = Sh*l/(Sw*c) = {sh * arm / (sw * mac):F4}");
            _o.WriteLine($"  elevator stops: {c.Controls.Elevator.MaxDeflRad * 180 / Math.PI:F1} deg TE-down, " +
                         $"{c.Controls.Elevator.LimitFor(-1) * 180 / Math.PI:F1} deg TE-up " +
                         $"({(c.Controls.Elevator.MaxDeflNegRad > 1e-9 ? "asymmetric" : "SYMMETRIC")}) " +
                         "-- TCDS A-743 Rev 16 says Up 24 deg, Down 22 deg");
            _o.WriteLine($"  wing stalls at body alpha {WingStallBodyAlphaRad(c) * 180 / Math.PI:F2} deg; " +
                         $"the Vso scan's ceiling is {PracticeScenario.AlphaCapRad(c) * 180 / Math.PI:F2} deg");
            // THE CG, IN THE UNITS THE CERTIFICATE USES. TCDS A-743 Rev 16: "Datum - Leading edge of lower wing",
            // "C.G. Range - (-1.0) to (+7.1)" inches, for both Section I and Section II (A75N1 = PT-17); the aft
            // limit tightens to +4.5 at the 3200 lb Note 6 weight and this aeroplane is nowhere near that.
            _o.WriteLine($"\n  THE CG IN TCDS UNITS. Lower-wing leading edge (the A-743 datum) is at model x " +
                         $"{le:+0.000;-0.000} m; the CG is at x {cg:+0.000;-0.000} m, i.e. " +
                         $"{(le - cg) / 0.0254:+0.00;-0.00} in aft of the datum.");
            _o.WriteLine($"    A-743 flight range -1.0 .. +7.1 in  ->  model x {le + 1.0 * 0.0254:+0.000;-0.000} " +
                         $"(fwd limit) .. {le - 7.1 * 0.0254:+0.000;-0.000} m (aft limit)");
            _o.WriteLine($"    so the config sits {(le - cg) / 0.0254 + 1.0:F2} in into an 8.10 in envelope = " +
                         $"{((le - cg) / 0.0254 + 1.0) / 8.1 * 100:F0} % of the way aft");
            _o.WriteLine($"    for reference the wing system's own area-weighted quarter-chord is at x " +
                         $"{xw:+0.000;-0.000} m, {(cg - xw) / mac * 100:F1} % of chord AHEAD of the CG");
            _o.WriteLine($"    measured stick-fixed neutral point x {npX:+0.000;-0.000} m, static margin " +
                         $"{sm * 100:F1} % chord\n");
        }

        // ---- 1. the trim ladder -------------------------------------------------------------------------------
        {
            var c = Load(Id);
            double stall = WingStallBodyAlphaRad(c) * 180 / Math.PI;
            double up = c.Controls.Elevator.LimitFor(-1) * 180 / Math.PI;
            _o.WriteLine("1. THE TRIM LADDER (power-off, flaps up, sea level). 'travel' is against the UP stop " +
                         $"({up:F1} deg); the wing needs alpha {stall:F2} deg for CLmax.");
            _o.WriteLine("    V m/s   V mph |  alpha deg   (of stall)  | elevator deg   % of travel | L/D   conv");
            for (double v = 30.0; v >= 22.0; v -= 0.5)
            {
                TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0);
                double ad = t.AlphaRad * 180 / Math.PI, ed = t.ElevatorRad * 180 / Math.PI;
                _o.WriteLine($"   {v,6:F1} {v / 0.44704,7:F1}  | {ad,9:F2} {ad / stall * 100,10:F0} %  | " +
                             $"{ed,11:F2} {Math.Abs(ed) / up * 100,12:F0} % | {t.GlideRatio,5:F2}  {(t.Converged ? "yes" : "NO")}");
            }
            var lim = Limit(c);
            _o.WriteLine($"  -> Vso scan stops at {lim.vso:F2} m/s ({lim.vso / 0.44704:F1} mph) with the elevator at " +
                         $"{lim.elevDeg:F2} deg = {lim.frac * 100:F0} % of travel, at alpha {lim.alphaDeg:F2} deg " +
                         $"= {lim.alphaDeg / stall * 100:F0} % of the wing's stalling alpha");
            _o.WriteLine($"     book Vso {BookVsoMph:F0} mph = {bookVso:F2} m/s, so the model reads " +
                         $"{(lim.vso / bookVso - 1) * 100:+0.0;-0.0} %\n");
        }

        // ---- the tail's own flow at the limiting condition ----------------------------------------------------
        {
            var c = Load(Id);
            var tables = Aircraft.BuildAirfoilTables(c);
            var lim = Limit(c);
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, lim.vso, 0);
            Vec3 v = new(lim.vso * Math.Cos(t.AlphaRad), 0, lim.vso * Math.Sin(t.AlphaRad));
            var cap = new List<ForceSample>(512);
            ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
            try { AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, 1.225, new ControlDeflections(0, t.ElevatorRad, 0, 0)); }
            finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
            _o.WriteLine($"   THE TAIL'S OWN FLOW at {lim.vso:F2} m/s, alpha {lim.alphaDeg:F2} deg, elevator {lim.elevDeg:F2} deg");
            _o.WriteLine("     x      y   |  alpha_t deg   downwash eps deg   eta_t = q_t/q_inf");
            var seen = new HashSet<string>();
            foreach (ForceSample fs in cap)
            {
                if (fs.Kind != "tailflow" || fs.PosBody.X > -1.0 || Math.Abs(fs.PosBody.Y) <= 0.03) continue;
                string k = $"{fs.PosBody.X:0.00}/{fs.PosBody.Y:0.00}";
                if (!seen.Add(k)) continue;
                _o.WriteLine($"   {fs.PosBody.X,6:F2} {fs.PosBody.Y,6:F2}  | {fs.MomentBody.X * 180 / Math.PI,11:F2}" +
                             $"{fs.MomentBody.Y * 180 / Math.PI,18:F2}{fs.MomentBody.Z,20:F3}");
            }
            _o.WriteLine("");
        }

        // ---- 2. the CG sweep, in TCDS inches ------------------------------------------------------------------
        {
            var c0 = Load(Id);
            double le = LowerWingLeX(c0), mac = c0.Surfaces[0].Strips[0].Chord;
            _o.WriteLine("2. CG, in the certificate's own units: inches AFT of the leading edge of the lower wing " +
                         "(TCDS A-743 Rev 16, 'Datum - Leading edge of lower wing'). Flight range -1.0 .. +7.1 in. " +
                         "Vso here is at a 0.1 m/s scan step, not the game's 0.5, so the trend is readable.");
            _o.WriteLine("     CG in   CG x m |   Vso m/s    mph  | elevator deg  % of 29.8 | alpha deg  stat.marg | note");
            foreach (double inAft in new[] { -1.0, 0.0, 0.24, 1.0, 1.34, 2.0, 2.54, 3.0, 3.53, 4.0, 4.38, 5.0, 6.0, 7.1, 9.0 })
            {
                var c = Load(Id);
                double cgx = le - inAft * 0.0254;
                c.Mass.Cg[0] = cgx;
                var lim = Limit(c, fine: true);
                var (_, sm) = NeutralPoint(c, 30.0, 3.0);
                string note = inAft switch
                {
                    -1.0 => "A-743 FORWARD limit",
                    0.24 => "the OLD config, before 2026-11-10",
                    1.34 => "W&B: solo rear, full fuel, empty CG -2.0 in",
                    2.54 => "W&B: solo rear, full fuel, empty CG -0.5 in",
                    3.53 => "AS CONFIGURED: W&B DUAL + full fuel at 2649 lb = the model's own mass",
                    4.38 => "W&B: dual, full fuel, empty CG +0.5 in",
                    7.1 => "A-743 AFT limit",
                    9.0 => "beyond the certificate",
                    _ => "",
                };
                _o.WriteLine($"   {inAft,8:+0.00;-0.00} {cgx,8:F3} | {lim.vso,9:F2} {lim.vso / 0.44704,6:F1}  | " +
                             $"{lim.elevDeg,12:F2} {lim.frac * 100,9:F0} % | {lim.alphaDeg,9:F2} {sm * 100,9:F1} % | {note}");
            }
            _o.WriteLine("");
            // THE REAL STOPS. A-743's Control Surface Movements table, identical in Section I and Section II:
            // "Elevator - Up 24 deg - Down 22 deg". The config's 29.8 deg symmetric is MORE travel than the real
            // aeroplane has, in both directions, so this is the combination that matters.
            _o.WriteLine("   WITH THE CERTIFICATE'S OWN STOPS (up 24 deg, down 22 deg), which the config now carries:");
            _o.WriteLine("     CG in |   Vso m/s    mph  | elevator deg  % of 24.0 | alpha deg | note");
            foreach (double inAft in new[] { 0.24, 2.54, 3.53, 5.0, 7.1, 9.0, 11.0 })
            {
                var c = Load(Id);
                c.Mass.Cg[0] = le - inAft * 0.0254;
                c.Controls.Elevator.MaxDeflRad = 22.0 * Math.PI / 180;
                c.Controls.Elevator.MaxDeflNegRad = 24.0 * Math.PI / 180;
                var lim = Limit(c, fine: true);
                _o.WriteLine($"   {inAft,8:+0.00;-0.00} | {lim.vso,9:F2} {lim.vso / 0.44704,6:F1}  | {lim.elevDeg,12:F2} " +
                             $"{lim.frac * 100,9:F0} % | {lim.alphaDeg,9:F2} | " +
                             $"{(inAft == 0.24 ? "the OLD config, before 2026-11-10" : inAft == 3.53 ? "AS CONFIGURED: the model's own mass, loaded per A-743's arms" : inAft == 7.1 ? "A-743 AFT limit" : "")}");
            }
            _o.WriteLine("");
        }

        // ---- 3. the elevator's share of the tail -------------------------------------------------------------
        {
            _o.WriteLine("3. ELEVATOR AREA, as its share of a horizontal tail of CONSTANT total area and constant " +
                         "leading edge: chord moved from the hStab row to the elevator row, station by station.");
            _o.WriteLine("    elev/htail  elev m2 |   Vso m/s    mph  | elevator deg  % travel | alpha deg");
            foreach (double frac in new[] { 0.28, 0.32, 0.36, 0.40, 0.43, 0.46, 0.50 })
            {
                var c = Load(Id);
                ReshapeElevator(c, frac);
                var (se, _) = Centroid(c, s => s == "elevator");
                var (sh, _) = Centroid(c, IsHtail);
                var lim = Limit(c);
                _o.WriteLine($"   {se / sh,10:F3} {se,9:F3}  | {lim.vso,9:F2} {lim.vso / 0.44704,6:F1}  | " +
                             $"{lim.elevDeg,12:F2} {lim.frac * 100,9:F0} % | {lim.alphaDeg,9:F2}" +
                             $"{(Math.Abs(lim.vso - bookVso) < 0.26 ? "   <- book Vso" : "")}");
            }
            _o.WriteLine("");
        }

        // ---- 4. the tail volume -------------------------------------------------------------------------------
        {
            var c0 = Load(Id);
            var (sw, xw0) = Centroid(c0, IsWing);
            double mac = c0.Surfaces[0].Strips[0].Chord;
            _o.WriteLine("4. TAIL VOLUME: the whole horizontal tail's AREA scaled at a fixed arm (chords and the " +
                         "elevator's 32 % share untouched, so this is more tail and not a different tail).");
            _o.WriteLine("    scale   Sh m2   Sh/Sw      Vh |   Vso m/s    mph  | elevator deg  % travel");
            foreach (double k in new[] { 0.9, 1.0, 1.1, 1.2, 1.3, 1.4 })
            {
                var c = Load(Id);
                foreach (SurfaceConfig sf in c.Surfaces)
                    if (IsHtail(sf.Id)) foreach (StripConfig st in sf.Strips) st.Area *= k;
                var (sh, xh) = Centroid(c, IsHtail);
                double arm = c.Mass.CgVec().X - xh;
                var lim = Limit(c);
                _o.WriteLine($"   {k,6:F2} {sh,7:F3} {sh / sw,7:F4} {sh * arm / (sw * mac),7:F4} | {lim.vso,9:F2} " +
                             $"{lim.vso / 0.44704,6:F1}  | {lim.elevDeg,12:F2} {lim.frac * 100,9:F0} %" +
                             $"{(Math.Abs(lim.vso - bookVso) < 0.26 ? "   <- book Vso" : "")}");
            }
            _o.WriteLine("");
        }

        // ---- 5. the travel, which Ty ruled out as an answer --------------------------------------------------
        {
            _o.WriteLine("5. TRAVEL, for scale only (Ty: investigate it properly, NOT 'just give it more travel'). " +
                         "Up stop only, as an asymmetric elevator has.");
            _o.WriteLine("    up stop deg |   Vso m/s    mph  | elevator deg  % travel | alpha deg");
            foreach (double upDeg in new[] { 29.8, 32.0, 34.0, 36.0, 38.0, 40.0 })
            {
                var c = Load(Id);
                c.Controls.Elevator.MaxDeflNegRad = upDeg * Math.PI / 180;
                var lim = Limit(c);
                _o.WriteLine($"   {upDeg,11:F1} | {lim.vso,9:F2} {lim.vso / 0.44704,6:F1}  | {lim.elevDeg,12:F2} " +
                             $"{lim.frac * 100,9:F0} % | {lim.alphaDeg,9:F2}" +
                             $"{(Math.Abs(lim.vso - bookVso) < 0.26 ? "   <- book Vso" : "")}");
            }
            _o.WriteLine("");
        }

        // ---- 6. the fleet, so the Stearman's numbers can be read against its own siblings --------------------
        {
            _o.WriteLine("6. THE FLEET, for context: how much of its travel each type needs at its OWN Vso, and the " +
                         "tail geometry behind it. Sorted by the margin left.");
            var rows = new List<(double frac, string line)>();
            foreach (string id in new[]
            {
                "stearman-pt17-like", "j3-cub-floats-like", "pa18-floats-like", "pa18-bush-like", "pa18-cub-like",
                "j3-cub-like", "pa25-pawnee-like", "jn4-jenny-like", "pitts-s2b-like", "decathlon-8kcab-like",
                "great-lakes-like", "glider-2-33-like",
            })
            {
                var c = Load(id);
                var (sw, xw) = Centroid(c, IsWing);
                var (sh, xh) = Centroid(c, IsHtail);
                var (se, _) = Centroid(c, s => s == "elevator");
                double mac = c.Surfaces[0].Strips[0].Chord, cg = c.Mass.CgVec().X, arm = cg - xh;
                var lim = Limit(c);
                var (_, sm) = NeutralPoint(c, Math.Max(20.0, lim.vso * 1.3), 3.0);
                rows.Add((lim.frac, $"   {id,-22} {lim.frac * 100,4:F0} % of travel at Vso {lim.vso / 0.44704,5:F1} mph" +
                                    $"  | up stop {c.Controls.Elevator.LimitFor(-1) * 180 / Math.PI,5:F1} deg" +
                                    $"  elev/ht {se / sh,5:F3}  Sh/Sw {sh / sw,6:F4}  Vh {sh * arm / (sw * mac),6:F4}" +
                                    $"  CG {(xw - cg) / mac * 100,6:F1} % chord aft of wing c/4  stat.marg {sm * 100,5:F1} %"));
            }
            rows.Sort((a, b) => b.frac.CompareTo(a.frac));
            foreach (var r in rows) _o.WriteLine(r.line);
            _o.WriteLine("");
        }

        // ---- 7. the two SOURCED geometry errors found while looking for the four ------------------------------
        //
        // TCDS A-743 Rev 16, NOTE 5 "Rigging specifications are as follows", verbatim:
        //     Incidence - Upper wing 4 deg, lower wing 3 deg
        //     Stagger   - 26-13/16" plus or minus 1/8" at strut point (spoilers on both wings or on neither wing)
        //
        // The config HAD the INCIDENCE THE OTHER WAY ROUND (upper 0.0524 rad = 3.00 deg, lower 0.0698 = 4.00 deg),
        // so its decalage was reversed: the certificate puts 1 deg MORE on the upper wing and the model put it on
        // the lower. On a biplane that decides which wing stalls first and what the cellule's CLmax is -- and with
        // the lift interference now applied per wing it decides it twice over, because the upper wing is also the
        // one that keeps the more of its isolated lift (BiplaneCellule.LiftInterferenceFor).
        //
        // And the STAGGER WAS 17.7 in where the certificate says 26-13/16 = 26.81 in: upper-wing leading edge at
        // model x +0.406, lower at -0.044. The upper wing was 0.231 m too far AFT, which dragged the cellule's lift
        // 0.117 m aft of where it belongs (area-weighted), and THAT was the static margin being 25.4 % of chord
        // when every other type in this fleet is at 6-18 % and the AeroModel's own note calls 5-20 % the
        // real-aeroplane band. Both were corrected on 2026-11-10; it is 13.1 % now. The 26-13/16 in also squares with the certificate's own loading arms:
        // the fuel tank is in the upper-wing centre section at (-5) in, and a tank at ~40 % of a 60 in chord puts
        // the upper leading edge about 29 in forward of the lower one.
        {
            // ALL OF THIS IS NOW IN THE CONFIG (2026-11-10, lane/claude-stearman-cellule), so the section is
            // inverted: each row PUTS BACK one of the old wrong values, which is the only way left to measure what
            // the correction was worth. The bottom row is the aeroplane as it now flies.
            _o.WriteLine("7. THE CORRECTIONS, EACH PUT BACK (TCDS A-743 Rev 16 NOTE 5 and Control Surface Movements). " +
                         "The config now carries all of them, so a row shows what the aeroplane was like WITHOUT that one.");
            _o.WriteLine("     variant                                    |   Vso m/s    mph  | elev deg  % travel | alpha  stat.marg");
            foreach (string which in new[] { "OLD: decalage back (upper 3 / lower 4)", "OLD: stagger back (17.7 in)",
                                             "OLD: CG back (+0.24 in)", "OLD: stops back (29.8 symmetric)",
                                             "OLD: cellule interference off", "OLD: all of them (the 2026-11-09 aeroplane)",
                                             "AS CONFIGURED (all corrections in)" })
            {
                var c = Load(Id);
                double le = LowerWingLeX(c);
                bool all = which.Contains("all of them");
                if (all || which.Contains("decalage"))
                    foreach (SurfaceConfig sf in c.Surfaces)
                        if (sf.Id == "wing-upper") foreach (StripConfig st in sf.Strips) st.IncidenceRad = 3.0 * Math.PI / 180;
                        else if (sf.Id.StartsWith("wing-lower")) foreach (StripConfig st in sf.Strips) st.IncidenceRad = 4.0 * Math.PI / 180;
                if (all || which.Contains("stagger"))
                {
                    // Back to 17.7 in of leading-edge stagger by moving the UPPER wing aft: the datum, and therefore
                    // the CG station, is defined off the LOWER wing, so only the upper one ever moves here.
                    double need = 0.450 - ((c.Surfaces.Find(s => s.Id == "wing-upper")!.Strips[0].Pos[0]
                                             + 0.25 * c.Surfaces.Find(s => s.Id == "wing-upper")!.Strips[0].Chord) - le);
                    foreach (SurfaceConfig sf in c.Surfaces)
                        if (sf.Id == "wing-upper") foreach (StripConfig st in sf.Strips) st.Pos[0] += need;
                }
                if (all || which.Contains("CG back")) c.Mass.Cg[0] = le + 0.24 * 0.0254;
                if (all || which.Contains("stops back")) { c.Controls.Elevator.MaxDeflRad = 0.52; c.Controls.Elevator.MaxDeflNegRad = 0.0; }
                if (all || which.Contains("interference off")) c.Cellule = null;
                var lim = Limit(c, fine: true);
                var (_, sm) = NeutralPoint(c, 30.0, 3.0);
                _o.WriteLine( $"   {which,-44} | {lim.vso,9:F2} {lim.vso / 0.44704,6:F1}  | {lim.elevDeg,8:F2} " +
                             $"{lim.frac * 100,9:F0} % | {lim.alphaDeg,6:F2} {sm * 100,9:F1} %");
            }
            // WHAT Vso IS BEING MEASURED AGAINST, because the "book 52" in the brief is Jane's LANDING speed and
            // not a stall. The primary stall figure is the Pilot's Handbook for Stearman Airplanes N2S-1/-2/-3
            // (Report A75NI-9001, 20 Jan 1941, Stearman Aircraft Division of Boeing, Bureau of Aeronautics), Table
            // of Characteristics: 53 mph for the N2S-1 "*With 50% Fuel and Oil Consumed", at a normal gross of
            // 2682.7 lb. 50 % of 46 gal of fuel and 4.4 gal of oil is 154 lb, so that stall is at about 2529 lb,
            // and the model flies at 2646 lb -- so the comparable figure is 53 * sqrt(2646/2529).
            double bookW = 2682.7 - 0.5 * (46 * 6.0 + 4.4 * 7.5);
            double corrected = 53.0 * Math.Sqrt(1200 / 0.45359237 / bookW);
            _o.WriteLine($"\n   THE BOOK FIGURE, weight-corrected: the handbook's 53 mph is at {bookW:F0} lb " +
                         $"(50 % fuel and oil of a 2682.7 lb gross); this model flies at {1200 / 0.45359237:F0} lb, " +
                         $"so the comparable stall is {corrected:F1} mph, not 52 (which is Jane's LANDING speed).");
        }
    }

    /// <summary>
    /// Move chord between the hStab row and the elevator row so the elevator becomes `frac` of the horizontal
    /// tail's area, at CONSTANT total area, constant strip span and constant leading edge — a bigger elevator on
    /// the same tailplane, which is the thing a 43 %-chord elevator on a Great Lakes tail actually is.
    ///
    /// The geometry the config holds: each strip's `pos[0]` is its own quarter-chord, its span is area/chord, and
    /// the hStab strip's trailing edge is the elevator strip's leading edge. So the section's leading edge is
    /// x_LE = x_hStab + c_h/4, the section chord is c = c_h + c_e, and the two rows are replaced at
    /// x_LE − c·(1−f)/4 and x_LE − c·(1−f) − c·f/4.
    /// </summary>
    private static void ReshapeElevator(AircraftConfig c, double frac)
    {
        SurfaceConfig h = c.Surfaces.Find(s => s.Id == "hStab")!, e = c.Surfaces.Find(s => s.Id == "elevator")!;
        for (int i = 0; i < h.Strips.Count && i < e.Strips.Count; i++)
        {
            StripConfig hs = h.Strips[i], es = e.Strips[i];
            double span = hs.Area / hs.Chord;                     // the station's width; it does not change
            double chord = hs.Chord + es.Chord;                   // the section chord; it does not change
            double xLe = hs.Pos[0] + 0.25 * hs.Chord;
            double ch = chord * (1.0 - frac), ce = chord * frac;
            hs.Chord = ch; hs.Area = ch * span; hs.Pos[0] = xLe - 0.25 * ch;
            es.Chord = ce; es.Area = ce * span; es.Pos[0] = xLe - ch - 0.25 * ce;
        }
    }
}

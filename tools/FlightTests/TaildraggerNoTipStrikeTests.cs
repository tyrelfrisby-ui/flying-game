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
/// Ty 2026-11-03 (contact-patch decision 2): "a well-flown airplane must NEVER drag a wingtip." The uncontrolled ground
/// loop still ends on a wingtip (GroundLoopTests / GroundStabilityProbe stop at the first airframe contact); these tests
/// are the other half: every taildragger in the fleet, flown with the CORRECT inputs by a closed-loop pilot, lands, rolls
/// out and taxis without a wingtip, a wing panel or a stab tip ever touching the ground, and no panel bends.
///
/// The fleet (configs/aircraft/*.json with a tailwheel entry), and why each is in or out:
///   IN  — every powered type standing on two mains and a tailwheel: AirCam, Cassutt, DC-3, Decathlon, Beaver, Extra 300,
///         Gee Bee R-2, Great Lakes, J-3 Cub, JN-4, P-51D, PA-18 Cub and Bush, PA-25, Pitts S-2B, Stearman.
///   OUT — the float and amphibian variants (j3-cub-floats, pa18-floats, dhc2-beaver-floats, aircam-amphib): their
///         "tailwheel" entries are float keel stations and they land on water, not a runway.
///   OUT — the gliders (2-33, EB29R, Swift): a monowheel glider has no track at all; its wing comes down onto the tip
///         skid by design at the end of every landing (the tip skid is a gear entry in the config), so "no wingtip
///         contact" is not a property it can have, and it cannot taxi.
///   OUT — target-drone: a Cassutt airframe flown by DronePilot off a launcher, no pilot and no landing.
///
/// The crosswind used per type (the demonstrated crosswind component, steady, direct 90°):
///   Extra 300: 15 kt, the Extra 300 information manual, limitations section (D-EJXA copy).
///   DHC-2 Beaver: 8.7 kt, the DHC-2 flight manual crosswind figure as quoted in ATSB report 198803506.
///   Everything else: no published demonstrated crosswind found (the Pitts S-2B POH figure could not be found; Aviat's
///   17 kt is for the S-2C; the vintage types predate the requirement), so the 14 CFR 23.233(a) rule — a 90° crosswind of
///   0.2 Vso must be demonstrated — on THIS model's Vso (PracticeScenario.EstimateVso, flaps up, sea level), copied in
///   as the literal below. The test re-measures that Vso and fails if it has drifted more than 3 % from the literal.
///
/// The pilot: wing-low (sideslip) to the flare — rudder holds the fuselage on the runway heading, aileron banks into the
/// wind to hold the centreline — then a three-point touchdown; on the rollout the stick comes full back, aileron goes
/// into the wind and increases as the speed decays (full at taxi speed), rudder chases a yaw-rate demand (heading and
/// centreline), differential brake when the rudder runs out; taxi turns on rudder/tailwheel and inside brake, with the
/// controls positioned for the wind: climb into a headwind, dive away from a tailwind. The elevator and power through the
/// approach and flare are the game's own landing law (PracticeScenario, LandingAileron); the lateral axes and brakes are
/// this pilot's.
///
/// What these found (2026-11-03): no wingtip ever came near the ground, but the J-3 and PA-18 elevator trailing corners
/// stood 5–6 cm off it at rest and scraped on firm three-point arrivals (1.4–1.8 kN on stab-TEcorner). Their hStab and
/// elevator strips sat on the CG line (z 0, a placeholder); the own-model J-3 puts the stab plane 0.485 m above it
/// (build_j3_cub.py Z_S 0.35 in the drawing frame, whose CG line is z −0.135). Raised to that in all five J-3 / PA-18
/// configs (one airframe, as with the eyes): rest clearance 0.06 → 0.54 m (J-3), 0.05 → 0.52 (PA-18), 0.18 → 0.66
/// (Bush); trim at 1.1–2.5 Vso moved ≤ 0.06° of elevator and Vso not at all; the rig was re-solved (RigTable).
/// </summary>
[Collection("WorldTerrainActive")]
public class TaildraggerNoTipStrikeTests : IDisposable
{
    public void Dispose() { WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null; }
    private readonly ITestOutputHelper _out;
    public TaildraggerNoTipStrikeTests(ITestOutputHelper o) { _out = o; }

    private const double Kt = 0.514444, Deg = Math.PI / 180.0;

    /// <summary>type, demonstrated crosswind used (kt), its source, and this model's Vso (kt) that the 0.2 Vso figures
    /// were taken from (literals, copied from <see cref="ModelVsoIsStillTheOneTheCrosswindsWereTakenFrom"/>'s output).</summary>
    public static IEnumerable<object[]> Fleet() => new[]
    {
        new object[] { "aircam-like",           8.2, Rule, 41.0 },
        new object[] { "cassutt-f1-like",       9.9, Rule, 49.6 },
        new object[] { "dc3-like",             14.4, Rule, 71.9 },
        new object[] { "decathlon-8kcab-like",  9.5, Rule, 47.6 },
        new object[] { "dhc2-beaver-like",      8.7, "DHC-2 flight manual crosswind figure, as quoted in ATSB report 198803506", 60.3 },
        new object[] { "extra-300-like",       15.0, "Extra 300 information manual, limitations: max demonstrated crosswind 15 kt", 55.4 },
        new object[] { "geebee-r2-like",       15.0, Rule, 74.8 },
        new object[] { "great-lakes-like",      8.9, Rule, 44.5 },
        new object[] { "j3-cub-like",           7.5, Rule, 37.5 },
        new object[] { "jn4-jenny-like",        6.8, Rule, 33.8 },
        new object[] { "p51d-like",            18.7, Rule, 93.3 },
        new object[] { "pa18-cub-like",         8.9, Rule, 44.7 },
        new object[] { "pa18-bush-like",        9.0, Rule, 45.1 },
        new object[] { "pa25-pawnee-like",      9.1, Rule, 45.7 },
        new object[] { "pitts-s2b-like",       11.7, Rule + " (no S-2B figure found; Aviat's 17 kt is the S-2C's)", 58.3 },
        new object[] { "stearman-pt17-like",    9.1, Rule, 45.7 },
    };
    private const string Rule = "none published: 14 CFR 23.233(a), 0.2 Vso (model Vso)";

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>The rule-of-thumb crosswinds are 0.2 × this model's Vso, copied in as literals: if the Vso moves more than
    /// 3 %, the crosswinds need copying again (deliberately, by hand).</summary>
    [Theory]
    [MemberData(nameof(Fleet))]
    public void ModelVsoIsStillTheOneTheCrosswindsWereTakenFrom(string type, double xwKt, string source, double vsoKt)
    {
        double vso = PracticeScenario.EstimateVso(Load(type), 0.0) / Kt;
        _out.WriteLine($"{type,-22} Vso {vso,5:F1} kt (literal {vsoKt:F1})  0.2 Vso {0.2 * vso,4:F1} kt; crosswind used {xwKt:F1} kt — {source}");
        Assert.InRange(vso, 0.97 * vsoKt, 1.03 * vsoKt);
        if (source.StartsWith("none published")) Assert.Equal(0.2 * vsoKt, xwKt, 1);
    }

    // ================================================================================================================
    // Measurement
    // ================================================================================================================

    /// <summary>What one manoeuvre did to the airframe.</summary>
    public sealed class Record
    {
        public double MinTipM = double.MaxValue, MinStabM = double.MaxValue, PeakBankDeg, PeakCentreM, PeakYawRateDeg;
        public string MinTipPoint = "", MinStabPoint = "";
        public List<string> Touches = new();
        public void Merge(Record o)
        {
            if (o.MinTipM < MinTipM) { MinTipM = o.MinTipM; MinTipPoint = o.MinTipPoint; }
            if (o.MinStabM < MinStabM) { MinStabM = o.MinStabM; MinStabPoint = o.MinStabPoint; }
            PeakBankDeg = Math.Max(PeakBankDeg, o.PeakBankDeg); PeakCentreM = Math.Max(PeakCentreM, o.PeakCentreM);
            PeakYawRateDeg = Math.Max(PeakYawRateDeg, o.PeakYawRateDeg);
            Touches.AddRange(o.Touches);
        }
        public override string ToString() => $"tip {MinTipM,5:F2} m ({MinTipPoint}) stab {MinStabM,5:F2} m ({MinStabPoint}) bank {PeakBankDeg,4:F1}° off-CL {PeakCentreM,4:F1} m r {PeakYawRateDeg,4:F0}°/s{(Touches.Count > 0 ? "  TOUCH: " + string.Join("; ", Touches.Distinct().Take(6)) : "")}";
    }

    private static bool IsWing(AirframeComponent c) => c is AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight or AirframeComponent.WingTipLowerLeft or AirframeComponent.WingTipLowerRight
        or AirframeComponent.WingLeft or AirframeComponent.WingRight or AirframeComponent.WingLeftOuter or AirframeComponent.WingRightOuter
        or AirframeComponent.WingLeftInner or AirframeComponent.WingRightInner or AirframeComponent.AileronLeft or AirframeComponent.AileronRight;
    private static bool IsStab(AirframeComponent c) => c is AirframeComponent.TailHorizontal or AirframeComponent.ElevatorLeft or AirframeComponent.ElevatorRight;

    /// <summary>Watches one aeroplane: clearances of every wing and stab hard point, their contact forces, scrapes and
    /// contact events, and the panels' state.</summary>
    private sealed class Watch
    {
        private readonly Aircraft _ac; private readonly List<ContactEvent> _ev = new();
        public Watch(Aircraft ac) { _ac = ac; ac.Contact += _ev.Add; }
        public void Sample(Record r)
        {
            RigidBodyState s = _ac.State; Vec3 cg = _ac.Config.Mass.CgVec();
            foreach (ContactPoint p in _ac.ContactPoints)
            {
                bool wing = IsWing(p.Component), stab = IsStab(p.Component);
                if (!wing && !stab) continue;
                Vec3 w = s.Position + s.Attitude.Rotate(p.Body - cg);
                double clear = -w.Z - WorldTerrain.GroundHeightAt(w.X, w.Y);
                if (wing && clear < r.MinTipM) { r.MinTipM = clear; r.MinTipPoint = p.Name; }
                if (stab && clear < r.MinStabM) { r.MinStabM = clear; r.MinStabPoint = p.Name; }
                if (p.NormalN > 0 || p.DepthM > 0) r.Touches.Add($"{p.Name} N {p.NormalN:F0}");
            }
            foreach (ScrapeState sc in _ac.Scrapes) if (IsWing(sc.Component) || IsStab(sc.Component)) r.Touches.Add("scrape " + sc.Part);
            foreach (ContactEvent e in _ev) if (IsWing(e.Component) || IsStab(e.Component) || e.Kind is ContactEventKind.PanelBent or ContactEventKind.PanelBroken) r.Touches.Add($"{e.Kind} {e.Part}");
            _ev.Clear();
            foreach (WingPanel p in _ac.Panels) if (p.State != PanelState.Intact) r.Touches.Add($"panel {p.Component} {p.State}");
            (double roll, _, _) = Euler(s.Attitude);
            r.PeakBankDeg = Math.Max(r.PeakBankDeg, Math.Abs(roll) / Deg);
            r.PeakCentreM = Math.Max(r.PeakCentreM, Math.Abs(s.Position.Y));
            r.PeakYawRateDeg = Math.Max(r.PeakYawRateDeg, Math.Abs(s.Rates.Z) / Deg);
        }
    }

    internal static (double roll, double pitch, double yaw) Euler(Quat q)
    {
        double roll = Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        double yaw = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        return (roll, pitch, yaw);
    }
    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    // ================================================================================================================
    // The pilot
    // ================================================================================================================

    /// <summary>Per-type numbers the pilot needs: stall speed, three-point attitude, stick per radian of alpha.</summary>
    private sealed class TypeData
    {
        public AircraftConfig C = null!;
        public double VsoMs, StanceRad, StickPerAlpha;
        public static TypeData For(AircraftConfig c)
        {
            var d = new TypeData { C = c, VsoMs = PracticeScenario.EstimateVso(c, 0.0) };
            var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tw = c.Gear.Find(g => g.IsTailwheel)!;
            d.StanceRad = Math.Atan((mains[0].Pos[2] - tw.Pos[2]) / (mains[0].Pos[0] - tw.Pos[0]));
            TrimSolver.Result a = TrimSolver.SolveGliderTrim(c, 1.3 * d.VsoMs, 100), b = TrimSolver.SolveGliderTrim(c, 1.6 * d.VsoMs, 100);
            d.StickPerAlpha = Math.Abs((Aircraft.StickForDeflection(a.ElevatorRad, c.Controls.Elevator) - Aircraft.StickForDeflection(b.ElevatorRad, c.Controls.Elevator)) / (a.AlphaRad - b.AlphaRad));
            if (!double.IsFinite(d.StickPerAlpha) || d.StickPerAlpha < 0.3) d.StickPerAlpha = 0.3;
            return d;
        }
        public double MainsHeight(RigidBodyState s)
        {
            Vec3 cg = C.Mass.CgVec(); double h = double.MaxValue;
            foreach (GearConfig g in C.Gear) if (!g.IsTailwheel) { Vec3 w = s.Position + s.Attitude.Rotate(g.PosVec() - cg); h = Math.Min(h, -w.Z); }
            return h;
        }
    }

    /// <summary>
    /// Approach, flare, three-point touchdown and rollout on a runway along +x (centreline y = 0), with a steady direct
    /// crosswind <paramref name="xwMs"/> from the right (+) or left (−). Returns the airborne record (spawn to touchdown) and
    /// the rollout record (touchdown to 4 m/s), and how the run ended.
    /// </summary>
    private (Record air, Record roll, string end) Land(TypeData d, double xwMs, bool trace = false)
    {
        AircraftConfig c = d.C;
        WorldTerrain.Active = null; Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null;
        // The game's own landing law flies the elevator and the power (PracticeScenario, LandingAileron: over the threshold
        // at 5 ft and 1.15 Vso, level for the first quarter of the runway, down to the hold-off, three-point; the law the
        // practice lessons have been regimen-tested with across the fleet). The wind is ours, and the pilot below has the
        // ailerons, rudder and brakes. Calm in the scenario: it never adds a wind of its own.
        var sc = new PracticeScenario(PracticeKind.LandingAileron, PracticeWind.Calm, c, PracticeScenarioTests.TestRunway(), 0.0);
        Aircraft ac = sc.Spawn();
        Vec3 wind = new(0, -xwMs, 0);          // from the right = blowing toward −y
        Atmosphere.SteadyWind = wind;
        {
            // Crab the spawn into the wind so the ground track still runs down the runway.
            RigidBodyState s0 = ac.State;
            Vec3 air0 = s0.Attitude.Rotate(s0.Velocity);
            double crab0 = Math.Asin(Math.Clamp(xwMs / air0.Length, -0.5, 0.5));
            var yaw = new Quat(0, 0, Math.Sin(crab0 / 2), Math.Cos(crab0 / 2));
            Quat att = Quat.Multiply(yaw, s0.Attitude);
            Vec3 ground = yaw.Rotate(air0) + wind;
            ac.State = new RigidBodyState(s0.Position, att, att.Conjugate().Rotate(ground), Vec3.Zero);
        }
        double crab = Euler(ac.State.Attitude).yaw;
        var sim = new SimLoop(ac); var watch = new Watch(ac);
        var air = new Record(); var roll = new Record();
        double side = Math.Sign(xwMs);                          // +1 wind from the right: right aileron is into wind
        double ailInt = 0, rudInt = 0, betaF = 0, gndRudInt = 0, gndT = 0, ailTd = 0;
        bool down = false; string end = "time";
        ControlInputs user = ControlInputs.Neutral;
        const double dt = 0.02;
        for (double t = 0; t < 150; t += dt)
        {
            ControlInputs game = sc.Step(ac, user, dt);
            user = sc.Autopilot;
            Atmosphere.SteadyWind = wind;                       // (Step installs the scenario's own, calm, wind)
            RigidBodyState s = ac.State;
            (double phi, double th, double psi) = Euler(s.Attitude);
            Vec3 vw = s.Attitude.Rotate(s.Velocity);
            Vec3 vAir = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
            double ias = vAir.Length, gs = Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y);
            double h = d.MainsHeight(s), y = s.Position.Y;
            double p = s.Rates.X, r = s.Rates.Z;
            if (!down && LandingGear.AnyMainWheelOnGround(c, s)) down = true;
            bool groundLaw = down && h < 0.25;     // a bounce goes back to flying it
            double ele = sc.Autopilot.Elevator, lever = sc.Autopilot.ThrottleLever;
            double ail, rud;
            if (!groundLaw)
            {
                // ---- wing low: rudder holds the fuselage on the runway heading, bank into the wind holds the centreline
                double psiCmd = crab * Math.Max(0, 1 - t / 4.0);
                ailInt = Math.Clamp(ailInt + 0.004 * y * dt, -0.3, 0.3);
                double phiCmd = Math.Clamp(-0.03 * y - 0.10 * vw.Y - ailInt, -0.3, 0.3);
                ail = Math.Clamp(2.0 * (phiCmd - phi) - 0.5 * p, -1, 1);
                double beta = ias > 3 ? Math.Asin(Math.Clamp(vAir.Y / ias, -1, 1)) : 0;
                betaF += (beta - betaF) * Math.Min(1, dt / 0.5);
                double psiE = Wrap(psi - psiCmd);
                rudInt = Math.Clamp(rudInt + 0.5 * psiE * dt, -0.6, 0.6);
                rud = Math.Clamp(-2.5 * psiE - 1.0 * r - 1.5 * betaF - rudInt, -1, 1);
                ac.BrakeInput = 0; ac.BrakeBias = 0; gndT = 0;
                if (!down) ailTd = Math.Clamp(Math.Abs(ail), 0.2, 1.0);
                watch.Sample(down ? roll : air);
            }
            else
            {
                gndT += dt;
                // ---- rollout: aileron into the wind, more as it slows (full by 0.4 Vso); rudder on a yaw-rate demand from
                // the heading and the centreline (the tailwheel reflex), differential brake when the rudder runs out.
                double f = Math.Clamp((d.VsoMs - ias) / (0.6 * d.VsoMs), 0, 1);
                ail = Math.Clamp(side * (ailTd + (1 - ailTd) * f) - 1.0 * phi - 0.2 * p, -1, 1);
                double rCmd = Math.Clamp(-2.5 * psi - 0.05 * y, -0.35, 0.35);
                gndRudInt = Math.Clamp(gndRudInt - 0.8 * psi * dt, -0.6, 0.6);
                rud = Math.Clamp(-30.0 * (r - rCmd) + gndRudInt, -1, 1);
                // Stick: the game's three-point hold for the first second, then FULL back (the tail stays on the ground and
                // the brakes cannot stand it on its nose). Brakes: gently once below 0.8 Vso, and let go of them as soon as
                // the tail starts to come up (a pilot feels the tail get light).
                if (gndT > 1.0 && ias < 0.95 * d.VsoMs) ele = Math.Max(-1.0, Math.Min(ele, -1.0 * (gndT - 1.0)));
                double tailUp = Math.Clamp((d.StanceRad - 2 * Deg - th) / (2 * Deg), 0, 1);   // 0 tail down … 1 two degrees light
                double brake = gndT > 2.0 && gs > 1.5 && ias < 0.8 * d.VsoMs ? 0.3 * (1 - tailUp) : 0.0;
                double bias = Math.Clamp(rud * 0.8, -1, 1);
                if (Math.Abs(rud) > 0.95 && gs > 1.0) { brake = Math.Max(brake, 0.6 * (1 - tailUp)); bias = Math.Sign(rud); }
                ac.BrakeInput = brake; ac.BrakeBias = bias;
                if (gs > 4.0) watch.Sample(roll);
                else { end = "taxi speed"; break; }
            }
            if (trace && Math.Abs(t / 0.5 - Math.Round(t / 0.5)) < 1e-6)
                _out.WriteLine($"  t={t,5:F1} x {s.Position.X,6:F0} y {y,5:F1} h {h,5:F2} ias {ias,5:F1} gs {gs,5:F1} φ {phi / Deg,5:F1} θ {th / Deg,5:F1} ψ {psi / Deg,5:F1} ail {ail,5:F2} ele {ele,5:F2} rud {rud,5:F2} lev {lever,5:F2} brk {ac.BrakeInput:F2} {(down ? "GND" : "")}");
            if (ac.LostComponents.Count > 0) { end = "lost " + string.Join(",", ac.LostComponents); break; }
            sim.RunFor(dt, new ControlInputs(ail, ele, rud, lever));
        }
        return (air, roll, end);
    }


    /// <summary>
    /// A taxi turn from rest: accelerate to <see cref="TaxiMs"/> on the runway heading, turn <paramref name="turnDeg"/>
    /// (+ right) at up to 12°/s on rudder/tailwheel steering and the inside brake, roll out and taxi straight 4 s. Wind
    /// <paramref name="windMs"/> comes FROM <paramref name="windFromDeg"/> (compass, relative to the starting heading).
    /// Controls for the wind: climb into a headwind (stick back, into wind), dive away from a tailwind (stick forward,
    /// away from it); calm: stick full back, ailerons neutral.
    /// </summary>
    private (Record rec, string end) Taxi(TypeData d, double turnDeg, double windMs, double windFromDeg, bool trace = false)
    {
        AircraftConfig c = d.C;
        WorldTerrain.Active = null; Atmosphere.Field = null; Atmosphere.ActiveTurbulence = null;
        double wf = windFromDeg * Deg;
        Atmosphere.SteadyWind = new Vec3(-windMs * Math.Cos(wf), -windMs * Math.Sin(wf), 0);
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac); var watch = new Watch(ac); var rec = new Record();
        const double dt = 0.02, rMax = 12 * Deg;
        double target = turnDeg * Deg, thrInt = 0, rudInt = 0, settled = -1; string end = "time";
        bool hasBrakes = c.Gear.Exists(g => g.Brake);
        for (double t = 0; t < 90; t += dt)
        {
            RigidBodyState s = ac.State;
            (double phi, double th, double psi) = Euler(s.Attitude);
            Vec3 vw = s.Attitude.Rotate(s.Velocity);
            double gs = Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y), r = s.Rates.Z;
            double psiTarget = t < 6 ? 0 : target;
            double psiErr = Wrap(psiTarget - psi);
            if (t >= 6 && settled < 0 && Math.Abs(psiErr) < 3 * Deg && Math.Abs(r) < 2 * Deg) settled = t;
            if (settled >= 0 && t > settled + 4) { end = "done"; break; }
            double rCmd = Math.Clamp(1.2 * psiErr, -rMax, rMax);
            rudInt = Math.Clamp(rudInt - 2.0 * (r - rCmd) * dt, -0.7, 0.7);
            double rud = Math.Clamp(-30.0 * (r - rCmd) + rudInt, -1, 1);
            // Power for the taxi speed; both brakes if too fast; the inside brake when the rudder runs out.
            thrInt = Math.Clamp(thrInt + 0.05 * (TaxiMs - gs) * dt, 0, 0.5);
            double thr = Math.Clamp(0.12 * (TaxiMs - gs) + thrInt, 0, 0.7);
            double brake = gs > TaxiMs + 1.0 ? Math.Clamp(0.3 * (gs - TaxiMs - 1.0), 0, 0.4) : 0, bias = 0;
            if (Math.Abs(rud) > 0.9 && Math.Abs(r) < Math.Abs(rCmd))
            {
                double inside = Math.Clamp(4.0 * (Math.Abs(rCmd) - Math.Abs(r)) / rMax, 0, 0.7);
                brake = Math.Max(brake, inside); bias = Math.Sign(rCmd);
                // No brakes and a fixed skid (the JN-4): a burst of power over the full rudder is all there is.
                if (!hasBrakes) thr = Math.Max(thr, 0.45);
            }
            ac.BrakeInput = brake; ac.BrakeBias = bias;
            // Wind relative to the nose (+ = from the right).
            double rel = Wrap(wf - psi);
            double ail = 0, ele = -1;
            if (windMs > 0.1)
            {
                double head = Math.Cos(rel), cross = Math.Sin(rel);
                ail = Math.Clamp(2.0 * cross * Math.Clamp(3.0 * head, -1, 1), -1, 1);      // into a headwind, away from a tailwind
                ele = -Math.Clamp(3.0 * head, -1, 1);                                      // back into a headwind, forward with a tailwind
            }
            if (trace && Math.Abs(t / 0.5 - Math.Round(t / 0.5)) < 1e-6)
                _out.WriteLine($"  t={t,5:F1} gs {gs,4:F1} ψ {psi / Deg,6:F1} r {r / Deg,5:F1} φ {phi / Deg,5:F1} θ {th / Deg,5:F1} rel {rel / Deg,5:F0} ail {ail,5:F2} ele {ele,5:F2} rud {rud,5:F2} brk {brake:F2}/{bias:F0} thr {thr:F2}");
            sim.RunFor(dt, new ControlInputs(ail, ele, rud, 1 - 2 * thr));
            watch.Sample(rec);
            if (ac.LostComponents.Count > 0) { end = "lost " + string.Join(",", ac.LostComponents); break; }
        }
        return (rec, end);
    }
    public const double TaxiMs = 3.0;   // a brisk walk, ~6 kt

    // ================================================================================================================
    // The tests
    // ================================================================================================================

    /// <summary>The Gee Bee is flown only on the ground here: the game's landing law (PracticeScenario) porpoises it in the
    /// 5 ft hold at 1.15 Vso (h 0.3 → 3.0 m, stick/alpha 0.53 — the most pitch-sensitive type in the fleet), it arrives at
    /// ~85 kt, bounces 3 m and comes back down nose-low on its propeller (calm, wind from the left) or on one main
    /// (wind from the right). That is a controller that cannot fly a Gee Bee landing, not a well-flown landing; its
    /// wingtips still never touched (closest 0.15 m in the bounces).</summary>
    public static IEnumerable<object[]> LandingFleet() => Fleet().Where(r => (string)r[0] != "geebee-r2-like");
    private const string GeeBeeSkip = "2026-11-03: the game's landing law (PracticeScenario LandingAileron) porpoises the Gee Bee in the 5 ft hold and bounces it onto its propeller / one main — the controller cannot land it, so there is no well-flown landing to check. Its taxi turns are checked. Re-enable with a Gee Bee wheel-landing law.";

    private void Report(string type, string what, Record r, string end)
        => _out.WriteLine($"{type,-22} {what,-26} {r} [{end}]");

    private static void AssertClean(string type, string what, Record r)
    {
        Assert.True(r.Touches.Count == 0, $"{type} {what}: the airframe touched — {string.Join("; ", r.Touches.Distinct().Take(8))}");
        Assert.True(r.MinTipM > 0, $"{type} {what}: wing {r.MinTipPoint} {r.MinTipM:F3} m");
        Assert.True(r.MinStabM > 0, $"{type} {what}: stab {r.MinStabPoint} {r.MinStabM:F3} m");
    }

    private void Landing(string type, double xwMs, string what)
    {
        var d = TypeData.For(Load(type));
        var (air, roll, end) = Land(d, xwMs);
        Report(type, what + ", flare", air, "");
        Report(type, what + ", rollout", roll, end);
        Assert.Equal("taxi speed", end);              // down, slowed to a taxi, nothing lost
        AssertClean(type, what + " (approach and flare)", air);
        AssertClean(type, what + " (rollout to taxi speed)", roll);
    }

    /// <summary>(1) + (3): a three-point landing in calm air from 5 ft over the threshold, rolled out to taxi speed.</summary>
    [Theory]
    [MemberData(nameof(LandingFleet))]
    public void NormalLandingAndRolloutNeverTouchATipOrAStab(string type, double xwKt, string source, double vsoKt)
    {
        _ = xwKt; _ = source; _ = vsoKt;
        Landing(type, 0.0, "calm landing");
    }

    /// <summary>(2) + (3): wing-low crosswind landing in the demonstrated crosswind, from the right.</summary>
    [Theory]
    [MemberData(nameof(LandingFleet))]
    public void CrosswindLandingFromTheRightNeverTouchesATipOrAStab(string type, double xwKt, string source, double vsoKt)
    {
        _ = source; _ = vsoKt;
        Landing(type, xwKt * Kt, $"{xwKt:F1} kt xw from R");
    }

    /// <summary>(2) + (3): the same from the left.</summary>
    [Theory]
    [MemberData(nameof(LandingFleet))]
    public void CrosswindLandingFromTheLeftNeverTouchesATipOrAStab(string type, double xwKt, string source, double vsoKt)
    {
        _ = source; _ = vsoKt;
        Landing(type, -xwKt * Kt, $"{xwKt:F1} kt xw from L");
    }

    [Fact(Skip = GeeBeeSkip)]
    public void GeeBeeLandings() { }

    /// <summary>(4): taxi turns at a brisk walk — 90° each way and 180° in calm air, and 180° each way starting with the
    /// demonstrated crosswind value quartering from ahead (it ends quartering from behind), controls positioned for it.</summary>
    [Theory]
    [MemberData(nameof(Fleet))]
    public void TaxiTurnsNeverTouchATipOrAStab(string type, double xwKt, string source, double vsoKt)
    {
        _ = source; _ = vsoKt;
        var d = TypeData.For(Load(type));
        double xw = xwKt * Kt;
        var turns = new (double turn, double windMs, double fromDeg, string what)[]
        {
            (90, 0, 0, "90° R, calm"), (-90, 0, 0, "90° L, calm"), (180, 0, 0, "180° R, calm"),
            (180, xw, -45, $"180° R, {xwKt:F1} kt from L-front"), (-180, xw, 45, $"180° L, {xwKt:F1} kt from R-front"),
        };
        var failures = new List<string>();
        foreach (var (turn, windMs, fromDeg, what) in turns)
        {
            var (rec, end) = Taxi(d, turn, windMs, fromDeg);
            Report(type, "taxi " + what, rec, end);
            if (rec.Touches.Count > 0 || rec.MinTipM <= 0 || rec.MinStabM <= 0) failures.Add($"{what}: {rec}");
            else if (end != "done") failures.Add($"{what}: the turn was not completed [{end}]");
        }
        Assert.True(failures.Count == 0, $"{type}:\n" + string.Join("\n", failures));
    }

    /// <summary>Standing on three points, stick back: every wing and stab point is off the ground, and the J-3 / PA-18
    /// elevator trailing corner is where the real one is. The own-model J-3 (tools/own_models/build_j3_cub.py: stab plane
    /// z 0.35 m in the drawing frame, tail tyre bottom −0.111 m, elevator TE xd ≈ 6.67 vs tail-wheel contact xd 6.58, 13.5°
    /// stance) puts it ≈ 0.44 m up; before 2026-11-03 the config's stab sat on the CG line and it was 0.06 m (0.05 PA-18).</summary>
    [Theory]
    [MemberData(nameof(Fleet))]
    public void AtRestEveryWingAndStabPointClearsTheGround(string type, double xwKt, string source, double vsoKt)
    {
        _ = xwKt; _ = source; _ = vsoKt;
        var c = Load(type);
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        for (double t = 0; t < 3; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, -1, 0, 1.0));
        var rec = new Record(); new Watch(ac).Sample(rec);
        (_, double th, _) = Euler(ac.State.Attitude);
        Report(type, $"at rest, pitch {th / Deg:F1}°", rec, "");
        AssertClean(type, "at rest", rec);
        if (type is "j3-cub-like" or "pa18-cub-like" or "pa18-bush-like") Assert.InRange(rec.MinStabM, 0.40, 0.75);
    }
}

using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// SIDESLIP-TO-PITCH COUPLING, MEASURED (lane/claude-slip-pitch, 2026-11-10). Static only: no flying, no trim, no
/// integration. The aeroplane is HELD at a body angle of attack α and a sideslip β with its rates at zero, at one speed
/// and altitude, and the model's own pitching moment about the CG is summed BY SOURCE. The quantity reported is
///
///     ΔCm(α, β) = (M_y(α, β) − M_y(α, 0)) / (q̄ S c̄)
///
/// the pitching moment the SIDESLIP ITSELF makes, with the α-only pitching moment taken out at each α. It is even in β
/// (the aeroplane is left-right symmetric) and in a real aeroplane it is NOSE-DOWN: ΔCm &lt; 0, growing with |β|. It is
/// what a knife-edge, a slow roll and a tumble all live on.
///
/// WHY THIS PROBE EXISTS. Owner Ty (competition aerobatic pilot, Pitts S-2B, 2026-11-03, re-confirmed 2026-11-10), on
/// the Endo tumble: the nose-down sideslip-to-pitch coupling is "still too weak by a factor of at least 5", and —
/// verbatim — "Do not scale it by 5; find the physics." You cannot find a factor of five you have not measured, and
/// the number has to be a coefficient, not a moment, because the only published data to check it against
/// (Polhamus, NACA RM L55E20b) is in coefficient form.
///
/// WHERE THE SHAPE OF THIS CAME FROM. The per-source static map is `EndoMomentMapProbe` from the orphaned
/// lane/claude-endo (2026-11-03, docs/aircraft/extra-300-endo-DERIVATION.md §6); its At() is kept almost whole. What is
/// NEW here: the source attribution is exact (each strip force is matched to its own surface through
/// AeroModel.LoadPoint, instead of being bucketed by "is it more than 1.5 m behind the CG", which mis-sorts a biplane's
/// lower wing and a short-coupled Pitts' tail), the tail volume coefficient is printed so the number can be compared
/// with a wind-tunnel configuration that has a different tail, and the ΔCm matrix is printed on its own so a
/// before/after is one diff. The slipstream-drift half of that lane is NOT carried over: today's `PropJet` traces the
/// jet from the disc through the air's own motion, which is the same physics done better, and it is already shipped.
///
/// SLIPPITCH=1 runs it. SLIPPITCH_IDS=a,b chooses the types (default: the two aeroplanes Ty flies the manoeuvre in).
/// SLIPPITCH_NOVORTEX=1 flies the tail without the fuselage's crossflow vortices, for the before/after.
/// SLIPPITCH_V, SLIPPITCH_ALT override the flow condition.
/// </summary>
[Collection("WorldTerrainActive")]
public class SlipPitchProbe
{
    private readonly ITestOutputHelper _out;
    public SlipPitchProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>The Endo's own condition: Ty flies the entry at about 100 KIAS at a few thousand feet, which is 54 m/s
    /// TAS at 1000 m ISA. Kept identical to lane/claude-endo's maps so the two runs' numbers are comparable.</summary>
    public static readonly double V = Env("SLIPPITCH_V", 54.0), AltM = Env("SLIPPITCH_ALT", 1000.0);

    private static double Env(string k, double dflt)
        => Environment.GetEnvironmentVariable(k) is string s && s.Length > 0 ? double.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : dflt;

    /// <summary>0 fuselage (crossflow, camber, Munk, body side force and drag, body damping), 1 wing (every wing and
    /// aileron strip), 2 stab (hStab + elevator), 3 fin (vStab + rudder), 4 prop (thrust, torque, P-factor, disc normal
    /// force, slipstream yaw; the gyro is zero here because the rates are).</summary>
    public static readonly string[] Src = { "fuselage", "wing", "stab", "fin", "prop" };

    public sealed class Map
    {
        public double[] M = new double[5];
        public double MTotal => M.Sum();
    }

    /// <summary>Body velocity for a held α and β. Positive β = the air comes from the RIGHT (body v &gt; 0), which is
    /// the sign convention the rest of the repo's probes use.</summary>
    public static Vec3 Velocity(double alphaDeg, double betaDeg, double v)
    {
        double a = alphaDeg * Math.PI / 180, b = betaDeg * Math.PI / 180;
        return new Vec3(v * Math.Cos(a) * Math.Cos(b), v * Math.Sin(b), v * Math.Sin(a) * Math.Cos(b));
    }

    /// <summary>Which bucket each strip's load point belongs to, keyed on the load point itself. AeroModel emits a
    /// strip's "lift"/"drag"/"moment" samples at <see cref="AeroModel.LoadPoint"/>, and a hinged section's loads act at
    /// the SECTION quarter-chord, so the key has to be built the same way the model builds it — walking the surfaces in
    /// order with the same running strip index — or an aileron row lands on the wrong surface.</summary>
    public static Dictionary<(long, long, long), int> BucketByLoadPoint(AircraftConfig cfg)
    {
        var map = new Dictionary<(long, long, long), int>();
        int idx = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            string key = AeroModel.SystemKey(sf.Id);
            int bucket = key.Equals("hStab", StringComparison.OrdinalIgnoreCase) ? 2
                       : key.Equals("vStab", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            foreach (StripConfig st in sf.Strips)
            {
                Vec3 p = AeroModel.LoadPoint(cfg, idx, st);
                map[Key(p)] = bucket;
                idx++;
            }
        }
        return map;
    }

    private static (long, long, long) Key(Vec3 p)
        => ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));

    /// <summary>Pitching moment about the CG at (α, β), by source. elevator / rudder / aileron are STICK fractions
    /// (−1..1) shaped as the sim shapes them (full travel = the axis limit on that side).</summary>
    public static Map At(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables,
                         Dictionary<(long, long, long), int> buckets, double alphaDeg, double betaDeg,
                         double elevator = 0, double rudder = 0, double aileron = 0, double throttle = 1.0, double v = 0,
                         bool[]? surfaceMask = null)
    {
        if (v <= 0) v = V;
        double rho = Atmosphere.DensityAtAltitude(AltM);
        Vec3 vel = Velocity(alphaDeg, betaDeg, v);
        double Defl(double s, ControlAxisConfig ax) => s == 0 ? 0 : Math.Sign(s) * Math.Abs(s) * ax.LimitFor(Math.Sign(s));
        var controls = new ControlDeflections(Defl(aileron, cfg.Controls.Aileron), Defl(elevator, cfg.Controls.Elevator),
                                              Defl(rudder, cfg.Controls.Rudder), 0);
        var (du, r, swirl) = Aircraft.TailSlipstream(cfg, throttle, vel, Vec3.Zero, rho);
        Vec3 cg = cfg.Mass.CgVec();
        var map = new Map();
        ForceDebug.Samples = new List<ForceSample>(512);
        try
        {
            AeroModel.Compute(cfg, tables, vel, Vec3.Zero, Vec3.Zero, rho, controls, -1.0, null, du, r, surfaceMask, slipstreamSwirlMs: swirl);
            // The hub has to be the REAL one: the "thrust" sample is applied there, so a wrong hub is a wrong thrust
            // pitching moment (an arm error of 0.1 m on 3 kN is 300 N·m, which is the size of the term being measured).
            // AirframeContact.PropHubsBody is the same construction the sim itself uses for _noseBody.
            var hubs = AirframeContact.PropHubsBody(cfg);
            Aircraft.PropulsionForces(cfg, throttle, vel, Vec3.Zero, rho, false, hubs.Count > 0 ? hubs[0] : new Vec3(1.5, 0, 0));
            foreach (ForceSample f in ForceDebug.Samples)
            {
                int k;
                switch (f.Kind)
                {
                    case "lift":
                    case "drag":
                    case "moment":
                    case "spoiler":
                        // A sample at the CG with no force is one of the fuselage's bulk moments (Munk in pitch and
                        // yaw, the body's rate damping, the separated wing's sideslip roll): those are bucketed with
                        // the body. Everything else is a strip, matched to its own surface.
                        if ((f.PosBody - cg).Length < 1e-6) k = 0;
                        else if (!buckets.TryGetValue(Key(f.PosBody), out k)) k = 1;
                        break;
                    case "fuselage": k = 0; break;
                    case "thrust": k = 4; break;
                    // "tailflow", "tailq", "gyro", "propnormal", "pfactor", "flow": breakdown records, not forces.
                    // ForceDebug's own doc-comment records what happened the last time one of these was summed as a
                    // force (a phantom +965 ft·lb in the Pitts' flat-spin pitch budget, 2026-11-09).
                    default: continue;
                }
                map.M[k] += Vec3.Cross(f.PosBody - cg, f.ForceBody).Y + f.MomentBody.Y;
            }
        }
        finally { ForceDebug.Samples = null; }
        return map;
    }

    /// <summary>
    /// WHICH ASSUMPTION CARRIES THE MISSING FACTOR (SLIPPITCH_SWEEP=1). The measurement says the horizontal tail
    /// carries nothing of the sideslip-to-pitch coupling and that lane/claude-endo's symmetric vortex pair adds a
    /// ninth of what Polhamus measured. A symmetric pair about the tail's own plane CANNOT load the tail: the two
    /// members' induced vertical velocities cancel when integrated across the span, so the only thing left is the
    /// small residue from the tail sitting a little off the pair's centre line. That is a prediction, so this sweeps
    /// each of lane/claude-endo's four assumed quantities one at a time and prints what each is worth. It is a
    /// MEASUREMENT of the mechanism's sensitivity, not a fit: whichever one carries the factor then has to be
    /// justified from geometry or a source, or the term does not ship.
    /// </summary>
    [Fact]
    public void Sweep()
    {
        if (Environment.GetEnvironmentVariable("SLIPPITCH_SWEEP") == null) return;
        string[] ids = Environment.GetEnvironmentVariable("SLIPPITCH_IDS")?.Split(',') ?? new[] { "extra-300-like", "pitts-s2b-like" };
        int[] betas = { 15, 25, 30, 45, 60, 75 };
        foreach (string id in ids)
        {
            AircraftConfig cfg = Load(id);
            var tables = Aircraft.BuildAirfoilTables(cfg);
            var buckets = BucketByLoadPoint(cfg);
            var (S, c) = WingRef(cfg);
            double qSc = 0.5 * Atmosphere.DensityAtAltitude(AltM) * V * V * S * c;
            var sb = new StringBuilder();
            sb.AppendLine($"==== {id}: ΔCm vs β, POWER OFF, α 0, controls neutral. The TAIL's own share (tail on − tail off).");
            sb.AppendLine("     Polhamus RM L55E20b: the tail's share is −0.13 to −0.17 by β 25–30°, the body's −0.02 to −0.03.");
            bool[] off = TailOffMask(cfg);

            var cases = new List<(string Name, BodyVortex.Knobs K)>();
            BodyVortex.Knobs shipped = BodyVortex.Knobs.Shipped;
            cases.Add(("endo lane as written", shipped));
            var k = shipped; k.LowerVortexShare = 0.0; cases.Add(("upper vortex only", k));
            k = shipped; k.SymmetricLimitDeg = 90; k.FadeDeg = 1; cases.Add(("no regime fade-out", k));
            k = shipped; k.MaxRadiusOverA = 4.0; cases.Add(("r cap 2a -> 4a", k));
            k = shipped; k.CentreZ = -0.30; cases.Add(("pair centre 0.30 m UP", k));
            k = shipped; k.CentreZ = 0.30; cases.Add(("pair centre 0.30 m DOWN", k));
            k = shipped; k.LowerVortexShare = 0.0; k.SymmetricLimitDeg = 90; k.FadeDeg = 1; cases.Add(("upper only + no fade", k));
            foreach (double core in new[] { 0.25, 0.5, 1.0, 1.5 })
            { k = shipped; k.CoreOverA = core; cases.Add(($"pair, core {core:F2}a", k)); }
            foreach (double core in new[] { 0.25, 0.5, 1.0, 1.5 })
            { k = shipped; k.LowerVortexShare = 0.0; k.SymmetricLimitDeg = 90; k.FadeDeg = 1; k.CoreOverA = core; cases.Add(($"upper only, no fade, core {core:F2}a", k)); }

            sb.Append($"   {"case",32} |");
            foreach (int b in betas) sb.Append($"{"β" + b,9}");
            sb.AppendLine("     (tail's share of ΔCm)");
            try
            {
                foreach (var (name, kn) in cases)
                {
                    BodyVortex.Diag = kn;
                    sb.Append($"   {name,32} |");
                    foreach (int b in betas)
                    {
                        double on = At(cfg, tables, buckets, 0, b, throttle: 0).MTotal - At(cfg, tables, buckets, 0, 0, throttle: 0).MTotal;
                        double ofv = At(cfg, tables, buckets, 0, b, throttle: 0, surfaceMask: off).MTotal
                                   - At(cfg, tables, buckets, 0, 0, throttle: 0, surfaceMask: off).MTotal;
                        sb.Append($"{(on - ofv) / qSc,9:F3}");
                    }
                    sb.AppendLine();
                }
            }
            finally { BodyVortex.Diag = BodyVortex.Knobs.Shipped; }
            _out.WriteLine(sb.ToString());
        }
    }

    /// <summary>SLIPPITCH_INDUCED=1: the vortex pair itself and the vertical air velocity it puts at each TAIL strip,
    /// so the geometry can be checked against a hand calculation before any aerodynamic response is believed.</summary>
    [Fact]
    public void InducedFlow()
    {
        if (Environment.GetEnvironmentVariable("SLIPPITCH_INDUCED") == null) return;
        string[] ids = Environment.GetEnvironmentVariable("SLIPPITCH_IDS")?.Split(',') ?? new[] { "extra-300-like", "pitts-s2b-like" };
        double lower = double.Parse(Environment.GetEnvironmentVariable("SLIPPITCH_LOWER") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        foreach (string id in ids)
        {
            AircraftConfig cfg = Load(id);
            double rho = Atmosphere.DensityAtAltitude(AltM);
            var kn = BodyVortex.Knobs.Shipped; kn.LowerVortexShare = lower; BodyVortex.Diag = kn;
            try
            {
                foreach (int beta in new[] { 25, 30, 45, 60 })
                {
                    Vec3 vel = Velocity(0, beta, V);
                    BodyVortex.Pair pr = BodyVortex.For(cfg, vel, rho);
                    sb.AppendLine($"==== {id} α 0 β {beta}: active {pr.Active} a {pr.A:F3} core {pr.Core:F3} Γ {pr.G:F1} m²/s  frame vortex ({pr.X1:F3}, {pr.Y1:F3})  Zc {pr.Zc:F2}  lower share {pr.Lower:F2}");
                    double sum = 0, span = 0;
                    int idx = 0;
                    foreach (SurfaceConfig sf in cfg.Surfaces)
                        foreach (StripConfig st in sf.Strips)
                        {
                            Vec3 at = AeroModel.LoadPoint(cfg, idx++, st);
                            string key = AeroModel.SystemKey(sf.Id);
                            if (!key.Equals("hStab", StringComparison.OrdinalIgnoreCase)) continue;
                            double w = st.Chord > 1e-9 ? st.Area / st.Chord : 0.0;
                            Vec3 ind = BodyVortex.Induced(pr, at, 0.5 * w, false);
                            sb.AppendLine($"     {sf.Id,-10} y {at.Y,6:F2} z {at.Z,6:F2} width {w,5:F2} | air vy {ind.Y,7:F2} vz {ind.Z,7:F2} m/s | upwash Δα {(-ind.Z / (V * Math.Cos(beta * Math.PI / 180)) * 180 / Math.PI),6:F2}°");
                            sum += -ind.Z * w; span += w;
                        }
                    sb.AppendLine($"     span-mean UPWASH over the hStab: {(span > 0 ? sum / span : 0),6:F2} m/s over {span:F2} m  → mean Δα {(span > 0 ? Math.Atan2(sum / span, V * Math.Cos(beta * Math.PI / 180)) * 180 / Math.PI : 0),6:F2}°");
                }
            }
            finally { BodyVortex.Diag = BodyVortex.Knobs.Shipped; }
        }
        _out.WriteLine(sb.ToString());
    }

    /// <summary>
    /// DOES THE TAIL'S SHARE SCALE WITH TAIL HEIGHT, AND DOES IT SURVIVE THE PUSH (SLIPPITCH_GEOM=1)?
    ///
    /// Two questions the measurement raised, and both of them decide whether Polhamus' number is the Extra's number.
    ///  1. HEIGHT. A vortex shed from the body induces a net vertical velocity over the tail only because the tail sits
    ///     off the vortex's own line; a tail ON the line gets upwash on one half and downwash on the other and carries
    ///     no net load at all. So the effect must scale steeply with how far the horizontal tail stands above the
    ///     fuselage axis. RM L55E20b's configurations are 1950s research aeroplanes (fig. 10 is the X-5) with tails
    ///     well up the fin; the Extra's stab sits 0.19 m above its body axis on a body 0.53 m in radius — i.e. INSIDE
    ///     the body's own crossflow wake, not above it. If the sensitivity to height is steep, then −0.13 to −0.17 is
    ///     his geometry's number and not the Extra's, and the whole attribution has to be re-opened.
    ///  2. THE PUSH. The Endo is flown at α −40°, where this model's stabiliser is stalled negative and restoring
    ///     +18 kN·m. A term carried on the TAIL dies there (a stalled plate's dC_N/dα is near zero); a term carried on
    ///     the BODY does not. So the same ΔCm can help the manoeuvre or do nothing for it depending on where it acts,
    ///     and this prints the tail's share at α −40 as well as α 0.
    /// </summary>
    [Fact]
    public void Geometry()
    {
        if (Environment.GetEnvironmentVariable("SLIPPITCH_GEOM") == null) return;
        string[] ids = Environment.GetEnvironmentVariable("SLIPPITCH_IDS")?.Split(',') ?? new[] { "extra-300-like" };
        int[] betas = { 15, 25, 30, 45, 60 };
        // The best case the sweep found: the symmetry broken, the regime fade removed, a core of one body half-breadth.
        var best = BodyVortex.Knobs.Shipped;
        best.LowerVortexShare = 0.0; best.SymmetricLimitDeg = 90; best.FadeDeg = 1; best.CoreOverA = 1.0;
        foreach (string id in ids)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"==== {id}: the VORTEX's own share of ΔCm (vortex on − vortex off), POWER OFF, controls neutral,");
            sb.AppendLine("     with the symmetry broken / no regime fade / core 1.0a -- the most this mechanism can give.");
            foreach (int alpha in new[] { 0, -20, -40 })
            {
                sb.Append($"   α {alpha,4}  {"stab z (m)",12} |");
                foreach (int b in betas) sb.Append($"{"β" + b,9}");
                sb.AppendLine();
                // The stab is moved bodily in z. Its own strips carry the z, so the surface is rebuilt each time; nothing
                // else about the aeroplane changes, so the only thing the column can be showing is the height.
                foreach (double dz in new[] { 0.0, -0.20, -0.40, -0.70, -1.00 })
                {
                    AircraftConfig cfg = Load(id);
                    double z0 = 0;
                    foreach (SurfaceConfig sf in cfg.Surfaces)
                    {
                        if (!AeroModel.SystemKey(sf.Id).Equals("hStab", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (StripConfig st in sf.Strips) { st.Pos[2] += dz; z0 = st.Pos[2]; }
                    }
                    var tables = Aircraft.BuildAirfoilTables(cfg);
                    var buckets = BucketByLoadPoint(cfg);
                    var (S, c) = WingRef(cfg);
                    double qSc = 0.5 * Atmosphere.DensityAtAltitude(AltM) * V * V * S * c;
                    try
                    {
                        sb.Append($"         {z0,12:F2} |");
                        foreach (int b in betas)
                        {
                            // The VORTEX's OWN share: with it and without it, at the same α and β. ("Tail on − tail off"
                            // is the right comparison at α 0, where the stabiliser carries nothing anyway, but at α −40 it
                            // is swamped by the stalled stabiliser's own +18 kN·m collapsing as cos²β, which was in the
                            // model already and is not what is being added.)
                            BodyVortex.Diag = best;
                            double on = At(cfg, tables, buckets, alpha, b, throttle: 0).MTotal - At(cfg, tables, buckets, alpha, 0, throttle: 0).MTotal;
                            AeroModel.NoBodyVortexForDiagnostics = true;
                            double ofv = At(cfg, tables, buckets, alpha, b, throttle: 0).MTotal - At(cfg, tables, buckets, alpha, 0, throttle: 0).MTotal;
                            AeroModel.NoBodyVortexForDiagnostics = false;
                            sb.Append($"{(on - ofv) / qSc,9:F3}");
                        }
                        sb.AppendLine();
                    }
                    finally { BodyVortex.Diag = BodyVortex.Knobs.Shipped; AeroModel.NoBodyVortexForDiagnostics = false; }
                }
            }
            _out.WriteLine(sb.ToString());
        }
    }

    public static (double S, double C) WingRef(AircraftConfig cfg)
    {
        double s = cfg.Surfaces.Where(x => x.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)).SelectMany(x => x.Strips).Sum(x => x.Area);
        double b = AeroModel.WingSpan(cfg);
        // A biplane's reference is its TOTAL area on the LARGER span, as the configs' own notes do it.
        return (s, b > 0 ? s / b : 1.0);
    }

    /// <summary>Tail volume coefficient V_H = S_t·l_t/(S·c̄), printed because the only data this term can be checked
    /// against (RM L55E20b) is a wind-tunnel model with a different tail, and a tail-carried moment scales with it.</summary>
    public static double TailVolume(AircraftConfig cfg)
    {
        var (s, c) = WingRef(cfg);
        double st = 0, sx = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            if (!AeroModel.SystemKey(sf.Id).Equals("hStab", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (StripConfig t in sf.Strips) { st += t.Area; sx += t.Area * t.PosVec().X; }
        }
        if (st <= 0 || s <= 0) return double.NaN;
        double lt = cfg.Mass.CgVec().X - sx / st;        // + = the tail is behind the CG
        return st * lt / (s * c);
    }

    private static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    public static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id + ".json"));

    public static readonly int[] Alphas = { 0, -10, -20, -30, -40, 20 };
    public static readonly int[] Betas = { 0, 10, 15, 20, 25, 30, 40, 45, 60, 75, 90 };

    [Fact]
    public void Maps()
    {
        if (Environment.GetEnvironmentVariable("SLIPPITCH") == null) return;
        string[] ids = Environment.GetEnvironmentVariable("SLIPPITCH_IDS")?.Split(',') ?? new[] { "extra-300-like", "pitts-s2b-like" };
        bool noVortex = Environment.GetEnvironmentVariable("SLIPPITCH_NOVORTEX") != null;
        AeroModel.NoBodyVortexForDiagnostics = noVortex;
        try { foreach (string id in ids) _out.WriteLine(Report(Load(id), id, noVortex)); }
        finally { AeroModel.NoBodyVortexForDiagnostics = false; }
    }

    /// <summary>The surface mask that takes the HORIZONTAL TAIL off, which is the one comparison Polhamus' figures are
    /// drawn as: tail on against tail off. The fin stays on (he removed the horizontal tail, not the whole empennage).</summary>
    public static bool[] TailOffMask(AircraftConfig cfg)
    {
        var mask = new bool[cfg.Surfaces.Count];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = !AeroModel.SystemKey(cfg.Surfaces[i].Id).Equals("hStab", StringComparison.OrdinalIgnoreCase);
        return mask;
    }

    /// <summary>
    /// THE COMPARISON WITH THE DATA (SLIPPITCH_POLHAMUS=1). Polhamus, NACA RM L55E20b (1955), plots ΔCm against sideslip
    /// for a configuration with the horizontal tail ON and with it OFF, and the whole point of the report is the gap
    /// between the two curves: the tail-off body makes only a small nose-down moment, and the complete configuration
    /// makes several times as much, because a vortex the fuselage sheds in sideslip passes over the horizontal tail.
    /// So that is the comparison this prints, POWER OFF (a tunnel model has no thrust, and at β 90° a propeller at full
    /// throttle is at zero advance ratio and adds a thrust-line moment of its own that is not Cm(β) at all), and with
    /// the fitted turtledeck/cowl camber in and out so its share is visible.
    /// </summary>
    [Fact]
    public void AgainstPolhamus()
    {
        if (Environment.GetEnvironmentVariable("SLIPPITCH_POLHAMUS") == null) return;
        string[] ids = Environment.GetEnvironmentVariable("SLIPPITCH_IDS")?.Split(',') ?? new[] { "extra-300-like", "pitts-s2b-like" };
        foreach (string id in ids)
        {
            AircraftConfig cfg = Load(id);
            var tables = Aircraft.BuildAirfoilTables(cfg);
            var buckets = BucketByLoadPoint(cfg);
            var (S, c) = WingRef(cfg);
            double qSc = 0.5 * Atmosphere.DensityAtAltitude(AltM) * V * V * S * c;
            bool[] off = TailOffMask(cfg);
            var sb = new StringBuilder();
            sb.AppendLine($"==== {id}: ΔCm vs β, POWER OFF, controls neutral, α 0. qSc̄ {qSc:F0} N·m, V_H {TailVolume(cfg):F3}.");
            sb.AppendLine("     Data to beat (RM L55E20b figs. 9, 10): tail ON −0.13 to −0.17 by β 25–30°; tail OFF (body alone) −0.02 to −0.03.");
            sb.AppendLine($"   {"β",4} | {"tail ON",9} {"tail OFF",9} {"ON−OFF",9} | {"camber",9} {"ON no camb",10} {"OFF no camb",11} | {"novortex",9} {"vortex Δ",9}");
            double camberCl = cfg.Fuselage.Crossflow.SectionCamberCl, cowlCl = cfg.Fuselage.Crossflow.CowlCamberCl;
            foreach (int beta in Betas)
            {
                cfg.Fuselage.Crossflow.SectionCamberCl = camberCl; cfg.Fuselage.Crossflow.CowlCamberCl = cowlCl;
                double on = (At(cfg, tables, buckets, 0, beta, throttle: 0).MTotal - At(cfg, tables, buckets, 0, 0, throttle: 0).MTotal) / qSc;
                double offv = (At(cfg, tables, buckets, 0, beta, throttle: 0, surfaceMask: off).MTotal
                             - At(cfg, tables, buckets, 0, 0, throttle: 0, surfaceMask: off).MTotal) / qSc;
                cfg.Fuselage.Crossflow.SectionCamberCl = 0; cfg.Fuselage.Crossflow.CowlCamberCl = 0;
                double onNc = (At(cfg, tables, buckets, 0, beta, throttle: 0).MTotal - At(cfg, tables, buckets, 0, 0, throttle: 0).MTotal) / qSc;
                double offNc = (At(cfg, tables, buckets, 0, beta, throttle: 0, surfaceMask: off).MTotal
                              - At(cfg, tables, buckets, 0, 0, throttle: 0, surfaceMask: off).MTotal) / qSc;
                cfg.Fuselage.Crossflow.SectionCamberCl = camberCl; cfg.Fuselage.Crossflow.CowlCamberCl = cowlCl;
                AeroModel.NoBodyVortexForDiagnostics = true;
                double nov = (At(cfg, tables, buckets, 0, beta, throttle: 0).MTotal - At(cfg, tables, buckets, 0, 0, throttle: 0).MTotal) / qSc;
                AeroModel.NoBodyVortexForDiagnostics = false;
                sb.AppendLine($"   {beta,4} | {on,9:F3} {offv,9:F3} {on - offv,9:F3} | {on - onNc,9:F3} {onNc,10:F3} {offNc,11:F3} | {nov,9:F3} {on - nov,9:F3}");
            }
            cfg.Fuselage.Crossflow.SectionCamberCl = camberCl; cfg.Fuselage.Crossflow.CowlCamberCl = cowlCl;
            _out.WriteLine(sb.ToString());
        }
    }

    public static string Report(AircraftConfig cfg, string id, bool noVortex)
    {
        var tables = Aircraft.BuildAirfoilTables(cfg);
        var buckets = BucketByLoadPoint(cfg);
        var sb = new StringBuilder();
        double rho = Atmosphere.DensityAtAltitude(AltM), qbar = 0.5 * rho * V * V;
        var (S, c) = WingRef(cfg);
        double qSc = qbar * S * c;
        sb.AppendLine($"==== {id}{(noVortex ? "  [NO BODY VORTEX]" : "")}: V {V} m/s TAS, alt {AltM} m, rho {rho:F3}, qbar {qbar:F0} Pa, "
                    + $"S {S:F2} m², c̄ {c:F3} m, qSc̄ {qSc:F0} N·m, V_H {TailVolume(cfg):F3}, full power, controls neutral");

        // 1. THE HEADLINE: ΔCm(α, β), total, one matrix. A before/after is one diff of this block.
        sb.AppendLine("1. ΔCm = (M(β) − M(0))/qSc̄, total, controls neutral. NEGATIVE = nose-down, which is what sideslip should do.");
        sb.Append($"   {"α\\β",6} |");
        foreach (int beta in Betas) sb.Append($"{beta,8}");
        sb.AppendLine();
        foreach (int alpha in Alphas)
        {
            Map z = At(cfg, tables, buckets, alpha, 0);
            sb.Append($"   {alpha,6} |");
            foreach (int beta in Betas)
            {
                Map m = At(cfg, tables, buckets, alpha, beta);
                sb.Append($"{(m.MTotal - z.MTotal) / qSc,8:F3}");
            }
            sb.AppendLine();
        }

        // 2. BY SOURCE, so a change can be attributed and a double count can be seen.
        sb.AppendLine("2. ΔM_y by source (N·m, + nose up), and the total as ΔCm.");
        foreach (int alpha in Alphas)
        {
            Map z = At(cfg, tables, buckets, alpha, 0);
            sb.AppendLine($"   α {alpha,4}:  M(β=0) by source " + string.Join(" ", Src.Select((s, k) => $"{s} {z.M[k]:F0}")) + $"  total {z.MTotal:F0}");
            sb.AppendLine($"          {"β",4} | " + string.Join(" ", Src.Select(s => $"{s,9}")) + $" | {"Δtotal",9} | {"ΔCm",7}");
            foreach (int beta in Betas)
            {
                if (beta == 0) continue;
                Map m = At(cfg, tables, buckets, alpha, beta);
                sb.AppendLine($"          {beta,4} | " + string.Join(" ", m.M.Select((x, k) => $"{x - z.M[k],9:F0}"))
                            + $" | {m.MTotal - z.MTotal,9:F0} | {(m.MTotal - z.MTotal) / qSc,7:F3}");
            }
        }

        // 3. What the sideslip does with the stick FORWARD, because that is the Endo: full rudder, then full forward
        //    stick 0.3 s later. A nose-down coupling that only exists with the elevator neutral is no use in the
        //    manoeuvre Ty is flying.
        sb.AppendLine("3. ΔCm with the stick neutral / full forward (full down elevator), so the term can be read in the manoeuvre's own configuration.");
        sb.Append($"   {"α\\β",6} |");
        foreach (int beta in new[] { 15, 30, 45, 60 }) sb.Append($"{"δe0 " + beta,10}{"δeF " + beta,10}");
        sb.AppendLine();
        foreach (int alpha in Alphas)
        {
            Map z0 = At(cfg, tables, buckets, alpha, 0), zF = At(cfg, tables, buckets, alpha, 0, elevator: 1);
            sb.Append($"   {alpha,6} |");
            foreach (int beta in new[] { 15, 30, 45, 60 })
            {
                Map m0 = At(cfg, tables, buckets, alpha, beta), mF = At(cfg, tables, buckets, alpha, beta, elevator: 1);
                sb.Append($"{(m0.MTotal - z0.MTotal) / qSc,10:F3}{(mF.MTotal - zF.MTotal) / qSc,10:F3}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

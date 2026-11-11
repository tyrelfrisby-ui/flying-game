using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// RUDDER_ROLL=1: DOES <see cref="SpinAeroConfig.ControlSpoilerK"/> COUPLE RUDDER INTO ROLL AT HIGH ALPHA?
///
/// WHY IT WAS WRITTEN. Ty, flying the current build on 2026-11-10: <i>"Inverted spins roll out again and flat
/// spins do[n't] recover beggs mueller and are very difficult to recover at all so go ahead with the spoiler
/// work"</i>, and then the sharper one: <i>"The inverted spin with right rudder rolls to knife edge then
/// upright"</i>. That is a described TRAJECTORY — the aeroplane leaves the spin by ROLLING through 90° of bank and
/// arriving upright — so something makes a large roll moment in response to rudder at high negative alpha. It
/// pairs with his 2026-11-09 report that he recovered an UPRIGHT flat spin with PRO-SPIN aileron, which should
/// sustain one. Both are a roll/yaw cross-coupling with the wrong sign or size at high alpha.
///
/// Before the eleven plate-tailed types were given k 0.6 it had to be answered whether the spoiler term is that
/// coupling — in which case the sweep is the fix and the two reports are one bug — or whether it is separate work
/// that a config sweep must not be allowed to absorb silently.
///
/// WHAT THE CODE SAYS, BEFORE ANY MEASUREMENT. For a VERTICAL surface `AeroModel` builds
/// `dragDir = (dx, dy, 0)` and `liftDir = (dy, -dx, 0)` — both strictly in the body x-y plane — and the spoiler
/// force is `(liftDir·cos aChord − dragDir·sin aChord)·n`, so it has NO z component at all. A force (Fx, Fy, 0)
/// at the strip's station (x, y≈0, z) makes
///     roll  Mx = −z·Fy        yaw  Mz = x·Fy − y·Fx        pitch  My = z·Fx
/// so the rudder's roll and yaw moments are ONE force on TWO FIXED ARMS: the fin's height above the CG and its
/// station aft. k multiplies that force. It therefore cannot flip the sign of the coupling, and it cannot change
/// the roll:yaw ratio of its OWN contribution at all. What it CAN do is shift the ratio of the TOTAL, because the
/// spoiler force points along the fin's chord normal while the table force points relative to the local wind, and
/// those are different directions once aChord is large. That residual is what this probe measures, and it is the
/// only way k could be implicated.
///
/// WHAT IT MEASURED (the numbers are in the run; the conclusion is here so a reader need not re-run it):
/// changing k moves the rudder's roll and yaw accelerations together and leaves the roll/yaw RATIO essentially
/// where it was, upright and inverted alike, on the Pitts and on the types that gained k. **The spoiler is not
/// the roll-out.** And the report itself is about an aeroplane this change does not touch: the Pitts already
/// carried k 0.6 and onset 10 before Ty flew it, so a roll-out seen in that build exists AT the setting and is
/// neither caused nor cured by giving ten other types the same number. The pro-spin-aileron recovery is a
/// different term again — `flapSep`, the aileron's chordwise flap-normal force, and
/// <see cref="SpinAeroConfig.RollDueToSideslip"/> — and this change touches neither. The coupling is its own work
/// item, and NO k was fitted to either report: all eleven types carry the Pitts' 0.6 unchanged.
/// </summary>
[Collection("WorldTerrainActive")]
public class RudderRollCouplingProbe
{
    private readonly ITestOutputHelper _o;
    public RudderRollCouplingProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("RUDDER_ROLL") == null) return;
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;
        try
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
            string root = d.FullName;
            foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "decathlon-8kcab-like",
                                          "great-lakes-like", "glider-2-33-like", "stearman-pt17-like" })
            {
                var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", id + ".json"));
                var tables = Aircraft.BuildAirfoilTables(cfg);
                cfg.SpinAero ??= new SpinAeroConfig();
                double kWas = cfg.SpinAero.ControlSpoilerK;
                double ixx = cfg.Mass.Inertia.Ixx, izz = cfg.Mass.Inertia.Izz;
                double rud = cfg.Controls.Rudder.MaxDeflRad;
                Vec3 cg = cfg.Mass.CgVec();

                // The fin's own arms, area-weighted over every vertical strip: the two numbers that fix the
                // roll:yaw ratio of ANY side force the rudder makes, whatever makes it.
                double a = 0, az = 0, ax = 0;
                foreach (SurfaceConfig sf in cfg.Surfaces)
                {
                    if (!sf.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                        && !sf.Id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (StripConfig st in sf.Strips)
                    { a += st.Area; az += st.Area * (st.Pos[2] - cg.Z); ax += st.Area * (st.Pos[0] - cg.X); }
                }
                double zArm = a > 0 ? az / a : 0, xArm = a > 0 ? ax / a : 0;

                _o.WriteLine($"{id,-22} k {kWas:F1} as shipped, rudder +/-{rud * 57.2958:F1} deg, " +
                             $"Ixx {ixx:F0} Izz {izz:F0}");
                _o.WriteLine($"{"",24}fin area {a:F3} m2, area-weighted arms: {zArm:+0.000;-0.000} m in z " +
                             $"(negative = ABOVE the CG), {xArm:+0.000;-0.000} m in x (negative = AFT)");
                _o.WriteLine($"{"",24}=> a side force makes |roll| / |yaw| moment of {Math.Abs(zArm / xArm):F3} " +
                             $"and, per inertia, |p_dot| / |r_dot| of {Math.Abs(zArm / xArm) * izz / ixx:F3}, " +
                             "FOR ANY k: one force, two fixed arms");
                // THREE CONDITIONS, because the first one taught something on its own: at ZERO SIDESLIP AND NO
                // RATES the two columns came out bit-identical at every alpha on every type, and the reason is
                // that a VERTICAL strip's `alphaBase` is `atan2(v, u)` — its own IN-PLANE angle, i.e. the local
                // sideslip — not the body alpha. With no yaw and no rotation that angle is zero, so the ramp
                // never opens however broadside the aeroplane is. Body alpha alone does not reach the rudder's
                // spoiler at all; it takes sideslip at the fin, which in a spin comes from the ROTATION.
                //   "static"  : zero sideslip, no rates — the condition that proved the above
                //   "beta 20" : 20 deg of steady sideslip
                //   "spinning": the rotation a developed spin has, about the VELOCITY vector (which is what a
                //               steady spin is), at 4.5 rad/s — the condition the owner's report is in. Both
                //               signs, because he described an inverted spin on RIGHT rudder.
                foreach (string cond in new[] { "static", "beta 20", "spin +4.5", "spin -4.5" })
                {
                    _o.WriteLine($"{"",24}{cond,-10}  alpha |     k 0: p_dot   r_dot   |p/r|  |   k 0.6: p_dot   r_dot   |p/r|");
                    foreach (int deg in new[] { -90, -80, -70, -60, -45, -30, 30, 45, 60, 70, 80, 90 })
                    {
                        double al = deg * Math.PI / 180;
                        double beta = cond == "beta 20" ? 20.0 * Math.PI / 180 : 0.0;
                        // Velocity at this alpha and sideslip, 30 m/s.
                        Vec3 v = new(30 * Math.Cos(al) * Math.Cos(beta), 30 * Math.Sin(beta), 30 * Math.Sin(al) * Math.Cos(beta));
                        double spin = cond.StartsWith("spin") ? (cond.EndsWith("-4.5") ? -4.5 : 4.5) : 0.0;
                        Vec3 om = spin == 0.0 ? Vec3.Zero : (v / v.Length) * spin;   // a steady spin rotates about its velocity
                        (double p, double r) At(double k)
                        {
                            cfg.SpinAero!.ControlSpoilerK = k;
                            // FULL RIGHT RUDDER against neutral, so only what the pedal commands is measured.
                            Vec3 m1 = AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, 1.0,
                                                        new ControlDeflections(0, 0, rud, 0)).Moment;
                            Vec3 m0 = AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, 1.0,
                                                        new ControlDeflections(0, 0, 0, 0)).Moment;
                            return ((m1.X - m0.X) / ixx * 57.2958, (m1.Z - m0.Z) / izz * 57.2958);
                        }
                        var lo = At(0.0); var hi = At(0.6);
                        // ROLL DAMPING AT THE SAME ATTITUDE, controls neutral, at the shipped k. If dL/dp is near
                        // zero or POSITIVE then any roll input rolls the aeroplane out and the owner's trajectory
                        // needs no wrong-signed coupling to explain it — so this is the number that either names
                        // the cause or rules the whole class out. Units 1/s: a divergence rate if positive.
                        cfg.SpinAero!.ControlSpoilerK = kWas;
                        double Lp;
                        {
                            double h = 0.05;
                            Vec3 ma = AeroModel.Compute(cfg, tables, v, om + new Vec3(h, 0, 0), Vec3.Zero, 1.0,
                                                        new ControlDeflections(0, 0, 0, 0)).Moment;
                            Vec3 mb = AeroModel.Compute(cfg, tables, v, om - new Vec3(h, 0, 0), Vec3.Zero, 1.0,
                                                        new ControlDeflections(0, 0, 0, 0)).Moment;
                            Lp = (ma.X - mb.X) / (2 * h) / ixx;
                        }
                        _o.WriteLine($"{"",24}{"",10}{deg,7} | {lo.p,13:F2} {lo.r,7:F2} {(Math.Abs(lo.r) > 1e-9 ? Math.Abs(lo.p / lo.r) : double.NaN),7:F3}" +
                                     $"  | {hi.p,14:F2} {hi.r,7:F2} {(Math.Abs(hi.r) > 1e-9 ? Math.Abs(hi.p / hi.r) : double.NaN),7:F3}" +
                                     $" | dL/dp {Lp,7:F3} 1/s{(Lp > 0 ? " ANTI-DAMPED" : "")}" +
                                     $"{(Math.Abs(hi.p - lo.p) < 1e-12 && Math.Abs(hi.r - lo.r) < 1e-12 ? "   (k inert here)" : "")}");
                    }
                }
                cfg.SpinAero.ControlSpoilerK = kWas;
                _o.WriteLine("");
            }
            _o.WriteLine("p_dot and r_dot are deg/s^2 from FULL right rudder at 30 m/s, zero sideslip, no rates, " +
                         "rho 1.0, against controls neutral. |p/r| is the ratio the owner's 'rolls to knife edge' " +
                         "would have to be large in. If it barely moves between the two columns, k is not the " +
                         "coupling.");
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }
}

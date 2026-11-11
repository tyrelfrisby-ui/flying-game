using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (HIGH_START_DRIFT=1): how far the wings drift, hands off, when the in-air start is put well above the
/// altitude the airframe rig was solved at — and what the rig's OWN solution at the start altitude says the aileron
/// preset should be.
///
/// WHY THIS EXISTS. <see cref="RigSolver"/> solves each type's aileron rig and fin offset ONCE, at
/// <see cref="RigSolver.RigAltitudeM"/> (4,000 ft ISA), and <c>RigTool</c> writes those two angles into the config as
/// FIXED GEOMETRY. An in-air start then calls <see cref="RigSolver.SolveLevel"/>, which holds that geometry and solves
/// only speed, α, elevator and the sliver of bank — so the ROLLING moment the fixed rig leaves at any other height is
/// not trimmed out by anything, and with the stick centred the aeroplane rolls off. lane/claude-start-altitude measured
/// this on 2026-11-03 and reported, hands off for 10 s at 10,000 ft AGL over KMYL: Gee Bee 12.7°, Glasair 9.9°,
/// Cassutt 8.6°, Pitts 5.6°, Great Lakes 1.7°, Cub 0.5°.
///
/// THE MEASUREMENT HAD TO BE REDONE, not quoted. Since that lane measured it, all 26 fins moved to the `fin-lowAR`
/// section and twelve types got flat-plate horizontal tails. Both of those change exactly the surfaces whose lift the
/// lateral rig balances, so last week's numbers are not evidence about this week's aeroplanes.
///
/// STILL AIR, NO TERRAIN, exactly as <c>RigTests</c> flies it: "the rig is a property of the airframe, not of the
/// weather". The island's ridge lift, thermals and terrain-driven field roll an aeroplane several degrees in ten
/// seconds all by themselves, which has nothing to do with the rig and would swamp the thing being measured. The
/// island's HEIGHTS are still used — the ground under KMYL, the highest authored in-air start — because the question
/// is about the heights a player can actually pick.
///
/// WHAT IT PRINTS, per type and height: the rolling- and yawing-moment residual the fixed rig leaves (L, N, as
/// fractions of W·m), the bank change after 10 s hands off, and then the rig RE-SOLVED at that height. The preset is
/// the DIFFERENCE between the aileron rig the solver wants at the start height and the one it wants at the rig
/// height — not the difference from the config's stored angle. That distinction is the whole design: the stored angle
/// is a hand-copied literal from an earlier RigTool run and is a few hundredths of a degree stale on most types, so
/// subtracting it would leave a preset that is NOT zero at the rig altitude, which is precisely the case Ty requires
/// to stay centred. A difference of two solver calls on the same config is identically zero there, whatever the
/// config says.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class HighStartDriftProbe
{
    private const double D = 180.0 / Math.PI, FtToM = 0.3048;
    private readonly ITestOutputHelper _out;
    public HighStartDriftProbe(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>Still air, no terrain, no ridge, no thermals — RigTests.StillAir, for the same reason.</summary>
    private static IDisposable StillAir()
    {
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence;
        var terrain = WorldTerrain.Active; var ridge = Atmosphere.ActiveRidge; var thermals = Atmosphere.Thermals.ToList();
        double isa = Atmosphere.IsaDeviationK;
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; WorldTerrain.Active = null;
        Atmosphere.ActiveRidge = null; Atmosphere.Thermals.Clear(); Atmosphere.IsaDeviationK = 0;
        return new Restore(() =>
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb; WorldTerrain.Active = terrain;
            Atmosphere.ActiveRidge = ridge; Atmosphere.Thermals.AddRange(thermals); Atmosphere.IsaDeviationK = isa;
        });
    }

    /// <summary>The types the lane reported on, fast end first, plus the Extra and the C-172 for the slow end.</summary>
    private static readonly string[] Types =
    {
        "geebee-r2-like", "glasair3-like", "cassutt-f1-like", "p51d-like", "pitts-s2b-like",
        "extra-300-like", "great-lakes-like", "j3-cub-like", "c172-like",
    };

    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("HIGH_START_COST") != null) { using (StillAir()) Cost(); return; }
        if (Environment.GetEnvironmentVariable("HIGH_START_DRIFT") == null) return;
        double ground;
        {
            var island = new WorldTerrain(); WorldTerrain.Active = island;
            try
            {
                IslandSites.Start st = IslandRunways.Authored("KMYL", "InTheAir")!.Value;
                ground = WorldTerrain.GroundHeightAt(st.X, st.Y);
                _out.WriteLine($"KMYL in-air start: ground {ground / FtToM:N0} ft MSL, authored {st.AltitudeM / FtToM:N0} ft MSL");
            }
            finally { WorldTerrain.Active = null; }
        }
        using var still = StillAir();
        double[] aglFt = { 3000, 6000, 10000 };
        foreach (string id in Types)
        {
            AircraftConfig c = Load(id);
            RigSolver.Result atRig = RigSolver.SolveRig(c, RigSolver.RigAltitudeM);
            _out.WriteLine($"---- {id}: config aileron {c.Rig.AileronRad * D:F4}° fin {c.Rig.FinOffsetRad * D:F4}°; "
                         + $"solver at 4,000 ft wants aileron {atRig.AileronRigRad * D:F4}° fin {atRig.FinOffsetRad * D:F4}° "
                         + $"(converged {atRig.Converged}, stale by {(atRig.AileronRigRad - c.Rig.AileronRad) * D:F4}°)");
            Line(c, atRig, RigSolver.RigAltitudeM, "the rig altitude, 4,000 ft MSL");
            foreach (double ft in aglFt)
                Line(c, atRig, ground + ft * FtToM, $"{ft:N0} ft AGL over KMYL = {(ground + ft * FtToM) / FtToM:N0} ft MSL");
        }
        Cost();
    }

    /// <summary>
    /// WHAT THE PRESET COSTS AT SPAWN, because it is not free and a spawn happens on a phone.
    /// <see cref="RigSolver.AileronTrimForAltitude"/> runs a six-unknown Newton solve at the start altitude; the
    /// rig-altitude solve beside it is cached per config, and the rig-altitude case short-circuits before either. The
    /// number that matters is the SECOND call onward, because <c>AirStart.ComputeRandom</c> can call Compute up to
    /// five times while it hunts for a separated multiplayer start.
    /// </summary>
    private void Cost()
    {
        AircraftConfig c = Load("extra-300-like");
        double alt = 14699 * FtToM;
        // SolveLevel is the Newton solve a spawn ALREADY pays for (AirStart.Compute calls it to find the trimmed
        // speed, alpha and elevator). It is the only honest yardstick here: the machine is shared with other agents,
        // so an absolute millisecond figure is noise, but the RATIO of the preset's solve to the one the spawn was
        // already doing is contention-independent and is what decides whether this is affordable.
        RigSolver.SolveLevel(c, RigSolver.RigThrottle, alt);                     // JIT
        RigSolver.AileronTrimForAltitude(c, alt, out _);                         // JIT + warm the per-config cache
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) RigSolver.SolveLevel(c, RigSolver.RigThrottle, alt + i);
        double level = sw.Elapsed.TotalMilliseconds / 5.0;
        sw.Restart();
        for (int i = 0; i < 5; i++) RigSolver.AileronTrimForAltitude(c, alt + i, out _);
        double preset = sw.Elapsed.TotalMilliseconds / 5.0;
        // THE PRODUCTION PATH: AirStart.Compute already has the SolveLevel result and passes it in, so the preset
        // costs only the 1-D aileron find on top of a solve the spawn was doing anyway. This is the number that
        // decides whether a spawn hitches.
        RigSolver.Result lvl = RigSolver.SolveLevel(c, RigSolver.RigThrottle, alt);
        sw.Restart();
        for (int i = 0; i < 5; i++) RigSolver.AileronTrimForAltitude(c, lvl, out _);
        double onTop = sw.Elapsed.TotalMilliseconds / 5.0;
        sw.Restart();
        for (int i = 0; i < 5; i++) RigSolver.AileronTrimForAltitude(c, RigSolver.RigAltitudeM, out _);
        double atRigAlt = sw.Elapsed.TotalMilliseconds / 5.0;
        _out.WriteLine($"cost: SolveLevel (the solve a spawn already does) {level:F1} ms. "
                     + $"The preset ON TOP of it, the production path with the level state passed in: {onTop:F1} ms "
                     + $"= {onTop / level:F3}x. Standalone, solving its own level state (tests and probes): "
                     + $"{preset:F1} ms = {preset / level:F2}x. At the rig altitude {atRigAlt:F4} ms (short-circuited). "
                     + "Absolutes are contention-dependent; the ratios are not.");
    }

    private void Line(AircraftConfig c, RigSolver.Result atRig, double altM, string label)
    {
        RigSolver.Result lvl = RigSolver.SolveLevel(c, RigSolver.RigThrottle, altM);
        double[] r = lvl.Residual;
        double bank0 = BankChange(c, lvl, 0.0);

        RigSolver.Result here = RigSolver.SolveRig(c, altM);
        bool ok = here.Converged && atRig.Converged && RigSolver.CanRig(c);
        // THE SHIPPED PRESET, through the production path, not a local re-derivation: whatever this probe prints is
        // what an in-air start at this height actually holds.
        double preset = RigSolver.AileronTrimForAltitude(c, altM, out string? why);
        double dFin = ok ? here.FinOffsetRad - atRig.FinOffsetRad : double.NaN;
        double bank1 = BankChange(c, lvl, preset);
        if (why != null) _out.WriteLine($"     ({why})");

        _out.WriteLine($"     {label,-48} {lvl.TasMs * 1.943844,6:F1} KTAS  L {r[3],9:E2} N {r[5],9:E2}  "
                     + $"bank@10s {bank0 * D,7:F2}°  preset {preset * D,8:F4}°  (dFin {dFin * D,8:F4}°)  "
                     + $"bank@10s with preset {bank1 * D,7:F2}°");
    }

    /// <summary>Peak |bank change| over 10 s hands off, stick fixed, `aileronRad` held as a preset on the aileron.
    /// A change, not an absolute: the solved state already starts with the rig's sliver of bank on it.</summary>
    private static double BankChange(AircraftConfig c, RigSolver.Result r, double aileronRad)
    {
        Quat att = RigSolver.Attitude(r, 0.0);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero),
                              new ControlDeflections(aileronRad, r.ElevatorRad, 0, 0));
        if (c.RetractableGear) ac.SetGear(false, immediate: true);
        var hands = new ControlInputs(Aircraft.StickForDeflection(aileronRad, c.Controls.Aileron),
                                      Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator), 0,
                                      c.Propulsion is null ? 0.0 : 1.0 - 2.0 * RigSolver.RigThrottle);
        double b0 = Bank(ac.State.Attitude), maxB = 0;
        const double dt = SimLoop.DefaultFixedDtSec;
        for (double t = 0; t < 10.0 - 1e-9; t += dt)
        {
            ac.Step(hands, dt);
            maxB = Math.Max(maxB, Math.Abs(Bank(ac.State.Attitude) - b0));
        }
        return maxB;
    }

    private static double Bank(Quat q) => Math.Asin(Math.Clamp(q.Rotate(new Vec3(0, 1, 0)).Z, -1, 1));
}

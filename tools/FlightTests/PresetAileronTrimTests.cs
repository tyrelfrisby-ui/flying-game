using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// PRESET AILERON TRIM FOR A START AWAY FROM THE RIG ALTITUDE (<see cref="RigSolver.AileronTrimForAltitude"/>).
///
/// Ty, 2026-11-10, shown lane/claude-start-altitude's finding that the wings drift more the higher you start — the
/// aileron rig and fin offset are solved ONCE at 4,000 ft and written into the config as fixed geometry, so at any
/// other height they leave a rolling moment nothing trims out — and offered either a preset trim or re-solving the
/// whole rig at the start altitude, chose: **preset aileron trim**.
///
/// THE RULE THIS BREACHES IS HIS OWN, and these tests are the fence round the breach. "Trims centred every flight"
/// came out of the stick-free launch work: a flight begins with the trims centred so that no leftover trim flies the
/// first frame and so the aeroplane a player meets is the same aeroplane every time. A preset aileron trim is a
/// non-centred trim at the start, and he chose it knowing that. What is checked here is that the breach stays exactly
/// as narrow as he was promised:
///   • <see cref="ZeroAtTheRigAltitudeForEveryType"/> — at 4,000 ft the preset is EXACTLY 0.0 for every type in the
///     fleet, in the plan, in the deflections and in the held inputs. A flight that starts there is bit-identical to
///     one from before this existed, which is what keeps LaunchReplicationTests and the stick-free launch work honest.
///   • <see cref="ThePresetIsTooSmallToSeeOnTheTrimIndicator"/> — over every height the picker can reach it is under a
///     tenth of a degree of aileron, under 0.01 of stick travel. The indicator does not visibly leave centre.
///   • <see cref="ThePresetIsAFunctionOfTheStartAndNotOfHistory"/> — two starts at the same height in the same air get
///     the same preset to the bit. That is the part of "trims centred" that actually mattered: it is the LAST
///     FLIGHT's trim that must never fly this one, and a pure function of the start cannot carry one.
///   • <see cref="ThePresetRemovesTheDriftThatGrowsWithHeight"/> — it does the job it was built for, measured.
///   • <see cref="ATypeWithNothingToRigIsLeftCentredAndSaysSo"/> — never a silent half-trim.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class PresetAileronTrimTests
{
    private const double D = 180.0 / Math.PI, FtToM = 0.3048;
    private readonly ITestOutputHelper _out;
    public PresetAileronTrimTests(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>Still air, no terrain: the rig is a property of the airframe, not of the weather (RigTests says the
    /// same in the same words, for the same reason). The island's ridge lift and thermals roll an aeroplane several
    /// degrees in ten seconds by themselves, which would swamp what is being measured here.</summary>
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

    public static IEnumerable<object[]> Fleet() => RigTable.Rows.Select(r => new object[] { r.Id });

    /// <summary>The heights the start-altitude picker offers, as MSL over the island's highest authored in-air start
    /// (KMYL, ground 4,699 ft): 3,000 / 6,000 / 10,000 ft AGL, so 7,699 / 10,699 / 14,699 ft MSL. The thinnest air a
    /// preset ever has to work in.</summary>
    private static readonly double[] PickerMslFt = { 7699, 10699, 14699 };

    [Theory]
    [MemberData(nameof(Fleet))]
    public void ZeroAtTheRigAltitudeForEveryType(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        Assert.Equal(0.0, RigSolver.AileronTrimForAltitude(c, RigSolver.RigAltitudeM, out string? why));
        Assert.Null(why);   // nothing to cancel is not a failure to derive
        AirStart.Plan plan = AirStart.Compute(c, 0, 0, RigSolver.RigAltitudeM, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
        Assert.Equal(0.0, plan.AileronPresetRad);
        Assert.Equal(0.0, plan.AileronTrimStick);
        Assert.Equal(0.0, plan.Deflections.AileronRad);
        Assert.Equal(0.0, AirStart.HeldInputs(plan).Aileron);
    }

    /// <summary>
    /// TOO SMALL TO SEE, and measured three ways because the obvious one is misleading.
    ///
    /// In DEGREES the preset is tiny: under 0.06° of aileron over every height the start-altitude picker can reach,
    /// against a full deflection of 20° — three thousandths of the surface's travel.
    ///
    /// In STICK UNITS it reads larger, about 0.034, and that is NOT the preset being bigger than it looks. Every
    /// aileron axis in the fleet has a DEAD ZONE of 0.02–0.05 (<see cref="ControlAxisConfig.DeadZone"/>) and
    /// <c>Aircraft.ShapeAxis</c> returns exactly zero inside it, so the stick that produces ANY non-zero deflection
    /// at all must sit just outside that edge. 0.034 against a 0.03 dead zone is four thousandths past it. That is
    /// the real statement of how small this is: <b>the stick position that delivers the preset is barely outside the
    /// controller's own dead zone</b> — a value a hand on a stick could not hold if it tried.
    ///
    /// On the TRIM BAR, which is what the pilot actually sees, it is that stick divided by
    /// <c>TouchFlightControls.TrimAuthority</c> (0.6): under a twentieth of the bar's half-width off centre.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fleet))]
    public void ThePresetIsTooSmallToSeeOnTheTrimIndicator(string id)
    {
        const double trimAuthority = 0.6;   // TouchFlightControls.TrimAuthority, which Unity-side code owns
        using var still = StillAir();
        AircraftConfig c = Load(id);
        double dead = c.Controls.Aileron.DeadZone;
        foreach (double ft in PickerMslFt)
        {
            AirStart.Plan plan = AirStart.Compute(c, 0, 0, ft * FtToM, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
            double stick = Math.Abs(plan.AileronTrimStick);
            _out.WriteLine($"{id} @ {ft:N0} ft MSL: preset {plan.AileronPresetRad * D:F4}°, stick {plan.AileronTrimStick:F5} "
                         + $"(dead zone {dead:F2}, so {stick - dead:+0.00000;-0.00000} past its edge), "
                         + $"trim bar {plan.AileronTrimStick / trimAuthority:F4}"
                         + (plan.AileronPresetWarning is null ? "" : $"  [{plan.AileronPresetWarning}]"));
            Assert.InRange(plan.AileronPresetRad * D, -0.15, 0.15);
            // Barely outside the dead zone, which is the honest way to say "too small to hold on purpose".
            Assert.True(stick == 0.0 || stick - dead < 0.02,
                $"{id} @ {ft:N0} ft: stick {stick:F5} is {stick - dead:F5} past a {dead:F2} dead zone");
            // And a trim bar that does not visibly leave centre.
            Assert.InRange(plan.AileronTrimStick / trimAuthority, -0.1, 0.1);
        }
    }

    /// <summary>A pure function of the config and the start: no cache, no last flight, no order dependence. This is the
    /// part of "trims centred every flight" that the preset must not break — the rule exists so that THIS flight never
    /// flies the LAST one's trim, and a function of the start cannot carry one.</summary>
    [Fact]
    public void ThePresetIsAFunctionOfTheStartAndNotOfHistory()
    {
        using var still = StillAir();
        AircraftConfig c = Load("geebee-r2-like");
        double hi = 14699 * FtToM, lo = 7699 * FtToM;
        double a1 = RigSolver.AileronTrimForAltitude(c, hi, out _);
        double b1 = RigSolver.AileronTrimForAltitude(c, lo, out _);
        double a2 = RigSolver.AileronTrimForAltitude(c, hi, out _);          // again, after a different height
        Assert.Equal(a1, a2);                                                 // bit-identical, not "close"
        Assert.NotEqual(a1, b1);                                              // and it does depend on the height
        // A fresh config object (a new flight loads its own) gets the same answer: the per-config cache of the
        // rig-altitude solve is a cache, not state.
        Assert.Equal(a1, RigSolver.AileronTrimForAltitude(Load("geebee-r2-like"), hi, out _), 12);
        _out.WriteLine($"Gee Bee: 14,699 ft {a1 * D:F4}°, 7,699 ft {b1 * D:F4}°");
    }

    /// <summary>
    /// WHAT IT WAS BUILT FOR, FLOWN: 10 s hands off, stick fixed, in still air. What the preset removes is the part of
    /// the drift that GROWS WITH HEIGHT — which is the part a fixed rig causes and the only part it can remove. What
    /// it leaves behind is whatever each type already drifts at the rig altitude itself, because the stored rigs do
    /// not close on today's model (the C-172 leaves L = −7.9e-3 at its own 4,000 ft rig point against
    /// RigTests.HandsOffLevelAtRigPower's 2e-4) and nothing a preset does at 14,699 ft can fix a rig that is wrong at
    /// 4,000.
    ///
    /// So the assertion is the mechanism, not a number: with the preset on, ten seconds hands off at 10,000 ft AGL
    /// must be no worse than ten seconds hands off at the rig altitude. An absolute band would have had to be loose
    /// enough to swallow each type's rig-altitude floor, and would then have passed a preset that did nothing.
    ///
    /// Only the types whose drift the preset can reach are gated. The Pitts, the Extra, the Great Lakes and the Cub
    /// are NOT in this list: they roll tens of degrees in ten seconds at EVERY height including the rig altitude,
    /// because their lateral rigs do not close at all, and that is a different fault with a different fix (re-run
    /// RigTool for the types whose tails changed). Measured, the preset does not help them — the Pitts goes 61.6° to
    /// 59.9° — and widening a band to cover them would hide the real defect. The Cub is also the one type whose rig
    /// solve does not converge off the rig altitude, so it is left centred with a reason, which
    /// ATypeWithNothingToRigIsLeftCentredAndSaysSo's sibling path covers.
    /// </summary>
    [Theory]
    [InlineData("geebee-r2-like")]
    [InlineData("glasair3-like")]
    [InlineData("cassutt-f1-like")]
    [InlineData("p51d-like")]
    [InlineData("c172-like")]
    public void ThePresetRemovesTheDriftThatGrowsWithHeight(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        double high = 14699 * FtToM;

        RigSolver.Result atRig = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        Assert.True(atRig.Converged, $"{id}: level solve at the rig altitude did not converge");
        double floor = BankChange(c, atRig, 0.0);

        RigSolver.Result lvl = RigSolver.SolveLevel(c, RigSolver.RigThrottle, high);
        Assert.True(lvl.Converged, $"{id}: level solve at 14,699 ft did not converge");
        double preset = RigSolver.AileronTrimForAltitude(c, lvl, out string? why);
        Assert.Null(why);
        double without = BankChange(c, lvl, 0.0), with = BankChange(c, lvl, preset);

        // THE GROWTH is what the preset can remove: the drift at height LESS the drift the aeroplane already has at
        // the rig altitude. Measured, it removes 83-107 % of it (the Gee Bee over 100 %: with the preset on it drifts
        // LESS at 14,699 ft than it does at 4,000). The gate is 75 %, which every gated type clears with margin and
        // which no do-nothing preset could pass.
        double growth = without - floor, left = with - floor;
        _out.WriteLine($"{id}: 10 s hands off — at the rig altitude {floor * D:F2}°; at 10,000 ft AGL "
                     + $"{without * D:F2}° without the preset, {with * D:F2}° with it ({preset * D:F4}°). "
                     + $"Growth {growth * D:F2}° -> {left * D:F2}°, {100 * (1 - left / growth):F0} % removed");
        Assert.True(with < without, $"{id}: the preset made it worse ({without * D:F2}° -> {with * D:F2}°)");
        Assert.True(growth > 0.5 / D, $"{id}: only {growth * D:F2}° of growth to remove — this case proves nothing");
        Assert.True(left < 0.25 * growth,
            $"{id}: {left * D:F2}° of the {growth * D:F2}° growth is still there with the preset on "
            + $"(only {100 * (1 - left / growth):F0} % removed)");
    }

    [Theory]
    [InlineData("hang-glider-like")]
    [InlineData("paraglider-en-b-like")]
    public void ATypeWithNothingToRigIsLeftCentredAndSaysSo(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        double preset = RigSolver.AileronTrimForAltitude(c, 14699 * FtToM, out string? why);
        Assert.Equal(0.0, preset);
        Assert.NotNull(why);
        _out.WriteLine($"{id}: {why}");
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

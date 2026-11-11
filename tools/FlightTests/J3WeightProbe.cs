using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// J3_WEIGHT=1: what the two J-3s weigh, and what that weight flies like. Prints only, asserts nothing — the pins
/// for all of this live in RigTests and NewAircraftPerformanceTests; this is the instrument that was used to decide
/// the 2026-11-11 empty-weight change and to measure what it did.
///
/// WHAT IT MEASURED (2026-11-11). Both configs had been authored at their PLACARDS — 553.0 kg = 1,220 lb and
/// 589.7 kg = 1,300 lb — which left their DERIVED empty weights at 1,030 and 1,110 lb against a published 680.
/// Authoring loading.emptyMassKg alone would have put the sim at 870 lb while RigSolver and PracticeScenario stayed
/// on mass.massKg at 1,220: a 29 % rig/sim mass mismatch, which the "rig/sim mismatch" line here exists to catch.
/// mass.massKg moved with it. Empty weights are now 680 lb (Piper Service Memo 41 for the J3C-65) and 805 lb (that
/// plus the 125 lb seaplane exchange), each + the fleet's 86 kg one-pilot-in-the-rear-seat reference.
///
///                         j3-cub-like (wheels)            j3-cub-floats-like
///   authored mass      553.0  -> 394.4 kg  (-28.7 %)   589.7  -> 451.1 kg  (-23.5 %)
///   empty weight      1029.6  -> 680.0 lb             1110.5  -> 805.0 lb
///   Vso                 43.2  -> 36.5 mph  (-15.5 %)     43.8  -> 38.3 mph  (-12.6 %)
///   ROC at Vy            537  -> 991 fpm   (+85 %)        506  -> 846 fpm   (+67 %)
///   cruise 75 %         80.7  -> 83.2 mph  ( +3.1 %)      83.8 -> 85.5 mph  ( +2.0 %)
///   rig point (75 %)    56.3  -> 62.6 KTAS                59.6 -> 65.6 KTAS
///
/// So the weight is worth a lot of CLIMB, a useful slice of stall speed, and little at cruise — which is what a
/// power-limited aeroplane does, and worth knowing before the same change is made to the other 33 types. The owner's
/// "the j3 is noticibly slower" (2026-11-09) is answered better by the rig point than by cruise75: at 75 % the
/// indicated speed goes 61.1 -> 67.9 mph IAS and the TAS 64.8 -> 72.0 mph, against a book cruise of about 73. The
/// two numbers disagree because PracticeScenario.EstimateCruise75 uses a flat propeller efficiency while RigSolver
/// flies the blade-element propeller; the rig number is the better one.
///
/// WHAT STILL READS HIGH: Vso. Book is 38 mph at the 1,220 lb gross, so a 870 lb Cub should stall near 32, not 36.5.
/// That is the known EstimateVso bias for wings whose CLmax is above 14 deg body alpha (its own comment measures the
/// 172 at +13.4 %, the Extra +12.0 %), not something this change introduced.
///
/// ONE ANOMALY LEFT, and it is NOT from this change: the floats J-3 reads a FASTER 75 % cruise than the wheels one
/// (86.2 against 82.4 mph), which floats cannot do. It read the same way before the change (83.8 against 80.7), so
/// it is a standing fault in the float drag or in EstimateCruise75's 1 m/s scan, not a weight error.
/// </summary>
[Collection("WorldTerrainActive")]
public class J3WeightProbe
{
    private readonly ITestOutputHelper _o;
    public J3WeightProbe(ITestOutputHelper o) { _o = o; }
    private const double Lb = 1.0 / 0.45359237;

    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void WeightAndPerformance(string id)
    {
        if (Environment.GetEnvironmentVariable("J3_WEIGHT") != "1") return;
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var load = new AircraftLoad(c);
        double refKg = 0; foreach (AircraftLoad.Slot s in load.Slots) refKg += s.Config.ReferenceKg;
        _o.WriteLine($"{id}");
        _o.WriteLine($"  authored mass.massKg        {c.Mass.MassKg,8:F2} kg = {c.Mass.MassKg * Lb,7:F1} lb   <- RigSolver and PracticeScenario fly THIS");
        _o.WriteLine($"  reference load              {refKg,8:F2} kg = {refKg * Lb,7:F1} lb   (seats {load.OccupantsKg:F2}, fuel {load.FuelKg:F2}, bags {load.BaggageKg:F2})");
        _o.WriteLine($"  EMPTY                       {load.EmptyMassKg,8:F2} kg = {load.EmptyMassKg * Lb,7:F1} lb   degenerate={load.Degenerate}");
        _o.WriteLine($"  loaded (what the sim flies) {load.TotalMassKg,8:F2} kg = {load.TotalMassKg * Lb,7:F1} lb");
        _o.WriteLine($"  rig/sim mismatch            {(load.TotalMassKg - c.Mass.MassKg) / c.Mass.MassKg * 100,8:F2} %");
        MassProperties p = load.Properties;
        _o.WriteLine($"  loaded   CG {load.Cg.X,8:F4} {load.Cg.Y,8:F4} {load.Cg.Z,8:F4}   inertia {p.Ixx,8:F1} {p.Iyy,8:F1} {p.Izz,8:F1} ixz {p.Ixz,7:F2}");
        _o.WriteLine($"  authored CG {c.Mass.CgVec().X,8:F4} {c.Mass.CgVec().Y,8:F4} {c.Mass.CgVec().Z,8:F4}   inertia {c.Mass.Inertia.Ixx,8:F1} {c.Mass.Inertia.Iyy,8:F1} {c.Mass.Inertia.Izz,8:F1} ixz {c.Mass.Inertia.Ixz,7:F2}");

        // Roskam Part V radii of gyration, the only sourced sanity check this project has for an authored inertia
        // (docs/NEW-AIRPLANE-PROTOCOL.md): Rx against the span, Ry against the fuselage length, Rz against their mean.
        double b = 10.73, len = c.Fuselage is { Crossflow.LengthM: > 0 } ? c.Fuselage.Crossflow.LengthM : 6.83;
        _o.WriteLine($"  radii of gyration (b={b:F2} L={len:F2}): Rx {2 * Math.Sqrt(c.Mass.Inertia.Ixx / c.Mass.MassKg) / b:F3}  Ry {2 * Math.Sqrt(c.Mass.Inertia.Iyy / c.Mass.MassKg) / len:F3}  Rz {2 * Math.Sqrt(c.Mass.Inertia.Izz / c.Mass.MassKg) / ((b + len) / 2):F3}");

        var sc = new PracticeScenario(PracticeKind.ClimbLevelDescend, PracticeWind.Calm, c, PracticeScenarioTests.TestRunway(), 0);
        sc.Spawn();
        _o.WriteLine($"  Vso {sc.VsoMs * 1.944:F1} kt ({sc.VsoMs * 2.2369:F1} mph)   Vy {sc.VyMs * 1.944:F1} kt   ROC@Vy {sc.RocAtVyMs * 196.85:F0} fpm   cruise75 {sc.CruiseMs * 1.944:F1} kt ({sc.CruiseMs * 2.2369:F1} mph)   best glide {sc.BestLdMs * 1.944:F1} kt {sc.BestGlideRatio:F1}:1");
    }
}

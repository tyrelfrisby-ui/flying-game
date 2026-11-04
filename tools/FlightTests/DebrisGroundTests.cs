using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-03 21:18 (iPhone island-nav 2026.1103.2111): "crashed the Pitts on grass … the detached wings sink below
/// the ground and disappear." The wreck pieces are the sim's (CrashRelay → WreckSim): each is a rigid body of its own
/// boxes with gravity, air drag, and an inelastic box-corner contact with Coulomb friction against the island's ground.
/// These crash the Pitts S-2B on an island runway and on the grass beside it — the scripted, reproducible crash the
/// game's debug launch uses too (<see cref="PittsCrash"/>) — and follow every piece for 12 s: none may go into the
/// ground by more than <see cref="PenetrationToleranceM"/>, and each must leave with v + ω × r.
/// </summary>
[Collection("WorldTerrainActive")]
public class DebrisGroundTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public DebrisGroundTests(ITestOutputHelper o) { _o = o; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    /// <summary>How far a box corner may be inside the ground at the end of a contact step (the contact's own lift-out
    /// leaves none; this is the step's worth of a 10 m/s arrival, 5 cm).</summary>
    public const double PenetrationToleranceM = 0.05;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPieceOfAPittsCrashStaysOnTheGroundAndLeavesWithItsMomentum(bool grass)
    {
        WorldTerrain.Active = new WorldTerrain();
        PittsCrash.Result r = PittsCrash.Run(grass, 12.0, _o.WriteLine);
        _o.WriteLine($"{(grass ? "grass" : "runway")}: surface {r.Surface}; {r.Pieces.Count} pieces; lost [{string.Join(",", r.Lost)}]");
        foreach (var p in r.Pieces) _o.WriteLine($"  {p.Name,-18} {p.MassKg,6:F1} kg  deepest {p.DeepestM * 100,6:F1} cm  rest {p.FinalClearanceM:F2} m  phase {p.Phase}  |v at birth − (v + ω×r)| {p.BirthErrorMs:E1}");
        Assert.Equal(grass ? WorldTerrain.Surface.Rough : WorldTerrain.Surface.Paved, r.Surface);
        Assert.True(r.Pieces.Count >= 2, "the crash must break something off");
        Assert.All(r.Pieces, p =>
        {
            Assert.True(p.DeepestM < PenetrationToleranceM, $"{p.Name} went {p.DeepestM * 100:F1} cm into the ground");
            Assert.True(p.FinalClearanceM > -PenetrationToleranceM, $"{p.Name} ended {p.FinalClearanceM:F2} m below the ground");
            Assert.True(p.BirthErrorMs < 1e-9, $"{p.Name} left at {p.BirthErrorMs:E1} m/s off v + ω×r");
            Assert.False(double.IsNaN(p.FinalClearanceM), $"{p.Name} went NaN");
        });
        Assert.True(r.LinearMomentumError < 1e-9, $"|Δp|/p {r.LinearMomentumError:E1} at a separation");
    }
}

/// <summary>
/// A Pitts S-2B cartwheel, scripted: on the first island runway's centreline (or on the grass beside it), heading
/// down the runway at 35 m/s, banked 60° right, nose 15° down, sinking 8 m/s, its lowest hard point 10 cm up. The game's
/// debug launch argument <c>-crashtest grass|runway</c> (FlightSimDriver) starts the same state (CrashScenario).
/// </summary>
public static class PittsCrash
{
    public sealed class Piece
    {
        public string Name = ""; public double MassKg, DeepestM, FinalClearanceM, BirthErrorMs; public WreckPhase Phase;
    }
    public sealed class Result
    {
        public WorldTerrain.Surface Surface; public List<Piece> Pieces = new(); public List<AirframeComponent> Lost = new();
        public double LinearMomentumError;
    }

    /// <summary>The scripted start (the game's <c>-crashtest</c> launch uses the same).</summary>
    public static RigidBodyState Start(AircraftConfig c, bool grass) => CrashScenario.PittsStart(c, grass);

    public static Result Run(bool grass, double seconds, Action<string>? log = null)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
        RigidBodyState s0 = Start(c, grass);
        var res = new Result { Surface = WorldTerrain.SurfaceAt(s0.Position.X, s0.Position.Y, -s0.Position.Z) };
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
        var relay = new CrashRelay(ac);   // the live world: the island's own ground under every piece
        var byPiece = new Dictionary<WreckPiece, Piece>();
        Vec3 P = c.Mass.CgVec();
        var inputs = new ControlInputs(0, 0, 0, 1);
        const double dt = 0.005;
        for (int i = 0; i < (int)(seconds / dt); i++)
        {
            MassProperties m0 = ac.MassProperties; Vec3 c0 = ac.CgShiftBody;
            int born = relay.Wreck.Pieces.Count;
            ac.Step(inputs, dt);
            if (relay.Wreck.Pieces.Count > born)
            {
                RigidBodyState s = ac.State;
                Vec3 pB = (s.Velocity + Vec3.Cross(s.Rates, c0)) * m0.MassKg;
                Vec3 pA = (s.Velocity + Vec3.Cross(s.Rates, ac.CgShiftBody)) * ac.MassProperties.MassKg;
                for (int k = born; k < relay.Wreck.Pieces.Count; k++)
                {
                    WreckPiece wp = relay.Wreck.Pieces[k];
                    Vec3 r = wp.CgBody - P, vb = s.Attitude.Conjugate().Rotate(wp.WorldVelocity);
                    pA += vb * wp.MassKg;
                    byPiece[wp] = new Piece { Name = wp.Name, MassKg = wp.MassKg, BirthErrorMs = (vb - (s.Velocity + Vec3.Cross(s.Rates, r))).Length };
                    log?.Invoke($"t {i * dt:F3}: {wp.Name} off, {wp.MassKg:F1} kg at {s.Attitude.Rotate(s.Velocity).Length:F1} m/s");
                }
                res.LinearMomentumError = System.Math.Max(res.LinearMomentumError, (pA - pB).Length / pB.Length);
            }
            relay.Step(dt);
            foreach (WreckPiece wp in relay.Wreck.Pieces)
            {
                Piece p = byPiece[wp];
                // The lowest box corner against the island's ground under THAT corner (not under the piece's centre).
                Vec3 low = wp.LowestPoint();
                double clear = -low.Z - WorldTerrain.GroundHeightAt(low.X, low.Y);
                p.DeepestM = System.Math.Max(p.DeepestM, -clear);
                p.FinalClearanceM = clear;
                p.Phase = wp.Phase;
            }
        }
        res.Pieces.AddRange(byPiece.Values);
        res.Lost.AddRange(ac.LostComponents);
        return res;
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (TURB_PROBE=1): what the TR-379 sideslip-roll table does to ORDINARY flight, not to spins.
/// `TurbulenceTests.GliderBuffetsButStaysStableInModerate` flies the 2-33 hands-on-the-trim through 40 s of
/// moderate turbulence and requires it to still be flying between 12 and 40 m/s. Giving the 2-33 the table
/// ended that run at 43 m/s from a 22 m/s trim, which is the aeroplane winding up rather than being buffeted.
///
/// The suspicion is a double count: the table is applied at every angle of attack, but TR-379 measured the
/// SEPARATED wing, and in attached flow the strip wing already makes its own roll-due-to-sideslip out of
/// dihedral and geometry. This asks how far it reaches — which types, and how badly.
/// </summary>
[Collection("WorldTerrainActive")]   // writes the global Atmosphere
public class TurbulenceStabilityProbe
{
    private readonly ITestOutputHelper _out;
    public TurbulenceStabilityProbe(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("glider-2-33-like", 22.0)]
    [InlineData("glider-swift-s1-like", 30.0)]
    [InlineData("glider-eb29r-like", 32.0)]
    [InlineData("c172-like", 45.0)]
    [InlineData("pa18-cub-like", 33.0)]
    [InlineData("extra-300-like", 55.0)]
    public void HandsOnTheTrimInModerateTurbulence(string id, double trimMs)
    {
        if (Environment.GetEnvironmentVariable("TURB_PROBE") == null) return;
        _out.WriteLine($"== {id} trimmed at {trimMs:F0} m/s, 40 s of moderate turbulence, stick holding the trim");
        foreach (bool table in new[] { false, true })
        {
            Atmosphere.ActiveTurbulence = Turbulence.Moderate(2);
            Atmosphere.SimTimeSec = 0;
            try
            {
                AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
                string col = c.SpinAero?.RollDueToSideslip ?? "monoplane";
                c.SpinAero = table ? new SpinAeroConfig { RollDueToSideslip = col } : null;
                TrimSolver.Result trim = TrimSolver.SolveGliderTrim(c, trimMs, 600, idleProp: c.Propulsion != null);
                double half = trim.ThetaRad / 2;
                var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -600),
                    new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                    new Vec3(trimMs * Math.Cos(trim.AlphaRad), 0, trimMs * Math.Sin(trim.AlphaRad)), Vec3.Zero),
                    new ControlDeflections(0, trim.ElevatorRad, 0, 0));
                var sim = new SimLoop(ac);
                double stick = Aircraft.StickForDeflection(trim.ElevatorRad, c.Controls.Elevator);
                double lever = c.Propulsion == null ? -1.0 : 1.0;
                double maxBank = 0, maxBeta = 0, maxV = 0;
                for (int i = 0; i < 400; i++)
                {
                    sim.RunFor(0.1, new ControlInputs(0, stick, 0, lever));
                    RigidBodyState s = ac.State;
                    (double roll, _, _) = FormationPilot.Euler(s.Attitude);
                    maxBank = Math.Max(maxBank, Math.Abs(roll) * 57.3);
                    maxBeta = Math.Max(maxBeta, Math.Abs(Math.Asin(Math.Clamp(s.Velocity.Y / Math.Max(1, s.Velocity.Length), -1, 1)) * 57.3));
                    maxV = Math.Max(maxV, s.Velocity.Length);
                }
                RigidBodyState e = ac.State;
                _out.WriteLine($"   table {(table ? "ON " : "off")}  ended {e.Velocity.Length,5:F1} m/s (peak {maxV,5:F1})  " +
                               $"bank peak {maxBank,5:F0}  beta peak {maxBeta,4:F0}  alt {-e.Position.Z,5:F0} m");
            }
            finally { Atmosphere.ActiveTurbulence = null; Atmosphere.SimTimeSec = 0; }
        }
    }
}

using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// "Start in a thermal" (owner 2026-10-01): spawned banked at the turning min-sink speed, trimmed, on a circle round the
/// core — holding the bank (and nothing else: elevator left at the start trim) keeps the aircraft in the thermal and
/// climbing, in still air AND in the default 5 m/s easterly (the plume leans with the wind, Thermal.CoreAt).
/// </summary>
[Collection("WorldTerrainActive")]   // writes/reads the global Atmosphere (wind, turbulence): not in parallel with others that do
public class ThermalStartTests
{
    private readonly ITestOutputHelper _out;
    public ThermalStartTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("glider-2-33-like", 0.0)]
    [InlineData("glider-2-33-like", 5.0)]
    [InlineData("pa18-cub-like", 0.0)]
    [InlineData("pa18-cub-like", 5.0)]
    public void HoldingTheBankStaysInTheThermal(string id, double windFromEastMs)
    {
        var keepThermals = new List<Thermal>(Atmosphere.Thermals);
        try
        {
            Atmosphere.Thermals.Clear();
            Atmosphere.SteadyWind = new Vec3(0, -windFromEastMs, 0);   // from 090: blowing toward the west (−y)
            var th = new Thermal(new Vec3(300, 300, 0), 360, 12.0, 2000);
            Atmosphere.Thermals.Add(th);
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            ThermalSpawn.Plan plan = ThermalSpawn.Compute(cfg, th, 600);
            _out.WriteLine($"{id} wind {windFromEastMs}: bank {plan.BankRad * 57.3:F0}°, {plan.SpeedMs:F1} m/s (wings-level min sink {plan.MinSinkWingsLevelMs:F1}), circle radius {plan.RadiusM:F0} m");

            var ac = new Aircraft(cfg, plan.State, new FlyingGame.Core.Aero.ControlDeflections(0, plan.ElevatorRad, 0, 0));
            double stick = Aircraft.StickForDeflection(plan.ElevatorRad, cfg.Controls.Elevator);
            double lever = cfg.Propulsion == null ? -1.0 : 1.0;   // glider: spoilers stowed; powered: idle
            double h = SimLoop.DefaultFixedDtSec, inCore = 0, total = 0, alt0 = -plan.State.Position.Z, maxAlpha = 0;
            double iRoll = 0;
            for (double t = 0; t < 120; t += h)
            {
                RigidBodyState s = ac.State;
                (double roll, _, _) = FormationPilot.Euler(s.Attitude);
                double err = plan.BankRad - roll;
                iRoll = Math.Clamp(iRoll + err * h * 0.5, -0.3, 0.3);
                double ail = Math.Clamp(err * 2.0 + iRoll - s.Rates.X * 0.3, -1, 1);   // the pilot holds the bank
                double rud = Math.Clamp(-s.Velocity.Y * 0.05, -0.5, 0.5);                  // and keeps the ball centred
                ac.Step(new ControlInputs(ail, stick, rud, lever), h);
                double alt = -ac.State.Position.Z;
                Vec3 core = th.CoreAt(alt);
                double zf = Math.Clamp(alt / th.TopAltitudeM, 0, 1);
                double d = Math.Sqrt(Math.Pow(ac.State.Position.X - core.X, 2) + Math.Pow(ac.State.Position.Y - core.Y, 2));
                if (d < th.CoreRadiusM * (1 + 0.8 * zf)) inCore++;
                total++;
                Vec3 vb = ac.State.Velocity;
                maxAlpha = Math.Max(maxAlpha, Math.Atan2(vb.Z, vb.X));
                if ((int)(t / h) % 4000 == 0) _out.WriteLine($"t={t:F0} alt {alt:F0} m, {d:F0} m from the core, bank {roll * 57.3:F0}°, V {vb.Length:F1}");
            }
            double frac = inCore / total, climb = -ac.State.Position.Z - alt0;
            _out.WriteLine($"in the core {frac:P0} of the time, climbed {climb:F0} m in 120 s, max α {maxAlpha * 57.3:F1}°");
            Assert.True(frac > 0.9, $"only {frac:P0} of the time inside the core");
            Assert.True(climb > 300, $"climbed only {climb:F0} m");
        }
        finally
        {
            Atmosphere.Thermals.Clear(); Atmosphere.Thermals.AddRange(keepThermals);
            Atmosphere.SteadyWind = Vec3.Zero;
        }
    }
}

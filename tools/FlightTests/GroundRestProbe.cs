using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Not an assertion, a measurement, kept because the next person to be shown an aeroplane sitting in the ground
/// should start from numbers. How deep the AIRFRAME's own hard points are in the terrain: on its gear, on its belly
/// with every leg torn off, and through an arrival.
///
/// The hard points are a PENALTY spring (AirframeContact.SpringNPerM = 150 kN/m per contact patch) with no position
/// correction, so an aeroplane held up by them is necessarily inside the ground by weight/k. That constant is in
/// newtons per metre and not per metre per tonne, so the depth is proportional to the aeroplane's weight: measured
/// 2026-11-10 on AP1's grass, 2.4 cm for a 2-33, 2.6 cm for a J-3, 3.7 cm for a Pitts — and 65.5 cm for a DC-3,
/// which is a wreck two-thirds of a metre into the hillside and is what Ty was looking at if he was ever in one.
/// Dropped in from 6 m the Pitts peaks at 34.8 cm and is back inside 20 cm in 0.15 s; the DC-3 peaks at 1.34 m and
/// settles at 0.97 m, because what it settles to is its own weight/k and not zero.
///
/// On its gear every one of them reads 0.0 cm: LandingGear carries the aeroplane on its wheels, and the wheels are
/// not this spring. So a healthy aeroplane taxiing is NOT in the ground; only one on its belly is.
///
/// NOT FIXED HERE, deliberately. Making the spring scale with mass (k = 1600 m, the stiffest a 200 Hz step holds at
/// the same stability margin for every aeroplane, giving all of them the same 0.6 cm) would change every contact in
/// the simulation — breaks, bounces, slides, ground loops — and the suite is globally coupled. It wants its own pass
/// with its own before/after.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class GroundRestProbe : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;
    private readonly ITestOutputHelper _out;
    public GroundRestProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static double Deepest(Aircraft ac)
    {
        RigidBodyState s = ac.State;
        double worst = 0;
        foreach (ContactPoint p in ac.ContactPoints)
        {
            Vec3 w = s.Position + s.Attitude.Rotate(p.Body - ac.Config.Mass.CgVec());
            double g = WorldTerrain.HardPointGroundAt(w.X, w.Y, -w.Z);
            worst = System.Math.Max(worst, g - (-w.Z));
        }
        return worst;
    }

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("dc3-like")]
    [InlineData("glider-2-33-like")]
    public void HowDeepIsTheAirframeAtRestAndOnArrival(string id)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig c = Load(id);
            IslandField f = IslandSites.Fields.First(z => z.Id == "AP1");
            // At rest on its gear, then with every leg gone (the wreck on its belly).
            var rest = new Aircraft(c, LandingGear.RestingState(c, f.Centre.X, f.Centre.Y, f.Elevation, 0), ControlDeflections.Neutral);
            new SimLoop(rest, 1.0 / 200.0).RunFor(5.0, new ControlInputs(0, 0, 0, 0));
            double onGear = Deepest(rest);
            foreach (AirframeComponent g in new[] { AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearNose, AirframeComponent.GearTail })
                rest.LoseComponent(g);
            new SimLoop(rest, 1.0 / 200.0).RunFor(8.0, new ControlInputs(0, 0, 0, 0));
            double onBelly = Deepest(rest);

            // Arrival: dropped from 6 m, nose a little down, no gear. Deepest excursion and how long to crawl back.
            var drop = new Aircraft(c, new RigidBodyState(
                new Vec3(f.Centre.X, f.Centre.Y, -(f.Elevation + 6.0)), new Quat(0, 0, 0, 1), new Vec3(10, 0, 6), Vec3.Zero), ControlDeflections.Neutral);
            foreach (AirframeComponent g in new[] { AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearNose, AirframeComponent.GearTail })
                drop.LoseComponent(g);
            var loop = new SimLoop(drop, 1.0 / 200.0);
            double peak = 0, peakT = 0, backT = -1;
            for (int i = 1; i <= 2000; i++)
            {
                loop.RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, 0));
                double d = Deepest(drop);
                if (d > peak) { peak = d; peakT = i / 200.0; }
                if (peak > 0.2 && backT < 0 && d < 0.2 && i / 200.0 > peakT) backT = i / 200.0 - peakT;
            }
            _out.WriteLine($"  {id,-22} on gear {onGear * 100,6:F1} cm   on belly {onBelly * 100,6:F1} cm   arrival peak {peak * 100,7:F1} cm at {peakT:F2} s, back inside 20 cm after {(backT < 0 ? double.NaN : backT),6:F2} s   final {Deepest(drop) * 100,6:F1} cm");
        }
        finally { WorldTerrain.Active = null; }
    }
}

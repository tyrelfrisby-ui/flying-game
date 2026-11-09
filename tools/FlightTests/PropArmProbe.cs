using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>WHERE THE MODEL PUTS THE PROPELLER (PROP_ARM=1). Ty 2026-11-09: "Check prop location on the pitts. Make
/// sure it has the correct arm." The hub is DERIVED from the fuselage construction, not authored, so it is worth
/// checking against the inertia build-up's measured disc station and the TCDS arms.</summary>
public class PropArmProbe
{
    private readonly ITestOutputHelper _o;
    public PropArmProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("PROP_ARM") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like" })
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
            var hubs = AirframeContact.PropHubsBody(c);
            (double noseCut, double tailEnd) = AirframeContact.FuselageStations(c);
            Vec3 cg = c.Mass.CgVec();
            _o.WriteLine($"\n=== {id}");
            _o.WriteLine($"  fuselage: nose cut x {noseCut,7:F3}   tail end x {tailEnd,7:F3}   length {c.Fuselage?.Crossflow?.LengthM,6:F3}");
            _o.WriteLine($"  CG        x {cg.X,7:F3}");
            for (int i = 0; i < hubs.Count; i++)
                _o.WriteLine($"  HUB[{i}]    x {hubs[i].X,7:F3}  z {hubs[i].Z,7:F3}   ARM ahead of CG {hubs[i].X - cg.X,6:F3} m ({(hubs[i].X - cg.X) / 0.0254,5:F1} in)");
            _o.WriteLine($"  thrustLineZ {c.Propulsion?.ThrustLineZ,6:F3}   prop diameter {c.Propulsion?.PropDiameterM,5:F2} m (radius {0.5 * (c.Propulsion?.PropDiameterM ?? 0),5:F3})");
        }
    }
}

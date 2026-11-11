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
/// Ty 2026-11-06 06:34 AKST (t915u): "the pitts crash still fails to follow the occupants portion of the wreck."
///
/// The camera follows the sim's Aircraft (FuselageFollowCameraTests), and the Aircraft is the CABIN: a fuselage break-up
/// sends the nose and the tail boom off as wreck pieces and leaves the cabin flying. What was wrong was where the cabin
/// ended. <see cref="AirframeContact.FuselageStations"/> set its cuts off the wing root alone, so the Pitts' tail cut fell
/// at x -0.70 m while its solo pilot sits in the REAR seat at -1.14: the break-up cut the pilot's cockpit off with the
/// tail, and the camera stayed with the front cockpit and the wings. The Extra's and Great Lakes' rear seats were aft of
/// their cuts as well, and the Grob's front seat ahead of its nose cut. The cuts now keep every seat inside the cabin.
/// </summary>
[Collection("WorldTerrainActive")]
public class OccupantCabinBreakupTests : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public OccupantCabinBreakupTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The four types whose seats used to fall outside the cabin, with cuts copied by hand from the configs'
    /// seat stations (loading.seats[].station[0]) and the 0.5 m seat clearance: Pitts rear -1.1351, Extra rear -1.2801,
    /// Great Lakes rear -1.7621 (tail cuts); Grob front 1.132078 (nose cut).</summary>
    [Theory]
    [InlineData("pitts-s2b-like", null, -1.6351)]
    [InlineData("extra-300-like", null, -1.7801)]
    [InlineData("great-lakes-like", null, -2.2621)]
    [InlineData("glider-grob103-acro-like", 1.632078, null)]
    public void TheCutsFallOutsideTheSeats(string id, double? nose, double? tail)
    {
        (double n, double t) = AirframeContact.FuselageStations(Load(id));
        _out.WriteLine($"{id}: nose cut {n:0.0000}, tail cut {t:0.0000}");
        if (nose is double nn) Assert.Equal(nn, n, 4);
        if (tail is double tt) Assert.Equal(tt, t, 4);
    }

    /// <summary>Every seat of every seated type is inside the cabin, with room for the occupant; the cabin still stops
    /// short of the tailplane.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("glider-grob103-acro-like")]
    [InlineData("glider-2-33-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    public void EverySeatIsInTheCabin(string id)
    {
        AircraftConfig c = Load(id);
        (double nose, double tail) = AirframeContact.FuselageStations(c);
        Assert.NotEmpty(c.Loading!.Seats);
        foreach (StationConfig s in c.Loading.Seats)
            Assert.InRange(s.Station[0], tail + AirframeContact.SeatClearM - 1e-9, nose - AirframeContact.SeatClearM + 1e-9);
        double tailplane = double.MinValue;
        foreach (SurfaceConfig sf in c.Surfaces)
            if (sf.Id == "hStab" || sf.Id.ToLowerInvariant().Contains("vstab"))
                foreach (StripConfig st in sf.Strips) tailplane = System.Math.Max(tailplane, st.Pos[0] + 0.25 * st.Chord);
        Assert.True(tail > tailplane + 0.3, $"{id}: tail cut {tail:0.00} reaches the tailplane at {tailplane:0.00}");
    }

    /// <summary>The wreck model tears the fuselage at the cabin's ends (its Cuts), and every element a nose or tail-boom
    /// piece takes has its centre beyond that tear: no piece's mass sits at a seat. (An element is a box with its
    /// section's volume, centred on that volume — the tail cone's box is the cone's length about its centroid — so its
    /// extent is not the section's and is not what is asserted.)</summary>
    private static void AssertNoPieceCarriesASeat(string id, Aircraft ac, CrashRelay relay)
    {
        var seats = ac.Config.Loading!.Seats;
        foreach (Cut cut in ac.Hydro.Model.Cuts)
        {
            if (cut.Parent != WreckAtom.Cabin || cut.Child is not (WreckAtom.Nose or WreckAtom.TailCone)) continue;
            foreach (StationConfig s in seats)
                Assert.True(cut.Child == WreckAtom.Nose ? cut.At.X >= s.Station[0] + AirframeContact.SeatClearM - 1e-9
                                                        : cut.At.X <= s.Station[0] - AirframeContact.SeatClearM + 1e-9,
                    $"{id}: the wreck tears {cut.Child} off at x {cut.At.X:0.00}, through the {s.Id} seat at {s.Station[0]:0.00}");
        }
        foreach (WreckPiece p in relay.Wreck.Pieces)
        {
            if (p.Component is not (AirframeComponent.Nose or AirframeComponent.TailBoom)) continue;
            bool nose = p.Component == AirframeComponent.Nose;
            foreach (StationConfig s in seats)
                foreach (HydroElement e in p.Elements)
                    Assert.True(nose ? e.Centre.X > s.Station[0] : e.Centre.X < s.Station[0],
                        $"{id}: the {p.Component} piece's {e.Name} at x {e.Centre.X:0.00} is on the {s.Id} seat's side ({s.Station[0]:0.00})");
        }
    }

    /// <summary>A mid-fuselage break (both ends off): the nose and the tail boom leave as pieces, the seats and the
    /// occupant stay on the Aircraft — the camera's Target — which carries on from where it was.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]
    public void TheBreakUpLeavesTheSeatsOnTheAircraft(string id)
    {
        WorldTerrain.Active = null;
        var ac = new Aircraft(Load(id), new RigidBodyState(new Vec3(0, 0, -60), new Quat(0, 0, 0, 1), new Vec3(30, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        Vec3 seat = ac.OccupantSeatBody;
        ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
        Vec3 before = ac.State.Position;
        ac.LoseComponent(AirframeComponent.Cabin);
        Assert.True(ac.IsLost(AirframeComponent.Nose) && ac.IsLost(AirframeComponent.TailBoom), $"{id}: not broken up");
        Assert.Contains(relay.Wreck.Pieces, p => p.Component == AirframeComponent.TailBoom);
        AssertNoPieceCarriesASeat(id, ac, relay);
        foreach (WreckPiece p in relay.Wreck.Pieces) _out.WriteLine($"{id}: {p.Component} {p.MassKg:0.0} kg, CG x {p.CgBody.X:0.00}");
        Assert.Equal(seat, ac.OccupantSeatBody);
        Assert.Equal(before, ac.State.Position);
    }

    /// <summary>The Pitts from the report, into flat ground 60 degrees nose down at 40 m/s forward and 30 m/s down: it
    /// breaks up in its own contact physics (no LoseComponent call; a steel-tube fuselage crushes in a flat slam, so this
    /// is the arrival that separates it): the nose with its engine, the wings and the tail surfaces leave as pieces, and
    /// none of them carries a seat. The rear seat Ty flies it from is still the Aircraft's.</summary>
    [Fact]
    public void APittsDiveBreaksUpAroundItsOccupant()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        double th = -60 * System.Math.PI / 180;
        Quat att = new(0, System.Math.Sin(th / 2), 0, System.Math.Cos(th / 2));
        double lowest = double.MaxValue;
        foreach (var p in AirframeContact.BuildPoints(c)) lowest = System.Math.Min(lowest, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        foreach (GearConfig g in c.Gear) lowest = System.Math.Min(lowest, -att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, lowest - 0.05), att, att.Conjugate().Rotate(new Vec3(40, 0, 30)), Vec3.Zero),
            ControlDeflections.Neutral);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        var sim = new SimLoop(ac);
        for (double t = 0; t < 2.0; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        _out.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.True(ac.IsLost(AirframeComponent.Nose), "the dive should tear the nose off");
        Assert.Contains(relay.Wreck.Pieces, p => p.Component == AirframeComponent.Nose);
        AssertNoPieceCarriesASeat("pitts-s2b-like", ac, relay);
        Assert.Equal(c.Loading!.Seats[0].StationVec(), ac.OccupantSeatBody);   // the rear seat, still the Aircraft's
    }
}

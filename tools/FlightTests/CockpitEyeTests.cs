using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The seated pilot's eye (owner Ty, approved 2026-10-31: "the cockpit camera sits exactly at the seated pilot's
/// eye", and the same points are the basis for VR later).
///
/// Before this, no config carried an eye and no model had an Eye node, so <c>ChaseCamera</c>'s Cockpit view
/// estimated one from the procedural canopy: 0.4-1.3 m FORWARD of the pilot and 0.4-0.7 m high, which on every
/// type but the 172 put the camera above the canopy, the wing or the cabin roof. These tests pin the authored
/// data the camera now reads. The camera itself is Unity's and is checked by
/// <c>FlyingGame.EditorTools.CockpitEyeCheck</c>, which flies the real ChaseCamera path and asserts the camera
/// lands on the eye within 1 mm.
///
/// Frame: body reference, +x FORWARD, +y RIGHT, +z DOWN, so an eye above the datum has a NEGATIVE z.
/// </summary>
public sealed class CockpitEyeTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The eyes Ty approved, newest values.
    ///
    /// The Great Lakes pair were LOWERED on 2026-11-01 (rear -0.487 -> -0.305, front -0.413 -> -0.293) after Ty
    /// flew build 2026.1101.0733 and found himself looking over the top of the windscreen at the cowling. Each
    /// now sits where the level sightline passes through the middle of its own screen's glass. Before that the
    /// rear was corrected once already: the v4 figure (-1.970, 0, -0.264) on lane/codex-eyepoints was computed
    /// with the aeroplane 0.223 m low and sat in the tub.</summary>
    public static TheoryData<string, string, double, double, double> ApprovedEyes => new()
    {
        { "glider-2-33-like",   "front",   0.971,  0.000, -0.2122 },
        { "glider-2-33-like",   "rear",    0.164,  0.000, -0.060 },
        { "c172-like",          "pilot",  -0.346, -0.337, -0.548 },
        { "c172-like",          "copilot",-0.346,  0.332, -0.548 },
        { "j3-cub-like",        "rear",   -0.840,  0.000, -0.489 },
        { "j3-cub-like",        "front",  -0.157,  0.000, -0.599 },
        { "j3-cub-floats-like", "rear",   -0.840,  0.000, -0.489 },
        { "j3-cub-floats-like", "front",  -0.157,  0.000, -0.599 },
        { "great-lakes-like",   "rear",   -1.960,  0.000, -0.305 },
        { "great-lakes-like",   "front",  -1.193,  0.000, -0.293 },
        { "pitts-s2b-like",     "rear",   -1.325,  0.000, -0.300 },
        { "pitts-s2b-like",     "front",  -0.564,  0.000, -0.308 },
        { "extra-300-like",     "rear",   -1.597,  0.000, -0.518 },
        { "extra-300-like",     "front",  -0.687,  0.000, -0.529 },
        // Ty 2026-11-03 (the Blanc model's Pilot_Eye nodes), carried through Place's 90 mm drop onto the wheels.
        { "glider-grob103-acro-like", "front", 1.150, 0.000, -0.070 },
        { "glider-grob103-acro-like", "rear", -0.040, 0.000, -0.130 },
    };

    /// <summary>Every approved eye is in the config, to the millimetre the camera test also works to.</summary>
    [Theory]
    [MemberData(nameof(ApprovedEyes))]
    public void TheSeatCarriesTheEyeTyApproved(string id, string seatId, double x, double y, double z)
    {
        StationConfig seat = Seat(Load(id), seatId);
        Assert.True(seat.HasEye, $"{id}/{seatId} has no authored eye");
        Vec3 eye = seat.EyeVec();
        Assert.True(System.Math.Abs(eye.X - x) <= 1e-3, $"{id}/{seatId} eye x {eye.X} wanted {x}");
        Assert.True(System.Math.Abs(eye.Y - y) <= 1e-3, $"{id}/{seatId} eye y {eye.Y} wanted {y}");
        Assert.True(System.Math.Abs(eye.Z - z) <= 1e-3, $"{id}/{seatId} eye z {eye.Z} wanted {z}");
    }

    /// <summary>The camera starts in seat 0, so the seat Ty flies solo from has to be FIRST: the rear seat on
    /// every tandem, the front on the 2-33, the left on the 172. Reordering the list silently moves the pilot.</summary>
    [Theory]
    [InlineData("glider-2-33-like", "front")]
    [InlineData("glider-grob103-acro-like", "front")]   // flown solo from the front ([POH])
    [InlineData("c172-like", "pilot")]
    [InlineData("j3-cub-like", "rear")]
    [InlineData("j3-cub-floats-like", "rear")]
    [InlineData("great-lakes-like", "rear")]
    [InlineData("pitts-s2b-like", "rear")]
    [InlineData("extra-300-like", "rear")]
    public void ThePrimarySeatIsFirstSoTheCameraStartsThere(string id, string primary)
    {
        var seats = Load(id).Loading.Seats;
        Assert.NotEmpty(seats);
        Assert.Equal(primary, seats[0].Id);
        Assert.True(seats[0].HasEye, $"{id}: the primary seat must carry the eye");
    }

    /// <summary>An eye below the datum would be an eye in the floor: +z is DOWN, so every one must be negative.</summary>
    [Theory]
    [MemberData(nameof(ApprovedEyes))]
    public void EveryEyeIsAboveTheBodyDatum(string id, string seatId, double x, double y, double z)
    {
        _ = x; _ = y;
        Assert.True(z < 0.0, $"{id}/{seatId} eye z {z} is at or below the datum");
        Assert.True(Seat(Load(id), seatId).EyeVec().Z < 0.0);
    }

    /// <summary>On a tandem the solo seat is the BACK one, so its eye must sit aft of the front seat's. This is the
    /// test that would have caught the two being swapped, which reads as a plausible cockpit until you fly it.</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    [InlineData("great-lakes-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void TheRearSeatEyeIsBehindTheFrontSeatEye(string id)
    {
        AircraftConfig cfg = Load(id);
        Assert.True(Seat(cfg, "rear").EyeVec().X < Seat(cfg, "front").EyeVec().X - 0.3,
            $"{id}: the rear eye must be at least 0.3 m aft of the front eye");
    }

    /// <summary>A tandem flies solo from the back, so its two eyes are on the centreline; the 172's are not.</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("great-lakes-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("glider-2-33-like")]
    public void ATandemSeesFromTheCentreline(string id)
    {
        foreach (StationConfig seat in Load(id).Loading.Seats)
            Assert.True(System.Math.Abs(seat.EyeVec().Y) <= 1e-9, $"{id}/{seat.Id} is off the centreline");
    }

    /// <summary>The 172 is side-by-side: the two front eyes straddle the centreline at the same station and height,
    /// and the primary is the LEFT one (negative y, because +y is RIGHT).</summary>
    [Fact]
    public void TheTrainerSeesFromTheLeftSeat()
    {
        AircraftConfig cfg = Load("c172-like");
        Vec3 left = Seat(cfg, "pilot").EyeVec(), right = Seat(cfg, "copilot").EyeVec();
        Assert.True(left.Y < 0.0 && right.Y > 0.0, "the left seat must be the negative-y one");
        Assert.Equal(left.X, right.X, 3);
        Assert.Equal(left.Z, right.Z, 3);
        Assert.Equal("pilot", cfg.Loading.Seats[0].Id);
    }

    /// <summary>Authoring an eye must not change how the aeroplane flies. These seats are mass-neutral: a fresh
    /// aeroplane loads every slot to its referenceKg, so the reference loading still reproduces the authored mass
    /// and CG exactly. (Whether an airframe's authored massKg already includes a pilot is the open W&amp;B
    /// decision — see AGENT-LOG NEEDS YOU — which is why these carry 0 rather than a guess.)</summary>
    [Theory]
    [InlineData("glider-2-33-like")]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    [InlineData("great-lakes-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void TheNewSeatsAreMassNeutral(string id)
    {
        AircraftConfig cfg = Load(id);
        var load = new AircraftLoad(cfg);
        Assert.Equal(cfg.Mass.MassKg, load.TotalMassKg, 6);
        Assert.Equal(cfg.Mass.CgVec().X, load.Cg.X, 6);
        Assert.Equal(cfg.Mass.CgVec().Y, load.Cg.Y, 6);
        Assert.Equal(cfg.Mass.CgVec().Z, load.Cg.Z, 6);
        Assert.Equal(cfg.Mass.Inertia.Ixx, load.Properties.Ixx, 6);
        Assert.Equal(cfg.Mass.Inertia.Iyy, load.Properties.Iyy, 6);
        Assert.Equal(cfg.Mass.Inertia.Izz, load.Properties.Izz, 6);
    }

    /// <summary>EVERY config, not just the ones with seats: a freshly built aeroplane must load to exactly the
    /// mass it was authored with, because that is the number every book speed in this suite is tuned against.
    ///
    /// This is the test that found the one that was already wrong. The older W&amp;B lane authored
    /// `loading.emptyMassKg` as a true EMPTY weight (no pilot, no fuel); the current LoadingConfig reads the same
    /// key as "what is left when every station is empty", and those types had no stations in the current schema.
    /// SessionSettings.ApplyLoadTo runs on every spawn, so the GAME flew the Great Lakes 258 kg light, the Cub
    /// 206 kg and the Swift 50 kg, while this suite — which builds an Aircraft without calling ApplyLoad — went on
    /// using the authored figure and never saw it.</summary>
    [Theory]
    [MemberData(nameof(EveryConfig))]
    public void EveryAeroplaneLoadsToTheMassItWasAuthoredWith(string id)
    {
        AircraftConfig cfg = Load(id);
        var load = new AircraftLoad(cfg);
        Assert.True(System.Math.Abs(load.TotalMassKg - cfg.Mass.MassKg) <= 1e-6,
            $"{id}: authored {cfg.Mass.MassKg} kg but a fresh aeroplane loads to {load.TotalMassKg} kg");
    }

    public static TheoryData<string> EveryConfig
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json"))
                data.Add(Path.GetFileNameWithoutExtension(path));
            return data;
        }
    }

    private static StationConfig Seat(AircraftConfig cfg, string seatId)
    {
        foreach (StationConfig s in cfg.Loading.Seats) if (s.Id == seatId) return s;
        throw new Xunit.Sdk.XunitException($"{cfg.Id} has no seat '{seatId}'");
    }
}

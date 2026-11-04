using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The chase camera's Flight Path / Track toggle (Ty 2026-11-03: the J-3 floats on a 300 ft final in a crosswind,
/// the chase view sat at about 7:30 because the camera had switched itself to the ground track below 500 ft).
/// </summary>
public class CameraReferenceTests
{
    private sealed class MemoryStore : CameraReference.IIntStore
    {
        public readonly Dictionary<string, int> Values = new();
        public int GetInt(string key, int fallback) => Values.TryGetValue(key, out int v) ? v : fallback;
        public void SetInt(string key, int value) => Values[key] = value;
    }

    // 300 ft AGL on final heading north at 26 m/s through the air, 7 m/s of wind from the west (blowing east, +Y).
    // The nose crabs into the wind; the ground track drifts east of the air path.
    private static readonly Vec3 Air = new(26.0, 0.0, 1.0);
    private static readonly Vec3 Wind = new(0.0, 7.0, 0.0);
    private static Vec3 Ground => Air + Wind;
    private const double MinTrack = 4.0;

    [Fact]
    public void TheDefaultIsFlightPath()
    {
        Assert.Equal(CameraReference.Mode.FlightPath, CameraReference.Default);
        Assert.Equal(CameraReference.Mode.FlightPath, CameraReference.Load(new MemoryStore()));
    }

    [Fact]
    public void FlightPathAt300FtInACrosswindFollowsTheAirVelocity()
    {
        Vec3 d = CameraReference.Direction(CameraReference.Mode.FlightPath, Air, Ground, MinTrack)!.Value;
        Vec3 want = Air / Air.Length;
        Assert.True((d - want).Length < 1e-12, $"got {d}");
        // And it is NOT the ground track: 15 degrees apart in this wind.
        double apart = Math.Acos(Vec3.Dot(d, Ground / Ground.Length)) * 180 / Math.PI;
        Assert.InRange(apart, 14.0, 16.0);
    }

    [Fact]
    public void TrackFollowsTheGroundVelocity()
    {
        Vec3 d = CameraReference.Direction(CameraReference.Mode.Track, Air, Ground, MinTrack)!.Value;
        Assert.True((d - Ground / Ground.Length).Length < 1e-12, $"got {d}");
    }

    [Fact]
    public void BelowTheTrackSpeedItFollowsTheNose()
    {
        // Parked in a 7 m/s crosswind: the air vector is the wind from the side; the camera must follow the nose.
        Assert.Null(CameraReference.Direction(CameraReference.Mode.FlightPath, new Vec3(0, -7, 0), new Vec3(1, 0, 0), MinTrack));
        Assert.Null(CameraReference.Direction(CameraReference.Mode.Track, new Vec3(0, -7, 0), new Vec3(1, 0, 0), MinTrack));
    }

    [Fact]
    public void ALessonCanForceTrackAndTheButtonShowsIt()
    {
        Assert.Equal(CameraReference.Mode.Track, CameraReference.Effective(CameraReference.Mode.FlightPath, lessonForcesTrack: true));
        Assert.Equal(CameraReference.Mode.FlightPath, CameraReference.Effective(CameraReference.Mode.FlightPath, lessonForcesTrack: false));
        Assert.Equal("CAM: TRACK (LESSON)", CameraReference.Label(CameraReference.Mode.Track, forced: true));
        Assert.Equal("CAM: FLT PATH", CameraReference.Label(CameraReference.Mode.FlightPath, forced: false));
    }

    [Fact]
    public void TheChoicePersists()
    {
        var store = new MemoryStore();
        CameraReference.Save(store, CameraReference.Toggle(CameraReference.Load(store)));
        Assert.Equal(1, store.Values["set.cameraReference"]);
        Assert.Equal(CameraReference.Mode.Track, CameraReference.Load(store));
        CameraReference.Save(store, CameraReference.Toggle(CameraReference.Load(store)));
        Assert.Equal(CameraReference.Mode.FlightPath, CameraReference.Load(store));
        store.Values["set.cameraReference"] = 7;   // a value from nowhere reads as the default
        Assert.Equal(CameraReference.Mode.FlightPath, CameraReference.Load(store));
    }
}

using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Which way the chase camera looks along: the FLIGHT PATH (the relative wind — the air velocity) or the TRACK (the
/// velocity over the ground). Ty 2026-11-03: on a low final in the J-3 floats, in a crosswind, the chase view sat
/// off at about 7:30, because below 500 ft the camera switched itself to the ground track. The automatic
/// altitude switch is gone for the camera; the choice is a full-time button, Flight Path by default, saved.
///
/// A runway lesson can still force Track (the runway is the reference there); the button shows that it is forced.
/// Below <paramref name="minTrackSpeedMs"/> over the ground the camera follows the nose (parked in a crosswind the
/// air vector is the wind from the side — that rule is older than this and unchanged).
///
/// Pure: no Unity. The bridge hands vectors in and gets a direction (or null = follow the nose) back, and the
/// persisted choice goes through <see cref="IIntStore"/> (PlayerPrefs on the device).
/// </summary>
public static class CameraReference
{
    public enum Mode { FlightPath = 0, Track = 1 }

    /// <summary>What a pilot gets before choosing (Ty 2026-11-03).</summary>
    public const Mode Default = Mode.FlightPath;

    public const string PrefKey = "set.cameraReference";

    /// <summary>The mode actually flown: a lesson that forces the ground reference wins over the button.</summary>
    public static Mode Effective(Mode chosen, bool lessonForcesTrack) => lessonForcesTrack ? Mode.Track : chosen;

    /// <summary>Unit direction to look along, or null to follow the nose (too slow over the ground, or the chosen
    /// vector itself too short to have a direction).</summary>
    public static Vec3? Direction(Mode effective, Vec3 airVelocity, Vec3 groundVelocity, double minTrackSpeedMs)
    {
        if (groundVelocity.Length < minTrackSpeedMs) return null;
        Vec3 v = effective == Mode.Track ? groundVelocity : airVelocity;
        double len = v.Length;
        return len >= minTrackSpeedMs ? v / len : null;
    }

    /// <summary>The HUD flight-path marker follows the same button (Ty 2026-11-03): FLT PATH draws the AIR-velocity
    /// vector (green), TRACK draws the GROUND track (magenta, with the impact point). The old automatic switch to the
    /// ground track below 500 ft AGL is gone for the marker too. A runway lesson that forces Track forces it here as
    /// well (pass the <see cref="Effective"/> mode).</summary>
    public static bool MarkerUsesGroundTrack(Mode effective) => effective == Mode.Track;

    /// <summary>The velocity the flight-path marker is drawn along for this mode.</summary>
    public static Vec3 MarkerVelocity(Mode effective, Vec3 airVelocity, Vec3 groundVelocity)
        => MarkerUsesGroundTrack(effective) ? groundVelocity : airVelocity;

    public static string Label(Mode effective, bool forced)
        => (effective == Mode.Track ? "CAM: TRACK" : "CAM: FLT PATH") + (forced ? " (LESSON)" : "");

    public static Mode Toggle(Mode m) => m == Mode.Track ? Mode.FlightPath : Mode.Track;

    /// <summary>The persistence a setting needs (PlayerPrefs' GetInt/SetInt), so the round trip can be tested.</summary>
    public interface IIntStore
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
    }

    public static Mode Load(IIntStore store)
    {
        int v = store.GetInt(PrefKey, (int)Default);
        return v == (int)Mode.Track ? Mode.Track : v == (int)Mode.FlightPath ? Mode.FlightPath : Default;
    }

    public static void Save(IIntStore store, Mode m) => store.SetInt(PrefKey, (int)m);
}

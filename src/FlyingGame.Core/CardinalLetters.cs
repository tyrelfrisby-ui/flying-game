using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The floating N / S / E / W letters (SceneBootstrap). Ty 2026-11-03: a setting, OFF by default, remembered; and
/// when they are on they must always sit far off, just above the horizon, never somewhere you can fly up to. So
/// they are drawn as if at infinity: each letter rides with the camera's POSITION (never its rotation) at a fixed
/// offset, a vertical card <see cref="RangeM"/> out along its cardinal direction, its lower edge
/// <see cref="BottomElevationDeg"/> above true horizontal and <see cref="AngularHeightDeg"/> tall. The offset does
/// not depend on where the camera is, so the letters keep the same bearing, elevation and angular size at any
/// altitude, and the visible horizon (which dips below horizontal as you climb) is always under them.
///
/// Nothing in the sim reads the letters: the Arena 1 cardinal-turn challenge scores <c>headingRad</c> off the
/// aircraft state, so with them off it is flown on the HUD heading, the nav radios' heading card or the dials.
///
/// Vectors here are in UNITY world axes (x east, y up, z north; CoordinateMap), because that is the only place
/// they are used.
/// </summary>
public static class CardinalLetters
{
    /// <summary>What a fresh install, and anyone who has never touched the setting, sees.</summary>
    public const bool DefaultShown = false;

    /// <summary>The PlayerPrefs key, alongside the other <c>set.*</c> display settings.</summary>
    public const string PrefKey = "set.cardinalLetters";

    /// <summary>Horizontal range from the camera to each card. Inside the main camera's 20 km far plane with room
    /// for the elevation, and far enough out that ordinary relief inside it hides a letter as a ridge would.</summary>
    public const double RangeM = 15000.0;

    /// <summary>The letter's lower edge, above true horizontal as seen from the camera.</summary>
    public const double BottomElevationDeg = 2.0;

    /// <summary>The letter's height as seen from the camera (the old fixed letters were ~3.7° from the origin).</summary>
    public const double AngularHeightDeg = 3.0;

    /// <summary>The four letters and their directions (Unity +z is sim north).</summary>
    public static readonly (string Letter, Vec3 Dir)[] Cards =
    {
        ("N", new Vec3(0, 0, 1)), ("S", new Vec3(0, 0, -1)), ("E", new Vec3(1, 0, 0)), ("W", new Vec3(-1, 0, 0)),
    };

    private static double Tan(double deg) => Math.Tan(deg * Math.PI / 180.0);

    /// <summary>The card's height in metres at <see cref="RangeM"/> that spans the chosen angular height.</summary>
    public static double CardHeightM => RangeM * (Tan(BottomElevationDeg + AngularHeightDeg) - Tan(BottomElevationDeg));

    /// <summary>Where the card's centre sits relative to the camera, for a horizontal unit direction. Independent of
    /// the camera's position and rotation by construction.</summary>
    public static Vec3 Offset(Vec3 dir)
    {
        double up = RangeM * 0.5 * (Tan(BottomElevationDeg) + Tan(BottomElevationDeg + AngularHeightDeg));
        return new Vec3(dir.X * RangeM, up, dir.Z * RangeM);
    }

    /// <summary>The card's centre in the world for a camera at <paramref name="camera"/>.</summary>
    public static Vec3 Centre(Vec3 camera, Vec3 dir) => camera + Offset(dir);
}

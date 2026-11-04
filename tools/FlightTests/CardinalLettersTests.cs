using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim.Challenge;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-03: the floating N / S / E / W letters are a setting, OFF by default, and when on they sit far
/// off just above the horizon — drawn as if at infinity, riding with the camera's position, never reachable.
/// Expected values are literals (2° bottom edge, 3° tall, 15 km out, under the 20 km far plane).</summary>
public class CardinalLettersTests
{
    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    [Fact]
    public void OffByDefaultAndSavedUnderItsOwnKey()
    {
        Assert.False(CardinalLetters.DefaultShown);
        Assert.Equal("set.cardinalLetters", CardinalLetters.PrefKey);
    }

    [Fact]
    public void EachLetterSitsOnItsCardinalBearing()
    {
        // Unity axes: x east, y up, z north.
        var dirs = CardinalLetters.Cards.ToDictionary(c => c.Letter, c => CardinalLetters.Offset(c.Dir));
        Assert.Equal(new[] { "N", "S", "E", "W" }, CardinalLetters.Cards.Select(c => c.Letter).ToArray());
        Assert.Equal(0.0, Deg(Math.Atan2(dirs["N"].X, dirs["N"].Z)), 9);
        Assert.Equal(90.0, Deg(Math.Atan2(dirs["E"].X, dirs["E"].Z)), 9);
        Assert.Equal(180.0, Math.Abs(Deg(Math.Atan2(dirs["S"].X, dirs["S"].Z))), 9);
        Assert.Equal(-90.0, Deg(Math.Atan2(dirs["W"].X, dirs["W"].Z)), 9);
    }

    [Fact]
    public void JustAboveTheHorizonAndAlwaysTheSameAngularSize()
    {
        // Ground level, 10 000 ft, and well off the origin: the card's lower and upper edges, as seen from the
        // camera, are 2° and 5° above horizontal every time, and the card is always 15 km away horizontally.
        foreach (var cam in new[] { new Vec3(0, 0, 0), new Vec3(0, 3048, 0), new Vec3(-41234.5, 812, 27890.25) })
            foreach (var (_, dir) in CardinalLetters.Cards)
            {
                Vec3 rel = CardinalLetters.Centre(cam, dir) - cam;
                double range = Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z);
                Assert.Equal(15000.0, range, 6);
                double half = CardinalLetters.CardHeightM / 2;
                Assert.Equal(2.0, Deg(Math.Atan2(rel.Y - half, range)), 9);
                Assert.Equal(5.0, Deg(Math.Atan2(rel.Y + half, range)), 9);
            }
    }

    [Fact]
    public void CanNeverBeFlownUpTo()
    {
        // The offset is the same wherever the camera is, so closing on a letter is impossible; and the farthest corner
        // of a card stays inside the main camera's 20 000 m far plane (SceneBootstrap) so it is never clipped.
        Vec3 a = CardinalLetters.Offset(new Vec3(0, 0, 1));
        Vec3 b = CardinalLetters.Centre(new Vec3(0, 0, 14000), new Vec3(0, 0, 1)) - new Vec3(0, 0, 14000);
        Assert.Equal(a.X, b.X, 9); Assert.Equal(a.Y, b.Y, 9); Assert.Equal(a.Z, b.Z, 9);
        double far = Math.Sqrt(15000.0 * 15000.0 + Math.Pow(a.Y + CardinalLetters.CardHeightM / 2, 2)
                               + Math.Pow(CardinalLetters.CardHeightM, 2));   // + a generous half-width
        Assert.True(far < 20000.0, $"far corner {far:F0} m");
    }

    [Fact]
    public void CardinalTurnIsScoredOnTheAircraftNotTheScenery()
    {
        // With the letters off (the default) the Arena 1 heading lesson still works: it is graded on heading and
        // sideslip from the aircraft state, and its only cues are its callouts and the instruments.
        var def = ChallengeLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "challenges", "a1c3-cardinal-turn.json"));
        Assert.Equal(new[] { "headingRad", "headingRad", "betaRad" },
                     def.Phases.SelectMany(p => p.Tolerances).Select(t => t.Signal).ToArray());
        Assert.Equal(new[] { "callout.westTurnLeftToNorth", "callout.rollOutNorth" },
                     def.Phases.SelectMany(p => p.Callouts).Select(c => c.TextKey).ToArray());
    }
}

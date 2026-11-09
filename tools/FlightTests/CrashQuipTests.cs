using System.Collections.Generic;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The line at the end of a bad day (Ty 2026-11-08): long, random, never the same thing twice running — and never a
/// joke when the pilot is killed.
/// </summary>
public class CrashQuipTests
{
    [Fact]
    public void ADeadPilotNeverGetsAJoke()
    {
        // The rule that matters most, checked against EVERY crash kind and a long run of draws.
        var fatal = new HashSet<string>(CrashQuips.FatalLines);
        var bag = new QuipBag(7);
        foreach (CrashKind kind in System.Enum.GetValues(typeof(CrashKind)))
            for (int i = 0; i < 60; i++)
            {
                string line = CrashQuips.Pick(bag, kind, LandingOutcome.Dead);
                Assert.Contains(line, fatal);
            }
    }

    [Fact]
    public void APilotWhoWalksAwayGetsTheLineForWhatHeDid()
    {
        var bag = new QuipBag(7);
        foreach (CrashKind kind in System.Enum.GetValues(typeof(CrashKind)))
        {
            var own = new HashSet<string>(CrashQuips.For(kind));
            foreach (LandingOutcome o in new[] { LandingOutcome.Unhurt, LandingOutcome.Bruised, LandingOutcome.Injured })
                Assert.Contains(CrashQuips.Pick(bag, kind, o), own);
        }
    }

    [Fact]
    public void NoLineFollowsItself()
    {
        // The thing he asked for. Plain random would fail this about once every list-length draws.
        var bag = new QuipBag(11);
        string prev = null;
        for (int i = 0; i < 4000; i++)
        {
            string line = CrashQuips.Pick(bag, CrashKind.FlatSpin, LandingOutcome.Bruised);
            Assert.NotEqual(prev, line);
            prev = line;
        }
    }

    [Fact]
    public void EveryLineIsUsedBeforeAnyIsUsedTwice()
    {
        var all = CrashQuips.For(CrashKind.Ditching);
        var bag = new QuipBag(3);
        var seen = new HashSet<string>();
        for (int i = 0; i < all.Count; i++) Assert.True(seen.Add(bag.Next("ditch", all)), "a line repeated before the set was exhausted");
        Assert.Equal(all.Count, seen.Count);
    }

    [Fact]
    public void EachKindKeepsItsOwnPlaceInTheList()
    {
        // Crashing one way must not skip you through another kind's list.
        var bag = new QuipBag(5);
        var spins = new HashSet<string>();
        for (int i = 0; i < CrashQuips.For(CrashKind.FlatSpin).Count; i++)
        {
            spins.Add(CrashQuips.Pick(bag, CrashKind.FlatSpin, LandingOutcome.Bruised));
            CrashQuips.Pick(bag, CrashKind.Ditching, LandingOutcome.Bruised);   // interleaved, different set
        }
        Assert.Equal(CrashQuips.For(CrashKind.FlatSpin).Count, spins.Count);
    }

    [Fact]
    public void EveryKindHasSomethingToSay()
    {
        foreach (CrashKind kind in System.Enum.GetValues(typeof(CrashKind)))
            Assert.True(CrashQuips.For(kind).Count >= 4, $"{kind} has too few lines to stay fresh");
    }

    [Fact]
    public void TheListIsLong()
    {
        Assert.True(CrashQuips.Count >= 80, $"only {CrashQuips.Count} lines — he asked for a long list");
    }

    [Fact]
    public void NoLineIsRepeatedAcrossTheWholeTable()
    {
        var seen = new HashSet<string>();
        foreach (CrashKind kind in System.Enum.GetValues(typeof(CrashKind)))
            foreach (string s in CrashQuips.For(kind))
                Assert.True(seen.Add(s), $"duplicated line: {s}");
        foreach (string s in CrashQuips.FatalLines) Assert.True(seen.Add(s), $"duplicated line: {s}");
    }
}

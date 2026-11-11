using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Tuning;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE DEVELOPER TUNING PAGE: the registry, the gate, and the two failures that would be silent.
///
/// The silent failures are the point of this file. A knob that saves a value and shows it on screen while the
/// aeroplane flies the shipped one would poison every flight report the beta test pilots send back, and nothing
/// would say so. And a gate that reads "closed" while the page is still reachable would ship a developer page to
/// the public. Both are held here.
/// </summary>
public class DeveloperTuningTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(RepoRoot(), "configs", "aircraft", id + ".json"));

    /// <summary>A store that behaves like PlayerPrefs: strings, no deletion, survives a "relaunch".</summary>
    private sealed class FakeStore : ITuningStore
    {
        public readonly Dictionary<string, string> Map = new(StringComparer.Ordinal);
        public string? Get(string key) => Map.TryGetValue(key, out string? v) ? v : "";
        public void Set(string key, string value) => Map[key] = value;
    }

    // ------------------------------------------------------------------ the registry

    [Fact]
    public void EveryKnobCarriesWhatThePageNeedsToDrawIt()
    {
        foreach (Tunable t in TunableRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id), "a tunable with no id cannot have its value saved");
            Assert.False(string.IsNullOrWhiteSpace(t.Label), $"{t.Id}: no label");
            Assert.False(string.IsNullOrWhiteSpace(t.Group), $"{t.Id}: no group");
            // Ty asked for this explicitly: "Clicking on the title for each provides a description of what it does".
            // A knob without one is a number nobody can use, so it is a hard requirement rather than a nicety.
            Assert.True(t.Description.Length > 40, $"{t.Id}: the description must say what moving it does to the aeroplane, not restate the name");
            Assert.True(t.Max > t.Min, $"{t.Id}: Max must exceed Min");
            Assert.True(t.Step > 0 && t.FineStep > 0, $"{t.Id}: both steps must move the value");
            Assert.True(t.FineStep <= t.Step, $"{t.Id}: the fine step must not be coarser than the coarse step");
            // The readout must be able to show the fine step moving. One decimal against a 0.01 step is a knob you
            // cannot see yourself turning, which is exactly the complaint the page exists to answer.
            Assert.True(Math.Pow(10, -t.Decimals) <= t.FineStep * 1.0001, $"{t.Id}: {t.Decimals} decimals cannot show a fine step of {t.FineStep}");
        }
    }

    [Fact]
    public void KnobIdsAreUnique()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Tunable t in TunableRegistry.All)
            Assert.True(seen.Add(t.Id), $"{t.Id} is registered twice; an id is the key its override is saved under");
    }

    [Fact]
    public void DerivedKnobsAreSetApartAndNameTheirRule()
    {
        foreach (Tunable t in TunableRegistry.All)
        {
            if (t.Derived)
            {
                Assert.Equal(TunableRegistry.DerivedGroup, t.Group);
                Assert.True(t.Rule.Length > 20, $"{t.Id}: a derived number must say which rule it came from");
            }
            else Assert.NotEqual(TunableRegistry.DerivedGroup, t.Group);
        }
    }

    /// <summary>
    /// The line the brief drew: measured and certificated facts are not knobs. Empty weights, CG limits, inertias
    /// and strip geometry come from published data, the TCDS and the models, and making them adjustable invites
    /// someone to tune away a correct number and then report handling against it.
    /// </summary>
    [Fact]
    public void MeasuredFactsAreNotOnThePage()
    {
        // Matched as whole dotted path segments, not as substrings: "strips" inside "stallDynamics.stripSepTau"
        // is a different word, and a substring test would ban a legitimate knob for sharing six letters.
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "massKg", "cg", "inertia", "loading", "instruments", "vneMs", "strips", "pos", "area", "chord",
            "maxPowerW", "propDiameterM", "hubXM", "emptyMassKg", "cgLimits", "envelope", "tire", "tundraTire",
            // Excluded on the contract's own instruction: it is a hook for a derived loss, not a tuning knob, and
            // it has already been tuned once and double-counted the flap effectiveness with it (commit 4e5e1a7).
            "effectiveness",
        };
        foreach (Tunable t in TunableRegistry.All)
            foreach (string seg in t.Id.Split('.'))
                Assert.False(forbidden.Contains(seg),
                             $"{t.Id} looks like a measured or certificated fact ('{seg}'), which must not be a knob");
    }

    [Fact]
    public void EveryKnobRoundTripsOnARealAeroplane()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!t.Applies(cfg)) continue;
            double start = t.Get(cfg);
            double want = t.Clamp(start + t.Step);
            t.Set(cfg, want);
            Assert.True(Math.Abs(t.Get(cfg) - want) < Math.Abs(t.FineStep) * 0.01 + 1e-9,
                        $"{t.Id}: set {want} but read back {t.Get(cfg)} — the getter and setter disagree, so the page would show a number the sim is not flying");
            t.Set(cfg, start);
        }
    }

    [Fact]
    public void TheFinOffsetAndToeOutAndIdleRpmTyAskedForAreAllThere()
    {
        // His three worked examples: "toe in/out idle rpm adjustments vertical tail offsets".
        Assert.NotNull(TunableRegistry.ById("rig.finOffsetRad"));
        Assert.NotNull(TunableRegistry.ById("gear.toeOutDeg"));
        Assert.NotNull(TunableRegistry.ById("propulsion.idleRpm"));
    }

    // ------------------------------------------------------------------ live vs next spawn

    /// <summary>
    /// A knob marked Live must really be read off the config by the physics, not baked at construction. This checks
    /// the one that is easiest to get wrong and most load-bearing: the fin offset, which AeroModel adds per vertical
    /// strip per step. If it were baked the page would move a number and the aeroplane would not notice.
    /// </summary>
    [Fact]
    public void AKnobMarkedLiveChangesTheForcesWithoutRebuildingTheAeroplane()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        Dictionary<string, AirfoilTable> tables = FlyingGame.Sim.Aircraft.BuildAirfoilTables(cfg);
        var vel = new FlyingGame.Core.MathTypes.Vec3(60, 6, 2);   // yawed, so the fin is working
        var rates = new FlyingGame.Core.MathTypes.Vec3(0, 0, 0);

        FlyingGame.Core.MathTypes.Vec3 Yaw()
            => AeroModel.Compute(cfg, tables, vel, rates, FlyingGame.Core.MathTypes.Vec3.Zero, 1.0,
                                 ControlDeflections.Neutral).Moment;

        double before = Yaw().Z;
        Tunable fin = TunableRegistry.ById("rig.finOffsetRad")!;
        fin.Set(cfg, fin.Get(cfg) + 2.0);                       // two degrees of fin
        double after = Yaw().Z;
        Assert.True(Math.Abs(after - before) > 1e-6,
                    "the fin offset is marked live but moving it changed no yawing moment: it is being baked somewhere");
    }

    /// <summary>
    /// The one cached number the page touches. munkScale is folded into a ConditionalWeakTable keyed on the config,
    /// so without the invalidation the knob would move and the aeroplane would keep the old value for the rest of
    /// its life — the quiet kind of wrong.
    /// </summary>
    [Fact]
    public void TheOneCachedKnobIsInvalidatedWhenItMoves()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        cfg.Fuselage.MunkScale = 1.0;
        double first = AeroModel.FuselageMunkM3(cfg);          // caches it
        Assert.True(first > 0, "this type should have a Munk moment to cache");

        cfg.Fuselage.MunkScale = 0.5;
        Assert.Equal(first, AeroModel.FuselageMunkM3(cfg));    // still the cached one — which is why the hook exists

        AeroModel.ForgetFuselageMunk(cfg);
        Assert.True(Math.Abs(AeroModel.FuselageMunkM3(cfg) - first * 0.5) < first * 1e-6,
                    "ForgetFuselageMunk did not make the next read pick up the new scale");
    }

    [Fact]
    public void MovingTheMunkKnobThroughThePageInvalidatesItByItself()
    {
        TuningSession.ResetForTest();
        AircraftConfig cfg = Load("pitts-s2b-like");
        TuningSession.Apply(cfg);
        Tunable munk = TunableRegistry.ById("fuselage.munkScale")!;
        double first = AeroModel.FuselageMunkM3(cfg);
        TuningSession.Set(cfg, munk, munk.Get(cfg) * 0.5);
        Assert.True(Math.Abs(AeroModel.FuselageMunkM3(cfg) - first) > first * 1e-6,
                    "setting munkScale through the page left the cached moment in place");
    }

    // ------------------------------------------------------------------ persistence

    /// <summary>
    /// THE FAILURE THAT WOULD BE SILENT. Set a knob, kill the app, relaunch, load the same aeroplane — and the value
    /// must be IN THE SIM, not merely on the page. The config is re-read from StreamingAssets on every spawn, so
    /// without the re-apply hook the number would be shown and not flown.
    /// </summary>
    [Fact]
    public void AValueSurvivesARelaunchAndIsActuallyInEffect()
    {
        var store = new FakeStore();

        TuningSession.ResetForTest();
        TuningSession.UseStore(store);
        AircraftConfig first = Load("pitts-s2b-like");
        TuningSession.Apply(first);
        Tunable fin = TunableRegistry.ById("rig.finOffsetRad")!;
        double shipped = fin.Get(first);
        TuningSession.Set(first, fin, shipped + 1.5);

        // --- the app is killed. Every in-memory default, override and label goes with it; the store does not. ---
        TuningSession.ResetForTest();
        TuningSession.UseStore(store);

        AircraftConfig relaunched = Load("pitts-s2b-like");     // straight off disk: the shipped value again
        Assert.True(Math.Abs(fin.Get(relaunched) - shipped) < 1e-9, "the file should still carry the shipped value");

        TuningSession.Apply(relaunched);                        // the hook on the spawn path
        Assert.True(Math.Abs(fin.Get(relaunched) - (shipped + 1.5)) < 1e-6,
                    "the saved fin offset was not put back on the reloaded aeroplane: the page would show it and the aeroplane would not fly it");
    }

    [Fact]
    public void ResetGoesBackToWhatTheTypeShippedWithRatherThanAFieldInitialiser()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig cfg = Load("pitts-s2b-like");
        TuningSession.Apply(cfg);

        Tunable onset = TunableRegistry.ById("spinAero.controlSpoilerOnsetDeg")!;
        double shipped = onset.Get(cfg);
        // The Pitts' config carries 10; the C# field initialiser is 30. Reset must go to the config's value.
        Assert.True(Math.Abs(shipped - 30.0) > 1.0, "this test is only meaningful while the config and the field default differ");

        TuningSession.Set(cfg, onset, 22.0);
        Assert.True(TuningSession.IsModified(cfg, onset));
        TuningSession.Reset(cfg, onset);
        Assert.True(Math.Abs(onset.Get(cfg) - shipped) < 1e-9, "reset went somewhere other than the shipped value");
        Assert.False(TuningSession.IsModified(cfg, onset));
    }

    [Fact]
    public void ConfigurationsKeepSeparateTuning()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig wheels = Load("j3-cub-like");
        AircraftConfig bush = Load("j3-cub-like");
        bush.Bushwheels = true;

        Assert.NotEqual(TuningSession.ConfigKeyOf(wheels), TuningSession.ConfigKeyOf(bush));

        TuningSession.Apply(wheels);
        TuningSession.Apply(bush);
        Tunable idle = TunableRegistry.ById("propulsion.idleRpm")!;
        double shipped = idle.Get(bush);
        TuningSession.Set(wheels, idle, shipped + 150);

        AircraftConfig bushAgain = Load("j3-cub-like");
        bushAgain.Bushwheels = true;
        TuningSession.Apply(bushAgain);
        Assert.True(Math.Abs(idle.Get(bushAgain) - shipped) < 1e-9,
                    "tuning the Cub on wheels leaked onto the Cub on bushwheels");
    }

    [Fact]
    public void TheModifiedCountAndReportSayWhetherTheAeroplaneIsStock()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig cfg = Load("pitts-s2b-like");
        TuningSession.Apply(cfg);

        Assert.Equal(0, TuningSession.ModifiedCount(cfg));
        Assert.Contains("stock", TuningSession.DeltaReport(cfg), StringComparison.OrdinalIgnoreCase);

        Tunable toe = TunableRegistry.ById("gear.toeOutDeg")!;
        TuningSession.Set(cfg, toe, toe.Get(cfg) + 1.0);
        Assert.Equal(1, TuningSession.ModifiedCount(cfg));
        string report = TuningSession.DeltaReport(cfg);
        Assert.Contains("gear.toeOutDeg", report, StringComparison.Ordinal);
        Assert.Contains("shipped", report, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ copy from

    /// <summary>
    /// COPY FROM carries the DISTANCE from default, not the number. Ty's own example is the J-3 on wheels and the
    /// J-3 on floats: what he means by copying is "two degrees more toe-out suits it", and an absolute value would
    /// mean something different measured against a different starting point.
    /// </summary>
    [Fact]
    public void CopyFromCarriesTheDeltaRatherThanTheAbsoluteValue()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());

        AircraftConfig src = Load("j3-cub-like");
        AircraftConfig dst = Load("pitts-s2b-like");
        TuningSession.Apply(src);
        TuningSession.Apply(dst);

        // Any knob whose two aeroplanes SHIPPED different values will do, and finding it rather than naming it
        // keeps the test true when the fleet's numbers move. It has to be one where the two differ, because that is
        // the only case in which copying the value and copying the move are distinguishable at all.
        Tunable? knob = null;
        double srcShipped = 0, dstShipped = 0;
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!t.Applies(src) || !t.Applies(dst)) continue;
            double a = t.Get(src), b = t.Get(dst);
            if (Math.Abs(a - b) > Math.Abs(t.Step)) { knob = t; srcShipped = a; dstShipped = b; break; }
        }
        Assert.True(knob != null, "no knob ships different values on the Cub and the Pitts, so the delta cannot be told from the value");

        double move = knob!.Step;
        TuningSession.Set(src, knob, srcShipped + move);         // "one step more than it came with"

        int skipped;
        int moved = TuningSession.CopyFrom(dst, TuningSession.ConfigKeyOf(src), out skipped);
        Assert.True(moved >= 1);
        Assert.True(Math.Abs(knob.Get(dst) - knob.Clamp(dstShipped + move)) < Math.Abs(knob.FineStep) * 0.01 + 1e-9,
                    $"{knob.Id}: the copy carried the absolute value ({srcShipped + move}) instead of the move ({move}) — it reads {knob.Get(dst)}, wanted {dstShipped + move}");
    }

    [Fact]
    public void ACopyCanBeUndone()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig src = Load("j3-cub-like");
        AircraftConfig dst = Load("pitts-s2b-like");
        TuningSession.Apply(src);
        TuningSession.Apply(dst);

        Tunable idle = TunableRegistry.ById("propulsion.idleRpm")!;
        TuningSession.Set(src, idle, idle.Get(src) + 100);
        TuningSession.Set(dst, idle, idle.Get(dst) + 25);        // the target was already tuned before the copy
        double before = idle.Get(dst);

        Dictionary<string, double> undo = TuningSession.Snapshot(dst);
        int skipped;
        TuningSession.CopyFrom(dst, TuningSession.ConfigKeyOf(src), out skipped);
        Assert.True(Math.Abs(idle.Get(dst) - before) > 1e-6, "the copy should have moved it");

        TuningSession.Restore(dst, undo);
        Assert.True(Math.Abs(idle.Get(dst) - before) < 1e-6, "undo did not put the aeroplane back");
    }

    [Fact]
    public void TheCopyPickerOffersOnlyAeroplanesThatCarryTuning()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig src = Load("j3-cub-like");
        AircraftConfig dst = Load("pitts-s2b-like");
        TuningSession.Apply(src);
        TuningSession.Apply(dst);

        Assert.Empty(TuningSession.TunedAircraft(dst));          // nothing tuned yet

        Tunable idle = TunableRegistry.ById("propulsion.idleRpm")!;
        TuningSession.Set(src, idle, idle.Get(src) + 50);

        List<TunedEntry> offered = TuningSession.TunedAircraft(dst);
        Assert.Single(offered);
        Assert.Equal(TuningSession.ConfigKeyOf(src), offered[0].Key);
        // And never the aeroplane you are already on.
        TuningSession.Set(dst, idle, idle.Get(dst) + 50);
        foreach (TunedEntry e in TuningSession.TunedAircraft(dst))
            Assert.NotEqual(TuningSession.ConfigKeyOf(dst), e.Key);
    }

    // ------------------------------------------------------------------ the gate

    /// <summary>
    /// THE RELEASE BLOCKER. Ty, 2026-11-11: "It's just another page and the shipped version just doesn't have the ui
    /// to get to it." So the gate is navigation: one switch, one answer, and flipping it closes the route.
    /// </summary>
    [Fact]
    public void TheGateIsOneSwitchAndClosingItClosesTheRoute()
    {
        DeveloperAccess.ResetForTest();
        try
        {
            Assert.True(DeveloperAccess.Open, "the page is open during beta: Ty's test pilots are flying it");

            DeveloperAccess.RuntimeOverride = false;
            Assert.False(DeveloperAccess.Open, "closing the gate must close the route");

            DeveloperAccess.RuntimeOverride = true;
            Assert.True(DeveloperAccess.Open);
        }
        finally { DeveloperAccess.ResetForTest(); }
    }

    /// <summary>
    /// The page ships in the binary either way, so it has to be HARMLESS when there is no route to it: it must not
    /// touch the aeroplane by existing. Nothing in Core writes a knob except a pilot moving one, so a config that
    /// has only ever been through Apply with an empty store must come out bit-identical to the file.
    /// </summary>
    [Fact]
    public void AnUntouchedAeroplaneIsUnchangedByThePageExisting()
    {
        TuningSession.ResetForTest();
        TuningSession.UseStore(new FakeStore());
        AircraftConfig fromFile = Load("pitts-s2b-like");
        AircraftConfig applied = Load("pitts-s2b-like");
        TuningSession.Apply(applied);

        foreach (Tunable t in TunableRegistry.All)
        {
            if (!t.Applies(fromFile)) continue;
            Assert.True(Math.Abs(t.Get(applied) - t.Get(fromFile)) < 1e-12,
                        $"{t.Id}: merely loading the page's machinery moved a number on a stock aeroplane");
        }
        Assert.Equal(0, TuningSession.ModifiedCount(applied));
    }

    /// <summary>
    /// A SHIPPED BUILD FLIES STOCK. A knob a beta pilot saved must not follow him into a release where the page is
    /// gone: there would be no indicator, no reset and no way to know the aeroplane was not the shipped one. This is
    /// the gate's real job, and it is the failure that would be hardest to diagnose from a bug report.
    /// </summary>
    [Fact]
    public void ClosingTheGateStopsSavedKnobsFromBeingApplied()
    {
        var store = new FakeStore();
        TuningSession.ResetForTest();
        TuningSession.UseStore(store);
        DeveloperAccess.ResetForTest();
        try
        {
            AircraftConfig tuned = Load("pitts-s2b-like");
            TuningSession.Apply(tuned);
            Tunable fin = TunableRegistry.ById("rig.finOffsetRad")!;
            double shipped = fin.Get(tuned);
            TuningSession.Set(tuned, fin, shipped + 1.5);

            // --- the public release: the route is closed ---
            DeveloperAccess.RuntimeOverride = false;
            TuningSession.ResetForTest();
            TuningSession.UseStore(store);

            AircraftConfig released = Load("pitts-s2b-like");
            TuningSession.Apply(released);
            Assert.True(Math.Abs(fin.Get(released) - shipped) < 1e-9,
                        "a knob saved during the beta was applied in a build with no Developer page to see or reset it");
            Assert.Equal(0, TuningSession.ModifiedCount(released));
        }
        finally { DeveloperAccess.ResetForTest(); }
    }

    /// <summary>Every type in the fleet must survive being handed to the registry — a knob whose Applies or Get
    /// throws on some type would take the Options page down on that aeroplane.</summary>
    [Fact]
    public void ThePageCanBeDrawnForEveryTypeInTheFleet()
    {
        foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "configs", "aircraft"), "*.json"))
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(path);
            List<KeyValuePair<string, List<Tunable>>> groups = TunableRegistry.ForAircraft(cfg);
            foreach (KeyValuePair<string, List<Tunable>> grp in groups)
                foreach (Tunable t in grp.Value)
                {
                    double v = t.Get(cfg);
                    Assert.False(double.IsNaN(v), $"{Path.GetFileName(path)}: {t.Id} reads NaN");
                }
            TuningSession.ResetForTest();
            TuningSession.Apply(cfg);
            Assert.Equal(0, TuningSession.ModifiedCount(cfg));
        }
    }
}

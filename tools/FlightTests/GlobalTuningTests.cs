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
/// THE GLOBAL KNOBS — the Developer page's second section, for numbers that belong to the MODEL rather than to an
/// aeroplane. Ty's flutter law is the first of them: one exponent and three speeds, the same claim whether you are
/// flying the Cub or the Sabre.
///
/// THE TRAP THIS FILE EXISTS FOR. A per-aircraft knob lives on an AircraftConfig that FlightSimDriver.ResetFlight
/// RE-READS FROM StreamingAssets on every spawn, so it has to be written back onto each freshly loaded config or it
/// is silently lost — which is why TuningSession.Apply sits on the spawn path. A global lives in a static field.
/// Copying the per-aircraft path for it would have been the natural thing to do and would have introduced a bug
/// with no symptom until someone pressed RESET: the defaults capture that runs on the same path would read the
/// pilot's own modified value as the shipped one, and RESET would then put the knob back to wherever he had left
/// it. So the globals are applied ONCE, from the store, and the two paths are kept apart on purpose.
/// </summary>
public class GlobalTuningTests : IDisposable
{
    private readonly double _rate = Flutter.RateExponent, _arm = Flutter.ArmVd,
                           _fall = Flutter.FallbackInitiateVd, _ceil = Flutter.CeilingVd;

    /// <summary>Both sessions are STATIC, and an earlier test class in the same run may have left a store or a
    /// captured default behind. Clearing here rather than hoping is the difference between a test and a coin.</summary>
    public GlobalTuningTests()
    {
        GlobalTuningSession.ResetForTest();
        TuningSession.ResetForTest();
        DeveloperAccess.ResetForTest();
    }

    public void Dispose()
    {
        GlobalTuningSession.ResetForTest();
        Flutter.RateExponent = _rate; Flutter.ArmVd = _arm;
        Flutter.FallbackInitiateVd = _fall; Flutter.CeilingVd = _ceil;
        TuningSession.ResetForTest();
        DeveloperAccess.ResetForTest();
    }

    /// <summary>A store that behaves like PlayerPrefs: strings, no deletion, survives a "relaunch".</summary>
    private sealed class FakeStore : ITuningStore
    {
        public readonly Dictionary<string, string> Map = new(StringComparer.Ordinal);
        public string? Get(string key) => Map.TryGetValue(key, out string? v) ? v : "";
        public void Set(string key, string value) => Map[key] = value;
    }

    private static GlobalTunable Knob(string id)
    {
        GlobalTunable? t = GlobalTunableRegistry.ById(id);
        Assert.NotNull(t);
        return t!;
    }

    [Fact]
    public void TheFourFlutterKnobsAreRegisteredAndDrawable()
    {
        foreach (string id in new[] { "flutter.rateExponent", "flutter.armVd", "flutter.fallbackInitiateVd", "flutter.ceilingVd" })
        {
            GlobalTunable t = Knob(id);
            Assert.Equal(GlobalTunableRegistry.FlutterGroup, t.Group);
            Assert.False(string.IsNullOrWhiteSpace(t.Label), $"{id}: no label");
            Assert.True(t.Description.Length > 40, $"{id}: the description must say what moving it does");
            Assert.True(t.Max > t.Min && t.Step > 0 && t.FineStep > 0 && t.FineStep <= t.Step, $"{id}: unusable range or steps");
            Assert.True(Math.Pow(10, -t.Decimals) <= t.FineStep * 1.0001, $"{id}: {t.Decimals} decimals cannot show a fine step of {t.FineStep}");
            // The shipped value has to be captured at registration or RESET has nothing to go back to.
            Assert.False(double.IsNaN(t.Shipped), $"{id}: no shipped value was captured");
            Assert.InRange(t.Shipped, t.Min, t.Max);
        }
    }

    [Fact]
    public void AGlobalKnobMovesTheLawItNames()
    {
        GlobalTunable k = Knob("flutter.rateExponent");
        GlobalTuningSession.Set(k, 2.0);
        Assert.Equal(2.0, Flutter.RateExponent, 9);
        // And it is really the law, not a display: the time to failure at a fixed speed must move with it.
        double atTwo = Flutter.SecondsToFailure(1.25);
        GlobalTuningSession.Set(k, 1.0);
        double atOne = Flutter.SecondsToFailure(1.25);
        Assert.True(atTwo > atOne * 2.0, $"the exponent must change the law: {atTwo:F0} s at k=2 against {atOne:F0} s at k=1");
        GlobalTuningSession.Reset(k);
        Assert.Equal(k.Shipped, Flutter.RateExponent, 9);
    }

    [Fact]
    public void AGlobalIsRememberedAcrossALaunchAndAppliedExactlyOnce()
    {
        DeveloperAccess.RuntimeOverride = true;
        var store = new FakeStore();
        GlobalTuningSession.UseStore(store);
        GlobalTunable k = Knob("flutter.ceilingVd");
        double shipped = k.Shipped;
        GlobalTuningSession.Set(k, 1.45);
        Assert.Equal(1.45, Flutter.CeilingVd, 9);

        // "Relaunch": the static is back at its initialiser, the store still has the value.
        Flutter.CeilingVd = shipped;
        GlobalTuningSession.ResetForTest();
        GlobalTuningSession.UseStore(store);
        Assert.Equal(1.45, Flutter.CeilingVd, 9);

        // Attaching the same store again must not do anything a second time — and must not re-capture the
        // shipped value from the moved static, which is the failure RESET would have hidden.
        GlobalTuningSession.UseStore(store);
        Assert.Equal(1.45, Flutter.CeilingVd, 9);
        Assert.Equal(shipped, k.Shipped, 9);
        GlobalTuningSession.Reset(k);
        Assert.Equal(shipped, Flutter.CeilingVd, 9);
    }

    /// <summary>
    /// THE TRAP, HELD DIRECTLY: spawning aeroplanes must not touch a global. TuningSession.Apply runs on every
    /// spawn and captures each aeroplane's defaults as it goes; if the globals had been put on that path, each
    /// spawn would have re-captured the pilot's modified value as "shipped".
    /// </summary>
    [Fact]
    public void SpawningAeroplanesNeitherReappliesNorRecapturesAGlobal()
    {
        DeveloperAccess.RuntimeOverride = true;
        var store = new FakeStore();
        TuningSession.UseStore(store);
        GlobalTuningSession.UseStore(store);
        GlobalTunable k = Knob("flutter.armVd");
        double shipped = k.Shipped;
        GlobalTuningSession.Set(k, 1.33);

        AircraftConfig c = Load("pitts-s2b-like");
        for (int spawn = 0; spawn < 5; spawn++)
        {
            AircraftConfig fresh = Load("pitts-s2b-like");   // exactly what ResetFlight does
            TuningSession.Apply(fresh);
        }
        Assert.Equal(1.33, Flutter.ArmVd, 9);
        Assert.Equal(shipped, k.Shipped, 9);
        Assert.True(GlobalTuningSession.IsModified(k));
        GlobalTuningSession.Reset(k);
        Assert.Equal(shipped, Flutter.ArmVd, 9);
        Assert.False(GlobalTuningSession.IsModified(k));
        Assert.Equal(0, TuningSession.ModifiedCount(c));
    }

    /// <summary>A flight flown on a moved flutter law is not a flight on the shipped model, so the delta report —
    /// which is both what a tester pastes into a bug report and the header of a flight-test recording — has to say
    /// so even when the aeroplane itself is stock.</summary>
    [Fact]
    public void TheReportSaysAGlobalWasMovedEvenOnAStockAeroplane()
    {
        DeveloperAccess.RuntimeOverride = true;
        AircraftConfig c = Load("pitts-s2b-like");
        TuningSession.Apply(c);
        Assert.DoesNotContain("GLOBAL", TuningSession.DeltaReport(c), StringComparison.Ordinal);

        GlobalTuningSession.Set(Knob("flutter.rateExponent"), 2.5);
        string report = TuningSession.DeltaReport(c);
        Assert.Contains("GLOBAL flutter.rateExponent", report, StringComparison.Ordinal);
        Assert.Contains("stock", report, StringComparison.Ordinal);   // the aeroplane still is
    }

    /// <summary>A shipped build flies stock: with the Developer route closed a value saved during the beta must not
    /// follow a pilot into the public release. The same rule the per-aircraft session keeps.</summary>
    [Fact]
    public void AClosedDeveloperRouteFliesTheShippedGlobals()
    {
        DeveloperAccess.RuntimeOverride = true;
        var store = new FakeStore();
        GlobalTuningSession.UseStore(store);
        GlobalTunable k = Knob("flutter.fallbackInitiateVd");
        double shipped = k.Shipped;
        GlobalTuningSession.Set(k, 1.4);

        Flutter.FallbackInitiateVd = shipped;
        GlobalTuningSession.ResetForTest();
        DeveloperAccess.RuntimeOverride = false;
        GlobalTuningSession.UseStore(store);
        Assert.Equal(shipped, Flutter.FallbackInitiateVd, 9);
    }

    [Fact]
    public void GlobalIdsAreUniqueAndDoNotCollideWithPerAircraftKnobs()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (GlobalTunable t in GlobalTunableRegistry.All)
            Assert.True(seen.Add(t.Id), $"{t.Id} is registered twice; an id is the key its override is saved under");
        foreach (Tunable t in TunableRegistry.All)
            Assert.False(seen.Contains(t.Id), $"{t.Id} is both a per-aircraft and a global knob; the page could not tell them apart");
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(RepoRoot(), "configs", "aircraft", id + ".json"));
}

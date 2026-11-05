using System.Reflection;
using FlyingGame.Core;
using Xunit.Sdk;

[assembly: FlyingGame.FlightTests.LeakProbe]

namespace FlyingGame.FlightTests;

/// <summary>Opt-in: with FLIGHTTESTS_LEAK_LOG=/some/file, every test whose run changes the process-wide world (see
/// <see cref="SimGlobals"/>) is appended there with the before and after. This is how the 2026-11-04 leaks were found
/// (ModelRigTests leaving the island installed; Playground/Seaside leaving 44 solids, 927 boxes and 4 decks; the
/// tailwheel rollout leaving a 4 m/s wind). The after is taken BEFORE the class's Dispose runs, so a class that
/// resets in Dispose still shows up here: read the log against the Dispose methods. Off, it costs nothing.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class LeakProbe : BeforeAfterTestAttribute
{
    private static readonly string? Log = Environment.GetEnvironmentVariable("FLIGHTTESTS_LEAK_LOG");
    [ThreadStatic] private static string? _before;
    public override void Before(MethodInfo m) { if (Log != null) _before = SimGlobals.Describe(); }
    public override void After(MethodInfo m)
    {
        if (Log == null) return;
        string after = SimGlobals.Describe();
        if (after != _before) File.AppendAllText(Log, $"{m.DeclaringType?.Name}.{m.Name}\n   before {_before}\n   after  {after}\n");
    }
}

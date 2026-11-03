namespace FlyingGame.Core;

/// <summary>
/// Where the sim's time goes, per subsystem, for a device that cannot be sampled (the Quest build is not debuggable,
/// so simpleperf is refused). Scopes are inclusive (a scope's time includes anything timed inside it) and are
/// summed until <see cref="Reset"/>; the host prints <see cref="Top"/> once per report window.
///
/// Off, a scope is one thread-static read. On, it is two <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>
/// calls and allocates nothing. It records only on a thread that turned it on, so a worker building terrain meshes
/// on the same height field never adds into the main thread's numbers.
/// </summary>
public static class SimProfile
{
    public enum Scope
    {
        /// <summary>Aircraft.Step, the whole step (all of the below and the aero bookkeeping).</summary>
        Step,
        /// <summary>The air the step flies in: the regional field's four samples, or the uniform wind.</summary>
        Air,
        /// <summary>The RK4 integration: four force evaluations.</summary>
        Forces,
        /// <summary>The ground under the CG for ground effect (per force evaluation).</summary>
        GroundEffect,
        /// <summary>The strip aero model.</summary>
        Aero,
        /// <summary>Float / hull hydrodynamics.</summary>
        Floats,
        /// <summary>Landing gear: every wheel against the ground.</summary>
        Gear,
        /// <summary>Airframe hard points against the ground and solids (includes the clear-of-the-ground test).</summary>
        Contact,
        /// <summary>The five-sample "is the airframe clear of the ground" test inside <see cref="Contact"/>.</summary>
        ContactClear,
        /// <summary>Every hard point tested, because the airframe was within reach of the ground.</summary>
        ContactPoints,
        /// <summary>Structure, damage, tailwheel and lag bookkeeping after the integration.</summary>
        AfterStep,
        Count,
    }

    private static readonly string[] Names =
    {
        "step", "air", "forces", "groundEffect", "aero", "floats", "gear", "contact", "contactClear", "contactPoints", "afterStep",
    };

    [System.ThreadStatic] private static bool _on;
    private static readonly long[] Ticks = new long[(int)Scope.Count];
    private static readonly int[] Calls = new int[(int)Scope.Count];
    private static long _heightEvaluations, _heightQueries, _solidTests;

    /// <summary>Records on THIS thread from now on (the host's main thread turns it on).</summary>
    public static bool Enabled { get => _on; set => _on = value; }

    /// <summary>A timestamp to hand to <see cref="End"/>; 0 when off.</summary>
    public static long Begin() => _on ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

    public static void End(Scope s, long t0)
    {
        if (t0 == 0) return;
        Ticks[(int)s] += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        Calls[(int)s]++;
    }

    /// <summary>A height query that missed the exact-coordinate memo and evaluated the island description.</summary>
    public static void CountHeightEvaluation() { if (_on) _heightEvaluations++; }

    /// <summary>Any island height query, hit or miss.</summary>
    public static void CountHeightQuery() { if (_on) _heightQueries++; }

    public static double Ms(Scope s) => Ticks[(int)s] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    public static int CallsOf(Scope s) => Calls[(int)s];
    /// <summary>World solids asked "is this point inside you?" (<see cref="WorldSolids.Penetration"/>): 1,475 per hard
    /// point per RK4 stage before the footprint index, a handful after it.</summary>
    public static void CountSolidTests(int n) { if (_on) _solidTests += n; }

    public static long HeightEvaluations => _heightEvaluations;
    public static long SolidTests => _solidTests;
    public static long HeightQueries => _heightQueries;

    public static void Reset()
    {
        System.Array.Clear(Ticks, 0, Ticks.Length);
        System.Array.Clear(Calls, 0, Calls.Length);
        _heightEvaluations = 0; _heightQueries = 0; _solidTests = 0;
    }

    /// <summary>The <paramref name="n"/> costliest scopes, as milliseconds PER FRAME over <paramref name="frames"/>
    /// frames, then the height-field and solid work: "step 31.2, contact 22.0, contactPoints 21.4 ms/frame | height 1890 q
    /// 1720 eval, solids 260 /frame". Allocates (once per report).</summary>
    public static string Top(int n, int frames)
    {
        frames = System.Math.Max(1, frames);
        var order = new int[(int)Scope.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => Ticks[b].CompareTo(Ticks[a]));
        var sb = new System.Text.StringBuilder(160);
        int shown = 0;
        foreach (int i in order)
        {
            if (shown >= n || Ticks[i] == 0) break;
            if (shown > 0) sb.Append(", ");
            sb.Append(Names[i]).Append(' ').Append((Ms((Scope)i) / frames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            shown++;
        }
        if (shown == 0) sb.Append("nothing timed");
        sb.Append(" ms/frame | height ").Append(_heightQueries / frames).Append(" q ")
          .Append(_heightEvaluations / frames).Append(" eval, solids ").Append(_solidTests / frames).Append(" /frame");
        return sb.ToString();
    }
}

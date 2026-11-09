using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>What did it: the first half of a damage message.</summary>
public enum DamageCause { GroundContact, Tree, Water, Solid, Overstress, Gunfire }

/// <summary>
/// "Ground contact — bent both gear and struck the propeller." The cause and the condition, said once, briefly, as
/// the damage happens (Ty 2026-11-08: "if I hit hard and bend both gear and strike my prop it would say all three
/// then disappears then a while later I hit a tree and my wing comes off it would say lost your right wing").
///
/// The grouping is the point. One arrival raises a handful of separate events a few milliseconds apart — each gear
/// leg, the propeller, each panel — and reporting them one at a time would be a stutter of boxes saying almost the
/// same thing. They are collected for a moment and said together, in one sentence, with their shared cause.
///
/// Engine-agnostic and pure, so the phrasing can be read in a test rather than off a phone.
/// </summary>
public static class DamageReport
{
    /// <summary>How long events are gathered before the sentence is said (s). Long enough for one arrival's worth of
    /// breakage, short enough that the message still belongs to the bang you just heard.</summary>
    public const double GroupWindowS = 0.6;

    public static string CauseText(DamageCause c) => c switch
    {
        DamageCause.GroundContact => "Ground contact",
        DamageCause.Tree => "Hit a tree",
        DamageCause.Water => "Hit the water",
        DamageCause.Solid => "Hit something solid",
        DamageCause.Overstress => "Overstressed",
        DamageCause.Gunfire => "Hit by gunfire",
        _ => "Damage",
    };

    /// <summary>"a", "a and b", "a, b and c" — the way it would be said aloud.</summary>
    public static string JoinConditions(IReadOnlyList<string> parts)
    {
        if (parts == null || parts.Count == 0) return "";
        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return parts[0] + " and " + parts[1];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Count - 1; i++) { if (i > 0) sb.Append(", "); sb.Append(parts[i]); }
        sb.Append(" and ").Append(parts[^1]);
        return sb.ToString();
    }

    public static string Line(DamageCause cause, IReadOnlyList<string> conditions) =>
        conditions == null || conditions.Count == 0 ? CauseText(cause) : CauseText(cause) + " — " + JoinConditions(conditions);
}

/// <summary>
/// Gathers the damage of one moment into one sentence. Hand it events as they happen and ask it, each frame, whether
/// a line is ready; it answers once the window since the last event has passed, so a crash that goes on breaking
/// things keeps extending the same message instead of starting another.
/// </summary>
public sealed class DamageGrouper
{
    private readonly List<string> _conditions = new();
    private DamageCause _cause;
    private double _lastT = double.NegativeInfinity;
    private bool _open;

    /// <summary>Events further apart than this start a new message (s).</summary>
    public double WindowS { get; set; } = DamageReport.GroupWindowS;

    public bool Pending => _open;

    /// <summary>Note one thing that has just broken. A condition already in the open message is not repeated — a
    /// panel reports again every further five degrees it folds, and "bent your right wing" is true once.</summary>
    public void Add(double timeSec, DamageCause cause, string condition)
    {
        if (string.IsNullOrEmpty(condition)) return;
        if (_open && timeSec - _lastT > WindowS) Flush(out _);   // the last one was a different moment
        if (!_open) { _cause = cause; _conditions.Clear(); _open = true; }
        // A harder cause wins the sentence: hitting a tree and bending something in the same instant is the tree's doing.
        if (cause != _cause && Rank(cause) > Rank(_cause)) _cause = cause;
        if (!_conditions.Contains(condition)) _conditions.Add(condition);
        _lastT = timeSec;
    }

    /// <summary>The line, once the moment has closed. False while it is still gathering or there is nothing to say.</summary>
    public bool TryTake(double timeSec, out string line)
    {
        line = null;
        if (!_open || timeSec - _lastT <= WindowS) return false;
        return Flush(out line);
    }

    private bool Flush(out string line)
    {
        line = _conditions.Count > 0 ? DamageReport.Line(_cause, _conditions) : null;
        _conditions.Clear(); _open = false;
        return line != null;
    }

    public void Clear() { _conditions.Clear(); _open = false; _lastT = double.NegativeInfinity; }

    /// <summary>Which cause gets to name the moment when several arrive together.</summary>
    private static int Rank(DamageCause c) => c switch
    {
        DamageCause.Overstress => 5, DamageCause.Tree => 4, DamageCause.Solid => 3,
        DamageCause.Water => 2, DamageCause.GroundContact => 1, _ => 0,
    };
}

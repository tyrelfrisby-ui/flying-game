using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>How the flight ended, as far as the quip is concerned.</summary>
public enum CrashKind
{
    FlatSpin, StructuralFailure, StallSpinLow, MidAir, LowAerobatics, WingStrike, TreeStrike, ObstacleStrike,
    GroundLoop, GearUp, Bounced, TaxiCollision, HardLanding, NoseOver, PropStrike, Ditching, FuelExhaustion, Cfit,
}

/// <summary>
/// The corny line at the end of a bad day (Ty 2026-11-08). Written in the SPIRIT of the aviation films — the doomed
/// barnstormer, the deadpan disaster parody, the jet-jock wingman, the rocket-pack hero, the crooner's last set, the
/// child star's mocktail — and not in their words. Tropes and premises are nobody's property; dialogue is, so none of
/// these is a quotation and none is a near-miss of one.
///
/// Fatal crashes draw from their own, gentler set: a gag over a dead pilot reads very differently from one over a
/// bent gear leg, and the first time it happens it should not feel as though the game is laughing.
/// </summary>
public static class CrashQuips
{
    private static readonly Dictionary<CrashKind, string[]> Lines = new()
    {
        // Jet-jock: wingmen, the hard deck, the club upstairs. Also the test-pilot era's demon in the air.
        [CrashKind.FlatSpin] = new[]
        {
            "Save him a stool at the club upstairs.",
            "The spin took the aeroplane. The aeroplane took the rest.",
            "Nobody out-turns a flat spin. Nobody.",
            "He rode it all the way down, which is one way to arrive.",
            "Two turns in, the argument was already over.",
            "Rudder, aileron, prayer. In that order, and none of them worked.",
        },
        [CrashKind.StructuralFailure] = new[]
        {
            "He wrote a cheque his airspeed couldn't cash.",
            "Pushed the outside of the envelope. The envelope pushed back.",
            "The wings had an opinion about that pull.",
            "Somewhere, a demon in the air stopped laughing.",
            "The spar had been keeping score and it didn't like the number.",
            "Built to nine. Flown to rather more than nine.",
        },
        [CrashKind.StallSpinLow] = new[]
        {
            "The last turn is always the cheapest one to get wrong.",
            "Base to final: the oldest trap at every aerodrome in the world.",
            "He was almost home. Almost is the whole story.",
            "Thirty more knots would have cost nothing at all.",
        },
        [CrashKind.MidAir] = new[]
        {
            "Two in the air, one on the ramp, none in the logbook.",
            "Big sky, small aeroplanes, rotten luck.",
            "They found each other. Neither was looking.",
            "A formation of one, rather suddenly.",
        },
        // Barnstormer and rocket-pack: county fairs, crowds, hedges, 1930s showmanship.
        [CrashKind.LowAerobatics] = new[]
        {
            "The county fair wanted a show. It got one.",
            "He flew for the crowd. The crowd ran for the fence.",
            "Low, inverted, and briefly magnificent.",
            "The hat went round. So did the aeroplane.",
            "There is no such thing as a cheap thrill. The invoice just arrived.",
        },
        [CrashKind.WingStrike] = new[]
        {
            "A rocket pack would have been handy right about then.",
            "He buzzed the field. The field buzzed back.",
            "Wing-walking is easy. It's the wing that quits.",
            "Six inches is a lot when you haven't got it.",
        },
        [CrashKind.TreeStrike] = new[]
        {
            "The forest kept a souvenir.",
            "Timber. Followed by a great deal of aluminium.",
            "He went looking for a clearing a little late in the day.",
            "The oak was there first and intends to stay.",
        },
        [CrashKind.ObstacleStrike] = new[]
        {
            "Another aerodrome, another hedge, another hat passed round.",
            "The wires were there yesterday as well.",
            "Something solid disagreed with the flight plan.",
            "Helmet, goggles, scarf, and no depth perception whatsoever.",
        },
        // Deadpan parody: literal-minded calm in catastrophe.
        [CrashKind.GroundLoop] = new[]
        {
            "A full circle. Nobody asked for an encore.",
            "He landed, and then kept landing, sideways.",
            "One of those magnificent men. Less magnificent landing.",
            "The rudder was consulted. The rudder was ignored.",
        },
        [CrashKind.GearUp] = new[]
        {
            "All systems nominal, apart from the one holding the sky up.",
            "Three greens would have been three more than he had.",
            "The propeller has filed a complaint with the tarmac.",
            "A perfectly good landing, conducted at ground level throughout.",
        },
        [CrashKind.Bounced] = new[]
        {
            "Three landings for the price of one. None of them free.",
            "Up, down, up, down. Pick one.",
            "The runway was willing. The aeroplane was not.",
            "Each arrival improved on the last, which is not a compliment.",
        },
        [CrashKind.TaxiCollision] = new[]
        {
            "The aeroplane is fine. It's the ground that's totalled.",
            "Walking pace, and still expensive.",
            "The inflatable co-pilot has filed a grievance.",
            "Nobody has ever taxied into anything cheap.",
        },
        // Sweet and pink: the mocktail, the child star, the good ship.
        [CrashKind.HardLanding] = new[]
        {
            "Sweet, pink, and over far too quickly.",
            "That wasn't a landing. That was an arrival with paperwork.",
            "The gear did its best. Its best was brief.",
            "Two cherries, a splash of grenadine, and a bent leg.",
        },
        [CrashKind.NoseOver] = new[]
        {
            "On the good ship Lollipop, the gear was also optional.",
            "Tail up, nose down, dignity elsewhere.",
            "He inspected the propeller closely. With the propeller.",
            "Animal crackers in the soup, aeroplane in the hedge.",
        },
        [CrashKind.PropStrike] = new[]
        {
            "That expensive noise? That was money.",
            "The blades are now more of a suggestion.",
            "One careful owner, one careless inch.",
            "The engine would like a word, and so would the shop.",
        },
        // Crooner: last call, the band packing up, doing it one's own way.
        [CrashKind.Ditching] = new[]
        {
            "One for the road. The road came up to meet him.",
            "Everybody out, and mind the step.",
            "A seaplane, for exactly one landing.",
            "The water always wins. It simply takes its time.",
            "He wanted a drink. He got the whole lake.",
        },
        [CrashKind.FuelExhaustion] = new[]
        {
            "That's the last call, and the band has packed up.",
            "He glided beautifully. It was never the plan.",
            "The tanks had been honest with him the entire time.",
            "Fuel is the one thing you cannot talk your way out of.",
        },
        [CrashKind.Cfit] = new[]
        {
            "He did it his way. The way involved a hedge.",
            "Start spreading the wreckage.",
            "The mountain had been there for some considerable while.",
            "Flying is easy. It's the scenery that gets you.",
        },
    };

    /// <summary>
    /// When the pilot does not walk away. NOT JOKES — Ty 2026-11-08: "fatal never a joke just a fitting tribute".
    /// No punchlines, no winks, nothing that invites a smirk. An earlier draft of this list had two lines with a
    /// smile in them and they were wrong: the first time a player kills a pilot, the game should not be amused.
    /// </summary>
    private static readonly string[] Fatal =
    {
        "Blue sky all the way down.",
        "The hangar is quieter tonight.",
        "Last flight, logged.",
        "Clear skies, and a tailwind.",
        "He flew. Not everyone does.",
        "A pilot's end, at a pilot's altitude.",
        "The aerodrome will remember him.",
        "Wheels up, for good.",
        "One more name on the hangar wall.",
        "Fair winds. The circuit is complete.",
        "He is off the frequency now.",
        "The sky keeps its own.",
    };

    public static IReadOnlyList<string> For(CrashKind kind) => Lines.TryGetValue(kind, out string[] v) ? v : System.Array.Empty<string>();
    public static IReadOnlyList<string> FatalLines => Fatal;

    /// <summary>
    /// THE ONLY WAY A LINE SHOULD BE CHOSEN. A dead pilot takes the tribute set and nothing else — not the tribute
    /// AND the gag, not the gag with a softer preface. Making that a rule of the function rather than of the caller
    /// is the point: there is no way to reach a joke from here with <paramref name="outcome"/> set to Dead.
    /// </summary>
    public static string Pick(QuipBag bag, CrashKind kind, LandingOutcome outcome) =>
        outcome == LandingOutcome.Dead
            ? bag.Next("fatal", Fatal)
            : bag.Next(kind.ToString(), For(kind));
    public static int Count { get { int n = Fatal.Length; foreach (var v in Lines.Values) n += v.Length; return n; } }
}

/// <summary>
/// Hands out lines without repeating itself. Ty 2026-11-08: "keep the list long and random so people don't see the
/// same thing two times in a row."
///
/// Plain random does not do that — with six lines it repeats immediately about one time in six, which is exactly the
/// case anyone notices. This is a shuffled bag instead: every line in a set is used once before any is used again,
/// and the shuffle of the next bag never puts the line that just played at the front, so there is no repeat across
/// the join either. Each set keeps its own bag, so crashing twice the same way still walks the whole list.
/// </summary>
public sealed class QuipBag
{
    private readonly Dictionary<string, List<string>> _bags = new();
    private readonly Dictionary<string, string> _last = new();
    private readonly System.Random _rng;

    public QuipBag(int seed = 0) { _rng = seed == 0 ? new System.Random() : new System.Random(seed); }

    /// <summary>The next line from a set, or null if the set is empty.</summary>
    public string Next(string setKey, IReadOnlyList<string> all)
    {
        if (all == null || all.Count == 0) return null;
        if (all.Count == 1) return all[0];
        if (!_bags.TryGetValue(setKey, out List<string> bag) || bag.Count == 0) bag = Refill(setKey, all);
        string pick = bag[^1];
        bag.RemoveAt(bag.Count - 1);
        _last[setKey] = pick;
        return pick;
    }

    private List<string> Refill(string setKey, IReadOnlyList<string> all)
    {
        var bag = new List<string>(all);
        for (int i = bag.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
        // The bag is drawn from the END, so the line that just played must not be sitting there.
        if (_last.TryGetValue(setKey, out string last) && bag.Count > 1 && bag[^1] == last)
            (bag[^1], bag[0]) = (bag[0], bag[^1]);
        _bags[setKey] = bag;
        return bag;
    }
}

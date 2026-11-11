using System;
using System.Collections.Generic;
using System.Text;

namespace FlyingGame.Core;

/// <summary>
/// ONE graphics-quality scale, and the table of what every expensive subsystem is allowed to spend at each step of
/// it. Ty 2026-11-11: "We need a graphics slider to account for these costs where a user can downgrade for higher
/// performance and it auto downgrades from the users own setting with a warning when graphics would compromise the
/// flight model or frame rate etc".
///
/// There were three quality ideas in the tree before this file and none of them knew about the others:
/// <see cref="Tier"/> replaces all three.
/// <list type="bullet">
/// <item>WaterReflection's own <c>ReflectionQuality { Low, Balanced, High }</c> plus a texture-size override.</item>
/// <item>Unity's <c>QualitySettings.GetQualityLevel()</c>, read ad hoc and compared against hand-written numbers
/// (<c>&lt;= 1</c> in PlaygroundBuilder, <c>&lt;= 2</c> and <c>&gt; 2</c> a dozen times in UrbanFacades) — the level
/// nothing in the game ever set, so every one of those branches was really reading the editor's default.</item>
/// <item>Per-subsystem constants with no scale at all (wake grid, spray parcels, water-shadow mask size).</item>
/// </list>
/// This project has already lost nights to one question having two implementations — the rig and the sim flying
/// different propellers — so the rule here is that there is one answer: <see cref="Knobs.For"/>.
///
/// Pure: no Unity. The bridge reads a <see cref="Knobs"/> and applies it; the policy, the state machine
/// (<see cref="AutoQuality"/>) and the wording of the warning are all testable headless.
/// </summary>
public static class GraphicsQuality
{
    /// <summary>
    /// The scale, lowest first, so <c>&lt;</c> and <c>Math.Min</c> mean what they look like. Four steps, because the
    /// three the water already had could not express "last resort": <see cref="Tier.Minimum"/> is where the automatic
    /// downgrade is allowed to put a device that cannot carry the flight model, and no pilot would choose it.
    /// </summary>
    public enum Tier
    {
        /// <summary>Last resort. Everything optional is off; the flight model, the instruments and the terrain stay.</summary>
        Minimum = 0,
        /// <summary>A phone that is working hard. Reflections and water shadow at their smallest, clouds close in.</summary>
        Low = 1,
        /// <summary>The shipped default on a handheld and in a headset.</summary>
        Balanced = 2,
        /// <summary>Everything the game draws today. A High build must look exactly like the build before this file.</summary>
        High = 3,
    }

    /// <summary>The lowest the automatic downgrade may ever go, and the lowest a user may pick.</summary>
    public const Tier Floor = Tier.Minimum;
    /// <summary>The highest tier that exists.</summary>
    public const Tier Ceiling = Tier.High;

    public const string PrefKey = "set.graphicsTier";

    /// <summary>Which machine this is. The tiers are the same everywhere; what differs is the shipped default and the
    /// frame time the automatic downgrade is measured against.</summary>
    public enum Platform
    {
        /// <summary>iPhone / iPad.</summary>
        Handheld = 0,
        /// <summary>Quest.</summary>
        Headset = 1,
        /// <summary>Mac / editor / the TV companion's renderer.</summary>
        Desktop = 2,
    }

    /// <summary>What a fresh install starts on. Balanced on a device — a first flight that stutters is a first flight
    /// that gets deleted — and High on a desktop, where the measurements in docs/ were all taken.</summary>
    public static Tier DefaultFor(Platform p) => p == Platform.Desktop ? Tier.High : Tier.Balanced;

    /// <summary>The frame time the automatic downgrade calls "slow", in seconds. A headset is pinned to its display
    /// (72 Hz = 13.9 ms and the compositor reprojects anything slower); a handheld is judged against 30 fps, which is
    /// the floor the touch build has always aimed at, not against 60.</summary>
    public static double TargetFrameSec(Platform p, double displayHz) =>
        p == Platform.Headset && displayHz > 1.0 ? 1.0 / displayHz : 1.0 / 30.0;

    public static string Label(Tier t) => t switch
    {
        Tier.Minimum => "MINIMUM",
        Tier.Low => "LOW",
        Tier.Balanced => "BALANCED",
        _ => "HIGH",
    };

    public static Tier Clamp(Tier t) => t < Floor ? Floor : t > Ceiling ? Ceiling : t;

    /// <summary>One step down, stopping at <see cref="Floor"/>.</summary>
    public static Tier Down(Tier t) => t <= Floor ? Floor : t - 1;
    /// <summary>One step up, stopping at <paramref name="ceiling"/> — which is the USER'S choice, never
    /// <see cref="Ceiling"/>. The automatic machine may only lower a floor it set itself.</summary>
    public static Tier Up(Tier t, Tier ceiling) => t >= ceiling ? ceiling : t + 1;

    /// <summary>
    /// What each subsystem may spend at a tier. Everything here is read by the bridge; nothing here is read by the
    /// physics. A knob that costs its time when the flight STARTS (mesh building, a bake, a seeded field) is marked in
    /// its comment, because the automatic downgrade cannot undo one of those in flight: see
    /// <see cref="Diff.NextFlightOnly"/>.
    /// </summary>
    public readonly struct Knobs
    {
        public readonly Tier Tier;

        /// <summary>The Unity quality level to put <c>QualitySettings</c> on, so its own shadows / anti-aliasing / LOD
        /// bias follow the one scale instead of sitting wherever the editor left them. The project's six levels are
        /// Very Low, Low, Medium, High, Very High, Ultra; the four tiers take 0, 1, 2, 3. The existing hand-written
        /// comparisons keep their meaning: <c>&lt;= 1</c> is Minimum and Low, <c>&gt; 2</c> is High alone.</summary>
        public readonly int UnityLevel;

        // ---- water -------------------------------------------------------------------------------------------
        /// <summary>Planar reflection target, square edge in pixels before the aspect split; 0 = no reflection camera
        /// at all. STEADY STATE, and the most elastic thing on the list: the cost is a second full scene draw at this
        /// resolution, every frame the aeroplane is over water. Wired: <c>WaterReflection.TextureSize</c>.</summary>
        public readonly int ReflectionTextureSize;
        /// <summary>Whether terrain and shoreline go into the reflection, or only the aircraft and the water-side
        /// props. STEADY STATE; this is the draw-call half of the reflection's cost, separate from its fill.
        /// Wired: <c>WaterReflection.ReflectScenery</c>.</summary>
        public readonly bool ReflectScenery;
        /// <summary>Cells across the wake height field — the GPU wave-equation solver's five textures are this many
        /// across the lake's long axis, with a 2 m floor on the cell. STEADY STATE, quadratic in this number, plus a
        /// one-off allocation hitch on first touching a lake. Wired: <c>WakeWaves.AllocateLake</c>.</summary>
        public readonly int WakeGridCells;

        // ---- shadow ------------------------------------------------------------------------------------------
        /// <summary>Height above which the airframe's planar ground shadow is not drawn, in metres; 0 = no shadow.
        /// STEADY STATE: below it the shadow is one <c>Graphics.DrawMesh</c> per airframe submesh per frame — the
        /// forty-odd extra draw calls — and it is a known per-frame allocation source on the Quest. Wired:
        /// <c>GroundShadow.MaxAltitudeM</c> (1500 today).</summary>
        public readonly double GroundShadowMaxAltitudeM;

        // ---- sky ---------------------------------------------------------------------------------------------
        /// <summary>Impostor lobes per cloud bank. STARTUP: the lobes are geometry made when the playground is built,
        /// so lowering this in flight only takes effect on the next flight, where the cull radius takes effect at
        /// once. Wired: <c>CloudPlayground</c>'s horseshoe bank (20 today).</summary>
        public readonly int CloudBankLobes;
        /// <summary>Banks further than this from the eye are not drawn, in metres. STEADY STATE, and the one cloud
        /// knob that is pure fill: the measured 11.6x overdraw of a 3 km cruise view is what this buys back. Wired:
        /// <c>CloudPlayground</c>'s cluster cull (19 km today).</summary>
        public readonly double CloudCullRadiusM;
        /// <summary>How far whole ground-fog sites are drawn, in metres; 0 = none. STEADY STATE, fill-rate bound.
        /// Wired: <c>GroundFog.DrawRangeM</c> (14 km today).</summary>
        public readonly double GroundFogDrawRangeM;

        // ---- scenery (all STARTUP: built into meshes when the world is built) ----------------------------------
        /// <summary>Along-lake spacing of the canyon-wall ribbon, metres — BIGGER IS CHEAPER, the only knob here that
        /// runs the other way. Wired: <c>PlaygroundBuilder.BuildPowellWalls</c> (12 m, or 24 m when the old code read
        /// Unity level ≤ 1).</summary>
        public readonly double CanyonWallStepM;
        /// <summary>Urban props at their detailed counts: roof chimneys and detailed yards. Wired THROUGH
        /// <see cref="UnityLevel"/> — <c>UrbanFacades</c>'s <c>&gt; 2</c> branches — and named here so the table says
        /// what that level means instead of leaving it to be rediscovered.</summary>
        public readonly bool DetailedScenery;
        /// <summary>Street traffic, parked cars and lamps at their dense counts. Wired THROUGH
        /// <see cref="UnityLevel"/>: <c>UrbanFacades</c>'s <c>&lt;= 1</c> branches, inverted.</summary>
        public readonly bool UrbanTraffic;

        private Knobs(Tier tier, int unityLevel, int reflectionTextureSize, bool reflectScenery, int wakeGridCells,
                      double groundShadowMaxAltitudeM, int cloudBankLobes, double cloudCullRadiusM,
                      double groundFogDrawRangeM, double canyonWallStepM, bool detailedScenery, bool urbanTraffic)
        {
            Tier = tier; UnityLevel = unityLevel;
            ReflectionTextureSize = reflectionTextureSize; ReflectScenery = reflectScenery;
            WakeGridCells = wakeGridCells;
            GroundShadowMaxAltitudeM = groundShadowMaxAltitudeM;
            CloudBankLobes = cloudBankLobes; CloudCullRadiusM = cloudCullRadiusM;
            GroundFogDrawRangeM = groundFogDrawRangeM;
            CanyonWallStepM = canyonWallStepM;
            DetailedScenery = detailedScenery; UrbanTraffic = urbanTraffic;
        }

        /// <summary>
        /// THE TABLE. Every number in the High column is what the code does TODAY, taken from the call site named on
        /// the field, so nothing is invented and nothing is lost by adopting this scale; every lower tier is a
        /// subtraction from it. EVERY knob here is wired to that call site — a knob in this table that nothing reads
        /// would be a third quality concept beside the two this file replaced, which is the failure it exists to
        /// prevent. The ones still to wire (spray parcel counts, wisp sheets, the cloud banks' own count, the wake
        /// solver's rate, and lane/claude-clouds' depth pre-pass) are deliberately absent until they are.
        ///
        /// The one place today's build is not the High column is the water reflection, and that is because today's
        /// build never chose: <c>SceneBootstrap</c> added <c>WaterReflection</c> and left its fields at
        /// <c>Balanced / 768 / scenery on</c>, so <see cref="Tier.Balanced"/> — the shipped default tier — is the
        /// build a pilot has been flying, and High is the 1536 the editor's capture tool has always used.
        /// </summary>
        public static Knobs For(Tier tier) => Clamp(tier) switch
        {
            //                       tier           lvl  refl scenery  wake shadow lobes   cull    fog  wall  detail traffic
            Tier.Minimum => new Knobs(Tier.Minimum,  0,     0, false,   128,     0,    6,  4000,     0, 48.0, false, false),
            Tier.Low     => new Knobs(Tier.Low,      1,   384, false,   256,   300,   10,  8000,  4000, 24.0, false, false),
            Tier.Balanced=> new Knobs(Tier.Balanced, 2,   768,  true,   512,  1500,   14, 14000,  9000, 12.0, false,  true),
            _            => new Knobs(Tier.High,     3,  1536,  true,  1024,  1500,   20, 19000, 14000, 12.0,  true,  true),
        };

        /// <summary>The reflection camera runs at all.</summary>
        public bool ReflectionOn => ReflectionTextureSize > 0;
        public bool GroundShadowOn => GroundShadowMaxAltitudeM > 0;
    }

    /// <summary>Why the automatic downgrade fired. Not interchangeable: one of these is the flight model losing time
    /// and the other is the picture being uncomfortable, and the pilot is told which.</summary>
    public enum Reason
    {
        /// <summary>The sim's catch-up was capped: simulated time was DISCARDED and the aeroplane flew a trajectory
        /// physics did not produce. The primary trigger.</summary>
        SimCapped = 0,
        /// <summary>Frames were long but the sim kept up. A comfort signal, secondary.</summary>
        FrameRate = 1,
    }

    /// <summary>What changed between two tiers, in words a pilot can act on.</summary>
    public readonly struct Diff
    {
        /// <summary>Phrases for what was given up, worst first ("reflections off", "clouds drawn closer in").
        /// Empty when nothing visible changed.</summary>
        public readonly IReadOnlyList<string> Lost;
        /// <summary>True when some of what changed is built when the flight starts, so this flight keeps it and the
        /// next one does not. The warning says so rather than promising a saving that will not arrive.</summary>
        public readonly bool NextFlightOnly;

        internal Diff(IReadOnlyList<string> lost, bool nextFlightOnly) { Lost = lost; NextFlightOnly = nextFlightOnly; }
    }

    /// <summary>What a move from <paramref name="from"/> to <paramref name="to"/> takes away (or gives back, when the
    /// move is upward — the phrases read the same either way round, so the caller supplies the verb).</summary>
    public static Diff Describe(Tier from, Tier to)
    {
        Knobs a = Knobs.For(from), b = Knobs.For(to);
        // Two groups, because a warning that names only its first few items must name the ones a pilot would notice:
        // something switched OFF outranks the same thing merely reduced, however expensive the reduction was. Within
        // a group the order is by measured cost — the clouds were +1.68 ms and 11.6x overdraw in a 3 km cruise view,
        // and the reflection is a second full scene render while over water.
        bool next = false;

        string? refl = a.ReflectionOn && !b.ReflectionOn ? "water reflections off"
                     : a.ReflectScenery && !b.ReflectScenery ? "water reflects only the aeroplane"
                     : b.ReflectionTextureSize != a.ReflectionTextureSize ? "water reflections reduced" : null;
        string? wake = b.WakeGridCells != a.WakeGridCells ? "wake waves coarser" : null;
        string? shadow = a.GroundShadowOn && !b.GroundShadowOn ? "the aeroplane's shadow off"
                       : b.GroundShadowMaxAltitudeM != a.GroundShadowMaxAltitudeM ? "the shadow fades lower down" : null;
        string? fog = a.GroundFogDrawRangeM > 0 && b.GroundFogDrawRangeM <= 0 ? "ground fog off"
                    : b.GroundFogDrawRangeM != a.GroundFogDrawRangeM ? "less ground fog" : null;
        string? cull = b.CloudCullRadiusM != a.CloudCullRadiusM ? "clouds drawn closer in" : null;
        string? lobes = b.CloudBankLobes != a.CloudBankLobes ? "clouds simpler" : null;
        if (lobes != null) next = true;
        string? scenery = b.DetailedScenery != a.DetailedScenery || b.UrbanTraffic != a.UrbanTraffic
                       || b.CanyonWallStepM != a.CanyonWallStepM ? "less scenery detail" : null;
        if (scenery != null) next = true;

        // Dearest first within each group: clouds (+1.68 ms, 11.6x overdraw measured), then the reflection (a second
        // full scene render over water), then the rest.
        var ordered = new[] { cull, lobes, refl, wake, fog, shadow, scenery };
        var lost = new List<string>(ordered.Length);
        foreach (string? s in ordered) if (s != null && IsOff(s)) lost.Add(s);
        foreach (string? s in ordered) if (s != null && !IsOff(s)) lost.Add(s);
        return new Diff(lost, next && lost.Count > 0);
    }

    /// <summary>A phrase that says something stopped being drawn at all, rather than being drawn less.</summary>
    private static bool IsOff(string phrase) =>
        phrase.EndsWith(" off", StringComparison.Ordinal) || phrase == "water reflects only the aeroplane";

    /// <summary>
    /// The warning, in full. Requirement, Ty 2026-11-11: it names what was given up AND why, not a generic icon —
    /// "reflections reduced: the simulator was not keeping up".
    ///
    /// <paramref name="discardedSec"/> is the simulated time the catch-up cap threw away over the window that
    /// triggered this (see <see cref="FlyingGame.Core.SimCatchUp"/> for what the cap does). A pilot deserves that
    /// number: in a spin or a landing flare the difference between half a second of discarded physics and none is the
    /// difference between a report that means something and one that does not.
    /// </summary>
    public static string Warning(Tier from, Tier to, Reason why, double discardedSec, double meanFrameSec)
    {
        Diff d = Describe(from, to);
        var sb = new StringBuilder();
        if (d.Lost.Count == 0) sb.Append("Graphics set to ").Append(Label(to));
        else
        {
            // A drop of a whole tier changes eight things and a pilot will not read eight. Name the three dearest
            // (Describe orders them by cost) and count the rest; the OPTIONS page has the full table.
            const int MaxNamed = 3;
            int named = d.Lost.Count <= MaxNamed + 1 ? d.Lost.Count : MaxNamed;
            sb.Append(char.ToUpperInvariant(d.Lost[0][0])).Append(d.Lost[0], 1, d.Lost[0].Length - 1);
            for (int i = 1; i < named; i++) sb.Append(i == named - 1 && named == d.Lost.Count ? " and " : ", ").Append(d.Lost[i]);
            if (named < d.Lost.Count) sb.Append(" and ").Append(d.Lost.Count - named).Append(" more");
        }
        sb.Append(": ");
        if (why == Reason.SimCapped)
        {
            sb.Append("the simulator was not keeping up");
            if (discardedSec > 0.0005)
                sb.Append(" — the flight model lost ").Append(FormatSec(discardedSec)).Append(" of flying");
            sb.Append('.');
        }
        else
        {
            sb.Append("the frame rate was low");
            if (meanFrameSec > 0.0005) sb.Append(" (").Append(Math.Round(1.0 / meanFrameSec)).Append(" fps)");
            sb.Append('.');
        }
        sb.Append(" Graphics ").Append(Label(to)).Append('.');
        if (d.NextFlightOnly) sb.Append(" Some of this takes effect on the next flight.");
        return sb.ToString();
    }

    private static string FormatSec(double s) =>
        s < 0.1 ? (s * 1000.0).ToString("0") + " ms" : s.ToString("0.0") + " s";
}

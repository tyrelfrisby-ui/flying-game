using System;
using System.Collections.Generic;

namespace FlyingGame.Core
{
    /// <summary>
    /// ONE LIVERY PER TYPE ON THE PHONE (Ty 2026-11-10). Offered four ways to get the iOS build back under App Store
    /// Connect's 4 GB limit, he chose:
    ///
    ///     "Ship a single default livery for each aeroplane; the alternates stay in desktop and Quest builds."
    ///
    /// WHAT ACTUALLY COSTS THE GIGABYTES, measured rather than assumed. The whole <c>unity/Assets</c> tree is 265 MB on
    /// disk and the build is 4.29 GB, so the on-disk size of a GLB tells you almost nothing. Every texture embedded in a
    /// GLB is imported by glTFast's ScriptedImporter, which means Unity's platform texture pipeline never sees it: there
    /// is no compression field in the .glb.meta at all, and the pixels ship as uncompressed 4 bytes each with a full mip
    /// chain. Counting every embedded image in all 39 GLBs gives 754.92 Mpix; 754.92e6 x 4 x 4/3 = 4,026,240,000 B
    /// against the recorded <c>Data/resources.assets.resS</c> of 4,024,558,864 B for build 2026.1105.1550 — 0.04 % out.
    /// So resS, 93.8 % of that build, is to within rounding NOTHING BUT GLB TEXTURES. It is not audio (1.1 MB in the
    /// whole tree), not video (none), not fonts, not the configs (StreamingAssets is 1.4 MB), and not meshes
    /// (resources.assets, 166 MB). Ty's instinct was right about the remedy for the right reason: the alternate liveries
    /// are the same aeroplane with the paint baked in again, so each one carries a FULL DUPLICATE texture set.
    ///
    /// THE ALTERNATES, at 4 B/px x 4/3 (measured per GLB):
    ///   extra-300-like   -own (Talk Flying) 479 MB, -own-n300uy 479 MB      (default -own-unmarked, 479 MB, stays)
    ///   pitts-s2b-like   -own (N31DG) 247, -own-n1191 247, -own-factory-unmarked 247
    ///                                                                      (default -own-checker-unmarked, 281, stays)
    ///   great-lakes-like -own-red-sunburst 171, -own-white-stripes 171      (default -own, 244, stays)
    ///   glider-2-33-like -own (plain factory yellow) 86                     (default -own-tf, 108, stays)
    ///   grob103          -own-tf 6                                         (default -own, 6, stays)
    ///   TOTAL HELD OUT OF A PHONE BUILD: 2.13 GB of a 4.03 GB texture payload.
    ///
    /// PACKAGING ONLY. Nothing is deleted and no art is authored twice: BuildScript.BuildiOS moves the alternates' GLBs
    /// out of Resources for the length of the player build (AssetDatabase.MoveAsset, so GUIDs, metas and imports
    /// survive) and puts them back in <c>finally</c>, and compiles the player with the <see cref="Define"/> scripting
    /// define, under which <see cref="LiveryChoice"/> offers the default alone. Mac and Quest builds do neither and are
    /// bit-for-bit what they were.
    ///
    /// PARITY (docs/PARITY.md). This is a build-time flag, which rule 2 allows, and NOT a fork: one GLB and one config
    /// still make every platform's art, the phone merely ships a subset of files that are authored, tracked and loaded
    /// the same everywhere. The subset is DERIVED from <see cref="LiveryChoice"/> rather than listed here, so a livery
    /// added to the picker cannot be forgotten by the packager and the phone's default cannot drift from the
    /// headset's. The precedent is <see cref="TestFlightFleet"/>, which does the same thing a folder at a time and
    /// lives on both lanes.
    /// </summary>
    public static class PhoneLiveries
    {
        /// <summary>The player-build scripting define: the phone has one GLB per type, so the picker offers one scheme.</summary>
        public const string Define = "FG_PHONE_ONE_LIVERY";

        /// <summary>Set to "1" to build iOS with EVERY livery anyway — a capture or render build on a phone, where
        /// AERO_LIVERY has to be able to reach the alternates. It puts the build back over 4 GB, so it is never how
        /// TestFlight is fed. Default (unset) is Ty's decision: one livery per type.</summary>
        public const string EnvVar = "FLYINGGAME_PHONE_ALL_LIVERIES";

        /// <summary>True in a player compiled with <see cref="Define"/> — the build this runtime is inside has only the
        /// default livery's GLB for each type, so nothing may ask for another.</summary>
        public static bool OneLiveryPerType =>
#if FG_PHONE_ONE_LIVERY
            true;
#else
            false;
#endif

        /// <summary>
        /// THE GLB SUFFIX THE PHONE KEEPS for a type: the one the game loads with no override and no saved choice —
        /// <see cref="LiveryChoice.Resolve"/> of nothing, through <see cref="LiveryChoice.OwnSuffix"/>. Derived, never
        /// listed, so the packager and the picker cannot disagree.
        ///
        /// This has to give the SAME answer in the editor (where <see cref="Define"/> is not set, so Resolve returns the
        /// type's first option) and in the player (where it is, so For() has one option, HasChoice is false and Resolve
        /// returns <see cref="LiveryChoice.FleetDefault"/>). It does, because every type's OwnSuffix sends its first
        /// option and "type" to the same GLB — <c>PhoneLiveryTests.TheKeptLiveryIsTheSameWithOrWithoutTheDefine</c>
        /// holds that, and it is the one thing that would silently ship a build with no aeroplane in it.
        /// </summary>
        public static string KeptSuffix(string modelId) =>
            LiveryChoice.OwnSuffix(modelId, LiveryChoice.Resolve(modelId, null, null));

        /// <summary>
        /// Does a phone build keep this file? <paramref name="folder"/> is a <c>Resources/Models</c> subfolder and
        /// <paramref name="fileName"/> a file in it.
        ///
        /// Only a GLB of OUR OWN model in a named livery is ever held out: <c>&lt;folder&gt;-own*.glb</c> whose suffix is
        /// not <see cref="KeptSuffix"/>. Everything else stays — the downloaded model (<c>&lt;folder&gt;.glb</c>, which is
        /// what most types draw with the "own models" option off), the .obj meshes, the loose textures the 172's own
        /// GLB reads, and every .meta. Held-out files are a tiny, explicit set and a type that has only one GLB loses
        /// nothing at all.
        /// </summary>
        public static bool KeepsModelFile(string? folder, string? fileName)
        {
            if (folder == null || fileName == null) return true;
            if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return true;
            string stem = fileName.Substring(0, fileName.Length - 4);
            string ownPrefix = folder + "-own";
            if (!stem.StartsWith(ownPrefix, StringComparison.Ordinal)) return true;   // not one of ours, or not a livery
            return string.Equals(stem, folder + KeptSuffix(folder), StringComparison.Ordinal);
        }

        /// <summary>Every type with more than one scheme in the picker, for the build log and the tests.</summary>
        public static IReadOnlyList<string> TypesWithAlternates()
        {
            var list = new List<string>();
            foreach (string id in LiveryChoice.TypesWithOptions) list.Add(id);
            return list;
        }
    }
}

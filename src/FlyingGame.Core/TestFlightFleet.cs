using System;
using System.Collections.Generic;

namespace FlyingGame.Core
{
    /// <summary>
    /// THE TESTFLIGHT SLIM PACKAGE (Ty 2026-11-05 18:05): a TestFlight-only iOS build whose only aeroplanes are the
    /// 2-33, the J-3 on wheels and on floats, the Pitts, the Extra and the Great Lakes. Build 2026.1105.1550 was 4.29 GB
    /// uncompressed (Data/resources.assets.resS alone 3.93 GB: every Resources/Models folder ships, used or not), over
    /// App Store Connect's 4 GB limit.
    ///
    /// PACKAGING ONLY, OPT-IN. Nothing is deleted from the tree. The slim build is made by
    /// <c>FLYINGGAME_TF_SLIM=1 zsh tools/deploy/deploy.sh</c>: BuildScript.BuildiOS then moves every other
    /// <c>Resources/Models/&lt;folder&gt;</c> out of Resources for the length of the player build (AssetDatabase.MoveAsset,
    /// so GUIDs and imports survive) and puts it back in <c>finally</c>, and compiles the player with the
    /// <see cref="Define"/> scripting define, under which SessionSettings.Fleet is <see cref="Filter"/>ed. Without the
    /// variable (every normal iOS, Mac and Quest build) neither happens.
    ///
    /// The list is the same six types as the headset's (Vr.QuestFleet.Listed), kept separately because the two lists
    /// answer different questions and may part later. Configs (StreamingAssets/aircraft, 1.4 MB for all 35) still ship
    /// whole: the 2-33's aerotow tug (Pawnee), the combat zone's DC-3 and drones and the dogfight's AI read theirs.
    /// </summary>
    public static class TestFlightFleet
    {
        /// <summary>The player-build scripting define of the slim package.</summary>
        public const string Define = "FG_TF_SLIM";

        /// <summary>The environment variable that asks BuildScript.BuildiOS for the slim package ("1").</summary>
        public const string EnvVar = "FLYINGGAME_TF_SLIM";

        /// <summary>The types the slim package offers, in the fleet's order of preference (the first is the default).</summary>
        public static readonly IReadOnlyList<string> Listed = new[]
        {
            "glider-2-33-like",    // SGS 2-33 (yellow, our own GLB)
            "j3-cub-like",         // J-3 Cub on wheels
            "j3-cub-floats-like",  // J-3 Cub on EDO floats
            "pitts-s2b-like",      // Pitts S-2B
            "extra-300-like",      // Extra 300
            "great-lakes-like",    // Great Lakes 2T-1A-2
        };

        /// <summary>Resources/Models folders that are not an aeroplane type and every listed type needs: the pilot's
        /// body and his bailout gear (VrPilotBody, VrCockpitHands.Bailout) and the sequence card.</summary>
        public static readonly IReadOnlyList<string> SharedModelFolders = new[] { "pilot-body", "pilot-gear", "sequence-card" };

        public static bool IsListed(string? aircraftId) => aircraftId != null && Contains(Listed, aircraftId);

        /// <summary>Does the slim package keep this Resources/Models folder? Each type's models live in a folder named
        /// for its id (AirframeModels: Models/&lt;id&gt;/&lt;id&gt;[-own…]); none of the listed types borrows another's.</summary>
        public static bool KeepsModelFolder(string? folder) => folder != null && (Contains(Listed, folder) || Contains(SharedModelFolders, folder));

        /// <summary>The fleet the slim package shows: <paramref name="fleet"/> in its own order, listed types only.</summary>
        public static (string id, string name)[] Filter((string id, string name)[] fleet)
        {
            var list = new List<(string, string)>();
            foreach (var f in fleet) if (IsListed(f.id)) list.Add(f);
            return list.ToArray();
        }

        /// <summary>The chosen type if it is listed, otherwise the first listed one.</summary>
        public static string Resolve(string? aircraftId) => IsListed(aircraftId) ? aircraftId! : Listed[0];

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}

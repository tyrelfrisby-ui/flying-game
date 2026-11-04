using System;
using System.Collections.Generic;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// WHICH AEROPLANES A HEADSET OFFERS. Ty on the Quest 3S, 2026-11-02: the 2-33 and the J-3 "spawn with the eye
    /// inside geometry". In a headset the cockpit is the whole picture — there are no flat dials to cover for it and
    /// the pilot's head is where the eye point puts it — so a type is listed only when the game DRAWS one of our own
    /// models with a designed interior for it and its approved eye (docs/models/eyepoints.json, carried into
    /// configs loading.seats[].eye) sits clear of that model's skin. tools/own_models/check_eye_interiors.py measures
    /// the second part from the committed GLBs and configs, and fails if any type below does not pass.
    ///
    /// Why the two failed: neither was drawing a model with a cockpit. The 2-33's default livery ("type") drew the
    /// procedural trainer, and the J-3's own GLB is never placed (AirframeModels has no OwnSpecs entry for it), so it
    /// is procedural too. The procedural airframe is a CLOSED body of revolution with no cockpit cut into it, and both
    /// eyes are inside it: the 2-33's front eye 0.261 m from the axis of a 0.457 m tube, the J-3's rear 0.239 m inside
    /// 0.409 m. The 2-33 now always draws our own yellow model (LiveryChoice, 2026-11-02), whose front eye is clear —
    /// so it stays. The J-3 (Ty 2026-11-02, "get the eye location fixed on the j3 and add it to quest too both on
    /// wheels and floats") now always draws our own GLB on wheels and on floats (AirframeModels.OwnSpecs / PickOwn),
    /// driven by its own rig, and its solo REAR eye is clear in both: the nearest structure is the cabin liner
    /// overhead, 15.9 cm away. Its FRONT eye is not: a ceiling tube of the cabin frame is 9 cm from it in both
    /// configurations, so in a headset the J-3 is flown from the rear seat only (<see cref="SeatsOnly"/>) until that
    /// eye or that tube is looked at.
    ///
    /// Headset only: phones and the Mac keep the whole fleet (their flat instruments and outside views do not depend on
    /// the eye being in a cockpit).
    /// </summary>
    public static class QuestFleet
    {
        /// <summary>The types a headset lists, in the fleet's own order.</summary>
        public static readonly IReadOnlyList<string> Listed = new[]
        {
            "glider-2-33-like",   // our own SGS 2-33 (yellow, glider-2-33-like-own-tf.glb), front eye clear
            "extra-300-like",     // our own EA300 for every livery, both eyes clear
            "pitts-s2b-like",     // our own S-2B for every livery, both eyes clear
            "great-lakes-like",   // our own 2T-1A-2, both eyes clear (rear lowered behind its screen, Ty 2026-11-01)
            "j3-cub-like",        // our own J-3C on wheels (j3-cub-like-own.glb), rear eye clear; rear seat only
            "j3-cub-floats-like", // our own J-3C on EDO 60-1320s (j3-cub-floats-like-own.glb), rear eye clear; rear seat only
        };

        /// <summary>Listed types a headset seats in only some of their seats, by seat id (never by index: SeatChoice).
        /// A seat left out here is one whose approved eye has the model's structure within 12 cm of it.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> SeatsOnly = new Dictionary<string, string[]>
        {
            ["j3-cub-like"] = new[] { "rear" },
            ["j3-cub-floats-like"] = new[] { "rear" },
        };

        /// <summary>Why a seat is left out, for the log.</summary>
        public const string FrontSeatReason = "J-3 front eye: a cabin ceiling tube is 9 cm from it (check_eye_interiors.py), so the headset seats you in the rear";

        /// <summary>May a headset put the pilot in this seat? A null or empty id is the primary seat, always allowed.</summary>
        public static bool SeatCleared(string? aircraftId, string? seatId)
        {
            if (string.IsNullOrEmpty(seatId) || aircraftId == null || !SeatsOnly.TryGetValue(aircraftId, out string[]? only)) return true;
            foreach (string s in only) if (string.Equals(s, seatId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The seats a headset offers for this type: <paramref name="seats"/> in their own order, cleared ones only.</summary>
        public static List<T> Seats<T>(string? aircraftId, IReadOnlyList<T>? seats, Func<T, string?> idOf)
        {
            var list = new List<T>();
            if (seats == null) return list;
            foreach (T s in seats) if (SeatCleared(aircraftId, idOf(s))) list.Add(s);
            return list;
        }

        /// <summary>Why a type with an approved eye point is NOT listed (shown in the log, so the reason travels with it).</summary>
        public static readonly IReadOnlyDictionary<string, string> HiddenBecause = new Dictionary<string, string>
        {
            ["c172-like"] = "draws the downloaded 172, not our own interior; its eye has not been checked against that model",
        };

        /// <summary>The default for a headset when the saved aircraft is not listed: the first listed type.</summary>
        public static string Default => Listed[0];

        public static bool IsListed(string? aircraftId) => aircraftId != null && Contains(Listed, aircraftId);

        /// <summary>The fleet a headset shows: <paramref name="fleet"/> in its own order, only the listed types.</summary>
        public static (string id, string name)[] Filter((string id, string name)[] fleet)
        {
            var list = new List<(string, string)>();
            foreach (var f in fleet) if (IsListed(f.id)) list.Add(f);
            return list.ToArray();
        }

        /// <summary>The aircraft a headset should load: the chosen one if it is listed, otherwise <see cref="Default"/>.</summary>
        public static string Resolve(string? aircraftId) => IsListed(aircraftId) ? aircraftId! : Default;

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}

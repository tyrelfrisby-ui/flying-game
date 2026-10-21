using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A named paint scheme applied OVER a type's own colours, so one livery dresses any aeroplane in the fleet
    /// rather than being painted into each type's <see cref="AirframeVisual.Style"/> by hand.
    ///
    /// Owner 2026-10-21: *"I would like to create custom liveries of each with Talk Flying branding"* — the
    /// Cessna, the Cub, the Stearman and the 737, the four he flies on the podcast.
    ///
    /// WHY THIS PAINTS THE GENERATED AIRFRAME AND NOT THE DOWNLOADED MODELS. A livery on a generated airframe is
    /// entirely ours: our geometry, our colours, no attribution owed and no manufacturer's mark on it. Painting
    /// the same scheme onto a downloaded model would be lawful (CC BY permits adaptation, commercially) but it
    /// does not make the mesh ours, and putting a brand's livery on a recognisably branded airframe is the one
    /// move that makes the trademark question WORSE rather than better. See docs/pending/OWN-AIRFRAMES-PLAN.md,
    /// where the owner's decision is that the generated fleet becomes the shipping look.
    ///
    /// WHAT THIS CAN AND CANNOT DO TODAY. The generated airframe carries four flat colours — fuselage, wing,
    /// tail, control surfaces — so a livery is a colour scheme, not yet a paint job: no stripe down the
    /// fuselage, no logo on the tail. Both want new capability in AirframeVisual (a banded loft for the stripe,
    /// a decal for the mark). The fuselage now carries a WATERLINE — a two-tone split, dark over light, the way
    /// a real livery is laid out — built by lofting the body of revolution's segments into two meshes that share
    /// their boundary vertices. A logo on the tail still wants UVs and a texture on the generated fin.
    /// </summary>
    public readonly struct Livery
    {
        public readonly string Id, Name;
        /// <summary>null = leave the type's own colours alone.</summary>
        public readonly Color? Fuselage, Wing, Tail, Control;
        /// <summary>A WATERLINE: the belly in a second colour, the way an airliner livery is laid out. null = a
        /// single-colour fuselage. <see cref="BellyFrac"/> is how much of the body height it takes from the
        /// bottom, so 0.45 puts the line a little below the middle, which is where it flatters most shapes.</summary>
        public readonly Color? Belly;
        public readonly float BellyFrac;

        public Livery(string id, string name, Color? fuselage = null, Color? wing = null, Color? tail = null,
                      Color? control = null, Color? belly = null, float bellyFrac = 0.45f)
        {
            Id = id; Name = name; Fuselage = fuselage; Wing = wing; Tail = tail; Control = control;
            Belly = belly; BellyFrac = bellyFrac;
        }

        public bool IsDefault => Fuselage == null && Wing == null && Tail == null && Control == null && Belly == null;
    }

    public static class Liveries
    {
        // Sampled from talk_flying_logo_transparent.png: brushed silver on near-black. The logo is monochrome,
        // so the livery is too — which suits an aeroplane, and suits all four types without a clash.
        private static readonly Color Black = new(0.051f, 0.055f, 0.063f);     // #0D0E10, the logo's ground
        private static readonly Color Silver = new(0.788f, 0.800f, 0.824f);    // #C9CCD2, the wordmark's highlight
        private static readonly Color Graphite = new(0.541f, 0.557f, 0.584f);  // #8A8E95, its shadow

        public static readonly Livery TypeColours = new("type", "Type colours");

        /// <summary>Black over silver on a waterline, silver tail and control surfaces, graphite wings.</summary>
        public static readonly Livery TalkFlying =
            new("talk-flying", "Talk Flying", Black, Graphite, Silver, Silver, belly: Silver, bellyFrac: 0.42f);

        /// <summary>The same palette inverted: silver airframe, black tail. Reads better on the big types, where
        /// an all-black fuselage loses its shape against a dark background.</summary>
        public static readonly Livery TalkFlyingLight =
            new("talk-flying-light", "Talk Flying (light)", Silver, Silver, Black, Black, belly: Graphite, bellyFrac: 0.35f);

        public static readonly Livery[] All = { TypeColours, TalkFlying, TalkFlyingLight };

        public static Livery ById(string id)
        {
            foreach (Livery l in All) if (l.Id == id) return l;
            return TypeColours;
        }
    }
}

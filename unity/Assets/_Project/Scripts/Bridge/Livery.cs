using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Where a marking goes. Wing markings are per SIDE because that is how they are really applied —
    /// a registration goes on the upper surface of one wing and the lower surface of the other.</summary>
    public enum DecalPlace { WingUpper, WingLower, FinBothSides, FuselageBothSides, TailSign, LightningBolt, WingCentreUpper, WingCentreLower, FuselageSweep }

    /// <summary>
    /// One marking: a word, a registration, or an image, laid on a surface.
    ///
    /// Text is drawn with TextMesh, the same way the world's signs are (WorldBuilder) — no texture to author, no
    /// UVs needed on the generated wing, and it stays sharp at any size. An image needs a texture and a quad.
    /// </summary>
    public readonly struct Decal
    {
        public readonly DecalPlace Place;
        /// <summary>-1 left wing only, +1 right wing only, 0 both. Ignored for fin and fuselage.</summary>
        public readonly int Side;
        public readonly string Text;        // null for an image decal
        public readonly string Texture;     // Resources path, null for a text decal
        public readonly Color Colour;
        /// <summary>Height of the lettering as a fraction of the local chord.</summary>
        public readonly float HeightFrac;
        /// <summary>Position along the half-span, 0 at the root and 1 at the tip.</summary>
        public readonly float SpanFrac;
        /// <summary>Slanted, the way a registration is painted.</summary>
        public readonly bool Italic;
        /// <summary>An OS font by name — "Futura" for the 737's art-deco wordmark. null = the default face.</summary>
        public readonly string FontName;

        public Decal(DecalPlace place, string text = null, string texture = null, int side = 0,
                     Color? colour = null, float heightFrac = 0.5f, float spanFrac = 0.55f, bool italic = false,
                     string fontName = null)
        {
            Place = place; Text = text; Texture = texture; Side = side;
            Colour = colour ?? Color.black; HeightFrac = heightFrac; SpanFrac = spanFrac; Italic = italic;
            FontName = fontName;
        }
    }

    /// <summary>
    /// A paint scheme applied OVER a type's own colours, plus its markings.
    ///
    /// Owner 2026-10-21, in detail: the Super Cub white with red trim and "TALK" / "FLYING" large under the
    /// wings a word to a side, like a banner tow; the J-3 in Cub yellow with the black lightning bolt and the
    /// logo where the oversized registration would go (upper right wing, lower left); the 172 and the Archer
    /// mostly white with the words above and below the wings, the logo on the fin and a registration reading
    /// TALKFLYING; the Stearman keeping its blue and yellow with the words on the wings and the logo on the fin;
    /// the 737 silver, art-deco, "TALK FLYING AIR", logo on the fin, black on silver.
    ///
    /// So a livery is per TYPE, not one scheme for the fleet: six aeroplanes, six schemes, one brand.
    /// </summary>
    public readonly struct Livery
    {
        public readonly string Id, Name;
        /// <summary>null = leave the type's own colour alone.</summary>
        public readonly Color? Fuselage, Wing, Tail, Control;
        /// <summary>A WATERLINE: the belly in a second colour, the way a real livery is laid out. null = one
        /// colour. <see cref="BellyFrac"/> is how much of the body height it takes from the bottom.</summary>
        public readonly Color? Belly;
        public readonly float BellyFrac;
        public readonly IReadOnlyList<Decal> Decals;

        public Livery(string id, string name, Color? fuselage = null, Color? wing = null, Color? tail = null,
                      Color? control = null, Color? belly = null, float bellyFrac = 0.45f, Decal[] decals = null)
        {
            Id = id; Name = name; Fuselage = fuselage; Wing = wing; Tail = tail; Control = control;
            Belly = belly; BellyFrac = bellyFrac; Decals = decals ?? System.Array.Empty<Decal>();
        }

        public bool IsDefault => Fuselage == null && Wing == null && Tail == null && Control == null
                                 && Belly == null && Decals.Count == 0;
    }

    public static class Liveries
    {
        // From talk_flying_logo_transparent.png: the mark is monochrome, brushed silver on near-black.
        public static readonly Color Black = new(0.051f, 0.055f, 0.063f);     // #0D0E10
        public static readonly Color Silver = new(0.788f, 0.800f, 0.824f);    // #C9CCD2
        public static readonly Color Graphite = new(0.541f, 0.557f, 0.584f);  // #8A8E95
        // Type colours the schemes keep.
        private static readonly Color White = new(0.945f, 0.945f, 0.930f);
        private static readonly Color TrimRed = new(0.706f, 0.090f, 0.129f);  // #B4172100 Cub/Cessna trim red
        private static readonly Color SgsYellow = new(1f, 0.8f, 0f);     // 255,204,0 sampled from the Commons 2-33A 3-view
        private static readonly Color CubYellow = new(0.976f, 0.761f, 0.063f); // "Cub Yellow" / Lock Haven yellow
        private static readonly Color StearmanBlue = new(0.153f, 0.247f, 0.549f);
        private static readonly Color StearmanYellow = new(0.949f, 0.800f, 0.149f);

        private const string LogoTex = "Decals/talk-flying-logo";

        public static readonly Livery TypeColours = new("type", "Type colours");

        /// <summary>The generic scheme, for any type without one of its own: black over silver.</summary>
        public static readonly Livery TalkFlyingGeneric =
            new("talk-flying", "Talk Flying", Black, Graphite, Silver, Silver, belly: Silver, bellyFrac: 0.42f);

        /// <summary>Per-type Talk Flying schemes. Each keeps the aeroplane's own character — the Cub stays Cub
        /// yellow, the Stearman stays blue and yellow — and carries the branding the way that type really would.</summary>
        private static readonly Dictionary<string, Livery> PerType = new()
        {
            // SUPER CUB: white with red trim; a word under each wing, big, reading nose-to-tail like a banner.
            ["pa18-cub-like"] = new("talk-flying", "Talk Flying", White, White, TrimRed, TrimRed, decals: new[]
            {
                new Decal(DecalPlace.WingLower, text: "TALK",   side: -1, colour: TrimRed, heightFrac: 0.55f, spanFrac: 0.62f),
                new Decal(DecalPlace.WingLower, text: "FLYING", side: +1, colour: TrimRed, heightFrac: 0.55f, spanFrac: 0.62f),
            }),

            // J-3: Cub yellow with the black lightning bolt. The oversized registration that would sit on the
            // upper right and lower left wing is the logo instead — subtle, as asked.
            // Control surfaces stay YELLOW: on a real J-3 the only black is the bolt and the cowl trim.
            ["j3-cub-like"] = new("talk-flying", "Talk Flying", CubYellow, CubYellow, CubYellow, CubYellow, decals: new[]
            {
                new Decal(DecalPlace.WingUpper, texture: LogoTex, side: +1, heightFrac: 0.70f, spanFrac: 0.55f),
                new Decal(DecalPlace.WingLower, texture: LogoTex, side: -1, heightFrac: 0.70f, spanFrac: 0.55f),
                // The black lightning bolt down the fuselage -- a SHAPE, so it is built as geometry rather
                // than laid on as a marking.
                new Decal(DecalPlace.LightningBolt, colour: Black),
                // The tail sign: our own teddy-bear cub holding a red-backed board. Ty 2026-10-21 chose an
                // ORIGINAL mascot over a modified Piper bear -- same wink, nothing derived from their mark.
                new Decal(DecalPlace.TailSign, text: "TALK\nFLYING", colour: White, heightFrac: 0.26f),
            }),

            // 172: retain the default model's shape; all paint slots stay light, including every hinge.
            ["c172-like"] = new("talk-flying", "Talk Flying", White, White, White, Silver,
                belly: White, bellyFrac: 0.22f, decals: new[]
            {
                new Decal(DecalPlace.WingUpper, text: "TALK FLYING", side: -1, colour: Black, heightFrac: 0.32f, spanFrac: 0.56f),
                new Decal(DecalPlace.WingUpper, text: "PODCAST", side: +1, colour: Black, heightFrac: 0.38f, spanFrac: 0.56f),
                new Decal(DecalPlace.WingLower, text: "TALK FLYING", side: +1, colour: Black, heightFrac: 0.32f, spanFrac: 0.56f),
                new Decal(DecalPlace.WingLower, text: "PODCAST", side: -1, colour: Black, heightFrac: 0.38f, spanFrac: 0.56f),
                new Decal(DecalPlace.WingCentreUpper, texture: LogoTex, colour: Black, heightFrac: 0.55f),
                new Decal(DecalPlace.WingCentreLower, texture: LogoTex, colour: Black, heightFrac: 0.55f),
                new Decal(DecalPlace.FinBothSides, texture: LogoTex, colour: Black, heightFrac: 0.50f),
                new Decal(DecalPlace.FuselageBothSides, text: "talkflying.com", colour: Black, heightFrac: 0.22f),
                new Decal(DecalPlace.FuselageSweep, colour: Black, heightFrac: 0.12f),
                new Decal(DecalPlace.FuselageSweep, colour: Silver, heightFrac: 0.065f, spanFrac: 0.16f),
            }),
            // ARCHER: retain its existing trainer scheme.
            ["pa28-archer-like"] = Trainer(White, Black),

            // STEARMAN: keeps its blue and yellow. Words on the wings where the registration would be, logo on the fin.
            ["stearman-pt17-like"] = new("talk-flying", "Talk Flying", StearmanBlue, StearmanYellow, StearmanYellow, StearmanBlue, decals: new[]
            {
                new Decal(DecalPlace.WingUpper, text: "TALKFLYING", colour: StearmanBlue, heightFrac: 0.5f, spanFrac: 0.55f),
                new Decal(DecalPlace.WingLower, text: "TALKFLYING", colour: StearmanBlue, heightFrac: 0.5f, spanFrac: 0.55f),
                new Decal(DecalPlace.FinBothSides, texture: LogoTex, heightFrac: 0.55f),
            }),

            // SCHWEIZER 2-33 (owner 2026-10-26): the 3-view's all-yellow scheme kept, branded with black/silver
            // accents. Our model bakes the cheat line, TALKFLYING and talkflying.com into the fuselage
            // (glider-2-33-like-own-tf.glb); the wing words and the fin logo are laid on as markings. Every
            // surface stays yellow -- black only as lettering/trim -- so the silhouette reads on a black background.
            ["glider-2-33-like"] = new("talk-flying", "Talk Flying", SgsYellow, SgsYellow, SgsYellow, SgsYellow,
                // v6: every marking is baked into the model -- wing TALK FLYING / PODCAST and the emblem too (the
                // config-geometry wing words sat under our high wing and the fin logo floated off our fin).
                decals: new Decal[0]),

            // SWIFT S-1: the aerobatic sailplane, marked the way the Grob's repaint is (owner, 2026-10-22 —
            // "brand the swift like the grob"): the winged badge over the LEFT wing, the wordmark over the
            // RIGHT, and the whole logo on both sides of the fin. Mirrored underneath so that whichever side
            // you are on, and from above or below, you get one of each. White with red trim, which is what
            // this type already wears.
            ["glider-swift-s1-like"] = new("talk-flying", "Talk Flying", White, White, White, TrimRed, decals: new[]
            {
                new Decal(DecalPlace.WingUpper, texture: LogoTex, side: -1, heightFrac: 0.72f, spanFrac: 0.42f),
                new Decal(DecalPlace.WingUpper, text: "TALK FLYING", side: +1, colour: TrimRed, heightFrac: 0.60f, spanFrac: 0.50f),
                new Decal(DecalPlace.WingLower, texture: LogoTex, side: +1, heightFrac: 0.72f, spanFrac: 0.42f),
                new Decal(DecalPlace.WingLower, text: "TALK FLYING", side: -1, colour: TrimRed, heightFrac: 0.60f, spanFrac: 0.50f),
                new Decal(DecalPlace.FinBothSides, texture: LogoTex, heightFrac: 0.55f),
            }),

            // 737: silver all over, art-deco, the wordmark and the logo in BLACK on silver — the logo reversed
            // out of its usual silver-on-black.
            ["boeing-737-like"] = new("talk-flying", "Talk Flying Air", Silver, Silver, Silver, Graphite,
                                      belly: Silver, bellyFrac: 0.40f, decals: new[]
            {
                // Futura, 1927 -- a geometric sans of the period, which is what makes it read deco rather than modern.
                new Decal(DecalPlace.FuselageBothSides, text: "TALK FLYING AIR", colour: Black, heightFrac: 0.30f, fontName: "Futura"),
                new Decal(DecalPlace.FinBothSides, texture: LogoTex, colour: Black, heightFrac: 0.55f),
            }),
        };

        private static Livery Trainer(Color body, Color trim) =>
            new("talk-flying", "Talk Flying", body, body, body, trim, belly: trim, bellyFrac: 0.22f, decals: new[]
            {
                new Decal(DecalPlace.WingUpper, text: "TALK FLYING", colour: trim, heightFrac: 0.45f, spanFrac: 0.55f),
                new Decal(DecalPlace.WingLower, text: "TALK FLYING", colour: trim, heightFrac: 0.45f, spanFrac: 0.55f),
                new Decal(DecalPlace.FinBothSides, texture: LogoTex, heightFrac: 0.50f),
                // The registration, slanted the way one is really painted -- and reading TALKFLYING.
                new Decal(DecalPlace.FuselageBothSides, text: "TALKFLYING", colour: trim, heightFrac: 0.26f, italic: true),
            });

        public static readonly Livery Factory233 = new("factory", "Schweizer factory",
            White, White, White, White);

        // Surface-conforming paint and raster lettering are embedded in model_extra300.py's GLB.
        public static readonly Livery SunriseExtra = new("sunrise", "Sunrise Aviation",
            White, White, White, White);

        public static readonly Livery[] All = { TypeColours, TalkFlyingGeneric };

        /// <summary>The livery to paint `aircraftId` in. A per-type scheme wins over the generic one.</summary>
        public static Livery For(string liveryId, string aircraftId)
        {
            if (liveryId == "factory" && aircraftId == "glider-2-33-like") return Factory233;
            if (liveryId == "sunrise" && aircraftId == "extra-300-like") return SunriseExtra;
            if (liveryId == "type" || string.IsNullOrEmpty(liveryId)) return TypeColours;
            if (liveryId == "talk-flying" && aircraftId != null && PerType.TryGetValue(aircraftId, out Livery l)) return l;
            if (liveryId == "talk-flying") return TalkFlyingGeneric;
            return TypeColours;
        }

        /// <summary>Kept for callers that have no aircraft id to hand.</summary>
        public static Livery ById(string id) => For(id, null);
    }
}

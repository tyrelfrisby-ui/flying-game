using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// What a part of the airframe is MADE OF, which decides how it answers the light.
    ///
    /// Until now every generated part was the same flat paint, so a polished cowl, a doped fabric wing, a rubber
    /// tyre and a perspex canopy all looked identical and the aeroplane read as one moulded object. Giving each
    /// a different smoothness and specular response is a large part of what separates a model that looks real
    /// from one that looks printed — more than polygon count does.
    /// </summary>
    public enum SurfaceKind
    {
        /// <summary>Doped fabric over ribs, or matte paint: broad, weak highlight, and panel detail is scalloping
        /// between ribs rather than rivets.</summary>
        Fabric,
        /// <summary>Painted aluminium: the default for most of the fleet. Tight-ish highlight, panel lines and rivets.</summary>
        Paint,
        /// <summary>Bare or polished metal — cowls, spinners, a bare-metal airliner. Sharp highlight, strongly tinted.</summary>
        Metal,
        /// <summary>Tyres, bungee, boots. Nearly no highlight, no panel detail.</summary>
        Rubber,
        /// <summary>Struts, wires, gear legs: small, hard, metallic.</summary>
        Strut,
        /// <summary>Windscreen, cabin windows, a canopy: dark, very smooth, no panel detail at all. Our own
        /// models ship the glazing as its own parts (Glass.*) because a 172 without windows does not read as
        /// a 172 whatever else is right about it.</summary>
        Glass,
    }

    /// <summary>
    /// Generates the airframe's surface detail in code, and hands out a material per <see cref="SurfaceKind"/>.
    ///
    /// THE PANEL-AND-RIVET NORMAL MAP IS THE TRICK. On a well-made aircraft model the rivets and panel lines are
    /// not geometry — they are a normal map, which is why such a model can be cheap and still read as metal. The
    /// map here is tiled in METRES (see FlyingGame/Airframe's _DetailTile), so a rivet comes out the same real
    /// size on a Cub and on a 737 instead of scaling with the aeroplane, which would give the game away at once.
    /// </summary>
    public static class AirframeSurface
    {
        private const int Res = 256;                 // one square metre of skin
        private static Texture2D _panels, _fabric, _blank;
        private static readonly Dictionary<(SurfaceKind, Color), Material> Cache = new();

        /// <summary>A tiling normal map of flush panel lines with a row of rivets down each seam.</summary>
        private static Texture2D Panels()
        {
            if (_panels != null) return _panels;
            var px = new Color32[Res * Res];
            // panel bays of roughly 0.5 m, rivets every 30 mm along the seam
            const int bay = Res / 2, rivetStep = 8, rivetR = 2;
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float nx = 0f, ny = 0f;
                // seams: a shallow groove, so the normal tips across the line and catches the light
                int dx = Mathf.Min(x % bay, bay - (x % bay));
                int dy = Mathf.Min(y % bay, bay - (y % bay));
                if (dx <= 1) nx += (x % bay < bay / 2 ? 1f : -1f) * 0.55f;
                if (dy <= 1) ny += (y % bay < bay / 2 ? 1f : -1f) * 0.55f;
                // rivets: domes along both seams
                bool onSeamX = dx <= 1, onSeamY = dy <= 1;
                if (onSeamX || onSeamY)
                {
                    int along = onSeamX ? y : x;
                    int o = along % rivetStep - rivetStep / 2;
                    if (Mathf.Abs(o) <= rivetR)
                    {
                        float f = 1f - Mathf.Abs(o) / (float)(rivetR + 1);
                        if (onSeamX) ny += Mathf.Sign(o == 0 ? 1 : o) * f * 0.5f;
                        else nx += Mathf.Sign(o == 0 ? 1 : o) * f * 0.5f;
                    }
                }
                var n = new Vector3(nx, ny, 1f).normalized;
                px[y * Res + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255),
                                              (byte)((n.z * 0.5f + 0.5f) * 255), 255);
            }
            _panels = Make(px, "AirframePanels");
            return _panels;
        }

        /// <summary>Doped fabric: no rivets and no seams, just the gentle scallop between ribs.</summary>
        private static Texture2D Fabric()
        {
            if (_fabric != null) return _fabric;
            var px = new Color32[Res * Res];
            const float ribs = 6f;                   // ribs roughly every 170 mm
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float s = Mathf.Sin(y / (float)Res * Mathf.PI * 2f * ribs);
                var n = new Vector3(0f, s * 0.22f, 1f).normalized;
                px[y * Res + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255),
                                              (byte)((n.z * 0.5f + 0.5f) * 255), 255);
            }
            _fabric = Make(px, "AirframeFabric");
            return _fabric;
        }

        private static Texture2D Blank()
        {
            if (_blank != null) return _blank;
            var px = new Color32[4];
            for (int i = 0; i < 4; i++) px[i] = new Color32(128, 128, 255, 255);   // flat normal
            _blank = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "AirframeFlat" };
            _blank.SetPixels32(px); _blank.Apply(); _blank.hideFlags = HideFlags.HideAndDontSave;
            return _blank;
        }

        private static Texture2D Make(Color32[] px, string name)
        {
            var t = new Texture2D(Res, Res, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            t.SetPixels32(px); t.Apply(true); t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>The material for a kind of surface in a colour, cached — the fleet reuses a handful.</summary>
        public static Material MaterialFor(SurfaceKind kind, Color colour)
        {
            if (Cache.TryGetValue((kind, colour), out Material m) && m != null) return m;
            Shader sh = Shader.Find("FlyingGame/Airframe");
            if (sh == null) return null;             // caller falls back to FlyingGame/Lit
            m = new Material(sh) { color = colour, hideFlags = HideFlags.HideAndDontSave };

            // smoothness, specular strength, detail map and how deep it bites — the character of each material
            (Texture2D tex, float tile, float depth, float smooth, float spec, Color tint) p = kind switch
            {
                SurfaceKind.Fabric => (Fabric(), 1.0f, 0.5f, 0.12f, 0.10f, Color.white),
                SurfaceKind.Metal  => (Panels(), 1.0f, 0.9f, 0.80f, 1.10f, colour),
                SurfaceKind.Rubber => (Blank(), 1.0f, 0.0f, 0.05f, 0.04f, Color.white),
                SurfaceKind.Strut  => (Blank(), 1.0f, 0.0f, 0.65f, 0.70f, colour),
                SurfaceKind.Glass  => (Blank(), 1.0f, 0.0f, 0.95f, 1.60f, Color.white),
                _                  => (Panels(), 1.0f, 0.6f, 0.38f, 0.32f, Color.white),   // Paint
            };
            m.SetTexture("_DetailNormal", p.tex);
            m.SetFloat("_DetailTile", p.tile);
            m.SetFloat("_DetailDepth", p.depth);
            m.SetFloat("_Smoothness", p.smooth);
            m.SetFloat("_SpecStrength", p.spec);
            m.SetColor("_SpecTint", p.tint);
            Cache[(kind, colour)] = m;
            return m;
        }

        /// <summary>What a part named `name` is made of. Names come from AirframeVisual's Spawn calls.</summary>
        public static SurfaceKind KindFor(string name, bool fabricAircraft)
        {
            if (name == null) return SurfaceKind.Paint;
            if (name.StartsWith("Glass") || name.StartsWith("Canopy") || name.StartsWith("Windscreen"))
                return SurfaceKind.Glass;
            if (name.StartsWith("Tyre") || name.StartsWith("Tire") || name.Contains("Wheel")) return SurfaceKind.Rubber;
            if (name.StartsWith("Strut") || name.StartsWith("Leg") || name.StartsWith("Wire")
                || name.StartsWith("Gear") || name.Contains("Bungee")) return SurfaceKind.Strut;
            if (name.StartsWith("Blade") || name.StartsWith("Spinner") || name.StartsWith("Prop")
                || name.StartsWith("Exhaust") || name.StartsWith("Cowl")) return SurfaceKind.Metal;
            // a fabric aeroplane's wings and tail are doped linen; its cowl is still metal, caught above
            return fabricAircraft ? SurfaceKind.Fabric : SurfaceKind.Paint;
        }
    }
}

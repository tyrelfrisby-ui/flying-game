using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Presentation tokens shared by the flat landing view and Quest's world-space view.
    /// No session state, persistence, catalog filtering, launch logic or platform branching.
    /// Rectangles use a top-left origin; the Quest presenter maps them onto its own panel.
    /// </summary>
    public static class LandingVisuals
    {
        public const float Gap = 16f;
        public const float MinimumTarget = 48f;
        public const float QuestMinimumTarget = 64f;
        public const float CardWidth = 184f;
        public const float CardHeight = 132f;

        public readonly struct Palette
        {
            public readonly Color Background, Surface, Raised, Ink, Muted, Accent, Border;
            public Palette(Color background, Color surface, Color raised, Color ink,
                Color muted, Color accent, Color border)
            {
                Background = background; Surface = surface; Raised = raised; Ink = ink;
                Muted = muted; Accent = accent; Border = border;
            }
        }

        // Semantic night/day selection is supplied by Claude's view model. Never infer it
        // from the device clock, and never write back to the simulation from a visual theme.
        public static Palette Theme(bool night) => night
            ? new Palette(Hex(0x101D2B), Hex(0x1C2C3D), Hex(0x293D50), Hex(0xF5F7FA),
                Hex(0xBDCDD9), Hex(0xFFCF54), Hex(0x54687B))
            : new Palette(Hex(0xF1F4F2), Hex(0xFFFFFF), Hex(0xE5ECE9), Hex(0x152D35),
                Hex(0x4B626B), Hex(0xFFD15C), Hex(0xA4B6B5));

        private static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f,
            ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public readonly struct Layout
        {
            public readonly Rect Header, Content, Rail, Footer;
            public readonly bool Compact;
            public readonly float Target;
            public Layout(Rect safeArea, bool quest)
            {
                // safeArea is already top-left, logical UI coordinates. The flat host must
                // convert Screen.safeArea's bottom-left origin before constructing this.
                Compact = safeArea.width < 1000f;
                Target = quest ? QuestMinimumTarget : MinimumTarget;
                float margin = Compact ? 20f : 32f;
                float header = quest ? 88f : 64f;
                float footer = Target + 24f;
                float rail = Compact ? 180f : 240f;
                float width = Mathf.Max(0, safeArea.width - margin * 2);
                float height = Mathf.Max(0, safeArea.height - margin * 2 - header - footer - Gap * 2);
                Header = new Rect(safeArea.x + margin, safeArea.y + margin, width, header);
                Footer = new Rect(Header.x, safeArea.yMax - margin - footer, width, footer);
                Content = new Rect(Header.x, Header.yMax + Gap,
                    Mathf.Max(0, width - rail - Gap), height);
                Rail = new Rect(Content.xMax + Gap, Content.y, rail, height);
            }
        }

        /// <summary>Width of a horizontal list; empty lists still produce a valid scroll rect.</summary>
        public static float CarouselWidth(int count, float viewportWidth, float cardWidth = CardWidth)
            => Mathf.Max(viewportWidth, Mathf.Max(0, count) * (cardWidth + Gap) - (count > 0 ? Gap : 0));

        /// <summary>Keep the selected card fully visible after keyboard, ray or arrow navigation.</summary>
        public static float RevealCard(float scroll, int index, int count, float viewportWidth,
            float cardWidth = CardWidth)
        {
            if (count <= 0 || viewportWidth <= 0) return 0;
            index = Mathf.Clamp(index, 0, count - 1);
            float left = index * (cardWidth + Gap), right = left + cardWidth;
            if (left < scroll) scroll = left;
            else if (right > scroll + viewportWidth) scroll = right - viewportWidth;
            return Mathf.Clamp(scroll, 0, Mathf.Max(0, CarouselWidth(count, viewportWidth, cardWidth) - viewportWidth));
        }

        /// <summary>Fit the capture without distorting the aircraft or its actual launch viewpoint.</summary>
        public static Rect FitThumbnail(Rect box, int width, int height)
        {
            if (width <= 0 || height <= 0 || box.width <= 0 || box.height <= 0) return box;
            float scale = Mathf.Min(box.width / width, box.height / height);
            var size = new Vector2(width * scale, height * scale);
            return new Rect(box.center - size * 0.5f, size);
        }
    }
}

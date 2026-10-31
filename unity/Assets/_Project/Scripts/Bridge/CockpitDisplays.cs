using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The two little LCDs on the Extra's rear panel: a Trig-style COM head over a transponder head
    /// (docs/models/ea300-own-RIG.md, `displays`). The model gives each one a quad with UV 0..1 over the window
    /// and leaves the text to the game.
    ///
    /// The text is DRAWN INTO A TEXTURE from a 5x7 font kept here, not set on a TextMesh. The livery code
    /// already carries the scar: "a TextMesh with a null font draws nothing", and Unity's built-in font is one
    /// of the things a player build loses silently — on a cockpit display that would read as a dead radio with
    /// nothing in the log. A generated texture has no font to lose, costs one 128x76 RGBA32 upload when the
    /// frequency actually changes, and gives exactly the hard-edged look a segment LCD has.
    ///
    /// The material is FlyingGame/Decal, which takes the image's LUMINANCE as alpha: white glyphs on black, so
    /// the black field drops out and what shows behind is the radio head's own dark face. That shader is already
    /// force-included in the iOS player (BuildScript.EnsureAlwaysIncludedShaders) — a new one would not be.
    /// </summary>
    public sealed class CockpitDisplays
    {
        private sealed class Lcd
        {
            public Renderer R;
            public Texture2D Tex;
            public Material Mat;
            public string Shown = "";
            public int W, H, Scale;
        }

        private Lcd _com, _xpdr;
        private bool _looked;

        /// <summary>The LCD's own colour: the greenish white of a backlit segment display.</summary>
        private static readonly Color Lit = new Color(0.82f, 0.93f, 0.86f, 1f);

        /// <summary>Find the display quads inside the placed model. Safe to call every frame; it looks once per
        /// aeroplane and does nothing at all for a model without them. Takes a LOOKUP rather than the visual, so
        /// the editor check can bind the same two displays on a model it built itself.</summary>
        public void Bind(System.Func<string, Transform> find)
        {
            if (_looked || find == null) return;
            _looked = true;
            // 32 x 19 mm and 39 x 16 mm faces (the rig's size_m): the textures carry the same aspect, so a
            // glyph is square on the glass.
            _com = Make(find("Radio_Display_R"), 128, 76, 3);
            _xpdr = Make(find("Xpdr_Display_R"), 156, 64, 4);
        }

        /// <summary>A fresh aeroplane: look the quads up again (the hierarchy was rebuilt), and let go of the
        /// material and texture this one made, which nothing else references.</summary>
        public void Forget()
        {
            _looked = false;
            Drop(_com); Drop(_xpdr);
            _com = _xpdr = null;
        }

        private static void Drop(Lcd lcd)
        {
            if (lcd == null) return;
            if (lcd.Tex != null) Object.Destroy(lcd.Tex);
            if (lcd.Mat != null) Object.Destroy(lcd.Mat);
        }

        private static Lcd Make(Transform t, int w, int h, int scale)
        {
            if (t == null || !t.TryGetComponent(out Renderer r)) return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,       // a segment display has hard edges
                wrapMode = TextureWrapMode.Clamp,
                name = t.name + "_LCD",
            };
            Shader sh = Shader.Find("FlyingGame/Decal") ?? Shader.Find("FlyingGame/UnlitTransparent");
            var mat = new Material(sh) { mainTexture = tex, color = Lit };
            r.sharedMaterial = mat;
            return new Lcd { R = r, Tex = tex, Mat = mat, W = w, H = h, Scale = scale };
        }

        /// <summary>Put the radio's and the transponder's current state on the glass. Nothing is uploaded unless
        /// the text changed, so a settled panel costs two string compares.</summary>
        public void Update(in RigInputs r)
        {
            if (_com != null)
            {
                // Active over standby, the way every COM head in a light aeroplane shows it.
                string text = Mhz(r.ComFreqMhz) + "\n" + Mhz(r.ComStandbyMhz);
                Draw(_com, text, r.ComVolume > 0.001);
            }
            if (_xpdr != null)
            {
                string mode = r.XpdrMode <= 0.5 ? "OFF" : r.XpdrMode <= 1.5 ? "SBY" : r.XpdrMode <= 2.5 ? "ON" : "ALT";
                int code = Mathf.Clamp((int)(r.XpdrSquawk + 0.5), 0, 7777);
                Draw(_xpdr, $"{code:0000} {mode}", r.XpdrMode > 0.5);
            }
        }

        /// <summary>A COM frequency the way the head shows it: five figures, 25 kHz spacing.</summary>
        private static string Mhz(double mhz)
        {
            if (mhz < 1.0) return "---.---";
            return mhz.ToString("000.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Lay the text out and upload it, unless it is already on the glass. An unpowered head shows a
        /// blank window rather than text with no electricity behind it.</summary>
        private static void Draw(Lcd lcd, string text, bool powered)
        {
            string want = powered ? text : "";
            if (lcd.Shown == want) return;
            lcd.Shown = want;

            var px = new Color32[lcd.W * lcd.H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);   // black: the Decal shader drops it

            if (want.Length > 0)
            {
                string[] lines = want.Split('\n');
                // Fit the glyph scale to the LONGEST line rather than trusting a constant: the transponder's
                // "1200 ALT" is eight cells, and at the scale the frequency wanted, the T fell off the glass.
                int longest = 1;
                foreach (string l in lines) longest = Mathf.Max(longest, l.Length);
                int s = Mathf.Clamp(Mathf.Min(lcd.Scale, lcd.W / (longest * 6)), 1, lcd.Scale);
                int gh = 7 * s, gap = s;
                int blockH = lines.Length * gh + (lines.Length - 1) * gap;
                int top = Mathf.Max(0, (lcd.H - blockH) / 2);
                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li];
                    int lineW = line.Length * (5 * s + s) - s;
                    int left = Mathf.Max(0, (lcd.W - lineW) / 2);
                    int y0 = lcd.H - top - gh - li * (gh + gap);          // rows run DOWN the glyph, up the texture
                    for (int ci = 0; ci < line.Length; ci++)
                        Blit(px, lcd.W, lcd.H, LcdFont.Rows(line[ci]), left + ci * (5 * s + s), y0, s);
                }
            }
            lcd.Tex.SetPixels32(px);
            lcd.Tex.Apply(false, false);
        }

        /// <summary>One glyph, scaled up, into the pixel buffer. (x, y) is its bottom-left in texture pixels.</summary>
        private static void Blit(Color32[] px, int w, int h, byte[] rows, int x, int y, int s)
        {
            if (rows == null) return;
            for (int gy = 0; gy < 7; gy++)
            {
                byte row = rows[gy];
                for (int gx = 0; gx < 5; gx++)
                {
                    if ((row & (1 << (4 - gx))) == 0) continue;
                    // Glyph row 0 is the TOP of the character, and texture y runs up.
                    int bx = x + gx * s, by = y + (6 - gy) * s;
                    for (int dy = 0; dy < s; dy++)
                    {
                        int ty = by + dy;
                        if (ty < 0 || ty >= h) continue;
                        int rowStart = ty * w;
                        for (int dx = 0; dx < s; dx++)
                        {
                            int tx = bx + dx;
                            if (tx < 0 || tx >= w) continue;
                            px[rowStart + tx] = new Color32(255, 255, 255, 255);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A 5 x 7 cell font, enough for what an avionics head says: the ten figures, the alphabet, and the four
    /// marks a frequency or a code needs. Each glyph is seven rows of five bits, row 0 at the TOP, bit 4
    /// (0b10000) at the LEFT — written out so a wrong pixel is visible in the source rather than hidden in a
    /// hexadecimal table.
    /// </summary>
    internal static class LcdFont
    {
        private static readonly Dictionary<char, byte[]> Glyphs = new();

        private static void Add(char c, string art)
        {
            string[] rows = art.Split('/');
            var bits = new byte[7];
            for (int i = 0; i < 7 && i < rows.Length; i++)
            {
                byte b = 0;
                for (int j = 0; j < 5 && j < rows[i].Length; j++)
                    if (rows[i][j] == '1') b |= (byte)(1 << (4 - j));
                bits[i] = b;
            }
            Glyphs[c] = bits;
        }

        static LcdFont()
        {
            Add('0', "01110/10001/10011/10101/11001/10001/01110");
            Add('1', "00100/01100/00100/00100/00100/00100/01110");
            Add('2', "01110/10001/00001/00010/00100/01000/11111");
            Add('3', "11111/00010/00100/00010/00001/10001/01110");
            Add('4', "00010/00110/01010/10010/11111/00010/00010");
            Add('5', "11111/10000/11110/00001/00001/10001/01110");
            Add('6', "00110/01000/10000/11110/10001/10001/01110");
            Add('7', "11111/00001/00010/00100/01000/01000/01000");
            Add('8', "01110/10001/10001/01110/10001/10001/01110");
            Add('9', "01110/10001/10001/01111/00001/00010/01100");
            Add('A', "01110/10001/10001/11111/10001/10001/10001");
            Add('B', "11110/10001/10001/11110/10001/10001/11110");
            Add('C', "01110/10001/10000/10000/10000/10001/01110");
            Add('D', "11110/10001/10001/10001/10001/10001/11110");
            Add('E', "11111/10000/10000/11110/10000/10000/11111");
            Add('F', "11111/10000/10000/11110/10000/10000/10000");
            Add('G', "01110/10001/10000/10111/10001/10001/01111");
            Add('H', "10001/10001/10001/11111/10001/10001/10001");
            Add('I', "01110/00100/00100/00100/00100/00100/01110");
            Add('J', "00111/00010/00010/00010/00010/10010/01100");
            Add('K', "10001/10010/10100/11000/10100/10010/10001");
            Add('L', "10000/10000/10000/10000/10000/10000/11111");
            Add('M', "10001/11011/10101/10101/10001/10001/10001");
            Add('N', "10001/10001/11001/10101/10011/10001/10001");
            Add('O', "01110/10001/10001/10001/10001/10001/01110");
            Add('P', "11110/10001/10001/11110/10000/10000/10000");
            Add('Q', "01110/10001/10001/10001/10101/10011/01101");
            Add('R', "11110/10001/10001/11110/10100/10010/10001");
            Add('S', "01111/10000/10000/01110/00001/00001/11110");
            Add('T', "11111/00100/00100/00100/00100/00100/00100");
            Add('U', "10001/10001/10001/10001/10001/10001/01110");
            Add('V', "10001/10001/10001/10001/10001/01010/00100");
            Add('W', "10001/10001/10001/10101/10101/11011/10001");
            Add('X', "10001/10001/01010/00100/01010/10001/10001");
            Add('Y', "10001/10001/01010/00100/00100/00100/00100");
            Add('Z', "11111/00001/00010/00100/01000/10000/11111");
            Add(' ', "00000/00000/00000/00000/00000/00000/00000");
            Add('.', "00000/00000/00000/00000/00000/01100/01100");
            Add('-', "00000/00000/00000/01110/00000/00000/00000");
            Add('/', "00001/00010/00010/00100/01000/01000/10000");
            Add(':', "00000/01100/01100/00000/01100/01100/00000");
        }

        /// <summary>The seven rows of a character, or null for one this font has no cell for (it draws as a gap,
        /// which is the right answer for a display: a missing glyph is not worth a crash).</summary>
        public static byte[] Rows(char c)
        {
            if (Glyphs.TryGetValue(char.ToUpperInvariant(c), out byte[] rows)) return rows;
            return null;
        }
    }
}

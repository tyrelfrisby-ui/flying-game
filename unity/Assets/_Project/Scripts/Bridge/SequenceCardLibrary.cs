using System.IO;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE ONE CARD DECK every holder in every aeroplane shows (Core <see cref="IacCardDeck"/>; Ty 2026-11-05: "Front-panel
    /// cards stay matched to the card selected in the back"). The built-in IAC Knowns are textures in
    /// Resources/SequenceCards; the pilot's own are PNGs, already letter-sized, in
    /// <c>persistentDataPath/sequence-cards/library/</c> beside <c>deck.txt</c> (his cards and the selection).
    ///
    /// The texture of the CURRENT card only is held: a deck of forty 1275 x 1650 cards would be 300 MB of texture.
    ///
    /// On the Quest there is no document picker: a PDF, PNG or JPEG copied into the library's <c>inbox/</c> folder
    /// (<c>adb push card.pdf /sdcard/Android/data/&lt;package&gt;/files/sequence-cards/library/inbox/</c>) is imported by
    /// the headset menu's "Import cards" row (<see cref="ImportInbox"/>), each PDF page a card.
    /// </summary>
    public static class SequenceCardLibrary
    {
        private static IacCardDeck _deck;
        private static Texture _tex;
        private static Texture2D _ownedTex;
        private static string _texFor = "";

        private static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        public static string Folder => Path.Combine(Application.persistentDataPath, "sequence-cards", "library");
        public static string Inbox => Path.Combine(Folder, "inbox");
        private static string DeckPath => Path.Combine(Folder, "deck.txt");
        private static string PngPath(string id) => Path.Combine(Folder, id + ".png");

        public static IacCardDeck Deck
        {
            get
            {
                if (_deck != null) return _deck;
                string text = "";
                try { if (File.Exists(DeckPath)) text = File.ReadAllText(DeckPath); }
                catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] deck unreadable ({e.Message}); starting fresh"); }
                _deck = IacCardDeck.Load(text);
                // A user card whose PNG has gone is not shown as a blank holder: it leaves the deck.
                foreach (IacCard c in new System.Collections.Generic.List<IacCard>(_deck.UserCards))
                    if (!File.Exists(PngPath(c.Id))) _deck.RemoveUser(c.Id);
                MigrateOldCards();
                return _deck;
            }
        }

        public static IacCard Current => Deck.Current;
        public static int Version => Deck.Version;

        /// <summary>The tap on the card (any seat, any platform).</summary>
        public static IacCard Next() { IacCard c = Deck.Next(); Save(); Debug.Log($"[SequenceCard] card: {c.LabelAscii}"); return c; }
        public static IacCard Previous() { IacCard c = Deck.Previous(); Save(); return c; }

        /// <summary>The current card's texture (loaded on demand; null only when even the built-in is missing).</summary>
        public static Texture CurrentTexture()
        {
            IacCard c = Current;
            if (_tex != null && _texFor == c.Id) return _tex;
            if (_ownedTex != null) { Kill(_ownedTex); _ownedTex = null; }
            _tex = null; _texFor = c.Id;
            if (c.IsUser)
            {
                try
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "SequenceCard " + c.Id, wrapMode = TextureWrapMode.Clamp };
                    if (t.LoadImage(File.ReadAllBytes(PngPath(c.Id)))) { t.Apply(true, true); _ownedTex = t; _tex = t; }
                    else Kill(t);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] {c.Id} would not load: {e.Message}"); }
            }
            else _tex = Resources.Load<Texture2D>("SequenceCards/" + c.Id);
            if (_tex == null)
            {
                Debug.LogWarning($"[SequenceCard] no texture for {c.Id}; the default goes on instead");
                _tex = Resources.Load<Texture2D>("SequenceCards/" + IacCardDeck.DefaultId);
            }
            return _tex;
        }

        /// <summary>A rendered card (any image: it is fitted onto letter portrait here) becomes one of his: it joins the
        /// deck and goes onto the holder. Returns the new card's id, or null.</summary>
        public static string Import(byte[] image, string title, out string why)
        {
            why = "";
            byte[] png = LetterFit(image, out why);
            if (png == null) return null;
            IacCardDeck d = Deck;
            if (d.UserCards.Count >= IacCardDeck.MaxUserCards) { why = $"the library holds {IacCardDeck.MaxUserCards} cards: remove one first"; return null; }
            string id = d.NewUserId(title);
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(PngPath(id), png);
            }
            catch (System.Exception e) { why = "could not save the card: " + e.Message; return null; }
            d.AddUser(id, title);
            Save();
            Debug.Log($"[SequenceCard] added '{title}' as {id} ({png.Length / 1024} kB)");
            return id;
        }

        /// <summary>Remove one of his cards (the file and the deck entry). The IAC Knowns stay.</summary>
        public static bool Remove(string id)
        {
            if (!Deck.RemoveUser(id)) return false;
            try { if (File.Exists(PngPath(id))) File.Delete(PngPath(id)); }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] could not delete {id}: {e.Message}"); }
            Save();
            return true;
        }

        private static void Save()
        {
            try { Directory.CreateDirectory(Folder); File.WriteAllText(DeckPath, Deck.Save()); }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] deck not saved: {e.Message}"); }
        }

        /// <summary>Any image onto the 1275 x 1650 white card, whole and centred (Core LetterCard.Fit), as a PNG. A letter
        /// page that is already that size goes through untouched.</summary>
        public static byte[] LetterFit(byte[] image, out string why)
        {
            why = "";
            if (image == null || image.Length == 0) { why = "empty file"; return null; }
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!src.LoadImage(image)) { why = "not an image this game can read"; return null; }
                if (src.width == LetterCard.PixelsW && src.height == LetterCard.PixelsH) return image;
                var (x, y, w, h, rot) = LetterCard.Fit(src.width, src.height);
                var rt = RenderTexture.GetTemporary(LetterCard.PixelsW, LetterCard.PixelsH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.white);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, LetterCard.PixelsW, LetterCard.PixelsH, 0);
                if (rot) { Texture2D turned = TurnQuarter(src); Kill(src); src = turned; }   // landscape: read it turned
                Graphics.DrawTexture(new Rect(x, y, w, h), src);
                GL.PopMatrix();
                var outTex = new Texture2D(LetterCard.PixelsW, LetterCard.PixelsH, TextureFormat.RGB24, false, false);
                outTex.ReadPixels(new Rect(0, 0, LetterCard.PixelsW, LetterCard.PixelsH), 0, 0);
                outTex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                byte[] png = outTex.EncodeToPNG();
                Kill(outTex);
                return png;
            }
            finally { Kill(src); }
        }

        /// <summary>The image turned a quarter anticlockwise (its top to the left), so a landscape sheet reads upright on
        /// a portrait card turned the way a pilot turns a kneeboard.</summary>
        private static Texture2D TurnQuarter(Texture2D t)
        {
            int w = t.width, h = t.height;
            Color32[] a = t.GetPixels32(), b = new Color32[a.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    b[x * h + (h - 1 - y)] = a[y * w + x];
            var r = new Texture2D(h, w, TextureFormat.RGBA32, false, false);
            r.SetPixels32(b); r.Apply();
            return r;
        }

        /// <summary>Before 2026-11-05 a card was chosen PER AEROPLANE (sequence-cards/&lt;aircraft&gt;/card.png). Each one
        /// found becomes a card in the shared deck, once, and the old folder is renamed so it is not imported again.</summary>
        private static void MigrateOldCards()
        {
            string root = Path.Combine(Application.persistentDataPath, "sequence-cards");
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (name == "library" || name.EndsWith(".imported")) continue;
                string card = Path.Combine(dir, "card.png");
                if (!File.Exists(card)) continue;
                try
                {
                    string id = Import(File.ReadAllBytes(card), name + " card", out string why);
                    Debug.Log(id != null ? $"[SequenceCard] old {name} card moved into the shared deck" : $"[SequenceCard] old {name} card not moved: {why}");
                    Directory.Move(dir, dir + ".imported");
                }
                catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] old {name} card: {e.Message}"); }
            }
        }

        /// <summary>The Quest's way in: every PDF / PNG / JPEG in <see cref="Inbox"/> becomes a card (each PDF page one),
        /// and the file is removed once imported. Returns how many cards were added and a line for the menu.</summary>
        public static int ImportInbox(out string report)
        {
            report = "";
            int added = 0, failed = 0;
            try
            {
                Directory.CreateDirectory(Inbox);
                foreach (string f in Directory.GetFiles(Inbox))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    string title = Path.GetFileNameWithoutExtension(f);
                    bool ok = false;
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") ok = Import(File.ReadAllBytes(f), title, out _) != null;
                    else if (ext == ".pdf")
                    {
                        int pages = SequenceCardPicker.PdfPageCount(f);
                        for (int p = 0; p < pages && p < SequenceCardPicker.MaxPagesPerPdf; p++)
                        {
                            byte[] png = SequenceCardPicker.RenderPage(f, p, LetterCard.PixelsW, LetterCard.PixelsH);
                            if (png != null && Import(png, pages > 1 ? $"{title} p{p + 1}" : title, out _) != null) { ok = true; added++; }
                        }
                        if (ok) added--;   // counted per page above
                    }
                    else continue;
                    if (ok) { added++; File.Delete(f); } else failed++;
                }
            }
            catch (System.Exception e) { report = "inbox: " + e.Message; return added; }
            report = added == 0 && failed == 0 ? "inbox empty: adb push a PDF/PNG to " + Inbox
                   : $"{added} card(s) added" + (failed > 0 ? $", {failed} file(s) would not read" : "");
            return added;
        }
    }
}

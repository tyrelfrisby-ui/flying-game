using System;
using System.Collections.Generic;
using System.Text;

namespace FlyingGame.Core
{
    /// <summary>Which way the wind arrow on a card points (the IAC's Form B points left, Form C right).</summary>
    public enum IacWind { None = 0, Left = 1, Right = 2 }

    /// <summary>One card the cockpit holder can show: a built-in IAC Known (a texture in Resources/SequenceCards) or one
    /// the pilot added (a PNG in the card library on the device).</summary>
    public sealed class IacCard
    {
        /// <summary>Stable id, never renamed once shipped: the selection is kept by it (CLAUDE.md: a choice stored as an
        /// index means something else in the next list).</summary>
        public readonly string Id;
        public readonly string Title;
        public readonly string Category;
        public readonly IacWind Wind;
        public readonly bool IsUser;

        public IacCard(string id, string title, string category, IacWind wind, bool isUser)
        { Id = id; Title = title; Category = category; Wind = wind; IsUser = isUser; }

        /// <summary>What the menus print: "Sportsman Known, wind ←".</summary>
        public string Label => Wind == IacWind.None ? Title : $"{Title}, wind {(Wind == IacWind.Left ? "←" : "→")}";
        /// <summary>The same in plain ASCII, for the headset's font and the log.</summary>
        public string LabelAscii => Wind == IacWind.None ? Title : $"{Title}, wind {(Wind == IacWind.Left ? "<-" : "->")}";
    }

    /// <summary>
    /// THE COCKPIT'S CARD DECK (Ty 2026-11-05, IAC CARD SYSTEM): tap the card to cycle "wind-left Primary, wind-right
    /// Primary, wind-left Sportsman, wind-right Sportsman, ... up through Unlimited", then the pilot's own cards; upload and
    /// remove cards in Settings. ONE selection for every aeroplane and every seat: the front card always shows what the
    /// rear one shows. Pure state, no files: the Bridge (SequenceCardLibrary) loads and saves it with <see cref="Save"/>.
    /// </summary>
    public sealed class IacCardDeck
    {
        /// <summary>The 2026 IAC Power Known sequences, Form B (wind ←) and Form C (wind →) of each category's published
        /// PDF (tools/own_models/make_iac_known_cards.py). Resource name = id.</summary>
        public static readonly IReadOnlyList<IacCard> BuiltIn = new[]
        {
            Known("primary", "Primary", IacWind.Left), Known("primary", "Primary", IacWind.Right),
            Known("sportsman", "Sportsman", IacWind.Left), Known("sportsman", "Sportsman", IacWind.Right),
            Known("intermediate", "Intermediate", IacWind.Left), Known("intermediate", "Intermediate", IacWind.Right),
            Known("advanced", "Advanced", IacWind.Left), Known("advanced", "Advanced", IacWind.Right),
            Known("unlimited", "Unlimited", IacWind.Left), Known("unlimited", "Unlimited", IacWind.Right),
        };

        /// <summary>What a pilot who has never touched the card sees: the Sportsman Known, the category the GLBs shipped
        /// with on their holders.</summary>
        public const string DefaultId = "iac-2026-sportsman-left";

        /// <summary>How many of his own a pilot may keep (each is a 1275 x 1650 PNG on the device, ~0.3-1 MB).</summary>
        public const int MaxUserCards = 40;

        private static IacCard Known(string slug, string cat, IacWind w) =>
            new($"iac-2026-{slug}-{(w == IacWind.Left ? "left" : "right")}", $"{cat} Known 2026", cat, w, false);

        private readonly List<IacCard> _user = new();
        private string _currentId = DefaultId;

        /// <summary>Bumped on every change, so the holders can tell they have something new to draw.</summary>
        public int Version { get; private set; }

        public IReadOnlyList<IacCard> UserCards => _user;

        /// <summary>Built-ins first, in Ty's order, then the pilot's own in the order he added them.</summary>
        public IReadOnlyList<IacCard> All
        {
            get { var l = new List<IacCard>(BuiltIn); l.AddRange(_user); return l; }
        }

        public IacCard Current => Find(_currentId) ?? Find(DefaultId);
        public string CurrentId => Current.Id;

        public IacCard Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (IacCard c in BuiltIn) if (c.Id == id) return c;
            foreach (IacCard c in _user) if (c.Id == id) return c;
            return null;
        }

        /// <summary>The tap on the card: the next one, wrapping from the last of his own back to Primary.</summary>
        public IacCard Next() => Step(+1);
        public IacCard Previous() => Step(-1);

        private IacCard Step(int d)
        {
            IReadOnlyList<IacCard> all = All;
            int i = IndexOf(all, CurrentId);
            int n = all.Count;
            _currentId = all[((i + d) % n + n) % n].Id;
            Version++;
            return Current;
        }

        private static int IndexOf(IReadOnlyList<IacCard> all, string id)
        {
            for (int i = 0; i < all.Count; i++) if (all[i].Id == id) return i;
            return 0;
        }

        public bool Select(string id)
        {
            if (Find(id) == null) return false;
            if (_currentId != id) { _currentId = id; Version++; }
            return true;
        }

        /// <summary>A card he uploaded: it goes on the end and onto the holder. False (nothing changes) when the id is
        /// taken or the library is full.</summary>
        public bool AddUser(string id, string title)
        {
            if (string.IsNullOrEmpty(id) || Find(id) != null || _user.Count >= MaxUserCards) return false;
            _user.Add(new IacCard(id, string.IsNullOrWhiteSpace(title) ? "My card" : title.Trim(), "User", IacWind.None, true));
            _currentId = id;
            Version++;
            return true;
        }

        /// <summary>Remove one of HIS cards (the IAC Knowns cannot be removed). If it was on the holder, the holder moves
        /// on to the card that followed it.</summary>
        public bool RemoveUser(string id)
        {
            int i = _user.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            if (_currentId == id)
            {
                IReadOnlyList<IacCard> all = All;
                int k = IndexOf(all, id);
                _currentId = all[(k + 1) % all.Count].Id;
                if (_currentId == id) _currentId = DefaultId;
            }
            _user.RemoveAt(i);
            if (Find(_currentId) == null) _currentId = DefaultId;
            Version++;
            return true;
        }

        /// <summary>A fresh id for an upload, from its title: "user-" + a slug, unique in this deck.</summary>
        public string NewUserId(string title)
        {
            var sb = new StringBuilder();
            foreach (char ch in (title ?? "").ToLowerInvariant())
            {
                if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9') sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                if (sb.Length >= 32) break;
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = "card";
            string id = "user-" + slug;
            for (int n = 2; Find(id) != null; n++) id = $"user-{slug}-{n}";
            return id;
        }

        // ---- persistence: one line per entry, tab separated; unknown lines are skipped ----

        public string Save()
        {
            var sb = new StringBuilder();
            sb.Append("current\t").Append(CurrentId).Append('\n');
            foreach (IacCard c in _user) sb.Append("user\t").Append(c.Id).Append('\t').Append(c.Title.Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
            return sb.ToString();
        }

        public static IacCardDeck Load(string text)
        {
            var d = new IacCardDeck();
            if (string.IsNullOrEmpty(text)) return d;
            string current = DefaultId;
            foreach (string raw in text.Split('\n'))
            {
                string[] f = raw.TrimEnd('\r').Split('\t');
                if (f.Length >= 2 && f[0] == "current") current = f[1];
                else if (f.Length >= 2 && f[0] == "user" && f[1].StartsWith("user-", StringComparison.Ordinal) && d.Find(f[1]) == null
                         && d._user.Count < MaxUserCards)
                    d._user.Add(new IacCard(f[1], f.Length > 2 && f[2].Trim().Length > 0 ? f[2].Trim() : "My card", "User", IacWind.None, true));
            }
            d._currentId = d.Find(current) != null ? current : DefaultId;
            return d;
        }
    }

    /// <summary>A page of any shape on the holder's card: "Any 8.5x11 PDF displays as a half-size IAC card" (Ty). The
    /// card is letter portrait at half scale (4.25 x 5.5 in), so a letter page fills it exactly; any other page is fitted
    /// whole, centred on white, never cropped or stretched.</summary>
    public static class LetterCard
    {
        public const int PixelsW = 1275, PixelsH = 1650;

        /// <summary>The rectangle (pixels, origin top-left) a page of <paramref name="pageW"/> x <paramref name="pageH"/>
        /// (any unit) occupies on the card. A landscape page is turned to portrait first when <paramref name="rotateLandscape"/>
        /// (a landscape sheet printed for a kneeboard reads larger turned).</summary>
        public static (int X, int Y, int W, int H, bool Rotated) Fit(double pageW, double pageH, bool rotateLandscape = true)
        {
            if (!(pageW > 0) || !(pageH > 0)) return (0, 0, PixelsW, PixelsH, false);
            bool rot = rotateLandscape && pageW > pageH * 1.05;
            double w = rot ? pageH : pageW, h = rot ? pageW : pageH;
            double s = Math.Min(PixelsW / w, PixelsH / h);
            int fw = (int)Math.Round(w * s), fh = (int)Math.Round(h * s);
            return ((PixelsW - fw) / 2, (PixelsH - fh) / 2, fw, fh, rot);
        }
    }
}

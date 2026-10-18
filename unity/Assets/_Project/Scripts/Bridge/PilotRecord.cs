using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Ratings;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot's own record, and the badge that comes out of it.
    ///
    /// Owner, 2026-10-18: *"As a user completes lessons their badge (new feature) changes. This badge is
    /// visible to other users above their airplane with their handle or callsign"* and *"Lesson records and
    /// scores are private nobody needs to know you took 100 hours to get a Pvt license in a glider or failed
    /// the checkride 20 times but challenge scores are public."*
    ///
    /// THE PRIVACY LINE RUNS THROUGH THIS CLASS. <see cref="Log"/> stays here, in PlayerPrefs on the device.
    /// What goes out on the wire is <see cref="Identity"/> — a handle, a badge and a score, which are all
    /// results. Nothing ever serialises the log, and `PilotIdentityTests` checks that the public type has not
    /// grown a field that could leak it.
    ///
    /// The log keeps the BEST score per lesson per class, so a rating once earned is not lost by flying a bad
    /// circuit afterwards, and twenty failed attempts leave no trace anybody else can see — which is the
    /// whole point. The thing that would most discourage somebody from practising is other people watching
    /// them practise badly.
    /// </summary>
    public static class PilotRecord
    {
        private const string PrefLog = "ratings.log";
        private const string PrefAwards = "ratings.awards";
        private const string PrefBoards = "ratings.boards";

        private static PilotLog _log;
        private static RatingBook _book;
        private static bool _loaded;

        /// <summary>PRIVATE. Never sent, never shown to anybody but its owner.</summary>
        public static PilotLog Log { get { Load(); return _log; } }

        public static RatingBook Book { get { Load(); return _book; } }

        /// <summary>The non-licence badges earned, short labels. Public.</summary>
        public static readonly List<string> Awards = new();

        /// <summary>Where this pilot stands on each board. Public; comes from the server when there is one.</summary>
        public static readonly List<BoardPlacing> Placings = new();

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _book = RatingBook.Default();
            SessionSettings.LoadBadgePrefs();
            _log = PilotLog.Deserialize(PlayerPrefs.GetString(PrefLog, ""));
            foreach (string a in PlayerPrefs.GetString(PrefAwards, "").Split(';'))
                if (!string.IsNullOrEmpty(a)) Awards.Add(a);
            // Board placings are the server's to tell us; until there is one, anything stored locally is a
            // PERSONAL BEST and not a rank, so it contributes nothing to the public score. See the note in
            // docs/BADGES.md: a rank nobody else can verify is not a rank.
            _ = PrefBoards;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(PrefLog, _log.Serialize());
            PlayerPrefs.SetString(PrefAwards, string.Join(";", Awards));
            PlayerPrefs.Save();
        }

        /// <summary>Which class a flight in this aeroplane counts toward.</summary>
        public static RatingClass ClassOf(string aircraftId)
        {
            AircraftConfig cfg = null;
            try { cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(aircraftId); }
            catch (System.Exception) { }
            return Certificate.ClassOf(aircraftId, cfg);
        }

        /// <summary>
        /// A lesson has finished. Records it against the class the aeroplane belongs to, and says whether
        /// that moved the badge — which is the only thing worth announcing.
        /// </summary>
        public static bool RecordLesson(string aircraftId, string lessonId, double score)
        {
            Load();
            RatingClass cls = ClassOf(aircraftId);
            string before = Certificate.ShortBadge(RatingResolver.Resolve(_log, _book));
            _log.Record(cls, lessonId, score);
            Save();
            string after = Certificate.ShortBadge(RatingResolver.Resolve(_log, _book));
            return before != after;
        }

        /// <summary>Award a non-licence badge, if it is not already held.</summary>
        public static bool Award(string label)
        {
            Load();
            if (string.IsNullOrEmpty(label) || Awards.Contains(label)) return false;
            Awards.Add(label);
            Save();
            return true;
        }

        /// <summary>The ratings held, by class.</summary>
        public static Dictionary<RatingClass, CertLevel> Held()
        {
            Load();
            return RatingResolver.Resolve(_log, _book);
        }

        /// <summary>PUBLIC. What other pilots see: handle, badge, awards, score.</summary>
        public static PilotIdentity Identity()
        {
            Load();
            return PilotIdentity.From(SessionSettings.PilotName, _log, _book, Awards, Placings);
        }

        /// <summary>
        /// What actually goes on the wire, honouring the pilot's own publishing choice. With
        /// <see cref="SessionSettings.PublishBadge"/> off this is the handle and nothing else — the badge is
        /// not sent, not merely not drawn at the far end.
        /// </summary>
        public static PilotIdentity Published() => Identity().Published(SessionSettings.PublishBadge);

        /// <summary>The badge as other pilots will get it: empty when the pilot has chosen not to publish.</summary>
        public static string Badge() => Published().Badge;

        /// <summary>The score as other pilots will get it.</summary>
        public static double Score() => Published().Score;

        /// <summary>The badge for the pilot's OWN profile screen, which their publishing choice never hides.</summary>
        public static string OwnBadge() => Identity().Badge;

        /// <summary>The score on the pilot's own profile, likewise.</summary>
        public static double OwnScore() => PilotScore.Combined(Placings);

        /// <summary>
        /// What is still outstanding for the next rating in a class. The pilot's own screen only — this is
        /// precisely the information the owner said nobody else needs.
        /// </summary>
        public static (CertLevel next, List<(string lesson, double need, double have)> missing) NextStep(RatingClass cls)
        {
            Load();
            return RatingResolver.NextStep(_log, _book, cls);
        }

        /// <summary>Wipe it. Offered because a shared iPad is a real thing and somebody else's badge is not yours.</summary>
        public static void Forget()
        {
            Load();
            _log = new PilotLog();
            Awards.Clear();
            Placings.Clear();
            Save();
        }
    }
}

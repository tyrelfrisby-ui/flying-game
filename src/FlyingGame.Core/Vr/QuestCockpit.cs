namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// Two small rules for the cockpit in a headset, engine-free so they are pinned in QuestCockpitTests.
    /// </summary>
    public static class QuestCockpit
    {
        /// <summary>
        /// THE GREEN HUD, in a headset. Owner Ty, 2026-11-02, after the first flight on the Quest: "the hud display
        /// is not necessary and really confusing while in the airplane". In the cockpit the aeroplane's own
        /// instruments are what count, so the HUD is drawn there only when the pilot has asked for it (VR menu →
        /// HUD, off by default). Outside views and every flat screen keep it exactly as before.
        /// </summary>
        public static bool ShowHud(bool headset, bool inCockpit, bool hudInCockpitOption) =>
            !headset || !inCockpit || hudInCockpitOption;

        /// <summary>How many flights the "press MENU" tip is shown on before it is taken as learnt.</summary>
        public const int TipFlights = 3;

        /// <summary>How long the tip stays up after a flight starts, seconds, the last <see cref="TipFadeSec"/>
        /// of them fading.</summary>
        public const double TipSeconds = 12.0, TipFadeSec = 2.0;

        /// <summary>Show the tip on this flight? Not once the pilot has opened the menu himself in flight (he
        /// has found it), and not after <see cref="TipFlights"/> flights.</summary>
        public static bool ShowTip(int flightsShown, bool menuFound) => !menuFound && flightsShown < TipFlights;

        /// <summary>The tip's opacity <paramref name="sinceShownSec"/> after it appeared: 1, then a fade to 0.</summary>
        public static double TipOpacity(double sinceShownSec)
        {
            if (sinceShownSec < 0 || sinceShownSec >= TipSeconds) return 0;
            double left = TipSeconds - sinceShownSec;
            return left >= TipFadeSec ? 1 : left / TipFadeSec;
        }

        // ---- the power readout ----
        // Ty, 2026-11-02: "make sure there is a clear throttle control". The lever is the left stick (or the left
        // grip and a push of the hand), and nothing in the headset said so or showed it moving — the Pitts has no
        // throttle lever in its cockpit at all, and the HUD with its power figure is off in the cockpit. So a small
        // "POWER 65 %" card shows low on the left, by the throttle hand, whenever the lever moves, and on the first
        // flights it says how to move it.

        /// <summary>How long the readout stays up after the lever last moved, the last <see cref="PowerFadeSec"/>
        /// of it fading, seconds.</summary>
        public const double PowerHoldSec = 1.5, PowerFadeSec = 0.5;

        /// <summary>How many flights start with the how-to line, and for how long, seconds.</summary>
        public const int PowerHintFlights = 3;
        public const double PowerHintSec = 10.0;

        /// <summary>The readout's opacity <paramref name="sinceMovedSec"/> after the lever last moved.</summary>
        public static double PowerOpacity(double sinceMovedSec)
        {
            if (sinceMovedSec < 0 || sinceMovedSec >= PowerHoldSec + PowerFadeSec) return 0;
            return sinceMovedSec <= PowerHoldSec ? 1 : 1 - (sinceMovedSec - PowerHoldSec) / PowerFadeSec;
        }

        /// <summary>A lever movement worth showing: a fifth of a percent, so the sim's own rounding never wakes it.</summary>
        public static bool LeverMoved(double before01, double now01) => System.Math.Abs(now01 - before01) > 0.002;

        /// <summary>What the card says: whole percent, POWER on an engine, AIRBRAKE on a glider (the same lever is
        /// the spoiler handle there).</summary>
        public static string PowerLabel(double lever01, bool glider) =>
            (glider ? "AIRBRAKE " : "POWER ") + (int)System.Math.Round(System.Math.Clamp(lever01, 0.0, 1.0) * 100.0) + " %";

        /// <summary>The how-to line for the first flights.</summary>
        public const string PowerHint = "THROTTLE: left stick forward / back - it stays where you leave it\n(or squeeze the left grip and push your hand)";
    }
}

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
    }
}

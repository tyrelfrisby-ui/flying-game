using System;

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

        /// <summary>What the tip says. The MENU tab first: it is the one way in that needs no button to be found.</summary>
        public const string MenuTip = "MENU / OPTIONS:  touch the MENU tab (lower left)\nor press the left MENU button\nor hold the left stick click for a second";

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

    /// <summary>
    /// THE MENU TAB: a small "MENU" tab low on the left of the panel that opens the VR menu by touch, because the
    /// menu button alone was not enough (owner Ty, 2026-11-02, build a222bda: he could not open the menu in flight at
    /// all and had to restart the app to change aircraft). It must never open by accident in the middle of a loop,
    /// so each way in asks for something deliberate:
    ///
    ///   POKE     a hand's (controller's or tracked hand's) tip inside the tab for <see cref="PokeHoldSec"/>, having
    ///            come in from OUTSIDE — a hand already resting there when the tab appears, or one that has just
    ///            opened it and stays, does nothing until it has left.
    ///   POINT    the hand's ray on the tab for <see cref="HoverSec"/> (the tab lights up), then the trigger pulled.
    ///            A trigger already held when the ray arrives — braking on the runway, guns — has to be let go first.
    ///
    /// No gaze dwell: in a cockpit the eyes rest on the panel all the time.
    /// </summary>
    public sealed class QuestMenuTab
    {
        public const double PokeHoldSec = 0.2, HoverSec = 0.3, TriggerOn = 0.6, TriggerOff = 0.4;

        private double _poke, _hover;
        private bool _pokeArmed, _pokeFired, _trigArmed, _trigWas;

        /// <summary>0..1 for the tab's light: how far a hover (or poke) has got.</summary>
        public double Glow01 { get; private set; }

        /// <summary>Forget everything (the tab hidden, the menu opened by something else): the next touch starts over,
        /// and a hand already inside must leave before it counts.</summary>
        public void Reset()
        {
            _poke = _hover = 0; _pokeArmed = false; _pokeFired = false; _trigArmed = false; _trigWas = false; Glow01 = 0;
        }

        /// <summary>One frame. <paramref name="pokeInside"/>: a tip is inside the tab; <paramref name="rayOnTab"/>: a ray
        /// is on it, with <paramref name="trigger"/> that hand's trigger (0..1; a pinch's strength for a tracked hand).</summary>
        public QuestMenuTrigger Step(double dt, bool pokeInside, bool rayOnTab, double trigger)
        {
            // ---- poke ----
            if (!pokeInside) { _poke = 0; _pokeArmed = true; _pokeFired = false; }
            else if (_pokeArmed && !_pokeFired) _poke += dt;
            bool poked = _pokeArmed && !_pokeFired && _poke >= PokeHoldSec;
            if (poked) _pokeFired = true;

            // ---- point + trigger ----
            bool trig = _trigWas ? trigger > TriggerOff : trigger >= TriggerOn;
            if (!rayOnTab) { _hover = 0; _trigArmed = false; }
            else
            {
                _hover += dt;
                if (!trig) _trigArmed = true;        // a trigger held as the ray arrived must be let go first
            }
            bool pulled = rayOnTab && _trigArmed && _hover >= HoverSec && trig && !_trigWas;
            _trigWas = trig;
            if (pulled) _trigArmed = false;

            Glow01 = Math.Max(rayOnTab ? Math.Min(1.0, _hover / HoverSec) : 0.0, pokeInside && _pokeArmed ? Math.Min(1.0, _poke / PokeHoldSec) : 0.0);
            return poked ? QuestMenuTrigger.TabPoked : pulled ? QuestMenuTrigger.TabPointedTrigger : QuestMenuTrigger.None;
        }
    }
}

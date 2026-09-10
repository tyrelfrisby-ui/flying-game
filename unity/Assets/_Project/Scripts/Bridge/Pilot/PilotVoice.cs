using System.Runtime.InteropServices;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot's spoken one-liners (owner 2026-09-09): after a parachute landing he comments on how it went,
    /// from "any landing you can walk away from" to "looks like I'll be buying that farm after all". Spoken with
    /// the iOS speech synthesizer (Plugins/iOS/PilotSpeech.mm) and shown on the egress status line; in the
    /// editor the line is only logged.
    /// </summary>
    internal static class PilotVoice
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FlyingGame_Speak(string text, float rate, float pitch);
        [DllImport("__Internal")] private static extern void FlyingGame_StopSpeech();
#endif

        public static void Say(string text, float rate = 0.5f, float pitch = 0.92f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log($"[Pilot] \"{text}\"");
#if UNITY_IOS && !UNITY_EDITOR
            try { FlyingGame_Speak(text, rate, pitch); } catch (System.Exception e) { Debug.LogWarning("speech: " + e.Message); }
#endif
        }

        public static void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { FlyingGame_StopSpeech(); } catch (System.Exception) { }
#endif
        }
    }

    /// <summary>How the pilot came out of the landing, from the hardest hit he took (initial impact or any tumble).</summary>
    public enum LandingOutcome { Unhurt, Bruised, Injured, Dead }

    /// <summary>What the pilot says, by outcome. Water softens the hit; a tumble down a slope gets its own lines.</summary>
    internal static class PilotPhrases
    {
        // Impact-speed thresholds (m/s) on land; a canopy landing is ~5 m/s, a good PLF walks away from 6.
        public const float BruisedMs = 6f, InjuredMs = 9f, DeadMs = 14f;
        public const float WaterSoftening = 1.6f;   // water: the same outcome needs this much more speed

        public static LandingOutcome Outcome(float maxImpactMs, bool water)
        {
            float v = water ? maxImpactMs / WaterSoftening : maxImpactMs;
            return v < BruisedMs ? LandingOutcome.Unhurt : v < InjuredMs ? LandingOutcome.Bruised : v < DeadMs ? LandingOutcome.Injured : LandingOutcome.Dead;
        }

        private static readonly string[] Unhurt =
        {
            "Any landing you can walk away from is a good one.",
            "Stuck the landing. Ten out of ten.",
            "I meant to do that.",
            "Nailed it. Nobody saw that, right?",
            "Piece of cake. Now, where did I park the airplane?",
        };
        private static readonly string[] Bruised =
        {
            "Ow. That one's going to leave a mark.",
            "Textbook landing. Wrong textbook, but still.",
            "Any landing you can limp away from.",
            "Should've bent my knees. Should've bent everything.",
            "I'll feel that in the morning. And the afternoon.",
        };
        private static readonly string[] Injured =
        {
            "I'm fine. Nothing's broken that I was using anyway.",
            "Ow. Ow, ow, ow. Somebody find my other leg.",
            "Call a doctor. Not the flight doctor. A real one.",
            "That's going to need more than a band-aid and a beer.",
            "I've got a bone to pick with gravity. Possibly several.",
        };
        private static readonly string[] Dead =
        {
            "Looks like I'll be buying that farm after all.",
            "Looks like I'll be buying that farm after all.",   // the owner's line, weighted
            "Tell my logbook I loved her.",
            "Well. That's one way to log a full stop.",
            "Note for the accident report: the ground won.",
            "Cancel my lunch. Cancel everything, actually.",
        };
        private static readonly string[] WaterUnhurt =
        {
            "Well, at least it was a soft landing. Wet, but soft.",
            "Nice day for a swim. Not really my choice, though.",
            "Splashdown. Where's my recovery ship?",
        };
        private static readonly string[] WaterHurt =
        {
            "Belly flop. From altitude. Do not recommend.",
            "Water's supposed to be soft. Somebody lied.",
            "I hit the water and the water hit back.",
        };
        private static readonly string[] WaterDead =
        {
            "Looks like I'll be buying that farm after all. A fish farm.",
            "Tell my logbook I loved her. And that I can't swim.",
        };
        private static readonly string[] TumbleUnhurt =
        {
            "Landed fine. The hill had other ideas.",
            "Did I miss the part of the briefing about the cliff?",
            "That's the fastest I've ever gone downhill without skis.",
        };
        private static readonly string[] TumbleHurt =
        {
            "Landed once, hit the ground nine times. New record.",
            "Next time I'm aiming for the flat bit.",
            "Every rock on that hill has my name on it now.",
        };

        public static string Pick(LandingOutcome outcome, bool water, int tumbleHits)
        {
            string[] set;
            if (water) set = outcome == LandingOutcome.Dead ? WaterDead : outcome == LandingOutcome.Unhurt ? WaterUnhurt : WaterHurt;
            else if (tumbleHits >= 3 && outcome != LandingOutcome.Dead && Random.value < 0.6f) set = outcome == LandingOutcome.Unhurt ? TumbleUnhurt : TumbleHurt;
            else set = outcome switch { LandingOutcome.Unhurt => Unhurt, LandingOutcome.Bruised => Bruised, LandingOutcome.Injured => Injured, _ => Dead };
            return set[Random.Range(0, set.Length)];
        }

        public static string Label(LandingOutcome o) => o switch
        {
            LandingOutcome.Unhurt => "WALKED AWAY", LandingOutcome.Bruised => "BRUISED", LandingOutcome.Injured => "INJURED", _ => "KILLED",
        };
    }
}

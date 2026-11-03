using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE UNDERWATER CAMERA MODE: on while the EYE is below the local water surface, off above it, with a short blend
    /// at the boundary so a head bobbing through the surface does not flicker. Pure logic; the Bridge's UnderwaterRender
    /// draws it (tint, fog, caustics, the surface seen from below, muted sound).
    ///
    ///   switch     under when eye &lt; surface − <see cref="HysteresisM"/>, back above when eye &gt; surface + HysteresisM
    ///              (1 cm, E: the lens's own size, so a still head never chatters)
    ///   blend      0 → 1 over <see cref="BlendSec"/> (0.25 s, E), and back
    ///   fog        Koschmieder: a dark target vanishes against the water's own colour at the 2 % contrast threshold at
    ///              V = 3.912 / σ. <see cref="VisibilityM"/> 6 m (E: a clear-ish lake; murky water 1–2 m, open ocean 20+)
    ///              gives σ = 0.652 /m, so the belt and latch a hand's length away (0.5 m) keep 72 % of their contrast.
    /// </summary>
    public sealed class UnderwaterViewMode
    {
        public const double HysteresisM = 0.01, BlendSec = 0.25, VisibilityM = 6.0;
        public static double FogSigmaPerM => 3.912 / VisibilityM;
        /// <summary>The fraction of an object's contrast left at a distance (Beer–Lambert on the fog's σ).</summary>
        public static double ContrastLeft(double distanceM) => Math.Exp(-FogSigmaPerM * Math.Max(0.0, distanceM));

        public bool Under { get; private set; }
        public double Blend01 { get; private set; }

        public void Reset() { Under = false; Blend01 = 0.0; }

        /// <summary>One frame. <paramref name="surfaceUp"/> null: no water here, never under.</summary>
        public void Step(double eyeUp, double? surfaceUp, double dt)
        {
            if (!surfaceUp.HasValue) Under = false;
            else if (Under) { if (eyeUp > surfaceUp.Value + HysteresisM) Under = false; }
            else if (eyeUp < surfaceUp.Value - HysteresisM) Under = true;
            double step = dt > 0.0 ? dt / BlendSec : 0.0;
            Blend01 = Math.Clamp(Blend01 + (Under ? step : -step), 0.0, 1.0);
        }

        /// <summary>Snap to the eye's state with no blend (a new flight, a teleport).</summary>
        public void Snap(double eyeUp, double? surfaceUp)
        {
            Under = surfaceUp.HasValue && eyeUp < surfaceUp.Value;
            Blend01 = Under ? 1.0 : 0.0;
        }
    }

    /// <summary>The only water sounds the pilot hears (Ty, 2026-11-02 14:31).</summary>
    [Flags]
    public enum WaterCue { None = 0, Splash = 1, Lapping = 2 }

    /// <summary>What the pilot-in-water audio plays this frame.</summary>
    public readonly struct PilotWaterAudioState
    {
        public readonly WaterCue Cues;
        /// <summary>The entry splash's gain this frame (0 unless it fires).</summary>
        public readonly double SplashGain;
        public readonly double LappingGain;
        /// <summary>Low-pass corner on everything he hears, Hz (22000 = open).</summary>
        public readonly double LowPassHz;
        /// <summary>Gain anything else may give bubble / gurgle sounds near him (PilotWaterAudioGate): always 0 in water.</summary>
        public readonly double BubbleGain;
        public PilotWaterAudioState(WaterCue cues, double splash, double lapping, double lowPassHz, double bubbleGain)
        { Cues = cues; SplashGain = splash; LappingGain = lapping; LowPassHz = lowPassHz; BubbleGain = bubbleGain; }
    }

    /// <summary>
    /// THE PILOT-IN-WATER AUDIO (Ty: "NO gurgling or bubbling sounds … only a LIGHT splash on entry and gentle water-lapping
    /// ambience at the surface; while the eye is under water, the same ambience low-passed / muted (no bubble layer)").
    ///
    ///   splash    once, on entry, LIGHT: gain ∝ the entry speed, capped at <see cref="SplashMax"/> (0.35, E — a parachute
    ///             landing at 6.5 m/s is about full; a step off a sill in at 1 m/s is a quarter of it)
    ///   lapping   while in the water: <see cref="LappingGain"/> (0.25, E)
    ///   under     the same lapping, at <see cref="UnderGainFrac"/> (40 %, E) through a <see cref="UnderLowPassHz"/> corner
    ///             (500 Hz, E: what an ear in water keeps of air-borne sound — the highs are gone); the same corner on
    ///             everything else he hears
    ///   bubbles   none, ever, from us; the gate tells anything else to be silent near him
    /// </summary>
    public static class PilotWaterAudio
    {
        public const double SplashMax = 0.35, SplashFullMs = 6.5, LappingGain = 0.25, UnderGainFrac = 0.40;
        public const double UnderLowPassHz = 500.0, OpenLowPassHz = 22000.0;

        public static PilotWaterAudioState Select(bool inWater, bool eyeUnder, bool enteredThisFrame, double entrySpeedMs, double underBlend01 = -1.0)
        {
            double blend = underBlend01 >= 0.0 ? Math.Clamp(underBlend01, 0.0, 1.0) : eyeUnder ? 1.0 : 0.0;
            double lp = OpenLowPassHz * Math.Pow(UnderLowPassHz / OpenLowPassHz, blend);   // log-blend the corner
            if (!inWater) return new PilotWaterAudioState(WaterCue.None, 0.0, 0.0, lp, 1.0);
            WaterCue cues = WaterCue.Lapping;
            double splash = 0.0;
            if (enteredThisFrame)
            {
                cues |= WaterCue.Splash;
                splash = SplashMax * Math.Clamp(Math.Abs(entrySpeedMs) / SplashFullMs, 0.15, 1.0);
            }
            double lap = LappingGain * (1.0 - (1.0 - UnderGainFrac) * blend);
            return new PilotWaterAudioState(cues, splash, lap, lp, 0.0);
        }
    }
}

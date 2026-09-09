using FlyingGame.Core.DataContracts;

namespace FlyingGame.Bridge
{
    /// <summary>Synthesis recipe for a piston engine + prop (one entry per real engine family).</summary>
    internal sealed class PistonProfile
    {
        public string Name;
        public int Cylinders;                 // 4-stroke: firing pulses per crank rev = Cylinders / 2
        public float[] Harmonics;             // additive amplitudes of the firing fundamental's harmonics
        public float PulseDecayS;             // exhaust pulse decay (short = crisp crackle, long = soft chuff)
        public float Crackle;                 // noise mixed into each exhaust pulse (0..1)
        public float CylinderSpread;          // fixed per-cylinder amplitude unevenness (0..0.3)
        public float Formant1Hz, Formant1Q, Formant1Gain;   // exhaust body resonance
        public float Formant2Hz, Formant2Q, Formant2Gain;   // stack / collector resonance (0 gain = off)
        public float LpIdleHz, LpFullHz;      // load-dependent brightness
        public float ExhaustGain, ToneGain;
        public float Lope;                    // idle rpm unevenness fraction (0.05 smooth .. 0.15 lumpy radial)
        public float Mechanical;              // valve-train / gear clatter level
        public float CrankThump;              // once-per-rev thump (radial master-rod signature)
        public float PropBlades, PropReduction, PropGain, PropTipNoise;
        public float SuperchargerRatio, SuperchargerBlades, SuperchargerGain;   // ratio 0 = none
        public float TwinDetune = 1.012f;
        public float Level = 1f;

        // Filled from the aircraft config at build time.
        public float IdleRpm = 700f, MaxRpm = 2700f, PropDiameterM = 1.9f;
    }

    /// <summary>Synthesis recipe for a turbojet / turbofan.</summary>
    internal sealed class JetProfile
    {
        public string Name;
        public float IdleN1;                  // N1 fraction at idle throttle
        public float MaxShaftRpm;             // fan / first-stage compressor rpm at N1 = 100%
        public float FanBlades;               // blade-pass multiplier for the whine
        public float WhineGain, HumGain, RoarGain, RumbleGain, BuzzGain;
        public float RoarLpIdleHz, RoarLpMaxHz;
        public float SpoolS;                  // audio-side spool lag
        public float TwinDetune = 1.02f;
        public float Level = 1f;
    }

    internal static class EngineProfiles
    {
        // ---- piston families -------------------------------------------------------------------

        public static PistonProfile LycomingFlat4(float blades) => new PistonProfile
        {
            Name = "Lycoming O-320/O-360 flat-4",
            Cylinders = 4,
            Harmonics = new[] { 1f, 0.7f, 0.5f, 0.4f, 0.3f, 0.22f, 0.16f, 0.12f },
            PulseDecayS = 0.006f, Crackle = 0.25f, CylinderSpread = 0.08f,
            Formant1Hz = 420f, Formant1Q = 1.2f, Formant1Gain = 0.8f,
            Formant2Hz = 0f, Formant2Q = 1f, Formant2Gain = 0f,
            LpIdleHz = 700f, LpFullHz = 4500f,
            ExhaustGain = 0.5f, ToneGain = 0.45f,
            Lope = 0.08f, Mechanical = 0.08f, CrankThump = 0f,
            PropBlades = blades, PropReduction = 1f, PropGain = 0.22f, PropTipNoise = 0.3f,
            SuperchargerRatio = 0f,
        };

        public static PistonProfile LycomingFlat6(float blades) => new PistonProfile
        {
            Name = "Lycoming O-540/AEIO-540 flat-6",
            Cylinders = 6,
            Harmonics = new[] { 1f, 0.8f, 0.55f, 0.4f, 0.28f, 0.2f, 0.14f, 0.1f },
            PulseDecayS = 0.005f, Crackle = 0.3f, CylinderSpread = 0.07f,
            Formant1Hz = 380f, Formant1Q = 1.1f, Formant1Gain = 0.8f,
            Formant2Hz = 900f, Formant2Q = 1.5f, Formant2Gain = 0.35f,
            LpIdleHz = 750f, LpFullHz = 5000f,
            ExhaustGain = 0.52f, ToneGain = 0.45f,
            Lope = 0.07f, Mechanical = 0.07f, CrankThump = 0f,
            PropBlades = blades, PropReduction = 1f, PropGain = 0.24f, PropTipNoise = 0.32f,
            SuperchargerRatio = 0f,
        };

        /// <summary>Continental R-670 on the Stearman: 7 cylinders → 3.5 firing pulses per rev, 2 075 rpm red-line
        /// (121 Hz firing), ~500 rpm idle with a lumpy lope. Long, soft exhaust pulses through big collector rings
        /// (formant ~180 Hz), little crackle, top rolled off hard — a slow heavy drone with the master-rod thump,
        /// nothing like a two-stroke buzz. Prop tip noise kept low (2.55 m two-blade at 2 000 rpm is subsonic).</summary>
        public static PistonProfile Radial7() => new PistonProfile
        {
            Name = "Continental R-670 7-cyl radial",
            Cylinders = 7,
            Harmonics = new[] { 1f, 0.55f, 0.28f, 0.14f, 0.07f },
            PulseDecayS = 0.022f, Crackle = 0.12f, CylinderSpread = 0.16f,
            Formant1Hz = 180f, Formant1Q = 0.9f, Formant1Gain = 1.0f,
            Formant2Hz = 420f, Formant2Q = 1.1f, Formant2Gain = 0.25f,
            LpIdleHz = 450f, LpFullHz = 1800f,
            ExhaustGain = 0.6f, ToneGain = 0.35f,
            Lope = 0.16f, Mechanical = 0.06f, CrankThump = 0.5f,
            PropBlades = 2f, PropReduction = 1f, PropGain = 0.22f, PropTipNoise = 0.12f,
            SuperchargerRatio = 0f,
        };

        public static PistonProfile Radial14() => new PistonProfile
        {
            Name = "P&W R-1830 14-cyl twin-row radial",
            Cylinders = 14,
            Harmonics = new[] { 1f, 0.75f, 0.5f, 0.35f, 0.22f, 0.15f, 0.1f },
            PulseDecayS = 0.008f, Crackle = 0.3f, CylinderSpread = 0.1f,
            Formant1Hz = 300f, Formant1Q = 0.9f, Formant1Gain = 0.9f,
            Formant2Hz = 800f, Formant2Q = 1.3f, Formant2Gain = 0.3f,
            LpIdleHz = 550f, LpFullHz = 2600f,
            ExhaustGain = 0.55f, ToneGain = 0.4f,
            Lope = 0.1f, Mechanical = 0.08f, CrankThump = 0.35f,
            PropBlades = 3f, PropReduction = 0.5625f, PropGain = 0.28f, PropTipNoise = 0.18f,
            SuperchargerRatio = 0f, TwinDetune = 1.013f,
        };

        /// <summary>
        /// Packard Merlin V-1650 for the P-51D. 12 cylinders → 6 firing pulses per crank rev
        /// (f0 = rpm/10 Hz: 270 Hz at 2700 rpm), rich 2nd–5th harmonics (the smooth snarl), very short
        /// exhaust pulses with heavy crackle through 12 short stacks (formant ~1.15 kHz) over a V-12 body
        /// resonance (~340 Hz), two-stage supercharger at ~7× crank with an 8-vane-ish whine that rises with
        /// boost, and a 4-blade prop at 0.479:1 (blade-pass 86 Hz at 2700 rpm) thrumming underneath.
        /// </summary>
        public static PistonProfile MerlinV12() => new PistonProfile
        {
            Name = "Packard Merlin V-1650 V-12",
            Cylinders = 12,
            Harmonics = new[] { 1f, 0.92f, 0.85f, 0.75f, 0.6f, 0.42f, 0.3f, 0.22f, 0.16f, 0.12f },
            PulseDecayS = 0.0022f, Crackle = 0.6f, CylinderSpread = 0.12f,
            Formant1Hz = 340f, Formant1Q = 1.2f, Formant1Gain = 0.8f,
            Formant2Hz = 1150f, Formant2Q = 1.8f, Formant2Gain = 1.1f,
            LpIdleHz = 900f, LpFullHz = 7000f,
            ExhaustGain = 0.55f, ToneGain = 0.5f,
            Lope = 0.05f, Mechanical = 0.05f, CrankThump = 0f,
            PropBlades = 4f, PropReduction = 0.479f, PropGain = 0.28f, PropTipNoise = 0.3f,
            SuperchargerRatio = 7f, SuperchargerBlades = 8f, SuperchargerGain = 0.16f,
        };

        // ---- jets -------------------------------------------------------------------------------

        public static JetProfile J47Turbojet() => new JetProfile
        {
            Name = "GE J47 axial turbojet",
            IdleN1 = 0.32f, MaxShaftRpm = 7950f, FanBlades = 30f,
            WhineGain = 0.22f, HumGain = 0.12f, RoarGain = 0.5f, RumbleGain = 0.1f, BuzzGain = 0f,
            RoarLpIdleHz = 500f, RoarLpMaxHz = 5500f, SpoolS = 0.9f,
        };

        public static JetProfile HighBypassTurbofan() => new JetProfile
        {
            Name = "CFM56-class high-bypass turbofan",
            IdleN1 = 0.22f, MaxShaftRpm = 5200f, FanBlades = 36f,
            WhineGain = 0.16f, HumGain = 0.15f, RoarGain = 0.38f, RumbleGain = 0.35f, BuzzGain = 0.14f,
            RoarLpIdleHz = 250f, RoarLpMaxHz = 3500f, SpoolS = 1.1f, TwinDetune = 1.02f,
        };

        // ---- selection ---------------------------------------------------------------------------

        /// <summary>Piston recipe for an aircraft id (null for jets / gliders). Falls back on cylinder
        /// count guessed from power for unknown ids.</summary>
        public static PistonProfile PistonFor(string id, AircraftConfig config)
        {
            PropulsionConfig p = config?.Propulsion;
            if (p == null || p.PropDiameterM <= 0.0) return null;
            PistonProfile prof;
            if (id.StartsWith("p51")) prof = MerlinV12();
            else if (id.StartsWith("dc3")) prof = Radial14();
            else if (id.StartsWith("stearman")) prof = Radial7();
            else if (id.StartsWith("pa25") || id.StartsWith("pitts")) prof = LycomingFlat6(2f);
            else if (id.StartsWith("extra")) prof = LycomingFlat6(3f);
            else if (id.StartsWith("c172") || id.StartsWith("pa18") || id.StartsWith("decathlon") || id.StartsWith("seminole")) prof = LycomingFlat4(2f);
            else prof = p.MaxPowerW > 160000 ? LycomingFlat6(2f) : LycomingFlat4(2f);

            prof.IdleRpm = (float)(p.IdleRpm > 0 ? p.IdleRpm : 700.0);
            prof.MaxRpm = (float)(p.MaxRpm > prof.IdleRpm ? p.MaxRpm : prof.IdleRpm + 2000.0);
            prof.PropDiameterM = (float)p.PropDiameterM;
            return prof;
        }

        public static JetProfile JetFor(string id, AircraftConfig config)
        {
            PropulsionConfig p = config?.Propulsion;
            if (p == null || p.PropDiameterM > 0.0) return null;
            if (id.StartsWith("f86")) return J47Turbojet();
            if (id.StartsWith("boeing")) return HighBypassTurbofan();
            return (config.Engines != null && config.Engines.Count >= 2) ? HighBypassTurbofan() : J47Turbojet();
        }
    }
}

using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>
/// What the wheels, brakes, skis and floats should SOUND like this frame, decided from the physics (docs
/// TIRE-SPINUP-AND-GROUND-SOUND.md §5). Pure arithmetic, so it is tested in the suite; Bridge/Audio's TyreBrakeSynth
/// only renders the numbers. Main thread, once a frame, no allocation after construction.
///
/// Per wheel, the ground under THAT wheel (not under the CG) drives a weight per <see cref="GroundMaterial"/>, eased
/// toward the new ground with the row's crossfade time and normalised so the weights sum to one; the voices get the
/// square roots (equal power), so a wheel crossing a strip edge keeps its loudness while the timbre changes over.
/// Each wheel is panned by its side (± <see cref="PanWidth"/>, equal-power), and voices are summed per side.
/// </summary>
public sealed class GroundSoundMix
{
    public const int Keys = GroundMaterials.Count;
    /// <summary>One gain on everything the ground makes heard except the tyre screech (<see cref="ScreechGainDb"/>):
    /// the rolling voices, the tyre's scrub, the
    /// bearing whir, the brakes, a worn or flat tyre, water and ski contact, a float keel, and the airframe's own scrape
    /// (ContactEffects). FlightAudio multiplies the ground bus by it after the synths, so every level above keeps its
    /// meaning and no pitch or filter moves. Ty 2026-11-05 ("just a little too loud so lower all ground contact
    /// volume a bit"): −4 dB.</summary>
    public const double OutputGainDb = -4.0;
    /// <summary><see cref="OutputGainDb"/> as a linear factor (10^(−4/20) ≈ 0.631).</summary>
    public static readonly float OutputGain = (float)System.Math.Pow(10.0, OutputGainDb / 20.0);
    /// <summary>The tyre SCREECH (TyreBrakeSynth's squeal voice: the spin-up chirp, a skid, a locked wheel) does NOT
    /// take the ground bus's cut. Ty 2026-11-05 ~15:57 ("Tire screech"): screech loudness unchanged from before the
    /// −4 dB; the bearing whir, scrub, rolling ground, brakes, water/ski/keel and airframe scrape stay at −4 dB.</summary>
    public const double ScreechGainDb = 0.0;
    /// <summary><see cref="ScreechGainDb"/> as a linear factor.</summary>
    public static readonly float ScreechGain = (float)System.Math.Pow(10.0, ScreechGainDb / 20.0);
    /// <summary>Stereo position of a side wheel, −1..1 (Ty: about half-way, from the cockpit).</summary>
    public const double PanWidth = 0.5;
    /// <summary>Bearing whir frequency per wheel revolution per second: the cage and rolling-element passing tones of a
    /// small tapered-roller bearing sit at ~8–10× the shaft rate.</summary>
    public const double WhirHzPerRevS = 9.0;
    /// <summary>Whir gain at the reference rim speed. Ty 2026-11-05 ("bearing noise is too loud"): −14 dB from the
    /// first 0.016, i.e. ≈ −50 dB below the engine's full level, and it grows with the SQUARE of the wheel's rpm (≈ 40 dB
    /// a decade, the steep end of what rolling-bearing noise does), so at taxi speed it is all but gone and only a fast
    /// roll or a coasting glider lets you hear it.</summary>
    public const double WhirGainAtRef = 0.0032, WhirRefSpeedMs = 25.7, WhirSpeedExponent = 2.0;
    public const double SquealPressure = 0.6, SquealSlip = 0.08;

    // ---- The tyre's own voice: squeal and scrub, from the contact patch (Ty 2026-11-05) -------------------------
    // What is heard is the rubber sliding in the patch, decided by two things the physics already has: how fast the
    // patch slides (its mean slip speed, E / ∫|F|dt, fore and aft and sideways separately) and how big the tyre is.
    //  - PITCH. Rubber squeal is stick-slip of the tread excited at the tread band's own tangential modes, whose
    //    frequencies go as a wave speed over a length of the tyre: f ∝ 1/OD. Reference: a ~25 in passenger tyre
    //    squeals around 1.1 kHz (ESTIMATE from the 0.7–1.3 kHz the tyre-noise literature reports), so
    //    f·OD ≈ 700 Hz·m: a 6.00-6 (17.5 in) 1.57 kHz, the J-3's 8.00-4 1.53 kHz, a 5.00-5 nose 1.94 kHz, an 8 in
    //    tailwheel 3.4 kHz, a 35 in bushwheel 0.79 kHz, a 737 main 0.64 kHz. Faster sliding drives the stick-slip
    //    faster and lifts it a little, (v/10 m/s)^0.2 bounded 0.6–1.35, so a spin-up chirp falls as the wheel catches
    //    up (25 m/s → ×1.20, 2 m/s → ×0.72).
    //  - SQUEAL OR SCRUB. Rubber's friction peaks at a low sliding speed and falls past it (Grosch 1963); the falling
    //    branch is what makes stick-slip lock into a tone. So the faster the patch slides the more of its power is a
    //    squeal, and the slower the more a broad, low, rubbery scrub: TonalFraction ramps from 1.5 to 6 m/s (ESTIMATE
    //    of where it locks). A spin-up starts at the touchdown speed (squeal) and ends in a scuff; a crabbed touchdown
    //    slides sideways at 1–5 m/s and scrubs; a tyre skidding sideways fast squeals as a car's does.
    //  - LEVEL. Loudness goes with the log of the slip power the patch dissipates, from audible (50 W) to full (50 kW),
    //    times the ground row's Chirp (full on asphalt and decks, a scuff on dirt, none on grass).
    /// <summary>f·OD of the tread's squeal (Hz·m): ~1.1 kHz on a 25 in passenger tyre (ESTIMATE).</summary>
    public const double SquealHzMetre = 700.0;
    public const double SquealRefSlipMs = 10.0, SquealSlipExponent = 0.2, SquealMinFactor = 0.6, SquealMaxFactor = 1.35;
    /// <summary>The scrub's centre sits well under the squeal (broadband abrasion, Q ~1), rising a little with slip.</summary>
    public const double ScrubHzFraction = 0.42, ScrubRefSlipMs = 3.0, ScrubSlipExponent = 0.3, ScrubMinFactor = 0.6, ScrubMaxFactor = 1.6;
    /// <summary>Slip speeds (m/s) over which the patch's sliding goes from all scrub to all squeal (ESTIMATE).</summary>
    public const double TonalFromMs = 1.5, TonalToMs = 6.0;
    /// <summary>Slip power (W) for full level: three decades above audible.</summary>
    public const double FullSlipPowerW = 50000.0;

    // ---- When the tyre starts to be heard (Ty 2026-11-05 08:50) -----------------------------------------------------
    // "i took off in a crosswind in a cessna no brakes and the tires were howling so make the threshold higher on when
    // the screetching starts and make it more of a fade in". A tyre under its friction peak is mostly gripping — only a
    // sliver at the back of the patch slides — and it is quiet; it squeals once the slip passes the peak and the patch
    // slides. So each joule of slip is heard in proportion to how far past the peak the tyre was when it made it,
    // measured against the TYRE MODEL's own peak (not a fixed angle):
    //  - SIDEWAYS: the cornering force the slip angle asks for over what the tyre has left sideways (LandingGear's
    //    friction cap). 1 is the peak slip angle (μ over the cornering stiffness per load: 3.8° for a main at 0.8 / 12,
    //    5.7° for a tailwheel); the squeal fades in from 1× to 2× it (full by 7.6° on a main), a smoothstep, and over
    //    <see cref="SideSquealAttackS"/> in time, so a gust or a steering wobble across the peak does not switch it on.
    //    The scrub fades in across the peak, from 0.75× to 1.5×: a faint rub as the tyre reaches it, and not before.
    //  - FORE AND AFT: the slip ratio over the tyre's own slip scale κ0 (TireModel.SlipRatioScale; 0.08 nominal, whose
    //    tanh is 96 % of the peak at 2 κ0): the squeal from 1.25 κ0 to 2.5 κ0 (0.10 → 0.20 nominal), the scrub from
    //    0.5 κ0 to 1.25 κ0. No time smoothing here: a touchdown spin-up is over in a few tens of ms at slip ratios near 1,
    //    and that chirp is the sound (Ty 08:48: "pretty good"); a locked wheel is at slip ratio 1 from the start.
    // Level and pitch are unchanged: the onset multiplies the level only, so the squeal still pitches with the patch's
    // slip speed and the tyre's diameter.
    /// <summary>Sideways squeal onset band, in multiples of the tyre's friction-peak slip angle.</summary>
    public const double SideSquealFrom = 1.0, SideSquealTo = 2.0;
    /// <summary>Sideways scrub onset band, in multiples of the friction-peak slip angle.</summary>
    public const double SideScrubFrom = 0.75, SideScrubTo = 1.5;
    /// <summary>Fore-aft squeal and scrub onset bands, in multiples of the tyre's slip-ratio scale κ0.</summary>
    public const double ForeSquealFrom = 1.25, ForeSquealTo = 2.5, ForeScrubFrom = 0.5, ForeScrubTo = 1.25;
    /// <summary>Time constants of the sideways squeal's onset (s): it builds over ~0.2 s, and lets go a little faster.</summary>
    public const double SideSquealAttackS = 0.2, SideSquealReleaseS = 0.1;

    /// <summary>0 at <paramref name="from"/>, 1 at <paramref name="to"/>, smooth (zero slope) at both ends.</summary>
    public static double Smoothstep(double from, double to, double x)
    {
        if (!double.IsFinite(x)) return x > 0 ? 1 : 0;
        double u = System.Math.Clamp((x - from) / (to - from), 0, 1);
        return u * u * (3 - 2 * u);
    }

    /// <summary>How much of a sideways slip is heard as squeal, by its demand over the tyre's sideways grip (1 = peak).</summary>
    public static double SideSquealOnset(double demand) => Smoothstep(SideSquealFrom, SideSquealTo, demand);
    /// <summary>The same for the sideways scrub.</summary>
    public static double SideScrubOnset(double demand) => Smoothstep(SideScrubFrom, SideScrubTo, demand);
    /// <summary>How much of a fore-aft slip is heard as squeal, by its slip ratio over the tyre's slip scale κ0.</summary>
    public static double ForeSquealOnset(double kappaOverK0) => Smoothstep(ForeSquealFrom, ForeSquealTo, kappaOverK0);
    /// <summary>The same for the fore-aft scrub.</summary>
    public static double ForeScrubOnset(double kappaOverK0) => Smoothstep(ForeScrubFrom, ForeScrubTo, kappaOverK0);

    /// <summary>The tread's squeal resonance for a tyre of outside diameter <paramref name="odM"/>.</summary>
    public static double SquealResonanceHz(double odM) => SquealHzMetre / System.Math.Max(odM, 0.05);

    /// <summary>The squeal's pitch: the tyre's resonance, lifted or lowered by how fast the patch slides.</summary>
    public static double TyreSquealHz(double odM, double slipMs) =>
        SquealResonanceHz(odM) * System.Math.Clamp(System.Math.Pow(System.Math.Max(slipMs, 1e-3) / SquealRefSlipMs, SquealSlipExponent),
                                                 SquealMinFactor, SquealMaxFactor);

    /// <summary>The scrub's centre frequency: well below the squeal, rising a little with the slip speed.</summary>
    public static double TyreScrubHz(double odM, double slipMs) =>
        ScrubHzFraction * SquealResonanceHz(odM)
        * System.Math.Clamp(System.Math.Pow(System.Math.Max(slipMs, 1e-3) / ScrubRefSlipMs, ScrubSlipExponent), ScrubMinFactor, ScrubMaxFactor);

    /// <summary>Share of the patch's slip power that is a squeal (the rest is scrub), 0..1 by its slip speed.</summary>
    public static double TonalFraction(double slipMs)
    {
        double x = System.Math.Clamp((slipMs - TonalFromMs) / (TonalToMs - TonalFromMs), 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Level 0..1 of a slip power: log from audible (<see cref="WheelSpin.AudibleSlipPowerW"/>) to
    /// <see cref="FullSlipPowerW"/>.</summary>
    public static double SlipLevel(double powerW) =>
        powerW <= WheelSpin.AudibleSlipPowerW ? 0
        : System.Math.Clamp(System.Math.Log10(powerW / WheelSpin.AudibleSlipPowerW) / System.Math.Log10(FullSlipPowerW / WheelSpin.AudibleSlipPowerW), 0, 1);

    // Output, per side (0 = left, 1 = right).
    public readonly float[] RollingGain = new float[2 * Keys];   // [side * Keys + key]
    public readonly float[] RollingPitch = new float[Keys];
    public readonly float[] WhirHz = new float[2], WhirGain = new float[2];
    /// <summary>Seconds of audible tyre squeal this frame (spin-up chirp, or a fast skid), its level 0..1 and its pitch
    /// (Hz, <see cref="TyreSquealHz"/>). The voice plays for exactly the slip time it is given.</summary>
    public readonly float[] ChirpSeconds = new float[2], ChirpGain = new float[2], ChirpHz = new float[2];
    /// <summary>The same for the low rubbery SCRUB of a slowly sliding patch (a crabbed touchdown, the tail of a spin-up,
    /// a tight turn): seconds, level 0..1, centre frequency (Hz, <see cref="TyreScrubHz"/>).</summary>
    public readonly float[] ScrubSeconds = new float[2], ScrubLevel = new float[2], ScrubCentreHz = new float[2];
    public readonly float[] ScrapeGain = new float[2], ScrapeHz = new float[2];
    public readonly float[] SquealGain = new float[2], SquealHz = new float[2];
    public float WaterSplash, WaterHiss, Spray, SkiHiss, KeelScrape;
    /// <summary>A worn or flat tyre (TyreWear), per side: the axle's work this frame dropping into and climbing out of the
    /// patch's lows (J: the thump, it comes exactly when the bad strip passes under the axle), the slap of a flat casing
    /// once per revolution (its rate and level), and the tyres that burst this frame.</summary>
    public readonly float[] ThumpJ = new float[2], FlapHz = new float[2], FlapGain = new float[2];
    public readonly int[] Pops = new int[2];
    /// <summary>Thump work (J) per frame that is full level: a flat 172 main climbs ~3 mm in and out of its burst strip at
    /// ~4.7 kN, ~30 J a revolution (TyreWearTests measures ~460 J/s at 12 rev/s).</summary>
    public const double ThumpRefJ = 30.0;

    private readonly double[] _w;           // [wheel * Keys + key], smoothed, sum 1 per wheel
    private readonly bool[] _seen;
    private readonly int _n;

    public GroundSoundMix(int wheels)
    {
        _n = wheels;
        _w = new double[wheels * Keys];
        _seen = new bool[wheels];
        _bursts = new int[wheels];
        _sideSqueal = new double[wheels];
    }

    private readonly double[] _sideSqueal;   // per wheel, the sideways squeal's onset as it has built up (0..1)

    /// <summary>One wheel's sideways squeal onset, smoothed in time (<see cref="SideSquealAttackS"/>).</summary>
    public double SideSquealEnvelope(int wheel) => _sideSqueal[wheel];

    private readonly int[] _bursts;

    /// <summary>One wheel's smoothed weight on a key (sums to 1 over keys once the wheel has touched anything).</summary>
    public double Weight(int wheel, GroundMaterial k) => _w[wheel * Keys + (int)k];

    /// <summary>Equal-power gain of a key for a wheel: √weight, so Σ gain² = 1 through any crossfade.</summary>
    public double EqualPower(int wheel, GroundMaterial k) => System.Math.Sqrt(Weight(wheel, k));

    /// <summary>Equal-power pan of a stereo position −1..1: (left, right) with L² + R² = 1.</summary>
    public static (double L, double R) Pan(double pos)
    {
        double a = (System.Math.Clamp(pos, -1, 1) + 1) * System.Math.PI / 4;
        return (System.Math.Cos(a), System.Math.Sin(a));
    }

    /// <summary>Ease a wheel's key weights toward the ground it is on now.</summary>
    public void Crossfade(int i, GroundMaterial target, double dt)
    {
        int o = i * Keys;
        if (!_seen[i])
        {
            for (int k = 0; k < Keys; k++) _w[o + k] = 0;
            _w[o + (int)target] = 1; _seen[i] = true;
            return;
        }
        double sum = 0;
        for (int k = 0; k < Keys; k++)
        {
            double tau = GroundMaterials.Row((GroundMaterial)k).CrossfadeS;
            double a = 1 - System.Math.Exp(-dt / System.Math.Max(1e-3, tau));
            double goal = k == (int)target ? 1 : 0;
            _w[o + k] += (goal - _w[o + k]) * a;
            sum += _w[o + k];
        }
        if (sum > 1e-12) for (int k = 0; k < Keys; k++) _w[o + k] /= sum;
    }

    /// <summary>
    /// The frame's sound. <paramref name="floats"/> is FloatHydro's per-float state (or empty), <paramref name="weightN"/>
    /// the aircraft's weight. Each wheel's slip accumulators are read and cleared here (<see cref="WheelState.TakeSlip"/>).
    /// </summary>
    public void Update(IReadOnlyList<WheelState> wheels, double weightN, double dt, FloatHydro.FloatState[]? floats = null, int floatCount = 0)
    {
        System.Array.Clear(RollingGain, 0, RollingGain.Length);
        System.Array.Clear(RollingPitch, 0, RollingPitch.Length);
        for (int s = 0; s < 2; s++)
        {
            WhirHz[s] = WhirGain[s] = ChirpSeconds[s] = ChirpGain[s] = ChirpHz[s] = 0;
            ScrubSeconds[s] = ScrubLevel[s] = ScrubCentreHz[s] = 0;
            ScrapeGain[s] = ScrapeHz[s] = SquealGain[s] = SquealHz[s] = 0;
            ThumpJ[s] = FlapHz[s] = FlapGain[s] = 0; Pops[s] = 0;
        }
        WaterSplash = WaterHiss = Spray = SkiHiss = KeelScrape = 0;
        double totalLoad = 0;
        int n = System.Math.Min(_n, wheels.Count);
        for (int i = 0; i < n; i++) if (wheels[i].InContact) totalLoad += System.Math.Max(0, wheels[i].LoadN);
        double loadRef = System.Math.Max(totalLoad, 0.25 * weightN);   // a wheel just kissing the ground is quiet
        double[] pitchW = _pitchW; System.Array.Clear(pitchW, 0, Keys);

        for (int i = 0; i < n; i++)
        {
            WheelState w = wheels[i];
            (double pl, double pr) = Pan(w.Side * PanWidth);
            double share = loadRef > 1e-9 ? System.Math.Max(0, w.LoadN) / loadRef : 0;
            double speed = System.Math.Abs(w.VFwdMs);
            w.TakeSlip(out WheelState.Slip fore, out WheelState.Slip side);
            {
                // The sideways squeal builds up over ~0.2 s toward this frame's onset (none off the hard ground).
                bool heard = w.IsWheel && w.InContact && !w.OverWater && !w.OnSki && side.TimeS > 0 && side.EnergyJ > 0;
                double goal = heard ? side.SquealOnset : 0;
                double tau = goal > _sideSqueal[i] ? SideSquealAttackS : SideSquealReleaseS;
                _sideSqueal[i] += (goal - _sideSqueal[i]) * (1 - System.Math.Exp(-System.Math.Max(dt, 0) / tau));
            }
            double thumpJ = w.ThumpJ; w.ThumpJ = 0;
            int pops = w.Bursts - (i < _bursts.Length ? _bursts[i] : 0);
            if (i < _bursts.Length) _bursts[i] = w.Bursts;
            if (pops > 0)
            {
                // A burst is heard wherever the wheel is, ground or not.
                if (w.Side <= 0) Pops[0] += pops;
                if (w.Side >= 0) Pops[1] += pops;
            }

            if (!w.IsWheel)
            {
                // A float keel on land slides on its skid strip; skids and feet scrape too.
                if (w.Scrapes && w.InContact && !w.OverWater) KeelScrape += (float)(share * System.Math.Clamp(speed / 10.0, 0, 1));
                continue;
            }

            // The wheel's bearing, from its own spin: in the air too, so it winds down after take-off; never in the
            // water or on snow (Ty), where you would not hear it.
            GroundMaterialRow? row = null;
            if (w.InContact)
            {
                Crossfade(i, w.Material, dt);
                row = GroundMaterials.Row(w.Material);
            }
            bool quietBearing = w.InContact && (w.OverWater || w.OnSki || !row!.BearingWhir);
            double rim = System.Math.Abs(w.RimSpeedMs);
            if (!quietBearing && rim > 0.2 && w.RadiusM > 0)
            {
                double revS = rim / (2 * System.Math.PI * w.RollingRadiusM);
                double g = WhirGainAtRef * System.Math.Pow(rim / WhirRefSpeedMs, WhirSpeedExponent);
                AddSide(WhirGain, pl, pr, g);
                double hz = WhirHzPerRevS * revS;
                if (hz > WhirHz[0]) WhirHz[0] = (float)hz;   // one tone per side, the fastest wheel's
                if (hz > WhirHz[1]) WhirHz[1] = (float)hz;
            }
            if (!w.InContact) continue;

            if (w.OverWater)
            {
                // A wheel in the water: splash at low speed, a planing hiss once it rides up (WheelHydro).
                double v = System.Math.Clamp(speed / 20.0, 0, 1);
                WaterSplash += (float)(share * (1 - v) * System.Math.Clamp(speed / 2.0, 0, 1));
                WaterHiss += (float)(share * v);
                continue;
            }
            if (w.OnSki)
            {
                SkiHiss += (float)(share * System.Math.Clamp(speed / 15.0, 0, 1));
                continue;
            }

            // Rolling voices, every key the wheel's weights reach (equal power), panned.
            for (int k = 0; k < Keys; k++)
            {
                double wk = _w[i * Keys + k];
                if (wk < 1e-4) continue;
                GroundMaterialRow rk = GroundMaterials.Row((GroundMaterial)k);
                if (rk.Voice == RollingVoice.None) continue;
                double g = System.Math.Sqrt(wk) * share * GroundMaterials.Curve(rk.GainVsSpeed, speed);
                RollingGain[k] += (float)(g * pl);
                RollingGain[Keys + k] += (float)(g * pr);
                RollingPitch[k] += (float)(g * GroundMaterials.Curve(rk.PitchVsSpeed, speed));
                pitchW[k] += g;
            }

            // The tyre's voice: the slip power the patch actually dissipated, for as long as it slipped audibly, fore and
            // aft and sideways, each split into squeal and scrub by how fast it slid, scaled by how much of it the rubber
            // makes heard on this ground (none on grass, a scuff on dirt).
            double chirp = row!.Chirp;
            if (chirp > 0)
            {
                int sIdx = w.Side < 0 ? 0 : w.Side > 0 ? 1 : -1;
                Voice(fore, fore.SquealOnset, w.SoundOdM, chirp, sIdx, pl, pr);
                Voice(side, System.Math.Min(side.SquealOnset, _sideSqueal[i]), w.SoundOdM, chirp, sIdx, pl, pr);
            }

            // A worn or flat tyre: the thump is the work the axle did on its lumps (it arrives when the low strip passes
            // under it, so it is in time with the wheel); a flat's loose casing slaps once a revolution, louder the
            // flatter it is and the more it carries.
            if (thumpJ > 0) AddSide(ThumpJ, pl, pr, thumpJ / 0.7071);
            if (w.Tyre != null && w.Tyre.PressureFraction < 0.9 && rim > 0.2 && w.RollingRadiusM > 0)
            {
                double revS = rim / (2 * System.Math.PI * w.RollingRadiusM);
                double g = (1 - w.Tyre.PressureFraction) * System.Math.Clamp(share * 2, 0, 1) * System.Math.Clamp(rim / 6.0, 0, 1);
                int sIdx = w.Side < 0 ? 0 : w.Side > 0 ? 1 : -1;
                for (int s = 0; s < 2; s++)
                {
                    if (sIdx >= 0 && s != sIdx) continue;
                    if (g > FlapGain[s]) { FlapGain[s] = (float)g; FlapHz[s] = (float)revS; }
                }
            }

            // Brakes: the pads on a turning disc scrape in proportion to the brake's power (pressure × wheel speed);
            // they squeal hard on or near lock-up. Neither in the water or on skis (above), nor where the row says so.
            double p = System.Math.Clamp(w.Brake01, 0, 1);
            if (p > 0 && rim > 0.3)
            {
                double vf = System.Math.Clamp(rim / 15.0, 0, 1);
                if (row.BrakeScrape)
                {
                    AddSide(ScrapeGain, pl, pr, 0.35 * p * vf);
                    float hz = (float)(300 + 600 * vf);
                    ScrapeHz[0] = System.Math.Max(ScrapeHz[0], hz); ScrapeHz[1] = System.Math.Max(ScrapeHz[1], hz);
                }
                double near = System.Math.Max((p - SquealPressure) / (1 - SquealPressure), (System.Math.Abs(w.SlipRatio) - SquealSlip) / 0.2);
                if (row.BrakeSqueal && near > 0)
                {
                    AddSide(SquealGain, pl, pr, 0.25 * System.Math.Clamp(near, 0, 1) * System.Math.Clamp(rim / 10.0, 0, 1));
                    float hz = (float)(2200 + 1300 * p);
                    SquealHz[0] = System.Math.Max(SquealHz[0], hz); SquealHz[1] = System.Math.Max(SquealHz[1], hz);
                }
            }
        }
        for (int k = 0; k < Keys; k++) RollingPitch[k] = pitchW[k] > 1e-9 ? (float)(RollingPitch[k] / pitchW[k]) : 1f;

        // Floats in the water (FloatHydro): hull slap off the step, a planing hiss on it, spray from its spray force.
        if (floats != null)
        {
            for (int f = 0; f < floatCount && f < floats.Length; f++)
            {
                FloatHydro.FloatState fs = floats[f];
                if (fs.WaterUpN <= 0 || weightN <= 0) continue;
                double share = System.Math.Clamp(fs.WaterUpN / weightN, 0, 1.5);
                double held = fs.PlaningLiftN + fs.BuoyancyN;
                double planing = held > 1e-6 ? System.Math.Clamp(fs.PlaningLiftN / held, 0, 1) : 0;
                WaterSplash += (float)(share * (1 - planing) * System.Math.Clamp(fs.SpeedMs / 3.0, 0, 1));
                WaterHiss += (float)(share * planing * System.Math.Clamp(fs.SpeedMs / 15.0, 0, 1));
                Spray += (float)System.Math.Clamp(fs.SprayN / System.Math.Max(1, 0.1 * weightN), 0, 1);
            }
        }
    }

    private readonly double[] _pitchW = new double[Keys];

    /// <summary>One direction's slip into the squeal and scrub voices of the wheel's side(s); the loudest wheel on a side
    /// sets that side's pitch. <paramref name="squealOnset"/> and the slip's own scrub onset scale the levels only.</summary>
    private void Voice(in WheelState.Slip slip, double squealOnset, double odM, double chirp, int sIdx, double pl, double pr)
    {
        if (slip.TimeS <= 0 || slip.EnergyJ <= 0) return;
        double power = slip.EnergyJ / System.Math.Max(slip.TimeS, 1e-4);
        double tonal = TonalFraction(slip.SpeedMs);
        double squeal = chirp * SlipLevel(power * tonal) * squealOnset, scrub = chirp * SlipLevel(power * (1 - tonal)) * slip.ScrubOnset;
        for (int s = 0; s < 2; s++)
        {
            if (sIdx >= 0 && s != sIdx) continue;
            double pan = (s == 0 ? pl : pr) / 0.7071;
            if (squeal > 0)
            {
                float g = (float)(squeal * pan);
                ChirpSeconds[s] = System.Math.Max(ChirpSeconds[s], (float)slip.TimeS);
                if (g >= ChirpGain[s]) { ChirpGain[s] = g; ChirpHz[s] = (float)TyreSquealHz(odM, slip.SpeedMs); }
            }
            if (scrub > 0)
            {
                float g = (float)(scrub * pan);
                ScrubSeconds[s] = System.Math.Max(ScrubSeconds[s], (float)slip.TimeS);
                if (g >= ScrubLevel[s]) { ScrubLevel[s] = g; ScrubCentreHz[s] = (float)TyreScrubHz(odM, slip.SpeedMs); }
            }
        }
    }

    private static void AddSide(float[] a, double pl, double pr, double g)
    {
        a[0] += (float)(g * pl);
        a[1] += (float)(g * pr);
    }
}

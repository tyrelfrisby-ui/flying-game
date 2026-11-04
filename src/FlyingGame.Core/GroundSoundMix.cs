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
    /// <summary>Stereo position of a side wheel, −1..1 (Ty: about half-way, from the cockpit).</summary>
    public const double PanWidth = 0.5;
    /// <summary>Bearing whir frequency per wheel revolution per second: the cage and rolling-element passing tones of a
    /// small tapered-roller bearing sit at ~8–10× the shaft rate.</summary>
    public const double WhirHzPerRevS = 9.0;
    /// <summary>Whir gain at the reference rim speed (≈ −36 dB below the engine's full level).</summary>
    public const double WhirGainAtRef = 0.016, WhirRefSpeedMs = 25.7;
    public const double SquealPressure = 0.6, SquealSlip = 0.08;

    // Output, per side (0 = left, 1 = right).
    public readonly float[] RollingGain = new float[2 * Keys];   // [side * Keys + key]
    public readonly float[] RollingPitch = new float[Keys];
    public readonly float[] WhirHz = new float[2], WhirGain = new float[2];
    /// <summary>Seconds of audible tyre slip this frame (spin-up chirp, or a skid), its level 0..1 and the slip ratio
    /// that sets its pitch. The voice plays for exactly the slip time it is given.</summary>
    public readonly float[] ChirpSeconds = new float[2], ChirpGain = new float[2], ChirpSlip = new float[2];
    public readonly float[] ScrapeGain = new float[2], ScrapeHz = new float[2];
    public readonly float[] SquealGain = new float[2], SquealHz = new float[2];
    public float WaterSplash, WaterHiss, Spray, SkiHiss, KeelScrape;

    private readonly double[] _w;           // [wheel * Keys + key], smoothed, sum 1 per wheel
    private readonly bool[] _seen;
    private readonly int _n;

    public GroundSoundMix(int wheels)
    {
        _n = wheels;
        _w = new double[wheels * Keys];
        _seen = new bool[wheels];
    }

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
            WhirHz[s] = WhirGain[s] = ChirpSeconds[s] = ChirpGain[s] = ChirpSlip[s] = 0;
            ScrapeGain[s] = ScrapeHz[s] = SquealGain[s] = SquealHz[s] = 0;
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
            w.TakeSlip(out double slipJ, out double slipS);

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
                double revS = rim / (2 * System.Math.PI * w.RadiusM);
                double g = WhirGainAtRef * System.Math.Pow(rim / WhirRefSpeedMs, 1.5);
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

            // Spin-up chirp / skid: the slip energy the wheel actually dissipated, for as long as it slipped, scaled by
            // how much of it the rubber makes heard on this ground (none on grass, a scuff on dirt).
            double chirp = row!.Chirp;
            if (chirp > 0 && slipS > 0)
            {
                double power = slipJ / System.Math.Max(slipS, 1e-4);
                double level = chirp * System.Math.Clamp(System.Math.Log10(power / WheelSpin.AudibleSlipPowerW) / 3.0, 0, 1);
                int sIdx = w.Side < 0 ? 0 : w.Side > 0 ? 1 : -1;
                for (int s = 0; s < 2; s++)
                {
                    if (sIdx >= 0 && s != sIdx) continue;
                    ChirpSeconds[s] = System.Math.Max(ChirpSeconds[s], (float)slipS);
                    ChirpGain[s] = System.Math.Max(ChirpGain[s], (float)(level * (s == 0 ? pl : pr) / 0.7071));
                    ChirpSlip[s] = System.Math.Max(ChirpSlip[s], (float)System.Math.Abs(w.SlipRatio));
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

    private static void AddSide(float[] a, double pl, double pr, double g)
    {
        a[0] += (float)(g * pl);
        a[1] += (float)(g * pr);
    }
}

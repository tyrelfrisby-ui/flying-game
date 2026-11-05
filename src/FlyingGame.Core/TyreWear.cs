using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// A tyre's TREAD, and what sliding does to it (Ty 2026-11-04, Q026: keep the 172's full-pedal lock-up, and let a locked
/// tyre screech, smoke, wear through and burst; "no special-case logic"). Nothing here knows what lock-up is: it is
/// told how much friction energy the patch dissipated this step, how fast the rubber slid, and which part of the tread
/// was on the ground, and the rest follows.
///
/// WEAR (Archard / Schallamach): rubber is torn off in proportion to the friction work done on it, volume = γ·W, with an
/// abradability γ that depends on how hot the sliding surface is. Cold, γ is the road figure (a car tyre loses ~1.4 L of
/// tread in ~50,000 km while its slip does ~6·10⁸ J of work on it: γ ≈ 2·10⁻¹² m³/J). Hot, past the rubber's reversion
/// temperature (~200 °C: the cross-links revert and the surface turns to a tacky layer that is wiped off — the
/// "reverted-rubber skid" of the NASA hydroplaning reports), it is ~500 times that. The surface temperature is the Blok
/// flash temperature of a sliding contact: the friction flux q = W/(A·t) shared between rubber and ground so both
/// surfaces are at the same temperature, each taking q in proportion to √π·e/(2√t), with e = √(kρc) its effusivity and
/// t how long it has been heated: the ground for l/v_slip (fresh ground keeps arriving), the rubber for as long as THAT
/// bit of tread has been in the patch. q per patch area is μ·p·v_slip, so the load cancels: every tyre at a given
/// pressure on a given surface heats the same, and a locked tyre's tread life is a distance (NASA TN D-6963: "the life of
/// a tire tread at a slip ratio of 1.0 can be measured in terms of meters along the runway, since the wear is limited to
/// an isolated area"), while a rolling one spreads the same wear round the whole tread and lives for thousands of km.
///
/// Where it goes: the tread is <see cref="Bins"/> strips round the circumference. Each step the patch (length
/// l = A/w, area A = N/p) sweeps from where it was to where the wheel's rotation has put it, and the step's work is spread
/// over the strips it swept, each in proportion to the time it spent under the patch. A locked wheel puts all of it on
/// the same 5 cm — the flat spot — and a spin-up smears it over the arc the wheel turned while it slipped.
///
/// BURST: when any strip is worn through tread and carcass (<see cref="WearThroughM"/>), the tyre lets go and its
/// pressure falls with <see cref="LeakTimeS"/>. A tyre without pressure is carried by its sidewalls folded over the rim:
/// the patch grows until the wheel stands on rim + two thicknesses of casing (<see cref="FlatRadiusM"/>). Everything
/// about a flat follows from that geometry: <see cref="Drop"/> (how much lower the axle is than on a sound tyre: the
/// aeroplane sits down on that side and rolls on a smaller wheel), the rolling resistance (a tyre's hysteresis loss per
/// metre goes with its deflection), and the lumps (the worn-through strip passing under the axle once a revolution).
/// </summary>
public sealed class TyreWear
{
    public const int Bins = 72;
    /// <summary>Abradability of tread rubber on asphalt, cold (the road figure above) and past reversion. The hot figure is
    /// FITTED, to one anchor: a 6.00-6 at 38 psi locked at 25 m/s on asphalt wears its 10 mm through in ~60 m of skid
    /// (TyreWearTests: a 172 locked from a flapless ~60 kt touchdown bursts after ~65 m; one locked at 25 kt flat-spots
    /// ~3 mm and does not). Checked against a second, independent one: a 50 kt spin-up then takes ~0.06 mm off a quarter
    /// of the tread, i.e. ~0.015 mm per landing over the whole tyre and ~450 landings of 7 mm tread, about what a
    /// training aeroplane's mains last.</summary>
    public const double GammaColdM3PerJ = 2e-12, GammaHotM3PerJ = 1.0e-9;
    /// <summary>Surface temperature rises over ambient (K) between which γ goes from cold to hot: ~120 °C, where tread
    /// compounds start to soften, and ~200 °C, natural rubber's reversion.</summary>
    public const double SoftenRiseK = 100, ReversionRiseK = 180;
    /// <summary>Tread rubber's thermal effusivity √(kρc): k 0.2 W/m·K, ρ 1100 kg/m³, c 1900 J/kg·K.</summary>
    public const double RubberEffusivity = 648;
    /// <summary>How long a tyre takes to go flat once it is through (a worn-through hole is centimetres, not a valve).</summary>
    public const double LeakTimeS = 0.15;
    /// <summary>Tread + carcass to wear through before the tyre lets go, per metre of free radius (6.00-6: ~7 mm of tread
    /// and 4 plies, 10 mm; scaled for the others).</summary>
    public const double WearThroughPerRadius = 0.045;
    /// <summary>Rim flange radius over free radius (6.00-6 on a 6 in rim: 3.5 in / 8.75 in).</summary>
    public const double RimPerRadius = 0.40;
    /// <summary>One flattened sidewall's thickness over free radius (~9 mm on a 6.00-6).</summary>
    public const double CasingPerRadius = 0.04;
    /// <summary>Tread width over free radius (a 6.00-6's 6 in section puts ~11 cm on the ground).</summary>
    public const double TreadWidthPerRadius = 0.5;
    /// <summary>What the casing alone carries, as an equivalent pressure over the inflation pressure (it is what stops a
    /// flat tyre's patch being infinite before the rim takes over).</summary>
    public const double CasingPressureRatio = 0.03;

    public readonly double RadiusM, WidthM, WearThroughM, RimRadiusM, FlatRadiusM, InflationPa, StaticLoadN;
    /// <summary>The tyre's deflection at its share of the weight on its rated pressure: the reference for the rolling loss.</summary>
    public readonly double ReferenceDeflectionM;

    /// <summary>Depth worn off each strip (m).</summary>
    public readonly double[] WornM = new double[Bins];
    private readonly double[] _dwellS = new double[Bins];

    /// <summary>Pressure over inflation pressure, 1 sound … 0 flat.</summary>
    public double PressureFraction { get; private set; } = 1;
    public bool Burst { get; private set; }
    /// <summary>The tread angle where it went through.</summary>
    public double BurstAngleRad { get; private set; }
    /// <summary>The hottest rise over ambient (K) under the patch on the last step it was asked about.</summary>
    public double PatchRiseK { get; private set; }
    public double MaxWornM { get; private set; }

    public TyreWear(AircraftConfig c, GearConfig g, double radiusM, double staticLoadN)
    {
        RadiusM = radiusM;
        WidthM = TreadWidthPerRadius * radiusM;
        WearThroughM = WearThroughPerRadius * radiusM;
        RimRadiusM = RimPerRadius * radiusM;
        FlatRadiusM = RimRadiusM + 2 * CasingPerRadius * radiusM;
        InflationPa = TirePressure.Psi(c, g) * 6894.76;   // the bushwheel option already put its own tyre and pressure on g (TireFit)
        StaticLoadN = System.Math.Max(1, staticLoadN);
        ReferenceDeflectionM = System.Math.Max(0.02 * radiusM, Deflection(StaticLoadN, InflationPa));
    }

    /// <summary>The pressure the patch carries the load at (the air's, plus what the casing holds up by itself).</summary>
    public double EffectivePressurePa
        => InflationPa * (PressureFraction + CasingPressureRatio * (1 - PressureFraction));

    /// <summary>The longest the patch can be: the chord at which the wheel stands on its flattened casing.</summary>
    public double MaxPatchLengthM => 2 * System.Math.Sqrt(System.Math.Max(0, RadiusM * RadiusM - FlatRadiusM * FlatRadiusM));

    public double PatchLengthM(double loadN, double pressurePa)
        => System.Math.Min(System.Math.Max(0, loadN) / System.Math.Max(1, pressurePa) / WidthM, MaxPatchLengthM);

    /// <summary>How far a free radius r has to come down to put a patch of length l on the ground: r − √(r² − l²/4).</summary>
    public double Deflection(double loadN, double pressurePa)
    {
        double half = 0.5 * PatchLengthM(loadN, pressurePa);
        return RadiusM - System.Math.Sqrt(System.Math.Max(0, RadiusM * RadiusM - half * half));
    }

    /// <summary>How much more the tyre is squashed than a sound one at the same load (0 for a sound tyre, to the bit).</summary>
    public double ExtraDeflection(double loadN)
    {
        if (PressureFraction >= 1) return 0;
        return System.Math.Max(0, Deflection(loadN, EffectivePressurePa) - Deflection(loadN, InflationPa));
    }

    /// <summary>
    /// How much lower the axle is than on a sound new tyre, with the tread at <paramref name="patchAngleRad"/> under it:
    /// the extra squash of a soft tyre plus the mean depth worn off the strips in the patch. Never more than the rim's own
    /// flange allows. Both are taken at the tyre's STATIC load: the leg's spring already stands for how a sound tyre
    /// gives with load, and a drop that followed the instantaneous load fed back on it (more load, a deeper sound-tyre
    /// deflection to subtract, a smaller drop, more load) and set the axle chattering. So the lumps come only from the
    /// tread turning under the axle.
    /// </summary>
    public double Drop(double patchAngleRad)
    {
        if (MaxWornM <= 0 && PressureFraction >= 1) return 0;
        double l = PatchLengthM(StaticLoadN, EffectivePressurePa);
        double worn = MeanWorn(patchAngleRad, System.Math.Max(l, 2 * System.Math.PI * RadiusM / Bins));
        double d = ExtraDeflection(StaticLoadN) + worn;
        return System.Math.Clamp(d, 0, RadiusM - RimRadiusM);
    }

    /// <summary>Extra rolling-resistance coefficient: a tyre's hysteresis loss per metre goes with how far it is squashed
    /// (Clark, Mechanics of Pneumatic Tires), so a flat one rolls with μr·(Δδ/δ_ref) more. <paramref name="hardSurfaceMu"/>
    /// is the sound tyre's own share (the hard-surface rolling coefficient).</summary>
    public double ExtraRollingCoefficient(double hardSurfaceMu)
        => hardSurfaceMu * ExtraDeflection(StaticLoadN) / ReferenceDeflectionM;

    /// <summary>Mean worn depth over a patch of length <paramref name="lengthM"/> centred at tread angle φ.</summary>
    public double MeanWorn(double phi, double lengthM)
    {
        if (MaxWornM <= 0) return 0;
        double binLen = 2 * System.Math.PI * RadiusM / Bins;
        int half = System.Math.Max(0, (int)System.Math.Round(0.5 * lengthM / binLen));
        int c = Bin(phi);
        double sum = 0; int n = 0;
        for (int k = -half; k <= half; k++) { sum += WornM[((c + k) % Bins + Bins) % Bins]; n++; }
        return sum / n;
    }

    public static int Bin(double phi)
    {
        double t = phi / (2 * System.Math.PI);
        t -= System.Math.Floor(t);
        return System.Math.Min(Bins - 1, (int)(t * Bins));
    }

    /// <summary>Abradability at a surface temperature rise (K) over ambient.</summary>
    public static double Gamma(double riseK)
    {
        double t = System.Math.Clamp((riseK - SoftenRiseK) / (ReversionRiseK - SoftenRiseK), 0, 1);
        return GammaColdM3PerJ + (GammaHotM3PerJ - GammaColdM3PerJ) * t * t * (3 - 2 * t);
    }

    /// <summary>How much a surface cuts rubber, against asphalt's sharp mineral texture: gravel and sand are as harsh,
    /// packed dirt a little less, turf hardly at all (vegetation and moist soil have no hard asperities, and the tyre
    /// slides on the film they leave). Schallamach abrasion is a cutting process; on a surface with nothing to cut with,
    /// the same work tears off little.</summary>
    public static double Abrasivity(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Paved or WorldTerrain.Surface.Gravel or WorldTerrain.Surface.Sand => 1.0,
        WorldTerrain.Surface.Dirt => 0.6,
        WorldTerrain.Surface.Grass => 0.1,
        WorldTerrain.Surface.Snow or WorldTerrain.Surface.PackedGlacierSnow or WorldTerrain.Surface.GlacierIce => 0.02,
        _ => 1.0,
    };

    /// <summary>Effusivity of the ground the tyre slides on (J/m²·K·s^½): asphalt / concrete ~1300 (k 0.75–1.4, ρc ~2 MJ/m³K);
    /// packed dirt and sand ~900; turf ~1600 (moist soil and leaves, near water's). Snow, ice and water melt or boil at the surface, which caps the flash
    /// temperature far below anything that burns rubber; a high figure stands for that.</summary>
    public static double GroundEffusivity(WorldTerrain.Surface s) => s switch
    {
        WorldTerrain.Surface.Paved => 1300,
        WorldTerrain.Surface.Gravel => 1100,
        WorldTerrain.Surface.Dirt or WorldTerrain.Surface.Sand => 900,
        WorldTerrain.Surface.Grass => 1600,
        WorldTerrain.Surface.Snow or WorldTerrain.Surface.PackedGlacierSnow or WorldTerrain.Surface.GlacierIce => 20000,
        _ => 1300,
    };

    /// <summary>
    /// The step's friction work <paramref name="workJ"/> done at the patch, the rubber sliding at
    /// <paramref name="slipSpeedMs"/> under <paramref name="loadN"/>, the patch moving round the tread from
    /// <paramref name="phiFrom"/> to <paramref name="phiTo"/> (rad). Returns the volume of rubber torn off (m³).
    /// </summary>
    public double Abrade(double workJ, double slipSpeedMs, double loadN, double phiFrom, double phiTo, double dt, WorldTerrain.Surface surface)
    {
        PatchRiseK = 0;
        if (!(workJ > 0) || !(loadN > 0) || !(dt > 0) || !double.IsFinite(phiFrom) || !double.IsFinite(phiTo))
        {
            System.Array.Clear(_dwellS, 0, Bins);
            return 0;
        }
        double l = System.Math.Max(PatchLengthM(loadN, EffectivePressurePa), 1e-3);
        double area = l * WidthM;
        double q = workJ / (dt * area);                              // friction flux over the patch, W/m²
        double kg = System.Math.Sqrt(System.Math.PI) * GroundEffusivity(surface)
                    / (2 * System.Math.Sqrt(l / System.Math.Max(slipSpeedMs, 0.05)));
        double binLen = 2 * System.Math.PI * RadiusM / Bins, binArea = binLen * WidthM;
        // The strips the patch covered: from its trailing edge at the start to its leading edge at the end.
        double lo = System.Math.Min(phiFrom, phiTo) - 0.5 * l / RadiusM, hi = System.Math.Max(phiFrom, phiTo) + 0.5 * l / RadiusM;
        bool all = hi - lo >= 2 * System.Math.PI;
        int n = 0;
        for (int i = 0; i < Bins; i++) if (all || Covers(lo, hi, (i + 0.5) * 2 * System.Math.PI / Bins)) n++;
        if (n == 0) { n = 1; lo = hi = 0.5 * (phiFrom + phiTo); }
        // The work is shared out over exactly the strips covered (so the volume is γ times it, to the joule), and each
        // was under the patch for the patch's share of the swept length.
        double perArea = workJ / (n * binArea);
        double f = System.Math.Min(1, l / (n * binLen));
        double cut = Abrasivity(surface);
        double volume = 0;
        for (int i = 0; i < Bins; i++)
        {
            double centre = (i + 0.5) * 2 * System.Math.PI / Bins;
            bool covered = all || Covers(lo, hi, centre) || (n == 1 && i == Bin(lo));
            if (!covered) { _dwellS[i] = 0; continue; }
            _dwellS[i] += f * dt;
            double kr = System.Math.Sqrt(System.Math.PI) * RubberEffusivity / (2 * System.Math.Sqrt(System.Math.Max(_dwellS[i], 1e-5)));
            double rise = q / (kr + kg);
            if (rise > PatchRiseK) PatchRiseK = rise;
            double dh = cut * Gamma(rise) * perArea;
            WornM[i] += dh;
            volume += dh * binArea;
            if (WornM[i] > MaxWornM) MaxWornM = WornM[i];
            if (!Burst && WornM[i] >= WearThroughM) { Burst = true; BurstAngleRad = centre; }
        }
        return volume;
    }

    private static bool Covers(double lo, double hi, double a)
    {
        // Bring a into [lo, lo + 2π).
        double t = a - lo;
        t -= 2 * System.Math.PI * System.Math.Floor(t / (2 * System.Math.PI));
        return lo + t <= hi;
    }

    /// <summary>The air leaving a tyre that has gone through.</summary>
    public void Leak(double dt)
    {
        if (Burst && PressureFraction > 0) PressureFraction *= System.Math.Exp(-dt / LeakTimeS);
        if (PressureFraction < 1e-4) PressureFraction = Burst ? 0 : PressureFraction;
    }

    /// <summary>Not cooling: forget the time each strip has been heated (called when the wheel leaves the ground).</summary>
    public void LeaveGround() => System.Array.Clear(_dwellS, 0, Bins);
}

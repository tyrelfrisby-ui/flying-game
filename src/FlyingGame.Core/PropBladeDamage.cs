using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>What a propeller's blades are made of (config <c>propulsion.propMaterial</c>): it decides what a strike does
/// to the part of a blade that hit (Ty 2026-11-05 9:04).</summary>
public enum PropMaterial
{
    /// <summary>Forged aluminium (2025-T6: McCauley, Sensenich metal, Hartzell, Hamilton Standard). Ductile: the struck
    /// span curls and stays on the blade.</summary>
    Metal,
    /// <summary>Laminated birch / maple (Sensenich wood, the J-3 and the Jenny). Brittle: the struck span splinters off.</summary>
    Wood,
    /// <summary>Glass or carbon over a wood or foam core (MT, Warp Drive, Hartzell carbon). Brittle: the struck span
    /// frays and breaks off.</summary>
    Composite,
}

/// <summary>
/// Per-blade propeller damage (Ty 2026-11-05 9:04: "metal blades curl back if less than zero thrust and curl forward if
/// more than zero thrust wood props shatter with jagged wood at the point they are broken off at but they only break off
/// the parts that hit and then the prop is modeled with a shorter span ... the metal props model only the portion that
/// isn't bent for the thrust and ignore the bent part that is just cosmetic. the vibration is a factor of how much blade
/// span gets affected more span more vibration").
///
/// Everything here is a pure function of the blades' own state, so the sim (<c>Aircraft.PropStrike</c>) and the tests
/// use the same numbers:
///  - each blade has an AERO radius (the straight span the air works on) and an AFFECTED span L = R₀ − R_aero: the
///    cosmetic curl of a metal blade, or the piece a wooden / composite blade lost;
///  - a metal curl is an arc of <see cref="MetalCurlRad"/> folded forward or aft, so the bent tip still reaches
///    <see cref="CurledReach"/> past the bend (it can strike again), while a broken blade ends at its break;
///  - <see cref="Performance"/>: the thrust and the rpm of a disc whose blades are unequal;
///  - <see cref="Imbalance"/>: the once-a-revolution shake and mount load.
/// </summary>
public static class PropBladeDamage
{
    public static PropMaterial MaterialOf(PropulsionConfig? pc) => (pc?.PropMaterial ?? "metal").Trim().ToLowerInvariant() switch
    {
        "wood" or "wooden" => PropMaterial.Wood,
        "composite" or "carbon" or "glass" => PropMaterial.Composite,
        _ => PropMaterial.Metal,
    };

    public static int BladeCountOf(PropulsionConfig? pc) => Math.Clamp(pc?.PropBladeCount ?? 2, 2, 6);

    // ---- the blade's section ------------------------------------------------------------------------------------------

    /// <summary>The section a blade is checked at, as a fraction of R, and its chord there (0.12 R: 11-12 cm on a 76-80 in
    /// prop).</summary>
    public const double BendStationOverR = 0.75, TipChordOverR = 0.12;

    /// <summary>The section's thickness / chord and the stress it gives way at, Pa:
    ///  - metal: 8 %, 2025-T6 forging yield 255 MPa (it bends);
    ///  - wood: 12 % (a wooden blade is thicker for the same job), yellow-birch laminate modulus of rupture 114 MPa
    ///    (USDA Wood Handbook, Table 5-3a, 12 % moisture);
    ///  - composite: 10 %, a glass/carbon skin over a wood core in flexure, ~200 MPa.</summary>
    public static (double thicknessRatio, double failPa) Section(PropMaterial m) => m switch
    {
        PropMaterial.Wood => (0.12, 114e6),
        PropMaterial.Composite => (0.10, 200e6),
        _ => (0.08, 255e6),
    };

    /// <summary>The flatwise bending moment the section at <see cref="BendStationOverR"/> fails at (yields, or breaks),
    /// N·m: σ·Z with an aerofoil's Z ≈ 0.075·c·t². The J-3's 0.915 m blade: 162 metal, 163 wood, 199 composite.</summary>
    public static double FailMomentNm(double radius, PropMaterial m)
    {
        (double tc, double sigma) = Section(m);
        double c = TipChordOverR * radius, t = tc * c;
        return sigma * 0.075 * c * t * t;
    }

    // ---- what a blade looks like after a strike ----------------------------------------------------------------------

    /// <summary>How far a struck metal tip curls, rad: about a right angle (every prop-strike photograph: the outer few
    /// inches turned through ~90°). Cosmetic, and it sets where the curled tip still reaches and where its mass went.</summary>
    public const double MetalCurlRad = 0.5 * Math.PI;

    /// <summary>How far past its bend a curl of length <paramref name="bent"/> still reaches radially: an arc of
    /// <see cref="MetalCurlRad"/>, ρ·sin θ with ρ = L/θ (0.64 L for a right angle).</summary>
    public static double CurledReach(double bent) => bent <= 0 ? 0 : bent / MetalCurlRad * Math.Sin(MetalCurlRad);

    /// <summary>The axial throw of a curl's centroid, m (+ along the curl): ρ(1 − sin θ/θ) = 0.23 L for a right angle.</summary>
    public static double CurlAxialCentroid(double bent) => bent <= 0 ? 0 : bent * (MetalCurlRad - Math.Sin(MetalCurlRad)) / (MetalCurlRad * MetalCurlRad);

    /// <summary>The curl's radial centroid past the bend, m: L(1 − cos θ)/θ² (0.41 L; the straight tip's is 0.5 L).</summary>
    public static double CurlRadialCentroid(double bent) => bent <= 0 ? 0 : bent * (1 - Math.Cos(MetalCurlRad)) / (MetalCurlRad * MetalCurlRad);

    // ---- the blade's mass ---------------------------------------------------------------------------------------------

    /// <summary>A blade's mass per unit span tapers outward, λ ∝ 1 − 0.6·r/R from the hub at 0.2 R (chord and thickness both
    /// fall toward the tip); these are its integrals.</summary>
    private const double HubOverR = 0.2;
    private static double M0(double x) => x - 0.3 * x * x;                       // ∫λ
    private static double M1(double x) => 0.5 * x * x - 0.2 * x * x * x;         // ∫λ·x
    private static double M2(double x) => x * x * x / 3.0 - 0.15 * x * x * x * x; // ∫λ·x²

    /// <summary>The blade material's density, kg/m³: 2025-T6 2,780; yellow birch at 12 % moisture 650 (USDA Wood Handbook
    /// Table 5-3a); a natural-composite blade (wood core under a glass / carbon skin) about 1,100.</summary>
    public static double DensityKgM3(PropMaterial m) => m switch { PropMaterial.Wood => 650, PropMaterial.Composite => 1100, _ => 2780 };

    /// <summary>One blade's mass, kg, from its section rather than the config's prop inertia (that is the prop AND the
    /// crankshaft): at <see cref="BendStationOverR"/> an aerofoil of chord c and thickness t holds ≈ 0.7·c·t of material,
    /// and the taper λ ∝ 1 − 0.6 r/R carries it along the blade. The J-3's 0.915 m blade: 1.6 kg metal (13.7 g in its
    /// outermost centimetre), 0.56 kg wood (4.8 g), 0.79 kg composite. A real blade is thicker toward its root than this
    /// (the forging is 4-5 kg), but a strike takes the outer span, which this gets right.</summary>
    public static double BladeMassKg(double radius, PropMaterial m)
    {
        (double tc, _) = Section(m);
        double c = TipChordOverR * radius, lambda75 = DensityKgM3(m) * 0.7 * c * tc * c;
        double lambda0 = lambda75 / (1 - 0.6 * BendStationOverR);
        return lambda0 * radius * (M0(1) - M0(HubOverR));
    }

    /// <summary>The share of a blade's mass outboard of <paramref name="x"/> = r/R, and that part's centroid as r/R.</summary>
    public static (double fraction, double centroid) Outboard(double x)
    {
        x = Math.Clamp(x, HubOverR, 1.0);
        double m = M0(1) - M0(x);
        return (m / (M0(1) - M0(HubOverR)), m > 1e-12 ? (M1(1) - M1(x)) / m : 1.0);
    }

    // ---- the disc's performance ---------------------------------------------------------------------------------------

    /// <summary>A constant-speed prop's governor can coarsen its blades enough to absorb full power with a disc that takes
    /// down to this share of the torque at the same rpm (a 15-35° pitch range at 0.75 R: about 3× the torque
    /// coefficient). Below it the blades sit on the coarse stop and it overspeeds like a fixed-pitch prop.</summary>
    public const double ConstantSpeedTorqueRange = 0.35;

    /// <summary>The highest rpm an engine will be driven to by a prop that cannot hold it, as a share of its red line: the
    /// power curve rolls over and the valves float not far beyond.</summary>
    public const double EngineOverspeedLimit = 1.2;

    /// <summary>
    /// The thrust (× the undamaged prop's, at the same throttle) and the rpm (× the undamaged rpm) of a disc whose blades
    /// work on radii <paramref name="aero"/>:
    ///  - BLADE ELEMENT: at a given rpm and advance a blade's torque goes as ∫r³dr ∝ R_k⁴, so the disc absorbs
    ///    S_Q = mean(R_k⁴)/R₀⁴ of its torque. The prop model's engine makes the throttle's power, so a fixed-pitch prop
    ///    speeds up until it absorbs it again, rpm × S_Q^(−1/3), as far as <see cref="EngineOverspeedLimit"/>; past
    ///    that it absorbs only S_Q·k³ of the power. A constant-speed prop's governor takes up the first
    ///    <see cref="ConstantSpeedTorqueRange"/>.
    ///  - MOMENTUM: the thrust that power buys is (P²·2ρA)^(1/3) on the disc the blades actually sweep, A ∝ mean(R_k²):
    ///    thrust × (P_abs/P)^(2/3)·(mean R_k² / R₀²)^(1/3).
    /// A metal blade's curl is not in <paramref name="aero"/>: the bent part is cosmetic (Ty).
    /// </summary>
    public static (double thrustScale, double rpmScale) Performance(ReadOnlySpan<double> aero, double r0, bool constantSpeed, double overspeedCap)
    {
        if (aero.Length == 0 || r0 <= 0) return (1, 1);
        double sa = 0, sq = 0;
        foreach (double r in aero) { double x = Math.Clamp(r / r0, 0, 1); sa += x * x; sq += x * x * x * x; }
        sa /= aero.Length; sq /= aero.Length;
        if (sq >= 1 - 1e-12) return (1, 1);
        double need = constantSpeed ? sq / ConstantSpeedTorqueRange : sq;
        double cap = Math.Max(1.0, overspeedCap);
        double k = need >= 1 ? 1 : Math.Min(Math.Pow(need, -1.0 / 3.0), cap);
        double absorbed = need >= 1 ? 1 : Math.Min(1.0, need * k * k * k);
        return (Math.Pow(absorbed, 2.0 / 3.0) * Math.Pow(sa, 1.0 / 3.0), k);
    }

    // ---- which way a metal blade curls ------------------------------------------------------------------------------

    /// <summary>The angle of attack the blade section works at that must be exceeded for it to pull, rad: a cambered prop
    /// section (Clark Y, RAF 6) lifts down to about −3°.</summary>
    public const double ZeroLiftAlphaRad = -3.0 * Math.PI / 180.0;
    /// <summary>The angle of attack a prop works at where it was designed to (and a governor holds), rad.</summary>
    public const double DesignAlphaRad = 4.0 * Math.PI / 180.0;
    /// <summary>A constant-speed prop's fine-pitch stop at 0.75 R, rad (Hartzell / McCauley low pitch 12-14°).</summary>
    public const double FinePitchRad = 13.0 * Math.PI / 180.0;

    /// <summary>
    /// The angle of attack, above the section's zero lift, of a blade working at 0.75 of its own aero radius, rad: its sign
    /// is the sign of that blade's thrust. Blade element: α = β(r) − φ, φ = atan(V_axial / ωr). β is the blade's geometric
    /// pitch, constant along it: a fixed-pitch prop's from its design point (<see cref="PropulsionConfig.DesignSpeedMs"/>
    /// at the red line, at <see cref="DesignAlphaRad"/>); a constant-speed prop's from its fine-pitch stop, unless the
    /// governor is driving it (<paramref name="governing"/>), when it holds the design α whatever the speed.
    /// Under power on the ground: positive (the blade pulls). Idle on the landing roll: φ is large, negative.
    /// </summary>
    public static double BladeAlphaRad(PropulsionConfig pc, double aeroRadius, double omega, double vAxial, bool governing)
    {
        double r0 = 0.5 * pc.PropDiameterM, r = BendStationOverR * Math.Max(aeroRadius, 0.05 * r0);
        if (pc.ConstantSpeed && governing) return DesignAlphaRad - ZeroLiftAlphaRad;
        double r75 = BendStationOverR * r0;
        double beta75 = pc.ConstantSpeed
            ? FinePitchRad
            : Math.Atan(Math.Max(1.0, pc.DesignSpeedMs) / (Math.Max(1.0, pc.MaxRpm * Math.PI / 30.0) * r75)) + DesignAlphaRad;
        double pitch = 2 * Math.PI * r75 * Math.Tan(beta75);   // geometric pitch, m per turn
        double beta = Math.Atan(pitch / (2 * Math.PI * r));
        double phi = Math.Atan2(vAxial, Math.Max(1e-6, omega * r));
        return beta - phi - ZeroLiftAlphaRad;
    }

    // ---- the shake ---------------------------------------------------------------------------------------------------

    /// <summary>The damaged blades' unknown share: no two blades are struck alike, and a struck span loses its twist and its
    /// track as well as its length. Taken as half the affected span's own share of the blade's thrust moment (blade element:
    /// the outer L carries 1 − (1 − L/R)³ of it), phase
    /// unknown (so it adds root-sum-square over the blades). It is what makes two equally struck blades still shake (Ty:
    /// "more span more vibration"); the asymmetry terms below are exact, this one is the stated assumption.</summary>
    public const double DamageScatter = 0.5;

    /// <summary>Prop flange to the engine mount's bolts, m (the fleet's flat fours and radials: 0.4-0.7): the arm a
    /// rotating moment at the hub works the mount through.</summary>
    public const double MountArmM = 0.5;

    /// <summary>
    /// How strongly a pilot feels a vibration of <paramref name="g"/> (at 10-50 Hz), 0 (not at all) .. 1 (violent), for the
    /// camera and the sound. Vibration is felt on a log scale (ISO 2631-1): perception begins near 0.015 m/s², "very
    /// uncomfortable" is past 2 m/s²; 3 m/s² is taken as the top. So the 0.007 g of a J-3 with 4 cm curled off both blades
    /// reads 0.28 and the 0.05 g of a centimetre of birch gone from one blade 0.66: the airframe barely moves, the pilot
    /// feels both.
    /// </summary>
    public static double Felt01(double g)
    {
        double a = Math.Abs(g) * Atmosphere.GravityMs2;
        if (a <= FeltThresholdMs2) return 0;
        return Math.Clamp(Math.Log10(a / FeltThresholdMs2) / Math.Log10(FeltTopMs2 / FeltThresholdMs2), 0, 1);
    }
    public const double FeltThresholdMs2 = 0.015, FeltTopMs2 = 3.0;

    public readonly struct Shake
    {
        /// <summary>Rotating force at the hub, N (mass unbalance).</summary>
        public readonly double ForceN;
        /// <summary>Rotating moment at the hub, N·m (couple unbalance, thrust asymmetry, damage scatter).</summary>
        public readonly double MomentNm;
        public Shake(double f, double m) { ForceN = f; MomentNm = m; }
        /// <summary>What the engine mount carries: the force plus the moment over <see cref="MountArmM"/>, N.</summary>
        public double MountLoadN => ForceN + MomentNm / MountArmM;
    }

    /// <summary>
    /// The once-a-revolution forcing of a prop whose blades k (at 2πk/n round the hub) have aero radius
    /// <paramref name="aero"/>[k], affected span <paramref name="affected"/>[k] (a curl, or a lost piece:
    /// <paramref name="broken"/>[k]) curled <paramref name="curl"/>[k] = +1 forward / −1 aft, turning at ω with thrust
    /// <paramref name="thrustN"/>:
    ///  - MASS: a lost piece takes its mass·centroid with it; a curl moves its mass a little inward (0.41 L, not 0.5 L)
    ///    and 0.23 L along the curl. Σ m·r over the blades → force |Σ|·ω²; Σ m·z·r → couple |Σ|·ω².
    ///  - THRUST: each blade's share of the disc's thrust ∝ its swept area R_k², acting at 0.7 R_k: |Σ T_k·0.7R_k|.
    ///  - SCATTER: <see cref="DamageScatter"/>.
    /// Zero for a straight prop; it grows with every blade's affected span.
    /// </summary>
    public static Shake Imbalance(ReadOnlySpan<double> aero, ReadOnlySpan<double> affected, ReadOnlySpan<int> curl, ReadOnlySpan<bool> broken,
        double r0, double bladeMassKg, double omega, double thrustN)
    {
        int n = aero.Length;
        if (n == 0 || r0 <= 0) return default;
        double sx = 0, sy = 0, cx = 0, cy = 0, tx = 0, ty = 0, scatter = 0, area = 0;
        for (int k = 0; k < n; k++) area += aero[k] * aero[k];
        if (area <= 0) area = 1e-9;
        for (int k = 0; k < n; k++)
        {
            double psi = 2 * Math.PI * k / n, c = Math.Cos(psi), s = Math.Sin(psi);
            double l = Math.Max(0, affected[k]);
            double dmr = 0, dmzr = 0;
            if (l > 0)
            {
                (double frac, double cen) = Outboard(1 - l / r0);
                double mSeg = bladeMassKg * frac;
                if (broken[k]) dmr = -mSeg * cen * r0;
                else
                {
                    double ra = aero[k];
                    dmr = mSeg * (CurlRadialCentroid(l) - 0.5 * l);
                    dmzr = mSeg * curl[k] * CurlAxialCentroid(l) * (ra + CurlRadialCentroid(l));
                }
            }
            sx += dmr * c; sy += dmr * s;
            cx += dmzr * c; cy += dmzr * s;
            double tk = thrustN * aero[k] * aero[k] / area * 0.7 * aero[k];
            tx += tk * c; ty += tk * s;
            double tipShare = 1 - Math.Pow(Math.Max(0, 1 - l / r0), 3);   // the outer span's share of a blade's thrust (∝ ∫r²dr)
            double sc = DamageScatter * (thrustN / n) * 0.7 * r0 * tipShare;
            scatter += sc * sc;
        }
        double w2 = omega * omega;
        double force = Math.Sqrt(sx * sx + sy * sy) * w2;
        double couple = Math.Sqrt(cx * cx + cy * cy) * w2, thrust = Math.Sqrt(tx * tx + ty * ty);
        return new Shake(force, Math.Sqrt(couple * couple + thrust * thrust + scatter));
    }
}

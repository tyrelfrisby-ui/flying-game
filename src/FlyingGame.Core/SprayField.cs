using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The floats' spray as physics (Ty 2026-11-02: "the water spray looks kind of cool but it is clearly not 'physics
/// based' it sprays in weird directions make it purely physics based"). Engine-free; FloatSplash draws it.
///
/// WHERE and HOW FAST: <see cref="FloatHydro.SprayRoots"/> — the same hull/waterline cut the forces use — gives the
/// spray-root points along the chines and the bottom with the jet velocity of the Wagner/Savitsky spray root (the
/// relative water velocity reflected about the root line: 2V sin α in the earth frame, including the aircraft's yaw,
/// roll and pitch rates) and the jet's mass flux.
/// HOW MUCH: emission follows the mass flux (ρ |u| δ per metre of root, δ = (π/4) sin²τ l). Each particle is a PARCEL of
/// spray; its mass is set so the strongest flux the type sees fills the budget (<see cref="MaxParcels"/>), so the
/// number of parcels in the air is proportional to the real mass of water thrown.
/// FLIGHT: each parcel is a cloud of droplets of one diameter, drawn uniformly 1–3 mm (a stated choice: a breaking
/// sheet throws millimetre drops; the spread of sizes is what spreads the spray, not random directions). Ballistic under gravity with sphere
/// drag relative to the air: a = g − (3/8)(ρ_a/ρ_w)(C_D/r)|u|u, C_D from Clift, Grace &amp; Weber (1978)
/// C_D = 24/Re (1 + 0.15 Re^0.687) + 0.42/(1 + 42500 Re^−1.16). A 2 mm drop's terminal speed comes out ≈ 6.5 m/s (Gunn &amp; Kinzer 1949 measured 6.49 m/s).
/// Back on the water (or after <see cref="MaxLifeS"/>) the parcel is gone.
/// KELVIN WAKE at displacement speeds: foam along the two cusp lines of the ship-wave pattern, which in deep water sit
/// at asin(1/3) = 19.47° to the track whatever the speed (Kelvin 1887). Seeded at the bow while the hull is in the
/// displacement regime (Froude number on the waterline length below 1), they stay where they are put on the water.
/// </summary>
public sealed class SprayField
{
    public const int MaxParcels = 600;           // iPhone / Quest budget, all floats together (the old FloatSplash ran ~1,500)
    public const int MaxFoam = 160;
    public const double MaxLifeS = 4.0;
    public const double KelvinHalfAngleRad = 0.3398369094541219;   // asin(1/3)
    public const double DropMinM = 0.001, DropMaxM = 0.003;
    public const double RhoAir = 1.225;

    public struct Parcel { public Vec3 Pos, Vel; public double Age, Diameter; public bool Alive; }
    public struct Foam { public Vec3 Pos; public double Age, Life; public bool Alive; }

    public readonly Parcel[] Parcels = new Parcel[MaxParcels];
    public readonly Foam[] Foams = new Foam[MaxFoam];
    private readonly FloatHydro.SprayRoot[] _roots = new FloatHydro.SprayRoot[256];
    private readonly System.Random _rng = new(1847);
    private double _carry, _parcelKg = 0.05, _peakFlux, _foamCarry;
    private int _next, _nextFoam;

    /// <summary>The mass flux (kg/s) the hull was throwing at the last step, all roots.</summary>
    public double LastFluxKgS { get; private set; }
    public int LastRootCount { get; private set; }
    public ReadOnlySpan<FloatHydro.SprayRoot> LastRoots => new(_roots, 0, LastRootCount);

    /// <summary>Sphere drag coefficient (Clift, Grace &amp; Weber 1978, eq. 5-58, valid to Re 2e5).</summary>
    public static double SphereCd(double re)
    {
        re = System.Math.Max(re, 1e-6);
        return 24 / re * (1 + 0.15 * System.Math.Pow(re, 0.687)) + 0.42 / (1 + 42500 * System.Math.Pow(re, -1.16));
    }

    /// <summary>Acceleration of a drop of diameter d moving at v through air moving at wind (NED; gravity +z).</summary>
    public static Vec3 DropAcceleration(Vec3 v, Vec3 wind, double d)
    {
        Vec3 u = v - wind;
        double um = u.Length;
        double re = um * d / 1.5e-5;   // air kinematic viscosity 1.5e-5 m²/s (ISA sea level)
        double k = 0.75 * RhoAir / FloatHydro.WaterDensity * SphereCd(re) / d;
        return new Vec3(0, 0, Atmosphere.GravityMs2) - u * (k * um);
    }

    /// <summary>One step: new parcels from the spray roots of the current state, then everything flies.</summary>
    public void Step(AircraftConfig config, RigidBodyState s, double dt)
    {
        if (config.Floats is null || dt <= 0) return;
        int n = FloatHydro.SprayRoots(config, s, _roots);
        LastRootCount = n;
        double flux = 0;
        for (int i = 0; i < n; i++) flux += _roots[i].MassFlux;
        LastFluxKgS = flux;
        // The parcel mass that lets the strongest flux seen so far fill the budget over a typical flight time (1 s).
        _peakFlux = System.Math.Max(_peakFlux, flux);
        _parcelKg = System.Math.Max(1e-4, _peakFlux * 1.0 / MaxParcels);
        if (n > 0 && flux > 0)
        {
            _carry += flux * dt / _parcelKg;
            int emit = (int)_carry;
            _carry -= emit;
            for (int e = 0; e < emit; e++)
            {
                // pick a root by its share of the flux
                double pick = _rng.NextDouble() * flux, acc = 0; int r = 0;
                for (; r < n - 1; r++) { acc += _roots[r].MassFlux; if (acc >= pick) break; }
                ref Parcel p = ref Parcels[_next];
                _next = (_next + 1) % MaxParcels;
                p.Pos = _roots[r].Position; p.Vel = _roots[r].JetVelocity; p.Age = 0; p.Alive = true;
                p.Diameter = DropMinM + (DropMaxM - DropMinM) * _rng.NextDouble();
            }
        }
        Vec3 wind = Atmosphere.SteadyWind;
        for (int i = 0; i < MaxParcels; i++)
        {
            ref Parcel p = ref Parcels[i];
            if (!p.Alive) continue;
            // semi-implicit Euler, fine for dt ≤ 1/30 s at these drag time constants (≥ 0.15 s for 1 mm drops)
            p.Vel += DropAcceleration(p.Vel, wind, p.Diameter) * dt;
            p.Pos += p.Vel * dt;
            p.Age += dt;
            double? w = FloatHydro.WaterSurfaceAt(p.Pos.X, p.Pos.Y, -p.Pos.Z);
            if (p.Age > MaxLifeS || (w.HasValue && -p.Pos.Z < w.Value - 0.02 && p.Vel.Z > 0)) p.Alive = false;
        }
        StepKelvin(config, s, dt);
    }

    private void StepKelvin(AircraftConfig config, RigidBodyState s, double dt)
    {
        for (int i = 0; i < MaxFoam; i++) { ref Foam f = ref Foams[i]; if (!f.Alive) continue; f.Age += dt; if (f.Age > f.Life) f.Alive = false; }
        Vec3 v = s.Attitude.Rotate(s.Velocity);
        double vh = System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
        FloatsConfig fl = config.Floats!;
        double lwl = fl.LengthM;
        double fn = vh / System.Math.Sqrt(Atmosphere.GravityMs2 * System.Math.Max(lwl, 0.5));
        if (vh < 0.5 || fn > 1.0 || !(FloatHydro.Floats[0].Wet || FloatHydro.Floats[1].Wet)) return;
        // One foam mark per float per transverse wavelength travelled: λ = 2πV²/g, the wave the hull itself makes.
        double lambda = 2 * System.Math.PI * vh * vh / Atmosphere.GravityMs2;
        _foamCarry += vh * dt / System.Math.Max(lambda, 0.2);
        if (_foamCarry < 1) return;
        _foamCarry -= 1;
        Vec3 dir = new Vec3(v.X / vh, v.Y / vh, 0), side = new Vec3(-dir.Y, dir.X, 0);
        for (int k = 0; k < 2; k++)
        {
            FloatHydro.FloatState f = FloatHydro.Floats[k];
            if (!f.Wet) continue;
            double? w = FloatHydro.WaterSurfaceAt(f.BowWorld.X, f.BowWorld.Y);
            if (w is null) continue;
            // The cusp points the bow left behind one wavelength ago: dist λ back along the track, ±tan(19.47°)·λ out.
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                ref Foam fm = ref Foams[_nextFoam]; _nextFoam = (_nextFoam + 1) % MaxFoam;
                Vec3 p = f.BowWorld - dir * lambda + side * (sgn * lambda * System.Math.Tan(KelvinHalfAngleRad));
                fm.Pos = new Vec3(p.X, p.Y, -w.Value); fm.Age = 0; fm.Life = 6.0; fm.Alive = true;
            }
        }
    }

    public void Clear()
    {
        for (int i = 0; i < MaxParcels; i++) Parcels[i].Alive = false;
        for (int i = 0; i < MaxFoam; i++) Foams[i].Alive = false;
        _carry = 0; _peakFlux = 0;
    }
}

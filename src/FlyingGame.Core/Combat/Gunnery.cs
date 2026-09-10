using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Combat;

/// <summary>One gun: body-frame muzzle position, boresight converging at ConvergenceM ahead, rate and rounds.</summary>
public sealed class GunConfig
{
    public Vec3 Muzzle; public double ConvergenceM = 275; public double RoundsPerMin = 800; public int Rounds = 400;
    public double MuzzleVelocityMs = 880;
}

/// <summary>The aircraft's guns and ammunition. P-51D: six .50 cal M2 in the wings (three a side, 1,880 rounds,
/// converged at 300 yd). F-86: six .50 cal M3 in the nose (1,602 rounds, ~1,200 rpm each). Everyone else (owner):
/// one .50 cal on the centreline.</summary>
public sealed class Armament
{
    public readonly List<GunConfig> Guns = new();
    public int RoundsLeft;
    public int RoundsTotal;
    public double RateHz => Guns.Count == 0 ? 0 : Guns.Count * Guns[0].RoundsPerMin / 60.0;
    private double _accum;

    public static Armament For(AircraftConfig c)
    {
        var a = new Armament();
        string id = c.Id.ToLowerInvariant();
        double minTe = double.MaxValue;
        foreach (SurfaceConfig sf in c.Surfaces) foreach (StripConfig st in sf.Strips) minTe = Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double noseX = minTe - 0.2 + len;
        double axisZ = c.Propulsion?.ThrustLineZ ?? 0.0;
        if (id.StartsWith("p51"))
        {
            foreach (double y in new[] { 1.9, 2.3, 2.7 })
                foreach (double sgn in new[] { -1.0, 1.0 })
                    a.Guns.Add(new GunConfig { Muzzle = new Vec3(1.2, sgn * y, 0.15), ConvergenceM = 274, RoundsPerMin = 800, Rounds = y < 2.0 ? 400 : y < 2.5 ? 270 : 270 });
            a.RoundsTotal = 1880;
        }
        else if (id.StartsWith("f86"))
        {
            foreach (double z in new[] { -0.15, 0.1, 0.35 })
                foreach (double sgn in new[] { -1.0, 1.0 })
                    a.Guns.Add(new GunConfig { Muzzle = new Vec3(noseX - 0.6, sgn * 0.35, axisZ + z), ConvergenceM = 300, RoundsPerMin = 1200, Rounds = 267 });
            a.RoundsTotal = 1602;
        }
        else
        {
            a.Guns.Add(new GunConfig { Muzzle = new Vec3(noseX - 0.4, 0, axisZ - 0.25), ConvergenceM = 250, RoundsPerMin = 800, Rounds = 400 });
            a.RoundsTotal = 400;
        }
        a.RoundsLeft = a.RoundsTotal;
        return a;
    }

    /// <summary>Rounds to fire this step with the trigger held (rate × dt, carried fractionally), limited by ammunition.</summary>
    public int RoundsThisStep(double dt)
    {
        if (RoundsLeft <= 0 || Guns.Count == 0) return 0;
        _accum += RateHz * dt;
        int n = (int)_accum;
        _accum -= n;
        n = Math.Min(n, RoundsLeft);
        RoundsLeft -= n;
        return n;
    }

    public void Reload() { RoundsLeft = RoundsTotal; _accum = 0; }
}

public sealed class Bullet
{
    public Vec3 Pos, Vel; public double Age; public int Shooter;
    public const double LifeSec = 3.0, DragPerM = 0.00055;   // v/v0 ≈ e^{-0.00055·s}: 880 → ~500 m/s over 1 km
}

/// <summary>What a bullet hit: a target aircraft's part, the ground, or a ground target.</summary>
public readonly struct HitEvent
{
    public readonly int Shooter; public readonly int Target; public readonly AirframeComponent? Part; public readonly Vec3 Pos; public readonly int GroundTarget;
    public HitEvent(int shooter, int target, AirframeComponent? part, Vec3 pos, int groundTarget) { Shooter = shooter; Target = target; Part = part; Pos = pos; GroundTarget = groundTarget; }
}

/// <summary>A target aircraft the bullets can hit (state, config, hit volumes, damage state).</summary>
public sealed class GunTarget
{
    public int Id; public AircraftConfig Config = null!; public IReadOnlyList<HitVolume> Volumes = null!;
    public Func<RigidBodyState> State = null!; public Func<AirframeComponent, bool>? Lost; public DamageState? Damage;
}

/// <summary>Fires bullets and flies them: gravity, drag, hits on target aircraft, ground targets and the ground.</summary>
public sealed class Gunnery
{
    public readonly List<Bullet> Bullets = new();
    public readonly List<HitEvent> Hits = new();
    public int Fired { get; private set; }
    private readonly Random _rng = new(12345);

    /// <summary>Fire from an aircraft this step (trigger held). Dispersion ~2 mrad; guns cycle round-robin.</summary>
    public int Fire(int shooterId, Armament arm, AircraftConfig c, RigidBodyState s, double dt)
    {
        int n = arm.RoundsThisStep(dt);
        if (n == 0) return 0;
        Vec3 cg = c.Mass.CgVec();
        for (int i = 0; i < n; i++)
        {
            GunConfig g = arm.Guns[(Fired + i) % arm.Guns.Count];
            Vec3 aim = new Vec3(g.ConvergenceM + g.Muzzle.X, 0, g.Muzzle.Z) - g.Muzzle;   // converge on the centreline ahead
            Vec3 dirBody = aim / aim.Length;
            dirBody = new Vec3(dirBody.X, dirBody.Y + (_rng.NextDouble() - 0.5) * 0.004, dirBody.Z + (_rng.NextDouble() - 0.5) * 0.004);
            dirBody = dirBody / dirBody.Length;
            Vec3 muzzleWorld = s.Position + s.Attitude.Rotate(g.Muzzle - cg);
            Vec3 velWorld = s.Attitude.Rotate(s.Velocity) + s.Attitude.Rotate(dirBody) * g.MuzzleVelocityMs;
            Bullets.Add(new Bullet { Pos = muzzleWorld, Vel = velWorld, Shooter = shooterId, Age = _rng.NextDouble() * 0.002 });
        }
        Fired += n;
        return n;
    }

    /// <summary>Advance every bullet; test each step segment against the targets, ground targets and terrain.</summary>
    public void Step(double dt, IReadOnlyList<GunTarget> targets, IReadOnlyList<GroundTarget>? groundTargets = null)
    {
        Hits.Clear();
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = Bullets[i];
            Vec3 p0 = b.Pos;
            double v = b.Vel.Length;
            Vec3 vel = b.Vel * Math.Exp(-Bullet.DragPerM * v * dt) + new Vec3(0, 0, 9.81 * dt);
            Vec3 p1 = p0 + vel * dt;
            b.Vel = vel; b.Pos = p1; b.Age += dt;
            bool done = b.Age > Bullet.LifeSec;
            foreach (GunTarget t in targets)
            {
                if (t.Id == b.Shooter) continue;
                RigidBodyState ts = t.State();
                if ((ts.Position - p0).Length > 60 && (ts.Position - p1).Length > 60) continue;   // broad phase
                AirframeComponent? part = HitVolumes.Test(t.Volumes, t.Config, ts, p0, p1, t.Lost);
                if (part.HasValue)
                {
                    Hits.Add(new HitEvent(b.Shooter, t.Id, part, p1, -1));
                    t.Damage?.RegisterHit(part.Value);
                    done = true; break;
                }
            }
            if (!done)
            {
                double ground = WorldTerrain.GroundHeightAt(p1.X, p1.Y);
                if (-p1.Z <= ground)
                {
                    int gt = -1;
                    if (groundTargets != null)
                        for (int k = 0; k < groundTargets.Count; k++)
                            if (groundTargets[k].Contains(p1.X, p1.Y)) { gt = k; groundTargets[k].Hits++; break; }
                    Hits.Add(new HitEvent(b.Shooter, -1, null, new Vec3(p1.X, p1.Y, -ground), gt));
                    done = true;
                }
            }
            if (done) Bullets.RemoveAt(i);
        }
    }

    public void Clear() { Bullets.Clear(); Hits.Clear(); }
}

/// <summary>A bullseye on the ground inside the combat zone.</summary>
public sealed class GroundTarget
{
    public double X, Y, RadiusM; public int Hits;
    public GroundTarget(double x, double y, double r) { X = x; Y = y; RadiusM = r; }
    public bool Contains(double x, double y) => (x - X) * (x - X) + (y - Y) * (y - Y) <= RadiusM * RadiusM;
}

/// <summary>The COMBAT ZONE (owner): a big box east of the Valley, from the surface to 3 km AGL — guns only work
/// inside it. Ground targets sit on its floor; target drones orbit inside it.</summary>
public static class CombatZone
{
    public const double X0 = 3500, X1 = 7500, Y0 = 2500, Y1 = 6500, HeightM = 3000;
    public static double CentreX => (X0 + X1) / 2;
    public static double CentreY => (Y0 + Y1) / 2;
    public static bool Inside(Vec3 pos)
    {
        if (pos.X < X0 || pos.X > X1 || pos.Y < Y0 || pos.Y > Y1) return false;
        double agl = -pos.Z - WorldTerrain.GroundHeightAt(pos.X, pos.Y);
        return agl >= -5 && agl <= HeightM;
    }
    public static List<GroundTarget> BuildGroundTargets()
    {
        var t = new List<GroundTarget>();
        foreach ((double x, double y) in new[] { (4200.0, 3200.0), (4800.0, 4600.0), (5500.0, 3000.0), (6100.0, 5400.0), (6800.0, 3800.0), (5200.0, 5900.0) })
            t.Add(new GroundTarget(x, y, 30));
        return t;
    }
}

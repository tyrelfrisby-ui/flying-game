using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;

namespace FlyingGame.Core;

/// <summary>
/// A BAILED-OUT PILOT'S ATTITUDE, ON THE AIR'S OWN MOMENT. Ty 2026-11-10, of the departing wings and canopies:
/// "same for the pilot when bailing out real forces leading to tumbles and flutter etc".
///
/// WHAT WAS WRONG. The pilot was a point mass with an angular velocity set by hand: PilotBody took an
/// AngularVelocityRad at birth and spun the figure at it for ever (and the tumble down a slope picked literal
/// Random.Range numbers). Nothing about his body's shape or the air he was in entered it, so a bail-out at 150 kt
/// and one at 40 kt tumbled identically, and he kept tumbling at the same rate all the way down.
///
/// WHAT HE IS NOW. Seven boxes — trunk, head, the rig on his back, two arms, two legs — in a body frame of x forward
/// (the way he faces), y right, z down (toward his feet), with the segment masses the anthropometric fractions
/// (Winter, Biomechanics and Motor Control of Human Movement, Table 4.1, from Clauser et al. 1969: head 8.1 %,
/// trunk 49.7 %, whole arm 5.0 % each, whole leg 16.1 % each — they sum to 100). Their CG, their inertia and their
/// air forces are all read off those boxes, and the air is <see cref="DebrisAero"/>'s — the flat-plate normal force
/// at a centre of pressure that moves with the angle. His trunk is the plate: 0.42 m across and 0.56 m long against
/// 0.24 m thick, so the force on it acts well off his CG at every angle but broadside, and the moment follows from
/// that. He tumbles because a man is not a sphere. Measured, released at 100 kt with his rates EXACTLY zero: belly
/// on he reaches 2.1 rad/s, side-on — which is how he actually leaves, over the right side while the aeroplane flies
/// on — 3.3 rad/s in all three axes at once, and feet first 3.7 rad/s. The old model could not tell the three apart.
///
/// HE IS NOT TRIMMED BELLY-ON, which is stated rather than fitted away. His frontal-area centroid sits about 48 mm
/// from his CG — his legs are dense for their area, his rig and arms area-rich for their mass — so belly to earth
/// carries a real 46 N·m at 100 kt. A skydiver trims exactly that out with his arms and legs, which is what "arch"
/// and "de-arch" MEAN and the one control a rigid seven-box body has not got. Belly on is still the least unstable
/// attitude he has, which is the claim the tests make. A limb trim is the next real step, not a patch.
///
/// HIS FALL SPEED IS STILL TY'S. <see cref="SkydiverAero"/> back-solves the drag areas from the terminal speeds
/// everyone agrees on (120 mph flat, 210 mph head down), and those must come out exactly. So the boxes' own Cd·A is
/// SCALED to hit <see cref="SkydiverAero.CdA"/> at his current body position (<see cref="CdScale"/>, measured once
/// against the belly-to-earth attitude) — the published speed sets the size of the force, the geometry sets where it
/// acts. Nothing here changes how fast he falls.
///
/// THE CHUTE STABILISES HIM, and not by being told to. The risers meet him at the shoulders,
/// <see cref="HarnessAboveCgM"/> above his CG along his own long axis, so the canopy's pull is a force on a lever: a
/// restoring moment that lines his long axis up with the lines, with its strength the tension the canopy is actually
/// carrying — and nothing else. Measured from a 6 rad/s side-on tumble, the mean angle he holds to his lines over the
/// following seconds is 85° with no canopy, 80° with the bag just off (5 % of his weight on the risers), 42° at half
/// inflation and 31° at full. The handover is the inflation's, not a flag's. He is not left perfectly still, and
/// should not be: a restoring moment with nothing to damp it is a pendulum, √(T·r/I) = 6.1 rad/s at full load, and a
/// parachutist under a fresh canopy does swing. The swing of the HARNESS — him on 7 m of line under the canopy — is
/// PilotBody's own damped pendulum and a different thing from this, which is his body about its own CG.
/// </summary>
public sealed class JumperTumble
{
    /// <summary>90 kg pilot and 15 kg of rig and kit — <see cref="SkydiverAero.ReferenceMassKg"/>.</summary>
    public const double MassKg = SkydiverAero.ReferenceMassKg;
    /// <summary>Of that, what is rig rather than man: it rides on his back, so it goes on the trunk.</summary>
    public const double RigKg = 15.0;
    /// <summary>A 1.80 m adult. Segment box sizes below are from this and the usual segment-length fractions
    /// (Drillis &amp; Contini 1966: trunk 0.31 H, head 0.13 H, upper+fore arm 0.31 H, whole leg 0.53 H).</summary>
    public const double HeightM = 1.80;
    /// <summary>How far back the free-fall arch carries his elbows and knees from his spine, m. An arched body is
    /// not flat: the limbs trail toward the rig, which puts area behind the CG. 0.06 m is a modest arch.</summary>
    public const double ArchSweepM = 0.06;
    /// <summary>Where the risers meet him, above the CG along his long axis, m: the shoulder harness of a sport rig
    /// sits about 0.37 m up from the mid-trunk CG of a 1.8 m man.</summary>
    public const double HarnessAboveCgM = 0.37;

    /// <summary>The six boxes, centres relative to his CG.</summary>
    public readonly DebrisAero.Segment[] Segments;
    /// <summary>His CG relative to the mid-trunk origin the boxes are written in, m. X is not zero: the rig.</summary>
    public readonly double CgOffsetX, CgOffsetZ;
    /// <summary>Principal moments about the CG, body axes, kg·m².</summary>
    public readonly Vec3 Inertia;
    /// <summary>The multiplier that makes the boxes' belly-on drag area come out at <see cref="SkydiverAero.CdABelly"/>
    /// — his published 120 mph, not the boxes' own guess.</summary>
    public readonly double CdBellyScale;

    /// <summary>His attitude and body rates. Both are STATE: nothing in this file sets a rate.</summary>
    public Quat Attitude;
    public Vec3 RatesBody;
    /// <summary>The last aerodynamic moment, body axes, N·m (probes).</summary>
    public Vec3 LastAirMomentNm { get; private set; }
    /// <summary>The last harness moment, body axes, N·m (probes).</summary>
    public Vec3 LastHarnessMomentNm { get; private set; }

    public JumperTumble(Quat attitude, Vec3 ratesBody)
    {
        Attitude = attitude;
        RatesBody = ratesBody;
        // Anthropometric masses; the rig is its own box, because WHERE it is matters more than what it weighs.
        double man = MassKg - RigKg;
        double head = 0.081 * man, trunk = 0.497 * man, arm = 0.050 * man, leg = 0.161 * man;
        // Boxes, centres in a frame whose origin is the mid-trunk, x forward, y right, z down toward his feet.
        //
        // TWO BITS OF ASYMMETRY THAT ARE NOT DECORATION. Built fore-and-aft symmetric — trunk, head and limbs all on
        // x = 0 — this body gave EXACTLY ZERO moment feet-first into the airstream, which is the attitude an ejection
        // seat hands him and one of the two a man cannot hold. The zero was the symmetry's, not the air's: with every
        // offset along z and the force along z too, r × F is identically nothing. A real jumper is not symmetric:
        //   THE RIG is on his BACK. 0.45 × 0.55 × 0.15 m of container (the usual sport back-mount), 0.195 m aft of
        //   his spine, which is where the 15 kg actually sits and where the area actually is.
        //   HIS LIMBS TRAIL. In the free-fall arch his elbows and knees are bent back toward his rig
        //   (<see cref="ArchSweepM"/>) — that is what the figure is posed as (PilotFigure.Stance.FreeFall), and it is
        //   the dart's fins: area aft of the CG, which is why a spread skydiver is stable belly to earth at all.
        var raw = new[]
        {
            new DebrisAero.Segment(new Vec3(0, 0, 0.00), new Vec3(0.120, 0.210, 0.280), trunk),   // trunk 0.24 x 0.42 x 0.56
            new DebrisAero.Segment(new Vec3(0, 0, -0.395), new Vec3(0.090, 0.080, 0.115), head),  // head and neck
            new DebrisAero.Segment(new Vec3(-0.195, 0, -0.060), new Vec3(0.075, 0.225, 0.275), RigKg),  // the rig, on his back
            new DebrisAero.Segment(new Vec3(-ArchSweepM, -0.300, -0.120), new Vec3(0.055, 0.085, 0.275), arm),
            new DebrisAero.Segment(new Vec3(-ArchSweepM, +0.300, -0.120), new Vec3(0.055, 0.085, 0.275), arm),
            new DebrisAero.Segment(new Vec3(-ArchSweepM, -0.120, 0.500), new Vec3(0.085, 0.095, 0.240), leg),
            new DebrisAero.Segment(new Vec3(-ArchSweepM, +0.120, 0.500), new Vec3(0.085, 0.095, 0.240), leg),
        };
        double mx = 0, mz = 0;
        foreach (DebrisAero.Segment g in raw) { mx += g.MassKg * g.Centre.X; mz += g.MassKg * g.Centre.Z; }
        CgOffsetX = mx / MassKg;
        CgOffsetZ = mz / MassKg;
        Segments = new DebrisAero.Segment[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            Segments[i] = raw[i].MovedTo(new Vec3(raw[i].Centre.X - CgOffsetX, raw[i].Centre.Y, raw[i].Centre.Z - CgOffsetZ));
        double ixx = 0, iyy = 0, izz = 0, bellyArea = 0;
        foreach (DebrisAero.Segment g in Segments)
        {
            Vec3 r = g.Centre, h = g.Half;
            ixx += g.MassKg * ((h.Y * h.Y + h.Z * h.Z) / 3 + r.Y * r.Y + r.Z * r.Z);
            iyy += g.MassKg * ((h.X * h.X + h.Z * h.Z) / 3 + r.X * r.X + r.Z * r.Z);
            izz += g.MassKg * ((h.X * h.X + h.Y * h.Y) / 3 + r.X * r.X + r.Y * r.Y);
            // Belly to earth, the flow is along his body x: the frontal faces are the chest, the arms and the legs.
            bellyArea += WaterPhysics.CdForFineness(2 * h.X / System.Math.Sqrt(g.FaceArea(0))) * g.FaceArea(0);
        }
        Inertia = new Vec3(ixx, iyy, izz);
        CdBellyScale = bellyArea > 1e-9 ? SkydiverAero.CdABelly / bellyArea : 1.0;
    }

    /// <summary>His inertia as mass properties about his CG (products neglected: he is near enough symmetric
    /// left to right, and the boxes above are exactly so).</summary>
    public MassProperties MassProps => new(MassKg, Inertia.X, Inertia.Y, Inertia.Z, 0.0);

    /// <summary>Cd·A scale for a body position: <see cref="SkydiverAero.CdA"/> relative to the belly-on value the
    /// boxes were measured against, so his published terminal speed at every position is exactly honoured.</summary>
    public double CdScale(double bodyPos) => CdBellyScale * SkydiverAero.CdA(bodyPos) / SkydiverAero.CdABelly;

    /// <summary>
    /// One step of his ATTITUDE only; his path is the caller's (PilotBody integrates the published drag areas, the
    /// canopy and the terrain).
    /// </summary>
    /// <param name="velocityWorld">His velocity, world axes (the caller's frame).</param>
    /// <param name="windWorld">The air's velocity in the same frame.</param>
    /// <param name="riserTensionN">What the canopy is pulling with, N — 0 before the bag comes off, the lot once it
    /// is full. The restoring moment is this on a <see cref="HarnessAboveCgM"/> lever, nothing more.</param>
    /// <param name="hangWorld">Unit vector from him TOWARD the canopy, the caller's frame.</param>
    /// <param name="bodyPos">His free-fall body position, 0 flat to 1 head down (<see cref="SkydiverAero"/>).</param>
    public void Step(double dt, Vec3 velocityWorld, Vec3 windWorld, double rho, double riserTensionN, Vec3 hangWorld,
        double bodyPos)
    {
        if (dt <= 0.0) return;
        // Sub-stepped on the same rule as the wreck: the moment is strongly nonlinear in the angle, and a man
        // tumbling at 8 rad/s turns 23 deg in a 50 Hz frame.
        int sub = System.Math.Clamp((int)System.Math.Ceiling(RatesBody.Length * dt / 0.05), 1, 8);
        double h = dt / sub;
        double scale = CdScale(bodyPos);
        MassProperties mp = MassProps;
        for (int i = 0; i < sub; i++)
        {
            Quat qi = Attitude.Conjugate();
            // His state as a rigid body: position does not matter to a moment, so it is left at the origin.
            var st = new RigidBodyState(Vec3.Zero, Attitude, qi.Rotate(velocityWorld), RatesBody);
            (Vec3 _, Vec3 mAir) = DebrisAero.Forces(Segments, st, rho, qi.Rotate(windWorld), scale);
            // THE HARNESS. The lines pull at his shoulders, which are HarnessAboveCgM up his own long axis (body −z),
            // so a tension along the hang direction is a moment about his CG. That is the whole of the stabilising:
            // no flag, no slerp, and it grows with the canopy's load exactly as the real one does.
            Vec3 mHarness = Vec3.Zero;
            if (riserTensionN > 0.0)
            {
                double hl = hangWorld.Length;
                if (hl > 1e-9)
                    mHarness = Vec3.Cross(new Vec3(0, 0, -HarnessAboveCgM), qi.Rotate(hangWorld * (riserTensionN / hl)));
            }
            Vec3 m = mAir + mHarness;
            LastAirMomentNm = mAir;
            LastHarnessMomentNm = mHarness;
            // Euler's equations about the CG, with the gyroscopic term — a man is I = (12.3, 10.8, 3.1) kg·m², so a
            // quarter as hard to spin about his own long axis as end over end, and a tumble started about one axis
            // walks into the others on its own. Side-on he leaves in all three at once for exactly that reason.
            Vec3 wd = mp.Solve(m - Vec3.Cross(RatesBody, mp.Times(RatesBody)));
            RatesBody += wd * h;
            Quat qd = Quat.Derivative(Attitude, RatesBody);
            Attitude = new Quat(Attitude.X + qd.X * h, Attitude.Y + qd.Y * h, Attitude.Z + qd.Z * h,
                Attitude.W + qd.W * h).Normalized();
        }
    }

    /// <summary>How nearly he is hanging the right way up under the lines: 1 = his long axis along them, −1 = upside
    /// down to them. What "he has stabilised" means, and it is read, not set.</summary>
    public double HangAlignment(Vec3 hangWorld)
    {
        double l = hangWorld.Length;
        if (l < 1e-9) return 0.0;
        // His long axis toward his head is body −z.
        return Attitude.Rotate(new Vec3(0, 0, -1)).Dot(hangWorld * (1.0 / l));
    }
}

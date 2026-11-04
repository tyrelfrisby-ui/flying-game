using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// TWO-BONE IK, analytic: a shoulder–elbow–wrist (or hip–knee–ankle) chain reaching for a target, with a pole
    /// that says which way the middle joint bends. Any frame, any units.
    ///
    /// With root R, target T, bone lengths a (upper) and b (lower), d = |T − R| clamped to [|a − b|, a + b]:
    ///
    ///   along the reach:   x = (a² − b² + d²) / (2d)          (law of cosines, measured from the root)
    ///   off the reach:     h = √(a² − x²)
    ///   middle joint:      M = R + x·û + h·p̂
    ///   end:               E = R + d·û
    ///
    /// û = (T − R)/|T − R|, and p̂ is the pole direction (pole − R) with its component along û removed, normalised —
    /// so the elbow swings toward the pole (out and down for an arm, forward for a knee) and never flips through
    /// the reach line. A target out of reach leaves the chain straight and pointing at it (E short of T); one
    /// closer than |a − b| folds it as far as it goes. Both bones keep their lengths exactly, always.
    ///
    /// Cost: one sqrt for d, one for h, one to normalise the pole — about 60 flops. Four chains a frame is nothing.
    /// </summary>
    public static class TwoBoneIk
    {
        public readonly struct Result
        {
            public readonly Vec3 Middle, End;
            /// <summary>False when the target was out of reach (or inside the fold) and the end stops short of it.</summary>
            public readonly bool Reached;
            public Result(Vec3 middle, Vec3 end, bool reached) { Middle = middle; End = end; Reached = reached; }
        }

        public static Result Solve(Vec3 root, Vec3 target, double upper, double lower, Vec3 pole)
        {
            Vec3 toT = target - root;
            double dRaw = toT.Length;
            Vec3 u = dRaw > 1e-9 ? toT / dRaw : new Vec3(0, -1, 0);

            double max = upper + lower, min = Math.Abs(upper - lower);
            // A hair inside the limits so h stays real and the joint has a direction to bend in.
            double d = Math.Clamp(dRaw, min + 1e-6, max - 1e-6);
            bool reached = dRaw <= max && dRaw >= min;

            Vec3 p = pole - root;
            p -= u * p.Dot(u);
            if (p.LengthSquared < 1e-12)
            {
                // The pole lies on the reach line: pick any perpendicular, deterministic.
                p = Math.Abs(u.Y) < 0.9 ? new Vec3(0, -1, 0) : new Vec3(0, 0, 1);
                p -= u * p.Dot(u);
            }
            p = p.Normalized();

            double x = (upper * upper - lower * lower + d * d) / (2.0 * d);
            double h = Math.Sqrt(Math.Max(0.0, upper * upper - x * x));
            return new Result(root + u * x + p * h, root + u * d, reached);
        }
    }
}

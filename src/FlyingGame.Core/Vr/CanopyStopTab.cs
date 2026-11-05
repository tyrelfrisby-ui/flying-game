using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE PITTS' CANOPY STOP TAB, AND WHAT THE OPEN CANOPY TOUCHES (Ty 2026-11-05: "Small canopy stop tab on top of the
    /// right wing: 1/4-in bent round-stock steel bar with black rubber; canopy contacts it to limit travel, so the geometry
    /// must be correct. After jettison is pulled, canopy slides back farther, no longer catches the tab, opens further,
    /// releases hinges and can tear away").
    ///
    /// There is no stop angle anywhere in this. The canopy's 3/8 in tube frame is a set of capsules fixed to the canopy;
    /// the tab is a capsule fixed to the right upper wing; the wing's skin near its trailing edge is a surface. Every
    /// substep CanopyDynamics carries the frame through its slide and swing (PointOnCanopy) and asks this class what it
    /// is pressing on. A contact is a stiff spring-damper along the contact normal (rubber over a bent bar, E), with
    /// Coulomb friction against the canopy's slide. Where the open canopy comes to rest, and whether it does, falls out of
    /// where the tab is and how far the canopy has slid:
    ///
    ///   4 in of slide (the track's normal stop): the left sill tube (the free edge) still overlaps the tab chordwise and
    ///          lands on it at 127.5° (<see cref="RestAngleDeg"/>), which the tests measure;
    ///   jettisoned: the track's stop is pulled and the canopy runs on aft (its own drag, or a hand) a further
    ///          <see cref="JettisonExtraSlideM"/>. Its front corner then passes behind the tab and the wing's trailing
    ///          edge, nothing catches it, and past <see cref="HookReleaseDeg"/> the open hinge hooks lift off their pins.
    ///
    /// FRAME: cockpit axes (+x right, +y up, +z nose), metres from the hinge line with the canopy SHUT. Measured off
    /// pitts-s2b-like-own.glb (glTF +x aft, −z right; hinge = the Canopy node's origin at glTF (0.8, 0.16, −0.36)):
    /// sills at y 0.17 from glTF x 0.126 to 1.60, ±0.358; glass crown 0.549 at x 0.8 and 0.547 at 1.1, 0.429 at the tail;
    /// the front edge low (0.222 at the centre-line). The two arcing top tubes are put at those two crown stations.
    ///
    /// THE WING is not simulated here: whether the swinging canopy clears the upper wing (through the entry cutout at
    /// the root) and where it first comes down onto the right wing is measured against the model's own Wing_Upper mesh
    /// by tools/own_models/check_pitts_canopy_swing.py. On lane/codex-pitts-cockpit-v2's model, slid the normal 4 in,
    /// the canopy's front-left corner is the first thing to reach the right wing, at 129.5°, about 0.40 m right of the
    /// hinge; slid 13 in (4 + 9) it clears the wing all the way over. The TAB stands on the skin there, under the front
    /// edge's track, a bar's thickness proud of where the frame would otherwise land, so the frame meets the tab first
    /// (that check fails if it does not). The Bridge replaces the default with the model's Canopy_Stop_Rubber node.
    /// </summary>
    public sealed class CanopyStopTab
    {
        public readonly struct Segment
        {
            public readonly Vec3 A, B;
            public Segment(Vec3 a, Vec3 b) { A = a; B = b; }
        }

        /// <summary>The frame tube, 3/8 in diameter (Ty).</summary>
        public const double TubeRadiusM = 0.0047625;
        /// <summary>1/4 in round stock (Ty) under about 1/16 in of rubber (E): 3.175 + 1.6 mm.</summary>
        public const double TabRadiusDefaultM = 0.004775;
        /// <summary>Contact stiffness, N/m (E: rubber pad and a short bent bar; a 400 J slam sinks it ~2.8 cm, at rest
        /// it sinks well under a millimetre), damping ratio on the canopy's effective mass there, and the rubber's
        /// friction against the tube.</summary>
        public const double StiffnessNPerM = 1.0e6, DampingRatio = 0.3, RubberFriction = 0.8;

        public readonly Segment[] Frame;
        public readonly Segment Tab;
        public readonly double TabRadiusM;
        /// <summary>Extra slide once the jettison has pulled the track's stop (E, see the class note).</summary>
        public readonly double JettisonExtraSlideM;
        /// <summary>Opened this far with the jettison pulled, the hinge hooks come off their pins (E).</summary>
        public readonly double HookReleaseDeg;

        public CanopyStopTab(Segment[] frame, Segment tab, double tabRadiusM, double jettisonExtraSlideM, double hookReleaseDeg)
        {
            Frame = frame; Tab = tab; TabRadiusM = tabRadiusM;
            JettisonExtraSlideM = jettisonExtraSlideM; HookReleaseDeg = hookReleaseDeg;
        }

        /// <summary>The same canopy with the tab where the model puts it (its two ends, hinge-relative, shut).</summary>
        public CanopyStopTab WithTab(Vec3 a, Vec3 b, double radiusM) =>
            new(Frame, new Segment(a, b), radiusM, JettisonExtraSlideM, HookReleaseDeg);

        /// <summary>glTF (x aft, y up, z left) → cockpit axes relative to the Pitts' hinge at glTF (0.8, 0.16, −0.36).</summary>
        public static Vec3 FromPittsGltf(double x, double y, double z) => new(-(z + 0.36), y - 0.16, -(x - 0.8));

        /// <summary>The S-2B as measured (class note).</summary>
        public static CanopyStopTab Pitts()
        {
            const double sillY = 0.01, left = -0.712, right = 0.008, front = 0.674, aft = -0.80;
            var f = new List<Segment>
            {
                new(new Vec3(left, sillY, front), new Vec3(left, sillY, aft)),     // left sill: the free edge
                new(new Vec3(right, sillY, front), new Vec3(right, sillY, aft)),   // right sill, on the hinge line
            };
            // Front edge, the two arcing top tubes, the tail: half-ellipses from sill to sill (crowns above the hinge).
            Arch(f, front, 0.062);
            Arch(f, 0.0, 0.389);
            Arch(f, -0.30, 0.387);
            Arch(f, aft, 0.269);
            // The bar's top run, fore-aft across the front edge's track, where the swing check puts the front-left corner
            // on the wing: the front edge 0.62 m inboard of the hinge (shut), slid 4 in, at 127.5°, plus the two radii
            // along the way it is swinging, so that is where they meet.
            Vec3 corner = Pose(new Vec3(-0.62, ArchHeight(-0.62, 0.062), front), 0.1016, 127.5);
            Vec3 swing = SwingDir(corner);
            Vec3 c = corner + swing * (TabRadiusDefaultM + TubeRadiusM);
            var tab = new Segment(c + new Vec3(0, 0, 0.02), c - new Vec3(0, 0, 0.02));   // 40 mm, fore-aft
            return new CanopyStopTab(f.ToArray(), tab, TabRadiusDefaultM, jettisonExtraSlideM: 0.2286, hookReleaseDeg: 150.0);
        }

        /// <summary>The unit direction a point (airframe frame, hinge at its shut place) moves as the canopy opens.</summary>
        private static Vec3 SwingDir(Vec3 p)
        {
            var d = new Vec3(p.Y, -p.X, 0.0);
            return d / d.Length;
        }

        /// <summary>The height of an arch with this crown above the sill, at x (cockpit, hinge-relative, shut).</summary>
        private static double ArchHeight(double x, double crown)
        {
            double c = Math.Clamp((x + 0.352) / 0.36, -1.0, 1.0);
            return 0.01 + crown * Math.Sqrt(1.0 - c * c);
        }

        private static void Arch(List<Segment> f, double z, double crown)
        {
            const int n = 8;
            const double cx = -0.352, w = 0.36, y0 = 0.01;
            Vec3 prev = default;
            for (int i = 0; i <= n; i++)
            {
                double t = Math.PI * (1.0 - (double)i / n);   // left (π) → right (0)
                var p = new Vec3(cx + w * Math.Cos(t), y0 + crown * Math.Sin(t), z);
                if (i > 0) f.Add(new Segment(prev, p));
                prev = p;
            }
        }

        /// <summary>One contact: where on the canopy (airframe frame, <see cref="Pose"/>), its unit normal (onto the
        /// canopy) and how deep.</summary>
        public readonly struct Contact
        {
            public readonly Vec3 Point, Normal;
            public readonly double DepthM;
            public Contact(Vec3 p, Vec3 n, double d) { Point = p; Normal = n; DepthM = d; }
        }

        /// <summary>
        /// Everything the frame is pressing on with the canopy slid <paramref name="slideM"/> and open
        /// <paramref name="angleDeg"/>, into <paramref name="into"/> (cleared first), in the airframe frame (<see cref="Pose"/>).
        /// Only past 45°: before that the frame is over the fuselage, nowhere near the tab.
        /// </summary>
        public void Contacts(double slideM, double angleDeg, List<Contact> into)
        {
            into.Clear();
            if (angleDeg < 45.0) return;
            double rr = TabRadiusM + TubeRadiusM;
            // The tab, exactly: segment against segment.
            foreach (Segment g in Frame)
            {
                Vec3 a = Pose(g.A, slideM, angleDeg), b = Pose(g.B, slideM, angleDeg);
                ClosestSegSeg(a, b, Tab.A, Tab.B, out Vec3 p, out Vec3 q);
                Vec3 d = p - q;
                double l = d.Length;
                if (l >= rr || l < 1e-9) continue;
                into.Add(new Contact(p, d / l, rr - l));
            }
        }

        /// <summary>A frame point with the canopy slid and swung, in the airframe's frame: relative to where the hinge is
        /// with the canopy SHUT (the tab is fixed there). The hinge itself is now at (0, 0, −slide).</summary>
        public static Vec3 Pose(Vec3 shut, double slideM, double angleDeg) =>
            CanopyDynamics.PointOnCanopy(shut, Vec3.Zero, slideM, angleDeg);

        /// <summary>Where the swinging frame first meets the tab with the canopy slid a given distance: the angle, and
        /// the contact's point and normal (onto the canopy) at that angle. Deg is NaN when it swings to 180° untouched.</summary>
        public readonly struct Touch
        {
            public readonly double Deg;
            public readonly Vec3 Point, Normal;
            public Touch(double deg, Vec3 p, Vec3 n) { Deg = deg; Point = p; Normal = n; }
            public bool Hits => !double.IsNaN(Deg);
        }

        /// <summary>Slide buckets of the touch table, m.</summary>
        public const double TouchStepM = 0.002;
        private readonly Dictionary<int, Touch> _touch = new();
        private readonly List<Contact> _scratch = new();

        /// <summary>
        /// THE CONTACT AS THE DYNAMICS USE IT. A capsule test at one instant lets a slam carry the tube straight through a
        /// 1/4 in bar (the first version did, at 60 KIAS). The frame only ever comes at the tab along its swing, so the
        /// contact is taken in the swing instead: for the canopy slid <paramref name="slideM"/>, the angle at which the
        /// frame first touches the tab (found from the geometry, <see cref="FirstTouch"/>, cached in 2 mm buckets and
        /// interpolated between them), and how far past it the canopy is. Slid until that bucket has no touch, there is
        /// no contact at all, which is how a jettisoned canopy comes off the tab.
        /// </summary>
        public Touch TouchAt(double slideM)
        {
            double u = Math.Max(0.0, slideM) / TouchStepM;
            int k = (int)Math.Floor(u);
            double w = u - k;
            Touch a = Bucket(k), b = Bucket(k + 1);
            if (a.Hits && b.Hits) return new Touch(a.Deg + (b.Deg - a.Deg) * w, a.Point + (b.Point - a.Point) * w, w < 0.5 ? a.Normal : b.Normal);
            if (a.Hits && w < 0.5) return a;
            if (b.Hits && w >= 0.5) return b;
            return new Touch(double.NaN, Vec3.Zero, Vec3.Zero);
        }

        private Touch Bucket(int k)
        {
            if (_touch.TryGetValue(k, out Touch t)) return t;
            t = FirstTouch(k * TouchStepM);
            _touch[k] = t;
            return t;
        }

        /// <summary>The first touch, by a 0.5° scan from 45° and a bisection to 0.001° on the bracket.</summary>
        public Touch FirstTouch(double slideM, double maxDeg = 180.0)
        {
            double lo = double.NaN;
            for (double d = 45.0; d <= maxDeg + 1e-9; d += 0.5)
            {
                Contacts(slideM, d, _scratch);
                if (_scratch.Count > 0) { lo = d - 0.5; break; }
            }
            if (double.IsNaN(lo)) return new Touch(double.NaN, Vec3.Zero, Vec3.Zero);
            double hi = lo + 0.5;
            for (int i = 0; i < 20; i++)
            {
                double mid = 0.5 * (lo + hi);
                Contacts(slideM, mid, _scratch);
                if (_scratch.Count > 0) hi = mid; else lo = mid;
            }
            Contacts(slideM, hi, _scratch);
            Contact c = _scratch[0];
            return new Touch(hi, c.Point, c.Normal);
        }

        /// <summary>Where the canopy, slid <paramref name="slideM"/>, first touches the tab (NaN: never).</summary>
        public double FirstTouchDeg(double slideM) => FirstTouch(slideM).Deg;

        /// <summary>Where it rests on the tab after the normal slide (iOS's scripted canopy opens to this).</summary>
        public double RestAngleDeg(double normalSlideM) => TouchAt(normalSlideM).Deg;

        /// <summary>Closest points of segments p1–q1 and p2–q2 (Ericson, Real-Time Collision Detection §5.1.9).</summary>
        public static void ClosestSegSeg(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2, out Vec3 c1, out Vec3 c2)
        {
            Vec3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
            double s, t;
            if (a <= 1e-12 && e <= 1e-12) { c1 = p1; c2 = p2; return; }
            if (a <= 1e-12) { s = 0.0; t = Math.Clamp(f / e, 0.0, 1.0); }
            else
            {
                double c = d1.Dot(r);
                if (e <= 1e-12) { t = 0.0; s = Math.Clamp(-c / a, 0.0, 1.0); }
                else
                {
                    double b = d1.Dot(d2), den = a * e - b * b;
                    s = den > 1e-12 ? Math.Clamp((b * f - c * e) / den, 0.0, 1.0) : 0.0;
                    t = (b * s + f) / e;
                    if (t < 0.0) { t = 0.0; s = Math.Clamp(-c / a, 0.0, 1.0); }
                    else if (t > 1.0) { t = 1.0; s = Math.Clamp((b - c) / a, 0.0, 1.0); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
    }
}

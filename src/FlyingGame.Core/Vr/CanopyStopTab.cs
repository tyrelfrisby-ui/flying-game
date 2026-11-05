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
    /// the tab's rubber is a capsule fixed to the airframe. CanopyDynamics carries the frame through its slide and swing
    /// (PointOnCanopy); where the frame first touches the rubber at the present slide (<see cref="TouchAt"/>) is where it
    /// stops, and whether it touches at all depends on how far it has slid:
    ///
    ///   4 in of slide (the track's normal stop): the front hoop comes over onto the rubber at about 100°
    ///          (<see cref="RestAngleDeg"/>, the tests measure it);
    ///   jettisoned: the track's stop is pulled and the canopy runs on aft (its own drag, or a hand) a further
    ///          <see cref="JettisonExtraSlideM"/>; past about 1 in more the hoop passes behind the end of the tab, nothing
    ///          catches it, and past <see cref="HookReleaseDeg"/> the open hinge hooks lift off their pins.
    ///
    /// GEOMETRY, copied from lane/codex-pitts-cockpit-v2's pitts-s2b-like-own.glb (b221022a): glTF +x aft, y up, −z right;
    /// the hinge is the Canopy node's origin at (0.8, 0.16, −0.36). Cockpit axes here: +x right, +y up, +z nose, metres
    /// from the hinge with the canopy SHUT (<see cref="FromPittsGltf"/>).
    ///   frame   Canopy_Frame: sills at y 0.153–0.17 from x 0.465 to 1.605, ±0.349; the FRONT HOOP at x 0.47 (it meets
    ///           the fixed windshield), its centre-line through (|z|, y) = (0.36, 0.167) (0.30, 0.26) (0.24, 0.404)
    ///           (0.18, 0.447) (0.12, 0.471) (0.06, 0.482) (0, 0.484); the tail at 1.605, the glass 0.429 there. No
    ///           top tubes between them (Ty 2026-11-05 07:26: "The two grey hoops on the canopy need to be eliminated").
    ///   tab     Canopy_Stop_Rubber: a fore-aft capsule from x 0.564 to 0.580 at y 0.658, z −0.721, radius 6 mm, on the
    ///           end of Canopy_Stop_Tab, the bent bar that runs aft from the right upper wing past its trailing edge.
    /// The wing itself is not simulated: the canopy is wholly aft of the upper wing's trailing edge, and
    /// tools/own_models/check_pitts_canopy_swing.py sweeps the model's canopy meshes against its Wing_Upper mesh to show
    /// it never touches it (shut, slid 4 in, and jettisoned). The Bridge takes the tab from the model's own
    /// Canopy_Stop_Rubber node when there is one (<see cref="WithTab"/>).
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
        /// <summary>The rubber over the 1/4 in round stock (Ty): Codex's Canopy_Stop_Rubber, 12 mm across.</summary>
        public const double TabRadiusDefaultM = 0.006;
        /// <summary>Contact stiffness, N/m (E: rubber and a bent bar; a slam sinks it a centimetre or two, at rest well
        /// under a millimetre), damping ratio on the canopy's effective mass there, and the rubber's
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
            Vec3 G(double x, double y, double z) => FromPittsGltf(x, y, z);
            const double sill = 0.16, front = 0.467, aft = 1.605;
            var f = new List<Segment>
            {
                new(G(front, sill, 0.349), G(aft, sill, 0.349)),     // left sill: the free edge
                new(G(front, sill, -0.349), G(aft, sill, -0.349)),   // right sill, on the hinge line
            };
            // The front hoop, measured, left to right.
            double[] hz = { 0.36, 0.30, 0.24, 0.18, 0.12, 0.06, 0.0, -0.06, -0.12, -0.18, -0.24, -0.30, -0.36 };
            double[] hy = { 0.167, 0.26, 0.404, 0.447, 0.471, 0.482, 0.484, 0.482, 0.471, 0.447, 0.404, 0.26, 0.167 };
            for (int i = 1; i < hz.Length; i++) f.Add(new Segment(G(front, hy[i - 1], hz[i - 1]), G(front, hy[i], hz[i])));
            // The tail: a half-ellipse from sill to sill.
            Arch(f, G(aft, 0, 0).Z, 0.429 - sill);
            var tab = new Segment(G(0.564, 0.658, -0.721), G(0.580, 0.658, -0.721));
            return new CanopyStopTab(f.ToArray(), tab, TabRadiusDefaultM, jettisonExtraSlideM: 0.0508, hookReleaseDeg: 150.0);
        }

        private static void Arch(List<Segment> f, double z, double crown)
        {
            const int n = 8;
            const double cx = -0.36, w = 0.349, y0 = 0.0;
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

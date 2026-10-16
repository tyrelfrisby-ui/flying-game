using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Everything in the Reno high desert that is not ground: the sagebrush, the rock and the junipers
    /// scattered over it, the graded strips with the wheel ruts worn down them, and the rock piles that mark
    /// each threshold. The mesas and the playa themselves come out of the height field like all terrain.
    ///
    /// The strips are what this place is FOR, so they are drawn to be read from the air: a graded surface a
    /// shade paler than the desert, two dark ruts down the middle where everybody's wheels have gone, and
    /// four cairns standing at the corners of the two thresholds.
    /// </summary>
    public static class HighDesertBuilder
    {
        private static Vector3 U(double x, double y, double up) => WorldBuilder.U(x, y, up);

        private static readonly Color Sage = new(0.42f, 0.45f, 0.33f);
        private static readonly Color DesertRock = new(0.52f, 0.44f, 0.36f);
        private static readonly Color Juniper = new(0.22f, 0.30f, 0.20f);
        private static readonly Color JuniperTrunk = new(0.38f, 0.30f, 0.23f);
        private static readonly Color Graded = new(0.63f, 0.56f, 0.46f);
        private static readonly Color Rut = new(0.46f, 0.40f, 0.33f);
        private static readonly Color Cairn = new(0.58f, 0.50f, 0.42f);
        private static readonly Color Playa = new(0.76f, 0.73f, 0.66f);

        public static void Build(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("HighDesert");
            root.transform.SetParent(parent, false);
            BuildPlayaFloor(root.transform);
            BuildScrub(root.transform);
            BuildStrips(root.transform);
        }

        /// <summary>The dry lake: pale cracked clay, laid flat on its own surface so it reads from miles away
        /// as the one place out here you can simply put down.</summary>
        private static void BuildPlayaFloor(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            const int n = 40;
            double up = HighDesert.PlayaSurfaceM + 0.03;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Mathf.PI * i / n, a1 = 2 * Mathf.PI * (i + 1) / n;
                Vector3 C = U(HighDesert.PlayaX, HighDesert.PlayaY, up);
                Vector3 P0 = U(HighDesert.PlayaX + Mathf.Cos((float)a0) * HighDesert.PlayaRx,
                               HighDesert.PlayaY + Mathf.Sin((float)a0) * HighDesert.PlayaRy, up);
                Vector3 P1 = U(HighDesert.PlayaX + Mathf.Cos((float)a1) * HighDesert.PlayaRx,
                               HighDesert.PlayaY + Mathf.Sin((float)a1) * HighDesert.PlayaRy, up);
                b.Tri(C, P0, P1, Playa);
            }
            b.Build("Playa", parent, SeasideBuilder.VC);
        }

        /// <summary>Sage, rock and juniper — the same scatter every time, so a hilltop can be learned.</summary>
        private static void BuildScrub(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (HighDesert.Hazard hz in HighDesert.Hazards())
            {
                double gh = HighDesert.GroundForSolidAt(hz.X, hz.Y);
                var rot = Quaternion.Euler(0, (float)hz.HeadingDeg, 0);
                float r = (float)hz.RadiusM, hgt = (float)hz.HeightM;
                switch (hz.Kind)
                {
                    case HighDesert.Scrub.Rock:
                        // A boulder sitting in the sage: angular, the colour of the ground it came out of.
                        b.Box(U(hz.X, hz.Y, gh + hgt * 0.35), new Vector3(r * 1.6f, hgt, r * 1.4f),
                              rot, DesertRock, DesertRock * 1.2f);
                        break;
                    case HighDesert.Scrub.Juniper:
                        // A tree: a short thick trunk and a dark ragged crown. The only thing out here you
                        // would see from any height, and the only thing that will stop an aeroplane dead.
                        b.Box(U(hz.X, hz.Y, gh + hgt * 0.25), new Vector3(r * 0.45f, hgt * 0.5f, r * 0.45f),
                              rot, JuniperTrunk);
                        b.Box(U(hz.X, hz.Y, gh + hgt * 0.70), new Vector3(r * 1.9f, hgt * 0.7f, r * 1.9f),
                              rot, Juniper, Juniper * 1.3f);
                        break;
                    default:
                        // Sagebrush: low, grey-green, and the thing the whole desert is made of.
                        b.Box(U(hz.X, hz.Y, gh + hgt * 0.5), new Vector3(r * 1.9f, hgt, r * 1.9f),
                              rot, Sage, Sage * 1.25f);
                        break;
                }
            }
            b.Build("HighDesertScrub", parent, SeasideBuilder.VC);
        }

        /// <summary>The made strips: graded surface, worn wheel ruts, and the four rock piles that mark the
        /// thresholds a hundred feet apart.</summary>
        private static void BuildStrips(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (HighDesert.DesertStrip s in HighDesert.Strips)
            {
                double HAt(double f, double across)
                {
                    var (px, py) = s.At(f, across);
                    return HighDesert.StripSurfaceAt(s, px, py) + 0.04;
                }

                const int n = 28;
                for (int i = 0; i < n; i++)
                {
                    double f0 = -0.5 + (double)i / n, f1 = -0.5 + (double)(i + 1) / n;
                    double w = s.WidthM * 0.5;
                    var (x0l, y0l) = s.At(f0, -w); var (x0r, y0r) = s.At(f0, w);
                    var (x1l, y1l) = s.At(f1, -w); var (x1r, y1r) = s.At(f1, w);
                    b.Quad(U(x0l, y0l, HAt(f0, -w)), U(x1l, y1l, HAt(f1, -w)),
                           U(x1r, y1r, HAt(f1, w)), U(x0r, y0r, HAt(f0, w)), Graded);

                    // The ruts: two worn tracks a main gear's width apart, darker than the graded surface
                    // because everybody who has landed here has gone down them.
                    foreach (int side in new[] { -1, 1 })
                    {
                        double c = side * HighDesert.RutGaugeM * 0.5;
                        double rw = 0.45;
                        var (a0, b0) = s.At(f0, c - rw); var (a1, b1) = s.At(f0, c + rw);
                        var (a2, b2) = s.At(f1, c - rw); var (a3, b3) = s.At(f1, c + rw);
                        b.Quad(U(a0, b0, HAt(f0, c) + 0.02), U(a2, b2, HAt(f1, c) + 0.02),
                               U(a3, b3, HAt(f1, c) + 0.02), U(a1, b1, HAt(f0, c) + 0.02), Rut);
                    }
                }

                // The rock piles: a cairn at each corner of each threshold, 100 ft apart across the strip, so
                // the two of them read as a gate you fly through on the way in.
                foreach (var (px, py, radius, height) in HighDesert.ThresholdPiles(s))
                {
                    double gh = HighDesert.StripSurfaceAt(s, px, py);
                    float rr = (float)radius, hh = (float)height;
                    b.Box(U(px, py, gh + hh * 0.22), new Vector3(rr * 2.0f, hh * 0.45f, rr * 2.0f), Cairn, Cairn * 1.2f);
                    b.Box(U(px, py, gh + hh * 0.62), new Vector3(rr * 1.3f, hh * 0.4f, rr * 1.3f), Cairn * 1.1f);
                    b.Box(U(px, py, gh + hh * 0.90), new Vector3(rr * 0.7f, hh * 0.3f, rr * 0.7f), Cairn * 0.9f);
                }
            }
            b.Build("HighDesertStrips", parent, SeasideBuilder.VC);
        }
    }
}

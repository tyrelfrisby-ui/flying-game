using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The Alaska range and the country below it: glacier ice, the lakes it calves into with bergs in them,
    /// the braided river's channels and bars, the driftwood and boulders on the gravel, and the two made
    /// strips with their cones, chairs, chalk line and picnic benches.
    ///
    /// The terrain itself comes from the height field like everywhere else; what is drawn here is everything
    /// that is not ground — the water, the ice surface, and the things standing on it.
    /// </summary>
    public static class AlaskaBuilder
    {
        private static Vector3 U(double x, double y, double up) => WorldBuilder.U(x, y, up);

        private static readonly Color Ice = new(0.80f, 0.88f, 0.95f);
        private static readonly Color IceDirty = new(0.62f, 0.66f, 0.68f);
        private static readonly Color Berg = new(0.90f, 0.95f, 1.0f);
        private static readonly Color Gravel = new(0.52f, 0.50f, 0.47f);
        private static readonly Color Wood = new(0.44f, 0.36f, 0.26f);
        private static readonly Color Rock = new(0.38f, 0.36f, 0.34f);
        private static readonly Color Bush = new(0.26f, 0.38f, 0.20f);
        private static readonly Color Cone = new(0.95f, 0.42f, 0.08f);
        private static readonly Color Chalk = new(0.95f, 0.95f, 0.92f);
        private static readonly Color Bench = new(0.55f, 0.40f, 0.26f);
        private static readonly Color Chair = new(0.20f, 0.35f, 0.60f);

        public static void Build(WorldTerrain t, Transform parent)
        {
            var root = new GameObject("Alaska");
            root.transform.SetParent(parent, false);
            BuildGlaciers(root.transform);
            BuildGlacierLakes(root.transform);
            BuildBraids(t, root.transform);
            BuildObstacles(root.transform);
            BuildMadeStrips(root.transform);
        }

        /// <summary>The ice: a crowned surface down each trough, dirtier toward the snout where the moraine is.</summary>
        private static void BuildGlaciers(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
            {
                double dx = (g.TermX - g.HeadX) / g.LengthM, dy = (g.TermY - g.HeadY) / g.LengthM;
                double rx = -dy, ry = dx;
                const int ns = 60, nu = 10;
                for (int i = 0; i < ns; i++)
                {
                    double t0 = (double)i / ns, t1 = (double)(i + 1) / ns;
                    for (int j = 0; j < nu; j++)
                    {
                        double u0 = -g.HalfWidthM + 2 * g.HalfWidthM * j / nu;
                        double u1 = -g.HalfWidthM + 2 * g.HalfWidthM * (j + 1) / nu;
                        Vector3 P(double tt, double uu) =>
                            U(g.HeadX + dx * g.LengthM * tt + rx * uu,
                              g.HeadY + dy * g.LengthM * tt + ry * uu,
                              g.IceHeightAt(tt, System.Math.Abs(uu)) + 0.4);
                        // Crevasse striping across the ice, and moraine dirt down the sides and at the snout.
                        float stripe = (i % 3 == 0) ? 0.90f : 1f;
                        float dirt = (float)System.Math.Max(System.Math.Abs(u0) / g.HalfWidthM, t0 * t0);
                        Color c = Color.Lerp(Ice, IceDirty, Mathf.Clamp01(dirt * 0.9f)) * stripe;
                        b.Quad(P(t0, u0), P(t1, u0), P(t1, u1), P(t0, u1), c);
                    }
                }
            }
            b.Build("GlacierIce", parent, SeasideBuilder.VC);
        }

        /// <summary>The proglacial lakes, and the bergs floating in them.</summary>
        private static void BuildGlacierLakes(Transform parent)
        {
            var water = WorldBuilder.Mat("FlyingGame/Water", new Color(0.35f, 0.55f, 0.62f, 0.92f));  // glacial milk
            var bergs = new SeasideBuilder.Batch();
            foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
            {
                var (cx, cy) = g.LakeCentre;
                const int seg = 44;
                var v = new Vector3[seg + 1]; var tr = new int[seg * 3];
                v[0] = U(cx, cy, g.LakeSurfaceM);
                for (int i = 0; i < seg; i++)
                {
                    double a = i * System.Math.PI * 2 / seg;
                    v[i + 1] = U(cx + g.LakeRx * System.Math.Cos(a), cy + g.LakeRy * System.Math.Sin(a), g.LakeSurfaceM);
                }
                for (int i = 0; i < seg; i++) { tr[i * 3] = 0; tr[i * 3 + 1] = i + 1; tr[i * 3 + 2] = (i + 1) % seg + 1; }
                WorldBuilder.Spawn($"GlacierLake-{g.Name}", v, tr, water, parent, true);

                foreach (var (bx, by, radius, above) in AlaskaRange.Icebergs(g))
                {
                    // A berg: a rough block, most of it under, tilted a little.
                    var rot = Quaternion.Euler((float)(bx % 11) - 5f, (float)(by % 90), (float)(by % 9) - 4f);
                    bergs.Box(U(bx, by, g.LakeSurfaceM + above * 0.4),
                              new Vector3((float)radius * 1.4f, (float)(above + radius * 0.5) , (float)radius * 1.2f),
                              rot, Berg, Berg * 1.05f);
                }
            }
            bergs.Build("Icebergs", parent, SeasideBuilder.VC);
        }

        /// <summary>The braids: a shallow ribbon of water down each channel, broken where a bar crosses it.</summary>
        private static void BuildBraids(WorldTerrain t, Transform parent)
        {
            var water = WorldBuilder.Mat("FlyingGame/Water", new Color(0.42f, 0.58f, 0.62f, 0.85f));
            for (int braid = 0; braid < BraidedRiver.Braids; braid++)
            {
                var v = new List<Vector3>(); var tr = new List<int>();
                int run = 0;
                for (double y = BraidedRiver.Y0 + 40; y < BraidedRiver.Y1 - 40; y += 45)
                {
                    double cx = BraidedRiver.ChannelCentreX(braid, y);
                    double? w = BraidedRiver.WaterSurfaceAt(cx, y);
                    if (!w.HasValue) { run = 0; continue; }
                    double hw = BraidedRiver.ChannelHalfWidthM * 0.8;
                    v.Add(U(cx - hw, y, w.Value)); v.Add(U(cx + hw, y, w.Value));
                    if (run > 0)
                    {
                        int bidx = v.Count - 4;
                        tr.AddRange(new[] { bidx, bidx + 2, bidx + 1, bidx + 1, bidx + 2, bidx + 3 });
                    }
                    run++;
                }
                if (tr.Count > 0) WorldBuilder.Spawn($"Braid{braid}", v.ToArray(), tr.ToArray(), water, parent, true);
            }
        }

        /// <summary>Driftwood, boulders and bushes — the same every time, so a bar can be learned.</summary>
        private static void BuildObstacles(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (BraidedRiver.Hazard hz in BraidedRiver.Hazards())
            {
                double gh = BraidedRiver.PlainHeightAt(hz.X, hz.Y);
                var rot = Quaternion.Euler(0, (float)hz.HeadingDeg, 0);
                switch (hz.Kind)
                {
                    case BraidedRiver.Obstacle.Driftwood:
                        // A bleached log lying on the gravel, with a stump of root at one end.
                        b.Box(U(hz.X, hz.Y, gh + hz.HeightM * 0.4),
                              new Vector3((float)hz.RadiusM * 0.35f, (float)hz.HeightM * 0.8f, (float)hz.RadiusM * 2f),
                              rot, Wood * 1.35f);
                        b.Box(U(hz.X, hz.Y, gh + hz.HeightM * 0.7), new Vector3(1.6f, 1.2f, 1.2f), rot, Wood);
                        break;
                    case BraidedRiver.Obstacle.Boulder:
                        b.Box(U(hz.X, hz.Y, gh + hz.HeightM * 0.35),
                              new Vector3((float)hz.RadiusM * 1.6f, (float)hz.HeightM, (float)hz.RadiusM * 1.4f),
                              rot, Rock, Rock * 1.2f);
                        break;
                    default:
                        b.Box(U(hz.X, hz.Y, gh + hz.HeightM * 0.5),
                              new Vector3((float)hz.RadiusM * 1.8f, (float)hz.HeightM, (float)hz.RadiusM * 1.8f),
                              rot, Bush, Bush * 1.25f);
                        break;
                }
            }
            b.Build("BraidedRiverObstacles", parent, SeasideBuilder.VC);
        }

        /// <summary>The practice strip with its cones, chairs and chalk line, and the picnic strip's benches.</summary>
        private static void BuildMadeStrips(Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (BraidedRiver.NamedStrip s in BraidedRiver.NamedStrips)
            {
                double grade = (BraidedRiver.HeadM - BraidedRiver.FootM) / (BraidedRiver.Y1 - BraidedRiver.Y0);
                double baseH = BraidedRiver.PlainHeightAt(s.X, s.Y);
                double HAt(double f) => baseH + f * s.LengthM * grade + 0.05;

                // The graded surface itself, a shade darker than the plain so it reads from the air.
                const int n = 24;
                for (int i = 0; i < n; i++)
                {
                    double f0 = -0.5 + (double)i / n, f1 = -0.5 + (double)(i + 1) / n;
                    var (x0l, y0l) = s.At(f0, -s.WidthM * 0.5);
                    var (x0r, y0r) = s.At(f0, s.WidthM * 0.5);
                    var (x1l, y1l) = s.At(f1, -s.WidthM * 0.5);
                    var (x1r, y1r) = s.At(f1, s.WidthM * 0.5);
                    b.Quad(U(x0l, y0l, HAt(f0)), U(x1l, y1l, HAt(f1)), U(x1r, y1r, HAt(f1)), U(x0r, y0r, HAt(f0)),
                           Gravel * 0.92f);
                }

                // The chalk line, right across it.
                if (s.ChalkLine)
                {
                    double f = BraidedRiver.ChalkLineFrac;
                    var (lx, ly) = s.At(f, -s.WidthM * 0.5);
                    var (rx, ry) = s.At(f, s.WidthM * 0.5);
                    var (lx2, ly2) = s.At(f + 0.004, -s.WidthM * 0.5);
                    var (rx2, ry2) = s.At(f + 0.004, s.WidthM * 0.5);
                    b.Quad(U(lx, ly, HAt(f) + 0.02), U(lx2, ly2, HAt(f) + 0.02),
                           U(rx2, ry2, HAt(f) + 0.02), U(rx, ry, HAt(f) + 0.02), Chalk);
                }

                foreach (var (cx, cy) in BraidedRiver.Cones(s))
                {
                    double gh = BraidedRiver.PlainHeightAt(cx, cy);
                    b.Box(U(cx, cy, gh + 0.25), new Vector3(0.45f, 0.5f, 0.45f), Cone);
                    b.Box(U(cx, cy, gh + 0.02), new Vector3(0.7f, 0.05f, 0.7f), Cone * 0.8f);
                }

                foreach (var (cx, cy, hdg) in BraidedRiver.Chairs(s))
                {
                    double gh = BraidedRiver.PlainHeightAt(cx, cy);
                    var rot = Quaternion.Euler(0, (float)hdg, 0);
                    b.Box(U(cx, cy, gh + 0.42), new Vector3(0.55f, 0.08f, 0.55f), rot, Chair);          // seat
                    b.Box(U(cx, cy, gh + 0.70), new Vector3(0.55f, 0.55f, 0.08f), rot, Chair * 1.1f);   // back
                    b.Box(U(cx, cy, gh + 0.20), new Vector3(0.45f, 0.40f, 0.45f), rot, Chair * 0.7f);   // legs
                }

                foreach (var (bx, by, hdg) in BraidedRiver.PicnicBenches(s))
                {
                    double gh = BraidedRiver.PlainHeightAt(bx, by);
                    var rot = Quaternion.Euler(0, (float)hdg, 0);
                    b.Box(U(bx, by, gh + 0.72), new Vector3(0.9f, 0.08f, 2.0f), rot, Bench * 1.1f);     // table top
                    b.Box(U(bx, by, gh + 0.35), new Vector3(0.7f, 0.70f, 0.12f), rot, Bench * 0.8f);    // legs
                    for (int k = -1; k <= 1; k += 2)
                        b.Box(U(bx, by, gh + 0.44) + rot * new Vector3(k * 0.85f, 0, 0),
                              new Vector3(0.35f, 0.07f, 2.0f), rot, Bench);                              // the seats
                }
            }
            b.Build("AlaskaStrips", parent, SeasideBuilder.VC);
        }
    }
}

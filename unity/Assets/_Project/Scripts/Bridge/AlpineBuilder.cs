using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// What the alpine range needs that a height field cannot carry: the timber. Owner 2026-10-16 asked for
    /// an obstacle at a lake's exit — "a lip of say 20–100 ft or tall trees" — and the trees are the harder
    /// of the two to read from the air, which is the point of them. Larch has a stand of 85 ft mountain
    /// hemlock across the mouth of its outlet valley; Crown, which has no valley, is ringed with it.
    ///
    /// The trunks are registered as solids in Core (<see cref="AlpineRange.RegisterSolids"/>), so this is the
    /// picture of something that is already there to hit.
    /// </summary>
    public static class AlpineBuilder
    {
        private static readonly Color Needle = new(0.17f, 0.27f, 0.20f);
        private static readonly Color NeedleLit = new(0.24f, 0.36f, 0.26f);
        private static readonly Color Trunk = new(0.30f, 0.24f, 0.19f);

        private static Vector3 U(double x, double y, double up) => new((float)y, (float)up, (float)x);

        public static void Build(WorldTerrain t, Transform parent)
        {
            var b = new SeasideBuilder.Batch();
            foreach (AlpineRange.Tree tr in AlpineRange.Trees())
            {
                double g = t.HeightAt(tr.X, tr.Y);
                var rot = Quaternion.Euler((float)tr.LeanDeg, (float)(tr.X * 7 % 360), 0);
                float r = (float)tr.RadiusM, h = (float)tr.HeightM;
                // A conifer at this altitude is a spire: a bare trunk for the first third, then three stacked
                // skirts of needles narrowing to a point. Three boxes, because there are two hundred of them.
                b.Box(U(tr.X, tr.Y, g + h * 0.30), new Vector3(r * 0.8f, h * 0.60f, r * 0.8f), rot, Trunk);
                b.Box(U(tr.X, tr.Y, g + h * 0.46), new Vector3(r * 4.2f, h * 0.34f, r * 4.2f), rot, Needle, NeedleLit);
                b.Box(U(tr.X, tr.Y, g + h * 0.70), new Vector3(r * 3.1f, h * 0.30f, r * 3.1f), rot, Needle, NeedleLit);
                b.Box(U(tr.X, tr.Y, g + h * 0.90), new Vector3(r * 1.7f, h * 0.26f, r * 1.7f), rot, Needle, NeedleLit);
            }
            b.Build("AlpineTimber", parent, SeasideBuilder.VC);
        }
    }
}

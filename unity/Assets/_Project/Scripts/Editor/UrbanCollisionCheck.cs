using System.Collections.Generic;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// What the cities' low-rise actually registered. The buildings are placed by the visual builder and
    /// registered as they are drawn, so this is the only place the two can be counted against each other.
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.UrbanCollisionCheck.Run
    /// </summary>
    public static class UrbanCollisionCheck
    {
        public static void Run()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                WorldBuilder.BuildAll();
                var t = WorldTerrain.Active;

                IReadOnlyList<UrbanLowRise.Block> blocks = UrbanLowRise.Blocks;
                Debug.Log($"URBAN {blocks.Count} low-rise buildings are solids, {UrbanLowRise.Refused} refused, "
                          + $"{UrbanLowRise.KeepClears.Count} keep-clear regions");
                double tallest = 0, widest = 0, area = 0;
                foreach (UrbanLowRise.Block b in blocks)
                {
                    double h = b.TopM - b.Base - UrbanLowRise.FootingM;
                    if (h > tallest) tallest = h;
                    if (2 * Mathf.Max((float)b.Hx, (float)b.Hy) > widest) widest = 2 * Mathf.Max((float)b.Hx, (float)b.Hy);
                    area += 4 * b.Hx * b.Hy;
                }
                Debug.Log($"URBAN tallest {tallest:F1} m to the ridge, widest {widest:F1} m, {area:F0} m2 of footprint");

                // Nothing on a pad, under a ring, or on a final.
                int onPad = 0;
                foreach (UrbanLowRise.Block b in blocks)
                    foreach (UrbanLowRise.KeepClear k in UrbanLowRise.KeepClears)
                        if (k.Touches(b.Cx, b.Cy, b.Hx, b.Hy))
                        { Debug.LogError($"URBAN a building at {b.Cx:F0},{b.Cy:F0} stands in '{k.Why}'"); onPad++; }
                Debug.Log($"URBAN {onPad} buildings standing where a pilot needs the ground");
                foreach (var kv in UrbanLowRise.RefusedBy)
                    Debug.Log($"URBAN refused by '{kv.Key}': {kv.Value}");

                // Fly a line through each city at roof height and count what it would hit.
                for (int p = 0; p < WorldTerrain.PlateauCount; p++)
                {
                    if (!WorldTerrain.Has(p, WorldTerrain.Feature.Town)) continue;
                    CityGrid city = ValleyCity.At(p);
                    var (cx, cy) = city.Centre;
                    double g = t.HeightAt(cx, cy);
                    int hits = 0;
                    for (double d = -1400; d <= 1400; d += 0.5)
                        if (UrbanLowRise.Penetration(cx + d, cy + 300, g + 4) is not null) hits++;
                    Debug.Log($"URBAN plateau {p} city at {cx:F0},{cy:F0}: a 2.8 km run at {g + 4:F0} m is inside a building for {hits * 0.5:F0} m");
                }
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}

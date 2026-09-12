using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Unity's OBJ importer ignores the .mtl map_Kd lines for these converted USDZ models, so this reads the
    /// .mtl beside each Resources/Models/*.obj and assigns every material's base-colour texture (found next to the
    /// .obj) — matched by a normalised material name.</summary>
    public sealed class ModelTextureAssigner : AssetPostprocessor
    {
        private static string Norm(string s) { var sb = new System.Text.StringBuilder(); foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c); return sb.ToString(); }

        private void OnPostprocessModel(GameObject g)
        {
            if (!assetPath.Contains("/Resources/Models/") || !assetPath.EndsWith(".obj")) return;
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string mtlPath = Path.ChangeExtension(assetPath, ".mtl");
            if (!File.Exists(mtlPath)) return;
            var texByMat = new Dictionary<string, string>();
            string cur = null;
            foreach (string raw in File.ReadAllLines(mtlPath))
            {
                string line = raw.Trim();
                if (line.StartsWith("newmtl ")) cur = Norm(line.Substring(7));
                else if (line.StartsWith("map_Kd ") && cur != null) texByMat[cur] = line.Substring(7).Trim();
            }
            int assigned = 0;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    if (!texByMat.TryGetValue(Norm(m.name), out string texFile)) continue;
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + texFile);
                    if (tex == null) continue;
                    m.mainTexture = tex;
                    m.color = Color.white;
                    assigned++;
                }
            }
            Debug.Log($"ModelTextureAssigner: {Path.GetFileName(assetPath)} — {assigned} material texture(s) assigned of {texByMat.Count} in the .mtl");
        }
    }
}

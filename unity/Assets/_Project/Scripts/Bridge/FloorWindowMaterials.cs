using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Name mapping at model placement only. Canopy/glTFast materials remain untouched.</summary>
    public static class FloorWindowMaterials
    {
        private static Material _lexan, _frame;
        private static bool _warned;
        public static Material Lexan
        {
            get
            {
                if (_lexan == null)
                {
                    // A shader looked up by name and not force-included is stripped from a player build, and
                    // new Material(null) throws - inside AirframeModels.Place, which would lose the whole model.
                    // Without it, keep glTFast's own (already alpha-blended) Lexan material and say so once.
                    Shader sh = Shader.Find("FlyingGame/Lexan");
                    if (sh == null)
                    {
                        if (!_warned) Debug.LogWarning("[Airframe] FlyingGame/Lexan not in this build: the floor windows keep the glTF material");
                        _warned = true;
                        return null;
                    }
                    _lexan = new Material(sh)
                    {
                        name = "Lexan", hideFlags = HideFlags.HideAndDontSave,
                        color = new Color(150f / 255, 160f / 255, 165f / 255, 0.18f)
                    };
                }
                return _lexan;
            }
        }
        public static void Apply(GameObject model)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    string name = materials[i].name;
                    // Unity can append the instance suffix to an imported material name.
                    if (name == "Lexan" || name == "Lexan (Instance)")
                    { Material lexan = Lexan; if (lexan != null) { materials[i] = lexan; changed = true; } }
                    else if (name == "Floor_Frame" || name == "Floor_Frame (Instance)")
                    {
                        if (_frame == null)
                        {
                            _frame = new Material(AirframeSurface.MaterialFor(SurfaceKind.Strut,
                                new Color(180f / 255, 184f / 255, 187f / 255)))
                                { name = "Floor_Frame", hideFlags = HideFlags.HideAndDontSave };
                            _frame.SetFloat("_Smoothness", 0.68f);
                            _frame.SetFloat("_SpecStrength", 1.1f);
                        }
                        materials[i] = _frame; changed = true;
                    }
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}

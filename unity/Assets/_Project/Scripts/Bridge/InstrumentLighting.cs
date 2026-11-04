using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Instruments that can be read in any light, on every platform: each instrument material of a placed model
    /// (<see cref="InstrumentFaces.IsInstrument"/>: dial faces, needles, tell-tales, compass cards, placard text) is
    /// redrawn with FlyingGame/Instrument — the same face art and colour, lit by the sun and sky, never below
    /// <see cref="InstrumentFaces.LightFloor"/>, markings glowing by <see cref="InstrumentFaces.MarkingGlow"/>. The
    /// physics of it is in Instrument.shader.
    ///
    /// ONE path for iOS/Mac and the Quest: AirframeBuilder.TryPlaceModel calls <see cref="Apply"/> on the placed
    /// model, which both builds use, and anything that makes dial materials of its own later (the Quest lane's
    /// CockpitLiveParts) passes them through <see cref="Lit"/>. The flat IMGUI dials (<see cref="AnalogGauges"/>)
    /// are screen overlays and never were lit by the scene.
    ///
    /// Why a shader of our own and not the glTF material's emission: glTFast's built-in shader compiles emission
    /// behind <c>shader_feature _EMISSION</c>, and no imported material enables it, so the variant is stripped from
    /// a player — switching the keyword on at runtime works in the editor and does nothing on the device. The shader
    /// here is referenced by Resources/Materials/Instrument.mat, which is what puts it into every build.
    /// </summary>
    public static class InstrumentLighting
    {
        private static Material _template;
        private static bool _failed;
        /// <summary>One lit copy per source material, kept for the session: every livery of a model shares its dial
        /// materials, and an aeroplane is rebuilt on every respawn.</summary>
        private static readonly Dictionary<Material, Material> Cache = new();

        private static Material Template
        {
            get
            {
                // Unity-null as well as never loaded: an asset unload (a new scene, Resources.UnloadUnusedAssets) destroys it.
                if (_template == null && !_failed)
                {
                    _template = Resources.Load<Material>("Materials/Instrument");
                    if (_template == null || _template.shader == null || !_template.shader.isSupported)
                    {
                        Debug.LogWarning("[Instruments] Resources/Materials/Instrument (FlyingGame/Instrument) missing or unsupported: instruments keep their glTF materials");
                        _template = null; _failed = true;
                    }
                }
                return _template;
            }
        }

        /// <summary>Redraw every instrument material under <paramref name="root"/>; returns how many renderer slots
        /// changed. Safe on a model with no instruments, and on one already done (a material already on the
        /// instrument shader is left as it is).</summary>
        public static int Apply(GameObject root)
        {
            if (root == null || Template == null) return 0;
            // Diagnostics (before/after renders): INSTRUMENT_LIGHT=off leaves the glTF materials as they import.
            if (System.Environment.GetEnvironmentVariable("INSTRUMENT_LIGHT") == "off") return 0;
            int changed = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material lit = Lit(mats[i]);
                    if (lit == null || lit == mats[i]) continue;
                    mats[i] = lit; any = true; changed++;
                }
                if (any) r.sharedMaterials = mats;
            }
            if (changed > 0) Debug.Log($"[Instruments] {root.name}: {changed} instrument material slots lit (floor {InstrumentFaces.LightFloor}, glow {InstrumentFaces.MarkingGlow})");
            return changed;
        }

        /// <summary>The instrument-lit copy of <paramref name="src"/>, or <paramref name="src"/> itself where it is not
        /// an instrument material (or cannot be carried over faithfully).</summary>
        public static Material Lit(Material src)
        {
            if (src == null || Template == null || src.shader == Template.shader || !InstrumentFaces.IsInstrument(src.name)) return src;
            // A cached copy can have been destroyed by an asset unload since (it is not an asset; nothing references it
            // between aeroplanes): Unity-null then, so make it again. Found by CockpitEyeShots, whose NewScene per shot
            // left every aeroplane after the first with its glTF dials and no word in the log.
            if (Cache.TryGetValue(src, out Material done) && done != null) return done;

            // glTFast's names (baseColorTexture/Factor, alphaCutoff); anything else through Unity's main texture/colour.
            string texProp = src.HasProperty("baseColorTexture") ? "baseColorTexture" : src.HasProperty("_MainTex") ? "_MainTex" : null;
            // A KHR_texture_transform rotation is not carried by the instrument shader: leave such a face alone.
            if (src.HasProperty("baseColorTexture_Rotation") && src.GetVector("baseColorTexture_Rotation").sqrMagnitude > 1e-8f
                && src.IsKeywordEnabled("_TEXTURE_TRANSFORM"))
            {
                Cache[src] = src;
                return src;
            }
            var m = new Material(Template) { name = src.name + " [lit]" };
            if (texProp != null)
            {
                m.mainTexture = src.GetTexture(texProp);
                m.mainTextureScale = src.GetTextureScale(texProp);
                m.mainTextureOffset = src.GetTextureOffset(texProp);
            }
            m.color = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_Color") ? src.color : Color.white;
            m.SetFloat("_Floor", InstrumentFaces.LightFloor);
            m.SetFloat("_Glow", InstrumentFaces.MarkingGlow);

            // Blend state: an alpha-blended source (a cut-out case PNG) stays blended, a MASK source keeps its cut.
            bool blend = src.IsKeywordEnabled("_ALPHABLEND_ON") || src.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || src.renderQueue >= 3000;
            if (blend)
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = src.renderQueue >= 3000 ? src.renderQueue : 3000;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else m.renderQueue = src.renderQueue;
            m.SetFloat("_Cutoff", src.IsKeywordEnabled("_ALPHATEST_ON") && src.HasProperty("alphaCutoff") ? src.GetFloat("alphaCutoff") : -1f);
            Cache[src] = m;
            return m;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// ASTC 4x4 on the phone and the headset, for every texture our aeroplanes wear.
    ///
    /// <para><b>Ty, 2026-11-10, choosing between four ways to get under App Store Connect's 4 GB:</b>
    /// <i>"Do it - 4x4, keep all liveries."</i> This is that decision, and it is the whole of it: one place
    /// says what format each platform gets, and it is derived from what the texture IS, not from a list.</para>
    ///
    /// <para><b>WHY THERE IS A SCRIPT HERE AT ALL.</b> Until 2026-11-10 every texture these models wear was
    /// embedded in its GLB, and a GLB is imported by glTFast's <c>ScriptedImporter</c> — so Unity's platform
    /// texture pipeline never saw it. There is no compression field in a <c>.glb.meta</c>, no per-platform
    /// override is possible, and the pixels shipped uncompressed at four bytes each with a full mip chain:
    /// 754.92 Mpix, 4,026 MB, which is 93.8 % of a 4.29 GB build that App Store Connect refused.
    /// <c>tools/models/glb_textures.py</c> moved those images out into ordinary texture assets under
    /// <c>Assets/_Project/ModelTextures/</c>, which is glTFast's own documented behaviour for an external
    /// texture (<c>GltfImport.LoadImageFromBytes</c>: <i>"Use the original texture at Editor (asset database)
    /// import"</i>). The moment they are ordinary texture assets, this — an <c>AssetPostprocessor</c>, the
    /// plainest thing in the editor — is all the mechanism that is needed.</para>
    ///
    /// <para><b>WHY THE RULE LIVES IN C# AND NOT IN THE .meta FILES.</b> The sidecars' metas are generated,
    /// and they carry what can be written as plainly true YAML: mip maps on, sRGB or linear, normal-map type,
    /// wrap and filter from the glTF sampler, 4096 maximum, no non-power-of-two rescale. They deliberately do
    /// NOT carry the per-platform format block, because that block is integers — <c>ASTC_4x4</c> is 48,
    /// <c>RGBA32</c> is 4 — and a wrong integer in a generated YAML file is a silent quality or size bug that
    /// nothing would catch. Here the compiler resolves the enum.</para>
    ///
    /// <para><b>WHAT EACH PLATFORM GETS, AND WHY.</b></para>
    /// <list type="bullet">
    /// <item><b>iOS → ASTC 4x4.</b> The decision. 8 bpp against 32.</item>
    /// <item><b>Android (the Quest) → ASTC 4x4.</b> The headset is a Snapdragon; ASTC is its native format and
    /// it was carrying the same uncompressed 4 GB of texture on 8 GB of shared memory. Parity rule 1 is one
    /// game: the phone and the headset get the same treatment of the same art.</item>
    /// <item><b>Mac → RGBA32, explicitly.</b> Not "Automatic", which would quietly become DXT/BC — a format
    /// change nobody asked for on the one platform that has no size problem. Written out, the Mac's pixels are
    /// the same bytes they were before any of this. If the Mac build ever needs to be smaller, BC7 is the
    /// obvious next line and it is one word.</item>
    /// <item><b>Normal maps → RGBA32 everywhere.</b> Measured, not assumed: encoding the fleet's five normal
    /// maps with Unity's own astcenc and decoding them back gives 47.1 dB on the P-51D's 4096² map but
    /// 25.2 dB on the pilot's 256² twill, with single-channel errors up to 51/255. Block compression shows up
    /// in a normal before it shows up anywhere else. There are five of them, keeping them exact costs 68 MB
    /// out of a predicted 682 MB of texture against a 4 GB limit, and spending that buys a whole class of
    /// artefact off the table. One line brings it back.</item>
    /// </list>
    ///
    /// <para><b>WHAT ASTC 4x4 DID TO THE PAINT</b>, measured over all 358 sidecars with the same astcenc the
    /// editor uses, at the same quality (50 / "Normal"): pixel-weighted mean 77.9 dB, 96 % of the pixels above
    /// 50 dB, 26 % of them bit-exact, and every alpha channel in the fleet — including the three hard-edged
    /// cut-outs — bit-exact. The Pitts' checkers come back with no error at all; the worst thing anywhere in
    /// Ty's own liveries is a 21/255 error on the anti-aliased edge of a registration letter at 70.3 dB.
    /// Visually lossless is normally taken as 40 dB. "Best" quality (100) was measured too: it buys about
    /// 0.1 dB and costs several times the import, so this stays at Unity's default 50.</para>
    ///
    /// <para><b>NOT DONE, ON PURPOSE.</b> ASTC 6x6 halves it again (~0.45 GB) and the 2026-11-05 size audit
    /// listed texture-format changes as not blanket-authorised; Ty authorised 4x4 specifically. 6x6 measured
    /// 29.7 dB against 4x4's 43.9 on a hard test pattern — that one WOULD show. Mip-map removal, maximum-size
    /// reduction and crunch compression are all available and none of them is asked for.</para>
    /// </summary>
    public sealed class ModelTextureFormat : AssetPostprocessor
    {
        /// <summary>Everything under here is a texture a GLB references by URI. Nothing else is touched.</summary>
        public const string TextureRoot = "Assets/_Project/ModelTextures/";

        // TargetName gives exactly the strings the .meta serialises ("iOS", "Android", "Standalone"), so the
        // settings this writes and the settings a human sees in the inspector are the same rows.
        private static readonly string IOS = UnityEditor.Build.NamedBuildTarget.iOS.TargetName;
        private static readonly string Android = UnityEditor.Build.NamedBuildTarget.Android.TargetName;
        private static readonly string Standalone = UnityEditor.Build.NamedBuildTarget.Standalone.TargetName;

        /// <summary>The largest texture in the fleet is 4096x3870; 4096 keeps every one of them at its
        /// authored size. Unity's own default is 2048, which would silently halve every livery.</summary>
        public const int MaxSize = 4096;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureRoot, System.StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;

            // The sidecar's own .meta already says sRGB/linear, normal-map or not, wrap, filter and mip maps —
            // those came out of the glTF and belong to the texture. Only size and format are decided here.
            importer.maxTextureSize = MaxSize;

            bool isNormal = importer.textureType == TextureImporterType.NormalMap;
            TextureImporterFormat mobile = isNormal
                ? TextureImporterFormat.RGBA32      // see the class comment: 25.2 dB on the pilot's twill
                : TextureImporterFormat.ASTC_4x4;   // Ty 2026-11-10: "Do it - 4x4, keep all liveries"

            Set(importer, IOS, mobile);
            Set(importer, Android, mobile);
            // The Mac keeps the pixels it already had. Explicit, so "Automatic" cannot turn into DXT later.
            Set(importer, Standalone, TextureImporterFormat.RGBA32);
        }

        private static void Set(TextureImporter importer, string platform, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.name = platform;
            settings.overridden = true;
            settings.maxTextureSize = MaxSize;
            settings.format = format;
            settings.compressionQuality = (int)TextureCompressionQuality.Normal;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }

        /// <summary>What this rule says for a given asset, so a test or a tool can ask without importing.
        /// Returns null for anything outside <see cref="TextureRoot"/>.</summary>
        public static IReadOnlyDictionary<string, TextureImporterFormat> FormatsFor(bool isNormalMap)
        {
            return new Dictionary<string, TextureImporterFormat>
            {
                [IOS] = isNormalMap ? TextureImporterFormat.RGBA32 : TextureImporterFormat.ASTC_4x4,
                [Android] = isNormalMap ? TextureImporterFormat.RGBA32 : TextureImporterFormat.ASTC_4x4,
                [Standalone] = TextureImporterFormat.RGBA32,
            };
        }
    }
}

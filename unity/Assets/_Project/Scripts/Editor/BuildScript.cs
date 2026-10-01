using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Reproducible iOS build (build-order step 5: get it onto a device). Configures the iOS player
    /// settings from code — bundle id, landscape+portrait orientation, IL2CPP, device family, min iOS — so the
    /// build is identical from the editor menu or headless CI, then generates an Xcode project the
    /// user opens/signs in Xcode to install on an iPhone/iPad.
    ///
    /// Headless:
    ///   Unity -batchmode -quit -projectPath unity \
    ///     -buildTarget iOS -executeMethod FlyingGame.EditorTools.BuildScript.BuildiOS
    /// The Xcode project lands in build/iOS (repo-relative). Signing/team is set in Xcode.
    /// </summary>
    public static class BuildScript
    {
        private const string BundleId = "com.tyrelfrisby.aeroplayground";   // App Store Connect app "Aero Playground"
        private const string ProductName = "Aero Playground";
        private const string IconPath = "Assets/_Project/Art/AppIcon.png";  // 1024² RGB (no alpha) — App Store Connect requires it
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string DevTeamId = "DH425V439F"; // Apple Development team (automatic signing)

        [MenuItem("FlyingGame/Configure iOS Player Settings")]
        public static void ConfigureiOS()
        {
            PlayerSettings.companyName = "FlyingGame";
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);

            // Landscape + portrait (owner 2026-09-08): in portrait ScreenLayout moves the touchpads into a
            // bottom tray and the 3D view/HUD sit above them. Upside-down stays off.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // iOS needs IL2CPP; ship both iPhone and iPad.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            // Engine code stripping decides native modules from asset usage; with no GUISkin/GUIStyle assets in
            // the scene it can drop the IMGUI native module, and then GUIUtility.GetDefaultSkin() returns null
            // (GUI.DoSetSkin NRE every OnGUI, nondeterministic per build). Keep engine code.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad; // ARM64 is the only iOS arch

            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = DevTeamId;

            EnsureSceneInBuild();
            EnsureAlwaysIncludedShaders();
            EnsureBuiltinFontPreloaded();
            EnsureAppIcon();
            EnsureEngineLoopsReadable();
            AssetDatabase.SaveAssets();
            Debug.Log($"iOS player settings configured: {BundleId} / \"{ProductName}\" (landscape+portrait, IL2CPP, ARM64).");
        }

        [MenuItem("FlyingGame/Build iOS (Xcode project)")]
        public static void BuildiOS()
        {
            ConfigureiOS();
            // Build stamp: version = build date/time (dotted numeric so iOS accepts it); the HUD shows it so a
            // device build can be told apart at a glance.
            string stamp = System.DateTime.Now.ToString("yyyy.MMdd.HHmm");
            PlayerSettings.bundleVersion = stamp;
            PlayerSettings.iOS.buildNumber = System.DateTime.Now.ToString("yyyyMMddHHmm");
            Debug.Log($"Build stamp {stamp}");

            // Default output is repo-relative build/iOS, but on this Mac ~/Documents is synced by Google
            // Drive, whose File Provider stamps com.apple.FinderInfo on the bundle directories and breaks
            // codesign. FLYINGGAME_IOS_OUT lets the build target a non-synced path (e.g. /private/tmp).
            string outDir = System.Environment.GetEnvironmentVariable("FLYINGGAME_IOS_OUT");
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/iOS"));
            }
            Directory.CreateDirectory(outDir);
            Debug.Log($"iOS Xcode project output dir: {outDir}");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"iOS build SUCCEEDED → {outDir} ({summary.totalSize / (1024 * 1024)} MB, " +
                          $"{summary.totalTime.TotalSeconds:F0}s). Open Unity-iPhone.xcodeproj in Xcode to sign & run.");
            }
            else
            {
                Debug.LogError($"iOS build FAILED: {summary.result}, {summary.totalErrors} error(s).");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        // Shaders referenced only by Shader.Find(name) at runtime are stripped from a player build
        // (they work in the editor, then return null on device — black screen). Force-include the ones
        // SceneBootstrap/BubbleField/SoaringScenery look up.
        private static void EnsureAlwaysIncludedShaders()
        {
            string[] names = { "Unlit/Color", "Unlit/Texture", "FlyingGame/Bubble", "FlyingGame/PlanarShadow", "FlyingGame/UnlitTransparent", "FlyingGame/Lit", "FlyingGame/HudLine", "FlyingGame/Terrain", "FlyingGame/Spray", "FlyingGame/Water", "FlyingGame/Waterfall", "FlyingGame/Glass", "GUI/3D Text Shader" };
            var so = new SerializedObject(UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings());
            SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (string name in names)
            {
                Shader sh = Shader.Find(name);
                if (sh == null)
                {
                    Debug.LogWarning($"EnsureAlwaysIncludedShaders: '{name}' not found in editor — skipped.");
                    continue;
                }

                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                {
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { present = true; break; }
                }
                if (!present)
                {
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                    Debug.Log($"EnsureAlwaysIncludedShaders: added '{name}'.");
                }
            }
            // Keep ALL instancing variants: the bubble field draws with Graphics.DrawMeshInstanced on a material made at
            // runtime, which the variant stripper cannot see — "Strip Unused" dropped INSTANCING_ON from the phone build
            // and the dense field rendered nothing (owner: bubbles missing on the phone, fine in the editor).
            SerializedProperty strip = so.FindProperty("m_InstancingStripping");
            if (strip != null && strip.intValue != 2) { strip.intValue = 2; Debug.Log("GraphicsSettings: instancing variants = Keep All"); }
            so.ApplyModifiedProperties();
        }

        // The built-in IMGUI default skin + its font get stripped from the iOS player (nondeterministically),
        // and then GUI.DoSetSkin NREs every frame and no GUI text renders. Pinning the built-in font as a
        // Preloaded Asset forces it into the build so the default skin's styles resolve at runtime.
        private static void EnsureBuiltinFontPreloaded()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
            {
                Debug.LogWarning("EnsureBuiltinFontPreloaded: built-in font not found.");
                return;
            }

            var preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).ToList();
            bool changed = false;
            if (!preloaded.Contains(font)) { preloaded.Add(font); changed = true; Debug.Log($"EnsureBuiltinFontPreloaded: pinned '{font.name}' into the build."); }
            // NOTE: pinning the built-in GUISkin (GameSkin/GameSkin.guiskin) as a preloaded asset does NOT help and
            // may be the 8488-byte "scripted object with a different serialization layout" seen at startup; only
            // the font is pinned. Drop any stale skin entry.
            preloaded.RemoveAll(a => a is GUISkin);
            changed = true;
            if (changed) PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        // The recorded-engine loops are read into float arrays at runtime (AudioClip.GetData), which needs them
        // decompressed on load; keep them PCM so the loop points stay sample-exact.
        private static void EnsureEngineLoopsReadable()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Resources/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter imp) continue;
                AudioImporterSampleSettings st = imp.defaultSampleSettings;
                if (st.loadType == AudioClipLoadType.DecompressOnLoad && st.compressionFormat == AudioCompressionFormat.PCM && imp.forceToMono) continue;
                st.loadType = AudioClipLoadType.DecompressOnLoad;
                st.compressionFormat = AudioCompressionFormat.PCM;
                st.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                imp.defaultSampleSettings = st;
                imp.forceToMono = true;
                imp.loadInBackground = false;
                imp.SaveAndReimport();
                Debug.Log($"EnsureEngineLoopsReadable: {path} → PCM, decompress on load.");
            }
        }

        // App Store Connect rejects an upload without the 1024×1024 marketing icon; Unity scales one source
        // texture to every iOS icon slot (app, spotlight, settings, notification, marketing).
        private static void EnsureAppIcon()
        {
            var imp = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (imp == null) { AssetDatabase.ImportAsset(IconPath); imp = AssetImporter.GetAtPath(IconPath) as TextureImporter; }
            if (imp == null) { Debug.LogWarning($"EnsureAppIcon: {IconPath} not found — no app icon."); return; }
            if (!imp.isReadable || imp.mipmapEnabled || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.alphaSource != TextureImporterAlphaSource.None)
            {
                imp.isReadable = true; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.alphaSource = TextureImporterAlphaSource.None; imp.npotScale = TextureImporterNPOTScale.None; imp.sRGBTexture = true;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (tex == null) { Debug.LogWarning("EnsureAppIcon: icon texture failed to load."); return; }
            int slots = 0;
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                foreach (PlatformIcon icon in icons) { icon.SetTexture(tex); slots++; }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
            }
            Debug.Log($"EnsureAppIcon: '{IconPath}' assigned to {slots} iOS icon slots.");
        }

        // Export compliance: the app only uses standard HTTPS/WSS (exempt), so declare it in Info.plist — otherwise
        // every TestFlight build waits on the "Missing Compliance" question in App Store Connect.
        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath)) return;
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            // Quick clips save videos to the photo library (add-only access).
            plist.root.SetString("NSPhotoLibraryAddUsageDescription", "Aero Playground saves the flight clips you capture to your photo library.");
            plist.WriteToFile(plistPath);
            Debug.Log("Info.plist: ITSAppUsesNonExemptEncryption = false");
            // MediaPlayer.framework for the volume-button trigger (Plugins/iOS/VolumeFireButton.mm).
            string projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            string fw = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(fw, "MediaPlayer.framework", false);
            // AVFoundation/CoreMedia/CoreVideo + Photos for the quick clips encoder (Plugins/iOS/ClipEncoder.mm).
            foreach (string f in new[] { "AVFoundation.framework", "CoreMedia.framework", "CoreVideo.framework", "Photos.framework" })
                proj.AddFrameworkToProject(fw, f, false);
            // The encoder guards its AVAssetWriter appends with @try (an append exception would otherwise abort the app);
            // Unity builds plugins with Objective-C exceptions off, so turn them on for that file.
            string enc = proj.FindFileGuidByProjectPath("Libraries/Plugins/iOS/ClipEncoder.mm");
            if (enc != null) proj.SetCompileFlagsForFile(fw, enc, new System.Collections.Generic.List<string> { "-fobjc-exceptions" });
            else Debug.LogWarning("Xcode: ClipEncoder.mm not found — clip encoder will not build");
            proj.WriteToFile(projPath);
            Debug.Log("Xcode: linked MediaPlayer, AVFoundation, CoreMedia, CoreVideo, Photos frameworks");
        }

        private static void EnsureSceneInBuild()
        {
            if (EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            {
                return;
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

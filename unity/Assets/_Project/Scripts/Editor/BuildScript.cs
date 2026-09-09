using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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
        // Placeholder reverse-DNS id — NOT branding; the app's real name is still TBD.
        private const string BundleId = "com.flyinggame.dev";
        private const string ProductName = "Flying Game";
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
            string[] names = { "Unlit/Color", "Unlit/Texture", "FlyingGame/Bubble", "FlyingGame/PlanarShadow", "FlyingGame/UnlitTransparent", "FlyingGame/Lit", "FlyingGame/HudLine", "FlyingGame/Terrain", "FlyingGame/Spray", "FlyingGame/Water", "FlyingGame/Waterfall", "FlyingGame/Glass" };
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

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
    /// settings from code — bundle id, landscape orientation, IL2CPP, device family, min iOS — so the
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

            // Landscape-only: the HUD and the dual touchpads assume a wide layout.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // iOS needs IL2CPP; ship both iPhone and iPad.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad; // ARM64 is the only iOS arch

            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = DevTeamId;

            EnsureSceneInBuild();
            EnsureAlwaysIncludedShaders();
            AssetDatabase.SaveAssets();
            Debug.Log($"iOS player settings configured: {BundleId} / \"{ProductName}\" (landscape, IL2CPP, ARM64).");
        }

        [MenuItem("FlyingGame/Build iOS (Xcode project)")]
        public static void BuildiOS()
        {
            ConfigureiOS();

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
            string[] names = { "Unlit/Color", "Unlit/Texture", "FlyingGame/BubbleInstanced" };
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

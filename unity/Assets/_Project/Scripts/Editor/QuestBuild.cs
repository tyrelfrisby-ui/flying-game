using System.IO;
using System.Linq;
using FlyingGame.Quest;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// META QUEST 3S BUILD: OpenXR on Android, and ONLY on Android.
    ///
    ///   Unity -batchmode -quit -projectPath unity -buildTarget Android \
    ///     -executeMethod FlyingGame.EditorTools.QuestBuild.BuildQuest        (tools/deploy/deploy-quest.sh runs this)
    ///
    /// What it sets, and why:
    ///  • XR Plug-in Management: the OpenXR loader for Android, initialised at startup. iOS and macOS get NO loader
    ///    (it removes one if it ever appears), so those builds start no XR at all and behave exactly as before.
    ///  • OpenXR for Android: Meta Quest Support (Quest 3S is a target device by default in OpenXR 1.16), the Oculus
    ///    Touch Controller Profile, the Hand Interaction Profile (pinch for the menu and recentre, without the XR
    ///    Hands package and the Input System backend it would need), and this project's display-rate feature.
    ///    Single-pass instanced = multiview on Quest. Legacy foveation, the only kind the built-in pipeline has.
    ///  • Android player: IL2CPP, ARM64 only, Vulkan only, minSdk 32, target "auto" (highest installed),
    ///    application id com.tyrelfrisby.aeroplayground.quest. All Android-only settings.
    ///  • Two SHARED settings are changed only for the duration of the build and put back afterwards, so iOS never
    ///    sees them: the default orientation (Meta's validation REFUSES to build unless it is Landscape Left; iOS
    ///    is AutoRotation and BuildScript.ConfigureiOS sets that itself anyway) and Frame Timing Stats (for the
    ///    [QuestPerf] log line). The bundleVersion build stamp is also put back, as deploy.sh does.
    ///  • A third, the ACTIVE INPUT HANDLER, goes from Old to Both for this build only, so the APK runs the Input
    ///    System's Android backend beside the old Input Manager: USB rudder pedals (RudderPedals, docs/RUDDER-PEDALS.md)
    ///    are read through it with their VID / PID and Android's own axis list. The controllers are untouched (they
    ///    are read through UnityEngine.XR.InputDevices whichever handler is active), and iOS / the Mac keep Old.
    ///    AERO_QUEST_OLD_INPUT=1 builds with Old as before; the pedals then fall back to the old Input Manager's
    ///    per-joystick axes (PJoy1A1 … PJoy4A12).
    /// </summary>
    public static class QuestBuild
    {
        public const string AppId = "com.tyrelfrisby.aeroplayground.quest";
        private const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("FlyingGame/Quest: Configure XR (Android only)")]
        public static void ConfigureXr()
        {
            // ---- XR Plug-in Management ----
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
                if (perTarget == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(XrSettingsPath)!);
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, XrSettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            if (!android.AssignedSettings.activeLoaders.Any(l => l is OpenXRLoader))
                XRPackageMetadataStore.AssignLoader(android.AssignedSettings, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android);

            // iOS and macOS: no XR, ever. A loader appearing there (someone ticking a box in Project Settings) would
            // start an XR runtime on a phone or a Mac — remove it rather than trust nobody does.
            foreach (BuildTargetGroup g in new[] { BuildTargetGroup.iOS, BuildTargetGroup.Standalone })
            {
                XRGeneralSettings s = perTarget.SettingsForBuildTarget(g);
                if (s == null || s.AssignedSettings == null) continue;
                foreach (var l in s.AssignedSettings.activeLoaders.ToList())
                    XRPackageMetadataStore.RemoveLoader(s.AssignedSettings, l.GetType().FullName, g);
                s.InitManagerOnStart = false;
                EditorUtility.SetDirty(s);
            }

            // ---- OpenXR features, Android ----
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            OpenXRSettings oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            // XrPerformanceSettingsFeature: Unity's own XR_EXT_performance_settings, for the sustained-high CPU/GPU hint
            // and the runtime's thermal notifications (QuestPerformanceHints, docs/quest-performance.md).
            var wanted = new System.Type[] { typeof(MetaQuestFeature), typeof(OculusTouchControllerProfile), typeof(HandInteractionProfile), typeof(QuestDisplayRateFeature),
                                             typeof(UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings.XrPerformanceSettingsFeature) };
            foreach (var f in oxr.GetFeatures())
            {
                bool on = wanted.Contains(f.GetType());
                if (f.enabled != on) { f.enabled = on; EditorUtility.SetDirty(f); }
            }
            foreach (var t in wanted)
                if (oxr.GetFeature(t) == null) Debug.LogError($"[QuestBuild] OpenXR feature {t.Name} is not available for Android");
            EditorUtility.SetDirty(oxr);
            EditorUtility.SetDirty(android);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBuild] XR configured: OpenXR loader on Android only; features " +
                      string.Join(", ", oxr.GetFeatures().Where(f => f.enabled).Select(f => f.GetType().Name)) + "; render mode " + oxr.renderMode);
        }

        /// <summary>The Android-only player settings. Persisted (they touch nothing iOS reads).</summary>
        public static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;        // the Quest store floor
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;  // highest installed
            // Vulkan only: OpenXR's multiview, fixed foveation (XR_FB_foveation) and Meta's symmetric-projection /
            // buffer-discard optimisations are Vulkan features on Quest; GLES3 would be a fallback nobody tests.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.buildAppBundle = false;
        }

        [MenuItem("FlyingGame/Quest: Build APK")]
        public static void BuildQuest()
        {
            ConfigSync.SyncAndRequireComplete();
            ConfigureXr();
            ConfigureAndroid();

            // Shared settings, changed only for this build and restored in `finally`.
            UIOrientation orientWas = PlayerSettings.defaultInterfaceOrientation;
            bool frameStatsWas = PlayerSettings.enableFrameTimingStats;
            string versionWas = PlayerSettings.bundleVersion;
            int codeWas = PlayerSettings.Android.bundleVersionCode;
            int inputWas = InputHandler();
            bool ok = false;
            try
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;   // Meta Quest: Landscape Left only
                PlayerSettings.enableFrameTimingStats = true;                                 // the [QuestPerf] cpu numbers
                if (System.Environment.GetEnvironmentVariable("AERO_QUEST_OLD_INPUT") != "1") SetInputHandler(2);   // Both: USB pedals
                Debug.Log($"[QuestBuild] active input handler {InputHandler()} (0 Old, 2 Both; was {inputWas})");
                string stamp = System.DateTime.Now.ToString("yyyy.MMdd.HHmm");
                PlayerSettings.bundleVersion = stamp;   // shown on the HUD in the flat game; in the log here
                Debug.Log($"[QuestBuild] build stamp {stamp}");
                BuildScript.EnsureSceneInBuildForTools();

                string outDir = System.Environment.GetEnvironmentVariable("FLYINGGAME_QUEST_OUT");
                if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/quest"));
                Directory.CreateDirectory(outDir);
                string apk = Path.Combine(outDir, "AeroPlayground.apk");
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BuildScript.MainScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                });
                ok = report.summary.result == BuildResult.Succeeded;
                if (ok) Debug.Log($"[QuestBuild] Quest build SUCCEEDED → {apk} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:F0}s)");
                else Debug.LogError($"[QuestBuild] Quest build FAILED: {report.summary.result}, {report.summary.totalErrors} error(s).");
            }
            finally
            {
                PlayerSettings.defaultInterfaceOrientation = orientWas;
                PlayerSettings.enableFrameTimingStats = frameStatsWas;
                PlayerSettings.bundleVersion = versionWas;
                PlayerSettings.Android.bundleVersionCode = codeWas;
                if (InputHandler() != inputWas) SetInputHandler(inputWas);
                AssetDatabase.SaveAssets();
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        // ProjectSettings' activeInputHandler (0 Old, 1 Input System, 2 Both) has no PlayerSettings API; the Input
        // System package itself sets it through the serialized PlayerSettings (EditorPlayerSettingHelpers).
        private static SerializedProperty InputHandlerProperty()
        {
            UnityEngine.Object ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            return ps == null ? null : new SerializedObject(ps).FindProperty("activeInputHandler");
        }

        private static int InputHandler() => InputHandlerProperty()?.intValue ?? 0;

        private static void SetInputHandler(int v)
        {
            SerializedProperty p = InputHandlerProperty();
            if (p == null) { Debug.LogWarning("[QuestBuild] activeInputHandler not found: building with the handler as it is"); return; }
            p.intValue = v;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Hand tracking on Quest needs two manifest lines the OpenXR package does not write (Unity's XR Hands package
    /// would, but it also needs the Input System backend this project does not use). Optional, so the app still
    /// installs and runs with controllers only.
    /// </summary>
#if UNITY_ANDROID
    public sealed class QuestManifest : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            string xml = File.ReadAllText(manifest);
            string add = "";
            if (!xml.Contains("com.oculus.permission.HAND_TRACKING"))
                add += "\n  <uses-permission android:name=\"com.oculus.permission.HAND_TRACKING\" />";
            if (!xml.Contains("oculus.software.handtracking"))
                add += "\n  <uses-feature android:name=\"oculus.software.handtracking\" android:required=\"false\" />";
            // USB rudder pedals: a HID game controller reaches the app through Android's input stack and needs no
            // permission; this only says the app uses a USB host port, and required="false" filters no device out.
            if (!xml.Contains("android.hardware.usb.host"))
                add += "\n  <uses-feature android:name=\"android.hardware.usb.host\" android:required=\"false\" />";
            if (add.Length == 0) return;
            int at = xml.IndexOf("<application", System.StringComparison.Ordinal);
            if (at < 0) return;
            File.WriteAllText(manifest, xml.Insert(at, add.TrimStart('\n') + "\n  "));
            Debug.Log("[QuestBuild] manifest: hand tracking permission + optional features (hand tracking, USB host) added");
        }
    }
#endif
}

using FlyingGame.Bridge;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace FlyingGame.Quest
{
    /// <summary>
    /// Tells the Quest what kind of load this is, and listens for it saying the headset is struggling, through
    /// Unity's own XR_EXT_performance_settings feature (<see cref="XrPerformanceSettingsFeature"/>, switched on for
    /// Android by QuestBuild.ConfigureXr).
    ///
    /// SUSTAINED HIGH for both CPU and GPU: a flight sim at 200 Hz plus a world to the horizon is "high or dynamic
    /// complexity" in the extension's words, so the runtime is asked to prioritise frame rate within the thermal
    /// limits rather than save power. (Boost is for bursts of under 30 s; it is never asked for.)
    ///
    /// The runtime's notifications — CPU or GPU, compositing / rendering / THERMAL, normal → warning → impaired —
    /// go to the log as [QuestPerf] lines and onto the periodic [QuestPerf] line, so a capture says whether a slow
    /// patch was the headset throttling itself or the game asking too much.
    /// </summary>
    public static class QuestPerformanceHints
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            XrHeadset.PerformanceHinter = Hint;
            XrPerformanceSettingsFeature.OnXrPerformanceChangeNotification -= OnChange;
            XrPerformanceSettingsFeature.OnXrPerformanceChangeNotification += OnChange;
        }

        private static bool Hint()
        {
            bool cpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Cpu, PerformanceLevelHint.SustainedHigh);
            bool gpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Gpu, PerformanceLevelHint.SustainedHigh);
            Debug.Log($"[Quest] performance level hint sustained-high: CPU {(cpu ? "accepted" : "NOT accepted")}, GPU {(gpu ? "accepted" : "NOT accepted")}" +
                      (cpu || gpu ? "" : " (XR_EXT_performance_settings missing: is the feature enabled for Android?)"));
            return cpu && gpu;
        }

        private static void OnChange(PerformanceChangeNotification n)
        {
            string s = $"{n.domain} {n.subDomain} {n.fromLevel}->{n.toLevel}";
            XrHeadset.PerformanceNotice = s;
            Debug.Log($"[QuestPerf] runtime: {s}");
        }
    }
}

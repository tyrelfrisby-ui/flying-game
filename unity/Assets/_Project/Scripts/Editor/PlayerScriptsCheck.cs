using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Compiles the PLAYER scripts for other platforms without switching this project's build target or building a
    /// player: the cheap proof that code under <c>#if UNITY_ANDROID</c> / <c>UNITY_STANDALONE_OSX</c> still compiles
    /// (Ty 2026-11-04, Q11: "run one Android (Quest) batch compile from this lane"). The Editor compile and the test
    /// suite never see those branches.
    ///
    ///   FG_COMPILE_TARGETS=Android,StandaloneOSX Unity -batchmode -nographics -projectPath unity \
    ///     -executeMethod FlyingGame.EditorTools.PlayerScriptsCheck.Run -logFile build/compile.log
    ///
    /// Logs "[PlayerScriptsCheck] &lt;target&gt;: COMPILED" or "FAILED" per target, and exits 1 if any failed.
    /// </summary>
    public static class PlayerScriptsCheck
    {
        public static void Run()
        {
            string targets = Environment.GetEnvironmentVariable("FG_COMPILE_TARGETS");
            if (string.IsNullOrWhiteSpace(targets)) targets = "Android";
            int failed = 0;
            foreach (string name in targets.Split(','))
            {
                var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), name.Trim());
                var settings = new ScriptCompilationSettings { target = target, group = BuildPipeline.GetBuildTargetGroup(target), options = ScriptCompilationOptions.None };
                string outDir = Path.Combine(Path.GetTempPath(), "fg-player-scripts-" + target);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, outDir);
                bool ok = result.assemblies != null && result.assemblies.Contains("FlyingGame.Bridge.dll") && result.assemblies.Contains("FlyingGame.Core.dll");
                Debug.Log($"[PlayerScriptsCheck] {target}: {(ok ? "COMPILED" : "FAILED")} ({result.assemblies?.Count ?? 0} assemblies)");
                if (!ok) failed++;
            }
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Mirrors the repo's configs/aircraft/*.json (single source of truth, shared with the headless
    /// flight tests) into StreamingAssets so runtime loads the exact same data on desktop and iOS.
    /// Runs on editor load; also available as a menu item.
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigSync
    {
        static ConfigSync() => Sync();

        [MenuItem("FlyingGame/Sync Aircraft Configs")]
        public static void Sync()
        {
            string repoConfigs = Path.GetFullPath(Path.Combine(Application.dataPath, "../../configs/aircraft"));
            string dest = Path.Combine(Application.streamingAssetsPath, "aircraft");
            if (!Directory.Exists(repoConfigs))
            {
                Debug.LogWarning($"ConfigSync: {repoConfigs} not found; skipping.");
                return;
            }

            Directory.CreateDirectory(dest);
            bool changed = false;
            foreach (string src in Directory.GetFiles(repoConfigs, "*.json"))
            {
                string target = Path.Combine(dest, Path.GetFileName(src));
                if (!File.Exists(target) || File.ReadAllText(src) != File.ReadAllText(target))
                {
                    File.Copy(src, target, overwrite: true);
                    changed = true;
                }
            }

            // Mirror challenge definitions too.
            string repoChallenges = Path.GetFullPath(Path.Combine(Application.dataPath, "../../configs/challenges"));
            string destChallenges = Path.Combine(Application.streamingAssetsPath, "challenges");
            if (Directory.Exists(repoChallenges))
            {
                Directory.CreateDirectory(destChallenges);
                foreach (string src in Directory.GetFiles(repoChallenges, "*.json"))
                {
                    string target = Path.Combine(destChallenges, Path.GetFileName(src));
                    if (!File.Exists(target) || File.ReadAllText(src) != File.ReadAllText(target))
                    {
                        File.Copy(src, target, overwrite: true);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("ConfigSync: aircraft + challenge configs mirrored to StreamingAssets.");
            }
        }
    }
}

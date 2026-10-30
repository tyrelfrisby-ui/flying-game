using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// StreamingAssets is GENERATED, not committed. The repo keeps exactly one copy of every config —
    /// configs/** and docs/CREDITS.md — and this script writes the player's copy from it.
    ///
    /// It used to be the other way round: both copies were committed, the aircraft mirror was gitignored-but-tracked,
    /// and a new aircraft only reached the player if somebody remembered `git add -f`. great-lakes-like shipped with no
    /// runtime config at all because nobody did (2026-10-30). Nothing under StreamingAssets is tracked any more, so
    /// there is nothing left to forget; <see cref="Verify"/> is the build's gate instead, and <see cref="Sync"/>
    /// deletes a mirror whose source is gone, so a renamed config cannot linger in the player forever.
    ///
    /// Runs on editor load, before a player build, and from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigSync
    {
        /// <summary>One mirror: a repo path, where it lands under StreamingAssets, and whether it is a whole directory.</summary>
        private readonly struct Mirror
        {
            public readonly string Source;      // repo-relative
            public readonly string Dest;        // StreamingAssets-relative
            public readonly string Glob;        // null for a single file

            public Mirror(string source, string dest, string glob = null) { Source = source; Dest = dest; Glob = glob; }
        }

        /// <summary>
        /// Everything the player reads out of StreamingAssets and the repo file it comes from. A directory mirror is
        /// swept: a file in the destination with no source is deleted. StreamingAssets/commerce.json is NOT here —
        /// it has no repo source, it is the original, and it stays committed.
        /// </summary>
        private static readonly Mirror[] Mirrors =
        {
            new("configs/aircraft", "aircraft", "*.json"),
            new("configs/challenges", "challenges", "*.json"),
            new("configs/atmosphere", "atmosphere", "*.json"),
            new("configs/glide-table.json", "glide-table.json"),
            new("configs/progression.json", "progression.json"),
            new("docs/CREDITS.md", "credits.md"),
        };

        /// <summary>
        /// FLYINGGAME_NO_CONFIG_SYNC=1 holds the editor's load-time sync back, so <see cref="CheckMirrors"/> can be
        /// asked what a CHECKOUT ships rather than what a sync would ship. That is the question the old setup could
        /// never answer, and the one whose wrong answer stranded the Great Lakes.
        /// </summary>
        private static bool SyncSuppressed =>
            System.Environment.GetEnvironmentVariable("FLYINGGAME_NO_CONFIG_SYNC") == "1";

        static ConfigSync()
        {
            if (SyncSuppressed) Debug.Log("ConfigSync: load-time sync suppressed by FLYINGGAME_NO_CONFIG_SYNC=1.");
            else Sync();
        }

        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        private static string RepoPath(string rel) => Path.Combine(Repo, rel.Replace('/', Path.DirectorySeparatorChar));
        private static string DestPath(string rel) => Path.Combine(Application.streamingAssetsPath, rel);

        [MenuItem("FlyingGame/Sync Aircraft Configs")]
        public static void Sync()
        {
            bool changed = false;
            foreach (Mirror m in Mirrors)
            {
                changed |= m.Glob == null ? SyncFile(m) : SyncDirectory(m);
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("ConfigSync: StreamingAssets regenerated from configs/ and docs/CREDITS.md.");
            }
        }

        private static bool SyncFile(Mirror m)
        {
            string src = RepoPath(m.Source), dst = DestPath(m.Dest);
            if (!File.Exists(src)) { Debug.LogWarning($"ConfigSync: {m.Source} not found; skipping."); return false; }
            if (File.Exists(dst) && File.ReadAllText(src) == File.ReadAllText(dst)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, overwrite: true);
            return true;
        }

        private static bool SyncDirectory(Mirror m)
        {
            string src = RepoPath(m.Source), dst = DestPath(m.Dest);
            if (!Directory.Exists(src)) { Debug.LogWarning($"ConfigSync: {m.Source} not found; skipping."); return false; }

            Directory.CreateDirectory(dst);
            bool changed = false;
            var wanted = new HashSet<string>();
            foreach (string file in Directory.GetFiles(src, m.Glob))
            {
                string name = Path.GetFileName(file);
                wanted.Add(name);
                string target = Path.Combine(dst, name);
                if (File.Exists(target) && File.ReadAllText(file) == File.ReadAllText(target)) continue;
                File.Copy(file, target, overwrite: true);
                changed = true;
            }

            // Sweep: a mirror whose source is gone. Without this a renamed config left its old copy in the player
            // for good, and the game went on offering an aircraft the repo no longer describes.
            foreach (string stale in Directory.GetFiles(dst, m.Glob))
            {
                if (wanted.Contains(Path.GetFileName(stale))) continue;
                File.Delete(stale);
                if (File.Exists(stale + ".meta")) File.Delete(stale + ".meta");
                Debug.Log($"ConfigSync: deleted stale mirror {m.Dest}/{Path.GetFileName(stale)} (no {m.Source} source).");
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Every source has a byte-identical copy in StreamingAssets, and no mirrored directory holds a file the repo
        /// does not describe. Returns the complaints; empty means the player will load exactly what the tests loaded.
        /// </summary>
        public static List<string> Verify()
        {
            var bad = new List<string>();
            foreach (Mirror m in Mirrors)
            {
                string src = RepoPath(m.Source), dst = DestPath(m.Dest);
                if (m.Glob == null)
                {
                    if (!File.Exists(src)) continue;
                    if (!File.Exists(dst)) bad.Add($"{m.Dest} missing (from {m.Source})");
                    else if (File.ReadAllText(src) != File.ReadAllText(dst)) bad.Add($"{m.Dest} differs from {m.Source}");
                    continue;
                }

                if (!Directory.Exists(src)) continue;
                if (!Directory.Exists(dst)) { bad.Add($"{m.Dest}/ missing entirely (from {m.Source}/)"); continue; }

                var sources = new HashSet<string>(Directory.GetFiles(src, m.Glob).Select(Path.GetFileName));
                foreach (string name in sources)
                {
                    string target = Path.Combine(dst, name);
                    if (!File.Exists(target)) bad.Add($"{m.Dest}/{name} missing (from {m.Source}/{name})");
                    else if (File.ReadAllText(Path.Combine(src, name)) != File.ReadAllText(target))
                        bad.Add($"{m.Dest}/{name} differs from {m.Source}/{name}");
                }

                foreach (string stale in Directory.GetFiles(dst, m.Glob))
                {
                    if (!sources.Contains(Path.GetFileName(stale))) bad.Add($"{m.Dest}/{Path.GetFileName(stale)} has no source in {m.Source}/");
                }
            }

            return bad;
        }

        /// <summary>
        /// Sync, then refuse to go on if the mirror still does not match. A player build calls this first: a build that
        /// cannot carry every config is a build that would strand an aircraft at the menu, and it should fail here
        /// rather than ship.
        /// </summary>
        public static void SyncAndRequireComplete()
        {
            Sync();
            List<string> bad = Verify();
            if (bad.Count == 0)
            {
                Debug.Log($"ConfigSync: StreamingAssets complete ({Mirrors.Length} mirrors verified).");
                return;
            }

            Debug.LogError("ConfigSync: StreamingAssets does not match the repo, so the build would ship the wrong configs:\n  "
                           + string.Join("\n  ", bad));
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new MirrorIncompleteException("ConfigSync: StreamingAssets does not match the repo; see the console.");
        }

        /// <summary>
        /// Report on the mirror WITHOUT writing it, exit 1 if it is wrong. The headless way to ask whether a checkout
        /// would ship the right configs:
        ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.ConfigSync.CheckMirrors
        /// Note the editor's own InitializeOnLoad sync runs before this, which is the point: it says whether a sync
        /// that just ran actually produced a complete mirror.
        /// </summary>
        public static void CheckMirrors()
        {
            List<string> bad = Verify();
            if (bad.Count == 0)
            {
                Debug.Log($"ConfigSync: StreamingAssets complete ({Mirrors.Length} mirrors verified).");
                return;
            }

            Debug.LogError("ConfigSync: StreamingAssets does not match the repo:\n  " + string.Join("\n  ", bad));
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Thrown in the interactive editor, where Exit(1) would just close Unity on the user.</summary>
        public sealed class MirrorIncompleteException : System.Exception
        {
            public MirrorIncompleteException(string message) : base(message) { }
        }
    }
}

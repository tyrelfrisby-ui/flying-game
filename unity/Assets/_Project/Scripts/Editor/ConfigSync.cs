using System;
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
            public readonly string Glob;        // which source files; null for a single file
            public readonly string DestGlob;    // which destination files the sweep owns; defaults to Glob

            /// <summary>
            /// Given a source file's text, the destination file name. null means "same name as the source", which is
            /// almost always right. The model rigs are the exception: a rig table DECLARES which model it belongs to
            /// in its own "model" field, and that is what names the file the game looks for. A fixed source→dest name
            /// table would hardcode a copy of something the file already says, and a rebuilt rig that changed its
            /// model name would then mirror to the old path and go on looking correct — which is the shape of bug
            /// this whole change exists to remove. So the name is read, not configured.
            /// </summary>
            public readonly Func<string, string> NameFromContent;

            public Mirror(string source, string dest, string glob = null, string destGlob = null,
                          Func<string, string> nameFromContent = null)
            {
                Source = source; Dest = dest; Glob = glob;
                DestGlob = destGlob ?? glob;
                NameFromContent = nameFromContent;
            }
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
            new("configs/vr-cardboard.json", "vr-cardboard.json"),   // the Cardboard viewer profile (VR)
            new("docs/CREDITS.md", "credits.md"),
            // The model rigs: every moving part of a model we drew ourselves, written beside the GLB by that model's
            // Blender build script. Matches nothing on this branch yet — the rig tables arrive with
            // lane/claude-extra-cockpit and lane/claude-sgs233-rig — and is declared here so those lanes only have to
            // stop committing their mirror, not teach ConfigSync about it again.
            new("docs/models", "rigs", "*-rig.json", destGlob: "*.json", nameFromContent: RigModelName),
        };

        /// <summary>A rig table's destination name: the model it says it belongs to. null skips the file.</summary>
        private static string RigModelName(string text)
        {
            try
            {
                string model = (string)Newtonsoft.Json.Linq.JObject.Parse(text)["model"];
                return string.IsNullOrEmpty(model) ? null : model + ".json";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ConfigSync: a *-rig.json is not a rig table ({e.Message}); skipped.");
                return null;
            }
        }

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

            // Mirror the models' RIG tables (docs/models/*-rig.json: every moving part of a model we drew
            // ourselves — surfaces, cockpit controls, instrument needles — written by that model's Blender
            // build script alongside the GLB). The destination is keyed by the "model" field INSIDE each
            // file, so a rebuilt rig arrives with nothing re-typed here and nothing renamed there.
            string repoRigs = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/models"));
            string destRigs = Path.Combine(Application.streamingAssetsPath, "rigs");
            if (Directory.Exists(repoRigs))
            {
                Directory.CreateDirectory(destRigs);
                foreach (string src in Directory.GetFiles(repoRigs, "*-rig.json"))
                {
                    string text = File.ReadAllText(src);
                    string model;
                    try { model = FlyingGame.Bridge.AirframeRig.Parse(text).Model; }
                    catch (System.Exception e) { Debug.LogWarning($"ConfigSync: {Path.GetFileName(src)} is not a rig table ({e.Message})."); continue; }
                    if (string.IsNullOrEmpty(model)) { Debug.LogWarning($"ConfigSync: {Path.GetFileName(src)} names no model; skipped."); continue; }
                    string target = Path.Combine(destRigs, model + ".json");
                    if (!File.Exists(target) || File.ReadAllText(target) != text)
                    {
                        File.WriteAllText(target, text);
                        FlyingGame.Bridge.AirframeRig.Forget();
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("ConfigSync: StreamingAssets regenerated from configs/ and docs/CREDITS.md.");
            }
        }

        /// <summary>Where one source file lands: what its content says, or its own name.</summary>
        private static string DestNameFor(Mirror m, string sourcePath, string text) =>
            m.NameFromContent == null ? Path.GetFileName(sourcePath) : m.NameFromContent(text);

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
                string text = File.ReadAllText(file);
                string name = DestNameFor(m, file, text);
                if (name == null) continue;          // the resolver rejected it; it has already said why
                wanted.Add(name);
                string target = Path.Combine(dst, name);
                if (File.Exists(target) && File.ReadAllText(target) == text) continue;
                File.WriteAllText(target, text);
                changed = true;
            }

            // Sweep: a mirror whose source is gone. Without this a renamed config left its old copy in the player
            // for good, and the game went on offering an aircraft the repo no longer describes.
            foreach (string stale in Directory.GetFiles(dst, m.DestGlob))
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

                var sources = new HashSet<string>();
                foreach (string file in Directory.GetFiles(src, m.Glob))
                {
                    string text = File.ReadAllText(file);
                    string name = DestNameFor(m, file, text);
                    if (name == null) { bad.Add($"{m.Source}/{Path.GetFileName(file)} does not say where it belongs"); continue; }
                    sources.Add(name);
                    string target = Path.Combine(dst, name);
                    if (!File.Exists(target)) bad.Add($"{m.Dest}/{name} missing (from {m.Source}/{Path.GetFileName(file)})");
                    else if (File.ReadAllText(target) != text)
                        bad.Add($"{m.Dest}/{name} differs from {m.Source}/{Path.GetFileName(file)}");
                }

                foreach (string stale in Directory.GetFiles(dst, m.DestGlob))
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

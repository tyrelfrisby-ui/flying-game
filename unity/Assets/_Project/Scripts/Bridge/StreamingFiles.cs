using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE ONE WAY to read a file out of StreamingAssets. On iOS and in the editor that folder is a folder, and
    /// <see cref="File.ReadAllText(string)"/> works. On Android it is not: <see cref="Application.streamingAssetsPath"/>
    /// is <c>jar:file:///…/base.apk!/assets</c>, the files are entries inside the APK, and every
    /// <see cref="File"/> call on that path throws or says "not there". The first Quest build read its aircraft
    /// config that way, so the aeroplane never spawned: it sat at the world origin with no sim, inside the island,
    /// and the headset showed a hillside with nothing to fly (Ty, 2026-11-02).
    ///
    /// Inside an APK this reads through <see cref="UnityWebRequest"/>, blocking: the files are small JSON and every
    /// caller wants the answer now. Paths are relative to StreamingAssets ("aircraft/c172-like.json").
    /// </summary>
    public static class StreamingFiles
    {
        /// <summary>The full path or URL of a StreamingAssets file — for messages; read it with <see cref="ReadAllText"/>.</summary>
        public static string PathOf(string relative) => Path.Combine(Application.streamingAssetsPath, relative);

        private static bool InsideArchive => Application.streamingAssetsPath.Contains("://");

        /// <summary>The file's text. Throws <see cref="FileNotFoundException"/> if it is not there, like File.ReadAllText.</summary>
        public static string ReadAllText(string relative)
        {
            if (TryReadAllText(relative, out string text, out string error)) return text;
            throw new FileNotFoundException($"StreamingAssets/{relative}: {error}", PathOf(relative));
        }

        /// <summary>False (with the reason) when the file is missing or unreadable.</summary>
        public static bool TryReadAllText(string relative, out string text, out string error)
        {
            string path = PathOf(relative);
            text = null; error = null;
            if (!InsideArchive)
            {
                if (!File.Exists(path)) { error = "not found"; return false; }
                text = File.ReadAllText(path);
                return true;
            }
            using UnityWebRequest req = UnityWebRequest.Get(path);
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!op.isDone && clock.ElapsedMilliseconds < 10000) System.Threading.Thread.Sleep(1);   // a local APK entry: milliseconds
            if (!op.isDone) { req.Abort(); error = "timed out reading inside the APK"; return false; }
            if (req.result != UnityWebRequest.Result.Success) { error = req.error ?? "unreadable"; return false; }
            text = req.downloadHandler.text;
            return true;
        }

        public static bool Exists(string relative) => TryReadAllText(relative, out _, out _);
    }
}

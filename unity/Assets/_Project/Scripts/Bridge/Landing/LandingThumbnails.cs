using System.Collections.Generic;
using System.IO;
using FlyingGame.Core.Landing;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE THUMBNAIL PIPELINE (docs/LANDING-V2.md, Ty 2026-11-05: "locations with icons showing them with that actual
    /// airplane ie a screenshot of what they'll first see"). Pictures are named by <see cref="ThumbnailKeys"/> and come
    /// from, first found wins:
    ///  1. this device's own captures, persistentDataPath/landing-thumbs/&lt;key&gt;.png — written by
    ///     <see cref="LandingPreview"/> whenever the page shows a combination with no picture yet (the aeroplane from
    ///     the preview camera, the location from the main camera at the launch pose), and again from the first frame
    ///     after LAUNCH, which is literally what the pilot first sees;
    ///  2. the bench captures shipped in Resources/LandingThumbs/&lt;key&gt;.png (AERO_SELFTEST=landing-thumbs walks the
    ///     fleet through this same code; tools/landing-thumbs.sh copies them in).
    /// A miss is remembered, so a card with no picture costs one dictionary lookup a frame, not a disk probe.
    /// </summary>
    public static class LandingThumbnails
    {
        public const int Width = 384, Height = 216;
        public const string ResourcesFolder = "LandingThumbs";

        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static readonly HashSet<string> _missing = new();

        public static string Dir => Path.Combine(Application.persistentDataPath, "landing-thumbs");
        public static string PathFor(string key) => Path.Combine(Dir, key.Replace('/', Path.DirectorySeparatorChar) + ".png");

        public static Texture2D Get(PickerItem item) => item == null ? null : Get(item.Thumbnails);

        public static Texture2D Get(IEnumerable<string> keys)
        {
            if (keys == null) return null;
            foreach (string k in keys) { var t = Get(k); if (t != null) return t; }
            return null;
        }

        public static bool Has(string key) => Get(key) != null;

        /// <summary>Was this key captured on THIS device (not only bundled)?</summary>
        public static bool Captured(string key) => File.Exists(PathFor(key));

        public static Texture2D Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_cache.TryGetValue(key, out var hit)) return hit;
            if (_missing.Contains(key)) return null;
            Texture2D tex = null;
            string p = PathFor(key);
            if (File.Exists(p))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = key, wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(p))) { Object.Destroy(tex); tex = null; }
            }
            if (tex == null) tex = Resources.Load<Texture2D>(ResourcesFolder + "/" + key);
            if (tex == null) { _missing.Add(key); return null; }
            _cache[key] = tex;
            return tex;
        }

        /// <summary>Render <paramref name="cam"/> once into a thumbnail-sized texture (its own target is untouched).</summary>
        public static Texture2D Render(Camera cam, int w = Width, int h = Height)
        {
            if (cam == null) return null;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture, prevActive = RenderTexture.active;
            float prevAspect = cam.aspect;
            try
            {
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                // A camera that is not the main one must not sample the main camera's planar reflection or its scene
                // depth: both are read in screen space, so the aircraft card — which is the PREVIEW camera's picture —
                // would be captured with the ghost aeroplane in the water that WaterReflection.RenderWithoutMirror
                // explains, and a captured card is then on disk for good. The launch-view card IS the main camera, and
                // keeps its own true reflection.
                if (cam == Camera.main) cam.Render();
                else WaterReflection.RenderWithoutMirror(cam);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                cam.targetTexture = prevTarget; cam.aspect = prevAspect; cam.ResetAspect();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Capture <paramref name="cam"/> as <paramref name="key"/>: written to disk and used at once.</summary>
        public static bool Capture(string key, Camera cam)
        {
            var tex = Render(cam);
            if (tex == null) return false;
            tex.name = key;
            try
            {
                string p = PathFor(key);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, tex.EncodeToPNG());
            }
            catch (System.Exception e) { Debug.LogWarning($"[Landing] thumbnail {key} not saved: {e.Message}"); }
            if (_cache.TryGetValue(key, out var old) && old != null && old != tex && !IsResource(old)) Object.Destroy(old);
            _cache[key] = tex;
            _missing.Remove(key);
            Debug.Log($"[Landing] captured {key}");
            return true;
        }

        private static bool IsResource(Texture2D t) => t != null && !t.isReadable;

        /// <summary>Forget every miss (a capture elsewhere may have filled them).</summary>
        public static void ForgetMisses() => _missing.Clear();
    }
}

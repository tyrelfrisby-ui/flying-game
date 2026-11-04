using System.Collections;
using FlyingGame.Bridge;
using FlyingGame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlyingGame.Tests
{
    /// <summary>
    /// TV MODE's pipeline, end to end, with a simulated 1920 × 1080 TV (docs/TV-COMPANION-PLAN.md §7.2). The Editor has
    /// one display, so the presenter draws into <see cref="TvOutput.SimDisplay"/> instead of display 1; everything else is
    /// the code the device runs. A -batchmode Editor has no Game view, so the TV's cameras are also rendered by hand
    /// (<see cref="TvOutput.RenderFrameForTest"/>): the frames still go through the same ping-pong as on the device.
    /// </summary>
    public sealed class TvOutputPlayTests
    {
        private static readonly Color Sky = new Color(0f, 1f, 0f, 1f), Box = new Color(1f, 0f, 0f, 1f);
        private Camera _cam;
        private GameObject _box;
        private bool _hadSetting; private int _setting;

        [SetUp]
        public void Build()
        {
            _hadSetting = PlayerPrefs.HasKey("tv.setting"); _setting = PlayerPrefs.GetInt("tv.setting", 0);
            TvOutput.Setting = TvSetting.GameOnTv;
            _cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Sky;
            _cam.transform.position = Vector3.zero;
            _cam.rect = new Rect(0f, 0.3f, 1f, 0.7f);   // a portrait tray's viewport: TV mode must take the whole picture
            // Entering Play mode runs SceneBootstrap, which builds the game and its own main camera: step it aside so
            // Camera.main is this one and the picture is the known box on the known sky.
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c != _cam && c.CompareTag("MainCamera")) { c.tag = "Untagged"; c.enabled = false; }
            _box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _box.transform.position = new Vector3(0f, 0f, 3f);
            var mat = new Material(Shader.Find("Unlit/Color")) { color = Box };
            _box.GetComponent<Renderer>().sharedMaterial = mat;
        }

        [TearDown]
        public void Clear()
        {
            TvOutput.DrawInTvPassForTest = null;
            TvOutput.ShutdownForTest();
            if (_box != null) Object.Destroy(_box);
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_hadSetting) PlayerPrefs.SetInt("tv.setting", _setting); else PlayerPrefs.DeleteKey("tv.setting");
            PlayerPrefs.Save();
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { yield return null; TvOutput.RenderFrameForTest(); }
        }

        private static Color Pixel(RenderTexture rt, int x, int y)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color c = tex.GetPixel(0, 0);
            Object.Destroy(tex);
            return c;
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.That(Mathf.Abs(expected.r - actual.r) < 0.1f && Mathf.Abs(expected.g - actual.g) < 0.1f && Mathf.Abs(expected.b - actual.b) < 0.1f,
                $"{what}: expected {expected}, got {actual}");
        }

        [UnityTest]
        public IEnumerator ATvTakesTheWorldAtItsOwnShapeAndTheCameraComesBackAfter()
        {
            TvOutput.SimulateForTest = true;
            TvOutput.EnsureRunnerForTest();
            yield return Frames(4);

            Assert.AreEqual(TvMode.GameOnTv, TvOutput.Mode);
            Assert.IsTrue(TvOutput.Active);
            Assert.AreEqual(1920, TvOutput.Width);
            Assert.AreEqual(1080, TvOutput.Height);
            Assert.IsNotNull(_cam.targetTexture, "the main camera renders the TV's picture");
            Assert.AreEqual(1920, _cam.targetTexture.width);
            Assert.AreEqual(1080, _cam.targetTexture.height);
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), _cam.rect, "the whole TV, not the phone's portrait viewport");
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), ScreenLayout.CameraViewport);
            Assert.IsFalse(ScreenLayout.HasAircraftKeepOut, "the phone does not dodge an aircraft it is not showing");
            Assert.IsNotNull(GameObject.Find("TvPhoneBackdrop"), "something clears the phone");

            // The world reached the "TV": the box in the middle, the sky in a corner.
            RenderTexture tv = TvOutput.SimDisplay;
            Assert.IsNotNull(tv);
            AssertColor(Box, Pixel(tv, 960, 540), "TV centre");
            AssertColor(Sky, Pixel(tv, 20, 20), "TV corner");

            // TV unplugged: everything goes back as it was.
            TvOutput.SimulateForTest = false;
            yield return null;
            yield return null;
            Assert.AreEqual(TvMode.Handheld, TvOutput.Mode);
            Assert.IsFalse(TvOutput.Active);
            Assert.IsNull(_cam.targetTexture, "the main camera draws the phone again");
            Assert.IsNull(TvOutput.SimDisplay);
            Assert.IsNull(GameObject.Find("TvPresenter"));
            Assert.IsNull(GameObject.Find("TvPhoneBackdrop"));
        }

        [UnityTest]
        public IEnumerator MirrorLeavesTheCameraAlone()
        {
            TvOutput.Setting = TvSetting.Mirror;
            TvOutput.SimulateForTest = true;
            TvOutput.EnsureRunnerForTest();
            yield return Frames(3);
            Assert.AreEqual(TvMode.Mirror, TvOutput.Mode);
            Assert.IsFalse(TvOutput.Active);
            Assert.IsNull(_cam.targetTexture);
            Assert.IsNull(GameObject.Find("TvPresenter"));

            // Switching to GAME ON TV while connected takes the TV; back to MIRROR gives it back, mid-flight.
            TvOutput.Setting = TvSetting.GameOnTv;
            yield return Frames(2);
            Assert.IsTrue(TvOutput.Active);
            Assert.IsNotNull(_cam.targetTexture);
            TvOutput.Setting = TvSetting.Mirror;
            yield return Frames(2);
            Assert.AreEqual(TvMode.Mirror, TvOutput.Mode);
            Assert.IsNull(_cam.targetTexture);
        }

        [UnityTest]
        public IEnumerator ConnectingAndDisconnectingLeaksNoTextures()
        {
            TvOutput.EnsureRunnerForTest();
            yield return null;
            int before = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
            for (int i = 0; i < 5; i++)
            {
                TvOutput.SimulateForTest = true;
                yield return Frames(2);
                Assert.IsTrue(TvOutput.Active);
                TvOutput.SimulateForTest = false;
                yield return null;
                yield return null;
                Assert.IsFalse(TvOutput.Active);
            }
            yield return null;   // Destroy() lands at the end of the frame
            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
        }

        [UnityTest]
        public IEnumerator WhatTheTvPassDrawsLandsInTheTvPictureAtTvPixels()
        {
            // A white square at TV pixels (100, 100)–(300, 300), top-left origin, drawn by the same pass as the dials.
            TvOutput.DrawInTvPassForTest = () =>
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(100f, 100f, 200f, 200f), Texture2D.whiteTexture);
            };
            TvOutput.SimulateForTest = true;
            TvOutput.EnsureRunnerForTest();
            int passes = TvOutput.TvPassesForTest;
            for (int i = 0; i < 30 && TvOutput.TvPassesForTest < passes + 2; i++) yield return Frames(1);
            if (TvOutput.TvPassesForTest < passes + 2)
                Assert.Ignore("no IMGUI Repaint pass ran (-batchmode Editor has no Game view); check on a device or in an interactive Editor");
            yield return Frames(2);
            RenderTexture tv = TvOutput.SimDisplay;
            // Texture y is bottom-up: GUI y 200 (the square's middle) is texture row 1080 − 200 = 880.
            AssertColor(Color.white, Pixel(tv, 200, 880), "inside the square");
            AssertColor(Box, Pixel(tv, 960, 540), "the world is still under it");
        }
    }
}

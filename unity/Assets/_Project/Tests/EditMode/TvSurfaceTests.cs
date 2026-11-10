using FlyingGame.Bridge;
using NUnit.Framework;
using UnityEngine;

namespace FlyingGame.Tests
{
    /// <summary>
    /// TV MODE's layout surface (docs/TV-COMPANION-PLAN.md §1.2). Inside a TV bracket the HUD lays itself out for the TV;
    /// outside it nothing differs from handheld play. The TV numbers are worked by hand for 1920 × 1080:
    /// S = 1080, Margin = 0.018 S = 19.44, ButtonH = 0.062 S = 66.96, title-safe = 2.5 % of each edge (48 and 27).
    /// </summary>
    public sealed class TvSurfaceTests
    {
        [TearDown]
        public void Unwind()
        {
            // A failed assertion inside a bracket must not leave the next test laying out for a TV.
            while (UiSurface.IsTv) UiSurface.End();
            // (UiSurface.End at depth 0 also clears the panel flag)
        }

        [Test]
        public void OutsideATvPassItIsTheScreen()
        {
            Assert.IsFalse(UiSurface.IsTv);
            Assert.AreEqual(Screen.width, UiSurface.Width);
            Assert.AreEqual(Screen.height, UiSurface.Height);
            Assert.AreEqual(Mathf.Min(Screen.width, Screen.height), UiLayout.S);
        }

        [Test]
        public void ABracketLaysOutForTheTvAndEndsCleanly()
        {
            UiSurface.Begin(1920, 1080);
            Assert.IsTrue(UiSurface.IsTv);
            Assert.AreEqual(1920, UiSurface.Width);
            Assert.AreEqual(1080, UiSurface.Height);
            UiSurface.End();
            Assert.IsFalse(UiSurface.IsTv);
            Assert.AreEqual(Screen.width, UiSurface.Width);
            UiSurface.End();   // an extra End is harmless
            Assert.IsFalse(UiSurface.IsTv);
        }

        [Test]
        public void TheToolbarAndBandAreTheTvsInsideItsTitleSafeMargin()
        {
            UiSurface.Begin(1920, 1080);
            Assert.AreEqual(1080f, UiLayout.S, 1e-3f);
            Assert.AreEqual(27f, UiLayout.SafeTop, 1e-3f);
            Assert.AreEqual(27f, UiLayout.SafeBottom, 1e-3f);
            Assert.AreEqual(48f, UiLayout.SafeLeft, 1e-3f);
            Assert.AreEqual(48f, UiLayout.SafeRight, 1e-3f);
            // No pads on the TV: the band is the whole width inside the margins, whatever the phone's pads published.
            Assert.AreEqual(48f + 19.44f, UiLayout.BandLeft, 1e-3f);
            Assert.AreEqual(1920f - 48f - 19.44f, UiLayout.BandRight, 1e-3f);
            Assert.AreEqual(27f + 19.44f + 66.96f, UiLayout.ToolbarBottom, 1e-3f);
            Assert.AreEqual(1920f - 48f - 19.44f, UiLayout.OptionsRect.xMax, 1e-3f);
        }

        [Test]
        public void NothingOnTheTvIsATouchStickAndATvIsLandscape()
        {
            UiSurface.Begin(1920, 1080);
            Assert.IsTrue(TouchFlightControls.TouchSticksHidden, "the TV's dials take the full-width row");
            Assert.IsTrue(ScreenLayout.FullScreenView);
            Assert.IsFalse(ScreenLayout.Portrait);
            Assert.AreEqual(0f, ScreenLayout.TrayHeightPx);
            UiSurface.End();
            UiSurface.Begin(1080, 1920);   // a TV on its side still lays out by its own shape
            Assert.IsTrue(ScreenLayout.Portrait);
        }

        [Test]
        public void WithNoTvTheCameraViewportIsUnchanged()
        {
            Assert.IsFalse(TvOutput.Active);
            var (x, y, w, h) = FlyingGame.Core.ChaseFraming.CameraViewport(ScreenLayout.TrayHeightPx, Screen.height, ScreenLayout.FullScreenView);
            Assert.AreEqual(new Rect((float)x, (float)y, (float)w, (float)h), ScreenLayout.CameraViewport);
        }

        [Test]
        public void TheSettingDefaultsToGameOnTvAndIsSaved()
        {
            bool had = PlayerPrefs.HasKey("tv.setting");
            int was = PlayerPrefs.GetInt("tv.setting", 0);
            try
            {
                PlayerPrefs.DeleteKey("tv.setting");
                Assert.AreEqual(FlyingGame.Core.TvSetting.GameOnTv, TvOutput.Setting);
                TvOutput.Setting = FlyingGame.Core.TvSetting.Mirror;
                Assert.AreEqual(1, PlayerPrefs.GetInt("tv.setting", 0));
                Assert.AreEqual(FlyingGame.Core.TvSetting.Mirror, TvOutput.Setting);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt("tv.setting", was); else PlayerPrefs.DeleteKey("tv.setting");
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void TheEditorNeverDrivesARealTv() => Assert.IsFalse(TvOutput.Supported);

        // ---- Ty's answers 2026-11-04 (Q2, Q7, Q8) ----

        [Test]
        public void TheEditorIsNoTvDeviceAndHasNoMonitorMode()
        {
            Assert.IsFalse(TvOutput.MonitorSupported);
            Assert.AreEqual(FlyingGame.Core.TvDevice.Other, TvOutput.Device);
            Assert.IsFalse(TvOutput.InstrumentsElsewhere);
            Assert.IsFalse(TvOutput.ExternalDisplay, "the radio has no TV to keep off in the Editor");
            Assert.AreEqual("", VoiceComms.RouteNote);
        }

        [Test]
        public void APanelBracketIsATvPassThatSaysItIsThePanel()
        {
            UiSurface.Begin(1206, 1206, panel: true);
            Assert.IsTrue(UiSurface.IsTv);
            Assert.IsTrue(UiSurface.IsPanel);
            Assert.IsTrue(TouchFlightControls.TouchSticksHidden, "the panel uses the full-width pad sizes");
            // 1206 square: title-safe 2.5 % = 30.15, Margin 0.018 S = 21.708, ButtonH 0.062 S = 74.772.
            Assert.AreEqual(30.15f, UiLayout.SafeLeft, 1e-3f);
            Assert.AreEqual(30.15f + 21.708f + 74.772f, UiLayout.ToolbarBottom, 1e-3f);
            // The flight row AnalogGauges lays out (left = SafeLeft + Margin, right = W − SafeRight − Margin, width 820,
            // centred) starts where Core MonitorPanel's block says it does, so the block that gets scaled is the dials.
            var block = FlyingGame.Core.MonitorPanel.Block(1206, UiLayout.ToolbarBottom, UiLayout.Gap, engine: true);
            float left = UiLayout.SafeLeft + UiLayout.Margin, right = UiSurface.Width - UiLayout.SafeRight - UiLayout.Margin;
            Assert.AreEqual((left + right - 820f) * 0.5f, (float)block.X, 1e-3f);
            UiSurface.End();
            Assert.IsFalse(UiSurface.IsPanel);
            UiSurface.Begin(1920, 1080);
            Assert.IsFalse(UiSurface.IsPanel, "a TV pass is not the panel");
        }

        [Test]
        public void TheMonitorSettingDefaultsToOffAndIsSaved()
        {
            bool had = PlayerPrefs.HasKey("tv.monitor");
            int was = PlayerPrefs.GetInt("tv.monitor", 0);
            try
            {
                PlayerPrefs.DeleteKey("tv.monitor");
                Assert.AreEqual(FlyingGame.Core.MonitorSetting.Off, TvOutput.Monitor);
                TvOutput.Monitor = FlyingGame.Core.MonitorSetting.Instruments;
                Assert.AreEqual(1, PlayerPrefs.GetInt("tv.monitor", 0));
                Assert.AreEqual(FlyingGame.Core.MonitorSetting.Instruments, TvOutput.Monitor);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt("tv.monitor", was); else PlayerPrefs.DeleteKey("tv.monitor");
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void NoCompanionLinkRunsInTheEditor()
        {
            Assert.IsFalse(FlyingGame.Bridge.Companion.CompanionLink.IsHost);
            Assert.IsFalse(FlyingGame.Bridge.Companion.CompanionLink.IsClient);
        }
    }
}

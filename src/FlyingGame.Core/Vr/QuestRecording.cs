using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// The engine-free half of the Quest's "what I see" recorder (Bridge QuestRecorder + Plugins/Android
    /// QuestRecorder.java), pinned by QuestRecordingTests.
    ///
    /// Owner Ty, 2026-11-02: a controller shortcut that starts and stops a recording without going through the Meta
    /// menu. The recording is the app's own: a mono camera at the head renders <see cref="Width"/> x
    /// <see cref="Height"/> at <see cref="Fps"/>, a shader packs it to I420 on the GPU, and Android's MediaCodec /
    /// MediaMuxer write H.264 + AAC to an .mp4 in the app's Movies folder. See docs/quest-controls.md for why not
    /// MediaProjection or the Meta capture.
    /// </summary>
    public static class QuestRecording
    {
        /// <summary>16:9, both divisible by 4 (the I420 packing puts two chroma rows in one luma-wide row).</summary>
        public const int Width = 1280, Height = 720;
        public const int Fps = 30;
        public const int VideoBitrate = 8_000_000, AudioBitrate = 128_000;
        /// <summary>Vertical field of view of the recording camera. 60° tall at 16:9 is about 91° wide, close to
        /// what one eye of a Quest 3S sees without the lens's distorted rim.</summary>
        public const float VerticalFovDeg = 60f;
        /// <summary>A recording left running stops by itself after this long (8 Mbit/s is about 1 MB a second).</summary>
        public const double MaxSeconds = 30 * 60;

        /// <summary>The file name for a recording started at <paramref name="local"/> (the headset's local time).</summary>
        public static string FileName(DateTime local) => $"AeroQuest-{local:yyyyMMdd-HHmmss}.mp4";

        /// <summary>"0:07", "12:34", "1:02:03" — the indicator's clock.</summary>
        public static string Clock(double seconds)
        {
            int s = seconds <= 0 ? 0 : (int)Math.Floor(seconds);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        /// <summary>
        /// The I420 frame the GPU writes and the encoder reads: the luma plane (w x h), then U (w/2 x h/2), then V,
        /// each tightly packed, top row first. Packed into an 8-bit texture <paramref name="w"/> wide and
        /// <see cref="I420Rows"/> tall.
        /// </summary>
        public static (int yBytes, int uOffset, int vOffset, int total) I420Layout(int w, int h)
        {
            if (w <= 0 || h <= 0 || w % 4 != 0 || h % 4 != 0)
                throw new ArgumentException($"I420 packing needs both sides divisible by 4, got {w}x{h}");
            int y = w * h, c = y / 4;
            return (y, y, y + c, y + 2 * c);
        }

        /// <summary>Rows of the packed I420 texture: h of luma, h/4 of U, h/4 of V.</summary>
        public static int I420Rows(int h) => h + h / 2;

        /// <summary>
        /// Frame pacing at <see cref="Fps"/> from a 72 / 90 Hz loop, the same rule as ClipRecorder: capture when
        /// the clock reaches <paramref name="next"/>, then step it by one frame period — or, after a stall of a
        /// quarter second or more (a pause, a load), restart from now rather than catch up with a burst.
        /// </summary>
        public static bool Due(double now, ref double next, int fps = Fps)
        {
            if (now < next) return false;
            next = now - next > 0.25 ? now + 1.0 / fps : next + 1.0 / fps;
            return true;
        }
    }

    /// <summary>
    /// THE RECORD SHORTCUT: A and B held together for <see cref="HoldSec"/>. Fires once, then not again until one of
    /// them is let go.
    ///
    /// Every single button on the Touch controllers already flies something (QuestControlMapper), and a hold on a
    /// button that acts on its PRESS would do that thing first: a held right stick click would cycle the gear (or
    /// release the tow) before it started a recording, and the left stick click is recentre plus, held, the menu.
    /// A and B are the one pair whose press does nothing when both are down: they are trim nose up and trim nose
    /// down, and held together the two rates cancel, so the trim does not move while the chord is held. Both sit
    /// under the right thumb, and nobody presses both by accident for a whole second.
    /// </summary>
    public sealed class QuestRecordChord
    {
        public const double HoldSec = 1.0;
        private double _held;
        private bool _fired;

        /// <summary>One frame. True on the frame the chord has been held <see cref="HoldSec"/>.</summary>
        public bool Step(bool a, bool b, double dt)
        {
            if (!(a && b)) { _held = 0; _fired = false; return false; }
            _held += dt;
            if (_fired || _held < HoldSec) return false;
            _fired = true;
            return true;
        }
    }
}

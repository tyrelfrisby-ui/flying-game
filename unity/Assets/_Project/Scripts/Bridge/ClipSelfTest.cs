using System.Collections;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// On-device check of replay + quick clips (added only when the app is launched with AERO_SELFTEST set):
    /// fly, bail out, save a live clip WITH the instruments, replay the bail-out, save a replay clip WITHOUT them, take
    /// screenshots of the real screen along the way. Clips are also copied to Documents/Clips and screenshots land in
    /// Documents, so they can be pulled off the phone with devicectl. Logs "[SelfTest]" lines.
    /// </summary>
    public sealed class ClipSelfTest : MonoBehaviour
    {
        public StartMenu Menu;
        public FlightReplay Replay;
        public PilotEgress Egress;
        public ChaseCamera Chase;

        private IEnumerator Start()
        {
            ClipRecorder.KeepCopies(true);
            bool keepInst = ClipRecorder.IncludeInstruments; int keepSec = ClipRecorder.ClipSeconds;
            ClipRecorder.IncludeInstruments = true;   // the setting applies to footage recorded from now on
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log("[SelfTest] fly");
            Menu.Fly();
            yield return new WaitForSecondsRealtime(4f);
            ScreenCapture.CaptureScreenshot("selftest-live.png");
            yield return new WaitForSecondsRealtime(4f);
            Debug.Log("[SelfTest] bail out");
            Egress.BailOut();
            yield return new WaitForSecondsRealtime(14f);
            ScreenCapture.CaptureScreenshot("selftest-chute.png");
            ClipRecorder.ClipSeconds = 15;
            Debug.Log("[SelfTest] clip live (instruments)");
            ClipRecorder.SaveClip();
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log("[SelfTest] replay");
            Replay.Enter();
            Replay.Seek(Replay.Recorder.StartTime);
            ClipRecorder.IncludeInstruments = false;   // the replay footage is recorded clean
            Chase.SetView(ChaseCamera.View.Flyby);
            yield return new WaitForSecondsRealtime(5f);
            Chase.SetView(ChaseCamera.View.RelativeWind);
            yield return new WaitForSecondsRealtime(12f);
            ScreenCapture.CaptureScreenshot("selftest-replay.png");
            yield return new WaitForSecondsRealtime(5f);
            ClipRecorder.ClipSeconds = 30;
            Debug.Log("[SelfTest] clip replay (no instruments)");
            ClipRecorder.SaveClip();
            yield return new WaitForSecondsRealtime(4f);
            Replay.Exit();
            ClipRecorder.IncludeInstruments = keepInst; ClipRecorder.ClipSeconds = keepSec;
            yield return new WaitForSecondsRealtime(6f);
            Debug.Log($"[SelfTest] DONE fps={1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):F0}");
        }

        private float _worst;
        private void Update()
        {
            if (Time.unscaledDeltaTime > _worst && Time.frameCount > 200) { _worst = Time.unscaledDeltaTime; }
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"[SelfTest] frame {Time.unscaledDeltaTime * 1000f:F1} ms, worst {_worst * 1000f:F1} ms, audio peak {ClipRecorder.AudioPeak:F4} blocks {ClipRecorder.AudioBlocks} vol {FlightAudio.MasterVolume:F2} rate {AudioSettings.outputSampleRate}");
                ClipRecorder.AudioPeak = 0f;
            }
        }
    }
}

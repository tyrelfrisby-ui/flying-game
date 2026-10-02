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
        public CombatController Combat;

        private IEnumerator Start()
        {
            string mode = System.Environment.GetEnvironmentVariable("AERO_SELFTEST");
            if (mode == "combat") { yield return CombatTest(); yield break; }
            if (mode == "thermal") { yield return ThermalTest(); yield break; }
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
            yield return new WaitForSecondsRealtime(6f);
            Debug.Log("[SelfTest] radio test transmission");
            VoiceComms.InjectTestTransmission();   // 2 s warbling 700 Hz tone on a receiver: must be in the live clip
            yield return new WaitForSecondsRealtime(8f);
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

        /// <summary>AERO_SELFTEST=combat: a P-51 in the combat zone, cockpit view, the P-51 formation provoked; logs the
        /// formations, the combat line and frame times; screenshots of the cockpit and of the attack.</summary>
        private IEnumerator CombatTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "p51d-like";
            SessionSettings.ChallengeId = "event:combat";
            Debug.Log("[SelfTest] combat fly");
            Menu.Fly();
            yield return new WaitForSecondsRealtime(3f);
            Chase.SetView(ChaseCamera.View.Cockpit);
            yield return new WaitForSecondsRealtime(3f);
            ScreenCapture.CaptureScreenshot("selftest-cockpit.png");
            Chase.SetView(ChaseCamera.View.RelativeWind);
            Combat.DebugProvoke();
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForSecondsRealtime(5f);
                Debug.Log($"[SelfTest] {Combat.DebugFlights()} | {Combat.Line}");
                if (i == 6) ScreenCapture.CaptureScreenshot("selftest-combat.png");
            }
            Debug.Log("[SelfTest] DONE combat");
        }

        /// <summary>AERO_SELFTEST=thermal: the 2-33 started in a thermal, hands off; logs height and bank every 5 s.</summary>
        private IEnumerator ThermalTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "glider-2-33-like";
            SessionSettings.StartMode = SessionSettings.Start.InThermal;
            Menu.Fly();
            var drv = Menu.Driver;
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSecondsRealtime(5f);
                var q = drv.Sim.Aircraft.State.Attitude;
                double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3;
                Debug.Log($"[SelfTest] thermal t={5 * (i + 1)} s alt {drv.AltitudeM:F0} m bank {roll:F0} IAS {drv.IasMs:F1}");
                if (i == 1) ScreenCapture.CaptureScreenshot("selftest-thermal.png");
            }
            Debug.Log("[SelfTest] DONE thermal");
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

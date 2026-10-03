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
            if (mode == "dogfight") { yield return DogfightTest(); yield break; }
            if (mode == "side2d") { yield return Side2DTest(); yield break; }
            if (mode == "menu") { yield return MenuLayoutTest(); yield break; }
            if (mode == "ui") { yield return UiLayoutTest(); yield break; }
            if (mode == "perf") { yield return PerfTest(); yield break; }
            if (mode == "clouds") { yield return CloudTest(); yield break; }
            if (mode == "seaside") { yield return SeasideTest(); yield break; }
            if (mode == "playground") { yield return PlaygroundTest(); yield break; }
            if (mode == "lesson") { yield return LessonTest(); yield break; }
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

        /// <summary>AERO_SELFTEST=perf (owner 2026-10-03: "the mac version is very slow"): fly the 172 and measure the frame
        /// time in phases — everything on; rolling clip capture off; air bubbles off; both off — 12 s each.</summary>
        private IEnumerator PerfTest()
        {
            SessionSettings.ChallengeId = null; SessionSettings.AircraftId = "c172-like";
            yield return new WaitForSecondsRealtime(2f);
            Menu.Fly();
            yield return new WaitForSecondsRealtime(5f);
            Debug.Log($"[Perf] screen {Screen.width}x{Screen.height} dpi {Screen.dpi:F0} quality {QualitySettings.GetQualityLevel()} ({QualitySettings.names[QualitySettings.GetQualityLevel()]}) vSync {QualitySettings.vSyncCount} targetFps {Application.targetFrameRate} AA {QualitySettings.antiAliasing} shadows {QualitySettings.shadows}");
            foreach ((string tag, bool clip, bool bubbles) in new[] { ("all on", true, true), ("clip capture OFF", false, true), ("bubbles OFF", true, false), ("both OFF", false, false), ("all on again", true, true) })
            {
                ClipRecorder.Suspended = !clip; SessionSettings.BubblesOn = bubbles;
                yield return new WaitForSecondsRealtime(2f);
                BubbleField.MsCandidates = BubbleField.MsAtmosphere = BubbleField.MsLoop = BubbleField.MsFlush = 0; BubbleField.TimedFrames = BubbleField.AtmosphereCalls = 0;
                int frames = 0; float worst = 0f; float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 10f) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
                float dur = Time.realtimeSinceStartup - t0;
                int tf = Mathf.Max(1, BubbleField.TimedFrames);
                if (bubbles) Debug.Log($"[Perf]   bubbles per frame: candidates {BubbleField.MsCandidates / tf:F1} ms, atmosphere {BubbleField.MsAtmosphere / tf:F1} ms ({BubbleField.AtmosphereCalls / tf} samples), per-bubble loop {BubbleField.MsLoop / tf:F1} ms, draw submit {BubbleField.MsFlush / tf:F1} ms");
                Debug.Log($"[Perf] {tag,-18} {frames / dur,5:F1} fps  mean {dur / frames * 1000f,5:F1} ms  worst {worst * 1000f,5:F0} ms  managed heap {System.GC.GetTotalMemory(false) / 1048576,4} MB");
            }
            ClipRecorder.Suspended = false; SessionSettings.BubblesOn = true;
            Debug.Log("[Perf] DONE");
        }

        /// <summary>AERO_SELFTEST=ui (owner 2026-10-03: no text or UI may overlap — landing page AND flying, landscape AND
        /// portrait): on a device the screen is rotated by the app itself; screenshots of the landing page (top/bottom) and
        /// in flight (powered with dials, with HUD, glider, a lesson live) in each orientation.</summary>
        private IEnumerator UiLayoutTest()
        {
            var orients = Application.isMobilePlatform
                ? new[] { ("land", ScreenOrientation.LandscapeLeft), ("port", ScreenOrientation.Portrait) }
                : new[] { ("land", ScreenOrientation.LandscapeLeft) };
            foreach ((string on, ScreenOrientation o) in orients)
            {
                if (Application.isMobilePlatform) Screen.orientation = o;
                yield return new WaitForSecondsRealtime(2.5f);
                SessionSettings.ChallengeId = null;
                SessionSettings.Instruments = SessionSettings.InstrumentMode.Analog;
                SessionSettings.AircraftId = "c172-like";
                Menu.Open(); Menu.ScrollTo(false);
                yield return new WaitForSecondsRealtime(1f);
                ScreenCapture.CaptureScreenshot($"ui-{on}-menu-top.png");
                yield return new WaitForSecondsRealtime(0.5f);
                Menu.ScrollTo(true);
                yield return new WaitForSecondsRealtime(1f);
                ScreenCapture.CaptureScreenshot($"ui-{on}-menu-end.png");
                yield return new WaitForSecondsRealtime(0.5f);
                foreach ((string tag, string ac, SessionSettings.InstrumentMode inst, string chal) in new[]
                {
                    ("c172-dials", "c172-like", SessionSettings.InstrumentMode.Analog, (string)null),
                    ("c172-hud", "c172-like", SessionSettings.InstrumentMode.Hud, null),
                    ("glider", "glider-2-33-like", SessionSettings.InstrumentMode.Analog, null),
                    ("lesson", "pa18-cub-like", SessionSettings.InstrumentMode.Analog, "lesson:straight"),
                })
                {
                    SessionSettings.AircraftId = ac; SessionSettings.Instruments = inst; SessionSettings.ChallengeId = chal;
                    Menu.Fly();
                    yield return new WaitForSecondsRealtime(3f);
                    var pc = Object.FindFirstObjectByType<FlyingGame.Bridge.Practice.PracticeController>();
                    if (pc != null) pc.StartCountdown();
                    yield return new WaitForSecondsRealtime(chal != null ? 5f : 2f);
                    ScreenCapture.CaptureScreenshot($"ui-{on}-fly-{tag}.png");
                    yield return new WaitForSecondsRealtime(0.7f);
                    Menu.Open();
                    yield return new WaitForSecondsRealtime(1f);
                }
                Debug.Log($"[SelfTest] ui {on} {Screen.width}x{Screen.height}");
            }
            if (Application.isMobilePlatform) Screen.orientation = ScreenOrientation.AutoRotation;
            SessionSettings.ChallengeId = null;
            Debug.Log("[SelfTest] DONE ui");
        }

        /// <summary>AERO_SELFTEST=menu (owner 2026-10-03: "doubled up text ... check it in both landscape and vertical"): the
        /// landing page at iPhone and iPad shapes, landscape and portrait, scrolled to the top and to the bottom.</summary>
        private IEnumerator MenuLayoutTest()
        {
            var shapes = new (string name, int w, int h)[] { ("iphone-land", 1311, 603), ("iphone-port", 603, 1311), ("ipad-land", 1180, 820), ("ipad-port", 820, 1180) };
            Menu.Open();
            SessionSettings.ChallengeId = "practice:approach-side";   // shows the lesson rows (YOU FLY / PRACTICE WIND) too
            foreach (var sh in shapes)
            {
                Screen.SetResolution(sh.w, sh.h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1.5f);
                Menu.ScrollTo(false);
                yield return new WaitForSecondsRealtime(0.5f);
                ScreenCapture.CaptureScreenshot($"selftest-menu-{sh.name}-top.png");
                yield return new WaitForSecondsRealtime(0.5f);
                Menu.ScrollTo(true);
                yield return new WaitForSecondsRealtime(0.5f);
                ScreenCapture.CaptureScreenshot($"selftest-menu-{sh.name}-end.png");
                yield return new WaitForSecondsRealtime(0.5f);
                Debug.Log($"[SelfTest] menu {sh.name} {Screen.width}x{Screen.height}");
            }
            Debug.Log("[SelfTest] DONE menu");
        }

        /// <summary>AERO_SELFTEST=side2d (owner 2026-10-03): each side-view lesson in turn, in the Cub, a screenshot a few
        /// seconds in — the 2-D side view (no 3-D world, orthographic, flat backdrop).</summary>
        private IEnumerator Side2DTest()
        {
            SessionSettings.AircraftId = "pa18-cub-like";
            foreach (string id in new[] { "practice:approach-side", "practice:flare-side", "practice:stall-side", "lesson:glide-side", "lesson:climb-vx" })
            {
                SessionSettings.ChallengeId = id;
                yield return new WaitForSecondsRealtime(2f);
                Debug.Log($"[SelfTest] side2d {id}");
                Menu.Fly();
                yield return new WaitForSecondsRealtime(3f);
                var pc = Object.FindFirstObjectByType<FlyingGame.Bridge.Practice.PracticeController>();
                if (pc != null) pc.StartCountdown();   // press GO
                yield return new WaitForSecondsRealtime(8f);
                ScreenCapture.CaptureScreenshot($"selftest-{id.Replace(':', '-')}.png");
                yield return new WaitForSecondsRealtime(1f);
                Debug.Log($"[SelfTest] side2d {id}: 2-D active {Side2DView.Active}, camera ortho {Camera.main.orthographic}");
                Menu.Open();
                yield return new WaitForSecondsRealtime(2f);
            }
            Debug.Log("[SelfTest] DONE side2d");
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
            yield return null; yield return null;   // the capture happens at the END of the frame: keep the view until then
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

        /// <summary>AERO_SELFTEST=lesson: the 172's round-out & flare lesson with full flaps, the game flying as the student —
        /// screenshots of the briefing, the live orb, the debrief (top and scrolled) and the replay with commentary.</summary>
        private IEnumerator LessonTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "c172-like";
            SessionSettings.ChallengeId = "practice:flare";
            SessionSettings.PracticeWindChoice = FlyingGame.Sim.Practice.PracticeWind.Calm;
            SessionSettings.LessonFlaps = 1f;
            Practice.PracticeController.SelfTestStudent = true;
            Menu.Fly();
            yield return new WaitForSecondsRealtime(2.5f);
            var pc = Object.FindFirstObjectByType<Practice.PracticeController>();
            while (pc.Scenario != null && pc.Scenario.LessonPages.Length > 0 && pc.LessonPage < pc.Scenario.LessonPages.Length) { pc.NextLessonPage(); yield return null; }
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot("lesson-briefing.png");
            yield return new WaitForSecondsRealtime(0.7f);
            pc.StartCountdown();
            yield return new WaitForSecondsRealtime(4.5f);
            ScreenCapture.CaptureScreenshot("lesson-live.png");
            for (int i = 0; i < 60 && pc.Scenario.Phase != FlyingGame.Sim.Practice.PracticePhase.Finished; i++) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log($"[SelfTest] lesson points {pc.Scenario.Judge.Points:F0} events {pc.Scenario.Judge.Events.Count}");
            ScreenCapture.CaptureScreenshot("lesson-debrief.png");
            yield return new WaitForSecondsRealtime(0.7f);
            var deb = Object.FindFirstObjectByType<Practice.LessonDebrief>();
            deb.WatchReplay();
            yield return new WaitForSecondsRealtime(6f);
            ScreenCapture.CaptureScreenshot("lesson-replay.png");
            yield return new WaitForSecondsRealtime(0.7f);
            Practice.PracticeController.SelfTestStudent = false;
            Debug.Log("[SelfTest] DONE lesson");
        }

        /// <summary>AERO_SELFTEST=playground: the canyon lake (slalom, Rainbow Bridge, the dam), the city (an avenue, the
        /// pad, the spinning ring, the fountains, a rooftop ring) and the Mall.</summary>
        private IEnumerator PlaygroundTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "c172-like";
            SessionSettings.StartMode = SessionSettings.Start.InTheAir;
            Menu.Fly();
            var drv = Menu.Driver;
            yield return new WaitForSecondsRealtime(2f);
            FlyingGame.Core.MathTypes.Quat Hdg(double deg) { double h = deg * System.Math.PI / 360; return new FlyingGame.Core.MathTypes.Quat(0, 0, System.Math.Sin(h), System.Math.Cos(h)); }
            IEnumerator Shot(string name, double x, double y, double up, double hdg, double v)
            {
                Menu.Fly();
                drv = Menu.Driver;
                yield return new WaitForSecondsRealtime(0.8f);
                drv.Sim.Aircraft.State = new FlyingGame.Core.RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(x, y, -up), Hdg(hdg), new FlyingGame.Core.MathTypes.Vec3(v, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero);
                yield return new WaitForSecondsRealtime(1.0f);
                Debug.Log($"[SelfTest] {name}");
                ScreenCapture.CaptureScreenshot(name + ".png");
                yield return new WaitForSecondsRealtime(0.5f);
            }
            double d = FlyingGame.Core.WorldTerrain.DatumM, lake = FlyingGame.Core.CanyonLake.SurfaceM;
            double ly = -1050; double lx = FlyingGame.Core.CanyonLake.CentreX(ly);
            yield return Shot("pg-lake", lx, ly, lake + 25, 90, 40);
            yield return Shot("pg-lake-high", lx - 900, -300, d + 250, 60, 45);
            var a = FlyingGame.Core.CanyonLake.Arches[0];
            yield return Shot("pg-rainbow", 0.5 * (a.Ax + a.Bx), 0.5 * (a.Ay + a.By) - 400, lake + 40, 90, 40);
            double dy = FlyingGame.Core.CanyonLake.DamY;
            yield return Shot("pg-dam", FlyingGame.Core.WorldTerrain.RiverCentreX(dy + 600), dy + 600, d + 40, -90, 40);
            yield return Shot("pg-city", FlyingGame.Core.FlyCity.X0 + 85, FlyingGame.Core.FlyCity.Y0 - 900, d + 120, 90, 45);
            var (px, py) = FlyingGame.Core.FlyCity.PadCentre;
            yield return Shot("pg-pad", px, py + 1100, d + 260, -90, 40);
            var (sx, sy, sz) = FlyingGame.Core.FlyCity.SpinRingCentre;
            yield return Shot("pg-spinring", sx - 700, sy, d + sz, 0, 40);
            yield return Shot("pg-fountains", FlyingGame.Core.FlyCity.PlazaX0 - 500, 0.5 * (FlyingGame.Core.FlyCity.PlazaY0 + FlyingGame.Core.FlyCity.PlazaY1), d + 60, 0, 40);
            yield return Shot("pg-mall", FlyingGame.Core.Mall.CentreX, FlyingGame.Core.Mall.MonumentY - 700, d + 60, 90, 40);
            var r1 = FlyingGame.Core.FlyCity.RooftopRings()[0];
            double rh = r1.headingDeg * System.Math.PI / 180;
            yield return Shot("pg-ring1", r1.t.Cx - 500 * System.Math.Cos(rh), r1.t.Cy - 500 * System.Math.Sin(rh), d + r1.t.HeightM + FlyingGame.Core.FlyCity.RingAboveRoofM, r1.headingDeg, 40);
            Debug.Log("[SelfTest] DONE playground");
        }

        /// <summary>AERO_SELFTEST=seaside: a screenshot tour — the Golden Gate up the strait, the city, the island from the
        /// channel, Avalon, the sea arch, the zone's clouds; then the Beaver on floats outside and inside the sea cave.</summary>
        private IEnumerator SeasideTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "c172-like";
            SessionSettings.StartMode = SessionSettings.Start.InTheAir;
            Menu.Fly();
            var drv = Menu.Driver;
            yield return new WaitForSecondsRealtime(2f);
            FlyingGame.Core.MathTypes.Quat Hdg(double deg) { double h = deg * System.Math.PI / 360; return new FlyingGame.Core.MathTypes.Quat(0, 0, System.Math.Sin(h), System.Math.Cos(h)); }
            IEnumerator Shot(string name, double x, double y, double up, double hdg, double v)
            {
                Menu.Fly();                       // a fresh, unbroken aircraft for every shot
                drv = Menu.Driver;
                yield return new WaitForSecondsRealtime(0.8f);
                drv.Sim.Aircraft.State = new FlyingGame.Core.RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(x, y, -up), Hdg(hdg), new FlyingGame.Core.MathTypes.Vec3(v, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero);
                yield return new WaitForSecondsRealtime(1.3f);
                Debug.Log($"[SelfTest] {name}");
                ScreenCapture.CaptureScreenshot(name + ".png");
                yield return new WaitForSecondsRealtime(0.7f);
            }
            double d = FlyingGame.Core.WorldTerrain.DatumM;
            yield return Shot("sea-gate", FlyingGame.Core.WorldTerrain.RiverCentreX(FlyingGame.Core.GoldenGate.Y - 700), FlyingGame.Core.GoldenGate.Y - 700, 90, 90, 45);
            yield return Shot("sea-city", FlyingGame.Core.FlyCity.CentreX - 1800, FlyingGame.Core.FlyCity.CentreY - 300, d + 260, 10, 50);
            yield return Shot("sea-zone", FlyingGame.Core.Combat.CombatZone.X0 - 1800, FlyingGame.Core.Combat.CombatZone.CentreY, d + 900, 0, 50);
            yield return Shot("sea-island", FlyingGame.Core.Island.Main.Cx, FlyingGame.Core.Coast.MeanShoreY + 400, 700, 90, 50);
            yield return Shot("sea-avalon", FlyingGame.Core.Island.AvalonX, FlyingGame.Core.Island.AvalonY - 900, 120, 90, 45);
            yield return Shot("sea-arch", FlyingGame.Core.SeaArch.Cx, FlyingGame.Core.SeaArch.Cy - 900, 80, 90, 45);
            yield return Shot("sea-runway", FlyingGame.Core.Island.RunwayX - 2200, FlyingGame.Core.Island.RunwayY, FlyingGame.Core.Island.RunwayElevM + 120, 0, 45);
            // The Beaver on floats: outside the cave mouth, then inside on the water.
            SessionSettings.AircraftId = "dhc2-beaver-floats-like";
            Menu.Fly();
            drv = Menu.Driver;
            yield return new WaitForSecondsRealtime(2f);
            double keel = drv.Sim.Aircraft.Config.Floats != null ? drv.Sim.Aircraft.Config.Floats.KeelZ - 0.25 : 1.5;
            yield return Shot("sea-cave-mouth", FlyingGame.Core.SeaCave.Cx, FlyingGame.Core.SeaCave.Cy - 230, keel, 90, 0);
            yield return Shot("sea-cave-inside", FlyingGame.Core.SeaCave.Cx, FlyingGame.Core.SeaCave.Cy - 45, keel, 90, 0);
            Chase?.SetView(ChaseCamera.View.SideLeft);
            yield return Shot("sea-cave-side", FlyingGame.Core.SeaCave.Cx, FlyingGame.Core.SeaCave.Cy - 30, keel, 90, 0);
            Debug.Log("[SelfTest] DONE seaside");
        }

        /// <summary>AERO_SELFTEST=clouds: the Skyhawk put at three spots round the first Valley thermal's cumulus — 3.5 km
        /// off at 1,500 m (the cloud on the horizon), under its base, and inside it (whiteout).</summary>
        private IEnumerator CloudTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "c172-like";
            SessionSettings.StartMode = SessionSettings.Start.InTheAir;
            Menu.Fly();
            var drv = Menu.Driver;
            yield return new WaitForSecondsRealtime(2f);
            var th = FlyingGame.Core.Atmosphere.Thermals[0];
            var core = th.CoreAt(th.TopAltitudeM);
            (string name, double dx, double alt)[] shots = { ("clouds-far", -3500, 1500), ("clouds-under", -900, th.TopAltitudeM - 250), ("clouds-inside", -50, th.TopAltitudeM + 250) };
            foreach (var (name, dx, alt) in shots)
            {
                var pos = new FlyingGame.Core.MathTypes.Vec3(core.X + dx, core.Y, -alt);
                drv.Sim.Aircraft.State = new FlyingGame.Core.RigidBodyState(pos, FlyingGame.Core.MathTypes.Quat.Identity,
                    new FlyingGame.Core.MathTypes.Vec3(50, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero);
                yield return new WaitForSecondsRealtime(1.2f);
                Debug.Log($"[SelfTest] {name} whiteout {Cumulus.Whiteout:F2}");
                ScreenCapture.CaptureScreenshot(name + ".png");
                yield return new WaitForSecondsRealtime(0.8f);
            }
            Debug.Log("[SelfTest] DONE clouds");
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
                if (i == 2) { Chase?.SetView(ChaseCamera.View.SideLeft); }
                if (i == 3) { SessionSettings.BubblesOn = false; }
                if (i == 4) { ScreenCapture.CaptureScreenshot("selftest-thermal-liftonly.png"); SessionSettings.BubblesOn = true; }
            }
            Debug.Log("[SelfTest] DONE thermal");
        }

        /// <summary>AERO_SELFTEST=dogfight: P-51 vs P-51 (moderate), the player hands off; logs the status line and takes
        /// screenshots before the merge (indicator + gunsight, guns cold) and during the fight.</summary>
        private IEnumerator DogfightTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            SessionSettings.AircraftId = "p51d-like";
            SessionSettings.ChallengeId = "event:dogfight";
            SessionSettings.DogfightOpponentId = "p51d-like"; SessionSettings.DogfightSkill = 1;
            Menu.Fly();
            for (int i = 0; i < 14; i++)
            {
                yield return new WaitForSecondsRealtime(4f);
                Debug.Log($"[SelfTest] t={4 * (i + 1)} {Combat.Line}");
                if (i == 1) ScreenCapture.CaptureScreenshot("selftest-dogfight-1.png");
                if (i == 7) ScreenCapture.CaptureScreenshot("selftest-dogfight-2.png");
            }
            Debug.Log("[SelfTest] DONE dogfight");
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

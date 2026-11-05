using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core.Landing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The landing flow as the Quest shows it (docs/LANDING-V2.md): the VR panel's landing rows, rendered from the eye the
    /// way <see cref="QuestMenuShots"/> renders the in-flight menu, with the flow in three states (first time, a J-3 on
    /// floats with a friend on the other controls, sunset). The rows are QuestMenu's own, on the same LandingFlow the flat
    /// page drives.
    ///
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.LandingQuestShots.Run
    ///   (NOT -nographics.) Writes $LANDING_SHOTS (default logs/)/quest-*.png.
    /// </summary>
    public static class LandingQuestShots
    {
        public static void Run()
        {
            int failures = 0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Head");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 97f;
            cam.nearClipPlane = 0.05f;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 0f), Quaternion.identity);
            // The landing page is up (StartMenu open, menu paused), so QuestMenu shows its landing rows.
            new GameObject("Landing").AddComponent<StartMenu>();
            SessionSettings.MenuOpen = true;
            LandingSession.Reset();
            var flow = LandingSession.Flow;
            var rig = camGo.AddComponent<QuestRig>();
            var menu = new QuestMenu();
            menu.OpenForTesting(rig);
            SessionSettings.PanelPause = false;
            Time.timeScale = 1f;

            string dir = System.Environment.GetEnvironmentVariable("LANDING_SHOTS");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../logs"));
            Directory.CreateDirectory(dir);

            Shoot(cam, menu, dir, "quest-landing-first-time");
            if (flow.FamilyId != LandingCatalog.FirstTimeAircraftId) { Debug.LogError($"[LandingQuestShots] first time is {flow.FamilyId}"); failures++; }

            flow.Pick(PickerKind.Aircraft, "j3-cub-like");
            flow.Pick(PickerKind.Configuration, "floats");
            var other = flow.Seats.Find(s => s.Occupant != Occupant.Me);
            if (other != null) flow.SetSeat(other.SeatId, Occupant.InvitedPlayer, SeatRole.DualControl);
            flow.SetFuelFraction(1.0);
            Shoot(cam, menu, dir, "quest-landing-j3-floats-friend");

            flow.SetTimeOfDay(TimeOfDay.Sunset);
            flow.SetWeather("gusty");
            Shoot(cam, menu, dir, "quest-landing-sunset-gusty");
            // Units (Ty 2026-11-05 10:56): the Units row and the W&B line in each system. The editor's own pref is put back.
            string savedUnits = PlayerPrefs.HasKey(DisplayUnits.PrefKey) ? PlayerPrefs.GetString(DisplayUnits.PrefKey) : null;
            flow.SetUnits(UnitSystem.Standard);
            Shoot(cam, menu, dir, "quest-landing-units-standard");
            if (!string.Join("|", flow.Summary()).Contains(" lb")) { Debug.LogError("[LandingQuestShots] Standard summary is not in lb"); failures++; }
            flow.SetUnits(UnitSystem.Metric);
            Shoot(cam, menu, dir, "quest-landing-units-metric");
            if (!string.Join("|", flow.Summary()).Contains(" kg")) { Debug.LogError("[LandingQuestShots] Metric summary is not in kg"); failures++; }
            if (savedUnits == null) PlayerPrefs.DeleteKey(DisplayUnits.PrefKey); else PlayerPrefs.SetString(DisplayUnits.PrefKey, savedUnits);
            Debug.Log($"[LandingQuestShots] {failures} failure(s); summary: {string.Join(" | ", flow.Summary())}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static void Shoot(Camera cam, QuestMenu menu, string dir, string name)
        {
            menu.RefreshForTesting();
            var rt = new RenderTexture(1600, 1680, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[LandingQuestShots] wrote {path}");
        }
    }
}

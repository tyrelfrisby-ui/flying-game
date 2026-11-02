using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Renders the Quest's VR menu from where the pilot's eye will be, so its size, orientation and legibility are
    /// looked at on the bench — nobody can see it any other way without a headset.
    ///
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.QuestMenuShots.Run
    ///   (NOT -nographics: it renders.) Writes logs/quest-menu.png.
    ///
    /// The camera is a stand-in for one eye of a Quest 3S: about 97° vertical, at the head, looking level. The menu
    /// is built and placed by the same <see cref="QuestMenu"/> code the headset runs, through its explicit test entry
    /// point (an editor batch runs no Awake / Update — CLAUDE.md, "An editor batch tool has to drive the component
    /// itself").
    /// </summary>
    public static class QuestMenuShots
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
            // Something solid in front, closer than the menu, the way an instrument panel is: the menu must show
            // through it.
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.transform.position = new Vector3(0f, 1.0f, 0.6f);
            panel.transform.localScale = new Vector3(1.2f, 0.25f, 0.05f);
            var rig = camGo.AddComponent<QuestRig>();
            var menu = new QuestMenu();
            GameObject root = menu.OpenForTesting(rig);
            // Opening the menu pauses the sim (PanelPause). Put time back by hand: an editor batch saves
            // ProjectSettings on exit, and a time scale of 0 ended up in TimeManager.asset the first time.
            SessionSettings.PanelPause = false;
            Time.timeScale = 1f;

            Vector3 toMenu = root.transform.position - camGo.transform.position;
            Debug.Log($"[QuestMenuShots] menu {toMenu.magnitude:F2} m ahead, facing {Vector3.Angle(root.transform.forward, toMenu):F0}° off the line of sight");
            if (Mathf.Abs(toMenu.magnitude - QuestMenu.Distance) > 0.1f) { Debug.LogError("[QuestMenuShots] menu not at its distance"); failures++; }
            if (Vector3.Angle(root.transform.forward, toMenu) > 5f) { Debug.LogError("[QuestMenuShots] menu does not face the pilot"); failures++; }

            int rows = 0;
            foreach (var t in root.GetComponentsInChildren<TextMesh>())
            {
                if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                rows++;
                Bounds b = t.GetComponent<MeshRenderer>().bounds;
                Debug.Log($"[QuestMenuShots] '{t.text}' {b.size.x * 100f:F1} x {b.size.y * 100f:F1} cm");
                if (b.size.x > QuestMenu.Width) { Debug.LogError($"[QuestMenuShots] '{t.text}' is wider than the panel"); failures++; }
            }
            if (rows < 5) { Debug.LogError($"[QuestMenuShots] only {rows} text rows"); failures++; }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../logs"));
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1600, 1680, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string path = Path.Combine(dir, "quest-menu.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[QuestMenuShots] wrote {path}; {failures} failure(s)");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}

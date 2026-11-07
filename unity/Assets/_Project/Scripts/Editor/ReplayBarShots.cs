using System;
using System.IO;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim.Replay;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// The REPLAY bar as the game draws it (Ty t982u: the replay smoke controls). IMGUI only exists in a real GUI view, so
    /// this is an EditorWindow (as LocalHudShots) that calls ViewPanel's own EnsureStyles and DrawReplayBar, verbatim, on a
    /// FlightReplay fixture (<see cref="FlightReplay.ReviewFixture"/>) holding a 60 s recording whose smoke was switched on
    /// from 0:10 to 0:40, the play head at 0:45. One PNG per screen size and per smoke state
    /// (<see cref="FlightReplay.ReviewSmokeStates"/>, in the order a pilot reaches them). Not batch mode:
    ///   Unity -projectPath unity -executeMethod FlyingGame.EditorTools.ReplayBarShots.Run -barTag before -barOut DIR
    /// </summary>
    public sealed class ReplayBarShots : EditorWindow
    {
        private static readonly Vector2Int[] Sizes = { new(932, 430), new(1180, 820), new(430, 932) };
        private static readonly string[] SizeNames = { "iphone-landscape", "ipad-landscape", "iphone-portrait" };
        private FlightReplay _replay;
        private ViewPanel _panel;
        private System.Collections.Generic.List<(string name, Action apply)> _states;
        private string _tag, _dir;
        private int _frame, _size, _state;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            var w = CreateInstance<ReplayBarShots>();
            w._tag = Arg("-barTag", "after");
            w._dir = Path.GetFullPath(Path.Combine(Arg("-barOut", Path.Combine(Application.dataPath, "../../renders/replay-bar")), w._tag));
            Directory.CreateDirectory(w._dir);
            var root = new GameObject("ReplayBar review fixture");
            w._replay = root.AddComponent<FlightReplay>();
            var camGo = new GameObject("ReviewCam"); camGo.AddComponent<Camera>();
            var chase = camGo.AddComponent<ChaseCamera>();
            w._panel = root.AddComponent<ViewPanel>();
            w._panel.Replay = w._replay; w._panel.Chase = chase;
            // The recording: 60 s, 30 Hz, a straight pass; the smoke switch on from 0:10 to 0:40.
            for (int i = 0; i <= 1800; i++)
            {
                double t = i / 30.0;
                var st = new RigidBodyState(new Vec3(50 * t, 0, -300), Quat.Identity, new Vec3(50, 0, 0), Vec3.Zero);
                w._replay.Recorder.Record(new ReplayFrame(t, st, new ControlDeflections(0, 0, 0, 0), 0.8, 0, 1, 1, Vec3.Zero,
                    new ReplaySmoke(t >= 10 && t < 40, 0.85f, 30, 1.1, 40 * t, 2400)));
            }
            w._replay.ReviewFixture(45);
            w._states = w._replay.ReviewSmokeStates();
            w._states[0].apply();
            w.ShowPopup();
            w.SetSize();
            EditorApplication.update += w.Tick;
        }

        private void SetSize()
        {
            var size = Sizes[_size];
            minSize = maxSize = size;
            position = new Rect(50, 80, size.x, size.y);
            _frame = 0;
            Focus();
        }

        private void Tick()
        {
            Repaint();
            if (++_frame < 25) return;
            Grab(Path.Combine(_dir, $"{SizeNames[_size]}_{_states[_state].name}.png"));
            if (++_state < _states.Count) { _states[_state].apply(); _frame = 0; return; }
            _state = 0; _states[0].apply();
            if (++_size < Sizes.Length) { SetSize(); return; }
            EditorApplication.update -= Tick;
            Debug.Log($"[ReplayBarShots] {_tag}: {Sizes.Length * _states.Count} captures in {_dir}");
            Close();
            EditorApplication.Exit(0);
        }

        private void Grab(string path)
        {
            var size = Sizes[_size];
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            object host = typeof(EditorWindow).GetField("m_Parent", flags).GetValue(this);
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
            int pw = Mathf.RoundToInt(size.x * EditorGUIUtility.pixelsPerPoint), ph = Mathf.RoundToInt(size.y * EditorGUIUtility.pixelsPerPoint);
            var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32); rt.Create();
            type.GetMethod("GrabPixels", flags).Invoke(host, new object[] { rt, new Rect(0, 0, pw, ph) });
            RenderTexture.active = rt;
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply();
            var px = tex.GetPixels(); var flipped = new Color[px.Length];
            for (int y = 0; y < ph; y++) Array.Copy(px, y * pw, flipped, (ph - 1 - y) * pw, pw);
            tex.SetPixels(flipped); tex.Apply();
            RenderTexture.active = null; rt.Release(); DestroyImmediate(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
            Debug.Log("[ReplayBarShots] " + path);
        }

        private void OnGUI()
        {
            if (_panel == null) return;
            var size = Sizes[_size];
            GUI.color = new Color(0.42f, 0.6f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, size.x, size.y), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(10, 8, size.x - 20, 40), $"[{_tag}] Screen {Screen.width}x{Screen.height} · replay head 0:45 of 1:00 · smoke flown ON 0:10–0:40 · state: {_states[_state].name}");
            // ViewPanel lays out in Screen pixels (a device's own); here Screen is the window in Retina pixels while GUI
            // coordinates are points, so draw the panel scaled by 1/pixelsPerPoint: one capture pixel = one device pixel.
            Matrix4x4 saved = GUI.matrix;
            float k = 1f / EditorGUIUtility.pixelsPerPoint;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ViewPanel).GetMethod("EnsureStyles", flags).Invoke(_panel, null);
            typeof(ViewPanel).GetMethod("DrawReplayBar", flags).Invoke(_panel, null);
            GUI.matrix = saved;
        }
    }
}

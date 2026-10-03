using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Quick clips (owner 2026-10-01). A hidden CLIP CAMERA copies the main camera every frame it captures (30 fps) and
    /// renders only the world — the aircraft, terrain, bubbles, pilot and chute, tracers — never the on-screen buttons.
    /// With <see cref="IncludeInstruments"/> the dials (or the HUD) are drawn into the clip image too. Frames and the
    /// game sound (the audio listener's final mix) stream to Plugins/iOS/ClipEncoder.mm, which keeps the last few
    /// minutes on disk. CLIP saves the newest <see cref="ClipSeconds"/> (15 s – 3 min) to Photos; REC saves everything
    /// between two taps. Works the same live and in a replay (the replay is what the camera sees).
    /// Lives on the main camera (same GameObject as the AudioListener, so OnAudioFilterRead hears the final mix).
    /// </summary>
    [DefaultExecutionOrder(2000)]   // after ChaseCamera has placed the main camera
    public sealed class ClipRecorder : MonoBehaviour
    {
        public const int Fps = 30;
        public const int MaxDim = 1920;
        public const int MinSeconds = 15, MaxSeconds = 180, StepSeconds = 15;
        public const int Bitrate = 10_000_000;

        // ---- user settings (OPTIONS) -----------------------------------------------------------
        private static int _clipSeconds = -1;
        private static int _instruments = -1;
        public static int ClipSeconds
        {
            get { if (_clipSeconds < 0) _clipSeconds = Mathf.Clamp(PlayerPrefs.GetInt("clip.seconds", 30), MinSeconds, MaxSeconds); return _clipSeconds; }
            set { _clipSeconds = Mathf.Clamp(Mathf.RoundToInt(value / (float)StepSeconds) * StepSeconds, MinSeconds, MaxSeconds); PlayerPrefs.SetInt("clip.seconds", _clipSeconds); PlayerPrefs.Save(); }
        }
        /// <summary>Include the instruments (analog dials or HUD, whichever is showing) in clips. Off = just the aircraft.</summary>
        public static bool IncludeInstruments
        {
            get { if (_instruments < 0) _instruments = PlayerPrefs.GetInt("clip.instruments", 0); return _instruments == 1; }
            set { _instruments = value ? 1 : 0; PlayerPrefs.SetInt("clip.instruments", _instruments); PlayerPrefs.Save(); }
        }
        public static string Length(int sec) => sec < 60 ? $"{sec} s" : $"{sec / 60}:{sec % 60:00}";

#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLib = "__Internal";       // linked into the iOS app by Xcode
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private const string NativeLib = "AeroNative";       // Mac app: Plugins/macOS/AeroNative.bundle (tools/native-mac/build.sh)
#endif
#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
        [DllImport(NativeLib)] private static extern double CE_Now();
        [DllImport(NativeLib)] private static extern void CE_SetPaused(int paused);
        [DllImport(NativeLib)] private static extern void CE_Configure(int w, int h, int fps, int sampleRate, int channels, int bitrate, double keepSec);
        [DllImport(NativeLib)] private static extern void CE_SetKeepCopy(int keep);
        [DllImport(NativeLib)] private static extern unsafe void CE_AppendVideo(void* bgra, int w, int h, double pts);
        [DllImport(NativeLib)] private static extern void CE_AppendAudio(float[] pcm, int frames, int channels, double pts);
        [DllImport(NativeLib)] private static extern void CE_SaveClip(double seconds);
        [DllImport(NativeLib)] private static extern void CE_StartRecording();
        [DllImport(NativeLib)] private static extern void CE_StopRecording();
        [DllImport(NativeLib)] private static extern int CE_IsRecording();
        [DllImport(NativeLib)] private static extern int CE_IsBusy();
        [DllImport(NativeLib)] private static extern int CE_MessageSeq();
        [DllImport(NativeLib)] private static extern string CE_Message();
        private const bool Native = true;
#else
        private static double CE_Now() => Time.realtimeSinceStartupAsDouble;
        private static void CE_SetPaused(int paused) { }
        private static void CE_Configure(int w, int h, int fps, int sampleRate, int channels, int bitrate, double keepSec) { }
        private static void CE_SetKeepCopy(int keep) { }
        private static unsafe void CE_AppendVideo(void* bgra, int w, int h, double pts) { }
        private static void CE_AppendAudio(float[] pcm, int frames, int channels, double pts) { }
        private static void CE_SaveClip(double seconds) { _editorMsg = "Clips are saved on the device only"; }
        private static void CE_StartRecording() { _editorRec = true; }
        private static void CE_StopRecording() { _editorRec = false; _editorMsg = "Clips are saved on the device only"; }
        private static int CE_IsRecording() => _editorRec ? 1 : 0;
        private static int CE_IsBusy() => 0;
        private static int CE_MessageSeq() => _editorMsg == null ? 0 : 1;
        private static string CE_Message() { string m = _editorMsg; _editorMsg = null; return m; }
        private static string _editorMsg; private static bool _editorRec;
        private const bool Native = false;
#endif

        public static bool Recording => CE_IsRecording() != 0;
        public static bool Busy => CE_IsBusy() != 0;
        public static void SaveClip() => CE_SaveClip(ClipSeconds);
        public static void ToggleRecording() { if (Recording) CE_StopRecording(); else CE_StartRecording(); }
        public static void KeepCopies(bool keep) => CE_SetKeepCopy(keep ? 1 : 0);   // self-test: also write clips to Documents/Clips

        private static int _seenSeq;
        /// <summary>The newest status message ("Clip saved to Photos", an error …) once, or null.</summary>
        public static string PollMessage()
        {
            int seq = CE_MessageSeq();
            if (seq == _seenSeq) return null;
            _seenSeq = seq;
            return CE_Message();
        }

        // ---- capture ---------------------------------------------------------------------------
        public HudOverlay Hud;
        public AnalogGauges Gauges;
        public CombatController Combat;

        private Camera _main, _clip;
        private RenderTexture _rt;
        private double _next;
        private bool _captureThisFrame;
        private double _framePts;
        private static volatile bool _audioOn;
        private int _sampleRate;
        private readonly WaitForEndOfFrame _eof = new WaitForEndOfFrame();

        private void Start()
        {
            _main = GetComponent<Camera>();
            _sampleRate = AudioSettings.outputSampleRate;
            var go = new GameObject("ClipCamera");
            _clip = go.AddComponent<Camera>();
            _clip.enabled = false;   // rendered by hand, only on capture frames
            go.AddComponent<ClipCameraHook>().Owner = this;
            go.AddComponent<TracerOverlay>().Combat = Combat;   // bullets are part of the action
            StartCoroutine(ReadbackAtEndOfFrame());
        }

        private void OnDestroy() { if (_rt != null) _rt.Release(); }

        private void LateUpdate()
        {
            bool live = Native && !SessionSettings.MenuOpen && _main != null;
            _audioOn = live;
            CE_SetPaused(live ? 0 : 1);
            if (!live) return;

            // The clip is the camera's view (portrait: the area above the control tray), at most 1920 px on its long side.
            Rect px = _main.pixelRect;
            float k = Mathf.Min(1f, MaxDim / Mathf.Max(px.width, px.height));
            int w = Mathf.Max(64, Mathf.RoundToInt(px.width * k / 2f) * 2), h = Mathf.Max(64, Mathf.RoundToInt(px.height * k / 2f) * 2);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ClipRT", antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) };
                _rt.Create();
                CE_Configure(w, h, Fps, _sampleRate, _channels > 0 ? _channels : 2, Bitrate, MaxSeconds + 20);
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _next) return;
            _next = now - _next > 0.25 ? now + 1.0 / Fps : _next + 1.0 / Fps;

            _clip.CopyFrom(_main);
            _clip.rect = new Rect(0, 0, 1, 1);
            _clip.targetTexture = _rt;
            _clip.enabled = false;
            _framePts = CE_Now();
            _clip.Render();
            _captureThisFrame = true;
        }

        private int _channels;
        private float[] _mix;   // audio thread scratch
        /// <summary>Diagnostics (self-test): loudest sample the listener mix delivered, and how many blocks arrived.</summary>
        public static volatile float AudioPeak;
        public static volatile int AudioBlocks;

        /// <summary>Clip camera's OnPostRender: the HUD's lines go into the clip image when instruments are wanted.</summary>
        internal void OnClipPostRender(Camera cam)
        {
            if (IncludeInstruments && Hud != null && SessionSettings.Instruments == SessionSettings.InstrumentMode.Hud) Hud.RenderForClip(cam);
        }

        private void OnGUI()
        {
            // Instruments drawn by IMGUI (the dials, the HUD's numbers) go into the clip image: same drawing code, the
            // clip texture as the target and GUI.matrix mapping the screen's view area onto it.
            if (!_captureThisFrame || !IncludeInstruments || Event.current.type != EventType.Repaint || _rt == null) return;
            bool analog = SessionSettings.Instruments == SessionSettings.InstrumentMode.Analog && Gauges != null;
            bool hud = SessionSettings.Instruments == SessionSettings.InstrumentMode.Hud && Hud != null;
            if (!analog && !hud) return;
            RenderTexture prev = RenderTexture.active;
            Matrix4x4 keep = GUI.matrix;
            Color keepColor = GUI.color;
            RenderTexture.active = _rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, _rt.width, _rt.height, 0);
            try
            {
                if (analog)
                {
                    Rect vp = _main.pixelRect;
                    float top = Screen.height - vp.yMax;   // GUI space is top-left origin
                    float s = _rt.width / vp.width;
                    GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f)) * Matrix4x4.Translate(new Vector3(-vp.x, -top, 0f));
                    Gauges.DrawForClip();
                }
                if (hud)
                {
                    GUI.matrix = Matrix4x4.identity;
                    Hud.DrawClipLabels(_rt.width, _rt.height);
                }
            }
            finally
            {
                GUI.matrix = keep;
                GUI.color = keepColor;
                GL.PopMatrix();
                RenderTexture.active = prev;
            }
        }

        private System.Collections.IEnumerator ReadbackAtEndOfFrame()
        {
            while (true)
            {
                yield return _eof;
                if (!_captureThisFrame || _rt == null) continue;
                _captureThisFrame = false;
                double pts = _framePts;
                int w = _rt.width, h = _rt.height;
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.BGRA32, req => OnFrame(req, w, h, pts));
            }
        }

        private static unsafe void OnFrame(AsyncGPUReadbackRequest req, int w, int h, double pts)
        {
            if (req.hasError || req.width != w || req.height != h) return;
            NativeArray<byte> data = req.GetData<byte>();
            CE_AppendVideo(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), w, h, pts);
        }

        // Final mix of the AudioListener on this GameObject (audio thread). Read only — the game hears it unchanged.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;
            float peak = 0f;
            for (int i = 0; i < data.Length; i += 7) { float a = data[i] < 0 ? -data[i] : data[i]; if (a > peak) peak = a; }
            if (peak > AudioPeak) AudioPeak = peak;
            AudioBlocks++;
            if (!_audioOn || !Native) return;
            int frames = data.Length / channels;
            // The clip also carries the pilot's own voice (intercom / radio) — added to a copy, never to what the game plays.
            if (_mix == null || _mix.Length != data.Length) _mix = new float[data.Length];
            System.Array.Copy(data, _mix, data.Length);
            VoiceComms.ReadClipVoice(_mix, frames, channels);
            CE_AppendAudio(_mix, frames, channels, CE_Now() - (double)frames / _sampleRate);
        }
    }

    /// <summary>On the clip camera: hands its OnPostRender to the recorder (HUD lines into the clip image).</summary>
    internal sealed class ClipCameraHook : MonoBehaviour
    {
        public ClipRecorder Owner;
        private Camera _cam;
        private void Awake() => _cam = GetComponent<Camera>();
        private void OnPostRender() { if (Owner != null) Owner.OnClipPostRender(_cam); }
    }
}

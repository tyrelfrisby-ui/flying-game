#if UNITY_ANDROID && !UNITY_EDITOR
#define QUEST_RECORDER
#endif
using System;
using FlyingGame.Core.Vr;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// RECORD WHAT THE HEADSET SHOWS, from the controllers (owner Ty, 2026-11-02: start / stop a "what I see"
    /// recording without the Meta menu). A + B held for a second (<see cref="QuestRecordChord"/>), or the VR menu's
    /// Record row. A red "REC 0:42" sits high in the left of the view while it runs; "Saved …" shows on stop.
    ///
    /// WHY THE APP RECORDS ITSELF. Android's MediaProjection needs a consent dialog on every session: a 2D system
    /// activity, which in an immersive OpenXR app takes the pilot out of the cockpit (the session loses focus)
    /// every time, and on Horizon OS it is not a supported way to capture the compositor's view. Meta's own
    /// capture (Meta button → Camera) has no intent a third-party app may send. So:
    ///
    ///   a mono camera at the head (the centre eye, <see cref="QuestRecording.VerticalFovDeg"/> tall) renders
    ///   <see cref="QuestRecording.Width"/> x <see cref="QuestRecording.Height"/> at <see cref="QuestRecording.Fps"/>
    ///   → Shaders/QuestRecordPack packs it to I420 on the GPU → AsyncGPUReadback (1.4 MB a frame, no stall)
    ///   → Plugins/Android/QuestRecorder.java: MediaCodec H.264 + AAC, MediaMuxer .mp4, on its own threads.
    ///
    /// Sound is the AudioListener's final mix (this component sits on the listener's GameObject), read only.
    /// Files go to the app's own Movies folder, /sdcard/Android/data/&lt;app id&gt;/files/Movies (no storage
    /// permission needed); `tools/deploy/quest-record.sh --pull` copies them to the Mac.
    ///
    /// COST: one extra mono scene render 30 times a second (not 72), at 0.9 Mpixel, plus a full-screen pack pass.
    /// The [QuestPerf] line says "rec ON" while it runs so its cost can be read off the log (docs/quest-performance.md).
    ///
    /// A recording is only a playable file once the muxer has written its index, so it is finalised on stop, on
    /// pause (headset off, Meta menu → quit), on quit and on destroy, waiting up to <see cref="StopWaitMs"/>.
    ///
    /// Android only: everywhere else <see cref="Supported"/> is false, QuestRig never adds this, and none of the
    /// JNI below is compiled.
    /// </summary>
    [DefaultExecutionOrder(10001)]   // after QuestRig has put the camera at the head
    [RequireComponent(typeof(Camera))]
    public sealed class QuestRecorder : MonoBehaviour
    {
        /// <summary>Headset-only overlays (the REC indicator, the toast): the main camera draws this layer, the
        /// recording camera does not.</summary>
        public const int OverlayLayer = 30;
        public const int StopWaitMs = 3000;

#if QUEST_RECORDER
        public static bool Supported => true;
#else
        public static bool Supported => false;
#endif
        public static QuestRecorder Instance { get; private set; }
        public static bool Recording => Instance != null && Instance._on;
        public static bool Finishing => Instance != null && !Instance._on && Instance.JavaFinishing();
        public static double Seconds => Recording ? Time.realtimeSinceStartupAsDouble - Instance._t0 : 0;

        /// <summary>The A + B chord and the menu row.</summary>
        public static void Toggle()
        {
            if (Instance == null) return;
            if (Instance._on) Instance.StopRecording(0);
            else Instance.StartRecording();
        }

        /// <summary>The VR menu's Record row.</summary>
        public static string MenuLabel =>
            Recording ? $"Record video:  ON  {QuestRecording.Clock(Seconds)}   (stop)"
            : Finishing ? "Record video:  saving ..."
            : "Record video:  Off   (or hold A + B)";

        /// <summary>For the [QuestPerf] line.</summary>
        public static string PerfTag => Recording ? $"rec ON ({Instance._sent} sent, {Instance.JavaDropped()} dropped)" : "rec off";

        private Camera _main, _rec;
        private RenderTexture _rgb, _yuv;
        private Material _pack;
        private bool _on;
        private double _t0, _next;
        private int _sent;
        private Transform _indicator, _dot;
        private TextMesh _recText, _toast;
        private Material _overlayText, _overlayDot;
        private float _toastUntil;
        private int _seenSeq;

        private void Awake()
        {
            Instance = this;
            _main = GetComponent<Camera>();
            _main.cullingMask |= 1 << OverlayLayer;
            _seenSeq = JavaMessageSeq();
        }

        private void OnDestroy()
        {
            StopRecording(StopWaitMs);
            if (_rgb != null) _rgb.Release();
            if (_yuv != null) _yuv.Release();
            if (_rec != null) Destroy(_rec.gameObject);
            ReleaseJni();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationPause(bool paused) { if (paused) StopRecording(StopWaitMs); }
        private void OnApplicationQuit() => StopRecording(StopWaitMs);

        // ---- start / stop ----

        private void StartRecording()
        {
            if (_on) return;
            string err = PrepareGpu();
            if (err != null) { Toast(err); Debug.LogError($"[Quest] record: {err}"); return; }
            string dir = MoviesDir();
            string path = System.IO.Path.Combine(dir, QuestRecording.FileName(DateTime.Now));
            int rate = AudioSettings.outputSampleRate;
            bool audio = GetComponent<AudioListener>() != null && rate > 0;
            ResetAudio();
            if (!JavaStart(path, audio ? rate : 0)) { PollMessage(); return; }
            _on = true;
            _sent = 0;
            _t0 = _next = Time.realtimeSinceStartupAsDouble;
            _audioOn = audio;
            Debug.Log($"[Quest] recording started: {path} ({QuestRecording.Width}x{QuestRecording.Height} @{QuestRecording.Fps}, audio {(audio ? rate + " Hz" : "off")})");
        }

        /// <summary>Stop and finalise. <paramref name="waitMs"/> &gt; 0 blocks until the file is written (pause,
        /// quit): the process may be frozen or killed right after.</summary>
        public void StopRecording(int waitMs)
        {
            if (!_on) { if (waitMs > 0 && JavaFinishing()) JavaStop(waitMs); return; }
            _on = false;
            _audioOn = false;
            PumpAudio();
            JavaStop(waitMs);
            Debug.Log($"[Quest] recording stopped after {QuestRecording.Clock(Time.realtimeSinceStartupAsDouble - _t0)}, {_sent} frames sent");
        }

        private string PrepareGpu()
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)) return "Recording needs R8 render textures";
            if (_pack == null)
            {
                Shader sh = Shader.Find("Hidden/FlyingGame/QuestRecordPack");
                if (sh == null) return "Recording shader missing from the build";
                _pack = new Material(sh);
            }
            int w = QuestRecording.Width, h = QuestRecording.Height;
            if (_rgb == null)
            {
                _rgb = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "QuestRecordRGB", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _rgb.Create();
            }
            if (_yuv == null)
            {
                _yuv = new RenderTexture(w, QuestRecording.I420Rows(h), 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear) { name = "QuestRecordI420", filterMode = FilterMode.Point };
                _yuv.Create();
                _pack.SetVector("_PackSize", new Vector4(w, h, 0, 0));
            }
            if (_rec == null)
            {
                var go = new GameObject("QuestRecordCamera");
                DontDestroyOnLoad(go);
                _rec = go.AddComponent<Camera>();
                _rec.enabled = false;   // rendered by hand, 30 times a second
            }
            return null;
        }

        // ---- every frame ----

        private void LateUpdate()
        {
            PollMessage();
            if (_on)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (!JavaRecording()) { _on = false; _audioOn = false; }   // the encoder gave up: its message says why
                else if (now - _t0 >= QuestRecording.MaxSeconds) { StopRecording(0); Toast($"Recording stopped at {QuestRecording.Clock(QuestRecording.MaxSeconds)}"); }
                else
                {
                    PumpAudio();
                    if (QuestRecording.Due(now, ref _next)) Capture(now);
                }
            }
            TickOverlay();
        }

        private void Capture(double now)
        {
            // The head as QuestRig placed it this frame; the main camera's look, mono, not the XR projection.
            _rec.transform.SetPositionAndRotation(_main.transform.position, _main.transform.rotation);
            _rec.clearFlags = _main.clearFlags;
            _rec.backgroundColor = _main.backgroundColor;
            _rec.cullingMask = _main.cullingMask & ~(1 << OverlayLayer);
            _rec.nearClipPlane = _main.nearClipPlane;
            _rec.farClipPlane = _main.farClipPlane;
            _rec.useOcclusionCulling = _main.useOcclusionCulling;
            _rec.stereoTargetEye = StereoTargetEyeMask.None;
            _rec.allowHDR = false;
            _rec.allowMSAA = false;
            _rec.depthTextureMode = DepthTextureMode.None;   // a depth pre-pass would double the cost
            _rec.fieldOfView = QuestRecording.VerticalFovDeg;
            _rec.aspect = QuestRecording.Width / (float)QuestRecording.Height;
            _rec.targetTexture = _rgb;
            _rec.Render();
            Graphics.Blit(_rgb, _yuv, _pack);
            long ptsUs = (long)((now - _t0) * 1e6);
            AsyncGPUReadback.Request(_yuv, 0, TextureFormat.R8, req => OnFrame(req, ptsUs));
        }

        private void OnFrame(AsyncGPUReadbackRequest req, long ptsUs)
        {
            if (!_on || req.hasError) return;
            NativeArray<byte> data = req.GetData<byte>();
            if (JavaVideo(data, ptsUs)) _sent++;
        }

        // ---- sound: the listener's mix, audio thread → ring → main thread → Java ----

        private const int AudioChannels = 2;
        private readonly object _ringLock = new();
        private short[] _ring;
        private int _ringWrite, _ringCount;
        private volatile bool _audioOn;
        private long _audioFrames;

        private void ResetAudio()
        {
            int rate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            lock (_ringLock)
            {
                if (_ring == null || _ring.Length != rate * AudioChannels * 2) _ring = new short[rate * AudioChannels * 2];   // 2 s
                _ringWrite = _ringCount = 0;
            }
            _audioFrames = 0;
        }

        // Final mix of the AudioListener on this GameObject (audio thread). Read only — the game hears it unchanged.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_audioOn || channels <= 0) return;
            int frames = data.Length / channels;
            lock (_ringLock)
            {
                if (_ring == null) return;
                for (int i = 0; i < frames; i++)
                {
                    if (_ringCount + AudioChannels > _ring.Length) return;   // the main thread is behind: drop the rest
                    float l = data[i * channels], r = channels > 1 ? data[i * channels + 1] : l;
                    _ring[_ringWrite] = ToPcm(l); _ringWrite = (_ringWrite + 1) % _ring.Length;
                    _ring[_ringWrite] = ToPcm(r); _ringWrite = (_ringWrite + 1) % _ring.Length;
                    _ringCount += AudioChannels;
                }
            }
        }

        private static short ToPcm(float v) => (short)(Mathf.Clamp(v, -1f, 1f) * 32767f);

        private short[] _pcmOut;

        private void PumpAudio()
        {
            int n;
            lock (_ringLock)
            {
                if (_ring == null || _ringCount == 0) return;
                n = _ringCount;
                if (_pcmOut == null || _pcmOut.Length < _ring.Length) _pcmOut = new short[_ring.Length];
                int read = (_ringWrite - _ringCount + _ring.Length) % _ring.Length;
                int first = Mathf.Min(n, _ring.Length - read);
                Array.Copy(_ring, read, _pcmOut, 0, first);
                if (n > first) Array.Copy(_ring, 0, _pcmOut, first, n - first);
                _ringCount = 0;
            }
            int rate = AudioSettings.outputSampleRate;
            long ptsUs = _audioFrames * 1_000_000L / Mathf.Max(1, rate);
            _audioFrames += n / AudioChannels;
            JavaAudio(_pcmOut, n, ptsUs);
        }

        // ---- the headset-only overlay: REC indicator and toast ----

        private void Toast(string s)
        {
            EnsureOverlay();
            _toast.text = s;
            _toastUntil = Time.unscaledTime + 4f;
        }

        private void PollMessage()
        {
            int seq = JavaMessageSeq();
            if (seq == _seenSeq) return;
            _seenSeq = seq;
            string m = JavaMessage();
            if (!string.IsNullOrEmpty(m)) { Toast(m); Debug.Log($"[Quest] record: {m}"); }
        }

        /// <summary>Head-locked, high in the left of the view (about 22° left and 16° up: in sight, off the
        /// horizon and the panel), on <see cref="OverlayLayer"/> so the recording never shows its own indicator.</summary>
        private void TickOverlay()
        {
            if (_indicator == null && !_on && _toastUntil <= 0f) return;
            EnsureOverlay();
            bool rec = _on;
            if (_indicator.gameObject.activeSelf != rec) _indicator.gameObject.SetActive(rec);
            if (rec)
            {
                _dot.gameObject.SetActive(Time.realtimeSinceStartup % 1f < 0.6f);   // blinks
                string s = "REC " + QuestRecording.Clock(Seconds);
                if (_recText.text != s) _recText.text = s;
            }
            bool toast = Time.unscaledTime < _toastUntil;
            if (_toast.gameObject.activeSelf != toast) _toast.gameObject.SetActive(toast);
        }

        private void EnsureOverlay()
        {
            if (_indicator != null) return;
            Shader sh = Shader.Find("FlyingGame/XrOverlay");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Font font = UiFont.Get();
            _overlayText = new Material(sh) { mainTexture = font.material.mainTexture, renderQueue = 4010 };
            Font.textureRebuilt += f => { if (f == font && _overlayText != null) _overlayText.mainTexture = font.material.mainTexture; };
            _overlayDot = new Material(sh) { mainTexture = Texture2D.whiteTexture, color = new Color(0.95f, 0.1f, 0.1f, 0.95f), renderQueue = 4009 };

            _indicator = new GameObject("VR Rec Indicator").transform;
            _indicator.SetParent(transform, false);
            _indicator.localPosition = new Vector3(-0.24f, 0.17f, 0.6f);
            _indicator.localRotation = Quaternion.LookRotation(_indicator.localPosition.normalized, Vector3.up);
            var dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "Dot";
            _dot = dot.transform;
            _dot.SetParent(_indicator, false);
            _dot.localPosition = new Vector3(-0.012f, 0f, 0f);
            _dot.localScale = new Vector3(0.011f, 0.011f, 1f);
            Overlay(dot.GetComponent<MeshRenderer>(), _overlayDot);
            _recText = Label(_indicator, new Vector3(0f, 0f, 0f), 0.016f, TextAnchor.MiddleLeft, new Color(1f, 0.25f, 0.2f, 1f));
            SetLayer(_indicator.gameObject);

            var toastRoot = new GameObject("VR Rec Toast").transform;
            toastRoot.SetParent(transform, false);
            toastRoot.localPosition = new Vector3(0f, -0.13f, 0.7f);
            toastRoot.localRotation = Quaternion.LookRotation(toastRoot.localPosition.normalized, Vector3.up);
            _toast = Label(toastRoot, Vector3.zero, 0.018f, TextAnchor.MiddleCenter, new Color(0.92f, 0.95f, 1f, 1f));
            SetLayer(toastRoot.gameObject);
            _toast.gameObject.SetActive(false);
            _indicator.gameObject.SetActive(false);
        }

        private TextMesh Label(Transform parent, Vector3 at, float charHeight, TextAnchor anchor, Color c)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var t = go.AddComponent<TextMesh>();
            t.font = UiFont.Get();
            t.fontSize = 64;
            t.characterSize = charHeight * 10f / t.fontSize;   // as QuestMenu: a line is fontSize x characterSize / 10 tall
            t.anchor = anchor;
            t.alignment = TextAlignment.Center;
            t.color = c;
            Overlay(go.GetComponent<MeshRenderer>(), _overlayText);
            return t;
        }

        private static void Overlay(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static void SetLayer(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = OverlayLayer;
        }

        // ---- Java (Plugins/Android/QuestRecorder.java) ----

#if QUEST_RECORDER
        private const string JavaClass = "com.tyrelfrisby.aeroplayground.QuestRecorder";
        private AndroidJavaClass _java;
        private IntPtr _videoId, _audioId;
        private NativeArray<byte> _frame, _pcm;
        private IntPtr _frameBuf, _pcmBuf;
        private readonly jvalue[] _args = new jvalue[3];

        private AndroidJavaClass Java
        {
            get
            {
                if (_java != null) return _java;
                _java = new AndroidJavaClass(JavaClass);
                IntPtr cls = _java.GetRawClass();
                _videoId = AndroidJNI.GetStaticMethodID(cls, "video", "(Ljava/nio/ByteBuffer;IJ)Z");
                _audioId = AndroidJNI.GetStaticMethodID(cls, "audio", "(Ljava/nio/ByteBuffer;IJ)V");
                return _java;
            }
        }

        private static IntPtr DirectBuffer(NativeArray<byte> a)
        {
            IntPtr local = AndroidJNI.NewDirectByteBuffer(a);
            IntPtr global = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
            return global;
        }

        private static string MoviesDir()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var dir = activity.Call<AndroidJavaObject>("getExternalFilesDir", "Movies");
                if (dir != null) return dir.Call<string>("getAbsolutePath");
            }
            catch (Exception e) { Debug.LogWarning($"[Quest] record: getExternalFilesDir failed ({e.Message}), using persistentDataPath"); }
            return System.IO.Path.Combine(Application.persistentDataPath, "Movies");
        }

        private bool JavaStart(string path, int sampleRate)
        {
            var l = QuestRecording.I420Layout(QuestRecording.Width, QuestRecording.Height);
            if (!_frame.IsCreated)
            {
                _frame = new NativeArray<byte>(l.total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _frameBuf = DirectBuffer(_frame);
            }
            int pcmShorts = Mathf.Max(8000, AudioSettings.outputSampleRate) * AudioChannels * 2;
            if (!_pcm.IsCreated || _pcm.Length != pcmShorts * 2)
            {
                if (_pcm.IsCreated) { AndroidJNI.DeleteGlobalRef(_pcmBuf); _pcm.Dispose(); }
                _pcm = new NativeArray<byte>(pcmShorts * 2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _pcmBuf = DirectBuffer(_pcm);
            }
            return Java.CallStatic<bool>("start", path, QuestRecording.Width, QuestRecording.Height, QuestRecording.Fps,
                QuestRecording.VideoBitrate, sampleRate, sampleRate > 0 ? AudioChannels : 0, QuestRecording.AudioBitrate);
        }

        private bool JavaVideo(NativeArray<byte> data, long ptsUs)
        {
            if (!_frame.IsCreated || data.Length != _frame.Length) return false;
            NativeArray<byte>.Copy(data, _frame);
            _ = Java;
            _args[0].l = _frameBuf; _args[1].i = _frame.Length; _args[2].j = ptsUs;
            bool ok = AndroidJNI.CallStaticBooleanMethod(_java.GetRawClass(), _videoId, _args);
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) { AndroidJNI.ExceptionClear(); return false; }
            return ok;
        }

        private void JavaAudio(short[] pcm, int count, long ptsUs)
        {
            if (!_pcm.IsCreated) return;
            NativeArray<short> s = _pcm.Reinterpret<short>(1);
            count = Mathf.Min(count, s.Length);
            NativeArray<short>.Copy(pcm, 0, s, 0, count);
            _ = Java;
            _args[0].l = _pcmBuf; _args[1].i = count * 2; _args[2].j = ptsUs;
            AndroidJNI.CallStaticVoidMethod(_java.GetRawClass(), _audioId, _args);
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) AndroidJNI.ExceptionClear();
        }

        private void JavaStop(int waitMs) => Java.CallStatic("stop", waitMs);
        private bool JavaRecording() => Java.CallStatic<bool>("isRecording");
        private bool JavaFinishing() => Java.CallStatic<bool>("isFinishing");
        private int JavaDropped() => Java.CallStatic<int>("droppedFrames");
        private int JavaMessageSeq() => Java.CallStatic<int>("messageSeq");
        private string JavaMessage() => Java.CallStatic<string>("message");

        private void ReleaseJni()
        {
            if (_frame.IsCreated) { AndroidJNI.DeleteGlobalRef(_frameBuf); _frame.Dispose(); }
            if (_pcm.IsCreated) { AndroidJNI.DeleteGlobalRef(_pcmBuf); _pcm.Dispose(); }
            _java?.Dispose();
            _java = null;
        }
#else
        private static string MoviesDir() => System.IO.Path.Combine(Application.persistentDataPath, "Movies");
        private bool JavaStart(string path, int sampleRate) { Toast("Recording works on the Quest only"); return false; }
        private bool JavaVideo(NativeArray<byte> data, long ptsUs) => false;
        private void JavaAudio(short[] pcm, int count, long ptsUs) { }
        private void JavaStop(int waitMs) { }
        private bool JavaRecording() => false;
        private bool JavaFinishing() => false;
        private int JavaDropped() => 0;
        private int JavaMessageSeq() => 0;
        private string JavaMessage() => null;
        private void ReleaseJni() { }
#endif
    }
}

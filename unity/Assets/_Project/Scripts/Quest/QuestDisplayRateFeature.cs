using System;
using System.Text;
using FlyingGame.Bridge;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace FlyingGame.Quest
{
    /// <summary>
    /// Asks the Quest for 72 or 90 Hz through the OpenXR extension XR_FB_display_refresh_rate.
    ///
    /// Unity's OpenXR package (1.16.1) enables neither the extension nor a C# call for it, and Unity's Meta
    /// OpenXR package would bring AR Foundation along to provide one. This is the whole of what is needed: request
    /// the extension, look up three functions with xrGetInstanceProcAddr, and call them with the session handle.
    ///
    /// The game asks through <see cref="XrHeadset.RefreshRateRequester"/>, which this sets at instance creation;
    /// the wanted rate is <see cref="XrHeadset.WantedRefreshHz"/> (72 by default, 90 opt-in from the VR menu,
    /// persisted). A request made before the session exists is held and made when it begins. Rates the headset
    /// does not offer are refused rather than rounded — the log says which rates it does offer.
    ///
    /// Android only (this assembly is Android + Editor); the editor needs it so OpenXR's settings can list it.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Aero Playground: Display Refresh Rate",
        Desc = "Requests 72 or 90 Hz from the runtime (XR_FB_display_refresh_rate).",
        Company = "Aero Playground",
        OpenxrExtensionStrings = "XR_FB_display_refresh_rate",
        Version = "1.0.0",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        FeatureId = FeatureId)]
#endif
    public sealed class QuestDisplayRateFeature : OpenXRFeature
    {
        public const string FeatureId = "com.tyrelfrisby.aeroplayground.displayrate";

        private static ulong _instance, _session;
        private static IntPtr _enumerate, _get, _request;
        private static float _pending;

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            _instance = xrInstance;
            _enumerate = Proc("xrEnumerateDisplayRefreshRatesFB");
            _get = Proc("xrGetDisplayRefreshRateFB");
            _request = Proc("xrRequestDisplayRefreshRateFB");
            XrHeadset.RefreshRateRequester = Request;
            Debug.Log($"[Quest] XR_FB_display_refresh_rate {(_request != IntPtr.Zero ? "available" : "NOT available")} on this runtime");
            return true;   // a missing extension must never stop the headset from starting
        }

        protected override void OnSessionCreate(ulong xrSession) => _session = xrSession;

        protected override void OnSessionBegin(ulong xrSession)
        {
            _session = xrSession;
            Debug.Log($"[Quest] display offers {string.Join(", ", Offered())} Hz; running {Current():F0} Hz");
            Request(_pending > 0f ? _pending : XrHeadset.WantedRefreshHz);
        }

        protected override void OnSessionDestroy(ulong xrSession) { if (_session == xrSession) _session = 0; }

        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            _instance = 0; _session = 0;
            _enumerate = _get = _request = IntPtr.Zero;
            if (XrHeadset.RefreshRateRequester == (Func<float, bool>)Request) XrHeadset.RefreshRateRequester = null;
        }

        /// <summary>Ask for <paramref name="hz"/>. True if the runtime accepted it (or will be asked when the session
        /// begins).</summary>
        public static bool Request(float hz)
        {
            if (_request == IntPtr.Zero) return false;
            if (_session == 0) { _pending = hz; return true; }
            if (Array.IndexOf(Offered(), hz) < 0) { Debug.LogWarning($"[Quest] {hz:F0} Hz is not offered by this display"); return false; }
            int r = CallRequest(_request, _session, hz);
            if (r < 0) { Debug.LogWarning($"[Quest] xrRequestDisplayRefreshRateFB({hz:F0}) failed: XrResult {r}"); return false; }
            _pending = 0f;
            return true;
        }

        public static float Current()
        {
            if (_get == IntPtr.Zero || _session == 0) return 0f;
            return CallGet(_get, _session, out float hz) >= 0 ? hz : 0f;
        }

        public static float[] Offered()
        {
            if (_enumerate == IntPtr.Zero || _session == 0) return Array.Empty<float>();
            if (CallEnumerate(_enumerate, _session, 0, out uint n, null) < 0 || n == 0) return Array.Empty<float>();
            var rates = new float[n];
            return CallEnumerate(_enumerate, _session, n, out _, rates) >= 0 ? rates : Array.Empty<float>();
        }

        // ---- the OpenXR calls ----

        private static unsafe IntPtr Proc(string name)
        {
            IntPtr gipa = xrGetInstanceProcAddr;
            if (gipa == IntPtr.Zero || _instance == 0) return IntPtr.Zero;
            byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
            IntPtr fn = IntPtr.Zero;
            fixed (byte* p = bytes)
            {
                var f = (delegate* unmanaged[Cdecl]<ulong, byte*, IntPtr*, int>)gipa;
                if (f(_instance, p, &fn) < 0) return IntPtr.Zero;
            }
            return fn;
        }

        private static unsafe int CallRequest(IntPtr fn, ulong session, float hz) =>
            ((delegate* unmanaged[Cdecl]<ulong, float, int>)fn)(session, hz);

        private static unsafe int CallGet(IntPtr fn, ulong session, out float hz)
        {
            float v = 0f;
            int r = ((delegate* unmanaged[Cdecl]<ulong, float*, int>)fn)(session, &v);
            hz = v;
            return r;
        }

        private static unsafe int CallEnumerate(IntPtr fn, ulong session, uint capacity, out uint count, float[] rates)
        {
            uint c = 0;
            int r;
            if (rates == null) r = ((delegate* unmanaged[Cdecl]<ulong, uint, uint*, float*, int>)fn)(session, 0, &c, null);
            else fixed (float* p = rates) r = ((delegate* unmanaged[Cdecl]<ulong, uint, uint*, float*, int>)fn)(session, capacity, &c, p);
            count = c;
            return r;
        }
    }
}

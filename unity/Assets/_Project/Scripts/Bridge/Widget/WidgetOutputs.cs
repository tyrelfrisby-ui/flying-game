using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's frame out to Glass Overlay (owner 2026-10-07: "both"): Syphon (same Mac — zero-copy, with alpha) and NDI
    /// (another machine on the network — with alpha), both named "Aero Widget", both fed from the same 1080 × 1080 RGBA
    /// texture. macOS only (the iOS game never builds these).
    /// </summary>
    public sealed class WidgetOutputs : MonoBehaviour
    {
        public AeroWidget Widget;
        public bool SyphonOn { get; private set; }
        public bool NdiOn { get; private set; }

        private void Start()
        {
#if (UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX) && KLAK_SYPHON && KLAK_NDI
            var assets = Resources.Load<WidgetAssets>("WidgetAssets");
            try
            {
                var sy = gameObject.AddComponent<Klak.Syphon.SyphonServer>();
                if (assets != null) sy.Resources = assets.Syphon as Klak.Syphon.SyphonResources;
                sy.KeepAlpha = true;
                sy.CaptureMethod = Klak.Syphon.CaptureMethod.Texture;
                sy.SourceTexture = Widget.Frame;
                sy.ServerName = AeroWidget.StreamName;
                SyphonOn = true;
            }
            catch (System.Exception e) { Debug.LogWarning("[Widget] Syphon: " + e.Message); }
            try
            {
                var go = new GameObject("WidgetNdi"); go.transform.SetParent(transform, false);
                var ndi = go.AddComponent<Klak.Ndi.NdiSender>();
                if (assets != null && assets.Ndi is Klak.Ndi.NdiResources nr) ndi.SetResources(nr);
                ndi.keepAlpha = true;
                ndi.captureMethod = Klak.Ndi.CaptureMethod.Texture;
                ndi.sourceTexture = Widget.Frame;
                ndi.ndiName = AeroWidget.StreamName;
                NdiOn = true;
            }
            catch (System.Exception e) { Debug.LogWarning("[Widget] NDI: " + e.Message); }
            Debug.Log($"[Widget] outputs: Syphon {SyphonOn}, NDI {NdiOn} as \"{AeroWidget.StreamName}\"");
#endif
        }
    }
}

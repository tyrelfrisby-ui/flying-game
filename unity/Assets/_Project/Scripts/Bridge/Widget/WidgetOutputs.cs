using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's frame out to Glass Overlay (owner 2026-10-07: "both"): Syphon (same Mac — zero-copy, with alpha) and NDI
    /// (another machine on the network — with alpha), both named "Aero Widget", both fed from the same RGBA (1080 × 1080, or 1920 × 1080 in 16:9)
    /// texture. macOS only (the iOS game never builds these).
    /// </summary>
    public sealed class WidgetOutputs : MonoBehaviour
    {
        public AeroWidget Widget;
        public bool SyphonOn { get; private set; }
        public bool NdiOn { get; private set; }

        private Component _sy; private GameObject _ndiGo;
        private void Start() => Bind();
        /// <summary>The frame changed size (square ⇄ 16:9): drop both senders and make them again on the new texture.</summary>
        public void Rebind()
        {
            if (_sy != null) Destroy(_sy);
            if (_ndiGo != null) Destroy(_ndiGo);
            _sy = null; _ndiGo = null; SyphonOn = NdiOn = false;
            StartCoroutine(BindNextFrame());
        }
        private System.Collections.IEnumerator BindNextFrame() { yield return null; Bind(); }
        private void Bind()
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
                _sy = sy; SyphonOn = true;
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
                _ndiGo = go; NdiOn = true;
            }
            catch (System.Exception e) { Debug.LogWarning("[Widget] NDI: " + e.Message); }
            Debug.Log($"[Widget] outputs: Syphon {SyphonOn}, NDI {NdiOn} as \"{AeroWidget.StreamName}\"");
#endif
        }
    }
}

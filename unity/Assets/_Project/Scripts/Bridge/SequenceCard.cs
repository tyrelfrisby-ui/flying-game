using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The aerobatic sequence cards in the cockpit's holders. A GENERIC feature: any model that brings a quad (and
    /// material) called <c>SequenceCard_Face</c> (the rear / PIC panel) or <c>FrontPanel_SequenceCard_Face</c> (the front
    /// seat; docs/cockpit-1105 contract) gets it. Letter portrait, UV 0..1, (0,0) bottom-left as the pilot sees it.
    ///
    /// Every holder shows the SAME card, the shared deck's current one (<see cref="SequenceCardLibrary"/>; Ty 2026-11-05:
    /// "Front-panel cards stay matched to the card selected in the back"): tap either card to go to the next
    /// (<see cref="CockpitTaps"/>), choose or upload in Options. Presentation only: nothing in Core or Sim knows the card.
    /// </summary>
    public sealed class SequenceCard
    {
        /// <summary>The rear / PIC card, and the front seat's.</summary>
        public const string FaceNode = "SequenceCard_Face";
        public const string FrontFaceNode = "FrontPanel_SequenceCard_Face";
        /// <summary>Letter portrait at about 150 dpi — what the holder is drawn for.</summary>
        public const int PixelsW = FlyingGame.Core.LetterCard.PixelsW, PixelsH = FlyingGame.Core.LetterCard.PixelsH;

        private readonly List<(Renderer R, Material M)> _faces = new();
        private bool _looked;
        private int _shownVersion = -1;
        private string _shownId = "";

        /// <summary>True once a model with a card holder is on screen.</summary>
        public bool HasHolder => _faces.Count > 0;
        /// <summary>The faces bound on this aeroplane (rear first), for the tap targets and the checks.</summary>
        public IReadOnlyList<(Renderer R, Material M)> Faces => _faces;

        /// <summary>Find the holders in the placed model and put the deck's card on them. Safe every frame.</summary>
        public void Bind(AirframeVisual visual, string aircraftId)
        {
            if (visual == null) return;
            Bind(visual.FindInModel);
        }

        public void Bind(System.Func<string, Transform> find)
        {
            if (!_looked && find != null)
            {
                _looked = true;
                foreach (string n in new[] { FaceNode, FrontFaceNode })
                {
                    Transform t = find(n);
                    if (t == null || !t.TryGetComponent(out Renderer r)) continue;
                    // An INSTANCE, so the card never writes into a shared GLB material. Outside play mode `.material`
                    // instantiates behind your back and warns about it, so the editor gets the shared one.
                    _faces.Add((r, Application.isPlaying ? r.material : r.sharedMaterial));
                }
                if (_faces.Count > 0) Debug.Log($"[SequenceCard] {_faces.Count} holder(s): {SequenceCardLibrary.Current.LabelAscii}");
                _shownVersion = -1;
            }
            Refresh();
        }

        /// <summary>Put the deck's current card on every holder when it has changed.</summary>
        public void Refresh()
        {
            if (_faces.Count == 0) return;
            var lib = SequenceCardLibrary.Current;
            if (SequenceCardLibrary.Version == _shownVersion && lib.Id == _shownId) return;
            _shownVersion = SequenceCardLibrary.Version; _shownId = lib.Id;
            Texture tex = SequenceCardLibrary.CurrentTexture();
            if (tex == null) return;
            foreach (var (_, m) in _faces) SetMainTexture(m, tex);
        }

        /// <summary>The hierarchy was rebuilt: look the holders up again next frame.</summary>
        public void Forget()
        {
            _looked = false;
            foreach (var (_, m) in _faces) if (m != null && Application.isPlaying) Object.Destroy(m);
            _faces.Clear();
            _shownVersion = -1; _shownId = "";
        }

        // glTFast's imported materials and the project's own shaders spell "the colour map" differently. Set
        // whichever this material actually has rather than assuming _MainTex and silently doing nothing.
        private static readonly string[] MapNames = { "_MainTex", "baseColorTexture", "_BaseMap", "_BaseColorMap" };

        internal static void SetMainTexture(Material m, Texture tex)
        {
            if (m == null) return;
            bool any = false;
            foreach (string n in MapNames) if (m.HasProperty(n)) { m.SetTexture(n, tex); any = true; }
            if (!any) Debug.LogWarning($"[SequenceCard] material '{m.name}' has no colour map to put the card on.");
        }
    }
}

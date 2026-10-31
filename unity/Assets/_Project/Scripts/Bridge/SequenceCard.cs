using System.IO;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The aerobatic sequence card in the cockpit holder. A GENERIC feature, not the Extra's: any model that
    /// brings a node and material called <c>SequenceCard_Face</c> gets it, which is the Extra today and the
    /// Pitts, the Great Lakes and the Decathlon as soon as Codex adds the same holder
    /// (docs/models/ea300-own-RIG.md, `card_slots`). The quad is letter portrait, UV 0..1, (0,0) bottom-left as
    /// the pilot sees it, and the GLB ships with the 2026 IAC Power Sportsman Known on it.
    ///
    /// What the player can do: pick a PDF off the device, choose a page, and have that page on the card instead
    /// — per aeroplane, remembered between flights, and revertible. The picking and the rasterising are native
    /// (<see cref="SequenceCardPicker"/>); everything here is presentation only, and nothing in Core or Sim
    /// knows the card exists.
    ///
    /// Stored under <c>persistentDataPath/sequence-cards/&lt;aircraftId&gt;/</c>: the rendered <c>card.png</c>,
    /// and beside it the <c>source.pdf</c> and <c>page.txt</c> it came from, so turning to another page does not
    /// need the picker again. A card that will not load falls back to the model's own default rather than
    /// leaving a blank holder.
    /// </summary>
    public sealed class SequenceCard
    {
        /// <summary>The node (and material) a model has to bring for any of this to apply.</summary>
        public const string FaceNode = "SequenceCard_Face";
        /// <summary>Letter portrait at about 150 dpi — what the holder is drawn for, and what the rasteriser
        /// letterboxes a page of any other shape onto.</summary>
        public const int PixelsW = 1275, PixelsH = 1650;

        private Renderer _face;
        private Material _mat;
        private Texture _default;
        private Texture2D _loaded;
        private string _aircraftId = "";
        private bool _looked;

        /// <summary>True once a model with a card holder is on screen.</summary>
        public bool HasHolder => _face != null;
        /// <summary>Which aeroplane's card is on the holder.</summary>
        public string AircraftId => _aircraftId;
        /// <summary>The page of the stored PDF that is on the card (1-based), or 0 when the card is the default.</summary>
        public int Page { get; private set; }

        /// <summary>Find the holder in the placed model and put the player's own card back on it. Safe every
        /// frame: it looks once per aeroplane.</summary>
        public void Bind(AirframeVisual visual, string aircraftId)
        {
            if (_looked || visual == null) return;
            _looked = true;
            _aircraftId = aircraftId ?? "";
            Transform t = visual.FindInModel(FaceNode);
            if (t == null || !t.TryGetComponent(out Renderer r)) return;
            _face = r;
            // An INSTANCE, so one aeroplane's card is not every aeroplane's. Outside play mode `.material`
            // instantiates behind your back and warns about it, so the editor gets the shared one.
            _mat = Application.isPlaying ? r.material : r.sharedMaterial;
            _default = MainTexture(_mat);
            LoadStored();
        }

        /// <summary>The hierarchy was rebuilt: look the holder up again next frame.</summary>
        public void Forget()
        {
            _looked = false;
            _face = null; _mat = null; _default = null;
            if (_loaded != null) { Object.Destroy(_loaded); _loaded = null; }
            Page = 0;
        }

        /// <summary>Where this aeroplane's card is kept.</summary>
        public string Folder => Path.Combine(Application.persistentDataPath, "sequence-cards",
                                             string.IsNullOrEmpty(_aircraftId) ? "default" : _aircraftId);
        private string CardPath => Path.Combine(Folder, "card.png");
        private string PdfPath => Path.Combine(Folder, "source.pdf");
        private string PagePath => Path.Combine(Folder, "page.txt");

        /// <summary>The PDF this aeroplane's card came from, or "" where the card is the model's own.</summary>
        public string StoredPdf => File.Exists(PdfPath) ? PdfPath : "";
        /// <summary>How many pages that PDF has, as counted when it was picked (0 where there is none).</summary>
        public int StoredPageCount { get; private set; }

        private void LoadStored()
        {
            if (!File.Exists(CardPath)) return;
            try
            {
                byte[] png = File.ReadAllBytes(CardPath);
                if (!Show(png)) return;
                // "<page>/<count>": the page count is remembered rather than re-counted, because counting
                // needs the rasteriser and the rasteriser only exists on the device.
                Page = 1; StoredPageCount = 1;
                if (File.Exists(PagePath))
                {
                    string[] bits = File.ReadAllText(PagePath).Trim().Split('/');
                    if (bits.Length > 0 && int.TryParse(bits[0], out int pg)) Page = pg;
                    if (bits.Length > 1 && int.TryParse(bits[1], out int ct)) StoredPageCount = ct;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SequenceCard] {_aircraftId}: the stored card would not load ({e.Message}); the model's own stays on.");
                ResetToDefault();
            }
        }

        /// <summary>Put a rendered page on the card and keep it for next time. The PDF it came from is kept
        /// beside it so another page is one tap away.</summary>
        public bool Use(byte[] png, string sourcePdf, int page1Based, int pageCount)
        {
            if (png == null || png.Length == 0 || !Show(png)) return false;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(CardPath, png);
                File.WriteAllText(PagePath, page1Based + "/" + pageCount);
                if (!string.IsNullOrEmpty(sourcePdf) && File.Exists(sourcePdf) && Path.GetFullPath(sourcePdf) != Path.GetFullPath(PdfPath))
                    File.Copy(sourcePdf, PdfPath, true);
                Page = page1Based;
                StoredPageCount = pageCount;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SequenceCard] {_aircraftId}: the card is on the holder but could not be saved ({e.Message}).");
                return true;      // it IS on the card; only the remembering failed
            }
        }

        /// <summary>Turn to another page of the PDF already stored. 1-based; does nothing without a stored PDF.</summary>
        public bool TurnTo(int page1Based)
        {
            string pdf = StoredPdf;
            if (pdf.Length == 0) return false;
            int count = Mathf.Max(1, StoredPageCount);
            int page = Mathf.Clamp(page1Based, 1, count);
            byte[] png = SequenceCardPicker.RenderPage(pdf, page - 1, PixelsW, PixelsH);
            return png != null && Use(png, pdf, page, count);
        }

        /// <summary>Back to the card the model shipped with, and forget the player's.</summary>
        public void ResetToDefault()
        {
            if (_mat != null && _default != null) SetMainTexture(_mat, _default);
            if (_loaded != null) { Object.Destroy(_loaded); _loaded = null; }
            Page = 0; StoredPageCount = 0;
            try
            {
                if (File.Exists(CardPath)) File.Delete(CardPath);
                if (File.Exists(PagePath)) File.Delete(PagePath);
                if (File.Exists(PdfPath)) File.Delete(PdfPath);
            }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] could not clear the stored card: {e.Message}"); }
        }

        private bool Show(byte[] png)
        {
            if (_mat == null) return false;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "SequenceCard" };
            if (!tex.LoadImage(png)) { Object.Destroy(tex); return false; }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(true, false);                       // mip-mapped: the card is read from a metre away
            SetMainTexture(_mat, tex);
            if (_loaded != null) Object.Destroy(_loaded);
            _loaded = tex;
            return true;
        }

        // glTFast's imported materials and the project's own shaders spell "the colour map" differently. Set
        // whichever this material actually has rather than assuming _MainTex and silently doing nothing.
        private static readonly string[] MapNames = { "_MainTex", "baseColorTexture", "_BaseMap", "_BaseColorMap" };

        private static Texture MainTexture(Material m)
        {
            if (m == null) return null;
            foreach (string n in MapNames) if (m.HasProperty(n)) return m.GetTexture(n);
            return null;
        }

        private static void SetMainTexture(Material m, Texture tex)
        {
            if (m == null) return;
            bool any = false;
            foreach (string n in MapNames) if (m.HasProperty(n)) { m.SetTexture(n, tex); any = true; }
            if (!any) Debug.LogWarning($"[SequenceCard] material '{m.name}' has no colour map to put the card on.");
        }
    }
}

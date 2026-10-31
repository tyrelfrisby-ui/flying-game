using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Getting a PDF off the device and a page of it onto the sequence card. The picker is iOS's own
    /// (UIDocumentPickerViewController, PDFs only) and the rasteriser is PDFKit —
    /// <c>Assets/Plugins/iOS/SequenceCard.mm</c>. The result is POLLED, like the clip encoder's: ask
    /// <see cref="Poll"/> once a frame while <see cref="Busy"/>.
    ///
    /// Off iOS there is no picker and no PDFKit, so the editor and the Mac build take a FILE PATH instead
    /// (<see cref="EditorPath"/>, or the SEQ_CARD env var): a PNG or JPEG of a card, which is enough to see the
    /// holder, the UV orientation and the material working without a device. A PDF cannot be rasterised there,
    /// and saying so is better than pretending.
    /// </summary>
    public static class SequenceCardPicker
    {
        public enum Result { Idle, Picking, Picked, Cancelled, Failed }

#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLib = "__Internal";
        [DllImport(NativeLib)] private static extern void SC_Pick();
        [DllImport(NativeLib)] private static extern int SC_State();
        [DllImport(NativeLib)] private static extern void SC_Clear();
        [DllImport(NativeLib)] private static extern int SC_PageCount();
        [DllImport(NativeLib)] private static extern System.IntPtr SC_PdfPath();
        [DllImport(NativeLib)] private static extern int SC_Render(string pdfPath, int pageIndex, string outPath, int pxW, int pxH);
        private static readonly bool Native = true;
#else
        private static void SC_Pick() { }
        private static int SC_State() => 0;
        private static void SC_Clear() { }
        private static int SC_PageCount() => 0;
        private static System.IntPtr SC_PdfPath() => System.IntPtr.Zero;
        private static int SC_Render(string pdfPath, int pageIndex, string outPath, int pxW, int pxH) => 0;
        private static readonly bool Native = false;
#endif

        /// <summary>Desktop / editor stand-in for the picker: a path to a PNG or JPEG of a card. Set it in
        /// OPTIONS, or pass SEQ_CARD=/path/to/card.png in the environment.</summary>
        public static string EditorPath = "";

        /// <summary>True while the document picker is up.</summary>
        public static bool Busy { get; private set; }
        /// <summary>Why the last attempt did nothing, for the panel to show. "" when all was well.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>True where a PDF can actually be picked and rasterised (iOS device only).</summary>
        public static bool Available => Native;

        /// <summary>Open the document picker. On anything but an iOS device this immediately takes
        /// <see cref="EditorPath"/> instead.</summary>
        public static void Pick(SequenceCard card)
        {
            LastError = "";
            if (card == null || !card.HasHolder) { LastError = "this aeroplane has no card holder"; return; }
            if (!Native) { UseLocalFile(card); return; }
            Busy = true;
            SC_Clear();
            SC_Pick();
        }

        /// <summary>Once a frame while <see cref="Busy"/>: finishes the job when the player has chosen.</summary>
        public static Result Poll(SequenceCard card)
        {
            if (!Busy || !Native) return Busy ? Result.Picking : Result.Idle;
            var state = (Result)SC_State();
            if (state == Result.Picking) return state;
            Busy = false;
            SC_Clear();
            if (state != Result.Picked)
            {
                if (state == Result.Failed) LastError = "that file could not be read as a PDF";
                return state;
            }
            string pdf = Marshal.PtrToStringAnsi(SC_PdfPath()) ?? "";
            int pages = SC_PageCount();
            if (pdf.Length == 0 || pages <= 0) { LastError = "that PDF has no pages"; return Result.Failed; }
            byte[] png = RenderPage(pdf, 0, SequenceCard.PixelsW, SequenceCard.PixelsH);
            if (png == null) { LastError = "that page could not be drawn"; return Result.Failed; }
            if (!card.Use(png, pdf, 1, pages)) { LastError = "that page would not go on the card"; return Result.Failed; }
            return Result.Picked;
        }

        /// <summary>Rasterise one page (0-based) letterboxed onto a white canvas, and hand back the PNG.
        /// null where there is no rasteriser or the page would not draw.</summary>
        public static byte[] RenderPage(string pdfPath, int pageIndex, int pxW, int pxH)
        {
            if (!Native || string.IsNullOrEmpty(pdfPath)) return null;
            string tmp = Path.Combine(Application.temporaryCachePath, "sequence-card-page.png");
            if (SC_Render(pdfPath, pageIndex, tmp, pxW, pxH) == 0) return null;
            try { return File.ReadAllBytes(tmp); }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] rendered page unreadable: {e.Message}"); return null; }
        }

        /// <summary>The editor / desktop path: an image of a card straight off the disk.</summary>
        private static void UseLocalFile(SequenceCard card)
        {
            string path = EditorPath;
            if (string.IsNullOrEmpty(path)) path = System.Environment.GetEnvironmentVariable("SEQ_CARD") ?? "";
#if UNITY_EDITOR
            // In the editor, ask for the file the way the editor asks for files. This is the desktop stub: it
            // is how the holder, the UV orientation and the material get tested without a device.
            if (string.IsNullOrEmpty(path))
                path = UnityEditor.EditorUtility.OpenFilePanel("Sequence card image", "", "png,jpg,jpeg");
#endif
            if (string.IsNullOrEmpty(path)) { LastError = "no PDF picker here — set a PNG path in OPTIONS or SEQ_CARD"; return; }
            if (!File.Exists(path)) { LastError = "no file at " + path; return; }
            if (path.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase))
            {
                LastError = "PDFs need the device (PDFKit); give a PNG here";
                return;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (!card.Use(bytes, "", 1, 1)) LastError = "that image would not go on the card";
            }
            catch (System.Exception e) { LastError = e.Message; }
        }
    }
}

using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Getting a card off the device and into the shared deck (<see cref="SequenceCardLibrary"/>; Ty 2026-11-05: "Users
    /// can upload their own cards and remove cards in Settings (freestyles, airshow cards). Any 8.5x11 PDF displays as a
    /// half-size IAC card"). Each PDF page (up to <see cref="MaxPagesPerPdf"/>) becomes a card of its own.
    ///
    ///   iOS / iPadOS   the system document picker (PDFs) and PDFKit — Assets/Plugins/iOS/SequenceCard.mm. POLLED.
    ///   Mac            the system "choose file" dialog (osascript) for a PDF, PNG or JPEG; a PDF is rasterised by the
    ///                  system's own <c>sips</c> — its FIRST PAGE only (sips draws no other). Runs off the main thread.
    ///   Quest          no picker: files copied into the library's inbox are imported from the headset menu, PDFs drawn
    ///                  by Android's own PdfRenderer (every page).
    ///   Editor         the editor's file dialog, then as the Mac.
    /// </summary>
    public static class SequenceCardPicker
    {
        public enum Result { Idle, Picking, Picked, Cancelled, Failed }

        public const int MaxPagesPerPdf = 6;

#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLib = "__Internal";
        [DllImport(NativeLib)] private static extern void SC_Pick();
        [DllImport(NativeLib)] private static extern int SC_State();
        [DllImport(NativeLib)] private static extern void SC_Clear();
        [DllImport(NativeLib)] private static extern int SC_PageCount();
        [DllImport(NativeLib)] private static extern System.IntPtr SC_PdfPath();
        [DllImport(NativeLib)] private static extern int SC_Render(string pdfPath, int pageIndex, string outPath, int pxW, int pxH);
        private static readonly bool IosNative = true;
#else
        private static void SC_Pick() { }
        private static int SC_State() => 0;
        private static void SC_Clear() { }
        private static int SC_PageCount() => 0;
        private static System.IntPtr SC_PdfPath() => System.IntPtr.Zero;
        private static int SC_Render(string pdfPath, int pageIndex, string outPath, int pxW, int pxH) => 0;
        private static readonly bool IosNative = false;
#endif
#if (UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX)
        private static readonly bool MacTools = true;
#else
        private static readonly bool MacTools = false;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly bool AndroidPdf = true;
#else
        private static readonly bool AndroidPdf = false;
#endif

        /// <summary>True while a picker is up (or the Mac's dialog / rasteriser is working).</summary>
        public static bool Busy { get; private set; }
        /// <summary>What the last attempt did, for the Options panel: "" when nothing to say.</summary>
        public static string LastMessage { get; private set; } = "";
        public static string LastError => LastMessage;

        /// <summary>True where the pilot can pick a file himself (iOS, Mac, editor). The Quest uses the inbox.</summary>
        public static bool Available => IosNative || MacTools || Application.isEditor;
        /// <summary>A line that says how cards get in on this device.</summary>
        public static string HowTo => IosNative ? "Add: pick a PDF (each page becomes a card)"
                                    : MacTools ? "Add: pick a PDF (first page), PNG or JPEG"
                                    : "Headset: copy PDFs/PNGs to the card inbox, then Import";

        // Mac: the dialog runs on a worker thread; the result is picked up on the main thread.
        private static volatile string _macPath;
        private static volatile bool _macDone;

        public static void Pick()
        {
            LastMessage = "";
            if (Busy) return;
            if (IosNative) { Busy = true; SC_Clear(); SC_Pick(); return; }
#if UNITY_EDITOR
            string p = UnityEditor.EditorUtility.OpenFilePanel("Add a sequence card (PDF, PNG or JPEG)", "", "pdf,png,jpg,jpeg");
            if (!string.IsNullOrEmpty(p)) ImportFile(p); else LastMessage = "no file chosen";
            return;
#else
            if (MacTools)
            {
                Busy = true; _macDone = false; _macPath = null;
                new System.Threading.Thread(() =>
                {
                    _macPath = Run("/usr/bin/osascript", "-e", "POSIX path of (choose file with prompt \"Add a sequence card (PDF, PNG or JPEG)\" of type {\"com.adobe.pdf\", \"public.png\", \"public.jpeg\"})");
                    _macDone = true;
                }) { IsBackground = true }.Start();
                return;
            }
            LastMessage = HowTo;
#endif
        }

        /// <summary>Once a frame while <see cref="Busy"/>: finishes the job when the pilot has chosen.</summary>
        public static Result Poll()
        {
            if (!Busy) return Result.Idle;
            if (IosNative)
            {
                var state = (Result)SC_State();
                if (state == Result.Picking) return state;
                Busy = false;
                SC_Clear();
                if (state != Result.Picked) { if (state == Result.Failed) LastMessage = "that file could not be read as a PDF"; return state; }
                string pdf = Marshal.PtrToStringAnsi(SC_PdfPath()) ?? "";
                int pages = SC_PageCount();
                if (pdf.Length == 0 || pages <= 0) { LastMessage = "that PDF has no pages"; return Result.Failed; }
                return ImportPdf(pdf, pages) > 0 ? Result.Picked : Result.Failed;
            }
            if (MacTools)
            {
                if (!_macDone) return Result.Picking;
                Busy = false;
                string p = (_macPath ?? "").Trim();
                if (p.Length == 0) { LastMessage = "no file chosen"; return Result.Cancelled; }
                return ImportFile(p) ? Result.Picked : Result.Failed;
            }
            Busy = false;
            return Result.Idle;
        }

        /// <summary>A file on disk: a PDF page by page, an image as it is.</summary>
        public static bool ImportFile(string path)
        {
            if (!File.Exists(path)) { LastMessage = "no file at " + path; return false; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf") return ImportPdf(path, PdfPageCount(path)) > 0;
            string id = SequenceCardLibrary.Import(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path), out string why);
            LastMessage = id != null ? "added: " + Path.GetFileNameWithoutExtension(path) : why;
            return id != null;
        }

        private static int ImportPdf(string pdf, int pages)
        {
            string title = Path.GetFileNameWithoutExtension(pdf);
            int added = 0;
            string why = "";
            for (int p = 0; p < pages && p < MaxPagesPerPdf; p++)
            {
                byte[] png = RenderPage(pdf, p, SequenceCard.PixelsW, SequenceCard.PixelsH);
                if (png == null) { why = $"page {p + 1} could not be drawn"; continue; }
                if (SequenceCardLibrary.Import(png, pages > 1 ? $"{title} p{p + 1}" : title, out why) != null) added++;
            }
            LastMessage = added > 0 ? $"added {added} card(s) from {title}" + (pages > MaxPagesPerPdf ? $" (first {MaxPagesPerPdf} pages)" : "") : (why.Length > 0 ? why : "nothing added");
            return added;
        }

        /// <summary>Pages in a PDF: Android counts them; the Mac's sips draws only the first, so 1 there.</summary>
        public static int PdfPageCount(string pdf)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var file = new AndroidJavaObject("java.io.File", pdf);
                using var pfdClass = new AndroidJavaClass("android.os.ParcelFileDescriptor");
                using var pfd = pfdClass.CallStatic<AndroidJavaObject>("open", file, 0x10000000);   // MODE_READ_ONLY
                using var renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
                int n = renderer.Call<int>("getPageCount");
                renderer.Call("close");
                return n;
            }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] PdfRenderer: {e.Message}"); return 0; }
#else
            return File.Exists(pdf) ? 1 : 0;
#endif
        }

        /// <summary>Rasterise one page (0-based) letterboxed onto a white letter canvas, as a PNG. null where it
        /// would not draw.</summary>
        public static byte[] RenderPage(string pdfPath, int pageIndex, int pxW, int pxH)
        {
            if (string.IsNullOrEmpty(pdfPath)) return null;
            string tmp = Path.Combine(Application.temporaryCachePath, "sequence-card-page.png");
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                if (IosNative)
                {
                    if (SC_Render(pdfPath, pageIndex, tmp, pxW, pxH) == 0) return null;
                    return File.ReadAllBytes(tmp);
                }
                if (AndroidPdf) return AndroidRender(pdfPath, pageIndex, pxW, pxH);
                if (MacTools)
                {
                    if (pageIndex != 0) return null;
                    // sips draws page 1 at the PDF's own size; LetterFit then puts it on the card.
                    Run("/usr/bin/sips", "-s", "format", "png", "-Z", pxH.ToString(), pdfPath, "--out", tmp);
                    return File.Exists(tmp) ? SequenceCardLibrary.LetterFit(File.ReadAllBytes(tmp), out _) : null;
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] page {pageIndex + 1} of {pdfPath}: {e.Message}"); }
            return null;
        }

        private static byte[] AndroidRender(string pdf, int page, int pxW, int pxH)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var file = new AndroidJavaObject("java.io.File", pdf);
            using var pfdClass = new AndroidJavaClass("android.os.ParcelFileDescriptor");
            using var pfd = pfdClass.CallStatic<AndroidJavaObject>("open", file, 0x10000000);
            using var renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
            using var pg = renderer.Call<AndroidJavaObject>("openPage", page);
            int w = pg.Call<int>("getWidth"), h = pg.Call<int>("getHeight");
            var (x, y, fw, fh, _) = FlyingGame.Core.LetterCard.Fit(w, h, rotateLandscape: false);
            using var cfg = new AndroidJavaClass("android.graphics.Bitmap$Config").GetStatic<AndroidJavaObject>("ARGB_8888");
            using var bmpClass = new AndroidJavaClass("android.graphics.Bitmap");
            using var bmp = bmpClass.CallStatic<AndroidJavaObject>("createBitmap", pxW, pxH, cfg);
            bmp.Call("eraseColor", unchecked((int)0xFFFFFFFF));                  // paper
            using var dst = new AndroidJavaObject("android.graphics.Rect", x, y, x + fw, y + fh);
            using var identity = new AndroidJavaObject("android.graphics.Matrix");   // (a null Matrix would not bind: JNI needs its type)
            pg.Call("render", bmp, dst, identity, 1);                            // RENDER_MODE_FOR_DISPLAY
            pg.Call("close");
            renderer.Call("close");
            using var baos = new AndroidJavaObject("java.io.ByteArrayOutputStream");
            using var fmt = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat").GetStatic<AndroidJavaObject>("PNG");
            bmp.Call<bool>("compress", fmt, 100, baos);
            byte[] png = baos.Call<byte[]>("toByteArray");
            bmp.Call("recycle");
            return png;
#else
            return null;
#endif
        }

        /// <summary>Run a system tool and hand back what it printed (Mac). "" on any failure.</summary>
        private static string Run(string exe, params string[] args)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (string a in args) sb.Append(sb.Length > 0 ? " " : "").Append('"').Append(a.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                var psi = new System.Diagnostics.ProcessStartInfo(exe, sb.ToString()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = System.Diagnostics.Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
                return p.ExitCode == 0 ? o : "";
            }
            catch (System.Exception e) { Debug.LogWarning($"[SequenceCard] {exe}: {e.Message}"); return ""; }
        }
    }
}

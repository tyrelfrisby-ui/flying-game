// The sequence card in the cockpit holder (Ty, 2026-10-30): let the pilot put THEIR OWN sequence on the card,
// from a PDF on the device or in iCloud Drive, and rasterise the page they choose onto the card's quad.
//
// Two jobs, both of which have to be native: the document picker (UIDocumentPickerViewController, PDFs only,
// asCopy so the app gets its own copy and needs no file-coordination dance) and the rasteriser (PDFKit). The
// page is drawn LETTERBOXED onto a white 8.5 x 11 canvas and never stretched -- an A4 or a landscape page keeps
// its own proportions on the card, because a squashed sequence is unreadable and a wrong one is worse.
//
// Results are POLLED from C# (SC_State) rather than pushed with UnitySendMessage, the same way ClipEncoder
// reports its saves: no named GameObject to keep alive, and nothing happens on Unity's thread that Unity did
// not ask for.
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <PDFKit/PDFKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>

extern "C" UIViewController *UnityGetGLViewController();

enum { SC_IDLE = 0, SC_PICKING = 1, SC_PICKED = 2, SC_CANCELLED = 3, SC_FAILED = 4 };

static volatile int scState = SC_IDLE;
static NSString *scPdfPath = @"";
static int scPageCount = 0;

@interface SCPickerDelegate : NSObject <UIDocumentPickerDelegate>
@end

@implementation SCPickerDelegate

- (void)documentPicker:(UIDocumentPickerViewController *)controller
    didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls
{
    if (urls.count == 0) { scState = SC_CANCELLED; return; }
    NSURL *src = urls[0];
    // asCopy:YES hands us a copy in a temporary directory; move it somewhere we own, because the picker's
    // copy is cleaned up behind us.
    NSString *dir = [NSTemporaryDirectory() stringByAppendingPathComponent:@"sequence-card"];
    [[NSFileManager defaultManager] createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:nil];
    NSString *dest = [dir stringByAppendingPathComponent:@"picked.pdf"];
    [[NSFileManager defaultManager] removeItemAtPath:dest error:nil];
    NSError *err = nil;
    if (![[NSFileManager defaultManager] copyItemAtURL:src toURL:[NSURL fileURLWithPath:dest] error:&err])
    {
        NSLog(@"[SequenceCard] could not copy the picked PDF: %@", err);
        scState = SC_FAILED;
        return;
    }
    PDFDocument *doc = [[PDFDocument alloc] initWithURL:[NSURL fileURLWithPath:dest]];
    if (doc == nil || doc.pageCount == 0) { NSLog(@"[SequenceCard] not a readable PDF"); scState = SC_FAILED; return; }
    scPdfPath = dest;
    scPageCount = (int)doc.pageCount;
    scState = SC_PICKED;
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController *)controller
{
    scState = SC_CANCELLED;
}

@end

static SCPickerDelegate *scDelegate = nil;

extern "C" void SC_Pick(void)
{
    if (scState == SC_PICKING) return;
    scState = SC_PICKING;
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController *root = UnityGetGLViewController();
        if (root == nil) { scState = SC_FAILED; return; }
        if (scDelegate == nil) scDelegate = [[SCPickerDelegate alloc] init];
        UIDocumentPickerViewController *picker =
            [[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[UTTypePDF] asCopy:YES];
        picker.delegate = scDelegate;
        picker.allowsMultipleSelection = NO;
        picker.modalPresentationStyle = UIModalPresentationFormSheet;
        [root presentViewController:picker animated:YES completion:nil];
    });
}

extern "C" int SC_State(void) { return scState; }
extern "C" void SC_Clear(void) { scState = SC_IDLE; }
extern "C" int SC_PageCount(void) { return scPageCount; }

extern "C" const char *SC_PdfPath(void)
{
    const char *s = [scPdfPath UTF8String];
    char *copy = (char *)malloc(strlen(s) + 1);     // Unity frees it
    strcpy(copy, s);
    return copy;
}

/// Rasterise one page onto a white pxW x pxH canvas, aspect preserved, and write it as a PNG.
/// Returns 1 on success.
extern "C" int SC_Render(const char *pdfPath, int pageIndex, const char *outPath, int pxW, int pxH)
{
    if (pdfPath == NULL || outPath == NULL || pxW <= 0 || pxH <= 0) return 0;
    @autoreleasepool
    {
        NSURL *url = [NSURL fileURLWithPath:[NSString stringWithUTF8String:pdfPath]];
        PDFDocument *doc = [[PDFDocument alloc] initWithURL:url];
        if (doc == nil || doc.pageCount == 0) return 0;
        NSUInteger idx = (NSUInteger)MAX(0, MIN(pageIndex, (int)doc.pageCount - 1));
        PDFPage *page = [doc pageAtIndex:idx];
        if (page == nil) return 0;

        CGRect box = [page boundsForBox:kPDFDisplayBoxCropBox];
        if (box.size.width <= 0 || box.size.height <= 0) return 0;

        UIGraphicsBeginImageContextWithOptions(CGSizeMake(pxW, pxH), YES, 1.0);
        CGContextRef ctx = UIGraphicsGetCurrentContext();
        CGContextSetRGBFillColor(ctx, 1, 1, 1, 1);                       // paper
        CGContextFillRect(ctx, CGRectMake(0, 0, pxW, pxH));

        // Letterbox: the biggest scale that fits, centred. Never stretched.
        CGFloat scale = MIN(pxW / box.size.width, pxH / box.size.height);
        CGFloat w = box.size.width * scale, h = box.size.height * scale;
        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx, (pxW - w) * 0.5, (pxH - h) * 0.5 + h);
        CGContextScaleCTM(ctx, scale, -scale);                           // PDF space is y-up, UIKit's is y-down
        CGContextTranslateCTM(ctx, -box.origin.x, -box.origin.y);
        [page drawWithBox:kPDFDisplayBoxCropBox toContext:ctx];
        CGContextRestoreGState(ctx);

        UIImage *img = UIGraphicsGetImageFromCurrentImageContext();
        UIGraphicsEndImageContext();
        if (img == nil) return 0;
        NSData *png = UIImagePNGRepresentation(img);
        if (png == nil) return 0;
        NSString *dest = [NSString stringWithUTF8String:outPath];
        [[NSFileManager defaultManager] createDirectoryAtPath:[dest stringByDeletingLastPathComponent]
                                  withIntermediateDirectories:YES attributes:nil error:nil];
        return [png writeToFile:dest atomically:YES] ? 1 : 0;
    }
}

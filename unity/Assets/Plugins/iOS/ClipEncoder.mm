// Quick clips (owner 2026-10-01): a rolling "dash-cam" of the game's clip camera (the 3D view only, plus the
// instruments when the user wants them) and the game sound. Frames + audio stream into 10 s H.264/AAC segment files
// in tmp; the last few minutes are kept. CE_SaveClip(N) stitches the newest N seconds (15 s .. 3 min) into one
// .mp4 and saves it to Photos. CE_StartRecording / CE_StopRecording save everything between the two taps.
// All writer work happens on one serial queue; results come back as a message the C# side polls.
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <QuartzCore/QuartzCore.h>
#import <Photos/Photos.h>
#include <os/lock.h>

@interface CESegment : NSObject
@property(nonatomic, strong) NSURL *url;
@property(nonatomic) double start;   // media clock (s) of the first video frame, -1 until then
@property(nonatomic) double end;     // media clock of the last video frame
@property(nonatomic) BOOL finished;  // file closed and playable
@property(nonatomic) BOOL failed;
@end
@implementation CESegment
@end

static dispatch_queue_t ceQ;
static AVAssetWriter *ceWriter;
static AVAssetWriterInput *ceVideo, *ceAudio;
static AVAssetWriterInputPixelBufferAdaptor *ceAdaptor;
static CESegment *ceCur;
static NSMutableArray<CESegment *> *ceSegs;
static int ceW = 0, ceH = 0, ceFps = 30, ceRate = 48000, ceCh = 2, ceBitrate = 8000000;
static double ceKeepSec = 200, ceSegSec = 10;
static double ceLastVideoPts = -1, ceAudioNext = -1, ceRecStart = -1;
static CMAudioFormatDescriptionRef ceAudioFmt = NULL;
static volatile int ceBusy = 0, ceQueued = 0;
static int ceSegCounter = 0;
static BOOL ceKeepCopy = NO;
static NSString *ceMsg = @"";
static int ceSeq = 0;

// Media clock: host time minus time spent paused (landing page), so clips have no frozen gaps.
static os_unfair_lock ceClockLock = OS_UNFAIR_LOCK_INIT;
static double cePausedTotal = 0, cePauseStart = -1;

static void CESetMessage(NSString *m)
{
    @synchronized ([CESegment class]) { ceMsg = [m copy]; ceSeq++; }
}

static void CEEnsureQueue(void)
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        ceQ = dispatch_queue_create("aero.clipencoder", DISPATCH_QUEUE_SERIAL);
        ceSegs = [NSMutableArray array];
        // Leftovers from an earlier run.
        NSString *dir = NSTemporaryDirectory();
        for (NSString *f in [[NSFileManager defaultManager] contentsOfDirectoryAtPath:dir error:nil])
            if ([f hasPrefix:@"ceseg-"] || [f hasPrefix:@"ceclip-"]) [[NSFileManager defaultManager] removeItemAtPath:[dir stringByAppendingPathComponent:f] error:nil];
    });
}

static void CEDeleteSegment(CESegment *s) { [[NSFileManager defaultManager] removeItemAtURL:s.url error:nil]; }

// ---- segments (ceQ only) ------------------------------------------------------------------------

static BOOL CEStartSegment(void)
{
    NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:[NSString stringWithFormat:@"ceseg-%d.mp4", ++ceSegCounter]];
    NSURL *url = [NSURL fileURLWithPath:path];
    [[NSFileManager defaultManager] removeItemAtURL:url error:nil];
    NSError *err = nil;
    AVAssetWriter *w = [AVAssetWriter assetWriterWithURL:url fileType:AVFileTypeMPEG4 error:&err];
    if (!w) { CESetMessage([NSString stringWithFormat:@"Clip recorder: %@", err.localizedDescription]); return NO; }
    NSDictionary *vs = @{
        AVVideoCodecKey: AVVideoCodecTypeH264,
        AVVideoWidthKey: @(ceW), AVVideoHeightKey: @(ceH),
        AVVideoCompressionPropertiesKey: @{
            AVVideoAverageBitRateKey: @(ceBitrate),
            AVVideoExpectedSourceFrameRateKey: @(ceFps),
            AVVideoMaxKeyFrameIntervalKey: @(ceFps),
            AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
        },
    };
    AVAssetWriterInput *v = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo outputSettings:vs];
    v.expectsMediaDataInRealTime = YES;
    AVAssetWriterInputPixelBufferAdaptor *ad = [AVAssetWriterInputPixelBufferAdaptor assetWriterInputPixelBufferAdaptorWithAssetWriterInput:v
        sourcePixelBufferAttributes:@{ (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA), (id)kCVPixelBufferWidthKey: @(ceW), (id)kCVPixelBufferHeightKey: @(ceH) }];
    NSDictionary *as = @{ AVFormatIDKey: @(kAudioFormatMPEG4AAC), AVSampleRateKey: @(ceRate), AVNumberOfChannelsKey: @(MIN(ceCh, 2)) };   // encoder's own bitrate: a fixed 160 kbps is invalid at Unity's 24 kHz iOS output
    AVAssetWriterInput *a = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeAudio outputSettings:as];
    a.expectsMediaDataInRealTime = YES;
    if (![w canAddInput:v] || ![w canAddInput:a]) { CESetMessage(@"Clip recorder: cannot add inputs"); return NO; }
    [w addInput:v];
    [w addInput:a];
    if (![w startWriting]) { CESetMessage([NSString stringWithFormat:@"Clip recorder: %@", w.error.localizedDescription]); return NO; }
    ceWriter = w; ceVideo = v; ceAudio = a; ceAdaptor = ad;
    ceCur = [CESegment new];
    ceCur.url = url; ceCur.start = -1; ceCur.end = -1;
    return YES;
}

static void CEPrune(void)
{
    if (ceBusy) return;   // an export may be reading old segments
    double newest = ceLastVideoPts;
    while (ceSegs.count > 0) {
        CESegment *s = ceSegs.firstObject;
        BOOL needed = s.end >= newest - ceKeepSec || (ceRecStart >= 0 && s.end >= ceRecStart);
        if (needed && !s.failed) break;
        CEDeleteSegment(s);
        [ceSegs removeObjectAtIndex:0];
    }
}

/// Close the open segment (if it has frames); it becomes part of the history once the file is finished.
static void CERotate(void)
{
    if (!ceWriter) return;
    AVAssetWriter *w = ceWriter;
    CESegment *seg = ceCur;
    ceWriter = nil; ceVideo = nil; ceAudio = nil; ceAdaptor = nil; ceCur = nil;
    ceAudioNext = -1;
    if (seg.start < 0) { [w cancelWriting]; CEDeleteSegment(seg); return; }
    [ceSegs addObject:seg];
    [w.inputs enumerateObjectsUsingBlock:^(AVAssetWriterInput *i, NSUInteger idx, BOOL *stop) { [i markAsFinished]; }];
    [w finishWritingWithCompletionHandler:^{
        dispatch_async(ceQ, ^{
            seg.finished = YES;
            seg.failed = w.status != AVAssetWriterStatusCompleted;
        });
    }];
    CEPrune();
}

// ---- export ------------------------------------------------------------------------------------

static void CESaveToPhotos(NSURL *url, NSString *what)
{
    if (ceKeepCopy) {
        NSString *docs = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES).firstObject;
        NSString *dir = [docs stringByAppendingPathComponent:@"Clips"];
        [[NSFileManager defaultManager] createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:nil];
        [[NSFileManager defaultManager] copyItemAtURL:url toURL:[NSURL fileURLWithPath:[dir stringByAppendingPathComponent:url.lastPathComponent]] error:nil];
        // Self-test: no Photos prompt to wait on.
        CESetMessage([NSString stringWithFormat:@"%@ saved to Documents/Clips", what]);
        [[NSFileManager defaultManager] removeItemAtURL:url error:nil];
        dispatch_async(ceQ, ^{ ceBusy = 0; CEPrune(); });
        return;
    }
    void (^done)(NSString *) = ^(NSString *m) {
        CESetMessage(m);
        [[NSFileManager defaultManager] removeItemAtURL:url error:nil];
        dispatch_async(ceQ, ^{ ceBusy = 0; CEPrune(); });
    };
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus st) {
        if (st != PHAuthorizationStatusAuthorized && st != PHAuthorizationStatusLimited) { done(@"Photos access is off - allow it in Settings > Aero Playground"); return; }
        [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
            [PHAssetChangeRequest creationRequestForAssetFromVideoAtFileURL:url];
        } completionHandler:^(BOOL ok, NSError *error) {
            done(ok ? [NSString stringWithFormat:@"%@ saved to Photos", what] : [NSString stringWithFormat:@"Could not save %@: %@", what, error.localizedDescription]);
        }];
    }];
}

static void CEExport(AVAsset *asset, NSString *preset, NSString *what, BOOL allowFallback)
{
    NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:[NSString stringWithFormat:@"ceclip-%lld.mp4", (long long)([[NSDate date] timeIntervalSince1970] * 1000)]];
    NSURL *out = [NSURL fileURLWithPath:path];
    AVAssetExportSession *ex = [[AVAssetExportSession alloc] initWithAsset:asset presetName:preset];
    ex.outputURL = out;
    ex.outputFileType = AVFileTypeMPEG4;
    ex.shouldOptimizeForNetworkUse = YES;
    [ex exportAsynchronouslyWithCompletionHandler:^{
        if (ex.status == AVAssetExportSessionStatusCompleted) { CESaveToPhotos(out, what); return; }
        [[NSFileManager defaultManager] removeItemAtURL:out error:nil];
        if (allowFallback) { CEExport(asset, AVAssetExportPresetHighestQuality, what, NO); return; }
        CESetMessage([NSString stringWithFormat:@"%@ failed: %@", what, ex.error.localizedDescription]);
        dispatch_async(ceQ, ^{ ceBusy = 0; });
    }];
}

/// Stitch [from, to] (media clock) out of the finished segments; waits (up to 5 s) for the files to close.
static void CEBuildAndExport(double from, double to, NSString *what, int tries)
{
    BOOL pending = NO;
    for (CESegment *s in ceSegs) if (!s.finished && s.end >= from && s.start <= to) pending = YES;
    if (pending && tries < 50) {
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)), ceQ, ^{ CEBuildAndExport(from, to, what, tries + 1); });
        return;
    }
    AVMutableComposition *comp = [AVMutableComposition composition];
    CMTime cursor = kCMTimeZero;
    for (CESegment *s in ceSegs) {
        if (!s.finished || s.failed || s.end < from || s.start > to) continue;
        AVURLAsset *a = [AVURLAsset URLAssetWithURL:s.url options:@{ AVURLAssetPreferPreciseDurationAndTimingKey: @YES }];
        CMTime dur = a.duration;
        double off = MAX(0.0, from - s.start);
        CMTime st = CMTimeMakeWithSeconds(off, 600);
        if (CMTimeCompare(st, dur) >= 0) continue;
        CMTimeRange r = CMTimeRangeFromTimeToTime(st, dur);
        NSError *e = nil;
        if ([comp insertTimeRange:r ofAsset:a atTime:cursor error:&e]) cursor = CMTimeAdd(cursor, r.duration);
    }
    if (CMTimeGetSeconds(cursor) < 0.5) { CESetMessage(@"Nothing recorded yet"); ceBusy = 0; return; }
    CESetMessage([NSString stringWithFormat:@"Saving %@ (%.0f s)...", what.lowercaseString, CMTimeGetSeconds(cursor)]);
    CEExport(comp, AVAssetExportPresetPassthrough, what, YES);
}

// ---- C API ---------------------------------------------------------------------------------------

extern "C" {

double CE_Now(void)
{
    os_unfair_lock_lock(&ceClockLock);
    double t = CACurrentMediaTime() - cePausedTotal;
    if (cePauseStart >= 0) t = cePauseStart - cePausedTotal;
    os_unfair_lock_unlock(&ceClockLock);
    return t;
}

void CE_SetPaused(int paused)
{
    os_unfair_lock_lock(&ceClockLock);
    double now = CACurrentMediaTime();
    if (paused && cePauseStart < 0) cePauseStart = now;
    else if (!paused && cePauseStart >= 0) { cePausedTotal += now - cePauseStart; cePauseStart = -1; }
    os_unfair_lock_unlock(&ceClockLock);
}

void CE_Configure(int width, int height, int fps, int sampleRate, int channels, int bitrate, double keepSec)
{
    CEEnsureQueue();
    dispatch_async(ceQ, ^{
        if (width == ceW && height == ceH && sampleRate == ceRate && channels == ceCh) return;
        // A new frame size (rotation): segments of the old size can't be stitched to the new one — start over.
        if (ceWriter) { [ceWriter cancelWriting]; CEDeleteSegment(ceCur); ceWriter = nil; ceVideo = nil; ceAudio = nil; ceAdaptor = nil; ceCur = nil; }
        if (!ceBusy) { for (CESegment *s in ceSegs) CEDeleteSegment(s); [ceSegs removeAllObjects]; }
        ceW = width; ceH = height; ceFps = fps; ceRate = sampleRate; ceCh = channels; ceBitrate = bitrate; ceKeepSec = keepSec;
        ceLastVideoPts = -1; ceAudioNext = -1;
        if (ceAudioFmt) { CFRelease(ceAudioFmt); ceAudioFmt = NULL; }
        AudioStreamBasicDescription asbd = {0};
        asbd.mSampleRate = sampleRate;
        asbd.mFormatID = kAudioFormatLinearPCM;
        asbd.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
        asbd.mBytesPerPacket = 4 * channels;
        asbd.mFramesPerPacket = 1;
        asbd.mBytesPerFrame = 4 * channels;
        asbd.mChannelsPerFrame = channels;
        asbd.mBitsPerChannel = 32;
        CMAudioFormatDescriptionCreate(kCFAllocatorDefault, &asbd, 0, NULL, 0, NULL, NULL, &ceAudioFmt);
    });
}

void CE_SetKeepCopy(int keep) { ceKeepCopy = keep != 0; }

/// One BGRA frame (bottom row first, as Unity reads a render texture back), stamped with CE_Now() at render time.
void CE_AppendVideo(const unsigned char *bgra, int width, int height, double pts)
{
    CEEnsureQueue();
    if (ceQueued > 4) return;   // the encoder is behind: drop rather than pile up
    NSData *copy = [NSData dataWithBytes:bgra length:(size_t)width * height * 4];
    __sync_fetch_and_add(&ceQueued, 1);
    dispatch_async(ceQ, ^{
        __sync_fetch_and_sub(&ceQueued, 1);
        if (width != ceW || height != ceH || ceW == 0) return;
        if (ceWriter && ceWriter.status == AVAssetWriterStatusFailed) {
            NSLog(@"[ClipEncoder] writer failed %dx%d rate %d ch %d: %@", ceW, ceH, ceRate, ceCh, ceWriter.error);
            CESetMessage([NSString stringWithFormat:@"Clip recorder restarted: %@", ceWriter.error.localizedDescription]);
            CEDeleteSegment(ceCur); ceWriter = nil; ceCur = nil;
        }
        if (!ceWriter && !CEStartSegment()) return;
        if (pts <= ceLastVideoPts) return;   // AVAssetWriter aborts on a repeated / backwards time stamp
        if (ceCur.start < 0) { [ceWriter startSessionAtSourceTime:CMTimeMakeWithSeconds(pts, 1000000)]; ceCur.start = pts; }
        if (!ceVideo.readyForMoreMediaData) return;
        CVPixelBufferRef pb = NULL;
        if (!ceAdaptor.pixelBufferPool || CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, ceAdaptor.pixelBufferPool, &pb) != kCVReturnSuccess) return;
        CVPixelBufferLockBaseAddress(pb, 0);
        unsigned char *dst = (unsigned char *)CVPixelBufferGetBaseAddress(pb);
        size_t dstRow = CVPixelBufferGetBytesPerRow(pb), srcRow = (size_t)width * 4;
        const unsigned char *src = (const unsigned char *)copy.bytes;
        for (int y = 0; y < height; y++) memcpy(dst + (size_t)y * dstRow, src + (size_t)(height - 1 - y) * srcRow, srcRow);
        CVPixelBufferUnlockBaseAddress(pb, 0);
        @try {
            if ([ceAdaptor appendPixelBuffer:pb withPresentationTime:CMTimeMakeWithSeconds(pts, 1000000)]) { ceLastVideoPts = pts; ceCur.end = pts; }
        } @catch (NSException *e) { NSLog(@"[ClipEncoder] video append: %@", e); }
        CVPixelBufferRelease(pb);
        if (ceCur.end - ceCur.start >= ceSegSec) CERotate();
    });
}

/// Interleaved float PCM from the audio listener's final mix; pts = media clock of the first sample.
void CE_AppendAudio(const float *pcm, int frames, int channels, double pts)
{
    CEEnsureQueue();
    if (frames <= 0 || ceQueued > 8) return;
    NSData *copy = [NSData dataWithBytes:pcm length:(size_t)frames * channels * 4];
    dispatch_async(ceQ, ^{
        if (!ceWriter || !ceCur || ceCur.start < 0 || !ceAudioFmt || channels != ceCh) return;
        double t = pts;
        if (ceAudioNext >= 0 && fabs(t - ceAudioNext) < 0.04) t = ceAudioNext;   // keep the stream contiguous
        if (t < ceCur.start || (ceAudioNext >= 0 && t < ceAudioNext - 1e-6)) return;
        if (!ceAudio.readyForMoreMediaData) return;
        size_t len = copy.length;
        CMBlockBufferRef bb = NULL;
        if (CMBlockBufferCreateWithMemoryBlock(kCFAllocatorDefault, NULL, len, kCFAllocatorDefault, NULL, 0, len, kCMBlockBufferAssureMemoryNowFlag, &bb) != kCMBlockBufferNoErr) return;
        CMBlockBufferReplaceDataBytes(copy.bytes, bb, 0, len);
        CMSampleBufferRef sb = NULL;
        OSStatus st = CMAudioSampleBufferCreateReadyWithPacketDescriptions(kCFAllocatorDefault, bb, ceAudioFmt, frames,
            CMTimeMake((int64_t)llround(t * ceRate), ceRate), NULL, &sb);
        CFRelease(bb);
        if (st != noErr || !sb) return;
        @try {
            if ([ceAudio appendSampleBuffer:sb]) ceAudioNext = t + (double)frames / ceRate;
        } @catch (NSException *e) { NSLog(@"[ClipEncoder] audio append: %@", e); }
        CFRelease(sb);
    });
}

void CE_SaveClip(double seconds)
{
    CEEnsureQueue();
    dispatch_async(ceQ, ^{
        if (ceBusy) { CESetMessage(@"Still saving the last clip..."); return; }
        if (ceLastVideoPts < 0) { CESetMessage(@"Nothing recorded yet"); return; }
        ceBusy = 1;
        double to = ceLastVideoPts, from = to - seconds;
        CERotate();
        CEBuildAndExport(from, to, @"Clip", 0);
    });
}

void CE_StartRecording(void)
{
    CEEnsureQueue();
    dispatch_async(ceQ, ^{
        if (ceRecStart >= 0) return;
        ceRecStart = ceLastVideoPts >= 0 ? ceLastVideoPts : CE_Now();
        CESetMessage(@"Recording");
    });
}

void CE_StopRecording(void)
{
    CEEnsureQueue();
    dispatch_async(ceQ, ^{
        if (ceRecStart < 0) return;
        double from = ceRecStart;
        ceRecStart = -1;
        if (ceBusy) { CESetMessage(@"Still saving the last clip - recording discarded"); return; }
        ceBusy = 1;
        double to = ceLastVideoPts;
        CERotate();
        CEBuildAndExport(from, to, @"Recording", 0);
    });
}

int CE_IsRecording(void) { return ceRecStart >= 0 ? 1 : 0; }
int CE_IsBusy(void) { return ceBusy; }
int CE_MessageSeq(void) { @synchronized ([CESegment class]) { return ceSeq; } }
const char *CE_Message(void) { @synchronized ([CESegment class]) { return strdup(ceMsg.UTF8String ?: ""); } }   // IL2CPP frees it

}

// Quick clips (owner 2026-10-01): ReplayKit keeps a rolling buffer of the last 15 s of the game (picture + game sound,
// no microphone); QC_ExportClip saves the end of it to Photos. QC_StartRecording / QC_StopRecording record an
// open-ended clip (the rolling buffer pauses while it runs). Results come back as a message the C# side polls.
#import <Foundation/Foundation.h>
#import <ReplayKit/ReplayKit.h>
#import <Photos/Photos.h>

static NSString *qcMessage = @"";
static int qcSeq = 0;
static volatile bool qcBuffering = false;
static volatile bool qcRecording = false;
static volatile bool qcBusy = false;

static void QCSetMessage(NSString *m)
{
    @synchronized ([RPScreenRecorder class]) { qcMessage = [m copy]; qcSeq++; }
}

static NSURL *QCTempURL(NSString *prefix)
{
    NSString *name = [NSString stringWithFormat:@"%@-%lld.mp4", prefix, (long long)([[NSDate date] timeIntervalSince1970] * 1000)];
    return [NSURL fileURLWithPath:[NSTemporaryDirectory() stringByAppendingPathComponent:name]];
}

static void QCSaveToPhotos(NSURL *url, NSString *what)
{
    void (^save)(void) = ^{
        [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
            [PHAssetChangeRequest creationRequestForAssetFromVideoAtFileURL:url];
        } completionHandler:^(BOOL ok, NSError *error) {
            QCSetMessage(ok ? [NSString stringWithFormat:@"%@ saved to Photos", what]
                            : [NSString stringWithFormat:@"Could not save %@: %@", what, error.localizedDescription]);
            [[NSFileManager defaultManager] removeItemAtURL:url error:nil];
            qcBusy = false;
        }];
    };
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus st) {
        if (st == PHAuthorizationStatusAuthorized || st == PHAuthorizationStatusLimited) save();
        else { QCSetMessage(@"Photos access is off - allow it in Settings > Aero Playground"); qcBusy = false; }
    }];
}

extern "C" {

void QC_StartBuffering(void)
{
    RPScreenRecorder *r = [RPScreenRecorder sharedRecorder];
    if (qcBuffering || qcRecording || !r.available) return;
    if (@available(iOS 15.0, *)) {
        r.microphoneEnabled = NO;
        qcBuffering = true;
        [r startClipBufferingWithCompletionHandler:^(NSError *error) {
            if (error) { qcBuffering = false; QCSetMessage([NSString stringWithFormat:@"Clips unavailable: %@", error.localizedDescription]); }
        }];
    }
}

int QC_IsBuffering(void) { return qcBuffering ? 1 : 0; }
int QC_IsRecording(void) { return qcRecording ? 1 : 0; }
int QC_IsBusy(void) { return qcBusy ? 1 : 0; }

void QC_ExportClip(double seconds)
{
    if (@available(iOS 15.0, *)) {
        if (!qcBuffering) { QCSetMessage(@"Clip buffer not running yet"); QC_StartBuffering(); return; }
        if (qcBusy) return;
        qcBusy = true;
        QCSetMessage(@"Saving clip...");
        NSURL *url = QCTempURL(@"clip");
        [[RPScreenRecorder sharedRecorder] exportClipToURL:url duration:seconds completionHandler:^(NSError *error) {
            if (error) { QCSetMessage([NSString stringWithFormat:@"Clip failed: %@", error.localizedDescription]); qcBusy = false; return; }
            QCSaveToPhotos(url, @"Clip");
        }];
    } else {
        QCSetMessage(@"Clips need iOS 15");
    }
}

void QC_StartRecording(void)
{
    RPScreenRecorder *r = [RPScreenRecorder sharedRecorder];
    if (qcRecording || qcBusy || !r.available) return;
    qcBusy = true;
    void (^start)(void) = ^{
        r.microphoneEnabled = NO;
        [r startRecordingWithHandler:^(NSError *error) {
            qcBusy = false;
            if (error) { QCSetMessage([NSString stringWithFormat:@"Recording failed: %@", error.localizedDescription]); QC_StartBuffering(); return; }
            qcRecording = true;
            QCSetMessage(@"Recording");
        }];
    };
    if (@available(iOS 15.0, *)) {
        if (qcBuffering) {
            [r stopClipBufferingWithCompletionHandler:^(NSError *error) { qcBuffering = false; dispatch_async(dispatch_get_main_queue(), start); }];
            return;
        }
    }
    start();
}

void QC_StopRecording(void)
{
    if (!qcRecording) return;
    qcBusy = true;
    NSURL *url = QCTempURL(@"recording");
    [[RPScreenRecorder sharedRecorder] stopRecordingWithOutputURL:url completionHandler:^(NSError *error) {
        qcRecording = false;
        if (error) { QCSetMessage([NSString stringWithFormat:@"Recording failed: %@", error.localizedDescription]); qcBusy = false; }
        else QCSaveToPhotos(url, @"Recording");
        dispatch_async(dispatch_get_main_queue(), ^{ QC_StartBuffering(); });
    }];
}

int QC_MessageSeq(void) { @synchronized ([RPScreenRecorder class]) { return qcSeq; } }

const char *QC_Message(void)
{
    @synchronized ([RPScreenRecorder class]) { return strdup(qcMessage.UTF8String ?: ""); }   // IL2CPP frees the returned string
}

}

// Volume-up as the gun trigger (owner 2026-09-10). iOS gives no game access to the volume buttons, so this watches
// the system output volume: each volume-UP step is a trigger press (holding the button repeats the steps), and the
// volume is immediately put back to where it was through a hidden MPVolumeView slider, so the game's loudness never
// changes and the button keeps registering. A hidden MPVolumeView in the window also suppresses the system volume HUD.
// Volume-down still works as volume-down. Armed only while the guns are hot (VolumeFire.Arm/Disarm from C#).
#import <AVFoundation/AVFoundation.h>
#import <MediaPlayer/MediaPlayer.h>
#import <UIKit/UIKit.h>

static MPVolumeView *g_volumeView = nil;
static UISlider *g_slider = nil;
static float g_baseline = 0.5f;
static double g_lastPress = 0;      // CACurrentMediaTime of the last volume-up step
static int g_pressCount = 0;
static bool g_armed = false;
static bool g_resetting = false;
static id g_observer = nil;

static double NowSec(void) { return [[NSDate date] timeIntervalSince1970]; }

static UISlider *FindSlider(UIView *v)
{
    for (UIView *sub in v.subviews)
    {
        if ([sub isKindOfClass:[UISlider class]]) return (UISlider *)sub;
        UISlider *deep = FindSlider(sub);
        if (deep) return deep;
    }
    return nil;
}

static void SetSystemVolume(float v)
{
    if (g_slider == nil) return;
    g_resetting = true;
    [g_slider setValue:v animated:NO];
    [g_slider sendActionsForControlEvents:UIControlEventTouchUpInside];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.15 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ g_resetting = false; });
}

@interface FGVolumeWatcher : NSObject
@end
@implementation FGVolumeWatcher
- (void)observeValueForKeyPath:(NSString *)keyPath ofObject:(id)object change:(NSDictionary *)change context:(void *)context
{
    if (!g_armed || ![keyPath isEqualToString:@"outputVolume"]) return;
    float v = [AVAudioSession sharedInstance].outputVolume;
    if (g_resetting) return;
    if (v > g_baseline + 0.005f)
    {
        g_lastPress = NowSec();
        g_pressCount++;
        SetSystemVolume(g_baseline);   // put it back: loudness unchanged, next press registers again
    }
    else if (v < g_baseline - 0.005f)
    {
        g_baseline = v;                // a genuine volume-down: accept the new level
        if (g_baseline < 0.15f) { g_baseline = 0.15f; SetSystemVolume(g_baseline); }   // keep headroom so UP still registers
    }
}
@end

static FGVolumeWatcher *g_watcher = nil;

extern "C" void FlyingGame_VolumeFireArm(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIWindow *win = nil;
        for (UIWindow *w in [UIApplication sharedApplication].windows) if (w.isKeyWindow) { win = w; break; }
        if (win == nil) win = [UIApplication sharedApplication].windows.firstObject;
        if (g_volumeView == nil && win != nil)
        {
            g_volumeView = [[MPVolumeView alloc] initWithFrame:CGRectMake(-3000, -3000, 100, 30)];
            g_volumeView.showsRouteButton = NO;
            g_volumeView.alpha = 0.01f;
            [win addSubview:g_volumeView];
            g_slider = FindSlider(g_volumeView);
        }
        AVAudioSession *session = [AVAudioSession sharedInstance];
        [session setActive:YES error:nil];
        float v = session.outputVolume;
        g_baseline = v > 0.85f ? 0.7f : (v < 0.15f ? 0.3f : v);
        if (fabsf(v - g_baseline) > 0.005f) SetSystemVolume(g_baseline);
        if (g_watcher == nil)
        {
            g_watcher = [[FGVolumeWatcher alloc] init];
            [session addObserver:g_watcher forKeyPath:@"outputVolume" options:NSKeyValueObservingOptionNew context:NULL];
        }
        g_armed = true;
    });
}

extern "C" void FlyingGame_VolumeFireDisarm(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        g_armed = false;
        if (g_volumeView != nil) { [g_volumeView removeFromSuperview]; g_volumeView = nil; g_slider = nil; }   // system HUD and volume buttons back to normal
    });
}

/// Seconds since the last volume-up step (large when none).
extern "C" double FlyingGame_VolumeFireSinceLastPress(void)
{
    return g_lastPress > 0 ? NowSec() - g_lastPress : 1e9;
}

extern "C" int FlyingGame_VolumeFirePressCount(void) { return g_pressCount; }

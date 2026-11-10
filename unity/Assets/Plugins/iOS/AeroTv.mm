// TV MODE (Ty 2026-11-04: "when a second screen connects the game renders full size on the TV and the phone becomes a
// full-screen touch controller"). Bridge/TvOutput.cs does the work through Unity's Display API; this file is only the
// two things Unity's trampoline does not do. docs/TV-COMPANION-PLAN.md §1.6.
//
// 1. A SCENE FOR THE TV. The app runs the UIScene lifecycle, and iOS only creates a UIWindowScene on an external screen
//    for an app that declares the external-display role (BuildScript.OnPostProcessBuild adds it to Info.plist, naming
//    the class below). Unity's DisplayManager attaches the TV's window to "the connected scene whose screen is the TV"
//    when Display.Activate() runs; with no such scene the window belongs to nothing and the TV never shows it.
//    The delegate does NOTHING on purpose: Unity's own UnityScene forwards every scene's lifecycle to the app
//    (sceneDidBecomeActive -> applicationDidBecomeActive ...), so using it here would pause and resume the game each
//    time the TV came and went. Until a window is shown on it, iOS keeps mirroring the phone onto the TV.
//
// 2. GIVING THE TV BACK. Unity has no Display.Deactivate. Options > TV SCREEN > Mirror while a TV is connected hides
//    the windows on the TV's scene, and iOS mirrors again; Game on TV shows them again.
#import <UIKit/UIKit.h>

extern "C" UIViewController *UnityGetGLViewController();

@interface AeroExternalDisplayScene : UIResponder <UIWindowSceneDelegate>
@property (strong, nonatomic) UIWindow *window;
@end

/// TELL UNITY, IN THE LANGUAGE ITS DISPLAY MANAGER STILL SPEAKS.
///
/// Ty 2026-11-10, HDMI adapter in, TV SCREEN on "Game on TV": "It is pixel for pixel" — the system mirror, with the
/// game never reaching the TV. The scene above connects now (UIApplicationSupportsMultipleScenes), but Unity never
/// learned of it: Classes/Unity/DisplayManager.mm registers external screens from UIScreenDidConnectNotification and
/// [UIScreen screens], and BOTH are deprecated as of iOS 16, because an external display arrives as a SCENE
/// connection now. So Display.displays stayed at 1, TvOutput.ExternalDisplay stayed false, and TvModeLogic never
/// left Mirror however the option was set.
///
/// Unity's observer is perfectly good — it is only never fired. So post the notification it is listening for, with
/// the scene's own screen as the object: DisplayManager's screenDidConnect: then registers that screen and calls
/// updateDisplayListCacheInUnity exactly as it would have for a cable on iOS 15.
static void AnnounceScreen(UIScene *scene, BOOL connected)
{
    if (![scene isKindOfClass:[UIWindowScene class]]) return;
    UIScreen *screen = ((UIWindowScene *)scene).screen;
    if (screen == nil || screen == UIScreen.mainScreen) return;
    NSString *name = connected ? UIScreenDidConnectNotification : UIScreenDidDisconnectNotification;
    [[NSNotificationCenter defaultCenter] postNotificationName:name object:screen];
    NSLog(@"[TV] announced %@ to Unity: %@ %.0f×%.0f", connected ? @"connect" : @"disconnect", screen,
          screen.bounds.size.width, screen.bounds.size.height);
}

@implementation AeroExternalDisplayScene
- (void)scene:(UIScene *)scene willConnectToSession:(UISceneSession *)session options:(UISceneConnectionOptions *)connectionOptions
{
    NSLog(@"[TV] external display scene connected (%@)", session.role);
    AnnounceScreen(scene, YES);
}
- (void)sceneDidDisconnect:(UIScene *)scene
{
    NSLog(@"[TV] external display scene disconnected");
    AnnounceScreen(scene, NO);
}
@end

/// Announce every external scene that is ALREADY connected. The scene can connect before Unity's DisplayManager has
/// registered its observer (prepareForFirstScene), which is exactly what happens when the TV is plugged in before
/// the app is launched — so the managed side calls this once it is up, and the two orders both end up announced.
extern "C" int AeroTv_AnnounceExternalScenes()
{
    __block int n = 0;
    void (^announce)(void) = ^{
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
        {
            NSString *role = scene.session.role;
            BOOL external = [role isEqualToString:@"UIWindowSceneSessionRoleExternalDisplay"];
            if (@available(iOS 16.0, *)) external = external || [role isEqualToString:UIWindowSceneSessionRoleExternalDisplayNonInteractive];
            if (!external) continue;
            AnnounceScreen(scene, YES);
            n++;
        }
    };
    if ([NSThread isMainThread]) announce(); else dispatch_sync(dispatch_get_main_queue(), announce);
    return n;
}

/// REGISTERING THE TV AS A SCENE ACCESSORY — the iOS 27 way, and on iOS 27 the only way.
///
/// Ty 2026-11-10, HDMI adapter in, TV showing the system mirror, the OPTIONS diagnostic reading
/// "iOS scenes 0 · screens 1 · Unity displays 1": iOS had made no external-display scene AND reported no second
/// UIScreen, while the TV was plainly receiving video. Declaring the role in Info.plist and
/// UIApplicationSupportsMultipleScenes = true — both verified in the shipped app — is the iOS 16-26 path and it
/// is no longer enough: an app built against the iOS 27 SDK is given an external-display scene only if it
/// REGISTERS one, as a scene accessory on a view controller. Until it does, UIScreen.screens holds only the
/// phone's own screen, which is exactly what the diagnostic printed.
///
/// A scene accessory is the system's own framing of this: the app declares what content it can supply, and the
/// system decides when and where to present it (UISceneAccessory.h). So the app asks once, at startup, and keeps
/// the registration alive; iOS connects the scene if and when a display appears. The Info.plist role stays for
/// iOS 16-26, where it is still the whole mechanism.
#if defined(__IPHONE_OS_VERSION_MAX_ALLOWED) && __IPHONE_OS_VERSION_MAX_ALLOWED >= 270000
static id gTvAccessoryRegistration;   // held: unregistering dismisses the accessory
#endif

/// 1 registered (or already was), 0 the registration failed, -1 this iOS does not need it (16-26: Info.plist).
extern "C" int AeroTv_RegisterExternalAccessory()
{
#if defined(__IPHONE_OS_VERSION_MAX_ALLOWED) && __IPHONE_OS_VERSION_MAX_ALLOWED >= 270000
    if (@available(iOS 27.0, *))
    {
        __block int result = 0;
        void (^reg)(void) = ^{
            if (gTvAccessoryRegistration != nil) { result = 1; return; }
            UIViewController *vc = UnityGetGLViewController();
            if (vc == nil) { NSLog(@"[TV] no Unity view controller yet; not registering the accessory"); return; }
            UISceneConfiguration *cfg =
                [UISceneConfiguration configurationWithName:@"External Display"
                                                sessionRole:UIWindowSceneSessionRoleExternalDisplayNonInteractive];
            cfg.sceneClass = UIWindowScene.class;
            cfg.delegateClass = AeroExternalDisplayScene.class;
            gTvAccessoryRegistration =
                [vc registerSceneAccessory:[UISceneAccessory externalNonInteractiveSceneAccessoryWithConfiguration:cfg]];
            result = gTvAccessoryRegistration != nil ? 1 : 0;
            NSLog(@"[TV] scene accessory registered: %d", result);
        };
        if ([NSThread isMainThread]) reg(); else dispatch_sync(dispatch_get_main_queue(), reg);
        return result;
    }
#endif
    return -1;
}

/// How many screens iOS itself has, the deprecated way Unity counts them — for the diagnostic line in OPTIONS, so a
/// TV that does not arrive can be told apart from a TV Unity cannot see.
extern "C" int AeroTv_UIScreenCount()
{
    __block int n = 0;
    void (^count)(void) = ^{ n = (int)UIScreen.screens.count; };
    if ([NSThread isMainThread]) count(); else dispatch_sync(dispatch_get_main_queue(), count);
    return n;
}

static BOOL IsExternal(UIScene *scene)
{
    NSString *role = scene.session.role;
    if (role == nil) return NO;
    if ([role isEqualToString:@"UIWindowSceneSessionRoleExternalDisplay"]) return YES;
    if (@available(iOS 16.0, *)) return [role isEqualToString:UIWindowSceneSessionRoleExternalDisplayNonInteractive];
    return NO;
}

/// Show (1) or hide (0) every window on the external-display scenes: hidden, iOS mirrors the phone onto the TV again.
extern "C" void AeroTv_SetExternalShown(int shown)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
        {
            if (!IsExternal(scene) || ![scene isKindOfClass:[UIWindowScene class]]) continue;
            for (UIWindow *w in ((UIWindowScene *)scene).windows) w.hidden = shown == 0;
        }
        NSLog(@"[TV] external windows %@", shown ? @"shown" : @"hidden (mirroring)");
    });
}

/// How many external-display scenes iOS has given us (0 means no TV, or the Info.plist role is missing): for the log.
extern "C" int AeroTv_ExternalSceneCount()
{
    __block int n = 0;
    void (^count)(void) = ^{
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) if (IsExternal(scene)) n++;
    };
    if ([NSThread isMainThread]) count(); else dispatch_sync(dispatch_get_main_queue(), count);
    return n;
}

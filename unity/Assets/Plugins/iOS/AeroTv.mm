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

@interface AeroExternalDisplayScene : UIResponder <UIWindowSceneDelegate>
@property (strong, nonatomic) UIWindow *window;
@end

@implementation AeroExternalDisplayScene
- (void)scene:(UIScene *)scene willConnectToSession:(UISceneSession *)session options:(UISceneConnectionOptions *)connectionOptions
{
    NSLog(@"[TV] external display scene connected (%@)", session.role);
}
- (void)sceneDidDisconnect:(UIScene *)scene
{
    NSLog(@"[TV] external display scene disconnected");
}
@end

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

// Put the app's audio session in the Playback category so the procedural flight audio plays even with the
// iPhone's ring/silent switch on silent (Unity's default Ambient category obeys the switch and mutes a game).
#import <AVFoundation/AVFoundation.h>

extern "C" void FlyingGame_SetPlaybackAudioSession(void)
{
    NSError *err = nil;
    AVAudioSession *session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayback withOptions:AVAudioSessionCategoryOptionMixWithOthers error:&err];
    [session setActive:YES error:&err];
}

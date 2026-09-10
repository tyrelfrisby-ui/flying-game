// The pilot's voice: iOS text-to-speech (AVSpeechSynthesizer) for the one-liners after a parachute landing.
// Called from FlyingGame.Bridge.PilotVoice via [DllImport("__Internal")]. Mixes with the game audio under the
// Playback session set in AudioSessionPlayback.mm.
#import <AVFoundation/AVFoundation.h>

static AVSpeechSynthesizer *g_synth = nil;

extern "C" void FlyingGame_Speak(const char *utf8, float rate, float pitch)
{
    if (utf8 == NULL) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (g_synth == nil) g_synth = [[AVSpeechSynthesizer alloc] init];
        if ([g_synth isSpeaking]) [g_synth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
        NSString *text = [NSString stringWithUTF8String:utf8];
        AVSpeechUtterance *u = [AVSpeechUtterance speechUtteranceWithString:text];
        u.voice = [AVSpeechSynthesisVoice voiceWithLanguage:@"en-US"];
        u.rate = rate;                 // 0.5 = AVSpeechUtteranceDefaultSpeechRate
        u.pitchMultiplier = pitch;
        u.volume = 1.0f;
        [g_synth speakUtterance:u];
    });
}

extern "C" void FlyingGame_StopSpeech(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (g_synth != nil && [g_synth isSpeaking]) [g_synth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    });
}
